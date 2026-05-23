# Gameplay loop — server's view

What happens server-side when a player does the things players
do: shoot, jump, take a mission, trade, join a guild, talk.
Each section is a starting pointer, not a complete spec.

## 1. Combat

The combat loop is request-driven: client sends an activation
packet, server resolves it, server broadcasts state deltas.

**Weapon firing.** Entry point is
`Equipable::ManualActivate()` (Equipable.h:59, body in
Equipable.cpp:119), the shared path for both player and MOB
equippable activation. Energy weapons specifically go through
`Player::FireEnergyCannon()` (PlayerCombat.cpp:234):

1. Check energy reserves (line 241).
2. Deduct energy cost (line 248).
3. Roll hit/miss/crit via `Player::CalcDamage()`
   (PlayerCombat.cpp:331) — uses weapon subcategory, stat
   modifiers, and `CalcMissChanceVersus()`
   (PlayerCombat.cpp:299).
4. Apply impact radius across sector — damage every target in
   the AoE (PlayerCombat.cpp:262-294).

**Damage application.** Funnels through
`Player::DamageObject()` (PlayerCombat.cpp:28): shields
first, then deflects, then hull. Hull zero triggers
`Player::RemoveHull()` (PlayerCombat.cpp:77) — incapacitation,
XP debt accounting, player immobilization. Warp transit
short-circuits damage (PlayerCombat.cpp:38) and aborts
prospecting (line 50).

## 2. Exploration and sector travel

Two distinct mechanisms — impulse warp and stargate jump.

**Impulse warp** (within or between adjacent sectors):
`Connection::HandleWarp()` (ClientToSectorServer.cpp:1836)
walks the nav chain via `Player::SetupWarpNavs()` (1848),
then `PrepareForWarp()` / `StartWarp()` /
`TerminateWarp()` (PlayerClass.h:391-394). Aborts on player
action or arrival (ClientToSectorServer.cpp:2726).

**Stargate jump** (cross-system):
`Connection::OpenStargate_1()`
(ClientToSectorServer.cpp:6192) → render-state change →
`Connection::GateSequenceEnd()` (line 6185) →
`SectorManager::GateJump()` transfers the player to the
target sector. Stargate objects are typed `OT_STARGATE`
(StarGateClass.h).

Sector handoff is documented in `docs/04-server-modules.md`
§8.2 — the actual entry into the new sector goes through
the sector-server connection.

## 3. Missions

Missions are tree-structured content loaded at boot
(MissionDatabaseSQL.h:43, `LoadMissionContent()`), then
exercised per-player against a node-completion bitmask.

**Per-tick check.** `Player::CheckStageCompletionNodes()`
(PlayerMissions.cpp:32) walks each active mission's current
stage and tests every node type: TalkToNPC, ObtainItem,
KillMob, ScanObject, etc. Progress lives in the player's
`m_PlayerIndex.Missions.Mission[slot]` (PlayerMissions.cpp:69)
and persists to `avatar_missions` + `mission_objectives`.

Rewards (XP, credits, items) dispatch from stage-complete
callbacks. Mission XML is interpreted at load — the runtime
form is `MissionTree` graphs in
`g_ServerMgr->m_Missions.m_Missions`.

## 4. Trading and economy

Player-to-player and player-to-vendor share infrastructure
but enter through different opcodes.

**Player-to-player.** `Connection::TradeAction()`
(ClientToSectorServer.cpp:3352) tracks window state (open,
close, confirm, accept) and routes through
`Player::TradeAction()` (PlayerClass.h:1012). Items in flight
live in `ShipIndex()->Inventory.TradeInv`
(PlayerInventory.cpp:33). Capacity gating via
`Player::CargoFreeSpace()` (PlayerInventory.cpp:48) and
`TradeSpaceUsed()` (line 38).

**Vendor / NPC trade.** `Player::NPCTradeItems()`
(PlayerClass.h:704). Pricing modulated by player trading
skill in `Player::Negotiate()` (PlayerClass.h:737).

## 5. Guilds

Guild operations are dispatched from a single command handler:
`Player::HandleGuildCommand()` (PlayerGuild.cpp:56), which
fans out to `HandleCreateGuild()` (line 66),
`HandlePromoteMember()` (line 82), `HandleRemoveMember()`
(line 89), `HandleListAllGuildMembers()` (line 74), etc.

Guild state loads at boot via
`PlayerManager::LoadGuildsFromSQL()` (GuildManager.cpp:26) —
tables `guilds`, `guild_ranks`, `guild_members`. Per-player
guild hookup is `Player::SetupGuildInfo()` (PlayerGuild.cpp:25),
which auto-subscribes the player to the guild chat channel.

Response-side constants (rank-changed, member-added, member-removed,
message-of-the-day, etc.) are in Guilds.h.

## 6. Chat and social

All chat goes through the connection handler:
`Connection::HandleClientChat()` (ClientToSectorServer.cpp:4073)
for in-world chat;
`Connection::HandleClientChatRequest()` (line 1701) for
channel ops and private messages.

Chat-type constants (ClientToSectorServer.cpp:4097):
- `0` channel
- `1` group
- `2` guild
- `3` local
- `4` sector-wide

Routing helpers on `PlayerManager`:
`ChatSendEveryone()` (line 4099),
`GroupChat()` (line 4105),
`ChatSendPrivate()` (line 1759),
`ChatSendChannel()` (line 1765).

## 7. The big picture

```
client packet ──► ConnectionManager (recv loop) ──► HandleXxx() dispatch
                                                            │
                                                            ▼
                                          ┌────────────────────────────────┐
                                          │ Player / sector / guild logic  │
                                          │  (mutates in-memory state)     │
                                          └────────────────────────────────┘
                                                            │
                              ┌─────────────────────────────┼─────────────────────────────┐
                              ▼                             ▼                             ▼
                       Player object              Sector object lists              DAO writes
                       (m_PlayerIndex)            (mobs, npcs, items)            (avatar_*, guild_*)
                              │
                              ▼
                       Outbound MessageQueue ──► PulseConnectionOutput ──► client
```

The packet-dispatch and per-tick scheduling that drives all of
this are documented in `docs/04-server-modules.md` §6-7. The
ability subsystem is `docs/05-abilities.md`.
