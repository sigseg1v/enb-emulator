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

#include "AbilityEnrage.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*
Description=Enrage abilities can be used to anger enemies, forcing them to attack regardless of the situation.
Desc_1=Enables the Anger ability.
Desc_2=Improves effect power and increases range to 5250 units.
Desc_3=Improves effect power, increases range to 5500 units, and enables the Cause Aggression ability.
Desc_4=Improves effect power and increases range to 5750 units.
Desc_5=Improves effect power, increases range to 6000 units, and enables the Enrage ability.
Desc_6=Improves effect power, increases range to 6250 units, and enables the Anger Group ability.
Desc_7=Improves effect power, increases range to 6500 units, and enables the Enrage Group ability.
*/

/*
* This calculates the activation cost of the skill.
*/
float AEnrage::CalculateEnergy ( float SkillLevel, long SkillRank )
{
	if ( m_SkillRank == 1 )
		return 35.0f;
	else if ( m_SkillRank == 3 )
		return 75.0f;
	else if ( m_SkillRank == 5 )
		return 150.0f;
	else if ( m_SkillRank == 6 )
		return 300.0f;
	else
		return 600.0f;
}

/*
* Calculate how much time must pass before the skill activates.
*
* Results are returned in seconds.
*/
float AEnrage::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	return 4.0f;
}

/*
* Calculate the maximum range this rank of the skill can be used at.
  Used for current targets on not group range.
*/
float AEnrage::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	return 5000.0f + (SkillLevel-1) * 250.0f;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long AEnrage::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case ANGER:
		return 1;
	case CAUSE_AGGRESSION:
		return 3;
	case ENRAGE:
		return 5;
	case ANGER_GROUP:
		return 6;
	case ENRAGE_GROUP:
		return 7;
	default:
		return -1;
	}
}

bool AEnrage::CanUse(long TargetID, long AbilityID, long SkillID)
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
bool AEnrage::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	ObjectEffect EnrageEffect;
	memset(&EnrageEffect, 0, sizeof(EnrageEffect));	// Zero out memory
	EnrageEffect.Bitmask = 3;
	EnrageEffect.GameID = p->GameID();
	EnrageEffect.TimeStamp = m_EffectID;
	EnrageEffect.EffectID = m_EffectID;
	EnrageEffect.EffectDescID = 717;
	p->SendObjectEffectRL(&EnrageEffect);

	return true;
}

/*
* This function is called when the SetTimer call returns.
*/
bool AEnrage::Update(long activation_ID)
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

	// cast effect
	ObjectEffect EnrageEffect;
	memset(&EnrageEffect, 0, sizeof(EnrageEffect));	// Zero out memory
	EnrageEffect.Bitmask = 3;
	EnrageEffect.GameID = p->GameID();
	EnrageEffect.TimeStamp = m_EffectID;
	EnrageEffect.EffectID = m_EffectID;
	EnrageEffect.EffectDescID = 212;
	p->SendObjectEffectRL(&EnrageEffect);
	SetObjectEffectTimer(m_EffectID, 1000);

	// Enrage Target
	if ( m_Target )
	{
		// delivery effect
		ObjectToObjectEffect DeliveryEffect;
		memset(&DeliveryEffect, 0, sizeof(DeliveryEffect));	// Zero out memory
		DeliveryEffect.Bitmask = 3;
		DeliveryEffect.GameID = p->GameID();
		DeliveryEffect.EffectDescID = 213;
		DeliveryEffect.TimeStamp = m_EffectID+1;
		DeliveryEffect.EffectID = m_EffectID+1;
		DeliveryEffect.TargetID = m_Target->GameID();
		p->SendObjectToObjectEffectRL(&DeliveryEffect);

		Enrage(m_Target,m_SkillRank > 5 ? ENRAGE : m_AbilityID);
		// Enrage area
		if ( m_SkillRank > 5 )
		{
			GetEnemyGroup();
			// group secondary delivery effect
			DeliveryEffect.GameID = m_Target->GameID();
			DeliveryEffect.EffectDescID = 422;
			for(int x=1;x<6;x++)
			{
				m_Target = m_AOEEnemyList[x];
				if (m_Target)
				{
					DeliveryEffect.TimeStamp = m_EffectID+2+x;
					DeliveryEffect.EffectID = m_EffectID+2+x;
					DeliveryEffect.TargetID = m_Target->GameID();
					p->SendObjectToObjectEffectRL(&DeliveryEffect);

					Enrage(m_Target,m_AbilityID == ANGER_GROUP ? ANGER : ENRAGE);
				}
			}
		}
	}
	
	p->SetCurrentSkill();
	m_InUse = false;

	return true;
}

void AEnrage::Enrage(CMob *mob, long ability)
{
	CMob *p = GetPointerToCommon();
	// enrage effect
	ObjectEffect EnrageEffect;
	memset(&EnrageEffect, 0, sizeof(EnrageEffect));	// Zero out memory

	MOB_Aggression aggression = mob->GetAggression();
	switch (ability)
	{
	case ANGER:
		EnrageEffect.EffectDescID = 420;
		break;
	case ENRAGE:
		EnrageEffect.EffectDescID = 421;
		// TODO: beam and projectile skill reduction
	case CAUSE_AGGRESSION:
		aggression = (MOB_Aggression)((int)aggression + 2);
	}

	// base chance + modify for level difference
	int chance = 50 + (p->CombatLevel() - mob->Level()) * 5;

	// modify by mob aggression
	switch (aggression)
	{
	case AGGRESSION_NONE:
		chance = 0;
		break;
	case AGGRESSION_CAUTIOUS:
		chance -= 25;
		break;
	case AGGRESSION_ANTAGONISTIC:
		chance += 25;
		break;
	case AGGRESSION_FANATIC:
	default: // aggression bonus can take it past fanatic
		chance += 50;
	}

	// TODO: check against psionic resist also when mobs have resists
	// did we hit?
	if (rand()%100 < chance)
	{
		EnrageEffect.Bitmask = 3;
		EnrageEffect.GameID = mob->GameID();
		EnrageEffect.TimeStamp = m_EffectID;
		EnrageEffect.EffectID = m_EffectID;
		p->SendObjectEffectRL(&EnrageEffect);

		mob->Taunt(p);
	}
	else
		SendError(chance == 0 ? "Immune!" : "Resisted!");
}
