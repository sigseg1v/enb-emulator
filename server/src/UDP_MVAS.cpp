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

#include "ServerManager.h"
#include "UDPConnection.h"
#include "Opcodes.h"
#include "PacketStructures.h"
#include "PlayerClass.h"

#define CURRENT_MVAS 174

void UDP_Connection::HandleMVASOpcode(char *msg, EnbUdpHeader *hdr, const long source_addr, const short source_port)
{
    switch (hdr->opcode)
    {
    case ENB_OPCODE_1000_MVAS_REGISTER_C_S:
        HandleMoveAssistRegister(msg, source_addr, source_port);
        break;
        
        //we received a position opcode from someone.
    case ENB_OPCODE_1004_MVAS_SEND_POSITION_C_S:
        HandleMVASPosReturn(msg, hdr, source_addr, source_port);
        break;
        
    case ENB_OPCODE_1008_MVAS_LOGOFF_C_S:
        //remove from player list
        HandleLogout(msg, source_addr);
        break;

    case ENB_OPCODE_200F_COMM_PORT:
        HandleCommReturn(msg, hdr, source_addr, source_port);
        break;

    case ENB_OPCODE_3001_AUX_RESPONSE:
        HandleLoungeReady(hdr);
        break;

    case ENB_OPCODE_3005_PLAYER_COMMS_ALIVE:
        HandleKeepCommsAlive(hdr, source_addr, source_port);
        break;
	
	case ENB_OPCODE_4000_SSL_REGISTER_SSL_S:
		HandleSSLregister(msg, source_addr, source_port);
		break;

	case ENB_OPCODE_4003_SSL_AVATARLOGIN_SSL_S:
		//handoff from Net7SSL - avatar in logging in.
		HandleSSLLogin(msg, source_addr, source_port);
		break;

	case ENB_OPCODE_5000_GET_PLAYER_COUNT:
		HandlePlayerCountRQ(msg, source_addr, source_port);
		break;

    default:					
        LogMessage("bad MVAS-UDP opcode, id 0x%04X\n",hdr->opcode);
        break;
    }   
}

void UDP_Connection::HandleLogout(char *buffer, const long IPaddr)
{
    long player_index;
    //connection_node *player_connection;
    Player *p;

    player_index = *((long *) &buffer[0]);

    p = m_ServerMgr->m_PlayerMgr.GetPlayer(player_index);

    if (p)
    {
        LogMessage("Force logout received from player '%s': 0x%08x\n", p->Name(), p->GameID());
		g_PlayerMgr->LeaveGroup(p->GroupID(), p->GameID());
        g_PlayerMgr->DropPlayerFromGalaxy(p);        
    }
}

void UDP_Connection::HandleCommReturn(char *msg, EnbUdpHeader *hdr, const long source_addr, const short source_port)
{
    Player *player = m_ServerMgr->m_PlayerMgr.GetPlayer(hdr->player_id);

    if (player)
    {
        //LogMessage("Comms port for '%s' confirmed: %d.\n", player->Name(), (unsigned long)source_port);
        player->SetPlayerPortIP(source_port, source_addr);
    }
}

void UDP_Connection::HandleLoungeReady(EnbUdpHeader *hdr)
{
    Player *player = m_ServerMgr->m_PlayerMgr.GetPlayer(hdr->player_id);

    if (player)
    {
        LogMessage("Player '%s' ready for lounge NPC.\n", player->Name());
        player->SetLoungeReady();
    }
}

void UDP_Connection::HandleMVASPosReturn(char *buffer, EnbUdpHeader *hdr, long addr, short port)
{
	float position[3];
    float heading[3];
	u32 frequency;
    bool heading_data = false;
	position[0] = *((float *) &buffer[0]);
	position[1] = *((float *) &buffer[4]);
	position[2] = *((float *) &buffer[8]);

    if (hdr->size > 28)
    {
        heading_data = true;
        heading[0] = *((float *) &buffer[12]);
        heading[1] = *((float *) &buffer[16]);
        heading[2] = *((float *) &buffer[20]);
    }

	//LogMessage("Got return from player %x, %.2f %.2f %.2f (%d)\n", player_index, position[0], position[1], position[2], bytes);
    /*if (heading_data)
    {
        LogMessage("Heading feed: %.5f, %.5f, %.5f\n", heading[0], heading[1], heading[2]);
    }*/

    //match player index
    Player *p = m_ServerMgr->m_PlayerMgr.GetPlayer(hdr->player_id);

    if (p)
    {
        p->SetMVASIndex(hdr->player_id);
        frequency = p->UpdatePositionFromMVAS(position, heading, heading_data);
		p->SetPlayerPortIP(port, addr);
       
        if (frequency != p->Frequency())
        {
            p->SetFrequency(frequency);
            SendOpcode(ENB_OPCODE_1007_MVAS_TOGGLE_SEND_FREQ_S_C, p, (unsigned char *) &frequency, sizeof(long), addr, port);
        }
    }
    else if (IS_PLAYER(hdr->player_id))
    {
        //something has gone hella wrong with the client link, best to send a terminate signal back to this port/ip and put them out of their misery
        long player_id = hdr->player_id;
        //SendOpcode(ENB_OPCODE_100A_MVAS_TERMINATE_S_C, (unsigned char *) &player_id, sizeof(long), addr, port);
        //this can interfere with normal shutdown ... better to ensure the client doesn't send any updates after logout.
    }
    else //didn't recognise player, they are probably still in the character selection
    {
        unsigned char *ip = (unsigned char *) &addr;
        LogMessage("Unable to find player with IP addr %d.%d.%d.%d.\n", ip[0], ip[1], ip[2], ip[3]);
        frequency = 20;
        SendOpcode(ENB_OPCODE_1007_MVAS_TOGGLE_SEND_FREQ_S_C, (unsigned char *) &frequency, sizeof(long), addr, port);
    }
}

void UDP_Connection::HandleMoveAssistRegister(char *buffer, long addr, short port)
{
	MVASHandoff handoff;

	handoff.port = 0;

	unsigned char *ip = (unsigned char *) &addr;
	long client_version = *((long *) &buffer[0]);

	m_LoginAttempts++;

	if (!g_ServerMgr->IsSectorAssignmentsComplete())
	{
		LogMessage("Received pre-start MVAS login [%d] -- client IP addr is %d.%d.%d.%d\n", m_LoginAttempts, ip[0], ip[1], ip[2], ip[3]);
		SendOpcode(ENB_OPCODE_100B_MVAS_PRE_START_S_C, (unsigned char *) &handoff, sizeof(handoff), addr, port);
	}
	else if (client_version >= CURRENT_MVAS) //only send confirmation if socket test is correct version
	{
        handoff.player_id = 0;//AddMVASConnection(addr, port);
        LogMessage("Received MVAS login [%d] -- client IP addr is %d.%d.%d.%d\n", m_LoginAttempts, ip[0], ip[1], ip[2], ip[3]);
		SendOpcode(ENB_OPCODE_1001_MVAS_LOGIN_S_C, (unsigned char *) &handoff, sizeof(handoff), addr, port);
		g_ServerMgr->m_UDPConnection->RegisterIP(addr); //register the IP as seen
	}
	else
	{
        float float_version = (float)client_version/100.0f;
		LogMessage("Someone is trying to log in using version %.2f of Net7Proxy. Access Denied!\n", float_version);
		g_PlayerMgr->ErrorBroadcast("Someone trying to log in using Net7Proxy v%.2f. Access Denied!\n", float_version);
        long current_net7proxy = CURRENT_MVAS;
        SendOpcode(ENB_OPCODE_1009_MVAS_BAD_LOGIN_S_C, (unsigned char *) &current_net7proxy, sizeof(current_net7proxy), addr, port);
	}
}

void UDP_Connection::HandleKeepCommsAlive(EnbUdpHeader *hdr, long addr, short port)
{
    Player *player = m_ServerMgr->m_PlayerMgr.GetPlayer(hdr->player_id);

    if (player)
    {
        LogDebug("Received Keepalive from %s.\n", player->Name());
        player->SetLastAccessTime(GetNet7TickCount());
    }
    else
    {
        //this impulse from a dead player.
        //send back a terminate signal
        SendOpcode(ENB_OPCODE_100A_MVAS_TERMINATE_S_C, (unsigned char *) &hdr->player_id, sizeof(long), addr, port);
    }
}

void UDP_Connection::HandleLoginStageAck(char *msg, EnbUdpHeader *hdr, const long source_addr, const short source_port)
{
	Player *player = m_ServerMgr->m_PlayerMgr.GetPlayer(hdr->player_id);
	
	if (player)
	{
		LogMessage("*Received login ack from %s\n", player->Name());
		player->SetLoginAck(true);
	}
}