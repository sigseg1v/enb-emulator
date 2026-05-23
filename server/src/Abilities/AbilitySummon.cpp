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

#include "AbilitySummon.h"
#include "PlayerClass.h"
#include "MOBClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*
Skill properties:
lvl 1: Enables the Summon Enemy ability. 5000 units
lvl 2: Increases teleportation range to 5250 units.
lvl 3: Increases teleportation range to 5500 units and enables the Summon Friend ability.
lvl 4: Increases teleportation range to 5750 units.
lvl 5: Increases teleportation range to 6000 units and enables the Summon Group ability.
lvl 6: Increases teleportation range to 6250 units and enables the Summon Enemy Group ability.
lvl 7: Increases teleportation range to 6500 units and enables the Return Friend ability.
*/

/*
* This calculates the activation cost of the skill.
*/
float ASummon::CalculateEnergy ( float SkillLevel, long SkillRank )
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
float ASummon::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	return m_AbilityID == RETURN_FRIEND ? 0.0f : 4.5f;
}

/*
* Calculate the maximum range this rank of the skill can be used at.
*/
float ASummon::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	// MOB range
	if (IsUsedOnEnemies())
		return 5000.0f + (SkillLevel-1) * 250.0f;
	// player range
	return SkillRank == 7 ? 6000.0f : 250000.0f;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long ASummon::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case SUMMON_ENEMY:
		return 1;
	case SUMMON_FRIEND:
		return 3;
	case SUMMON_GROUP:
		return 5;
	case SUMMON_ENEMY_GROUP:
		return 6;
	case RETURN_FRIEND:
		return 7;
	default:
		return -1;
	}
}

bool ASummon::IsUsedOnEnemies()
{
	return m_SkillRank == 1 || m_SkillRank == 6;
}

bool ASummon::RequiresTarget()
{
	return m_AbilityID != SUMMON_GROUP && m_AbilityID != RETURN_FRIEND;
}

bool ASummon::IsGroupSkill()
{
	return !IsUsedOnEnemies();
}

// --------------------------------------------

bool ASummon::CanUse(long TargetID, long AbilityID, long SkillID)
{
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx(false) ||
		!AbilityBase::CanUseWithCurrentTarget(true))
	{
		return false;
	}

	return true;
}

/*
* Send confirmation to a player.
*/
void ASummon::Confirmation(bool Confirm, long AbilityID, long GameID)
{
	Player *p = GetPointerToPlayer();
	if (Confirm)
	{
		// return was accepted
		Execute();
	}
	else
	{
		// return was not accepted
		p->SendVaMessage("Return home was declined.");
		m_InUse = false;
		p->SetCurrentSkill();
		return;
	}
}

/*
* If the call to Confirmation() succeeds, this is called.
*/
void ASummon::Execute()
{
	Player *p = GetPointerToPlayer();
	Player *t = GetTargetAsPlayer();
	if (t)
	{
		t->DamageTradeCargo(0.5f);
		t->TowToBase();
	}
	else
		p->SendVaMessage("Return home bad target.");
	m_InUse = false;
	p->SetCurrentSkill();
}

/*
* This will be the first function called once the skill is determined
* as useable.
*/
bool ASummon::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	ObjectToObjectEffect SummonEffect;
	memset(&SummonEffect, 0, sizeof(SummonEffect));		// Zero out memory
	
	// 3 bar player effect
	SummonEffect.Bitmask = 3;
	SummonEffect.GameID = p->GameID();
	SummonEffect.TimeStamp = m_EffectID+1;
	SummonEffect.EffectID = m_EffectID+1;
	SummonEffect.TargetID = m_Target->GameID();
	SummonEffect.EffectDescID = 347;
	p->SendObjectToObjectEffectRL(&SummonEffect);
	p->RemoveEffectRL(m_EffectID);

	// target dissolve effect
	if (m_AbilityID == SUMMON_ENEMY || m_AbilityID == SUMMON_FRIEND)
	{
		SummonEffect.TimeStamp = m_EffectID;
		SummonEffect.EffectID = m_EffectID;
		SummonEffect.EffectDescID = 344;
		p->SendObjectToObjectEffectRL(&SummonEffect);
	}

	// gfx for group summon
	if (m_AbilityID == SUMMON_GROUP)
	{
		GetFriendlyGroup();
		for(int x=0;x<6;x++)
		{
			// Send to everyone but ourself!
			m_Target = m_AOEFriendList[x];
			if (m_Target && m_Target != p)
			{
				// target dissolve effect to all
				SummonEffect.TimeStamp = m_EffectID+2+x;
				SummonEffect.EffectID = m_EffectID+2+x;
				SummonEffect.EffectDescID = 344;
				SummonEffect.TargetID = m_Target->GameID();
				p->SendObjectToObjectEffectRL(&SummonEffect);
			}
		}
	}
	if (m_AbilityID == SUMMON_ENEMY_GROUP)
	{
		GetEnemyGroup();
		for(int x=0;x<6;x++)
		{
			m_Target = m_AOEEnemyList[x];
			if (m_Target)
			{
				// target dissolve effect to all
				SummonEffect.TimeStamp = m_EffectID+2+x;
				SummonEffect.EffectID = m_EffectID+2+x;
				SummonEffect.EffectDescID = 344;
				SummonEffect.TargetID = m_Target->GameID();
				p->SendObjectToObjectEffectRL(&SummonEffect);
			}
		}
	}

	// do recall immediately and defer the rest until the dissolve is done
	if (m_AbilityID == RETURN_FRIEND)
	{
		Player *t = GetTargetAsPlayer();
		if (t && t != p)
			t->SendConfirmation("Do you wish to return to your registered station? Cargo items will take 50% damage.", p->GameID(), m_AbilityID);
		else
			Execute();
	}
	
	return true;
}

/*
* This function is called when the SetTimer call returns.   
*/
bool ASummon::Update(long activation_ID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	// do the summon work
	switch (m_AbilityID)
	{
	case SUMMON_ENEMY_GROUP:
		GetEnemyGroup();
		for(int x=0;x<6;x++)
		{
			m_Target = m_AOEEnemyList[x];
			if (m_Target)
			{
				SummonEnemy(x>0);
				p->RemoveEffectRL(m_EffectID+2+x);
			}
		}
		break;
	case SUMMON_ENEMY:
		SummonEnemy(false);
		p->RemoveEffectRL(m_EffectID);
		break;
	case SUMMON_FRIEND:
		SummonFriend(1);
		p->RemoveEffectRL(m_EffectID);
		break;
	case SUMMON_GROUP:
		GetFriendlyGroup();
		for(int x=0;x<6;x++)
		{
			m_Target = m_AOEFriendList[x];
			// Send to everyone but ourself!
			if (m_Target && m_Target != p)
			{
				SummonFriend(x+1);
				p->RemoveEffectRL(m_EffectID+2+x);
			}
		}
		break;
	case RETURN_FRIEND:
		// not done here
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
bool ASummon::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = false;
	*OnAction = true;

	return true;
}

// TODO: when mobs have deflects, test against psionic for a resist chance
void ASummon::SummonEnemy(bool randomise_position)
{
	CMob *p = GetPointerToCommon();
	if (m_Target)
	{
		float *heading = p->Heading();

		SummonObject(m_Target);
		if (randomise_position)
			m_Target->MovePosition((float)(rand()%1000),(float)(rand()%1000),(float)(rand()%1000),false);
		else
			m_Target->MovePosition(heading[0]*500.0f,heading[1]*500.0f,heading[2]*500.0f,false);
		FinaliseSummon();
	}
}

void ASummon::SummonFriend(int offset)
{
	if (m_Target)
	{
		// copied from HandleGoto
		SummonObject(m_Target);
		m_Target->MovePosition(0,0,(float)offset*50.0f,false);
		FinaliseSummon();
	}
}

void ASummon::SummonObject(Object *obj)
{
	CMob *p = GetPointerToCommon();
	obj->SetPosition(p->Position());

	// materialise effect on this object
	ObjectEffect FoldSpaceEffect;
	memset(&FoldSpaceEffect, 0, sizeof(FoldSpaceEffect));		// Zero out memory
	
	FoldSpaceEffect.Bitmask = 3;
	FoldSpaceEffect.TimeStamp = m_EffectID;
	FoldSpaceEffect.EffectID = m_EffectID;
	FoldSpaceEffect.EffectDescID = 267;
	FoldSpaceEffect.GameID = obj->GameID();
	p->SendObjectEffectRL(&FoldSpaceEffect);
	p->RemoveEffectRL(m_EffectID);
}

void ASummon::FinaliseSummon()
{
	CMob *p = GetPointerToCommon();
	Player *t = GetTargetAsPlayer();
	if (t)
	{
		t->UpdatePlayerVisibilityList();
		t->SendLocationAndSpeed(true);
		t->UpdateVerbs();
		t->CheckObjectRanges();
		t->CheckNavs();
	}
	else
	{
		MOB *Mob = GetTargetAsMOB();
		if (Mob)
		{
			Mob->SetVelocity(0);
			Mob->FaceObject(p);
			Mob->UpdateObjectVisibilityList();
			Mob->AddHate(p->GameID(),1);
		}
	}
}
