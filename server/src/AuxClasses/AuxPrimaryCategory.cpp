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
#include "AuxPrimaryCategory.h"

void AuxPrimaryCategory::BuildPacket(unsigned char *buffer, long &index, bool TwoBitFlags)
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
		AddString(buffer, Data->Name, index);
	}

	if (Flags[0] & 0x20)	//ExtendedFlags[0] & 0x80
	{
		Categories.BuildPacket(buffer, index, TwoBitFlags);
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

_PrimaryCategory * AuxPrimaryCategory::GetData()    {return Data;}

char * AuxPrimaryCategory::GetName()                {return Data->Name;}

/******************************
*         SET METHODS         *
******************************/

void AuxPrimaryCategory::SetData(_PrimaryCategory * NewData)
{
	ReplaceString(Data->Name, NewData->Name, 0, 20);
    Categories.SetData(&NewData->Categories);

    CheckData();
}

void AuxPrimaryCategory::SetName(char * NewName)
{
	ReplaceString(Data->Name, NewName, 0, 20);
    CheckData();
}

/******************************
*     PARENT FLAG METHODS     *
******************************/

void AuxPrimaryCategory::CheckData()
{
	SetParentExtendedFlag(HasData());
}

int AuxPrimaryCategory::HasData()
{
	return (ExtendedFlags[0] & 0x30);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxPrimaryCategory::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));
    Categories.ClearFlags();
}