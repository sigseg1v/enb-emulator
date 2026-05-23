// ResourceClass.cpp
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
#include "ObjectClass.h"
#include "FieldClass.h"
#include "ObjectManager.h"
#include "MOBClass.h"
#include "PacketMethods.h"
#include "SaveManager.h"
#include <float.h>

Field::Field(long object_id) : Object (object_id)
{
    m_Type = OT_FIELD;
    m_FieldRadius = 0.0f;
    m_FieldCount = 0;
    m_FieldType = 0;
    m_FirstFieldID = 0;
	memset(m_PlayerClearList, 0, sizeof(m_PlayerClearList));
	m_FieldRespawn = 0;
	m_Respawn_timer = 30;
	m_RapidClearCount = 0;
	m_LevelBoost = 0;
	m_LastFieldClear = 0;
}

Field::~Field()
{
	for (ItemIDList::iterator itrItem = m_AdditionalOreItemIDs.begin(); itrItem < m_AdditionalOreItemIDs.end(); ++itrItem)
		delete *itrItem;
}

void Field::SetHSV(float h1, float h2, float h3)
{
    m_Level = (short)h3;
    m_FieldType = (short)h2;
    m_FieldCount = (short)h1;
}

void Field::PopulateField(bool live, bool repopulate)
{
    m_Signature = m_FieldRadius;
	ObjectManager *om = GetObjectManager();
    if (m_FieldRadius == 0 || !om) return; //don't populate yet
    if (m_Level == 0 || m_FieldCount == 0)
    {
        LogMessage("Unable to create asteroid field '%s'. level:%d,Type:%d,count:%d\n",Name(), m_Level, m_FieldType, m_FieldCount);
        m_FieldCount = 0;
        return;
    }

	if (m_Level > 8) m_Level = 8;

    if (m_FieldType == 0) //random
    {
        m_FieldType = GetTickCount()%5 + 1;
    }

    //create and populate asteroid field
	float pos[3];
	float inc;
	unsigned int count;
	float angle = 0.0f;
	inc = (2*3.14159f)/m_FieldCount;
	pos[2] = m_Position_info.Position[2];

	u16 types_size;
	//char types[16];
	u16 type_index;
	int this_level;
	int level_seed;
	u16 field_spread;
	float factor1, factor2, factor3 = 0.0f;
    Object *obj;

	long current_id = GameID() + 1;

    PopulateTypes(field_spread);

	if (field_spread == 4 || field_spread == 5) inc = inc * 3;

    types_size = m_ResourceIDs.size();

	for (count = 0; count < m_FieldCount; count++)
	{
		factor2 = (float)(rand()%2000 - 1000);
		switch (field_spread)
		{
		case 1: //ring shape
			pos[0] = m_Position_info.Position[0] + m_FieldRadius * cosf(inc * count);
			pos[1] = m_Position_info.Position[1] + m_FieldRadius * sinf(inc * count);
			pos[2] = m_Position_info.Position[2] + (rand()%1000 - 500);
			break;
		case 3: //cylinder shape
			factor2 = rand()%(int)(m_FieldRadius) - (m_FieldRadius/2);
			//drop through
		case 2: //donut shape
			factor1 = 1.0f - (float)(rand()%20)/100.0f;
			pos[0] = m_Position_info.Position[0] + (m_FieldRadius * factor1) * cosf(inc * count);
			pos[1] = m_Position_info.Position[1] + (m_FieldRadius * factor1) * sinf(inc * count);
			pos[2] = m_Position_info.Position[2] + factor2;
			break;
		case 4:	//regular sphere
			factor1 = (rand()%360)/(180.0f/PI); //random Z plane
			factor2 = (rand()%360)/(180.0f/PI); //random Y plane
			factor3 = (rand()%1000) * (m_FieldRadius/1000.0f);
			pos[0] = m_Position_info.Position[0] + (factor3*cosf(factor2)) * cosf(factor1);
			pos[1] = m_Position_info.Position[1] + (factor3*cosf(factor2)) * sinf(factor1);
			pos[2] = m_Position_info.Position[2] + (factor3*sinf(factor2));
			break;
        case 5:	//centre weighted sphere
			factor1 = (rand()%360)/(180.0f/PI); //random Z plane
			factor2 = (rand()%360)/(180.0f/PI); //random Y plane
			factor3 = (rand()%1000) * (m_FieldRadius/1000.0f);

			//produce centre weight (simple, dirty, but it looks right!)
			if (rand()%10 > 5) factor3 = factor3 * 0.7f;
			if (rand()%10 > 5) factor3 = factor3 * 0.7f;
			pos[0] = m_Position_info.Position[0] + (factor3*cosf(factor2)) * cosf(factor1);
			pos[1] = m_Position_info.Position[1] + (factor3*cosf(factor2)) * sinf(factor1);
			pos[2] = m_Position_info.Position[2] + (factor3*sinf(factor2));
			break;

		default:
			factor1 = 1.0f - (float)(rand()%20)/100.0f;
			pos[0] = m_Position_info.Position[0] + (m_FieldRadius * factor1) * cosf(inc * count);
			pos[1] = m_Position_info.Position[1] + (m_FieldRadius * factor1) * sinf(inc * count);
			pos[2] = m_Position_info.Position[2] + factor2;
			break;
		}

		//now move the roid WRT field orientation.
		if (field_spread == 1 || field_spread == 2 || field_spread == 3)
		{
			TransformCoords(pos, this->Position(), this->Orientation());
		}

		type_index = rand()%types_size;
		level_seed = rand()%20;
		this_level = m_Level + m_LevelBoost; //level boost will come into effect if the field has been left for a long period of time, but won't affect low level fields (ore missions)
		if (level_seed > 17)
		{
			this_level++;
		}
		else if (level_seed < 4)
		{
			this_level--;
		}

		if (repopulate)
		{
			obj = om->GetObjectFromID(current_id);
			current_id++;
		}
		else
		{
			obj = om->AddNewObject(OT_RESOURCE, true);
		}
        obj->SetTypeAndName((short)m_ResourceIDs[type_index]);
        obj->SetPosition(pos);
        obj->SetLevel(this_level);
        obj->RandomiseOrientation();
        obj->SetContainerField(this);

		m_LastFieldAsteroid = obj; //this will end up pointing to the last asteroid

        if (m_FirstFieldID == 0)
        {
            m_FirstFieldID = obj->GameID();
        }

        if (live || repopulate)
        {
			obj->SendObjectReset();
            obj->ResetResource();
        }
    }

    //Add guardians if required.
	if (!(live && repopulate))
	{
		AddFieldGuardian(repopulate);
	}

	m_FieldCountRemaining = m_FieldCount;
	m_FieldRespawn = 0;
	memset(m_PlayerClearList, 0, sizeof(m_PlayerClearList));

	m_LastAccessTime = GetNet7TickCount();
	m_LastFieldClear = GetNet7TickCount();
}

//TODO: remove this function completely when we switch over to new field descriptors
void Field::PopulateTypes(u16 &field_spread)
{   
    //New DASE2 driven field data
    if (m_ResourceIDs.size() > 0)
    {
        field_spread = m_FieldType;
    }
    else
    {
        //old hardcoded style
        switch (m_FieldType)
        {
        case 1:
            m_ResourceIDs.push_back(6 + 0x71E);
            m_ResourceIDs.push_back(7 + 0x71E);
            m_ResourceIDs.push_back(8 + 0x71E);
            m_ResourceIDs.push_back(10 + 0x71E);
            field_spread = 1;
            break;
            
        case 2:
            m_ResourceIDs.push_back(2 + 0x71E);
            m_ResourceIDs.push_back(4 + 0x71E);
            m_ResourceIDs.push_back(5 + 0x71E);
            m_ResourceIDs.push_back(11 + 0x71E);
            m_ResourceIDs.push_back(12 + 0x71E);
            field_spread = 3;
            break;
            
        case 3:
            m_ResourceIDs.push_back(3 + 0x71E);
            m_ResourceIDs.push_back(6 + 0x71E);
            m_ResourceIDs.push_back(7 + 0x71E);
            m_ResourceIDs.push_back(9 + 0x71E);
            m_ResourceIDs.push_back(12 + 0x71E);
            m_ResourceIDs.push_back(11 + 0x71E);
            field_spread = 2;
            break;
            
        case 4:
            m_ResourceIDs.push_back(1 + 0x71E);
            m_ResourceIDs.push_back(3 + 0x71E);
            m_ResourceIDs.push_back(4 + 0x71E);
            m_ResourceIDs.push_back(6 + 0x71E);
            m_ResourceIDs.push_back(12 + 0x71E);
            m_ResourceIDs.push_back(10 + 0x71E);
            field_spread = 4;
            break;
            
        case 5: //gas cloud clump
            m_ResourceIDs.push_back(12 + 0x71E);
            field_spread = 4;
            break;
            
        default:
            m_ResourceIDs.push_back(1 + 0x71E);
            m_ResourceIDs.push_back(0 + 0x71E);
            m_ResourceIDs.push_back(6 + 0x71E);
            m_ResourceIDs.push_back(9 + 0x71E);
            field_spread = 1;
            break;
        }
    }
}

void Field::AddMOBID(long mob_id)
{
    //LogMessage("Adding mob %d to field %s.\n", mob_id, Name());
    m_MOBIDs.push_back(mob_id);
}

void Field::AddResource(long resource)
{
    m_ResourceIDs.push_back(resource);
}

void Field::AddItemID(long item_id, float frequency)
{
	//see if item already in list, or if we can re-use a blank entry
	for (ItemIDList::iterator itrItem = m_AdditionalOreItemIDs.begin(); itrItem < m_AdditionalOreItemIDs.end(); ++itrItem)
	{
		if ( (*itrItem)->item_id == item_id || (*itrItem)->item_id == 0 )
		{
			(*itrItem)->item_id = item_id;
			(*itrItem)->frequency = frequency;
			return;
		}
	}

	OreNode *node = new OreNode;
	node->item_id = item_id;
	node->frequency = frequency;
	m_AdditionalOreItemIDs.push_back(node);
}

void Field::BlankItemIDs()
{
	for (ItemIDList::iterator itrItem = m_AdditionalOreItemIDs.begin(); itrItem < m_AdditionalOreItemIDs.end(); ++itrItem)
	{	
		(*itrItem)->frequency = 0;
		(*itrItem)->item_id = 0;
	}
}

ItemIDList* Field::GetAdditionalItemIDs()
{
	return (&m_AdditionalOreItemIDs);
}

void Field::FullResetResource()
{
	m_MOBIDs.clear();
	m_ResourceIDs.clear();
}

void Field::ResetResource()
{
	//first remove this field for everyone in range
	ObjectManager *om = GetObjectManager();
	om->RemoveField(this);
	PopulateField(true, true);
	u32 current_tick = GetNet7TickCount();

	if (m_FieldCountRemaining < 5 && m_FieldCount > 5 && g_ResetContent == false 
		&& m_LastFieldClear != 0 && (current_tick - m_LastFieldClear) < 60000*45)
	{
		//this field is getting respawned rapidly
		m_RapidClearCount++;
		if (m_RapidClearCount > 10)
		{
			m_Respawn_timer += 3;
			if (Level() > 6)
			{
				if (m_Respawn_timer > 45) m_Respawn_timer = 45;
			}
			else
			{
				if (m_Respawn_timer > 30) m_Respawn_timer = 30;
			}
			SetDatabaseFieldRespawnTime();
			char buffer[100];
			_snprintf(buffer, 100, "Field %s[%d] respawn increased to %d mins.", Name(), GetSector(), m_Respawn_timer);
			g_PlayerMgr->ChatSendChannel(GameID(), "Dev", buffer);
		}
	}
	else
	{
		m_RapidClearCount--;
		if (m_RapidClearCount < 0)
		{
			m_RapidClearCount = 0;
		}
	}

	m_LastFieldClear = current_tick;
}

void Field::AddFieldGuardian(bool repopulate)
{
    u32 mob_count = m_MOBIDs.size();
	ObjectManager *om = GetObjectManager();

    if (m_FieldRadius < 2500.0f)
    {
        //LogMessage("Field %d(%s) too small for MOBs\n", m_DatabaseUID, Name());
		m_FieldRadius = 2500.0f;
    }

	unsigned long index = 0;

    for (u32 x = 0; x < mob_count; x++)
    {
		MOB *mob = (0);
		if (repopulate)
		{
			//use MOBs from this field
			//see if we can find an existing mob for this
			if (index < m_MOBGameIDs.size())
			{
				mob = (MOB*) om->GetObjectFromID(m_MOBGameIDs[index]);
				index++;
			}

			if (!mob)
			{
				//didn't find a suitable MOB
				mob = (MOB*)om->AddNewObject(OT_MOB); //MOB creation
				mob->AddBehaviourObject(this);
				mob->AddBehaviourPosition(Position());
				m_MOBGameIDs.push_back(mob->GameID());
			}
		}
		else
		{
			mob = (MOB*)om->AddNewObject(OT_MOB); //MOB creation
			mob->AddBehaviourObject(this);
			mob->AddBehaviourPosition(Position());
			m_MOBGameIDs.push_back(mob->GameID());
		}
        
        if (mob)
        {
            mob->SetPosition(Position());
            mob->MovePosition(-200.0f, 0.0f, -200.0f);
            mob->SetActive(true);
            mob->SetRespawnTick(0);
            mob->SetHostileTo(OT_PLAYER);
            mob->SetOrientation(0.0f, 0.0f, 0.0f, 1.0f);
            mob->SetUpdateRate(50);
            float velocity = (float)(rand()%100 + 130);
            float turn = (float)(rand()%3 + 2)*0.02f;
            if (rand()%10 > 7) turn = -(float)(rand()%3 + 2)*0.02f;
            if (turn == 0.0f) turn = 0.02f;
            mob->SetVelocity(velocity);
            mob->SetBehaviour(PATROL_FIELD);
			mob->AddBehaviourPosition(Position());
            mob->SetDefaultStats(turn, PATROL_FIELD, velocity, 50);
			mob->SetMOBType((short)m_MOBIDs[x]);
			mob->SetSpawnGroup(this);
        }
    }
}

Object * Field::SetDestination(Object *current)
{
    //choose a destination somewhere a reasonable distance from the current asteroid.
    Object *obj;
	ObjectManager *om = GetObjectManager();
    long id;
    short spin_count = 0;
    if (!current && om)
    {
        current = om->GetObjectFromID(m_FirstFieldID);
    }

    id = current->GameID();

    Object *target_obj = (0);

    while (!target_obj && spin_count < 3)
    {
		id += ((rand()%3) + 1);
        if (id >= (m_FirstFieldID + m_FieldCount - 1)) 
        {
            id = m_FirstFieldID;
            spin_count++;
        }

        obj = om->GetObjectFromID(id);

        if (obj->RangeFrom(current) > 2000.0f) //this assumes a minimum field size ...
        {
            target_obj = obj;
        }
    }

    if (!target_obj)
    {
        id = m_FirstFieldID + rand()%5;
        target_obj = om->GetObjectFromID(id);
    }

	if (!target_obj)
	{
		target_obj = om->GetObjectFromID(m_FirstFieldID);
	}

	if (!target_obj)
	{
		LogMessage("Critical error. Unable to find any asteroids in field ID %d '%s'\n", GetDatabaseUID(), Name());
	}

    return (target_obj);
}

void Field::SendAuxDataPacket(Player *player)
{
	player->SendAuxNameSignature(this);
}

void Field::SendPosition(Player *player)
{
	player->SendConstantPositionalUpdate(
		GameID(),
		PosX(),
		PosY(),
		PosZ(),
		Orientation());
}

void Field::SendObjectEffects(Player *player)
{
	ObjectEffect ObjEffect;
	ObjEffect.Bitmask = 3;
	ObjEffect.GameID = GameID();
	ObjEffect.EffectDescID = 407; 
	ObjEffect.EffectID = GetSectorManager()->GetSectorNextObjID();
	ObjEffect.TimeStamp = GetNet7TickCount();

	player->SendEffect(&ObjEffect);
}

void Field::SendToVisibilityList(bool include_player)
{
	Player * p = (0);
    u32 * sector_list = GetSectorList();
	if (sector_list)
	{
		while (g_PlayerMgr->GetNextPlayerOnList(p, sector_list))
		{
			if (p) 
			{
				p->SendAdvancedPositionalUpdate(GameID(), PosInfo());
			}
		}
	}
}

void Field::AddToClearList(Player *player)
{
	//Add player to clear list
	long oldest_entry = 0;
	u32 oldest_clear_time = m_PlayerClearList[0].last_clear;
	bool inserted = false;
	u32 current_tick = GetNet7TickCount();

	//find spot on clear list
	if (player)
	{
		for (long i = 0; i < CLEAR_SIZE; i++)
		{
			if (m_PlayerClearList[i].last_clear < oldest_clear_time) //track oldest clear entry
			{
				oldest_entry = i;
				oldest_clear_time = m_PlayerClearList[i].last_clear;
			}

			if (m_PlayerClearList[i].game_id == player->GameID() || m_PlayerClearList[i].clear_count == 0)
			{
				m_PlayerClearList[i].clear_count++;
				m_PlayerClearList[i].game_id = player->GameID();
				m_PlayerClearList[i].last_clear = current_tick;
				inserted = true;
				break;
			}
		}

		if (!inserted) //no empty slots, but player might be able to replace an older entry
		{
			if ((oldest_clear_time + 10*1000*60) < current_tick) //allow players to be replaced after 10 minutes of no mining
																 //this would only happen when a single field is being very heavily mined
																 //and player becomes inactive for more than 10 mins, and other people arrive in that time.
			{
				m_PlayerClearList[oldest_entry].game_id = player->GameID();
				m_PlayerClearList[oldest_entry].clear_count = 1;
				m_PlayerClearList[oldest_entry].last_clear = current_tick;
			}
		}
	}

	//flag field has been disturbed
	m_FieldRespawn = (current_tick + 60000*m_Respawn_timer); //respawn field in 30 minutes if undisturbed

	//has the field been cleared?
	m_FieldCountRemaining--;

	if (m_FieldCountRemaining == 0)
	{
		//field emptied, award XP to all players in range
		AwardFieldClearXP();
	}
}

void Field::AwardFieldClearXP()
{
	long xp_for_clear = Level()*FieldCount()*20; //xp for clearing a field, this is subject to XP balancing
	Player *p;

	for (long i = 0; i < CLEAR_SIZE; i++)
	{
		if (m_PlayerClearList[i].game_id != 0)
		{
			//is this player in range?
			p = g_PlayerMgr->GetPlayer(m_PlayerClearList[i].game_id);
			if (p && p->GetSector() == this->GetSector())
			{
				if (RangeFrom(p) < 40000.0f)
				{
					long xp_share = (long)( (float)m_PlayerClearList[i].clear_count / (float)FieldCount() * (float) (xp_for_clear) );
					p->AwardExploreXP("Field Cleared!", xp_share);
					p->SendClientSound("Exploration_Exp", 2, 0);
				}
			}
		}
	}

	//blank field clearance data
	memset(m_PlayerClearList, 0, sizeof(m_PlayerClearList));
}

void Field::CheckFieldRespawn(u32 tick)
{
	char buffer[100];
	//when was the last time this field was interacted with?
	u32 current_tick = GetNet7TickCount();
	if (current_tick > (LastAccessTime() + 1000*60*200))
	{
		if (rand()%20 == 0 && m_LevelBoost == 0 && Level() > 2 && Level() < 8)
		{
			//1 in 20 chance for increase in asteroid level (temporary)
			if (Level() < 8)
			{
				m_LevelBoost++;
				_snprintf(buffer, 100, "Field %s[%d] level increased to %d.", Name(), GetSector(), (Level() + m_LevelBoost));
				g_PlayerMgr->ChatSendChannel(GameID(), "Dev", buffer);
				m_FieldRespawn = (current_tick + 5000); //respawn field in 5 seconds
			}
		}
		else
		{
			//200 mins passed without any interaction with this field, increase respawn rate depending on level
			switch (Level())
			{
			case 1:
			case 2:
			case 3:
				m_Respawn_timer -= 5;
				if (m_Respawn_timer < 10) m_Respawn_timer = 10;
				break;
			case 4:
			case 5:
				m_Respawn_timer -= 3;
				if (m_Respawn_timer < 12) m_Respawn_timer = 12;
				break;
			case 6:
				m_Respawn_timer -= 2;
				if (m_Respawn_timer < 15) m_Respawn_timer = 15;
				break;
			case 7:
				m_Respawn_timer -= 1;
				if (m_Respawn_timer < 20) m_Respawn_timer = 20;
				break;
			case 8:
			case 9:
				m_Respawn_timer -= 1;
				if (m_Respawn_timer < 30) m_Respawn_timer = 30;
				break;
			}

			_snprintf(buffer, 100, "Field %s[%d] respawn decreased to %d mins.", Name(), GetSector(), m_Respawn_timer);
			g_PlayerMgr->ChatSendChannel(GameID(), "Dev", buffer);
			//save this value
			SetDatabaseFieldRespawnTime();
		}
		
		m_LastAccessTime = tick;
	}

	if (m_FieldRespawn != 0 && tick > m_FieldRespawn)
	{
		ResetResource();
	}
}

void Field::SetDatabaseFieldRespawnTime()
{
	unsigned char data[16];
	int index = 0;

	AddData(data, m_Respawn_timer, index);
	AddData(data, GetDatabaseUID(), index);
	g_SaveMgr->AddSaveMessage(SAVE_CODE_FIELD_RESPAWN_TIME, -1, index, data);
}

void Field::SetRespawnTimer(int respawn_time)
{
	if (respawn_time > 2 && respawn_time < 200)
	{
		m_Respawn_timer = respawn_time;
	}
}

void Field::SetLastAccessTime(unsigned long time)
{ 
	m_LastAccessTime = time;

	//flag field has been disturbed
	m_FieldRespawn = (GetNet7TickCount() + 60000*m_Respawn_timer); //respawn field in 30 minutes (or less) if undisturbed
}