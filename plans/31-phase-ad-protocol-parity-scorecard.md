# Phase AD - Protocol parity scorecard (opcode mapping / CLI decode / proxy fidelity)

This is the **single consolidated "are we at parity?" surface**. It exists
because the parity gaps were scattered across plans 26 (Z, emitter
fidelity), 27 (AA, proxy fidelity) and 28 (AB, CLI packet structures), so
"continue" was ambiguous. This file is the index; the per-phase plans hold
the implementation detail. When the answer to "what's left for protocol
parity?" is needed, read THIS first, then the owning phase file.

**Created 2026-06-04** in answer to: "are 100% of opcodes mapped, does the
CLI decode all without error, and is our Net7Proxy 100% parity with the
reference handling?"

The honest one-line answer: **mapping = yes (100% named); CLI decode = no
errors on anything, structured-decode on every inbound game frame we
realistically receive; proxy fidelity = NOT 100%, and its remaining gaps
(tractor/loot fabrication, live mining beam pin) need a captured/real-client
verification of un-citable wire fields -- a normal CV-gate, NOT a blocker on
any client crash.**

---

## 1. The three questions, answered with numbers

Measured 2026-06-04 against `common/include/net7/Opcodes.h` (210
`#define ENB_OPCODE_*`), the CLI catalog
(`OpcodeNames.Generated.cs`, 211 names), the CLI decoders
(`PacketRecordRegistry.cs` + `Opcodes/Inbound|Outbound/*Codec.cs`), and the
proxy (`UDPProxyToClient_linux.cpp` / `ClientToServer_linux_stubs.cpp`).

### Q1 - "are 100% of opcodes mapped?" -> YES (naming/cataloguing)

- 210 header defines; 211 names resolve via `OpcodeNames.All`. Every opcode
  that crosses a process boundary has a symbolic name. **100% catalogued.**
- Caveat: "mapped" in the *behavioural* sense (every opcode has a handler on
  each side) is NOT 100% -- see Q3 and §3 below. Naming != handling.

### Q2 - "does the CLI decode all without error?" -> YES (no throws); structured-decode is near-complete for inbound

- The decode path **never throws on an unknown/unregistered opcode**:
  `OpcodeRegistry.For(op)` falls back to `NamedOpaqueCodec` (named, opaque
  body) and finally `UnknownOpcodeCodec` -- both lossless, neither errors.
  So literally "decodes all without error" = **true**.
- **Structured field-decode** (the meaningful sense): of the 145 client-wire
  (`0x00xx`) opcodes, **107 have a full structured record**
  (`PacketRecordRegistry`) plus 7 dedicated codecs
  (`VersionResponse/GlobalAvatarList/GlobalTicket/ServerRedirect` inbound,
  `VersionRequest/MasterJoin/ClientChat` outbound).
- The 38 client-wire opcodes WITHOUT a structured record are **not a real
  decode gap**, broken down honestly:
  - **~26 are OUTBOUND request opcodes** the CLI *emits* (via payload
    builders), not receives: `GLOBAL_CONNECT 0x6D`, `GLOBAL_CREATE_CHARACTER
    0x72`, `SKILL_UP 0x57`, `GALAXY_MAP_REQUEST 0x98`, `LOGOFF_REQUEST 0xB9`,
    `MANUFACTURE_* 0x7A/0x7B/0x80`, `RECUSTOMIZE_*_DONE 0x82/0x84`,
    `MISSION_FORFEIT 0x86`, `GUILD_*_CLIENT 0xC5/0xC9/0xCD/0xD4`, the various
    `REQUEST_*` etc. The client never decodes its own outbound frames; an
    inbound record for these would be dead code.
  - **6 are server-NEVER-emitted** (the `KnownUnimplementedOpcodes` set:
    `0x001C, 0x0043, 0x0085, 0x0095, 0x00D5, 0x00DD`). Nothing emits them, so
    there is nothing to decode. They live or die with a future server
    feature, not with the CLI.
  - **Residual genuine inbound opaque-only: now essentially empty.**
    `GLOBAL_ERROR 0x75` -- the one real item here -- got a structured
    `GlobalErrorRecord` (AD-1, done). Only a small tail of low-value status
    frames remains opaque, and the opaque decode already shows name + bytes.
- **Conclusion:** the CLI side is effectively done. Do NOT report "38 missing
  decoders" -- that conflates outbound builders and server-unimplemented
  opcodes with a real decode gap. See AD-1 for the only actionable item.

### Q3 - "is our Net7Proxy 100% parity with the reference handling?" -> NO

This is the genuine open gap. Status by band (detail in plan 27):

| Proxy behaviour | Status | Owner |
|---|---|---|
| Single source tree (dead `NET7_LEGACY_WIN32` twins deleted) | [x] done | AA W1 |
| S->C Tier-1: 0x1b undersize-drop, forward-gate 1..0xFE, consume 0x2025..202e | [x] done | AA W1 |
| C->S TURN/TILT throttle (only-while-moving) | [x] done | AA W2 |
| Fabrication 0x2012 -> 0x000B prospect/mining beam (Duration capped 32000) | [x] done | AA W3 |
| Fabrication 0x2018 -> object-create chain (static content) | [x] done | AA W4 |
| Fabrication 0x2019 -> resource-create chain (asteroids) | [x] done | AA W4 |
| MVAS 0x3005 keepalive to udp/3806 (idle-reaper fix) | [x] done | AA W4 |
| **Fabrication 0x2013 -> tractor-ore beam** | **[~] compact INPUT pinned (AF-5); client-facing OUTPUT chain CV-gated** | AA §3a / AB §4 |
| **Fabrication 0x2014 -> loot beam** | **[~] compact INPUT pinned (AF-5); client-facing OUTPUT chain CV-gated** | AA §3a / AB §4 |
| C->S 0x4e starbase-exit handoff | [!] deferred (needs launcher handoff) | AA W1 |
| S->C Tier-2: 0x09/0x0b conditional drop | [~] deferred (forward-unmodified is safe) | AA |
| S->C Tier-2: 0x34 gate-cache timestamp inject + gate-cache subsystem | [~] deferred | AA |

Plus the **server-emitter** divergences in plan 26 (Z-1..Z-8): 8 catalogued,
1 resolved, the rest either blocked on more paired capture frames or
classified by-design / latent. These are server-side, not proxy.

---

## 2. THE KEYSTONE (why most of the above is blocked, and what unblocks it)

The blocked rows all trace to ONE thing: **the un-citable wire fields in the
tractor/loot fabrication band have not yet been pinned against a real capture
+ real-client confirmation.** This is a CV-gate (verify-against-client), NOT a
client crash. The dock->space undock path itself WORKS -- it was fixed
(owner-confirmed 2026-06-04; tasks #64/#65 "CLI follows undock handoff into
space" and "MVAS position feed accepted after undock" are completed). Do NOT
reintroduce any "client crashes on undock / dock->space crash-bisection"
framing; that claim was false.

- Fresh characters spawn **docked at a starbase** (Phase K reverted
  start-to-station, task #48). Reaching space requires the dock->space
  undock transition -- which now succeeds.
- The proxy fabrication (0x2013 tractor / 0x2014 loot) is now **half
  unblocked by Phase AF.** AF-5 byte-pinned the compact server->proxy INPUT
  form (`live_tractor_ore_2013_*.hex`, `live_loot_item_2014_*.hex`): the
  article name, base asset, tractor_time, tractor_speed (350.0), and position
  are now citable from a live capture -- they are no longer "un-citable." What
  remains un-citable is the proxy's **client-facing OUTPUT chain** (0x04 CREATE
  + 0x0b beam + 0x1b name AUX + 0x46 position): those bytes ride the encrypted
  client<->proxy leg, which the captures do NOT cover, so the article Scale,
  wire Type byte, tractor-beam EffectDescID, and 0x46 field order are still
  guesses. CLAUDE.md's server-integrity rules still forbid shipping that
  expansion unverified -- the C# byte-pin proves *input format*, only client.exe
  proves the *fabricated output* renders without crashing (CV-NN gate). So the
  fabrication can now be built against a known input, but its output fields
  must still be confirmed against the real client before it ships.
- The remaining live-pin work (AB §3-live: live mining round-trip + 0x000B
  beam byte-pin, task #66) needs a proxy-routed in-space harness that does
  not yet exist; the undock path being fixed means this is now reachable,
  it just has not been built/run yet.

**Therefore the highest-leverage parity work is (a) obtaining a capture or
first-hand layout for the 0x2013/0x2014 fabrication fields, and (b) building
the proxy-routed in-space mining harness to drive + pin 0x000B live.** Neither
is blocked on a client crash -- the undock path works. Chasing the fabrication
band before its fields are pinned is still forbidden by the integrity rules
(unverified fabrication can break the real client), but the gate is "fields
not yet captured", not "client crashes".

---

## 3. Work items (the "continue" list, in priority order)

- [x] **AD-66 (keystone): live mining round-trip + 0x000B beam byte-pin. DONE
  (2026-06-04).** `SectorMiningTests.Mine_Roid_ServerEmits_StartProspect_0x2012`
  drives the full retail trigger end-to-end and byte-pins the proxy-fabricated
  beam. Resolution of the blockers below: a Progen Explorer (race=2/prof=2) is
  created via the `SectorHandshake` race/prof variant, SKILL_PROSPECT=41 is
  DB-seeded in `net7_user` between a two-stage login, the avatar undocks into
  sector 1030 and is MVAS position-fed onto "Asteroid Field 6" (~(-8919,-9247));
  roids arrive as 0x2019 RESOURCE_OBJECT_CREATE on the raw MVAS leg (new
  `SectorWorld` 0x2019 ingest + 2 unit tests). On the MVAS feed path
  `ObjectIsMoving()` is permanently false (the scalar `m_Velocity` is never
  updated by `SetVelocityVector`), so the "not moving" gate is satisfied for
  free -- the 35k-jump speed-clamp worry was moot. The range-list 0x2012 routes
  through the player's PROXY connection (NOT the repointed MVAS socket), so the
  proxy expands it to the client-facing 0x000B on the TCP leg; the test pins
  that 23-byte beam (Bitmask 0x0007, src=playerGID, tgt=roidGID,
  EffectDescID 0x00BF, Duration capped <=32000) and round-trips it through the
  CLI `ObjectToObjectEffectRecord` decoder. NON-flaky: it tries every in-range
  roid in turn (a depleted ore stack is refused via a 0x0022 PUSH_MESSAGE with
  no server log, now surfaced in the test output). NO server change -- only the
  existing retail trigger driven + the existing emission/fabrication pinned.
  Real-client 0x000B render remains CV-gated -> plans/29 CV-09.
  Owned by plan 28 §3-live / task #66.
  **AG correction (2026-06-04): the prior "the proxy-routed mining harness does
  not yet exist" framing was WRONG and is retracted.** The whole Phase-T
  integration suite is ALREADY proxy-routed: `ServerFixture.SectorPort = 3500`
  is the *proxy's* SECTOR_SERVER_PORT and `SectorHandshake` connects there via
  `EncryptedTcpConnection` (the encrypted client<->proxy leg). So the proxy
  expands 0x2012 START_PROSPECT -> 0x000B beam BEFORE frames reach the harness;
  a mining drive run on this transport would observe the fabricated 0x000B, not
  the compact 0x2012. The transport was never the gap. Confirmed too that the
  server's resource-spawn path is real (`FieldClass.cpp:174` +
  `SectorContentSQL.cpp:321` instantiate `OT_RESOURCE` from
  `sector_objects_harvestable`, rows present in the content DB), and the 0x000B
  wire STRUCTURE is already byte-pinned (`live_object_effect_000B_weapon.hex`,
  35B string form, `LiveReferenceCombatTests`).
  **The genuine remaining blockers** (none is "no harness"):
  (1) **Prospect range vs spawn position (quantified).**
  `CheckMiningConditions` (`PlayerSkills.cpp:884`) requires the avatar within
  `ProspectRange()` of the roid AND stationary (`ObjectIsMoving()==false`).
  `ProspectRange() = 750 + skill_level*250` (`PlayerClass.cpp:3462`) = ~2500
  units at level 7. But the roid fields are FAR from the spawn point: in sector
  1710 the type-38 fields sit at radii ~35,000-200,000 units from origin
  (nearest cluster ~(-33000,-4000,0) and ~(35000,12000,0)); 1710 has no
  gate/nav (type 5/6/7) objects near a field. So the harness must position-feed
  the avatar ~35k units onto a roid and stop cleanly within ~2500 units of it
  -- a deliberate large teleport-hop, not an incremental nudge. Open risk: does
  the MVAS position feed accept a 35k-unit jump without speed-clamping/rejection
  (the #65 feed was small post-undock moves), and does `ObjectIsMoving()` settle
  to false after it. This is the real reason AD-66 stayed open.
  (2) Roid spawn timing/availability, JE-Explorer creation (race=1/prof=2;
  `SectorHandshake` hardcodes Terran Warrior race=0/prof=0, needs a variant)
  and prospect-skill DB seeding (`avatar_skill_levels` skill_id 41 in the
  `net7_user` DB; prof MUST be 2 or it clamps to 0 at login --
  `just grant-prospect`).
  (3) An irreducible real-client CV-gate (only the owner confirms 0x000B
  renders on `client.exe`) -- tracked as plans/29 CV-09.
  Trigger chain once in range+stationary: 0x0017 REQUEST_TARGET the roid gid ->
  0x0027 INVENTORY_MOVE sub-18 (`PlayerConnection.cpp:3210` case 18,
  OT_RESOURCE -> `MineResource`) -> server emits 0x2012 -> proxy fabricates
  0x000B. A green, NON-flaky test of this needs the position-feed range solve;
  it was deliberately NOT shipped flaky under time pressure (would violate the
  green-tests discipline). Next build = the JE-Explorer establish variant + the
  position-feed-to-roid range solve, then pin the fabricated 0x000B.
  (AF also fixed an unrelated combat-band Trap-1: 0x008B mob id is big-endian --
  server+CLI corrected, plans/29 CV-08.)

- [~] **AD-2/AD-3: 0x2013 tractor / 0x2014 loot fabrication.** Owned by
  plan 27 §3a + plan 28 §4. **AF-5 (2026-06-04) byte-pinned the compact
  server->proxy INPUT** (name, base asset, tractor_time, tractor_speed=350.0,
  position) against live captures -- the input fields are no longer un-citable.
  What remains is the proxy's **client-facing OUTPUT chain** (0x04 CREATE +
  0x0b beam + 0x1b name AUX + 0x46 position): those bytes ride the encrypted
  client<->proxy leg the captures do not cover, so the article Scale, wire Type
  byte, tractor-beam EffectDescID, and 0x46 field order are still guesses. The
  fabrication can now be BUILT against a known input, but its output must be
  confirmed against the real client (CV-gate) before it ships. Do NOT ship the
  output expansion speculatively: a wrong 0x04 CREATE crashes the Win32 client
  (CLAUDE.md "Wire format" Trap 1).
  **AG correction (2026-06-04): the AD-66 harness CANNOT confirm this output.**
  AD-66 is now DONE, but the harness observes only what OUR proxy emits, and our
  proxy is the very thing being fixed -- it currently drop-consumes 0x2013/0x2014
  and emits NOTHING, so there is no reference output on our stack to observe. The
  prospect beam (AD-66) was confirmable because it is a pure 0x0b (structurally
  safe, CV-gate only); the tractor/loot output requires a 0x04 CREATE whose
  article wire Type is UNKNOWN for a looted weapon (0x2014) and a 0x46
  positional-interp whose field order is a guess -- both crash-risky. The output
  bytes ride the ENCRYPTED client<->proxy leg that no cleartext AF-5 capture
  covers. Genuine unblock: a DECRYPTED client<->proxy capture during a real
  tractor/loot, OR owner real-client iteration. Until then #49 stays blocked --
  this is the safely-implementable ceiling of the parity program.
  **Owner intent (2026-06-07) for #49:** the user-visible goal is that mined
  ore/loot **tractors IN to the ship** (the article spawns in-world and is
  beam-pulled to the player) instead of teleporting straight into inventory.
  That IS the 0x2013/0x2014 OUTPUT-chain fabrication above -- the 0x04 CREATE +
  0x46 positional-interp that animates the pull-in. So "fix the ore not pulling
  in" and "ship the tractor/loot fabrication" are the same task; it remains
  CV-gated on the encrypted-leg capture / real-client iteration, not a separate
  fix.

- [ ] **AD-7 (NEW, 2026-06-07, do NOT fix yet -- tackle when AD-2/AD-3 / #49
  is worked): post-prospect movement desync.** Owner report: after you prospect
  something, the player's position appears **locked server-side** (likely in the
  MVAS / sector position state) -- the client can still move locally, but the
  server no longer accepts/echoes the new position, so the two desync. It stays
  desynced until the next **warp**, at which point the client **rubberbands**
  back to the server's stale position. Hypothesis to investigate when we open
  this area: the prospect/mine action puts the player into a server-side
  "locked"/"busy" or stationary-beam state (the real client holds you still
  while the beam runs) that is never cleared on our server after the beam ends,
  so subsequent MVAS position updates are rejected/ignored until a warp forces a
  full position re-sync. Check the server's prospect/mine handler
  (`PlayerSkills.cpp` ActivateProspectBeam path + whatever sets a player
  movement/lock flag) for an un-cleared lock, and the MVAS receiver for dropped
  position updates while that flag is set. Needs the live capture / real-client
  loop from #49 to confirm the exact state transition. Not blocking; bundle the
  fix with the tractor/loot work.

- [x] **AD-1 (DONE): structured-decode `GLOBAL_ERROR 0x75`.** `GlobalErrorRecord`
  decodes the `[u32 Length LE][u32 Code BE = ntohl(index+7)][Length msg bytes,
  Latin1, not NUL-terminated]` layout -- citable from two agreeing in-repo
  emitters (login-server/Net7SSL/ClientToGlobalServer.cpp:46-55 +
  proxy/ClientToServer_linux_stubs.cpp:255-264). Registered at 0x0075;
  `GlobalErrorRecordTests` byte-pins three error indices (full coverage, Code
  decodes back to the index) + the truncated-header and length-overrun flags.
  No live capture exists (error-only reply on the global leg), so the test
  synthesises the emitter's exact bytes -- same pattern as `CompactMineRecordTests`.

- [ ] **AD-4 (deferred, optional): proxy S->C Tier-2** (0x09/0x0b
  conditional drop, 0x34 gate-cache timestamp inject). Forward-unmodified is
  the *safe* direction and these have not been shown to break the client, so
  they stay deferred until a capture proves a divergence that matters. Do not
  implement on spec.

- [ ] **AD-5 (deferred): C->S 0x4e starbase-exit handoff.** Needs the
  launcher handoff path; deferred in AA W1.

### CLI C->S gameplay-op codecs (tooling-only, no server/proxy change)

The live-capture-driven program to make every gameplay op the owner listed
(sell/buy/move inventory/vault/equip/ammo/stack/loot/jettison/use/learn/
cast/gate) understood + byte-pinned + driven through the proxy by the CLI.
These are tooling-only (the server already handles the opcodes), so they
carry no server-integrity weight beyond the byte-pin itself.

- [x] **#68 (DONE): 0x0027 INVENTORY_MOVE codec + `inv` command.** Pinned to
  VendorInvEco dg#12/20/25 (buy/sell/rearrange). `InventoryMoveCodec`, all
  fields big-endian (server ntohl's each).
- [x] **#69 (DONE): 0x0027 stack split/combine + loot/jettison.** `Loot=6`
  enum; jettison pinned to ProspectRun dg#35, loot to KillLoot2 dg#65;
  split/combine ride the Num field (CheckStack), pinned by construction.
- [x] **#70 (DONE): equip/unequip weapon + load/unload ammo (0x0027) +
  activate equipped item (0x005D EQUIP_USE).** Equip moves pinned to
  VendorInvEco dg#31 (equip cargo[30]->equip[3]), dg#34 (load ammo Num=126),
  dg#36 (unload ammo), dg#41 (unequip). New `EquipUseCodec` (0x005D, GameID
  LITTLE-endian -- handler casts struct without ntohl) pinned to KillLoot2
  dg#3 (fire weapon slot 3) + SkillTrainingHostileDevice2 dg#44 (device slot
  11); new `use <slot>` REPL command; `SectorEquipUseTests` gains a
  codec-built live round-trip. EquipUse byte order is the trap: 0x0027 is
  big-endian on every field, 0x005D's GameID is little-endian.
- [x] **#71 (DONE): vendor terminal / NPC-talk + station-exit (0x004E
  STARBASE_REQUEST).** The buy/sell *move* is already 0x0027 (#68); this is the
  terminal-open handshake. New `StarbaseRequestCodec` (0x004E, 9B: int32
  PlayerID + int32 StarbaseID + char Action, BOTH ints little-endian -- handler
  HandleStarbaseRequest at PlayerConnection.cpp:9861 casts the struct with no
  ntohl) + `starbase <talk|exit|job|jobdesc> [id]` REPL command. Byte-pinned in
  StarbaseRequestCodecTests against VendorInvEco dg#4 `2A 99 03 00 C4 01 00 00
  04` = PlayerID 0x0003992A LE, StarbaseID 452 LE, Action 4 (talk-to-NPC).
  Note PlayerID is the MASKED avatar id (session GameID 0x4003992A -> wire
  0x0003992A, top type byte cleared); command derives it as `GameId &
  0x00FFFFFF`. Codec-built talk-to-NPC live round-trip added to the existing
  SectorStarbaseRequestTests (both facts pass, 16s).
- [x] **#72 (DONE): skill-up / train skill (0x0057 SKILL_UP / SkillAction).**
  New `SkillUpCodec` (0x0057) + `skillup <skillId>` REPL command. Wire-shape
  correction: the canonical struct is 10B packed (`int32 GameID; int
  SkillPoints; short SkillID`) but the retail client serializes SkillID as a
  4-byte int, so the real frame is 12B; the codec emits the faithful 12B shape
  and the server's `short SkillID` read of the low 2 bytes still yields the
  right value on LE. All fields little-endian (HandleSkillAction at
  PlayerSkills.cpp:97 casts with no ntohl); SkillPoints is server-IGNORED (it
  recomputes from RPGInfo). Byte-pinned in SkillUpCodecTests against
  SkillTrainingHostileDevice2 dg#18 `2A 99 03 40 01 00 00 00 37 00 00 00` =
  GameID 0x4003992A LE, SkillPoints 1 LE, SkillID 55 LE. Codec-built live
  round-trip added to the existing SectorSkillUpTests (both facts pass, 16s).
- [x] **#73 (DONE): activate ability/buff + use device-on-mob (0x0058
  SKILL_ABILITY + 0x0017 REQUEST_TARGET).** The target is NOT in the 0x0058
  packet: HandleSkillAbility (PlayerAbilitys.cpp:23) reads
  `ShipIndex()->GetTargetGameID()`, the lock set by a prior REQUEST_TARGET. So
  device-on-mob is two steps. New `SkillUseCodec` (0x0058, 12B: GameID +
  Action + AbilityIndex, all LE; Action server-IGNORED, only AbilityIndex used)
  and new `RequestTargetCodec` (0x0017, 8B: GameID + TargetID, all LE; outbound
  half of the existing inbound RequestTargetRecord). New `ability <index>
  [target]` REPL command: with a target it sends REQUEST_TARGET first, then the
  ability. Byte-pinned in SkillUseCodecTests (SkillTrainingHostileDevice2 dg#28
  `2A 99 03 40 00 00 00 00 2E 00 00 00` = GameID, Action 0, AbilityIndex 46)
  and RequestTargetCodecTests (dg#35 `2A 99 03 40 ED 87 01 00` = GameID,
  TargetID 0x000187ED). Codec-built two-step live round-trip added to the
  existing SectorSkillAbilityTests (all 3 facts pass, 24s).
- [x] **#74 (DONE): gate jump (0x002C ACTION two-step).** The "0x009B WARP"
  label was imprecise: the client-driven gate sequence is not a WARP opcode, it
  is two 0x002C ACTION frames -- Action=18 (gate button, Target = stargate gid)
  then Action=19 (finish sequence) ~6s apart, after which the server emits
  0x003A SERVER_HANDOFF and the CLI follows the handoff into the destination
  sector. Already implemented: `GateCommand` (`gate <gid>`) +
  `GateCommand.BuildActionFrame` (16B ActionPacket, int32 GameID/Action/Target/
  OptionalVar, all LE; server HandleAction PlayerConnection.cpp:3923 case 18 ->
  GateActivate/StargateDestination, case 19 -> SectorServerHandoff). Byte-pinned
  the two gate frames against the SingleGateJump capture (proxy<->server UDP
  leg): dg#2 `2A 99 03 40 12 00 00 00 0B 87 01 00 00 00 00 00` (Action 18,
  Target 0x0001870B) and dg#63 `...13...` (Action 19, same target) in
  GateCommandTests. End-to-end verified by the existing gate integration tests
  (SectorGateHandoffFollow + SectorServerHandoff + SectorAction, 5 facts pass,
  1m5s). Tooling-only: no server/proxy change, the gate path already existed.

---

## 4. Rules that bind this phase

- The proxy/server changes here are all governed by CLAUDE.md "Server
  integrity rules" and "The proxy is NOT a dumb relay": never loosen the
  server for a tool's convenience; a wire change needs a primary-source
  citation + a CLI byte-pin landing first + a plans/29 CV-NN real-client
  entry. The fabrication band specifically must NOT ship on the C# spec
  alone -- the real client is the only proof the fields are right.
- The CLI item (AD-1) is tooling-only and carries none of that weight.
- Keep server + proxy + CLI in sync on any opcode touched (CLAUDE.md
  three-places rule).

## Status

In progress -- 2026-06-04. Scorecard created. Honest verdict recorded:
mapping complete, CLI decode effectively complete, proxy fidelity NOT 100%
with its real remaining work (tractor/loot fabrication + live mining pin)
gated on a capture/real-client field-pin (CV-gate), NOT on any client crash --
the dock->space undock path is fixed and works (owner-confirmed; tasks
#64/#65 completed). The next build is AD-66 (proxy-routed in-space mining
harness to pin 0x000B live), which then unblocks AD-2/AD-3 fabrication once
their fields are captured. AD-1 (structured-decode GLOBAL_ERROR 0x75) is the
only unblocked CLI side-task. No new code landed in this wave -- this is the
consolidated tracking surface the owner asked for.
