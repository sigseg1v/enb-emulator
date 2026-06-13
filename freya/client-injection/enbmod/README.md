# enbmod -- injected Lua runtime for Earth & Beyond (client.exe)

A 32-bit DLL that injects a Lua 5.4 VM into the E&B client, hooks the message pump for a
per-frame tick, and exposes an `enb.*` Lua API for reading game state and (later) hooking/calling
game functions. Plus a tiny `inject.exe` loader. Built to run under Wine.

## Layout

```
enbmod/
  Makefile            cross-compile to win32 (mingw-w64)
  src/
    mem.h             guarded memory primitives (VirtualQuery-gated reads/writes)
    game.h/.cpp       address book (static code addrs) + runtime-editable offsets table
    hooks.h/.cpp      MinHook setup; PeekMessageA tick; opt-in game-fn event hooks
    lua_api.h/.cpp    the enb.* Lua bindings
    log.h/.cpp        enbmod.log writer
    dllmain.cpp       DllMain -> worker thread: Lua init, script load, hot-reload
  inject/inject.cpp   CreateRemoteThread/LoadLibraryA injector
  scripts/
    init.lua          loaded on startup, hot-reloaded on change
    calib.lua         runtime helpers to FIND the offsets (dump/scan/watch)
  third_party/lua     Lua 5.4.7 (static)
  third_party/minhook MinHook (static)
```

## Build

```
make            # -> build/enbmod.dll + build/inject.exe
make clean
```

Requires `i686-w64-mingw32-{gcc,g++,ar}` (mingw-w64, 32-bit). Outputs are PE32 (Intel 80386).

### Why these flags (the parts that actually matter)
- **`i686-w64-mingw32-*` (32-bit).** `client.exe` is 32-bit; an injected DLL **must** match the
  target architecture. Lua is built x86 to match.
- **`-static -static-libgcc -static-libstdc++`.** The game directory won't have
  `libgcc_s_dw2-1.dll` / `libstdc++-6.dll`; static linking makes a self-contained DLL.
- **`-fno-strict-aliasing`.** Lua and the memory primitives type-pun; disable strict aliasing.
- **`-Wl,--kill-at`.** Strip `@N` stdcall decoration from exports (clean names for the proxy path).
- **Lua as a static lib** (no `LUA_BUILD_AS_DLL`): single self-contained module.
- **MinHook needs `hde32.c`** (the length disassembler) compiled alongside buffer/hook/trampoline.

### Fault isolation -- how, and why not SEH
A bad/uncalibrated offset must not crash the game. mingw-w64 GCC (32-bit) has **no** MSVC SEH
(`__try`/`__except`). clang was tested too: with `-fms-extensions` its 32-bit `__try` *compiles*
on the mingw target but **does not catch at runtime** (verified under Wine -- the AV drops to
WineDbg; 32-bit SEH only actually works on the `-windows-msvc` target, unusable when
cross-compiling). So neither compiler's `__try` is viable here.

Instead `mem.h` uses a **Vectored Exception Handler** (`mem.cpp`): a thread-local guard flag +
`__builtin_setjmp`, and a VEH that -- *only while a guarded read is in flight on this thread* --
`__builtin_longjmp`s back with a failure. Every read/write still does the cheap `VirtualQuery`
pre-check first; the VEH is the backstop that also covers the TOCTOU race (page unmapped between
check and deref). This was verified working under Wine (null and wild-pointer reads both return
the default and the process survives). It's scoped to our own reads, so it does **not** swallow
the game's legitimate access violations. `mem::install_guard()` is called once from the worker
thread before any game memory is touched.

## Inject

Start the game, then:
```
wine build/inject.exe                       # targets client.exe, loads ./enbmod.dll
wine build/inject.exe --proc client.exe path\to\enbmod.dll
wine build/inject.exe --pid 1234
```
`enbmod.dll` and the `scripts/` folder must sit together (the DLL resolves `scripts/init.lua`
relative to its own location). On success you'll get `enbmod.log` next to the DLL.

Alternative loaders (see `../LUA-MODDING-DESIGN.md §2`): a `d3d8.dll` proxy (the game's actual
render API) with `WINEDLLOVERRIDES=d3d8=n,b`; or piggyback the Net-7 launcher.
**Do not** proxy `authlogin.dll` -- Net-7 already patches it.

## The Lua API

| Call | Returns / effect |
|---|---|
| `enb.log(s)` | append to `enbmod.log` |
| `enb.base` | `0x00400000` |
| `enb.addr.<Name>` | static code addresses (StatBlock, SkillLifecycle, ChatChannel, …) |
| `enb.mem.u8/u16/u32/i32/f32/f64/ptr(a)` | guarded reads (0 if unreadable) |
| `enb.mem.str(a[,cap])` / `wstr` | C / UTF-16 string |
| `enb.mem.readable(a[,n])` | bool |
| `enb.mem.chain(base, o1, o2, …)` | pointer-chain walk → final address |
| `enb.mem.write_u32/write_f32(a,v)` | bool (false if not writable) |
| `enb.calibrate{ … }` | set offsets at runtime (hot-reloadable) |
| `enb.offsets()` | current offsets table |
| `enb.self()` | `{ base, name, hull, shield, energy, combat_lvl, x,y,z, … }`; uncalibrated fields absent, `base==0` if no `player_ptr_addr` |
| `enb.target()` | `{ base, name, distance, … }` or `nil` |
| `enb.state()` | game-state name: `"space"`/`"station"`/`"login"`/`"charsel"`/`"load"`/`"unknown"` (calibration-driven; `"unknown"` until `game_state_addr` is set → HUD shows) |
| `enb.cursor(on)` | draw our procedural mouse arrow ON TOP of the overlay (the native cursor renders under the HUD); `on=false` stops it |
| `enb.patch_ret(addr[,pop])` | overwrite a function entry with `ret` to suppress a native draw routine. `pop` = callee stack-cleanup bytes (`0`→`0xC3`, `N`→`0xC2 imm16`). DANGEROUS: real-client-verified `(addr,pop)` only |
| `enb.on_tick(fn)` | fn runs every pump iteration (game thread) |
| `enb.on_skill(fn)` / `enb.on_chat(fn)` | event-hook callbacks (need `enable_event_hooks`) |
| `enb.enable_event_hooks()` | install the game-fn hooks (off by default) |
| `enb.screen()` | `w, h` of the real backbuffer (`0,0` until the first present) |
| `enb.measure(s)` | `w, h` of `s` in the overlay font (`0,0` until the atlas is built) |
| `enb.on_input(fn[,mask])` | `fn(msg,wparam,lparam)` from the PeekMessageA hook; **return truthy to SWALLOW** (msg → `WM_NULL`). Fail-open on Lua error. `mask` = `enb.WANT_KEY\|WANT_CHAR\|WANT_MOUSE` (default all). `enb.msg.*` = raw `WM_*` ids |
| `enb.draw.text(x,y,s[,rgb])` | overlay text (rebuilt each tick) |
| `enb.draw.rect(x,y,w,h[,rgb[,filled[,alpha]]])` | overlay rectangle |
| `enb.draw.line(x0,y0,x1,y1[,rgb[,alpha]])` | overlay line |
| `enb.draw.rect_grad(x,y,w,h,rgbTop,rgbBot[,alpha])` | vertical-gradient filled rect (per-vertex color) |
| `enb.draw.rrect(x,y,w,h,radius[,rgb[,alpha[,filled]]])` | rounded rectangle (triangle-fan corners) |
| `enb.draw.rrect_grad(x,y,w,h,radius,rgbTop,rgbBot[,alpha])` | rounded gradient rect |
| `enb.draw.image(path,x,y[,w,h[,alpha]])` | blit PNG/TGA/BMP/JPG (stb_image, cached, alpha-blended) |
| `enb.tap(vk)` / `enb.key(vk[,down])` | post key to game window (fires bound abilities) |
| `enb.char(cp)` | post WM_CHAR |
| `enb.hwnd()` | game window handle |
| `enb.call(addr,this,…)` | call a game member fn as `__thiscall`, returns EAX |
| `enb.call_cdecl(addr,…)` | call a free fn (cdecl/stdcall), returns EAX |

### Overlay
client.exe renders through **Direct3D 8** (`client.exe` imports `d3d8.dll`; an earlier
DirectDraw Flip/Blt hook never fired in a full session). The overlay resolves the
`IDirect3DDevice8` vtable from a throwaway probe device, hooks **Present** (vtable index 15)
with MinHook, and in the hook draws a per-frame display list using the *game's* device:
state-blocked `XYZRHW` quads (`DrawPrimitiveUP`) for rects/lines/images and a GDI-baked
font-atlas texture (Tahoma, ASCII 32..126) for text. Device state is snapshotted/restored
around the draw via `CreateStateBlock(D3DSBT_ALL)`. The list is immediate-mode: Lua rebuilds it
every `on_tick` (the tick swaps staging→render under a lock so the Present thread always sees a
complete frame). Images load via `stb_image` into pow2 `D3DPOOL_MANAGED` textures (managed
resources survive a device `Reset`, so no Reset hook is needed); all GPU caches rebuild if the
device pointer ever changes. The one-shot `overlay: Present hook FIRED` log line in
`enbmod.log` confirms the hook is live in-game.

### UI overhaul -- the Freya cockpit-glass HUD (Phase AS)
A replacement HUD that ports the `Earth & Beyond HUD.html` design. Three scripts share
`scripts/freya_hud.lua` (palette, the `H.glass()` panel = gradient body + gloss + hairline
border + cyan corner ticks, outlined text, and visibility gating):

- **`freya_ui.lua`** -- the **PlayerCard** (name + LV header, three vitals as a track +
  vertical-gradient fill with cur/max printed inside + percent) and a **glass hotbar** of twelve
  rounded slots (`1 2 3 4 5 6 7 8 9 0 - =`, VKs `0x31..0x39,0x30,0xBD,0xBB`): a click taps the
  matching key (`enb.tap`) and a physical keypress lights the slot (key messages observed but
  **not** swallowed -- the game still needs the bind). Mouse over the cards is swallowed to
  `WM_NULL`.
- **`xp_overlay.lua`** -- the bottom-left **DiscCard**: three rows (C/E/T colored letter + xp bar
  + "LV n" badge), fed by `enb.self()` (gray skeleton until calibrated).

All shapes are procedural `rrect_grad`/`rrect`/`line` -- **no binary assets** (repo rule). Geometry
derives from `enb.screen()` (bottom-anchored at any resolution).

**Visibility** is gated on `enb.state()` via `freya_hud.vis()`: **space** = cards + hotbar,
**station** = cards only (hotbar hidden, owner ask), **login/charsel/load** = nothing. `enb.cursor()`
draws our pointer on top of it all. Both need real-client calibration/confirmation
(CV-AS-STATE / CV-AS-CURSOR in `plans/29`).

**Hiding the native widgets we replace** (AS-6): the glass is translucent, so the native stat/xp/
skill widgets bleed through. `enb.patch_ret(addr[,pop])` neuters a widget's per-frame PAINT routine
with an early `ret` (init.lua has a commented, **OFF-by-default** HIDE block). The paint targets are
pinned: `enb.addr.VitalsPaint` (0x005dcae0) and `enb.addr.XpPaint` (0x0058cf60) -- both `void
__fastcall`, pop 0, pure paint (they only check each gadget's visible flag and call the paint
primitive), so an early ret hides them without touching state. The `*Bars`/`EnergyBar`/`SkillButton`
entries are the constructors/updater and must NOT be patched. The skill buttons have no standalone
pure-paint entry, so they need a runtime per-gadget visible-flag write instead of a static patch.
Ships disabled because "does an early ret break gameplay" is only confirmable on the real client
(CV-AS-HIDE-VITALS/-XP/-SKILL). Until enabled, the native widgets remain visible behind the glass --
honest status, not hidden yet.

**Fail-open is load-bearing:** `on_input` runs Lua via `lua_pcall`, and any error returns *false*
(do not swallow), so a script bug can never wedge the user's keyboard/mouse.

### Actions
`enb.tap/key/char` post Win32 messages to the game window (found by enumerating this process's
visible top-level windows) -- the keybind system turns those into ability activations, so this is
a real trigger path today, just focus/remap-fragile. `enb.call`/`call_cdecl` are a verified-correct
asm thunk (this→ECX, args pushed right-to-left, ESP saved/restored so a wrong cleanup convention
won't unbalance the stack -- tested under Wine). The thunk is correct; **the arguments are on you** --
recover the real `this`/args for a target (e.g. `SkillLifecycle` @ `0x0060f1a0`) at runtime before
calling, and test on a throwaway character.

The tick is driven by hooking **`PeekMessageA`** -- a Win32 API with a known, stable signature, so
we don't have to correctly reverse a game function just to get a heartbeat. Event hooks patch the
game's own `__thiscall` functions (`SkillLifecycle` @ `0x0060f1a0`, `ChatChannel` @ `0x0065bfd0`);
they're **opt-in** because the arg layout is unverified. Each detour is a **naked tail-jump
trampoline**: it preserves all registers, calls a cdecl notify helper with `(this, first-stack-arg)`
for observation, then restores everything and `jmp`s into MinHook's trampoline -- so the real game
function receives its **full, unmodified argument list** (this in ECX + every stack dword) and
returns to the original caller. We observe `this` and the first argument; we never alter the call.
(An earlier `__fastcall(ecx,edx)` re-call dropped the stack args and fed the game garbage -- that was
verified broken and replaced.)

## Testing the mods headless -- `make test`

`tests/` is a headless test suite for the Lua mods: the scripts under `scripts/` run unmodified
against **`tests/mock_enb.lua`**, a pure-Lua mock of the C++ `enb` host API, on a *native* Linux
build of the vendored Lua (built automatically into `build/tests/`). No client, no WINE, no D3D8.
This is how mods get debugged and verified programmatically -- including visually:

- **Specs** (`tests/spec/*_spec.lua`) assert layout geometry, calibrated/uncalibrated rendering,
  click->tap->flash, key lighting, and the swallow contract. `tests/spec/mock_contract_spec.lua`
  pins the mock's mirror of the C++ semantics (msg_class/mask filtering from `hooks.cpp`,
  run_input fail-open from `lua_api.cpp`) so mock/DLL drift breaks the suite loudly.
- **Screenshots**: `tests/spec/screenshots_spec.lua` dumps representative full-HUD frames, and
  `tests/render_frame.py` (Pillow) rasterizes them to `build/tests/shots/*.png` approximating
  `overlay.cpp`'s draw semantics (vertical gradients, rounded corners, alpha over a dark
  backdrop) -- so the UI can be *looked at* without launching the game.

**Want to PLAY with it?** From the repo root run **`just mock-ui`**: it opens an interactive
in-browser previewer. The real `scripts/*.lua` run inside a native Lua host
(`tests/interactive_host.lua`), a Python stdlib server (`tests/preview_server.py`) bridges a browser
`<canvas>` to them, and the **actual game screen** (`tests/enb-mod-bg.png`, 1280x960) is the
background so HUD positioning can be checked against the real client view. Mouse move/click and
keyboard events drive the scripts' `on_input`/`on_tick` handlers live -- click an action-bar button
or press `1`-`9`/`0`/`-`/`=` to fire its keybind; the corner HUD shows mouse coords, the swallow
state, and the live tap count. Toggle calibrated stats, the background, and the resolution from the
top-right controls. `PORT=N` changes the port; `NO_OPEN=1` skips auto-opening the browser; Ctrl-C
stops the server.

The background is `tests/enb-mod-bg.png`, a 1280x960 game-client screenshot, so the HUD is
positioned against the real view. Swap in your own screenshot at that path to check a different
scene; without any file there the previewer falls back to a dark backdrop.

**Just want a quick snapshot?** **`just mock-ui-shots`** runs the suite and stitches the four
scenarios into a labeled 2x2 contact sheet (`build/tests/shots/_contact.png`), opened in your image
viewer -- a static visual-diff for a script tweak. `NO_OPEN=1` writes the files without opening.

Caveats, honestly: the mock's text metric is the scripts' own 7px/char fallback (the real Tahoma
atlas is variable-width), and the Python rasterizer is an approximation of the D3D8 path, not the
D3D8 path. The suite verifies *logic and layout*; the in-game CV pass still owns final pixel truth.

## Calibration -- the part you finish at runtime

`enb.self()`/`enb.target()` read from the **`Offsets`** table in `game.h`. Every field defaults to
`-1`/`0` = *unknown*, so out of the box `enb.self()` returns `{ base = 0 }` and nothing else. This
is deliberate: the static map gives stable function addresses and AuxData *keys*, not C++ field
layouts. You supply the offsets:

1. Run the game. Use `scripts/calib.lua` from `init.lua`:
   - `calib.find_ptr_to_value(currentHull, hullOffsetGuess)` -- scan `.data` for the pointer that
     leads to your ship object.
   - `calib.dump(base, 64)` -- hex-dump the object; eyeball which `+0x..` holds hull/shield/energy
     (look for the float/int that matches the HUD).
   - `calib.watch(addr,"hull")` + `calib.pump_watches()` -- confirm a field by watching it change.
   (A memory scanner/debugger works too; same goal -- find `player_ptr_addr` and the field offsets.)
2. Put the numbers in `enb.calibrate{ player_ptr_addr=…, hull=…, shield=…, … }` in `init.lua`.
3. Save -- hot-reload applies it without restarting the game.

The cleaner long-term path (see `INJECTION-MAP.md §1`) is to resolve the generic **`AuxData::Get`**
accessor and read every stat by key instead of by raw offset. That replaces the offsets table
entirely, but requires recovering the accessor signature first.

## Security model -- read this

This is a **scripting runtime with full process privileges**, not a sandbox. A loaded `init.lua`
can call `enb.mem.write_u32/write_f32` (arbitrary writes into client.exe) and `enb.call`/
`enb.call_cdecl` (call any address with any arguments). That is **arbitrary code execution in the
game process** by design -- it's what makes modding possible. Consequences:

- **Only run `scripts/` you wrote or fully trust.** A malicious init.lua is equivalent to running
  an untrusted `.exe`. There is no permission boundary between Lua and the game/OS.
- The fault guard (`mem.h` VEH) makes *bad reads* survivable; it does **not** make *writes* or
  *calls* safe. A wrong `enb.call` argument list or a write to the wrong address can corrupt or
  crash the game -- test on a throwaway character.
- Injection itself (`inject.exe`) needs the same privileges as the game and trips anti-cheat on
  servers that have it. E&B's emulator (Net-7) doesn't, but don't assume that elsewhere.

## Fixes applied (2026-06-10)

- **Event-hook arg forwarding (was a crash bug).** The `__thiscall` event detours used a
  `__fastcall(ecx,edx)` wrapper that re-called `real(ecx,edx)`, silently dropping every stack
  argument → the real game function ran on garbage. Replaced with naked tail-jump trampolines that
  forward the full register+stack state (see Actions, above). Verified correct in isolation.
- **Hot-reload callback leak.** Each reload re-ran init.lua without releasing the previous run's
  `on_tick` refs, so handlers (and their work) accumulated every save. `reset_callbacks()` now
  unrefs them first.
- **Overlay hooked the wrong API (never drew).** The original overlay hooked
  `IDirectDrawSurface::Flip`/`Blt`, but the game presents through Direct3D 8 -- a full session
  log showed the hooks installed and never firing. Rewritten as the D3D8 Present hook above.
- **Unsafe unload.** `DllMain(DETACH)` ran `MH_Uninitialize` even on process termination (under the
  loader lock, with other threads dead) -- now it only unhooks on a real `FreeLibrary`.
- **String reads.** `mem::cstr/wstr` did a `VirtualQuery` *per byte* and read outside the VEH guard;
  now one region scan + reads under the guard.

Still **unverified at runtime**: in-game overlay rendering, the event detours against the live
game, and any struct offset (all calibration is yours).

## Status -- what's real vs. pending

**Real / working (verified):** build, injection, Lua VM, per-frame tick, hot-reload,
VEH-guarded crash-safe memory primitives (verified under Wine), the full `enb.*` surface, the
offsets/calibration plumbing, opt-in event hooks, the D3D8 Present-hook overlay (text/rect/
line/image via stb_image + textured quads), input-synthesis actions, and the `__thiscall` call
thunk (verified correct + stack-stable under Wine).

**Pending / needs runtime work:**
- All struct offsets (`Offsets`) are placeholders -- `enb.self()` is empty until you calibrate.
- Event-hook arg layouts are unknown; only `this` is trustworthy today.
- **Overlay (D3D8 rewrite) is code-complete but unverified in-game** -- watch `enbmod.log` for
  the one-shot `overlay: Present hook FIRED` line on first launch.
- **Direct ability-call args are unrecovered** -- the call *mechanism* works; you supply verified
  `this`/args per target. Input-synthesis (`enb.tap`) is the works-today trigger path.
