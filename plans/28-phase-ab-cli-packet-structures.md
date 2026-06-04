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
proxy fabrication, not follow it).

- **§1 landed** (`FabricationBandCoverageTests`, commit 0e2c16cd): 11-opcode
  fabrication-band ratchet.
- **§2 landed** (commit 3fcb7973): the two compact-band gap decoders
  (0x2012 START_PROSPECT + 0x2013 TRACTOR_ORE) + `CompactMineRecordTests`.
- **§3 spec half landed** (commit 9276d632): `ProspectBeamFabricationSpecTests`
  pins the 0x2012->0x000B fabrication contract incl. the Duration cap; the
  **live** round-trip is `[!]` blocked behind Phase K (see §3).
- **§5 landed** (commits dbe93970 + dae171ea): dual-emitter drift resolved
  (Z-8); proxy beam Duration capped at 32000 to match the authoritative
  range-list emitter.

621/621 unit tests green.

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
      commit 0e2c16cd): pins every opcode in the documented fabrication band --
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

- [x] **Spec half landed** (commit 9276d632): `ProspectBeamFabricationSpecTests`
      re-states in C# the exact 0x000B bytes `UDPClient::StartProspecting`
      fabricates, decodes them with the production `ObjectToObjectEffectRecord`,
      and pins Bitmask 0x0007, GameID=prospector, TargetID=asteroid,
      EffectDescID=0x00BF, EffectID/TimeStamp/Duration, plus the Duration cap.
      It is honestly labelled a SPEC, not a live capture: it is the byte
      reference the live harness will assert equality against, and it
      regression-guards the cap. It does NOT auto-catch proxy C++ drift (only
      the live harness can) -- that limit is documented in the test.
- [x] **Undock / dock->space transition PROVEN live (2026-06-03).** The
      earlier "blocked behind Phase K crash-bisection" claim was wrong and is
      retracted. A fresh character starts DOCKED at a starbase (sector id >
      9999; `StartSector[]` 10151..10551, `MAX_SECTOR_ID` 9999). The real
      undock is opcode **0x004E STARBASE_REQUEST with Action=1** ("exit
      station"), NOT 0x009F (which only walks between rooms inside a station).
      Primary source: live capture
      `proxy/local-debug/net7-live-2026-06-01-login-undock-dock-logout.pcap`
      seq=26 (the 0x009F frames at seq 15/25/61 are room-walks; only seq=26
      triggers the launch). Server path: `Player::HandleStarbaseRequest`
      case-1 (PlayerConnection.cpp:9879) -> `SectorManager::LaunchIntoSpace`
      (SectorManager.cpp:558), which drops the player from the station sector
      and hands off to the parent SPACE sector `m_SectorID / 10` (10151 ->
      1015). Implemented in the CLI `undock` command (commit c8c944e3, byte-
      pinned to seq=26) and verified END-TO-END live through our proxy:
      `SectorServerHandoffTests` sends 0x004E Action=1 from Luna 10151 and
      asserts the 0x003A SERVER_HANDOFF arrives with ToSectorID=1015 (commit
      aef3ec3c). No server-log errors; no client crash.
- [~] **Live mining still gated on the MVAS move IP-mismatch.** Sub-task (a)
      DONE and PROVEN live: the CLI now FOLLOWS the 0x003A handoff into space.
      `SectorEnterDriver.FollowHandoffAsync` re-runs master-join + sector LOGIN
      against ToSectorID reusing the SAME GameId (no fresh GlobalTicketRequest --
      the server kept the player node alive via DropPlayerFromSector, not
      DropPlayerFromGalaxy, SectorManager.cpp:574), and `UndockCommand` swaps the
      active sector connection to the space leg. Verified live by
      `SectorUndockHandoffFollowTests.Undock_FollowsHandoff_LandsInSpaceWithAsteroids`:
      undock from Luna Station 10151 -> handoff -> re-join space sector 1015,
      START 0x0B, 141 objects (7 mob spawns / 17 navs). NOTE: 1015 is a mob/nav
      newbie field, NOT a resource sector -- it has 0 OT_RESOURCE. Landing in
      space does NOT itself show asteroids; mining requires a subsequent
      gate/warp to a resource sector (e.g. 1710=45 asteroids, 1070=38).
      Sub-task (b) DISPROVEN (the earlier diagnosis was wrong): the direct MVAS
      feed is NOT dropped. The anti-spoof guard (UDP_Client.cpp:72,
      PlayerIPAddr()==source_addr) lives ONLY on the SECTOR connection
      (HandleClientOpcode). The MVAS port (3806) runs a different dispatcher
      (HandleMVASOpcode, UDPConnection.cpp:214); its 0x1004 handler
      (HandleMVASPosReturn, UDP_MVAS.cpp:118) and its 0x3005 keepalive
      (HandleKeepCommsAlive, UDP_MVAS.cpp:206) key purely on hdr->player_id and
      re-point SetPlayerPortIP UNCONDITIONALLY -- no IP guard. So a headless
      CLI's direct 0x1004 from the docker-gateway IP IS accepted even though the
      player was established through the proxy. PROVEN live by
      `SectorMvasMoveTests.Mvas_DirectPositionFeed_IsAccepted_AndStreamsSectorBack`:
      after undock+handoff-follow into 1015, a direct SectorUdpClient feed to
      :3806 gets back 0x1007 MVAS_TOGGLE_SEND_FREQ (the server's direct reply to
      our position) and 0x2016 PACKET_SEQUENCE (the player's downstream sector
      stream re-routed to our socket). NO server change, NO guard weakened.
      Remaining toward live mining: gate/warp from a mob sector (e.g. 1015) to a
      resource sector (1710/1070), then run the prospect/mine chain below.
- [~] **Gate command landed (commit `fbe16a5e`).** CLI `gate <gid>` sends
      `0x002C ACTION Action=18` (select, Target=gate gid) then, ~6s later
      (mirroring the retail B_CAMERA_CONTROL +5800ms), `Action=19` (finish),
      then follows the `0x003A SERVER_HANDOFF` into the destination sector with
      the same avatar id. Server path: `PlayerConnection.cpp:3923` case 18 ->
      `SectorManager::Gate` (`:639`) stores StargateDestination; `:3965` case 19
      -> `SectorServerHandoff` (`:579`, DropPlayerFromSector -- keeps the node
      alive, identical to LaunchIntoSpace). Shared post-handoff re-join extracted
      to `HandoffFollow.CompleteAsync`; `undock` refactored onto it. ActionPacket
      byte-pinned (16 bytes, 4x int32 LE, `PacketStructures.h:546`). 687 unit
      tests green. Single faithful route from a newbie space sector to a resource
      sector: Luna space 1015 -> obj 533 "Sector Gate to Earth" -> 1060 (20
      type-38 resource nodes). **Gate jump PROVEN live** by
      `SectorGateHandoffFollowTests.Gate_FromMobSector_LandsInResourceSector`
      (commit `77b40318`): undock 10151 -> 1015, find "Sector Gate to Earth" in
      the fanout, send 0x002C Action 18 then 19, assert 0x003A -> 1060, follow
      the handoff into Earth space, assert resource nodes fan out. 27s, no
      server change, no server-log errors. Route confirmed against the live DB
      (sector_objects 1015 gate 533 gate_to 1060; 1060 type-38 = 20 nodes).
      Real-client gate-send (does client.exe emit 18->19?) still tracked as
      `plans/29` CV-06.
- [ ] **Live mining chain -- remaining, with two real blockers found (not yet
      done).** Tracing the server: mining is triggered by **0x0027
      INVENTORY_MOVE** with `FromInv==18` ("From Mining Window") and
      `FromSlot`=the asteroid's resource slot, target = an OT_RESOURCE
      (PlayerConnection.cpp:3210-3224 -> `MineResource`). `MineResource`
      (PlayerSkills.cpp:625) then `SendToRangeList(ENB_OPCODE_2012_START_PROSPECT,
      ...)` (the 20-byte compact our CLI already decodes as `StartProspectRecord`).
      `CheckMiningConditions` (PlayerSkills.cpp:884) gates it on: target is
      OT_RESOURCE with a non-empty slot; **`SKILL_PROSPECT` level > 0**; ship
      NOT moving; inventory room; reactor energy >= per-ore; **within
      `ProspectRange()` of the asteroid**; not incapacitated; `m_Gating` false.
      BLOCKER 1 (RESOLVED, commit pending): a fresh Terran Warrior (10151 start)
      has NO prospecting skill. Added `just grant-prospect <user> [slot] [level]
      [explore]` -- it UPSERTs `avatar_skill_levels (avatar_id, 41, level)` and
      sets `avatar_info.explore`, which the server loads at login
      (PlayerSaves.cpp:653-680); prospecting is gated only on
      `Skill[SKILL_PROSPECT].GetLevel() != 0`. Prospect MaxLevel is 7 for the
      Explorer classes and 0 elsewhere (Skills.xml, `Quest="1"`), so the recipe
      warns if the target is not prof=2 (it would clamp to 0 at login).
      Workflow: seed-account -> play-cli `create JE <name>` -> grant-prospect ->
      relog. Verified against a synthetic JE: prospect=7, explore=150, "OK
      (Explorer)"; non-explorer correctly warns. NOTE: `CheckMiningConditions`
      (PlayerSkills.cpp:884-943) does NOT require a mining beam equipped -- it
      gates only on SKILL_PROSPECT>0, target OT_RESOURCE/OT_HULK with a non-empty
      slot, ship stationary, `m_ProspectBeam` not already active, inventory room,
      and reactor energy >= per-ore. So after grant-prospect the only remaining
      live steps are: reach a resource sector, target an asteroid, sit still,
      then send 0x0027 sub-18. BLOCKER 2: the CLI
      integration harness talks DIRECTLY to the sector server, so it only sees
      the server's compact `0x2012 START_PROSPECT`, NOT the proxy-fabricated
      `0x000B` beam. Byte-pinning the fabricated beam (the actual §3 deliverable)
      requires a PROXY-ROUTED harness; the direct-to-server suite can pin
      0x2012 but cannot observe the 0x000B fabrication. So the live mining
      round-trip is two distinct pieces of remaining work, not a quick add-on.
- [ ] When the CLI lands in space: drive the prospect/mine path, drain the
      client-leg frames, assert the `0x000B` arrives with the fields above, and
      assert byte-equality against `FabricateBeamBody` from the spec test (so
      the spec and the live behaviour are tied together).
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
- [x] **Uncapped Duration in the proxy beam fabrication -- FIXED** (commit
      dae171ea). `UDPClient::StartProspecting` wrote Duration as
      `(drain_ms & 0xFFFF)` with no cap. The client reads the field SIGNED, so
      a value > 32767 ms wraps negative and the beam does not render; a full
      ore stack mines for well over 32.7s. The authoritative range-list emitter
      `Object::SendObjectToObjectEffectRL` caps at 32000 (ObjectClass.cpp:884-885)
      for exactly this reason. Mirrored the cap in the proxy; the prior comment
      (which cited the uncapped single-player path and a ~65s wrap) was wrong
      and is corrected. Linux + Win64 proxy rebuilt clean.

---

## Open items summary

- [x] §1 fabrication-band ratchet test (commit 0e2c16cd).
- [x] §2 `StartProspectRecord` (0x2012) + `TractorOreRecord` (0x2013) + registry + byte-pin (commit 3fcb7973).
- [~] §3 fabrication verification: SPEC half landed (commit 9276d632);
      undock/dock->space PROVEN live (0x004E Action=1 -> handoff to 1015,
      commits c8c944e3 + aef3ec3c); LIVE mining round-trip now gated only on
      the CLI following the handoff into space (no longer Phase-K-blocked).
- [ ] §4 gate: the §3 LIVE harness must precede the remaining Phase-AA
      fabrication (0x2013/0x2014 tractor/loot, 0x2018/0x2019 spawn) -- those
      have un-citable fields that crash the Win32 client if wrong, so they stay
      blocked on the live harness, which is blocked on Phase K. The spec half
      alone is NOT sufficient to unblock them (a C# spec cannot validate proxy
      C++ output against the real client).
- [x] §5 dual-emitter + stale-comment + uncapped-Duration drift findings
      resolved (commits dbe93970, dae171ea).
- [x] §6 `pcap-inventory` tool + sector-object metadata parity. Built
      `tools/pcap-inventory/` -- a standalone decoder that turns a
      proxy<->server sector `.pcapng` into a nav/station/gate + mob + resource
      inventory, reusing `SectorStreamReassembler` / `SectorWorld` /
      `AuxDataRecord` so the byte semantics stay in lock-step with the CLI. It
      is an accumulating decode (ignores `0x0007` REMOVE) so flying out of range
      does not drop an already-catalogued object. Captures + outputs are
      gitignored (a capture may carry credentials). While wiring it, surfaced
      and fixed real CLI gaps, each verified against an upstream net7 Ishuan
      capture:
      - **`0x0089` RELATIONSHIP reaction enum was WRONG in the CLI.**
        `RelationshipRecord` annotated `5=FRIENDLY/4=NEUTRAL/3=HOSTILE/0=UNFRIENDLY`;
        the authoritative server enum (PacketStructures.h `RELATIONSHIP_*` +
        `MOB::SendRelationship`) is `0=ATTACK/1=SHUN/2=FRIENDLY/3=ADORATION`, and
        the capture only ever carries 0/1/2/3 -- never 4/5. Corrected the labels.
      - **`SectorWorld` now models disposition.** Added `Reaction`/`IsAttacking`
        (from `0x0089`) and `Faction` (from `0x001B` ShipIndex flag 57
        FactionIdentifier) to `Tracked`, plus `ReactionName()`. This is what gives
        each mob a hostile/shun/friendly/adoration label.
      - **Hidden navs were mislabelled as decos.** `SectorWorld` keyed nav-ness
        off seeing a `0x0099 NAVIGATION` frame, but the server gates
        `SendNavigation` on `AppearsInRadar` (NavTypeClass.cpp:417), so a HIDDEN
        nav (clickable but off-minimap: `IS_NAV`/`NavType>0`, `HAS_NAV` clear --
        e.g. "Traders Run", "Strange Ship") emits no `0x0099` and fell through
        to "deco". Fixed `IngestStatic` to read the `0x2018` sig_flags byte@57
        directly (the server's authoritative nav class -- DB `nav_type` from the
        `sector_nav_points` LEFT JOIN, SectorContentSQL.cpp:231/373) and label
        `nav` / `nav (major)` / `nav (hidden)` / `nav (hidden, major)` vs a true
        `deco`. Cross-validated against the runtime DB `appears_in_radar` for the
        Ishuan objects (Traders Run/Little Rock radar=1 -> on-minimap nav;
        Strange Ship/Hou'jeu Byeon radar=0 -> hidden nav; Deco/Rings -> deco).
      - Read-side only (no server/proxy wire change), so no plans/29 CV entry is
        required. Pinned by new `SectorWorldTests` (reaction theory + faction +
        unknown-reaction guard + nav-class sig_flags theory); full CLI unit suite
        green (682; 687 after the gate-command tests landed).
