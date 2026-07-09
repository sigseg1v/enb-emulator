---
name: explore-live
description: Run the sector-exploration survey against a LIVE client you attached enbmod into (native client.exe under WINE + Net7Proxy.exe, no Freya docker stack), with unattended crash recovery -- it resolves the enbmod store dir from the running client's /proc cwd, and on a client crash/wedge it relaunches via the real Net-7 launcher (LaunchNet7.exe), presses Play (the 'p' key), re-injects enbmod, and lets enbmod autologin return to in-game, then resumes. Use when the owner says "explore live" against their own attached client. Reads account/character from a gitignored .env.
---

# Explore live (attached client + Net7Proxy, crash-recovering)

One command to survey sectors against a **live** client -- one the owner launched
(or one you attached enbmod into), talking to the real reference server through
`Net7Proxy.exe`, with **no Freya docker stack** involved:

```bash
bash .claude/skills/explore-live/scripts/explore-live.sh
```

It reuses the [`explore-sector`](../explore-sector/SKILL.md) survey engine
(`drive_lua.py`, the enbmod Lua channel) unchanged. This skill only adds the two
things the live/attach case needs that the plain explore-sector entry does not:

1. **Correct path resolution for a client we did not launch.** The enbmod
   `enbmod.cmd`/`enbmod.log` store dir is resolved from the **running client.exe's
   `/proc/<pid>/cwd`** (found via its X window), not from our launcher's
   `settings.json` -- which would point at the wrong WINE prefix / mbox slot for an
   attached client. The WINE prefix and the launcher path are resolved the same
   way (from the running client's environ, else conventional defaults).

2. **Unattended crash recovery.** If the client crashes or hard-wedges mid-survey,
   the driver runs `recover.sh`: it relaunches the real **Net-7 launcher**
   (`LaunchNet7.exe`), presses **Play** (the `p` accelerator key -- no coordinate
   needed; starts the proxy + client), re-injects **enbmod**
   (`inject.exe --proc client.exe`), and enbmod's in-client **autologin** accepts
   the EULA, logs in, and enters the character -- all from the `.env` credentials,
   no OCR. Then the survey resumes from the persistent ledger.

## Prerequisites

- A **live client** running under WINE (`DISPLAY=:0`) with **enbmod injected** (the
  standalone injector, `inject.exe --proc client.exe`), OR nothing running yet and a
  working `LaunchNet7.exe` + `.env` credentials so recovery can bring one up.
- **`Net7Proxy.exe`** running under WINE for a LIVE run (or the launcher's Play will
  start it). If instead the **docker `net7proxy`** is up, the entry auto-detects a
  LOCAL run and disables packet capture (never pcap a local Freya run).
- A filled-in **`.env`** (see below). It is gitignored.

## Configure (.env)

```bash
cp .claude/skills/explore-live/.env.example .claude/skills/explore-live/.env
$EDITOR .claude/skills/explore-live/.env
```

The values that matter:

| Key | What it is |
|---|---|
| `ENB_ACC_NAME` / `ENB_ACC_PASS` | Account for enbmod autologin (read from the client process env; never typed). |
| `ENB_CHARACTER` | Character to enter. |
| `ENB_EULA` | `ACCEPT` to auto-accept the EULA on a fresh client. |
| `ENB_EXPLORE_WORKDIR` | Persistent survey state (ledgers + log + captures), **outside the repo**. |
| `ENB_WINEPREFIX` | WINE prefix of the EnB install. Blank = auto-detect (running client env, else `$HOME/.wine-enb`). |
| `ENB_LAUNCHNET7` | Path to `LaunchNet7.exe`. Blank = the standard spot in the prefix (`.../Net-7/bin/LaunchNet7.exe`). |
| `ENB_LAUNCHNET7_PLAY_XY` | Fallback only. Window-relative `x,y` of the launcher's **Play** button, used if the `p` accelerator key does not start the client. Blank = recovery relies on `p` (verified) and, failing that, Enter, else screenshots the launcher and asks. |

## How recovery works (and its one manual seed)

`recover.sh` is called both at startup (if the client is not in-game yet) and by
`drive_lua.py` on a mid-run wedge (via `ENB_RECOVER_CMD`). Steps: no-op if already
in-game -> kill the stale `client.exe` -> `wine start LaunchNet7.exe` with the
autologin creds exported -> press Play -> wait for the client window -> inject
enbmod -> wait for the enbmod channel to report in-game.

Recovery needs **no screen coordinate at all**: Play is fired with the `p`
accelerator key (the underlined P in "Play"), verified live to spawn `client.exe`
in ~6s. `ENB_LAUNCHNET7_PLAY_XY` (a window-relative Play pixel) and Enter are only
fallbacks if the key is somehow dropped; the first time even those fail, the
launcher is screenshotted to `WORKDIR/launchnet7.png` and the coordinate is asked
for. Launch, window wait, inject, and autologin use no coordinates either.

## Liveness / wedge detection

Same as explore-sector: the enbmod command poll is clocked off the client message
pump, so a hard-wedged client stops answering `enbmod.cmd`. A scene load silences
the channel only briefly (< `ENB_LUA_HANG_SECS`, default 150s); a wedge never
clears. Continuously silent past the threshold + a failed liveness probe == wedge
-> recovery. This distinguishes a wedge from a normal gate-transit load screen.

## Scripts

| Script | Role |
|---|---|
| `scripts/explore-live.sh` | Entry: load `.env`, resolve WINE prefix + client dir + launcher, export autologin creds, preflight, ensure in-game (recover if needed), hand off to `drive_lua.py`. |
| `scripts/recover.sh` | Crash recovery: relaunch `LaunchNet7.exe` -> Play -> inject enbmod -> wait autologin in-game. Standalone-runnable. |
| `../explore-sector/scripts/drive_lua.py` | The survey engine (shared, unchanged). |

## Rules carried over from explore-sector

- **Screen-free survey.** All facts/actions go through the enbmod Lua channel
  (`enb.*`), never screenshots. The **only** synthetic input is the launcher Play
  keypress (`p`) during recovery.
- **Gravity-well refusal**, **hybrid target order**, **completion = our ledger**,
  and the persistent ledger/action-log accounting are all inherited verbatim from
  the survey engine -- see [`explore-sector`](../explore-sector/SKILL.md).
- **Capture is LIVE-only.** `ENB_PCAP=1` is honoured only for a WINE-proxy LIVE run;
  a docker `net7proxy` forces `ENB_PCAP=0`.

## Related skills

- [`explore-sector`](../explore-sector/SKILL.md) -- the survey engine + the
  local/dev entry.
- [`login-to-client`](../login-to-client/SKILL.md) -- provides the shared WINE
  window helpers (`lib.sh`) and the in-game check (`08-wait-ingame.sh`).
- [`run-lua-client-command`](../run-lua-client-command/SKILL.md) -- the enbmod Lua
  channel this drives.
