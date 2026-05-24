// PlayerCombat.cpp
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

#include <float.h>
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"
#include <net7/Opcodes.h>
#include "PacketMethods.h"
#include "StaticData.h"

float Player::DamageObject(long source_id, long damage_type, float damage, long inflicted)
{
	ObjectManager *om = GetObjectManager();

	if (!om)
	{
		return 0;
	}

	//make a player who is in warp immune to damage. No more getting shot out of / during warp.
	if(!IsWarpCharging() && WarpDrive())
	{
		return 0;
	}

	Object *obj = om->GetObjectFromID(source_id);
	if (obj && !DebugPlayer())
	{
		float damage_orig = damage;

		//terminate warp, prospecting
		//TerminateWarp();						// This should only be done for hull damage
		AbortProspecting(true, false);

		damage = CommonDamageHandling(obj,damage_type,damage);

		// Find out if we absorbed any damage with the deflects
		float DamageAbsorbed = damage - damage_orig;
		SendClientDamage(GameID(), source_id, damage, DamageAbsorbed, damage_type, 1);

		bool send_shield_hit = GetShieldValue() >= 0.0f;
		if (send_shield_hit)
		{
			Player *p = (0);

			while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))
			{
				p->SendObjectToObjectLinkedEffect(this, obj, 0x0021, 0x0003);
			}
		}

		//DB: Note - Shake does not occur on first hit
		//Shake stops when you run out of shields as it expects hull to decrease then
		ShipIndex()->Shake.SetDamage(damage);
	}

	return damage;
}

void Player::RemoveHull(float hull_dmg, CMob *enemy)
{
    //float hull = ShipIndex()->GetHullPoints() - hull_dmg;
	float hullOld = ShipIndex()->GetHullPoints();
	float hull = hullOld - hull_dmg;

	if (hullOld/ShipIndex()->GetMaxHullPoints() > 0.25f && hull/ShipIndex()->GetMaxHullPoints() <= 0.25f)
	{
		//SendClientSound("Hull at 25%");
	}

    ShipIndex()->SetHullPoints(hull);

	// new function added to implement equipment damage
	DamageEquipment(hull_dmg,hull,enemy);

    //If we have used all of the hull, the player goes BOOM! (but only once!)
    if (hull <= 0.0f && !GetIsIncapacitated())
    {
       	ShipIndex()->SetIsIncapacitated(true);

		SectorManager *sm = GetSectorManager();
        //damage any trade cargo in the player's cargo hold
        int count = DamageTradeCargo(0.5f);
        if (count > 0)
            SendVaMessage("%d trade cargo damaged.", count);

        ShipIndex()->SetHullPoints(0);
 		SendClientSound("On_Death");
		CalculateXPDebt();

		if(GetCurrentSkill())
		{
			GetCurrentSkill()->InterruptSkillOnAction(INCAPACITATE);
		}

		// Break formation/Leave Formation if in group
		if (GroupID() != -1)
		{
			g_PlayerMgr->BreakFormation(GameID());
			g_PlayerMgr->LeaveFormation(GameID());
		}

		// Stop regen
		RemoveEnergy(0);
		ShipIndex()->Shield.SetStartValue(GetShield());
		ShipIndex()->Shield.SetEndTime(GetNet7TickCount());		// Set end time now!

        ImmobilisePlayer();
        TerminateWarp();    //This sends the packet (TODO: Remove packet send from here)

        //now deselect player's target - this causes the immobilisation to occur
        UnSetTarget(-1);

        //Stop any mobs from attacking
		//ObjectManager *om = GetObjectManager();
        //if (om) om->RemovePlayerFromMOBRangeLists(this);
        if (enemy && enemy->HostilityTarget() == this->GameID())
        {
            enemy->LostTarget(this, true);
        }

		// make player explosion more dramatic
		ObjectEffect ObjExplosion;
		ObjExplosion.Bitmask = 0x07;
		ObjExplosion.GameID = GameID();
		ObjExplosion.EffectDescID = 0x0191; //shockwave
		if (sm)
			ObjExplosion.EffectID = sm->GetNextEffectID();
		else
			ObjExplosion.EffectID = GameID() - 0x100000; //choose an ID that should be safe
		ObjExplosion.TimeStamp = GetNet7TickCount();
		ObjExplosion.Duration = 4000;

        Player *p = (0);

		while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))
		{
            p->PointEffect(Position(), 1017);
			p->SendObjectEffect(&ObjExplosion);
        }
    }

	// Send Data
	SendAuxShip();
}

void Player::ImmobilisePlayer()
{
    ShipIndex()->SetLockOrient(true);
    ShipIndex()->SetLockSpeed(true);
    ShipIndex()->SetEngineThrustState(0);  
}

void Player::RemobilisePlayer()
{
    ShipIndex()->SetLockOrient(false);
    ShipIndex()->SetLockSpeed(false);
}

void Player::SelfDestruct()
{
	// Immobilise and incapacitate player
	ShipIndex()->SetIsIncapacitated(true);
	ImmobilisePlayer();

	// Toggle the selfdestruted properties for player
	SetSelfDestructed(true);

	// Damage Cargo Items
    int count = DamageTradeCargo(0.5f);
	if (count > 0)
		SendVaMessage("%d trade cargo damaged.", count);

	// Remove all shield and energy from ship.
	RemoveShield(GetShieldValue());
	RemoveEnergy(GetEnergyValue());
}

// Set and reset selfdestructed properties for player
void Player::SetSelfDestructed(bool update)
{
	m_SelfDestructed = update;
}

void Player::AttackMusicUpdate(bool update, long mob_id)
{
    if (update)
    {
        m_AttackerCount++;
        SendAttackerUpdates(ntohl(mob_id), 1);
    }
    else
    {
        m_AttackerCount--;
        SendAttackerUpdates(ntohl(mob_id), 0);
        
        if (m_AttackerCount < 0)
        {
            LogMessage("**** ERROR *: Attack count negative for %s\n", Name());
			m_AttackerCount = 0;
        }
    }
}

void Player::FireAllWeapons()
{
    if (ShipIndex()->GetTargetGameID() > 0)
    {
        for (int i=0; i<6; i++)
        {
            m_Equip[i+3].ManualActivate();
        }
        SendPacketCache();
    }
}

bool Player::FireEnergyCannon(ItemInstance *item)
{
	bool Ignored = false, OnAction = false;
    float pos[3];
    float *_heading = Heading();
    float range = (float)item->WeaponRange;
    
    if (GetEnergyValue() < item->EnergyUse)
    {
        SendVaMessage("Not enough energy! Need: %f", item->EnergyUse);
        return false;
    }
   
    /* Use the energy */
    RemoveEnergy(item->EnergyUse);

	if(m_CurrentSkill && m_CurrentSkill->SkillInterruptable(&Ignored, &Ignored, &OnAction))
	{
		if(OnAction)
		{
			m_CurrentSkill->InterruptSkillOnAction(SHOOTING);
		}
	}
       
    pos[0] = PosX() + ( range * _heading[0] + (rand()%50 - 25));
    pos[1] = PosY() + ( range * _heading[1] + (rand()%50 - 25));
    pos[2] = PosZ() + ( range * _heading[2] + (rand()%50 - 25));
   
    RangeListVec::iterator itrRList;
    Player *p = (0);

	//needs to be all players sector wide

    u32 * sector_list = GetSectorPlayerList();

	while (g_PlayerMgr->GetNextPlayerOnList(p, sector_list))
	{
		float range_to_blast = p->RangeFrom(pos, true);
		if (range_to_blast < 15000.0f)
		{
			p->PointEffect(pos, 1017, 3.0f);
			//see if this player is in range of explosion.
			float range_to_blast = p->RangeFrom(pos, true);

			if (range_to_blast < 1000.0f)
			{
				float damage = CaughtInEnergyBlast(range_to_blast);
				if (damage > 0.0f)
				{
					damage *= item->WeaponDamage;
					p->DamageObject(GameID(), DAMAGE_ENERGY, damage, 0);
					SendVaMessage("Blast Damage: %.2f", damage);
					SendClientDamage(p->GameID(), GameID(), damage, 0, 0);
				}
			}
		}

		//now check MOBs
		SectorManager *sm = GetSectorManager();
		sm->MOBBlastDamage(this, pos, (float)item->WeaponDamage);
	}

    return true;
}

short Player::CalcMissChanceVersus(int subcat, short mob_level)
{
	short miss_chance = 5; //5% base miss_chance
	short level_diff = mob_level - (short)CombatLevel();
	float weapon_skill = 0.0f;

	if (level_diff > 0)
		miss_chance += level_diff*level_diff/5; // +10->20% +16(raid)->51%, +20->80% +25 impossible to hit
	else
		miss_chance += level_diff; // subtract

	// weapon skill mods
    switch(subcat)
    {
    case IB_SUBCATEGORY_BEAM_WEAPON:
		weapon_skill = m_Stats.ModifyValueWithStat(STAT_BEAM_ACCURACY,100.0f)-100.0f;
        break;

    case IB_SUBCATEGORY_PROJECTILE_LAUNCHER:
		weapon_skill = m_Stats.ModifyValueWithStat(STAT_PROJECTILE_ACCURACY,100.0f)-100.0f; 
        break;

    case IB_SUBCATEGORY_MISSILE_LAUNCHER:
		weapon_skill = m_Stats.ModifyValueWithStat(STAT_MISSILE_ACCURACY,100.0f)-100.0f; 
        break;
    }
	if (weapon_skill > 300.0f)
		weapon_skill = 300.0f;
	miss_chance -= (short)(weapon_skill / 10.0f); // 30% at max, giving 74% chance to hit raid bosses (66) fully buffed 
	return miss_chance;
}

float Player::CalcDamage(int weapon_damage, int subcat, bool *critical, int mob_level)
{
	const short FRACTION_MULTIPLIER = 10000; //This value determines how many decimals of a percent are kept for rolling damage tables.
	const short FRACTION_FIXUP = 100;
    float damage_bonus;
    float damage_inflicted;
    short critical_chance = 5 * FRACTION_FIXUP; //5%
	short miss_chance = CalcMissChanceVersus(subcat,mob_level) * FRACTION_FIXUP;

	//This calculates a different crit value for any class that should have critical targeting.
    critical_chance += (short) 
		( (m_Stats.GetStatType(STAT_CRITICAL_RATE, STAT_BUFF_MULT)*FRACTION_MULTIPLIER) +
		  (m_Stats.GetStatType(STAT_CRITICAL_RATE, STAT_BUFF_VALUE)*FRACTION_FIXUP) ); 

	if(FRACTION_MULTIPLIER-critical_chance < 0)
	{
		critical_chance = FRACTION_MULTIPLIER; //100% crits, when you hit.
	}

    // Get Damage bonus by weapon type
    switch(subcat)
    {
    case IB_SUBCATEGORY_BEAM_WEAPON:		// Beam
        damage_bonus = 1.0f + m_Stats.GetStatType(STAT_BEAM_DAMAGE, STAT_BUFF_MULT);
        break;

    case IB_SUBCATEGORY_PROJECTILE_LAUNCHER:		// Projectile
        damage_bonus = 1.0f + m_Stats.GetStatType(STAT_PROJECTILES_DAMAGE, STAT_BUFF_MULT);
        break;

    case IB_SUBCATEGORY_MISSILE_LAUNCHER:		// Missiles
        damage_bonus = 1.0f + m_Stats.GetStatType(STAT_MISSILE_DAMAGE, STAT_BUFF_MULT);
        break;

    default:
        LogMessage("ERROR: Weapon subcategory [%d] wrong for %s\n", subcat, Name());
        damage_bonus = 0.0f;
        break;
    }

    //find the base weapon damage, first have we got a critical or missed?
    short to_hit = (rand() % FRACTION_MULTIPLIER) + 1;

    float damage_fraction = 1.0f;

    //This system rolls a number, and then determines which category of damage it falls into.
	//It is still possible to miss with a 100% crit rate. It is possible to miss on a crit.
    if (to_hit <= miss_chance)
	{
        //Weapon missed, damage zero
        damage_bonus = 0;
    }
    else if (to_hit >= FRACTION_MULTIPLIER-critical_chance)
    {
        //Critical!! Double damage
        damage_bonus = damage_bonus * 2.0f;
        *critical = true;
    }
	//IMPLIED else: did normal damage.

	//TO-DO: Put in fractional damage FOR BEAMS ONLY, and also DOTs for Chemical/Plasma damage.

    //Now calculate the damage based on weapon base damage and bonus.
    damage_inflicted = damage_fraction * (float)(weapon_damage) * damage_bonus;

    return (damage_inflicted);
}

static char *mount_names[] =
{
    "~WEAP_06",     // 0 nose mount 1 (progen only) 
    "~WEAP_05",     // 1 nose mount 2 (All other races, centre).
    "~02/~WEAP_06", // 2 wing mounts
    "~02/~WEAP_05", // 3
    "~02/~WEAP_04", // 4
    "~02/~WEAP_03", // 5
    "~02/~WEAP_02", // 6
    "~02/~WEAP_01", // 7
    "~01/~WEAP_02", // 8 pod mounts for Progen Warriors (on the ends of the pylons).
    "~01/~WEAP_01"  // 9
};

static char MountNameIndexes[] = //choose names from above table
{
	7, 6, 5, 4, 1,-1,  // TW
    1, 4, 5, 6,-1,-1,  // TT
    6, 7, 4, 5,-1,-1,  // TE
	//1, 6 ,7,-1,-1,-1, // 3 weapon TE
    1, 4, 5, 6, 7,-1,  // JW
    4, 5, 6, 7,-1,-1,  // JT
    1, 4, 5,-1,-1,-1,  // JE (4 outside right, 5 outside left)
    9, 8, 5, 4, 3, 2,  // PW
    2, 3, 4, 5, 0,-1,  // PT
    4, 5, 2, 3,-1,-1   // PE
};

static char PEAltMounts[] =
{
    4, 5, 0, 1,-1,-1,   // PE hull 1
    4, 5, 2, 3,-1,-1,
    4, 5, 0, 1,-1,-1,
	4, 5, 6, 7,-1,-1
};

static char PWAltMounts[] =
{
    9, 8, 7, 6, 5, 4    // PW Wings 1
};

static char PTAltMounts[] =
{
    7, 6, 5, 4, 0,-1,    // PT Wings 1
};

char *Player::GetBoneName(long weapon_id)
{
   char *bonename;
   if (MountNameIndexes[ClassIndex() * 6 + (weapon_id - 1)] == -1)
    {
        LogMessage("Error adding weapon for %s, class not permitted to have weapon #%d\n", Name(), weapon_id);
        return NULL;
    }
    else
    {
		if (ClassIndex() == 6 && m_Database.ship_data.wing == 0) //TODO: put class indexes on an enum for clarity. 6 = PW
        {
			bonename = mount_names[PWAltMounts[weapon_id - 1]]; // wing 1 not showing
        }
		else if (ClassIndex() == 7 && m_Database.ship_data.wing == 0) //TODO: put class indexes on an enum for clarity. 7 = PT
        {
			bonename = mount_names[PTAltMounts[weapon_id - 1]]; // wing 1 not showing
        }
        else if (ClassIndex() == 8) //TODO: put class indexes on an enum for clarity. 8 = PE
        {
			if (m_Database.ship_data.hull == 1 && m_Database.ship_data.wing == 0)
			{
				bonename = mount_names[PEAltMounts[18 + (weapon_id - 1)]]; // fix for hull 2 wing 1 not showing
			}
			else
			{
				bonename = mount_names[PEAltMounts[m_Database.ship_data.hull * 6 + (weapon_id - 1)]];
			}
        }
		else
        {
            bonename = mount_names[MountNameIndexes[ClassIndex() * 6 + (weapon_id - 1)]];
        }
    }
   return bonename;
}

void Player::AddWeapon(long weapon_id)  //weapon 1 to 6
{
    char *bonename = GetBoneName(weapon_id);
	if(bonename)
	{
		ShipIndex()->Inventory.Mounts.SetMount(2+weapon_id, WeaponMount);
		ShipIndex()->Inventory.EquipInv.EquipItem[2+weapon_id].SetItemTemplateID(-1);
		ShipIndex()->Inventory.MountBones.SetMountBoneName(2 + weapon_id, bonename);
		m_WeaponSlots++;
	}
}

void Player::ChangeMountBoneName(long weapon_id, char *mount_name)
{
    ShipIndex()->Inventory.MountBones.SetMountBoneName(2 + weapon_id, mount_name);
}

void Player::NeatenUpWeaponMounts()
{
	u32 weapon_id,i;
	char *bonename;

	for(weapon_id=1;weapon_id<=(u32)WeaponTable[ClassIndex() * 7];weapon_id++) 
	{
		bonename = GetBoneName(weapon_id);
		if(bonename)
		{
			ShipIndex()->Inventory.MountBones.SetMountBoneName(2 + weapon_id, bonename );
		}
	}

  	for (i = 1; i <= PlayerIndex()->RPGInfo.GetHullUpgradeLevel(); i++)
	{
		if (WeaponTable[ClassIndex() * 7 + i] != 0)
		{
			bonename = GetBoneName(weapon_id);
			if(bonename)
			{
				ShipIndex()->Inventory.MountBones.SetMountBoneName(2 + weapon_id, bonename );
			}	
			weapon_id++;
		}
	}
}

void Player::RepairShip()
{
	// undo incapacitation
	if (ShipIndex()->GetIsIncapacitated())
	{
		ShipIndex()->SetIsIncapacitated(false);   
		SetDistressBeacon(false);
		RemobilisePlayer();

		// charge for the tow
		u64 repair_cost = ((u64)ShipIndex()->GetMaxHullPoints() - (u64)ShipIndex()->GetHullPoints()) * 10;
		repair_cost = Negotiate((int)repair_cost,true,false);
		if (repair_cost < PlayerIndex()->GetCredits())
		{
			PlayerIndex()->SetCredits(PlayerIndex()->GetCredits() - repair_cost);
			SaveCreditLevel();
			SendVaMessageC(17,"%d credits paid for tow.",(int)repair_cost);
		}
		else
		{
			SendVaMessageC(17,"You could not afford the tow so Galactic Insurance has paid on your behalf.");
		}
	}
	// perform hull repairs
	if (ShipIndex()->GetMaxHullPoints() != ShipIndex()->GetHullPoints())
	{
		ShipIndex()->SetHullPoints(ShipIndex()->GetMaxHullPoints());
	}
}

void Player::CalculateXPDebt()
{
	extern int LevelXP[];
	u32 nextcombat, nextexplore, nexttrade, debt;

	// no debt til 10
	if (TotalLevel() >= 10)
	{
    	nextcombat  = (u32)((1.0f-PlayerIndex()->RPGInfo.GetCombatXP() ) * LevelXP[CombatLevel()]);
    	nextexplore = (u32)((1.0f-PlayerIndex()->RPGInfo.GetExploreXP()) * LevelXP[ExploreLevel()]);
    	nexttrade   = (u32)((1.0f-PlayerIndex()->RPGInfo.GetTradeXP()  ) * LevelXP[TradeLevel()]) ;
		// debt is the average of the amount of xp needed to level in each bar / 10
		debt = (nextcombat + nextexplore + nexttrade) / 30;
		m_IncapXPDebt = debt;
		PlayerIndex()->SetXPDebt(debt + PlayerIndex()->GetXPDebt());
		SendVaMessageC(23,"You have received an xp debt of %d.",debt);
		SaveXPDebt();
	}
}

// strftime(timestr, sizeof(timestr), "%Y/%m/%d %H:%M:%S", gmttime);
time_t Player::ConvertStringToTimeT(char *datetime)
{
	time_t rettime=0;

	time(&rettime);
	if (datetime && strlen(datetime) == 19)
	{
		struct tm temp;
		if (sscanf(datetime,"%4d/%2d/%2d %2d:%2d:%2d",&temp.tm_year,&temp.tm_mon,&temp.tm_mday,&temp.tm_hour,&temp.tm_min,&temp.tm_sec) == 6)
		{
			temp.tm_year -= 1900;
			temp.tm_mon--;
			rettime = mktime(&temp);
		}
	}
	return rettime;
}

void Player::ForgiveXPDebt(time_t logout)
{
	u32 debt = PlayerIndex()->GetXPDebt();
	u32 start = debt;
	time_t now;
	int hours_offline;

	if (debt)
	{
		time(&now);
		hours_offline = (int)((now-logout)/3600);

		while (debt > 0 && hours_offline > 0)
		{
			debt = debt * 8 / 10;
			hours_offline--;
		}

		if (hours_offline >= 24) 
		{	
			debt = 0;
		}

		if (start != debt)
		{
			SendVaMessageC(23,"%d of your xp debt has been forgiven, %d remaining.", start-debt, debt);
			PlayerIndex()->SetXPDebt(debt);
			SaveXPDebt();
			SendAuxPlayer();
		}
	}
}

void Player::JumpStart(float hull_repair, float level)
{
	u32 debt = PlayerIndex()->GetXPDebt();
	u32 payment = (u32)level * 7 * m_IncapXPDebt / 100;

	// debt 
	if (payment > debt)
	{
		payment = debt;
	}
	SendVaMessageC(23,"%d of your xp debt has been paid.",payment);
	PlayerIndex()->SetXPDebt(debt - payment);
	SaveXPDebt();

	SendClientSound("On_Jumpstarted", 1, 1);

	// repair
	if (hull_repair > ShipIndex()->GetMaxHullPoints())
	{
		hull_repair = ShipIndex()->GetMaxHullPoints();
	}
	ShipIndex()->Shield.SetStartValue(0);			// Set shields at 0%
	ShipIndex()->SetHullPoints(hull_repair);		// Set hull points
	RemobilisePlayer();								// Allow player to move
	ShipIndex()->SetIsIncapacitated(false);			// Tell us that he is now alive
	ShipIndex()->SetIsRescueBeaconActive(false);	// Turn off 
	RechargeReactor();								// Start regening
	RechargeShield();								// Start regening
	ResetCombatTrance();
	// restore 1 point to destroyed critical systems
	if (ShipIndex()->Inventory.EquipInv.EquipItem[0].GetStructure() < 0.01f)
		RestoreEquipmentStructure(0,0.01f);
	if (ShipIndex()->Inventory.EquipInv.EquipItem[1].GetStructure() < 0.01f)
		RestoreEquipmentStructure(1,0.01f);
	if (ShipIndex()->Inventory.EquipInv.EquipItem[2].GetStructure() < 0.01f)
		RestoreEquipmentStructure(2,0.01f);
	// TODO: reset jumpstart verb
	//Player *p = g_PlayerMgr->GetPlayerFromIndex(m_PlayerID);
	//Target->OnTargeted(p);

	SendAuxShip();
}

void Player::SwitchOffAutofire()
{
	for (int i=0; i<6; i++)
	{
		m_Equip[i+3].CancelAutofire();
	}
	SendPacketCache();
}