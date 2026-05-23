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

#include "AbilityBioRepression.h"
#include "PlayerClass.h"
#include "MOBClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*
Description=Biorepression abilities can be used to slow the rate of fire, reduce skill usage, and temporarily disable the weapons of organic targets.
Desc_1=Reduces the firing rate of all weapons possessed by an organic target.
Desc_2=Further decreases the firing rate of all weapons possessed by an organic target, increases duration, and extends range to 1250 units.
Desc_3=Reduces the firing rate of all weapons possessed by an organic target, lowers the probability that the target will use skills it possesses, increases duration, and extends range to 1500 units.
Desc_4=Further decreases the firing rate of all weapons possessed by an organic target, further lowers the probability that the target will uses skills, increases duration, and extends range to 1750 units.
Desc_5=Reduces the firing rate of all weapons possessed by organic entities within the area of effect, increases duration, and extends range to 2000 units.
Desc_6=Reduces the firing rate of all weapons possessed by organic entities within the area of effect, lowers the probability that the entities will use skills they possess, increases duration, and extends range to 2250 units.
Desc_7=Attempts to temporarily disable a single weapon possessed by an organic target, increases duration, and extends range to 2500 units. Entities with a single active weapon are more difficult to effect.
*/

/*
* This calculates the activation cost of the skill.
*/
float ABioRepression::CalculateEnergy ( float SkillLevel, long SkillRank )
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
float ABioRepression::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	return 2.5f;
}

/*
* Calculate the maximum range this rank of the skill can be used at.
  Used for current targets on not group range.
*/
float ABioRepression::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	return 1000.0f + (SkillLevel-1) * 250.0f;
}

/*
*  Calculate the Area of Effect
*/
float ABioRepression::CalculateAOE ( float SkillLevel, long SkillRank )
{
	return SkillRank > 5 ? 1500.0f * (float)(SkillRank - 5) : 0.0f;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long ABioRepression::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case BIOREPRESS:
		return 1;
	case BIOSUPPRESS:
		return 3;
	case BIOREPRESSION_SPHERE:
		return 5;
	case BIOSUPPRESSION_SPHERE:
		return 6;
	case BIOCESSATION:
		return 7;
	default:
		return -1;
	}
}

// --------------------------------------------

bool ABioRepression::CanUse(long TargetID, long AbilityID, long SkillID)
{
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx(false) ||
		!AbilityBase::CanUseWithCurrentTarget())
	{
		return false;
	}

	// works on organics only
	if (!m_Target->IsOrganic())
	{
		SendError("Only for use on organics!");
		return false;
	}

	return true;	
}

/*
* This will be the first function called once the skill is determined
* as useable.
*/
bool ABioRepression::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	ObjectEffect RepressEffect;
	memset(&RepressEffect, 0, sizeof(RepressEffect));	// Zero out memory
	RepressEffect.Bitmask = 3;
	RepressEffect.GameID = p->GameID();
	RepressEffect.TimeStamp = m_EffectID;
	RepressEffect.EffectID = m_EffectID;
	RepressEffect.EffectDescID = 713;
	p->SendObjectEffectRL(&RepressEffect);

	return true;
}

/*
* This function is called when the SetTimer call returns.
*/
bool ABioRepression::Update(long activation_ID)
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

	// repress Target
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
		case BIOREPRESSION_SPHERE:
		case BIOREPRESS:
			DeliveryEffect.EffectDescID = 193;
			break;
		case BIOSUPPRESSION_SPHERE:
		case BIOSUPPRESS:
			DeliveryEffect.EffectDescID = 194;
			break;
		case BIOCESSATION:
			DeliveryEffect.EffectDescID = 195;
			break;
		}
		p->SendObjectToObjectEffectRL(&DeliveryEffect);

		// repress area
		if ( m_SkillRank >= 5 && m_SkillRank <= 6)
		{
			GetEnemyGroup();
			// group secondary delivery effect
			DeliveryEffect.GameID = m_Target->GameID();
			DeliveryEffect.EffectDescID = 279;
			for(int x=0;x<6;x++)
			{
				m_Target = m_AOEEnemyList[x];
				if (m_Target)
				{
					DeliveryEffect.TimeStamp = m_EffectID+2+x;
					DeliveryEffect.EffectID = m_EffectID+2+x;
					DeliveryEffect.TargetID = m_Target->GameID();
					p->SendObjectToObjectEffectRL(&DeliveryEffect);

					Repress(m_Target,m_AbilityID == BIOSUPPRESSION_SPHERE ? BIOSUPPRESS : BIOREPRESS);
				}
			}
		}
		else
		{
			Repress(m_Target,m_AbilityID == BIOSUPPRESSION_SPHERE ? BIOSUPPRESS : (m_AbilityID == BIOREPRESSION_SPHERE ? BIOREPRESS : m_AbilityID));
		}
	}
	
	p->SetCurrentSkill();
	m_InUse = false;

	return true;
}

/*
* What can interrup this skill.
*/
bool ABioRepression::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = true;
	*OnAction = true;

	return true;
}

void ABioRepression::Repress(CMob *mob, long ability)
{
	CMob *p = GetPointerToCommon();
	// opposed rolls (mob is supposed to use its equipment "tech level")
	int mobroll;
	int ourroll = rand()%(30*(short)m_SkillLevel);

	// repress effect
	ObjectEffect RepressEffect;
	memset(&RepressEffect, 0, sizeof(RepressEffect));	// Zero out memory
	RepressEffect.Bitmask = 3;
	RepressEffect.GameID = mob->GameID();
	switch(ability)
	{
	case BIOCESSATION: 
		RepressEffect.TimeStamp = m_EffectID+10;
		RepressEffect.EffectID = m_EffectID+10;
		RepressEffect.EffectDescID = 276;
		mobroll = rand()%(40 * (mob->Level() / 5 + 1));
		if (ourroll > mobroll)
		{
			p->SendObjectEffectRL(&RepressEffect);
			SetObjectEffectTimer(m_EffectID+10, 1000);
			mob->Hack(p,HACK_WEAPON);
			p->SendVaMessage("Enemy weapon disabled!");
		}
		else
			p->SendVaMessage("Attempt to disable enemy weapons has failed!");
		break;
	case BIOSUPPRESS:
		RepressEffect.TimeStamp = m_EffectID+11;
		RepressEffect.EffectID = m_EffectID+11;
		RepressEffect.EffectDescID = 277;
		mobroll = rand()%(30 * (mob->Level() / 5 + 1));
		if (ourroll > mobroll)
		{
			p->SendObjectEffectRL(&RepressEffect);
			SetObjectEffectTimer(m_EffectID+11, 1000);
			mob->Hack(p,HACK_DEVICE);
			p->SendVaMessage("Enemy skills disrupted!");
		}
		else
			p->SendVaMessage("Attempt to disrupt enemy skills has failed!");
	case BIOREPRESS:
		RepressEffect.TimeStamp = m_EffectID+12;
		RepressEffect.EffectID = m_EffectID+12;
		RepressEffect.EffectDescID = 275;
		mobroll = rand()%(30 * (mob->Level() / 5 + 1));
		if (ourroll > mobroll)
		{
			p->SendObjectEffectRL(&RepressEffect);
			SetObjectEffectTimer(m_EffectID+12, 1000);
			Buff RepressionDebuff;
			memset(&RepressionDebuff, 0, sizeof(Buff));
			for(int i = 0; i < 5; i++)
			{
				RepressionDebuff.EffectID[i] = -1;
			}
			RepressionDebuff.ExpireTime = GetNet7TickCount() + 5000 + 5000 * (int)m_SkillLevel; // guess
			RepressionDebuff.IsPermanent = false;
			strcpy_s(RepressionDebuff.Stats[0].StatName, sizeof(RepressionDebuff.Stats[0].StatName), STAT_WEAPON_TURBO);
			RepressionDebuff.Stats[0].StatName[sizeof(RepressionDebuff.Stats[0].StatName)-1] = '\0';
			RepressionDebuff.Stats[0].Value = 0.05f * m_SkillLevel;	// guess
			RepressionDebuff.Stats[0].StatType = STAT_BUFF_MULT;
			strcpy_s(RepressionDebuff.BuffType, sizeof(RepressionDebuff.BuffType), "Biorepression");
			RepressionDebuff.BuffType[sizeof(RepressionDebuff.BuffType)-1] = '\0';
			mob->m_Buffs.RemoveBuff(RepressionDebuff.BuffType);
			mob->m_Buffs.AddBuff(&RepressionDebuff);
			p->SendVaMessage("Enemy weapons slowed!");
		}
		else
			p->SendVaMessage("Attempt to slow enemy weapons has failed!");
		break;
	}
}
