// SaveManager.cpp
// This class runs a thread which handles all the players' changes
// It keeps a connection to the SQL DB open
// Eventually this could become a separate process and run on another server PC.
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

#include "Net7.h"
#include "SaveManager.h"
#include "PacketStructures.h"
#include "PacketMethods.h"
#include "Guilds.h"
#include <float.h>

enum experience_type { XP_COMBAT, XP_EXPLORE, XP_TRADE };

#ifdef WIN32
void __cdecl LaunchSaveThread(void *sm)
#else
void * LaunchSaveThread(void *sm)
#endif
{
    ((SaveManager *)sm)->RunSaveThread();
#ifdef WIN32
    _endthread();
#else
    return NULL;
#endif
}

SaveManager::SaveManager()
{
	m_SaveBuffer = new CircularBuffer(0x80000, SAVE_SLOTS);  //save buffer is only user of this queue, therefore the 'slots' should be the same in both the buffer and queue
	m_SaveQueue = new MessageQueue("Save", m_SaveBuffer, SAVE_SLOTS, true); //check queue for any overlap corruption
	m_SQL_Conn.connect("net7_user", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
#ifdef WIN32
    _beginthread(LaunchSaveThread,65536,this);
#else
    //pthread_create(&m_Thread (??), NULL, &LaunchSaveThread, (void *) this);
#endif
}

SaveManager::~SaveManager()
{
	while (m_ThreadRunning)
		Sleep(100);
	CheckSaves();
	delete m_SaveQueue;
	delete m_SaveBuffer;
	m_SQL_Conn.disconnect();
}

void SaveManager::CheckSaves()
{
	int length;
	EnbSaveHeader *header;
	unsigned char msg[SAVE_MESSAGE_MAX_LENGTH]; //message will be under 256 or the 'AddMessage' method will not process it
	long player_id;

	while (m_SaveQueue->CheckQueue(msg, &length, SAVE_MESSAGE_MAX_LENGTH, &player_id)) //check if there are any messages in the queue, if there are write them into the 'msg' buffer
	{
		//process this message
		//format is opcode/length/message
		header = (EnbSaveHeader*)msg;
		unsigned char *data = (msg + sizeof(EnbSaveHeader));

		//process opcode accordingly
		HandleSaveCode(header->save_code, header->player_id, header->size, data);
	}
}

void SaveManager::RunSaveThread()
{
	m_ThreadRunning = true;
	while (!g_ServerShutdown)
	{
		Sleep(10);
		CheckSaves();
	}
	m_ThreadRunning = false;
}

void SaveManager::AddSaveMessage(short save_code, long player_id, short length, unsigned char *data)
{
    unsigned char pData[SAVE_MESSAGE_MAX_LENGTH];

    if (length + 4 > SAVE_MESSAGE_MAX_LENGTH)
    {
        LogMessage("AddSaveMessage message overflow: length = %d\n", length);
        return;
    }

    *((short*) &pData[0]) = length;
    *((short*) &pData[2]) = save_code;
	*((long* ) &pData[4]) = player_id;

	if (data)
	{
		memcpy(pData + sizeof(short)*2 + sizeof(long), data, length);
	}

	// "block" if the save queue is full
	// locking up the sector or login threads would be BAD
	// now handled automatically in the message queue system, as a warning "expand the queue" type thing
	/*if (m_SaveQueue->Count() == SAVE_SLOTS)
	{
		LogMessage(">>> Save Queue Full!, waiting for space <<<\n");
		while (m_SaveQueue->Count() == SAVE_SLOTS)
		{
			Sleep(10);
		}
	}*/

    m_SaveQueue->Add(pData, length+sizeof(EnbSaveHeader), player_id);
}

void SaveManager::HandleSaveCode(short save_code, long player_id, short bytes, unsigned char *data)
{
	switch (save_code)
	{
	case SAVE_CODE_INFRACTION:
		HandelInfraction(player_id, bytes, data);
		break;
	case SAVE_CODE_NEW_RECIPE:
		HandleNewRecipe(player_id, bytes, data);
		break;
	case SAVE_CODE_NEW_ATTEMPT:
		HandleManufactureAttempt(player_id, bytes, data);
		break;
	case SAVE_CODE_ADVANCE_LEVEL:
		HandleAdvanceLevel(player_id, bytes, data);
		break;
	case SAVE_CODE_ADVANCE_SKILL:
		HandleAdvanceSkill(player_id, bytes, data);
		break;
	case SAVE_CODE_CHANGE_INVENTORY:
		HandleChangeInventory(player_id, bytes, data);
		break;
	case SAVE_CODE_CHANGE_EQUIPMENT:
		HandleChangeEquipment(player_id, bytes, data);
		break;
	case SAVE_CODE_AWARD_XP:
		HandleAwardXP(player_id, bytes, data);
		break;
	case SAVE_CODE_CREDIT_LEVEL:
		HandleCreditChange(player_id, bytes, data);
		break;
	case SAVE_CODE_STORE_POSITION:
		HandleStorePosition(player_id, bytes, data);
		break;
	case SAVE_CODE_ADVANCE_MISSION:
		HandleAdvanceMission(player_id, bytes, data);
		break;
	case SAVE_CODE_CHARACTER_PROGRESS_WIPE:
		HandleWipeCharacter(player_id);
		break;
	case SAVE_CODE_FULL_CHARACTER_WIPE:
		HandleFullWipeCharacter(player_id);
		break;
	case SAVE_CODE_MISSION_FLAGS:
		HandleAdvanceMissionFlags(player_id, bytes, data);
		break;
	case SAVE_CODE_CHANGE_AMMO:
		HandleChangeAmmo(player_id, bytes, data);
		break;
	case SAVE_CODE_HULL_UPGRADE:
		HandleHullUpgrade(player_id, bytes, data);
		break;
	case SAVE_CODE_HULL_LEVEL_CHANGE:
		HandleHullLevelChange(player_id, bytes, data);
		break;
	case SAVE_CODE_MISSION_REMOVE:
		HandleMissionRemove(player_id, bytes, data);
		break;
	case SAVE_CODE_MISSION_COMPLETE:
		HandleMissionComplete(player_id, bytes, data);
		break;
	case SAVE_CODE_DISCOVER_NAV:
		HandleDiscoverNav(player_id, bytes, data);
		break;
	case SAVE_CODE_EXPLORE_NAV:
		HandleExploreNav(player_id, bytes, data);
		break;
	case SAVE_CODE_SET_SKILLPOINTS:
		HandleSetSkillPoints(player_id, bytes, data);
		break;
	case SAVE_CODE_SET_STARBASE:
		HandleSetRegisteredStarbase(player_id, bytes, data);
		break;
	case SAVE_CODE_SET_ENERGY_LEVELS:
		HandleSaveEnergyLevels(player_id, bytes, data);
		break;
	case SAVE_CODE_UPDATE_DATABASE:
		HandleUpdateDatabase(player_id, bytes, data);
		break;
	case SAVE_CODE_FACTION_CHANGE:
		HandleFactionUpdate(player_id, bytes, data);
		break;
	case SAVE_CODE_FULL_FACTION_WIPE:
		HandleFullFactionWipe(player_id, bytes, data);
		break;
	case SAVE_CODE_LOGIN:
		HandleLogin(player_id, bytes, data);
		break;
	case SAVE_CODE_LOGOUT:
		HandleLogout(player_id, bytes, data);
		break;
	case SAVE_CODE_FRIENDS_LIST:
		HandleFriendsList(player_id, bytes, data);
		break;
	case SAVE_CODE_IGNORE_LIST:
		HandleIgnoreList(player_id, bytes, data);
		break;
	case SAVE_CODE_PETITION:
		HandlePetition(player_id, bytes, data);
		break;
	case SAVE_CODE_DATABASE:
		HandleDatabase(player_id, bytes, data);
		break;
	case SAVE_CODE_NEW_WARN_LEVEL:
		HandleNewWarnLevel(player_id, bytes, data);
		break;
	case SAVE_CODE_XP_DEBT:
		HandleXPDebt(player_id, bytes, data);
		break;
	case SAVE_CODE_GUILD_ID:
		HandleGuildId(player_id, bytes, data);
		break;
	case SAVE_CODE_GUILD_MEMBER:
		HandleGuildMember(player_id, bytes, data);
		break;
	case SAVE_CODE_GUILD_RANK:
		HandleGuildRank(player_id, bytes, data);
		break;
	case SAVE_CODE_GUILD_INFO:
		HandleGuildInfo(player_id, bytes, data);
		break;
	case SAVE_CODE_DELETE_GUILD:
		HandleDeleteGuild(player_id);
		break;
	case SAVE_CODE_FIELD_RESPAWN_TIME:
		HandleChangeFieldRespawn(bytes, data);
		break;
	default:
		LogMessage( "Bad save code : %d for player %x\n", save_code, (player_id&0x00FFFFFF) );
		break;
	}
}

void SaveManager::HandelInfraction(long player_id, short bytes, unsigned char *data)
{
	sql_query_c account_query (&m_SQL_Conn);

	long account_id  = *((long *) &data[0]);
	long inc_ammount = *((long *) &data[4]);
	char msg[256];
	memcpy(msg, &data[8], 256);

	//we need to create a new entry
	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "CALL net7_user.incWarn('%d','%d','%s','%d')", account_id, player_id, msg, inc_ammount);

	LogMessage("SQL CALL: %s\n", m_QueryStr);

	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleLogin(long player_id, short bytes, unsigned char *data)
{
	sql_query_c account_query (&m_SQL_Conn);
	char timestr[32];

	memcpy(timestr, &data[0], 32);

	//we need to create a new entry
	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "CALL net7_user.avaLogin(%d,'%s')", player_id, timestr);

	LogMessage("SQL CALL: %s\n", m_QueryStr);

	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleLogout(long player_id, short bytes, unsigned char *data)
{
	sql_query_c account_query (&m_SQL_Conn);
	char timestr[32];

	memcpy(timestr, &data[0], 32);

	//we need to create a new entry
	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "CALL net7_user.avaLogout(%d,'%s')", player_id, timestr);

	LogMessage("SQL CALL: %s\n", m_QueryStr);

	account_query.run_query(m_QueryStr);

	time_t now;
	time( &now );

	__int64 time_conv = (__int64)now;

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
		"UPDATE net7_user.avatar_info SET last_logout_t = '%I64d' WHERE avatar_id = '%d'",
		time_conv, player_id);

	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleNewRecipe(long player_id, short bytes, unsigned char *data)
{
	// player learnt a new recipe
	sql_query_c account_query (&m_SQL_Conn);

	long item_id = *((long *) &data[0]);

	//we need to create a new entry
	sql_query SkillBuilder;
	SkillBuilder.Clear();
	SkillBuilder.SetTable("avatar_recipes");
	SkillBuilder.AddData("avatar_id", player_id);
	SkillBuilder.AddData("item_id", item_id);

	if (!account_query.run_query(SkillBuilder.CreateQuery()))
	{
		LogMessage("Could not save recipes Info for id %d, %s\n", player_id, account_query.ErrorMsg());
	}

	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleManufactureAttempt(long player_id, short bytes, unsigned char *data)
{
	// player manufactured an item
	sql_query_c account_query (&m_SQL_Conn);

	long item_id = *((long *) &data[0]);
	float quality = (*(float *) &data[4]);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
		"UPDATE net7_user.avatar_recipes SET avg_quality = (avg_quality*attempts+%f)/(attempts+1), attempts=attempts+1 WHERE avatar_id = '%d' AND item_id = '%d'",
		quality, player_id, item_id);
	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleAdvanceLevel(long player_id, short bytes, unsigned char *data)
{
	//player just levelled up.
	sql_query_c account_query (&m_SQL_Conn);

	u8 xp_type = *((u8 *) &data[0]);
	long new_level = *((long *) &data[1]);

	switch (xp_type)
	{
    case XP_COMBAT:
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
			"UPDATE net7_user.avatar_info SET combat = '%d' WHERE avatar_id = '%d'", new_level, player_id);
        break;

    case XP_EXPLORE:
		sprintf_s(m_QueryStr, sizeof(m_QueryStr),
			"UPDATE net7_user.avatar_info SET explore = '%d' WHERE avatar_id = '%d'", new_level, player_id);
        break;

    case XP_TRADE:
		sprintf_s(m_QueryStr, sizeof(m_QueryStr),
			"UPDATE net7_user.avatar_info SET trade = '%d' WHERE avatar_id = '%d'", new_level, player_id);
		break;
	}

	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleAdvanceSkill(long player_id, short bytes, unsigned char *data)
{
	//player has just increased a skill
	sql_query_c account_query (&m_SQL_Conn);
	sql_result_c result;

	short skill_id = *((short *) &data[0]);
	short new_level= *((short *) &data[2]);

	//does this skill exist in the DB?  //TODO:: create this entry when the skill is awarded, then just run 'UPDATE' queries on it.
	sprintf_s(m_QueryStr, sizeof(m_QueryStr),
		"SELECT * FROM net7_user.avatar_skill_levels WHERE avatar_id = '%d' AND skill_id = '%d'", player_id, skill_id);
	account_query.execute(m_QueryStr);
	account_query.store(&result);

	if (result.n_rows() != 0)
	{
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
			"UPDATE net7_user.avatar_skill_levels SET skill_level = '%d' WHERE avatar_id = '%d' AND skill_id = '%d'", 
			new_level, player_id, skill_id);
		account_query.run_query(m_QueryStr);
	}
	else
	{
		//we need to create a new entry
		sql_query SkillBuilder;
		SkillBuilder.Clear();
		SkillBuilder.SetTable("avatar_skill_levels");

		SkillBuilder.AddData("avatar_id", player_id);
		SkillBuilder.AddData("skill_id", skill_id);
		SkillBuilder.AddData("skill_level", new_level);

		if (!account_query.run_query(SkillBuilder.CreateQuery()))
		{
			LogMessage("Could not save Skill Info for id %d, %s\n", player_id, account_query.ErrorMsg());
		}
	}
}

void SaveManager::HandleChangeInventory(long player_id, short bytes, unsigned char *data)
{
	//player has just had an inventory change of some sort
	sql_query_c account_query (&m_SQL_Conn);
	sql_result_c result;

	u8 inventory_slot = *((u8 *) &data[0]);
	u8 inventory_type = *((u8 *) &data[1]);
	short stack_level	 = *((short *) &data[2]);
	short trade_stack	 = *((short *) &data[4]);
	float quality		 = *((float *) &data[6]);
	long  item_id		 = *((long *)  &data[10]);
	unsigned long cost	 = *((unsigned long *)   &data[14]);
	float structure		 = *((float *) &data[18]);
	char builder_name[64];

	// Copy builder Name
	memcpy(builder_name, &data[22], 64);

	switch (inventory_type)
	{
	case PLAYER_INVENTORY:
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
			"SELECT * FROM net7_user.avatar_inventory_items WHERE avatar_id = '%d' AND inventory_slot = '%d'", 
			player_id, inventory_slot);
		break;

	case PLAYER_VAULT:
		sprintf_s(m_QueryStr, sizeof(m_QueryStr),
			"SELECT * FROM net7_user.avatar_vault_items WHERE avatar_id = '%d' AND inventory_slot = '%d'", 
			player_id, inventory_slot);
		break;

	case PLAYER_TRADE:
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
			"SELECT * FROM net7_user.avatar_trade_items WHERE avatar_id = '%d' AND inventory_slot = '%d'", 
			player_id, inventory_slot);
		break;
	}

	//does this item exist in the DB?
	account_query.execute(m_QueryStr);
	account_query.store(&result);

	if (result.n_rows() != 0)
	{
		//yes, just update the item
		switch (inventory_type)
		{
		case PLAYER_INVENTORY:
			sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
				"UPDATE net7_user.avatar_inventory_items SET item_id = '%d', stack_level = '%d', trade_stack = '%d',"
				"quality = '%f', cost = '%d', builder_name ='%s', structure = '%f' WHERE avatar_id = '%d' AND "
				"inventory_slot = '%d'",
				item_id, stack_level, trade_stack, quality, cost, builder_name, structure, player_id, inventory_slot);
			break;

		case PLAYER_VAULT:
			sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
				"UPDATE net7_user.avatar_vault_items SET item_id = '%d', stack_level = '%d', trade_stack = '%d', "
				"quality = '%f', cost = '%d', builder_name ='%s', structure = '%f' WHERE avatar_id = '%d' AND "
				"inventory_slot = '%d'",
				item_id, stack_level, trade_stack, quality, cost, builder_name, structure, player_id, inventory_slot);
			break;

		case PLAYER_TRADE:
			sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
				"UPDATE net7_user.avatar_trade_items SET item_id = '%d', stack_level = '%d', trade_stack = '%d', "
				"quality = '%f', cost = '%d', builder_name ='%s', structure = '%f' WHERE avatar_id = '%d' AND "
				"inventory_slot = '%d'",
				item_id, stack_level, trade_stack, quality, cost, builder_name, structure, player_id, inventory_slot);
			break;
		}

		account_query.run_query(m_QueryStr);
	}
	else
	{
		//we need to create a new entry for this item, that's ok
		sql_query ItemBuilder;
		ItemBuilder.Clear();

		switch (inventory_type)
		{
		case PLAYER_INVENTORY:
			ItemBuilder.SetTable("avatar_inventory_items");
			break;

		case PLAYER_VAULT:
			ItemBuilder.SetTable("avatar_vault_items");
			break;

		case PLAYER_TRADE:
			ItemBuilder.SetTable("avatar_trade_items");
			break;
		}

		ItemBuilder.AddData("avatar_id", player_id);
		ItemBuilder.AddData("item_id", item_id);
		ItemBuilder.AddData("stack_level", stack_level);
		ItemBuilder.AddData("trade_stack", trade_stack);
		ItemBuilder.AddData("quality", quality);
		ItemBuilder.AddData("inventory_slot", inventory_slot);
		ItemBuilder.AddData("cost", cost);
		ItemBuilder.AddData("structure", structure);
		ItemBuilder.AddData("builder_name", builder_name);

		if (!account_query.run_query(ItemBuilder.CreateQuery()))
		{
			LogMessage("Could not save Inventory Info for id %d [item id %d], %s\n", player_id, item_id, account_query.ErrorMsg());
		}
	}
}

void SaveManager::HandleChangeEquipment(long player_id, short bytes, unsigned char *data)
{
	//player has just had an equipment change
	sql_query_c account_query (&m_SQL_Conn);
	sql_result_c result;
	char builder_name[64];

	u8 equipment_slot = *((u8 *) &data[0]);
	float quality		 = *((float *) &data[1]);
	long  item_id		 = *((long *)  &data[5]);
	float structure		 = *((float *)  &data[9]);
	// Copy builder Name
	memcpy(builder_name, &data[13], 64);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
		"SELECT * FROM net7_user.avatar_equipment WHERE avatar_id = '%d' AND equipment_slot = '%d'", 
		player_id, equipment_slot);

	//does this item exist in the DB?
	account_query.execute(m_QueryStr);
	account_query.store(&result);

	if (result.n_rows() != 0)
	{
		//yes, just update the equipment slot
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
			"UPDATE net7_user.avatar_equipment SET item_id = '%d', quality = '%f', builder_name = '%s', "
			"structure = '%f' WHERE avatar_id = '%d' AND equipment_slot = '%d'",
			item_id, quality, builder_name, structure, player_id, equipment_slot);

		if (!account_query.run_query(m_QueryStr))
		{
			LogMessage("Could not update Equip Info for id %d, %s\n", player_id, account_query.ErrorMsg());
		}

	}
	else
	{
		//we need to create a new entry for this item, that's ok
		sql_query EquipBuilder;
		EquipBuilder.Clear();
		EquipBuilder.SetTable("avatar_equipment");

		EquipBuilder.AddData("avatar_id", player_id);
		EquipBuilder.AddData("item_id", item_id);
		EquipBuilder.AddData("quality", quality);
		EquipBuilder.AddData("equipment_slot", equipment_slot);
		EquipBuilder.AddData("structure", structure);
		EquipBuilder.AddData("builder_name", builder_name);

		if (!account_query.run_query(EquipBuilder.CreateQuery()))
		{
			LogMessage("Could not save Equip Info for id %d, %s\n", player_id, account_query.ErrorMsg());
		}
	}
}

void SaveManager::HandleChangeAmmo(long player_id, short bytes, unsigned char *data)
{
	//player has just had an ammo change
	sql_query_c account_query (&m_SQL_Conn);
	sql_result_c result;
	char builder_name[64];

	u8 equipment_slot = *((u8 *) &data[0]);
	short ammo_stack	 = *((short *) &data[1]);
	float quality		 = *((float *) &data[3]);
	long  item_id		 = *((long *)  &data[7]);
	float structure      = *((float *) &data[11]);
	// Copy builder Name
	memcpy(builder_name, &data[15], 64);

	// Sanity check for negative ammo
	if (ammo_stack < 0)
	{
		ammo_stack = 0;
		LogMessage("ERROR: negative ammo_stack for %d\n", player_id );
	}

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
		"SELECT * FROM net7_user.avatar_ammo WHERE avatar_id = '%d' AND equipment_slot = '%d'", player_id, equipment_slot);

	//does this item exist in the DB?
	account_query.execute(m_QueryStr);
	account_query.store(&result);

	if (result.n_rows() != 0)
	{
		//yes, just update the equipment slot
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
			"UPDATE net7_user.avatar_ammo SET item_id = '%d', quality = '%f', ammo_stack = '%d', structure = '%f', "
			"builder_name = '%s' WHERE avatar_id = '%d' AND equipment_slot = '%d'",
			item_id, quality, ammo_stack, structure, builder_name, player_id, equipment_slot);

		account_query.run_query(m_QueryStr);
	}
	else
	{
		//we need to create a new entry for this item, that's ok
		sql_query EquipBuilder;
		EquipBuilder.Clear();
		EquipBuilder.SetTable("avatar_ammo");

		EquipBuilder.AddData("avatar_id", player_id);
		EquipBuilder.AddData("item_id", item_id);
		EquipBuilder.AddData("quality", quality);
		EquipBuilder.AddData("equipment_slot", equipment_slot);
		EquipBuilder.AddData("ammo_stack", ammo_stack);
		EquipBuilder.AddData("structure", structure);
		EquipBuilder.AddData("builder_name", builder_name);

		if (!account_query.run_query(EquipBuilder.CreateQuery()))
		{
			LogMessage("Could not save Ammo Info for id %d, %s\n", player_id, account_query.ErrorMsg());
		}
	}
}

void SaveManager::HandleAwardXP(long player_id, short bytes, unsigned char *data)
{
	//player just received XP, bar is now at a new level
	sql_query_c account_query (&m_SQL_Conn);

	u8 xp_type = *((u8 *) &data[0]);
	float new_level = *((float *) &data[1]);

	switch (xp_type)
	{
    case XP_COMBAT:
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
			"UPDATE net7_user.avatar_level_info SET combat_bar_level = '%f' WHERE avatar_id = '%d'", new_level, player_id);
        break;

    case XP_EXPLORE:
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
			"UPDATE net7_user.avatar_level_info SET explore_bar_level = '%f' WHERE avatar_id = '%d'", new_level, player_id);
        break;

    case XP_TRADE:
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
			"UPDATE net7_user.avatar_level_info SET trade_bar_level = '%f' WHERE avatar_id = '%d'", new_level, player_id);
		break;
	}

	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleUpdateDatabase(long player_id, short bytes, unsigned char *data)
{
	//player just changed sectors or logged out
	sql_query_c account_query (&m_SQL_Conn);

	u32 sector_id = *((u32 *) &data[0]);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
		"UPDATE net7_user.avatar_info SET sector = '%d' WHERE avatar_id = '%d'", sector_id, player_id);

	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleCreditChange(long player_id, short bytes, unsigned char *data)
{
	//player just received XP, bar is now at a new level
	sql_query_c account_query (&m_SQL_Conn);

	u64 credits = *((u64 *) &data[0]);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr),
		"UPDATE net7_user.avatar_level_info SET credits = '%I64d' WHERE avatar_id = '%d'", credits, player_id);

	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleStorePosition(long player_id, short bytes, unsigned char *data)
{
	//get the position stored
	//get position out of data
	float position[3];
	float orientation[4];
	long sector_id;

	position[0] = *((float *) &data[0]);
	position[1] = *((float *) &data[4]);
	position[2] = *((float *) &data[8]);

	orientation[0] = *((float *) &data[12]);
	orientation[1] = *((float *) &data[16]);
	orientation[2] = *((float *) &data[20]);
	orientation[3] = *((float *) &data[24]);

	sector_id = *((long *) &data[28]);

	//now store data into DB

	sql_query_c account_query (&m_SQL_Conn);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
		"UPDATE net7_user.avatar_position SET posx = '%.2f', posy = '%.2f', posz = '%.2f', ori_w = '%f', ori_x = '%f', "
		"ori_y = '%f', ori_z = '%f', sector_id = '%d' WHERE avatar_id = '%d'",
		position[0], position[1], position[2], orientation[0], orientation[1], orientation[2], orientation[3], 
		sector_id, player_id);

	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleAdvanceMission(long player_id, short bytes, unsigned char *data)
{
	//player has just either been awarded a mission or has advanced in one
	sql_query_c account_query (&m_SQL_Conn);
	sql_result_c result;

	u8		mission_slot	= *((u8 *) &data[0]);
	long	mission_id		= *((long *) &data[1]);
	short	mission_stage	= *((short *) &data[5]);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
		"SELECT * FROM net7_user.avatar_mission_progress WHERE avatar_id = '%d' AND mission_slot = '%d'", 
		player_id, mission_slot);

	//does this item exist in the DB?
	account_query.execute(m_QueryStr);
	account_query.store(&result);

	if (result.n_rows() != 0)
	{
		//yes, just update this mission and blank the mission flags
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
			"UPDATE net7_user.avatar_mission_progress SET stage_num = '%d', mission_flags = '0' WHERE avatar_id = "
			"'%d' AND mission_slot = '%d'",
			mission_stage, player_id, mission_slot);

		account_query.run_query(m_QueryStr);
	}
	else
	{
		//we need to create a new entry for this mission
		sql_query MissionBuilder;
		MissionBuilder.Clear();
		MissionBuilder.SetTable("avatar_mission_progress");

		MissionBuilder.AddData("avatar_id", player_id);
		MissionBuilder.AddData("mission_id", mission_id);
		MissionBuilder.AddData("mission_slot", mission_slot);
		MissionBuilder.AddData("mission_flags", 0);
		MissionBuilder.AddData("stage_num", mission_stage);

		if (!account_query.run_query(MissionBuilder.CreateQuery()))
		{
			LogMessage("Could not save Mission Info for id %d, %s\n", player_id, account_query.ErrorMsg());
		}
	}
}

void SaveManager::HandleAdvanceMissionFlags(long player_id, short bytes, unsigned char *data)
{
	//player has just had mission flags changed. This is only relevant if the mission exists in the DB
	//so just do a simple DB commit.
	sql_query_c account_query (&m_SQL_Conn);
	sql_result_c result;

	u8		mission_slot	= *((u8 *) &data[0]);
	long	mission_flags	= *((long *) &data[1]);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
		"UPDATE net7_user.avatar_mission_progress SET mission_flags = '%d' WHERE avatar_id = '%d' AND mission_slot = '%d'",
		mission_flags, player_id, mission_slot);

	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleHullUpgrade(long player_id, short bytes, unsigned char *data)
{
	//player has just had an hull upgrade change (or this is a new player)
	sql_query_c account_query (&m_SQL_Conn);
	sql_result_c result;

	u8 player_rank_name   = *((u8 *) &data[0]);
	u8 hull_upgrade_level = *((u8 *) &data[1]);
	float max_hull_points = *((float *) &data[2]);
	u8 cargo_space 		  = *((u8 *) &data[6]);
	u8 weapon_slots		  = *((u8 *) &data[7]);
	u8 device_slots		  = *((u8 *) &data[8]);
	u8 warp_power_level	  = *((u8 *) &data[9]);
	u8 engine_thrust_type = *((u8 *) &data[10]);


	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "SELECT * FROM net7_user.avatar_level_info WHERE avatar_id = '%d'", player_id);

	//does this item exist in the DB?
	account_query.execute(m_QueryStr);
	account_query.store(&result);

	if (result.n_rows() != 0)
	{
		//yes, just update the level info row
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
			"UPDATE net7_user.avatar_level_info SET player_rank_name = '%d', hull_upgrade_level = '%d', "
			"max_hull_points = '%.2f', cargo_space = '%d', weapon_slots = '%d', device_slots = '%d' WHERE avatar_id = '%d'",
			player_rank_name, hull_upgrade_level, max_hull_points, cargo_space, weapon_slots, device_slots, player_id);

		account_query.run_query(m_QueryStr);

		sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
			"UPDATE net7_user.avatar_level_info SET engine_thrust_type = '%d', warp_power_level = '%d' WHERE avatar_id = '%d'",
			engine_thrust_type, warp_power_level, player_id);

		account_query.run_query(m_QueryStr);
	}
	else
	{
		//we need to create a new entry for this player, that's ok
		sql_query LevelBuilder;
		LevelBuilder.Clear();
		LevelBuilder.SetTable("avatar_level_info");

		LevelBuilder.AddData("avatar_id", player_id);
		LevelBuilder.AddData("player_rank_name", player_rank_name);
		LevelBuilder.AddData("hull_upgrade_level", hull_upgrade_level);
		LevelBuilder.AddData("max_hull_points", max_hull_points);
		LevelBuilder.AddData("cargo_space", cargo_space);
		LevelBuilder.AddData("weapon_slots", weapon_slots);
		LevelBuilder.AddData("device_slots", device_slots);
		LevelBuilder.AddData("combat_bar_level", 0.0f);
		LevelBuilder.AddData("explore_bar_level", 0.0f);
		LevelBuilder.AddData("trade_bar_level", 0.0f);
		LevelBuilder.AddData("warp_power_level", warp_power_level);
		LevelBuilder.AddData("engine_thrust_type", engine_thrust_type);
		LevelBuilder.AddData("hull_points", max_hull_points);
		LevelBuilder.AddData("credits", 0);
		LevelBuilder.AddData("skill_points", 0); //skill

		if (!account_query.run_query(LevelBuilder.CreateQuery()))
		{
			LogMessage("Could not save Avatar level Info for id %d, %s\n", player_id, account_query.ErrorMsg());
		}
	}
}

void SaveManager::HandleHullLevelChange(long player_id, short bytes, unsigned char *data)
{
	//player has just had an hull level change
	sql_query_c account_query (&m_SQL_Conn);
	sql_result_c result;

	float hull_points = *((float *) &data[0]);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
		"UPDATE net7_user.avatar_level_info SET hull_points = '%.2f' WHERE avatar_id = '%d'",
		hull_points, player_id);

	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleMissionRemove(long player_id, short bytes, unsigned char *data)
{
	sql_query_c account_query (&m_SQL_Conn);

	long mission_id =  *((long *) &data[0]);

	//first remove mission progress
	sprintf_s(m_QueryStr, sizeof(m_QueryStr),
		"DELETE FROM net7_user.avatar_mission_progress WHERE avatar_id = '%d' AND mission_id = '%d'",player_id, mission_id);
	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleMissionComplete(long player_id, short bytes, unsigned char *data)
{
	//player has just completed a mission
	sql_query_c account_query (&m_SQL_Conn);
	sql_result_c result;

	long mission_id =  *((long *) &data[0]);
	u8 mission_flags = *((u8 *)   &data[4]);

	//first remove mission progress
	sprintf_s(m_QueryStr, sizeof(m_QueryStr),
		"DELETE FROM net7_user.avatar_mission_progress WHERE avatar_id = '%d' AND mission_id = '%d'",
		player_id, mission_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
		"SELECT * FROM net7_user.missions_completed WHERE avatar_id = '%d' AND mission_id = '%d'", player_id, mission_id);

	//does this item exist in the DB?
	account_query.execute(m_QueryStr);
	account_query.store(&result);

	if (result.n_rows() != 0)
	{
		//yes, just update the info row
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
			"UPDATE net7_user.missions_completed SET mission_completion_flags = '%d' WHERE avatar_id = '%d' "
			"AND mission_id = '%d'",
			mission_flags, player_id, mission_id);

		account_query.run_query(m_QueryStr);
	}
	else
	{
		sql_query MissionBuilder;
		MissionBuilder.Clear();
		MissionBuilder.SetTable("missions_completed");

		MissionBuilder.AddData("avatar_id", player_id);
		MissionBuilder.AddData("mission_id", mission_id);
		MissionBuilder.AddData("mission_completion_flags", mission_flags);

		account_query.run_query(MissionBuilder.CreateQuery());
	}
}

//this method leaves the base character design, name and ship but resets them back to a starting condition.
void SaveManager::HandleWipeCharacter(long player_id)
{
	//ok remove all records of inventory, equipment, ammo, skills, levels & rank info
	sql_query_c account_query (&m_SQL_Conn);

	//remove position info
	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_position` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_level_info` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_ammo` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_inventory_items` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_vault_items` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_equipment` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_mission_progress` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `missions_completed` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);
	
	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `faction_data` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);
	
	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_exploration` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_skill_levels` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
		"UPDATE net7_user.avatar_info SET combat = '0', explore = '0', trade = '0' WHERE avatar_id = '%d'", player_id);
	account_query.run_query(m_QueryStr);
}

//this method removes all trace of the avatar in the database, must be used when the avatar is deleted via the client avatar character selector.
void SaveManager::HandleFullWipeCharacter(long player_id)
{
	sql_query_c account_query (&m_SQL_Conn);

	//Delete this character from the tables
	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `ship_info` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `ship_data` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_data` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_info` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);


	//remove position info
	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_position` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_level_info` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_ammo` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_inventory_items` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_vault_items` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_equipment` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_mission_progress` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `missions_completed` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);
	
	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `faction_data` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);
	
	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_exploration` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_skill_levels` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_faction_level` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_recipes` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `ignore_lists` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `friends_lists` WHERE `avatar_id` = '%d'", player_id);
	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleFullFactionWipe(long player_id, short bytes, unsigned char *data)
{
	sql_query_c account_query (&m_SQL_Conn);

	//remove all faction data

	strcpy_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM faction_data");
	m_QueryStr[sizeof(m_QueryStr)-1] = '\0';
	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleDiscoverNav(long player_id, short bytes, unsigned char *data)
{
	//player has just discovered a nav, make a record
	sql_query_c account_query (&m_SQL_Conn);

	long object_uid =  *((long *) &data[0]);

	sql_query ExploreBuilder;
	ExploreBuilder.Clear();
	ExploreBuilder.SetTable("avatar_exploration");

	ExploreBuilder.AddData("avatar_id", player_id);
	ExploreBuilder.AddData("object_id", object_uid);
	ExploreBuilder.AddData("explore_flags", DISCOVER_NAV);

	account_query.run_query(ExploreBuilder.CreateQuery());
}

void SaveManager::HandleExploreNav(long player_id, short bytes, unsigned char *data)
{
	//player has just explored a nav, update the record
	sql_query_c account_query (&m_SQL_Conn);
	sql_result_c result;

	long object_uid =  *((long *) &data[0]);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
		"SELECT * FROM net7_user.avatar_exploration WHERE avatar_id = '%d' AND object_id = '%d'", player_id, object_uid);

	//does this item exist in the DB?
	account_query.execute(m_QueryStr);
	account_query.store(&result);

	if (result.n_rows() == 0)
	{  
		sprintf_s(m_QueryStr, sizeof(m_QueryStr),   // need to add a fresh entry here
			"INSERT INTO net7_user.avatar_exploration (avatar_id,object_id,explore_flags) VALUES ('%d','%d','%d')", 
			player_id, object_uid, EXPLORE_NAV);
	}
	else
	{
		sprintf_s(m_QueryStr, sizeof(m_QueryStr),   // entry exists, just update it
			"UPDATE net7_user.avatar_exploration SET explore_flags = '%d' WHERE avatar_id = '%d' AND object_id = '%d'",
			EXPLORE_NAV, player_id, object_uid);
	}

	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleSetSkillPoints(long player_id, short bytes, unsigned char *data)
{
	//set skill points
	sql_query_c account_query (&m_SQL_Conn);

	long skill_points =  *((long *) &data[0]);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
		"UPDATE net7_user.avatar_level_info SET skill_points = '%d' WHERE avatar_id = '%d'",
		skill_points, player_id);

	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleSetRegisteredStarbase(long player_id, short bytes, unsigned char *data)
{
	//set skill points
	sql_query_c account_query (&m_SQL_Conn);

	long registered_starbase =  *((long *) &data[0]);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
		"UPDATE net7_user.avatar_level_info SET registered_starbase = '%d' WHERE avatar_id = '%d'",
		registered_starbase, player_id);

	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleSaveEnergyLevels(long player_id, short bytes, unsigned char *data)
{
	//set skill points
	sql_query_c account_query (&m_SQL_Conn);

	float energy =  *((float *) &data[0]);
	float shield =  *((float *) &data[4]);

	if (_isnan(energy)) energy = 0.1f; //last ditch attempt to stop a crash.
	if (_isnan(shield)) shield = 0.1f;

	sprintf_s(m_QueryStr, sizeof(m_QueryStr),
		"UPDATE net7_user.avatar_level_info SET reactor_level = '%f', shield_level = '%f' WHERE avatar_id = '%d'",
		energy, shield, player_id);

	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleFactionUpdate(long player_id, short bytes, unsigned char *data)
{
	//player has just completed a mission
	sql_query_c account_query (&m_SQL_Conn);
	sql_result_c result;

	long faction_id =  *((short *) &data[0]);
	float faction_value = *((float *) &data[2]);
	long faction_order = -1;

	if (bytes > 6)
	{
		faction_order = *((short *) &data[6]);
	}

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
		"SELECT * FROM net7_user.faction_data WHERE avatar_id = '%d' AND faction_id = '%d'", player_id, faction_id);

	//does this item exist in the DB?
	account_query.execute(m_QueryStr);
	account_query.store(&result);

	if (result.n_rows() != 0)
	{
		//yes, just update the info row
		if (faction_order != -1)
		{
			sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
				"UPDATE net7_user.faction_data SET faction_order = '%d' AND faction_value = %.2f WHERE avatar_id = "
				"'%d' AND faction_id = '%d'",
				faction_order, faction_value, player_id, faction_id);
		}
		else
		{
			sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
				"UPDATE net7_user.faction_data SET faction_value = %.2f WHERE avatar_id = '%d' AND faction_id = '%d'",
				faction_value, player_id, faction_id);
		}

		account_query.run_query(m_QueryStr);
	}
	else
	{
		if (faction_order == -1)
		{
			LogMessage("FACTION SETUP ERROR: Adding New faction, order = -1... Faction ID = %d, value = %.2f\n", faction_id, faction_value);
			faction_order = faction_id;
		}

		sql_query FactionBuilder;
		FactionBuilder.Clear();
		FactionBuilder.SetTable("faction_data");

		FactionBuilder.AddData("avatar_id", player_id);
		FactionBuilder.AddData("faction_id", faction_id);
		FactionBuilder.AddData("faction_value", faction_value);
		FactionBuilder.AddData("faction_order", faction_order);

		account_query.run_query(FactionBuilder.CreateQuery());
	}
}

void SaveManager::HandleFriendsList(long player_id, short bytes, unsigned char *data)
{
	sql_query_c account_query (&m_SQL_Conn);

	bool add = *((bool *)&data[0]);
	unsigned char *name = &data[1];

	if (add)
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), "INSERT INTO friends_lists (avatar_id,name) VALUES ('%d','%s')", player_id, name);
	else
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM friends_lists WHERE avatar_id = '%d' AND name = '%s'", player_id, name);

	account_query.run_query(m_QueryStr);
}

void SaveManager::HandleIgnoreList(long player_id, short bytes, unsigned char *data)
{
	sql_query_c account_query (&m_SQL_Conn);

	bool add = *((bool *)&data[0]);
	unsigned char *name = &data[1];

	if (add)
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), "INSERT INTO ignore_lists (avatar_id,name) VALUES ('%d','%s')", player_id, name);
	else
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM ignore_lists WHERE avatar_id = '%d' AND name = '%s'", player_id, name);

	account_query.run_query(m_QueryStr);
}

void SaveManager::HandlePetition(long player_id, short bytes, unsigned char *data)
{
	sql_connection_c SQL_Conn;
	// Not sure theses pointers are safe??
	char queryString[2048],*email="unknown",*username="unknown",*name="unknown";

	// parse packet
	char *p = (char *)data;
	long GameID = *((long *) p);
	p += 4;
	long ProblemType = *((long *) p);
	p += 4;
	char *Subject = p;
	p += strlen(p) + 1;
	char *Complaint = p;
	p += strlen(p) + 1;
	char *PlayerList = p;

	// get player info out the database instead
	sql_query_c account_query (&m_SQL_Conn);
	sql_result_c account_result;      
	sql_row_c account_row;
	sprintf_s(queryString, sizeof(queryString),
		"SELECT t1.username,t1.email,t3.first_name FROM accounts AS t1 JOIN avatar_info AS t2 ON t1.id=t2.account_id \
		JOIN avatar_data AS t3 ON t2.avatar_id=t3.avatar_id WHERE t2.avatar_id=%d", player_id);

	if (account_query.run_query(queryString) && account_query.n_rows())
	{
		account_query.store(&account_result);
		account_result.fetch_row(&account_row);
		username = account_row[0];
		email = account_row[1];
		name = account_row[2];
	}

	// connect to ticket system
	SQL_Conn.connect(g_Ticket_DB , g_Ticket_Host, g_Ticket_User, g_Ticket_Pass);
	{
		sql_query_c Ticket(&SQL_Conn);
		// Get Escape strings to make sure we can use this data will work in the query
		// Also no way to hack it
		char Complaint_esc[2048], Subject_esc[2048], PlayerList_esc[2048];

		mysql_escape_string(Complaint_esc, Complaint, strlen(Complaint));
		mysql_escape_string(Subject_esc, Subject, strlen(Subject));
		mysql_escape_string(PlayerList_esc, PlayerList, strlen(PlayerList));

		sprintf_s(queryString, sizeof(queryString), "CALL %s.TicketViaServer('%s', '%s', '%s', '%s', '%s', '%s', %d)", g_Ticket_DB, username, name, email, Subject_esc, Complaint_esc, PlayerList_esc, ProblemType);
		queryString[sizeof(queryString)-1] = 0; // Make sure we put a null after this string
		Ticket.run_query(queryString);
	}
	// interesting side note if sql_query_c is in scope when disconnect is called
	// a freed heap block is written to and Windows breakpoints with a HEAP debug message in _endthread
	// bug in mysql interface?
	SQL_Conn.disconnect();
}

void SaveManager::HandleDatabase(long avatar_id, short bytes, unsigned char *data)
{
	CharacterDatabase *database = (CharacterDatabase *)data;
	sql_query_c account_query (&m_SQL_Conn);

	//We need to completely remove the character we are saving (in this order due to foreign keys)

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `ship_info` WHERE `avatar_id` = '%d'", avatar_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `ship_data` WHERE `avatar_id` = '%d'", avatar_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_data` WHERE `avatar_id` = '%d'", avatar_id);
	account_query.run_query(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM `avatar_info` WHERE `avatar_id` = '%d'", avatar_id);
	account_query.run_query(m_QueryStr);

	sql_query account_builder;

	///////////////////////////////////////////////////////////////////////////////////
	////////////////////////////    Save AvatarInfo    ////////////////////////////////
	///////////////////////////////////////////////////////////////////////////////////

	account_builder.Clear();
	account_builder.SetTable("avatar_info");

	account_builder.AddData("avatar_id", avatar_id);
	account_builder.AddData("account_id", ntohl(database->info.account_id_lsb));
	account_builder.AddData("slot", ntohl(database->info.avatar_slot));
	account_builder.AddData("sector", ntohl(database->info.sector_id));
	account_builder.AddData("galaxy", ntohl(database->info.galaxy_id));
	account_builder.AddData("count", ntohl(database->info.count));
	account_builder.AddData("admin", ntohl(database->info.admin_level));
	account_builder.AddData("combat", ntohl(database->info.combat_level));
	account_builder.AddData("explore", ntohl(database->info.explore_level));
	account_builder.AddData("trade", ntohl(database->info.trade_level));

	if (!account_query.run_query(account_builder.CreateQuery()))
	{
		LogMessage("Could not save AvatarInfo for id %d, %s\n", avatar_id, account_query.ErrorMsg());
	}

	///////////////////////////////////////////////////////////////////////////////////
	////////////////////////////    Save AvatarData    ////////////////////////////////
	///////////////////////////////////////////////////////////////////////////////////

	account_builder.Clear();
	account_builder.SetTable("avatar_data");

	account_builder.AddData("avatar_id", avatar_id);
	account_builder.AddData("first_name", database->avatar.avatar_first_name);
	account_builder.AddData("last_name", database->avatar.avatar_last_name);
	account_builder.AddData("type", database->avatar.avatar_type);
	account_builder.AddData("version", database->avatar.avatar_version);
	account_builder.AddData("race", database->avatar.race);
	account_builder.AddData("prof", database->avatar.profession);
	account_builder.AddData("gender", database->avatar.gender);
	account_builder.AddData("mood", database->avatar.mood_type);
	account_builder.AddData("personality", database->avatar.personality);
	account_builder.AddData("nlp", database->avatar.nlp);
	account_builder.AddData("body", database->avatar.body_type);
	account_builder.AddData("pants", database->avatar.pants_type);
	account_builder.AddData("head", database->avatar.head_type);
	account_builder.AddData("hair", database->avatar.hair_num);
	account_builder.AddData("ear", database->avatar.ear_num);
	account_builder.AddData("goggle", database->avatar.goggle_num);
	account_builder.AddData("beard", database->avatar.beard_num);
	account_builder.AddData("weapon_hip", database->avatar.weapon_hip_num);
	account_builder.AddData("weapon_unique", database->avatar.weapon_unique_num);
	account_builder.AddData("weapon_back", database->avatar.weapon_back_num);
	account_builder.AddData("head_texture", database->avatar.head_texture_num);
	account_builder.AddData("tattoo_texture", database->avatar.tattoo_texture_num);
	account_builder.AddData("tattoo_X", database->avatar.tattoo_offset[0]);
	account_builder.AddData("tattoo_Y", database->avatar.tattoo_offset[1]);
	account_builder.AddData("tattoo_Z", database->avatar.tattoo_offset[2]);
	account_builder.AddData("hair_H", database->avatar.hair_color[0]);
	account_builder.AddData("hair_S", database->avatar.hair_color[1]);
	account_builder.AddData("hair_V", database->avatar.hair_color[2]);
	account_builder.AddData("beard_H", database->avatar.beard_color[0]);
	account_builder.AddData("beard_S", database->avatar.beard_color[1]);
	account_builder.AddData("beard_V", database->avatar.beard_color[2]);
	account_builder.AddData("eye_H", database->avatar.eye_color[0]);
	account_builder.AddData("eye_S", database->avatar.eye_color[1]);
	account_builder.AddData("eye_V", database->avatar.eye_color[2]);
	account_builder.AddData("skin_H", database->avatar.skin_color[0]);
	account_builder.AddData("skin_S", database->avatar.skin_color[1]);
	account_builder.AddData("skin_V", database->avatar.skin_color[2]);
	account_builder.AddData("shirt_p_H", database->avatar.shirt_primary_color[0]);
	account_builder.AddData("shirt_p_S", database->avatar.shirt_primary_color[1]);
	account_builder.AddData("shirt_p_V", database->avatar.shirt_primary_color[2]);
	account_builder.AddData("shirt_s_H", database->avatar.shirt_secondary_color[0]);
	account_builder.AddData("shirt_s_S", database->avatar.shirt_secondary_color[1]);
	account_builder.AddData("shirt_s_V", database->avatar.shirt_secondary_color[2]);
	account_builder.AddData("pants_p_H", database->avatar.pants_primary_color[0]);
	account_builder.AddData("pants_p_S", database->avatar.pants_primary_color[1]);
	account_builder.AddData("pants_p_V", database->avatar.pants_primary_color[2]);
	account_builder.AddData("pants_s_H", database->avatar.pants_secondary_color[0]);
	account_builder.AddData("pants_s_S", database->avatar.pants_secondary_color[1]);
	account_builder.AddData("pants_s_V", database->avatar.pants_secondary_color[2]);
	account_builder.AddData("shirt_p_metal", database->avatar.shirt_primary_metal);
	account_builder.AddData("shirt_s_metal", database->avatar.shirt_secondary_metal);
	account_builder.AddData("pants_p_metal", database->avatar.pants_primary_metal);
	account_builder.AddData("pants_s_metal", database->avatar.pants_secondary_metal);
	account_builder.AddData("height_weight_0", database->avatar.height_weight_1[0]);
	account_builder.AddData("height_weight_1", database->avatar.height_weight_1[1]);
	account_builder.AddData("height_weight_2", database->avatar.height_weight_1[2]);
	account_builder.AddData("height_weight_3", database->avatar.height_weight_1[3]);
	account_builder.AddData("height_weight_4", database->avatar.height_weight_1[4]);

	if (!account_query.run_query(account_builder.CreateQuery()))
	{
		LogMessage("Could not save AvatarData for id %d, %s\n", avatar_id, account_query.ErrorMsg());
	}

	///////////////////////////////////////////////////////////////////////////////////
	/////////////////////////////    Save ShipData    /////////////////////////////////
	///////////////////////////////////////////////////////////////////////////////////

	account_builder.Clear();
	account_builder.SetTable("ship_data");

	account_builder.AddData("avatar_id", avatar_id);
	account_builder.AddData("race", database->ship_data.race);
	account_builder.AddData("prof", database->ship_data.profession);
	account_builder.AddData("hull", database->ship_data.hull);
	account_builder.AddData("wing", database->ship_data.wing);
	account_builder.AddData("decal", database->ship_data.decal);
	account_builder.AddData("name", database->ship_data.ship_name);
	account_builder.AddData("name_H", database->ship_data.ship_name_color[0]);
	account_builder.AddData("name_S", database->ship_data.ship_name_color[1]);
	account_builder.AddData("name_V", database->ship_data.ship_name_color[2]);
	account_builder.AddData("hull_p_H", database->ship_data.HullPrimaryColor.HSV[0]);
	account_builder.AddData("hull_p_S", database->ship_data.HullPrimaryColor.HSV[1]);
	account_builder.AddData("hull_p_V", database->ship_data.HullPrimaryColor.HSV[2]);
	account_builder.AddData("hull_p_flat", database->ship_data.HullPrimaryColor.flat);
	account_builder.AddData("hull_p_metal", database->ship_data.HullPrimaryColor.metal);
	account_builder.AddData("hull_s_H", database->ship_data.HullSecondaryColor.HSV[0]);
	account_builder.AddData("hull_s_S", database->ship_data.HullSecondaryColor.HSV[1]);
	account_builder.AddData("hull_s_V", database->ship_data.HullSecondaryColor.HSV[2]);
	account_builder.AddData("hull_s_flat", database->ship_data.HullSecondaryColor.flat);
	account_builder.AddData("hull_s_metal", database->ship_data.HullSecondaryColor.metal);
	account_builder.AddData("prof_p_H", database->ship_data.ProfessionPrimaryColor.HSV[0]);
	account_builder.AddData("prof_p_S", database->ship_data.ProfessionPrimaryColor.HSV[1]);
	account_builder.AddData("prof_p_V", database->ship_data.ProfessionPrimaryColor.HSV[2]);
	account_builder.AddData("prof_p_flat", database->ship_data.ProfessionPrimaryColor.flat);
	account_builder.AddData("prof_p_metal", database->ship_data.ProfessionPrimaryColor.metal);
	account_builder.AddData("prof_s_H", database->ship_data.ProfessionSecondaryColor.HSV[0]);
	account_builder.AddData("prof_s_S", database->ship_data.ProfessionSecondaryColor.HSV[1]);
	account_builder.AddData("prof_s_V", database->ship_data.ProfessionSecondaryColor.HSV[2]);
	account_builder.AddData("prof_s_flat", database->ship_data.ProfessionSecondaryColor.flat);
	account_builder.AddData("prof_s_metal", database->ship_data.ProfessionSecondaryColor.metal);
	account_builder.AddData("wing_p_H", database->ship_data.WingPrimaryColor.HSV[0]);
	account_builder.AddData("wing_p_S", database->ship_data.WingPrimaryColor.HSV[1]);
	account_builder.AddData("wing_p_V", database->ship_data.WingPrimaryColor.HSV[2]);
	account_builder.AddData("wing_p_flat", database->ship_data.WingPrimaryColor.flat);
	account_builder.AddData("wing_p_metal", database->ship_data.WingPrimaryColor.metal);
	account_builder.AddData("wing_s_H", database->ship_data.WingSecondaryColor.HSV[0]);
	account_builder.AddData("wing_s_S", database->ship_data.WingSecondaryColor.HSV[1]);
	account_builder.AddData("wing_s_V", database->ship_data.WingSecondaryColor.HSV[2]);
	account_builder.AddData("wing_s_flat", database->ship_data.WingSecondaryColor.flat);
	account_builder.AddData("wing_s_metal", database->ship_data.WingSecondaryColor.metal);
	account_builder.AddData("engine_p_H", database->ship_data.EnginePrimaryColor.HSV[0]);
	account_builder.AddData("engine_p_S", database->ship_data.EnginePrimaryColor.HSV[1]);
	account_builder.AddData("engine_p_V", database->ship_data.EnginePrimaryColor.HSV[2]);
	account_builder.AddData("engine_p_flat", database->ship_data.EnginePrimaryColor.flat);
	account_builder.AddData("engine_p_metal", database->ship_data.EnginePrimaryColor.metal);
	account_builder.AddData("engine_s_H", database->ship_data.EngineSecondaryColor.HSV[0]);
	account_builder.AddData("engine_s_S", database->ship_data.EngineSecondaryColor.HSV[1]);
	account_builder.AddData("engine_s_V", database->ship_data.EngineSecondaryColor.HSV[2]);
	account_builder.AddData("engine_s_flat", database->ship_data.EngineSecondaryColor.flat);
	account_builder.AddData("engine_s_metal", database->ship_data.EngineSecondaryColor.metal);

	if (!account_query.run_query(account_builder.CreateQuery()))
	{
		LogMessage("Could not save ShipData for id %d, %s\n", avatar_id, account_query.ErrorMsg());
	}

	///////////////////////////////////////////////////////////////////////////////////
	/////////////////////////////    Save ShipInfo    /////////////////////////////////
	///////////////////////////////////////////////////////////////////////////////////

	account_builder.Clear();
	account_builder.SetTable("ship_info");

	account_builder.AddData("avatar_id", avatar_id);
	account_builder.AddData("hull", database->ship_info.hull);
	account_builder.AddData("prof", database->ship_info.profession);
	account_builder.AddData("engine", database->ship_info.engine);
	account_builder.AddData("wing", database->ship_info.wing);
	account_builder.AddData("pos_0", database->ship_info.Position[0]);
	account_builder.AddData("pos_1", database->ship_info.Position[1]);
	account_builder.AddData("pos_2", database->ship_info.Position[2]);
	account_builder.AddData("ori_0", database->ship_info.Orientation[0]);
	account_builder.AddData("ori_1", database->ship_info.Orientation[1]);
	account_builder.AddData("ori_2", database->ship_info.Orientation[2]);
	account_builder.AddData("ori_3", database->ship_info.Orientation[3]);

	if (!account_query.run_query(account_builder.CreateQuery()))
	{
		LogMessage("Could not save ShipData for id %d, %s\n", avatar_id, account_query.ErrorMsg());
	}
}

void SaveManager::HandleNewWarnLevel(long player_id, short bytes, unsigned char *data)
{
	sql_query_c account_query (&m_SQL_Conn);
	sql_result_c result;

	long new_level = *((long *)&data[0]);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
		"SELECT * FROM net7_user.warning_levels WHERE avatar_id = '%d'", player_id);

	//does this item exist in the DB?
	account_query.execute(m_QueryStr);
	account_query.store(&result);

	if (result.n_rows() != 0)
	{
		//yes, just update the info
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
			"UPDATE net7_user.warning_levels SET sound_warning_level = '%d' WHERE avatar_id = '%d'",
			new_level, player_id);

		account_query.run_query(m_QueryStr);
	}
	else
	{
		sql_query Builder;
		Builder.Clear();
		Builder.SetTable("warning_levels");

		Builder.AddData("avatar_id", player_id);
		Builder.AddData("sound_warning_level", new_level);

		account_query.run_query(Builder.CreateQuery());
	}
}

void SaveManager::HandleXPDebt(long player_id, short bytes, unsigned char *data)
{
	sql_query_c account_query (&m_SQL_Conn);

	u32 xp_debt = *((u32 *) &data[0]);
	u32 last_debt = *((u32 *) &data[4]);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "UPDATE avatar_level_info SET xp_debt=%d,last_debt=%d WHERE avatar_id=%d", xp_debt, last_debt, player_id);
 
	account_query.run_query(m_QueryStr);
}

// called from Player with minimal info (leaving or joining)
void SaveManager::HandleGuildId(long player_id, short bytes, unsigned char *data)
{
	sql_query_c guild_query (&m_SQL_Conn);

	short rank_num = *((short *) &data[0]);
	int id = *((int *) &data[2]);

	if (id == -1)
	{
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM guild_members WHERE avatar_id=%d", player_id);
 		guild_query.run_query(m_QueryStr);
	}
	else
	{
		sql_query MemberBuilder;
		MemberBuilder.Clear();
		MemberBuilder.SetTable("guild_members");

		MemberBuilder.AddData("avatar_id", player_id);
		MemberBuilder.AddData("guild_id", id);
		MemberBuilder.AddData("rank", rank_num);
		MemberBuilder.AddData("contribution", 0);
		MemberBuilder.AddData("active", 1);
		MemberBuilder.AddData("tag", "");

		guild_query.run_query(MemberBuilder.CreateQuery());
	}
}

void SaveManager::HandleGuildMember(long guild_id, short bytes, unsigned char *data)
{
	sql_query_c guild_query (&m_SQL_Conn);
	sql_result_c result;
	sql_row_c guild_row;

	struct GuildMember *member = (struct GuildMember *)data;
	
	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "SELECT * FROM guild_members WHERE avatar_id=%d", member->avatar_id);

	//does this item exist in the DB?
	guild_query.execute(m_QueryStr);
	guild_query.store(&result);

	if (result.n_rows() != 0)
	{
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), "UPDATE guild_members SET rank=%d, contribution=%d, active=%d, tag='%s' WHERE avatar_id=%d", 
			member->Rank, member->Contribution, (int)member->Active, member->Tag, member->avatar_id);
 		guild_query.run_query(m_QueryStr);
	}
	else
	{
		sql_query MemberBuilder;
		MemberBuilder.Clear();
		MemberBuilder.SetTable("guild_members");

		MemberBuilder.AddData("avatar_id", member->avatar_id);
		MemberBuilder.AddData("guild_id", guild_id);
		MemberBuilder.AddData("rank", member->Rank);
		MemberBuilder.AddData("contribution", 0);
		MemberBuilder.AddData("active", 1);
		MemberBuilder.AddData("tag", "");

		guild_query.run_query(MemberBuilder.CreateQuery());
	}
}

void SaveManager::HandleGuildRank(long guild_id, short bytes, unsigned char *data)
{
	sql_query_c guild_query (&m_SQL_Conn);
	sql_result_c result;

	short rank_num = *((short *) &data[0]);
	struct GuildRank *rank = (struct GuildRank *)&data[2];
	long id = guild_id*10+rank_num;

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "SELECT * FROM guild_ranks WHERE id=%d", id);

	//does this item exist in the DB?
	guild_query.execute(m_QueryStr);
	guild_query.store(&result);

	if (result.n_rows() != 0)
	{
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), "UPDATE guild_ranks SET name='%s', permissions=%d, maxpromote=%d, maxremove=%d, mindemote=%d WHERE id=%d",
			rank->Name, rank->PermissionFlags, rank->MaxPromote, rank->MaxRemove, rank->MinDemote, id);
 		guild_query.run_query(m_QueryStr);
	}
	else
	{
		sql_query RankBuilder;
		RankBuilder.Clear();
		RankBuilder.SetTable("guild_ranks");

		RankBuilder.AddData("id", id);
		RankBuilder.AddData("name", rank->Name);
		RankBuilder.AddData("permissions", rank->PermissionFlags);
		RankBuilder.AddData("maxpromote", rank->MaxPromote);
		RankBuilder.AddData("maxremove", rank->MaxRemove);
		RankBuilder.AddData("mindemote", rank->MinDemote);

		guild_query.run_query(RankBuilder.CreateQuery());
	}
}

void SaveManager::HandleGuildInfo(long guild_id, short bytes, unsigned char *data)
{
	sql_query_c guild_query (&m_SQL_Conn);
	sql_result_c result;

	struct Guild *guild = (struct Guild *)data;

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "SELECT * FROM guilds WHERE guild_id=%d", guild_id);

	//does this item exist in the DB?
	guild_query.execute(m_QueryStr);
	guild_query.store(&result);

	if (result.n_rows() != 0)
	{
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), "UPDATE guilds SET name='%s', motd='%s', points=%d, level=%d, public=%d WHERE guild_id=%d",
			guild->Name, guild->MOTD, guild->Points, guild->Level, (int)guild->PublicStats, guild_id);
 		guild_query.run_query(m_QueryStr);
	}
	else
	{
		sql_query GuildBuilder;
		GuildBuilder.Clear();
		GuildBuilder.SetTable("guilds");

		GuildBuilder.AddData("guild_id", guild_id);
		GuildBuilder.AddData("name", guild->Name);
		GuildBuilder.AddData("motd", guild->MOTD);
		GuildBuilder.AddData("points", guild->Points);
		GuildBuilder.AddData("level", guild->Level);
		GuildBuilder.AddData("public", guild->PublicStats);

		guild_query.run_query(GuildBuilder.CreateQuery());
	}
}

void SaveManager::HandleDeleteGuild(long guild_id)
{
	sql_query_c guild_query (&m_SQL_Conn);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM guilds WHERE guild_id=%d", guild_id);
	guild_query.execute(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM guild_ranks WHERE id>=%d AND id <=%d", guild_id*10, guild_id*10+9);
	guild_query.execute(m_QueryStr);

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), "DELETE FROM guild_members WHERE guild_id=%d", guild_id);
	guild_query.execute(m_QueryStr);
}

void SaveManager::HandleChangeFieldRespawn(short bytes, unsigned char *data)
{
	sql_query_c FldUpdate(&m_SQL_Conn);
	u16 new_respawn = *((u16 *) &data[0]);
	long database_id = *((long *) &data[2]);

	sql_result_c result;

	sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
		"SELECT * FROM server_local_field_respawn_times WHERE resource_id = '%d'", database_id);

	//does this item exist in the DB?
	FldUpdate.execute(m_QueryStr);
	FldUpdate.store(&result);

	if (result.n_rows() != 0)
	{
		//yes, just update the info
		sprintf_s(m_QueryStr, sizeof(m_QueryStr), 
			"UPDATE server_local_field_respawn_times SET local_respawn_time = '%d' WHERE resource_id = '%d'", 
			new_respawn, database_id);

		FldUpdate.run_query(m_QueryStr);
	}
	else
	{
		sql_query Builder;
		Builder.Clear();
		Builder.SetTable("server_local_field_respawn_times");

		Builder.AddData("resource_id", database_id);
		Builder.AddData("local_respawn_time", new_respawn);

		FldUpdate.run_query(Builder.CreateQuery());
	}
}