---
name: run-lua-client-command
description: Drive a line of Lua into the RUNNING Earth & Beyond client (enbmod DLL) from the shell and read its result back, hot-reload the mod scripts, and run an edit->reload->screenshot test-verify loop. Use when debugging the live game -- e.g. reading character levels/vitals, probing client memory (enb.mem.*, enb.aux, enb.rpg_level), or testing a HUD/mod change against the real client without typing /run in-game or restarting it. Requires the game running via `just play-local` with Client Mods enabled.
---

# Run a Lua command in the live client

The enbmod DLL (injected into `client.exe` under WINE) polls a file called
`enbmod.cmd` every ~15 ticks. Any line you write to it is executed through the
exact same path as the in-game `/run` console, and the result/error is appended
to `enbmod.log` as `[run] ...`. This lets you (the agent) iterate on the running
game without touching the keyboard.

This is a dev/debug channel, not an RPG. Single writer assumed. It only does
anything while the game is actually running with mods enabled.

## Resolve the client directory

`enbmod.cmd` and `enbmod.log` live next to the staged DLL, i.e. the folder of the
configured client exe. Resolve it from the launcher settings (do NOT hardcode --
there can be more than one WINE prefix):

```bash
SETTINGS="/data/dev/enb-emulator/tools/LaunchFreya/bin/Debug/net10.0/FreyaLauncher.settings.json"
DIR=$(python3 -c "import json,os;print(os.path.dirname(json.load(open('$SETTINGS'))['ClientPath']))")
echo "client dir: $DIR"
```

## Send a command and read the result

Append the Lua line to `enbmod.cmd`, then watch the tail of `enbmod.log` for the
new `[run]` line. The poll fires within ~1s; loop until it shows up rather than
sleeping blindly.

```bash
# 1) note where the log currently ends, so we only read NEW output
START=$(wc -l < "$DIR/enbmod.log" 2>/dev/null || echo 0)

# 2) queue the command (a bare expression auto-echoes; statements run as-is)
printf '%s\n' 'return 1 + 1' >> "$DIR/enbmod.cmd"

# 3) poll for the new output (give up after ~5s)
for i in $(seq 1 25); do
  NEW=$(tail -n +$((START+1)) "$DIR/enbmod.log" 2>/dev/null)
  [ -n "$NEW" ] && { echo "$NEW"; break; }
  sleep 0.2
done
```

Output appears as `[run] 2`. Multiple return values print one `[run]` line each;
tables are dumped via the global `dump()`; errors show as `[run] error: ...`.

## What you can probe (the `enb.*` API)

- `return 1 + 1` -- sanity check the channel.
- `return enb.self()` -- player table: hull/shield/energy (+ _max), x/y/z, and
  combat_lvl/trade_lvl/explore_lvl (the level fields are flat-offset reads and may
  be uncalibrated -> nil; prefer rpg_level below).
- `return enb.rpg_level("RPGInfo.CombatLevel"), enb.rpg_level("RPGInfo.ExploreLevel"), enb.rpg_level("RPGInfo.TradeLevel")`
  -- discipline levels off the RPG manager (the reliable source). The keys are the
  client's own DOTTED form ("RPGInfo.CombatLevel" with a DOT) -- the space form
  never matches an entry. Returns NOTHING (zero values, blank output) until the
  level hook has fired -- see next bullet.
- `return enb.xp_frac("explore")` -- per-discipline xp fill fraction (0..1) off the
  XpBars controller ("combat" / "trade" / "explore"). `enb.xp_ctrl()` is the
  captured controller pointer; `0` means the XpBars updater (`0x0058cb50`) has not
  run this session yet (go to space) so xp_frac returns the empty fallback.
- `return enb.rpg_mgr()` -- diagnostic: pointer to the captured RPG manager.
  `0` means the client's level-reader (`0x0074bfb0`) has not run this session, so
  `rpg_level` returns nothing. Open the in-game status/avatar panel to trigger it,
  then re-check; non-zero means levels are now readable.
- `return enb.aux("HullPoints"), enb.aux("MaxHullPoints"), enb.aux("MaxShieldPower"), enb.aux("MaxEnergyPower")`
  -- ship vitals from the AuxData property bag (floats; nil out of space).
- `return enb.vitals()` -- live hull/shield/energy fill fractions.
- `return enb.mem.u32(0x00b6c000)` / `enb.mem.f32` / `enb.mem.ptr` /
  `enb.mem.str(addr)` / `enb.mem.wstr(addr)` -- raw (guarded) memory probing. The
  namespace is `enb.mem`, NOT `enb.read` (also: u8/u16/i32/f64/readable/chain/
  write_u32/write_f32).
- `return enb.call(addr, this, a, b, ...)` -- call a client function `__thiscall`
  (ECX=`this`); `enb.call_cdecl(addr, a, b, ...)` for cdecl. Returns EAX.
- `return enb.state(), enb.inspace()` -- game state + in-space heartbeat.
- `return enb.screen()` -- backbuffer w,h (0,0 before first present).

## Hot-reload the mod scripts (no game restart)

You can change Lua and see it live without relaunching. Two halves:

1. **Re-stage the files.** The client loads scripts from `<clientDir>/scripts/`,
   NOT from the repo -- so a repo edit is invisible until you copy it over. Use
   the bundled script (resolves the client dir from the launcher settings):

   ```bash
   .claude/skills/run-lua-client-command/scripts/restage.sh            # all mods + lib + init
   .claude/skills/run-lua-client-command/scripts/restage.sh freya-hud  # one mod only
   ```

   It overwrites changed files but never deletes at the destination, so the
   install-specific `calib_data.lua` and any user-only mod survive. It does NOT
   rebuild the DLL -- Lua edits don't need a build. (If you changed C++ in
   `enbmod/src/`, you must `just build-enbmod` AND relaunch -- a running DLL
   can't be hot-swapped.)

2. **Reload in-client.** Push `reload()` over the command channel:

   ```bash
   printf '%s\n' 'reload()' >> "$DIR/enbmod.cmd"
   ```

   `reload()` drops every cached pure-Lua module from `package.loaded` (so
   `require` re-reads from disk), clears accumulated event handlers, and re-runs
   `init.lua` -- which re-discovers and re-initialises every staged mod. Watch
   `enbmod.log` for `reload: re-running init.lua` and the per-mod `mod loaded:`
   lines. A reload error logs `reload FAILED: ...`.

## The test-verify loop

Tie it together to iterate on a UI/HUD change entirely from the shell:

1. Edit the Lua in the repo (e.g. `freya/.../mods/freya-hud/freya_ui.lua`).
2. `restage.sh` -- copy it next to the client.
3. `printf 'reload()\n' >> "$DIR/enbmod.cmd"` -- reload in-client (wait ~1s).
4. Optionally probe state with a `/run` command (read levels/vitals/offsets).
5. **Take a screenshot and Read it** to confirm it actually rendered -- use the
   `take-client-screenshot` skill. Logs tell you it loaded; only the screenshot
   tells you it's correct.

Loop steps 1-5 until the screenshot looks right.

## Gotchas

- **No output at all** usually means either the game is not running / mods are off,
  OR the called function returned zero values (e.g. `rpg_level` before its hook
  fired). Check `enb.rpg_mgr()` and that `enbmod.log` is being written.
- A *statement* (e.g. a `for` loop) must end in a `return ...` to echo anything.
- One command per line. Multiple lines in one write each run in order.
- Keep `[[ ]]` long-string literals on a single line -- the channel is line-based.
- Related: read-lua-client-logs (for tailing logs and the docker side).
