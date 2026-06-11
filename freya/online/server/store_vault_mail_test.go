// SPDX-License-Identifier: MIT
// Freya Online -- integration tests for the vault-transfer and send-mail paths.
//
// Gated on FREYA_TEST_DB (see itest_helpers_test.go). These exercise the real
// SQL behind TransferVaultItem and SendMail against the two-DB schema: the
// ownership checks, the offline-guard, the dup-safe serializable move (item is
// never in two places at once), and every validation error.

package main

import (
	"context"
	"sync"
	"testing"
)

// vaultSlotItem reports the item id stored in a given vault slot, or -1 if empty.
func vaultSlotItem(t *testing.T, st *Store, ctx context.Context, avID int64, slot int) int {
	t.Helper()
	var id *int
	err := st.user.QueryRow(ctx,
		`SELECT item_id FROM avatar_vault_items WHERE avatar_id=$1 AND inventory_slot=$2`,
		avID, slot).Scan(&id)
	if err != nil {
		return -1
	}
	if id == nil {
		return -1
	}
	return *id
}

// vaultRows counts every vault row an avatar holds (any item, any slot).
func vaultRows(t *testing.T, st *Store, ctx context.Context, avID int64) int {
	t.Helper()
	var n int
	if err := st.user.QueryRow(ctx,
		`SELECT count(*) FROM avatar_vault_items WHERE avatar_id=$1`, avID).Scan(&n); err != nil {
		t.Fatalf("vaultRows: %v", err)
	}
	return n
}

// --- vault transfer --------------------------------------------------------

// A normal transfer moves the whole stack from one owned character's vault into
// the other's first free slot, leaving the source slot empty.
func TestIT_VaultTransfer_MovesItem(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 20, 2, 1000)
	src, dst := acct.avatars[0], acct.avatars[1]

	itemID, _ := pickTradableItem(t, st, ctx, true)
	const stack = 7
	seedVaultItem(t, st, ctx, src.id, 3, itemID, stack, nil)

	if err := st.TransferVaultItem(ctx, acct.id, src.name, dst.name, 3, itemID); err != nil {
		t.Fatalf("TransferVaultItem: %v", err)
	}

	if got := vaultSlotItem(t, st, ctx, src.id, 3); got != -1 {
		t.Errorf("source slot 3 still holds item %d; want empty", got)
	}
	if vaultRows(t, st, ctx, src.id) != 0 {
		t.Errorf("source vault not empty after transfer")
	}
	if n := vaultCount(t, st, ctx, dst.id, itemID); n != 1 {
		t.Errorf("dest holds %d rows of the item, want 1", n)
	}
	// The stack must survive the move (slot 0 is the dest's first free slot).
	var ts *int
	if err := st.user.QueryRow(ctx,
		`SELECT trade_stack FROM avatar_vault_items WHERE avatar_id=$1 AND item_id=$2`,
		dst.id, itemID).Scan(&ts); err != nil {
		t.Fatalf("read dest stack: %v", err)
	}
	if ts == nil || *ts != stack {
		t.Errorf("dest stack = %v, want %d", ts, stack)
	}
}

// Transferring a character's slot to itself is rejected before any DB work.
func TestIT_VaultTransfer_SameCharacterRejected(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 21, 2, 1000)
	src := acct.avatars[0]
	itemID, _ := pickTradableItem(t, st, ctx, true)
	seedVaultItem(t, st, ctx, src.id, 0, itemID, 1, nil)

	if err := st.TransferVaultItem(ctx, acct.id, src.name, src.name, 0, itemID); err != errSameCharacter {
		t.Fatalf("same-char transfer = %v, want errSameCharacter", err)
	}
	if vaultRows(t, st, ctx, src.id) != 1 {
		t.Errorf("source vault mutated on a rejected same-char transfer")
	}
}

// A character that the account does not own cannot be a transfer endpoint.
func TestIT_VaultTransfer_NotOwnedRejected(t *testing.T) {
	st, ctx := testStore(t)
	owner := seedAccount(t, st, ctx, 22, 1, 1000)
	other := seedAccount(t, st, ctx, 23, 1, 1000)
	itemID, _ := pickTradableItem(t, st, ctx, true)
	seedVaultItem(t, st, ctx, owner.avatars[0].id, 0, itemID, 1, nil)

	// Destination belongs to a different account.
	err := st.TransferVaultItem(ctx, owner.id, owner.avatars[0].name, other.avatars[0].name, 0, itemID)
	if err != errCharacterNotFound {
		t.Fatalf("transfer to foreign char = %v, want errCharacterNotFound", err)
	}
	if vaultRows(t, st, ctx, owner.avatars[0].id) != 1 {
		t.Errorf("source vault mutated on a rejected cross-account transfer")
	}
	if vaultRows(t, st, ctx, other.avatars[0].id) != 0 {
		t.Errorf("foreign character received the item")
	}
}

// The offline-guard blocks a transfer while either endpoint is online, and the
// item stays put.
func TestIT_VaultTransfer_BlocksOnlineCharacter(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 24, 2, 1000)
	src, dst := acct.avatars[0], acct.avatars[1]
	itemID, _ := pickTradableItem(t, st, ctx, true)
	seedVaultItem(t, st, ctx, src.id, 0, itemID, 1, nil)

	setAvatarOnline(t, st, ctx, dst.id)

	if err := st.TransferVaultItem(ctx, acct.id, src.name, dst.name, 0, itemID); err != errCharacterOnline {
		t.Fatalf("transfer with online dest = %v, want errCharacterOnline", err)
	}
	if vaultRows(t, st, ctx, src.id) != 1 {
		t.Errorf("source vault mutated while dest online")
	}
}

// A slot/item mismatch (the SPA's optimistic check) is rejected as not found.
func TestIT_VaultTransfer_ItemMismatchRejected(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 25, 2, 1000)
	src, dst := acct.avatars[0], acct.avatars[1]
	itemID, _ := pickTradableItem(t, st, ctx, true)
	seedVaultItem(t, st, ctx, src.id, 0, itemID, 1, nil)

	// Ask to move a different item id than the slot actually holds.
	if err := st.TransferVaultItem(ctx, acct.id, src.name, dst.name, 0, itemID+999999); err != errItemNotFound {
		t.Fatalf("mismatched item transfer = %v, want errItemNotFound", err)
	}
	if vaultRows(t, st, ctx, src.id) != 1 {
		t.Errorf("source vault mutated on a mismatched transfer")
	}
}

// A full destination vault refuses the transfer; the item stays at the source.
func TestIT_VaultTransfer_DestFull(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 26, 2, 1000)
	src, dst := acct.avatars[0], acct.avatars[1]
	itemID, _ := pickTradableItem(t, st, ctx, true)

	// Fill all 96 destination slots, then seed one item in the source.
	for i := 0; i < vaultSlotCount; i++ {
		seedVaultItem(t, st, ctx, dst.id, i, itemID, 1, nil)
	}
	seedVaultItem(t, st, ctx, src.id, 0, itemID, 1, nil)

	if err := st.TransferVaultItem(ctx, acct.id, src.name, dst.name, 0, itemID); err != errDestVaultFull {
		t.Fatalf("transfer to full vault = %v, want errDestVaultFull", err)
	}
	if vaultRows(t, st, ctx, src.id) != 1 {
		t.Errorf("source lost the item on a full-dest transfer")
	}
	if vaultRows(t, st, ctx, dst.id) != vaultSlotCount {
		t.Errorf("dest gained a slot past capacity")
	}
}

// Dup-safety: two concurrent transfers of the SAME source slot must result in
// exactly one moved item -- never two copies, never zero. The serializable tx +
// FOR UPDATE row lock guarantees one wins and the other sees an empty slot.
func TestIT_VaultTransfer_ConcurrentNoDupe(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 27, 3, 1000)
	src, dstA, dstB := acct.avatars[0], acct.avatars[1], acct.avatars[2]
	itemID, _ := pickTradableItem(t, st, ctx, true)
	seedVaultItem(t, st, ctx, src.id, 0, itemID, 1, nil)

	var wg sync.WaitGroup
	errs := make([]error, 2)
	wg.Add(2)
	go func() { defer wg.Done(); errs[0] = st.TransferVaultItem(ctx, acct.id, src.name, dstA.name, 0, itemID) }()
	go func() { defer wg.Done(); errs[1] = st.TransferVaultItem(ctx, acct.id, src.name, dstB.name, 0, itemID) }()
	wg.Wait()

	okCount := 0
	for _, e := range errs {
		if e == nil {
			okCount++
		} else if e != errItemNotFound {
			t.Errorf("unexpected concurrent transfer error: %v", e)
		}
	}
	if okCount != 1 {
		t.Fatalf("%d of 2 concurrent transfers succeeded, want exactly 1", okCount)
	}
	// Exactly one copy exists across all three vaults.
	total := vaultRows(t, st, ctx, src.id) + vaultRows(t, st, ctx, dstA.id) + vaultRows(t, st, ctx, dstB.id)
	if total != 1 {
		t.Fatalf("item count across vaults = %d, want exactly 1 (no dupe / no loss)", total)
	}
}

// --- send mail -------------------------------------------------------------

// Sending mail with item attachments removes each item from the sender's vault
// and lands it as a mail attachment in the recipient's mailbox.
func TestIT_SendMail_RemovesItemsAndDelivers(t *testing.T) {
	st, ctx := testStore(t)
	sender := seedAccount(t, st, ctx, 30, 1, 1000)
	recip := seedAccount(t, st, ctx, 31, 1, 1000)
	itemID, _ := pickTradableItem(t, st, ctx, true)
	seedVaultItem(t, st, ctx, sender.avatars[0].id, 0, itemID, 3, nil)
	seedVaultItem(t, st, ctx, sender.avatars[0].id, 1, itemID, 5, nil)

	items := []MailItemInput{{Slot: 0, ItemID: itemID}, {Slot: 1, ItemID: itemID}}
	if err := st.SendMail(ctx, sender.id, sender.avatars[0].name, recip.avatars[0].name,
		"Hello", "here are some items", items); err != nil {
		t.Fatalf("SendMail: %v", err)
	}

	if n := vaultRows(t, st, ctx, sender.avatars[0].id); n != 0 {
		t.Errorf("sender vault holds %d rows after send, want 0 (items removed)", n)
	}
	mail := st.accountMail(ctx, recip.id)
	if !hasItemAttachment(mail, itemID) {
		t.Errorf("recipient mailbox has no item attachment; got %+v", mail)
	}
	// Both attachments delivered.
	count := 0
	for _, m := range mail {
		for _, a := range m.Attachments {
			if a.Type == "item" {
				count++
			}
		}
	}
	if count != 2 {
		t.Errorf("delivered %d item attachments, want 2", count)
	}
}

// A body-only message (no items) is delivered fine.
func TestIT_SendMail_BodyOnly(t *testing.T) {
	st, ctx := testStore(t)
	sender := seedAccount(t, st, ctx, 32, 1, 1000)
	recip := seedAccount(t, st, ctx, 33, 1, 1000)

	if err := st.SendMail(ctx, sender.id, sender.avatars[0].name, recip.avatars[0].name,
		"Subject only", "just a note", nil); err != nil {
		t.Fatalf("SendMail body-only: %v", err)
	}
	if len(st.accountMail(ctx, recip.id)) == 0 {
		t.Error("recipient got no mail")
	}
}

// Sending between two of your OWN characters is allowed (just not to itself).
func TestIT_SendMail_BetweenOwnAlts(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 34, 2, 1000)
	from, to := acct.avatars[0], acct.avatars[1]
	itemID, _ := pickTradableItem(t, st, ctx, true)
	seedVaultItem(t, st, ctx, from.id, 0, itemID, 1, nil)

	if err := st.SendMail(ctx, acct.id, from.name, to.name, "to my alt", "", []MailItemInput{{Slot: 0, ItemID: itemID}}); err != nil {
		t.Fatalf("SendMail to own alt: %v", err)
	}
	if vaultRows(t, st, ctx, from.id) != 0 {
		t.Errorf("sender vault not emptied")
	}
	// The mail lands in the SAME account's mailbox (recipient is an alt).
	if !hasItemAttachment(st.accountMail(ctx, acct.id), itemID) {
		t.Errorf("alt did not receive the item")
	}
}

// A subject is required.
func TestIT_SendMail_SubjectRequired(t *testing.T) {
	st, ctx := testStore(t)
	sender := seedAccount(t, st, ctx, 35, 1, 1000)
	recip := seedAccount(t, st, ctx, 36, 1, 1000)
	if err := st.SendMail(ctx, sender.id, sender.avatars[0].name, recip.avatars[0].name,
		"   ", "body", nil); err != errNoSubject {
		t.Fatalf("empty subject = %v, want errNoSubject", err)
	}
}

// A message with neither body nor items is rejected.
func TestIT_SendMail_EmptyRejected(t *testing.T) {
	st, ctx := testStore(t)
	sender := seedAccount(t, st, ctx, 37, 1, 1000)
	recip := seedAccount(t, st, ctx, 38, 1, 1000)
	if err := st.SendMail(ctx, sender.id, sender.avatars[0].name, recip.avatars[0].name,
		"subject", "   ", nil); err != errEmptyMail {
		t.Fatalf("empty mail = %v, want errEmptyMail", err)
	}
}

// You cannot send to your own (sending) character.
func TestIT_SendMail_ToSelfRejected(t *testing.T) {
	st, ctx := testStore(t)
	sender := seedAccount(t, st, ctx, 39, 1, 1000)
	if err := st.SendMail(ctx, sender.id, sender.avatars[0].name, sender.avatars[0].name,
		"subject", "body", nil); err != errSameCharacter {
		t.Fatalf("send to self = %v, want errSameCharacter", err)
	}
}

// More than six attachments is rejected.
func TestIT_SendMail_TooManyItems(t *testing.T) {
	st, ctx := testStore(t)
	sender := seedAccount(t, st, ctx, 40, 1, 1000)
	recip := seedAccount(t, st, ctx, 41, 1, 1000)
	itemID, _ := pickTradableItem(t, st, ctx, true)
	items := make([]MailItemInput, 7)
	for i := range items {
		items[i] = MailItemInput{Slot: i, ItemID: itemID}
	}
	if err := st.SendMail(ctx, sender.id, sender.avatars[0].name, recip.avatars[0].name,
		"subject", "", items); err != errTooManyItems {
		t.Fatalf("7 items = %v, want errTooManyItems", err)
	}
}

// Attaching the same vault slot twice is rejected.
func TestIT_SendMail_DupSlotRejected(t *testing.T) {
	st, ctx := testStore(t)
	sender := seedAccount(t, st, ctx, 42, 1, 1000)
	recip := seedAccount(t, st, ctx, 43, 1, 1000)
	itemID, _ := pickTradableItem(t, st, ctx, true)
	seedVaultItem(t, st, ctx, sender.avatars[0].id, 0, itemID, 1, nil)
	items := []MailItemInput{{Slot: 0, ItemID: itemID}, {Slot: 0, ItemID: itemID}}
	if err := st.SendMail(ctx, sender.id, sender.avatars[0].name, recip.avatars[0].name,
		"subject", "", items); err != errDupMailSlot {
		t.Fatalf("dup slot = %v, want errDupMailSlot", err)
	}
	if vaultRows(t, st, ctx, sender.avatars[0].id) != 1 {
		t.Errorf("vault mutated on a rejected dup-slot send")
	}
}

// An unknown recipient name is rejected and nothing leaves the sender's vault.
func TestIT_SendMail_BadRecipient(t *testing.T) {
	st, ctx := testStore(t)
	sender := seedAccount(t, st, ctx, 44, 1, 1000)
	itemID, _ := pickTradableItem(t, st, ctx, true)
	seedVaultItem(t, st, ctx, sender.avatars[0].id, 0, itemID, 1, nil)
	err := st.SendMail(ctx, sender.id, sender.avatars[0].name, "No_Suchplayer",
		"subject", "", []MailItemInput{{Slot: 0, ItemID: itemID}})
	if err != errBadRecipient {
		t.Fatalf("unknown recipient = %v, want errBadRecipient", err)
	}
	if vaultRows(t, st, ctx, sender.avatars[0].id) != 1 {
		t.Errorf("vault mutated on a send to an unknown recipient")
	}
}

// The offline-guard blocks a send while the sender is online.
func TestIT_SendMail_BlocksOnlineSender(t *testing.T) {
	st, ctx := testStore(t)
	sender := seedAccount(t, st, ctx, 45, 1, 1000)
	recip := seedAccount(t, st, ctx, 46, 1, 1000)
	itemID, _ := pickTradableItem(t, st, ctx, true)
	seedVaultItem(t, st, ctx, sender.avatars[0].id, 0, itemID, 1, nil)
	setAvatarOnline(t, st, ctx, sender.avatars[0].id)

	if err := st.SendMail(ctx, sender.id, sender.avatars[0].name, recip.avatars[0].name,
		"subject", "", []MailItemInput{{Slot: 0, ItemID: itemID}}); err != errCharacterOnline {
		t.Fatalf("send while online = %v, want errCharacterOnline", err)
	}
	if vaultRows(t, st, ctx, sender.avatars[0].id) != 1 {
		t.Errorf("vault mutated while sender online")
	}
}

// Dup-safety: two concurrent sends attaching the SAME vault slot must result in
// the item being mailed exactly once -- never twice, never lost.
func TestIT_SendMail_ConcurrentNoDupe(t *testing.T) {
	st, ctx := testStore(t)
	sender := seedAccount(t, st, ctx, 47, 1, 1000)
	recipA := seedAccount(t, st, ctx, 48, 1, 1000)
	recipB := seedAccount(t, st, ctx, 49, 1, 1000)
	itemID, _ := pickTradableItem(t, st, ctx, true)
	seedVaultItem(t, st, ctx, sender.avatars[0].id, 0, itemID, 1, nil)

	var wg sync.WaitGroup
	errs := make([]error, 2)
	wg.Add(2)
	go func() {
		defer wg.Done()
		errs[0] = st.SendMail(ctx, sender.id, sender.avatars[0].name, recipA.avatars[0].name,
			"s", "", []MailItemInput{{Slot: 0, ItemID: itemID}})
	}()
	go func() {
		defer wg.Done()
		errs[1] = st.SendMail(ctx, sender.id, sender.avatars[0].name, recipB.avatars[0].name,
			"s", "", []MailItemInput{{Slot: 0, ItemID: itemID}})
	}()
	wg.Wait()

	okCount := 0
	for _, e := range errs {
		if e == nil {
			okCount++
		} else if e != errItemNotFound {
			t.Errorf("unexpected concurrent send error: %v", e)
		}
	}
	if okCount != 1 {
		t.Fatalf("%d of 2 concurrent sends succeeded, want exactly 1", okCount)
	}
	// The item left the vault and lives in exactly one recipient's mailbox.
	if vaultRows(t, st, ctx, sender.avatars[0].id) != 0 {
		t.Errorf("sender vault not emptied after the winning send")
	}
	delivered := 0
	if hasItemAttachment(st.accountMail(ctx, recipA.id), itemID) {
		delivered++
	}
	if hasItemAttachment(st.accountMail(ctx, recipB.id), itemID) {
		delivered++
	}
	if delivered != 1 {
		t.Fatalf("item delivered to %d mailboxes, want exactly 1", delivered)
	}
}
