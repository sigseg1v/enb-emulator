// SPDX-License-Identifier: MIT
// Freya Online -- vault item transfer between two of an account's own characters.
//
// The Vault page lets a player move an item from one of their characters' vaults
// into another's. Both sides are server-authoritative while the character is
// online, so BOTH the source and destination characters are offline-guarded, and
// the whole move (remove from source slot + insert into the destination's first
// free slot) runs in ONE serializable transaction (serialTx) with row locks --
// an item can never be in both vaults at once, nor vanish, regardless of
// concurrent web mutations.

package main

import (
	"context"
	"errors"
	"sort"

	"github.com/jackc/pgx/v5"
)

var (
	errSameCharacter     = errors.New("source and destination characters must be different")
	errCharacterNotFound = errors.New("character not found on this account")
	errDestVaultFull     = errors.New("destination vault is full")
)

// ownedAvatarByName resolves a live avatar by its display name (first_last),
// requiring it to belong to accountID. Caller holds the tx. errCharacterNotFound
// if the account has no such live character.
func ownedAvatarByName(ctx context.Context, tx pgx.Tx, accountID int64, name string) (int64, error) {
	var id int64
	err := tx.QueryRow(ctx, `
		SELECT ai.avatar_id
		  FROM avatar_info ai
		  JOIN avatar_data ad ON ad.avatar_id = ai.avatar_id
		 WHERE ai.account_id = $1 AND ai.deleted_at IS NULL
		   AND trim(both '_' from coalesce(ad.first_name,'') || '_' || coalesce(ad.last_name,'')) = $2`,
		accountID, name).Scan(&id)
	if errors.Is(err, pgx.ErrNoRows) {
		return 0, errCharacterNotFound
	}
	return id, err
}

// vaultInstance is one stored vault row's full item-instance payload, read under
// a FOR UPDATE lock so the move is consistent.
type vaultInstance struct {
	itemID    int
	stack     int
	quality   *float64
	cost      *int64
	builder   *string
	structure *float64
}

// lockVaultSlot reads and row-locks a single vault slot, verifying it currently
// holds wantItemID (optimistic check: the SPA sends the item id it last saw, so
// a slot that changed underneath returns errItemNotFound rather than moving the
// wrong thing). Caller holds the tx.
func lockVaultSlot(ctx context.Context, tx pgx.Tx, avatarID int64, slot, wantItemID int) (vaultInstance, error) {
	var (
		vi       vaultInstance
		itemID   *int
		tradeS   *int
		stackLvl *int
	)
	err := tx.QueryRow(ctx, `
		SELECT item_id, trade_stack, stack_level, quality, cost, builder_name, structure
		  FROM avatar_vault_items
		 WHERE avatar_id = $1 AND inventory_slot = $2
		 FOR UPDATE`, avatarID, slot).
		Scan(&itemID, &tradeS, &stackLvl, &vi.quality, &vi.cost, &vi.builder, &vi.structure)
	if errors.Is(err, pgx.ErrNoRows) {
		return vaultInstance{}, errItemNotFound
	}
	if err != nil {
		return vaultInstance{}, err
	}
	if itemID == nil || *itemID != wantItemID {
		return vaultInstance{}, errItemNotFound
	}
	vi.itemID = *itemID
	// The game writes stack_level and trade_stack equal for a stack; prefer
	// trade_stack, fall back to stack_level, floor at 1 (a single, unstacked item
	// leaves both NULL/0 -- it still counts as one).
	vi.stack = 1
	if tradeS != nil && *tradeS > 0 {
		vi.stack = *tradeS
	} else if stackLvl != nil && *stackLvl > 0 {
		vi.stack = *stackLvl
	}
	return vi, nil
}

// insertVaultItem drops an item instance into a specific (free) vault slot,
// writing stack_level and trade_stack equal so the game loader reports the
// correct StackCount (PlayerSaves.cpp SaveVaultChange writes them equal too).
func insertVaultItem(ctx context.Context, tx pgx.Tx, avatarID int64, slot int, vi vaultInstance) error {
	_, err := tx.Exec(ctx, `
		INSERT INTO avatar_vault_items
		  (avatar_id, item_id, inventory_slot, stack_level, trade_stack, quality, cost, builder_name, structure)
		VALUES ($1, $2, $3, $4, $4, $5, $6, $7, $8)`,
		avatarID, vi.itemID, slot, vi.stack, vi.quality, vi.cost, vi.builder, vi.structure)
	return err
}

// guardOffline offline-guards a set of avatars, locking their avatar_info rows in
// ascending id order so two transfers touching the same pair of characters in
// opposite directions cannot deadlock on the guard. Caller holds the tx.
func guardOffline(ctx context.Context, tx pgx.Tx, avatarIDs ...int64) error {
	ordered := append([]int64(nil), avatarIDs...)
	sort.Slice(ordered, func(i, j int) bool { return ordered[i] < ordered[j] })
	for _, id := range ordered {
		if err := assertAvatarOffline(ctx, tx, id); err != nil {
			return err
		}
	}
	return nil
}

// TransferVaultItem moves the whole stack in fromName's vault slot into the first
// free slot of toName's vault. Both characters must belong to accountID, must be
// different, and must be offline. The item id is cross-checked against the slot.
func (s *Store) TransferVaultItem(ctx context.Context, accountID int64,
	fromName, toName string, slot, itemID int) error {

	if fromName == toName {
		return errSameCharacter
	}

	return s.serialTx(ctx, func(tx pgx.Tx) error {
		fromAvatar, err := ownedAvatarByName(ctx, tx, accountID, fromName)
		if err != nil {
			return err
		}
		toAvatar, err := ownedAvatarByName(ctx, tx, accountID, toName)
		if err != nil {
			return err
		}
		if fromAvatar == toAvatar {
			return errSameCharacter
		}

		// Both vaults mutate -- guard both (lock-ordered to avoid deadlock).
		if err := guardOffline(ctx, tx, fromAvatar, toAvatar); err != nil {
			return err
		}

		vi, err := lockVaultSlot(ctx, tx, fromAvatar, slot, itemID)
		if err != nil {
			return err
		}

		destSlot, err := freeVaultSlot(ctx, tx, toAvatar)
		if errors.Is(err, errNoVaultSpace) {
			return errDestVaultFull
		}
		if err != nil {
			return err
		}

		if err := insertVaultItem(ctx, tx, toAvatar, destSlot, vi); err != nil {
			return err
		}
		_, err = tx.Exec(ctx,
			`DELETE FROM avatar_vault_items WHERE avatar_id = $1 AND inventory_slot = $2`,
			fromAvatar, slot)
		return err
	})
}
