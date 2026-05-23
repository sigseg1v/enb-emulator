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

#include "AbilityRepulsorField.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*
Skill properties:
Description=Repulsor Field abilities generate feedback that returns energy damage to the original source of incoming damage. At higher levels the damaged returned increases.  The affected damage types for all levels are: Chemical, EMP, Energy, Explosive, Impact, Plasma
Desc_1=Generates feedback damage when skill user is struck by affected damage types. Ability may only be applied to skill user.
Desc_2=Increases maximum feedback amount and duration.
Desc_3=Generates feedback damage when skill user is struck by affected damage types, increases feedback amount, maximum feedback amount, and duration. Ability may be applied to skill user or other characters.
Desc_4=Increases maximum feedback amount and duration.
Desc_5=Generates feedback damage when skill user is struck by affected damage types, increases feedback amount, maximum feedback amount, and duration. Ability may be applied to skill user or other characters.
Desc_6=Generates feedback damage when skill user is struck by affected damage types, increases maximum feedback amount, and duration. Ability may be applied to skill user or other characters.
Desc_7=Generates feedback damage when skill user is struck by affected damage types, increases maximum feedback amount, and duration.. Ability may be applied to skill user or other characters.
*/

/*
* This calculates the activation cost of the skill.
*/
float ARepulsorField::CalculateEnergy ( float SkillLevel, long SkillRank )
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
float ARepulsorField::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
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
float ARepulsorField::CalculateCoolDownTime ( float SkillLevel, long SkillRank ) 
{
	return 5.0f; // TODO: find out correct answer
}

/*
* Calculate the maximum range this rank of the skill can be used at.
*/
float ARepulsorField::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	return 2500.0f + (SkillLevel-1) * 250.0f;
}

float ARepulsorField::CalculateDamageReflect ( float SkillLevel, long SkillRank )
{
	switch(SkillRank)
	{
	case 1:
	case 2:
		return 0.2f;
	case 3:
	case 4:
		return 0.4f;
	case 5:
	case 6:
	case 7:
		return 0.6f;
		return 0.6f;
	default:
		return 0.0f;
	}
}

/*
	
*/

float ARepulsorField::CalculateDamageReflectCap ( float SkillLevel, long SkillRank)
{
	switch (SkillRank)
	{
	case 1:
		return SkillLevel * 15.0f;
	case 3:
		return SkillLevel * 30.0f;
	case 5:
		return SkillLevel * 60.0f;
	case 6:
		return SkillLevel * 120.0f;
	case 7:
		return SkillLevel * 240.0f;
	}
	return 0.0f; // shouldnt happen
}

/*
* Computes the duration of the ability.
*
* Results are returned in seconds.
*/
float ARepulsorField::CalculateDuration ( float SkillLevel, long SkillRank )
{
	return SkillLevel * 90.0f;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long ARepulsorField::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case MINOR_REPULSOR_FIELD:
		return 1;
	case LESSER_REPULSOR_FIELD:
		return 3;
	case REPULSOR_FIELD: 
		return 5;
	case GREATER_REPULSOR_FIELD:
		return 6;
	case MAJOR_REPULSOR_FIELD: 
		return 7;
	default:
		return -1;
	}
}

long ARepulsorField::DetermineMinimumLevel(int SkillID)
{
	switch(SkillID)
	{
	case MINOR_REPULSOR_FIELD:
		return 0;
	case LESSER_REPULSOR_FIELD:
		return 10;
	case REPULSOR_FIELD: 
		return 20;
	case GREATER_REPULSOR_FIELD:
		return 30;
	case MAJOR_REPULSOR_FIELD: 
		return 40;
	default:
		return 51;
	}
}

bool ARepulsorField::SelfOnly()
{ 
	return m_SkillRank < 3;
}

// --------------------------------------------

bool ARepulsorField::CanUse(long TargetID, long AbilityID, long SkillID)
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
	DamageShield *ds = m_Target->FindDamageShield(REPULSOR_FIELD);
	if( ds != NULL) 
	{
		if(ds->shield_level > m_SkillRank)
		{
			SendError("Target already has a better repulsor field!");
			return false;
		}		
	}
	
	return true;
}

/*
* This will be the first function called once the skill is determined
* as useable.
*/
bool ARepulsorField::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	ObjectEffect RepulsorFieldEffect;
	memset(&RepulsorFieldEffect, 0, sizeof(RepulsorFieldEffect));		// Zero out memory
	
	// psi shield effect on player
	RepulsorFieldEffect.Bitmask = 3;
	RepulsorFieldEffect.TimeStamp = m_EffectID;
	RepulsorFieldEffect.EffectID = m_EffectID;
	RepulsorFieldEffect.EffectDescID = 727;
	RepulsorFieldEffect.GameID = p->GameID();
	p->SendObjectEffectRL(&RepulsorFieldEffect);
	
	return true;
}

/*
* This function is called when the SetTimer call returns.   
*/
bool ARepulsorField::Update(long activation_ID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	// remove the effects used during the delay
	p->RemoveEffectRL(m_EffectID); 

	// check for shield cast by someone else during charge up
	DamageShield *ds = m_Target->FindDamageShield(REPULSOR_FIELD);
	if( ds != NULL)
	{
		if(ds->shield_level > m_SkillRank)
		{
			SendError("Target already has a better repulsor field!");
			p->SetCurrentSkill();
			m_InUse = false;
			return false;
		}
		else
		{
			SendError("Target has same level repulsor field.");
		}
	}

	ObjectEffect RepulsorFieldEffect;
	memset(&RepulsorFieldEffect, 0, sizeof(RepulsorFieldEffect));		// Zero out memory
	RepulsorFieldEffect.Bitmask = 3;
	
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
		RepulsorFieldEffect.EffectDescID = 530;
		RepulsorFieldEffect.TimeStamp = m_EffectID+2;
		RepulsorFieldEffect.EffectID = m_EffectID+2;
		RepulsorFieldEffect.GameID = m_Target->GameID();
		p->SendObjectEffectRL(&RepulsorFieldEffect);
	}

	// shield finish on player
	RepulsorFieldEffect.EffectDescID = 668;
	RepulsorFieldEffect.TimeStamp = m_EffectID+3;
	RepulsorFieldEffect.EffectID = m_EffectID+3;
	RepulsorFieldEffect.GameID = p->GameID();
	p->SendObjectEffectRL(&RepulsorFieldEffect);

	long Duration = (long)(CalculateDuration(m_SkillLevel, m_SkillRank) * 1000.0f);
	int colour = -1;

	// Now lets figure out what buff to add
	Buff ShieldBuff;
	memset(&ShieldBuff, 0, sizeof(Buff));
	for(int i = 0; i < 5; i++)
	{
		ShieldBuff.EffectID[i] = -1;
	}
	ShieldBuff.ExpireTime = GetNet7TickCount() + Duration;
	ShieldBuff.IsPermanent = false;
	ShieldBuff.AbsorbID = REPULSOR_FIELD;
	ShieldBuff.EffectID[0] = 216;
	strcpy_s(ShieldBuff.BuffType, sizeof(ShieldBuff.BuffType), "Sentinel_Shield");
	ShieldBuff.BuffType[sizeof(ShieldBuff.BuffType)-1] = '\0';
	
	switch(m_AbilityID)
	{
	case MINOR_REPULSOR_FIELD:
		colour = 300; // green
		break;
	case LESSER_REPULSOR_FIELD:
		colour = 60; // blue
		break;
	case REPULSOR_FIELD: 
		colour = 120; // purple
		break;
	case GREATER_REPULSOR_FIELD:
		colour = 240; // yellow
		break;
	case MAJOR_REPULSOR_FIELD: 
		colour = 210; // orange
		break;
	}
	
	ShieldBuff.AbsorbID = m_Target->m_Buffs.AddBuff(&ShieldBuff,colour);
	m_Target->RemoveDamageShield(REPULSOR_FIELD);
	m_Target->AddDamageShield(REPULSOR_FIELD,
							 (short)m_SkillRank,
							 ShieldBuff.AbsorbID,
							 0,
							 CalculateDamageReflect(m_SkillLevel,m_SkillRank),
							 CalculateDamageReflectCap(m_SkillLevel,m_SkillRank),
							 0,-1,false);

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
bool ARepulsorField::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = false;
	*OnAction = true;

	return true;
}
