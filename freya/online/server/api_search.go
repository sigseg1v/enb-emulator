// SPDX-License-Identifier: MIT
// Freya Online -- item search endpoint (GET /api/search).

package main

import (
	"context"
	"log"
	"net/http"
	"strings"
	"time"
)

// searchResultLimit caps how many distinct items one query returns, so a broad
// term (e.g. "laser") cannot fan out to thousands of items and their drop lists.
const searchResultLimit = 50

// GET /api/search?q=<item name> -- fuzzy item lookup returning each matched
// item's level, whether it is manufacturable, and the mobs that drop it (with
// each mob's combat level and sector). Public: read-only game-catalogue data.
func (s *apiServer) handleSearch(w http.ResponseWriter, r *http.Request) {
	q := strings.TrimSpace(r.URL.Query().Get("q"))
	if q == "" {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "missing search term"})
		return
	}
	// Guard against a pathologically long term; the DB binds it as a parameter
	// regardless, this just avoids pointless work.
	if len(q) > 64 {
		q = q[:64]
	}
	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
	defer cancel()

	items, err := s.store.searchItems(ctx, q, searchResultLimit)
	if err != nil {
		log.Printf("api: search %q: %v", q, err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "search failed"})
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{
		"query":   q,
		"results": items,
	})
}
