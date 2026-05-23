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

#include "AbilitySelfDestruct.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*

The skill inflicts massive explosive damage to the mobs in range of the blast.
The ship will then be incapacitated with drained energy and shields.
The player can't be jumpstarted and needs to get a tow to registered station.

There is a 10 second countdown and it can be interrupted by pressing warp or 
toggle selfdestruct button.
The skill has a 10 minute cooldown time.

The Menace part is implemented without skill deny and correct success calculations.

Skill properties:

Self_Destruct_1 :	Damage 90   * SkillLevel points , EnergyCost 35.
Self_Destruct_2 :	Damage 180  * SkillLevel points , EnergyCost 75.
Self_Destruct_3 :	Damage 540  * SkillLevel points , EnergyCost 150.
Self_Destruct_4 :	Damage 1440 * SkillLevel points , EnergyCost 300.
Self_Destruct_5 :	Damage 3780 * SkillLevel points , EnergyCost 600.

Range : SkillLevel 1 :	1k.
		SkillLevel 2 :	1.25k.
		SkillLevel 3 :	1.5k.
		SkillLevel 4 :	1.75k.
		SkillLevel 5 :	2k.
		SkillLevel 6 :	2.25k.
		SkillLevel 7 :	2.5k.
*/

ASelfDestruct::ASelfDestruct(CMob *me) : AbilityBase(me, STAT_SKILL_SELF_DESTRUCT)
{
	m_Revive=false;
	m_ReviveTime=0;
	m_CountDown=0;
}

/*
* Calculate EnergyCost.
* 
*/
float ASelfDestruct::CalculateEnergy ( float SkillLevel, long SkillRank )
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
* Compute how much time must pass between skill uses.
*
* Results are returned in seconds.
*/
float ASelfDestruct::CalculateCoolDownTime ( float SkillLevel, long SkillRank ) 
{
	return 600.0f;
}

/*
* Calculate the maximum range this rank of the skill can be used at.
*/
float ASelfDestruct::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	float range = 0.0f;

	if ( SkillLevel == 1 )
	{
		range = 1000.0f;
	}
	else
	{
		range = 1000.0f + ( ( SkillLevel -1 ) * 250.0f );
	}

	return range;
}

/*
* Calculate the explosive damage to inflict mob with.
*/
float ASelfDestruct::CalculateDamage ( float SkillLevel, long SkillRank ) 
{
	float damage_base = 0.0f;

	switch ( SkillRank )
	{
		case 1:
			damage_base = 90.0f;
			break;
		case 3:
			damage_base = 180.0f;
			break;
		case 5:
			damage_base = 540.0f;
			break;
		case 6:
			damage_base = 1440.0f;
			break;
		default:
			damage_base = 3780.0f;
			break;
	}
	
	return ( damage_base * SkillLevel ) ;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long ASelfDestruct::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case SELF_DESTRUCT_1:
		return 1;
	case SELF_DESTRUCT_2:
		return 3;
	case SELF_DESTRUCT_3:
		return 5;
	case SELF_DESTRUCT_4:
		return 6;
	case SELF_DESTRUCT_5:
		return 7;
	default:
		return -1;
	}
}

bool ASelfDestruct::CanUse(long TargetID, long AbilityID, long SkillID)
{
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx())
	{
		return false;
	}

	return true;	
}

/*
* This will be the first function called once the skill is determined
* as useable.
*/
bool ASelfDestruct::UseSkill(long ChargeTime)
{
	m_CountDown = 10;
	CMob *p = GetPointerToCommon();
	char buffer[100];
	sprintf_s(buffer,sizeof(buffer),"Selfdestruct in %d seconds!",m_CountDown); 
	p->SendPriorityMessageString(buffer,"MessageLine",1000,2);
	SetTimer(1000);
	m_CountDown--;
	
	return true;
}

/*
* This function is called when the SetTimer call returns.
*/
bool ASelfDestruct::Update(long activation_ID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}
	
	if ( m_CountDown )
	{
		// Menace mobs in blast radius 
		UseOnAllEnemiesInRange();

		if ( m_CountDown == 3 )
		{
			m_EffectID = GetNet7TickCount();
			ObjectEffect SelfDestructEffect;
			memset(&SelfDestructEffect, 0, sizeof(SelfDestructEffect));		// Zero out memory
			SelfDestructEffect.Bitmask = 3;
			SelfDestructEffect.GameID = p->GameID();
			SelfDestructEffect.TimeStamp = m_EffectID;
			SelfDestructEffect.EffectID = m_EffectID;
			SelfDestructEffect.EffectDescID = 205;
			p->SendObjectEffectRL(&SelfDestructEffect);
		}

		char buffer[100];
		sprintf_s(buffer,sizeof(buffer),"Selfdestruct in %d seconds!",m_CountDown); 
		p->SendPriorityMessageString(buffer,"MessageLine",1000,2);
		SetTimer((long)1000.0f);
		m_CountDown--;
		return true;
	}
	else
	{
		char buffer[100];
		sprintf_s(buffer,sizeof(buffer),"BOOOMM!"); 
		p->SendPriorityMessageString(buffer,"MessageLine",1000,2);
	}

	p->RemoveEffectRL(m_EffectID);

	// boom
	p->SelfDestruct();

	// Calculate what damage to inflict on mobs
	proxparam p1(1L);
	proxparam p2(CalculateDamage( m_SkillLevel , m_SkillRank ));
	UseOnAllEnemiesInRange(false,p1,p2);

	p->SetCurrentSkill();
	m_InUse = false;

	return true;
}

void ASelfDestruct::ProximityAOE(CMob *target, short seq, proxparam tag, proxparam damage, proxparam p3)
{
	CMob *p = GetPointerToCommon();
	if (tag.lng == 0)
	{
		// Menace enemy with deny skills and weapons
		target->Menace(p,m_SkillRank,30.0f,-1,true,true);
	}
	else
	{
		// Inflict the calculated explosive damage to mob
		target->DamageObject(p->GameID(),DAMAGE_EXPLOSIVE,damage.flt,0);
	}
}

/*
* What can interrupt this skill.
*/
bool ASelfDestruct::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = false;
	*OnAction = true;

	return true;
}
