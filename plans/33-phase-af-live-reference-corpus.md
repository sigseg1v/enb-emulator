# Phase AF -- Live reference capture corpus

## What this phase is

On 2026-06-04 the project owner captured packet traces against the **live
Net-7 production server (216.219.87.147)** and directed: treat that server as
a **reference implementation we copy**. Per CLAUDE.md "Server modification
rules", a capture of the live retail/reference server is the canonical
primary source -- the highest-weight evidence for what correct wire behaviour
is. This phase parses that corpus, banks the decoded layouts, and pins the
ones we already emit/fabricate against the live bytes as regression fixtures.

The captures live in `proxy/local-debug/` (gitignored -- they contain a real
login leg, so the `.pcapng` files are NEVER committed). What gets committed is
the **decoded hex of individual server->client frames**, extracted into
`tests/integration/.../Fixtures/Captures/*.hex` with a frame citation -- the
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
| 0x000B | OBJECT_TO_OBJECT_EFFECT (string form) | u16(0x0007), src@2, effectDesc@6, u16(0x0035), cstr "~02/~WEAP_0N" (N=1/2/3 = hardpoint), target, timestamp, u16 trailing -- 35B. This is PLAYER WEAPON FIRE (rockets at the enemy in the kill step), distinct from the numeric-EffectDescID form the proxy fabricates from 0x2012. Frame order proves it: 0x000B fall AFTER all mine/tractor cycles and interleave with 0x0064 damage. | `[banked]` (Z-8 dual-emitter context) |
| 0x0064 | CLIENT_DAMAGE | 24B: float@0, float@4, u32@8, u32@12, u32@16, u32@20. In combat @16=a gameID, @20=a weapon effectDesc. SUSPECTED CLI mislabel: ClientDamageRecord calls @20 "TargetId" but it looks like a weapon effectDesc. NEEDS CLI parse + test before any change. | `[banked]` -- verify |
| 0x000E | OBJECT_TO_OBJECT_LINKED_EFFECT | 58B projectile/munition | `[banked]` |
| 0x008B | ATTACKER_UPDATES | 9B effect/buff state; tail matches an effectDesc | `[banked]` |
| 0x008C | LOOT_HULK_PERMISSION | (KillLoot2) | `[banked]` |
| 0x006A | sound trigger | carries a wav name (e.g. "coin.wav") | `[banked]` |

### Vendor / economy band (starbase :3636)

| Opcode | Name | Layout (observed) | Status |
|---|---|---|---|
| 0x0054 | vendor dialog | NPC dialog text + buttons | `[banked]` |
| 0x0056 | dialog state | u32 state | `[banked]` |
| 0x006A | sound | coin.wav etc on purchase | `[banked]` |
| 0x0027 | INVENTORY_MOVE | 36B client->server request, BE-looking 00 00 00 NN fields; drives prospect/tractor/loot | `[banked]` |

### Gate-jump band (SingleGateJump :3573 -> :3569)

| Opcode | Name | Layout (observed) | Status |
|---|---|---|---|
| 0x003A | SERVER_HANDOFF | 112B; dest sector id (BE), "MY_Avatar_Ticket", "Ishuan (Castor System)", system + adjacent nav names | `[banked]` |
| 0x009C | WARP_INDEX | -1 = FF FF FF FF (arrival AND boundary-interrupt both send -1) | `[verified]` (WarpIndexRecord; AE narration) |
| 0x0034 | gate-cache | u32 id + 2x timestamp, 12B | `[banked]` |
| 0x0097 | GALAXY_MAP | adjacency | `[verified]` (GalaxyMapRecord) |

## Checklist

- [x] AF-1 Build scratch opcode dumper reusing `SectorStreamReassembler` (`proxy/local-debug/opdump/`)
- [x] AF-2 Parse all 6 captures; per-capture inventory + per-opcode decode table (this file)
- [x] AF-3 Confirm 0x2012/0x2013/0x2014 CLI records match the live bytes byte-for-byte (they do)
- [x] AF-4 Resolve the 0x2012 id-field-order question against server source (records correct; note was wrong)
- [x] AF-5 **Pin the fabrication band** with real-capture fixtures + byte-pin tests
      (`LiveReferenceFabricationTests`, 5 fixtures, ProspectRun/KillLoot2 frames). 5/5 green.
- [x] AF-6 Fix the pre-existing integration build break (`SectorMvasMoveTests.cs` method-group
      `onInbound: world.Ingest` broke when 5c991f84 changed `Ingest`'s return type -> wrapped in a lambda)
- [ ] AF-7 Bank -> pin combat band (0x000B string form, 0x0064 damage, 0x000E, 0x008B) -- needs the
      live-mining/combat harness (shares the AD-66 / #66 in-space harness)
- [ ] AF-8 Verify/correct `ClientDamageRecord` 0x0064 @20 (weapon effectDesc vs "TargetId") -- CLI parse + test first
- [ ] AF-9 Bank -> pin vendor band (0x0054 dialog / 0x0056 / 0x006A sound) + the 0x0027 request family
- [ ] AF-10 Bank -> pin gate-jump band (0x003A handoff, 0x0034 gate-cache, 0x0097)

## Relationship to other phases

- **Unblocks the field-layout half of AD-2 / AD-3 (plans/31) and Phase AB §4.**
  Those tasks were CV-gated because 0x2013 tractor / 0x2014 loot had un-citable
  wire fields. The live corpus now SUPPLIES those fields (byte-pinned here), so
  the format is proven. What remains for those is the real-client (client.exe)
  confirmation that the proxy's *fabricated* client-facing expansion renders
  correctly -- still a CV-gate entry in plans/29, NOT a format unknown.
- **Combat 0x000B string form** feeds the Z-8 dual-emitter note (plans/26) and
  AD-66 / #66 live 0x000B beam pin.
- Sourcing stays neutral (clean-room stream observation + committed decoded
  frames); no `.pcapng` is ever committed.
