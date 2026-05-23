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
#include "AuxAttachment.h"

void AuxAttachment::BuildPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	if (Flags[0] & 0x10)	//ExtendedFlags[1] & 0x01
	{
		AddString(buffer, Data->BoneName, index);
	}

	if (Flags[0] & 0x20)	//ExtendedFlags[1] & 0x02
	{
		AddData(buffer, Data->Type, index);
	}

	if (Flags[0] & 0x40)	//ExtendedFlags[1] & 0x04
	{
		AddData(buffer, Data->Asset, index);
	}

	if (Flags[0] & 0x80)	//ExtendedFlags[1] & 0x08
	{
		AddString(buffer, Data->DataStr, index);
	}

	memset(Flags,0,sizeof(Flags));
}

void AuxAttachment::BuildExtendedPacket(unsigned char *buffer, long &index)
{
	AddFlags(ExtendedFlags, sizeof(ExtendedFlags), buffer, index);

	if (ExtendedFlags[0] & 0x10)	//ExtendedFlags[1] & 0x01
	{
		AddString(buffer, Data->BoneName, index);
	}

	if (ExtendedFlags[0] & 0x20)	//ExtendedFlags[1] & 0x02
	{
		AddData(buffer, Data->Type, index);
	}

	if (ExtendedFlags[0] & 0x40)	//ExtendedFlags[1] & 0x04
	{
		AddData(buffer, Data->Asset, index);
	}

	if (ExtendedFlags[0] & 0x80)	//ExtendedFlags[1] & 0x08
	{
		AddString(buffer, Data->DataStr, index);
	}
}

void AuxAttachment::BuildSpecialPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	if (Flags[0] & 0x10)	//ExtendedFlags[1] & 0x01
	{
		AddString(buffer, Data->BoneName, index);
	}

	if (Flags[0] & 0x20)	//ExtendedFlags[1] & 0x02
	{
		AddData(buffer, Data->Type, index);
	}

	if (Flags[0] & 0x40)	//ExtendedFlags[1] & 0x04
	{
		AddData(buffer, Data->Asset, index);
	}

	if (Flags[0] & 0x80)	//ExtendedFlags[1] & 0x08
	{
		AddString(buffer, Data->DataStr, index);
	}
}

/******************************
*         GET METHODS         *
******************************/

_Attachment * AuxAttachment::GetData()			{return Data;}

char * AuxAttachment::GetBoneName()				{return Data->BoneName;}
u32 AuxAttachment::GetType()					{return Data->Type;}
u32 AuxAttachment::GetAsset()					{return Data->Asset;}
char * AuxAttachment::GetDataStr()				{return Data->DataStr;}

/******************************
*         SET METHODS         *
******************************/

void AuxAttachment::SetData(_Attachment *NewData)
{
	ReplaceString(Data->BoneName, NewData->BoneName, 0, 64);
	ReplaceData(&Data->Type, NewData->Type, 1);
	ReplaceData(&Data->Asset, NewData->Asset, 2);
	ReplaceString(Data->DataStr, NewData->DataStr, 3,64);

	CheckData();
}

void AuxAttachment::SetBoneName(char *NewBoneName)
{
	ReplaceString(Data->BoneName, NewBoneName, 0,64);
	CheckData();
}

void AuxAttachment::SetType(u32 NewType)
{
	ReplaceData(&Data->Type, NewType, 1);
	CheckData();
}

void AuxAttachment::SetAsset(u32 NewAsset)
{
	ReplaceData(&Data->Asset, NewAsset, 2);
	CheckData();
}

void AuxAttachment::SetDataStr(char *NewDataStr)
{
	ReplaceString(Data->DataStr, NewDataStr, 3,64);
	CheckData();
}

/******************************
*     PARENT FLAG METHODS     *
******************************/

void AuxAttachment::CheckData()
{
	SetParentExtendedFlag(HasData());
}

int AuxAttachment::HasData()
{
	return (ExtendedFlags[0] & 0xF0);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxAttachment::Clear()
{
    _Attachment ClearData;

    *ClearData.BoneName = 0;
	ClearData.Type = 0;
	ClearData.Asset = 0;
	*ClearData.DataStr = 0;

    SetData(&ClearData);
}

void AuxAttachment::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));
}