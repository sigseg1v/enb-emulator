// SPDX-License-Identifier: MIT
// Freya Online -- item template resolution from the content DB (net7.item_base).
//
// The AH/mailbox tables live in net7_user and store only an item_id reference
// into net7.item_base (cross-DB, no FK -- see freya_online.sql). Resolving a
// listing/attachment/vault row into something the SPA can render means a second
// query against the content pool. We batch those lookups by id.

package main

import (
	"context"
	"strconv"
)

// ItemView mirrors the SPA's Item interface (web/src/types.ts). rarity is
// computed per-instance by the caller (it depends on the instance quality), so
// it is filled in at listing/attachment build time, not here.
type ItemView struct {
	ID          string `json:"id"`
	Name        string `json:"name"`
	Rarity      string `json:"rarity"`
	Cat         string `json:"cat"`
	Slot        string `json:"slot"`
	Level       int    `json:"level"`
	Glyph       string `json:"glyph"`
	Description string `json:"description"`
	Prices      Prices `json:"prices"`
	Vendor      int64  `json:"vendor"`
	MaxStack    int64  `json:"-"`
	// Exposed so the Auction House UI can disable listing of no-trade items up
	// front; the server still hard-rejects them (errItemUntradable) regardless.
	NoTrade bool `json:"noTrade"`
}

type Prices struct {
	Buy         int64 `json:"buy"`
	Sell        int64 `json:"sell"`
	Manufacture int64 `json:"manufacture"`
}

// itemTemplate is the raw item_base row before rarity (which needs an instance
// quality) is applied.
type itemTemplate struct {
	view ItemView
}

// categoryToCat maps item_base.category (an item_categories id) to the SPA's
// coarse Cat bucket (web/src/types.ts: weapon|shield|reactor|device|component|
// ore). The catalogue ids are: 2 Shield, 6 Engine, 7 Reactor, 10 Weapon, 11
// Device, 14 Energy Beam Weapon, 15 Missile Launcher, 16 Projectile Launcher,
// 18 Computer, 50-54 components (Electronic/Reactor/Fabricated/Weapon/Ammo),
// 80 Refined Resource, 81 Raw Resource, 90 Trade Good. Engine has no dedicated
// SPA Cat, so it rides under "device" (installed ship gear). Unknown ids fall
// back to "component".
func categoryToCat(categoryID int, name string) string {
	switch categoryID {
	case 2:
		return "shield"
	case 7:
		return "reactor"
	case 6, 11, 18:
		return "device"
	case 10, 14, 15, 16:
		return "weapon"
	case 80, 81:
		return "ore"
	case 50, 51, 52, 53, 54:
		return "component"
	}
	if containsFold(name, "ore") {
		return "ore"
	}
	return "component"
}

func glyphForCat(cat string) string {
	switch cat {
	case "weapon":
		return "◈"
	case "shield":
		return "◐"
	case "reactor":
		return "⚙"
	case "device":
		return "◉"
	case "ore":
		return "◇"
	default:
		return "▣"
	}
}

func containsFold(s, sub string) bool {
	if len(sub) > len(s) {
		return false
	}
	lower := func(b byte) byte {
		if b >= 'A' && b <= 'Z' {
			return b + 32
		}
		return b
	}
	for i := 0; i+len(sub) <= len(s); i++ {
		ok := true
		for j := 0; j < len(sub); j++ {
			if lower(s[i+j]) != lower(sub[j]) {
				ok = false
				break
			}
		}
		if ok {
			return true
		}
	}
	return false
}

// resolveItems batch-loads item templates for the given ids from the content
// pool. Missing ids are simply absent from the returned map.
func (s *Store) resolveItems(ctx context.Context, ids []int) (map[int]itemTemplate, error) {
	out := make(map[int]itemTemplate, len(ids))
	if len(ids) == 0 {
		return out, nil
	}
	rows, err := s.content.Query(ctx, `
		SELECT id, level, category, name, description, price,
		       buying_price, selling_price, man_cost, max_stack, no_trade
		  FROM item_base
		 WHERE id = ANY($1)`, ids)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	for rows.Next() {
		var (
			id, category, manCost int64
			level, noTrade        int
			buyPrice, sellPrice   int64
			price, maxStack       int64
			name, description     string
		)
		if err := rows.Scan(&id, &level, &category, &name, &description, &price,
			&buyPrice, &sellPrice, &manCost, &maxStack, &noTrade); err != nil {
			return nil, err
		}
		cat := categoryToCat(int(category), name)
		out[int(id)] = itemTemplate{view: ItemView{
			ID:          strconv.FormatInt(id, 10),
			Name:        name,
			Cat:         cat,
			Slot:        cat,
			Level:       level,
			Glyph:       glyphForCat(cat),
			Description: description,
			Prices:      Prices{Buy: buyPrice, Sell: sellPrice, Manufacture: manCost},
			// Vendor is the economic base value (the SPA documents it as the
			// "vendor sell price" and derives bid increments / open-bid / buyout
			// from it). That is item_base.buying_price -- what an NPC vendor pays
			// for the item -- NOT item_base.price (the inflated catalogue price,
			// ~10x higher), which would make every AH min-bid step and deposit
			// an order of magnitude too large.
			Vendor:   buyPrice,
			MaxStack: maxStack,
			NoTrade:  noTrade != 0,
		}}
	}
	return out, rows.Err()
}

// itemName resolves a single item's display name from the content pool, for
// building human-readable mail subjects. Returns "" if the id is unknown so
// callers fall back to a bare subject. Uses the content pool (net7), so it is
// safe to call alongside a net7_user transaction -- it is a separate connection,
// not a cross-DB read on the same one.
func (s *Store) itemName(ctx context.Context, itemID int) string {
	var name string
	if err := s.content.QueryRow(ctx,
		`SELECT name FROM item_base WHERE id = $1`, itemID).Scan(&name); err != nil {
		return ""
	}
	return name
}

// itemView applies an instance quality to a template, producing the SPA Item
// with the correct per-instance rarity.
func (t itemTemplate) itemView(quality *float64) ItemView {
	v := t.view
	v.Rarity = rarityForLevelQuality(v.Level, quality)
	return v
}
