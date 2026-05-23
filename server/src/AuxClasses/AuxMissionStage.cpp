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
#include "AuxMissionStage.h"

void AuxMissionStage::BuildPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	if (Flags[0] & 0x10)	//ExtendedFlags[0] & 0x40
	{
		AddString(buffer, Data->Text, index);
	}

	if (Flags[0] & 0x20)	//ExtendedFlags[0] & 0x80
	{
		AddData(buffer, char(Data->IsTimed), index);
	}

	memset(Flags,0,sizeof(Flags));
}

void AuxMissionStage::BuildExtendedPacket(unsigned char *buffer, long &index)
{
	AddFlags(ExtendedFlags, sizeof(ExtendedFlags), buffer, index);

	if (ExtendedFlags[0] & 0x10)	//ExtendedFlags[0] & 0x40
	{
		AddString(buffer, Data->Text, index);
	}

	if (ExtendedFlags[0] & 0x20)	//ExtendedFlags[0] & 0x80
	{
		AddData(buffer, char(Data->IsTimed), index);
	}
}

/******************************
*         GET METHODS         *
******************************/

_MissionStage * AuxMissionStage::GetData()			{return Data;}

char * AuxMissionStage::GetText()					{return Data->Text;}
bool AuxMissionStage::GetIsTimed()					{return Data->IsTimed;}

/******************************
*         SET METHODS         *
******************************/

void AuxMissionStage::SetData(_MissionStage *NewData)
{
	ReplaceString(Data->Text, NewData->Text, 0, sizeof(Data->Text));
	ReplaceData(&Data->IsTimed, NewData->IsTimed, 1);

	CheckData();
}

void AuxMissionStage::SetText(char * NewText)
{
	ReplaceString(Data->Text, NewText, 0, sizeof(Data->Text));
	CheckData();
}

void AuxMissionStage::SetIsTimed(bool NewIsTimed)
{
	ReplaceData(&Data->IsTimed, NewIsTimed, 1);
	CheckData();
}

/******************************
*     PARENT FLAG METHODS     *
******************************/

void AuxMissionStage::CheckData()
{
	SetParentExtendedFlag(HasData());
}

int AuxMissionStage::HasData()
{
	return (ExtendedFlags[0] & 0x30);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxMissionStage::SetAllFlags()
{
	if ((Flags[0] & 0x02) == 0)
	{
		Flags[0] |= 0x02;
		SetParentFlag();
		SetParentExtendedFlag(1);
	}
	for(int x=0;x<2;x++)
	{
		// If flag should be set, then set it
		if (ExtendedFlags[(x + 4) / 8] & (1 << ((x + 4) % 8)))
		{
			Flags[(x + 4) / 8] |= (1 << ((x + 4) % 8));
		}
	}
}

_MissionStage AuxMissionStage::GetClearStruct()
{
    _MissionStage ClearData;
    
	*ClearData.Text = 0; 
	ClearData.IsTimed = 0;

    return ClearData;
}

void AuxMissionStage::Clear()
{
    SetData(&GetClearStruct());
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxMissionStage::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));
}