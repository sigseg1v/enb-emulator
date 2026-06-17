// SPDX-License-Identifier: MIT
// Freya Online -- Mailbox writes (deliver / mark-read / loot / expiry sweep).
//
// Delivery helpers run inside a caller-provided transaction so AH settlement and
// the mailbox row that records it commit atomically. Looting moves an attachment
// into a live character: credits add to that character's wallet, items drop into
// the first free vault slot. Mail (and any unlooted attachments) expires after
// 90 days.

package main

import (
	"context"
	"errors"
	"time"

	"github.com/jackc/pgx/v5"
)

// vaultSlotCount is the in-game vault capacity (slots 0..95). Looting refuses
// once all are occupied -- the client cannot display a 97th slot.
const vaultSlotCount = 96

var (
	errMailGone      = errors.New("mail not found")
	errAttachGone    = errors.New("attachment not found")
	errAlreadyLooted = errors.New("attachment already looted")
	errNoVaultSpace  = errors.New("vault is full, cannot loot mail")
)

// recipientName resolves an avatar's display name within a tx (best-effort).
func recipientName(ctx context.Context, tx pgx.Tx, avatarID int64) *string {
	if avatarID == 0 {
		return nil
	}
	var name string
	err := tx.QueryRow(ctx, `
		SELECT trim(both '_' from coalesce(first_name,'') || '_' || coalesce(last_name,''))
		  FROM avatar_data WHERE avatar_id = $1`, avatarID).Scan(&name)
	if err != nil {
		return nil
	}
	return &name
}

// insertMessage creates a mailbox row (90-day default expiry) and returns its id.
func insertMessage(ctx context.Context, tx pgx.Tx, accountID, recipientAvatar int64, sender, subject, body string) (int64, error) {
	var id int64
	err := tx.QueryRow(ctx, `
		INSERT INTO mailbox_messages
		  (account_id, recipient_avatar_id, recipient_name, sender_name, subject, body)
		VALUES ($1, $2, $3, $4, $5, $6)
		RETURNING id`,
		accountID,
		nullIfZero(recipientAvatar),
		recipientName(ctx, tx, recipientAvatar),
		sender, subject, body).Scan(&id)
	return id, err
}

func nullIfZero(v int64) *int64 {
	if v == 0 {
		return nil
	}
	return &v
}

// deliverCredits posts a credits attachment to an account's mailbox.
func deliverCredits(ctx context.Context, tx pgx.Tx, accountID int64, sender, subject, body string, amount int64) error {
	if amount <= 0 {
		return nil
	}
	msgID, err := insertMessage(ctx, tx, accountID, 0, sender, subject, body)
	if err != nil {
		return err
	}
	_, err = tx.Exec(ctx, `
		INSERT INTO mailbox_attachments (message_id, kind, credits)
		VALUES ($1, 1, $2)`, msgID, amount)
	return err
}

// deliverItem posts an item-instance attachment to an account's mailbox,
// addressed to recipientAvatar (0 = account-level, looted by any character).
func deliverItem(ctx context.Context, tx pgx.Tx, accountID, recipientAvatar int64,
	sender, subject, body string, itemID, stack int, quality *float64,
	cost *int64, builder *string, structure *float64) error {

	msgID, err := insertMessage(ctx, tx, accountID, recipientAvatar, sender, subject, body)
	if err != nil {
		return err
	}
	_, err = tx.Exec(ctx, `
		INSERT INTO mailbox_attachments
		  (message_id, kind, item_id, stack_size, quality, item_cost, builder_name, structure)
		VALUES ($1, 0, $2, $3, $4, $5, $6, $7)`,
		msgID, itemID, stack, quality, cost, builder, structure)
	return err
}

// MarkRead flags a message read, scoped to the owning account.
func (s *Store) MarkRead(ctx context.Context, accountID int64, messageID int64) error {
	tag, err := s.user.Exec(ctx,
		`UPDATE mailbox_messages SET is_read = true WHERE id = $1 AND account_id = $2`,
		messageID, accountID)
	if err != nil {
		return err
	}
	if tag.RowsAffected() == 0 {
		return errMailGone
	}
	return nil
}

// DeleteMessage removes a mail message (and its attachments, via FK cascade),
// scoped to the owning account. Any unlooted attachments are forfeited -- this
// is the user explicitly discarding the mail. Mirrors the expiry sweep.
func (s *Store) DeleteMessage(ctx context.Context, accountID int64, messageID int64) error {
	tag, err := s.user.Exec(ctx,
		`DELETE FROM mailbox_messages WHERE id = $1 AND account_id = $2`,
		messageID, accountID)
	if err != nil {
		return err
	}
	if tag.RowsAffected() == 0 {
		return errMailGone
	}
	return nil
}

// LootAttachment loots the index-th attachment of a message into a character:
// credits add to the character's wallet; items drop into a free vault slot.
// lootAvatar is the account's primary live character when the message is not
// addressed to a specific (still-live) character.
func (s *Store) LootAttachment(ctx context.Context, accountID int64, messageID int64, index int) error {
	return s.serialTx(ctx, func(tx pgx.Tx) error {
		return s.lootAttachmentTx(ctx, tx, accountID, messageID, index)
	})
}

// lootAttachmentTx is the body of LootAttachment, run inside a serializable tx.
func (s *Store) lootAttachmentTx(ctx context.Context, tx pgx.Tx, accountID int64, messageID int64, index int) error {
	// Confirm ownership and find the loot-target character.
	var recipientAvatar *int64
	err := tx.QueryRow(ctx,
		`SELECT recipient_avatar_id FROM mailbox_messages WHERE id = $1 AND account_id = $2 FOR UPDATE`,
		messageID, accountID).Scan(&recipientAvatar)
	if errors.Is(err, pgx.ErrNoRows) {
		return errMailGone
	}
	if err != nil {
		return err
	}

	// The index-th attachment (stable order by id), locked.
	rows, err := tx.Query(ctx, `
		SELECT id, kind, item_id, stack_size, quality, item_cost, builder_name, structure, credits, looted
		  FROM mailbox_attachments
		 WHERE message_id = $1
		 ORDER BY id
		 FOR UPDATE`, messageID)
	if err != nil {
		return err
	}
	var atts []attachRow
	type extra struct {
		cost      *int64
		builder   *string
		structure *float64
	}
	var extras []extra
	for rows.Next() {
		var a attachRow
		var e extra
		if err := rows.Scan(&a.id, &a.kind, &a.itemID, &a.stack, &a.quality,
			&e.cost, &e.builder, &e.structure, &a.credits, &a.looted); err != nil {
			rows.Close()
			return err
		}
		atts = append(atts, a)
		extras = append(extras, e)
	}
	rows.Close()
	if err := rows.Err(); err != nil {
		return err
	}
	if index < 0 || index >= len(atts) {
		return errAttachGone
	}
	a := atts[index]
	e := extras[index]
	if a.looted {
		return errAlreadyLooted
	}

	// Resolve the loot-target avatar: addressed recipient if still live, else
	// the account's primary character.
	var lootAvatar int64
	if recipientAvatar != nil {
		var live bool
		err = tx.QueryRow(ctx,
			`SELECT deleted_at IS NULL FROM avatar_info WHERE avatar_id = $1`, *recipientAvatar).Scan(&live)
		if err == nil && live {
			lootAvatar = *recipientAvatar
		}
	}
	if lootAvatar == 0 {
		lootAvatar, _, err = primaryAvatar(ctx, tx, accountID)
		if err != nil {
			return err
		}
	}

	// Offline-guard: looting adds credits/items directly to the character's
	// wallet/vault, which the server owns in memory while it is online.
	if err := assertAvatarOffline(ctx, tx, lootAvatar); err != nil {
		return err
	}

	if a.kind == 1 {
		if a.credits != nil && *a.credits > 0 {
			_, err = tx.Exec(ctx,
				`UPDATE avatar_level_info SET credits = credits + $2 WHERE avatar_id = $1`,
				lootAvatar, *a.credits)
			if err != nil {
				return err
			}
		}
	} else {
		// Drop into the first free vault slot.
		slot, err := freeVaultSlot(ctx, tx, lootAvatar)
		if err != nil {
			return err
		}
		stack := 1
		if a.stack != nil {
			stack = *a.stack
		}
		// The game reads BOTH stack_level (-> StackCount, the displayed quantity)
		// and trade_stack; it writes both equal for a stack (PlayerSaves.cpp
		// SaveVaultChange). Setting only trade_stack leaves stack_level=0, which
		// the loader forces to 1 (PlayerSaves.cpp:561) -- so a stack of 20 would
		// show as 1 in the vault. Write both equal, mirroring the game.
		// freeVaultSlot may hand back a slot that still has a stale item_id = 0
		// row (an emptied-but-not-deleted slot, see the note there), so UPSERT --
		// a plain INSERT would collide on the (avatar_id, inventory_slot) PK.
		_, err = tx.Exec(ctx, `
			INSERT INTO avatar_vault_items
			  (avatar_id, item_id, inventory_slot, stack_level, trade_stack, quality, cost, builder_name, structure)
			VALUES ($1, $2, $3, $4, $4, $5, $6, $7, $8)
			ON CONFLICT (avatar_id, inventory_slot) DO UPDATE SET
			  item_id = EXCLUDED.item_id, stack_level = EXCLUDED.stack_level,
			  trade_stack = EXCLUDED.trade_stack, quality = EXCLUDED.quality,
			  cost = EXCLUDED.cost, builder_name = EXCLUDED.builder_name,
			  structure = EXCLUDED.structure`,
			lootAvatar, a.itemID, slot, stack, a.quality, e.cost, e.builder, e.structure)
		if err != nil {
			return err
		}
	}

	if _, err := tx.Exec(ctx, `UPDATE mailbox_attachments SET looted = true WHERE id = $1`, a.id); err != nil {
		return err
	}
	return nil
}

// freeVaultSlot finds the lowest unused vault slot for an avatar.
func freeVaultSlot(ctx context.Context, tx pgx.Tx, avatarID int64) (int, error) {
	// Only rows that actually hold an item occupy a slot. The C++ server empties
	// a vault slot by UPDATEing its row to item_id = 0 (Player::SaveVaultChange ->
	// SaveManager UPDATE), never deleting it, so a heavily-used vault accumulates
	// stale item_id = 0 (or NULL) rows. The website's vault VIEW already ignores
	// those (resolveItems(0) fails -> the slot is dropped in accountVaultStorage),
	// so counting them here as occupied is what made a half-full vault report
	// "full" -- the view showed 56/96 while this saw all 96 slots "used". Match the
	// view: a slot is free unless it carries a real (non-zero, non-NULL) item.
	rows, err := tx.Query(ctx,
		`SELECT inventory_slot FROM avatar_vault_items
		  WHERE avatar_id = $1 AND item_id IS NOT NULL AND item_id <> 0
		  ORDER BY inventory_slot`, avatarID)
	if err != nil {
		return 0, err
	}
	used := map[int]bool{}
	for rows.Next() {
		var s int
		if err := rows.Scan(&s); err != nil {
			rows.Close()
			return 0, err
		}
		used[s] = true
	}
	rows.Close()
	if err := rows.Err(); err != nil {
		return 0, err
	}
	// The in-game vault is 96 slots (0..95). If every slot is occupied there is
	// nowhere to put the looted item, so refuse rather than write slot 96+ (which
	// the client cannot show and the server would clamp/drop).
	for i := 0; i < vaultSlotCount; i++ {
		if !used[i] {
			return i, nil
		}
	}
	return 0, errNoVaultSpace
}

// sweepExpiredMail deletes mail past its expiry. Attachments cascade-delete;
// unlooted ones are forfeited (per spec). Returns the number of rows removed.
func (s *Store) sweepExpiredMail(ctx context.Context) (int64, error) {
	tag, err := s.user.Exec(ctx, `DELETE FROM mailbox_messages WHERE expires_at < now()`)
	if err != nil {
		return 0, err
	}
	return tag.RowsAffected(), nil
}

// timeUntilNextSweep is a small helper kept here so the sweeper cadence lives
// next to the work it drives.
const sweepInterval = 5 * time.Minute
