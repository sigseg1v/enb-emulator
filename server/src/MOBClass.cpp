// MOBClass.cpp
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
** Copyright of our assets/code/software began in 2005-2009 �, Net-7 Entertainment.
**
*/

#include "Net7.h"
#include "ObjectClass.h"
#include "MOBClass.h"
#include "PlayerClass.h"
#include "ObjectManager.h"
#include "Opcodes.h"

// Legacy admin-command MOB sentinel table. The original code declared
// MOB_Info MobData[]; without a definition; under MSVC's relaxed rules this
// linked as a zero-size symbol and the admin commands that walked it
// (ClientToSectorServer.cpp's /spawn-style commands, ObjectManager hooks)
// effectively no-oped. We provide a single zero-terminator entry to make
// the linker happy and preserve that behavior on Linux.
MOB_Info MobData[] = { { 0, 0, "", 0, 0, 0, 0, 0, 0.0f } };
#include "ServerManager.h"
#include "MOBDatabase.h"
#include "PlayerManager.h"
#include "PacketMethods.h"
#include "FactionDataSQL.h"

#define SHIELD 1
#define HULL 2

//MOB DPS average for each level step, 1.1f = level 0, 14933 = level 200 (CL 66).
float MobDPS[] =
{
	1.1f,
	3.2f,	
	7.2f,	
	14.7f,	
	24.0f,	
	41.0f,	
	74.0f,	
	131.0f,	
	198.0f,	
	310.0f,
	448.0f,
	648.0f,
	915.0f,
	1436.0f,	
	2080.0f,	
	2912.0f,	
	3965.0f,	
	5017.0f,	
	6347.0f,	
	8028.0f,	
	14933.0f
};

//Damage Absorbtion Capacity - average for MOBs
float MobDAC[] =
{
	29,	
	57,	
	160,	
	377,	
	864,	
	1365,
	2845,
	5203,
	10146,
	14171,
	27030,
	36864,
	62797,
	87172,
	164296,
	220191,
	350057,
	466263,
	619402,
	820852,
	1085421
};

MOB::MOB(long object_id) : CMob (object_id)
{
	m_Type = OT_MOB;
	m_CreateInfo.Type = 0;
	m_MOBVisibleList.clear();
	m_BehaviourList.clear();
	m_Respawn_tick = 0;
	m_Respawn_time = 240000;  // 4 minutes by default
	m_MovementID = 0;
	m_Velocity = 0.0f;
	m_GravityField = 1.0f;
	m_GravityHandle = 1.0f;
	m_GravityTimeExpire = 0;
	m_GravityVelocity = 0;
	m_GravityAcceleration = 0;
	m_LastUpdate = GetNet7TickCount();
	m_YInput = 0;
	m_ZInput = 0;
	m_LastAttackTime = 0;
	m_Position_info.Orientation[0] = 0.0f;
	m_Position_info.Orientation[1] = 0.0f;
	m_Position_info.Orientation[2] = 0.0f;
	m_Position_info.Orientation[3] = 1.0f;
	m_ObjectRadius = 250.0f; //default ship size
	m_Signature = 5000.0f;
	m_ScanSkill = 0;
	m_SpawnGroup = 0;
	m_GoingHome = false;
	m_LastTickHullLevel = 0;
	m_Attacking = false;
	m_MOB_Data = (0);
	m_SkillTime = 0;

	m_ShieldModifier = 1.0f;
	m_DamageModifier = 1.0f;
	m_RangeModifier = 1.0f;

	m_DamageType = 1;

	// Set up gameID
	m_ShipIndex.SetGameID(object_id);

	m_ArrivalTime = 0;
	m_Behaviour = DRIFT;
	m_Destination = (0);
	m_ShieldFXSent = 0;
	m_WeaponFX = 0;
	m_WeaponDamageDelay = 0;
	m_WeaponReloadTime = 0;

	memset (&m_HomePosition, 0, sizeof(m_HomePosition));
	memset (&m_DestinationPos, 0, sizeof(m_DestinationPos));

	memset (&m_PlayerVisibleList, 0, sizeof(m_PlayerVisibleList));
	memset (&m_HateList, 0, sizeof(m_HateList));
	memset (m_DamageList, 0, sizeof(m_DamageList));
	memset (&m_DamageNode, 0, sizeof(m_DamageNode));

	m_DamageTime = 0;
	m_Menace = false;
	m_MenaceDenyWeapon = false;
	m_MenaceDenySkill = false;

	m_Buffs.Init(this);
	m_Stats.Init(this);
	ResetAbilities();
}

MOB::~MOB()
{
	// TODO: destroy everything
}

void MOB::SetMOBType(short type)
{
	MOBData *mob_data = g_ServerMgr->MOBList()->GetMOBData(type);
	m_MOB_Data = mob_data;

	if (type <= 0)
	{
		//crawl MOB DB until we find a matching entry
		mob_data = g_ServerMgr->MOBList()->GetMOBDataFromBasset(this->BaseAsset());
		if (mob_data) type = mob_data->m_Type;
	}

	if (!mob_data)
	{
		return;
	}

	m_MOB_Data = mob_data;
	// Set mob info
	SetName(mob_data->m_Name);
	SetBasset(mob_data->m_Basset);
	SetLevel(type);
	SetHSV(mob_data->m_HSV[0], mob_data->m_HSV[1], mob_data->m_HSV[2]);
	SetScale(mob_data->m_Scale);
	//LogMessage("Adding MOB: %s\n", Name());
	m_MOB_Type = type;
	// Set aux data
	m_ShipIndex.SetName(m_Name);
	m_ShipIndex.SetCombatLevel(mob_data->m_Level);
	SetFactionID(mob_data->m_FactionID);

	if (IsOrganic())
	{
		m_ShipIndex.SetIsOrganic(true);
	}
}

void MOB::SetLevel(short type)
{
	if (!m_MOB_Data)
	{
		return;
	}

	short overall_level = m_MOB_Data->m_Level * 3;
	short index = (overall_level / 10);
	float fraction = (float)(overall_level) / 10.0f - (float) index;

	//TODO: calculate this from MOB items when in place
	float lower_bound = MobDPS[index];
	float upper_bound = MobDPS[index+1];
	m_WeaponDPS = lower_bound + (upper_bound - lower_bound)*fraction;

	//work out MOB Damage absorbtion Capacity from table
	lower_bound = MobDAC[index];
	upper_bound = MobDAC[index+1];

	float StartShieldLevel  = lower_bound + (upper_bound - lower_bound)*fraction;
	if (m_MOB_Data->m_Type == MANNED || m_MOB_Data->m_Type == ROBOTIC)
	{
		//split the DAC between hull and shield
		SetMaxHullPoints(StartShieldLevel * 0.3f);
		StartShieldLevel -= GetMaxHullPoints() * 0.9f;
	}
	else
	{
		SetMaxHullPoints(0);
	}

	SetHullPoints(GetMaxHullPoints());
	m_Level = m_MOB_Data->m_Level;

	// Add the modifier
	m_ShieldModifier = m_MOB_Data->m_ShieldModifier;
	m_RangeModifier = m_MOB_Data->m_RangeModifier;
	m_DamageModifier = m_MOB_Data->m_DamageModifier;


	StartShieldLevel = StartShieldLevel * m_ShieldModifier;

	// Set up hull points
	m_ShipIndex.SetMaxHullPoints(GetHullPoints());
	m_ShipIndex.SetHullPoints(GetHullPoints());
	//Setup Shields
	m_ShipIndex.Shield.SetStartValue(StartShieldLevel);
	m_ShipIndex.Shield.SetChangePerTick(BaseShieldRecharge());
	m_ShipIndex.Shield.SetEndTime(GetNet7TickCount());
	m_ShipIndex.SetMaxShield(StartShieldLevel);

	//TODO: Add this entry into MOB database, and get it from there.
	m_ScanRange = 1000.0f + (50.0f*m_MOB_Data->m_Level); 


	m_WeaponFX = 0;
	m_WeaponDamageDelay = 0;
	m_WeaponReloadTime = 5; // default to 5
	m_WeaponAmmoPerShot = 1;

	bool found_beam_weapon = false;
	bool found_launcher_weapon = false;
	bool found_ammo = false;
	bool found_engine = false;

	ItemBase * launcher;
	ItemBase * ammo;
	ItemBase * equip;
	ItemBase * engine;

	ItemInstance launcherInstance;
	ItemInstance beamInstance;
	AmmoInstance ammoInstance;
	ItemInstance engineInstance;

	for (u32 i = 0; i < m_MOB_Data->m_Loot.size(); i++)
	{
		equip = g_ItemBaseMgr->GetItem(m_MOB_Data->m_Loot[i]->item_base_id);

		if (equip)
		{
			_Item mob_item;
			memset(&mob_item,0,sizeof(_Item));
			mob_item.ItemTemplateID = equip->ItemTemplateID();
			mob_item.Quality = 1.0f; //we could adjust quality of MOB items here
			mob_item.Structure = 1.0f;
			mob_item.StackCount = 1;

			QualityCalculator(&mob_item);

			if (equip->EquipableCount() <= 0)
				continue;

			switch (equip->SubCategory())
			{
			case 101:
			case 102: //todo - use ammo FX if available
				launcher = equip;
				launcherInstance = launcher->GetItemInstance(mob_item.InstanceInfo);
				found_launcher_weapon = true;
				break;
			case 103:
				ammo = equip;
				ammoInstance = ammo->GetAmmoInstance(mob_item.InstanceInfo);
				found_ammo = true;
				break;
			case 100:
				beamInstance = equip->GetItemInstance(mob_item.InstanceInfo);
				found_beam_weapon = true;
				break;
			case 121:
				engine = equip;
				engineInstance = engine->GetItemInstance(mob_item.InstanceInfo);
				found_engine = true;
				break;
			};

			if (found_engine && ((found_launcher_weapon && found_ammo) || found_beam_weapon)) break;
		}
	}

	float average_dps = m_WeaponDPS;

	if (found_ammo)
	{
		m_WeaponDamageDelay = 1;
		m_WeaponFX = ammo->UseEffect();
		m_WeaponFiringRange = (float)ammoInstance.WeaponRange;
		m_DamageType = ammoInstance.WeaponDamageType;

		if (found_launcher_weapon)
		{
			m_WeaponReloadTime = launcherInstance.WeaponReload;
			m_WeaponAmmoPerShot = launcher->Fields(22)->iData;

			m_WeaponDPS = (float)(ammoInstance.WeaponDamage * m_WeaponAmmoPerShot) / m_WeaponReloadTime;

			//do a sanity check here - if the chosen weapon is a lot lower than the average DPS, do an auto-correct
			if (m_WeaponDPS < average_dps * 0.75f || m_WeaponDPS > average_dps * 1.25f)
			{
				if (m_MOB_Data->m_CreateWarning == false)
				{
					LogMessage("Adjusted MOB weapon DPS: '%s' [%d], using: '%s' [%d] firing '%s' \n",
						Name(), m_Level, launcher->Name(), launcher->TechLevel(), ammo->Name());
					m_MOB_Data->m_CreateWarning = true;
				}
				m_WeaponAmmoPerShot = (int)ceil((average_dps * (1.0f + (rand()%50/100.0f)) * m_WeaponReloadTime) / ammoInstance.WeaponDamage);
				if (m_WeaponAmmoPerShot < 1) m_WeaponAmmoPerShot = 1;
				m_WeaponDPS = average_dps;
			}
		}
		else
		{
			if (m_WeaponAmmoPerShot < 1)
				m_WeaponAmmoPerShot = 1;
			// Fake weapon firing rate & damage (based on db averages)
			if (ammoInstance.MissleManeuverability) // this a missle launcher
			{
				m_WeaponReloadTime = 10.0f; 
				m_WeaponAmmoPerShot = (int)ceil((average_dps * (1.0f + (rand()%50/100.0f)) * m_WeaponReloadTime) / ammoInstance.WeaponDamage);
				if (m_WeaponAmmoPerShot < 1) m_WeaponAmmoPerShot = 1;
				m_WeaponDPS = (float)(ammoInstance.WeaponDamage * m_WeaponAmmoPerShot) / m_WeaponReloadTime;
			}
			else // projectile launcher
			{
				m_WeaponReloadTime = 1.0f;
				m_WeaponAmmoPerShot = (int)ceil((average_dps * (1.0f + (rand()%50/100.0f)) * m_WeaponReloadTime) / ammoInstance.WeaponDamage);
				if (m_WeaponAmmoPerShot < 1) m_WeaponAmmoPerShot = 1;
				m_WeaponDPS = (float)(ammoInstance.WeaponDamage * m_WeaponAmmoPerShot) / m_WeaponReloadTime;
			}

			if (m_MOB_Data->m_CreateWarning == false)
			{
				LogMessage("No launcher for MOB '%s' [%d], using '%s' [%d]\n",
					Name(), m_Level, ammo->Name(), ammo->TechLevel());
				m_MOB_Data->m_CreateWarning = true;
			}
			//do a sanity check here - if the chosen weapon is a lot lower than the average DPS, do an auto-correct
			if (m_WeaponDPS < average_dps * 0.75f || m_WeaponDPS > average_dps * 1.25f)
			{
				if (m_MOB_Data->m_CreateWarning == false)
				{
					LogMessage("Adjusted MOB weapon DPS: '%s' [%d], using: '%s' [%d]\n",
						Name(), m_Level, ammo->Name(), ammo->TechLevel());
					m_MOB_Data->m_CreateWarning = true;
				}
				m_WeaponDPS = average_dps;
			}
		}
		//LogMessage("Weapon DPS = %.2f. Reload time = %.2f\n", m_WeaponDPS, m_WeaponReloadTime);
	}
	else if (found_beam_weapon)
	{
		m_WeaponFX = equip->UseEffect();
		m_WeaponDamageDelay = 0;
		m_DamageType = beamInstance.WeaponDamageType;
		m_WeaponFiringRange = (float)beamInstance.WeaponRange;
		m_WeaponReloadTime = beamInstance.WeaponReload;
		m_WeaponDPS = (float)beamInstance.WeaponDamage / m_WeaponReloadTime;
		//do a sanity check here - if the chosen weapon is a lot lower than the average DPS, do an auto-correct
		if ((m_WeaponDPS < average_dps * 0.75f || m_WeaponDPS > average_dps * 1.5f) && equip->ItemTemplateID() != 5978) // adjust except for turrets
		{
			if (m_MOB_Data->m_CreateWarning == false)
			{
				LogMessage("Adjusted MOB weapon DPS: '%s' [%d], using: '%s' [%d]\n",
					Name(), m_Level, equip->Name(), equip->TechLevel());
				m_MOB_Data->m_CreateWarning = true;
			}
			m_WeaponDPS = average_dps;
		}
	}
	else
	{

		m_WeaponFiringRange = Level() * 50.0f + 1000.0f; // Variable weapon range based on mob level
		m_WeaponReloadTime = 4;
		switch (m_MOB_Data->m_Type)
		{
		case MANNED:
			m_WeaponFX = 33;
			m_WeaponDamageDelay = 0;
			break;
		case ROBOTIC:
			m_WeaponFX = 34;
			m_WeaponDamageDelay = 0;
			break;
		case ENERGY:
			m_WeaponFX = 35;
			m_WeaponDamageDelay = 0;
			break;
		case ROCK_BASED:
			m_WeaponFX = 38;
			m_WeaponDamageDelay = 1;
			break;
		case ORGANIC_GREEN:
			m_WeaponFX = 41;
			m_WeaponDamageDelay = 1;
			break;
		default:
			m_WeaponFX = 43;
			m_WeaponDamageDelay = 1;
			break;
		};
	}

	if (found_engine)
	{
		//calculate pursuit velocity
		//TODO: this should be done via the stats system
		m_PursuitVelocity = m_DefaultVelocity + (float)engineInstance.EngineSpeed;
	}
}

///////////////////////////////////////////////////
// Range List Handling
//
// These methods handle adding to and removing from
// the player range lists.

// m_RangeList functions in exactly the same way as the Player list, it's a list of players who can see this MOB.
//             Used only for updating the MOB status to players
// 
// m_MOBVisibleList on the other hand is a list of players and MOBs that this MOB can see
//             Used for deciding which target to attack, and target interactions.

void MOB::UpdateObjectVisibilityList(u32 check_tick)
{
	ObjectManager *om = GetObjectManager();
	if (!om) return;

	Player *p = (0);
	u32 * sector_list = 0;
	sector_list = GetSectorList();

	ObjectList * mob_list = om->GetMOBList();
	bool on_players_range_list;
	bool on_mobs_range_list;
	float range;
	float player_visible_range;
	float mob_visibile_range;
	float range_from_home;

	if (RespawnTick() != 0)
	{
		return;
	}

	//TODO: 
	//      - Add grid system to further reduce checking

	while (g_PlayerMgr->GetNextPlayerOnList(p, sector_list))
	{
		if (p && p->Active() && !p->IsGating())
		{
			//Find out if this player is already on the list
			on_players_range_list = g_PlayerMgr->GetIndex(p, m_RangeList);

			if (on_players_range_list)
			{ 
				//Since we are leaving the area, only check once per second. 
				//This will cut down on method calls (network traffic remains the same since we have to send the remove packet anyway).
				if (check_tick % 20 == 0)
				{			
					mob_visibile_range = (float)(p->ShipIndex()->CurrentStats.GetScanRange() + m_Signature);
					range = RangeFrom(p);

					// Check range here (another method call savings)
					//see if this MOB has gone out of player 'p' scan range
					if (range > mob_visibile_range + 1000.0f)
					{
						g_PlayerMgr->UnSetIndex(p, m_RangeList);
						UnSetClickedBy(p);
						p->RemoveObject(GameID());
					} 
				}
			}
			else
			{
				mob_visibile_range = (float)(p->ShipIndex()->CurrentStats.GetScanRange() + m_Signature);
				range = RangeFrom(p);

				//see if this MOB has come into player 'p' scan range
				//eg decloak (dont remove from click list, stops updates attacking from cloak)
				if (range <= mob_visibile_range && p->InSpace())
				{
					g_PlayerMgr->SetIndex(p, m_RangeList); 
					SendMOBData(p); //send this MOB to 'p'
				}
			}

			if (check_tick % 50 == 0 && !m_PlayerHijack)
			{
				if (!m_GoingHome)
				{
					range_from_home = RangeFrom(m_HomePosition, true);

					if (range_from_home > 30000.0f)
					{
						on_mobs_range_list = g_PlayerMgr->GetIndex(p, m_PlayerVisibleList);
						if (on_mobs_range_list)
						{
							p->RemoveObject(GameID());
							LostTarget(p);

							char buf[192];
							sprintf_s(buf,sizeof(buf),"L%d %s [%x, scan: %.0f] vs. %s going home. %.0f away.\n", 
								Level(), Name(), GameID(), m_ScanRange, p->Name(), range_from_home);
							LogMessage(buf);
							g_ServerMgr->m_PlayerMgr.ErrorBroadcast(buf);
						}

						GoingHome();
					}
				}
				else
				{
					GoingHome();
				}
			}

			//Mobs check to see if players come in and out of their range much slower 
			//This saves more network and cycles
			if (check_tick % 20 == 0 && !m_GoingHome && !m_PlayerHijack)
			{
				long player_visibility = p->ShipIndex()->CurrentStats.GetVisibility();
				if (player_visibility < 0) player_visibility = 0;
				player_visible_range = (float)(m_ScanRange + player_visibility);
				range = RangeFrom(p);
				on_mobs_range_list = g_PlayerMgr->GetIndex(p, m_PlayerVisibleList);
				
				if (on_mobs_range_list)
				{
					// only kite them so far, then return home. No more rogue mobs

					// If the mob is hostile to the player, 'lock on' to them
					// This prevents players from using long range weapons to
					// fire at mobs which can't see them.

					// this code didn't work correctly - should be ok now
					if (p->WarpDrive())
					{
						LostTarget(p);
					} 
					else if (CheckHateList(p->GameID()))
					{
						if (range > player_visible_range + 20000.0f) // > max missle range
						{
							LostTarget(p);
						}
					}
					else if (range > (player_visible_range + 2000.0f))
					{
						LostTarget(p);
					}
					else if (range < 1000.0f) //you might be too close to a mob
					{
						//see if MOB fires a warning shot off
						CheckWarningShots(p);
					}
					else 
					{
						CheckAggro(p);
					}
				}
				else
				{
					//see if 'p' has come into MOB scan range
					if ((range < player_visible_range && p->InSpace()) ||
						(CheckHateList(p->GameID()) && range < (player_visible_range + 10000.0f) && p->InSpace())) // 6500 is max missle range
					{
						g_PlayerMgr->SetIndex(p, m_PlayerVisibleList);
					}
				}
			}
		}
	}

	//now scan other MOBs in sector, to see if any have come into our range
	//Only for turrets for now
	ObjectList::iterator itrOList;
	Object *o;
	if (m_DefaultBehaviour == TURRET && check_tick % 20 == 0)
	{
		for (itrOList = mob_list->begin(); itrOList < mob_list->end(); ++itrOList) 
		{
			o = (*itrOList);
			if ( o != this && o->Active() && o->ObjectType() == OT_MOB)
			{
				range    = o->RangeFrom(this);
				on_mobs_range_list = ObjectOnRangeList(&m_MOBVisibleList, o);

				if (on_mobs_range_list)
				{
					//see if MOB 'o' has gone out of this MOB weapon range
					if (range > (float)(7500.0f + 200.0f) )
					{                   
						RemoveObjectFromRangeList(&m_MOBVisibleList, o);
						if (o->GameID() == HostilityTarget()) SetHostilityTarget(0);
					}
				}
				else
				{
					//see if MOB 'o' has come into this MOB weapon range
					if (range < (float)(7500.0f) )
					{
						AddObjectToRangeList(&m_MOBVisibleList, o);
						//now lock onto this target if we don't already have one
						LockMOBTarget((MOB *)o);
					}
				}
			}
		}
	}
}


void MOB::GoingHome()
{
	if (!m_GoingHome)
	{
		m_GoingHome = true;
//		m_ShieldLevel = m_StartShieldLevel;
//		m_HullLevel   = m_StartHullLevel;
		SetVelocity(1000.0f);
		TravelToPosition(m_HomePosition);
		// clear hate list
		while (int id = GetMaxHateID())
		{
			RemoveHate(id);
		}
	}

	if (RangeFrom(m_HomePosition) < 5000.0f)
	{
		m_GoingHome = false;
		SetVelocity(m_DefaultVelocity);
		SendLocationAndSpeed(true);
//		m_ShieldLevel = m_StartShieldLevel;
//		m_HullLevel   = m_StartHullLevel;
	}
}

/*
aggro:
0 - never
1 - only if shot
2 - if inside <aggro_rng>
3 - if visible
4 - always
*/

void MOB::CheckWarningShots(Player *p)
{
	//first make sure this player isn't already on the shitlist
	if (CheckHateList(p->GameID()) || p->Hijackee() || p->HasCombatImmunity())
	{
		return;
	}

	if (NoFaction() || p->GetFactionStanding(this) < 0.0f) //MOB only spontaneously attacks if faction standing 0 or less
	{
		if (m_MOB_Data->m_Agressiveness > 4) m_MOB_Data->m_Agressiveness = 4;

		if (m_MOB_Data->m_Agressiveness > 0)
		{
			long hate = 0;
			long roll = rand()%100;
			switch (m_MOB_Data->m_Agressiveness)
			{
			case 0:
				//if (roll > 98) //broadcast a collision warning maybe, use an object to object effect?
				break;

			case 1:
				//this MOB not likely to fire warning shot, if it does then not too many, just a 'back off'.
				if (roll > 97) hate = 1;
				break;

			case 2:
				if (roll > 95) hate = 5;
				break;

			case 3:
				if (roll > 93) hate = 20;
				break;

			case 4:
				hate = 100;
				break;

			default:
				break;
			}

			if (hate != 0)
			{
				AddHate(p->GameID(), hate);
			}
		}
	}
}

void MOB::CheckAggro(Player *p)
{
	//first make sure this mob gives aggro and player isn't already on the shitlist
	if (m_MOB_Data->m_Agressiveness < 3 || CheckHateList(p->GameID()) || p->HasCombatImmunity() || p->Hijackee())
	{
		return;
	}

	if (NoFaction() || p->GetFactionStanding(this) < 0.0f) //MOB only spontaneously attacks if faction standing 0 or less
	{
		if (m_MOB_Data->m_Agressiveness > 4) m_MOB_Data->m_Agressiveness = 4;

		if (m_MOB_Data->m_Agressiveness > 0)
		{
			long hate = 0;
			long roll = rand()%100;
			switch (m_MOB_Data->m_Agressiveness)
			{
			case 3:
				if (roll > 95) hate = 20;
				break;

			case 4:
				hate = 100;
				break;

			default:
				break;
			}

			if (hate != 0)
			{
				AddHate(p->GameID(), hate);
			}
		}
	}
}

void MOB::UpdateMOB(u32 current_tick, bool handle_attacks, u32 check_tick)
{
	if (m_PlayerHijack) //Only process range list handling for hijacked mobs
	{
		UpdateObjectVisibilityList(check_tick);
		UpdateObject(current_tick);
		m_Buffs.CheckBuffExpire(current_tick);
		return;
	}

	if (RespawnTick() > 0 && RespawnTick() < current_tick)
	{
		LogMessage("Re-Spawning %s\n", Name());
		SetRespawnTick(0); //re-spawn mob
		SetActive(true);
		SetLastUpdateNow();
		OnRespawn();
	}
	else if (RespawnTick() == 0)
	{
		CalcNewPosition(current_tick);
		UpdateObjectVisibilityList(check_tick);
		UpdateObject(current_tick);
		IncrementMovementID(5);
		if (MovementID() % UpdateRate() == 0)
		{
			SendLocationAndSpeed(false);
		}
		if (MovementID() % 100 == 0)
		{
			HandleMovementChanges();
		}
		
		m_Buffs.CheckBuffExpire(current_tick);
		
		if (handle_attacks)
		{
			if (HostilityTarget() == 0)
			{
				ChooseTarget();
				m_SkillTime = current_tick + 5000 + rand()%10000;
			}
			else
			{
				HandleAttack();
			}
		}
	}
}

bool MOB::ObjectOnRangeList(ObjectList *object_list, Object *obj)
{
	bool on_range_list = false;
	ObjectList::iterator itrOList;

	//m_Mutex.Lock();

	for (itrOList = object_list->begin(); itrOList < object_list->end(); ++itrOList) 
	{
		if (obj == (*itrOList))
		{ 
			on_range_list = true;
			break;
		}
	}

	//m_Mutex.Unlock();

	return on_range_list;
}

void MOB::RemoveObjectFromRangeList(ObjectList *object_list, Object *obj)
{
	ObjectList::iterator itrOList;

	//m_Mutex.Lock();

	for (itrOList = object_list->begin(); itrOList < object_list->end(); ++itrOList) 
	{
		if (obj == (*itrOList))
		{ 
			object_list->erase(itrOList);
			//LogMessage("%s no longer sees %s\n", Name(), obj->Name());
			break;
		}
	}

	//m_Mutex.Unlock();
}

void MOB::AddObjectToRangeList(ObjectList *object_list, Object *obj)
{
	//m_Mutex.Lock();
	object_list->push_back(obj);
	//LogMessage("%s now sees %s\n", Name(), obj->Name());
	//m_Mutex.Unlock();
}

// called by the summon enemy group skill
// TODO: proper MOB grouping behaviour, just grab nearby mobs for now
// TODO: use this MOB's spawn.
short MOB::GetGroupPointers(CMob **list,float within_range)
{
	ObjectManager *om = GetObjectManager();
	if (!om) return 0;
	ObjectList *mob_list = om->GetMOBList();
	ObjectList::iterator itrOList;
	Object *o;
	short count=0;

	list[count++] = this;
	for (itrOList = mob_list->begin(); itrOList < mob_list->end(); ++itrOList) 
	{
		o = (*itrOList);
		// check range to me
		if (o != this && o->ObjectType() == OT_MOB && o->Active() && o->RangeFrom(this) < within_range)
		{
			list[count++] = dynamic_cast<CMob *>(o);
			if (count >= 6)
				break;
		}
	}
	return count;
}

void MOB::RemovePlayerFromRangeLists(Player *p)
{
	//RemovePlayerFromRangeList(p);
	//RemoveObjectFromRangeList(&m_MOBVisibleList, p);
	if (p)
	{
		long game_id = p->GameID();

		if (game_id > 0)
		{
			g_PlayerMgr->UnSetIndex(p, m_RangeList);
			g_PlayerMgr->UnSetIndex(p, m_PlayerVisibleList);

			RemoveHate(game_id);
		}
	}
}

void MOB::AddHate(long GameID, long Hate)
{
	// Add to hate list if required

	long lowest_hate = 0;
	long lowest_hate_val = m_HateList[0].hate;
	bool inserted = false;

	//find spot on hate list
	for (int i = 0; i < HATE_SIZE; i++)
	{
		if (m_HateList[i].hate < lowest_hate_val) //track lowest hate value
		{
			lowest_hate = i;
			lowest_hate_val = m_HateList[i].hate;
		}

		if (m_HateList[i].GameID == GameID || m_HateList[i].hate == 0)
		{
			m_HateList[i].hate += Hate;
			m_HateList[i].GameID = GameID;
			inserted = true;
			break;
		}
	}

	if (!inserted && Hate > lowest_hate_val) //no empty slots, but player qualifies for shit list
	{
		m_HateList[lowest_hate].GameID = GameID;
		m_HateList[lowest_hate].hate = Hate;
	}

	//need to add this player/mob to the MOB's visible list
}

void MOB::SubtractHate(long GameID, long Hate)
{
	for (int i = 0; i < HATE_SIZE; i++)
	{
		if (m_HateList[i].GameID == GameID)
		{
			m_HateList[i].hate -= Hate;
			if (m_HateList[i].hate < 0) m_HateList[i].hate = 0;
			break;
		}
	}
}

//Call with GameID = 0 to flush the mob's hate list.
void MOB::RemoveHate(long GameID)
{
	for (int i = 0; i < HATE_SIZE; i++)
	{
		if (m_HateList[i].GameID == GameID || GameID==0)
		{
			m_HateList[i].GameID = 0;
			m_HateList[i].hate = 0;
			break;
		}
	}
}

long MOB::GetMaxHateID()
{
	long max_hate = 0;
	long game_id = 0;

	for (int i = 0; i < HATE_SIZE; i++)
	{
		if (m_HateList[i].hate > max_hate)
		{
			max_hate = m_HateList[i].hate;
			game_id = m_HateList[i].GameID;
		}
	}

	return game_id;
}

bool MOB::CheckHateList(long GameID)
{
	for (int i = 0; i < HATE_SIZE; i++)
	{
		if (m_HateList[i].GameID == GameID)
		{
			return true;
		}
	}
	return false;
}

// NOTE: if DAMAGE_SIZE+1 people attack the same target only the first DAMAGE_SIZE are counted
void MOB::AddDamage(long GameID, long Damage)
{
	int x;

	Group *g = g_PlayerMgr->GetGroupFromID(GameID);
	if (g)
	{
		// group already done some damage?
		for (x=0;x < DAMAGE_SIZE;x++)
			if (m_DamageList[x].GroupID == g->GroupID)
			{
				m_DamageList[x].damage += Damage;
				return;
			}
	}
	// solo player already done some damage?
	for (x=0;x < DAMAGE_SIZE;x++)
	{
		if (m_DamageList[x].GameID == GameID)
		{
			m_DamageList[x].damage += Damage;
			return;
		}
	}
	if (g)
	{
		// new group attackers
		for (x=0;x < DAMAGE_SIZE;x++)
		{
			if (m_DamageList[x].damage <= 0)
			{
				m_DamageList[x].GroupID = g->GroupID;
				m_DamageList[x].GameID = GameID; // 1st in group to hit it
				m_DamageList[x].damage = Damage;
				return;
			}
		}
		// no free spaces, overwrite last solo player
		for (x=DAMAGE_SIZE-1;x >= 0;x--)
		{
			if (m_DamageList[x].GroupID == -1)
			{
				m_DamageList[x].GroupID = g->GroupID;
				m_DamageList[x].GameID = GameID; // 1st in group to hit it
				m_DamageList[x].damage = Damage;
				return;
			}
		}
	}
	// new solo attacker
	for (x=0;x < DAMAGE_SIZE;x++)
	{
		if (m_DamageList[x].damage <= 0 || x == DAMAGE_SIZE-1)
		{
			m_DamageList[x].GroupID = -1;
			m_DamageList[x].GameID = GameID;
			m_DamageList[x].damage = Damage;
			return;
		}
	}
}

long MOB::GetMaxDamageID()
{
	long most_damage = 0;
	long loot_player = -1;
	for (int x=0;x < DAMAGE_SIZE;x++)
	{
		if (m_DamageList[x].damage > most_damage)
		{
			most_damage = m_DamageList[x].damage;
			loot_player = m_DamageList[x].GameID;
		}
	}
	return loot_player;
}

float MOB::GetStealthDetectionLevel()
{
	return (m_ScanSkill * 5.0f) + m_MOB_Data->m_Level * 3;
}

//TODO: expand so MOBs can target other MOBs
void MOB::ChooseTarget()
{
	Player *p = (0);
	short player_count = 0;

	if (m_DefaultBehaviour == TURRET)
	{
		if (m_MOB_Type)
		{
			LockTurretTarget();
		}
		return;
	}

	LockTarget();

	/*while (g_PlayerMgr->GetNextPlayerOnList(p, m_PlayerVisibleList))
	{
		//do not allow players who are cloaked at a lvl insufficient for the mob's targeting to be added to the target list
		if(!IsDetectable(p))
		{
			continue;
		}

		if (!p->ShipIndex()->GetIsIncapacitated() && !p->WarpDrive() && !p->Hijackee())
		{
			player_count++;
			break;
		}
	}

	if (player_count > 0)
	{
		LockTarget();
	}*/
}

void MOB::LockMOBTarget(MOB *target)
{
	//is this another turret? Don't attack other turrets
	if (!target || !m_MOB_Type || (target->ObjectType() == OT_MOB && target->m_DefaultBehaviour == TURRET))
	{
		return;
	}

	//are we already attacking? ... if not ...
	if (HostilityTarget() == 0 && target->ObjectType() == OT_MOB)
	{
		//see if this target is to be attacked based on faction
		float faction_standing = GetFactionStanding(target);

		if ((NoFaction() && m_DefaultBehaviour == TURRET) || faction_standing <= 0.0f) // only turrets attack zero faction mobs, not other mobs.
		{
			if (target->GetMaxHateID())
			{
				//attack this target if its fighting a player
				SetHostilityTarget(target->GameID());
			}
		}
	}
}

void MOB::LockTarget()
{
	// Find target based on hate
	int AttackGameID = 0;
	int MaxHate = -1;

	//see if we hate someone the most
	AttackGameID = GetMaxHateID();

	if (AttackGameID == 0)
	{
		return;
	}

	Player *p = g_PlayerMgr->GetPlayer(AttackGameID);

	if (!p) 
	{
		Object *obj = GetObjectManager()->GetObjectFromID(AttackGameID);
		if (obj && obj->ObjectType() == OT_MOB)
		{
			//check if MOB is dead
			//if (obj->RespawnTick() != 0)
			//we locked onto a MOB
			SetHostilityTarget(AttackGameID);                    
			SetBehaviour(PURSUE);                        
			SetUpdateRate(10);
			m_ArrivalTime = 0;
		}
		return;
	}

	if (p && (p->ShipIndex()->GetIsIncapacitated() || p->HasCombatImmunity() || p->HasScanInvisibility()))
	{
		RemoveHate(p->GameID());
		AttackGameID = 0;
		LostTarget(p);
	}

	if (p && !g_PlayerMgr->GetIndex(p, m_PlayerVisibleList))
	{
		RemoveHate(p->GameID());
		AttackGameID = 0;
		LostTarget(p);
	}

	//do not lock players that have cloaked
	if (p && !IsDetectable(p))
	{
		RemoveHate(p->GameID());
		AttackGameID = 0;
		LostTarget(p);
	}

	if (HostilityTarget() != 0 && HostilityTarget() != AttackGameID)
	{
		//ok we switched targets, so we stopped locking onto the last target.
		Player *p2 = g_PlayerMgr->GetPlayer(HostilityTarget());
		if (p2)
		{
			SendRelationship(p2);
			LostTarget(p2);
		}
	}

	if (AttackGameID != 0 && p)
	{
		if (HostilityTarget() != AttackGameID)
		{
			SendAggroRelationship(p);
		}
		SetHostilityTarget(AttackGameID);                    
		SetBehaviour(PURSUE);                        
		SetUpdateRate(10);
		m_ArrivalTime = 0;
	}
}

void MOB::HandleAttack()
{
	unsigned long current_tick = GetNet7TickCount();
	Player *p = g_PlayerMgr->GetPlayer(m_HostilityTargetID);
	Object *o = 0;
	ObjectManager *om = GetObjectManager();

	if (!m_MOB_Data || !om)
	{
		return;
	}

	if (!p)
	{
		o = om->GetObjectFromID(m_HostilityTargetID);
		if (o && o->ObjectType() != OT_MOB) 
		{
			LostTarget(p);
			return;
		}
		if (o && !o->Active())
		{
			RemoveObjectFromRangeList(&m_MOBVisibleList, o);
			LostTarget(p);
			RemoveHate(m_HostilityTargetID);
			return;
		}
	}

	if(p && !IsDetectable(p))
	{
		LostTarget(p);
		return;
	}

	float turbo = m_Stats.ModifyValueWithStat(STAT_WEAPON_TURBO,1.0f);
	unsigned long time_to_attack = m_LastAttackTime + (unsigned long)(m_WeaponReloadTime * 1000.0f * turbo);

	if (p && !p->ShipIndex()->GetIsIncapacitated() && p->InSpace() && !p->Hijackee())
	{
		if ( current_tick > time_to_attack 
			&& RangeFrom(p) < (m_WeaponFiringRange * m_RangeModifier))
		{
			float range = RangeFrom(p);

			if (range > (10000.0f * m_RangeModifier)) // 10k range is higher then any weapon range.
			{
				LostTarget(p);
				return;
			}

			if (m_Attacking == false)
			{
				m_Attacking = true;
				p->AttackMusicUpdate(true, GameID());
			}

			if ( !m_MenaceDenyWeapon )
				FireWeapon((Object *)p);

			// Look for a new target with the most hate
			LockTarget();
		}
		// does this mob have skills it can use
		if (m_MOB_Data->m_NumSkills && current_tick > m_SkillTime && 
			(RangeFrom(p) < (m_WeaponFiringRange * m_RangeModifier)))
		{
			if (rand()%100 < m_MOB_Data->m_SkillChance)
			{
				// success
				if (UseSkill())
				{
					// set the cooldown
					m_SkillTime = current_tick + m_MOB_Data->m_SkillCooldown*1000;
				}
				else
				{
					// try again in 5s
					m_SkillTime = current_tick + 5000;
				}
			}
			else
			{
				// try again in 5s
				m_SkillTime = current_tick + 5000;
			}
		}
	}
	else if (o && current_tick > time_to_attack)
	{
		//attack a non-player target
		if ( !m_MenaceDenyWeapon )
			FireWeapon(o);

		LockTarget();
		m_DamageNode.func = B_MOB_DAMAGE;
		m_DamageNode.obj = o;
	}
	else
	{
		m_HostilityTargetID = 0;
	}
}

void MOB::LockTurretTarget()
{
	//choose target for turret
	ObjectList::iterator itrOList;
	Object *o;

	//first see if there's anything in the hate list to attack - this overrides anything else
	if (GetMaxHateID() != 0)
	{
		LockTarget();
	}

	for (itrOList = m_MOBVisibleList.begin(); itrOList < m_MOBVisibleList.end(); ++itrOList) 
	{
		o = (*itrOList);
		//choose a new target
		LockMOBTarget((MOB *)o);
	}
}

void MOB::FireWeapon(Object *obj)
{
	// Only fire on things in front of you
	// NOTE: needs fixing for missles
	if (m_DamageNode.event_time != 0)
	{
		return;
	}

	if (!IsFacingObject(obj))
	{
		FaceObject(obj);
		//	return;
	}


	unsigned long current_tick = GetNet7TickCount();
	short weapon_fx = 0;
	long weapon_time = 0;
	float range = RangeFrom(obj);

	weapon_time = m_WeaponDamageDelay ? (long)(range*3.0f) : 1000;

	SendFXToVisibilityList(obj->GameID(), GameID(), weapon_time, m_WeaponFX);

	float damage_fraction = (float)((rand() % 13) + (rand() % 13) + (rand() % 13) + 3)/30.0f;
	float damage = m_WeaponDPS * damage_fraction * m_WeaponReloadTime * m_DamageModifier;

	// Remove hate for every hit you take
	SubtractHate(obj->GameID(), (int)(damage/10.0f));

	//this will also send the client-damage packet
	GetSectorManager()->AddTimedCallPNode(&m_DamageNode, B_SHIP_DAMAGE, weapon_time, obj, GameID(), m_DamageType, 0, 0, 0, damage);
	m_DamageNode.player_id = obj->GameID();
	m_LastAttackTime = current_tick;
}

bool MOB::UseSkill()
{
	int ability = m_MOB_Data->m_SkillIDs[rand()%m_MOB_Data->m_NumSkills];
	long base_skill = g_ServerMgr->m_SkillsList.GetBaseSkillID(ability);
	int TargetID = HostilityTarget();
	if (ability < MAX_ABILITY_IDS && ability >= 0 && m_AbilityList[ability])
	{
		__try
		{
			if (m_AbilityList[ability]->CanUse(TargetID, ability, base_skill))
			{
				SetCurrentSkill(m_AbilityList[ability]);
				m_AbilityList[ability]->Use(TargetID);
				return true;
			}
		}
		__except(EXCEPTION_EXECUTE_HANDLER)
		{
			LogMessage("mob skill use %d crashed\n", ability);
		}
	}
	return false;
}

void MOB::SendFXToVisibilityList(long target_id, long source_id, long time_delay, short effect_id)
{
	Player *p = (0);
	ObjectManager *om = GetObjectManager();
	if (!om) return;

	ObjectToObjectEffect object_fx;
	object_fx.Bitmask = 0x07;
	object_fx.GameID = source_id;
	object_fx.TargetID = target_id;
	object_fx.EffectDescID = effect_id;
	object_fx.Message = 0;
	object_fx.EffectID = om->GetAvailableSectorID();
	object_fx.TimeStamp = GetNet7TickCount();
	object_fx.Duration = short(time_delay);

	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))
	{
		p->SendObjectToObjectEffect(&object_fx);
	}
}

//This sends to all players who can see this MOB
void MOB::SendToVisibilityList(bool force_update)
{
	Player *p = (0);
	PositionInformation pos;

	if ((m_Destination || m_DestinationFlag) && !force_update) return; //don't send regular impluses if we have a target

	//m_Mutex.Lock();
	UpdatePositionInfo(&pos);
	//m_Mutex.Unlock();

	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))
	{
		p->SendAdvancedPositionalUpdate(GameID(), &pos);
	}    
}

void MOB::SendMOBData(Player *p)
{
	if (p->DebugPlayer()) return;

	SendObject(p);
}

//tentative aggro settings:
/*
0 - green
1 - yellow - MOB may fire a warning shot or two if you get too close.
2 - yellow - as above, but doing more damage.
3 - yellow - as above, but may call in mates if you attack first.
4 - yellow - as above but with stronger chance of summoning help if you attack first.
5 - red - will attack when it spots you + same rules as 3. above.
6 - red - patrols area for players/enemy mobs.
7 - red - hunt for players, will summon help if it's getting in trouble.
8 - red - hunt for players, will summon all other MOBs in spawn within 5k as soon as it attacks.

group_attack:
0 - never
1 - paired
2 - recruit all nearby

aggro:
0 - never
1 - only if shot
2 - if inside <aggro_rng>
3 - if visible
4 - always
*/

void MOB::SendRelationship(Player *p)
{
	long reaction = RELATIONSHIP_ATTACK; //MOBs are hostile by default if they have no faction

	if (m_MOB_Data && m_FactionID > 0)
	{
		//OK, MOB has faction. Let's get the MOB's standing with this player.
		float standing = p->GetFactionStanding(this);

		if (standing >= -500.0f) reaction = RELATIONSHIP_SHUN;
		if (standing >= 2000.0f) reaction = RELATIONSHIP_FRIENDLY;
	}

	p->SendRelationship(GameID(), reaction, 0);
}

void MOB::SendAggroRelationship(Player *p)
{
	p->SendRelationship(GameID(), RELATIONSHIP_ATTACK, 0);
}

void MOB::Remove()
{
	Object *o = (0);
	Player *p = (0);
	ObjectManager *om = GetObjectManager();
	if (!om) return;

	SetActive(false);

	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))
	{
		p->RemoveObject(GameID());
		if (p->ShipIndex()->GetTargetGameID() == GameID())
		{
			p->SwitchOffAutofire();
		}
	}

	if (m_HostilityTargetID)
	{
		o = om->GetObjectFromID(m_HostilityTargetID);
		if (o)
		{
			if (o->ObjectType() == OT_PLAYER)
			{
				p = (Player *)o;
				p = g_PlayerMgr->GetPlayer(m_HostilityTargetID);
				LostTarget(p);
			}
			else
			{
				RemoveObjectFromRangeList(&m_MOBVisibleList, o);
				LostTarget(p);
			}
		}	
	}


	m_HostilityTargetID = 0;
	memset (&m_RangeList, 0, sizeof(m_RangeList));
	memset (&m_PlayerVisibleList, 0, sizeof(m_PlayerVisibleList));
	memset (&m_ClickedList, 0, sizeof(m_ClickedList));
	memset (&m_HateList, 0, sizeof(m_HateList));
}

float MOB::DamageObject(long source_id, long damage_type, float damage, long inflicted)
{
	float damage_orig = damage;
	ObjectManager *om = GetObjectManager();

	if (!om)
	{
		return 0;
	}

	if (m_MOB_Data && (damage_type == DAMAGE_EMP && (m_MOB_Data->m_Type == ORGANIC_GREEN || m_MOB_Data->m_Type == ORGANIC_RED)))
	{
		//Organic vs EMP, no damage!
		damage = 0;
	}
	else
	{
		Object *obj = om->GetObjectFromID(source_id);
		damage = CommonDamageHandling(obj,damage_type,damage);
	}

	AddHate(source_id, (int)damage);
	AddDamage(source_id, (int)damage);

	//Add some group hate if required
	if (m_SpawnGroup)
	{
		m_SpawnGroup->AddGroupHate(source_id, (int)(damage/2.0f));
	}

	u32 tick = GetNet7TickCount();

	//issue mob hit FX - limit this to 1 per half second, pointless sending more
	if (tick > (m_DamageTime + 500))
	{
		float bonus_damage = damage - damage_orig;
		SendMobDamage(damage_orig, bonus_damage, damage_type, source_id, inflicted);
		m_DamageTime = tick;
	}

	//is the MOB dead now?
	if (damage >= GetShieldValue() && GetHullPoints() <= 0.0f && Active())
	{
		DestroyMOB(source_id);
	}

	return damage;
}

void MOB::RemoveHull(float hull_dmg, CMob *mob)
{
	SetHullPoints(GetHullPoints()-hull_dmg);
}

short husk_bassets[] =
{
	1963,
	1960,
	1961,
	1966,
	1962,
	1964,
	1965
};

void MOB::DestroyMOB(long killing_blow)
{
	//send explosion to everyone on rangelist
	SendExplosion();
	//remove MOB
	Remove();
	LostTarget();
	CalcOrientation(1.0f, 0.0f);
	SetHeading();
	ObjectManager *om = GetObjectManager();

	// Respawn check for lower bound on respawn time (needs to be at least 6 seconds to prevent dots ticking on respawned mobs)
	if(m_Respawn_time < 10)
	{
		m_Respawn_time = 20 + (Level()*5);
		g_ServerMgr->m_PlayerMgr.ErrorBroadcast("Mob respawn is < 10: %s [%d]", Name(), GameID());
	}
	if(GetCurrentSkill())
	{
		GetCurrentSkill()->InterruptSkillOnAction(INCAPACITATE);
	}

	SetRespawnTick(GetNet7TickCount() + m_Respawn_time*1000);

	if (!om) return;

	//create & populate Husk
	Husk *husk = (Husk*) om->AddNewObject(OT_HUSK, false);
	husk->SetActive(false);

	if (!m_MOB_Data)
	{
		return;
	}

	if (husk)
	{
		husk->SetBasset(husk_bassets[m_MOB_Data->m_Type]);
		husk->SetHuskName(Name());
		husk->SetPosition(Position());
		husk->SetLevel((Level() / 8) + 1);
		husk->SetScale(20.0f);
		husk->ResetResource();
		husk->PopulateHusk(m_MOB_Type);
		husk->SendObjectReset(); //ensure contents are re-sent
		husk->SetCreateType(25);
		husk->SetDestroyTimer(300000, -1); //corpse timer set to 5 minutes

		//temporary credit loot for mobs TODO: get this from Database when data ready
		switch (m_MOB_Data->m_Type)
		{
		case MANNED:
			husk->SetCreditLoot(Level() * 20 + rand()%(Level()+1));
			break;
		case ROBOTIC:
			husk->SetCreditLoot(Level() * 10 + rand()%(Level()+1));
			break;

		default:
			husk->SetCreditLoot(0);
			break;
		}
	}

	// if a turret kills something, no loot or xp
	Object *o = om->GetObjectFromID(killing_blow);
	if (o && o->ObjectType() == OT_MOB)
	{
		MOB *m = (MOB*)o;
		LogMessage("Mob %s killed by Turret %s in sector %d\n", Name(), o->Name(), GetSector());
		m->RemoveObjectFromRangeList(&m->m_MOBVisibleList, this);
		m->LostTarget();
		husk->SetPlayerLootLock(o->GameID());
		husk->SetLootTimer(1000*330);
	}
	else
	{
		//set target of most damage player to husk and give XP
		long most_damage = GetMaxDamageID();
		Player *p = g_PlayerMgr->GetPlayer(most_damage);

		if (!(p && p->PlayerIndex()->GetSectorNum() == m_SectorID && p->RangeFrom(Position()) < 40000.0f))
			p = g_PlayerMgr->GetPlayer(killing_blow);
		if (p)
		{
			if (m_DefaultBehaviour == TURRET)
			{
				g_ServerMgr->m_PlayerMgr.ErrorBroadcast("%s destroyed turret %s [%d]", p->Name(), Name(), GameID());
			}

			p->SendClientSound("On_Destroy",0,0);
			p->AddMOBDestroyExperience(Level(), Name());

			FactionData * fData = g_ServerMgr->m_FactionData.GetFactionData(GetFactionID());
			if (fData != NULL)
			{
				for (FactionValues::iterator itrRList = fData->m_reaction.begin(); itrRList != fData->m_reaction.end(); ++itrRList)
				{
					int FactionID = itrRList->first;
					float Reaction = itrRList->second;
					if (Reaction != 0.0f)
					{
						p->AwardFaction(FactionID, (long)Reaction);
					}
				}
			}

			p->CheckMissionMOBKill(m_MOB_Type);
			if (p->GroupID() != -1 && husk)
			{
				long nextLooter = GetNextGroupLooter(husk, p->GroupID());
				if (nextLooter)
					husk->SetPlayerLootLock(nextLooter);
				else
					husk->SetPlayerLootLock(p->GameID());
			}
			else
			{
				husk->SetPlayerLootLock(p->GameID());
			}
			husk->SetLootTimer(1000*150); //2 1/2 mins until public loot
		}
		else
			husk->SetLootTimer(0); // if we cant get the correct player, open to all
	}
	ResetMOBSpawnPosition();
	husk->SetActive(true);
}

long MOB::GetNextGroupLooter(Husk* husk, int GroupID)
{
	Group *g = g_ServerMgr->m_PlayerMgr.GetGroupFromID(GroupID);
	Player *looter = (0);

	if (g)
	{
		long checkedLooters = 0;
		while (checkedLooters < 6)
		{
			//Is there someone in this group spot?
			if (g->Member[g->NextLooter].GameID != -1)
			{
				Player *p = g_ServerMgr->m_PlayerMgr.GetPlayer(g->Member[g->NextLooter].GameID);
				// Does this person exist?
				if (p)
				{
					long pSectorID = p->PlayerIndex()->GetSectorNum();

					// Are they in range?
					if (p->RangeFrom(husk->Position()) <= 30000.0f && 
						pSectorID == husk->GetSectorManager()->GetSectorID())
					{
						//Bingo, found our looter, break out!
						looter = p;
						checkedLooters = 6;
						break;
					}
					else
					{
						checkedLooters++; //Not in range, keep checking
					}
				}
				else
				{
					checkedLooters++; //Don't exist, keep checking
				}
			}
			else if (g->NextLooter == 5) //No one in that spot so check the next spot
				g->NextLooter = 0;
			else
				g->NextLooter++;

			if (! g_ServerMgr->m_PlayerMgr.GetGroupFromID(GroupID)) // Sanity check
				break;
		}

		if (looter)
		{
			for (int i = 0; i < 6; ++i)
			{
				Player *p = g_ServerMgr->m_PlayerMgr.GetPlayer( g_ServerMgr->m_PlayerMgr.GetMemberID(GroupID, i));
				if (p)
				{
					p->SendVaMessage("Loot awarded to %s", looter->Name());
				}
			}

			if (g->NextLooter == 5)
				g->NextLooter = 0;
			else
				g->NextLooter++;

			return looter->GameID();
		}
		else
		{
			return 0;
		}
	}
	return 0;
}

//this can stay in the sectors thread for now - people can't do anything to the MOB while it's not there
void MOB::OnRespawn()
{
	m_ShipIndex.Shield.SetStartValue(GetMaxShield());
	SetHullPoints(GetMaxHullPoints());

	// Clear all buffs
	m_Buffs.Clear();
	m_Stats.ResetStats();	

	// clear hate list
	RemoveHate(0);

	// clear damage list
	memset(m_DamageList,0,sizeof(m_DamageList));
}

void MOB::DebuffDisplay(ObjectEffect *DebuffEffect)
{
	Player * p = (0);
	if (!m_MOB_Data)
	{
		return;
	}
	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))
	{
		p->SendObjectEffect(DebuffEffect);
	}
	return;
}

void MOB::SendExplosion()
{
	Player * p = (0);
	short explosion_basset;
	short explosion_fx;
	short explosion_sfx = 0;
	ObjectManager *om = GetObjectManager();

	if (!m_MOB_Data)
	{
		return;
	}

	switch (m_MOB_Data->m_Type)
	{
	case ROBOTIC:
		explosion_basset = 1979;
		explosion_fx = 1016;
		break;
	case MANNED:
		explosion_basset = 1976;
		explosion_fx = 1013;
		explosion_sfx = 0x2755; //TODO: bots only
		break;
	case ORGANIC_RED:
		explosion_basset = 1977;
		explosion_fx = 1014;
		break;
	case ORGANIC_GREEN:
		explosion_basset = 1982;
		explosion_fx = 1019;
		break;
	case CRYSTALLINE:
		explosion_basset = 1978;
		explosion_fx = 1015;
		break;
	case ENERGY:
		explosion_basset = 1980;
		explosion_fx = 1017;
		if (m_MOB_Data->m_Name && strstr(m_MOB_Data->m_Name, "Manes") != 0)
		{
			//LogMessage("exploding manes\n");
			explosion_sfx = 10167;
		}
		break;
	case ROCK_BASED:
		explosion_basset = 1981;
		explosion_fx = 1018;
		break;
	default:
		explosion_basset = 1976;
		explosion_fx = 1013;
		break;
	}

	ObjectEffect ObjExplosion1;

	ObjExplosion1.Bitmask = 0x07;
	ObjExplosion1.GameID = GameID();
	ObjExplosion1.EffectDescID = explosion_sfx;
	ObjExplosion1.EffectID = om->GetAvailableSectorID();
	ObjExplosion1.TimeStamp = GetNet7TickCount();
	ObjExplosion1.Duration = 4000;

	ObjectEffect ObjExplosion2;

	ObjExplosion2.Bitmask = 0x07;
	ObjExplosion2.GameID = GameID();
	ObjExplosion2.EffectDescID = 0x018d; //red shockwave
	ObjExplosion2.EffectID = om->GetAvailableSectorID();
	ObjExplosion2.TimeStamp = GetNet7TickCount();
	ObjExplosion2.Duration = 4000;

	//PointEffect(Position(), explosion_fx);

	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))
	{
		p->PointEffect(Position(), explosion_fx);
		p->UnSetTarget(GameID());
		if (explosion_sfx != 0)
		{
			p->SendObjectEffect(&ObjExplosion1);
		}
		p->SendObjectEffect(&ObjExplosion2);
	}
}


void MOB::UpdateObject(u32 current_tick)
{
	bool shields = m_ShieldRecharge > 0; // recently hit?
	bool hull = m_LastTickHullLevel != GetHullPoints(); // recently hit?

	if (m_ShieldRecharge > 0 && m_ShieldRecharge < current_tick)
	{
		//Add to player command stack
		RechargeShield();
	}

	//do the MOB shield/hull level
	if (shields || hull)
	{
		m_LastTickHullLevel = GetHullPoints();
		SendAuxShieldUpdate(shields,hull);
	}
}

void MOB::SendMobDamage(float damage, float bonus_damage, long damage_type, long game_id, long inflicted)
{
	RangeListVec::iterator itrRList;
	Player * p = (0);

	ObjectEffect ObjExplosion;

	ObjExplosion.Bitmask = 0x07;
	ObjExplosion.GameID = GameID();
	ObjExplosion.EffectDescID = 566;
	ObjExplosion.EffectID = GetObjectManager()->GetAvailableSectorID();
	ObjExplosion.TimeStamp = GetNet7TickCount();
	ObjExplosion.Duration = 1000;

	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))
	{
		//if this is first couple of explosions, send everything
		if (m_ShieldFXSent < 2)
		{
			p->SendObjectToObjectLinkedEffect(this, p, 0x0021, 0x0003, 2.0f);
			p->SendObjectEffect(&ObjExplosion);
			m_ShieldFXSent++;
		}
		else if (p->GameID() == game_id && p->AttacksThisTick() == 0) //always send at least one effect for the attacking player
		{
			p->SendObjectToObjectLinkedEffect(this, p, 0x0021, 0x0003, 2.0f);
			p->IncAttacksThisTick();
		}
		//always send client damage
		p->SendClientDamage(GameID(), p->GameID(), damage, bonus_damage, damage_type, inflicted);            
	}
}

void MOB::SendShieldLevel(Player *p)
{
	SendAuxShip(p);
}

void MOB::UpdatePositionInfo(PositionInformation *pos)
{
	memcpy(pos, &m_Position_info, sizeof(m_Position_info));

	if (m_DestinationFlag)
	{
		pos->Bitmask |= 0x0100;
		pos->UpdatePeriod = m_ArrivalTime - GetNet7TickCount();
		pos->Position[0] = m_DestinationPos[0];
		pos->Position[1] = m_DestinationPos[1];
		pos->Position[2] = m_DestinationPos[2];
	}
}

void MOB::SendPosition(Player *player)
{
	PositionInformation pos;

	UpdatePositionInfo(&pos);

	player->SendAdvancedPositionalUpdate(
		GameID(),
		&pos);
}

//called on creation of MOB graphic object for Player
void MOB::OnCreate(Player *player)
{

}

//Called when this MOB is targeted by player
void MOB::OnTargeted(Player *player)
{
	if (!IsClickedBy(player)) 
	{
		SendShieldLevel(player);
		SetClickedBy(player);
	}

	player->BlankVerbs();
	if (player->ShipIndex()->GetIsIncapacitated())
	{
		player->AddVerb(VERBID_FOLLOW, -1.0f);
	}
	else
	{
		player->AddVerb(VERBID_FOLLOW, 1500.0f);
	}
}

void MOB::OnUnTargeted(Player *player)
{
	UnSetClickedBy(player);
	//remove autofire if set
	//player->SwitchOffAutofire();
}

void MOB::SetBehaviour(short new_behaviour)
{
	if ( !m_Menace )
	{
		m_Behaviour = new_behaviour;
	}
	else
	{
		if ( new_behaviour == MENACE )
			m_Behaviour = MENACE;
	}
}

void MOB::AddBehaviourObject(Object *obj)
{
	//m_Mutex.Lock();
	m_BehaviourList.push_back(obj);
	//m_Mutex.Unlock();
}

void MOB::AddBehaviourPosition(float *pos)
{
	m_HomePosition[0] = pos[0];
	m_HomePosition[1] = pos[1];
	m_HomePosition[2] = pos[2];
}

void MOB::LostTarget(Player *p, bool incap) //we may need to know if the player was incapacitated
{
	if (p && m_HostilityTargetID == p->GameID())
	{
		if (m_Attacking == true) p->AttackMusicUpdate(false, GameID());
		m_Attacking = false;
	}

	if (p)
	{
		RemoveHate(p->GameID());
		g_PlayerMgr->UnSetIndex(p, m_PlayerVisibleList);
	}

	if (GetCurrentSkill())
	{
		GetCurrentSkill()->InterruptSkillOnAction(OTHER);
	}

	m_HostilityTargetID = 0;
	Turn(m_DefaultTurn);
	m_YInput = 0;
	m_Position_info.RotY = 0;
	m_ZInput = 0;
	m_Position_info.RotZ = 0;

	SetBehaviour(m_DefaultBehaviour);
	SetVelocity( m_DefaultVelocity );
	m_Destination = (0);
	SetUpdateRate(m_DefaultUpdateRate);
}

void MOB::CalcNewHeading(float tdiff)
{
	float rot_Z[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
	float rot_Y[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
	float result[4];
	bool change = false;

	if (m_ZInput != 0.0f)
	{
		rot_Z[2] = -m_ZInput*tdiff*0.42f/883.0f*1000.0f;
		change = true;
	}

	if (m_YInput != 0.0f)
	{
		rot_Y[1] = -m_YInput*tdiff*0.42f/883.0f*1000.0f;
		change = true;
	}

	if (change)
	{
		Quat4fMul(rot_Z, rot_Y, result);
		Quat4fMul(Orientation(), result, Orientation());
		Quat4fNormalize(Orientation());
		SetHeading();
	}

	//work out behaviour
	MOBBehaviour();
}

void MOB::MOBBehaviour()
{
	SectorManager *sm = GetSectorManager();
	m_DestinationFlag = false;
	switch(m_Behaviour)
	{
	case INVALID_BEHAVIOUR:
		{
			//see if this MOB is part of a field
			if (m_SpawnGroup)
			{
				Object *obj = m_SpawnGroup;
				if (obj->ObjectType() == OT_FIELD)
				{
					if (m_BehaviourList.size() == 0)
					{
						m_BehaviourList.push_back(obj);
					}
					SetBehaviour(PATROL_FIELD);
					m_DefaultBehaviour = PATROL_FIELD;
				}
			}
			else
			{
				SetBehaviour(DRIFT);
				m_DefaultBehaviour = DRIFT;
			}
		}
		break;

	case PATROL_FIELD:
		{
			//select a target to check out. TODO: If the MOB sees gaps in the field he'll sometimes check them out
			Field *field = (0);
			if (GameID() == 100084)
			{
				Sleep(1);
			}
			if (m_BehaviourList.size() > 0)
			{
				Object *obj = m_BehaviourList[0];
				if (obj->ObjectType() == OT_FIELD)
				{
					field = (Field*)m_BehaviourList[0];
				}
			}
			if (!field) return;

			//have we reached the current destination?
			if (m_Destination)
			{
				if (GetNet7TickCount() > m_ArrivalTime)
				{
					if (GameID() == 100084)
					{
						Sleep(1);
					}
					m_Destination = field->SetDestination(m_Destination);
					TravelToObject();
				}
			}
			else
			{
				m_Destination = field->SetDestination(0);
				TravelToObject();
			}
		}
		break;

	case PURSUE:
		PursueTarget();
		break;

	case CURIOUS:
		//set off after destination
		ExamineTarget();
		break;

	case MENACE:
		if ( GetNet7TickCount() > m_MenaceExpire )
		{
			//LogMessage("Resetting Menace for MOB_ID : %d\n",GameID());
			m_Menace = false;
			m_MenaceDenyWeapon = false;
			m_MenaceDenySkill = false;
			SetBehaviour( m_MencaceOldBehaviour );
			// Remove Effect
			if ( m_MenaceID != -1 )
			{
				Player* player = (Player*)m_MenaceObject;
				if ( player )
					player->RemoveEffectRL(m_MenaceID);
			}
			MOBBehaviour();
		}
		else
		{
			FaceAwayFromObject( m_MenaceObject );
			SetVelocity( m_DefaultVelocity );
			SendLocationAndSpeed(true);
		}
		break;

	default:
		break;
	}
}

void MOB::PursueTarget()
{
	if (HostilityTarget() != 0)
	{
		Object *obj = GetObjectManager()->GetObjectFromID(HostilityTarget());
		if (obj && GetNet7TickCount() > m_ArrivalTime)
		{
			FaceObject(obj);
			SetVelocity( m_DefaultVelocity );

			//for stationary MOBs, don't move
			if (m_DefaultVelocity > 0.0f)
			{
				// work out position to target, with distance to travel before next update
				float *_heading = Heading();
				float range = RangeFrom(obj->Position());
				float travel_distance = Velocity() * ((float)UpdateRate()*0.1f);

				if (range < travel_distance || range < m_AttackRange)
				{
					travel_distance = range - (m_AttackRange - 100.0f); //aim for within a good fair distance
					SetVelocity(m_PursuitVelocity);
					if (range < 400.0f)
					{
						SetVelocity( -50.0f );
						travel_distance = Velocity() * ((float)UpdateRate()*0.1f);
					}
					else if (range < m_AttackRange || (Velocity() == -50.0f && range > 750.0f))
					{
						SetVelocity( 0.0f );
						travel_distance = 0.0f;
					}
					else if (Velocity() > m_PursuitVelocity)
					{
						SetVelocity( m_PursuitVelocity );
					}
				}

				//do we exceed range to host field
				if (m_DefaultBehaviour == PATROL_FIELD)
				{
					if (m_BehaviourList.size() > 0)
					{
						Object *f = m_BehaviourList[0];
						if (RangeFrom(f->Position(), true) > (10000.0f + f->Signature()))
						{
							LogMessage("Stopped pursuing %s, home is %.2f away.\n", obj->Name(), RangeFrom(f->Position(), true));
							GoingHome();
						}
					}
					else
					{
						m_DefaultBehaviour = DRIFT;
						SetBehaviour(DRIFT);
					}
				}

				m_DestinationFlag = true;

				//calculate destination
				m_DestinationPos[0] = PosX() + ( travel_distance * _heading[0] );
				m_DestinationPos[1] = PosY() + ( travel_distance * _heading[1] );
				m_DestinationPos[2] = PosZ() + ( travel_distance * _heading[2] );

				if (range < travel_distance)
				{
					m_ArrivalTime = GetNet7TickCount() + UpdateRate()*20;
				}
				else
				{
					m_ArrivalTime = GetNet7TickCount() + UpdateRate()*100;
				}
			}
			else
			{
				m_ArrivalTime = GetNet7TickCount() + UpdateRate()*100;
			}

			SendLocationAndSpeed(true);
		}
	}
}

bool MOB::Menace(Object* obj, long skillRank, float seconds,long EffectID,bool deny_skill,bool deny_weapon)
{
	// Get level of mob
	short level = Level();

	// Set base chance
	int chance = 50 + (int)((skillRank*7 - level)*1.5f);

	if ( level < 15 )      // Cowardly
		chance += 10;
	else if ( level < 25 ) // Timid
		chance += 5;
	else if ( level < 40 ) // Bold
		chance += 0;
	else if ( level < 50 ) // Suicidal
		chance += -5;
	else if ( level < 55 ) // Hard Knox
		chance += -10;
	else if ( level < 60 ) // Invincible
		chance += -20;
	else
		chance += -25;

	if (chance < 0)
		chance = 0;
	if (chance > 100)
		chance = 100;

	// Calculate success rate
	int random = rand() % 100;
	bool success = ( random <= chance ) ? true : false;

	if ( m_Behaviour != MENACE && success )
	{
		//LogMessage("Setting Menace for MOB_ID : %d\n",GameID());
		m_MenaceID = EffectID;
		m_MenaceExpire = GetNet7TickCount() + (unsigned long)( seconds * 1000 );
		m_MenaceObject = obj;
		m_MencaceOldBehaviour = m_Behaviour;
		m_Menace = true;
		m_MenaceDenyWeapon = deny_weapon;
		m_MenaceDenySkill = deny_skill;
		SetBehaviour(MENACE);
		return true;
	}

	return false;
}

void MOB::SetDefaultStats(float turn, short behaviour, float velocity, long update_rate)
{
	m_DefaultTurn = turn;
	m_DefaultBehaviour = behaviour;
	m_DefaultVelocity = velocity;
	m_DefaultUpdateRate = update_rate;
	m_PursuitVelocity = m_DefaultVelocity * 1.2f;
}

void MOB::TravelToObject()
{
	m_ZInput = 0;
	m_Position_info.RotZ = 0;

	if (m_Destination == 0) return;

	FaceObject(m_Destination);
	//calculate arrival time
	m_ArrivalTime = GetNet7TickCount() + (u32)((m_Destination->RangeFrom(Position())/Velocity())*1000.0f);
	/*m_DestinationPos[0] = m_Destination->PosX();
	m_DestinationPos[1] = m_Destination->PosY();
	m_DestinationPos[2] = m_Destination->PosZ();*/
	SendLocationAndSpeed(true);
}

void MOB::TravelToPosition(float * position)
{
	m_ZInput = 0;
	m_Position_info.RotZ = 0;

	FacePosition(position);
	//calculate arrival time
	m_ArrivalTime = GetNet7TickCount() + (u32)((RangeFrom(position)/Velocity())*1000.0f);
	SendLocationAndSpeed(true);
}

bool MOB::IsDetectable(Player *p)
{
	if (!p) return false;

	float visibility = (float)p->ShipIndex()->CurrentStats.GetVisibility();
	if (visibility < 0) visibility = 0;
	visibility += m_ScanRange;

	if (p && p->ShipIndex()->GetIsCloaked() && GetStealthDetectionLevel() < p->GetStealthLevel())
	{
		return false;
	}
	else if (p && p->ShipIndex()->GetIsCountermeasureActive() && GetStealthDetectionLevel() < p->GetCounterMeasuresLevel())
	{
		return false;
	}
	else if (p && visibility <= 0.0f)
	{
		return false;
	}
	else
	{
		return true;
	}
}

void MOB::HandleMovementChanges()
{
	float range;
	long rand_roll = rand()%50 + rand()%50;
	Object *target = (0);
	if (m_SpawnGroup)
	{
		switch (m_SpawnGroup->ObjectType())
		{
		case OT_MOBSPAWN:
			if (m_Behaviour == DRIFT)
			{
				//first see if MOB is too far out from spawn
				//get spawn range
				range = m_SpawnGroup->RangeFrom(Position());
				//get spawn radius
				if (range > m_SpawnGroup->SpawnRadius())
				{
					//out of range, reflect the MOB back to the centre
					FaceObject(m_SpawnGroup);	
					SetupRandomMovement();
					SendLocationAndSpeed(false);
					break;
				}

				//now introduce random position changes
				if (rand_roll > 75)
				{
					//head toward an object on the range list if any
					target = PickObject();
					if (target)
					{
						FaceObject(target);
						SetBehaviour(CURIOUS);
						m_Destination = target;
						m_YInput = 0;
						m_ZInput = 0;
					}
				}
				else if (rand_roll < 5)
				{
					//change heading
					SetupRandomMovement();
					SendLocationAndSpeed(false);
				}
			}


			break;

		default:
			break;
		}
	}
}

void MOB::SetupRandomMovement()
{
	float velocity = (float)(rand()%100 + 130);
	float turn = (float)((rand()%11) - 5) * 0.01f;
	if (turn == 0.0f) turn = 0.02f;
	SetVelocity(velocity);
	SetDefaultStats(turn, DRIFT, velocity, 50);
	Turn(turn);
}

void MOB::ResetMOBSpawnPosition()
{
	float pos[3];
	if (m_SpawnGroup && m_SpawnGroup->ObjectType() == OT_MOBSPAWN)
	{
		SetPosition(m_SpawnGroup->Position());
		//work out a position vector & angle
		float vector = (float)(rand()%200 - 99)*0.01f;
		float angle = (float)(rand()%360)/(360.0f / (2*PI));
		pos[0] = (m_SpawnGroup->SpawnRadius() * vector) * cosf(angle);
		pos[1] = (m_SpawnGroup->SpawnRadius() * vector) * sinf(angle);
		MovePosition(pos[0], pos[1], ((float)((rand()%11) - 5) * 200.0f));
	}
	else
	{
		LogMessage("Unable to find %s [%d] spawn position.\n", Name(), GameID());
		g_ServerMgr->m_PlayerMgr.ErrorBroadcast("Unable to find %s [%d] spawn position.\n", Name(), GameID());
		SetPosition(m_HomePosition);
	}
}

Object * MOB::PickObject()
{
	Object *obj = (0);
	Player *p = (0);
	long player_count = 0;

	while (g_PlayerMgr->GetNextPlayerOnList(p, m_PlayerVisibleList))
	{
		//do not allow players who are cloaked at a lvl insufficient for the mob's targeting to be added to the target list
		if(!IsDetectable(p))
		{
			continue;
		}

		++player_count;
	}

	if (player_count > 0)
	{
		//pick a target
		long targ_num = rand()%player_count;
		p = (0);
		while (g_PlayerMgr->GetNextPlayerOnList(p, m_PlayerVisibleList))
		{
			if(!IsDetectable(p))
			{
				continue;
			}

			if (targ_num == 0) break;

			--targ_num;
		}

		if (p) obj = (Object*)p;
	}

	return obj;
}

void MOB::MOBEffect(long player_id, long effect_id)
{
	Player *p = g_PlayerMgr->GetPlayer(player_id);

	if (p)
	{
		ObjectEffect ObjEffect;

		ObjEffect.Bitmask = 0x07;
		ObjEffect.GameID = GameID();
		ObjEffect.EffectDescID = (short) effect_id;
		ObjEffect.EffectID = GetObjectManager()->GetAvailableSectorID();
		ObjEffect.TimeStamp = GetNet7TickCount();
		ObjEffect.Duration = 1000;

		p->SendObjectEffect(&ObjEffect);
	}
}

void MOB::ExamineTarget()
{
	if (m_Destination && m_Destination->Active())
	{
		Player * p = (Player*)m_Destination;

		if (p->AdminLevel() == 90) //this is for checking MOBs are behaving correctly.
		{
			MOBEffect(p->GameID(), 106);
		}

		FaceObject(m_Destination);
		SetVelocity( m_DefaultVelocity );
		// work out position to target, with distance to travel before next update
		float *_heading = Heading();
		float range = RangeFrom(m_Destination->Position());
		float travel_distance = Velocity() * ((float)UpdateRate()*0.1f);

		if (range < travel_distance)
		{
			travel_distance = range - 900.0f; //aim for within a good fair distance
			SetVelocity((travel_distance)/((float)UpdateRate()*0.1f));
			if (range < 1000.0f)
			{
				SetVelocity ( 0.0f );
				travel_distance = 0.0f;
				m_Destination = (0); //return to drift
			}
			else if (Velocity() > m_DefaultVelocity * 0.9f)
			{
				SetVelocity( m_DefaultVelocity * 0.9f );
			}
		}
		SendLocationAndSpeed(true);
	}
	else
	{
		SetBehaviour(m_DefaultBehaviour);
		//LogMessage("Mob %s return to drift\n", Name());
		Turn(m_DefaultTurn);
		m_YInput = 0;
		m_Position_info.RotY = 0;
		m_ZInput = 0;
		m_Position_info.RotZ = 0;

		SetBehaviour(m_DefaultBehaviour);
		SetVelocity( m_DefaultVelocity );
		m_Destination = (0);
		SetUpdateRate(m_DefaultUpdateRate);
		m_Destination = (0);
	}
}

char * MOB::GetSpawnName()
{
	char *spawn = 0;
	if (m_SpawnGroup)
	{
		spawn = m_SpawnGroup->Name();
	}

	return spawn;
}

void MOB::DisplayLoot(Player *p)
{
	long loot_item_id;

	if (!p ) return;

	p->SendVaMessage("MOB %s Level %d Loot Table", Name(), Level());

	if (!m_MOB_Data)
	{
		p->SendVaMessage("Error: No m_MOB_Data for %d", GameID());
		return;
	}

	for (unsigned int i = 0; i < m_MOB_Data->m_Loot.size(); i++)
	{
		loot_item_id = m_MOB_Data->m_Loot[i]->item_base_id;
		ItemBase *item = g_ItemBaseMgr->GetItem(loot_item_id);

		if (item)
		{
			p->SendVaMessage("id %d: %s[L%d:ID#%d] Count: %d Dropchance %.2f", i, item->Name(), item->TechLevel(), loot_item_id, m_MOB_Data->m_Loot[i]->quantity, m_MOB_Data->m_Loot[i]->drop_chance);
		}
	}
}

MOB_Aggression MOB::GetAggression()
{
	if (m_MOB_Data)
		switch (m_MOB_Data->m_Agressiveness) // shouldnt this be 0-3?
	{
		case 0:
		case 1:
		case 2:
			return AGGRESSION_NONE;
		case 3:
		case 4:
			return AGGRESSION_CAUTIOUS;
		case 5:
		case 6:
			return AGGRESSION_ANTAGONISTIC;
		default:
			return AGGRESSION_FANATIC;
	}
	return (MOB_Aggression)(rand()%4); // shouldnt get here
}

void MOB::Taunt(CMob *taunter)
{
	long taunt_id = taunter->GameID();
	int highest_hate = 0;
	long highest_hate_val = m_HateList[0].hate;
	int lowest_hate = 0;
	long lowest_hate_val = m_HateList[0].hate;
	int player_index = -1;

	//m_Mutex.Lock();

	//find highest other person on hate list
	for (int i = 0; i < HATE_SIZE; i++)
	{
		if (m_HateList[i].GameID == taunt_id)
			player_index = i;
		else
		{
			if (m_HateList[i].hate > highest_hate)
			{
				highest_hate = i;
				highest_hate_val = m_HateList[i].hate;
			}
			if (m_HateList[i].hate < lowest_hate_val)
			{
				lowest_hate = i;
				lowest_hate_val = m_HateList[i].hate;
			}
		}
	}

	// is player already in the list
	if (player_index == -1)
		player_index = lowest_hate;

	// boost the players hate to 125% of maximum other player + a small amount for 0 hate cases
	m_HateList[player_index].GameID = taunt_id;
	long newhate = highest_hate_val * 5 / 4 + 10;
	if (newhate > m_HateList[player_index].hate)
		m_HateList[player_index].hate = newhate;

	//m_Mutex.Unlock();

	LockTarget();
}

void MOB::Hack(CMob *hacker, HACK_Type hack)
{
	float damage;

	switch (hack)
	{
	case HACK_ENGINE:
		SetGravityField(0,0,10); // cant move for 10 seconds
		AddHate(hacker->GameID(),10);
		break;
	case HACK_REACTOR: // reduce energy to 50%
		// TODO: mobs dont have energy at the moment
		AddHate(hacker->GameID(),1);
		break;
	case HACK_SHIELD: // reduce shields to 50%
		damage = GetShieldValue() - GetMaxShield() * 0.5f;
		if (damage > 0.0f)
			DamageObject(hacker->GameID(),DAMAGE_ENERGY,damage,0);
		break;
	case HACK_DEVICE:
		// TODO: no idea, prevent skill use maybe?
		AddHate(hacker->GameID(),1);
		break;
	case HACK_WEAPON:
		m_LastAttackTime = GetNet7TickCount() + 10000;
		AddHate(hacker->GameID(),(int)(m_WeaponDPS*10.0f));
		break;
	case HACK_COMMS:
		// TODO: stop it calling for help when mobs are properly grouped/baf
		AddHate(hacker->GameID(),1);
		break;
	}
}

void MOB::Befriend(CMob *afriend, Befriend_Type type)
{
	switch (type)
	{
	case BEFRIEND: // improve relations
		RemoveHate(afriend->GameID());
		// TODO: improve faction/reaction
		break;
	case BEFRIEND_CALM: // reduce aggro range
		// TODO: mobs dont seem to have an aggro range :O
		break;
	}
}

void MOB::BlastDamage(Player *p, float *position, float damage)
{
	//is this mob in range of the detonate position?
	if (g_PlayerMgr->GetIndex(p, m_RangeList))
	{
		//MOB is in visual range of player
		float range = RangeFrom(position, true);

		if (range < 1000.0f)
		{
			damage *= CaughtInEnergyBlast(range);
			DamageObject(p->GameID(), DAMAGE_ENERGY, damage, 0);
		}
	}
}

void MOB::SendAuxDataPacket(Player *player)
{
	m_ShipIndex.BuildCreatePacket();

	if (m_ShipIndex.m_CreateSize > 0)
	{
		player->SendOpcode(ENB_OPCODE_001B_AUX_DATA, 
			m_ShipIndex.m_CreatePacket, 
			(u16)m_ShipIndex.m_CreateSize);
	}
}

//This method is used to update shield levels for MOBs regenerating shields
//Only send to players which are in range and have clicked on the mob
void MOB::SendAuxShieldUpdate(bool shields, bool hull)
{	
	Player *p = (0);
	m_ShipIndex.BuildDiffPacket(shields, hull);

	//shield level has changed, update clients in range who have clicked on this MOB 
	while (g_PlayerMgr->GetNextPlayerOnList(p, m_ClickedList))
	{
		p->SendOpcode(ENB_OPCODE_001B_AUX_DATA, m_ShipIndex.m_DiffPacket, m_ShipIndex.m_DiffSize);
	}

	m_ShieldFXSent = 0; //reset the shield FX send
}

void MOB::SendAuxShip(Player * other)
{
	if (!other) return SendAuxShieldUpdate(); //only the shield update sends without a specific player
	/* Block anyone else from using the buffer */
	//m_ShipIndex.Buffer()->Lock();

	m_ShipIndex.BuildClickPacket();

	if (other)
	{
		other->SendOpcode(ENB_OPCODE_001B_AUX_DATA, m_ShipIndex.m_ClickPacket, m_ShipIndex.m_ClickSize);
	}

	/* Unblock buffer */
	//m_ShipIndex.Buffer()->Unlock();   
}

void MOB::_SendAuxDataPacket(Player *player)
{
	//  Build and send aux packet
	//m_ShipIndex.Buffer()->Lock();

	m_ShipIndex.BuildCreatePacket();

	if (m_ShipIndex.m_CreateSize > 0)
	{
		player->SendOpcode(ENB_OPCODE_001B_AUX_DATA, 
			m_ShipIndex.m_CreatePacket, 
			(u16)m_ShipIndex.m_CreateSize);
	}

	//m_ShipIndex.Buffer()->Unlock();
}

void MOB::MovePosition(float x, float y, float z, bool _override)
{
	if (IsTurret() && !_override) //don't allow turrets to be moved
	{
		return;
	}
    m_Position_info.Position[0] = m_Position_info.Position[0] + x;
    m_Position_info.Position[1] = m_Position_info.Position[1] + y;
    m_Position_info.Position[2] = m_Position_info.Position[2] + z;
}

void MOB::SetName(char *name)
{
	if (name)
	{
		m_Name = g_StringMgr->GetStr(name);
		m_NameLen = strlen(name);
		m_ShipIndex.SetName(m_Name);
	}
}