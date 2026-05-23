// ObjectManager.cpp
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
#include "ObjectManager.h"
#include "stdlib.h"
#include "AssetDatabase.h"
#include "ServerManager.h"

#define MAX_OBJECTS_IN_SECTOR 16384
#define MAX_STATIC_OBJECTS_IN_SECTOR 400

GlobalObjectList g_SectorObjects;

ObjectManager::ObjectManager()
{
    m_StartObjectID     = 100000;
    m_StartSectorFXID   = 1000000;
    m_SectorFXID        = 0;
    m_NumberOfObjects   = 0;
    m_TempResources     = new MemorySlot<Resource>(DEFAULT_TEMP_OBJLIST_SIZE); //create slotted array of temporary objects
	m_TempHusks			= new MemorySlot<Husk>(DEFAULT_TEMP_OBJLIST_SIZE); //create slotted array of temporary objects

    m_SectorMOBCount    = 0;

	m_SectorMgr			= 0;

	m_ObjectsList		= 0;

	m_ObjectListUpdated = 0;

    m_Lockdown = false;
}

ObjectManager::~ObjectManager()
{
	DeleteAllObjects();
	delete m_TempResources;
	delete m_TempHusks;
}

ObjectList *ObjectManager::GetMOBList()
{
	m_Mutex.Lock(); // delay return until delete finished
	m_Mutex.Unlock();
	return &m_MOBSectorList; 
}

void ObjectManager::DeleteAllObjects()
{
	ObjectList::iterator itrOList;

	m_Mutex.Lock();

	for (itrOList = m_SectorIndexList.begin(); itrOList < m_SectorIndexList.end(); ++itrOList) 
	{
		if (!(*itrOList)->IsTemp())
		{
			delete *itrOList;
		}
	}
	/*for (itrOList = m_MOBSectorList.begin(); itrOList < m_MOBSectorList.end(); ++itrOList) 
		delete *itrOList;*/
	m_StaticSectorList.clear();
	m_SectorIndexList.clear();
    m_MOBSectorList.clear();
    m_SectorMOBCount  = 0;
    m_NumberOfObjects = 0;

	m_Mutex.Unlock();
}

Object * ObjectManager::AddNewMOB(bool static_obj) //if static obj is set true this will be a permanent MOB
{
    long object_index; 
    long object_id;
    MOB *object;

    //TODO: mutex this

    /*if (!static_obj)
    {
        object = m_TempMOBs->GetNode();
        HandleTempCreation(object); //destroy the object if necessary
        object_index = object->GetObjectIndex();
        object_id = object->GameID();
    }
    else*/
    {
        object_index = m_NumberOfObjects;
        object_id = GetNewGameID();
        object = new MOB(object_id);
        //Add to MOB list
        m_MOBSectorList.push_back(object);
        //m_DynamicSectorList.push_back(object);
    }

    object->SetObjectIndex(object_index);

	AddObjectToSectorList(object);

    m_SectorMOBCount++;

    return (object);
}

Object * ObjectManager::AddNewMOBSpawn() 
{
    long object_index; 
    long object_id;
    MOBSpawn *object;

    //TODO: mutex this

    object_index = m_NumberOfObjects;
    object_id = GetNewGameID();
    object = new MOBSpawn(object_id);
    m_MOBSectorList.push_back(object);
    //m_DynamicSectorList.push_back(object);
	//AddToDynamicList(object);

    object->SetObjectIndex(object_index);

	AddObjectToSectorList(object);

    return (object);
}

Object * ObjectManager::AddNewField(bool static_obj) //if static obj is set true this will be a permanent field.
{
    long object_index; 
    long object_id;
    Field *object;

    //TODO: mutex this

    /*if (!static_obj)
    {
        object = m_TempMOBs->GetNode();
        HandleTempCreation(object); //destroy the object if necessary
        object_index = object->GetObjectIndex();
        object_id = object->GameID();
    }
    else*/
    {
        object_index = m_NumberOfObjects;
        object_id = GetNewGameID();
        object = new Field(object_id);
        //m_DynamicSectorList.push_back(object);
		AddToDynamicList(object);
    }

    object->SetObjectIndex(object_index);

	object->SetBasset(2241); //use generic nav buoy
	object->SetScale(3.0f);
	object->SetSignature(40000.0f);

	AddObjectToSectorList(object);

    return (object);
}

void ObjectManager::AddObjectToSectorList(Object *obj)
{
	//perform range check before adding
	long object_index = obj->ObjectIndex();

	long list_size = m_SectorIndexList.size();

	if (object_index > (list_size))
	{
		object_index = list_size;
		obj->SetObjectIndex(object_index);
	}

	obj->SetActive(true);
    obj->SetSector(m_SectorID);
    m_SectorIndexList.push_back(obj);
    m_SectorIndexList[object_index] = obj;
}

void ObjectManager::AddToDynamicList(Object *obj)
{
	if (m_ObjectsList)
	{
		m_ObjectsList->SetNextObject(obj);
	}
	m_ObjectsList = obj;
}

Object * ObjectManager::AddNewResource(bool static_obj) //if static obj is set true this will be a permanent asteroid
{
    long object_index; 
    long object_id;
    Resource *object;

    //mutex this

    if (!static_obj)
    {
        object = this->m_TempResources->GetNode();
        HandleTempCreation(object); //destroy the object if necessary
        object_index = object->ObjectIndex();
        object_id = object->GameID();
		object->SetTemp();
    }
    else
    {
        object_index = m_NumberOfObjects;
        object_id = GetNewGameID();
        object = new Resource(object_id);
        //m_DynamicSectorList.push_back(object);
		AddToDynamicList(object);
    }

    object->SetObjectIndex(object_index);

	AddObjectToSectorList(object);

    return (object);
}

Object * ObjectManager::AddNewHusk(bool static_obj) //if static obj is set true this will be a permanent asteroid
{
    long object_index; 
    long object_id;
    Husk *object;

    if (!static_obj)
    {
        object = m_TempHusks->GetNode();
        HandleTempCreation(object); //destroy the object if necessary
        object_index = object->ObjectIndex();
        object_id = object->GameID();
		object->SetTemp();
    }
    else
    {
        object_index = m_NumberOfObjects;
        object_id = GetNewGameID();
        object = new Husk(object_id);
		//m_DynamicSectorList.push_back(object);
		AddToDynamicList(object);
    }

    object->SetObjectIndex(object_index);

	AddObjectToSectorList(object);

    return (object);
}

Object * ObjectManager::AddNewObject(object_type ot, bool static_obj)
{
    Object *object;
    switch (ot)
    {
        case OT_STATION :          
        case OT_STARGATE:
			object = AddNewNav(true);
			break;
        case OT_PLANET  :
            object = AddNewPlanet();
            break;
        case OT_CAPSHIP :
        case OT_DECO    :
        case OT_NAV     :   
		// Enviremental Effects
		case OT_GWELL:
		case OT_RADIATION:
            object = AddNewNav();
            break;

		case OT_RESOURCE:
            object = AddNewResource(static_obj);
            break;

        case OT_MOB :	
            object = AddNewMOB(static_obj);
            break;

        case OT_MOBSPAWN:
            object = AddNewMOBSpawn();
            break;

        case OT_FIELD:
			object = AddNewField(true);
			break;

        case OT_HULK:
            object = AddNewResource(static_obj);
            break;   

		case OT_HUSK:
			object = AddNewHusk(static_obj);
			break;

        case OT_FLOATING_ORE:
            object = AddNewResource(false);
            break;

        default:
            LogMessage("ObjectManager::AddNewObject - Attempted to create an invalid object [%d]\n", ot);
            return (Object*)(0);
            break;
    }

    if (object)
    {
        object->SetObjectType(ot);
        object->SetSector(m_SectorID);
    }

    return (object);
}

Object * ObjectManager::AddNewNav(bool appears_in_radar)
{
    StaticMap *object = new StaticMap(0);
    m_StaticSectorList.push_back(object); //add nav to static sector list
    AssignStaticID(object);
    object->SetSector(m_SectorID);
    object->SetActive(true);
    if (appears_in_radar) object->SetAppearsInRadar();
    return (object);
}

Object * ObjectManager::AddNewPlanet()
{
    Planet *object = new Planet(0);
    m_StaticSectorList.push_back(object); //add nav to static sector list
    AssignStaticID(object);
    object->SetSector(m_SectorID);
    object->SetActive(true);
    object->SetAppearsInRadar();
	object->SetHuge();
    return (object);
}

void ObjectManager::AssignStaticID(Object *object)
{
    long object_index = m_NumberOfObjects;
    long object_id = GetNewGameID();

    object->SetGameID(object_id);
    object->SetObjectIndex(object_index);

    m_SectorIndexList.push_back(object);
    m_SectorIndexList[object_index] = object; //assign spot in object index
}

long ObjectManager::GetNewGameID()
{
    long game_id = m_StartObjectID + m_NumberOfObjects;
    ++m_NumberOfObjects;
    return (game_id);
}

void ObjectManager::HandleTempCreation(Object *object)
{
	object->ResetObjectContents();

    if (object->ObjectIndex() == -1)
    {
        //object slot hasn't been used yet
        object->SetObjectIndex(m_NumberOfObjects);
        object->SetGameID(GetNewGameID());
        //m_DynamicSectorList.push_back(object); //add to sector list
		AddToDynamicList(object);
    }
    else if (object->Active())
    {
        //has been used, and object is currently active
		object->Remove();	
        object->SetRequiresReset(true);
    }
    else
    {
        //has been used, but has been subsequently destroyed correctly
        object->SetActive(true);
        //m_DynamicSectorList.push_back(object); //add to sector list
		AddToDynamicList(object);
    }
}

//These two methods are an attempt to cut down on sector load times
//First we send all normal navs, there's never too many of these
//and any Deco that's within 10000k
void ObjectManager::SendAllNavs(Player *player)
{
    Object *obj;
	u32 index;

	for (index = 0; index < m_StaticSectorList.size(); index++) 
	{
        obj = m_StaticSectorList[index];
		if (obj->ObjectType() != OT_DECO && obj->Active())
        {
            if (((obj->ObjectType() == OT_PLANET) || obj->GetEIndex(player->ExposedNavList())) && !obj->GetIndex(player->ObjectRangeList()) )
            {
                obj->SendObject(player);
                obj->SetEIndex(player->ExposedNavList());
                obj->SetIndex(player->ObjectRangeList());
            }
        }
	}
}

//Now we stream the DECOs in as and when they are needed (10k from sig should be fine).
//This should cut the loading times right down
void ObjectManager::SendRemainingStaticObjs(Player *player)
{
    ObjectList::iterator itrOList;
    Object *obj;

	for (itrOList = m_StaticSectorList.begin(); itrOList < m_StaticSectorList.end(); ++itrOList) 
	{
        obj = (*itrOList);
		if (obj->ObjectType() == OT_DECO && !obj->GetIndex(player->ObjectRangeList()) &&
           (obj->RangeFrom(player->Position()) < (obj->Signature() + 40000.0f) ) )
        {
			player->ExposeDecosOn(obj);

            obj->SendObject(player);
            obj->SetIndex(player->ObjectRangeList());

			player->ExposeDecosOff(obj);
        }
	}
}

bool ObjectManager::CheckNavRanges(Player *player)
{
    ObjectList::iterator itrOList;
    Object *obj;
    float range;
    float explore_range = 3000.0f;
    bool hidden;
    bool unexplored;
    bool unexplored_navs = false;
	bool object_effect;
    float scan_range = (float)player->ShipIndex()->CurrentStats.GetScanRange();

    for (itrOList = m_StaticSectorList.begin(); itrOList < m_StaticSectorList.end(); ++itrOList) 
    {
        obj = (*itrOList);
		range = 0;
		if (obj->Active() && obj->ObjectType() != OT_DECO && obj->ObjectType() != OT_PLANET && 
			obj->ObjectType() != OT_GWELL && obj->ObjectType() != OT_RADIATION)
        {
            hidden =     !obj->GetEIndex(player->ExposedNavList());
            unexplored = !obj->GetEIndex(player->ExploredNavList());
			
            //do we need to check this nav?
            if (hidden || unexplored)
            {
				range = obj->RangeFrom(player->Position());
                if (hidden)
                {
                    if ( (obj->AppearsInRadar() && range < (obj->RadarRange() + scan_range) ) || //if this is a minimap object, uncover as '?' at 'RadarRange + scanrange'
                         (range < (obj->Signature())) ) //if it's not minimap, uncover when it's in visual range
                    {               
                        //object is exposed
                        obj->SetEIndex(player->ExposedNavList());
						player->SaveDiscoverNav(obj->GetDatabaseUID());
                        //send the static nav
                        obj->SendObject(player);
						hidden = false;
                    }
                }
                
				// dont set the explore range to 0
                if (obj->Signature() && obj->Signature() < explore_range)
				{
					explore_range = obj->Signature();
				}
                
                if (range <= explore_range)
                {
                    obj->SetEIndex(player->ExploredNavList());

					if (hidden)
					{
						obj->SetEIndex(player->ExposedNavList()); //ensure we don't get infinite XP
						obj->SendObject(player);
					}
					
                    obj->SendNavigation(player);
                    player->AwardNavExploreXP(obj);
                }
                
                unexplored_navs = true;
            }
			//check for sound effects 
			//TODO: use sound effect range from DASE when available
			if (obj->BroadcastID() > 0)
			{
				if (range == 0) range = obj->RangeFrom(player->Position());
				if (range < 40000.0f && !obj->GetIndex(player->EffectNavList()))
				{
					//switch on effect if we get close
					obj->SetIndex(player->EffectNavList());
					obj->SendSoundBroadcast(player);
				}
			}
        }
		// See if we are in the range of an environmental effect
		if (obj->ObjectType() == OT_GWELL || obj->ObjectType() == OT_RADIATION)
		{	
			object_effect = obj->GetEIndex(player->ExploredNavList());
			range = obj->RangeFrom(player->Position());

			if (!object_effect) 
			{
				//currently we are out of range, see if we've come into range
				if (range < obj->Signature())
				{
					obj->SetEIndex(player->ExploredNavList());
					player->SetEnvironmentalEffect(obj);
				}
			}
			else
			{
				if (range > obj->Signature() + 500.0f) //prevent ratcheting
				{
					obj->UnSetEIndex(player->ExploredNavList());
					player->UnSetEnvironmentalEffect(obj);
				}
			}
		}
    }

    return unexplored_navs;
}

void ObjectManager::Explored(Player *player, Object *obj)
{
    if (!obj->GetEIndex(player->ExploredNavList()) )
    {
        obj->SetEIndex(player->ExploredNavList());
        obj->SendNavigation(player);
        player->AwardNavExploreXP(obj);
    }
}

Object *ObjectManager::GetObjectFromID(long object_id)
{
    Object *obj = (0);

	if (object_id < m_StartObjectID && object_id >= 0)
	{
		//LogMessage("Trying to get objectID %d\n", object_id);
	}
    else if (object_id >= m_StartObjectID && object_id < (m_StartObjectID + m_NumberOfObjects))
    {
        obj = m_SectorIndexList[object_id - m_StartObjectID];
        obj->SetLastAccessTime(GetNet7TickCount());
    }
    else if (IS_PLAYER(object_id))
    {
        obj = g_PlayerMgr->GetPlayer(object_id);
    }

    return (obj);
}

//NB object_name must be in string manager
Object *ObjectManager::GetObjectFromName(char *object_name)
{
    Object *obj = (0);
    ObjectList::iterator itrOList;

	for (itrOList = m_SectorIndexList.begin(); itrOList < m_SectorIndexList.end(); ++itrOList) 
	{
        if ((*itrOList)->Name() == object_name)
        {
            return (*itrOList);
        }
	}

    return (0);
}

void ObjectManager::SetSectorManager(SectorManager *sect_manager)
{
    m_SectorMgr = sect_manager;
}

void ObjectManager::PerformObjectAdmin()
{
    Object *obj;
	unsigned long current_tick = GetNet7TickCount();

	unsigned long index;
	unsigned long size = m_SectorIndexList.size();

	for (index = 0; index < size; ++index)	
	{
		obj = m_SectorIndexList[index];
        if (obj->RespawnTick() == 0)
        {
            switch (obj->ObjectType())
            {   
            case OT_RESOURCE:
            case OT_HULK:
            case OT_FLOATING_ORE:
            case OT_HUSK:
				//if object requires reset, blip it out of view and back in
				if (obj->RequiresReset())
				{
					obj->Remove();
					obj->SetRequiresReset(false);
				}
				break;

            case OT_FIELD:
				obj->CheckFieldRespawn(current_tick);
                break;

            case OT_MOBSPAWN:
				//this is where we'd process mob progressions (probably).
                break;
                
            default:
                break;
            }
        }
        else if (obj->RespawnTick() == -1 || obj->IsToBeRemoved())
        {
			obj->SetActive(false);
        }
        else if (obj->RespawnTick() > 0 && current_tick > obj->RespawnTick())
        {
            obj->ResetResource(); 
        }
	} 
}

//need to ensure that only one of these is running per sector at any time. This can be ensured by only sequentially processing players
void ObjectManager::DisplayDynamicObjects(Player *player, bool all_objects)
{
    float scan_range = (float)(player->ShipIndex()->CurrentStats.GetScanRange()); //TODO: should be in Player class
    Object *obj;
	unsigned long current_tick = GetNet7TickCount();
	short broadcast_count = 0;
    short broadcast_max = player->WarpDrive() ? 3 : 10;
	unsigned long index = 0;
	unsigned long size = m_SectorIndexList.size();

	for (index = 0; index < size; ++index) 
	{
		obj = m_SectorIndexList[index];
		if (obj->RespawnTick() == 0 && obj->Active())
        {
            switch (obj->ObjectType())
            {
            case OT_RESOURCE:
            case OT_HULK:
            case OT_FLOATING_ORE:
            case OT_HUSK:
                broadcast_count += HandleObjectUpdate(player, obj);
                break;
                
            case OT_FIELD:
                index += HandleFieldUpdate(player, obj);
                break;

            case OT_MOBSPAWN:
                break;
                
            default:
                //LogMessage("WARNING: unhandled object '%s' [%d] in %s sector\n", obj->Name(), obj->GameID(), m_SectorMgr->GetSectorName(m_SectorMgr->GetSectorID()) );
                break;
            }
        }
		
        if (!all_objects && broadcast_count > broadcast_max)
        {
            break;
        }
	}

    SendRemainingStaticObjs(player);
}

short ObjectManager::HandleObjectUpdate(Player *player, Object *obj)
{
	if (!obj->Active() || (!obj->HasValidContent() && obj->ObjectType() != OT_HUSK)) return 0; //early return for inactive or invalid objects

    float scan_range = (float)(player->ShipIndex()->CurrentStats.GetScanRange());
    short broadcast_count = 0;
    if (player->GrailAffinity() && obj->Level() >= 9 && obj->BaseAsset() == 0x726) //grail affinity device
    {
        scan_range += 12000.0f;
    }

	if (obj->ObjectType() == OT_HUSK && obj->GetPlayerLootLock() == player->GameID() )
	{
		scan_range += 20000.0f;
	}

	if (obj->ObjectType() == OT_HULK)
	{
		scan_range += 2000.0f; // makes it a bit easier to see hulks - especially at low level
	}

    bool is_active = obj->GetIndex(player->ObjectRangeList());
    bool in_range = obj->IsInRange(player->Position(), scan_range, is_active);

    if (in_range && !is_active)
    {
        obj->SetIndex(player->ObjectRangeList());
        obj->SendObject(player);
        broadcast_count = 1;
    }
    else if (!in_range && is_active)
    {
        obj->UnSetIndex(player->ObjectRangeList());
        obj->UnSetIndex(player->ResourceSendList());
        player->RemoveObject(obj->GameID());
    }

    return broadcast_count;
}

short ObjectManager::HandleMOBUpdate(Player *player, Object *obj)
{
    short broadcast_count = 0;
    //we need to traverse the MOB's rangelist and see 
    return broadcast_count;
}

u32 ObjectManager::HandleFieldUpdate(Player *player, Object *obj)
{
	u32 index_advance = 0;

	if (!obj) return 0;

	if (!obj->Active()) 
	{
		return obj->FieldCount(); //early return for inactive fields
	}

    float scan_range = (float)(player->ShipIndex()->CurrentStats.GetScanRange());
    bool is_active = obj->GetIndex(player->ObjectRangeList());
    bool in_range = obj->IsInRange(player->Position(), scan_range, is_active);

    if (in_range && !is_active)
    {
        obj->SetIndex(player->ObjectRangeList());
		//display field centre
		if (player->AdminLevel() >= 80)	obj->SendObject(player);
    }
    else if (!in_range)
    {
        if (is_active)
        {
            obj->UnSetIndex(player->ObjectRangeList());
            RemoveAllFieldAsteroids(player, (Field*)obj);
			if (player->AdminLevel() >= 80)	player->RemoveObject(obj->GameID());
        }
		index_advance = obj->FieldCount(); //set to last object in field
    }

    return (index_advance);
}

void ObjectManager::RemoveField(Object *obj)
{
	Field *field = (Field*)obj;

	if (field->FieldCount() < 1) return;

	//remove the field for all players
	//use sector manager
	if (!m_SectorMgr)
	{
		return;
	}

	//get a player list from the sector manager
	u32 *player_list = GetSectorList();

	//iterate through all players in this sector
	if (player_list)
	{
		Player *p = (0);
		while (g_PlayerMgr->GetNextPlayerOnList(p, player_list))
		{
			if (p)
			{
				RemoveAllFieldAsteroids(p, field);
			}
		}
	}
}

/*void ObjectManager::RemoveAllFieldAsteroids(Player *player, u32 index, ObjectList *sector_list)
{
    Object *obj = (*sector_list)[index];
    short field_count = obj->FieldCount();
    short i;
    index++;

	//loop through all asteroids, ensure they are all removed
	for (i = 0; i < field_count && index < sector_list->size(); ++index, ++i)
	{
        obj = (*sector_list)[index];
		if (obj->GetIndex(player->ObjectRangeList()))
		{
            player->RemoveObject(obj->GameID());			
            obj->UnSetIndex(player->ObjectRangeList());
			obj->UnSetIndex(player->ResourceSendList()); //this should fix the issue with empty looking but non-empty roids.
		}
	}
}*/

void ObjectManager::RemoveAllFieldAsteroids(Player *player, Field *field)
{
    short field_count = field->FieldCount();
    long index;
	Object *obj = field->GetNextObject();

	//loop through all asteroids, ensure they are all removed
	for (index = 0; index < field_count; index++)
	{
		if (obj->GetIndex(player->ObjectRangeList()))
		{
            player->RemoveObject(obj->GameID());			
            obj->UnSetIndex(player->ObjectRangeList());
			obj->UnSetIndex(player->ResourceSendList());
		}
		obj = obj->GetNextObject();
	}
}

//This function will either set a timer to destroy an object, or will do it immediately
void ObjectManager::DestroyObject(Object *obj, long time_to_destroy, long duration)
{
    if (duration == -1)
    {
        obj->SetRespawnTick(-1); //This schedules the object for destruction from sector object list
        obj->SetRemove();
    }
	else if (duration == -2) //this indicates it's a roid that's part of a field, we respawn from the field handling
	{
		obj->SetRespawnTick(-2);
	}
    else
    {
        obj->SetRespawnTick(GetNet7TickCount() + duration);
    }

	obj->SetDestroyTimer(time_to_destroy, duration);
}

void ObjectManager::InitialiseResourceContent()
{
    Object *obj;

    m_Lockdown = true; //we don't want any object creation while we're scanning the list

	unsigned long index = 0;
	unsigned long size = m_SectorIndexList.size();

	for (index = 0; index < size; ++index) 
	{
		obj = m_SectorIndexList[index];        
		switch (obj->ObjectType())
        {
            case OT_RESOURCE:
            case OT_HULK:
            case OT_HUSK:
                obj->ResetResource();
                break;

            default:
                break;
        }
	}

	m_Lockdown = false;
}

Object * ObjectManager::FindStation(short station_number)
{
    ObjectList::iterator itrOList;

    if (station_number == 0) station_number = 1;

	for (itrOList = m_StaticSectorList.begin(); itrOList < m_StaticSectorList.end(); ++itrOList) 
	{
        if ( ((*itrOList)->ObjectType() == OT_STATION) )
        {
            return (*itrOList);
        }
	}

    return (0);
}

Object * ObjectManager::FindPlanet()
{
    ObjectList::iterator itrOList;
	Object *planet = (0);
	Object *obj;
	AssetData *asset;

	for (itrOList = m_StaticSectorList.begin(); itrOList < m_StaticSectorList.end(); ++itrOList) 
	{
		obj = (*itrOList);

		if (obj->BaseAsset() == 1018) continue; //skip rings

		asset = g_ServerMgr->AssetList()->GetAssetData(obj->BaseAsset());

		if (asset && asset->m_CatName[0] == 'M' && asset->m_CatName[1] == 'o' && asset->m_CatName[2] == 'o')
		{
			planet = obj;
			if (rand()%80 < 35) break;
		}
	}

    return planet;
}

Object * ObjectManager::FindGate(long gate_destination)
{
    ObjectList::iterator itrOList;

	if (gate_destination != -1)
	{
		for (itrOList = m_StaticSectorList.begin(); itrOList < m_StaticSectorList.end(); ++itrOList) 
		{
			if ( ((*itrOList)->Destination() == gate_destination) || 
				(gate_destination == 0 && (*itrOList)->Destination() > 0) )
			{
				return (*itrOList);
			}
		}
	}
    return (0);
}

Object * ObjectManager::FindGate()
{
	ObjectList::iterator itrOList;
	long gatecount = 0;
	long gatechoice;
	Object *gate = (0);

	for (itrOList = m_StaticSectorList.begin(); itrOList < m_StaticSectorList.end(); ++itrOList) 
	{
		if ( (*itrOList)->ObjectType() == OT_STARGATE &&  
			(*itrOList)->Destination() > 0 )
		{
			gatecount++;
		}
	}

	if (gatecount == 0) return 0;

	gatechoice = rand()%gatecount;
	gatecount = 0;

	for (itrOList = m_StaticSectorList.begin(); itrOList < m_StaticSectorList.end(); ++itrOList) 
	{
		if ( (*itrOList)->ObjectType() == OT_STARGATE &&  
			(*itrOList)->Destination() > 0 )
		{
			if (gatecount == gatechoice)
			{
				gate = (*itrOList);
				break;
			}
			gatecount++;
		}
	}

	return gate;
}

Object * ObjectManager::FindFirstNav()
{
    ObjectList::iterator itrOList;

	for (itrOList = m_StaticSectorList.begin(); itrOList < m_StaticSectorList.end(); ++itrOList) 
	{
        if ( (*itrOList)->ObjectType() == OT_NAV  )
        {
            return (*itrOList);
        }
	}

    return (0);
}

//This method is called once at sector startup
void ObjectManager::SectorSetup()
{
    ObjectList::iterator itrOList;
    Object *obj;
    Object *last_obj = 0;

	for (itrOList = m_StaticSectorList.begin(); itrOList < m_StaticSectorList.end(); ++itrOList) 
	{
        obj = (*itrOList);
		if (obj->GetBroadcastID())
		{
			obj->SetBroadcastID(obj->GetBroadcastID());
			if (obj->ObjectType() == OT_STATION)
			{
				if (last_obj && obj->RangeFrom(last_obj->Position()) < 40000.0f)
				{
					obj->SetBroadcastID(0);
				}
				last_obj = obj;
			}
		}
	}
}

long ObjectManager::GetAvailableSectorID()
{
    if (m_SectorFXID == 0 || m_SectorFXID > 0x0FFFFFFF)
    {
        m_SectorFXID = m_StartSectorFXID;
    }

    return m_SectorFXID++;
}

void ObjectManager::CalcNewPosition(Object *obj, unsigned long current_time)
{
    obj->CalcNewPosition(current_time);
}

Object *ObjectManager::NearestNav(float *position)
{
    ObjectList::iterator itrOList;
    Object *obj;

	if (!m_SectorMgr || m_SectorMgr->GetSectorID() > 9999 || m_StaticSectorList.size() == 0)
	{
		return (0);
	}

    Object *closest_obj = m_StaticSectorList[0];

    if (!closest_obj)
    {
        return (0);
    }

	for (itrOList = m_StaticSectorList.begin(); itrOList < m_StaticSectorList.end(); ++itrOList) 
	{
        obj = (*itrOList);
        if (obj->ObjectType() != OT_DECO && 
            obj->RangeFrom(position) < closest_obj->RangeFrom(position))
        {
            closest_obj = obj;
        }
	}

    return closest_obj;
}

void ObjectManager::SetObjectsAtRange(Player *p, float range, long *rangelist, long *rangelist2)
{
    ObjectList::iterator itrOList;
    Object *obj;
	float *position = p->Position();
	bool in_range;
	bool in_range2;

	if (!p->GetSectorManager() || p->GetSectorManager()->GetSectorID() > 9999 || m_StaticSectorList.size() == 0)
	{
		return;
	}

	for (itrOList = m_StaticSectorList.begin(); itrOList < m_StaticSectorList.end(); ++itrOList) 
	{
        obj = (*itrOList);

		if (obj->ObjectIndex() >= MAX_NAVS_DECOS) //make sure we don't overwrite the range buffers
		{
			continue;
		}

		in_range = obj->GetIndex(rangelist);
		in_range2 = rangelist2 ? obj->GetIndex(rangelist2) : true; //if we aren't in range of the bigger range list, don't bother checking

		if (in_range2)
		{
			if (in_range && obj->RangeFrom(position) > range + 200.0f)
			{
				obj->UnSetIndex(rangelist);
				obj->OutOfRangeTrigger(p, range);
			}
			else if (!in_range && obj->RangeFrom(position) < range)
			{
				if (range != (float)TRIGGER_RANGE_3 || !p->ObjectIsMoving())
				{
					obj->SetIndex(rangelist);
					obj->InRangeTrigger(p, range);
				}
			}
		}
		else
		{
			//it's out of range of the bigger list, so can't be in this one
			obj->UnSetIndex(rangelist);
		}
	}
}

void ObjectManager::RemovePlayerFromMOBRangeLists(Player *p)
{
    ObjectList::iterator itrOList;
    ObjectList * mob_list = GetMOBList();
    Object *mob;
    
    for (itrOList = mob_list->begin(); itrOList < mob_list->end(); ++itrOList) 
    {
        mob = (*itrOList);
        mob->RemovePlayerFromRangeLists(p);
    }
}

u32 *ObjectManager::GetSectorList()
{
	try
	{
		if (m_SectorMgr)
		{
			return (m_SectorMgr->GetSectorPlayerList());
		}
		else
		{
			return (0);
		}
	}
	catch (...)
	{
		return (0);
	}
}

/*Object *ObjectManager::GetMobFromSpawn(Object *obj, long index)
{
	ObjectList *sector_list = &m_DynamicSectorList;

	if (!sector_list) return 0;

	MOBSpawn *spawn = (MOBSpawn*)obj;

	Object *mob;

	long spawn_count = spawn->SpawnCount();

	if (index < spawn_count)
	{
		//get the resource corresponding to this index
		mob = GetObjectFromID(spawn->GetFirstMOBID() + index);
	}
	else
	{
		LogMessage("ERROR: Attempt to access out of bounds mob from spawn %s, size %d, index = %d\n", obj->Name(), spawn_count, index);
	}

	return mob;
}*/

#if 0
void ObjectManager::SpawnRandomMOB(float *position)
{
    short mob_type = 0;

    switch (rand()%9)
    {
    case 0:
        mob_type = MOB_EnergyPhoenix;
        break;
    case 1:
        mob_type = MOB_Leviathan;
        break;
    case 2:
        mob_type = MOB_Crystalloid2;
        break;
    case 3:
        mob_type = MOB_Oni;
        break;
    case 4:
        mob_type = MOB_Manta;
        break;
    case 5:
        mob_type = MOB_RedDragonAdvancedFighter;
        break;
    case 6:
        mob_type = MOB_MalefariFighter;
        break;
    case 7:
        mob_type = MOB_NebulaKraken;
        break;
    case 8:
        mob_type = MOB_Worm1;
        break;
    }

    MOB *mob = (MOB*)AddNewObject(OT_MOB); //MOB creation
    
    if (mob)
    {
        mob->SetMOBType(mob_type);
        mob->SetPosition(position);
        mob->MovePosition(-200.0f, 0.0f, -200.0f);
        mob->SetLevel(rand()%20 + 1);
        mob->SetActive(true);
        mob->SetRespawnTick(0);
        mob->SetHostileTo(OT_PLAYER);
        mob->SetVelocity(210.0f);
        mob->SetUpdateRate(50);
        float turn = (float)((rand()%11) - 5) * 0.01f;
        if (turn == 0)
        {
            turn = 0.05f;
        }
        mob->Turn(turn);
        mob->SetDefaultStats(turn, MobData[mob_type].behaviour, 210.0f, 50);
		mob->->AddBehaviourPosition(position);
    }
}

void ObjectManager::SpawnSpecificMOB(float *position, short mob_type, short level)
{
    MOB *mob = (MOB*)AddNewObject(OT_MOB); //MOB creation
    
    if (mob)
    {
        mob->SetMOBType(mob_type);
        mob->SetPosition(position);
        mob->SetLevel(level);
        mob->SetActive(true);
        mob->SetRespawnTick(0);
        mob->SetHostileTo(OT_PLAYER);
        mob->SetVelocity(210.0f);
        mob->SetUpdateRate(50);
        mob->SetSignature(10000.0f);
        float turn = (float)((rand()%11) - 5) * 0.01f;
        if (turn == 0)
        {
            turn = 0.05f;
        }
        mob->Turn(turn);
        mob->SetDefaultStats(turn, MobData[mob_type].behaviour, 210.0f, 50);
		mob->AddBehaviourPosition(position);
    }
}
#endif
