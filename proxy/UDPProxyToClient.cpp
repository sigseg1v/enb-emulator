#include "Net7.h"
#include "UDPClient.h"
#include "opcodes.h"
#include "PacketStructures.h"
#include "PacketMethods.h"
#include "Connection.h"
#include "ServerManager.h"

void __cdecl ObjectRemoveThread(void *Param);
void __cdecl EffectRemoveThread(void *Param);
void __cdecl ShutdownThread(void *Param);

bool g_ShuttingDown = false;

//Client->Server opcode
//TODO: allow for changing IP
void UDPClient::ForwardClientOpcode(short opcode, short bytes, char *packet)
{
	if (m_PlayerID != 0 && m_ConnectionActive)
	{
		SendResponse(m_ClientPort, opcode, (unsigned char*)packet, bytes);
		LogVMessage("Sending [0x%04x] via UDP\n",opcode);
	}
}

extern DWORD time_debug;

//Server->Client direct opcode - we don't see any of these any more.
void UDPClient::ProcessClientOpcode(char *msg, EnbUdpHeader *header)
{
	//this is a raw, direct opcode just for forwarding to the client
	//we need to strip out the TCP opcode
	short opcode = header->opcode;
	short bytes = header->size - sizeof(EnbUdpHeader);
	//long sequence_num = header->packet_sequence;

	//LogMessage("Incomming opcode from Net7 Server: 0x%04x\n", opcode);
	if (time_debug > 0)
	{
		//LogMessage("Incomming opcode from Net7 Server: 0x%04x\n", opcode);
		time_debug--;
	}

	if (!ConnectionActive())
	{
		//LogMessage("Processing opcode when connection inactive: 0x%04x\n",opcode);
	}


	if (g_ServerMgr->m_SectorConnection)
	{
		g_ServerMgr->m_SectorConnection->SendResponse(opcode, (unsigned char *) msg, bytes, header->packet_sequence);
	}

	IncommingOpcodePreProcessing(opcode, msg, bytes);
}

//examine incomming opcodes and react accordingly
void UDPClient::IncommingOpcodePreProcessing(short opcode, char *msg, short bytes, bool tcp)
{
	switch (opcode)
	{
	case ENB_OPCODE_00BA_LOGOFF_CONFIRMATION:
		LogMessage("---> LogOff confirm\n");
		g_ServerMgr->m_UDPConnection->SetConnectionActive(false);
		g_ServerMgr->m_UDPClient->SetConnectionActive(false);
		g_ServerMgr->m_UDPClient->SetPlayerID(0);
		g_ServerMgr->m_UDPConnection->SetPlayerID(0);
		break;

	case ENB_OPCODE_003A_SERVER_HANDOFF:
		LogMessage("---> Server handoff\n");
		g_ServerMgr->m_UDPConnection->SetConnectionActive(false);
		g_ServerMgr->m_UDPClient->SetConnectionActive(false);
		m_Packets.clear();
		m_CurrentPacketNum = -1;
		g_ServerMgr->m_UDPConnection->RecordLastHandoff(msg, bytes);

		break;

	case ENB_OPCODE_0005_START:
		if (!tcp)
		{
			g_ServerMgr->m_UDPClient->SetLoginComplete(true);
			g_ServerMgr->m_UDPConnection->KillTCPConnection();
			//LogMessage("Terminating TCP link (UDP Login)\n");
		}
		break;

	default:
		break;
	}
}

void UDPClient::SendCachedGalaxyMap()
{
	if (g_ServerMgr->m_SectorConnection)
	{
		g_ServerMgr->m_SectorConnection->SendDataFileToClient("GalaxyMap.dat");
	}
	m_PacketDropThisSession = 0;
	m_PacketTimer = 100;
}

void UDPClient::SendClientDataFile(char *msg, EnbUdpHeader *header)
{
	//this is a raw, direct opcode just for forwarding to the client
	//we need to strip out the TCP opcode
	m_Resync = true;

	short bytes = header->size - sizeof(EnbUdpHeader);

	short opcode = *((short*) &msg[2]);
	short length = *((short*) &msg[0]);

	if (g_ServerMgr->m_SectorConnection)
	{
		switch (opcode)
		{
		case ENB_OPCODE_0097_GALAXY_MAP:
			SendCachedGalaxyMap();
			break;

		default:
			g_ServerMgr->m_SectorConnection->SendResponse(opcode, (unsigned char *) msg + 4, length - 4, header->packet_sequence);
			break;
		}
	}
}

#define PACKET_BLANK ((char*)0)
#define PACKET_DONE  ((char*)-1)
#define PACKET_RE_REQUESTED  ((char*)-2)

//this is all received from "m_UDPConnection" on ServerManager
void UDPClient::SendPacketSequence(char *msg, EnbUdpHeader *header, bool continuation)
{
	ReSend resend;
	long header_size = 512;

	if (g_Packet_Opt_requested)
	{
		header_size = 1400;
	}

	_ASSERTE( _CrtCheckMemory( ) );
	if (header->packet_sequence == 0) //reset packet sequence
	{
		LogVMessage("Packet header num reset\n");
		m_CurrentPacketNum = 0;
		m_Packets.clear();
		m_PacketDropThisSession = 0;
		m_PacketTimer = 100;
	}  

	if (!m_ConnectionActive)
	{
		LogVMessage("Prevent receive of UDP packet outside of login connection\n");
		return;
	}

	LogVMessage("incomming packet sequence: header id %d Expecting %d\n",header->packet_sequence, m_CurrentPacketNum);

	//store packet in sequence
	if (m_Packets[header->packet_sequence] == 0) //should take care of echoes
	{
		char *packet = new char[header->size];
		memcpy(packet, msg, header->size);
		m_Packets[header->packet_sequence] = packet;
	}
	else
	{
		if (m_Packets[header->packet_sequence] == PACKET_DONE)
		{
			LogVMessage("incomming: %d. Already processed [%d]\n", header->packet_sequence, m_CurrentPacketNum);
		}
		else
		{
			LogVMessage("incomming: %d. Was re-requested [%d]\n", header->packet_sequence, m_CurrentPacketNum);
		}
		//replace with new one though
		char *message = m_Packets[header->packet_sequence];
		if (message && message != (PACKET_DONE) && message != (PACKET_RE_REQUESTED) )
		{
			delete [] message;
		}
		char *packet = new char[header->size];
		memcpy(packet, msg, header->size);
		m_Packets[header->packet_sequence] = packet;
	}

	if (header->packet_sequence > (m_CurrentPacketNum + 30))
	{
		LogVMessage("Prevent incomming messages way out of range: %d\n", header->packet_sequence);
		return;
	}

	if (header->packet_sequence > m_CurrentPacketNum) //packet too early, or previous packet dropped out, and we don't have this packet
	{
		m_PacketTimeout++;
		if (m_Packets[m_CurrentPacketNum] == 0)
		{
			//LogDebug("incomming: %x. Expected: %x\n", header->packet_sequence, m_CurrentPacketNum);

			//re-request packet range
			resend.packet_start = m_CurrentPacketNum;
			resend.packet_count = 1;

			if (m_PacketTimeout > 10) 
			{
				//skip this pesky packet
				m_CurrentPacketNum++;
				m_PacketTimeout = 0;
				LogVMessage("Skipping pesky packet %d\n", m_CurrentPacketNum-1);
				return;
			}

			//are we allowed to ask for more packets yet?
			unsigned long tick = GetNet7TickCount();

			//LogDebug(">> request packet range %x [%x] to be re-sent\n", resend.packet_start, resend.packet_count);
			LogVMessage(">> request packet %d to be re-sent\n", m_CurrentPacketNum);
			m_Packets[m_CurrentPacketNum] = PACKET_RE_REQUESTED;
			g_ServerMgr->m_UDPConnection->ForwardClientOpcode(ENB_OPCODE_2017_RESEND_PACKET_SEQUENCE, sizeof(ReSend), (char*)&resend);
			m_PacketResendTimer = tick;
			m_PacketDropThisSession++;
			if (m_PacketDropThisSession > 40)
			{
				m_PacketTimer = 500; //persistant offenders slow down packet requests
			}
			if (m_PacketDropThisSession > 500)
			{
				LogMessage("Your packet dropout is way too great to run the game. If you get this message please contact the devs on the forum.\n");
				m_PacketTimer = 2000;
			}

			return;
		}
		else if ( m_Packets[m_CurrentPacketNum] == PACKET_RE_REQUESTED)
		{
			if (m_PacketTimeout > 10)
			{
				m_CurrentPacketNum++;
				m_PacketTimeout = 0;
				LogVMessage("Skipping pesky packet %d\n", m_CurrentPacketNum-1);
				return;
			}
		}
	}

	_ASSERTE( _CrtCheckMemory( ) );

	//now kick off packet if it's what we're expecting
	while(m_Packets[m_CurrentPacketNum] != 0 && m_Packets[m_CurrentPacketNum] != PACKET_DONE && m_Packets[m_CurrentPacketNum] != (PACKET_RE_REQUESTED))
	{
		char *message = m_Packets[m_CurrentPacketNum];
		LogVMessage("Processing packet %d\n", m_CurrentPacketNum);
		bool packet_pass = true;

		if (m_CurrentPacketNum == 16)
		{
			Sleep(1);
		}

		if (m_SplitPacketLength == 0)
		{
			//test to see if this is the start of a split packet
			char *ptr = message + sizeof(EnbUdpHeader);
			unsigned short length = *((short*) &ptr[0]);
			if (length > (header_size) && length < 20000)
			{
				//start of split packet
				m_SplitPacketLength = length - (header_size);
				memcpy(m_SplitPacketBuffer, message, header_size + sizeof(EnbUdpHeader));
				m_SplitPacketptr = m_SplitPacketBuffer + header_size + sizeof(EnbUdpHeader);
				LogVMessage("Starting split packet total size = 0x%x [%d] Remaining: %d\n", length, length, m_SplitPacketLength);
				m_SplitPacketStart = m_CurrentPacketNum;
			}
			else
			{
				//normal packet
				//first check this isn't a continuation opcode
				if (continuation == true)
				{
					//ERROR: continuation opcode in normal sequence. Try to recover from this by skipping!
					LogMessage("ERROR: Continuation opcode found as normal packet sequence. Attempting to recover by skipping whole sequence.\n");
					packet_pass = true;
				}
				else
				{
					packet_pass = SendClientPacketSequence(message);
				}
			}
		}
		else if (m_SplitPacketLength > 0) //this could be a resend of a continuation packet
		{
			//continuation of split packet
			EnbUdpHeader *hdr = (EnbUdpHeader*) message;
			memcpy(m_SplitPacketptr, message + sizeof(EnbUdpHeader), (hdr->size - sizeof(EnbUdpHeader)));
			m_SplitPacketptr += (hdr->size - sizeof(EnbUdpHeader));
			m_SplitPacketLength -= (hdr->size - sizeof(EnbUdpHeader));
			LogVMessage("Received %d, remaining %d\n", (hdr->size - sizeof(EnbUdpHeader)), m_SplitPacketLength);

			if (m_SplitPacketLength <= 0)
			{
				packet_pass = SendClientPacketSequence((char*)m_SplitPacketBuffer);
				if (m_SplitPacketLength < 0)
				{
					LogVMessage("Packet error, remaining packet = %d\n", m_SplitPacketLength);
				}
				m_SplitPacketLength = 0;
				if (packet_pass == false)
				{
					//request re-send of entire packet from start
					for (int i = m_SplitPacketStart; i <= m_CurrentPacketNum; i++)
					{
						if (m_Packets[i] != 0 && m_Packets[i] != (PACKET_DONE) && m_Packets[i] != (PACKET_RE_REQUESTED))
						{
							delete[] m_Packets[i];
						}
						m_Packets[i] = PACKET_RE_REQUESTED;
					}
					LogVMessage(">> request packet %d to %d be re-sent\n", m_SplitPacketStart, m_CurrentPacketNum);
					resend.packet_start = m_SplitPacketStart;
					resend.packet_count = m_CurrentPacketNum - m_SplitPacketStart;
					m_CurrentPacketNum = m_SplitPacketStart;
					g_ServerMgr->m_UDPConnection->ForwardClientOpcode(ENB_OPCODE_2017_RESEND_PACKET_SEQUENCE, sizeof(ReSend), (char*)&resend);
					return;
				}
			}
		}

		delete [] message;

		m_PacketTimeout = 0;

		if (packet_pass)
		{
			m_Packets[m_CurrentPacketNum] = (PACKET_DONE);
			m_CurrentPacketNum++;
		}
		else
		{
			m_Packets[m_CurrentPacketNum] = 0;
		}
	}

	_ASSERTE( _CrtCheckMemory( ) );
}

void UDPClient::SendLoginPacketSequence(char *msg, EnbUdpHeader *header)
{
	LogVMessage("Received Login Packet sequence %d\n", header->packet_sequence);
	long player_id = g_ServerMgr->m_UDPClient->PlayerID();
	//first send the packet as normal
	SendPacketSequence(msg, header);
	//now send the ack
	//g_ServerMgr->m_UDPClient->SendResponse(player_id, MVAS_LOGIN_PORT, ENB_OPCODE_2021_LOGIN_STAGE_ACK, (unsigned char *) &player_id, sizeof(player_id));
	//g_ServerMgr->m_UDPConnection->ForwardClientOpcode(ENB_OPCODE_2021_LOGIN_STAGE_ACK, sizeof(player_id), (char *) &player_id);
}

bool UDPClient::HandleCustomOpcode(short opcode, char *ptr, u8 *tcp_packet, short &tcp_index)
{
	bool handled = true;
	//LogMessage("opcode 0x%04x\n", opcode);
	switch (opcode)
	{
	case ENB_OPCODE_2011_GALAXY_MAP_CACHE:
		SendCachedGalaxyMap();
		g_ServerMgr->m_UDPClient->SetReceivedGalaxyMap();
		break;

	case ENB_OPCODE_2012_START_PROSPECT:
		StartProspecting(ptr, tcp_packet, tcp_index);
		break;

	case ENB_OPCODE_2013_TRACTOR_ORE:
		TractorOre(ptr, tcp_packet, tcp_index);
		break;

	case ENB_OPCODE_2014_LOOT_ITEM:
		LootItem(ptr, tcp_packet, tcp_index);
		break;

	case ENB_OPCODE_2018_STATIC_OBJECT_CREATE:
		CreateObject(ptr, tcp_packet, tcp_index);
		break;

	case ENB_OPCODE_2019_RESOURCE_OBJECT_CREATE:
		CreateResource(ptr, tcp_packet, tcp_index);
		break;

	case ENB_OPCODE_2020_LOGIN_STAGE_S_C:
		//server is waiting for a login stage confirm
		HandleStageConfirm(ptr, tcp_packet, tcp_index);
		break;

	case ENB_OPCODE_100A_MVAS_TERMINATE_S_C:
		{
			long player_id = m_PlayerID;
			//shutdown the client if it's still running and then close Net7Proxy
			LogMessage("Shutdown1\n");
			g_ShuttingDown = true;
			g_ServerMgr->m_SectorConnection->QueueResponse(tcp_packet, tcp_index, ENB_OPCODE_0003_LOGOFF, (unsigned char*)&player_id, sizeof(player_id) );
			_beginthread( ShutdownThread, 0, (void *) (0) );
		}
		break;

	default:
		handled = false;
	}

	return handled;
}

bool UDPClient::SendClientPacketSequence(char *msg)
{
	EnbUdpHeader *header = (EnbUdpHeader*) msg;
	short bytes = header->size - sizeof(EnbUdpHeader);
	long index = 0;
	short tcp_index = 0;
	unsigned char *tcp_packet = m_QueueBuffer;
	char *ptr = msg + sizeof(EnbUdpHeader);
	bool terminate = false;

	if (!g_ServerMgr->m_SectorConnection) return false;

	while (index < bytes && !terminate && !g_ShuttingDown)
	{
		short length = *((short*) &ptr[0]);
		short opcode = *((short*) &ptr[2]);
		LogVMessage("--> opcode #%04x, length: %x\n", opcode, length);

		if (length > (bytes - index))
		{
			//opcode length exceeds packet size
		}
		else if (length < 0)
		{
			//bail out here, some kind of error
			//TODO: get this packet re-sent
			LogMessage("Error in packet format for packet %x\n", header->packet_sequence);
			break;
		}

		if (!HandleCustomOpcode(opcode, ptr+4, tcp_packet, tcp_index))
		{
			LogVMessage("<SERVER TO CLIENT UDP> ----> %04x [%04x]\n", opcode, length);
			if (opcode > 0x0000 && opcode < 0x0FFF)
			{
				IncommingOpcodePreProcessing(opcode, (char *) ptr + 4, length - 4);
				g_ServerMgr->m_SectorConnection->QueueResponse(tcp_packet, tcp_index, opcode, (unsigned char *) ptr + 4, length - 4);
			}
			else
			{
				LogMessage("Bad opcode through to Proxy: 0x%04x Length: 0x%x\n", opcode, length);
				//terminate here
				tcp_index = 0;
				terminate = true;
			}
		}

		//if this is occurring during login, we want to send off the packets, otherwise we'll go over the limit
		if (!terminate && g_ServerMgr->m_UDPClient->GetLoginComplete())
		{
			g_ServerMgr->m_SectorConnection->SendQueuedPacket(tcp_packet, tcp_index);
			tcp_packet = m_QueueBuffer;
			tcp_index = 0;
		}

		ptr += length;
		index += length;
	}

	if (!terminate && tcp_index > 0)
	{
		g_ServerMgr->m_SectorConnection->SendQueuedPacket(tcp_packet, tcp_index);        
	}
	return !terminate;
}

void UDPClient::StartProspecting(char *msg, u8* tcp_packet, short &tcp_index)
{
	long player_id = *((long*) &msg[0]);
	long target_id = *((long*) &msg[4]);
	long effect_id = *((long*) &msg[8]);
	long prospect_tick = *((long*) &msg[12]);
	long effect_time = *((long*) &msg[16]);

	if (!g_ServerMgr->m_SectorConnection) return;

	if (tcp_packet == 0)
	{
		tcp_index = 0;
		tcp_packet = m_QueueBuffer;
	}

	if (player_id == m_PlayerID) //we are the originating player
	{
		g_ServerMgr->m_SectorConnection->QueueProspectAUX(tcp_packet, tcp_index, prospect_tick, 0);
		g_ServerMgr->m_SectorConnection->QueueMessageString(tcp_packet, tcp_index, "Prospect ability activated.", 0);
	}

	g_ServerMgr->m_SectorConnection->QueueBeamEffect(tcp_packet, tcp_index, player_id, target_id, 0, 0x00BF, effect_id, prospect_tick);

	//now kick off a thread to remove the effect - this ensures we never get hanging effects, even if the next packet drops
	EffectCancel *cancel_effect = new EffectCancel;
	cancel_effect->effect_id = effect_id;
	cancel_effect->time_delay = effect_time;

	_beginthread( EffectRemoveThread, 0, (void *) (cancel_effect) );
}

void UDPClient::TractorOre(char *ch_msg, u8* tcp_packet, short &tcp_index)
{
	char name[128];
	int index = 0;
	unsigned char *msg = (unsigned char *)ch_msg;
	long player_id = ExtractLong(msg, index);
	long article_id = ExtractLong(msg, index);
	long article_effect_id = ExtractLong(msg, index);
	short resource_basset = ExtractShort(msg, index);
	long prospect_tick = ExtractLong(msg, index);
	ExtractDataLS(msg, name, index);
	long tractor_time = ExtractLong(msg, index);
	float tractor_speed = ExtractFloat(msg, index);
	float obj_pos[3];
	obj_pos[0] = ExtractFloat(msg, index);
	obj_pos[1] = ExtractFloat(msg, index);
	obj_pos[2] = ExtractFloat(msg, index);

	if (!g_ServerMgr->m_SectorConnection) return;

	//now construct the large TCP packet to send in one go (this stops the old problem with the beam shooting off to 0,0,0)
	if (tcp_packet == 0)
	{
		tcp_index = 0;
		tcp_packet = m_QueueBuffer;
	}

	if (player_id == m_PlayerID) //we are the originating player
	{
		g_ServerMgr->m_SectorConnection->QueueProspectAUX(tcp_packet, tcp_index, 0, 1);
		g_ServerMgr->m_SectorConnection->QueueCameraControl(tcp_packet, tcp_index, 0x03000000, ntohl(article_id));
		g_ServerMgr->m_SectorConnection->QueueObjectCreate(tcp_packet, tcp_index, article_id, 1.0f, resource_basset, 4);
		g_ServerMgr->m_SectorConnection->QueueCameraControl(tcp_packet, tcp_index, 0x03000000, ntohl(article_id));
	}
	else
	{
		g_ServerMgr->m_SectorConnection->QueueObjectCreate(tcp_packet, tcp_index, article_id, 1.0f, resource_basset, 4);
	}

	g_ServerMgr->m_SectorConnection->QueueEffect(tcp_packet, tcp_index, player_id, article_id, "TRACTOR", 0x0002, article_effect_id, prospect_tick, tractor_time);
	g_ServerMgr->m_SectorConnection->QueueRelationship(tcp_packet, tcp_index, article_id, RELATIONSHIP_FRIENDLY, 0);
	g_ServerMgr->m_SectorConnection->QueueResourceName(tcp_packet, tcp_index, article_id, name);
	g_ServerMgr->m_SectorConnection->QueueTractorComponent(tcp_packet, tcp_index, obj_pos, tractor_speed, player_id, article_id, article_effect_id, prospect_tick);

	//now kick off a thread to remove the effect
	EffectCancel *cancel_effect = new EffectCancel;
	cancel_effect->effect_id = article_id;
	cancel_effect->time_delay = tractor_time;

	_beginthread( ObjectRemoveThread, 0, (void *) (cancel_effect) );
}

void UDPClient::LootItem(char *ch_msg, u8* tcp_packet, short &tcp_index)
{
	char name[128];
	int index = 0;
	unsigned char *msg = (unsigned char *)ch_msg;
	long player_id = ExtractLong(msg, index);
	long article_id = ExtractLong(msg, index);
	long article_effect_id = ExtractLong(msg, index);
	short resource_basset = ExtractShort(msg, index);
	long prospect_tick = ExtractLong(msg, index);
	ExtractDataLS(msg, name, index);
	long tractor_time = ExtractLong(msg, index);
	float tractor_speed = ExtractFloat(msg, index);
	float obj_pos[3];
	obj_pos[0] = ExtractFloat(msg, index);
	obj_pos[1] = ExtractFloat(msg, index);
	obj_pos[2] = ExtractFloat(msg, index);

	if (!g_ServerMgr->m_SectorConnection) return;

	//now construct the large TCP packet to send in one go (this stops the old problem with the beam shooting off to 0,0,0)

	if (tcp_packet == 0)
	{
		tcp_index = 0;
		tcp_packet = m_QueueBuffer;
	}

	g_ServerMgr->m_SectorConnection->QueueObjectCreate(tcp_packet, tcp_index, article_id, 1.0f, resource_basset, 4);
	g_ServerMgr->m_SectorConnection->QueueRelationship(tcp_packet, tcp_index, article_id, RELATIONSHIP_FRIENDLY, 0);
	g_ServerMgr->m_SectorConnection->QueueResourceName(tcp_packet, tcp_index, article_id, name);
	g_ServerMgr->m_SectorConnection->QueueObjectLinkedEffect(tcp_packet, tcp_index, 0x07, player_id, article_id, 10018, 2, tractor_time, prospect_tick);
	g_ServerMgr->m_SectorConnection->QueueTractorComponent(tcp_packet, tcp_index, obj_pos, tractor_speed, player_id, article_id, article_effect_id, prospect_tick);

	//now kick off a thread to remove the effect
	EffectCancel *cancel_effect = new EffectCancel;
	cancel_effect->effect_id = article_id;
	cancel_effect->time_delay = tractor_time;

	_beginthread( ObjectRemoveThread, 0, (void *) (cancel_effect) );
}

void UDPClient::CreateObject(char *ch_msg, u8* tcp_packet, short &tcp_index)
{
	char name[128];
	int index = 0;
	unsigned char *msg = (unsigned char *)ch_msg;

	long game_id = ExtractLong(msg, index);
	u8 create_type = ExtractU8(msg, index);
	short base_asset = ExtractShort(msg, index);
	float scale = ExtractFloat(msg, index);
	float hsv0 = ExtractFloat(msg, index);
	float hsv1 = ExtractFloat(msg, index);
	float hsv2 = ExtractFloat(msg, index);
	long reaction = (long)ExtractU8(msg, index);
	u8 pos_type = ExtractU8(msg, index);
	float obj_pos[3];
	obj_pos[0] = ExtractFloat(msg, index);
	obj_pos[1] = ExtractFloat(msg, index);
	obj_pos[2] = ExtractFloat(msg, index);
	float obj_orientation[4];
	obj_orientation[0] = ExtractFloat(msg, index);
	obj_orientation[1] = ExtractFloat(msg, index);
	obj_orientation[2] = ExtractFloat(msg, index);
	obj_orientation[3] = ExtractFloat(msg, index);
	float signature = ExtractFloat(msg, index);
	u8 sig_flags = ExtractU8(msg, index);
	ExtractDataLS(msg, name, index);

	if (!g_ServerMgr->m_SectorConnection) return;

	//now construct the large TCP packet to send the whole object in one go

	if (tcp_packet == 0)
	{
		tcp_index = 0;
		tcp_packet = m_QueueBuffer;
	}

	g_ServerMgr->m_SectorConnection->QueueObjectCreate(tcp_packet, tcp_index, game_id, scale, base_asset, create_type, hsv0, hsv1, hsv2);
	g_ServerMgr->m_SectorConnection->QueueRelationship(tcp_packet, tcp_index, game_id, reaction, 0);
	g_ServerMgr->m_SectorConnection->QueuePosition(tcp_packet, tcp_index, game_id, obj_pos, obj_orientation);
	g_ServerMgr->m_SectorConnection->QueueAuxPacket(tcp_packet, tcp_index, game_id, name, signature, create_type, sig_flags);
}

void UDPClient::CreateResource(char *ch_msg, u8* tcp_packet, short &tcp_index)
{
	char name[128];
	int index = 0;
	unsigned char *msg = (unsigned char *)ch_msg;

	long game_id = ExtractLong(msg, index);
	short base_asset = ExtractShort(msg, index);
	float scale = ExtractFloat(msg, index);
	float hsv0 = ExtractFloat(msg, index);
	//float hsv1 = ExtractFloat(msg, index);
	//float hsv2 = ExtractFloat(msg, index);
	float obj_pos[3];
	obj_pos[0] = ExtractFloat(msg, index);
	obj_pos[1] = ExtractFloat(msg, index);
	obj_pos[2] = ExtractFloat(msg, index);
	float obj_orientation[4];
	obj_orientation[0] = ExtractFloat(msg, index);
	obj_orientation[1] = ExtractFloat(msg, index);
	obj_orientation[2] = ExtractFloat(msg, index);
	obj_orientation[3] = ExtractFloat(msg, index);
	ExtractDataLS(msg, name, index);

	if (!g_ServerMgr->m_SectorConnection) return;

	//now construct the large TCP packet to send the whole object in one go

	if (tcp_packet == 0)
	{
		tcp_index = 0;
		tcp_packet = m_QueueBuffer;
	}

	g_ServerMgr->m_SectorConnection->QueueObjectCreate(tcp_packet, tcp_index, game_id, scale, base_asset, 38, hsv0, 0.0f, 0.0f);
	g_ServerMgr->m_SectorConnection->QueueRelationship(tcp_packet, tcp_index, game_id, 2, 0);
	g_ServerMgr->m_SectorConnection->QueuePosition(tcp_packet, tcp_index, game_id, obj_pos, obj_orientation);
	g_ServerMgr->m_SectorConnection->QueueAuxPacket(tcp_packet, tcp_index, game_id, name, 0, 38, 0);
}

void UDPClient::HandleStageConfirm(char *ch_msg, u8* tcp_packet, short &tcp_index)
{
	//get the stage number
	int index = 0;
	unsigned char *msg = (unsigned char *)ch_msg;
	long stage_id = ExtractLong(msg, index);

	LogVMessage("Confirm stage %d\n", stage_id);

	//send the stage back to the server
	g_ServerMgr->m_UDPConnection->ForwardClientOpcode(ENB_OPCODE_2021_LOGIN_STAGE_ACK_C_S, sizeof(stage_id), (char *) &stage_id);
}

void UDPClient::SendCommsAlive()
{
	SendResponse(m_ClientPort, ENB_OPCODE_3005_PLAYER_COMMS_ALIVE, 0, 0);
}

void __cdecl ObjectRemoveThread(void *Param)
{
	EffectCancel* effect = reinterpret_cast<EffectCancel*>( Param );

	if (effect->time_delay < 200) effect->time_delay = 200;

	Sleep(effect->time_delay);

	if (g_ServerMgr->m_SectorConnection)
	{
		g_ServerMgr->m_SectorConnection->RemoveObject(effect->effect_id);
	}

	delete effect;
	_endthread(); //also kills the thread handle
}

void __cdecl EffectRemoveThread(void *Param)
{
	EffectCancel* effect = reinterpret_cast<EffectCancel*>( Param );

	if (effect->time_delay < 200) effect->time_delay = 200;

	Sleep(effect->time_delay);

	if (g_ServerMgr->m_SectorConnection)
	{
		g_ServerMgr->m_SectorConnection->SendRemoveEffect(effect->effect_id);
	}

	delete effect;
	_endthread(); //also kills the thread handle
}

void __cdecl ShutdownThread(void *Param)
{
	Sleep(1000);

	g_ServerShutdown = true;
	ShutdownClient();

	_endthread(); //also kills the thread handle
}