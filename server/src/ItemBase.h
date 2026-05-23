// ItemBase.h
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

#include "net7.h"

#ifndef _ITEM_BASE_H_INCLUDED_
#define _ITEM_BASE_H_INCLUDED_

// ItemBaseData.Flags
#define ITEM_FLAGS_NO_TRADE			1
#define ITEM_FLAGS_TEMPORARY		2
#define ITEM_FLAGS_UNIQUE			4
#define ITEM_FLAGS_NO_STORE			8
#define ITEM_FLAGS_NO_DESTROY		16
#define ITEM_FLAGS_NO_MANUFACTURE	128

#define ITEM_TAG					(short)0x17EF  //tag for items
#define AMMO_TAG					(short)0xAEE0  //tag for ammo

#define VALID_ITEM(item)			((item)->m_check == ITEM_TAG)
#define VALID_AMMO(ammo)			((ammo)->m_check == AMMO_TAG)

#define MAX_REFINE_TO	5

// these enums extracted from tools/item editor/items.cs
typedef enum
{
	//Other
	IB_CATEGORY_LOOTED_ITEM = -1,
	IB_CATEGORY_INVALID = 0,

	//Equipment
	IB_CATEGORY_WEAPON = 10,
	IB_CATEGORY_DEVICE = 11,
	IB_CATEGORY_CORE_ITEM = 12,
	IB_CATEGORY_CONSUMABLE = 13,

	//Components
	IB_CATEGORY_ELECTRONIC_ITEM = 50,
	IB_CATEGORY_REACTOR_COMPONENT = 51,
	IB_CATEGORY_FABRICATED_ITEM = 52,
	IB_CATEGORY_WEAPON_COMPONENT = 53,
	IB_CATEGORY_AMMO_COMPONENT = 54,

	//Ores
	IB_CATEGORY_REFINED_RESOURCE = 80,
	IB_CATEGORY_RAW_RESOURCE = 81,

	//Trade
	IB_CATEGORY_TRADE_GOOD = 90
} Category;

typedef enum
{
	//Other
	IB_SUBCATEGORY_OTHER = -1,
	IB_SUBCATEGORY_INVALID = 0,

	//Weapon
	IB_SUBCATEGORY_ENERGY_CANNON = 99,
	IB_SUBCATEGORY_BEAM_WEAPON = 100,
	IB_SUBCATEGORY_PROJECTILE_LAUNCHER = 101,
	IB_SUBCATEGORY_MISSILE_LAUNCHER = 102,
	IB_SUBCATEGORY_AMMO = 103,

	//Device
	IB_SUBCATEGORY_DEVICE = 110,
	IB_SUBCATEGORY_DRONE = 111,

	//Core
	IB_SUBCATEGORY_REACTOR = 120,
	IB_SUBCATEGORY_ENGINE = 121,
	IB_SUBCATEGORY_SHIELD = 122,

	//Consumable
	IB_SUBCATEGORY_CONSUMABLE = 130,

	//Electronic Components
	IB_SUBCATEGORY_SOFTWARE = 140,
	IB_SUBCATEGORY_ELECTRONIC_ITEM = 141,
	IB_SUBCATEGORY_COMPUTER = 142,

	//Reactors
	IB_SUBCATEGORY_POWER_CONVERTER = 150,
	IB_SUBCATEGORY_POWER_COUPLING = 151,
	IB_SUBCATEGORY_POWER_CORE = 152,
	IB_SUBCATEGORY_POWER_GENERATOR = 153,

	//Fabricated
	IB_SUBCATEGORY_CASING = 160,
	IB_SUBCATEGORY_ENGINE_FRAME = 161,
	IB_SUBCATEGORY_DRONE_FRAME = 162,
	IB_SUBCATEGORY_MOUNT = 163,

	//Weapon
	IB_SUBCATEGORY_FIRING_MECHANISM = 170,
	IB_SUBCATEGORY_AMMUNITION_FEEDER = 171,
	IB_SUBCATEGORY_BEAM_OPTIC = 172,
	IB_SUBCATEGORY_WEAPON_BARREL = 173,

	//Ammo
	IB_SUBCATEGORY_AMMUNITION_SLUG = 180,
	IB_SUBCATEGORY_WARHEAD = 181,
	IB_SUBCATEGORY_PROPELLANT = 182,
	IB_SUBCATEGORY_SHELL_CASING = 183,

	//Refined
	IB_SUBCATEGORY_OPTIC_GEM = 200,
	IB_SUBCATEGORY_METAL = 201,
	IB_SUBCATEGORY_CONDUCTOR = 202,
	IB_SUBCATEGORY_ALLOY = 203,
	IB_SUBCATEGORY_RADIOACTIVE = 204,
	IB_SUBCATEGORY_CATALYST = 205,
	IB_SUBCATEGORY_HYDROCARBON = 206,
	IB_SUBCATEGORY_SILICATE = 207,
	IB_SUBCATEGORY_MAGNETIC = 208,
	IB_SUBCATEGORY_GEMSTONE = 209,
	IB_SUBCATEGORY_GAS = 210,
	IB_SUBCATEGORY_CORE_METAL = 211
} SubCategory;

typedef enum
{
	IB_ITEMTYPE_SHIELDS = 2,
	IB_ITEMTYPE_ENGINES = 6,
	IB_ITEMTYPE_REACTORS = 7,
	IB_ITEMTYPE_AMMO = 10,
	IB_ITEMTYPE_DEVICES = 11,
	IB_ITEMTYPE_OREANDCOMPS = 13, 
	IB_ITEMTYPE_BEAMS =	14,
	IB_ITEMTYPE_MISSILES = 15,
	IB_ITEMTYPE_PROJECTILES = 16
} ItemType;

/* Enum defining all of the item fields */
/* The last letter corresponds to the data type */
typedef enum
{
    ITEM_FIELD_UNKNOWN0_I,                  // #0
    ITEM_FIELD_AMMO_S,                      // #1
    ITEM_FIELD_AUTOFIRE_I,                  // #2
    ITEM_FIELD_PROFESSION_RESTRICTION_I,    // #3
    ITEM_FIELD_COMBAT_LVL_REQ_I,            // #4
    ITEM_FIELD_WEAPON_DAMAGE_I,             // #5
    ITEM_FIELD_WEAPON_DAMAGE_TYPE_I,        // #6
    ITEM_FIELD_EFFECT_RANGE_I,              // #7
    ITEM_FIELD_EFFECT_RADIUS_I,             // #8
    ITEM_FIELD_ENERGY_USE_F,                // #9
    ITEM_FIELD_ENERGY_DRAIN_F,              // #10
    ITEM_FIELD_SKILL_REQ_S,                 // #11
    ITEM_FIELD_EXPLORE_LVL_REQ_I,           // #12
    ITEM_FIELD_ITEM_TYPE_S,                 // #13
    ITEM_FIELD_OVERALL_LVL_REQ_I,           // #14
    ITEM_FIELD_MISSLE_MANUV_I,              // #15
    ITEM_FIELD_REACTOR_CAP_I,               // #16
    ITEM_FIELD_LORE_RESTRICTION_I,          // #17
    ITEM_FIELD_RACE_RESTRICTION_I,          // #18
    ITEM_FIELD_WEAPON_RANGE_I,              // #19
    ITEM_FIELD_REACTOR_RECHARGE_F,          // #20
    ITEM_FIELD_WEAPON_RELOAD_F,             // #21
    ITEM_FIELD_ROUND_PER_SHOT_I,            // #22
    ITEM_FIELD_SHIELD_USE_F,                // #23
    ITEM_FIELD_SHIELD_CAP_I,                // #24
    ITEM_FIELD_SHIELD_DRAIN_F,              // #25
    ITEM_FIELD_SHIELD_RECHARGE_F,           // #26
    ITEM_FIELD_OTHER_REQ_S,                 // #27
    ITEM_FIELD_OTHER_REQ_LVL_I,             // #28
    ITEM_FIELD_ENGINE_SIGNATURE_I,          // #29
    ITEM_FIELD_UNKNOWN30_I,                 // #30
    ITEM_FIELD_ENGINE_SPEED_I,              // #31
    ITEM_FIELD_TRADE_LVL_REQ_I,             // #32
    ITEM_FIELD_ENGINE_WARP_SPEED_I,         // #33
    ITEM_FIELD_ENGINE_FREEWARP_DRAIN_F,     // #34
    ITEM_FIELD_OVERRIDE_FLAGS_I,            // #35
    ITEM_FIELD_OVERRIDE_SKILL_INCREASE_F,   // #36
    ITEM_FIELD_OVERRIDE_CRIT_INCREASE_F     // #37
} ItemBaseFields;

typedef enum
{
    ITEM_NO_PROFESSION_RESTRICTION,
    ITEM_WARRIOR_RESTRICTED,
    ITEM_TRADER_RESTRICTED,
    ITEM_EXPLORER_ONLY,
    ITEM_EXPLORER_RESTRICTED,
    ITEM_TRADER_ONLY,
    ITEM_WARRIOR_ONLY,
    ITEM_PROFESSION_RESTRICTED
} ProfessionRestriction;

typedef enum
{
    ITEM_NO_RACE_RESTRICTION,
    ITEM_TERRAN_RESTRICTED,
    ITEM_JENQUAI_RESTRICTED,
    ITEM_PROJEN_ONLY,
    ITEM_PROJEN_RESTRICTED,
    ITEM_JENQUAI_ONLY,
    ITEM_TERRAN_ONLY,
    ITEM_RACE_RESTRICTED
} RaceRestriction;

typedef enum
{
    DAMAGE_IMPACT,
    DAMAGE_EXPLOSIVE,
    DAMAGE_PLASMA,
    DAMAGE_ENERGY,
    DAMAGE_EMP,
    DAMAGE_CHEMICAL
} DamageType;

struct ItemInstance
{
    int WeaponDamage;
    int WeaponDamageType;
    int WeaponRange;
    float WeaponReload;
    char * WeaponAmmo;

    int EffectRange;
    int EffectRadius;
    float EnergyUse;
    float EnergyDrain;
    float ShieldUse;
    float ShieldDrain;

    float ReactorCap;
    float ReactorRecharge;

    float ShieldCap;
    float ShieldRecharge;

    int EngineSignature;
    int EngineSpeed;
    int EngineWarpSpeed;
    float EngineFreeWarpDrain;

    //int OverrideFlags;
    //float OverrideSkillIncrease;
    //float OverrideCritIncrease;
} ATTRIB_PACKED;

struct AmmoInstance
{
    int WeaponDamage;
    int WeaponRange;
    int WeaponDamageType;
    int MissleManeuverability;
} ATTRIB_PACKED;

struct EffectInstance
{
	struct
	{
		float DescVar[3];
	} Active[6];
	struct
	{
		float DescVar[3];
	} Passive[6];
} ATTRIB_PACKED;

struct ItemRequirements
{
    int RaceRestriction;
    int ProfessionRestriction;
    int LoreRestriction;
    int CombatRequirement;
    int ExploreRequirement;
    int TradeRequirement;
    int OverallRequirement;
} ATTRIB_PACKED;


//------------------------ ItemData Structs 

struct ItemField
{
    bool HasData;
    int iData;
    float fData;
    char * sData;
} ATTRIB_PACKED;

struct ItemEffect
{
	char * Name;
	char * Description;
    char * Tooltip;
    int VisualEffect;
	bool ObjectToObject;
	int Flag1;
	int Flag2;
    int DescVarCount;

	char * BuffName;
	// 
	float	VarMod[3];

	// Stats for Varables
	char *	VarStats[3];
	int		VarType[3];
    float	DescVar[3];

	// Stats for Constants
	char *	ConstStats[2];
	int		ConstType[2];
	float	ConstValue[2];
} ATTRIB_PACKED;

struct EffectData
{
    int Count;
    ItemEffect Effects[6];
	// Container Data
	int RechargeTime;
	float Unknown2;
	int EffectRange;
	int Unknown4;
	int EnergyUse;
} ATTRIB_PACKED;

struct ItemBaseData
{
	int ItemTemplateID;
	int Category;
	int SubCategory;
    int ItemType;

    char * Name;
	char * Description;
	char * Manufacturer;

	short GameBaseAsset;
	short IconBaseAsset;
	short TechLevel;
    short MaxStack;

   	int Cost;             //Cost will never be more than 2.1 billion
	int manufactureCost;  //Cost to manufacture this item
	int manufacture_diff; //Difficulty to manufacture
	int analyse_diff;	  //Difficulty to analyse
	int vendorSellPrice;  //Price this vendor charges players for this item.
	int vendorBuyPrice;   //Price this vendor will pay for an item from a player.
	int UseEffect;        //Visual effect when the item is activated (not when effects hit)
	int EquipEffect;	  //Visual effect when an item is equipted
	int Flags;

	int ItemFieldCount;
	int Components[6];
	ItemField ItemFields[38];
	float ItemMod[2];			// Item Quality Mod.
    
    EffectData ActivatableEffects;
    EffectData EquipableEffects;
} ATTRIB_PACKED;

class Field;

class ItemBase 
{
public:
    ItemBase();
    virtual ~ItemBase();

public:
    bool BuildPacket();

public:
	short m_check;

public:
    int PacketLength();
    unsigned char * Packet();

    ItemBaseData * Data();
    int ItemTemplateID();
    int Category();
    int SubCategory();
    int ItemType();

    char * Name();
    char * Description();
    char * Manufacturer();

    u16 GameBaseAsset();
    u16 IconBaseAsset();
    u16 TechLevel();
    u16 MaxStack();

	int FieldType(int FieldID);

    int Cost();
	int ManufactureCost();
	int ManufactureDifficulty();
	int AnalyseDifficulty();
	int VendorSellPrice();
	int VendorBuyPrice();
    int UseEffect();
    int Flags();
	int EquipEffect();

    int NumItemFields();
    ItemField * Fields(int idx);

    int ActivatableCount();
	int EquipableCount();
	
	int ActivatableEnergyUse();
	int ActivatableRechargeTime();
	int ActivatableEffectRange();

    int RefinesInto(int idx);
    int Component(int idx);

    void SetData(ItemBaseData *);

    void SetRefinesInto(int val);
    void SetComponent(int idx, int val);

    ItemInstance GetItemInstance(char *ItemInstanceInfo);
    AmmoInstance GetAmmoInstance(char *ItemInstanceInfo);
	EffectInstance GetEffectInstance(char *ActiveInstanceInfo, char *PassiveInstanceInfo);

	ItemEffect * GetEquipEffect(int index);
	ItemEffect * GetActiveEffect(int index);

    ItemRequirements GetItemRequirements();

private:
    void BuildItemBasePacket();

private:
	ItemBaseData m_Data;

    int m_RefinesInto[MAX_REFINE_TO];

    int m_PacketLength;
    unsigned char * m_Packet;
};

#endif // _ITEM_BASE_H_INCLUDED_
