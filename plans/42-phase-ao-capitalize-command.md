# Phase AO: `/capitalize` chat command (class-suffix case toggle)

Owner feature request (2026-06-08):

> when you type "/capitalize" in chat it checks if your name ends with
> TE TS TT JE JS JD PW PS PP and capitalizes it and saves. if its
> capitalized on those 2 letters already it lowercases and saves.

A cosmetic, player-facing toggle for the 2-letter **class suffix** that
EnB players append to their character names (the class abbreviations:
Terran Enforcer/Scout/Trader = TE/TS/TT, Jenquai Explorer/Seeker/Defender
= JE/JS/JD, Progen Warrior/Sentinel/Privateer = PW/PS/PP).

## Behaviour

- `/capitalize` with no argument.
- Look at the last two characters of the caller's own avatar first name.
- Match is **case-insensitive** against the 9 class codes.
- If the suffix is **not** one of the 9 codes: do nothing, tell the
  player their name must end in a class suffix.
- If it **is** a code:
  - both letters already upper-case -> lower-case both, save.
  - otherwise -> upper-case both, save.
- It is a **toggle**: re-running flips it back.

## Why this design (persistence is the subtle part)

The avatar name is persisted in `net7_user.avatar_data.first_name`
(citext; preserves stored case). There are exactly two ways `first_name`
gets rewritten while a player is online:

1. `SaveManager::HandleDatabase` (`SAVE_CODE_DATABASE` = 0x0026) -- a
   **full** character save. The ONLY enqueuer of this save code is the
   server-side `Player::SaveDatabase()`, which serializes the server's
   own authoritative `m_Database`. The **client never sends a full-DB
   blob** -- it sends granular save codes (credit, inventory, position,
   sector, ...) that do not touch `first_name`. `HandleLogout`
   (`SAVE_CODE_LOGOUT`) and `HandleUpdateDatabase` (`SAVE_CODE_UPDATE_DATABASE`)
   only touch `avatar_info` timestamps / sector, never `avatar_data`.

So the server owns the name. The correct, clobber-free persist path is:
mutate `m_Database.avatar.avatar_first_name` (via `Database()`), then call
the existing `Player::SaveDatabase()`. No raw UPDATE, no pending-rename
override map, no new mutex -- the existing audited save path does it.

An earlier draft considered a surgical `UPDATE avatar_data SET first_name`
plus a SaveManager override to defend against a client full-save clobber.
That was over-engineering built on a false premise: there is no inbound
client full-DB save to clobber it. Dropped.

## Live vs relog

On `/capitalize` the command also calls:
- `SetName(newname)` -- updates `m_Name`/`m_NameBuffer` (chat sender, logs).
- `ShipIndex()->SetName(newname)` + `SetOwner(newname)` -- the ship object
  (mirrors `PlayerSaves.cpp:320-321`, the login path).

The client caches its rendered nameplate from the login avatar record, so
the **visible nameplate updates on relog**. Chat/ship-owner update live.
The player message says so explicitly.

## Scope / security

- Operates strictly on the **caller's own** `m_Database` -- no lookup of
  any other avatar, no id taken from the wire. No new auth surface.
- Non-retail **additive** command: the retail server answered `/capitalize`
  with "unknown command". This does not change how any retail EnB client
  wire packet is parsed or emitted -- it is a server-internal name edit
  routed through an existing save opcode. The wire-fidelity gate (CLI
  byte-pin + plans/29 CV) therefore does not strictly apply. A cosmetic
  real-client nameplate confirmation is tracked as **CV-CAP-1** anyway.

## Checklist

- [x] AO-1: implement `/capitalize` in `PlayerConnection.cpp` case 'c'
      (no-param `strcmp` command; 9-code table; case-insensitive match;
      toggle; `SetName` + ship name/owner + `SaveDatabase()`; user
      messages for both the toggled and the "not a class suffix" cases).
      Syntax-checked clean against the real `compile_commands.json` flags
      (host CMake reconfigure blocked only by a missing libsodium dev pkg,
      unrelated to this change; docker build is the deploy path).
- [x] AO-2: register Phase AO in `plans/00-master.md`.
- [x] AO-3: add **CV-CAP-1** to `plans/29-client-verification.md` (owner
      confirms against the real client: name ends in a class code,
      `/capitalize` flips the suffix case, persists across relog, and the
      rendered nameplate reflects it after relog).
- [ ] AO-4: owner confirms CV-CAP-1 against the real client.

## Notes

- The change is local until pushed. Standing instruction: author + push to
  git when done, do NOT deploy.
- `toupper`/`tolower` reach `PlayerConnection.cpp` via `Net7.h` ->
  `<ctype.h>` (already used elsewhere in the file).
- `avatar_first_name` is `char[20]` (`common/include/net7/PacketStructures.h:144`);
  the toggle edits in place and never grows the string, so no length risk.
