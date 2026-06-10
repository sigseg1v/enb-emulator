# Phase AF -- Live reference capture corpus

## What this phase is

On 2026-06-04 the project owner captured packet traces against the **live
Net-7 production server (the live reference server)** and directed: treat that server as
a **reference implementation we copy**. Per CLAUDE.md "Server modification
rules", a capture of the live retail/reference server is the canonical
primary source -- the highest-weight evidence for what correct wire behaviour
is. This phase parses that corpus, banks the decoded layouts, and pins the
ones we already emit/fabricate against the live bytes as regression fixtures.

The captures live in `proxy/local-debug/` (gitignored -- they contain a real
login leg, so the `.pcapng` files are NEVER committed). What gets committed is
the **decoded hex of individual server->client frames**, extracted into
`freya/tests/integration/.../Fixtures/Captures/*.hex` with a frame citation -- the
approved clean-room-observation artifact, same pattern as the existing
`masterjoin_packet220.hex` / `serverredirect_packet222.hex`.

All captures are on the **cleartext proxy<->server UDP leg**. Server->client
"inner" frames are reassembled from the 0x2016/0x201A packed datagrams via the
production `SectorStreamReassembler` (so decoded offsets match the CLI exactly).
Client->server requests appear as outer datagram opcodes. The proxy is NOT a
dumb relay: 0x20xx control opcodes here are CONSUMED/EXPANDED by the proxy and
never reach the real client in this form (see CLAUDE.md).

Scratch dumper: `proxy/local-debug/opdump/` (gitignored) -- reuses
`CliClient.Core`'s reassembler; run
`dotnet run --project proxy/local-debug/opdump -c Release -- <cap.pcapng> [hexOp ...]`.

Player avatar GameIDs in the corpus: ProspectRun = `0x4000C95F`; the five
2026-06-04 072xxx/073xxx captures = `0x4003992A`.

## The corpus

| Capture | Owner's actions | Sector port | Notable inner opcodes |
|---|---|---|---|
| `ProspectRun-...065646` | buff ship, warp to Ishuan nav, mine gas-cloud/asteroid/hydrocarbon, tractor each, kill+loot enemy | :3573 / MVAS :3806 | 0x2012 x?, 0x2013 x4, 0x000B x8, 0x0064, 0x0007 x26 |
| `Combat-...072157` | combat against NPCs | :3573 | 0x000B x6, 0x000E x4, 0x0064 x2, 0x008B, 0x006A x3, 0x0089 x4 |
| `VendorInvEco-...072833` | open vendor, inventory, economy UI | :3636 (starbase) | 0x0054 x2, 0x0056 x4, 0x006A x6, 0x001B x39 |
| `SkillTrainingHostileDevice2-...073543` | skill training + deploy hostile device | :3573 | 0x000B x7, 0x000E x4, 0x0064 x4, 0x008B x3 |
| `KillLoot2-...073637` | kill enemy, loot hulk | :3573 | 0x2014 x3, 0x000B x6, 0x0064 x5, 0x008C x2 (loot perm), 0x0066 x2 |
| `SingleGateJump-...073814` | jump one gate | :3573 -> :3569 | 0x003A handoff, 0x009C, 0x0034 x3, 0x0097, 0x2018 x16, 0x2020 x4, 0x2011 |

## Decode table

Status legend: `[pinned]` real-capture fixture + byte-pin test committed;
`[verified]` decoded + confirmed against an existing CLI record but not yet
fixtured; `[banked]` decoded layout recorded here for a later wave.

### Fabrication band (proxy CONSUMES/EXPANDS -- never reaches client raw)

| Opcode | Name | Len | Layout | Status |
|---|---|---|---|---|
| 0x2012 | START_PROSPECT | 20 fixed | PlayerGID@0, AsteroidGID@4 (obj->GameID, STATIC node, 0x000188xx band), EffectUID@8 (GetSectorNextObjID, DYNAMIC 0x0025xxxx), ProspectTick@12, DrainMs@16 | `[pinned]` `live_start_prospect_2012.hex` (ProspectRun #190) |
| 0x2013 | TRACTOR_ORE | var | PlayerGID@0, article_UID@4, article_effect_UID@8, basset@12(i16), prospect_tick@14, nameLen@18(i16), name@20, tractor_time@(20+n), tractor_speed@+4 (=350.0), pos xyz@+8 | `[pinned]` Helium #197 + Californium #251 |
| 0x2014 | LOOT_ITEM | var | identical to 0x2013 (article-create reused for loot drops) | `[pinned]` Craxel Hide #91 + Juuona #99 |
| 0x2018 | STATIC_OBJECT_CREATE | var | nav/station/gate/deco spawn (already EXPANDED by proxy, AA Wave 4) | `[verified]` (SingleGateJump #?, 16x) |
| 0x2020 / 0x2011 | gate-cache control | -- | proxy-consumed login/gate-cache band | `[banked]` |

Key finding (corrected mid-parse): the two id fields in 0x2012 read in the
WRONG order in a working note at first. The server source settles it --
`PlayerSkills.cpp:691-695` emits `GameID(), obj->GameID(), effect_UID, tick,
drain`, and `effect_UID = GetSectorNextObjID()` (the dynamic 0x0025xxxx
counter). STATIC resource nodes carry low 0x000188xx GameIDs; DYNAMIC spawns
(NPC ships, tractored articles, the effect_UID) come from the running sector
counter = 0x0025xxxx. So `@4` (0x000188xx) is the asteroid's obj->GameID() and
`@8` (0x0025xxxx) is effect_UID -- matching the existing CLI records exactly.
The records were already correct; only the scratch note was mislabeled.

### Combat band (server->client, direct -- NOT proxy-fabricated)

| Opcode | Name | Layout (observed) | Status |
|---|---|---|---|
| 0x000B | OBJECT_TO_OBJECT_EFFECT (string form) | 35B: Bitmask@0(0x0007), GameID@2, TargetID@6, EffectDescID@A, cstr "~02/~WEAP_0N"@C (N=hardpoint), EffectID@19, TimeStamp@1D, Duration@21. PLAYER WEAPON FIRE, distinct from the numeric-EffectDescID form the proxy fabricates from 0x2012. Frame order proves it: 0x000B fall AFTER all mine/tractor cycles and interleave with 0x0064 damage. | `[pinned]` Combat #4 |
| 0x0064 | CLIENT_DAMAGE | 24B: Damage@0(f), Modifier@4(f), Type@8, Inflicted@12, SourceId@16, TargetId@20 -- @16/@20 are BOTH entity GameIDs (the player's own id flips @16<->@20 by damage direction). The earlier "@20 is a weapon effectDesc" suspicion was wrong: the non-player id is just in the static 0x00018xxx band. | `[pinned]` dealt (Combat #16) + received (KillLoot2 #3) |
| 0x000E | OBJECT_TO_OBJECT_LINKED_EFFECT | 58B: ObjectID@0, TimeStamp@4, SourceID@8, TargetID@D, LinkedEffectDescID@11, EffectDescID@13, TargetOffset@15, Scale@26(f), HSVShift@2A, Speedup@36(f). All little-endian. | `[pinned]` Combat #14 |
| 0x008B | ATTACKER_UPDATES | 9B: Update@0(LE; 0=stop,1=start), Fixed@4(0x01), **MobId@5 BIG-ENDIAN**. Trap-1 defect: record + server both read/wrote LE -> byte-reversed attacker id to the client. Fixed (CLI `ReadI32BE`; server `htonl(mob_id)`); plans/29 CV-08. | `[pinned]` start (SkillTraining) + stop (Combat #26) |
| 0x008C | LOOT_HULK_PERMISSION | (KillLoot2) | `[banked]` |
| 0x006A | sound trigger | carries a wav name (e.g. "coin.wav") | `[banked]` |

### Vendor / economy band (starbase :3636)

| Opcode | Name | Layout (observed) | Status |
|---|---|---|---|
| 0x0054 | TALK_TREE | MainText (cstr) + NumBranches + per-branch (u32 dest + cstr text) | `[pinned]` VendorInvEco #1 |
| 0x0056 | TALK_TREE_ACTION | u32 Action (dialog state) | `[pinned]` VendorInvEco #2 |
| 0x006A | CLIENT_SOUND | Length@0, SoundName@4 (cstr), Channel, Queue -- "coin.wav" on purchase | `[pinned]` VendorInvEco #16 |
| 0x0027 | INVENTORY_MOVE | 36B client->server request, BE-looking 00 00 00 NN fields; drives prospect/tractor/loot | `[banked]` (client->server) |

### Gate-jump band (SingleGateJump :3573 -> :3569)

| Opcode | Name | Layout (observed) | Status |
|---|---|---|---|
| 0x003A | SERVER_HANDOFF | 112B; AvatarIdLsb, ToSectorID (BE), FromSectorID (BE), "MY_Avatar_Ticket", from/to sector+system names | `[pinned]` SingleGateJump #45 |
| 0x009C | WARP_INDEX | -1 = FF FF FF FF (arrival AND boundary-interrupt both send -1) | `[pinned]` SingleGateJump #2 |
| 0x0034 | CLIENT_SET_TIME | ClientSent@0, ServerReceived@4, ServerSent@8 -- time-sync ping, 12B (NOT gate-cache; my first note was wrong). ServerReceived==ServerSent (+0 tick) in live. | `[pinned]` SingleGateJump #25 |
| 0x0097 | GALAXY_MAP | Type 4 map update: PlayerID + system/sector/station names | `[pinned]` SingleGateJump #49 |

## Checklist

- [x] AF-1 Build scratch opcode dumper reusing `SectorStreamReassembler` (`proxy/local-debug/opdump/`)
- [x] AF-2 Parse all 6 captures; per-capture inventory + per-opcode decode table (this file)
- [x] AF-3 Confirm 0x2012/0x2013/0x2014 CLI records match the live bytes byte-for-byte (they do)
- [x] AF-4 Resolve the 0x2012 id-field-order question against server source (records correct; note was wrong)
- [x] AF-5 **Pin the fabrication band** with real-capture fixtures + byte-pin tests
      (`LiveReferenceFabricationTests`, 5 fixtures, ProspectRun/KillLoot2 frames). 5/5 green.
- [x] AF-6 Fix the pre-existing integration build break (`SectorMvasMoveTests.cs` method-group
      `onInbound: world.Ingest` broke when 5c991f84 changed `Ingest`'s return type -> wrapped in a lambda)
- [x] AF-7 Pin combat band. 0x0064 DONE (see AF-8). 0x000B (35B string form) + 0x000E pinned clean
      (`LiveReferenceCombatTests`); both decode the live Combat frames with full byte coverage, no
      code change. **0x008B ATTACKER_UPDATES: real Trap-1 defect found + fixed.** The reference emits
      the mob id BIG-ENDIAN (`00 01 86 F5` = 0x000186F5), but `AttackerUpdatesRecord` AND the server
      (`SendAttackerUpdates`) both read/wrote it little-endian -- the server was handing the real client
      a byte-reversed attacker id. Cross-corroborated: the `00 01 87 EC` mob is the SAME 0x000187EC the
      0x0064 frames carry little-endian. Fixed CLI record (`ReadI32BE`) + pinned both start/stop frames
      first, then fixed the server (`htonl(mob_id)`, consistent with the existing htonl'd recustomize
      playerid). Server change cites Combat frame #26; real-client check = plans/29 CV-08. Docker server
      build green. (This is exactly the AD-66 / #66 combat-band byte-pin -- done here, not deferred.)
- [x] AF-8 Verify `ClientDamageRecord` 0x0064 @16/@20. **Result: NO mislabel -- record is correct.**
      The server emitter (PlayerConnection.cpp:4563-4573) passes (source_id, target_id), both entity
      GameIDs. Live frames prove it: the player's own GameID sits at @16 when dealing and @20 when
      receiving, so both fields are entities flipping by direction (the non-player id is in the static
      0x00018xxx band, which I'd misread as an "effectDesc"). Pinned both directions:
      `live_client_damage_0064_dealt.hex` (Combat #16) + `live_client_damage_0064_received.hex`
      (KillLoot2 #3), `LiveReferenceFabricationTests` 7/7 green. No code change.
- [x] AF-9 Pin vendor band: 0x0054 TALK_TREE (vendor dialog + branches), 0x0056 TALK_TREE_ACTION,
      0x006A CLIENT_SOUND (coin.wav). All existing records decode the live frames with full byte
      coverage. `LiveReferenceDialogGateTests`. (0x0027 request family deferred -- it's client->server.)
- [x] AF-10 Pin gate-jump band: 0x003A SERVER_HANDOFF (112B, ticket + sector/system names),
      0x0034 CLIENT_SET_TIME, 0x0097 GALAXY_MAP, 0x009C WARP_INDEX. All clean. `LiveReferenceDialogGateTests`.
      **Z-4 data point:** 0x0034 ServerReceived == ServerSent (+0 tick) in every live frame -- the live
      reference AGREES with our server's "equal" emit, contradicting the plans/26 Z-4 "retail +1 tick"
      note. Surface to Phase Z; do not change anything (our behaviour is already reference-correct here).

## Relationship to other phases

- **Unblocks the field-layout half of AD-2 / AD-3 (plans/31) and Phase AB §4.**
  Those tasks were CV-gated because 0x2013 tractor / 0x2014 loot had un-citable
  wire fields. The live corpus now SUPPLIES those fields (byte-pinned here), so
  the format is proven. What remains for those is the real-client (client.exe)
  confirmation that the proxy's *fabricated* client-facing expansion renders
  correctly -- still a CV-gate entry in plans/29, NOT a format unknown.
- **Combat 0x000B string form** feeds the Z-8 dual-emitter note (plans/26); now
  byte-pinned here (Combat #4), which satisfies the AD-66 / #66 live 0x000B beam
  pin.
- Sourcing stays neutral (clean-room stream observation + committed decoded
  frames); no `.pcapng` is ever committed.
