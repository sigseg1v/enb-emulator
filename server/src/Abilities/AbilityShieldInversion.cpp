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

#include "AbilityShieldInversion.h"
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
lvl 1: 3 shield units per skill level into damage per second. 90% damage conversion. 1K range
lvl 2: 1250 unit range
lvl 3: 9 shield to damage per skill level per second, 100% conversion, 1.5K range
lvl 4: 1750 unit range
lvl 5: 15 shield to damage per skill per second, 100% conversion. 2K range
lvl 6: 18 ... 120% conversion, 2250 range
lvl 7: 21 ... 130% conversion, all targets in 2500 units
*/
AShieldInversion::AShieldInversion(CMob *me) : AbilityBase(me, STAT_SKILL_SHIELD_INVERSION)
{
	m_Conversion=0;
	m_ShieldDrain=0;
	m_DamageTick=0;
	m_AdjustedSkillLevel=0;
	m_hit_something=false;
}

/*
* This calculates the activation cost of the skill.
*/
float AShieldInversion::CalculateShieldDrain ( float SkillLevel, long SkillRank )
{
	switch(SkillRank)
	{
	case 1:
		m_Conversion = 0.9f;
		return 3.0f * SkillLevel;
	case 3:
		m_Conversion = 1.0f;
		return 9.0f * SkillLevel;
	case 5:
		m_Conversion = 1.1f;
		return 15.0f * SkillLevel;
	case 6:
		m_Conversion = 1.2f;
		return 18.0f * SkillLevel;
	case 7:
		m_Conversion = 1.3f;
		return 21.0f * SkillLevel;
	}
	return -1.0f;
}

float AShieldInversion::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	return 1.0f;
}

/*
* Calculate the maximum range this rank of the skill can be used at.
*/
float AShieldInversion::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	return 750 + 250 * SkillLevel; 
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long AShieldInversion::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case SHIELD_RAM:
		return 1;
	case SHIELD_SPIKE:
		return 3;
	case SHIELD_BURN:
		return 5;
	case SHIELD_FLARE:
		return 6;
	case SHIELD_NOVA:
		return 7;
	default:
		return -1;
	}
}
	
bool AShieldInversion::RequiresTarget()
{
	return m_SkillRank != 7;
}

// --------------------------------------------

bool AShieldInversion::CanUse(long TargetID, long AbilityID, long SkillID)
{
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx() ||
		!AbilityBase::CanUseWithCurrentTarget())
	{
		return false;
	}

	return true;
}

/*
* Returns true if this skill was interrupted based on player action.
*
* Action = Shooting guns, using devices or activated effects, starting to warp,
*  anything except basic impulse or opening windows on the player's client.
*/
bool AShieldInversion::InterruptSkillOnAction(int Type)
{
	CMob *p = GetPointerToCommon();
	if(!m_InUse)
	{
		p->SetCurrentSkill();
		return false;
	}

	if(Type == OTHER)
	{
		// Remove the effect
		for(int x=m_FirstEffectID;x<m_CurrentEffectID;x++)
		{
			p->RemoveEffectRL(x);
		}
		m_CurrentEffectID = m_EffectID;
		//mark skill as not in use
		m_InUse = false;
		p->SetCurrentSkill();
		m_NextUse = 0;
		return true;
	}

	return false;
}

/*
* This will be the first function called once the skill is determined
* as useable.
*/
bool AShieldInversion::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	//Compute shield drain

	//use me after things change stat_skill_shield_inversion
	//m_ShieldDrain = CalculateShieldDrain(m_AdjustedSkillLevel, m_SkillRank); 

	m_ShieldDrain = CalculateShieldDrain(m_SkillLevel, m_SkillRank);
	m_DamageTick = m_ShieldDrain * m_Conversion;

	//ensure sufficient shielding exists to activate skill
	if(p->GetShieldValue() - m_ShieldDrain < 0)
	{
		SendError("Not enough shielding!");
		p->SetCurrentSkill();
		m_InUse = false;
		return false;
	}

	ObjectToObjectEffect NovaEffect;

	memset(&NovaEffect, 0, sizeof(NovaEffect));		// Zero out memory
	
	//color shield nova! woo!
	switch(m_SkillRank)
	{
	//case 1: do nothing, correct as is
	case 3: //green
		NovaEffect.HSVShift[0] = 240.0f;
		break;
	case 5: //purple (TODO: change to correct color)
		NovaEffect.HSVShift[0] = 56.0f;
		break;
	case 6: //yellow
		NovaEffect.HSVShift[0] = 180.0f;
		break;
	case 7: //red
		NovaEffect.HSVShift[0] = 120.0f;
		break;
	}

	NovaEffect.Bitmask = 3 | 0x100;
	NovaEffect.GameID = p->GameID();
	NovaEffect.TimeStamp = m_EffectID;
	NovaEffect.EffectID = m_EffectID;
	NovaEffect.EffectDescID = 98;
	// Store the first effect
	m_FirstEffectID = m_EffectID;
	m_CurrentEffectID = m_EffectID + 1;

	if(m_SkillRank < 7)
	{
		NovaEffect.TargetID = m_Target->GameID();
		p->SendObjectToObjectEffectRL(&NovaEffect);
	}
	else
	{
		proxparam p1(1L);
		proxparam p2(&NovaEffect);
		UseOnAllEnemiesInRange(false,p1,p2);
	}

	return true;
}

/*
* This function is called when the SetTimer call returns.
*/
bool AShieldInversion::Update(long activation_ID)
{
	m_hit_something = false;
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	if(m_Target == NULL && m_SkillRank < 7)
	{
		return false;
	}

	//Do damage once per second.
	if(p->GetShieldValue() - m_ShieldDrain < 0)
	{
		InterruptSkillOnAction(OTHER);
		return false;
	}

	//if the mob is dead, stop
	if(m_SkillRank < 7 && m_Target->GetIsIncapacitated())
	{
		InterruptSkillOnAction(OTHER);
		return false;
	}

	// See if we are in range
	if (m_SkillRank < 7 && m_Target && m_Target->RangeFrom(p) > CalculateRange(m_SkillLevel, m_SkillRank))
	{
		InterruptSkillOnAction(OTHER);
		return false;
	}

	if(m_SkillRank < 7)
	{
		m_Target->DamageObject(p->GameID(), DAMAGE_ENERGY, m_DamageTick, 0);
	}
	else
	{
		UseOnAllEnemiesInRange();

		if(!m_hit_something)
		{
			InterruptSkillOnAction(OTHER);
			return false;
		}
	}

	p->RemoveShield(m_DamageTick);
	p->SendAuxShip();

	SetTimer(1000);

	return true;
}

void AShieldInversion::ProximityAOE(CMob *target, short seq, proxparam tag, proxparam effect, proxparam p3)
{
	CMob *p = GetPointerToCommon();
	if (tag.lng == 1)
	{
		ObjectToObjectEffect *NovaEffect = (ObjectToObjectEffect *)effect.struc;
		NovaEffect->TargetID = target->GameID();
		NovaEffect->EffectID = m_CurrentEffectID++;
		p->SendObjectToObjectEffectRL(NovaEffect);
	}
	else if (target->DamageObject(p->GameID(), DAMAGE_ENERGY, m_DamageTick, 0) >= 0.0f)
		m_hit_something = true;
}

/*
* Returns true in the case that this skill can be interrupted.
* What can interrupt the skill is returned inside the OnMotion 
*  and OnDamage pointers.
*/
bool AShieldInversion::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = false;
	*OnAction = true;

	return true;
}
