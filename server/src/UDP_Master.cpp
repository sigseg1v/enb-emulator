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

#include "ServerManager.h"
#include "UDPConnection.h"
#include <net7/Opcodes.h>
#include <net7/PacketStructures.h>
#include "PacketMethods.h"
#include "PlayerClass.h"
#include "MemoryHandler.h"

#define ENB_OPCODE_2008_MASTER_HANDOFF                  0x2008
#define ENB_OPCODE_2009_MASTER_HANDOFF_CONFIRM          0x2009

void UDP_Connection::HandleMasterOpcode(char *msg, EnbUdpHeader *hdr, const long source_addr, const short source_port)
{
    switch (hdr->opcode)
    {
    case ENB_OPCODE_2008_MASTER_HANDOFF:
        ProcessHandoff(msg, hdr, source_addr, source_port);
        break;

    default:					
        LogMessage("[UDP] bad Master opcode, id 0x%04X\n",hdr->opcode);
        break;
    }   
}

void UDP_Connection::ProcessHandoff(char *msg, EnbUdpHeader *hdr, const long source_addr, const short source_port)
{
    unsigned char data[32],*ip;
    int index = 0;
    // Wire format: bytes 0-3 sector_id, byte 4 packet_opt. Original code
    // read sizeof(long)=8 bytes for sector_id (would over-read 3 bytes on
    // Linux x86_64); ntohl masked it because it only consumes the low 32 bits.
    int32_t sector_id = *((int32_t*) msg);
	u8 packet_opt = *((u8*) &msg[4]);
    Player *player = m_ServerMgr->m_PlayerMgr.GetPlayer(hdr->player_id);

    if (player)
	{
        ServerRedirect redirect;
        memset(&redirect, 0, sizeof(redirect));
        redirect.sector_id = ntohl(sector_id);
		ip = (unsigned char *)&source_addr;
		LogMessage("[UDP port:%u IP:%d.%d.%d.%d] Master handoff player %s [%08x], to sector %d\n", source_port, ip[0], ip[1], ip[2], ip[3], player->Name(), player->GameID(), sector_id);
        if (m_ServerMgr->m_SectorServerMgr.LookupSectorServer(redirect))
        {
            //LogMessage("Found sector %d\n",sector_id);
            //build response, ip addr of sector and sector port
            AddData(data, redirect.ip_address, index);
            AddData(data, redirect.port, index);
			long game_id = player->GameID();
			AddData(data, game_id, index);
            SendOpcode(ENB_OPCODE_2009_MASTER_HANDOFF_CONFIRM, player, data, index, source_addr, source_port);
            player->SetUDPConnection(g_ServerMgr->m_UDPConnection);
            player->SetHandoffReceived(true);
			if (packet_opt > 0)
			{
				player->HandlePacketOptRequest("lac"); //launcher activated, no feedback
				LogMessage("Player requested packetopt from launcher.\n");
			}
        }
        else
        {
            LogMessage("[UDP] Unable to locate sector server for sector %d\n", sector_id);
        }
    }
    else
    {
        LogMessage("[UDP] SERVER ERROR: Unable to find player [%x]\n", hdr->player_id);
		//send msg to proxy to terminate client
		// Phase K: wire expects 4-byte game id; sizeof(long) was 8 on Linux
		// x86_64, blowing the packet length by 4 bytes.
		int32_t player_id = 0;
		SendOpcode(ENB_OPCODE_100A_MVAS_TERMINATE_S_C, (unsigned char *) &player_id, sizeof(player_id), source_addr, source_port);
    }
}

