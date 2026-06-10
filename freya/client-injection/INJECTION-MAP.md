# INJECTION-MAP.md -- client.exe function & data map for hooking

> Static map of `client.exe` (42,636 functions) plus the recovered `__FILE__` source tree.
> Machine-readable companions: `client.funcmap.tsv` (per-function: addr, module, srcfile,
> size, callees, APIs, strings, signature), `client.modules.tsv`, `client.srcfiles.tsv`,
> `client.apihist.tsv`.

## 0. Ground truth for injection

| Fact | Value | Why it matters |
|---|---|---|
| Architecture | **32-bit x86 PE** | Your injected DLL + Lua **must be 32-bit** (Lua 5.4 x86 or LuaJIT x86). |
| Image base | **`0x00400000`** (fixed) | **No ASLR** (no `DYNAMIC_BASE` flag). Every address below is stable every run -- hook them directly, no rebase math. |
| `.text` (code) | `0x00401000` – `0x00AE68DD` | All function addresses here. |
| `.rdata` | `0x00AE7000` – … | String/vtable/RTTI constants (e.g. browser CLSID `DAT_00ba0f90`). |
| `.data` | `0x00B6C000` – … | Mutable globals (candidate "current player" / state pointers). |
| Calling conv | mostly `__thiscall` / `__fastcall` (this in ECX) | C++ member fns -- first arg is the object `this`. Matters when you call them. |

> **Confidence.** These addresses are **string-anchored candidates**: a function
> is listed under "shields" because it references the `ShieldPercent` AuxData key or a shield
> UI string. That's a strong lead, not proof it's *the* getter. Validate each at runtime (hook +
> log, or a pointer scan) before trusting offsets. Struct field offsets are **not** recovered
> here -- you reconstruct those per target at runtime.

---

## 1. The key abstraction: **AuxData** (read this first)

The client stores per-object gameplay state in a string-keyed property bag called **AuxData**
(`shared\auxdata\auxdata.h`). Nearly everything you want to read is an AuxData **key**, not a
hard struct field. Keys seen in the client:

```
ShieldPercent   MaxShieldPower      EnergyPercent   MaxEnergyPower   (reactor = "energy")
HullPoints      MaxHull/HullPercent
RPGInfo CombatLevel   RPGInfo TradeLevel   RPGInfo ExploreLevel
RPGInfo SkillPoints   RPGInfo SkillPowerupAbilityNumber
Owner  Title  Effects  TargetRange  ReadyTime  Validity
```

The access idiom in the code is: **`Get Client::` (resolve the client object) → `Peek Aux Data::`
(look up a key)**. Functions that do this are tagged in §3. **Strategy: find the one generic
AuxData getter, hook/call it, and you can read *every* stat by key** -- far better than chasing a
separate function per stat. Candidate getter sites (all log `"Peek Aux Data::"`):

| Addr | Module | Reads |
|---|---|---|
| `0x006a4b40` | client/skill | `RPGInfo SkillPowerupAbilityNumber` (ability slots) |
| `0x004180de` (thunk) → resolves into skill | client/skill | `RPGInfo SkillPoints` |
| `0x00417f21` (thunk) | client/skill | `Get Client::` + `Peek Aux Data::` + RPGInfo Skill\* |
| `0x00408913` (thunk) | client/ui | `Get Client::` + `aux data` |

> Follow the `Peek Aux Data` call from one of these into the shared accessor -- that callee is
> the generic `AuxData::Get(key)`, your universal read primitive.

---

## 2. Source-module map (address ranges)

From embedded `e:\build\beta\G\app\<module>\<file>.cpp` assert paths. 3,069 of 42,636
functions carry a path; ranges below are where each subsystem's code lives.

| Module | Funcs | Addr range | What it is |
|---|---:|---|---|
| `app\Client` | 2,253 | `0x401041`–`0x81f890` | **Client gameplay + UI** (the bulk of what you'll hook) |
| `app\shared` | 468 | `0x401073`–`0x8b2230` | Engine: AuxData, physics (`px`), math, RTTI containers |
| `app\Common` | 157 | `0x40104b`–`0x84b020` | Net connection layer (`connection`, `tcpconnection`) |
| `app\Packets` | 87 | `0x401b04`–`0x8acab0` | Packet (de)serialization -- the 87 `Process_Packet` handlers |
| `app\Audio` | 50 | `0x40101e`–`0x442650` | Miles/sound managers |
| `app\wwudp` | 20 | `0x4021f8`–`0x85a000` | Westwood UDP transport |
| `app\input` | 14 | `0x402d24`–`0x430d90` | Keyboard/mouse (`w32kbd`, `w32mouse`, `inpdsp`) |
| `app\kernel`,`display`,`main`,`dbgmgr` | ~20 | -- | Bootstrap / main loop / display init |

Top client source files (truncated names -- full names in `client.srcfiles.tsv`):

| file | funcs | range | likely meaning |
|---|---:|---|---|
| `ui*` | 596 | `0x401212`–`0x7c8e50` | UI panels/controls (largest subsystem) |
| `galax*` | 95 | `0x4035a3`–`0x7b3f00` | galaxy/starmap |
| `scrip*` | 94 | `0x401c67`–`0x8099b0` | in-game scripting/triggers (data-driven, not Lua) |
| `gd*` (gadget) | 93/47 | `0x40141a`–`0x7b4d80` | **Gadget UI widgets** |
| `eff*` | 82 | effects | visual effects |
| `skill*` | 78 | `0x401f73`–`0x818640` | **skills/abilities** |
| `inv*` | 72 | `0x401b90`–`0x690fa0` | **inventory** |
| `avata*` | 62 | `0x401573`–`0x7e3a20` | **avatar/player** |
| `radar*` | 50 | `0x403319`–`0x7be560` | **radar/targeting** |
| `item*` | 55 | items | item definitions |
| `chat*`,`ChatG*` | -- | chat | **chat window + ChatGadget** |
| `contr*` | 38 | `0x402de7`–`0x71cca0` | **controls / keybinds** |
| `nav*` | -- | `0x7b7ef0`+ | **navigation / navs** |
| `px*` (shared) | 38 | `0x401997`–`0x456b60` | **physics / position** |
| `cpack*` | 39 | `0x733700`–`0x73f5f0` | client packet handlers |

---

## 3. Feature → candidate functions

All addresses are absolute (base `0x400000`, no rebase). `thunk_` entries are import/jump
wrappers -- follow to the real function body; the real body is listed where known.

### Health / shields / reactor (energy) / hull
| Addr | Notes |
|---|---|
| `0x006f66e0` / `0x0040c9ff` | Reads full stat block: `EnergyPercent, MaxEnergyPower, ShieldPercent, MaxShieldPower, Owner, Title`. **Best single read point for ship vitals.** |
| `0x005dc4a0` | `EnergyPercent` / `MaxEnergyPower` + "UI POWER/ENERGY" -- energy bar updater. |
| `0x006fe1e0` / `0x0040c31a` (client/co) | `HullPoints`, `Level 1/2/3 Ship Damage`, `IsOrganic` -- hull/damage. |
| `0x005dbfc0` (`thunk 0x004122ce`) | Draws `reactor bar`, `shield bar`, `hull bar` (HUD power UI). |
| `0x00583dc0` (`thunk 0x0040e165`) | `shield bar0` / `reactor bar0` HUD elements. |

### Combat / Trade / Explore level + % (XP bars)
| Addr | Notes |
|---|---|
| `0x00548d60` | Formats `Combat / Trade / Explore … %s Level:%d` -- the level text. |
| `0x0060ed60` | `Combat Level:%d` (e.g. tooltip/title). |
| `0x0058c450` (`thunk 0x0040a399`) | `combat bar / trade bar / explore bar`, `UI EXPERIENCE` -- the XP bar % updater. |
| `0x0074bfb0` (client/sclie) | Reads `RPGInfo CombatLevel`, `RPGInfo TradeLevel` from AuxData. |

### Target name + distance
| Addr | Notes |
|---|---|
| `0x0065e5e0` (`thunk 0x00412ecc`) | `"No target"`, `"Target: %s at"`, `"Target at"`, `%.2f %.2f %.2f` -- **target name + distance/position formatter.** |
| `0x00581e60` / `0x00581f90` (client/ui/cp) | `"current target"`, `HulkUI` -- current-target panel. |
| `0x00410c49` | current target + hulk handling. |

### Chat
| Addr | Notes |
|---|---|
| `0x0065e3e0` (`thunk 0x00401541`) | **ChatGadget** (`app\Client\ChatGadg`), `MSG:%d`. |
| `0x00680700` (`thunk 0x004020f9`) | chat rendering (`app\Client\chat`, `font ptr`). |
| `0x0065bfd0` (`thunk 0x00402c2f`) | Channel routing: `Channel/Radio/Group/Guild/Local/Sector Message`. |
| `0x00749ed0` (`thunk 0x00408760`) | Channel state: `Channel General`, `Not monitoring that channel`, send-message. |

### Navs / navigation
| Addr | Notes |
|---|---|
| `0x007b7ef0` / `0x007b8510` / `0x007b9dc0` | `app\Client\nav` -- nav list build/render (`object list size`, `Lines end`). |
| `0x007c0ee0` (`thunk 0x00402964`) | `WarpPath` / `TargetPath` (starmap routing). |

### Keybinds / activation slots
| Addr | Notes |
|---|---|
| `0x0071a960` (`thunk 0x00402de7`,`0x00403161`) | `app\Client\contr` -- **KeyDefinitions list** (`keylist`, `KeyDefinitions end`). |
| `0x004081fc` (client/contr) | `NumKeyDefs` + bind categories: `Targeting, Navigation, Camera, Activation` -- **ability/hotbar slot binds.** |
| `0x0041802a` / `0x00413110` | KeyDef table load/save (`def ptr`, `NumKeyDefs`). |

### Skills / abilities (read + activate)
| Addr | Notes |
|---|---|
| `0x006a4b40` (client/skill) | Reads `RPGInfo SkillPowerupAbilityNumber` -- **ability slot contents.** |
| `0x00815320` (`thunk 0x004067b7`) | `ability` (skill object). |
| `0x0060f1a0` (`thunk 0x00401ab4`) | **Ability lifecycle**: `Skill Activated / Deactivated / Interrupted / Interruption Failed`. Hook to detect or (with care) **trigger** ability use. |
| `0x00662dc0` / `0x00663df0` (client/gd) | Skill **gadget buttons** (`SKILL NAME ATT`, `LargeBttn`) -- the clickable hotbar UI; calling their click handler is one way to trigger an ability. |

### Browser / embedded IE (HTML UI surface)
| Addr | Notes |
|---|---|
| `0x00412ed1` (`thunk_FUN_007e0070`) | Creates the WebBrowser COM object: `CoCreateInstance(&DAT_00ba0f90, …, &DAT_00ba0fa8)` + `OleRun`. |
| `client.c:1123074`, `1328476` | Other `CoCreateInstance` sites (browser + a 2nd CLSID `DAT_00bb9c08`). |
| Data: `DAT_00ba0f90` (CLSID), `DAT_00ba0fa8`/`DAT_00ba0f78` (IIDs) | `{2B2CC8B0-…}` FEBrowserEngine2. HTML lives in `..\data\client\htmldocs\` (packed in mixfiles). |

---

## 4. Engine anchors (for injection & per-frame work)

| Purpose | Addr | Notes |
|---|---|---|
| **Win32 message pump** | `0x00443710`, `0x00443e90` | `GetMessageA/TranslateMessage/DispatchMessageA` -- main window loop; good place to pump your own per-frame logic on the game thread. |
| Message pump (peek) | `0x007428f0` | `PeekMessageA + RedrawWindow` variant. |
| Keyboard input | `0x004307a0` (input/w32kbd) | `GetKeyState/GetMessagePos` -- raw key handling; sits next to the keybind system. |
| Gamma/Device | `0x00926e80` | `SetDeviceGammaRamp` (display). |
| Rendering | via **DDRAW.dll** | The 3D/2D present path is DirectDraw, called through COM vtables (not named imports), so it isn't a single named function. For overlays, hook `IDirectDrawSurface::Flip`/`Blt` at the vtable (see `LUA-MODDING-DESIGN.md §Drawing`). |

---

## 5. Network packet handlers (state from the server)

87 `*PacketClass::Process_Packet()` handlers (`app\Packets`, range `0x401b04`–`0x8acab0`). Each
logs its name on entry, e.g. `s_AvatarDescPacketClass__Process_Packet_…`, registered via the
logger `FUN_009ea930`. Hooking these is often the **cleanest** way to read game state because the
data is already deserialized and typed. To map a handler name → function: follow the string's
single data xref to the function. High-value examples seen:
`AvatarDescPacketClass`, `AdvancedPositionalUpdatePacketClass` (position!), `StartPacketClass`,
`GroupPacketClass`, `SoundPacketClass`, `DecalPacketClass`, `SubPartsPacket`. Full list:
`grep -oE '[A-Za-z0-9_]+PacketClass::Process_Packet' client.c | sort -u`.

See the proxy for the wire side of these same packets.

---

## 6. How to use this map

1. Pick a target from §3, go to the address, read it, recover the struct/offsets it touches.
2. For reads: prefer hooking the **AuxData getter** (§1) or a per-frame **UI updater** (§3) and
   snapshotting values into shared memory your Lua DLL reads.
3. For position specifically: combine the `AdvancedPositionalUpdatePacket` handler (§5) with the
   `px` physics module (§2) -- the local player's transform lives there.
4. Validate every offset at runtime before shipping (addresses are static, but struct layouts
   are assumptions until confirmed).
</content>
