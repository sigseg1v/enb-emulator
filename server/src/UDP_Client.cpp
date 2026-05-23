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
#include "PacketMethods.h"
#include "PlayerClass.h"
#include "MemoryHandler.h"

#define ENB_OPCODE_2006_SECTOR_VALIDATE                 0x2006
#define ENB_OPCODE_2007_SECTOR_VALID_CONFIRM            0x2007


void UDP_Connection::HandleClientOpcode(char *msg, EnbUdpHeader *hdr, const long source_addr, const short source_port)
{
    if (m_Sector_Operational)
    {
        ProcessClientOpcode(msg, hdr, source_addr, source_port);
    }
    else
    {
        ProcessServerValidation(msg, hdr, source_addr, source_port);
    }
}

void UDP_Connection::ProcessServerValidation(char *msg, EnbUdpHeader *hdr, const long source_addr, const short source_port)
{
    switch (hdr->opcode)
    {
    case ENB_OPCODE_2006_SECTOR_VALIDATE:
        //msg should hold the sectorID
        m_SectorID = *((long*) msg);
        if (m_ServerMgr->SetupSectorServer(m_SectorID))
        {
            // This is no longer a Client to Sector Server connection
            // This is now a Master Server to Sector Server connection
            m_Sector_Operational = true;
            m_ServerMgr->SetSectorServerReady(m_SectorID, true);
            LogMessage("Port: %d, Sector: %d '%s'\n", m_Port, m_SectorID, g_ServerMgr->GetSectorName(m_SectorID));
        }
        break;
        
    default:					
        LogMessage("bad Sector activate UDP opcode, id 0x%04X\n",hdr->opcode);
        break;
    }   
}

void UDP_Connection::ProcessClientOpcode(char *msg, EnbUdpHeader *hdr, const long source_addr, const short source_port)
{
    Player *player = m_ServerMgr->m_PlayerMgr.GetPlayer(hdr->player_id);

    if (player && player->PlayerIPAddr() == source_addr)
	{
        switch (hdr->opcode)
        {
        case ENB_OPCODE_2006_SECTOR_VALIDATE:
            LogMessage("BAD Sector validation... Attempt to validate already active sector  Port: %d, Sector: %d\n", m_Port, m_SectorID);
            break;

        case ENB_OPCODE_3005_PLAYER_COMMS_ALIVE:
            HandleKeepCommsAlive(hdr, source_addr, source_port);
            break;

        case ENB_OPCODE_3006_PLAYER_LOGIN_FAILED:
            HandleLoginFailed(player);
            break;
            
        default:
            //This should be a raw client->server opcode
            
            //LogMessage("Received an opcode from the client, opcode = %x, length = %d, playerID = %x\n", hdr->opcode, hdr->size - sizeof(EnbUdpHeader), hdr->player_id);
            player->AddMessage(hdr->opcode, hdr->size, (unsigned char*)msg);        
            break;
        }   
    }
	else if (player)
	{
		LogMessage("Player IP mismatch [%s]: %x %x\n", player->Name(), source_addr, player->PlayerIPAddr());
	}
}

void UDP_Connection::HandleLoginFailed(Player *p)
{
    LogMessage("Handle login fail\n");
    long sector_id = p->PlayerIndex()->GetSectorNum();
	if (sector_id == 0) sector_id = 10711; //Net-7 Station.

	p->SetActive(false);
	g_PlayerMgr->DropPlayerFromGalaxy(p);
}
