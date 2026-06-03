# Phase AB - Port packet-structure knowledge into the C# CLI (and keep all three in sync)

Opcode/packet-structure knowledge now lives in THREE places that must agree
(see CLAUDE.md "Opcode / packet-structure knowledge lives in THREE places"):

1. `server/src/` - the authoritative emitter (what bytes go on the wire).
2. `proxy/` - the translator (strips/re-frames/consumes/rewrites/fabricates).
3. `tools/cli-client/src/CliClient.Core/Opcodes/` - the C# decoder + the
   Phase-T integration suite that byte-pins the wire format.

This phase makes #3 a complete, kept-in-sync mirror of the wire knowledge in
#1 and #2, and -- critically -- turns the CLI into the **byte-pin verification
harness** that the remaining proxy-fabrication work (Phase AA) needs before it
is safe to implement.

**Sourcing.** Grounded in our own in-repo server/proxy source (the
authoritative emitters), cross-checked against clean-room observation of a
known-good Net7Proxy<->client byte stream and the committed retail captures
under `archive/kyp-snapshot/capturedPackets/`. No server security posture is
loosened; the CLI is a passive decoder and a test driver, never a reason to
change server behaviour (CLAUDE.md server-integrity rules bind #1).

## Status

Not started -- 2026-06-03. Created as the verification backbone for the
Phase-AA fabrication band. See §0 for the honest scope correction and §4 for
the deliberate ordering inversion (this harness must precede the high-risk
proxy fabrication, not follow it).

---

## 0. Honest scope correction (READ FIRST)

The literal request was "convert all the knowledge about packet structures to
the C# CLI." Taken at face value that implies a from-scratch port. It is not.

The CLI **already** carries field-level decoders for the overwhelming majority
of the wire format -- this landed incrementally as the Phase-K / Phase-Z
"Tier-2 decode" work. `Opcodes/Records/PacketRecordRegistry.cs` already maps
~95 opcodes to dedicated `PacketRecord` subclasses, including every
client-facing target of the fabrication band and most of the compact control
band:

- Client-facing: `0x0004` CreateRecord, `0x0007` RemoveRecord, `0x000B`
  ObjectToObjectEffectRecord (full bitmask-conditional parser, byte-validated
  vs the live capture), `0x000F` RemoveEffectRecord, `0x001B` AuxDataRecord,
  `0x0046` ComponentPositionalUpdateRecord.
- Compact control band: `0x2014` LootItemRecord (full layout, cites
  `PlayerSkills.cpp:1141-1156`), `0x2018` StaticObjectCreateRecord, `0x2019`
  ResourceObjectCreateRecord, `0x2011` GalaxyMapCacheRecord, `0x2020`
  LoginStageRecord, `0x2025` Opcode2025Record.

So the accurate scope of this phase is **consolidation + gap-fill + harness**,
not "port everything." Concretely:

1. Close the two remaining compact-band decoder gaps (`0x2012`, `0x2013` -- see §2).
2. Make the three-way agreement an enforced invariant, not a hope (§1).
3. Stand up the fabrication-verification integration test (§3) -- the real
   deliverable, because it is what makes the rest of Phase AA safe.
4. Resolve the drift findings already surfaced while reading the three copies (§5).

---

## 1. Three-way consistency invariant (the kept-in-sync rule)

CLAUDE.md now states the rule. This section makes it checkable.

- [ ] Inventory the union of opcodes that (a) the server emits to the
      client/proxy and (b) the proxy forwards or fabrications produce on the
      client leg. For each, assert a `PacketRecordRegistry` entry exists (not
      `GenericRecord`). Emit the misses as a list.
- [ ] For the fabrication band specifically, assert the CLI record's field
      layout matches the proxy fabricator's emitted layout AND the server
      compact emitter's layout. These three are the ones that drift silently.
- [ ] Decide where the check lives: a `dotnet test` ratchet in the Phase-T
      suite (preferred -- runs in CI) modelled on the existing
      `CoverageRatchetTests` / `TestedOpcodes` pattern, NOT a shell script.
      The ratchet asserts equality against a hand-maintained floor so adding a
      decoder is a deliberate, reviewed bump (same discipline as
      `KnownUnimplementedOpcodes`).

## 2. Compact-band decoder gaps (the only real "port" work)

Two compact opcodes are NAMED in `OpcodeNames.Generated.cs` (`0x2012`
START_PROSPECT, `0x2013` TRACTOR_ORE) but fall through to `GenericRecord`.
Add real decoders mirroring `LootItemRecord`'s shape.

- [ ] `StartProspectRecord` (0x2012). Layout from the server emitter
      `Player::MineResource` (`server/src/PlayerSkills.cpp`, compact send at
      :697): int32 playerGID@0, int32 asteroidGID@4, int32 effectUID@8,
      uint32 prospectTick@12, int32 drainMs@16 = 20 bytes. This is the exact
      packet our proxy now expands into the `0x000B` beam (Phase AA Wave 3) --
      so the CLI decoding it closes the loop for byte-pinning that fabrication.
- [ ] `TractorOreRecord` (0x2013). Layout from `Player::UseTractorBeam`
      (`server/src/PlayerSkills.cpp`, compact send at :785). Same structural
      family as `LootItemRecord` (GameID, article UID + effect UID, BaseAsset,
      prospect/loot tick, LS-prefixed Name, tractor time/speed, PosX/Y/Z).
      Confirm field-by-field against the emitter before pinning.
- [ ] Add both to `PacketRecordRegistry` and bump the §1 ratchet floor by two.

## 3. Fabrication-verification harness (the actual deliverable)

A Phase-T integration test that drives the LIVE proxy + server stack and
byte-pins the client-facing bytes the proxy fabricates. This is what makes the
remaining Phase-AA fabrication implementable without crashing the real client.

- [ ] Mining round-trip test: establish a sector session via the existing
      `SectorHandshake` helper, trigger the prospect/mine path, drain the
      client-leg frames, and assert a `0x000B` OBJECT_TO_OBJECT_EFFECT arrives
      whose decoded fields (via `ObjectToObjectEffectRecord`) match the
      fabricated beam: Bitmask 0x0007, GameID=prospector, TargetID=asteroid,
      EffectDescID=0x00BF, EffectID/TimeStamp/Duration present. Pin the bytes.
- [ ] This converts the Phase-AA 0x2012->0x0b fabrication from "compiles and
      looks right" to "regression breaks the build."
- [ ] Mirror the fixture discipline in CLAUDE.md "Wire format" step 4: add a
      capture fixture + `CaptureReplayTests` `[Fact]` if a paired retail frame
      exists for the same effect; cite the capture file + frame in the commit.

## 4. Ordering inversion (brutally honest)

The user's stated sequence was "achieve 1:1 proxy parity, THEN add the CLI
phase." The safe engineering order is the reverse for the high-risk remainder:

The already-shipped 0x2012->0x0b beam (Phase AA) was groundable entirely from
citable in-repo server code, so it shipped first -- fine. But the REMAINING
fabrication (0x2013/0x2014 tractor/loot expansion into 0x04 CREATE + 0x0b beam
+ 0x1b name AUX + 0x46 position; 0x2018/0x2019 object spawn) has fields that
are NOT citable from in-repo server source today -- the floating-ore article
Scale, the wire Type byte mapping, the tractor-beam EffectDescID, and the exact
0x46 positional field order. A wrong `0x04` CREATE crashes the real Win32
client on the next vtable dispatch (the same failure mode as the
ServerRedirect byte-order crash in CLAUDE.md "Wire format" Trap 1).

Therefore: **§3's harness must land BEFORE the remaining Phase-AA fabrication
is implemented.** With the harness, each fabricated field can be pinned against
either a decoded compact source field or a paired retail capture frame, and a
wrong layout fails a test instead of crashing a player's client. Without it we
are guessing at byte layouts that crash on contact.

- [ ] After §1-§3 land, re-open Phase AA's 0x2013/0x2014 fabrication and
      implement each field against the harness (cross-link from plans/27 §3a).

## 5. Drift findings surfaced while reading the three copies

Reading server + proxy + CLI side by side already turned up disagreements the
§1 invariant exists to catch. Resolve each (do NOT silently "fix" toward the
CLI -- the server emitter is authoritative per CLAUDE.md):

- [ ] **Dual 0x000B emitter.** `ObjectToObjectEffectRecord`'s doc cites
      `Object::SendObjectToObjectEffect` (`server/src/ObjectClass.cpp:870-925`)
      as authoritative; the Phase-AA proxy beam was grounded in
      `Player::SendObjectToObjectEffect` (`server/src/PlayerConnection.cpp:1394`)
      + `ActivateProspectBeam`. Confirm the two server emitters produce the
      same wire layout (bitmask order + conditional fields). If they diverge,
      document which the retail capture matches; do not assume.
- [ ] **Stale struct comment.** `common/include/net7/PacketStructures.h`
      `struct ObjectToObjectEffect` comment disagrees with the byte-validated
      layout in `ObjectToObjectEffectRecord`. The CLI doc already flags this as
      stale. Reconcile the header comment to the validated layout (comment-only;
      the struct is not memcpy'd on this path).

---

## Open items summary

- [ ] §1 three-way ratchet test (CI-enforced equality floor).
- [ ] §2 `StartProspectRecord` (0x2012) + `TractorOreRecord` (0x2013) + registry + floor bump.
- [ ] §3 mining fabrication-verification integration test (byte-pin the 0x000B beam).
- [ ] §4 gate: harness precedes the remaining Phase-AA fabrication.
- [ ] §5 resolve the dual-emitter + stale-comment drift findings.
