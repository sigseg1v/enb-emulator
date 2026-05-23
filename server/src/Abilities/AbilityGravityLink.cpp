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

#include "AbilityGravityLink.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*
The Gravity Link function affects the objects velocity and acceleration.
If a SkillRank greater than 3 is applied it also affects the steering of the object.
The GravityForce is multiplied to velocity and acceleration so less is stronger.
When lvl 5 or 7 the objects can't move or face another direction.
Warp interferance is not implemented.
And yes...The object will hate you after this :)

Time that field applies : 30 sec

Skill properties:

SkillRank 1 : lvl 1 : Range 5k,    EnergyCost 35  , GravityForce 70% , Target only
			  lvl 2 : Range 5.25k, EnergyCost 35  , GravityForce 60% , Target only
SkillRank 3 : lvl 3 : Range 5.5k,  EnergyCost 75  , GravityForce 50% , Target only
			  lvl 4 : Range 5.75k, EnergyCost 75  , GravityForce 40% , Target only
SkillRank 5 : lvl 5 : Range 6k,    EnergyCost 150 , GravityForce 30%  , Target only
SkillRank 6 : lvl 6 : Range 6.5k,  EnergyCost 300 , GravityForce 45% , Target + Area around target
SkillRank 7 : lvl 7 : Range 7k,    EnergyCost 600 , GravityForce 20%  , target + Area around target

*/
/*
	This function calculates what gravity force we should apply to object.
	Lower is stronger.
*/
float AGravityLink::CalculateGravityForce( float SkillLevel, long SkillRank )
{
	/*
	Yes, this means Rank 6 provides a "worse" slowdown than Rank 5, but the tradeoff is you are slowing EVERYONE
	around the target down, which balances it out, and also makes sense given how the AoE ranks are just AoE
	versions of earlier skills.
	*/
	switch(SkillRank)
	{
	case 1:
		return 0.7f + ((SkillLevel-1) * 0.05f); //rank 1 @ lvl 1 = 70% max speed, R1 @ L10 = 25% max
	case 3:
		return 0.6f + ((SkillLevel-1) * 0.05f); //rank 3 @ lvl 3 = 60% max speed, R3 @ L10 = 15% max
	case 5:
		return 0.5f + ((SkillLevel-1) * 0.05f); //rank 5 @ lvl 5 = 50%, R5 @ L5 = 5% max
	case 6:
		return 0.7f + ((SkillLevel-1) * 0.05f); //rank 6 @ lvl 6 = 40% max speed, R1 @ L10 = 25% max
	case 7:
		return 0.5f + ((SkillLevel-1) * 0.05f); //rank 7 @ lvl 7 = 50%, R5 @ L5 = 5% max
	default:
		return 1.0f;
 	}
}

/*
* This calculates the activation cost of the skill.
*/
float AGravityLink::CalculateEnergy ( float SkillLevel, long SkillRank )
{
	switch(SkillRank)
	{
	case 1:
		return 35.0f;
	case 3:
		return 75.0f;
	case 5:
		return 150.0f;
	case 6:
		return 300.0f;
	case 7:
		return 600.0f;
	default:
		return 100000.0f; //makes the skill impossible to use in case SkillRank is somehow invalid
	}
}

/*
* Calculate how much time must pass before the skill activates.
*
* Results are returned in seconds.
*/
float AGravityLink::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	return (5.0f - (SkillLevel - SkillRank)) < 1.0f ? 1.0f : 5.0f - (SkillLevel - SkillRank);
}

/*
* Calculate the maximum range this rank of the skill can be used at.
  Used for current targets on not group range.
*/
float AGravityLink::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	return 4500 + 500 * SkillLevel;
}

/*
* Not used
*/
float AGravityLink::CalculateAOE ( float SkillLevel, long SkillRank )
{
	return SkillRank == 6 ? 1500.0f : 3000.0f;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long AGravityLink::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case MASS_FIELD:
		return 1;
	case GRAVITY_FIELD:
		return 3;
	case IMMOBILIZATION_FIELD:
		return 5;
	case AREA_MASS_FIELD:
		return 6;
	case AREA_IMMOBILIZATION_FIELD:
		return 7;
	default:
		return -1;
	}
}

// --------------------------------------------

bool AGravityLink::CanUse(long TargetID, long AbilityID, long SkillID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx() ||
		!AbilityBase::CanUseWithCurrentTarget())
	{
		return false;
	}

	return true;	
}

/*
* This will be the first function called once the skill is determined
* as useable.
*/
bool AGravityLink::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	ObjectEffect GravityEffect;
	memset(&GravityEffect, 0, sizeof(GravityEffect));		// Zero out memory

	GravityEffect.EffectDescID = 721;
	GravityEffect.Bitmask = 3;
	GravityEffect.GameID = p->GameID();
	GravityEffect.TimeStamp = m_EffectID;
	GravityEffect.EffectID = m_EffectID;
	GravityEffect.Duration = (short)ChargeTime;
	p->SendObjectEffectRL(&GravityEffect);

	return true;
}

/*
* This function is called when the SetTimer call returns.
*/
bool AGravityLink::Update(long activation_ID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	/* Remove effect */
	p->RemoveEffectRL(m_EffectID);

	float gravity_force = CalculateGravityForce( m_SkillLevel, m_SkillRank );
	float gravity_force_modified = 0.0f;
	long gravity_time = 30;
	float steering = 1.0f;
	
	// We use gravity link on a target 

	// Do we have a target?
	if ( m_Target )
	{
		//beam to target ship
		ObjectToObjectEffect GLinkBeamEffect;

		memset(&GLinkBeamEffect, 0, sizeof(GLinkBeamEffect));		// Zero out memory

		//beam sent to other ship
		switch(m_SkillRank)
		{
		case 1:
			GLinkBeamEffect.EffectDescID = 219;
			break;
		case 3:
			GLinkBeamEffect.EffectDescID = 302;
			break;
		case 5:
			GLinkBeamEffect.EffectDescID = 306;
			break;
		case 6:
			GLinkBeamEffect.EffectDescID = 654;
			break;
		case 7:
			GLinkBeamEffect.EffectDescID = 657;
			break;
		}

		GLinkBeamEffect.Bitmask = 3;
		GLinkBeamEffect.GameID = p->GameID();
		GLinkBeamEffect.TimeStamp = m_EffectID+1;
		GLinkBeamEffect.EffectID = m_EffectID+1;
		GLinkBeamEffect.Duration = 1000;
		GLinkBeamEffect.TargetID = m_Target->GameID();
		p->SendObjectToObjectEffectRL(&GLinkBeamEffect);
		SetObjectEffectTimer(m_EffectID+1, 1000);

		// Transfer the Gravity effect to the enemy target
		ObjectEffect GravityEffect;
		memset(&GravityEffect, 0, sizeof(GravityEffect));		// Zero out memory

		//graphic effect on other ship
		if( m_SkillRank == 1 )
			GravityEffect.EffectDescID = 247;
		else if ( m_SkillRank == 3 )
			GravityEffect.EffectDescID = 304;
		else if ( m_SkillRank == 5 )
			GravityEffect.EffectDescID = 308;
		else if ( m_SkillRank == 6 )
			GravityEffect.EffectDescID = 247;
		else
			GravityEffect.EffectDescID = 308;

		GravityEffect.Bitmask = 3;
		GravityEffect.TimeStamp = m_EffectID+2;
		GravityEffect.EffectID = m_EffectID+2;
		GravityEffect.Duration = (short)gravity_time*1000;

		//Certain ranks of GLink have additional effects, so one more object effect for those too
		ObjectEffect GravityEffectSpecial;
		memset(&GravityEffectSpecial, 0, sizeof(GravityEffectSpecial));		// Zero out memory

		//graphic effect on other ship
		if( m_SkillRank == 1 )
			GravityEffectSpecial.EffectDescID = 301;
		else if ( m_SkillRank == 3 )
			GravityEffectSpecial.EffectDescID = 305;
		else if ( m_SkillRank == 5 )
			GravityEffectSpecial.EffectDescID = 409;
		else if ( m_SkillRank == 6 )
			GravityEffectSpecial.EffectDescID = 301;
		else
			GravityEffectSpecial.EffectDescID = 409;

		GravityEffectSpecial.Bitmask = 3;
		GravityEffectSpecial.TimeStamp = m_EffectID+12;
		GravityEffectSpecial.EffectID = m_EffectID+12;

		m_Target->SendClientSound("On_Gravity_Linked", 1, 1);

		if(m_SkillRank < 6)
		{
			GravityEffect.GameID = m_Target->GameID();
			p->SendObjectEffectRL(&GravityEffect);
			SetObjectEffectTimer(m_EffectID+2, 1000);

			GravityEffectSpecial.GameID = m_Target->GameID();
			p->SendObjectEffectRL(&GravityEffectSpecial);
			SetObjectEffectTimer(m_EffectID+12, 1000);

			// +/- 2% of level difference
			gravity_force_modified = gravity_force - ((p->CombatLevel() - m_Target->Level()) * 0.02f);

			if (gravity_force_modified > 1.0f)
				gravity_force_modified = 1.0f;

			if (gravity_force_modified < 0.0f)
				gravity_force_modified = 0.0f;

			steering = (m_SkillRank > 3) ? gravity_force_modified : 1.0f;

			// Apply gravity field on object
			m_Target->SetGravityField(gravity_force_modified,steering,gravity_time);

			// Add a little love :)
			m_Target->AddHate(p->GameID(),1);

			//if rank 3, 5, or 7 deny m_Target warp
			if(m_SkillRank == 3 || m_SkillRank == 5 || m_SkillRank == 7)
			{
				Buff deny_warp;
				memset(&deny_warp, 0, sizeof(Buff));
				for(int i = 0; i < 5; i++)
				{
					deny_warp.EffectID[i] = -1;
				}
				deny_warp.ExpireTime = GetNet7TickCount() + gravity_time * 1000;
				deny_warp.IsPermanent = false;
				strcpy_s(deny_warp.Stats[0].StatName, sizeof(deny_warp.Stats[0].StatName), STAT_WARP);
				deny_warp.Stats[0].StatName[sizeof(deny_warp.Stats[0].StatName)-1] = '\0';
				deny_warp.Stats[0].Value = -1 * m_Target->m_Stats.GetStat(STAT_WARP);
				deny_warp.Stats[0].StatType = STAT_DEBUFF_VALUE;
				strcpy_s(deny_warp.BuffType, sizeof(deny_warp.BuffType), "Gravity Link Warp Disrupt");
				deny_warp.BuffType[sizeof(deny_warp.BuffType)-1] = '\0';
				m_Target->m_Buffs.RemoveBuff(deny_warp.BuffType);
				m_Target->m_Buffs.AddBuff(&deny_warp);
			}

			//if level 5, apply Impact/Explosive debuff
			if(m_SkillRank == 5 || m_SkillRank == 7)
			{
				Buff deflect_debuff;
				memset(&deflect_debuff, 0, sizeof(Buff));
				for(int i = 0; i < 5; i++)
				{
					deflect_debuff.EffectID[i] = -1;
				}
				deflect_debuff.ExpireTime = GetNet7TickCount() + gravity_time * 1000;
				deflect_debuff.IsPermanent = false;
				strcpy_s(deflect_debuff.Stats[0].StatName, sizeof(deflect_debuff.Stats[0].StatName), STAT_EXPLOSIVE_DEFLECT);
				deflect_debuff.Stats[0].StatName[sizeof(deflect_debuff.Stats[0].StatName)-1] = '\0';
				deflect_debuff.Stats[0].Value = 30.0f;
				deflect_debuff.Stats[0].StatType = STAT_DEBUFF_VALUE;
				strcpy_s(deflect_debuff.Stats[1].StatName, sizeof(deflect_debuff.Stats[1].StatName), STAT_IMPACT_DEFLECT);
				deflect_debuff.Stats[1].StatName[sizeof(deflect_debuff.Stats[1].StatName)-1] = '\0';
				deflect_debuff.Stats[1].Value = 30.0f;
				deflect_debuff.Stats[1].StatType = STAT_DEBUFF_VALUE;
				strcpy_s(deflect_debuff.BuffType, sizeof(deflect_debuff.BuffType), "Gravity Link Deflect Damage");
				deflect_debuff.BuffType[sizeof(deflect_debuff.BuffType)-1] = '\0';
				m_Target->m_Buffs.RemoveBuff(deflect_debuff.BuffType);
				m_Target->m_Buffs.AddBuff(&deflect_debuff);
			}

			//p->SendVaMessage("Slowed %f", gravity_force_modified); 
		}
		else if(m_SkillRank >= 6)
		{
			// Area GravityLink
			proxparam p1(gravity_force);
			proxparam p2(&GravityEffect);
			proxparam p3(&GravityEffectSpecial);
			UseOnAllEnemiesInRange(true,p1,p2,p3);
		}		
	}
	else
	{
		SendError("Lost Target");
	}

	p->SetCurrentSkill();
	m_InUse = false;

	return true;
}

void AGravityLink::ProximityAOE(CMob *target, short seq, proxparam force, proxparam effect1, proxparam effect2)
{
	CMob *p = GetPointerToCommon();
	float gravity_force = force.flt;
	float gravity_force_modified = 0.0f;
	long gravity_time = 30;
	float steering = 1.0f;

	// +/- 2% of level difference
	gravity_force_modified = gravity_force - ((p->CombatLevel() - target->Level()) * 0.02f);

	if (gravity_force_modified > 1.0f)
		gravity_force_modified = 1.0f;

	if (gravity_force_modified < 0.0f)
		gravity_force_modified = 0.0f;

	steering = (m_SkillRank > 3) ? gravity_force_modified : 1.0f;

	// Transfer the Gravity effect to the enemy target
	ObjectEffect *GravityEffect = (ObjectEffect *)effect1.struc;
	GravityEffect->EffectID = m_EffectID+2+seq;
	GravityEffect->GameID = target->GameID();
	p->SendObjectEffectRL(GravityEffect);
	SetObjectEffectTimer(m_EffectID+2+seq, 1000);
		 

	//Certain ranks of GLink have additional effects, so one more object effect for those too
	ObjectEffect *GravityEffectSpecial = (ObjectEffect *)effect1.struc;
	GravityEffectSpecial->EffectID = m_EffectID+12+seq;
	GravityEffectSpecial->GameID = target->GameID();
	p->SendObjectEffectRL(GravityEffectSpecial);
	SetObjectEffectTimer(m_EffectID+12+seq, 1000);

	// Apply gravity field on object
	target->SetGravityField(gravity_force_modified,steering,gravity_time);
	// Add a little love :)
	target->AddHate(p->GameID(),1);

	//only Rank 7 can appear in here, and do something, so uber debuff time! 
	if(m_SkillRank == 7)
	{
		Buff uber_debuff;
		memset(&uber_debuff, 0, sizeof(Buff));
		for(int i = 0; i < 5; i++)
		{
			uber_debuff.EffectID[i] = -1;
		}
		//basic properties
		uber_debuff.ExpireTime = GetNet7TickCount() + gravity_time * 1000;
		uber_debuff.IsPermanent = false;
		strcpy_s(uber_debuff.BuffType, sizeof(uber_debuff.BuffType), "Gravity Link Warp Disrupt");
		uber_debuff.BuffType[sizeof(uber_debuff.BuffType)-1] = '\0';

		//deny warp
		strcpy_s(uber_debuff.Stats[0].StatName, sizeof(uber_debuff.Stats[0].StatName), STAT_WARP);
		uber_debuff.Stats[0].StatName[sizeof(uber_debuff.Stats[0].StatName)-1] = '\0';
		uber_debuff.Stats[0].Value = -1 * target->m_Stats.GetStat(STAT_WARP);
		uber_debuff.Stats[0].StatType = STAT_DEBUFF_VALUE;

		//30% bonus to impact/explosive damage
		strcpy_s(uber_debuff.Stats[1].StatName, sizeof(uber_debuff.Stats[1].StatName), STAT_EXPLOSIVE_DEFLECT);
		uber_debuff.Stats[1].StatName[sizeof(uber_debuff.Stats[1].StatName)-1] = '\0';
		uber_debuff.Stats[1].Value = 30.0f;
		uber_debuff.Stats[1].StatType = STAT_DEBUFF_VALUE;
		strcpy_s(uber_debuff.Stats[2].StatName, sizeof(uber_debuff.Stats[2].StatName), STAT_IMPACT_DEFLECT);
		uber_debuff.Stats[2].StatName[sizeof(uber_debuff.Stats[2].StatName)-1] = '\0';
		uber_debuff.Stats[2].Value = 30.0f;
		uber_debuff.Stats[2].StatType = STAT_DEBUFF_VALUE;

		//place debuff on target
		target->m_Buffs.RemoveBuff(uber_debuff.BuffType);
		target->m_Buffs.AddBuff(&uber_debuff);
	}

	if(target == m_Target && m_SkillRank > 5)
	{
		ObjectEffect AreaRing;
		memset(&AreaRing, 0, sizeof(AreaRing));		// Zero out memory

		AreaRing.EffectDescID = m_SkillRank == 6 ? 656 : 659;
		AreaRing.Bitmask = 3;
		AreaRing.GameID = p->GameID();
		AreaRing.TimeStamp = m_EffectID + 7;
		AreaRing.EffectID = m_EffectID + 7;
		AreaRing.Duration = 1000;
		p->SendObjectEffectRL(&AreaRing);
		SetObjectEffectTimer(m_EffectID+7, 1000);
	}
}

/*
* What can interrup this skill.
*/
bool AGravityLink::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = false;
	*OnAction = true;

	return true;
}
