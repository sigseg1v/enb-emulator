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

#include "AbilityEnergyLeech.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*
Skill properties:
Description=This ability drains a portion of an enemy's or group of enemies' energy.  At higher levels, this skill transfers some of that drained energy to the player's or a player group's energy banks.
Desc_1=Drains energy from an enemy target.
Desc_2=Improves amount of drain on a single target and extends range to 1250 units.
Desc_3=Improves amount of drain on a single target, converts a percentage of that into energy for the player, and extends range to 1500 units.
Desc_4=Improves amount of drain and extends range to 1750 units.
Desc_5=Improves amount of energy drain on a single target, converts a percentage of that into energy for the player and their group, and extends range to 2000 units.
Desc_6=Improves amount of energy drain in a spherical area of effect of 1500 units surrounding the player, converts a percentage of that into energy for the player, and extends range to 2250 units.
Desc_7=Improves amount of energy drain in a spherical area of effect of 3000 units surrounding the player, converts a percentage of that into energy for the player and their group, and extends range to 2500 units.
Range=1000,1250,1500,1750,2000,2250,2500
*/

/*
* This calculates the activation cost of the skill.
*/
float AEnergyLeech::CalculateEnergy ( float SkillLevel, long SkillRank )
{
	switch (SkillRank)
	{
	case 1:
		return 35.0f;
	case 3:
		return 75.0f;
	case 5:
		return 150.0f;
	case 6:
		return 300.0f;
	case 7:
		return 600.0f;
	}
	return 0.0f; // shouldnt happen
}

/*
* Calculate how much time must pass before the skill activates.
*
* Results are returned in seconds.
*/
float AEnergyLeech::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	return 4.0f;
}

/*
* Calculate the maximum range this rank of the skill can be used at.
*/
float AEnergyLeech::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	return 1000.0f + (SkillLevel-1) * 250.0f;
}

/*
* Computes the AoE per skill level for an ability.
*/
float AEnergyLeech::CalculateAOE ( float SkillLevel, long SkillRank )
{
	return SkillLevel < 6 ? 0.0f : (SkillLevel == 6 ? 1500.0f : 3000.0f);
}

float AEnergyLeech::CalculateDrain ( float SkillLevel, long SkillRank )
{
	return CalculateEnergy(SkillLevel,SkillRank) * 0.5f; // TODO: find out real drain amounts
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long AEnergyLeech::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case ENERGY_DRAIN:
		return 1;
	case ENERGY_LEECH:
		return 3;
	case RENDER_ENERGY:
		return 5;
	case ENERGY_LEECHING_SPHERE:
		return 6;
	case RENDER_ENERGY_SPHERE: 
		return 7;
	default:
		return -1;
	}
}

// --------------------------------------------

bool AEnergyLeech::CanUse(long TargetID, long AbilityID, long SkillID)
{
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx(false) ||
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
bool AEnergyLeech::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	ObjectEffect LeechEffect;
	memset(&LeechEffect, 0, sizeof(LeechEffect));		// Zero out memory
	
	// predelay effect on self
	LeechEffect.Bitmask = 3;
	LeechEffect.TimeStamp = m_EffectID;
	LeechEffect.EffectID = m_EffectID;
	LeechEffect.EffectDescID = 1057;
	LeechEffect.GameID = p->GameID();
	p->SendObjectEffectRL(&LeechEffect);

	return true;
}

/*
* This function is called when the SetTimer call returns.   
*/
bool AEnergyLeech::Update(long activation_ID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	// remove the effects
	p->RemoveEffectRL(m_EffectID);

	float Drain = CalculateDrain(m_SkillLevel, m_SkillRank);
	float AOErange = CalculateAOE(m_SkillLevel, m_SkillRank);

	ObjectToObjectEffect LeechEffect;
	memset(&LeechEffect, 0, sizeof(LeechEffect));		// Zero out memory
	
	// drain beam effect
	LeechEffect.Bitmask = 3;
	LeechEffect.TimeStamp = m_EffectID;
	LeechEffect.EffectID = m_EffectID;
	LeechEffect.EffectDescID = 1020;
	LeechEffect.GameID = p->GameID();
	LeechEffect.TargetID = m_Target->GameID();
	p->SendObjectToObjectEffectRL(&LeechEffect);
	SetObjectEffectTimer(m_EffectID, 1000);

	// TODO: mobs dont have energy to drain currently!
	if (m_SkillRank > 5)
	{
		// how many mobs to drain
		proxparam p1(Drain);
		proxparam p2(&LeechEffect);
		Drain *= UseOnAllEnemiesInRange(false,p1,p2);
	}
	else
		m_Target->AddHate(p->GameID(),(int)(Drain * 2.0f));

	// add to other player(s)
	if (m_SkillRank >= 5 && m_SkillRank != 6)
	{
		GetFriendlyGroup();
		for(int x=1;x<6;x++)
		{
			CMob* groupMember = m_AOEFriendList[x];
			if (groupMember)
			{
				// ADD the energy drained
				groupMember->RemoveEnergy(-Drain);

				// Send Energy update
				groupMember->SendAuxShip();

				//beam to target ship
				ObjectToObjectEffect RechargeBeamEffect;
				memset(&RechargeBeamEffect, 0, sizeof(RechargeBeamEffect));		// Zero out memory

				RechargeBeamEffect.Bitmask = 3;
				RechargeBeamEffect.TimeStamp = m_EffectID+x;
				RechargeBeamEffect.EffectID = m_EffectID+x;
				RechargeBeamEffect.EffectDescID = 139;
				RechargeBeamEffect.GameID = p->GameID();
				RechargeBeamEffect.TargetID = groupMember->GameID();
				p->SendObjectToObjectEffectRL(&RechargeBeamEffect);
				SetObjectEffectTimer(m_EffectID+x, 1000);
			}
		}
	}

	// and self
	if (m_SkillRank > 1)
	{
		// ADD the energy drained
		p->RemoveEnergy(-Drain);

		// Send Energy update
		p->SendAuxShip();
	}

	m_InUse = false;
	p->SetCurrentSkill();

	return true;
}

void AEnergyLeech::ProximityAOE(CMob *target, short seq, proxparam Drain, proxparam effect, proxparam p3)
{
	CMob *p = GetPointerToCommon();

	// TODO: mobs dont have energy to drain currently!
	target->AddHate(p->GameID(),(long)(Drain.flt * 2.0f));
	
	ObjectToObjectEffect *LeechEffect = (ObjectToObjectEffect *)effect.struc;
	// drain beam effect
	LeechEffect->TimeStamp = m_EffectID+seq;
	LeechEffect->EffectID = m_EffectID+seq;
	LeechEffect->TargetID = target->GameID();
	p->SendObjectToObjectEffectRL(LeechEffect);
	SetObjectEffectTimer(m_EffectID+seq, 1000);
}

/*
* Returns true in the case that this skill can be interrupted.
* What can interrupt the skill is returned inside the OnMotion 
*  and OnDamage pointers.
*/
bool AEnergyLeech::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = true;
	*OnAction = true;

	return true;
}
