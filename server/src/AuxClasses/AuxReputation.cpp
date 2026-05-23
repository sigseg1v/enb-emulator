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
#include "AuxReputation.h"

void AuxReputation::BuildPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	if (Flags[0] & 0x10)	//ExtendedFlags[0] & 0x40
	{
		Factions.BuildPacket(buffer, index);
	}

	if (Flags[0] & 0x20)	//ExtendedFlags[0] & 0x80
	{
		AddString(buffer, Data->Affiliation, index);
	}

	memset(Flags,0,sizeof(Flags));
}

void AuxReputation::BuildExtendedPacket(unsigned char *buffer, long &index)
{
	AddFlags(ExtendedFlags, sizeof(ExtendedFlags), buffer, index);

	if (ExtendedFlags[0] & 0x10)	//ExtendedFlags[0] & 0x40
	{
		Factions.BuildExtendedPacket(buffer, index);
	}
	else if (ExtendedFlags[0] & 0x40)
	{
		AddData(buffer, char(0x05), index);
	}

	if (ExtendedFlags[0] & 0x20)	//ExtendedFlags[0] & 0x80
	{
		AddString(buffer, Data->Affiliation, index);
	}
}

/******************************
*         GET METHODS         *
******************************/

_Reputation * AuxReputation::GetData()      {return Data;}

char * AuxReputation::GetAffiliation()		{return Data->Affiliation;}

/******************************
*         SET METHODS         *
******************************/

void AuxReputation::SetData(_Reputation *NewData)
{
	Factions.SetData(&NewData->Factions);
	ReplaceString(Data->Affiliation, NewData->Affiliation, 1, 64);
}

void AuxReputation::SetAffilitation(char * NewAffiliation)
{
	ReplaceString(Data->Affiliation, NewAffiliation, 1, 64);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxReputation::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));
    Factions.ClearFlags();
}