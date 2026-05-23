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
#include "AuxGroupMember.h"

void AuxGroupMember::BuildPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	if (Flags[0] & 0x10)	//ExtendedFlags[1] & 0x01
	{
		AddString(buffer, Data->Name, index);
	}

	if (Flags[0] & 0x20)	//ExtendedFlags[1] & 0x02
	{
		AddData(buffer, Data->GameID, index);
	}

	if (Flags[0] & 0x40)	//ExtendedFlags[1] & 0x04
	{
		AddData(buffer, Data->Formation, index);
	}

	if (Flags[0] & 0x80)	//ExtendedFlags[1] & 0x08
	{
		AddData(buffer, Data->Position, index);
	}

	memset(Flags,0,sizeof(Flags));
}

void AuxGroupMember::BuildExtendedPacket(unsigned char *buffer, long &index)
{
	AddFlags(ExtendedFlags, sizeof(ExtendedFlags), buffer, index);

	if (ExtendedFlags[0] & 0x10)	//ExtendedFlags[1] & 0x01
	{
		AddString(buffer, Data->Name, index);
	}

	if (ExtendedFlags[0] & 0x20)	//ExtendedFlags[1] & 0x02
	{
		AddData(buffer, Data->GameID, index);
	}

	if (ExtendedFlags[0] & 0x40)	//ExtendedFlags[1] & 0x04
	{
		AddData(buffer, Data->Formation, index);
	}

	if (ExtendedFlags[0] & 0x80)	//ExtendedFlags[1] & 0x08
	{
		AddData(buffer, Data->Position, index);
	}
}

/******************************
*         GET METHODS         *
******************************/

_GroupMember * AuxGroupMember::GetData()		{return Data;}

char * AuxGroupMember::GetName()				{return Data->Name;}
u32 AuxGroupMember::GetGameID()					{return Data->GameID;}
u32 AuxGroupMember::GetFormation()				{return Data->Formation;}
u32 AuxGroupMember::GetPosition()				{return Data->Position;}

/******************************
*         SET METHODS         *
******************************/

void AuxGroupMember::SetData(_GroupMember *NewData)
{
	ReplaceString(Data->Name, NewData->Name, 0, 64);
	ReplaceData(&Data->GameID, NewData->GameID, 1);
	ReplaceData(&Data->Formation, NewData->Formation, 2);
	ReplaceData(&Data->Position, NewData->Position, 3);

	CheckData();
}

void AuxGroupMember::SetName(char * NewName)
{
	ReplaceString(Data->Name, NewName, 0, 64);
	CheckData();
}

void AuxGroupMember::SetGameID(u32 NewGameID)
{
	ReplaceData(&Data->GameID, NewGameID, 1);
	CheckData();
}

void AuxGroupMember::SetFormation(u32 NewFormation)
{
	ReplaceData(&Data->Formation, NewFormation, 2);
	CheckData();
}

void AuxGroupMember::SetPosition(u32 NewPosition)
{
	ReplaceData(&Data->Position, NewPosition, 3);
	CheckData();
}

/******************************
*     PARENT FLAG METHODS     *
******************************/

void AuxGroupMember::CheckData()
{
	SetParentExtendedFlag(HasData());
}

int AuxGroupMember::HasData()
{
	return (ExtendedFlags[0] & 0xF0);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxGroupMember::Clear()
{
    _GroupMember ClearData;

	*ClearData.Name = 0; 
	ClearData.GameID = 0;
	ClearData.Formation = 0;
	ClearData.Position = 0;

    SetData(&ClearData);
}

void AuxGroupMember::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));
}