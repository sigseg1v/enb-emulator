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
#include "AuxFactions.h"

void AuxFactions::BuildPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	for (int i=0;i<32;i++)
	{
		if (CheckFlagBit(i))
		{
			Faction[i].BuildPacket(buffer, index);
		}
	}

	memset(Flags,0,sizeof(Flags));
}

void AuxFactions::BuildExtendedPacket(unsigned char *buffer, long &index)
{
	AddFlags(ExtendedFlags, sizeof(ExtendedFlags), buffer, index);

	for (int i=0;i<32;i++)
	{
		if (CheckExtendedFlagBit1(i))
		{
			Faction[i].BuildExtendedPacket(buffer, index);
		}
		else if (CheckExtendedFlagBit2(i))
		{
			AddData(buffer, char(0x05), index);
		}
	}
}

/******************************
*         GET METHODS         *
******************************/

_Factions * AuxFactions::GetData()  {return Data;}

/******************************
*         SET METHODS         *
******************************/

void AuxFactions::SetData(_Factions *NewData)
{
	for (int i=0;i<32;i++)
	{
		Faction[i].SetData(&NewData->Faction[i]);
	}
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxFactions::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));

	for (int i=0;i<32;i++)
	{
        Faction[i].ClearFlags();
	}
}