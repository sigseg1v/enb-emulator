
#include "AbilityHacking.h"
#include "PlayerClass.h"
#include "MOBClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*
Description=Hacking abilities can be used to temporarily disable a target's shields, reactor, engine, weapons or devices.
Desc_1=Enables the Hack Systems ability.
Desc_2=Improves hacking success rates and extends range to 1250 units.
Desc_3=Improves hacking success rates, extends range to 1500 units, and enables the Hack Weapons ability.
Desc_4=Improves hacking success rates, extends range to 1750 units.
Desc_5=Improves hacking success rates, extends range to 2000 units, and enables the Multi-Hack ability.
Desc_6=Improves hacking success rates, extends range to 2250 units, and enables the Area System Hack ability.
Desc_7=Improves hacking success rates, extends range to 2500 units, and enables the Area Multi-Hack ability.
*/

/*
* This calculates the activation cost of the skill.
*/
float AHacking::CalculateEnergy ( float SkillLevel, long SkillRank )
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
float AHacking::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	return 2.5f;
}

/*
* Calculate the maximum range this rank of the skill can be used at.
  Used for current targets on not group range.
*/
float AHacking::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	return 1000.0f + (SkillLevel-1) * 250.0f;
}

/*
*  Calculate the Area of Effect
*/
float AHacking::CalculateAOE ( float SkillLevel, long SkillRank )
{
	return SkillRank > 5 ? 1500.0f * (float)(SkillRank - 5) : 0.0f;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long AHacking::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case HACK_SYSTEMS:
		return 1;
	case HACK_WEAPONS:
		return 3;
	case MULTI_HACK:
		return 5;
	case AREA_SYSTEM_HACK:
		return 6;
	case AREA_MULTI_HACK:
		return 7;
	default:
		return -1;
	}
}

bool AHacking::CanUse(long TargetID, long AbilityID, long SkillID)
{
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx() ||
		!AbilityBase::CanUseWithCurrentTarget())
	{
		return false;
	}

	// organics are immune to hacks
	if (m_Target->IsOrganic())
	{
		SendError("Organics are immune!");
		return false;
	}

	return true;	
}

/*
* This will be the first function called once the skill is determined
* as useable.
*/
bool AHacking::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	ObjectEffect HackEffect;
	memset(&HackEffect, 0, sizeof(HackEffect));	// Zero out memory
	HackEffect.Bitmask = 3;
	HackEffect.GameID = p->GameID();
	HackEffect.TimeStamp = m_EffectID;
	HackEffect.EffectID = m_EffectID;
	HackEffect.EffectDescID = 713;
	p->SendObjectEffectRL(&HackEffect);

	return true;
}

/*
* This function is called when the SetTimer call returns.
*/
bool AHacking::Update(long activation_ID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	// Remove Effect
	p->RemoveEffectRL(m_EffectID);

	// Get tick count
	m_EffectID = GetNet7TickCount();

	// hack Target
	if ( m_Target )
	{
		// delivery effect
		ObjectToObjectEffect DeliveryEffect;
		memset(&DeliveryEffect, 0, sizeof(DeliveryEffect));	// Zero out memory
		DeliveryEffect.Bitmask = 3;
		DeliveryEffect.GameID = p->GameID();
		DeliveryEffect.TimeStamp = m_EffectID+1;
		DeliveryEffect.EffectID = m_EffectID+1;
		DeliveryEffect.TargetID = m_Target->GameID();
		switch(m_AbilityID)
		{
		case AREA_SYSTEM_HACK:
		case HACK_SYSTEMS:
			DeliveryEffect.EffectDescID = 193;
			break;
		case HACK_WEAPONS:
			DeliveryEffect.EffectDescID = 194;
			break;
		case AREA_MULTI_HACK:
		case MULTI_HACK:
			DeliveryEffect.EffectDescID = 195;
			break;
		}
		p->SendObjectToObjectEffectRL(&DeliveryEffect);

		// hack area
		if ( m_SkillRank > 5 )
		{
			GetEnemyGroup();
			// group secondary delivery effect
			DeliveryEffect.GameID = m_Target->GameID();
			DeliveryEffect.EffectDescID = m_AbilityID == AREA_SYSTEM_HACK ? 197 : 279;
			for(int x=0;x<6;x++)
			{
				m_Target = m_AOEEnemyList[x];
				if (m_Target)
				{
					DeliveryEffect.TimeStamp = m_EffectID+2+x;
					DeliveryEffect.EffectID = m_EffectID+2+x;
					DeliveryEffect.TargetID = m_Target->GameID();
					p->SendObjectToObjectEffectRL(&DeliveryEffect);

					Hack(m_Target,m_AbilityID == AREA_SYSTEM_HACK ? HACK_SYSTEMS : MULTI_HACK);
				}
			}
		}
		else
		{
			Hack(m_Target,m_SkillRank > 5 ? MULTI_HACK : m_AbilityID);
		}
	}
	
	p->SetCurrentSkill();
	m_InUse = false;

	return true;
}

/*
* What can interrup this skill.
*/
bool AHacking::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = true;
	*OnAction = true;

	return true;
}

void AHacking::Hack(CMob *mob, long ability)
{
	CMob *p = GetPointerToCommon();
	// opposed rolls (mob is supposed to use its equipment "tech level")
	int mobroll;
	int ourroll = rand()%(30*(short)m_SkillLevel);

	// hack effect
	ObjectEffect HackEffect;
	memset(&HackEffect, 0, sizeof(HackEffect));	// Zero out memory
	HackEffect.Bitmask = 3;
	HackEffect.GameID = mob->GameID();
	switch(ability)
	{
	case MULTI_HACK: 
		HackEffect.TimeStamp = m_EffectID+10;
		HackEffect.EffectID = m_EffectID+10;
		HackEffect.EffectDescID = 276;
		if (rand()%2) // supposed to use INT
		{
			p->SendObjectEffectRL(&HackEffect);
			SetObjectEffectTimer(m_EffectID+10, 1000);
			mob->Hack(p,HACK_COMMS);
			p->SendVaMessage("Enemy communications hacked!");
		}
		else
			p->SendVaMessage("Attempt to hack enemy comms has failed!");
	case HACK_WEAPONS:
		HackEffect.TimeStamp = m_EffectID+11;
		HackEffect.EffectID = m_EffectID+11;
		HackEffect.EffectDescID = 277;
		mobroll = rand()%(40 * (mob->Level() / 5 + 1));
		if (ourroll > mobroll)
		{
			p->SendObjectEffectRL(&HackEffect);
			SetObjectEffectTimer(m_EffectID+11, 1000);
			mob->Hack(p,HACK_WEAPON);
			p->SendVaMessage("Enemy weapons hacked!");
		}
		else
			p->SendVaMessage("Attempt to hack enemy weapons has failed!");
	case HACK_SYSTEMS:
		HackEffect.TimeStamp = m_EffectID+12;
		HackEffect.EffectID = m_EffectID+12;
		HackEffect.EffectDescID = 275;
		mobroll = rand()%(30 * (mob->Level() / 5 + 1));
		if (ourroll > mobroll)
		{
			p->SendObjectEffectRL(&HackEffect);
			SetObjectEffectTimer(m_EffectID+12, 1000);
			HACK_Type type = (HACK_Type)(rand()%HACK_WEAPON);
			mob->Hack(p,type);
			switch (type)
			{
			case HACK_ENGINE:
				p->SendVaMessage("Enemy engines hacked!");
				break;
			case HACK_REACTOR:
				p->SendVaMessage("Enemy reactor hacked!");
				break;
			case HACK_SHIELD:
				p->SendVaMessage("Enemy shield hacked!");
				break;
			case HACK_DEVICE:
				p->SendVaMessage("Enemy device hacked!");
				break;
			}
		}
		else
			p->SendVaMessage("Attempt to hack enemy systems has failed!");
		break;
	}
}
