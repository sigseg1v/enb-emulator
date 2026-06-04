# Phase AE - CLI full-field dump + REPL semantic event narration

Two operator-facing CLI tooling features, both PURE TOOLING (no server /
proxy / login-server wire changes -- governed by none of the server-integrity
gates, fully unblocked, no Phase K dependency):

1. **Full-field dump.** Every packet the CLI dumps should parse *all* its
   fields, driving the `???` undecoded-gap ranges toward zero per opcode.
   Today inbound game frames are ~all structured (107/145 + 7 codecs, plan
   31/AD), but some structured records still leave tail bytes unmarked, and
   a few realistically-received opcodes still fall through to the hex-only
   `GenericRecord`.

2. **REPL semantic event narration.** When the REPL sees a packet cross the
   wire, emit a one-line human-readable informational message for the events
   that matter to an operator, instead of only the raw dump. Flagship example
   (verbatim from the owner): a mob loading into scanner range while moving
   should print

   > Mob (L17, hostile, Mbonae Whatever) appeared in scanner range, d=...

   Owner-listed events to narrate: **entity appeared in scanner range**,
   **being invited to a group** (already done -- `AnnounceGroupInvite`),
   **leaving a group**, **warp interrupted**, **loading a new sector**,
   **taking damage**. Plus the natural counterpart **entity left scanner
   range**.

## Design

Narration is sourced ONLY from faithfully-pinned signals (no guessed wire
fields). Each owner event maps to a real server emission:

| Narrated event | Source signal (pinned) |
|---|---|
| Entity appeared in scanner range | World model: 0x0004 CREATE + 0x001B AUX (name/level/faction) + 0x0089 RELATIONSHIP (reaction). Announce-once when an entity first has a Name. |
| Entity left scanner range | 0x0007 REMOVE of a previously-announced entity |
| Taking damage | 0x0064 CLIENT_DAMAGE where `TargetId == self GameId` (`ClientDamageRecord` layout) |
| Invited to a group | 0x001E GROUP Flag 0x01 (existing `AnnounceGroupInvite`) |
| Left a group / system notices | 0x001D MESSAGE_STRING -- server sends "You have left the group" via `SendVaMessage`->`SendMessageString` (PlayerConnection.cpp:10979). Surfaced as a system line. |
| Warp progressed / ended | 0x009C WARP_INDEX: index>=0 = warping to leg N (`SendWarpIndex(m_WarpNavIndex)`), index -1 = warp ended (`TerminateWarp`->`SendWarpIndex(-1)`, PlayerClass.cpp:2590). |
| Loading a new sector | 0x003A SERVER_HANDOFF ToSectorID (BE @20, same field `CaptureHandoff` reads) |

Note we narrate "warp ended", not "warp interrupted": the server sends
`SendWarpIndex(-1)` for BOTH a boundary-interrupt and a normal arrival
(PlayerClass.cpp TerminateWarp is the single end path) -- the WARP_INDEX
signal does not distinguish them, so claiming "interrupted" would overstate
what the wire says.

### Mechanism

- `SectorWorld.Ingest` returns `IReadOnlyList<WorldEvent>` (was void). A
  `WorldEvent { Kind, GameId }` is emitted for Appeared (first Name on an
  entity, `Tracked.Announced` latch) and Departed (REMOVE of an announced
  entity). Distance/level/reaction are pulled from the live model at format
  time, so late-arriving aux/relationship frames are reflected.
- `EventNarrator` consumes `(Packet, worldEvents, selfGameId)` and formats
  the line; world events use `SectorWorld.NearestTo`/`ReactionName`/`TypeName`,
  packet-derived events parse the pinned record fields. Output goes above the
  live prompt via `LivePrompt.TryWriteLineAbove`, same as chat echo.
- Wired into `SessionContext.OnPacketReceived` + `ReplayInboundFrame` +
  the SectorUdp path, behind a `NarrateEnabled` flag (default true), toggled
  by `narrate-on` / `narrate-off` REPL commands.

## Waves

- [x] **AE-W1: narration spine.** `WorldEvent`/`WorldEventKind`,
  `Tracked.Announced`, `Ingest` returns events, `EventNarrator` formatting
  the flagship "appeared in scanner range" + "left scanner range" lines,
  wired behind `NarrateEnabled`. Unit tests mirroring `SectorWorldTests`.
- [x] **AE-W2: opcode-derived events.** Damage-taken (0x0064), system message
  (0x001D, covers leave-group), warp progressed/ended (0x009C), loading
  sector (0x003A). Unit tests.
- [x] **AE-W3: narrate-on/off REPL commands** + help text.
- [x] **AE-W4: full-field dump audit.** Done -- the audit shows the
  dump already parses all fields of every frame the server actually sends:
  - **Emit-diff is empty.** Cross-referencing every `SendOpcode(ENB_OPCODE_00xx..)`
    literal call site in `server/` + `login-server/` (79 distinct client-wire
    opcodes) against the 109 opcodes registered in `PacketRecordRegistry`
    leaves ZERO server-emitted client-wire opcodes without a structured record.
    So nothing the server emits inbound falls to the hex-only `GenericRecord`.
  - **No record leaves fields unmarked.** Only `PacketRecord` (the base) and
    `GenericRecord` reference the legacy non-offset `Field*`/`FieldHex*`
    emitters; every concrete record uses the byte-marking `F*` family, so
    `WriteGaps` reports `???` only for genuinely-variable trailing data, never
    for a field the record simply forgot to decode.
  - The opcodes still on `GenericRecord` are exactly the outbound-only request
    builders the CLI emits (never receives) and the 6 server-never-emitted
    `KnownUnimplemented` opcodes. Giving them inbound records would be dead
    code (no emitter to exercise it) -- forbidden by the no-dead-code rule and
    pointless per plan 31/AD Q2. They are intentionally left record-less.
  - Conclusion: "parse all fields" holds for the realistic inbound stream
    today. If a future live capture surfaces a variable-tail `???` in a
    specific record, close it then against that capture -- but there is no
    standing gap to chase speculatively.

## Rules that bind this phase

- TOOLING-ONLY. No `server/`, `proxy/`, or `login-server/` changes. The
  narrator READS pinned signals; it never asks the server to emit anything
  new, so none of the server-integrity / CV-gate machinery applies.
- Narration must not overstate the wire (the "warp ended" vs "interrupted"
  point above). If a signal is ambiguous, narrate what is actually pinned.
- Best-effort: a malformed frame must never kill the drain (every handler
  is wrapped, matching the existing `Ingest`/`EchoChat` contract).

## Status

Complete -- 2026-06-04. All four waves landed. Narration (W1-W3) shipped:
scanner-contact appeared/departed + damage/warp/sector-load/system-message
notices behind `narrate-on`/`narrate-off`, 16 new tests, 701-test suite green.
Dump-all-fields (W4) audited to complete: the emit-diff is empty and no record
leaves fields unmarked, so every frame the server actually sends already parses
all its fields; the only `GenericRecord` fallbacks are outbound-only/never-
emitted opcodes that must stay record-less to avoid dead code.
