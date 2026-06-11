// SPDX-License-Identifier: MIT
// Freya Online -- per-avatar Profile reads (identity, ship, skills).
//
// The Profile page loads on demand, one avatar at a time, split across three
// small endpoints so switching characters never re-pulls the whole account.
// Every read is ownership-scoped: the avatar must belong to the session's
// account (ownedAvatar), so a signed-in user can never address another
// account's character by guessing a name.
//
// Two pools as everywhere else: avatar_* save-state lives in net7_user (s.user);
// the sector-name and skill-definition catalogues live in net7 (s.content). A
// single connection cannot read across them, so each method issues its user-pool
// query and its content-pool query separately and joins in Go.

package main

import (
	"context"
	"errors"

	"github.com/jackc/pgx/v5"
)

// Class identity in Earth & Beyond is race + profession together: the same
// profession slot is a different class per race. ClassIndex = race*3 + prof.
// Mirrors the server (UDP_Global.cpp kClassAbbrev), the CLI (CharacterClass.cs),
// and the status-notifier bot (freya/status-notifier/bot.go classCodes).
var (
	raceNames  = [3]string{"Terran", "Jenquai", "Progen"}
	classNames = [9]string{"Enforcer", "Trader", "Scout", "Defender", "Seeker", "Explorer", "Warrior", "Privateer", "Sentinel"}
	classCodes = [9]string{"TE", "TT", "TS", "JD", "JS", "JE", "PW", "PP", "PS"}
)

func classIndex(race, prof int) int { return race*3 + prof }

// avatarIdent is the minimum identity needed to scope and classify subsequent
// reads: the avatar id (for the per-avatar joins) and race/prof (for the class
// name and the per-class skill max-level column).
type avatarIdent struct {
	id   int64
	race int
	prof int
}

func (a avatarIdent) idx() int { return classIndex(a.race, a.prof) }

// ownedAvatar resolves a live avatar by its display name (first_last), requiring
// it to belong to accountID. errCharacterNotFound if the account has no such
// live character. Uses idx_avatar_info_account_live (account_id WHERE
// deleted_at IS NULL).
func (s *Store) ownedAvatar(ctx context.Context, accountID int64, name string) (avatarIdent, error) {
	var a avatarIdent
	err := s.user.QueryRow(ctx, `
		SELECT ai.avatar_id, ad.race, ad.prof
		  FROM avatar_info ai
		  JOIN avatar_data ad ON ad.avatar_id = ai.avatar_id
		 WHERE ai.account_id = $1 AND ai.deleted_at IS NULL
		   AND trim(both '_' from coalesce(ad.first_name,'') || '_' || coalesce(ad.last_name,'')) = $2`,
		accountID, name).Scan(&a.id, &a.race, &a.prof)
	if errors.Is(err, pgx.ErrNoRows) {
		return a, errCharacterNotFound
	}
	return a, err
}

// Discipline is one of the three skill trees: the discrete level plus the
// fractional bar progress (0..1) toward the next level.
type Discipline struct {
	Level int     `json:"level"`
	Bar   float64 `json:"bar"`
}

// AvatarProfile is the identity card at the top of the Profile page.
type AvatarProfile struct {
	Name      string     `json:"name"`
	Race      string     `json:"race"`
	Class     string     `json:"class"`     // e.g. "Terran Enforcer"
	ClassCode string     `json:"classCode"` // e.g. "TE"
	Level     int        `json:"level"`     // overall level = combat + explore + trade
	Combat    Discipline `json:"combat"`
	Explore   Discipline `json:"explore"`
	Trade     Discipline `json:"trade"`
	Sector    string     `json:"sector"`
	Credits   int64      `json:"credits"`
}

// avatarProfile builds the identity card for one owned avatar. Two reads: the
// level/credit row (user pool) and the sector name (content pool).
func (s *Store) avatarProfile(ctx context.Context, accountID int64, name string) (AvatarProfile, error) {
	a, err := s.ownedAvatar(ctx, accountID, name)
	if err != nil {
		return AvatarProfile{}, err
	}

	var (
		sectorID                        int64
		combat, explore, trade          int
		combatBar, exploreBar, tradeBar float64
		credits                         int64
	)
	err = s.user.QueryRow(ctx, `
		SELECT ai.sector, ai.combat, ai.explore, ai.trade,
		       coalesce(ali.combat_bar_level, 0), coalesce(ali.explore_bar_level, 0),
		       coalesce(ali.trade_bar_level, 0), coalesce(ali.credits, 0)::bigint
		  FROM avatar_info ai
		  LEFT JOIN avatar_level_info ali ON ali.avatar_id = ai.avatar_id
		 WHERE ai.avatar_id = $1`, a.id).
		Scan(&sectorID, &combat, &explore, &trade, &combatBar, &exploreBar, &tradeBar, &credits)
	if err != nil {
		return AvatarProfile{}, err
	}

	// Sector name is a content-DB lookup; an unknown id just renders blank.
	var sector string
	_ = s.content.QueryRow(ctx,
		`SELECT name FROM sectors WHERE sector_id = $1`, sectorID).Scan(&sector)

	idx := a.idx()
	p := AvatarProfile{
		Name:    name,
		Level:   combat + explore + trade,
		Combat:  Discipline{Level: combat, Bar: clampBar(combatBar)},
		Explore: Discipline{Level: explore, Bar: clampBar(exploreBar)},
		Trade:   Discipline{Level: trade, Bar: clampBar(tradeBar)},
		Sector:  sector,
		Credits: credits,
	}
	if a.race >= 0 && a.race < len(raceNames) {
		p.Race = raceNames[a.race]
	}
	if idx >= 0 && idx < len(classNames) {
		p.Class = p.Race + " " + classNames[idx]
		p.ClassCode = classCodes[idx]
	}
	return p, nil
}

func clampBar(v float64) float64 {
	if v < 0 {
		return 0
	}
	if v > 1 {
		return 1
	}
	return v
}

// EquippedItem is one item fitted to the ship hull, with its installed slot and
// per-instance quality. The Item carries the full tooltip payload.
type EquippedItem struct {
	Slot    int      `json:"slot"`
	Quality *float64 `json:"quality"`
	Item    ItemView `json:"item"`
}

// ShipView is the starship card: chassis name, hull integrity, cargo, warp
// speed, and every fitted item.
//
// We deliberately expose only Warp, not impulse/thrust. Warp is a flat property
// of the equipped engine (item_engine.warp), so it round-trips exactly. The
// in-game thrust number is a RUNTIME aggregate (hull base + engine + Engine
// skill + buffs) that the server computes and never persists; the save DB has
// no column for it and the engine's raw thrust_100 does not match the in-game
// figure. Showing a wrong thrust is worse than showing none, so it is omitted.
// The legacy avatar_level_info.engine_thrust_type / warp_power_level columns are
// an engine TRAIL type and a power-level INDEX -- not speeds -- so we ignore them.
type ShipView struct {
	Name          string         `json:"name"`
	HullPoints    float64        `json:"hullPoints"`
	MaxHullPoints float64        `json:"maxHullPoints"`
	CargoSpace    int            `json:"cargoSpace"`
	Warp          int            `json:"warp"` // equipped engine's item_engine.warp
	Equipment     []EquippedItem `json:"equipment"`
}

// avatarShip builds the starship card for one owned avatar. User-pool reads:
// the ship name, the level-info ship stats, and the equipment rows. Content-pool
// read: the item templates for the fitted ids (batched).
func (s *Store) avatarShip(ctx context.Context, accountID int64, name string) (ShipView, error) {
	a, err := s.ownedAvatar(ctx, accountID, name)
	if err != nil {
		return ShipView{}, err
	}

	var ship ShipView
	err = s.user.QueryRow(ctx, `
		SELECT coalesce(sd.name, ''),
		       coalesce(ali.hull_points, 0), coalesce(ali.max_hull_points, 0),
		       coalesce(ali.cargo_space, 0)
		  FROM avatar_info ai
		  LEFT JOIN ship_data sd        ON sd.avatar_id = ai.avatar_id
		  LEFT JOIN avatar_level_info ali ON ali.avatar_id = ai.avatar_id
		 WHERE ai.avatar_id = $1`, a.id).
		Scan(&ship.Name, &ship.HullPoints, &ship.MaxHullPoints, &ship.CargoSpace)
	if err != nil {
		return ShipView{}, err
	}

	// Fitted gear. item_id <= 0 are empty/locked slot sentinels (-1 empty,
	// -2 locked); skip them. PK (avatar_id, equipment_slot) orders cheaply.
	rows, err := s.user.Query(ctx, `
		SELECT equipment_slot, item_id, quality
		  FROM avatar_equipment
		 WHERE avatar_id = $1 AND item_id > 0
		 ORDER BY equipment_slot ASC`, a.id)
	if err != nil {
		return ShipView{}, err
	}
	type equipRow struct {
		slot    int
		itemID  int
		quality float64
	}
	var fitted []equipRow
	ids := make([]int, 0, 16)
	for rows.Next() {
		var e equipRow
		if err := rows.Scan(&e.slot, &e.itemID, &e.quality); err != nil {
			rows.Close()
			return ShipView{}, err
		}
		fitted = append(fitted, e)
		ids = append(ids, e.itemID)
	}
	rows.Close()
	if err := rows.Err(); err != nil {
		return ShipView{}, err
	}

	templates, err := s.resolveItems(ctx, ids)
	if err != nil {
		return ShipView{}, err
	}

	// Warp speed comes from the fitted engine itself, not from any save-state
	// column. Of the fitted item ids, at most one is an engine; look its warp up
	// in the content pool. (Cross-DB join is impossible -- avatar_equipment is in
	// the user pool, item_engine in the content pool -- so this is a second query
	// keyed on the ids we already gathered.)
	if len(ids) > 0 {
		var warp int
		err = s.content.QueryRow(ctx, `
			SELECT warp FROM item_engine WHERE item_id = ANY($1)
			 ORDER BY warp DESC LIMIT 1`, ids).Scan(&warp)
		if err == nil {
			ship.Warp = warp
		} else if !errors.Is(err, pgx.ErrNoRows) {
			return ShipView{}, err
		}
	}

	for _, e := range fitted {
		t, ok := templates[e.itemID]
		if !ok {
			continue // template missing from catalogue -- skip rather than show a husk
		}
		q := e.quality
		ship.Equipment = append(ship.Equipment, EquippedItem{
			Slot:    e.slot,
			Quality: &q,
			Item:    t.itemView(&q),
		})
	}
	return ship, nil
}

// SkillView is one learned skill: its current level and the max level for THIS
// character's class (so the UI can render level-of-max as filled/empty dots).
type SkillView struct {
	ID       int    `json:"id"`
	Name     string `json:"name"`
	Category string `json:"category"` // Combat | Explore | Trade | Total
	Level    int    `json:"level"`
	MaxLevel int    `json:"maxLevel"`
}

// avatarSkills lists an owned avatar's learned skills with per-class caps.
// User-pool read: the learned (skill_id, level) rows. Content-pool read: the
// skill definitions (name, category, and the nine per-class max-level columns)
// for exactly those ids; the right column is chosen by class index in Go.
func (s *Store) avatarSkills(ctx context.Context, accountID int64, name string) ([]SkillView, error) {
	a, err := s.ownedAvatar(ctx, accountID, name)
	if err != nil {
		return nil, err
	}

	rows, err := s.user.Query(ctx, `
		SELECT skill_id, coalesce(skill_level, 0)
		  FROM avatar_skill_levels
		 WHERE avatar_id = $1
		 ORDER BY skill_id ASC`, a.id)
	if err != nil {
		return nil, err
	}
	learned := make(map[int]int) // skill_id -> level
	ids := make([]int, 0, 32)
	for rows.Next() {
		var id, lvl int
		if err := rows.Scan(&id, &lvl); err != nil {
			rows.Close()
			return nil, err
		}
		learned[id] = lvl
		ids = append(ids, id)
	}
	rows.Close()
	if err := rows.Err(); err != nil {
		return nil, err
	}
	if len(ids) == 0 {
		return []SkillView{}, nil
	}

	// The nine per-class max-level columns, ordered to match class index 0..8
	// (Enforcer, Trader, Scout, Defender, Seeker, Explorer, Warrior, Privateer,
	// Sentinel). NULL/-1 means "not available to this class"; coalesce to 0.
	defRows, err := s.content.Query(ctx, `
		SELECT skill_id, name, coalesce(category, ''),
		       coalesce(enforcer_max_level, 0),  coalesce(tradesman_max_level, 0),
		       coalesce(scout_max_level, 0),     coalesce(defender_max_level, 0),
		       coalesce(seeker_max_level, 0),    coalesce(explorer_max_level, 0),
		       coalesce(warrior_max_level, 0),   coalesce(privateer_max_level, 0),
		       coalesce(sentinal_max_level, 0)
		  FROM skills
		 WHERE skill_id = ANY($1)`, ids)
	if err != nil {
		return nil, err
	}
	defer defRows.Close()

	idx := a.idx()
	var out []SkillView
	for defRows.Next() {
		var (
			id         int
			nm, cat    string
			maxByClass [9]int
		)
		if err := defRows.Scan(&id, &nm, &cat,
			&maxByClass[0], &maxByClass[1], &maxByClass[2],
			&maxByClass[3], &maxByClass[4], &maxByClass[5],
			&maxByClass[6], &maxByClass[7], &maxByClass[8]); err != nil {
			return nil, err
		}
		max := 0
		if idx >= 0 && idx < len(maxByClass) {
			max = maxByClass[idx]
		}
		lvl := learned[id]
		if max < lvl {
			max = lvl // never render more filled dots than the cap
		}
		out = append(out, SkillView{ID: id, Name: nm, Category: cat, Level: lvl, MaxLevel: max})
	}
	if err := defRows.Err(); err != nil {
		return nil, err
	}
	return out, nil
}
