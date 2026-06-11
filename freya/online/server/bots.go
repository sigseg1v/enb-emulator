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
// ratios the SPA suggests to a human seller).

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
)

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
func botPickCandidate(ctx context.Context, s *Store) (botCandidate, bool, int, bool) {
	roll := rand.IntN(100)
	switch {
	case roll < botRareWeight:
		// rare: equipment L5-9 OR devices L1-9.
		c, ok := botQuery(ctx, s, `
			SELECT id, buying_price, max_stack
			  FROM item_base
			 WHERE no_trade = 0 AND buying_price > 0
			   AND ( (category = ANY($1) AND level BETWEEN 5 AND 9)
			      OR (category = ANY($2) AND level BETWEEN 1 AND 9) )
			 ORDER BY random() LIMIT 1`, botEquipmentCats, botDeviceCats)
		return c, true, 1, ok
	case roll < botRareWeight+botUncommonWeight:
		// uncommon: equipment family at L1-4.
		c, ok := botQuery(ctx, s, `
			SELECT id, buying_price, max_stack
			  FROM item_base
			 WHERE no_trade = 0 AND buying_price > 0
			   AND category = ANY($1) AND level BETWEEN 1 AND 4
			 ORDER BY random() LIMIT 1`, botEquipmentCats, nil)
		return c, true, 1, ok
	default:
		// common: ores/resources, stacked, no quality.
		c, ok := botQuery(ctx, s, `
			SELECT id, buying_price, max_stack
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
	if err := row.Scan(&c.id, &c.vendor, &c.maxStack); err != nil {
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
	}

	itemValue := cand.vendor * int64(stack)
	startBid := int64(math.Round(float64(itemValue) * 1.10))
	if startBid < 1 {
		startBid = 1
	}
	buyout := int64(math.Round(float64(itemValue) * 2.00))
	if buyout <= startBid {
		buyout = startBid + 1
	}

	hours := []int{8, 12, 24}[rand.IntN(3)]
	expires := time.Now().Add(time.Duration(hours) * time.Hour)

	_, err := s.user.Exec(ctx, `
		INSERT INTO auction_listings
		  (seller_avatar_id, seller_account_id, item_id, stack_size, quality,
		   item_value, start_bid, current_bid, buyout, deposit_paid, status, expires_at)
		VALUES ($1,$2,$3,$4,$5,$6,$7,$7,$8,0,0,$9)`,
		ahBotAvatarID, ahBotAccountID, cand.id, stack, quality,
		itemValue, startBid, buyout, expires)
	return err
}
