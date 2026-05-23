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
#include "AuxKnownFormula.h"

void AuxKnownFormula::BuildPacket(unsigned char *buffer, long &index, bool TwoBitFlags)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	if (Flags[0] & 0x10)	//ExtendedFlags[0] & 0x80
	{
		AddString(buffer, Data->ItemName, index);
	}

	if (Flags[0] & 0x20)	//ExtendedFlags[1] & 0x01
	{
		AddData(buffer, Data->ItemID, index);
	}

	if (Flags[0] & 0x40)	//ExtendedFlags[1] & 0x02
	{
		AddData(buffer, Data->TechLevel, index);
	}

	memset(Flags,0,sizeof(Flags));
}

/******************************
*         GET METHODS         *
******************************/

_KnownFormula * AuxKnownFormula::GetData()			{return Data;}

char * AuxKnownFormula::GetItemName()               {return Data->ItemName;}
u32 AuxKnownFormula::GetItemID()                    {return Data->ItemID;}
u32 AuxKnownFormula::GetTechLevel()                 {return Data->TechLevel;}

/******************************
*         SET METHODS         *
******************************/

void AuxKnownFormula::SetData(_KnownFormula * NewData)
{
	ReplaceString(Data->ItemName, NewData->ItemName, 0, 52);
	ReplaceData(Data->ItemID, NewData->ItemID, 1);
	ReplaceData(Data->TechLevel, NewData->TechLevel, 2);
}

void AuxKnownFormula::SetItemName(char * NewItemName)
{
	ReplaceString(Data->ItemName, NewItemName, 0, 52);
}

void AuxKnownFormula::SetItemID(u32 NewItemID)
{
	ReplaceData(Data->ItemID, NewItemID, 1);
}

void AuxKnownFormula::SetTechLevel(u32 NewTechLevel)
{
	ReplaceData(Data->TechLevel, NewTechLevel, 2);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxKnownFormula::ClearFlags()
{
    memset(Flags, 0, sizeof(Flags));
}
