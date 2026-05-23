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

#include "AbilityMenace.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

class PlayerClass;

/*
The Menace skill is used to frighten enemies, and causing them to flee.
Deny Skills don't work yet. Otherwise if you disregard the success calculations
everything in skill should work.

With the lack of bravery settings the implementation is as follows:
THIS SCHEME WILL CHANGE WHEN BRAVERY ETC IS IN GAME.

base_chance = 50%

mob lvl < 15       = +50%
mob 15 <= lvl < 25 = +25%
mob 25 <= lvl < 40 = -25%
mob lvl => 40      = -50%

example : menace on a mob lvl 10 = 50% + 50%  = 100% success
                           lvl 23 = 50% + 25% = 75% success
						   lvl 35 = 50% - 25% = 25% success
						   lvl 50 = 50% - 50% = 0% success

Duration is 30 seconds
Cooldown time 60 seconds.

SkillRank 1 : EnergyCost 35 , Fear.
SkillRank 3 : EnergyCost 75 , Fear , Deny Skills.
SkillRank 5 : EnergyCost 150, Fear , Deny Skills, Deny Weapons.
SkillRank 6 : EnergyCost 300, Fear , Deny Skills, Deny Weapons. 
SkillRank 7 : EnergyCost 600, Fear , Deny Skills, Deny Weapons. 

SkillLevel 1: Range 2.5k.
SkillLevel 2: Range 2.75k.
SkillLevel 3: Range 3k.
SkillLevel 4: Range 3.25k.
SkillLevel 5: Range 3.5k.
SkillLevel 6: Range 3.75k. AOE 1.5k radius from target
SkillLevel 7: Range 4k.    AOE 3k radius from target. 
*/

/*
* This calculates the activation cost of the skill.
*/
float AMenace::CalculateEnergy ( float SkillLevel, long SkillRank )
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
float AMenace::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	return 2.0f;
}

/*
* Calculate the maximum range this rank of the skill can be used at.
  Used for current targets on not group range.
*/
float AMenace::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	return 2500.0f + (SkillLevel-1.0f)*250.0f;
}

/*
*  Calculate the Area of Effect
*/
float AMenace::CalculateAOE ( float SkillLevel, long SkillRank )
{
	float aoe = 0.0f;

	if ( SkillRank == 6 )
		aoe = 1500.0f;
	else if ( SkillRank == 7 )
		aoe = 3000.0f;
	else
		aoe = 0.0f;

	return aoe;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long AMenace::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case INTIMIDATE:
		return 1;
	case SCARE:
		return 3;
	case TERRIFY:
		return 5;
	case AREA_INTIMIDATE:
		return 6;
	case AREA_TERRIFY:
		return 7;
	default:
		return -1;
	}
}

// --------------------------------------------

bool AMenace::CanUse(long TargetID, long AbilityID, long SkillID)
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
bool AMenace::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	ObjectToObjectEffect MenaceEffect;
	memset(&MenaceEffect, 0, sizeof(MenaceEffect));	// Zero out memory
	
	if ( m_SkillRank == 1 )
		MenaceEffect.EffectDescID = 198;
	else if ( m_SkillRank == 3 )
		MenaceEffect.EffectDescID = 199;
	else if ( m_SkillRank == 5 )
		MenaceEffect.EffectDescID = 273;
	else if ( m_SkillRank == 5 )
		MenaceEffect.EffectDescID = 660;
	else
		MenaceEffect.EffectDescID = 662;

	MenaceEffect.Bitmask = 3;
	MenaceEffect.GameID = p->GameID();
	MenaceEffect.TimeStamp = m_EffectID;
	MenaceEffect.EffectID = m_EffectID;
	MenaceEffect.TargetID = m_Target->GameID();
	p->SendObjectToObjectEffectRL(&MenaceEffect);

	return true;
}

/*
* This function is called when the SetTimer call returns.
*/
bool AMenace::Update(long activation_ID)
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

	bool success = false;
	bool deny_weapons = false;
	bool deny_skills = false;

	// Menace Target
	if ( m_SkillRank > 1 )
		deny_skills = true;
	if ( m_SkillRank > 3 )
		deny_weapons = true;

	// Send effect if SkillRank > 1
	if ( m_SkillRank > 1 )
		success = m_Target->Menace(p, m_SkillRank, 30.0f,m_EffectID,deny_skills,deny_weapons);
	else
		success = m_Target->Menace(p, m_SkillRank, 30.0f,-1,false,false);
	// If menaced send effect
	if ( success && m_SkillRank > 1)
	{
		// Deny Effect. Object removes it.
		ObjectEffect DenyEffect;
		memset(&DenyEffect, 0, sizeof(DenyEffect));		// Zero out memory
		DenyEffect.Bitmask = 3;
		DenyEffect.TimeStamp = m_EffectID;
		DenyEffect.EffectDescID = 274;
		DenyEffect.EffectID = m_EffectID;
		DenyEffect.GameID = m_Target->GameID();
		p->SendObjectEffectRL(&DenyEffect);
	}

	// Menace area
	if ( m_SkillRank > 5 )
	{
		UseOnAllEnemiesInRange(true);
	}
	
	p->SetCurrentSkill();
	m_InUse = false;

	return true;
}

void AMenace::ProximityAOE(CMob *target, short seq, proxparam p1, proxparam p2, proxparam p3)
{
	CMob *p = GetPointerToCommon();
	bool success;
	// Menace target
	if ( m_SkillRank == 7 )
		success = target->Menace(p, m_SkillRank, 30.0f,m_EffectID+seq,true,true);
	else
		success = target->Menace(p, m_SkillRank, 30.0f,-1,false,false);
	// If menaced send effect
	if ( success && m_SkillRank == 7)
	{
		// Deny Effect. Object removes it.
		ObjectEffect DenyEffect;
		memset(&DenyEffect, 0, sizeof(DenyEffect));		// Zero out memory
		DenyEffect.Bitmask = 3;
		DenyEffect.TimeStamp = m_EffectID+seq;
		DenyEffect.EffectID = m_EffectID+seq;
		DenyEffect.EffectDescID = 274;
		DenyEffect.GameID = target->GameID();
		p->SendObjectEffectRL(&DenyEffect);
	}
}
