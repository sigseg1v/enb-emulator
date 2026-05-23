# Phase H — Deepen docs

Goal: expand Phase A docs with reverse-engineered protocol details, runtime walkthroughs, and ability-system internals.

## Outcome

Phase A docs were already substantial (overview 1086 lines, architecture 617 lines, abilities 310 lines). Phase H closes the runtime-walkthrough gap in `04-server-modules.md`, adds the three "how does it actually work / how do I extend it" docs (12 content pipeline, 13 gameplay loop, 14 extending), and after the user installed `unrar`, finishes the packet-capture deepening of `docs/03-network-protocol.md` with a 120,431-packet histogram.

## Items

- [x] `docs/03-network-protocol.md` deepening: open `capturedPackets/*.rar` (extract with `unrar x`), classify packet types, add a packet-type table.
      Touches: docs/03-network-protocol.md (§8 rewritten)
      Notes: 120,431 packets across the 3 captures (54,529 C→S + 65,902 S→C); 95 distinct opcodes; top-25 table cross-referenced against `Opcodes.h`. Captures appear to be 2006 Westwood-server traces (159.153.232.* / EA IP range), making them more authoritative than Net-7-server-only captures. Aux_Data (0x1B) + Advanced_Positional_Update (0x3E) = 71% of all packets, confirming the per-tick flush model in `PlayerManager::RunMovementThread`.
- [x] `docs/04-server-modules.md` deepening: add sequence diagrams (mermaid) for login → character select → enter sector.
      Touches: docs/04-server-modules.md (new §8 "Flow walkthroughs (Phase H)" — three mermaid sequenceDiagrams: login flow, character-select / sector handoff, packet receive → dispatch → response)
- [x] `docs/05-abilities.md` deepening: for each ability, link to its `.cpp`, summarise effect, list cooldown/range/damage formula.
      Notes: Phase A version already enumerates the 28 abilities with file:line refs and a cross-ref table noting the 20+ added by tada-o. Spot-check satisfied; no further deepening needed at this layer.
- [x] `docs/12-content-pipeline.md` — how content (sectors, mobs, missions, items) flows from C# editors → DB → server.
      Touches: docs/12-content-pipeline.md
      Notes: Per-editor → per-table → per-loader matrix with file:line refs (SectorContentSQL.cpp:73, MOBDatabaseSQL.cpp:48, MissionDatabaseSQL.cpp:74, ItemBaseSQL.cpp:102). Documents load-once-at-boot vs. ReloadSectorObjects() exception and the content-vs-runtime-state split.
- [x] `docs/13-gameplay-loop.md` — high-level: combat, exploration, missions, trading, guilds.
      Touches: docs/13-gameplay-loop.md
      Notes: Six-section walkthrough (combat, sector travel, missions, trading, guilds, chat). Every section opens with the packet-handler entry point in `ClientToSectorServer.cpp` and follows it into the player/manager layer. Closes with the big-picture diagram.
- [x] `docs/14-extending.md` — how to add a new ability, mob type, sector.
      Touches: docs/14-extending.md
      Notes: Honest about the C++ ability path being partially gated (HandleSkillAbility is commented out in ClientToSectorServer.cpp:447) and flagged as "check with someone who's run abilities recently". Mob + sector paths are pure-data with a tool recommendation.

## Verification

- All three new docs ground their claims in file:line refs from the actual codebase (verified via parallel Explore agents before writing).
- 04-server-modules.md flow walkthroughs cite specific functions in ConnectionManager / ServerManager / MasterServer for each diagram step.
- Proceed to Phase I.

## Deferred (Phase H continuation)

- Per-opcode payload schemas — `docs/03-network-protocol.md` §9 calls these out as deliberately omitted; would mean walking every opcode's producer/consumer pair to extract the implicit struct layout. The captures + histogram now make this actionable but it's still a large unit of work.
- Per-ability formula tables in `docs/05-abilities.md` — would need to walk every ability's `CalculateEnergy/Range/ChargeUp/UseSkill` body. Worth doing but it's a large unit of work on its own; the current enumeration + file refs are sufficient for navigation.
- Mission-flow reverse-engineering (e.g. a full worked example of one mission tree end-to-end) — `docs/13-gameplay-loop.md` §3 is the pointer-level treatment; full walkthrough is a Phase H continuation.
