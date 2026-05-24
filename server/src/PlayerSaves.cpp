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
///////////////////////////////////////////////////////////////////
//
// PlayerSaves.cpp
//
// This file contains all the methods needed to save game status.
//
///////////////////////////////////////////////////////////////////

#include "PlayerClass.h"
#include "ServerManager.h"
#include "StaticData.h"
#include "mysql/mysqlplus.h"
#include "SaveManager.h"
#include "PacketMethods.h"
#include <float.h>

#ifndef USE_MYSQL_ACCOUNT_DATA
#error "BUILD ERROR: USE_MYSQL_ACCOUNT IS NOW ESSENTIAL"
#endif

extern sql_connection_c m_SQL_Conn;


void Player::SaveWarnLvl(long WarnChar, long warn_inc, char *WMsg)
{
	unsigned char data[264];
	int index = 0;

	AddData(data, WarnChar, index);
	AddData(data, warn_inc, index);
	AddDataS(data, WMsg, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_INFRACTION, m_CharacterID, index, data);
}

void Player::LoadGMItems()
{
	// Pull down items and then dump them from the avatar_gm_items
	sql_query_c account_query (&m_SQL_Conn);
	sql_result_c account_result;
	sql_row_c account_row;

	account_query.AddParam((long)this->CharacterID());
	if (account_query.run_query_params("SELECT avatar_gm_items.avatar_id,avatar_gm_items.item_id,avatar_gm_items.stack_level,avatar_gm_items.trade_stack,avatar_gm_items.quality,avatar_gm_items.cost,avatar_gm_items.builder_name,avatar_gm_items.structure FROM avatar_gm_items WHERE avatar_gm_items.avatar_id = ?") && account_query.n_rows() > 0)
	{
		account_query.store(&account_result);

		if (account_query.n_rows() == 0)
		{
			SendVaMessage("There were no items found for your avatar.");
			return;
		}

		if (CargoFreeSpace() < account_query.n_rows())
		{
			SendVaMessage("You do not have enough free cargo space for the items. Please free up cargo space.");
			return;
		}

		for(int r=0;r<account_query.n_rows();r++)
		{
			account_result.fetch_row(&account_row);

			_Item cItem;
			memset(&cItem, 0, sizeof(_Item));
			cItem.ItemTemplateID = (int)account_row["item_id"];
			cItem.StackCount = (int)account_row["stack_level"];
			cItem.TradeStack = (int)account_row["trade_stack"];
			cItem.Quality = (float)account_row["quality"];
			cItem.Structure = (float)account_row["structure"];
			strcpy_s(cItem.BuilderName, sizeof(cItem.BuilderName), (char *)account_row["builder_name"]);
			cItem.Structure = (cItem.Structure > 1.0f)? 1.0f : cItem.Structure;
			CargoAddItem(&cItem);
			// Display the item
			ItemBase * ItemData = g_ItemBaseMgr->GetItem(cItem.ItemTemplateID);
			SendVaMessage("Added Item \"%s\"", ItemData->Name());
		}

		SendAuxShip();
		account_query.AddParam((long)CharacterID());
		account_query.run_query_params("DELETE FROM avatar_gm_items WHERE avatar_gm_items.avatar_id = ?");
	}
	else
	{
		SendVaMessage("There were no items found for your avatar.");
	}

	return;
}


void Player::SavePosition()
{
    if (m_Hijackee)
    {
        return;
    }

	unsigned char pos_data[32];
	float *ori = Orientation();
    *((float *) &pos_data[0]) = PosX();
    *((float *) &pos_data[4]) = PosY();
    *((float *) &pos_data[8]) = PosZ();
    *((float *) &pos_data[12]) = ori[0];
    *((float *) &pos_data[16]) = ori[1];
    *((float *) &pos_data[20]) = ori[2];
	*((float *) &pos_data[24]) = ori[3];

	*((long *) &pos_data[28]) = PlayerIndex()->GetSectorNum();

	g_SaveMgr->AddSaveMessage(SAVE_CODE_STORE_POSITION, m_CharacterID, 32, pos_data);
}

void Player::CreatePositionSave()
{
	sql_query_c account_query (&m_SQL_Conn);

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `avatar_position` WHERE `avatar_id` = ?");

	sql_query PositionBuilder;
	PositionBuilder.Clear();
	PositionBuilder.SetTable("avatar_position");

	PositionBuilder.AddData("avatar_id", m_CharacterID);
	PositionBuilder.AddData("posx", 0);
	PositionBuilder.AddData("posy", 0);
	PositionBuilder.AddData("posz", 0);
	PositionBuilder.AddData("ori_w", 0);
	PositionBuilder.AddData("ori_x", 0);
	PositionBuilder.AddData("ori_y", 0);
	PositionBuilder.AddData("ori_z", 0);
	PositionBuilder.AddData("sector_id", (long)PlayerIndex()->GetSectorNum());

	if (!account_query.run_query(PositionBuilder.CreateQuery()))
	{
		LogMessage("Could not create position save for player %s [%d]\n", Name(), m_CharacterID);
	}
}

bool Player::LoadPosition()
{
    bool success = false;

    if (CharacterID() == NULL) 
	{
		LogMessage("Null avatar_id in LoadPosition, skipping load.");
		return false;
	}

	sql_query_c account_query (&m_SQL_Conn);
	sql_result_c account_result;
    sql_row_c account_row;

	account_query.AddParam((long)m_CharacterID);
	if (!account_query.run_query_params("SELECT * FROM `avatar_position` WHERE `avatar_id` = ?") || account_query.n_rows() == 0)
	{
		return false;
	}

    account_query.store(&account_result);
	account_result.fetch_row(&account_row);

	if ((int)account_row["sector_id"] != PlayerIndex()->GetSectorNum()) //make sure this position applies to current sector
	{
		return false;
	}

	float posx, posy, posz, oriw, orix, oriy, oriz;

	posx = (float)account_row["posx"];
	posy = (float)account_row["posy"];
	posz = (float)account_row["posz"];

	oriw = (float)account_row["ori_w"];
	orix = (float)account_row["ori_x"];
	oriy = (float)account_row["ori_y"];
	oriz = (float)account_row["ori_z"];

	SetPosition(posx, posy, posz);
	SetOrientation(oriw, orix, oriy, oriz);

	success = true;

	return success;
}

// This may be called from the Global Server or Sector Server
bool Player::ReadSavedData()
{
//	sql_connection_c connection( "net7_user", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
	sql_query_c account_query (&m_SQL_Conn);

	bool success = false;
	int i = 0;

	//see if we need to initialise or re-load
	//try to load in the avatar_level info, if this exists, character has been initialised
	sql_result_c result;
    sql_row_c account_row;

	account_query.AddParam((long)m_CharacterID);
	account_query.execute_params("SELECT * FROM `avatar_level_info` WHERE `avatar_id` = ?");
	account_query.store(&result);

	if (account_query.n_rows() == 0)
	{
		//we need to initialise the data
		ReInitializeSavedData();
	}
	else
	{
		//do a normal player load
		ReloadSavedData();
	}

    return (success);
}

void Player::SetHullUpgrade()
{
	long upgrade = PlayerIndex()->RPGInfo.GetHullUpgradeLevel();

	m_Database.ship_info.hull         = BaseHullAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.hull];
	m_Database.ship_info.profession   = BaseProfAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.profession];
	m_Database.ship_info.wing         = BaseWingAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.wing];
	m_Database.ship_info.engine       = BaseEngineAsset[m_Database.ship_data.race];

	switch (upgrade)
	{
	case 6:
		m_Database.ship_info.profession += 1;
	case 5:
		m_Database.ship_info.wing += 1;
		m_Database.ship_info.engine += 1;
	case 4:
		m_Database.ship_info.hull += 1;
	case 3:
		m_Database.ship_info.profession += 1;
	case 2:
		m_Database.ship_info.wing += 1;
		m_Database.ship_info.engine += 1;
	case 1:
		m_Database.ship_info.hull += 1;
	default:
		break;
	}
}

//TODO: this should all be done via the savemanager, it's here really for prototyping.
//then again, providing we keep the ptr to the database it should be ok just for reloading
void Player::ReloadSavedData()
{
//	sql_connection_c connection( "net7_user", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
	sql_query_c account_query (&m_SQL_Conn);
	sql_result_c account_result;
    sql_row_c account_row;
	m_WeaponSlots = 0;
	m_DeviceSlots = 0;

	char blank = 0;

    long i, class_index = ClassIndex(), race = Race();

	unsigned long myTime = GetNet7TickCount();

/*---------------------------------------------------------------------*/
	//allow sector number to persist so our equipment can be installed

	u32 secNum = PlayerIndex()->GetSectorNum();
    PlayerIndex()->Reset();
	PlayerIndex()->SetSectorNum(secNum);

	// get logout time for xp debt calcs
	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("SELECT last_logout_t FROM `avatar_info` WHERE `avatar_id` = ?");
    account_query.store(&account_result);
	account_result.fetch_row(&account_row);
	m_LogoutTime = (time_t)(unsigned long)account_row["last_logout_t"];

	if (m_LogoutTime == 0) 
	{
		time(&m_LogoutTime);
	}

	//get credits
	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("SELECT * FROM `avatar_level_info` WHERE `avatar_id` = ?");
    account_query.store(&account_result);
	account_result.fetch_row(&account_row);

	unsigned long credits = account_row["credits"];

	if (credits < 0)
	{
		credits = 0;
		LogMessage("Avatar '%s' has less then zero credits", m_Database.avatar.avatar_first_name);
	}

	PlayerIndex()->SetCredits(credits);

    ShipIndex()->Reset();

	ShipIndex()->SetName(m_Database.avatar.avatar_first_name);
	ShipIndex()->SetOwner(m_Database.avatar.avatar_first_name);

	unsigned long hull_level = (unsigned long)account_row["hull_upgrade_level"];

	PlayerIndex()->RPGInfo.SetHullUpgradeLevel(hull_level);

    ShipIndex()->SetRank(GetRank());

	float hull_points = (float)account_row["hull_points"];

	ShipIndex()->SetHullPoints(hull_points);
	ShipIndex()->SetMaxHullPoints((float)account_row["max_hull_points"]);

	ShipIndex()->SetAcceleration(117.5f);
	ShipIndex()->SetMaxTiltRate(1.158f);
	ShipIndex()->SetMaxTurnRate(1.158f);
	PlayerIndex()->RPGInfo.SetRace(m_Database.ship_data.race);
	PlayerIndex()->RPGInfo.SetProfession(m_Database.ship_data.profession);
	PlayerIndex()->RPGInfo.SetSkillPowerUpAbilityNumber(-1);
	ShipIndex()->SetWarpAvailable(2);

	ShipIndex()->Inventory.SetEquipMountModel("tvf01_1");

    for (i=0; i<20; i++)
    {
        m_Equip[i].Init(this, i);
    }

	//now set up slots
	//work out cargo space from scratch
	ShipIndex()->Inventory.SetCargoSpace(BaseCargo[class_index] + 2*hull_level);

	for(i=0;i<(long)ShipIndex()->Inventory.GetCargoSpace();i++) 
	{
		ShipIndex()->Inventory.CargoInv.Item[i].Empty();
    }

    for(i=0;i<96;i++) 
	{
		PlayerIndex()->SecureInv.Item[i].SetItemTemplateID(-1);
    }

	ShipIndex()->CurrentStats.SetScanRange(BaseScanRange[class_index]);
	ShipIndex()->CurrentStats.SetVisibility(BaseVisableRange[class_index]);

	//weapons & mounts
	//TODO: store mount positions so players can customise mounts
	long weapon_count = account_row["weapon_slots"];
	long device_count = account_row["device_slots"];

	ResetWeaponMounts();
	ResetDeviceMounts();

	ShipIndex()->Inventory.SetFutureDevices(MaxDeviceSlots[class_index]);
	ShipIndex()->Inventory.SetFutureWeapons(MaxWeaponSlots[class_index]);

	ShipIndex()->Inventory.Mounts.SetMount(0, ShieldMount);
	ShipIndex()->Inventory.Mounts.SetMount(1, ReactorMount);
	ShipIndex()->Inventory.Mounts.SetMount(2, EngineMount);

	//now set EXP bars
	float combatxp = (float)account_row["combat_bar_level"];
	float explorexp = (float)account_row["explore_bar_level"];
	float tradexp = (float)account_row["trade_bar_level"];

	PlayerIndex()->RPGInfo.SetCombatXP(combatxp);
	PlayerIndex()->RPGInfo.SetExploreXP(explorexp);
	PlayerIndex()->RPGInfo.SetTradeXP(tradexp);
	PlayerIndex()->SetXPDebt((unsigned long)account_row["xp_debt"]);
	m_IncapXPDebt = (unsigned long)account_row["last_debt"];

	//skillpoints
	PlayerIndex()->RPGInfo.SetSkillPoints((unsigned long)account_row["skill_points"]);

	//warp power level
	ShipIndex()->CurrentStats.SetWarpPowerLevel(account_row["warp_power_level"]);
	unsigned long thrust_type = (unsigned long)account_row["engine_thrust_type"];
	ShipIndex()->SetEngineTrailType(thrust_type);

	m_RegisteredSectorID = account_row["registered_starbase"];

	float energy_bar = (float)account_row["reactor_level"];
	float shield_bar = (float)account_row["shield_level"];

	//now add cargo (do this before equipment so errors can be moved to cargo and not erased)
	for(u32 j=0;j<ShipIndex()->Inventory.GetCargoSpace();j++)
	{
		ShipIndex()->Inventory.CargoInv.Item[j].SetItemTemplateID(-1);
	}
	
	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("SELECT * FROM `avatar_inventory_items` WHERE `avatar_id` = ?");
    account_query.store(&account_result);

	for(i=0;i<account_result.n_rows();i++)
	{
		account_result.fetch_row(&account_row);
		long inventory_slot = account_row["inventory_slot"];
		long item_template_id = account_row["item_id"];
		float quality = (float)account_row["quality"];
		float structure = (float)account_row["structure"];
		char *builder = (char*)account_row["builder_name"];
		long stack = account_row["stack_level"];
		long trade_stack = account_row["trade_stack"];
		long cost = account_row["cost"];
		if (!builder) builder = &blank;

		if (item_template_id > 0 && stack > 0)
		{
			if (stack == 0) stack = 1; //this is to fix an old bug with equipment stack level

			ShipIndex()->Inventory.CargoInv.Item[inventory_slot].SetItemTemplateID(item_template_id);
			ShipIndex()->Inventory.CargoInv.Item[inventory_slot].SetQuality(quality);
			ShipIndex()->Inventory.CargoInv.Item[inventory_slot].SetStructure((structure>1.0f)?1.0f : structure);
			ShipIndex()->Inventory.CargoInv.Item[inventory_slot].SetBuilderName(builder);
			ShipIndex()->Inventory.CargoInv.Item[inventory_slot].SetStackCount(stack);
			ShipIndex()->Inventory.CargoInv.Item[inventory_slot].SetTradeStack(trade_stack);
			ShipIndex()->Inventory.CargoInv.Item[inventory_slot].SetAveCost((float)cost);
		}
	}

	//now add equipment, new table.
	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("SELECT * FROM `avatar_equipment` WHERE `avatar_id` = ?");
    account_query.store(&account_result);

	for(i=0;i<account_result.n_rows();i++)
	{
		account_result.fetch_row(&account_row);
		long equipment_slot = account_row["equipment_slot"];
		long item_template_id = account_row["item_id"];
		float structure = (float)account_row["structure"];
		float quality = (float)account_row["quality"];
		char *builder = (char*)account_row["builder_name"];
		if (!builder) builder = &blank;

		_Item new_equipment;
		memset(&new_equipment,0,sizeof(_Item));

		if (item_template_id > 0)
		{
			sprintf_s(new_equipment.BuilderName, 64, "%s", builder);
			new_equipment.BuilderName[sizeof(new_equipment.BuilderName)-1]='\0';
			new_equipment.Quality = quality;
			new_equipment.Structure = (structure>1.0f)?1.0f : structure;
			new_equipment.ItemTemplateID = item_template_id;
			new_equipment.StackCount = 1;
			memset(new_equipment.InstanceInfo, 0, 64);
			memset(new_equipment.ActivatedEffectInstanceInfo, 0, 64);
			memset(new_equipment.EquipEffectInstanceInfo, 0, sizeof(new_equipment.EquipEffectInstanceInfo));

			m_Equip[equipment_slot].Equip(&new_equipment, true);
		}
	}

	ShipIndex()->Shield.SetStartValue(shield_bar);
	ShipIndex()->Energy.SetStartValue(energy_bar);

	if (hull_points == 0.0f)
	{
		ShipIndex()->SetIsIncapacitated(true);

		// Stop regen
		RemoveEnergy(0);
		ShipIndex()->Energy.SetStartValue(0);
		ShipIndex()->Shield.SetStartValue(0);
		ShipIndex()->Shield.SetEndTime(GetNet7TickCount());		// Set end time now!
		m_ShieldRecharge = 0;

        ImmobilisePlayer();
		ShieldUpdate(GetNet7TickCount(), 0, 0.0f);
	}
	else
	{
		unsigned long now = GetNet7TickCount();
		ShieldUpdate(now, 0, ShipIndex()->Shield.GetStartValue());
		EnergyUpdate(now, 0, ShipIndex()->Energy.GetStartValue());
	}
	
	//now add ammo
    for(i=0;i<20;i++)
    {
        ShipIndex()->Inventory.AmmoInv.Item[i].SetItemTemplateID(-1);
    }

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("SELECT * FROM `avatar_ammo` WHERE `avatar_id` = ?");
    account_query.store(&account_result);

	for(i=0;i<account_result.n_rows();i++)
	{
		account_result.fetch_row(&account_row);
		long equipment_slot = account_row["equipment_slot"];
		long item_template_id = account_row["item_id"];
		float quality = (float)account_row["quality"];
		float structure = (float)account_row["structure"];
		char *builder = (char*)account_row["builder_name"];
		if (!builder) builder = &blank;
		long stack = account_row["ammo_stack"];

		if (item_template_id > -1)
		{
			_Item ammo;
			memset(&ammo,0,sizeof(_Item));

			sprintf_s(ammo.BuilderName, 64, "%s", builder);
			ammo.Quality = quality;
			ammo.Structure = (structure>1.0f)?1.0f : structure;
			ammo.ItemTemplateID = item_template_id;
			// Sanity check. Reset all negative ammo to 1
			if (stack < 1)
			{
				stack = 1;
				LogMessage("ERROR: negative ammo_stack for %d\n", m_CharacterID );
			}
			ammo.StackCount = stack;

			m_Equip[equipment_slot].Equip(&ammo);
		}
	}

	//now add vault

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("SELECT * FROM `avatar_vault_items` WHERE `avatar_id` = ?");
    account_query.store(&account_result);

	for(i=0;i<account_result.n_rows();i++)
	{
		account_result.fetch_row(&account_row);
		long inventory_slot = account_row["inventory_slot"];
		long item_template_id = account_row["item_id"];
		float quality = (float)account_row["quality"];
		float structure = (float)account_row["structure"];
		char *builder = (char*)account_row["builder_name"];
		long stack = account_row["stack_level"];
		long trade_stack = account_row["trade_stack"];
		long cost = account_row["cost"];
		if (!builder) builder = &blank;

		if (stack == 0) stack = 1; //this is to fix an old bug with equipment stack level

		PlayerIndex()->SecureInv.Item[inventory_slot].SetItemTemplateID(item_template_id);
		PlayerIndex()->SecureInv.Item[inventory_slot].SetQuality(quality);
		PlayerIndex()->SecureInv.Item[inventory_slot].SetStructure((structure>1.0f)?1.0f : structure);
		PlayerIndex()->SecureInv.Item[inventory_slot].SetBuilderName(builder);
		PlayerIndex()->SecureInv.Item[inventory_slot].SetStackCount(stack);
		PlayerIndex()->SecureInv.Item[inventory_slot].SetTradeStack(trade_stack);
		PlayerIndex()->SecureInv.Item[inventory_slot].SetAveCost((float)cost);
	}

	//now add trade window (crash recovery)

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("SELECT * FROM `avatar_trade_items` WHERE `avatar_id` = ?");
    account_query.store(&account_result);

	for(i=0;i<account_result.n_rows();i++)
	{
		account_result.fetch_row(&account_row);
		long inventory_slot = account_row["inventory_slot"];
		long item_template_id = account_row["item_id"];
		float quality = (float)account_row["quality"];
		float structure = (float)account_row["structure"];
		char *builder = (char*)account_row["builder_name"];
		long stack = account_row["stack_level"];
		long trade_stack = account_row["trade_stack"];
		long cost = account_row["cost"];
		if (!builder) builder = &blank;

		ShipIndex()->Inventory.TradeInv.Item[inventory_slot].SetItemTemplateID(item_template_id);
		ShipIndex()->Inventory.TradeInv.Item[inventory_slot].SetQuality(quality);
		ShipIndex()->Inventory.TradeInv.Item[inventory_slot].SetStructure((structure>1.0f)?1.0f : structure);
		ShipIndex()->Inventory.TradeInv.Item[inventory_slot].SetBuilderName(builder);
		ShipIndex()->Inventory.TradeInv.Item[inventory_slot].SetStackCount(stack);
		ShipIndex()->Inventory.TradeInv.Item[inventory_slot].SetTradeStack(trade_stack);
		ShipIndex()->Inventory.TradeInv.Item[inventory_slot].SetAveCost((float)cost);
	}

	//now set skills

	u32 Availability[4];
    SkillData *Skills = g_ServerMgr->m_SkillList;
    SkillClassData *ClassData = 0;

	for(i=0;i<64;i++)
	{
        // Check to see if this class has this skill
		if (Skills[i].ClassType[class_index].MaxLevel > 0)
		{
            ClassData = &Skills[i].ClassType[class_index];

            if (ClassData->Quested == 0 && ClassData->LevelAquired == 0)
            {
                // This skill is available to level up
				Availability[0] = 4;
				Availability[1] = 2;    
				Availability[2] = 0;
				Availability[3] = 1;
            }
            else if (ClassData->Quested == 0 && ClassData->LevelAquired > 0)
            {
                // This skill is available but at a higher level
                // Not sure if this will ever be used
				Availability[0] = 4;
				Availability[1] = 1;
				Availability[2] = ClassData->LevelAquired;
				Availability[3] = 1;
            }
            else
            {
                // This skill is aquired via a quest (Quested == 1)
				Availability[0] = 3;
				Availability[1] = 0;
				Availability[2] = 0;
				Availability[3] = 0;
            }

			PlayerIndex()->RPGInfo.Skills.Skill[i].SetAvailability(Availability);
			PlayerIndex()->RPGInfo.Skills.Skill[i].SetMaxSkillLevel(ClassData->MaxLevel);
			PlayerIndex()->RPGInfo.Skills.Skill[i].SetQuestOnlyLevel(ClassData->MaxLevel);
			PlayerIndex()->RPGInfo.Skills.Skill[i].SetLastActivationTime(myTime);
        }
    }

	PlayerIndex()->RPGInfo.Skills.Skill[SKILL_BEAM_WEAPON].SetLevel(1);
	PlayerIndex()->RPGInfo.Skills.Skill[SKILL_DEVICE_TECH].SetLevel(1);
	PlayerIndex()->RPGInfo.Skills.Skill[SKILL_ENGINE_TECH].SetLevel(1);
	PlayerIndex()->RPGInfo.Skills.Skill[SKILL_REACTOR_TECH].SetLevel(1);
	PlayerIndex()->RPGInfo.Skills.Skill[SKILL_SHIELD_TECH].SetLevel(1);

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("SELECT * FROM `avatar_skill_levels` WHERE `avatar_id` = ?");
    account_query.store(&account_result);

	for(i=0;i<account_result.n_rows();i++)
	{
		account_result.fetch_row(&account_row);
		long skill_id = account_row["skill_id"];
		long skill_level = account_row["skill_level"];

        Availability[0] = 4;
		Availability[1] = 2;
		Availability[2] = 0;
		Availability[3] = 1;

		//see if this skill has reached max level
		ClassData = &Skills[skill_id].ClassType[class_index];

		if (skill_level >= ClassData->MaxLevel)
		{
			Availability[1] = SKILL_ERROR_MAXLVL;
			Availability[2] = 0;
			Availability[3] = 0;
			skill_level = ClassData->MaxLevel;
		}

		PlayerIndex()->RPGInfo.Skills.Skill[skill_id].SetAvailability(Availability);
		PlayerIndex()->RPGInfo.Skills.Skill[skill_id].SetLevel(skill_level);
		SkillUpdateStats(skill_id);
	}

	UpdateSkills();

	// setup guild info
	// fixed sending MOTD first before any other packets - was causing a lot of login issues.
	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("SELECT * FROM `guild_members` WHERE `avatar_id` = ?");
    account_query.store(&account_result);
	if (account_result.n_rows())
	{
		account_result.fetch_row(&account_row);
		SetupGuildInfo(account_row["guild_id"], account_row["rank"]);
	}
	else if (AdminLevel() >= GM)
	{
		SetupGuildInfo(0, AdminLevel()/10-1, true);
	}

	//now set levels
	PlayerIndex()->RPGInfo.SetCombatLevel(ntohl(m_Database.info.combat_level));
	PlayerIndex()->RPGInfo.SetExploreLevel(ntohl(m_Database.info.explore_level));
	PlayerIndex()->RPGInfo.SetTradeLevel(ntohl(m_Database.info.trade_level));

	//now set factions
	SetupFactions(&account_query, false);

	PlayerIndex()->SetRegistrationStarbase("Net-7 SOL");
	PlayerIndex()->SetRegistrationStarbaseSector("Saturn Sector (Sol System)");

	PlayerIndex()->SetMusicID(-1);
	PlayerIndex()->SetPIPAvatarID(-1);

	ShipIndex()->BaseStats.SetMissleDefence(10);
	ShipIndex()->BaseStats.SetTurnRate(50);

	ShipIndex()->CurrentStats.SetMissleDefence(10);
	ShipIndex()->CurrentStats.SetTurnRate(50);
	

	
	//ShipIndex()->SetFactionIdentifier(faction_list[class_index]);

	//now set correct hull types
	SetHullUpgrade();

	// Load all itemID's for manufacturing
	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("SELECT * FROM `avatar_recipes` WHERE `avatar_id` = ?");
    account_query.store(&account_result);

	for(i=0;i<account_result.n_rows();i++)
	{
		account_result.fetch_row(&account_row);
		long item_template_id = account_row["item_id"];

		// Set a item to known
		m_ManuRecipes.insert(pair<int,int>(item_template_id,1));
	}

	// load friends list
	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("SELECT * FROM `friends_lists` WHERE `avatar_id` = ?");
    account_query.store(&account_result);

	m_NumFriends = 0;
	for(i=0;i<account_result.n_rows();i++)
	{
		account_result.fetch_row(&account_row);
		char *name = account_row["name"];
		if (name)
		{
			strcpy_s(m_FriendNames[m_NumFriends++],sizeof(m_FriendNames[m_NumFriends]),name);
		}
	}

	// load ignore list
	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("SELECT * FROM `ignore_lists` WHERE `avatar_id` = ?");
    account_query.store(&account_result);

	m_NumIgnore = 0;
	for(i=0;i<account_result.n_rows();i++)
	{
		account_result.fetch_row(&account_row);
		char *name = account_row["name"];
		if (name)
		{
			strcpy_s(m_IgnoreNames[m_NumIgnore++],sizeof(m_IgnoreNames[m_NumIgnore]),name);
		}
	}

	LoadExploredNavs(&account_query);
	LoadMissionStatus(&account_query);
	LoadMissionCompletions(&account_query);

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("SELECT * FROM `warning_levels` WHERE `avatar_id` = ?");
    account_query.store(&account_result);
	account_result.fetch_row(&account_row);

	if (account_result.n_rows() == 0)
	{
		m_SoundWarningSetting = 2;
	}
	else
	{
		m_SoundWarningSetting = account_row["sound_warning_level"];
	}
}

void Player::LoadExploredNavs(sql_query_c *query)
{
	sql_result_c account_result;
    sql_row_c account_row;

	query->AddParam((long)m_CharacterID);
	query->run_query_params("SELECT * FROM `avatar_exploration` WHERE `avatar_id` = ?");
    query->store(&account_result);

	for(int i=0;i<account_result.n_rows();i++)
	{
		account_result.fetch_row(&account_row);
		long object_uid = account_row["object_id"];
		long flags = account_row["explore_flags"];
		long offset = (object_uid/(sizeof(long)*8));
		long *explored;
		long *exposed = (long*) (m_NavsExposed + offset);
		
		switch (flags)
		{
		case EXPLORE_NAV:
			explored = (long*) (m_NavsExplored + offset);
			*explored |= (1 << object_uid%32);
			//deliberate drop-through - an explored nav must have been exposed
		case DISCOVER_NAV:
			*exposed |= (1 << object_uid%32);
			break;
		}
	}
}

void Player::SetupFactions(sql_query_c *query, bool force)
{
	sql_result_c account_result;
    sql_row_c account_row;
	int i;

	if (!force)
	{
		query->AddParam((long)m_CharacterID);
		query->run_query_params("SELECT * FROM `faction_data` WHERE `avatar_id` = ?");
		query->store(&account_result);
	}

	long faction_count = g_ServerMgr->m_FactionData.GetFactionCount();

	if (faction_count > 32) faction_count = 32;

	long player_faction_id = faction_list[ClassIndex()];

	PlayerIndex()->Reputation.SetAffilitation( g_ServerMgr->m_FactionData.GetFactionName(player_faction_id) );
	ShipIndex()->SetFactionIdentifier( g_ServerMgr->m_FactionData.GetFactionPDAName(player_faction_id) );
	SetFactionID(player_faction_id);

	if (force || account_result.n_rows() == 0)
	{
		long order = 0;
		for (i=1;i<=faction_count;i++)
		{
			if (g_ServerMgr->m_FactionData.GetPDA(i))
			{
				char *faction_name = g_ServerMgr->m_FactionData.GetFactionPDAName(i);
				if (!faction_name) continue;
				float faction_standing = g_ServerMgr->m_FactionData.GetFactionStanding(player_faction_id, i);
				if (faction_standing < -9000.0f) faction_standing = -9000.0f;
				if (faction_standing > 9999.0f) faction_standing = 9999.0f;
				PlayerIndex()->Reputation.Factions.Faction[order].SetName( faction_name );
				PlayerIndex()->Reputation.Factions.Faction[order].SetReaction(faction_standing);
				PlayerIndex()->Reputation.Factions.Faction[order].SetOrder(order);
				SaveFactionChange(i, faction_standing, order);
				m_PDAFactionID[i] = (u8)order;
				order++;
			}
		}

		PlayerIndex()->Reputation.Factions.Faction[m_PDAFactionID[player_faction_id]].SetReaction(7500.0f);
		SaveFactionChange(player_faction_id, 7500.0f);
	}
	else
	{
		for(i=0;(i<account_result.n_rows() && i<33);i++)
		{
			account_result.fetch_row(&account_row);
			long faction_id = account_row["faction_id"];
			long order = account_row["faction_order"];
			float faction_value = (float)account_row["faction_value"];

			char *faction_name = g_ServerMgr->m_FactionData.GetFactionPDAName(faction_id);

			if (!faction_name) continue;
			if (order == -1) order = faction_id;

			PlayerIndex()->Reputation.Factions.Faction[i].SetName( faction_name );
			PlayerIndex()->Reputation.Factions.Faction[i].SetReaction(faction_value);
			PlayerIndex()->Reputation.Factions.Faction[i].SetOrder(order);
			m_PDAFactionID[faction_id] = i;
		}
	}
}

void Player::LoadMissionStatus(sql_query_c *query)
{
	sql_result_c account_result;
    sql_row_c account_row;

	query->AddParam((long)m_CharacterID);
	query->run_query_params("SELECT * FROM `avatar_mission_progress` WHERE `avatar_id` = ?");
    query->store(&account_result);

	for(int i=0;i<account_result.n_rows();i++)
	{
		account_result.fetch_row(&account_row);
		long mission_id = account_row["mission_id"];
		long mission_slot = account_row["mission_slot"];
		long stage_num = account_row["stage_num"];
		long mission_flags = account_row["mission_flags"];

		MissionTree *mission = g_ServerMgr->m_Missions.GetMissionTree(mission_id);
        
		if (mission)
		{
			AuxMission * m = &m_PlayerIndex.Missions.Mission[mission_slot];
			m->Clear();
			if (stage_num > mission->NumNodes) stage_num = mission->NumNodes;
			m->SetDatabaseID(mission_id);
			m->SetName(mission->name);
			m->SetStageNum(stage_num);
			m->SetStageCount(mission->NumNodes);
			m->SetSummary(mission->summary);
			m->SetMissionData(mission_flags);
			m->SetIsForfeitable(mission->forfeitable);

			//load the mission descriptions
			for (int j=1; j<=stage_num; j++)
			{
				if (mission->Nodes[j] && mission->Nodes[j]->description)
				{
					m->Stages.Stage[j-1].SetText(mission->Nodes[j]->description);
				}
			}
		}
		else
		{
			// This shouldnt happen unless someone deleted a mission
			LogMessage("Mission Error: Mission ID: %d is missing!\n", mission_id);
		}
	}
}

void Player::LoadMissionCompletions(sql_query_c *query)
{
	sql_result_c account_result;
    sql_row_c account_row;

	query->AddParam((long)m_CharacterID);
	query->run_query_params("SELECT * FROM missions_completed WHERE avatar_id = ?");
    query->store(&account_result);

	for(int i=0;i<account_result.n_rows();i++)
	{
		account_result.fetch_row(&account_row);
		long mission_id = account_row["mission_id"];
		long completion = account_row["mission_completion_flags"];

		if (completion && mission_id >= 0 && mission_id < MAX_MISSION_ID)
		{
			SetBitEntry(m_CompletedMissions, mission_id);
		}
	}
}

void Player::ReInitializeSavedData()
{
    long i, class_index = ClassIndex(), race = Race();

    static char *StartingRank[] =     // starting rank, based on class
    {
        "Ensign",           // TW
        "Prentice",         // TT
        "Cadet",            // TE
        "J'nai",            // JW
        "Nan'Jeu",          // JT
        "Aspirant",         // JE
        "Legionaire",       // PW
        "Quaestor",         // PT
        "Inceptor",         // PE
    };


	unsigned long myTime = GetNet7TickCount();

	DeleteAllAvatarRecords();

/*---------------------------------------------------------------------*/

	//save sector number 
	long start_sector = StartSector[m_Database.ship_data.race * 3 + m_Database.ship_data.profession];
	//u32 sector = PlayerIndex()->GetSectorNum();
    PlayerIndex()->Reset();
	PlayerIndex()->SetSectorNum(start_sector);
	PlayerIndex()->SetCredits(500);

    for(i=0;i<96;i++) 
	{
		PlayerIndex()->SecureInv.Item[i].SetItemTemplateID(-1);
    }

	PlayerIndex()->RPGInfo.SetRace(m_Database.ship_data.race);
	PlayerIndex()->RPGInfo.SetProfession(m_Database.ship_data.profession);
	PlayerIndex()->RPGInfo.SetSkillPowerUpAbilityNumber(-1);

	u32 Availability[4];
    SkillData *Skills = g_ServerMgr->m_SkillList;
    SkillClassData *ClassData = 0;

    // Do all 64 for forward compatibility
	for(i=0;i<64;i++)
	{
        // Check to see if this class has this skill
		if (Skills[i].ClassType[class_index].MaxLevel > 0)
		{
            ClassData = &Skills[i].ClassType[class_index];

            if (ClassData->Quested == 0 && ClassData->LevelAquired == 0)
            {
                // This skill is available to level up
				Availability[0] = 4;
				Availability[1] = 2;    
				Availability[2] = 0;
				Availability[3] = 1;
            }
            else if (ClassData->Quested == 0 && ClassData->LevelAquired > 0)
            {
                // This skill is available but at a higher level
                // Not sure if this will ever be used
				Availability[0] = 4;
				Availability[1] = 1;
				Availability[2] = ClassData->LevelAquired;
				Availability[3] = 1;
            }
            else
            {
                // This skill is aquired via a quest (Quested == 1)
				Availability[0] = 3;
				Availability[1] = 0;
				Availability[2] = 0;
				Availability[3] = 0;
            }

			PlayerIndex()->RPGInfo.Skills.Skill[i].SetAvailability(Availability);
			PlayerIndex()->RPGInfo.Skills.Skill[i].SetMaxSkillLevel(ClassData->MaxLevel);
			PlayerIndex()->RPGInfo.Skills.Skill[i].SetQuestOnlyLevel(ClassData->MaxLevel);
			PlayerIndex()->RPGInfo.Skills.Skill[i].SetLastActivationTime(myTime);
        }
    }

    // Starting skill points
	PlayerIndex()->RPGInfo.Skills.Skill[SKILL_BEAM_WEAPON].SetLevel(1);
	PlayerIndex()->RPGInfo.Skills.Skill[SKILL_DEVICE_TECH].SetLevel(1);
	PlayerIndex()->RPGInfo.Skills.Skill[SKILL_ENGINE_TECH].SetLevel(1);
	PlayerIndex()->RPGInfo.Skills.Skill[SKILL_REACTOR_TECH].SetLevel(1);
	PlayerIndex()->RPGInfo.Skills.Skill[SKILL_SHIELD_TECH].SetLevel(1);

	SaveNewSkillLevel(SKILL_BEAM_WEAPON, 1);
	SaveNewSkillLevel(SKILL_DEVICE_TECH, 1);
	SaveNewSkillLevel(SKILL_ENGINE_TECH, 1);
	SaveNewSkillLevel(SKILL_REACTOR_TECH, 1);
	SaveNewSkillLevel(SKILL_SHIELD_TECH, 1);

	SetupFactions(0, true);

	PlayerIndex()->SetRegistrationStarbase("Net-7 SOL");
	PlayerIndex()->SetRegistrationStarbaseSector("Saturn Sector (Sol System)");

	PlayerIndex()->SetMusicID(-1);
	PlayerIndex()->SetPIPAvatarID(-1);

    ItemBase * myItem = 0;

    ShipIndex()->Reset();

	ShipIndex()->SetName(m_Database.avatar.avatar_first_name);
	ShipIndex()->SetOwner(m_Database.avatar.avatar_first_name);
    ShipIndex()->SetRank(StartingRank[class_index]);

	ShipIndex()->SetHullPoints(float(HullTable[class_index * 7]));
	ShipIndex()->SetMaxHullPoints(float(HullTable[class_index * 7]));
	
	ShipIndex()->SetAcceleration(117.5f);
	ShipIndex()->SetMaxTiltRate(1.158f);
	ShipIndex()->SetMaxTurnRate(1.158f);

	ShipIndex()->SetWarpAvailable(2);

	ShipIndex()->Inventory.SetEquipMountModel("tvf01_1");

	ShipIndex()->Inventory.SetCargoSpace(BaseCargo[class_index]);
    
	for(i=0;i<BaseCargo[class_index];i++)
	{
		ShipIndex()->Inventory.CargoInv.Item[i].SetItemTemplateID(-1);
	}

    for(i=0;i<20;i++)
    {
        ShipIndex()->Inventory.AmmoInv.Item[i].SetItemTemplateID(-1);
    }

	ShipIndex()->Inventory.Mounts.SetMount(0, ShieldMount);
	ShipIndex()->Inventory.Mounts.SetMount(1, ReactorMount);
	ShipIndex()->Inventory.Mounts.SetMount(2, EngineMount);
	
	for(i=0;i<WeaponTable[class_index * 7];i++) 
	{
        AddWeapon(i+1);
	}

	for(i=0;i<DeviceTable[class_index * 7];i++)
	{
		ShipIndex()->Inventory.Mounts.SetMount(9+i, DeviceMount);
		ShipIndex()->Inventory.EquipInv.EquipItem[9+i].SetItemTemplateID(-1);
		m_DeviceSlots++;
	}

    ItemBase * Item;

	_Item *instance;

    Item = g_ItemBaseMgr->GetItem(BaseShield[race]);
	ShipIndex()->Inventory.EquipInv.EquipItem[0].SetItemTemplateID(Item->ItemTemplateID());

    Item = g_ItemBaseMgr->GetItem(BaseReactor[race]);
	ShipIndex()->Inventory.EquipInv.EquipItem[1].SetItemTemplateID(Item->ItemTemplateID());

    Item = g_ItemBaseMgr->GetItem(BaseEngine[race]);
	if (!Item) Item = g_ItemBaseMgr->GetItem(nBaseEngine[race]);

	ShipIndex()->Inventory.EquipInv.EquipItem[2].SetItemTemplateID(Item->ItemTemplateID());

    Item = g_ItemBaseMgr->GetItem(BaseWeapon[race]);
	ShipIndex()->Inventory.EquipInv.EquipItem[3].SetItemTemplateID(Item->ItemTemplateID());

	for(i=0;i<4;i++) 
	{
		ShipIndex()->Inventory.EquipInv.EquipItem[i].SetQuality(1.0f);
		ShipIndex()->Inventory.EquipInv.EquipItem[i].SetStackCount(1);
		ShipIndex()->Inventory.EquipInv.EquipItem[i].SetStructure(1.0f);
		m_Equip[i].Init(this,i);
		instance = m_Equip[i].GetItem();
		if (instance)
		{
			SaveEquipmentChange(i, instance);
		}
	}

	ShipIndex()->Inventory.SetFutureDevices(MaxDeviceSlots[class_index]);
	ShipIndex()->Inventory.SetFutureWeapons(MaxWeaponSlots[class_index]);

    ShipIndex()->Lego.Attachments.Attachment[0].SetAsset(Item->GameBaseAsset());
    ShipIndex()->Lego.Attachments.Attachment[0].SetType(2);
    ShipIndex()->Lego.Attachments.Attachment[0].SetBoneName(ShipIndex()->Inventory.MountBones.GetMountBoneName(3));

	ShipIndex()->BaseStats.SetMissleDefence(10);
	ShipIndex()->BaseStats.SetSpeed(BaseSpeed[class_index]);
	ShipIndex()->BaseStats.SetWarpSpeed(2000);
	ShipIndex()->BaseStats.SetWarpPowerLevel(3);
	ShipIndex()->BaseStats.SetTurnRate(50);
	ShipIndex()->BaseStats.SetScanRange(BaseScanRange[class_index]);
	ShipIndex()->BaseStats.SetVisibility(BaseVisableRange[class_index]);

	ShipIndex()->CurrentStats.SetMissleDefence(10);
	ShipIndex()->CurrentStats.SetSpeed(BaseSpeed[class_index]);
	ShipIndex()->CurrentStats.SetWarpSpeed(2000);
	ShipIndex()->CurrentStats.SetWarpPowerLevel(3);
	ShipIndex()->CurrentStats.SetTurnRate(50);
	ShipIndex()->CurrentStats.SetScanRange(BaseScanRange[class_index]);
	ShipIndex()->CurrentStats.SetVisibility(BaseVisableRange[class_index]);

	ShipIndex()->SetEngineTrailType(10);
	//ShipIndex()->SetFactionIdentifier(faction_list[class_index]);

	m_Database.ship_info.hull         = BaseHullAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.hull];
	m_Database.ship_info.profession   = BaseProfAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.profession];
	m_Database.ship_info.wing         = BaseWingAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.wing];
	m_Database.ship_info.engine       = BaseEngineAsset[m_Database.ship_data.race];

	LogMessage("Save data reinitialized for avatar `%s` [%d]\n", Name(), CharacterID());


    SaveData();

	CreatePositionSave();
	SaveHullUpgrade();
	SaveCreditLevel();
	SaveAdvanceLevel(0,0);
}

void Player::SaveData(bool reset_data)
{
    bool success = false;

    if (m_Hijackee)
    {
        return;
    }

    LogDebug("Saving player %d info [%s]\n", CharacterID(), Name());

	SaveHullLevelChange(ShipIndex()->GetMaxHullPoints());
}

void Player::SaveDatabase()
{
    if (m_Hijackee)
    {
        return;
    }

    //first update database with relevant info
    m_Database.info.combat_level = ntohl(PlayerIndex()->RPGInfo.GetCombatLevel());
    m_Database.info.explore_level = ntohl(PlayerIndex()->RPGInfo.GetExploreLevel());
    m_Database.info.trade_level = ntohl(PlayerIndex()->RPGInfo.GetTradeLevel());
    m_Database.info.sector_id = ntohl(PlayerIndex()->GetSectorNum());

	g_SaveMgr->AddSaveMessage(SAVE_CODE_DATABASE, m_CharacterID, sizeof(m_Database), (unsigned char *)&m_Database);
}

void Player::SavePetition(unsigned char *data, short bytes)
{
	g_SaveMgr->AddSaveMessage(SAVE_CODE_PETITION, m_CharacterID, bytes, data);
}

void Player::UpdateDatabase()
{
	unsigned char data[32];
	int index = 0;

    u32 sector_id = PlayerIndex()->GetSectorNum();//ntohl(PlayerIndex()->GetSectorNum());

	if (sector_id > 0)
	{
		//now just save the new sector_id
		AddData(data, (unsigned long)sector_id, index);
		g_SaveMgr->AddSaveMessage(SAVE_CODE_UPDATE_DATABASE, m_CharacterID, index, data);
	}
}

void Player::SaveNewSkillLevel(long skill_id, long skill_level)
{
	unsigned char skill_data[32];
	int index = 0;

	AddData(skill_data, (short)skill_id, index);
	AddData(skill_data, (short)skill_level, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_ADVANCE_SKILL, m_CharacterID, index, skill_data);
}

void Player::SaveNewRecipe(long item_id)
{
	unsigned char skill_data[32];
	int index = 0;

	AddData(skill_data, item_id, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_NEW_RECIPE, m_CharacterID, index, skill_data);
}

void Player::SaveManufactureAttempt(long item_id, float quality)
{
	unsigned char data[8];
	int index = 0;

	AddData(data, item_id, index);
	AddData(data, quality, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_NEW_ATTEMPT, m_CharacterID, index, data);
}

void Player::SaveInventoryChange(long slot)
{
	_Item *item = ShipIndex()->Inventory.CargoInv.Item[slot].GetData();
	long item_id = item->ItemTemplateID;
	short stack_level = (short)item->StackCount;
	short trade_stack = (short)item->TradeStack;
	float quality = item->Quality;
	u32 price = (u32)item->AveCost;
	float structure = item->Structure;
	char builder_name[64];
	
	// Copy name into char data
	memcpy(builder_name, item->BuilderName, sizeof(builder_name));

	unsigned char data[96];
	int index = 0;

	AddData(data, (u8)slot, index);
	AddData(data, (u8)PLAYER_INVENTORY, index);
	AddData(data, stack_level, index);
	AddData(data, trade_stack, index);
	AddData(data, quality, index);
	AddData(data, item_id, index);
	AddData(data, price, index);
	AddData(data, structure, index);
	AddBuffer(data, (unsigned char *) &builder_name, sizeof(builder_name), index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_CHANGE_INVENTORY, m_CharacterID, index, data);

	CheckMissions(0, item->ItemTemplateID, 0, OBTAIN_ITEMS);
}

void Player::SaveVaultChange(long slot)
{
	_Item *item = PlayerIndex()->SecureInv.Item[slot].GetData();
	long item_id = item->ItemTemplateID;
	short stack_level = (short)item->StackCount;
	short trade_stack = (short)item->TradeStack;
	float quality = item->Quality;
	u32 price = (u32)item->AveCost;
	float structure = item->Structure;
	char builder_name[64];
	
	// Copy name into char data
	memcpy(builder_name, item->BuilderName, sizeof(builder_name));

	unsigned char data[96];
	int index = 0;

	AddData(data, (u8)slot, index);
	AddData(data, (u8)PLAYER_VAULT, index);
	AddData(data, stack_level, index);
	AddData(data, trade_stack, index);
	AddData(data, quality, index);
	AddData(data, item_id, index);
	AddData(data, price, index);
	AddData(data, structure, index);
	AddBuffer(data, (unsigned char *) &builder_name, sizeof(builder_name), index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_CHANGE_INVENTORY, m_CharacterID, index, data);
}

void Player::SaveTradeChange(long slot)
{
	_Item *item = ShipIndex()->Inventory.TradeInv.Item[slot].GetData();
	long item_id = item->ItemTemplateID;
	short stack_level = (short)item->StackCount;
	short trade_stack = (short)item->TradeStack;
	float quality = item->Quality;
	u32 price = (u32)item->AveCost;
	float structure = item->Structure;
	char builder_name[64];
	
	// Copy name into char data
	memcpy(builder_name, item->BuilderName, sizeof(builder_name));

	unsigned char data[96];
	int index = 0;

	AddData(data, (u8)slot, index);
	AddData(data, (u8)PLAYER_TRADE, index);
	AddData(data, stack_level, index);
	AddData(data, trade_stack, index);
	AddData(data, quality, index);
	AddData(data, item_id, index);
	AddData(data, price, index);
	AddData(data, structure, index);
	AddBuffer(data, (unsigned char *) &builder_name, sizeof(builder_name), index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_CHANGE_INVENTORY, m_CharacterID, index, data);
}

void Player::SaveXPBarLevel(long xp_type, float xp_bar)
{
	unsigned char data[32];
	int index = 0;

	AddData(data, (u8)xp_type, index);
	AddData(data, xp_bar, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_AWARD_XP, m_CharacterID, index, data);
}

void Player::SaveAdvanceLevel(long xp_type, long level)
{
	unsigned char data[32];
	int index = 0;

	AddData(data, (u8)xp_type, index);
	AddData(data, level, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_ADVANCE_LEVEL, m_CharacterID, index, data);
}

void Player::SaveAdvanceMission(long slot)
{
	unsigned char data[32];
	int index = 0;

	AuxMission *m = &m_PlayerIndex.Missions.Mission[slot];
	long mission_id = m->GetDatabaseID();
	long mission_stage = m->GetStageNum();

	AddData(data, (u8)slot, index);
	AddData(data, mission_id, index);
	AddData(data, (short)mission_stage, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_ADVANCE_MISSION, m_CharacterID, index, data);
}

void Player::SaveMissionFlags(long slot)
{
	unsigned char data[32];
	int index = 0;

	AuxMission *m = &m_PlayerIndex.Missions.Mission[slot];
	long mission_id = m->GetDatabaseID();
	long mission_flags = m->GetMissionData();

	AddData(data, (u8)slot, index);
	AddData(data, mission_flags, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_MISSION_FLAGS, m_CharacterID, index, data);
}

void Player::SaveRemoveMission(long mission_id)
{
	unsigned char data[32];
	int index = 0;

	AddData(data, mission_id, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_MISSION_REMOVE, m_CharacterID, index, data);
}

void Player::ResetAllMissions()
{
	sql_query_c account_query (&m_SQL_Conn);

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `avatar_mission_progress` WHERE `avatar_id` = ?");

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `missions_completed` WHERE `avatar_id` = ?");

	for (int i=0;i < MAX_MISSIONS;i++)
		PlayerIndex()->Missions.Mission[i].Clear();
	SendAuxPlayer();
		
	memset(&m_CompletedMissions, 0, sizeof(m_CompletedMissions));
}

void Player::CompleteMission(long mission_id, long completion_flags)
{
	unsigned char data[32];
	int index = 0;

	AddData(data, mission_id, index);
	AddData(data, (u8)completion_flags, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_MISSION_COMPLETE, m_CharacterID, index, data);

	if (mission_id >= 0 && mission_id < MAX_MISSION_ID)
	{
		if (completion_flags)
			SetBitEntry(m_CompletedMissions, mission_id);
		else
			UnsetBitEntry(m_CompletedMissions, mission_id);
	}
}

void Player::SaveCreditLevel()
{
	u64 credits = PlayerIndex()->GetCredits();

	unsigned char data[32];
	int index = 0;

	AddData(data, credits, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_CREDIT_LEVEL, m_CharacterID, index, data);
}

void Player::SaveFactionChange(long faction_id, float new_value, long faction_order)
{
	unsigned char data[32];
	int index = 0;

	AddData(data, (short)faction_id, index);
	AddData(data, new_value, index);

	if (faction_order != -1)
	{
		AddData(data, (short)faction_order, index);
	}

	g_SaveMgr->AddSaveMessage(SAVE_CODE_FACTION_CHANGE, m_CharacterID, index, data);
}

void Player::SaveEquipmentChange(long slot, _Item *item)
{
	if(item == NULL || !this)return;
	long item_id = item->ItemTemplateID;
	float quality = item->Quality;
	float structure = item->Structure;
	char builder_name[64];
	
	// Copy name into char data
	memcpy(builder_name, item->BuilderName, sizeof(builder_name));

	unsigned char data[96];
	int index = 0;

	AddData(data, (u8)slot, index);
	AddData(data, quality, index);
	AddData(data, item_id, index);
	AddData(data, structure, index);
	AddBuffer(data, (unsigned char *) &builder_name, sizeof(builder_name), index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_CHANGE_EQUIPMENT, m_CharacterID, index, data);
}

void Player::SaveAmmoChange(long slot, _Item *ammo)
{
	long item_id = ammo->ItemTemplateID;
	long stack = ammo->StackCount;
	float quality = ammo->Quality;
	float structure = ammo->Structure;
	char builder_name[64];

	// Copy name into char data
	memcpy(builder_name, ammo->BuilderName, sizeof(builder_name));

	unsigned char data[96];
	int index = 0;

	AddData(data, (u8)slot, index);
	AddData(data, (short)stack, index);
	AddData(data, quality, index);
	AddData(data, item_id, index);
	AddData(data, structure, index);
	AddBuffer(data, (unsigned char *) &builder_name, sizeof(builder_name), index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_CHANGE_AMMO, m_CharacterID, index, data);
}

void Player::SaveHullLevelChange(float new_hull_level)
{
	unsigned char data[32];
	int index = 0;

	float hull_level = ShipIndex()->GetHullPoints();

	AddData(data, hull_level, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_HULL_LEVEL_CHANGE, m_CharacterID, index, data);
}

void Player::SaveHullUpgrade()
{
	ShipIndex()->SetRank(GetRank());
	u8 player_rank_name   = (u8)PlayerIndex()->RPGInfo.GetHullUpgradeLevel();
	u8 hull_upgrade_level = (u8)PlayerIndex()->RPGInfo.GetHullUpgradeLevel();
	float max_hull_points = ShipIndex()->GetMaxHullPoints();
	u8 cargo_space 		  = (u8)ShipIndex()->Inventory.GetCargoSpace();
	u8 weapon_slots		  = m_WeaponSlots;
	u8 device_slots		  = m_DeviceSlots;
	u8 warp_power_level	  = (u8)ShipIndex()->CurrentStats.GetWarpPowerLevel();
	u8 engine_thrust_type = (u8)ShipIndex()->GetEngineTrailType();

	unsigned char data[32];
	int index = 0;

	AddData(data, player_rank_name, index);
	AddData(data, hull_upgrade_level, index);
	AddData(data, max_hull_points, index);
	AddData(data, cargo_space, index);
	AddData(data, weapon_slots, index);
	AddData(data, device_slots, index);
	AddData(data, warp_power_level, index);
	AddData(data, engine_thrust_type, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_HULL_UPGRADE, m_CharacterID, index, data);
}

void Player::WipeCharacter()
{
	g_SaveMgr->AddSaveMessage(SAVE_CODE_CHARACTER_PROGRESS_WIPE, m_CharacterID, 0, 0);
}

void Player::WipeFactions()
{
	g_SaveMgr->AddSaveMessage(SAVE_CODE_FULL_FACTION_WIPE, m_CharacterID, 0, 0);
}

void Player::SaveDiscoverNav(long object_uid)
{
	unsigned char data[32];
	int index = 0;

	AddData(data, object_uid, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_DISCOVER_NAV, m_CharacterID, index, data);
}

void Player::SaveExploreNav(long object_uid)
{
	unsigned char data[32];
	int index = 0;

	AddData(data, object_uid, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_EXPLORE_NAV, m_CharacterID, index, data);
}

void Player::SaveSkillPoints()
{
	unsigned char data[32];
	int index = 0;
	long points = PlayerIndex()->RPGInfo.GetSkillPoints();

	AddData(data, points, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_SET_SKILLPOINTS, m_CharacterID, index, data);
}

void Player::SaveLogin()
{
	unsigned char data[96];
	int index = 0;

	char timestr[32];
	time_t rawtime;
	struct tm	* gmttime = NULL;
	time(&rawtime);
	gmttime = gmtime(&rawtime);
	strftime(timestr, sizeof(timestr), "%Y/%m/%d %H:%M:%S", gmttime);

	// Add time to buffer
	AddBuffer(data, (unsigned char*) timestr, sizeof(timestr), index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_LOGIN, m_CharacterID, index, data);
}

void Player::SaveLogout()
{
	unsigned char data[96];
	int index = 0;

	char timestr[32];
	time_t rawtime;
	struct tm	* gmttime = NULL;
	time(&rawtime);
	gmttime = gmtime(&rawtime);
	strftime(timestr, sizeof(timestr), "%Y/%m/%d %H:%M:%S", gmttime);

	// Add time to buffer
	AddBuffer(data, (unsigned char*) timestr, sizeof(timestr), index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_LOGOUT, m_CharacterID, index, data);
}

void Player::SaveAmmoLevels()
{
	_Item * ammo;

	for (int i = 0; i < m_WeaponSlots && i < 7; i++)
	{
		ammo = ShipIndex()->Inventory.AmmoInv.Item[i+3].GetData();
		if (ammo && ammo->StackCount > 0)
		{
			SaveAmmoChange(i+3,ammo);
		}
	}
}

void Player::SaveRegisteredStarbase()
{
	unsigned char data[32];
	int index = 0;

	AddData(data, m_RegisteredSectorID, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_SET_STARBASE, m_CharacterID, index, data);
}

void Player::SaveEnergyLevels()
{
	unsigned char data[32];
	float shield_bar = GetShield();
	float energy_bar = GetEnergy();

	if (_isnan(shield_bar)) shield_bar = 0.1f;
	if (_isnan(energy_bar)) energy_bar = 0.1f;

	int index = 0;

	AddData(data, energy_bar, index); // LOL, was wrong way round to HandleSaveEnergyLevels :O
	AddData(data, shield_bar, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_SET_ENERGY_LEVELS, m_CharacterID, index, data);
}

void Player::DeleteAllAvatarRecords()
{
	sql_query_c account_query (&m_SQL_Conn);

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `avatar_ammo` WHERE `avatar_id` = ?");

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `avatar_equipment` WHERE `avatar_id` = ?");

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `avatar_faction_level` WHERE `avatar_id` = ?");

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `avatar_inventory_items` WHERE `avatar_id` = ?");

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `avatar_mission_progress` WHERE `avatar_id` = ?");

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `avatar_skill_levels` WHERE `avatar_id` = ?");

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `avatar_vault_items` WHERE `avatar_id` = ?");

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `missions_completed` WHERE `avatar_id` = ?");

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `avatar_exploration` WHERE `avatar_id` = ?");

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `faction_data` WHERE `avatar_id` = ?");

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `avatar_recipes` WHERE `avatar_id` = ?");

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `friends_lists` WHERE `avatar_id` = ?");

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `ignore_lists` WHERE `avatar_id` = ?");

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `guild_members` WHERE `avatar_id` = ?");

	account_query.AddParam((long)m_CharacterID);
	account_query.run_query_params("DELETE FROM `guild_members` WHERE `avatar_id` = ?");
}

void Player::SaveFriendsList(char *name, bool add)
{
	unsigned char data[32];
	int index = 0;

	AddData(data, add, index);
	AddDataSN(data, name, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_FRIENDS_LIST, m_CharacterID, index, data);
}

void Player::SaveIgnoreList(char *name, bool add)
{
	unsigned char data[32];
	int index = 0;

	AddData(data, add, index);
	AddDataSN(data, name, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_IGNORE_LIST, m_CharacterID, index, data);
}

void Player::SaveAudioWarnLvl()
{
	unsigned char data[12];
	int index = 0;

	AddData(data, (long)m_SoundWarningSetting, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_NEW_WARN_LEVEL, m_CharacterID, index, data);
}

void Player::SaveXPDebt()
{
	unsigned char data[32];
	int index = 0;

	AddData(data, PlayerIndex()->GetXPDebt(), index);
	AddData(data, m_IncapXPDebt, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_XP_DEBT, m_CharacterID, index, data);
}

void Player::SaveCargoAndVault()
{
	_Item *item;
	long item_id;

	for (long x=0;x < (long)ShipIndex()->Inventory.GetCargoSpace();x++)
	{
		item = ShipIndex()->Inventory.CargoInv.Item[x].GetData();
		item_id = item->ItemTemplateID;

		if (item_id > 0)
		{
			SaveInventoryChange(x);
		}
	}

	for (long x=0;x < 96;x++)
	{
		item = PlayerIndex()->SecureInv.Item[x].GetData();
		item_id = item->ItemTemplateID;

		if (item_id > 0)
		{
			SaveVaultChange(x);
		}
	}
}

void Player::SaveGuildId(short rank)
{
	unsigned char data[32];
	int index = 0;

	AddData(data, rank, index);
	AddData(data, m_GuildID, index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_GUILD_ID, m_CharacterID, index, data);
}
