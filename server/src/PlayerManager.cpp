// PlayerManager.cpp
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
#include "PlayerManager.h"
#include "SectorManager.h"
#include <net7/Opcodes.h>
#include <net7/PacketStructures.h>
#include <float.h>
#include "UDPConnection.h"
#include "MemoryHandler.h"
#include "ObjectClass.h"
#include "PlayerClass.h"
#include "ServerManager.h"
#include "StaticData.h"

// Entry point handed to pthread_create for the per-player login loop.
// (RunMovementThread is a per-tick helper called from elsewhere, not a
// standalone thread main.)
void * RunLoginThreadAPI(void *arg)
{
    PlayerManager::MParam * param = (PlayerManager::MParam *) arg;
    PlayerManager * mgr = param->ClassAdd;
    delete param;
    mgr->RunLoginThread();
    return NULL;
}

PlayerManager::PlayerManager()
{
	m_NextGroup = 0;
	m_NextGuild = 0;
	m_NextPendingGuild = 1;
	m_GroupList = NULL;
	m_Movement_thread_running = false;
	m_last_group_tick = 0;

	memset(m_GlobalPlayerList, 0, sizeof(m_GlobalPlayerList));

	// login thread (the surrounding "movement thread" naming is legacy;
	// what actually runs in the background is RunLoginThread()).
	if (!m_Movement_thread_running)
	{
		LogMessage("Creating login thread\n");
		m_Movement_thread_running = true;

		MParam *param = new MParam;
		if (param)
		{
			param->ClassAdd = this;
			if (pthread_create(&m_LoginThread, NULL, &RunLoginThreadAPI, param) != 0)
				LogMessage("PlayerManager: pthread_create failed (%s)\n", strerror(errno));
		}
	}
}

PlayerManager::~PlayerManager()
{
	//delete groups
	Group * g = m_GroupList;
	while (g)
	{
		Group * temp = g->next;
		delete g;
		g = temp;
	}
	FreeGuilds();
}

//PM
bool PlayerManager::SetupPlayer(Player *player, long IPaddr)
{
    player->ResetPlayer();
    player->SetLastAccessTime(GetNet7TickCount());
	m_PlayerLookup[player->CharacterID()] = player->GameID();

    //add this to the global player list
	SetIndex(player, m_GlobalPlayerList);

    player->SetMVASIndex(player->GameID());

    LogMessage("Confirmed MVAS connection for [%x]: MVAS node index %x\n", player->GameID(), player->MVASIndex());
    return true;    
}

void PlayerManager::DropPlayerFromSector(Player *p)
{
    //first remove the player from all the sector lists
    p->RemoveFromAllSectorRangeLists();
    //now set inactive until player revived at login
    p->SetActive(false);
	//now remove all events for this player from the manager
	
    if (p->PlayerIndex()->GetSectorNum() > 0)
    {
        p->SaveData(true);
		p->UpdateDatabase();
		p->SaveAmmoLevels();
    }
    LogMessage("Player '%s' Removed from sector %s.\n", p->Name(), p->PlayerIndex()->GetSectorName());
}

void PlayerManager::DropPlayerFromGalaxy(Player *p)
{
	if (p->GameID() == PLAYER_NODE_AVAILABLE) //player already dropped
	{
		return;
	}
	
    //first drop the player from the sector - this should remove the player ship from everyone's visibility
    DropPlayerFromSector(p);
	//p->SavePosition();
    p->SetRemove();
	p->SaveLogout();
	m_GlobMemMgr->ReleasePlayerNode(p);
	p->SetActive(false);
	if (p->Name())
	{
		LogMessage("Player '%s' Removed from Galaxy.\n", p->Name());
		SendGlobalChatEvent(CHEV_LOGGED_OUT,p);
	}
	UnSetIndex(p, m_GlobalPlayerList);
	p->SetLoginStage(-1);
}

//This method is developer only for debugging.
//any players using it should expect grief!
//PM
void PlayerManager::SendPlayerWithoutConnection(long player_id)
{
	Player * p = 0;
	Player * player_to_send = 0;

    p = GetPlayer(player_id);

    //first create a new player entry.
    player_to_send = m_GlobMemMgr->GetPlayerNode();

	if (!p || !player_to_send)
    {
		LogMessage("*** Error *** NULL Pointer in SendPlayerWithoutConnection AvatarID: %d", player_id | 0x00FFFFFF);
		return;
	}

    long sector_id = p->PlayerIndex()->GetSectorNum();

    player_to_send->ResetPlayer();
    player_to_send->SetGroupID(-1);
    player_to_send->PlayerIndex()->SetSectorNum(sector_id);
    player_to_send->AddPlayerToRangeList(player_to_send); //add self to range list
    player_to_send->SetDebugPlayer();
    player_to_send->SetActive(true);

    //load some data
    player_to_send->SetCharacterID(p->CharacterID());
    memcpy(player_to_send->Database(), p->Database(), sizeof(CharacterDatabase));
    memcpy(player_to_send->Database()->avatar.avatar_first_name, "Dropdead Fred\0", 14);
    player_to_send->ReadSavedData();
    player_to_send->SetCharacterID(0);

    player_to_send->ShipIndex()->SetName("Dropdead Fred");
    player_to_send->ShipIndex()->SetGameID(player_to_send->GameID());
    player_to_send->SetName("Dropdead Fred");
    strcpy_s(player_to_send->Database()->ship_data.ship_name, 
		sizeof(player_to_send->Database()->ship_data.ship_name), "Summer Holiday");

    //add this to the global player list
	SetIndex(player_to_send, m_GlobalPlayerList);
    SectorManager *SectorMgr = g_ServerMgr->GetSectorManager(sector_id);
    SectorMgr->AddPlayerToSectorList(player_to_send);
	player_to_send->SetSector(sector_id);

	if (player_to_send && p)
	{
        p->SetMyDebugPlayer(player_to_send);
		player_to_send->SetPosition(p->Position());
        player_to_send->SetOrientation(p->Orientation());
		player_to_send->SetVelocity(0.0f);
	}

    if (p->PlayerIndex()->GetSectorNum() > 9999)
    {
        //add to starbase
        player_to_send->SetActionFlag(65);
        player_to_send->SetOrient(0);
        p->DebugPlayerDock(true);
        //player_to_send->SendStarbaseAvatarList();
        //player_to_send->SetPosition(p->Position());
    }
}

// Finally this is OK because all the player info is protected
Player * PlayerManager::GetPlayer(long GameID, bool sector_login)
{
	long lookup_id = m_PlayerLookup[GameID & 0x00FFFFFF];
	return (m_GlobMemMgr->GetPlayerA(lookup_id, sector_login));
}

Player * PlayerManager::GetPlayerFromIndex(long index, bool sector_login)
{
	return (m_GlobMemMgr->GetPlayerA(index | PLAYER_TAG, sector_login));
}

Player * PlayerManager::GetPlayerFromCharacterID(long CharacterID)
{
	Player * p = (0);
    
	while (GetNextPlayerOnList(p, m_GlobalPlayerList))    
	{
        if ( p->CharacterID() == CharacterID )
        {
			return p;
        }
    }

	return 0;
}

Player * PlayerManager::GetPlayer(char * Name)
{
    Player * p = (0);
    Player * retval = (0);

	while (GetNextPlayerOnList(p, m_GlobalPlayerList))
    {
        if (p->Name() && strcasecmp(p->Name(), Name) == 0)
        {
            retval = (Player*)p;
            break;
        }
    }

    return retval;
}

long PlayerManager::GetGameIDFromName(char * Name)
{
    Player * p = GetPlayer(Name);

    if (p)
    {
        return p->GameID();
    }
    else
    {
        return -1;
    }
}

//PM
void PlayerManager::ListPlayersAndLocations(Player *send_to, int min_admin_level, int max_admin_level)
{
	Player * p = (0);
	long player_count = g_ServerMgr->m_GlobMemMgr->GetPlayerCount();

	int sender_admin_level = send_to->AdminLevel();
	if (sender_admin_level >= DEV) sender_admin_level = SDEV; //DEVs can see SDEVs
	if (sender_admin_level >= SDEV) sender_admin_level = ADMIN; //SDEVS can see ADMINS

	if (send_to->AdminLevel() >= 30)
	{
		player_count = 0;
		while (GetNextPlayerOnList(p, m_GlobalPlayerList))    
		{
			if (p->AdminLevel() < min_admin_level || sender_admin_level < p->AdminLevel() || p->AdminLevel() > max_admin_level)
				continue;

			player_count++;
			Object *nearest_nav = (0);

			if (send_to->AdminLevel() >= 50)
			{
				nearest_nav = p->NearestNav();
			}

			if (nearest_nav && p->InSpace())
			{
				send_to->SendVaMessage("%s/%s [%x/C%d/E%d/T%d] in %d near %s", p->Name(), p->AccountUsername(), (p->GameID() & 0x00FFFFFF), 
					p->CombatLevel(),p->ExploreLevel(),p->TradeLevel(), p->PlayerIndex()->GetSectorNum(), nearest_nav->Name());
			}
			else if (send_to->AdminLevel() >= 50)
			{
				send_to->SendVaMessage("%s/%s [%x/C%d/E%d/T%d] in %d", p->Name(), p->AccountUsername(), (p->GameID() & 0x00FFFFFF), 
					p->CombatLevel(),p->ExploreLevel(),p->TradeLevel(), p->PlayerIndex()->GetSectorNum());
			}
			else
			{
				send_to->SendVaMessage("%s", p->Name());
			}
		}
	}

	send_to->SendVaMessage("Total players = %d\n", player_count);
}

void PlayerManager::ListPlayersWithSearch(Player *send_to, char * searchString, int min_admin_level, int max_admin_level)
{
	Player * p = (0);
	long player_count = g_ServerMgr->m_GlobMemMgr->GetPlayerCount();
	long len = strlen(searchString);
	long players_found = 0;

	int sender_admin_level = send_to->AdminLevel();
	if (sender_admin_level >= DEV) sender_admin_level = SDEV; //DEVs can see SDEVs
	if (sender_admin_level >= SDEV) sender_admin_level = ADMIN; //SDEVS can see ADMINS

	if (send_to->AdminLevel() >= 30)
	{
		player_count = 0;
		while (GetNextPlayerOnList(p, m_GlobalPlayerList))    
		{
			if (p->GetLoginStage() != LAST_LOGIN_STAGE || p->AdminLevel() < min_admin_level || sender_admin_level < p->AdminLevel() || p->AdminLevel() > max_admin_level)
				continue;

			player_count++;
			Object *nearest_nav = (0);

			if (send_to->AdminLevel() >= 50)
			{
				nearest_nav = p->NearestNav();
			}

			if (nearest_nav && p->InSpace())
			{
				if (_strnicmp(searchString, p->Name(), len) == 0 
					|| _strnicmp(searchString, p->PlayerIndex()->GetSectorName(), len) == 0)
				{
					players_found++;
					send_to->SendVaMessage("%s%s [%x/C%d/E%d/T%d] in %s [%d] Near to %s", p->Name(), p->AccountUsername(), (p->GameID() & 0x00FFFFFF), 
						p->CombatLevel(),p->ExploreLevel(),p->TradeLevel(), p->PlayerIndex()->GetSectorName(), 
						p->PlayerIndex()->GetSectorNum(), nearest_nav->Name());
				}
			}
			else if (send_to->AdminLevel() >= 50)
			{
				if (strncmp(searchString, p->Name(), len) == 0 
					|| strncmp(searchString, p->PlayerIndex()->GetSectorName(), len) == 0)
				{
					players_found++;
					send_to->SendVaMessage("%s%s [%x/C%d/E%d/T%d] in %s [%d]", p->Name(), p->AccountUsername(), (p->GameID() & 0x00FFFFFF), 
						p->CombatLevel(),p->ExploreLevel(),p->TradeLevel(), p->PlayerIndex()->GetSectorName(), 
						p->PlayerIndex()->GetSectorNum());
				}
			}
			else if (strncmp(searchString, p->Name(), len) == 0)
			{
				players_found++;
				send_to->SendVaMessage("%s", p->Name());
			}
		}
	}

	send_to->SendVaMessage("%d players found for your search.", players_found++);
	send_to->SendVaMessage("Total players = %d\n", player_count);
}

void PlayerManager::CheckForDuplicatePlayers(Player *player)
{
	Player * p = (0);
    
	while (GetNextPlayerOnList(p, m_GlobalPlayerList))    
	{
        if (player != p &&
            p->CharacterID() == player->CharacterID() )
        {
            //this player is already logged in. Remove the old player
            LogMessage("Killing old instance of player '%s' [0x%08x]\n", p->Name(), p->GameID());
            DropPlayerFromGalaxy(p);
			//now forcefully free the node
        }
    }
}

bool PlayerManager::CheckAccountInUse(char *username)
{
	Player * p = (0);
    
	while (GetNextPlayerOnList(p, m_GlobalPlayerList))    
	{
		// strncmp changed to strcasecmp here to prevent multiple 
		// useraccount logins by changing caps
		if (strcasecmp(p->AccountUsername(), username) == 0)
		{
			if (p->Active())
			{			
				//this player's account is already active 
				//this needs to be here due to extreme abuse which is harming the server.

				LogMessage("Account user %s trying to log in twice, removed.\n", username);
				//ErrorBroadcast("Account user %s [%s] trying to log in twice!\n", username, p->Name());
				p->Dialog("Your account has tried to login twice. Disconnected.",0);			
				p->ForceLogout();
				DropPlayerFromGalaxy(p);

				//IMPORTANT: DO NOT CHANGE THIS.
				return true;
				//p->ForceLogout();
			}
			else
			{
				if ((p->LastAccessTime() + 30000) < GetNet7TickCount())
				{
					LogMessage("Account user %s has dead player on server, remove\n", username);
					p->ForceLogout();
					DropPlayerFromGalaxy(p);
				}
				//ErrorBroadcast("Account user %s trying to log in twice!\n", username);
			}
		}
    }
	return false;
}

//Remove all players
void PlayerManager::TerminateAllPlayers()
{
	Player *p = 0;
	while (GetNextPlayerOnList(p, m_GlobalPlayerList))    
	{
		if (p)
		{
			DropPlayerFromGalaxy(p);
			long player_id = p->GameID();
			p->SendOpcode(ENB_OPCODE_100A_MVAS_TERMINATE_S_C, (unsigned char *) &player_id, sizeof(long)); //force terminate Client & Proxy.
			p->SendPacketCache();
		}
	}
}

// Only check the group buff every 10 seconds from here
void PlayerManager::RunMovementThread()
{
	unsigned long current_tick = GetNet7TickCount();
	// check group buffing
	if (current_tick > (m_last_group_tick + 10000)) // 10 seconds (I think :) )
	{
		m_last_group_tick = current_tick;
		TransferGroupBuffs();
	}
}

//this method is called for each player in the sector by the sector thread  (see SectorManager)
void PlayerManager::RunPlayerUpdate(Player *p)
{
	unsigned long current_tick;
	if (p)
	{
		if (p->GetLoginStage() == LAST_LOGIN_STAGE)
		{
			if (p->MovementID() % 5 == 0 && !p->CheckQueueOverloading())
			{
				if (p->InSpace())
				{
					current_tick = GetNet7TickCount();
					p->CalcNewPosition(current_tick); //update the positions

					if (p->WarpDrive() || p->Following() || p->PlayerUpdateSet() ) 
					{
						p->SendLocationAndSpeed(true);
					}

					if (p->ObjectIsMoving())
					{
						p->UpdateVerbs();
					}

					if (p->MovementID() % 50 == 0)
					{
						Object *obj = p->NearestNav();
						p->SetNearestNav(obj);
					}

					p->CheckObjectRanges();
					p->UpdatePlayerVisibilityList();
					p->ResetAttacksThisTick();
					p->CheckEventTimes(current_tick);
				}
				else //not in space, only check events once in 5 calls
				{
					current_tick = GetNet7TickCount();
					p->CheckEventTimes(current_tick);
				}
			} //movementID % 5
			p->IncrementMovementID(1);
			p->SendPacketCache();  //issue UDP opcodes
		}
	}
}

//PM - start at startup
void PlayerManager::RunLoginThread()
{
	Player * p = (0);
	unsigned long current_tick;
	unsigned long sleep_time;
	SectorManager *sm = 0;

	LogMessage("Commence Login thread startup.\n");

	while (!g_ServerShutdown)
	{
		current_tick = GetNet7TickCount();
		p = (0);
		while (GetNextPlayerOnList(p, m_GlobalPlayerList))    
		{
			if (p && p->GetLoginStage() != LAST_LOGIN_STAGE)
			{
				if (p->IsToBeRemoved() || (p->LastAccessTime() + 2*60000) < current_tick)
				{
					DropPlayerFromGalaxy(p);
				}
				else if (p->SendItemList())
				{
					//do nothing
				}
				else
				{
					long stage = p->GetLoginStage();
					switch (stage)
					{
					case 255:
						// not started login yet
						break;

					case 1:
						// we somehow tried to log in a -1 character! :O
						if (p->CharacterID() != -1)
						{
							//LogMessage("Stage %d\n",stage);
							p->SectorLogin();
							p->SetLoginStage(2);
						}
						break;

					case 4:
						//LogMessage("Stage %d\n",stage);
						sm = p->GetSectorManager();
						if (sm)	sm->HandleSectorLogin(p);
						p->SetLoginStage(5);	
						break;

					case 7:
						//LogMessage("Stage %d\n",stage);
						p->HandleLoginStage2();
						p->SetLoginStage(8);
						break;

					case 10:
						//LogMessage("Stage %d\n",stage);
						p->HandleLoginStage3();
						p->SetLoginStage(11);
						break;

					case 2:
					case 5:
					case 8:
					case 11:
						//intermediate wait state for item sends to complete - drops here once all items have been sent for each stage
						p->SetLoginAck(0);
						p->SetLoginStage(stage+1);
						p->SendLoginStageConfirm(p->GetLoginStage());
						break;

					case 3:
					case 6:
					case 9:
					case 12:
						//here we hold for each acknowledgement before moving to the next stage
						//LogMessage("Wait for login confirm for stage %d\n",stage);
						if (p->WaitForLoginAck(stage))
						{
							//LogMessage("Client %s confirms login - advance to stage %d\n", p->Name(), stage+1);
							p->SetLoginStage(stage+1);
						}
						break;

					case 13:
						p->SetLoginStage(LAST_LOGIN_STAGE);
						p->CompleteLogin();
						break;
						
					default:
						//LogMessage("Bad login stage %d for %s\n", stage, p->Name());
						break;
					}
				}

				p->SendPacketCache();
			}
		}
		sleep_time = (long)(100 - (GetNet7TickCount() - current_tick)); 
		if (sleep_time < 0) sleep_time = 0;
		if (sleep_time > 100) sleep_time = 100;
		Sleep(sleep_time);
	}
}

//a global method to ensure all player opcodes have been sent
void PlayerManager::SendUDPOpcodes()
{
	Player * p = (0);
	unsigned long current_tick = GetNet7TickCount();

	while (GetNextPlayerOnList(p, m_GlobalPlayerList))    
	{
		//Handle player removal here 
		if (p)
		{
			if (p->IsToBeRemoved() || (p->LastAccessTime() + 2*60000) < current_tick)
			{
				DropPlayerFromGalaxy(p);
				p->SendPacketCache();
			}
			else
			{
				p->SendPacketCache();
			}
		}
	}
}

//call from local sector to issue player's opcodes.
void PlayerManager::SendUDPPlayerOpcodes(Player *p)
{
	unsigned long current_tick = GetNet7TickCount();

	//Handle player removal here 
	if (p && IS_PLAYER(p->GameID()))
	{
		if (p->IsToBeRemoved() || (p->LastAccessTime() + 2*60000) < current_tick)
		{
			DropPlayerFromGalaxy(p);
			p->SendPacketCache();
		}
	}

}

///////////////////////////
////    Chat System    ////
///////////////////////////

void PlayerManager::ChatSendEveryone(long GameID, char *message, bool copy_to_originator, bool display_postfix, long minAdminLevel)
{
    if (message)
    {
        // Find sender's name
        Player * s = GetPlayer(GameID);
		Player * p = (0);

		if (minAdminLevel >= 90) minAdminLevel = 100; //Dev's see everyone
		if (minAdminLevel < 50)  minAdminLevel = 0; // Everyone below GM status sees everyone else

        if (!s)
        {
            return;
        }

        if (s)
        {
			char Channel[] = "Broadcast";		// Channel
            
			while (GetNextPlayerOnList(p, m_GlobalPlayerList))   
			{
				//only send to originating player if directed to.
				if (((p->GameID() != GameID) || copy_to_originator) 
					&& p->AdminLevel() >= minAdminLevel
					&& p->IsSubscribed(GetChannelFromName(Channel)))
				{
					p->SendClientChatEvent(CHEV_CHANNEL_MESSAGE, s, Channel, message);
				}
			}
        }
    }
}

void PlayerManager::BroadcastChat(long GameID, char *message, bool copy_to_originator, bool display_postfix, long minAdminLevel)
{
    if (message)
    {
        // Find sender's name
        Player * s = GetPlayer(GameID);
		Player * p = (0);

		if (minAdminLevel >= 90) minAdminLevel = 100; //Dev's see everyone
		if (minAdminLevel < 50)  minAdminLevel = 0; // Everyone below GM status sees everyone else

        if (!s)
        {
            return;
        }

        if (s && s->GetSectorPlayerList())
        {
			char Channel[] = "Broadcast";		// Channel
            
			while (GetNextPlayerOnList(p, s->GetSectorPlayerList()))   
			{
				//only send to originating player if directed to.
				if (((p->GameID() != GameID) || copy_to_originator) 
					&& p->AdminLevel() >= minAdminLevel	
					&& p->IsSubscribed(GetChannelFromName(Channel)))
				{
					p->SendClientChatEvent(CHEV_CHANNEL_MESSAGE, s, Channel, message);
				}
			}
        }
    }
}

void PlayerManager::LocalChat(long GameID, char *message, bool copy_to_originator, bool display_postfix, long minAdminLevel)
{
    if (message)
    {
        // Find sender's name
        Player * s = GetPlayer(GameID);
		Player * p = (0);

		if (minAdminLevel >= 90) minAdminLevel = 100; //Dev's see everyone
		if (minAdminLevel < 50)  minAdminLevel = 0; // Everyone below GM status sees everyone else

        if (!s)
        {
            return;
        }

        if (s)
        {
			char Channel[] = "Local";		// Channel
            
			while (GetNextPlayerOnList(p, s->GetSectorPlayerList()))   
			{
				//only send to originating player if directed to.
				if (((p->GameID() != GameID) || copy_to_originator) 
					&& p->AdminLevel() >= minAdminLevel	
					&& p->IsSubscribed(GetChannelFromName(Channel))
					&& s->RangeFrom(p->Position()) < 25000.0f)		// best misplaced parenthesis bug ever!
				{
					p->SendClientChatEvent(CHEV_CHANNEL_MESSAGE, s, Channel, message);
				}
			}
        }
    }
}

//Variant of SendMessage which is global and lets you use a colour
//Only sends messages to adminLevel or higher

//colors
//17 - red
//13 - white
//12 - light green
//11 - dark blue

//adminLevel
//  0 - all players
// 50 - GMs
//100 - devs and admins

void PlayerManager::SendGlobalVaMessage(long GameID, long adminLevel, bool copy_to_originator, char colour, char *string, ...)
{
	if (string)
	{
		if (adminLevel >= 90) adminLevel = 100; //Dev's see everyone
		if (adminLevel < 50)  adminLevel = 0; // Everyone below GM status sees everyone else

		unsigned int len = strlen(string) + 256;
		char *pch = (char*)_alloca(len);
		va_list args;
		va_start(args, string);
		vsprintf_s(pch, len, string, args);

		Player * p = (0);

		while (GetNextPlayerOnList(p, m_GlobalPlayerList))   
		{
			//only send to originating player if directed to.
			if (((p->GameID() != GameID) || copy_to_originator) && p->AdminLevel() >= adminLevel)
			{
				p->SendMessageString(pch, colour);
			}
		}
		va_end(args);
	}
}

void PlayerManager::SendGlobalChatEvent(int type, Player *source, char *channel, char *message)
{
	long adminLevel = source->AdminLevel() >= GM ? GM : 0;
	Player * p = (0);

	while (GetNextPlayerOnList(p, m_GlobalPlayerList))   
	{
		if (p != source && p->AdminLevel() >= adminLevel || p->AdminLevel() >= GM || source->IsMyFriend(p->Name()))
		{
			p->SendClientChatEvent(type, source, channel, message);
		}
	}
}

//PM
void PlayerManager::GMMessage(char *message)
{
	Player * p = 0;

    if (message)
    {
        //char broadcast[2048];
		char Channel[] = "Admin Broadcast";		// Channel
            
		while (GetNextPlayerOnList(p, m_GlobalPlayerList))   
		{
			if (p->AdminLevel() >= 50)
			{
				p->SendClientChatEvent(CHEV_CHANNEL_MESSAGE, NULL, Channel, message, "", "GM");
			}
		}
	}
}

//PM
void PlayerManager::ErrorBroadcast(char *string, ...)
{
	Player * p = 0;

    if (string)
    {
		unsigned int len = strlen(string) + 256;
		char *pch = (char*)_alloca(len);
		va_list args;
		va_start(args, string);
		vsprintf_s(pch, len, string, args);

        //char broadcast[2048];
		char Channel[] = "Errors";		// Channel
           
		while (GetNextPlayerOnList(p, m_GlobalPlayerList))   
		{
			if (p->AdminLevel() >= BETA && p->IsSubscribed(GetChannelFromName(Channel)))
			{
				p->SendClientChatEvent(CHEV_CHANNEL_MESSAGE, NULL, Channel, pch, "", "Error");
			}
		}
	}
}

long PlayerManager::GetChannelFromName(char *channel_name)
{
	long channel_id = 0;
	if (!channel_name) return INVALID_CHANNEL;
	char *test_name;
	long len = strlen(channel_name);
	bool found = false;

	while (test_name = ChannelNames[channel_id])
	{
		if (channel_name[0] == test_name[0]) //faster than strcmp
		{
			if (channel_name[1] == test_name[1])
			{
				if (channel_name[2] == test_name[2])
				{
					if (len <= 7 || len > 7 && channel_name[7] == test_name[7])
					{
						found = true;
						break;
					}
				}
			}
		}
		channel_id++;
	}

	if (found)
	{
		return channel_id;
	}
	else
	{
		return INVALID_CHANNEL;
	}
}

//PM
void PlayerManager::ChatSendChannel(long GameID, char * Channel, char * Message)
{
    if (Message && Channel)
    {
        // Find sender's name
        Player * s = GetPlayer(GameID);
        Player * p = (0);

        long channel_id = GetChannelFromName(Channel);

		if (s && s->IsSubscribed(channel_id))
        {
            if (GetChannelFromName("New Players") == channel_id && s->TotalLevel() > 50 && s->AdminLevel() < 40)
            {
                s->SendVaMessage("Please only use this channel to help new players.");
            }

			if (GetChannelFromName("Beta") == channel_id && s->AdminLevel() < BETA)
				return;
			if (GetChannelFromName("GM") == channel_id && s->AdminLevel() < GM)
				return;
			if (GetChannelFromName("Dev") == channel_id && s->AdminLevel() < DEV)
				return;

			while (GetNextPlayerOnList(p, m_GlobalPlayerList))   
			{			
				if (p->Active() && p->IsSubscribed(channel_id))
				{
					p->SendClientChatEvent(CHEV_CHANNEL_MESSAGE, s, Channel, Message);
				}
			}
		}
	}
}

void PlayerManager::GlobalMessage(char * Message)
{
	Player * p = 0;
    
    if (Message)
    {
		while (GetNextPlayerOnList(p, m_GlobalPlayerList))   
		{
			p->SendPriorityMessageString(Message, "MessageLine", 5000, 1);
		}
    }
}

void PlayerManager::GlobalAdminMessage(char * Message)
{
	Player * p = 0;
    
    if (Message)
    {
		while (GetNextPlayerOnList(p, m_GlobalPlayerList))   
		{
			p->SendMessageString(Message, 0x0A, false);
		}
    }
}

//PM
void PlayerManager::ChatSendPrivate(long GameID, char * Nick, char * Message)
{
    if (Nick && Message)
    {
        Player * p_list = (0);
        bool FoundPlayer = false;
		char Channel[] = "Private";

		if (Nick[0] == 0) return;

        Player * p = GetPlayer(GameID);

		//Trim off the postfix tag to send the tell to the proper person and not a character named "Person [TAG]" or something.
		char nickTrim[40];
		strcpy_s(nickTrim, sizeof(nickTrim), Nick);
		nickTrim[sizeof(nickTrim)-1] = '\0';
		for(int i = 0; i < 40; i++)
		{
			if(nickTrim[i]==' ' || nickTrim[i] =='\0')
			{
				nickTrim[i] = '\0';
				break;
			}
		}

        if (p)
        {
            while (GetNextPlayerOnList(p_list, m_GlobalPlayerList))  
			{
				if (p_list->Name() && !strcasecmp(p_list->Name(), nickTrim))
                {
                    p_list->SendClientChatEvent(CHEV_PRIVATE_MESSAGE, p, Channel, Message);
                    FoundPlayer = true;
                    break;
                }
            }
            
            if (!FoundPlayer)
            {
				p->SendClientChatError(CHAT_ERROR_INVALID_PERSON,CCE_SPEAK_LOCALLY,Nick);
            }
        }
    }
}

//PM
char *PlayerManager::WhoHtml(size_t *response_length)
{
    // returns a list of who is online
    // first estimate how much memory we need
    int count = 0;
    size_t length = 512; // increase this if we add more formatting and decorations
    char *player = NULL;
    Player * p = (0);

    while (GetNextPlayerOnList(p, m_GlobalPlayerList))  
	{
        player = p->Name();
        count++;
        length += 128; // increase this if we display other player stuff in the table
    }
    
    char *data = (char*)_alloca(length);
    //char *data = new char[length];

	/*
    if (count == 0)
    {
        strcpy(data,
            "<html><head><title>Net-7: Who is Online</title></head><body>\r\n"
            "No players are currently online on Net-7\r\n"
            "</body></html>\r\n");
    }
    else if (count == 1)
    {
        sprintf(data,
            "<html><head><title>Net-7: Who is Online</title></head><body>\r\n"
            "The only player currently online on Net-7 is %s\r\n"
            "</body></html>\r\n", player);
    }
    else
    {
		*/
        sprintf_s(data, length,
			"<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\n\r"
            "<online number=\"%d\">\r\n"
            "	<list>\r\n",
            count);
		struct in_addr in;
		p = (0);
        while (GetNextPlayerOnList(p, m_GlobalPlayerList))  
		{
			if (p)
			{
				in.s_addr = p->PlayerIPAddr();
				sprintf_s(data, length, 
					"%s		<player name=\"%s\" ip=\"%s\" clevel=\"%d\" elevel=\"%d\" tlevel=\"%d\" "
					"race=\"%d\" profession=\"%d\"/>\r\n", 
					data, p->Name(), inet_ntoa(in), p->Database()->info.combat_level, p->Database()->info.explore_level, 
					p->Database()->info.trade_level,
					p->Database()->ship_data.race, p->Database()->ship_data.profession);
			} 
            else 
            {
				in.s_addr = 0;
            }
            //strcat(data, "      <player name=\"");
            //strcat(data, p->Database()->avatar.avatar_first_name);
            //strcat(data, "</td></tr>\r\n");
            
        }
		strcat_s(data, length, "	</list>\r\n</online>\r\n");
        //strcat(data, "</table></body></html>\r\n");
    //}

    // Determine the actual length
    length = strlen(data);

    char header[256];
	sprintf_s(header, sizeof(header),
		"HTTP/1.1 200 OK\r\n"
        "<META HTTP-EQUIV=\"Pragma\" CONTENT=\"no-cache\">\r\n"
		"Content-Type: text/html\r\n"
		"Server: AuthServer/2.5\r\n"
		"Content-Length: %d\r\n"
 		"\r\n",
		length);

    size_t header_length = strlen(header);
    *response_length = header_length + length;

    char * response = new char[header_length + length + 1]; // !heap alloc!

    if (response)
    {
	    strcpy_s(response, header_length + length + 1, header);
		response[header_length + length] = '\0';
        memcpy(response + header_length, data, length);
        response[header_length + length] = 0;
    }

    // done with data
    //delete [] data;

	return (response);
}

//PM
void PlayerManager::SetUDPConnection(UDP_Connection *connection)
{
	m_UDPConnection = connection;
}

//PM
void PlayerManager::SetGlobalMemoryHandler(GMemoryHandler *MemMgr)
{
    m_GlobMemMgr = MemMgr;
}

//Method to iterate along a player list
bool PlayerManager::GetNextPlayerOnList(Player *&p, u32 *player_list)
{
	//iterate along the player list
	u32 index = 0;
	u32 block_index = 0;
	u32 *entry = player_list;
	bool found = false;

	if (player_list)
	{
		if (p)
		{
			index = (p->GetGameIndex()) + 1;
			block_index = index/32;
			entry = (u32*) (player_list + block_index);
		}

		while (index < MAX_ONLINE_PLAYERS)
		{
			//removed try/catch bodge, fix correctly when/if it crashes here (if unsure, just leave it or PM Tienbau).
			//is this bit set?
			if (*entry & (1 << index%32))
			{
				p = GetPlayerFromIndex(index | PLAYER_TAG);
				if (p)
				{
					found = true;
					break;
				}
				else
				{
					*entry &= (0xFFFFFFFF ^ (1 << index%32)); //unset the bit, as it's invalid
				}
			}
			index++;

			if (*entry == 0)
			{
				//skip to start of next block
				block_index++;
				index = 32*block_index;
				entry = (u32*) (player_list + block_index);
			}
			else if (index%32 == 0)
			{
				block_index++;
				entry = (u32*) (player_list + block_index);
			}
		}
	}

	return found;
}

//object index methods
void PlayerManager::SetIndex(Player *p, u32 *player_list)
{
	u32 index;
	if (p)
	{
		index = p->GetGameIndex();
	}
	else
	{
		return;
	}

	m_Mutex.Lock();
	if (index >= 0 && index < MAX_ONLINE_PLAYERS)
	{
		u32 *entry = (u32*) (player_list + (index/(sizeof(u32)*8)));

		//now set the specific bit
		*entry |= (1 << index%32);
	}
	m_Mutex.Unlock();
}

void PlayerManager::UnSetIndex(Player *p, u32 *player_list)
{
	u32 index;
	if (p)
	{
		index = p->GetGameIndex();
	}
	else
	{
		return;
	}

	m_Mutex.Lock();
	if (index >= 0 && index < MAX_ONLINE_PLAYERS)
	{
		u32 *entry = (u32*) (player_list + (index/(sizeof(u32)*8)));
		
		//now unset the specific bit
		*entry &= (0xFFFFFFFF ^ (1 << index%32));
	}
	m_Mutex.Unlock();
}

bool PlayerManager::GetIndex(Player *p, u32 *player_list)
{
	u32 index;
	if (p)
	{
		index = p->GetGameIndex();
	}
	else
	{
		return false;
	}

	if (index > MAX_ONLINE_PLAYERS) return false;

	m_Mutex.Lock();
	u32 *entry = (u32*) (player_list + (index/(sizeof(u32)*8)));
	m_Mutex.Unlock();

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

void PlayerManager::RequestAllPlayersLFG(Player *player)
{
	Player *p = NULL;
	int count = 0;

	// count first to calc memory needed
	while (GetNextPlayerOnList(p, m_GlobalPlayerList))    
	{
		if (p != player && p->IsInSameSector(player) && p->PlayerIndex()->GroupInfo.GetLookingForGroup())
		{
			count++;
		}
	}
	struct FindMember *players = (struct FindMember *)new char [count * 16 + 4];
	players->count = count;
	p = NULL;
	count = 0;
	// add to list
	while (GetNextPlayerOnList(p, m_GlobalPlayerList))    
	{
		if (p != player && p->IsInSameSector(player) && p->PlayerIndex()->GroupInfo.GetLookingForGroup())
		{
			players->list[count].GameID = ntohl(p->GameID());
			players->list[count].Level = ntohl(p->CombatLevel());
			players->list[count].Race = ntohl(p->Race());
			players->list[count].Profession = ntohl(p->Profession());
			p->AddPlayerToRangeList(player);
		}
	}
	p->SendFindMember(players);
	delete[] players;
}
