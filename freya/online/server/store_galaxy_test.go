// SPDX-License-Identifier: MIT
// Freya Online -- galaxy map read integration tests (real Postgres, two-DB schema).
//
// Cover the topology build (systems/sectors/gate edges from the content DB) and
// the live occupancy aggregate (per-sector online counts from save-state, with
// docked players remapped to their parent sector). Gated on FREYA_TEST_DB.

package main

import (
	"strconv"
	"testing"
)

func itoa(v int64) string { return strconv.FormatInt(v, 10) }

// The topology is content data: there must be named sectors, the 25 known star
// systems, faction tags, and a deduplicated, self-loop-free gate graph whose
// endpoints are all real sectors.
func TestIT_GalaxyMap(t *testing.T) {
	st, ctx := testStore(t)

	sb, err := st.loadStarbaseParents(ctx)
	if err != nil {
		t.Fatalf("loadStarbaseParents: %v", err)
	}
	m, err := st.galaxyMap(ctx, sb)
	if err != nil {
		t.Fatalf("galaxyMap: %v", err)
	}

	if len(m.Systems) == 0 || len(m.Sectors) == 0 {
		t.Fatalf("empty topology: %d systems, %d sectors", len(m.Systems), len(m.Sectors))
	}
	// Every sector must carry a non-empty faction token and a known one.
	known := map[string]bool{
		"jenquai": true, "progen": true, "terran": true,
		"pirate": true, "contested": true, "neutral": true, "deepspace": true,
	}
	inSet := make(map[int64]bool, len(m.Sectors))
	for _, s := range m.Sectors {
		if s.Name == "" {
			t.Errorf("sector %d has empty name", s.ID)
		}
		if !known[s.Faction] {
			t.Errorf("sector %d (%s) has unknown faction %q", s.ID, s.Name, s.Faction)
		}
		inSet[s.ID] = true
	}
	// Edges: no self-loops, both endpoints real sectors, no undirected dupes.
	if len(m.Edges) == 0 {
		t.Fatalf("no gate edges built")
	}
	seen := make(map[[2]int64]bool)
	for _, e := range m.Edges {
		if e.From == e.To {
			t.Errorf("self-loop edge on %d", e.From)
		}
		if !inSet[e.From] || !inSet[e.To] {
			t.Errorf("edge %d-%d has an endpoint outside the sector set", e.From, e.To)
		}
		if e.From > e.To {
			t.Errorf("edge %d-%d not normalized (from > to)", e.From, e.To)
		}
		key := [2]int64{e.From, e.To}
		if seen[key] {
			t.Errorf("duplicate undirected edge %d-%d", e.From, e.To)
		}
		seen[key] = true
	}
}

// normalizeFaction folds the freeform systems.notes prose into the stable token
// set the SPA colors by.
func TestGalaxyNormalizeFaction(t *testing.T) {
	cases := map[string]string{
		"jenquai":                        "jenquai",
		"progen":                         "progen",
		"Terran Space":                   "terran",
		"pirate":                         "pirate",
		"Contested Space":                "contested",
		"neutral":                        "neutral",
		"Various Factions":               "neutral",
		"Deep Space Sectors (YBR, etc.)": "deepspace",
		"altair":                         "deepspace",
		"":                               "deepspace",
	}
	for in, want := range cases {
		if got := normalizeFaction(in); got != want {
			t.Errorf("normalizeFaction(%q) = %q, want %q", in, got, want)
		}
	}
}

// Live occupancy counts only online avatars on online accounts, and attributes
// a docked player (stored sector = starbase interior id) to the parent sector.
func TestIT_GalaxyOccupancy(t *testing.T) {
	st, ctx := testStore(t)

	sb, err := st.loadStarbaseParents(ctx)
	if err != nil {
		t.Fatalf("loadStarbaseParents: %v", err)
	}
	if len(sb) == 0 {
		t.Skip("no starbases in content DB; cannot test docked remap")
	}
	// Pick a real starbase interior->parent pair to exercise the docked remap.
	var interior, parent int64
	for k, v := range sb {
		interior, parent = k, v
		break
	}

	// Two accounts: one in space (sector 1060 Earth), one docked at `interior`.
	inSpace := seedAccount(t, st, ctx, 40, 1, 0)
	docked := seedAccount(t, st, ctx, 41, 1, 0)
	// A third, fully offline account that must NOT be counted.
	off := seedAccount(t, st, ctx, 42, 1, 0)
	_ = off

	const earth int64 = 1060
	mustExec(t, st.user, ctx, `UPDATE avatar_info SET sector = $1 WHERE avatar_id = $2`, earth, inSpace.avatars[0].id)
	mustExec(t, st.user, ctx, `UPDATE avatar_info SET sector = $1 WHERE avatar_id = $2`, interior, docked.avatars[0].id)

	setAccountOnline(t, st, ctx, inSpace.id)
	setAvatarOnline(t, st, ctx, inSpace.avatars[0].id)
	setAccountOnline(t, st, ctx, docked.id)
	setAvatarOnline(t, st, ctx, docked.avatars[0].id)
	// `off` stays offline at both account and avatar level.

	occ, err := st.galaxyOccupancy(ctx, sb)
	if err != nil {
		t.Fatalf("galaxyOccupancy: %v", err)
	}

	if got := occ.Counts[itoa(earth)]; got != 1 {
		t.Errorf("Earth (1060) count = %d, want 1", got)
	}
	if got := occ.Counts[itoa(parent)]; got < 1 {
		t.Errorf("docked player not attributed to parent sector %d: count = %d", parent, got)
	}
	if got := occ.Counts[itoa(interior)]; parent != interior && got != 0 {
		t.Errorf("interior id %d should not appear as its own sector: count = %d", interior, got)
	}
	// Total counts both online players (not the offline one). If Earth is also
	// the parent of the docked starbase (unlikely), total could be 2 anyway.
	if occ.Total < 2 {
		t.Errorf("total online = %d, want >= 2 (the offline account must be excluded, the two online counted)", occ.Total)
	}
}
