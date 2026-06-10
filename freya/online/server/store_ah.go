// SPDX-License-Identifier: MIT
// Freya Online -- Auction House reads.
//
// Listings live in net7_user.auction_listings; item templates live in
// net7.item_base. We read the listings, batch-resolve their item_ids against the
// content pool, resolve seller names from avatar_data, and assemble the SPA view.
//
// Time remaining is deliberately surfaced only as an opaque band (low/med/high),
// and listings are NOT ordered by exact expiry, so a bidder cannot infer the
// precise end time within a band (anti-snipe / anti-abuse, per the spec).

package main

import (
	"context"
	"sort"
	"strconv"
	"time"
)

// ListingView mirrors the SPA Listing interface.
type ListingView struct {
	ID         string   `json:"id"`
	Item       ItemView `json:"item"`
	Seller     string   `json:"seller"`
	Stack      int      `json:"stack"`
	Quality    *float64 `json:"quality"`
	Band       string   `json:"band"`
	Bid        int64    `json:"bid"`
	Buyout     *int64   `json:"buyout"`
	HighBidder *string  `json:"highBidder"`
}

// MyListingView mirrors the SPA MyListing interface.
type MyListingView struct {
	ID         string   `json:"id"`
	Item       ItemView `json:"item"`
	Avatar     string   `json:"avatar"`
	Stack      int      `json:"stack"`
	Quality    *float64 `json:"quality"`
	Band       string   `json:"band"`
	CurrentBid *int64   `json:"currentBid"`
	Buyout     *int64   `json:"buyout"`
}

// listingRow is the raw auction_listings row needed to build a view.
type listingRow struct {
	id               int64
	sellerAvatarID   int64
	itemID           int
	stackSize        int
	quality          *float64
	currentBid       int64
	startBid         int64
	buyout           *int64
	highBidderAvatar *int64
	expiresAt        time.Time
}

// computeBand maps remaining time to an opaque band. The exact thresholds are
// an implementation detail the client never sees -- it only gets low/med/high.
func computeBand(remaining time.Duration) string {
	switch {
	case remaining > 12*time.Hour:
		return "high"
	case remaining > 4*time.Hour:
		return "med"
	default:
		return "low"
	}
}

// avatarNames resolves avatar_id -> "First Last" for display. Soft-deleted
// avatars still resolve (the row is not removed) so their listings keep a name.
func (s *Store) avatarNames(ctx context.Context, ids []int64) map[int64]string {
	out := make(map[int64]string, len(ids))
	if len(ids) == 0 {
		return out
	}
	rows, err := s.user.Query(ctx, `
		SELECT avatar_id,
		       trim(both '_' from coalesce(first_name,'') || '_' || coalesce(last_name,''))
		  FROM avatar_data
		 WHERE avatar_id = ANY($1)`, ids)
	if err != nil {
		return out
	}
	defer rows.Close()
	for rows.Next() {
		var id int64
		var name string
		if err := rows.Scan(&id, &name); err == nil {
			out[id] = name
		}
	}
	return out
}

// scanListings reads listingRow slices from a query result.
func scanListings(rows interface {
	Next() bool
	Scan(...any) error
	Err() error
	Close()
}) ([]listingRow, error) {
	defer rows.Close()
	var out []listingRow
	for rows.Next() {
		var r listingRow
		if err := rows.Scan(&r.id, &r.sellerAvatarID, &r.itemID, &r.stackSize,
			&r.quality, &r.currentBid, &r.startBid, &r.buyout,
			&r.highBidderAvatar, &r.expiresAt); err != nil {
			return nil, err
		}
		out = append(out, r)
	}
	return out, rows.Err()
}

// enrich resolves item templates + seller/bidder names for a set of listings.
func (s *Store) enrich(ctx context.Context, rows []listingRow) (map[int]itemTemplate, map[int64]string) {
	itemIDs := make([]int, 0, len(rows))
	nameIDs := make([]int64, 0, len(rows)*2)
	for _, r := range rows {
		itemIDs = append(itemIDs, r.itemID)
		nameIDs = append(nameIDs, r.sellerAvatarID)
		if r.highBidderAvatar != nil {
			nameIDs = append(nameIDs, *r.highBidderAvatar)
		}
	}
	items, err := s.resolveItems(ctx, itemIDs)
	if err != nil {
		items = map[int]itemTemplate{}
	}
	return items, s.avatarNames(ctx, nameIDs)
}

const listingSelectCols = `id, seller_avatar_id, item_id, stack_size, quality,
	current_bid, start_bid, buyout, high_bidder_avatar_id, expires_at`

// openListings returns all active listings for the buy view, ordered by item
// name then id (NOT by expiry) so the opaque band is the only time signal.
func (s *Store) openListings(ctx context.Context) []ListingView {
	rows, err := s.user.Query(ctx, `
		SELECT `+listingSelectCols+`
		  FROM auction_listings
		 WHERE status = 0`)
	if err != nil {
		return nil
	}
	lr, err := scanListings(rows)
	if err != nil {
		return nil
	}
	items, names := s.enrich(ctx, lr)
	now := time.Now()

	out := make([]ListingView, 0, len(lr))
	for _, r := range lr {
		tpl := items[r.itemID]
		var highBidder *string
		if r.highBidderAvatar != nil {
			if n, ok := names[*r.highBidderAvatar]; ok {
				highBidder = &n
			}
		}
		out = append(out, ListingView{
			ID:         strconv.FormatInt(r.id, 10),
			Item:       tpl.itemView(r.quality),
			Seller:     names[r.sellerAvatarID],
			Stack:      r.stackSize,
			Quality:    r.quality,
			Band:       computeBand(r.expiresAt.Sub(now)),
			Bid:        r.currentBid,
			Buyout:     r.buyout,
			HighBidder: highBidder,
		})
	}
	sort.SliceStable(out, func(i, j int) bool {
		if out[i].Item.Name != out[j].Item.Name {
			return out[i].Item.Name < out[j].Item.Name
		}
		return out[i].ID < out[j].ID
	})
	return out
}

// listingsBySeller returns an account's own active listings for the sell view.
func (s *Store) listingsBySeller(ctx context.Context, accountID int64) []MyListingView {
	rows, err := s.user.Query(ctx, `
		SELECT `+listingSelectCols+`
		  FROM auction_listings
		 WHERE status = 0 AND seller_account_id = $1`, accountID)
	if err != nil {
		return nil
	}
	lr, err := scanListings(rows)
	if err != nil {
		return nil
	}
	items, names := s.enrich(ctx, lr)
	now := time.Now()

	out := make([]MyListingView, 0, len(lr))
	for _, r := range lr {
		tpl := items[r.itemID]
		var currentBid *int64
		if r.currentBid > r.startBid {
			cb := r.currentBid
			currentBid = &cb
		}
		out = append(out, MyListingView{
			ID:         strconv.FormatInt(r.id, 10),
			Item:       tpl.itemView(r.quality),
			Avatar:     names[r.sellerAvatarID],
			Stack:      r.stackSize,
			Quality:    r.quality,
			Band:       computeBand(r.expiresAt.Sub(now)),
			CurrentBid: currentBid,
			Buyout:     r.buyout,
		})
	}
	return out
}
