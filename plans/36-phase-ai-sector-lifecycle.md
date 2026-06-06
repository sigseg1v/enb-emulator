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

- Delete the dead distributed-cluster auto-assignment path made unreachable by
  AI-6: `SectorServerManager::CheckConnections` / `AssignSectorToAvailableServer`
  / `UDP_Connection::ValidateSectorServer` / the `ServerCheck` auto-assign branch
  (ServerManager.cpp:419-429) / the BeginSectorThread loop inside it. It was
  already only-ever the single-process no-op assignment loop; AI-6 makes it fully
  dead. (no-dead-code rule -- tracked, not yet done.)

## Stage 2 -- idle teardown  [ ]

- [ ] AI-8: Per-sector clean stop. `volatile bool m_SectorShutdownRequested`;
  event loop `while(!g_ServerShutdown && !m_SectorShutdownRequested)`
  (SectorManager.cpp:813); `StopSectorThread()` sets flag + pthread_join. UDP
  recv thread already stops cleanly in `~UDP_Connection` (UDPConnection.cpp:83).
- [ ] AI-9: `TeardownSector(sector_id)` (under the same per-sector mutex,
  re-check occupancy==0 && pending==0): StopSectorThread -> delete
  m_SectorConnection (unbind) -> PURGE g_SectorObjects of this sector's UIDs
  (new ObjectManager helper iterating m_SectorIndexList GetDatabaseUID) ->
  free/reset obj_manager -> clear m_SectorMap entry, m_Started=false,
  m_SectorID back to unassigned. Port stays the deterministic value (rebinds on
  re-entry).
- [ ] AI-10: Idle poll in ServerCheck (5-min throttle like the 5s
  SendPlayerCount throttle at :445). Guard the handoff-in-flight race with a
  per-sector last-activity timestamp + pending-arrivals counter (bumped in
  EnsureSectorStarted/ProcessHandoff, decremented in AddPlayerToSectorList);
  only tear down if occupancy==0 && pending==0 && idle>threshold.
- [ ] AI-11: Verify: empty a sector, force teardown, re-enter via gate -> fresh
  reload, no stale g_SectorObjects, no crash. Measure RSS reclaim.

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
