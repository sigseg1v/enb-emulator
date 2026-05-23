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
#ifndef _ABILITY_TEMPLATE_H_INCLUDED_
#define _ABILITY_TEMPLATE_H_INCLUDED_

#include "AbilityBase.h"

class ATemplate : public AbilityBase
{
public:
	ATemplate(CMob *me) { Init(me); };

public:
	bool Use(long TargetID);						// When a player trys to use an ability
	void Confirmation(bool Confirm, long AbilityID, long GameID);			// Used when a conformation is needed
	void Execute();								// Used when ability can be used on other players
	bool CanUse(long TargetID, long AbilityID, long SkillID);	// Used to see if player can use this ability
	void Update(long activation_ID);			// Updated by a timer

	bool SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction);	//Can this skill be interrupted by anything?
	bool InterruptSkillOnDamage(float Damage);		//Returns if this skill was interrupted based on damage taken
	bool InterruptSkillOnMotion(float Speed);		//Returns if this skill was interrupted based on current motion.
	bool InterruptSkillOnAction(int Type);

	// Calculate skill level Data
private:
	float CalculateEnergy ( float SkillLevel, long SkillRank );
	float CalculateChargeUpTime ( float SkillLevel, long SkillRank );
	float CalculateRange ( float SkillLevel, long SkillRank );
	float CalculateAOE ( float SkillLevel, long SkillRank );
	long DetermineSkillRank(int SkillID);
};

#endif