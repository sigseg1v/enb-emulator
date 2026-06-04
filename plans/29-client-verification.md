# Phase 29 -- Real-client (client.exe) verification checklist

## What this is

This is the running checklist mandated by the **Server modification rules** in
`CLAUDE.md` (clause C). The CLI client proves a packet's *format* byte-for-byte;
only the real Win32 client proves the *game still works*. Every server/proxy
wire-behaviour change that the CLI alone cannot fully validate gets an entry
here.

These checks are **asynchronous and non-blocking**: implementation does not
wait on them. Keep building. The project owner runs the real client whenever
convenient and flips the entry's status. A change is only considered DONE once
its entry here is confirmed.

## How to use it

- When you land a server/proxy wire change, append an entry below with a fresh
  `CV-NN` id, what changed, the exact in-game thing to look for, and how to set
  it up. Cite the entry id (`CV-NN`) in the commit message (rule's Process step 3).
- Status legend: `[ ]` pending owner verification, `[~]` owner testing,
  `[x]` confirmed working with real client, `[!]` confirmed BROKEN with real
  client (then it is a live defect -- open a fix, do not close).
- Keep newest at the bottom. Never delete a confirmed entry; it is the audit
  trail that the wire change was validated against client.exe, not just the CLI.

## How to run the real client

`just play-local` launches the WINE client against the local docker stack.
After a C++ change you OWN the rebuild: `just rebuild [svc]` (or
`just rebuild-cli <UNIT>` for the CLI/unit proxy) BEFORE relaunching, or the
container keeps the old binary (the stale-image trap -- see CLAUDE.md "Wire
format & byte order", Trap 2).

---

## Entries

### [ ] CV-01 -- Static sector content renders (navs / stations / gates / decos)

- **Change**: proxy now expands `0x2018` STATIC_OBJECT_CREATE (compact server
  fabrication band) into the client-facing `0x0004` CREATE + `0x0089`
  RELATIONSHIP + `0x0040` CONSTANT_POSITIONAL_UPDATE + `0x001B` AUX-name +
  `0x0099` NAVIGATION sequence, instead of consuming+dropping it.
  (`proxy/UDPProxyToClient_linux.cpp`, `UDPClient::CreateObject`.)
- **Primary source**: `proxy/local-debug/net7-live-2026-06-02-login-undock-clicknavs-warp-logout.pcap`.
  Endpoint check (2026-06-03): this is a capture of the **real net7 retail
  server** (`216.219.87.147`) talking to a player-side proxy
  (`192.168.0.150`), on the cleartext server<->proxy UDP leg -- i.e. the
  canonical primary source CLAUDE.md ranks highest, NOT a capture of our own
  stack. The 51x `0x2018` frames come from the retail server's sector port
  (`:3573`).
- **Our server emits the same opcode (verified in code, 2026-06-03)**: not just
  the retail server -- OUR server also emits compact `0x2018`/`0x2019` on sector
  entry, so the proxy expander is live (not dead code). Path:
  `ObjectManager::SendAllNavs` (ObjectManager.cpp:416) ->
  `StaticMap::SendObject` (NavTypeClass.cpp:327, calls `SendObjectFull` :353)
  for navs/gates/stations, and `Resource::SendObject` (ResourceClass.cpp:720,
  :724) for asteroids. `Player::SendObjectFull` (PlayerConnection.cpp:1113) is
  the sole `0x2018`/`0x2019` emitter and is reached only via those two.
  (Planets/decos take the base `Object::SendObject` at ObjectClass.cpp:670 =
  plain `0x04`+`0x89`, already forwarded -- see CV-03.)
- **Compact-body layout agreement (verified statically)**: the proxy's
  `CreateObject` parser reads the exact field offsets the server's
  `StaticMap::FormStaticPacket` (NavTypeClass.cpp:287) writes -- GameID@0,
  CreateType@4, BaseAsset@5, Scale@7, HSV@11/15/19, Relationship@23,
  PosType@24, Pos@25/29/33, Orient@37-49, Signature@53, sig_flags@57, Name@58
  (length-prefixed). So the parse side is provably correct; the only
  reconstructed-and-unverified part is the EXPANSION OUTPUT (the client-facing
  `0x04`/`0x89`/`0x40`/`0x1b`/`0x99` bytes), because the client<->proxy leg is
  encrypted and absent from any capture. That output is what this CV entry must
  confirm against the real client.
- **CLI proof**: `StaticObjectCreateRecord` + fabrication byte-pin test. NOTE:
  the byte-pin locks what the proxy EMITS (regression guard), not that the
  emitted format is what the client wants -- only client.exe proves that.
- **What to look for in client.exe**: zone into **Luna**. The galaxy/sector map
  and the in-space HUD should show navs, the station(s), the gate(s) and any
  decorative objects -- not an empty sector. Click a nav: it should be
  selectable and show its name.
- **Setup**: `just rebuild proxy && just play-local`, log in, fly/zone to Luna.

### [ ] CV-02 -- Asteroid fields render (resources)

- **Change**: proxy now expands `0x2019` RESOURCE_OBJECT_CREATE into `0x0004`
  CREATE (Type=38) + `0x0040` position + `0x001B` resource-name, instead of
  dropping it. (`UDPClient::CreateResource`.)
- **Primary source**: same capture, 34x `0x2019` frames.
- **CLI proof**: `ResourceObjectCreateRecord` + fabrication byte-pin test.
- **What to look for**: asteroids visible in-space in a sector that has a
  resource field; targetable; mining beam can lock one.

### [ ] CV-03 -- Luna planet renders

- **Change**: none yet to the planet path (Planet rides the already-forwarded
  `0x0004`/`0x0089`/`0x003F`/`0x001B`/`0x0099` sequence, not the dropped band).
  This entry is to confirm that once CV-01 lands, the **Luna planet body**
  itself is visible. If it is still missing with navs present, that isolates a
  separate server-side planet/sector issue (do NOT change the server for it
  without primary-source proof per the rules).
- **What to look for**: the Luna planet sphere visible in-space and on the map.

### [ ] CV-04 -- Player ship does not vanish after idle (keepalive)

- **Root cause (CONFIRMED, primary source = live server log)**: the server's
  2-minute idle reaper drops any in-game player whose `LastAccessTime` is not
  refreshed within 120s (`server/src/PlayerManager.cpp` `SendUDPOpcodes` /
  `SendUDPPlayerOpcodes`: `LastAccessTime + 2*60000 < now` ->
  `DropPlayerFromGalaxy` -> `DropPlayerFromSector`, which removes the ship from
  every other client's view -- i.e. "the ship disappeared"). The running
  server's own log shows it: player `clienta` fully logged in at 15:17:58 and
  was `Removed from sector Luna` + `Removed from Galaxy` at 15:20:09 -- exactly
  2m11s later, with NO `LastAccessTime` refresh in between. (This overturns the
  earlier "not a timeout" guess.)
- **Why our proxy never refreshed it**: `UDPProxyMVAS.cpp` (the upstream Win32
  file that scrapes ship position and streams the MVAS keepalive) was never
  ported -- it is absent from the repo, and `MVASThread()` is declared but
  defined/started nowhere. So neither proxy build sent ANY `0x3005`/`0x1004` to
  the server's MVAS port; an idle in-space avatar behind the proxy was
  guaranteed to be reaped at 2 min.
- **Change (implemented)**: added a minimal MVAS idle keepalive to the proxy --
  `UDPClient::MVASKeepaliveThread` / `SendServerKeepalive`
  (`proxy/UDPClient_linux.cpp`, compiled into BOTH the Linux docker build and
  the Win32-PE/WINE build). Started once per session in `FixedClientComm()`
  (sector plane, post-login), it sends a bare 12-byte `0x3005`
  PLAYER_COMMS_ALIVE to the server's MVAS port (`MVAS_LOGIN_PORT`/3806) every
  30s carrying the live GameID (`m_PlayerID`). The server's
  `UDP_MVAS.cpp HandleKeepCommsAlive` resolves `GetPlayer(player_id)` and
  refreshes `LastAccessTime`.
- **Port correction (2026-06-03, found in review)**: the first cut sent via
  `UDP_Send(0, ...)`, which on a connected SOCK_DGRAM routes to the connected
  peer. But `g_ServerMgr->m_UDPClient` (the object the keepalive thread runs
  on) is connect()'d to `UDP_MASTER_SERVER_PORT`/**3808**, not 3806 -- the
  "connected MVAS peer" assumption was wrong. The master plane has no
  comms-alive handler, so the keepalive refreshed nothing and the ship would
  still vanish. Fixed to `UDP_Send(MVAS_LOGIN_PORT, ...)` (explicit
  `sendto(server:3806)`; Linux permits sendto to a non-peer port on a
  connected UDP socket -- verified empirically).
- **Primary source**: cleartext proxy<->server capture
  `proxy/local-debug/net7-live-2026-06-02-login-undock-clicknavs-warp-logout.pcap`
  (re-parsed 2026-06-03). The real proxy emits 0x3005 on TWO synchronized
  ~30s streams: (1) a bare keepalive from its MVAS socket (sport 43471) to
  udp/**3806** -- first frame `0c 00 05 30 2a 99 03 40 01 00 00 00` (size=12,
  opcode=0x3005, player_id=0x4003992a, dedicated low sequence 1,2,2,2); and
  (2) a 0x3005 from its main socket (sport 40208) to the active **sector**
  server port (3636 then 3573 after a warp), sharing that socket's
  incrementing packet sequence. 0x1004 position (x96) also targets 3806. We
  replicate stream (1) only -- it is the server's authoritative refresh path
  (`HandleKeepCommsAlive` on 3806). Stream (2) (sector-port 0x3005) is
  deliberately NOT replicated: it would have to ride the sector socket's live
  packet-sequence counter (risk of breaking sector opcode ordering) for no
  extra reaper benefit, since (1) already refreshes the same per-Player
  `LastAccessTime`. (The pre-existing dead `SendCommsAlive()` ->
  `m_ClientPort` is the unwired stub for stream (2).)
- **Server-side runtime proof (2026-06-03)**: sent a raw 0x3005 with a bogus
  player_id (0xDEADBEEF) to the live server's udp/3806; it replied
  `0x100A MVAS_TERMINATE` echoing the id (`14 00 0a 10 ... ef be ad de ...`),
  proving 3806 is bound and dispatches 0x3005 -> `HandleKeepCommsAlive`. A
  VALID GameID takes the other branch (`SetLastAccessTime`, no reply), so the
  proxy's keepalive refreshes the timer. (This also confirms the
  "MVAS receiver was never started" server fix, commit bd3dc407, is live.)
- **CLI proof**: `MvasClientTests.BuildCommsAlive_IsHeaderOnly_ByteExact` and
  `BuildCommsAlive_MatchesRetailCaptureFrame` byte-pin the identical 12-byte
  frame the proxy emits.
- **CLI/play-cli half (already covered)**: the CLI's `MvasClient` dials the
  server's 3806 directly (not through the proxy), so play-cli is fixed by the
  CLI's own `StartCommsAlive` (0x3005 every 30s) plus its pre-existing
  `StartKeepalive` (0x0044 REQUEST_TIME every 30s, which also refreshes
  `LastAccessTime` via the sector dispatcher tail). No double-send vs the proxy.
- **What to look for (play-local / real client)**: log in, undock, sit idle in
  space for >2 minutes without moving or touching anything. The ship must NOT
  disappear and the session must NOT drop. Cross-check the server log: there
  should be NO `Player '<name>' Removed from Galaxy` line while you sit idle.
- **Setup**: `just rebuild proxy && just play-local`, undock, idle 3+ min.

### [ ] CV-05 -- Fresh character starts docked in the home station

- **Change**: `server/src/StaticData.h` `StartSector[]` reverted to the station
  IDs (`10151`, `10201`, `10251`, `10551`, `10401`, `10521`, `10361`, `10371`,
  `10301`) it held before commit `80e87f82`. A station id is its containing
  space sector's `sectors.sector_id * 10 + 1` and is `> 9999`, so
  `SectorManager::HandleSectorLogin` takes the `StationLogin` branch (gated on
  `m_SectorID > 9999`), which calls `SetInSpace(false)`. Commit `80e87f82` had
  changed these to the bare space sector IDs (`< 10000`), spawning new
  characters in open space instead of docked.
- **Primary source**: retail new-character behaviour -- a freshly created
  character begins docked inside the home station for its race/profession
  (e.g. a Terran Warrior starts in Luna Station, not Luna space). This entry
  reverts a divergence introduced by `80e87f82`; the prior committed state is
  the fidelity baseline.
- **What to look for (play-local / real client)**: create a brand-new
  character of each race/profession (or at least a Terran Warrior). On first
  login you must appear DOCKED in the home station UI (station services menu),
  NOT floating in space. Undocking then puts you in the correct home space
  sector.
- **Setup**: `just rebuild server && just play-local`, create a fresh
  character, observe the first-login state.

### [ ] CV-06 -- Real client gate-jump sends 0x002C ACTION 18 then 19

- **Change**: NONE server-side. This is a tooling-consumer verification only:
  the new CLI `gate <gid>` command (commit `fbe16a5e`) sends `0x002C ACTION`
  `Action=18` (gate button, Target=stargate gid) then, ~6s later, `Action=19`
  (finish gate sequence). The CLI byte-pins the ActionPacket layout (16 bytes,
  4x int32 LE per `PacketStructures.h:546`) and the 18/19 values are taken from
  the SERVER handler (`PlayerConnection.cpp:3923` case 18 -> `SectorManager::Gate`
  stores StargateDestination; `:3965` case 19 -> `SectorServerHandoff`).
- **Gap being verified**: the 18/19 sequence is confirmed against the *server*
  side only. I do NOT have a captured *client*->server gate jump pinning that
  the retail `client.exe` actually emits `0x002C` with Action 18 then 19 (the
  committed captures cover login/undock/navs/warp/logout, not a gate jump). So
  the CLI proves our server ACCEPTS the sequence and hands off correctly, but
  only the real client proves the sequence is what the client SENDS.
- **What to look for (play-local / real client)**: in a sector with a working
  stargate (e.g. Luna space 1015 -> obj 533 "Sector Gate to Earth" -> 1060),
  fly to the gate and jump. Capture the cleartext proxy<->server leg
  (`proxy/local-debug/`); confirm the client->server frames carry `0x002C` with
  `Action=18` (Target=the gate gid) followed by `Action=19`, and that the
  server replies with a `0x003A SERVER_HANDOFF` to the destination sector. If
  the real client uses different Action codes or a different Target convention,
  fix `GateCommand.BuildActionFrame` callers to match and re-pin.
- **Setup**: `just play-local`, fly to a stargate, jump; diff the proxy capture
  against `GateCommand`'s emitted bytes.

### [ ] CV-07 -- Luna planet 0x003F survives the LP64 in-range bitset fix

- **Supersedes the root-cause half of CV-03.** CV-03 asks "is the Luna body
  visible"; this entry pins WHY it was intermittently NOT, and what changed.
- **Root cause (CONFIRMED)**: `Object::SetIndex/UnSetIndex/GetIndex` (and the
  `*BitEntry` pair) in `server/src/ObjectClass.cpp` computed the word stride
  from `sizeof(long)*8` but the in-word bit position from a hardcoded `%32`.
  On the retail Win32 server `sizeof(long)==4`, so both were 32 and agreed. On
  our LP64 Linux build `sizeof(long)==8`: the stride divides by 64 while the
  bit still wraps at 32, so object indices that differ by 32 alias onto the
  same bit. A sector nav (e.g. index 80) and the Luna planet (index 112) both
  map to word 1 bit 16 of `ObjectRangeList`; whichever was processed first in
  `SendAllNavs` set the bit, and the second was then seen as "already in
  range" and skipped -- intermittently suppressing Luna's planet-exclusive
  0x003F PLANET_POSITIONAL_UPDATE at sector entry.
- **Change (implemented)**: derive the in-word bit width from the word type
  (`OBJ_BITS_PER_WORD = sizeof(long)*8`) and use it for BOTH the stride and the
  modulus, with `1L <<` shifts. The two can no longer disagree on any ILP32 or
  LP64 target. This restores the behaviour the 32-bit retail server always had.
- **Primary source**: the retail server is x86 (32-bit `long`); its bitset
  packs 32 indices per word and never aliases at the 32-boundary because stride
  and modulus are both 32. Our LP64 build is the only place the two diverged.
  The send-every-nav path that exposed it is commit `636c6175` (cited there
  against the live-server roster). The aliasing arithmetic is mechanical, not a
  behaviour guess.
- **CLI proof**: `SectorPlanetPositionalUpdateHardeningTests` Wave 107
  (`PlanetPositionalUpdateAndNavigation_..._PinsSendAllNavsCount`) byte-pins
  exactly one 48-byte 0x003F plus one-per-nav 14-byte 0x0099 on the space-sector
  handshake; Wave 89 pins the 48-byte 0x003F payload length. Before the fix the
  0x003F collection came back empty on the LP64 server (the failing CI signal).
- **What to look for (play-local / real client)**: enter Luna space (1015) from
  Luna Station several times; the Luna planet body must render in-space and on
  the map EVERY time, with all clickable navs also present (no run where the
  planet is missing while navs show, which was the aliasing signature).
- **Setup**: `just rebuild server && just play-local`, undock into Luna space,
  repeat 3+ times.

### [ ] CV-08 -- 0x008B ATTACKER_UPDATES mob id is big-endian on the wire

- **Change (implemented)**: `Player::SendAttackerUpdates`
  (`server/src/PlayerConnection.cpp:3604-3620`) now writes `mob_id` with
  `htonl` (`*((int32_t*)&attacker_data[5]) = htonl(mob_id)`) instead of
  host-order. The `update` field stays host-order (little-endian).
- **Root cause**: the 9-byte 0x008B payload is `update(4 LE) + 0x01 + mob_id(4)`.
  We were writing `mob_id` host-order (LE on x86), so the real client received
  a byte-reversed attacker id, looked it up in its object pool, got NULL, and
  could fault on the next dispatch -- the classic Trap-1 (CLAUDE.md "Wire
  format & byte order").
- **Primary source**: live Net-7 reference server 216.219.87.147:3573,
  cleartext proxy<->server UDP leg, `Combat-...-20260604-072157.pcapng`
  frame #26 emits mob 0x000186F5 as the trailing bytes `00 01 86 F5`
  (big-endian). The Ishuan / SkillTrainingHostileDevice2 / KillLoot2 captures
  show the same big-endian form for many distinct mobs (00 01 8x xx), and the
  one at `00 01 87 EC` is the SAME mob the 0x0064 CLIENT_DAMAGE frames report
  little-endian as source 0x000187EC -- independent cross-corroboration. This
  matches the existing `htonl(playerid)` in the 0x0083/0x0081 recustomize
  opcodes (same file, ~line 10028).
- **CLI proof**: `LiveReferenceCombatTests.AttackerUpdates_0x008B_MobIdIsBigEndian`
  byte-pins both the start (update=1, mob 0x000187EC) and stop (update=0, mob
  0x000186F5) frames from the live captures, asserting MobId decodes only when
  read big-endian (and explicitly NOT little-endian). `AttackerUpdatesRecord`
  was fixed to read MobId via `ReadI32BE` in the same change.
- **What to look for (play-local / real client)**: pull aggro from a mob; the
  attacker indicator / "being attacked by" UI must reference the correct mob
  (and clear when it disengages). Before the fix the client saw a garbage
  attacker id -- watch for a crash or a phantom/absent attacker marker on the
  first hostile in a sector.
- **Setup**: `just rebuild server && just play-local`, undock, let a mob start
  and stop attacking.

### [ ] CV-09 -- Live mining prospect beam (0x000B) renders on the real client

- **Change**: none yet to the server/proxy -- this entry pre-registers the
  real-client confirm for the live mining round-trip (task #66 / AD-66). The
  server already emits `0x2012` START_PROSPECT from `Player::MineResource`
  (`server/src/PlayerSkills.cpp:625`) and the proxy already fabricates the
  client-facing `0x000B` OBJECT_TO_OBJECT_EFFECT prospect beam from it
  (`UDPClient::StartProspecting` / `SendProspectAUX`). No wire change is
  required; what is unproven is that the *fabricated* beam renders correctly on
  `client.exe` during a real mining cycle.
- **Primary source (format)**: the `0x000B` wire STRUCTURE is byte-pinned
  against the live Net-7 reference server (`live_object_effect_000B_weapon.hex`,
  35B string form) by `LiveReferenceCombatTests`. CV-02 already covers the roid
  field rendering + targetability; this entry is specifically the prospect
  *beam* effect.
- **CLI proof**: `StartProspectRecord` (0x2012) and `ObjectToObjectEffectRecord`
  (0x000B) decode the compact source and the fabricated beam. A live end-to-end
  Phase-T drive (JE-Explorer, prospect skill seeded, in-range + stationary on a
  roid, 0x0017 REQUEST_TARGET -> 0x0027 INVENTORY_MOVE sub-18) to pin the
  fabricated 0x000B on our own stack is the open build -- gated on the
  prospect-range/position-feed solve (see AD-66 in plans/31), NOT on the format.
- **What to look for (play-local / real client)**: as a Jenquai Explorer with
  the Prospect skill, fly to a resource sector (e.g. 1710/3606/1750), stop on an
  asteroid field, target a roid, and start mining. The prospect beam effect must
  draw from the ship to the roid for the drain duration and clear when the ore
  is pulled. Watch for a missing beam, a beam to the wrong object, or a client
  fault when the beam starts.
- **Setup**: `just play-local` as a JE Explorer; `just grant-prospect <user>`
  if the skill is not trained; mine a roid in a resource sector.
