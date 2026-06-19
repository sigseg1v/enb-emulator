// SPDX-License-Identifier: MIT
// Freya Online -- Auction House faucet bots.
//
// When FREYA_AH_BOTS=1, the AhBot seller (seeded by freya_online_bots.sql at the
// fixed sentinel ids below) keeps the Auction House stocked with a
// rarity/quality/price-distributed pool of listings, so the house never looks
// empty. This is a FAUCET, not a player: it inserts listings directly (it pays
// no 10% deposit and removes nothing from a vault -- it has none), and proceeds
// from any sale land in AhBot's mailbox where they are simply never looted (the
// 90-day mail sweeper reaps them).
//
// Selection buckets (per the spec):
//   - common (~70%): ores / raw+refined resources (cat 80,81), stacks of 20, no
//     quality -> always renders common.
//   - uncommon (~25%): the equipment+component family (shields, reactors,
//     weapons, components) at level 1-4.
//   - rare (~5%): that same family at level 5-9, plus devices (incl. engines &
//     computers) at any level 1-9.
//
// These are SELECTION buckets (what KIND of item to post, and how often). The
// COLOUR a listing renders (common/uncommon/rare/epic) is a separate axis driven
// by the rolled quality% via rarityForLevelQuality -- so a "rare-bucket" device
// with a mediocre quality roll can still render uncommon, exactly as the spec's
// two distinct rarity rules intend.
//
// Pricing mirrors the player path: item_value = vendor (buying_price) * stack;
// the bot's opening bid is 110% of item_value and its buyout 200% (the same
// ratios the SPA suggests to a human seller), then scaled by a per-category
// factor (botPriceMultiplier) -- the raw vendor base undervalued several gear
// families and ore, so weapons/ore list at 20x, shields/reactors/engines/
// components at 10x, and devices at 40x.

package main

import (
	"context"
	"log"
	"math"
	"math/rand/v2"
	"time"
)

const (
	// Reserved high-but-SAFE ids (see db/postgres/freya_online_bots.sql): the
	// avatar id must stay < 2^30 so the player GameID (avatar_id|PLAYER_TAG)
	// round-trips the 32-bit wire field. account 9000001 -> avatar 45000006
	// (= account*5+slot+1). The old ~1e9 sentinel produced a ~5e9 avatar id
	// whose high bit was truncated off the wire and broke the master handoff.
	ahBotAccountID int64 = 9000001
	ahBotAvatarID  int64 = 45000006

	// Bucket weights (percent). Must sum to 100.
	botRareWeight     = 5
	botUncommonWeight = 25
	// common = the remaining 70.

	// Keep this many active AhBot listings on the shelf. With 8-24h durations
	// this replenishes to roughly the spec's "~128/day" as listings expire/sell.
	botTargetActive = 128
	// Cap how many we post per tick so a cold start fills in gradually rather
	// than dumping the whole shelf in one transaction burst.
	botBatchPerTick = 16
	botInterval     = 15 * time.Minute

	// Quality roll for equipment: normal(mean, std) clamped to [min,max], in
	// PERCENT POINTS (140 == 140%). The rolled value is divided by 100 before it
	// is stored, because quality is persisted on the GAME-NATIVE FRACTION scale
	// (1.0 == 100%) -- the same scale avatar_inventory_items / avatar_vault_items
	// use, which is what auction_listings round-trips into a buyer's ship on
	// purchase (PostListing/deliverItem copy it verbatim). Storing the percent
	// number here put a 100x quality (e.g. 15200%) onto every purchased item.
	botQualityMean = 140.0
	botQualityStd  = 25.0
	botQualityMin  = 80.0
	botQualityMax  = 200.0

	botOreStack = 20

	// How often the AhBot looks for player listings to bid on. The per-listing
	// bid probabilities below are calibrated as "chance to bid PER CHECK", so this
	// interval and botBidChance are coupled -- changing one without the other
	// changes the effective bid rate.
	botBidInterval = 30 * time.Minute
)

// botBidChance returns the per-check probability (0..1) that the AhBot bids on a
// player listing, as a function of ratio = current asking price / the AhBot's
// own minimum bid for the same item+stack. The cheaper a listing is relative to
// what the AhBot would price it at, the likelier the AhBot snaps it up; once it
// is priced above 120% of the AhBot's number the AhBot ignores it entirely.
//
// Anchor points (per spec), with linear interpolation between them:
//
//	ratio <= 0.50 -> 20%   (a steal: at or under half the AhBot's min bid)
//	ratio  = 0.75 ->  5%
//	ratio  = 0.90 ->  1%
//	ratio  = 1.20 ->  0.5%
//	ratio  > 1.20 ->  0%   (overpriced: ignore)
func botBidChance(ratio float64) float64 {
	switch {
	case ratio <= 0.50:
		return 0.20
	case ratio <= 0.75:
		return botLerp(ratio, 0.50, 0.75, 0.20, 0.05)
	case ratio <= 0.90:
		return botLerp(ratio, 0.75, 0.90, 0.05, 0.01)
	case ratio <= 1.20:
		return botLerp(ratio, 0.90, 1.20, 0.01, 0.005)
	default:
		return 0
	}
}

// botLerp linearly interpolates y across [x0,x1] -> [y0,y1] for x in that range.
func botLerp(x, x0, x1, y0, y1 float64) float64 {
	return y0 + (y1-y0)*(x-x0)/(x1-x0)
}

// Candidate category sets. ANY($1) over these in the content DB.
var (
	botEquipmentCats = []int{2, 7, 10, 14, 15, 16, 50, 51, 52, 53, 54} // shield/reactor/weapon/components
	botDeviceCats    = []int{6, 11, 18}                                // engine/device/computer
	botOreCats       = []int{80, 81}                                   // refined/raw resource
)

type botCandidate struct {
	id       int
	vendor   int64 // buying_price (economic base)
	maxStack int64
	category int64 // item_base.category (drives botPriceMultiplier)
}

// botPriceMultiplier scales a listing's opening bid and buyout per item
// category, on top of the base 110%/200% ratios. The faucet was pricing several
// gear families and ore far below their real worth; these per-category factors
// bring the shelf in line. Engine (cat 6) is priced as ship gear (10x), not as a
// generic device (40x). Mapping uses the raw item_base.category -- the bot pools
// only ever select direct categories (never the cat-12 "Core Item"
// supercategory), so no item_engine/reactor/shield probe is needed here.
func botPriceMultiplier(category int64) float64 {
	switch category {
	case 10, 14, 15, 16: // weapons
		return 20
	case 80, 81: // ore (refined / raw resource)
		return 20
	case 11, 18: // device, computer
		return 40
	case 2, 7, 6: // shield, reactor, engine
		return 10
	case 50, 51, 52, 53, 54: // components
		return 10
	default:
		return 1
	}
}

// startBots launches the faucet loop if enabled. It tops the shelf up once
// immediately, then every botInterval, until ctx is cancelled.
func startBots(ctx context.Context, s *Store) {
	go func() {
		log.Printf("freya-online: AH bots ENABLED (AhBot acct %d); target=%d active",
			ahBotAccountID, botTargetActive)
		t := time.NewTicker(botInterval)
		defer t.Stop()
		botTopUp(ctx, s)
		for {
			select {
			case <-ctx.Done():
				return
			case <-t.C:
				botTopUp(ctx, s)
			}
		}
	}()
}

// botTopUp posts up to botBatchPerTick listings toward botTargetActive. It is
// idempotent across restarts: it reads the current active count first, so a
// restart with a full shelf posts nothing.
func botTopUp(ctx context.Context, s *Store) {
	var active int
	err := s.user.QueryRow(ctx,
		`SELECT count(*) FROM auction_listings WHERE seller_account_id = $1 AND status = 0`,
		ahBotAccountID).Scan(&active)
	if err != nil {
		log.Printf("freya-online: AH bots: count active failed: %v", err)
		return
	}
	deficit := botTargetActive - active
	if deficit <= 0 {
		return
	}
	if deficit > botBatchPerTick {
		deficit = botBatchPerTick
	}
	posted := 0
	for i := 0; i < deficit; i++ {
		if err := botPostOne(ctx, s); err != nil {
			log.Printf("freya-online: AH bots: post failed: %v", err)
			break
		}
		posted++
	}
	if posted > 0 {
		log.Printf("freya-online: AH bots: posted %d listing(s) (%d -> %d active)",
			posted, active, active+posted)
	}
}

// botPickBucket rolls a selection bucket and returns the candidate + whether it
// carries a quality roll (equipment) and its stack size.
//
// The equipment buckets also require sub_category <> 0: every real piece of
// equipment carries a non-zero sub_category (its slot/class), so a zero one is a
// dev/test item that should never reach the AH. In the current content this band
// is exactly two unobtainable weapons -- "Energy Cannon" and "Disruptor Mortar"
// -- which the bot was otherwise listing for sale. Filtering on the structural
// flag (not a name blacklist) keeps any future dev junk out automatically.
func botPickCandidate(ctx context.Context, s *Store) (botCandidate, bool, int, bool) {
	roll := rand.IntN(100)
	switch {
	case roll < botRareWeight:
		// rare: equipment L5-9 OR devices L1-9.
		c, ok := botQuery(ctx, s, `
			SELECT id, buying_price, max_stack, category
			  FROM item_base
			 WHERE no_trade = 0 AND buying_price > 0 AND sub_category <> 0
			   AND ( (category = ANY($1) AND level BETWEEN 5 AND 9)
			      OR (category = ANY($2) AND level BETWEEN 1 AND 9) )
			 ORDER BY random() LIMIT 1`, botEquipmentCats, botDeviceCats)
		return c, true, 1, ok
	case roll < botRareWeight+botUncommonWeight:
		// uncommon: equipment family at L1-4.
		c, ok := botQuery(ctx, s, `
			SELECT id, buying_price, max_stack, category
			  FROM item_base
			 WHERE no_trade = 0 AND buying_price > 0 AND sub_category <> 0
			   AND category = ANY($1) AND level BETWEEN 1 AND 4
			 ORDER BY random() LIMIT 1`, botEquipmentCats, nil)
		return c, true, 1, ok
	default:
		// common: ores/resources, stacked, no quality.
		c, ok := botQuery(ctx, s, `
			SELECT id, buying_price, max_stack, category
			  FROM item_base
			 WHERE no_trade = 0 AND buying_price > 0
			   AND category = ANY($1)
			 ORDER BY random() LIMIT 1`, botOreCats, nil)
		stack := botOreStack
		if c.maxStack > 0 && int64(stack) > c.maxStack {
			stack = int(c.maxStack)
		}
		if stack < 1 {
			stack = 1
		}
		return c, false, stack, ok
	}
}

// botQuery runs a one-row candidate query. arg2 may be nil (only $1 referenced).
func botQuery(ctx context.Context, s *Store, sql string, arg1, arg2 []int) (botCandidate, bool) {
	var (
		c   botCandidate
		row interface{ Scan(...any) error }
	)
	if arg2 == nil {
		row = s.content.QueryRow(ctx, sql, arg1)
	} else {
		row = s.content.QueryRow(ctx, sql, arg1, arg2)
	}
	if err := row.Scan(&c.id, &c.vendor, &c.maxStack, &c.category); err != nil {
		return botCandidate{}, false
	}
	return c, true
}

// botPostOne selects one item and inserts a single AhBot listing for it.
func botPostOne(ctx context.Context, s *Store) error {
	cand, withQuality, stack, ok := botPickCandidate(ctx, s)
	if !ok || cand.vendor <= 0 {
		return nil // empty bucket / unpriced item -- skip this slot quietly
	}

	var quality *float64
	var structure *float64
	if withQuality {
		q := math.Round(rand.NormFloat64()*botQualityStd + botQualityMean)
		if q < botQualityMin {
			q = botQualityMin
		}
		if q > botQualityMax {
			q = botQualityMax
		}
		qf := q / 100 // percent points -> game-native fraction (1.0 == 100%)
		quality = &qf

		// Equipment carries a structural-integrity value on the same
		// game-native fraction scale (1.0 == 100%). The bot sells brand-new
		// goods, so seed full structure -- a NULL here flows through the
		// buyout -> mailbox -> inventory claim and the game reads it as 0%
		// (PlayerInventory.cpp treats <0.5 as damaged, 0.0 as destroyed), so
		// purchased items arrived broken (PB-15). Ores/resources (no quality)
		// have no structure and stay NULL.
		full := 1.0
		structure = &full
	}

	itemValue := cand.vendor * int64(stack)
	mult := botPriceMultiplier(cand.category)
	startBid := int64(math.Round(float64(itemValue) * 1.10 * mult))
	if startBid < 1 {
		startBid = 1
	}
	buyout := int64(math.Round(float64(itemValue) * 2.00 * mult))
	if buyout <= startBid {
		buyout = startBid + 1
	}

	hours := []int{8, 12, 24}[rand.IntN(3)]
	expires := time.Now().Add(time.Duration(hours) * time.Hour)

	_, err := s.user.Exec(ctx, `
		INSERT INTO auction_listings
		  (seller_avatar_id, seller_account_id, item_id, stack_size, quality, structure,
		   item_value, start_bid, current_bid, buyout, deposit_paid, status, expires_at)
		VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$8,$9,0,0,$10)`,
		ahBotAvatarID, ahBotAccountID, cand.id, stack, quality, structure,
		itemValue, startBid, buyout, expires)
	return err
}

// startBotBidder launches the AhBot auto-bid loop (FREYA_AH_BOT_BID_ITEMS,
// default on). Every botBidInterval it rolls, per eligible player listing,
// whether to place a faucet bid. It does NOT run an immediate pass at startup --
// the probabilities are per-30-minute-check, so firing on every restart would
// inflate the effective rate.
func startBotBidder(ctx context.Context, s *Store) {
	go func() {
		log.Printf("freya-online: AH bot-bidding ENABLED (AhBot acct %d); interval=%s",
			ahBotAccountID, botBidInterval)
		t := time.NewTicker(botBidInterval)
		defer t.Stop()
		for {
			select {
			case <-ctx.Done():
				return
			case <-t.C:
				botBidSweep(ctx, s)
			}
		}
	}()
}

// botBidSweep considers every ACTIVE player listing that currently has NO bidder
// and rolls botBidChance against it. The AhBot only ever places the FIRST bid on
// a listing: it never bids on its own faucet stock, never bids when it is already
// the high bidder, and -- because it only touches no-bidder listings -- never
// outbids a player who has already bid. It only ever bids (never buys out).
func botBidSweep(ctx context.Context, s *Store) {
	type cand struct {
		id         int64
		itemID     int
		currentBid int64
		itemValue  int64
	}
	rows, err := s.user.Query(ctx, `
		SELECT id, item_id, current_bid, item_value
		  FROM auction_listings
		 WHERE status = 0
		   AND seller_account_id <> $1
		   AND high_bidder_avatar_id IS NULL
		   AND expires_at > now()`, ahBotAccountID)
	if err != nil {
		log.Printf("freya-online: AH bot-bid: scan listings failed: %v", err)
		return
	}
	var cands []cand
	idSet := map[int]struct{}{}
	for rows.Next() {
		var c cand
		if err := rows.Scan(&c.id, &c.itemID, &c.currentBid, &c.itemValue); err != nil {
			rows.Close()
			log.Printf("freya-online: AH bot-bid: scan row failed: %v", err)
			return
		}
		cands = append(cands, c)
		idSet[c.itemID] = struct{}{}
	}
	rows.Close()
	if len(cands) == 0 {
		return
	}

	// Resolve each item's category (drives botPriceMultiplier) from the content
	// pool in one query -- it is a different database, so no cross-DB join.
	ids := make([]int, 0, len(idSet))
	for id := range idSet {
		ids = append(ids, id)
	}
	catRows, err := s.content.Query(ctx,
		`SELECT id, category FROM item_base WHERE id = ANY($1)`, ids)
	if err != nil {
		log.Printf("freya-online: AH bot-bid: resolve categories failed: %v", err)
		return
	}
	cats := map[int]int64{}
	for catRows.Next() {
		var id int
		var category int64
		if err := catRows.Scan(&id, &category); err != nil {
			catRows.Close()
			log.Printf("freya-online: AH bot-bid: scan category failed: %v", err)
			return
		}
		cats[id] = category
	}
	catRows.Close()

	placed := 0
	for _, c := range cands {
		category, ok := cats[c.itemID]
		if !ok {
			continue // item not in the catalogue any more -- skip quietly
		}
		// The AhBot's own minimum bid for this item+stack: the same 110%-of-value
		// (x per-category multiplier) it uses when it lists (botPostOne). The stored
		// item_value already encodes vendor * stack, so reuse it directly.
		ahMinBid := int64(math.Round(float64(c.itemValue) * 1.10 * botPriceMultiplier(category)))
		if ahMinBid < 1 {
			ahMinBid = 1
		}
		ratio := float64(c.currentBid) / float64(ahMinBid)
		chance := botBidChance(ratio)
		if chance <= 0 {
			continue // priced above 120% of the AhBot's number -- ignore
		}
		if rand.Float64() >= chance {
			continue
		}
		if err := s.botPlaceBid(ctx, c.id); err != nil {
			// errListingGone just means a real player bid (or expiry) beat us to it
			// between the scan and the locked re-check -- expected, not an error.
			if err != errListingGone {
				log.Printf("freya-online: AH bot-bid: listing %d: %v", c.id, err)
			}
			continue
		}
		placed++
	}
	if placed > 0 {
		log.Printf("freya-online: AH bot-bid: placed %d bid(s) across %d eligible listing(s)",
			placed, len(cands))
	}
}
