// SPDX-License-Identifier: MIT
// Freya Online -- rarity model (server-authoritative).
//
// Go mirror of web/src/lib/rarity.ts. The server is the authority; the client
// copy exists only so the dev mock / optimistic UI renders the same colour.
// Both MUST agree:
//
//   quality >= 180 -> epic ; >= 150 -> rare ; >= 130 -> uncommon ; else common
//   no quality (nil) -> always common
//   level cap: 1-3 -> uncommon, 4-6 -> rare, 7+ -> epic

package main

// rarityForLevelQuality derives the instance rarity. quality is nil when the
// item has no variable quality (always common).
func rarityForLevelQuality(level int, quality *float64) string {
	if quality == nil {
		return "common"
	}
	order := map[string]int{"common": 0, "uncommon": 1, "rare": 2, "epic": 3, "legendary": 4}

	var tier string
	switch q := *quality; {
	case q >= 180:
		tier = "epic"
	case q >= 150:
		tier = "rare"
	case q >= 130:
		tier = "uncommon"
	default:
		tier = "common"
	}

	var cap string
	switch {
	case level <= 3:
		cap = "uncommon"
	case level <= 6:
		cap = "rare"
	default:
		cap = "epic"
	}

	if order[tier] > order[cap] {
		return cap
	}
	return tier
}
