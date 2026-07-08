# 11 - GM / admin slash commands

Complete catalog of the server's in-chat slash commands, **verified against
the server source** (`server/src/PlayerConnection.cpp`, `Player::HandleSlashCommands`,
dispatch starts ~line 4931). This supersedes the old partial doc that only
reproduced the 35-line `archive/kyp-snapshot/Documents/GMCommands.txt` (that
file lists ~10 commands; the server implements ~150). Everything here is read
from the dispatch code, not invented.

Use this as a **testing tool**. When driving the CLI client or a live client
against the dev stack, these commands let you set up state fast -- most
importantly **`/wormhole <sector>` to jump between sectors** without flying
(see [Movement / teleport](#movement--teleport-)). Handy for reproducing a
sector-specific bug, positioning for a capture, or getting a test character to
a mob/quest contact quickly.

## How dispatch + gating works

All chat starting with `/` routes into `HandleSlashCommands`. There are two
dispatch blocks:

- **`//` (double-slash)** -- account/server administration. The **entire block
  is gated at `AdminLevel() >= GM (50)`**: a non-GM cannot reach any `//`
  command. Some add a higher per-command gate on top.
- **`/` (single-slash)** -- in-game/session commands. **No block-level gate**;
  each command self-gates. Many single-slash editing/debug commands are
  currently **ungated** (any authenticated player can invoke them) -- flagged
  inline below. That is a known privilege-surface wart, not a spec.

Access-level constants (`server/src/Net7.h`): `USER=0, HELPER=20, BETA=30,
BETA_PLUS=40, GM=50, DGM=60, HGM=70, DEV=80, SDEV=90, ADMIN=100`.

Argument syntax below is read from the parsing code (`sscanf`, token split,
`MatchOptWithParam`). Replace `<angle-bracketed>` names with concrete values.
`[bracketed]` args are optional.

---

## Movement / teleport ⭐

The commands you actually want for test navigation.

| Command | Gate | What it does |
|---|---|---|
| `/wormhole <sectorID \| sectorName>` | `>= BETA_PLUS (40)` [1] | **Self-teleport across sectors.** Numeric id, or a sector name resolved via `GetSectorIDFromName`. A station id (`> 9999`) is divided by 10 to the parent sector. Blocked while docked in a starbase, and blocked for non-zero destination sector types, unless you are `SDEV (90)` (which bypasses both). On success does a real `SectorServerHandoff`. |
| `/gwormhole <sectorID \| sectorName>` | `>= BETA_PLUS` | Sends every group member (not already there, not docked) a follow-me wormhole confirmation to that sector. Cannot target a station id. Closest thing to a "summon group" -- there is **no** `/summon`. |
| `/goto` *(no args)* | `>= GM` | Teleport **you** to your currently-targeted object's position, same sector. Prints "disabled" and no-ops below GM. |
| `/fetch` *(no args)* | `>= GM` | Move your targeted object (not a player/MOB) to your position. |
| `/warp <speed>` | `>= GM` | **Not** a teleport -- sets your warp *speed* stat (clamped 1000-6000). |
| `/warpreset` | none | Reset/terminate your warp state. |
| `/dockp` / `/undockp` | none | Debug dock / undock yourself. |

[1] The on-screen text says "GM and above only" but the code gate is
`BETA_PLUS`; on `DEV_SERVER` builds it is `BETA (30)`.

> **Do not use `/move x,y,z`** for navigation -- it is a dead no-op (its
> `SetPosition` is behind an `if (!this)` that can never be true). Use
> `/wormhole` (cross-sector) or `/goto` (to a target, same sector).

Example: `/wormhole Aganju` or `/wormhole 1115`.

---

## Account / access (`//`, all block-gated `>= GM`)

| Command | Extra gate | What it does |
|---|---|---|
| `//adduser <username> <password> <access>` | -- | Create a server account. |
| `//setpassword <username> <password>` | -- | Change another account's password. |
| `//ban <playername>` | -- | Set target account status to banned (-2) and kick. Cannot ban above your level. |
| `//gmsetaccess <playername> <level>` | `>= SDEV` | Persist an online player's access level (clamped to yours). |
| `//bumpaccess <playername> <access>` | `>= DEV` | Set an online player's *in-memory* access (clamped to yours). |
| `//gmgetaccess <playername>` | -- | Print an online player's access level. |
| `//gmwarn <playername> <inc> <message>` | -- | Increment a player's warn level with a message. |
| `//countsp <player>` | -- | Print a player's spent/total/unspent skill points. |
| `//friends` | `>= SDEV` | List your friends list. |

## Player state (levels / skills / character)

`//` commands (block `>= GM`):

| Command | Extra gate | What it does |
|---|---|---|
| `//gmskillpoints <playername> <points>` | -- | Set an online player's skill-point pool, re-level skills. |
| `//gmenableskills <playername>` | -- | Enable all "unavailable" skills for a player. |
| `//gmplayerlevel <playername> <level>` | level `<= 50`, target `> BETA` | Set a player's combat/trade/explore level. |
| `//gmupgrade <playername>` | `>= GM` | Bump a player's hull upgrade level by 1. |
| `//respec <username> <all\|call\|0-63>` | -- | Reset a player's skills (all / call-forward / one skill id), refund points. |
| `//replaceship <asset> <scale>` | -- | Override your ship asset/scale (asset 0 resets). |
| `//resetmymissions` | -- | Reset your own missions. |

`/` commands (self, gate `>= GM` unless noted):

| Command | Gate | What it does |
|---|---|---|
| `/level <0-50>` | `>= GM` | Set your own levels + `level*10` skill points. |
| `/skillpoints <n>` | `>= GM` | Set your own skill-point pool. |
| `/enableskills` | `>= GM` | Enable all your unavailable skills. |
| `/upgrade <level>` | `>= GM` | Set your own hull upgrade level. |
| `/resetchar` | `>= GM` | Wipe your own character to zero and force logout. |
| `/resetmounts` | none | Reset your weapon mounts. |
| `/resetnavs` | `>= GM` | Mark all your navs unexplored, then refresh via wormhole. |
| `/ht <head> <body> <gender>` | none | Set your avatar head/body/gender ids. |
| `/capitalize` | none | Toggle case of the 2-letter class suffix on your name. |
| `/changepassword <newpassword>` | none | Change your own account password. |
| `/stat <statname> <type 0-4> <value>` | `>= DEV` | Set a ship stat on yourself. |
| `/getstat <statname>` | `>= GM` | Print base/mult/add/div/sub of a stat. |
| `/scan <1000..N>` | `>= BETA_PLUS` | Set your scan range (max 20000, or 400000 if `>= DEV`). |
| `/shieldbuff [amount]` | `>= DEV`/`GM` | Add an absorb shield, or print remaining absorb. |
| `/buff <bufftype>` | none | Apply a hardcoded 10s test buff (sig/scanrange/effect 214). |
| `/noattack` | `>= GM` | Toggle your combat immunity. |
| `/invisible` / `/invis [on\|off]` | `>= GM` | Toggle scan-invisibility. |
| `/factionoverride` | `>= GM` | Toggle ignoring class/faction gates for yourself. |
| `/notells` | `>= GM` | Toggle "tells from friends only". |
| `/authlevel` | none | Print your own access level. |
| `/shieldwarnings <0-4>` | none | Set audio shield-warning level. |

## Items / credits / inventory

| Command | Gate | What it does |
|---|---|---|
| `/createitem <itemID> [count] [quality]` | `>= GM` | Add item(s) to your first free cargo slot (validates itemID; quality <= 2.0; count default 1). |
| `/createcredits <credits>` | `>= GM` | Award yourself credits. |
| `/getgmitems` | none [2] | Load the GM item set. |
| `/flushinv` | `>= GM` | Empty every occupied cargo slot. |
| `/restoreinv` | none | Empty cargo, ensure >= 20 cargo slots. |
| `/trade <playername>` | none [3] | Open a trade window with a player. |
| `//baseitemlist` | `== SDEV` | One-shot base-item-table builder. |

[2] `/getgmitems` has no gate in source. [3] `/trade` waives the range check
`>= BETA_PLUS`.

## Spawning / effects / missions

| Command | Gate | What it does |
|---|---|---|
| `/create <...>` | none | Create an object (args parsed in `HandleObjCreateRequest`). |
| `/createmob <...>` | `>= GM` | Spawn a MOB (`HandleMobCreateRequest`). |
| `/deco <basset>` | none | Spawn a decorative object at your position. |
| `/createmission <missionID>` | `>= GM` | Assign a mission to yourself. |
| `/effect <effectDescID> [len_ms]` | `>= GM` | Play a self object-effect (default 4000ms). |
| `/effecto <effectDescID> [len] [scale] [speedup]` | `>= GM` | Object-to-object effect on your target. |
| `/effects <effectID>` | `>= GM` | Stop/remove an active effect. |

## Object / world editing

Mostly `/`, **many ungated** (any player) -- flagged. All act on your
**currently-targeted object**.

| Command | Gate | What it does |
|---|---|---|
| `/hijack` / `/release` | `>= GM` / none | Take / release control of your target. |
| `/face` / `/faceme` | none | Target faces its heading / faces you. |
| `/panup\|panx\|pany\|panz <v>` | none | Move target along an axis. |
| `/rotatex\|rotatey\|rotatez <v>` | none | Rotate target roll/pitch/yaw. |
| `/levelout` | none | Level the target. |
| `/scale <v>` | none | Set target scale. |
| `/tilt <0-90>` | none | Planet tilt. |
| `/planetspin <1-1000>` | none | Planet rotation speed. |
| `/orientation <o1,o2,o3,o4>` / `/oeuler <...>` | none | Set target quaternion / euler orientation. |
| `/signature <v>` | `>= DEV` | Set nav signature. |
| `/setradius [v]` | `>= DEV` | Set object radius (no arg = distance to target). |
| `/commit` | `>= DEV` | Persist target edits to the DB. |
| `/destroyobject` (`//`) | `>= GM` | Destroy the target. |
| `/exposedecos on\|off` | `>= GM` | Make nearby decos clickable. |
| `/checklock` | none | Report who is mining/looting your target. |
| `/stats` | `>= DEV` | Dump target Field/Nav/Gate/MOB stats. |
| `/lootstats` | `>= BETA` | Dump target loot table. |
| `/position` / `/range` / `/heading` / `/ori` | none | Print target coords / range / your heading / orientation. |
| `/rs`, `/rsi <v>`, `/rsa <v>`, `/rsn <v>`, `/rsd` | none | Target render-state print / init / activate / activate-next / deactivate. |

### Asteroid-field editing (`/f…`, gate `>= DEV`)

Act on your targeted field via `HandleChangeFieldRequest`:
`/fhelp`, `/fradius <v>`, `/ftype <0-5>`, `/flevel <1-8>`, `/fcount <v>`,
`/faddasteroidtype <basset>`, `/faddoretofield <itemID>`,
`/fdelorefromfield <itemID>`, `/faddoretosector <itemID>`,
`/fdelorefromsector <itemID>`, plus `/addbaseore <itemID>` /
`/removebaseore <itemID>` (sector-wide).

## World / sector admin

`//` (block `>= GM`): `//rstations`, `//rsectors`, `//rsectorall`
(**slow/dangerous**), `//ritems`, `//rmissions`, `//rfactions`,
`//restartcomms <...>`, `//killfactions`, `//findsector <name>`,
`//halloween on|off`, `//floodsave` (`>= SDEV`, stress test).

`/`: `/slaysectormobs` (`>= SDEV`, kill all mobs in your sector),
`/setturrets` / `/setrespawns` (`>= SDEV`), `/shutdown` (`>= DEV`),
`/uptime` (none), `/strings` (`>= DEV`).

## Chat channels / who-lists

`/who [search]` (none), `/bwho` (`>= BETA`), `/gwho`, `/dwho`,
`/find <playername>` (`>= GM`). Faction: `//displayfactions`,
`//displayplayerfaction <name>`, `//editfaction <id> <value>`,
`//editplayerfaction <...>`. Channels: `/be`,`/beon`,`/beoff` (`>= BETA`);
`/d`,`/don`,`/doff` (`>= DEV`); `/gm`,`/gmon`,`/gmoff` (`>= GM`);
`/errorson`/`/errorsoff` (`>= BETA_PLUS`); `/global <msg>` (`>= BETA_PLUS`);
`/chjoin <ch>` / `/chleave <ch>` (`>= GM`); `/gc [args]` (none, guild);
`/gmgc [args]` (`>= GM`).

## Group

`/invite <playername>` (none), `/leavegroup` (none), `/groupc`, `/groupid`,
`/gameid`, `/form` (`>= GM`), `/gform <formation>` (`>= GM`),
`/kick <playername> <reason>` (`>= GM`).

## Misc / debug

`/test`, `/talktree`, `/testmsg <ms>` (`>= DEV`), `/dialog <text>,<type>`
(`>= DEV`), `/debug` (`>= DEV`), `/debugmissions on|off`, `/endtalk`,
`/sounds <name>`, `/music <id>` (`>= DEV`), `/ccamera <mode>`,
`/openif <a>,<b>`, `/uitrigger <a>,<b>`, `/packetopt on|off|lac`,
`/mobaggro <...>` (`>= DEV`), `/fgps`, `/fireweapon`,
`/terminate <player|id>` (`>= DEV`), `/sendp` (`== SDEV`),
`/altweapon`/`/altname` (`>= DEV`), `/script <name>` (`>= SDEV`, Lua removed),
`/helpedit`, `/helpfield`.

---

## Notes / caveats (read before relying on these in a test)

- **`/wormhole` is your sector-jump primitive**; `/move` is dead; `/warp` sets
  speed, not position.
- **Privilege surface:** many single-slash object/field/debug commands are
  ungated. Treat "no gate" above as a fact about the current code, not an
  endorsement -- do not depend on it staying that way.
- **Access level:** to use the `//` block or the GM-gated `/` commands your
  character's account status must be `>= 50`. The dev seed account can be
  bumped with `//gmsetaccess` from an already-privileged session, or set in the
  DB.
- Line references and full behavior are in `server/src/PlayerConnection.cpp`
  (`Player::HandleSlashCommands`). When a command's behavior here disagrees
  with the code, the code wins -- update this doc.
