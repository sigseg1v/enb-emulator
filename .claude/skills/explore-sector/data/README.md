# explore-sector routing data

## `galaxy.json` -- the gate-routing map

Tells the survey driver (`drive_lua.py`) where every gate leads, so it can route
between sectors and across systems instead of only crossing gates blind. Built by
`../scripts/build_galaxy.py`; regenerate after the sector/gate layout changes:

```bash
python3 .claude/skills/explore-sector/scripts/build_galaxy.py   # writes galaxy.json
python3 .claude/skills/explore-sector/scripts/test_galaxy.py    # regression guard
```

Contents (keys): `gate_dest`, `gate_dest_by_sector`, `wormhole_pairs`,
`system_gate`, `system_entry`, `adjacency`, `sector_system`, `systems`,
`sid2name`. See the builder's module docstring for the shape of each.

### How gates are labelled (why two resolution paths exist)

EnB groups sectors into star **systems**. A gate's label depends on where it goes:

- **Intra-system** gate: labelled with the destination **sector** ("Gate to
  Jupiter"). Resolved directly to a sid.
- **Inter-system** gate: labelled with the destination **system** ("Gate to Beta
  Hydri System"). You land in that system's entry sector, which depends on the
  system you leave from -- so it resolves per source (`system_entry`).
- **Named wormhole**: a gate whose identical name appears in exactly two sectors
  (e.g. "Vishao's Gate" in both Antares Frontier and Vishao's Cove). Each endpoint
  resolves the shared name to the other (`gate_dest_by_sector`).

## Inputs

- **The content DB (`net7`)** -- authoritative and regenerable: every stargate
  object + its display name, and the sector-id -> name catalog. Queried read-only
  by the builder.
- **`systems.json`** -- system -> [sector ids] membership. This mapping is NOT in
  our DB; it was derived from the external reconstruct dataset's public galaxy
  maps (the enbmaps sector menu, which encodes system membership). Committed as a
  static topology reference -- regenerate only if the galaxy layout itself changes.
  Note: these gate-derived system names do NOT line up with the section headers in
  `docs/sectors/sector-ids.md` (those are the emulator's own DB grouping labels);
  membership must come from here, not that catalog.
