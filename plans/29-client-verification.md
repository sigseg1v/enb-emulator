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
  server** (the live reference server) talking to a player-side proxy
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
- **Primary source**: live Net-7 reference server, port 3573,
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

### [ ] CV-10 -- On-demand sector start (Phase AI) is invisible on the real client

- **Change**: Phase AI Stage 1 (lazy sector lifecycle). Sectors no longer start
  at boot; the first arrival (handoff/undock/gate/warp) cold-starts the
  destination sector: load its deferred objects (MOBs + asteroid fields), claim
  a manager, bind its deterministic UDP port, start its thread. The nav skeleton
  (gates/planets/stations/navs/gwell/radiation) stays resident galaxy-wide so
  cross-sector mission generation + gate-sealing still see remote sectors.
  Server files: `ServerManager::EnsureSectorStarted`, `SectorContentSQL.cpp`
  (SectorLoadMode), `Player::ChangeSectorID` (PlayerConnection.cpp:9336 -- the
  cold-start hook that fixed the undock-into-space segfault). NO WIRE CHANGE:
  the 0x003A handoff bytes and the sector-entry fanout are byte-identical to the
  eager-boot build; only the TIMING of allocation + mob-AI activation moves to
  first entry.
- **Primary source (format)**: none needed -- no emitted bytes change. The full
  Phase-T lazy-path slice (SectorMining, SectorUndockHandoffFollow, SectorWarp,
  SectorGateHandoffFollow, SectorStartAck, BootLogHealth) is byte-pinned and
  passes against the cold-start path; cold-starts fire for stations AND space
  sectors on demand with no server restart.
- **CLI proof**: the above slice drives a real undock/gate/warp through the
  master handoff, which cold-starts the target sector before sector login; the
  re-joined sector announces live mob spawns + asteroid fields (asserted). This
  proves the player lands in a fully populated, threaded sector that was offline
  microseconds earlier.
- **What to look for (play-local / real client)**: (1) undock from a station you
  just logged into -- you must arrive in open space with NPC ships + roids
  present and animating, no hang, no "empty sector". (2) Gate or warp to a
  sector no one has visited since server boot -- same: it must populate on
  arrival with no perceptible extra delay and no client fault. (3) Combat-mission
  MOBs pre-placed into an unvisited sector: confirm they are present + hostile
  when you arrive (their AI now starts on first entry, not at boot).
- **Setup**: `just play-local`; log in at a station, undock, then gate to a
  fresh sector. Watch the first-arrival moment specifically.

### [ ] CV-11 -- Idle sector teardown + re-entry (Phase AI Stage 2) is invisible on the real client

- **Change**: Phase AI Stage 2 (idle teardown). A space sector that has sat
  continuously empty past NET7_SECTOR_IDLE_TEARDOWN_MS (default 5 min) is PARKED:
  the whole shared-obj_manager family (the space sector + every station built on
  it, since stations reuse the parent's obj_manager) is taken offline, its event
  + recv threads are joined, and the deferred objects (MOBs / asteroid fields /
  husks / dropped loot) are freed; the nav skeleton + the manager mapping stay
  resident. The next arrival restarts the parked family in place (reload deferred
  content, rebind port, restart thread) -- same code as a Stage 1 cold-start.
  Server files: `ServerManager::TeardownSector` / `IdleSectorPoll`,
  `ObjectManager::PurgeDeferredObjects` / `CanPurgeDeferredObjects`,
  `SectorManager::StopSectorThread` / `ClearAllEventSlots` / `DropListener`,
  `UDP_Connection::StopReceiver`. NO WIRE CHANGE: a parked sector emits nothing;
  re-entry reuses the byte-identical cold-start fanout.
- **Primary source (format)**: none needed -- no emitted bytes change. Same
  Phase-T lazy-path slice as CV-10 exercises the restart path (re-entry after a
  park is a cold-start). Verified live 2026-06-06 with low thresholds: park ->
  freed 111 deferred objects in sector 1015 (family of 2) -> re-entry restarted
  both station+space and the undock test re-passed with asteroids present.
- **What to look for (play-local / real client)**: (1) Visit a sector, populate
  some transient state (drop loot / jettison cargo / partially mine a roid /
  aggro a mob), LEAVE it empty for > teardown interval, then RE-ENTER -- the
  transient state must be RESET (fresh roids, no dropped loot, mobs at full /
  de-aggroed, respawn cycle restarted from the top). This is expected and
  correct, not a bug. (2) Re-entry itself must be seamless: arrive in a fully
  populated, animating sector with no hang, no "empty sector", no client fault --
  identical feel to a first cold-start. (3) Persistent state (your inventory,
  ship, missions, vault) is UNAFFECTED -- only sector-transient objects reset.
- **Setup**: `just play-local` with NET7_SECTOR_IDLE_TEARDOWN_MS set low (e.g.
  15000) and NET7_SECTOR_IDLE_POLL_MS=5000; enter a space sector, drop loot /
  mine a roid, fly to a station and wait out the interval, then return.


### [x] CV-12 -- Sector entry over real internet (NAT-traversed global UDP) works on the real client

> **VERIFIED 2026-06-07** against `enb.sigsegv.land` with the real Win32 client
> (after the CV-20 loopback-redirect fix unblocked sector entry). Sector load
> completed and rendered -- no login-stage-3 timeout / loading-screen hang --
> and a gate jump (0x009B WARP) worked, which re-exercises the same
> MasterJoin -> handoff -> NAT-punched global UDP path. Still nice-to-confirm:
> sit stationary > 2 min to watch the 0x3005 keepalive refresh the reaper.

- **Change**: proxy global-plane NAT-hole punch. On the sector `0x0002 LOGIN`
  the proxy now sends a `0x3005 PLAYER_COMMS_ALIVE` FROM the global UDP socket
  TO `server:3806` (MVAS port) and starts that socket's 0x3005 keepalive thread.
  Without it, behind a restricted-cone/symmetric NAT (home router + the cloud
  host's docker MASQUERADE) the server's in-game UDP -- which arrives from
  server port 3806, not the 3810 the global socket last sent to -- has no
  conntrack mapping and is dropped, so the client never receives the login-stage
  `0x2020` confirm and the server times out at "login stage 3". Files:
  `proxy/ClientToServer_linux_stubs.cpp` (0x0002 LOGIN handler),
  `proxy/UDPClient_linux.cpp` / `UDPClient.h` (`StartMVASKeepalive`).
- **Primary source (format)**: the 0x3005-to-3806 packet is unchanged and
  already captured -- `proxy/local-debug/net7-live-2026-06-02-login-undock-clicknavs-warp-logout.pcap`
  (4x P->S 0x3005 to udp/3806, bare 12-byte EnbUdpHeader). This change only
  alters WHICH socket emits it and WHEN (one extra emit, in lockstep at LOGIN);
  no new bytes. The race-free guarantee comes from the server registering the
  player at avatar login (`UDP_Global.cpp` GlobalTicketRequest -> SetupPlayer)
  BEFORE the sector LOGIN, so the punch's GetPlayer() resolves and no
  `0x100A MVAS_TERMINATE` is returned.
- **Why the CLI/integration suite cannot validate it**: the Phase T suite runs
  proxy+server on one docker bridge with NO stateful NAT between them, so the
  3806 return path is never filtered -- the bug is invisible locally and only a
  real client (or the CLI) connecting over the real internet through NAT
  exercises it. This is a transport/NAT fix, not a wire-format change.
- **What to look for (real client over the internet)**: log in to the cloud
  host, pick a character, ENTER the sector (dock at a station) -- it must reach
  the sector and render, NOT hang at the loading screen / time out at login
  stage 3. Then undock, gate, and sit stationary > 2 min to confirm the same
  keepalive still refreshes the reaper (no "ship disappeared").
- **Setup**: cloud deploy (`deploy/do/`), real client pointed at the cloud host
  over the public internet (NOT loopback / same-LAN, which may not NAT the UDP
  return path the same way).


### [ ] CV-13 -- Real client still plays end-to-end with proxy<->server DTLS ON

- **Change**: Phase AH. The proxy<->server UDP leg is now wrapped in DTLS 1.2
  (both directions), default-ON and fail-closed on both ends. The proxy opens a
  client-role DTLS association per (server_ip, server_port) on each of its UDP
  sockets (master plane -> 3808/3806/3501+N; global plane -> 3810/3806); the
  server wraps its UDP receivers in a server-role transport keyed per proxy peer.
  The cleartext opcode stream inside the channel is byte-for-byte unchanged --
  DTLS is a transport wrapper (RFC 6347), not a new packet format -- so the
  existing per-opcode CLI byte-pins still describe the plaintext payload exactly.
  Files: `proxy/UDPClient_linux.cpp`, `proxy/Net7.cpp`, `proxy/UDPClient.h`,
  `proxy/Net7.h`, `server/src/ServerManager.cpp`, `server/src/UDPConnection.cpp`,
  `common/DtlsTransport.{h,cpp}`.
- **Primary source (format)**: none needed for a payload change -- there is no
  payload change. The plaintext inside the DTLS record is the identical
  EnbUdpHeader+opcode stream the existing captures and CLI tests already pin.
  DTLS framing itself is the OpenSSL implementation of RFC 6347; correctness of
  the wrapper is proven by the handshake completing and the same opcodes arriving
  decrypted (the integration suite, run with DTLS configured, is the byte-pin).
- **Why the CLI/integration suite cannot fully validate it**: the dev/test stack
  runs on a trusted private docker bridge and (until the CLI gains a DTLS client)
  opts out via `NET7_DTLS_ALLOW_PLAINTEXT`, so locally the channel is cleartext.
  Only a real client whose proxy runs with DTLS ON, verifying a real Let's
  Encrypt server cert by name (connect-by-IP / verify-by-name via SSL_set1_host),
  proves the handshake + verification + decrypt path works against the live
  server over the public internet under WINE.
- **What to look for (real client over the internet, DTLS ON)**: with the proxy
  running DTLS-enabled (NOT opted out) and pointed at the cloud host's real
  hostname, full login -> character select -> sector entry -> undock -> move ->
  gate -> chat -> logout all work exactly as before, with NO added latency spikes
  or disconnects. The proxy log prints "proxy<->server DTLS ENABLED (verify host
  '<domain>' ...)" at startup and NOT the plaintext-opt-out banner. A cert-name
  mismatch or missing CA must make the proxy refuse to start (fail-closed), not
  silently fall back to cleartext.
- **Setup**: cloud deploy (`deploy/do/`) with a provisioned LE cert/key for the
  server (NET7_DTLS_CERT/KEY or <domain>.cer/.pem) and the player-side proxy
  configured with NET7_GAME_SERVER_DOMAIN=<cert hostname> and, under WINE, a CA
  bundle reachable via NET7_DTLS_CA (or the system trust store). Real client over
  the public internet, NOT loopback.

### [ ] CV-14 -- Real client still logs in with server-side ticket-suffix validation ON

- **Change**: Phase AH-9. The game server now validates the full ticket suffix
  presented on the global UDP plane. `server/src/UDP_Global.cpp ProcessTicketInfo`
  calls the new `AccountManager::ValidateTicketSuffix` -- a parameterized SELECT
  against the shared `net7_user.login_ticket` table + constant-time
  `sodium_memcmp` token compare + expiry check -- and rejects with
  G_ERROR_TICKET_INVALID (7) when the row is missing, expired, or the token does
  not match. Both ticket issuers UPSERT the row when they mint a ticket:
  login-server `LinuxAuth.cpp BuildTicketLocked` (TLS :443 path) and game-server
  `AccountManager.cpp BuildTicket` (UDP 0x2000->0x2001 path). This is ALWAYS-ON
  (not DTLS-gated): it is a pure tightening that rejects an input the real Win32
  server rejected via the login->game `RegisterSectorServer` SSL ticket handoff,
  which was dropped in the Linux port. Files: `server/src/UDP_Global.cpp`,
  `server/src/AccountManager.{h,cpp}`, `login-server/Net7SSL/LinuxAuth.cpp`,
  `db/postgres/login_ticket.sql`, `docker-compose.yml` (schema-init apply).
- **Primary source (format)**: no payload change. The 0x2002 TICKET packet on the
  internal UDP plane is unchanged (it already carried the full `username-<32hex>`
  string via `strlen(ticket)`); only the server's acceptance criterion tightened.
  The CSPRNG suffix format is byte-pinned in
  `freya/tests/integration/.../Handshake/TlsLoginTests.cs`; the accept (genuine ticket)
  and reject (forged suffix) paths are pinned in
  `GlobalConnectTests.ValidTicket_RoundTripsThroughUdpGlobalPlane_ReturnsAvatarList`
  and `GlobalConnectTests.ForgedTicketSuffix_GlobalConnect_ReturnsGlobalErrorTicketInvalid`.
- **Why the CLI/integration suite cannot fully validate it**: the suite proves the
  server accepts a genuine issued ticket and rejects a forged one, but only the
  real Win32 client proves the live login->character-select->sector-entry flow
  still completes with the validation active -- i.e. that the real client's
  ticket (issued by the same login-server BuildTicketLocked path) round-trips
  through ProcessTicketInfo's new SELECT without false rejection, and that the
  game-server-issued BuildTicket path (used on the 0x2000 ACCOUNTDATA flow) also
  validates. A clock skew between the login-server and game-server containers, or
  a net7_user connection failure in ValidateTicketSuffix, could falsely reject a
  legitimate client login -- the suite's shared-clock single-host docker stack
  would not surface that.
- **What to look for (real client)**: full login -> character select -> sector
  entry completes normally; the server log shows NO
  "ProcessTicketInfo: rejected invalid/expired ticket" line for a legitimate
  login, and the `login_ticket` table has a fresh row for the account
  (`SELECT username, expires_at FROM login_ticket`) with `expires_at` in the
  future. A relogin after >5 min (ticket expiry) re-issues and re-UPSERTs the row
  cleanly. No added login latency.
- **Setup**: any deploy (local docker or cloud). Ensure schema-init applied
  `login_ticket.sql` to net7_user (`\d login_ticket` shows username/token/expires_at)
  -- on a pre-existing volume the apply is unconditional so it lands on restart.

### [ ] CV-15 -- Real client chat is attributed to the speaker, never a spoofed id (AJ-4)

- **What changed**: `server/src/PlayerConnection.cpp HandleClientChat` now passes
  the authenticated connection's `GameID()` -- never the wire-controlled
  `chat->GameID` -- to GuildChat/LocalChat/BroadcastChat (Type 2/3/4). The
  GroupChat branch already used `GameID()`. A wire id that disagrees with
  `GameID()` is logged at debug as a spoof attempt and the real id is used.
  Pure tightening: the retail client only ever sends its own id here.
- **Primary source (format)**: no wire-format change. The inbound 0x0033
  CLIENT_CHAT layout is unchanged (`common/include/net7/PacketStructures.h:624`
  `struct ClientChat`); only which field the server trusts as the sender
  tightened. The displayed-sender attribution path is
  `PlayerManager::BroadcastChat`/`LocalChat` -> `GetPlayer(GameID)` ->
  `SendClientChatEvent(... s ...)` emitting 0x00A5 CLIENT_CHAT_EVENT with the
  resolved sender's LastName (`server/src/PlayerManager.cpp:701-768`,
  `PlayerConnection.cpp SendClientChatEvent`).
- **Why the CLI/integration suite cannot fully validate it**: the spoof is
  observable only at a SECOND avatar in the same sector (attacker broadcasts with
  a victim's GameID; the fix makes the victim see the attacker's name, the bug
  made the victim see nothing). The two-player integration test
  (`freya/tests/integration/.../Opcodes/TwoPlayerChatSenderSpoofTests.cs`) is written
  and byte-correct but `[Fact(Skip)]` -- BLOCKED by Net7Proxy single-tenancy
  (one UDPClient/m_MasterConnection per proxy; Player B's handshake clobbers
  Player A's routing). It will run as-is once the proxy demultiplexes by session.
  Until then only the real client (two clients, two proxies via `just play-cli`)
  can witness the live fan-out.
- **What to look for (real client)**: with two clients A and B in the same
  sector, normal Local/Broadcast/Guild chat from A shows as sent by A on B's
  client (regression guard: the fix must not have broken ordinary attribution).
  The server log shows NO "sent ClientChat type N with spoofed sender GameID"
  debug line during honest play (the real client never spoofs). The
  authorization property -- that a crafted packet cannot make chat appear from
  another avatar -- is not reachable from the stock client (it always sends its
  own id); it is covered by the Skip'd integration test once the proxy
  multiplexes. The owner-facing check here is purely the no-regression one:
  Local range (~25000 units), Broadcast (whole sector), and Guild chat still
  deliver and are attributed to the real speaker.
- **Setup**: two real clients in one sector (`just play-cli` per client, distinct
  accounts/avatars). Any deploy.

### [ ] CV-16 -- Real client inventory moves still work with slot-bounds validation ON (AJ-3)

- **What changed**: `server/src/PlayerConnection.cpp HandleInventoryMove` (0x0027)
  now bounds-checks the wire-controlled `FromSlot`/`ToSlot` against the real
  per-inventory slot counts (CargoInv 40, EquipInv/m_Equip/AmmoInv 20, SecureInv
  96, TradeInv 6, VendorInv 128) via a new `InventorySlotCount(type, asDest)`
  helper, rejecting out-of-range indices (the `-1` auto-select sentinel excepted
  for the cargo<->vault and manu-override->cargo branches that resolve it) with a
  LogDebug + early return before the dispatch switch. Pure tightening: the retail
  client only ever sends a concrete in-range slot.
- **Primary source (format)**: no wire-format change. The inbound 0x0027 InvMove
  layout is unchanged (`common/include/net7/PacketStructures.h:243`, 24 bytes,
  all fields ntohl'd); only the server's slot-range acceptance tightened. The
  slot counts are read directly from the array declarations:
  `server/src/AuxClasses/AuxInventory40.h:25` (Cargo), `AuxInventory20.h:25`
  (Ammo), `AuxEquipInventory.h:25` (EquipItem[20]), `PlayerClass.h:1320`
  (m_Equip[20]), `AuxSecureInventory.h:25` (Item[96]), `AuxInventory6.h:25`
  (Trade), `AuxVendorInventory.h:25` (Item[128]).
- **Why the CLI/integration suite cannot fully validate it**: the suite proves
  the server drops an OOB move (FromSlot=20000) without crashing and that legit
  moves (cargo split, vault transfer, vendor) still round-trip
  (`freya/tests/integration/.../Opcodes/SectorInventoryMoveTests.cs` --
  `InventoryMove_OutOfBoundsFromSlot_IsDropped_ConnectionSurvives` + the four
  existing move tests, all green). What it cannot exhaustively cover is the full
  matrix of real-client move operations against a populated inventory: equipping
  weapons/devices into specific equip slots, loading ammo, multi-slot vault
  deposits with the `-1` auto-select sentinel, manufacturing input/output box
  moves, and trade-window moves -- each indexes a different array at a
  client-chosen slot, and only the real client exercises the slots the way the
  game UI generates them. A miscount in the helper (e.g. if equip were really
  some count other than 20) could wrongly reject a legitimate high equip slot.
- **What to look for (real client)**: with a non-empty inventory, all normal item
  operations work: drag cargo<->cargo, equip/unequip every weapon and device
  slot, load/swap ammo, deposit/withdraw vault items (including the "drop on the
  vault, auto-pick a slot" path), sell/buy at a vendor, jettison, and any
  manufacturing moves. The server log shows NO "Rejected InventoryMove: ...
  out of range" line during legitimate play (that line should only ever appear
  for a crafted/hacked client). No item loss, no dupe, no stuck slot.
- **Setup**: any deploy; a character with items in cargo, equip, vault, and a
  docked vendor to exercise all five inventory types.

### [ ] CV-17 -- End-to-end DTLS + per-packet auth token with the real client (AH-8/AH-9/AH-10)

- **What changed**: the proxy<->server UDP leg now runs DTLS, and every C->S app
  datagram inside that channel carries a 17-byte auth wrapper
  (`EnbUdpAuthWrapper`: 1 version byte + 16-byte token =
  `common/include/net7/PacketStructures.h`). The proxy learns the token from the
  ticket suffix at global connect (`proxy/ClientToServer_linux_stubs.cpp`),
  prepends it on every C->S send (`proxy/UDPClient_linux.cpp UDP_Send`, `m_Dtls`
  branch), and the server strips + validates it at the recv edge
  (`server/src/UDPConnection.cpp` -> `net7::CheckAuthWrapper` in
  `server/src/AuthWrapper.h`). The token is bound to the player at
  character-select (`UDP_Global.cpp HandleGlobalTicketRequest` ->
  `AccountManager::GetLoginTokenBinary` -> `Player::SetAuthToken`) and enforced
  only on gameplay datagrams (inner header `player_id != 0`); pre-auth datagrams
  (player_id 0) carry a zero token and skip the check.
- **Primary source (format)**: the wrapper is a NEW field on the proxy<->server
  leg ONLY -- the real client never sees this leg (the proxy terminates the
  client's encrypted TCP and re-frames). The inner [EnbUdpHeader][payload] is
  byte-for-byte identical to the cleartext form, pinned three ways: server gtest
  `freya/tests/server/protocol/header_layout_test.cpp` (`EnbUdpAuthWrapperIs17Bytes`),
  C# `freya/cli-client/.../Net/AuthWrappedPacket.cs` +
  `.../CliClient.UnitTests/Net/AuthWrappedPacketTests.cs`, and the accept/drop
  decision gtest `freya/tests/server/protocol/auth_wrapper_test.cpp`.
- **Why the CLI/integration suite cannot fully validate it**: the CLI has no DTLS
  client, so the integration suite runs the plaintext opt-out path and never
  exercises the wrapper on the wire. The unit tests prove the decision logic
  (correct token accepted; wrong-but-well-formed token dropped as "token
  mismatch"; no-bound-token dropped; pre-auth skipped; bad version / short /
  oversize dropped) and the byte layout, but ONLY the real client driving the
  real proxy with DTLS on proves a full game session survives the wrapper on
  every C->S datagram.
- **What to look for (real client)**: with DTLS ON (the public default, NOT the
  `NET7_DTLS_ALLOW_PLAINTEXT=i-accept-unencrypted-udp` opt-out), log in and play:
  movement, chat, combat, inventory, gating, docking, vendor -- all C->S actions
  must work normally. The server log shows NO "DTLS auth wrapper: dropped N C->S
  datagram(s)" line during legitimate play (that line should appear only for a
  forged/skewed client). Confirm a dashed username (if one can be created) also
  logs in and plays -- the ticket suffix parse now splits on the last dash.
- **Setup**: a deploy with DTLS enabled (no plaintext opt-out), valid server
  cert/key, and the proxy built with the DTLS client path. A normal character.

### [ ] CV-18 -- Fresh character reaches space/station instead of hanging on the loading screen (lazy-sector login fix)

- **Change**: server commit `765f1eca` -- at login stage 4, when
  `Player::GetSectorManager()` resolves NULL (a fresh char's
  `ReInitializeSavedData` reset sector_num to a not-yet-started home
  StartSector under Phase AI lazy sectors), bring the sector online via
  `ServerManager::EnsureSectorStarted()` and re-resolve before advancing, so
  `HandleSectorLogin -> SendStart` (0x0005 START) actually fires.
- **Primary source (format)**: login state-machine analysis
  (`PlayerManager.cpp` case 4 -> `SectorManager::HandleSectorLogin` ->
  `SendStart`) plus server-log evidence -- a fresh-char cold-start to a space
  sector logged no "Sector login for player" line and emitted no START, while
  warm/station logins did. Restores the pre-Phase-AI guarantee (all sectors
  online at boot) that the sector-login stages run against a live manager.
- **Why the CLI/integration suite cannot fully validate it**: the integration
  suite confirms the START packet is now emitted (the two tests that wedged --
  `StationHandshake_ManufactureSetManufactureIdPayload` and
  `SlashUitriggerMissingArg` -- pass 2/2), but only the real Win32 client
  proves the game session actually proceeds past the loading screen into space
  / the station after receiving START.
- **What to look for (real client)**: create a BRAND-NEW character (any
  race/profession), log in for the first time. The client must load into the
  home station (or space) and be playable, NOT hang on the loading screen.
  Repeat for a race/profession whose StartSector differs (e.g. a Jenquai vs a
  Terran) so a sector that is genuinely cold at first login is exercised.
- **Setup**: a deploy with Phase AI lazy sectors active (the default) and an
  empty/fresh save state so the character is created and reinitialised on first
  login.

### [ ] CV-19 -- Online login no longer fails with "EA.com temporarily unavailable (INV-300)" (TLS full-chain fix)

- **Change**: login-server `SSL_Listener::SSL_Listener` now loads the cert with
  `SSL_CTX_use_certificate_chain_file` instead of `SSL_CTX_use_certificate_file`.
  The leaf-only loader silently dropped the intermediate, so port 443 handed
  clients a leaf-only chain.
- **Primary source (root cause)**: live probe of `enb.sigsegv.land:443`
  (`openssl s_client -showcerts` returned exactly 1 certificate; `-verify`
  failed num=20 "unable to get local issuer certificate"). The launcher's
  `LocalAuthRelay` validates the upstream against the OS trust store (remote
  upstream => full validation; .NET on Linux does no AIA fetch), so the broken
  chain failed the relay's TLS handshake and the EnB client reported WinINet
  12029 / INV-300. The deploy provisions `<domain>.cer` as the FULLCHAIN
  (deploy/do README), so chain-loading sends the intermediate.
- **Why the CLI/integration suite cannot fully validate it**: the online auth
  path (authlogin.dll -> LocalAuthRelay -> remote 443 TLS) is exercised only by
  the real client + launcher against a real CA-signed deploy; the integration
  suite connects with verify-skip / local self-signed single certs and never
  sees an incomplete public chain.
- **What to look for (real client)**: `just play-online` against a redeployed
  `enb.sigsegv.land`, enter member name + password, click Accept. Login must
  proceed to character select instead of the ~30s hang + INV-300. Independently
  confirm with `openssl s_client -connect enb.sigsegv.land:443 -showcerts`
  returning 2 certs (leaf + LE intermediate) and `Verify return code: 0`.
- **Setup**: REQUIRES redeploy of the login binary (the code fix) AND the
  remote `<domain>.cer` being the fullchain. If after redeploy s_client still
  shows 1 cert, the deployed `.cer` is leaf-only -- regenerate it as fullchain.

### [x] CV-20 -- Online sector entry no longer kicks back to login (proxy advertises loopback in ServerRedirect)

> **VERIFIED 2026-06-07** against `enb.sigsegv.land` with the real Win32 client.
> Proxy console showed the previously-absent `accept on port 3500 from 127.0.0.1`
> -> `<client> SectorServer LOGIN -- connection active` -> `START_ACK -> server`
> (player boxB, sector 10151). Sector entry succeeds; no +15s kick.

- **Change**: the launcher (`tools/LaunchFreya/Launcher.cs::LaunchNet7Proxy`)
  now spawns Net7Proxy with `/ADDRESS:127.0.0.1` for ALL upstreams, instead of
  `/ADDRESS:{resolved-upstream-IP}`. `/ADDRESS` sets the proxy's local
  listen/advertise identity (`g_internal_addr` -> `ServerManager::m_IpAddress`),
  which is the only thing `Connection::SendServerRedirect` (sector handoff,
  opcode 0x0036) stamps as the IP the client must reconnect to for the sector.
  The dialled upstream is conveyed separately via `NET7_UPSTREAM_HOST` ->
  `NET7_GAME_SERVER_HOST` (the proxy's own UDP resolver), so /ADDRESS never
  needed the server IP.
- **Primary source (root cause)**: live `just play-online` proxy log (two runs,
  15:36 and 15:58) -- after `MasterJoin ToSectorID=10151` ->
  `MasterLoginConfirm udp_port=3501` -> `Master Login received - UDP sector
  port: 3501` -> `DTLS association ESTABLISHED to <droplet>:3501`, the client
  goes SILENT for exactly ~15s, then `TCP 3801 closed` and the client falls back
  to `accept on port 3805` (login). There is NO `SectorServer LOGIN -- connection
  active` and NO `accept on port 3500` -- i.e. the client never reconnected to
  the local proxy's sector TCP port. Cross-checked against a droplet
  `udp portrange 3501-3820` tcpdump: the sector leg's DTLS handshake completes
  but ZERO app-data ever flows, because the proxy only forwards sector app-data
  after the client sends `0x0002 LOGIN` over the local 3500 TCP, which it never
  did. Code trace: `SendServerRedirect` (ClientToMasterServer.cpp:191) emits
  `ntohl(m_ServerMgr.m_IpAddress)`; `m_IpAddress = inet_addr(g_internal_addr)`
  (Net7.cpp:271,287-292) and the launcher fed `g_internal_addr` the resolved
  droplet IP -- so the client was told to open its sector TCP to
  `<droplet>:3500` (the remote server, which has no proxy TCP listener there)
  instead of `127.0.0.1:3500` (the co-located proxy). It thus never connected,
  timed out at the client's ~15s sector-handshake deadline, and got kicked.
- **Why play-local was unaffected**: there `_setting.Hostname` already resolves
  to `127.0.0.1`, so the launcher happened to pass `/ADDRESS:127.0.0.1` -- the
  correct value -- and the loopback `ntohl(inet_addr("127.0.0.1"))` redirect on
  this exact path is therefore already proven good by every working local run.
- **Why the CLI/integration suite cannot validate it**: the CLI connects to the
  server directly and never goes through the proxy's client-facing sector
  ServerRedirect (0x0036) at all -- only the real Win32 client reconnects on the
  address this packet advertises.
- **What to look for (real client)**: `just play-online` against `enb.sigsegv.land`,
  log in to character select, enter a character into its sector. The load screen
  must complete into the sector (station or space) instead of sitting ~15s and
  bouncing to login. In the proxy console, confirm `<client> SectorServer LOGIN
  -- connection active` appears (it was absent in the failing runs), followed by
  the in-sector traffic. Then confirm a gate jump still works (CV-12 territory).
- **Setup**: rebuild the launcher (`just play-online` build-if-stale picks it up);
  no proxy/server binary change is required -- only the argument the launcher
  passes changed.

### [ ] CV-AM-1 -- External status events render correctly for the 4 player-driven kinds (Phase AM)

- **Change**: Phase AM emits 5 server status events as fully-rendered lines into
  the `external_status_events` outbox (net7_user) via `EmitExternalStatusEvent`
  (`server/src/SaveManager.cpp`), which the `status-notifier` Go sidecar relays to
  a Discord channel via the bot. **Emit-only** -- alters no EnB client wire byte, so this is NOT a
  wire-fidelity CV. The reason it is here anyway: the four player-driven lines are
  RENDERED from live server state (name, class abbrev, the three level fields, the
  `(N online)` count, the broadcast sender+text) and that rendering has only been
  exercised by code inspection, never by a real client session.
- **What is ALREADY proven (no client needed)**: the sidecar end-to-end
  (NOTIFY -> drain -> bot channel post -> sent_at) against the real dev
  Postgres, the startup catch-up drain, and the server's own emit path -- a live
  server brought up with `NET7_EXTERNAL_STATUS_ENABLED=1` wrote a real
  `server_start` row, proving SaveManager queue -> parameterized INSERT works.
- **What to look for (real client)**: with the flag on and `DISCORD_BOT_TOKEN` +
  `STATUS_CHANNEL_ID` set, log a character in and confirm the channel shows
  `Player <name> (level: Cx/Ty/Ez, class: XX) logged in. (N online)` with the
  RIGHT class abbreviation (`ClassIndex() = Race()*3 + Profession()` ->
  `{TE,TT,TS,JD,JS,JE,PW,PP,PS}`), the right C/T/E levels, and an N that matches
  the actual online count. Then: level up a skill (`Player <name> leveled up
  {Combat|Trade|Explore} to ##!`), send a sector broadcast (`Server broadcast:
  [<sender>] <message>`), and log out (`Player <name> logged out. (N online)`).
- **Why the CLI/integration suite does NOT close this**: the four lines depend on
  full in-game state transitions (character load with race/profession/levels, the
  AwardXP skill-point branch, BroadcastChat, the clean-logout drop path) that the
  headless CLI does not drive end-to-end in this env. The class-abbrev mapping in
  particular (the `Net7.h` Profession enum ordering) is the most likely thing to
  be subtly wrong and only a real character of a known class confirms it.
- **Setup**: `export NET7_EXTERNAL_STATUS_ENABLED=1`, a `DISCORD_BOT_TOKEN`, and a
  `STATUS_CHANNEL_ID`, `just play-local`, log in / level / broadcast / log out,
  watch the channel. See `docs/18-external-status-events.md`.

### [ ] CV-AM-2 -- `/status` Discord bot answers correctly (Phase AM-8)

- **Change**: AM-8 adds a read-only `/status` Discord bot inside the same
  `status-notifier` sidecar (`freya/status-notifier/bot.go`, `discordgo`). It connects
  OUTBOUND to Discord's gateway (no inbound port) and answers `/status` with an
  embed: up/down + uptime + in-memory player/warm-sector counts (from the
  `server_status` heartbeat the game server UPSERTs), plus a per-player table
  (name, class name+code, floored C/E/T levels, sector name). **Read-only** -- all
  `SELECT`s, no path into the game server -- so this is NOT a wire-fidelity CV; it
  is here only because the discordgo gateway/slash-command handshake cannot be
  exercised without a real bot token, which only the owner can create.
- **What is ALREADY proven (no bot token needed)**: the `server_status` heartbeat
  writes + refreshes every ~30s against the real dev Postgres (verified live, no DB
  errors); both bot queries (the `accounts`-join player list and the net7 `sectors`
  name lookup) run cleanly against the live dev schema; and the render logic
  (class name/code incl. JD!=JW, C/E/T levels, sector formatting, uptime,
  char-select rows) is covered by `bot_test.go`.
- **Setup (owner)**: create a Discord application + bot, invite it to the server
  with the `applications.commands` scope, put the token in the droplet/dev `.env`
  as `DISCORD_BOT_TOKEN` (and optionally `DISCORD_GUILD_ID` for instant command
  registration), and bring the stack up with `NET7_EXTERNAL_STATUS_ENABLED=1`.
- **What to look for**: run `/status` in Discord. Confirm the embed shows the right
  up/down state, a plausible uptime, the in-memory player + warm-sector counts, and
  -- with a character logged in -- that player's name, class (e.g. `Explorer JE`,
  NOT `JW`), C/E/T levels, and the sector NAME (not a bare id). Log everyone out and
  confirm it shows "nobody online". Stop the server and confirm `/status` flips to
  the offline/stale-heartbeat state within ~2 min. See
  `docs/18-external-status-events.md`.
- **Also verify (AM-9, same bot token)**: with `STATUS_CHANNEL_ID` set alongside the
  bot token, confirm the relay posts login/logout/level-up lines INTO that channel.
  As a Manage-Server admin, run `/notify list`
  (shows the five kinds on/off) and `/notify set logout off`; confirm subsequent
  logouts no longer post while logins still do, and that re-enabling does NOT flush a
  backlog of the suppressed events. Confirm a non-admin cannot see/use `/notify`.

### [x] CV-MVAS-POS -- Normal-thruster flight stays in sync (PB-2 position feed)

- **OWNER-CONFIRMED WORKING (2026-06-09)**: with the engine read wired and the
  two-plane feed-socket bug fixed (see "ROOT CAUSE FOUND + FIXED" below), the owner
  flew under normal thrusters with the in-client DLL feed active and confirmed
  chat, target selection, and warp ALL keep working -- position stays in sync, no
  S->C black-hole. Temp diagnostics removed. CV-MVAS-POS closed.


- **Change**: PB-2 restores the client->server position feed the real Net7Proxy
  provided. A minimal in-client DLL (`freya/client-injection/ClientPositionFeed.{h,cpp}` +
  `PosFeedDllMain.cpp`, built as `bin/FreyaPosFeed.dll`, injected via a launch-time
  remote-thread inject -- `bin/FreyaInject.exe`, NOT `AppInit_DLLs` which WINE does
  not implement)
  reads the engine ship position/orientation in-process and sends it to the proxy
  as a fixed 40-byte UDP datagram on loopback port `FREYA_CLIENT_POS_PORT`=3807
  (`freya/client-injection/ClientPositionShared.h`); the proxy consumer
  (`UDPClient::ReadClientShipPosition` -> `SendPositionIfChanged`,
  `proxy/UDPClient_linux.cpp`) binds 3807, drains the latest sample, and streams
  opcode `0x1004` to the server's MVAS port (3806), change-gated, so the server's
  authoritative position tracks what the client renders instead of dead-reckoning a
  diverging path from the coordinate-less `0x0014 MOVE` impulse.
- **Transport note (2026-06-08)**: the proxy<->hook transport is a loopback
  datagram, NOT shared memory. Shared memory could not reach `just play-local` --
  the proxy there is a Linux docker process and the WINE client's Win32 named
  mapping is invisible to it. A loopback datagram works in ALL three run modes:
  play-local (docker proxy, port published `127.0.0.1:3807:3807/udp` loopback-only),
  play-online/NET7MP (WINE proxy), native Win32. No server change and no `0x1004`
  wire change; the proxy attributes the position to its own session `m_PlayerID`,
  so a loopback feed only moves the feeder's own avatar.
- **Why the CLI/integration suite does NOT close this**: the CLI proves the
  `0x1004` wire FORMAT (`CaptureReplayTests.MvasPosition_1004_*`, fixture
  `live_mvas_position_1004.hex`, against the live Combat capture) and that the
  server accepts a direct feed (task #65). It does NOT prove the in-client engine
  read is correct -- there is no headless engine to read from. The engine read
  (`ReadEngineShipState` in `ClientPositionFeed.cpp`, backed by
  `FreyaReadEngineShipState_Local` in `freya/client-injection/ClientEngineOffsets.h`)
  is filled for the current client build; this CV entry is what proves it
  actually reads the right transform against the real client.
- **Setup (owner)**:
  1. The engine read is already in `freya/client-injection/ClientEngineOffsets.h`
     (committed, compiled into the DLL) -- it reads ship world position x/y/z +
     orientation x/y/z in-process and returns true only for a live in-space
     sample. If the client build changes, update the addresses there.
  2. Build the feed DLL: `just build-posfeed-dll` -> `bin/FreyaPosFeed.dll`. This
     needs the **i686** MinGW toolchain (`sudo apt install gcc-mingw-w64-i686-posix
     g++-mingw-w64-i686-posix`) because `client.exe` is PE32/i386 and the injected
     DLL must match its bitness; the 64-bit MinGW used for the proxy cannot emit
     it. The DLL is a minimal standalone unit (`PosFeedDllMain.cpp` +
     `ClientPositionFeed.cpp`) -- NOT the legacy `ClientDetours.dll` (which links
     MSVC `detours.lib` and hooks hardcoded client offsets). `play-local` builds it
     for you as a required step.
  3. Launch. Injection + transport are wired -- nothing to hand-run:
     - `just play-local`: builds the DLL + injector, sets `EnablePositionFeed=true`,
       and the launcher spawns `FreyaInject.exe` which starts `client.exe` suspended,
       remote-LoadLibrary's `FreyaPosFeed.dll` into it, and resumes it (WINE has no
       `AppInit_DLLs`); the docker proxy already binds 3807 (published
       loopback-only). On attach the DLL emits an `OutputDebugStringA`
       `[FreyaPosFeed] attached to client.exe` marker, so you can confirm injection
       even before the engine read is filled (the feed sends nothing until then).
       The definitive load check is `WINEDEBUG=+loaddll just play-local 2>&1 | grep
       -i net7posfeed` -- it logs the DLL map regardless of OutputDebugString.
     - play-online / native Win32: rebuild the Win32 proxy
       (`just build-proxy-win64`) so the WINE/native proxy carries the consumer,
       and set `EnablePositionFeed=true`. Native-Windows injection (Detours withdll)
       is NOT wired -- the remote-thread injector path is WINE-only.
  4. Undock and fly with normal thrusters only (no autopilot/warp).
- **Regression diagnosed + fixed (2026-06-09)**: when the feed first reached the
  server, targeting/warp/chat all broke. Root cause was the proxy's MVAS emit
  socket, NOT the wire format. The server's `HandleMVASPosReturn` /
  `HandleKeepCommsAlive` call `SetPlayerPortIP(source)` (server/src/UDP_MVAS.cpp:103,149)
  on every `0x1004`/`0x3005`, which repoints `m_Player_IPAddr`/`m_Player_Port` --
  the SINGLE address the server sends ALL server->client sector data to
  (PlayerConnection.cpp:246,804) -- at the source socket of that MVAS datagram.
  A short-lived "dedicated MVAS socket" experiment sent the feed+keepalive from a
  separate ephemeral socket, so the server then black-holed every S->C sector
  reply (chat/nav-select/target) to a socket the proxy never drains. Retail ran
  the whole server bridge on ONE socket (`SetUDPConnections(&udp_to_master,
  &udp_to_master)` -> `m_UDPClient == m_UDPConnection`), making `SetPlayerPortIP`
  inherently safe. Fix: feed+keepalive emit via `UDP_Send(MVAS_LOGIN_PORT)` on
  `m_Listen_Socket` -- the same single socket the proxy receives S->C on -- and
  position-only (<=28 bytes) so the server applies no phantom velocity. No server
  or wire change; the proxy emit socket is the only thing that moved.
  **Owner: retest -- with the engine read wired, confirm chat/nav-select/target/
  warp ALL keep working while flying under thrusters.**
- **Single-socket fix was NECESSARY but NOT SUFFICIENT (2026-06-09, second pass)**:
  after the single-socket revert the owner still reported chat/target/warp broken
  with the in-client DLL feed active. Instrumented run (proxy `PB-2: feed ...`
  + server `MVAS feed ...` diagnostics) proved the wire+server half is CORRECT:
  feed fires with SANE engine coordinates and the server applies them with
  `jump 0.00` (fed-pos == server-pos). So position-value, EISCONN, and a total
  S->C stall are all ruled out by measurement (S->C kept arriving at the proxy,
  ~12 datagrams/2s, after the feed started). Decisive code finding that exonerates
  the SERVER: the feed path ends in `SendLocationAndSpeed(false)` ->
  `SendToVisibilityList(include_player=false)` (PlayerClass.cpp:2095,3112), which
  sends position only to OTHER players' range lists and skips `p == this` -- so the
  server emits ZERO server->client traffic to the MOVING client in response to the
  feed. The server therefore cannot be the cause of the moving client's
  chat/target/warp breaking. Remaining suspects: (a) a proxy socket mismatch
  (feed source port != the socket whose RecvThread relays S->C), or (b) the
  injected client DLL itself (memory read / loopback send) destabilizing
  client.exe. Added per-socket proxy diagnostics (feed source bind-port + per-
  instance S->C rx bind-port) to settle (a) empirically on the next run.
  (Those temp diagnostics have since been removed -- see the OWNER-CONFIRMED
  note at the top of this entry.)
- **ROOT CAUSE FOUND + FIXED (2026-06-09, third pass)**: per-socket bind-port
  diagnostics proved a two-plane tug-of-war over the server's single S->C delivery
  address. Sector S->C arrives from server:3806 on the proxy's GLOBAL plane socket
  (`m_UDPGlobalClient`, unconnected, e.g. bind-port 60786) -- that is the only
  socket whose RecvThread relays sector data to the TCP client, and the server
  pins delivery there because `m_Player_Port` is captured from the avatar-login
  source on the global plane (UDP_Global.cpp). BUT `FixedClientComm()`
  (UDPClient_linux.cpp) ALSO started the feed/keepalive on the SECTOR/master plane
  socket (`m_UDPClient`, connect()'d to UDP_MASTER_SERVER_PORT 3808, e.g. bind-port
  41155). Each `0x1004`/`0x3005` the server receives calls `SetPlayerPortIP(source)`
  (UDP_MVAS.cpp:103,149), so the 200ms position feed emitted from 41155 repeatedly
  STOLE the S->C delivery mapping from 60786 to 41155 -- a socket connect()'d to
  3808 that kernel-drops the server's 3806 stream AND whose RecvThread does not
  relay to the client. Result: all S->C (chat/target/warp, position-independent
  included) black-holed seconds after the feed started. The latent bug predates
  PB-2 (sector-plane 30s keepalive vs global 30s keepalive = slow, mostly-benign
  tug-of-war); the 200ms feed amplified it into constant breakage. Empirical proof:
  `PB-2: feed 0x1004 ... FROM my-bind-port=41155 (connected-peer)` vs
  `PB-2: S->C rx 17/2s on MY-bind-port=60786 (unconnected) last src_port=3806`.
  Fix: remove `StartMVASKeepalive()` from `FixedClientComm()` so the feed/keepalive
  runs ONLY on `m_UDPGlobalClient` (started at the sector LOGIN handler,
  ClientToServer_linux_stubs.cpp:509) -- the socket that owns S->C delivery. No
  server or wire change. **Owner: retest -- chat/target/warp should now all keep
  working while flying under thrusters with the feed active.**
- **What to look for**: with thrusters you should now travel and STAY where the
  client shows you -- pressing spacebar/stop, or starting autopilot/warp, must NOT
  snap you back to a stale position. Confirm you can move off a planet/station and
  warp normally. Sanity-check the proxy emits `0x1004` to udp/3806 only while
  actually moving (change-gated; quiet when stationary), matching the live capture
  cadence. If position still diverges, the engine read offsets are wrong -- the
  wire half is already byte-pinned, so debug `ReadEngineShipState`, not the proxy.

### [ ] CV-MVAS-ORIENT -- Manual weapon fire works while orbiting a target (firing-arc uses client nose)

- **Bug**: fly up to an enemy, circle it for 10s while pointed straight at it, press
  the weapon key (`1`) -- nothing fires. Press spacebar (auto-follow) and it fires
  instantly. Not retail-matching; a real bug.
- **Root cause**: the beam/projectile firing-arc gate (`Equipable::CheckOrientation`,
  `server/src/CMobEquippable.cpp`) measures the target bearing against
  `Object::GetAngleTo`, which derives "facing" from the velocity DIRECTION
  (`Heading()` = `m_Position_info.Velocity`). While orbiting, velocity is TANGENTIAL
  (~90 deg off the line to the target) even though the ship's NOSE is on-target, so
  the `< PI/4.5` arc never closes and manual fire never arms. Auto-follow makes the
  server steer the nose onto the target, which re-aligns velocity with facing -- which
  is why spacebar fires immediately. The client's TRUE nose direction was available
  (it already flows to the proxy in the position-feed datagram) but was being dropped.
- **Change**: the proxy now sends the MVAS `0x1004` heading triple (36-byte payload,
  server heading branch) instead of position-only (`proxy/UDPClient_linux.cpp`
  `SendPositionIfChanged`). The server stores the normalized nose vector in a new
  DECOUPLED facing field (`Player::m_ClientHeading`, set in `UpdatePositionFromMVAS`)
  used ONLY by `CheckOrientation` via `Player::GetClientHeadingAngleTo` -- it never
  touches `m_Velocity` / `m_Position_info.Velocity` / the broadcast, so it CANNOT
  reintroduce the CV-MVAS-POS phantom-velocity warp regression. This is deliberately
  NOT the earlier `SetVelocityVector(heading)` path that broke warp.
- **Client-side SECOND fix (2026-07-03) -- the "same problem" cause**: after the
  server/proxy change the bug persisted because the position feed (`enbmod.dll`,
  `freya/client-injection/enbmod/src/lua_api.cpp` `try_read_transform_heading`) was
  reading the ship's world transform from the WRONG offset (`*(obj+0x14)+0x48`), which
  on the live client is a static near-identity matrix with translation `(-36, 0, -7)`
  -- ~160k units off the real ship position. The feed's own translation-validation
  (`dx^2+dy^2+dz^2 <= 4`) correctly REJECTED it, so it published heading `{0,0,0}`;
  the server then saw `mag==0`, never set `m_HaveClientHeading`, and fell back to the
  velocity-based `GetAngleTo` -- reproducing the bug exactly. Proven live via the
  enbmod cmd channel: the CORRECT world transform is `*(obj+0xac)+0x1c` (obj =
  `targeting_data_obj()`), whose translation column read `(-83605.6, 137262.1, 931.6)`
  EXACTLY matching the vtable world_pos getter, and whose X (nose) column swung from
  `(-0.687,-0.722,0.085)` to `(0.984,-0.125,-0.125)` while the ship turned onto the
  target under auto-follow (translation tracking the flight). `try_read_transform_heading`
  now walks a candidate list `{(0xac,0x1c),(0x14,0x48)}`, each still translation-validated,
  and publishes the X column of the first that resolves -- so a wrong offset can only ever
  yield a neutral heading, never a wrong facing. enbmod DLL rebuilt (`just build-enbmod`).
- **Primary source**: the retail `0x1004` heading is a UNIT orientation vector (the
  nose / X-axis basis), magnitude 1.0 -- proven by the capture fixture
  `mvas_send_position_full` (heading `-0.864, 0.289, 0.412`, |h| == 1.000), pinned
  by `MvasRecordDecodeTests.MvasSendPosition_Heading_IsUnitOrientationVector_NotVelocity`
  and the 36-byte wire encoding by `MvasClientTests.BuildDatagram_WithHeading_*`.
- **Why the CLI/integration suite does NOT close this**: the CLI proves the wire
  FORMAT and that the server accepts the heading variant, but there is no headless
  combat/arc path -- only the real client, orbiting a live mob, exercises
  `CheckOrientation`. That is what this entry verifies.
- **Setup (owner)**: `just play-local` (position feed already enabled). Undock, find
  an enemy, fly circles around it while keeping the nose pointed at it.
- **What to look for**:
  1. Pressing `1` (or any beam/projectile weapon) FIRES while orbiting-and-facing,
     without needing spacebar/auto-follow first.
  2. Warp STILL engages normally (the decoupled facing field must not pin a phantom
     velocity -- this is the CV-MVAS-POS regression this design specifically avoids).
  3. Target LOCKS stay stable while the feed is active.
  4. Missiles (arc-exempt) behave unchanged.

### [ ] CV-CAP-1 -- `/capitalize` flips the class-suffix case and persists (Phase AO)

- **Change**: new non-retail player command `/capitalize` toggles the case of the
  trailing 2-letter class suffix (TE TS TT JE JS JD PW PS PP) on the caller's own
  avatar name and persists it through the server-authoritative full-DB save
  (`Player::SaveDatabase()` -> `SAVE_CODE_DATABASE`). `server/src/PlayerConnection.cpp`
  case 'c'.
- **Why the CLI/integration suite does NOT close this**: the command alters no
  retail wire packet (it is a server-internal name edit on the caller's own
  `m_Database` routed through an existing save opcode), so there is no byte to pin.
  The remaining unknown is purely cosmetic-client: whether the rendered nameplate
  actually reflects the new case after relog, and whether the change survives a
  full session + logout/login round-trip.
- **Setup (owner)**: log in a character whose name ends in a class code (e.g.
  `BobTE`). Type `/capitalize`.
- **What to look for**:
  1. Server replies "Class suffix toggled: your name is now `Bobte`. Log out and
     back in to see it update everywhere." (lower-case the second time -> `BobTE`).
  2. Log out and back in. The nameplate / character-select shows the toggled case.
  3. Run it on a name that does NOT end in a class code -> "Your name must end in a
     class suffix (...)" and the name is unchanged.
  4. Confirm it does not affect any other player's name and cannot be aimed at one.

### [ ] CV-NET7GO-1 -- Real client logs in + self-updates through the net7go relay (net7ssl cutover)

- **Change**: the C++ `login` (net7ssl) TLS auth server is removed from every
  deploy path. Game-auth is now served by `net7go` (`login-server/net7go`, a CC
  BY-NC-SA Go reimplementation that speaks PLAIN HTTP on :8085) sitting behind
  `freya-online`, which terminates TLS on :443 and RAW-RELAYS the legacy auth
  URIs (`/AuthLogin`, `/SectorServer`, `/Certificate`, `/updateCheck`) to net7go,
  copying net7go's hand-framed response bytes back to the client verbatim
  (`freya/online/server/legacy_proxy.go`). Touches docker-compose.yml, the
  prod compose (`deploy/do/compose/docker-compose.prod.yml`), the justfile
  launch recipes, and the deploy build/push pipeline + cloud-init cert hook.
- **Why the CLI/integration suite does NOT close this**: the suite hits the
  same host:4443 endpoint, so it exercises the relay path -- but it uses
  CliClient, not client.exe. The bytes are byte-pinned by
  `legacy_proxy_test.go` (TestRelayIsByteExact: no Date header, `AuthServer/2.5`
  token, exact framing) and net7go's own auth/ticket tests. What only the real
  Win32 client proves: that client.exe's TLS stack completes the handshake
  against freya-online's listener, parses net7go's relayed `Valid=TRUE` /
  ticket / certificate responses byte-for-byte the way it did net7ssl's, and
  that the issued login ticket is accepted by the game server (ticket-suffix
  validation, CV-14) end-to-end. Also: the launcher self-update `/updateCheck`
  round-trip (previously a C++ path, now net7go's manifest-backed handler).
- **Setup (owner)**: `just play-local` (boots postgres + server + net7go +
  freya-online + proxy; client AuthenticationPort=4443 -> freya-online). Log in
  a seeded account, create/enter a character, confirm you reach space/station.
  Then separately point the FreyaLauncher at a deploy with the patcher
  configured and confirm `/updateCheck` still reports UP_TO_DATE / triggers a
  self-update as before.
- **What to look for**:
  1. Login succeeds with no "EA.com temporarily unavailable (INV-300)" or TLS
     error (compare CV-19) -- the freya-online cert/handshake must satisfy the
     client exactly as net7ssl's did.
  2. Character select + enter-game work (the ticket net7go issued is accepted by
     the game server; no G_ERROR_TICKET_INVALID).
  3. The server's keepalive shows net7go as UP (the AF_UNIX Ping/pong on
     /run/net7-ipc) -- no "login DOWN" log on the server side.
  4. `/updateCheck`: an up-to-date launcher reports UP_TO_DATE; a stale one is
     offered the changed files and self-replaces (same as the old C++ path).

### [ ] CV-AQ-VAULT-1 -- Real client vault moves still work + survive a relog with atomic persistence (AQ-11)

- **Change**: `server/src/PlayerConnection.cpp HandleInventoryMove` (0x0027) now
  persists a vault move's slot writes (source + destination(s)) in ONE DB
  transaction instead of independent autocommit writes. The cargo->vault
  (1->3), vault->cargo explicit (3->1), and vault->vault (3->3) branches emit a
  single 2-record `SAVE_CODE_MOVE_INVENTORY` (`Player::SaveInventoryMove`) that
  `SaveManager::HandleMoveInventory` commits atomically via the new
  `sql_query_c::begin/commit/rollback` primitives
  (`server/src/db/sqlplus.{h,cpp}`).
- **AQ-11b (2026-06-11)**: the vault->cargo AUTO-STACK path (ToSlot == -1), where
  one vault stack can spread across several cargo slots, now ALSO commits
  atomically. The move message is variable-length (N records, capped at
  `SAVE_MOVE_MAX_RECORDS=15`): `CargoAddItem` optionally collects the cargo slots
  it touches and `Player::SaveInventoryMoveSlots` packs the emptied vault source
  + every touched cargo slot into one transaction. Player-visible behaviour is
  unchanged (same in-memory move, same Aux sends); only the persistence grouping
  changed. So this folds into the SAME real-client check below -- in particular
  the "drop on the vault / auto-pick" deposit/withdraw path now exercises the
  N-slot atomic move.
- **Why this is NOT a wire-format change**: no opcode, packet, or DB row
  content changed. The inbound 0x0027 layout is identical; the persisted rows in
  `avatar_vault_items`/`avatar_inventory_items` are identical values. The ONLY
  change is that the two writes now share a transaction, so a crash between them
  can no longer dupe or lose the item. The byte-level move parsing is already
  pinned by `SectorInventoryMoveTests.cs` (CV-16) and the crash-atomicity is
  pinned by the new `SqlplusWrapperTx.*` gtests. So the wire gate does not
  strictly apply; this entry exists only because the change touches inherited
  item persistence and item loss/dupe is high-stakes.
- **Why the CLI/integration suite cannot fully close it**: the suite proves the
  transaction primitive commits/rolls back correctly and that legit vault moves
  round-trip, but it cannot simulate a real server crash in the microsecond
  window between the two slot commits, nor exercise the real client's full
  deposit/withdraw UI (drag-to-vault auto-slot, stack splits, multi-deposit).
- **What to look for (real client)**: with a populated cargo + vault, deposit and
  withdraw items every way the UI allows -- drag a specific cargo item onto a
  specific vault slot, use the auto-pick "drop on the vault" path, move items
  vault->vault, split stacks. Every move sticks. Then LOG OUT and back IN: no
  item is duplicated (in both the source and dest slot) and none is lost (in
  neither). The server log shows NO "HandleMoveInventory: ... rolled back" or
  "could not begin transaction" line during legitimate play.
- **Setup**: any deploy; a character with items in cargo and vault, docked at a
  station so the vault is accessible.

### [ ] CV-21 -- Can buy more than one item per transaction at a vendor (vendor slot shows a full stack, not 1)

- **Change**: `Player::NPCTradeItems` (server/src/PlayerConnection.cpp) now sets
  each presented vendor slot's `StackCount` to the item's `MaxStack()` (clamped
  to >= 1) instead of the hardcoded `1`. The buy handler (case 4 in
  HandleInventoryMove) was already correct -- it honours `InvMo.Num` and caps the
  purchase by credits (`Cost = Source.Price * InvMo.Num`) and cargo space
  (`CargoAddItem`). Nothing else changed.
- **Why it was wrong**: the client's buy-quantity slider is bounded by the source
  stack it drags from. With every vendor slot presented as a stack of 1, the
  slider was pinned at 1 -- the reported "can only buy 1 bullet at a time" bug.
  The item's true `MaxStack` (200-900 for bullets) already reaches the client in
  the ItemBase template (that is why ammo stacks to 200 in cargo), so the slider
  being capped at 1 proves it is bounded by the per-slot stack, not the template
  -- hence raising the slot stack to `MaxStack` is the fix. Vendor stock is
  effectively infinite (`starbase_vender_inventory.quanity == -1` for ~all rows),
  so a full-stack presentation is correct.
- **Evidence gap CLOSED 2026-06-15.** The live-retail capture this entry lacked now
  exists: cap1 (purchaseammo-stack-single-sell, cleartext proxy<->server against the
  retail reference server). It shows the retail vendor buy uses `0x0027`
  INVENTORY_MOVE (NOT a separate trade opcode), and crucially a STACK buy carries
  `Num=100` (`FromInv=4 vendor, ToInv=1 cargo, ToSlot=-1`) -- i.e. retail's client
  DID let the user request a stack of 100, which is only possible if the retail
  vendor slot was presented as a stack (slider 1..stack), exactly the behaviour this
  fix restores. Single buys carry `Num=1`. So the earlier Net-7 `Num==1` capture was
  the inherited bug (or just a 1-qty purchase), and retail confirms multi-qty buys.
  The CLI `InventoryMoveRecord` decodes all six BE fields; cap1 validates it. The
  remaining open item is the real-client check below (does OUR slider now run
  1..stack after the NPCTradeItems fix is deployed) -- PB-3/PB-6 in plans/41.
- **What to look for (real client)**: dock, open a vendor that sells ammo
  ("bullets"), drag an ammo item toward cargo: the quantity slider must now run
  1..stack (not be stuck at 1). Buy several (e.g. 100). Cargo receives the full
  amount, credits drop by `unit price * quantity`, and an over-credits or
  over-cargo request is refused with the existing "Insufficient credits!" /
  "Insufficient space in cargo hold." message. Confirm the same for trade goods
  (category 90) and ordinary stackable items.
- **Setup**: any deploy; a character docked at a station with a vendor, holding
  enough credits and free cargo to buy a multi-item stack.

### [ ] CV-22 -- Formation commands arrange the group (CLI emits 0x00BC CTA_REQUEST)

- **Change**: NO server/proxy wire change. This is a new CLI emitter only: the
  `formation <pipe|block|slot|join|break>` command sends `0x00BC` CTA_REQUEST
  (struct CTARequest: int32 SourceID, TargetID, Action; 12 bytes LE) with the
  GroupAction codes the server's `PlayerManager::GroupAction` already dispatches
  -- 6 pipe / 5 block / 4 slot-back (leader SetFormation), 7 form-up (member
  FormUp), 8 leave-formation (member), 9 break-formation (leader). SourceID is
  our own tagged GameID; the server strips PLAYER_TAG in GetPlayer. The server
  handler is inherited and unchanged. Logged here because the CLI cannot observe
  ships physically snapping into formation -- only the real client proves the
  feature works end to end.
- **CLI byte-pin**: `CtaRequest_IsByteExact` in
  `freya/cli-client/tests/CliClient.UnitTests/Opcodes/SelfGroupStateTests.cs`
  pins the 12-byte emitter; `SelfGroupStateTests` pins the self-group-state
  decode (PlayerIndex 0x001B GroupInfo) that drives command gating.
- **What to look for (real client)**: form a group (one client leads, others
  accept). On the leader, run `formation pipe` (then `block`, `slot`) -- the
  group's formation indicator changes. On a non-leader member, `formation join`
  snaps the ship into the leader's formation (needs same sector, within 5000 of
  the leader). On the leader `formation break` ends the formation for everyone;
  on a member `formation break` drops just that ship out. Cross-check that the
  CLI's offered verbs match the role: a leader is offered pipe/block/slot/break,
  a not-yet-formed member is offered join/break, and `formation` is hidden when
  solo (not grouped).
- **Setup**: `just play-cli` (or `play-local`) with at least two characters in
  the same sector, grouped via `group invite <player>` + `group accept`.
- **Wire side now capture-confirmed (2026-06-15)**: live-retail group-play captures
  (cap7/cap8 group+target+formation, cap9/cap10 group disband) show the server's
  `0x00BD` CTA_RESPONSE simply echoes the request's GroupAction selector in field@4
  (4=Slot Back, 5=Block, 6=Pipe, 7=Form Up, 8=Leave, 9=Break, 12=Request Target)
  with field@8=Success -- exactly what our inherited handler emits. This RETIRES the
  earlier (false) "field@4 must be in {13,14,15,17} or the client rejects it / beacon
  feature broken" theory (see plans/26 Z-1, now resolved as not-a-divergence). So
  this CV is narrowed to the purely VISUAL/physical check below (do ships snap into
  formation) -- the wire format is no longer in question.

### [ ] CV-23 -- Long-idle session can still zone (reliable-UDP resend protocol fix)

- **Change**: server + proxy, the 0x2016/0x2017 reliable-stream recovery path
  (proxy<->server control band; the client never sees these opcodes, but a
  failed recovery strands the client on the sector load screen). Server
  `Player::ReSendOpcodes` (server/src/PlayerConnection.cpp) previously parsed
  the 0x2017 request with SIGNED SHORT reads, so any request for a sequence
  number above 32767 (a few hours of play) went negative, matched nothing, and
  -- because the no-match path sent NO reply -- the proxy waited forever. Fixed
  to parse the canonical 8-byte `ReSendRequest {int32 packet_start, int32
  packet_count}` (now in common/include/net7/PacketStructures.h, with
  legacy 16-byte LP64-proxy tolerance), honour the count, and answer EVERY
  requested sequence: the cached datagram when the resend ring still holds it,
  a header-only blank when evicted. Ring deepened RESEND_ELEMENTS 20 -> 64
  (zone-out bursts evicted entries before the NACK arrived). Proxy side:
  emits the shared 8-byte struct (its old private `{long;long}` was 16 bytes
  on LP64 builds), guards header-only blank fills in the reassembly drain
  (previously a heap overread), prunes the reassembly map (previously grew
  unbounded all session), and -- the load-bearing half -- retries
  timer-driven via SO_RCVTIMEO + `PumpPacketResend` every ~300ms, because the
  old retry only ran on ARRIVAL of further datagrams and the post-handoff
  0x003A burst is the FINAL traffic on a zone-out, so one lost tail packet
  could never recover.
- **CLI byte-pin**: `ResendPacketSequenceCodecTests` in
  `freya/cli-client/tests/CliClient.UnitTests/Opcodes/` pins the 8-byte
  encode, both decode forms, and the >32767 sequence case; codec is
  `CliClient.Core/Opcodes/Outbound/ResendPacketSequenceCodec.cs`.
- **What to look for (real client)**: the long-session zone-out. Stay logged
  in for several hours (idle in a sector is fine -- sequence numbers accrue
  past 32768), then gate or undock. Previously this was the "stuck on load
  screen after sitting a while" hang (also reported on the online deploy);
  now the zone must complete. Also do a burst of rapid back-to-back gate
  jumps on a fresh session -- no load-screen stall. Watch the proxy log for
  "re-request ... (timer)" lines recovering lost packets and "blank fill for
  evicted packet" lines unsticking evicted ones.
- **Setup**: `just play-local` (rebuilt server + proxy images and a freshly
  built bin/FreyaProxy.exe -- both sides must be the new binaries, since the
  wire struct width and the blank-reply contract changed together). The
  online deploy needs the same rebuild before the friend's hang is fixed.

### [ ] CV-24 -- Repeated zone in ONE session does not hang (CV-23 resend pump scoped to post-login)

- **Change**: proxy only, a correctness fix to the CV-23 reliable-stream work
  (regression repair, not new wire behaviour). CV-23 added the timer-driven
  `PumpPacketResend` (SO_RCVTIMEO + ~300ms pump) so a lost tail of the
  post-handoff 0x003A burst could recover when no further datagrams arrive. But
  the pump ran unconditionally, so during the NEXT sector login on the same
  single-client proxy it could build a bulk resend NACK from the PREVIOUS
  session's leftover reassembly residue (a late datagram landing in the new
  window before its seq-0 reset). The server then flooded header-only blank
  fills that raced `m_CurrentPacketNum` past the new login's real stage packets,
  which then dropped as "already processed" -- so the SECOND sector login in one
  session hung on the load screen forever. Fix: gate `PumpPacketResend` on
  `m_LoginComplete` (false from sector LOGIN 0x0002 until START_ACK 0x0006, i.e.
  exactly the handshake window) so the timer pump is dormant during any login
  and only runs in the established post-login steady state it was built for; the
  arrival-driven recovery in `SendPacketSequence` still runs during login,
  matching pre-CV-23 behaviour. Companion fix: reset `m_UDPClient`'s
  `m_LoginComplete` at sector LOGIN in lockstep with `m_UDPConnection` /
  `m_UDPGlobalClient` (it was the odd plane out -- set true at START but never
  reset, leaving its pump gate stuck open across a re-join).
  `proxy/UDPProxyToClient_linux.cpp` (the gate), `proxy/ClientToServer_linux_stubs.cpp`
  (the reset).
- **CLI byte-pin**: no new packet format -- the gate changes WHEN an existing
  packet is emitted, not its bytes. Pinned behaviourally by the
  `*HandoffFollow` integration tests, which each do a second full sector login
  (follow re-join) on one proxy: `SectorUndockHandoffFollowTests`,
  `SectorGateHandoffFollowTests`, and `SectorMvasMoveTests` (its follow leg).
  All three pass on a fresh stack; before the fix the follow login hung.
- **What to look for (real client)**: in ONE session (no relog), zone twice or
  more -- e.g. undock, then gate, then gate again. Each subsequent zone must
  complete; previously the second could stick on the load screen. The CV-23
  long-idle and rapid-burst checks still apply.
- **Setup**: `just play-local` (rebuilt proxy image + freshly built
  bin/FreyaProxy.exe -- this is a proxy-only change, but both the docker proxy
  and the WINE-side FreyaProxy.exe must be the new binary).

### [ ] CV-AS-STATE -- Freya HUD shows ONLY in space/station, never on login/charsel/load

- **Change**: client-only (enbmod.dll, Freya/MIT -- no server/proxy wire
  change). The HUD is visibility-gated on `enb.state()`, which now has TWO
  sources (lua_api.cpp `l_state`):
  1. the calibration-driven game-state int (`game_state_addr` + `state_space/
     station/login/charsel/load` in game.h) -- the full state set, but currently
     UNCALIBRATED (`game_state_addr == 0`), so it names nothing yet;
  2. a NEW zero-calibration **in-space heartbeat** -- a read-only hook on the
     per-frame in-space vitals VALUE updater (`game.h::addr::EnergyBar`
     0x005dc4a0, installed via `enb.enable_inspace()` from init.lua). That
     updater repaints every frame in space and not at all on the front-end / in
     station, so a fresh call stamp positively means "space". It can ONLY
     distinguish space-vs-not-space, so it upgrades "unknown" -> "space" and
     never names station/login/charsel.
  Net effect today: `enb.state()` = "space" when in space, "unknown" everywhere
  else. `freya_hud.vis()` maps space -> cards+hotbar, station -> cards only
  (owner ask), login/charsel/load -> nothing, unknown -> full HUD (so dev /
  pre-calibration still renders). init.lua also logs every `enb.state()` /
  `enb.inspace()` transition (one line per change) to enbmod.log to DISCOVER the
  real transitions the game fires.
- **Headless coverage**: `freya_ui_spec.lua` + `xp_overlay_spec.lua` pin the
  gating against the mock (station drops the hotbar; login/charsel/load draw an
  empty frame and pass input through). The EnergyBar hook is UNVERIFIABLE
  headless (like `patch_ret`): the mock fakes `enb.inspace()` off mock state. The
  real hook firing per-frame in space, and being silent everywhere else, is
  exactly this entry.
- **What to look for (real client)**: enbmod.log shows `inspace heartbeat -> true`
  and `state: unknown -> space` ONLY after entering space (not at login/charsel/
  load, not docked). In space, the full HUD appears (player + discipline cards +
  hotbar). On the front-end and docked, the heartbeat stays false. Walk
  login -> charsel -> load -> space -> dock and read the transition log to
  confirm the heartbeat tracks space entry/exit and to capture what other
  observable states the game exposes (for later calibrating `game_state_addr`).
- **Setup**: `just play-local` with `UseClientMods=true`; walk login -> charsel
  -> load -> space -> dock and tail enbmod.log.

### [ ] CV-AS-CURSOR -- Our mouse pointer draws ON TOP of the HUD, not under it

- **Change**: client-only (enbmod.dll). `enb.cursor(on)` (lua_api.cpp
  `l_cursor` -> overlay.cpp `set_cursor`/`draw_cursor`) draws a procedural arrow
  (no binary asset) after the display list, inside the D3D8 Present hook, mapping
  the screen cursor to client coords via GetCursorPos+ScreenToClient. freya_ui
  toggles it with HUD visibility (on in space/station, off when hidden).
- **Headless coverage**: `freya_ui_spec.lua` "cursor follows visibility" pins
  the on/off toggle against the mock; it CANNOT prove the real draw order /
  client-coord mapping.
- **What to look for (real client)**: in space, the pointer renders above the
  glass cards/hotbar (never hidden behind a panel), tracks the real cursor 1:1,
  and disappears on the login/charsel/load screens.
- **Setup**: `just play-local` with `UseClientMods=true`; move the mouse over
  the player card and hotbar -- the arrow stays visible on top.

### [ ] CV-AS-HIDE-VITALS / CV-AS-HIDE-XP / CV-AS-HIDE-SKILL -- native widgets suppressed cleanly behind the glass

- **Change**: client-only (enbmod.dll). The glass cards are translucent, so the
  native in-space stat bars / xp bars / skill buttons bleed through. `enb.patch_ret(addr [,pop])`
  (lua_api.cpp `l_patch_ret`) overwrites a function entry with a `ret` (0xC3, or
  0xC2 imm16 for callee-cleanup) to suppress the routine that draws each.
  **Turned ON by default (owner-directed 2026-06-13)** for the two pinned paint
  targets -- init.lua runs `enb.patch_ret(VitalsPaint)` + `enb.patch_ret(XpPaint)`
  at startup. This is ON ahead of confirmation: "an early ret hides the widget
  without breaking gameplay" is only provable against the real client, so this CV
  stays open and is the load-bearing check. If the client crashes on load,
  comment the two patch_ret lines in init.lua.
- **Addresses pinned by behavioural analysis (2026-06-13).** The hide targets
  are each widget's per-frame PAINT routine, NOT the constructor/updater. A
  painter is a small `void __fastcall(ECX)` that, per child bar, checks the
  gadget visible flag and calls the gadget paint primitive, then returns -- it
  touches no game state, so pop=0 (`0xC3`) and an early ret is clean:
  - **VITALS paint** = `enb.addr.VitalsPaint` 0x005dcae0 (paints hull/shield/
    reactor gadgets at controller +0x1c/+0x20/+0x24).
  - **XP paint** = `enb.addr.XpPaint` 0x0058cf60 (paints combat/trade/explore).
  The earlier candidates were WRONG and unsafe: VitalsBars 0x005dbfc0 and
  XpBars 0x0058c450 are CONSTRUCTORS; EnergyBar 0x005dc4a0 is the value-updater;
  SkillButton 0x00662dc0 is the skill CONSTRUCTOR. Ret-patching any of those
  leaves uninitialised pointers / frozen state and crashes -- do not.
- **Headless coverage**: the mock records `enb.patch_ret` calls but cannot
  execute the patch; correctness is entirely this entry.
- **What to look for (real client), PER widget** -- both are ON at startup, so
  this is a confirm-or-revert check, not an enable step:
  - **VITALS** (`VitalsPaint` patched): the native reactor/shield/hull bars are
    gone; the player card's bars remain; flight, targeting, and stat updates
    still work; no crash on load, zone, or undock.
  - **XP** (`XpPaint` patched): the native combat/trade/explore xp bars are gone;
    the discipline card remains; leveling/xp still functions.
  - **SKILL**: no clean ret-patch exists -- the skill gadget's render is fused
    with state mutation, so there is no standalone pure-paint entry. Hiding it
    needs a runtime per-gadget visible-flag write (clear the byte at gadget+0x60
    on the skill gadget once its live pointer is known), not a static patch. This
    sub-id stays open pending that runtime path; the constructor 0x00662dc0 must
    NOT be ret-patched.
- **Setup**: `just play-local` with `UseClientMods=true`. The vitals+xp hides
  apply automatically at startup. If the client crashes on load, comment the two
  `patch_ret` lines in init.lua and re-launch to bisect.

### [x] CV-AS-HIDE-COCKPIT -- all native bottom 2D cockpit chrome suppressed (SOLVED)

- **SOLVED (live, screenshot-verified, 2026-06-18) -- the draw-gate flag clear.**
  This supersedes EVERYTHING below it in this entry, including the "DEFINITIVE
  NEGATIVE" note (which was wrong: it concluded the dial/buttons could not be
  hidden because it was poking per-instance paint vtable slots, which are NOT on
  the actual draw path). The native chrome IS hideable, cleanly and reversibly.
  - **The whole in-space 2D HUD is ONE interface-scene object** (vtable
    `0x00aff1f4`), reachable at `enb.cockpit_ctrl() + 0x44`. It owns one intrusive,
    circular doubly-linked list of 2D widget render objects -- the list the
    per-frame draw pass walks. List layout: begin = `*(scene+0x40)`; the loop stops
    when the node pointer equals the sentinel `scene+0x3c`; `node next = *(node+0x04)`;
    `node+0x0c` points 0x10 bytes INTO the child object, so child base =
    `*(node+0x0c) - 0x10`.
  - **The draw gate:** each leaf draws only when `(*(uint*)(leaf+0x18) & 0x700) == 0x700`.
    Clearing ANY one of bits 0x100/0x200/0x400 makes the draw pass SKIP that widget.
    We clear bit **0x400**. This touches no vtable and no value field, runs no game
    code, and is fully reversible (re-raise the bit and it repaints). This is why
    SetVisible (the +0x2c bit), the +0x60 flag, mesh scale-zeroing, and per-instance
    paint-slot `vt_hide_paint` all had NO visual effect -- none of them is the bit
    the draw pass actually reads.
  - **Scope.** The same widget list also holds the chat window (a different leaf
    class, `0x00afbf28`) and a band of inactive leaves (`0x00b111a4`). We clear the
    hide bit ONLY on leaves of class `0x00b1327c` -- the native cockpit chrome. The
    3D cockpit border is a separate PIP scene list (scene+0x58 / +0x70) and is
    untouched. Chat and the 3D border survive; all native bottom chrome disappears.
  - **Side effect (disclosed):** class `0x00b1327c` ALSO covers the top menu bar
    (Chat/Group/Options/Help/Emote) and the top-right buttons, so those go too. If
    the menu bar must come back, scope has to tighten by screen position (per-class
    relative float fields -- fragile), not by class.
  - **Implementation:** `freya/client-injection/enbmod/scripts/mods/hide-ui/native_hud_hide.lua`
    re-applies the clear every tick (`enb.on_tick`) because the widgets are rebuilt
    on sector/dock changes and value updaters re-raise the flag. Every pointer is
    range- + readable-checked and the walk is iteration-bounded (MAX_NODES=256);
    worst case it no-ops and the native UI shows. Screenshot-verified on the real
    WINE client.
  - **Per-element API (2026-06-18).** The chrome panels are named and exposed via
    `enb.hud.{hide,show,toggle,set,hide_all,show_all,list,names,dump}`; a modder
    addresses a panel by name. (The baseline is **16** persistent chrome leaves;
    see CV-AS-HUD-INVENTORY for the tail-anchoring fix to ordinal drift.) Findings
    (all verified live, so nobody re-walks them):
    the PANEL is the atomic unit -- it paints its own children and ignores per-child
    draw gates (clearing a child gadget's 0x700 bits did nothing on screen), so
    sub-widget hiding needs a DLL draw-hook or mesh-scale (out of scope); panels
    carry NO name/id at a fixed offset so the key is tail-anchored position; the bits
    0x100/0x200/0x400 are Visible/Enabled/in-Layout and ALSO gate hit-testing, so
    hiding a panel can break 3D click-to-select (show it back if so). Default hidden
    set (owner-chosen): ActionBarIcons, LevelBarsAndMicroMenu, BottomHudChrome,
    ActionBarBackground, ChatFrameBackgroundAndScroll.
- **What to look for (real client, owner confirm):** in space, all native bottom
  chrome (the 3 black status text bars bottom-left, the 6 circular action buttons,
  the ^/v + `<<Warp` cluster, the green reactor / red hull / blue shield bars, and
  the bottom-right scanner + its 2 bars) is gone; the 3D cockpit border and the
  chat window remain; flight/throttle/warp still WORK (widgets are hidden, not
  destroyed); no crash on load/zone/undock/dock. The top menu bar + top-right
  buttons also vanish (known side effect above).
- **Setup:** `just play-local` with `UseClientMods=true`, hide-ui mod enabled.

---

## [ ] CV-AS-HUD-INVENTORY -- inventory equipment view + loot button no longer clobbered by hide-ui

- **What was wrong.** The in-space HUD scene list is NOT static: opening a window
  inserts chrome leaves (inventory +5, target +1, comm/portrait +2) which shifts
  later ordinals. hide-ui keyed its hides by raw ordinal, and apply() runs every
  tick, so whatever inventory/loot/equipment leaf happened to land on a "hidden"
  ordinal had its draw bit cleared permanently. That blanked the inventory
  **equipment frame** ("shows my ship but not engine/reactors/weapons/etc") and
  dropped the **loot button** when a target was selected.
- **Ground truth (verified live, raw scene walks).** Baseline is **16** persistent
  chrome leaves. Every transient window inserts its leaves at the FRONT, ahead of the
  persistent block (inventory +5 before it, a target +1), so the persistent panels
  are always the LAST 16 chrome leaves with ActionBarBackground last. The inventory
  equipment paperdoll is one of the transient FRONT leaves -- a DIFFERENT object from
  `ActionBarIcons` (the native ability glyph by hotbar slot 1), proven by diffing the
  chrome list across an inventory open: ActionBarIcons keeps its exact address+flags
  while inventory is open. They were never the same leaf.
- **The fix (Lua only, no DLL/server change).** `native_hud_hide.lua` rewritten to
  **tail-anchor**: key each of the 16 persistent panels off the END of the list
  (`leaves[count - #ELEMENTS + k]`), immune to ordinal drift and to a target selected
  when the mod first runs. `ActionBarIcons` is hidden unconditionally -- the old
  "restore it while a modal is open" hack (from a wrong 17-leaf theory that also
  broke load timing) was both unnecessary (equipment is a separate untouched leaf)
  and the cause of the ability glyph FLASHING back on inventory open. Verified live
  via `enb.hud.dump()` draw bits in space (front=0), with a target (front=1), and
  inventory-open (front=5): ActionBarIcons stays hidden throughout; equipment leaves
  keep their draw bit set. Screenshot-confirmed clean action bar (no glyph by slot 1).
- **What to look for (real client, owner confirm):** in space with hide-ui on, open
  inventory (`i`) -> the ship paperdoll shows engine/reactor/shield/weapon equipment
  slots (not just the bare ship), with NO ability-glyph flash on the action bar, and
  the native ability glyph stays hidden by hotbar slot 1; select a target -> the loot
  button is visible on the target frame; the 5 default-hidden cockpit-chrome panels
  stay hidden throughout. No crash on open/close/zone.
- **Why CLI cannot prove it:** purely client-side 2D widget draw-gate state; no wire
  traffic. (Draw-bit state machine-verified here; the equip-view pixels are the
  owner's eyeball check.)
- **Setup:** `just play-local`, `UseClientMods=true`, hide-ui enabled.

---

**SUPERSEDED HISTORY (kept for the record -- all conclusions below are WRONG or
moot; the SOLVED block above is the truth):**

- **UPDATE (live session, 2026-06-18) -- `vt_hide_paint` PROVEN, supersedes the
  "promising-but-unvalidated" note below; commit e46793a1**:
  - **The per-instance vtable-copy hide WORKS and is reversible.**
    `enb.vt_hide_paint(gadget, slot, pop)` no longer patches the shared class
    vtable (that is what wedged the client on 06-17). It allocates a PRIVATE copy
    of just that one instance's vtable, replaces the paint slot with a bare
    `ret <pop>` stub, and repoints the instance. Only that instance stops painting;
    every other gadget of the same class is untouched, input/state are untouched,
    and `enb.vt_unhide()` restores all hidden instances. No crash across multiple
    relaunch/probe cycles via the login-to-client skill.
  - **The profiler is now safe via the same copy trick.** `enb.vt_profile(gadget)`
    copies the vtable into counting+forwarding stubs (kVtSlots=64) on a private
    copy, so the 06-17 "spinning at ~874% CPU on a hot slot" wedge does not recur.
    `enb.vt_dump()` reports per-slot call counts; `enb.vt_restore()` reverts.
  - **Paint slot is CLASS-SPECIFIC (measured live, not from any external source):**
    the cockpit-panel controller paints via vtable **slot 24**; the vitals and xp
    controllers paint via **slot 4**. All three pop 0. `vt_hide_paint` on those
    three cleanly removes the native vitals + xp readouts and the cockpit panel's
    painted children.
  - **SetVisible(+0x40) confirmed ineffective again** -- consistent with the 06-17
    correction below. The working hide is paint-slot suppression, full stop.
  - **DEFINITIVE NEGATIVE (live, 2026-06-18): the dial + action buttons CANNOT be
    hidden by `vt_hide_paint`.** Investigation, all proven live this session:
    - The throttle/dial controller is `cockpit_ctrl()` = 0x3B66B10 (ctor 0x0057DD20
      per decomp, vtable 0x00AF0CFC). It owns the dial gadgets (UI_CPIT_WARP/
      THROTTLE/REVERSE/LEFT/RIGHT at controller fields +0x2d..+0x31) AND the
      UI_POWER vitals gadgets AND the UI_CPIT_*_BUT action buttons. `enb.cockpit_ctrl()`
      DOES capture it (returns 0x3B66B10, non-zero) -- the earlier "ctor 0x0057BE50
      never fires / returns 0" note was about a different (commands) label and is
      moot: the dial's owner is captured fine.
    - Hiding the owner's per-frame paint slot (profiled: slot 24 -> 0x581670, the
      only per-frame slot, count ~222) did NOTHING on screen.
    - The dial gadgets are referenced by a second object 0x76444D8 (vt 0x00AEAA20)
      at +0x20, but that object's vtable is NEVER called per-frame (empty profile)
      -- it is a layout/registry holder, not a painter.
    - The leaf gadgets self-report per-frame on vtable slots 12/24/43 (WARP vt
      0x00AFBB58, slot 24 -> base-class 0x00407496). `vt_hide_paint(g, 24)` on ALL
      11 cockpit leaf gadgets (UI_CPIT_* + UI_COMMANDS: MAP/CHARACTER/SHIP/OPTIONS/
      X_SPECIALS buttons + WARP/THROTTLE/REVERSE/LEFT/RIGHT dial) changed NOTHING.
    - **Conclusion:** instance-vtable-repoint only intercepts CUSTOM immediate-mode
      controllers the engine calls through the live instance vtable each frame (the
      vitals controller vt 0x00AF5D70 slot 4 -- proven to hide the bars). STANDARD
      framework gadgets (dial, throttle, buttons) are drawn by the gadget render
      PASS, which does not dispatch through the per-instance vtable slot we repoint,
      so the technique has no effect on them. SetVisible(+0x40) and the +0x60 flag
      are also ineffective (above).
    - **Path forward (not done, needs deeper RE):** intercept the gadget framework's
      render pass itself and gate per-gadget on a flag the renderer actually reads,
      OR scale-zero each gadget's fill/quad mesh (the only proven visible removal for
      standard elements, per the QUAD vtable note above) -- a per-gadget-type effort.
      Until one of those lands, the dial + action buttons stay visible and the Freya
      glass overlay must coexist with them. This CV stays open.
- **UPDATE (live session, 2026-06-17) -- supersedes the gadget+0x60 plan below**:
  - **+0x60 is NOT a usable hide.** Live test: clearing the low byte of +0x60 on
    all three vitals gauges changed NOTHING on screen (pixel-identical before/after).
    +0x60 is an "Exists/has-buffer" flag set once at construction; clearing it can
    trip an `_Exists` assertion and disables update logic broadly, not just paint.
    Abandoned.
  - **CORRECTION -- SetVisible (the +0x2c visible bit) does NOT visually remove
    these widgets.** This retracts the "SetVisible is the clean hide" claim made
    earlier the same day. Proven live: registering a PER-TICK `gadget_set_visible(g,
    false)` on the JEFIVE ship-vitals bars (`UI_POWER_ENERGY/SHIELDS/HULL_0`) and on
    the cockpit buttons (`UI_CPIT_SHIP_BUT_0` etc.) flips the +0x2c bit 0x20 to 0 but
    the panel still renders unchanged (screenshots pixel-equal). The tell: the
    `<<Warp` text and the `UI_CPIT_WARP_0` gadget keep drawing while reading vis=0.
    These widgets are painted by their CONTROLLER's per-frame paint, which ignores
    the child gadget visible bit. `enb.hide_cockpit()` returns "hid 12" and changes
    NOTHING on screen -- same reason. **So commit c4b232d2's SetVisible-based hides
    of the xp/discipline and throttle clusters are very likely visually
    ineffective** (could not re-verify the xp class 0xAF15EC before the client wedged
    -- see below; treat as unconfirmed, NOT done). Only the original fill-mesh
    SCALE-zeroing (QUAD vtable 0xb10f24, scales +0x10/+0x50) is a proven visible
    removal, and only for fill bars -- not frames, text, buttons, dials.
  - **NEW capability found: gadgets carry their UI name C-string at instance +0x24**
    (verified live: `UI_POWER_SHIELDS_0`, `UI_EXPERIENCE_COMBAT_0`, `UI_CPIT_WARP_0`,
    `UI_SCAN_PREV_TARGET_0`, ...). A bounded heap scan of the gadget arena
    (~0x75E0000..0x7640000) enumerates them by name -- the way to TARGET specific
    native elements. (No reachable global GadgetManager: gadget+0x08 is the owning
    controller, not a manager; the decomp's manager-list offsets did not hold live.)
  - **"JEFIVE" is the player's SHIP NAME, not a panel** (found as the ship entity's
    name string). So `enb.vitals_ctrl()` (@runtime vt 0x00af5d70) IS the ship-vitals
    panel controller; its gauges at +0x1c/+0x20/+0x24 are the on-screen Hull/Shield/
    Energy bars.
  - **Promising-but-UNVALIDATED mechanism: `vt_hide_paint(vtable, slot)`** -- no-op a
    controller's per-frame paint vtable slot so the WHOLE element stops drawing
    regardless of the visible bit. This is the right shape (kills frame+text+bars+
    buttons at once). NOT yet proven: the slot index must be identified WITHOUT the
    profiler (see caution).
  - **CAUTION / INCIDENT: `vt_dump` / `vt_profile` wedged the client.** They install
    call-counting stubs on vtable slots; profiling the vitals controller vtable
    (0x00af5d70) left the client spinning at ~874% CPU with winedbg attached -- a
    hard hang, had to `kill -9` client.exe + relaunch + relogin. **Do NOT run
    vt_profile/vt_dump on a hot (per-frame) slot on a live session.** Identify the
    paint slot from the decomp instead, then `vt_hide_paint` it directly.
  - **STILL NOT HIDDEN (the user's "ALL of cropped 018" target)**: the JEFIVE ship-
    vitals panel, the numbered ability hotbar, the 4 skill "orb" buttons, the central
    reactor dial, the right radar dial, the `<<Warp` text. Next step: identify each
    owning controller's per-frame paint slot from the decomp and `vt_hide_paint` it
    (validated carefully, with `vt_restore` ready), OR scale-zero the fill meshes for
    the bar fills. Needs a live client (relaunch + relogin after the wedge).

- **Change**: client-only (enbmod.dll). The stock bottom-center cockpit widgets
  (the throttle / up-down / WARP cluster, and the "UI COMMANDS" action buttons)
  bleed through the Freya overlay that replaces them. Unlike vitals/xp, these have
  NO pure per-frame PAINT routine to ret-patch -- they are painted by a generic
  widget-tree walker -- so the clean `patch_ret` approach is unavailable. Instead
  the hide-ui mod clears each child gadget's engine visible flag (gadget + 0x60 --
  the same byte VitalsPaint and 42 other paint gates read to decide whether to
  paint a gadget) every frame while in space (`enb.hide_cockpit`).
- **Mechanism**: two read-only capture hooks on the cockpit CONSTRUCTORS record
  each controller `this` (ECX), the proven naked-trampoline pattern of the vitals/
  xp/rpg capture hooks -- `game::addr::CockpitThrottle` 0x0057dd20 (children at
  controller int-slots 0x2d..0x31: WARP/THROTTLE/REVERSE/LEFT/RIGHT) and
  `game::addr::CockpitCommands` 0x0057be50 ("UI COMMANDS", children from slot 0x2b,
  inclusive 0x2b..0x33). `enb.hide_cockpit` (lua_api `l_hide_cockpit`) walks those
  slots off the captured controllers and clears child+0x60. Every step is
  guarded (mem::ptr / mem::write skip anything not committed), so a wrong
  controller/slot can only no-op, never fault.
- **Reversible, runs no game code**: the only write is the guarded visible-flag
  byte clear -- no constructor/updater is patched and no game method is called.
  Because it is a per-frame clear (not a one-way ret-patch), disabling the hide-ui
  mod restores the stock cockpit the next frame (the engine repaints it). Per-tick
  rather than one-shot specifically because the game re-shows the throttle gadgets
  whenever throttle/warp state changes.
- **Decomp-verified, NOT live-verified -- needs RELAUNCH + the owner's eyes**: the
  constructor conventions (__fastcall ECX) and child slots were read out of the two
  constructors; +0x60 as the engine visible flag is proven for the vitals/xp gadget
  class but only *inferred* for the cockpit gadget classes (button/bar widgets
  built by different gadget ctors). The running session this was built against has
  the OLD injected DLL (`enb.hide_cockpit` / `enb.cockpit_ctrl` are nil), so it
  cannot be exercised until `just play-local` rebuilds + redeploys. If clearing
  +0x60 does NOT hide a given cockpit gadget class, the fallback is the game's own
  SetVisible -- each gadget's vtable +0x40 method called with 0 (observed in
  FUN_0057c1c0, the UI-commands hide-all) -- which definitely works but runs game
  code; escalate to it only if the flag clear proves insufficient on test.
- **NOT DONE -- moving the 4 bottom-left action buttons up**: the owner also asked
  to shift the bottom-left action buttons upward to clear the bottom-left glass
  card. The widget screen-position field offsets are NOT confidently located (the
  decomp does not expose an unambiguous SetPosition / X-Y field on these gadgets),
  so a position write would be a blind guess that risks corrupting the client. This
  sub-task is deliberately left unimplemented pending verified position offsets --
  do not ship a guessed offset.
- **Headless coverage**: none -- the mock has no cockpit controllers.
- **What to look for (real client, after relaunch)**:
  - In space, the stock throttle/up-down/WARP cluster and the bottom-center action
    buttons are gone; the Freya overlay's controls remain; flight/throttle/warp
    still WORK (the gadgets are only hidden, not destroyed); no crash on load/zone/
    undock/dock. `/run return enb.hide_cockpit()` returns a non-zero count once in
    space, and `/run return enb.cockpit_ctrl()` shows two non-zero pointers.
  - If a cockpit widget is still visible, +0x60 is not the gate for that gadget
    class -> escalate to the vtable-+0x40 SetVisible fallback noted above.
- **Setup**: `just play-local` with `UseClientMods=true`; rebuilds enbmod.dll.

### [ ] CV-AS-ACTIONBAR -- Freya hotbar fires the native action bar (dispatch half)

- **Change**: client-only (enbmod.dll + freya-hud Lua). A read-only ECX-capture
  hook on the in-space Use-Slot dispatcher (`FUN_006120e0`) exposes the live
  action-bar controller as `enb.actionbar()`; `freya_ui.lua` routes each Freya
  hotbar slot back through that dispatcher via `enb.call`, so clicking a Freya
  slot fires the exact native action. No server/proxy/wire change.
- **CORRECTED MODEL (2026-06-19, read from the dispatcher itself + verified live)**:
  the bar is SIX slots, not twelve. The native bar is two 3-box bank controllers
  (vt `0x00af7484`) 0x60 apart; bank0 = slots 1-3, bank1 = slots 4-6; each bank's
  three box pointers are at `+0x10/+0x14/+0x18`. The dispatcher is PRESS/RELEASE
  keyed, NOT primary/alternate: a bank's `+0x50` is the press-id base, `+0x54` the
  release-id base, for the SAME three boxes. Box k has press id `v50+k` and
  release id `v54+k` (k in 0..2). Press fires only when the box is idle
  (`box+0x6c == 0`) then sets it; release fires only when active then clears it.
  So replaying a keypress = read the box's live `+0x6c` and pick press-or-release
  exactly as the native keybind does. Calling a press id on an already-active box
  (or vice-versa) hits neither branch and the dispatcher returns 0 -- a silent
  no-op. That mis-pick (the old mapping computed ids OUTSIDE both ranges) is why
  the Freya bar never actually dispatched: keys 1-6 only looked right because the
  native keybind fired underneath them. The bar-toggle id (`v54+3`) advances a
  page index (`controller +0x4c`) that swaps the action bound to each of the same
  six boxes -- the player's "alternate bar" (their abilities 7-12) is that second
  PAGE, not six more live slots. Paging is a separate, unimplemented feature.
- **Why it is safe**: the dispatcher call is byte-identical to a native slot
  click / "1".."6" keypress -- same controller, same id, same per-slot toggle
  gating. We add no behaviour; we invoke the path the native UI invokes. The
  capture hook is the established read-only `pushal/push ecx/call/popal/jmp`
  trampoline (same shape as the rpg/xp/cockpit hooks), so it cannot alter state.
- **Verified live (this dev box, in space, beasleyte)**: dispatching the release
  id of an active toggle flipped `box+0x6c` 1->0, and re-pressing flipped it 0->1,
  via `enb.call` -- proving our path drives the real dispatch (no crash,
  `enb.inspace()` stayed true). The 6-slot mapping resolves to bank0 ids 2/3/4
  and bank1 ids 9/11 (press) + 13 (release, the one active toggle) -- all inside
  the dispatcher's valid ranges, matching the user's loadout slot order.
- **What to look for (real client, owner confirm)**: in space, clicking a Freya
  hotbar slot 1-6 fires the same weapon/ability as native "1".."6"; pressing
  physical 1-6 still fires natively (Freya slot just lights, no double-fire);
  toggle abilities turn on/off correctly; no crash on use/zone/undock.
- **NOTE**: a slot dispatches only after the bar has been touched once in space
  (any use captures the controller); before that, a Freya click falls back to
  tapping the native key (which fires it AND warms the capture).
- **Setup**: `just play-local` with `UseClientMods=true`, freya-hud enabled. The
  Lua is hot-reloadable (`reload()`); the capture hook needs the staged DLL.

### [~] CV-AS-ACTIONBAR-ICONS -- Freya hotbar shows each slot's real ability icon (icon half SOLVED for abilities; awaits a populated-bar real-client confirm)

- **Status**: NOT implemented. The dispatch half (CV-AS-ACTIONBAR) works without
  it; this entry tracks the remaining "show the native icon on the Freya slot".
- **Why it is non-trivial**: the ability icons are packed inside Westwood
  `.mix`/`.th6` archives (not loose files), and EA art cannot be redistributed,
  so loading a PNG from disk (`enb.draw.image`) is out. The correct route is to
  blit the LIVE game texture: the overlay already draws inside the game's D3D8
  Present hook with the game device, so a new `enb.draw.game_tex(tex, uv..., rect)`
  primitive that binds a game-owned `IDirect3DTexture8*` is feasible (no art
  redistribution, live-correct).
- **The open work**: locate, per slot, the icon gadget's texture pointer + UV
  rect in the gadget tree (the `ICON_BOX_%02d` / AbilityIcons gadgets resolved by
  name at action-bar setup). This is unfinished deep RE; it also needs a DLL
  rebuild + relaunch (an injected DLL cannot hot-swap).
- **Progress 2026-06-18 (pipeline PROVEN + wired; awaits a populated bar)**:
  - DONE: `enb.draw.texture_quad(texptr,x,y,w,h[,alpha])` -- the game-texture blit
    primitive (`K_TEXQUAD`, pointer-guarded), built + staged.
  - PROVEN live: captured the game's own font-glyph atlas texture by walking the
    gadget tree and blitted it with `texture_quad` -- the glyphs rendered on screen.
    So binding a game-owned `IDirect3DTexture8*` from memory and drawing it in our
    Present hook works end-to-end. (The earlier "no readable texture" conclusion was
    WRONG: there IS a stable live texture object; it just is not where the static
    address pointed and not via a draw hook.)
  - FOUND: the per-slot icon texture is reachable by a FIXED pointer chain off the
    captured action-bar bank -- slot gadget (bank `+0x10 + k*4`, k=0..2) `-> +0x64`
    ability-data `-> +0x64` icon gadget (vt `0x00afcd04`) `-> +0x24` image node
    `-> +0x34` = the live `IDirect3DTexture8*` (device ptr at `+0x18`, refcount at
    `+0x04`, vtable in the `0x7f______` band). No draw hook needed.
  - WIRED: `freya_ui.lua` `slot_icon_tex(i)` walks that chain per Freya slot and
    blits the icon onto the glass button; empty slots draw nothing. Live-reloaded,
    no crash. The dead `0x006a8760` "IconDraw" hook (fired 0 times -> wrong fn) and
    `enb.icon_src`/`enb.icon_stats` were removed; DLL rebuilt clean.
  - STILL TO VERIFY HERE: the dev character's bar is EMPTY (every slot = "Unused
    Slot" placeholder -> transparent texture), so a real icon has never rendered on
    the Freya bar. Confirm with the real client + a character that has an equipped
    weapon / trained ability that (a) the chain yields the correct icon art for a
    populated slot, and (b) the primary (1-6) vs alternate (7-12) icons are right
    (both currently resolve through the same slot gadget).
- **Progress 2026-06-19 (root-caused the "empty bar" + fixed the resolution chain)**:
  - The bar is NOT empty. With the Freya HUD hidden, slots 1/2/3 of the native
    circular weapon-group bar (bottom-middle, flanking the warp dial) hold the
    character's beam weapon (slot id = 85 at slot box `+0x68`, identical across all
    three). The earlier "every slot = Unused Slot placeholder" read conflated
    "no 2D icon" with "empty slot".
  - ROOT CAUSE of "no icon ever renders": a WEAPON group is drawn as a live 3D
    MODEL in the native bar (slot box `+0x30` display node -> `+0x8c` render node
    vt `0x00b11d88`: mesh `+0x0c` + materials vt `0x00aea230` -- no flat texture in
    the node or material). The slot's 2D icon atlas (box `+0x64` data `+0x80`
    embedded icon `+0x34`, AND the item-card path box `+0x64` -> `+0x64` -> `+0x24`
    -> `+0x34`) IS a valid `IDirect3DTexture8*` but renders fully TRANSPARENT --
    weapons have no 2D glyph. Proven by blitting both textures over a magenta
    background: magenta showed through 100%. So a beam-weapon slot correctly draws
    nothing; 2D glyphs exist only for activated abilities / devices. A weapon
    thumbnail would require re-rendering the 3D model (out of scope; the rejected
    "replicate 3D render" option). EA art cannot ship in the repo, so the disk-PNG
    route stays out -- the live game-texture blit is the only legitimate path, and
    it is correct as-is.
  - FIXED: `freya_ui.lua` `slot_icon_tex` was rooting on a FIXED HUD-scene offset
    chain (`{0x3c,0x2c,0x14,0x00,0x14,0x3c,0x34}` off `cockpit_ctrl +0x44`) that is
    session-unstable -- it broke at hop 3 (`+0x14 -> 0`) on this relaunch and would
    break on every relaunch. Replaced it with the controller-rooted chain the spec
    above already documents: captured action-bar bank (`enb.actionbar`/`ab_banks`)
    -> slot box (`bank +0x10 + k*4`, vt `0x00afac78`) -> `+0x64` -> `+0x64` ->
    `+0x24` -> `+0x34` (vt `0x7fc8d5a0`). Verified live: all three primary slots
    resolve to a valid d3d texture (transparent, as expected for a weapon).
    Re-resolved each frame, freed-safe; dead `hud_scene`/`follow_to_tex`/
    `HUD_SCENE_VT`/`SCENE_OFF`/`SLOT_ICON_CHAIN` removed. Reloaded clean, no crash,
    HUD intact.
  - 2D PLACEHOLDER for weapon slots (owner ask "use a 2d placeholder for weap"):
    since a weapon group has no 2D glyph, an empty square reads as "unfilled".
    `freya_ui.lua` now draws an ORIGINAL Freya placeholder (`weapon_icon.png`, a
    cyan energy-bolt we authored -- MIT mod art, NOT EA art, so it ships fine) on
    any occupied weapon slot, detected by the slot's 3D render node (display
    `+0x30` -> node `+0x8c` vt `0x00b11d88`). Ability/device slots keep blitting
    their real live 2D texture. Empty slots draw nothing -- emptiness is read from
    the bank fill count (`bank +0x34`), NOT the box's own vtable/id, because a
    bank always exposes three boxes and the unfilled ones keep stale vtable/id
    (id=85 lingered on bank1's empty boxes). New shared `slot_box(i)` resolver
    gates both the placeholder and the texture blit on that count. Verified live:
    bolts show on the 3 filled weapon slots (and their 7-9 alternate-fire mirror);
    the 6 empty slots stay blank; reloaded clean, no crash.
  - STILL TO VERIFY (unchanged): needs a character with a trained activated ability
    / a 2D-icon device slotted to confirm the chain yields the correct glyph art on
    the Freya bar, and the primary (1-6) vs alternate (7-12) split. Beam-weapon
    slots show the placeholder bolt (by design), not a real thumbnail.
- **Progress 2026-06-19b (tested on a populated bar -- SUPERSEDES the above on two
  points; with beasleyte: slot1 weapon, slots 2-6 abilities, all six filled)**:
  - SUPERSEDED -- weapon detection: the "3D render node `+0x8c` vt `0x00b11d88`"
    test flagged EVERY slot (all six had a 3D node + the lingering id=85), so it
    bolted every slot. The RELIABLE discriminator is the bound card's vtable: from
    a slot box, `+0x64` (slot-data gadget) `-> +0x64` (item card) vtable is
    `0x00afcd04` for a WEAPON group and `0x00afd664` for an activated ability /
    device. Verified live: only slot1 (the equipped beam weapon) read
    `0x00afcd04`; all five abilities read `0x00afd664`. `slot_is_weapon` now uses
    this; the bolt shows on slot1 only. The `RENDER3D_VT` / `box+0x30` display-node
    path and the `bank+0x34` fill-count gate are removed (occupancy is now
    `box+0x64 != 0`, which the dispatcher itself uses).
  - SUPERSEDED -- 2D ability glyphs: the documented `box+0x64 -> +0x64 -> +0x24 ->
    +0x34` texture chain CONVERGES on ONE shared `IDirect3DTexture8*` for all six
    boxes (same `+0x24` node `072b4d80` -> same texture), so it is NOT the
    per-ability icon -- blitting it would draw identical art on every slot. Removed
    `slot_icon_tex` + `D3DTEX_VT`; ability slots now draw NO glyph rather than a
    wrong/identical one. The per-ability 2D icon source is still unfound: it must
    hang off the per-slot item card (`08c55360`/`03b17fe0`/`06e51e90`, which DO
    differ per slot), but a scan of that card's first 0xc0 bytes found no texture
    pointer or name string. `texture_quad` (the blit primitive) stays -- it is
    proven and will be reused once the real per-slot texture is located.
  - STILL OPEN: locate the per-ability 2D icon texture (off the per-slot item
    card), and the page-toggle path for abilities 7-12. The weapon placeholder +
    the 6-slot dispatch are done and verified live.
- **Progress 2026-06-19c (keypress SWALLOW + native-glyph HIDE -- both SOLVED &
  verified live on beasleyte; supersedes the "ability glyph unfound" search above
  with the working icon-render route)**:
  - SWALLOW (the user's "you are NOT capturing the keypress" bug) FIXED + VERIFIED:
    `freya_ui.lua` now OWNS keys 1-6. On keydown it dispatches through our path and
    returns true (swallow) so the native keybind handler never runs; the matching
    keyup is swallowed only when we owned the down. Proof: read every box `+0x6c`,
    sent a real key (xdotool) -- the pressed slot's flag toggled 0->1 EXACTLY once,
    and 1->0 on the next press. A double-fire (native not swallowed) would toggle
    twice per press (net zero). One toggle per press == the key was captured and
    only our `enb.call` ran. If the controller is not captured yet, keydown falls
    through (native fires + warms the capture) and that cycle's up is not swallowed.
  - NATIVE GLYPH HIDE (the user's "2d icons from the native bar bleed through ...
    doesnt hide ANY hover states") SOLVED: each box owns its 2D glyph as a native
    chrome panel leaf (vt `0x00b1327c`) at `box +0x74`. The in-space HUD draw pass
    draws a chrome leaf only when `(flags & 0x700) == 0x700` at leaf `+0x18`;
    clearing the `0x400` bit drops it from BOTH the draw pass and the input/hover
    pass (the 0x700 gate is V|E|L and gates input too -- so the hover highlight dies
    with the icon). `hide_native_glyphs()` walks all six boxes from the captured
    controller and clears each `box+0x74` glyph every frame. Verified live: with our
    bar ON, every native floating icon (the spread-out yellow key / purple wrench /
    cyan / yellow "!" / arrow icons either side of the reticle) is gone; only the
    clean Freya 6-slot bar remains. This is why hide-ui's single named
    "ActionBarIcons" leaf only hid box 0 -- that leaf is `box0 +0x74` (the slot-1
    glyph); the other five boxes' glyphs are separate leaves it never named. Note
    these glyph leaves are NOT in the chrome tail-anchored ELEMENTS block (they sit
    in the list FRONT), and the box->glyph link (`+0x74`) is the stable, controller-
    rooted way to reach all six regardless of list position.
  - ICON SOURCE for #3 FOUND (route, not yet rendered): the `box+0x74` glyph leaf
    is the thing that renders the real per-slot 2D icon. Its slotted-item descriptor
    is at glyph-leaf `+0x8c` (a struct tagged "INVITEM"); that descriptor's `+0x20`
    is the item's AuxData property hashmap (vt `0x00aff304`). The icon texture/name
    is a property in that map. This is a cleaner lead than the earlier item-card
    `+0x24 -> +0x34` chain (which converged on one shared/transparent texture). Open
    work: traverse the INVITEM AuxData map for the icon property and blit via
    `texture_quad`. Until then ability slots draw no glyph (weapons draw the bolt).
  - STILL OPEN: render the real per-ability icon (see 2026-06-19d for why the
    INVITEM-AuxData route does not yield a blittable texture).
- **Progress 2026-06-19d (7-12 PAGE TOGGLE -- SOLVED & verified live; real-icon
  route re-assessed)**:
  - PAGE TOGGLE (the user's "7-12 toggle path") DONE + VERIFIED on beasleyte.
    `freya_ui.lua` adds `current_page()` / `toggle_page()` / `slot_label()` and a
    "PG" button at the right of the bar (`page_rect`). Clicking it flips BOTH banks
    together (per-bank toggle id = `u32(bank+0x54)+3` -> 8 for bank0, 15 for bank1)
    so all six slots page as a unit; the slot labels follow (1-6 <-> 7-12) and the
    button shows the page it switches TO. The six press ids are fixed across pages,
    so keys/clicks need no remap -- they fire whatever page is live. Verified: tapped
    the toggle (8+15) -> `pg` flipped `0,0`->`1,1` and the labels became 7-12; tapped
    again -> restored `0,0`. Keyboard dispatch confirmed on both pages (xdotool key
    2/3 -> `[ab] dispatch arg=3/4`). NOTE the box `+0x6c` active flag is per-box and
    shared across pages, so a press on one page leaves that physical box active when
    you flip; for instant abilities the game clears it on cooldown, but a held/toggle
    ability must be released before/after paging (our keyup does not auto-release on
    page flip -- a known edge, fine for instant skills which are the common case).
    Mouse-CLICK on the button could not be driven from the harness (synthetic xdotool
    mouse events do not reach this in-game WINE window; physical keyboard does, and
    the user's real mouse does), but the click handler reuses the proven
    `toggle_page()` and the hit-test math matches the live layout (`enb.screen()`
    1280x960 -> bar_x=487, page button at x=799).
  - REAL-ICON ROUTE RE-ASSESSED (the 2026-06-19c "INVITEM +0x20 AuxData" lead does
    NOT pan out): the action-bar glyph leaf resolves its 2D icon from a texture cache
    at DRAW time and does NOT persist a blittable `IDirect3DTexture8*` anywhere in its
    node subtree. Proof: a bounded BFS of the glyph leaf's own subtree (excluding the
    `+0x7c` HUD-scene parent) found ZERO objects with a module-range (D3D COM) vtable
    -- only heap/exe-vtable render nodes. The INVITEM at glyph-leaf `+0x8c` carries a
    `.w3d` model name + a sub-object vtable at `+0x18` (`0x00b10d20`); its `+0x20` is
    NOT the `0x00aff304` AuxData map the earlier note assumed. The inventory-icon
    setter `FUN_0056c180` (client/ui) is a DIFFERENT widget class and reads its icon
    ids from widget `+0x13c/+0x140` via a manager vtable+0x18 call -- not reusable for
    the action-bar glyph without reconstructing that widget. CONCLUSION: real ability
    icons need a PASSIVE DLL hook on the glyph-leaf draw to capture the texture it
    binds (robust, no guessed `enb.call` args -- a guessed dispatcher call on a bad
    `this` already crashed the client once this session). That is the next step and a
    DLL change (relaunch cycle), not a Lua-only fix. Weapon slots keep the original
    Freya bolt placeholder; ability slots draw no glyph until the hook lands.
  - REAL-ICON RECIPE FOUND (the resolve-by-name path the inventory uses; this is
    the concrete route to implement, no blind guessing): the inventory icon setter
    (`client/ui`, the widget whose icon-name fields live at widget `+0x13c` /
    `+0x140`) resolves its icon as
    `tex = (*(texmgr))[vtable+0x18](nameA, nameB, variant)` where
    `texmgr = *(widget + 0x40)`, `nameA = widget+0x13c`, `nameB = widget+0x140`,
    `variant` = 0/1 (normal/alt). The returned object is then wrapped by an
    icon-texture HANDLE class (ctor takes the surface as `*param_2`, stores it at
    handle `+0x8`, vtable `0x00aedd88`). So the engine DOES expose a
    "get-icon-surface-by-name" manager method (`texmgr` vtable slot `+0x18`,
    `__thiscall(nameA, nameB, variant)`), and a surface->`IDirect3DTexture8*`
    accessor lives on the handle/surface. NEXT STEPS to finish #3: (1) find the
    `texmgr` singleton (the `widget+0x40` value -- likely a global UI texture
    manager) and the icon-name string for an action-bar ability (off the per-slot
    item card vt `0x00afd664`, which differs per slot but had no name in its first
    0xc0 bytes -- the name is deeper, in the ability/item DEFINITION the card
    points at); (2) either call the manager `+0x18` method via `enb.call` with the
    REAL (now non-guessed) 3-arg signature, or hook it passively to record
    name->surface; (3) walk surface -> `IDirect3DTexture8*` and blit with
    `enb.draw.texture_quad`. This converts the long-standing "per-ability icon
    source unfound" into a known method call -- the remaining unknown is just the
    icon-name string per ability and the manager singleton address.
- **Progress 2026-06-19e (DEFINITIVE: the slot icon is a 3D MODEL render, not a
  flat sprite -- this CORRECTS every prior "find/capture the icon texture" plan
  above, including 19c/19d; verified live in space on a populated bar)**:
  - Empirically force-showed the `box+0x74` glyph leaf (set flags `|0x700` every
    frame via a run-channel tick) and screenshotted: it renders a **WW3D 3D model**
    (the per-slot `INVITEM` aggregate, sub-objects named `ITEMON01` = icon mesh,
    `ITEM_DARK`/`ITEM_NOSKILL`/`ITEM_TIMER` = state overlays, `GD_SLOT_DATA_*` =
    slot index), NOT a flat 2D icon. So there is NO single blittable
    `IDirect3DTexture8*` "icon" to capture or chain to -- the icon is a textured,
    UV-mapped 3D mesh viewed at a fixed angle.
  - This explains every earlier dead end: the static chains converged on a SHARED
    texture (the frame/border atlas), and a BFS of the mesh subtree surfaced only
    transparent overlay/frame textures (`07d54590` = shared frame, `07d545c0` = a
    near-empty overlay) -- blitting them over a magenta backer showed ~100%
    transparency. None is the visible icon because the visible icon is geometry,
    not a sprite.
  - SEPARATELY corrected: the ornate orange "disks" in the bottom-centre cockpit
    are NOT the action-bar slot glyphs -- they are part of the always-on cockpit
    chrome MODEL, a different node. The `0x400` hide (`box+0x74` leaf `+0x18`)
    correctly drops the slot glyph (confirmed `bit400=clr` in production, and the
    slot 3D model vanished) but does not (and should not) touch those cockpit
    disks. So #2 (hide native slot icons) remains correct and complete.
  - CONSEQUENCE for #3: "show the real per-slot icon on the flat Freya bar"
    requires **render-to-texture of each slot's 3D `INVITEM` model** into an
    offscreen RT, then blit that RT region with `enb.draw.texture_quad`. There is
    no shortcut sprite. RTT is a substantial DLL effort (create RT+depth in
    `D3DPOOL_DEFAULT`, drive each model's WW3D render with a framing camera into
    the RT, restore, blit) with real risk that a WW3D `RenderObj` will not render
    correctly outside the engine's scene/camera pipeline. The "passive SetTexture
    capture" idea from 19d is ALSO insufficient on its own: even captured, a single
    bound texture is one material stage of a multi-mesh model, not the composited
    icon. The honest status: #3 as "pixel-faithful real icon" is a render-to-texture
    project, not a hook-and-blit; the current Freya bar (numbered labels + weapon
    bolt placeholder + working dispatch + paging) is the functional state until RTT
    lands. This is a design call for the owner: invest in per-slot model RTT, or
    keep the placeholder/label bar.
- **Progress 2026-06-19f (SOLVED for abilities -- SUPERSEDES 19e's "it is a 3D
  model, needs RTT" conclusion; the ability card DOES expose a flat icon texture)**:
  - 19e was WRONG. It force-showed the native `box+0x74` glyph LEAF (a WW3D
    `INVITEM` 3D aggregate) and concluded "no blittable sprite exists -> RTT only".
    That leaf is the native bar's renderer, not the icon source. The per-slot ITEM
    CARD (`box+0x64 -> +0x64`, vt `0x00afd664` for an ability) carries a flat 2D
    icon and surfaces it through its OWN vtable: the card's vtable slot `+0x34` is a
    `__thiscall` "get icon texture" getter (the engine's preset-texture branch). Call
    it on the card and it returns a `TextureClass` (vt `0x00b119f0`, w/h at
    `+0x2c`/`+0x30`, `IDirect3DTexture8*` at `+0x34`) that the GAME has already
    loaded and realized -- no RTT, no 3D model, no name lookup needed. Verified live:
    the getter returns a DISTINCT texture per ability (two boxes holding the same
    ability return the SAME texture pointer; different abilities differ), and the
    `+0x34` d3d is already non-NULL (COM vtable in the `0x7f______` d3d8 band).
  - GENERAL name-resolve path (also proven, used for weapons later / any icon): the
    asset manager singleton is `*(0x00bfaee0)`; its vtable `+0x30` is
    `GetTexture(nameA, 1, 0, 1)` (`__thiscall`) returning a `TextureClass`; if its
    `+0x34` d3d is NULL, `TextureClass::Init` (`0x0094b920`, thiscall, no args)
    realizes it. The skill icon NAME for an ability lives in the AllSkills static
    array (base `*(0x00be8e2c)`, end `*(0x00be8e30)`, array of record ptrs; record
    `+0x04` = `char*` icon name e.g. "skill_afterburn.tga", default "skill_.tga").
    Proven live: resolved skill_afterburn / skill_befriend / skill_weaponkill by
    name and blitted all three -- crisp, correct art.
  - TWO render bugs fixed (this is why earlier blits showed "valid texture, nothing
    draws"): (1) `enb.draw.texture_quad`'s alpha arg is 0..255, NOT 0..1 -- passing
    `1.0` truncated to `1` ~= fully transparent; pass `255`. (2) the overlay's
    `K_TEXQUAD` path used `ALPHAOP=MODULATE` (texAlpha * diffuseAlpha); game icon
    textures are opaque RGB with a zero/absent alpha channel, so MODULATE multiplied
    the icon to invisible. `overlay.cpp` now sets `ALPHAOP=SELECTARG2` (alpha solely
    from the diffuse / our requested alpha) for `K_TEXQUAD`, restoring MODULATE
    after. DLL rebuilt + reinjected.
  - WIRED in `freya_ui.lua`: `slot_icon_tex(i)` resolves an ability slot's icon via
    the card `+0x34` getter (Init fallback if not realized) and `draw_hotbar` blits
    it with `texture_quad(..., 255)`. Weapon slots keep the `weapon_icon.png` bolt
    placeholder (a weapon card's `+0x34` returns only SHARED item-state chrome, not a
    per-weapon icon -- the real weapon icon is off the item record, a follow-up).
    Re-resolved every frame; texture pointer never cached.
  - VERIFIED end-to-end EXCEPT a real populated-ability bar: this env's only
    avatar (and beasley's logged-in char) has 3 weapons + no abilities slotted, so a
    real ability glyph has not rendered in the live Freya bar. Proved the in-mod
    plumbing instead: a temp store-only patch made `slot_icon_tex` name-resolve a
    real skill icon for the empty slots; the actual mod `draw_hotbar` then rendered
    those skill icons crisply IN the real slot rects (slots 5/6), confirming
    `draw_hotbar -> slot_icon_tex -> texture_quad` + the alpha fix work in production.
    The only unproven-in-this-env link is the card `+0x34` getter on a real ability
    card -- which WAS proven live in the prior session (5 distinct ability textures).
  - REAL-CLIENT CHECK (owner): on a character with trained activated abilities /
    2D-icon devices slotted, confirm each ability slot on the Freya bar shows the
    correct per-ability art (and that paging 1-6 <-> 7-12 shows the right page's
    icons). Weapon slots show the bolt placeholder by design.
- **Progress 2026-06-19g (four follow-up fixes after icons confirmed on beasleyte:
  delayed-icons, icon appearance, hover, key activation)**:
  - #4 KEY ACTIVATION -- FIXED + VERIFIED LIVE. Pressing 1-6 did nothing because
    `slot_action` read the box active flag `box+0x6c` as a `u32`; the dispatcher
    `FUN_006120e0` reads it as a single `char`. The dword always holds a nonzero
    pointer/float, so the Lua always picked the release base (0x54) and dispatched
    a release id on an idle box -- a dispatcher no-op -- AND swallowed the native
    key, so nothing fired. Fix: read `m.u8(box+0x6c)`. Verified live on beasleyte:
    a real keydown of "1" flips box0 `+0x6c` 0->1 (ability activates), a second tap
    flips 1->0 (toggle off), and the key is swallowed (no native double-fire).
    FOLLOW-UP (every-other-tap): the first cut dispatched ONLY a press on key-down
    and nothing on key-up, so the box flag stuck at 1 and the next press was read by
    the dispatcher as a release (no fire) -- the user had to press/click TWICE to
    activate. The native keybind sends press on down AND release on up (ability
    fires on the press; release just resets the flag). Fixed to mirror that:
    `press_slot` on key-down, `release_slot` on key-up, `tap_slot` (press+release)
    for a click. Verified live: keydown sets the byte to 1 (fires), key-up clears it
    to 0, and three consecutive single taps each net the byte back to 0 -- every tap
    fires once. Single press/click now activates.
  - #3 HOVER -- FIXED + VERIFIED LIVE. The native bar stayed hidden in the normal
    state but reappeared under the cursor on hover. A chrome leaf draws + takes
    input only when `(flags & 0x700) == 0x700` at leaf `+0x18` (Visible 0x100 |
    Enabled 0x200 | inLayout 0x400). The old hide cleared only 0x400 (inLayout),
    which the game RE-SETS on rollover after our per-frame clear -> bar flashes back.
    Fix: `hide_native_glyphs` clears 0x200 (Enabled) on all three chrome leaves
    (`box+0x30/+0x58/+0x74`) per box; the game never re-asserts 0x200, so the gate
    can never complete even on hover. Verified live: flags probe shows en=0 on all
    nine leaves and a hover screenshot shows no native bar below the Freya HUD.
  - #1 IMMEDIATE ICONS -- FIXED + VERIFIED LIVE. Icons appeared only after ~30s or
    a bank switch because the controller was captured ONLY from the
    interaction-triggered dispatch hook. Fix: added a capture hook on the bank
    CONSTRUCTOR `FUN_00610f80` (`game.h::ActionBarCtor`), same capture-only naked
    trampoline as the dispatch hook -- stores `this` (ECX) the moment a bank exists
    (entering space), before any interaction. Store-only; Lua reads bank fields
    frames later after construction completes (guarded by `box~=0`/`readable`), so
    no use-before-init. Verified live on beasleyte: immediately after entering space
    with ZERO clicks/keypresses, `enb.actionbar()` is non-zero (`0x7672EA8`) -- the
    only no-interaction capture path is the constructor hook -- and icons render
    on the bar at once, no ~30s / bank-switch wait.
  - #2 ICON APPEARANCE -- FIXED + VERIFIED LIVE. Icons rendered on an opaque
    dark/black square with no tint. Fix: `overlay.cpp` K_TEXQUAD now uses a
    black-key SCREEN blend (SRCBLEND=ONE, DESTBLEND=INVSRCCOLOR) so the icon art's
    black background blends to transparent, and a MODULATE color stage multiplies
    the texture by a diffuse tint. `texture_quad` gained `tint`/`additive` params;
    `freya_ui.lua` blits abilities with `ICON_TINT = 0xAFD4FF` (light blue).
    Verified live: the brightest icon pixels measured (168,216,243) under the new
    DLL vs neutral (233,240,247) under the old -- matching the tint 0xAFD4FF
    (175,212,255); the black squares now blend into the glass slots.
  - All four (#1-#4) verified live on beasleyte (account "beasley") this session
    after relaunch with the rebuilt `bin/enbmod.dll`.
  - REAL-CLIENT CHECK (owner): confirmed by agent on beasleyte; owner may re-confirm
    on other ability characters / paging if desired, but the mechanism is proven.

### [ ] CV-AS-AUXNUMS -- HUD shows correct numeric cur/max vitals + discipline levels (AuxData getter)

- **Change**: client-only (enbmod.dll). New `enb.aux(key)` / `enb.aux_i(key)`
  (lua_api.cpp) call the client's own property-bag getter to read the numeric
  vitals + levels, which live in a string-keyed bag on the player ship entity,
  NOT at flat struct offsets. `H.stats()` (freya_hud.lua) feeds the player card:
  hull `HullPoints`/`MaxHullPoints` (absolute); shield/reactor max
  `MaxShieldPower`/`MaxEnergyPower` with current = max * live gadget fill
  fraction; levels `RPGInfo CombatLevel`/`TradeLevel`/`ExploreLevel`.
- **Calling convention is load-bearing** (got this class of bug before with the
  `/run` ChatLocalLine crash): build_key `0x004ad380` is `__thiscall` (ECX = a
  zeroed >=0x40-byte key buffer); get_value `0x00546710` is `__cdecl(entity,
  keybuf)`; value float/int at entry+0x84, validity flag at entry+0x70. Reads are
  guarded (entity + entry readability checked, NaN-guarded) but the getter itself
  is real game code, so a wrong convention would corrupt the stack and crash.
  Called from on_tick = the game message-pump thread = the same thread the game's
  own vitals updater runs the identical getter on, so it is single-threaded
  against the game's aux access (no concurrent-mutation race).
- **Discipline-level KEY STRING fix (2026-06-17).** The level keys are the client's
  own DOTTED form -- `RPGInfo.CombatLevel` / `RPGInfo.TradeLevel` /
  `RPGInfo.ExploreLevel` (verified by reading the static image strings at
  0x00b79eb0 / 0x00b79e88 / 0x00b79e60). The earlier SPACE form
  ("RPGInfo CombatLevel") never matched an entry, so `enb.rpg_level` returned
  nothing and the card showed "LV --". With the dotted keys it reads the real
  level live (0/0/0 on a fresh char -> overall "LV 0"), verified over the live
  `/run` channel this session. `H.stats()` now passes the dotted keys via
  `H.RPG_KEY`; the dead Lua BSS-scratch fallback recipe was deleted.
- **Headless coverage**: none -- the mock has no aux property bag; correctness is
  entirely this entry. The CLI cannot validate it.
- **What to look for (real client)**:
  - In space, each bar prints "cur / max" inside it, and the maxes match the
    character sheet (owner's were hull 11, shield 45, reactor 142). Hull current
    tracks damage; shield/reactor current = max * the bar's fill.
  - The discipline card shows the real Combat/Trade/Explore levels (not the gray
    skeleton) and they update on level-up.
  - No crash on load / zone / undock / dock from the aux calls. If it crashes,
    the convention or an offset is wrong -- comment the `enb.aux*` use in
    freya_hud.lua `H.stats()` to bisect (the rest of the HUD is independent).
  - Overall level (JEFIVE card) now reads the SUM of the three discipline levels
    (0 on a fresh char) -- no longer the "LV --" skeleton. Per-discipline xp% is
    pinned separately by CV-AS-XPFRAC below.
- **Setup**: `just play-local` with `UseClientMods=true`.

## CV-AS-RUNJMP -- /run console no longer crashes (Lua error path uses GCC builtins)

- **What changed**: Lua's error handling (`ldo.c` LUAI_THROW / LUAI_TRY) was
  using libc `setjmp`/`longjmp`. On 32-bit mingw-w64 under Wine that unwind path
  is unreliable -- the FIRST Lua error (a `/run` syntax error such as the failed
  `return return 1+1` expr-wrap probe) faulted with unhandled STATUS_SINGLE_STEP
  (0x80000004) instead of jumping cleanly back to the protected call. Removing
  the earlier-suspected `ChatLocalLine` echo did NOT change the crash code, which
  ruled that out and pointed at the jump machinery itself. The build now force-
  includes `src/lua_build_config.h` into every Lua TU, pointing LUAI_THROW/
  LUAI_TRY/luai_jmpbuf at `__builtin_setjmp`/`__builtin_longjmp` -- the SAME
  mechanism `mem.h` already uses and documents as "verified working under Wine".
- **Why this is the likely fix but UNVERIFIED here**: cannot be exercised without
  the real Win32 client under Wine; the headless Lua tests run native (libc
  jmp is fine there) so they cannot reproduce or confirm it.
- **What to look for (real client)**:
  - In space, type `/run return 1 + 1` in chat -> no crash; `enbmod.log` shows
    `[run] 2`.
  - `/run enb.aux("MaxHullPoints")` etc. echoes the value to `enbmod.log`
    (this is the intended in-game evaluation path for the aux reads).
  - A deliberately broken `/run x = (` (syntax error) and `/run error("boom")`
    (runtime error) both log a `[run] compile error`/`[run] error` line and do
    NOT crash -- that is the longjmp path that previously took the client down.
- **Setup**: `just play-local` with `UseClientMods=true`. Rebuild the DLL first
  (`just build-enbmod`); the Lua objects must be recompiled with the new
  `-include` (a stale `build/lua/ldo.o` would keep the old libc-jmp behaviour).
- **UPDATE 2026-06-17**: this longjmp fix was real but was NOT what crashed the
  live `/run return 1 + 1` repro -- that was the ChatSend calling-convention bug
  (CV-AS-CHATSEND below). The crash happened BEFORE the Lua error path ever ran.
  Both fixes are now in; verify `/run` against the combined build.

## CV-AS-CHATSEND -- /run (and any chat command hook) no longer corrupts the stack

> **Confirmed by project owner (2026-06-17):** in-space `/run return 1 + 1;`
> produced `[run] 2` in enbmod.log and the client did NOT crash. The
> __thiscall/__cdecl stack-imbalance crash is fixed.

- **What was wrong**: the enbmod chat send-line hook (`hk_ChatSend`, target
  `game::addr::ChatSend`) was declared as a typed `__cdecl` C function. The
  client calls that member as `__thiscall` (`this` in ECX, **callee** pops the
  4-byte arg -- `ret 4`). A `__cdecl` detour returns `ret 0`, so every chat send
  left ESP 4 bytes high. The next pointer the real ChatSend loaded from the stack
  (`mov 0x14(%esp),%ecx`) came back as an adjacent slot's small integer (`0x10`),
  and `mov -0x1(%ecx),%al` faulted reading `0x0F`. First fault `eip=0074b083`,
  `c0000005`, in the real ChatSend body -- a stack-misalignment crash, not heap.
- **Fix (commit 853d0e33)**: `hk_ChatSend` is now a NAKED `__thiscall`
  trampoline (the same pattern as the AuxData/RpgLevels capture hooks):
  `pushal` / push the original `[esp+4]` line arg / `call notify_chat_send` /
  on swallow `popal; mov $1,eax; ret $4` / on pass-through `popal; jmp
  *real_ChatSend_tramp`. Both paths now balance the stack and preserve ECX.
  Verified in the emitted DLL: swallow path emits `mov $1,eax ; ret $0x4`.
- **Why UNVERIFIED here**: needs the real Win32 client under Wine; the headless
  Lua tests never exercise the client's ChatSend.
- **What to look for (real client)**: in space, type `/run return 1 + 1` -> no
  crash, `enbmod.log` shows `[run] 2`. Any normal chat line (no leading command)
  still sends to other players. Repeated chat sends do not destabilise the client.
- **Setup**: `just play-local` with `UseClientMods=true` -- it rebuilds
  enbmod.dll and the launcher redeploys it into the wine prefix. (The DLL that
  crashed, prefix copy stamped 08:22, predated this fix.)

## CV-AS-RPGLVL -- discipline levels read off the RPG manager (not the ship entity)

- **What was wrong**: the discipline levels (Combat/Trade/Explore) showed "LV --"
  because they were read with enb.aux_i on the ship vitals entity. They do NOT
  live there: RPGInfo is a property bag on the RPG MANAGER object, and the level
  entries use a different value type than the vitals getter -- so the read missed
  twice over (wrong object, wrong getter type).
- **Fix**: a new read-only hook on the client's own discipline-level reader
  (game.h addr::RpgLevels, __fastcall ECX = the RPG manager) captures the manager
  pointer live (hooks::rpg_mgr()), exactly like the in-space heartbeat captures
  the vitals controller. enb.rpg_level(key) resolves the RPGInfo AuxData container
  at manager+0x12c0 and looks the key up through the level getter (0x00514b60,
  __cdecl(container, keybuf) -- same shape as the vitals getter, confirmed via the
  funcmap, different value-type tag). freya_hud H.stats() now uses enb.rpg_level.
- **Calling convention is load-bearing** (same risk class as CV-AS-AUXNUMS): the
  capture hook is the proven naked-trampoline pattern (pushal / push ecx / call /
  popal / jmp trampoline); the getter is __cdecl(container, keybuf), value int at
  entry+0x84, validity at entry+0x70. Reads are readability- and validity-guarded.
- **Expected behaviour**: on a FRESH character all three levels are 0, so a
  correct read shows "0" (not the "--" skeleton). They are nil -> "--" only until
  the client's RPG level-reader has run at least once (manager pointer still 0).
- **Headless coverage**: none -- the mock has no RPG manager / aux bag.
- **What to look for (real client)**:
  - In space (or once the character/RPG panel has updated), the discipline card
    shows 0/0/0 on a fresh char, and real numbers that climb as you level a
    discipline -- never the gray "LV --" skeleton once the reader has fired.
  - No crash on load / zone / open-character-sheet from the new hook or getter.
    If it crashes, comment the enb.rpg_level use in freya_hud H.stats() lvl() to
    bisect (vitals + the rest of the HUD are independent of it).
- **Setup**: `just play-local` with `UseClientMods=true`; `just build-enbmod` first.

## CV-AS-XPFRAC -- per-discipline xp bar fill % reads off the XpBars controller

- **What was wrong**: the discipline card's per-row xp bars had no live data --
  there was no flat-struct or AuxData source for the 0..1 xp fill fraction, so the
  bars rendered empty (the one xp pair we had fed the overall card only). The
  Explore bar should read ~94.50% on the owner's character; it showed empty.
- **Fix (client-only, enbmod.dll)**: a new read-only capture hook on the client's
  own XpBars value-updater (game.h addr::XpBarsUpdate `0x0058cb50`, __fastcall
  ECX = the XpBars controller) records the controller pointer live
  (hooks::xp_ctrl()), the same proven naked-trampoline pattern as the vitals/RPG
  capture hooks. enb.xp_frac(which) then resolves the per-discipline bar gadget
  off the controller (combat +0x1c / trade +0x20 / explore +0x24), checks the
  gadget's exists flag (+0x60), and reads the cached fill fraction f32 at +0x68
  (NaN-guarded, clamped 0..1). xp_overlay.lua multiplies by 100 for the row %.
- **Calling convention is load-bearing** (same risk class as CV-AS-RPGLVL): the
  capture hook only reads ECX and jumps to the trampoline -- it never alters the
  updater's behaviour. The pointer chain (ctrl -> bar gadget -> +0x68) is
  readability-guarded at every deref.
- **Decomp-verified, NOT live-verified**: the +0x24/+0x68 chain was confirmed by
  reading the XpBars updater + paint, but the running session predates the rebuilt
  DLL (xp_ctrl() == 0, enb.xp_frac returns the empty fallback) -- it needs a client
  RELAUNCH (`just play-local`, which rebuilds enbmod.dll) to take effect. I cannot
  relaunch the WINE client myself, so this is staged-and-pending the owner's run.
- **Headless coverage**: none -- the mock has no XpBars controller.
- **What to look for (real client, after relaunch)**:
  - In space, the discipline card's Explore row bar fills to ~94.50% (and Combat /
    Trade to their real fractions); `/run return enb.xp_frac("explore")` logs
    ~0.945 and `/run return enb.xp_ctrl()` is non-zero once the updater has run.
  - No crash on load / zone from the new hook or the pointer-chain reads.
- **Setup**: `just play-local` with `UseClientMods=true`; rebuilds enbmod.dll.

## CV-AR-1 -- /AuthLogin throttle returns wire-identical Valid=False (low risk)

- **What changed**: Phase AR-1 added a per-IP rate limiter + a global Argon2id
  concurrency cap in front of net7go's `/AuthLogin` (login-server/net7go/
  ratelimit.go). On throttle OR Argon2 saturation, handleAuthLogin returns the
  EXACT same bytes as a normal failed login (`HTTP 200 ... Valid=False\r\n`) --
  no new status code, no new body, no new wire surface. The legacy wire format
  stays entirely inside the CC BY-NC-SA net7go binary.
- **Why this is likely a no-op for the client**: a throttled response is
  byte-identical to a wrong-password response, which the client already handles
  (shows "login failed", lets the player retry). So there is no NEW behaviour to
  the client; this CV exists only to confirm the UX nuance below.
- **Behavioural nuance to confirm (real client)**:
  - Under a genuine login flood, a *legitimate* player whose attempt is shed sees
    the ordinary "login failed / could not log in" message and a retry succeeds
    (the limiter is per-IP with a generous burst of 20, and the Argon2 cap only
    sheds momentarily under saturation). Confirm a normal failed-login UX, not a
    hang or a protocol error dialog.
  - A normal single login on an idle server is never throttled (burst is full on
    first sight of an IP).
- **Tuning knobs** (env, defaults are generous): `NET7GO_AUTH_RATE_PER_MIN=60`,
  `NET7GO_AUTH_BURST=20`, `NET7GO_ARGON_MAX_CONCURRENT=4`. Raise the burst if a
  large NAT'd group ever reports spurious failures.
- **Headless coverage**: net7go ratelimit_test.go pins the limiter/gate logic;
  the wire-identical Valid=False is exercised by the existing legacy auth tests.
  The real-client UX under throttle is the only unverified part.

## CV-MAP -- In-game galaxy map renders the full node list (PB-4 / Z-5, proxy serve)

- **What changed**: the proxy now serves the prebuilt galaxy-map node stream.
  `UDPClient::SendCachedGalaxyMap()` (`proxy/UDPProxyToClient_linux.cpp:643`,
  previously a no-op stub) reads `GalaxyMap.dat` and re-emits its
  `[len:u16][opcode:u16=0x0097][body]` records to the client on the `0x2010`
  DATA_FILE request for inner opcode `0x0097`, then `SetReceivedGalaxyMap()`.
  docker-compose mounts `./server/data:/app/database:ro` so the committed 305-record
  `server/data/GalaxyMap.dat` resolves at `./database/GalaxyMap.dat`.
- **Why a CV is needed**: this is a proxy wire-fabrication change -- the CLI proves
  the record FORMAT (`GalaxyMapRecord` decodes Types 5/6/7/8/9 with unit tests), but
  only the real client proves the MAP RENDERS and is navigable. Primary source: cap6
  (login-opengalaxymap) shows the client's 0x2010/0x0097 request; the retail proxy
  answered by streaming GalaxyMap.dat.
- **What to look for (real client)**: launch client.exe through FreyaProxy, log in,
  open the galaxy map (M). Expect the full node set (~305 nodes -- named systems,
  sectors, planets, gates/links) to render and be navigable, NOT an empty map with
  only a "you are here" marker (the PB-4 symptom). Confirm the proxy log shows
  "served galaxy-map cache ... (305 records ...)" and NO "WARNING ... cannot open
  galaxy-map cache".
- **Setup**: `just play-local` (rebuilds the proxy image; the data dir mount is in
  docker-compose). Distinct from PB-16 (website map colors) and PB-13 (nav/explore).

## [x] CV-MANU -- Analyze/Dismantle no longer hangs the manufacturing UI (PB-14)

> **Confirmed by project owner (2026-06-17):** analyze, dismantle, and
> manufacture all work end-to-end on the real client -- the terminals open,
> return results, and the UI does not hang.

- **What changed**: `server/src/PlayerManufacturing.cpp` -- `Player::ManufactureTimedReturn`
  no longer early-returns out of the dismantle/analyze callback when zero components
  resolve. The `if (compList.size() == 0) return;` at :962 (which skipped the
  `SendAuxPlayer`/`SendAuxShip`/`SendAuxManu` reply block) was replaced with a guarded
  `if (!compList.empty()) { ...sort/shuffle/keep-drop... }` so control ALWAYS falls
  through to the three replies. The server now always answers a manufacturing action
  with a `0x001B` AUX_DATA ManufacturingIndex reply.
- **Why a CV is needed**: the reply emit is a server wire-behaviour change. Primary
  source: cap3 (analyze-damage/success/fail) + cap4 (analyze-success + dismantle-success,
  many runs) show the retail server ALWAYS emits a `0x001B` ManufacturingIndex reply on
  every analyze/dismantle -- there is no consume-and-stay-silent path. CLI: the
  `0x001B` ManufacturingIndex reply is decoded + pinned in `CliClient.Core`.
- **What to look for (real client)**: dock at a starbase with a manufacturing/analyze
  terminal, load an analyzable item, run Analyze and (separately) item breakdown /
  Dismantle, including on items that resolve no recovered components. The terminal must
  return a result (success/fail message + updated component grid) and the UI must NOT
  hang -- the PB-14 symptom was the UI freezing with no reply. Repeat several dismantles
  back-to-back.
- **Setup**: `just play-local` (rebuilds the server image with the PlayerManufacturing
  fix). A character docked at a manufacturing terminal with analyzable/dismantlable
  items in cargo.

---

### [~] CV-25 -- Position feed keeps updating after a sector change (planets / gating): can dock at a planet base -- SUPERSEDED 2026-07-01 by CV-BA-2c (the trampoline this verified was deleted; re-verify the concern via the new enbmod-sourced feed under CV-BA-2c)

- **What changed**: `freya/client-injection/ClientEngineOffsets.h` -- the in-client
  position-feed trampoline no longer LATCHES the ship transform pointer on first
  capture. The old sequence captured the first player-hull transform pointer seen
  after login (`MOV ECX,[scratch]; TEST ECX,ECX; JNZ skip`) and never refreshed it,
  with the reset-on-sector-change patch disabled. On any sector change -- gating, and
  most visibly entering a PLANET sub-sector -- the client rebuilds the ship object at
  a new heap address, so the latched pointer went stale; the feed read zero/garbage,
  went silent, and the server froze the avatar at its last position. Symptom: flying
  around a planet, position never updates server-side, so you can never get "near
  enough" to a planet base to dock. The trampoline now writes the current
  player-hull transform pointer to the slot every frame it runs, mirroring how the
  real proxy continuously re-tracks the controlled ship's transform.
- **Why a CV is needed**: this is an in-client (FreyaPosFeed.dll) hand-assembled
  code-hook change; the CLI integration suite cannot exercise the client's engine
  memory or the loopback MVAS datagram path, so only the real client proves it.
  Not a server/proxy wire change -- the server-side MVAS handler is unchanged.
- **What to look for (real client)**: fly to a planet (e.g. Inverness), fly around
  near a planet base (Inverness Down). Your ship icon must track on the in-sector
  map and the base must register you as "in range" so the Dock prompt works. Then
  gate between several sectors in one session and confirm position keeps updating
  after each gate (not just the first sector). Watch nav/HUD position, not just dock.
- **Known caveat (multiplayer)**: the player-hull signature gate ('S' at [EAX],
  0x48 at [EAX+2]) is a generic hull signature, not player-specific, and the GameID
  disambiguation patch is disabled (address unverified). In a populated sector the
  slot could momentarily track another player's hull. Acceptable for solo play and
  strictly better than the fully-frozen-on-planet status quo; revisit if remote
  players' positions bleed into the local feed.
- **Setup**: rebuild the DLL (`just build-posfeed-dll`) -- already done -- then
  relaunch with `just play-local`; the launcher re-injects the rebuilt
  FreyaPosFeed.dll into client.exe.

---

### [ ] CV-26 -- Vendor trade window populates quickly (large vendor in ~6s, not ~25-30s)

- **What changed**: `server/src/PlayerConnection.cpp` -- `ITEMS_PER_TICK` raised
  from 2 to 8. The vendor/vault/cargo item lists are streamed from the login
  thread (`RunLoginThread`, ~100ms/tick) one `0x0025 ITEM_BASE` datagram per row.
  At 2/tick a ~500-item general/ammo vendor took ~25-30s to fill its trade
  window (reported live). Each datagram occupies a reliable-UDP resend ring slot
  (RESEND_ELEMENTS=64); at 8/tick at most ~48 are in flight over the proxy's
  ~600ms NACK window, safely under 64, so no ring entry is evicted before a
  resend can be served. 8/tick loads the same vendor in ~6s.
- **Why a CV is needed**: changes server emit timing/batching (not packet
  bytes -- the 0x0025 frames are unchanged and already byte-pinned). The risk it
  must clear on the real client is ring-overflow: too fast a burst would strand
  the client on a hole it can't get resent (the CV-23/24 failure mode). Only the
  real client + proxy prove the new rate stays within the ring.
- **What to look for (real client)**: dock at a starbase with a big vendor (a
  general store / ammo vendor with hundreds of items). The trade window's item
  list must fill in a few seconds, and every item must appear (no missing rows,
  no stall on the load screen, no stuck-populating window). Also open the vault
  (96 slots) and confirm it fills fast and completely. Repeat a few times.
- **Setup**: `just rebuild server` then `just play-local`.

---

### [ ] CV-27 -- Vendor "Buy" / "Buy Stack" buttons actually purchase (not just drag-and-drop)

- **Root cause (corrected)**: the real culprit was NOT the `ToInv` value (an
  earlier hypothesis that the buttons send `ToInv==4` was wrong -- see below).
  It was the AJ-3 InvMove slot bounds check in `HandleInventoryMove`. The retail
  client's Buy / Buy Stack buttons have no drop target, so they emit `0x0027`
  with `FromInv=4, ToInv=1, ToSlot=-1` (cargo, auto-select). AJ-3 added a
  `ToSlot==-1` auto-select whitelist for `1->3`, `3->1`, `14->1` but omitted the
  vendor-buy `4->1`, so every Buy / Buy Stack frame was rejected before reaching
  the switch -- no purchase, no credit change, no message. (A drag-buy drops onto
  a concrete cargo slot, e.g. `ToSlot=31`, which passed the bounds check, so
  drag-and-drop kept working -- exactly the reported symptom.)
- **What changed**: `server/src/PlayerConnection.cpp` -- added
  `(InvMo.FromInv == 4 && InvMo.ToInv == 1)` to the `auto_select_dest` whitelist
  so the `ToSlot==-1` vendor buy reaches case 4. case 4 resolves the cargo slot
  via `CargoAddItem` and never indexes `ToSlot`, so no slot-indexing risk. Also
  added a NULL guard on the vendor item lookup (`GetItem` could return NULL for
  an empty/unknown vendor slot and was dereferenced via `myItem->Category()`).
  The `ToInv == 1 || ToInv == 4` accept in case 4 is retained defensively.
- **Primary source**: real Win32 client capture on the proxy<->server leg of a
  "Buy Stack" of PL-X1 Impact Round (200): `0x0027` body
  `40 00 00 0B  00 00 00 04  00 00 00 07  00 00 00 01  FF FF FF FF  00 00 00 C8`
  i.e. `FromInv=4, FromSlot=7, ToInv=1, ToSlot=-1, Num=200`. The captured drag
  buy (VendorInvEco dg #12: `FromInv=4, ToInv=1, ToSlot=31`) confirms the drag
  path uses a concrete slot.
- **CLI parse + test**:
  `InventoryMoveCodecTests.Encode_BuyStackButton_VendorStockToCargo_AutoSlot_Num200`
  pins the captured button frame (`ToInv=1, ToSlot=-1, Num=200`); the drag path
  stays pinned by `Encode_BuyIntoCargoSlot31_MatchesCapture_dg12`.
- **What to look for (real client)**: at a vendor, select an item, click "Buy"
  (buys 1) and "Buy Stack" (buys the chosen quantity) WITHOUT dragging. The item
  must land in cargo and credits must be deducted by `each-price x quantity`.
  Confirm insufficient-credits / full-cargo still refuse cleanly.
- **Setup**: `just rebuild server` then `just play-local`. A character at a
  vendor with credits and free cargo space.

---

### [ ] CV-28 -- ManufacturingIndex (0x001B) "Items" category list matches retail (FIDELITY only)

- **What changed**: `server/src/AuxClasses/AuxManufacturingIndex.cpp`
  (`InitializeCategories`) -- deleted the hardcoded fourth "Items" category
  "Consumables" -> sub-category "Consumable X" (CategoryID 13 / SubCategoryID
  130). The retail "Items" primary category stops at "Core" (Weapons / Systems /
  Core); it has no fourth child. The extra category was emitted in every
  `0x001B` AUX_DATA ManufacturingIndex reply as an extra present-bit in the
  "Items" category-list flag plus an extra subtree.
- **NOT the analyze crash fix.** This was first pursued as the crash cause, but
  a captured post-fix run confirmed our `0x001B` index AND the analyze-mode
  `0x001B` delta were already byte-identical to live with this fix applied, yet
  the client STILL crashed on Analyze. The actual crash cause is the 0x007F
  byte-order bug -- see **CV-29**. This entry stays as a pure fidelity fix.
- **Why a CV is needed**: changes server wire bytes (the 0x001B aux now has one
  fewer category). The CLI proves the format; only the real Win32 client proves
  it renders.
- **Primary source**: a `0x001B` ManufacturingIndex reply captured on the
  proxy<->server leg from the live reference server (correct: 3 "Items"
  categories) diffed byte-for-byte against our pre-fix server output (4
  categories). The only divergence is the extra "Consumables" category: the
  "Items" category-list flag byte (retail 0x76 vs ours 0xf6, the fourth-slot
  present bit) and the spliced "Consumables"/"Consumable X" subtree. Removing it
  makes our reply byte-identical to the retail capture (modulo GameID).
- **CLI parse + test**:
  `freya/cli-client/tests/CliClient.UnitTests/Opcodes/AuxManufacturingIndexWalkTests.cs`
  -- `OnlyDivergence_IsTheConsumablesCategory` pins that the sole difference is
  the fourth category, and `Fix_RemovesConsumables_YieldsRetailBytes` proves
  excising it + clearing the present-bit reproduces the retail body exactly.
- **What to look for (real client)**: dock at a starbase with a manufacturing
  terminal and open the manufacture index -- the "Items" primary category lists
  Weapons / Systems / Core with no spurious "Consumables" leaf.
- **Setup**: `just rebuild server` then `just play-local`. A character docked at
  a manufacturing terminal.

### [x] CV-29 -- Analyze at a manufacturing terminal no longer crashes the client

> **Confirmed by project owner (2026-06-17):** with the 0x007F byte-swap fix
> applied, docking a manufacturing terminal and clicking Analyze opens the
> window and no longer crashes the client. Dismantle and Manufacture work
> end-to-end too (same `0x007F` session-key path).


- **What changed**: `server/src/PlayerConnection.cpp` (`Player::SetManufactureID`)
  -- the `0x007F` MANUFACTURE_SET_MANUFACTURE_ID field is now byte-swapped
  (`htonl`) before send. It is the ONE GameID on the wire that is byte-reversed
  (network order) relative to every other GameID (which go host little-endian).
  A previously committed change (`730ba649`) removed the swap on the mistaken
  belief the field was host-LE; this restores it. The `SectorManager` call sites
  stay host-order (the swap lives in the emitter).
- **Why this is the crash cause**: the client reads `0x007F` BIG-ENDIAN to key
  the manufacture-session lookup, then matches it against the manu-lab object
  which is stored under its CREATE GameID (read little-endian). With the field
  emitted host-LE, the client's BE read produces the byte-reversed id, the
  lookup misses, the analyze window's session pointer stays NULL, and the open
  path faults dereferencing it. (Live crashed-client confirmed the NULL session
  pointer.) MANUFACTURE mode does not take this lookup path, which is why only
  Analyze (and by the same path Dismantle / Refine) crashed.
- **Primary source**: `archive/replay/capture_1.txt`, a direct cleartext
  client<->server capture. The manu-lab anchor appears as the same GameID two
  ways in one stream: the lab object CREATE and its AuxData carry it host-LE
  (`F7 13 EE 06`, lines 4604/4626), while the `0x007F` payload carries it
  byte-reversed (`06 EE 13 F7`, line 3769). `htonl(host GameID)` reproduces the
  retail `0x007F` bytes exactly. Our own pre-fix capture emits `0x007F` =
  `27 00 00 C0` (same order as our CREATE `27 00 00 C0`) -- i.e. NOT reversed,
  the divergence from retail.
- **CLI parse + test**:
  `freya/cli-client/tests/CliClient.UnitTests/Opcodes/ManufactureSetManufactureIdRecordTests.cs`
  -- `Decode_ReadsFieldBigEndian_RecoveringHostGameId` pins that the field reads
  big-endian to recover the host GameID the CREATE/AuxData carry. The record
  (`ManufactureSetManufactureIdRecord`) reads `0x007F` big-endian to match.
- **What to look for (real client)**: dock at a starbase with a manufacturing
  terminal, load an analyzable item, and click **Analyze**. The terminal must
  open and return a result -- it must NOT crash. Confirm Dismantle / Refine too
  (same lookup path). MANUFACTURE mode already worked before the fix.
- **Setup**: `just rebuild server` then `just play-local`. A character docked at
  a manufacturing terminal with an analyzable item in cargo.

### [ ] CV-PB19 -- "Build Ammunition" skill appears + trains on a Progen Sentinel (PB-19)

- **What changed**: `server/data/Skills.xml` gained the `<Skill ID="59"
  Name="Build Ammunition" Category="Trade" Type="Passive">` block (Sentinel +
  Scout, Max=7, Quest=1, LearnLvl=30). Primary source: client `cskill_t.ini`
  `[All Skills]` `59=Build Ammunition` (the skill-id enumeration the server's
  Skills.xml indexes into) and the Build_Ammunition wiki page (classes, rank,
  level-30 quest unlock). The skill list is wire-visible -- the server sends the
  skill table to the client, so this is a content add the real client renders.
- **SCOPE / honesty**: this is the skill DEFINITION only. The unlock mission
  "Bite The Bullet", the ammo recipes the skill gates, and the ammo economy are
  NOT implemented (separate work, tied to PB-26).
- **What to look for (real client)**: on a Progen Sentinel (or Terran Scout),
  "Build Ammunition" now appears in the Trade skill list, max rank 7, and does
  NOT error in the skill window. It is gated behind the level-30 quest (so it
  may show as locked/untrainable until that mission exists -- which is expected,
  not a regression). Confirm the skill table still loads cleanly for OTHER
  classes (no parse error introduced).
- **Setup**: `just rebuild server` then `just play-local`. A Progen Sentinel
  character; open the skills window.

### [ ] CV-AS-NOTICE -- account-notice dialog ret-patch stops the login-screen crash (enbmod)

- **What changed**: new always-on enbmod client safe-patch
  (`freya/client-injection/enbmod/scripts/lib/safe_patches.lua`, applied from
  `init.lua` before mods load) ret-patches the retail account-notice /
  subscription-expiry dialog at `0x005aea60` so it never runs.
- **Root cause (empirically confirmed, live crashed process)**: the dialog reads
  an account-status block (`account+0x1098..`, expiry int32 at `account+0x1130`)
  that the retail billing server populated. Our global-login flow never sends
  that data (the server has no subscription concept), so the block is left as
  uninitialised heap garbage. When `account+0x1d` (notice flag) is a nonzero
  garbage byte the dialog fires; when `account+0x1130` is a negative garbage
  int32 the date formatter `0x00a2a68f` returns NULL (it bails on negative
  input) and the dialog `rep movs` 9 dwords from NULL -> access violation at
  `0x005aebce` during LoginTask (after auth, before char select). Intermittent
  because it depends on the heap contents at account-object allocation. Verified
  by reading the live faulting process: `account+0x1d = 1`,
  `account+0x1130 = 0x81460010` (= -2126118896), block contained stale
  sound-asset string fragments, not account data.
- **NOT enbmod / NOT hide-ui**: hide-ui's `patch_ret` writes single `0xC3` bytes
  at `0x005dcae0`/`0x0058cf60` (unrelated paint routines); the crash is at
  `0x005aebce`. Confirmed by relaunching with the hide-ui patch gated to
  in-space -- crash reproduced identically.
- **What to look for (real client)**: log in repeatedly (the crash was
  intermittent). The client should reach character select every time, with no
  account-notice popup (there is none to show on this server). `enbmod.log`
  prints `safe_patches: account-notice dialog ret-patched -> true`.
- **Setup**: restage scripts (`run-lua-client-command/scripts/restage.sh`) then
  `just play-local`. No DLL rebuild needed (pure-Lua patch).
- **Long-term**: the faithful fix is server-side -- initialise the account-status
  block in the global-login flow (notice flag 0, valid/empty expiry) so the
  retail path sees sane data. Tracked as follow-up; this stops the crash now.

### [ ] CV-PB29-30 -- Terran-Trader skill-quest offers depend on character state, not content (PB-29 / PB-30)

- **Why this is here, not a content fix**: the PB-29 and PB-30 content audits
  (plans/41) found the mission/NPC data is CORRECT and self-consistent, so there
  is nothing to change in the `net7` content DB. The reporter's symptom (quest
  not offered / skill not trained) can only be explained by the live character's
  runtime state, which needs the real client + the reporter's actual character
  to diagnose. Do NOT fabricate a content fix against correct data.
- **PB-29 -- "Reconfiguration of Shields" (mission 347) not offered by Louden
  MacEwen (npc 41) at Loki Station**: data is correct -- mission 347 exists,
  Stage-0 giver == Louden's `npc_Id` 41, gated ONLY to Race==Terran(0) +
  Profession==Trader(1), rewards skill 51 "Shield Charging", no level/prereq
  gate. A clean Terran-Trader WILL be offered it. **What to verify on the real
  client**: with a Terran Trader who has NOT done mission 347, talk to Louden at
  Loki Station (High Earth) -- the quest should appear. If it does, the
  reporter's character had already started/completed it, or its stored
  race/profession is not exactly Terran/Trader. Capture the reporter's
  completed-missions set + character race/profession to settle it.
- **PB-30 -- Katlyn Walsh (npc 306) won't train Build Components (skill 4) at
  New Edinburgh/Somerled**: the reporter's "gated behind Reconfiguration of
  Shields" premise is FALSE per data. Build Components is taught by mission 117
  "Tinkering with Parts" (Katlyn), gated by MISSION_REQUIRED on missions **6 +
  116** (NOT 347). **What to verify on the real client**: a Terran Trader who has
  completed missions 6 and 116 should be offered "Tinkering with Parts" by
  Katlyn. If completing 6+116 unlocks it, the report was just an unmet (and
  mis-attributed) prereq, and there is no defect.
- **Setup**: `just play-local`, a Terran Tradesman character. No code/content
  change to test -- this is a state-diagnosis, not a fix verification.
- **Primary-source caveat**: net7wiki (the cited source for PB-29) is behind a
  Cloudflare JS challenge and could not be machine-fetched to cross-check
  retail prereq fidelity. Our content is internally coherent; if the wiki later
  shows retail gated these differently, revisit then.

### [ ] CV-PB17 -- idle-then-zone-transition no longer hangs on the loading screen (PB-17)

- **What changed**: TWO reap-during-handshake paths closed.
  (1) server `UDP_Connection::HandleLoginStageAck`
  (server/src/UDP_MVAS.cpp:230) now refreshes `LastAccessTime` on every
  login-stage ack, mirroring `HandleKeepCommsAlive`. This closes a code-found
  race: a zone change re-LOGINs the player (resets below `LAST_LOGIN_STAGE`),
  arming the 2-minute idle reaper in `RunLoginThread`; the per-stage acks did
  not refresh the idle timer, so a player who idled near the 2-min mark before
  transitioning could be reaped mid-load, freeing the GameID -> every later
  keepalive answered with `0x100A MVAS_TERMINATE` -> infinite loading screen.
  (2) `Player::HandleLogin` (server/src/PlayerConnection.cpp) now refreshes
  `LastAccessTime` BEFORE it resets the stage. The reporter repro ("sit 2-3 min
  on a station, then leave") proved the stage-ack fix alone is insufficient: a
  station-parked player is at `LAST_LOGIN_STAGE` so the reaper ignores them, so
  idle mattering at all means the keepalive is letting `LastAccessTime` go stale
  during the idle (PB-9). When they then leave, the reaper is armed with an
  already-stale timer and drops them on the next ~1s pass, before any stage-ack
  lands. Resetting the clock at the transition gives the handshake a full fresh
  2-min window regardless of how stale idle left it.
- **Why it needs the real client**: the 30s `0x3005` keepalive SHOULD also
  refresh the timer through the handoff, so on the happy path the race may not
  fire. This fix is correct regardless (an acking client is not idle) but is
  NOT a proven cure -- only a live repro confirms PB-17 is actually resolved,
  and confirms there isn't a SECOND cause (keepalive thread dying, dropped
  handoff reply).
- **What to do (real client)**: reproduce the reporter's exact path -- log in,
  sit idle on a STATION for 2-3 min, then leave (undock / dock to another base /
  gate). The load must COMPLETE every time. Also try the ~110s-then-gate variant
  and a longer idle (3-5 min) to stress the empty-sector teardown path. Run it
  enough times to clear the previous "happens intermittently" framing.
- **What to watch in logs** (`docker logs freya-dev-server-1` + proxy): there
  must be NO `Player '<name>' Removed from Galaxy.` line during a loading
  screen, and NO `0x100A MVAS_TERMINATE` sent in response to a keepalive for a
  live session. If the hang still reproduces, capture the server+proxy logs at
  the stuck moment -- the next suspect is whether the keepalive thread is still
  firing (grep `Received Keepalive`) or a handoff reply was dropped.
- **Setup**: `just rebuild server` (done) then `just play-local`.

### [ ] CV-PB22 -- nav map reveals on proximity, not all-at-entry (PB-22)

- **What changed**: `SendAllNavs` (server/src/ObjectManager.cpp:572) entry gate
  reverted from `obj->IsNav()` (send every nav) to
  `obj->GetEIndex(player->ExposedNavList())` (send only previously-discovered
  navs). Undiscovered navs are now revealed by `CheckNavRanges` as the player
  flies within scanner range. The 636c6175 capture citation was a misread (the
  capture char had explored the whole sector, so the bytes are identical either
  way -- owner-confirmed), so this is not a contradiction of the capture.
- **Why the real client is needed**: confirms the three discovery states render
  correctly AND that the "Luna empty-map" symptom 636c6175 was chasing does not
  return in a harmful form.
- **What to verify (real client)**:
  1. **Fresh character, never-visited sector**: on entry the sector map should
     show NO "?" nodes for navs you've never been near (only planets / things
     in range). Previously every nav showed as "?" immediately -- that must be
     gone.
  2. **Fly toward a nav**: as it enters scanner range it should appear as a "?"
     node (exposed), and the "discovered <nav>" message should arrive TOGETHER
     with the node appearing (not a message with no node -- the old Luna
     complaint). Flying closer (explore range) should mark it visited and award
     XP.
  3. **Re-enter the sector**: navs you previously exposed/visited should still
     be on the map at entry (persisted ExposedNavList), so your discovered map
     is not lost on zone change.
- **If the Luna symptom returns** (discovered message with no map node): the bug
  is in `StaticMap::SendObject` / `CheckNavRanges` create emission for an
  exposed-but-unexplored nav (HAS_NAV gating on `AppearsInRadar`), not in the
  entry gate -- investigate there rather than reverting this.
- **Setup**: `just rebuild server` (done) then `just play-local`, fresh char.

### [ ] CV-PB22b -- nav proximity reveal radius widened 1.5x (PB-22)

- **What changed**: `CheckNavRanges` (server/src/ObjectManager.cpp:613) now
  reveals a hidden nav at 1.5x the prior proximity -- both the radar branch
  `(RadarRange + scan_range) * 1.5f` and the visual branch `Signature() * 1.5f`
  (new `reveal_scale = 1.5f` constant). Builds on CV-PB22's proximity reveal.
- **Primary source**: owner live comparison (2026-06-23) -- the prior
  `RadarRange + scan_range` revealed navs noticeably later than the live
  reference server; 1.5x brings discovery distance in line with live.
- **Why the real client is needed**: the CLI cannot fly a ship across a sector
  to observe at what distance a "?" node pops; only the real client confirms
  navs now reveal at the wider, live-matching range (and that nothing reveals
  the entire sector at once -- the CV-PB22 entry-gate behaviour is unchanged).
- **What to verify (real client)**:
  1. **Fresh char, never-visited sector**: still NO "?" nodes at entry for navs
     you have never been near (the 1.5x only changes the *fly-by* reveal radius,
     not the entry gate).
  2. **Fly toward a nav**: the "?" node should now appear ~50% farther out than
     before, matching how early it appears on the live server.
- **Setup**: `just rebuild server` then `just play-local`, fresh char.

### [ ] CV-PB32 -- Swooping Eagle "Default" lvl-0 crash-mob (PB-32)

- **What changed**: (1) NULL-data guards in `server/src/MOBClass.cpp`
  (`CheckWarningShots`, `CheckAggro`, `GetStealthDetectionLevel`) so a mob with
  a missing `mob_base` row no longer faults the sector server; (2) data cleanup
  `db/postgres/fix_orphan_mob_spawns.sql` (run by schema-init in both compose
  files) removes the `mob_spawn_group` rows that referenced non-existent mobs,
  so the unnamed "Default" ghost mob no longer spawns at all.
- **Why the real client is needed**: the CLI cannot enter a sector and fly a
  ship within a mob's range; only the real client proves the crash is gone AND
  that removing the orphan spawns did not leave a visibly broken sector.
- **What to verify (real client)**, after deploy (Build-And-Push + Update-Stack):
  1. **Enter Swooping Eagle (Sirius), fly to Field of Glass Point.** Server must
     NOT crash; no unnamed "Default" lvl-0 mob present. Sit in range for >2 min.
  2. **Confirm the Juoona mobs behave normally** (no zone-wide spaz-chase of a
     phantom target).
  3. **Sanity-check the other 5 affected sectors** if convenient: Carpenter,
     Mars Gamma, Saturn, Lagarto, Unknown Galaxy -- no "Default" mob, no crash.
     Note "Nesshix the Hand" (Unknown Galaxy, spawn 12385) referenced ONLY the
     orphan mob, so that spawn is now empty by design (its mob's stats do not
     exist in any source and cannot be fabricated) -- expected, not a new bug.
- **Setup**: deploy to DO (the live-stack fix is automatic on deploy: new server
  image carries the guards, schema-init auto-deletes the orphan rows from the
  existing prod volume because its command changed and the DELETE is idempotent).

### [ ] CV-AX16 -- baseline guardian turrets visible at gates/starbases

- **What changed**: `db/postgres/seed_turrets.sql` (generated by
  `.claude/skills/import-captures/scripts/gen_turrets.py`) rings every gate
  (type 11) and starbase (type 12) that lacks one with guardian turrets --
  type-42 stationary turrets, model `base_asset_id 14`, matching the existing
  base-data turrets (e.g. sector_object 1733). Applied by schema-init on boot.
- **Why the real client is needed**: the shape matches existing in-game turrets,
  but only the real client proves the new rings actually render at the rings'
  offset positions and read as guardian turrets, not invisible/placeholder.
- **What to verify (real client)**: enter a sector that had NO prior turrets
  (e.g. High Earth) and fly to a gate (e.g. the Tau Ceti gate) and a starbase.
  Confirm 3 turrets ring each gate and 4 ring each starbase, ~2k out, with the
  guardian-turret model and the "Gate/Starbase Guardian Turret" name. Spot-check
  a gate that already had a real turret -- it should NOT have a doubled ring.
- **Setup**: a fresh `docker compose` boot loads them from the first sector load;
  on a live DB they appear only after the sector reloads (re-enter / restart).

### [ ] CV-30 -- HUD no longer lags/flickers on mouse move

- **What changed**: `freya/client-injection/enbmod/src/lua_api.cpp` `tick()` now
  gates the overlay display-list rebuild (`begin_frame` + `on_tick` handlers +
  `commit_frame`) to run at most once per Present, instead of once per
  `PeekMessageA` pump iteration. A mouse-move flood spins the pump several times
  per drawn frame, so the old code re-ran every draw handler ~2-4x per frame
  (wasted CPU -> lag) and widened the window to commit a transient empty list
  between Presents (-> flicker). This is client-only (enbmod, MIT); no wire change.
- **Why the real client is needed**: agent-side measurement proved the rebuild
  rate is now decoupled from the pump (`enb.diag()`: rebuild == present in both
  idle and mouse-flood; pump rate still spikes but rebuild no longer follows), and
  a screenshot showed the HUD renders without a blank frame -- but only the owner
  playing can confirm the *subjective* "no lag, no flicker when I move the mouse".
- **What to verify (real client)**: in space with the Freya HUD up, move the mouse
  rapidly/continuously. Confirm the HUD no longer flickers or stutters and the
  framerate does not visibly drop with mouse motion. (Separate, still-open: the
  top-middle chat-duplicate on new messages -- AV-1/AV-2 -- is NOT addressed here.)
- **Setup**: `just build-enbmod` then relaunch the client (the launcher re-stages
  the DLL next to client.exe on each launch).

### [ ] CV-31 -- No client crash on the action-bar ability-icon (K_TEXQUAD) path

- **What changed**: `freya/client-injection/enbmod/src/overlay.cpp` -- the overlay
  no longer binds a game-owned ability-icon `IDirect3DTexture8*` that the game may
  have freed between the display-list REBUILD (resolve, on the tick thread) and the
  DRAW (in the Present hook). `texture_quad` now `AddRef()`s the texture at resolve
  time so the game's later `Release` cannot take it below 1; the queued command owns
  that ref and `Release`s it when its list rotates out (`begin_frame` / device-reset
  `release_caches`), both on the Present thread. Client-only (enbmod, MIT); no wire
  change.
- **Root cause (the crash)**: the action-bar slot icons are LIVE textures owned by
  the game's UI cards. Lua resolved the card's texture during the rebuild and queued
  the raw pointer; the game then ran a full frame of logic and could free the card
  (ability used/swapped, zoning, logout), Releasing its texture to refcount 0. The
  d3d8 wrapper was destroyed but its freed memory's vtable pointer still read back
  (so the old shallow guard passed), while its wined3d resource was gone -- and
  `DrawPrimitiveUP` then NULL-deref'd it. Captured live as `_except.txt`
  EXCEPTION_ACCESS_VIOLATION read 0x14 at a d3d8 address (`mov edx,[eax+0x14]`,
  eax=0), call chain hk_Present -> draw_frame -> draw_quad -> d3d8. This is a
  PRE-EXISTING bug in the K_TEXQUAD path, NOT introduced by the CV-30 lag fix.
- **Why the real client is needed**: agent-side proved the path is exercised (the
  leftmost action-bar slot, an ability, renders as a tinted icon) and that entering
  space + a mouse-move flood + a soak are now stable with no `_except.txt` and no
  WINE page fault. But the actual free-during-gap trigger (use/swap an ability, or
  zone with the bar populated) cannot be driven from the cmd channel -- only the
  owner playing can exercise it. This is an enbmod-only crash (the K_TEXQUAD path
  exists only when the Lua HUD is loaded), so it is NOT the crash a player (Veret)
  reported -- he plays with Lua/mods off, so enbmod never loads for him.
- **What to verify (real client)**: in space with abilities slotted on the action
  bar, repeatedly activate abilities, swap/re-slot them, and gate between sectors.
  Confirm no client crash (no new `_except.txt`) and the ability icons keep
  rendering.
- **Setup**: `just build-enbmod` then relaunch the client.

## CV-AS-TARGETFRAME -- redesigned bottom-right target frame (bars + name + level)

- **What changed (client-only, MIT)**: the native bottom-right target frame is now
  hidden by default (hide-ui `TargetFrame`) and the Freya HUD repaints the 2D layer
  (`freya-hud/target_frame.lua`): NO glass background, two stacked thin bars at the
  top of the target area (hull red over shield blue, each ~1/3 the player-card bar
  height, no gap, full target-area width, current/total value printed on the bar at
  a reduced scale), and below them the target name + " (L<level>)", wrapping to a
  second line when too wide. The target's 3D model is a separate PIP scene render
  and stays visible behind the new 2D layer.
- **Data**: `enb.target()` (the VEH-guarded current-target read) -> name, hull /
  hull_max (HullPoints / MaxHullPoints), shield_max (MaxShieldPower), shield
  (MaxShieldPower * ShieldPercent when present), level. A station target has no
  hull/shield/level, so its bars render as empty tracks and no level suffix shows
  -- correct.
- **Target TRACKING fix (relaunch required)**: the live target object is captured
  from the radar-highlight setter (`TargetEntitySet`, 0x007bd6e0, arg0 = the resolved
  object, 0 on de-target), which fires on every target switch, and the level from the
  frame display update (`TargetFrameUpdate`, 0x0060e540, param_4). Both still run while
  hide-ui draw-suppresses the native frame (hide-ui only clears the draw-gate bit, not
  the update logic). This is a DLL hook change -> needs a client relaunch to load (Lua
  hot-reloads; the C++ DLL does not).
- **VERIFIED LIVE (this box, post-relaunch)**: cycling targets with the in-space
  target keys, `enb.target()` now returns a DIFFERENT entity on every switch and the
  rendered frame followed it across genuinely different object types -- Decoration ->
  Stargate -> Planet -> Starbase -- with the name + 3D model changing each time
  (screenshots: "Decoration" + station model, then "Stargate" + gate model). Before
  the fix the captured manager went stale and the frame did not update. The level
  field reads junk (an uninitialized stack pointer) for non-combat targets, so it is
  clamped to a plausible range [0,255] in `l_target` and reports no level otherwise
  -- matching the native frame, which shows the "Combat Level" line for combat
  targets only. STILL NOT shown: a FILLED bar tracking damage -- stations / gates /
  decorations expose no hull/shield aux (only mobs / ships do) and the home sector
  has no mob or ship target, so the bars render as empty tracks here (correct). The
  filled-bar + damage-tracking case remains the one open real-client check.
- **Headless coverage**: none -- the mock has no live target entity.
- **Validated live (this box)**: layout proven against the home-sector station
  target (model kept, bg/native-bars/name/dist/arrows gone, two empty stacked bars
  + name below). Bar FILL + cur/total text + level + name wrap proven by temporarily
  stubbing `enb.target()` with fake vitals (842/1200 hull, 310/600 shield, L37,
  long name) -- screenshot-verified. NOT yet validated against a real vital target.
- **What to look for (real client)**:
  - Target a MOB or another SHIP (something with real hull/shield). The hull and
    shield bars must FILL to the right fraction with the live current/total numbers
    on them, and the bars must track damage in real time (drop as you shoot it).
  - The " (L<level>)" suffix shows the target's combat level for a mob; the name
    wraps to a second line for a long name and is single-line otherwise.
  - The 3D model still spins behind the bars; the native glass/bars/name/dist/arrows
    are gone; no flicker, no crash on target/de-target/zone.
  - **Switching targets UPDATES the frame**: select a different mob/ship (or cycle
    with the nav/target keys) and the hull/shield bars + name + level must change to
    the NEW target immediately; de-targeting clears the frame. This is the tracking
    fix above and is the primary thing to confirm.
- **INSTANCE NAME + ENEMY HULL -- FIXED (verified live 2026-06-28)**: the two
  owner-reported defects (frame showed the generic class name "Ship"/"Starbase", and
  enemy targets showed NO hull bar) had the same root cause: the hull/shield AuxData
  and the names live on the object's PROPERTIES CONTAINER at `object+0x88`, not on the
  contact object itself. `enb.target()` now reads, off that container: hull/shield aux
  (so enemies show hull -- live a mob read HullPoints 32.55/32.55 and the on-screen
  frame rendered a full hull bar), the proper INSTANCE name as a char* at
  `container+0x124` (live: "Needlenose" instead of "Ship"), and the class name at
  `container+0x3c` only as a fallback when the instance name is empty. The old code
  read aux + name off the contact object (name at object+0x84), which carries no aux
  and a garbage +0x84 -- that was the bug.
- **WHAT TO CONFIRM (real client, remaining)**: instance name was verified on a ship
  mob only ("Needlenose"); confirm it also reads the proper name on a STATION ("Loki
  Station") and a named NPC ("Scuttlebug") -- same `container+0x124` field, same object
  layout, so expected to work. Tracked in plans/49 AS-13.
- **DISTANCE -- FIXED (verified live 2026-06-28)**: the owner reported the distance was
  "totally wrong, pulling from the wrong spot". The old code used the magnitude of the
  contact's flat +0xE8/+0xEC/+0xF0 offsets, which are NOT world coords. `enb.target().dist`
  now computes the true straight-line range `|player_wpos - target_wpos|` the way the
  native client does: both ends are positional locatables whose world-position getter is
  `vtable+0x24` (struct-return, 3 packed floats), and the local SHIP object is fetched via
  the native target-frame chain (`target_ctrl[0x4c]` targeting subsystem -> `vtable+0x28()`),
  NOT the vitals-chain entity (which is not positional and returns 0,0,0). Live: targeting
  the station just undocked-from (Luna), `enb.target()` returned distinct ship/target world
  positions and `dist=28268`, and the freya-hud frame rendered `Dist 28.27k` on-screen under
  the name. The frame now prints the abbreviated `Dist %.2fk` (owner ask). **DANGER NOTE**:
  the ship object MUST come from that deterministic native chain -- calling `vtable+0x24` on
  a wrong-class object invokes a different virtual method and WEDGES the message pump (froze
  the live client 3x while developing this); the VEH fault-guard does not catch a non-faulting
  wrong call.
- **WHAT TO CONFIRM (real client, distance)**: the `Dist N.NNk` value should match the
  native "Dist:" readout (now hidden) and shrink as you fly toward the target / grow as you
  fly away, updating smoothly. Verify on a mob/ship target as well as the station case.
- **Setup**: `just build-enbmod` then relaunch the client; ensure freya-hud +
  hide-ui mods enabled.

## [ ] CV-AS-VERBDISPATCH -- Freya target-action letter buttons fire the native action

- **What changed (client-only, MIT)**: the redesigned target frame
  (`freya-hud/target_frame.lua`) now draws square single-letter glass buttons above
  the target bars (A Attack, F Follow, L Loot, B Board, T Trade, D Dock), gated by
  the target class (A/F/L/B for ships, T for ship+station, D for station+gate).
  Clicking one calls the new `enb.target_action(op[, no_target])`, which builds the
  server avatar command (`CmdBuild` 0x00876360 / `AutoFollowBuild` 0x0089d070) and
  pushes it via the same sender the chat path uses (`CmdSend` 0x00728150, through the
  sector Connection at `M+0x1124`). Player id reads `M+0x112c`; target gid reads the
  contact's container `obj+0x88 -> +0x90`. Op bytes: Attack 0x0a, Follow 0x0c,
  Board 0x12, Trade 0x14, Loot 0x19, Dock 0x1d.
- **Why this path (not the native verb dispatcher)**: the native verb dispatcher
  `FUN_00658430`'s `this` (the verb-UI object) has no static xref and the Freya
  server rarely emits the `0x5C VERB_UPDATE` packet, so neither reading a persistent
  verb catalog nor hooking the packet handler is reliable. The command path mirrors
  the proven chat-send, so it carries that path's low freeze risk -- but it bypasses
  the client-side verb UI entirely, which is exactly why it needs real-client
  verification.
- **This is NOT a server/proxy wire change**: it emits the same avatar-command
  opcodes the native client already sends when you click a verb button. No server,
  proxy, or login change; nothing to pin in the CLI. The verification is purely
  "does the real client behave correctly when WE synthesize that command".
- **Headless coverage**: none -- requires a live target in space.
- **Validated**: NOT yet live. Code built clean and staged; the freeze-safety plan
  is to fire ONE controlled `enb.target_action` via the enbmod cmd channel against a
  real station/gate target BEFORE trusting the buttons.
- **What to look for (real client)**:
  - Target a station within range, click **T** (Trade) -- the trade/vendor window
    should open exactly as the native Trade verb does.
  - Target a ship/mob, click **A** (Attack) -- weapons engage; click **F** (Follow)
    -- the ship autofollows; click **L** (Loot) on a wreck -- the loot window opens.
  - No client freeze / pump wedge on any button (the command build + send must not
    fault). If any button wedges the client, STOP -- the convention is wrong.
- **Setup**: `just build-enbmod` then relaunch; freya-hud + hide-ui enabled; fly
  within range of a station/gate (and have a mob/ship to test combat verbs).

## [ ] CV-AS-DOCK -- Freya Dock (D) button completes the client-side landing sequence

- **The concern**: the Dock button sends the server dock command (op 0x1d) directly,
  but the native Dock verb ALSO runs a client-side landing sequence
  (camera/Landing_Init transition) that op 0x1d alone does not trigger. The server
  may accept the dock and place the avatar at the station, but the **client station
  transition (the dock/landing screen) may not fire**, leaving the client in space
  while the server thinks it docked.
- **What to look for (real client)**: target a station within docking range, click
  **D**. Confirm the client actually transitions into the station (dock/landing
  screen + station UI), not just that the server logs a dock. If the client stays in
  space, the Freya Dock button must additionally invoke the client landing sequence
  (the dispatcher's `0x20000` path runs `FUN_006...` landing init after the op).
- **Setup**: same as CV-AS-VERBDISPATCH.

## [ ] CV-AZ-1 -- Multibox port-block: docker COUNT=2 re-verify on the new code

- **Change**: multibox redesigned from distinct loopback IPs (127.0.0.N) + socat
  facade to SAME IP (127.0.0.1) + a contiguous 4-port block per instance. The
  in-client ws2_32 `connect()` hook (`netredirect.cpp`) now remaps the client's
  fixed 3500/3801/3805 dials to `FREYA_GAME_PORT_BASE`+0/+1/+2 (posfeed -> base+3
  via `FREYA_POS_FEED_PORT`); `docker-compose.mbox.yml` publishes the block on
  127.0.0.1 and the proxy container keeps stock ports. The earlier socat/distinct-IP
  cut was live-verified COUNT=2, but this new code path has NOT been re-verified.
- **Why a CV entry**: the proxy gained a client-facing listener offset
  (`proxy_client_port()` in `Net7.cpp`/`ServerManager.cpp`/`UDPClient_linux.cpp`).
  On the docker path it stays a no-op (stock ports + docker publish), but the whole
  port-remap chain (client hook -> docker publish -> proxy -> server) must be proven
  end-to-end against the real client, and the server anti-hijack IP-match check must
  still pass with ZERO `Player IP mismatch`.
- **What to look for (real client)**: `just play-multibox-local 2` with two
  accounts. `ss -tnp` shows client 1 dialing `127.0.0.1:{3500,3801,3805}` and client
  2 dialing `127.0.0.1:{23510,23511,23512}` (its block). Both reach `enb.self()==
  table` in-world; server log has ZERO `Player IP mismatch`; each client's position
  feed reaches its own proxy. Single-client (`just play-local`) is byte-identical to
  before (base 3500, hook + offset both no-ops).
- **Setup**: `just build-enbmod && just rebuild proxy`, two seeded accounts
  (boxA/devuser2), `ENB_ACC_1=... ENB_ACC_2=... just play-multibox-local 2`.

## [ ] CV-AZ-2 -- Multibox port-block: NATIVE WINE proxy (play-online) multibox

- **Change**: on the NATIVE proxy path (play-online -- proxy runs in WINE, not
  docker, so there is no docker publish layer), the proxy reads
  `FREYA_PROXY_PORT_BASE` and offsets its OWN 4 client-facing listeners to
  base+0..base+3, matching the client hook's block. `FREYA_PROXY_BIND_ADDRESS` is
  always 127.0.0.1 now. This path CANNOT be tested on the Linux dev box (no native
  WINE-proxy multibox here) -- owner verification required.
- **What to look for (real client)**: two play-online clients on one machine, each
  with its own native FreyaProxy on its own port block, both reach in-world against
  the cloud server with no IP mismatch and independent position feeds.
- **Setup**: owner's play-online multibox path with per-instance
  `FREYA_GAME_PORT_BASE`/`FREYA_PROXY_PORT_BASE` (launcher autodetects a free block
  per instance when unset).
- **Recipe fix (2026-06-30)**: `just play-online N` (N>=2) now also clones a
  per-instance WINE prefix (`~/.wine-enb-mboxN`, cached) and launches each with its
  own `WINEPREFIX`+`FREYA_CLIENT_PATH`, matching `play-multibox-local`. Before this
  it shared one prefix across all instances, which would have interleaved each
  client's enbmod cmd/log channel and clobbered per-instance registry/config -- so
  this is part of what owner verification must confirm actually multiboxes cleanly.

## [ ] CV-AZ-3 -- Multibox port-block: native Windows multibox

- **Change**: the port-block scheme was chosen partly because it is portable to
  native Windows (no 127.0.0.N loopback aliasing, which the old distinct-IP design
  needed). Same client hook + native proxy port offset as CV-AZ-2, but on real
  Windows rather than WINE. Untestable on this Linux box.
- **What to look for (real client)**: two clients on one Windows box multibox
  cleanly with the autodetected per-instance port blocks; both in-world, no IP
  mismatch.
- **Setup**: owner's Windows environment; press Play twice (the launcher autodetects
  the next free 127.0.0.1 port block per instance).

## [ ] CV-BA-2c -- grouped/co-located MVAS position no longer flickers between players

- **What changed**: the MVAS position feed's source. The old in-client feed
  (`freya/client-injection/ClientEngineOffsets.h`) hijacked the engine per-ship
  render loop and captured the transform of ANY object matching a shared
  player-hull signature (`BYTE[EAX]=='S'` + `BYTE[EAX+2]==0x48`), which matches
  every player hull. With a grouped teammate rendered in range, each box's feed
  periodically shipped the OTHER player's coordinates under its own `player_id`,
  so the other client saw the box teleport onto the teammate for ~one update
  every few seconds ("flickers the other player on top for ~15ms then back").
  The render hijack is now DELETED. Position + orientation are sourced from
  enbmod's per-GameID local-ship resolution: `enbmod.dll` publishes the sample
  (`lua_api.cpp publish_ship_state()`, `world_pos(targeting_data_obj())` for
  position, the validated ship-entity transform for heading) and exports
  `FreyaEnbmodShipState`; `FreyaPosFeed.dll` reads that export instead of patching
  client code; the launcher injects enbmod whenever the position feed is enabled.
- **Why a CV is needed**: in-client (FreyaPosFeed.dll + enbmod.dll) change reading
  live engine memory over the loopback MVAS datagram path; the CLI integration
  suite cannot exercise the client engine or two grouped clients, so only the real
  client proves it. Not a server/proxy wire change -- the server-side MVAS handler
  is unchanged (it faithfully relayed whatever the client sent; the bug was the
  client sending the wrong ship's coordinates).
- **What to look for (real client)**: two boxes logged in, grouped, in the SAME
  sector. From each box, watch the other player's avatar/HUD marker for ~60s of
  normal flight: it must track the teammate's real position smoothly with NO
  periodic snap onto your own ship (or between two positions). Also confirm the
  remote ship's FACING rotates as it turns (heading is being fed) rather than
  staying locked -- if it never rotates, the build's transform offset for the
  orientation matrix needs revisiting (position is unaffected either way).
- **Also re-verify (supersedes CV-25's mechanism)**: the old trampoline this
  change removed was the subject of CV-25 (position must keep updating after a
  sector change / entering a planet sub-sector, so you can still dock at a planet
  base). Re-confirm that with the new source: gate to a new sector and fly to a
  planet base -- your position must keep updating server-side and you must be able
  to dock. enbmod's `targeting_data_obj()` must keep resolving across the zone.
- **Setup**: `just play-cli` (or the multibox launch) for two accounts in one
  sector; group them; fly. Runtime launch only -- credentials are not committed.

## CV-AS-GRPBTN -- group frame action buttons (Freya-drawn, native packet paths)

- **What changed (client-only, MIT)**: `freya-hud/party_frame.lua` now draws an
  action-button row ABOVE the party frame (leader `[D]isband [F]ormation [T]arget`;
  member `[L]eave [F] [T]`; F expands a formation sub-row -- leader
  `S`lot-Back/`B`lock/`P`ipe/`X` break, member `U` form-up/`X` leave-formation) and
  the roster panel shifted down below it. Two new DLL calls back it:
  `enb.group_action(action[,target_gid])` builds the group call-to-arms /
  formation request with the client's own constructor and pushes it through the
  Connection (wire opcode 0x00BC `CTARequest{SourceID, TargetID, Action}`,
  TargetID -1 = whole group), and `enb.is_leader()` runs the client's own
  leader check (the one the native group window uses to pick GRP DISBAND vs
  GRP LEAVE). Disband/Leave fire the existing avatar-command path
  (`enb.target_action` 0x0d / 0x0e self-targeted -- byte-identical to the
  `/group disband` / `/group remove` slash verbs). NO server or proxy change.
- **Why a CV is needed**: DLL change (needs a client relaunch to load; Lua alone
  hot-reloads but the buttons gate on the new `enb.is_leader`). The CLI suite
  covers the server's 0xBC handling but cannot click the overlay or prove the
  in-client constructor/leader-check calls are stable across sessions; only the
  real client does.
- **What to look for (real client, two grouped multibox boxes)**:
  1. Leader box shows D/F/T above the party frame; member box shows L/F/T; the
     roster sits just below the row on both.
  2. Leader D disbands the group on BOTH boxes (rosters vanish); re-group, member
     L removes only the member.
  3. F opens the sub-row; leader S/B/P sets the formation type (server chat
     confirms / formation HUD updates), member U forms up, X leaves; leader X
     breaks the formation.
  4. T ("Target my target") makes each OTHER member receive the group chat line
     "Target my target." -- and NOTHING more: the server's GroupAction 12 is a
     stub upstream (the actual retarget push is commented out), so an automatic
     target switch on the other box would be a surprise, not the expectation.
  5. Clicks on the buttons must NOT leak into the 3D scene behind them; no
     crash/fault lines in enbmod.log from `is_leader` polling while solo, docked,
     or during a sector gate.
- **Known caveat recorded**: making GroupAction 12 actually retarget is a server
  change under the full CLAUDE.md gate (primary source + CLI parse/tests first +
  its own CV entry); not part of this change.
- **Live-verify 2026-07-01 (agent-driven, two multibox clients on the new DLL)**:
  group formed via the native command path (invite = avatar-command 0x0a with the
  member's explicit GameID via CmdBuild -- note the `/group invite <name>` slash
  verb submitted through chat_send never reached the server; accept = 0x0b);
  `enb.group()` rosters correct on both boxes, `enb.is_leader()` true on the
  leader / false on the member (and false solo, no fault); `enb.group_action(12)`
  sent clean from the leader, server log clean. Items 2/3/5 (click-through
  behaviour, disband/leave/formation via the ON-SCREEN buttons) + the visual
  row check remain for the owner.
- **RESOLVED 2026-07-01**: owner confirmed the on-screen rows work on the live
  grouped pair ("works great").

## CV-AZ-DUPCHAR -- duplicate-login kick is now per-CHARACTER, not per-account

- **What changed (server, owner-directed 2026-06-30)**: the login path no longer
  rejects a second ONLINE session on the same ACCOUNT. The account-level guard
  (`CheckAccountInUse` in `PlayerManager`, its call in `UDP_Global.cpp`, and the
  IssueTicket-time account check in `AccountManager.cpp`) is removed; the
  per-CHARACTER duplicate check (`CheckForDuplicatePlayers` at SetCharacterID
  time, which force-kicks the OLDER session of the SAME avatar) is retained
  unchanged. Owner call: multiboxing two DIFFERENT characters of one account is
  wanted dev/live behaviour; only the same character twice must still be kicked.
  Recorded in plans/99 as an owner-directed divergence (no retail capture governs
  the account-scoped kick; upstream's guard was itself a fork-era addition).
- **What to look for (real client)**:
  1. Launch two multibox clients on the SAME account, pick two DIFFERENT
     characters: both must reach in-space and stay online simultaneously (no
     force-kick of the first).
  2. On a third launch (or relog of box 2) pick the SAME character as box 1: the
     older session MUST still be force-kicked (the per-character check).
  3. Server log shows no account-in-use rejects; no new Error/WARNING lines on
     the login path.

## CV-BA-MOUSEUNLOCK -- "Lock Mouse to Window" OFF now neuters the client's ClipCursor

- **What changed (client-side enbmod DLL, 2026-07-01)**: unticking the launcher's
  "Lock Mouse to Window" checkbox (BA-4b rework, plans/57) sets FREYA_LOCK_MOUSE=0
  and injects enbmod, whose new hook (`enbmod/src/hooks.cpp`) swallows every
  user32 ClipCursor(rect) call with TRUE and frees any pre-injection grab once at
  init. The old DXGrab registry write was a dead knob (wine-11.8 winex11 has no
  such option) and is deleted; ticked (the default) means the game's native
  confinement runs untouched -- no hook, no injection on its account.
- **Second hook (2026-07-01)**: ClipCursor alone left the pointer still
  teleporting back to centre a few times a second while focused (owner: unlock
  "only half works") -- the client also actively re-warps the pointer with
  SetCursorPos. Added a second MinHook on user32 SetCursorPos under the same
  FREYA_LOCK_MOUSE=0 gate that swallows it. This deliberately disables
  cursor-recenter camera look (a free pointer and a recentering pointer are
  mutually exclusive).
- **Verified so far (throwaway client, 2026-07-01)**: with the box unticked,
  enbmod.log prints "mouse unlock: ClipCursor neutered (FREYA_LOCK_MOUSE=0)"
  and "mouse unlock: SetCursorPos recenter neutered (FREYA_LOCK_MOUSE=0)"; the
  client boots normally to the login screen.
- **What to look for (real client, human at the mouse)**:
  1. Box UNTICKED: while the client has focus (windowed), the pointer must cross
     freely OUT of the client window -- e.g. straight onto the second multibox
     client -- during login, char select, in-space, and while a station UI is up,
     AND must NOT snap/teleport back inside a fraction of a second later.
  2. Box TICKED (default): confinement behaves exactly as it always did (pointer
     held inside the focused window); no enbmod injection happens if Lua mods,
     auto-login, and the position feed are all off.
  3. No new WARNING/fault lines in enbmod.log from the hooks.
  4. UNTICKED, camera behaviour: right-drag camera-look that relied on
     cursor-recenter will change (the pointer no longer snaps back). Confirm this
     is acceptable for the unlock use case -- if the owner wants recenter look
     preserved while unlocked, the SetCursorPos swallow needs a look-drag gate.
     TICKED: camera-drag unchanged.

## CV-BA-PARTYTARGET -- clicking a party-frame member row targets that member

- **What changed (client-side enbmod DLL + freya-hud, 2026-07-01)**: clicking a
  member row in the Freya party frame now selects that member as the target, as
  long as they are targetable (in the same sector / in scanner range); a member
  who is not present is skipped (no-op, click still swallowed).
- **How it works (no server or proxy change)**: a new DLL primitive
  `enb.request_target(gid)` (`enbmod/src/lua_api.cpp`) resolves the GameID to its
  live contact object via the same gid->object hash walk `enb.group` uses, then
  calls the client's own target-request routine (`addr::RequestTarget = 0x00723230`,
  `enbmod/src/game.h`). That builds a REQUEST_TARGET (wire opcode 0x17) packet from
  the object's GameID and pushes it through M's sector-server Connection -- byte-for
  -byte the same path a click on the object in space takes. The server's stock
  `HandleRequestTarget` (server/src/PlayerConnection.cpp) validates the target via
  `GetObjectManager()->GetObjectFromID()` and replies SET_TARGET (0x19), which the
  client applies natively (target frame, threat text, highlight). "Skip if not
  targetable" falls out for free: a member in another sector resolves to no live
  entity, so `enb.request_target` returns false and sends nothing.
  `party_frame.lua` records one clickable rect per drawn roster row (buttons above
  keep priority) and calls the primitive on left-button-down.
- **Verified so far (static, 2026-07-01)**: `FUN_00723230` reads `obj + 0x90`
  (== `world::tgt_gid`, the contact object's own GameID) into the packet and sends
  via `CmdSend` -- the same M and Connection enb.target_action/enb.group_action
  already drive. enbmod builds clean (`just build-enbmod`, no warnings). Server side
  is stock EnB targeting (unchanged), so no server/proxy/CLI wire change and no new
  opcode -- the CLI already need not parse anything new.
- **What to look for (real client, human at the mouse, two grouped chars)**:
  1. Group two characters in the SAME sector. Click the other member's row in the
     party frame: they must become your selected target (target frame populates
     with their name/level/threat, ship highlights), exactly as clicking them in
     space would.
  2. Move the other member to a DIFFERENT sector (still grouped). Their row either
     drops from the roster (no live entity) or, if still listed, clicking it does
     nothing and the click is swallowed (no crash, no wrong-target, no fall-through
     to the 3D scene). enbmod.log shows "target: <name> (not in range)".
  3. Clicking a member row must NOT also fire a group action button (the button
     rects are tested first) and must NOT rotate/click the ship behind the frame.
  4. No new WARNING/fault lines in enbmod.log; server log shows the normal
     REQUEST_TARGET handling with no Error/WARNING.

## [ ] CV-AS-LOOT -- Freya LEFT loot readout reads a real hulk byte-correct; native loot still works

- **What changed**: new `mods/freya-hud/loot_frame.lua` (LEFT-anchored) + DLL
  binding `enb.loot()`/`enb.loot_age()`. The binding captures the hulk cargo
  CONTAINER read-only from a naked ECX hook on the per-slot ItemTemplateID accessor
  (`game::addr::CargoTemplateID = 0x00689ca0`) and replays the three accessors
  (`0x00689ca0` id / `0x00689d20` stack / `0x0068aaa0` template) to enumerate
  occupied slots. Source: behavioural analysis of the retail client's hulk-inventory
  panel path (the same accessors the native loot grid calls each repaint).
- **READ path: no server/proxy/CLI wire change** -- a client-side READ of an object
  the client already builds; nothing new goes on the wire. **LIVE-VALIDATED
  2026-07-02**: a prospected asteroid read "Copper Ore x5" byte-correct with
  `src=HarvestableResources`. Header simplified to "Loot"; a `src`-prefix filter now
  drops the player's own holds (`Inventory*`) so only lootable containers show.
- **TAKE path (added 2026-07-02; premise re-corrected same day)**: clicking a Freya
  row's icon tile calls `enb.loot_take(slot)`, which builds + sends the SAME command
  the native mining/loot take emits -- an **INVENTORY_MOVE, wire opcode 0x0027**
  (six u32 fields: GameID, FromInv, FromSlot, ToInv, ToSlot, Num), in-place ctor
  `InvMoveBuild 0x00894ca0` (serializer `0x00894e30` stamps 0x27), pushed via
  `CmdSend 0x00728150`. For a prospected asteroid `FromInv=18 (0x12)` -> server
  `HandleInventoryMove` case 18 -> MineResource. Primary source: behavioural analysis
  of the retail client's inventory-transaction commit (start `0x00690aa0` -> commit
  `0x00690d20`), which does `InvMoveBuild(*(C+0x1130), FromInv, FromSlot, ToInv,
  ToSlot, Num) + CmdSend(C, cmd)` where `C = *(container+0x4c)` is the SClient that
  owns the open container. This is NOT a new packet and NOT a server/proxy change --
  the client already sends this exact command on a native mining take; the DLL only
  drives the same builder + sender. **The abandoned first attempt used the compact
  loot command 0x005D (`LootBuild 0x008ae810`) AND routed the send through M's
  connection -- both wrong: the mining take is 0x0027, and an inventory command sent
  through M (the world-manager) does NOT transmit; it must go through the
  container-owner SClient's Connection.** GameID + holder now derived off the
  container per the native commit; inv_type from the container name (`Harvest...`=0x12,
  else hulk cargo 0x06); command object allocated with the client operator new
  (`0x004e9cb0`) and NOT freed so the Connection send path frees it through the
  matching heap (cross-heap free would crash).
- **What to look for (real client, human at the mouse, at a prospected hulk)**:
  1. Prospect/kill something to spawn a hulk, open its loot -- a LEFT-side Freya
     "Loot" panel appears listing one row per occupied slot: correct 1-based Slot
     number, correct item Name, and "xN" stack for N>1. Cross-check every field
     against the native loot grid on the right -- they must match exactly
     (VALIDATED: Copper Ore x5). 
  2. Click a Freya row's icon tile -> that slot is looted into your hold (the item
     leaves the hulk grid / your cargo gains it), exactly as clicking the native
     loot entry does. No crash, no fault line in enbmod.log. Loot each of several
     slots to confirm the slot index maps correctly (loot slot 2, not slot 1).
  3. Close the loot window: the Freya panel must DISAPPEAR within ~5s (the
     `enb.loot_age()` staleness gate) and never read a freed container.
  4. Open your own ship inventory (NOT a hulk): confirm the panel does NOT appear
     (the `src` `Inventory*` filter now suppresses it).

## [ ] CV-BA-LOOT -- server accepts the mine/husk-loot INVENTORY_MOVE (AJ-3 regression fix)

- **What changed (SERVER)**: `server/src/PlayerConnection.cpp` `HandleInventoryMove`
  (opcode 0x0027). The AJ-3 security hardening added a destination-slot range check
  that silently rejected every mine/husk-loot take: source codes `FromInv==18`
  (mine/harvest -> MineResource) and `FromInv==6` (husk-loot -> LootItem) send
  `ToSlot==-1` and never index it (the case body places the item via `CargoAddItem`,
  which auto-selects a free cargo slot), but the `auto_select_dest` sentinel
  whitelist only covered the cargo/vault/vendor/manu combos, so `ToSlot==-1` failed
  the `>=0 && < dst_slots` check and returned early via the dead `LogDebug` no-op
  (= silent). Fix: `bool dest_not_indexed = (FromInv==6 || FromInv==18);` added to
  the guard so those two source codes skip the destination range check.
- **Why this is a correctness change, not a weakening**: it RESTORES pre-AJ-3
  behaviour (the real retail server accepted these takes). ToSlot is never used as
  an index for source codes 6/18, so exempting them from the destination range
  check cannot violate any bound -- it only stops rejecting a legitimate take. The
  FromSlot range check and every other AJ-3 bound remain in force. Primary source:
  the client's native mining/loot take emits exactly this 0x0027 with `ToSlot==-1`
  (see CV-AS-LOOT behavioural analysis of commit `0x00690d20`), and the server's
  own case 18/6 bodies never read ToSlot -- so rejecting it on ToSlot was the bug.
- **Understanding-before-change (CLAUDE.md B)**: the 0x0027 INVENTORY_MOVE wire
  format is already parsed + pinned in the CLI (`SectorInventoryMoveTests`, the AJ-3
  OOB tests). **Follow-up owed**: add the positive mirror -- a `FromInv=18,
  ToSlot=-1` move is ACCEPTED and round-trips -- so the regression cannot recur
  silently. (Deferred; not yet written.)
- **What to look for (real client, human at the mouse, devuserje the LV75 miner)**:
  1. Prospect an IN-RANGE asteroid, open the Freya loot panel, click a loot row.
     The ore is added to your cargo (stack count rises / hold gains the item), the
     same as clicking the native loot entry. Before the fix this did nothing at all
     (silent server-side drop).
  2. If a take still fails, it now fails VISIBLY: MineResource's range/condition
     gates `SendMessageString` a reason to the client (e.g. out of prospect range),
     never a silent no-op. A visible message = a DIFFERENT, diagnosable condition,
     not this bug.
  3. No dupe / no crash across repeated takes; cargo count matches the ore actually
     mined.

## [ ] CV-BC-GROUPINVITE-CRASH -- invitee client no longer crashes on group invite

- **What changed (CLIENT, freya/ MIT)**: `freya/client-injection/enbmod/src/lua_api.cpp`,
  `l_is_leader`. The freya-hud party frame calls `enb.is_leader()` every frame while a
  group exists. `l_is_leader` called the client-native leader check
  (`group::is_leader`, 0x0074d690) after only verifying `base+container_off` was a
  readable non-null pointer. The instant a group INVITE lands, the invitee's client
  holds a GroupInfo entry (server sets the invitee's GroupID before they accept -- see
  `server/src/GroupManager.cpp` GroupInvite) whose internal pointers are not yet
  constructed. The native check walked that half-built object and dispatched through an
  uninitialized vtable -> control flow jumped into zeroed/unmapped memory
  (`page fault on read at 0x00000001`, EIP in a zero page executing `00 00`). The
  enbmod setjmp/VEH guard catches READ faults but NOT a wild jump, so it crashed the
  client outright the moment the invite arrived.
- **The fix**: gate the native call behind the SAME validation the proven-safe roster
  read (`l_group`) uses -- resolve the group via `get_object(container, "GroupInfo")`
  and require the group's active flag (`+0x70`) set AND its member-array end (`+0x678`)
  readable BEFORE calling `is_leader`. Until the client finishes building the group,
  `l_is_leader` reports not-leader (correct: you are not the leader of a group that is
  not active yet). No wire/protocol change; pure client-side mod hardening.
- **Root-cause evidence**: gdb backtrace of the crashed WINE `client.exe` (ptrace_scope
  lifted), Wine's saved CONTEXT decoded off the crashing thread's stack; enbmod frames
  resolved via `x86_64-w64-mingw32-nm` -- fault chain ran through enbmod's
  `l_is_leader` -> `call_thiscall(group::is_leader,...)`. See memory
  `group-invite-crash-is-enbmod-is-leader` + `reading-wine-crash-backtrace-on-this-box`.
- **Why the CLI can't validate it**: this is client-side injected-mod UI logic driven
  off the client's own message pump against live client memory; no server/proxy/CLI
  packet is involved.
- **What to look for (real client, TWO windows, multibox devuserte + devuserje)**:
  1. From devuserte, `/invite devuserje` (in-game chat). devuserje's client must NOT
     crash when the invite/half-built group state arrives.
  2. Accept on devuserje. The group forms; both party frames render; `enb.is_leader()`
     returns a sane value on both once the group is active.
  3. Leader-only buttons appear only for the actual leader (no false leader while the
     group is still building).

## [ ] CV-BD-OVERLAY-STATEBLOCK-LEAK -- HUD no longer leaks a D3D state block every frame

- **What changed (CLIENT, freya/ MIT)**: `freya/client-injection/enbmod/src/overlay.cpp`,
  `draw_frame`. The overlay snapshots the game's device state around its own 2D pass so
  the HUD's render states never leak into the game's next frame. It did this by calling
  `dev->CreateStateBlock(D3DSBT_ALL, &sb)` at the top of every drawn frame and
  `DeleteStateBlock(sb)` at the bottom -- i.e. it allocated and freed a FULL-device
  state block 30-60x per second, but only while the HUD was actually drawing (an empty
  display list early-returns before the state block). Under wined3d that per-frame
  create/delete churn grew committed D3D (file/shared) memory smoothly and without
  plateau, HUD-gated. For a 32-bit `client.exe` (WOW64) that boots at ~3.45 GB VmSize
  with only ~500 MB of address-space headroom, hours of that growth exhausts the space
  and the next ~4 MB reservation fails -> `err:virtual:allocate_virtual_memory out of
  memory` storm -> `Unhandled page fault on read access to 00000001`. This is the
  leader/te crash (the group-invite fix CV-BC is a DIFFERENT, opposite crash -- invitee,
  control-flow corruption, not address-space).
- **The fix**: create ONE state block lazily on first draw, then `CaptureStateBlock` it
  each frame before our SetRenderState calls and `ApplyStateBlock` after -- reusing a
  single allocation instead of Create/Delete per frame. Released in `release_caches`
  (device reset/unload) alongside the texture caches. No wire/protocol change; pure
  client-side mod fix.
- **Measurement evidence (this box, WINE, in-space, HUD on, ungrouped)**: controlled
  on/off/on toggle with a second client as control isolated the growth to the HUD and
  to file/shared (wined3d) mappings, not private heap. OLD dll: RSS +2790..+3022
  kB/min, smooth and monotonic over an hour. FIXED dll (13.1 min, 40 samples): RSS
  +214 kB/min, anon +5 kB/min, and the file/shared series was FLAT (262232 kB) except a
  single one-time +2 MB asset step -- the smooth per-frame linear climb is gone. ~93% of
  the HUD-gated growth eliminated; the residual is discrete/base-client asset churn.
- **Why the CLI can't validate it**: client-side D3D8 present-hook rendering under
  wined3d; no server/proxy/CLI packet is involved. Only the real client over a LONG
  session proves the address space no longer fills.
- **What to look for (real client, LONG session, HUD on)**: play for several hours with
  the Freya HUD visible (party frame + vitals, the normal case). The client must NOT
  accumulate VmSize toward the ~4 GB WOW64 ceiling and must NOT die with the
  `allocate_virtual_memory out of memory` / `page fault on read at 00000001` signature.
  Sanity check: `grep -c allocate_virtual_memory` in the WINE stderr should stay ~0.

## [ ] CV-GROUPNAV -- formation exploration shares the nav MAP REVEAL, not just XP

- **What changed (SERVER, CC BY-NC-SA)**: `server/src/GroupManager.cpp` (new
  `PlayerManager::GroupRevealNav`), called from
  `server/src/PlayerExperience.cpp` `Player::AwardNavExploreXP` right after the
  existing `GroupExploreXP` call. Header decl in `server/src/PlayerManager.h`.
- **The bug (internal inconsistency)**: when a grouped player explores a nav, the
  server already fans the explore XP AND the "Discovered &lt;nav&gt;" message out
  to every in-range formation member (`GroupExploreXP`, `GroupManager.cpp:339`),
  but the nav OBJECT + 0x0099 NAVIGATION map entry were sent to the DISCOVERER
  only -- `ObjectManager::CheckNavRanges` (`ObjectManager.cpp:613`) reveals
  per-player by proximity and iterates nobody's group. So a formation member was
  credited with discovering a nav (XP + message) they could never see on their
  map. Observed live in multibox: the follower earned group explore XP for a gate
  the leader reached in formation while the gate stayed off the follower's map,
  and flying back through the area did not help.
- **The fix**: `GroupRevealNav` mirrors `GroupExploreXP`'s member gating verbatim
  (same sector, within 40k of the owner, active &amp; not incapacitated) and, for
  each member that does not already have the nav, sets its
  `ExposedNavList`/`ExploredNavList`, persists the discovery
  (`SaveDiscoverNav`/`SaveExploreNav`), and sends the SAME `SendObject` +
  `SendNavigation` the discoverer got. Reveal set == XP set, so they can never
  drift. No-op for a solo (ungrouped) player.
- **Primary source (CLAUDE.md clause A)**: (1) internal inconsistency above -- the
  server already asserts the member discovered the nav while withholding it; (2)
  owner first-hand retail testimony that EnB formation flight shared exploration
  (map reveal), not merely XP. No capture of the retail follower's CREATE stream
  was available; proceeding on (1)+(2) per owner direction.
- **Why the CLI can't fully validate it (clause B)**: the emitted packets (0x0004
  object create, 0x0099 NAVIGATION) are byte-format-pinned already
  (`SectorNavigationHardeningTests`) -- this change only widens the RECIPIENT set,
  not any byte layout. The fan-out is observable only at a second grouped avatar,
  which is blocked by Net7Proxy single-tenancy (see
  `TwoPlayerGroupNavExploreShareTests`, `[Fact(Skip)]`, same wall as the other
  two-player tests). The regression is pinned in correct shape for when the proxy
  multiplexes.
- **What to look for (real client, TWO grouped chars)**: group two characters,
  put them in formation, and fly the LEADER to a nav/gate the FOLLOWER has never
  personally explored. The follower must (a) get the group explore XP + "Discovered"
  message as before, AND (b) now see that nav/gate appear on its own map, marked
  visited. Re-log the follower: the nav must persist (it was saved to the
  follower's explored list). Solo (ungrouped) exploration must be unchanged.

### [ ] CV-MVAS-ORIENT-BCAST -- Other players' ships visibly TURN (nose facing) while flying, not just slide

- **Bug (owner-observed, live multibox)**: when you watch another player fly past
  in your sector, their ship's POSITION updates smoothly but its ORIENTATION never
  changes -- the hull slides through space frozen at whatever facing it had when it
  came into range. Their rotation is not replicated.
- **Root cause**: under the MVAS position feed the server refreshes
  `m_Position_info.Position` every frame (`UpdatePositionFromMVAS` -> `SetPosition`)
  but NEVER refreshes `m_Position_info.Orientation`: `CalcNewHeading` is skipped
  while `IS_PLAYER(m_MVAS_index)` (`CalcNewPosition`, `PlayerClass.cpp`), and the
  MVAS feed deliberately stores the client nose vector only in the decoupled
  `m_ClientHeading` (firing-arc use, see CV-MVAS-ORIENT). So the 0x003E
  ADVANCED_POSITIONAL_UPDATE the server fans out to every in-range observer
  (`Player::SendToVisibilityList` -> `SendAdvancedPositionalUpdate`) carries a
  stale/spawn quaternion. Observers get correct position, frozen facing.
- **Change (`server/src/PlayerClass.cpp` `SendToVisibilityList`)**: on the OUTBOUND
  broadcast only, convert the already-fed nose vector (`m_ClientHeading`) to the wire
  quaternion using the engine's own look-rotation (`Object::CalcOrientation(target,
  self, set_heading=false)`, yaw+pitch, roll leveled) and send THAT, then restore
  the stored orientation before returning -- exactly mirroring the existing transient
  `RotZ`/`UpdatePeriod` mutate-restore in the same function. The override is strictly
  on the broadcast copy: it never enters `Orientation()` persistently, never touches
  `m_Velocity`/`m_Position_info.Velocity`, and cannot reintroduce the CV-MVAS-POS
  phantom-velocity warp regression (the whole reason facing was decoupled).
- **ROLL IS NOT AND CANNOT BE REPLICATED -- primary source**: the retail MVAS
  `0x1004` position feed carries position xyz + nose-heading xyz and NOTHING ELSE
  (live Net7Proxy capture, `proxy/local-debug` Combat-*.pcapng; the server reads
  only the 6 floats -- `UDP_MVAS.cpp` / `proxy/UDPClient_linux.cpp` comment). There
  is no roll component anywhere on the retail flying-client -> server wire, so no
  observer in retail ever saw another ship's true roll either. Replicating nose
  direction with roll leveled is the MOST any observer could faithfully have seen;
  fabricating a roll channel would be a divergence from retail, not a fix, and is
  therefore deliberately NOT done.
- **Primary source (CLAUDE.md clause A)**: (1) internal inconsistency -- the server
  advances a player's broadcast position every frame while broadcasting a frozen
  orientation for the same player, which is self-contradictory; (2) the nose vector
  is already present server-side (the same `m_ClientHeading` proven by the `0x1004`
  heading capture, CV-MVAS-ORIENT) -- this change only routes existing data onto the
  existing 0x003E Orientation slot for observers; (3) owner first-hand retail
  testimony that other players' ships visibly turned. No new wire field, no widened
  input acceptance, no loosened gate.
- **Why the CLI can't fully validate it (clause B)**: the 0x003E byte layout is
  already pinned (`SectorAdvancedPositionalUpdateHardeningTests`); this changes only
  the VALUE of the Orientation quaternion for a moving player, observable solely at a
  SECOND avatar within range while the first flies under the MVAS feed. Blocked by
  Net7Proxy single-tenancy + the absence of an in-test movement driver -- identical
  wall to `TwoPlayerGroupNavExploreShareTests`. Pinned in correct shape by
  `TwoPlayerRemoteFacingReplicationTests` (`[Fact(Skip)]`) for when the proxy
  multiplexes.
- **What to look for (real client, TWO chars)**: put two characters in the same
  sector within visual range. Fly char A in circles / bank turns while char B
  watches. B must now see A's ship NOSE swing to match A's flight direction
  (yaw+pitch), not merely translate. Roll will look leveled -- that is expected and
  correct (see the roll note above). CRUCIAL regressions to confirm ABSENT on the
  moving client A itself: warp still engages, target locks stay stable, no phantom
  drift -- i.e. the broadcast-only override did not leak into A's motion path.

## [ ] CV-BC-FORMATION-GATE -- group formation auto-carries + auto-reforms through a gate

- **What changed (SERVER)**: `server/src/GroupManager.cpp` (`PlayerManager::GroupLeaderGate`,
  `GroupReformOnGate`, + `SavedFormation` maintenance in `SetFormation`/`BreakFormation`),
  `server/src/PlayerConnection.cpp` (gate-arm case 18), `server/src/PlayerClass.cpp`
  (`FinishLogin`), `server/src/PlayerManager.h` (new `Group::SavedFormation` + decls). New
  behaviour the retail server did not have (owner-authorised 2026-07-05): when a group LEADER
  gates while flying a formation, the server carries every FORMED member in the leader's sector
  through the same stargate (`SectorServerHandoff` to the leader's destination), and on the far
  side re-establishes the formation -- the leader re-forms every member present, each member
  rejoins (FormUp) when it lands. Unformed group members are NOT carried. A member gating solo
  still just drops out of the formation. A deliberate Break Formation clears the saved formation
  so it does not auto-restore.
- **Why no CLI byte-pin**: no new wire format. It reuses packets the client already parses
  (SectorServerHandoff / SendAuxPlayer / the formation positional updates SetFormation+FormUp
  already emit) to drive new server logic. Per the updated server rules (CLAUDE.md, owner
  2026-07-05) that needs no capture citation -- there are no new bytes to pin.
- **Why the CLI/integration suite can't validate it**: it needs a live MULTI-CLIENT group
  (leader + members, formed, crossing a gate together). The proxy is single-client (one per
  client) and the suite has no in-space movement/formation driver -- same wall as the two-player
  group nav tests. Hence this real-client entry.
- **What to look for (real client, >=2 chars, LOCAL dev clients only -- `just play-cli` per
  client; NEVER the owner's live game)**:
  1. Group two chars, leader initiates a Slot Back / Block / Pipe formation, member forms up.
  2. Leader gates. The formed member is pulled through the SAME gate (no manual gate on the
     member). Both land in the destination sector.
  3. On the far side the formation visibly re-establishes (member snaps back into the slot),
     once, with no CTA spam. Confirm a grouped-but-UNFORMED third char is left behind, and that
     a member gating on its own just leaves the formation (old behaviour intact).
  4. Break Formation, then gate -- confirm it does NOT auto-reform on arrival.
