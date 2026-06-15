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

### Z-1 `[x]` 0x00BD CTA_RESPONSE field@4 -- RESOLVED: NOT a divergence (server is correct)

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
- **RESOLVED 2026-06-15 -- the divergence was NEVER real; the earlier
  {13,14,15,17} "beacon / feature-broken" narrative is WITHDRAWN as false.** New
  cleartext proxy<->server captures of live-retail group play (cap7/cap8 =
  group+target+formation player1/player2; cap9/cap10 = group disband
  player1/player2) supply the MANY paired 0xBC->0xBD frames the single-sample
  analysis lacked, and they show field@4 is simply the **echoed GroupAction
  selector** from the request, 1:1:
  - 4 = Slot Back, 5 = Block, 6 = Pipe, 7 = Form Up, 8 = Leave Formation,
    9 = Break Formation, 12 = Request Target (and the other GroupAction codes).
  field@8 is a **Success** flag. There is no `(field@4 - 13)` switch and no
  {13,14,15,17} restriction in the live exchanges -- the retail server echoes the
  request's GroupAction back, which is EXACTLY what our `Player::HandleCTARequest`
  already does (`*((int32_t*)&CTAResponse[4]) = myCTARequest->Action`). The old
  0x0F/0x0E sample was a different action selector echoed back, not a beacon
  on/off toggle.
- **Conclusion**: our emitter is correct as-is. No server change, no CV entry.
  The 0x0F template default at :7733 is dead (always overwritten at :7746) but
  harmless; leave it. Category downgraded from A (wire-value mismatch) to
  not-a-bug.
- **CLI artifact note (2026-06-15)**: `CtaResponseRecord` was rewritten to drop the
  entire false {13,14,15,17}/"client rejects"/feature-broken narrative; it now
  decodes field@4 as the echoed GroupAction selector and field@8 as Success,
  pinned by tests against the cap7/cap9 frames. CtaRequest's ActionRecord enum was
  extended to the full GroupAction set to match.

### Z-2 `[~]` 0x005F AVATAR_EMOTE_RESPONSE -- (b) RESOLVED, (a) RE-SCOPED (wrong opcode in old note)

The earlier note conflated two code paths AND mis-identified the station-chat
opcode. A dedicated live-retail capture (cap5 = station local chat + 3 emotes,
cleartext proxy<->server) settles both. **The old "retail relays station chat as
0x5F byte@2=0x07" claim is WITHDRAWN -- it was a misread of capture_3.**

- **(b) Category A -- emote byte@2 hardcode: RESOLVED, server is CORRECT.** cap5
  carries three real emote responses on `0x005F` (server->client, GameID 0x40039930):
  `08 00 01 30 99 03 40 02 09 00 00 08 00 00 00`, then `...0A...`, then `...0D...`.
  byte@2 = **0x01 in all three** (the emote SELECTOR differs at byte@8: 0x09/0x0A/0x0D).
  Our `Player::HandleChatStream` emote path (`message[0]==0x02`,
  `server/src/PlayerConnection.cpp:10473`) sets `buffer[2] = 0x01` (:10481) and
  emits 0x5F via `SendToSector` -- byte-for-byte what retail does. No change.
- **(a) Category C -- station local chat is 0x001D, NOT 0x5F.** cap5's station local
  chat line is a single `0x001D` MESSAGE_STRING broadcast (server->client):
  `1A 00 02 "Guildsman StarstrukkTT: w\0"` -- u16 length, byte@2 = **0x02** (chat
  color), then the NUL-terminated "<name>: <text>" string. So retail relays station
  local chat as a `0x001D` MESSAGE_STRING with color 2, NOT as a 0x5F broadcast. Our
  `SendMessageString` (`PlayerConnection.cpp:11232`) already builds exactly this
  shape (`buffer[2] = color`, opcode `ENB_OPCODE_001D_MESSAGE_STRING`). The
  `message[0]==0x01` ("Chat in Stations") branch in HandleChatStream (:10494) is
  commented out "so local messages aren't sent twice" -- station chat is broadcast
  through the `ENB_OPCODE_0033_CLIENT_CHAT` path (:514), not HandleChatStream.
- **Remaining open question (a)**: does our server actually deliver station local
  chat as `0x001D` color 2 to the OTHER players in the station (not just echo to
  self)? cap5 only has one client, so it cannot show the cross-player broadcast.
  This needs a two-client LOCAL repro (two CLI/clients docked in the same station,
  one types local chat, assert the other receives `0x001D` color 2). NOT a wire
  emitter format bug -- the emitter shape is already correct -- it is a
  delivery/routing check.
- **CLI artifact note (2026-06-15)**: emote 0x5F decode pinned against the cap5
  three-emote frames (byte@2=0x01, selector@8); 0x001D MESSAGE_STRING decode pinned
  against the cap5 station-chat frame (color@2=0x02). Tests green.
- **Unblock criterion (a)**: two-client local station-chat delivery repro; if the
  other player does NOT receive the 0x001D, that is a routing fix (capture-cited),
  with a plans/29 CV entry for the real-client cross-player check.

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

### Z-5 `[~]` 0x0097 GALAXY_MAP -- PB-4 addressed via PROXY serve; server Type-5..9 emit still a gap

- **Category**: C (capability gap). **This is the root cause of PB-4 (in-game
  galaxy map shows nothing) -- see plans/41.**
- **PB-4 FIXED 2026-06-15 via path (b) (proxy serve), NOT path (a) (server emit).**
  `UDPClient::SendCachedGalaxyMap()` (`proxy/UDPProxyToClient_linux.cpp:643`) now
  reads `GalaxyMap.dat` and streams its `[len][0x0097][body]` records to the client
  on the `0x2010` DATA_FILE / `0x0097` request, so the in-game map populates from
  the prebuilt 305-record cache without the server emitting Type-5..9 at all.
  client.exe check = CV-MAP (plans/29). **The server-side Type-5..9 streaming below
  remains a separate, still-open gap** -- the proxy serve makes it non-urgent
  (the map renders), but a server that streams the live sub-types would let the map
  reflect runtime state rather than the static file. Lower priority now.
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
