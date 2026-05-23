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

#include "AbilityRally.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*
* This calculates the activation cost of the skill.
*/
float ARally::CalculateEnergy ( float SkillLevel, long SkillRank )
{
	switch(SkillRank)
	{
		case 1:
			return 35.0f;
		case 3:
			return 75.0f;
		case 5:
			return 150.0f;
		case 7:
			return 300.0f;
		default:
			return 300.0f;
	}
}

/*
* Calculate the maximum range this rank of the skill can be used at.
*/
float ARally::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	//TO-DO: implement buffs
	return 1000.0f + 250.0f * SkillLevel; 
}

/*
* Computes the AoE per skill level for an ability.
*/
float ARally::CalculateDuration ( float SkillLevel, long SkillRank )
{
	// Give them all 10 mins
	return 600.0f;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long ARally::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
		case DAMAGE_TACTICS:
			return 1;
		case DEFENSE_TACTICS:
			return 3;
		case FIRING_TACTICS:
			return 5;
		case STEALTH_TACTICS:
			return 7;
	}
	return -1;
}

// --------------------------------------------

bool ARally::CanUse(long TargetID, long AbilityID, long SkillID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx(false,false))
	{
		return false;
	}

	return true;
}

bool ARally::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	// See if we have the buff allready
	switch(m_SkillRank)
	{
	case 1:
		if (p->m_Buffs.FindBuff("Damage_Tactics"))
			return false;
		break;
	case 3:
		if (p->m_Buffs.FindBuff("Defense_Tactics"))
			return false;
		break;
	case 5:
		if (p->m_Buffs.FindBuff("Firing_Tactics"))
			return false;
		break;
	case 7:
		if (p->m_Buffs.FindBuff("Stealth_Tactics"))
			return false;
		break;
	}

	float range = CalculateRange(m_SkillLevel, m_SkillRank);

	// First buff self then everyone else in group if it exists
	GetFriendlyGroup();
	for(int x=0;x<6;x++)
	{
		m_Target = m_AOEFriendList[x];
		if (m_Target)
			CreateRallyBuff(m_Target);
	}
	
	return true;
}

void ARally::CreateRallyBuff(CMob* recipient)
{
	long Duration = (long)(CalculateDuration(m_SkillLevel, m_SkillRank)*1000.0f);
	int statcounter = 0;

	Buff RallyBuff;
	memset(&RallyBuff, 0, sizeof(Buff));

	for(int i = 0; i < 5; i++)
	{
		RallyBuff.EffectID[i] = -1;
	}
	RallyBuff.ExpireTime = GetNet7TickCount() + Duration;
	RallyBuff.IsPermanent = false;

	if(recipient->m_Buffs.FindBuff("Stealth_Tactics"))
		if(m_SkillRank == 7)
			recipient->m_Buffs.RemoveBuff("Stealth_Tactics");
		else
			return;
	if(recipient->m_Buffs.FindBuff("Firing_Tactics"))
		if(m_SkillRank >= 5)
			recipient->m_Buffs.RemoveBuff("Firing_Tactics");
		else
			return;
	if(recipient->m_Buffs.FindBuff("Defense_Tactics"))
		if(m_SkillRank >= 3)
			recipient->m_Buffs.RemoveBuff("Defense_Tactics");
		else
			return;
	if(recipient->m_Buffs.FindBuff("Damage_Tactics"))
		if(m_SkillRank >= 1)
			recipient->m_Buffs.RemoveBuff("Damage_Tactics");
		else
			return;


	switch(m_SkillRank)
	{
		case 7:
			strcpy_s(RallyBuff.Stats[statcounter].StatName, sizeof(RallyBuff.Stats[statcounter].StatName), STAT_SIGNATURE);
			RallyBuff.Stats[statcounter].StatName[sizeof(RallyBuff.Stats[statcounter].StatName)-1] = '\0';
			RallyBuff.Stats[statcounter].Value = 50.0f * m_SkillLevel;		// .3 Per level
			RallyBuff.Stats[statcounter].StatType = STAT_DEBUFF_VALUE;
			statcounter++;
		case 5:
			strcpy_s(RallyBuff.Stats[statcounter].StatName, 
				sizeof(RallyBuff.Stats[statcounter].StatName), STAT_SKILL_MISSILE_WEAPON);
			RallyBuff.Stats[statcounter].StatName[sizeof(RallyBuff.Stats[statcounter].StatName)-1] = '\0';
			RallyBuff.Stats[statcounter].Value = 0.3f * m_SkillLevel;		// .3 Per level
			RallyBuff.Stats[statcounter].StatType = STAT_BUFF_VALUE;
			statcounter++;

			strcpy_s(RallyBuff.Stats[statcounter].StatName, 
				sizeof(RallyBuff.Stats[statcounter].StatName), STAT_SKILL_PROJECTILE_WEAPON);
			RallyBuff.Stats[statcounter].StatName[sizeof(RallyBuff.Stats[statcounter].StatName)-1] = '\0';
			RallyBuff.Stats[statcounter].Value = 0.3f * m_SkillLevel;		// .3 Per level
			RallyBuff.Stats[statcounter].StatType = STAT_BUFF_VALUE;
			statcounter++;

			strcpy_s(RallyBuff.Stats[statcounter].StatName, 
				sizeof(RallyBuff.Stats[statcounter].StatName), STAT_SKILL_BEAM_WEAPON);
			RallyBuff.Stats[statcounter].StatName[sizeof(RallyBuff.Stats[statcounter].StatName)-1] = '\0';
			RallyBuff.Stats[statcounter].Value = 0.3f * m_SkillLevel;		// .3 Per level
			RallyBuff.Stats[statcounter].StatType = STAT_BUFF_VALUE;
			statcounter++;
		case 3:
			strcpy_s(RallyBuff.Stats[statcounter].StatName, 
				sizeof(RallyBuff.Stats[statcounter].StatName), STAT_CHEM_DEFLECT);
			RallyBuff.Stats[statcounter].StatName[sizeof(RallyBuff.Stats[statcounter].StatName)-1] = '\0';
			RallyBuff.Stats[statcounter].Value = 5 * m_SkillLevel;		// 5% Per level
			RallyBuff.Stats[statcounter].StatType = STAT_BUFF_VALUE;
			statcounter++;

			strcpy_s(RallyBuff.Stats[statcounter].StatName, 
				sizeof(RallyBuff.Stats[statcounter].StatName), STAT_IMPACT_DEFLECT);
			RallyBuff.Stats[statcounter].StatName[sizeof(RallyBuff.Stats[statcounter].StatName)-1] = '\0';
			RallyBuff.Stats[statcounter].Value = 5 * m_SkillLevel;		// 5% Per level
			RallyBuff.Stats[statcounter].StatType = STAT_BUFF_VALUE;
			statcounter++;

			strcpy_s(RallyBuff.Stats[statcounter].StatName, 
				sizeof(RallyBuff.Stats[statcounter].StatName), STAT_ENERGY_DEFLECT);
			RallyBuff.Stats[statcounter].StatName[sizeof(RallyBuff.Stats[statcounter].StatName)-1] = '\0';
			RallyBuff.Stats[statcounter].Value = 5 * m_SkillLevel;		// 5% Per level
			RallyBuff.Stats[statcounter].StatType = STAT_BUFF_VALUE;
			statcounter++;

			strcpy_s(RallyBuff.Stats[statcounter].StatName, 
				sizeof(RallyBuff.Stats[statcounter].StatName), STAT_PLASMA_DEFLECT);
			RallyBuff.Stats[statcounter].StatName[sizeof(RallyBuff.Stats[statcounter].StatName)-1] = '\0';
			RallyBuff.Stats[statcounter].Value = 5 * m_SkillLevel;		// 5% Per level
			RallyBuff.Stats[statcounter].StatType = STAT_BUFF_VALUE;
			statcounter++;

			strcpy_s(RallyBuff.Stats[statcounter].StatName, 
				sizeof(RallyBuff.Stats[statcounter].StatName), STAT_PSIONIC_DEFLECT);
			RallyBuff.Stats[statcounter].StatName[sizeof(RallyBuff.Stats[statcounter].StatName)-1] = '\0';
			RallyBuff.Stats[statcounter].Value = 5 * m_SkillLevel;		// 5% Per level
			RallyBuff.Stats[statcounter].StatType = STAT_BUFF_VALUE;
			statcounter++;

			strcpy_s(RallyBuff.Stats[statcounter].StatName, 
				sizeof(RallyBuff.Stats[statcounter].StatName), STAT_EXPLOSIVE_DEFLECT);
			RallyBuff.Stats[statcounter].StatName[sizeof(RallyBuff.Stats[statcounter].StatName)-1] = '\0';
			RallyBuff.Stats[statcounter].Value = 5 * m_SkillLevel;		// 5% Per level
			RallyBuff.Stats[statcounter].StatType = STAT_BUFF_VALUE;
			statcounter++;

			strcpy_s(RallyBuff.Stats[statcounter].StatName, 
				sizeof(RallyBuff.Stats[statcounter].StatName), STAT_EMP_DEFLECT);
			RallyBuff.Stats[statcounter].StatName[sizeof(RallyBuff.Stats[statcounter].StatName)-1] = '\0';
			RallyBuff.Stats[statcounter].Value = 5 * m_SkillLevel;		// 5% Per level
			RallyBuff.Stats[statcounter].StatType = STAT_BUFF_VALUE;
			statcounter++;
		case 1:
			strcpy_s(RallyBuff.Stats[statcounter].StatName, 
				sizeof(RallyBuff.Stats[statcounter].StatName), STAT_CRITICAL_RATE);
			RallyBuff.Stats[statcounter].StatName[sizeof(RallyBuff.Stats[statcounter].StatName)-1] = '\0';
			RallyBuff.Stats[statcounter].Value = 5 * m_SkillLevel;		// 5% Per level
			RallyBuff.Stats[statcounter].StatType = STAT_BUFF_VALUE;
	}
	switch(m_SkillRank)
	{
		case 7:
			strcpy_s(RallyBuff.BuffType, sizeof(RallyBuff.BuffType), "Stealth_Tactics");
			RallyBuff.BuffType[sizeof(RallyBuff.BuffType)-1] = '\0';
			RallyBuff.EffectID[0] = 327;
			break;
		case 5:
			strcpy_s(RallyBuff.BuffType, sizeof(RallyBuff.BuffType), "Firing_Tactics");
			RallyBuff.BuffType[sizeof(RallyBuff.BuffType)-1] = '\0';
			RallyBuff.EffectID[0] = 330;
			break;
		case 3:
			strcpy_s(RallyBuff.BuffType, sizeof(RallyBuff.BuffType), "Defense_Tactics");
			RallyBuff.BuffType[sizeof(RallyBuff.BuffType)-1] = '\0';
			RallyBuff.EffectID[0] = 324;
			RallyBuff.EffectID[1] = 333;
			break;
		case 1:
			strcpy_s(RallyBuff.BuffType, sizeof(RallyBuff.BuffType), "Damage_Tactics");
			RallyBuff.BuffType[sizeof(RallyBuff.BuffType)-1] = '\0';
			RallyBuff.EffectID[0] = 333;
			break;
	}
	recipient->m_Buffs.AddBuff(&RallyBuff);
}
