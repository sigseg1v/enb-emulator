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

#include "AbilityShieldLeech.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*

The skill drains shield from a single target or an area of targets and converts it into pure energy.
This energy is then transfered to self and group members.
Skill is interrupted by warp.

This skill has a cooldown of 15 sec.

Skill properties:

SkillRank 1 : lvl 1 : Range 1k.    EnergyCost 35.  Drains 15% of target shields if lower than 50% of own shields. 
								   Convert into 75% energy. Only self.
			  lvl 2 : Range 1.25k. EnergyCost 35.  Drains 18% of target shields if lower than 50% of own shields.
								   Convert into 75% energy. Only self.
SkillRank 3 : lvl 3 : Range 1.5k.  EnergyCost 75.  Drains 21% of target shields if lower than 50% of own shields.
								   Convert into 85% energy. Only self.
			  lvl 4 : Range	1.75k. EnergyCost 75.  Drains 24% of target shields if lower than 50% of own shields.
								   Convert into 85% energy. Only self.
SkillRank 5 : lvl 5 : Range 2k.    EnergyCost 150. Drains 27% of target shields if lower than 50% of own shields.
								   Convert into 100% energy. Splits energy in Group.
SkillRank 6 : lvl 6 : Range 1.5k.  EnergyCost 300. Drains 30% of target shields if lower than 50% of own shields.
								   Convert into 110% energy. Only Self.
SkillRank 7 : lvl 7 : Range 3k.    EnergyCost 600. Drains 33% of target shields if lower than 50% of own shields.
								   Convert into 125% energy. Splits energy in Group.
*/

/*
	This function calculates how much energy comes out from a given 
	number of shield-points.
	Returns the converted energy.
*/
float AShieldLeech::ShieldToEnergy( float SkillLevel, int SkillRank, float shield )
{
	// Sanity check
	if (shield == 0.0f || SkillRank < 1 || SkillRank > 7) 
		return 0.0f;

	float percent;

	if( SkillRank == 1 )
	{
		if( SkillLevel == 1 )
			percent = 0.75f;
		else
			percent = 0.75f;
	}
	else if( SkillRank == 3 )
	{
		if( SkillLevel == 3 )
			percent = 0.85f;
		else
			percent = 0.85f;
	}
	else if( SkillRank == 5 )
	{
		percent = 1.0f;
	}
	else if (SkillRank == 6 )
	{
		percent = 1.1f;
	}
	else
	{
		percent = 1.25f;
	}

	return shield * percent;
}
/*
	This function calculates how much shield to leech from a target. This amount can't be
	greater than 50% of users own shield amount.
	After this the shield amount is leeched from target and the shield amount is returned.
*/
float AShieldLeech::DrainShieldFromTarget ( float SkillLevel, int SkillRank, CMob* target )
{
	CMob *p = GetPointerToCommon();
	float percent,drain_amount;

	if( SkillRank == 1 )
	{
		if( SkillLevel == 1 )
			percent = 0.15f;
		else
			percent = 0.18f;
	}
	else if( SkillRank == 3 )
	{
		if( SkillLevel == 3 )
			percent = 0.21f;
		else
			percent = 0.24f;
	}
	else if( SkillRank == 5 )
	{
		percent = 0.27f;
	}
	else if (SkillRank == 6 )
	{
		percent = 0.3f;
	}
	else
	{
		percent = 0.33f;
	}
	
	drain_amount = 0.0f;
	
	// Get how much we can possible drain from target object
	drain_amount = target->GetMaxShield() * percent;
	// Calculate how much to drain from target object.
	if ( drain_amount > (p->GetMaxShield() / 2.0f))
	{
		drain_amount = p->GetMaxShield() / 2.0f;
	}

	// See if target has the wanted shield. If not set it to targets current shield value.
	if (target->GetShieldLevel() - drain_amount < 0)
	{
		drain_amount = target->GetShieldLevel();
	}

	// Drain shield from target
	target->DamageObject(p->GameID(),DAMAGE_ENERGY, drain_amount, 0);

	return drain_amount;
}

/*
* This calculates the activation cost of the skill.
*/
float AShieldLeech::CalculateEnergy ( float SkillLevel, long SkillRank )
{
		switch ( SkillRank )
	{
		case 1:
			return 35.0f;
		case 3:
			return 75.0f;
		case 5:
			return 150.0f;
		case 6:
			return 300.0f;
		default:
			return 600.0f;
	}
}

/*
* Calculate how much time must pass before the skill activates.
*
* Results are returned in seconds.
*/
float AShieldLeech::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	return SkillLevel < 6.0f ? 8.0f - SkillLevel : 3.0f;
}

/*
* Compute how much time must pass between skill uses.
*
* Results are returned in seconds.
*/
float AShieldLeech::CalculateCoolDownTime ( float SkillLevel, long SkillRank ) 
{
	return 15.0f;
}

/*
* Calculate the maximum range this rank of the skill can be used at.
  Used for current targets on not group range.
*/
float AShieldLeech::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	float range = 0.0f;

	if( SkillRank == 1 )
	{
		if( SkillLevel == 1 )
			range = 1000.0f;
		else
			range = 1250.0f;
	}
	else if( SkillRank == 3 )
	{
		if( SkillLevel == 3 )
			range = 1500.0f;
		else
			range = 1750.0f;
	}
	else if( SkillRank == 5 )
	{
		range = 2000.0f;
	}
	else if (SkillRank == 6 )
	{
		range = 1500.0f;
	}
	else
	{
		range = 3000.0f;
	}

	return range;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long AShieldLeech::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case SHIELD_DRAIN:
		return 1;
	case SHIELD_LEECH:
		return 3;
	case GROUP_LEECH:
		return 5;
	case SHIELD_LEECHING_SPHERE:
		return 6;
	case GROUP_LEECHING_SPHERE:
		return 7;
	default:
		return -1;
	}
}

bool AShieldLeech::CanUse(long TargetID, long AbilityID, long SkillID)
{
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx() ||
		!AbilityBase::CanUseWithCurrentTarget())
	{
		return false;
	}

	return true;	
}

/*
* This will be the first function called once the skill is determined
* as useable.
*/
bool AShieldLeech::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	ObjectEffect ShieldLeechEffect;
	memset(&ShieldLeechEffect, 0, sizeof(ShieldLeechEffect));		// Zero out memory

	ShieldLeechEffect.Bitmask = 3;
	ShieldLeechEffect.GameID = p->GameID();
	ShieldLeechEffect.TimeStamp = m_EffectID;
	ShieldLeechEffect.EffectID = m_EffectID;
	ShieldLeechEffect.Duration = (short)ChargeTime;
	ShieldLeechEffect.EffectDescID = 1031;
	p->SendObjectEffectRL(&ShieldLeechEffect);

	return true;
}

/*
* This function is called when the SetTimer call returns.
*/
bool AShieldLeech::Update(long activation_ID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	/* Remove effect */
	p->RemoveEffectRL(m_EffectID);

	// Get tick count
	m_EffectID = GetNet7TickCount();

	m_drained_shield = 0.0f;

	if ( m_SkillRank  < 6 )
	{
		// We use shield leech on a target 
		//beam to target ship
		ObjectToObjectEffect LeechBeamEffect;
		memset(&LeechBeamEffect, 0, sizeof(LeechBeamEffect));		// Zero out memory

		LeechBeamEffect.Bitmask = 3;
		LeechBeamEffect.GameID = p->GameID();
		LeechBeamEffect.TimeStamp = m_EffectID;
		LeechBeamEffect.EffectID = m_EffectID;
		LeechBeamEffect.Duration = 1000;
		LeechBeamEffect.EffectDescID = 139;
		LeechBeamEffect.TargetID = m_Target->GameID();
		p->SendObjectToObjectEffectRL(&LeechBeamEffect);
		SetObjectEffectTimer(m_EffectID, 1000);

		m_drained_shield = DrainShieldFromTarget( m_SkillLevel, m_SkillRank, m_Target);
	}
	else
	{
		// Area shield leech
		UseOnAllEnemiesInRange();
	}

	// If no drained shield..Exit
	if ( m_drained_shield == 0.0f )
	{
		SendError("Found no shield to leech");
		p->SetCurrentSkill();
		m_InUse = false;
		return false;
	}

	float energy = 0.0f;
	// Convert shield into energy. 
	// Note: This was causing a caught exception when ShieldToEnergy 
	// called with drain_shield at 0.. maybe lag related bug.
	if (m_drained_shield > 0.0f)
	{
		energy = ShieldToEnergy( m_SkillLevel, m_SkillRank, m_drained_shield);
		p->SendVaMessage("Drained %d shield from target(s), converted to %d energy",(int)m_drained_shield,(int)energy);
	}

	// Get group id
	int GroupID = p->GroupID();

	if ( ( m_SkillRank == 5 || m_SkillRank == 7 ) && GroupID >= 0)
	{
		// Transfer energy to group
		int group_members = GetFriendlyGroup();

		// Split energy between group members
		float energy_part = energy / group_members;
		
		p->SendVaMessage("All group members will get %d energy",(int)energy_part);

		// Transfer energy to group
		for(int i=0;i < 6;i++)
		{
			if(m_AOEFriendList[i] != NULL)
			{
				// Transfer energy to group member
				m_AOEFriendList[i]->RemoveEnergy(-energy_part);

				ObjectToObjectEffect RechargeEffect;
				memset(&RechargeEffect, 0, sizeof(RechargeEffect));		// Zero out memory
				
				RechargeEffect.Bitmask = 3;
				RechargeEffect.GameID = p->GameID();
				RechargeEffect.TimeStamp = m_EffectID+i+1;
				RechargeEffect.EffectID = m_EffectID+i+1;
				RechargeEffect.Duration = 1000;
				RechargeEffect.EffectDescID = 166;
				RechargeEffect.TargetID = m_AOEFriendList[i]->GameID();
				p->SendObjectToObjectEffectRL(&RechargeEffect);
				SetObjectEffectTimer(m_EffectID+i+1, 1000);
			}
		}
	}
	else
	{
		// Just transfer energy to player.
		p->RemoveEnergy(-energy);

		ObjectToObjectEffect RechargeEffect;
		memset(&RechargeEffect, 0, sizeof(RechargeEffect));		// Zero out memory
				
		RechargeEffect.Bitmask = 3;
		RechargeEffect.GameID = p->GameID();
		RechargeEffect.TimeStamp = m_EffectID+1;
		RechargeEffect.EffectID = m_EffectID+1;
		RechargeEffect.Duration = 1000;
		RechargeEffect.EffectDescID = 166;
		RechargeEffect.TargetID = p->GameID();
		p->SendObjectToObjectEffectRL(&RechargeEffect);
		SetObjectEffectTimer(m_EffectID+1, 1000);
		
	}

	p->SetCurrentSkill();
	m_InUse = false;

	return true;
}

void AShieldLeech::ProximityAOE(CMob *target, short seq, proxparam p1, proxparam p2, proxparam p3)
{
	m_drained_shield += DrainShieldFromTarget( m_SkillLevel, m_SkillRank, target);
}

/*
* What can interrup this skill.
*/
bool AShieldLeech::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = false;
	*OnAction = true;

	return true;
}
