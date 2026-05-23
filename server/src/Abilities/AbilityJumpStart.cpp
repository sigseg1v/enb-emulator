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

#include "AbilityJumpStart.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*
Skill properties:
lvl 1: 7%, 1K
lvl 2: 14%, 1.25K
lvl 3: 21%, 1.5K
lvl 4: 28%, 1.75K
lvl 5: 35%, 2K
lvl 6: 42%, 2.25K
lvl 7: 49%, 2.5K
*/

/*
* This calculates the activation cost of the skill.
*/
float AJumpStart::CalculateEnergy ( float SkillLevel, long HullLevel )
{
	float EnergyCost = 0.0f;
	
	switch (HullLevel)
	{
	case 1:
		EnergyCost = 35.0f;
		break;
	case 2:
		EnergyCost = 50.0f;
		break;
	case 3:
		EnergyCost = 75.0f;
		break;
	case 4:
		EnergyCost = 100.0f;
		break;
	case 5:
		EnergyCost = 150.0f;
		break;
	case 6:
		EnergyCost = 300.0f;
		break;
	case 7:
		EnergyCost = 600.0f;
		break;
	}

	if (HullLevel > SkillLevel)
	{
		EnergyCost *= (float)(HullLevel - SkillLevel + 1);
	}

	//TO-DO: implement buffs
	//EnergyCost = 
	//	((1.0f - p->m_Stats.GetStatType(, STAT_BUFF_MULT)) * EnergyCost) -
	//	p->m_Stats.GetStatType(, STAT_BUFF_VALUE);

	//EnergyCost < 0.0f ? 0.0f : EnergyCost;

	return EnergyCost; 
}

/*
* Calculate how much time must pass before the skill activates.
*
* Results are returned in seconds.
*/
float AJumpStart::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	float ChargeTime = 7.0f;

	//TO-DO: implement buffs
	//ChargeTime = 
	//	((1.0f - p->m_Stats.GetStatType(, STAT_BUFF_MULT)) * ChargeTime) -
	//	p->m_Stats.GetStatType(, STAT_BUFF_VALUE);

	//ChargeTime < 0.0f ? 0.0f : ChargeTime;

	return ChargeTime; 
}

/*
* Calculate the maximum range this rank of the skill can be used at.
*/
float AJumpStart::CalculateRange ( float SkillLevel, long ) 
{
	//TO-DO: implement buffs
	return 1000.0f + 250.0f * (SkillLevel-1); 
}

float AJumpStart::CalculateHullRepair ( float SkillLevel )
{
	const float mult[8] = {0.4f, 0.65f, 1.33f, 5.0f, 19.33f, 80.0f, 340.0f, 1000.0f}; // extrapolate a "+skill bonus" amount
	int Skill = (int)(SkillLevel - 0.95f);
	if (Skill > 7)
		Skill = 7;
	return 30.0f * SkillLevel * mult[Skill]; 
}

// --------------------------------------------

bool AJumpStart::CanUse(long TargetID, long AbilityID, long SkillID)
{
	Player *p = GetPointerToPlayer();
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx(true,true,false,false) ||
		!AbilityBase::CanUseWithCurrentTarget())
	{
		return false;
	}
	Player *t = GetTargetAsPlayer();
	// target self if necessary
	if (!t)
	{
		t = p;
		m_SkillRank = p->PlayerIndex()->RPGInfo.GetHullUpgradeLevel()+1;
	}
	else
		m_SkillRank = t->PlayerIndex()->RPGInfo.GetHullUpgradeLevel()+1;

	// See if player selfdestructed
	if (t->GetSelfDestructed())
	{
		SendError("Target can't be jumpstarted, has selfdestructed and needs tow");
		return false;
	}

	// See if player is busy
	if (t->ConfirmationBusy())
	{
		SendError("Player is busy, try again later");
		return false;
	}

	if (!t->GetIsIncapacitated())
	{
		SendError("Target doesnt need a jumpstart");
		return false;
	}

	// need more skill (includes self!)
	if (m_SkillRank > (long)(m_SkillLevel + 2.05f))		
	{
		SendError("You need more skill to jumpstart this player");
		return false;
	}

	return true;
}

/*
* Send confirmation to a player.
*/
void AJumpStart::Confirmation(bool Confirm, long AbilityID, long GameID)
{
	CMob *p = GetPointerToCommon();
	if (Confirm)
	{
		// Jumpstart was accepted
		ChangeTarget(GameID);
		Execute();
	}
	else
	{
		// Jumpstart was not accepted
		p->SendVaMessage("Jump start was declined");
	}
}

/*
* If the call to Confirmation() succeeds, this is called.
*/
void AJumpStart::Execute()
{
	Player *Target = GetTargetAsPlayer();
	if (Target)
	{
		Target->JumpStart(CalculateHullRepair(m_SkillLevel), m_SkillLevel);
	}
}

bool AJumpStart::UseSkill(long ChargeTime)
{
	Player *p = GetPointerToPlayer();

	/* Activate effect */
	if (m_Target == p)
	{
		ObjectEffect JumpStart;
		memset(&JumpStart, 0, sizeof(JumpStart));		// Zero out memory

		JumpStart.Bitmask = 0x03;
		JumpStart.GameID = p->GameID();
		JumpStart.EffectDescID = 411;	// Jumpstart Self
		JumpStart.EffectID = m_EffectID;
		JumpStart.TimeStamp = m_EffectID;
		
		p->SendObjectEffectRL(&JumpStart);
	}
	else
	{
		ObjectToObjectEffect JumpStart;
		memset(&JumpStart, 0, sizeof(JumpStart));		// Zero out memory

		JumpStart.Bitmask = 0x03;
		JumpStart.GameID = p->GameID();
		JumpStart.TargetID = m_Target->GameID();
		JumpStart.EffectDescID = 414;	// Jumpstart Others Effect
		JumpStart.EffectID = m_EffectID;
		JumpStart.TimeStamp = m_EffectID;
		//JumpStart.Duration = 500;

		p->SendObjectToObjectEffectRL(&JumpStart);
	}

	return true;
}

bool AJumpStart::Update(long activation_ID)
{
	Player *p = GetPointerToPlayer();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	/* Remove effect */
	p->RemoveEffectRL(m_EffectID);

	// chance of success?
	long skill = (long)(m_SkillLevel+0.5f);
	int chance = 100;

	if (m_SkillRank == skill+1) // 75%
		chance = 75;
	else if (m_SkillRank == skill+2) // 50%
		chance = 50;
	if (rand()%100 >= chance)
	{
		SendError("Jumpstart failed, try again");
	}
	else
	{
		// Send a message to other player
		// Target must be a player
		if (m_Target && m_Target != p)
		{
			char msg[512];
			// Create a message
			sprintf_s(msg, 512, "Do you want to accept a jumpstart level %d from %s", skill, p->Name());
			// Send a message & wait for a reply
			GetTargetAsPlayer()->SendConfirmation(msg, p->GameID(), m_AbilityID);
		}
		else
		{
			// Jumpstart ourself
			Execute();
		}
	}

	p->SetCurrentSkill();
	m_InUse = false;

	return true;
}

/*
* Returns true in the case that this skill can be interrupted.
* What can interrupt the skill is returned inside the OnMotion 
*  and OnDamage pointers.
*/
bool AJumpStart::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = true;
	*OnAction = true;
	return true;
}
