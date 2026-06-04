# Capture-replay fixtures

Small extracts from `archive/kyp-snapshot/capturedPackets/capture_1.rar`
(54MB textual hex-dump of a real 2006-era Earth & Beyond session against
the live retail server at `159.153.232.146`). These extracts contain
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
reference server 216.219.87.147**, on the cleartext proxy↔server UDP leg
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
