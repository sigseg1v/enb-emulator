// SectorServerToSectorServer.cpp
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
 *   //  SECTOR SERVER TO SECTOR SERVER OPCODES  //   *
 *   //////////////////////////////////////////////   *
 ******************************************************/
#if 0
#include "Net7.h"
#include "Connection.h"
#include <net7/Opcodes.h>
#include "ServerManager.h"
#include "PacketStructures.h"

void Connection::ProcessSectorServerToSectorServerOpcode(short opcode, short bytes)
{
	switch (opcode)
	{
	case ENB_OPCODE_7802_REQUEST_CHARACTER_DATA :
		SendCharacterData();
		break;

	default :
		LogMessage("ProcessSectorServerToSectorServerOpcode - UNRECOGNIZED OPCODE 0x%04x\n", opcode);
		break;
	}
}

#endif