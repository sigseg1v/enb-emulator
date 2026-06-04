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
are correctly BLOCKED behind one keystone (the Phase K dock->space
in-space session).**

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
  - **Residual genuine inbound opaque-only: essentially `GLOBAL_ERROR 0x75`**
    (and a small tail of low-value status frames). This is the only real,
    safe, unblocked CLI structured-decode work, and it is marginal.
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
| **Fabrication 0x2013 -> tractor-ore beam** | **[!] BLOCKED** | AA §3a / AB §4 |
| **Fabrication 0x2014 -> loot beam** | **[!] BLOCKED** | AA §3a / AB §4 |
| C->S 0x4e starbase-exit handoff | [!] deferred (needs launcher handoff) | AA W1 |
| S->C Tier-2: 0x09/0x0b conditional drop | [~] deferred (forward-unmodified is safe) | AA |
| S->C Tier-2: 0x34 gate-cache timestamp inject + gate-cache subsystem | [~] deferred | AA |

Plus the **server-emitter** divergences in plan 26 (Z-1..Z-8): 8 catalogued,
1 resolved, the rest either blocked on more paired capture frames or
classified by-design / latent. These are server-side, not proxy.

---

## 2. THE KEYSTONE (why most of the above is blocked, and what unblocks it)

The blocked rows all trace to ONE thing: **there is no live in-space client
session to verify against.**

- Fresh characters spawn **docked at a starbase** (Phase K reverted
  start-to-station, task #48). Reaching space requires the dock->space
  undock transition.
- That transition is under **Phase K crash-bisection** (task #38, plan 11):
  the real Win32 client currently crashes somewhere on the dock->space path,
  so we cannot drive a clean in-space session end to end.
- The blocked proxy fabrication (0x2013 tractor / 0x2014 loot) has
  **un-citable wire fields that crash the real client if guessed wrong**
  (plan 27 §3a, plan 28 §4). CLAUDE.md's server-integrity rules forbid
  shipping a fabrication we cannot verify against the real client -- the C#
  byte-pin proves *format*, only client.exe proves *the game still works*
  (CV-NN gate). So we CANNOT just implement them speculatively.
- Same keystone blocks AB §3-live (live mining round-trip + 0x000B beam
  byte-pin, task #66) and the Phase S enumerate-* items.

**Therefore the highest-leverage parity work is finishing the Phase K
dock->space crash-bisection.** Everything downstream (tractor/loot
fabrication, live mining pin, enumerate-*) unblocks the moment a clean
in-space session exists. Chasing the fabrication band before then is
forbidden by the integrity rules and would risk crashing the real client.

---

## 3. Work items (the "continue" list, in priority order)

- [ ] **AD-K (keystone): Phase K dock->space crash-bisection.** Owned by
  plan 11 / task #38. This is the gate for everything below. NOT a quick
  add-on -- it is an active crash-bisection. Continue there.

- [ ] **AD-66: live mining round-trip + 0x000B beam byte-pin.** Owned by
  plan 28 §3-live / task #66. Blocked on AD-K (needs in-space start).
  Groundwork already understood: create a JE (race=1/prof=2, `StartSector[5]
  = 10521` Nishino/Io), `just grant-prospect` to seed SKILL_PROSPECT(41)+
  explore, reach a resource sector (e.g. 1710/3606/1750 have the most
  type-38 fields), target an asteroid, sit still, send 0x0027 INVENTORY_MOVE
  sub-18. The server-side 0x2012 START_PROSPECT pin is drivable on the direct
  harness; the 0x000B beam pin needs a proxy-routed harness (does not yet
  exist) + real-client confirm (CV gate).

- [ ] **AD-2/AD-3: 0x2013 tractor / 0x2014 loot fabrication.** Owned by
  plan 27 §3a + plan 28 §4. Blocked on AD-66 (live verification path) which
  is blocked on AD-K. Do NOT start speculatively.

- [ ] **AD-1 (UNBLOCKED, low value): structured-decode `GLOBAL_ERROR 0x75`**
  and any small inbound status tail currently opaque-only. Pure CLI tooling,
  zero server risk. The only parity item that needs nothing from the
  keystone. Marginal value -- the opaque decode already shows the named
  frame + bytes -- but it is the one thing that can move now.

- [ ] **AD-4 (deferred, optional): proxy S->C Tier-2** (0x09/0x0b
  conditional drop, 0x34 gate-cache timestamp inject). Forward-unmodified is
  the *safe* direction and these have not been shown to break the client, so
  they stay deferred until a capture proves a divergence that matters. Do not
  implement on spec.

- [ ] **AD-5 (deferred): C->S 0x4e starbase-exit handoff.** Needs the
  launcher handoff path; deferred in AA W1.

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
correctly blocked behind the Phase K dock->space keystone. No new code
landed in this wave -- this is the consolidated tracking surface the owner
asked for so "continue" resolves unambiguously to AD-K (Phase K bisection),
with AD-1 as the only unblocked side-task.
