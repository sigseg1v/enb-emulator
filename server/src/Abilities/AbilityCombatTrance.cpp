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

#include "AbilityCombatTrance.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

ACombatTrance::ACombatTrance(CMob *me) : AbilityBase(me, STAT_SKILL_COMBAT_TRANCE)
{
	m_InTrance = false;
	m_Expire = 0;
	m_SkillID = SKILL_COMBAT_TRANCE;
}

void ACombatTrance::SetTranceTimer(long Duration)
{
	CMob *p = GetPointerToCommon();
	if (p)
	{
		SectorManager *sm = p->GetSectorManager();
		if (sm) sm->AddTimedCall(p, B_BUFF_TIMEOUT, Duration, NULL, m_AbilityID, m_SkillActivationID, 0);
	}
}

bool ACombatTrance::Use(long )
{
	CMob *p = GetPointerToCommon();
	m_InTrance = false;
	if(p && p->m_Buffs.FindBuff("Combat_Trance"))
	{
		p->m_Buffs.RemoveBuff("Combat_Trance");
	}
	SetTranceTimer(500); 
	return true;
}

// Updated by a timer
bool ACombatTrance::Update(long )
{
	CMob *p = GetPointerToCommon();
	bool moving = p->ObjectIsMoving();

	//In station check
	if(!p->InSpace())
	{
		//remove the buff and don't set a new callback.
		if(p->m_Buffs.FindBuff("Combat_Trance"))
		{
			p->m_Buffs.RemoveBuff("Combat_Trance");
		}
		return false;
	}
	
	//Incapacitate check
	if(p->GetIsIncapacitated())
	{
		//remove the buff
		if(p->m_Buffs.FindBuff("Combat_Trance"))
		{
			p->m_Buffs.RemoveBuff("Combat_Trance");
		}
		SetTranceTimer(6000); //Check alot less often now.
		return false;
	}
	
	//Ship movement checks
	if(moving && m_InTrance)
	{
		m_TranceBuff.ExpireTime = GetNet7TickCount() + m_Expire;
		m_TranceBuff.IsPermanent = false;
		m_InTrance = false;
		p->m_Buffs.RefreshOrAddBuff(&m_TranceBuff);
	}
	else if(!moving && !m_InTrance)
	{
		if(p->m_Buffs.FindBuff("Combat_Trance"))
		{
			p->m_Buffs.RemoveBuff("Combat_Trance");
		}
		CreateTranceBuff();

		m_TranceBuff.ExpireTime = 0;
		m_TranceBuff.IsPermanent = true;
		m_InTrance = true;
		p->m_Buffs.AddBuff(&m_TranceBuff);
	}
	
	//Set when this ability should be checked again.
	SetTranceTimer(1500);

	return true;
}

// Creates the trance buff for the player
void ACombatTrance::CreateTranceBuff()
{
	CMob *p = GetPointerToCommon();
	int buffStatI = 0;
	int stat_lvl = GetSkillLevel(GetPointerToPlayer(), p);
	float stat_mag = 30.0f*m_SkillLevel;
	int tranceEffects[7] = {669,669,670,670,671,672,673};
	
	//set how long this buff will expire upon movement
	m_Expire = (int)(100 * stat_mag);

	//send effect and buff
	memset(&m_TranceBuff, 0, sizeof(Buff));
	//m_TranceBuff.EffectID = tranceEffects[stat_lvl-1];

	switch(stat_lvl)
	{
	case 7:
		m_TranceBuff.EffectID[4] = 673;
	case 6:
		m_TranceBuff.EffectID[3] = 672;
	case 5:
		m_TranceBuff.EffectID[2] = 671;
	case 4:
	case 3:
		m_TranceBuff.EffectID[1] = 670;
	case 2:
	case 1:
		m_TranceBuff.EffectID[0] = 669;
	default:
		break;
	}
		
	strcpy_s(m_TranceBuff.BuffType, sizeof(m_TranceBuff.BuffType), "Combat_Trance");
	m_TranceBuff.BuffType[sizeof(m_TranceBuff.BuffType)-1] = '\0';
	strcpy_s(m_TranceBuff.Stats[buffStatI].StatName, sizeof(m_TranceBuff.Stats[buffStatI].StatName), STAT_PROJECTILE_ACCURACY);
	m_TranceBuff.Stats[buffStatI].StatName[sizeof(m_TranceBuff.Stats[buffStatI].StatName)-1] = '\0';
	m_TranceBuff.Stats[buffStatI].Value = stat_mag;
	m_TranceBuff.Stats[buffStatI].StatType = STAT_BUFF_VALUE;
	buffStatI++;
	
	if(stat_lvl >= 3)
	{
		float val = 4.0f + (stat_mag / 10);
		float res;
		res = p->m_Stats.GetStat(STAT_IMPACT_DEFLECT);
		if(res < 50.0f)
		{
			strcpy_s(m_TranceBuff.Stats[buffStatI].StatName, 
				sizeof(m_TranceBuff.Stats[buffStatI].StatName), STAT_IMPACT_DEFLECT);
			m_TranceBuff.Stats[buffStatI].StatName[sizeof(m_TranceBuff.Stats[buffStatI].StatName)-1] = '\0';
			m_TranceBuff.Stats[buffStatI].Value = min(50.0f-res,val);
			m_TranceBuff.Stats[buffStatI].StatType = STAT_BUFF_VALUE;
			buffStatI++;
		}

		res = p->m_Stats.GetStat(STAT_EXPLOSIVE_DEFLECT);
		if(res < 50.0f)
		{
			strcpy_s(m_TranceBuff.Stats[buffStatI].StatName, 
				sizeof(m_TranceBuff.Stats[buffStatI].StatName), STAT_EXPLOSIVE_DEFLECT);
			m_TranceBuff.Stats[buffStatI].StatName[sizeof(m_TranceBuff.Stats[buffStatI].StatName)-1] = '\0';
			m_TranceBuff.Stats[buffStatI].Value =  min(50.0f-res,val);
			m_TranceBuff.Stats[buffStatI].StatType = STAT_BUFF_VALUE;
			buffStatI++;
		}

		res = p->m_Stats.GetStat(STAT_PLASMA_DEFLECT);
		if(res < 50.0f)
		{
			strcpy_s(m_TranceBuff.Stats[buffStatI].StatName, 
				sizeof(m_TranceBuff.Stats[buffStatI].StatName), STAT_PLASMA_DEFLECT);
			m_TranceBuff.Stats[buffStatI].StatName[sizeof(m_TranceBuff.Stats[buffStatI].StatName)-1] = '\0';
			m_TranceBuff.Stats[buffStatI].Value =  min(50.0f-res,val);
			m_TranceBuff.Stats[buffStatI].StatType = STAT_BUFF_VALUE;
			buffStatI++;
		}
		res = p->m_Stats.GetStat(STAT_ENERGY_DEFLECT);
		if(res < 50.0f)
		{
			strcpy_s(m_TranceBuff.Stats[buffStatI].StatName, 
				sizeof(m_TranceBuff.Stats[buffStatI].StatName), STAT_ENERGY_DEFLECT);
			m_TranceBuff.Stats[buffStatI].StatName[sizeof(m_TranceBuff.Stats[buffStatI].StatName)-1] = '\0';
			m_TranceBuff.Stats[buffStatI].Value =  min(50.0f-res,val);
			m_TranceBuff.Stats[buffStatI].StatType = STAT_BUFF_VALUE;
			buffStatI++;
		}
		res = p->m_Stats.GetStat(STAT_EMP_DEFLECT);
		if(res < 50.0f)
		{
			strcpy_s(m_TranceBuff.Stats[buffStatI].StatName, 
				sizeof(m_TranceBuff.Stats[buffStatI].StatName), STAT_EMP_DEFLECT);
			m_TranceBuff.Stats[buffStatI].StatName[sizeof(m_TranceBuff.Stats[buffStatI].StatName)-1] = '\0';
			m_TranceBuff.Stats[buffStatI].Value =  min(50.0f-res,val);
			m_TranceBuff.Stats[buffStatI].StatType = STAT_BUFF_VALUE;
			buffStatI++;
		}
		res = p->m_Stats.GetStat(STAT_CHEM_DEFLECT);
		if(res < 50.0f)
		{
			strcpy_s(m_TranceBuff.Stats[buffStatI].StatName, 
				sizeof(m_TranceBuff.Stats[buffStatI].StatName), STAT_CHEM_DEFLECT);
			m_TranceBuff.Stats[buffStatI].StatName[sizeof(m_TranceBuff.Stats[buffStatI].StatName)-1] = '\0';
			m_TranceBuff.Stats[buffStatI].Value =  min(50.0f-res,val);
			m_TranceBuff.Stats[buffStatI].StatType = STAT_BUFF_VALUE;
			buffStatI++;
		}
	}
	if(stat_lvl >= 5)
	{
		strcpy_s(m_TranceBuff.Stats[buffStatI].StatName, 
				sizeof(m_TranceBuff.Stats[buffStatI].StatName), STAT_BEAM_ACCURACY);
		m_TranceBuff.Stats[buffStatI].StatName[sizeof(m_TranceBuff.Stats[buffStatI].StatName)-1] = '\0';
		m_TranceBuff.Stats[buffStatI].Value = stat_mag;
		m_TranceBuff.Stats[buffStatI].StatType = STAT_BUFF_VALUE;
		buffStatI++;
	}
	if(stat_lvl >= 6)
	{
		strcpy_s(m_TranceBuff.Stats[buffStatI].StatName, 
				sizeof(m_TranceBuff.Stats[buffStatI].StatName), STAT_ENERGY_RECHARGE);
		m_TranceBuff.Stats[buffStatI].StatName[sizeof(m_TranceBuff.Stats[buffStatI].StatName)-1] = '\0';
		
		//I know what the Janice Doc says about this skill, but in this case, it's wrong. 
		//+6 and +7 reactor is WAY too small for lvl 6, 7, 8, 9 reactors
		//Well...ok, +6 is fine for a lvl 6 reactor, +7 is ok for a lvl 7, but much higher values are needed
		//for lvls 8 and 9
		//m_TranceBuff.Stats[buffStatI].Value = stat_mag/30;
		m_TranceBuff.Stats[buffStatI].Value = 
			p->m_Stats.GetStatType(STAT_ENERGY_RECHARGE, STAT_BASE_VALUE) * 0.25f * ((stat_mag/30)-5);
		m_TranceBuff.Stats[buffStatI].StatType = STAT_BUFF_VALUE;
		buffStatI++;
	}
	if(stat_lvl >= 7)
	{
		strcpy_s(m_TranceBuff.Stats[buffStatI].StatName, 
				sizeof(m_TranceBuff.Stats[buffStatI].StatName), STAT_SHIELD_RECHARGE);
		m_TranceBuff.Stats[buffStatI].StatName[sizeof(m_TranceBuff.Stats[buffStatI].StatName)-1] = '\0';
		m_TranceBuff.Stats[buffStatI].Value = (stat_mag * stat_mag )/100;
		m_TranceBuff.Stats[buffStatI].StatType = STAT_BUFF_VALUE;
		buffStatI++;
	}

}
