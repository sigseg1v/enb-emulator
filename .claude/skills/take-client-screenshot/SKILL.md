---
name: take-client-screenshot
description: Capture a PNG of the running Earth & Beyond client window (client.exe under WINE) so you can SEE whether a HUD/mod/UI change actually worked. Use when debugging the live game visually -- after editing Lua, re-staging, and reloading -- to verify the result instead of guessing from logs. Requires the game running via `just play-local` on the X11 display.
---

# Screenshot the live client

The EnB client runs as `client.exe` under WINE on the host X11 display
(`DISPLAY=:0`). You can grab its window to a PNG and then Read the PNG to see the
actual game UI -- the only way to truly verify a HUD/overlay change.

## Take a shot

```bash
.claude/skills/take-client-screenshot/scripts/screenshot.sh           # -> /tmp/enbshot/client-N.png
.claude/skills/take-client-screenshot/scripts/screenshot.sh /tmp/x.png # explicit path
```

The script prints the absolute path it wrote. Then view it:

```
Read /tmp/enbshot/client-1.png
```

## How it finds the right window (don't shortcut this)

There are usually several windows whose TITLE contains "Earth & Beyond" (a
browser tab on the wiki, the Discord status channel, ...). The game is identified
by its **WM_CLASS = `client.exe`**, not its title. WINE also creates a 1x1
unmapped "Default IME" helper that shares that class, so the script keeps only
windows that are `Map State: IsViewable` and picks the largest by area. If you do
it by hand:

```bash
for id in $(xdotool search --class client.exe); do
  xwininfo -id "$id" | grep -q "IsViewable" && import -window "$id" out.png
done
```

`import` is ImageMagick; `xdotool`/`xwininfo`/`wmctrl` are all present on the host.

## Gotchas

- **"no viewable client.exe window found"** -> the game isn't running (or mods/
  launch failed). Start it with `just play-local`. This skill cannot launch the
  WINE GUI itself.
- The capture is of the window's current frame. If you just sent a `reload()` over
  the command channel, give it ~1s (the DLL polls the cmd file every ~15 ticks)
  before screenshotting, or the shot predates the reload.
- A minimised/occluded window may capture stale or black pixels under some
  compositors; the script captures the window directly (`import -window`) which
  usually works even when occluded, but raise it first (`xdotool windowactivate`)
  if a shot looks wrong.

## The test-verify loop

Combine with the other two skills for an edit -> verify cycle with no game
restart (see run-lua-client-command for the full loop):

1. Edit the Lua (e.g. `freya/.../mods/freya-hud/freya_ui.lua`).
2. Re-stage: `.claude/skills/run-lua-client-command/scripts/restage.sh`.
3. Reload in-client: push `reload()` over `enbmod.cmd`.
4. **Screenshot here** and Read it to confirm the change rendered.

Related: run-lua-client-command (edit/run/reload), read-lua-client-logs (logs).
