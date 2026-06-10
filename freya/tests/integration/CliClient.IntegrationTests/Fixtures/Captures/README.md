# Capture-replay fixtures

Small extracts from `archive/kyp-snapshot/capturedPackets/capture_1.rar`
(54MB textual hex-dump of a real 2006-era Earth & Beyond session against
the live retail server). These extracts contain
post-decrypt application-layer bytes for individual opcodes, ready to
feed straight into a codec's `DecodeInbound` / `EncodeOutbound`.

We extract the bytes (not the whole RAR) so:

1. The fixture files are KB-scale, not MB-scale; CI clone time stays sane.
2. Tests don't need `unrar` on the build host.
3. The bytes are visible in PR diffs — anyone reviewing a codec change can
   eyeball them against `common/include/net7/PacketStructures.h`.

Each file ships as hex-with-comments. Lines starting with `#` are
ignored by the loader; everything else is hex bytes (whitespace
ignored). Citing the source frame in the comment is required — that's
the primary-source proof per the server-integrity rules in CLAUDE.md.

## Files

| File | Source | Opcode | Direction | Use |
|---|---|---|---|---|
| `masterjoin_packet220.hex` | capture_1 frame 220 | 0x0035 MASTER_JOIN | client→server | codec round-trip + decoded-field reference |
| `serverredirect_packet222.hex` | capture_1 frame 222 | 0x0036 SERVER_REDIRECT | server→client | codec decode + decoded-field reference |

### Live Net-7 reference corpus (Phase AF, plans/33)

Extracted from traces captured 2026-06-04 against the **live Net-7
reference server**, on the cleartext proxy↔server UDP leg
(server→client inner frames reassembled from 0x2016/0x201A via the
production `SectorStreamReassembler`). The owner directed we treat that
server as a reference implementation to copy, so these are canonical
primary sources. The source `.pcapng` files stay in the gitignored
`proxy/local-debug/` (they carry a login leg) — only the decoded frames
below are committed. Pinned by `LiveReferenceFabricationTests`.

| File | Source frame | Opcode | Use |
|---|---|---|---|
| `live_start_prospect_2012.hex` | ProspectRun #190 | 0x2012 START_PROSPECT | fabrication-band byte-pin (20B fixed) |
| `live_tractor_ore_2013_helium.hex` | ProspectRun #197 | 0x2013 TRACTOR_ORE | variable-len pin, 6-byte name |
| `live_tractor_ore_2013_californium.hex` | ProspectRun #251 | 0x2013 TRACTOR_ORE | variable-len pin, 15-byte name |
| `live_loot_item_2014_craxelhide.hex` | KillLoot2 #91 | 0x2014 LOOT_ITEM | variable-len pin, 11-byte name |
| `live_loot_item_2014_juuona.hex` | KillLoot2 #99 | 0x2014 LOOT_ITEM | variable-len pin, 20-byte name |
| `live_client_damage_0064_dealt.hex` | Combat #16 | 0x0064 CLIENT_DAMAGE | 24B; player @16 source (deals) |
| `live_client_damage_0064_received.hex` | KillLoot2 #3 | 0x0064 CLIENT_DAMAGE | 24B; player @20 target (receives) |
| `live_object_effect_000B_weapon.hex` | Combat #4 | 0x000B OBJECT_TO_OBJECT_EFFECT | 35B string form, "~02/~WEAP_02" |
| `live_object_linked_effect_000E.hex` | Combat #14 | 0x000E OBJECT_TO_OBJECT_LINKED_EFFECT | 58B fixed |
| `live_attacker_updates_008B_start.hex` | SkillTraining | 0x008B ATTACKER_UPDATES | 9B; **MobId big-endian** (start) |
| `live_attacker_updates_008B_stop.hex` | Combat #26 | 0x008B ATTACKER_UPDATES | 9B; **MobId big-endian** (stop) |
| `live_talktree_0054_vendor.hex` | VendorInvEco #1 | 0x0054 TALK_TREE | 166B vendor dialog + branches |
| `live_talktreeaction_0056.hex` | VendorInvEco #2 | 0x0056 TALK_TREE_ACTION | 4B |
| `live_clientsound_006A_coin.hex` | KillLoot2 #16 | 0x006A CLIENT_SOUND | coin.wav |
| `live_clientsettime_0034.hex` | SingleGateJump #25 | 0x0034 CLIENT_SET_TIME | 12B; +0 tick (Z-4 data point) |
| `live_serverhandoff_003A.hex` | SingleGateJump #45 | 0x003A SERVER_HANDOFF | 112B; ticket + sector/system names |
| `live_galaxymap_0097.hex` | SingleGateJump #49 | 0x0097 GALAXY_MAP | 31B |
| `live_warpindex_009C.hex` | SingleGateJump #2 | 0x009C WARP_INDEX | 4B; -1 |
