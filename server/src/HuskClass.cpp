// HuskClass.cpp
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
#include "MemoryHandler.h"
#include "ObjectClass.h"
#include "HuskClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"
#include <float.h>
#include <math.h>
#include <vector>

// Array length
#define length(a) ( sizeof ( a ) / sizeof ( *a ) )

// These trash components drop in certain loot situations as random trash
// and are mostly building components - need to go in DB eventually
/*
int trash1[] = {1222,1182,1184,1190,1207};
int trash2[] = {1272,1273,1274,1275,1276,1318};
int trash3[] = {1321,1322,1323,1324,1325};
int trash4[] = {1381,1382,1383,1384,1391};
int trash5[] = {1441,1442,1443,1444,1445,1446,1447};
int trash6[] = {1496,1495,1499,1500,1501,1504,1505,1507,1524};
int trash7[] = {1562,1564,1565,1567,1568};
int trash8[] = {1193,1195,1620,1621,1623,1626};
int trash9[] = {1666,1667,1668,1673,1681,1684,1690,1692,1693,1695,5200};
*/

//shortened loot tables
int trash1[] = {1182,1184,1190};
int trash2[] = {1273,1274,1275};
int trash3[] = {1321,1322,1323};
int trash4[] = {1381,1382,1383,1384};
int trash5[] = {1443,1444,1445};
int trash6[] = {1496,1499,1500,1501};
int trash7[] = {1562,1564,1565,1566};
int trash8[] = {1193,1195,1623,1626};
int trash9[] = {1673,1681,1684,5200};

Husk::Husk(long object_id) : Object (object_id)
{
	m_Type = OT_HUSK;
	m_Position_info.type = POSITION_CONSTANT;
	m_Content_count = 0;
	m_Resource_value = 0;
	m_Respawn_tick = 0;
	m_Resource_type = 0;
	m_CreateInfo.Type = 38;
	m_Resource_remains = 0.0f;
	m_Resource_start_value = 0;
	memset(m_Resource_contents, 0, MAX_ITEMS_PER_RESOURCE*sizeof(ContentSlot));
	memset(&m_ObjectTimeSlot, 0, sizeof(m_ObjectTimeSlot));
	m_HuskCredits = 0;
}

Husk::Husk() : Object (0)
{
	m_Type = OT_HUSK;
	m_Position_info.type = POSITION_CONSTANT;
	m_Content_count = 0;
	m_Resource_value = 0;
	m_Respawn_tick = 0;
	m_Resource_type = 0;
	m_CreateInfo.Type = 38;
	m_Resource_remains = 0.0f;
	m_Resource_start_value = 0;
	memset(m_Resource_contents, 0, MAX_ITEMS_PER_RESOURCE*sizeof(ContentSlot));
	memset(&m_ObjectTimeSlot, 0, sizeof(m_ObjectTimeSlot));
	m_HuskCredits = 0;
}

Husk::~Husk()
{
	// TODO: destroy everything
}

void Husk::ResetObjectContents()
{
	m_Content_count = 0;
	m_Resource_value = 0;
	m_Respawn_tick = 0;
	m_Resource_type = 0;
	m_CreateInfo.Type = 38;
	m_Resource_remains = 0.0f;
	m_Resource_start_value = 0;
	memset(m_Resource_contents, 0, MAX_ITEMS_PER_RESOURCE*sizeof(ContentSlot));
	memset(&m_ObjectTimeSlot, 0, sizeof(m_ObjectTimeSlot));
	m_HuskCredits = 0;
}

void Husk::SetLevel(short level)
{
	float obj_colour;

	switch (level)
	{
	case 1:
		obj_colour = 180.0f;
		break;
	case 2:
		obj_colour = 120.0f;
		break;
	case 3:
		obj_colour = 30.0f;
		break;
	case 4:
		obj_colour = 20.0f;
		break;
	case 5:
		obj_colour = 10.0f;
		break;
	case 6:
		obj_colour = 0.0f;
		break;
	case 7:
		obj_colour = -60.0f;
		break;
	case 8:
		obj_colour = -70.0f;
		break;
	case 9:
		obj_colour = -100.0f;
		break;
	case 10:
		obj_colour = -110.0f;
		break;
	case 11:
		obj_colour = -120.0f;
		break;

	default:
		obj_colour = 180.0f;
		level = 1;
		break;
	}

	m_Level = level;
	m_CreateInfo.HSV[0] = obj_colour; //NB: this can still be overriden easily with a SetColour
}

void Husk::SetBasset(short basset)
{
	m_CreateInfo.BaseAsset = basset;
	AssetData *asset = g_ServerMgr->AssetList()->GetAssetData(basset);

	m_Type = OT_HUSK;
	if (asset->m_Name[0] == 'O')
	{
		m_Resource_type = ORGANIC_HULK;
	}
	else
	{
		m_Resource_type = INORGANIC_HULK;
	}

	switch (m_CreateInfo.BaseAsset)
	{
	case 1822:
	case 1823:
	case 1824:
		m_ObjectRadius = 800.0f;
		break;

	case 1825:
	case 1826:
	case 1827:
		m_ObjectRadius = 630.0f;
		break;

	case 1828:
	case 1829:
	case 1830:
		m_ObjectRadius = 510.0f;
		break;

	case 1831:
	case 1832:
	case 1833:
		m_ObjectRadius = 750.0f;
		break;

	case 1834:
		m_ObjectRadius = 0;
		break;

	default:
		m_ObjectRadius = g_ServerMgr->BAssetRadii()->GetRadius(basset);
		break;
	};
}

//this should go into the object base class really, since it's used for resource class too
void Husk::AddItem(ItemBase *item, long stack)
{
	int i;
	int j;

	if (item == (0) || stack == 0)
	{
		LogMessage("NULL item passed to AddItem\n");
		return;
	}

	//locate first empty slot
	for (i=0; i < MAX_ITEMS_PER_RESOURCE; i++)
	{
		if (m_Resource_contents[i].stack == 0)
		{
			break;
		}
	}

	//see if Husk already has this
	for (j=0; j < MAX_ITEMS_PER_RESOURCE; j++)
	{
		if (m_Resource_contents[j].item == item)
		{
			if ((stack + m_Resource_contents[j].stack) <= item->MaxStack())
			{
				m_Resource_contents[j].stack += (u16)stack;
				m_Resource_start_value += (u16)stack;
			}
			else
			{
				m_Resource_start_value += (item->MaxStack() - m_Resource_contents[j].stack);
				m_Resource_contents[j].stack = item->MaxStack();
			}
			m_Resource_value = m_Resource_start_value;
			return;
		}
	}

	if (i < MAX_ITEMS_PER_RESOURCE)
	{
		m_Resource_contents[i].item = item;
		m_Resource_contents[i].stack = (u16)stack;
		m_Content_count++;
		m_Resource_start_value += (u16)stack;
	}

	m_Resource_value = m_Resource_start_value;
}

ItemBase * Husk::GetItem(long slot)
{
	if (slot >= 0 && slot < MAX_ITEMS_PER_RESOURCE && m_Resource_contents[slot].item)
	{
		return (m_Resource_contents[slot].item);
	}
	else
	{
		return (0);
	}
}

short Husk::GetStack(long slot)
{
	if (slot >= 0 && slot < MAX_ITEMS_PER_RESOURCE)
	{
		return (m_Resource_contents[slot].stack);
	}
	else
	{
		return (0);
	}
}

float Husk::RemoveItem(long slot_id, long stack)
{
	float resource_remains = 0.0f;
	float resource_remaining;

	if (slot_id < MAX_ITEMS_PER_RESOURCE)
	{
		if (m_Resource_contents[slot_id].stack > (u16)stack)
		{
			m_Resource_contents[slot_id].stack -= (u16)stack;
		}
		else
		{
			m_Resource_contents[slot_id].stack = 0;
			m_Resource_contents[slot_id].item = (0);
		}

		m_Resource_value -= (u16)stack;

		resource_remaining = ( (float)(m_Resource_value) / (float)(m_Resource_start_value) );

		resource_remains = 0.0f;

		//roundings needed by client
		if (resource_remaining > 0.0f)
		{
			resource_remains = 0.125f;
		}
		if (resource_remaining > 0.125f)
		{
			resource_remains = 0.25f;
		}
		if (resource_remaining > 0.25f)
		{
			resource_remains = 0.4f;
		}
		if (resource_remaining > 0.4f)
		{
			resource_remains = 0.5f;
		}
		if (resource_remaining > 0.5f)
		{
			resource_remains = 0.6f;
		}
		if (resource_remaining > 0.6f)
		{
			resource_remains = 0.88889f;
		}
		if (resource_remaining > 0.95f)
		{
			resource_remains = 1.0f;
		}
	}
	else
	{
		LogMessage("ERROR: slot out of range for object '%s' id [%d]\n", Name(), GameID());
	}

	m_Resource_remains = resource_remains;

	SendObjectDrain(slot_id);

	return (resource_remains);
}


// The new loot system works like this:
// - Two loot bags named 'loot' and 'trash'
// - Roll to get an item from 'loot', if fail, replace with an item from 'trash'
// - Still to be implemented: 
// - second 'trash' bag, for now this is a random mineral drop.
// - more then one bag which can be used for loot, this could be used to give better 
//   loot bags depending on the fight difficulty (player level vs. mob level).
void Husk::PopulateHusk(long mob_type)
{
	MOBData *mob_data = g_ServerMgr->MOBList()->GetMOBData(mob_type);
	long loot_item_id;
	short stack;
	float drop_chance, drop_roll;
	long highest_count = mob_data->m_Loot.size();

	float total_chance = 0;

	for (int i = 0; i < highest_count; i++)
	{
		// Get item and its listed drop change
		loot_item_id = mob_data->m_Loot[i]->item_base_id;
		drop_chance = mob_data->m_Loot[i]->drop_chance;
		total_chance += drop_chance;
	}

	// If loot bag item's total % != 100%, notify devs. 
	// A scaled random roll will be used instead of a 100% roll.

	/*
	if (total_chance != 100.0f)
		g_PlayerMgr->ErrorBroadcast(
		"Loot %% on %s [Id: %d] not 100 [%-3.1f]\n", 
		mob_data->m_Name, mob_type, total_chance);

	LogMessage(
		"Loot %% on %s [Id: %d] not 100 [%-3.1f]\n", 
		mob_data->m_Name, mob_type, total_chance);
	*/

	//pick a number of slots
	long random = rand()%100 + 1;
	long slots = 1;

	if (random > 50) slots++;
	if (random > 75) slots++;
	if (random > 85) slots++;
	if (random > 90) slots++;
	if (random > 96) slots++;

	if (highest_count < 1)
	{
		LogMessage("%s has no loot.\n", Name());
		g_PlayerMgr->ErrorBroadcast("%s has no loot.\n", Name());

		// Fill each slot with an item, either loot or trash
		for (int x = 0; x < slots; x++)
		{
			DropTrashLoot(mob_data->m_Level, mob_data->m_Type);
		}
	} 
	else
	{
		ItemBase * drop;
		bool rare_dropped = false;
		bool equippable_dropped = false;
		float scale_factor = 1.0f;

		// If loot table is full, cap max item slots to number of items.
		// This prevents odd behavior from a single 100% item dropping 4
		// times of a mob for example.
		if (slots > highest_count && total_chance >= 100.0f) 
			slots = highest_count;

		if (total_chance > 100.0f)
		{
			scale_factor = 100.0f/total_chance; // scale drop percents by this amount

			g_PlayerMgr->ErrorBroadcast(
				"Loot %% on %s [Id: %d] above 100%% [%-3.1f]\n", 
				mob_data->m_Name, mob_type, total_chance);

			LogMessage(
				"Loot %% on %s [Id: %d] above 100%% [%-3.1f]\n", 
				mob_data->m_Name, mob_type, total_chance);
		}

		// Fill each slot with an item, either loot or trash
		for (int x = 0; x < slots; x++)
		{
			stack = 1;
			drop_roll = 0.0f;

			// roll a number based on the total if above 100
			drop_roll = (rand()%((int)(1000.0f))/10.0f) + 0.1f; // d[total_chance] roll, 1 decimal place precision

			if (drop_roll > total_chance)
			{
				DropTrashLoot(mob_data->m_Level, mob_data->m_Type);
				continue;
			}

			// Now that we are here drop is not automatically trash, lets see what it is
			float current_total = 0.0f;
			for (int i = 0; i < highest_count; i++)
			{
				drop_chance = (mob_data->m_Loot[i]->drop_chance) * scale_factor;
				current_total += drop_chance;

				// Check if this is the drop item we rolled for
				if (drop_roll <= (current_total) && drop_roll > (current_total - drop_chance))
				{
					loot_item_id = mob_data->m_Loot[i]->item_base_id;
					drop = g_ItemBaseMgr->GetItem(loot_item_id);
					if (loot_item_id == -1 || !drop) // Something's gone south, use trash.
					{
						DropTrashLoot(mob_data->m_Level, mob_data->m_Type);
						break;
					}
					else
					{
						// SANITY CHECK: Quality & Structure can't be 0
						if (mob_data->m_Loot[i]->quantity == 0) mob_data->m_Loot[i]->quantity = 1;

						// Now that we have a potential drop item, lets do some level sanity checks
						float level_difference = ceil(drop->TechLevel()*5.5f) - (mob_data->m_Level); 
						float noobie_bonus = ceil(5.5f - (mob_data->m_Level/2.0f)); // give noobs a break!
						if (noobie_bonus < 0.0f) noobie_bonus = 0.0f;
						level_difference = level_difference - noobie_bonus;

						// Max drop chance scales based on level difference using the following formula:
						// new_drop_chance = 2/(level_difference^2 + level_difference). This produces a series
						// where the max chance of dropping is 1 in 1, 1 in 2, 1 in 5, 1 in 9, etc.
						float original_drop_chance = drop_chance;
						float new_drop_chance = drop_chance;
						if (level_difference > 0.0f)
							new_drop_chance = (2.0f / (pow(level_difference + 1.0f, 2.0f) + level_difference)) * 100.0f;

						// 11 level difference = 1 in 78, 12 = 1 in 91, 13 = 1 in 105, etc. Enjoy!
						if (new_drop_chance < 1.0f) new_drop_chance = 0.0f;
						if (new_drop_chance > 100.0f) new_drop_chance = 100.0f;

						if (loot_item_id == 5468 || loot_item_id == 5469) // Lets gene maps drop for quests
							new_drop_chance = original_drop_chance;

						if (new_drop_chance < original_drop_chance)
						{

							g_PlayerMgr->ErrorBroadcast( 
								"TL%d %s max drop %-3.1f%%, L%d %s [%d] in %d\n", 
								drop->TechLevel(), drop->Name(), new_drop_chance, mob_data->m_Level, 
								mob_data->m_Name, mob_type, GetSectorManager()->GetSectorID());
							LogMessage( 
								"TL%d %s max drop %-3.1f%%, L%d %s [%d] in %d\n", 
								drop->TechLevel(), drop->Name(), new_drop_chance, mob_data->m_Level, 
								mob_data->m_Name, mob_type, GetSectorManager()->GetSectorID());
						}

						// Also check that the item isn't priced too high. This can happen if a special quest
						// item drops, or an item has a mistaken price set on it.
						int price_cap = GetMaxPricePerTechLevel(drop->Cost());
						if (drop->Cost() > price_cap)
						{
							g_PlayerMgr->ErrorBroadcast( 
								"TL%d %s price [$%d] too high on Lvl %d %s [%d] in %d\n", 
								drop->TechLevel(), drop->Name(), drop->Cost(), mob_data->m_Level, 
								mob_data->m_Name, mob_type, GetSectorManager()->GetSectorID());
							LogMessage( 
								"TL%d %s price [$%d] too high on Lvl %d %s [%d] in %d\n", 
								drop->TechLevel(), drop->Name(), drop->Cost(), mob_data->m_Level, 
								mob_data->m_Name, mob_type, GetSectorManager()->GetSectorID());

							new_drop_chance = 0.0f; // this will cause it to drop trash
						}

						// Now that we have the adjusted loot drop chance, see if the item actually did drop
						if (drop_roll < (current_total - original_drop_chance + new_drop_chance))
						{
							bool drop_ok = false;

							if (drop->ItemType() < 13) // equippable
							{
								// Always drop ammo
								if (drop->SubCategory() == 103)
								{
									//g_PlayerMgr->SendGlobalVaMessage(-1, 80, false, 5,"Ammo dropped\n");
									drop_ok = true;
								}
								// Don't drop equipment twice
								else if (!equippable_dropped)
								{
									if (drop_chance < 5.0f && rare_dropped)
									{
										//g_PlayerMgr->SendGlobalVaMessage(-1, 80, false, 5,"Rare Equippable skipped\n");
										drop_ok = false;
									}
									else
									{
										//g_PlayerMgr->SendGlobalVaMessage(-1, 80, false, 5,"Equippable dropped\n");
										equippable_dropped = true;
										drop_ok = true;
									}
								}
							} 
							else if (drop_chance <= 5.0f) //not an equippable, maybe rare?
							{
								if (rare_dropped)
									drop_ok = false;
								else
								{
									//g_PlayerMgr->SendGlobalVaMessage(-1, 80, false, 5,"Rare dropped\n");
									rare_dropped = true;
									drop_ok = true;
								}
							} 
							else 
							{
								//g_PlayerMgr->SendGlobalVaMessage(-1, 80, false, 5,"Normal dropped\n");
								drop_ok = true;
							}

							if (drop_ok)
							{
								// must be something else
								if (drop->MaxStack() > 1) stack = rand()%(mob_data->m_Loot[i]->quantity) + 1;
								AddItem(drop, stack);
								break;
							}
							else
							{
								//g_PlayerMgr->SendGlobalVaMessage(-1, 80, false, 5,"Trash dropped\n");
								DropTrashLoot(mob_data->m_Level, mob_data->m_Type);
								break;
							}

						}
						else // Drop replacement trash item
						{
							//g_PlayerMgr->SendGlobalVaMessage(-1, 80, false, 5,
							//	"Loot (%-3.1f%%) failed roll (%-3.1f%%), dropping trash.\n",
							//	new_drop_chance, drop_roll);
							DropTrashLoot(mob_data->m_Level, mob_data->m_Type);
							break;
						}
					}
				}
			}   
		}
	}

	if (m_Resource_start_value > 0) 
	{
		m_Resource_remains = 1.0f;
	}
	else
	{
		m_Resource_remains = 0.0f;
	}

	SetRespawnTick(0);

	m_ToBeRemoved = false;
}

// This will have to be moved to the DB eventually but for now it should do.
void Husk::DropTrashLoot(int mob_level, int mob_type)
{
	int trash_id = 2837; // calcite ore

	// Give random change of higher/lower item being dropped
	int level_adjust = rand()%100;
	if (level_adjust < 10) mob_level = mob_level - 2;
	else if (level_adjust < 20) mob_level = mob_level -1;
	else if (level_adjust >= 80) mob_level = mob_level + 1;
	else if (level_adjust >= 90) mob_level = mob_level + 2;
	if (mob_level < 1) mob_level = 1;

	int item_rank = (int)ceil(mob_level/5.5f);

	// 1 in 5 chance of a component dropping
	int rand_roll = rand()%5;

	// 1 in 3 chance of a component off inorganics
	if (mob_type == CRYSTALLINE || mob_type == ROCK_BASED)
	{
	//	if (rand_roll < 2)
	//		trash_id = GetRandomOre(item_rank);
	//	else if (rand_roll >= 2)
		if (rand_roll > 3)
			trash_id = GetRandomCrystal(item_rank);
	}
	else if (m_Resource_type == INORGANIC_HULK)
	{
	//	if (rand_roll == 0)
	//		trash_id = GetRandomOre(item_rank);
	//	else if (rand_roll == 1)
		if(rand_roll < 2)
			trash_id = GetRandomTech(item_rank, mob_type);
		else
			trash_id = GetRandomComponent(item_rank);
	}
	else
	{
	//	if (rand_roll == 0)
	//		trash_id = GetRandomOre(item_rank);
	//	else if (rand_roll == 1)
		if(rand_roll < 2)
			trash_id = GetRandomComponent(item_rank);
		else
			trash_id = GetRandomBio(item_rank);
	}

	ItemBase * drop = g_ItemBaseMgr->GetItem(trash_id);
	int stack = 1;
	if (drop)
	{
		//if (drop->MaxStack() > 1) stack = rand()%2 + 1;
		//g_PlayerMgr->ErrorBroadcast("Trash dropped: %s\n", drop->Name());
		//LogMessage("Trash dropped: %s\n", drop->Name());
		AddItem(drop, stack);
	} 
}

int Husk::GetRandomTech(int level, int mob_type)
{
	int tech_id = 5998; // circuit board

	if (level < 1)
		level = 1;

	switch(level)
	{
	case 1:
		if (mob_type == MANNED)
			tech_id = rand()%3 + 5998;
		else
			tech_id = rand()%5 + 5998;
		break;
	case 2:
		if (mob_type == MANNED)
			tech_id = rand()%3 + 6005;
		else
			tech_id = rand()%5 + 6005;
		break;
	case 3:
		if (mob_type == MANNED)
			tech_id = rand()%3 + 6012;
		else
			tech_id = rand()%5 + 6012;
		break;
	case 4:
		if (mob_type == MANNED)
			tech_id = rand()%3 + 6019;
		else
			tech_id = rand()%5 + 6019;
		break;
	case 5:
		if (mob_type == MANNED)
			tech_id = rand()%3 + 6026;
		else
			tech_id = rand()%5 + 6026;
		break;
	case 6:
		if (mob_type == MANNED)
			tech_id = rand()%3 + 6033;
		else
			tech_id = rand()%5 + 6033;
		break;
	case 7:
		if (mob_type == MANNED)
			tech_id = rand()%3 + 6040;
		else
			tech_id = rand()%5 + 6040;
		break;
	case 8:
		if (mob_type == MANNED)
			tech_id = rand()%3 + 6047;
		else
			tech_id = rand()%5 + 6047;
		break;
	case 9:
		if (mob_type == MANNED)
			tech_id = rand()%3 + 6054;
		else
			tech_id = rand()%5 + 6054;
		break;
	default:
		if (mob_type == MANNED)
			tech_id = rand()%3 + 6054;
		else
			tech_id = rand()%5 + 6054;
		break;
	}

	return tech_id;
}

int Husk::GetRandomComponent(int level)
{
	int component_id = 1222;

	if (level < 1)
		level = 1;

	switch(level)
	{
	case 1:
		component_id = trash1[rand()%length(trash1)];
		break;
	case 2:
		component_id = trash2[rand()%length(trash2)];
		break;
	case 3:
		component_id = trash3[rand()%length(trash3)];
		break;
	case 4:
		component_id = trash4[rand()%length(trash4)];
		break;
	case 5:
		component_id = trash5[rand()%length(trash5)];
		break;
	case 6:
		component_id = trash6[rand()%length(trash6)];
		break;
	case 7:
		component_id = trash7[rand()%length(trash7)];
		break;
	case 8:
		component_id = trash8[rand()%length(trash8)];
		break;
	case 9:
		component_id = trash9[rand()%length(trash9)];
		break;
	default:
		component_id = trash9[rand()%length(trash9)];
		break;
	}

	return component_id;
}


int Husk::GetRandomOre(int level)
{
	int ore_id = 2837;

	if (level < 1)
		level = 1;

	switch(level)
	{
	case 1:
		ore_id = rand()%29 + 2837; // random level 1 ore
		break;
	case 2:
		ore_id = rand()%22 + 2867; // random level 2 ore
		break;
	case 3:
		ore_id = rand()%18 + 2890; // random level 3 ore
		break;
	case 4:
		ore_id = rand()%18 + 2909; // random level 4 ore
		break;
	case 5:
		ore_id = rand()%19 + 2928; // random level 5 ore
		break;
	case 6:
		ore_id = rand()%19 + 2948; // random level 6 ore
		break;
	case 7:
		ore_id = rand()%18 + 2968; // random level 7 ore
		break;
	case 8:
		ore_id = rand()%15 + 2987; // random level 8 ore
		break;
	case 9:
		ore_id = rand()%18 + 3003; // random level 9 ore
		break;
	default:
		ore_id = rand()%18 + 3003; // random level 9 ore
		break;
	}
	return ore_id;
}
int Husk::GetRandomCrystal(int level)
{
	int crystal_id = 6065; //pulverized crystal

	if (level < 1)
		level = 1;

	switch(level)
	{
	case 1:
		crystal_id = rand()%2 + 6065; // random level 1 ore
		break;
	case 2:
		crystal_id = rand()%2 + 6067; // random level 2 ore
		break;
	case 3:
		crystal_id = rand()%2 + 6069; // random level 3 ore
		break;
	case 4:
		crystal_id = rand()%2 + 6071; // random level 4 ore
		break;
	case 5:
		crystal_id = rand()%2 + 6073; // random level 5 ore
		break;
	case 6:
		crystal_id = rand()%2 + 6075; // random level 6 ore
		break;
	case 7:
		crystal_id = rand()%2 + 6077; // random level 7 ore
		break;
	case 8:
		crystal_id = rand()%2 + 6079; // random level 8 ore
		break;
	case 9:
		crystal_id = rand()%2 + 6081; // random level 9 ore
		break;
	default:
		crystal_id = rand()%2 + 6081; // random level 9 ore
		break;
	}

	return crystal_id;
}

int Husk::GetRandomBio(int level)
{
	int bio_id = 5923;

	if (level < 1)
		level = 1;

	switch(level)
	{
	case 1:
		bio_id = rand()%3 + 5923; // random level 1 bio
		break;
	case 2:
		bio_id = rand()%3 + 5929; // random level 2 bio
		break;
	case 3:
		bio_id = rand()%3 + 5935; // random level 3 bio
		break;
	case 4:
		bio_id = rand()%3 + 5941; // random level 4 bio
		break;
	case 5:
		bio_id = rand()%4 + 5947; // random level 5 bio
		break;
	case 6:
		bio_id = rand()%4 + 5953; // random level 6 bio
		break;
	case 7:
		bio_id = rand()%6 + 5959; // random level 7 ore
		if(bio_id == 5961 || bio_id == 5964) bio_id = 5963;
		break;
	case 8:
		bio_id = rand()%2 + 5965; // random level 8 bio
		break;
	case 9:
		bio_id = rand()%6 + 5972; // random level 9 bio
		if(bio_id == 5973 || bio_id == 5974) bio_id = 5975;
		break;
	default:
		bio_id = rand()%6 + 5972; // random level 9 bio
		if(bio_id == 5973 || bio_id == 5974) bio_id = 5975;
		break;
	}
	return bio_id;
}

int Husk::GetMaxPricePerTechLevel(int tech_level)
{
	int max_item_price = 2500; //level 1 price cap

	switch(tech_level)
	{
	case 1:
		max_item_price = 4000;
		break;
	case 2:
		max_item_price = 10000;
		break;
	case 3:
		max_item_price = 20000;
		break;
	case 4:
		max_item_price = 40000;
		break;
	case 5:
		max_item_price = 100000;
		break;
	case 6:
		max_item_price = 300000;
		break;
	case 7:
		max_item_price = 700000;
		break;
	case 8:
		max_item_price = 2000000;
		break;
	default:
		max_item_price = 5000000;
		break;
	}
	return max_item_price;
}

void Husk::SendObjectReset()
{
	Player * p = 0;
	u32 * sector_list = GetSectorList();

	if (sector_list)
	{
		while (g_PlayerMgr->GetNextPlayerOnList(p, sector_list))
		{
			UnSetIndex(p->ResourceSendList());
		}
	}
}

void Husk::SendObjectDrain(long slot)
{
	PlayerList::iterator itrPList;
	Player * p = 0;
	u32 * sector_list = GetSectorList();

	if (slot != -1 && sector_list)
	{		
		while (g_PlayerMgr->GetNextPlayerOnList(p, sector_list))
		{			
			if (GetIndex(p->ObjectRangeList()))
			{
				p->SetHuskDrainLevel(this, slot);
			}
		}
	}

	if (m_Resource_remains == 0)
	{
		RemoveDestroyTimer();
		SetRespawnTick(-1);
		Remove();
	}
}

// Send object to all players who can see this object. 
// Currently used only for resource hijacks
void Husk::SendToVisibilityList(bool include_player)
{
	Player * p = 0;
	u32 * sector_list = GetSectorList();

	if (sector_list)
	{
		while (g_PlayerMgr->GetNextPlayerOnList(p, sector_list))
		{			
			if (GetIndex(p->ObjectRangeList()))
			{
				p->SendAdvancedPositionalUpdate(GameID(), PosInfo());
			}
		}
	}
}

void Husk::SetHuskName(char *name)
{
	char buffer[128];
	m_NameLen = sprintf_s(buffer, 128, "Corpse of %s", name);
	m_Name = g_StringMgr->GetStr(buffer);
}

void Husk::SendPosition(Player *player)
{
	player->SendConstantPositionalUpdate(
		GameID(),
		PosX(),
		PosY(),
		PosZ(),
		Orientation());
}

void Husk::SendAuxDataPacket(Player *player)
{
	player->SendHuskName(this);
}

//On creation of resource graphic object for Player
void Husk::OnCreate(Player *player)
{
	//if 'TargetOnCreate' is set for this object, we want a player to auto-target it 
	//when it comes into their view. (eg Husks).
	/*if (player->GameID() == GetPlayerLootLock()) 
	{
	player->SendHuskContent(this);
	player->SendPacketCache();
	player->ShipIndex()->SetTargetGameID(GameID());
	player->SendAuxShip();
	player->BlankVerbs();
	player->AddVerb(VERBID_LOOT, player->TractorRange());
	SetPlayerLootLock(0);
	}*/
}

//Called every time this resource is targeted.
void Husk::OnTargeted(Player *player)
{
	player->BlankVerbs();
	player->SendHuskContent(this);
	player->AddVerb(VERBID_LOOT, 10000.0f);
}

void Husk::SetDestroyTimer(long time_delay, long respawn_delay)
{
	if (time_delay == 0)
	{
		//destroy now
		Remove();
	}
	else
	{
		GetSectorManager()->AddTimedCallPNode(&m_ObjectTimeSlot, B_DESTROY_HUSK, time_delay, this, respawn_delay);
	}
}

void Husk::RemoveDestroyTimer()
{
	if (m_ObjectTimeSlot.event_time != 0) 
	{
		GetSectorManager()->RemoveTimedCall(&m_ObjectTimeSlot, true);
	}
}

void Husk::DestroyHusk()
{
	Player *p = 0;
	u32 * sector_list = GetSectorList();

	SetRespawnTick(-1);

	if (!Active()) return;

	//have a little removal effect
	ObjectEffect ObjExplosion;

	ObjExplosion.Bitmask = 0x07;
	ObjExplosion.GameID = GameID();
	ObjExplosion.EffectDescID = 393; //orange shockwave
	ObjExplosion.EffectID = GetObjectManager()->GetAvailableSectorID();
	ObjExplosion.TimeStamp = GetNet7TickCount();
	ObjExplosion.Duration = 4000;

	while (g_PlayerMgr->GetNextPlayerOnList(p, sector_list))
	{
		if (p)
		{
			if (GetIndex(p->ObjectRangeList()))
			{
				p->SendObjectEffect(&ObjExplosion);
			}
		}
	}

	Remove();
}

void Husk::SetLootTimer(long time_delay)
{
	if (time_delay == 0)
	{
		m_LootTime = 0;
	}
	else
	{
		m_LootTime = GetNet7TickCount() + time_delay;
	}
}