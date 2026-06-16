// SPDX-License-Identifier: MIT
// Freya Online -- AhBot auto-bid integration tests (real Postgres, two-DB schema).
//
// These exercise the live bid path the 30-minute loop drives: botBidSweep selects
// eligible player listings and rolls botBidChance; botPlaceBid commits a faucet
// bid under a row lock. Gated on FREYA_TEST_DB (see itest_helpers_test.go).

package main

import (
	"context"
	"strconv"
	"testing"
)

// listingBidState reads the fields that prove who (if anyone) holds a listing.
func listingBidState(t *testing.T, st *Store, ctx context.Context, listingID int64) (currentBid, startBid int64, highBidderAcct *int64) {
	t.Helper()
	if err := st.user.QueryRow(ctx,
		`SELECT current_bid, start_bid, high_bidder_account_id
		   FROM auction_listings WHERE id = $1`, listingID).
		Scan(&currentBid, &startBid, &highBidderAcct); err != nil {
		t.Fatalf("listingBidState: %v", err)
	}
	return
}

// postPlayerListing seeds a tradable item into a seller's vault and lists it via
// the real PostListing path. minBid sets the opening bid (== start_bid).
func postPlayerListing(t *testing.T, st *Store, ctx context.Context, seller testAccount, vaultSlot, minBid int) int64 {
	t.Helper()
	itemID, _ := pickTradableItem(t, st, ctx, false)
	seedVaultItem(t, st, ctx, seller.avatars[0].id, vaultSlot, itemID, 1, nil)
	lv, err := st.PostListing(ctx, seller.id, itemID, vaultSlot, 1,
		seller.avatars[0].name, "short", int64(minBid), 0)
	if err != nil {
		t.Fatalf("PostListing: %v", err)
	}
	id, err := strconv.ParseInt(lv.ID, 10, 64)
	if err != nil {
		t.Fatalf("parse listing id %q: %v", lv.ID, err)
	}
	return id
}

// TestIT_BotPlaceBid_BecomesHighBidder_NoWalletDebit is the deterministic core:
// a faucet bid makes the AhBot the high bidder at start_bid, records the bid, and
// moves NO credits out of the AhBot wallet.
func TestIT_BotPlaceBid_BecomesHighBidder_NoWalletDebit(t *testing.T) {
	st, ctx := testStore(t)
	seller := seedAccount(t, st, ctx, 1, 1, 10_000_000)

	listingID := postPlayerListing(t, st, ctx, seller, 0, 1)

	walletBefore := creditsOf(t, st, ctx, ahBotAvatarID)

	if err := st.botPlaceBid(ctx, listingID); err != nil {
		t.Fatalf("botPlaceBid: %v", err)
	}

	cur, start, high := listingBidState(t, st, ctx, listingID)
	if high == nil || *high != ahBotAccountID {
		t.Fatalf("high bidder = %v, want AhBot account %d", high, ahBotAccountID)
	}
	if cur != start {
		t.Fatalf("current_bid = %d, want start_bid %d (first bid lands at start_bid)", cur, start)
	}

	var bidCount int
	if err := st.user.QueryRow(ctx,
		`SELECT count(*) FROM auction_bids WHERE listing_id=$1 AND bidder_account_id=$2`,
		listingID, ahBotAccountID).Scan(&bidCount); err != nil {
		t.Fatalf("count bids: %v", err)
	}
	if bidCount != 1 {
		t.Fatalf("AhBot bid rows = %d, want 1", bidCount)
	}

	if walletAfter := creditsOf(t, st, ctx, ahBotAvatarID); walletAfter != walletBefore {
		t.Fatalf("AhBot wallet moved: %d -> %d (faucet must not debit)", walletBefore, walletAfter)
	}
}

// TestIT_BotPlaceBid_NeverOutbidsAPlayer: once a real player holds the high bid,
// the AhBot stands down (the under-lock hadBidder guard => errListingGone), so it
// can never outbid a human.
func TestIT_BotPlaceBid_NeverOutbidsAPlayer(t *testing.T) {
	st, ctx := testStore(t)
	seller := seedAccount(t, st, ctx, 1, 1, 10_000_000)
	bidder := seedAccount(t, st, ctx, 2, 1, 10_000_000)

	listingID := postPlayerListing(t, st, ctx, seller, 0, 1)

	if _, err := st.PlaceBid(ctx, bidder.id, listingID); err != nil {
		t.Fatalf("player PlaceBid: %v", err)
	}

	err := st.botPlaceBid(ctx, listingID)
	if err != errListingGone {
		t.Fatalf("botPlaceBid over a player bid = %v, want errListingGone (never outbid)", err)
	}

	_, _, high := listingBidState(t, st, ctx, listingID)
	if high == nil || *high != bidder.id {
		t.Fatalf("high bidder = %v, want the player account %d (unchanged)", high, bidder.id)
	}
}

// TestIT_BotBidSweep_BidsCheapIgnoresOverpriced drives the actual sweep the loop
// calls: it must bid on at least one dirt-cheap listing (chance 20% each) and must
// NEVER touch a listing priced far above 120% of the AhBot's own number (chance 0).
func TestIT_BotBidSweep_BidsCheapIgnoresOverpriced(t *testing.T) {
	st, ctx := testStore(t)
	seller := seedAccount(t, st, ctx, 1, 1, 1_000_000_000)

	// One deliberately overpriced listing: minBid is ~1000x the item value, so the
	// ratio is far above 1.20 and botBidChance returns exactly 0 -> never bid.
	itemID, buyingPrice := pickTradableItem(t, st, ctx, false)
	seedVaultItem(t, st, ctx, seller.avatars[0].id, 0, itemID, 1, nil)
	overLV, err := st.PostListing(ctx, seller.id, itemID, 0, 1,
		seller.avatars[0].name, "short", int64(buyingPrice)*1000+1, 0)
	if err != nil {
		t.Fatalf("PostListing overpriced: %v", err)
	}
	overID, err := strconv.ParseInt(overLV.ID, 10, 64)
	if err != nil {
		t.Fatalf("parse overpriced id: %v", err)
	}

	// Many dirt-cheap listings (start_bid 1 << AhBot min bid -> ratio ~0 -> 20%
	// each). With N=40 the chance the sweep bids on none is 0.8^40 ~ 1.3e-4.
	const N = 40
	cheapIDs := make([]int64, 0, N)
	for i := 0; i < N; i++ {
		cheapIDs = append(cheapIDs, postPlayerListing(t, st, ctx, seller, i+1, 1))
	}

	botBidSweep(ctx, st)

	// The overpriced listing must still have no bidder.
	if _, _, high := listingBidState(t, st, ctx, overID); high != nil {
		t.Fatalf("overpriced listing got a bidder %d; must be ignored (>120%%)", *high)
	}

	// At least one cheap listing must now be held by the AhBot.
	var held int
	for _, id := range cheapIDs {
		if _, _, high := listingBidState(t, st, ctx, id); high != nil && *high == ahBotAccountID {
			held++
		}
	}
	if held == 0 {
		t.Fatalf("AhBot bid on 0/%d cheap listings; expected ~%d", N, N/5)
	}
	t.Logf("AhBot bid on %d/%d cheap listings (expected ~%d), ignored the overpriced one", held, N, N/5)
}
