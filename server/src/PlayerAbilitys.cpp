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
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"
#include <net7/Opcodes.h>

void Player::HandleSkillAbility(unsigned char *data)
{
    SkillUse * Action = (SkillUse *) data;

	//SendVaMessage("Skill id %d just activated.", Action->AbilityIndex);

	// TODO: Add code here
	long base_skill;
	long level;

	base_skill = ConvertAbilityToBaseSkill(level, Action->AbilityIndex);

	if (base_skill >= 0)
	{
		//check mission completion
		CheckMissionSkillUse(base_skill, level);
	}

	// Execute ability
	// See if we are in range ID for the ability
	// And we have a ability handeler
	int TargetID = ShipIndex()->GetTargetGameID();

	if (Action->AbilityIndex < MAX_ABILITY_IDS && Action->AbilityIndex >= 0 && m_AbilityList[Action->AbilityIndex])
	{
		__try
		{
			if (m_CurrentSkill && m_CurrentSkill != m_AbilityList[Action->AbilityIndex])
				SendPriorityMessageString("Error: Another skill is active, please wait or cancel it","MessageLine",1000,4);
			else
			// Ask if we can use this on this player
			if (m_AbilityList[Action->AbilityIndex]->CanUse(TargetID, Action->AbilityIndex, base_skill))
			{
				//mark skill as the skill currently being used.
				SetCurrentSkill(m_AbilityList[Action->AbilityIndex]);

				// If so lets execute it!
				if (!m_AbilityList[Action->AbilityIndex]->Use(TargetID))
				{
					SendPriorityMessageString("Error: This ability is not activating","MessageLine",1000,4);
				}
			}
		}
		__except(EXCEPTION_EXECUTE_HANDLER)
		{
			LogMessage("skill use %d crashed\n", Action->AbilityIndex);
		}
	}
	else
	{
		SendPriorityMessageString("Error: This ability is not yet working. Try later!","MessageLine",1000,4);
	}
}

//we need a relational database to convert the used skill to base skill and level
//until then, this is a hardcoded system that should suffice for now

long Player::ConvertAbilityToBaseSkill(long &level, long ability)
{
	long base_skill = g_ServerMgr->m_SkillsList.GetBaseSkillID(ability);
	char *desc = g_ServerMgr->m_SkillsList.GetSkillDescription(ability);
	level = g_ServerMgr->m_SkillsList.GetSkillLevel(ability);

	if (desc)
	{
		//SendVaMessage("Skill usage: %s", desc);
	}

	return base_skill;
}
