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
#include "AuxPrimaryCategories.h"

void AuxPrimaryCategories::BuildPacket(unsigned char *buffer, long &index, bool TwoBitFlags)
{
	if (TwoBitFlags)
	{
		AddFlags(ExtendedFlags, sizeof(ExtendedFlags), buffer, index);
		memcpy(Flags, ExtendedFlags, sizeof(Flags));
	}
	else
	{
		AddFlags(Flags, sizeof(Flags), buffer, index);
	}

	if (Flags[0] & 0x10)	//ExtendedFlags[0] & 0x40
	{
		PrimaryCategory[0].BuildPacket(buffer, index, TwoBitFlags);
	}
	else if (TwoBitFlags && ExtendedFlags[0] & 0x40)
	{
		AddData(buffer, char(0x05), index);
	}

	if (Flags[0] & 0x20)	//ExtendedFlags[0] & 0x80
	{
		PrimaryCategory[1].BuildPacket(buffer, index, TwoBitFlags);
	}
	else if (TwoBitFlags && ExtendedFlags[0] & 0x80)
	{
		AddData(buffer, char(0x05), index);
	}

	memset(Flags,0,sizeof(Flags));
}

/******************************
*         GET METHODS         *
******************************/

_PrimaryCategories * AuxPrimaryCategories::GetData()    {return Data;}

/******************************
*         SET METHODS         *
******************************/

void AuxPrimaryCategories::SetData(_PrimaryCategories *NewData)
{
	PrimaryCategory[0].SetData(&NewData->PrimaryCategory[0]);
	PrimaryCategory[1].SetData(&NewData->PrimaryCategory[1]);

    CheckData();
}

/******************************
*     PARENT FLAG METHODS     *
******************************/

void AuxPrimaryCategories::CheckData()
{
	SetParentExtendedFlag(HasData());
}

int AuxPrimaryCategories::HasData()
{
	return (ExtendedFlags[0] & 0x30);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxPrimaryCategories::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));

	for (int i=0;i<2;i++)
	{
        PrimaryCategory[i].ClearFlags();
	}
}