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
#ifndef _ABILITY_CLOAK_H_INCLUDED_
#define _ABILITY_CLOAK_H_INCLUDED_

#include "AbilityBase.h"

class ACloak : public AbilityBase
{
public:
	ACloak(CMob * me);

public:
	bool UseSkill(long ChargeTime);
	bool CanUse(long TargetID, long AbilityID, long SkillID);
	bool Update(long activation_ID);	

	bool SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction);
	bool InterruptSkillOnDamage(float Damage);	
	bool InterruptSkillOnMotion(float Speed);	
	bool InterruptSkillOnAction(int Type);	

	// Calculate skill level Data
private:
	float CalculateEnergy ( float SkillLevel, long SkillRank );
	float CalculateEnergyPerSecond (long SkillRank );
	float CalculateChargeUpTime ( float SkillLevel, long SkillRank );
	float CalculateAOE ( float SkillLevel, long SkillRank );
	float CalculateStealthLevel ( float SkillLevel );
	long DetermineSkillRank(int SkillID);
	bool IsUsedOnEnemies()  { return false; };
	bool SelfOnly()			{ return true; };
	bool IsGroupSkill()		{ return true; };
	bool IsToggleSkill()	{ return true; };
	void EnableHalfSpeed(bool answer = true);
	void EnableDoubleDamage(bool answer = true);
	void SetIsCloaked(bool on);
	void ApplyReduceSigBuffTo(CMob *p, bool remove);
	bool CheckGroupCloaks(bool on);

	bool m_DoubleDamageActive;
	bool m_WholeGroup;
	int m_BuffID;
};


#endif