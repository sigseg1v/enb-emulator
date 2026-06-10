// deaths.go -- in-memory wreck/jumpstart correlation for the status relay.
//
// The game server emits two related events: `player_destroyed` when a player is
// wrecked, and `jumpstarted` when another player revives them IN THE SAME SECTOR
// (rather than the wreck towing to a station). The desired Discord behaviour is:
//
//   - On a wreck, post "Player X (C25) was destroyed by <enemy> in <sector>."
//   - If the player tows to a station, leave that line as-is (we never see a
//     jumpstart, so nothing more happens -- correct by omission).
//   - If instead they are jumpstarted within a short window, EDIT the original
//     wreck message: strike it through and append the jumpstart line.
//
// To edit the original message we must remember the Discord message id we got
// back when we posted the wreck, keyed by the player. This state is deliberately
// PROCESS-MEMORY ONLY -- not persisted. It is a best-effort cosmetic follow-up:
// if the sidecar restarts between the wreck and the jumpstart, we simply forget
// the wreck and the arriving jumpstart is dropped (no standalone post). That is
// an acceptable loss for a non-durable annotation, and avoids growing the schema
// or coupling the durable outbox to Discord message ids.
package main

import (
	"strings"
	"sync"
	"time"
)

// jumpstartWindow bounds how long after a wreck a jumpstart still edits the
// original death message. Past this, the wreck line stands on its own and the
// jumpstart event is consumed without posting anything.
const jumpstartWindow = 30 * time.Minute

// deathRecord remembers one posted wreck line so a later jumpstart can edit it.
type deathRecord struct {
	messageID string    // Discord id of the posted wreck message
	content   string    // the original (un-struck) wreck line
	when      time.Time // when we posted it
}

// deathTracker maps a player's (server-unique) avatar name to their most recent
// posted wreck. Guarded by a mutex: the drain loop is single-goroutine today, but
// the lock keeps it safe if that ever changes and is uncontended regardless.
type deathTracker struct {
	mu sync.Mutex
	m  map[string]deathRecord
}

func newDeathTracker() *deathTracker {
	return &deathTracker{m: map[string]deathRecord{}}
}

// recordDeath stores (replacing any prior) the wreck for player. It also drops
// any entries older than the window so a server that never jumpstarts cannot grow
// the map without bound.
func (t *deathTracker) recordDeath(player, messageID, content string, now time.Time) {
	t.mu.Lock()
	defer t.mu.Unlock()
	for k, r := range t.m {
		if now.Sub(r.when) > jumpstartWindow {
			delete(t.m, k)
		}
	}
	t.m[player] = deathRecord{messageID: messageID, content: content, when: now}
}

// recentDeath returns the player's wreck if one is on file AND still inside the
// window. It does NOT remove it -- the caller clears it only after a successful
// edit, so a failed Discord edit can be retried on the next drain. A stale entry
// is pruned here and reported as a miss.
func (t *deathTracker) recentDeath(player string, now time.Time) (deathRecord, bool) {
	t.mu.Lock()
	defer t.mu.Unlock()
	r, ok := t.m[player]
	if !ok {
		return deathRecord{}, false
	}
	if now.Sub(r.when) > jumpstartWindow {
		delete(t.m, player)
		return deathRecord{}, false
	}
	return r, true
}

// clear removes the player's wreck record (called after a successful edit).
func (t *deathTracker) clear(player string) {
	t.mu.Lock()
	defer t.mu.Unlock()
	delete(t.m, player)
}

// playerNameFromContent extracts the avatar name from a rendered wreck/jumpstart
// line. Both render as "Player <Name> ..." and EnB first names are a single
// whitespace-free token, so the second field is the name. Returns "" if the line
// does not match that shape (then correlation is skipped, never crashes).
func playerNameFromContent(content string) string {
	f := strings.Fields(content)
	if len(f) >= 2 && f[0] == "Player" {
		return f[1]
	}
	return ""
}

// strikethrough wraps s in Discord strikethrough markdown.
func strikethrough(s string) string {
	return "~~" + s + "~~"
}
