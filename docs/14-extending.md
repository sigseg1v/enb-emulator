# Extending the server

How to add a new ability, mob, or sector. Reflects the actual
code shape — if something is unclear from the source, this doc
says so rather than guessing.

## Add a new ability

Abilities are real C++ classes (not data-driven). Walk the
existing pattern — `AbilityEnvironmentShield` is a good
example.

### Files to create

```
server/src/Abilities/AbilityFoo.h
server/src/Abilities/AbilityFoo.cpp
```

Inherit from `AbilityBase` (AbilityBase.h:68-175). Override
the virtuals the engine calls:

| Method | Purpose | AbilityBase.h |
|---|---|---|
| `CalculateEnergy()` | Energy cost for the activation | line 77 |
| `CalculateChargeUpTime()` | Cast / wind-up duration | line 78 |
| `CalculateRange()` | Effective range | line 80 |
| `DetermineSkillRank()` | Map ability ID → rank | line 82 |
| `CanUse()` | Pre-activation gate | line 87 |
| `UseSkill()` | The actual effect | line 88 |
| `Update()` | Per-tick (channels, DoTs) | line 89 |
| `SkillInterruptable()` (opt) | Whether damage cancels | line 97 |

Pass the stat-skill constant to the `AbilityBase` constructor,
e.g. AbilityEnvironmentShield.h:26:

```cpp
AbilityEnvironmentShield(Player *me) : AbilityBase(me, STAT_SKILL_ENVIRONMENT_SHIELD) {}
```

### Registration

1. **Include** in the engine aggregator — add
   `#include "Abilities/AbilityFoo.h"` to
   `server/src/Abilities.h` (line 40 shows the existing
   pattern).
2. **Ability ID** — add a `#define FOO_ABILITY <n>` to
   `server/src/PlayerSkills.h` (lines 44-275 are the existing
   enum-as-defines).
3. **Stat skill name** — add `STAT_SKILL_FOO` to the same
   file (line 109 shows `STAT_SKILL_ENVIRONMENT_SHIELD`).
4. **Dispatcher wiring** — *unclear from code.* The
   client-side skill dispatcher
   (`Connection::HandleSkillAbility`) is commented out in
   `ClientToSectorServer.cpp` (around line 447), so adding an
   ability won't fire end-to-end until that path is restored.
   Check with someone who's run abilities recently before
   shipping.

### DB row?

Most abilities have a row in the skill/ability lookup tables
(`db/mysql/net7.sql`). Check if your ability needs entries in
`ability_base` and `skill_base` — diff against an existing
ability's rows. Use AbilityEditor (if present) rather than
hand-writing SQL.

## Add a new mob

Mobs are **pure data** — no C++ changes needed for a new mob
type, only DB rows.

### DB rows

| Table | Why | Schema ref |
|---|---|---|
| `mob_type` | Register the type ID → name | db/mysql/net7.sql:668-672 |
| `mob_base` | Definition: level, faction, AI script, base asset, `skill0..skill9` slots | db/mysql/net7.sql:603-635 |
| `mob_items` | Equipment loadout | db/mysql/net7.sql:641-655 |
| `mob_spawn_group` | Group of mobs that spawn together | db/mysql/net7.sql:656-667 |
| `sector_objects` | Place the mob in the world | db/mysql/net7.sql:709-737 |
| `sector_objects_mob` | Link the placement to the `mob_base` row | db/mysql/net7.sql:778-791 |

### Tool

Use **MobEditor** (`tools/mob-editor/`) — fills out the rows
above through a UI. The `mob_base.ai` column holds an AI
script reference; `skill0..skill9` reference `PlayerSkills.h`
ability IDs.

### Server side

Nothing. `MOBDatabaseSQL.cpp:48` `LoadMOBContent()` picks up
the new rows on next server start. For an already-running
server, `ReloadSectorObjects()` (ServerManager.cpp:488)
reloads MOBs and re-parses sector content for one sector
(`/reloadsector` GM command).

## Add a new sector

Also data-driven. Two layers: define the sector, populate it.

### Define the sector

| Table | Fields that matter | Schema ref |
|---|---|---|
| `sectors` | name, `system_id`, grid coords, bounds (`x/y/z_min/max`), fog distances, `backdrop_asset` | db/mysql/net7.sql:850-895 |

### Populate the sector

| Table | Why | Schema ref |
|---|---|---|
| `sector_objects` | Every object in the sector (mobs, gates, starbases, asteroids, debris) | db/mysql/net7.sql:709-737 |
| `sector_objects_stargates` | Per-stargate metadata: faction, min security level, target | db/mysql/net7.sql:825-833 |
| `sector_objects_mob` | Link MOB placements to `mob_base` rows | db/mysql/net7.sql:778-791 |
| `sector_objects_starbases` | Starbase placements (via station-tools) | — |

### Tool

**SectorEditor** (`tools/sector-editor/`) does the visual
layout and writes all of the above. Hand-editing the SQL is
possible but tedious — coords matter and the editor at least
gives you a visual sanity check.

### Server side

Nothing. `SectorContentParser::ParseSectorContent()` at
`server/src/SectorContentSQL.cpp:73` loads every row on
startup. Per-sector reload at runtime via the GM
`/reloadsector <id>` command.

### Don't forget

- A new sector with no warp into it is unreachable. Either
  add a `sector_objects_stargates` row in some existing
  sector pointing to it, or position it on an existing
  warp-nav chain.
- Backdrop assets must exist in the client. Server-side you
  only ever store the asset ID; if the client doesn't ship
  that backdrop, players see a blank sky.

## Other extensions

- **New C# tool** — see `docs/07-tools-toolchain.md` and
  `tools/README.md`. Drop into `tools/<kebab-name>/` with an
  SDK-style csproj.
- **New POSIX shim** — `server/compat/`. New code should not
  reintroduce Win32 APIs (CLAUDE.md coding rules).
- **New documentation page** — `docs/<NN-topic>.md`, then
  link from `docs/README.md`.
- **New test** — `tests/<area>/`, then add an
  `add_executable` + `gtest_discover_tests` block in
  `tests/CMakeLists.txt`. See `tests/README.md` for the
  pattern.
