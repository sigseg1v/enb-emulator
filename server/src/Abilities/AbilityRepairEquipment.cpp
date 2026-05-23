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

#include "AbilityRepairEquipment.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*

The Repair Skill allways repairs the players equipment. If SkillRank is greater or equal to 3 the current
target is repaired if in range. The players group is repaired if SkillRank is greater
or equal to 6. If the current target is in players group the properties for repairing a target applies.
No incapacitated ships are repaired and the energy for the current level should be present.
Interruptable when going to warp.

Repairing is done by calculating a base repair rate with a multiplier. The resulting repair_sum is how much damage
we should repair on each round. If the repair_sum is greater than all equipment damage on the ship all equipment will
get 100% structure. If the repair_sum is not sufficient all equipment is repaired with an equal percentage calculated
with repair_sum / total_damage .

Skill properties:

Repair baserate = 0.01f

lvl 1: Repair only self at baserate. EnergyCost 50
lvl 2: Repair only self at baserate * 2.5 . EnergyCost 50
lvl 3: Repair self and target within range 3k at baserate * 3 . EnergyCost 150
lvl 4: Repair self and target within range 3.25k at baserate * 5 . EnergyCost 150
lvl 5: Repair self and target within range 3.5k at baserate * 15 . EnergyCost 250
lvl 6: Repair self and target within range 3.75k at baserate * 30, Group repair within range 1.5k at baserate * 6 . EnergyCost 300
lvl 7: Repair self and target within range 4.0k at baserate * 60, Group repair within range 3k at baserate * 20 . EnergyCost 350

SkillRank 1 uses : lvl 1 and 2
SkillRank 3 uses : lvl 3 and 4
SkillRank 5 uses : lvl 5
SkillRank 6 uses : lvl 6
SkillRank 7 uses : lvl 7
*/

/*
	This function calculates the total equipment damage for player p.
	It stores the structure of each equipment if damaged and calculates the
	sum of all damage to equipment structure.
	Returns the total structure damage.
*/
float ARepairEquipment::CalculateDamage (Player* p2, equipment_damage* damage )
{
	damage->player = p2;
	damage->total_damage = 0;

	// Sweep over the equipment
	for(u32 i=0;i<20;i++) 
	{
		// Init current equipment slot with -1. This is for no damage to structure.
		damage->equipID[i] = -1;
		if(p2->ShipIndex()->Inventory.EquipInv.EquipItem[i].GetItemTemplateID() >= 0)
		{
			float structure = p2->ShipIndex()->Inventory.EquipInv.EquipItem[i].GetStructure();
			if(structure < 1.0f)
			{
				// Save the damaged structure points in the right index.
				damage->equipID[i] = structure;
				// Increment the total damage
				damage->total_damage += 1.0f - damage->equipID[i];
			}
		}
	}
	// Return total damage on equipment
	return damage->total_damage; 
}

/*
	The function repairs the damage to equipment structure. The repair_rate
	sets how much damage that should be repaired and if the repair_rate is 
	sufficient to repair all equipment all structure will return to 100%.
	If the repair_rate is less than equipment total damage all equipment will
	be repaired with an percentage.
	The function returns total structure repaired.
*/

float ARepairEquipment::RepairDamage ( float repair_rate , equipment_damage* damage )
{
	float total_repaired = 0;
	float percent;
	if(repair_rate >= damage->total_damage)
	{
		// Repair rate is sufficient to repair all damage to equipment structure.
		percent = 1.0f;
	}
	else
	{
		// Calculate the repair percentage.
		percent = repair_rate / damage->total_damage;
	}

	// Sweep over the equipment
	for(u32 i=0;i<20;i++) 
	{
		// If the current equipment element is damaged
		if(damage->equipID[i] != -1)
		{
			float repair_value = 0;
			if(percent == 1.0f)
			{
				// Full repair to the current element
				repair_value = 1.0f;
				total_repaired += 1.0f - damage->equipID[i];
			}
			else
			{
				// Partial repair to the current element
				repair_value = damage->equipID[i] + ((1.0f - damage->equipID[i]) * percent);
				total_repaired += ((1.0f - damage->equipID[i]) * percent);
			}
			// Update the structure of equipment element in database
			damage->player->RestoreEquipmentStructure(i,repair_value);
		}
	}
	// Return total points repaired
	return total_repaired;
}

/*
	This function checks and repairs equipment damage with Player p.
	If no damage the function simply returns but if damage the function
	notifies the player how the repair went.
*/

int ARepairEquipment::RepairTarget( Player* p2 , float repair_rate )
{
	if (!p2) return -1;
	Player *p = GetPointerToPlayer();

	equipment_damage damage;
	float dmg,fixed;
	dmg = CalculateDamage (p2 , &damage );
	if(!dmg)
	{
		// No damage to repair
		return -1;
	}
	fixed = RepairDamage ( repair_rate , &damage );
	if(fixed != dmg)
	{		
		if(p2 != p)
			p2->SendVaMessage("You got a partial equipment repair from : %s",p->ShipIndex()->GetOwner());
		else
			p2->SendVaMessage("Computer : Only partial repair was done.");
	}
	else
	{		
		if(p2 != p)
			p2->SendVaMessage("You got a total equipment repair from : %s",p->ShipIndex()->GetOwner());
		else
			p2->SendVaMessage("Computer : Full repair was done.");
	}
	return 0;
}	

/*
* This calculates the activation cost of the skill.
*/
float ARepairEquipment::CalculateEnergy ( float SkillLevel, long SkillRank )
{
	return 50.0f * SkillRank;
}

/*
* Calculate how much time must pass before the skill activates.
*
* Results are returned in seconds.
*/
float ARepairEquipment::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	return SkillLevel < 6.0f ? 8.0f - SkillLevel : 3.0f;
}

/*
* Calculate the maximum range this rank of the skill can be used at.
  Used for current targets on not group range.
*/
float ARepairEquipment::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	float range = 0.0f;

	if( SkillRank < 5 )
	{
		if( SkillLevel < 4 )
			range = 3000.0f;
		else
			range = 3250.0f;
	}
	else if ( SkillRank == 5 )
	{
		range = 3500.0f;
	}
	else if (SkillRank == 6 )
	{
		range = 3750.0f;
	}
	else
	{
		range = 4000.0f;
	}

	return range;
}

float ARepairEquipment::CalculateAOE ( float SkillLevel, long SkillRank )
{
	return SkillRank == 6 ? 1500.0f : (SkillRank == 7 ? 3000.0f : 0.0f);
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long ARepairEquipment::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
	case REGENERATE_EQUIPMENT:
		return 1;
	case REPAIR_EQUIPMENT:
		return 3;
	case COMBAT_EQUIPMENT_REPAIR:
		return 5;
	case AREA_EQUIPMENT_REPAIR:
		return 6;
	case IMPROVED_AREA_REPAIR:
		return 7;
	default:
		return -1;
	}
}

// --------------------------------------------

bool ARepairEquipment::CanUse(long TargetID, long AbilityID, long SkillID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx(false) ||
		!AbilityBase::CanUseWithCurrentTarget(true))
	{
		return false;
	}

	return true;	
}

/*
* This will be the first function called once the skill is determined
* as useable.
*/
bool ARepairEquipment::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	ObjectEffect RepairEffect;
	memset(&RepairEffect, 0, sizeof(RepairEffect));		// Zero out memory

	RepairEffect.Bitmask = 3;
	RepairEffect.GameID = p->GameID();
	RepairEffect.TimeStamp = m_EffectID;
	RepairEffect.EffectID = m_EffectID;
	RepairEffect.Duration = (short)ChargeTime;
	RepairEffect.EffectDescID = 170;
	p->SendObjectEffectRL(&RepairEffect);

	return true;
}

/*
* This function is called when the SetTimer call returns.
*/
bool ARepairEquipment::Update(long activation_ID)
{
	Player *p = GetPointerToPlayer();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	float repair_rate,rate_mult,group_range;
	long TimeStamp;

	/* Remove effect */
	p->RemoveEffectRL(m_EffectID);

	repair_rate = 0.01f;

	// Calculate the repair multiplier
	if( m_SkillRank == 1 )
	{	
		if( m_SkillLevel == 1.0f )
			rate_mult = 1.0f;
		else
			rate_mult = 2.5f;
	}
	else if ( m_SkillRank == 3 )
	{
		if( m_SkillLevel == 3.0f )
			rate_mult = 3.0f;
		else
			rate_mult = 5.0f;
	}
	else if ( m_SkillRank == 5 )
	{
		rate_mult = 15.0f;
	}
	else if ( m_SkillRank == 6 )
	{
		rate_mult = 30.0f;
	}
	else
	{
		rate_mult = 60.0f;
	}
	
	// First repair player ship. This should always be done.
	RepairTarget(p , repair_rate * rate_mult);

	// Then repair target if valid and m_SkillRank >= 3
	if( m_Target && m_Target != p && m_SkillRank >= 3 )
	{
		// Repair target
		int result = RepairTarget(GetTargetAsPlayer() , repair_rate * rate_mult );
		if(result == 0)
		{
			// We have fixed damage of target ship
			// show repairbeam and repair effect at target
			m_EffectID = GetNet7TickCount();
			TimeStamp = m_EffectID;										// Get TimeStamp

			ObjectToObjectEffect RepairEffect;
			memset(&RepairEffect, 0, sizeof(RepairEffect));				// Zero out memory	
			RepairEffect.Bitmask = 3;
			RepairEffect.GameID = p->GameID();
			RepairEffect.TimeStamp = TimeStamp;
			RepairEffect.EffectID = m_EffectID;						    // Unique EffectID
			RepairEffect.Duration = 1000;
			RepairEffect.EffectDescID = 174;
			RepairEffect.TargetID = m_Target->GameID();
			p->SendObjectToObjectEffectRL(&RepairEffect);
			SetObjectEffectTimer(m_EffectID, 1000);
		}
	}

	// Then repair groupmembers if in range and m_SkillRank >= 6

	if ( m_SkillRank >= 6 )
	{
		// Calculate group repair rate and group range
		if (m_SkillRank == 6 )
		{
			rate_mult = 6.0f;
			group_range = 1500.0f;
		}
		else
		{
			rate_mult = 20.0f;
			group_range = 3000.0f;
		}

		// Get TickCount for all members. This is used to calculate an unique for each
		m_EffectID = GetNet7TickCount();
		GetFriendlyGroup();
		for(u32 i=0;i < 6;i++)
		{
			m_Target = m_AOEFriendList[i];
			if (m_Target && m_Target != p)
			{
				// Repair group member
				int result = RepairTarget(GetTargetAsPlayer() , repair_rate * rate_mult);
				if(result == 0)
				{
					// We have fixed damage of target ship
					// show repairbeam and repair effect at target
					TimeStamp = GetNet7TickCount();								// Get TimeStamp

					ObjectToObjectEffect RepairEffect;
					memset(&RepairEffect, 0, sizeof(RepairEffect));				// Zero out memory	
					RepairEffect.Bitmask = 3;
					RepairEffect.GameID = p->GameID();
					RepairEffect.TimeStamp = TimeStamp;
					RepairEffect.EffectID = m_EffectID+i;					    // Unique EffectID
					RepairEffect.Duration = 1000;
					RepairEffect.EffectDescID = 174;
					RepairEffect.TargetID = m_Target->GameID();
					p->SendObjectToObjectEffectRL(&RepairEffect);
					SetObjectEffectTimer(m_EffectID+i, 1000);
				}
			}
		}
	}

	p->SetCurrentSkill();
	m_InUse = false;

	return true;
}

/*
* What can interrup this skill.
*/
bool ARepairEquipment::SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction)
{
	*OnMotion = false;
	*OnDamage = false;
	*OnAction = true;

	return true;
}
