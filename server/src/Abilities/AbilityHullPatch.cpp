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

#include "AbilityHullPatch.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

class PlayerClass;

/* TO-DO: Get better values (if needed) for hull repair per lvl
Skill properties:
lvl 1: 50 energy, 90 hull per skill lvl, only useable on self
lvl 2: null
lvl 3: 125 energy, 3K range, only useable on other players & self, 360pts per lvl
lvl 4: range increased to 3250
lvl 5: 250 energy, 3.5K range, 1440pts per lvl
lvl 6: 500 energy, 3.75k range, 2880pts per lvl, all friendly ships within 1500 units of target by 540pts per lvl
lvl 7: 1000 energy, 4k range, 5760pts per lvl, all friendly ships within 3000 units of target by 1800pts per lvl
*/

/*
* This calculates the activation cost of the skill.
*/
float AHullPatch::CalculateEnergy ( float SkillLevel, long SkillRank )
{
	float EnergyCost;
	CMob *p = GetPointerToCommon();

	switch(SkillRank)
	{
	case 1:
		EnergyCost = 50.0f;
		break;
	case 3:
		EnergyCost = 125.0f;
		break;
	case 5:
		EnergyCost = 250.0f;
		break;
	case 6:
		EnergyCost = 500.0f;
		break;
	case 7:
		EnergyCost = 1000.0f;
		break;
	}

	EnergyCost = 
		((1.0f + p->m_Stats.GetStatType(STAT_HULL_PATCH_ECOST, STAT_BUFF_MULT)) * EnergyCost) +
		p->m_Stats.GetStatType(STAT_HULL_PATCH_ECOST, STAT_BUFF_VALUE);

	EnergyCost < 0.0f ? 0.0f : EnergyCost;

	return EnergyCost;
}

/*
* Calculate how much time must pass before the skill activates.
*
* Results are returned in seconds.
*/
float AHullPatch::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	CMob *p = GetPointerToCommon();
	
	//minus 1 second per lvl of skill above the rank
	float ChargeTime = 5.0f - (SkillLevel - SkillRank);

	//ensure wierdness didn't happen
	ChargeTime = ChargeTime > 5.0f ? 5.0f : ChargeTime;

	//apply any direct bonuses to chargetime
	ChargeTime = 
		((1.0f - p->m_Stats.GetStatType(STAT_HULL_PATCH, STAT_BUFF_MULT)) * ChargeTime) -
		p->m_Stats.GetStatType(STAT_HULL_PATCH, STAT_BUFF_VALUE);

	//ensure charge time is still positive, or 0
	ChargeTime = ChargeTime > 1.0f ? ChargeTime : 1.0f;

	return ChargeTime;
}

/*
* Calculate the maximum range this rank of the skill can be used at.
*/
float AHullPatch::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	CMob *p = GetPointerToCommon();
	if(SkillRank < 3)
	{
		return 0.0f;
	}
	else
	{
		float Range = 3000 + ((SkillLevel - 3)*250);

		Range = 
			((1.0f + p->m_Stats.GetStatType(STAT_HULL_PATCH_RANGE, STAT_BUFF_MULT)) * Range) +
			p->m_Stats.GetStatType(STAT_HULL_PATCH_RANGE, STAT_BUFF_VALUE);
		return Range;
	}
}

/*
* Computes the AoE per skill level for an ability.
*/
float AHullPatch::CalculateAOE ( float SkillLevel, long SkillRank )
{
	return 1500.0f + (1500.0f * (SkillLevel-6));
}

/*
* Returns the ammount of sheilds that have been recharged.
*/
float AHullPatch::CalculateHullRepair ( float SkillLevel, long SkillRank )
{
	CMob *p = GetPointerToCommon();
	float ChargeAmount;

	//TO-DO: Write in code for buffs to shield charge ammount per lvl &
	//  to overall ammount charged. If needed.

	switch(SkillRank)
	{
	case 1:
		ChargeAmount = SkillLevel * 90;
		break;
	case 3:
		ChargeAmount = SkillLevel * 360;
		break;
	case 5:
		ChargeAmount = SkillLevel * 1440;
		break;
	case 6:
		ChargeAmount = SkillLevel * 2880;
		break;
	case 7:
		ChargeAmount = SkillLevel * 5760;
		break;
	default:
		ChargeAmount = 0;
		break;
	}

	return ChargeAmount;
}

/*
* Returns the ammount of sheilds that have been recharged to AOE targets.
*/
float AHullPatch::CalculateAOEHullRepair ( float SkillLevel, long SkillRank )
{
	CMob *p = GetPointerToCommon();
	float ChargeAmount;

	//TO-DO: Write in code for buffs to shield charge ammount per lvl &
	//  to overall ammount charged. If needed.

	switch(SkillRank)
	{
	case 6:
		ChargeAmount = SkillLevel * 540;
		break;
	case 7:
		ChargeAmount = SkillLevel * 1920;
		break;
	default:
		ChargeAmount = 0;
		break;
	}

	return ChargeAmount;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long AHullPatch::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case PATCH_HULL:
		return 1;
	case REPAIR_HULL:
		return 3;
	case COMBAT_HULL_REPAIR:
		return 5;
	case AREA_HULL_REPAIR:
		return 6;
	case IMPROVED_AREA_HULL_REPAIR:
		return 7;
	default:
		return -1;
	}
}

bool AHullPatch::SelfOnly()
{ 
	return m_SkillRank < 3;
}

// --------------------------------------------

bool AHullPatch::CanUse(long TargetID, long AbilityID, long SkillID)
{
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
bool AHullPatch::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	//This uses the same animation that Shield Recharge does
	ObjectEffect HullPatchChargeEffect;
	memset(&HullPatchChargeEffect, 0, sizeof(HullPatchChargeEffect));		// Zero out memory

	HullPatchChargeEffect.Bitmask = 3;
	HullPatchChargeEffect.GameID = p->GameID();
	HullPatchChargeEffect.TimeStamp = m_EffectID;
	HullPatchChargeEffect.EffectID = m_EffectID;
	HullPatchChargeEffect.Duration = (short)ChargeTime;
	HullPatchChargeEffect.EffectDescID = 733;
	p->SendObjectEffectRL(&HullPatchChargeEffect);

	return true;
}

/*
* This function is called when the SetTimer call returns.
*/
bool AHullPatch::Update(long activation_ID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	p->RemoveEffectRL(m_EffectID);

	m_EffectID = GetNet7TickCount();

	float RepairAmount = CalculateHullRepair(m_SkillLevel, m_SkillRank);

	if(m_SkillRank < 3)
	{
		RechargeFriendly(p, RepairAmount);
	}
	else if(m_Target)
	{
		RechargeFriendly(m_Target, RepairAmount);
	}

	if(m_SkillRank < 3)
	{
		ObjectEffect HullPatchEffect;

		memset(&HullPatchEffect, 0, sizeof(HullPatchEffect));		// Zero out memory

		HullPatchEffect.Bitmask = 3;
		HullPatchEffect.GameID = p->GameID();
		HullPatchEffect.TimeStamp = m_EffectID;
		HullPatchEffect.EffectID = m_EffectID;
		HullPatchEffect.Duration = 1000;
		HullPatchEffect.EffectDescID = 136;
		p->SendObjectEffectRL(&HullPatchEffect);
		SetObjectEffectTimer(m_EffectID, 1000);
	}
	else if (m_Target)
	{
		//beam to target ship
		ObjectToObjectEffect HullPatchBeamEffect;

		memset(&HullPatchBeamEffect, 0, sizeof(HullPatchBeamEffect));		// Zero out memory

		HullPatchBeamEffect.Bitmask = 3;
		HullPatchBeamEffect.GameID = p->GameID();
		HullPatchBeamEffect.TimeStamp = m_EffectID+1;
		HullPatchBeamEffect.EffectID = m_EffectID+1;
		HullPatchBeamEffect.Duration = 1000;
		HullPatchBeamEffect.EffectDescID = 139;
		HullPatchBeamEffect.TargetID = m_Target->GameID();
		m_Target->SendObjectToObjectEffectRL(&HullPatchBeamEffect);
		SetObjectEffectTimer(m_EffectID+1, 1000);

		//Target ship recharge orb
		ObjectToObjectEffect HullPatchEffect;

		memset(&HullPatchEffect, 0, sizeof(HullPatchEffect));		// Zero out memory

		HullPatchEffect.Bitmask = 3;
		HullPatchEffect.GameID = p->GameID();
		HullPatchEffect.TimeStamp = m_EffectID;
		HullPatchEffect.EffectID = m_EffectID;
		HullPatchEffect.Duration = 1000;
		HullPatchEffect.EffectDescID = 166;
		HullPatchEffect.TargetID = m_Target->GameID();
		p->SendObjectToObjectEffectRL(&HullPatchEffect);
		SetObjectEffectTimer(m_EffectID, 1000);
	}

	float AOERepairAmount = CalculateAOEHullRepair(m_SkillLevel, m_SkillRank);
	if(m_SkillRank > 5) // do an AOE recharge effect to everyone in range
	{
		proxparam p1(AOERepairAmount);
		UseOnAllFriendsInRange(true,p1);
	}

	m_InUse = false;
	p->SetCurrentSkill();

	return true;
}

void AHullPatch::ProximityAOE(CMob *target, short seq, proxparam repair, proxparam p2, proxparam p3)
{
	if (target != m_Target)
	{
		CMob *p = GetPointerToCommon();

		RechargeFriendly(target, repair.flt);

		//beam to target ship
		ObjectToObjectEffect RechargeBeamEffect;

		memset(&RechargeBeamEffect, 0, sizeof(RechargeBeamEffect));		// Zero out memory

		RechargeBeamEffect.Bitmask = 3;
		RechargeBeamEffect.GameID = p->GameID();
		RechargeBeamEffect.TimeStamp = m_EffectID+seq*2;
		RechargeBeamEffect.EffectID = m_EffectID+seq*2;
		RechargeBeamEffect.Duration = 1000;
		RechargeBeamEffect.EffectDescID = 139;
		RechargeBeamEffect.TargetID = target->GameID();
		p->SendObjectToObjectEffectRL(&RechargeBeamEffect);
		SetObjectEffectTimer(m_EffectID+seq*2, 1000);

		//recharge orb around target ship.
		ObjectToObjectEffect RechargeEffect;

		memset(&RechargeEffect, 0, sizeof(RechargeEffect));		// Zero out memory

		RechargeEffect.Bitmask = 3;
		RechargeEffect.GameID = p->GameID();
		RechargeEffect.TimeStamp = m_EffectID+seq*2+1;
		RechargeEffect.EffectID = m_EffectID+seq*2+1;
		RechargeEffect.Duration = 1000;
		RechargeEffect.EffectDescID = 166;
		RechargeEffect.TargetID = target->GameID();
		p->SendObjectToObjectEffectRL(&RechargeEffect);
		SetObjectEffectTimer(m_EffectID+seq*2+1, 1000);
	}
}

/*
* Returns true in the case that this skill can be interrupted.
* What can interrupt the skill is returned inside the OnMotion 
*  and OnDamage pointers.
*/
bool AHullPatch::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = true;
	*OnAction = true;
	return true;
}

void AHullPatch::RechargeFriendly(CMob *Target, float ChargeAmount)
{
	float HullPoints = ChargeAmount + Target->GetHullPoints();

	// Make sure we dont over fill your hull
	if (HullPoints > Target->GetMaxHullPoints())
	{
		HullPoints = Target->GetMaxHullPoints(); // Set it at full
	}

	Target->SetHullPoints(HullPoints);

	// Send Shield update/Energy update
	Target->SendAuxShip();
}
