# Phase Y — Reference-data import (NPCs / skills / buffs / sector maps / items / quests)

Goal: close the **curatorial** gap between our server and retail. The
Phase K byte-diff work has reduced the StationLogin handshake stream
to a 73-frame ItemBase deficit + a handful of per-character-state
gaps (see `tests/integration/CliClient.IntegrationTests/Verification/DockHandshakeFriendship7Tests.cs`).
That residual deficit is **almost entirely DB seed**: starbase 73's
lounge NPC roster, equipped-item EquipEffect rows, ability/skill
definitions, sector route topology, mission/quest tables.

None of these tasks are server-code changes. Each is a one-shot
DB-seed import from a primary-source data dump.

## Process

**Every task in this phase is GATED on the user providing an
up-to-date reference dump for the corresponding data category.**
Do not start any task in this phase without:

1. Explicit user approval to begin the task, AND
2. A pointer to an up-to-date reference: a MySQL dump file path,
   a CSV from a Net-7 admin export, a packet capture with the
   relevant authoritative rows, a docs/RTF spec from the kyp-snapshot,
   or equivalent primary source per the CLAUDE.md server-integrity
   rules.

The kyp-snapshot under `archive/kyp-snapshot/` contains an OLDER
reference set; do not assume it is current. The user has stated they
will provide newer references when each task is approved.

## Items

- [ ] **Y1: NPC roster import** -- populate `npc`, `npc_lounge`,
  `npc_dialog`, `npc_avatar` (and any related lookup tables in
  `db/postgres/`) with the full Net-7-era NPC roster. Primary use:
  closes the 73-frame ItemBase deficit on the Friendship 7 dock
  handshake (the missing rows are the lounge NPC inventory and
  avatar/ship descriptors that Friendship 7's `LoungeNPC_45151.dat`
  references). Acceptance: `DockHandshakeFriendship7Tests` ItemBase
  count climbs from 4 toward 77.
  **STATUS: AWAITING USER APPROVAL + REFERENCE DATA.**

- [ ] **Y2: Skill definitions import** -- populate the `skill` /
  `skill_data` / class-skill-map tables so `SkillsList()` at
  `server/src/PlayerClass.cpp:362` returns the full per-class skill
  unlock matrix instead of the bare defaults a fresh test character
  sees today. Touches: SkillsList wire-frame contents at zone-in,
  the `0x001B` AuxData ship-stats frame that includes derived skill
  bonuses, and the per-equip skill-gated install checks.
  **STATUS: AWAITING USER APPROVAL + REFERENCE DATA.**

- [ ] **Y3: Buff / item-effect definitions import** -- populate the
  `item_effect_data` / buff tables that drive
  `ItemBase::EquipEffect()` lookups in `server/src/Equipable.cpp:1357`.
  Closes the gap that makes our fresh-character `m_Effects.SendEffects`
  iterate an empty list while retail emits one or more `0x0009`
  ObjectEffect frames per persistent-buff slot.
  **STATUS: AWAITING USER APPROVAL + REFERENCE DATA.**

- [ ] **Y4: Sector map / system topology import** -- populate
  `sector`, `sector_link`, `system`, `nav_object`, `gate` and the
  associated coordinates / faction-region tables so warp routes and
  the in-game galaxy map match retail. Closes the `m_ObjectMgr->SendAllNavs`
  curatorial gap on the SectorLogin (space-arm) path -- not the
  station path -- but is a prerequisite for any post-Phase-K cross-
  sector navigation testing.
  **STATUS: AWAITING USER APPROVAL + REFERENCE DATA.**

- [ ] **Y5: Item catalog import** -- populate `item_base` /
  `item_template` / equip-slot / inventory-stack-limit tables with
  the full retail item catalog (~10k+ rows). Acceptance: a fresh-
  character's starter-loadout `SendItemBase` calls in
  `server/src/PlayerClass.cpp:1080-1083` resolve to a real template,
  not the placeholder-or-NULL most rows currently return.
  **STATUS: AWAITING USER APPROVAL + REFERENCE DATA.**

- [ ] **Y6: Mission / quest catalog import** -- populate
  `mission`, `mission_step`, `mission_dialog`, `mission_npc_link`
  tables so mission terminals (`0x0011` `Hijackee` interactions,
  job-terminal flows) return real missions instead of empty lists.
  Acceptance: the in-game job terminal in any starbase returns at
  least one available mission for a fresh character at level-
  appropriate range.
  **STATUS: AWAITING USER APPROVAL + REFERENCE DATA.**

## Tracking notes

- Each Y-task above is one DB-seed import, NOT a code change. The
  Postgres schema in `db/postgres/` should already accommodate the
  rows; if it does not, that is an in-scope schema gap to add to the
  corresponding Y-task.
- After each Y-task lands, re-run `DockHandshakeFriendship7Tests`
  and any other Phase K byte-diff regression tests to see how much
  of the histogram deficit closed; record the before/after numbers
  in the task's "Notes" line.
- Do NOT batch Y-tasks together: each is independently testable
  against a specific byte-diff metric, and batching loses the
  attribution of which import fixed which frame count.
