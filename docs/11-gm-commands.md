# 11 - GM commands

Slash commands available to game masters and administrators. Reformatted
from `reference/gm-commands-original.txt`. The original source is short
and partial -- this document is faithful to it and does not invent
additional commands. Commands that exist in the server source but are not
in the original GM-commands document are out of scope for this
preservation and will be added if and when they are verified.

Two prefix conventions exist:

- `//` -- account administration commands (require admin / GM access
  level). Typically affect player accounts or grant access.
- `/` -- in-game admin commands (require GM access level). Typically
  affect the current session, a target player, or game state.

Where the original document gives no example, the example column shows
the syntax pattern. Replace angle-bracketed names with concrete values.

## Account administration (`//` prefix)

### `//adduser`

| Field | Value |
|---|---|
| Command | `//adduser <username> <password> <access>` |
| Description | Adds a user to the server. |
| Example | `//adduser dev devpassword 100` |

### `//setpassword`

| Field | Value |
|---|---|
| Command | `//setpassword <username> <password>` |
| Description | Change the password for a username. |
| Example | `//setpassword dev newdevpass` |

### `//gmskillpoints`

| Field | Value |
|---|---|
| Command | `//gmskillpoints <playername> <skillpoints>` |
| Description | Add skillpoints to a player. |
| Example | `//gmskillpoints dev 50` |

### `//gmenableskills`

| Field | Value |
|---|---|
| Command | `//gmenableskills <playername>` |
| Description | Enable all skills for a user. |
| Example | `//gmenableskills dev` |

### `//gmplayerlevel`

| Field | Value |
|---|---|
| Command | `//gmplayerlevel <playername> <level>` |
| Description | Add levels to a player. |
| Example | `//gmplayerlevel dev 10` |

### `//gmsetaccess`

| Field | Value |
|---|---|
| Command | `//gmsetaccess <playername> <level>` |
| Description | Set the access level for a player. |
| Example | `//gmsetaccess dev 100` |

### `//gmgetaccess`

| Field | Value |
|---|---|
| Command | `//gmgetaccess <playername>` |
| Description | Get the access level for a player. |
| Example | `//gmgetaccess dev` |

## In-game admin commands (`/` prefix)

### `/kick`

| Field | Value |
|---|---|
| Command | `/kick <Playername> <reason>` |
| Description | Kick a player from the server. |
| Example | `/kick dev "AFK timeout"` |

### `/createcredits`

| Field | Value |
|---|---|
| Command | `/createcredits <credits>` |
| Description | Create credits (in-game currency) for yourself. |
| Example | `/createcredits 100000` |

### `/createitem`

| Field | Value |
|---|---|
| Command | `/createitem <item ID> <number>` |
| Description | Create a quantity of an item by base ID. Item IDs are listed in `item_base.id` (see `06-database-schema.md`). |
| Example | `/createitem 516 1` |

## Notes

- The original source does not specify access-level numeric values.
  Convention in the preserved code uses 0 for normal players and 100
  for full admin; intermediate values exist for moderators. Verify
  against the running server before relying on a specific number.
- `//adduser` is the same command referenced in
  `09-running-locally.md` for creating a dev account.
- Item IDs for `/createitem` correspond to `item_base.id`. The Item
  Editor (`tools/itemeditor/`) is the easiest way to look them up.
- This document will be expanded as additional commands are verified
  against the server source. The Phase H docs deepening pass is the
  natural time to do that audit.
