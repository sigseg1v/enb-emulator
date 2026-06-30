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
- [~] Full end-to-end multibox run needs DIFFERENT accounts per instance (server
  force-kicks duplicate login of same account). Unblocked by Half 2's
  `ENB_ACC_NAME`/`ENB_CHARACTER` per-process credentials, AND a second dev account
  is now seeded: **`devuser2`/`devpass2`** with 5 chars (`just seed-dev-account
  devuser2 devpass2`), so a 2-account local test has its credentials ready. (Task #6.)
- [ ] play-local MVAS: native-proxy play-local impossible on rootless docker;
  MVAS works with the docker proxy. (Record + verify.)
- [~] **Multibox now self-logs per window; the only open piece is which SERVER to
  run it against.** Earlier note here over-stated the blocker: the launcher DOES have
  a native per-slot-proxy mode -- it is `Net7MP` / `just play-online N`. Each slot
  spawns its OWN WINE `FreyaProxy.exe` bound to `127.0.0.<slot+1>` (specific IP, never
  `INADDR_ANY`), so the loopback-overlap problem is already solved for that path; the
  docker proxy's `INADDR_ANY:3500` only collides when the local docker stack is left
  up, which `play-online` tears down first. The slot mechanism is wired and
  bind-verified.
  - **NEW this session:** the `play-online N` multibox loop now wires per-slot
    auto-login from env -- window n (1-based) reads `ENB_ACC_<n>`/`ENB_PASS_<n>`/
    `ENB_CHAR_<n>` and, if set, launches that window with `ENB_EULA=ACCEPT` +
    `FREYA_AUTOPLAY=1` so the AZ in-client auto-login drives it hands-free (no
    xdotool, no per-window typing). Windows without per-slot creds fall back to manual
    entry. This is the "make scripting multibox easy" payoff: N windows, N accounts,
    zero manual input.
  - Owner picked **(b) local-proxy-only mode**: keep server+login+postgres up, run
    the clients against the LOCAL docker server. `devuser2`/`devpass2` (5 chars) is
    seeded LOCALLY as the prerequisite.

#### (b) FINDING (2026-06-30): native WINE proxies do NOT work against the local docker server -- pivot to in-docker per-unit proxies

The first cut of (b) spawned N **native** `FreyaProxy.exe` (the `Net7MP` per-slot
path) dialing the host-published docker server ports. **This is NAT-broken and
fails for even a SINGLE client**, not just multibox:

- The server logs the proxy's address as `IP:172.23.0.1` (the docker bridge
  gateway). A native proxy runs OUTSIDE docker, so its UDP reaches the server
  through docker's published-port DNAT and is MASQUERADE'd to the gateway. The
  server's **pushed** replies on its multi-port UDP scheme (sector `Ack request`
  on 3501, global ticket on 3810) do not reverse back to the proxy's flow.
- Observed: the proxy reaches `SectorServer LOGIN -- connection active`, then the
  server emits `Re-send Ack request 3 for <char>` x5 and `---!!> Player <char>
  timed out during login stage 3`. The proxy log shows
  `UDPClient(Linux,global): SendTicket timed out / errored`. Reproduced with
  COUNT=1 (devusertt, sector 4025) and COUNT=2.
- `play-local` / `play-cli` never hit this because their proxy runs IN docker (own
  container IP on the stack network) -- the proxy<->server leg never crosses NAT.

**Corrected design (in progress):** one **in-docker** proxy per client
(`docker-compose.mbox.yml`, mirrors the per-unit `docker-compose.cli.yml` proxy),
each publishing its client-facing TCP (3801/3805/3500) + posfeed UDP (3807) on a
distinct host loopback IP `127.0.0.N`. Each WINE client launches in the
`Net7Local` no-native-proxy path with a new `FREYA_GAME_HOST=127.0.0.N` override
(points the client dial + the position feed at its own docker proxy) and
`FREYA_MULTIBOX=1` for the mutex bypass. The default docker proxy is taken down
first (its `0.0.0.0:3500/3801/3805` bind would shadow the per-unit `127.0.0.N`
binds). Auth stays the single shared `LocalAuthRelay` on `127.0.0.1:4180`
(authlogin.dll always dials it; the relay TLS-fans to freya-online). This removes
the NAT variable entirely and reuses the proven play-local client<->docker-proxy
path, scaled to N loopback IPs.

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
  view ptr/edit-widget children, inline char-name slots, selected index). The
  EULA-DIALOG accept seam (AZ-3) is also mapped and dismissed in-client (a modal
  `DialogBoxParamA` identified by its license-text control, driven via
  `WM_COMMAND`/IDOK from a worker thread); all login seams are now covered.

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

### AZ-3 -- EULA auto-accept -- DONE + LIVE-VERIFIED (fully in-client, no screen click)
- [x] `ENB_EULA`/`FREYA_EULA == ACCEPT` -> `autologin::init()` writes
  `HKCU\Software\Westwood Studios\Earth & Beyond` `Trial` = DWORD 0 (the value the
  client's own trial-demo EULA check reads). Registry write only; no game memory/call.
- [x] **The registry write alone does NOT skip the dialog** -- the dialog the client
  actually pops is the Rules-of-Conduct modal (it has no persistent accepted-flag in
  the registry to pre-set), so `Trial`=0 only suppresses the separate trial-demo
  prompt. The RoC dialog must be dismissed live.
- [x] **In-client dialog dismiss (no screen coords).** The dialog is a real modal
  `DialogBoxParamA`; while it is up the main thread is parked in the dialog's own
  message loop, so the per-frame `tick()` is DORMANT and cannot dismiss it. So
  `init()` spawns a dedicated worker thread (`eula_dismiss_thread`) that polls our
  process's top-level windows, identifies the dialog by the presence of its
  license-text control (`GetDlgItem(hwnd, 0x40D) != NULL`), and `PostMessage`s
  `WM_COMMAND`/IDOK to drive its own Accept path (`EndDialog(hwnd, 1)`). Deterministic;
  no pixel, no button-coord, no `xdotool`. Stops itself after the dialog stays gone.
- [x] **LIVE trace (clean run, this session), with NO `05-eula-accept.sh`:**
  `autologin: license dialog accepted (hwnd=0002005c)` ->
  `autologin: state ... -> 1` -> `credentials submitted` ->
  `entering world as devusertt`; in-game `enb.self()==table`, `enb.state()=="space"`.
  The ENTIRE login is now pixel-free end to end.

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
- [x] **Single-client: DONE + LIVE-VERIFIED (this session), FULLY PIXEL-FREE.** Env
  vars only (`ENB_ACC_NAME`/`ENB_ACC_PASS`/`ENB_CHARACTER`/`ENB_EULA`), launch, land
  in-game with ZERO screen automation -- the last screen dependency (the EULA-dialog
  click) is gone, dismissed in-client by AZ-3's dismiss thread. Confirmed repeatedly:
  license dialog accepted -> credentials submitted -> entering world -> server "fully
  logged in" + MVAS synced. No new packet shape emitted (calls existing client code
  paths only), so no CV gate.
- [x] Multibox: owner chose **(b) local-proxy-only mode** -- keep the docker
  server/login/postgres up, take the shared docker proxy DOWN, give each WINE client
  its own in-docker proxy bridged to the local server, with per-window env auto-login.
  `just play-multibox-local COUNT` drives this. **DONE + LIVE-VERIFIED (2026-06-30):
  COUNT=2 fully works end to end** -- both clients auto-login with separate accounts
  (devuser/devusertt + devuser2/devuser2tt) and each reaches its OWN proxy on all four
  game planes, with the server reporting NO IP mismatch. (Task #6 / #13.)

  **Transport blocker -- SOLVED (socat facade).** The client dials FIXED ports baked
  into client.exe (3500 sector / 3801 master / 3805 global TCP, 3807 posfeed UDP), so
  N proxies need N distinct `<ip>:3500` endpoints. Distinct loopback IPs
  (127.0.0.1, 127.0.0.2, ...) is the obvious split, BUT rootless docker
  (rootlesskit, builtin port driver) collapses all 127.0.0.0/8 to 127.0.0.1 in its
  port-conflict allocator -- a 2nd unit publishing :3500 on 127.0.0.2 fails "Bind for
  127.0.0.1:3500 already allocated". Fix: each proxy publishes on a UNIQUE 127.0.0.1
  host port; the recipe runs a host-side `socat` per unit binding the FIXED
  127.0.0.N:{3500,3801,3805,3807} the client wants and forwarding to that unit's
  unique port. socat is a plain host process so the kernel lets it bind distinct
  loopback IPs rootless docker refuses. No sudo, no client patch. (compose +
  justfile, this session.) Verified: 127.0.0.1:3500 and 127.0.0.2:3500 both bound
  simultaneously; devusertt fully logs in via the facade.

  **COUNT>=2 master/sector blocker -- the client HARDCODES 127.0.0.1, fixed by a
  connect() hook (netredirect).** The first theory (per-instance `network.ini` would
  hold a per-instance master address) was WRONG. The client honours the master/sector
  host from NO config knob at all: `ss -tnp` showed both clients dial `127.0.0.1` for
  master (3801) AND sector (3500) regardless of (a) `Data/common/network.ini`
  `[MasterServer]`, (b) `Data/client/ini/network.ini` (which resolves to a public IP,
  not loopback -- also ignored), and (c) the per-process `-SERVER_ADDR` arg (honoured
  ONLY for the auth/global 3805 plane). The master+sector host is effectively baked to
  `127.0.0.1` in client.exe. So on one host every concurrent client funnels its
  master+sector to one `127.0.0.1:{3801,3500}`, the second client's handoff arrives
  from the WRONG proxy, and the server's anti-hijack IP-match check
  (`UDP_Connection::ProcessClientOpcode`, correct, MUST NOT be weakened) rejected it --
  observed `Player IP mismatch [devuser2tt]: 60017ac 70017ac`.
  - **Fix (mechanism-independent): a ws2_32 `connect()` hook in the injected DLL**
    (`freya/client-injection/enbmod/src/netredirect.{h,cpp}`, MIT). It reads
    `FREYA_GAME_HOST=127.0.0.N` and rewrites any dial to `127.0.0.1` on the four fixed
    game ports (3500/3801/3805/3807) to `127.0.0.N`, so EVERY plane of client-N reaches
    its OWN proxy. No-op for instance 1 (`127.0.0.1`) and for non-game ports (the shared
    `127.0.0.1:4180` auth relay is untouched). Since we already inject the DLL this is
    the lightest tool: no network namespaces, no root, no server/proxy change, and it
    works no matter where the client computes the address. The per-instance WINE prefix
    (`~/.wine-enb-mboxN`, via the launcher's `FREYA_CLIENT_PATH` override) is still
    used, but ONLY to give each client its own `enbmod.cmd`/`.log`/registry/DLL copy --
    NOT to fix master/sector (no config knob can).
  - **LIVE VERIFY (2026-06-30, COUNT=2):** `ss -tnp` shows client 1 master+sector ->
    `127.0.0.1`, client 2 master+sector -> `127.0.0.2`; client 2 logs
    `netredirect: loopback game ports (3500/3801/3805/3807) -> 127.0.0.2`; both proxies
    reach `SectorServer LOGIN -- connection active` with distinct avatars/sectors
    (0x40000013 sector 4025 via mbox1; 0x40000018 sector 10201 via mbox2); both clients
    `enb.self()==table` in-world; server log has ZERO `Player IP mismatch`. The
    anti-hijack check passes LEGITIMATELY (each client's planes all share one proxy
    container IP) -- it was never weakened.

## Notes / decisions
- No screen coords by design (owner: "make not rely on screen coords"). The
  screen-coord login (Phase AU / `login-to-client` skill) remains as a fallback.
- Credentials come only from the per-process environment; never committed.
- This calls EXISTING client functions -- no server/proxy wire change expected,
  so no CV gate unless something unexpectedly emits a new packet shape.
