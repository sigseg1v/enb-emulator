// MOBDatabaseSQL.cpp
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

#ifdef USE_MYSQL_SECTOR

#include "MOBDatabase.h"
#include "mysql/mysqlplus.h"
#include "StringManager.h"


MOBContent::MOBContent()
{
    m_highest_id = 0;
    m_MOB.clear();
	m_updating = false;
}

MOBContent::~MOBContent()
{
	for (MOBList::iterator itrAList = m_MOB.begin(); itrAList != m_MOB.end(); ++itrAList) 
		if (itrAList->second) // [0] is blank
		{
			for (MOBLoot::iterator itrLList = itrAList->second->m_Loot.begin(); itrLList < itrAList->second->m_Loot.end(); ++itrLList) 
				delete *itrLList;
			delete itrAList->second;
		}
    m_MOB.clear();
}

bool MOBContent::LoadMOBContent()
{
    long current_mob_id;
    MOBData *current_mob;
	LootNode *current_loot;
	char QueryString[1024];

	if (m_updating) return false;

	m_updating = true;

	if(!g_MySQL_User || !g_MySQL_Pass) 
    {
		LogMessage("You need to set a mysql user/pass in the net7.cfg\n");
		return false;
	}

	sql_connection_c connection( "net7", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
	sql_query_c MOBTable( &connection );
    sql_result_c result;
	sql_result_c *mob_result = &result;

    strcpy_s(QueryString, sizeof(QueryString), "SELECT * FROM `mob_base`");
	QueryString[sizeof(QueryString)-1] = '\0';

    if ( !MOBTable.execute( QueryString ) )
    {
        LogMessage( "MySQL Login error/Database error: (User: %s Pass: %s)\n", g_MySQL_User, g_MySQL_Pass );
        return false;
    }
    
    MOBTable.store(mob_result);
    
    if (!mob_result->n_rows() || !mob_result->n_fields()) 
    {
        LogMessage("Error loading rows/fields\n");
        return false;
    }
    
    LogMessage("Loading MOBs from SQL (%d)\n", (int)mob_result->n_rows());
    
	sql_row_c MOBSQLData;
	for(int x=0;x<mob_result->n_rows();x++)
	{
		mob_result->fetch_row(&MOBSQLData);
        current_mob_id = (long)MOBSQLData["mob_id"];
		current_mob = m_MOB[current_mob_id];
		if (!current_mob)
		{
			current_mob = new MOBData;
		}

        current_mob->m_Name = g_StringMgr->GetStr((char*)MOBSQLData["name"]);
        current_mob->m_Level = (int)MOBSQLData["level"];
        current_mob->m_Type = (int)MOBSQLData["type"];
        current_mob->m_Agressiveness = (int)MOBSQLData["aggressiveness"];
        current_mob->m_Basset = (int)MOBSQLData["base_asset_id"];
        current_mob->m_Altruism = (int)MOBSQLData["altruism"];
        current_mob->m_FactionID = (int)MOBSQLData["faction_id"];
        current_mob->m_Bravery = (int)MOBSQLData["bravery"];
        current_mob->m_Intelligence = (int)MOBSQLData["intelligence"];
        current_mob->m_Scale = (float)MOBSQLData["scale"];
        current_mob->m_HSV[0]= (float)MOBSQLData["h"];
        current_mob->m_HSV[1]= (float)0.0f;//MOBSQLData["s"]; //don't use these for now - cause bad effects.
        current_mob->m_HSV[2]= (float)0.0f;//MOBSQLData["v"];
		current_mob->m_SkillIDs[0] = (int)MOBSQLData["skill0"];
		current_mob->m_SkillIDs[1] = (int)MOBSQLData["skill1"];
		current_mob->m_SkillIDs[2] = (int)MOBSQLData["skill2"];
		current_mob->m_SkillIDs[3] = (int)MOBSQLData["skill3"];
		current_mob->m_SkillIDs[4] = (int)MOBSQLData["skill4"];
		current_mob->m_SkillIDs[5] = (int)MOBSQLData["skill5"];
		current_mob->m_SkillIDs[6] = (int)MOBSQLData["skill6"];
		current_mob->m_SkillIDs[7] = (int)MOBSQLData["skill7"];
		current_mob->m_SkillIDs[8] = (int)MOBSQLData["skill8"];
		current_mob->m_SkillIDs[9] = (int)MOBSQLData["skill9"];
		current_mob->m_SkillChance = (int)MOBSQLData["skillchance"];
		current_mob->m_SkillCooldown = (int)MOBSQLData["skillcooldown"];
		current_mob->m_ShieldModifier = (float)MOBSQLData["shield_modifier"];
		current_mob->m_DamageModifier = (float)MOBSQLData["damage_modifier"];
		current_mob->m_RangeModifier = (float)MOBSQLData["range_modifier"];
		current_mob->m_NumSkills = 0;
		current_mob->m_MOB_ID = current_mob_id;
		for (int i=0;i<SKILL_LIST_SIZE;i++)
		{
			if (current_mob->m_SkillIDs[i] != -1)
			{
				current_mob->m_NumSkills = i+1;
			}
		}
		current_mob->m_CreateWarning = false;

        //patch MOB basset if not yet set
        if (current_mob->m_Basset == 65535)
        {
            current_mob->m_Basset = 1134; //set as evil mushroom
        }

        if (current_mob->m_Scale == 0.0f)
        {
            current_mob->m_Scale = 1.0f;
        }

        m_MOB[current_mob_id] = current_mob; //add to MOB map

        if (current_mob_id > m_highest_id)
        {
            m_highest_id = current_mob_id;
        }
		
		if (current_mob->m_Loot.size() != 0) //invalidate any loot
		{
			for (u32 i = 0; i < current_mob->m_Loot.size(); i++)
			{
				current_mob->m_Loot[i]->item_base_id = -1;
			}
		}
    }

	//now read in any Loot
    strcpy_s(QueryString, sizeof(QueryString), "SELECT * FROM `mob_items`");
	QueryString[sizeof(QueryString)-1] = '\0';

    if ( !MOBTable.execute( QueryString ) )
    {
        LogMessage( "MySQL Login error/Database error: (User: %s Pass: %s)\n", g_MySQL_User, g_MySQL_Pass );
        return false;
    }
    
    MOBTable.store(mob_result);
    
    if (!mob_result->n_rows() || !mob_result->n_fields()) 
    {
        LogMessage("Error loading rows/fields for MOB items\n");
        return false;
    }

	u32 current_mob_loot_index = 0;
	long previous_mob_id = -1;
        
	for(u32 x=0;x<mob_result->n_rows();x++)
	{
		mob_result->fetch_row(&MOBSQLData);
		current_mob_id = (long)MOBSQLData["mob_id"];
		current_mob = m_MOB[current_mob_id];

		if (current_mob)
		{
			current_loot = 0;
			//find first available entry on list or create new entry
			if (current_mob->m_Loot.size() != 0)
			{
				for (u32 i = 0; i < current_mob->m_Loot.size(); i++)
				{
					if (current_mob->m_Loot[i]->item_base_id == -1)
					{
						current_loot = current_mob->m_Loot[i];
						break;
					}
				}
			}

			if (current_loot == 0)
			{
				//need to add new loot node
				current_loot = new LootNode;
				current_mob->m_Loot.push_back(current_loot);
			}

			current_loot->item_base_id= (long)MOBSQLData["item_base_id"];
			float drop_chance = (float)MOBSQLData["drop_chance"];
			float usage_chance= (float)MOBSQLData["usage_chance"];
			long quantity	 = (long)MOBSQLData["qty"];
			long type		 = (long)MOBSQLData["type"];

			current_loot->drop_chance = (unsigned char)drop_chance;
			current_loot->usage_chance= (unsigned char)usage_chance;
			current_loot->quantity	  = (unsigned char)quantity;
			current_loot->type		  = (unsigned char)type;
		}
	}

	m_updating = false;

    return true;
}

MOBData * MOBContent::GetMOBData(long MOB_id)
{
    return (m_MOB[MOB_id]);
}

MOBData * MOBContent::GetMOBDataFromBasset(short MOB_basset)
{
	MOBList::const_iterator itrMOB;
	MOBData *mob_data = 0;

	for(itrMOB = m_MOB.begin(); itrMOB != m_MOB.end(); ++itrMOB)
	{
		if ((itrMOB->second) && itrMOB->second->m_Basset == MOB_basset)
		{
			mob_data = itrMOB->second;
			break;
		}
	}

	return mob_data;
}

MOBData * MOBContent::GetMOBforLevel(long level)
{
	MOBList::const_iterator itrMOB;
	MOBData *mob_data = 0;

	for(itrMOB = m_MOB.begin(); itrMOB != m_MOB.end(); ++itrMOB)
	{
		if ((itrMOB->second) && (itrMOB->second->m_Level < level + 1 && itrMOB->second->m_Level > level - 1))
		{
			mob_data = itrMOB->second;
			if (rand()%100 > 75) break;
		}
	}

	return mob_data;
}

long MOBContent::GetMOBIDFromBasset(short MOB_basset)
{
	MOBList::const_iterator itrMOB;
	long mob_id = 0;

	for(itrMOB = m_MOB.begin(); itrMOB != m_MOB.end(); ++itrMOB)
	{
		if ((itrMOB->second) && itrMOB->second->m_Basset == MOB_basset)
		{
			mob_id = itrMOB->first;
			break;
		}
	}

	return mob_id;
}

long MOBContent::GetMOBCount()
{
    return (m_highest_id);
}

#endif