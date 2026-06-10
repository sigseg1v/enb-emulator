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

Alternative loaders (see `../LUA-MODDING-DESIGN.md §2`): `ddraw.dll` proxy (also puts you in the
render path for overlays) with `WINEDLLOVERRIDES=ddraw=n,b`; or piggyback the Net-7 launcher.
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
| `enb.self()` | `{ base, hull, shield, energy, combat_lvl, x,y,z, … }`; uncalibrated fields absent, `base==0` if no `player_ptr_addr` |
| `enb.target()` | `{ base, name, distance, … }` or `nil` |
| `enb.on_tick(fn)` | fn runs every pump iteration (game thread) |
| `enb.on_skill(fn)` / `enb.on_chat(fn)` | event-hook callbacks (need `enable_event_hooks`) |
| `enb.enable_event_hooks()` | install the game-fn hooks (off by default) |
| `enb.draw.text(x,y,s[,rgb])` | overlay text (rebuilt each tick) |
| `enb.draw.rect(x,y,w,h[,rgb[,filled]])` | overlay rectangle |
| `enb.draw.line(x0,y0,x1,y1[,rgb])` | overlay line |
| `enb.draw.image(path,x,y[,w,h[,alpha]])` | blit PNG/TGA/BMP/JPG (stb_image, cached, alpha-blended) |
| `enb.tap(vk)` / `enb.key(vk[,down])` | post key to game window (fires bound abilities) |
| `enb.char(cp)` | post WM_CHAR |
| `enb.hwnd()` | game window handle |
| `enb.call(addr,this,…)` | call a game member fn as `__thiscall`, returns EAX |
| `enb.call_cdecl(addr,…)` | call a free fn (cdecl/stdcall), returns EAX |

### Overlay
Grabs the `IDirectDrawSurface` vtable from a throwaway surface, hooks **Flip** (vtable index 11)
with MinHook, and draws a per-frame display list onto the surface's GDI DC before present. The
list is immediate-mode: Lua rebuilds it every `on_tick` (the tick swaps staging→render under a
lock so the Flip thread always sees a complete frame). Images load via `stb_image` and blit with
`AlphaBlend` (premultiplied). **Unverified in-game** -- windowed-mode present often uses `Blt`
(vtable 5) instead of `Flip`; if nothing draws, also hook `Blt`. Wine's ddraw vtable layout is
the standard one, so index 11 should hold.

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
- **Overlay HDC leak.** `load_image`'s DIBSection-failure path leaked a memory DC every frame and
  never cached the failure; now it releases the DC and caches the negative result.
- **Unsafe unload.** `DllMain(DETACH)` ran `MH_Uninitialize` even on process termination (under the
  loader lock, with other threads dead) -- now it only unhooks on a real `FreeLibrary`.
- **String reads.** `mem::cstr/wstr` did a `VirtualQuery` *per byte* and read outside the VEH guard;
  now one region scan + reads under the guard.

Still **unverified at runtime**: in-game overlay rendering, the event detours against the live
game, and any struct offset (all calibration is yours).

## Status -- what's real vs. pending

**Real / working (verified):** build, injection, Lua VM, per-frame tick, hot-reload,
VEH-guarded crash-safe memory primitives (verified under Wine), the full `enb.*` surface, the
offsets/calibration plumbing, opt-in event hooks, the DirectDraw Flip-hook overlay (text/rect/
line/image via stb_image+AlphaBlend), input-synthesis actions, and the `__thiscall` call thunk
(verified correct + stack-stable under Wine).

**Pending / needs runtime work:**
- All struct offsets (`Offsets`) are placeholders -- `enb.self()` is empty until you calibrate.
- Event-hook arg layouts are unknown; only `this` is trustworthy today.
- **Overlay is code-complete but unverified in-game** -- may need the `Blt` hook for windowed mode.
- **Direct ability-call args are unrecovered** -- the call *mechanism* works; you supply verified
  `this`/args per target. Input-synthesis (`enb.tap`) is the works-today trigger path.
