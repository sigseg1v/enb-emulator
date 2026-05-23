// PlayerConnection.cpp
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
#include "UDPConnection.h"
#include "MOBDatabase.h"
#include "PlayerManufacturing.h"

//remove Lua for now - giving a lot of build warnings.
#if 0
extern "C" 
{
  #include "lua.h"
  #include "lualib.h"
  #include "lauxlib.h"
}
// This is the only header we need to include for LuaBind to work
#include "luabind/luabind.hpp"
#endif

enum AvatarZone
{
	az_hair,
	az_beard,
	az_haircolour,
	az_skincolour,
	az_eyecolour,
	az_detail,
	az_glasses,
	az_earpiece,
	az_shirt,
	az_shirtcolourpri,
	az_shirtcoloursec,
	az_pants,
	az_pantscolourpri,
	az_pantscoloursec,
};
long g_CustomiseAvatarCosts[14] = {5000,5000,2500,2500,2500,5000,5000,5000,1000,2500,2500,1000,2500,2500}; // guesses for now

enum ShipZone
{
	sz_hull,
	sz_wing,
	sz_hullcolourpri,
	sz_hullcoloursec,
	sz_wingcolourpri,
	sz_wingcoloursec,
	sz_profcolourpri,
	sz_profcoloursec,
	sz_enginecolourpri,
	sz_enginecoloursec,
	sz_name,
	sz_decal,
};
long g_CustomiseShipCosts[12] = {100000,50000,10000,10000,10000,10000,10000,10000,10000,10000,25000,25000}; // guesses for now

void Player::SetPlayerPortIP(short port, long ip_addr)
{
	m_Player_IPAddr = ip_addr;
	m_Player_Port = port;
}

#define RECEIVE_MESSAGE_MAX_LENGTH 1302	// petitions can be very large (1297)
char unknown_corpse[] = "Corpse\0";

void Player::PulsePlayerInput()
{
	int length;
	EnbTcpHeader *header;
	unsigned char msg[RECEIVE_MESSAGE_MAX_LENGTH]; //message will be under 256 or the 'AddMessage' method will not process it
	long player_id;

	while (m_RecvQueue->CheckQueue(msg, &length, RECEIVE_MESSAGE_MAX_LENGTH, &player_id)) //check if there are any messages in the queue, if there are write them into the 'msg' buffer
	{
		if (player_id != GameID())
		{
			LogMessage("Queue failure for player %s\n", Name());
		}
		//process this message
		//format is opcode/length/message
		header = (EnbTcpHeader*)msg;
		unsigned char *data = (msg + sizeof(EnbTcpHeader));

		//process opcode accordingly
		HandleClientOpcode(header->opcode, header->size, data);                
	}
}

void Player::AddMessage(short opcode, short length, unsigned char *data)
{
	unsigned char pData[RECEIVE_MESSAGE_MAX_LENGTH];

	if (length + 4 > RECEIVE_MESSAGE_MAX_LENGTH)
	{
		LogMessage("Recv message overflow: length = %d\n", length);
		return;
	}

	*((short*) &pData[0]) = length;
	*((short*) &pData[2]) = opcode;
	memcpy(pData + sizeof(short)*2, data, length);

	m_RecvQueue->Add(pData, length+sizeof(EnbTcpHeader), GameID());
}

void Player::SendOpcode(short opcode, unsigned char *data, long length, bool issue)
{
	if (!this || m_UDPQueue->Count() > 600)
	{
		return; //start choking packets if we're sending too many
	}

	int bytes = length + sizeof(long);

	//grab some buffer space from the queue. Scratch buffer is just dummy data here.
	//this doesn't work because the buffers are circular!
	//TODO: write an 'AddOpcode' variant of Add
	//unsigned char * buffer = m_UDPQueue->Add(m_ScratchBuffer, bytes, GameID());

	*((short *) &m_OpcodeFormingBuffer[0]) = (short) length + sizeof(long);
	*((short *) &m_OpcodeFormingBuffer[2]) = opcode;

	memcpy(m_OpcodeFormingBuffer + sizeof(long), data, length);

	m_UDPQueue->Add(m_OpcodeFormingBuffer, bytes, GameID());

	if (m_UDPQueue->Count() > 250)
	{
		LogMessage("UDP Queue Count for %s getting high = %d\n", Name(), m_UDPQueue->Count());
	}
}

void Player::SendPacketCache()
{
	//first process all our inputs, unless we're in a very high queue load situation
	if (!CheckQueueOverloading())
	{
		PulsePlayerInput();
	}

	//now build the next packet to send.
	int length;
	int cumulative = 0;
	long player_id;
	short opcode = ENB_OPCODE_2016_PACKET_SEQUENCE;

	//continue sending a split up packet
	//NB we can optimise this a little, by using 'leftover' packet space on the end of
	//any packet split end send, but let's wait until it's all working before we do it
	if (m_PacketSplitRemaining > 0)
	{
		if (m_PacketSplitRemaining < m_PeriodicCacheSize)
		{
			cumulative = m_PacketSplitRemaining;
			m_PacketSplitRemaining = 0;
		}
		else
		{
			cumulative = m_PeriodicCacheSize;
			m_PacketSplitRemaining -= m_PeriodicCacheSize;
		}

		//write another chunk
		memcpy(m_OpcodeFormingBuffer, m_PacketSplitBuffer, cumulative);
		//shift the remaining message to the front of split buffer
		if (m_PacketSplitRemaining > 0)
		{
			memmove(m_PacketSplitBuffer, m_PacketSplitBuffer + m_PeriodicCacheSize, m_PacketSplitRemaining);
		}
		opcode = ENB_OPCODE_201A_PACKET_C_SEQUENCE;
		//LogMessage(".. Sending %x [%d]\n", cumulative, m_PacketSequenceNum);
	}
	else if (m_UDPQueue->CheckNextQueueSize() > m_PeriodicCacheSize)
	{
		//split this packet up
		m_UDPQueue->CheckQueue(m_PacketSplitBuffer, &length, MAXIMUM_PACKET_CACHE, &player_id);
		if (player_id != GameID())
		{
			LogMessage("Queue corruption for %s\n", Name());
		}
		//LogMessage("Splitting up large opcode: %x\n", length);
		m_PacketSplitRemaining = length - m_PeriodicCacheSize;
		//load up the send buffer
		memcpy(m_OpcodeFormingBuffer, m_PacketSplitBuffer, m_PeriodicCacheSize);
		cumulative = m_PeriodicCacheSize;
		//shift the remaining message to the front of split buffer
		memmove(m_PacketSplitBuffer, m_PacketSplitBuffer + m_PeriodicCacheSize, m_PacketSplitRemaining);
		//LogMessage(":- Sending %x [%d]\n", cumulative, m_PacketSequenceNum);
	}
	else
	{
		while (m_UDPQueue->CheckQueue((m_OpcodeFormingBuffer+cumulative), &length, (m_PeriodicCacheSize - cumulative), &player_id) )
		{
			cumulative += length;
			//LogMessage(">- Sending %x [%d]\n", cumulative, m_PacketSequenceNum);
			if (m_UDPQueue->CheckNextQueueSize() >= (m_PeriodicCacheSize - cumulative))
			{
				break;
			}
		}
	}

	if (m_UDPConnection && cumulative > 0)
	{
#if 0
		//packet drop out simulation testing
		if (rand()%5 == 0)
		{
			LogMessage("Dropped packet %x\n", m_PacketSequenceNum);
			//m_PacketSequenceNum++;
		}
		else
#endif
		m_UDPConnection->SendOpcode(opcode, this, m_OpcodeFormingBuffer, cumulative, m_Player_IPAddr, m_Player_Port, m_PacketSequenceNum);
		//LogMessage("Sending packet sequence %d to %s. Size = %x\n", m_PacketSequenceNum, Name(), cumulative);
		//Add to resend queue
		u8* data = m_RSendQueue->Add(m_OpcodeFormingBuffer, cumulative, GameID());

		m_ResendQueue[m_ResendIndex].data = data;
		m_ResendQueue[m_ResendIndex].length = cumulative;
		m_ResendQueue[m_ResendIndex].packet_num = m_PacketSequenceNum;
		m_ResendQueue[m_ResendIndex].message = *((u32 *) m_OpcodeFormingBuffer);
		m_PacketSequenceNum++;
		m_ResendIndex++;
		if (m_ResendIndex >= RESEND_ELEMENTS) m_ResendIndex = 0;
	}

	m_OpcodeResends = 0;
}

void Player::ReSendOpcodes(unsigned char *data)
{
	long packet_num = *((short*) &data[0]);
	long opcode_count = *((short*) &data[4]);
	u32 current_tick = GetNet7TickCount();

	LogMessage("Opcode re-send #%x (%s)\n", packet_num, Name());

	//see if this packet still exists, send if it does, otherwise send a blank
	if (m_UDPConnection)
	{
		for (int i = 0; i < RESEND_ELEMENTS; i++)
		{
			if (m_ResendQueue[i].packet_num == packet_num)
			{
				if (m_ResendQueue[i].data && m_ResendQueue[i].message == *((int *) m_ResendQueue[i].data) )
				{
					m_RSendQueue->RetreiveMessage(m_ScratchBuffer, m_ResendQueue[i].length, m_ResendQueue[i].data );
					m_UDPConnection->SendOpcode(ENB_OPCODE_2016_PACKET_SEQUENCE, this, m_ScratchBuffer, m_ResendQueue[i].length, m_Player_IPAddr, m_Player_Port, packet_num);
				}
				else
				{
					m_UDPConnection->SendOpcode(ENB_OPCODE_2016_PACKET_SEQUENCE, this, 0, 0, m_Player_IPAddr, m_Player_Port, packet_num);
				}
				break;
			}
		}
	}
}

#define ITEMS_PER_TICK 2 //send 2 extra items per tick
bool Player::SendItemList()
{
	bool in_progress = false;
	long send_count = 0;

	switch (m_ItemSendType)
	{
	case ITEMLIST_CARGO:
		//continue sending cargo list
		//send next few cargo items
		while (send_count < ITEMS_PER_TICK && (m_ItemSendIndex < ShipIndex()->Inventory.GetCargoSpace()) && m_ItemSendIndex < 40)
		{
			SendItemBase(ShipIndex()->Inventory.CargoInv.Item[m_ItemSendIndex].GetItemTemplateID());
			send_count++;
			m_ItemSendIndex++;
		}
		if (m_ItemSendIndex < ShipIndex()->Inventory.GetCargoSpace()) //end send
		{
			in_progress = true;
		}
		else
		{
			m_ItemSendIndex = 0;
			m_ItemSendType = (item_send)0;
		}
		break;
	case ITEMLIST_VAULT:
		//continue sending vault list
		while (send_count < ITEMS_PER_TICK && (m_ItemSendIndex < 96))
		{
			SendItemBase(PlayerIndex()->SecureInv.Item[m_ItemSendIndex].GetItemTemplateID());
			send_count++;
			m_ItemSendIndex++;
		}
		if (m_ItemSendIndex < 96) //end send
		{
			in_progress = true;
		}
		else
		{
			m_ItemSendIndex = 0;
			m_ItemSendType = (item_send)0;
		}
		break;
	case ITEMLIST_NPC:
		//continue sending NPC item list : TODO: hook this up
		while (send_count < ITEMS_PER_TICK && (m_ItemSendIndex < m_CurrentNPC->Vendor.Items.size()))
		{
			SendItemBase(m_CurrentNPC->Vendor.Items[m_ItemSendIndex]);
			send_count++;
			m_ItemSendIndex++;
		}
		if (m_ItemSendIndex < m_CurrentNPC->Vendor.Items.size()) //end send
		{
			in_progress = true;
		}
		else
		{
			m_ItemSendIndex = 0;
			m_ItemSendType = (item_send)0;
		}
		break;
	default:
		break;
	}

	return in_progress;
}

//take a peek at the server-to-proxy send queue - use this to wait a few cycles before sending more info
bool Player::CheckQueueOverloading()
{
	bool overloading = false;
	if (m_UDPQueue) overloading = m_UDPQueue->Count() > 50 ? true : false;
	return overloading;
}

bool Player::WaitForLoginAck(long stage)
{
	//have we received the login ack yet?
	if (m_LoginAckReceived == stage)
	{
		m_LoginAckCounter = 0;
		return true;
	}
	else if (m_UDPQueue->Count() == 0) //only start bumping the login counter when the queue is low
	{
		m_LoginAckCounter++;
		//still haven't had word from the client
		if (m_LoginAckCounter > 100) //timeout
		{
			char buffer[100];
			//assume player is dead, log them out
			LogMessage(" ---!!> Player %s timed out during login stage %d\n", Name(), m_LoginStage);
			//send message to GM+
			_snprintf(buffer, 100, "Player %s timed out during login", Name());
			g_PlayerMgr->ChatSendChannel(GameID(), "GM", buffer);
			g_PlayerMgr->DropPlayerFromGalaxy(this);
			return false;
		}
		if (m_LoginAckCounter%20 == 0) //wait 5 seconds before resending
		{
			LogMessage("Re-send Ack request %d for %s\n", stage, Name());
			//after 5 seconds no response from the player's Net7Proxy, so re-send packet
			SendLoginStageConfirm(stage);
		}
	}

	return false;
}

void Player::SendLoginStageConfirm(long stage)
{
	//send opcode to client, but don't advance the packetnum.
	//this is so we don't flood the user with packets. Under normal conditions we should never need to use 'advance'
	SendOpcode(ENB_OPCODE_2020_LOGIN_STAGE_S_C, (unsigned char*) &stage, sizeof(stage));
}

void Player::HandleClientOpcode(short opcode, short bytes, unsigned char *data)
{
	//LogMessage("Opcode for player 0x%08x '%s': %x\n", GameID(), Name(), opcode);
	switch (opcode)
	{
	case ENB_OPCODE_0002_LOGIN:
		HandleLogin(data);
		break;

	case ENB_OPCODE_0006_START_ACK :
		HandleStartAck(data);
		break;

	case ENB_OPCODE_0012_TURN:
		HandleTurn(data);
		break;

	case ENB_OPCODE_0013_TILT:
		HandleTilt(data);
		break;

	case ENB_OPCODE_0014_MOVE :
		HandleMove(data);
		break;

	case ENB_OPCODE_0017_REQUEST_TARGET :
		HandleRequestTarget(data);
		break;

	case ENB_OPCODE_0018_REQUEST_TARGETS_TARGET :
		HandleRequestTargetsTarget(data);
		break;

	case ENB_OPCODE_001A_DEBUG :
		HandleDebug(data);
		break;

	case ENB_OPCODE_0027_INVENTORY_MOVE:
		HandleInventoryMove(data);
		break;

	case ENB_OPCODE_0027_INVENTORY_SORT:
		HandleInventorySort(data);
		break;

	case ENB_OPCODE_0029_ITEM_STATE:
		HandleItemState(data);
		break;

	case ENB_OPCODE_002C_ACTION :
		HandleAction(data);
		break;

	case ENB_OPCODE_002D_ACTION2 : // different packet structure to 2C
		HandleAction2(data);
		break;

	case ENB_OPCODE_002E_OPTION :
		HandleOption(data);
		break;

	case ENB_OPCODE_0033_CLIENT_CHAT :
		HandleClientChat(data);
		break;

	case ENB_OPCODE_0044_REQUEST_TIME :
		HandleRequestTime(data);
		break;

	case ENB_OPCODE_004E_STARBASE_REQUEST :
		HandleStarbaseRequest(data);
		break;

	case ENB_OPCODE_0051_SKILL_STRING_RQ:
		HandleSkillStringRequest(data);
		break;

	case ENB_OPCODE_0055_SELECT_TALK_TREE :
		HandleSelectTalkTree(data);
		break;

	case ENB_OPCODE_0057_SKILL_UP:
		HandleSkillAction(data);
		break;

	case ENB_OPCODE_0058_SKILL_ABILITY:
		HandleSkillAbility(data);
		break;

	case ENB_OPCODE_005A_VERB_REQUEST:
		HandleVerbRequest(data);
		break;

	case ENB_OPCODE_005D_EQUIP_USE :
		HandleEquipUse(data);
		break;

	case ENB_OPCODE_005E_AVATAR_EMOTE:
		HandleChatStream(data);
		break;

	case ENB_OPCODE_0079_MANUFACTURE_ITEM_CATAGORY:
		HandleManufactureTerminal(data);
		break;

	case ENB_OPCODE_007A_MANUFACTURE_ITEM_CATAGORY :
		HandleManufactureCategorySelection(data);
		break;

	case ENB_OPCODE_007B_MANUFACTURE_ITEM_ID :
		HandleManufactureSetItem(data);
		break;

	case ENB_OPCODE_007C_REFINERY_ITEM_ID :
		HandleRefineSetItem(data); 
		break;

	case ENB_OPCODE_007E_MANUFACTURE_ACTION:
		HandleManufactureAction(data);
		break;

	case ENB_OPCODE_0080_MANUFACTURE_TECH_LEVEL_FILTER:
		HandleManufactureLevelFilter(data);
		break;

	case ENB_OPCODE_0082_RECUSTOMIZE_SHIP_DONE:
		HandleRecustomizeShipDone(data);
		break;

	case ENB_OPCODE_0084_RECUSTOMIZE_AVATAR_DONE:
		HandleRecustomizeAvatarDone(data);
		break;

	case ENB_OPCODE_0086_MISSION_FORFEIT:
		HandleMissionForfeit(data);
		break;

	case ENB_OPCODE_0087_MISSION_DISMISSAL:
		HandleMissionDismissal(data);
		break;

	case ENB_OPCODE_0088_PETITION_STUCK:
		HandlePetitionStuck(data,bytes);
		break;

	case ENB_OPCODE_008D_INCAPACITANCE_REQUEST:
		HandleIncapacitanceRequest(data);
		break;

	case ENB_OPCODE_0098_GALAXY_MAP_REQUEST :
		HandleGalaxyMapRequest();
		break;

	case ENB_OPCODE_009B_WARP:
		HandleWarp(data);
		break;

	case ENB_OPCODE_009D_STARBASE_AVATAR_CHANGE :
		HandleStarbaseAvatarChange(data);
		break;

	case ENB_OPCODE_009F_STARBASE_ROOM_CHANGE :
		HandleStarbaseRoomChange(data);
		break;

	case ENB_OPCODE_00A1_TRIGGER_EMOTE :
		HandleTriggerEmote(data);
		break;

	case ENB_OPCODE_00A3_CLIENT_CHAT_REQUEST :
		HandleClientChatRequest(data);
		break;

	case ENB_OPCODE_00B9_LOGOFF_REQUEST :
		HandleLogoffRequest(data);
		break;

	case ENB_OPCODE_00BC_CTA_REQUEST :
		HandleCTARequest(data);
		break;

	case ENB_OPCODE_00C0_CONFIRMED_ACTION_RESPONSE:
		HandleActionResponse(data);
		//ProcessConfirmedActionOffer(data);
		break;

	case ENB_OPCODE_00C5_GUILD_LEADER_ACCEPT_CLIENT:
		HandleGuildLeaderAcceptClient(data);
		break;

	case ENB_OPCODE_00C9_GUILD_RECRUIT_ACCEPT_CLIENT:
		HandleRecruitAcceptClient(data);
		break;

	case ENB_OPCODE_00CD_GUILD_SIMPLE_CLIENT_SECTOR:
		HandleGuildSimpleClientSector(data);
		break;

	case ENB_OPCODE_00D4_GUILD_RANK_NAMES_REQUEST_CLIENT:
		HandleGuildRankNamesRequestClient(data);
		break;

		//resend lost/dropped opcodes
	case ENB_OPCODE_2017_RESEND_PACKET_SEQUENCE:
		ReSendOpcodes(data);
		break;

    case ENB_OPCODE_3004_PLAYER_SHIP_SENT:
        //SetConnectionTerminate(); //this will ensure the connection is terminated after 10 seconds
        SetNavCommence();
		FinishLogin(true);
        break;

	case ENB_OPCODE_3008_STARBASE_LOGIN_COMPLETE:
		// fix bad opcode messages
        SetNavCommence();
		break;

	case ENB_OPCODE_2021_LOGIN_STAGE_ACK_C_S:
		HandleLoginAckReturn(data);
		break;

	default:
		LogMessage("Bad opcode for player 0x%08x '%s': %x\n", GameID(), Name(), opcode);
		break;
	}

	SetLastAccessTime(GetNet7TickCount());
}

void Player::HandleLoginStage2()
{
	SectorManager *sm = GetSectorManager();
	if (sm) sm->HandleSectorLogin2(this);
}

void Player::HandleLoginStage3()
{
	SectorManager *sm = GetSectorManager();
	if (sm) sm->HandleSectorLogin3(this);
	SetActive(true);
	LogMessage("player 0x%08x '%s' fully logged in\n", GameID(), Name());
}

void Player::HandleLoginAckReturn(unsigned char *data)
{
	long stage = *((long*) &data[0]);
	//LogMessage("Received login stage ack %d for %s\n", stage, Name());
	SetLoginAck(stage);
}

void Player::HandleLogin(unsigned char *data)
{
	SetLoginStage(0);

	Login * login = (Login *) data;

	m_MasterJoin = login->join_data;

	m_SentStart = false;

	long sector_id = ntohl(m_MasterJoin.ToSectorID);
	PlayerIndex()->SetSectorNum(sector_id);
	if (m_FromSectorID != -1)
		m_FromSectorID = ntohl(m_MasterJoin.FromSectorID);

	SectorReset();

	m_JoinTime = login->TimeSent;

	LogMessage("Handle Sector Login for %s\n", Name());

	SetLoginStage(1);
}

void Player::SendClientSetTime(long TimeSent)
{
	ClientSetTime data;
	data.ClientSent = TimeSent;
	data.ServerReceived = GetNet7TickCount();
	data.ServerSent = data.ServerReceived;

	SendOpcode(ENB_OPCODE_0034_CLIENT_SET_TIME, (unsigned char*) &data, sizeof(data));
}

void Player::SendStarbaseSet(char action, char exit_mode)
{
	StarbaseSet starbase_set;
	memset(&starbase_set, 0, sizeof(starbase_set));
	starbase_set.StarbaseID = PlayerIndex()->GetSectorNum();
	starbase_set.Action = action;
	starbase_set.ExitMode = exit_mode;

	SendOpcode(ENB_OPCODE_004F_STARBASE_SET, (unsigned char *) &starbase_set, sizeof(starbase_set));
}

long Player::TryLoungeFile(long sector_id)
{
	long return_sector = sector_id;
	char old_path[MAX_PATH];
	char lounge_npc[MAX_PATH];

	_snprintf(lounge_npc, sizeof(lounge_npc), "LoungeNPC_%d.dat", sector_id);

	GetCurrentDirectory(sizeof(old_path), old_path);
	SetCurrentDirectory(SERVER_DATABASE_PATH);
	FILE *f;
	fopen_s(&f, lounge_npc, "rb");
	if (!f)
	{
		sector_id = sector_id / 1000;
		//no file exists for this station.
		switch(sector_id) //note: none of these will work and we don't go here anymore
		{
		case 14: //alpha-c
			return_sector = 10601;
			break;
		case 15: //antares
			return_sector = 10521;
			break;
		case 17: //aragoth
			return_sector = 10201;
			break;
		case 19: //capella
			return_sector = 10521;
			break;
		case 22: //
			return_sector = 10651;
			break;
		case 35: //
			return_sector = 10301;
			break;
		case 40: //Tau ceti
			return_sector = 10601;
			break;
		case 41: //sirius
			return_sector = 10551;
			break;
		default:
			return_sector = 10601;
			break;
		}
		//LogMessage("Used lounge NPC of %d\n", return_sector);
	}

	SetCurrentDirectory(old_path);
	return return_sector;
}

void Player::SendDataFileToClient(char *filename, long avatar_id)
{
	char old_path[MAX_PATH];

	GetCurrentDirectory(sizeof(old_path), old_path);
	SetCurrentDirectory(SERVER_DATABASE_PATH);
	FILE *f;
	fopen_s(&f, filename, "rb");
	if (f)
	{
		fseek(f,0,SEEK_END);
		long length = ftell(f);
		if ((length > 0) && (length < UDP_BUFFER_SEND_SIZE + sizeof(EnbUdpHeader)))
		{
			fseek(f,0,SEEK_SET);
			char * buffer = (char*)&m_UDPSendBuffer[0]; //use actual send buffer
			fread(buffer + sizeof(EnbUdpHeader), 1, length, f);

			*((short*) &buffer[0]) = (short)length;
			*((short*) &buffer[2]) = ENB_OPCODE_2010_DATA_FILE;
			*((long *) &buffer[4]) = GameID();

			if (avatar_id)
			{
				*((long *) &buffer[12]) = avatar_id;
			}

			//DumpBuffer((unsigned char*)buffer, length);

			m_UDPConnection->UDP_Send(buffer, length, m_Player_IPAddr, m_Player_Port);
		}
		else
		{
			LogMessage("SendDataFileToClient: Invalid file length %d : '%s'\n", length, filename);
		}
		fclose(f);
	}
	else
	{
		LogMessage("SendDataFileToClient: Unable to open %s\n", filename);
	}

	SetCurrentDirectory(old_path);
}

/*void Player::SendDataFileToClientTCP(char *filename, long avatar_id)
{
	char old_path[MAX_PATH];
	GetCurrentDirectory(sizeof(old_path), old_path);
	SetCurrentDirectory(SERVER_DATABASE_PATH);
	FILE *f;
	fopen_s(&f, filename, "rb");
	if (f)
	{
		fseek(f,0,SEEK_END);
		long length = ftell(f);
		if ((length > 0) && (length < TCP_BUFFER_SIZE))
		{
			fseek(f,0,SEEK_SET);
			unsigned char * buffer = new unsigned char[length];
			fread(buffer, 1, length, f);
			if (avatar_id)
			{
				*((long *) &buffer[4]) = avatar_id;
			}
			if(m_LoginConnection)
			{
				m_LoginConnection->Send(buffer, length);
			}
			delete [] buffer;
		}
		else
		{
			LogMessage("SendDataFileToClient: Invalid file length %d : '%s'\n", length, filename);
		}
		fclose(f);
	}
	else
	{
		LogMessage("SendDataFileToClient: Unable to open %s\n", filename);
	}

	SetCurrentDirectory(old_path);
}*/

void Player::SendConfirmedActionOffer()
{
	unsigned char action_data[] = 
	{
		0x00, 0x00, 0x00, 0x01,
		0x00, 0x00, 0x00, 0x65,
		0x07, 0x00, 
		0x4d, 0x65, 0x73, 0x73, 0x61, 0x67, 0x65
	};

	SendOpcode(ENB_OPCODE_00BE_CONFIRMED_ACTION_OFFER, action_data, sizeof(action_data));	
	SendClientSound("push_mission_alert_sound", 2, 0);
}

void Player::HandleActionResponse(unsigned char *data)
{
	long player_id = ntohl(*((long *) &data[0]));
	if (player_id == GameID())
	{
		m_ActionResponseReceived = true;
		ProcessConfirmedActionOffer();
	}
}

void Player::ProcessConfirmedActionOffer()
{
	char buffer[256];
	memset(&buffer, 0, 256);

	if (m_ActionResponseReceived == false)
	{
		SendConfirmedActionOffer();
	}
	else
	{
		if (m_PushMissionID != 0)
		{
			//now send talk tree as appropriate
			ProposeMissionTree(m_PushMissionID, 1);
			m_ActionResponseReceived = false;
			SendPIPAvatar(-2, m_PushMissionUID, true);
		}
		else
		{
			//is there any other use for push messages?

		}
	}
}

void Player::SendObjectToObjectLinkedEffect(Object *target, Object *source, short effect1, short effect2, float speedup)
{
	unsigned char link_data[128];
	memset(link_data,0,128);
	int index = 0;
	SectorManager *sm = GetSectorManager();

	if (!sm)
	{
		return;
	}

	long effect_id = sm->GetSectorNextObjID();

	AddData(link_data, effect_id, index);
	AddData(link_data, GetNet7TickCount(), index);
	AddData(link_data, source->GameID(), index);
	AddData(link_data, (char)(0), index);       //Unknown spacer
	AddData(link_data, target->GameID(), index);
	AddData(link_data, effect1, index);         //DurationLinkedEffectDescID
	AddData(link_data, effect2, index);         //EffectDescID
	AddData(link_data, 0.0f, index);            //x offset from default target hit zone 
	AddData(link_data, 0.0f, index);            //y offset //NB - we leave these at zero
	AddData(link_data, 0.0f, index);            //z offset //     because the client seems to do a good job
	AddData(link_data, (long)(0), index);       //unknown (doesn't appear to be used)
	AddData(link_data, (char)(1), index);       //outside target radius
	AddData(link_data, (float)(1.0f), index);   //scale
	AddData(link_data, (float)(0.0f), index);   //HSV[0]
	AddData(link_data, (float)(0.0f), index);   //HSV[1]
	AddData(link_data, (float)(0.0f), index);   //HSV[2]
	AddData(link_data, speedup, index);         //speedup

	SendOpcode(ENB_OPCODE_000E_OBJECT_TO_OBJECT_LINKED_EFFECT, link_data, index);
}

void Player::SendClientSound(char *sound_name, long channel, char queue, long warninglevel)
{
	unsigned char packet[128];
	memset(packet,0,128);
	int index = 0;

	if (warninglevel > -1 && warninglevel > m_SoundWarningSetting) //player didn't want to hear this
	{
		return;
	}

	long length = strlen(sound_name) + 1;

	AddData(packet, length, index);
	AddDataS(packet, sound_name, index);
	AddData(packet, char(0), index);
	AddData(packet, channel, index);
	AddData(packet, queue, index);

	SendOpcode(ENB_OPCODE_006A_CLIENT_SOUND, (unsigned char *) &packet[0], index);
}

void Player::SendObjectEffect(ObjectEffect *obj_effect)
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

void Player::PointEffect(float *position, short effect_id, float scale)
{
	/*2C 00            Length = 44 bytes
	0A 00            Opcode 0x0A = Point_Effect
	BF 0C 3A 00 object_id
	24 6A 7E 25 time+200
	18 E4 CA C7 x
	B5 05 19 47 y
	F6 50 98 45 z
	00 00 duration
	F5 03 effect_id
	7A FB 01 43 scale
	00 00 00 00 H
	00 00 00 00 S
	00 00 00 00 V */

	SectorManager *sm = GetSectorManager();

	if (!sm)
	{
		return;
	}

	unsigned char point_data[] = 
	{
		0x00, 0x00, 0x00, 0x00, //0
		0x00, 0x00, 0x00, 0x00, //4
		0x00, 0x00, 0x00, 0x00, //8
		0x01, 0x00, 0x00, 0x00, //12
		0x59, 0x0B, 0x00, 0x00, //16
		0x64, 0x4C, //20
		0x20, 0x25, //22
		0x00, 0x00, 0x00, 0x00, //24
		0x00, 0x00, 0x00, 0x00, //28
		0x00, 0x00, 0x00, 0x00,
		0x00, 0x00, 0x00, 0x00
	};  

	*((long *) &point_data[0]) = sm->GetSectorNextObjID();
	*((long *) &point_data[4]) = GetNet7TickCount() + 200;
	*((float *) &point_data[8]) = position[0];
	*((float *) &point_data[12]) = position[1];
	*((float *) &point_data[16]) = position[2];
	*((short *) &point_data[20]) = 0;
	*((short *) &point_data[22]) = effect_id;
	*((float *) &point_data[24]) = 129.982f * scale;

	SendOpcode(ENB_OPCODE_000A_POINT_EFFECT, point_data, sizeof(point_data));				
}

void Player::SendClientType(long client_type)
{
	//LogMessage("Sending ClientType packet\n");
	SendOpcode(ENB_OPCODE_003C_CLIENT_TYPE, (unsigned char *) &client_type, sizeof(client_type));
}

void Player::SendStart(long start_id)
{
	m_SentStart = true;
	//LogMessage("Sending Start packet %d\n", start_id);
	SendOpcode(ENB_OPCODE_0005_START, (unsigned char *) &start_id, sizeof(start_id));
}

void Player::SendSetBBox(float xmin, float ymin, float xmax, float ymax)
{
	SetBBox bbox;
	bbox.XMin = xmin;
	bbox.YMin = ymin;
	bbox.XMax = xmax;
	bbox.YMax = ymax;

	//LogMessage("Sending SetBBox packet\n");
	SendOpcode(ENB_OPCODE_002B_SET_BBOX, (unsigned char *) &bbox, sizeof(bbox));
}

void Player::SendSetZBand(float min, float max)
{
	SetZBand zband;
	zband.Min = min;
	zband.Max = min;

	//LogMessage("Sending SetZBand packet\n");
	SendOpcode(ENB_OPCODE_002A_SET_ZBAND, (unsigned char *) &zband, sizeof(zband));
}

void Player::SendObjectFull(unsigned char *msg, int index, object_type ot)
{
	short packet;
	switch (ot)
	{
	case OT_RESOURCE:
		packet = ENB_OPCODE_2019_RESOURCE_OBJECT_CREATE;
		break;
	default:
		packet = ENB_OPCODE_2018_STATIC_OBJECT_CREATE;
		break;
	};

	SendOpcode(packet, msg, index);
}

/*
--------------------

12 00            Length = 18 bytes
99 00            Opcode 0x99 = Navigation
6E 01 00 00      GameID
00 C8 2F 47      Sig
00               visited
02 00 00 00      Type
00               Ishuge*/

void Player::SendNavigation(int game_id, float signature, char visited, int nav_type, char is_huge)
{
	Navigation navigation;
	navigation.GameID = game_id;
	navigation.Signature = signature;
	navigation.PlayerHasVisited = visited;
	navigation.NavType = nav_type;
	navigation.IsHuge = is_huge;

	SendOpcode(ENB_OPCODE_0099_NAVIGATION, (unsigned char *) &navigation, sizeof(navigation));
}

void Player::SendCreateAttachment(int parent, int child, int slot)
{
	CreateAttachment attachment;
	attachment.Parent_ID = ntohl(parent);
	attachment.Child_ID = ntohl(child);
	attachment.Slot = ntohl(slot);

	SendOpcode(ENB_OPCODE_004A_CREATE_ATTACHMENT, (unsigned char *) &attachment, sizeof(attachment));
}

void Player::SendDecal(int game_id, int decal_id, int decal_count)
{
	if (decal_count > MAX_DECALS)
	{
		decal_count = MAX_DECALS;
	}

	Decal decal;
	decal.GameID = game_id;
	decal.DecalCount = (short)decal_count;
	for (int i = 0; i < decal_count; i++)
	{
		decal.Item[i].Index = i + 1;
		decal.Item[i].decal_id = decal_id;
		decal.Item[i].HSV[0] = 1.0f;
		decal.Item[i].HSV[1] = 1.0f;
		decal.Item[i].HSV[2] = 1.0f;
		decal.Item[i].opacity = 1.0f;
	}

	size_t size = ((char *) &decal.Item[decal_count]) - ((char *) &decal);

	SendOpcode(ENB_OPCODE_0010_DECAL, (unsigned char *) &decal, size);
}

void Player::SendNameDecal(Player *send_to)
{
	NameDecal name_decal;

	memset(&name_decal, 0, sizeof(name_decal));
	name_decal.GameID = GameID();
	name_decal.RGB[0] = m_Database.ship_data.ship_name_color[0];
	name_decal.RGB[1] = m_Database.ship_data.ship_name_color[1];
	name_decal.RGB[2] = m_Database.ship_data.ship_name_color[2];
	strncpy_s(name_decal.Name, sizeof(name_decal.Name), m_Database.ship_data.ship_name, sizeof(name_decal.Name) - 1);
	name_decal.Name[sizeof(name_decal.Name)-1] = '\0'; //probably redundant

	send_to->SendOpcode(ENB_OPCODE_00B2_NAME_DECAL, (unsigned char *) &name_decal, sizeof(name_decal));
}

void Player::SendConstantPositionalUpdate(long game_id, float x, float y, float z, float *orientation)
{
	ConstantPositionalUpdate update;
	memset(&update, 0, sizeof(update));

	update.GameID = game_id;
	update.Position[0] = x;
	update.Position[1] = y;
	update.Position[2] = z;
	if (orientation)
	{
		update.Orientation[0] = orientation[0];
		update.Orientation[1] = orientation[1];
		update.Orientation[2] = orientation[2];
		update.Orientation[3] = orientation[3];
	}

	SendOpcode(ENB_OPCODE_0040_CONSTANT_POSITIONAL_UPDATE, (unsigned char *) &update, sizeof(update));
}

void Player::SendFormationPositionalUpdate(long leader_id, long target_id, float x, float y, float z)
{
	FormationPositionalUpdate update;
	memset(&update, 0, sizeof(update));

	update.LeaderID = leader_id;
	update.TargetID = target_id;
	update.Position[0] = x;
	update.Position[1] = y;
	update.Position[2] = z;

	Player *leader = g_PlayerMgr->GetPlayer(leader_id);

	//TODO: work out way of smoothing out warp drive.
	//		plan: send 2 of these formation updates after warp drive kicks in to get orientation locked
	//		      then just do normal warp updates
	//	          every time we change direction, repeat the above.

	SendOpcode(ENB_OPCODE_0041_FORMATION_POSITIONAL_UPDATE, (unsigned char *) &update, sizeof(update));
}

void Player::SendSimplePositionalUpdate(long object_id, PositionInformation * position_info)
{
	SimplePositionalUpdate update;
	memset(&update, 0, sizeof(update));

	update.GameID = object_id;
	update.TimeStamp = GetNet7TickCount();
	update.Position[0] = position_info->Position[0];
	update.Position[1] = position_info->Position[1];
	update.Position[2] = position_info->Position[2];
	update.Orientation[0] = position_info->Orientation[0];
	update.Orientation[1] = position_info->Orientation[1];
	update.Orientation[2] = position_info->Orientation[2];
	update.Orientation[3] = position_info->Orientation[3];
	update.Velocity[0] = position_info->Velocity[0];
	update.Velocity[1] = position_info->Velocity[1];
	update.Velocity[2] = position_info->Velocity[2];

	SendOpcode(ENB_OPCODE_0008_SIMPLE_POSITIONAL_UDPATE, (unsigned char *) &update, sizeof(update));
}

void Player::SendPlanetPositionalUpdate(long object_id, PositionInformation * position_info)
{
	PlanetPositionalUpdate update;
	memset(&update, 0, sizeof(update));

	update.GameID = object_id;
	update.TimeStamp = GetNet7TickCount();
	update.Position[0] = position_info->Position[0];
	update.Position[1] = position_info->Position[1];
	update.Position[2] = position_info->Position[2];
	update.OrbitID = position_info->OrbitID;
	update.OrbitDist = position_info->OrbitDist;
	update.OrbitAngle = position_info->OrbitAngle;
	update.OrbitRate = position_info->OrbitRate;
	update.RotateAngle = position_info->RotateAngle;
	update.RotateRate = position_info->RotateRate;
	update.TiltAngle = position_info->TiltAngle;

	SendOpcode(ENB_OPCODE_003F_PLANET_POSITIONAL_UPDATE, (unsigned char *) &update, sizeof(update));
}

void Player::SendComponentPositionalUpdate(long object_id, PositionInformation * position_info, long timestamp)
{
	ComponentPositionalUpdate update;
	memset(&update, 0, sizeof(update));

	update.simple.GameID = object_id;
	if (timestamp)
	{
		update.simple.TimeStamp = timestamp;
	}
	else
	{
		update.simple.TimeStamp = GetNet7TickCount();
	}
	update.simple.Position[0] = position_info->Position[0];
	update.simple.Position[1] = position_info->Position[1];
	update.simple.Position[2] = position_info->Position[2];
	update.simple.Orientation[0] = position_info->Orientation[0];
	update.simple.Orientation[1] = position_info->Orientation[1];
	update.simple.Orientation[2] = position_info->Orientation[2];
	update.simple.Orientation[3] = position_info->Orientation[3];
	update.simple.Velocity[0] = position_info->Velocity[0];
	update.simple.Velocity[1] = position_info->Velocity[1];
	update.simple.Velocity[2] = position_info->Velocity[2];
	update.ImpartedDecay = position_info->ImpartedDecay;
	update.TractorSpeed = position_info->TractorSpeed;
	update.TractorID = position_info->TractorID;
	update.TractorEffectID = position_info->TractorEffectID;

	SendOpcode(ENB_OPCODE_0046_COMPONENT_POSITIONAL_UPDATE, (unsigned char *) &update, sizeof(update));
}

void Player::SendAdvancedPositionalUpdate(long object_id, PositionInformation * position_info)
{
	char packet[sizeof(AdvancedPositionalUpdate)];
	memset(packet, 0, sizeof(packet));
	short *pBitmask = (short *) &packet[0];
	long *pLong = (long *) &packet[2];
	float *pFloat = (float *) &packet[2];
	short bitmask = position_info->Bitmask;
	int index = 0;

	// Package the data into the packet
	*pBitmask = bitmask;
	pLong[index++] = object_id;           // GameID
	pLong[index++] = GetNet7TickCount();      // TimeStamp
	pFloat[index++] = position_info->Position[0];
	pFloat[index++] = position_info->Position[1];
	pFloat[index++] = position_info->Position[2];
	pFloat[index++] = position_info->Orientation[0];
	pFloat[index++] = position_info->Orientation[1];
	pFloat[index++] = position_info->Orientation[2];
	pFloat[index++] = position_info->Orientation[3];
	pLong[index++] = position_info->MovementID;
	if (bitmask & 0x0001)
	{
		pFloat[index++] = position_info->CurrentSpeed;
	}
	if (bitmask & 0x0002)
	{
		pFloat[index++] = position_info->SetSpeed;
	}
	if (bitmask & 0x0004)
	{
		pFloat[index++] = position_info->Acceleration;
	}
	if (bitmask & 0x0008)
	{
		pFloat[index++] = position_info->RotY;
	}
	if (bitmask & 0x0010)
	{
		pFloat[index++] = position_info->DesiredY;
	}
	if (bitmask & 0x0020)
	{
		pFloat[index++] = position_info->RotZ;
	}
	if (bitmask & 0x0040)
	{
		pFloat[index++] = position_info->DesiredZ;
	}
	if (bitmask & 0x0080)
	{
		pFloat[index++] = position_info->ImpartedVelocity[0];
		pFloat[index++] = position_info->ImpartedVelocity[1];
		pFloat[index++] = position_info->ImpartedVelocity[2];
		pFloat[index++] = position_info->ImpartedSpin;
		pFloat[index++] = position_info->ImpartedRoll;
		pFloat[index++] = position_info->ImpartedPitch;
	}
	if (bitmask & 0x0100)
	{
		pLong[index++] = position_info->UpdatePeriod;
	}

	int length = 2 + 4 * index;
	SendOpcode(ENB_OPCODE_003E_ADVANCED_POSITIONAL_UPDATE, (unsigned char *) &packet, length);

	//is this to ourselves?
	if (this->GameID() == object_id)
	{
		Sleep(1);
	}
}

void Player::SendObjectToObjectEffect(ObjectToObjectEffect *obj_effect)
{
	unsigned char effect[128];
	memset(effect,0,128);
	int index = 0;

	AddData(effect, obj_effect->Bitmask, index);
	AddData(effect, obj_effect->GameID, index);
	AddData(effect, obj_effect->TargetID, index);
	AddData(effect, obj_effect->EffectDescID, index);

	if (obj_effect->Message)
	{
		AddDataS(effect, obj_effect->Message, index);
	}
	else
	{
		AddData(effect, (char)0, index);
	}

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
		AddData(effect, obj_effect->TargetOffset[0], index);
		AddData(effect, obj_effect->TargetOffset[1], index);
		AddData(effect, obj_effect->TargetOffset[2], index);
	}
	if (obj_effect->Bitmask & 0x10)
	{
		AddData(effect, obj_effect->OutsideTargetRadius, index);
	}
	if (obj_effect->Bitmask & 0x20) //from here on isn't correct - packet struct in packetstructures.h is wrong... TODO: work out correct packet structure.
	{
		AddData(effect, obj_effect->unused, index);
	}
	if (obj_effect->Bitmask & 0x40)
	{
		AddData(effect, obj_effect->Scale, index);
	}
	if (obj_effect->Bitmask & 0x80)
	{
		AddData(effect, obj_effect->HSVShift[0], index);
		AddData(effect, obj_effect->HSVShift[1], index);
		AddData(effect, obj_effect->HSVShift[2], index);
	}
	if (obj_effect->Bitmask & 0x100)
	{
		AddData(effect, obj_effect->Speedup, index);
	}

	SendOpcode(ENB_OPCODE_000B_OBJECT_TO_OBJECT_EFFECT, effect, index);
}

void Player::SendActivateRenderState(long game_id, unsigned long render_state_id)
{
	ActivateRenderState state;

	state.GameID = game_id;
	state.RenderStateID = render_state_id;

	//LogMessage("Sending ActivateRenderState packet\n");
	SendOpcode(ENB_OPCODE_0030_ACTIVATE_RENDER_STATE, (unsigned char *) &state, sizeof(state));
}


//TODO: Find out more about packet data structure - this is just a guess.
void Player::SendInitRenderState(long game_id, unsigned long render_state_id)
{
	InitRenderState state;

	state.GameID = game_id;
	state.RenderStateID = render_state_id;

	SendOpcode(ENB_OPCODE_002F_INIT_RENDER_STATE, (unsigned char *) &state, sizeof(state));
}

void Player::SendActivateNextRenderState(long game_id, unsigned long render_state_id)
{
	ActivateRenderState state;

	state.GameID = game_id;
	state.RenderStateID = render_state_id;

	//LogMessage("Sending ActivateRenderState packet\n");
	SendOpcode(ENB_OPCODE_0031_ACTIVATE_NEXT_RENDER_STATE, (unsigned char *) &state, sizeof(state));
}

void Player::SendDeactivateRenderState(long game_id)
{
	/*ActivateRenderState state;

	state.GameID = game_id;
	state.RenderStateID = render_state_id;*/

	//LogMessage("Sending ActivateRenderState packet\n");
	SendOpcode(ENB_OPCODE_0032_DEACTIVATE_RENDER_STATE, (unsigned char *) &game_id, sizeof(game_id));
}

// keep track of each GameID sent in each 

void Player::SendCreate(int game_id, float scale, short asset, int type, float h, float s, float v)
{
	Create  create;
	create.GameID = game_id;
	create.Scale = scale;
	create.BaseAsset = asset;
	create.Type = (char) type;
	create.HSV[0] = h;
	create.HSV[1] = s;
	create.HSV[2] = v;

#ifdef TEST_CREATE
	if (m_CheckList[game_id] == true)
	{
		LogMessage("Error: sending same objectID twice in one sector session\n");
	}
	else
	{
		m_CheckList[game_id] = true;
	}
#endif

	//LogMessage("Sending Create packet\n");
	SendOpcode(ENB_OPCODE_0004_CREATE, (unsigned char *) &create, sizeof(create));
}

void Player::HandleSkillStringRequest(unsigned char *data)
{
	ClientSkillsRequest * request = (ClientSkillsRequest *) data;

	//Loot item
	//not sure if this packet is used for anything other than looting (doesn't appear to be).

	//check we're targetting a HUSK
	ObjectManager *om = GetObjectManager();
	Object *obj = (0);
	if (om) obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());

	if (obj && obj->ObjectType() == OT_HUSK)
	{
		//Now check no-one else is looting this object, and loot timer not active
		Object *player = obj->CheckResourceLock();
		if (player && player != this)
		{
			SendVaMessage("%s currently being looted by %s", obj->Name(), player->Name());
		}
		//loot lock still engaged, and this player is not the credited kill player
		else if (obj->GetPlayerLootLock() != 0 && 
			GetNet7TickCount() < obj->GetLootTime() && 
			obj->GetPlayerLootLock() != this->GameID()) 
		{
			player = om->GetObjectFromID(obj->GetPlayerLootLock());
			if (player == NULL) //player has logged out
			{
				goto loot_as_normal;
			}
			
			//if the player is grouped
			if(GroupID() != -1)
			{
				Group *g = g_PlayerMgr->GetGroupFromID(GroupID());
				//If the player is in a group with FFA looting, ensure this player is in the group
				if(g && g->AutoReleaseLootingRights)
				{
					for(int i = 0; i < 6; ++i)
					{
						if(player->GameID() == g_PlayerMgr->GetGroupFromID(GroupID())->Member[i].GameID)
							goto loot_as_normal;
					}
				}
				//if this is a master-looter group, make sure that only the leader can loot
				if(g && g->RestrictedLootingRights)
				{
					if(player->GameID() == g_PlayerMgr->GetGroupFromID(GroupID())->Member[0].GameID)
						goto loot_as_normal;
				}
			}		
			SendVaMessage("%s credited to %s for %.1f seconds", 
				obj->Name(), player->Name(), ((float)obj->GetLootTime() - (float)GetNet7TickCount())*0.001f);
		}
		else
		{
loot_as_normal:
			long id = ntohl(obj->GameID());
			SendOpcode(ENB_OPCODE_008C_LOOT_HULK_PERMISSION, (unsigned char *)&id, sizeof(long));
			//see if there are any credits
			AwardCreditsToGroup(obj->GetCreditLoot());
			obj->SetCreditLoot(0);

			//check if MOB loot is empty
			if (obj->ResourceRemains() == 0.0f)
			{
				obj->SendObjectDrain(-1);
			}
			else
			{
				m_ProspectWindow = true;
			}

			//now mark husk as being available to everyone
			obj->SetPlayerLootLock(0);
			obj->SetLootTimer(0);
		}
	}
}

void Player::HandleStartAck(unsigned char *data)
{
	LogDebug("Received StartAck packet\n");

	SetActive(true);

	// Added to do things on sector change at top of 'Player' class
	// don't put them here!!

	//send the login camera just after we finally log in
	if (PlayerIndex()->GetSectorNum() < MAX_SECTOR_ID)
	{
		SendLoginCamera();
	}
}

void Player::HandleRequestTime(unsigned char *data)
{
	LogDebug("Received RequestTime packet\n");

	// Set the client time
	SendClientSetTime(*((long *) data));
}


//m_ChannelSubscription

//I think this also handles requests to subscribe to certain channels
void Player::HandleClientChatRequest(unsigned char *data)
{
	ClientChatRequest * request = (ClientChatRequest *) data;

	//Not having these initalized caused a runtime checking exception.
	char * string1 = NULL;
	char * string2 = NULL;
	char * string3 = NULL;

	char * p_scratch = (char *)m_ScratchBuffer;

	short length1 = request->string_length1;
	char *p = ((char *) &request->string_length1);
	p += sizeof(short);
	long buffer_remaining = MAXIMUM_PACKET_CACHE - 1024; //cut to 3*1024
	if (length1 > 0)
	{
		string1 = p_scratch;
		memcpy_s(string1, (buffer_remaining-1), p, length1);
		string1[length1] = 0;
		p += length1;
		p_scratch += length1 + 1;
		buffer_remaining -= (length1 + 1);
	}
	short length2 = *((short *) p);
	p += sizeof(short);
	if (length2 > 0)
	{
		string2 = p_scratch;
		memcpy_s(string2, (buffer_remaining-1), p, length2);
		string2[length2] = 0;
		p += length2;
		p_scratch += length2 + 1;
		buffer_remaining -= (length2 + 1);
	}
	short length3 = *((short *) p);
	p += sizeof(short);
	if (length3 > 0)
	{
		string3 = p_scratch;
		memcpy_s(string3, (buffer_remaining-1), p, length3);
		string3[length3] = 0;
		p += length3;
	}
	long data_size = *((long *) p);

	// Friends Request
	switch(request->type)
	{
		case CCE_ADD_FRIEND:
			AddFriend(string1);
			break;
		case CCE_REMOVE_FRIEND:
			RemoveFriend(string1);
			break;
		case CCE_IGNORE:
			AddIgnore(string1);
			break;
		case CCE_UNIGNORE:
			RemoveIgnore(string1);
			break;
		case CCR_FRIEND_STATUS_ONLY:
			m_StatusToFriendsOnly = true;
			SendClientChatEvent(CHEV_FRIEND_STATUS_ONLY,this);
			break;
		case CCR_ANYONE_STATUS:
			m_StatusToFriendsOnly = false;
			SendClientChatEvent(CHEV_ALL_STATUS,this);
			break;
		case CCR_LIST_IGNORES:
			ListIgnores();
			break;
		case CCR_LIST_FRIENDS:
			ListFriends();
			return;
		break;
		default:
			break;
	}

	//sprintf(Nick, "%s", string1);
	//sprintf(Message, "%s", string3);
	//LogMessage("ClientChatRequest: SendToChannel: %s Message: %s Type: %d\n", string2, string3, request->type);
	if (request->type == CCE_SPEAK_LOCALLY)
	{
		if(string3 != NULL)
		{
			if( string3[0] == '/')
			{
				HandleSlashCommands(string3);
			}
			else
			{
				//wouldn't do anything if string3 were null anyway
				g_ServerMgr->m_PlayerMgr.ChatSendPrivate(GameID(), string1, string3);
			}
		}
	}
	else if (request->type == CCE_SPEAK_ON)		// Channel Message
	{
		//LogMessage("ClientChatRequest: SendToChannel: %s Message: %s\n", string2, string3);
		if(string3 != NULL)
		{
			if (string3[0] == '/')
			{
				HandleSlashCommands(string3);
			}
			else
			{
				//wouldn't do anything if string3 were null anyway
				if (Hijackee()) DoVrixEncoding(string3);
				g_ServerMgr->m_PlayerMgr.ChatSendChannel(GameID(), string2, string3);
			}
		}
	}
	else if (request->type == CCE_ENTER_CHANNEL)
	{
		//subscribe to channel
		int channel_id = g_PlayerMgr->GetChannelFromName(string2);  //null input ok
		if(channel_id != INVALID_CHANNEL)
		{
			if ((strcmp(string2, "Errors") == 0 && AdminLevel() < BETA_PLUS) ||
				(strcmp(string2, "Staff") == 0 && AdminLevel() < BETA_PLUS) ||
				(strcmp(string2, "GM") == 0 && AdminLevel() < GM) ||
				(strcmp(string2, "Dev") == 0 && AdminLevel() < DEV))
				m_ChannelSubscription[channel_id] = false;
			else
				m_ChannelSubscription[channel_id] = true;
		}
		// joining a specific private channel comes in here, string2 is the channel
	}
	else if (request->type == CCE_EXIT_CHANNEL)
	{
		int channel_id = g_PlayerMgr->GetChannelFromName(string2); //null input ok
		if(channel_id != INVALID_CHANNEL)
		{
			m_ChannelSubscription[channel_id] = false;
		}
	}
	else if (request->type == CCE_INSERT_CHANNEL)
	{
		// request to create a new private channel
	}
	else if (request->type == CCR_LIST_CHANNELS)
	{
		SendVaMessage("Request channel list");
	}
	else if (request->type == CCR_LIST_ALL_CHANNELS)
	{
		SendVaMessage("Request all channels list");
	}
	else if (request->type == CCR_SECTOR_LOGIN)
	{
		// this is received after every sector change
		// all the strings are blank
	}
}

void Player::HandleTurn(unsigned char *data)
{
	struct PacketTurn {
		long GameID;
		float Intensity;
	} ATTRIB_PACKED;

	PacketTurn * Turning = (PacketTurn *)  data;

	if (!WarpDrive())
	{
		AbortProspecting(true,false);
		Turn(Turning->Intensity);
	}
}

void Player::HandleTilt(unsigned char *data)
{
	struct PacketTurn {
		long GameID;
		float Intensity;
	} ATTRIB_PACKED;

	PacketTurn * Turning = (PacketTurn *)  data;

	if (!WarpDrive())
	{
		AbortProspecting(true,false);
		Tilt(Turning->Intensity);
	}
}

void Player::HandleMove(unsigned char *data)
{
	MovePacket * Movement = (MovePacket *) data;

	if (!WarpDrive())
	{
		AbortProspecting(true,false);

		// Break formation if we are in a group & formed
		if (GroupID() != -1)
		{
			// If we are the leader we can move the whole group
			if (g_ServerMgr->m_PlayerMgr.GetMemberID(this->GroupID(), 0) != this->GameID())
			{
				g_ServerMgr->m_PlayerMgr.LeaveFormation(GameID());
			}
		}

		if (Movement->type == 4)
		{
			SendContrailsRL(false);
			g_ServerMgr->m_PlayerMgr.FormationEngineOperation(this, false);
		}
		else
		{
			SendContrailsRL(true);
			g_ServerMgr->m_PlayerMgr.FormationEngineOperation(this, true);
		}

		Move(Movement->type);
	}
}

void Player::HandleWarp(unsigned char *data)
{
	WarpPacket * warp = (WarpPacket *) data;

	if (!CheckForInstalls())
	{
		SendMessageString("Cannot initiate warp - installation in progress.", 17, false);
		return;
	}

	if (WarpDrive())
	{
		TerminateWarpGroup(true);
	}
	else
	{
		// Make sure we are both in formation & Group leader
		if (g_ServerMgr->m_PlayerMgr.CheckGroupFormation(this) && g_ServerMgr->m_PlayerMgr.GetMemberID(this->GroupID(), 0) == this->GameID())
		{
			// Start up warps if you are in a formation
			for(int x=0;x<6;x++)
			{
				int PlayerID = g_ServerMgr->m_PlayerMgr.GetMemberID(this->GroupID(), x);
				if (PlayerID > 0)
				{
					Player* pid = g_ServerMgr->m_PlayerMgr.GetPlayer(PlayerID);

					if (g_ServerMgr->m_PlayerMgr.CheckGroupFormation(pid))
					{
						// first check none of the other players are installing stuff, if so break formation (take that, 'sploiters!)
						if (!pid->CheckForInstalls())
						{
							pid->SendMessageString("Cannot initiate group warp - installation in progress - dropped from formation.", 17);
							g_ServerMgr->m_PlayerMgr.LeaveFormation(PlayerID);
						}
						else
						{
							pid->SendContrailsRL(false);
							pid->SetupWarpNavs(warp->Navs, warp->TargetID);
							pid->PrepareForWarp();
						}
					}
				}
			}
		}
		else
		{
			// Break formation if we are in a group & formed
			if (GroupID() != -1)
			{
				g_ServerMgr->m_PlayerMgr.LeaveFormation(GameID());
			}
			SendContrailsRL(false);
			LogDebug("Warp Navs: %ld, GameID=%d (%s)\n", warp->Navs, (warp->GameID & 0x00FFFFFF), Name());
			SetupWarpNavs(warp->Navs, warp->TargetID);
			PrepareForWarp();
		}
	}
}

void Player::Contrails(long player_id, bool contrails)
{
	unsigned char aux_data[] = 
	{
		0x00, 0x00, 0x00, 0x00,
		0x13, 0x00,
		0x01, 
		0x02, 0x00, 0x00, 0x00, 0x00, 0x00,	0x04, 0x00, 
		0x00, 0x00, 0x00, 0x00
	};

	if (contrails == true)
	{
		*((long*) &aux_data[15]) = 1;
	}

	*((long *) aux_data) = player_id;

	SendOpcode(ENB_OPCODE_001B_AUX_DATA, aux_data, sizeof(aux_data));
}

void Player::SendResourceName(long resourceID, char *resource_name)
{
	unsigned char aux_data[64]; 
	memset(aux_data, 0, 64);
	short length = strlen(resource_name);
	*((long *) aux_data) = resourceID;
	*((short *) &aux_data[4]) = length + 4;
	*((short *) &aux_data[6]) = 0x1201;
	*((short *) &aux_data[8]) = length;

	strncpy_s((char*)&aux_data[10], sizeof(aux_data) - 10, resource_name, length);
	aux_data[sizeof(aux_data)-1] = '\0';

	SendOpcode(ENB_OPCODE_001B_AUX_DATA, aux_data, length+10);	
}

//this is the same way that the live server sent husk content, but it is horrendously inefficient
//I wonder if there's a better way we can send Husk content
//we can re-do this with Net7Proxy so it's optimal
void Player::SendHuskContent(Object *husk)
{
	bool contents_already_sent;

	if (husk)
	{
		contents_already_sent = husk->GetIndex(ResourceSendList());

		if (!contents_already_sent)
		{
			unsigned char spacer[4] =
			{
				0x36, 0x00, 0xFF
			};
			unsigned char filler[8] =
			{                    
				0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF
			};
			unsigned char prologue1[] =
			{
				0xF6, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F
			};
			unsigned char prologue2[] =
			{
				0xF6, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F
			};

			unsigned char aux_data[1024];
			int index = 0;
			int x;

			long length = husk->NameLen();
			AddData(aux_data, husk->GameID(), index);
			AddData(aux_data, (short)0x0000, index);
			AddData(aux_data, (long) (0x0FEC1601), index);
			AddData(aux_data, (short)(length), index);
			AddDataS(aux_data, husk->Name(), index);
			AddData(aux_data, (long)0x05050505, index);

			ContentSlot *slot;

			AddBuffer(aux_data, prologue1, 6, index);

			for (x = 0; x < 20; x++)
			{
				AddBuffer(aux_data, filler, 7, index);
			}

			AddBuffer(aux_data, prologue2, 11, index);

			for (x = 0; x < MAX_ITEMS_PER_RESOURCE; x++)
			{
				slot = husk->GetContents(x);
				if (slot->stack > 0 && slot->item)
				{
					AddBuffer(aux_data, spacer, 3, index);
					AddData(aux_data, slot->item->ItemTemplateID(), index);
					AddData(aux_data, long(slot->stack), index);
					SendItemBase(slot->item->ItemTemplateID());
				}
				else
				{
					AddBuffer(aux_data, filler, 7, index);
				}
			}

			for (x = 0; x < 40-MAX_ITEMS_PER_RESOURCE; x++)
			{
				AddBuffer(aux_data, filler, 7, index);
			}

			//set length
			*((short *) &aux_data[4]) = index-8;

			SendOpcode(ENB_OPCODE_001B_AUX_DATA, aux_data, index);
			husk->SetIndex(ResourceSendList());
		}
	}
}

void Player::SendHuskName(Object *husk)
{
	unsigned char aux_data[256]; 
	memset(aux_data, 0, 256);
	int index = 0;

	char *name = unknown_corpse;
	int length = 7;

	if (husk->Name())
	{
		name = husk->Name();
		length = husk->NameLen();
	}

	AddData(aux_data, husk->GameID(), index);
	AddData(aux_data, (short)(length+10), index);		// Lengh of Aux Packet
	AddData(aux_data, (long) (0x03E01601), index);
	AddData(aux_data, (short)(length), index);
	AddDataS(aux_data, name, index);
	AddData(aux_data, (long)0x05050505, index);

	if (index >= 256)
	{
		LogMessage("**ERROR**: HuskNameIndex overflow error, %s, index len = %d\n", name, index);
	}

	SendOpcode(ENB_OPCODE_001B_AUX_DATA, aux_data, index);	
}

void Player::SendMobName(Object *mob)
{
	unsigned char packet[340];
	int index = 0;

	unsigned char epilogue[] =
	{
		0x05, 0x05, 0x05, 0x05, 
		0x05, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00  
	};

	unsigned char aux_data[] = 
	{
		0x3B, 0x00, //AUX length [4]
		0x01, 
		0x16, 0x30, 0x00, 0x40, 0x00, 0x00, 0x0C, 0x80, 0x03, 
		0x00, 0xBE, 0x40, 0xF8, 0xC0, 0x07, 
	};

	AddData  (packet, (long)mob->GameID(), index);
	AddBuffer(packet, aux_data, sizeof(aux_data), index);
	AddDataLS(packet, mob->Name(), index);
	AddData  (packet, (float)mob->GetHullLevel(), index);
	AddData  (packet, (float)mob->GetStartHullLevel(), index);
	AddData  (packet, (long)mob->Level(), index);
	AddBuffer(packet, epilogue, sizeof(epilogue), index);

	*((short *) &packet[4]) = index - 6;

	SendOpcode(ENB_OPCODE_001B_AUX_DATA, packet, index); 
}

void Player::SendSimpleAuxName(Object *obj)
{
	int index = 0;
	int length = obj->NameLen();
	unsigned char packet[256];

	AddData  (packet, obj->GameID(), index);
	AddData  (packet, (short) 0, index);
	AddData  (packet, (short) (0x1201), index);

	if (length < 246)
	{
		AddDataLS(packet, obj->Name(), index);
	}
	else
	{
		AddDataLS(packet, "Name of this object too long", index);
	}

	*((short *) &packet[4]) = index - 6;

	SendOpcode(ENB_OPCODE_001B_AUX_DATA, (unsigned char *) packet, index);    
}

void Player::SendAuxNameSignature(Object *obj)
{
	char *name = "d";
	char nav = 0;
	if (obj->IsNav() || obj->ObjectType() == OT_FIELD)
	{
		if (obj->Name() != 0) 
		{
			name = obj->Name();
		}
		nav = 1;
	}

	float sig = obj->Signature();

	if (sig < 3000.0f) sig = 3000.0f; //ship sensors aren't that crap

	int length = strlen(name);
	char *packet = (char*)_alloca(length + 15);
	*((long *) packet) = obj->GameID();
	*((short *) &packet[4]) = length + 9;
	packet[6] = 0x01;
	packet[7] = 0x72;
	*((short *) &packet[8]) = length;
	strncpy_s(&packet[10], length + 5, name, length);//this string has info beyond the end of the string.
	int i = 10 + length;
	packet[i++] = nav;
	*((float *) &packet[i]) = sig;

	SendOpcode(ENB_OPCODE_001B_AUX_DATA, (unsigned char *) packet, length+15);    
}

void Player::SendAuxNameResource(Object *obj)
{
	char packet[256];
	char *name = unknown_corpse;
	int length = 7;

	if (obj->Name())
	{
		name = obj->Name();
		length = obj->NameLen();
	}

	*((long *)  &packet[0]) = obj->GameID();
	*((short *) &packet[4]) = length + 5;
	packet[6] = 0x01;
	packet[7] = 0x16;
	packet[8] = 0x04;
	*((short *) &packet[9]) = length;
	strncpy_s(&packet[11], sizeof(packet)-11, name, length);

	SendOpcode(ENB_OPCODE_001B_AUX_DATA, (unsigned char *) packet, length + 11);
}

void Player::UnSetTarget(long GameID)
{
	if (ShipIndex()->GetTargetGameID() == GameID)
	{
		ObjectManager *obj_manager = GetObjectManager();
		if (obj_manager)
		{
			Object *oldtarget = obj_manager->GetObjectFromID(ShipIndex()->GetTargetGameID());
			if (oldtarget)
			{
				oldtarget->OnUnTargeted(this);
			}
		}

		if (m_ProspectWindow)
		{
			m_ProspectWindow = false;
			OpenInterface(1,0);
		}
		//LogMessage("Sending SetTarget packet\n");
		SendSetTarget(0, -1);

		ShipIndex()->SetTargetGameID(-1);
		SendAuxShip();
		BlankVerbs();
	}
}

void Player::ActivateProspectBeam(long player_id, long target_id, char *message, short effect_type, long effectUID, long timestamp, short effect_time)
{
	ObjectToObjectEffect Prospect;

	if (effect_time == 0)
	{
		Prospect.Bitmask = 0x03;
	}
	else
	{
		Prospect.Bitmask = 0x07;
	}
	Prospect.GameID = player_id;
	Prospect.TargetID = target_id;
	Prospect.EffectDescID = effect_type;// 0x00BF;
	Prospect.Message = message;
	Prospect.EffectID = effectUID;
	Prospect.Duration = short(effect_time);
	Prospect.TimeStamp = timestamp;

	SendObjectToObjectEffect(&Prospect);
}

void Player::SendPushMessage(char *msg1, char *type, long time, long priority)
{
	unsigned char packet[512];
	memset(packet,0,512);
	unsigned char *pptr = &packet[0];
	int index = 0;

	if (strlen(msg1) > 480) msg1[481] = 0;

	AddDataS(pptr, msg1, index);
	AddData(pptr, char(0), index);
	AddDataS(pptr, type, index);
	AddData(pptr, char(0), index);
	AddData(pptr, time, index);
	AddData(pptr, priority, index);

	SendOpcode(ENB_OPCODE_0022_PUSH_MESSAGE, pptr, index);
}

void Player::SetResourceDrainLevel(Object *obj, long slot)
{
	unsigned long slot_index = ((0x10 << slot) | 0x02); //slot index calc
	u16 length = 28;
	//Control which resource gets removed and how much of the resource is left
	unsigned char aux_data[] = 
	{
		0x00, 0x00, 0x00, 0x00, 
		0x16, 0x00, 
		0x01, // 6
		0x62, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x32, 0x00, //the second byte value determines which resource to remove ((0x10 << slot) | 0x02)
		0xFE, 0xFF, 0xFF, 0xFF, 
		0x00, 0x00, 0x00, 0x00, 
		0x00, 0x00, 0x00, 0x00  // amount of colour left in resource - 0 is collapse
	};

	*((long *) aux_data) = obj->GameID();
	*((long *) &aux_data[8]) = slot_index;
	*((float *) &aux_data[24]) = obj->ResourceRemains();

	if (obj->GetStack(slot) > 0)
	{
		length = 24;
		*((char *) &aux_data[4]) = 0x12;  //new size
		*((char *) &aux_data[14]) = 0x22; //indicates partial removal
		*((long *) &aux_data[16]) = obj->GetStack(slot) ; //resource remaining in this slot
		*((float *) &aux_data[20]) = obj->ResourceRemains(); 
	}

	SendOpcode(ENB_OPCODE_001B_AUX_DATA, aux_data, length); //this seems to initiate resource collapse or drains the resource according to last float val
}

void Player::SetHuskDrainLevel(Object *obj, long slot)
{
	unsigned long slot_index = ((0x10 << slot) | 0x02); //slot index calc
	unsigned char packet[128];
	memset(packet,0,128);
	unsigned char *pptr = &packet[0];
	int index = 0;

	unsigned char aux_data[] = 
	{
		0x01, 0x02, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x32, 0x00
	} ;

	AddData(pptr, obj->GameID(), index);
	AddData(pptr, short(0), index);
	AddBuffer(pptr, aux_data, 11, index);
	AddData(pptr, 0xFFFFFFFE, index);
	AddData(pptr, long(0), index);

	*((long *) &pptr[9]) = slot_index;
	*((short *) &pptr[4]) = (index - 6); //set info length

	SendOpcode(ENB_OPCODE_001B_AUX_DATA, pptr, index);
}

void Player::RemoveObject(long object_id)
{
	UnSetTarget(object_id);
	SendOpcode(ENB_OPCODE_0007_REMOVE, (unsigned char *) &object_id, sizeof(object_id)); //remove the raw resource
#ifdef TEST_CREATE
	m_CheckList[object_id] = false;
#endif
}

//This is where I put all the AUX prospecting stuff I don't really understand
//any help decoding any of this appreciated!

//TODO: REMOVE THIS!!
void Player::SendProspectAUX(long value, int type)
{
	switch (type)
	{
	case 0:
		{
			//non-parsemode AuxPlayer 
			//sets prospect skill last activation time
			unsigned char aux_data[] = 
			{
				0x00, 0x00, 0x00, 0x00, 
				0x15, 0x00,
				0x00, 
				0x01, 0x00, 0x00, 0x00,
				0x59, 0x0B, 0x00, 0x00, //always this for prospecting
				0x64, 0x4C, 0x20, 0x25, //timestamp...
				0x00, 0x00, 0x00, 0x00,  
				0x00, 0x00, 0x00, 0x00
			};

			*((long *) &aux_data[15]) = value;

			SendOpcode(ENB_OPCODE_001B_AUX_DATA, aux_data, sizeof(aux_data));
		}
		break;

	case 1:
		{
			//second type of AUX prospecting requires to be sent
			//diables the users cloak and advancd cloak abilities
			unsigned char aux_data[] = 
			{
				0x00, 0x00, 0x00, 0x00, 
				0x1D, 0x00, 
				0x00, 
				0x02, 0x00, 0x00, 0x00, 
				0x15, 0x0C, 0x00, 0x00, 
				0x00, 0x01, 0x00, 0x00, 
				0xF5, 0x0C, 0x00, 0x00, 
				0x00, 0x01, 0x00, 0x00, 
				0x00, 0x00, 0x00, 0x00, 
				0x00, 0x00, 0x00, 0x00   
			}; //no idea what this does, but always the same for prospect. Maybe some effect?

			SendOpcode(ENB_OPCODE_001B_AUX_DATA, aux_data, sizeof(aux_data));
		}
		break;

	case 2:
		{
			ShipIndex()->SetTargetGameID(0);
			SendAuxShip();
			BlankVerbs();
		}
		break;

	case 3:
		{
			//AuxShip Packet (most likely "disables" weapons)
			unsigned char aux_data[] = 
			{
				0x00, 0x00, 0x00, 0x00, 
				0x1C, 0x00, 
				0x01, 
				0x02, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, //inventory
				0x02, 0x08,										//equip inventory
				0x22, 0x08, 0x00,								//items 1 and 7
				0x02, 0x00,	0x01,								//equipitem flags
				0x10, 0x20, 0x00, 0x00,							//itemstats
				0x02, 0x00, 0x01,								//equipitem flags
				0x10, 0x20, 0x00, 0x00							//itemstats
			};
			*((long *) aux_data) = value;
			SendOpcode(ENB_OPCODE_001B_AUX_DATA, aux_data, sizeof(aux_data));
		}
		break;

	case 4:
		{
			//re-enables the users cloak and advancd cloak abilities
			unsigned char aux_data[] = 
			{
				0x00, 0x00, 0x00, 0x00, 
				0x1D, 0x00, 
				0x00, 
				0x02, 0x00, 0x00, 0x00, 
				0x15, 0x0C, 0x00, 0x00, 
				0x00, 0x00, 0x00, 0x00, 
				0xF5, 0x0C, 0x00, 0x00, 
				0x00, 0x00, 0x00, 0x00, 
				0x00, 0x00, 0x00, 0x00, 
				0x00, 0x00, 0x00, 0x00   
			}; //no idea what this does, but always the same for prospect. Maybe some effect?

			SendOpcode(ENB_OPCODE_001B_AUX_DATA, aux_data, sizeof(aux_data));
		}
		break;
	}
}

void Player::CreateTractorComponent(float *position, float decay, float tractor_speed, long player_id, long article_id, long effect_id, long timestamp)
{
	PositionInformation pos_info;

	memset(&pos_info, 0, sizeof(PositionInformation));

	pos_info.Position[0] = position[0];
	pos_info.Position[1] = position[1];
	pos_info.Position[2] = position[2];
	pos_info.Orientation[3] = 1.0f;
	pos_info.ImpartedDecay	= decay;
	pos_info.TractorSpeed	= tractor_speed;
	pos_info.TractorID		= player_id;//playerID
	pos_info.TractorEffectID= effect_id;

	SendComponentPositionalUpdate(
		article_id,
		&pos_info, 
		timestamp);	
}

void Player::HandleInventoryMove(unsigned char *data)
{
	// if you are incapacited you cant equip items
	if(m_Gating)
	{
		return;
	}
	if (ShipIndex()->GetIsIncapacitated())
	{
		SendMessageString("Unable to move inventory while incapacitated.", 11);
		return;
	}

	ObjectManager *om = GetObjectManager();
	InvMove * Inventory = (InvMove *) data;
	InvMove InvMo;

	_Item Source, Destination;

	InvMo.FromInv = ntohl(Inventory->FromInv);
	InvMo.FromSlot = ntohl(Inventory->FromSlot);
	InvMo.ToSlot = ntohl(Inventory->ToSlot);
	InvMo.ToInv = ntohl(Inventory->ToInv);
	InvMo.GameID = ntohl(Inventory->GameID);
	InvMo.Num = ntohl(Inventory->Num);

	LogDebug("Inventory Move - GameID: %ld From %ld Slot: %ld To: %ld Slot %ld Number: %ld\n", InvMo.GameID,InvMo.FromInv,
		InvMo.FromSlot, InvMo.ToInv, InvMo.ToSlot, InvMo.Num);

	//you can only move certain items from certain places (cannot equip from vault, ect)
	
	switch(InvMo.FromInv)
	{
		// From Cargo Inventory
	case 1:
		m_Mutex.Lock();
		Source = *ShipIndex()->Inventory.CargoInv.Item[InvMo.FromSlot].GetData();
		m_Mutex.Unlock();
		if (InvMo.ToInv == 1)	//cargo to cargo
		{
			m_Mutex.Lock();
			Destination = *ShipIndex()->Inventory.CargoInv.Item[InvMo.ToSlot].GetData();
			//TODO: check for client hacks: use what the server thinks the client has
			CheckStack(InvMo.Num, &Source, &Destination);
			ShipIndex()->Inventory.CargoInv.Item[InvMo.ToSlot].SetData(&Source);
			ShipIndex()->Inventory.CargoInv.Item[InvMo.FromSlot].SetData(&Destination);
			m_Mutex.Unlock();

			SendAuxShip();

			SaveInventoryChange(InvMo.FromSlot);
			SaveInventoryChange(InvMo.ToSlot);
			//TODO: request to save inv slots
		}
		else if(InvMo.ToInv == 2)	//equip from cargo
		{
			//first check this slot has valid content, if not, reset
			m_Equip[InvMo.ToSlot].InvalidType(InvMo.ToSlot);

			if (!m_Equip[InvMo.ToSlot].CanEquip(&Source))
			{
				break;
			}

			//If we are moving ammo, they can stack if they are the same itemid
			m_Mutex.Lock();
			if (ShipIndex()->Inventory.AmmoInv.Item[InvMo.ToSlot].GetItemTemplateID() == Source.ItemTemplateID)
			{
				Destination = *ShipIndex()->Inventory.AmmoInv.Item[InvMo.ToSlot].GetData();
				CheckStack(InvMo.Num, &Source, &Destination);
				m_Mutex.Unlock();
				m_Equip[InvMo.ToSlot].Equip(&Source);
			}
			else
			{
				m_Mutex.Unlock();
				Destination = m_Equip[InvMo.ToSlot].Equip(&Source);
			}
			
			if(Destination.ItemTemplateID == -2)
			{
				Destination = g_ItemBaseMgr->EmptyItem;
			}

			m_Mutex.Lock();
			ShipIndex()->Inventory.CargoInv.Item[InvMo.FromSlot].SetData(&Destination);
			m_Mutex.Unlock();

			SendAuxShip();
			SaveInventoryChange(InvMo.FromSlot);
		}
		else if(InvMo.ToInv == 3)	//cargo to vault
		{
			if (InvMo.ToSlot == -1)
			{
				//pick first free vault slot
				m_Mutex.Lock();
				InvMo.ToSlot = FindFreeVaultSpace(Source.ItemTemplateID, Source.StackCount);
				m_Mutex.Unlock();
				if (InvMo.ToSlot == -1)
				{
					SendVaMessageC(17,"No free vault slots");
					break;
				}
			}

			//check source is not a 'trade' item.
			ItemBase * myItem = g_ItemBaseMgr->GetItem(Source.ItemTemplateID);

			if (myItem && myItem->Category() == 90)
			{
				SendVaMessageC(17,"Cannot move trade item into vault.");
				break;
			}
			
			m_Mutex.Lock();
			Destination = *PlayerIndex()->SecureInv.Item[InvMo.ToSlot].GetData();
			CheckStack(InvMo.Num, &Source, &Destination);
			PlayerIndex()->SecureInv.Item[InvMo.ToSlot].SetData(&Source);
			ShipIndex()->Inventory.CargoInv.Item[InvMo.FromSlot].SetData(&Destination);
			m_Mutex.Unlock();

			SendAuxPlayer();
			SendAuxShip();
			SaveInventoryChange(InvMo.FromSlot);
			SaveVaultChange(InvMo.ToSlot);
		}
		else if(InvMo.ToInv == 4)	//selling cargo
		{
			if (Source.ItemTemplateID < 0)		// Keeps down XP hacking
			{
				break;
			}

			if (Source.Price <= 0)
			{
				SendVaMessage("Vendor does not want this item!");
				break;
			}

			ItemBase * myItem = g_ItemBaseMgr->GetItem(Source.ItemTemplateID);

			if (!myItem || myItem->TechLevel() > 9) //not allowed to sell items of level 10 or above to vendors (fixes grail water XP hack).
			{
				break;
			}

			if (myItem->Flags() & ITEM_FLAGS_NO_DESTROY)
			{
				SendVaMessage("You can not sell this item!");
				break;
			}

			//make sure player actually has this amount, they are not hacking
			if ((u32)InvMo.Num > Source.StackCount)
			{
				InvMo.Num = (long)Source.StackCount;
			}

			u32 trade_stack = (u32)InvMo.Num < Source.TradeStack ? (u32)InvMo.Num : Source.TradeStack;

			if (Source.StackCount <= (u32)InvMo.Num)
			{
				Destination = g_ItemBaseMgr->EmptyItem;
			}
			else
			{
				Destination = Source;
				Destination.StackCount -= InvMo.Num;
				Destination.TradeStack -= InvMo.Num;
			}

			long XP_earned = CalcItemStackTradeXP(&Source, (u32)InvMo.Num);

		//	LogMessage("Trade Stack = %d  Profit = %d\n", trade_stack, (Source.Price - (u64)Source.AveCost));

			if (AdminLevel() >= DEV && trade_stack > 0)
			{
				long profit = (long) (Source.Price - (u64)Source.AveCost);
				if (profit < 0) profit = 0;
				SendVaMessage("Profit per item: %d", profit);
			}

			if (myItem && myItem->Category() == 90) // Damage value based on quality of trade cargo
			{
				AwardCredits((long) (Source.Price * InvMo.Num * Source.Structure), XP_earned);
			} 
			else 
			{

				float qualityFactor  = 1.0f;
				float structureFactor = 1.0f;
				float creditsAwarded = 0.0f;

				// -1% to price for each 2% quality below 100
				if (Source.Quality < 1.0f)
					qualityFactor = 1.0f - ((1.0f - Source.Quality)*0.5f);
				// +1% to price for each 4% quality above 100 
				if (Source.Quality > 1.0f)
					qualityFactor = 1.0f + ((Source.Quality - 1.0f) * 0.25f);
				// -1% to price for each 2% structure below 100
				if (Source.Structure < 1.0f)
					structureFactor = 1.0f - ((1.0f - Source.Structure)*0.5f);
				// +1% to price for each 4% structure above 100 
				if (Source.Structure > 1.0f)
					structureFactor = 1.0f + ((Source.Structure - 1.0f) * 0.25f);

				creditsAwarded = Source.Price * InvMo.Num * qualityFactor * structureFactor;

				// Items only sell for half their price * quality and structure factors
				AwardCredits((u64)creditsAwarded, XP_earned);
			}

			m_Mutex.Lock();
			ShipIndex()->Inventory.CargoInv.Item[InvMo.FromSlot].SetData(&Destination);
			m_Mutex.Unlock();

			SendAuxShip();
			SaveInventoryChange(InvMo.FromSlot);
		}            
		else if(InvMo.ToInv == 11)	//spaceing item
		{
			ItemBase * myItem = g_ItemBaseMgr->GetItem(Source.ItemTemplateID);
			if (myItem && (myItem->Flags() & ITEM_FLAGS_NO_DESTROY))
			{
				SendVaMessageC(17,"That item cannot be destroyed.");
			}
			else
			{
				Destination = Source;
				Destination.StackCount -= InvMo.Num;

				if (Destination.StackCount <= 0)
				{
					Destination = g_ItemBaseMgr->EmptyItem;
				}
				
				m_Mutex.Lock();
				ShipIndex()->Inventory.CargoInv.Item[InvMo.FromSlot].SetData(&Destination);
				m_Mutex.Unlock();

				SendAuxShip();
				SaveInventoryChange(InvMo.FromSlot);
			}
		}
		// Result box in Manufacturing
		else if(InvMo.ToInv == 12 && (ManuIndex()->GetMode() == 2 || ManuIndex()->GetMode() == 3))
		{ 
			m_Mutex.Lock();
			Destination = *ManuIndex()->Target.Item[0].GetData();
			m_Mutex.Unlock();
			if( !(Source.StackCount > 1 && CargoFreeSpace() == 0 && Destination.ItemTemplateID != -1))
			{
				if(AnalyseDismantleSetItem(&Source))
				{
					if(Source.StackCount > 1)
					{
						//decrement the stack count
						Source.StackCount--;
						//Write source back into the cargo
						m_Mutex.Lock();
						ShipIndex()->Inventory.CargoInv.Item[InvMo.FromSlot].SetData(&Source);
						m_Mutex.Unlock();
						//save the change
						//add the item that was in the slot back to our inventory.
						CargoAddItem(&Destination);
						SendAuxShip();
						SaveInventoryChange(InvMo.FromSlot);
					}
					else
					{
						//just swap the items
						m_Mutex.Lock();
						ShipIndex()->Inventory.CargoInv.Item[InvMo.FromSlot].SetData(&Destination);
						m_Mutex.Unlock();

						SendAuxShip();
						SaveInventoryChange(InvMo.FromSlot);
					}
					
				}
			}
		}
		// Control Interface box in Manufacturing
		else if(InvMo.ToInv == 14 && ManuIndex()->GetMode() != 4)
		{
			m_Mutex.Lock();
			Destination = *ManuIndex()->Override.Item[0].GetData();
			m_Mutex.Unlock();
			// This does not work at this time
			/*
			ShipIndex()->Inventory.CargoInv.Item[InvMo.FromSlot].SetData(&Destination);
			ManuIndex()->Override.Item[0].SetData(&Source);
			_SendAuxShip();
			SendAuxManu();
			SaveInventoryChange(InvMo.FromSlot);
			*/
		}
		else if(InvMo.ToInv == 16)	//cargo to trade
		{
			m_Mutex.Lock();
			Destination = *ShipIndex()->Inventory.TradeInv.Item[InvMo.ToSlot].GetData();
			m_Mutex.Unlock();

			if (m_TradeID == -1)
			{
				break;
			}

			ItemBase * myItem = g_ItemBaseMgr->GetItem(Source.ItemTemplateID);

			if ((AdminLevel() < GM) && myItem)
			{
				// trade goods
				if (myItem->Category() == IB_CATEGORY_TRADE_GOOD)
				{
					SendVaMessageC(17,"Cannot trade goods.");
					break;
				}
				// Don't move non tradeable Items into the tradewindow...
				if (myItem->Flags() & ITEM_FLAGS_NO_TRADE)
				{
					SendVaMessageC(17,"You can not trade an untradeable item.");
					break;
				}
			}

			m_Mutex.Lock();
			long item_id = ShipIndex()->Inventory.CargoInv.Item[InvMo.FromSlot].GetItemTemplateID();
			m_Mutex.Unlock();

			Player * targetp;
			targetp = g_ServerMgr->m_PlayerMgr.GetPlayer(m_TradeID);
			if (targetp)
			{
				targetp->SendItemBase(Source.ItemTemplateID);
				
				m_Mutex.Lock();
				ShipIndex()->Inventory.CargoInv.Item[InvMo.FromSlot].SetData(&Destination);
				m_Mutex.Unlock();

				SendAuxShip();
				
				m_Mutex.Lock();
				ShipIndex()->Inventory.TradeInv.Item[InvMo.ToSlot].SetData(&Source);
				m_Mutex.Unlock();

				SendAuxShip(targetp);
				targetp->TradeAction(0,6); // cancel confirmations
				TradeAction(0,6);		   // cancel confirmations
				targetp->m_TradeConfirm = 0;
				m_TradeConfirm = 0;

				SaveInventoryChange(InvMo.FromSlot);
				SaveTradeChange(InvMo.ToSlot);
			}
		}
		break;

		// From Equip Inventory
	case 2:
		m_Mutex.Lock();
		Source = *ShipIndex()->Inventory.EquipInv.EquipItem[InvMo.FromSlot].GetItemData();
		m_Mutex.Unlock();

		if (InvMo.ToInv == 1)	//unequip item
		{
			m_Mutex.Lock();
			Destination = *ShipIndex()->Inventory.CargoInv.Item[InvMo.ToSlot].GetData();
			m_Mutex.Unlock();

			if (!m_Equip[InvMo.FromSlot].CanEquip(&Destination))
			{
				break;
			}
			
			Source = m_Equip[InvMo.FromSlot].Equip(&Destination);
			
			m_Mutex.Lock();
			ShipIndex()->Inventory.CargoInv.Item[InvMo.ToSlot].SetData(&Source);
			m_Mutex.Unlock();

			SendAuxShip();
			SaveInventoryChange(InvMo.ToSlot);
		}
		else if(InvMo.ToInv == 2)	//move equip item
		{
			//If one of these is Reactor, Engine or Shield, you cant move them
			if (InvMo.FromSlot < 3 || InvMo.ToSlot < 3)
			{
				break;
			}

			//If trying to move a weapon to device, or a device to weapon, prevent it
			if ((InvMo.FromSlot < 9 && InvMo.ToSlot > 8) || (InvMo.FromSlot > 8 && InvMo.ToSlot < 9))
			{
				break;
			}
			
			m_Mutex.Lock();
			Destination = *ShipIndex()->Inventory.EquipInv.EquipItem[InvMo.ToSlot].GetItemData();
			m_Mutex.Unlock();
			//Moving two devices, just exchange them
			if (InvMo.FromSlot > 8 || InvMo.ToSlot > 8)
			{
				Source = m_Equip[InvMo.FromSlot].Equip(&Destination);
				m_Equip[InvMo.ToSlot].Equip(&Source);
			}
			//Now we are exchanging two weapon slots, the problem is that they can have ammo
			//If either weapon has ammo
			else
			{
				m_Mutex.Lock();
				if (ShipIndex()->Inventory.AmmoInv.Item[InvMo.ToSlot].GetItemTemplateID() != -2 ||
					ShipIndex()->Inventory.AmmoInv.Item[InvMo.FromSlot].GetItemTemplateID() != -2)
				{
					Source = *ShipIndex()->Inventory.AmmoInv.Item[InvMo.FromSlot].GetData();
					Destination = *ShipIndex()->Inventory.AmmoInv.Item[InvMo.ToSlot].GetData();
					m_Mutex.Unlock();
					//if the weapons use the same ammo, swap the ammo in them, otherwise do nothing
					if (m_Equip[InvMo.ToSlot].CorrectAmmo(&Source))
					{
						CheckStack(InvMo.Num, &Source, &Destination);
						m_Equip[InvMo.FromSlot].EquipAmmo(&Destination);
						m_Equip[InvMo.ToSlot].EquipAmmo(&Source);
					}
					else
					{
						break;
					}
				}
				else
				{
					//If neither have ammo, just swap them normally
					m_Mutex.Unlock();
					Source = m_Equip[InvMo.FromSlot].Equip(&Destination);
					m_Equip[InvMo.ToSlot].Equip(&Source);
				}
			}

			SendAuxShip();
		}
		break;

		// From Vault Inventory
	case 3:
		m_Mutex.Lock();
		Source = *PlayerIndex()->SecureInv.Item[InvMo.FromSlot].GetData();
		m_Mutex.Unlock();

		if(InvMo.ToInv == 11 || InvMo.ToInv == -1)	//destroy item
		{
			ItemBase * myItem = g_ItemBaseMgr->GetItem(Source.ItemTemplateID);
			if (myItem && (myItem->Flags() & ITEM_FLAGS_NO_DESTROY))
			{
				SendVaMessageC(17,"That item cannot be destroyed.");
			}
			else
			{
				Destination = Source;
				Destination.StackCount -= InvMo.Num;

				if (Destination.StackCount <= 0)
				{
					Destination = g_ItemBaseMgr->EmptyItem;
				}
				
				m_Mutex.Lock();
				PlayerIndex()->SecureInv.Item[InvMo.FromSlot].SetData(&Destination);
				m_Mutex.Unlock();

				SendAuxPlayer();
				SaveVaultChange(InvMo.FromSlot);
			}
		}
		else if (InvMo.ToInv == 1)	//move from vault to cargo
		{
			if(InvMo.ToSlot == -1) //vault to unspecified cargo slot
			{
				//find an empty inventory slot or somewhere to stack this object
				if (CargoAddItemCount(Source.ItemTemplateID,Source.Quality) >= Source.StackCount)
				{
					CargoAddItem(&Source);
					m_Mutex.Lock();
					PlayerIndex()->SecureInv.Item[InvMo.FromSlot].SetData(&g_ItemBaseMgr->EmptyItem);
					m_Mutex.Unlock();
				}
				else
				{
					SendVaMessageC(17,"No free cargo slots");
					break;
				}
			}
			else
			{
				m_Mutex.Lock();
				Destination = *ShipIndex()->Inventory.CargoInv.Item[InvMo.ToSlot].GetData();
				m_Mutex.Unlock();

				//check source is not a 'trade' item.
				ItemBase * myItem = g_ItemBaseMgr->GetItem(Destination.ItemTemplateID);

				if (myItem && myItem->Category() == 90)
				{
					SendVaMessageC(17,"Cannot move trade item into vault.");
					break;
				}

				CheckStack(InvMo.Num, &Source, &Destination);

				m_Mutex.Lock();
				ShipIndex()->Inventory.CargoInv.Item[InvMo.ToSlot].SetData(&Source);
				PlayerIndex()->SecureInv.Item[InvMo.FromSlot].SetData(&Destination);
				m_Mutex.Unlock();

				SaveInventoryChange(InvMo.ToSlot);
			}
			SendAuxShip();					
			SendAuxPlayer();
			SaveVaultChange(InvMo.FromSlot);
		}
		else if(InvMo.ToInv == 3)	//vault to vault
		{
			m_Mutex.Lock();
			Destination = *PlayerIndex()->SecureInv.Item[InvMo.ToSlot].GetData();
			CheckStack(InvMo.Num, &Source, &Destination);

			PlayerIndex()->SecureInv.Item[InvMo.ToSlot].SetData(&Source);
			PlayerIndex()->SecureInv.Item[InvMo.FromSlot].SetData(&Destination);
			m_Mutex.Unlock();

			SendAuxPlayer();
			SaveVaultChange(InvMo.ToSlot);
			SaveVaultChange(InvMo.FromSlot);
		}
		break;

		// From Vendor Inventory
	case 4:
		m_Mutex.Lock();
		Source = *PlayerIndex()->VendorInv.Item[InvMo.FromSlot].GetData();
		m_Mutex.Unlock();

		if (InvMo.ToInv == 1)	//buy item
		{
			ItemBase * myItem = g_ItemBaseMgr->GetItem(Source.ItemTemplateID);

			u64 Cost = Source.Price * InvMo.Num;

			m_Mutex.Lock();
			u64 credits = PlayerIndex()->GetCredits();
			m_Mutex.Unlock();

			if (credits < Cost)
			{
				SendVaMessageC(17,"Insufficient credits!");
				break;
			}

			if (Source.Price <= 0)
			{
				SendVaMessageC(17,"Vendor will not sell this trade item.");
				break;
			}

			if (myItem->Category() == 90) //trade goods
			{
				//if this is a trade good we just bought, we don't want to recalculate the price!
				Source.TradeStack = InvMo.Num;
			}
			/*
			else
			{
				//not a trade good, reacalculate the price.
				Source.Price = Negotiate(GetVenderBuyPrice(Source.ItemTemplateID),false,true);
			}
			*/
			
		
			// Set the buy price for the item

			//vendor markup
			
			Source.StackCount = InvMo.Num;	// Trade this many items
			Source.Quality = 1.0f;
			Source.Structure = 1.0f;
			if (CargoAddItem(&Source) == 0)	//check we have enough space first
			{
				PlayerIndex()->SetCredits(PlayerIndex()->GetCredits() - Cost);
				SaveCreditLevel();
				SetPrices();
				SendAuxPlayer();
				SendAuxShip();
			}
			else
			{
				SendVaMessageC(17,"Insufficient space in cargo hold.");
			}
		}
		break;

		// From Loot Window
	case 6:
		{	
			Object *obj = 0;
			if (ShipIndex()->GetIsIncapacitated())
			{
				SendMessageString("Unable to loot while incapacitated.", 11);
				break;
			}

			if (om) obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
			if (obj)
			{
				switch (obj->ObjectType())
				{
				case OT_HUSK:
					// let the verb icon greying out handle the range check
					LootItem(InvMo.FromSlot, false);
					break;

				default:
					LogMessage("Attempt to loot %s [%d]\n", obj->Name(), obj->GameID());
				}
			}
			else
			{
				LogMessage("Attempt to loot invalid object :%d\n", ShipIndex()->GetTargetGameID());
			}
		}
		break;

		// Manufacturing Target
	case 12:
		if (InvMo.ToInv == 1 && (ManuIndex()->GetMode() == 2 || ManuIndex()->GetMode() == 3))
		{
			/*
			Source = *ManuIndex()->Target.Item[0].GetData();

			if (InvMo.ToSlot == -1)
			{
			CargoAddItem(&Source);
			ManuIndex()->Target.Item[0].Empty();
			for(int i=0;i<6;i++)
			ManuIndex()->Components.Item[i].Empty();
			ManuIndex()->SetValidity(VALIDITY_NO_TARGET);
			SendAuxShip();
			SendAuxManu();
			}
			else if (InvMo.ToInv == 1)
			{
			Destination = *ShipIndex()->Inventory.CargoInv.Item[InvMo.ToSlot].GetData();

			ShipIndex()->Inventory.CargoInv.Item[InvMo.ToSlot].SetData(&Source);
			ManuIndex()->Target.Item[0].SetData(&Destination);
			for(int i=0;i<6;i++)
			ManuIndex()->Components.Item[i].Empty();
			ManuIndex()->SetValidity(VALIDITY_NO_TARGET);
			SaveInventoryChange(InvMo.ToSlot);
			SendAuxShip();
			SendAuxManu();
			}*/
			
		}
		break;

		// Manufacturing Override
	case 14:
		if (InvMo.ToInv == 1 && ManuIndex()->GetMode() != 4)
		{
			Source = *ManuIndex()->Override.Item[0].GetData();

			if (InvMo.ToSlot == -1)
			{
				CargoAddItem(&Source);

				m_Mutex.Lock();
				ManuIndex()->Override.Item[0].Empty();
				m_Mutex.Unlock();

				SendAuxShip();
				SendAuxManu();
			}
			else if (InvMo.ToInv == 1)
			{
				m_Mutex.Lock();
				Destination = *ShipIndex()->Inventory.CargoInv.Item[InvMo.ToSlot].GetData();
				ShipIndex()->Inventory.CargoInv.Item[InvMo.ToSlot].SetData(&Source);
				ManuIndex()->Override.Item[0].SetData(&Destination);
				m_Mutex.Unlock();

				SaveInventoryChange(InvMo.ToSlot);
				SendAuxShip();
				SendAuxManu();
			}
		}
		break;

		// From Trade Window
	case 16:
		Source = *ShipIndex()->Inventory.TradeInv.Item[InvMo.FromSlot].GetData();

		if (InvMo.ToInv == 1 && InvMo.ToSlot > 0)	//back to inventory
		{
			Destination = *ShipIndex()->Inventory.CargoInv.Item[InvMo.ToSlot].GetData();

			if (m_TradeID == -1)
			{
			
				break;
			}

			Player * targetp;
			targetp = g_ServerMgr->m_PlayerMgr.GetPlayer(m_TradeID);
			if (targetp)
			{
				targetp->SendItemBase(Source.ItemTemplateID);
				
				ShipIndex()->Inventory.CargoInv.Item[InvMo.ToSlot].SetData(&Source);

				SaveInventoryChange(InvMo.ToSlot);
				SendAuxShip();
				
				ShipIndex()->Inventory.TradeInv.Item[InvMo.FromSlot].SetData(&Destination);

				SaveTradeChange(InvMo.FromSlot);
				SendAuxShip(targetp);
				targetp->TradeAction(0,6); // cancel confirmations
				TradeAction(0,6);		   // cancel confirmations
				targetp->m_TradeConfirm = 0;
				m_TradeConfirm = 0;
			}
		}
		break;

		// From Mining Window
	case 18:
		{
			Object *obj = 0;
			if (om) obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
			if (obj)
			{
				switch (obj->ObjectType())
				{
				case OT_HUSK:
					LootItem(InvMo.FromSlot, true);
					break;

				case OT_HULK:
				case OT_RESOURCE:
					MineResource(InvMo.FromSlot);
					break;

				default:
					LogMessage("Attempt to loot/mine %s [%d]\n", obj->Name(), obj->GameID());
				}
			}
			else
			{
				LogMessage("Attempt to mine invalid object :%d\n", ShipIndex()->GetTargetGameID());
			}
		}
		break;

	default:
		LogMessage("UNRECOGNISED INVENTORY MOVE -- GameID: %ld From: %ld Slot: %ld To: %ld Slot %ld Number: %ld\n", InvMo.GameID,InvMo.FromInv,
			InvMo.FromSlot, InvMo.ToInv, InvMo.ToSlot, InvMo.Num);

		SendVaMessage("UNRECOGNISED INVENTORY MOVE!\nPlease submit a bug report\n");
		break;
	}
}

long InvSortType[3];
char InvSortRev;
int __cdecl InvSortFunc(const void *a, const void *b)
{
	ItemBase *item1 = g_ItemBaseMgr->GetItem(((_Item *)a)->ItemTemplateID);
	ItemBase *item2 = g_ItemBaseMgr->GetItem(((_Item *)b)->ItemTemplateID);
	int ret=0;

	if (item1 && item2)
	{
		for (int d=0;d < 3;d++)
		{
			switch (InvSortType[d])
			{
			case 1: // name
				ret = strcmp(item1->Name(), item2->Name());
				break;
			case 5: // category
				ret = item1->Category() - item2->Category();
				break;
			case 10: // value
				ret = item1->VendorSellPrice() - item2->VendorSellPrice();
				break;
			case 4: // unknown secondary
			case 8: // unknown tertiary
			default:
				break;
			}
			// stop at a difference
			if (ret)
				break;
		}
	}
	else if (item2) return  1;
	else if (item1) return -1;
	return InvSortRev ? -ret : ret;
}

void Player::HandleInventorySort(unsigned char *data)
{
	InvSort *RevData = (InvSort *)data;
	InvSort Data;

	u32 tick = GetNet7TickCount();
	if (tick < (m_LastSort + 10000))
	{
		SendVaMessageC(17, "Cargo sort machine resetting, please try in a few seconds.");
		return;
	}

	m_LastSort = tick;

	Data.ID        = ntohl(RevData->ID);
	Data.TargetInv = ntohl(RevData->TargetInv);
	Data.Sort1     = ntohl(RevData->Sort1);
	Data.Sort2     = ntohl(RevData->Sort2);
	Data.Sort3     = ntohl(RevData->Sort3);
	Data.Reverse   = RevData->Reverse;

	InvSortType[0] = Data.Sort1;
	InvSortType[1] = Data.Sort2;
	InvSortType[2] = Data.Sort3;
	InvSortRev = Data.Reverse;
	switch (Data.TargetInv)
	{
	case 1: // cargo
		{
			// cant sort in situ because that bypasses the packet change detection system
			_Inventory40 InvCopy;
			memcpy(&InvCopy, ShipIndex()->Inventory.CargoInv.GetData(), sizeof(InvCopy));
			qsort(InvCopy.Item, ShipIndex()->Inventory.GetCargoSpace(), sizeof(_Item), InvSortFunc);
			for (long x=0;x < (long)ShipIndex()->Inventory.GetCargoSpace();x++)
			{
				s32 before = ShipIndex()->Inventory.CargoInv.Item[x].GetItemTemplateID();
				ShipIndex()->Inventory.CargoInv.Item[x].SetData(&InvCopy.Item[x]);
				s32 after = ShipIndex()->Inventory.CargoInv.Item[x].GetItemTemplateID();

				//save cargo positions at logout
				if (before != -1 || after != -1)
				{
					SaveInventoryChange(x);
				}
			}
			SendAuxShip();
			break;
		}
	case 3: // vault
		{
			// cant sort in situ because that bypasses the packet change detection system
			_SecureInv InvCopy;
			memcpy(&InvCopy, PlayerIndex()->SecureInv.GetData(), sizeof(InvCopy));
			qsort(InvCopy.Item, 96, sizeof(_Item), InvSortFunc);
			for (long x=0;x < 96;x++)
			{
				s32 before = PlayerIndex()->SecureInv.Item[x].GetItemTemplateID();
				PlayerIndex()->SecureInv.Item[x].SetData(&InvCopy.Item[x]);
				s32 after = PlayerIndex()->SecureInv.Item[x].GetItemTemplateID();

				//save vault positions at logout
				if (before != -1 || after != -1)
				{
					SaveVaultChange(x);
				}
			}
			SendAuxPlayer();
			break;
		}
	default:
		LogMessage("unknown InvSort request id:%x inv:%d sort1:%d sort2:%d sort3:%d rev:%d\n",Data.ID,Data.TargetInv,Data.Sort1,Data.Sort2,Data.Sort3,(int)Data.Reverse);
	}
}

void Player::HandleItemState(unsigned char *data)
{
	ItemState * Data = (ItemState *) data;

	if (Data->Inventory == 2)
	{
		m_Mutex.Lock();
		long ItemState = ShipIndex()->Inventory.EquipInv.EquipItem[Data->ItemNum].GetItemState();

		if (Data->Enable == 1)
		{
			ItemState |= Data->BitMask;
		}
		else
		{
			ItemState &= ~Data->BitMask;
		}

		ShipIndex()->Inventory.EquipInv.EquipItem[Data->ItemNum].SetItemState(ItemState);
		m_Mutex.Unlock();
		SendAuxShip();
	}
	else
	{
		LogMessage("UNRECOGNISED ITEM STATE:\n");
		DumpBuffer(data, sizeof(ItemState));

		SendVaMessage("UNRECOGNISED ITEM STATE!\nPlease submit a bug report\n");
	}
}

void Player::HandleRequestTarget(unsigned char *data)
{
	RequestTarget * request = (RequestTarget *) data;
	ObjectManager *obj_manager = GetObjectManager();
	Object *newtarget = NULL, *oldtarget = NULL;

	if (obj_manager)
	{
		newtarget = obj_manager->GetObjectFromID(request->TargetID);
		oldtarget = obj_manager->GetObjectFromID(ShipIndex()->GetTargetGameID());
	}

	//LogMessage("Received RequestTarget packet\n");

	if (m_ProspectWindow == true)
	{
		m_ProspectWindow = false;
		OpenInterface(1,0);
	}

	SendSetTarget(request->TargetID, -1);

	ShipIndex()->SetTargetGameID(request->TargetID);
	BlankVerbs();

	if (oldtarget)
	{
		oldtarget->OnUnTargeted(this);
	}
	if (newtarget)
	{
		newtarget->OnTargeted(this);
	}

	if (newtarget && newtarget->ObjectType() == OT_MOB) 
	{
		int Lvl = newtarget->Level();
		char Threat[40];
		_snprintf(Threat, sizeof(Threat), "Level %d", Lvl);
		ShipIndex()->SetTargetThreat(Threat);
		//ShipIndex()->SetTargetThreatLevel(Lvl);		// If Player Use this
	} 
	else if (newtarget && newtarget->ObjectType() == OT_PLAYER) 
	{
		Player *p = g_ServerMgr->m_PlayerMgr.GetPlayer(request->TargetID);

		if (p)
		{
			int LvlDif = p->CombatLevel() - CombatLevel();
			char Threat[40];

			if (LvlDif >= 8)
			{
				strcpy_s(Threat, sizeof(Threat), "Impossible");
				Threat[sizeof(Threat)-1] = '\0';
			} 
			else if (LvlDif >= 5)
			{
				strcpy_s(Threat, sizeof(Threat), "Very Hard");
				Threat[sizeof(Threat)-1] = '\0';
			} 
			else if (LvlDif >= 2)
			{
				strcpy_s(Threat, sizeof(Threat), "Hard");
				Threat[sizeof(Threat)-1] = '\0';
			} 
			else if (LvlDif >= -1)
			{
				strcpy_s(Threat, sizeof(Threat), "Even");
				Threat[sizeof(Threat)-1] = '\0';
			} 
			else if (LvlDif >= -4)
			{
				strcpy_s(Threat, sizeof(Threat), "Easy");
				Threat[sizeof(Threat)-1] = '\0';
			} 
			else if (LvlDif >= -7)
			{
				strcpy_s(Threat, sizeof(Threat), "Very Easy");
				Threat[sizeof(Threat)-1] = '\0';
			} 
			else 
			{
				strcpy_s(Threat, sizeof(Threat), "Pathetic");
				Threat[sizeof(Threat)-1] = '\0';
			}

			ShipIndex()->SetTargetThreat(Threat);
		}
	} 
	else 
	{
		ShipIndex()->SetTargetThreatLevel(0);
		ShipIndex()->SetTargetThreat("");
		//check missions
		if (newtarget && newtarget->GetUsedInMission() && newtarget->RangeFrom(Position()) <= 5000.0f)
		{
			CheckMissions(newtarget->GetDatabaseUID(), 1, newtarget->GetDatabaseUID(), TALK_SPACE_NPC);
			CheckForNewMissions(newtarget->GetDatabaseUID(), 1, newtarget->GetDatabaseUID());
		}
	}
	SendAuxShip();
}

void Player::HandleRequestTargetsTarget(unsigned char *data)
{
	RequestTarget * request = (RequestTarget *) data;
	Player *p = g_ServerMgr->m_PlayerMgr.GetPlayer(request->TargetID);

	if (p)
	{
		*((int *) &data[4]) = p->ShipIndex()->GetTargetGameID();
		HandleRequestTarget(data);
	}
	else
	{
		SendSetTarget(0, -1);
		ShipIndex()->SetTargetGameID(-1);
		SendAuxShip();
	}
}

bool Player::CheckResourceLock(long object_id)
{
	Player * player = 0; 
	ObjectManager *om = GetObjectManager();
	Object *obj = (0);
	if (om) obj = om->GetObjectFromID(object_id);

	if (obj)
	{
		player = obj->CheckResourceLock();
	}

	if (player && player != this)
	{
		SendVaMessage("%s is being mined by %s", obj->Name(), player->Name());
		return false;
	}
	else
	{
		return true;
	}
}

long Player::CurrentResourceTarget()
{
	if (m_ProspectWindow)
	{
		return (ShipIndex()->GetTargetGameID());
	}
	else
	{
		return 0;
	}
}

void Player::HandleVerbRequest(unsigned char *data)
{
	//Here is a list of VerbID's for HUD icons:
	//01 - scan;	02 - land;		03 - loot
	//04 - group;	05 - message;	06 - trade
	//07 - tractor;	08 - Dock;		09 - Prospect
	//0a - gate;	0b - register	0c - jumpstart
	//0d - follow

	//And Attributes:
	//00 - Enabled	 (all others disabled)
	//01 - Player already in group
	//02 - Too far
	//03+ - Unavailable -- Disabled with no reason given

	VerbRequest * pkt = (VerbRequest *) data;

	long subject_id = (long) ntohl(pkt->SubjectID);
	long object_id = (long) ntohl(pkt->ObjectID);

	//LogMessage("Received VerbRequest packet {SubjectID=%d, ObjectID=%d, Action=%d}\n",
	//    subject_id, object_id, pkt->Action);

	if (subject_id == GameID() && pkt->Action == 1)
	{
		UpdateVerbs(true);
	}
}

void Player::OpenInterface(long UIChange, long UIType)
{
	SetInterface set_interface;
	set_interface.UIChange = UIChange;
	set_interface.UIType = UIType;

	//LogMessage("Sending SetInterface packet\n");
	SendOpcode(ENB_OPCODE_0066_OPEN_INTERFACE, (unsigned char *) &set_interface, sizeof(set_interface));
}

void Player::CloseInterfaceIfTargetted(long target_id)
{
	if (ShipIndex()->GetTargetGameID() == target_id)
	{
		m_ProspectWindow = false;
		OpenInterface(1,0);
	}
}

void Player::CloseInterfaceIfOpen()
{
	if (m_ProspectWindow)
	{
		m_ProspectWindow = false;
		OpenInterface(1,0);
	}
}

void Player::SendAttackerUpdates(long mob_id, long update)
{
	unsigned char attacker_data[] =
	{
		0x00, 0x00, 0x00, 0x00,
		0x01,
		0x00, 0x00, 0x00, 0x00 //[5]
	};

	*((long *) &attacker_data[0]) = update;
	*((long *) &attacker_data[5]) = mob_id;

	SendOpcode(ENB_OPCODE_008B_ATTACKER_UPDATES, attacker_data, sizeof(attacker_data));
}
void Player::SendChangeBasset(ChangeBaseAsset *NewAsset)
{
	SendOpcode(ENB_OPCODE_0026_CHANGE_BASE_ASSET, (unsigned char*) NewAsset, sizeof(ChangeBaseAsset));
}

void Player::SendObjectLinkedEffectRL(short bitmask, long UID, long effectID, short effectDID, long effect_time)
{
	//send an effect linked to our ship
	ObjectToObjectEffect OBTOBE;

	OBTOBE.Bitmask = bitmask;
	OBTOBE.GameID = GameID();
	OBTOBE.TargetID = UID;
	OBTOBE.EffectDescID = effectDID;
	OBTOBE.Message = 0;
	OBTOBE.EffectID = effectID;
	OBTOBE.Duration = short(effect_time);
	OBTOBE.TimeStamp = GetNet7TickCount();

	SendObjectToObjectEffectRL(&OBTOBE);
}

void Player::CheckObjectRanges()
{
	ObjectManager *om = GetObjectManager();
	if (!m_DebugPlayer && om)
	{
		om->DisplayDynamicObjects(this);
	}

	CheckArrivalTriggers();
}

void Player::SendRemainingStaticObjs()
{
	ObjectManager *om = GetObjectManager();
	if (!m_DebugPlayer && om)
	{
		om->SendRemainingStaticObjs(this);
	}
}

void Player::SendSetTarget(int game_id, int target_id)
{
	SetTarget set_target;
	set_target.GameID = game_id;
	set_target.TargetID = target_id;

	SendOpcode(ENB_OPCODE_0019_SET_TARGET, (unsigned char *) &set_target, sizeof(set_target));
}

void Player::SendRemoveEffect(int target_id)
{
	SendOpcode(ENB_OPCODE_000F_REMOVE_EFFECT, (unsigned char *) &target_id, sizeof(int));
}

void Player::TradeAction(long GameID, int Action)
{
	unsigned char buffer[5];

	*((long *) &buffer[0]) = GameID;
	*((char *) &buffer[4]) = (char) Action;

	SendOpcode(ENB_OPCODE_001F_TRADE, buffer, 5);
}

void Player::SendConfirmation(char * msg, int PlayerID, int Ability, int Confirmation)
{
	unsigned char buffer[2000];
	int index = 0;

	// Save this data so we can call back
	m_Confirmation = Confirmation;
	m_Confirmation_PlayerID = PlayerID;
	m_Confirmation_Ability = Ability;

	*((short *) &buffer[index]) = strlen(msg) + 1; index += 2;
	*((char *) &buffer[index]) = (char) 0x01; index++;
	memcpy(&buffer[index], msg, strlen(msg) + 1); index += (strlen(msg) + 1);

	SendOpcode(ENB_OPCODE_001E_GROUP, buffer, index);
}

//Handle Verb icon clicks (actions)
void Player::HandleAction(unsigned char *data)
{
	ActionPacket * myAction = (ActionPacket *) data;
	char message[128];
	SectorManager *sm = GetSectorManager();
	ObjectManager *om = GetObjectManager();
	Object *obj = (0);
//	if (om) obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
//  someone managed to "mine" another PLAYER with the above code, (why does that ignore the target in the packet anyway?)
	if (om) obj = om->GetObjectFromID(myAction->Target);

	LogDebug("Action - ID: 0x%x, Action: %d, Target: %d, OptVar: %d\n",myAction->GameID, myAction->Action, myAction->Target, myAction->OptionalVar);

	int x;

	switch (myAction->Action)
	{
	case 1:		//tractor
		{
			if (ShipIndex()->GetIsIncapacitated())
			{
				SendMessageString("Unable to tractor while incapacitated.", 11);
				return;
			}
			// target in packet is wrong (self) for floating loot!
			if (obj == this)
				obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
			if (obj)
			{
				//shouldnt targeted ID be myAction->target??
				//you would think that, but for some reason HULK action doesn't send the target ID...
				switch (obj->ObjectType())
				{
				case OT_FLOATING_ORE:
					{
						LootItem(0, false);
					}
					break;
				case OT_HUSK:
					{
						if (m_ProspectWindow == false && CheckResourceLock(ShipIndex()->GetTargetGameID()))
						{
							SendResourceContentsAUX(obj);
							m_ProspectWindow = true;
							OpenInterface(0, 0);
							SendMessageString("\0", 17);
						}
						else
						{
							m_ProspectWindow = false;
							OpenInterface(1,0);
						}
					}
					break;
				default:
					break;
				}
			}
		}
		break;
	case 7:     //docking complete
		{
			if (StargateDestination() > 0 && sm)
			{
				sm->SectorServerHandoff(this, StargateDestination());
			}
		}
		break;
	case 8:		//land
		{
			if (ShipIndex()->GetIsIncapacitated())
			{
				SendMessageString("Unable to land while incapacitated.", 11);
				return;
			}
			if(!CheckForInstalls())
			{
				SendMessageString("Cannot land, unstable items may cause hazardous waste leakage and is punishable by galactic law.",17);
				return;
			}
			if (StargateDestination() > 0 && sm)
			{
				m_Gating=true;
				// Cancel trade if docking
				if (m_TradeID != -1)
					CancelTrade();

				TerminateWarp();
				sm->SectorServerHandoff(this, StargateDestination());
				return;
			}
		}
		break;
	case 9:		//fire all weapons
		{
			// if you are incapacited you cant equip items
			if (ShipIndex()->GetIsIncapacitated())
			{
				SendMessageString("Unable to fire weapons while incapacitated.", 11);
				return;
			}
			FireAllWeapons();
		}
		break;
	case 10:	//invite target to group
		{
			g_ServerMgr->m_PlayerMgr.GroupInvite(GroupID(),GameID(),myAction->Target);
		}
		break;
	case 11:	//accept group invitation
		{
			// See if we have a conformation up insted of group
			if (m_Confirmation == 2)
			{
				Player * p = g_ServerMgr->m_PlayerMgr.GetPlayer(m_Confirmation_PlayerID);

				if (p && p->m_AbilityList[m_Confirmation_Ability])
				{
					// Call back and give a responce
					p->m_AbilityList[m_Confirmation_Ability]->Confirmation(true, m_Confirmation_Ability, GameID());
				}

				// reset data
				m_Confirmation = 0;
			}
			else if (m_Confirmation == 3)		// GM Wormhole
			{
				Player * p = g_ServerMgr->m_PlayerMgr.GetPlayer(m_Confirmation_PlayerID);

				if (p)
				{
					this->WormHole(m_Confirmation_Ability);
				}

				// reset data
				m_Confirmation = 0;
			}
			else
			{
				g_ServerMgr->m_PlayerMgr.AcceptGroupInvite(GroupID(),GameID());
				m_Confirmation = 0;
			}
		}
		break;
	case 12:	//decline group invitation
		{
			// See if we have a conformation up insted of group
			if (m_Confirmation == 2)
			{
				Player * p = g_ServerMgr->m_PlayerMgr.GetPlayer(m_Confirmation_PlayerID);

				if (p && p->m_AbilityList[m_Confirmation_Ability])
				{
					// Call back and give a responce
					p->m_AbilityList[m_Confirmation_Ability]->Confirmation(false, m_Confirmation_Ability, GameID());
				}

				// reset data
				m_Confirmation = 0;
			}
			else if (m_Confirmation == 3)		// GM Wormhole
			{
				// reset data
				m_Confirmation = 0;
			}
			else
			{
				m_Confirmation = 0;
				g_ServerMgr->m_PlayerMgr.RejectGroupInvite(GroupID(), GameID());
			}
		}
		break;
	case 13:	//disban group
		{
			g_ServerMgr->m_PlayerMgr.DisbanGroup(GroupID(), GameID());
		}
		break;
	case 14:	//leave group
		{
			g_ServerMgr->m_PlayerMgr.LeaveGroup(GroupID(), GameID());
		}
		break;
	case 15:	//kick target from group
		{
			g_ServerMgr->m_PlayerMgr.KickFromGroup(GroupID(), GameID(), myAction->Target);
		}
		break;
	case 16:	//request list of players LFG
		{
			g_ServerMgr->m_PlayerMgr.RequestAllPlayersLFG(this);
		}
		break;
	case 17:	//mine
		{
			if (ShipIndex()->GetIsIncapacitated())
			{
				SendMessageString("Unable to mine while incapacitated.", 11);
				return;
			}
			//if window open close it
			if (m_ProspectWindow)
			{
				m_ProspectWindow = false;
				OpenInterface(1,0);
				break;
			}

			if (obj != 0 && (obj->ObjectType() == OT_RESOURCE || obj->ObjectType() == OT_HULK) && 
				CheckResourceLock(myAction->Target))
			{
				m_ProspectWindow = true;
				OpenInterface(0,0);
				SendMessageString("\0", 17);
			}
		}
		break;
	case 18:	//gate button
		{    
			if (ShipIndex()->GetIsIncapacitated())
			{
				SendMessageString("Unable to gate while incapacitated.", 11);
				return;
			}
			if(!CheckForInstalls())
			{
				SendMessageString("Cannot initiate gate transit, unstable items may cause unwanted matter rearangement. Please wait until your equipment has finished being installed before gating.",17);
				return;
			}
			if (m_Gating)
			{
				SendMessageString("Gating in progress.", 11);
				return;
			}
			if (obj != 0 && obj->ObjectType() == OT_STARGATE)
			{
			
				m_Gating = true;
				if (this->GroupID() != -1)
				{
					g_ServerMgr->m_PlayerMgr.BreakFormation(GameID());
					g_ServerMgr->m_PlayerMgr.LeaveFormation(GameID());
				}
				TerminateWarp();
			
				SendClientSound("1512_00_032Se.mp3",0,0);
				if (sm)
				{
					m_Gating = sm->GateActivate(this, myAction->Target);
				}
				else
				{
					m_Gating = false;
				}
			}
		}
		break;
	case 19:	//finish gate sequence
		{
			if (StargateDestination() > 0 && sm)
			{
				sm->SectorServerHandoff(this, StargateDestination());
			}
		}
		break;
	case 20:	//trade
		{
			Player * targetp = g_ServerMgr->m_PlayerMgr.GetPlayer(myAction->Target);
			if (targetp)
			{
				if (targetp->m_TradeID != -1)
				{
					SendVaMessage("That player is already trading with someone!");
				}

				/* dont erase on opening window, (for crash recovery)
				for(x=0;x<6;x++)
				{
					targetp->ShipIndex()->Inventory.TradeInv.Item[x].Empty();
					ShipIndex()->Inventory.TradeInv.Item[x].Empty();
				}*/

				SendAuxShip(targetp);
				targetp->SendAuxShip(this);

				// TESTING
				if (Active() && myAction->Target != GameID() && !InSpace())
				{
					SendCreate(targetp->GameID(), 1, 0x4B06, CREATE_SHIP, 0, 0, 0);
					targetp->SendCreate(GameID(), 1, 0x4B06, CREATE_SHIP, 0, 0, 0);
				}
				// -------

				TradeAction(myAction->Target,0);						// Opens a trade window
				targetp->TradeAction(myAction->GameID, 0);	// Open trade window for other player
				m_TradeID = myAction->Target;							// Set player tradeing with
				m_TradeConfirm = 0;
				targetp->m_TradeID = myAction->GameID;
				targetp->m_TradeConfirm = 0;
			}
		}
		break;
	case 21:	//confirm trade
		{
			Player * targetp;
			targetp = g_ServerMgr->m_PlayerMgr.GetPlayer(m_TradeID);
			if (targetp)
			{
				// check that we have the space to store the other players items
				if (targetp->TradeSpaceUsed() > CargoFreeSpace())
				{
					SendVaMessage("You dont have enough inventory space!");
					targetp->SendVaMessage("Other player doesnt have enough inventory space!");
					break;
				}
				// and that we dont have any uniques already
				if (!CanReceiveTradeItems(targetp))
				{
					SendVaMessage("You already have that unique item!");
					targetp->SendVaMessage("Other player already has that unique item!");
					break;
				}

				m_TradeConfirm = 1;
				TradeAction(0,3);
				targetp->TradeAction(0,5);

				if (targetp->m_TradeConfirm == 1) 
				{
					LogMessage("Trade confirmed for players %x and %x\n",myAction->GameID,myAction->Target);

					//close windows and reset tradeIds
					TradeAction(m_TradeID,2);
					targetp->TradeAction(myAction->GameID, 2);
					m_TradeID = -1;
					targetp->m_TradeID = -1;

					//add and remove credits
					PlayerIndex()->SetCredits(PlayerIndex()->GetCredits() + targetp->ShipIndex()->GetTradeMoney() - ShipIndex()->GetTradeMoney());
					SaveCreditLevel();
					SendAuxPlayer();
					targetp->PlayerIndex()->SetCredits(targetp->PlayerIndex()->GetCredits() + ShipIndex()->GetTradeMoney() - targetp->ShipIndex()->GetTradeMoney());
					targetp->SaveCreditLevel();
					targetp->SendAuxPlayer();

					long targetXP = 0;
					long myXP = 0;

					//Add other player's items
					for(x=0;x<6;x++) 
					{
						//see if there was any tradable on the item's stack
						//trade_stack = Source.TradeStack;
						targetXP += targetp->CalcItemStackTradeXP(targetp->ShipIndex()->Inventory.TradeInv.Item[x].GetData()); //fix trade exploit (use target player's trade level).
						CargoAddItem(targetp->ShipIndex()->Inventory.TradeInv.Item[x].GetData());
					}
					SendAuxShip();

					//add items to other player
					for(x=0;x<6;x++) 
					{
						myXP += CalcItemStackTradeXP(ShipIndex()->Inventory.TradeInv.Item[x].GetData());
						targetp->CargoAddItem(ShipIndex()->Inventory.TradeInv.Item[x].GetData());
					}
					targetp->SendAuxShip();

					//clear trade for both players
					LogMessage("Clearing trade for players %x and %x\n",myAction->GameID,myAction->Target);
					ClearTradeWindowForBoth(targetp);

					// Reset money
					targetp->ShipIndex()->SetTradeMoney(0);
					ShipIndex()->SetTradeMoney(0);

					SendAuxShip(targetp);
					targetp->SendAuxShip(this);

					// allocate XP (2x bonus for trading with players rather than npcs)
					if (targetXP > 0) targetp->AwardTradeXP("Trade:", 2*targetXP);
					if (myXP > 0) AwardTradeXP("Trade:", 2*myXP);

					// TESTING
					if (Active() && myAction->Target != GameID() && !InSpace())		// if we are in a station use this
					{
						RemoveObject(targetp->GameID());
						targetp->RemoveObject(GameID());
					}
					// ----
				}
			}
		}
		break;
	case 22:	//cancel trade
		{
			CancelTrade();
		}
		break;
	case 23:	//keep trading???
		{
			//			SendVaMessage("ACTION 23. Target: %d",myAction->Target);				
		}
		break;
	case 24:	//trade money
		{
			// TODO: We need to log all player trades for the GMs
			Player * targetp;
			targetp = g_ServerMgr->m_PlayerMgr.GetPlayer(m_TradeID);
			if (targetp)
			{
				ShipIndex()->SetTradeMoney(myAction->OptionalVar);
				SendAuxShip(targetp);
				SendAuxShip();
				TradeAction(m_TradeID,4);
				targetp->TradeAction(m_TradeID,4);
				m_TradeConfirm = 0;
				targetp->m_TradeConfirm = 0;
			}
		}
		break;
	case 25:	//register
		{
			if (obj)
			{
				//Starbase Registration
				if (obj->ObjectType() == OT_STATION && m_RegisteredSectorID != obj->Destination())
				{
					SendClientSound("Reg_OK",0,0);
					_snprintf(message, 128, "%s control: Registration Confirmed.", obj->Name());
					SendMessageString(message,5);

					m_RegisteredSectorID = obj->Destination();

					SaveRegisteredStarbase();

					PlayerIndex()->SetRegistrationStarbase(obj->Name());
					PlayerIndex()->SetRegistrationStarbaseSector(PlayerIndex()->GetSectorName());
				}
			}
		}
		break;
	case 26:	// Jump Start
		{
			if (!obj)
			{
				SendVaMessage("Invalid JS target! SUBMIT BUG REPORT!");
				return;
			}

			if (obj->ObjectType() == OT_PLAYER)
			{
				// Make sure we can use the ability
				if (m_AbilityList[JUMPSTART]->CanUse(obj->GameID(), JUMPSTART, SKILL_JUMPSTART))
				{
					// Execute the ability
					m_AbilityList[JUMPSTART]->Use(obj->GameID());
				}
			}
		}
		break;
	case 28:	//dock
		{
			// if you are incapacited you cant equip items
			if (ShipIndex()->GetIsIncapacitated())
			{
				SendMessageString("Unable to dock while incapacitated.", 11);
				return;
			}
			if(!CheckForInstalls())
			{
				SendMessageString("Cannot dock, unstable items may cause hazardous waste leakage and is punishable by galactic law.",17);
				return;
			}
			if (obj)
			{
				if (obj->ObjectType() == OT_STATION && m_Gating == false)
				{
					m_Gating=true;
					if (this->GroupID() != -1)
					{
						g_ServerMgr->m_PlayerMgr.BreakFormation(GameID());
						g_ServerMgr->m_PlayerMgr.LeaveFormation(GameID());
					}
					if (m_TradeID != -1)
					{
						CancelTrade();
					}
					// Register when you dock at a station
					_snprintf(message, 128, "%s control: Registration Confirmed.", obj->Name());
					SendMessageString(message,5);
					m_RegisteredSectorID = obj->Destination();
					SaveRegisteredStarbase();
					PlayerIndex()->SetRegistrationStarbase(obj->Name());
					PlayerIndex()->SetRegistrationStarbaseSector(PlayerIndex()->GetSectorName());

					if (sm) sm->Dock(this, obj->GameID());
				}
			}
		}
		break;
	case 29:	//planet landing button (or our satellite deployment button).
		{	
			if (obj && m_Gating == false && obj->Destination() > 0)
			{
				m_Gating=true;
				g_ServerMgr->m_PlayerMgr.BreakFormation(GameID());
				g_ServerMgr->m_PlayerMgr.LeaveFormation(GameID());
				SendClientSound("1512_00_032Se.mp3",0,0); //NB - this just triggers an effect. When the player arrives, a handoff message is sent
				SetStargateDestination(obj->Destination());
			}
			else if (sm)
			{
				//this could be the player trying to deploy something
				obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
				long UID = sm->GetSectorNextObjID();
				if (obj)
				{
					SendEffectRL(obj->GameID(), 0, 10007, UID, GetNet7TickCount(), 3000);
					//when you scan something you may cause a mission advance
					MissionObjectVerb(obj, DEPLOY_ITEM);
				}
			}
		}
		break;
	case 30:    //scan object in space
		{
			if (obj && sm)
			{
				obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
				long UID = sm->GetSectorNextObjID();
				if (obj)
				{
					SendEffectRL(obj->GameID(), 0, 10007, UID, GetNet7TickCount(), 3000);
					//when you scan something you may cause a mission advance
					MissionObjectVerb(obj, SCAN_OBJECT);
					MissionObjectVerb(obj, DEPLOY_ITEM);
				}
			}
		}
		break;
	default:
		{
			LogMessage("UNRECOGNIZED ACTION! ID: %d, Action: %d, Target: %d, OptVar: %d\n",myAction->GameID, myAction->Action, myAction->Target, myAction->OptionalVar);
			SendVaMessage("UNRECOGNIZED ACTION! SUBMIT BUG REPORT!");
			SendVaMessage("Action ID: %d, Action: %d, Target: %d, OptVar: %d",myAction->GameID, myAction->Action, myAction->Target, myAction->OptionalVar);
			break;
		}
	}
}

// actions across sector boundaries (ie group kick)
void Player::HandleAction2(unsigned char *data)
{
	ActionPacket2 *myAction2 = (ActionPacket2 *)data;
	ActionPacket converted;

	converted.GameID = ntohl(myAction2->GameID);
	converted.Action = ntohl(myAction2->Action);
	converted.Target = g_PlayerMgr->GetGameIDFromName(myAction2->string);
	converted.OptionalVar = ntohl(*(u_long *)(myAction2->string+myAction2->string_len));

	HandleAction((unsigned char *)&converted);
}

void Player::ClearTradeWindowForBoth(Player *targetp)
{
	for(int x=0;x<6;x++) 
	{
		if (targetp->ShipIndex()->Inventory.TradeInv.Item[x].GetItemTemplateID() != -1)
		{
			targetp->ShipIndex()->Inventory.TradeInv.Item[x].Empty();
			targetp->SaveTradeChange(x);
		}
		if (ShipIndex()->Inventory.TradeInv.Item[x].GetItemTemplateID() != -1)
		{
			ShipIndex()->Inventory.TradeInv.Item[x].Empty();
			SaveTradeChange(x);
		}
	}
}

void Player::CancelTrade()
{
	Player * targetp;
	targetp = g_ServerMgr->m_PlayerMgr.GetPlayer(m_TradeID);
	int x;

	if (targetp)
	{
		TradeAction(m_TradeID,1);									// Closes a trade window
		targetp->TradeAction(GameID(), 1);	// Closes trade window for other player
		m_TradeID = -1;												// No longer Tradeing
		targetp->m_TradeID = -1;

		// Reset money
		targetp->ShipIndex()->SetTradeMoney(0);
		ShipIndex()->SetTradeMoney(0);

		//return player's items
		for(x=0;x<6;x++) 
		{
			CargoAddItem(ShipIndex()->Inventory.TradeInv.Item[x].GetData());
		}
		SendAuxShip();

		//return other player's items
		for(x=0;x<6;x++) 
		{
			targetp->CargoAddItem(targetp->ShipIndex()->Inventory.TradeInv.Item[x].GetData());
		}
		targetp->SendAuxShip();

		//clear trade fr both players
		//LogMessage("Clearing trade for players %x and %x\n", GameID(), m_TradeID);
		ClearTradeWindowForBoth(targetp);

		SendAuxShip(targetp);
		targetp->SendAuxShip(this);

		targetp->m_TradeConfirm = 0;
		m_TradeConfirm = 0;

		// TESTING
		if (!Active() && m_TradeID != GameID())
		{
			RemoveObject(targetp->GameID());
			targetp->RemoveObject(GameID());
		}
		// ----
	}
}

void Player::SendResourceLevel(long target_id)
{
	ObjectManager *om = GetObjectManager();
	if (!om) return;
	Object *obj = om->GetObjectFromID(target_id);
	float resource_remains;

	if (obj)
	{
		resource_remains = obj->ResourceRemains();//m_SectorMgr->CalcResourceRemains(obj);
		unsigned char aux_data[] = 
		{
			0x00, 0x00, 0x00, 0x00, 
			0x0C, 0x00,
			0x01,
			0xC6, 0x02,
			0x05,
			0x00, 0x00, 0x00, 0x00,	
			0x00, 0x00,	0x00, 0x00
		};

		*((long *) aux_data) = target_id;
		*((float *) &aux_data[10]) = resource_remains;
		*((long *) &aux_data[14]) = obj->Level();

		SendOpcode(ENB_OPCODE_001B_AUX_DATA, aux_data, sizeof(aux_data));
	}
}

/*
void Player::SendResourceContentsAUXForHulk(Object *obj) 
{
bool contents_already_sent;

if (obj)
{
contents_already_sent = obj->GetIndex(ResourceSendList());

if (!contents_already_sent)
{
unsigned char aux_data[] = //NB this only supports 4 ores in the resource inventory
{
0x00, 0x00, 0x00, 0x00, 
0x3A, 0x01,
0x01,
0xA6, 0x02, 0xF6, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F, 
0x36, 0x00, 0xFF, 
0x00, 0x00, 0x00, 0x00, 
0x00, 0x00, 0x00, 0x00, 
0x36, 0x00, 0xFF, 
0x00, 0x00, 0x00, 0x00, 
0x00, 0x00, 0x00, 0x00, 
0x36, 0x00, 0xFF, 
0x00, 0x00, 0x00, 0x00, 
0x00, 0x00, 0x00, 0x00, 
0x36, 0x00, 0xFF, 
0x00, 0x00, 0x00, 0x00, 
0x00, 0x00, 0x00, 0x00, 
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF,	0xFF, 0xFF, 
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF,
0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 
0x00, 0x00,	0x00, 0x00 //resource tech level
};
short entries = 0;
int index = 0;
ContentSlot *slot;
bool client_already_has_itembase = false;
for (int x = 0; x < MAX_ITEMS_PER_RESOURCE; x++)
{
slot = obj->GetContents(x);
if (slot->stack > 0 && slot->item)
{
index = 23 + x*11;

*((long *) &aux_data[index]) = slot->item->m_ItemTemplateID;
*((long *) &aux_data[index+4]) = slot->stack;
slot->item->SendItemBasePacket(this);
entries++;
}
}

*((long *) aux_data) = obj->GameID();
*((long *) &aux_data[316]) = obj->Level();

SendOpcode(ENB_OPCODE_001B_AUX_DATA, aux_data, sizeof(aux_data));
obj->SetIndex(ResourceSendList());
}
}
}*/

//TODO: make this part of AUX handler.
void Player::SendResourceContentsAUX(Object *obj) 
{
	bool contents_already_sent;

	if (obj)
	{
		contents_already_sent = obj->GetIndex(ResourceSendList());

		if (!contents_already_sent)
		{
			unsigned char spacer[4] =
			{
				0x36, 0x00, 0xFF
			};
			unsigned char filler[8] =
			{                    
				0x16, 0x80, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF //6
			};
			unsigned char prologue[16] =
			{
				0x3A, 0x01,
				0x01,
				0xA6, 0x02, 0xF6, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F //15
			};

			unsigned char packet[340];

			int index = 0;
			ContentSlot *slot;

			AddData(packet, obj->GameID(), index);
			AddBuffer(packet, prologue, 16, index);

			int x;
			for (x = 0; x < MAX_ITEMS_PER_RESOURCE; x++)
			{
				slot = obj->GetContents(x);
				if (slot->stack > 0 && slot->item)
				{
					AddBuffer(packet, spacer, 3, index);
					AddData(packet, slot->item->ItemTemplateID(), index);
					AddData(packet, long(slot->stack), index);
					SendItemBase(slot->item->ItemTemplateID());
				}
				else
				{
					AddBuffer(packet, filler, 7, index);
				}
			}

			for (x = 0; x < 32; x++)
			{
				AddBuffer(packet, filler, 7, index);
			}

			AddData(packet, (long)obj->Level(), index);

			//set length
			*((short *) &packet[4]) = index-10;

			SendOpcode(ENB_OPCODE_001B_AUX_DATA, packet, index);
			obj->SetIndex(ResourceSendList());
		}
	}
}

void Player::SendCameraControl(long Message, long GameID)
{
	CameraControl data;
	data.Message = Message;
	data.GameID = GameID;

	//LogMessage("Sending CameraControl packet\n");
	SendOpcode(ENB_OPCODE_0092_CAMERA_CONTROL, (unsigned char *) &data, sizeof(data));
}

bool Player::MatchOptWithParam (char *option, char *arg, char *&param, bool &msg_sent, bool allowNoParams)
{
	int len = strlen(option);

	if (strncmp(option, arg, len) == 0)
	{
		if (arg[len] == '=' || arg[len] == ' ')
		{
			param = arg + len + 1;
			return true;
		}
		else if (isalpha((unsigned char)arg[len]))
		{
			return false;
		}
		else if (allowNoParams)
		{
			param = NULL;
			return true;
		}
		else
		{
			SendVaMessage("Missing arg for option %s", option);
			msg_sent = true;
		}
	}

	return false;
}

void Player::HandleEquipUse(unsigned char *data)
{
	EquipUse *myUse = (EquipUse *) data;

	m_Equip[myUse->InvSlot].ManualActivate();
}

void Player::SendClientDamage(long target_id, long source_id, float damage, float modifier, long type, long inflicted)
{
	int index = 0;
	unsigned char packet[32];

	AddData(packet, damage, index);
	AddData(packet, modifier, index);
	AddData(packet, type, index);
	AddData(packet, inflicted, index);
	AddData(packet, source_id, index);
	AddData(packet, target_id, index);

	SendOpcode(ENB_OPCODE_0064_CLIENT_DAMAGE, packet, index);
}

//what's this?
void Player::Dialog(char *Stringd, int Type)
{
	int Index = 0;
	unsigned char Data[75];

	*((short*) &Data[Index]) = strlen(Stringd) + 1;		// String Size
	Index+=2;
	*((long*) &Data[Index]) = Type;						// Type
	Index+=4;
	memcpy(&Data[Index], Stringd, strlen(Stringd) + 1);		// copy the string
	Index+=strlen(Stringd)+1;

	SendOpcode(0x62, (unsigned char *) &Data, Index);	
}

void Player::HandleClientChat(unsigned char *data)
{
	ClientChat * chat = (ClientChat *) data;
	char * types[] =
	{
		"To Target", // chat->Type == 0
		"To Group", // chat->Type == 1
		"To Guild", // chat->Type == 2
		"To Local Area", // chat->Type == 3
		"To Entire Sector" // chat->Type == 4
	};

	//LogMessage("Received ClientChat packet -- GameID=%d  Type=%d (%s)\n",
	//    chat->GameID, chat->Type, types[chat->Type]);

	char *p = chat->String;
	p += strlen(p) + 1;
	short length = *((short *) p);
	p += 2;

	if (this && chat->String[0] == '/')
	{
		HandleSlashCommands(chat->String);
	}
	// Group "To Group"
	else if (this && chat->Type == 1)
	{
		if (GroupID() != -1)
			g_ServerMgr->m_PlayerMgr.GroupChat(GroupID(), GameID(), chat->String);
		else
			SendVaMessage("Error: You are not in a group!");
	}
	// Guild "To Guild"
	else if (this && chat->Type == 2)
	{
		g_ServerMgr->m_PlayerMgr.GuildChat(chat->GameID, chat->String);
	}
	// Local "To Local Area"
	else if (this && chat->Type == 3)
	{
		if (Hijackee()) DoVrixEncoding(chat->String);
		g_ServerMgr->m_PlayerMgr.LocalChat(chat->GameID, chat->String);
	}
	// Broadcast "To Entire Sector"
	else if (this && chat->Type == 4)
	{
		if (Hijackee()) DoVrixEncoding(chat->String);
		g_ServerMgr->m_PlayerMgr.BroadcastChat(chat->GameID, chat->String);
	}
}

void Player::SendClientChatList(long listtype, char **names, char **sector, long number1, long number2, char *channel)
{
	int index = 0;
	unsigned char *packet = new unsigned char[14 + number1 * 20 + number2 * 64];

	AddData(packet, listtype, index);	// ListType
	AddDataLS(packet, channel, index);	// channel (for type 2)
	AddData(packet, number1, index);	// List Number
	for(int x=0;x<number1;x++)
	{
		AddDataLS(packet, names[x], index);
	}
	AddData(packet, number2, index);	// List Number
	for(int x=0;x<number2;x++)
	{
		AddDataLS(packet, sector[x], index);
	}
	SendOpcode(ENB_OPCODE_00A4_CLIENT_CHAT_LIST, packet, index);

	delete[] packet;
}

void Player::SendClientChatError(long reason, long type, char *player, char *channel, char *other)
{
	int index = 0;
	unsigned char *packet = new unsigned char[8 + strlen(player) + strlen(channel) + strlen(other) + 6];

	AddData(packet, reason, index);
	AddData(packet, type, index);
	AddDataLS(packet, player, index);
	AddDataLS(packet, channel, index);
	AddDataLS(packet, other, index);
	SendOpcode(ENB_OPCODE_00A6_CLIENT_CHAT_ERROR, packet, index);

	delete[] packet;
}

void Player::HandleSlashCommands(char *Msg)
{
	if(Msg == NULL) return;  //if Msg were somehow magically Null, this function would crash
	bool success = false;
	bool msg_sent = false;
	ObjectManager *om = GetObjectManager();
	SectorManager *sm = GetSectorManager();
	char *next_token;

	// TODO: Handle slash commands here
	// 
	//      - Add slash command '/opcode' to allow user to enter hex opcodes to be echoed
	//          back to the client application for opcode testing.
	//
	//          -- interpret data as hex (-x) (default)
	//          -- interpret data as little endian integer data (-i) (-i2) (-i4)
	//          -- interpret data as big endian integer data (-I2) (-I4)
	//          -- interpret data as floating point data (-f)
	//
	//      - (Navigation)  /n int gameID, float signature, byte visited, int navtype, byte ishuge 
	//      - (ConstPositionalUp) /c int gameID, float x, float y, float z 
	//      - (Create Object)  /o int gameID, float scale, short asset, int type, float x, float y, float x 
	//      - (Remove Object)  /e int gameID 
	//

	//TODO: reformat this to remove C hacker style braces ('} else {') and other unreadable formatting
	//      the rest of the Net7 source code adheres to the new C++ style standard
	//      please change this to use Net7/C++ formatting style ASAP.

	// This is for GM/DEV/ADMIN commands!




	if (Msg[0] == '/' && Msg[1] == '/' && Msg[2] != 0 && AdminLevel() >= GM)
	{
		char *param;
		char *pch = (char*)_alloca(strlen(&Msg[2]) + 1);//copy to stack to avoid heap fragment
		strcpy_s(pch, strlen(&Msg[2]) + 1, &Msg[2]);
		pch[strlen(&Msg[2])] = '\0';
		int retval = 0;

		switch(*pch)
		{
		case 'a':
			{
				if (MatchOptWithParam("adduser", pch, param, msg_sent) && AdminLevel() >= GM)
				{
					char *Username = strtok_s(param, " ", &next_token);
					char *Password = strtok_s(NULL, " ", &next_token);
					char *Access = strtok_s(NULL, " ", &next_token);

					if (!Username || !Password || !Access)
					{
						SendVaMessage("Syntax: //adduser <username> <password> <access>");
						return;
					}

					if (g_AccountMgr->AddUser(Username, Password, Access))
					{
						SendVaMessage("Account %s / %s / %s Created", Username, Password, Access);
					}
					else
					{
						SendVaMessage("Account creation failed!");
					}

					msg_sent = true;
				}
			}
			break;

		case 'b':
			{
				if (MatchOptWithParam("ban", pch, param, msg_sent))
				{
					char *Username = strtok_s(param, " ", &next_token);

					if (!Username)
					{
						SendVaMessage("Syntax: //ban <playername>");
						return;
					}

					Player * target = g_PlayerMgr->GetPlayer(Username);
					if (!target)
					{
						SendVaMessage("Player `%s` not found", Username);
						return;
					}

					long new_access = -2;

					// Can't ban someone above your access
					if (g_AccountMgr->GetAccountStatus(target->AccountUsername()) > AdminLevel())
					{
						SendVaMessage("Can't ban player above your access level");
						msg_sent = true;
					}
					else
					{
						g_AccountMgr->SetAccountStatus(target->AccountUsername(), new_access);
						SendVaMessage("Player %s has access set to %d", Username, new_access);
						msg_sent = HandleKick(param);
						success = true;
					}
				}
				else if (MatchOptWithParam("bumpaccess", pch, param, msg_sent) && AdminLevel() >= DEV) //this allows a dev to recruit helpers
				{
					char *Username = strtok_s(param, " ", &next_token);
					char *Access = strtok_s(NULL, " ", &next_token);

					if (!Username || !Access)
					{
						SendVaMessage("Syntax: //gmsetaccess <playername> <password>");
						return;
					}

					Player * target = g_PlayerMgr->GetPlayer(Username);
					if (!target)
					{
						SendVaMessage("Player `%s` not found", Username);
						return;
					}

					long new_access = atoi(Access);

					// Can't promote over your level
					if (new_access > AdminLevel())
						new_access = AdminLevel();

					// Can't demote someone above your access
					if (g_AccountMgr->GetAccountStatus(target->AccountUsername()) > AdminLevel())
					{
						SendVaMessage("Can't change access for player above your access level");
						msg_sent = true;
					}
					else
					{
						target->SetAdminLevel(new_access);
						SendVaMessage("Player %s has access set to %d", Username, new_access);
						msg_sent = true;
						success = true;
					}
				}
			}
			break;
		case 'c':
			{
				if(MatchOptWithParam("countsp", pch, param, msg_sent))
				{
					char *Username = strtok_s(param," ", &next_token);
					Player * TargetP;

					if(Username)	
					{
						TargetP = g_ServerMgr->m_PlayerMgr.GetPlayer(Username);
						if(TargetP)
						{
							SendVaMessage("%s has spent %d of %d earned points, and has %d unspent points.",
								Username,
								TargetP->CountSpentPoints(),
								TargetP->m_PlayerIndex.RPGInfo.GetTotalSkillPoints(),
								TargetP->m_PlayerIndex.RPGInfo.GetSkillPoints());					

						}
					}
					else
					{
						SendVaMessage("Usage: /countsp <player>");
					}
					msg_sent = true;
					success = true;
				}
			}
			break;
		case 'd':
			if (strcmp(pch,"displayfactions") == 0)
			{
				DisplayClassFactionStanding();
				msg_sent = true;
				success = true;
			}
			else if (MatchOptWithParam("displayplayerfaction", pch, param, msg_sent))
			{
				char *next_token;
				char *p_name= strtok_s(param, " ", &next_token); 
				char *p_faction_id = strtok_s(NULL, " ", &next_token);//we just ignore the rest for now
				
				if (!p_name)
				{
					SendVaMessageC(17, "//displayplayerfaction <playername>");
					return;
				}
				if (!DisplayPlayerFactionStanding(p_name))
				{
					SendVaMessageC(17,"Couldnt list faction for '%s' !", p_name);
					return;
				}
				msg_sent = true;
				success = true;
			}
			else if (strcmp(pch, "destroyobject") == 0)
			{
				success = HandleObjectDestruction();
				msg_sent = true;
			}
			break;

		case 'e':
			if(MatchOptWithParam("editfaction", pch, param, msg_sent))
			{
				success = EditFactionStanding(param);
				msg_sent = true;
			}
			if(MatchOptWithParam("editplayerfaction", pch, param, msg_sent))
			{
				success = EditPlayerFactionStanding(param);
				msg_sent = true;
			}
			break;
			
		case 'f':
			if (strcmp(pch,"floodsave") == 0 && AdminLevel() >= SDEV)
			{
				for (int i=0;i < 900;i++)
					SaveInventoryChange(0);
				SendVaMessage("sent 900 save messages");
				msg_sent = true;
				success = true;
			}
			if (strcmp(pch,"friends") == 0 && AdminLevel() >= SDEV)
			{
				ListFriends();
				msg_sent = true;
				success = true;
			}
			else if(MatchOptWithParam("findsector", pch, param, msg_sent))
			{
				success = FindSectorFromName(param);
				msg_sent = true;
			}
			break;

		case 'h':
			if (MatchOptWithParam("halloween", pch, param, msg_sent))
			{
				char *setting = strtok_s(param," ", &next_token);
				if (setting)
				{
					if (_stricmp(setting, "on") == 0)
					{
						g_ServerMgr->SetHalloween(true);
						SendVaMessageC(12,"Halloween Items Activated!");					
					}
					else
					{
						g_ServerMgr->SetHalloween(false);
						SendVaMessageC(12,"Halloween Items OFF");
					}
				}
				else
				{
					SendVaMessageC(12,"usage: //halloween ON/OFF");
				}
				msg_sent = true;
				success = true;
			}
		case 'k':
			{
				if (strcmp(pch, "killfactions") == 0)
				{
					g_ServerMgr->m_PlayerMgr.ChatSendEveryone(GameID(), "Erasing all saved faction data. Data will be reset at next player login.");
					WipeFactions();
					msg_sent = true;
					success = true;
				}
			}
			break;

		case 'r':
			{
				if (strcmp(pch, "rstations") == 0)
				{
					// Read in stations again
					g_ServerMgr->m_PlayerMgr.ChatSendEveryone(GameID(), "Reloading Stations...");

					//SendVaMessage("Reloading Stations...");
					g_ServerMgr->m_StationMgr.LoadStations();
					g_ServerMgr->m_PlayerMgr.ChatSendEveryone(GameID(), "Station Reload Complete.");

					//SendVaMessage("Station Reload Complete.");
					if (PlayerIndex()->GetSectorNum() > MAX_SECTOR_ID)
					{
						SendVaMessage("You must launch into space and re-dock for changes.");
					}

					msg_sent = true;
					success = true;
				}
				else if (strcmp(pch, "rsectors") == 0)
				{
					long scan_range = ShipIndex()->CurrentStats.GetScanRange();
					ShipIndex()->CurrentStats.SetScanRange(10); //set ship almost blind
					CheckObjectRanges();

					g_ServerMgr->m_PlayerMgr.ChatSendEveryone(GameID(), "Reloading Sector and mobs...");
					SendVaMessage("Reloading %d.",PlayerIndex()->GetSectorNum());
					g_ServerMgr->ReloadSectorObjects(PlayerIndex()->GetSectorNum());
					g_ServerMgr->m_PlayerMgr.ChatSendEveryone(GameID(), "Sector Reload Complete.");
					SendVaMessage("%d Reload Complete.",PlayerIndex()->GetSectorNum());
					if (PlayerIndex()->GetSectorNum() < MAX_SECTOR_ID)
					{
						SendVaMessage("You must change sectors to see changes to navs.");
					}
					SendVaMessage("use /rsectorall to reload all sectors (VERY SLOW, DANGEROUS)");

					ShipIndex()->CurrentStats.SetScanRange(scan_range);
					ResetRangeLists();
					CheckObjectRanges();

					msg_sent = true;
					success = true;
				}
				else if (strcmp(pch, "rsectorall") == 0)
				{
					// Read in sectors again
					long scan_range = ShipIndex()->CurrentStats.GetScanRange();
					ShipIndex()->CurrentStats.SetScanRange(10); //set ship almost blind
					CheckObjectRanges();

					g_ServerMgr->m_PlayerMgr.ChatSendEveryone(GameID(), "Reloading ALL Sectors and mobs...");
					//SendVaMessage("Reloading Sectors and mobs...");
					g_ServerMgr->ReloadAllObjects();
					g_ServerMgr->m_PlayerMgr.ChatSendEveryone(GameID(), "ALL Sectors Reloaded.");
					//SendVaMessage("Sector Reload Complete.");
					if (PlayerIndex()->GetSectorNum() < MAX_SECTOR_ID)
					{
						SendVaMessage("You must change sectors to see changes to navs.");
					}

					ShipIndex()->CurrentStats.SetScanRange(scan_range);
					ResetRangeLists();
					CheckObjectRanges();

					msg_sent = true;
					success = true;
				}
				else if (strcmp(pch, "ritems") == 0)
				{
					g_ServerMgr->m_PlayerMgr.ChatSendEveryone(GameID(), "Reloading Items...");
					//SendVaMessage("Reloading Items...");
					g_ServerMgr->m_ItemBaseMgr.Initialize();
					g_ServerMgr->m_PlayerMgr.ChatSendEveryone(GameID(), "Items Reloaded.");
					//SendVaMessage("Items Reloaded.");
					msg_sent = true;
					success = true;
				}
				else if (strcmp(pch, "rmissions") == 0)
				{
					g_ServerMgr->m_PlayerMgr.ChatSendEveryone(GameID(), "Reloading Missions...");
					g_ServerMgr->m_Missions.LoadMissionContent();
					SendVaMessage("Missions Reloaded.");
					msg_sent = true;
					success = true;
				}
				else if (strcmp(pch, "resetmymissions") == 0)
				{
					ResetAllMissions();
					SendVaMessage("Missions Reset.");
					msg_sent = true;
					success = true;
				}
				else if (MatchOptWithParam("restartcomms", pch, param, msg_sent))
				{
					success = HandleRestartSectorComms(param);
					msg_sent = true;
				}
				else if (strcmp(pch, "rfactions") == 0)
				{
					g_ServerMgr->m_PlayerMgr.ChatSendEveryone(GameID(), "Reloading Factions...");
					g_ServerMgr->m_FactionData.LoadFactions();
					SendVaMessage("Factions Reloaded. You are advised to issue //killfactions now, so player's faction standings are reset on next login");
					msg_sent = true;
					success = true;
				}
				else if (MatchOptWithParam("replaceship", pch, param, msg_sent))
				{
					char *asset = strtok_s(param," ", &next_token);
					if (asset)
					{
						char *scale = strtok_s(NULL," ", &next_token);
						m_ReplacementShipAsset = atoi(asset);
						if (scale)
							m_ReplacementShipScale = (float)atof(scale);
						else
							m_ReplacementShipScale = 0.0f;
						RemoveObject(GameID());
						// send new asset to yourself and everyone around
						SendShipData(this);
						SendAuxShipExtended();
						BlankRangeList();
						UpdatePlayerVisibilityList();
					}
					SendVaMessage("usage: //replaceship <asset> <scale>  (asset=0 to reset)");
					msg_sent = true;
					success = true;
				}
				else if (MatchOptWithParam("respec", pch, param, msg_sent))
				{
					char *Username = strtok_s(param," ", &next_token);
					char *Type = strtok_s(NULL," ", &next_token);
					int SkillID = 0;
					int refund = -1;
					Player * TargetP;

					if(Username && Type)	
					{
						TargetP = g_ServerMgr->m_PlayerMgr.GetPlayer(Username);
						if(!TargetP)
						{
							SendVaMessage("Player %s is not online.", Username);
							return;
						}

						if( tolower(Type[0]) == 'a')
						{
							SendVaMessage("All Forward!");
							refund = TargetP->RespecSkills(false);
						}
						else if(tolower(Type[0]) == 'c')
						{
							SendVaMessage("Call Forward!");
							refund = TargetP->RespecSkills(true);
						}
						else if(isdigit((unsigned char)Type[0]))
						{
							SkillID = atoi(Type);
							if(SkillID < 0 || SkillID >= 64)
							{
								SendVaMessage("Syntax: //respec <username> <all|call|0-63>");
								return;
							}
							else
							{
								SendVaMessage("Respec skill: %d",SkillID);
								refund = TargetP->RespecOneSkill(SkillID);
							}
						}
						else
						{
							SendVaMessage("Syntax: //respec <username> <all|call|0-63>");
							return;
						}

						if(refund >= 0)
						{						
							TargetP->SendVaMessage("Your skill points have been reset and you have regained %d skill points.",refund);
							SendVaMessage("Player %s has had his skill points reset and was refunded %d skill points.",Username,refund);
							return;
						}
					}
					else
					{
						SendVaMessage("Syntax: //respec <username> <all|call|0-63>");
						return;
					}
				}
			}
			break;

		case 's':
			{
				if (MatchOptWithParam("setpassword", pch, param, msg_sent))
				{
					char *Username = strtok_s(param, " ", &next_token);
					char *Password = strtok_s(NULL, " ", &next_token);

					if (!Username || !Password)
					{
						SendVaMessage("Syntax: //setpassword <username> <password>");
						return;
					}

					char msg[1000];
					sprintf(msg, "%s has used setpassword on %s", Name(), Username);
					g_PlayerMgr->ChatSendChannel(GameID(), "GM", msg);
					g_PlayerMgr->ChatSendChannel(GameID(), "Dev", msg);
					g_PlayerMgr->ChatSendChannel(GameID(), "Beta", msg);

					g_AccountMgr->ChangePassword(Username, Password);
					SendVaMessage("Account %s password has been set to %s", Username, Password);
					msg_sent = true;
				}
			}
			break;

		case 'w':
				if (MatchOptWithParam("gmwarn", pch, param, msg_sent) && AdminLevel() >= GM)
				{
					char *Username = strtok_s(param, " ", &next_token);
					char *Warn_Num = strtok_s(NULL, " ", &next_token);
					char *WMsg = strtok_s(NULL, "", &next_token);

					if (!Username || !Warn_Num || !WMsg)
					{
						SendVaMessage("Syntax: //gmwarn <playername> <inc_amount> <message>");
						return;
					}

					Player * target = g_PlayerMgr->GetPlayer(Username);
					if (!target)
					{
						SendVaMessage("Player `%s` not found", Username);
						return;
					}

					long warn_inc = atoi(Warn_Num);

					SaveWarnLvl(target->CharacterID(), warn_inc, WMsg);
				}


		case 'g':
			{
				if (MatchOptWithParam("gmgetaccess", pch, param, msg_sent))
				{
					Player * target = g_PlayerMgr->GetPlayer(param);
					if (!target)
					{
						SendVaMessage("Player `%s` not found", param);
						return;
					}

					SendVaMessage("Access level for `%s` is %d", param, target->AdminLevel());
					msg_sent = true;
					success = true;	
				}

				if (MatchOptWithParam("gmsetaccess", pch, param, msg_sent) && AdminLevel() >= SDEV)
				{
					char *Username = strtok_s(param, " ", &next_token);
					char *Access = strtok_s(NULL, " ", &next_token);

					if (!Username || !Access)
					{
						SendVaMessage("Syntax: //gmsetaccess <playername> <password>");
						return;
					}

					Player * target = g_PlayerMgr->GetPlayer(Username);
					if (!target)
					{
						SendVaMessage("Player `%s` not found", Username);
						return;
					}

					long new_access = atoi(Access);

					// Can't promote over your level
					if (new_access > AdminLevel())
						new_access = AdminLevel();

					// Can't demote someone above your access
					if (g_AccountMgr->GetAccountStatus(target->AccountUsername()) > AdminLevel())
					{
						SendVaMessage("Can't change access for player above your access level");
						msg_sent = true;
					}
					else
					{
						g_AccountMgr->SetAccountStatus(target->AccountUsername(), new_access);
						SendVaMessage("Player %s has access set to %d", Username, new_access);
						msg_sent = true;
						success = true;
					}
				}

				if (MatchOptWithParam("gmskillpoints", pch, param, msg_sent))
				{
					char *Username = 0, *SSkillPoints = 0;
					int SkillPoints;

					Username = strtok_s(param, " ", &next_token);
					if (Username) {
						SSkillPoints = strtok_s(NULL, " ", &next_token);
						if (SSkillPoints)
							SkillPoints = atoi(SSkillPoints);
					}

					if (SSkillPoints)
					{
						Player * TargetP = g_ServerMgr->m_PlayerMgr.GetPlayer(Username);

						if (!TargetP) 
						{
							SendVaMessage("Player %s is not online", Username);
							msg_sent = true;
							success = true;
						} 
						else 
						{
							TargetP->PlayerIndex()->RPGInfo.SetSkillPoints(SkillPoints);
							TargetP->LevelUpForSkills();
							TargetP->UpdateSkills();
							TargetP->SendAuxPlayer();
							TargetP->SendVaMessage("You have gotten %d SkillPoints", SkillPoints);
							SendVaMessage("You have gave %s %d SkillPoints", Username, SkillPoints);
							success = true;
							msg_sent = true;
						}
					} 
					else 
					{
						SendVaMessage("Syntax: //gmskillpoints <playername> <skillpoints>");
						success = false;
						msg_sent = true;
					}
				}

				if (MatchOptWithParam("gmenableskills", pch, param, msg_sent))
				{
					char *PlayerName = strtok_s(param, " ", &next_token);

					if (!PlayerName) 
					{
						SendVaMessage("Syntax: //gmenableskills <playername>");
						msg_sent = true;
						success = false;
					} 
					else 
					{
						Player * TargetP = g_ServerMgr->m_PlayerMgr.GetPlayer(PlayerName);

						if (!TargetP) 
						{
							SendVaMessage("Player %s is not online", PlayerName);
							msg_sent = true;
							success = true;
						} 
						else 
						{
							u32 Availability[4] = {4,0,0,1};
							for (int i=0;i<64;i++)
							{
								if (TargetP->PlayerIndex()->RPGInfo.Skills.Skill[i].GetAvailability()[0] == 3)
								{
									TargetP->PlayerIndex()->RPGInfo.Skills.Skill[i].SetAvailability(Availability);
								}
								TargetP->SendAuxPlayer();
							}
							msg_sent = true;
							success = true;
							TargetP->SendVaMessage("Your skills are now enabled");
							SendVaMessage("Player %s had skills enabled", PlayerName);
						}
					}
				}

				if (MatchOptWithParam("gmplayerlevel", pch, param, msg_sent))
				{
					char *Username = 0, *SLevel = 0;
					int Level = 0;

					Username = strtok_s(param, " ", &next_token);
					if (Username) 
					{
						SLevel = strtok_s(NULL, " ", &next_token);
						if (SLevel)
							Level = atoi(SLevel);
					}

					if (SLevel && Level <= GM)
					{
						Player * TargetP = g_ServerMgr->m_PlayerMgr.GetPlayer(Username);

						if (!TargetP) 
						{
							SendVaMessage("Player %s is not online", Username);
							msg_sent = true;
							success = true;
						} 
						else if (TargetP->AdminLevel() <= BETA)
						{
							SendVaMessage("Unable to use gmplayerlevel on BETA and below at the moment - reason: more low level testing needed.");
							msg_sent = true;
							success = true;
						}
						else 
						{
							TargetP->PlayerIndex()->RPGInfo.SetCombatLevel(Level);
							TargetP->PlayerIndex()->RPGInfo.SetTradeLevel(Level);
							TargetP->PlayerIndex()->RPGInfo.SetExploreLevel(Level);
							TargetP->LevelUpForSkills();
							TargetP->UpdateSkills();
							TargetP->SendAuxPlayer();

							TargetP->SendVaMessage("Combat, Explore and Trade LVLs set to %d",Level);
							SendVaMessage("Player %s leveled to %d", Username, Level);
							msg_sent = true;
							success = true;
						}
					} 
					else 
					{
						SendVaMessage("Syntax: //gmplayerlevel <playername> <level 1-50>");
						msg_sent = true;
						success = false;
					}
				}
				if(MatchOptWithParam("gmupgrade", pch, param, msg_sent))
				{
					if (AdminLevel() >= GM) //Dev +
					{
						char *Username = strtok_s(param, " ", &next_token);
						msg_sent = true;
						success = true;
						if(Username)
						{
							Player * TargetP = g_ServerMgr->m_PlayerMgr.GetPlayer(Username);
							
							if(TargetP)
							{
								long upgradelvl = TargetP->PlayerIndex()->RPGInfo.GetHullUpgradeLevel();
								TargetP->ShipUpgrade(upgradelvl+1);
								long newupgradelvl = TargetP->PlayerIndex()->RPGInfo.GetHullUpgradeLevel();
								if(upgradelvl != newupgradelvl)
								{
									SendVaMessage("%s hull upgraded to level %d.",Username,newupgradelvl);
									TargetP->SendVaMessage("Your hull has been upgraded.");
								}
								else
								{
									SendVaMessage("%s not eligible for an upgrade.",Username);
								}
							}
							else
							{
								SendVaMessage("Player %s not online.",Username);
							}
						}
						else
						{
							SendVaMessage("Syntax: //gmupgrade <playername>");
						}
					}
				}
			}
		}
	}

	// This is for normal commands
	if ((Msg[0] == '/') &&
		(Msg[1] != 0) && (!msg_sent || !success))
	{
		char *param;
		char *pch = (char*)_alloca(strlen(&Msg[1]) + 1);//copy to stack to avoid heap fragment
		strcpy_s(pch, strlen(&Msg[1]) + 1, &Msg[1]);
		pch[strlen(&Msg[1])] = '\0';
		int retval = 0;
		switch(*pch)
		{
		case 'a':
			{
				if (strcmp(pch, "anon") == 0)
				{
					if (AdminLevel() >= GM)
					{
						SendVaMessage("Not yet implemented.\n");
						msg_sent = true;
						success = true;
					}
				}
				if (strcmp(pch, "authlevel") == 0)
				{
					SendVaMessage("Authentication Level - Num: %d", AdminLevel());
					msg_sent = true;
					success = true;
				}
				if (AdminLevel() >= DEV && MatchOptWithParam("altweapon", pch, param, msg_sent)) //no error checking - server devs only
				{
					char *cmd = strtok_s(param, " ", &next_token);
					int weapon_id, bone_id;
					if (cmd)
					{
						weapon_id = atoi(cmd);
						cmd = strtok_s(NULL, " ", &next_token);
					}

					if (cmd)
					{
						bone_id = atoi(cmd);
					}

					//ChangeMountBoneName(weapon_id, bone_id);
					msg_sent = true;
					success = true;
				}
				if (AdminLevel() >= DEV && MatchOptWithParam("altname", pch, param, msg_sent)) //no error checking - server devs only
				{
					char *cmd = strtok_s(param, " ", &next_token);
					int weapon_id = 0;
					if (cmd)
					{
						weapon_id = atoi(cmd);
						cmd = strtok_s(NULL, " ", &next_token);
					}

					if (cmd)
					{
						ChangeMountBoneName(weapon_id, cmd);
						SendVaMessage("Change weapon id #%d to %s", weapon_id, cmd);
					}


					msg_sent = true;
					success = true;
				}
				if (AdminLevel() >= DEV && MatchOptWithParam("addbaseore", pch, param, msg_sent))
				{
					success = HandleChangeFieldRequest(param, 7);
					msg_sent = true;
				}
			}
			break;

		case 'b':
			{
				if (MatchOptWithParam("be", pch, param, msg_sent) && AdminLevel() >= BETA)
				{
					//char *message =&param[1];
					if (param)
					{
						g_PlayerMgr->ChatSendChannel(GameID(), "Beta", param);
					}
					msg_sent = true;
				} 
				else if (strcmp(pch, "beon") == 0)
				{
					if (AdminLevel() >= BETA) // low right now for zapgun's loot builders
					{
						int channel_id = g_PlayerMgr->GetChannelFromName("Beta");
						m_ChannelSubscription[channel_id] = true;
						SendVaMessage("Beta channel on.");
						msg_sent = true;
						success = true;
					} 
					else
					{
						msg_sent = true;
						success = false;
					}
				}
				else if (strcmp(pch, "beoff") == 0)
				{
					if (AdminLevel() >= BETA)
					{
						int channel_id = g_PlayerMgr->GetChannelFromName("Beta");
						m_ChannelSubscription[channel_id] = false;
						SendVaMessage("Beta channel off.");
						msg_sent = true;
						success = true;
					}
					else
					{
						msg_sent = true;
						success = false;
					}
				}
				if (MatchOptWithParam("bwho", pch, param, msg_sent, true) && AdminLevel() >= BETA)
				{
					if (!param)
					{
						g_ServerMgr->m_PlayerMgr.ListPlayersAndLocations(this, BETA, BETA_PLUS);
					}
					else
					{
						char *searchString = strtok_s(param, " ", &next_token);
						if (searchString)
						{
							g_ServerMgr->m_PlayerMgr.ListPlayersWithSearch(this, searchString, BETA, BETA_PLUS);
						}
						else
						{
							g_ServerMgr->m_PlayerMgr.ListPlayersAndLocations(this, BETA, BETA_PLUS);
						}
					}

					msg_sent = true;
					success = true;
				}
				else if (MatchOptWithParam("basset", pch, param, msg_sent))
				{
					success = HandleBassetRequest(param);
					msg_sent = true;
				}
				//BUFF for future reference
				if (MatchOptWithParam("buff", pch, param, msg_sent))
				{
					Buff TestBuff;
					memset(&TestBuff, 0, sizeof(Buff));

					strncpy_s(TestBuff.BuffType, sizeof(TestBuff.BuffType), param,128);
					TestBuff.BuffType[127] = '\0'; //force terminate string
					TestBuff.ExpireTime = GetNet7TickCount() + 10000;
					TestBuff.IsPermanent = false;
					for(int i = 0; i < 5; i++)
					{
						TestBuff.EffectID[i] = -1;
					}
					TestBuff.EffectID[0] = 214;
					// Set stats for buff
					strcpy_s(TestBuff.Stats[0].StatName, sizeof(TestBuff.Stats[0].StatName), STAT_SIGNATURE);
					TestBuff.Stats[0].StatName[sizeof(TestBuff.Stats[0].StatName)-1] = '\0';
					TestBuff.Stats[0].Value = 300;
					TestBuff.Stats[0].StatType = STAT_BUFF_VALUE;

					strcpy_s(TestBuff.Stats[1].StatName, sizeof(TestBuff.Stats[1].StatName), STAT_SCAN_RANGE);
					TestBuff.Stats[1].StatName[sizeof(TestBuff.Stats[1].StatName)-1] = '\0';
					TestBuff.Stats[1].Value = 30000;
					TestBuff.Stats[1].StatType = STAT_BUFF_VALUE;

					strcpy_s(TestBuff.Elements.Element[0].SourceEntity, 
						sizeof(TestBuff.Elements.Element[0].SourceEntity), "Test");
					TestBuff.Elements.Element[0].SourceEntity[sizeof(TestBuff.Elements.Element[0].SourceEntity)-1] = '\0';
					strcpy_s(TestBuff.Elements.Element[1].SourceEntity,
						sizeof(TestBuff.Elements.Element[1].SourceEntity), "Test");
					TestBuff.Elements.Element[1].SourceEntity[sizeof(TestBuff.Elements.Element[1].SourceEntity)-1] = '\0';
					strcpy_s(TestBuff.Elements.Element[2].SourceEntity,
						sizeof(TestBuff.Elements.Element[2].SourceEntity), "Test");
					TestBuff.Elements.Element[1].SourceEntity[sizeof(TestBuff.Elements.Element[1].SourceEntity)-1] = '\0';
					TestBuff.Elements.Element[0].IsActive = true;
					TestBuff.Elements.Element[0].ExpirationTime = TestBuff.ExpireTime;
					TestBuff.Elements.Element[1].IsActive = true;
					TestBuff.Elements.Element[1].ExpirationTime = TestBuff.ExpireTime + 1000;
					TestBuff.Elements.Element[2].IsActive = true;
					TestBuff.Elements.Element[2].ExpirationTime = TestBuff.ExpireTime + 2000;

					m_Buffs.AddBuff(&TestBuff);
					SendVaMessage("Sending buff 0!\n");
					msg_sent = true;
				}
				else if (AdminLevel() == SDEV && MatchOptWithParam("baseitemlist", pch, param, msg_sent)) //one use only, remove once table created
				{
					success = HandleBaseItemListCreate();
					msg_sent = true;
				}
			}
			break;
		case 'c':
			{
				if (MatchOptWithParam("chjoin", pch, param, msg_sent))
				{
					success = false;
					char *channel = strtok_s(param, " ", &next_token);
					if (channel && AdminLevel() >= GM)
					{
						//subscribe to channel
						int channel_id = g_PlayerMgr->GetChannelFromName(channel);
						if (channel_id == INVALID_CHANNEL ||
							(_stricmp(channel, "Errors") == 0 && AdminLevel() < BETA_PLUS) ||
							(_stricmp(channel, "Staff") == 0 && AdminLevel() < BETA_PLUS) ||
							(_stricmp(channel, "GM") == 0 && AdminLevel() < GM) ||
							(_stricmp(channel, "Dev") == 0 && AdminLevel() < DEV))
						{
							m_ChannelSubscription[channel_id] = false;
							if (AdminLevel() < GM)
								SendVaMessage("Unable to join channel %s", channel);
							else
								SendVaMessage("Unable to join channel %s [%d]", channel, channel_id);
						}
						else
						{
							if (AdminLevel() < GM)
								SendVaMessage("Joining channel %s", channel);
							else
								SendVaMessage("Joining channel %s [%d]", channel, channel_id);

							m_ChannelSubscription[channel_id] = true;
						}

						success = true;
					}
					msg_sent = true;
				}
				if (MatchOptWithParam("chleave", pch, param, msg_sent))
				{
					success = false;
					char *channel = strtok_s(param, " ", &next_token);
					if (channel && AdminLevel() >= GM)
					{
						//unsubscribe to channel
						int channel_id = g_PlayerMgr->GetChannelFromName(channel);
						m_ChannelSubscription[channel_id] = false;
						if (AdminLevel() < GM)
							SendVaMessage("Leaving channel %s", channel);
						else
							SendVaMessage("Leaving channel %s [%d]", channel, channel_id);
						success = true;
					}
					msg_sent = true;
				}
				else if (MatchOptWithParam("ccamera", pch, param, msg_sent))
				{
					Object *obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
					if (obj)
					{
						SendVaMessage("Camera control: %x, %x", ntohl(atoi(param)), ntohl(obj->GameID()) );
						if (atoi(param) != 4) //this would cause a crash
						{
							SendCameraControl( ntohl(atoi(param)), ntohl(obj->GameID()) ) ;
						}
						success = true;
						msg_sent = true;
					}
				}
				else if (MatchOptWithParam("changepassword", pch, param, msg_sent))
				{
					g_ServerMgr->m_AccountMgr->ChangePassword(AccountUsername(), param);
					SendVaMessage("Your password has been changed to: `%s`", param);
					msg_sent = true;
				}
				else if (MatchOptWithParam("createitem", pch, param, msg_sent))
				{
					if (atoi(param) == 6137 && strcmp(AccountUsername(), "Tienbau")) //temp lockout for testing
					{
						SendVaMessage("Unable to create.");
					}
#ifdef DEV_SERVER
					else if (AdminLevel() >= GM)      // GM to Admin (Beta testers tend to spam this with random numbers)
#else
					else if (AdminLevel() >= GM)      // GM to Admin (Beta testers tend to spam this with random numbers)
#endif
					{  
						int FreeSlot = GetCargoSlotFromItemID(0, -1);;

						if (FreeSlot == -1) 
						{
							SendVaMessage("No free slots in inventory free up space");
						} 
						else 
						{
							_Item myItem = g_ItemBaseMgr->EmptyItem;

							char *ItemIDs = strtok_s(param, " ", &next_token);
							char *NumberS;
							char *QualityS;
							int Number;
							float Quality = 1.0f;

							if (ItemIDs) 
							{
								//first check this item ID exists, otherwise this can crash the server or the player
								if (!g_ItemBaseMgr->GetItem(atoi(ItemIDs)))
								{
									SendVaMessage("Invalid item id");
									msg_sent = true;
									return;
								}

								NumberS = strtok_s(NULL, " ", &next_token);
								if (NumberS)
								{
									Number = atoi(NumberS);

									QualityS = strtok_s(NULL, " ", &next_token);
									if (QualityS)
									{
										Quality = (float)atof(QualityS);
									}
								}
								else
								{
									Number = 1;
								}

								if (Quality > 2.0f) Quality = 2.0f;
								//if (AdminLevel() != 90) Quality = 1.0f; //quality 2.0 seems to cause the quality calculator to output garbage

									myItem.ItemTemplateID = atoi(ItemIDs);
									myItem.StackCount = Number;
									myItem.TradeStack = Number;
									if(this->m_CurrentNPC)
									{
										myItem.Price = Negotiate(GetVenderBuyPrice(myItem.ItemTemplateID),false,true);
									}
									else
									{
										myItem.Price = 0;
									}
									myItem.Quality = Quality;
									myItem.Structure = 1.0f;
									_snprintf(myItem.BuilderName, sizeof(myItem.BuilderName), "%s GM/DEV", Name());

								if (CargoAddItem(&myItem) != -2)
								{
									SendAuxShip();
									SendVaMessage("Item %d Created %d", atoi(ItemIDs), Number);
								}
								else
								{
									SendVaMessage("No item exists with id %d", myItem.ItemTemplateID);
								}

							} 
							else 
							{
								Number = 1;
								SendVaMessage("/createitem used incorrectly.");
							}
						}
						msg_sent = true;
					} 
					else 
					{
						SendVaMessage("GM Only command.");
					}
				} 
				else if (MatchOptWithParam("createcredits", pch, param, msg_sent))
				{
#ifdef DEV_SERVER
					if (AdminLevel() >= GM)      // Beta to Admin
#else
					if (AdminLevel() >= GM)      // Beta to Admin
#endif
					{
						AwardCredits(_atoi64(param), 0);
						//PlayerIndex()->SetCredits(PlayerIndex()->GetCredits() + _atoi64(param));
						//SaveCreditLevel();
						//SendAuxPlayer();
					} 
					else 
					{
						SendVaMessage("GM Only command.");
					}
					msg_sent = true;
				} 
				
				else if (MatchOptWithParam("createmission", pch, param, msg_sent))
				{
					if (AdminLevel() >= GM)      // GM to Admin
					{
						AssignMission(atoi(param));
						msg_sent = true;
					} 
					else 
					{
						SendVaMessage("GM Only command.");
					}
				} 

				else if (MatchOptWithParam("createmob", pch, param, msg_sent))
				{
					if (AdminLevel() >= GM)      // GM -> admin
					{  
						success = HandleMobCreateRequest(param);
						msg_sent = true;
					}
					else
					{
						SendVaMessage("GM Only command.");
					}
				}
				else if (MatchOptWithParam("create", pch, param, msg_sent))
				{
					success = HandleObjCreateRequest(param);
					msg_sent = true;
				}
				else if (0 == strcmp("checklock", pch))
				{
					Object *obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
					if (obj)
					{
						Player *player = obj->CheckResourceLock();
						if (player)
						{
							SendVaMessage("Currently being mined/looted by %s", player);
						}
						else
						{
							SendVaMessage("Not being looted.");
						}
					}
					msg_sent = true;
				}
				else if (strcmp(pch, "commit") == 0 && AdminLevel() >= 80)
				{
					success = HandleCommitRequest(ShipIndex()->GetTargetGameID());
					msg_sent = true;
				}

				else if(MatchOptWithParam("customizeship",pch,param,msg_sent))
				{
					SendVaMessage("Please visit the appropriate starbase terminal to customize.");
					msg_sent=true;
					success=true;
					/*if(PlayerIndex()->GetSectorNum() > 10000)
					{
						if(RecustomizeShip(param))
						{
							SendVaMessage("Ship recustomized.");
							msg_sent=true;
							success=true;
						}
						else
						{
							SendVaMessage("Usage: /customizeship color <hull|wing|prof|engine|all> <primary|secondary> <gold|silver|bronze|<flat|glossy> #hexcolor\n     style <hull|wing> <1|2|3>\n     name \"[newname]\" | [#hexcolor]\n(Parameters inside <> denote mandatory data, and [] denotes optional.");
							msg_sent=true;
							success=true;
						}
					}
					else
					{
						SendVaMessage("Ship recustomization can only be done at a starbase.");
						msg_sent=true;
						success=true;
					}*/
				}
			}
			break;
		case 'd' :
			if (MatchOptWithParam("d", pch, param, msg_sent) && AdminLevel() >= DEV)
			{
				//char *message =&param[1];
				if (param)
				{
					g_PlayerMgr->ChatSendChannel(GameID(), "Dev", param);
				}
				msg_sent = true;
			} 
			else if (strcmp(pch, "don") == 0)
			{
				if (AdminLevel() >= DEV) // low right now for zapgun's loot builders
				{
					int channel_id = g_PlayerMgr->GetChannelFromName("Dev");
					m_ChannelSubscription[channel_id] = true;
					SendVaMessage("Dev channel on [ch.%d].", channel_id);
					msg_sent = true;
					success = true;
				} 
				else
				{
					msg_sent = true;
					success = false;
				}
			}
			else if (strcmp(pch, "doff") == 0)
			{
				int channel_id = g_PlayerMgr->GetChannelFromName("Dev");
				if (AdminLevel() >= DEV)
				{
					m_ChannelSubscription[channel_id] = false;
					SendVaMessage("Dev channel off.");
					msg_sent = true;
					success = true;
				}
				else
				{
					msg_sent = true;
					success = false;
				}
			}
			if (MatchOptWithParam("dwho", pch, param, msg_sent, true))
			{
				if (!param)
				{
					g_ServerMgr->m_PlayerMgr.ListPlayersAndLocations(this, DEV, ADMIN);
				}
				else
				{
					char *searchString = strtok_s(param, " ", &next_token);
					if (searchString)
					{
						g_ServerMgr->m_PlayerMgr.ListPlayersWithSearch(this, searchString, DEV, ADMIN);
					}
					else
					{
						g_ServerMgr->m_PlayerMgr.ListPlayersAndLocations(this, DEV, ADMIN);
					}
				}

				msg_sent = true;
				success = true;
			}
			else if (MatchOptWithParam("dialog", pch, param, msg_sent) && AdminLevel() >= DEV)
			{
				char *a = strtok_s(param, ",", &next_token);
				char *b = strtok_s(NULL, ",", &next_token);
				if (b) 
				{
					Dialog(a,atoi(b));					
				}
				msg_sent = true;
			} 
			else if (strcmp(pch, "debug") == 0 
				&& AdminLevel() >= DEV)
			{
				g_Debug = !g_Debug;
				LogMessage("Debug mode is %s\n", g_Debug ? "ON" : "OFF");
				msg_sent = true;
				success = true;
			}
			else if (MatchOptWithParam("deco", pch, param, msg_sent))
			{
				if (param)
				{
					short basset = atoi(param);
					char msg_buffer[128];
					Object *obj = om->AddNewObject(OT_DECO);
					obj->SetPosition(Position());
					obj->SetBasset(basset);
					obj->SetSignature(30000.0f);
					obj->SetOrientation(0,0,0,0);
					obj->SetScale(1.0f);
					m_CurrentDecoObj = obj;
					AssetData *asset = g_ServerMgr->AssetList()->GetAssetData(basset);

					if (asset && asset->m_Name)
					{
						_snprintf(msg_buffer, sizeof(msg_buffer), "%d:%s", basset, asset->m_Name);
						SendPushMessage(msg_buffer, "MessageLine", 3000, 3);
					}

					LogMessage("Deco created at:\n %.2f, %.2f %.2f Basset %d\n", obj->PosX(), obj->PosY(), obj->PosZ(), basset);
				}
				else
				{
					LogMessage("/deco <basset>");
				}
				msg_sent = true;
				success = true;
			}
			else if (strcmp(pch, "dockp") == 0)
			{
				success = true;
				msg_sent = true;
				DebugPlayerDock(true);
				//g_ServerMgr->m_PlayerMgr.SendPlayerWithoutConnection(GameID());
			}
			else if (MatchOptWithParam("debugmissions", pch, param, msg_sent))
			{
				char *setting = strtok_s(param," ", &next_token);
				if (setting)
				{
					if (_stricmp(setting, "on") == 0)
					{
						m_DebugMissions = true;
						SendVaMessageC(12,"Mission debugging activated.");					
					}
					else
					{
						m_DebugMissions = true;
						SendVaMessageC(12,"Mission debugging off.");						
					}
				}
				else
				{
					SendVaMessageC(12,"usage: //debugmissions ON/OFF");
				}
				msg_sent = true;
				success = true;
			}
			break;
		case 'e' :
			{	
				if (strcmp(pch, "endtalk") == 0)
				{
					SendTalkTreeAction(-32); // kept locking myself into an empty talk tree :)
					m_MissionDebriefed = false;
					success = true;
					msg_sent = true;
				}
				if (strcmp(pch, "enableskills") == 0)
				{
#ifdef DEV_SERVER
					if (AdminLevel() >= GM)
#else
					if (AdminLevel() >= GM)
#endif
					{
						u32 Availability[4] = {4,0,0,1};
						for (int i=0;i<64;i++)
						{
							if (PlayerIndex()->RPGInfo.Skills.Skill[i].GetAvailability()[0] == 3)
							{
								PlayerIndex()->RPGInfo.Skills.Skill[i].SetAvailability(Availability);
							}
							SendAuxPlayer();
						}
						msg_sent = true;
					}
				}
				if (AdminLevel() >= GM)      // Beta to Admin
				{
					if (MatchOptWithParam("effect", pch, param, msg_sent)) 
					{
						char *cmd = strtok_s(param, " ", &next_token);
						int EffectDID = 0, Length, count = 0;
						if (cmd)
						{
							EffectDID = atoi(cmd);
							cmd = strtok_s(NULL, " ", &next_token);
							count++;
						}
						if (cmd)
						{
							Length = atoi(cmd);
						}
						else
						{
							Length = 4000;
						}

						if (count == 1)
						{
							ObjectEffect WarpEffect;

							WarpEffect.Bitmask = 0x07;
							WarpEffect.EffectDescID = EffectDID;
							WarpEffect.EffectID = sm->GetSectorNextObjID();;
							WarpEffect.GameID = GameID();
							WarpEffect.Duration = Length;
							WarpEffect.TimeStamp = GetNet7TickCount();

							m_Effects.AddEffect(&WarpEffect);

							//SendObjectEffectRL(&WarpEffect);

							SendVaMessage("Send Effect %d Disc: %d for %dms", WarpEffect.EffectID, EffectDID, Length);
						}
						else
						{
							SendVaMessage("/effect <effect_desc_id> <length>");
						}

						success = true;
						msg_sent = true;
					} 
					else if (MatchOptWithParam("effecto", pch, param, msg_sent))	
					{
						char *cmd = strtok_s(param, " ", &next_token);
						int EffectDID = 0, Length, count = 0;
						float scale = 1.0f, speedup = 0.0f;
						short bitmask = 0x07;
						if (cmd)
						{
							EffectDID = atoi(cmd);
							cmd = strtok_s(NULL, " ", &next_token);
							count++;
						}
						if (cmd)
						{
							Length = atoi(cmd);
							cmd = strtok_s(NULL, " ", &next_token);
							if (cmd)
							{
								scale = (float)atof(cmd);
								bitmask += 0x40;
								cmd = strtok_s(NULL, " ", &next_token);
								if (cmd)
								{
									speedup = (float)atof(cmd);
									bitmask += 0x100;
								}
							}
						}
						else
						{
							Length = 4000;
						}

						if (count == 1)
						{
							ObjectToObjectEffect OBTOBE;
							memset(&OBTOBE, 0, sizeof(ObjectToObjectEffect));

							OBTOBE.Bitmask = bitmask;
							OBTOBE.GameID = GameID();
							OBTOBE.TargetID = ShipIndex()->GetTargetGameID();
							OBTOBE.EffectDescID = EffectDID;// 0x00BF;
							OBTOBE.Message = 0;
							OBTOBE.EffectID = sm->GetSectorNextObjID();
							OBTOBE.Duration = Length;
							OBTOBE.TimeStamp = GetNet7TickCount();
							OBTOBE.Scale = scale;
							OBTOBE.Speedup = speedup;

							if (Hijackee())
							{
								ObjectManager *om = GetObjectManager();
								OBTOBE.GameID = Hijackee();

								if (om)
								{
									//use the target object's Range List
									Object *obj = om->GetObjectFromID(Hijackee());
									obj->SendObjectToObjectEffectRL(&OBTOBE);
								}
							}
							else
							{
								SendObjectToObjectEffectRL(&OBTOBE);
							}
							SendVaMessage("Send Effect %d Disc: %d to Object", OBTOBE.EffectID, EffectDID);
						}
						else
						{
							SendVaMessage("/effecto <effect_desc_id> <length>");
						}
						success = true;
						msg_sent = true;
					} 
					else if (MatchOptWithParam("effects", pch, param, msg_sent)) 
					{
						int EffectID = atoi(strtok_s(param,  " ", &next_token));

						m_Effects.RemoveEffect(EffectID);

						SendVaMessage("Stopping Effect %d", EffectID);
						success = true;
						msg_sent = true;
					}
					else if (MatchOptWithParam("exposedecos", pch, param, msg_sent))  
					{
						success = true;
						msg_sent = true;

						if (GetObjectManager())
						{
							bool selection = false;
							if (strcmp(param, "on") == 0) selection = true;

							if (selection)
							{
								if (!ObjectIsMoving())
								{
									SendVaMessage("Nearby decos should now be clickable.");
									m_ExposeDecos = true;
									if (sm->GetSectorType() == 0)
									{
										RemoveFromAllSectorRangeLists();
										sm->SectorServerHandoff(this, PlayerIndex()->GetSectorNum());										
									}
									else
									{
										SendVaMessage("Unable to auto-wormhole you, please leave sector and return to expose decos.");
									}

									success = true;
									msg_sent = true;
								}
								else
								{
									SendVaMessage("You should be stationary when using /exposedecos.");
								}
							}
							else
							{
								m_ExposeDecos = false;
								SendVaMessage("Decos no longer exposed - gate out of system to return to normal");
							}
						}
					}
					else if (strcmp(pch, "errorson") == 0)
					{
						if (AdminLevel() >= BETA_PLUS) // low right now for zapgun's loot builders
						{
							int channel_id = g_PlayerMgr->GetChannelFromName("Errors");
							m_ChannelSubscription[channel_id] = true;
							SendVaMessage("Error messages on [ch.%d].", channel_id);
							msg_sent = true;
							success = true;
						} 
						else
						{
							msg_sent = true;
							success = false;
						}
					}
					else if (strcmp(pch, "errorsoff") == 0)
					{
						if (AdminLevel() >= BETA_PLUS)
						{
							int channel_id = g_PlayerMgr->GetChannelFromName("Errors");
							m_ChannelSubscription[channel_id] = false;
							SendVaMessage("Error messages off.");
							msg_sent = true;
							success = true;
						}
						else
						{
							msg_sent = true;
							success = false;
						}
					}
				}
			}
			break;
		case 'f':
			if (AdminLevel() >= GM)      // Beta to Admin
			{
				if (MatchOptWithParam("form", pch, param, msg_sent) && AdminLevel() >= 50)
				{
					SendVaMessage("Forming up target!");
					SendFormationPositionalUpdate(this->GameID(), ShipIndex()->GetTargetGameID(), 100, 100, 100);
					success = true;
					msg_sent = true;
				}

				if (strcmp(pch, "flushinv") == 0)
				{
					// Flush inventory clean!
					for(u32 Slot=0;Slot<ShipIndex()->Inventory.GetCargoSpace();Slot++)
					{
						if (ShipIndex()->Inventory.CargoInv.Item[Slot].GetItemTemplateID() != -1)
						{
							ShipIndex()->Inventory.CargoInv.Item[Slot].Empty();
							SaveInventoryChange(Slot);
						}
					}

					SendAuxShip();
					SendVaMessage("Your inventory is now flushed!");

					success = true;
					msg_sent = true;
				}
				else if (strcmp(pch, "factionset") == 0)
				{
					AwardFaction(1, 5);
				}
				else if (strcmp(pch, "factionoverride") == 0)
				{
					if (AdminLevel() >= GM)
					{
						if (!GetOverrideFaction())
						{
							SendVaMessageC(12, "You can now use all class & faction restricted gates.");
							SetOverrideFaction(true);
						}
						else
						{
							SendVaMessageC(12, "You are now class & faction restricted in your gate travel.");
							SetOverrideFaction(false);					
						}
						success = true;
						msg_sent = true;
					}
					else
					{
						SendVaMessage("Override faction command not available");
					}
				}
			}

			if (AdminLevel() >= GM)      // GM to Admin
			{
				if (strcmp(pch,"fetch") == 0) //no args
				{
					success = HandleFetchRequest();
					msg_sent = true;
				}

				if (MatchOptWithParam("find", pch, param, msg_sent))
				{
					Player * target = g_ServerMgr->m_PlayerMgr.GetPlayer(param);
					if (target)
					{
						SendVaMessage("Player found! Name: `%s` GameID: %x Account: `%s`",target->Name(), target->GameID(),target->AccountUsername());
					}
					else
					{
						SendVaMessage("Player `%s` not found!", param);
					}
					msg_sent =  true;
				}
			}

			if (strcmp(pch, "face") == 0)
			{
				success = HandleFaceRequest(ShipIndex()->GetTargetGameID());
				msg_sent = true;
			}
			else if (strcmp(pch, "faceme") == 0)
			{
				success = HandleFaceMeRequest(ShipIndex()->GetTargetGameID());
				msg_sent = true;
			}
			/*
			else if (strcmp(pch, "faceawayfromme") == 0)
			{
			success = HandleFaceAwayFromMeRequest(ShipIndex()->GetTargetGameID());
			msg_sent = true;
			}
			*/
			else if (strcmp(pch, "fgps") == 0)
			{
				SendConfirmedActionOffer();
				success = true;
				msg_sent = true;
			}
			else if (strcmp(pch, "fireweapon") == 0)
			{
				HandleFireMOBWeapon();
				success = true;
				msg_sent = true;
			}

			/*TODO:
			SendVaMessage("/faddore <ore itemID from database> - adds ore choice to field");
			SendVaMessage("/fremoveore <ore itemID from database> - removes ore choice from field");*/

			if (AdminLevel() >= DEV) 
			{
				if (MatchOptWithParam("fhelp", pch, param, msg_sent, true))
				{
					success = HandleChangeFieldRequest("-1", 0);
					msg_sent = true;
				}
				if (MatchOptWithParam("fradius", pch, param, msg_sent))
				{
					success = HandleChangeFieldRequest(param, 1);
					msg_sent = true;
				}
				else if (MatchOptWithParam("ftype", pch, param, msg_sent))
				{
					success = HandleChangeFieldRequest(param, 2);
					msg_sent = true;
				}
				else if (MatchOptWithParam("flevel", pch, param, msg_sent))
				{
					success = HandleChangeFieldRequest(param, 3);
					msg_sent = true;
				}
				else if (MatchOptWithParam("fcount", pch, param, msg_sent))
				{
					success = HandleChangeFieldRequest(param, 4);
					msg_sent = true;
				}
				else if (MatchOptWithParam("faddasteroidtype", pch, param, msg_sent))
				{
					success = HandleChangeFieldRequest(param, 5);
					msg_sent = true;
				}
				else if (MatchOptWithParam("faddoretofield", pch, param, msg_sent))
				{
					success = HandleChangeFieldRequest(param, 6);
					msg_sent = true;
				}
				else if (MatchOptWithParam("fdelorefromfield", pch, param, msg_sent))
				{
					success = HandleChangeFieldRequest(param, 7);
					msg_sent = true;
				}
				else if (MatchOptWithParam("faddoretosector", pch, param, msg_sent))
				{
					success = HandleChangeFieldRequest(param, 8);
					msg_sent = true;
				}
				else if (MatchOptWithParam("fdelorefromsector", pch, param, msg_sent))
				{
					success = HandleChangeFieldRequest(param, 9);
					msg_sent = true;
				}
			}
			break;

		case 'g':
			if (MatchOptWithParam("gc", pch, param, msg_sent, true))
			{
				HandleGuildCommand(param);
				msg_sent = true;
				success = true;
			}
			else if (AdminLevel() >= GM && MatchOptWithParam("gmgc", pch, param, msg_sent, true))
			{
				HandleGuildGMCommand(param);
				msg_sent = true;
				success = true;
			}
			if (MatchOptWithParam("getgmitems", pch, param, msg_sent, true))
			{
				LoadGMItems();

				msg_sent = true;
				success = true;
			}
			// Global Chat
            else if (strncmp(pch, "global ", 7) == 0)
			{
				if (AdminLevel() >= BETA_PLUS)
				{
					char msg[1024];

					sprintf(msg, "%s:%s", Name(), &pch[7]);
					g_PlayerMgr->GlobalMessage(msg);
					g_PlayerMgr->GlobalAdminMessage(msg);

					msg_sent = true;
					success = true;
				}
			}
			else if (MatchOptWithParam("gm", pch, param, msg_sent) && AdminLevel() >= GM)
			{
				//char *message =&param[1];
				if (param)
				{
					g_PlayerMgr->ChatSendChannel(GameID(), "GM", param);
				}
				msg_sent = true;
			} 
			else if (strcmp(pch, "gmon") == 0)
			{
				if (AdminLevel() >= GM) // low right now for zapgun's loot builders
				{
					int channel_id = g_PlayerMgr->GetChannelFromName("GM");
					m_ChannelSubscription[channel_id] = true;
					SendVaMessage("GM channel on.");
					msg_sent = true;
					success = true;
				} 
				else
				{
					msg_sent = true;
					success = false;
				}
			}
			else if (strcmp(pch, "gmoff") == 0)
			{
				if (AdminLevel() >= GM)
				{
					int channel_id = g_PlayerMgr->GetChannelFromName("GM");
					m_ChannelSubscription[channel_id] = false;
					SendVaMessage("GM channel off.");
					msg_sent = true;
					success = true;
				}
				else
				{
					msg_sent = true;
					success = false;
				}
			}
			else if (MatchOptWithParam("gwho", pch, param, msg_sent, true))
			{
				if (!param)
				{
					g_ServerMgr->m_PlayerMgr.ListPlayersAndLocations(this, GM, GM);
				}
				else
				{
					char *searchString = strtok_s(param, " ", &next_token);
					if (searchString)
					{
						g_ServerMgr->m_PlayerMgr.ListPlayersWithSearch(this, searchString, GM, GM);
					}
					else
					{
						g_ServerMgr->m_PlayerMgr.ListPlayersAndLocations(this, GM, GM);
					}
				}

				msg_sent = true;
				success = true;
			}
			else if (MatchOptWithParam("getstat", pch, param, msg_sent) && AdminLevel() >= GM)
			{
				float final = m_Stats.GetStat(param);
				float base = m_Stats.GetStatType(param,STAT_BASE_VALUE);
				float mult = m_Stats.GetStatType(param,STAT_BUFF_MULT);
				float add  = m_Stats.GetStatType(param,STAT_BUFF_VALUE);
				float div  = m_Stats.GetStatType(param,STAT_DEBUFF_MULT);
				float sub  = m_Stats.GetStatType(param,STAT_DEBUFF_VALUE);
				SendVaMessage("%s %f: base %f mult %f add %f divide %f sub %f",param,final,base,mult,add,div,sub);
				success = true;
				msg_sent = true;
			}
			else if (MatchOptWithParam("gform", pch, param, msg_sent) && AdminLevel() >= GM)
			{
				SendVaMessage("Group Formation Set, Please accept!");
				this->PlayerIndex()->GroupInfo.SetFormationName(param);
				this->PlayerIndex()->GroupInfo.SetFormation(1);
				this->PlayerIndex()->GroupInfo.SetPosition(-1);
				this->SendAuxPlayer();
				success = true;
				msg_sent = true;
			}
			else if (MatchOptWithParam("gwormhole", pch, param, msg_sent) && AdminLevel() >= BETA_PLUS)
			{
				int GroupID = this->GroupID();
				Player * p = NULL;

				if (param)
				{
					int SectorID = atoi(param);
					if (SectorID == 0)
						SectorID = sm->GetSectorIDFromName(param);
					if (SectorID > MAX_SECTOR_ID)
					{
						SendVaMessage("You can not wormhole to a station");
					}
					else if (GroupID != -1)
					{
						for(int x=0;x<6;x++)
						{
							int GameID = g_PlayerMgr->GetMemberID(GroupID, x);
							p = g_ServerMgr->m_PlayerMgr.GetPlayer(GameID);
							// Lets send them an invite if they are not in the same sector as we are going to
							// and that we are not in a starbase
							if (p && p->PlayerIndex()->GetSectorNum() != SectorID && p->PlayerIndex()->GetSectorNum() <= MAX_SECTOR_ID)
							{
								char Message[200];
								_snprintf(Message, sizeof(Message), 
									"%s is asking you to goto SectorID: %d.  Do you want to take this WormHole?", 
									this->Name(), SectorID);
								p->SendConfirmation(Message, this->GameID(), SectorID, 3);	// Send GM Wormhole Conform
							}
						}
					}
					success = true;
					msg_sent = true;
				}
			}
			else if (strcmp(pch, "goto") == 0)
			{
				success = HandleGotoRequest();
				msg_sent = true;
			}
			else if (strcmp(pch, "groupc") == 0)
			{
				SendVaMessage("Group Count = %d", g_ServerMgr->m_PlayerMgr.GetMemberCount(GroupID()));
				msg_sent = true;
			}            
			else if (strcmp(pch, "groupid") == 0)
			{
				SendVaMessage("Group ID = %d", GroupID());
				msg_sent = true;
			}            
			else if (strcmp(pch, "gameid") == 0)
			{
				SendVaMessage("Game ID = %x", GameID());
				msg_sent = true;
			}            
			break;

		case 'h':
			if (strcmp(pch, "hijack") == 0 && ShipIndex()->GetTargetGameID() > 0)
			{
				if (AdminLevel() >= GM)      // GM to Admin
				{
					success = HandleObjectHijack();
					msg_sent = true;
				}
				else
				{
					SendVaMessage("You do not have hijack permission.\n" );
				}
			}
			if (strcmp(pch, "heading") == 0)
			{
				float *heading = Heading();
				SendVaMessage("Heading: %.5f %.5f %.5f", heading[0], heading[1], heading[2]);
				msg_sent = true;
				success = true;
			}
			if (MatchOptWithParam("ht", pch, param, msg_sent))
			{
				int head = atoi(strtok_s(param,  " ", &next_token));
				int body = atoi(strtok_s(NULL, " ", &next_token));
				int gender = atoi(strtok_s(NULL, " ", &next_token));
				SendVaMessage("Current Head: %d Body: %d G: %d", Database()->avatar.head_type, Database()->avatar.body_type, 
					Database()->avatar.gender);

				Database()->avatar.head_type = head;
				Database()->avatar.body_type = body;
				Database()->avatar.gender = gender;
				success = true;
				msg_sent =  true;
			}
			else if (strcmp(pch, "helpedit") == 0)
			{
				SendVaMessage("/hijack - take control of object.");
				SendVaMessage("/release - release control of object.");
				SendVaMessage("/exposedecos on - make all decos in sector clickable.");
				SendVaMessage("/exposedecos off - decos unclickable; back to normal.");
				SendVaMessage("/faceme - selected object faces player.");
				SendVaMessage("/panup <value> - pans the selected object up or down by the value (use negative for down).");
				SendVaMessage("/panx <value> - pans selected object in x axis (east/west on map).");
				SendVaMessage("/pany <value> - pans selected object in y axis (north/south on map).");
				SendVaMessage("/panz <value> - pans selected object in z axis (up/down) - same as panup.");
				SendVaMessage("/rotatex <value> - rotates selected object in x axis (roll).");
				SendVaMessage("/rotatey <value> - rotates selected object in y axis (pitch)");
				SendVaMessage("/rotatez <value> - rotates selected object in z axis (yaw)");
				SendVaMessage("/levelout - makes the selected object level - useful after positioning by /hijack.");
				SendVaMessage("/scale <value> - change scale of object to floating point <value> eg 1.2");
				SendVaMessage("/setradius     - used on its own, this will set the server's idea of the size of the object.");
				SendVaMessage("   ... it will set the radius to the distance you are from the object targetted.");
				SendVaMessage("/setradius <value> - change the server's idea of the size of the object to <value>");
				SendVaMessage("/signature <value> - change sig of object to floating point <value> eg 20000");
				SendVaMessage("/planetspin <value 1...1000> - gives a planet type a rotational spin. 1000 is fast, 1 is slow");
				SendVaMessage("/tilt <value 0...90> - gives a planet type a tilt, in degrees");
				SendVaMessage("/commit (DEV only) - commits changes to selected object to database.");
				SendVaMessage("//rsectors - undo all non-committed changes.");
				SendVaMessage("//killfactions - delete all the player's current faction ratings and refresh with baseline at next login.");
				SendVaMessage("//displayfactions - list the current default standings with each faction for your player's class.");
				SendVaMessage("//editfaction <faction id from //displayfactions> <value -9000...9999> change the base faction standing for your player's class.");
				success = true;
				msg_sent =  true;
			}
			else if (strncmp(pch, "helpfield", 9) == 0)
			{
				SendVaMessage("/fradius xxxx - adjust field radius");
				SendVaMessage("/ftype <0-5> - change the field spread type");
				SendVaMessage("/flevel <1-8>");
				SendVaMessage("/fcount xxxx - adjust field asteroid count");
				SendVaMessage("/faddasteroidtype <roid basset> - add additional asteroid type");
				SendVaMessage("/faddore <ore itemID from database> - adds ore choice to field");
				SendVaMessage("/fremoveore <ore itemID from database> - removes ore choice from field");
				SendVaMessage("/addbaseore <ore itemID from database> - adds ore choice to all fields in this sector");
				SendVaMessage("/removebaseore <ore itemID from database> - removes ore choice from all fields in this sector.");
				SendVaMessage(" note that field ore choices override base ore choices, so you could have a sector with a certain ore in one field only.");
				success = true;
				msg_sent =  true;
			}
			break;

		case 'i':
			if (MatchOptWithParam("invite", pch, param, msg_sent))
			{
				long game_id = g_ServerMgr->m_PlayerMgr.GetGameIDFromName(param);
				if (game_id == -1)
				{
					SendVaMessage("Could not find player %s",param);
					return;
				}

				if (game_id == GameID())
				{
					if (!(AdminLevel() >= GM))      // GM to Admin
					{
						SendVaMessage("Cannot group with yourself!");
						return;
					}
				}
				g_ServerMgr->m_PlayerMgr.GroupInvite(GroupID(),GameID(), game_id);
				msg_sent = true;
			}

			if (AdminLevel() >= GM)
			{
				if (strcmp(pch, "invisible") == 0)
				{
					SendVaMessageC(17, "Total Invisiblity %s", m_ScanInvisible ? "off" : "on" );
					m_ScanInvisible = !m_ScanInvisible;
					if (m_ScanInvisible)
						BlankRangeList();
					else
						UpdatePlayerVisibilityList();
					success = true;
					msg_sent = true;
				}
				else if (strcmp(pch, "invis") == 0)
				{
					char *ptr = 0;
					if (strlen(pch) > 7) 
					{
						ptr = pch + 7;
					}
					success = HandleInvis(ptr);
					msg_sent = true;				
				}
			}
			break;

		case 'k':
			if (AdminLevel() >= GM)      // GM to Admin
			{
				if (MatchOptWithParam("kick", pch, param, msg_sent))
				{
					success = HandleKick(param);
					msg_sent = true;
				}
			}

		case 'l':
			if (strcmp(pch, "leavegroup") == 0)
			{
				g_ServerMgr->m_PlayerMgr.LeaveGroup(GroupID(),GameID());
				msg_sent = true;
			}
			else if (strcmp(pch, "levelout") == 0)
			{
				success = HandleLevelOutRequest(ShipIndex()->GetTargetGameID());
				msg_sent = true;
			}
			else if (MatchOptWithParam("level", pch, param, msg_sent))
			{
#ifdef DEV_SERVER
				if (AdminLevel() >= GM)      // Beta Plus to Admin
#else
				if (AdminLevel() >= GM)      // Beta Plus to Admin
#endif
				{
					if (atoi(param) < 0 || atoi(param) > 50)
					{
						SendVaMessage("0 <= Level <= 50");
						return;
					}

					PlayerIndex()->RPGInfo.SetCombatLevel(atoi(param));
					PlayerIndex()->RPGInfo.SetTradeLevel(atoi(param));
					PlayerIndex()->RPGInfo.SetExploreLevel(atoi(param));
					PlayerIndex()->RPGInfo.SetSkillPoints(atoi(param) * 10);
					LevelUpForSkills();
					UpdateSkills();
					SendAuxPlayer();
					SaveAdvanceLevel(XP_COMBAT, atoi(param));
					SaveAdvanceLevel(XP_EXPLORE, atoi(param));
					SaveAdvanceLevel(XP_TRADE, atoi(param));

					SendVaMessage("Combat, Explore and Trade LVLs set to %d",atoi(param));
					SendVaMessage("Additionally, you now have %d skillpoints",atoi(param) * 10);
					msg_sent = true;
				}
				else
				{
					SendVaMessage("/level not available at [BETA] and below");
					msg_sent = true;
				}
			}
			else if (strcmp(pch, "lootstats") == 0 && AdminLevel() >= BETA && om)
			{
				Object *obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
				if (obj)
				{
					obj->DisplayLoot(this);
				}
				success = true;
				msg_sent = true;
			}
			break;

		case 'm':
			if (MatchOptWithParam("move", pch, param, msg_sent))
			{
				success = HandleMoveRequest(param);
				msg_sent = true;
			}
			else if (MatchOptWithParam("mobaggro", pch, param, msg_sent) && AdminLevel() >= 80)
			{
				success = HandleAggroSetting(param);
				msg_sent = true;
			}
			else if (MatchOptWithParam("music", pch, param, msg_sent) && AdminLevel() >= 80)
			{
				if (param)
				{
					long music_id = atoi(param);
					PlayerIndex()->SetMusicID(music_id);
				}

				success = true;
				msg_sent = true;
			}
			/*
			else if (MatchOptWithParam("menacetest", pch, param, msg_sent) && AdminLevel() >= GM)
			{
			success = HandleMenaceTest(param);
			msg_sent = true;
			}
			*/
			break;

		case 'n':
			if (strcmp(pch, "noattack") == 0 && AdminLevel() >= GM)
			{
				SendVaMessageC(17, "Combat immunity %s", m_CombatImmunity ? "off" : "on" );
				m_CombatImmunity = !m_CombatImmunity;
				msg_sent = true;
				success = true;
			}
			if (strcmp(pch, "notells") == 0 && AdminLevel() >= GM)
			{
				SendVaMessageC(17, "Allow tells %s", m_TellsFromFriendsOnly ? "off" : "on" );
				m_TellsFromFriendsOnly = !m_TellsFromFriendsOnly;
				msg_sent = true;
				success = true;
			}
			break;

		case 'o':
			if (strcmp(pch, "ori") == 0)
			{
				float *ori = Orientation();
				SendVaMessage("Orientation: %.5f %.5f %.5f %.5f", ori[0], ori[1], ori[2], ori[3]);
				msg_sent = true;
				success = true;
			}

			if (MatchOptWithParam("orientation", pch, param, msg_sent))
			{
				success = HandleOrientationRequest(param);
				msg_sent = true;
			}
			else if (MatchOptWithParam("oeuler", pch, param, msg_sent))				
			{
				success = HandleEulerOrientationRequest(param);
				msg_sent = true;
			} 
			else if (MatchOptWithParam("openif", pch, param, msg_sent))
			{
				char *a = strtok_s(param, ",", &next_token);
				char *b = strtok_s(NULL, ",", &next_token);
				if (b) 
				{
					OpenInterface(atoi(a), atoi(b));
					SendVaMessage("OpenInterface (%d,%d):", atoi(a), atoi(b));
				}
				msg_sent = true;
			}
			/*else if (MatchOptWithParam("ore", pch, param, msg_sent))
			{
			char *ch0 = strtok(param, ",");
			int base_energy = 0; 
			char *ch = strtok(NULL, ",");
			char *ch2 = strtok(NULL, ",");
			float base_time = 0.0f;
			float base_speed = 0.0f;

			if (ch0 != 0)
			{
			base_energy = atoi(ch0);
			}

			if (ch != 0)
			{
			base_time = (float)atof(ch);
			}

			if (ch2 != 0)
			{
			base_speed = (float)atof(ch2);
			}

			if (base_energy < 1 || base_time == 0 || base_speed == 0)
			{
			SendVaMessage("/ore base ore energy,base ore time,base tractor speed");
			SendVaMessage("Base Ore Energy: %d", m_BaseOreEnergy);
			SendVaMessage("Base Ore Time: %.2f", m_BaseOreTime);
			SendVaMessage("Tractor Speed base: %.2f", m_BaseOreTracSpeed);
			}
			else
			{
			SendVaMessage("New base ore energy: %d", base_energy);
			SendVaMessage("New base ore time: %.2f", base_time);
			SendVaMessage("New tractor speed base: %.2f", base_speed);

			m_BaseOreEnergy = base_energy;
			m_BaseOreTime = base_time;
			m_BaseOreTracSpeed = base_speed;
			}
			msg_sent = true;
			success = true;
			}*/
			break;

		case 'p':
			if (strcmp(pch, "position") == 0)
			{
				SendVaMessage("ObjectID = 0x%08x",ShipIndex()->GetTargetGameID());
				if (ShipIndex()->GetTargetGameID() != -1)
				{
					Object *obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
					if (obj) SendVaMessage("%s @ %.2f %.2f %.2f", obj->Name(), obj->PosX(), obj->PosY(), obj->PosZ());
				}
				msg_sent = true;					
				success = true;
			}
			else if (MatchOptWithParam("packetopt", pch, param, msg_sent))
			{
				success = HandlePacketOptRequest(param);
				msg_sent = true;
			}
			else if (MatchOptWithParam("panup", pch, param, msg_sent))
			{
				success = HandlePanRequest(param, 3);
				msg_sent = true;
			}
			else if (MatchOptWithParam("panx", pch, param, msg_sent))
			{
				success = HandlePanRequest(param, 1);
				msg_sent = true;
			}
			else if (MatchOptWithParam("pany", pch, param, msg_sent))
			{
				success = HandlePanRequest(param, 2);
				msg_sent = true;
			}
			else if (MatchOptWithParam("panz", pch, param, msg_sent))
			{
				success = HandlePanRequest(param, 3);
				msg_sent = true;
			}
			else if (MatchOptWithParam("planetspin", pch, param, msg_sent))
			{
				success = HandleSpinRequest(param);
				msg_sent = true;
			}
			break;

		case 'r':
			if (strcmp(pch, "reffect") == 0)
			{
				//m_SectorMgr->m_EffectManager.RemoveEffectsByPlayer(GameID());
				SendVaMessage("Removed all effects on you!");
				msg_sent = true;
				success = true;
			}
			if (strcmp(pch, "rs") == 0)
			{
				success = HandleRenderStateRequest();
				msg_sent = true;
			}
			else if (strcmp(pch, "release") == 0)
			{
				//release hijack
				HandleReleaseHijack();
				msg_sent = true;
				success = true;
			}
			else if (MatchOptWithParam("rsi", pch, param, msg_sent))
			{
				success = HandleRenderStateInitRequest(param);
			}
			else if (MatchOptWithParam("rsa", pch, param, msg_sent))
			{
				success = HandleRenderStateActivateRequest(param);
			}
			else if (MatchOptWithParam("rsn", pch, param, msg_sent))
			{
				success = HandleRenderStateActivateNextRequest(param);
			}
			else if (strcmp(pch, "rsd") == 0)
			{
				success = HandleRenderStateDeactivate();
				msg_sent = true;
			}
			else if (strcmp(pch, "range") == 0)
			{
				success = HandleRangeRequest();
				msg_sent = true;
			}
			else if (strcmp(pch, "restoreinv") == 0)
			{
				// Flush invtory clean!
				for(u32 Slot=0;Slot<ShipIndex()->Inventory.GetCargoSpace();Slot++)
				{
					ShipIndex()->Inventory.CargoInv.Item[Slot].Empty();
				}

				SendVaMessage("Cargo slots = %d", ShipIndex()->Inventory.GetCargoSpace());

				if (ShipIndex()->Inventory.GetCargoSpace() < 20)
				{
					ShipIndex()->Inventory.SetCargoSpace(20);
					for(u32 i=0;i<ShipIndex()->Inventory.GetCargoSpace();i++)
					{
						ShipIndex()->Inventory.CargoInv.Item[i].Empty();
					}
					SendVaMessage("Restored cargo slots = %d", ShipIndex()->Inventory.GetCargoSpace());
				}

				SendAuxShip();

				success = true;
				msg_sent = true;
			}
			else if (MatchOptWithParam("rotatex", pch, param, msg_sent))
			{
				success = HandleRotateRequest(param, 1);
				msg_sent = true;
			}
			else if (MatchOptWithParam("rotatey", pch, param, msg_sent))
			{
				success = HandleRotateRequest(param, 2);
				msg_sent = true;
			}
			else if (MatchOptWithParam("rotatez", pch, param, msg_sent))
			{
				success = HandleRotateRequest(param, 3);
				msg_sent = true;
			}
			else if (AdminLevel() >= DEV && MatchOptWithParam("removebaseore", pch, param, msg_sent))
			{
				success = HandleChangeFieldRequest(param, 8);
				msg_sent = true;
			}
			/*
			else if (strcmp(pch, "refreshmissionlist") == 0)
			{
			if (AdminLevel() >= 50)      // GM to Admin
			{
			LogMessage("Refreshing mission list\n");
			g_ServerMgr->m_Missions.Initialize();
			success = true;
			}
			}
			*/
			else if (strcmp(pch, "resetchar") == 0)
			{
#ifdef DEV_SERVER
				if (AdminLevel() >= GM)      // GM to Admin
#else
				if (AdminLevel() >= GM)      // GM to Admin
#endif
				{
					SendVaMessageC(17, "Character %s reset to zero", Name());
					success = true;
					msg_sent = true;
					ForceLogout();
					WipeCharacter(); //when they next login, should be back to normal.
				}
			}
			else if (strcmp(pch, "resetmounts") == 0)
			{
				ResetWeaponMounts();
				success = true;
				msg_sent = true;
			}
			else if (strcmp(pch, "resetnavs") == 0)
			{
				if (AdminLevel() >= GM)      // GM to Admin
				{
					memset(m_NavsExplored, 0, sizeof(m_NavsExplored));
					memset(m_NavsExposed, 0, sizeof(m_NavsExposed));
					memset(m_FoundAllSectorNavs, 0, sizeof(m_FoundAllSectorNavs));
					SendVaMessage("All navs have been marked unexplored and undiscovered");
					if (PlayerIndex()->GetSectorNum() <= MAX_SECTOR_ID)
					{
						char thissector[5];
						_snprintf(thissector, 5, "%d\0", PlayerIndex()->GetSectorNum());
						HandleWormholeRequest(thissector);
					}

					msg_sent = true;
				}
				else
				{
					SendVaMessage("/resetnavs not available at [BETA] and below");
					msg_sent = true;
				}
			}
			break;

		case 's' :
			if (strcmp(pch,"slaysectormobs") == 0 && AdminLevel() >= SDEV)
			{
				sm->SlaySectorMobs(this);
				msg_sent = true;
				success = true;
			}
			else if (MatchOptWithParam("script", pch, param, msg_sent) && AdminLevel() >= SDEV) 
			{
#if 0
				// Create a new lua state  
				lua_State *myLuaState = lua_open(); 
				  
				// Connect LuaBind to this lua state 
				luabind::open(myLuaState); 

				// Export our class with LuaBind 
				luabind::module(myLuaState) [ 
					//luabind::def("LogMessage", &LogMessage),
					luabind::class_<Player>("Player") 
						.def("Name", &Player::Name)
						.def("Level", &Player::Level)
						.def("SendVaMessage", &Player::SendVaMessageS)
				]; 

				// Assign MyResourceManager to a global in lua   
				luabind::globals(myLuaState)["Me"] = this;

				char fName[256];
				sprintf(fName, "Scripts\\%s.lua", param);

				if (luaL_dofile(myLuaState, fName) == 1)
				{
					SendVaMessage("Command did not run properly");
				}

				lua_close(myLuaState);
#else
				SendVaMessage("Lua removed until build warnings fixed.");
#endif
				success = true;
				msg_sent = true;
			}
			else if (MatchOptWithParam("sounds", pch, param, msg_sent))
			{
				if (param)
				{
					SendClientSound(param);
				}

				success = true;
				msg_sent = true;
			}
			else if (strcmp(pch,"setturrets") == 0 && AdminLevel() >= SDEV)  //WARNING! do not use this unless turret data is empty, it will blank turret factions
			{
				HandleSetTurrets();
				success = true;
				msg_sent = true;
			}
			else if (strcmp(pch,"setrespawns") == 0 && AdminLevel() >= SDEV)  //WARNING! do not use this unless turret data is empty, it will blank turret factions
			{
				HandleSetRespawns();
				success = true;
				msg_sent = true;
			}
			else if (MatchOptWithParam("scale", pch, param, msg_sent))
			{
				success = HandleScaleRequest(param);
				msg_sent = true;
			}
			else if (MatchOptWithParam("skillpoints", pch, param, msg_sent))
			{
				if (AdminLevel() >= GM)      // GM to Admin
				{
					PlayerIndex()->RPGInfo.SetSkillPoints(atoi(param));
					LevelUpForSkills();
					UpdateSkills();
					SendAuxPlayer();
					SendVaMessage("You now have %d skillpoints", atoi(param));
					success = true;
					msg_sent = true;
				}
			}
			else if (MatchOptWithParam("stat", pch, param, msg_sent))
			{
				if (AdminLevel() >= DEV)      // Beta to Admin
				{
					char *stat = strtok_s(param, " ", &next_token);
					char *types = strtok_s(NULL, " ", &next_token);
					char *values = strtok_s(NULL, " ", &next_token);
					int type = 0;
					float value = 0.0f;

					//beta testers will break this - add checking
					if (types) type = atoi(types);
					if (values) value = (float) atof(values);

					if (stat && type >= 0 && type < 5)
					{
						m_Stats.SetStat(type, stat, value, "USER_TEST");					
						m_Stats.UpdateAux(stat);					
						SendAuxShip();
						SendVaMessage("Update stat %s", stat);
						success = true;
						msg_sent = true;
					}
					else
					{
						SendVaMessage("Usage: stat <stat name> <stat type<0...4>> <stat value>");
					}
				}
			}
			else if (MatchOptWithParam("scan", pch, param, msg_sent))
			{
				if (AdminLevel() >= BETA_PLUS)      // Beta to Admin
				{
					long scan_range = atoi(param);
					long max_scan = AdminLevel() >= 80 ? 400000 : 20000;

					if (scan_range >= 1000 && scan_range <= max_scan)
					{
						//ShipIndex()->CurrentStats.SetScanRange(scan_range);
						m_Stats.ResetStat(STAT_SCAN_RANGE);
						m_Stats.SetStat(STAT_BASE_VALUE, STAT_SCAN_RANGE, (float)scan_range);
						m_Stats.UpdateAux(STAT_SCAN_RANGE);
						SendAuxShip();
						SendVaMessage("Scan range set to %d", scan_range);
						success = true;
						msg_sent = true;
					}
					else
					{
						SendVaMessage("Set Scan Range: /scan <1000..%d>", max_scan);
					}
				}
			}
			else if (MatchOptWithParam("shieldwarnings", pch, param, msg_sent))
			{
				char warning_level = (char)atoi(param);
				if (warning_level > 4 || warning_level < 0)
				{
					warning_level = 2;
				}
				SendVaMessageC(13, "Setting new sound warning level to %d", warning_level);
				m_SoundWarningSetting = warning_level;
				SaveAudioWarnLvl();
				success = true;
				msg_sent = true;
			}

			if (AdminLevel() >= DEV)      // Dev to Admin
			{
				if (MatchOptWithParam("signature", pch, param, msg_sent))
				{
					success = HandleNavChangeRequest(param, 1);
					msg_sent = true;
				}
				else if (strcmp(pch, "setradius") == 0)
				{
					success = HandleNavChangeRequest(0, 2);
					msg_sent = true;
				}
				else if (MatchOptWithParam("setradius", pch, param, msg_sent))
				{
					success = HandleNavChangeRequest(param, 2);
					msg_sent = true;
				} 

				if (strcmp(pch, "shutdown") == 0)
				{
					LogMessage(">>>> SHUTDOWN issued by %s [%s]\n", Name(), AccountUsername());
					g_ServerMgr->m_PlayerMgr.GlobalAdminMessage("Server shutdown in 4 Min!");
					GetSectorManager()->AddTimedCall(0, B_SERVER_SHUTDOWN, 60000, NULL);                    
					m_ShuttingDown = true;
					success = true;
					msg_sent = true;
				}
				else if ((strcmp(pch, "sendp") == 0) && AdminLevel() == SDEV)
				{
					success = true;
					msg_sent = true;
					g_ServerMgr->m_PlayerMgr.SendPlayerWithoutConnection(GameID());
				}
				else if (strcmp(pch, "strings") == 0)
				{
					char buffer[256];
					g_StringMgr->Statistics(buffer, sizeof(buffer));
					SendVaMessage(buffer);
					success = true;
					msg_sent = true;
				}
				else if (strcmp(pch, "stats") == 0)
				{
					Object *obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
					Field *f;
					MOB *m;
					StaticMap *s;
					if (obj)
					{
						switch (obj->ObjectType())
						{
						case OT_FIELD:
							f = (Field*)obj;
							SendVaMessage("Stats for field: %s ID[%d]", f->Name(), f->GetDatabaseUID());
							SendVaMessage("Level: %d", f->Level());
							SendVaMessage("Radius: %.1f", f->FieldRadius());
							SendVaMessage("Spread Type: %d", f->GetFieldType());
							SendVaMessage("Roid Count: %d", f->FieldCount());
							success = true;
							msg_sent = true;
							break;
						case OT_NAV:
						case OT_PLANET:
						case OT_DECO:
						case OT_CAPSHIP:
						case OT_STATION:
						case OT_STARGATE:
							s = (StaticMap*)obj;
							SendVaMessage("Stats for Nav: %s ID[%d]", s->Name(), s->GetDatabaseUID());
							SendVaMessage("Base Asset: %d", s->BaseAsset());
							SendVaMessage("Sig: %.1f", s->Signature());
							SendVaMessage("Radar Range: %.1f", s->RadarRange());
							SendVaMessage("Server radius: %.1f", s->Radius());
							SendVaMessage("Scale: %.3f", s->Scale());
							SendVaMessage("Nav Type: %d", s->NavType());
							if (obj->GetFactionID() > 0)
							{
								char *name = g_ServerMgr->m_FactionData.GetFactionName(obj->GetFactionID());
								float standing = GetFactionStanding(obj);
								SendVaMessage("Faction ID %d [%s]", obj->GetFactionID(), name);
								SendVaMessage("Your standing with that faction: %.1f", standing);
							}
							if (obj->GetClassSpecific())
							{
								SendVaMessage("Gate is class specific.");
								if (obj->GetFactionID() < 1)
								{
									SendVaMessageC(17, "Database error: class specific chosen, no faction specified. Use DASE to change faction affiliation.");
								}
							}
							if (obj->ObjectType() == OT_STARGATE)
							{
								if (obj->Destination() > 0)
								{
									if (obj->IsLocalStargate())
									{
										Object *local_destination = g_SectorObjects[obj->Destination()];
										if (local_destination)
										{
											SendVaMessage("Local Gate Destination ID: %d [%s]", obj->Destination(), local_destination->Name());
										}
									}
									else
									{
										char *sector_name = g_ServerMgr->GetSectorName(obj->Destination());
										SendVaMessage("Gate Destination ID: %d [%s]", obj->Destination(), sector_name);
									}
								}
								else
								{
									SendVaMessage("Stargate does not appear to have a valid destination: %d", obj->Destination());
								}
							}
							success = true;
							msg_sent = true;
							break;
						case OT_MOB:
							m = (MOB*)obj;
							SendVaMessage("Stats for MOB: %s GameID[%d]", m->Name(), m->GameID());
							SendVaMessage("Base Asset: %d", m->BaseAsset());
							SendVaMessage("Scale: %.3f", m->Scale());
							SendVaMessage("MOB Type ID: %d", m->GetMOBType());
							SendVaMessage("MOB DPS: %.2f", m->GetMOBDPS());
							SendVaMessage("MOB Shields: %.2f", m->GetShieldLevel());
							SendVaMessage("MOB Aggro setting: %d", m->GetMOBAggroLevel());
							if (m->GetSpawnName()) SendVaMessage("Part of Spawn name: %s", m->GetSpawnName());
							if (m->GetFactionID() > 0)
							{
								char *name = g_ServerMgr->m_FactionData.GetFactionName(obj->GetFactionID());
								float standing = GetFactionStanding(obj);
								SendVaMessage("Faction ID %d [%s]", obj->GetFactionID(), name);
								SendVaMessage("Your standing with that faction: %.1f", standing);
							}
							else
							{
								SendVaMessage("No Faction data.");
							}
							success = true;
							msg_sent = true;
							break;
						default:
							break;
						}
					}
				}
				else if (MatchOptWithParam("shieldbuff", pch, param, msg_sent))
				{
					if(param && AdminLevel() >= GM)
					{
						if(isdigit((unsigned char)param[0]))
						{
							float BuffAmount = (float)atoi(param);
							AddDamageShield(DAMAGE_ABSORB,1,-1,BuffAmount);
							SendVaMessage("%d shield buff added",(int)BuffAmount);
						}
						else
						{
							DamageShield *ds = FindDamageShield(DAMAGE_ABSORB);
							if(ds)
							{
								SendVaMessage("Absorb Left: %f", ds->capacitance);
							}
							else
							{
								SendVaMessage("Absorb Left: 0");
							}
						}
					}
					success = true;
					msg_sent = true;
				}
			}
			break;

			// /test command (do not remove)
		case 't':
			if (strcmp(pch, "test") == 0)
				//if (MatchOptWithParam("test", pch, param, msg_sent))
			{
				SendVaMessage("Test Successful!");
				PlayerIndex()->GroupInfo.SetAllowGroupInvite(true);
				SendAuxPlayer();
				msg_sent =  true;
			}

			if (_strcmpi(pch, "talktree") == 0)
			{
				char string[] = 
					"That was one heck of an explotion! Are you alright over there?\0"
					"\0\3"
					"\0\0\0\0"
					"I need a tow\0"
					"\1\0\0\0"
					"Toggle distress beacon\0"
					"\2\0\0\0"
					"I'm OK\0";

				SendOpcode(ENB_OPCODE_0054_TALK_TREE, (unsigned char *) string, sizeof(string));
			}

			if (MatchOptWithParam("testmsg", pch, param, msg_sent) && (AdminLevel() >= DEV))
			{
				long time_delay = atoi(param);
				sm->AddTimedCall(this, B_TEST_MESSAGE, time_delay, 0, time_delay, GetNet7TickCount());

				msg_sent = true;
				success = true;
			}

			if (MatchOptWithParam("tilt", pch, param, msg_sent))
			{
				success = HandleTiltRequest(param);
				msg_sent = true;
			}

			if (MatchOptWithParam("terminate", pch, param, msg_sent))
			{
				Player *p = g_ServerMgr->m_PlayerMgr.GetPlayer(param);

				if (!p)
				{
					p = g_ServerMgr->m_PlayerMgr.GetPlayer(atoi(param) + 0x40000000);
				}

				//send terminate to net7 proxy
				if (p && AdminLevel() >= DEV)
				{
					long player_id = p->GameID();
					SendVaMessage("Terminate player %s [%d]", p->Name(), p->GameID());
					p->SendOpcode(ENB_OPCODE_100A_MVAS_TERMINATE_S_C, (unsigned char *) &player_id, sizeof(long));
				}
			}

			if (MatchOptWithParam("trade", pch, param, msg_sent))
			{
				// Trade requires that both players recieved a create packet for eachother's ships
				Player * targetp = g_ServerMgr->m_PlayerMgr.GetPlayer(param);

				if (!targetp)
				{
					SendVaMessage("Could not find player %s",param);
					return;
				}

#ifdef DEV_SERVER
				if (!PlayerInRangeList(targetp) && (AdminLevel() < BETA))
#else
				if (!PlayerInRangeList(targetp) && (AdminLevel() < BETA_PLUS))
#endif
				{
					SendVaMessageC(17,"%s needs to be closer for trade", targetp->Name());
					return;
				}

				if (targetp->GameID() == GameID())
				{
					if (!(AdminLevel() >= GM))      // GM to Admin
					{

						SendVaMessage("Cannot trade with yourself!");
						return;
					}
				}

				if (targetp->m_TradeID != -1)
				{
					SendVaMessage("Target is already trading");
					return;
				}

				LogDebug("Clearing trade for players `%s` and `%s`\n",Name(),targetp->Name());
				ClearTradeWindowForBoth(targetp);

				SendAuxShip(targetp);
				targetp->SendAuxShip(this);

				TradeAction(targetp->GameID(),0);					// Opens a trade window
				targetp->TradeAction(GameID(), 0);	// Open trade window for other player
				m_TradeID = targetp->GameID();						// Set player tradeing with
				m_TradeConfirm = 0;
				targetp->m_TradeID = GameID();
				targetp->m_TradeConfirm = 0;

				msg_sent = true;
			}
			break;

		case 'u' :
			if (MatchOptWithParam("uitrigger", pch, param, msg_sent))
			{
				char *a = strtok_s(param, ",", &next_token);
				char *b = strtok_s(NULL, ",", &next_token);
				if (b)
				{
					int Index = 0;
					unsigned char Data[75];

					*((long*) &Data[Index]) = atoi(a);
					Index+=4;
					*((long*) &Data[Index]) = atoi(b);
					Index+=4;    
					SendOpcode(ENB_OPCODE_0065_UI_TRIGGER, (unsigned char *) &Data, Index);
				}
				msg_sent = true;
			}
			else if (MatchOptWithParam("upgrade", pch, param, msg_sent))
			{
#ifdef DEV_SERVER
				if (AdminLevel() >= GM) //Dev +
#else
				if (AdminLevel() >= GM) //Dev +
#endif
				{
					long upgrade = atoi(param);
					ShipUpgrade(upgrade);
					msg_sent = true;
					success = true;
				}
			}
			else if (strcmp(pch, "undockp") == 0)
			{
				success = true;
				msg_sent = true;
				DebugPlayerDock(false);
				//g_ServerMgr->m_PlayerMgr.SendPlayerWithoutConnection(GameID());
			}
			else if (strcmp(pch, "uptime") == 0)
			{
				success = true;
				msg_sent = true;
				int RSec = GetNet7TickCount() / 1000;
				int Sec = RSec % 60;
				int Min = (RSec/60) % 60;
				int Hours = (RSec/3600);

				SendVaMessage("Uptime: %d Hour(s) %d Min(s) %d Sec(s)", Hours, Min, Sec);
			}
			break;

		case 'w':
			if (MatchOptWithParam("who", pch, param, msg_sent, true))
			{
				if (!param)
				{
					g_ServerMgr->m_PlayerMgr.ListPlayersAndLocations(this);
				}
				else
				{
					char *searchString = strtok_s(param, " ", &next_token);
					if (searchString)
					{
						g_ServerMgr->m_PlayerMgr.ListPlayersWithSearch(this, searchString);
					}
					else
					{
						g_ServerMgr->m_PlayerMgr.ListPlayersAndLocations(this);
					}
				}

				msg_sent = true;
				success = true;
			}			
			else if (MatchOptWithParam("warp", pch, param, msg_sent))
			{
				int limit = 6000;

				if (AdminLevel() >= GM) //Beta +
				{
					if (atoi(param) > limit || atoi(param) < 1000)
					{
						SendVaMessage("Warp limits are between 1000 and %d!", limit);
					}
					else
					{
						SendVaMessage("Setting warp to %d",atoi(param));
						ShipIndex()->CurrentStats.SetWarpSpeed(atoi(param));
						SendAuxShip();
					}
					msg_sent = true;
				}
			} else if (strcmp(pch, "warpreset") == 0)
			{
				TerminateWarp();
			}

			if (MatchOptWithParam("wormhole", pch, param, msg_sent))
			{
#ifdef DEV_SERVER
				if (AdminLevel() >= BETA)      // Beta Plus to Admin
#else
				if (AdminLevel() >= BETA_PLUS)      // Beta Plus to Admin
#endif
				{
					success = HandleWormholeRequest(param);	
				}
				else
				{
					SendVaMessage("/wormhole GM and above only");
					success = false;
				}
				msg_sent = true;
			}

			if (strcmp(pch, "warpreset") == 0)
			{
				SetWarp();
				TerminateWarp();
				msg_sent = true;
				success = true;
			}

			break;
		}

		if (!success && !msg_sent)
		{
			SendVaMessage("Illegal slash command: %s", pch);
		}
	}
}

void Player::HandleLogoffRequest(unsigned char *data)
{
	//LogoffRequest * request = (LogoffRequest *) data;
	//LogMessage("Received LogoffRequest for player '%s'\n",Name());

	//remove player from the group
	g_ServerMgr->m_PlayerMgr.LeaveGroup(GroupID(),GameID());

	SendLogoffConfirmation();

	g_ServerMgr->m_PlayerMgr.DropPlayerFromGalaxy(this);
}

// CTA = Call To Arms?
void Player::HandleCTARequest(unsigned char *data)
{
	CTARequest * myCTARequest = (CTARequest *) data;

//	LogMessage("CTA Request:\n");
//	DumpBuffer(data, sizeof(CTARequest));

	g_ServerMgr->m_PlayerMgr.GroupAction(myCTARequest->SourceID, myCTARequest->TargetID, myCTARequest->Action);

	//capture_3		packet# 21495
	unsigned char CTAResponse[] =
	{
		0x00, 0x00, 0x00, 0x00,		//GameID
		0x0F, 0x00, 0x00, 0x00,		//RequestType
		0x01						//Success
	};

	*((long*) &CTAResponse[0]) = myCTARequest->SourceID;
	*((long*) &CTAResponse[4]) = myCTARequest->Action;

	SendOpcode(ENB_OPCODE_00BD_CTA_RESPONSE, (unsigned char *) &CTAResponse, sizeof(CTAResponse));
}

void Player::SendLogoffConfirmation()
{
	//LogMessage("Sending LogoffConfirmation packet\n");
	SendOpcode(ENB_OPCODE_00BA_LOGOFF_CONFIRMATION, 0, 0);
	SendPacketCache();
}

// 0 no visible effect
// 1 displays a "skill" button
// 2 maybe a "more" button? (screws up space npc chat)
// 3 displays a "trade" button
// 4 no visible effect
// 5 opens the trade window
// 6 displays a "done" button
// 7 no visible effect
// 8 terminates the tree
void Player::SendTalkTreeAction(long action)
{
	//LogMessage("Sending TalkTreeAction packet\n");
	SendOpcode(ENB_OPCODE_0056_TALK_TREE_ACTION, (unsigned char *) &action, sizeof(long));
}

bool Player::HandleRangeRequest()
{
	bool success = false;
	ObjectManager *om = GetObjectManager();

	if (ShipIndex()->GetTargetGameID() > 0 && om)
	{
		Object *obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
		if (obj)
		{
			SendVaMessageC(12,"Range to centre of object = %f", obj->RangeFrom(Position(), true)); //report absolute range
			SendVaMessageC(12,"Range to edge of object = %f", RangeFrom(obj) ); //report absolute range
			success = true;
		}
	}
	else
	{
		SendVaMessage("Unable to access selected object - could not find object ID");
	}

	return (success);
}

bool Player::HandleWormholeRequest(char *sector)
{
	bool success = false;
	bool illegal_dest = false;
	SectorManager *sm = GetSectorManager();

	if (PlayerIndex()->GetSectorNum() > MAX_SECTOR_ID && AdminLevel() != 90)
	{
		SendVaMessage("Unable to wormhole out of starbase, disembark first.");
	}
	else if (sm)
	{
		m_Gating = true;
		SetWormholed(true);
		FinishAllInstalls();
		TerminateWarp();
		long sector_id = atoi(sector);

		if (sector_id == 0)
			sector_id = sm->GetSectorIDFromName(sector);

		if (sector_id > MAX_SECTOR_ID)
		{
			sector_id /= 10;
		}

		if (sm->GetSectorType() != 0 && AdminLevel() != 90)
		{
			illegal_dest = true;
		}    

		char *current_sector_name = g_ServerMgr->GetSectorName(PlayerIndex()->GetSectorNum());
		char *sector_name = g_ServerMgr->GetSectorName(sector_id);
		if (sector_name && !illegal_dest)
		{
			SendVaMessage("Wormhole from %s to %s\n", current_sector_name, sector_name);
			SendVaMessage("Wormhole out of sector %d", PlayerIndex()->GetSectorNum());

			RemoveFromAllSectorRangeLists();

			//Hand off server
			sm->SectorServerHandoff(this, sector_id);
			success = true;
		}
		else
		{
			SendVaMessage("Invalid or Illegal Sector ID %d", sector_id);
		}
	}

	RemoveProspectNodes();

	return (success);
}

void Player::WormHole(int sector_id)
{
	SectorManager *sm = GetSectorManager();

	SetWormholed(true);

	if (!sm) return;			// THIS SHOULD NOT HAPPEN

	m_Gating = true;

	//finish all player installs
	FinishAllInstalls();

	// Leave formation if in one
	g_ServerMgr->m_PlayerMgr.BreakFormation(GameID());
	g_ServerMgr->m_PlayerMgr.LeaveFormation(GameID());

	RemoveFromAllSectorRangeLists();

	m_FromSectorID = -1; // notify login to position at weft
	//Hand off server
	sm->SectorServerHandoff(this, sector_id);
}

bool Player::HandleInvis(char *param)
{
	if (param)
	{
		if (_stricmp(param, "ON") == 0)
		{
			SetInvisible(true);
		}
		else if (_stricmp(param, "OFF") == 0)
		{
			SetInvisible(false);
		}
		else
		{
			SetInvisible(!GetInvisible());
		}
	}
	else //toggle
	{
		SetInvisible(!GetInvisible());
	}
	return true;
}

bool Player::HandlePacketOptRequest(char *param)
{
	if (param)
	{
		if (_stricmp(param, "ON") == 0)
		{
			m_PeriodicCacheSize = PERIODIC_CACHE_SEND_SIZE_OPT;
		}
		else if (_stricmp(param, "OFF") == 0)
		{
			m_PeriodicCacheSize = PERIODIC_CACHE_SEND_SIZE;
		}
		else if (strcmp(param, "lac") == 0)
		{
			m_PeriodicCacheSize = PERIODIC_CACHE_SEND_SIZE_OPT;
			return true;
		}
		else
		{
			//toggle if user types something odd
			m_PeriodicCacheSize = (m_PeriodicCacheSize == PERIODIC_CACHE_SEND_SIZE) ? PERIODIC_CACHE_SEND_SIZE_OPT : PERIODIC_CACHE_SEND_SIZE;
		}
	}
	else //toggle
	{
		m_PeriodicCacheSize = (m_PeriodicCacheSize == PERIODIC_CACHE_SEND_SIZE) ? PERIODIC_CACHE_SEND_SIZE_OPT : PERIODIC_CACHE_SEND_SIZE;
	}
	SendVaMessageC(12, "New max UDP packet size: %d", m_PeriodicCacheSize);
	return true;
}

bool Player::HandleKick(char *param)
{
	char Message[1024];
	char *next_token;

	char *name = strtok_s(param, " ", &next_token);
	char *reason = 0;

	if (name)
		reason = strtok_s(NULL, "", &next_token);
	else
		return 0;

	Player * target = g_ServerMgr->m_PlayerMgr.GetPlayer(name);

	if (!reason) 
	{
		reason = "No Reason Given";
	}

	if (target) 
	{
		_snprintf(Message, 1024, "Player '%s' kicked by '%s': %s", target->Name(), Name(), reason);
		LogMessage(" ** Kick: %s\n", Message);

		target->SendVaMessage("You have been kicked by %s: %s", Name(), reason);
		g_ServerMgr->m_PlayerMgr.GMMessage(Message);
		Sleep(100);
		target->ForceLogout();
		return 1;
	}

	return 0;    
}

//TODO: spawn a thread to do this, otherwise the GM is waiting for ages for the kick to complete
void Player::ForceLogout()
{
	int Count = 0;

	// Wait 30 seconds for the player to become active, while in space
	/*
	while (!Active() && PlayerIndex()->GetSectorNum() < 10000 && Count < 30)
	{
	Sleep(1000);
	Count++;
	}
	*/

	// Give them 5 seconds to read the kick message!
	//Sleep(5000);

	long GameIDD = GameID();

	SendOpcode(ENB_OPCODE_0003_LOGOFF, (unsigned char*)&GameIDD, sizeof(GameIDD));
	SendPacketCache();
	Sleep(100);

	g_ServerMgr->m_PlayerMgr.LeaveGroup(GroupID(),GameID());
	g_ServerMgr->m_PlayerMgr.DropPlayerFromGalaxy(this);
	/*m_SectorMgr->RemovePlayerFromSectorList(this);
	g_ServerMgr->m_PlayerMgr.UnallocatePlayer(this);*/
}

bool Player::HandleBaseItemListCreate()
{
	char QueryString[256];
	//ok we run a query on the item database, find all the ores and populate the base_ore_list table
	_snprintf(QueryString, sizeof(QueryString), "SELECT * FROM `item_base` WHERE `category` = '81'");
	sql_connection_c connection( "net7", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
	sql_result_c result;
	sql_result_c *ItemList_result = &result;
	sql_query_c ItemList  (&connection);
	sql_query_c ItemUpdate(&connection);
	sql_row_c ItemList_Data;

	// Execute Query
	ItemList.execute( QueryString );
	ItemList.store(ItemList_result);

	if (!ItemList_result || !ItemList_result->n_rows())
		return false;

	for(int item=0;item<ItemList_result->n_rows();item++)
	{
		//create this entry in the base_ore_list table
		sql_query BaseItemBuilder;
		ItemList_result->fetch_row(&ItemList_Data);
		long item_id = ItemList_Data["id"];
		char *name = ItemList_Data["name"];

		// Loop though each sector
		long sector_id = 0;
		while (sector_id = g_ServerMgr->m_SectorContent.GetNextSectorID(sector_id))
		{
			if (sector_id < MAX_SECTOR_ID)
			{
				BaseItemBuilder.Clear();
				BaseItemBuilder.SetTable("base_ore_list");
				BaseItemBuilder.AddData("item_id", item_id);
				BaseItemBuilder.AddData("name", name);
				BaseItemBuilder.AddData("sector_id", sector_id);

				ItemUpdate.run_query(BaseItemBuilder.CreateQuery());
			}
		}
	}

	SendVaMessage("Base item list completed.");

	return true;
}

bool Player::HandleAggroSetting(char *param)
{
	bool success = false;
	long aggro_level = atoi(param);
	char queryString[256];
	ObjectManager *om = GetObjectManager();

	Object *obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
	MOB *mob = (0);
	MOBData *mob_data = (0);

	if (obj && obj->ObjectType() == OT_MOB)
	{
		mob = (MOB*)obj;
		mob_data = g_ServerMgr->MOBList()->GetMOBData(mob->GetMOBType());
	}

	if (mob && mob_data && (aggro_level >= 0 && aggro_level <=10) && (aggro_level != mob_data->m_Agressiveness))
	{
		success = true;
		long scan_range = ShipIndex()->CurrentStats.GetScanRange();
		ShipIndex()->CurrentStats.SetScanRange(10); //set ship almost blind
		CheckObjectRanges();
		mob_data->m_Agressiveness = (u8)aggro_level;

		SendVaMessage("Setting mob aggro level of '%s' to %d", mob->Name(), aggro_level);

		//now save new aggro level in MOB DB
		SendVaMessage("Now commit setting to database.");
		sql_connection_c connection( "net7", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
		sql_query_c MobUpdate(&connection);

		_snprintf(queryString, sizeof(queryString),
			"UPDATE `mob_base` SET `aggressiveness` = '%d' WHERE `mob_id` = '%d'", aggro_level, mob->GetMOBType());
		MobUpdate.run_query(queryString);

		Sleep(200);
		ShipIndex()->CurrentStats.SetScanRange(scan_range);
		ResetRangeLists();
		CheckObjectRanges();
	}

	return (success);
}

bool Player::HandleObjectDestruction()
{
	ObjectManager *om = GetObjectManager();
	Object *obj = (0);

	if (Hijackee() == 0) //can only destroy objects while hijacked
	{
		SendVaMessage("You can only destroy objects while you're Hijacking something\n");
		return false;
	}

	if (om) obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());

	if (obj && obj->GameID() == Hijackee())
	{
		SendVaMessage("Target's self preservation instincts kick in, won't destroy itself!\n");
		return false;
	}

	if (obj)
	{
		if (RangeFrom(obj) > 10000.0f)
		{
			SendVaMessage("You must be within 10k of target to destroy it.\n");
			return false;
		}
		//first handle any additional actions that need to be done
		switch (obj->ObjectType())
		{
		case OT_STATION:
			//TODO: transmit a warning and eject all occupants
			break;
		case OT_NAV:
		case OT_PLANET:
			break;
		case OT_STARGATE:
			{
				// seal gate on other side
				long destination = obj->Destination();
				if (!obj->IsLocalStargate())
				{
					//find destination on other side.
					ObjectManager *om = g_ServerMgr->GetObjectManager(destination);
					if (om)
					{
						Object *other_gate = om->FindGate(PlayerIndex()->GetSectorNum());
						if (other_gate)
						{
							//disable this gate
							char *name = g_StringMgr->GetStr("Disabled Gate");
							other_gate->SetName(name);
							other_gate->SetDestination(0);
						}
					}
				}
			}
			break;

		default:
			return false;
			break;
		}
	}

	SectorManager *sm = GetSectorManager();

	//send a scary looking beam
	ObjectToObjectEffect OBTOBE;
	memset(&OBTOBE, 0, sizeof(ObjectToObjectEffect));

	OBTOBE.Bitmask = 0x47;
	OBTOBE.GameID = Hijackee();
	OBTOBE.TargetID = ShipIndex()->GetTargetGameID();
	OBTOBE.EffectDescID = 67;
	OBTOBE.Message = 0;
	OBTOBE.EffectID = sm->GetSectorNextObjID();
	OBTOBE.Duration = 5000;
	OBTOBE.TimeStamp = GetNet7TickCount();
	OBTOBE.Scale = 5.0f;

	obj->SendObjectToObjectEffectRL(&OBTOBE);

	obj->SendObjectEffect(106, 5000);

	//now add time event to finish this object off
	sm->AddTimedCall(this, B_DESTROY_OBJECT, 5000, obj, obj->GameID() );

	return true;
}

bool Player::FindSectorFromName(char *param)
{
	//romp through all the sector names giving each match
	SectorManager **sm_list = g_ServerMgr->GetSectorManagerList();
	long count = g_ServerMgr->GetSectorCount();
	bool found = false;
	bool wildcard = (param[0] == '*') ? true : false;

	if (!param) return false;

	SendVaMessageC(12, "Sectors like '%s':", param);

	char *name;
	for (int i = 0; i < count; i++)
	{
		name = sm_list[i]->GetSectorName();
		if ((name && strstr(name, param) != 0) || wildcard)
		{
			SendVaMessageC(10, " %s : %d Occupancy: %d", name, sm_list[i]->GetSectorID(), sm_list[i]->GetOccupancy());
			found = true;
		}
	}

	if (found == false)
	{
		SendVaMessageC(13,"//findsector is case sensitive eg '//findsector Xipe' will have results, '//findsector xipe' will not.");
	}

	return true;
}

bool Player::HandleRestartSectorComms(char *param)
{
	if (!param) return false;
	long sector_id = atoi(param);
	//first get sector manager for the required sector
	SectorManager *sm = g_ServerMgr->GetSectorManager(sector_id);

	if (sm)
	{
		//this may cause a small amount of lag in the GM's sector
		sm->ShutdownListener();
		sm->ReStartListener();

		SendVaMessageC(17,"Restarted Comms system for sector %s [%d]", sm->GetSectorName(), sm->GetSectorID());
		SendVaMessageC(17,"Port for sector is now %d", sm->GetPort());
	}

	return true;
}

void Player::HandleSetTurrets()
{
	char queryString[256];

	SendVaMessageC(17,"Setting up turrets. This is done as a one-time op to populate the table to get baseline turrets working.");
	SendVaMessageC(17,"Factions for turrets and specific MOB_ID (from MOB database/editor) will need to be selected afterwards.");

	sql_connection_c connection( "net7", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
	sql_query_c TurretQuery(&connection);

	sql_result_c result;
	Object *obj;

	//go through each object in the galaxy database

	for (GlobalObjectList::iterator itrGList = g_SectorObjects.begin(); itrGList != g_SectorObjects.end(); ++itrGList)
	{
		obj = itrGList->second;
		if (obj)
		{
			long object_uid = obj->GetDatabaseUID();

			//is this object a MOB?
			if (obj->ObjectType() != OT_MOB) continue;

			MOB *mob = (MOB*)obj;

			if (mob->IsTurret())
			{
				//Now set type to be type 42 in database
				_snprintf(queryString, sizeof(queryString), 
					"UPDATE net7.sector_objects SET type = '42' WHERE sector_object_id = '%d'", object_uid);

				TurretQuery.run_query(queryString);
			}

#if 0
			AssetData *asset = g_ServerMgr->AssetList()->GetAssetData(base_asset);
			if (asset && strcmp(asset->m_CatName, "Turrets") == 0)
			{
				char *name = obj->Name();
				if (strstr(name, "turret") || strstr(name, "Turret"))
				{
					_snprintf(queryString, sizeof(queryString), 
						"SELECT * FROM net7.sector_objects_turrets WHERE turret_id = '%d'", object_uid);

					//is this turret in the DB?
					TurretQuery.execute(queryString);
					TurretQuery.store(&result);

					if (result.n_rows() == 0)
					{
						sql_query TurretBuilder;
						TurretBuilder.Clear();
						TurretBuilder.SetTable("sector_objects_turrets");

						TurretBuilder.AddData("turret_id", object_uid);
						TurretBuilder.AddData("turret_mob_id", -1);  //set the mob id to -1 so we know a correct mob id hasn't been added yet

						TurretQuery.run_query(TurretBuilder.CreateQuery());
					}
				}
			}
#endif
		}
	}

}

void Player::HandleSetRespawns()
{
	char queryString[256];

	SendVaMessageC(17,"Setting up Respawns. This is done as a one-time op.");

	sql_connection_c connection( "net7", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
	sql_query_c RespawnQuery(&connection);

	sql_result_c result;
	Object *obj;

	//go through each object in the galaxy database

	for (GlobalObjectList::iterator itrGList = g_SectorObjects.begin(); itrGList != g_SectorObjects.end(); ++itrGList)
	{
		obj = itrGList->second;
		if (obj)
		{
			long object_uid = obj->GetDatabaseUID();

			//is this object a MOB?
			if (obj->ObjectType() != OT_FIELD) continue;

			Field *f = (Field*)obj;

			if (f->GetRespawnTimer() > 30)
			{
				long rtime = 30 - rand()%5;
				f->SetRespawnTimer(rtime);
				_snprintf(queryString, sizeof(queryString),
					"UPDATE net7.sector_objects_harvestable SET respawn_timer = '%d' WHERE resource_id = '%d'", rtime, object_uid);

				RespawnQuery.run_query(queryString);
			}
		}
	}

}
bool Player::HandleMoveRequest(char *param)
{
	bool success = false;
	float x, y, z;
	char *next_token;
	char *a = strtok_s(param, ",", &next_token);
	char *b = strtok_s(NULL, ",", &next_token);
	char *c = strtok_s(NULL, ",", &next_token);

	x = y = z = 0.0f;

	if (a == NULL)
	{
		SendVaMessage("/move: syntax: x,y,z");
		SendVaMessage("/move: all parameters float type");
	}
	else
	{
		x = (float)atof(a);

		if (b != NULL)
		{
			y = (float)atof(b);
		}
		else
		{
			y = PosY();
		}

		if (c != NULL)
		{
			z = (float)atof(c);
		}
		else
		{
			z = PosZ();
		}
	}

	if (!this)
	{
		SetPosition(x, y, z);

		SendVaMessage("Setting position to : %d %d %d", x, y, z);

		//g_ServerMgr->m_PlayerMgr.SendLocation(this, this);
		//CheckTargetUpdate();
		UpdateVerbs();
		CheckObjectRanges();
		success = true;
	}

	return (success);
}


bool Player::HandleOrientationRequest(char *orientation)
{
	bool success = false;
	float o1, o2, o3, o4;
	char *next_token;
	char *a = strtok_s(orientation, ",", &next_token);
	char *b = strtok_s(NULL, ",", &next_token);
	char *c = strtok_s(NULL, ",", &next_token);
	char *d = strtok_s(NULL, ",", &next_token);
	ObjectManager *om = GetObjectManager();

	o1 = o2 = o3 = o4 = 0.0f;

	if (a == NULL)//allow incomplete args
	{
		SendVaMessage("/orientation: syntax: o1,o2,o3,o4");
		SendVaMessage("/orientation: all parameters float type");
	}
	else
	{
		o1 = (float)atof(a);

		if (b != NULL)
		{
			o2 = (float)atof(b);
		}
		if (c != NULL)
		{
			o3 = (float)atof(c);
		}
		if (d != NULL)
		{
			o4 = (float)atof(d);
		}


		Object *obj;

		SendVaMessage("Targetting 0x%08x Player = 0x%08x",ShipIndex()->GetTargetGameID(), GameID());

		obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());

		if (obj)
		{
			//New orientation info:
			obj->SetOrientation(o1, o2, o3, o4);

			SendVaMessage("Object name: %s", obj->Name());

			obj->SendPosition(this);

			success = true;
		}
		else
		{
			SendVaMessage("Unable to access selected object - could not find object ID");
		}
	}

	return (success);
}

bool Player::HandleFaceRequest(long Target)
{
	bool success = false;
	ObjectManager *om = GetObjectManager();

	Object *obj = om->GetObjectFromID(Target);

	if (obj)
	{
		FaceObject(obj);
		SendLocationAndSpeed(true);
		success = true;
	}
	else
	{
		SendVaMessage("Unable to access selected object - could not find object ID");
	}

	return (success);
}

bool Player::HandleFaceMeRequest(long Target)
{
	bool success = false;
	ObjectManager *om = GetObjectManager();
	Object *obj = om->GetObjectFromID(Target);

	if (obj)
	{
		obj->CalcOrientation(Position(), obj->Position(), false); //orient towards player
		obj->SendLocationAndSpeed(true);
		success = true;
	}
	else
	{
		SendVaMessage("Unable to access selected object - could not find object ID");
	}

	return (success);
}

/*
bool Player::HandleFaceAwayFromMeRequest(long Target)
{
bool success = false;

Object *obj = GetObjectManager()->GetObjectFromID(Target);

if (obj)
{
obj->CalcReverseOrientation(Position(), obj->Position(), false); // face away from player
obj->SendLocationAndSpeed(true);
success = true;
}
else
{
SendVaMessage("Unable to access selected object - could not find object ID");
}

return (success);
}

bool Player::HandleMenaceTest(char* param)
{
bool success = false;
float duration = 0.0f;
if (param) 
duration = (float)atof(param);

Object *obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());

if (obj && obj->ObjectType() == OT_MOB)
{
MOB *mob = (MOB*)obj;
mob->SendAggroRelationship(this);
mob->FleeTarget(this, duration);
success = true;
}
else
{
SendVaMessage("Unable to access selected mob - could not find mob ID");
}

return (success);
}
*/

bool Player::HandleNavChangeRequest(char *param, int option)
{
	bool success = false;
	bool change = false;
	bool update_done = false;
	char queryString[256];
	ObjectManager *om = GetObjectManager();

	Object *obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
	float value = 0;

	if (param) value = (float)atof(param);

	if (obj && (obj->ObjectType() == OT_NAV || obj->ObjectType() == OT_DECO 
		|| obj->ObjectType() == OT_PLANET || obj->ObjectType() == OT_CAPSHIP 
		|| obj->ObjectType() == OT_STATION) )
	{
		sql_connection_c connection( "net7", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
		sql_query_c NavUpdate(&connection);
		StaticMap *s = (StaticMap*)obj;

		switch (option)
		{
		case 1: //signature
			if (value > 1999)
			{
				SendVaMessageC(12, "Setting %s signature to %.2f (Commit to DB)", obj->Name(), value);
				LogMessage("%s changed %s sig to: %.2f\n", AccountUsername(), obj->Name(), value);
				s->SetSignature(value);
				//commit to DB
				_snprintf(queryString, sizeof(queryString),
					"UPDATE `sector_nav_points` SET `signature` = '%f' WHERE `sector_object_id` = '%d'", 
					value, s->GetDatabaseUID());
				change = true;
			}
			else
			{
				SendVaMessage("signature must be 2000 or greater");
			}
			break;

		case 2: //server radius
			if (value == 0)
			{
				//determine range to target
				value = obj->RangeFrom(Position(), true);
			}
			SendVaMessageC(12, "Setting %s server radius to %.2f (Commit to DB)", obj->Name(), value);
			LogMessage("%s changed %s radius to: %.2f\n", AccountUsername(), obj->Name(), value);
			s->SetObjectRadius(value);
			_snprintf(queryString, sizeof(queryString),
				"UPDATE `sector_nav_points` SET `object_radius_patch` = '%f' WHERE `sector_object_id` = '%d'", 
				value, s->GetDatabaseUID());
			change = true;
			break;

		default: 
			break;
		}

		success = true;
		if (change == true)
		{
			NavUpdate.run_query(queryString);
		}
	}
	else
	{
		SendVaMessage("No valid nav object selected or invalid value.");
	}

	return (success);
}

bool Player::HandleChangeFieldRequest(char *param, int option)
{
	bool success = false;
	bool change = false;
	bool update_done = false;
	char queryString[256];
	ObjectManager *om = GetObjectManager();

	Object *obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
	int value = atoi(param);

	if (value == -1)
	{
		SendVaMessage("Ore Field Commands\n");
		SendVaMessage("/fradius <float>: Set field radius\n");
		SendVaMessage("/ftype <type#>: Sets field's type (1 to 5)\n");
		SendVaMessage("/flevel <level#>: Sets field's level (1 to 9)\n");
		SendVaMessage("/fcount <count#>: How many spawn in field\n");
		SendVaMessage("/faddasteroidtype: add ore type to spawn. Not working right now\n");
		SendVaMessage("/faddoretofield <id#>: add ore to field\n");
		SendVaMessage("/fdelorefromfield <id#>: del ore from field\n");
		SendVaMessage("/faddoretosector <id#>: add ore to sector\n");
		SendVaMessage("/fdelorefromsector <id#>: del ore from sector\n");
		return true;
	}

	if (obj && value != 0 && obj->ObjectType() == OT_FIELD)
	{
		sql_connection_c connection( "net7", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
		sql_query_c FieldUpdate(&connection);
		Field *f = (Field*)obj;

		switch (option)
		{
		case 1: //radius
			if (value > 1999)
			{
				f->SetFieldRadius((float)value);
				//commit to DB & re-pop
				_snprintf(queryString, sizeof(queryString), 
					"UPDATE `sector_objects_harvestable` SET `max_field_radius` = '%d' WHERE `resource_id` = '%d'", 
					value, f->GetDatabaseUID());
				change = true;
			}
			else
			{
				SendVaMessage("field radius must be 2000 or greater");
			}
			break;

		case 2: //spread type
			if (value > -1 && value < 6)
			{
				f->SetFieldType(value);
				//commit to DB & re-pop
				_snprintf(queryString, sizeof(queryString),
					"UPDATE `sector_objects_harvestable` SET `field` = '%d' WHERE `resource_id` = '%d'", 
					value, f->GetDatabaseUID());
				change = true;
			}
			else
			{
				SendVaMessage("level must be between 0 and 5");
			}
			break;
		case 3: //level
			if (value > 0 && value < 9)
			{
				f->SetLevel(value);
				//commit to DB & re-pop
				_snprintf(queryString, sizeof(queryString), 
					"UPDATE `sector_objects_harvestable` SET `level` = '%d' WHERE `resource_id` = '%d'", 
					value, f->GetDatabaseUID());
				change = true;
			}
			else
			{
				SendVaMessage("level must be between 1 and 8");
			}
			break;

		case 4: //count
			if (value != f->FieldCount())
			{
				SendVaMessage("New asteroid field count: cannot display new changes until server re-start");
				//commit to DB & re-pop
				_snprintf(queryString, sizeof(queryString),
					"UPDATE `sector_objects_harvestable` SET `res_count` = '%d' WHERE `resource_id` = '%d'", 
					value, f->GetDatabaseUID());
				change = true;
			}
			break;

		case 5: //add type
			//first check basset is valid
			break;

		case 6: //add item_id choice to this field
			//1. check item is valid
			if (value > 0 && value < 10000)
			{
				f->AddItemID(value, 0.0f);
				//need to make a new entry
				sql_result_c result;
				_snprintf(queryString, sizeof(queryString), 
					"SELECT * FROM sector_objects_harvestable_oretypes WHERE resource_id = '%d' AND additional_ore_item_id = '%d'", 
					f->GetDatabaseUID(), value);
				FieldUpdate.execute(queryString);
				FieldUpdate.store(&result);

				if (result.n_rows() == 0)
				{
					sql_query ItemBuilder;
					ItemBuilder.Clear();
					ItemBuilder.SetTable("sector_objects_harvestable_oretypes");

					ItemBuilder.AddData("resource_id", f->GetDatabaseUID());
					ItemBuilder.AddData("additional_ore_item_id", value);
					ItemBuilder.AddData("frequency", 0);

					if (!FieldUpdate.run_query(ItemBuilder.CreateQuery()))
					{
						SendVaMessage("Error while comitting new item to DB.");
					}
				}
				change = true;
				update_done = true;
			}
			else
			{
				SendVaMessage("invalid item id: %d", value);
			}
			break;

		case 7: //remove base ore -  - so it doesn't drops in this zone
			//1. check item is valid
			if (value > 0 && value < 10000)
			{				
				ItemBase *base_ore = g_ItemBaseMgr->GetItem(value);
				//delete from DB
				if (base_ore)
				{
					_snprintf(queryString, sizeof(queryString), 
						"DELETE FROM sector_objects_harvestable_oretypes WHERE resource_id = '%d' AND additional_ore_item_id = '%d'", 
						f->GetDatabaseUID(), value);
					change = true;
					SendVaMessageC(13, "%s removed from field's ore list.", base_ore->Name());
				}
			}
			else
			{
				SendVaMessage("invalid item id: %d", value);
			}
			break;

		case 8: //add base ore to 'base_ore_list' - so it drops in this zone
			//1. check item is valid
			if (value > 0 && value < 10000)
			{				
				ItemBase *base_ore = g_ItemBaseMgr->GetItem(value);
				//is this ore already in the DB?
				sql_result_c result;
				_snprintf(queryString, sizeof(queryString), 
					"SELECT * FROM base_ore_list WHERE sector_id = '%d' AND item_id = '%d'", 
					PlayerIndex()->GetSectorNum(), value);
				FieldUpdate.execute(queryString);
				FieldUpdate.store(&result);

				if (result.n_rows() == 0 && base_ore)
				{
					sql_query ItemBuilder;
					ItemBuilder.Clear();
					ItemBuilder.SetTable("base_ore_list");

					ItemBuilder.AddData("name", base_ore->Name());
					ItemBuilder.AddData("item_id", value);
					ItemBuilder.AddData("frequency", 0);
					ItemBuilder.AddData("sector_id", (long)PlayerIndex()->GetSectorNum());

					if (!FieldUpdate.run_query(ItemBuilder.CreateQuery()))
					{
						SendVaMessage("Error while comitting new item to DB.");
					}
					else
					{
						SendVaMessageC(13, "Added %s to base ores for sector %s.", base_ore->Name(), PlayerIndex()->GetSectorName());
					}
				}
				change = true;
				update_done = true;
			}
			else
			{
				SendVaMessage("invalid item id: %d", value);
			}
			break;

		case 9: //remove base ore -  - so it doesn't drops in this zone
			//1. check item is valid
			if (value > 0 && value < 10000)
			{				
				ItemBase *base_ore = g_ItemBaseMgr->GetItem(value);
				//delete from DB
				if (base_ore)
				{
					_snprintf(queryString, sizeof(queryString), 
						"DELETE FROM base_ore_list WHERE sector_id = '%d' AND item_id = '%d'", 
						PlayerIndex()->GetSectorNum(), value);
					change = true;
					SendVaMessageC(13, "%s removed from Sector's Base Ore list.", base_ore->Name());
				}
			}
			else
			{
				SendVaMessage("invalid item id: %d", value);
			}
			break;

		default: //
			break;
		}

		if (change == true)
		{
			if (!update_done) 
			{
				FieldUpdate.run_query(queryString);
			}
			//now blank & re-pop field
			long scan_range = ShipIndex()->CurrentStats.GetScanRange();
			ShipIndex()->CurrentStats.SetScanRange(10); //set ship almost blind
			CheckObjectRanges();

			//now rebuild field
			f->PopulateField(false, true);

			ShipIndex()->CurrentStats.SetScanRange(scan_range);
			ResetRangeLists();
			CheckObjectRanges();
		}

		success = true;
	}
	else
	{
		SendVaMessage("No valid asteroid field selected or invalid value.");
	}

	return (success);
}

bool Player::HandlePanRequest(char *param, int axis)
{
	bool success = false;

	Object *obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());
	float value = (float)atof(param);

	if (obj && value != 0.0f)
	{
		switch (axis)
		{
		case 1:
			obj->MovePosition(value,0,0);
			break;

		case 2:
			obj->MovePosition(0,value,0);
			break;

		case 3:
			obj->MovePosition(0,0,value);
			break;

		default:
			break;
		}

		obj->SendLocationAndSpeed(true);
		success = true;
	}
	else
	{
		SendVaMessage("Unable to move selected object - move amount: %.2f", value);
	}

	return (success);
}

bool Player::HandleRotateRequest(char *param, int axis)
{
	bool success = false;

	Object *obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());
	float value = (float)atof(param);

	if (obj && value != 0.0f)
	{
		value = value * PI / 180.0f;

		float *ori = obj->Orientation();

		if (ori[0] == 0 && ori[1] == 0 && ori[2] == 0 && ori[3] == 0)
		{
			obj->SetOrientation(0.0f,0.0f,0.0f,1.0f);
			obj->SetHeading();
			obj->LevelOut();
			obj->LevelOrientation();
		}
		else
		{
			obj->SetHeading();
		}

		switch (axis)
		{
		case 1:
			obj->Rotate(value,0,0);
			break;

		case 2:
			obj->Rotate(0,value,0);
			break;

		case 3:
			obj->Rotate(0,0,value);
			break;

		default:
			break;
		}

		obj->SendLocationAndSpeed(true);
		success = true;

		if (obj->ObjectType() == OT_FIELD)
		{
			Field *f = (Field*)obj;
			//issue field update
			long scan_range = ShipIndex()->CurrentStats.GetScanRange();
			ShipIndex()->CurrentStats.SetScanRange(10); //set ship almost blind
			CheckObjectRanges();

			//now rebuild field
			f->PopulateField(false, true);

			ShipIndex()->CurrentStats.SetScanRange(scan_range);
			ResetRangeLists();
			CheckObjectRanges();
		}
	}
	else
	{
		SendVaMessage("Unable to move selected object - move amount: %.2f", value);
	}

	return (success);
}

bool Player::HandleLevelOutRequest(long Target)
{
	bool success = false;

	Object *obj = GetObjectManager()->GetObjectFromID(Target);

	if (obj)
	{
		obj->SetHeading();
		obj->LevelOut();
		obj->LevelOrientation();
		//leave object square on the Z plane
		obj->SendLocationAndSpeed(true);
		success = true;
		SendVaMessage("%s levelled out", obj->Name());
	}
	else
	{
		SendVaMessage("Unable to access selected object - could not find object ID");
	}

	return (success);
}

bool Player::HandleCommitRequest(long Target)
{
	bool success = false;
	char queryString[256];

	Object *obj = GetObjectManager()->GetObjectFromID(Target);

	if (obj)
	{
		float *ori = obj->Orientation();
		SendVaMessage("Attempting to commit %s to database.", obj->Name());
		sql_connection_c connection( "net7", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
		sql_query_c PositionUpdate(&connection);

		_snprintf(queryString, sizeof(queryString), 
			"UPDATE `sector_objects` SET `position_x` = '%.2f' WHERE `sector_object_id` = '%d'", 
			obj->PosX(), obj->GetDatabaseUID());
		PositionUpdate.run_query(queryString);
		_snprintf(queryString, sizeof(queryString), 
			"UPDATE `sector_objects` SET `position_y` = '%.2f' WHERE `sector_object_id` = '%d'", 
			obj->PosY(), obj->GetDatabaseUID());
		PositionUpdate.run_query(queryString);
		_snprintf(queryString, sizeof(queryString), 
			"UPDATE `sector_objects` SET `position_z` = '%.2f' WHERE `sector_object_id` = '%d'", 
			obj->PosZ(), obj->GetDatabaseUID());
		PositionUpdate.run_query(queryString);
		_snprintf(queryString, sizeof(queryString), 
			"UPDATE `sector_objects` SET `orientation_u` = '%.6f' WHERE `sector_object_id` = '%d'", 
			ori[0], obj->GetDatabaseUID());
		PositionUpdate.run_query(queryString);
		_snprintf(queryString, sizeof(queryString), 
			"UPDATE `sector_objects` SET `orientation_v` = '%.6f' WHERE `sector_object_id` = '%d'", 
			ori[1], obj->GetDatabaseUID());
		PositionUpdate.run_query(queryString);
		_snprintf(queryString, sizeof(queryString), 
			"UPDATE `sector_objects` SET `orientation_w` = '%.6f' WHERE `sector_object_id` = '%d'", 
			ori[2], obj->GetDatabaseUID());
		PositionUpdate.run_query(queryString);
		_snprintf(queryString, sizeof(queryString), 
			"UPDATE `sector_objects` SET `orientation_z` = '%.6f' WHERE `sector_object_id` = '%d'", 
			ori[3], obj->GetDatabaseUID());
		PositionUpdate.run_query(queryString);
		_snprintf(queryString, sizeof(queryString), 
			"UPDATE `sector_objects` SET `scale` = '%.4f' WHERE `sector_object_id` = '%d'", 
			obj->Scale(), obj->GetDatabaseUID());
		PositionUpdate.run_query(queryString);

		//store specialist information
		switch (obj->ObjectType())
		{
		case OT_PLANET:
			_snprintf(queryString, sizeof(queryString), 
				"UPDATE `sector_objects_planets` SET `rotate_rate` = '%f' WHERE `planet_id` = '%d'", 
				obj->Spin(), obj->GetDatabaseUID());
			PositionUpdate.run_query(queryString);
			_snprintf(queryString, sizeof(queryString), 
				"UPDATE `sector_objects_planets` SET `tilt_angle` = '%f' WHERE `planet_id` = '%d'", 
				obj->Tilt(), obj->GetDatabaseUID());
			PositionUpdate.run_query(queryString);
			break;
		default:
			break;
		} ;

		SendVaMessage("Object %s committed to Net7 Database.", obj->Name());
		LogMessage("%s just committed changes to object %s %d\n", AccountUsername(), obj->Name(), obj->GetDatabaseUID());
	}
	else
	{
		SendVaMessage("Unable to commit selected object - could not find object ID");
	}

	return (success);
}


bool Player::HandleEulerOrientationRequest(char *orientation)
{
	bool success = false;
	float o1, o2, o3;
	char *next_token;
	char *a = strtok_s(orientation, ",", &next_token);
	char *b = strtok_s(NULL, ",", &next_token);
	char *c = strtok_s(NULL, ",", &next_token);

	o1 = o2 = o3 = 0.0f;

	if (a == NULL)//allow incomplete args
	{
		SendVaMessage("/oeuler: syntax: heading,alt,bank");
		SendVaMessage("/oeuler: all parameters float type");
	}
	else
	{
		o1 = (float)atof(a);

		if (b != NULL)
		{
			o2 = (float)atof(b);
		}
		if (c != NULL)
		{
			o3 = (float)atof(c);
		}

		if (o1 == 180.0f) o1 = o1 + 0.5f;
		if (o2 == 180.0f) o2 = o2 + 0.5f;
		if (o3 == 180.0f) o3 = o3 + 0.5f;

		o1 = o1 / 180.0f * PI;
		o2 = o2 / 180.0f * PI;
		o3 = o3 / 180.0f * PI;

		Object *obj;

		obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());

		if (!obj)
		{
			obj = m_CurrentDecoObj;
		}

		if (obj)
		{
			//New orientation info:
			obj->SetEulerOrientation(o1,o2,o3);

			SendVaMessage("Object name: %s", obj->Name());
			SendVaMessage("Setting Euler orientation to %.2f,%.2f,%.2f",o1*180.0f/PI,o2*180.0f/PI,o3*180.0f/PI);
			float *vector;
			vector = obj->Orientation();
			SendVaMessage("Setting Quaternion orientation to %.4f,%.4f,%.4f,%.4f",vector[0],vector[1],vector[2],vector[3]);

			obj->SendPosition(this);

			success = true;
		}
		else
		{
			SendVaMessage("Unable to access selected object - could not find object ID");
		}
	}

	return (success);
}

bool Player::HandleMobCreateRequest(char *param)
{
	char *pch_parse, *name = 0, *next_token;
	short level = 0;
	long mob_type = 0;
	long mob_count = 0;
	bool direct_basset = false;
	bool directional = false;
	float velocity = 100.0f;
	float turn = 0.2f;
	float xoffset = 300.0f;

	//are we trying to create a mob too close to a station?
	Object *obj = NearestNav();

	if (obj)
	{
		if (obj->ObjectType() == OT_STATION && obj->RangeFrom(Position()) < 5000.0f)
		{
			SendVaMessage("You're trying to create a MOB too close to a station. Move to at least 5K away.");
			return false;
		}
	}

#ifdef SQL_ENABLE
	/*if (AdminLevel() < 90)
	{
		SendVaMessage("createmob has been suspended temporarily - will be replaced by createspawn very soon\n");
		return true;
	}*/
#endif

	mob_count = g_ServerMgr->MOBList()->GetMOBCount();

	pch_parse = strtok_s(param, " ", &next_token);

	if (!pch_parse)
	{
		return false;
	}

	char msg[1000];
	sprintf(msg, "%s has used the createmob command (%s)", Name(), param);
	g_PlayerMgr->ChatSendChannel(GameID(), "GM", msg);
	g_PlayerMgr->ChatSendChannel(GameID(), "Dev", msg);
	g_PlayerMgr->ChatSendChannel(GameID(), "Beta", msg);


	if (pch_parse && directional == false)
	{
		mob_type = (long)atoi(pch_parse);
		pch_parse = strtok_s(NULL, " ", &next_token);
		if (pch_parse)
		{
			level = atoi(pch_parse);
			name = strtok_s(NULL, " ", &next_token);
		}
	}

	if (mob_type > -1 && mob_type < mob_count)
	{
		MOB *mob = (MOB*)GetObjectManager()->AddNewObject(OT_MOB); //MOB creation
		MOBData *mob_data = g_ServerMgr->MOBList()->GetMOBData(mob_type);

		if (mob && mob_data)
		{
			name = mob_data->m_Name;
			SendVaMessage("Creating %s at level %d", name, mob_data->m_Level);

			mob->SetName(name);
			mob->SetPosition(Position());
			mob->MovePosition(xoffset, 0.0f, -200.0f);
			mob->SetActive(true);
			mob->SetRespawnTick(0);
			mob->SetHostileTo(OT_PLAYER);
			mob->SetVelocity(velocity);
			mob->Turn(turn);
			mob->SetDefaultStats(turn, DRIFT, velocity, 50);
			mob->SetMOBType((short)mob_type);
			mob->SetUpdateRate(50);
			mob->SetBehaviour(DRIFT);
			mob->AddBehaviourPosition(Position());

			return true;
		}
	}

	SendVaMessage("Create Mob: /createmob <MOB type>0..%d <level>1..66 <name>", (mob_count - 1));
	return false;
}

bool Player::HandleObjCreateRequest(char *param)
{
	char *pch_level;
	short level = 0;
	pch_level = strchr(param, ' ');

	if (strcmp(param, "ON") == 0)
	{
		SendVaMessage("Resource Creation ACTIVATED");
		SendVaMessage("Create Resource: /create <type>0..12 <level>1..9");
		g_ServerMgr->m_AllowCreate = true;
		return true;
	}
	else if (strcmp(param, "XML") == 0)
	{
		SendVaMessage("XML output on");
		g_ServerMgr->m_DumpXML = true;
		return true;
	}
	else if (strncmp(param, "F", 1) == 0)
	{
		char *pch_lev = 0;
		char *pch_number = 0;
		char *pch_radius = 0;
		int type = 0;
		int number = 0;
		int radius = 0;

		if (pch_level) pch_lev = strchr(pch_level + 1, ' ');
		if (pch_lev) pch_number = strchr(pch_lev + 1, ' ');
		if (pch_number) pch_radius = strchr(pch_number + 1, ' ');

		if (pch_lev && pch_level && pch_number && pch_radius)
		{
			type = atoi(pch_level + 1);
			level = atoi(pch_lev + 1);
			number = atoi(pch_number + 1);
			radius = atoi(pch_radius + 1);
		}

		if (radius < 3000.0f || number < 1 || level > 8 || type > 9 || level < 1 || type < 1)
		{
			SendVaMessage("Create Asteroid field: /create F <type>1..9 <level>1..8 <count>1.. <radius>1.. ");
			return false;
		}
		else if (AdminLevel() >= 80)      // GM to Admin
		{
			Object *obj = GetObjectManager()->AddNewObject(OT_FIELD); //complete creation of field
			obj->SetFieldRadius((float)radius);
			obj->SetLevel(level);
			obj->SetFieldCount(number);
			obj->SetFieldType(type);
			obj->SetPosition(Position());
			obj->PopulateField(true); // issue field
			SendVaMessage("Asteroid field created. Level:%d Count:%d Radius:%d", level, number, radius);
			GetObjectManager()->DisplayDynamicObjects(this, true); //update display for all roids.
			return true;
		}
		else
		{
			SendVaMessage("%s, you can't create an asteroid field ... yet.", Name());
			return false;
		}
	}
	else if (strncmp(param, "M", 1) == 0)
	{
		Object *obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());
		if (obj)
		{
			//p->m_SectorMgr->GetMobPosition(mob, &position[0]);
			//mob->position_info.Position
			//m_SectorMgr->GetMobPosition(obj, &position[0]);



			/*position[0] = obj->position_info.Position[0];
			position[1] = obj->position_info.Position[1];
			position[2] = obj->position_info.Position[2];*/
			//m_SectorMgr->AddNewObject(this, 9, 7, &position[0]);
		}
	}
	else if (g_ServerMgr->m_AllowCreate == false)
	{
		SendVaMessage("Create Resource: Unable to create resource");
		return false;
	}

	if (!pch_level)
	{
		SendVaMessage("Create Resource: /create <type>0..12 <level>1..9");
		return false;
	}

	long object_type = (long)atoi(param);

	level = atoi(pch_level + 1);

	if ((level > 0 && level < 10) && (object_type > -1 && object_type < 13))
	{
		Object *obj = GetObjectManager()->AddNewObject(OT_RESOURCE);
		obj->SetTypeAndName((short)object_type + 0x71E);
		obj->SetLevel(level);
		obj->SetPosition(Position());
		obj->ResetResource(); //populate with items

		return true;
	}
	else
	{
		SendVaMessage("Create Resource: /create <type>0..12 <level>1..9");
		return false;
	}
}

void Player::ChangeSectorID(long SectorID)
{
	PlayerIndex()->SetSectorNum(SectorID);
	//Add Player to new sector
	SectorManager *sm = g_ServerMgr->GetSectorManager(SectorID);
    sm->AddPlayerToSectorList(this);
}

bool Player::HandleFetchRequest()
{
	bool success = false;
	Object *obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());

	if (obj != NULL)
	{
		if (obj->ObjectType() != OT_PLAYER && obj->ObjectType() != OT_MOB)
		{
			obj->SetPosition(Position());
			SendVaMessage("Object name: %s", obj->Name());
			obj->SendPosition(this); //TODO: send new position to everyone
		}
		else
		{
			SendVaMessage("Unable to change position of Player or MOB");
		}
	}
	else
	{
		SendVaMessage("Unable to access selected object - could not find object ID");
	}

	return (success);
}

bool Player::HandleObjectHijack()
{
	Object *obj = NULL;
	ObjectManager *obj_manager = GetObjectManager();

	if (obj_manager)
	{
		obj = obj_manager->GetObjectFromID(ShipIndex()->GetTargetGameID());
	}

	if (obj && HijackObject(obj))
	{
		//try a different approach with this.
		//lets try just locking the position of the hijacked object onto the player's ship
		//and not showing the ship to anyone else
		SendVaMessage("You have been successfully assigned to control '%s'.", obj->Name());
		LogMessage("HIJACK occurred. Account name '%s'.\n", AccountUsername());        
		return true;
	}
	else
	{
		SendVaMessage("Unable to hijack object with GameID %d.\n", ShipIndex()->GetTargetGameID());
		return false;
	}
}

void Player::HandleReleaseHijack()
{
	if (GetObjectManager() && Hijackee() > 0)
	{
		Object *obj = GetObjectManager()->GetObjectFromID(Hijackee());
		if (obj)
		{
			SendVaMessage("You have been unassigned from control of '%s'.", obj->Name());
			obj->SetVelocity(0);
			obj->SetPlayerHijack(0);   
			obj->ResetStaticPacket();
		}
		SetHijackee(0);  
	}
}

void Player::HandleFireMOBWeapon()
{
	ObjectManager *om = GetObjectManager();

	if (om && Hijackee() > 0)
	{
		Object *obj = om->GetObjectFromID(Hijackee());
		Object *target = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
		if (obj && target && 
			(target->ObjectType() == OT_PLAYER || target->ObjectType() == OT_MOB) &&
			obj->ObjectType() == OT_MOB)
		{
			MOB *mob = (MOB*) obj;

			//fire MOB weapon at target
			mob->FireWeapon(target);
		}
	}
}

bool Player::HandleGotoRequest()
{
	bool success = false;
	Object *obj;
	bool admin = false;

	//only allow admins to jump to resources
	if (AdminLevel() >= GM)      // GM to Admin
	{
		admin = true;
	}

	if (admin == false)
	{
		SendVaMessage("/goto has been disabled");
		return (false);
	}

	if (GetObjectManager())
	{
		obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());

		if (obj != NULL)
		{
			SetPosition(obj->Position());
			SendLocationAndSpeed(true);
			UpdateVerbs();
			CheckObjectRanges();
			CheckNavs();
			success = true;
		}
	}

	return (success);
}

void Player::OpenStargate(long object_id)
{
	SendActivateNextRenderState(object_id, 1);
}

void Player::CloseStargate(long object_id)
{
	SendActivateNextRenderState(object_id, 3);
}

bool Player::HandleRenderStateRequest()
{
	bool success = false;
	Object *obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());

	if (obj)
	{
		SendVaMessage("Object '%s'", obj->Name());
		SendVaMessage("Current Render State   = 0x%X", obj->RenderState());
		//SendVaMessage("Allowable Render State = 0x%X", obj->allowable_render_states);
		success = true;
	}
	else
	{
		SendVaMessage("Unable to access selected object - could not find object ID");
	}

	return (success);
}

bool Player::HandleScaleRequest(char *param)
{
	bool success = false;
	if (GetSectorManager() && (ShipIndex()->GetTargetGameID() > 0 || m_CurrentDecoObj))
	{
		float scale = (float) atof(param);
		Object *obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());

		if (!obj) obj = m_CurrentDecoObj;

		if (obj)
		{
			if (scale == 0.0)
			{
				// Display current scale
				SendVaMessage("Scale of %s is currently %.2f", obj->Name(), obj->Scale());
			}
			else
			{
				// Set new scale
				SendVaMessage("Changing scale from %g to %g", obj->Scale(), scale);
				obj->SetScale(scale);
				long target_id = obj->GameID();
				if (obj->ObjectType() == OT_RESOURCE)
				{
					// Remove the Object
					obj->Remove();
					SendPacketCache();
					Sleep(200);
					// Recreate the Object
					obj->SendObject(this);
				}
				else
				{
					RemoveObject(target_id);
					SendPacketCache();
					Sleep(200);
					obj->SendObject(this);
				}
			}
		}
	}

	return (success);
}

bool Player::HandleSpinRequest(char *param)
{
	bool success = false;
	if (ShipIndex()->GetTargetGameID() > 0)
	{
		float spin = (float) atof(param);
		Object *obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());

		if (obj && obj->ObjectType() == OT_PLANET && (spin > 0 && spin < 1001))
		{
			float old_spin = (obj->Spin() - 0.000001f)/0.00001f;
			SendVaMessage("Changing spin from %f to %f", old_spin, spin);
			// Set new spin
			spin = 0.000001f + spin*0.00001f;
			obj->SetSpin(spin);
			long target_id = obj->GameID();

			RemoveObject(target_id);
			SendPacketCache();
			Sleep(200);
			obj->SendObject(this);
		}
		else
		{
			SendVaMessage("/spin only works on 'Planet' type objects, and must be 1 to 1000");
		}
	}

	return (success);
}

bool Player::HandleTiltRequest(char *param)
{
	bool success = false;
	if (GetSectorManager() && (ShipIndex()->GetTargetGameID() > 0))
	{
		float tilt = (float) atof(param);
		Object *obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());

		if (obj && obj->ObjectType() == OT_PLANET && (tilt >= 0 && tilt <= 90))
		{
			// Set new tilt
			SendVaMessage("Changing tilt from %f to %f", (obj->Tilt()*180.0f/PI), tilt);
			tilt = tilt * PI / 180.0f;
			obj->SetTilt(tilt);
			long target_id = obj->GameID();

			RemoveObject(target_id);
			SendPacketCache();
			Sleep(200);
			obj->SendObject(this);
		}
		else
		{
			SendVaMessage("/tilt only works on 'Planet' type objects, and must be 0 to 90");
		}
	}

	return (success);
}

bool Player::HandleBassetRequest(char *param)
{
	bool success = false;
	if (m_CurrentDecoObj)
	{
		char msg_buffer[128];
		long basset = atoi(param);
		Object *obj = m_CurrentDecoObj;
		AssetData *asset = g_ServerMgr->AssetList()->GetAssetData(basset);

		if (asset && asset->m_Name)
		{
			_snprintf(msg_buffer, sizeof(msg_buffer), "%d:%s", basset, asset->m_Name);
			SendPushMessage(msg_buffer, "MessageLine", 3000, 3);
		}

		// Set new basset
		SendVaMessage("Changing basset from %d to %d", obj->BaseAsset(), basset);

		obj->SetBasset((short)basset);
		// Remove the Object
		obj->Remove();
		//ensure remove and re-create aren't in same packet stream.
		SendPacketCache();
		Sleep(200);
		// Recreate the Object
		obj->SendObject(this);
		SendPacketCache();
		Sleep(200);
		obj->Remove();
		//ensure remove and re-create aren't in same packet stream.
		SendPacketCache();
		Sleep(200);
		// Recreate the Object
		obj->SendObject(this);
	}

	return (success);
}

bool Player::SendLoungeNPC(long StationID)
{
#ifdef USE_MYSQL_STATIONS
	struct StationLounge LoungeData;
	unsigned char bufferd[10000];
	unsigned char *buffer = bufferd;
	int Size, x;

	memset(buffer, 0, sizeof(buffer));
	memset(&LoungeData, 0, sizeof(LoungeData));

	StationTemplate * Stn = g_ServerMgr->m_StationMgr.GetStation(StationID);

	if (!Stn)			// Station not found?
	{
		LogMessage("Station %d not on SQL list\n", StationID);
		return false;
	}

	LoungeData.Station.StationType = Stn->Type;

	NPCTemplate * NPCs = NULL;
	x=0;
	for(NPCList::const_iterator npc = Stn->NPCs.begin(); npc < Stn->NPCs.end(); ++npc)
	{
		NPCs = g_ServerMgr->m_StationMgr.GetNPC(*npc);//npc->second;
		LoungeData.NPC[x].BoothType = NPCs->Booth;
		LoungeData.NPC[x].Location = NPCs->Location;
		LoungeData.NPC[x].NPCID = NPCs->StarbaseID;
		LoungeData.NPC[x].RoomNumber = NPCs->Room;
		memcpy(&LoungeData.NPC[x].Avatar,&NPCs->Avatar,sizeof(AvatarData));
		x++;
	}
	LoungeData.NumNPCs = x;

	RoomTemplate * Rooms;
	x=0;
	for(RoomList::const_iterator room = Stn->Rooms.begin(); room < Stn->Rooms.end(); ++room)
	{
		Rooms = g_ServerMgr->m_StationMgr.GetRoom(*room);

		LoungeData.Rooms[x].RoomNumber = Rooms->Index;
		LoungeData.Rooms[x].RoomStyle = Rooms->Style;
		
		LoungeData.Rooms[x].FogRed = (float) Rooms->FogRed / 255.0f;
		LoungeData.Rooms[x].FogGreen = (float) Rooms->FogGreen / 255.0f;
		LoungeData.Rooms[x].FogBlue = (float) Rooms->FogBlue / 255.0f;

		LoungeData.Rooms[x].FogFar = Rooms->FogFar;
		LoungeData.Rooms[x].FogNear = Rooms->FogNear;
		x++;
	}
	LoungeData.Station.RoomNumber = x;

	TermTemplate * Terms;
	x=0;
	for(TermList::const_iterator term = Stn->Terms.begin(); term < Stn->Terms.end(); ++term)
	{
		Terms = g_ServerMgr->m_StationMgr.GetTerminal(*term);
		LoungeData.Terms[x].TermType = Terms->Type;
		LoungeData.Terms[x].Location = Terms->Location;
		LoungeData.Terms[x].RoomNumber = Terms->Room;
		x++;
	}
	LoungeData.NumTerms = x;

	// Build Packet

	memcpy(buffer,&LoungeData.Station,sizeof(LoungeData.Station));
	buffer+=sizeof(LoungeData.Station);

	for(x=0;x<LoungeData.Station.RoomNumber;x++) 
	{
		memcpy(buffer,&LoungeData.Rooms[x],sizeof(LoungeData.Rooms[x]));
		buffer+=sizeof(LoungeData.Rooms[x]);
	}

	*((int *) buffer) = LoungeData.NumTerms; buffer+=4;

	for(x=0;x<LoungeData.NumTerms;x++) 
	{
		memcpy(buffer,&LoungeData.Terms[x], sizeof(LoungeData.Terms[x]));
		buffer+=sizeof(LoungeData.Terms[x]);
	}

	*((int *) buffer) = LoungeData.NumNPCs; buffer+=4;

	for(x=0;x<LoungeData.NumNPCs;x++) 
	{
		memcpy(buffer,&LoungeData.NPC[x], sizeof(LoungeData.NPC[x]));
		buffer+=sizeof(LoungeData.NPC[x]);
	}

	Size = buffer - &bufferd[0];

	SendOpcode(ENB_OPCODE_0052_LOUNGE_NPC, (unsigned char *) bufferd, Size);

	//DumpBuffer(bufferd, Size);

	return true;
#else
	return false;
#endif
}

//Not too sure on the data structure of this packet
bool Player::HandleRenderStateInitRequest(char *param)
{
	bool success = false;
	Object *obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());
	long render_state = atol(param);

	if (obj != NULL)
	{
		SendVaMessage("Object '%s'", obj->Name());
		SendVaMessage("Current Render State   = 0x%X", obj->RenderState());
		SendVaMessage("Allowable Render State = 0x%X", 3);
		SendVaMessage("New Render State       = 0x%X", render_state);
		SendInitRenderState(ShipIndex()->GetTargetGameID(), render_state);
		obj->SetRenderState((short)render_state);
		success = true;
	}
	else
	{
		SendVaMessage("Unable to access selected object - could not find object ID");
	}
	return (success);
}

bool Player::HandleRenderStateActivateRequest(char *param)
{
	bool success = false;
	Object *obj;
	long render_state = atol(param);
	obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());

	if (obj != NULL)
	{
		SendVaMessage("Object '%s'", obj->Name());
		SendVaMessage("Activate Render State: 2 = open gate half way");
		SendVaMessage("Current Render State   = 0x%X", obj->RenderState());
		SendVaMessage("Allowable Render State = 0x%X", 3);
		SendVaMessage("New Render State       = 0x%X", render_state);
		SendActivateRenderState(ShipIndex()->GetTargetGameID(), render_state);
		obj->SetRenderState((short)render_state);
		success = true;
	}
	else
	{
		SendVaMessage("Unable to access selected object - could not find object ID");
	}
	return (success);
}

bool Player::HandleRenderStateActivateNextRequest(char * param)
{
	bool success = false;
	Object *obj;
	long render_state = atol(param);
	obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());

	if (obj != NULL)
	{
		SendVaMessage("Object '%s'", obj->Name());
		SendVaMessage("Activate Next Render State: 1 = Gate opens fully 3 = gate closes");
		SendVaMessage("Current Render State   = 0x%X", obj->RenderState());
		SendVaMessage("Allowable Render State = 0x%X", 3);
		SendVaMessage("New Render State       = 0x%X", render_state);
		SendActivateNextRenderState(ShipIndex()->GetTargetGameID(), render_state);
		obj->SetRenderState((short)render_state);
		success = true;
	}
	else
	{
		SendVaMessage("Unable to access selected object - could not find object ID");
	}
	return (success);
}

bool Player::HandleRenderStateDeactivate()
{
	bool success = false;
	Object *obj;
	obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());

	if (obj != NULL)
	{
		SendVaMessage("Object '%s'", obj->Name());
		SendVaMessage("Deactivate Render State");
		SendVaMessage("Current Render State   = 0x%X", obj->RenderState());
		SendVaMessage("Allowable Render State = 0x%X", 3);
		SendVaMessage("New Render State       = 0x%X", 0);
		SendDeactivateRenderState(ShipIndex()->GetTargetGameID());
		obj->SetRenderState(0); //Not sure if I should be changing this here
		success = true;
	}
	else
	{
		SendVaMessage("Unable to access selected object - could not find object ID");
	}
	return (success);
}

void Player::SendStarbaseAvatarChange(Player *p)
{
	if (!p) return;

	StarbaseAvatarChange_S2C change;
	memset(&change, 0, sizeof(change));
	change.AvatarID = p->GameID();
	change.Orient = p->m_Orient;
	change.Position[0] = p->PosX();
	change.Position[1] = p->PosY();
	change.Position[2] = p->PosZ();
	change.ActionFlag = p->ActionFlag();
	change.Room = p->m_Room;

	SendOpcode(ENB_OPCODE_009E_STARBASE_AVATAR_CHANGE, (unsigned char *) &change, sizeof(change));
}

void Player::HandleStarbaseRequest(unsigned char *data)
{
	StarbaseRequest * pkt = (StarbaseRequest *) data;
	
	LogDebug("Starbase Request - PlayerID: %d Action: %d StarBaseID: %d\n",pkt->PlayerID, pkt->Action, pkt->StarbaseID);
	
	NPCTemplate * NPC = NULL;
	SectorManager *sm = GetSectorManager();

	char *professions[] =
	{
		" Warriors",
		"  Traders",
		"Explorers"
	};

	char string[] = 
		"/happy1 Hello! Hello - step right up, Sir. Explorers are welcome here.  How can I help you?\0"
		"\0\2"
		"\0\0\0\0"
		"I would like to trade\0"
		"\1\0\0\0"
		"Nothing today\0";

	memcpy((string+43),professions[Profession()], 9);

	/*printf("Received StarbaseRequest packet, PlayerID=%x, StarbaseID=%d, Action=%d\n",
	pkt->PlayerID, pkt->StarbaseID, pkt->Action);*/

	m_TradeWindow = false;

	switch(pkt->Action)
	{
	case 1: // Exiting the station action
		if (sm)
		{
			FinishAllInstalls();
			m_Gating = true;
			if (m_TradeID != -1)
			{
				CancelTrade();
			}
			//LogMessage("Player '%s' Leaving station.\n", Name());
			// Launch into space!
			sm->LaunchIntoSpace(this);
		}
		break; 
	case 4: // Talk to NPC
		m_StarbaseTargetID = pkt->StarbaseID;
		// Display the standard talk tree unless we have a mission here
		if (CheckMissions(0, 1, m_StarbaseTargetID, TALK_NPC) ||
			CheckForNewMissions(0, 1, m_StarbaseTargetID) )
		{
			return;
		}

		NPC = g_ServerMgr->m_StationMgr.GetNPC(m_StarbaseTargetID);

		// Save our current NPC to make it easier to find
		m_CurrentNPC = NPC;

#ifdef USE_MYSQL_STATIONS
		// Get talk tree for NPC
		// and save to m_CurrentTalkTree
		if (NPC && NPC->NPCInteraction.talk_tree.NumNodes > 0)
		{
			long length = GenerateTalkTree(&NPC->NPCInteraction.talk_tree, 1);

			if (length == 0)
			{
				return;
			}

			// Output first node
			SendOpcode(ENB_OPCODE_0054_TALK_TREE, (unsigned char *) m_TalkTreeBuffer, length);
		}
		else
#endif
		{
			SendOpcode(ENB_OPCODE_0054_TALK_TREE, (unsigned char *) string, sizeof(string));
			m_CurrentTalkTree = (0);
		}

		m_StarbaseTargetID = pkt->StarbaseID;

		break;
	case 6: // Activating Job Terminal
		{
			LogMessage("Job 6\n");
			//see what jobs are available, if any

			u8 *ptr = m_ScratchBuffer;
			SectorManager *sm = GetSectorManager();
			int index = sm->GetJobList(m_ScratchBuffer);
			if (index > 0)
			{
				SendOpcode(ENB_OPCODE_0093_JOB_LIST, ptr, index);
			}

			break;			
		}
	case 7: // Clicking on a job on the job terminal to pull up a job description
		{	
			LogMessage("Job %d\n", pkt->StarbaseID);
			/*  JobDescription structure

			0x00, 0x00, 0x00, 0x00, // JobDescriptionID
			0x00,					// Is this job still available? (0x00 = no, 0x01 = yes)
			0xXX, ........... 0x00, // Variable length null terminated "Title" string
			0xXX, ........... 0x00, // Variable length null terminated "Description" string
			*/

			SectorManager *sm = GetSectorManager();
			int index = sm->GetJobDescription(m_ScratchBuffer, pkt->StarbaseID);

			if (index > 0)
			{
				SendOpcode(ENB_OPCODE_0094_JOB_DESCRIPTION, m_ScratchBuffer, index);
			}

			/*
			PacketBuffer buffer;

			g_ServerMgr->m_Jobs.GetJobDescription(pkt->StarbaseID, &buffer, &g_ServerMgr->m_TokenParser);
			SendOpcode(ENB_OPCODE_0094_JOB_DESCRIPTION, buffer.getBuffer(), buffer.size());

			_Item myItem; 
			memset(&myItem,0,sizeof(_Item)); 

			int itemId = g_ServerMgr->m_Jobs.GetItem(pkt->StarbaseID);

			ItemBase * item = g_ItemBaseMgr->GetItem(itemId);

			if (item)
			{
			myItem.ItemTemplateID = itemId;
			myItem.Price = item->Cost();
			myItem.Quality = 1.0f;
			myItem.Structure = 1.0f;
			myItem.StackCount = 1;

			printf("itemID: %d\n", itemId);


			ItemBase *myItemBase = g_ItemBaseMgr->GetItem(myItem.ItemTemplateID);
			SendItemBase(myItem.ItemTemplateID);

			PlayerIndex()->RewardInv.Item[0].SetData(&myItem);
			SendAuxPlayer();
			}
			*/

			break;
		}

	case 8: // Accept job?
		LogMessage("Accepting Job %d\n", pkt->StarbaseID);
		{
			long job_id = pkt->StarbaseID;
			SendOpcode(ENB_OPCODE_0096_JOB_ACCEPT_REPLY, (u8*)&job_id, sizeof(job_id));
			SectorManager *sm = GetSectorManager();
			if (sm && !sm->AwardJob(this, job_id))
			{
				SendVaMessageC(17,"Job unavailable.");
			}
		}
		break;

	case 9: // Accept job?
		LogMessage("Accepting Job 9\n");
		SendOpcode(ENB_OPCODE_0096_JOB_ACCEPT_REPLY);
		//g_ServerMgr->m_Missions.givePlayerMission(this, 1);			
		break;

	case 10: // Customize avatar
		{
			struct RecustomizeAvatarStart ras;
			for (int i=0;i < 14;i++)
				ras.costs[i] = g_CustomiseAvatarCosts[i];
			ras.playerid = htonl(pkt->PlayerID);
			SendOpcode(ENB_OPCODE_0083_RECUSTOMIZE_AVATAR_START,(unsigned char *)&ras,sizeof(ras));
		}
		break;		
	case 11: // Customize starship
		{			
			struct RecustomizeShipStart rss;
			rss.ship = m_Database.ship_data;
			for (int i=0;i < 12;i++)
				rss.costs[i] = g_CustomiseShipCosts[i];
			rss.playerid = htonl(pkt->PlayerID);
			rss.unknown[0] = rss.unknown[1] = rss.unknown[2] = rss.unknown[3] = 0;
			SendOpcode(ENB_OPCODE_0081_RECUSTOMIZE_SHIP_START,(unsigned char *)&rss,sizeof(rss));
		}
		break;
	default:
		LogMessage("Unhandled starbase request %d",pkt->Action);
	}
}

void Player::HandleRecustomizeShipDone(unsigned char *data)
{
	struct RecustomizeShipDone *packet = (struct RecustomizeShipDone *)data;
	u64 cost=0;
// seems we have to compare the current and returned to recalculate the cost, even though the client has already worked it out :(
	if (packet->ship.hull != m_Database.ship_data.hull)
		cost += g_CustomiseShipCosts[sz_hull];
	if (packet->ship.wing != m_Database.ship_data.wing)
		cost += g_CustomiseShipCosts[sz_wing];
	if (strcmp(packet->ship.ship_name,m_Database.ship_data.ship_name) || memcmp(packet->ship.ship_name_color,m_Database.ship_data.ship_name_color,12))
		cost += g_CustomiseShipCosts[sz_name];
	if (packet->ship.decal != m_Database.ship_data.decal)
		cost += g_CustomiseShipCosts[sz_decal];
	if (memcmp(&packet->ship.HullPrimaryColor,&m_Database.ship_data.HullPrimaryColor,sizeof(ColorInfo)))
		cost += g_CustomiseShipCosts[sz_hullcolourpri];
	if (memcmp(&packet->ship.HullSecondaryColor,&m_Database.ship_data.HullSecondaryColor,sizeof(ColorInfo)))
		cost += g_CustomiseShipCosts[sz_hullcoloursec];
	if (memcmp(&packet->ship.WingPrimaryColor,&m_Database.ship_data.WingPrimaryColor,sizeof(ColorInfo)))
		cost += g_CustomiseShipCosts[sz_wingcolourpri];
	if (memcmp(&packet->ship.WingSecondaryColor,&m_Database.ship_data.WingSecondaryColor,sizeof(ColorInfo)))
		cost += g_CustomiseShipCosts[sz_wingcoloursec];
	if (memcmp(&packet->ship.ProfessionPrimaryColor,&m_Database.ship_data.ProfessionPrimaryColor,sizeof(ColorInfo)))
		cost += g_CustomiseShipCosts[sz_profcolourpri];
	if (memcmp(&packet->ship.ProfessionSecondaryColor,&m_Database.ship_data.ProfessionSecondaryColor,sizeof(ColorInfo)))
		cost += g_CustomiseShipCosts[sz_profcoloursec];
	if (memcmp(&packet->ship.EnginePrimaryColor,&m_Database.ship_data.EnginePrimaryColor,sizeof(ColorInfo)))
		cost += g_CustomiseShipCosts[sz_enginecolourpri];
	if (memcmp(&packet->ship.EngineSecondaryColor,&m_Database.ship_data.EngineSecondaryColor,sizeof(ColorInfo)))
		cost += g_CustomiseShipCosts[sz_enginecoloursec];
	PlayerIndex()->SetCredits(PlayerIndex()->GetCredits() - cost);
	SaveCreditLevel();
//
	m_Database.ship_data = packet->ship;
	SaveDatabase();
	NeatenUpWeaponMounts();
	RemoveObject(GameID());
	SendShipData(this);
	SendAuxShipExtended();
	SendAuxPlayer();
}

void Player::HandleRecustomizeAvatarDone(unsigned char *data)
{
	struct RecustomizeAvatarDone *packet = (struct RecustomizeAvatarDone *)data;
	u64 cost=0;
// seems we have to compare the current and returned to recalculate the cost, even though the client has already worked it out :(
	if (packet->avatar.hair_num != m_Database.avatar.hair_num)
		cost += g_CustomiseAvatarCosts[az_hair];
	if (packet->avatar.beard_num != m_Database.avatar.beard_num)
		cost += g_CustomiseAvatarCosts[az_beard];
	if (memcmp(packet->avatar.hair_color,m_Database.avatar.hair_color,12))
		cost += g_CustomiseAvatarCosts[az_haircolour];
	if (memcmp(packet->avatar.skin_color,m_Database.avatar.skin_color,12))
		cost += g_CustomiseAvatarCosts[az_skincolour];
	if (memcmp(packet->avatar.eye_color,m_Database.avatar.eye_color,12))
		cost += g_CustomiseAvatarCosts[az_eyecolour];
	if (packet->avatar.goggle_num != m_Database.avatar.goggle_num)
		cost += g_CustomiseAvatarCosts[az_glasses];
	if (packet->avatar.ear_num != m_Database.avatar.ear_num)
		cost += g_CustomiseAvatarCosts[az_earpiece];
	if (packet->avatar.body_type != m_Database.avatar.body_type)
		cost += g_CustomiseAvatarCosts[az_shirt];
	if (packet->avatar.pants_type != m_Database.avatar.pants_type)
		cost += g_CustomiseAvatarCosts[az_pants];
	if (memcmp(packet->avatar.shirt_primary_color,m_Database.avatar.shirt_primary_color,12))
		cost += g_CustomiseAvatarCosts[az_shirtcolourpri];
	if (memcmp(packet->avatar.shirt_secondary_color,m_Database.avatar.shirt_secondary_color,12))
		cost += g_CustomiseAvatarCosts[az_shirtcoloursec];
	if (memcmp(packet->avatar.pants_primary_color,m_Database.avatar.pants_primary_color,12))
		cost += g_CustomiseAvatarCosts[az_pantscolourpri];
	if (memcmp(packet->avatar.pants_secondary_color,m_Database.avatar.pants_secondary_color,12))
		cost += g_CustomiseAvatarCosts[az_pantscoloursec];
	PlayerIndex()->SetCredits(PlayerIndex()->GetCredits() - cost);
	SaveCreditLevel();
//	
	m_Database.avatar = packet->avatar;
	SaveDatabase();
	SendStarbaseAvatarList();
	SendAuxPlayer();
}

void Player::SetManufactureID(long mfg_id)
{
	//LogMessage("Sending SetManufactureID packet\n");
	SendOpcode(ENB_OPCODE_007F_MANUFACTURE_SET_MANUFACTURE_ID, (unsigned char *) &mfg_id, sizeof(mfg_id));
}

/*
1C 00  "Friendship 7 Recreation Port"
00 00  ""
05 00  "Glenn"
0A 00  "Beta Hydri"

20 00  "Glenn Sector (Beta Hydri System)       from sector
0A 00  "Beta Hydri"                            from system
0E 00  "Swooping Eagle"                        to sector
06 00  "Sirius"                                to system

*/

//TODO: Recode using new packet methods
void Player::SendServerHandoff(long from_sector_id, long to_sector_id, char *from_sector, char *from_system, char *to_sector, char *to_system)
{
	int offset = 0;
	// Check for invalid destinations
	if (to_sector == 0 || to_sector_id == 0 || to_system == 0)
	{
		LogMessage("FATAL ERROR! Invalid server handoff destination! Returning to source\n");
		to_sector = from_sector;
		to_sector_id = from_sector_id;
		to_system = from_system;
	}

	ServerHandoff server_handoff;
	memset(&server_handoff, 0, sizeof(server_handoff));

	server_handoff.join = m_MasterJoin;
	server_handoff.join.ToSectorID = ntohl(to_sector_id);
	server_handoff.join.FromSectorID = ntohl(from_sector_id);

	// Populate "FROM SECTOR" string
	char *p = (char *) &server_handoff.variable_data;
	*((short *) p) = strlen(from_sector);
	p += 2;
	offset += 2;
	strncpy_s(p, sizeof(server_handoff.variable_data)-offset, from_sector, strlen(from_sector));
	p += strlen(from_sector);
	offset += strlen(from_sector);

	// Populate "FROM SYSTEM" string
	*((short *) p) = strlen(from_system);
	p += 2;
	offset += 2;
	strncpy_s(p, sizeof(server_handoff.variable_data)-offset, from_system, strlen(from_system));
	p += strlen(from_system);
	offset += strlen(from_system);

	// Populate "TO SECTOR" string
	*((short *) p) = strlen(to_sector);
	p += 2;
	offset += 2;
	strncpy_s(p, sizeof(server_handoff.variable_data)-offset, to_sector, strlen(to_sector));
	p += strlen(to_sector);
	offset += strlen(to_sector);

	// Populate "TO SYSTEM" string
	*((short *) p) = strlen(to_system);
	p += 2;
	offset += 2;
	strncpy_s(p, sizeof(server_handoff.variable_data)-offset, to_system, strlen(to_system));
	p += strlen(to_system);
	offset += strlen(to_sector);

	size_t length = (p - (char *) &server_handoff);

	//LogMessage("Sending ServerHandoff, ToSectorID = %d\n", to_sector_id);

	SendOpcode(ENB_OPCODE_003A_SERVER_HANDOFF, (unsigned char *) &server_handoff, length);

	SendPacketCache();

	ChangeSectorID(to_sector_id);
}

void Player::HandleTriggerEmote(unsigned char *data)
{
	TriggerEmote * emote = (TriggerEmote *) data;

	SendNotifyEmote(emote->GameID, emote->Emote);
}

//TODO: Recode using new packet methods
void Player::HandleChatStream(unsigned char *data)
{
	ChatStream * chat_stream = (ChatStream *) data;
	unsigned char *buffer;
	unsigned char *p;

	if (chat_stream->message[0] == 0x02)	// Emote
	{
		//LogMessage("Received AvatarTriggerEmote packet -- GameID=%d\n", chat_stream->GameID);

		buffer = new unsigned char[chat_stream->ChatSize + 7];		// 7 = (long GameID, short ChatSize, char type)
		memset(buffer, 0, chat_stream->ChatSize + 7);

		*((short *) &buffer[0]) = chat_stream->ChatSize;
		buffer[2] = 0x01;
		*((long *) &buffer[3]) = chat_stream->GameID;
		p = (unsigned char *)chat_stream;
		p+=7;
		memcpy(&buffer[7],p,chat_stream->ChatSize);

		//SendOpcode(ENB_OPCODE_005F_AVATAR_EMOTE_RESPONSE, buffer, chat_stream->ChatSize + 7);
		SendToSector(ENB_OPCODE_005F_AVATAR_EMOTE_RESPONSE, buffer,  chat_stream->ChatSize + 7);
		delete[] buffer;
	}
	else if (chat_stream->message[0] == 0x01)	// Chat in Stations
	{
		//Commented this section out so local messages aren't sent twice (Once as local, the other as broadcast)
		//short chatStringLength = *((short *)&chat_stream->message[2]);
		//char broadcast[2048];
		//sprintf(broadcast, "[Local] %s: %s", Name(), &chat_stream->message[4]);
		//SendMessageString(broadcast, 7);
		LogDebug("ChatStream code: %d\n", chat_stream->message[0]);
	}
	else
	{
		LogMessage("ChatStream code: %d\n", chat_stream->message[0]);
		LogMessage("Received Unknown ChatStream code\n");
	}
}

void Player::GetPostFix(char *FName, int length)
{
	char PostFix[10];

	switch(AdminLevel())
	{
		case HELPER:
			strcpy_s(PostFix, sizeof(PostFix), "HLP");
			break;
		case BETA:
			strcpy_s(PostFix, sizeof(PostFix), "BETA");
			break;
		case BETA_PLUS:
			strcpy_s(PostFix, sizeof(PostFix), "STAFF");
			break;
		case GM:
			strcpy_s(PostFix, sizeof(PostFix), "GM");
			break;
		case DGM:
			strcpy_s(PostFix, sizeof(PostFix), "DGM");
			break;
		case HGM:
			strcpy_s(PostFix, sizeof(PostFix), "HGM");
			break;
		case DEV:
			strcpy_s(PostFix, sizeof(PostFix), "DEV");
			break;
		case SDEV:
			strcpy_s(PostFix, sizeof(PostFix), "SDEV");
			break;
		case ADMIN:
			strcpy_s(PostFix, sizeof(PostFix), "ADMIN");
			break;

		default:
			strcpy_s(PostFix, sizeof(PostFix), "");
			break;
	}
	PostFix[sizeof(PostFix)-1] = '\0';

	if (Hijackee())
	{
		_snprintf(FName, length, "%s", GetHijackeeName());
	}
	else if (GetInvisible())
	{
		_snprintf(FName, length, "%s", Name());
	}
	else if (PostFix[0])
	{
		_snprintf(FName, length, "%s [%s]", Name(), PostFix);
	}
	else
	{
		_snprintf(FName, length, "%s", Name());
	}
}

void Player::SendClientChatEvent(long Type, Player *Source, char *Channel, char *Message , char *OtherPlayer, char *NonPlayerSrc)
{
	unsigned char Packet[1024];
	int Index = 0;
	char *LastName,*Rank;
	char PostFixName[40];

	// do ignoring here (cant ignore GMs + DEVs)
	if (Source)
	{
		if (Source->AdminLevel() < GM && IsIgnored(Source->Name()))
		{
			if (Type == CHEV_PRIVATE_MESSAGE)
			{
				Source->SendClientChatError(CHAT_ERROR_IS_IGNORED,CCE_SPEAK_LOCALLY,Name());
			}
			return;
		}
		if (m_TellsFromFriendsOnly && Type == CHEV_PRIVATE_MESSAGE && Source->AdminLevel() >= GM && !IsMyFriend(Source->Name()))
		{
			Source->SendClientChatError(CHAT_ERROR_NO_PERMISSION,CCE_SPEAK_LOCALLY,Name());
			return;
		}
		Source->GetPostFix(PostFixName, sizeof(PostFixName));
		LastName = PostFixName;
		Rank = (Type == CHEV_PRIVATE_MESSAGE) ? LastName : GetRank();
	}
	else
	{
		Rank = "";
		LastName = NonPlayerSrc;
	}

	AddData(Packet, Type, Index);			// Event type
	AddData(Packet, 0, Index);				// unknown number
//	AddDataLS(Packet, Rank, Index);			// Rank of Player
	AddDataLS(Packet, LastName, Index);		// Last Name of Player
	AddDataLS(Packet, LastName, Index);		// Last Name of Player
	AddDataLS(Packet, OtherPlayer, Index);	// for eg. kick event
	AddDataLS(Packet, Channel, Index);		// Channel it came from
	AddDataLS(Packet, Message, Index);		// Message
	AddData(Packet, (short)0, Index);		// unknown blank string
	AddData(Packet, 0, Index);				// unknown block length for private messages

	// Log all chat messages to the chat log. (only log broadcasts once)
	if (Type == CHEV_PRIVATE_MESSAGE || (Type == CHEV_CHANNEL_MESSAGE && Source == this))
	{
		LogChatMsg("%s->[%s]> \"%s\" (%s)\n", LastName, Channel, Message, Name());
	}

	SendOpcode(ENB_OPCODE_00A5_CLIENT_CHAT_EVENT, (unsigned char *) Packet, Index);	// Send Packet
}

bool Player::DevGMChannel(char *channel)
{
	if (channel[0] == 'D' && channel[1] == 'e' && channel[2] == 'v') return true;
	if (channel[0] == 'G' && channel[2] == 'M') return true;

	return false;
}

void Player::SendNotifyEmote(long game_id, long emote)
{
	NotifyEmote response;
	response.GameID = game_id;
	response.Emote = emote;

	SendToRangeList(ENB_OPCODE_00A2_NOTIFY_EMOTE, (unsigned char *) &response, sizeof(response));
}

//This function deals with Group options.
void Player::HandleOption(unsigned char *data)
{
	OptionPacket * myOption = (OptionPacket *) data;
	Group *myGroup = g_ServerMgr->m_PlayerMgr.GetGroupFromID(GroupID());
	
	if(myOption->OptionType == 0)
	{
		//looking for group
		PlayerIndex()->GroupInfo.SetLookingForGroup(myOption->OptionVar!=0);
		SendAuxPlayer();
	}
	else if(myOption->OptionType == 1)
	{
		//allow group invite
		PlayerIndex()->GroupInfo.SetAllowGroupInvite(myOption->OptionVar!=0);
		SendAuxPlayer();
	}
	//only allow group leader to change certain options
	else if(myGroup && myGroup->Member[0].GameID == GameID() && GroupID() != -1)
	{	
		//credit auto-split
		if(myOption->OptionType == 3)
			myGroup->ForceAutoSplit = myOption->OptionVar!=0;
			//PlayerIndex()->GroupInfo.SetForceAutoSplit(myOption->OptionVar!=0);
		//master-looter
		if(myOption->OptionType == 4)
			myGroup->RestrictedLootingRights = myOption->OptionVar!=0;
			//PlayerIndex()->GroupInfo.SetRestrictedLootingRights(myOption->OptionVar!=0);
		//Free for all
		if(myOption->OptionType == 5)
			myGroup->AutoReleaseLootingRights = myOption->OptionVar!=0;
			//PlayerIndex()->GroupInfo.SetAutoReleaseLootingRights(myOption->OptionVar!=0);
	
		g_ServerMgr->m_PlayerMgr.SendAuxToGroup(myGroup);
	}

	LogMessage("Received Option packet -- GameID=%d  OptionType=%d  OptionVar=%d\n",
		myOption->GameID, myOption->OptionType, myOption->OptionVar);
	DumpBuffer(data,sizeof(OptionPacket));
}

// This is going to need re-coding in the same way as the Action Packet method
void Player::HandleSelectTalkTree(unsigned char *data)
{
	SelectTalkTree * packet = (SelectTalkTree *) data;
	NPCTemplate * NPC = NULL;

	/*LogMessage("Received SelectTalkTree packet -- PlayerID=%x  Selection=%d\n",
	(packet->PlayerID & 0x00FFFFFF), packet->Selection);*/

	// check for more
	if (packet->Selection == 0 && m_MoreDestination)
	{
		packet->Selection = m_MoreDestination;
		m_MoreDestination = 0;
	}

	if (m_ActionResponseReceived == true
		&& packet->Selection == 0)
	{
		SendTalkTreeAction(-32);
		SendTalkTreeAction(-32);
		m_ActionResponseReceived = false;
		m_MissionDebriefed = false;
		return;
	}

	if (m_TradeWindow == true)
	{
		if (packet->Selection == 0)
		{
			SendTalkTreeAction(-32);
			m_TradeWindow = false;
			m_MissionDebriefed = false;
			return;
		}
		m_TradeWindow = false;
	}

	if (m_BeaconRequest)
	{
		switch(packet->Selection)
		{
		case 0:    // Tow to Base
			SendTalkTreeAction(-32);
			SetDistressBeacon(false);
			TowToBase();
			m_BeaconRequest = false;
			break;
		case 1:	   //  Distress Beacon
			SendTalkTreeAction(-32);
			SetDistressBeacon(true);
			m_BeaconRequest = false;
			break;
		case 2:    // I'm OK
			SendTalkTreeAction(-32);
			SetDistressBeacon(false);
			m_BeaconRequest = false;
			break;
		case 230:  // Close
			SendTalkTreeAction(-32);
			m_BeaconRequest = false;
			break;
		}

		if (!m_BeaconRequest)
			return;
	}

	if (packet->Selection == 255)
	{
		//just returned from a debrief, resume normal convo tree
		NPC = g_ServerMgr->m_StationMgr.GetNPC(m_StarbaseTargetID);

		// Save our current NPC to make it easier to find
		m_CurrentNPC = NPC;

		// Get talk tree for NPC
		// and save to m_CurrentTalkTree
		if (NPC && NPC->NPCInteraction.talk_tree.NumNodes > 0)
		{
			long length = GenerateTalkTree(&NPC->NPCInteraction.talk_tree, 1);

			if (length == 0)
			{
				SendTalkTreeAction(-32); //close display
				m_PushMissionID = 0;
				m_PushMissionUID = 0;
				m_MissionDebriefed = false;
				return;
			}

			// Output first node
			SendOpcode(ENB_OPCODE_0054_TALK_TREE, (unsigned char *) m_TalkTreeBuffer, length);
		}
		return;
	}
	else if (packet->Selection != 230 && packet->Selection != 0) 
	{
		if (CheckMissions(0, packet->Selection, m_StarbaseTargetID, TALK_NPC) ) 
		{
			m_TradeWindow = true;
			if (packet->Selection == 0)
			{
				SendTalkTreeAction(-32); //close display
				m_PushMissionID = 0;
				m_PushMissionUID = 0;
				m_MissionDebriefed = false;
			}
			return;
		}
	}

	if (CheckTalkTree(packet->Selection))		// See if we need a talktree from the database
		return;

	// Trade with vendor
	if (packet->Selection == 0 && m_CurrentNPC != NULL)
	{
		char string[] = "/happy2 Thanks for coming by my booth.  May good fortune be with you.\0\0\0";

		// Send Item List to Player
		NPCTradeItems();

		SendTalkTreeAction(5);
		SendTalkTreeAction(3);
		SendOpcode(ENB_OPCODE_0054_TALK_TREE, (unsigned char *) string, sizeof(string));
		SendTalkTreeAction(6);

		m_TradeWindow = true;
	}
	else
	{
		//just close the dialogue here, nothing to do
		SendTalkTreeAction(-32);
		m_MissionDebriefed = false;
	}

	if (packet->Selection == 1 || packet->Selection == 230 ) 
	{
		SendTalkTreeAction(-32);
		m_PushMissionID = 0;
		m_PushMissionUID = 0;
		m_MissionDebriefed = false;
		ClearPrices();
		SendAuxPlayer();
		SendAuxShip();
	}
}

bool Player::CheckTalkTree(int Selection)
{
	// If we have a talk tree open use this
	if (!m_CurrentNPC) return false;

	// if Selection is 0 exit!
	if (Selection == 0)
	{
		SendTalkTreeAction(-32); //close display
		m_MissionDebriefed = false;
		return true;
	}

	long length = GenerateTalkTree(&m_CurrentNPC->NPCInteraction.talk_tree, Selection);
	u16 flags = GetNodeFlags(&m_CurrentNPC->NPCInteraction.talk_tree, Selection);

	if (length == 0) return false;

	if (flags == NODE_TRADE)
	{
		// Send Items to Trade
		NPCTradeItems();
		// Send Exit Message
		SendTalkTreeAction(5);		// Open Trade Window
		SendTalkTreeAction(3);		// Send the "Trade" button
		SendOpcode(ENB_OPCODE_0054_TALK_TREE, (unsigned char *) m_TalkTreeBuffer, length);
		SendTalkTreeAction(6);		// Not sure?
	} 
	else
	{
		// Send out Node
		SendOpcode(ENB_OPCODE_0054_TALK_TREE, (unsigned char *) m_TalkTreeBuffer, length);
	}

	return true;
}

bool Player::NPCTradeItems()
{
	int TotalItems = 0;

	if (m_CurrentNPC == NULL)
		return false;

	ItemBase * item = 0;
	VendorItemList *ItemL;

	TotalItems = 0;

	for(vecItemList::const_iterator v_item = m_CurrentNPC->Vendor.Items.begin(); v_item < m_CurrentNPC->Vendor.Items.end(); ++v_item)
	{
		ItemL = g_ServerMgr->m_StationMgr.GetVendorItem(*v_item);

		if (ItemL && (item = g_ItemBaseMgr->GetItem(ItemL->ItemID)) && ItemL->Quanity != 0 )
		{
			if (item->Category() == 90 && ItemL->SellPrice <= 0)
			{
				continue; //Don't display this item, it's not for sale!
			}

			// Send ItemBase for every item if needed
			// TODO: wire this through the queue system
			SendItemBase(item->ItemTemplateID());

			PlayerIndex()->VendorInv.Item[TotalItems].SetItemTemplateID(item->ItemTemplateID());
			PlayerIndex()->VendorInv.Item[TotalItems].SetStackCount(1);
			PlayerIndex()->VendorInv.Item[TotalItems].SetStructure(1.0f);
			PlayerIndex()->VendorInv.Item[TotalItems].SetQuality(1.0f);

			// if we have a set sell price then sell it for that price
			if (ItemL->SellPrice == 0)
			{
				//This item does not have a special sell price set by the vendor, use the normal price for the item
				//vendor discount
				PlayerIndex()->VendorInv.Item[TotalItems].SetPrice((u64) Negotiate(item->VendorSellPrice(),true,true));
			}
			else if (ItemL->SellPrice == -1)		// We are not selling this item
			{
				// We are not selling this item
				TotalItems--;
			}
			else
			{
				//This item does have a special sell price set by the vendor, use that price
				PlayerIndex()->VendorInv.Item[TotalItems].SetPrice((u64) Negotiate(ItemL->SellPrice,true,true));
			}

			// Make sure we dont go over 128 Items
			TotalItems++;
		}
		if (TotalItems >= 128)
		{
			break;
		}
	}

	// Fill the rest of the inventory with no items
	for (int x=TotalItems; x<128; x++)
	{
		PlayerIndex()->VendorInv.Item[x].Clear();
	}

	SetPrices();
	SendAuxPlayer();
	SendAuxShip();

	m_TradeWindow = true;

	return true;
}

void Player::SendRelationship(long ObjectID, long Reaction, bool IsAttacking)
{
	Relationship response;
	response.ObjectID = ntohl(ObjectID);
	response.Reaction = Reaction;
	response.IsAttacking = IsAttacking ? 1 : 0;

	//LogMessage("Sending Relationship packet\n");
	SendOpcode(ENB_OPCODE_0089_RELATIONSHIP, (unsigned char *) &response, sizeof(response));
}

//TODO: Cache this with Net7Proxy
void Player::HandleGalaxyMapRequest()
{
	//LogMessage("Received GalaxyMap request packet\n");
	SendOpcode(ENB_OPCODE_2011_GALAXY_MAP_CACHE, 0, 0);
	//SendDataFileToClient("GalaxyMap.dat");
}

void Player::SendGalaxyMap(char *system, char *sector, char *station)
{
	struct GalaxyMap
	{
		long    Type;
		long    Size;
		long    PlayerID;
		char    Variable[64];
		long    unknown;
	};

	GalaxyMap galaxy_map;
	long string_length;
	long size = 8;  // include PlayerID and unknown
	long offset = 0;
	char *p = galaxy_map.Variable;

	galaxy_map.Type = 4;
	galaxy_map.PlayerID = CharacterID();

	// System
	strcpy_s(p, sizeof(galaxy_map.Variable), system);
	string_length = strlen(p) + 1;
	size += string_length;
	p += string_length;
	offset += string_length;

	// Sector
	strcpy_s(p, sizeof(galaxy_map.Variable)-offset, sector);
	string_length = strlen(p) + 1;
	size += string_length;
	p += string_length;
	offset += string_length;

	// Station
	strcpy_s(p, sizeof(galaxy_map.Variable)-offset, station);
	string_length = strlen(p) + 1;
	size += string_length;
	p += string_length;
	offset += string_length;

	*((long *) p) = 375;    // unknown = 375
	galaxy_map.Size = size;

	//LogMessage("Sending GalaxyMap packet\n");
	SendOpcode(ENB_OPCODE_0097_GALAXY_MAP, (unsigned char * ) &galaxy_map, (u16)(size + 8));
}

void Player::HandleDebug(unsigned char *data)
{
	LogDebug("Received Debug packet\n");
}

void Player::SetStartingPosition()
{
	bool wormhole = false;
	Object *nearest_nav = (0);
	SetOrientation(0.0f, 0.0f, 1.0f, 0.0f);

	if (PlayerIndex()->GetSectorNum() > MAX_SECTOR_ID) //stations is simplest case
	{
		SetPosition(0.0f, 0.0f, 0.0f);
		return;
	}

	ObjectManager *om = GetObjectManager();
	if(!om)
	{	
		SetPosition(0.0f,0.0f,0.0f);
		return;
	}
	if (m_FromSectorID > MAX_SECTOR_ID)
	{
		// The player just left a station
		// see if we had any entry coords
		if (RestoreDockingCoords())
		{
			//ok let's just check we're within sector boundaries
			SectorManager *sector_manager = GetSectorManager();
			if (sector_manager) 
			{
				ServerParameters *params = GetSectorManager()->GetSectorParams();
				float *pos = this->Position();
				if (om && (pos[0] > params->XMax ||
					pos[0] < params->XMin ||
					pos[1] > params->YMax ||
					pos[1] < params->YMin))
				{
					//find nearest nav point, and place player next to it.
					nearest_nav = om->NearestNav(Position());
					SetPosition(nearest_nav->Position());
				}
			}
		}
		else
		{
			Object *station = om->FindStation(0);
			if (station)
			{
				int offset = (int)station->Radius();
				SetPosition(station->Position());
				nearest_nav = NearestNav();
				if (nearest_nav)
				{
					FaceObject(nearest_nav);
					SetHeading();
				}

				// now move the ship forward by the station radius
				ExtrapolatePosition(station->Radius(), 1.0f);
			}
		}
	}
	else 
	{
		// The player just went through a gate
		Object *gate = om->FindGate(m_FromSectorID);
		// If we can't find a gate we probaly worm holed here
		if (!gate) 
		{
			//gate = om->FindFirstNav();
			long sector_id = PlayerIndex()->GetSectorNum();
			m_FromSectorID = sector_id;
			wormhole = true;

			struct WeftList 
			{
				int SectorID;
				char * Name;
				long ObjectID;
			};

			// List of wefts
			WeftList ListOfWefts[] =
			{
				1910, "Maeldun's Weft", 137,
				1070, "Virgil's Weft", 1094,
				4120, "Arion`s Weft", 1646,
				1705, "Hagoth's Weft", 2906,
				1077, "Hagoth's Weft", 52,
				4520, "Yamuna's Weft", 1557,
				2210, "Brendan's Weft", 624
			};

			char * WeftName = NULL;
			long WeftID = 0;


			// Find the Weft Name
			for(int x=0;x<7;x++)
			{
				if (ListOfWefts[x].SectorID == sector_id)
				{
					WeftName = ListOfWefts[x].Name;
					WeftID = ListOfWefts[x].ObjectID;
					break;
				}
			}

			// Set your location at the weft
			SectorManager *sm = g_ServerMgr->GetSectorManager((long) sector_id);
			if (sm && WeftName)
			{
				ObjectManager *om2 = sm->GetObjectManager();

				gate = om2->GetObjectFromName(g_StringMgr->GetStr(WeftName));
			}
			else
			{
				//didn't find a weft for this sector, so player used a /wormhole command
				gate = om->FindFirstNav();
			}
		}
		if (gate)
		{
			// Place the player's ship near the gate
			SetPosition(gate->Position());
			nearest_nav = NearestNav();
			if (nearest_nav)
			{
				FaceObject(nearest_nav);
				SetHeading();
			}

			// Now move the ship outside of the gate
			ExtrapolatePosition(gate->Radius(), 1.0f);
		}
		else
		{
			SetPosition(0.0f, 0.0f, 0.0f);
		}
	}

	if (!wormhole && (m_FromSectorID == 0 || m_FromSectorID == PlayerIndex()->GetSectorNum()))		// We just logged in! 
	{
		bool success = LoadPosition();

		if (!success || (PosX() == 0.0f && PosY() == 0.0f && PosZ() == 0.0f))
		{
			LogDebug("Cannot load postion file setting to nearest gate.\n");

			//find first nav
			Object *obj = om->FindFirstNav();

			if (!obj)
			{
				obj = om->GetObjectFromID(10000);
			}

			if (obj)
			{
				SetPosition(obj->Position()); 
				nearest_nav = NearestNav();
				if (nearest_nav)
				{
					FaceObject(nearest_nav);
					SetHeading();
				}
				ExtrapolatePosition(obj->Radius() + 500.0f, 1.0f);
			}
		} 
	}

	UpdateVerbs();
	SavePosition();
}

void Player::SendMessageStringToGroup(char *msg, char color, bool log)
{
	// check for group
	Group *g = g_ServerMgr->m_PlayerMgr.GetGroupFromID(GroupID());
	Player *player = 0;

	if (g)
	{	
		for (int i=0;i<6;i++)
		{
			if (g->Member[i].GameID != -1 && g->AcceptedInvite[i])
			{
				player = g_ServerMgr->m_PlayerMgr.GetPlayer(g->Member[i].GameID);	

				if (player)
				{
					// check the player is still in the group
					if (player->GroupID() == GroupID())
						player->SendMessageString(msg,color,log);
					else
						LogMessage("ERROR: attempt to send message string from group %d to player %d of group %d\n",GroupID(),player->GameID(),player->GroupID());
				}
			}
		}
	}
	else
	{
		// send to self
		SendMessageString(msg);
	}
}

// color
// 5 top panel green
// 4 bottom panel purple (group)
void Player::SendMessageString(char *msg, char color, bool log)
{
	char buffer[512];
	memset(buffer, 0, sizeof(buffer));
	short length = strlen(msg) + 1;
	*((short *) &buffer[0]) = length;
	buffer[2] = color;
	strcpy_s(&buffer[3], sizeof(buffer) - 4, msg);

	SendOpcode(ENB_OPCODE_001D_MESSAGE_STRING, (unsigned char *) buffer, length + 3);
}

void Player::SendPriorityMessageString(char *msg1, char *msg2, long time, long priority)
{
	unsigned char buffer[512];
	int index = 0;
	memset(buffer, 0, sizeof(buffer));

	AddDataSN(buffer, msg1, index);
	AddDataSN(buffer, msg2, index);
	AddData(buffer, time, index);
	AddData(buffer, priority, index);

	SendOpcode(ENB_OPCODE_0020_PRIORITY_MESSAGE, &buffer[0], index);
}

void Player::HandleMissionDismissal(unsigned char *data)
{
	MissionDismissal *dismiss = (MissionDismissal *)data;
	long MissionID = ntohl(dismiss->MissionID);
	long PlayerID = ntohl(dismiss->PlayerID);

	MissionDismiss(MissionID,false);
}

void Player::HandleMissionForfeit(unsigned char *data) //TODO: change handling of forfeit. at the moment, we allow ppl to repeat the mission.
{
	MissionDismissal *dismiss = (MissionDismissal *)data;
	long MissionID = ntohl(dismiss->MissionID);
	long PlayerID = ntohl(dismiss->PlayerID);

	if (MissionID >= 0 && MissionID < 12)
		MissionDismiss(MissionID,true);
}

/*
struct PetitionStuck
{
long GameID;            // Player ID
long ProblemType;       // see below
char Subject[];         // variable length null-terminated string
char Complaint[];       // variable length null-terminated string
char PlayerList[];      // variable length null-terminated string
};

Problem Type:
1 = I have a question about game play
2 = I am stuck, trapped, or blackholed
3-7 [I am having a problem with another player]
3 = ... has a bad or offensive name
4 = ... is verbally harassing me using the chat system
5 = ... is harassing me using game mechanics
6 = ... is spamming or using bad language
7 = ... is cheating, macroing or exploiting

23 00                              Length = 35 bytes
88 00                              Opcode 0x88 = PetitionStuck
EF 17 C0 07                        GameID = player
03 00 00 00                        ProblemType
73 75 62 6A 65 63 74 00            Subject = "Subject"
64 65 74 61 69 6C 73 00            Complaint = "Details"
50 6C 61 79 65 72 6E 61 6D 65 00   PlayerList = "Playername"
*/

void Player::HandlePetitionStuck(unsigned char *data, short bytes)
{
	SavePetition(data, bytes);
}

void Player::HandleIncapacitanceRequest(unsigned char *data)
{
	long player = *data; 

	char string[] = 
		"That was one heck of an explosion! Are you alright over there?\0"
		"\0\3"
		"\0\0\0\0"
		"I need a tow\0"
		"\1\0\0\0"
		"Toggle distress beacon\0"
		"\2\0\0\0"
		"I'm OK\0";

	SendOpcode(ENB_OPCODE_0054_TALK_TREE, (unsigned char *) string, sizeof(string));

	if (!m_IncapAvatarSent)
	{
		m_IncapAvatarSent = true;

		// Send Avatar ID to show up in talk tree
		PlayerIndex()->SetPIPAvatarID(-3);
		SendAuxPlayer();

		StationTemplate * Stn = g_ServerMgr->m_StationMgr.GetStation(m_RegisteredSectorID);

		if (!Stn)
		{
			Stn = g_ServerMgr->m_StationMgr.GetStation(10711); //Net7
			if (!Stn) return;
		}

		NPCTemplate * NPCs = g_ServerMgr->m_StationMgr.GetNPC(Stn->NPCs[0]);

		if (!NPCs) return;

		AvatarDescription avatar;
		memset(&avatar, 0, sizeof(avatar));

		memcpy(&avatar.avatar_data, &NPCs->Avatar, sizeof(avatar.avatar_data));

		NPCs->Avatar.shirt_primary_color[0] += 0.1f;
		NPCs->Avatar.shirt_primary_color[1] -= 0.1f;
		NPCs->Avatar.shirt_primary_color[2] += 0.15f;

		strcpy_s(avatar.avatar_data.avatar_first_name, sizeof(avatar.avatar_data.avatar_first_name), "Station\0");
		strcpy_s(avatar.avatar_data.avatar_last_name, sizeof(avatar.avatar_data.avatar_last_name), "Mechanic\0");

		avatar.AvatarID = -3;
		avatar.unknown3 = 1.0;
		avatar.unknown4 = 1.0;
		SendOpcode(ENB_OPCODE_0061_AVATAR_DESCRIPTION, (unsigned char *) &avatar, sizeof(avatar));
	}

	m_BeaconRequest = true;
}

void Player::SendWarpIndex(int index)
{
	SendOpcode(ENB_OPCODE_009C_WARP_INDEX, (unsigned char*) &index, sizeof(index));
}

void Player::LoginFailed()
{
	SectorReset();
	m_LoginFailed = true;
	m_NavCommence = true;
}

void Player::SendPIPAvatar(long game_id, long avatar_id, bool actual_use, bool avatar_id_is_npc)
{
	Object *obj = 0;
	RemoveObject(game_id);
	// Send Avatar ID to show up in talk tree
	PlayerIndex()->SetPIPAvatarID(game_id);
	SendAuxPlayer();

	//get a random NPC face for now
	mapNPCs *npc_list = g_ServerMgr->m_StationMgr.GetNPCList();
	NPCTemplate * NPC = NULL;

	AvatarDescription avatar;
	memset(&avatar, 0, sizeof(avatar));

	if (avatar_id_is_npc)
	{
		actual_use = false; // don't override the avatar data if we're using an avatar
		NPC = g_ServerMgr->m_StationMgr.GetNPC(avatar_id);

		memcpy(&avatar.avatar_data, &NPC->Avatar, sizeof(avatar.avatar_data));

		game_id = -3;
	}
	else
	{
		obj = g_SectorObjects[avatar_id];
		if (!obj) return;
		long index = avatar_id%200; 
		//get a random avatar from the station NPC list
		for(mapNPCs::const_iterator npc = npc_list->begin(); npc != npc_list->end(); ++npc)
		{
			if (index-- <= 0 && npc->second) //choose this NPC
			{
				memcpy(&avatar.avatar_data, &npc->second->Avatar, sizeof(avatar.avatar_data));
				break;
			}
		}

		avatar.avatar_data.shirt_primary_color[0] += 0.1f;
		avatar.avatar_data.shirt_primary_color[1] -= 0.1f;
		avatar.avatar_data.shirt_primary_color[2] += 0.15f;
	}

	if (actual_use)
	{
		long len = strlen(obj->Name());
		bool split = false;

		if (len >= 20)
		{
			//split the name between first and last names
			//trace back from just after the centre first
			char *ptr = obj->Name();

			if(len > 38)
			{
				strncpy_s(avatar.avatar_data.avatar_first_name, sizeof(avatar.avatar_data.avatar_first_name), "Name too long.", strlen("Name too long."));
				memset(avatar.avatar_data.avatar_last_name, 0, 20);
			
			}
			else
			{
				strncpy_s(avatar.avatar_data.avatar_first_name, sizeof(avatar.avatar_data.avatar_first_name), ptr, 19);
				avatar.avatar_data.avatar_first_name[sizeof(avatar.avatar_data.avatar_first_name)-1] = '\0';
				strncpy_s(
					avatar.avatar_data.avatar_last_name, 
					sizeof(avatar.avatar_data.avatar_last_name), 
					ptr + 19, 
					len-19);
				avatar.avatar_data.avatar_last_name[sizeof(avatar.avatar_data.avatar_last_name)-1] = '\0';
				split = true;
			}
		}

		if (!split)
		{
			strncpy_s(avatar.avatar_data.avatar_first_name, sizeof(avatar.avatar_data.avatar_first_name), obj->Name(), 20);
			memset(avatar.avatar_data.avatar_last_name, 0, 20);
		}
	}
	else
	{
		strcpy_s(avatar.avatar_data.avatar_first_name, sizeof(avatar.avatar_data.avatar_first_name), "Station\0");
		strcpy_s(avatar.avatar_data.avatar_last_name, sizeof(avatar.avatar_data.avatar_last_name), "Mechanic\0");
	}

	avatar.AvatarID = game_id;
	avatar.unknown3 = 1.0;
	avatar.unknown4 = 1.0;
	SendOpcode(ENB_OPCODE_0061_AVATAR_DESCRIPTION, (unsigned char *) &avatar, sizeof(avatar));
}

void Player::SendFindMember(struct FindMember *players)
{
	SendOpcode(ENB_OPCODE_0053_FIND_MEMBER, (unsigned char *) players, players->count * 16 + 4);
}

void Player::HandleLocalGate(long destination, long source)
{
	SectorManager *sm = GetSectorManager();
	if (!sm) return;
    m_WarpEffect = sm->GetSectorNextObjID();

	//activate fold space effect
	ObjectEffect LocalGateEffect;
	memset(&LocalGateEffect, 0, sizeof(LocalGateEffect));
	
	LocalGateEffect.Bitmask = 3;
	LocalGateEffect.TimeStamp = GetNet7TickCount();
	LocalGateEffect.EffectID = m_WarpEffect;
	LocalGateEffect.EffectDescID = 202;
	LocalGateEffect.GameID = GameID();

	SendObjectEffectRL(&LocalGateEffect);

	ShipIndex()->SetTargetGameID(-1);
	SendAuxShip();
	BlankVerbs();

	sm->AddTimedCall(this, B_ARRIVE_AT_LOCAL_GATE, 2000, 0, destination );

	/*Object *gate = g_SectorObjects[destination];
	if (gate)
	{
		SetPosition(gate->Position());
		ExtrapolatePosition(gate->Radius(), 1.0f);
	}*/

	CloseStargate(source);
}

void Player::ArriveAtLocalGate(long destination)
{
	RemoveEffectRL(m_WarpEffect);
	//now reappear at the other gate

	Object *gate = g_SectorObjects[destination];
	if (gate)
	{
		SetPosition(gate->Position());
		ExtrapolatePosition(gate->Radius(), 1.0f);
	}

	// materialise effect on this object
	ObjectEffect LocalGateEffect;
	memset(&LocalGateEffect, 0, sizeof(LocalGateEffect));
	
	LocalGateEffect.Bitmask = 0x07;
	LocalGateEffect.TimeStamp = GetNet7TickCount();
	LocalGateEffect.Duration = 3000;
	LocalGateEffect.EffectID = m_WarpEffect;
	LocalGateEffect.EffectDescID = 267;
	LocalGateEffect.GameID = GameID();
	SendObjectEffectRL(&LocalGateEffect);

	m_Gating = false;

	UpdatePlayerVisibilityList();
	SendLocationAndSpeed(true);
	UpdateVerbs();
	CheckObjectRanges();
	CheckNavs();
}