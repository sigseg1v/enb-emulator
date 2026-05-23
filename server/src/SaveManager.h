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

#include "Mutex.h"
#include "MessageQueue.h"
#include "mysql/mysqlplus.h"

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

private:
	void	HandleSaveCode(short save_code, long player_id, short bytes, unsigned char *data);
	void	HandleNewRecipe(long player_id, short bytes, unsigned char *data);
	void	HandleManufactureAttempt(long player_id, short bytes, unsigned char *data);
	void	HandleAdvanceLevel(long player_id, short bytes, unsigned char *data);
	void	HandleAdvanceSkill(long player_id, short bytes, unsigned char *data);
	void	HandleChangeInventory(long player_id, short bytes, unsigned char *data);
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

private:
	MessageQueue    * m_SaveQueue;
	sql_connection_c  m_SQL_Conn;
	sql_connection_c  m_SQL_Conn_main;

	char			  m_QueryStr[512];
	CircularBuffer  * m_SaveBuffer;
	bool m_ThreadRunning;
};

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

#define	PLAYER_VAULT								0x0001
#define PLAYER_INVENTORY							0x0002
#define PLAYER_TRADE								0x0004

#define DISCOVER_NAV								0x0001
#define EXPLORE_NAV									0x0002

#endif // _SAVE_MANAGER_H_INCLUDED_
