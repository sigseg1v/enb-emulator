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
  feed pinned to its own proxy; covered by `FREYA_POS_FEED_PORT` below
  (= this instance's `base+3` on 127.0.0.1).

## Env var contract

All read by the injected DLL inside `client.exe` (Win32 process environment),
so a launcher/wrapper sets them per-process before/at spawn.

| Var | Effect |
|---|---|
| `ENB_EULA=ACCEPT` | Auto-accept the EULA when its screen/dialog appears. |
| `ENB_ACC_NAME` | Account name. With `ENB_ACC_PASS` set, fill + submit login automatically (no screen coords). |
| `ENB_ACC_PASS` | Account password (paired with `ENB_ACC_NAME`). |
| `ENB_CHARACTER` | Character name; once the character list loads, select that character and enter the world. |
| `FREYA_MULTIBOX=1` | (base != 3500) bypass client single-instance mutex. |
| `FREYA_GAME_PORT_BASE=N` | client connect() hook remaps the fixed dials 3500/3801/3805 -> N+0/N+1/N+2 (this instance's contiguous block on 127.0.0.1). Launcher autodetects a free block when unset (native path). |
| `FREYA_POS_FEED_PORT=N` | client position feed targets `127.0.0.1:N` (= base+3). |
| `FREYA_PROXY_PORT_BASE=N` | NATIVE proxy offsets its 4 client-facing listeners to N+0..N+3 (docker proxy keeps stock ports + publishes the block instead). |

**Port-block redesign (2026-06-30, owner: "same IP diff PORT").** Multibox is now
"same 127.0.0.1, a different contiguous 4-port block per instance" -- NOT a
distinct loopback IP per instance. The earlier `FREYA_INSTANCE_SLOT` /
`FREYA_PROXY_BIND_ADDRESS=127.0.0.N` / `FREYA_POS_FEED_ADDRESS` / `FREYA_GAME_HOST`
knobs (distinct-IP + host-side socat facade) are REMOVED -- see the redesign note
under AZ-7. Same IP, different ports is portable to native Windows and sidesteps
rootless docker's 127.0.0.0/8 collapse. `FREYA_PROXY_BIND_ADDRESS` survives only
as the always-`127.0.0.1` client-facing bind for the native proxy.

Values are EXAMPLES only in docs (`devuser`/`devpass`/`devusertt`); never commit
a real account or character name (CLAUDE.md). The credentials live only in the
spawning process's environment, never in a committed file.

## Half 1 -- multi-box launch isolation

> **SUPERSEDED (2026-06-30) by the port-block redesign** -- see the REDESIGN note
> under AZ-7 and the env-contract table above. The "one loopback IP per proxy
> (127.0.0.N)" premise below was replaced by "same 127.0.0.1, one contiguous
> 4-port block per instance" at the owner's direction. The mutex-bypass, shared
> auth relay, and per-prefix isolation items below still hold; the `FREYA_INSTANCE_SLOT`
> / `FREYA_PROXY_BIND_ADDRESS=127.0.0.N` / `FREYA_GAME_HOST` / `FREYA_POS_FEED_ADDRESS`
> IP knobs do NOT -- they are removed in favour of `FREYA_GAME_PORT_BASE` /
> `FREYA_POS_FEED_PORT` / `FREYA_PROXY_PORT_BASE`. The text below is kept as the
> historical record of why distinct-IP was tried first.

The EnB client dials FIXED compiled-in ports (3500 PROXY_LOCAL_TCP, 3801 MASTER,
3805 GLOBAL, 3807 pos intake). The first design gave N proxies on one box **one
loopback IP per proxy** (127.0.0.1, 127.0.0.2, ...). All of 127.0.0.0/8 is
loopback on Linux/WINE but NOT native Windows -- one of the reasons the redesign
moved to a per-instance PORT block instead.

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

**Corrected design:** one **in-docker** proxy per client
(`docker-compose.mbox.yml`, mirrors the per-unit `docker-compose.cli.yml` proxy).
The in-docker-proxy conclusion (kill the NAT variable, reuse the proven play-local
client<->docker-proxy leg) is what stuck. The client-facing exposure was first
distinct host loopback IPs `127.0.0.N` + a socat facade, then -- at the owner's
direction -- the **port-block** scheme: each unit publishes a contiguous
`127.0.0.1:base..base+3` block, the in-client connect() hook remaps the client's
fixed dials onto it (`FREYA_GAME_PORT_BASE`), the position feed targets `base+3`
(`FREYA_POS_FEED_PORT`), and `FREYA_MULTIBOX=1` bypasses the mutex. The default
docker proxy is taken down first (its `0.0.0.0:3500/3801/3805` bind would shadow
unit 1's stock-numbered host ports). Auth stays the single shared `LocalAuthRelay`
on `127.0.0.1:4180`. See the REDESIGN note under AZ-7 for the full mechanism.

## Half 2 -- env-driven in-client auto-login (NEW)

In-client only -- the injected DLL drives the client's own EULA / login /
character-select functions, NOT screen coordinates. Precedent: AS-14b already
calls client functions directly from `enbmod` (CmdBuild/CmdSend verb dispatch),
so calling the login/charselect entry points the same way is the established
pattern.

### AZ-1 -- scope the client seams -- DONE
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
  `0x00767f50` (`game::addr::LoginRunLoop`), capturing the LoginTask `this`
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
- [~] Multibox: owner chose **(b) local-proxy-only mode** -- keep the docker
  server/login/postgres up, take the shared docker proxy DOWN, give each WINE client
  its own in-docker proxy bridged to the local server, with per-window env auto-login.
  `just play-multibox-local COUNT` drives this. The **distinct-loopback-IP + socat**
  cut of this was live-verified COUNT=2 earlier this session, but the owner then
  directed the **port-block redesign** below; that redesign is **implemented + all
  four units compile clean, and is the source of truth, but the docker COUNT=2 path
  has NOT yet been re-verified end-to-end on the new code.** (Task #6 / #13.)

  **REDESIGN (2026-06-30, owner: "if they all go to the same machine why does it
  need a different ip. shouldnt it use same ip diff PORT?").** Multibox now uses
  SAME IP (127.0.0.1) + a DIFFERENT contiguous 4-port block per instance, replacing
  the distinct-loopback-IP + host-side socat facade. This is simpler (no socat, no
  PID reaping), portable to native Windows (no 127.0.0.N aliasing), and sidesteps
  rootless docker's 127.0.0.0/8 collapse outright.
  - **How the fixed client ports get spread.** The client dials FIXED ports baked
    into client.exe (3500 sector / 3801 master / 3805 global TCP, 3807 posfeed UDP).
    The injected ws2_32 `connect()` hook (`netredirect.{h,cpp}`, MIT) reads
    `FREYA_GAME_PORT_BASE` and remaps any 127.0.0.1 dial on 3500/3801/3805 ->
    base+0/+1/+2 (posfeed handled separately via `FREYA_POS_FEED_PORT` = base+3,
    since it is `sendto` not `connect`). DefaultBase 3500 == first instance ==
    multibox OFF == byte-identical no-remap. The auth relay (`127.0.0.1:4180`) is
    outside the block and untouched.
  - **Proxy side.** DOCKER path: the per-unit proxy container keeps its STOCK ports
    and `docker-compose.mbox.yml` PUBLISHES host `127.0.0.1:base..base+3` onto them
    (no `FREYA_PROXY_PORT_BASE` in the container). The redirect needs no change: the
    proxy stamps fixed 3500 + `/ADDRESS:127.0.0.1` into every sector ServerRedirect,
    and the client hook remaps 3500 -> base. NATIVE path (play-online / Windows): the
    proxy reads `FREYA_PROXY_PORT_BASE` and offsets its own 4 client-facing listeners
    (`proxy_client_port()` in `Net7.cpp` + `ServerManager.cpp` + the posfeed intake
    in `UDPClient_linux.cpp`), since there is no docker publish layer.
  - **Anti-hijack check still passes legitimately.** All of client-N's planes reach
    the one proxy (one container/process IP), so `UDP_Connection::ProcessClientOpcode`
    sees a consistent source -- the security check (correct, MUST NOT be weakened) is
    untouched.
  - **Why the redesign is correct re: the old master/sector finding.** The earlier
    finding still holds -- the client bakes master(3801)+sector(3500) to `127.0.0.1`
    and honours `-SERVER_ADDR` only for the auth/global(3805) plane. The connect hook
    is exactly what neutralizes that: it remaps the PORT instead of needing a distinct
    IP, so every plane of client-N lands on its own proxy via its own port block.
  - **Per-instance WINE prefix retained** (`~/.wine-enb-mboxN`) only to isolate each
    client's `enbmod.cmd`/`.log`/registry/DLL copy -- network.ini is now identical
    (127.0.0.1) across instances, so it is no longer the driver. This applies to
    BOTH multibox recipes: `play-multibox-local` (docker per-unit proxies) AND
    `play-online` (native per-instance WINE proxies). **Fix this session:** the
    `play-online N` multibox loop previously shared ONE `WINEPREFIX` across all
    instances (a latent bug -- it would have interleaved each client's cmd/log
    channel + clobbered per-instance registry/config), so it now clones
    `~/.wine-enb-mboxN` per instance (cached, `--reflink=auto`) exactly like
    `play-multibox-local`, and launches each with its own
    `WINEPREFIX`+`FREYA_CLIENT_PATH`+`FREYA_GAME_PORT_BASE`. Instance 0 keeps the
    base prefix + stock port block 3500 (multibox OFF).
  - **Self-cleaning teardown (2026-06-30).** `play-multibox-local` runs in the
    foreground for the whole session (waits on the launcher PIDs), so it now sets a
    `trap _mbox_cleanup EXIT` that, on any exit (normal end / Ctrl-C / `set -e`
    failure), tears down the `mbox1..N` docker proxies it started and reaps stray
    `socat TCP-LISTEN:{3500,3801,3805},bind=127.0.0.*` forwarders left by the OLD
    (pre-port-block) design. Without it a leftover mbox listener squats on the fixed
    client ports and the next `just play-local` fails its proxy bind
    (`0.0.0.0:3500 ... address already in use`) -- observed this session from a prior
    multibox run that stranded 6 socats + the mbox1/mbox2 containers. `stop-multibox-local`
    stays as the manual escape hatch for a SIGKILL'd run whose trap never fired.
  - **STATUS: implemented + compiles; docker COUNT=2 NOT yet re-verified on the new
    code; native/online + Windows are needs-owner-verification (see plans/29
    CV-AZ-1..3).** Re-run `just play-multibox-local 2` and confirm `ss -tnp` shows
    client 1 -> 127.0.0.1:{3500,3801,3805} and client 2 -> 127.0.0.1:{23510,23511,23512}
    (its block), both `enb.self()==table` in-world, server log ZERO `Player IP mismatch`.
  - **`just play-local` auto-promote (2026-06-30, owner: "do (a) and make it
    automatically work. Automatically pass FREYA_MULTIBOX. Assume I will pass
    ENB_NOREBUILD=1 myself on the 2nd one").** Running `play-local` a SECOND time
    while a client is up no longer collides on port 3500 -- it self-promotes to a
    multibox SLOT. A mkdir-lock allocator (`$XDG_RUNTIME_DIR/freya-play-local-slots/<k>`,
    atomic claim of the lowest free k in 0..7, PID-liveness stale reclaim) assigns
    a slot; the lock + any private proxy unit are released by an EXIT trap. Slot 0
    (first launch) is the ordinary shared-proxy path BYTE-IDENTICAL to before. Slot
    k>=1 maps onto play-multibox-local instance index k: brings up only its own
    docker proxy unit (`-p mbox$((k+1))`, port block 23500+k*10 on 127.0.0.1, via
    docker-compose.mbox.yml on the shared stack net), clones the WINE prefix to
    ~/.wine-enb-mbox$((k+1)) (cached), and exports FREYA_GAME_PORT_BASE -- the
    launcher then DERIVES FREYA_MULTIBOX itself from _slot.Multibox (so the env is
    armed automatically, as asked) plus the connect-hook remap + posfeed port. It
    does NOT rebuild/recreate the shared stack (that would bounce client 1); the
    user passes ENB_NOREBUILD=1 on the 2nd launch. Don't run play-local promotion
    and play-multibox-local at once -- they share the mboxN projects/prefixes.
  - **Windows double-launch fix (2026-06-30, owner: "launch the freya launcher
    twice and click play on both ... Will it? If not, fix it").** The mutex bypass
    (FreyaMultiboxHook, in FreyaPosFeed.dll) was only injected when the position-feed
    toggle was ON, so a 2nd Windows client with the feed off would stall on the
    "already running" mutex. Fixed in `ConfigureInjection`: a `_slot.Multibox` slot
    now forces the posfeed DLL injection regardless of the feed toggle. The slot
    machinery (autodetect a free 127.0.0.1 port block when 3500 is busy) + native
    per-instance proxy offset + shared LocalAuthRelay reuse were already wired, so
    on Windows two launcher windows each clicking Play now auto-pick distinct port
    blocks. Caveat: needs a DIFFERENT account per client (server force-kicks a
    duplicate per-account login -- NOT bypassed). Real-Windows path is
    owner-verification (plans/29 CV-AZ-3).

## Notes / decisions
- No screen coords by design (owner: "make not rely on screen coords"). The
  screen-coord login (Phase AU / `login-to-client` skill) remains as a fallback.
- Credentials come only from the per-process environment; never committed.
- This calls EXISTING client functions -- no server/proxy wire change expected,
  so no CV gate unless something unexpectedly emits a new packet shape.
