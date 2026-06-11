// SPDX-License-Identifier: MIT
// Freya Online -- galaxy map reads (topology + live per-sector occupancy).
//
// The Galaxy tab draws every sector in the game as a node, grouped by its star
// system, with gate links between sectors, and lights each sector by how many
// players are in it right now. That splits cleanly into two reads with very
// different cadences:
//
//   - The TOPOLOGY (systems, sectors, gate edges) is content data that almost
//     never changes -- cached hard (galaxyMapTTL) and served to every client.
//   - The OCCUPANCY (live players per sector) changes constantly but is tiny --
//     a short-cached aggregate the SPA polls.
//
// Live presence is derived entirely from save-state, the same way the Discord
// /status bot does it (freya/status-notifier/bot.go readOnlinePlayers): an
// account is online while accounts.last_login > last_logout, and its active
// avatar while avatar_info.last_login > last_logout. No server change is needed
// to know who is where. A docked player's avatar_info.sector holds the
// starbase's interior id (a distinct high id space), not the parent sector, so
// we remap those to the parent sector_id via the starbases table -- otherwise a
// docked player would light up nothing on the map.
//
// Two pools as everywhere: the sector/system/gate catalogue and the starbase
// remap live in net7 (s.content); live presence lives in net7_user (s.user). A
// single connection cannot read across them, so each is its own query joined in
// Go.

package main

import (
	"context"
	"strconv"
	"strings"
	"sync"
	"time"
)

// normalizeFaction folds the freeform systems.notes label into a small stable
// token the SPA colors by. The content DB stores faction as prose ("Terran
// Space", "jenquai", "Contested Space", "Deep Space Sectors (YBR, etc.)"), so we
// reduce it to a known set; anything unrecognized (the outer named regions --
// Altair, Castor, Eridani, ...) becomes "deepspace".
func normalizeFaction(notes string) string {
	n := strings.ToLower(notes)
	switch {
	case strings.Contains(n, "jenquai"):
		return "jenquai"
	case strings.Contains(n, "progen"):
		return "progen"
	case strings.Contains(n, "terran"):
		return "terran"
	case strings.Contains(n, "pirate"):
		return "pirate"
	case strings.Contains(n, "contested"):
		return "contested"
	case strings.Contains(n, "neutral"), strings.Contains(n, "various"):
		return "neutral"
	default:
		return "deepspace"
	}
}

// GalaxySystem is one star system node-group.
type GalaxySystem struct {
	ID      int    `json:"id"`
	Name    string `json:"name"`
	Faction string `json:"faction"`
}

// GalaxySector is one sector node, tagged with the system it belongs to (so the
// SPA can cluster it) and the system's faction (so it can color it).
type GalaxySector struct {
	ID       int64  `json:"id"`
	Name     string `json:"name"`
	SystemID int    `json:"systemId"`
	System   string `json:"system"`
	Faction  string `json:"faction"`
}

// GalaxyEdge is an undirected gate link between two sectors.
type GalaxyEdge struct {
	From int64 `json:"from"`
	To   int64 `json:"to"`
}

// GalaxyMap is the static topology: every named sector, its system, and the
// sector-to-sector gate graph.
type GalaxyMap struct {
	Systems []GalaxySystem `json:"systems"`
	Sectors []GalaxySector `json:"sectors"`
	Edges   []GalaxyEdge   `json:"edges"`
}

// GalaxyOccupancy is the live light-up data: a count of online players per
// sector id (string-keyed for the JSON map / direct SPA lookup) and the total.
type GalaxyOccupancy struct {
	Counts map[string]int `json:"counts"`
	Total  int            `json:"total"`
}

// loadStarbaseParents maps a starbase interior id (the high-id-space value the
// server stores in avatar_info.sector when a player is docked, and the target of
// dock gates) to its parent sector_id. Content-pool read.
func (s *Store) loadStarbaseParents(ctx context.Context) (map[int64]int64, error) {
	rows, err := s.content.Query(ctx, `
		SELECT starbase_sector_id, sector_id
		  FROM starbases
		 WHERE starbase_sector_id IS NOT NULL AND sector_id IS NOT NULL`)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	out := make(map[int64]int64)
	for rows.Next() {
		var interior, parent int64
		if err := rows.Scan(&interior, &parent); err != nil {
			return nil, err
		}
		out[interior] = parent
	}
	return out, rows.Err()
}

// galaxyMap builds the full topology from the content DB. Sectors without a
// name are skipped (unnamed ids are internal/placeholder). Edges come from
// stargate objects' gate_to; a gate that targets a starbase interior is remapped
// to the parent sector, which collapses dock gates into self-links that are then
// dropped -- so only genuine sector-to-sector gates survive. Edges are
// deduplicated undirected (min,max).
func (s *Store) galaxyMap(ctx context.Context, starbase map[int64]int64) (GalaxyMap, error) {
	var m GalaxyMap

	sysRows, err := s.content.Query(ctx,
		`SELECT system_id, coalesce(name, ''), coalesce(notes, '') FROM systems ORDER BY system_id`)
	if err != nil {
		return m, err
	}
	for sysRows.Next() {
		var g GalaxySystem
		var notes string
		if err := sysRows.Scan(&g.ID, &g.Name, &notes); err != nil {
			sysRows.Close()
			return m, err
		}
		g.Faction = normalizeFaction(notes)
		m.Systems = append(m.Systems, g)
	}
	sysRows.Close()
	if err := sysRows.Err(); err != nil {
		return m, err
	}

	// Faction per system, for tagging sectors.
	sysFaction := make(map[int]string, len(m.Systems))
	for _, g := range m.Systems {
		sysFaction[g.ID] = g.Faction
	}

	secRows, err := s.content.Query(ctx, `
		SELECT s.sector_id, s.name, coalesce(s.system_id, 0), coalesce(sys.name, '')
		  FROM sectors s
		  LEFT JOIN systems sys ON sys.system_id = s.system_id
		 WHERE s.name IS NOT NULL
		 ORDER BY s.sector_id`)
	if err != nil {
		return m, err
	}
	inMap := make(map[int64]bool)
	for secRows.Next() {
		var g GalaxySector
		if err := secRows.Scan(&g.ID, &g.Name, &g.SystemID, &g.System); err != nil {
			secRows.Close()
			return m, err
		}
		g.Faction = sysFaction[g.SystemID]
		if g.Faction == "" {
			g.Faction = "deepspace"
		}
		m.Sectors = append(m.Sectors, g)
		inMap[g.ID] = true
	}
	secRows.Close()
	if err := secRows.Err(); err != nil {
		return m, err
	}

	gateRows, err := s.content.Query(ctx, `
		SELECT DISTINCT sector_id, gate_to
		  FROM sector_objects
		 WHERE gate_to IS NOT NULL AND gate_to > 0`)
	if err != nil {
		return m, err
	}
	seen := make(map[[2]int64]bool)
	for gateRows.Next() {
		var from, to int64
		if err := gateRows.Scan(&from, &to); err != nil {
			gateRows.Close()
			return m, err
		}
		if p, ok := starbase[to]; ok { // dock-gate target -> parent sector
			to = p
		}
		if from == to || !inMap[from] || !inMap[to] {
			continue
		}
		key := [2]int64{from, to}
		if from > to {
			key = [2]int64{to, from}
		}
		if seen[key] {
			continue
		}
		seen[key] = true
		m.Edges = append(m.Edges, GalaxyEdge{From: key[0], To: key[1]})
	}
	gateRows.Close()
	if err := gateRows.Err(); err != nil {
		return m, err
	}
	return m, nil
}

// galaxyOccupancy counts online players per sector. Live presence is the same
// definition the /status bot uses: the account is logged in (last_login >
// last_logout) and the avatar is its active, non-deleted character. Docked
// players (whose stored sector is a starbase interior id) are attributed to the
// parent sector via the starbase map.
func (s *Store) galaxyOccupancy(ctx context.Context, starbase map[int64]int64) (GalaxyOccupancy, error) {
	rows, err := s.user.Query(ctx, `
		SELECT i.sector, count(*)
		  FROM accounts a
		  JOIN avatar_info i
		    ON i.account_id = a.id
		   AND i.last_login > i.last_logout
		   AND i.deleted_at IS NULL
		 WHERE a.last_login > a.last_logout
		 GROUP BY i.sector`)
	if err != nil {
		return GalaxyOccupancy{}, err
	}
	defer rows.Close()

	counts := make(map[string]int)
	total := 0
	for rows.Next() {
		var sector int64
		var n int
		if err := rows.Scan(&sector, &n); err != nil {
			return GalaxyOccupancy{}, err
		}
		if p, ok := starbase[sector]; ok { // docked -> parent sector
			sector = p
		}
		counts[strconv.FormatInt(sector, 10)] += n
		total += n
	}
	if err := rows.Err(); err != nil {
		return GalaxyOccupancy{}, err
	}
	return GalaxyOccupancy{Counts: counts, Total: total}, nil
}

// galaxyMapTTL is how long the static topology is cached. It is content data
// that effectively never changes at runtime, so a long TTL is safe and keeps the
// (relatively heavy) three-query build off the hot path.
const galaxyMapTTL = 5 * time.Minute

// galaxyOccupancyTTL bounds how stale the light-up data can be. Short enough to
// feel live, long enough that many clients polling does not hammer Postgres.
const galaxyOccupancyTTL = 15 * time.Second

// galaxyCache memoizes the topology, the starbase remap, and the live occupancy,
// each on its own clock. now is injected so tests stay deterministic.
type galaxyCache struct {
	store *Store

	mu sync.Mutex

	starbase    map[int64]int64
	starbaseAt  time.Time
	starbaseHas bool
	cachedMap   *GalaxyMap
	mapAt       time.Time
	cachedOcc   *GalaxyOccupancy
	occAt       time.Time
}

func newGalaxyCache(store *Store) *galaxyCache { return &galaxyCache{store: store} }

// starbaseParents returns the cached interior->parent map, refreshing it on the
// topology TTL. Caller holds c.mu.
func (c *galaxyCache) starbaseParents(ctx context.Context, now time.Time) (map[int64]int64, error) {
	if c.starbaseHas && now.Sub(c.starbaseAt) < galaxyMapTTL {
		return c.starbase, nil
	}
	sb, err := c.store.loadStarbaseParents(ctx)
	if err != nil {
		if c.starbaseHas {
			return c.starbase, nil // serve stale rather than fail the whole read
		}
		return nil, err
	}
	c.starbase = sb
	c.starbaseAt = now
	c.starbaseHas = true
	return sb, nil
}

// getMap returns the cached topology, rebuilding it past the TTL.
func (c *galaxyCache) getMap(ctx context.Context, now time.Time) (GalaxyMap, error) {
	c.mu.Lock()
	defer c.mu.Unlock()
	if c.cachedMap != nil && now.Sub(c.mapAt) < galaxyMapTTL {
		return *c.cachedMap, nil
	}
	sb, err := c.starbaseParents(ctx, now)
	if err != nil {
		return GalaxyMap{}, err
	}
	m, err := c.store.galaxyMap(ctx, sb)
	if err != nil {
		return GalaxyMap{}, err
	}
	c.cachedMap = &m
	c.mapAt = now
	return m, nil
}

// getOccupancy returns the cached live counts, refreshing past the short TTL.
func (c *galaxyCache) getOccupancy(ctx context.Context, now time.Time) (GalaxyOccupancy, error) {
	c.mu.Lock()
	defer c.mu.Unlock()
	if c.cachedOcc != nil && now.Sub(c.occAt) < galaxyOccupancyTTL {
		return *c.cachedOcc, nil
	}
	sb, err := c.starbaseParents(ctx, now)
	if err != nil {
		return GalaxyOccupancy{}, err
	}
	occ, err := c.store.galaxyOccupancy(ctx, sb)
	if err != nil {
		return GalaxyOccupancy{}, err
	}
	c.cachedOcc = &occ
	c.occAt = now
	return occ, nil
}
