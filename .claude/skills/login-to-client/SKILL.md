---
name: login-to-client
description: Log the local EnB client all the way into the game (clean slate -> in-game on a character), driving the launcher, EULA, login screen, and character select automatically with screenshot/ref verification at each step. Use when you need a live client to do in-game / Lua-HUD work and don't want a human to click through login.
---

# Log in to the EnB client

Takes the local stack from "nothing running" to "in-game on a character", with a
real verification signal at every step (no blind sleeps). Built for this dev box
(WINE client on `DISPLAY=:0`, native Avalonia launcher, single-client proxy).

## TL;DR

```bash
# full from-scratch login (kill -> stack up -> seed-if-needed -> launch -> in-game)
bash .claude/skills/login-to-client/scripts/login.sh
```

On success the last line is `[login] ==== LOGIN COMPLETE ====` and the character
is loaded (08 prints `INGAME inspace=<bool> state=<s>`). On failure a step prints
`STUCK=<step> ...` and a screenshot path, and `login.sh` aborts with that step.

## How it works (the flow)

The client is a deterministic 1280x960 render, so fixed window-relative click
coords (in `scripts/coords.json`) are stable across runs. Each screen is
confirmed before acting:

| step | screen | how it's confirmed |
|------|--------|--------------------|
| `00-kill.sh` | clean slate | client window gone |
| `01-start-stack.sh` | stack up | `docker compose ps` server/net7go/proxy Up |
| `02-seed.sh` | account ready | `avatar_info` row count (skips if already seeded) |
| `03-launch-launcher.sh` | client window | `client.exe` window appears (autoplay clicks Play) |
| `05-eula-accept.sh` | EULA dialog | window is 506x362; then 1280x960 appears |
| `06-login.sh` | login screen | ref `refs/login_field.png` matches; ends on charselect |
| `07-charselect-enter.sh` | character select | ref `refs/charselect_frame.png` matches |
| `08-wait-ingame.sh` | in-game | Lua `type(enb.self())=="table"` via enbmod channel |

### Key design points (why it's reliable here)

- **No mouse click on Play.** The launcher is our own Avalonia app; setting
  `FREYA_AUTOPLAY=1` makes it click Play itself once the server reports ONLINE
  (`tools/LaunchFreya/MainWindow.MaybeAutoPlay`). Synthetic clicks on the
  launcher were defeated by pointer-grab contention on this desktop.
- **The intro cinematic is skipped** by clicking the window centre until the
  login-field ref matches (06).
- **In-game truth comes from the enbmod Lua channel**, not a screenshot: the
  load screen is unreliable to classify, but the injected DLL answering
  `type(enb.self())=="table"` is authoritative. NOTE: `enbmod.log` is written by
  a Win32 DLL under WINE, so its lines are **CRLF** -- strip `\r` before any
  string compare (08 does this; bit us once).
- **Input is XTEST at absolute root coords**: raise window -> read `xwininfo`
  "Absolute upper-left" -> `xdotool mousemove --sync` + `click`. `--window`
  XSendEvent is unreliable for WINE/Avalonia.

## Modes / env

- `ENB_ATTACH=1` -- skip 00-03, drive the ALREADY-running client to in-game
  (05-08). Use when a client is up and you don't want to bounce the stack.
- `ENB_SKIP_SEED=1` -- skip 02 (account known-seeded).
- `ENB_FORCE_SEED=1` -- force a re-seed in 02 even if chars exist.
- `ENB_LOGIN_USER` / `ENB_LOGIN_PASS` -- credentials (default `devuser`/`devpass`).

Run any building block standalone (they're idempotent and re-entrant), e.g.
`bash scripts/05-eula-accept.sh`. Work screenshots land in `/tmp/enblogin/`.

## Environment gotchas (this box)

- **A QEMU/KVM VM warping the pointer** (XWarpPointer ~3x/s) defeats XTEST
  clicks. If clicks land nowhere, check `ps -ef | grep qemu` for a *running*
  (not `-S`/paused) VM and pause/stop it.
- **Cinnamon screensaver** intercepts ALL input when idle (clicks go to the
  overlay, not WINE). `login.sh` does not disable it; if a fresh run's clicks do
  nothing, run `cinnamon-screensaver-command -d` and
  `gsettings set org.cinnamon.desktop.session idle-delay 0`.
- **`import -window <id>` can grab the X server and hang** if the window is
  destroyed mid-capture, which wedges ALL X clients until the `import` is killed.
  If `xdotool`/`xset` start timing out, `pkill -9 import` first -- it is almost
  never a real X freeze.
- **Never move/resize a WINE window** (xdotool/wmctrl move) -- it desyncs WINE's
  internal rect from its X position and breaks click coordinate translation.

## Re-capturing coords / refs (if the client UI ever changes)

1. Get the live client window id: `xdotool search --class client.exe`.
2. Screenshot it: `import -window <id> /tmp/shot.png` (1:1 with click coords).
3. Read the PNG, find the new pixel coords, update `scripts/coords.json`.
4. Re-cut a ref crop: `python3 scripts/detect.py savecrop /tmp/shot.png \
   scripts/refs/<name>.png X Y W H`, choosing an OPAQUE UI region (not the
   animated starfield/nebula background), and validate it matches the right
   screen and mismatches the others with `detect.py match`.

## Related

- `run-lua-client-command` -- drive the game once logged in (enbmod cmd channel).
- `take-client-screenshot` -- the screenshot helper this skill reuses.
- Plan: `plans/51-phase-au-login-automation.md`.
