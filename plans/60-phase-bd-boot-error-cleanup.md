# Phase BD -- server boot-log error cleanup (mission data debt)

Owner ask (2026-07-05): the server boots with ~48 `ERROR` lines. Make a plan to
fix them. Per the "Log warnings and errors are not noise" rule, none get waved
off -- each is either fixed or proven inert in writing.

## What the 48 lines actually are (root cause, established 2026-07-05)

Two error classes, BOTH pre-existing content-data debt inherited from the
Net-7/kyp mission seed. **Neither is introduced by anything on `dev` vs `main`**
-- `git diff --name-only main..dev -- db/ server/data/` shows no NPC / mission /
station data changed, and the emitter files (`TalkTreeParser.cpp`,
`PlayerMissions.cpp`) are unchanged on the branch. So they exist on `main` too;
this is not a deploy regression, it is standing debt. No wire byte changes ->
**no CV gate, no plans/29 entry** required. Pure data/tooling fix.

### Class 1 -- "Mutually exclusive types in stage N" (34 of 48 lines)

- **Emitter:** `server/src/TalkTreeParser.cpp:324`. After parsing a mission
  stage, it counts completion nodes whose type is in the mutually-exclusive set
  (`ARRIVE_AT`, `FIGHT_MOB`, `OBTAIN_ITEMS`, `TALK_NPC`,
  `USE_SKILL_ON_MOB_TYPE`, `USE_SKILL_ON_OBJECT`, `TALK_SPACE_NPC`,
  `PROXIMITY_TO_SPACE_NPC`, `NAV_MESSAGE`). If a single stage has more than one,
  it logs the error.
- **Consequence (must confirm, do NOT assume cosmetic):** the parser can only
  drive ONE such completion condition per stage; the extra ones are silently
  ignored. That means the affected stage may be completable by the wrong action,
  or uncompletable. This is a *functional* mission-correctness question, not
  noise -- it must be checked per mission, not muted.
- **Affected missions (12):** `16, 115, 131, 170, 182, 287, 288, 289, 293, 355,
  376, 417` (stage numbers vary; some missions trip several stages). Mission XML
  lives in the content DB `missions.mission_XML` column.

### Class 2 -- "ERROR IN MISSION: NPC [N] doesn't exist" (9 of 48 lines)

- **Emitter:** `server/src/TalkTreeParser.cpp:286`. A `TALK_NPC` completion node
  names an NPC id that `StationManager::GetNPC(id)` can't resolve.
- **Root cause (confirmed):** 7 distinct NPC ids -- `329, 431, 471, 661, 664,
  666, 667` -- are referenced by missions but ABSENT from the `starbase_npcs`
  table (598 rows, id range 7..100501; a quoted `"npc_Id"` lookup of those 7
  returns nothing).
- **Consequence:** each affected mission's TALK_NPC stage points at a
  non-existent NPC -> the player cannot talk to the mission starter/target ->
  **the mission is broken / uncompletable.** This is a real gameplay defect, not
  cosmetic.
- **Affected missions (4):** `47, 53, 203, 417` (417 also appears in Class 1).

## Plan

Data-only fixes plus one optional parser-hygiene improvement. All work is in
`db/postgres/` seeds + tooling; no server code behaviour change is required to
clear the errors (though BD-4 is a code-side nicety).

- [ ] **BD-1 -- Triage the 7 missing NPCs (Class 2).** For each id
  (`329,431,471,661,664,666,667`): decide whether the NPC is genuinely missing
  from the seed (add the `starbase_npcs` row from the authoritative source /
  reconstruct dataset) or the mission references a wrong/obsolete id (fix the
  mission_XML reference). Cross-check against the retail mission intent before
  choosing -- do not invent an NPC to silence a log line. Record the decision
  per id. Missions to read: 47, 53, 203, 417.
- [ ] **BD-2 -- Triage the 12 mutually-exclusive missions (Class 1).** For each of
  `16,115,131,170,182,287,288,289,293,355,376,417`, open the mission_XML, find
  the offending stage(s), and determine the intended single completion
  condition. Two legitimate outcomes: (a) the stage genuinely should have one
  completion type and the extras are data corruption -> remove them; (b) the
  mission was authored expecting multi-condition stages the parser never
  supported -> flag for BD-4. Do NOT blindly delete completion nodes; a wrong
  delete makes a mission trivially/never completable.
- [ ] **BD-3 -- Emit the corrected mission/NPC data as committed seed SQL.**
  Parameterised generation only where a tool touches the DB; the emitted `.sql`
  is a script artifact (literal values, quote-doubled) per the SQL rules. Verify
  a fresh `docker compose` boot logs ZERO Class-1/Class-2 lines. Idempotent
  re-apply.
- [ ] **BD-4 (optional, code-side) -- Decide the parser's contract for
  multi-condition stages.** If BD-2 finds missions that legitimately want more
  than one completion condition in a stage, that is a `TalkTreeParser` /
  `PlayerMissions` capability gap, not just bad data -- scope it separately
  (server change, would then need the normal server-change discipline). If BD-2
  finds all cases are data corruption, close BD-4 as "not needed" and keep the
  warning as a genuine data-integrity guard.
- [ ] **BD-5 -- Sweep for any OTHER boot ERROR/WARNING classes** not in these two
  buckets (re-run the `docker logs | grep -iE 'error|fatal|fail|unable'`
  categorisation on a clean boot) and either fix or prove-inert-in-writing each,
  per the log-noise rule. Current clean-boot count to drive to zero: the 48
  above are fully accounted for by Class 1 + Class 2 + the mission self-test
  harness "Mission NNN: Check a completion" INFO lines (those are the retail
  beta-mission self-test diagnostic, not errors -- if they read as ERROR/`report
  to a developer`, demote them to debug so the log stops crying wolf).

## Non-goals / notes

- This does NOT touch the formation-gate change or any wire format. Deploy
  safety of `dev` was established separately (2026-07-05): builds clean, boots,
  formation-gate emits no new wire. These boot errors are orthogonal standing
  debt.
- The mission self-test "Check a completion ... report it to a developer" lines
  are a built-in QA harness, not failures. BD-5 decides whether they should log
  at ERROR level at all on a normal boot.
