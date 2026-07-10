// SPDX-License-Identifier: MIT
// Freya Online -- item search over the content DB (net7), for the Search screen.
//
// One item name (fuzzy ILIKE) resolves to its base stats plus the set of mobs
// that drop it, each with its combat level and the sector it spawns in. Every
// table here lives in net7 (the content pool), so this is a single-connection
// read against s.content -- no cross-DB join.

package main

import (
	"context"
	"strconv"
)

// SearchItem is one matched item and everything the Search screen shows about
// it: base stats plus the aggregated list of mobs that drop it.
type SearchItem struct {
	ID             string       `json:"id"`
	Name           string       `json:"name"`
	Level          int          `json:"level"`
	Manufacturable bool         `json:"manufacturable"`
	Drops          []SearchDrop `json:"drops"`
}

// SearchDrop is one mob that drops the item, with its combat level and the
// sector it is found in. Sector is "" when the mob template has no resolved
// spawn location (a drop-table entry with no placed spawn).
type SearchDrop struct {
	Mob         string `json:"mob"`
	CombatLevel int    `json:"combatLevel"`
	Sector      string `json:"sector"`
}

// searchItems finds items whose name matches term (case-insensitive substring)
// and, for each, the mobs that drop it. term is bound as a parameter ($1) --
// never concatenated into the SQL.
//
// The join walks item_base -> mob_items (drop table) -> mob_base (the mob
// template + its combat level) -> the spawn chain (mob_spawn_group ->
// sector_objects_mob -> sector_objects) -> sectors (the sector name). The spawn
// chain and sector are LEFT-joined so a mob that drops the item but has no
// placed spawn still appears (with an empty sector). Manufacturable is true
// when the item has an item_manufacture recipe and is not flagged no_manu.
//
// A mob template spawns at many sector_objects, so the same (item, mob, sector)
// triple recurs across rows; DISTINCT collapses them. Results are aggregated in
// Go into one SearchItem per item id with its de-duplicated drop list.
func (s *Store) searchItems(ctx context.Context, term string, limit int) ([]SearchItem, error) {
	rows, err := s.content.Query(ctx, `
		SELECT DISTINCT
		       ib.id,
		       ib.name,
		       ib.level,
		       (im.item_id IS NOT NULL AND ib.no_manu = 0) AS manufacturable,
		       mb.name  AS mob,
		       mb.level AS combat_level,
		       s.name   AS sector
		  FROM item_base ib
		  JOIN mob_items mi          ON mi.item_base_id = ib.id
		  JOIN mob_base  mb          ON mb.mob_id = mi.mob_id
		  LEFT JOIN mob_spawn_group msg    ON msg.mob_id = mb.mob_id
		  LEFT JOIN sector_objects_mob som ON som.mob_id = msg.spawn_group_id
		  LEFT JOIN sector_objects so      ON so.sector_object_id = som.mob_id
		  LEFT JOIN sectors s              ON s.sector_id = so.sector_id
		  LEFT JOIN item_manufacture im    ON im.item_id = ib.id
		 WHERE ib.name ILIKE '%' || $1 || '%'
		 ORDER BY ib.name, mob, sector`, term)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	// Aggregate rows into one SearchItem per id, preserving first-seen order and
	// de-duplicating identical (mob, level, sector) drops.
	byID := make(map[int64]*SearchItem)
	var order []int64
	seen := make(map[string]bool) // "<itemID>|<mob>|<level>|<sector>"

	for rows.Next() {
		var (
			id          int64
			name        string
			level       int
			manu        bool
			mob         string
			combatLevel int
			sector      *string // NULL when the drop has no placed spawn
		)
		if err := rows.Scan(&id, &name, &level, &manu, &mob, &combatLevel, &sector); err != nil {
			return nil, err
		}
		item, ok := byID[id]
		if !ok {
			item = &SearchItem{
				ID:             strconv.FormatInt(id, 10),
				Name:           name,
				Level:          level,
				Manufacturable: manu,
			}
			byID[id] = item
			order = append(order, id)
		}
		sec := ""
		if sector != nil {
			sec = *sector
		}
		key := item.ID + "|" + mob + "|" + strconv.Itoa(combatLevel) + "|" + sec
		if seen[key] {
			continue
		}
		seen[key] = true
		item.Drops = append(item.Drops, SearchDrop{Mob: mob, CombatLevel: combatLevel, Sector: sec})
	}
	if err := rows.Err(); err != nil {
		return nil, err
	}

	out := make([]SearchItem, 0, len(order))
	for _, id := range order {
		if limit > 0 && len(out) >= limit {
			break
		}
		out = append(out, *byID[id])
	}
	return out, nil
}
