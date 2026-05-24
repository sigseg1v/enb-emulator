// ItemBase.cpp
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

//
#include "Net7.h"
#include "ItemBase.h"
#include <net7/Opcodes.h>
#include "PlayerClass.h"
#include "PacketMethods.h"
#include "StringManager.h"
#include "ServerManager.h"

ItemBase::ItemBase()
{
    memset(&m_Data, 0, sizeof(ItemBaseData));
    memset(m_RefinesInto, 0, sizeof(m_RefinesInto));

    m_PacketLength = 0;
    m_Packet = 0;
	m_check = ITEM_TAG;
}

ItemBase::~ItemBase()
{
	delete [] m_Packet;
}

int ItemBase::FieldType(int FieldID)
{
	switch (FieldID)
	{
        case 0x01 : // #1  = Ammo Parent
        case 0x0B : // #11 = Requires (0x0B) Level (ItemLVL)
        case 0x0D : // #13 = Item Type (Subdescription)
        case 0x1B : // #27 = String for 0x1C
            return 0;
		    break;

        case 0x00 : // #0  = ????
        case 0x09 : // #9  = Energy Usage
        case 0x0A : // #10 = Energy Drain
        case 0x14 : // #20 = Reactor Recharge Rate
        case 0x15 : // #21 = Weapon Reload
        case 0x17 : // #23 = Shield Usage
        case 0x19 : // #25 = Shield Drain
        case 0x1A : // #26 = Shield Recharge Rate
        case 0x22 : // #34 = Engine Freewarp Drain
        case 0x24 : // #36 = Terminal Override Skill Increase
        case 0x25 : // #37 = Terminal Override Crit Increase
            return 1;
		    break;

        case 0x02 : // #2  = Autofire Box
        case 0x03 : // #3  = Profession Restriction
        case 0x04 : // #4  = Combat Level Requirement
        case 0x05 : // #5  = Weapon Damage
        case 0x06 : // #6  = Weapon Damage Type -- See Enum
        case 0x07 : // #7  = Effect Range
        case 0x08 : // #8  = Effect Radius
        case 0x0C : // #12 = Explore Level Requirement
        case 0x0E : // #14 = Overall Level Requirement
        case 0x0F : // #15 = Missle Manuverability
        case 0x10 : // #16 = Reactor Capacity
        case 0x11 : // #17 = Lore Requirements (1 - Projen Restricted, 2 - Jenq Restricted) 
        case 0x12 : // #18 = Race Requirement
        case 0x13 : // #19 = Weapon Range
        case 0x16 : // #22 = Rounds Per Shot
        case 0x18 : // #24 = Shield Capacity
        case 0x1C : // #28 = (0x1B) Level Requirement
        case 0x1D : // #29 = Engine Signature
        case 0x1E : // #30 = ????
        case 0x1F : // #31 = Engine Speed
        case 0x20 : // #32 = Trade Level Requirement
        case 0x21 : // #33 = Engine Warp Speed
        case 0x23 : // #35 = Terminal Override Flags
            return 2;
		    break;

		default:
			return 0;
			break;
	}
}


void ItemBase::BuildItemBasePacket()
{
    unsigned char buffer[4096];
    memset(buffer, 0, sizeof(buffer));  // Not sure if this is needed anymore
    int i, j, index = 0;

    AddDataFlip4(buffer, m_Data.ItemTemplateID, index);
    AddData(buffer, m_Data.Category, index);
    AddData(buffer, m_Data.SubCategory, index);
    AddData(buffer, m_Data.ItemType, index);
    AddData(buffer, char(m_Data.ItemFieldCount), index);

    for (i=0; i<38; i++)
    {
        if (m_Data.ItemFields[i].HasData)
        {
			AddData(buffer, i, index);  // This is the FlagID
			switch (i)
			{
                case 0x01 : // #1  = Ammo Parent
                case 0x0B : // #11 = Requires (0x0B) Level (ItemLVL)
                case 0x0D : // #13 = Item Type (Subdescription)
                case 0x1B : // #27 = String for 0x1C
                    AddDataLSN(buffer, m_Data.ItemFields[i].sData, index);
				    break;

                case 0x00 : // #0  = ????
                case 0x09 : // #9  = Energy Usage
                case 0x0A : // #10 = Energy Drain
                case 0x14 : // #20 = Reactor Recharge Rate
                case 0x15 : // #21 = Weapon Reload
                case 0x17 : // #23 = Shield Usage
                case 0x19 : // #25 = Shield Drain
                case 0x1A : // #26 = Shield Recharge Rate
                case 0x22 : // #34 = Engine Freewarp Drain
                case 0x24 : // #36 = Terminal Override Skill Increase
                case 0x25 : // #37 = Terminal Override Crit Increase
                    AddData(buffer, m_Data.ItemFields[i].fData, index);
				    break;

                case 0x02 : // #2  = Autofire Box
                case 0x03 : // #3  = Profession Restriction
                case 0x04 : // #4  = Combat Level Requirement
                case 0x05 : // #5  = Weapon Damage
                case 0x06 : // #6  = Weapon Damage Type -- See Enum
                case 0x07 : // #7  = Effect Range
                case 0x08 : // #8  = Effect Radius
                case 0x0C : // #12 = Explore Level Requirement
                case 0x0E : // #14 = Overall Level Requirement
                case 0x0F : // #15 = Missle Manuverability
                case 0x10 : // #16 = Reactor Capacity
                case 0x11 : // #17 = Lore Requirements (1 - Projen Restricted, 2 - Jenq Restricted) 
                case 0x12 : // #18 = Race Requirement
                case 0x13 : // #19 = Weapon Range
                case 0x16 : // #22 = Rounds Per Shot
                case 0x18 : // #24 = Shield Capacity
                case 0x1C : // #28 = (0x1B) Level Requirement
                case 0x1D : // #29 = Engine Signature
                case 0x1E : // #30 = ????
                case 0x1F : // #31 = Engine Speed
                case 0x20 : // #32 = Trade Level Requirement
                case 0x21 : // #33 = Engine Warp Speed
                case 0x23 : // #35 = Terminal Override Flags
                    AddData(buffer, m_Data.ItemFields[i].iData, index);
				    break;
			}
        }
    }

    AddDataFlip4(buffer, m_Data.ActivatableEffects.Count, index);

    for (i=0; i<m_Data.ActivatableEffects.Count; i++)
    {
        AddDataLS(buffer, m_Data.ActivatableEffects.Effects[i].Name, index);
        AddDataLS(buffer, m_Data.ActivatableEffects.Effects[i].Description, index);
        AddDataLS(buffer, m_Data.ActivatableEffects.Effects[i].Tooltip, index);

        // Filler data
        AddData(buffer, int(0), index);

        AddDataFlip4(buffer, m_Data.ActivatableEffects.Effects[i].DescVarCount, index);

        for (j=0; j<m_Data.ActivatableEffects.Effects[i].DescVarCount; j++)
        {
            long Data = *(long *)&m_Data.ActivatableEffects.Effects[i].DescVar[j];
            AddDataFlip4(buffer, Data, index);
        }

        AddData(buffer, m_Data.ActivatableEffects.Effects[i].Flag1, index);
        AddData(buffer, m_Data.ActivatableEffects.Effects[i].Flag2, index);
    }

    // This has no effect on the client (filler data)
    if (m_Data.ActivatableEffects.Count > 0)
    {
        AddData(buffer, m_Data.ActivatableEffects.RechargeTime, index);
        AddData(buffer, int(0), index);
		AddData(buffer, m_Data.ActivatableEffects.EffectRange, index);
        AddData(buffer, int(0), index);
    }

    AddDataFlip4(buffer, m_Data.EquipableEffects.Count, index);

    for (i=0; i<m_Data.EquipableEffects.Count; i++)
    {
        AddDataLS(buffer, m_Data.EquipableEffects.Effects[i].Name, index);
        AddDataLS(buffer, m_Data.EquipableEffects.Effects[i].Description, index);
        AddDataLS(buffer, m_Data.EquipableEffects.Effects[i].Tooltip, index);

        // Filler data
        AddData(buffer, int(0), index);

        AddDataFlip4(buffer, m_Data.EquipableEffects.Effects[i].DescVarCount, index);

        for (j=0; j<m_Data.EquipableEffects.Effects[i].DescVarCount; j++)
        {
            long Data = *(long *)&m_Data.EquipableEffects.Effects[i].DescVar[j];
            AddDataFlip4(buffer, Data, index);
        }

        AddData(buffer, m_Data.EquipableEffects.Effects[i].Flag1, index);
        AddData(buffer, m_Data.EquipableEffects.Effects[i].Flag2, index);
    }

    // This has no effect on the client (filler data)
    if (m_Data.EquipableEffects.Count > 0)
    {
        AddData(buffer, int(0), index);
        AddData(buffer, int(0), index);
        AddData(buffer, int(0), index);
        AddData(buffer, int(0), index);
    }

    AddDataFlip2(buffer, m_Data.GameBaseAsset, index);
    AddDataFlip2(buffer, m_Data.IconBaseAsset, index);
    AddDataFlip2(buffer, m_Data.TechLevel, index);

    AddDataFlip4(buffer, m_Data.Cost, index);
    AddDataFlip4(buffer, m_Data.MaxStack, index);
    AddDataFlip4(buffer, m_Data.UseEffect, index);

	AddData(buffer, m_Data.Flags, index);

    AddDataLS(buffer, m_Data.Name, index);
    AddDataLS(buffer, m_Data.Description, index);
    AddDataLS(buffer, m_Data.Manufacturer, index);

    // Copy the packet to this class
    m_PacketLength = index;
    m_Packet = new unsigned char[m_PacketLength];
    memcpy(m_Packet, buffer, m_PacketLength);
}

bool ItemBase::BuildPacket()
{
    if (m_PacketLength == 0)
    {
        BuildItemBasePacket();
    }

    // If Build was successful our length is not zero
    return (m_PacketLength != 0);
}

/* Format for instance info is [ID]:[VAL]^[ID]:[VAL]^.... */

ItemInstance ItemBase::GetItemInstance(char * ItemInstanceInfo)
{
    ItemInstance RetVal;
	char *next_token = NULL;
    memset(&RetVal, 0, sizeof(RetVal));

    /* OK - We need to obtain the item instance of this item */
    /* First load up the 100% quality values from Itembase */

    RetVal.EffectRadius = m_Data.ItemFields[ITEM_FIELD_EFFECT_RADIUS_I].iData;
    RetVal.EffectRange = m_Data.ItemFields[ITEM_FIELD_EFFECT_RANGE_I].iData;
    RetVal.EnergyUse = m_Data.ItemFields[ITEM_FIELD_ENERGY_USE_F].fData;
    RetVal.ShieldUse = m_Data.ItemFields[ITEM_FIELD_SHIELD_USE_F].fData;
    RetVal.EnergyDrain = m_Data.ItemFields[ITEM_FIELD_ENERGY_DRAIN_F].fData;
    RetVal.ShieldDrain = m_Data.ItemFields[ITEM_FIELD_SHIELD_DRAIN_F].fData;
    RetVal.EngineSignature = m_Data.ItemFields[ITEM_FIELD_ENGINE_SIGNATURE_I].iData;
    RetVal.EngineSpeed = m_Data.ItemFields[ITEM_FIELD_ENGINE_SPEED_I].iData;
    RetVal.EngineFreeWarpDrain = m_Data.ItemFields[ITEM_FIELD_ENGINE_FREEWARP_DRAIN_F].fData;
    RetVal.EngineWarpSpeed = m_Data.ItemFields[ITEM_FIELD_ENGINE_WARP_SPEED_I].iData;
    RetVal.ReactorCap = float(m_Data.ItemFields[ITEM_FIELD_REACTOR_CAP_I].iData);
    RetVal.ReactorRecharge = m_Data.ItemFields[ITEM_FIELD_REACTOR_RECHARGE_F].fData;
    RetVal.ShieldCap = float(m_Data.ItemFields[ITEM_FIELD_SHIELD_CAP_I].iData);
    RetVal.ShieldRecharge = m_Data.ItemFields[ITEM_FIELD_SHIELD_RECHARGE_F].fData;
    RetVal.WeaponDamage = m_Data.ItemFields[ITEM_FIELD_WEAPON_DAMAGE_I].iData;
    RetVal.WeaponDamageType = m_Data.ItemFields[ITEM_FIELD_WEAPON_DAMAGE_TYPE_I].iData;
    RetVal.WeaponRange = m_Data.ItemFields[ITEM_FIELD_WEAPON_RANGE_I].iData;
    RetVal.WeaponReload = m_Data.ItemFields[ITEM_FIELD_WEAPON_RELOAD_F].fData;
    RetVal.WeaponAmmo = m_Data.ItemFields[ITEM_FIELD_AMMO_S].sData;

    /* Now load up the changes in the InstanceInfo */
    char InstanceBuffer[64];
    char * Part1, * Part2;
	/*There are some memory corruption problems related to items aux data.  This is a buffer overrun identified by the analyze tool*/
    strncpy_s(InstanceBuffer, sizeof(InstanceBuffer), ItemInstanceInfo,63); //only copy 63 characters
	InstanceBuffer[63]='\0'; //terminate string

	Part1 = strtok_s(InstanceBuffer, ":", &next_token);

	while (Part1)
	{
		Part2 = strtok_s(NULL, "^", &next_token);

        if (Part2)
        {
            switch (atoi(Part1))
            {
            case ITEM_FIELD_EFFECT_RADIUS_I:
                RetVal.EffectRadius = atoi(Part2);
                break;

            case ITEM_FIELD_EFFECT_RANGE_I:
                RetVal.EffectRange = atoi(Part2);
                break;

            case ITEM_FIELD_ENERGY_USE_F:
                RetVal.EnergyUse = float(atof(Part2));
                break;

            case ITEM_FIELD_SHIELD_USE_F:
                RetVal.ShieldUse = float(atof(Part2));
                break;

            case ITEM_FIELD_ENERGY_DRAIN_F:
                RetVal.EnergyDrain = float(atof(Part2));
                break;

            case ITEM_FIELD_SHIELD_DRAIN_F:
                RetVal.ShieldDrain = float(atoi(Part2));
                break;

            case ITEM_FIELD_ENGINE_SIGNATURE_I:
                RetVal.EngineSignature = atoi(Part2);
                break;

            case ITEM_FIELD_ENGINE_SPEED_I:
                RetVal.EngineSpeed = atoi(Part2);
                break;

            case ITEM_FIELD_ENGINE_FREEWARP_DRAIN_F:
                RetVal.EngineFreeWarpDrain = float(atof(Part2));
                break;

            case ITEM_FIELD_ENGINE_WARP_SPEED_I:
                RetVal.EngineWarpSpeed = atoi(Part2);
                break;

            case ITEM_FIELD_REACTOR_CAP_I:
                RetVal.ReactorCap = float(atof(Part2));
                break;

            case ITEM_FIELD_REACTOR_RECHARGE_F:
                RetVal.ReactorRecharge = float(atof(Part2));
                break;

            case ITEM_FIELD_SHIELD_CAP_I:
                RetVal.ShieldCap = float(atoi(Part2));
                break;

            case ITEM_FIELD_SHIELD_RECHARGE_F:
                RetVal.ShieldRecharge = float(atof(Part2));
                break;

            case ITEM_FIELD_WEAPON_DAMAGE_I:
                RetVal.WeaponDamage = atoi(Part2);
                break;

            case ITEM_FIELD_WEAPON_DAMAGE_TYPE_I:
                RetVal.WeaponDamageType = atoi(Part2);
                break;

            case ITEM_FIELD_WEAPON_RANGE_I:
                RetVal.WeaponRange = atoi(Part2);
                break;

            case ITEM_FIELD_WEAPON_RELOAD_F:
                RetVal.WeaponReload = float(atof(Part2));
                break;
            }
        }

		Part1 = strtok_s(NULL, ":", &next_token);
    }

    /* Sanity check */
    if (RetVal.WeaponDamage <= 0)
    {
        RetVal.WeaponDamage = 3;
    }

    if (RetVal.WeaponRange <= 0)
    {
        RetVal.WeaponRange = 1000; //1K
    }

    return RetVal;
}

AmmoInstance ItemBase::GetAmmoInstance(char * ItemInstanceInfo)
{
    AmmoInstance RetVal;
    RetVal.WeaponDamage = m_Data.ItemFields[ITEM_FIELD_WEAPON_DAMAGE_I].iData;
    RetVal.WeaponDamageType = m_Data.ItemFields[ITEM_FIELD_WEAPON_DAMAGE_TYPE_I].iData;
    RetVal.WeaponRange = m_Data.ItemFields[ITEM_FIELD_WEAPON_RANGE_I].iData;
    RetVal.MissleManeuverability = m_Data.ItemFields[ITEM_FIELD_MISSLE_MANUV_I].iData;

    /* Now load up the changes in the InstanceInfo */
    char InstanceBuffer[64];
    char * Part1, * Part2;
	char *next_token;

	/*There are some memory corruption problems related to items aux data.  This is a buffer overrun identified by the analyze tool*/
    strncpy_s(InstanceBuffer, sizeof(InstanceBuffer), ItemInstanceInfo,63); //only copy 63 characters
	InstanceBuffer[63]='\0'; //terminate string

	Part1 = strtok_s(InstanceBuffer, ":", &next_token);

	while (Part1)
	{
		Part2 = strtok_s(NULL, "^", &next_token);

        if (Part2)
        {
            switch (atoi(Part1))
            {
            case ITEM_FIELD_WEAPON_DAMAGE_I:
                RetVal.WeaponDamage = atoi(Part2);
                break;

            case ITEM_FIELD_WEAPON_DAMAGE_TYPE_I:
                RetVal.WeaponDamageType = atoi(Part2);
                break;

            case ITEM_FIELD_WEAPON_RANGE_I:
                RetVal.WeaponRange = atoi(Part2);
                break;

            case ITEM_FIELD_MISSLE_MANUV_I:
                RetVal.MissleManeuverability = atoi(Part2);
                break;
            }
        }

		Part1 = strtok_s(NULL, ":", &next_token);
    }

    /* Sanity check */
    if (RetVal.WeaponDamage <= 0)
    {
        RetVal.WeaponDamage = 3;
    }

    if (RetVal.WeaponRange <= 0)
    {
        RetVal.WeaponRange = 1000; //1K
    }

    return RetVal;
}

EffectInstance ItemBase::GetEffectInstance(char *ActiveInstanceInfo, char *PassiveInstanceInfo)
{
	EffectInstance RetVal;
	int i;

	for (i=0;i < 6;i++)
	{
		RetVal.Active[i].DescVar[0] = m_Data.ActivatableEffects.Effects[i].DescVar[0];
		RetVal.Active[i].DescVar[1] = m_Data.ActivatableEffects.Effects[i].DescVar[1];
		RetVal.Active[i].DescVar[2] = m_Data.ActivatableEffects.Effects[i].DescVar[2];
		RetVal.Passive[i].DescVar[0] = m_Data.EquipableEffects.Effects[i].DescVar[0];
		RetVal.Passive[i].DescVar[1] = m_Data.EquipableEffects.Effects[i].DescVar[1];
		RetVal.Passive[i].DescVar[2] = m_Data.EquipableEffects.Effects[i].DescVar[2];
	}
    
	/* Now load up the changes in the InstanceInfo */
    char InstanceBuffer[64];
    char *ptr=InstanceBuffer;
	char *next_token=NULL;
	int count,varcount;

    strncpy_s(InstanceBuffer, sizeof(InstanceBuffer), ActiveInstanceInfo,63); //only copy 63 characters
	InstanceBuffer[63]='\0'; //terminate string

	ptr = strtok_s(InstanceBuffer, "^,", &next_token);
	count = 0;
	while (ptr && count < m_Data.ActivatableEffects.Count)
	{
		varcount = 0;
		while (ptr && varcount <  m_Data.ActivatableEffects.Effects[count].DescVarCount)
		{
			RetVal.Active[count].DescVar[varcount++] = (float)atof(ptr);
			ptr = strtok_s(NULL, "^,", &next_token);
			if (ptr && *ptr == '#')
			{
				ptr = strtok_s(NULL, "^,", &next_token);
				break;
			}
		}
		count++;
	}

	/* Now load up the changes in the InstanceInfo */
    strncpy_s(InstanceBuffer, sizeof(InstanceBuffer), PassiveInstanceInfo,63); //only copy 63 characters
	InstanceBuffer[63]='\0'; //terminate string

	ptr = strtok_s(InstanceBuffer, "^,", &next_token);
	count = 0;
	while (ptr && count < m_Data.EquipableEffects.Count)
	{
		varcount = 0;
		while (ptr && varcount <  m_Data.EquipableEffects.Effects[count].DescVarCount)
		{
			RetVal.Passive[count].DescVar[varcount++] = (float)atof(ptr);
			ptr = strtok_s(NULL, "^,", &next_token);
			if (ptr && *ptr == '#')
			{
				ptr = strtok_s(NULL, "^,", &next_token);
				break;
			}
		}
		count++;
	}

	return RetVal;
}

ItemRequirements ItemBase::GetItemRequirements()
{
    ItemRequirements Reqs;

    Reqs.RaceRestriction = m_Data.ItemFields[ITEM_FIELD_RACE_RESTRICTION_I].iData;
    Reqs.ProfessionRestriction = m_Data.ItemFields[ITEM_FIELD_PROFESSION_RESTRICTION_I].iData;
    Reqs.LoreRestriction = m_Data.ItemFields[ITEM_FIELD_LORE_RESTRICTION_I].iData;
    Reqs.CombatRequirement = m_Data.ItemFields[ITEM_FIELD_COMBAT_LVL_REQ_I].iData;
    Reqs.ExploreRequirement = m_Data.ItemFields[ITEM_FIELD_EXPLORE_LVL_REQ_I].iData;
    Reqs.TradeRequirement = m_Data.ItemFields[ITEM_FIELD_TRADE_LVL_REQ_I].iData;
    Reqs.OverallRequirement = m_Data.ItemFields[ITEM_FIELD_OVERALL_LVL_REQ_I].iData;

    return Reqs;
}

int ItemBase::PacketLength()            {return m_PacketLength;}
unsigned char * ItemBase::Packet()      {return m_Packet;}

ItemBaseData * ItemBase::Data()         {return &m_Data;}
int ItemBase::ItemTemplateID()          {return m_Data.ItemTemplateID;}
int ItemBase::Category()                {return m_Data.Category;}
int ItemBase::SubCategory()             {return m_Data.SubCategory;}
int ItemBase::ItemType()                {return m_Data.ItemType;}

char * ItemBase::Name()                 {return m_Data.Name;}
char * ItemBase::Description()          {return m_Data.Description;}
char * ItemBase::Manufacturer()         {return m_Data.Manufacturer;}


//TODO: check for illegal basset during item parse, rather than each time it's used.
u16 ItemBase::GameBaseAsset()			
{
	//perform check for illegal 2d asset
	AssetData *asset = g_ServerMgr->AssetList()->GetAssetData(m_Data.GameBaseAsset);
	char *cat_name = (0);

	if (asset) cat_name = asset->m_CatName;

	if ((cat_name && (0 == strcmp(cat_name, "Effects") || 
		0 == strcmp(cat_name, "Icons") ||
		0 == strcmp(cat_name, "Backgrounds") )) ||
		!cat_name)
	{
		//make this stick out like a sore thumb
		m_Data.GameBaseAsset = 3000;
	}

	return m_Data.GameBaseAsset;
}

u16 ItemBase::IconBaseAsset()			{return m_Data.IconBaseAsset;}
u16 ItemBase::TechLevel()				{return m_Data.TechLevel;}
u16 ItemBase::MaxStack()				{return m_Data.MaxStack;}

int ItemBase::Cost()                    {return m_Data.Cost;}
int ItemBase::ManufactureCost()         {return m_Data.manufactureCost;}
int ItemBase::ManufactureDifficulty()	{return m_Data.manufacture_diff;}
int ItemBase::AnalyseDifficulty()		{return m_Data.analyse_diff;}
int ItemBase::VendorSellPrice()         {return m_Data.vendorSellPrice;}
int ItemBase::VendorBuyPrice()          {return m_Data.vendorBuyPrice;}
int ItemBase::UseEffect()               {return m_Data.UseEffect;}
int ItemBase::EquipEffect()				{return m_Data.EquipEffect;}
int ItemBase::Flags()                   {return m_Data.Flags;}

int ItemBase::NumItemFields()           {return m_Data.ItemFieldCount;}
ItemField * ItemBase::Fields(int idx)   {return &m_Data.ItemFields[idx];}

int ItemBase::ActivatableCount()        {return m_Data.ActivatableEffects.Count;}
int ItemBase::EquipableCount()          {return m_Data.EquipableEffects.Count;}

int ItemBase::ActivatableEnergyUse()		{return m_Data.ActivatableEffects.EnergyUse;}
int ItemBase::ActivatableRechargeTime()		{return m_Data.ActivatableEffects.RechargeTime;}
int ItemBase::ActivatableEffectRange()		{return m_Data.ActivatableEffects.EffectRange;}

ItemEffect * ItemBase::GetEquipEffect(int index)	
{
	if (index < m_Data.EquipableEffects.Count) 
	{
		return &m_Data.EquipableEffects.Effects[index];
	}
	else
	{
		return 0;
	}
}

ItemEffect * ItemBase::GetActiveEffect(int index)	
{
	if (index < m_Data.ActivatableEffects.Count) 
	{
		return &m_Data.ActivatableEffects.Effects[index];
	}
	else
	{
		return 0;
	}
}

int ItemBase::RefinesInto(int idx)      {return m_RefinesInto[idx];}
int ItemBase::Component(int idx)        {return m_Data.Components[idx];}


void ItemBase::SetData(ItemBaseData * newData)
{
    memcpy(&m_Data, newData, sizeof(ItemBaseData));
}

void ItemBase::SetRefinesInto(int val)
{
	for (int i=0;i < MAX_REFINE_TO;i++)
	{
		// already added
		if (m_RefinesInto[i] == val)
		{
			break;
		}
		// new item
		if (m_RefinesInto[i] == 0)
		{
	    	m_RefinesInto[i] = val;
			break;
		}
	}
}

void ItemBase::SetComponent(int idx, int val)
{
    m_Data.Components[idx] = val;
}
