# Phase: Player-reported live bugs

Running log of bugs reported by the project owner from real in-game play
(real client.exe under WINE against the live server). Distinct from the
phase checklists: these are observed runtime defects, triaged here, fixed
under whichever phase/rule applies. Newest at top.

Status key: `[ ]` open `[~]` in progress `[x]` fixed `[!]` blocked `[?]` needs repro/capture

| # | Status | Bug | Root cause / notes |
|---|--------|-----|--------------------|
| PB-4 | `[ ]` | **Galaxy map doesn't work.** | Reported 2026-06-08. Not yet triaged. The galaxy-map cache is a proxy-consumed `0x20xx` control band (see CLAUDE.md "proxy is NOT a dumb relay") -- the proxy answers galaxy-map cache opcodes itself. First step: determine whether the map data feed (sector list / nav points) is being served at all, or the proxy-side cache handler is the gap. Needs a capture of the map-open sequence. |
| PB-3 | `[?]` | **Vendor "Buy" button does nothing; must drag item to inventory instead.** | Reported 2026-06-08. PROVEN: server implements vendor purchase ONLY in `Player::HandleInventoryMove` (opcode `0x0027`, `PlayerConnection.cpp:3076`, `case 4` vendor->cargo) -- that's the working drag path. There is NO `0x001F TRADE` handler; it falls through to the "Bad opcode" default. NOT PROVEN: that the client's Buy button actually emits `0x001F` -- that's an inference, no capture yet. **Next:** capture what the client sends on Buy-click (grep `archive/kyp-snapshot/capturedPackets/` or take a local cleartext proxy<->server capture) BEFORE writing any handler. Do not code a `0x001F` handler blind. |
| PB-2 | `[~]` | **Can't fly with normal thrusters -- client shows you moving but server position diverges; spacebar/autopilot/warp snap you back to the real (server) position; stuck on planets where you can't warp.** | Reported 2026-06-01. ROOT CAUSE (corrected): our proxy has NEVER fed client position to the server. `proxy/UDPProxyMVAS.cpp` (which streamed `0x1004` to MVAS port 3806) existed but was already inert in the proxy build -- its position source `engine_read_process` was a `return false` stub in `proxy/Net7.cpp:288`; the only real cross-process scrape lived in `launcher/SocketTest.cpp` (the old Win32 launcher, a separate binary, not how players run today). That dormant file was then deleted in `4dc71638` (Phase AA consolidation). So this is NOT a simple "port" -- there is no working feed to restore; a NEW position source is required. The server only gets thrust-impulse `0x0014 MOVE` (no coords) and dead-reckons via `CalcNewPosition`, so it diverges; autopilot/warp are server-driven (explicit `SetPosition` + `SendLocationAndSpeed(true)`) so they re-sync. PRIMARY SOURCE: live Net7Proxy capture `proxy/local-debug/Combat-...-20260604-072157.pcapng` -- `0x1004` to udp/3806 x196 during thrust (the real proxy streamed it densely). FIX (Option B, scaffolded): an in-client Detours hook (`client/detours/ClientPositionFeed.{h,cpp}`) reads the engine ship state in-process and publishes it into named shared memory (`proxy/ClientPositionShared.h`); the proxy consumer (`UDPClient::SendPositionIfChanged`, `proxy/UDPClient_linux.cpp`) reads it and streams `0x1004` to 3806, change-gated. This avoids the old hardcoded-offset cross-process scrape. `0x1004` wire shape pinned by `CaptureReplayTests.MvasPosition_1004_*` (fixture `live_mvas_position_1004.hex`). REMAINING BLOCKER: the engine read itself (`ReadEngineShipState`, owner seam, returns false until filled) + real-client confirmation -- tracked as CV-MVAS-POS in `plans/29`. Both build targets (Linux-native + Win32 mingw) compile green. Same subsystem noted in `plans/27 §"Why it broke"`. |
| PB-1 | `[x]` | **Credits underflowed to ~270 billion, change on every login.** | Fixed 2026-06-01, commit `421cd9bd` (local). `Player::SaveCreditLevel` wrote a u64 credits value through the 4-byte `AddData<unsigned long>` specialization (on Linux `u64 == unsigned long`), so the SaveManager 8-byte read pulled an uninitialized high dword. Fixed with explicit `AddData64` (8-byte LE emitter) in `PacketMethods.h`; regression test `tests/server/protocol/credit_save_width_test.cpp` (3/3 pass). Server-internal IPC, no CV/CLI gating needed. |

## Notes

- PB-1 fix is local-only (commit `421cd9bd`) -- not yet pushed/deployed per
  the standing "author, don't deploy" instruction.
- PB-2 is a proxy wire-behaviour change: primary-source citation is the live
  `0x1004` capture (`proxy/local-debug/Combat-...-20260604-072157.pcapng`),
  CLI parse of `0x1004` exists (`MvasClient` + `CaptureReplayTests`), and
  `plans/29` CV-MVAS-POS tracks real-client confirmation. The earlier "port the
  deleted `UDPProxyMVAS.cpp`" framing was wrong -- that file's position source
  was a `return false` stub in the proxy build, so nothing functional was lost
  to "port back". The feed is a NEW in-client-hook source, scaffolded inert.
