// FieldClass.h
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

#ifndef _FIELD_CLASS_H_INCLUDED_
#define _FIELD_CLASS_H_INCLUDED_

#include "SectorData.h"
#include "ObjectClass.h"

class Player;

typedef std::vector<long> ResourceIDList;
typedef std::vector<long> MOBGameIDList;
typedef std::vector<OreNode*> ItemIDList;

#define CLEAR_SIZE 10
//this keeps track of who's been emptying the field
struct PlayerClearList
{
	long game_id;
	long clear_count;
	u32	 last_clear;
};

class Field : public Object
{
public:
    Field(long object_id);
    virtual ~Field();
    float   FieldRadius()                   { return (m_FieldRadius); };
    void    SetFieldRadius(float radius)    { m_FieldRadius = radius; };
    void    SetFieldCount(u16 count)		{ m_FieldCount = count; };
    short   FieldCount()                    { return (m_FieldCount); };
    void    SetHSV(float h1, float h2, float h3);
    void    SetFieldType(u16 type)			{ m_FieldType = type; };
	void	SetRespawnTimer(int respawn_time);
	long	GetFieldType()					{ return (long)m_FieldType; };
    void    PopulateField(bool live = true, bool repopulate = false);
    Object *SetDestination(Object *current);
    void    AddMOBID(long MOBID);
	void	AddItemID(long item_id, float frequency);
	void	BlankItemIDs();
    void    AddResource(long resource);
    void    PopulateSpawn(bool live = true) { PopulateField(live); };
	void	ResetResource();
	void	FullResetResource();
	void	SendAuxDataPacket(Player *player);
	void	SendPosition(Player *player);
	void	SendObjectEffects(Player *player);
	void	SendToVisibilityList(bool include_player);
	void	AddToClearList(Player *player);
	long	GetRespawnTimer()				{ return (long)m_Respawn_timer; }

	ItemIDList *GetAdditionalItemIDs();

	long	GetFirstFieldIndex()			{ return m_FirstFieldID; }
	void	CheckFieldRespawn(u32 tick);

	Object *GetLastFieldAsteroid()			{ return m_LastFieldAsteroid; }

	void    SetLastAccessTime(unsigned long time);      

private:
    void    PopulateTypes(u16 &field_spread);
    void    AddFieldGuardian(bool repopulate);
	void	AwardFieldClearXP();
	void	SetDatabaseFieldRespawnTime();

private:
	unsigned long	m_Respawn_tick;
	u16				m_Respawn_timer;
    u16				m_Resource_value;
    float           m_FieldRadius;
    u16				m_FieldCount;
    u16				m_FieldType;
	u16				m_FieldCountRemaining;
	u16				m_RapidClearCount;
	u16				m_LevelBoost;
    long            m_FirstFieldID;
	u32				m_FieldRespawn;
	u32				m_LastFieldClear;
    MOBIDList       m_MOBIDs;
    ResourceIDList  m_ResourceIDs;
	MOBGameIDList	m_MOBGameIDs;
	ItemIDList		m_AdditionalOreItemIDs;
	PlayerClearList m_PlayerClearList[CLEAR_SIZE]; //keep track of up to 'CLEAR_SIZE' players clearing this field
	Object		*	m_LastFieldAsteroid;
};

#endif // _RESOURCE_CLASS_H_INCLUDED_