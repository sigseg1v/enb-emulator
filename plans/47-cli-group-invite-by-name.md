# Plan 47 -- CLI: invite any online player to a group by name (no scanner visibility)

Status: **OPEN** (recorded 2026-06-12, not started). CLI-only change; no server change.

## The problem

`group invite <player>` in the CLI only works when the target is already tracked
in your sector scanner:

```
griever2@Luna > group invite Teone
no tracked player named 'Teone' -- run `list` first, they must be in your sector
```

That is wrong. In real Earth & Beyond you can invite anyone who is **online**,
regardless of which sector they are in. The CLI restriction is purely an artifact
of how it currently resolves the name: `GroupCommand.InviteAsync`
(`freya/cli-client/src/CliClient.Core/Repl/Commands/GroupCommands.cs`) calls
`_ctx.World.FindByName(name)` to get a **GameID**, then sends the host-order
`0x002C` ACTION (Action 10 = invite, Target = that GameID). `World.FindByName`
only knows players currently in scanner range, so an out-of-sector target can
never be found and the invite is refused before it is even sent.

## The server already supports by-name, cross-sector invite

There is a SECOND action opcode built exactly for this. Verified in the server:

- **`0x002D` ENB_OPCODE_002D_ACTION2** (`server/src/PlayerConnection.cpp:472`)
  -> `Player::HandleAction2` (`PlayerConnection.cpp:4370`). The function comment
  is literally *"actions across sector boundaries (ie group kick)"*.
- It carries a **name string**, not a target id, and the server resolves the name
  globally: `converted.Target = g_PlayerMgr->GetGameIDFromName(myAction2->string)`
  (`PlayerConnection.cpp:4377`), then dispatches into the normal `HandleAction`.
- `PlayerManager::GetPlayer(char* Name)` / `GetGameIDFromName`
  (`server/src/PlayerManager.cpp:253` / `:270`) walk `m_GlobalPlayerList`
  case-insensitively -- **global, across all sectors**. Likewise the invite's
  `GetPlayer(PlayerID)` (`GroupManager.cpp:130`) keys on the global
  `m_PlayerLookup` CharacterID table, not a sector list. So the server can invite
  any online player by name today; nothing server-side needs to change.

### `ActionPacket2` wire layout (`common/include/net7/PacketStructures.h:594`)

```c
struct ActionPacket2 {
    int32_t  GameID;        // reversed bytes (network byte order)
    int32_t  Action;        // reversed bytes
    short    string_len;    // BSTR length prefix
    char     string[1];     // the target NAME (string_len bytes, not NUL-terminated on wire)
    int32_t  _OptionalVar;  // reversed bytes -- read at (string + string_len)
} ATTRIB_PACKED;
```

**CRITICAL byte-order trap:** unlike `0x002C` ACTION (which the server reads raw
host-order little-endian), `HandleAction2` runs `ntohl()` on GameID, Action, and
OptionalVar (`PlayerConnection.cpp:4375-4382`). So those three int32 fields go on
the wire **big-endian / network byte order**, and `string_len` is a little-endian
`short` (BSTR). Get this wrong and the server resolves Action != 10 or a garbage
name. (See CLAUDE.md "Wire format & byte order" -- this is the inverse of the
usual LE rule, and it is intentional here because the server byte-swaps.)

OptionalVar is read at offset `string + string_len` (immediately after the name
bytes, no NUL). For an invite it is unused (0).

## The fix (CLI-only)

1. Add an `ActionPacket2Builder` (sibling of `ActionPacketBuilder` in
   `GroupCommands.cs`) that emits the 0x002D layout: BE GameID, BE Action,
   LE int16 `string_len`, the ASCII name bytes, BE int32 OptionalVar. Send via
   `OpcodeId.Known.Action2` (add the 0x002D entry if not already present).
2. Change `InviteAsync`: if `World.FindByName(name)` hits, the in-sector 0x002C
   path is fine to keep (it is the same end result); but when it MISSES, fall
   back to the 0x002D by-name invite instead of refusing. Simplest and most
   faithful: **always** use the 0x002D by-name invite for `group invite <name>`
   (it works for in-sector and out-of-sector identically), and drop the
   `FindByName` precondition entirely. Decide which during implementation; the
   by-name-always path is closest to how the real client behaves.
3. Update the command's error/help text (it currently says "they must be in your
   sector").

## Mandatory per CLAUDE.md

- **CLI parse + byte-pin test FIRST.** Add a unit test that pins the 0x002D
  emitter byte-for-byte, *especially the big-endian GameID/Action/OptionalVar and
  the LE BSTR length* -- this is the whole risk surface. Mirror
  `SelfGroupStateTests.CtaRequest_IsByteExact`.
- **Keep the three places in sync.** This is a client->server opcode the proxy
  forwards; confirm the proxy does not special-case 0x002D
  (`proxy/ClientToServer_linux_stubs.cpp` `ProcessSectorServerOpcode`). If it
  does, note it; if it forwards unchanged, say so in the commit.
- **No server change**, so no primary-source-capture gate -- but add a
  `plans/29-client-verification.md` CV entry: the CLI proves the bytes, only the
  real client proves an out-of-sector invite actually pops the invite dialog on
  the target. What to look for: two characters in *different* sectors, `group
  invite <name>` from one, the other gets "You have been invited by ..." and
  `group accept` forms the group.

## Open question to resolve during implementation

Does the retail client send 0x002D for ALL invites, or only when the target is
not in-sector? Check a capture if one exists
(`archive/kyp-snapshot/capturedPackets/`); otherwise default to by-name-always
since the server handles both through the same `HandleAction` path anyway.
