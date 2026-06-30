# Phase AZ -- env-driven in-client auto-login + multi-box launch

**Goal.** Make scripted multibox trivial: launch N clients on one box, each on
its own loopback IP with its own proxy, and have each client log itself all the
way into the game from environment variables -- no `xdotool`, no screen
coordinates. The driving feature is **in-client auto-login**: the injected
`enbmod` DLL reads credentials from the process environment and drives the
client's own EULA / login / character-select code paths directly (the same
functions the buttons call), so a wrapper that sets a few env vars per process
gets N different accounts into the game with zero pixel automation.

This phase has two halves:

1. **Multi-box launch isolation** (proxy bind / slot / mutex / posfeed) --
   already implemented and verified this session; recorded here as the source of
   truth.
2. **Env-driven auto-login** (EULA / account / character) via injection hooks --
   the new work.

Relationship to other phases:
- Phase AU (`51-phase-au-login-automation.md`) automates login with **screen
  coordinates** (the `login-to-client` skill: XTEST clicks on the EULA / login /
  charselect screens). This phase is the **in-client, no-screen-coords**
  replacement for that flow, driven by env vars instead of pixels. AU stays as
  the screen-driven fallback; AZ is the preferred path for scripted multibox.
- Phase B (MVAS position feed, PB-2) -- multibox needs each client's position
  feed pinned to its own proxy; covered by `FREYA_POS_FEED_ADDRESS` below.

## Env var contract

All read by the injected DLL inside `client.exe` (Win32 process environment),
so a launcher/wrapper sets them per-process before/at spawn.

| Var | Effect |
|---|---|
| `ENB_EULA=ACCEPT` | Auto-accept the EULA when its screen/dialog appears. |
| `ENB_ACC_NAME` | Account name. With `ENB_ACC_PASS` set, fill + submit login automatically (no screen coords). |
| `ENB_ACC_PASS` | Account password (paired with `ENB_ACC_NAME`). |
| `ENB_CHARACTER` | Character name; once the character list loads, select that character and enter the world. |
| `FREYA_MULTIBOX=1` | (slot >= 1) bypass client single-instance mutex. |
| `FREYA_INSTANCE_SLOT=N` | force this launcher's slot (loopback `127.0.0.(N+1)`). |
| `FREYA_PROXY_BIND_ADDRESS=IP` | proxy binds this specific loopback IP (3500/3801/3805/3807). |
| `FREYA_POS_FEED_ADDRESS=IP` | client position feed targets this proxy's loopback IP. |

Values are EXAMPLES only in docs (`devuser`/`devpass`/`devusertt`); never commit
a real account or character name (CLAUDE.md). The credentials live only in the
spawning process's environment, never in a committed file.

## Half 1 -- multi-box launch isolation (DONE + VERIFIED this session)

The EnB client dials FIXED compiled-in ports (3500 PROXY_LOCAL_TCP, 3801 MASTER,
3805 GLOBAL, 3807 pos intake). N proxies on one box therefore need **one
loopback IP per proxy** (127.0.0.1, 127.0.0.2, ...), never one port per proxy.
All of 127.0.0.0/8 is loopback on Linux/WINE (NOT native Windows -- documented
limitation, would need loopback aliases there).

- [x] **Proxy binds a configurable IP.** `proxy/Net7.{h,cpp}` add
  `g_proxy_bind_addr` (default `INADDR_ANY`), parsed from
  `FREYA_PROXY_BIND_ADDRESS` in `main()`. `proxy/TcpListener.cpp` (3500/3801/3805
  listeners) and `proxy/UDPClient_linux.cpp` (3807 pos intake) bind it instead of
  hardcoded `INADDR_ANY`.
- [x] **CRITICAL bind rule:** a proxy on `INADDR_ANY:3500` OVERLAPS every
  specific-IP bind on :3500 -> `EADDRINUSE`. So the WINE launcher proxy ALWAYS
  pins a specific IP (slot 0 = 127.0.0.1, not `INADDR_ANY`). Only the **docker**
  proxy keeps `INADDR_ANY` (it needs published-port reachability and is spawned by
  compose, never by the launcher).
- [x] **Mutex bypass.** `freya/client-injection/FreyaMultiboxHook.{cpp,h}` IAT-hooks
  `CreateMutexA`, clearing `ERROR_ALREADY_EXISTS` for the client's single-instance
  mutex, gated on `FREYA_MULTIBOX`, installed from `DllMain` before the client's
  guard fires.
- [x] **Position feed per-instance target.** `ClientPositionFeed.cpp` reads
  `FREYA_POS_FEED_ADDRESS` so each client's MVAS feed reaches only its paired
  proxy.
- [x] **Launcher slot resolution.** `tools/LaunchFreya/MultiboxSlot.cs`: explicit
  `FREYA_INSTANCE_SLOT` wins; else auto-detect the lowest free slot by probing
  `127.0.0.(i+1):3500`. "Just launch the launcher again" -> lands on a fresh
  instance. `Launcher.cs` wires slot -> gameHost/proxy IP / mutex / posfeed env.
- [x] **Shared auth relay.** `LocalAuthRelay` terminates client plaintext auth on
  `127.0.0.1:4180` and re-wraps as TLS to the one shared upstream login; it's
  already concurrent-safe, so a single relay (owned by whichever launcher starts
  first) serves all instances. On `AddressAlreadyInUse` it returns null ("reuse
  sibling's relay").
- [x] **`just play-online N`** spawns N launcher processes with
  `FREYA_INSTANCE_SLOT=0..N-1`, staggered, no autoplay (each window gets a
  different account via the auto-login env below).
- [x] **VERIFIED:** two native proxies on 127.0.0.1 and 127.0.0.2 both bound
  3500/3801/3805 with 0 bind failures (the always-pin-IP fix). Launcher rebuilt
  clean.

Still open in Half 1:
- [ ] Full end-to-end multibox run needs DIFFERENT accounts per instance (server
  force-kicks duplicate login of same account). Unblocked by Half 2's
  `ENB_ACC_NAME`/`ENB_CHARACTER` per-process credentials. (Task #6.)
- [ ] play-local MVAS: native-proxy play-local impossible on rootless docker;
  MVAS works with the docker proxy. (Record + verify.)

## Half 2 -- env-driven in-client auto-login (NEW)

In-client only -- the injected DLL drives the client's own EULA / login /
character-select functions, NOT screen coordinates. Precedent: AS-14b already
calls client functions directly from `enbmod` (CmdBuild/CmdSend verb dispatch),
so calling the login/charselect entry points the same way is the established
pattern.

### AZ-1 -- scope the client seams (decomp) -- DONE
- [x] All seams the driver needs are mapped and live-validated (addresses in
  `game.h` as `LoginRunLoop` / `EditSetText` / `CredentialAccept` / `CharEnter` and
  the `game::login` offset block): the front-end run loop (LoginTask `this` source),
  the edit-control text setter, the NON-blocking credential-accept, the
  character-enter entry point, and the LoginTask layout (state `+0x24`, credential
  view ptr/edit-widget children, inline char-name slots, selected index). The one
  seam still missing is the EULA-DIALOG accept (AZ-3) -- the registry value the
  client reads does not gate that dialog.

### AZ-2 -- login-stage observation hook -- DONE + LIVE-VERIFIED
- [x] `hooks::enable_login_hook()` installs a READ-ONLY capture (NAKED trampoline,
  same pattern as the in-space hooks) on the front-end run loop
  `FUN_00767f50` (`game::addr::LoginRunLoop`), capturing the LoginTask `this`
  (ECX). `hooks::login_task()` returns it. `autologin::init()` installs it only
  when an account/character env var is set; an ordinary launch never gains it.
- [x] `autologin::tick()` reads the captured LoginTask, logs state transitions,
  and dumps the candidate offsets (read-only, mem-guarded) on each state change.
- [x] **LIVE DUMP (single client, login screen, state=1) CONFIRMED the layout:**
  - `+0x24` = int state (1 at login screen).
  - `+0x1084` = `char*` -> username ASCII ("devuser"); `+0x1088` = `char*` ->
    password buffer (empty). **These are COPIES the task fills at submit time, NOT
    the live edit-control buffers** -- writing bytes into `*(+0x1088)` did NOT
    change the on-screen password field. So filling these alone does not feed auth.
  - `+0xf74` = char count (0 at login screen, populated at char-select); `+0xf70`
    = char-list head; `+0x1080` = selected index.
  - Buffers live in the low data segment (`0x0036xxxx`), 256+ writable bytes each.

### AZ-3 -- EULA auto-accept -- REGISTRY DONE; dialog dismiss still screen-driven
- [x] `ENB_EULA`/`FREYA_EULA == ACCEPT` -> `autologin::init()` writes
  `HKCU\Software\Westwood Studios\Earth & Beyond` `Trial` = DWORD 0 (the value the
  client's own EULA check reads). Registry write only; no game memory/call.
- [!] **The registry write does NOT skip the 506x362 EULA dialog.** Live-verified
  on the running prefix: with `Trial`=0 written before launch, the client STILL
  pops the small EULA dialog, and the autologin trace only starts (`state ... -> 1`)
  AFTER the dialog is dismissed. So the dialog is gated by something other than that
  registry value. Currently dismissed by a single screen click
  (`05-eula-accept.sh`); this is the LAST remaining screen-coord dependency in the
  otherwise-hands-free flow. Open: find the dialog's in-client accept seam (its
  window proc / button handler) and click it from the DLL, OR identify the real
  state byte the dialog reads, so the whole login is pixel-free.

### AZ-4 -- account/password auto-submit -- DONE + LIVE-VERIFIED
- [x] Final approach (the blocking-handler trap from the first attempt is avoided):
  - Find the credential VIEW under the LoginTask by a bounded BFS over its pointer
    graph (`find_credential_view`: 3-part signature -- back-ptr to the LoginTask at
    `+0x9c`, plus two edit-widget children at `+0xa4`/`+0xa8` whose vtable is
    `edit_widget_vtable`). Re-checked before any call (game-function calls are NOT
    VEH-guarded).
  - Set each edit control's text with its OWN setter
    `EditSetText` (`thiscall(widget, char* text, int append=0)`) -- the real edit
    control objects, NOT the `+0x1084/+0x1088` submit-time copies the first attempt
    chased.
  - Trigger auth with the NON-blocking `CredentialAccept`
    (`thiscall(view)`) -- it kicks off validation and RETURNS, so the client's own
    per-frame run loop pumps the result, not the enbmod tick. No wedge.
- [x] **LIVE trace (clean run, this session):**
  `autologin: state -2147483647 -> 1` ->
  `autologin: credentials submitted (view=06dd1640)` -> auth succeeds, server logs
  `player 0x40000013 'devusertt' fully logged in`. Zero screen coords for the
  login itself.

### AZ-5 -- character auto-enter -- DONE + LIVE-VERIFIED
- [x] When `ENB_CHARACTER` is set, scan the 8 inline char-name slots in the
  LoginTask (`char_name_base 0x424` + `char_name_stride 0x10c`, `_stricmp`) for the
  requested name; on match, write the selected index to `sel_index` (`+0x1080`) and
  call the NON-blocking `CharEnter` (`thiscall(LoginTask)`). Latches `g_entered` so
  the tick stops touching the (soon-freed) LoginTask after world entry.
- [x] **KEY FINDING -- the programmatic-submit path does NOT advance `+0x24` to a
  char-select value.** After auth, the front-end state stays at `1`; the char-select
  screen never renders, but the avatar list DOES arrive and POPULATES the inline
  name slots. So AZ-5 is gated on **"the requested name appears in a slot"**, NOT on
  any `+0x24` transition. (The original manual-test assumption that state goes 1->2
  was wrong for this path; proven by manually firing `CharEnter` from state 1, which
  entered the world.) `drive_enter` polls the slots and fires once the name shows up.
- [x] **LIVE trace:** `autologin: entering world as devusertt (idx=3)` ->
  in-game confirmed (`enb.self()=table`, `enb.state()=space`, server
  `MVAS synched and locked in for devusertt`).

### AZ-6 -- wire into DllMain / driver -- DONE
- [x] `autologin::tick()` runs first thing in the game-thread `on_tick`
  (`dllmain.cpp`), no-op once `g_entered` or when unconfigured. `autologin::init()`
  arms the LoginTask capture hook ONLY when an account/character env var is set.
  Idempotent: `g_submitted` latches the credential submit, `g_entered` latches world
  entry, so each step fires once. Reads env via `env_either("FREYA_*","ENB_*")`.
  Cleanup fix verified: the `if (!g_enabled || g_entered) return;` guard is the FIRST
  line of `tick()`, so no garbage state-transition logging from reading the freed
  LoginTask after entry (the clean trace above shows zero spam post-entry).

### AZ-7 -- verify
- [x] **Single-client: DONE + LIVE-VERIFIED (this session).** Env vars only
  (`ENB_ACC_NAME`/`ENB_ACC_PASS`/`ENB_CHARACTER`), launch, land in-game with NO
  screen automation except the one EULA-dialog click (AZ-3, still open). Confirmed
  repeatedly: credentials submitted -> entering world -> server "fully logged in" +
  MVAS synced. No new packet shape emitted (calls existing client code paths only),
  so no CV gate.
- [ ] Multibox: `just play-online 2` with two different accounts -> both clients
  self-login to their own character. (Task #6; needs a second seeded account.)

## Notes / decisions
- No screen coords by design (owner: "make not rely on screen coords"). The
  screen-coord login (Phase AU / `login-to-client` skill) remains as a fallback.
- Credentials come only from the per-process environment; never committed.
- This calls EXISTING client functions -- no server/proxy wire change expected,
  so no CV gate unless something unexpectedly emits a new packet shape.
