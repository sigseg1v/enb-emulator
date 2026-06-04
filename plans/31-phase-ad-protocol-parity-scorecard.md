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

- [ ] **AD-66 (keystone): live mining round-trip + 0x000B beam byte-pin.**
  Owned by plan 28 §3-live / task #66. The undock path is fixed, so an
  in-space start is reachable; what is missing is the **proxy-routed in-space
  mining harness** (does not yet exist) to drive + pin the 0x000B beam, plus
  a real-client confirm (CV gate). Groundwork already understood: create a JE
  (race=1/prof=2, `StartSector[5] = 10521` Nishino/Io), `just grant-prospect`
  to seed SKILL_PROSPECT(41)+explore, reach a resource sector (e.g.
  1710/3606/1750 have the most type-38 fields), target an asteroid, sit still,
  send 0x0027 INVENTORY_MOVE sub-18. The server-side 0x2012 START_PROSPECT pin
  is already drivable on the direct harness; the 0x000B beam pin needs the
  proxy-routed harness. This is the highest-leverage next build.
  **AF update (2026-06-04):** the 0x000B wire STRUCTURE is now byte-pinned
  against the live reference (`live_object_effect_000B_weapon.hex`, 35B string
  form, `LiveReferenceCombatTests`) -- so the beam target format is proven; what
  AD-66 still needs is the proxy-routed in-space harness to drive it live + the
  real-client confirm, not the format. (AF also fixed an unrelated combat-band
  Trap-1: 0x008B mob id is big-endian -- server+CLI corrected, plans/29 CV-08.)

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
  (CLAUDE.md "Wire format" Trap 1). The AD-66 harness is the vehicle to confirm
  the output once built.

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
- [ ] **#72: learn ability / skill-up (0x0057 SKILL_UP).**
- [ ] **#73: activate ability/buff + use device-on-mob (0x0058 SKILL_ABILITY,
  host-LE).**
- [ ] **#74: verify gate jump (0x009B WARP / gate-cache).**

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
