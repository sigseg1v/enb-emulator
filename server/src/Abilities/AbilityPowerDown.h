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
#ifndef _ABILITY_POWERDOWN_H_INCLUDED_
#define _ABILITY_POWERDOWN_H_INCLUDED_

#include "AbilityBase.h"
#include "PlayerClass.h"

//Powerdown Skill

class APowerDown : public AbilityBase
{
public:
	APowerDown(CMob *me);
public:
	bool UseSkill(long ChargeTime);
	bool CanUse(long TargetID, long AbilityID, long SkillID);
	bool Update(long activation_ID);

	bool SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction);
	bool InterruptSkillOnMotion(float Speed);
	bool InterruptSkillOnAction(int Type);

	// Calculate skill level Data
private:
	float CalculateEnergy ( float SkillLevel, long SkillRank );
	float CalculateEnergyPerSecond (long SkillRank );
	float CalculateCoolDownTime ( float SkillLevel, long SkillRank );
	float CalculatePowerDownLevel ( long SkillLevel );
	long DetermineSkillRank(int SkillID);
	char *APowerDown::GetBuffName();
	bool IsToggleSkill() { return true; };

	void RemoveBuff(Player *p);
	void ApplyBuff(Player *p, float shieldFactor, float energyFactor);
	// factor is a % change to normal regen rate
	void ModifyShieldRegen(float factor = 1.0f); 
	void ModifyReactorRegen(float factor = 1.0f);

	float m_OldShieldRegen;
	float m_OldReactorRegen;
	int m_BuffID;
};

#endif