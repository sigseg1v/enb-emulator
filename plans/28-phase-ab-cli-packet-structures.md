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

In progress -- 2026-06-03. Created as the verification backbone for the
Phase-AA fabrication band. See §0 for the honest scope correction and §4 for
the deliberate ordering inversion (this harness must precede the high-risk
proxy fabrication, not follow it). **§2 landed (commit 3fcb7973):** the two
compact-band gap decoders (0x2012 START_PROSPECT + 0x2013 TRACTOR_ORE) +
`CompactMineRecordTests` byte-pin; 617/617 unit tests green.

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

- [x] **Fabrication-band ratchet landed** (`FabricationBandCoverageTests`,
      commit pending): pins every opcode in the documented fabrication band --
      the compact server->proxy sources (0x2012/0x2013/0x2014/0x2018/0x2019) and
      the client-facing targets the proxy fabricates (0x0004/0x0007/0x000B/
      0x000F/0x001B/0x0046) -- to its EXACT dedicated record type. Deleting or
      remapping any registry line in the band fails the test. This is the part
      of the invariant that drifts silently and is cheaply automatable.
- [x] Placed in the **unit** suite, not Phase-T: it needs no live stack, so it
      runs in every CI unit pass (faster, no docker). Deviates from the original
      "put it in Phase-T" note -- the band check is pure-static, so the lighter
      home is correct.
- [~] **Whole-protocol union NOT separately re-implemented.** Enumerating "every
      opcode the server emits" from C# would just be another hand-maintained
      list; the existing Phase-T `CoverageRatchetTests` / `TestedOpcodes`
      already ramps that against the LIVE server (the authoritative emitter), so
      duplicating it statically here would rot. The fabrication band -- the only
      part with silent-drift risk this phase introduced -- is covered above. The
      field-layout agreement for the band is pinned by `CompactMineRecordTests`
      (§2) against the emitter bytes + (§3) against the live fabrication.

## 2. Compact-band decoder gaps (the only real "port" work)

Two compact opcodes are NAMED in `OpcodeNames.Generated.cs` (`0x2012`
START_PROSPECT, `0x2013` TRACTOR_ORE) but fall through to `GenericRecord`.
Add real decoders mirroring `LootItemRecord`'s shape.

- [x] `StartProspectRecord` (0x2012). Layout from the server emitter
      `Player::MineResource` (`server/src/PlayerSkills.cpp:691-697`): int32
      playerGID@0, int32 asteroidGID@4, int32 effectUID@8, uint32
      prospectTick@12, uint32 drainMs@16 = 20 bytes. This is the exact packet
      our proxy now expands into the `0x000B` beam (Phase AA Wave 3) -- so the
      CLI decoding it closes the loop for byte-pinning that fabrication.
- [x] `TractorOreRecord` (0x2013). Layout from `Player::UseTractorBeam`
      (`server/src/PlayerSkills.cpp:773-785`) -- confirmed byte-identical to
      `LootItemRecord` (0x2014): GameID, ArticleUID + ArticleEffectUID,
      int16 BaseAsset (m_GameBaseAsset is short), u32 ProspectTick, LS-prefixed
      Name, u32 TractorTime, float TractorSpeed, float PosX/Y/Z.
- [x] Added both to `PacketRecordRegistry`; `CompactMineRecordTests`
      synthesises the emitter bytes and pins every field + full consumption
      (no `???` gap). 617/617 green.

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

- [x] **Dual 0x000B emitter -- RESOLVED, registered as Z-8.** The two server
      emitters DO diverge above bit 0x04: `Object::SendObjectToObjectEffectRL`
      (`ObjectClass.cpp ~850-928`) is the authoritative, capture-validated
      layout; the single-player `Player::SendObjectToObjectEffect`
      (`PlayerConnection.cpp:1394`) is wrong above 0x04 (its own comment admits
      it). Both callers of the single-player twin use Bitmask 0x07 (bits
      0x01/0x02/0x04 only), where the two AGREE, so the divergence is latent and
      the shipped 0x2012->0x000B beam is correct. Fixed the proxy citation to
      point at the authoritative RL emitter. Server alignment deferred (plans/26
      Z-8) -- no live caller exercises the wrong region; high-risk for nil gain.
- [x] **Stale struct comment -- RECONCILED.** Rewrote the per-field bitmask
      annotations in `common/include/net7/PacketStructures.h struct
      ObjectToObjectEffect` to the validated RL layout (TargetOffset@0x40,
      OutsideTargetRadius@0x08, Scale@0x80, HSV@0x100/0x200/0x400,
      Speedup@0x800). Comment-only -- field declaration order preserved; the
      struct is field-addressed and never memcpy'd to the wire on this path
      (verified: only `memset(&x,0,sizeof)` uses exist). Proxy rebuilt clean.

---

## Open items summary

- [ ] §1 three-way ratchet test (CI-enforced equality floor).
- [ ] §2 `StartProspectRecord` (0x2012) + `TractorOreRecord` (0x2013) + registry + floor bump.
- [ ] §3 mining fabrication-verification integration test (byte-pin the 0x000B beam).
- [ ] §4 gate: harness precedes the remaining Phase-AA fabrication.
- [ ] §5 resolve the dual-emitter + stale-comment drift findings.
