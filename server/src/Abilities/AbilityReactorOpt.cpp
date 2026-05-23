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

#include "AbilityReactorOpt.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*
Skill properties:
Description=The Reactor Optimization ability allows players to temporarily increase their Reactor Capacity and Reactor Regeneration Rate.
Desc_1=Enables the Reactor Boost ability.
Desc_2=Increases the Reactor Capacity of a player by 10%. Ability may only be applied to skill user.
Desc_3=Enables the Reactor Surge ability. Ability may only be applied to skill user.
Desc_4=Increases the amount of Reactor Capacity by 15%. Ability may only be applied to skill user.
Desc_5=Enables the Reactor Extenstion ability with a range of up to 3500 units.
Desc_6=Enables the Reactor Augmentation ability and increases range to 3750 units.
Desc_7=Enables the Reactor Optimization ability and increases range to 4000 units.

Range=1000,1000,1000,1000,3500,3750,4000,4000,4000
Energy=50,50,125,125,250,500,1000
Reactor Boost = Increases the Reactor Capacity of a player by 5%. Ability may only be applied to skill user.
Reactor Surge = Increases the Reactor Capacity of a player by 10% and their Reactor Regeneration Rate by 10%.
Reactor Extenstion = Increases the Reactor Capacity of a player or a friendly target by 20%.
Reactor Augmentation = Increases the Reactor Capacity of a player or a friendly target by 20% and their Reactor Regeneration Rate by 10%.
Reactor Optimization = Increases the Reactor Capacity of a player or a friendly target by 25%, increases their Reactor Regeneration Rate by 20%.


float smag = 0.0f;
float rmag = 0.0f;
bool self_only;
int range = 0;

switch(rank)
{
case 1:
	smag = 0.05f;
	rmag = 0.0f;
	resists = 0;
	self_only = true;
	break;
case 2:
	smag = 0.1f;
	rmag = 0.0f;
	resists = 0;
	self_only = true;
	break;
case 3:
	smag = 0.1f;
	rmag = 0.1f;
	resists = 0;
	self_only = true;
	break;
case 4:
	smag = 0.15f;
	rmag = 0.1f;
	resists = 0;
	self_only = true;
	break;
case 5:
	smag = 0.2f;
	rmag = 0.0f;
	resists = 0;
	self_only = false;
	break;
case 6:
	smag = 0.2f;
	rmag = 0.1f;
	resists = 0;
	self_only = false;
	break;
case 7:
	smag = 0.25f;
	rmag = 0.2f;
	resists = 10;
	self_only = false;
	break;
*/

/*
* This calculates the activation cost of the skill.
*/
float AReactorOptimisation::CalculateEnergy ( float SkillLevel, long SkillRank )
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
float AReactorOptimisation::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	//shorter casting time for lower rank with high skill.
	// 5 second default minus the difference between the skill rank and skill level (a negative to 0 value)
	// Capped at a .5 second minimum cast time.
	return 5.0f + ( ((float)SkillRank - SkillLevel) <= -5.0f ? -4.5f : ((float)SkillRank - SkillLevel) );
}

/*
* Calculate the maximum range this rank of the skill can be used at.
*/
float AReactorOptimisation::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	return 2500.0f + (SkillLevel-1) * 250.0f;
}

/*
* Computes the duration of the ability.
*
* Results are returned in seconds.
*/
float AReactorOptimisation::CalculateDuration ( float SkillLevel, long SkillRank )
{
	return SkillLevel * 90.0f;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long AReactorOptimisation::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case REACTOR_BOOST:
		return 1;
	case REACTOR_SURGE:
		return 3;
	case REACTOR_EXTENSION: 
		return 5;
	case REACTOR_AUGMENTATION:
		return 6;
	case REACTOR_OPTIMISATION: 
		return 7;
	default:
		return -1;
	}
}

long AReactorOptimisation::DetermineMinimumLevel(int SkillID)
{
	switch(SkillID)
	{
	case REACTOR_BOOST:
		return 0;
	case REACTOR_SURGE:
		return 10;
	case REACTOR_EXTENSION: 
		return 20;
	case REACTOR_AUGMENTATION:
		return 30;
	case REACTOR_OPTIMISATION: 
		return 40;
	default:
		return 51;
	}
}

bool AReactorOptimisation::SelfOnly()
{
	return m_SkillRank < 5;
}

// --------------------------------------------

bool AReactorOptimisation::CanUse(long TargetID, long AbilityID, long SkillID)
{
	CMob *p = GetPointerToCommon();
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
bool AReactorOptimisation::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	ObjectEffect ReactorOptEffect;
	memset(&ReactorOptEffect, 0, sizeof(ReactorOptEffect));		// Zero out memory
	
	// shield charging effect on player
	ReactorOptEffect.Bitmask = 3;
	ReactorOptEffect.TimeStamp = m_EffectID;
	ReactorOptEffect.EffectID = m_EffectID;
	ReactorOptEffect.EffectDescID = 719;
	ReactorOptEffect.GameID = p->GameID();
	p->SendObjectEffectRL(&ReactorOptEffect);
	
	return true;
}

/*
* This function is called when the SetTimer call returns.   
*/
bool AReactorOptimisation::Update(long activation_ID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	float rmag = 0.0f;
	float emag = 0.0f;
	
	// remove the effects used during the delay
	p->RemoveEffectRL(m_EffectID); 

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
		BeamEffect.EffectDescID = 736;
		p->SendObjectToObjectEffectRL(&BeamEffect);
	}

	long Duration = (long)(CalculateDuration(m_SkillLevel, m_SkillRank) * 1000.0f);
	int colour = -1;
	
	// Now lets figure out what buff to add
	Buff ReactorOptBuff;
	memset(&ReactorOptBuff, 0, sizeof(Buff));

	for(int i = 0; i < 5; i++)
	{
		ReactorOptBuff.EffectID[i] = -1;
	}

	switch(m_AbilityID)
	{
	case REACTOR_BOOST:
		emag = (m_SkillLevel == 1)? 0.05f : 0.1f;
		rmag = 0.0f;
		colour = 300; // green
		break;
	case REACTOR_SURGE:
		emag = (m_SkillLevel == 3)? 0.1f : 0.15f;
		rmag = 0.1f;
		colour = 60; // blue
		break;
	case REACTOR_EXTENSION:
		emag = 0.2f;
		rmag = 0.0f;
		colour = 120; // purple
		break;
	case REACTOR_AUGMENTATION:
		emag = 0.2f;
		rmag = 0.1f;
		colour = 240; // yellow
		break;
	case REACTOR_OPTIMISATION: 
		// Set stats for buff
		emag = 0.25f;
		rmag = 0.2f;
		colour = 210; // orange
		break;
	}
	ReactorOptBuff.EffectID[0] = 1192;
	strcpy_s(ReactorOptBuff.Stats[0].StatName, sizeof(ReactorOptBuff.Stats[0].StatName), STAT_ENERGY);
	ReactorOptBuff.Stats[0].StatName[sizeof(ReactorOptBuff.Stats[0].StatName)-1] = '\0';
	ReactorOptBuff.Stats[0].Value = emag;
	ReactorOptBuff.Stats[0].StatType = STAT_BUFF_MULT;

	if(rmag > 0.0f)
	{
		strcpy_s(ReactorOptBuff.Stats[1].StatName, sizeof(ReactorOptBuff.Stats[1].StatName), STAT_ENERGY_RECHARGE);
		ReactorOptBuff.Stats[1].StatName[sizeof(ReactorOptBuff.Stats[1].StatName)-1] = '\0';
		ReactorOptBuff.Stats[1].Value = rmag;
		ReactorOptBuff.Stats[1].StatType = STAT_BUFF_MULT;
		//reactor boost buff doesn't seem to work correctly
		/*strcpy_s(ReactorOptBuff.BuffType, sizeof(ReactorOptBuff.BuffType), "Reactor_Boost");
		ReactorOptBuff.BuffType[sizeof(ReactorOptBuff.BuffType)-1] = '\0';

		if(m_Target->m_Buffs.FindBuff("Reactor_Boost"))
		{
			m_Target->m_Buffs.RemoveBuff("Reactor_Boost");
		}
		m_Target->m_Buffs.AddBuff(&ReactorOptBuff,colour);*/
	}

	ReactorOptBuff.ExpireTime = GetNet7TickCount() + Duration;
	ReactorOptBuff.IsPermanent = false;
	strcpy_s(ReactorOptBuff.BuffType, sizeof(ReactorOptBuff.BuffType), "ReactorOpt_Regen");
	ReactorOptBuff.BuffType[sizeof(ReactorOptBuff.BuffType)-1] = '\0';
	
	if(m_Target->m_Buffs.FindBuff("ReactorOpt_Regen"))
	{
		m_Target->m_Buffs.RemoveBuff("ReactorOpt_Regen");
	}
	m_Target->m_Buffs.AddBuff(&ReactorOptBuff,colour);
	
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
bool AReactorOptimisation::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = true;
	*OnAction = true;

	return true;
}
