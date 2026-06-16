// SPDX-License-Identifier: MIT
// Freya Online -- Auction House writes (post / bid / buyout / expiry sweep).
//
// All money movement runs inside a single net7_user transaction with row locks
// (SELECT ... FOR UPDATE) so two concurrent bids / a bid racing the sweeper
// cannot double-spend or double-deliver. The server NEVER trusts client-supplied
// prices: item_value, the 1% min increment, and the 10% fee are recomputed from
// item_base + the actual stored instance, not from the request body.
//
// Credit model (documented; flagged as an owner question in plans/44):
//   - The account's PRIMARY live character (lowest slot) is the wallet for
//     website-initiated debits (deposit, bid, buyout).
//   - Proceeds, refunds, and won/returned items are DELIVERED to the mailbox and
//     looted into a character later -- they are never silently credited.
//
// Economics (per spec):
//   - 10% fee on item_value collected UP FRONT as deposit when listing.
//   - Sale: winner pays the held bid/buyout; seller receives it in full (the fee
//     was already taken); the item is delivered to the winner's mailbox.
//   - Unsold at expiry: item returned to seller's mailbox; 95% of the deposit
//     refunded (5% kept). Bids already held are refunded when outbid / on buyout.

package main

import (
	"context"
	"errors"
	"fmt"
	"math"
	"time"

	"github.com/jackc/pgx/v5"
)

var (
	errListingGone     = errors.New("listing not available")
	errOwnListing      = errors.New("cannot bid on your own listing")
	errNoBuyout        = errors.New("listing has no buyout")
	errInsufficient    = errors.New("insufficient credits")
	errNoCharacter     = errors.New("no live character on this account")
	errItemNotFound    = errors.New("item not found in storage")
	errItemUntradable  = errors.New("item is not tradable")
	errBadInput        = errors.New("invalid listing parameters")
	errCharacterOnline = errors.New("character is online; log it out to manage its credits, items, or mail")
)

// wonSubject builds the "Auction won" mail subject, naming the item (and its
// stack count when > 1) so the inbox shows what was won at a glance. Falls back
// to the bare subject if the name could not be resolved.
func wonSubject(name string, stack int) string {
	if name == "" {
		return "Auction won"
	}
	if stack > 1 {
		return fmt.Sprintf("Auction won - %s x%d", name, stack)
	}
	return "Auction won - " + name
}

// assertAvatarOffline is the offline-guard (plans/44, AQ open question #1). The
// AH/mail tables are shared with the live game, but the game server is a SECOND
// authority: while a character is in-game the server holds that character's
// credits and vault in memory and writes them back to the DB on logout (and on
// boot it force-resets stale-online rows, ServerManager.cpp). So a website debit
// or vault mutation applied to an ONLINE character is a lost update -- the
// server's in-memory copy overwrites it on save, handing the player a free item
// or a double-spend.
//
// The server's own definition of online is last_login > last_logout
// (AccountManager.h). We SELECT ... FOR UPDATE the avatar_info row so this check
// serializes against the server's last_login UPDATE at login: if a login is
// in-flight we block until it commits, then read the fresh (online) state and
// refuse; if we win the lock the character cannot transition to online until our
// transaction commits. Caller must hold the tx.
//
// Mailbox DELIVERY (deliverItem/deliverCredits) is deliberately NOT guarded: it
// writes only to the mailbox tables, which the game does not hold in memory, so
// a sale can settle to an online winner's mailbox and be looted later (looting
// IS guarded, because it touches the character's vault/wallet).
func assertAvatarOffline(ctx context.Context, tx pgx.Tx, avatarID int64) error {
	var offline bool
	err := tx.QueryRow(ctx,
		`SELECT last_login <= last_logout FROM avatar_info WHERE avatar_id = $1 FOR UPDATE`,
		avatarID).Scan(&offline)
	if errors.Is(err, pgx.ErrNoRows) {
		return errNoCharacter
	}
	if err != nil {
		return err
	}
	if !offline {
		return errCharacterOnline
	}
	return nil
}

// durationHours maps the SPA's duration token to listing hours.
func durationHours(d string) (int, bool) {
	switch d {
	case "short":
		return 8, true
	case "med":
		return 12, true
	case "long":
		return 24, true
	}
	return 0, false
}

// primaryAvatar returns the lowest-slot live avatar for an account (the wallet /
// default recipient). Caller must hold the tx.
func primaryAvatar(ctx context.Context, tx pgx.Tx, accountID int64) (int64, string, error) {
	var id int64
	var name string
	err := tx.QueryRow(ctx, `
		SELECT ai.avatar_id,
		       trim(both '_' from coalesce(ad.first_name,'') || '_' || coalesce(ad.last_name,''))
		  FROM avatar_info ai
		  JOIN avatar_data ad ON ad.avatar_id = ai.avatar_id
		 WHERE ai.account_id = $1 AND ai.deleted_at IS NULL
		 ORDER BY ai.slot ASC
		 LIMIT 1`, accountID).Scan(&id, &name)
	if errors.Is(err, pgx.ErrNoRows) {
		return 0, "", errNoCharacter
	}
	return id, name, err
}

// debitAvatar deducts amount from an avatar's credits, locking the row. Fails
// with errInsufficient if the balance would go negative.
func debitAvatar(ctx context.Context, tx pgx.Tx, avatarID, amount int64) error {
	var bal int64
	err := tx.QueryRow(ctx,
		`SELECT credits::bigint FROM avatar_level_info WHERE avatar_id = $1 FOR UPDATE`,
		avatarID).Scan(&bal)
	if err != nil {
		return err
	}
	if bal < amount {
		return errInsufficient
	}
	_, err = tx.Exec(ctx,
		`UPDATE avatar_level_info SET credits = credits - $2 WHERE avatar_id = $1`,
		avatarID, amount)
	return err
}

// minIncrement is the bid step: 1% of item_value, at least 1.
func minIncrement(itemValue int64) int64 {
	inc := int64(math.Ceil(float64(itemValue) * 0.01))
	if inc < 1 {
		return 1
	}
	return inc
}

// PostListing lists an item from a character's vault/inventory. input mirrors the
// SPA PostListingInput; prices in it are advisory -- the server recomputes value,
// deposit, and the increment floor from authoritative data.
func (s *Store) PostListing(ctx context.Context, accountID int64, itemID, slot, stack int,
	avatarName, duration string, minBid, buyout int64) (MyListingView, error) {

	hours, ok := durationHours(duration)
	if !ok || stack < 1 {
		return MyListingView{}, errBadInput
	}

	// Tradability + vendor value come from the content DB (separate pool).
	tpl, err := s.resolveItems(ctx, []int{itemID})
	if err != nil {
		return MyListingView{}, err
	}
	t, ok := tpl[itemID]
	if !ok {
		return MyListingView{}, errItemNotFound
	}
	if t.view.NoTrade {
		return MyListingView{}, errItemUntradable
	}
	vendor := t.view.Vendor

	if minBid < 1 {
		minBid = 1
	}

	expires := time.Now().Add(time.Duration(hours) * time.Hour)
	var (
		listingID int64
		quality   *float64
		buyoutPtr *int64
	)
	err = s.serialTx(ctx, func(tx pgx.Tx) error {
		// The named character must belong to this account and be live.
		sellerAvatar, err := ownedAvatarByName(ctx, tx, accountID, avatarName)
		if err != nil {
			return err
		}

		// Offline-guard: listing debits the seller's credits and removes the item
		// from its vault -- both are server-authoritative while online.
		if err := assertAvatarOffline(ctx, tx, sellerAvatar); err != nil {
			return err
		}

		// Lock and read the actual stored instance, then remove it from storage.
		// Try the vault first, then inventory.
		var (
			cost      *int64
			builder   *string
			structure *float64
			haveStack *int
			fromTable string
		)
		quality = nil
		for _, tbl := range []string{"avatar_vault_items", "avatar_inventory_items"} {
			q := fmt.Sprintf(`
				SELECT trade_stack, quality, cost, builder_name, structure
				  FROM %s
				 WHERE avatar_id = $1 AND inventory_slot = $2 AND item_id = $3
				 FOR UPDATE`, tbl)
			err = tx.QueryRow(ctx, q, sellerAvatar, slot, itemID).
				Scan(&haveStack, &quality, &cost, &builder, &structure)
			if err == nil {
				fromTable = tbl
				break
			}
			if !errors.Is(err, pgx.ErrNoRows) {
				return err
			}
		}
		if fromTable == "" {
			return errItemNotFound
		}

		storedStack := 1
		if haveStack != nil && *haveStack > 0 {
			storedStack = *haveStack
		}
		if stack > storedStack {
			return errBadInput
		}

		// item_value = vendor value for the listed quantity. AR-2: guard the
		// multiply -- an absurd catalogue price * stack could overflow int64 and
		// wrap negative, corrupting the deposit/credit math. Reject rather than
		// wrap. (stack is already bounded by storedStack, so this is defensive.)
		itemValue := vendor * int64(stack)
		if vendor != 0 && itemValue/vendor != int64(stack) {
			return errBadInput
		}
		deposit := int64(math.Ceil(float64(itemValue) * 0.10))

		// Validate the seller-chosen buyout against the recomputed floor.
		buyoutPtr = nil
		if buyout > 0 {
			if buyout <= minBid {
				return errBadInput
			}
			buyoutPtr = &buyout
		}

		// Collect the 10% fee up front from the listing character.
		if err := debitAvatar(ctx, tx, sellerAvatar, deposit); err != nil {
			return err
		}

		// Remove the listed quantity from storage (delete the slot, or decrement a
		// partial stack -- keeping stack_level and trade_stack in step).
		if stack >= storedStack {
			_, err = tx.Exec(ctx,
				fmt.Sprintf(`DELETE FROM %s WHERE avatar_id = $1 AND inventory_slot = $2`, fromTable),
				sellerAvatar, slot)
		} else {
			_, err = tx.Exec(ctx,
				fmt.Sprintf(`UPDATE %s SET trade_stack = trade_stack - $3,
				                stack_level = GREATEST(coalesce(stack_level, trade_stack) - $3, 0)
				             WHERE avatar_id = $1 AND inventory_slot = $2`, fromTable),
				sellerAvatar, slot, stack)
		}
		if err != nil {
			return err
		}

		return tx.QueryRow(ctx, `
			INSERT INTO auction_listings
			  (seller_avatar_id, seller_account_id, item_id, stack_size, quality,
			   item_cost, builder_name, structure, item_value, start_bid, current_bid,
			   buyout, deposit_paid, status, expires_at)
			VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$10,$11,$12,0,$13)
			RETURNING id`,
			sellerAvatar, accountID, itemID, stack, quality,
			cost, builder, structure, itemValue, minBid,
			buyoutPtr, deposit, expires).Scan(&listingID)
	})
	if err != nil {
		return MyListingView{}, err
	}

	return MyListingView{
		ID:      fmt.Sprintf("%d", listingID),
		Item:    t.itemView(quality),
		Avatar:  avatarName,
		Stack:   stack,
		Quality: quality,
		Band:    computeBand(time.Until(expires)),
		Buyout:  buyoutPtr,
	}, nil
}

// PlaceBid bids the next increment on a listing. The first bid lands at
// start_bid; each subsequent bid adds max(1, 1% of item_value).
func (s *Store) PlaceBid(ctx context.Context, accountID int64, listingID int64) (ListingView, error) {
	err := s.serialTx(ctx, func(tx pgx.Tx) error {
		var (
			sellerAccount  int64
			itemValue      int64
			startBid       int64
			currentBid     int64
			hadBidder      bool
			prevBidderAcct *int64
			expiresAt      time.Time
		)
		err := tx.QueryRow(ctx, `
			SELECT seller_account_id, item_value, start_bid, current_bid,
			       high_bidder_avatar_id IS NOT NULL, high_bidder_account_id, expires_at
			  FROM auction_listings
			 WHERE id = $1 AND status = 0
			 FOR UPDATE`, listingID).
			Scan(&sellerAccount, &itemValue, &startBid, &currentBid, &hadBidder, &prevBidderAcct, &expiresAt)
		if errors.Is(err, pgx.ErrNoRows) {
			return errListingGone
		}
		if err != nil {
			return err
		}
		if time.Now().After(expiresAt) {
			return errListingGone
		}
		if sellerAccount == accountID {
			return errOwnListing
		}

		bidder, _, err := primaryAvatar(ctx, tx, accountID)
		if err != nil {
			return err
		}
		// Offline-guard: the bid debits the bidder's credits.
		if err := assertAvatarOffline(ctx, tx, bidder); err != nil {
			return err
		}

		// First bid lands at start_bid; later bids add the increment.
		newBid := startBid
		if hadBidder {
			newBid = currentBid + minIncrement(itemValue)
		}

		if err := debitAvatar(ctx, tx, bidder, newBid); err != nil {
			return err
		}

		// Refund the previous high bidder via mailbox credits.
		if hadBidder && prevBidderAcct != nil {
			if err := deliverCredits(ctx, tx, *prevBidderAcct, "Auction House",
				"Outbid refund", "You were outbid; your bid has been refunded.", currentBid); err != nil {
				return err
			}
		}

		if _, err := tx.Exec(ctx, `
			UPDATE auction_listings
			   SET current_bid = $2, high_bidder_avatar_id = $3, high_bidder_account_id = $4
			 WHERE id = $1`, listingID, newBid, bidder, accountID); err != nil {
			return err
		}
		_, err = tx.Exec(ctx, `
			INSERT INTO auction_bids (listing_id, bidder_avatar_id, bidder_account_id, amount)
			VALUES ($1,$2,$3,$4)`, listingID, bidder, accountID, newBid)
		return err
	})
	if err != nil {
		return ListingView{}, err
	}

	return s.singleListing(ctx, listingID), nil
}

// botPlaceBid places a faucet bid from the AhBot account on a PLAYER listing. It
// mirrors the no-bidder branch of PlaceBid but, like the listing faucet
// (botPostOne), moves no money out of the AhBot wallet: the AhBot is an NPC
// liquidity source, not a player, so it pays nothing up front (its seeded wallet
// is 0 -- see freya_online_bots.sql). If it ends up the winning bidder at expiry,
// resolveOneExpired pays the SELLER the standing bid (deliverCredits) -- the
// faucet buying the player's item -- and the won item is delivered to AhBot's
// mailbox and never looted (the mail sweeper reaps it), exactly as faucet sales
// already work.
//
// Eligibility is re-validated UNDER THE ROW LOCK so the AhBot can never race
// ahead of, or outbid, a real player: it bids only when the listing is still
// active, unexpired, not AhBot's own, and still has no high bidder. A bid that
// arrived between the sweep read and this lock yields errListingGone (callers
// treat that as a benign miss, not an error).
func (s *Store) botPlaceBid(ctx context.Context, listingID int64) error {
	return s.serialTx(ctx, func(tx pgx.Tx) error {
		var (
			sellerAccount int64
			startBid      int64
			hadBidder     bool
			expiresAt     time.Time
		)
		err := tx.QueryRow(ctx, `
			SELECT seller_account_id, start_bid,
			       high_bidder_avatar_id IS NOT NULL, expires_at
			  FROM auction_listings
			 WHERE id = $1 AND status = 0
			 FOR UPDATE`, listingID).
			Scan(&sellerAccount, &startBid, &hadBidder, &expiresAt)
		if errors.Is(err, pgx.ErrNoRows) {
			return errListingGone
		}
		if err != nil {
			return err
		}
		if time.Now().After(expiresAt) {
			return errListingGone
		}
		if sellerAccount == ahBotAccountID {
			return errOwnListing
		}
		// Never outbid: a real bid landed since the sweep read. Stand down.
		if hadBidder {
			return errListingGone
		}

		// The AhBot's first (and only) bid lands at start_bid. No wallet debit.
		if _, err := tx.Exec(ctx, `
			UPDATE auction_listings
			   SET current_bid = $2, high_bidder_avatar_id = $3, high_bidder_account_id = $4
			 WHERE id = $1`, listingID, startBid, ahBotAvatarID, ahBotAccountID); err != nil {
			return err
		}
		_, err = tx.Exec(ctx, `
			INSERT INTO auction_bids (listing_id, bidder_avatar_id, bidder_account_id, amount)
			VALUES ($1,$2,$3,$4)`, listingID, ahBotAvatarID, ahBotAccountID, startBid)
		return err
	})
}

// Buyout buys a listing outright at its buyout price.
func (s *Store) Buyout(ctx context.Context, accountID int64, listingID int64) error {
	return s.serialTx(ctx, func(tx pgx.Tx) error {
		var (
			sellerAccount  int64
			sellerAvatar   int64
			itemID         int
			stack          int
			quality        *float64
			cost           *int64
			builder        *string
			structure      *float64
			buyout         *int64
			currentBid     int64
			prevBidderAcct *int64
			hadBidder      bool
			expiresAt      time.Time
		)
		err := tx.QueryRow(ctx, `
			SELECT seller_account_id, seller_avatar_id, item_id, stack_size, quality,
			       item_cost, builder_name, structure, buyout, current_bid,
			       high_bidder_account_id, high_bidder_avatar_id IS NOT NULL, expires_at
			  FROM auction_listings
			 WHERE id = $1 AND status = 0
			 FOR UPDATE`, listingID).
			Scan(&sellerAccount, &sellerAvatar, &itemID, &stack, &quality, &cost, &builder,
				&structure, &buyout, &currentBid, &prevBidderAcct, &hadBidder, &expiresAt)
		if errors.Is(err, pgx.ErrNoRows) {
			return errListingGone
		}
		if err != nil {
			return err
		}
		if time.Now().After(expiresAt) {
			return errListingGone
		}
		if buyout == nil {
			return errNoBuyout
		}
		if sellerAccount == accountID {
			return errOwnListing
		}

		buyer, _, err := primaryAvatar(ctx, tx, accountID)
		if err != nil {
			return err
		}
		// Offline-guard: the buyout debits the buyer's credits.
		if err := assertAvatarOffline(ctx, tx, buyer); err != nil {
			return err
		}
		if err := debitAvatar(ctx, tx, buyer, *buyout); err != nil {
			return err
		}

		// Refund a standing high bidder (if any).
		if hadBidder && prevBidderAcct != nil {
			if err := deliverCredits(ctx, tx, *prevBidderAcct, "Auction House",
				"Outbid refund", "A buyout ended the auction; your bid has been refunded.", currentBid); err != nil {
				return err
			}
		}

		// Deliver the item to the buyer and the proceeds to the seller.
		if err := deliverItem(ctx, tx, accountID, buyer, "Auction House",
			wonSubject(s.itemName(ctx, itemID), stack), "You bought this item via buyout.",
			itemID, stack, quality, cost, builder, structure); err != nil {
			return err
		}
		if err := deliverCredits(ctx, tx, sellerAccount, "Auction House",
			"Auction sold", "Your item sold via buyout.", *buyout); err != nil {
			return err
		}

		_, err = tx.Exec(ctx,
			`UPDATE auction_listings SET status = 1, resolved_at = now() WHERE id = $1`, listingID)
		return err
	})
}

// singleListing reloads one listing as a view (best-effort, post-commit).
func (s *Store) singleListing(ctx context.Context, listingID int64) ListingView {
	rows, err := s.user.Query(ctx, `
		SELECT `+listingSelectCols+` FROM auction_listings WHERE id = $1`, listingID)
	if err != nil {
		return ListingView{}
	}
	lr, err := scanListings(rows)
	if err != nil || len(lr) == 0 {
		return ListingView{}
	}
	items, names := s.enrich(ctx, lr)
	r := lr[0]
	var highBidder *string
	if r.highBidderAvatar != nil {
		if n, ok := names[*r.highBidderAvatar]; ok {
			highBidder = &n
		}
	}
	return ListingView{
		ID:         fmt.Sprintf("%d", r.id),
		Item:       items[r.itemID].itemView(r.quality),
		Seller:     names[r.sellerAvatarID],
		Stack:      r.stackSize,
		Quality:    r.quality,
		Band:       computeBand(time.Until(r.expiresAt)),
		Bid:        r.currentBid,
		Buyout:     r.buyout,
		HighBidder: highBidder,
	}
}

// sweepExpiredAuctions resolves every active listing past its expiry: a standing
// high bid wins (item to winner, proceeds to seller); no bids => unsold (item
// returned to seller, 95% of deposit refunded).
func (s *Store) sweepExpiredAuctions(ctx context.Context) (int, error) {
	rows, err := s.user.Query(ctx, `
		SELECT id FROM auction_listings WHERE status = 0 AND expires_at < now()`)
	if err != nil {
		return 0, err
	}
	var ids []int64
	for rows.Next() {
		var id int64
		if err := rows.Scan(&id); err != nil {
			rows.Close()
			return 0, err
		}
		ids = append(ids, id)
	}
	rows.Close()

	resolved := 0
	for _, id := range ids {
		if err := s.resolveOneExpired(ctx, id); err != nil {
			return resolved, err
		}
		resolved++
	}
	return resolved, nil
}

func (s *Store) resolveOneExpired(ctx context.Context, listingID int64) error {
	return s.serialTx(ctx, func(tx pgx.Tx) error {
		var (
			sellerAccount int64
			itemID        int
			stack         int
			quality       *float64
			cost          *int64
			builder       *string
			structure     *float64
			currentBid    int64
			deposit       int64
			winnerAccount *int64
			hadBidder     bool
		)
		err := tx.QueryRow(ctx, `
			SELECT seller_account_id, item_id, stack_size, quality, item_cost,
			       builder_name, structure, current_bid, deposit_paid,
			       high_bidder_account_id, high_bidder_avatar_id IS NOT NULL
			  FROM auction_listings
			 WHERE id = $1 AND status = 0 AND expires_at < now()
			 FOR UPDATE`, listingID).
			Scan(&sellerAccount, &itemID, &stack, &quality, &cost, &builder, &structure,
				&currentBid, &deposit, &winnerAccount, &hadBidder)
		if errors.Is(err, pgx.ErrNoRows) {
			return nil // already resolved by a racing path
		}
		if err != nil {
			return err
		}

		if hadBidder && winnerAccount != nil {
			// Sale at the standing bid (already held from the winner).
			if err := deliverItem(ctx, tx, *winnerAccount, 0, "Auction House",
				wonSubject(s.itemName(ctx, itemID), stack), "You won this auction.",
				itemID, stack, quality, cost, builder, structure); err != nil {
				return err
			}
			if err := deliverCredits(ctx, tx, sellerAccount, "Auction House",
				"Auction sold", "Your item sold at auction.", currentBid); err != nil {
				return err
			}
			_, err = tx.Exec(ctx,
				`UPDATE auction_listings SET status = 1, resolved_at = now() WHERE id = $1`, listingID)
			return err
		}

		// Unsold: return the item, refund 95% of the deposit (5% kept).
		refund := int64(math.Floor(float64(deposit) * 0.95))
		if err := deliverItem(ctx, tx, sellerAccount, 0, "Auction House",
			"Auction expired", "Your item did not sell and has been returned.",
			itemID, stack, quality, cost, builder, structure); err != nil {
			return err
		}
		if refund > 0 {
			if err := deliverCredits(ctx, tx, sellerAccount, "Auction House",
				"Deposit refund", "95% of your listing deposit has been returned.", refund); err != nil {
				return err
			}
		}
		_, err = tx.Exec(ctx,
			`UPDATE auction_listings SET status = 2, resolved_at = now() WHERE id = $1`, listingID)
		return err
	})
}
