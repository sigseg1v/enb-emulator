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
#include "AuxLego.h"

void AuxLego::BuildPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	if (Flags[0] & 0x10)
	{
		AddData(buffer, Data->Scale, index);
	}

	if (Flags[0] & 0x20)
	{
		Attachments.BuildPacket(buffer, index);
	}
	
    memset(Flags,0,sizeof(Flags));
}

void AuxLego::BuildExtendedPacket(unsigned char *buffer, long &index)
{
	AddFlags(ExtendedFlags, sizeof(ExtendedFlags), buffer, index);

	if (ExtendedFlags[0] & 0x10)
	{
		AddData(buffer, Data->Scale, index);
	}

	if (ExtendedFlags[0] & 0x20)
	{
		Attachments.BuildExtendedPacket(buffer, index);
	}
	else if (ExtendedFlags[0] & 0x80)
	{
		AddData(buffer, char(0x05), index);
	}
}

void AuxLego::BuildSpecialPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	if (Flags[0] & 0x10)
	{
		AddData(buffer, Data->Scale, index);
	}

	if (Flags[0] & 0x20)
	{
		Attachments.BuildSpecialPacket(buffer, index);
	}
}

/******************************
*         GET METHODS         *
******************************/

_Lego * AuxLego::GetData()      {return Data;}

float AuxLego::GetScale()		{return Data->Scale;}

/******************************
*         SET METHODS         *
******************************/

void AuxLego::SetData(_Lego *NewData)
{
	ReplaceData(Data->Scale, NewData->Scale, 0);
	Attachments.SetData(&NewData->Attachments);
}

void AuxLego::SetScale(float NewScale)
{
	ReplaceData(Data->Scale, NewScale, 0);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxLego::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));
    Attachments.ClearFlags();
}