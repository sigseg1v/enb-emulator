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

#include "AbilityAfterburn.h"
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

AAfterburn::AAfterburn(CMob * me) : AbilityBase(me, STAT_SKILL_AFTERBURN)
{
	m_LastUse = 0;
	m_EndTime = 0;
}

/*
* This calculates the activation cost of the skill.
*/
float AAfterburn::CalculateEnergy ( float SkillLevel, long SkillRank )
{
	return  SkillLevel * 6.0f;
}

/*
* Calculate how much time must pass before the skill activates.
*
* Results are returned in seconds.
*/
float AAfterburn::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	return SkillLevel < 6.0f ? 8.0f - SkillLevel : 3.0f;
}

/*
* Compute how much time must pass between skill uses.
*
* Results are returned in seconds.
*/
float AAfterburn::CalculateCoolDownTime ( float SkillLevel, long SkillRank ) 
{
	return 30.0f;
}

/*
* Computes the AoE per skill level for an ability.
*/
float AAfterburn::CalculateAOE ( float SkillLevel, long SkillRank )
{
	return 1000.0f + 0.0f * SkillLevel;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long AAfterburn::DetermineSkillRank(int SkillID)
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

float AAfterburn::CalculateSpeedIncrease(float SkillLevel, long SkillRank)
{
	return 1.0;
}

float AAfterburn::CalculateDuration(float SkillLevel, long SkillRank)
{
	return 3000 + 1000*SkillLevel;
}

// --------------------------------------------

bool AAfterburn::CanUse(long TargetID, long AbilityID, long SkillID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx())
	{
		return false;
	}

	//energy
   if(p->GetEnergyValue() < CalculateEnergy(m_SkillLevel,m_SkillRank))
   {
		SendError("Insufficient energy!");
		return false;
   }

   return true;
}

/*
* This will be the first function called once the skill is determined
* as useable.
*/
bool AAfterburn::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();
	//allow the ability to be toggled off.
	if(m_InUse)
	{
		p->SendVaMessageC(12,"Afterburners off.");
		m_InUse = false;
		//p->RecalculateEnergyRegen();
		RechargeReactor();
		if(p->m_Buffs.FindBuff("Afterburn"))
		{
			p->m_Buffs.RemoveBuff("Afterburn");
		}
		return true;
	}
	m_InUse = true;
	p->SendVaMessageC(12,"Afterburners on.");
	//grab a number for the effectID & timestamp
	m_LastUse = m_EffectID = m_SkillActivationID = GetNet7TickCount();

	Buff abBuff;
	memset(&abBuff,0,sizeof(Buff));
	strcpy_s(abBuff.BuffType, sizeof(abBuff.BuffType), "Afterburn");
	abBuff.BuffType[sizeof(abBuff.BuffType)-1] = '\0';
	abBuff.ExpireTime = m_SkillActivationID + (int)CalculateDuration(m_SkillLevel,m_SkillRank);
	abBuff.IsPermanent = false;
	abBuff.Stats[0].Value = CalculateSpeedIncrease(m_SkillLevel, m_SkillRank);
	abBuff.Stats[0].StatType = STAT_BUFF_MULT;
	strcpy_s(abBuff.Stats[0].StatName, sizeof(abBuff.Stats[0].StatName), STAT_IMPULSE);
	abBuff.Stats[0].StatName[sizeof(abBuff.Stats[0].StatName)-1] = '\0';

	abBuff.NumEffects = 0;
	for(int i = 0; i < 5; i ++)
	{
		abBuff.EffectID[i] = -1;
	}

	if(p->m_Buffs.FindBuff("Afterburn"))
	{
		p->m_Buffs.RemoveBuff("Afterburn");
	}

	p->m_Buffs.AddBuff(&abBuff);

	Player *player = GetPointerToPlayer();
	if (player)
	{
		player->AdjustAndSetSpeeds(true,false);
		player->ResetMaxSpeed(true);
		DrainReactor(CalculateEnergy(m_SkillLevel, m_SkillRank));
	}

	m_EndTime = abBuff.ExpireTime;
	UpdateDelay(m_EndTime);

	return true;
}

/*
* This function is called when the SetTimer call returns.
*/
bool AAfterburn::Update(long activation_ID)
{
	CMob *p = GetPointerToCommon();
	float energy = p->GetEnergyValue();
	float drain = CalculateEnergy(m_SkillLevel,m_SkillRank);
	u32 current_tick = GetNet7TickCount();

	if(m_InUse)
	{
		Player *player = GetPointerToPlayer();
		if(m_EndTime <= current_tick ||
			(energy < drain || energy == 0.0f))
		{
			//p->RecalculateEnergyRegen();
			if(p->m_Buffs.FindBuff("Afterburn"))
			{
				p->m_Buffs.RemoveBuff("Afterburn");
			}
			m_InUse = false;
			p->SendVaMessageC(12,"Afterburners off.");
			if (player)
			{
				player->AdjustAndSetSpeeds(true,false);
				player->ResetMaxSpeed(false);
			}
			m_EndTime = 0;
			RechargeReactor();
			return false;
		}

		UpdateDelay(m_EndTime);	// set timer to either 20000, m_EndTime or when energy runs out
	}	
	
	return true;
}
