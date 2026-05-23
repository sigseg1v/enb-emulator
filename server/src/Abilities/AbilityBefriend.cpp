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

#include "AbilityBefriend.h"
#include "PlayerClass.h"
#include "MOBClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*
Description=Befriend abilities can be used to make NPCs friendly or keep them from attacking.
Desc_1=Enables the Befriend ability.
Desc_2=Improves success rates and increases range to 5250 units.
Desc_3=Improves success rates, increases range to 5500 units, and enables the Improved Befriend ability.
Desc_4=Improves success rates and increases range to 5750 units.
Desc_5=Improves success rates, increases range to 6000 units, and enables the Entrance ability.
Desc_6=Improves success rates, increases range to 6250 units, and enables the Soothe ability.
Desc_7=Improves success rates, increases range to 6500 units, and enables the Area Soothe ability.
*/

/*
* This calculates the activation cost of the skill.
*/
float ABefriend::CalculateEnergy ( float SkillLevel, long SkillRank )
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
float ABefriend::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	return 2.5f;
}

/*
* Calculate the maximum range this rank of the skill can be used at.
  Used for current targets on not group range.
*/
float ABefriend::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	return 5000.0f + (SkillLevel-1) * 250.0f;
}

/*
*  Calculate the Area of Effect
*/
float ABefriend::CalculateAOE ( float SkillLevel, long SkillRank )
{
	return SkillRank == 7 ? 3000.0f : 0.0f;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long ABefriend::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case BEFRIEND:
		return 1;
	case IMPROVED_BEFRIEND:
		return 3;
	case ENTRANCE:
		return 5;
	case SOOTHE:
		return 6;
	case AREA_SOOTHE:
		return 7;
	default:
		return -1;
	}
}

// --------------------------------------------

bool ABefriend::CanUse(long TargetID, long AbilityID, long SkillID)
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
bool ABefriend::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	// predelay
	ObjectEffect BefriendEffect;
	memset(&BefriendEffect, 0, sizeof(BefriendEffect));	// Zero out memory
	BefriendEffect.Bitmask = 3;
	BefriendEffect.GameID = p->GameID();
	BefriendEffect.TimeStamp = m_EffectID;
	BefriendEffect.EffectID = m_EffectID;
	BefriendEffect.EffectDescID = 715;
	p->SendObjectEffectRL(&BefriendEffect);

	return true;
}

/*
* This function is called when the SetTimer call returns.
*/
bool ABefriend::Update(long activation_ID)
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

	// befriend Target
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
		DeliveryEffect.EffectDescID = m_AbilityID == AREA_SOOTHE ? 648 : 221;

		p->SendObjectToObjectEffectRL(&DeliveryEffect);

		// befriend area
		if ( m_SkillRank == 7)
		{
			GetEnemyGroup();
			// group secondary delivery effect
			DeliveryEffect.GameID = m_Target->GameID();
			DeliveryEffect.EffectDescID = 649;
			for(int x=0;x<6;x++)
			{
				m_Target = m_AOEEnemyList[x];
				if (m_Target)
				{
					DeliveryEffect.TimeStamp = m_EffectID+2+x;
					DeliveryEffect.EffectID = m_EffectID+2+x;
					DeliveryEffect.TargetID = m_Target->GameID();
					p->SendObjectToObjectEffectRL(&DeliveryEffect);

					Befriend(m_Target,SOOTHE);
				}
			}
		}
		else
		{
			Befriend(m_Target,m_AbilityID == AREA_SOOTHE ? SOOTHE : m_AbilityID);
		}
	}
	
	p->SetCurrentSkill();
	m_InUse = false;

	return true;
}

/*
* What can interrup this skill.
*/
bool ABefriend::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = true;
	*OnAction = true;

	return true;
}

void ABefriend::Befriend(CMob *mob, long ability)
{
	CMob *p = GetPointerToCommon();
	// opposed rolls (mob is supposed to use its equipment "tech level")
	int mobroll;
	int ourroll = rand()%(30*(short)m_SkillLevel);

	// befriend effect
	ObjectEffect BefriendEffect;
	memset(&BefriendEffect, 0, sizeof(BefriendEffect));	// Zero out memory
	BefriendEffect.Bitmask = 7;
	BefriendEffect.GameID = mob->GameID();
	BefriendEffect.Duration = 10000;

	switch(ability)
	{
	case SOOTHE:
	case ENTRANCE:
		mobroll = rand()%(30 * (mob->Level() / 5 + 1));
		if (ourroll > mobroll)
		{
			Buff PsionicDebuff;
			memset(&PsionicDebuff, 0, sizeof(Buff));
			for(int i = 0; i < 5; i++)
			{
				PsionicDebuff.EffectID[i] = -1;
			}
			PsionicDebuff.ExpireTime = GetNet7TickCount() + 5000 + 5000 * (int)m_SkillLevel; // guess
			PsionicDebuff.IsPermanent = false;
			strcpy_s(PsionicDebuff.Stats[0].StatName, sizeof(PsionicDebuff.Stats[0].StatName), STAT_PSIONIC_DEFLECT);
			PsionicDebuff.Stats[0].StatName[sizeof(PsionicDebuff.Stats[0].StatName)-1] = '\0';
			PsionicDebuff.Stats[0].Value = 0.05f * m_SkillLevel;	// guess
			PsionicDebuff.Stats[0].StatType = STAT_DEBUFF_VALUE;
			strcpy_s(PsionicDebuff.BuffType, sizeof(PsionicDebuff.BuffType), "Entrance");
			PsionicDebuff.BuffType[sizeof(PsionicDebuff.BuffType)-1] = '\0';
			mob->m_Buffs.RemoveBuff(PsionicDebuff.BuffType);
			mob->m_Buffs.AddBuff(&PsionicDebuff);
			p->SendVaMessage("Enemy entranced (currently not useful)");
		}
	case IMPROVED_BEFRIEND:
		mobroll = rand()%(30 * (mob->Level() / 5 + 1));
		if (ourroll > mobroll)
		{
			BefriendEffect.TimeStamp = ++m_EffectID;
			BefriendEffect.EffectID = m_EffectID;
			BefriendEffect.EffectDescID = 405;
			p->SendObjectEffectRL(&BefriendEffect);
//			p->RemoveEffectRL(m_EffectID);
			mob->Befriend(p,BEFRIEND_CALM);
			p->SendVaMessage("Enemy calmed (currently no effect)");
		}
	case BEFRIEND:
		mobroll = rand()%(30 * (mob->Level() / 5 + 1));
		if (ourroll > mobroll)
		{
			BefriendEffect.TimeStamp = ++m_EffectID;
			BefriendEffect.EffectID = m_EffectID;
			BefriendEffect.EffectDescID = 222;
			p->SendObjectEffectRL(&BefriendEffect);
//			p->RemoveEffectRL(m_EffectID);
			mob->Befriend(p,BEFRIEND_RELATION);
			p->SendVaMessage("Enemy befriended");
		}
		break;
	}
}
