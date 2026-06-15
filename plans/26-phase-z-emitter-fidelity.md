# Phase Z -- Server-emitter fidelity (retail-vs-emulator wire divergences)

## Why this phase exists

The CLI Tier-2 decode work (Phase S) grounds every decoder in the server
emitter/parser AND pins it to verbatim retail capture bytes. That process
occasionally surfaces a place where **our server emits bytes that disagree with
what the retail server put on the wire** for the same opcode. Those findings were
previously scattered across record doc-comments and the decisions log and were at
risk of being lost. This phase is the single durable register for them, plus the
confirm-and-resolve workflow.

This is NOT a license to "fix" the server casually. It is the opposite: a place to
record a suspected divergence, gather enough primary-source evidence to either
**confirm** it (and only then resolve), or **explain it away**, so the suspicion
doesn't rot into folklore.

## The resolution rule (do not skip)

Per CLAUDE.md "Server integrity rules":

- A server change that makes us emit bytes the real server did NOT emit is
  forbidden. A change that **tightens** us toward what the real server DID emit is
  always welcome -- but only with a primary-source citation (capture file + frame
  number, decompiled function, first-hand doc) in the commit message.
- "Near-irrefutable confidence" is the bar (the user's standing instruction). One
  capture frame proves the value *in that frame*; it does NOT prove the field is a
  constant or prove the general rule. To resolve a value divergence you need either
  (a) the same field agreeing across multiple independent frames/captures such that
  the rule is unambiguous, or (b) a decomp/doc showing the formula.
- If you cannot meet the bar: the server stays as-is, the decoder pins the RETAIL
  bytes (source of truth), and the divergence is documented here. The CLI decoder
  describes what the real client saw, never what our server happens to emit.

## Workflow for a new finding

1. **Capture the observation**: opcode, field offset, retail value(s) with capture
   frame number(s), and our emitter's value with `file:line`.
2. **Classify** (see categories below).
3. **Confirm**: tally the field across ALL frames of that opcode in every available
   capture. Does retail agree on a value/rule? Is our path even the same code path
   the capture exercised?
4. **Resolve or defer**: if confirmed and the fix tightens fidelity, fix the
   emitter with a primary-source citation and add a capture-pinned regression test.
   Otherwise record the sharp unblock criterion and defer.

## Classification categories

- **A -- wire-VALUE mismatch**: same wire shape, our emitter writes a different
  byte/value than retail at a specific field. The actionable category.
- **B -- id-allocation / value-domain difference**: both well-formed; the values
  live in different domains (e.g. CharacterID vs a sector-local id). Usually not a
  wire bug if our server is internally consistent.
- **C -- capability gap**: our server emits a strict subset of what retail emitted
  (missing opcodes or sub-types). Feature work, not a format fix.
- **D -- deliberate, documented divergence**: an intentional design choice
  (e.g. our ASCII ticket vs retail opaque binary). Accept.

---

## Register

Status legend: `[!]` confirmed-but-blocked (cannot resolve at the bar yet),
`[~]` accepted as-is (cosmetic / by-design), `[x]` resolved, `[gap]` capability gap.

### Z-1 `[!]` 0x00BD CTA_RESPONSE field@4 -- emitter writes request Action; retail varies

- **Category**: A (wire-value mismatch).
- **Our emitter**: `Player::HandleCTARequest` (server/src/PlayerConnection.cpp:7733)
  builds a `CTAResponse[]` template with field@4 hardcoded `0x0F` (commented
  "RequestType"), then at :7746 OVERWRITES it: `*((int32_t*)&CTAResponse[4]) =
  myCTARequest->Action`. The template comment literally cites "capture_3 packet#
  21495" -- the author saw 0x0F in that one frame, templated it, then a later edit
  clobbered it with the client's Action.
- **Retail observation (capture_3)**: field@4 is NOT constant. Two 0xBD frames:
  `88 7C 16 00 0F 00 00 00 01` (#21495, field@4 = 0x0F = 15) and a later
  `88 7C 16 00 0E 00 00 00 01` (field@4 = 0x0E = 14). SourceID 1473672 in both.
- **Why it's a divergence**: the client's `Action` (a GroupAction selector, e.g. 5
  in the paired request) is a different value domain than retail's field@4
  (server-side response/type code, observed 14/15). Our server emits neither the
  template's 0x0F nor a value matching retail -- it emits the raw request Action.
- **Confirm status**: divergence is real and confirmed. **Client semantics now
  RESOLVED (2026-06-14, behavioural analysis of the retail client's 0x00BD handler).**
  The client switches on `(field@4 - 13)` and recognises ONLY field@4 in
  {13, 14, 15, 17}:
  - 13 -> acknowledge / no-op (no visual change)
  - 14 -> clear the Call-To-Arms highlight on the LOCAL player (beacon OFF)
  - 15 -> set the Call-To-Arms highlight on the LOCAL player (beacon ON)
  - 17 -> set the Call-To-Arms highlight on the avatar named by SourceID (field@0)
  Any other value lands in the client's default arm: it logs an error and renders
  NO effect. The retail-observed 0x0F/0x0E are therefore beacon ON / beacon OFF.
- **Severity upgrade**: our server echoes the request `Action` (the GroupAction
  selector, 4..12, or 0 for the no-op arm) into field@4. Every one of those is
  OUTSIDE {13,14,15,17}, so the retail client REJECTS our 0x00BD and the
  Call-To-Arms beacon never fires against our server -- this is a live feature
  break, not a cosmetic value mismatch.
- **Still blocked on**: deriving the request->response mapping from ONE sample.
  The paired capture is already decoded -- request `cta_request_groupaction5`
  (Action=5) pairs with response `cta_response_requesttype0f` (field@4=0x0F=15),
  same SourceID 1473672. So we know retail mapped (Action 5) -> (beacon ON, 15)
  in that one exchange, plus an UNPAIRED later 0x0E=14 (beacon OFF). That is not
  enough to choose between two hypotheses: (a) field@4 is a pure function of the
  request Action, or (b) it is a STATEFUL toggle (first press ON=15, next OFF=14)
  independent of Action. Worse, our server routes 0x00BC into GroupAction
  (server/src/GroupManager.cpp:1233), where Action=5 means "Block" FORMATION --
  a different domain from the beacon, so retail's 0x00BC handler was likely not
  our formation path at all. Pinning the formula needs MORE paired 0xBC->0xBD
  frames (ideally repeated presses to see the toggle). Until then the server stays
  as-is -- emitting a guessed beacon code blind risks the wrong on/off state. When
  the server change lands, file a CV entry for the real-client beacon check.
- **CLI artifact note**: CtaResponseRecord now decodes field@4's recognised meaning
  (13/14/15/17 -> ack/off/on/on-by-id, else "client rejects") and its class doc
  records the full client semantics (2026-06-14).

### Z-2 `[!]` 0x005F AVATAR_EMOTE_RESPONSE -- two distinct issues, neither resolvable yet

This is the finding that most needed confirming -- the earlier one-line note
("retail 0x07 vs our 0x01 at byte@2") conflated two different code paths.

- **(a) Category C -- station-chat relay missing.** `Player::HandleChatStream`
  (server/src/PlayerConnection.cpp:10230) sends a 0x5F broadcast ONLY when the
  request's `message[0] == 0x02` (emote). The `message[0] == 0x01` branch
  ("Chat in Stations", :10257) is commented out and does nothing. BUT both 0x5F
  frames in capture_3 (#1211 and the #199841-region frame) are responses to 0x5E
  requests whose `message[0] == 0x01` (station chat) -- i.e. retail relays station
  chat as a 0x5F broadcast and our server does not. Missing feature.
- **(b) Category A -- emote-path byte@2 hardcode, UNVALIDATED.** For the emote path
  (`message[0] == 0x02`) the emitter sets `buffer[2] = 0x01` (:10244). We have NO
  capture of an actual emote (`message[0] == 0x02`) to compare, so we cannot say
  0x01 is wrong. The `byte@2 = 0x07` seen in the captures belongs to path (a)
  (station chat), NOT this emote path.
- **Retail observation (capture_3)**: both 0x5F frames carry byte@2 = 0x07, ChatSize
  + message copied from the request, GameID 11193582. Their 0x5E requests have
  `message[0] == 0x01` (e.g. #1209: `EE CC AA 00 01 0D 00 01 48 05 00 2F 77 68 6F
  00 ...` -> the "/who" station-chat line).
- **Confirm status**: both sub-findings confirmed; neither resolvable. (a) is a
  feature add (relaying station chat) that touches live chat broadcast -- high
  blast radius, needs its own design + capture-pinned test. (b) cannot be judged
  without a `message[0] == 0x02` emote capture.
- **Unblock criterion**: (a) design the station-chat relay against the two captured
  0x5F frames as the byte target; (b) obtain an emote-path (`message[0] == 0x02`)
  capture to validate or correct the 0x01.

### Z-3 `[!]` 0x0005 START StartID -- CharacterID vs retail sector-local id

- **Category**: B (id-allocation), bordering on a real preservation concern.
- **Our emitter**: `SendStart(player->CharacterID())`
  (server/src/PlayerConnection.cpp:1079, from SectorManager.cpp:387/551).
- **Retail observation (capture_1)**: StartID is a small sector-ASSIGNED id that
  changes on every sector entry (10069, 8865, 3126, 8873, ...) and carries no
  PLAYER_TAG bits; the same value leads that sector's 0x97/0xA3/0x4E frames.
  CharacterID is constant per character and tagged differently.
- **Confirm status**: wire shape identical (bare LE int32); the id *domain* differs.
  Our server is internally consistent (keys avatar-scoped packets to CharacterID
  throughout the sector), so the client gets a coherent self-id -- not obviously a
  bug. Open question: does the retail client special-case the StartID's tag bits?
- **Unblock criterion**: a live Win32-client trace against our server showing
  whether emitting CharacterID (vs a sector-local id) causes any avatar-scoping or
  rendering anomaly. High risk to change (touches the avatar id scheme); do NOT
  change without that evidence. Low priority.

### Z-4 `[~]` 0x0034 CLIENT_SET_TIME ServerSent -- equal vs retail +1 tick

- **Category**: A, but cosmetic.
- **Our emitter**: `SendClientSetTime` sets ServerSent = ServerReceived (zero
  measured processing latency).
- **Retail observation**: ServerSent = ServerReceived + 1 tick.
- **Resolution**: ACCEPT as-is. Both are well-formed (the only invariant is
  ServerSent >= ServerReceived); the difference is a 1-tick latency cosmetic with no
  client-visible effect. Emitting +1 would be arbitrary, not a fidelity improvement,
  so it does not meet the bar to change.

### Z-5 `[gap]` 0x0097 GALAXY_MAP -- our server emits only Type 4 (ties to PB-4)

- **Category**: C (capability gap). **This is the likely root cause of PB-4
  (in-game galaxy map shows nothing) -- see plans/41.**
- Retail emits map/nav-detail sub-types 3/5/6/7/8/9 (named star systems "Aragoth"
  etc., nav points); our server (`SendGalaxyMap`) emits only Type 4 ("you are here").
- **Client-side rendering requirement RESOLVED (2026-06-14, behavioural analysis of
  the retail client's 0x0097 parser/renderer)**: the client demuxes on the leading
  record-type byte. The renderable map-node collections (systems, sectors, gates,
  links) are populated EXCLUSIVELY by sub-types 5/6/7/8/9 (and 0xb). Type 2 only
  refreshes already-cached nodes; Type 3 stashes a raw blob; **Type 4 adds NO
  renderable node -- it only sets the "you are here" label strings and the array
  capacity counter.** So a server that emits only Type 4 hands the client an empty
  node set and the map draws nothing but the "you are here" marker. That matches
  PB-4 exactly.
- **Per-record wire layout -- partially known, MUST be pinned from capture, NOT
  guessed.** The behavioural read of the client deserializers gave a rough read
  ORDER (id(s), then a NUL-string name, then float coordinate block(s)), but it
  was derived from in-object field offsets and does NOT cleanly match the actual
  bytes -- it is unreliable for the trailing fields. The in-repo capture fixtures
  ARE the source of truth and already exist:
  - Type 5 `galaxymap_system_aragoth` (63B): `Type=5, Size=55, id=3, id2=3,
    "Aragoth\0"`, then two float coord triples `(0,0.3,1.0)`/`(0,-6,0)`, then a
    trailing `1.0, u32 3, ...` block that does NOT fit a clean "two u32 flags"
    tail -- so the exact trailing layout is still open.
  - Type 9 `galaxymap_sector_earth` (64B): `Type=9, Size=56, id=108, id=1060,
    id=1015, "Earth\0"`, then `u32 6` + a coordinate block. (The behavioural read
    wrongly predicted "3 ids only, no name/coords" for Type 9 -- the capture
    disproves it. Trust the capture.)
  The CLI decoder (GalaxyMapRecord) therefore decodes only the header + the
  embedded name for these sub-types and leaves the numeric tail in the hex dump
  until a careful per-byte pin against the Aragoth/Earth fixtures is done. That
  per-byte pin is the next CLI step; it is finicky and was deliberately NOT
  guessed here.
- **Alternative client path**: the retail proxy also serves a prebuilt
  `..\database\GalaxyMap.dat` to the client on the 0x2010/0x2011 control band
  (the file-stream-then-finalize path), independent of server Type-5..9 streaming.
  PB-4 triage must determine which path our stack is (not) feeding -- whether our
  proxy ships a GalaxyMap.dat and/or our server should stream the record sub-types.
- **Unblock criterion**: byte-pin the Type-5/9 records against the EXISTING
  capture fixtures (`galaxymap_system_aragoth`, `galaxymap_sector_earth` in
  capture3-records.txt; plus the Type-4 `live_galaxymap_0097.hex`), extend the CLI
  decode past the name, THEN implement the server emit (or proxy GalaxyMap.dat
  serve) and file a CV entry. The captures -- not the behavioural read -- pin the
  byte offsets before any wire change.
- The CLI decoder (GalaxyMapRecord) currently decodes the Type-4 layout fully and
  the retail sub-types' header + embedded name (numeric fields left unmodeled until
  a capture pins them).

### Z-6 `[gap]` 0x0021 PUSH_MESSAGE -- our server never emits it

- **Category**: C (capability gap). Our server emits only 0x22; 0x21 has the
  identical wire shape but is never sent. Tracked, not a format bug.

### Z-7 `[~]` 0x003A SERVER_HANDOFF ticket / variable_data -- ASCII vs opaque binary

- **Category**: D (deliberate, documented). Retail filled the ticket/variable_data
  with opaque random bytes; our login issues a printable `username-rand` ASCII
  ticket (login-server/Net7SSL/LinuxAuth.cpp BuildTicketLocked; decisions-log
  2026-05-23 entry on the ticket re-scope). There is no signature/MAC in either;
  the format choice is ours by design. ACCEPT.

### Z-R1 `[x]` 0x0036 SERVER_REDIRECT sector_id -- LE-vs-BE (RESOLVED, recorded for history)

- Was flagged in the decisions log (Phase T close-out, 2026-05-24) as an unresolved
  LE-vs-BE divergence. RESOLVED: the proxy assigns `redirect.sector_id = sector_id`
  in host order (LE on the x86 wire) -- see proxy/ClientToMasterServer.cpp:177-190,
  which cites capture_1.rar frames 222/656/1062 and capture_2.rar frame 222 (all
  show sector_id LE-on-wire, e.g. `69 29 00 00` for Aragoth 10601). A prior `ntohl`
  here byte-swapped to BE and crashed the Win32 client on sector handoff. The codec
  (ServerRedirectRecord) reads LE to match. Closed; listed so the old flag isn't
  re-opened by accident.

### Z-8 `[~]` 0x000B OBJECT_TO_OBJECT_EFFECT -- two server emitters disagree above bit 0x04 (latent)

- **Category**: A (wire-value mismatch), but LATENT -- not currently reachable.
- **Finding** (surfaced 2026-06-03 reading server/proxy/CLI side by side for
  Phase AB §5): the 0x000B opcode has TWO server serializers and their bit-gated
  field order DIVERGES above bit 0x04.
  - `Object::SendObjectToObjectEffectRL` (server/src/ObjectClass.cpp ~850-928,
    range-list path): 0x08 OutsideTargetRadius(as long, 4B), 0x10/0x20 nothing,
    0x40 TargetOffset[3], 0x80 Scale, 0x100/0x200/0x400 HSVShift[0..2],
    0x800 Speedup. This is the AUTHORITATIVE layout -- the CLI decoder
    `ObjectToObjectEffectRecord` validated it byte-exact vs the retail capture
    (bitmask 0x0807 = bits 0,1,2 + Speedup@0x800 consumes to the last byte; the
    Speedup-at-0x800 bit only exists in THIS layout).
  - `Player::SendObjectToObjectEffect` (server/src/PlayerConnection.cpp:1394,
    single-player path): 0x08 TargetOffset, 0x10 OutsideTargetRadius, 0x40 Scale,
    0x80 HSVShift[3], 0x100 Speedup -- WRONG above 0x04. The function's own
    comment (:1441) flags "packetstructures.h is wrong... work out correct
    structure", confirming the author knew this path was unverified.
- **Why latent / why NOT fixed inline**: both callers of the single-player twin
  set Bitmask 0x07 only -- `Player::ActivateProspectBeam` (0x03/0x07) and
  `MOBClass.cpp:1436` (0x07). Bits 0x01/0x02/0x04 (EffectID/TimeStamp/Duration)
  are IDENTICAL in both emitters, so no live code path exercises the divergent
  region. There is no functional bug to fix today, and aligning the twin is a
  server change that, while capture-justified (Z-8 has the primary-source proof
  the escape hatch requires), is high-risk for zero current benefit. Defer until
  a caller actually needs bits > 0x04 through the single-player path.
- **Done in Phase AB**: corrected the proxy fabrication comment to cite the
  authoritative `Object::SendObjectToObjectEffectRL` (not the divergent twin),
  and reconciled the stale per-field bitmask annotations in
  `common/include/net7/PacketStructures.h struct ObjectToObjectEffect` to the
  validated layout (comment-only; the struct is field-addressed, never memcpy'd
  to the wire on this path). The shipped 0x2012->0x000B beam uses only the
  agreeing bits, so it is correct regardless.

---

## Open work

- [ ] Z-1: find the CTA/GroupAction response-type enum; pair more 0xBC->0xBD frames.
- [ ] Z-2(a): design station-chat (message[0]==0x01) 0x5F relay, byte-targeted at
      capture_3 #1211; capture-pinned test before any server change.
- [ ] Z-2(b): obtain a message[0]==0x02 emote capture to validate buffer[2]=0x01.
- [ ] Z-3: live-client trace to decide whether StartID id-domain matters. Low pri.
- [ ] Z-5 / Z-6: galaxy-map sub-types + 0x21 push -- feature work, not this phase.
- [ ] Z-8: align Player::SendObjectToObjectEffect to the authoritative RL layout
      IF/WHEN a single-player-path caller needs bits > 0x04 (latent today).
- [x] Z-4, Z-7: accepted as-is. Z-R1: resolved.

## Notes

- New findings append a `Z-N` entry here. If the CLI decode work surfaces a
  divergence, the record's doc-comment should reference `[[Phase Z]]` / this file so
  the two stay linked.
- Nothing in this phase has loosened the server. Every entry either keeps the server
  as-is (pending evidence) or records a fidelity-tightening fix that already shipped
  with a primary-source citation.
