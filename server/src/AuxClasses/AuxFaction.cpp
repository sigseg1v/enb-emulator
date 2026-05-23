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
#include "AuxFaction.h"

void AuxFaction::BuildPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	if (Flags[0] & 0x10)	//ExtendedFlags[0] & 0x80
	{
		AddString(buffer, Data->Name, index);
	}

	if (Flags[0] & 0x20)	//ExtendedFlags[1] & 0x01
	{
		AddData(buffer, Data->Reaction, index);
	}

	if (Flags[0] & 0x40)	//ExtendedFlags[1] & 0x02
	{
		AddData(buffer, Data->Order, index);
	}

	memset(Flags,0,sizeof(Flags));
}

void AuxFaction::BuildExtendedPacket(unsigned char *buffer, long &index)
{
	AddFlags(ExtendedFlags, sizeof(ExtendedFlags), buffer, index);

	if (ExtendedFlags[0] & 0x10)	//ExtendedFlags[0] & 0x80
	{
		AddString(buffer, Data->Name, index);
	}

	if (ExtendedFlags[0] & 0x20)	//ExtendedFlags[1] & 0x01
	{
		AddData(buffer, Data->Reaction, index);
	}

	if (ExtendedFlags[0] & 0x40)	//ExtendedFlags[1] & 0x02
	{
		AddData(buffer, Data->Order, index);
	}
}

/******************************
*         GET METHODS         *
******************************/

_Faction * AuxFaction::GetData()		{return Data;}

char * AuxFaction::GetName()			{return Data->Name;}
float AuxFaction::GetReaction()			{return Data->Reaction;}
u32 AuxFaction::GetOrder()				{return Data->Order;}

/******************************
*         SET METHODS         *
******************************/

void AuxFaction::SetData(_Faction *NewData)
{
	ReplaceString(Data->Name, NewData->Name, 0,64);
	ReplaceData(Data->Reaction, NewData->Reaction, 1);
	ReplaceData(Data->Order, NewData->Order, 2);

	CheckData();
}

void AuxFaction::SetName(char * NewName)
{
	ReplaceString(Data->Name, NewName, 0,64);
	CheckData();
}

void AuxFaction::SetReaction(float NewReaction)
{
	ReplaceData(Data->Reaction, NewReaction, 1);
	CheckData();
}

void AuxFaction::SetOrder(u32 NewOrder)
{
	ReplaceData(Data->Order, NewOrder, 2);
	CheckData();
}

/******************************
*     PARENT FLAG METHODS     *
******************************/

void AuxFaction::CheckData()
{
	SetParentExtendedFlag(HasData());
}

int AuxFaction::HasData()
{
	return (ExtendedFlags[0] & 0x70);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxFaction::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));
}