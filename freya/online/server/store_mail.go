// SPDX-License-Identifier: MIT
// Freya Online -- Mailbox + Vault reads.
//
// The mailbox is per-account (mailbox_messages.account_id). Each message may
// carry attachments (mailbox_attachments) that render as clickable squares:
// kind=0 an item instance, kind=1 credits. Items in the mailbox expire after 90
// days; the SPA shows expiresInDays and warns when attachments are present.
//
// The vault read powers the AH sell view: an account's tradable item instances,
// grouped per character, that can be listed.

package main

import (
	"context"
	"math"
	"sort"
	"strconv"
	"time"
)

// AttachmentView is the SPA Attachment union: credits or item.
type AttachmentView struct {
	Type    string    `json:"type"` // "credits" | "item"
	Amount  *int64    `json:"amount,omitempty"`
	Item    *ItemView `json:"item,omitempty"`
	Stack   *int      `json:"stack,omitempty"`
	Quality *float64  `json:"quality,omitempty"`
	// index within the message, so the client can loot a specific square.
	Index int `json:"index"`
	// already looted squares render spent.
	Looted bool `json:"looted"`
}

// MailView mirrors the SPA Mail interface.
type MailView struct {
	ID            string           `json:"id"`
	Subject       string           `json:"subject"`
	Sender        string           `json:"sender"`
	Recipient     string           `json:"recipient"`
	Read          bool             `json:"read"`
	ExpiresInDays int              `json:"expiresInDays"`
	Body          string           `json:"body"`
	Attachments   []AttachmentView `json:"attachments"`
}

// VaultSlotView mirrors the SPA VaultSlot interface.
type VaultSlotView struct {
	Slot    int      `json:"slot"`
	Item    ItemView `json:"item"`
	Stack   int      `json:"stack"`
	Quality *float64 `json:"quality"`
}

type attachRow struct {
	id        int64
	messageID int64
	kind      int
	itemID    *int
	stack     *int
	quality   *float64
	credits   *int64
	looted    bool
}

// accountMail returns an account's mailbox, newest first, with attachments
// resolved. Returns an empty (non-nil-marshalling) slice on error.
func (s *Store) accountMail(ctx context.Context, accountID int64) []MailView {
	msgRows, err := s.user.Query(ctx, `
		SELECT id, recipient_name, sender_name, subject, body, is_read, expires_at
		  FROM mailbox_messages
		 WHERE account_id = $1
		 ORDER BY created_at DESC`, accountID)
	if err != nil {
		return []MailView{}
	}

	type msg struct {
		id        int64
		recipient *string
		sender    string
		subject   string
		body      string
		read      bool
		expires   time.Time
	}
	var msgs []msg
	var ids []int64
	for msgRows.Next() {
		var m msg
		if err := msgRows.Scan(&m.id, &m.recipient, &m.sender, &m.subject,
			&m.body, &m.read, &m.expires); err != nil {
			msgRows.Close()
			return []MailView{}
		}
		msgs = append(msgs, m)
		ids = append(ids, m.id)
	}
	msgRows.Close()
	if err := msgRows.Err(); err != nil {
		return []MailView{}
	}

	attByMsg, itemIDs := s.attachmentsFor(ctx, ids)
	items, err := s.resolveItems(ctx, itemIDs)
	if err != nil {
		items = map[int]itemTemplate{}
	}

	now := time.Now()
	out := make([]MailView, 0, len(msgs))
	for _, m := range msgs {
		days := int(math.Ceil(m.expires.Sub(now).Hours() / 24))
		if days < 0 {
			days = 0
		}
		recipient := ""
		if m.recipient != nil {
			recipient = *m.recipient
		}
		view := MailView{
			ID:            strconv.FormatInt(m.id, 10),
			Subject:       m.subject,
			Sender:        m.sender,
			Recipient:     recipient,
			Read:          m.read,
			ExpiresInDays: days,
			Body:          m.body,
			Attachments:   []AttachmentView{},
		}
		for i, a := range attByMsg[m.id] {
			av := AttachmentView{Index: i, Looted: a.looted}
			if a.kind == 1 {
				av.Type = "credits"
				av.Amount = a.credits
			} else {
				av.Type = "item"
				if a.itemID != nil {
					iv := items[*a.itemID].itemView(a.quality)
					av.Item = &iv
				}
				av.Stack = a.stack
				av.Quality = a.quality
			}
			view.Attachments = append(view.Attachments, av)
		}
		out = append(out, view)
	}
	return out
}

// attachmentsFor batch-loads attachments for the given message ids, preserving
// per-message order by attachment id, and collects the item ids to resolve.
func (s *Store) attachmentsFor(ctx context.Context, messageIDs []int64) (map[int64][]attachRow, []int) {
	byMsg := make(map[int64][]attachRow)
	var itemIDs []int
	if len(messageIDs) == 0 {
		return byMsg, itemIDs
	}
	rows, err := s.user.Query(ctx, `
		SELECT id, message_id, kind, item_id, stack_size, quality, credits, looted
		  FROM mailbox_attachments
		 WHERE message_id = ANY($1)
		 ORDER BY message_id, id`, messageIDs)
	if err != nil {
		return byMsg, itemIDs
	}
	defer rows.Close()
	for rows.Next() {
		var a attachRow
		if err := rows.Scan(&a.id, &a.messageID, &a.kind, &a.itemID, &a.stack,
			&a.quality, &a.credits, &a.looted); err != nil {
			return byMsg, itemIDs
		}
		byMsg[a.messageID] = append(byMsg[a.messageID], a)
		if a.kind == 0 && a.itemID != nil {
			itemIDs = append(itemIDs, *a.itemID)
		}
	}
	return byMsg, itemIDs
}

// accountVault returns an account's listable item instances grouped by character
// name. Only tradable items (item_base.no_trade = 0) are listable.
func (s *Store) accountVault(ctx context.Context, accountID int64) map[string][]VaultSlotView {
	out := map[string][]VaultSlotView{}

	// Live characters define the grouping (account_id -> avatars).
	chars, err := s.liveCharacters(ctx, accountID)
	if err != nil || len(chars) == 0 {
		return out
	}
	avatarIDs := make([]int64, 0, len(chars))
	nameByID := make(map[int64]string, len(chars))
	for _, c := range chars {
		avatarIDs = append(avatarIDs, c.AvatarID)
		nameByID[c.AvatarID] = c.Name
	}

	rows, err := s.user.Query(ctx, `
		SELECT avatar_id, item_id, inventory_slot, trade_stack, quality
		  FROM avatar_vault_items
		 WHERE avatar_id = ANY($1) AND item_id IS NOT NULL
		 UNION ALL
		SELECT avatar_id, item_id, inventory_slot, trade_stack, quality
		  FROM avatar_inventory_items
		 WHERE avatar_id = ANY($1) AND item_id IS NOT NULL`, avatarIDs)
	if err != nil {
		return out
	}
	type vrow struct {
		avatarID int64
		itemID   int
		slot     int
		stack    *int
		quality  *float64
	}
	var vrows []vrow
	var itemIDs []int
	for rows.Next() {
		var v vrow
		if err := rows.Scan(&v.avatarID, &v.itemID, &v.slot, &v.stack, &v.quality); err != nil {
			rows.Close()
			return out
		}
		vrows = append(vrows, v)
		itemIDs = append(itemIDs, v.itemID)
	}
	rows.Close()

	items, err := s.resolveItems(ctx, itemIDs)
	if err != nil {
		return out
	}

	for _, v := range vrows {
		tpl, ok := items[v.itemID]
		if !ok || tpl.view.NoTrade {
			continue // untradable items are not listable
		}
		stack := 1
		if v.stack != nil && *v.stack > 0 {
			stack = *v.stack
		}
		name := nameByID[v.avatarID]
		out[name] = append(out[name], VaultSlotView{
			Slot:    v.slot,
			Item:    tpl.itemView(v.quality),
			Stack:   stack,
			Quality: v.quality,
		})
	}
	for name := range out {
		slots := out[name]
		sort.SliceStable(slots, func(i, j int) bool { return slots[i].Slot < slots[j].Slot })
		out[name] = slots
	}
	return out
}
