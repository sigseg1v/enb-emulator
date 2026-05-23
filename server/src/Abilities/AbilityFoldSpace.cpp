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

#include "AbilityFoldSpace.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*
Skill properties:
lvl 1: Enables the Teleport Self ability. 5000 units
lvl 2: Increases teleportation range to 5300 units.
lvl 3: Increases teleportation range to 5600 units and enables the Teleport Enemy ability.
lvl 4: Increases teleportation range to 5900 units.
lvl 5: Increases teleportation range to 6200 units and enables the Teleport Friend ability.
lvl 6: Increases teleportation range to 6500 units and enables the Directional Teleport ability.
lvl 7: Increases teleportation range to 6800 units and enables the Area Teleport ability.
*/

/*
* This calculates the activation cost of the skill.
*/
float AFoldSpace::CalculateEnergy ( float SkillLevel, long SkillRank )
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
		return 200.0f;
	case 7:
		return 300.0f;
	}
	return 0.0f; // shouldnt happen
}

/*
* Calculate how much time must pass before the skill activates.
*
* Results are returned in seconds.
*/
float AFoldSpace::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	return 3.0f;
}

/*
* Calculate the maximum range this rank of the skill can be used at.
*/
float AFoldSpace::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	return SkillRank < 5 ? 1000.0f : 5000.0f + (SkillLevel-1) * 250.0f;
}

float AFoldSpace::CalculateTeleportDistance ( float SkillLevel, long SkillRank )
{
	CMob *p = GetPointerToCommon();
	return 5000.0f + (SkillLevel-1) * 300.0f + p->m_Stats.GetStat(STAT_FOLD_SPACE_DISTANCE); 
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long AFoldSpace::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case TELEPORT_SELF:
		return 1;
	case TELEPORT_ENEMY:
		return 3;
	case TELEPORT_FRIEND: // OR self
		return 5;
	case DIRECTIONAL_TELEPORT: // friend OR self
		return 6;
	case AREA_TELEPORT: // whole group
		return 7;
	default:
		return -1;
	}
}

bool AFoldSpace::IsUsedOnEnemies()
{
	return m_AbilityID == TELEPORT_ENEMY;
}

bool AFoldSpace::RequiresTarget()
{
	return m_AbilityID == TELEPORT_ENEMY || m_AbilityID == TELEPORT_FRIEND;
}

bool AFoldSpace::IsGroupSkill()
{
	return !IsUsedOnEnemies();
}

// --------------------------------------------

bool AFoldSpace::CanUse(long TargetID, long AbilityID, long SkillID)
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
bool AFoldSpace::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	ObjectEffect FoldSpaceEffect;
	memset(&FoldSpaceEffect, 0, sizeof(FoldSpaceEffect));		// Zero out memory
	
	FoldSpaceEffect.Bitmask = 3;
	FoldSpaceEffect.TimeStamp = m_EffectID;
	FoldSpaceEffect.EffectID = m_EffectID;
	FoldSpaceEffect.EffectDescID = 202;
	// vanish effect on self
	if (m_Target == p)
	{
		FoldSpaceEffect.GameID = p->GameID();
		p->SendObjectEffectRL(&FoldSpaceEffect);
	}
	else if (m_Target) // vanish effect on target
	{	
		ObjectToObjectEffect FoldSpaceEffect2;
		memset(&FoldSpaceEffect2, 0, sizeof(FoldSpaceEffect2));		// Zero out memory
	
		FoldSpaceEffect2.Bitmask = 3;
		FoldSpaceEffect2.TimeStamp = m_EffectID;
		FoldSpaceEffect2.EffectID = m_EffectID;
		FoldSpaceEffect2.EffectDescID = 667;
		FoldSpaceEffect2.GameID = p->GameID();
		FoldSpaceEffect2.TargetID = m_Target->GameID();
		p->SendObjectToObjectEffectRL(&FoldSpaceEffect2);
	}

	// gfx for group version
	if (m_AbilityID == AREA_TELEPORT && p->GroupID() != -1)
	{
		GetFriendlyGroup();
		for(int x=0;x<6;x++)
		{
			m_Target = m_AOEFriendList[x];
			if (m_Target)
			{
				FoldSpaceEffect.GameID = m_Target->GameID();
				FoldSpaceEffect.TimeStamp = m_EffectID+1+x;
				FoldSpaceEffect.EffectID = m_EffectID+1+x;
				p->SendObjectEffectRL(&FoldSpaceEffect);
			}
		}
	}

	return true;
}

/*
* This function is called when the SetTimer call returns.   
*/
bool AFoldSpace::Update(long activation_ID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	// do the fold work
	switch (m_AbilityID)
	{
	case TELEPORT_FRIEND: // OR self
	case DIRECTIONAL_TELEPORT: // friend OR self
		if (m_Target)
		{
			Fold();
			p->RemoveEffectRL(m_EffectID);
			break;
		}
		// drop through
	case TELEPORT_SELF:
		m_Target = p;
		Fold();
		p->RemoveEffectRL(m_EffectID);
		break;
	case TELEPORT_ENEMY:
		if (m_Target)
		{
			Fold();
			p->RemoveEffectRL(m_EffectID);
		}
		break;
	case AREA_TELEPORT: // whole group
		GetFriendlyGroup();
		for(int x=0;x<6;x++)
		{
			m_Target = m_AOEFriendList[x];
			if (m_Target)
			{
				Fold();
				p->RemoveEffectRL(m_EffectID+1+x);
			}
		}
		break;
	}

	m_InUse = false;
	p->SetCurrentSkill();

	return true;
}

/*
* Returns true in the case that this skill can be interrupted.
* What can interrupt the skill is returned inside the OnMotion 
*  and OnDamage pointers.
*/
bool AFoldSpace::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = false;
	*OnAction = true;

	return true;
}

// TODO: when mobs have deflects, test against psionic for a resist chance
void AFoldSpace::Fold()
{
	CMob *p = GetPointerToCommon();
	float distance = CalculateTeleportDistance(m_SkillLevel,m_SkillRank);

	if (m_SkillRank <= 5)
	{
		float angle = (float)(rand()%3141*2) * 0.001f;
		m_Target->MovePosition(cos(angle)*distance,sin(angle)*distance,0,false);
	}
	else
	{
		float *heading = p->Heading();
		m_Target->MovePosition(heading[0]*distance,heading[1]*distance,heading[2]*distance,false);
	}

	// materialise effect on this object
	ObjectEffect FoldSpaceEffect;
	memset(&FoldSpaceEffect, 0, sizeof(FoldSpaceEffect));		// Zero out memory
	
	FoldSpaceEffect.Bitmask = 3;
	FoldSpaceEffect.TimeStamp = m_EffectID;
	FoldSpaceEffect.EffectID = m_EffectID;
	FoldSpaceEffect.EffectDescID = 267;
	FoldSpaceEffect.GameID = m_Target->GameID();
	p->SendObjectEffectRL(&FoldSpaceEffect);
	SetObjectEffectTimer(m_EffectID, 1000);

	// type specific processing
	Player *player = GetTargetAsPlayer();
	if (player)
	{
		player->UpdatePlayerVisibilityList();
		player->SendLocationAndSpeed(true);
		player->UpdateVerbs();
		player->CheckObjectRanges();
		player->CheckNavs();
	}
	else
	{
		MOB *mob = GetTargetAsMOB();
		if (mob)
		{
			mob->UpdateObjectVisibilityList();
			mob->AddHate(p->GameID(),1);
		}
	}
}
