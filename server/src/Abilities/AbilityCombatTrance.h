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
#ifndef _ABILITY_COMBAT_TRANCE_H_INCLUDED_
#define _ABILITY_COMBAT_TRANCE_H_INCLUDED_

#include "AbilityBase.h"
#include "CMobBuffs.h"

class ACombatTrance : public AbilityBase
{
public:
	ACombatTrance(CMob * me);
public:
	bool Update(long activation_ID);
	bool Use(long TargetID);

	// Calculate skill level Data
private:
	void CreateTranceBuff();			// Creates the trance buff in m_TranceBuff
	void SetTranceTimer(long Duration);
	bool IsUsedOnEnemies()  { return false; };
	bool IsToggleSkill() { return true; };	

	Buff m_TranceBuff;
	bool m_InTrance;
	long m_Expire;
};

#endif
