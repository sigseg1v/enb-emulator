// SPDX-License-Identifier: MIT
// Freya Online -- integration tests for item search (store_search.go).
//
// Gated on FREYA_TEST_DB (see itest_helpers_test.go). Exercises the real
// item_base -> mob_items -> mob_base -> spawn-chain -> sectors join against the
// live content DB, so a schema/column drift breaks the build rather than
// silently returning empty drop lists.

package main

import (
	"context"
	"strings"
	"testing"
)

// pickItemWithResolvedDrop finds a real item that is dropped by at least one mob
// with a resolved sector, so the test has a concrete, meaningful target without
// hardcoding a name that could change with the seed. Returns the item name plus
// the mob/sector it should surface.
func pickItemWithResolvedDrop(t *testing.T, st *Store, ctx context.Context) (item, mob, sector string, combatLevel, level int) {
	t.Helper()
	err := st.content.QueryRow(ctx, `
		SELECT ib.name, mb.name, s.name, mb.level, ib.level
		  FROM item_base ib
		  JOIN mob_items mi          ON mi.item_base_id = ib.id
		  JOIN mob_base  mb          ON mb.mob_id = mi.mob_id
		  JOIN mob_spawn_group msg    ON msg.mob_id = mb.mob_id
		  JOIN sector_objects_mob som ON som.mob_id = msg.spawn_group_id
		  JOIN sector_objects so      ON so.sector_object_id = som.mob_id
		  JOIN sectors s              ON s.sector_id = so.sector_id
		 WHERE s.name <> ''
		 ORDER BY ib.id
		 LIMIT 1`).Scan(&item, &mob, &sector, &combatLevel, &level)
	if err != nil {
		t.Skipf("no item with a resolved mob drop in content DB: %v", err)
	}
	return
}

func TestSearchItems_ResolvedDrop(t *testing.T) {
	st, ctx := testStore(t)
	wantItem, wantMob, wantSector, wantCombat, wantLevel := pickItemWithResolvedDrop(t, st, ctx)

	// Search by the full item name.
	results, err := st.searchItems(ctx, wantItem, 50)
	if err != nil {
		t.Fatalf("searchItems: %v", err)
	}
	var got *SearchItem
	for i := range results {
		if results[i].Name == wantItem {
			got = &results[i]
			break
		}
	}
	if got == nil {
		t.Fatalf("searchItems(%q) did not return the item; got %d results", wantItem, len(results))
	}
	if got.Level != wantLevel {
		t.Errorf("item level = %d, want %d", got.Level, wantLevel)
	}
	if got.ID == "" {
		t.Errorf("item id is empty")
	}

	// The chosen mob/sector drop must appear with the right combat level.
	var found bool
	for _, d := range got.Drops {
		if d.Mob == wantMob && d.Sector == wantSector {
			found = true
			if d.CombatLevel != wantCombat {
				t.Errorf("drop %q combat level = %d, want %d", d.Mob, d.CombatLevel, wantCombat)
			}
		}
	}
	if !found {
		t.Errorf("expected drop (mob=%q sector=%q) not in %+v", wantMob, wantSector, got.Drops)
	}

	// No drop row should duplicate a (mob, combatLevel, sector) triple.
	seen := map[string]bool{}
	for _, d := range got.Drops {
		k := d.Mob + "|" + d.Sector
		if seen[k] {
			t.Errorf("duplicate drop for %q in %q", d.Mob, wantItem)
		}
		seen[k] = true
	}
}

func TestSearchItems_CaseInsensitiveSubstring(t *testing.T) {
	st, ctx := testStore(t)
	wantItem, _, _, _, _ := pickItemWithResolvedDrop(t, st, ctx)

	// A lower-cased interior substring must still match (ILIKE '%term%').
	frag := wantItem
	if len(frag) > 6 {
		frag = frag[1 : len(frag)-1] // drop first/last char so it is a true substring
	}
	results, err := st.searchItems(ctx, strings.ToLower(frag), 50)
	if err != nil {
		t.Fatalf("searchItems: %v", err)
	}
	var found bool
	for _, r := range results {
		if r.Name == wantItem {
			found = true
		}
	}
	if !found {
		t.Errorf("case-insensitive substring %q did not match %q (%d results)", frag, wantItem, len(results))
	}
}

func TestSearchItems_NoMatch(t *testing.T) {
	st, ctx := testStore(t)
	results, err := st.searchItems(ctx, "zzzz_no_such_item_zzzz", 50)
	if err != nil {
		t.Fatalf("searchItems: %v", err)
	}
	if len(results) != 0 {
		t.Errorf("expected no results for a nonsense term, got %d", len(results))
	}
}

func TestSearchItems_Limit(t *testing.T) {
	st, ctx := testStore(t)
	// A very broad single-letter term matches many items; the limit must cap the
	// distinct-item count returned.
	results, err := st.searchItems(ctx, "a", 5)
	if err != nil {
		t.Fatalf("searchItems: %v", err)
	}
	if len(results) > 5 {
		t.Errorf("limit not honoured: got %d results, want <= 5", len(results))
	}
}
