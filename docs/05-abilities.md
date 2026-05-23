# Abilities

This document covers the player/MOB ability system: how abilities are
declared, instantiated, dispatched, and cooled down, followed by a
table of every ability class shipped in `server/src/Abilities/`.

All file:line references are against the tada-o fork checkpoint
imported into `server/src/`. The `--binary-files=text` flag is needed
when grepping the source tree (ISO-8859 + CRLF).

## Concepts

### Skill vs Ability

The codebase distinguishes:

- **Skill** — a trainable proficiency on the character sheet. There
  are 58 of these, defined as `SKILL_*` constants in
  `server/src/PlayerSkills.h:86-143` (`SKILL_AFTERBURN=0` through
  `SKILL_TERRAN_CULTURE=57`). A skill's "rank" determines which ranks
  of the related abilities the player can use.
- **Ability** — a single button on the player's hotbar with its own
  activation cost, charge time, cooldown, range, and effect. There are
  138 of these (`MAX_ABILITY_IDS=138` —
  `server/src/PlayerSkills.h:275`). Each ability is mapped to a
  `#define` constant in the same header (`CLOAK=28`, `PATCH_HULL=77`,
  `JUMPSTART=65`, etc.).

A single skill can drive multiple abilities. For example,
`SKILL_CLOAK` unlocks five abilities (CLOAK, ADVANCED_CLOAK,
COMBAT_CLOAK, GROUP_STEALTH, GROUP_CLOAK) representing successively
higher ranks of the cloak power. All five route to a single C++ class
(`ACloak`) which branches on the requested ability ID at execute time.

### AbilityBase

The base class lives at `server/src/Abilities/AbilityBase.h:68`. Every
concrete ability derives from it. The relevant surface:

```
class AbilityBase {
public:
    AbilityBase(CMob *me, long SkillID = -1);
    virtual ~AbilityBase();

    virtual bool CanUse(long TargetID, long AbilityID, long SkillID);
    virtual bool Use(long TargetID, long AbilityID, long SkillID,
                     long activation_ID);
    virtual bool Update(long activation_ID);
    virtual void Confirmation(bool Confirm, long AbilityID, long GameID);
    virtual bool Execute(long activation_ID);
    virtual bool UseSkill(long ChargeTime);
    virtual void Init(CMob *me);

    CMob *m_AOEEnemyList[6];
    CMob *m_AOEFriendList[6];
    // ...
};
```

The lifecycle is:

1. **Use()** is called when the client clicks the ability hotbar
   button. It does range/cost/state checks via `CanUse()`, deducts
   energy, posts a charge-up effect, and schedules `Execute()` to fire
   after the ability's charge time.
2. **Update()** is called every server tick while the ability is in
   flight (charging or sustained). It returns `false` once the
   ability has completed or been cancelled.
3. **Execute()** is the post-charge-up callback that applies the
   ability's effect (damage, buff, debuff, teleport, etc.).
4. **Confirmation()** is the callback for abilities that require the
   client to accept a popup (wormhole entry, summon accept, etc.).

The two AOE rosters (`m_AOEEnemyList`, `m_AOEFriendList`) cap an
area-effect ability at six targets per side. This is a hard limit
baked into the array sizes, not a tunable.

### Per-MOB ability table

Every `CMob` instance owns an array of pointers:

```
AbilityBase *m_AbilityList[MAX_ABILITY_IDS];  // 138 entries
```

declared in `server/src/CMobClass.h`. The array is populated by
`CMob::SetupAbilities()` at `server/src/CMobClass.cpp:885`. The
mapping strategy, quoted from that function's comment
(`CMobClass.cpp:889-900`):

> This may not seem intuative, but what it does is link all SkillRanks
> into the array so that they all point to a single copy of the class
> that handles them. The reason for doing this is so that we can have
> the access time of an array for calling a skill class without having
> to search through each class to determine if the class contains the
> SkillRank we need.

So lookup is O(1) by ability ID, and the destructor at
`CMobClass.cpp:855-883` is careful to `delete` only the rank-1 slot
of each ability family to avoid double-frees.

### CombatTrance — the one outlier

`m_CombatTrance` (`server/src/CMobClass.h`) is a separate pointer,
not stored in `m_AbilityList`. It is instantiated unconditionally at
`CMobClass.cpp:1091` and runs as a passive buff, not a clickable
ability — there is no ability ID assigned to it, so it doesn't fit
the array model.

### Ability ID ranges

Looking at the constants in
`server/src/PlayerSkills.h:146-269`, abilities are roughly grouped
alphabetically but the numbering is not contiguous — there are gaps
(e.g. 83-88 are vacant). New ranks added by tada-o (e.g.
`REACTOR_BOOST=82` through `REACTOR_OPTIMISATION=86`) plug into those
gaps at the end of the header (lines 270-274), out of alphabetical
order, because the surrounding code already shipped without them.

### EffectIDs

Several abilities have an `EffectID:` annotation in the
`SetupAbilities()` comments (e.g. `SHIELD_SAP` is EffectID 146 per
`CMobClass.cpp:939`). Effect IDs are visual/audio cues defined in
`effect.ini` (referenced in the comment at `CMobClass.cpp:996`); the
data file itself is loaded by `EffectDatabase` on startup. Effect IDs
are not the same as ability IDs.

### DamageShields and reactive abilities

Some abilities (e.g. `ARepulsorField`) install a `DamageShield`
struct entry into the owning MOB's reactive-effect list rather than
firing a one-shot effect. The struct is declared in
`server/src/CMobClass.h` and processed in the combat path
(`PlayerCombat.cpp`).

## Ability dispatch flow

```
Client clicks hotbar button
    │
    ▼
UDP packet OPCODE_USE_ABILITY arrives at sector server
    │
    ▼
CMob::Use(target, ability_id, skill_id, activation_id)
    │
    ▼
m_AbilityList[ability_id]->Use(...)
    │
    ├─→ CanUse(): range, energy, state, hostility checks
    │
    ├─→ Deduct energy, set state to CHARGING
    │
    ├─→ Broadcast charge-up effect to nearby clients
    │
    └─→ Schedule Execute() after ChargeTime ms
        │
        ▼
    Execute(activation_id)
        │
        ├─→ Apply damage / heal / buff
        │
        ├─→ Broadcast effect packet
        │
        └─→ Set cooldown timer
```

The activation_id is a per-cast unique counter so that cancellations
(player gets stunned mid-charge, target dies, etc.) can address the
right in-flight cast.

## Origin: what tada-o added

The original Net-7 codebase shipped only a handful of fully
implemented abilities. The tada-o fork (svn r2974, 2010-03-15) added
implementations for most combat and utility abilities. Looking at
git/svn blame would be the authoritative way to mark these, but
neither is preserved at file granularity in the merged tree. As a
proxy: the abilities flagged "tada-o new" below are the ones called
out in the project README under "What it brought" — namely the
following 22:

Befriend, BioRepression, CombatTrance, EnergyLeech, Enrage,
EnvironmentShield, FoldSpace, GravityLink, Hacking, Menace, PowerDown,
PsionicShield, Rally, ReactorOpt, RepairEquipment, RepulsorField,
SelfDestruct, ShieldCharging, ShieldInversion, ShieldLeech, Summon,
Afterburn.

The remaining five (Cloak, HullPatch, WormHole, JumpStart,
RechargeShields, ShieldSap — that's six, but ShieldSap straddles the
boundary as it is referenced in older comments and may have existed
in stub form before tada-o) appear to predate tada-o. Treat that
split as best-effort; the only authoritative source would be the
original Net-7 svn history, which is not preserved here.

## Ability classes

Every ability class is in `server/src/Abilities/`. The table below
lists each class with the ability IDs it handles (from
`CMobClass.cpp:907-1089`), a one-line summary, and a tada-o-new flag.

Class names start with `A` (e.g. `ACloak`). Filenames are
`Ability<Name>.{cpp,h}`. Class-line refs are against the `.h` file.

| Class | File | Ability IDs handled | Summary | tada-o new |
|---|---|---|---|---|
| `ACloak` | `Abilities/AbilityCloak.h:23` | CLOAK, ADVANCED_CLOAK, COMBAT_CLOAK, GROUP_STEALTH, GROUP_CLOAK | Hides the player (and group) from MOB targeting until they fire or take a hit. Group ranks extend invisibility to nearby groupmates. | no |
| `AHullPatch` | `Abilities/AbilityHullPatch.h:23` | PATCH_HULL, REPAIR_HULL, COMBAT_HULL_REPAIR, AREA_HULL_REPAIR, IMPROVED_AREA_HULL_REPAIR | Heals hull. Lower ranks self-only and out-of-combat; higher ranks combat-capable and area-of-effect. | no |
| `AWormHole` | `Abilities/AbilityWormHole.h:23` | WORMHOLE_ASTEROID_BELT_BETA, WORMHOLE_CARPENTER, WORMHOLE_ENDRIAGO, WORMHOLE_JUPITER, WORMHOLE_KAILAASA, WORMHOLE_SWOOPING_EAGLE, WORMHOLE_VALKYRIE_TWINS | Opens a wormhole to a specific destination sector. Requires confirmation popup on group members. | no |
| `AJumpStart` | `Abilities/AbilityJumpStart.h:23` | JUMPSTART | Resurrects a dead group member in-place. Has a confirmation callback because the target chooses whether to accept. | no |
| `ARechargeShields` | `Abilities/AbilityRechargeShields.h:23` | REGENERATE_SHIELDS, RECHARGE_SHIELDS, COMBAT_RECHARGE_SHIELDS, AREA_SHIELD_RECHARGE, IMPROVED_AREA_RECHARGE | Restores shields to self or group. The rank-1 class name `ARechargeShields` is what the rank-1 ability `REGENERATE_SHIELDS` maps to (the class is named after the more familiar high-rank name). | no |
| `AShieldSap` | `Abilities/AbilityShieldSap.h:23` | SHIELD_SAP, SHIELD_TRANSFER, GROUP_SAP, SAPPING_SPHERE, GROUP_SAPPING_SPHERE | Drains target's shields and adds them to caster's. EffectID 146. | no/unclear |
| `APowerDown` | `Abilities/AbilityPowerDown.h:26` | POWER_DOWN, ADVANCED_POWER_DOWN, ADVANCED_POWER_DOWN_2/3/4 | Reduces target's weapon damage output for a duration. EffectID 340. | yes |
| `ARepairEquipment` | `Abilities/AbilityRepairEquipment.h:30` | REGENERATE_EQUIPMENT, REPAIR_EQUIPMENT, COMBAT_EQUIPMENT_REPAIR, AREA_EQUIPMENT_REPAIR, IMPROVED_AREA_REPAIR | Restores hit-point/durability on damaged equipment. Carries an internal `equipment_damage` struct tracking per-slot damage during the heal. EffectID 178. | yes |
| `AShieldCharging` | `Abilities/AbilityShieldCharging.h:23` | SUPERCHARGE_SHIELDS, ULTRACHARGE_SHIELDS, SUPERCHARGE_TARGET, ULTRACHARGE_TARGET, MEGACHARGE_SHIELDS | Temporarily increases maximum shield capacity (overcharge). | yes |
| `ABefriend` | `Abilities/AbilityBefriend.h:23` | BEFRIEND, IMPROVED_BEFRIEND, ENTRANCE, SOOTHE, AREA_SOOTHE | Pacifies a hostile MOB so it stops attacking the caster. EffectID 221. | yes |
| `AEnrage` | `Abilities/AbilityEnrage.h:23` | ANGER, CAUSE_AGGRESSION, ENRAGE, ANGER_GROUP, ENRAGE_GROUP | Forces a MOB to switch aggro onto the caster (taunt). The class is `AEnrage` even though rank-1 is named ANGER. EffectID 212. | yes |
| `AGravityLink` | `Abilities/AbilityGravityLink.h:26` | MASS_FIELD, GRAVITY_FIELD, IMMOBILIZATION_FIELD, AREA_MASS_FIELD, AREA_IMMOBILIZATION_FIELD | Slows or immobilises target(s). EffectID 219. | yes |
| `ASelfDestruct` | `Abilities/AbilitySelfDestruct.h:23` | SELF_DESTRUCT_1/2/3/4/5 | Caster blows up and deals area damage. EffectID 206. | yes |
| `AShieldInversion` | `Abilities/AbilityShieldInversion.h:23` | SHIELD_RAM, SHIELD_SPIKE, SHIELD_BURN, SHIELD_FLARE, SHIELD_NOVA | Converts shields into a damage pulse. The rank-1 ability is called SHIELD_RAM but the class is `AShieldInversion`. EffectID 98. | yes |
| `AHacking` | `Abilities/AbilityHacking.h:23` | HACK_SYSTEMS, HACK_WEAPONS, MULTI_HACK, AREA_SYSTEM_HACK, AREA_MULTI_HACK | Disables target's equipment slots (weapons, devices, etc.). The class declares its skill ID as `STAT_SKILL_HULL_PATCH` (`AbilityHacking.h`), which is suspicious — likely a copy-paste from `AHullPatch` that was never fixed. EffectID 193. | yes |
| `ABioRepression` | `Abilities/AbilityBioRepression.h:23` | BIOREPRESS, BIOSUPPRESS, BIOREPRESSION_SPHERE, BIOSUPPRESSION_SPHERE, BIOCESSATION | Debuff against organic enemies (suppresses regeneration / dampens psionic effects). | yes |
| `ARally` | `Abilities/AbilityRally.h:23` | DAMAGE_TACTICS, DEFENSE_TACTICS, FIRING_TACTICS, STEALTH_TACTICS | Group buff that boosts one stat (damage, defence, firing speed, stealth). Note: FIRING_TACTICS maps to rank 5 but there is no rank-6 entry — the comments at `CMobClass.cpp:1010-1013` list only ranks 1/3/5/7. | yes |
| `AEnvironmentShield` | `Abilities/AbilityEnvironmentShield.h:23` | ENVIRONMENTAL_BARRIER, LESSER_ENVIRONMENTAL_SHIELD, ENVIRONMENTAL_SHIELD, GREATER_ENVIRONMENTAL_SHIELD, ULTRA_ENVIRONMENTAL_SHIELD | Buffs resistance to environmental damage (e.g. nebula damage). EffectID 216. | yes |
| `AFoldSpace` | `Abilities/AbilityFoldSpace.h:23` | TELEPORT_SELF, TELEPORT_ENEMY, TELEPORT_FRIEND, DIRECTIONAL_TELEPORT, AREA_TELEPORT | Teleport caster, an ally, or an enemy. DIRECTIONAL_TELEPORT shares rank-5 with TELEPORT_FRIEND in the mapping (`CMobClass.cpp:1031`) — both point to the same class instance, distinguished by ability ID at runtime. EffectID 202. | yes |
| `AShieldLeech` | `Abilities/AbilityShieldLeech.h:23` | SHIELD_DRAIN, SHIELD_LEECH, GROUP_LEECH, SHIELD_LEECHING_SPHERE, GROUP_LEECHING_SPHERE | Drains target's shields without transferring them to caster (compare to ShieldSap which does transfer). | yes |
| `ARepulsorField` | `Abilities/AbilityRepulsorField.h:25` | MINOR_REPULSOR_FIELD, LESSER_REPULSOR_FIELD, REPULSOR_FIELD, GREATER_REPULSOR_FIELD, MAJOR_REPULSOR_FIELD | Installs a reactive damage shield (`DamageShield`) that pushes attackers back when they hit. The header `#define REPULSOR_FIELD_ID 42` is a hardcoded effect/ability marker used elsewhere. | yes |
| `AMenace` | `Abilities/AbilityMenace.h:23` | INTIMIDATE, SCARE, TERRIFY, AREA_INTIMIDATE, AREA_TERRIFY | Fear effect — causes target to flee. EffectIDs: scare 199, intimidate 198. | yes |
| `AEnergyLeech` | `Abilities/AbilityEnergyLeech.h:23` | ENERGY_DRAIN, ENERGY_LEECH, RENDER_ENERGY, ENERGY_LEECHING_SPHERE, RENDER_ENERGY_SPHERE | Drains target's reactor energy. The leech variants likely transfer to caster, the render variants likely just deplete (verify in `.cpp`). | yes |
| `APsionicShield` | `Abilities/AbilityPsionicShield.h:23` | PSIONIC_BARRIER, LESSER_PSIONIC_SHIELD, PSIONIC_SHIELD, GREATER_PSIONIC_SHIELD, PSIONIC_INVULNERABILITY | Buffs resistance to psionic-class damage. EffectID 214. | yes |
| `ASummon` | `Abilities/AbilitySummon.h:23` | SUMMON_ENEMY, SUMMON_FRIEND, SUMMON_GROUP, SUMMON_ENEMY_GROUP, RETURN_FRIEND | Pulls a target (friendly or hostile) to the caster's position. Friendly summons require confirmation. | yes |
| `AAfterburn` | `Abilities/AbilityAfterburn.h:25` | AFTERBURN | Temporary movement-speed boost at energy cost. Single rank only. | yes |
| `AReactorOptimisation` | `Abilities/AbilityReactorOpt.h:23` | REACTOR_BOOST, REACTOR_SURGE, REACTOR_EXTENSION, REACTOR_AUGMENTATION, REACTOR_OPTIMISATION | Increases reactor regeneration rate for a duration. Comment in source calls this "JT's new Reactor ability" (`CMobClass.cpp:1083`). | yes |
| `ACombatTrance` | `Abilities/AbilityCombatTrance.h:24` | (none — passive) | Passive Jenquai-tier-9 buff that grows in strength over a fight. Instantiated as `m_CombatTrance` (separate pointer, not in `m_AbilityList`) at `CMobClass.cpp:1091`. Driven by `Update()` calls, not by player hotbar input. | yes |

Total: 28 ability classes (27 hotbar-driven + 1 passive). They
collectively service all 138 ability IDs except the gaps in the
constant range and the `COMPULSORY_CONTEMPLATION` ability whose
class is commented out at `CMobClass.cpp:1018` with the note "this
skill isn't implemented yet".

## Gaps and known issues found while reading the code

- `AHacking` declares its skill as `STAT_SKILL_HULL_PATCH`
  (`AbilityHacking.h:27`). Almost certainly wrong; should be
  `STAT_SKILL_HACKING`. Filed as a real bug to chase later.
- `COMPULSORY_CONTEMPLATION` (ability ID 33,
  `PlayerSkills.h:179`) has no implementation. `SetupAbilities()`
  has its `new ACompulsoryContemplation(this)` line commented out
  (`CMobClass.cpp:1017-1018`) with "this skill isn't implemented
  yet". Skill ID 14 (`SKILL_COMPULSORY_CONTEMPLATION`) is therefore
  trainable but does nothing.
- `AAfterburn`'s `delete` line at `CMobClass.cpp:882` has the
  comment "this wasn't being deleted. Memory leak!" indicating
  someone fixed the leak in this fork; useful as a marker for
  what the upstream was missing.
- `ARally` only covers ranks 1/3/5/7 — there is no rank-6 entry.
  Either rank 6 was deliberately omitted from the design or the
  mapping is incomplete; unresolved.
- `AFoldSpace` maps both TELEPORT_FRIEND and DIRECTIONAL_TELEPORT
  to "rank 5" in the comments (`CMobClass.cpp:1030-1031`). Two
  abilities at the same rank tier; the design intent is unclear
  from code alone.
- Ability cooldown/cost/range values are not in the C++ source —
  they live in the `abilities` table in the MySQL schema (and are
  loaded into memory at startup, then looked up by ability ID
  during `CanUse()`). See `docs/06-database-schema.md` for the
  table layout.

## Adding a new ability (cookbook)

To add a 29th ability:

1. Add an ability ID `#define` in
   `server/src/PlayerSkills.h:146-274`, picking an unused slot
   (note: `MAX_ABILITY_IDS=138` may need to bump if you exceed it).
2. If it represents a new trainable skill, add a `SKILL_*` constant
   in the same file under `server/src/PlayerSkills.h:86-143`.
3. Create `server/src/Abilities/AbilityFoo.{h,cpp}`, deriving from
   `AbilityBase`. Implement at minimum `CanUse()`, `UseSkill()`, and
   `Execute()`.
4. Wire it into `CMob::SetupAbilities()` at
   `server/src/CMobClass.cpp:907+` — add a `new AFoo(this)`
   assignment for rank 1, then alias higher-rank ability IDs to the
   same pointer.
5. Add the corresponding `delete m_AbilityList[FOO_RANK_1]` line in
   the destructor at `CMobClass.cpp:856+`. Do not delete the
   aliased higher-rank slots; they point to the same object.
6. Add an `Init()` call in the init block at
   `CMobClass.cpp:792-849` if your ability needs per-MOB
   setup work.
7. Add the ability row to the `abilities` table in
   `db/postgres/schema.sql` (or `db/mysql/net7.sql`) with its
   cost, range, cooldown, charge time, and effect IDs.

## What's NOT in this doc

- Per-ability damage formulas, balance numbers, cooldowns — those
  are data, not code, and live in the database.
- AI ability use (which abilities a MOB uses, when, against whom) —
  driven by `MOB_Behaviour` enum and the AI tick loop in
  `MOBClass.cpp`, covered in `docs/04-server-modules.md`.
- Visual/audio effect details — effect IDs are referenced in the
  ability classes but the effect definitions live in client-side
  `effect.ini` files.
- Permission/access checking beyond skill rank (e.g. faction
  restrictions on certain abilities) — handled higher up in the
  call chain in `PlayerCombat.cpp`.
