# HOOKING-ANALYSIS.md -- Where to hook Earth & Beyond for UI/Lua modding

> Reference for the installed E&B client
> (`~/.wine-enb/drive_c/Program Files/EA GAMES/Earth & Beyond`).
> Companion to `INJECTION-MAP.md` and `LUA-MODDING-DESIGN.md`.

## 0. TL;DR

E&B has **two completely separate UI systems**, and that dictates the whole modding strategy:

1. **Embedded Internet Explorer (Trident/MSHTML)** -- used for menu/panel/web screens
   (login, news, EULA, some station panels). These are **real HTML pages** driven by a
   `window.external` JS<->native bridge. *This is the easy, high-leverage surface.* You can
   mod these with HTML/CSS/JS today, no binary patching, and you can add Lua by bridging
   it to the JS engine or to the native `external` IDispatch object.

2. **A native C++ "Gadget" widget system** -- the actual in-game HUD, chat, inventory,
   targeting, etc. This is compiled C++ with vtables, **no scripting layer at all**. To
   mod this you must hook native functions (DLL injection + detours). Harder, but this is
   where the real gameplay UI lives.

There is **no existing scripting engine** in the client (no Lua/Python/Squirrel/AngelScript).
Anything you add is new.

The client has **zero symbols**. RTTI yields real C++ class names (`GadgetClass`,
`ClientObjectClass`, the 87 `*PacketClass` handlers), but mapping those names to specific
function addresses is per-target work. The catalog in this file is a *map to locate* a hook;
confirm each target at runtime (hook + log) before relying on it.

---

## 1. Binary inventory

| File | Source binary | Funcs | Role |
|---|---|---:|---|
| `client.c` (42 MB) | `release/client.exe` (8.6 MB) | **42,636** | **The entire game** |
| `BrowserEngine.c` | `release/BrowserEngine.dll` | 1,172 | COM wrapper around IE WebBrowser control |
| `authlogin.c` | `release/authlogin.dll` | 445 | Auth/login |
| `FreyaPosFeed.c` | `release/FreyaPosFeed.dll` | 54 | Positional telemetry feed |
| `enb_launcher.c` | `e&b.exe` (45 KB) | 136 | Launcher shim |
| `dimple.c` | `dimple.dll` | 487 | misc |
| `mrbupd.c` | `mrbupd.dll` | 464 | MRB archive updater |
| `patchw32.c` | `patchw32.dll` | 541 | Patcher runtime |
| `Mss32.c` | `release/Mss32.dll` | -- | Miles Sound System (3rd-party) |
| `binkw32.c` | `release/binkw32.dll` | 423 | Bink video (3rd-party) |

The config/installer/character-creator tools (E&BConfig.exe, net7config.exe, VIDTEST.EXE,
CnSC.exe, gpatch/patch) are not the game loop -- low modding value.

---

## 2. Architecture map (imports + RTTI + strings)

`client.exe` DLL imports (the dependency surface you can hijack):
```
authlogin.dll  mss32.dll  binkw32.dll        <- game/middleware, proxy candidates
DDRAW.dll                                      <- DirectDraw: the 3D/2D world + native Gadget UI
urlmon.dll  WININET.dll  ole32  OLEAUT32       <- the embedded IE browser stack
ws2_32.dll                                     <- networking (the 87 packet handlers)
COMCTL32  GDI32  USER32  SHELL32  WINMM  AVIFIL32  ADVAPI32  KERNEL32
```

`BrowserEngine.dll` is a **COM in-proc server** -- it exports only the 4 COM registration
stubs (`DllGetClassObject`, `DllRegisterServer`, etc.), not named game functions:
- ProgID: `BrowserEngine.FEBrowserEngine2.1`
- CLSID: `{2B2CC8B0-2DC0-48c6-B6FD-C07820A6477E}` ("FEBrowserEngine2 Class")
- Interfaces: `IFEBrowserEngine2`, `IFEBrowserInstance2`
- Methods seen in strings: `CreateBrowserWWW`, `DestroyBrowser`, `CloseBrowser`, `Navigated`
- Imports `urlmon` + `WININET` + `ole32` → it **embeds the IE WebBrowser ActiveX control**.

So "FE" = front-end. The front-end browser screens are HTML rendered by Trident.

---

## 3. HOOK SURFACE A -- the embedded IE browser + `window.external` (recommended first target)

### Evidence
`client.exe` contains inline HTML built at runtime, e.g. the string:
```html
about:<html><body onload="window.external.EnableNavigation=false;" onselectstart="return false;" ...>
```
That `window.external.X` means client.exe (or BrowserEngine) implements an **`IDispatch`
"external" object** that JS pages call into. This is the classic IE-host pattern
(`IDocHostUIHandler::GetExternal`). Every HTML screen can call native game functions through
it, and native code pushes data into pages via the DOM/`IHTMLDocument2`.

### Browser creation call sites
`CoCreateInstance` for the browser/COM objects appears at:
- `client.c:406421`  → `CoCreateInstance(&DAT_00ba0f90, 0, 7, &DAT_00ba0fa8, ...)`
- `client.c:1123074` → same CLSID/IID pair
- `client.c:1328476` → `CoCreateInstance(&DAT_00bb9c08, ...)` (different CLSID)

`DAT_00ba0f90` / `DAT_00bb9c08` are the GUID constants -- read the 16 bytes to confirm which is
`FEBrowserEngine2` (2B2CC8B0…) vs the raw `WebBrowser` control. That function is where the host
wires up the browser and (nearby) the `external` handler -- the anchor for everything in this section.

### How to mod here (ranked, easiest first)
1. **Pure HTML/CSS/JS reskin** -- no binary patching. Find the HTML the browser navigates to.
   Only 3 loose HTML files exist on disk (`enbwebsite.html`, `news/index.html`,
   `Data/client/text/credits.htm`); the rest are packed in **`.mrb` archives**
   (`Data/index.mrb`, Castanet/MRB format -- `mrbupd.dll` is the updater). The `.mrb` archives
   hold the other front-end pages. The `window.external.*` calls in those pages are the
   native API the UI already uses.
2. **Add Lua behind JS** -- host a Lua VM in an injected DLL, expose it to pages either by
   (a) adding methods to the `external` IDispatch (intercept `GetIDsOfNames`/`Invoke`), or
   (b) injecting a `<script>` that posts to a localhost endpoint your DLL serves. (a) is
   cleaner, (b) is faster to prototype.
3. **Swap the engine** -- replace the ancient Trident control with a modern WebView
   (CEF/WebView2) by reimplementing the `IFEBrowserEngine2` COM contract in a drop-in
   `BrowserEngine.dll`. Big effort, but you fully own the front-end and can ship Lua/modern JS.

This surface is by far the best ROI for "mod the UI + add a scripting language."

---

## 4. HOOK SURFACE B -- the native "Gadget" widget system (in-game HUD)

The actual in-game UI is native C++, not HTML. RTTI class names from `client.exe`:
```
GadgetClass                 GadgetCollectionInterface   GadgetEvent
GadgetSlider / Event        ToggleButtonDynamicWindow   ToggleButtonStateEvent
MovableUIBackGadget         MovableUIBackGadgetMoved     FlashingGadgetEvent
DestroyGadgetEvent          GadgetGroupToFrontEvent      ChatWindowInterface(/Event)
```
~111 functions in `client.c` reference "Gadget". This is an event-driven widget
framework: a base `GadgetClass` (vtable: paint/hit-test/handle-event), an event hierarchy
(`GadgetEvent` subclasses), and a `GadgetCollectionInterface` container.

### How to hook here
- The high-value hook is **`GadgetClass`'s event-dispatch vtable slot** (the virtual that
  receives `GadgetEvent`s). Detour it and you see/inject every UI interaction.
- `GadgetCollectionInterface` is where gadgets are registered -- hook it to enumerate live
  widgets or add your own.
- To *render* custom native UI you'd subclass `GadgetClass` (replicate its vtable). Painful
  without the struct layout; recover the vtable + member offsets at runtime first.
- Realistic near-term play: **bridge the Gadget system to Lua read/write** (read targeting,
  inventory, chat state; trigger existing actions) rather than rendering new native widgets.
  Render new UI in the *browser* layer (Surface A) and drive game actions via Gadget/packet
  hooks.

This needs per-target work; the ~111 Gadget functions are anchored by their RTTI/vtable refs.

---

## 5. HOOK SURFACE C -- the 87 network packet handlers

`client.exe` strings expose **87 distinct `*PacketClass::Process_Packet()` handlers**, e.g.
`AvatarDescPacketClass`, `AdvancedPositionalUpdatePacketClass`, `ActivateRenderStatePacketClass`,
`AuxDataPlayerVarPacketClass`, … (these are debug strings each handler logs on entry, so each
maps to exactly one handler function). This is the server→client message dispatch -- the
authoritative source of game state (your mods will want to read it).

- Each `Process_Packet` string is referenced by exactly one function → the data xref from the
  string lands on the handler.
- Hooking these gives you a clean, well-typed event stream ("avatar updated", "object
  spawned", "chat received") to feed a Lua event API -- much nicer than scraping the Gadget UI.
- This dovetails with existing work: the proxy already covers the wire format / crypto.
  Surface C is the *client-side* consumer of those same packets.

---

## 6. Injection mechanics -- how to get your code in

The client already tolerates injected/proxied DLLs (Net-7 does exactly this). Candidates:

- **DLL proxy / hijack** of an imported DLL. `authlogin.dll` (game-authored, 445 funcs) and
  the middleware `mss32.dll` / `binkw32.dll` are all loaded by `client.exe`. A proxy DLL that
  forwards real exports and runs your loader in `DllMain` is the standard E&B mod entry. Net-7
  already wraps the auth/login path -- check `release/authlogin.dll` vs vanilla before reusing it.
- **Detours/inline hooks** from inside the injected DLL onto the targets in §3–§5.
  32-bit, so use a 32-bit hooking lib (MinHook x86, or hand-rolled 5-byte JMP trampolines).
- **Launcher route** -- `e&b.exe`/Net-7 `LaunchNet7` already control process start; you can
  inject at create-suspended.

---

## 7. Recommended Lua integration plan (concrete, ranked)

1. **Phase 0 -- reskin proof:** edit a front-end HTML page, confirm it renders. Establishes
   the HTML pipeline with zero binary risk.
2. **Phase 1 -- Lua in the browser layer:** inject a DLL, embed Lua (5.4 or LuaJIT-x86), expose
   it to front-end pages via the `external` IDispatch (hook the host's `GetExternal`/`Invoke`,
   anchored at the §3 CoCreateInstance site). Now HTML buttons can call Lua. **Lowest-risk way
   to get a real scripting language modding the UI.**
3. **Phase 2 -- game-state events to Lua:** detour a handful of §5 `Process_Packet` handlers,
   marshal their fields into Lua callbacks (`on_chat`, `on_target`, `on_inventory`). Read-only
   first.
4. **Phase 3 -- drive the game from Lua:** hook §4 Gadget dispatch / action functions so Lua
   can trigger existing in-game actions. This is where it stops being a skin and becomes a mod
   API.

Render new UI in HTML (Phase 1), read/drive game state via native hooks (Phases 2–3). Don't
try to render native Gadgets from scratch -- wrong cost/benefit.

---

## 8. Known limits of this map

- **No struct layouts.** RTTI gives class *names*, not field offsets. Every native hook
  needs the relevant struct/vtable reconstructed at runtime first.
- **The `external` bridge method table isn't enumerated.** Its existence is confirmed
  (`window.external.EnableNavigation`); the full method list lives in the packed HTML pages and
  in the host's `IDispatch::GetIDsOfNames`.
- **Trident is ancient and quirky.** The embedded IE runs in a low compat mode; modern JS/CSS
  won't work without a `X-UA-Compatible`/engine swap. Plan for IE7-era HTML or replace the
  engine (§3.3).
- **No symbols.** 42,636 functions. The catalog in §3–§5 is your map; expect per-target
  runtime confirmation.
