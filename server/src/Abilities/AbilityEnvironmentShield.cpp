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

#include "AbilityEnvironmentShield.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*
Skill properties:
Description=Psionic Shield abilities create a temporary shield around any ship that absorbs damage and improves resistance to special attacks.
Desc_1=Enables the Psionic Barrier ability.
Desc_2=Psionic Barriers can absorb 60 damage and range is increased to 2750 units.
Desc_3=Enables the Lesser Psionic Shield ability and increases range to 3000 units.
Desc_4=Increases range to 3250 units and increases the protection provided by abilities.
Desc_5=Enables the Psionic Shield ability and increases range to 3500 units.
Desc_6=Enables the Greater Psionic Shield ability and increases range to 3750 units.
Desc_7=Enables the Psionic Invulnerability ability and increases range to 4000 units.
*/

/*
* This calculates the activation cost of the skill.
*/
float AEnvironmentShield::CalculateEnergy ( float SkillLevel, long SkillRank )
{
	switch (SkillRank)
	{
	case 1:
		return 35.0f;
	case 3:
		return 100.0f;
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
float AEnvironmentShield::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	//shorter casting time for lower rank with high skill.
	// 5 second default minus the difference between the skill rank and skill level (a negative to 0 value)
	// Capped at a .5 second minimum cast time.
	return 5.0f + ( ((float)SkillRank - SkillLevel) <= -5.0f ? -4.5f : ((float)SkillRank - SkillLevel) );
}

/*
* Calculate the maximum range this rank of the skill can be used at.
*/
float AEnvironmentShield::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	return 2500.0f + (SkillLevel-1) * 250.0f;
}

/*
* Computes the duration of the ability.
*
* Results are returned in seconds.
*/
float AEnvironmentShield::CalculateDuration ( float SkillLevel, long SkillRank )
{
	return SkillLevel * 90.0f;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long AEnvironmentShield::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case ENVIRONMENTAL_BARRIER:
		return 1;
	case LESSER_ENVIRONMENTAL_SHIELD:
		return 3;
	case ENVIRONMENTAL_SHIELD: 
		return 5;
	case GREATER_ENVIRONMENTAL_SHIELD:
		return 6;
	case ULTRA_ENVIRONMENTAL_SHIELD: 
		return 7;
	default:
		return -1;
	}
}

long AEnvironmentShield::DetermineMinimumLevel(int SkillID)
{
	switch(SkillID)
	{
	case ENVIRONMENTAL_BARRIER:
		return 0;
	case LESSER_ENVIRONMENTAL_SHIELD:
		return 10;
	case ENVIRONMENTAL_SHIELD: 
		return 20;
	case GREATER_ENVIRONMENTAL_SHIELD:
		return 30;
	case ULTRA_ENVIRONMENTAL_SHIELD: 
		return 40;
	default:
		return 51;
	}
}

bool AEnvironmentShield::CanTargetBeShielded()
{
	DamageShield * shield = m_Target->FindDamageShield(ENVIRONMENTAL_BARRIER);

	if (shield && shield->shield_level > m_SkillRank)
		return false;

	return true;
}

// --------------------------------------------

bool AEnvironmentShield::CanUse(long TargetID, long AbilityID, long SkillID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx() ||
		!AbilityBase::CanUseWithCurrentTarget(true))
	{
		return false;
	}

	// check level of target player
	if (m_Target != p && m_Target->CombatLevel() < DetermineMinimumLevel(m_AbilityID))
	{
		SendError("Player is too low level for this shield!");
		return false;
	}
	
	// check for existing shield 
	if (!CanTargetBeShielded())
	{
		SendError("Target already has a better damage shield!");
		return false;
	}

	return true;
}

/*
* This will be the first function called once the skill is determined
* as useable.
*/
bool AEnvironmentShield::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	ObjectEffect EnvShieldEffect;
	memset(&EnvShieldEffect, 0, sizeof(EnvShieldEffect));		// Zero out memory
	
	// psi shield effect on player
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
bool AEnvironmentShield::Update(long activation_ID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	// remove the effects used during the delay
	p->RemoveEffectRL(m_EffectID); 

	// check for shield cast by someone else during charge up
	if (!CanTargetBeShielded())
	{
		SendError("Target already has a better environment shield!");
		p->SetCurrentSkill();
		m_InUse = false;
		return false;
	}

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
	float deflect = m_SkillRank > 1 ? 10.0f : 5.0f;
	float max_reflect = 0.0f;
	float to_reactor = 0.0f;

	// Now lets figure out what buff to add
	Buff EnviroBuff;
	memset(&EnviroBuff, 0, sizeof(Buff));
	EnviroBuff.ExpireTime = GetNet7TickCount() + Duration;
	EnviroBuff.IsPermanent = false;
	for(int i = 0; i < 5; i++)
	{
		EnviroBuff.EffectID[i] = -1;
	}
	EnviroBuff.EffectID[0] = 216;
	
	// Set stats for buff
	strcpy_s(EnviroBuff.Stats[0].StatName, sizeof(EnviroBuff.Stats[0].StatName), STAT_ENERGY_DEFLECT);
	EnviroBuff.Stats[0].StatName[sizeof(EnviroBuff.Stats[0].StatName)-1] = '\0';
	EnviroBuff.Stats[0].Value = deflect;	
	EnviroBuff.Stats[0].StatType = STAT_BUFF_VALUE;
	strcpy_s(EnviroBuff.Stats[1].StatName, sizeof(EnviroBuff.Stats[1].StatName), STAT_IMPACT_DEFLECT);
	EnviroBuff.Stats[1].StatName[sizeof(EnviroBuff.Stats[1].StatName)-1] = '\0';
	EnviroBuff.Stats[1].Value = deflect;	
	EnviroBuff.Stats[1].StatType = STAT_BUFF_VALUE;
	strcpy_s(EnviroBuff.Stats[2].StatName, sizeof(EnviroBuff.Stats[2].StatName), STAT_EMP_DEFLECT);
	EnviroBuff.Stats[2].StatName[sizeof(EnviroBuff.Stats[2].StatName)-1] = '\0';
	EnviroBuff.Stats[2].Value = deflect;	
	EnviroBuff.Stats[2].StatType = STAT_BUFF_VALUE;
	strcpy_s(EnviroBuff.Stats[3].StatName, sizeof(EnviroBuff.Stats[3].StatName), STAT_PLASMA_DEFLECT);
	EnviroBuff.Stats[3].StatName[sizeof(EnviroBuff.Stats[3].StatName)-1] = '\0';
	EnviroBuff.Stats[3].Value = deflect;	
	EnviroBuff.Stats[3].StatType = STAT_BUFF_VALUE;
	strcpy_s(EnviroBuff.Stats[4].StatName, sizeof(EnviroBuff.Stats[4].StatName), STAT_CHEM_DEFLECT);
	EnviroBuff.Stats[4].StatName[sizeof(EnviroBuff.Stats[4].StatName)-1] = '\0';
	EnviroBuff.Stats[4].Value = deflect;	
	EnviroBuff.Stats[4].StatType = STAT_BUFF_VALUE;
	strcpy_s(EnviroBuff.Stats[5].StatName, sizeof(EnviroBuff.Stats[5].StatName), STAT_EXPLOSIVE_DEFLECT);
	EnviroBuff.Stats[5].StatName[sizeof(EnviroBuff.Stats[5].StatName)-1] = '\0';
	EnviroBuff.Stats[5].Value = deflect;	
	EnviroBuff.Stats[5].StatType = STAT_BUFF_VALUE;
	switch(m_AbilityID)
	{
	case ENVIRONMENTAL_BARRIER:
		strcpy_s(EnviroBuff.BuffType, sizeof(EnviroBuff.BuffType), "Environment_Shield");
		EnviroBuff.BuffType[sizeof(EnviroBuff.BuffType)-1] = '\0';
		colour = 300; // green
		break;
	case LESSER_ENVIRONMENTAL_SHIELD:
		strcpy_s(EnviroBuff.BuffType, sizeof(EnviroBuff.BuffType), "Environment_Shield");
		EnviroBuff.BuffType[sizeof(EnviroBuff.BuffType)-1] = '\0';
		colour = 60; // blue
		break;
	case ENVIRONMENTAL_SHIELD: 
		strcpy_s(EnviroBuff.BuffType, sizeof(EnviroBuff.BuffType), "Environment_Shield");
		EnviroBuff.BuffType[sizeof(EnviroBuff.BuffType)-1] = '\0';
		colour = 120; // purple
		max_reflect = 10.0f;
		break;
	case GREATER_ENVIRONMENTAL_SHIELD:
		strcpy_s(EnviroBuff.BuffType, sizeof(EnviroBuff.BuffType), "Environment_Shield");
		EnviroBuff.BuffType[sizeof(EnviroBuff.BuffType)-1] = '\0';
		colour = 240; // yellow
		max_reflect = 25.0f;
		to_reactor = 0.05f;
		break;
	case ULTRA_ENVIRONMENTAL_SHIELD: 
		strcpy_s(EnviroBuff.BuffType, sizeof(EnviroBuff.BuffType), "Environment_Shield");
		EnviroBuff.BuffType[sizeof(EnviroBuff.BuffType)-1] = '\0';
		colour = 210; // orange
		max_reflect = 50.0f;
		to_reactor = 0.05f;
		break;
	}
	EnviroBuff.AbsorbID = m_Target->m_Buffs.AddBuff(&EnviroBuff,colour);

	// add the damage shield
	m_Target->RemoveDamageShield(ENVIRONMENTAL_BARRIER);
	m_Target->AddDamageShield(ENVIRONMENTAL_BARRIER,(short)m_SkillRank,EnviroBuff.AbsorbID,0,max_reflect > 0.0f ? 0.1f : 0.0f,max_reflect,to_reactor,10.0f,m_AbilityID==ULTRA_ENVIRONMENTAL_SHIELD);
	// Drop all Radiation Damage
	p->RadiationDmg(false);

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
bool AEnvironmentShield::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = true;
	*OnAction = true;

	return true;
}
