// SPDX-License-Identifier: MIT
// Freya Online -- server status cache.
//
// The website's always-visible ONLINE/OFFLINE + player count reads the singleton
// server_status row (heartbeated by the game server). We cache it for 60s so a
// burst of page loads does not hammer Postgres; the row itself is indexed by its
// primary key (id = 1), so the read is a single-row lookup.

package main

import (
	"context"
	"sync"
	"time"
)

const statusCacheTTL = 60 * time.Second

type statusCache struct {
	store     *Store
	staleSecs int

	mu       sync.Mutex
	cached   ServerStatus
	fetched  time.Time
	hasValue bool
}

func newStatusCache(store *Store, staleSecs int) *statusCache {
	return &statusCache{store: store, staleSecs: staleSecs}
}

// get returns the cached status, refreshing it from Postgres if older than the
// TTL. now is injected so tests are deterministic (Date.now()-free).
func (c *statusCache) get(ctx context.Context, now time.Time) ServerStatus {
	c.mu.Lock()
	defer c.mu.Unlock()

	if c.hasValue && now.Sub(c.fetched) < statusCacheTTL {
		return c.cached
	}

	c.cached = c.store.readServerStatus(ctx, c.staleSecs)
	c.fetched = now
	c.hasValue = true
	return c.cached
}
