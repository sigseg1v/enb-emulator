# Phase AI -- On-demand sector lifecycle (lazy start + idle teardown)

Goal: stop paying RAM for ~130 sectors that nobody is in. Today every sector
is fully content-loaded, UDP-bound, and thread-started at boot. Make a sector
go live only when a player is first handed off to it, and (Stage 2) tear it
back down when it has been empty for a while.

Baseline (v0.2 tag `before-server-optimizations`): settled RSS ~975 MiB.
Already landed (pre-Phase-AI, separate commits, local):
- `MALLOC_ARENA_MAX=2` in docker-compose -> 975 -> ~910 MiB (commit 4ec7a656)
- lazy ObjectManager temp pools (mining husks/corpses) -> ~910 -> ~861 MiB (commit 6538d826)

**Stage 1 RESULT (2026-06-06): settled boot RSS ~517 MiB** (down from ~861).
Lazy MOB/field load deferred galaxy-wide; only the nav skeleton + metadata are
resident at boot. ~344 MiB reclaimed. Verified: server boots clean, mission
catalog generates correctly across sectors (proves cross-sector skeleton reads
work), handoff cold-starts the target sector on demand (SectorServerHandoff
integration test passes).

## Architecture facts established (read before editing)

- Heavy load is `ServerManager::m_SectorContent.LoadSectorContent()` (no-arg)
  at `ServerManager.cpp:234` -> `ParseSectorContent(-1)` -- loops ALL sectors,
  for each loads the `sectors` row (metadata) AND the heavy `sector_objects`
  join (navs/stations/asteroid fields/MOBs/spawns) into that sector's
  `SectorData::obj_manager` + the global `g_SectorObjects` map.
- `SectorData` (per sector, stored in `m_SectorList[sector_id]`) holds BOTH
  metadata (name/system/boundaries/params) AND the `obj_manager` pointer.
- `GetSectorData(sector_id)` is read for ARBITRARY sectors a player is not in
  (MissionManager.cpp:1033, ItemBaseManager.cpp:185, PlayerExperience.cpp:118,
  SectorManager.cpp:708/719/732 by name/id). => metadata for ALL sectors must
  stay resident; only the per-sector OBJECTS may be deferred.
- `SectorManager::SetupSectorServer(sector_id)` (SectorManager.cpp:177) is the
  per-sector "go live" step: points `m_ObjectMgr = m_SectorData->obj_manager`,
  `InitialiseResourceContent()`, `InitialiseSector()`, `m_SectorCount++`.
- `BeginSectorThread()` (SectorManager.cpp:75) pthread_creates the event thread
  + `StartListener(port)` binds the UDP socket and starts the recv thread.
- Handoff funnel: client MasterJoin -> proxy -> UDP `0x2008` ->
  `UDP_Master::ProcessHandoff` (UDP_Master.cpp) -> `LookupSectorServer` ->
  `GetSectorManager(sector_id)->GetPort()` (refuses if `port<=0`) -> port goes
  in the `0x2009 MASTER_HANDOFF_CONFIRM`. This single path covers BOTH initial
  login and gate jumps.
- Player presence: `m_PlayerCount`/`m_PlayerList` on SectorManager, inc in
  `AddPlayerToSectorList` (SectorManager.cpp:398), dec in
  `RemovePlayerFromSectorList` (:416); `GetOccupancy()` (:1448).
- `g_SectorObjects` (global `map<long,Object*>`, ObjectManager.cpp:29): inserted
  in SectorContentSQL.cpp (`g_SectorObjects[object_uid] = current_object`) keyed
  by object UID; NEVER purged on unload today -- the central use-after-free
  hazard for Stage 2. All lookup sites null-check, so erasing a sector's UIDs on
  teardown is sufficient + safe.

## CROSS-SECTOR OBJECT ACCESS IS PROVEN -- naive object deferral is UNSAFE (2026-06-06)

The owner asked: can we PROVE cross-sector object access occurs, or do sectors
only share a datastructure? PROVEN -- it occurs, in three distinct ways, all
reading/writing a *remote* sector's `ObjectManager`:

1. **Mission generation reads remote gates + planets.**
   `MissionManager::PickMissionSector` (MissionManager.cpp:296-342) walks the
   gate graph across sectors the player is NOT in: `FindGate()` ->
   `stargate->Destination()` -> `GetObjectManager(dest_sector)` -> `FindPlanet()`.
   `GetObjectFromName` on a remote node_manager at :985. This runs AT BOOT
   (`m_Missions.LoadMissionContent()`), before any player connects.
2. **Gate-sealing reads the far-side gate object.** `g_SectorObjects[gate->
   Destination()]` (SectorManager.cpp:674, PlayerConnection.cpp:7395/11277) reads
   a gate that lives in the destination sector.
3. **Combat missions WRITE a MOB into a remote sector.**
   PlayerMissions.cpp:1856 `GetObjectManager(jn->Obj->GetSector())` ->
   `AddNewObject(OT_MOB)` -- pre-places the objective MOB into a sector the
   player has not yet entered.

CONSEQUENCE: a naive "defer ALL per-sector objects until first entry" breaks
mission generation (remote `obj_manager`s would be empty, `FindGate`/`FindPlanet`
return null) and the first-visit `DeleteAllObjects()` would wipe any pre-placed
mission MOB. So the split MUST be TYPE-AWARE:
- **Resident galaxy-wide (the "nav skeleton")**: stargates (type 10/11), planets
  (3), stations (12), deco/navs (37), gravity wells (41), radiation (40).
- **Deferred to first entry**: MOBs (0/1/42, incl. turret-promoted) and asteroid
  fields/resources (38).
The classification is a pure function of the `sector_objects.type` column (after
the turret_mob_id promotion to type 1); see `is deferred_type` in
SectorContentSQL.cpp ParseSectorContent.

## Deterministic ports (owner directive, 2026-06-06)

Port is `SECTOR_SERVER_PORT (3501) + the manager's pool-slot index`
(`m_SectorNumber`, fixed at boot in the standalone setup loop). REQUIRED, not
cosmetic: the old `GetSectorPort()` sequential `++m_Port` allocator only matched
the reported-port formula `m_Port + GetSectorNumber()`
(ServerManager::SetSectorServerReady) because BeginSectorThread ran in slot order
at boot. Lazy/out-of-order start breaks that -> drop the mutating allocator, bind
`base + GetSectorNumber()` in BeginSectorThread (same formula the report uses, so
they agree). Routing reads the live bound `m_Port` via LookupSectorServer anyway,
so bound and reported never diverge. **No `m_Started` flag needed**: `m_Port`
stays -1 until StartListener binds, so the existing `port<=0` guard in
LookupSectorServer remains the liveness check (EnsureSectorStarted binds before
ProcessHandoff reads the port).

## Stage 1 -- lazy start  [x] (2026-06-06)

- [x] AI-1: TYPE-AWARE split (was "metadata only" -- corrected after proving
  cross-sector access, above). `LoadSectorMetadata()` ->
  `ParseSectorContent(-1, SECTOR_LOAD_SKELETON_ONLY)` loads `sectors` rows +
  the nav skeleton for ALL sectors at boot; defers MOBs/fields. Three load modes
  (`SectorLoadMode` enum, SectorContentParser.h): SKELETON_ONLY (boot),
  DEFERRED_ONLY (first entry, no DeleteAllObjects -- preserves skeleton +
  pre-placed mission MOBs), ALL (GM /rsectors|/rsectorall reload, wipes+reloads).
  Per-object skip in ParseSectorContent keyed on `deferred_type`. Boot call at
  ServerManager.cpp:237.
- [x] AI-2: Deterministic port. `GetSectorBasePort()` (immutable
  SECTOR_SERVER_PORT); BeginSectorThread + ReStartListener bind
  `base + GetSectorNumber()`. Removed mutating `GetSectorPort()`.
- [x] AI-3: NO `m_Started` flag needed -- `m_Port` stays -1 until bind, so the
  existing `port<=0` guard in LookupSectorServer is the liveness check.
- [x] AI-4: `ServerManager::EnsureSectorStarted(long)` -- lock-free O(1)
  GetSectorManager fast path, then a SINGLE GLOBAL `m_SectorStartMutex` (NOT
  per-sector: the manager-pool claim from GetSectorManager(-1) and the
  g_SectorObjects population are global shared state; cold starts are rare,
  ~tens of ms). Double-checked. Loads DEFERRED_ONLY -> SetupSectorServer (points
  m_ObjectMgr at the boot-created obj_manager already holding the skeleton) ->
  BeginSectorThread.
- [x] AI-5: Hooked `EnsureSectorStarted` before LookupSectorServer in
  ProcessHandoff (UDP_Master.cpp:82); gate/dock online-checks
  (SectorManager.cpp:625 dock, :693 gate) use `!EnsureSectorStarted(dest)`.
- [x] AI-6: Boot no longer drives assignment -- `m_SectorAssignmentsComplete =
  true` immediately, master + MVAS receivers start at once
  (ServerManager.cpp:286-307). The auto-assign branch (ServerCheck ->
  CheckConnections -> ValidateSectorServer) is now DEAD (gated off). FOLLOW-UP
  CLEANUP: that whole distributed-cluster assignment path is dead scaffolding --
  delete it in a separate change (see "Follow-ups").
- [x] AI-7: Verified. Boot RSS ~517 MiB (was ~861). Mission catalog generates
  cross-sector correctly at boot. SectorServerHandoff integration test cold-
  starts sector 10151 on demand and passes. Test fixture readiness gate updated
  (ServerFixture.cs: wait for "Registering sector server: port=" boot-complete
  marker instead of a specific sector's BeginSectorThread, which no longer fires
  at boot).
- [x] AI-8 (regression caught + fixed 2026-06-06): the FIRST lazy-path run
  segfaulted the server on undock-into-space. gdb backtrace:
  `Player::ChangeSectorID(1015)` -> `g_ServerMgr->GetSectorManager(1015)` returns
  NULL (sector not started) -> `sm->AddPlayerToSectorList(this)` deref of `this=0x0`
  in Mutex::Lock. Root cause: `SendServerHandoff` PRE-registers the arriving player
  into the destination sector's player list the instant it tells the client to
  reconnect -- and under the old all-sectors-resident boot that manager was always
  non-NULL. Lazy start made it NULL. FIX: `ChangeSectorID` now calls
  `EnsureSectorStarted(SectorID)` (idempotent) instead of bare GetSectorManager,
  and null-guards. This is the single deref point for ALL handoff kinds, so it
  covers undock + gate + warp in one place. PlayerConnection.cpp:9336.
  Re-verified: full lazy-path slice (SectorMining, SectorUndockHandoffFollow,
  SectorWarp, SectorGateHandoffFollow, SectorStartAck, BootLogHealth) = 7/7 pass,
  NO server restart, cold-starts fired for stations (10151,10301) AND space
  sectors (1015,1060,1030) with distinct deterministic ports 3501-3505.
  Mission-data "NPC doesn't exist" / "Mutually exclusive types" boot lines are the
  pre-existing documented content gap (BootLogHealthTests excludes them by design;
  mission NPC resolution reads the global MOB table, not deferred sector objects).

- [x] AI-9 (log-noise fix 2026-06-06): a targeted deferred load of a
  station-instance id (10151 = docked instance of Luna/1015) has no row in
  `sectors` and returns 0 rows -> the old code printed "Error loading
  rows/fields" on every station cold-start. Station furniture comes from
  StationLogin, not `sector_objects`, so 0 rows is EXPECTED there. Suppressed
  the print for parse_id != -1 (kept loud for the boot-time full load).
  SectorContentSQL.cpp:133. Verified absent in the re-run.

**Stage 1 COMMITTED: cb7eecae** (2026-06-06). Verified twice post-fix:
6/6 isolated (fresh stack) + 4/4 against a live stack driving real login ->
station cold-start (10151) -> undock into Luna space (1015). Server log clean,
no spurious "Error loading rows/fields", distinct ports 3501/3502. The
full-suite OperationCanceledException cluster is the pre-existing
session-accumulation wedge (#35-#39), load-dependent and absent in isolation.
NOT pushed (awaiting explicit authorization).

## Follow-ups (Stage 1 left these dead -- clean up next)

- [x] Deleted the dead distributed-cluster auto-assignment path made unreachable
  by AI-6: `SectorServerManager::CheckConnections` / `AssignSectorToAvailableServer`
  / `UDP_Connection::ValidateSectorServer` / the `ServerCheck` auto-assign branch +
  its BeginSectorThread loop. Standalone (the only functional mode -- docker runs
  it) sets `m_SectorAssignmentsComplete = true` at boot, so the branch's guard was
  always false there; the distributed master+sector-server multi-process mode it
  served was already non-functional upstream (remote `ValidateSectorServer` ping
  and `RunSectorServer` registration both commented out). KEPT
  `m_SectorAssignmentsComplete` + `IsSectorAssignmentsComplete()`: still
  load-bearing -- the MVAS login handler (UDP_MVAS.cpp:184) returns the pre-start
  0x100B response until it flips true at boot. (Near-miss: the grep
  binary-detection trap on the CRLF .cpp first hid that caller; the build error
  surfaced it before push.) Verified: server rebuilds clean, 7/7 lazy-path slice
  passes, cold-starts fire (10151->3501, 1015->3502, 1060->3503), no new errors.

## Stage 2 -- idle teardown  [x]

### Ownership + memory model established (2026-06-06 investigation -- read before coding)

The naive AI-9 "free/reset obj_manager" is UNSAFE as originally written. Facts:

1. **obj_manager is cache-owned (process lifetime), NOT sector-owned.**
   `SectorManager::m_ObjectMgr` is a BORROWED pointer into
   `m_SectorData->obj_manager`, owned by the `SectorDataMap` content cache.
   Station sectors (id>9999) SHARE the parent space sector's obj_manager
   (`GetSectorData(id/10)`). So you cannot `delete` it on teardown, and you
   cannot `DeleteAllObjects()` it -- that wipes the galaxy-resident skeleton
   (navs/gates/planets/stations/deco/gwell/radiation) other sectors' mission
   gen + gate-seal dereference cross-sector. Teardown must therefore do a
   **selective purge of deferred-only objects**, leaving the skeleton resident.

2. **Deferred set (what to purge), from SectorContentSQL.cpp:317** -- source
   `type in (0,1,42,38)` => `OT_MOBSPAWN, OT_MOB, OT_FIELD, OT_RESOURCE`, plus
   anything spawned at runtime from those (runtime MOBs from spawners, husks,
   floating ore, mined resources). Keep everything else. Cleanest predicate:
   purge `ObjectType()` NOT in the skeleton whitelist {OT_STARGATE, OT_STATION,
   OT_PLANET, OT_DECO, OT_NAV, OT_GWELL, OT_RADIATION}.

3. **Two allocation classes.** MOBs/MOBSpawns/loaded fields+resources are
   `new`'d (ObjectManager.cpp:94/128 -- the temp-MOB pool path is commented
   out) => must `delete` + remove from m_SectorIndexList / m_MOBSectorList /
   g_SectorObjects. Runtime husks/resources come from fixed circular
   `MemorySlot` pools (m_TempHusks/m_TempResources), IsTemp()==true => must NOT
   `delete` (DeleteAllObjects already skips IsTemp); mark slot reusable. The
   pool ARENA is fixed-size and freed only at obj_manager dtor, so its RAM is a
   bounded per-sector cost that teardown does NOT reclaim -- acceptable.

4. **Cross-sector pointer safety: PROVEN OK.** Player->object refs are GameID
   (`long`), resolved per-op, never a retained raw `Object*` (HandleFaceRequest
   etc. take `long Target`). The 5 cross-sector `g_SectorObjects[id]` reads
   (SectorManager.cpp:674, PlayerMissions.cpp:149/500/702, TalkTreeParser:272)
   are transient locals, safe once the map entry is erased (lookup returns
   NULL, all sites null-check -- AI-4 risk note). Retained `Object*` members
   point either at skeleton (Player::m_NearestNav/m_CurrentDecoObj -- never
   purged) or at same-sector MOBs (MOB::m_Destination/m_SpawnGroup/m_MenaceObject
   -- torn down together). No cross-sector raw pointer into the deferred set.

5. **THE residual hazard: the global broadcast/timed-call queue.** `TimeNode`
   holds a raw `Object *obj` (TimeNode.h:48) and lives in the process-wide
   GMemoryHandler pool; player actions (prospect/mine/tractor,
   PlayerSkills.cpp:702/803) queue a node pointing at a sector resource/MOB. A
   node is live only while `player_id != 0`. If a player could gate out leaving
   a live node, purging its `obj` => use-after-free when the timer fires.
   **Safety-by-construction:** during purge, BEFORE freeing, walk the broadcast
   queue and set `player_id = NODE_NO_LONGER_NEEDED` on any node whose `obj` is
   in the purge set. Do NOT rely on player-exit having cleared it. (Subagent
   audit AI-AUDIT below must also confirm player sector-exit cancels these.)

### Tasks

- [x] AI-8 (implemented 2026-06-06, build-clean; verified via AI-AUDIT + AI-11; pushed a009f1a8): Per-sector clean stop.
  `volatile bool m_SectorShutdownRequested`; event loop
  `while(!g_ServerShutdown && !m_SectorShutdownRequested)` (SectorManager.cpp);
  `StopSectorThread()` sets the flag + pthread_join + m_SectorThreadRunning=false
  + resets the flag. `BeginSectorThread()` now clears the flag and sets
  m_SectorThreadRunning/m_SectorOnline on success (so restart re-arms cleanly).
  `DropListener()` = `delete m_SectorConnection; =NULL; m_Port=-1;` (relies on
  ~UDP_Connection closing the socket to unblock recvfrom + joining the recv
  thread). `ClearAllEventSlots()` NULLs every m_CoarseEventSlots/m_EventSlots
  pointer + zeroes m_EventSlotsIndex under m_Mutex (slot pointers, NOT the pooled
  node memory).
- [x] AI-9 (implemented 2026-06-06, build-clean; verified via AI-AUDIT + AI-11; pushed a009f1a8 -- PARK, don't unmap):
  `ServerManager::TeardownSector(sector_id)` under m_SectorStartMutex. Re-checks
  online + space-sector (id<9999) + occupancy==0 UNDER THE LOCK (EnsureSectorStarted,
  the only path that adds players, takes the same mutex, so the reading is
  stable -- this replaces the originally-planned separate pending-arrivals
  counter). Sequence: SetSectorOnline(false) FIRST (closes the lock-free fast
  path) -> StopSectorThread -> ClearAllEventSlots -> DropListener ->
  `ObjectManager::PurgeDeferredObjects()` -> m_SectorCount--.
  PurgeDeferredObjects tail-truncates m_SectorIndexList past the highest-index
  skeleton object, erases each freed object's UID from g_SectorObjects,
  `delete`s !IsTemp() objects (skips pooled husks/resources, mirroring
  DeleteAllObjects), rebuilds m_MOBSectorList from survivors, and lowers
  m_NumberOfObjects so freed GameIDs fall out of GetObjectFromID range.
  DELTA FROM ORIGINAL PLAN: the manager is NOT unmapped and m_SectorID is NOT
  reset -- mutating m_SectorMap would race the lock-free EnsureSectorStarted fast
  path (operator[] find vs erase = UB). Instead the manager stays mapped, parked
  (m_SectorOnline=false), and EnsureSectorStarted's new parked-restart branch
  reloads DEFERRED_ONLY + BeginSectorThread + StampActivity + m_SectorCount++
  in place under the start mutex (does NOT re-run SetupSectorServer).
- [x] AI-10 (implemented 2026-06-06, build-clean): `IdleSectorPoll()` called
  from ServerCheck, throttled by m_SectorIdlePollMs via m_LastTeardownPoll
  (mirrors the 5s SendPlayerCount throttle). Iterates m_SectorMgrList; online
  space sectors only; occupied -> StampActivity, else empty past
  m_SectorIdleTeardownMs -> TeardownSector. Per-sector m_LastActivityTick is
  stamped on every EnsureSectorStarted hit + every poll that sees occupancy>0
  (no separate pending counter needed).
- [x] AI-10b (env-config, 2026-06-06): both thresholds are runtime-configurable
  via NET7_SECTOR_IDLE_POLL_MS / NET7_SECTOR_IDLE_TEARDOWN_MS (read in the
  ServerManager ctor, ReadIdleMsEnv, default 300000 ms = 5 min, rejects <=0).
  The two `#define` macros were deleted (no-dead-code rule). docker-compose.yml
  wires both env vars through with host-overridable defaults
  (`${NET7_..:-300000}`). This is how the teardown is verified WITHOUT a
  test-only source hack (set the env low in a dev run, no code edit).
- [x] AI-AUDIT (user-requested subagent code audit -- SAFETY GATE):
  adversarial dangling-`Object*` hunt across the teardown path. Round 1
  (2026-06-06) found a CRITICAL: the central "teardown only runs on space
  sectors so purging the shared manager is safe" claim was WRONG -- a docked
  player counts toward the STATION manager, so the shared obj_manager stays
  live; GetObjectFromID is lockless -> UAF. Fixes A-D landed (family teardown,
  real recv-thread join via StopReceiver, contiguity guard, temp-pool index
  reset, lockless null guards). Round 2 re-audit of the FIXED code found one
  remaining HIGH: PurgeDeferredObjects' contiguity-abort path returned after
  Phase 1 had already parked the family -> restart double-loads. FIXED:
  PurgeDeferredObjects now returns bool, TeardownSector probes
  CanPurgeDeferredObjects() BEFORE parking and bails (stays online) if the purge
  would abort; the post-park call logs loudly if it ever races. Also fixed
  Finding 5 (shutdown delete loop iterated m_SectorCount, now m_MaxSectors with
  NULL guard) + made m_ThreadRunning std::atomic<bool>. Gate CLEARED.
- [x] AI-11 (VERIFIED live 2026-06-06): drove the full cycle on a fresh stack
  with NET7_SECTOR_IDLE_POLL_MS=5000 / NET7_SECTOR_IDLE_TEARDOWN_MS=10000 and
  the SectorUndockHandoffFollowTests (undock into space 1015) in
  CLI_INTEGRATION_SKIP_COMPOSE=1 externally-managed mode. Observed:
  `cold-starting 10151` (station) + `cold-starting 1015` (space) -> on
  disconnect `TeardownSector: parking idle sector_id=1015 (2 shared managers in
  family)` (correctly collected the station+space family) +
  `PurgeDeferredObjects sector_id=1015: freed 111 deferred objects, 188 skeleton
  retained` -> re-entry `restarting parked 10151` + `restarting parked 1015`,
  and the re-entry test PASSED (asserts asteroids present -> deferred content
  reloaded correctly). No crash, no WARNING, no double-load; the post-boot
  (20:36+) log window is error-free. Boot-time TalkTree/mission errors are
  pre-existing and unrelated. RSS reclaim = the 111 freed objects per parked
  space sector (the honest spin-DOWN number).
- [x] AI-12 (DTLS park/restart session-preservation fix, 2026-07-05):
  **PROD BUG.** On the online (DTLS-required) server two grouped multibox
  players wedged gating Earth -> Asteroid Belt Alpha (sector 1076, port 3504).
  Prod logs proved three cases: cold-start of a sector works (fresh handshake),
  online re-visit of a live sector works (both sides keep the session), but a
  gate into a PARKED-then-RESTARTED sector produced NO "Handle Sector Login"
  line at all -- the client's sector-login datagram never decrypted.
  ROOT CAUSE: the client-side proxy keys its DTLS association by
  (server_ip, port) and, once Established, `UDPClient::DtlsKickHandshake` is a
  no-op -- it never re-handshakes, it just resends app records under the
  existing session. AI-9's park does `delete m_SectorConnection` ->
  `~UDP_Connection` -> `delete m_Dtls`, destroying the server's per-peer SSL
  associations; the restart binds a FRESH transport on the same deterministic
  port. The proxy keeps sending under the stale session -> the fresh server
  cannot decrypt -> every C->S datagram (incl. the sector login) is silently
  dropped -> client wedges on the loading screen.
  FIX (server-side, no proxy redeploy -- fixes all deployed clients and does
  NOT risk the working online-re-visit path a proxy-side always-rehandshake
  would): preserve `m_Dtls` across the park/restart cycle instead of destroying
  it. `UDP_Connection::DetachDtls()` surrenders ownership (nulls m_Dtls so
  ~UDP_Connection tears down nothing); `AdoptDtls()` re-installs it BEFORE
  StartReceiver so the recv path reuses it instead of minting a fresh one.
  `SectorManager::DropListener()` stashes the transport in a new `m_ParkedDtls`
  member between StopReceiver() and delete; `StartListener()` re-adopts it after
  the bind, before StartReceiver. Mutually exclusive with m_SectorConnection so
  ~SectorManager frees whichever is live (no double-free). Plaintext (local
  docker default) is unaffected: m_Dtls is null there, so DetachDtls returns
  null and the AdoptDtls path is guarded off. NOT a wire-format change -- the
  same bytes keep flowing; the fix is DTLS session lifetime only.
  VERIFIED locally: `docker compose build server` compiles clean; 2 new gtests
  in `dtls_transport_test.cpp` reproduce the exact mechanism --
  `ParkedRestartFreshServerTransportDropsReusedProxySession` proves a fresh
  server transport drops the reused-session login (empty app_data = the wedge),
  `ParkedRestartPreservedServerTransportKeepsReusedProxySession` proves the
  preserved transport delivers the login byte-for-byte. Full DTLS suite (6
  tests) green. Real-client end-to-end confirmation: plans/29 CV-AI-DTLS-PARK
  (local default is plaintext, where the bug can't occur, so only a real client
  on a DTLS deploy exercises the full gate/park/gate).
  SECOND ROOT CAUSE, found by actually running the WINE-client gate/park/gate
  repro (2026-07-06): the DTLS session fix above was NECESSARY but not
  SUFFICIENT. With an aggressive idle timer the repro still wedged mid-login,
  and it was NOT a DTLS-association problem -- it was an idle-park RACE. Two
  code paths never stamped `m_LastActivityTick`: (1) `EnsureSectorStarted`'s
  cold-start branch bound the port and started the thread but left the ctor
  default `m_LastActivityTick == 0`, so the very next `IdleSectorPoll` saw
  `now > (0 + m_SectorIdleTeardownMs)` (true for any uptime past the teardown
  window) and PARKED the sector the caller had just cold-started for an inbound
  gate; (2) a player mid-handshake is not yet on `m_PlayerList`, so
  `GetOccupancy()` returns 0 for them -- a long login (slow grouped/DTLS gate is
  the worst case) could be parked out from under the player by `IdleSectorPoll`
  while the stage acks were still in flight, timing them out at login stage 12
  and silently logging the player out (this is what made a wormhole/gate look
  like it "did nothing" -- the destination sector was correct, the player had
  just been reaped mid-login). FIX (server-side, not a wire change):
  `EnsureSectorStarted` stamps activity right after `BeginSectorThread` on the
  cold-start path (the fast @772 and parked-restart @810 paths already did), and
  `Player::HandleLogin` re-arms the destination sector's idle clock at the START
  of the sector-login handshake (mirrors the existing PB-17 login-stage clock
  reset), so the fresh sector gets the full idle grace window to complete the
  login. Genuinely-empty sectors are still parked correctly (occupancy 0 AND no
  recent login activity). VALIDATED END-TO-END under the real WINE client on the
  DTLS-on stack (2026-07-06): drove gate 1015 -> wormhole 1076 -> park empty
  1015 -> gate back into parked 1015 -> park 1076 -> gate back into parked 1076.
  Every leg reached `player 'devuserte' fully logged in` with NO stage-12
  timeout, NO auth-wrapper drop, NO wedge: cold-start 1015 login, cold-start
  1076 login, legitimate park of empty 1015 (~10s empty), PARKED-RESTART of 1015
  (`restarting parked sector_id=1015` -> `Handle Sector Login` -> `fully logged
  in`, ~6s), and PARKED-RESTART of 1076 (`restarting parked` 16:07:32 -> `fully
  logged in` 16:07:36). Both fixes are complementary: 8d2e938b preserves the
  DTLS association across a legit park; the idle-park stamps stop a sector being
  parked while a login is in flight. Prior session's "DTLS parked-restart still
  wedges with 8d2e938b" discriminator was real but MIS-ATTRIBUTED to a DTLS
  problem -- the confound was this idle-park-mid-login race.

## Client-verification entries (plans/29)

Behaviour changes a real client could observe (NOT wire-format changes, so the
CLI-parse precondition does not apply, but the integrity rules' behaviour-change
tracking does):
- CV: first visit to a cold sector -> mobs freshly spawned (start of respawn
  cycle) rather than mid-cycle.
- CV: idle teardown wipes transient state (dropped loot/husks, partially-mined
  asteroids, MOB aggro) -> reset on re-entry.
- CV: re-entry after teardown reloads cleanly; first-player handoff into a cold
  sector adds ~40-115 ms content-load latency under the per-sector mutex (from
  the 130-sectors-in-5-15s boot trace), hidden by the gate/loading animation.

## Risks / honest caveats

- The original "metadata resident, all objects deferred" risk MATERIALIZED:
  cross-sector code dereferences a remote sector's `obj_manager` CONTENTS (not
  just the pointer) -- mission gen + gate-seal. Resolved by the type-aware split
  (nav skeleton resident, only MOBs/fields deferred). See the proof section.
- Station sectors (id > 9999) reuse the PARENT space sector's obj_manager
  (SetupSectorServer: GetSectorData(id/10)). EnsureSectorStarted(station_id)
  loads no `sectors` row for the station itself (stations aren't in `sectors`),
  so a station's first handoff starts it against the parent's skeleton. The
  SectorServerHandoff test (station 10151) passes, but the broader station
  furniture / two-player room-fanout suites should be in the regression slice.
- Combat-mission MOBs pre-placed into a not-yet-started sector
  (PlayerMissions.cpp:1856) sit in an unthreaded obj_manager until the player
  arrives and EnsureSectorStarted threads the sector. Under the old all-loaded
  boot they got AI immediately; now AI starts on first entry. Functionally fine
  (player must travel there to do the mission) but is a behavioural nuance -- CV.
- Stage 2 is the dangerous half (concurrency + use-after-free). Land Stage 1
  fully verified FIRST; do not interleave.
