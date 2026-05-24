// SectorManager.cpp
//
//	Used by the Sector Server to manage a single sector
//
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

#include "Net7.h"
#include "SectorManager.h"
#include <net7/Opcodes.h>
#include <net7/PacketStructures.h>
#include "ServerManager.h"
#include "ItemBase.h"
#include "SectorData.h"
#include "MemoryHandler.h"
#include "ObjectManager.h"
#include "UDPConnection.h"
#include "PacketMethods.h"
#include <cstring>     // std::memset for pthread_t init (Phase M)
#include <cerrno>      // strerror in pthread_create error path
#include <float.h>

// There is one instance of this class for each sector

SectorManager::SectorManager(ServerManager *server_mgr)
{
    m_ServerMgr = server_mgr;
    m_SectorData = NULL;
    m_SectorConnection = NULL;
    m_SectorID = -1;
    m_Port = -1;
	m_IPAddr = -1;
	m_IsSectorServerReady = false;
	m_ShutDownMark = 0;	
	m_SectorName = 0;

	//need to start up a timer thread
	m_SectorThreadRunning = false;

	memset(m_EventSlots, 0, sizeof(m_EventSlots));
	memset(m_CoarseEventSlots, 0, sizeof(m_CoarseEventSlots));
    m_EventSlotIndex = 0;
	memset(m_EventSlotsIndex, 0, sizeof(m_EventSlotsIndex));
	m_Greetings = (0);
	m_ObjectMgr = (0);

	memset(m_PlayerList, 0, sizeof(m_PlayerList));
	m_PlayerCount = 0;
	m_SectorEffectID = 0;

	m_JobTerminalLevel = 0;
	m_JobListID = 0;

	std::memset(&m_SectorThread, 0, sizeof(m_SectorThread));
}

void SectorManager::BeginSectorThread()
{
	if (m_SectorID != -1)
	{
		// Don't actually create the thread until we know it's needed.
		if (pthread_create(&m_SectorThread, NULL, RunEventThreadAPI, this) != 0)
			LogMessage("SectorManager [%d]: pthread_create failed (%s)\n", m_SectorID, strerror(errno));
		//now start this sector's listener
		// Find a port that will work!
		while(StartListener(g_ServerMgr->GetSectorPort()) == false);
		//LogMessage("Starting %s\n", m_SectorName);
		//ResumeThread(m_SectorThread);
	}
}

void SectorManager::ShutdownListener()
{
	LogMessage("UDP connection for sector %s [%d] terminated.\n", m_SectorName, m_SectorID);
	m_SectorConnection->~UDP_Connection();
}

void SectorManager::ReStartListener()
{
	StartListener(g_ServerMgr->GetSectorPort());
}

SectorManager::~SectorManager()
{
	delete m_SectorConnection;
}

long SectorManager::GetSectorNextObjID()
{
	return (m_ObjectMgr->GetAvailableSectorID());
}

long SectorManager::GetNextEffectID()
{
	return (m_ServerMgr->GetNextEffectID());
}

void SectorManager::SetBoundaries(int sector)
{
    if (!m_SectorData)
    {
        return;
    }

    m_xmax = m_SectorData->m_xmax + 10000.0f;
    m_xmin = m_SectorData->m_xmin - 10000.0f;
    m_ymax = m_SectorData->m_ymax + 10000.0f;
    m_ymin = m_SectorData->m_ymin - 10000.0f;
    
    m_xctr = (m_xmax + m_xmin)/2;
    m_yctr = (m_ymax + m_ymin)/2;
    
    LogMessage("Sector: %d. X: %.2f   Y: %.2f\n", sector, m_xmax-m_xmin, m_ymax-m_ymin);
}

long SectorManager::GetSectorID()
{
    return m_SectorID;
}

short SectorManager::GetTcpPort()
{
    return m_Port;
}

bool SectorManager::IsSectorServerReady()
{
    return m_IsSectorServerReady;
}

void SectorManager::SetSectorServerReady(bool ready)
{
    m_IsSectorServerReady = ready;
}

// This must be called for each instance of the sector manager
bool SectorManager::StartListener(short port)
{
    m_Port = port;
    m_SectorConnection = new UDP_Connection(port, m_ServerMgr, CONNECTION_TYPE_SECTOR_SERVER_TO_PROXY);

	// Make sure we can use the port!
	if (m_SectorConnection->GetError())
	{
		delete m_SectorConnection;
		return false;
	}

	return true;
}

bool SectorManager::SetupSectorServer(long sector_id)
{
    bool success = true;
	m_SectorID = sector_id;

	g_ServerMgr->SetSectorMap(sector_id, this);

	if (sector_id < 9999)
	{
		m_SectorData = g_ServerMgr->m_SectorContent.GetSectorData(sector_id);
	}
	else
	{
		m_SectorData = g_ServerMgr->m_SectorContent.GetSectorData(sector_id/10);
	}
	
    if (!m_SectorData)
    {
        LogMessage("SetupSectorServer sector_id=%d failed\n", sector_id);
		if (sector_id > 9999)
		{
			LogMessage("This has occurred because there is no sector data for %d for station %d\n", sector_id/10, sector_id);
		}
        success = false;
    }
    else
    {
		m_SectorName = m_SectorData->name;
		m_SystemName = m_SectorData->system_name;
        m_SystemID = m_SectorData->system_id;
        m_ObjectMgr = m_SectorData->obj_manager;
		m_ParentSectorName = m_SectorData->system_name;
        if (sector_id < 9999)
        {
            m_ObjectMgr->SetSectorManager(this); //now we have a link to the sector manager via object manager
            m_ObjectMgr->InitialiseResourceContent();
        }
		else
		{
			StationTemplate *station = g_ServerMgr->m_StationMgr.GetStation(sector_id);
			m_ParentSectorName = 0;
			if (station)
			{
				if (station->WelcomeMessage) m_Greetings = station->WelcomeMessage;
				SectorData *parent_sector = g_ServerMgr->m_SectorContent.GetSectorData(sector_id / 10);
				if (parent_sector)
				{
					m_ParentSectorName = parent_sector->name;
					m_SystemName = parent_sector->system_name;
				}
				m_SectorName = station->Name;

				if (station->JTLevel > 0)
				{
					m_JobList.clear();
					m_JobTerminalLevel = station->JTLevel;
					m_JobListCount = rand()%5 + 5;
					//initialise job list
					for (int i = 0; i < m_JobListCount; i++)
					{
						JobNode *jn = new JobNode;
						memset(jn, 0, sizeof(JobNode));
						m_JobList.push_back(jn);
					}
				}
			}
			else
			{
				LogMessage("data for station id %d seems to be missing\n", sector_id);
			}
		}
    }

	InitialiseSector();

	m_ServerMgr->m_SectorCount++;

	return (success);
}

Object *SectorManager::GetObject(long object_id)
{
    return (m_ObjectMgr->GetObjectFromID(object_id));
}

Object *SectorManager::GetObject(char *object_name) //NB object_name must be in string manager
{
    return (m_ObjectMgr->GetObjectFromName(object_name));
}

ServerParameters *SectorManager::GetSectorParams()
{
    return (&m_SectorData->server_params);
}

void SectorManager::SendServerParameters(Player *player)
{
	ServerParameters parameters;
    memset(&parameters, 0, sizeof(parameters));

	parameters.ZBandMin = m_SectorData->server_params.ZBandMin;
	parameters.ZBandMax = m_SectorData->server_params.ZBandMax;
	parameters.XMin     = m_SectorData->server_params.XMin;
	parameters.YMin     = m_SectorData->server_params.YMin;
	parameters.XMax     = m_SectorData->server_params.XMax;
	parameters.YMax     = m_SectorData->server_params.YMax;

	parameters.FogNear  = m_SectorData->server_params.FogNear;
	parameters.FogFar   = m_SectorData->server_params.FogFar;
	parameters.DebrisMode = m_SectorData->server_params.DebrisMode;
	parameters.LightBackdrop = m_SectorData->server_params.LightBackdrop; //false;
	parameters.FogBackdrop = m_SectorData->server_params.FogBackdrop;
	parameters.SwapBackdrop = m_SectorData->server_params.SwapBackdrop;
	parameters.BackdropFogNear = m_SectorData->server_params.BackdropFogNear;// 0.0;
	parameters.BackdropFogFar = m_SectorData->server_params.BackdropFogFar;// 0.0;
	parameters.MaxTilt = m_SectorData->server_params.MaxTilt;
	parameters.AutoLevel = m_SectorData->server_params.AutoLevel;
	parameters.ImpulseRate = m_SectorData->server_params.ImpulseRate;
	parameters.DecayVelocity = 9.33033f;
	parameters.DecaySpin = 9.33033f;
	parameters.SectorNum = m_SectorID;
	parameters.BackdropBaseAsset = m_SectorData->server_params.BackdropBaseAsset;

	player->SendOpcode(ENB_OPCODE_0042_SERVER_PARAMETERS, (unsigned char *) &parameters, sizeof(parameters));
}

void SectorManager::HandleSectorLogin2(Player *player)
{
    if (m_SectorID > 9999)
    {
        StationLogin2(player);
    }
    else
    {
        SectorLogin2(player);
    }
}

void SectorManager::HandleSectorLogin3(Player *player)
{
    if (m_SectorID < 10000)
    {
        SectorLogin3(player);
    }

    if (!player->ToBeRemoved())
    {
        AddPlayerToSectorList(player);
    }
	else
	{
		LogMessage("Don't add player %s to sector list (for some reason!)\n", player->Name());
	}
}

bool SectorManager::HandleSectorLogin(Player *player)
{
    if (m_SectorID > 9999)
    {
        StationLogin(player);
    }
    else
    {
        SectorLogin(player);
    }

    return true;
}

void SectorManager::SectorLogin(Player *player)
{
    long GameID = player->GameID();

	LogMessage("Sector login for player %s [%08x]\n", player->Name(), GameID);

    player->SendGalaxyMap(m_SystemName, m_SectorName, "");
    player->SetManufactureID(0);
    
	player->SendClientType(m_SectorData->sector_type);
    
    player->PlayerIndex()->SetSectorName(m_SectorName);
    player->PlayerIndex()->SetSectorNum(m_SectorID);
    player->SendPlayerInfo();
}

void SectorManager::SectorLogin2(Player *player)
{
    long GameID = player->GameID();

    player->SendLoginShipData();
    
    int SpeedFactor = (m_SectorData->sector_type == ST_SPACE) ? 1 : 2;
    player->SendShipInfo(GameID, SpeedFactor);
    
    //player->SendDataFileToClient("ServerParameters.dat");
    SendServerParameters(player);
   
    // Send Navs
    if (m_SectorData)
    {
		m_ObjectMgr->SendAllNavs(player);
    }
    else
    {
        player->SendDataFileToClient("EarthSector.dat");
        player->SendDataFileToClient("EarthSectorObjects.dat");
    }
    
    player->SendVaMessage("We have entered %s Sector (%s System).", m_SectorName, m_SystemName);

    player->SendStart(player->CharacterID());
}

void SectorManager::SectorLogin3(Player *player)
{
    if (m_SectorData)
    {
		player->SendRemainingStaticObjs();
    }
}

void SectorManager::AddPlayerToSectorList(Player *player)
{
	//TODO: call a debug checker to ensure the player is not on any other sector list.
	//printf("AddPlayerToSectorList locking sm mutex\n");
    m_Mutex.Lock();
	//printf("AddPlayerToSectorList sm mutex locked\n");
	u32 player_num = player->GetGameIndex();

	if (!GetIndex(player_num))
	{
		SetIndex(player_num);
		m_PlayerCount++;
	}
    //printf("AddPlayerToSectorList sm mutex unlocked\n");
    m_Mutex.Unlock();
}

//only do this at the end of each sector processing cycle.
void SectorManager::RemovePlayerFromSectorList(Player *player)
{
    m_Mutex.Lock();
	u32 player_num = player->GetGameIndex();

	//see if it's set
	if (GetIndex(player_num))
	{
		UnSetIndex(player_num);
		m_PlayerCount--;
	}
        
    m_Mutex.Unlock();
}

//object index methods
void SectorManager::SetIndex(u32 index)
{
	u32 *entry = (u32*) (m_PlayerList + (index/(sizeof(u32)*8)));

	//now set the specific bit
	*entry |= (1 << index%32);
}

void SectorManager::UnSetIndex(u32 index)
{
	u32 *entry = (u32*) (m_PlayerList + (index/(sizeof(u32)*8)));

	//now unset the specific bit
	*entry &= (0xFFFFFFFF ^ (1 << index%32));
}

bool SectorManager::GetIndex(u32 index)
{
	u32 *entry = (u32*) (m_PlayerList + (index/(sizeof(u32)*8)));

	//now get the specific bit
	if (*entry & (1 << index%32))
	{
		return true;
	}
	else
	{
		return false;
	}
}

u32* SectorManager::GetSectorPlayerList()
{
	return m_PlayerList;
}

void SectorManager::StationLogin(Player *player)
{
    long GameID = player->GameID();
    long ManuID = player->ManuID();
    
 	LogMessage("Station login for player %s [%08x]\n", player->Name(), GameID);
   
    //player->SendGalaxyMap(m_SystemName, m_ParentSectorName, m_SectorName);
    
    // TODO: Send ClientChatEvent packet for the 'local' channel
    
    // This is the manufacturing lab
    player->ManuIndex()->SetGameID(ManuID);
    player->ManuIndex()->TerminalReset(0);
    
    player->SetManufactureID(ntohl(ManuID));
	player->SetInSpace(false);
    
    player->SendCreate(ManuID, 1.0, -1, CREATE_MANU_LAB);
    player->SendRelationship(ManuID, RELATIONSHIP_FRIENDLY, false);
    player->SendConstantPositionalUpdate(ManuID, 0, 0, 0);
    player->SendAuxManu(true);

	// Repair ship if needed
	player->RepairShip();
	// and equipment
   	player->RestoreEquipmentStructure();
	// and remove absorb
	player->ClearDamageShields(); 

	// Remove selfdestructed properties.
	player->SetSelfDestructed(false);

    player->PlayerIndex()->SetSectorName(m_SectorName);
    player->PlayerIndex()->SetSectorNum(m_SectorID);
    player->SendPlayerInfo();

	//only need to send this when logging into stations
	player->SetItemList(ITEMLIST_VAULT);
    
    player->SendLoginShipData();
    
    // TODO: send ClientChatEvent to enable the station channel

    // TODO: send ClientChatList to show which friends are online
}

void SectorManager::StationLogin2(Player *player)
{
    long GameID = player->GameID();
    
    player->SendShipInfo(GameID, 1);
    
    player->SendMessageString(m_Greetings);
    player->SendStarbaseSet(0, 0);
    
    char lounge_npc[MAX_PATH];
    long lounge_npc_id = player->TryLoungeFile(m_SectorID);
    sprintf_s(lounge_npc, sizeof(lounge_npc), "LoungeNPC_%d.dat", lounge_npc_id);
    
    if (!player->SendLoungeNPC(m_SectorID))	
    {	// if we cant find it in MySQL load from file
        LogMessage("Can't load from MySQL Loading from file (%d)\n", m_SectorID);
        player->SendDataFileToClient(lounge_npc);
    }
    
    player->SendStart(player->CharacterID());
	player->SetActive(true);
    
    // TODO: send ClientChatEvent to enable the station channel

    // TODO: send ClientChatList to show which friends are online
}

void SectorManager::LaunchIntoSpace(Player *player)
{
	char launch_msg[256];
    player->SendStarbaseSet(1, 0);
    player->SetManufactureID(0);
	if (g_ServerMgr->m_StationMgr.GetStation(m_SectorID))
	{
		sprintf_s(launch_msg, sizeof(launch_msg),
			"This is %s control... you are cleared for departure.", g_ServerMgr->m_StationMgr.GetStation(m_SectorID)->Name);
		player->SendMessageString(launch_msg);
	}
    
    // TODO: ClientChatEvent to cancel the station channel
    long to_sector_id = m_SectorID / 10;
	if (m_SectorID < 9999) to_sector_id = m_SectorID;
    m_ServerMgr->m_PlayerMgr.DropPlayerFromSector(player);
    player->PlayerIndex()->SetSectorNum(to_sector_id);
    player->SendServerHandoff(m_SectorID, to_sector_id, m_SectorName, "", m_ParentSectorName, m_SystemName);
}

void SectorManager::SectorServerHandoff(Player *player, int new_sector_id)
{
    char from_sector[128];
	char *to_sector = g_ServerMgr->GetSectorName(new_sector_id);
	char *to_system = g_ServerMgr->GetSystemName(new_sector_id);

	if (to_sector && to_system && from_sector)
	{
		player->RadiationDmg(false);
		player->SetActive(false);
		sprintf_s(from_sector, sizeof(from_sector), "%s (%s System)", m_SectorName, m_SystemName);
		m_ServerMgr->m_PlayerMgr.DropPlayerFromSector(player);
		player->PlayerIndex()->SetSectorNum(new_sector_id);
		player->SendServerHandoff(m_SectorID, new_sector_id, from_sector, m_SystemName, to_sector, to_system);
	}
}

void SectorManager::Dock(Player *player, long target)
{
    //cancel warp
    player->TerminateWarp(true);
	Object * obj = GetObject(target);
    //LogMessage("Player requested docking at %s\n", obj->Name());

	long destination = obj->Destination();
	if (destination < 900)
	{
        player->SendVaMessage("Station has no destination set - Please add station destination in DASE.");
		return;
	}
    if (!m_ServerMgr->GetSectorManager(destination))
    {
        player->SendVaMessage("Station is offline, no access permitted.");
        return;
    }

	//TODO: add field to Station DB for docking message, use that here
	char dock_message[128];

	sprintf_s(dock_message,128,"%s CONTROL: Docking clearance granted.", obj->Name());

    player->SendMessageString(dock_message);

	player->MoveToward(obj, 100.0f);
	player->SetNoPlayerUpdate();
    player->SendCameraControl(0x05000000, ntohl(target));

	player->SetStargateDestination(destination);

	player->SetVelocity(100.0f);
	player->SendContrailsRL(true);

    player->LogDockCoords();
}

bool SectorManager::GateActivate(Player *player, long target)
{
	return Gate(player, target);
}

bool SectorManager::Gate(Player *player, long target)
{
    // Initiate Gating
    //LogMessage("Player requested Gate\n");

    // determine the destination
    Object *gate = m_ObjectMgr->GetObjectFromID(target);
    player->SetStargateDestination(gate->Destination());

	long time_delay = 5800; 

	if (gate->Destination() == 0)
	{
		player->SendVaMessage("Gate Malfunction. Destination coordinates unsafe.");
		return false;
	}

	if (gate->IsLocalStargate())
	{
		Object *destination = g_SectorObjects[gate->Destination()];
		if (!destination)
		{
			LogMessage("non existant destination for local stargate ID: %d", gate->Destination());
			player->SendVaMessage("Stargate '%s' has invalid destination", gate->Name());
			return false;
		}
		if (destination->GetObjectManager() != gate->GetObjectManager())
		{
			player->SendVaMessage("Gate target is not an object in this sector: ID: %d [%s]", gate->Destination(), destination->Name());
			return false;
		}

		player->OpenStargate(target);
		AddTimedCall(player, B_LOCAL_GATE, 2000, 0, gate->Destination(), target );
		return true;
	}
    else if (!m_ServerMgr->GetSectorManager(gate->Destination()))
    {
        LogMessage("SECTOR OFFLINE. Gate Inoperative.\n");
        player->SendVaMessage("Destination sector is offline. Gate inoperative.");
        return false;
    }
	else if (gate->Destination() < 400 || gate->Destination() > 8000) 
    {
        LogMessage("Stargate '%s' has invalid destination: %d\n", gate->Name(), gate->Destination());
        player->SetStargateDestination(1060); //something's gone wrong, send player to earth sector
    }

	player->OpenStargate(target);
	player->SendClientSound("sfx_generic_gate_thrust 1",0,0);
    player->SendContrailsRL(true);
	if (player->RangeFrom(gate->Position(), true) < 80.0f)
	{
		player->FaceObject(gate);
	}
	else
	{
		player->MoveToward(gate, 20.0f);
	}

	//LogMessage("GATE HANDOFF SEQUENCE COMPLETE\n");
    //add timer to gate cam operation
    AddTimedCall(player, B_CAMERA_CONTROL, time_delay, 0, 0x04000000, ntohl(gate->GameID()) );
	return true;
}

char * SectorManager::GetSystemName(long sector_id)
{
    char *server_name = "Unknown";
    SectorData *p = g_ServerMgr->m_SectorContent.GetSectorData(sector_id);
	if (p) 
	{
		server_name = p->system_name;
	}
    return (server_name);
}

char *SectorManager::GetSectorName(long sector_id)
{
    char *sector_name = "Unknown";
    SectorData *p = g_ServerMgr->m_SectorContent.GetSectorData(sector_id);
	if (p) 
	{
		sector_name = p->name;
	}

    return (sector_name);
}

long SectorManager::GetSectorIDFromName(char *sector_name)
{
    long sector_id = 0;
	//loop through all the sectors
    SectorData *p = g_ServerMgr->m_SectorContent.GetSectorData(sector_name);
	if (p) 
	{
		sector_id = p->sector_id;
	}

    return (sector_id);
}

void SectorManager::RechargeReactor(Player *player)
{
	//command queue
    player->RechargeReactor();
}

void SectorManager::RechargeShield(Player *player)
{
    player->RechargeShield();
}

void SectorManager::ForceLogout(Player *player)
{
    player->ForceLogout();
}

void SectorManager::BuffTimeout(Player *player)
{
	// TODO: This needs to be reworked

	/*
	if (player->connection)
	{
		for(int x=0;x<16;x++)
		{
			// Remove expired buffs
			if (player->player->m_AuxShip.m_BuffArray.Buffs[x].BuffRemovalTime < (signed) GetNet7TickCount() 
                && !player->player->m_AuxShip.m_BuffArray.Buffs[x].IsPermanent)
			{
				// Remove effect
				//player->player->SendResponse(ENB_OPCODE_000F_REMOVE_EFFECT, (unsigned char *)&player->player->m_AuxShip.m_BuffArray.Buffs[x].Elements[0].Magnitude, sizeof(int));
				// Send to sector to remove effect
				m_ServerMgr->m_PlayerMgr.SendToSector(player, ENB_OPCODE_000F_REMOVE_EFFECT, 
                    (unsigned char *)&player->player->m_AuxShip.m_BuffArray.Buffs[x].Elements[0].Magnitude, sizeof(int));
				// Remove buff from memory
				memset(&player->player->m_AuxShip.m_BuffArray.Buffs[x],0,sizeof(player->player->m_AuxShip.m_BuffArray.Buffs[x]));
			}
		}
	}
	*/
	//Combat Trance Timeout?  This will return immediately if the player does not have combat trance.
	player->RefreshCombatTrance();
}

void SectorManager::InitialiseSector()
{
	m_SectorThreadRunning = true;
	m_StartSlotTick = GetNet7TickCount() / 100; 
	
	m_check_tick = (u32)m_SectorID;

	if (m_ObjectMgr == (0)) return;

	//TODO: don't need an object manager for stations, but we do need an event thread.
    
	if (m_SectorID < 9999)
	{
		m_ObjectMgr->SectorSetup();
		m_ObjectMgr->SetSectorManager(this);
	}
}

// each sector is now on its own thread
void SectorManager::RunSectorEventThread()
{
	unsigned long current_tick;
	unsigned long starting_tick;
	unsigned long last_term_tick = GetNet7TickCount();
	long sleep_time;

	RefreshJobs();

	while (!g_ServerShutdown)
	{
		current_tick = GetNet7TickCount();
		starting_tick = current_tick;

		if (m_SectorID < 9999) //only MOBs and object effects for space & planet sectors
		{
			//no mutex needed here
			ProcessMOBs(current_tick, m_check_tick, true);
		}

		//work out where we are along the timeslot system - see if any extra slots need calling.
		/*
		//This is left in here for any future debugging
		float expected = (float)(m_EventSlotIndex*100);
		float actual = (float)(current_tick - m_EndSlotTick);
		int calling = (int)((actual - expected)/100.0f);

		LogMessage("Expected time slot diff: %.4f, actual %.4f, call %d slots\n", expected, actual, calling);*/

		//CATCH_UP_LIMIT limits how fast events will happen post lag
		//DONT SET TO ZERO or below
		// Setting 1 will disable catching up and lead to increased weapon fire times 
		//and other lag issues
		// Setting 2 or higher is recommended

		unsigned long CATCH_UP_LIMIT = 4;
		current_tick = current_tick / 100;
		unsigned long cycles = 0;
		//Large Scale Lagspike detection to reduce deadly death speedup
		//if we are more then 10 seconds behind then make us only one second behind
		// this is adjustable adding or subtracting one increases and decreased by 0.1 seconds
		if( (m_StartSlotTick + 100) < current_tick)
		{
			m_StartSlotTick = current_tick - 10;
		}

		//If we haven't caught up to current and we haven't hit the limit then loop
		while( (m_StartSlotTick < current_tick) && (cycles < CATCH_UP_LIMIT) )
		{
			CallSlotEvents(m_EventSlotIndex);
			//if it is a slot to call long term events then call them
			if (m_EventSlotIndex % 10 == 0)
			{
				CallLongTermEvents(m_EventSlotIndex);
			}

			// increase and check m_EventSlotIndex
			m_EventSlotIndex++;
			if (m_EventSlotIndex >= (TIMESLOT_DURATION*10))
			{
				m_EventSlotIndex = 0;
			}

			//increase the old sector tick to show match the time 
			// to where we should be in event slots
			m_StartSlotTick++;

			//increase cycles for our limiter check
			cycles++;
		}

		m_check_tick++;

		// OK now call the player movements for all players in this sector
		Player *p = (0);
		// NB does not matter if player list changes while this occurs
		// bitfields don't crash like STL.
		// no mutex required.
		while (g_PlayerMgr->GetNextPlayerOnList(p, m_PlayerList))
		{
			if (p)
			{
				g_PlayerMgr->RunPlayerUpdate(p);
			}
			g_PlayerMgr->SendUDPPlayerOpcodes(p); //this can terminate the player, so may change m_PlayerList
		}

		if (m_SectorID < 9999) //only MOBs and object effects for space & planet sectors
		{
			//now perform any object admin, such as list removals, resource respawns etc.
			if (m_ObjectMgr) m_ObjectMgr->PerformObjectAdmin();
		}
		else
		{
			if (m_JobTerminalLevel > 0)
			{
				//see if job terminal needs refreshing
				if ((last_term_tick + 5*60*1000) < starting_tick)
				{
					last_term_tick = starting_tick;
					RefreshJobs();
				}
			}
		}

		//run thread every 100ms
		//TODO: we should do this with timed interrupts rather than sleeps
		sleep_time = (long)(100 - (GetNet7TickCount() - starting_tick)); 
		if (sleep_time < 1) sleep_time = 1;
		if (sleep_time > 100) sleep_time = 100;
		usleep(sleep_time * 1000);
	}
}

void SectorManager::ProcessMOBs(u32 current_tick, long check_tick, bool handle_attacks)
{
	u32 index = 0;

    //update MOBs
    if (check_tick % 5 == 0) //update every 500ms
    {
		ObjectList *olist = m_ObjectMgr->GetMOBList();
		u32 mob_size = olist->size();

		Object *obj;

		while (index < mob_size && mob_size == olist->size())
		{
			obj = (*olist)[index];
			
            switch (obj->ObjectType())
            {
            case OT_MOB:
                ((MOB*)obj)->UpdateMOB(current_tick, handle_attacks, check_tick);
                break;

            case OT_MOBSPAWN:
                index += ((MOBSpawn*)obj)->UpdateSpawn(current_tick, handle_attacks);
                break;

            default:
				LogMessage("Unknown object in MOB list, GameID: '%d'\n",obj->GameID());
				g_ServerMgr->m_PlayerMgr.ErrorBroadcast("Unknown object in MOB list, GameID: '%d'\n",obj->GameID());
                break;
            }
			index++;
        }
    }
}

void SectorManager::MOBBlastDamage(Player *p, float *position, float damage)
{
	u32 index = 0;
	ObjectList *olist = m_ObjectMgr->GetMOBList();
	u32 mob_size = olist->size();

	Object *obj;

	while (index < mob_size && mob_size == olist->size())
	{
		obj = (*olist)[index];
		if (obj->ObjectType()== OT_MOB)
		{
			((MOB*)obj)->BlastDamage(p, position, damage);
		}
		index++;
	}
}

void SectorManager::SlaySectorMobs(Player *dev)
{
	m_Mutex.Lock();
	u32 index = 0;
	ObjectList *olist = m_ObjectMgr->GetMOBList();
	u32 mob_size = olist->size();

	Object *obj;

	while (index < mob_size && mob_size == olist->size())
	{
		obj = (*olist)[index];
		if (obj->ObjectType()== OT_MOB)
			obj->DamageObject(dev->GameID(), DAMAGE_EXPLOSIVE, 1e7f, 0);
		index++;
	}
	m_Mutex.Unlock();
}

bool SectorManager::CallSlotEvents(long index)
{
    bool called_event = false;
	long i;

	m_Mutex.Lock();
	u8 max = m_EventSlotsIndex[index];
	
    //traverse this node list and call all the nodes
	
	for (i = 0; i < max; ++i)
	{
	
		if (m_EventSlots[index][i] != 0) //node may have been force removed by RemoveNode
		{
			MakeTimedCall(m_EventSlots[index][i]);
			called_event = true;
		}
		
	}

	m_EventSlotsIndex[index] = 0;
 	m_Mutex.Unlock();

    return called_event;
}

bool SectorManager::CallLongTermEvents(long index)
{
	//printf("SectorManager::CallLongTermEvents locking mutex\n");
	m_Mutex.Lock();
    bool called_event = false;
	u32 tick = GetNet7TickCount();

	//now check for long term time nodes, check every 5 seconds, and only pull one off at a time
	if (index % 50 == 0)
	{
		for (int i = 0; i < LONG_TERM_NODES; i++)
		{
			
			if (m_CoarseEventSlots[i] != 0 && m_CoarseEventSlots[i]->event_time < tick)
			{
				MakeTimedCall(m_CoarseEventSlots[i]);
				m_CoarseEventSlots[i] = 0;
				called_event = true;
			}
			
		}
	}
	//printf("SectorManager::CallLongTermEvents unlocking mutex\n");
	m_Mutex.Unlock();
	return called_event;
}

void SectorManager::AddTimedCallPNode(TimeNode *node, unsigned long time_offset)
{
	SlotTimedCall(node, time_offset);
}

void SectorManager::AddTimedCallPNode(TimeNode *this_node, broadcast_function func, long time_offset, Object *obj, long i1, long i2, long i3, long i4, char *ch, float a)
{
	//store params in node	
	//printf("SectorManager::AddTimedCallPNode locking mutex\n");
    m_Mutex.Lock();

	memset ((void*)this_node, 0, sizeof(TimeNode));

	this_node->func = func;
	this_node->obj = obj;
	this_node->i1 = i1;
	this_node->i2 = i2;
	this_node->i3 = i3;
	this_node->i4 = i4;
	if (ch != 0)
	{
        this_node->ch = g_StringMgr->GetStr(ch);
	}
	else
	{
		this_node->ch = 0;
	}
	this_node->a = a;
	//printf("SectorManager::AddTimedCallPNode unlocking mutex\n");
	m_Mutex.Unlock();	

	SlotTimedCall(this_node, time_offset);
}

TimeNode * SectorManager::AddTimedCall(CMob *player, broadcast_function func, long time_offset, Object *obj, long i1, long i2, long i3, long i4, char *ch, float a)
{
	//store params in node
	//printf("SectorManager::AddTimedCall locking mutex\n");
	m_Mutex.Lock();
	
	TimeNode * this_node = g_GlobMemMgr->GetBroadcastNodeSlot();

	memset ((void*)this_node, 0, sizeof(TimeNode));

	this_node->func = func;
	this_node->obj = obj;
	this_node->i1 = i1;
	this_node->i2 = i2;
	this_node->i3 = i3;
	this_node->i4 = i4;
	if (ch != 0)
	{
        this_node->ch = g_StringMgr->GetStr(ch);
	}
	else
	{
		this_node->ch = 0;
	}
	this_node->a = a;

	if (player == 0)
	{
		this_node->player_id = SECTOR_SERVER_CONNECTIONLESS_NODE;
	}
	else
	{
		this_node->player_id = player->GameID();
	}
	//printf("SectorManager::AddTimedCall unlocking mutex\n");
	m_Mutex.Unlock();

	SlotTimedCall(this_node, time_offset);
	
	
	return (this_node);
}

long SectorManager::GetSlotIndex(long time_offset)
{
    long index,startindex;

    //work out required number of slots ahead we need
    long slots = ((time_offset+50) / 100);

    if (slots < 2) slots = 2; //Ensure we never crash into CallSlotEvents call

    startindex = m_EventSlotIndex;
	index = (m_EventSlotIndex + slots) % (TIMESLOT_DURATION * 10);

	//advance until we find space for this call in the slots
	while (m_EventSlotsIndex[index] >= 100)
	{
		if (++index >= (TIMESLOT_DURATION * 10)) index = 0;
		// something (lag perhaps?) can cause every timeslot to fill, then it gets stuck in this loop
		if (index == startindex)
			return -1;
	}

    return index;
}

void SectorManager::SlotTimedCall(TimeNode *this_node, unsigned long time_offset)
{
	//printf("SectorManager::SlotTimedCall locking mutex\n");
	m_Mutex.Lock();
	if (m_EventSlots)
	{
		this_node->event_time = time_offset + GetNet7TickCount();
		long index = -1;

		if (time_offset < (TIMESLOT_DURATION * 1000)) //use the slotted system
		{
			//slot call into appropriate vector slot
			index = GetSlotIndex(time_offset);
			if (index != -1)
			{
				long slot_index = m_EventSlotsIndex[index];		
				this_node->EventIndex = (short)index;
				m_EventSlots[index][slot_index] = this_node;
				m_EventSlotsIndex[index]++;
			}
		}
		//use long term slot system
		if (index == -1)
		{
			this_node->EventIndex = -1;
			long slot_index = -1;
			//find an empty long term time slot
			for (int i = 0; i < LONG_TERM_NODES; i++)
			{
				if (m_CoarseEventSlots[i] == 0)
				{
					slot_index = i;
					break;
				}
			}

			if (slot_index == -1) // didn't find a slot, force process next slot to be used
			{
				slot_index = 0;
				for (int i = 0; i < LONG_TERM_NODES; i++)
				{
					if (m_CoarseEventSlots[i] != 0 && (m_CoarseEventSlots[i]->event_time < m_CoarseEventSlots[slot_index]->event_time) )
					{
						slot_index = i;
					}
				}
				//force call slot_index node
				if (m_CoarseEventSlots[slot_index])
				{
					MakeTimedCall(m_CoarseEventSlots[slot_index]);
				}
			}

			//by hook or by crook we have a slot
			m_CoarseEventSlots[slot_index] = this_node;
		}
	}
	else
	{
		LogMessage("*** FATAL ERROR: TimeSlots not available in sector %d %s\n", this->m_SectorNumber, this->m_SectorName);
	}
	//printf("SectorManager::SlotTimedCall unlocking mutex\n");
	m_Mutex.Unlock();
}

void SectorManager::MakeTimedCall(TimeNode *this_node)
{
	MOB *mob = NULL;
    TimeNode node;
	
    memcpy(&node, this_node, sizeof(TimeNode));
	memset(this_node, 0, sizeof(TimeNode)); //erase node contents.
    m_Mutex.Unlock();
	int ShutdownTimes[] = { 30, 30, 30, 30, 30, 15, 10, 5 };
	int RealShutdownTimes[] = { 210, 180, 150, 120, 60, 30, 15, 5 };

	Player *p = g_PlayerMgr->GetPlayer(node.player_id);

	//this should be safe now with UDP.
	/*if (p && p->GetLoginStage() != LAST_LOGIN_STAGE)
	{
		//LogMessage("!!! Player pointer invalidated for %s because a timed event %d occurred outside of fully logged in status\n", p->Name(),node.func);
		//p = (0); //invalidate p if player not logged in.
	}*/

    if (node.player_id != NODE_NO_LONGER_NEEDED)
	{
		//printf("MakeTimedCall event = %d\n",node.func);
		switch(node.func)
		{
		case B_WARP_BROADCAST:
            if (p) p->StartWarp();
			break;
		case B_DESTROY_RESOURCE:
			m_ObjectMgr->DestroyObject(node.obj, 0, node.i1);
			break;
		case B_WARP_RESET:
			if (p) p->FinalWarpReset();
			break;
		case B_WARP_TERMINATE:
            if (p) p->TerminateWarp();
			break;
		case B_MINE_RESOURCE://(time_to_drain, obj, stack_val, slot);
            if (p) p->PullOreFromResource(node.obj, node.i1, node.i2, node.i3, node.i4);
			break;
		case B_COLLECT_RESOURCE:
            if (p) p->TakeOreOnboard(node.obj, node.i3, node.i4 != 0);
			break;
		case B_MOB_DAMAGE:
        case B_SHIP_DAMAGE:
			if (node.obj && (node.obj->ObjectType() == OT_MOB || node.obj->ObjectType() == OT_PLAYER) )
			{
				node.obj->DamageObject(node.i1, node.i2, node.a, node.i3);
			}
            break;
		case B_RECHARGE_REACTOR:
			if (p) RechargeReactor(p);
			break;
        case B_RECHARGE_SHIELD:
            if (p) RechargeShield(p);
            break;
		case B_FORCE_LOGOUT:
			if (p) ForceLogout(p);
			break;
		case B_BUFF_TIMEOUT:
			if (p) BuffTimeout(p);
			break;
        case B_CAMERA_CONTROL:
            if (p) p->SendCameraControl(node.i1, node.i2);
            break;
		case B_LOCAL_GATE:
			if (p) p->HandleLocalGate(node.i1, node.i2);
			break;
		case B_ARRIVE_AT_LOCAL_GATE:
			if (p) p->ArriveAtLocalGate(node.i1);
			break;
        case B_MANUFACTURE_ACTION:
            if (p) p->ManufactureTimedReturn(node.i1);
            break;
        case B_ITEM_INSTALL:
			if (p && node.i1 >= 0 && node.i1 < 20) p->m_Equip[node.i1].FinishInstall(p,node.i1);
            break;
        case B_ITEM_COOLDOWN:
            if (p && node.i1 >= 0 && node.i1 < 20) p->m_Equip[node.i1].CoolDown();
            break;
        case B_TEST_MESSAGE:
            if (p) p->SendVaMessage("Test Message time: %d [%d]",GetNet7TickCount() - node.i2, node.i1);
            break;
		case B_ABILITY_REMOVE:
			if (node.obj && (node.obj->ObjectType() == OT_MOB || node.obj->ObjectType() == OT_PLAYER))
			{
				((CMob *)node.obj)->AbilityRemove(node.i1, node.i2);
			}
			break;
		case B_REMOVE_BUFF:
			if (node.obj && (node.obj->ObjectType() == OT_MOB || node.obj->ObjectType() == OT_PLAYER))
			{
				((CMob *)node.obj)->m_Buffs.Update(node.i1);
			}
			break;
/*
		case B_SHOOT_AMMO:
			if (p && node.i1 >= 0 && node.i1 < 20) p->m_Equip[node.i1].ShootAmmo(node.i2);
			break;
*/
		case B_SERVER_SHUTDOWN:
			char Msg[64];
			sprintf_s(Msg, sizeof(Msg), 
				"Server shutdown in %d seconds! Please logout to save properly.", RealShutdownTimes[m_ShutDownMark]);
			g_ServerMgr->m_PlayerMgr.GlobalAdminMessage(Msg);
			g_ServerMgr->m_PlayerMgr.GlobalMessage(Msg);
			AddTimedCall(0, B_SERVER_SHUTDOWN, ShutdownTimes[m_ShutDownMark] * 1000, NULL);
			m_ShutDownMark++;
			if (m_ShutDownMark > 7)
			{
				// Shutdown the server
				g_ServerShutdown = true;
				// save all player's data and force terminate them
				g_PlayerMgr->TerminateAllPlayers();
			}
			break;
		case B_DESTROY_HUSK:
			if (node.obj) node.obj->DestroyHusk();
			break;
		case B_DESTROY_OBJECT:
			if (node.obj) node.obj->DestroyStaticObject();
			break;
		case B_REMOVE_EFFECT:
			if(p && node.i1) p->m_Effects.RemoveEffect((int)node.i1);
			break;
		case B_REMOVE_OBJECT_EFFECT:
			if(p && node.i1) p->RemoveEffectRL((int)node.i1);
			break;
		default:
			break;
		}
	}
	m_Mutex.Lock();
}

void SectorManager::RemoveTimedCall(TimeNode *node, bool force)
{
	//printf("SectorManager::RemoveTimedCall locking mutex\n");
	m_Mutex.Lock();
	if (force)
	{
		if (node->EventIndex == -1)
		{
			for (int i = 0; i < LONG_TERM_NODES; i++)
			{
				if (m_CoarseEventSlots[i] == node)
				{
					m_CoarseEventSlots[i] = 0;
					break;
				}
			}
		}
		else
		{
			for (long i = 0; i < m_EventSlotsIndex[node->EventIndex]; ++i) //set the node ptr to zero
			{
				if (m_EventSlots[node->EventIndex][i] == node)
				{
					m_EventSlots[node->EventIndex][i] = 0;
					break;
				}
			}
		}
	}

    memset(node, 0, sizeof(TimeNode)); //erase node contents.
	//printf("SectorManager::AddTimedCall unlocking mutex\n");
    m_Mutex.Unlock();
}

// Remove all events from player (done at logout)
void SectorManager::RemovePlayerEvents(Player *player)
{
	if(player == NULL)
	{
		return;
	}
	//printf("SectorManager::RemovePlayerEvents locking mutex\n");
	m_Mutex.Lock();
	long us = player->GameID();
	for (int i = 0; i < LONG_TERM_NODES; i++)
	{
		if (m_CoarseEventSlots[i] != NULL && m_CoarseEventSlots[i]->player_id == us)
		{
			memset(m_CoarseEventSlots[i], 0, sizeof(TimeNode));
			m_CoarseEventSlots[i] = NULL;
		}
	}
	for(int i = 0; i < TIMESLOT_DURATION*10; i++)
	{
		for(int j = 0 ; j < 100; j ++)
		{
			if(m_EventSlots[i][j] != NULL && m_EventSlots[i][j]->player_id == us && m_EventSlots[i][j]->func != B_SERVER_SHUTDOWN)
			{
				memset(m_EventSlots[i][j], 0, sizeof(TimeNode));
				m_EventSlots[i][j] = (NULL);
			}
		}
	}
	//printf("SectorManager::RemovePlayerEvents locking mutex\n");
	m_Mutex.Unlock();
}

void SectorManager::RemovePlayerInstallEvents(Player *player)
{
	if(player == NULL)
	{
		return;
	}
	//printf("SectorManager::RemovePlayerInstallEvents locking mutex\n");
	m_Mutex.Lock();
	long us = player->GameID();
	for (int i = 0; i < LONG_TERM_NODES; i++)
	{
		if (m_CoarseEventSlots[i] != NULL && m_CoarseEventSlots[i]->player_id == us && m_CoarseEventSlots[i]->func ==B_ITEM_INSTALL)
		{
			memset(m_CoarseEventSlots[i], 0, sizeof(TimeNode));
			m_CoarseEventSlots[i] = NULL;
		}
	}
	for(int i = 0; i < TIMESLOT_DURATION*10; i++)
	{
		for(int j = 0 ; j < 100; j ++)
		{
			if(m_EventSlots[i][j] != NULL && m_EventSlots[i][j]->player_id == us && m_EventSlots[i][j]->func ==B_ITEM_INSTALL)
			{
				memset(m_EventSlots[i][j], 0, sizeof(TimeNode));
				m_EventSlots[i][j] = (NULL);
				LogMessage("-->> Grim Hack! Player event force removed from event stack %s %s\n", player->Name(), player->Active() ? "active" : "non-active" );
			}
		}
	}
	//printf("SectorManager::RemovePlayerInstallEvents locking mutex\n");
	m_Mutex.Unlock();
}

long SectorManager::GetOccupancy()
{
	long count = 0;
	Player *p = (0);
	while (g_PlayerMgr->GetNextPlayerOnList(p, m_PlayerList))
	{
		count++;
	}

	return count;
}

// Entry point handed to pthread_create for the per-sector event thread.
void *SectorManager::RunEventThreadAPI(void *Param)
{
	SectorManager* p_this = reinterpret_cast<SectorManager*>( Param );
	p_this->RunSectorEventThread();
	return NULL;
}

void SectorManager::RefreshJobs()
{
	//simply fill in the job list we've been given
	long i;
	for (i = 0; i < m_JobListCount; i++)
	{
		//first choose a category
		long mission_id = 0;
		u8 category;
		MissionTree *job = (0);
		long bailout = 0;  //prevent possibility of infinite loop
		while (mission_id == 0)
		{
			category = rand()%3 + 1; //1 to 3
			if (g_ServerMgr->JobCount(category) > 0)
			{
				//randomly choose a job from this category
				long selection = rand()%(g_ServerMgr->JobCount(category));
				job = g_ServerMgr->m_JobMgr->SelectJob(selection, category);
				if (job)
				{
					mission_id = job->MissionID;
					break;
				}
			}
			bailout++;
			if (bailout > 100) break;
		} ;

		if (mission_id > 0 && job)
		{
			JobNode *jn = m_JobList[i];
			//g_ServerMgr->m_JobMgr->SetupJobNode(jn, job, m_JobTerminalLevel, m_SectorID);
			jn->ID = m_JobListID;
			jn->Category = job->Job_Category; //g_ServerMgr->m_JobMgr->GetJobData(type, jn->Item, jn->Obj, m_JobTerminalLevel, m_SectorID);
			jn->Obj = g_ServerMgr->m_JobMgr->GetJobObject(m_JobTerminalLevel, m_SectorID);
			if (job->Job_Category == 1) 
			{
				jn->Mob = g_ServerMgr->m_JobMgr->GetMOB(m_JobTerminalLevel);
			}
			else
			{
				jn->Mob = 0;
			}
			jn->Level = m_JobTerminalLevel;
			jn->Sponsor = rand()%22 + 1;//TODO: use this station's or system's owner as sponsor, but pick random for now
			jn->available = true;
			jn->MissionID = mission_id;
			m_JobListID++;
		}
	}
}

int SectorManager::GetJobList(u8 *buffer)
{
	int index = 0;
	int index_dummy = 0;
	u8 *ptr = buffer;
	char str_buffer[64];
	long jobs = 0;
	long i;

	if (m_JobListCount == 0) return 0; // no jobs here

	AddData(ptr, m_JobListCount, index);  //placeholder job count

	for (i = 0; i < m_JobListCount; i++)
	{
		JobNode *jn = m_JobList[i];
		if (jn->available)
		{
			AddData(ptr, (long)jn->ID, index);  //job id (not shown)
			AddData(ptr, (long)jn->Category-1, index);  //category (Combat = 0, Explore, Trade)
			AddData(ptr, (long)0, index);  //?
			AddData(ptr, (long)jn->Level, index); //Level
			AddDataSN(ptr, g_ServerMgr->m_JobMgr->GetJobTitle(jn->MissionID), index);  //title
			FactionData * fData = g_ServerMgr->m_FactionData.GetFactionData(jn->Sponsor);
			AddDataSN(ptr, fData->m_name, index);			//sponsor
			long XP_reward = (jn->Level * 50);
			snprintf(str_buffer, 64, "%d XP", XP_reward);
			AddDataSN(ptr, str_buffer, index);			//reward
			jobs++;
		}
	}

	AddData(ptr, jobs, index_dummy);  //actual job count

	return index;
}

int SectorManager::GetJobDescription(u8 *buffer, long job_id)
{
	int index = 0;
	u8 *ptr = buffer;
	long i;

	if (m_JobListCount == 0) return 0; // no jobs here

	AddData(ptr, job_id, index);

	//find job
	for (i = 0; i < m_JobListCount; i++)
	{
		JobNode *jn = m_JobList[i];
		if (jn->ID == job_id)
		{
			if (jn->available)
			{
				AddData(ptr, (char)1, index);
			}
			else
			{
				AddData(ptr, (char)0, index);
			}

			//Title of job
			AddDataSN(ptr, g_ServerMgr->m_JobMgr->GetJobTitle(jn->MissionID), index);  //title
			//job description
			index += g_ServerMgr->m_JobMgr->GetJobDescription((ptr + index), jn);

			//null terminate
			*(ptr + index) = (0);
			index++;
			break;
		}
	}

	if (index == 0)
	{
		//the job has expired
		AddData(ptr, (char)0, index);
		AddDataSN(ptr, "Job Expired", index);
		AddDataSN(ptr, "Job Expired", index);
	}

	return index;
}

bool SectorManager::AwardJob(Player *p, long job_id)
{
	long i;
	bool success = false;
	//first get job data
	for (i = 0; i < m_JobListCount; i++)
	{
		JobNode *jn = m_JobList[i];
		if (jn->ID == job_id)
		{
			if (jn->available)
			{
				//award job
				p->AcceptJob(jn);
				jn->available = false;
				success = true;
				break;
			}
		}
	}

	return success;
}