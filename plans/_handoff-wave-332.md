# Handoff: Phase K Wave 332 -- structured-decode the 15 missing handshake opcodes

**Status at handoff**: HEAD = `884c56f6` (Wave 331 race-fix + replay landed). Build
green. Wave 331 task #215 completed. Wave 332 is the next entry.

## Goal (verbatim from user)

> "yes we need to build out all 29 or whatever of those"
> "i want to see the details in every packet that we are getting so far up to the crash"
> "i also want to be able to dump and fully understand the captured login packet
> traces so i can compare later so make sure to get all those"

Translation: every frame in REPL `dump-on` output AND every frame in offline
`replay` output should produce GlobalTicket-style structured field decode, not
the hex-only `GenericRecord` fallback. We are NOT inventing new fixtures or
investigating crashes in this wave; the deliverable is decoder coverage.

## Concrete scope: 15 PacketRecord subclasses

All server-to-client, all observed in the sector-handshake stream after
MasterJoin. Implement in difficulty order (easy first so a failed mid-wave
doesn't leave the hard ones unstarted):

| Opcode | Name                              | Complexity     |
|--------|-----------------------------------|----------------|
| 0x0034 | CLIENT_SET_TIME                   | trivial (~12B) |
| 0x001D | MESSAGE_STRING                    | small + string |
| 0x0011 | COLORIZATION                      | small fixed    |
| 0x0010 | DECAL                             | small fixed    |
| 0x0009 | OBJECT_EFFECT                     | small fixed    |
| 0x003E | ADVANCED_POSITIONAL_UPDATE        | medium fixed   |
| 0x00B4 | SUBPARTS                          | medium         |
| 0x0089 | RELATIONSHIP                      | medium         |
| 0x0052 | LOUNGE_NPC                        | medium         |
| 0x007F | MANUFACTURE_SET_MANUFACTURE_ID    | medium         |
| 0x001B | AUX_DATA                          | medium-large   |
| 0x0097 | GALAXY_MAP                        | large          |
| 0x0037 | CLIENT_AVATAR                     | variable-shape |
| 0x0047 | CLIENT_SHIP                       | variable-shape |
| 0x0025 | ITEM_BASE                         | hardest (nested items, variable) |

## Procedure (per opcode)

1. Look up the symbol in `/data/dev/enb-emulator/common/include/net7/Opcodes.h`
   to confirm the value -> name mapping.
2. Find the server emitter in `/data/dev/enb-emulator/server/src/` -- typically
   `PlayerClass.cpp`, `PlayerConnection.cpp`, `SectorManager.cpp`, `Items/*`.
   The emitter is usually a `SendResponse((unsigned char*)&someStruct, sizeof)`
   call or a manual `*((int*)p) = X; p += 4;` buffer build.
3. Look up the struct in `/data/dev/enb-emulator/common/include/net7/PacketStructures.h`
   for fixed-shape packets; reconstruct from the build code for variable ones.
4. **WIRE-FORMAT TRAP** (re-read CLAUDE.md "Wire format & byte order"): x86 LE.
   Most fields are host-order. `htonl`/`ntohl` on the send path is usually a
   bug, EXCEPT for true network-order IP addresses (ServerRedirect.m_IpAddress
   is the canonical legit case). Note any quirks per-opcode.
5. Cross-check against retail bytes in
   `/data/dev/enb-emulator/archive/replay/capture_1-sector-s2c.bin`
   (avatar=Ace, ship="Revenge of the Jenquai", sector=45151, 101 frames).
   Recognisable field values (sector id, names, coordinates) prove the decode.

## Code placement

- Each record class: `tools/cli-client/src/CliClient.Core/Opcodes/Records/<Name>Record.cs`.
- Subclass `PacketRecord`, override `WriteFields(StringBuilder sb)`.
- Pattern reference: `GlobalTicketRecord.cs`, `AvatarDescriptionRecord.cs`,
  `NameDecalRecord.cs`, `CreateRecord.cs`.
- Helpers already available in `PacketRecord` base: `FieldHex`,
  `FieldDec`, `FieldFloat`, `FieldString`, `Flag`, `FlagSuspicious`,
  `ReadI32LE`, `ReadU32LE`, `ReadI32BE`, `ReadU16LE`, `ReadI16LE`,
  `ReadF32LE`, `ReadNulString`, `FindFirstAsciiString`,
  `ExtractAsciiStrings`.
- Register each opcode in
  `tools/cli-client/src/CliClient.Core/Opcodes/Records/PacketRecordRegistry.cs`
  (one switch arm per opcode).

## Verify

1. `dotnet build tools/cli-client/src/CliClient.App` -- expect `0 Warning(s), 0 Error(s)`.
2. `NO_COLOR=1 just cli-replay 2>&1 | head -300` -- every frame should show
   structured fields; no `ascii-scan = (none)` lines (those signal
   `GenericRecord` fallback).
3. Live test: `just dev` -> `just launch-cli` -> `dump-on`, then
   `connect`/`login`/`enter` against a real avatar. Every handshake frame
   prints structured detail (the Wave 331 replay path is what surfaces them).

## Plan-file bookkeeping

- Append Wave 332 entry to top of `plans/11-phase-k-ingame.md`.
- Append a Wave 332 note to the Phase K row in `plans/00-master.md`. That row
  is one giant single line (~154KB). Find the existing trailing-text + ` |`
  anchor near the end of the row, insert before the closing pipe.

## Commit

`Phase K Wave 332: structured decode for 15 sector-handshake opcodes`
plus the Co-Authored-By trailer. One commit for the whole wave is fine
(records are independent files; no risk of half-applied state).

Per CLAUDE.md "Wire format" section: cite the capture file +
representative frame number(s) for at least the variable-shape decoders
(0x0025, 0x0037, 0x0047). Pure fixed-struct opcodes can cite the
PacketStructures.h struct name instead.

## Hard rules (project CLAUDE.md, re-checked)

- **No em-dashes (`---`) in committed files**; use `--` or `-`. Wine console
  renders `-` as `â?` + garbage.
- **Don't name Net-7 client RE/decomp/disassembly** in committed files,
  commit messages, PRs, docs. Server source is fine. Conversation is fine.
- **Server integrity**: NEVER modify server to help the tool. If a field
  doesn't decode cleanly, the decoder adapts -- not the emitter.
- **No dead code**. No mocked / feature-flag-gated paths.
- **Be brutally honest** about bugs, scope, tradeoffs.

## Resume prompt to paste into the next session

```
Resume Phase K Wave 332 in /data/dev/enb-emulator. Read this handoff first:
plans/_handoff-wave-332.md. Then read plans/00-master.md and
plans/11-phase-k-ingame.md to confirm current phase state.

Then do the wave: implement 15 PacketRecord subclasses for the missing
handshake opcodes (full list and procedure in the handoff file), register
them, verify the build is clean, smoke-test via `just cli-replay`, update
both plan files, commit as Phase K Wave 332.

Start by spawning an Explore agent to survey the 15 server emitters in one
pass -- give it the exact opcode list from the handoff and ask for opcode +
server-emitter-file:line + struct-layout + variable-length-handling per
opcode. Then implement record classes in the order listed in the handoff
(easiest first). Don't ask for clarification on scope -- the handoff has it.
```
