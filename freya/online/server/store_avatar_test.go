// SPDX-License-Identifier: MIT
// Freya Online -- Profile read integration tests (real Postgres, two-DB schema).
//
// Cover the three on-demand Profile reads: identity/class/level, the starship
// card + fitted equipment (excluding empty-slot sentinels), and learned skills
// with the per-class max level. Every read is ownership-scoped, so the
// cross-account negative case is asserted too. Gated on FREYA_TEST_DB.

package main

import (
	"errors"
	"testing"
)

// A default-seeded avatar is race 0 / prof 0 == Terran Enforcer, with
// combat/explore/trade = 1/1/1 (overall level 3). Ownership is enforced: a
// second account cannot read the first account's character by name.
func TestIT_AvatarProfile(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 30, 1, 7500)
	other := seedAccount(t, st, ctx, 31, 1, 0)
	name := acct.avatars[0].name

	p, err := st.avatarProfile(ctx, acct.id, name)
	if err != nil {
		t.Fatalf("avatarProfile: %v", err)
	}
	if p.Class != "Terran Enforcer" || p.ClassCode != "TE" {
		t.Errorf("class = %q / %q, want Terran Enforcer / TE", p.Class, p.ClassCode)
	}
	if p.Level != 3 {
		t.Errorf("overall level = %d, want 3 (combat+explore+trade = 1+1+1)", p.Level)
	}
	if p.Combat.Level != 1 || p.Explore.Level != 1 || p.Trade.Level != 1 {
		t.Errorf("disciplines = %d/%d/%d, want 1/1/1", p.Combat.Level, p.Explore.Level, p.Trade.Level)
	}
	if p.Credits != 7500 {
		t.Errorf("credits = %d, want 7500", p.Credits)
	}

	// Ownership: the other account must not resolve this character.
	if _, err := st.avatarProfile(ctx, other.id, name); !errors.Is(err, errCharacterNotFound) {
		t.Errorf("cross-account read err = %v, want errCharacterNotFound", err)
	}
}

// The starship card reflects ship_data.name + avatar_level_info ship stats, and
// equipment excludes the -1/-2 empty/locked slot sentinels while including real
// fitted items resolved from the content catalogue.
func TestIT_AvatarShip(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 32, 1, 0)
	av := acct.avatars[0]

	// Give the ship a name and a real fitted item + two sentinel slots.
	seedZeroedRow(t, st.user, ctx, "ship_data", map[string]any{
		"avatar_id": av.id, "name": "Test Runner",
	})
	itemID, _ := pickTradableItem(t, st, ctx, false)
	q := 120.0
	mustExec(t, st.user, ctx,
		`INSERT INTO avatar_equipment (avatar_id, equipment_slot, item_id, quality) VALUES ($1,0,$2,$3)`,
		av.id, itemID, q)
	mustExec(t, st.user, ctx,
		`INSERT INTO avatar_equipment (avatar_id, equipment_slot, item_id, quality) VALUES ($1,1,-1,0)`, av.id)
	mustExec(t, st.user, ctx,
		`INSERT INTO avatar_equipment (avatar_id, equipment_slot, item_id, quality) VALUES ($1,2,-2,0)`, av.id)

	ship, err := st.avatarShip(ctx, acct.id, av.name)
	if err != nil {
		t.Fatalf("avatarShip: %v", err)
	}
	if ship.Name != "Test Runner" {
		t.Errorf("ship name = %q, want Test Runner", ship.Name)
	}
	if len(ship.Equipment) != 1 {
		t.Fatalf("equipment count = %d, want 1 (sentinels excluded)", len(ship.Equipment))
	}
	eq := ship.Equipment[0]
	if eq.Item.ID == "" || eq.Item.Name == "" {
		t.Errorf("fitted item not resolved from catalogue: %+v", eq.Item)
	}
	if eq.Quality == nil || *eq.Quality != q {
		t.Errorf("fitted quality = %v, want %v", eq.Quality, q)
	}
}

// Learned skills resolve their name/category from the content catalogue and the
// max level from THIS character's class column (Terran Enforcer ->
// enforcer_max_level). The reported max is never below the learned level.
func TestIT_AvatarSkills(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 33, 1, 0)
	av := acct.avatars[0]

	// Find a skill whose Enforcer cap is known, so we can assert the class pick.
	var skillID, enforcerMax int
	var skillName string
	if err := st.content.QueryRow(ctx, `
		SELECT skill_id, name, enforcer_max_level
		  FROM skills
		 WHERE enforcer_max_level > 1
		 ORDER BY skill_id LIMIT 1`).Scan(&skillID, &skillName, &enforcerMax); err != nil {
		t.Skipf("no skill with enforcer_max_level > 1 in content DB: %v", err)
	}

	const learned = 1
	mustExec(t, st.user, ctx,
		`INSERT INTO avatar_skill_levels (avatar_id, skill_id, skill_level) VALUES ($1,$2,$3)`,
		av.id, skillID, learned)

	skills, err := st.avatarSkills(ctx, acct.id, av.name)
	if err != nil {
		t.Fatalf("avatarSkills: %v", err)
	}
	if len(skills) != 1 {
		t.Fatalf("skill count = %d, want 1", len(skills))
	}
	s := skills[0]
	if s.ID != skillID || s.Name != skillName {
		t.Errorf("skill = %d/%q, want %d/%q", s.ID, s.Name, skillID, skillName)
	}
	if s.Level != learned {
		t.Errorf("skill level = %d, want %d", s.Level, learned)
	}
	if s.MaxLevel != enforcerMax {
		t.Errorf("skill max = %d, want %d (enforcer_max_level)", s.MaxLevel, enforcerMax)
	}

	// Empty case: an account's other-class-or-no-skills character returns [].
	other := seedAccount(t, st, ctx, 34, 1, 0)
	empty, err := st.avatarSkills(ctx, other.id, other.avatars[0].name)
	if err != nil {
		t.Fatalf("avatarSkills (empty): %v", err)
	}
	if len(empty) != 0 {
		t.Errorf("empty skills = %d, want 0", len(empty))
	}
}
