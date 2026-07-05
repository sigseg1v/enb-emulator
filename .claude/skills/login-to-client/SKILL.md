---
name: login-to-client
description: Launch / relaunch / restart the local EnB client and log it all the way into the game (clean slate -> in-game on a character). The client logs ITSELF in via the injected enbmod auto-login (EULA + credentials + character-enter, no OCR / no synthetic input); in-game is confirmed over the enbmod Lua channel. Use to launch or relaunch the client (e.g. after rebuilding enbmod.dll, which only reloads on a fresh launch), or whenever you need a live client to do in-game / Lua-HUD work and don't want a human to click through login.
---

# Log in to the EnB client

Takes the local stack from "nothing running" to "in-game on a character", with a
real verification signal at every step (no blind sleeps). Built for this dev box
(WINE client on `DISPLAY=:0`, native Avalonia launcher, single-client proxy).

**This login is OCR-free.** The client logs itself in: the injected enbmod
auto-login (`freya/client-injection/enbmod/src/autologin.cpp`) accepts the EULA,
fills + submits the credentials, and enters the world as the named character by
driving the client's OWN front-end code paths off game memory -- no pixels, no
synthetic mouse/keyboard. The only "reading" is the enbmod Lua channel in step
08, which is authoritative for "in-game".

## TL;DR

```bash
# full from-scratch login (kill -> stack up -> seed-if-needed -> launch -> in-game)
bash .claude/skills/login-to-client/scripts/login.sh
```

On success the last line is `[login] ==== LOGIN COMPLETE ====` and the character
is loaded (08 prints `INGAME inspace=<bool> state=<s>`). On failure a step prints
`STUCK=<step> ...` and `login.sh` aborts with that step.

## How it works (the flow)

Each step confirms its own success before the next runs:

| step | screen | how it's confirmed |
|------|--------|--------------------|
| `00-kill.sh` | clean slate | client window gone |
| `01-start-stack.sh` | stack up | `docker compose ps` server/net7go/proxy Up |
| `02-seed.sh` | account ready | `avatar_info` row count (skips if already seeded) |
| `03-launch-launcher.sh` | client window | `client.exe` window appears; launched with auto-login armed |
| `08-wait-ingame.sh` | in-game | Lua `type(enb.self())=="table"` via enbmod channel |

Steps 05 (EULA), 06 (login), 07 (character-select) that used to click/type
through the front end are **gone** -- the injected auto-login does all three.

### Key design points (why it's reliable here)

- **The client logs itself in.** `03-launch-launcher.sh` runs
  `just play-local <user> <pass> <char>`, which exports the auto-login env
  (`ENB_EULA=ACCEPT`, `ENB_ACC_NAME`, `ENB_ACC_PASS`, `ENB_CHARACTER`,
  `FREYA_AUTOPLAY=1`). The injected enbmod then: pre-accepts the trial EULA in the
  registry + dismisses the modal license dialog from its own thread, locates the
  credential form by a runtime signature and submits it, and enters the world once
  the chosen character's name appears in the avatar list. A wrong memory read is
  VEH-guarded (harmless); it never guesses a write/call, so a layout mismatch
  degrades to "did not log in", never a crash.
- **No mouse click on Play.** The launcher is our own Avalonia app; `FREYA_AUTOPLAY=1`
  makes it click Play itself once the server reports ONLINE
  (`tools/LaunchFreya/MainWindow.MaybeAutoPlay`). Synthetic clicks on the launcher
  were defeated by pointer-grab contention on this desktop.
- **In-game truth comes from the enbmod Lua channel**, not a screenshot: the load
  screen is unreliable to classify, but the injected DLL answering
  `type(enb.self())=="table"` is authoritative. NOTE: `enbmod.log` is written by a
  Win32 DLL under WINE, so its lines are **CRLF** -- strip `\r` before any string
  compare (08 does this; bit us once).
- **The default character is `<user>te`** -- `seed-dev-account` names characters
  `<user><class-suffix>` and creates them te/je/jd/tt/ts in order, so slot 0 is
  `<user>te`, and `02-seed.sh` forces slot 0 to start in space. Override with
  `ENB_LOGIN_CHAR`.

## Modes / env

- `ENB_ATTACH=1` -- skip 00-03; just confirm the ALREADY-running client reached
  in-game (08 only). The running client must have been launched with auto-login
  armed (03 / `just play-local <user> <pass> <char>`): with no OCR there is no way
  to drive a client parked at the login screen, so attach only verifies.
- `ENB_SKIP_SEED=1` -- skip 02 (account known-seeded).
- `ENB_FORCE_SEED=1` -- force a re-seed in 02 even if chars exist.
- `ENB_LOGIN_USER` / `ENB_LOGIN_PASS` -- credentials (default `devuser`/`devpass`).
- `ENB_LOGIN_CHAR` -- character to auto-enter (default `<user>te`).

Run any building block standalone (they're idempotent and re-entrant), e.g.
`bash scripts/03-launch-launcher.sh`. Work logs land in `/tmp/enblogin/`.

## Injecting enbmod into a client that is ALREADY running

If a client is already up but has NO enbmod (e.g. launched by the Net-7 launcher
or a bare `wine client.exe`), the enbmod Lua channel step 08 relies on can be
added without relaunching: `just inject-enbmod` (or the launcher's
"Inject (running client)" button) hot-attaches the current enbmod.dll + scripts to
the running `client.exe` in the configured WINE prefix. Set `WINEPREFIX` to target
a specific prefix. This is the runtime-attach counterpart to the launch-time
injection `03` performs.

## Environment gotchas (this box)

- **Cinnamon screensaver** can intercept input when idle. It does not affect the
  OCR-free login (no synthetic input), but if you later drive the client by hand
  and clicks do nothing, run `cinnamon-screensaver-command -d`.
- **Never move/resize a WINE window** (xdotool/wmctrl move) -- it desyncs WINE's
  internal rect from its X position. Window discovery here only reads geometry, it
  never moves anything.

## Related

- `run-lua-client-command` -- drive the game once logged in (enbmod cmd channel).
- `take-client-screenshot` -- screenshot the client if you need a visual.
- Plans: `plans/51-phase-au-login-automation.md`,
  `plans/56-phase-az-env-autologin-multibox.md` (the auto-login mechanism).
