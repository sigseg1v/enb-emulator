// PlayerClass.cpp
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

#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"
#include "Opcodes.h"
#include "PacketMethods.h"
#include "StaticData.h"

//#define MOVEMENT_DEBUG

Player::Player(long object_id) : CMob (object_id)
{
	// This is called once, ordinary variables should go in ResetPlayer, which can be called more than once
    m_Type = OT_PLAYER;
    m_Remove = false;
	m_RecvQueue = new MessageQueue("Player receive", g_ServerMgr->GetMessageBuffer());
	m_RSendQueue= new MessageQueue("Player re-send", g_ServerMgr->GetReSendCBuffer());
	m_UDPQueue  = new MessageQueue("Player UDP Queue", g_ServerMgr->GetUDPCBuffer(), 800, true);
}

Player::Player(void) : CMob () //default constructor so we can create an array of Player
{
	// This is called once, ordinary variables should go in ResetPlayer, which can be called more than once
    m_Type = OT_PLAYER;
    m_Remove = false;
	m_RecvQueue = new MessageQueue("Player receive", g_ServerMgr->GetMessageBuffer());
	m_RSendQueue= new MessageQueue("Player re-send", g_ServerMgr->GetReSendCBuffer());
	m_UDPQueue  = new MessageQueue("Player UDP Queue", g_ServerMgr->GetUDPCBuffer(), 800, true);
}

Player::~Player()
{
    delete m_RecvQueue;
	delete m_RSendQueue;
	delete m_UDPQueue;
}

void Player::ResetPlayer()
{
	ResetCommon();
	SetLastAccessTime(0);
    m_GroupID = -1;
	m_GuildID = -1;
	m_AcceptedGroupInvite = false;
    m_MVAS_index = -1;
    BlankPositionInfo();
    m_Remove = false;
    m_Room = 0;
    m_Oldroom = 0;
    m_LastRead = 0;
    m_ReceivedMVAS = false;
    m_MovementID = 0;
    m_Group = 0;
    m_Active = false;
    m_Accelerating = false;
    m_Velocity = 0;
    m_YInput = 0;
    m_ZInput = 0;
    m_DebugPlayer = false;
	m_ToBeRemoved = false;
    m_CameraSignal = -1;
    m_AccountUsername = g_StringMgr->NullStr();
    m_FirstLogin = true;
    m_Hijackee = (0);
    m_WarpDrive = false;
	m_WarpCharing = false;
    m_DistToNav = 0.0f;
    m_OverrunCount = 0;
    m_NumFormulas = 0;
    m_MVAS_frequency = 10;
    m_FollowObject = false;
    m_Prospecting = false;
	m_Looting = false;
    m_AttackMusic = false;
    m_AttackerCount = 0;
    m_MyDebugPlayer = (0);
    m_MissionFlags = 0;
	m_MoreDestination = 0;
    m_LoadFlags = 0;
    m_ObjectRadius = 245.0f;
    m_UDPConnection = (0);
    m_FromSectorID = 0;
    m_LoginFailed = false;
    m_CurrentTalkTree = (0);
	m_CurrentStation = (0);
	m_CurrentNPC = (0);
	m_ExposeDecos = false;
	m_WeaponSlots = 0;
	m_DeviceSlots = 0;
	m_Confirmation = 0;
	m_Confirmation_PlayerID = 0;
	m_Confirmation_Ability = 0;
	m_RegisteredSectorID = 0;
	m_WeaponsPerTick = 0;
	m_BroadcastPositionTick = 0;
	m_OpcodeResends = 0;
	m_LoginStage = -1;
	m_PushMissionID = 0;
	m_PushMissionUID = 0;
	m_Faction_Override = false;
	m_CombatImmunity = false;
	m_ScanInvisible = false;
	m_TagInvisible = false;
	m_SpeedDoubled = false;
	m_SpeedHalved = false;
	m_UpdateSent = false;
	m_LastReactorChange = 0;
	m_Gating = false;
	m_SelfDestructed = false;
	m_ItemsWaiting = false;
	m_ReplacementShipAsset = 0;
	m_ReplacementShipScale = 0;
	m_Wormholed = false;
	m_DebugMissions = false;
	m_MissionStageDebrief = false;
	m_MissionDebriefed = false;
	m_SoundWarningSetting = 2; //default 25% and criticals
	m_IncapXPDebt = 0;
	m_LogoutTime = 0;
	m_PacketSplitRemaining = 0;
	m_PeriodicCacheSize = PERIODIC_CACHE_SEND_SIZE;
	m_ResendIndex = 0;
	m_ResendTimer = 0;
	m_ItemSendType = (item_send)0;
	m_ItemSendIndex = 0;
	m_LastSort = 0;
	m_PacketSequenceNum = 0;
	m_NoPlayerUpdate = false;

	m_GWell = -1;
	m_Radiation = -1;
	m_EnvStats[0] = 0;
	m_EnvStats[1] = 0;
	m_EnvEffects[0] = 0;

	memset(&m_PDAFactionID, 0, sizeof(m_PDAFactionID));

	m_UDPQueue->ResetQueue();
	m_RecvQueue->ResetQueue();

    memset(m_Verbs, 0, sizeof(m_Verbs));
    memset(m_NavsExplored, 0, sizeof(m_NavsExplored));
    memset(m_NavsExposed, 0, sizeof(m_NavsExposed));
    memset(m_FoundAllSectorNavs, 0, sizeof(m_FoundAllSectorNavs));
	memset(m_ChannelSubscription, 0, sizeof(m_ChannelSubscription));
	m_ChannelSubscription[g_PlayerMgr->GetChannelFromName("Broadcast")] = true;	// temporary (work out how to get the tickbox setting from the client)
	m_ChannelSubscription[g_PlayerMgr->GetChannelFromName("Local")] = true;		// temporary (work out how to get the tickbox setting from the client)
	m_StatusToFriendsOnly = false;
	m_TellsFromFriendsOnly = false;

    memset(&m_ProspectBeamNode, 0, sizeof(TimeNode)); //erase node contents.
    memset(&m_ProspectTractorNode, 0, sizeof(TimeNode)); //erase node contents.

	memset(&m_NavRange_1, 0, sizeof(m_NavRange_1));
	memset(&m_NavRange_2, 0, sizeof(m_NavRange_2));
	memset(&m_NavRange_3, 0, sizeof(m_NavRange_3));
	memset(&m_NavEffects, 0, sizeof(m_NavEffects));

	memset(&m_CompletedMissions, 0, sizeof(m_CompletedMissions));
	m_Arrival_Flag = false;

    m_DockCoords[0] = m_DockCoords[1] = m_DockCoords[2] = 0.0f;
    m_DockHeading[0] = m_DockHeading[1] = m_DockHeading[2] = 0.0f;

	// erase last players recipes!
	m_ManuRecipes.clear();
	m_CurManuItem = NULL;

	// Reset abilities
	ResetAbilities();

	//reset damage shields list
	ClearDamageShields(true);

	// Setup Stats
	m_Stats.Init(this);
	m_Effects.Init(this);
	m_Buffs.Init(this);

	// Zero out data
	memset(m_BaseID, 0, sizeof(m_BaseID));
	memset(&m_Database, 0, sizeof(m_Database));
	memset(m_MissionNodes, 0, sizeof(m_MissionNodes));

	PlayerIndex()->Reset();
	PlayerIndex()->ClearFlags();
	ShipIndex()->Reset();
	ShipIndex()->ClearFlags();
}

//Do not use this to perform actions for login,
//Use 'SectorLogin()' below
void Player::SectorReset()
{
    m_EnergyRecharge = 0;
    m_Manufacturing = false;
    BlankPositionInfo();
    m_Room = 0;
    m_Oldroom = 0;
    m_HavePosition = false;
    m_Orient = 0.0f;
    m_LastRead = 0;
    m_MovementID = 5;
    m_WarpDrive = false;
    m_Accelerating = false;
    m_Velocity = 0;
    m_YInput = 0;
    m_ZInput = 0;
    m_SetUpdate = 0;
    m_CameraSignal = 0;
    m_CameraID = 0;
    m_DistToNav = 0.0f;
    m_OverrunCount = 0;
    m_FollowObject = false;
    m_Prospecting = false;
	m_Looting = false;
    m_AttackMusic = false;
    m_AttackerCount = 0;
    m_LastVelocity = 0.0f;
    m_WarpTime = 0;
    m_NearestNav = (0);
	m_FloatingOre_contents = (0);
	m_FloatingOre_stack = 0;
    m_MissionAcceptance = false;
    m_ProspectBeam = false;
    m_TractorBeam = false;
    m_WarpBroadcastTime = 0;
    RemoveProspectNodes();
    m_AttacksThisTick = 0;
    m_LogDockCoords = false;
    m_StargateDestination = 0;
    m_ActionResponseReceived = false;
    m_ProspectWindow = false;
    m_TradeID = -1;
    m_TradeConfirm = false;
    m_CurrentDecoObj = (0);
    m_TradeWindow = false;
    m_StarbaseTargetID = 0;
    m_BeaconRequest = 0;
    m_AuxWaiting = true;
    m_NavCommence = false;
    m_PacketSequenceNum = 0;
    m_CurrentTalkTree = (0);
	m_WeaponsPerTick = 0;
	m_SentDockPos = false;
	m_SendDockPos = false;
	m_IncapAvatarSent = false;
	m_MissionStageDebrief = false;
	m_MissionDebriefed = false;
	m_PushMissionID = 0;
	m_ResendIndex = 0;
	m_ResendTimer = 0;
	m_LastSort = 0;
	m_NoPlayerUpdate = false;

#ifdef TEST_CREATE
	m_CheckList.clear();
#endif

	m_GWell = -1;
	m_Radiation = -1;
	m_EnvStats[0] = 0;
	m_EnvStats[1] = 0;
	m_EnvEffects[0] = 0;
    
    memset (m_ScratchBuffer, 0, MAXIMUM_PACKET_CACHE);

    memset(m_ObjRangeList, 0, MAX_OBJS_PER_SECTOR/8);
    memset(m_ResourceSendList, 0, MAX_OBJS_PER_SECTOR/8);

	memset (&m_RangeList, 0, sizeof(m_RangeList));

	memset(&m_NavRange_1, 0, sizeof(m_NavRange_1));
	memset(&m_NavRange_2, 0, sizeof(m_NavRange_2));
	memset(&m_NavRange_3, 0, sizeof(m_NavRange_3));

	memset(&m_ResendQueue, 0, sizeof(m_ResendQueue));
	m_Arrival_Flag = false;

	m_ObjectRadius = 122.5f * Scale(); // reset player radius every sector login
}

//moving the player name to a buffer stops the gradual leak of the string manager.
void Player::SetName(char *name)
{
	if (name)
	{
		strncpy(m_NameBuffer, name, 20);
		m_Name = m_NameBuffer;
	}
}

void Player::FirstLogin()
{
    if (m_FirstLogin) //this only executes the first time it is run (per player).
    {
		//first load all the avatar & ship info - this used to be done in ClientToGlobalServer.cpp, ::HandleGlobalTicketRequest.
		//but now because the server first finds out about the player from Net7SSL, when they click on the char to login	
		//we need to load here. This is good because now the time taken to load in the avatar is on the login thread, not the TCP comms (main thread).
		g_AccountMgr->ReadDatabase(Database(), m_CharacterID);
		SetName(Database()->avatar.avatar_first_name);

		int class_index = ClassIndex();
		// Setup base ScanRange, impuse, and signature
		m_BaseID[ID_SCAN_RANGE]    = m_Stats.SetStat(STAT_BASE_VALUE, STAT_SCAN_RANGE,    (float)BaseScanRange[class_index], "BASE_SHIP_VALUE");
		m_BaseID[ID_SIGNATURE]     = m_Stats.SetStat(STAT_BASE_VALUE, STAT_SIGNATURE,     (float)BaseVisableRange[class_index], "BASE_SHIP_VALUE");
		m_BaseID[ID_IMPULSE]       = m_Stats.SetStat(STAT_BASE_VALUE, STAT_IMPULSE,       62.0f, "BASE_SHIP_VALUE");
		m_BaseID[ID_WARP_RECOVERY] = m_Stats.SetStat(STAT_BASE_VALUE, STAT_WARP_RECOVERY, 4000.0f, "BASE_SHIP_VALUE");
	
		// Update Base Stats
		long Start_Tick = GetNet7TickCount();
        ReadSavedData();
		long End_Tick = GetNet7TickCount();

		LogMessage("Loaded player: %s [%d] from SQL, took %d to ReadSaveData()\n", Name(), CharacterID(), (End_Tick - Start_Tick));

		u32 hull_upgrade = PlayerIndex()->RPGInfo.GetHullUpgradeLevel();
		// these need the hull upgrade
		m_BaseID[ID_MASS]          = m_Stats.SetStat(STAT_BASE_VALUE, STAT_SHIP_MASS,     (float)(BaseMass[class_index]+hull_upgrade*5), "BASE_SHIP_VALUE");
		m_BaseID[ID_TURN_RATE]     = m_Stats.SetStat(STAT_BASE_VALUE, STAT_TURN_RATE,     (float)(150-(BaseManeuver[class_index]-hull_upgrade*5)), "BASE_SHIP_VALUE"); // "reverse" the final number, max level TE = 130deg/sec

		//Tractorbeam range needs the hull upgrade (but lets do both at once :) )
		m_BaseID[ID_TRACTORBEAM_RANGE] = m_Stats.SetStat(STAT_BASE_VALUE,STAT_TRACTORBEAM_RANGE, (float)1000.0f + 250.0f*hull_upgrade,"BASE_SHIP_VALUE");
		m_BaseID[ID_TRACTORBEAM_SPEED] = m_Stats.SetStat(STAT_BASE_VALUE,STAT_TRACTORBEAM_SPEED, (float)200.0f, "BASE_SHIP_VALUE");

		//go with these for slightly different warp effects...
		long level = 3 + (hull_upgrade / 2);
		ShipIndex()->BaseStats.SetWarpPowerLevel(level);
		ShipIndex()->CurrentStats.SetWarpPowerLevel(level);

		ShipIndex()->BaseStats.SetScanRange((s32) m_Stats.GetStat(STAT_SCAN_RANGE));
		ShipIndex()->CurrentStats.SetScanRange((s32) m_Stats.GetStat(STAT_SCAN_RANGE));

		float speed = CalculateSpeed();
		ShipIndex()->BaseStats.SetSpeed((s32)speed);
		ShipIndex()->CurrentStats.SetSpeed((s32)speed);

		ShipIndex()->BaseStats.SetVisibility((s32) m_Stats.GetStat(STAT_SIGNATURE));
		ShipIndex()->CurrentStats.SetVisibility((s32) m_Stats.GetStat(STAT_SIGNATURE));

		// ------

		// Update all skill avalabilitys
		SkillsList();

		// Make sure we calculate the quality for every item (should be fixed soon!)
		for(int i=0;i<40;i++)
		{
			ShipIndex()->Inventory.CargoInv.Item[i].SetInstanceInfo(" ");				// Set the flag
			QualityCalculator(ShipIndex()->Inventory.CargoInv.Item[i].GetData());
		}
		for(int i=0;i<96;i++)
		{
			PlayerIndex()->SecureInv.Item[i].SetInstanceInfo(" ");						// Set the flag
			QualityCalculator(PlayerIndex()->SecureInv.Item[i].GetData());
		}
		for(int i=0;i<20;i++)
		{
            if (ShipIndex()->Inventory.AmmoInv.Item[i].GetItemTemplateID() == -2)
            {
                ShipIndex()->Inventory.AmmoInv.Item[i].SetItemTemplateID(-1);
            }
			ShipIndex()->Inventory.AmmoInv.Item[i].SetInstanceInfo(" ");				// Set the flag
			QualityCalculator(ShipIndex()->Inventory.AmmoInv.Item[i].GetData());
		}

        //This is temporary and sohuld be removed in a few weeks ???
		// do this after ammo quality is calculated so login damage is not lower
        for(int i=0;i<20;i++)
        {
			m_Equip[i].Init(this, i);
			m_Equip[i].PullAuxData();
        }

		//g_PlayerMgr->SendGlobalVaMessage(GameID(), AdminLevel(), false , 5 , "%s has logged in", Name());
		g_PlayerMgr->SendGlobalChatEvent(CHEV_LOGGED_IN,this);

		SendMOTD();
		if (AdminLevel() >= DEV)
		{
			SendVaMessage("Dev Chat: /d <message>, GM Chat: /gm <message>, Beta Chat: /be <message>");

			int channel_id = g_PlayerMgr->GetChannelFromName("Dev");
			m_ChannelSubscription[channel_id] = true;
			channel_id = g_PlayerMgr->GetChannelFromName("GM");
			m_ChannelSubscription[channel_id] = true;
			channel_id = g_PlayerMgr->GetChannelFromName("Beta");
			m_ChannelSubscription[channel_id] = true;
		}
		else if (AdminLevel() >= GM)
		{
			SendVaMessage("GM Chat: /gm <message>, Beta Chat: /be <message>");

			int channel_id = g_PlayerMgr->GetChannelFromName("GM");
			m_ChannelSubscription[channel_id] = true;
			channel_id = g_PlayerMgr->GetChannelFromName("Beta");
			m_ChannelSubscription[channel_id] = true;
		}
		else if (AdminLevel() >= BETA)
		{
			SendVaMessage("Beta Chat: /be <message>");

			int channel_id = g_PlayerMgr->GetChannelFromName("Beta");
			m_ChannelSubscription[channel_id] = true;
		}
		//char message[80];
        //sprintf(message, "%s has logged in", Name());
        //g_PlayerMgr->ChatSendEveryone(GameID(), message, false, false, AdminLevel());
		//Do not send Aux here, it causes PDA corruption
    }
}	

void Player::CompleteLogin()
{
	//any code you need to run when you're guaranteed a player is logged in can go here
	//either inside the 'first login' if you only want it to run at first login,
	//or outside if it needs to run every time
	if (m_FirstLogin)
	{
		m_FirstLogin = false;
		CompleteInstalls();
		ForgiveXPDebt(m_LogoutTime);
		SendGuildMOTD();
		// Set our avatar as logged in
		SaveLogin();
	}
	ResetCombatTrance(); //only actually does anything if progen warrior and combat trance > 0
}

void Player::SetCharacterID(long char_id)
{
    m_CharacterID = char_id;
    //now execute a check to see if this character ID is currently logged in
    if (char_id > 0)
    {
        g_PlayerMgr->CheckForDuplicatePlayers(this);
    }
}

//This gets called every start of sector for players
void Player::SectorLogin()
{
    /* Clear the itembase-sent list */
    m_ItemList.ClearList();

	SetInSpace(false);
	m_Gating = false;

    AddPlayerToRangeList(this); //add ourselves to the range list - we're always in range of ourselves

    FirstLogin();

	long new_sector_id = PlayerIndex()->GetSectorNum();

	if (GroupID() > 0)
	{
		g_ServerMgr->m_PlayerMgr.RequestGroupAux(GroupID(),GameID());
	}

	UpdateDatabase();

    if (new_sector_id > 9999) // player is resurrected upon entering starbase - TODO: XP debt
    {
		// Find our Station and save the location for this
		// This will make sure we dont have to search again
		m_CurrentStation = g_ServerMgr->m_StationMgr.GetStation(new_sector_id);

		// Save and null out any data
		m_CurrentTalkTree = NULL;
		m_CurrentNPC = NULL;
    }

    if (ShipIndex()->GetIsIncapacitated())
    {
        ImmobilisePlayer();
        SendAuxShip();
    }

	SendClientSetTime(m_JoinTime);

	// Remove Radiation Damage if you are taking any
	RadiationDmg(false);

    if (m_LoginFailed == true)
    {
        m_LoginFailed = false;
		ObjectManager *om = GetObjectManager();
		if (om) om->RemovePlayerFromMOBRangeLists(this);
    }
}

//this is called when we receive the HandleStartAck from the client - indicates we're in space (99% sure).
void Player::SendLoginCamera()
{
    Object *came_from = 0;
    if (Active())
    {
        ObjectManager *obj_manager = GetObjectManager();
        if (obj_manager) came_from = obj_manager->FindGate(m_FromSectorID);
        if (came_from) //TODO: make a sequence where player spills from opening gate
        {
            SendActivateRenderState(came_from->GameID(), 1); //gate graphics activate
            CloseStargate(came_from->GameID());
        }
        else
        {
            //_sleep(50); //a little time required when you leave stations, but not for arriving from a gate.
        }
        
        SendCameraControl(m_CameraSignal, m_CameraID);
    }
    //This is called once the AckStart is sent
}

//DIMA: Shouldn't this be in playermanager??
//TB: It should be changed so the message is read into heap at startup (startup of PM is ok).
//Then, we should use a 'PushMessage' similar to how MVAS used to eject you, from this class.
//I rekon, anyway. I just hacked it in here so it still works.
//Message of the day.
void Player::SendMOTD()
{
#define MAX_MESSAGE_SIZE 512
    char message[MAX_MESSAGE_SIZE]; //use stack instead of heap.
	// Send MOTD
	FILE *f;
	fopen_s(&f, "motd.txt", "r");
	char *next_token = NULL;
	if (f)
	{
		fseek(f, 0, SEEK_END);
		long file_size = ftell(f);
		fseek(f, 0, SEEK_SET);
        if (file_size > MAX_MESSAGE_SIZE - 1)
        {
            file_size = MAX_MESSAGE_SIZE - 1;
        }

		long size = fread(message, 1, file_size, f);
		message[size] = 0;

		if (message)
		{
			//SendMessageString("Message of the day:", 3, false);
			char * line = strtok_s(message, "\n\r", &next_token);
			do
			{
				SendVaMessage("MOTD: %s", line);
				//SendMessageString(line, 3, false);
				line = strtok_s(NULL, "\n\r", &next_token);
			} 
            while (line != 0);
		}

		fclose(f);
	}
}

void Player::SetAccountUsername(char *name)
{
    m_AccountUsername = g_StringMgr->GetStr(name);
}

void Player::SetDatabase(CharacterDatabase &database)
{
    m_Database = database;
    SetName(m_Database.avatar.avatar_first_name);
}

void Player::HandleStarbaseAvatarChange(unsigned char* data)
{
    StarbaseAvatarChange *change = (StarbaseAvatarChange *) data;
    long sector_id = PlayerIndex()->GetSectorNum();

    if (change)
    {      
        Player * p = g_PlayerMgr->GetPlayer(change->AvatarID);

        if (p == (0))
        {
            return;
        }

        m_Mutex.Lock();
        if (sector_id != p->m_PlayerIndex.GetSectorNum())
        {
            p->m_PlayerIndex.SetSectorNum(sector_id);
        }
        p->SetPosition(change->Position);
        p->m_Orient = change->Orient;
        p->SetActionFlag(change->ActionFlag);
        p->SetHavePosition();
        m_Mutex.Unlock();
       
        // Broadcast this position to all other players in the same room
        if (p->ActionFlag() == 0x41)			// Send Avatar to everyone
        {
            p->SendStarbaseAvatarList();
			m_BroadcastPositionTick = GetNet7TickCount() + 500; //broadcast position in 0.5 seconds
        }
/*      else if (p->ActionFlag() == 0x11) // recustomise avatar terminal enter
		{
		}
		else if (p->ActionFlag() == 0x01) // recustomise cancel
		{
		}*/
		else
        {   
            p->BroadcastPosition();
        }
    }
}

void Player::HandleStarbaseRoomChange(unsigned char *data)
{
    StarbaseRoomChange *change = (StarbaseRoomChange *) data;

    Player *p = (0);
    u32 * sector_list = GetSectorPlayerList();

	if (change->OldRoom == -1 && change->NewRoom == 0)
	{
		m_Room = -1;
	}

    m_Mutex.Lock();
    m_Oldroom = m_Room;
    m_Room = change->NewRoom;
    SetActionFlag(0x01);
    m_Mutex.Unlock();

    StarbaseRoomChange SRoomUpdate;
    SRoomUpdate.AvatarID = GameID();
    SRoomUpdate.OldRoom = m_Oldroom;
    SRoomUpdate.NewRoom = m_Room;

    if (change && sector_list)
    {
		while (g_PlayerMgr->GetNextPlayerOnList(p, sector_list))
		{
			if (p)
			{
				 if (p->GameID() != GameID())
				 {
					 p->SendOpcode(ENB_OPCODE_00A0_STARBASE_ROOM_CHANGE,(unsigned char *) &SRoomUpdate, sizeof(SRoomUpdate));
					 
					 if (p->m_Room == m_Room)
					 {
						 //stimulate each avatar in the same room for this player, so we can see stationary players
						 SendStarbaseAvatarChange(p);
					 }
				 }
			}
		}
    }
}

// TODO: Broadcast the position change to all other players in the room
void Player::BroadcastPosition()
{
	Player *p = (0);
	u32 * sector_list = GetSectorPlayerList(); 

	if (sector_list)
	{	
		// This method broadcasts a player's position to all other players
		// in the same room.

		while (g_PlayerMgr->GetNextPlayerOnList(p, sector_list))
		{
			if (p && p->HavePosition() &&
				p->GameID() != GameID())
			{   
				p->SendStarbaseAvatarChange(this);
			}
		}
	}
}

void Player::SendStarbaseAvatarList()
{
    Player *p = 0;

	u32 * sector_list = GetSectorPlayerList();
    
    if (!sector_list)
    {
        return;
    }
    
    StarbaseRoomChange SRoomUpdate;
    SRoomUpdate.AvatarID = GameID();
    SRoomUpdate.OldRoom = m_Oldroom;
    SRoomUpdate.NewRoom = m_Room;
    
	while (g_PlayerMgr->GetNextPlayerOnList(p, sector_list))
	{        
        if (p && p->GameID() != GameID())            // Not this player
            // TODO: check action flag?
        {
            // This avatar is in the same room as this player
            // Send this player's avatar to the other player

			AvatarDescription avatar;
			if (m_Room != -1)
			{
				
				memset(&avatar, 0, sizeof(avatar));
				m_Mutex.Lock();
				memcpy(&avatar.avatar_data, &m_Database.avatar, sizeof(avatar.avatar_data));
				avatar.AvatarID = GameID();
				avatar.unknown3 = 1.0;
				avatar.unknown4 = 1.0;
				m_Mutex.Unlock();

				p->SendOpcode(ENB_OPCODE_0061_AVATAR_DESCRIPTION, (unsigned char *) &avatar, sizeof(avatar));

				p->SendStarbaseAvatarChange(this);
			}
            
            // Removes player from the room/orbs out
            p->SendOpcode(ENB_OPCODE_00A0_STARBASE_ROOM_CHANGE,(unsigned char *) &SRoomUpdate, sizeof(SRoomUpdate));

            if (m_Room != -1)
            {
                memset(&avatar, 0, sizeof(avatar));
                m_Mutex.Lock();
                memcpy(&avatar.avatar_data, &p->m_Database.avatar, sizeof(avatar.avatar_data));
                avatar.AvatarID = p->GameID();
                avatar.unknown3 = 1.0;
                avatar.unknown4 = 1.0;
                m_Mutex.Unlock();
                
                SendOpcode(ENB_OPCODE_0061_AVATAR_DESCRIPTION, (unsigned char *) &avatar, sizeof(avatar)); 
                
                SendOpcode(ENB_OPCODE_00A0_STARBASE_ROOM_CHANGE,(unsigned char *) &SRoomUpdate, sizeof(SRoomUpdate));
                
                if (p->HavePosition())
                {
                    // Send the Starbase Avatar Change packet to this client to update the other player's position
                    SendStarbaseAvatarChange(p);
                }
            }
        }
    }
}

char * Player::GetRank()
{
    static char *rank_list[] =
    {
        // TW (Enforcer)
        "Ensign","Lieutenant","Lt.Commander","Commander","Captain","Commodore","Admiral",
        // TT (Tradesman)
        "Prentice","Journeyman","Agent","Emptor","Guildsman","Magnate","Merchant Prince",
        // TE (Scout)
        "Scout","Tracker","Spotter","Outrider","Pathfinder","Ranger","Elite Ranger",
        // JW (Defender)
        "J'nai","D'nai","U'nai","Lai'shao","Dai'shao","Ten'shao","Ken'shao",
        // JT (Seeker)
        "Nan'Jeu","Zi'Jeu","Bo'Jeu","Hou'Jeu","Gong'Jeu","Wang'Jeu","Huangdi'Jeu",
        // JE (Explorer)
        "Aspirant","Initiate","Novice","Disciple","Adept","Master","Grandmaster",
        // PW (Warrior)
        "Legionaire","Centurion","Lancearate","Praefect","Legate","Consul","Proconsul",
        // PT (Privateer)
        "Quaestor","Aedile","Tribune","Praetor","Procurator","Triumvir","Imperator",
        // PE (Sentinel)
        "Inceptor","Librorum","Savant","Pedagogue","Doctrinaire","Magister","Magister Magna"
    };

    int rank_index = PlayerIndex()->RPGInfo.GetHullUpgradeLevel();

	return (rank_list[ClassIndex() * 7 + rank_index]);
}

void Player::Remove()
{
    Player * p;
    RangeListVec::iterator itrRList;
    PlayerList::iterator itrPList;

    u32 * sector_list = GetSectorPlayerList();

    if (m_PlayerIndex.GetSectorNum() == 0)
    {
        return;
    }
  
    if (m_PlayerIndex.GetSectorNum() <= 9999) //in space
    {
		p = (0);
		//now remove this player from all players who can see this player
		while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))
		{
            if (p->Active())
            {
                p->RemoveObject(GameID());
            }
		}

		if (sector_list)
		{
			p = (0);
			//now remove this player from all rangelists in this sector
			while (g_PlayerMgr->GetNextPlayerOnList(p, sector_list))
			{
				if (p && p->Active())
				{
					RemovePlayerFromRangeList(p);
				}
			}
		}
	}
    else
    {
        m_Oldroom = m_Room;
        m_Room = -1;
        SendStarbaseAvatarList();
    }

    //now remove the player from the sector list
	SectorManager *sm = GetSectorManager();
    if (sm) sm->RemovePlayerFromSectorList(this);

	SaveHullLevelChange(ShipIndex()->GetMaxHullPoints());
	SaveEnergyLevels();
}

void Player::MarkForRemoval()
{
    m_Remove = true;
	SectorManager *sm = GetSectorManager();
    if (sm)
    {
        sm->RemovePlayerFromSectorList(this);
    }
}

void Player::SendLoginShipData()
{
    short hull = (short) m_Database.ship_info.hull;
	float scale = 1.0f;
	if (m_ReplacementShipAsset)
	{
		hull = m_ReplacementShipAsset;
	}
	if (m_ReplacementShipScale > 0.0f)
	{
		scale = m_ReplacementShipScale;
	}
    SendCreate(GameID(), scale, hull, CREATE_SHIP);
    
    SendSubparts(this);
    SendShipColorization(this, 8);    // send the ship color scheme
    
    SendOpcode(ENB_OPCODE_0037_CLIENT_AVATAR, (unsigned char *) &m_CreateInfo.GameID, sizeof(long));
    SendOpcode(ENB_OPCODE_0047_CLIENT_SHIP, (unsigned char *) &m_CreateInfo.GameID, sizeof(long));
    
    AvatarDescription avatar;
    memset(&avatar, 0, sizeof(avatar));
    avatar.AvatarID = GameID();  
    memcpy(&avatar.avatar_data, &m_Database.avatar, sizeof(avatar.avatar_data));
    avatar.unknown3 = 1.0;
    avatar.unknown4 = 1.0;
        
    SendOpcode(ENB_OPCODE_0061_AVATAR_DESCRIPTION, (unsigned char *) &avatar, sizeof(avatar));
    
    SendDecal(GameID(), m_Database.ship_data.decal, 2);
    SendNameDecal(this);
    SendRelationship(GameID(), RELATIONSHIP_FRIENDLY, false);
    
    SetStartingPosition();
    SendAdvancedPositionalUpdate(GameID(), &m_Position_info);

	// Set shields to recharge
	RechargeReactor();
	RechargeShield();
}

void Player::SendShipData(Player *player, bool is_group_member)
{
    Player *player_to_send_to = (Player*)player;
    if (player_to_send_to)
    {
        short hull = (short) m_Database.ship_info.hull;
		float scale = 1.0f;
		if (m_ReplacementShipAsset)
		{
			hull = m_ReplacementShipAsset;
		}
		if (m_ReplacementShipScale > 0.0f)
		{
			scale = m_ReplacementShipScale;
		}
        player_to_send_to->SendCreate(GameID(), scale, hull, CREATE_SHIP);
        
        SendSubparts(player_to_send_to);
        SendShipColorization(player_to_send_to, 8);    // send the ship color scheme

        if (this == player)
        {
            SendOpcode(ENB_OPCODE_0037_CLIENT_AVATAR, (unsigned char *) &m_CreateInfo.GameID, sizeof(long));
	        SendOpcode(ENB_OPCODE_0047_CLIENT_SHIP, (unsigned char *) &m_CreateInfo.GameID, sizeof(long));
        }
        
        AvatarDescription avatar;
        memset(&avatar, 0, sizeof(avatar));
        avatar.AvatarID = GameID();  
        memcpy(&avatar.avatar_data, &m_Database.avatar, sizeof(avatar.avatar_data));
        avatar.unknown3 = 1.0;
        avatar.unknown4 = 1.0;
        
        if (player_to_send_to->AdminLevel() == 90 && player_to_send_to != this) //gives devs a bit more feedback.
        {
            player_to_send_to->SendVaMessage("Sending %s [%x] to you", Name(), GameID());
        }

		if (AdminLevel() == SDEV && player_to_send_to != this)
		{
			SendVaMessage("Sending you to %s [%x]", player_to_send_to->Name(), player_to_send_to->GameID());
		}

        player_to_send_to->SendOpcode(ENB_OPCODE_0061_AVATAR_DESCRIPTION, (unsigned char *) &avatar, sizeof(avatar));
        
        player_to_send_to->SendDecal(GameID(), m_Database.ship_data.decal, 2);
        SendNameDecal(player_to_send_to);
        player_to_send_to->SendRelationship(GameID(), RELATIONSHIP_FRIENDLY, false);

        m_ShipIndex.Buffer()->Lock();
        
        if (m_ShipIndex.BuildCreateExtendedPacket())
        {
            player_to_send_to->SendOpcode(ENB_OPCODE_001B_AUX_DATA, 
                m_ShipIndex.m_Packet, 
                (u16)m_ShipIndex.m_PacketLength);
        }
		m_ShipIndex.Buffer()->Unlock();
		
		if (is_group_member)
        {
			SendClickPacketTo(player_to_send_to);
        }
                
        player->SendAdvancedPositionalUpdate(GameID(), &m_Position_info);
    }
}

void Player::SendClickPacketTo(Player *player_to_send_to)
{
	m_ShipIndex.Buffer()->Lock();

	if (m_ShipIndex.BuildClickExtendedPacket())
	{
		player_to_send_to->SendOpcode(ENB_OPCODE_001B_AUX_DATA, 
			m_ShipIndex.m_Packet, 
			(u16)m_ShipIndex.m_PacketLength);
	}

	m_ShipIndex.Buffer()->Unlock();
}

void Player::SendSubparts(Player *player_to_send_to)
{
    if (player_to_send_to)
    {
		unsigned char subparts[128];
		memset(subparts,0,128);
		int index = 0;

		AddData(subparts, ntohl(GameID()), index);
		AddData(subparts, ntohl(4), index);
		AddDataS(subparts,"~01", index);
		index++;
		AddData(subparts, ntohl(m_Database.ship_info.profession), index);

		switch (m_Database.ship_data.race)
		{
		case RACE_JENQUAI:
			AddDataS(subparts,"~01/~03_01", index);
			index++;
			AddData(subparts, ntohl(m_Database.ship_info.engine), index);
			AddDataS(subparts,"~01/~03_02", index);
			index++;
			AddData(subparts, ntohl(m_Database.ship_info.engine), index);
			AddDataS(subparts,"~02", index);
			index++;
			AddData(subparts, ntohl(m_Database.ship_info.wing), index);
			break;

		default:
			AddDataS(subparts,"~02", index);
			index++;
			AddData(subparts, ntohl(m_Database.ship_info.wing), index);
			AddDataS(subparts,"~02/~03_01", index);
			index++;
			AddData(subparts, ntohl(m_Database.ship_info.engine), index);
			AddDataS(subparts,"~02/~03_02", index);
			index++;
			AddData(subparts, ntohl(m_Database.ship_info.engine), index);
			if (Profession() == PROFESSION_TRADER && Race() == RACE_TERRAN && PlayerIndex()->RPGInfo.GetHullUpgradeLevel() >= 5)
			{
				*((long*) &subparts[4]) = ntohl(6);
				AddDataS(subparts,"~02/~03_03", index);
				index++;
				AddData(subparts, ntohl(m_Database.ship_info.engine), index);
				AddDataS(subparts,"~02/~03_04", index);
				index++;
				AddData(subparts, ntohl(m_Database.ship_info.engine), index);
			}
			break;
		}

        player_to_send_to->SendOpcode(ENB_OPCODE_00B4_SUBPARTS, subparts, index);	
    }
}

void Player::SendPlayerInfo()
{
	PlayerIndex()->VendorInv.Clear();
	PlayerIndex()->SetMusicID(-1);

	SendAuxPlayerExtended();

	// Send missions now!
	for(int x=0;x<11;x++)
	{
		int stages = PlayerIndex()->Missions.Mission[x].GetStageCount();
		PlayerIndex()->Missions.Mission[x].SetAllFlags();
		//PlayerIndex()->Missions.Mission[x].Stages.SetAllFlags(stages);
		for(int y=0;y<stages;y++)
			PlayerIndex()->Missions.Mission[x].Stages.Stage[y].SetAllFlags();
	}
	SendAuxPlayer();

    //This actually invokes sending the AuxPlayer packet
    //Therefore do it after the initial is sent
	g_ServerMgr->m_PlayerMgr.RequestGroupAux(GroupID(), GameID());
}

void Player::SendShipInfo(long NewGameID, long SpeedFactor)
{
	int x, class_index = ClassIndex();
    //float stat_val;

   	ShipIndex()->SetGameID(NewGameID);
//	AddRacialBuff(); // needs ship gameid set (no longer applies to solo players)

//	m_Stats.ResetStats();

	for(x=0;x<20;x++) 
	{
		// Only need to do this once per login
        SendItemBase(ShipIndex()->Inventory.EquipInv.EquipItem[x].GetItemTemplateID());
		SendItemBase(ShipIndex()->Inventory.AmmoInv.Item[x].GetItemTemplateID());
	}

	ShipIndex()->SetTargetGameID(-1);
    ShipIndex()->SetPrivateWarpState(PRIVATE_WARP_AVAILABLE); 
    ShipIndex()->SetWarpTriggerTime(GetNet7TickCount());

	if (SpeedFactor == 2 && !m_SpeedDoubled)
	{
		SetDoubleSpeed(true);
	}
	else if (SpeedFactor == 1 && m_SpeedDoubled)
	{
		SetDoubleSpeed(false);
	}

    SendAuxShipExtended();

	m_Effects.SendEffects(this);
	m_Buffs.ChangeSector();

	SetItemList(ITEMLIST_CARGO);
}


void Player::SendAuxPlayer()
{	
	if (PlayerIndex() == NULL)
	{
		LogMessage("ERROR: Player Index not found for: %s", Name());
		return;
	}

	if (PlayerIndex()->BuildPacket())
	{
		SendOpcode(ENB_OPCODE_001B_AUX_DATA, PlayerIndex()->PacketBuffer, PlayerIndex()->PacketSize);
		//DumpBufferToFile(PlayerIndex()->PacketBuffer, PlayerIndex()->PacketSize, "auxplayerdat.dat",true);
	}
}

void Player::SendAuxPlayerExtended()
{
	if (PlayerIndex() == NULL)
	{
		LogMessage("ERROR: Player Index not found for: %s", Name());
		return;
	}

	if (PlayerIndex()->BuildExtendedPacket())
	{
        //assumes login only
		SendOpcode(ENB_OPCODE_001B_AUX_DATA, PlayerIndex()->PacketBuffer, PlayerIndex()->PacketSize); 
		//DumpBufferToFile(PlayerIndex()->PacketBuffer, PlayerIndex()->PacketSize, "auxplayerdat.dat",true);
        PlayerIndex()->ClearFlags();
	}
	else
	{
		// This should not happen...
		LogMessage("ERROR: NOT SENDING PlayerExtended for %s\n", Name());
	}

}

void Player::SendAuxShip(Player * other)
{
 /* If no Diff exists, exit */
    if (!m_ShipIndex.HasDiff())
    {
        return;
    }
    
    /* Block anyone else from using the buffer */
    m_ShipIndex.Buffer()->Lock();
    
    m_ShipIndex.BuildDiff();

	if ( m_ShipIndex.m_DiffLength == 0)
	{
		/* Unblock buffer */
		m_ShipIndex.Buffer()->Unlock();
		return;
	}
    
    SendOpcode(ENB_OPCODE_001B_AUX_DATA, m_ShipIndex.m_Diff, m_ShipIndex.m_DiffLength);
    
    /* If we need to copy this entire packet to someone else, do it now */
    if (other)
    {
        other->SendOpcode(ENB_OPCODE_001B_AUX_DATA, m_ShipIndex.m_Diff, m_ShipIndex.m_DiffLength);
    }
    
    /* It is possible that no changes exist for the create and click packet
    Its pointless to iterate though the range list if neither packet is valid */
    
    if (m_ShipIndex.m_CreateDiffLength || m_ShipIndex.m_ClickDiffLength)
    {
        Player *p;
        m_Mutex.Lock();
		p = (0);
		while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))
		{         
            /* Dont repeat packets to the same player */
            if (p == this || p == other)
            {
                continue;
            }
            
            /* This player is in the range list, he recieves the create diff always */
            if (m_ShipIndex.m_CreateDiffLength)
            {
                p->SendOpcode(ENB_OPCODE_001B_AUX_DATA, 
                    m_ShipIndex.m_CreateDiff, m_ShipIndex.m_CreateDiffLength);
            }
            
            /* If they have clicked and a click diff packet exists */
            if (/*IsClickedBy(p) && */m_ShipIndex.m_ClickDiffLength)
            {
                p->SendOpcode(ENB_OPCODE_001B_AUX_DATA, 
                    m_ShipIndex.m_ClickDiff, m_ShipIndex.m_ClickDiffLength);
            }

            if (!m_ShipIndex.m_ClickDiffLength && m_ShipIndex.BuildClickExtendedPacket())
            {
                p->SendOpcode(ENB_OPCODE_001B_AUX_DATA, 
                    m_ShipIndex.m_Packet, 
                    m_ShipIndex.m_PacketLength);
            }
        }
        m_Mutex.Unlock();
    }
    
    /* Unblock buffer */
    m_ShipIndex.Buffer()->Unlock();
    
}

void Player::_SendAuxShip(Player * other)
{
    /* If no Diff exists, exit */
    if (!m_ShipIndex.HasDiff())
    {
        return;
    }
    
    /* Block anyone else from using the buffer */
    m_ShipIndex.Buffer()->Lock();
    
    m_ShipIndex.BuildDiff();

	if ( m_ShipIndex.m_DiffLength == 0)
	{
		/* Unblock buffer */
		m_ShipIndex.Buffer()->Unlock();
		return;
	}
    
    SendOpcode(ENB_OPCODE_001B_AUX_DATA, m_ShipIndex.m_Diff, m_ShipIndex.m_DiffLength);
    
    /* If we need to copy this entire packet to someone else, do it now */
    if (other)
    {
        other->SendOpcode(ENB_OPCODE_001B_AUX_DATA, m_ShipIndex.m_Diff, m_ShipIndex.m_DiffLength);
    }
    
    /* It is possible that no changes exist for the create and click packet
    Its pointless to iterate though the range list if neither packet is valid */
    
    if (m_ShipIndex.m_CreateDiffLength || m_ShipIndex.m_ClickDiffLength)
    {
        Player *p;
        
		p = (0);
		while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))
		{         
            /* Dont repeat packets to the same player */
            if (p == this || p == other)
            {
                continue;
            }
            
            /* This player is in the range list, he recieves the create diff always */
            if (m_ShipIndex.m_CreateDiffLength)
            {
                p->SendOpcode(ENB_OPCODE_001B_AUX_DATA, 
                    m_ShipIndex.m_CreateDiff, m_ShipIndex.m_CreateDiffLength);
            }
            
            /* If they have clicked and a click diff packet exists */
            if (/*IsClickedBy(p) && */m_ShipIndex.m_ClickDiffLength)
            {
                p->SendOpcode(ENB_OPCODE_001B_AUX_DATA, 
                    m_ShipIndex.m_ClickDiff, m_ShipIndex.m_ClickDiffLength);
            }

            if (!m_ShipIndex.m_ClickDiffLength && m_ShipIndex.BuildClickExtendedPacket())
            {
                p->SendOpcode(ENB_OPCODE_001B_AUX_DATA, 
                    m_ShipIndex.m_Packet, 
                    m_ShipIndex.m_PacketLength);
            }
        }
        
    }
    
    /* Unblock buffer */
    m_ShipIndex.Buffer()->Unlock();
    
}

void Player::SendAuxShipExtended()
{
	if (m_ShipIndex.BuildExtendedPacket())
	{
        //assume this is only sent at login
		SendOpcode(ENB_OPCODE_001B_AUX_DATA, m_ShipIndex.m_Packet, m_ShipIndex.m_PacketLength);
        m_ShipIndex.ClearFlags();
	}
}

void Player::SendAuxManu(bool TwoBitFlags)
{
	if (ManuIndex()->BuildPacket(TwoBitFlags))
	{
        SendOpcode(ENB_OPCODE_001B_AUX_DATA, ManuIndex()->PacketBuffer, ManuIndex()->PacketSize);
		//DumpBufferToFile(ManuIndex()->PacketBuffer, ManuIndex()->PacketSize, "auxmanu.dat",true);
	}
}

void Player::SendShipColorization(Player * player_to_send_to, int count)
{
    if (player_to_send_to)
    {
        m_Mutex.Lock();

        if (count > MAX_COLORIZATION_ITEMS)
        {
            count = MAX_COLORIZATION_ITEMS;
        }

        Colorization colorization;
        colorization.GameID = GameID();
        colorization.ItemCount = count;

        colorization.item[0].metal = m_Database.ship_data.HullPrimaryColor.metal;
        colorization.item[0].HSV[0] = m_Database.ship_data.HullPrimaryColor.HSV[0];
        colorization.item[0].HSV[1] = m_Database.ship_data.HullPrimaryColor.HSV[1];
        colorization.item[0].HSV[2] = m_Database.ship_data.HullPrimaryColor.HSV[2];
        colorization.item[1].metal = m_Database.ship_data.HullSecondaryColor.metal;
        colorization.item[1].HSV[0] = m_Database.ship_data.HullSecondaryColor.HSV[0];
        colorization.item[1].HSV[1] = m_Database.ship_data.HullSecondaryColor.HSV[1];
        colorization.item[1].HSV[2] = m_Database.ship_data.HullSecondaryColor.HSV[2];

        colorization.item[2].metal = m_Database.ship_data.ProfessionPrimaryColor.metal;
        colorization.item[2].HSV[0] = m_Database.ship_data.ProfessionPrimaryColor.HSV[0];
        colorization.item[2].HSV[1] = m_Database.ship_data.ProfessionPrimaryColor.HSV[1];
        colorization.item[2].HSV[2] = m_Database.ship_data.ProfessionPrimaryColor.HSV[2];
        colorization.item[3].metal = m_Database.ship_data.ProfessionSecondaryColor.metal;
        colorization.item[3].HSV[0] = m_Database.ship_data.ProfessionSecondaryColor.HSV[0];
        colorization.item[3].HSV[1] = m_Database.ship_data.ProfessionSecondaryColor.HSV[1];
        colorization.item[3].HSV[2] = m_Database.ship_data.ProfessionSecondaryColor.HSV[2];

        colorization.item[4].metal = m_Database.ship_data.WingPrimaryColor.metal;
        colorization.item[4].HSV[0] = m_Database.ship_data.WingPrimaryColor.HSV[0];
        colorization.item[4].HSV[1] = m_Database.ship_data.WingPrimaryColor.HSV[1];
        colorization.item[4].HSV[2] = m_Database.ship_data.WingPrimaryColor.HSV[2];
        colorization.item[5].metal = m_Database.ship_data.WingSecondaryColor.metal;
        colorization.item[5].HSV[0] = m_Database.ship_data.WingSecondaryColor.HSV[0];
        colorization.item[5].HSV[1] = m_Database.ship_data.WingSecondaryColor.HSV[1];
        colorization.item[5].HSV[2] = m_Database.ship_data.WingSecondaryColor.HSV[2];

        colorization.item[6].metal = m_Database.ship_data.EnginePrimaryColor.metal;
        colorization.item[6].HSV[0] = m_Database.ship_data.EnginePrimaryColor.HSV[0];
        colorization.item[6].HSV[1] = m_Database.ship_data.EnginePrimaryColor.HSV[1];
        colorization.item[6].HSV[2] = m_Database.ship_data.EnginePrimaryColor.HSV[2];
        colorization.item[7].metal = m_Database.ship_data.EngineSecondaryColor.metal;
        colorization.item[7].HSV[0] = m_Database.ship_data.EngineSecondaryColor.HSV[0];
        colorization.item[7].HSV[1] = m_Database.ship_data.EngineSecondaryColor.HSV[1];
        colorization.item[7].HSV[2] = m_Database.ship_data.EngineSecondaryColor.HSV[2];

        size_t size = ((char *) &colorization.item[count]) - ((char *) &colorization);

        player_to_send_to->SendOpcode(ENB_OPCODE_0011_COLORIZATION, (unsigned char *) &colorization, size);
        m_Mutex.Unlock();
    }
}

void Player::StoreDockingCoords(float *position, float *heading)
{
    m_DockHeading[0] = heading[0];
    m_DockHeading[1] = heading[1];
    m_DockHeading[2] = heading[2];
    m_DockCoords[0] = position[0];
    m_DockCoords[1] = position[1];
    m_DockCoords[2] = position[2];
}

void Player::LogDockCoords()
{ 
    m_LogDockCoords = true; 
    StoreDockingCoords(Position(), Heading());
}

bool Player::RestoreDockingCoords()
{
    if (m_DockCoords[0] == 0 && m_DockCoords[1] == 0 && m_DockCoords[2] == 0)
    {
        return false;
    }
    else
    {
        m_Position_info.Velocity[0] =- m_DockHeading[0];
        m_Position_info.Velocity[1] =- m_DockHeading[1];
        m_Position_info.Velocity[2] =- m_DockHeading[2];
        SetPosition(m_DockCoords);
        LevelOrientation();
        m_DockCoords[0] = m_DockCoords[1] = m_DockCoords[2] = 0.0f;
        m_DockHeading[0] = m_DockHeading[1] = m_DockHeading[2] = 0.0f;
        return true;
    }
}

u16 Player::UpdatePositionFromMVAS(float *position, float *heading, bool heading_sent)
{
	unsigned long current_tick = GetNet7TickCount();
	bool tick_valid = true;
	float Range = 0.0f;
	u16 frequency = 2;

    if (m_LogDockCoords && heading_sent)
    {
		m_SendDockPos = true;
		m_SentDockPos = false;
        StoreDockingCoords(position, heading);
        m_LogDockCoords = false; //first received impulse since docking signal sent.
    }

	if (WarpDrive() || m_FollowObject || (!ReceivedMVAS() && PosX() == 0.0f && PosY() == 0.0f && PosZ() == 0.0f))
	{
		return (frequency);
	}

    m_LastAccessTime = current_tick;
    m_ReceivedMovement = true;

	if (Active() && !WarpDrive())
	{
		frequency = 1;
		
		/*if (m_LastRead != 0) 
		{
            Range = RangeFrom(position);

			if (Range > ((float)(current_tick - m_LastRead) * ShipIndex()->BaseStats.GetSpeed() * 4))
			{
				LogMessage("Poll position update not in range of ship. Would Reject\n");
				tick_valid = false;
			}
		}*/

#ifdef MOVEMENT_DEBUG
        LogDebug("Received MVAS from %s[0x%x]: %.2f %.2f %.2f\n", Name(), GameID(), PosX(), PosY(), PosZ());
#endif
		
		if (tick_valid)
		{
            m_Mutex.Lock();
			if (m_ReceivedMVAS == false)
			{
				LogMessage("MVAS synched and locked in for %s[0x%x]: x %.2f y %.2f z %.2f\n", Name(), GameID(), PosX(), PosY(), PosZ());
				m_ReceivedMVAS = true;

				if (g_Debug && AdminLevel() == 90)
				{
					SendVaMessage("MVAS: MVAS synched and locked.");
				}
			}

            SetPosition(position); //take player position from MVAS feed
            m_Mutex.Unlock();

            if (heading_sent) //take new heading from MVAS feed
            {
                SetVelocityVector(heading); //take heading from MVAS feed
                LevelOrientation(); //postfix our orientation from the heading data
            }

            UpdateLastPosition(current_tick);
			CheckAndRemoveGravity();

			//now send our position here, providing we're not following something or the force update is set
			if ( !(Following() || PlayerUpdateSet()) )
			{
				SendLocationAndSpeed(false);
			}

            
            /*if (m_MyDebugPlayer)
            {
                float *ori = Orientation();
                m_MyDebugPlayer->SetPosition(position);
                m_MyDebugPlayer->SetOrientation(ori[0], ori[1], ori[2], ori[3]);
                m_MyDebugPlayer->SetVelocity(m_Velocity);
                m_MyDebugPlayer->m_Position_info.SetSpeed = m_Velocity*0.001f;
                m_MyDebugPlayer->SendLocationAndSpeed(false);
            }*/
		}
        else //this update wasn't valid ... hacking most likely - use normal position calculation
        {
            SendLocationAndSpeed(true);
        }
	}

	return frequency;
}

float Player::FollowTarget()
{
    //get target
	ObjectManager *om = GetObjectManager();
	if (!om)
	{
		return 0.0f;
	}
    Object *obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());

	if (!obj)
	{
		StopFollowing();
	}
	else if (obj->ObjectType() == OT_STARGATE || obj->ObjectType() == OT_STATION)
	{
		FaceObject(obj);
	}
	else if (obj->ObjectType() != OT_MOB && obj->ObjectType() != OT_PLAYER)
    {
        m_FollowObject = false;
    }
    else if (m_FollowObject)
    {
        //update orientation to face target
        FaceObject(obj);

        float target_velocity = (obj->Velocity() > ShipIndex()->GetMaxSpeed()) ? 
                ShipIndex()->GetMaxSpeed() : obj->Velocity(); //make sure we can't exceed top speed.

        float range = obj->RangeFrom(Position());

        if (range < 10.0f)
        {
            m_Velocity = 0.0f;
        }
        else if (range < 200.0f)
        {
            m_Velocity = (target_velocity * 0.25f);
        }
        else if (range > 1500.0f)
        {
            m_Velocity = ShipIndex()->GetMaxSpeed();
        }
        else if (range > 3000.0f)
        {
            StopFollowing();
        }
        else
        {
            //update velocity to match target
            m_Velocity = target_velocity;
        }
    }
    else
    {
        StopFollowing();
    }

	//interrupt any skill that breaks on motion
	bool OnMotion = false, Ignored = false;
	if(m_CurrentSkill && m_CurrentSkill->SkillInterruptable(&OnMotion, &Ignored, &Ignored))
	{
		if(OnMotion)
		{
			m_CurrentSkill->InterruptSkillOnMotion(m_Velocity);
		}
	}

    return (m_Velocity);
}

void Player::StopFollowing()
{
    if (m_FollowObject && m_Velocity != 0.0f) SendContrailsRL(false);
    m_Mutex.Lock();
    m_FollowObject = false;
    m_Velocity = 0.0f;
	m_Position_info.SetSpeed = 0;
    m_SetUpdate = 1;
    m_Mutex.Unlock();
}

void Player::UpdateLastPosition(unsigned long current_tick)
{
    m_Mutex.Lock();
    m_LastRead = current_tick;
    m_LastPos[0] = PosX();
    m_LastPos[1] = PosY();
    m_LastPos[2] = PosZ();
    m_LastVelocity = m_Velocity;
    m_Mutex.Unlock();
}

bool Player::CheckUpdateConditions(unsigned long current_tick)
{
	if (!Active() || current_tick == m_LastUpdate)
	{
		return false;
	}

    if (m_DebugPlayer)
    {
        return false;
    }
	
	if (m_LastUpdate == 0)
	{
		m_LastUpdate = current_tick;
		return false;
	}

    return true;
}

void Player::CalcNewHeading(float tdiff, bool turn)
{
    /*if (m_YInput != 0.0f)
    {
        m_YHeading += m_YInput*tdiff*ShipIndex()->GetMaxTiltRate(); 
        if (m_YHeading > 1.221731f) m_YHeading =   1.221731f; // ShipIndex()->GetMaxTiltAngle()) m_YHeading =  ShipIndex()->GetMaxTiltAngle(); //TODO: set this value in Aux, currently zero
        if (m_YHeading < -1.221731f) m_YHeading = -1.221731f; //-ShipIndex()->GetMaxTiltAngle()) m_YHeading = -ShipIndex()->GetMaxTiltAngle();
    }

    if (m_ZInput != 0.0f)
    {
        m_ZHeading += m_ZInput*tdiff*ShipIndex()->GetMaxTurnRate(); 
        if (m_ZHeading > 2.0f*3.14159f) m_ZHeading -= 2.0f*3.14159f;
        if (m_ZHeading < 0.0f)          m_ZHeading += 2.0f*3.14159f;
    }*/

    float rot_Z[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
    float rot_Y[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
    float result[4];
    
    if (m_ZInput != 0.0f)
    {
        rot_Z[2] = -m_ZInput*tdiff*0.42f/883.0f*1000.0f;
        m_ReceivedMovement = true;
    }
    
    if (m_YInput != 0.0f)
    {
        rot_Y[1] = -m_YInput*tdiff*0.42f/883.0f*1000.0f;
        m_ReceivedMovement = true;
    }
    
    if (m_ReceivedMovement)
    {
        Quat4fMul(rot_Z, rot_Y, result);
        Quat4fMul(Orientation(), result, Orientation());
        Quat4fNormalize(Orientation());
        SetHeading();
    }
}

//This updates the instantaneous velocity for the packets (m_Velocity)
//and also calculates the average velocity over the time period for position calculation
float Player::CalcVelocity(float tdiff)
{
    float av_speed;

	//max tdiff we should ever get is about 1 second
	if (tdiff > 1.0f)
	{
		tdiff = 1.0f;
	}
   
    if (m_Accelerating)
    {
        float endspeed_time = 0.0f;
        float final_velocity = 0.0f;
        float acc = 1.0f;

        //now calculate time to end speed
        switch (m_Thrust)
        {
        case 1: //thrust forward
            endspeed_time = (ShipIndex()->GetMaxSpeed() - m_Velocity) / ShipIndex()->GetAcceleration();
            final_velocity = ShipIndex()->GetMaxSpeed();
            break;
        case 0:
            if (m_Velocity > 0) acc = -1.0f;
            endspeed_time = -acc * m_Velocity / ShipIndex()->GetAcceleration();
            final_velocity = 0.0f;
            break;
        case -1:
            acc = -1.0f;
            endspeed_time = (ShipIndex()->GetMinSpeed() - m_Velocity) / -ShipIndex()->GetAcceleration();
            final_velocity = ShipIndex()->GetMinSpeed();
            break;
        }

        //now calculate average speed over time period
        if (endspeed_time < tdiff)
        {
            //we finished accelerating in this time period
            av_speed = final_velocity;
			m_Velocity = final_velocity;
            m_Accelerating = false;
        }
        else
        {
			av_speed = m_Velocity + 0.5f*acc*tdiff*ShipIndex()->GetAcceleration(); //starting velocity + 1/2 of velocity increase
            m_Velocity = m_Velocity + acc*tdiff*ShipIndex()->GetAcceleration();
        }
    }
    else
    {
        av_speed = m_Velocity;
    }

	//interrupt any skill that breaks on motion
	bool OnMotion = false, Ignored = false;
	if(m_CurrentSkill && m_CurrentSkill->SkillInterruptable(&OnMotion, &Ignored, &Ignored))
	{
		if(OnMotion && ObjectIsMoving() && m_CurrentSkill)
		{
			m_CurrentSkill->InterruptSkillOnMotion(m_Velocity);
		}
	}

    return (av_speed);
}

void Player::CheckNavs()
{
	SectorManager *sm = GetSectorManager();
	ObjectManager *om = GetObjectManager();
	if (sm && om)
	{
		if (/*m_FoundAllSectorNavs[sm->GetSectorNumber()] ||*/ m_Hijackee != 0) //no need to check if we've explored everything, or we've hijacked something
		{
			return;
		}

		//begin sweep of navs in sector to see if we're in expose or explore range of any navs
		m_FoundAllSectorNavs[sm->GetSectorNumber()] = !om->CheckNavRanges(this);
	}
}

long *Player::ExposedNavList()
{
    return (m_NavsExposed);
}

void Player::CalcNewPosition(unsigned long current_tick, bool turn)
{
	float tdiff;
    float av_speed;
	u32 tick1 = 0, tick2 = 0, tick3 = 0, tick4 = 0, tick5 = 0;
	u32 current_tick1 = GetNet7TickCount();

	m_WeaponsPerTick = 0;

    if (turn && m_FollowObject)
    {
        m_FollowObject = false;
		m_Position_info.SetSpeed = 0;
    }

    if (!CheckUpdateConditions(current_tick))
    {
        return;
    }
    
    tdiff = (float)(current_tick - m_LastUpdate)/1000.0f;

    if (m_FollowObject)
    {
        av_speed = FollowTarget();
    }
    else
    {
        if (!IS_PLAYER(m_MVAS_index))
        {
            CalcNewHeading(tdiff, turn);
        }
        //Now perform the acceleration calculations to update player velocity and get average speed of tdiff period
        av_speed = CalcVelocity(tdiff);
		tick1 = GetNet7TickCount();
    }
       
    if (av_speed != 0.0f)
	{
        if (m_WarpDrive || m_FollowObject) 
		{
			//only calculate new position if player is not in control
			ExtrapolatePosition(av_speed, tdiff);
		}
		tick2 = GetNet7TickCount();
		if ((m_MovementID % 10) == 0)
		{
			CheckNavs(); //this checks to see if we're in range of any unexposed or unexplored navs
			tick3 = GetNet7TickCount();
		}
		tick4 = GetNet7TickCount();
        if (m_WarpDrive)
        {
			if (CheckBoundaries())
			{
				SendVaMessage("Reached boundary.");
				TerminateWarpGroup();
			}

            m_ReceivedMovement = true;
            CheckWarpNavigation();

			//detect gravity well in warp
			if (m_GWell != -1)
			{
				TerminateWarp();
			}
        }
		tick5 = GetNet7TickCount();
    }

	if (tick5 > 0 && (tick5 - current_tick) > 10)
	{
		//LogMessage("CNP %d %d %d %d %d %d\n", (current_tick1 - current_tick), (tick1-current_tick), (tick2-current_tick), (tick3-current_tick)
		//	, (tick4-current_tick), (tick5-current_tick));
	}

    m_LastUpdate = current_tick;
}

void Player::SetVelocity(float velocity)
{
	m_Velocity = velocity;
	
	//interrupt any skill that breaks on motion
	bool OnMotion = false, Ignored = false;
	if(m_CurrentSkill && m_CurrentSkill->SkillInterruptable(&OnMotion, &Ignored, &Ignored))
	{
		if(OnMotion)
		{
			m_CurrentSkill->InterruptSkillOnMotion(m_Velocity);
		}
	}
}

float Player::CalculateSpeed()
{
	float mass,thrust;

	mass  = m_Stats.GetStat(STAT_SHIP_MASS);
	if (mass == 0.0f) // just in case
		mass = 40.0f;
	thrust = m_Stats.GetStat(STAT_IMPULSE);
	return thrust/mass * 100.0f + m_Stats.GetStat(STAT_ENGINE_TOP_SPEED); // use WW speed formula
}

float Player::CalculateTurnRate()
{
//	float mass,thrust;

//	mass  = m_Stats.GetStat(STAT_SHIP_MASS);
//	if (mass == 0.0f) // just in case
//		mass = 40.0f;
//	thrust = m_Stats.GetStat(STAT_IMPULSE);
	return /*thrust/mass * */m_Stats.GetStat(STAT_TURN_RATE);
}

void Player::AdjustAndSetSpeeds(bool sendaux, bool immobilise)
{
	float speed,accel,turn,multiplier = 1.0f;

	if (m_SpeedDoubled)
		multiplier *= 2.0f;
	if (m_SpeedHalved)
		multiplier *= 0.5f;
	if (immobilise)
	{
		speed = accel = turn = 0.0f;
	}
	else
	{
		m_Accelerating = true;
		speed = CalculateSpeed() * m_GravityField;
		accel = 180.0f - m_Stats.GetStat(STAT_SHIP_MASS); // unmodified mass min 30, max 80
		// cap it at a minimum value
		if (accel < 50.0)
			accel = 50.0f;
		turn = CalculateTurnRate() * m_GravityHandle;
		// cap the turn rate so it doesnt get out of control
		if (turn * multiplier > 150.0f)
			turn = 150.0f / multiplier;
		if (turn < 30.0f)
			turn = 30.0f;
	}
	ShipIndex()->SetMaxSpeed( speed * multiplier);
	ShipIndex()->SetMinSpeed(-speed * multiplier * 0.5f);
	ShipIndex()->SetAcceleration(accel * multiplier);
	ShipIndex()->SetMaxTurnRate((turn * 3.141f / 180.0f) * multiplier); // default was 1.158
	ShipIndex()->CurrentStats.SetSpeed((s32)(speed * multiplier));
	ShipIndex()->CurrentStats.SetTurnRate((s32)(turn * multiplier));

	if (sendaux)
		SendAuxShip();
}

void Player::SetDoubleSpeed(bool on)
{
	m_SpeedDoubled = on;
	AdjustAndSetSpeeds(true,false);
}

void Player::SetHalfSpeed(bool on)
{
	m_SpeedHalved = on;
	AdjustAndSetSpeeds(true,false);
}

void Player::Move(int type)
{
    if (m_FollowObject)
    {
        m_FollowObject = false;
        //bring ship to halt.
        m_Velocity = 0.0f;
        m_SetUpdate = 1;
    }

    m_ReceivedMovement = true;
	m_Arrival_Flag = false;

    unsigned long current_tick = GetNet7TickCount();
        
    switch(type)
    {
    case 0:             //accelerate via mouse
    case 2:				//forward thrust button
        CalcNewPosition(current_tick);
        m_Thrust = 1;
        m_Accelerating = true;
		if (m_Velocity == 0.0f) SendStartMoving();
        break;
    case 1:             //Reverse thrust on 'z' keypress
    case 3:				//Reverse thrust
        CalcNewPosition(current_tick);
        m_Thrust = -1;
        m_Accelerating = true;
        break;
    case 4:             //kill thrust, start decelerating
        CalcNewPosition(current_tick);
        m_Thrust = 0;
        m_Accelerating = true;
		SendSlowingDown();
        break;
    case 5:
        //do nothing
        break;

    case 12:
        //follow something
        if (!m_WarpDrive && !m_Prospecting && !ShipIndex()->GetIsIncapacitated())
        {
            m_FollowObject = true;
        }
        break;
        
    default:
        LogMessage("** Received strange move impulse from %s = %d\n", Name(), type);
        break;
    }

    SetLastAccessTime(current_tick);
}

void Player::MoveToward(Object *obj, float speed)
{
    FaceObject(obj);
    m_Velocity = speed;
    SendLocationAndSpeed(true);
	m_FollowObject = true;

	//interrupt any skill that breaks on motion
	bool OnMotion = false, Ignored = false;
	if(m_CurrentSkill && m_CurrentSkill->SkillInterruptable(&OnMotion, &Ignored, &Ignored))
	{
		if(OnMotion)
		{
			m_CurrentSkill->InterruptSkillOnMotion(m_Velocity);
		}
	}
}

void Player::UpdatePositionInfo()
{
    if (m_WarpDrive)
    {
        m_Position_info.Bitmask = 0x01;  
    }
    else if (m_Velocity == 0.0f)
	{
		m_Position_info.Bitmask = 0x00;
		m_Position_info.RotY = 0.0f;
		m_Position_info.RotZ = 0.0f;
	}
	else
	{
		m_Position_info.Bitmask = 0x07;
	}

	m_Position_info.Bitmask |= 0x28;
    
    m_Position_info.CurrentSpeed = m_Velocity*0.001f;
    m_Position_info.MovementID = m_MovementID;
    m_Position_info.DesiredY = 0;
    m_Position_info.DesiredZ = 0;
    m_Position_info.Acceleration = ShipIndex()->GetAcceleration()*0.001f;
	if (m_FollowObject)
	{
		m_Position_info.SetSpeed = m_Velocity*0.001f;
	}
}

void Player::SendStartMovementRefresh()
{
	m_MovementID = 5;
	UpdatePositionInfo();
	SendAdvancedPositionalUpdate(GameID(), &m_Position_info);
}

void Player::SendStartMoving()
{
	UpdatePositionInfo();
	m_Position_info.Bitmask = 0x04;
	m_Position_info.Acceleration = ShipIndex()->GetAcceleration()*0.001f;

	SendToVisibilityList(false);
	m_UpdateSent = true;
}

void Player::SendSlowingDown()
{
	UpdatePositionInfo();
	m_Position_info.Bitmask = 0x04;
	m_Position_info.Acceleration = -ShipIndex()->GetAcceleration()*0.001f;

	SendToVisibilityList(false);
	m_UpdateSent = true;
}

void Player::SendLocationAndSpeed(bool include_player, bool zeroupdate)
{
    if (!m_UpdateSent && Active())
    {
        UpdatePositionInfo();

        //use setupdate to force local client player update.
        if (m_SetUpdate > 0)
        {
            m_SetUpdate--;
            include_player = true;
        }

		if (m_NoPlayerUpdate)
		{
			include_player = false;
		}

		if (m_Hijackee != 0)
		{
			Object *obj = GetObjectManager()->GetObjectFromID(Hijackee());
			if (obj)
			{
				obj->SetPosition(Position());
				obj->SetOrientation(Orientation());
				obj->SetVelocity(m_Velocity);
				obj->SetMovementID(m_MovementID);
				obj->SetAcceleration(m_Position_info.Acceleration);
				obj->Turn(m_ZInput*0.1f);
				obj->SendLocationAndSpeed(true, true);
				
				if (include_player || m_WarpDrive)
				{
					SendAdvancedPositionalUpdate(GameID(), &m_Position_info);
				}
			}
		}
		else
		{
			SendToVisibilityList(include_player);
		}

#ifdef MOVEMENT_DEBUG
        // Too many printouts to use without a #define
        LogDebug("Pos Update [%c][%d] sent for %s [%x]: %.2f %.2f %.2f [vel %.2f] [mask %x]\n", include_player ? '*' : ' ',
            m_Position_info.MovementID,
            Name(), GameID(), 
			PosX(), PosY(), PosZ(),
            m_Position_info.CurrentSpeed, m_Position_info.Bitmask);
#endif
	}
	m_UpdateSent = false;
}

//this just removes us from view of all players without affecting anything else
void Player::RemovePlayerFromView(bool group)
{
	//all the players that can see us are those on our range list
	bool group_member = false;
	Player *p = (0);
	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))	
	{
		if (group)
		{
			group_member = g_ServerMgr->m_PlayerMgr.CheckGrouped(this, p);
		}
		if (p->GameID() != GameID() && !group_member)
		{
			p->RemoveObject(GameID());
			g_PlayerMgr->UnSetIndex(p, m_RangeList);
			g_PlayerMgr->UnSetIndex(p, m_ClickedList);
		}
	}
}

bool Player::HijackObject(Object *obj)
{
	if (!obj || obj->ObjectType() == OT_PLAYER)
    {
        return false;
    }

    //first hide this player from view of all players that see us (even grouped)
	RemovePlayerFromView(false);

    //now set Hijackee
	m_Hijackee = obj->GameID();
    obj->SetPlayerHijack(this);
	obj->Turn(0);
	obj->Tilt(0);

    //now set position to Hijackee
    SetPosition(obj->Position());
    //ok, ready to carry on as usual now!

	return true;
}

char *Player::GetHijackeeName()
{
	char *retval = Name();
    if (GetObjectManager() && Hijackee() > 0)
    {
		Object *obj = GetObjectManager()->GetObjectFromID(Hijackee());
		if (obj)
		{
			retval = obj->Name();
		}
	}
	
	return retval;
}

void Player::SetActive(bool flag)
{
    m_Mutex.Lock();
    if (Active() == false && flag == true)
    {
        SendLocationAndSpeed(true);
        SetLastAccessTime(GetNet7TickCount());
    }
    m_Active = flag;
    m_Mutex.Unlock();
}

//warp methods
void Player::BlankWarpNavs()
{
    memset(m_WarpNavs, 0, sizeof(m_WarpNavs));
    m_WarpNavIndex = 0;
    m_WarpNavCount = 0;
}

void Player::SetupWarpNavs(short navs, long *target_id)
{
    BlankWarpNavs();

    if (navs > 0)
	{
		for(int i = 0; i < navs; i++)
		{
			m_WarpNavs[i] = target_id[i];
		}
	}

    m_WarpNavCount = navs;
}

void Player::InitiateWarpBroadcast()
{
    ImmobilisePlayer();

	ShipIndex()->SetWarpTriggerTime(m_WarpBroadcastTime);
    ShipIndex()->SetPrivateWarpState(PRIVATE_WARP_INITIATE_WARP);
    ShipIndex()->SetGlobalWarpState(GLOBAL_WARP_INITIATE_WARP);

    //This now sends to the range list as needed
    SendAuxShip();

	//TODO: investigate how to get the effects properly - we shouldn't need to manually drive them
	//      there is some problem with how the AuxShip data is sent (I think).
	ObjectEffect WarpEffect;
	WarpEffect.Bitmask = 3;
	WarpEffect.GameID = GameID();
	WarpEffect.EffectDescID = 443; // Warp effect
	m_WarpEffect = GetSectorNextObjID();
	WarpEffect.EffectID = m_WarpEffect;
	WarpEffect.TimeStamp = GetNet7TickCount();

	SendObjectEffectRL(&WarpEffect);
}

void Player::StartWarpBroadcast()
{
    //Dima: Changed to first send the warp effect, and THEN send the aux
    //      There may be a small delay before the aux packet is sent

	ObjectEffect WarpEffect;
	WarpEffect.Bitmask = 3;
	WarpEffect.GameID = GameID();
	WarpEffect.EffectDescID = 23; // Warp effect
	WarpEffect.EffectID = GetSectorNextObjID();
	WarpEffect.TimeStamp = GetNet7TickCount();

	SendObjectEffectRL(&WarpEffect);
	
    ShipIndex()->SetPrivateWarpState(PRIVATE_WARP_SHAKE);
    ShipIndex()->SetGlobalWarpState(GLOBAL_WARP_ENGINE_TRAIL);
    ShipIndex()->SetEngineThrustState(1);

    //This now sends to the range list as needed
    SendAuxShip();
}

void Player::SendEndWarpBroadcast()
{
    ShipIndex()->SetPrivateWarpState(PRIVATE_WARP_END_NORMALLY);
    ShipIndex()->SetGlobalWarpState(GLOBAL_WARP_END_WARP);
	ShipIndex()->SetWarpTriggerTime(GetNet7TickCount() + (u32)m_Stats.GetStat(STAT_WARP_RECOVERY));
    ShipIndex()->SetEngineThrustState(0);

	//now cancel the warp effect:
	RemoveEffectRL(m_WarpEffect);

    //This now sends to the range list as needed
    SendAuxShip();
}

void Player::ResetMaxSpeed(bool increase)
{
	/*
	if(m_Thrust != 0)
	{
		m_Accelerating = true;
	}
	*/
	
	m_ReceivedMovement = true;
	if(increase)
	{
		m_Thrust = 1;
		m_Accelerating = true;
		SendContrailsRL(true);
	}
	
	else
	{
		m_Thrust = 0;
	 	m_Accelerating = false;
	}
	m_Position_info.SetSpeed = ShipIndex()->GetMaxSpeed() * 0.001f;
	CalcNewPosition(GetNet7TickCount(),false);
	SendLocationAndSpeed(true);
}

void Player::PrepareForWarp()
{
	bool Ignored = false, OnAction = false;
    m_Velocity = 0;
    m_Position_info.SetSpeed = 0.0f;
    m_Position_info.RotZ = 0.0f;
    m_Position_info.RotY = 0.0f;
    m_Thrust = 0;
    m_Accelerating = false;
    m_FollowObject = false;

	//if cloaked remove cloak (ranks < 6 only)
	if(m_CurrentSkill && m_CurrentSkill->SkillInterruptable(&Ignored, &Ignored, &OnAction))
	{
		if(OnAction)
		{
			m_CurrentSkill->InterruptSkillOnAction(WARPING);
		}
	}

	// See if we are warping and not incapacitated and not in a gravitywell
	if (!m_WarpDrive && !ShipIndex()->GetIsIncapacitated() && m_GWell == -1)
	{
		if (m_WarpNavCount > 0)
		{
			//find last object on nav list
			ObjectManager *om = GetObjectManager();
			if (om)
			{
				Object *obj = om->GetObjectFromID(m_WarpNavs[m_WarpNavCount - 1]);
				if (obj)
				{
					m_DistToNav = obj->RangeFrom(Position());
					if (m_DistToNav < 4000.0f)
					{
						SendVaMessageC(17,"Target too close for warp drive.");
						return;
					}
				}
				// face the first waypoint
				obj = om->GetObjectFromID(m_WarpNavs[0]);
				if (obj)
					FaceObject(obj);
			}
		}

		// calculate the energy required to warp (targeted warp only)
		m_Equip[1].Lock();
		ItemBase *itembr = m_Equip[1].GetItemBase(); // reactor
		u16 reactor_level = itembr ? itembr->TechLevel() : 1;
		m_Equip[1].Unlock();

		m_Equip[2].Lock();
		ItemBase *itemb = m_Equip[2].GetItemBase(); // engine
		ItemInstance *itemi = m_Equip[2].GetItemInstance();
		u16 warp_rating = itemb ? itemb->TechLevel() : 1;
		m_Equip[2].Unlock();

		float warp_charge_factor = 0.25f;

		// Lets keep this simple.. just seems to be all over the place using item stats
		if (warp_rating >= reactor_level)
			warp_charge_factor =  0.25f * (float)(warp_rating - reactor_level + 1);
		if (warp_rating < reactor_level)
			warp_charge_factor =  0.25f - (float)(reactor_level - warp_rating) * 0.05f;

		if (warp_charge_factor > 0.95f) warp_charge_factor = 0.95f;
		if (warp_charge_factor < 0.05f) warp_charge_factor = 0.05f;
		float warp_energy = warp_charge_factor * ShipIndex()->GetMaxEnergy();

		//float warp_energy = itemi ? itemi->EngineFreeWarpDrain*10 : 50; // based on 10 seconds of free warping
		float warp_drain = m_Stats.ModifyValueWithStat(STAT_WARP_ENERGY,(float)(warp_energy));
	
		// Take structure damage each time if it doesnt have the power needed
		if (warp_drain > ShipIndex()->GetMaxEnergy() && GetEnergy() >= 0.95f)
		{
			float overload = (warp_drain - ShipIndex()->GetMaxEnergy() + rand()%25) / 500.0f;
			AuxEquipItem *reactor = &ShipIndex()->Inventory.EquipInv.EquipItem[1];
			if (reactor && reactor->GetStructure() > 0.0f)
			{
				if (overload > reactor->GetStructure())
					overload = reactor->GetStructure();
				reactor->SetStructure(reactor->GetStructure() - overload);
				_Item *i = reactor->GetItemData();
				if (i)
				{
					SaveEquipmentChange(1,i);
					QualityCalculator(i);
				}
			}
			SendVaMessageC(17,"Warning, engine is straining the reactor, which has been damaged.");
			warp_drain = ShipIndex()->GetMaxEnergy() * 0.95f;
		}

		// enough energy?
		if (GetEnergyValue() < warp_drain)
		{
			SendVaMessageC(17,"Not enough energy to engage warp."); 
			return;
		}
		m_WarpBroadcastTime = g_PlayerMgr->GetGroupWarpRecovery(this);

		RemoveEnergy(warp_drain, m_WarpBroadcastTime);

		m_WarpBroadcastTime += GetNet7TickCount();

		//tell movement code we have stopped
		CalcNewPosition(GetNet7TickCount());

		LevelOrientation();

		m_WarpDrive = true;
		m_WarpCharing = true;

		SendLocationAndSpeed(true);

		InitiateWarpBroadcast();
		AbortProspecting(true,true);
		m_Arrival_Flag = false;
	}
}

void Player::FreeWarpDrain()
{
	//work out how long we have to warp based on current reactor level
	float energy = GetEnergyValue();
	m_Equip[2].Lock();
	float warpdrain = m_Equip[2].GetItemInstance()->EngineFreeWarpDrain;
	m_Equip[2].Unlock();
	if (warpdrain <= 0)
	{
		LogMessage("Setting Engine Freewarp drain to 20.0f\n");
		warpdrain = 20.0f;
	}
	float min_drain = warpdrain * 0.05f; // 5% minimum cost
	warpdrain = m_Stats.ModifyValueWithStat(STAT_WARP_ENERGY,warpdrain);
	if (warpdrain < min_drain)
		warpdrain = min_drain;

	long time_to_drain = (long)((energy/warpdrain)*1000.0f);
	m_WarpDrain = DrainReactor(time_to_drain, energy);
	//at this rate we'll have to terminate warp at time_to_drain secs.
    m_WarpTime = time_to_drain + GetNet7TickCount();
}

void Player::StartWarp()
{
    if (m_WarpDrive)
    {
		m_WarpDrain = -1.0f;
		m_WarpCharing = false;
        StartWarpBroadcast();
		// Get the current speed as a group wide
        m_Velocity = g_ServerMgr->m_PlayerMgr.GetGroupWarpSpeed(this) + 0.5f;
		if (m_Velocity < 0.0f || m_Velocity > 10000.0f) m_Velocity = (float)ShipIndex()->CurrentStats.GetWarpSpeed();
		if (m_Velocity < 0.0f || m_Velocity > 10000.0f) m_Velocity = 3500.0f; //emergency! Just use a value of 3500 if everything's gone wrong.

        SendLocationAndSpeed(true);
        m_WarpBroadcastTime = 0;

		ObjectManager *om = GetObjectManager();
        
        if (m_WarpNavs[0] != 0 && om)
        {
            Object *obj = om->GetObjectFromID(m_WarpNavs[0]);
            if (obj)
            {
                FaceObject(obj);
                ShipIndex()->SetTargetGameID(obj->GameID());
                SendAuxShip();
                obj->OnTargeted(this);
				m_WarpDrain = DrainReactor(0x7FFFFFFFL, 0.0f); // no recharge while warping
            }
            else
            {
                TerminateWarpGroup();
            }
        }
        else
        {
            FreeWarpDrain();
        }
    }
}

void Player::UpdateWarpNavigation()
{
    if (m_WarpDrive)
    {
        m_WarpNavIndex++;
		ObjectManager *om = GetObjectManager();
        if (m_WarpNavs[m_WarpNavIndex] != 0 && om)
        { 
            SendWarpIndex(m_WarpNavIndex);
            Object *obj = om->GetObjectFromID(m_WarpNavs[m_WarpNavIndex]);
			if (obj)
			{
				FaceObject(obj);
				ShipIndex()->SetTargetGameID(obj->GameID());
				SendAuxShip();
				obj->OnTargeted(this);
				m_Velocity = g_ServerMgr->m_PlayerMgr.GetGroupWarpSpeed(this) + 0.5f; //restore speed if required
			}
        }
        else
        {
            TerminateWarpGroup();
        }
    }
}

void Player::CheckWarpNavigation()
{
    if (m_WarpDrive && m_WarpNavs[m_WarpNavIndex] != 0)
    {
		ObjectManager *om = GetObjectManager();
		if (om)
		{
			Object *obj = om->GetObjectFromID(m_WarpNavs[m_WarpNavIndex]);
			if (obj)
			{
				float multiplier = 0.98f;
				if (m_WarpNavs[m_WarpNavIndex+1] == 0) multiplier = 0.75f;
				float warp_nav_distance = obj->RangeFrom(Position());

				//if (warp_nav_distance > m_DistToNav) m_OverrunCount++;

				/*if (m_OverrunCount < 3 && m_WarpNavs[m_WarpNavIndex+1] == 0 && (warp_nav_distance < m_Velocity)) //ensure we reach the target the tick after next
				{
					//we want to appear about 100.0f away from the target
					//warp_nav_distance - 100 = dist to target dest.
					m_Velocity = (warp_nav_distance - 100.0f);
					m_DistToNav = warp_nav_distance;
					m_OverrunCount = 3;
					return;
				}*/

				//if we're faster than about 3000, then we want to reduce speed as we approach the nav.
				if (m_Velocity > 3000.0f && warp_nav_distance < m_Velocity * 3.0f)
				{
					m_Velocity = m_Velocity * multiplier; //gradually slow down for navs
				}

				if (warp_nav_distance > m_DistToNav || warp_nav_distance < 500.0f)
				{
					om->Explored(this, obj); //force exploration of nav
					//m_OverrunCount = 0;
					m_DistToNav = 1e6;
					UpdateWarpNavigation();
				}
				else
				{
					m_DistToNav = warp_nav_distance;
				}
			}
		}
		else
		{
			TerminateWarpGroup();
		}
	}
    else
    {
        if (GetNet7TickCount() > m_WarpTime)
        {
            TerminateWarpGroup();
        }
    }
}

void Player::TerminateWarpGroup(bool player_forced)
{
	if (g_ServerMgr->m_PlayerMgr.CheckGroupFormation(this))
	{
		if (PlayerIndex()->GroupInfo.GetIsGroupLeader())
		{
			// only group leader can terminate all warps
			for(int x=0;x<6;x++)
			{
				int PlayerID = g_ServerMgr->m_PlayerMgr.GetMemberID(this->GroupID(), x);
				if (PlayerID > 0)
				{
					Player* pid = g_ServerMgr->m_PlayerMgr.GetPlayer(PlayerID);

					if (g_ServerMgr->m_PlayerMgr.CheckGroupFormation(pid))
					{
						pid->TerminateWarp(player_forced);
					}
				}
			}
		}
		else if (player_forced)
		{
			g_ServerMgr->m_PlayerMgr.LeaveFormation(GameID());
			TerminateWarp(player_forced);
		}
	}
	else
	{
		TerminateWarp(player_forced);
	}
}

void Player::TerminateWarp(bool player_forced)
{
    if (m_WarpDrive)
    {
        if (player_forced)
        {
            CalcNewPosition(GetNet7TickCount());
        }
        SendEndWarpBroadcast();
        m_Velocity = 0;
        SendLocationAndSpeed(true);
        SendWarpIndex(-1);
        RemobilisePlayer();
	    ShipIndex()->SetPrivateWarpState(PRIVATE_WARP_TARGET_REACHED);
	    ShipIndex()->SetGlobalWarpState(GLOBAL_WARP_END_WARP);
		ShipIndex()->SetWarpTriggerTime(GetNet7TickCount() + (u32)m_Stats.GetStat(STAT_WARP_RECOVERY)); //TODO: Get warp reset time from ship index
	    ShipIndex()->SetEngineThrustState(0);
        SendAuxShip();

		//check arrival nav for talk tree mission
		ObjectManager *om = GetObjectManager();
		if (om)
		{
			Object *obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
			if (obj && obj->GetUsedInMission() && obj->RangeFrom(Position()) <= 5000.0f)
			{
				CheckMissions(obj->GetDatabaseUID(), 1, obj->GetDatabaseUID(), TALK_SPACE_NPC);
			}
		}

        if (player_forced && m_WarpTime > 0)
        {
            m_WarpTime = 0;
        }

		SectorManager *sm = GetSectorManager();
        if (sm)
        {
            sm->AddTimedCall(this, B_WARP_RESET, (u32)m_Stats.GetStat(STAT_WARP_RECOVERY), NULL); //TODO: Get warp reset time from ship index
        }
		if (m_WarpCharing)
		{
			RechargeReactor();
		}
		else
		{
			RechargeReactor(m_WarpDrain);
		}
		CheckNavs();
        UpdateVerbs();
        m_SetUpdate = 1;
        m_WarpDrive = false;
    }

    SavePosition();
}

void Player::FinalWarpReset()
{
    ShipIndex()->SetPrivateWarpState(PRIVATE_WARP_AVAILABLE); 
	SendAuxShip();
}

float Player::BaseEnergyRecharge()
{
	float energy = m_Stats.GetStat(STAT_ENERGY);
	float recharge = m_Stats.GetStat(STAT_ENERGY_RECHARGE);
	if (energy != 0.0f)
		return (recharge / energy) / 1000.0f;
	return 0.0f;
}

float Player::BaseShieldRecharge()
{
	float shield = m_Stats.GetStat(STAT_SHIELD);
	float recharge = m_Stats.GetStat(STAT_SHIELD_RECHARGE);
	if (shield != 0.0f)
		return (recharge / shield) / 1000.0f;
	return 0.0f;
}

float Player::GetEnergy()
{
	u32 myTime = GetNet7TickCount();

	if (m_LastReactorChange == 0)
	{
		m_LastReactorChange = myTime;
	}

	// Dont update when dead
	if (ShipIndex()->GetIsIncapacitated())
	{
		m_LastReactorChange = myTime;
	}

    u32 timeElapsed = myTime - m_LastReactorChange;

	if (myTime > ShipIndex()->Energy.GetEndTime())
	{
		timeElapsed = ShipIndex()->Energy.GetEndTime() - m_LastReactorChange;
	}

	float myEnergy = ShipIndex()->Energy.GetStartValue() + (float)(timeElapsed) * ShipIndex()->Energy.GetChangePerTick();

	if (myEnergy > 1.0f)
	{
		myEnergy = 1.0f;
	}
	else if (myEnergy < 0.0f)
	{
		myEnergy = 0.0f;
	}

	//LogMessage("Energy: %f\n",myEnergy);

	return (myEnergy);
}

float Player::GetEnergyValue()
{
	return ((GetEnergy() * ShipIndex()->GetMaxEnergy()));
}

float Player::DrainReactor(unsigned long DrainTime, float EnergyUsed)
{
	float drainInstance = -1.0f;

	if (DrainTime > 0) 
	{
		float myEnergy = GetEnergy();
		float energy_ratio = EnergyUsed/GetMaxEnergy();
		float drainRate = energy_ratio / (float)DrainTime;
		float current_drain = ShipIndex()->Energy.GetChangePerTick();
		drainInstance = drainRate;
		//now factor in existing drain
		if (current_drain < 0.0f)
		{
			if (drainInstance == 0.0f)
				return drainInstance; // no change required
			drainRate -= current_drain;
		}
		//need to extrapolate the time for the energy used back up to the full bar
		if (drainRate != 0.0f)
			DrainTime = (unsigned long)(myEnergy / drainRate);
		EnergyUpdate(GetNet7TickCount() + DrainTime, -drainRate, myEnergy);
		//LogMessage("Drain rate: %f New Drain: %f\n", current_drain, -drainRate);
	}
	else
	{
		LogDebug("Drain Reactor used with Draintime = 0\n");
		RemoveEnergy(EnergyUsed);
	}

	return drainInstance;
}

// also called by energy leech with -ve value to add energy
// use the 'recharge time' to delay charging back for a while
void Player::RemoveEnergy(float EnergyRemoved, long recharge_time)
{
	unsigned long endTime;
	float maxEnergy = GetMaxEnergy();
	if (maxEnergy == 0.0f)
		return;

	//myEnergy is the reactor energy after we remove power
	float myEnergy = GetEnergy() - (EnergyRemoved / maxEnergy);

	// easier and safer to rangecheck here
	if (myEnergy < 0.0f)
	{
		myEnergy = 0.0f;
	}
	else if (myEnergy > 1.0f)
	{
		myEnergy = 1.0f;
	}

	float ChangePerTick = ShipIndex()->Energy.GetChangePerTick();
	if (ChangePerTick > 0.0f)
	{
		if (EnergyRemoved > 0.0f && recharge_time > 0)
		{
			endTime = 0;
			ChangePerTick = 0;
			m_EnergyRecharge = GetNet7TickCount() + recharge_time;
		}
		else
		{
			endTime = (unsigned long)((1.0f - myEnergy) / ChangePerTick);
		}
	}
	else if (ChangePerTick < 0.0f)
	{
		endTime = 0 - (unsigned long)(myEnergy / ChangePerTick);
	}
	else
	{
		endTime = 0;
	}
	
	// Dont regen if Incapacited
	if (ShipIndex()->GetIsIncapacitated())
    {
		// Stop Regen
		EnergyUpdate(0, 0, myEnergy);
	}
	else
	{
		// Start regen if we are revived
		if (BaseEnergyRecharge() == 0)
		{
			ShipIndex()->Energy.SetChangePerTick(BaseEnergyRecharge());
		}

		EnergyUpdate(GetNet7TickCount() + endTime, ChangePerTick, myEnergy);
	}
}

void Player::RechargeReactor(float drain_relief)
{
	float Energy = GetEnergy();
	unsigned long RequiredTime = GetNet7TickCount();
	float Recharge = BaseEnergyRecharge();
	float current_charge = ShipIndex()->Energy.GetChangePerTick();

	if (drain_relief != -1.0f)
	{
		current_charge += drain_relief;
		if (current_charge > (-0.0000001f) /* allow for floating pt math inaccuracies */ )
		{
			current_charge = Recharge; //reactor can charge now
		}
	}
	else
	{
		current_charge = Recharge; //force reactor charge
	}

	if (current_charge > 0.0f)
	{
		RequiredTime += (unsigned long)((1.0f - Energy) / current_charge); //required time to empty reactor
	}
	else if (current_charge < 0.0f)
	{
		RequiredTime += (unsigned long)(Energy / -current_charge); //required time to empty reactor
	}
	
	// Dont regen if Incapacited
	if (!ShipIndex()->GetIsIncapacitated())
	{
		EnergyUpdate( RequiredTime, current_charge, Energy);
		m_EnergyRecharge = 0;
	}
}

void Player::EnergyUpdate(unsigned long EndTime, float ChangePerTick, float StartValue)
{
	if (m_CurrentSkill && StartValue <= 0.0f)
		m_CurrentSkill->InterruptSkillOnAction(OTHER);

	//LogMessage("EnergyUpdate - CurTime: %x EndTime: %x, Change: %f, Start: %f\n", GetNet7TickCount(), EndTime, ChangePerTick, StartValue);
    m_LastReactorChange = GetNet7TickCount();
    ShipIndex()->Energy.SetEndTime(EndTime);
    ShipIndex()->Energy.SetChangePerTick(ChangePerTick);
    ShipIndex()->Energy.SetStartValue(StartValue);

    SendAuxShip();
}

void Player::RadiationDmg(bool enb)
{
	if (enb && !m_Buffs.FindBuff("Environment_Shield"))
	{
		if (m_EnvStats[0] == 0)
		{
			// Set stats to degen
			m_EnvStats[0] = m_Stats.SetStat(STAT_DEBUFF_VALUE, STAT_SHIELD_RECHARGE, 15, "ENV_DMG");
			//m_EnvStats[1] = m_Stats.SetStat(STAT_DEBUFF_VALUE, STAT_ENERGY_RECHARGE, 15, "ENV_DMG");

			/*
			 *TODO: Find an add the effect
			 */
			/*
			// Effect Information
			ObjectEffect obj_effect;
			// Send Effect
			obj_effect.Bitmask = 3;
			obj_effect.GameID = GameID();
			obj_effect.TimeStamp = GetNet7TickCount();
			obj_effect.Duration = 0;
			obj_effect.EffectDescID = AddBuff->EffectID[i];
			m_EnvEffects[0] = m_Effects.AddEffect(&obj_effect);
			*/

			// Make sure we degen reactor and shields
			//RechargeReactor();
			RechargeShield();
		}
	}
	else
	{
		if (m_EnvStats[0] != 0)
		{
			m_Stats.DelStat(m_EnvStats[0]);
			//m_Stats.DelStat(m_EnvStats[1]);
			//m_Effects.RemoveEffect(m_EnvEffects[0]);
			//RechargeReactor();
			RechargeShield();
			m_EnvStats[0] = 0;
			m_EnvStats[1] = 0;
			m_EnvEffects[0] = 0;
			SetRadiation(-1);
		}
	}
}

//use this method to halt various activities. There's lots that doesn't need to go into the time slot system
//devices can go onto the normal time queue because usage is not so intense.
void Player::CheckEventTimes(unsigned long current_tick)
{
	if (PlayerIndex()->GetSectorNum() > 9999)
	{
		//station events go here
		if (m_BroadcastPositionTick > 0 && m_BroadcastPositionTick < current_tick)
		{
			BroadcastPosition();
			m_BroadcastPositionTick = 0;
		}
	}
	else
	{
		//space events go here
		// Dont regen if Incapacited
		if (ShipIndex()->GetIsIncapacitated())
		{
			return;
		}

		if (m_ShieldRecharge > 0 && m_ShieldRecharge < current_tick)
		{
			RechargeShield();
		}

		if (m_EnergyRecharge > 0 && m_EnergyRecharge < current_tick)
		{
			RechargeReactor(0.0f);
		}

		//delay tractoring next ore until current tractor is complete
		if (m_ProspectBeamNode.player_id !=0 && m_ProspectBeamNode.event_time < current_tick && m_ProspectTractorNode.player_id == 0)
		{
			m_ProspectBeamNode.player_id = 0;
			//need to cancel the prospect beam effect now (doesn't matter if Net7Proxy re-cancels it later)
			RemoveEffectRL(m_ProspectBeamNode.i3);
			PullOreFromResource(m_ProspectBeamNode.obj, m_ProspectBeamNode.i1, m_ProspectBeamNode.i2, m_ProspectBeamNode.i3, m_ProspectBeamNode.i4);
		}

		if (m_ProspectTractorNode.player_id !=0)
		{
			if(m_ProspectTractorNode.event_time < current_tick)
			{
				//timeout
				m_ProspectTractorNode.player_id = 0;
				TakeOreOnboard(m_ProspectTractorNode.obj, m_ProspectTractorNode.i3, m_ProspectTractorNode.i4 != 0);
			}
			else
			{
				unsigned long now = GetNet7TickCount();
				unsigned long dT = now-m_ProspectTractorNode.i1;
				m_ProspectTractorNode.i1 = now;
				float sX=PosX();
				float sY=PosY();
				float sZ=PosZ();
				float vX= sX - m_TractorItemLocation[0];
				float vY= sY - m_TractorItemLocation[1];
				float vZ= sZ - m_TractorItemLocation[2];
				float vMag = sqrtf(powf(vX,2)+powf(vY,2)+powf(vZ,2));
				float tMag = ((float)m_ProspectTractorNode.a/1000.0f) * dT;
				float tractorSpeed = min(vMag,tMag);
				m_TractorItemLocation[0] += (vX/vMag) * tractorSpeed;
				m_TractorItemLocation[1] += (vY/vMag) * tractorSpeed;
				m_TractorItemLocation[2] += (vZ/vMag) * tractorSpeed;
				if(m_TractorItemLocation[0] == sX &&
					m_TractorItemLocation[1] == sY &&
					m_TractorItemLocation[2] == sZ)
				{
					//scooped up :)
					m_ProspectTractorNode.player_id=0;
					TakeOreOnboard(m_ProspectTractorNode.obj,m_ProspectTractorNode.i3, m_ProspectTractorNode.i4 != 0);
				}
			}
		}

		if (m_WarpBroadcastTime > 0 && m_WarpBroadcastTime < current_tick)
		{
			StartWarp();
		}

		m_Buffs.CheckBuffExpire(current_tick);

		// check weapons waiting to fire
		for (int i=0;i < m_WeaponSlots;i++)
			m_Equip[3+i].CheckAutoActivate();
	}
}

///////////////////////////////////////////////////
// Range List Handling
//
// These methods handle adding to and removing from
// the player range lists.

void Player::UpdatePlayerVisibilityList()
{
    Player *p = 0;
    u32 * sector_list = GetSectorPlayerList();
    bool in_range;
    float player_range;
    bool group_member;
	bool DistressBeacon = ShipIndex()->GetIsRescueBeaconActive();
	long signature = ShipIndex()->CurrentStats.GetVisibility();
	if (signature < 0) signature = 0;

	if (!InSpace() || m_Gating || !sector_list || HasScanInvisibility() || Hijackee())
		return;

    //TODO: 
    //      - Only send the player info once (use bitfield to indicate playerinfo sent).
    //      - Add grid system to further reduce checking

	while (g_PlayerMgr->GetNextPlayerOnList(p, sector_list))
	{
		if (p != this && p->InSpace() && !p->m_Gating)
		{
            //get range and find out if this player is already on the list
            player_range = p->RangeFrom(this);
            in_range = PlayerInRangeList(p);

			group_member = g_ServerMgr->m_PlayerMgr.CheckGrouped(this, p);
			
            if (in_range) 
            { 
                //see if we've gone out of their scan range
                if ((!DistressBeacon && !group_member) && (player_range > (float)(p->ShipIndex()->CurrentStats.GetScanRange() + signature) + 200.0f)) //200.0f stops 'ratcheting'
                {
					RemovePlayerFromRangeList(p);
					m_Effects.SendRemoveEffects(p);
                }
            }
            else
            {
                //see if we've come into their scan range
                if (DistressBeacon || group_member || player_range < (float)(p->ShipIndex()->CurrentStats.GetScanRange() + signature) && !GetIsCloaked())
                {
                    AddPlayerToRangeList(p, group_member);
					m_Effects.SendEffects(p);
                }
            }
		}
	}
}

bool Player::PlayerInRangeList(Player *p_check)
{
	return (g_PlayerMgr->GetIndex(p_check, m_RangeList));
}

//this means that player 'p' can no longer see what this player does
void Player::RemovePlayerFromRangeList(Player *p)
{
	if (g_PlayerMgr->GetIndex(p, m_RangeList))
	{
		g_PlayerMgr->UnSetIndex(p, m_RangeList);
		g_PlayerMgr->UnSetIndex(p, m_ClickedList);
		if (!p->DebugPlayer())
		{
			p->RemoveObject(GameID());
			if (g_Debug && p->AdminLevel() == 90)
			{
				p->SendVaMessage("%s has just gone out of your scan range.", Name());
			}
		}
		else if (g_Debug && p->AdminLevel() == 90)
		{
			SendVaMessage("We've just gone out of scan range of %s.", p->Name());
		}
	}
}

//This means that player 'p' can now see anything that this player does.
void Player::AddPlayerToRangeList(Player *p, bool is_group_member)
{
	//see if we've already sent this player
	if (g_PlayerMgr->GetIndex(p, m_RangeList))
	{
		//we already sent this player
		return;
	}
	g_PlayerMgr->SetIndex(p, m_RangeList);
	g_PlayerMgr->UnSetIndex(p, m_ClickedList);

	if (!p->DebugPlayer() && p->GameID() != GameID())
	{
		if (g_Debug && p->AdminLevel() == 90)
		{
			p->SendVaMessage("%s just came into your scan range.", Name());
		}
		SendShipData(p, is_group_member); //send our ship to 'p' - we can cut this down by separating out some data into the 'OnTargetted' method
	}
	else if (g_Debug && p->AdminLevel() == 90 && !DebugPlayer())
	{
		SendVaMessage("We just came into scan range of %s.", p->Name());
	}
}

//Send object to all players who can see us
void Player::SendToVisibilityList(bool include_player)
{
	Player *p = (0);

	if (m_SendDockPos && !m_SentDockPos)
	{
		m_SentDockPos = true;
	}
	else if (m_SentDockPos)
	{
		return; //don't update position
	}

	//sort out 'this' player first if we need to
	if (include_player)
	{
		if (!g_ServerMgr->m_PlayerMgr.CheckGroupFormation(this))
		{
			SendAdvancedPositionalUpdate(GameID(), &m_Position_info);
		}
		else if (g_ServerMgr->m_PlayerMgr.CheckGroupFormation(this))
		{
			// Send formation info
			g_ServerMgr->m_PlayerMgr.SendFormation(this, this);
		}
	}

    long update = m_Position_info.UpdatePeriod; //this could smooth out player movement a little.
    m_Position_info.UpdatePeriod = 100;
	m_Position_info.RotZ *= 0.1f;

	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))
	{
		if (!IsInSameSector(p)) 
		{
			g_PlayerMgr->UnSetIndex(p, m_RangeList);
		}
		else if (p != this)
		{
			if (m_ReceivedMovement && p->Active() && !p->DebugPlayer() && !g_ServerMgr->m_PlayerMgr.CheckGroupFormation(p))
			{
				p->SendAdvancedPositionalUpdate(GameID(), &m_Position_info);
			} 
			else if (g_ServerMgr->m_PlayerMgr.CheckGroupFormation(p))
			{
				// Send formation info
				g_ServerMgr->m_PlayerMgr.SendFormation(this, p);
			}	
		}
	}

	m_Position_info.RotZ *= 10.0f;
    m_Position_info.UpdatePeriod = update;

    m_ReceivedMovement = false;
}

void Player::RemoveFromAllSectorRangeLists()
{
    RangeListVec::iterator itrRList;
    Player *p;
    long sector_id = PlayerIndex()->GetSectorNum();

    if (sector_id == 0)
    {
        return;
    }

    u32 * sector_list = GetSectorPlayerList();
	SectorManager *sect_manager = g_ServerMgr->GetSectorManager(sector_id);
    ObjectManager *obj_manager = GetObjectManager();

    if (m_PlayerIndex.GetSectorNum() <= 9999) //in space
    {
		if (Hijackee() != 0)
		{
			//release player from hijackee
			HandleReleaseHijack();
		}

		p = (0);
		// Send the Remove packet to everyone that can see us
		while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))
		{
            if (p->Active())
            {
                p->RemoveObject(GameID());
            }
        }

        if (sector_list)
        {
			p = (0);
            while (g_PlayerMgr->GetNextPlayerOnList(p, sector_list))
			{
                if (p != this)
                {
                    p->RemovePlayerFromRangeList(this);
                }
            }
        }
        
        if (obj_manager)
        {
            obj_manager->RemovePlayerFromMOBRangeLists(this);
        }

		BlankRangeList();
    }
    else
    {
        m_Oldroom = m_Room;
        m_Room = -1;
        SendStarbaseAvatarList();
    }

    //now remove from the sector list itself
    if (sect_manager) sect_manager->RemovePlayerFromSectorList(this);

	SaveHullLevelChange(ShipIndex()->GetMaxHullPoints());
	SaveEnergyLevels();
}

////////////////////////////////////////////////////
//
// These methods traverse the player visibility lists
// And send FX packets to all ships that can see us
// TODO: most of these could be bunched into one call

void Player::SendTractorComponentRL(Object *obj, float decay, float tractor_speed, long article_id, long effect_id, long timestamp)
{
	Player *p = (0);
	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))	
	{
        p->CreateTractorComponent(obj->Position(), decay, tractor_speed, GameID(), article_id, effect_id, timestamp);
	}
}

void Player::SendResourceNameRL(long article_UID, char *raw_name)
{
	Player *p = (0);
	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))	
	{
        p->SendResourceName(article_UID, raw_name);
    }
}

void Player::SendResourceRemoveRL(long article_UID)
{
	Player *p = (0);
	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))	
	{
        p->RemoveObject(article_UID);
	}
}

void Player::SendContrailsRL(bool contrails)
{
	Player *p = (0);
	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))	
	{
        p->Contrails(GameID(), contrails);
	}

	if (contrails == false)
	{
		//now set velocity zero, as we've stopped if 
		SetVelocity(0);
	}
}

void Player::SendEffectRL(long target_id, char *message, long effect_type, long effectUID, long timestamp, short duration)
{
	Player *p = (0);
	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))	
	{
        p->ActivateProspectBeam(GameID(), target_id, message, (short)effect_type, effectUID, timestamp, duration);
	}
}

//Sometimes, we might need to send an effect to a specific player only
void Player::SendEffect(ObjectEffect *obj_effect)
{
	unsigned char effect[128];
	memset(effect,0,128);
    int index = 0;

    AddData(effect, obj_effect->Bitmask, index);
    AddData(effect, obj_effect->GameID, index);
    AddData(effect, obj_effect->EffectDescID, index);

	if (obj_effect->Bitmask & 0x01)
	{
        AddData(effect, obj_effect->EffectID, index);
	}
	if (obj_effect->Bitmask & 0x02)
	{
        if (obj_effect->TimeStamp == 0)
        {
            obj_effect->TimeStamp = GetNet7TickCount();
        }

        AddData(effect, obj_effect->TimeStamp, index);
	}
	if (obj_effect->Bitmask & 0x04)
	{
        AddData(effect, obj_effect->Duration, index);
	}
	if (obj_effect->Bitmask & 0x08)
	{
        AddData(effect, obj_effect->Scale, index);
	}
	if (obj_effect->Bitmask & 0x10)
	{
        AddData(effect, obj_effect->HSVShift[0], index);
	}
	if (obj_effect->Bitmask & 0x20)
	{
        AddData(effect, obj_effect->HSVShift[1], index);
	}
	if (obj_effect->Bitmask & 0x40)
	{
        AddData(effect, obj_effect->HSVShift[2], index);
	}

	SendOpcode(ENB_OPCODE_0009_OBJECT_EFFECT, effect, index);
}

void Player::SendToRangeList(short opcode, unsigned char *data, size_t length, bool weapon_fire)
{
	Player *p = (0);
	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))	
	{
		if (weapon_fire)
		{
			long player_id = *((long*) &data[2]);
			if (m_WeaponsPerTick < MAX_WEAPON_FIRE_PER_TICK || player_id == this->GameID()) //always ensure player's own weapons are shown
			{
				p->SendOpcode(opcode, data, length);
				m_WeaponsPerTick++;
			}
		}
		else
		{
			p->SendOpcode(opcode, data, length);
		}
	}
}

void Player::SendToGroup(short opcode, unsigned char *data, size_t length)
{
	Player *p = (0);
	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))	
	{
        if (p == this || g_ServerMgr->m_PlayerMgr.CheckGrouped(this, p) )
        {
            p->SendOpcode(opcode, data, length);
        }
	}
}

void Player::SendToSector(short opcode, unsigned char *data, size_t length) //this method is a dumb send to everyone in the sector
{
    u32 * sector_list = GetSectorPlayerList();
	Player *p = (0);
	
    while (g_PlayerMgr->GetNextPlayerOnList(p, sector_list))
	{
		p->SendOpcode(opcode, data, length);
    }
}

void Player::SendObjectCreateRL(long article_UID, float scale, short basset, int type)
{
	Player *p = (0);
	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))	
	{
        p->SendCreate(article_UID, scale, basset, type, 0.0f, 0.0f, 0.0f);			
	}
}

void Player::SendRelationshipRL(long article_UID, long relationship, long is_attacking)
{
	bool b_is_attacking = (is_attacking == 1) ? true : false;
	
	Player *p = (0);
	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))	
	{
        p->SendRelationship(article_UID, relationship, b_is_attacking);
	}
}

bool Player::GetInGroupFormation()
{
	if (m_Group)
	{
		return g_ServerMgr->m_PlayerMgr.CheckGroupFormation(this);
	}
	return false;
}

Player* Player::GetGroupLeader()
{
	Player *leader = (0);
	if (m_Group)
	{
		long leaderID = g_ServerMgr->m_PlayerMgr.GetMemberID(m_GroupID, 0);
		leader = g_ServerMgr->m_PlayerMgr.GetPlayer(leaderID);
	}
	return leader;
}

// put this back on the stack again.
// Server should avoid new/delete cycles if it wants to stay working
// We can now allocate a persistant heap space for these messages.
void Player::SendVaMessage(char *string, ...)
{
    unsigned int len = strlen(string) + 256;
    char *pch = (char*)_alloca(len);
    
    va_list args;
    va_start(args, string);
    vsprintf_s(pch, len, string, args);
    SendMessageString(pch);
    va_end(args);
}

void Player::SendVaMessageS(const char *string)
{
    unsigned int len = strlen(string) + 1;
    char *pch = (char*)_alloca(len);

    sprintf_s(pch, len, string);
    SendMessageString(pch);
}

//Variant of SendMessage which lets you use a colour

//17 - red
//13 - white
//12 - light green
//11 - dark blue
//10 - cyan
void Player::SendVaMessageC(char colour, char *string, ...)
{
    unsigned int len = strlen(string) + 256;
    char *pch = (char*)_alloca(len);
    
    va_list args;
    va_start(args, string);
    vsprintf_s(pch, len, string, args);
    SendMessageString(pch, colour);
    va_end(args);
}

float Player::TractorRange()
{
	
    //return (1000 + PlayerIndex()->RPGInfo.GetHullUpgradeLevel() * 250);
	return m_Stats.GetStat(STAT_TRACTORBEAM_RANGE);
}

long Player::ProspectRange()
{
    return (750 + (PlayerIndex()->RPGInfo.Skills.Skill[SKILL_PROSPECT].GetLevel() * 250));
}

void Player::BlankVerbs()
{
    memset(m_Verbs, 0, sizeof(m_Verbs));
}

bool Player::AddVerb(short verb_id, float verb_activate_range)
{
    bool success = false;
	//check we haven't already got this verb, if we have, update the activation range
    for (int i = 0; i < 4; i++)
    {
        if (m_Verbs[i].verb_id == verb_id)
        {
			m_Verbs[i].activate_range = verb_activate_range;
            return true;
        }
    }

    for (int i = 0; i < 4; i++)
    {
        if (m_Verbs[i].verb_id == 0)
        {
            m_Verbs[i].verb_id = verb_id;
            m_Verbs[i].activate_range = verb_activate_range;
            m_Verbs[i].active = false;
            success = true;
            break;
        }
    }

    return success;
}

void Player::UpdateVerbs(bool force_update)
{
	unsigned char verb_update[100];
	int index = 0;
	long drops = 0;
    bool changed = force_update;
    short default_attribute = ATTRIBUTE_DIS_TOOFAR;
    short attribute;
	int i;

	ObjectManager *om = GetObjectManager();

    Object *obj = (0);
	
	if (om) obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());

    if (!obj)
    {
        return;
    }

    float target_range = RangeFrom(obj);

    for (i = 0; i < 4; i++)
    {
        if (m_Verbs[i].verb_id > 0)
        {
            drops++;
            if ((target_range <= m_Verbs[i].activate_range || m_Verbs[i].activate_range == 0.0f) &&
                m_Verbs[i].activate_range != -1.0f) //in_range
            {
                if (!m_Verbs[i].active)
                {
                    m_Verbs[i].active = true;
                    changed = true;
                }
            }
            else
            {
                if (m_Verbs[i].active)
                {
                    m_Verbs[i].active = false;
                    changed = true;
                    CloseInterfaceIfRequired(m_Verbs[i].verb_id);
                }
            }
        }
    }

    if (changed)
    {
        AddData(verb_update, ntohl(obj->GameID()), index);
        AddData(verb_update, ntohl(drops), index);

        for (i = 0; i < 4; i++)
        {
            if (m_Verbs[i].verb_id > 0)
            {
                AddData(verb_update, default_attribute, index);
                AddData(verb_update, m_Verbs[i].verb_id, index);
            }
        }

        AddData(verb_update, ntohl(drops), index);

        for (i = 0; i < 4; i++)
        {
            if (m_Verbs[i].verb_id > 0)
            {
                attribute = m_Verbs[i].active ? ATTRIBUTE_ENABLE : ATTRIBUTE_DIS_TOOFAR;
                AddData(verb_update, attribute, index);
                AddData(verb_update, m_Verbs[i].verb_id, index);
            }
        }
        
        SendOpcode(ENB_OPCODE_005C_VERB_UPDATE, verb_update, index);
    }
}

void Player::CloseInterfaceIfRequired(short verb_id)
{
    switch (verb_id)
    {
    case VERBID_LOOT:
    case VERBID_PROSPECT:
        CloseInterfaceIfOpen();
        break;
    case VERBID_TRADE: //todo: close trade window
        break;
    }
}

//Player 'player' is targetting this player
void Player::OnTargeted(Player *player)
{
    if (player != this)
    {
        player->BlankVerbs();
        if (ShipIndex()->GetIsIncapacitated()) //if the targetted player is incapacitated
        {
            player->AddVerb(VERBID_TRADE, -1.0f);
            player->AddVerb(VERBID_GROUP, -1.0f);
            if (player->Database()->ship_data.profession == PROFESSION_EXPLORER &&
                player->m_PlayerIndex.RPGInfo.Skills.Skill[SKILL_JUMPSTART].GetLevel() > 0 )
            {
				// add in range as skill goes up
				int Range = (player->m_PlayerIndex.RPGInfo.Skills.Skill[SKILL_JUMPSTART].GetLevel() - 1) * 250;
                player->AddVerb(VERBID_JUMPSTART, 1000.0f + Range);
            }
        }
        else if (player->ShipIndex()->GetIsIncapacitated()) //player doing the clicking is incapacitated
        {
            player->AddVerb(VERBID_TRADE, -1.0f);
            player->AddVerb(VERBID_GROUP, -1.0f);
            player->AddVerb(VERBID_FOLLOW, -1.0f);
        }
        else
        {
            player->AddVerb(VERBID_TRADE, 1000.0f);
            player->AddVerb(VERBID_GROUP, 0.0f);
            player->AddVerb(VERBID_FOLLOW, 1500.0f);
        }

        player->AddVerb(VERBID_MESSAGE, 0.0f);

        if (!IsClickedBy(player))
        {
            SetClickedBy(player);
			SendClickPacketTo(player);
        }
    } 
    else 
    { 
		// We can Jumpstart ourselves
		if (Database()->ship_data.profession == PROFESSION_EXPLORER &&
			player->m_PlayerIndex.RPGInfo.Skills.Skill[SKILL_JUMPSTART].GetLevel() > 0 &&
			ShipIndex()->GetIsIncapacitated())
		{
			BlankVerbs();
			AddVerb(VERBID_JUMPSTART, 1000.0f);
		}
	}
}

void Player::SetDistressBeacon(bool Status)
{
	ShipIndex()->SetIsRescueBeaconActive(Status);
	//BlankRangeList();
	//all ships in sector will now be sent your ship with distress beacon toggled on
}

void Player::BlankRangeList(bool group)
{	
	Player *p = (0);
	bool group_member = false;
	while (g_PlayerMgr->GetNextPlayerOnList(p, m_RangeList))	
	{
		if (group)
		{
			group_member = g_ServerMgr->m_PlayerMgr.CheckGrouped(this, p);
		}
		if (p->GameID() != GameID() && !group_member)
		{
			p->RemoveObject(GameID());
			g_PlayerMgr->UnSetIndex(p, m_RangeList);
			g_PlayerMgr->UnSetIndex(p, m_ClickedList);
		}
	}

	if (!group)
	{
		memset(&m_RangeList, 0, sizeof(m_RangeList));
		AddPlayerToRangeList(this);
	}

	if (GetObjectManager())
	{
		GetObjectManager()->RemovePlayerFromMOBRangeLists(this);
	}
}

void Player::TowToBase()
{
	//This needs to start a TCP connection
	SetWormholed(true);
	SectorManager *sm = GetSectorManager();
    if (!sm) return;
    //for now, just transfer to registered starbase.
    long sector_id = m_RegisteredSectorID;//m_SectorMgr->GetSectorIDFromName(PlayerIndex()->GetRegistrationStarbaseSector());

	if (sector_id == 0)
    {
        //class starting sector, so there's always somewhere to go
        sector_id = StartSector[m_Database.avatar.race * 3 + m_Database.avatar.profession];
    }

    if (sector_id > 0)
    {
        long destination = sector_id;
        if (sector_id < 9999) destination = sector_id * 10 + 1;
        SetLoginCamera(0, 0);
        sm->SectorServerHandoff(this, destination);
    }
    else
    {
        SendVaMessage("Invalid Registered Starbase: %d", m_RegisteredSectorID/*PlayerIndex()->GetRegistrationStarbase()*/);
    }
}

void Player::ShipUpgrade(long upgrade)
{
    //first check if hull upgrade is valid
    if ((upgrade - 1) != PlayerIndex()->RPGInfo.GetHullUpgradeLevel())
    {
        SendVaMessage("BUG: Invalid upgrade sent #%d current upgrade [%d]. Report to devs.", upgrade, PlayerIndex()->RPGInfo.GetHullUpgradeLevel());
        LogMessage("BUG: Invalid upgrade sent #%d current upgrade [%d].\n", upgrade, PlayerIndex()->RPGInfo.GetHullUpgradeLevel());
        return;
    }
	
    //verify hull upgrade is valid
    switch (upgrade)
    {
    case 1: //10
        if (TotalLevel() >= 10)
        {
			m_Database.ship_info.hull         = 1 + BaseHullAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.hull];   
			m_Database.ship_info.wing         = BaseWingAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.wing];
			m_Database.ship_info.engine       = BaseEngineAsset[m_Database.ship_data.race];
	        m_Database.ship_info.profession   = BaseProfAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.profession];
        }
		else
		{
			return;
		}
        break;

    case 2: //30
        if (TotalLevel() >= 30)
		{
			ShipIndex()->BaseStats.SetWarpPowerLevel(4);
			ShipIndex()->CurrentStats.SetWarpPowerLevel(4);
			m_Database.ship_info.hull         = 1 + BaseHullAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.hull];			
			m_Database.ship_info.wing         = 1 + BaseWingAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.wing];
			m_Database.ship_info.engine       = 1 + BaseEngineAsset[m_Database.ship_data.race];
			m_Database.ship_info.profession   = BaseProfAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.profession];
		}
		else
		{
            return;
        }
        break;

    case 3: //50
        if (TotalLevel() >= 50)
        {
            //Take the assets from static data for consitency
			m_Database.ship_info.hull         = 1 + BaseHullAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.hull];			
			m_Database.ship_info.wing         = 1 + BaseWingAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.wing];
			m_Database.ship_info.engine       = 1 + BaseEngineAsset[m_Database.ship_data.race];
	        m_Database.ship_info.profession   = 1 + BaseProfAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.profession];
        }
        else
        {
            return;
        }
        break;

    case 4: //75
        if (TotalLevel() >= 75)
        {
			ShipIndex()->BaseStats.SetWarpPowerLevel(5);
			ShipIndex()->CurrentStats.SetWarpPowerLevel(5);
			m_Database.ship_info.hull         = 2 + BaseHullAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.hull];   
			m_Database.ship_info.wing         = 1 + BaseWingAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.wing];
			m_Database.ship_info.engine       = 1 + BaseEngineAsset[m_Database.ship_data.race];
	        m_Database.ship_info.profession   = 1 + BaseProfAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.profession];
        }
		else
		{
			return;
		}
		
        break;

    case 5: //100
        if (TotalLevel() >= 100)
        {
            //Take the assets from static data for consitency	        
			m_Database.ship_info.hull         = 2 + BaseHullAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.hull];   
			m_Database.ship_info.wing         = 2 + BaseWingAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.wing];
			m_Database.ship_info.engine       = 2 + BaseEngineAsset[m_Database.ship_data.race];
	        m_Database.ship_info.profession   = 1 + BaseProfAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.profession];
        }
        else
        {
            return;
        }
        break;

    case 6: //135
        if (TotalLevel() >= 135)
		{
			ShipIndex()->BaseStats.SetWarpPowerLevel(6);
			ShipIndex()->CurrentStats.SetWarpPowerLevel(6);
			m_Database.ship_info.hull         = 2 + BaseHullAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.hull];   
			m_Database.ship_info.wing         = 2 + BaseWingAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.wing];
			m_Database.ship_info.engine       = 2 + BaseEngineAsset[m_Database.ship_data.race];
	        m_Database.ship_info.profession   = 2 + BaseProfAsset[m_Database.ship_data.race * 3 + m_Database.ship_data.profession];
		}
		else
        {
            return;
        }
        break;

    default:
        SendVaMessage("BUG: Invalid upgrade sent #%d. Report to devs.", upgrade);
        return;
        break;
	}

    ShipIndex()->SetHullPoints(float(HullTable[ClassIndex() * 7 + upgrade]));
    ShipIndex()->SetMaxHullPoints(float(HullTable[ClassIndex() * 7 + upgrade]));
	m_Stats.ChangeStat(m_BaseID[ID_MASS], (float)(BaseMass[ClassIndex()]+upgrade*5));
	m_Stats.ChangeStat(m_BaseID[ID_TURN_RATE], (float)(150-(BaseManeuver[ClassIndex()]-upgrade*5)));
	m_Stats.ChangeStat(m_BaseID[ID_TRACTORBEAM_RANGE],1000.0f + 250.0f*upgrade);

    //do we add a weapon?
    if (WeaponTable[ClassIndex() * 7 + upgrade] != 0)
    {
        AddWeapon(m_WeaponSlots + 1);
    }

    //always add 2 to cargo slots
    long slots = ShipIndex()->Inventory.GetCargoSpace();

    if (slots <= 38)
    {
        ShipIndex()->Inventory.SetCargoSpace(slots+2);
        ShipIndex()->Inventory.CargoInv.Item[slots].SetItemTemplateID(-1);
        ShipIndex()->Inventory.CargoInv.Item[slots+1].SetItemTemplateID(-1);
    }

    //add device slot if required
    if (DeviceTable[ClassIndex() * 7 + upgrade] != 0)
    {
        //see how many devices we have. Perhaps we could track this?
        short devices = 0;
        for (int i = 0; i < upgrade; i++)
        {
            devices += DeviceTable[ClassIndex()*7 + i];
        }

        ShipIndex()->Inventory.Mounts.SetMount(9+devices, DeviceMount);
		ShipIndex()->Inventory.EquipInv.EquipItem[9+devices].SetItemTemplateID(-1);
		m_DeviceSlots++;
    }

    PlayerIndex()->RPGInfo.SetHullUpgradeLevel(upgrade);


	SaveHullUpgrade();
	SaveHullLevelChange(ShipIndex()->GetMaxHullPoints());
	
	// send new asset to yourself and everyone around
	RemoveObject(GameID());
	SendShipData(this);
	SendAuxShipExtended();
    SendAuxPlayer();
	SaveDatabase();
}

void Player::ResetRangeLists()
{
    //memset(m_ObjRangeList, 0, MAX_OBJS_PER_SECTOR/8);
    memset(m_ResourceSendList, 0, MAX_OBJS_PER_SECTOR/8);
}

void Player::FinishLogin(bool udp)
{       
    long entry_point_id = 0x6C;
    long camera_type = 0;
    Object *came_from = 0;

	ObjectManager *om = GetObjectManager();
    
	if (om)
	{
		if (FromSector() > 9999)
		{
			came_from = om->FindStation(0);
			if (came_from)
			{        
				entry_point_id = ntohl(came_from->GameID());
			}
			else
			{
				LogMessage("Sector Login - Failed to find entry point!\n");
				entry_point_id = 0;
			}
		}
		else if (FromSector() > 900)
		{
			camera_type = 0x02000000;
			entry_point_id = 0x00;
			came_from = om->FindGate(FromSector());
			if (came_from) came_from->BlipGate(GameID());
		}
	}
    
    //player->SendCameraControl(camera_type, entry_point_id);
    SetLoginCamera(camera_type, entry_point_id);
    CheckNavs();
	
	SetInSpace(true);

	SetWormholed(false);

	//player->m_Effects.SendEffects(player);

    //LogMessage("SECTOR login finish\n");

    // TODO: send ClientChatEvent to enable the sector channel
}

void Player::ResetCombatTrance()
{
	if((Race() == RACE_PROGEN) &&
	   (Profession() == PROFESSION_WARRIOR) &&
	   (PlayerIndex()->RPGInfo.Skills.Skill[SKILL_COMBAT_TRANCE].GetLevel() > 0))
	{
		if(m_CombatTrance)
		{
			m_CombatTrance->Use(0);
		}
	}
}
void Player::RefreshCombatTrance()
{
	if((Race() == RACE_PROGEN) &&
	   (Profession() == PROFESSION_WARRIOR) &&
	   (PlayerIndex()->RPGInfo.Skills.Skill[SKILL_COMBAT_TRANCE].GetLevel() > 0))
	{
		if(m_CombatTrance)
		{
			m_CombatTrance->Update(0);
		}
	}
}
