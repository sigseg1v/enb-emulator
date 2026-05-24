// ItemBaseParser.cpp
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
** Copyright of our assets/code/software began in 2005-2009 �, Net-7 Entertainment.
**
*/

#include "Net7.h"

#ifdef USE_MYSQL_ITEMS

#include "ItemBaseParser.h"
#include "StringManager.h"
#include "XmlParser.h"
#include "ItemBase.h"
#include "mysql/mysqlplus.h"
#include "StringManager.h"

long g_ItemCount;

float top_DPS_level [] =
{
	5.0f,
	4.0f,	
	7.2f,	
	14.7f,	
	24.0f,	
	41.0f,	
	74.0f,	
	131.0f,	
	198.0f,	
	310.0f,
	448.0f,
	648.0f,
	915.0f,
	1436.0f,	
	2080.0f,	
	2912.0f,	
	3965.0f,	
	5017.0f,	
	6347.0f,	
	8028.0f,	
	14933.0f
};

long leveltotal[10];
float cumulative_value[10];

sql_result_c * ItemBaseParser::SqlQuery(sql_connection_c *connection, char * QueryString)
{
	sql_query_c SqlQuery( connection );
	sql_result_c * SqlQuery_result = new sql_result_c;

	if ( !SqlQuery.execute( QueryString ) )
	{
		LogMessage( "Error executing \"%s\"\n", QueryString );
		delete SqlQuery_result;
		return 0;
	}

	SqlQuery.store(SqlQuery_result);
	return SqlQuery_result;
}

sql_result_c * ItemBaseParser::SqlQueryP1(sql_connection_c *connection, const char *sql, long param)
{
	sql_query_c q( connection );
	sql_result_c * out = new sql_result_c;

	q.AddParam(param);
	if ( !q.execute_params( sql ) )
	{
		LogMessage( "Error executing \"%s\" param=%ld\n", sql, param );
		delete out;
		return 0;
	}

	q.store(out);
	return out;
}


bool ItemBaseParser::LoadItemBase(ItemBase ** GlobalDB)
{
    bool success = false;

	memset (leveltotal, 0, sizeof(leveltotal));
	memset (cumulative_value, 0, sizeof(cumulative_value));

    ItemEffect * m_Effect = 0;
	char QueryString[1024];

	char * m_DamageType[] = {"Impact", "Explosive", "Plasma", "Energy", "Emp", "Chemical", "Absolute", "System", "Effect"};

	if(!g_MySQL_User || !g_MySQL_Pass) 
	{
		printf("You need to set a mysql user/pass in the net7.cfg\n");
		return false;
	}

	sql_connection_c connection( "net7", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
	sql_query_c ItemLists( &connection );
	sql_result_c *ItemList_result = 0;
	sql_row_c ItemList_Data;

	//char ItemListFields[] = "";
	strcpy_s(QueryString, sizeof(QueryString), 
		"SELECT * FROM `item_base` Inner Join `item_manufacturer_base` ON `item_base`.`manufacturer` = `item_manufacturer_base`.`id`");
	QueryString[sizeof(QueryString)-1] = '\0';

	// Execute Query
	ItemList_result = SqlQuery(&connection, QueryString);

	if (!ItemList_result || !ItemList_result->n_rows())
	{
		delete ItemList_result;
		return false;
	}

	LogMessage("Loading Items from SQL (%d)\n",ItemList_result->n_rows());
    ItemBaseData * m_Data = new ItemBaseData;
	// Loop though each item
	for(int item=0;item<ItemList_result->n_rows();item++)
	{
		// Get a row of data
		ItemList_result->fetch_row(&ItemList_Data);

		int ItemID = ItemList_Data["id"];
		int FieldCount = 0;

		if (!GlobalDB[ItemID])
		{
			if (!(GlobalDB[ItemID] = new ItemBase()))
			{
				printf("ItemBaseParser - Unable to allocate memory for ItemBase! ItemCount: %d\n", ItemID);
				delete ItemList_result;
				delete m_Data;
				return false;
			}
		}

		if (!(item%100)) printf("."); //progress

		// Set Base information
		memset(m_Data, 0, sizeof(ItemBaseData));
		m_Data->ItemTemplateID = ItemID;
		m_Data->Category = ItemList_Data["category"];
		m_Data->SubCategory = ItemList_Data["sub_category"];

		m_Data->Description = g_StringMgr->GetStr((char *) ItemList_Data["description"]);
		m_Data->Manufacturer = g_StringMgr->GetStr((char *) ItemList_Data["item_manufacturer_base.name"]);
		m_Data->Name = g_StringMgr->GetStr((char *) ItemList_Data["name"]);

		m_Data->Cost = ItemList_Data["price"];
		m_Data->manufactureCost = ItemList_Data["man_cost"];
		m_Data->manufacture_diff = ItemList_Data["man_dif"]; 
		m_Data->analyse_diff = ItemList_Data["ana_dif"];
		m_Data->vendorBuyPrice = ItemList_Data["buying_price"];
		m_Data->vendorSellPrice = ItemList_Data["selling_price"];
		m_Data->GameBaseAsset = ItemList_Data["3d_asset"];
		m_Data->IconBaseAsset = ItemList_Data["2d_asset"];
		m_Data->MaxStack = ItemList_Data["max_stack"];
		m_Data->TechLevel = ItemList_Data["level"];
		m_Data->ItemType = ItemList_Data["type"];
		m_Data->EquipEffect = ItemList_Data["effect_id"];

		// Flag Bit Generation
		m_Data->Flags += (int) ItemList_Data["no_trade"] == 1 ? 1 : 0;
		// 2 is missing at this time for Temporary
		m_Data->Flags += (int) ItemList_Data["unique"] == 1 ? 4 : 0;
		m_Data->Flags += (int) ItemList_Data["no_store"] == 1 ? 8 : 0;
		m_Data->Flags += (int) ItemList_Data["no_destroy"] == 1 ? 16 : 0;
		m_Data->Flags += (int) ItemList_Data["no_manu"] == 1 ? 128 : 0;


		char m_SubDiscription[120];
		int m_RaceRestric = 0;
		int m_ProfRestric = 0;
		int m_AutoFire = 0;

		sql_result_c *Item_result = 0;
		sql_row_c Item_Data;

		if (!ItemList_result->n_rows())
		{
			delete m_Data;
			delete ItemList_result;
			return false;
		}

		// Execute Query
		Item_result = SqlQueryP1(&connection, "SELECT * FROM `item_manufacture` WHERE `item_id` = ?", ItemID);
		if (Item_result && Item_result->n_rows() > 0)
		{
			// Get results
			Item_result->fetch_row(&Item_Data);

			// Main Data
			m_Data->Components[0] = Item_Data["comp_1"];
			m_Data->Components[1] = Item_Data["comp_2"];
			m_Data->Components[2] = Item_Data["comp_3"];
			m_Data->Components[3] = Item_Data["comp_4"];
			m_Data->Components[4] = Item_Data["comp_5"];
			m_Data->Components[5] = Item_Data["comp_6"];
		}
		delete Item_result;

		// Type check/loading of fields
		switch(m_Data->ItemType)
		{
			// Beams
			case 14:
				{
					// Execute Query
					Item_result = SqlQueryP1(&connection, "SELECT * FROM `item_beam` WHERE `item_id` = ?", ItemID);
					if (Item_result && Item_result->n_rows() > 0)
					{
						// Get results
						Item_result->fetch_row(&Item_Data);

						// Main Data
						int DamageType = Item_Data["damage_type"];
						m_Data->UseEffect = Item_Data["fire_effect"];
						m_Data->ItemFields[6].HasData = true;
						m_Data->ItemFields[6].iData = DamageType;
						m_Data->ItemFields[5].HasData = true;
						m_Data->ItemFields[5].iData = Item_Data["damage_100"];
						m_Data->ItemFields[19].HasData = true;
						m_Data->ItemFields[19].iData = Item_Data["range_100"];
						m_Data->ItemFields[9].HasData = true;
						m_Data->ItemFields[9].fData = Item_Data["energy_100"];
						m_Data->ItemFields[21].HasData = true;
						m_Data->ItemFields[21].fData = Item_Data["reload_100"];

						// Restrictions
						m_RaceRestric = Item_Data["rest_race"];
						m_ProfRestric = Item_Data["rest_prof"];


						m_AutoFire = 1;					// Make it show up as auto fire
						m_Data->ItemFields[11].HasData = true;
						m_Data->ItemFields[11].sData = g_StringMgr->GetStr("Beam Weapon");
						// Make Sub discription
						sprintf_s(m_SubDiscription, 120, 
							"Level %d %s Beam Weapon", m_Data->TechLevel, m_DamageType[DamageType]);
						m_Data->ItemFields[13].HasData = true;
						m_Data->ItemFields[13].sData = g_StringMgr->GetStr(m_SubDiscription);

						//check for sanity
						leveltotal[m_Data->TechLevel] += 1;
						cumulative_value[m_Data->TechLevel] += ((float)m_Data->ItemFields[5].iData / m_Data->ItemFields[21].fData);

					}
					delete Item_result;
				}
				break;
			// Projectile (Weapon)
			case 16:
				{
					// Execute Query
					Item_result = SqlQueryP1(&connection,
						"SELECT * FROM `item_projectile` Inner Join `item_ammo_type` ON `item_projectile`.`ammo_type_id` = `item_ammo_type`.`id` WHERE `item_projectile`.`item_id` = ?", ItemID);
					if (Item_result && Item_result->n_rows() > 0)
					{
						// Get results
						Item_result->fetch_row(&Item_Data);

						// Set ammo Type
						m_Data->ItemFields[1].HasData = true;
						m_Data->ItemFields[1].sData = g_StringMgr->GetStr((char *) Item_Data["item_ammo_type.name"]);

						// Main Data
						m_Data->ItemFields[9].HasData = true;
						m_Data->ItemFields[9].fData = Item_Data["energy_100"];
						m_Data->ItemFields[19].HasData = true;
						m_Data->ItemFields[19].iData = Item_Data["range_100"];
						m_Data->ItemFields[21].HasData = true;
						m_Data->ItemFields[21].fData = Item_Data["reload_100"];
						m_Data->ItemFields[22].HasData = true;
						m_Data->ItemFields[22].iData = Item_Data["ammo_per_shot"];

						// Restrictions
						m_RaceRestric = Item_Data["rest_race"];
						m_ProfRestric = Item_Data["rest_prof"];

						m_AutoFire = 1;					// Make it show up as auto fire
						m_Data->ItemFields[11].HasData = true;
						m_Data->ItemFields[11].sData = g_StringMgr->GetStr("Projectile Weapon");

						// Make Sub discription
						sprintf_s(m_SubDiscription, 120, "Level %d Projectile Weapon", m_Data->TechLevel);
						m_Data->ItemFields[13].HasData = true;
						m_Data->ItemFields[13].sData = g_StringMgr->GetStr(m_SubDiscription);
					}
					delete Item_result;
				}
				break;
			// Missile (Weapon) 
			case 15:
				{
					// Execute Query
					Item_result = SqlQueryP1(&connection,
						"SELECT * FROM `item_missile` Inner Join `item_ammo_type` ON `item_missile`.`ammo_type_id` = `item_ammo_type`.`id` WHERE `item_missile`.`item_id` = ?", ItemID);
					if (Item_result && Item_result->n_rows() > 0)
					{
						// Get results
						Item_result->fetch_row(&Item_Data);

						// Set ammo Type
						m_Data->ItemFields[1].HasData = true;
						m_Data->ItemFields[1].sData = g_StringMgr->GetStr((char *) Item_Data["item_ammo_type.name"]);

						// Main Data
						m_Data->ItemFields[9].HasData = true;
						m_Data->ItemFields[9].fData = Item_Data["energy_100"];
						m_Data->ItemFields[21].HasData = true;
						m_Data->ItemFields[21].fData = Item_Data["reload_100"];
						m_Data->ItemFields[22].HasData = true;
						m_Data->ItemFields[22].iData = Item_Data["ammo_per_shot"];

						// Restrictions
						m_RaceRestric = Item_Data["rest_race"];
						m_ProfRestric = Item_Data["rest_prof"];

						m_AutoFire = 1;					// Make it show up as auto fire
						m_Data->ItemFields[11].HasData = true;
						m_Data->ItemFields[11].sData = g_StringMgr->GetStr("Missile Weapon");

						// Make Sub discription
						sprintf_s(m_SubDiscription, 120, "Level %d Missile Weapon", m_Data->TechLevel);
						m_Data->ItemFields[13].HasData = true;
						m_Data->ItemFields[13].sData = g_StringMgr->GetStr(m_SubDiscription);
					}
					delete Item_result;
				}
				break;
			// Device 
			case 11:
				{
					// Execute Query
					Item_result = SqlQueryP1(&connection,
						"SELECT a.*,b._Range,b.EnergyUse FROM item_device as a LEFT JOIN item_effect_container AS b ON a.item_id=b.itemid WHERE a.item_id = ?", ItemID);
					if (Item_result && Item_result->n_rows() > 0)
					{
						// Get results
						Item_result->fetch_row(&Item_Data);

						// Main Data
						m_Data->ItemFields[9].HasData = true;
						m_Data->ItemFields[7].HasData = true;
						// For devices pull range from item_effect_container instead
						m_Data->ItemFields[9].fData = Item_Data["EnergyUse"];
						m_Data->ItemFields[7].iData = Item_Data["_Range"];

						// Restrictions
						m_RaceRestric = Item_Data["rest_race"];
						m_ProfRestric = Item_Data["rest_prof"];

						m_AutoFire = 0;					// Make it show up as auto fire
						m_Data->ItemFields[11].HasData = true;
						m_Data->ItemFields[11].sData = g_StringMgr->GetStr("Device Tech");

						// Make Sub discription
						sprintf_s(m_SubDiscription, 120, "Level %d Equipable", m_Data->TechLevel);
						m_Data->ItemFields[13].HasData = true;
						m_Data->ItemFields[13].sData = g_StringMgr->GetStr(m_SubDiscription);
					}
					delete Item_result;
				}
				break;
			// Reactor 
			case 7:
				{
					// Execute Query
					Item_result = SqlQueryP1(&connection,
						"SELECT * FROM `item_reactor` WHERE `item_reactor`.`item_id` = ?", ItemID);
					if (Item_result && Item_result->n_rows() > 0)
					{
						// Get results
						Item_result->fetch_row(&Item_Data);

						// Main Data
						m_Data->ItemFields[16].HasData = true;
						m_Data->ItemFields[16].iData = Item_Data["cap_100"];

						m_Data->ItemFields[20].HasData = true;
						m_Data->ItemFields[20].fData = Item_Data["recharge_100"];

						// TODO: When using activatables need to display energy

						// Restrictions
						m_RaceRestric = Item_Data["rest_race"];
						m_ProfRestric = Item_Data["rest_prof"];

						m_AutoFire = 0;					// Make it show up as auto fire
						m_Data->ItemFields[11].HasData = true;
						m_Data->ItemFields[11].sData = g_StringMgr->GetStr("Reactor Tech");

						// Make Sub discription
						sprintf_s(m_SubDiscription, 120, "Level %d Reactor", m_Data->TechLevel);
						m_Data->ItemFields[13].HasData = true;
						m_Data->ItemFields[13].sData = g_StringMgr->GetStr(m_SubDiscription);
					}
					delete Item_result;
				}
				break;
			// Engine 
			case 6:
				{
					// Execute Query
					Item_result = SqlQueryP1(&connection,
						"SELECT * FROM `item_engine` WHERE `item_engine`.`item_id` = ?", ItemID);
					if (Item_result && Item_result->n_rows() > 0)
					{
						// Get results
						Item_result->fetch_row(&Item_Data);

						// Main Data
						m_Data->ItemFields[29].HasData = true;
						m_Data->ItemFields[29].iData = Item_Data["signature_100"];

						m_Data->ItemFields[31].HasData = true;
						m_Data->ItemFields[31].iData = Item_Data["thrust_100"];

						m_Data->ItemFields[33].HasData = true;
						m_Data->ItemFields[33].iData = Item_Data["warp"];

						m_Data->ItemFields[34].HasData = true;
						m_Data->ItemFields[34].fData = Item_Data["warp_drain_100"];

						// TODO: When using activatables need to display energy

						// Restrictions
						m_RaceRestric = Item_Data["rest_race"];
						m_ProfRestric = Item_Data["rest_prof"];

						m_AutoFire = 0;					// Make it show up as auto fire
						m_Data->ItemFields[11].HasData = true;
						m_Data->ItemFields[11].sData = g_StringMgr->GetStr("Engine Tech");

						// Make Sub discription
						sprintf_s(m_SubDiscription, 120, "Level %d Engine", m_Data->TechLevel);
						m_Data->ItemFields[13].HasData = true;
						m_Data->ItemFields[13].sData = g_StringMgr->GetStr(m_SubDiscription);
					}
					delete Item_result;
				}
				break;
			// Shield 
			case 2:
				{
					// Execute Query
					Item_result = SqlQueryP1(&connection,
						"SELECT * FROM `item_shield` WHERE `item_shield`.`item_id` = ?", ItemID);
					if (Item_result && Item_result->n_rows() > 0)
					{
						// Get results
						Item_result->fetch_row(&Item_Data);

						// Main Data
						m_Data->ItemFields[24].HasData = true;
						m_Data->ItemFields[24].iData = Item_Data["cap_100"];

						m_Data->ItemFields[26].HasData = true;
						m_Data->ItemFields[26].fData = Item_Data["recharge_100"];

						// TODO: When using activatables need to display energy

						// Restrictions
						m_RaceRestric = Item_Data["rest_race"];
						m_ProfRestric = Item_Data["rest_prof"];

						m_AutoFire = 0;					// Make it show up as auto fire
						m_Data->ItemFields[11].HasData = true;
						m_Data->ItemFields[11].sData = g_StringMgr->GetStr("Shield Tech");

						// Make Sub discription
						sprintf_s(m_SubDiscription, 120, "Level %d Shield", m_Data->TechLevel);
						m_Data->ItemFields[13].HasData = true;
						m_Data->ItemFields[13].sData = g_StringMgr->GetStr(m_SubDiscription);
					}
					delete Item_result;
				}
				break;
			// Ammunition 
			case 10:
				{
					// Execute Query
					Item_result = SqlQueryP1(&connection,
						"SELECT * FROM `item_ammo` Inner Join `item_ammo_type` ON `item_ammo`.`ammo_type_id` = `item_ammo_type`.`id` WHERE `item_ammo`.`item_id` = ?", ItemID);
					if (Item_result && Item_result->n_rows() > 0)
					{
						// Get results
						Item_result->fetch_row(&Item_Data);

						int AmmoTypeNum = Item_Data["item_ammo_type.sub_category"];

						int DamageType = Item_Data["damage_type"];
						m_Data->UseEffect = Item_Data["fire_effect"];

						// Main Data
						m_Data->ItemFields[6].HasData = true;
						m_Data->ItemFields[6].iData = DamageType;

						m_Data->ItemFields[5].HasData = true;
						m_Data->ItemFields[5].iData = Item_Data["damage_100"];

						// Set ammo Type
						m_Data->ItemFields[1].HasData = true;
						m_Data->ItemFields[1].sData = g_StringMgr->GetStr((char *) Item_Data["item_ammo_type.name"]);

						m_AutoFire = 0;					// Make it show up as auto fire
						m_Data->ItemFields[11].HasData = true;

						if (AmmoTypeNum == 102)
						{
							// Send Range for Missiles
							m_Data->ItemFields[19].HasData = true;
							m_Data->ItemFields[19].iData = Item_Data["range_100"];

							// Send Manoverability
							m_Data->ItemFields[15].HasData = true;
							m_Data->ItemFields[15].iData = Item_Data["maneuv_100"];

							m_Data->ItemFields[11].sData = g_StringMgr->GetStr("Missile Weapon");
						}
						else
						{
							m_Data->ItemFields[11].sData = g_StringMgr->GetStr("Projectile Weapon");
						}

						// Make Sub discription
						sprintf_s(m_SubDiscription, 120, "Level %d %s %s", 
							m_Data->TechLevel, m_DamageType[DamageType], AmmoTypeNum == 102 ? "MISSILE" : "PROJECTILE");
						m_Data->ItemFields[13].HasData = true;
						m_Data->ItemFields[13].sData = g_StringMgr->GetStr(m_SubDiscription);
					}
					delete Item_result;
				}
				break;
		}

		// Profession and Race Restriction
		if (m_AutoFire != 0)
		{
			m_Data->ItemFields[2].HasData = true;
			m_Data->ItemFields[2].iData = m_AutoFire;
		}
		m_Data->ItemFields[3].HasData = true;
		m_Data->ItemFields[3].iData = m_ProfRestric;
		m_Data->ItemFields[17].HasData = true;
		m_Data->ItemFields[17].iData = 0;
		m_Data->ItemFields[18].HasData = true;
		m_Data->ItemFields[18].iData = m_RaceRestric;

		// Load Effects


		// Load in Effects
		sql_result_c *Effect_Container_result = 0;
		sql_row_c Effect_Container_Data;

		// Execute Query
		Effect_Container_result = SqlQueryP1(&connection, "SELECT * FROM item_effect_container WHERE `ItemID` = ?", ItemID);
		if (Effect_Container_result && Effect_Container_result->n_rows() > 0)
		{
			// Get results
			Effect_Container_result->fetch_row(&Effect_Container_Data);

			// Main Data
			int EffectContainerID = Effect_Container_Data["EffectContainerID"];

			int EEffectCount = 0;
			int AEffectCount = 0;

			// Setup Activatable Effects
			m_Data->ActivatableEffects.EffectRange = Effect_Container_Data["_Range"];
			m_Data->ActivatableEffects.RechargeTime = Effect_Container_Data["RechargeTime"]; 
			m_Data->ActivatableEffects.EnergyUse = Effect_Container_Data["EnergyUse"];

			// Read in all the effect ID's and data
			sql_result_c *Effect_List_result = 0;
			sql_row_c Effect_List_Data;

			// Execute Query
			Effect_List_result = SqlQueryP1(&connection, "SELECT * FROM item_effects Inner Join item_effect_base ON item_effects.item_effect_base_id = item_effect_base.EffectID WHERE `ItemID` = ?", ItemID);
			if (Effect_List_result && Effect_List_result->n_rows() > 0)
			{
				for(int eff=0;eff<Effect_List_result->n_rows();eff++)
				{
					// Get results
					Effect_List_result->fetch_row(&Effect_List_Data);

					int EffectType = Effect_List_Data["EffectType"];

					// Activatable
					if (EffectType == 1)
					{
						// Incroment count
						AEffectCount++;

						m_Data->ActivatableEffects.Effects[AEffectCount-1].Description = g_StringMgr->GetStr((char *) Effect_List_Data["Description"]);
						m_Data->ActivatableEffects.Effects[AEffectCount-1].Tooltip = g_StringMgr->GetStr((char *) Effect_List_Data["Tooltip"]);
						m_Data->ActivatableEffects.Effects[AEffectCount-1].Name = g_StringMgr->GetStr((char *) Effect_List_Data["Name"]);
						m_Data->ActivatableEffects.Effects[AEffectCount-1].BuffName = g_StringMgr->GetStr((char *) Effect_List_Data["Buff_Name"]);
						m_Data->ActivatableEffects.Effects[AEffectCount-1].Flag1 = Effect_List_Data["flag1"];
						m_Data->ActivatableEffects.Effects[AEffectCount-1].Flag2 = Effect_List_Data["flag2"];
						m_Data->ActivatableEffects.Effects[AEffectCount-1].VisualEffect = Effect_List_Data["VisualEffect"];
						m_Data->ActivatableEffects.Effects[AEffectCount-1].ObjectToObject = (char)Effect_List_Data["O2OEffect"] != 0;

						// Varables
						m_Data->ActivatableEffects.Effects[AEffectCount-1].VarStats[0] = g_StringMgr->GetStr((char *) Effect_List_Data["Var1Stat"]);
						m_Data->ActivatableEffects.Effects[AEffectCount-1].VarStats[1] = g_StringMgr->GetStr((char *) Effect_List_Data["Var2Stat"]);
						m_Data->ActivatableEffects.Effects[AEffectCount-1].VarStats[2] = g_StringMgr->GetStr((char *) Effect_List_Data["Var3Stat"]);
						m_Data->ActivatableEffects.Effects[AEffectCount-1].VarType[0] = Effect_List_Data["Var1Type"];
						m_Data->ActivatableEffects.Effects[AEffectCount-1].VarType[1] = Effect_List_Data["Var2Type"];
						m_Data->ActivatableEffects.Effects[AEffectCount-1].VarType[2] = Effect_List_Data["Var3Type"];
						m_Data->ActivatableEffects.Effects[AEffectCount-1].DescVar[0] = Effect_List_Data["Var1Data"];
						m_Data->ActivatableEffects.Effects[AEffectCount-1].DescVar[1] = Effect_List_Data["Var2Data"];
						m_Data->ActivatableEffects.Effects[AEffectCount-1].DescVar[2] = Effect_List_Data["Var3Data"];
						m_Data->ActivatableEffects.Effects[AEffectCount-1].DescVarCount = 3;

						// Constants
						m_Data->ActivatableEffects.Effects[AEffectCount-1].ConstStats[0] = g_StringMgr->GetStr((char *) Effect_List_Data["Constant1Stat"]);
						m_Data->ActivatableEffects.Effects[AEffectCount-1].ConstStats[1] = g_StringMgr->GetStr((char *) Effect_List_Data["Constant2Stat"]);
						m_Data->ActivatableEffects.Effects[AEffectCount-1].ConstType[0] = Effect_List_Data["Constant1Type"];
						m_Data->ActivatableEffects.Effects[AEffectCount-1].ConstType[1] = Effect_List_Data["Constant2Type"];
						m_Data->ActivatableEffects.Effects[AEffectCount-1].ConstValue[0] = Effect_List_Data["Constant1Value"];
						m_Data->ActivatableEffects.Effects[AEffectCount-1].ConstValue[1] = Effect_List_Data["Constant2Value"];

						// Mod
						m_Data->ActivatableEffects.Effects[AEffectCount-1].VarMod[0] = Effect_List_Data["Var1_mod"];
						m_Data->ActivatableEffects.Effects[AEffectCount-1].VarMod[1] = Effect_List_Data["Var2_mod"];
						m_Data->ActivatableEffects.Effects[AEffectCount-1].VarMod[2] = Effect_List_Data["Var3_mod"];

					}
					else
					{
						// Equipable
						// Incroment count
						EEffectCount++;

						m_Data->EquipableEffects.Effects[EEffectCount-1].Description = g_StringMgr->GetStr((char *) Effect_List_Data["Description"]);
						m_Data->EquipableEffects.Effects[EEffectCount-1].Tooltip = g_StringMgr->GetStr((char *) Effect_List_Data["Tooltip"]);
						m_Data->EquipableEffects.Effects[EEffectCount-1].Name = g_StringMgr->GetStr((char *) Effect_List_Data["Name"]);
						m_Data->EquipableEffects.Effects[EEffectCount-1].Flag1 = Effect_List_Data["flag1"];
						m_Data->EquipableEffects.Effects[EEffectCount-1].Flag2 = Effect_List_Data["flag2"];
						m_Data->EquipableEffects.Effects[EEffectCount-1].VisualEffect = Effect_List_Data["VisualEffect"];

						// Varables
						m_Data->EquipableEffects.Effects[EEffectCount-1].VarStats[0] = g_StringMgr->GetStr((char *) Effect_List_Data["Var1Stat"]);
						m_Data->EquipableEffects.Effects[EEffectCount-1].VarStats[1] = g_StringMgr->GetStr((char *) Effect_List_Data["Var2Stat"]);
						m_Data->EquipableEffects.Effects[EEffectCount-1].VarStats[2] = g_StringMgr->GetStr((char *) Effect_List_Data["Var3Stat"]);
						m_Data->EquipableEffects.Effects[EEffectCount-1].VarType[0] = Effect_List_Data["Var1Type"];
						m_Data->EquipableEffects.Effects[EEffectCount-1].VarType[1] = Effect_List_Data["Var2Type"];
						m_Data->EquipableEffects.Effects[EEffectCount-1].VarType[2] = Effect_List_Data["Var3Type"];
						m_Data->EquipableEffects.Effects[EEffectCount-1].DescVar[0] = Effect_List_Data["Var1Data"];
						m_Data->EquipableEffects.Effects[EEffectCount-1].DescVar[1] = Effect_List_Data["Var2Data"];
						m_Data->EquipableEffects.Effects[EEffectCount-1].DescVar[2] = Effect_List_Data["Var3Data"];
						m_Data->EquipableEffects.Effects[EEffectCount-1].DescVarCount = 3;
						// Constants
						m_Data->EquipableEffects.Effects[EEffectCount-1].ConstStats[0] = g_StringMgr->GetStr((char *) Effect_List_Data["Constant1Stat"]);
						m_Data->EquipableEffects.Effects[EEffectCount-1].ConstStats[1] = g_StringMgr->GetStr((char *) Effect_List_Data["Constant2Stat"]);
						m_Data->EquipableEffects.Effects[EEffectCount-1].ConstType[0] = Effect_List_Data["Constant1Type"];
						m_Data->EquipableEffects.Effects[EEffectCount-1].ConstType[1] = Effect_List_Data["Constant2Type"];
						m_Data->EquipableEffects.Effects[EEffectCount-1].ConstValue[0] = Effect_List_Data["Constant1Value"];
						m_Data->EquipableEffects.Effects[EEffectCount-1].ConstValue[1] = Effect_List_Data["Constant2Value"];

						// Mod
						m_Data->EquipableEffects.Effects[EEffectCount-1].VarMod[0] = Effect_List_Data["Var1_mod"];
						m_Data->EquipableEffects.Effects[EEffectCount-1].VarMod[1] = Effect_List_Data["Var2_mod"];
						m_Data->EquipableEffects.Effects[EEffectCount-1].VarMod[2] = Effect_List_Data["Var3_mod"];
					}

				}

				m_Data->EquipableEffects.Count = EEffectCount;
				m_Data->ActivatableEffects.Count = AEffectCount;
			}
			delete Effect_List_result;
		}
		delete Effect_Container_result;

		// -------------------


		// Loop thought to set the correct field count
		for(int u=0;u<38;u++)
		{
			if (m_Data->ItemFields[u].HasData)
			{
				m_Data->ItemFieldCount++;
			}
		}

		// Save the data
		GlobalDB[ItemID]->SetData(m_Data);
	}
	delete ItemList_result;
	delete m_Data;

	printf("done\n");

	for (int counter = 1; counter < 10; counter++)
	{
		printf("Average DPS for Level %d beam weapons: %.2f\n", counter, (cumulative_value[counter]/(float)leveltotal[counter]));
	}

    return (success);
}
#endif

