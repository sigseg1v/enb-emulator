// ObjectManager.h
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

#ifndef _OBJECT_MANAGER_H_INCLUDED_
#define _OBJECT_MANAGER_H_INCLUDED_



#include "ObjectClass.h"
#include "FieldClass.h"
#include "ResourceClass.h"
#include "MOBClass.h"
#include "MOBSpawnClass.h"
#include "PlayerClass.h"
#include "PlanetClass.h"
#include "NavTypeClass.h"
#include "MemoryHandler.h"
#include "HuskClass.h"
#include <vector>
#include <map>

struct EffectCancel
{
    long time_delay;
    long effect_id;
};

#define DEFAULT_TEMP_OBJLIST_SIZE 200 //by default have 200 temporary object slots per sector

typedef std::vector<Object*> ObjectList;
typedef std::map<long, Object*> GlobalObjectList;

class ObjectManager
{
// Constructor/Destructor
public:
    ObjectManager();
	virtual ~ObjectManager();

public:
	void		DeleteAllObjects();
    Object    * AddNewObject(object_type ot, bool static_obj = false);
    Object    * GetObjectFromID(long object_id);
    Object    * GetObjectFromName(char *object_name); //NB this call is slow! Try to avoid use within game loop (used for setting up missions).
    Object    * FindStation(short station_number);
    Object    * FindGate(long gate_id);
	Object	  * FindGate(); //find a random gate in this sector
    Object    * FindFirstNav();
    Object    * NearestNav(float *position);
	Object    * FindPlanet();

    void        SetLockdown(bool lockdown) { m_Lockdown = lockdown; };

	void        DestroyObject(Object *obj, long time_to_destroy, long duration);

    void        SendAllNavs(Player *player);
    void        SendRemainingStaticObjs(Player *player);
	void		MakeDecosClickable(Player *player);

    void        SpawnRandomMOB(float *position);
    void        SpawnSpecificMOB(float *position, short mob_type, short level);

    void        SendObject(Player *player, Object *obj); //this may be better off in private
    void        SendPosition(Player *player, Object *obj); //handy for direct position sends

    void        SetSectorManager(SectorManager *sect_manager);
    SectorManager *GetSectorManager()         { return (m_SectorMgr); };

    void        RemovePlayerFromMOBRangeLists(Player *p);

    void        DisplayDynamicObjects(Player *player, bool all_objects = false);
	void		PerformObjectAdmin();

    void        InitialiseResourceContent();

    void        SectorSetup();

    long        GetAvailableSectorID();

    u32		  *	GetSectorList();

    bool        CheckNavRanges(Player *player);
    void        Explored(Player *player, Object *obj);
    ObjectList *GetMOBList();

	void		SetSectorID(long sector_id) { m_SectorID = sector_id; }

	void		SetObjectsAtRange(Player *p, float range, long *rangelist, long *rangelist2 = 0);

	Object	*	GetFieldAsteroid(Object *obj, long index);
	Object  *   GetMobFromSpawn(Object *obj, long index);

	void		RemoveField(Object *obj);

//Movement methods to preserve encapsulation
    void        CalcNewPosition(Object *obj, unsigned long current_time);

private:
    long        GetNewGameID();
    void        HandleTempCreation(Object *object);
    void        AssignStaticID(Object *object);
    short       HandleObjectUpdate(Player *player, Object *obj);
    short       HandleMOBUpdate(Player *player, Object *obj);
    u32			HandleFieldUpdate(Player *player, Object* obj);
    void        RemoveAllFieldAsteroids(Player *player, u32 index, Object *obj);
	void		RemoveAllFieldAsteroids(Player *player, Field *field);
    Object    * AddNewMOB(bool static_obj);
    Object    * AddNewField(bool static_obj);
    Object    * AddNewNav(bool appears_in_radar=false);
    Object    * AddNewResource(bool temp);
    Object    * AddNewMOBSpawn();
	Object	  * AddNewHusk(bool temp);
	Object	  * AddNewPlanet();

	void		AddObjectToSectorList(Object *obj);

    void        CheckCreationTarget(Object *obj);

	void		AddToDynamicList(Object *obj);
	void		RemoveFromDynamicList(Object *obj);

	ObjectList *GetDynamicSectorList();

private:
    ObjectList  m_StaticSectorList;   //all static sector objects: navs, starbases. These objects are only ever sent at first connection
    ObjectList  m_SectorIndexList;    //list of pointers to each object that is clickable in the sector. This gives instant translation from GameID to Object
    ObjectList  m_MOBSectorList;      //all MOBs that require more specialised range handling. It'd be a waste of time and power to do this for all objects, but when
                                      //we're running the server on Terrabit RAM with Quantum processors clocking at 'fuck off' speeds it'll be fine.

	Object	  * m_ObjectsList;

	u32			m_ObjectListUpdated;

    MemorySlot<Resource>    *m_TempResources;   //Re-usable memory for temp resources (hulks, asteroids, corpses, anything temp the GMs toss in etc).
	MemorySlot<Husk>		*m_TempHusks;

    //methods involved in sending object data
    void        SendCreateInfo(Player *player, Object *obj);
    void        SendObjectEffects(Player *player, Object *obj);
    void        SendRelationship(Player *player, Object *obj);
    void        SendAuxDataPacket(Player *player, Object *obj);
    void        SendNavigation(Player *player, Object *obj);

    long        m_StartObjectID;
    long        m_NumberOfObjects;
    long        m_StartSectorFXID;
    long        m_SectorFXID;
	long		m_SectorID;

    long        m_SectorMOBCount;

    bool        m_Lockdown;                 //lockdown is imposed when we create asteroid fields, to avoid MOBs/Player temporary stuff etc getting into field space
    SectorManager * m_SectorMgr;
    Mutex       m_Mutex;

};

extern GlobalObjectList g_SectorObjects;

#endif // _OBJECT_MANAGER_H_INCLUDED_
