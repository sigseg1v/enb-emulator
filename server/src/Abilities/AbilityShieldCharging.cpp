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

#include "AbilityShieldCharging.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*
Skill properties:
Description=The Shield Charging ability allows players to temporarily increase their Shield Capacity, Shield Regeneration Rate and Damage Resistances.
Desc_1=Enables the Supercharge Shields ability.
Desc_2=Increases the Shield Capacity of a player by 10%. Ability may only be applied to skill user.
Desc_3=Enables the Ultracharge Shields ability. Ability may only be applied to skill user.
Desc_4=Increases the amount of Shield Capacity by 15%. Ability may only be applied to skill user.
Desc_5=Enables the Supercharge Target ability with a range of up to 3500 units.
Desc_6=Enables the Ultracharge Target ability and increases range to 3750 units.
Desc_7=Enables the Megacharge Shields ability and increases range to 4000 units.
Range=1000,1000,1000,1000,3500,3750,4000,4000,4000
Energy=50,50,125,125,250,500,1000

Supercharge Shields = Player Only Shields +smag%
Ultracharge Shields = Player Only Shields +smag%, Shield Regen +rmag%
Supercharge Target  = Target, Shields +smag%
Ultracharge Target  = Target, Shields +smag% Shield Regen +rmag%
Megacharge Shields  = Target, Shields +smag% Shield Regen +rmag% Resistances +10

float smag = 0.0f;
float rmag = 0.0f;
bool self_only;
int range = 0;

switch(rank)
{
case 1:
	smag = 0.05f;
	rmag = 0.0f;
	resists = 0;
	self_only = true;
	break;
case 2:
	smag = 0.1f;
	rmag = 0.0f;
	resists = 0;
	self_only = true;
	break;
case 3:
	smag = 0.1f;
	rmag = 0.1f;
	resists = 0;
	self_only = true;
	break;
case 4:
	smag = 0.15f;
	rmag = 0.1f;
	resists = 0;
	self_only = true;
	break;
case 5:
	smag = 0.2f;
	rmag = 0.0f;
	resists = 0;
	self_only = false;
	break;
case 6:
	smag = 0.2f;
	rmag = 0.1f;
	resists = 0;
	self_only = false;
	break;
case 7:
	smag = 0.25f;
	rmag = 0.2f;
	resists = 10;
	self_only = false;
	break;
*/

/*
* This calculates the activation cost of the skill.
*/
float AShieldCharging::CalculateEnergy ( float SkillLevel, long SkillRank )
{
	switch (SkillRank)
	{
	case 1:
		return 50.0f;
	case 3:
		return 125.0f;
	case 5:
		return 250.0f;
	case 6:
		return 500.0f;
	case 7:
		return 1000.0f;
	}
	return 0.0f; // shouldnt happen
}

/*
* Calculate how much time must pass before the skill activates.
*
* Results are returned in seconds.
*/
float AShieldCharging::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	//shorter casting time for lower rank with high skill.
	// 5 second default minus the difference between the skill rank and skill level (a negative to 0 value)
	// Capped at a .5 second minimum cast time.
	return 5.0f + ( ((float)SkillRank - SkillLevel) <= -5.0f ? -4.5f : ((float)SkillRank - SkillLevel) );
}

/*
* Calculate the maximum range this rank of the skill can be used at.
*/
float AShieldCharging::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	return 2500.0f + (SkillLevel-1) * 250.0f;
}

/*
* Computes the duration of the ability.
*
* Results are returned in seconds.
*/
float AShieldCharging::CalculateDuration ( float SkillLevel, long SkillRank )
{
	return SkillLevel * 90.0f;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long AShieldCharging::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case SUPERCHARGE_SHIELDS:
		return 1;
	case ULTRACHARGE_SHIELDS:
		return 3;
	case SUPERCHARGE_TARGET: 
		return 5;
	case ULTRACHARGE_TARGET:
		return 6;
	case MEGACHARGE_SHIELDS: 
		return 7;
	default:
		return -1;
	}
}

long AShieldCharging::DetermineMinimumLevel(int SkillID)
{
	switch(SkillID)
	{
	case SUPERCHARGE_SHIELDS:
		return 0;
	case ULTRACHARGE_SHIELDS:
		return 10;
	case SUPERCHARGE_TARGET: 
		return 20;
	case ULTRACHARGE_TARGET:
		return 30;
	case MEGACHARGE_SHIELDS: 
		return 40;
	default:
		return 51;
	}
}

bool AShieldCharging::SelfOnly()
{
	return m_SkillRank < 5;
}

// --------------------------------------------

bool AShieldCharging::CanUse(long TargetID, long AbilityID, long SkillID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx() ||
		!AbilityBase::CanUseWithCurrentTarget(true))
	{
		return false;
	}

	return true;
}

/*
* This will be the first function called once the skill is determined
* as useable.
*/
bool AShieldCharging::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	ObjectEffect EnvShieldEffect;
	memset(&EnvShieldEffect, 0, sizeof(EnvShieldEffect));		// Zero out memory
	
	// shield charging effect on player
	EnvShieldEffect.Bitmask = 3;
	EnvShieldEffect.TimeStamp = m_EffectID;
	EnvShieldEffect.EffectID = m_EffectID;
	EnvShieldEffect.EffectDescID = 719;
	EnvShieldEffect.GameID = p->GameID();
	p->SendObjectEffectRL(&EnvShieldEffect);
	
	return true;
}

/*
* This function is called when the SetTimer call returns.   
*/
bool AShieldCharging::Update(long activation_ID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	float rmag = 0.0f;
	float smag = 0.0f;
	float deflect = 0.0f;
	
	// remove the effects used during the delay
	p->RemoveEffectRL(m_EffectID); 

	if (m_Target != p)
	{
		ObjectToObjectEffect BeamEffect;
		memset(&BeamEffect, 0, sizeof(BeamEffect));		// Zero out memory

		// beam effect
		BeamEffect.Bitmask = 3;
		BeamEffect.GameID = p->GameID();
		BeamEffect.TimeStamp = m_EffectID+1;
		BeamEffect.EffectID = m_EffectID+1;
		BeamEffect.TargetID = m_Target->GameID();
		BeamEffect.EffectDescID = 668;
		p->SendObjectToObjectEffectRL(&BeamEffect);
	}

	long Duration = (long)(CalculateDuration(m_SkillLevel, m_SkillRank) * 1000.0f);
	int colour = -1;
	
	// Now lets figure out what buff to add
	Buff ShieldChargeBuff;
	memset(&ShieldChargeBuff, 0, sizeof(Buff));

	for(int i = 0; i < 5; i++)
	{
		ShieldChargeBuff.EffectID[i] = -1;
	}
		
	switch(m_AbilityID)
	{
	case SUPERCHARGE_SHIELDS:
		smag = (m_SkillLevel == 1)? 0.05f : 0.1f;
		rmag = 0.0f;
		deflect = 0.0f;
		colour = 300; // green
		break;
	case ULTRACHARGE_SHIELDS:
		smag = (m_SkillLevel == 3)? 0.1f : 0.15f;
		rmag = 0.1f;
		deflect = 0.0f;
		colour = 60; // blue
		break;
	case SUPERCHARGE_TARGET:
		smag = 0.2f;
		rmag = 0.0f;
		deflect = 0.0f;
		colour = 120; // purple
		break;
	case ULTRACHARGE_TARGET:
		smag = 0.2f;
		rmag = 0.1f;
		deflect = 0.0f;
		colour = 240; // yellow
		break;
	case MEGACHARGE_SHIELDS: 
		// Set stats for buff
		smag = 0.25f;
		rmag = 0.2f;
		deflect = 10.0f;
		colour = 210; // orange
		break;
	}
	ShieldChargeBuff.EffectID[0] = 216;
	strcpy_s(ShieldChargeBuff.Stats[0].StatName, sizeof(ShieldChargeBuff.Stats[0].StatName), STAT_SHIELD);
	ShieldChargeBuff.Stats[0].StatName[sizeof(ShieldChargeBuff.Stats[0].StatName)-1] = '\0';
	ShieldChargeBuff.Stats[0].Value = smag;
	ShieldChargeBuff.Stats[0].StatType = STAT_BUFF_MULT;

	if(rmag > 0.0f)
	{
		strcpy_s(ShieldChargeBuff.Stats[1].StatName, sizeof(ShieldChargeBuff.Stats[1].StatName), STAT_SHIELD_RECHARGE);
		ShieldChargeBuff.Stats[1].StatName[sizeof(ShieldChargeBuff.Stats[1].StatName)-1] = '\0';
		ShieldChargeBuff.Stats[1].Value = rmag;
		ShieldChargeBuff.Stats[1].StatType = STAT_BUFF_MULT;
	}
	if(deflect > 0.0f)
	{
		strcpy_s(ShieldChargeBuff.Stats[2].StatName, sizeof(ShieldChargeBuff.Stats[2].StatName), STAT_ENERGY_DEFLECT);
		ShieldChargeBuff.Stats[2].StatName[sizeof(ShieldChargeBuff.Stats[2].StatName)-1] ='\0';
		ShieldChargeBuff.Stats[2].Value = deflect;	
		ShieldChargeBuff.Stats[2].StatType = STAT_BUFF_VALUE;

		strcpy_s(ShieldChargeBuff.Stats[3].StatName, sizeof(ShieldChargeBuff.Stats[3].StatName), STAT_IMPACT_DEFLECT);
		ShieldChargeBuff.Stats[3].StatName[sizeof(ShieldChargeBuff.Stats[3].StatName)-1] ='\0';
		ShieldChargeBuff.Stats[3].Value = deflect;
		ShieldChargeBuff.Stats[3].StatType = STAT_BUFF_VALUE;

		strcpy_s(ShieldChargeBuff.Stats[4].StatName, sizeof(ShieldChargeBuff.Stats[4].StatName), STAT_EMP_DEFLECT);
		ShieldChargeBuff.Stats[4].StatName[sizeof(ShieldChargeBuff.Stats[4].StatName)-1] ='\0';
		ShieldChargeBuff.Stats[4].Value = deflect;
		ShieldChargeBuff.Stats[4].StatType = STAT_BUFF_VALUE;

		strcpy_s(ShieldChargeBuff.Stats[5].StatName, sizeof(ShieldChargeBuff.Stats[5].StatName), STAT_PLASMA_DEFLECT);
		ShieldChargeBuff.Stats[5].StatName[sizeof(ShieldChargeBuff.Stats[5].StatName)-1] ='\0';
		ShieldChargeBuff.Stats[5].Value = deflect;
		ShieldChargeBuff.Stats[5].StatType = STAT_BUFF_VALUE;

		strcpy_s(ShieldChargeBuff.Stats[6].StatName, sizeof(ShieldChargeBuff.Stats[6].StatName), STAT_CHEM_DEFLECT);
		ShieldChargeBuff.Stats[6].StatName[sizeof(ShieldChargeBuff.Stats[6].StatName)-1] ='\0';
		ShieldChargeBuff.Stats[6].Value = deflect;
		ShieldChargeBuff.Stats[6].StatType = STAT_BUFF_VALUE;

		strcpy_s(ShieldChargeBuff.Stats[7].StatName, sizeof(ShieldChargeBuff.Stats[7].StatName), STAT_EXPLOSIVE_DEFLECT);
		ShieldChargeBuff.Stats[7].StatName[sizeof(ShieldChargeBuff.Stats[7].StatName)-1] ='\0';
		ShieldChargeBuff.Stats[7].Value = deflect;
		ShieldChargeBuff.Stats[7].StatType = STAT_BUFF_VALUE;
	}

	ShieldChargeBuff.ExpireTime = GetNet7TickCount() + Duration;
	ShieldChargeBuff.IsPermanent = false;
	strcpy_s(ShieldChargeBuff.BuffType, sizeof(ShieldChargeBuff.BuffType), "Trader_Shield");
	ShieldChargeBuff.BuffType[sizeof(ShieldChargeBuff.BuffType)-1] = '\0';
	
	if(m_Target->m_Buffs.FindBuff("Trader_Shield"))
	{
		m_Target->m_Buffs.RemoveBuff("Trader_Shield");
	}
	m_Target->m_Buffs.AddBuff(&ShieldChargeBuff,colour);
	
	// all done
	p->SetCurrentSkill();
	m_InUse = false;

	return true;
}

/*
* Returns true in the case that this skill can be interrupted.
* What can interrupt the skill is returned inside the OnMotion 
*  and OnDamage pointers.
*/
bool AShieldCharging::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = true;
	*OnAction = true;

	return true;
}
