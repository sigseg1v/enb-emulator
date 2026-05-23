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

#include "AbilityPsionicShield.h"
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
float APsionicShield::CalculateEnergy ( float SkillLevel, long SkillRank )
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
float APsionicShield::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	//shorter casting time for lower rank with high skill.
	// 5 second default minus the difference between the skill rank and skill level (a negative to 0 value)
	// Capped at a .5 second minimum cast time.
	return 5.0f + ( ((float)SkillRank - SkillLevel) <= -5.0f ? -4.5f : ((float)SkillRank - SkillLevel) );
}

/*
* Compute how much time must pass between skill uses.
*
* Results are returned in seconds.
*/
float APsionicShield::CalculateCoolDownTime ( float SkillLevel, long SkillRank ) 
{
	return 5.0f; // TODO: find out correct answer
}

/*
* Calculate the maximum range this rank of the skill can be used at.
*/
float APsionicShield::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	return 2500.0f + (SkillLevel-1) * 250.0f;
}

float APsionicShield::CalculateDamageAbsorb ( float SkillLevel, long SkillRank )
{
	switch (SkillRank)
	{
	case 1:
		return SkillLevel * 30.0f;
	case 3:
		return SkillLevel * 90.0f;
	case 5:
		return SkillLevel * 360.0f;
	case 6:
		return SkillLevel * 720.0f;
	case 7:
		return SkillLevel * 1440.0f;
	}
	return 0.0f; // shouldnt happen
}

/*
* Computes the duration of the ability.
*
* Results are returned in seconds.
*/
float APsionicShield::CalculateDuration ( float SkillLevel, long SkillRank )
{
	return SkillLevel * 90.0f;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long APsionicShield::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case PSIONIC_BARRIER:
		return 1;
	case LESSER_PSIONIC_SHIELD:
		return 3;
	case PSIONIC_SHIELD: 
		return 5;
	case GREATER_PSIONIC_SHIELD:
		return 6;
	case PSIONIC_INVULNERABILITY: 
		return 7;
	default:
		return -1;
	}
}

long APsionicShield::DetermineMinimumLevel(int SkillID)
{
	switch(SkillID)
	{
	case PSIONIC_BARRIER:
		return 0;
	case LESSER_PSIONIC_SHIELD:
		return 10;
	case PSIONIC_SHIELD: 
		return 20;
	case GREATER_PSIONIC_SHIELD:
		return 30;
	case PSIONIC_INVULNERABILITY: 
		return 40;
	default:
		return 51;
	}
}

long APsionicShield::GetLevelBasedRank(int combatLevel)
{
	if (combatLevel <=10)
		return 1;
	if (combatLevel <=20)
		return 3;
	if (combatLevel <=30)
		return 5;
	if (combatLevel <=40)
		return 6;
	if (combatLevel <=50)
		return 7;
	return 7;
}

// --------------------------------------------

bool APsionicShield::CanUse(long TargetID, long AbilityID, long SkillID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx(false) ||
		!AbilityBase::CanUseWithCurrentTarget(true))
	{
		return false;
	}

	// check level of target player
	if (m_Target != p && m_Target->CombatLevel() < DetermineMinimumLevel(m_AbilityID))
	{		
		m_SkillRank = GetLevelBasedRank(m_Target->CombatLevel());
		return true;
	}
	
	// check for existing shield 
	DamageShield *ds = m_Target->FindDamageShield(PSIONIC_SHIELD);
	if (ds != NULL && ds->capacitance > CalculateDamageAbsorb ( m_SkillLevel, m_SkillRank ))
	{
		SendError("Target already has a higher capacitance psionic shield.");
		return false;
	}

	return true;
}

/*
* This will be the first function called once the skill is determined
* as useable.
*/
bool APsionicShield::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	ObjectEffect PsionicShieldEffect;
	memset(&PsionicShieldEffect, 0, sizeof(PsionicShieldEffect));		// Zero out memory
	
	// psi shield effect on player
	PsionicShieldEffect.Bitmask = 3;
	PsionicShieldEffect.TimeStamp = m_EffectID;
	PsionicShieldEffect.EffectID = m_EffectID;
	PsionicShieldEffect.EffectDescID = 727;
	PsionicShieldEffect.GameID = p->GameID();
	p->SendObjectEffectRL(&PsionicShieldEffect);
	
	return true;
}

/*
* This function is called when the SetTimer call returns.   
*/
bool APsionicShield::Update(long activation_ID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	// remove the effects used during the delay
	p->RemoveEffectRL(m_EffectID); 

	// check for shield cast by someone else during charge up
	DamageShield *ds = m_Target->FindDamageShield(PSIONIC_SHIELD);
	if (ds && ds->capacitance > CalculateDamageAbsorb ( m_SkillLevel, m_SkillRank ))
	{
		SendError("Target already has a higher capacitance psionic shield.");
		p->SetCurrentSkill();
		m_InUse = false;
		return false;
	}

	ObjectEffect PsionicShieldEffect;
	memset(&PsionicShieldEffect, 0, sizeof(PsionicShieldEffect));		// Zero out memory
	PsionicShieldEffect.Bitmask = 3;
	
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
		BeamEffect.EffectDescID = 664;
		p->SendObjectToObjectEffectRL(&BeamEffect);

		// beam hit on target
		PsionicShieldEffect.EffectDescID = 530;
		PsionicShieldEffect.TimeStamp = m_EffectID+2;
		PsionicShieldEffect.EffectID = m_EffectID+2;
		PsionicShieldEffect.GameID = m_Target->GameID();
		p->SendObjectEffectRL(&PsionicShieldEffect);
	}

	// psi shield finish on player
	PsionicShieldEffect.EffectDescID = 507;
	PsionicShieldEffect.TimeStamp = m_EffectID+3;
	PsionicShieldEffect.EffectID = m_EffectID+3;
	PsionicShieldEffect.GameID = p->GameID();
	p->SendObjectEffectRL(&PsionicShieldEffect);

	long Duration = (long)(CalculateDuration(m_SkillLevel, m_SkillRank) * 1000.0f);
	int colour = -1;

	// Now lets figure out what buff to add
	Buff ShieldBuff;
	memset(&ShieldBuff, 0, sizeof(Buff));
	ShieldBuff.ExpireTime = GetNet7TickCount() + Duration;
	ShieldBuff.AbsorbID = PSIONIC_SHIELD;
	ShieldBuff.IsPermanent = false;
	
	for(int i = 0; i < 5; i++)
	{
		ShieldBuff.EffectID[i] = -1;
	}
	ShieldBuff.EffectID[0] = 214;

	// Set stats for buff
	strcpy_s(ShieldBuff.Stats[0].StatName, sizeof(ShieldBuff.Stats[0].StatName), STAT_PSIONIC_DEFLECT);
	ShieldBuff.Stats[0].StatName[sizeof(ShieldBuff.Stats[0].StatName)-1] = '\0';
	ShieldBuff.Stats[0].Value = 30.0f;	
	ShieldBuff.Stats[0].StatType = STAT_BUFF_VALUE;
	switch(m_AbilityID)
	{
	case PSIONIC_BARRIER:
		strcpy_s(ShieldBuff.BuffType, sizeof(ShieldBuff.BuffType), "Psionic_Barrier");
		ShieldBuff.BuffType[sizeof(ShieldBuff.BuffType)-1] = '\0';
		colour = 300; // green
		break;
	case LESSER_PSIONIC_SHIELD:
		strcpy_s(ShieldBuff.BuffType, sizeof(ShieldBuff.BuffType), "Lesser_Psionic_Shield");
		ShieldBuff.BuffType[sizeof(ShieldBuff.BuffType)-1] = '\0';
		colour = 60; // blue
		break;
	case PSIONIC_SHIELD: 
		strcpy_s(ShieldBuff.BuffType, sizeof(ShieldBuff.BuffType), "Psionic_Shield");
		ShieldBuff.BuffType[sizeof(ShieldBuff.BuffType)-1] = '\0';
		colour = 120; // purple
		break;
	case GREATER_PSIONIC_SHIELD:
		strcpy_s(ShieldBuff.BuffType, sizeof(ShieldBuff.BuffType), "Greater_Psionic_Shield");
		ShieldBuff.BuffType[sizeof(ShieldBuff.BuffType)-1] = '\0';
		colour = 240; // yellow
		break;
	case PSIONIC_INVULNERABILITY: 
		strcpy_s(ShieldBuff.BuffType, sizeof(ShieldBuff.BuffType), "Psionic_Invulnerability");
		ShieldBuff.BuffType[sizeof(ShieldBuff.BuffType)-1] = '\0';
		colour = 210; // orange
		break;
	}
	
	m_Target->AddDamageShield(PSIONIC_SHIELD,(short)m_SkillLevel,m_Target->m_Buffs.AddBuff(&ShieldBuff,colour),
		CalculateDamageAbsorb ( m_SkillLevel, m_SkillRank ),0.0f,-1.0f,0.0f,-1.0f,false);

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
bool APsionicShield::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = true;
	*OnAction = true;

	return true;
}
