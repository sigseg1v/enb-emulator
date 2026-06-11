// SPDX-License-Identifier: MIT
// Freya Online -- player-to-player mail composition (Mailbox "Send" form).
//
// A player composes mail FROM one of their characters TO any live character (one
// of their own alts, or someone else's). The message carries a subject, an
// optional body, and up to maxMailItems item attachments taken from the SENDING
// character's vault. Each attached item is REMOVED from the sender's vault and
// lives in the message's attachment storage until the recipient loots it.
//
// The whole send (remove every attached item from the sender's vault + insert
// the message and its attachments) runs in ONE serializable transaction with row
// locks, so a send can never dup an item (item in both the vault and the mail) or
// lose one. The sender's vault is server-authoritative while online, so the
// sender is offline-guarded; delivery only writes the recipient's mailbox tables
// (which the game does not hold in memory), so the recipient is NOT guarded --
// they loot it later, and looting is itself guarded.

package main

import (
	"context"
	"errors"
	"strings"

	"github.com/jackc/pgx/v5"
)

const (
	// maxMailItems is the attachment cap (mirrors the client; the server is the
	// authority and rejects a 7th regardless).
	maxMailItems = 6
	// maxMailSubject / maxMailBody bound the text fields. The subject is required;
	// the body is optional but, if empty, at least one item must be attached.
	maxMailSubject = 128
	maxMailBody    = 4000
)

var (
	errBadRecipient = errors.New("no such character to send to")
	errEmptyMail    = errors.New("mail must have a body or at least one item")
	errTooManyItems = errors.New("a message can carry at most 6 items")
	errDupMailSlot  = errors.New("the same vault slot was attached twice")
	errNoSubject    = errors.New("a subject is required")
)

// MailItemInput is one attachment the composer wants to send: the vault slot and
// the item id the SPA last saw there (cross-checked under lock).
type MailItemInput struct {
	Slot   int `json:"slot"`
	ItemID int `json:"itemId"`
}

// insertItemAttachment adds one item-instance attachment to an existing message.
func insertItemAttachment(ctx context.Context, tx pgx.Tx, msgID int64, vi vaultInstance) error {
	_, err := tx.Exec(ctx, `
		INSERT INTO mailbox_attachments
		  (message_id, kind, item_id, stack_size, quality, item_cost, builder_name, structure)
		VALUES ($1, 0, $2, $3, $4, $5, $6, $7)`,
		msgID, vi.itemID, vi.stack, vi.quality, vi.cost, vi.builder, vi.structure)
	return err
}

// SendMail composes and delivers a message. fromName must be an owned, offline
// character; toName must be a live character other than the sender. Either a
// non-empty body or at least one item (1..maxMailItems) is required; both is
// allowed. Every attached item is removed from fromName's vault atomically.
func (s *Store) SendMail(ctx context.Context, accountID int64,
	fromName, toName, subject, body string, items []MailItemInput) error {

	subject = strings.TrimSpace(subject)
	if subject == "" {
		return errNoSubject
	}
	if len([]rune(subject)) > maxMailSubject {
		return errBadInput
	}
	if len([]rune(body)) > maxMailBody {
		return errBadInput
	}
	if len(items) > maxMailItems {
		return errTooManyItems
	}
	if strings.TrimSpace(body) == "" && len(items) == 0 {
		return errEmptyMail
	}
	if fromName == toName {
		return errSameCharacter
	}

	// Reject duplicate vault slots up front -- attaching the same slot twice is a
	// malformed request, and processing it would try to remove an already-removed
	// item. Distinct slots also let us lock them in a fixed order.
	seen := make(map[int]bool, len(items))
	for _, it := range items {
		if seen[it.Slot] {
			return errDupMailSlot
		}
		seen[it.Slot] = true
	}

	// Tradability is decided from the content DB (separate pool) before the tx:
	// no-trade items cannot be mailed (they cannot leave the character), matching
	// the AH. Resolve every distinct item id once.
	if len(items) > 0 {
		ids := make([]int, 0, len(items))
		for _, it := range items {
			ids = append(ids, it.ItemID)
		}
		tpls, err := s.resolveItems(ctx, ids)
		if err != nil {
			return err
		}
		for _, it := range items {
			t, ok := tpls[it.ItemID]
			if !ok {
				return errItemNotFound
			}
			if t.view.NoTrade {
				return errItemUntradable
			}
		}
	}

	// Lock the attached slots in a stable (ascending-slot) order so two concurrent
	// sends from the same character cannot deadlock.
	ordered := append([]MailItemInput(nil), items...)
	sortMailItems(ordered)

	return s.serialTx(ctx, func(tx pgx.Tx) error {
		fromAvatar, err := ownedAvatarByName(ctx, tx, accountID, fromName)
		if err != nil {
			return err
		}

		// Recipient: any live character (own alt or another player). Resolve its
		// owning account + avatar so the message lands in the right mailbox.
		var toAvatar, toAccount int64
		err = tx.QueryRow(ctx, `
			SELECT ai.avatar_id, ai.account_id
			  FROM avatar_info ai
			  JOIN avatar_data ad ON ad.avatar_id = ai.avatar_id
			 WHERE ai.deleted_at IS NULL
			   AND trim(both '_' from coalesce(ad.first_name,'') || '_' || coalesce(ad.last_name,'')) = $1
			 LIMIT 1`, toName).Scan(&toAvatar, &toAccount)
		if errors.Is(err, pgx.ErrNoRows) {
			return errBadRecipient
		}
		if err != nil {
			return err
		}
		if toAvatar == fromAvatar {
			return errSameCharacter
		}

		// The sender's vault mutates -- guard the sender only. (Delivery writes
		// only the recipient's mailbox, which the game does not hold in memory.)
		if err := assertAvatarOffline(ctx, tx, fromAvatar); err != nil {
			return err
		}

		// Pull every attached item out of the sender's vault first, capturing its
		// instance payload, so any failure (missing slot, changed item) aborts the
		// whole send before the message exists.
		instances := make([]vaultInstance, 0, len(ordered))
		for _, it := range ordered {
			vi, err := lockVaultSlot(ctx, tx, fromAvatar, it.Slot, it.ItemID)
			if err != nil {
				return err
			}
			if _, err := tx.Exec(ctx,
				`DELETE FROM avatar_vault_items WHERE avatar_id = $1 AND inventory_slot = $2`,
				fromAvatar, it.Slot); err != nil {
				return err
			}
			instances = append(instances, vi)
		}

		msgID, err := insertMessage(ctx, tx, toAccount, toAvatar, fromName, subject, body)
		if err != nil {
			return err
		}
		for _, vi := range instances {
			if err := insertItemAttachment(ctx, tx, msgID, vi); err != nil {
				return err
			}
		}
		return nil
	})
}

// sortMailItems sorts attachments by ascending vault slot in place.
func sortMailItems(items []MailItemInput) {
	for i := 1; i < len(items); i++ {
		for j := i; j > 0 && items[j-1].Slot > items[j].Slot; j-- {
			items[j-1], items[j] = items[j], items[j-1]
		}
	}
}
