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
#include "AuxSubCategories.h"

void AuxSubCategories::BuildPacket(unsigned char *buffer, long &index, bool TwoBitFlags)
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

	for (int i=0;i<5;i++)
	{
		if (CheckFlagBit(i))
		{
			SubCategory[i].BuildPacket(buffer, index, TwoBitFlags);
		}
		else if (TwoBitFlags && CheckExtendedFlagBit2(i))
		{
			AddData(buffer, char(0x05), index);
		}
	}

	memset(Flags,0,sizeof(Flags));
}

/******************************
*         GET METHODS         *
******************************/

_SubCategories * AuxSubCategories::GetData()    {return Data;}

/******************************
*         SET METHODS         *
******************************/

void AuxSubCategories::SetData(_SubCategories *NewData)
{
	for (int i=0;i<5;i++)
	{
		SubCategory[i].SetData(&NewData->SubCategory[i]);
	}

    CheckData();
}

/******************************
*     PARENT FLAG METHODS     *
******************************/

void AuxSubCategories::CheckData()
{
	SetParentExtendedFlag(HasData());
}

int AuxSubCategories::HasData()
{
	return (ExtendedFlags[0] & 0xF0 || ExtendedFlags[1] & 0x01);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxSubCategories::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));

	for (int i=0;i<5;i++)
	{
        SubCategory[i].ClearFlags();
	}
}