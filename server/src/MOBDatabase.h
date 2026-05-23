// MOBDatabaseSQL.h
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

#ifndef _MOBDATABASE_SQL_H_INCLUDED_
#define _MOBDATABASE_SQL_H_INCLUDED_

#include <map>
#include <vector>

#define SKILL_LIST_SIZE	10

// forward references
class ItemBase;
struct SectorData;
struct MOBData;
class sql_connection_c;
struct LootNode;

typedef std::map<unsigned long, MOBData*> MOBList;
typedef std::vector<LootNode*> MOBLoot;

class MOBContent
{
// Constructor/Destructor
public:
    MOBContent();
	virtual ~MOBContent();

// Public Methods
public:
    bool        LoadMOBContent();
    MOBData *   GetMOBData(long MOB_id);
	MOBData *	GetMOBDataFromBasset(short MOB_basset);
	long		GetMOBIDFromBasset(short MOB_basset);
    void        ReloadMOBData(long MOB_id);
    long        GetMOBCount();
	MOBData *	GetMOBforLevel(long level);

// Private Methods
private:
    //bool ParseSectorContent(char *data);
    //void AddMOBTypes(Object *obj, long resource_id, sql_connection_c *connection);

// Private Member Attributes
private:
    MOBList      m_MOB;
    long         m_highest_id;
	bool	     m_updating;

};

struct LootNode
{
	long item_base_id;
	float drop_chance;
	float usage_chance;
	u8 type;
	u8 quantity;
};

//This is incomplete, just want to get some data read in for now
struct MOBData
{
    char   *m_Name;
    u8      m_Level;
    u8      m_Intelligence;
    u8      m_Bravery;
    u8      m_Type;
    u16     m_FactionID;
    u16     m_Basset;
    u8      m_Altruism;
    u8      m_Agressiveness;
	bool	m_CreateWarning;
    float   m_Scale;
    float   m_HSV[3];
	int 	m_SkillIDs[SKILL_LIST_SIZE];
	int     m_NumSkills;
	int		m_SkillChance;
	int		m_SkillCooldown;
	int		m_MOB_ID;		// back index into the MOBData structure

	float	m_ShieldModifier;
	float	m_DamageModifier;
	float	m_RangeModifier;

	//loot
	MOBLoot m_Loot;
};


#endif // _MOBDATABASE_SQL_H_INCLUDED_

