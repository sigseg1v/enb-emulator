// SaveManager.h
/* Net-7 Entertainment: Net-7 Earth and Beyond emulator project
**
** This code/content is licensed under the Creative Commons license, it is interactive content. You can view the terms of our:
** Creative Commons Attribution-Noncommercial-Share Alike 3.0 United States License
** http://creativecommons.org/licenses/by-nc-sa/3.0/us/
**
** Net-7 Emulator Project, an Earth & Beyond emulator by Net7 Entertainment is licensed under a Creative Commons Attribution-Noncommercial-Share Alike 3.0 United States License
**
** Based on a work at http://www.earthandbeyond.com
**
** Permissions beyond the scope of this license may be available at http://www.dreamersofdawn.org/docs/More_Information.htm
**
** The license can be modified at our discretion within the bounds of Creative Commons at any time.
**
** Copyright of our assets/code/software began in 2005-2009 ©, Net-7 Entertainment.
**
*/

#ifndef _SAVE_MANAGER_H_INCLUDED_
#define _SAVE_MANAGER_H_INCLUDED_

#include <net7/Mutex.h>
#include "MessageQueue.h"
#include "db/sqlplus.h"

#define SAVE_MESSAGE_MAX_LENGTH 1306	// petitions can be very large (1297)
#define SAVE_SLOTS				32768

class Object;
class Player;

// There is one instance of this class for all Players on this server
class SaveManager
{
public:
    SaveManager();
    virtual ~SaveManager();

	void	CheckSaves(void);
	void	RunSaveThread(void);
	void	AddSaveMessage(short save_code, long player_id, short bytes, unsigned char *data);

	// Phase AM (AM-8): periodically UPSERT the singleton server_status row
	// (boot_time / players_online / sectors_online) so the status-notifier
	// sidecar can answer a read-only `/status` without any inbound path into the
	// game process. Runs on the SaveManager thread (shares m_SQL_Conn, like every
	// Handle*). No-op unless NET7_EXTERNAL_STATUS_ENABLED. Throttled internally.
	void	WriteServerStatusHeartbeat(void);

private:
	void	HandleSaveCode(short save_code, long player_id, short bytes, unsigned char *data);
	void	HandleNewRecipe(long player_id, short bytes, unsigned char *data);
	void	HandleManufactureAttempt(long player_id, short bytes, unsigned char *data);
	void	HandleAdvanceLevel(long player_id, short bytes, unsigned char *data);
	void	HandleAdvanceSkill(long player_id, short bytes, unsigned char *data);
	void	HandleChangeInventory(long player_id, short bytes, unsigned char *data);
	void	HandleMoveInventory(long player_id, short bytes, unsigned char *data);
	// Upsert ONE 86-byte inventory/vault/trade slot record through `q`. When
	// `q` is mid-transaction (begin() called) the write defers to that
	// transaction's commit; otherwise it autocommits. Returns false on SQL
	// failure (already logged). Shared by HandleChangeInventory (single slot,
	// autocommit) and HandleMoveInventory (two slots, one transaction).
	bool	UpsertInventorySlot(sql_query_c &q, long player_id, const unsigned char *rec);
	void	HandleChangeEquipment(long player_id, short bytes, unsigned char *data);
	void	HandleAwardXP(long player_id, short bytes, unsigned char *data);
	void	HandleCreditChange(long player_id, short bytes, unsigned char *data);
	void	HandleStorePosition(long player_id, short bytes, unsigned char *data);
	void	HandleAdvanceMission(long player_id, short bytes, unsigned char *data);
	void	HandleAdvanceMissionFlags(long player_id, short bytes, unsigned char *data);
	void	HandleChangeAmmo(long player_id, short bytes, unsigned char *data);
	void	HandleHullUpgrade(long player_id, short bytes, unsigned char *data);
	void	HandleHullLevelChange(long player_id, short bytes, unsigned char *data);
	void	HandleMissionRemove(long player_id, short bytes, unsigned char *data);
	void	HandleMissionComplete(long player_id, short bytes, unsigned char *data);
	void	HandleWipeCharacter(long player_id);
	void	HandleFullWipeCharacter(long player_id);
	void	HandleDiscoverNav(long player_id, short bytes, unsigned char *data);
	void	HandleExploreNav(long player_id, short bytes, unsigned char *data);
	void	HandleSetSkillPoints(long player_id, short bytes, unsigned char *data);
	void	HandleSetRegisteredStarbase(long player_id, short bytes, unsigned char *data);
	void	HandleSaveEnergyLevels(long player_id, short bytes, unsigned char *data);
	void	HandleUpdateDatabase(long player_id, short bytes, unsigned char *data);
	void	HandleFactionUpdate(long player_id, short bytes, unsigned char *data);
	void	HandleFullFactionWipe(long player_id, short bytes, unsigned char *data);
	void	HandleLogin(long player_id, short bytes, unsigned char *data);
	void	HandelInfraction(long player_id, short bytes, unsigned char *data);
	void	HandleLogout(long player_id, short bytes, unsigned char *data);
	void	HandleFriendsList(long player_id, short bytes, unsigned char *data);
	void	HandleIgnoreList(long player_id, short bytes, unsigned char *data);
	void	HandlePetition(long player_id, short bytes, unsigned char *data);
	void	HandleDatabase(long player_id, short bytes, unsigned char *data);
	void	HandleNewWarnLevel(long player_id, short bytes, unsigned char *data);
	void	HandleXPDebt(long player_id, short bytes, unsigned char *data);
	void	HandleGuildId(long player_id, short bytes, unsigned char *data);
	void	HandleGuildMember(long guild_id, short bytes, unsigned char *data);
	void	HandleGuildRank(long guild_id, short bytes, unsigned char *data);
	void	HandleGuildInfo(long guild_id, short bytes, unsigned char *data);
	void 	HandleDeleteGuild(long guild_id);
	void	HandleChangeFieldRespawn(short bytes, unsigned char *data);
	void	HandleExternalStatusEvent(long player_id, short bytes, unsigned char *data);
	// Phase AQ. Persist one sector-transition row into gate_events (net7_user)
	// for the website's galaxy-traffic animation. Runs on the SaveManager thread
	// (shares m_SQL_Conn like every Handle*). Payload is two packed int32s:
	// [from_sector][to_sector]; player_id carries the moving avatar_id.
	void	HandleGateEvent(long player_id, short bytes, unsigned char *data);

private:
	MessageQueue    * m_SaveQueue;
	sql_connection_c  m_SQL_Conn;
	sql_connection_c  m_SQL_Conn_main;

	char			  m_QueryStr[512];
	CircularBuffer  * m_SaveBuffer;
	bool m_ThreadRunning;
	pthread_t m_Thread;
	unsigned long	  m_LastHeartbeatTick;   // AM-8: last server_status UPSERT (GetNet7TickCount ms)
};

// Enqueue an external-status event (relayed out-of-process by the
// status-notifier sidecar, e.g. to a Discord channel). No-op unless
// NET7_EXTERNAL_STATUS_ENABLED is set (cached once) and the SaveManager is up.
// Best-effort: a delivery failure must never stall or fail a
// login/logout/level-up/chat. Callable from any gameplay thread (it only
// touches the lock-free save queue, exactly like SaveLogout). `content` is the
// final message line; it is truncated to fit the save slot.
void EmitExternalStatusEvent(unsigned char kind, const char *content);

// Phase AQ. Enqueue a sector-transition (gate / undock / dock) for the website's
// galaxy-traffic animation. On by default (NET7_GATE_EVENTS_ENABLED=0 disables
// it); no-op unless the SaveManager is up. Best-effort and off the DB thread, exactly
// like EmitExternalStatusEvent: a failure here must never stall a handoff.
// avatar_id is the moving character; from_sector/to_sector are host-order
// sector ids (a self-hop from == to is dropped by the caller).
void EmitGateEvent(long avatar_id, long from_sector, long to_sector);

struct EnbSaveHeader
{
    short   size;
    short   save_code;
	long	player_id;
} ATTRIB_PACKED;

#define SAVE_CODE_ADVANCE_LEVEL						0x0001
#define SAVE_CODE_ADVANCE_SKILL						0x0002
#define SAVE_CODE_CHANGE_INVENTORY					0x0003
#define SAVE_CODE_CHANGE_EQUIPMENT					0x0004
#define SAVE_CODE_AWARD_XP							0x0005
#define SAVE_CODE_CREDIT_LEVEL						0x0006
#define SAVE_CODE_STORE_POSITION					0x0007
#define SAVE_CODE_ADVANCE_MISSION					0x0008
#define SAVE_CODE_CHARACTER_PROGRESS_WIPE			0x0009
#define SAVE_CODE_MISSION_FLAGS						0x000A
#define SAVE_CODE_CHANGE_AMMO						0x000B
#define SAVE_CODE_HULL_UPGRADE						0x000C
#define SAVE_CODE_HULL_LEVEL_CHANGE					0x000D
#define SAVE_CODE_FULL_CHARACTER_WIPE				0x000E
#define SAVE_CODE_MISSION_COMPLETE					0x000F
#define SAVE_CODE_DISCOVER_NAV						0x0010
#define SAVE_CODE_EXPLORE_NAV						0x0011
#define SAVE_CODE_SET_SKILLPOINTS					0x0012
#define	SAVE_CODE_SET_STARBASE						0x0013
#define SAVE_CODE_SET_ENERGY_LEVELS					0x0014
#define SAVE_CODE_UPDATE_DATABASE					0x0015
#define SAVE_CODE_FACTION_CHANGE					0x0016
#define SAVE_CODE_FULL_FACTION_WIPE					0x0017
#define SAVE_CODE_NEW_RECIPE						0x0018
#define SAVE_CODE_MISSION_REMOVE					0x0019
#define SAVE_CODE_LOGIN								0x0020 //DOH! Hexadecimal!
#define SAVE_CODE_LOGOUT							0x0021
#define SAVE_CODE_INFRACTION						0x0022
#define SAVE_CODE_FRIENDS_LIST						0x0023
#define SAVE_CODE_IGNORE_LIST						0x0024
#define SAVE_CODE_PETITION							0x0025
#define SAVE_CODE_DATABASE							0x0026
#define SAVE_CODE_NEW_ATTEMPT						0x0027
#define SAVE_CODE_NEW_WARN_LEVEL					0x0028
#define SAVE_CODE_XP_DEBT							0x0029
#define SAVE_CODE_GUILD_ID							0x002A
#define SAVE_CODE_GUILD_MEMBER						0x002B
#define SAVE_CODE_GUILD_RANK						0x002C
#define SAVE_CODE_GUILD_INFO						0x002D
#define SAVE_CODE_DELETE_GUILD						0x002E
#define SAVE_CODE_FIELD_RESPAWN_TIME				0x002F
// Phase AM. Not a save at all -- reuses the SaveManager queue purely because it
// owns the single serialized net7_user connection. The payload is [u8 kind]
// followed by the already-rendered UTF-8 message line; HandleExternalStatusEvent
// INSERTs it into external_status_events + NOTIFYs the consumer. player_id unused.
#define SAVE_CODE_EXT_STATUS_EVENT					0x0030
// Atomic inventory move. Payload is N back-to-back SAVE_CODE_CHANGE_INVENTORY
// slot records (86 bytes each); HandleMoveInventory persists ALL of them inside
// ONE DB transaction so a crash mid-move can neither duplicate nor lose the
// item. N == 2 for a plain source->destination swap (Player::SaveInventoryMove,
// cargo<->vault and vault<->vault). N > 2 for a vault->cargo auto-stack where
// one vault stack spreads across several cargo slots: the emptied vault source
// plus every touched cargo slot all commit together (Player::SaveInventoryMoveSlots).
#define SAVE_CODE_MOVE_INVENTORY					0x0031
// Phase AQ. Not a save -- reuses the SaveManager queue (which owns the single
// serialized net7_user connection) to append a sector-transition row to
// gate_events for the website's galaxy-traffic animation. player_id carries the
// moving avatar_id; the payload is two packed int32s [from_sector][to_sector].
// HandleGateEvent INSERTs the row. On by default (NET7_GATE_EVENTS_ENABLED=0
// disables it).
#define SAVE_CODE_GATE_EVENT						0x0032

// Wire size of one inventory/vault/trade slot record as packed by
// Player::SaveInventoryChange / SaveVaultChange / SaveTradeChange and parsed by
// SaveManager::UpsertInventorySlot: slot(1) + type(1) + stack_level(2) +
// trade_stack(2) + quality(4) + item_id(4) + cost(4) + structure(4) +
// builder_name(64) = 86. A SAVE_CODE_MOVE_INVENTORY payload is N of these.
#define SAVE_INVENTORY_RECORD_BYTES					86

// Maximum slot records one SAVE_CODE_MOVE_INVENTORY payload may carry. Bounded
// by AddSaveMessage's SAVE_MESSAGE_MAX_LENGTH (1306) buffer minus the 12-byte
// EnbSaveHeader: floor((1306 - 12) / 86) = 15. A move that would touch more
// slots than this (a single vault stack spreading across >14 cargo slots --
// pathological, needs that many partial stacks of the identical item already in
// cargo) falls back to independent per-slot saves at the call site, trading
// crash-atomicity for not dropping the save.
#define SAVE_MOVE_MAX_RECORDS						15

// external_status_events.kind bytes (wire-packed at data[0]; mapped to the kind
// text stored in the row). Keep in sync with HandleExternalStatusEvent + docs.
#define EXT_STATUS_LOGIN							1
#define EXT_STATUS_LOGOUT							2
#define EXT_STATUS_LEVELUP							3
#define EXT_STATUS_BROADCAST						4
#define EXT_STATUS_SERVER_START						5
#define EXT_STATUS_PLAYER_DESTROYED					6
#define EXT_STATUS_JUMPSTARTED						7

#define	PLAYER_VAULT								0x0001
#define PLAYER_INVENTORY							0x0002
#define PLAYER_TRADE								0x0004

#define DISCOVER_NAV								0x0001
#define EXPLORE_NAV									0x0002

#endif // _SAVE_MANAGER_H_INCLUDED_
