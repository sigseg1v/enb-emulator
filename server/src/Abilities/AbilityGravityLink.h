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
#ifndef _ABILITY_AGRAVITYLINK_H_INCLUDED_
#define _ABILITY_AGRAVITYLINK_H_INCLUDED_

#include "AbilityBase.h"
#include "ServerManager.h"
#include "ObjectClass.h"
#include "PlayerClass.h"

class AGravityLink : public AbilityBase
{
public:
	AGravityLink(CMob *me) : AbilityBase(me, STAT_SKILL_GRAVITY_LINK) {}
public:
	bool UseSkill(long ChargeTime);	
	bool CanUse(long TargetID, long AbilityID, long SkillID);
	bool Update(long activation_ID);

	bool SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction);
	bool Interrupts(int Type) { return (Type == OTHER || Type == WARPING || Type == INCAPACITATE); };

	// Calculate skill level Data
private:
	float CalculateEnergy ( float SkillLevel, long SkillRank );
	float CalculateChargeUpTime ( float SkillLevel, long SkillRank );
	float CalculateRange ( float SkillLevel, long SkillRank );
	float CalculateAOE ( float SkillLevel, long SkillRank );
	long  DetermineSkillRank(int SkillID);
	float CalculateGravityForce( float SkillLevel, long SkillRank );
	void ProximityAOE(CMob *target, short seq, proxparam force, proxparam effect1, proxparam effect2);
};

#endif