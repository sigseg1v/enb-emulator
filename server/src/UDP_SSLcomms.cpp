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

void UDP_Connection::HandleSSLregister(char *buffer, long addr, short port)
{
	unsigned char *ip = (unsigned char *) &addr;
	long Net7SSL_version = *((long *) &buffer[0]);

	if (m_SSLIPAddr != 0 && m_SSLIPAddr != addr)
	{
		LogMessage("Second attempt to log an SSL server. From %d.%d.%d.%d\n", ip[0], ip[1], ip[2], ip[3]);
		return;
	}

	m_SSLIPAddr = addr;
	m_SSLPort = port;

	long max_player_count = MAX_ONLINE_PLAYERS;

	LogMessage("Received Net7SSL startup -- Version %d SSL IP addr is %d.%d.%d.%d\n", Net7SSL_version, ip[0], ip[1], ip[2], ip[3]);
	SendOpcode(ENB_OPCODE_4001_SSL_REGISTER_S_SSL, (unsigned char *) &max_player_count, sizeof(max_player_count), addr, port);
}

void UDP_Connection::SendPlayerCount()
{
	if (m_SSLIPAddr == 0) return;
	long player_count = g_ServerMgr->m_GlobMemMgr->GetPlayerCount();
	SendOpcode(ENB_OPCODE_4002_SSL_PLAYERCOUNT, (unsigned char *) &player_count, sizeof(player_count), m_SSLIPAddr, m_SSLPort);
}

void UDP_Connection::HandleSSLLogin(char *buffer, long addr, short port)
{
    char account_name[64];
    int index = 0;

	long avatar_id = ExtractLong((unsigned char*)buffer, index);
	long player_addr = ExtractLong((unsigned char*)buffer, index);
	short slot = ExtractShort((unsigned char*)buffer, index);
    ExtractDataLS((unsigned char*)buffer, account_name, index);

	if (addr != m_SSLIPAddr)
	{
		unsigned char *ip = (unsigned char *) &addr;
		LogMessage("!! Bad player Auth attempt from %d.%d.%d.%d; AvatarID = %d\n", ip[0], ip[1], ip[2], ip[3]);
		return;
	}
	
	Player *player = g_ServerMgr->m_GlobMemMgr->GetPlayerNode(0);

    if (player)
    {
		player->SetCharacterID(avatar_id);
		player->SetCharacterSlot((long)slot);
		g_PlayerMgr->SetupPlayer(player, player_addr);
		player->SetAccountUsername(account_name);
		player->SetGameID(player->CharacterID() | PLAYER_TAG);
		player->SetPlayerPortIP(0, 0);
		LogMessage("Received Net7SSL login for avatar %d\n", avatar_id);
	}

	SendOpcode(ENB_OPCODE_4004_SSL_AVATARCONFIRM_S_SSL, (unsigned char *) &avatar_id, sizeof(avatar_id), addr, port);
}

void UDP_Connection::HandlePlayerCountRQ(char *buffer, long addr, short port)
{
	unsigned char *ip = (unsigned char *) &addr;

	long max_player_count = MAX_ONLINE_PLAYERS;
	long player_count = g_ServerMgr->m_GlobMemMgr->GetPlayerCount();

	unsigned char data[32];
	int index = 0;
	memset(data, 0, sizeof(buffer));

	AddData(data, player_count, index);
	AddData(data, max_player_count, index);

	LogMessage("Received player count query IP addr is %d.%d.%d.%d\n", ip[0], ip[1], ip[2], ip[3]);
	SendOpcode(ENB_OPCODE_5001_RETURN_PLAYER_COUNT, data, index, addr, port);
}