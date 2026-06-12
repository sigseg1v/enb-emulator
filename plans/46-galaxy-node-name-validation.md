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

RESOLVED 2026-06-12 -- owner-confirmed every row. 8 nodes aliased, 2 confirmed
dark. The aliases live in `NODE_ALIAS` in `Galaxy.tsx` (applied via `nodeNorm()`
before the `idByNorm` lookup at both the presence-count and my-avatar-marker
sites). Initial candidate hunt found 5 (rows 1-5); the owner supplied the real
sector names for 4 of the remaining 5 (rows 6-9), of which 3 resolved to real
occupiable sectors and 1 (Lagarto Moon Risco) is a nav inside the Lagarto sector.

| # | Node (design)         | DB sector aliased to (id, name)                      | Case | Status |
|---|-----------------------|------------------------------------------------------|------|--------|
| 1 | Inverness Planet      | 4093 `Planet Inverness`                              | A    | [x] aliased |
| 2 | Grissom Planet        | 4595 `Grissom Meteorological Site ` (trailing space) | A    | [x] aliased |
| 3 | Arduinne Planet       | 4095 `Arduinne Gas Cloud`                            | A    | [x] aliased |
| 4 | Primus Planet         | 3595 `Praetorium Mons Area (Planet Primus)`          | A    | [x] aliased |
| 5 | Dahin Planet          | 1930 `Dahin Mining Interest`                         | A    | [x] aliased |
| 6 | Swooping Eagle Planet | 4195 `Yasuragi Area ` (trailing space)              | A    | [x] aliased |
| 7 | Zweihander Planet     | 1493 `Jagerstadt`                                   | A    | [x] aliased |
| 8 | Endriago Planet       | 2295 `Porvenir Mons Area`                           | A    | [x] aliased |
| 9 | Lagarto Moon Risco    | (none -- Risco is a nav INSIDE 2215 `Lagarto`)      | B    | [x] dark |
| 10| FishBowl              | (none -- unreachable)                                | B    | [x] dark |

Validation evidence (per candidate, `freya-dev-postgres-1` / db `net7`): each
aliased sector is a normal sector (NOT a `starbases.starbase_sector_id` interior)
whose only gate-out is back to its base sector -- the planet-sub-sector dead-end
pattern -- so a docked/in-space pilot there is genuinely in that sector id and
lights the node. No double-mapping: each base node ("Inverness", "Grissom", ...)
already matched its own sector, so the "Planet" node claims the previously
orphaned dead-end.

Owner notes that fed rows 6-9: Swooping Eagle Planet was "Master Cha'han's Dojo or
Yasuragi" -- there is no Cha'han/Dojo sector row, only `Yasuragi Area` (4195), so
that is the one. Zweihander Planet = `Jagerstadt`. Endriago Planet = `Porvenir
Mons Area`. Lagarto Moon Risco is reached via the Risco nav inside the Lagarto
sector (Gallina system) -- no standalone sector, so it correctly stays dark
(aliasing it to 2215 would double-count the "Lagarto" node).

## Done criteria (met)

- [x] Every row flipped with its case (A-aliased / B-confirmed-dark) recorded.
- [x] A-rows aliased in `Galaxy.tsx` (`NODE_ALIAS` + `nodeNorm()`); `npm run build`
      clean. Real-client pilot-lights-the-node check is the last manual step the
      owner does when convenient (the id bridge is byte-verified against the DB).
- [x] This file updated.
