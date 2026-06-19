# Phase AU -- client login automation skill

**Goal:** an agent-runnable, reliable, repeatable skill that takes the local
stack from "nothing running" to "in-game on a character", so the Lua/HUD work
(Phase AS) can be resumed without a human clicking through EULA -> login ->
character-select -> load every time. Born out of Phase AS: a wedged/relaunched
client currently blocks all live HUD iteration until the owner re-logs in by
hand.

## Constraints / environment (verified 2026-06-18)

- The client (`client.exe`) is a Win32 binary under WINE on `DISPLAY=:0`. The
  launcher (`FreyaLauncher`, Avalonia/.NET) runs natively on Linux.
- Tooling present: `xdotool`, `wmctrl`, `xwininfo`, ImageMagick `import`,
  `python3` + `PIL` (10.2) + `numpy` (1.26). **Absent:** `tesseract` (no OCR),
  `cv2` (no OpenCV), no running `ydotoold`. => detection must be PIL+numpy
  region matching, NOT OCR/template-via-cv2.
- Input: XTEST via `xdotool` (move real pointer to absolute coords + click;
  `type` to the focused window). `--window` XSendEvent is unreliable for
  WINE/Avalonia, so we RAISE the target window then click absolute coords
  computed from `xwininfo` "Absolute upper-left".
- Screenshot: reuse `.claude/skills/take-client-screenshot/scripts/screenshot.sh`
  (`import -window <id>`); the client window is chosen by WM_CLASS `client.exe`,
  largest viewable.
- Dev account: `devuser` / `devpass` (the `just seed-dev-account` default).
  Seeded with a 5-character roster (TE/JE/JD/TT/TS), each flyable. The login
  screen username/password are typed every launch; the client shows a EULA
  every launch; first character nameplate is what we enter game with.
- Single-client proxy (see memory): only ONE client/proxy at a time; the skill
  always kills any prior client + launcher first.

## Skill layout (AS BUILT)

The Play click became an in-app autoplay hook (see AU-5), so there is no
`02-click-play` step; numbering follows the as-built files.

`.claude/skills/login-to-client/`
- `SKILL.md` -- usage, the per-step table, modes, and env gotchas (VM pointer
  warp, screensaver input-steal, `import` server-grab hang, CRLF logs).
- `scripts/lib.sh` -- shared: DISPLAY/client-dir resolution, `client_win`/
  `launcher_win` (largest viewable by WM_CLASS), `win_abs`, `raise_win`,
  `click_win <id> <relx> <rely>`, `click_abs`, `type_text`/`send_key`,
  `shot`/`shot_win`, `wait_until`, `coord`/`click_coord` (read coords.json),
  `is_screen`/`on_login`/`on_charselect` (ref-crop match), COMPOSE_PROJECT_NAME
  branch derivation.
- `scripts/detect.py` -- PIL+numpy: `size`, `mean`, `black`/`bright` region
  stats, `match` (mean-abs-diff vs a stored ref crop), `savecrop`.
- `scripts/coords.json` -- per-screen window-relative click coords (eula.agree,
  intro.skip, login.user/pass/accept, charselect.first/enter).
- `scripts/refs/` -- `login_field.png`, `charselect_frame.png` (opaque-UI crops;
  validated to match their screen at <1 MAD and mismatch others at >30).
- Building blocks (each: clear exit code + one-line `STATE=...`/`STUCK=...`):
  - `00-kill.sh` -- kill client.exe + wineserver, FreyaLauncher, leftover
    FreyaProxy; `just down`; clear enbmod.cmd; verify client window gone.
  - `01-start-stack.sh` -- `just run-stack-bg` (returns), wait server/net7go/
    proxy Up.
  - `02-seed.sh` -- ensure `$ENB_LOGIN_USER` has a roster (idempotent: skips if
    `avatar_info` count > 0; `ENB_FORCE_SEED=1` to re-seed).
  - `03-launch-launcher.sh` -- `FREYA_AUTOPLAY=1 ENB_NOREBUILD=1 just play-local`
    fully detached via setsid; wait for the `client.exe` window.
  - `05-eula-accept.sh` -- confirm EULA by 506x362 size, click I Agree, wait for
    the 1280x960 game window.
  - `06-login.sh` -- skip intro until login_field ref matches; clear+type user &
    pass; click Accept; wait for charselect ref.
  - `07-charselect-enter.sh` -- click first nameplate + Enter; confirm we leave
    charselect.
  - `08-wait-ingame.sh` -- wait until Lua `type(enb.self())=="table"` via the
    enbmod cmd channel; report `inspace`/`state`.
- `login.sh` -- orchestrator: 00,01,02,03,05,06,07,08; `ENB_ATTACH=1` skips
  00-03 to drive an already-running client; `ENB_SKIP_SEED=1` skips 02.

## Reliability principles

- **Never blind-sleep through a stage.** Each stage polls a real signal
  (window mapped, region match, heartbeat) up to a timeout, then fails loud
  with a screenshot + "STUCK at <stage>".
- **Re-derive window geometry every stage** (WINE can move/resize the window;
  the launcher may be offscreen on the multi-monitor desktop).
- **Coordinates discovered empirically** by walking the live flow once and
  recording into `coords.json`; refs captured at the same time. The client is
  fixed-resolution + deterministic, so fixed coords are stable across runs.
- Detection is layered: window existence (cheap) -> region match (confirm the
  right screen) -> heartbeat (in-game truth).

## Checklist

- [x] AU-1 Write this plan; slot Phase AU into `plans/00-master.md`.
- [x] AU-2 Seed `devuser`/`devpass` with a flyable roster (idempotent; 02-seed).
- [x] AU-3 `lib.sh` + `detect.py` helpers; click + type land in the launcher
      and in a WINE window (XTEST raise+abs-click+type; PIL+numpy region match).
- [x] AU-4 `00-kill.sh`, `01-start-stack.sh` -- reach "stack up" reliably.
- [x] AU-5 Play click -- became the in-app `FREYA_AUTOPLAY=1` hook in
      `tools/LaunchFreya/MainWindow.axaml.cs` (clicks Play once the server is
      ONLINE), so there is no `02-click-play` step; `03-launch-launcher.sh`
      launches detached and waits for the `client.exe` window.
- [x] AU-6 `05-eula-accept.sh` -- EULA confirmed by 506x362 size, I Agree at
      coords.json `eula.agree`, waits for the 1280x960 game window.
- [x] AU-7 `06-login.sh` -- intro-skip until `login_field.png` ref matches,
      clear+type user/pass, Accept, wait for `charselect_frame.png`.
- [x] AU-8 `07-charselect-enter.sh` -- first nameplate + Enter; confirm we
      leave charselect (load screen begins).
- [x] AU-9 `08-wait-ingame.sh` -- in-game confirm via enbmod heartbeat
      (`type(enb.self())=="table"`), reports `inspace`/`state`.
- [x] AU-10 `login.sh` orchestrator + `SKILL.md`; clean end-to-end runs
      (`/tmp/enblogin/login_run3.log`: `INGAME inspace=true state=space`).
- [x] AU-12 Start-in-space: 02-seed `ensure_slot0_in_space` sets slot-0
      `avatar_info.sector = sector/10` (+ `avatar_position.sector_id`, pos 0)
      when docked (sector > 9999), so the char loads into space not a station.
      Server reads `avatar_info.sector` (AccountManager.cpp:694) and docks when
      > 9999 (PlayerManager.cpp:209). Idempotent, runs after 00-kill so no live
      session overwrites it. `ENB_START_IN_SPACE=0` opts out. NOT baked into the
      shared `just seed-dev-account` -- that would break the undock/mining
      integration tests that rely on a docked start.
- [ ] AU-11 Use the skill to log in; resume Phase AS HUD work (hide native
      bottom widgets + restack action buttons).

## Notes

- coords.json (validated): EULA window-relative in 506x362 (`agree` 430,337);
  game window 1280x960 -- `intro.skip` 640,480, `login.user` 300,232,
  `login.pass` 300,297, `login.accept` 1010,905, `charselect.first` 150,70,
  `charselect.enter` 1053,897. Refs `login_field.png` (165,228) and
  `charselect_frame.png` (70,50) match their screen at <1 MAD, mismatch others
  at >30.
- enbmod.log is CRLF (Win32 DLL under WINE); `08-wait-ingame.sh` strips `\r`
  before matching `[run]` output, and uses `tostring()` since the channel
  prints `<boolean>` without the value.
- `import -window <id>` holds XGrabServer during capture; if the window dies
  mid-capture it wedges all X clients -- recover with `pkill -9 import`. This
  looked like a "frozen X server" once; it is not a GPU/DirectX freeze.
