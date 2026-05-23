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

#include "AbilityCloak.h"
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

ACloak::ACloak(CMob * me) : AbilityBase(me,STAT_SKILL_CLOAK)
{ 
	Init(me);
	m_DoubleDamageActive = false;
	m_WholeGroup = false;
	m_BuffID = -1;
}

float ACloak::CalculateEnergy ( float SkillLevel, long SkillRank )
{
	float time = CalculateChargeUpTime(SkillLevel, SkillRank);
	float energy_per_sec;

	switch(SkillRank)
	{
	case 1:
		energy_per_sec = 3.0f;
		break;
	case 3:
		energy_per_sec = 10.0f;
		break;
	case 5:
		energy_per_sec = 33.0f;
		break;
	case 6:
		energy_per_sec = 67.0f;
		break;
	case 7:
		energy_per_sec = 117.0f;
		break;
	default:
		energy_per_sec = 0.0f;
	}
	return energy_per_sec * time;
}

float ACloak::CalculateEnergyPerSecond (long SkillRank )
{
	switch(SkillRank)
	{
	case 1:
		return 0.5f;
	case 3:
		return 1.5f;
	case 5:
		return 3.0f;
	case 6:
		return 6.0f;
	case 7:
		return 10.0f;
	default:
		return -1;
	}
}

float ACloak::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	CMob *p = GetPointerToCommon();
	float CloakTime = SkillLevel < 6.0f ? 8.0f - SkillLevel : 3.0f;

	//cap unbuffed cloak at 3 seconds
	CloakTime = CloakTime < 3.0f ? 3.0f : CloakTime;

	CloakTime = p->m_Stats.ModifyValueWithStat(STAT_CLOAK, CloakTime);
	if (CloakTime < 1.0f)
		CloakTime = 1.0f;
	return CloakTime;
}

/*
* Levels 6 & 7 of cloak have a range over which they effect group members. This
* computes that range.
*/
float ACloak::CalculateAOE ( float SkillLevel, long SkillRank )
{
	return m_WholeGroup ? 500000.0f : 1000.0f;
}

/*
* Stealth detection is based on your overall level + 5 times your skill at cloak.
* So, a lvl 150 JD with lvl 5 cloak will have an effective, unmodified, cloak skill of
* 175. Therefore, any mob with Clvl 55 or lower will be unable to see a cloaked player.
* Scan skill will increase the mob's ability to see through stealth above the MOB's total
* level. Checks against this number will be simple: if(MOBSeeCloakedSkill > CloakSkill)
*  then SeeThisPlayer = false;
*/
float ACloak::CalculateStealthLevel ( float SkillLevel )
{
	CMob *p = GetPointerToCommon();
	return p->TotalLevel() + (5.0f * SkillLevel);
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long ACloak::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case CLOAK:
		return 1;
	case ADVANCED_CLOAK:
		return 3;
	case COMBAT_CLOAK:
		return 5;
	case GROUP_STEALTH:
		return 6;
	case GROUP_CLOAK:
		return 7;
	default:
		return -1;
	}
}

/*
* By default this enables half of max speed for Rank 1 cloak.
* Pass false in to enable full speed again.
*/
void ACloak::EnableHalfSpeed(bool answer)
{
	CMob *p = GetPointerToCommon();
	p->SetHalfSpeed(answer);
}

/*
* By default this enables the 2x bonus to beam damage for rank 5 cloak.
* Mozu: We can just do this with a buff with duration 5 seconds instead of doing it by hand. Plus we get the icon!
*/

void ACloak::EnableDoubleDamage(bool answer)
{
	if(m_DoubleDamageActive)
	{
		CMob *p = GetPointerToCommon();
		p->SendObjectEffect(701, (long)(1000.0f * m_SkillLevel)); //701 is the stealth attack effect
		Buff SneakAttack;
		memset(&SneakAttack,0,sizeof(Buff));
		strcpy_s(SneakAttack.BuffType, sizeof(SneakAttack.BuffType), "Stealth Strike");
		SneakAttack.BuffType[sizeof(SneakAttack.BuffType)-1] = '\0';
		SneakAttack.ExpireTime = GetNet7TickCount() + (long)(1000 * m_SkillLevel);
		strcpy_s(SneakAttack.Stats[0].StatName, sizeof(SneakAttack.Stats[0].StatName), STAT_BEAM_DAMAGE);
		SneakAttack.Stats[0].StatName[sizeof(SneakAttack.Stats[0].StatName)-1] = '\0';
		SneakAttack.Stats[0].StatType = STAT_BUFF_MULT;
		SneakAttack.Stats[0].Value = 1.0f;
		p->m_Buffs.RefreshOrAddBuff(&SneakAttack);
		m_DoubleDamageActive = false;
	}
}

// --------------------------------------------

bool ACloak::CanUse(long TargetID, long AbilityID, long SkillID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx(true,m_SkillRank < 6,true))
	{
		return false;
	}

	//are we moving?
	if(m_SkillRank < 5 && p->ObjectIsMoving() && !m_InUse)
	{
		SendError("Must be stopped to use this rank of the skill.");
		return false;
	}

	return true;
}

/*
* Cloak is a toggle ability. Keep that in mind while reading though this
*  function call. This function must deal with turning the skill on and off.
*/
bool ACloak::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	//mark skill as in use (this sets m_InUse to true on first call, since it's default is false)
	m_InUse = !m_InUse;

	if(m_InUse)
	{
		ObjectEffect CloakEffect;
		//perform animation
		switch(m_SkillRank)
		{
		case 1:
			EnableHalfSpeed();
			CloakEffect.EffectDescID = 180;
			break;
		case 3:
			CloakEffect.EffectDescID = 181;
			break;
		case 5:
			CloakEffect.EffectDescID = 182;
			break;
		case 6:
			CloakEffect.EffectDescID = 183;
			break;
		case 7:
			CloakEffect.EffectDescID = 184;
			break;
		}

		// Send Effect
		CloakEffect.Bitmask = 7;
		CloakEffect.GameID = p->GameID();
		CloakEffect.EffectID = m_EffectID;
		CloakEffect.TimeStamp = m_EffectID;
		CloakEffect.Duration = (short)ChargeTime;

		p->SendObjectEffectRL(&CloakEffect);
	}
	else
	{
		p->RemoveEffectRL(m_EffectID);
		p->SetCurrentSkill();

		//Mark player as uncloaked
		SetIsCloaked(false);

		if(m_SkillRank == 1)
		{
			EnableHalfSpeed(false);
		}
		//combat cloak damage bonus
		else if(m_SkillRank == 5)
		{
			EnableDoubleDamage();
		}
	}
	
	return true;
}

/*
* This function is called when the SetTimer call returns.
*
* DO NOT modify m_InUse in this function.
* DO NOT removeeffects in here. This is done elsewhere.
*/
bool ACloak::Update(long activation_ID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}
	
	if(m_InUse)
	{
		if (m_FirstUse)
		{
			DrainReactor(CalculateEnergyPerSecond(m_SkillRank));
			
			p->RemovePlayerFromView(true); //don't blank group members - they still see you cloaked
			m_FirstUse = false;
			
			//Mark player as cloaked
			SetIsCloaked(true);
			if(m_SkillRank == 5)
			{
				m_DoubleDamageActive = true;  //we actually fully cloaked, so allow double damage upon uncloak
			}
		}
		bool group = false;
		if (p->GroupID() != -1)
			group = CheckGroupCloaks(true);

		float energy = p->GetEnergyValue();
		float drain = CalculateEnergyPerSecond(m_SkillRank);

		if(energy < (drain+2)) //safety margin
		{
			InterruptSkillOnAction(OTHER);
			return false;
		}

		UpdateDelay(0, group ? 5000 : 20000);
	}
    else if(p->GetIsIncapacitated())
	{
		InterruptSkillOnAction(OTHER); //this might not be needed, but it ensures that the cloak effect is removed.
	}
	//else, do nothing, skill interrupted during timer.

	return true;
}

/*
* Returns true if the skill can be interrupted, what can interrupt it 
*   is returned by the boolean pointers.
*/
bool ACloak::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = m_SkillRank < 5;
	*OnDamage = true;
	*OnAction = true;
	return true;
}

/*
* Determines whether or not this skill was interrupted based on damage taken.
* Note: if you get shot while cloaked, it doesn't play the interrupt effect, it just
*   removes the cloak effect.
*/
bool ACloak::InterruptSkillOnDamage(float damage)
{
	CMob *p = GetPointerToCommon();
	ObjectEffect InterruptEffect;

	if(!m_InUse) //cannot interrupt non-active skill.
	{
		p->SetCurrentSkill();
		return false;
	}

	if(damage <= 0.0f)
	{
		return false;
	}

	m_DamageTaken += damage;

	if(p->GetIsCloaked())
	{
		//remove cloak effect
		p->RemoveEffectRL(m_EffectID);

		m_InUse = false;
		p->SetCurrentSkill();

		SetIsCloaked(false);

		if(m_SkillRank < 5)
		{
			EnableHalfSpeed(false);		
		}
		else if(m_SkillRank == 5)
		{
			EnableDoubleDamage();
		}
		else
		{
			//TO-DO: Remove AoE cloak effects as well.
		}

		return true;
	}
	else if(m_DamageTaken > GetInterruptThreshHold())//make this in line with the other skills
	{
		//get new effectID
		m_EffectID = GetNet7TickCount();

		InterruptEffect.Bitmask = 3;
		InterruptEffect.GameID = p->GameID();
		InterruptEffect.EffectDescID = 735;
		InterruptEffect.EffectID = m_EffectID;
		InterruptEffect.TimeStamp = m_EffectID;

		p->SendObjectEffectRL(&InterruptEffect);

		//TO-DO: I don't know if this will work or not.
		SetObjectEffectTimer(m_EffectID, 1000);

		m_InUse = false;

		m_DamageTaken = 0;

		p->SetCurrentSkill();

		return true;
	}

	return false;
}

/*
* Determines whether or not this skill was interrupted based on current motion.
*/
bool ACloak::InterruptSkillOnMotion(float speed)
{
	CMob *p = GetPointerToCommon();
	ObjectEffect InterruptEffect;

	if(!m_InUse) //skill not in use, cannot interrupt.
	{
		p->SetCurrentSkill();
		return false;
	}

	if(p->GetIsCloaked())
	{
		return false;
	}
	else if(m_SkillRank >= 5)
	{
		return false;
	}
	else if (p->ObjectIsMoving())
	{
		//remove current effect
		p->RemoveEffectRL(m_EffectID);

		//get new effectID
		m_EffectID = GetNet7TickCount();

		InterruptEffect.Bitmask = 3;
		InterruptEffect.GameID = p->GameID();
		InterruptEffect.EffectDescID = 735;
		InterruptEffect.EffectID = m_EffectID;
		InterruptEffect.TimeStamp = m_EffectID;

		p->SendObjectEffectRL(&InterruptEffect);

		SetObjectEffectTimer(m_EffectID, 1000);

		m_InUse = false;
		p->SetCurrentSkill();

		EnableHalfSpeed(false);

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
bool ACloak::InterruptSkillOnAction(int Type)
{
	CMob *p = GetPointerToCommon();
	if(!m_InUse && !p->GetIsCloaked())
	{
		p->SetCurrentSkill();
		return false;
	}

	if(Type == WARPING && m_SkillRank >= 6)
	{
		return false;
	}

	if(Type == LOOTING && m_SkillRank > 3)
	{
		return false;
	}

	//Remove stealth effect
	p->RemoveEffectRL(m_EffectID);

	//mark skill as not in use
	m_InUse = false;
	p->SetCurrentSkill();

	SetIsCloaked(false);

	//if rank 1,3 cloak, restore full speed.
	if(m_SkillRank <= 3)
	{
		EnableHalfSpeed(false);
	}
	//if combat cloak enable double damage
	if(m_SkillRank == 5 && Type != INCAPACITATE)
	{
		EnableDoubleDamage();
	}

	return true;
}

// when called with false (from interrupt), m_InUse/m_CurrentSkill needs to be false, Recharge re-calls the interrupt if power is 0
void ACloak::SetIsCloaked(bool on)
{
	CMob *p = GetPointerToCommon();
	if (on)
	{
		Buff Cloak;
		memset(&Cloak,0,sizeof(Buff));
		strcpy_s(Cloak.BuffType, sizeof(Cloak.BuffType), "Cloak");
		Cloak.BuffType[sizeof(Cloak.BuffType)-1] = '\0';
		Cloak.IsPermanent = true;
		p->m_Buffs.RefreshOrAddBuff(&Cloak); 
		p->SetStealthLevel((int)CalculateStealthLevel(m_SkillLevel));
	}
	else
	{
		p->m_Buffs.RemoveBuff("Cloak");
		RechargeReactor();
		if (p->GroupID() != -1)
			CheckGroupCloaks(on);
	}
	p->SetIsCloaked(on);
}

void ACloak::ApplyReduceSigBuffTo(CMob *p, bool remove)
{
	Buff ReduceSig;
	memset(&ReduceSig,0,sizeof(Buff));
	strcpy_s(ReduceSig.BuffType, sizeof(ReduceSig.BuffType), remove ? "Remove_Signature" : "Reduce_Signature");
	ReduceSig.BuffType[sizeof(ReduceSig.BuffType)-1] = '\0';
	strcpy_s(ReduceSig.Stats[0].StatName, STAT_SIGNATURE);
	ReduceSig.Stats[0].Value = remove ? 1.0f : (0.1f * m_SkillLevel);
	ReduceSig.Stats[0].StatType = STAT_DEBUFF_MULT;
	ReduceSig.ExpireTime = GetNet7TickCount() + 6000; // buffer for 5s refresh (if in range)
	ReduceSig.EffectID[0]=328;
	ReduceSig.NumEffects=1;
	p->m_Buffs.RefreshOrAddBuff(&ReduceSig);
}

bool ACloak::CheckGroupCloaks(bool on)
{
	CMob *p;
	int count;

	// get everyone (in sector) when the cloak drops
	m_WholeGroup = !on;
	count = GetFriendlyGroup();
	if (count > 1) // > self only
	{
		if (m_AbilityID == GROUP_STEALTH)
		{
			for (int x=1;x < count;x++)
			{
				p = m_AOEFriendList[x];
				if (p)
				{
					if (on)
					{
						ApplyReduceSigBuffTo(p, false);
					}
					else
					{
						p->m_Buffs.RemoveBuff("Reduce_Signature");
					}
				}
			}
		}
		else if (m_AbilityID == GROUP_CLOAK)
		{
			for (int x=1;x < count;x++)
			{
				p = m_AOEFriendList[x];
				if (p)
				{
					if (on)
					{
						ApplyReduceSigBuffTo(p, true);
					}
					else
					{
						p->m_Buffs.RemoveBuff("Remove_Signature");
					}
				}
			}
		}
	}
	return count > 1;
}