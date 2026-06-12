# Plan 46 -- Galaxy map: validate the unmatched node names

The website Galaxy tab (`freya/online/web/src/screens/Galaxy.tsx`) draws 84 authored
sector nodes at fixed coordinates. Live pilot presence is wired by normalizing each
node name with `gnorm()` and looking it up against the DB sector names (also `gnorm()`'d)
that come back from `/api/galaxy`, then reading `counts[sectorId]` from
`/api/galaxy/occupancy`.

74 / 84 nodes match a DB sector cleanly. **10 do not.** A non-matching node renders
fine but can never light up (it always reads 0 pilots). All 10 are minor sub-locations
(Planet / Moon / a nickname), so the practical impact is low -- but each needs a human
to confirm it is *genuinely* unmatchable vs. a fixable name skew.

## How to validate each one

For a candidate DB row, decide which case it is:

- **(A) Fixable name skew** -- the DB has a real, standalone sector that IS this node,
  just under a different name/word-order. Players can actually be *in* that sector.
  -> FIX: add an explicit alias so `gnorm(nodeName)` resolves to that sector id. Put the
  alias map in `Galaxy.tsx` next to `gnorm` (a `const NODE_ALIAS: Record<string,string>`
  mapping the node name to the DB sector name, applied before `gnorm` in the
  `idByNorm`/`presence` lookup). Verify a docked/in-space pilot there lights the node.
- **(B) Genuinely no standalone sector** -- the "planet/moon" is a point-of-interest
  inside its parent sector, or a starbase interior. Docked pilots are already remapped to
  the *parent* sector by the backend (`store_galaxy.go` starbase remap), so the parent
  node lights and this sub-node correctly stays dark.
  -> NO FIX. Leave the node as decoration. Note it here as confirmed-dark.

Quick DB check per candidate (compose project is `freya-<branch>`; on `website-dev2`):
```
docker exec freya-website-dev2-postgres-1 psql -U net7 -d net7 -c \
  "SELECT sector_id, name, system_id FROM sectors WHERE name ILIKE '%<term>%';"
```
To see if a sector can hold a live pilot, check whether it is a normal sector vs. a
starbase interior id (join `starbases.starbase_sector_id`).

## The 10 nodes (design name -> best DB candidate)

| # | Node (design)         | gnorm key            | Candidate DB row (id, name)                          | Likely | Status |
|---|-----------------------|----------------------|------------------------------------------------------|--------|--------|
| 1 | Inverness Planet      | invernessplanet      | 4093 `Planet Inverness`                              | **A**  | [ ] |
| 2 | Grissom Planet        | grissomplanet        | 4595 `Grissom Meteorological Site ` (trailing space) | **A**  | [ ] |
| 3 | Arduinne Planet       | arduinneplanet       | 4095 `Arduinne Gas Cloud`                            | **A?** | [ ] |
| 4 | Primus Planet         | primusplanet         | 3595 `Praetorium Mons Area (Planet Primus)`          | **A?** | [ ] |
| 5 | Dahin Planet          | dahinplanet          | 1930 `Dahin Mining Interest`                         | **A?** | [ ] |
| 6 | Swooping Eagle Planet | swoopingeagleplanet  | (none -- only 4120 `Swooping Eagle`)                | **B**  | [ ] |
| 7 | Zweihander Planet     | zweihanderplanet     | (none -- only 1425 `Zweihander`)                    | **B**  | [ ] |
| 8 | Endriago Planet       | endriagoplanet       | (none -- only 2210 `Endriago`)                      | **B**  | [ ] |
| 9 | Lagarto Moon Risco    | lagartomoonrisco     | (none -- only 2215 `Lagarto`)                       | **B**  | [ ] |
| 10| FishBowl              | fishbowl             | (none at all)                                        | **B**  | [ ] |

Notes:
- #1 is the strongest fix candidate: pure word-order skew (`Inverness Planet` vs
  `Planet Inverness`). Confirm it is a standalone sector, then alias it.
- #2 trailing-space DB name already normalizes away under `gnorm`; the mismatch is
  `Planet` vs `Meteorological Site`. Confirm same place, then alias.
- #3-#5 are "A?" because the DB candidate is a descriptive variant (Gas Cloud / Mons
  Area / Mining Interest). Confirm it is the same in-game location AND a sector a pilot
  can occupy before aliasing; otherwise mark B.
- #6-#10: if the DB truly has no separate sector row, these are POIs inside the parent
  and stay dark by design. Just record the confirmation.

## Done criteria

- Every row above flipped to `[x]` with the case (A-aliased / B-confirmed-dark) recorded.
- Any A-row has its alias added in `Galaxy.tsx` and a manual check that a pilot there
  lights the node.
- This file updated; `plans/00-master.md` ticked if it tracks plan 46.
