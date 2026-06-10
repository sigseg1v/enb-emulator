# LUA-MODDING-DESIGN.md -- external Lua runtime DLL for Earth & Beyond

> How to build an injected Lua mod runtime that hooks game functions, draws overlay widgets,
> loads images at runtime, reads game state (HP/shields/reactor/levels/target/chat/navs/binds/
> abilities), and triggers ability use. See `INJECTION-MAP.md` for the concrete addresses
> referenced here.

## TL;DR -- is it possible?

**Yes, all of it is technically possible**, and the target is unusually friendly:
- 32-bit PE, **fixed base `0x400000`, no ASLR** → static addresses work directly.
- A clean property model (**AuxData**) means most reads route through one accessor.
- Net-7 already injects/proxies DLLs into this exact client, so the loader pattern is proven.

The catch: **reading** state is straightforward (hook + cache). **Calling** game functions
(e.g. "trigger ability") is harder -- you must recover each function's `this`-pointer and argument
layout and validate at runtime. Budget accordingly: reads first, actions later.

---

## 1. Architecture

```
 ┌─────────────────────────── client.exe (32-bit, base 0x400000) ───────────────────────────┐
 │  game thread ──> Win32 msg pump (0x443710) ──> game tick ──> DirectDraw present (Flip)    │
 │       ▲ hooks                    ▲ per-frame callback             ▲ overlay draw hook       │
 │       │                          │                                │                         │
 │  ┌────┴───────────── enbmod.dll (your injected runtime) ──────────┴───────────┐            │
 │  │  MinHook (x86)  │  Lua 5.4 VM (x86)  │  overlay renderer  │  image loader   │            │
 │  │  - hook AuxData getter, packet handlers, UI updaters                        │            │
 │  │  - expose enb.* Lua API: read state, draw widgets, bind keys, call actions  │            │
 │  └─────────────────────────────────────────────────────────────────────────────┘          │
 └────────────────────────────────────────────────────────────────────────────────────────────┘
```

One in-process DLL. Inside it: a 32-bit Lua VM, the MinHook trampolines, and an overlay drawer.
Lua scripts live on disk (hot-reloadable); the DLL exposes an `enb` table to them.

---

## 2. Getting your DLL loaded (injection)

Four options, easiest → most robust:

1. **CreateRemoteThread injector** (recommended to start). A tiny standalone launcher starts
   `client.exe` suspended (or finds it), `VirtualAllocEx` + `WriteProcessMemory` the path to
   `enbmod.dll`, `CreateRemoteThread`→`LoadLibraryA`. Zero game-file changes, easy to iterate.
2. **DLL proxy / hijack of `ddraw.dll`.** The client imports `DDRAW.dll`; drop a proxy `ddraw.dll`
   in `release/` that forwards the real exports and `LoadLibrary`s your runtime in `DllMain`.
   Bonus: you're already sitting in the render stack, ideal for overlays. (Wine: forward to the
   system `ddraw`.) Other proxy candidates the client loads: `authlogin.dll`, `mss32.dll`,
   `binkw32.dll` -- but `authlogin` is already Net-7-modified, so don't fight it.
3. **Launcher cooperation.** Net-7's `LaunchNet7`/`e&b.exe` already control startup; piggyback there.
4. **Hook a known init function** to call `lua_init` once the game is up (avoid doing real work in
   `DllMain` -- loader-lock).

You run under **Wine**. All of the above work under Wine; the proxy-DLL route needs a Wine
`WINEDLLOVERRIDES=ddraw=n,b` (native-then-builtin) entry so your proxy is preferred.

---

## 3. Hooking game functions

- 32-bit inline hooks: **MinHook (x86 build)** or hand-rolled 5-byte `E9` JMP trampolines.
- Addresses are static (`INJECTION-MAP.md §0`) -- e.g. to watch ability use, hook
  `0x0060f1a0` (`Skill Activated/Deactivated`); to read vitals, hook the stat-block reader at
  `0x006f66e0`.
- Calling convention is mostly `__thiscall` (object `this` in `ECX`). In MinHook, declare the
  trampoline `__fastcall` with a dummy `EDX` arg: `ret __fastcall hk(void* ecx, void* edx, …)`.
- **Don't hook everything.** Pick a few high-value chokepoints: the AuxData getter, the per-frame
  HUD updaters, the packet handlers. One AuxData hook ≈ all stats.

---

## 4. Reading game state (your list)

Two complementary strategies:

**A. Passive (hook + cache) -- do this first.** Hook the functions that the game *already* calls
every frame to populate the HUD, and copy the values out as they pass:

| Want | Hook (from `INJECTION-MAP.md §3`) |
|---|---|
| HP/hull | `0x006fe1e0` (`HullPoints`, ship damage) |
| Shields | stat block `0x006f66e0` (`ShieldPercent/MaxShieldPower`) |
| Reactor/energy | `0x005dc4a0` (`EnergyPercent/MaxEnergyPower`) |
| Combat/Trade/Explore level + % | `0x0058c450` (XP bars), `0x00548d60` (level text) |
| Target name + distance | `0x0065e5e0` (`"Target: %s at"`, `%.2f` distance) |
| Chat lines | `0x0065e3e0` (ChatGadget), `0x0065bfd0` (channel routing) |
| Navs | `0x007b7ef0`/`0x007b8510` (nav list) |
| Keybinds / activation slots | `0x0071a960` (KeyDefinitions), `0x004081fc` (slot categories) |
| Ability slots | `0x006a4b40` (`RPGInfo SkillPowerupAbilityNumber`) |
| Position | `AdvancedPositionalUpdatePacket` handler (`§5`) + `px` physics module |

**B. Active (call the getter yourself).** Recover the generic **`AuxData::Get(key)`** accessor
(`INJECTION-MAP.md §1`) and the "current player object" global (likely in `.data`, `0xB6C000+`).
Then from Lua you can pull *any* key on demand: `enb.aux(playerObj, "ShieldPercent")`. This is the
clean long-term API -- one primitive, every stat.

Either way, expose results to Lua as a table refreshed each frame:
```lua
enb.on_frame(function()
  local s = enb.self()       -- {hull=, shield=, energy=, combat_lvl=, ...}
  local t = enb.target()     -- {name=, distance=, ...} or nil
end)
```

---

## 5. Drawing widgets / overlay

Three routes, pick per need:

1. **DirectDraw overlay (best for custom HUD).** The present path is DirectDraw via COM vtables.
   Hook `IDirectDrawSurface::Flip` (and/or `Blt`) by grabbing the surface vtable pointer at
   runtime (create a throwaway surface, read vtable[Flip], or read it off the game's primary
   surface) and swapping the slot. In your hook, draw onto the primary surface before Flip:
   GDI text via `GetDC`/`TextOut`, or `Blt` your own loaded sprites. This gives you arbitrary
   widgets independent of the game UI.
2. **Reuse the native Gadget system** (`gd*`/`gadge*`, `INJECTION-MAP.md §2`). Heaviest path --
   you'd construct `GadgetClass` instances (vtable + struct layout must be reconstructed). Native
   look, but high cost. Not recommended for v1.
3. **Embedded IE browser** (`§3` browser). The client renders HTML from `data\client\htmldocs\`.
   You can host an HTML/JS overlay and drive it from Lua via the `window.external` bridge. Good
   for menu-style panels, awkward for fast HUD.

**Recommendation:** DirectDraw-Flip overlay for the live HUD; browser for config menus.

---

## 6. Loading images externally at runtime

- In the DirectDraw-overlay path you own the blitter, so just load PNG/TGA/DDS from disk with your
  own loader (e.g. stb_image, 32-bit) into an offscreen `IDirectDrawSurface`, then `Blt` it in the
  Flip hook. No dependency on the game's asset pipeline.
- If you want the game to load them *natively*, the loose-file search paths are baked in
  (`..\data\client\art\`, `htmldocs\`, etc.), and the mixfile loader falls back to loose files, so
  dropping art there is an option for game-rendered assets.

---

## 7. Triggering ability use / actions (the hard part)

Three approaches, increasing fidelity / difficulty:

1. **Synthesize input.** The keybind system (`0x0071a960`, slots `Targeting/Navigation/Camera/
   Activation`) maps keys → actions. Easiest hack: post the key for the ability slot to the game
   window. Fragile (focus, remaps) but quick.
2. **Invoke the Gadget click handler.** The hotbar abilities are gadget buttons
   (`0x00662dc0`/`0x00663df0`). Call the button's activation handler with its object pointer --
   replays exactly what a click does. Needs the gadget instance pointer (enumerate via the gadget
   collection) and the handler signature.
3. **Call the skill-activation function directly** (cleanest, hardest). Around `0x0060f1a0`
   (`Skill Activated`) is the activation path. Recover its `this` + args (skill id / slot, target)
   and `__thiscall` it from your DLL. **This requires runtime work**: set a breakpoint, watch ECX +
   stack when you click an ability in-game, then replicate. The args won't come from static
   analysis alone.

For all three: the server is authoritative, so a "use ability" ultimately emits a packet. Cross-
check against the outbound packet you see in the proxy when you use an ability -- that confirms
you triggered the real path and didn't just poke UI.

---

## 8. Suggested build order

1. **Injector + Lua VM + log.** Get `enbmod.dll` loaded, Lua running, printing to a file. (proves
   injection under Wine)
2. **Frame callback.** Hook the message pump (`0x443710`) or Flip; call `enb.on_frame` each tick.
3. **Reads.** Hook the §4A updaters; expose `enb.self()/enb.target()/enb.chat`. This alone is a
   useful HMI/overlay mod.
4. **Overlay.** DirectDraw Flip hook + image blitter (§5/§6); draw the values from step 3.
5. **AuxData primitive.** Recover `AuxData::Get`; replace ad-hoc hooks with `enb.aux(obj,key)`.
6. **Actions.** Keybind synth → gadget-click → direct call, in that order, validating each against
   outbound packets.

---

## 9. Risk list

- **Struct offsets are unconfirmed.** Every "read X" assumes a field layout you must verify at
  runtime. Addresses are stable; layouts are hypotheses.
- **Calling native funcs** can corrupt state if the convention/args are wrong -- test in a throwaway
  character/area.
- **Wine specifics.** DirectDraw vtable hooking and DLL-proxy overrides behave slightly differently
  under Wine than Windows; expect some fiddling with `WINEDLLOVERRIDES` and surface formats.
- **Net-7 already patches some DLLs** (`authlogin`) -- don't proxy those; prefer `ddraw` or a
  standalone injector to avoid colliding with the emulator.
- **No existing scripting engine** in the client to lean on. You're building the runtime, not
  extending one.

See `INJECTION-MAP.md` for addresses and `HOOKING-ANALYSIS.md` for the UI-architecture overview.
