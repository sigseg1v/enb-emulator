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

#include "AbilityShieldSap.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"
#include "MOBClass.h"

//**************************************************************
//NOTE!!! use p->SetCurrentSkill(); to flag the skill as unused when you are m_InUse is false.
//  p->SetCurrentSkill(); is set just before the Use() function is called, be sure to clear it 
//  in any code after that function call.
//**************************************************************
/*
Skill properties:
lvl 1: Drains up to 15% of target's sheilds or up to 50% of the user's sheilds (15%{target} > 50%{user} ? 50% : 15%;)
		from the target. Range 1K. Energy cost: 35 (rank 1)
lvl 2: Drains up to 18% (as above). Range 1.25K.
lvl 3: Drains up to 21% (as in lvl 1) and transfers 75% of the energy drained to the user's shields. Range: 1.5K. 
		Energy Cost: 75 units (rank 3)
lvl 4: Drains up to 24% (as in lvl 1) and transfers 87.5% of energy drained to the user's shields. Range: 1.75K
lvl 5: Drains up to 27% (as in lvl 1) and either:
		a. Transfers 100% of the energy drained to the user
		b. Transfers 100% / # of group members energy to each member of the group's shields.
		Range: 2.00K. Energy Cost: 150 (rank 5)
lvl 6: Drains up to 30% (as in lvl 1) from all targets within 1.5K of the target and either:
		a. Transfers 112.5% of the energy drained to the user
		b. Transfers 112.5% / # of group members energy to each member of the group's shields.
		Range: 2.25K. Energy Cost: 300 (rank 6)
lvl 7: Drains up to 33% (as in lvl 1) from all targets within 3.0K of the target and either:
		a. Transfers 125% of the energy drained to the user
		b. Transfers 125% / # of group members energy to each member of the group's shields.
		Range: 2.50K. Energy Cost: 600 (rank 7)
*/

/*
* This calculates the activation cost of the skill.
*/
float AShieldSap::CalculateEnergy ( float SkillLevel, long SkillRank )
{
	switch(SkillRank)
	{
	case 1:
		return 35;
		break;
	case 3:
		return 75;
		break;
	case 5:
		return 150;
		break;
	case 6:
		return 300;
		break;
	case 7:
		return 600;
		break;
	default:
		return -1;
		break;
	}
}

/*
* Calculate how much time must pass before the skill activates.
*
* Results are returned in seconds.
*/
float AShieldSap::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	return (SkillLevel - SkillRank) >= 2.5f ? 0.5f : 3.0f - (SkillLevel - SkillRank);
}

float AShieldSap::CalculateCoolDownTime ( float SkillLevel, long SkillRank )
{
	return 15.0f; //15 second cooldown
}

/*
* Calculate the maximum range this rank of the skill can be used at.
*/
float AShieldSap::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	return SkillLevel * 250.0f + 750.0f; 
}

/*
* Computes the AoE per skill level for an ability.
*/
float AShieldSap::CalculateAOE ( float SkillLevel, long SkillRank )
{
	return SkillRank >= 6 ? (float)(SkillRank-5) * 1500.0f : 0.0f;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long AShieldSap::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case SHIELD_SAP:
		return 1;
	case SHIELD_TRANSFER:
		return 3;
	case GROUP_SAP:
		return 5;
	case SAPPING_SPHERE:
		return 6;
	case GROUP_SAPPING_SPHERE:
		return 7;
	default:
		return -1;
	}
}

float AShieldSap::CalculateShieldDrainPercent(float SkillLevel, long SkillRank)
{
	return SkillLevel * 0.03f + 0.12f;
}

float AShieldSap::CalculateShieldRestorePercent(float SkillLevel, long SkillRank)
{
	return (SkillLevel-3.0f) * 0.125f + 0.75f;
}

/*
* Computes what percent of a player's shield should be restored. If the player is in a group,
*  this function will restore thier shield HP directly. If the player
*  is alone, it will compute what percent of their shields should be restored.
*
* It should be noted that in the event of group healing, no group member will recieve more than 
*  50% of the SKILL USER'S shield hitpoints as restored sheild points. For example, if a PW with
*  100K shields is grouped with a JD @ 0 shields (max 50K), and casts this skill, getting a hit 
*  which heals the PW for 50% of his max shield value, the PW will get at most 50K shields 
*  (a 50% increase). However, the JD will also get up to 50K shield points (a 100% shield restore
*  for him).
*/
void AShieldSap::RestoreShields(CMob *receiver, float shields_drained, float maximum)
{
	float player_shield, shield_restore, new_ratio;
	CMob *p = GetPointerToCommon();

	shield_restore = shields_drained * CalculateShieldRestorePercent(m_SkillLevel, m_SkillRank);

	shield_restore = shield_restore > p->GetMaxShield()/2 ? 
		p->GetMaxShield()/2 : shield_restore;

	player_shield = p->GetShieldValue() + shield_restore;

	if(player_shield < p->GetMaxShield())
	{
		new_ratio = player_shield / p->GetMaxShield();
	}
	else
	{
		new_ratio = 1.0f;
	}

	p->ShieldAux()->SetStartValue(new_ratio);
	p->SendAuxShip();
}

/*
* Computes how much shield energy should be drained from the target and
*  then proceeds to drain the energy.
* Returns how much shielding was drained.
*/
float AShieldSap::DrainShields(CMob *from)
{
	CMob *p = GetPointerToCommon();
	float max_drain = p->GetMaxShield() / 2.0f;
	float drain_amount = 0;

	if(from)
	{
		drain_amount = from->GetMaxShield() * CalculateShieldDrainPercent(m_SkillLevel, m_SkillRank);

		//Will only drain shield points from the mob equal to up to half 
		//  of the player's shield points.
		if(drain_amount > max_drain)
		{
			drain_amount = max_drain;
		}

		if(from->GetShieldLevel() - drain_amount < 0)
		{
			drain_amount = from->GetShieldLevel();
		}

		//perform drain (0 in last parameter means Not a crit)
		from->DamageObject(p->GameID(), DAMAGE_ENERGY, drain_amount, 0);
	}

	return drain_amount;
}

// --------------------------------------------

bool AShieldSap::CanUse(long TargetID, long AbilityID, long SkillID)
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
bool AShieldSap::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	ObjectEffect SapEffect;
	memset(&SapEffect, 0, sizeof(SapEffect));		// Zero out memory

	SapEffect.Bitmask = 3;
	SapEffect.GameID = p->GameID();
	SapEffect.TimeStamp = m_EffectID;
	SapEffect.EffectID = m_EffectID;
	SapEffect.Duration = (short)ChargeTime;
	SapEffect.EffectDescID = 1065;
	p->SendObjectEffectRL(&SapEffect);

	return true;
}

/*
* This function is called when the SetTimer call returns.
*/
bool AShieldSap::Update(long activation_ID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	p->RemoveEffectRL(m_EffectID);

	m_EffectID = GetNet7TickCount();

	//beam to target ship
	ObjectToObjectEffect RechargeBeamEffect;

	memset(&RechargeBeamEffect, 0, sizeof(RechargeBeamEffect));		// Zero out memory

	RechargeBeamEffect.Bitmask = 3;
	RechargeBeamEffect.GameID = p->GameID();
	RechargeBeamEffect.TimeStamp = m_EffectID+1;
	RechargeBeamEffect.EffectID = m_EffectID+1;
	RechargeBeamEffect.Duration = 1000;
	RechargeBeamEffect.EffectDescID = 1047;
	RechargeBeamEffect.TargetID = m_Target->GameID();
	p->SendObjectToObjectEffectRL(&RechargeBeamEffect);
	SetObjectEffectTimer(m_EffectID+1, 1000);

	//recharge orb around target ship.
	ObjectToObjectEffect StealEffect;

	memset(&StealEffect, 0, sizeof(StealEffect));		// Zero out memory

	StealEffect.Bitmask = 3;
	StealEffect.GameID = p->GameID();
	StealEffect.TimeStamp = m_EffectID;
	StealEffect.EffectID = m_EffectID;
	StealEffect.Duration = 1000;
	StealEffect.EffectDescID = 1041;
	StealEffect.TargetID = m_Target->GameID();
	p->SendObjectToObjectEffectRL(&StealEffect);
	SetObjectEffectTimer(m_EffectID, 1000);

	m_DrainAmount = 0;
	float MaxRestore = p->GetMaxShield()/2;
	if (m_SkillRank > 5)
	{
		proxparam p1(&RechargeBeamEffect);
		proxparam p2(&StealEffect);
		UseOnAllEnemiesInRange(true,p1,p2);
	}
	else
		m_DrainAmount += DrainShields(m_Target);

	// add drained energy to user (+group)
	if(m_SkillRank > 1)
	{
		RestoreShields(p, m_DrainAmount, MaxRestore);
		if (m_SkillRank == 5 || m_SkillRank == 7)
		{
			int count = GetFriendlyGroup();
			m_DrainAmount /= (float)count;
			for (int x=1;x < 6;x++)
			{
				CMob *groupmember = m_AOEFriendList[x];
				if (groupmember)
				{
					RestoreShields(groupmember, m_DrainAmount, MaxRestore);
				}
			}
		}
	}

	m_InUse = false;
	p->SetCurrentSkill();

	return true;
}

void AShieldSap::ProximityAOE(CMob *target, short seq, proxparam effect1, proxparam effect2, proxparam p3)
{
	CMob *p = GetPointerToCommon();

	m_DrainAmount += DrainShields(target);

	ObjectToObjectEffect *RechargeBeamEffect = (ObjectToObjectEffect *)effect1.struc;
	RechargeBeamEffect->EffectID = m_EffectID+1+seq*2;
	RechargeBeamEffect->TargetID = target->GameID();
	p->SendObjectToObjectEffectRL(RechargeBeamEffect);
	SetObjectEffectTimer(m_EffectID+1+seq*2, 1000);

	ObjectToObjectEffect *StealEffect = (ObjectToObjectEffect *)effect2.struc;
	StealEffect->EffectID = m_EffectID+seq*2;
	StealEffect->TargetID = target->GameID();
	p->SendObjectToObjectEffectRL(StealEffect);
	SetObjectEffectTimer(m_EffectID+seq*2, 1000);
}

/*
* Returns true in the case that this skill can be interrupted.
* What can interrupt the skill is returned inside the OnMotion 
*  and OnDamage pointers.
*/
bool AShieldSap::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = true;
	*OnAction = true;

	return true;
}
