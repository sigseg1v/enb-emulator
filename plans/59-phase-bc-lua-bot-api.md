# Phase BC -- Lua bot API + skill rewrite (drive the client from Lua, not OCR)

## Goal

Expose EVERY action the client-playing skills need as a reliable `enb.*` Lua
function in enbmod, so bots/skills drive the game through in-process native calls
instead of screen OCR + absolute-coordinate mouse clicks. Then rewrite the skills
(`login-to-client`, `explore-sector`, ...) to call the Lua API and delete the
OCR/click machinery.

Owner directive (2026-07-05): "ideally all the functions the client playing skill
does we can write in lua ... add warp, selecting navs, gating, registration, dock,
[undock], calculating distance, current hull/shield etc into a nice lua api that we
can make bots from" + "update those skills to call lua funcs for reliability and
remove all the screen ocr and screen coordinate clicks".

## Why this is reliable where XTEST is not

enbmod runs INSIDE client.exe. `enb.*` calls hit the game's own command paths
(`CmdBuild`/`CmdSend` on `M+connection`, `RequestTarget`, the entity hash table)
and read live memory directly -- no keyboard focus, no pointer-grab contention, no
OCR misreads, no 1280x960 coordinate assumptions. The skills already proved the
cmd channel works (login, group invite, loot).

## API surface (status)

Legend: [x] done+built, [~] in progress, [ ] todo, [!] blocked.
"composable" = wraps already-validated primitives (no new native RE).
"native" = needs an address/opcode mined from client behaviour + live validation.

### Already existed before this phase (reuse)
- [x] `enb.self()` -- hull/shield/energy (+_max), x/y/z, name, levels. **Vitals + position DONE.**
- [x] `enb.vitals()` -- hull/shield/energy fill fractions.
- [x] `enb.target()` / `enb.target_obj()` -- current target info (name, class, hull/shield).
- [x] `enb.request_target(gid)` -- target an object by GameID (cdecl(M,obj), validated).
- [x] `enb.target_action(op, mode)` -- avatar-command dispatch on target/self/follow.
      Known ops: DOCK 0x1c, REGISTER 0x19, "land" = 0x1d then 0x08 (TerminateWarp+handoff).
- [x] `enb.group()` / `enb.group_action()` -- group state + CTA actions.
- [x] `enb.state()` / `enb.inspace()` -- game state (station detection blocked, see below).

### New -- composable (low risk, no new native)  -- ALL SHIPPED, build-clean
- [x] `enb.dock()`         -- send_target_cmd 0x1c on current target. (dock was already
      live-validated via target_action; this is the named wrapper.)
- [x] `enb.register()`     -- send_target_cmd 0x19 on current target.
- [x] `enb.dist(...)`      -- self->target / self->point / point->point via world_pos (the
      locatable getter, same source enb.target()/enb.self() distance uses).
- [x] `enb.objects(filter?)`-- walk M's entity hash (ent_buckets/0x101/ent_node_*, the same
      table entity_by_gid uses) and return every live object {gid, base, class, name,
      x,y,z, dist}. Position comes through world_pos (the vtable locatable getter) which
      works on ANY locatable object, so it is NOT the unvalidated flat player pos offset.
- [x] `enb.navs()`         -- enb.objects() filtered to nav-ish class tokens, dist-sorted.
      Nav SELECTION is enb.request_target(gid) (already shipped) -- no separate select_nav
      wrapper (would be dead-code duplication). See client-verification CV-BOT-NAVS below.

### New -- native (mined address/opcode; needs live validation)
- [x] `enb.gate()`     -- COMPOSABLE after all: the gate button is avatar-command 0x12
      (arm: TerminateWarp + GateActivate) then 0x13 (finish: sector handoff), same CmdBuild
      path as dock. Shipped as send_target_cmd(0x12) then send_target_cmd(0x13). CV-BOT-GATE.
- [x] `enb.undock()`   -- leave station -> space. NOT an avatar-command: it calls the
      client's own StationExit method (addr 0x0074ba30, thiscall(M, action=1, 0, 0)), which
      emits wire opcode 0x004E STARBASE_REQUEST Action=1; server LaunchIntoSpace. Guarded on
      docked (M+starbase_id != 0). game.h: addr::StationExit. CV-BOT-UNDOCK.
- [x] `enb.warp()` / `enb.warp_stop()` -- IMPLEMENTED (needs live validation, CV-BOT-WARP).
      Warp is NOT a CmdBuild verb: it is its own packet OBJECT the client serializes itself
      (wire opcode 0x009B), a server-side TOGGLE (re-send while warping -> TerminateWarp, so
      warp and warp_stop are one code path). The mod does NOT hand-encode bytes -- it drives
      the native warp-orb sequence on M: WarpPathBuild(radarCtrl) builds the nav path from the
      current target, then WarpPacketCtor wraps the persistent NavigationList (M + navlist =
      0x1138) + the built nav vector, and CmdSend (the SAME generic *(M+connection)->vt[2] send
      as everything else) transmits 0x009B; WarpPacketDtor frees it. game.h: addr::WarpPathBuild
      / WarpNavVec / WarpPacketCtor / WarpPacketDtor + world::navlist. The one piece with no
      fixed offset -- the radar/targeting controller `this` -- is captured read-only at
      WarpPathBuild's entry (hooks::warp_radar_ctrl), same pattern as the cockpit captures, so
      it is populated once a warp path has been built this session. enb.warp() operates on the
      CURRENT target (target a nav first with enb.request_target/enb.navs), mirroring dock/gate.
      Returns false + a reason string when M/radar not captured or the path is not ready this
      frame (retry next tick). NOT faked: every address is decomp-proven; only the end-to-end
      client effect is unverified (CV-BOT-WARP).

## Station detection -- DONE (M-field discriminator, not a global enum)
- [x] `enb.state()` now positively names "station" vs "space" from the captured world
      manager M: docked == (*(int*)(M + world::station_view) != 0) (station interior view
      ptr; StarbaseID mirror at M + world::starbase_id). The decomp mining confirmed the
      client has NO single global state enum (game_state_addr stays 0/uncalibrated as an
      override hook) -- it uses task polymorphism (LoginTask=front-end, M=in-world, then
      station_view splits station/space). game.h: world::station_view / world::starbase_id.
      CV-BOT-STATE. This unblocks the station-UI task below.

## Client-verification checklist (real client.exe -- owner confirms async)
These are enbmod (client-side) calls into existing native command paths, so they are NOT
server/proxy wire changes (the server already handles 0x004E/0x2C/0x009B) and do not fall
under the server-integrity plans/29 gate. But the CLI cannot prove the client-side effect,
so each native-touching call gets a real-client check here:
- [ ] CV-BOT-UNDOCK: from a station, `enb.undock()` returns true and the client actually
      transitions station -> space (0x004E Action=1 -> LaunchIntoSpace). In space it returns
      false and does nothing (starbase_id==0 guard).
- [ ] CV-BOT-GATE: with a stargate targeted, `enb.gate()` transits to the adjacent sector.
      Confirm the back-to-back 0x12->0x13 (skipping the native fly-in animation) is accepted
      by the real client the same as the native gate button.
- [ ] CV-BOT-STATE: `enb.state()` reads "station" while docked and "space" once undocked,
      flipping exactly on the dock/undock transition.
- [ ] CV-BOT-NAVS: `enb.objects()` in a live sector returns navs, and the class strings they
      carry are covered by kNavClassTokens {"nav","gate","wormhole"} so `enb.navs()` is
      non-empty. If navs are NOT in the entity hash (they may live in the NavListBuild
      0x007b7ef0 nav list instead), navs() needs a second source -- confirm which.
- [ ] CV-BOT-WARP: with a nav targeted (enb.request_target), `enb.warp()` engages warp to it
      the same as clicking the warp orb, and a second `enb.warp()` / `enb.warp_stop()` while
      warping terminates it. Confirm the radar controller is captured (hooks::warp_radar_ctrl
      non-zero) BEFORE the first bot warp -- determine whether it is seeded merely by targeting
      a nav (NavListRender path) or only after one manual orb warp; if the latter, document the
      one-manual-warp bootstrap. Also confirm M+0x1138 is the NavigationList on the SAME object
      CmdSend uses (M), not a distinct player object.

## Original station-UI task (folded in -- depends on station detection above)
- [ ] freya-hud in station: show ONLY chat + C/E/T discipline bars (xp_overlay) + top
      buttons (micromenu); hide target/party/action(hotbar)/loot/vitals-cards.
      Needs positive station detection + per-frame station gating. Verify via screenshots.

## Group-formation automation (owner asked 2026-07-05: "auto gate in group formation +
## auto redo formation after gate on leader + auto rejoin formation after gate on members")
BUILT (needs live validation) -- new mod `freya-groupbot` (scripts/mods/freya-groupbot/).
Auto-discovered by the launcher ModCatalog (no registry edit); default-DISABLED in Configure
Mods, and its follow-gate half is a further separate default-OFF switch. Design honesty: the
exposed API forced a split -- reform-after-gate is solid, "member auto-gates when the LEADER
gates" is NOT expressible as specified, so it ships as a heuristic user-designated follow.
- [x] Reform-after-gate (LEADER, part 2) + rejoin-after-gate (MEMBER, part 3). SOLID. Keys on
      the `enb.inspace()` false->true rising edge (a gate runs a loading screen, dropping the
      in-space heartbeat, then resumes). Leader re-issues formation CTA (4/5/6, remembered);
      member re-issues form-up CTA 7. Debounced to once per sector entry. LIGHT_DIV tick
      sampling, no per-frame work. party_frame notifies groupbot.formation() when the leader
      picks a type in the HUD (nil-guarded, no hard dep).
- [x] Follow-gate (MEMBER, part 1). HEURISTIC + default-OFF. HARD API LIMIT: `enb.group()`
      does NOT flag which roster member is the leader (only is_leader() = whether WE are), and
      there is no leader-sector/position signal -- so "detect the LEADER gated" is impossible
      directly. Implemented against a USER-DESIGNATED follow gid (groupbot.follow(gid)): watch
      that ship via enb.objects(); when it was hugging a stargate (<6k) and drops out of
      scanner range for >=2 heavy samples, target that gate + enb.gate(). Debounce + cooldown.
      HEAVY_DIV sampling only while follow_on. Genuinely flaky (scanner-range race, wrong-gate
      risk) -- that is why it is a separate opt-in, not on by default.
- [ ] Depends on `enb.gate()` (CV-BOT-GATE) and the reform trigger's group state surviving a
      gate -- both still UNVERIFIED. Building on them is provisional until those CV checks pass.
- [ ] Plan 48 (CLI loses group/formation state after warp/sector-change): the client-side
      enb.group()/is_leader() reader may hit the same. CV-BOT-FORMATION must confirm they read
      correctly immediately after a gate before the reform trigger is trusted.
- [ ] CANNOT be truthfully marked "tested" without a live MULTI-CLIENT run (leader + members
      grouped, formed, crossing a gate together) on LOCAL dev clients -- the proxy is
      single-client (one per client). Never run this against the owner's live game client.

Group-formation CV checks (real client.exe, owner confirms async):
- [ ] CV-BOT-FORMATION: in a live group, immediately AFTER a gate, enb.group() still returns
      the roster and enb.is_leader() still reads correctly (i.e. group state survived the gate,
      NOT lost like plan 48's CLI). If lost, the reform trigger fires against a stale/empty
      group and does nothing -- document the actual post-gate state.
- [ ] CV-BOT-REFORM: with freya-groupbot enabled, a LEADER gating re-forms the group into the
      remembered formation (visible formation offsets re-applied), and a MEMBER gating re-sends
      form-up and snaps back into the formation. Once per sector entry, no CTA spam.
- [ ] CV-BOT-FOLLOWGATE: with follow_on + a designated follow gid, when that ship gates the
      watching client jumps the SAME gate (not a wrong one), fires at most once, and does
      nothing when the designated ship merely warps off without gating.

## Skill rewrite (after the API lands + is live-validated)
- [ ] `explore-sector`: replace W/D/C key enum + warp-orb click + map OCR with
      enb.navs()/enb.select_nav()/enb.warp()/enb.dist(); keep the ledger/logging.
- [ ] `login-to-client`: already mostly Lua (in-game truth via enb channel); replace the
      remaining coordinate clicks where a native exists.
- [ ] Audit other skills (undock.sh, run-sector.sh, hangcheck.sh) for OCR/click paths that
      a Lua call now covers.

## Progress log
- 2026-07-05: Phase opened. Task A (remote-facing rotation replication) shipped separately
  (commit 8faf2fba) -- NOT part of this phase but was the entry point. Surveyed existing
  enb.* API; confirmed dock/register/nav-select/vitals/distance are composable from
  validated primitives; undock/warp/gate/game-state need decomp mining (two agents running).
- 2026-07-05: Bot API landed (build-clean, clang-format clean). Shipped enb.dock / register /
  gate / undock / dist / objects / navs and calibrated enb.state() station detection.
  Findings from the mining: gate turned out composable (0x12->0x13 avatar-cmd); undock is a
  distinct StationExit thiscall (opcode 0x004E), added as addr::StationExit; station detect is
  an M-field discriminator (station_view/starbase_id), not a global enum. WARP is the one
  deferred item -- opcode 0x009B WarpPacket, no one-arg builder, needs its own layout + live
  validation. All native-touching calls have CV-BOT-* real-client checks above. hull/shield/
  energy were already covered by enb.self()/enb.vitals(). NEXT: warp, then the station-UI
  gating task (now unblocked by state detection), then the skill rewrite.
- 2026-07-05: WARP implemented (no longer deferred). Decomp mining proved the full native
  warp-orb sequence: generic packet-object send (CmdSend, *(M+connection)->vt[2]), WarpPacket
  ctor/dtor (0x008b1aa0 / 0x008b1c70), path builder (0x007bd980) + nav-vector getter
  (0x007bd960), NavigationList at M+0x1138. Added addr::WarpPathBuild/WarpNavVec/WarpPacketCtor
  /WarpPacketDtor + world::navlist to game.h; a read-only capture hook on WarpPathBuild grabs
  the radar-controller ECX (hooks::warp_radar_ctrl) since it has no fixed offset. enb.warp /
  enb.warp_stop are one toggle path. Build-clean + clang-clean. NOT faked -- addresses are
  decomp-proven; CV-BOT-WARP tracks the live client effect. Owner also requested group-
  formation automation (auto-gate/reform/rejoin) -- recorded above as NOT-built, with its
  gate/warp-validation + multi-client-test dependencies spelled out. That feature never
  existed; only the manual formation buttons do.
- 2026-07-05: Group-formation automation BUILT as a new mod `freya-groupbot`
  (scripts/mods/freya-groupbot/ init.lua + mod.json). reform-after-gate (leader re-issues
  formation type, member re-issues form-up) keys on the enb.inspace() false->true edge --
  solid, debounced, LIGHT_DIV-sampled, default-ON but the mod is default-DISABLED in Configure
  Mods. Follow-gate is a heuristic against a USER-DESIGNATED follow gid (the API can't identify
  the leader in the roster, so "member auto-gates when the LEADER gates" is not directly
  expressible) -- separate default-OFF switch, HEAVY_DIV-sampled, debounce+cooldown, honestly
  flagged flaky. party_frame.lua now nil-guard-notifies groupbot.formation() when the leader
  picks a type in the HUD. Both Lua files parse-checked (lupa load()). Console control via the
  global `groupbot` table. CANNOT be marked tested: needs a live multi-client run on LOCAL dev
  clients + CV-BOT-GATE/FORMATION/REFORM/FOLLOWGATE. Told the owner plainly the feature was
  never built and what the API can and cannot honestly do here.
