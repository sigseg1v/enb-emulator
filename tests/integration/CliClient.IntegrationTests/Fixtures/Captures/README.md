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
