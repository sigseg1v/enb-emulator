// MasterServerToSectorServer.cpp
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
/******************************************************
 *   //////////////////////////////////////////////   *
 *   //  MASTER SERVER TO SECTOR SERVER OPCODES  //   *
 *   //////////////////////////////////////////////   *
 ******************************************************/

#include "Net7.h"
#include <net7/Opcodes.h>
#include "ServerManager.h"
#include <net7/PacketStructures.h>

#if 0
void Connection::ProcessMasterServerToSectorServerOpcode(short opcode, short bytes)
{
	switch (opcode)
	{
	case ENB_OPCODE_7902_CHARACTER_DATA :
		HandleCharacterData(bytes);
		break;

	case ENB_OPCODE_7802_REQUEST_CHARACTER_DATA :
		HandleRequestCharacterData();
		break;

	default :
		LogMessage("ProcessMasterServerToSectorServerOpcode -- UNRECOGNIZED OPCODE 0x%04x\n", opcode);
		break;
	}
}

void Connection::SendSectorAssignment(long sector_id)
{
	SendResponse(ENB_OPCODE_7801_SECTOR_ASSIGNMENT, (unsigned char *) &sector_id, sizeof(sector_id));
}

void Connection::HandleRequestCharacterData()
{
	//LogMessage("Received DatabaseRequest packet\n");

	if (g_ServerMgr->m_IsMasterServer || g_ServerMgr->m_IsStandaloneServer)
	{
		SendCharacterData();
	}
}

void Connection::SendCharacterData()
{
	long response = 0;

	//LogMessage("Sending DatabaseResponse packet\n");

	SendResponse(ENB_OPCODE_7902_CHARACTER_DATA, (unsigned char *) &response, sizeof(response));
}

void Connection::HandleCharacterData(short bytes)
{
	// Are we a Sector Server?
	if (g_ServerMgr->m_IsStandaloneServer || !g_ServerMgr->m_IsMasterServer)
	{
		// Yes, store the character data
		if (bytes == sizeof(CharacterDatabase))
		{
			CharacterDatabase * database = (CharacterDatabase *) m_RecvBuffer;
			//g_ServerMgr->StoreCharacterData(*database);
		}
	}
}

#endif
