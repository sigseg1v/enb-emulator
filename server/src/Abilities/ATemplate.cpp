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
#if 0

#include "ATemplate.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

//**************************************************************
//NOTE!!! use p->SetCurrentSkill(); to flag the skill as unused when you are m_InUse is false.
//  p->SetCurrentSkill(); is set just before the Use() function is called, be sure to clear it 
//  in any code after that function call.
//**************************************************************
/*
Skill properties:
lvl 1: 
lvl 2: 
lvl 3: 
lvl 4: 
lvl 5: 
lvl 6: 
lvl 7: 
*/

/*
* This calculates the activation cost of the skill.
*/
float ATemplate::CalculateEnergy ( float SkillLevel, long SkillRank )
{
	return SkillRank * 5.0f;
}

/*
* Calculate how much time must pass before the skill activates.
*
* Results are returned in seconds.
*/
float ATemplate::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	return SkillLevel < 6.0f ? 8.0f - SkillLevel : 3.0f;
}

/*
* Calculate the maximum range this rank of the skill can be used at.
*/
float ATemplate::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	return 0.0f; 
}

/*
* Computes the AoE per skill level for an ability.
*/
float ATemplate::CalculateAOE ( float SkillLevel, long SkillRank )
{
	return 1000.0f + 0.0f * SkillLevel;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long ATemplate::DetermineSkillRank(int SkillID)
{
	/*switch(SkillID)
	{
	case :
		return 1;
	case :
		return 3;
	case :
		return 5;
	case :
		return 6;
	case ;
		return 7;
	default:
		return -1;
	}*/

	//REMOVE ME
	return -1;
}

// --------------------------------------------

bool ATemplate::CanUse(long TargetID, long AbilityID, long SkillID)
{
	CMob *p = GetPointerToMyCommonClass();
	// Get the skill level
	m_SkillLevel = (float) p->PlayerIndex()->RPGInfo.Skills.Skill[SkillID].GetLevel();
	m_SkillRank = DetermineSkillRank(AbilityID);
	m_AbilityID = AbilityID;
	m_SkillID = SkillID;
	ObjectManager *om = p->GetObjectManager();

	if(m_SkillRank > m_SkillLevel)
	{
		return false;
	}

	//Skill does not exist.
	if(m_SkillRank == -1)
	{
		return false;
	}

	// Make sure we are not dead
	if (p->ShipIndex()->GetIsIncapacitated())
	{
		SendError("Can not use this ability while dead!");
		return false;
	}

	// See if we can use this skill
	if (TargetID > 0 && m_SkillRank >= 3 && om)
	{
		Object * Target = om->GetObjectFromID(TargetID);	// Get Target

		//TODO: Update target types
		if (!Target || Target && Target->ObjectType() != OT_MOB)
		{
			SendError("Incorrect target type!");
			return false;
		}

		// See if we are in range
		if (Target->RangeFrom(p) > CalculateRange(m_SkillLevel, m_SkillRank))
		{
			SendError("Out of range!");
			return false;
		}
	}

	//If we are prospecting we cannot use this skill
	if (p->Prospecting())
	{
		SendError("Cannot use while prospecting.");
		return false;
	}

	//if we are warping we cannot use the skill
	if (p->WarpDrive())
	{
		SendError("Cannot use while in warp.");
		return false;
	}

	return true;
}


/*
* Send confirmation to a player.
*/
void ATemplate::Confirmation(bool Confirm, long AbilityID, long GameID)
{
	//
}

/*
* If the call to Confirmation() succeeds, this is called.
*/
void ATemplate::Execute()
{
	//
}

/*
* This will be the first function called once the skill is determined
* as useable.
*/
bool ATemplate::Use(long TargetID)
{
	CMob *p = GetPointerToMyCommonClass();
	//allow the ability to be toggled off.
	if(m_InUse)
	{
		InterruptSkillOnAction(OTHER); //To-DO: change me
		return false;
	}

	//grab a number for the effectID & timestamp
	m_EffectID = m_SkillActivationID = GetNet7TickCount();

	//REMOVE ME
	return false;
}

/*
* This function is called when the SetTimer call returns.
*/
void ATemplate::Update(long activation_ID)
{
	if(activation_ID != m_SkillActivationID)
	{
		return;
	}
	//
}

/*
* Returns true in the case that this skill can be interrupted.
* What can interrupt the skill is returned inside the OnMotion 
*  and OnDamage pointers.
*/
bool ATemplate::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = true;
	*OnAction = true;

	return true;
}

/*
* Determines whether or not this skill was interrupted based on damage taken.
*/
bool ATemplate::InterruptSkillOnDamage(float Damage)
{
	CMob *p = GetPointerToMyCommonClass();
	ObjectEffect InterruptEffect;

	if(!m_InUse) //cannot interrupt non-active skill.
	{
		p->SetCurrentSkill();
		return false;
	}

	if(Damage <= 0.0f)
	{
		Damage = 0.0f;
		return false;
	}
	
	m_DamageTaken += Damage;
	if(m_DamageTaken > GetInterruptThreshHold())
	{
		//remove current effect
		p->RemoveEffectRL(m_EffectID);

		//get new effectID
		m_EffectID = GetNet7TickCount();

		InterruptEffect.Bitmask = 3;
		InterruptEffect.GameID = p->GameID();
		InterruptEffect.EffectDescID = 735;
		InterruptEffect.EffectID = m_EffectID;
		InterruptEffect.TimeStamp = m_EffectID;

		p->SendObjectEffect(&InterruptEffect);

		p->RemoveEffectRL(m_EffectID);
		p->SetCurrentSkill();

		m_InUse = false;
		m_DamageTaken = 0;
		return true;
	}

	return false;
}

/*
* Determines whether or not this skill was interrupted based on current motion.
*
* Note: This refers to impulse motion. Warp is handled by OnAction.
*/
bool ATemplate::InterruptSkillOnMotion(float Speed)
{
	CMob *p = GetPointerToMyCommonClass();
	ObjectEffect InterruptEffect;

	if(!m_InUse) //skill not in use, cannot interrupt.
	{
		p->SetCurrentSkill();
		return false;
	}

	//remove current effect
	p->RemoveEffectRL(m_EffectID);

	//get new effectID
	m_EffectID = GetNet7TickCount();

	InterruptEffect.Bitmask = 3;
	InterruptEffect.GameID = p->GameID();
	InterruptEffect.EffectDescID = 735;
	InterruptEffect.EffectID = m_EffectID;
	InterruptEffect.TimeStamp = m_EffectID;

	p->SendObjectEffect(&InterruptEffect);

	p->RemoveEffectRL(m_EffectID);

	m_InUse = false;
	m_DamageTaken = 0;

	//REMOVE ME
	return false;
}

/*
* Returns true if this skill was interrupted based on player action.
*
* Action = Shooting guns, using devices or activated effects, starting to warp,
*  anything except basic impulse or opening windows on the player's client.
*/
bool ATemplate::InterruptSkillOnAction(int Type)
{
	CMob *p = GetPointerToMyCommonClass();
	if(!m_InUse)
	{
		p->SetCurrentSkill();
		m_DamageTaken = 0;
		return false;
	}

	//Remove skill effect
	p->RemoveEffectRL(m_EffectID);

	//clear current skill pointer
	p->SetCurrentSkill();

	//mark skill as not in use
	m_InUse = false;
	m_DamageTaken = 0;

	return true;
}

#endif