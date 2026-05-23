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

#include "AbilityWormHole.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"

/*
* This calculates the activation cost of the skill.
*/
float AWormHole::CalculateEnergy ( float SkillLevel, long SkillRank )
{
	float EnergyCost = 0.0f;

	switch (SkillRank)
	{
		case 1:
			EnergyCost = 35;
			break;
		case 2:
			EnergyCost = 50;
			break;
		case 3:
			EnergyCost = 75;
			break;
		case 4:
			EnergyCost = 100;
			break;
		case 5:
			EnergyCost = 150;
			break;
		case 6:
			EnergyCost = 250;
			break;
		case 7:
			EnergyCost = 350;
			break;
		default:
			EnergyCost = 350;
			break;
	}

	return EnergyCost;
}





/*
 * See what SectorID we should use based on the ability thats used
 */
int AWormHole::GetSectorID( long SkillRank )
{
	int SectorID = 0;

	switch (SkillRank)
	{
		case 1: // KAILAASA
			SectorID = 1910;
			break;
		case 2: // Jupiter
			SectorID = 1070;
			break;
		case 3: // SWOOPING_EAGLE
			SectorID = 4120;
			break;
		case 4: // VALKYRIE_TWINS
			SectorID = 1705;
			break;
		case 5: // ASTEROID_BELT_BETA
			SectorID = 1077;
			break;
		case 6: // CARPENTER
			SectorID = 4520;
			break;
		case 7: // ENDRIAGO
			SectorID = 2210;
			break;
	}
	return SectorID;
}

/*
* Calculate how much time must pass before the skill activates.
*
* Results are returned in seconds.
*/
float AWormHole::CalculateChargeUpTime ( float SkillLevel, long SkillRank )
{
	return 3.0f;
}

/*
* Calculate the maximum range this rank of the skill can be used at.
*/
float AWormHole::CalculateRange ( float SkillLevel, long SkillRank ) 
{
	return 5000.0f;
}

/*
* Determine's which rank of the skill was used based on the SkillID.
*/
long AWormHole::DetermineSkillRank(int SkillID)
{
	switch(SkillID)
	{
		case WORMHOLE_KAILAASA:
			return 1;
		case WORMHOLE_JUPITER:
			return 2;
		case WORMHOLE_SWOOPING_EAGLE:
			return 3;
		case WORMHOLE_VALKYRIE_TWINS:
			return 4;
		case WORMHOLE_ASTEROID_BELT_BETA:
			return 5;
		case WORMHOLE_CARPENTER:
			return 6;
		case WORMHOLE_ENDRIAGO:
			return 7;
		default:
			return -1;
	}
}

// --------------------------------------------

bool AWormHole::CanUse(long TargetID, long AbilityID, long SkillID)
{
	CMob *p = GetPointerToCommon();
	if (!AbilityBase::CanUse(TargetID,AbilityID,SkillID) ||
		!AbilityBase::CanUseEx())
	{
		return false;
	}

	return true;
}


/*
* Send confirmation to a player.
*/
void AWormHole::Confirmation(bool Confirm, long AbilityID, long GameID)
{
	CMob *p = GetPointerToCommon();
    Player *p2 = g_PlayerMgr->GetPlayer(GameID);

	if (!p2) return;

	if (!p) {
		p2->SendVaMessage("Can't find player that wormholed you!");
		return;
	}


    if (Confirm && p2)
    {
        // Make the player wormhole
        p2->WormHole(GetSectorID(m_SkillRank));

        // Damage all cargo in the ship's inventory
        int count = p2->DamageTradeCargo(0.5f);
        if (count > 0)
            p2->SendVaMessage("%d trade cargo damaged.", count);
    }

    //reguardless of what the player chooses, this is the "end" of the skill, mark it as such.
    if(p == p2)
    {
        p->SetCurrentSkill();
        m_DamageTaken = 0.0f;
        m_InUse = false;
    }
}

/*
* This will be the first function called once the skill is determined
* as useable.
*/
bool AWormHole::UseSkill(long ChargeTime)
{
	CMob *p = GetPointerToCommon();

	ObjectEffect WormholeEffect;
	memset(&WormholeEffect, 0, sizeof(WormholeEffect));		// Zero out memory

	WormholeEffect.EffectDescID = 689;
	WormholeEffect.Bitmask = 3;
	WormholeEffect.GameID = p->GameID();
	WormholeEffect.TimeStamp = m_EffectID;
	WormholeEffect.EffectID = m_EffectID;
	WormholeEffect.Duration = 1000;
	p->SendObjectEffectRL(&WormholeEffect);

	return true;
}

/*
* This function is called when the SetTimer call returns.
*/
bool AWormHole::Update(long activation_ID)
{
	Player *p = GetPointerToPlayer();
	if (!AbilityBase::Update(activation_ID))
	{
		return false;
	}

	p->RemoveEffectRL(m_EffectID);	

	// Send a message to all the players in range in the group
	Player * p2 = NULL;
	int GroupID = p->GroupID();
	if (GroupID != -1)
	{
		for(int MID = 0; MID < 6; MID++)
		{
			int MGameID = g_PlayerMgr->GetMemberID(GroupID, MID);

			// Send to everyone but ourself!
			if (MGameID != p->GameID() && MGameID != -1)
			{
				p2 = g_ServerMgr->m_PlayerMgr.GetPlayer(MGameID);
				if (p2)
				{
					// Make sure we are in the same sector
					if (p2->PlayerIndex()->GetSectorNum() == p->PlayerIndex()->GetSectorNum())
					{
						// See if we are now in range of the player
						if (p2->RangeFrom(p) < CalculateRange(m_SkillLevel, m_SkillRank))
						{
							p2->SendConfirmation("All cargo items in your inventory will take 50% durability damage if you take this wormHole.  Do you want to take this WormHole?", p->GameID(), m_AbilityID);
						}
					}
				}
			}
		}
	}
	p->SendConfirmation("All cargo items in your inventory will take 50% durability damage if you take this wormHole.  Do you want to take this WormHole?", p->GameID(), m_AbilityID);
	
	p->SetCurrentSkill();
	m_InUse = false;
	return true;
}
