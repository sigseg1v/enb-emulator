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

#include <math.h>
#include "AbilityPowerDown.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*
lvl 1: 7 second init, .5 drain per second, must be stopped to cloak, 1/2 speed once cloaked
lvl 2: 6 second init, .5 drain
lvl 3: 5 second init, 1.5 drain, must be stopped to cloak, full speed while cloaked
lvl 4: 4 second init
lvl 5: 3 second init, 3 drain, ship may move to cloak, full speed, 2x unmodified beam damage for 5 seconds after uncloaking
lvl 6: 3 second init, 6 drain, cloaks ship + all ships within 1K. May warp while cloaked, reduces ship sig for group members?
lvl 7: 3 second init, 10 drain, all ships fully cloaked in 1K, may warp.
*/

APowerDown::APowerDown(CMob *me) : AbilityBase(me)
{
	m_OldShieldRegen=0;
	m_OldReactorRegen=0;
}

/*
* This calculates the activation cost of the skill.
* TO-DO: Balance this value
*/
float APowerDown::CalculateEnergy ( float SkillLevel, long SkillRank )
{
	switch(SkillRank)
	{
	case 1:
		return 35;
		break;
	case 3:
		return 75;
		break;
	case 5:
		return 150;
		break;
	case 6:
		return 300;
		break;
	case 7:
		return 600;
		break;
	default:
		return -1;
		break;
	}
}

/*
* Considering the difference is power output from JE's, set these values to be slightly less then for JE cloak
*/
float APowerDown::CalculateEnergyPerSecond (long SkillRank )
{
	return 0.0f;
}

float APowerDown::CalculateCoolDownTime ( float SkillLevel, long SkillRank ) 
{
	return 2.0f;
}

/*
* Power Down detection is based on your overall level + 5 times your power down skill.
* So, a lvl 150 PE with lvl 5 power down will have an effective, unmodified skill of
* 175. Therefore, any mob with Clvl 55 or lower will be unable to see a power downed player.
* Scan skill will increase the mob's ability to see through power down above the MOB's total
* level. Checks against this number will be simple: if(MOBSeePoweredDownSkill > PowerDownSkill)
*  then SeeThisPlayer = false;
*/
float APowerDown::CalculatePowerDownLevel ( long SkillLevel )
{
	CMob *p = GetPointerToCommon();
	float val = p->TotalLevel() + (5.0f * SkillLevel);
	float mult = p->m_Stats.ModifyValueWithStat(STAT_POWERDOWN,val);
	return mult;//(mult > 0)? val * mult : val;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long APowerDown::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case POWER_DOWN:
		return 1;
	case ADVANCED_POWER_DOWN:
		return 3;
	case ADVANCED_POWER_DOWN_2:
		return 5;
	case ADVANCED_POWER_DOWN_3:
		return 6;
	case ADVANCED_POWER_DOWN_4:
		return 7;
	default:
		return -1;
	}
}

char *APowerDown::GetBuffName()
{
	switch(m_AbilityID)
	{
	default:
	case POWER_DOWN:
		return "Power_Down";
	case ADVANCED_POWER_DOWN:
		return "Advanced Power Down";
	case ADVANCED_POWER_DOWN_2:
		return "Advanced Power Down 2";
	case ADVANCED_POWER_DOWN_3:
		return "Advanced Power Down 3";
	case ADVANCED_POWER_DOWN_4:
		return "Advanced Power Down 4";
	}
}

void APowerDown::RemoveBuff(Player *p)
{
	char *buffname = GetBuffName();
	if(p->m_Buffs.FindBuff(buffname))
	{
		p->m_Buffs.RemoveBuff(buffname);
		m_BuffID = 0;
		p->RecalculateEnergyRegen();
		p->RecalculateShieldRegen();
	}
}

void APowerDown::ApplyBuff(Player *p, float shieldFactor,  float energyFactor)
{
	RemoveBuff(p);

	int index = 0;
	Buff pdbuff;
	memset(&pdbuff,0,sizeof(Buff));
	strcpy_s(pdbuff.BuffType,sizeof(pdbuff.BuffType),GetBuffName());
	pdbuff.BuffType[sizeof(pdbuff.BuffType)-1]='\0'; //force terminate string
	
	pdbuff.EffectID[0]=340;
	for(int c = 1; c < 5; c++)
	{
		pdbuff.EffectID[c] = -1;
	}
	pdbuff.NumEffects=1;

	pdbuff.ExpireTime = 0;
	pdbuff.IsPermanent = true;

	if(shieldFactor < 1.0f)
	{
		strncpy(pdbuff.Stats[index].StatName,STAT_SHIELD_RECHARGE,sizeof(pdbuff.Stats[index].StatName));
		pdbuff.Stats[index].StatName[sizeof(pdbuff.Stats[index].StatName)-1]='\0';
		pdbuff.Stats[index].StatType = STAT_DEBUFF_MULT;
		pdbuff.Stats[index].Value = 1.0f - shieldFactor;
		index++;
	}
	if(energyFactor < 1.0f)
	{
		strncpy(pdbuff.Stats[index].StatName,STAT_ENERGY_RECHARGE,sizeof(pdbuff.Stats[index].StatName));
		pdbuff.Stats[index].StatName[sizeof(pdbuff.Stats[index].StatName)-1]='\0';
		pdbuff.Stats[index].StatType = STAT_DEBUFF_MULT;
		pdbuff.Stats[index].Value = 1.0f - energyFactor;
		index++;
	}

	m_BuffID = p->m_Buffs.AddBuff(&pdbuff);
}
/*
* By default this enables full shield regen but this is also used to set 
* the 0.5f and 0.0f shield regen as required by the skill levels
*/
void APowerDown::ModifyShieldRegen(float factor)
{
	Player *p = GetPointerToPlayer();
	
	m_OldShieldRegen = p->m_Stats.GetStat(STAT_SHIELD_RECHARGE);
	float newShieldRegen = m_OldShieldRegen*factor;

	float startValue = p->GetShield() / (p->m_Stats.GetStat(STAT_SHIELD) / p->GetMaxShield());

	if (startValue > 1.0f)
	{
		startValue = 1.0f;
	}

	float chargeRate = (newShieldRegen / p->m_Stats.GetStat(STAT_SHIELD)) / 1000.0f;
	unsigned long endTime = GetNet7TickCount();
	if(chargeRate != 0.0f)
	{
		endTime += (unsigned long)((1.0f - startValue) / chargeRate);
	}

	p->ShieldUpdate(endTime, chargeRate, startValue);
}

/*
* By default this enables full reactor regen but this is also used to set 
* the 0.5f and 0.0f reactor regen as required by the skill levels
*/
void APowerDown::ModifyReactorRegen(float factor)
{
	Player *p = GetPointerToPlayer();
	m_OldReactorRegen = p->m_Stats.GetStat(STAT_ENERGY_RECHARGE);
	float newReactorRegen = m_OldReactorRegen*factor;

	float startValue = p->GetEnergy() / (p->m_Stats.GetStat(STAT_ENERGY) / p->GetMaxEnergy());

	if (startValue > 1.0f)
	{
		startValue = 1.0f;
	}

	float chargeRate = (newReactorRegen / p->m_Stats.GetStat(STAT_ENERGY)) / 1000.0f;
	
	unsigned long endTime = GetNet7TickCount();
	if(chargeRate != 0.0f)
	{
		endTime += (unsigned long)((1.0f - startValue) / chargeRate);
	}

	p->EnergyUpdate(endTime, chargeRate, startValue);
}

// --------------------------------------------

bool APowerDown::CanUse(long TargetID, long AbilityID, long SkillID)
{
	Player *p = GetPointerToPlayer();
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx(true,true,true))
	{
		return false;
	}

	//are we moving?
	if(p->ObjectIsMoving() && !m_InUse)
	{
		SendError("Must be stopped to use this skill.");
		return false;
	}

	return true;
}

/*
* PowerDown is a toggle ability. Keep that in mind while reading though this
*  function call. This function must deal with turning the skill on and off.
*/
bool APowerDown::UseSkill(long ChargeTime)
{
	Player *p = GetPointerToPlayer();
	/*
	ObjectEffect PowerDownEffect;
	PowerDownEffect.EffectDescID = 340;
	*/

	//mark skill as in use (this sets m_InUse to true on first call, since it's default is false)
	m_InUse = !m_InUse;

	if(m_InUse)
	{
		//Disable weapons
		p->SwitchOffAutofire();
        
		//Apply buff

		switch(m_SkillRank)
		{
		case 1:
			ApplyBuff(p,0.0f,0.0f);
			break;
		case 3:
			ApplyBuff(p,0.5f,0.0f);
			break;
		case 5:
			ApplyBuff(p,0.5f,0.5f);
			break;
		case 6:
			ApplyBuff(p,1.0f,0.5f);
			break;
		case 7:
			ApplyBuff(p,1.0f,1.0f);
			break;
		}

		//send energy update
		//p->SendAuxShip();

		// Send Effect
		/*
		PowerDownEffect.Bitmask = 3;
		PowerDownEffect.GameID = p->GameID();
		PowerDownEffect.EffectID = m_EffectID;
		PowerDownEffect.TimeStamp = m_EffectID;
		PowerDownEffect.Duration = 0; //instantly charges up

		p->SendObjectEffectRL(&PowerDownEffect);
		*/

		//powerdown in X seconds (skill rank dependant)
		SetTimer((long)1000); //calls Update when finished.
	}
	else
	{
		//Mark player as uncloaked & remove powerdown buff
		RemoveBuff(p);
		p->ShipIndex()->SetIsCountermeasureActive(false);
		p->SetCounterMeasuresLevel(0);
		p->RemoveEffectRL(m_EffectID);
		p->SetCurrentSkill();		
	}
	
	return true;
}

/*
* This function is called when the SetTimer call returns.
*
* DO NOT modify m_InUse in this function.
* DO NOT removeeffects in here. This is done elsewhere.
*/
bool APowerDown::Update(long activation_ID)
{
	Player *p = GetPointerToPlayer();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	if(m_InUse)
	{
		//Mark player as powered down
		p->ShipIndex()->SetIsCountermeasureActive(true);
		p->SetCounterMeasuresLevel((int)CalculatePowerDownLevel((long)m_SkillLevel));

		//send energy update
		p->SendAuxShip();

		SetTimer(1000);
	}
	else if(p->GetIsIncapacitated())
	{
		InterruptSkillOnAction(OTHER); //this might not be needed, but it ensures that powerdown is removed.
	}
	//else, do nothing, skill interrupted during timer.

	return true;
}

/*
* Returns true if the skill can be interrupted, what can interrupt it 
*   is returned by the boolean pointers.
*/
bool APowerDown::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = true;
	*OnDamage = false;
	*OnAction = true;
	return true;
}

/*
* Determines whether or not this skill was interrupted based on current motion.
*/
bool APowerDown::InterruptSkillOnMotion(float speed)
{
	ObjectEffect InterruptEffect;
	Player *p = GetPointerToPlayer();

	if(!m_InUse) //skill not in use, cannot interrupt.
	{
		p->SetCurrentSkill();
		m_DamageTaken = 0;
		return false;
	}

	if (p->ObjectIsMoving())
	{
		//remove current effect
		RemoveBuff(p);

		//ensure we are not flagged as cloaked, for good measure.
		p->ShipIndex()->SetIsCountermeasureActive(false);
		p->SetCounterMeasuresLevel(0);
		//get new effectID
		m_EffectID = GetNet7TickCount();

		InterruptEffect.Bitmask = 3;
		InterruptEffect.GameID = p->GameID();
		InterruptEffect.EffectDescID = 735;
		InterruptEffect.EffectID = m_EffectID;
		InterruptEffect.TimeStamp = m_EffectID;

		p->SendObjectEffect(&InterruptEffect);

		//TO-DO: I don't know if this will work or not.
		SetObjectEffectTimer(m_EffectID, 1000);

		m_InUse = false;

		m_DamageTaken = 0;
		p->SetCurrentSkill();

		return true;
	}
	else
	{
		return false;
	}
}

/*
* Returns true if this skill was interrupted based on player action.
*
* Action = Shooting guns, using devices or activated effects, starting to warp,
*  anything except basic impulse or opening windows on the player's client.
*/
bool APowerDown::InterruptSkillOnAction(int Type)
{
	Player *p = GetPointerToPlayer();
	if(!m_InUse && !p->ShipIndex()->GetIsCountermeasureActive())
	{
		p->SetCurrentSkill();
		return false;
	}

	//Remove power down effect
	RemoveBuff(p);
	p->ShipIndex()->SetIsCountermeasureActive(false);
	p->SetCounterMeasuresLevel(0);
	//mark skill as not in use
	m_InUse = false;

	m_DamageTaken = 0;
	p->SetCurrentSkill();
	return true;
}