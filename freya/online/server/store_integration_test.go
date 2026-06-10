// SPDX-License-Identifier: MIT
// Freya Online -- store-level integration tests (real Postgres, two-DB schema).
//
// Gated on FREYA_TEST_DB (see itest_helpers_test.go). These cover the SQL that
// the pure unit tests cannot: cross-DB item resolve, the FOR-UPDATE offline
// guard, the deposit/bid/buyout credit math, and mail loot.

package main

import (
	"strconv"
	"testing"
)

func mustListingID(t *testing.T, id string) int64 {
	t.Helper()
	v, err := strconv.ParseInt(id, 10, 64)
	if err != nil {
		t.Fatalf("listing id %q: %v", id, err)
	}
	return v
}

// Auth: a seeded Argon2id PHC verifies, the account id resolves, and a wrong
// password is rejected -- the exact path /api/login uses.
func TestIT_Auth(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 1, 1, 0)

	phc, err := st.passwordPHC(ctx, acct.username)
	if err != nil {
		t.Fatalf("passwordPHC: %v", err)
	}
	if ok, err := verifyPassword(phc, acct.password); err != nil || !ok {
		t.Fatalf("verifyPassword(correct) = %v, %v", ok, err)
	}
	if ok, _ := verifyPassword(phc, "wrong-"+acct.password); ok {
		t.Fatal("verifyPassword accepted a wrong password")
	}
	if id, err := st.accountID(ctx, acct.username); err != nil || id != acct.id {
		t.Fatalf("accountID = %d, %v; want %d", id, err, acct.id)
	}
	if _, err := st.passwordPHC(ctx, "no_such_user_itest"); err == nil {
		t.Fatal("passwordPHC for missing account should error")
	}
}

// liveCharacters lists every non-deleted avatar with summed credits, which is
// what bootstrap reports as the account credit total.
func TestIT_LiveCharacters(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 2, 2, 5000) // 2 chars @ 5000 each

	chars, err := st.liveCharacters(ctx, acct.id)
	if err != nil {
		t.Fatalf("liveCharacters: %v", err)
	}
	if len(chars) != 2 {
		t.Fatalf("got %d chars, want 2", len(chars))
	}
	var total int64
	for _, c := range chars {
		total += c.Credits
		if c.Name == "" {
			t.Error("character has empty name")
		}
	}
	if total != 10000 {
		t.Errorf("credit total = %d, want 10000", total)
	}
}

// PostListing collects the 10% deposit up front and removes the instance from
// the seller's vault; the new listing surfaces to the seller and to browse.
func TestIT_PostListing_DebitsAndRemovesItem(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 3, 1, 1_000_000)
	av := acct.avatars[0]

	itemID, buyingPrice := pickTradableItem(t, st, ctx, true) // ore, stackable
	const stack = 5
	seedVaultItem(t, st, ctx, av.id, 0, itemID, stack, nil)

	before := creditsOf(t, st, ctx, av.id)
	mv, err := st.PostListing(ctx, acct.id, itemID, 0, stack, av.name, "short", 10, 0)
	if err != nil {
		t.Fatalf("PostListing: %v", err)
	}

	itemValue := int64(buyingPrice) * stack
	wantDeposit := (itemValue + 9) / 10 // ceil(10%)
	after := creditsOf(t, st, ctx, av.id)
	if before-after != wantDeposit {
		t.Errorf("deposit debited = %d, want %d (itemValue=%d)", before-after, wantDeposit, itemValue)
	}
	if n := vaultCount(t, st, ctx, av.id, itemID); n != 0 {
		t.Errorf("vault still holds %d of the listed item; should be removed", n)
	}

	listingID := mustListingID(t, mv.ID)
	mine := st.listingsBySeller(ctx, acct.id)
	if !containsListing(mine, mv.ID) {
		t.Errorf("listing %d absent from listingsBySeller", listingID)
	}
	if !containsListingView(st.openListings(ctx), mv.ID) {
		t.Errorf("listing %d absent from openListings (browse)", listingID)
	}
}

// The offline-guard refuses a listing while the seller character is online.
func TestIT_OfflineGuard_BlocksOnlineSeller(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 4, 1, 1_000_000)
	av := acct.avatars[0]
	itemID, _ := pickTradableItem(t, st, ctx, true)
	seedVaultItem(t, st, ctx, av.id, 0, itemID, 1, nil)

	setAvatarOnline(t, st, ctx, av.id)

	_, err := st.PostListing(ctx, acct.id, itemID, 0, 1, av.name, "short", 10, 0)
	if err != errCharacterOnline {
		t.Fatalf("PostListing while online = %v, want errCharacterOnline", err)
	}
	// And the item must still be in the vault (transaction rolled back).
	if n := vaultCount(t, st, ctx, av.id, itemID); n != 1 {
		t.Errorf("vault count = %d after blocked listing, want 1 (no mutation)", n)
	}
}

// A second bid refunds the previous high bidder via mailbox credits.
func TestIT_Bid_RefundsPriorBidder(t *testing.T) {
	st, ctx := testStore(t)
	seller := seedAccount(t, st, ctx, 5, 1, 1_000_000)
	bidder1 := seedAccount(t, st, ctx, 6, 1, 1_000_000)
	bidder2 := seedAccount(t, st, ctx, 7, 1, 1_000_000)

	itemID, _ := pickTradableItem(t, st, ctx, true)
	seedVaultItem(t, st, ctx, seller.avatars[0].id, 0, itemID, 1, nil)
	mv, err := st.PostListing(ctx, seller.id, itemID, 0, 1, seller.avatars[0].name, "short", 100, 0)
	if err != nil {
		t.Fatalf("PostListing: %v", err)
	}
	listingID := mustListingID(t, mv.ID)

	if _, err := st.PlaceBid(ctx, bidder1.id, listingID); err != nil {
		t.Fatalf("PlaceBid(bidder1): %v", err)
	}
	b1WalletAfterBid := creditsOf(t, st, ctx, bidder1.avatars[0].id)
	if _, err := st.PlaceBid(ctx, bidder2.id, listingID); err != nil {
		t.Fatalf("PlaceBid(bidder2): %v", err)
	}

	// bidder1 should now have a refund mail with a credits attachment = 100.
	mail := st.accountMail(ctx, bidder1.id)
	if !hasCreditsAttachment(mail, 100) {
		t.Errorf("bidder1 has no 100-credit refund mail; got %+v", mail)
	}
	// Their wallet stays debited until they loot the refund (offline-guard aside).
	if b1WalletAfterBid != 1_000_000-100 {
		t.Errorf("bidder1 wallet = %d after bid, want %d", b1WalletAfterBid, 1_000_000-100)
	}
}

// Buyout delivers the item to the buyer's mailbox and the credits to the seller.
func TestIT_Buyout_DeliversItemAndProceeds(t *testing.T) {
	st, ctx := testStore(t)
	seller := seedAccount(t, st, ctx, 8, 1, 1_000_000)
	buyer := seedAccount(t, st, ctx, 9, 1, 1_000_000)

	itemID, _ := pickTradableItem(t, st, ctx, true)
	seedVaultItem(t, st, ctx, seller.avatars[0].id, 0, itemID, 1, nil)
	const buyout = 5000
	mv, err := st.PostListing(ctx, seller.id, itemID, 0, 1, seller.avatars[0].name, "short", 100, buyout)
	if err != nil {
		t.Fatalf("PostListing: %v", err)
	}
	listingID := mustListingID(t, mv.ID)

	buyerBefore := creditsOf(t, st, ctx, buyer.avatars[0].id)
	if err := st.Buyout(ctx, buyer.id, listingID); err != nil {
		t.Fatalf("Buyout: %v", err)
	}
	buyerAfter := creditsOf(t, st, ctx, buyer.avatars[0].id)
	if buyerBefore-buyerAfter != buyout {
		t.Errorf("buyer debited %d, want %d", buyerBefore-buyerAfter, buyout)
	}

	if !hasItemAttachment(st.accountMail(ctx, buyer.id), itemID) {
		t.Error("buyer mailbox has no item attachment after buyout")
	}
	if !hasCreditsAttachment(st.accountMail(ctx, seller.id), buyout) {
		t.Error("seller mailbox has no proceeds after buyout")
	}
	if containsListingView(st.openListings(ctx), mv.ID) {
		t.Error("bought-out listing still in openListings")
	}
}

// Looting a credits attachment adds to the recipient character's wallet and
// marks the square spent (re-loot fails).
func TestIT_MailLoot_Credits(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 10, 1, 1000)
	av := acct.avatars[0]

	// Deliver a 777-credit mail by settling a buyout from a throwaway seller, or
	// directly: use a one-off tx via the exported helper path. Simplest is a
	// second account buying out a listing this account posted -- but that routes
	// proceeds here. Instead, post+expire is heavier; deliver directly in a tx.
	deliverCreditsForTest(t, st, ctx, acct.id, av.id, 777)

	before := creditsOf(t, st, ctx, av.id)
	mail := st.accountMail(ctx, acct.id)
	var mailID string
	for _, m := range mail {
		if hasCreditsAttachment([]MailView{m}, 777) {
			mailID = m.ID
		}
	}
	if mailID == "" {
		t.Fatalf("seeded credit mail not found; got %+v", mail)
	}
	id := mustListingID(t, mailID)

	if err := st.LootAttachment(ctx, acct.id, id, 0); err != nil {
		t.Fatalf("LootAttachment: %v", err)
	}
	if got := creditsOf(t, st, ctx, av.id); got != before+777 {
		t.Errorf("wallet = %d after loot, want %d", got, before+777)
	}
	if err := st.LootAttachment(ctx, acct.id, id, 0); err != errAlreadyLooted {
		t.Errorf("re-loot = %v, want errAlreadyLooted", err)
	}
}

// --- assertion helpers -----------------------------------------------------

func containsListing(ls []MyListingView, id string) bool {
	for _, l := range ls {
		if l.ID == id {
			return true
		}
	}
	return false
}

func containsListingView(ls []ListingView, id string) bool {
	for _, l := range ls {
		if l.ID == id {
			return true
		}
	}
	return false
}

func hasCreditsAttachment(mail []MailView, amount int64) bool {
	for _, m := range mail {
		for _, a := range m.Attachments {
			if a.Type == "credits" && a.Amount != nil && *a.Amount == amount {
				return true
			}
		}
	}
	return false
}

func hasItemAttachment(mail []MailView, itemID int) bool {
	want := strconv.Itoa(itemID)
	for _, m := range mail {
		for _, a := range m.Attachments {
			if a.Type == "item" && a.Item != nil && a.Item.ID == want {
				return true
			}
		}
	}
	return false
}
