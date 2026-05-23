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
#include "AuxInventory1.h"

void AuxInventory1::BuildPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	if (Flags[0] & 0x10)
	{
	    Item[0].BuildPacket(buffer, index);
	}

	memset(Flags,0,sizeof(Flags));
}

void AuxInventory1::BuildExtendedPacket(unsigned char *buffer, long &index)
{
	AddFlags(ExtendedFlags, sizeof(ExtendedFlags), buffer, index);

	if (ExtendedFlags[0] & 0x10)
	{
	    Item[0].BuildExtendedPacket(buffer, index);
	}
	else if (ExtendedFlags[0] & 0x20)
	{
		AddData(buffer, char(0x05), index);
	}
}

/******************************
*         GET METHODS         *
******************************/

_Inventory1 * AuxInventory1::GetData()      {return Data;}

/******************************
*         SET METHODS         *
******************************/

void AuxInventory1::SetData(_Inventory1 *NewData)
{
	Item[0].SetData(&NewData->Item[0]);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxInventory1::Empty()
{
	Item[0].Empty();
}

void AuxInventory1::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));
    Item[0].ClearFlags();
}