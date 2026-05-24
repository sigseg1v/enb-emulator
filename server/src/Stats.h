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


#ifndef _STATS_H_INCLUDED_
#define _STATS_H_INCLUDED_
#include <net7/Mutex.h>
#include "ItemBase.h"
#include <map>
#include <vector>
#include <string>

using namespace std;

typedef enum
{
    STAT_BASE_VALUE = 0,
    STAT_BUFF_VALUE,
	STAT_BUFF_MULT,
    STAT_DEBUFF_VALUE,
    STAT_DEBUFF_MULT,
	STAT_VALUE_OVERRIDE,
	STAT_BUFF_OVERRIDE_MULT,
	STAT_DEBUFF_OVERRIDE_MULT,
	NUM_STAT_TYPES
} StatBase;

// TODO: Add more
// NOTE: THESE NEED TO MATCH THE ITEMS IN THE DATABASE
// Shield Stuff
#define STAT_SHIELD				"STAT_SHIELD"
#define STAT_SHIELD_RECHARGE	"STAT_SHIELD_RECHARGE"
// Reactor Stuff
#define STAT_ENERGY				"STAT_ENERGY"
#define STAT_ENERGY_RECHARGE	"STAT_ENERGY_RECHARGE"
// TURBOs
#define STAT_WEAPON_TURBO		"STAT_WEAPON_TURBO"
#define STAT_BEAM_TURBO			"STAT_BEAM_TURBO"
#define STAT_PROJECTILE_TURBO	"STAT_PROJECTILE_TURBO"
#define STAT_MISSILE_TURBO		"STAT_MISSILE_TURBO"
// Engine Stuff
#define STAT_TURN_RATE			"STAT_TURN_RATE"
#define STAT_IMPULSE			"STAT_IMPULSE"
#define STAT_SHIP_MASS			"STAT_SHIP_MASS"
#define STAT_WARP				"STAT_WARP"
#define STAT_WARP_RECOVERY		"STAT_WARP_RECOVERY"
#define STAT_WARP_ENERGY		"STAT_WARP_ENERGY"
#define STAT_ENGINE_TOP_SPEED	"STAT_ENGINE_TOP_SPEED"
#define STAT_SIGNATURE			"STAT_SIGNATURE"
// Weapon Damages
#define STAT_BEAM_DAMAGE		"STAT_BEAM_DAMAGE"
#define STAT_PROJECTILES_DAMAGE "STAT_PROJECTILES_DAMAGE"
#define STAT_MISSILE_DAMAGE		"STAT_MISSILE_DAMAGE"
// Weapon Ranges
#define STAT_MISSILE_RANGE		"STAT_MISSILE_RANGE"
#define STAT_PROJECTILES_RANGE	"STAT_PROJECTILES_RANGE"
#define STAT_BEAM_RANGE			"STAT_BEAM_RANGE"
// Weapon Accuracies
#define STAT_MISSILE_ACCURACY		"STAT_MISSILE_ACCURACY"
#define STAT_PROJECTILE_ACCURACY	"STAT_PROJECTILE_ACCURACY"
#define STAT_BEAM_ACCURACY			"STAT_BEAM_ACCURACY"
// Weapon Energy
#define STAT_WEAPON_ENERGY_CONSERVATION		"STAT_WEAPON_ENERGY"
#define STAT_MISSILE_ENERGY_CONSERVATION	"STAT_MISSILE_ENERGY"
#define STAT_PROJECTILE_ENERGY_CONSERVATION	"STAT_PROJECTILE_ENERGY"
#define STAT_BEAM_ENERGY_CONSERVATION		"STAT_BEAM_ENERGY"
// Other Stats
#define STAT_SCAN_RANGE				"STAT_SCAN_RANGE"
#define STAT_TRACTORBEAM_SPEED		"STAT_TRACTORBEAM_SPEED"
#define STAT_TRACTORBEAM_RANGE		"STAT_TRACTORBEAM_RANGE"
#define STAT_CRITICAL_RATE			"STAT_CRITICAL_RATE"
#define STAT_SALES_BONUS			"STAT_SALES_BONUS" // racial buff only
#define STAT_EQUIPMENT_ENGINEERING	"STAT_EQUIPMENT_ENGINEERING"
// Damage Control
#define STAT_HULL_DAMAGE_CONTROL				"STAT_HULL_DAMAGE_CONTROL"
#define STAT_EQUIPMENT_DAMAGE_CONTROL			"STAT_EQUIPMENT_DAMAGE_CONTROL"
#define STAT_EQUIPMENT_DAMAGE_CONTROL_DEVICES	"STAT_EQUIPMENT_DAMAGE_CONTROL_DEVICES"
#define STAT_EQUIPMENT_DAMAGE_CONTROL_ENGINE	"STAT_EQUIPMENT_DAMAGE_CONTROL_ENGINE"
#define STAT_EQUIPMENT_DAMAGE_CONTROL_SHIELD	"STAT_EQUIPMENT_DAMAGE_CONTROL_SHIELD"
#define STAT_EQUIPMENT_DAMAGE_CONTROL_REACTOR	"STAT_EQUIPMENT_DAMAGE_CONTROL_REACTOR"
#define STAT_EQUIPMENT_DAMAGE_CONTROL_WEAPONS	"STAT_EQUIPMENT_DAMAGE_CONTROL_WEAPONS"
// Deflects	
#define STAT_CHEM_DEFLECT		"STAT_CHEM_DEFLECT"
#define STAT_IMPACT_DEFLECT		"STAT_IMPACT_DEFLECT"
#define STAT_ENERGY_DEFLECT		"STAT_ENERGY_DEFLECT"
#define STAT_PLASMA_DEFLECT		"STAT_PLASMA_DEFLECT"
#define	STAT_PSIONIC_DEFLECT	"STAT_PSIONIC_DEFLECT"
#define STAT_EXPLOSIVE_DEFLECT	"STAT_EXPLOSIVE_DEFLECT"
#define STAT_EMP_DEFLECT		"STAT_EMP_DEFLECT"
// Skills
#define STAT_SKILL_NAVIGATE				"SKILL_NAVIGATE"
#define STAT_SKILL_SCAN					"SKILL_SCAN"
#define STAT_SKILL_MISSILE_WEAPON		"SKILL_MISSILE_WEAPON"
#define STAT_SKILL_PROJECTILE_WEAPON	"SKILL_PROJECTILE_WEAPON"
#define STAT_SKILL_BEAM_WEAPON			"SKILL_BEAM_WEAPON"
#define STAT_SKILL_CLOAK				"SKILL_CLOAK"
#define STAT_SKILL_CRITICAL_TARGETING	"SKILL_CRITICAL_TARGETING"
#define STAT_SKILL_DAMAGE_CONTROL		"SKILL_DAMAGE_CONTROL"
#define STAT_SKILL_NEGOTIATE			"SKILL_NEGOTIATE"
#define STAT_SKILL_COMBAT_TRANCE		"SKILL_COMBAT_TRANCE"
#define STAT_SKILL_HULL_PATCH			"SKILL_HULL_PATCH"
#define STAT_SKILL_PROSPECT				"SKILL_PROSPECT"
#define STAT_SKILL_BUILD_DEVICES		"SKILL_BUILD_DEVICES"
#define STAT_SKILL_BUILD_ENGINES		"SKILL_BUILD_ENGINES"
#define STAT_SKILL_BUILD_SHIELDS		"SKILL_BUILD_SHIELDS"
#define STAT_SKILL_BUILD_REACTORS		"SKILL_BUILD_REACTORS"
#define STAT_SKILL_BUILD_WEAPONS		"SKILL_BUILD_WEAPONS"
#define STAT_SKILL_BUILD_ALL			"SKILL_BUILD_ALL"
#define STAT_SKILL_RALLY                "SKILL_RALLY"
#define STAT_SKILL_BEFRIEND             "SKILL_BEFRIEND"
#define STAT_SKILL_REPULSOR_FIELD       "SKILL_REPULSOR_FIELD"
#define STAT_SKILL_JUMPSTART            "SKILL_JUMPSTART"
#define STAT_SKILL_GRAVITY_LINK         "SKILL_GRAVITY_LINK"
#define STAT_SKILL_SHIELD_INVERSION     "SKILL_SHIELD_INVERSION"
#define STAT_SKILL_BIOREPRESSION		"SKILL_BIOREPRESSION"
#define STAT_SKILL_SUMMON				"SKILL_SUMMON"
#define STAT_SKILL_AFTERBURN			"SKILL_AFTERBURN"
#define STAT_SKILL_SHIELD_SAP			"SKILL_SHIELD_SAP"
#define STAT_SKILL_SHIELD_LEECH			"SKILL_SHIELD_LEECH"
#define STAT_SKILL_SHIELD_INVERSION		"SKILL_SHIELD_INVERSION"
#define STAT_SKILL_SHIELD_CHARGING		"SKILL_SHIELD_CHARGING"
#define STAT_SKILL_SELF_DESTRUCT		"SKILL_SELF_DESTRUCT"
#define STAT_SKILL_REPAIR_EQUIPMENT		"SKILL_REPAIR_EQUIPMENT"
#define STAT_SKILL_RECHARGE_SHIELDS		"SKILL_RECHARGE_SHIELDS"
#define STAT_SKILL_PSIONIC_SHIELD		"SKILL_PSIONIC_SHIELD"
#define STAT_SKILL_MENACE				"SKILL_MENACE"
#define STAT_SKILL_JUMP_START			"SKILL_JUMP_START"
#define STAT_SKILL_HACKING				"SKILL_HACKING"
#define STAT_SKILL_FOLD_SPACE			"SKILL_FOLD_SPACE"
#define STAT_SKILL_ENVIRONMENT_SHIELD	"SKILL_ENVIRONMENT_SHIELD"
#define STAT_SKILL_ENERGY_LEECH			"SKILL_ENERGY_LEECH"
#define	STAT_SKILL_REACTOR_OPTIMISATION	"SKILL_REACTOR_OPTIMISATION"

//Abilities
#define STAT_CLOAK						"STAT_CLOAK"
#define STAT_SHIELD_RECHARGING			"STAT_SHIELD_RECHARGING"
#define STAT_SHIELD_RECHARGING_RANGE	"STAT_SHIELD_RECHARGING_RANGE"
#define STAT_SHIELD_RECHARGING_ECOST	"STAT_SHIELD_RECHARGING_ECOST"
#define STAT_HULL_PATCH					"STAT_HULL_PATCH"
#define STAT_HULL_PATCH_RANGE			"STAT_HULL_PATCH_RANGE"
#define STAT_HULL_PATCH_ECOST			"STAT_HULL_PATCH_ECOST"
#define STAT_POWERDOWN                  "STAT_POWERDOWN"

#define STAT_REPAIR_EQUIPMENT			"STAT_REPAIR_EQUIPMENT"

#define STAT_SHIELD_INVERSION_DURATION  "STAT_SHIELD_INVERSION_DURATION"
#define STAT_FOLD_SPACE_DISTANCE        "STAT_FOLD_SPACE_DISTANCE"
#define STAT_CLOAKING_TIME              "STAT_CLOAKING_TIME"

// Stores the values for each added Stat
typedef map<int, float> mapStatValue;

// Lists all the values for each Stat
struct MapValue
{
	mapStatValue Values;
	float MaxValue;
} ATTRIB_PACKED;

typedef map<string, MapValue> mapBuffStats;
// Stores each buff with its max value for the stat
struct MapBuff
{
	mapBuffStats Buff;
	float Total;
	bool Valid;
} ATTRIB_PACKED;

struct MapType
{
	MapBuff Types[NUM_STAT_TYPES];
	float Total;
} ATTRIB_PACKED;

typedef map<string, MapType> mapStatValues;

typedef void(__stdcall *StatCallBack)(char *);


struct StatLookup
{
	string StatName;
	int Type;
	string BuffName;
} ATTRIB_PACKED;

// used in loops
typedef map<int, StatLookup> mapValueIdLookup;
typedef map<string, StatCallBack> mapStatFuncLookup;



class Object;
class Player;
class MOB;

class Stats
{
public:
    Stats();
    ~Stats();

    void Init(Object *);

	// CallBacks
	//bool	StatCallBack(StatCallBack func, char *Stat);

	// Stats
	bool	ChangeStat( int StatID, float NewValue );
	int  	SetStat( int BaseType, string Stat, float Data, string BuffName = "BASE_VALUE");
	bool	DelStat( int StatD );
	float	GetStat( string Stat );									// main choice to call when the STAT_BASE_VALUE is setup
	float	GetStatType( string Stat, int Type );					// best not to use this
	float   ModifyValueWithStat(string Stats, float Base_Value );	// alternative for when there isnt a base value set
	void	UpdateAux(string StatName);
	void	ResetStats();				// Reset all stats
	void	ResetStat(string Stat);

private:
	float	CalculateStat( string Stat );
	float	FindMaxBuff( string Stat, string Buff, int Type);
	float	CalculateStatType( string Stat, int Type );
	void	CalculateValue(string Stat, int Type, string Buff);
	
	
	bool    _ChangeStat( int StatID, float NewValue );
	int     _SetStat(int BaseType, string Stat, float Data, string BuffName = "BASE_VALUE");
	bool    _DelStat(int StatID);
	float   _GetStat(string Stat);
	float	_GetStatType( string Stat, int Type );					// best not to use this
	void	_UpdateAux(string Stat);
	
	
private:
	Mutex   m_Mutex;
	Object * m_Owner;
    Player * m_Player;
	MOB * m_MOB;
	StatBase m_StatType;

	int m_ValueID;
	mapValueIdLookup	m_ValueLookup;
	mapStatValues		m_StatsValues;
	mapStatFuncLookup	m_CallBackFunc;	// Save Functions to call when a stat is updated

/*	// Holds the stats for each player
	float m_Value[200];					// Saves the full calculation
	float m_BaseValue[200];				// Saves base stats (from items)
	float m_BuffValue[200];				// Saves any buffs (this includes actives and equips)
	float m_BuffMult[200];
	float m_DeBuffValue[200];			// Any debuffs to a player is stored here
	float m_DeBuffMult[200];*/
};

#endif