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
	NoTrade     bool   `json:"-"`
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
// coarse Cat bucket. Unknown ids fall back to "component"; ores are detected by
// name because there is no dedicated ore category id in the catalogue.
func categoryToCat(categoryID int, name string) string {
	switch categoryID {
	case 2:
		return "shield"
	case 7:
		return "reactor"
	case 11, 18, 6:
		return "device"
	case 14, 15, 16:
		return "weapon"
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
			Vendor:      price,
			MaxStack:    maxStack,
			NoTrade:     noTrade != 0,
		}}
	}
	return out, rows.Err()
}

// itemView applies an instance quality to a template, producing the SPA Item
// with the correct per-instance rarity.
func (t itemTemplate) itemView(quality *float64) ItemView {
	v := t.view
	v.Rarity = rarityForLevelQuality(v.Level, quality)
	return v
}
