// SPDX-License-Identifier: MIT
// Freya Online -- galaxy traffic flow (live sector-to-sector movement).
//
// The Galaxy tab animates player movement: every time someone gates from one
// sector to another, a light travels along the lane connecting the two. The
// server records each transition into gate_events (net7_user); this read groups
// the trailing window by ORDERED (from, to) pair and counts the events, and the
// count is exactly how many lights the SPA draws travelling that directed lane.
//
// Direction matters -- A->B and B->A are different ordered pairs and animate in
// opposite directions -- so unlike the undirected gate-edge graph in
// store_galaxy.go we do NOT canonicalise (min,max) here.
//
// A docked/undocked transition has a starbase interior id on one side (the high
// id space the server stores while docked). We remap both endpoints to their
// parent sector via the same starbase map occupancy uses, then drop self-hops:
// an undock (interior -> parent) and a dock (parent -> interior) both collapse to
// the parent sector, leaving nothing to draw -- correct, because the map has no
// node for a station distinct from its sector. Only genuine sector-to-sector
// gate jumps survive as drawable lanes. The server still records dock/undock
// (cheap, and the avatar_id is kept for a future per-player trail); the map just
// has no lane to animate for an in-sector dock.
//
// gate_events is read-only from the website's side: retention is owned by the
// server, which prunes rows older than an hour on the same write that inserts a
// new transition (SaveManager::HandleGateEvent). The website never deletes.

package main

import (
	"context"
	"time"
)

// GateLane is one directed flow: Count players moved From -> To in the window.
type GateLane struct {
	From  int64 `json:"from"`
	To    int64 `json:"to"`
	Count int   `json:"count"`
}

// GalaxyGateFlow is the live directed-traffic payload the SPA animates.
type GalaxyGateFlow struct {
	Lanes []GateLane `json:"lanes"`
}

// galaxyGateFlowWindow is how far back a transition still animates. The user-
// facing spec: "all gate events in the last 5 minutes".
const galaxyGateFlowWindow = "5 minutes"

// galaxyGateFlowTTL bounds how stale the flow data can be. The spec asks the
// website to cache this query and refresh it every 60s when someone asks for it.
const galaxyGateFlowTTL = 60 * time.Second

// galaxyGateFlow reads the trailing window of transitions grouped by directed
// pair, remapping starbase-interior endpoints to their parent sector and
// dropping the resulting self-hops. Retention is the server's job (it prunes on
// insert), so this is a pure read.
func (s *Store) galaxyGateFlow(ctx context.Context, starbase map[int64]int64) (GalaxyGateFlow, error) {
	rows, err := s.user.Query(ctx, `
		SELECT from_sector, to_sector, count(*)
		  FROM gate_events
		 WHERE created_at > now() - ($1)::interval
		 GROUP BY from_sector, to_sector`, galaxyGateFlowWindow)
	if err != nil {
		return GalaxyGateFlow{}, err
	}
	defer rows.Close()

	// Remap interior ids to parent sectors, then re-aggregate by directed pair
	// (the remap can collapse two raw pairs onto one drawable lane).
	merged := make(map[[2]int64]int)
	for rows.Next() {
		var from, to int64
		var n int
		if err := rows.Scan(&from, &to, &n); err != nil {
			return GalaxyGateFlow{}, err
		}
		if p, ok := starbase[from]; ok {
			from = p
		}
		if p, ok := starbase[to]; ok {
			to = p
		}
		if from == to { // dock/undock collapsed to one sector -- nothing to draw
			continue
		}
		merged[[2]int64{from, to}] += n
	}
	if err := rows.Err(); err != nil {
		return GalaxyGateFlow{}, err
	}

	flow := GalaxyGateFlow{Lanes: make([]GateLane, 0, len(merged))}
	for k, n := range merged {
		flow.Lanes = append(flow.Lanes, GateLane{From: k[0], To: k[1], Count: n})
	}
	return flow, nil
}

// getGateFlow returns the cached directed-traffic flow, refreshing past the TTL.
// Mirrors getOccupancy: the starbase remap is shared and time is injected.
func (c *galaxyCache) getGateFlow(ctx context.Context, now time.Time) (GalaxyGateFlow, error) {
	c.mu.Lock()
	defer c.mu.Unlock()
	if c.cachedFlow != nil && now.Sub(c.flowAt) < galaxyGateFlowTTL {
		return *c.cachedFlow, nil
	}
	sb, err := c.starbaseParents(ctx, now)
	if err != nil {
		return GalaxyGateFlow{}, err
	}
	flow, err := c.store.galaxyGateFlow(ctx, sb)
	if err != nil {
		return GalaxyGateFlow{}, err
	}
	c.cachedFlow = &flow
	c.flowAt = now
	return flow, nil
}
