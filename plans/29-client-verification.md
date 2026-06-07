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
  `tests/integration/.../Handshake/TlsLoginTests.cs`; the accept (genuine ticket)
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
  (`tests/integration/.../Opcodes/TwoPlayerChatSenderSpoofTests.cs`) is written
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
  (`tests/integration/.../Opcodes/SectorInventoryMoveTests.cs` --
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
  `tests/server/protocol/header_layout_test.cpp` (`EnbUdpAuthWrapperIs17Bytes`),
  C# `tools/cli-client/.../Net/AuthWrappedPacket.cs` +
  `.../CliClient.UnitTests/Net/AuthWrappedPacketTests.cs`, and the accept/drop
  decision gtest `tests/server/protocol/auth_wrapper_test.cpp`.
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
> (player griever, sector 10151). Sector entry succeeds; no +15s kick.

- **Change**: the launcher (`tools/launchnet7-avalonia/Launcher.cs::LaunchNet7Proxy`)
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
