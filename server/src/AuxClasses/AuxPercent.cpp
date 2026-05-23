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
#include "AuxPercent.h"

void AuxPercent::BuildPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	if (Flags[0] & 0x10)	//ExtendedFlags[0] & 0x80
	{
		AddData(buffer, Data->EndTime, index);
	}

	if (Flags[0] & 0x20)	//ExtendedFlags[1] & 0x10
	{
		AddData(buffer, Data->ChangePerTick, index);
	}

	if (Flags[0] & 0x40)	//ExtendedFlags[1] & 0x20
	{
		AddData(buffer, Data->StartValue, index);
	}

	memset(Flags,0,sizeof(Flags));
}

void AuxPercent::BuildExtendedPacket(unsigned char *buffer, long &index)
{
	AddFlags(ExtendedFlags, sizeof(ExtendedFlags), buffer, index);

	if (ExtendedFlags[0] & 0x10)	//ExtendedFlags[0] & 0x80
	{
		AddData(buffer, Data->EndTime, index);
	}

	if (ExtendedFlags[0] & 0x20)	//ExtendedFlags[1] & 0x10
	{
		AddData(buffer, Data->ChangePerTick, index);
	}

	if (ExtendedFlags[0] & 0x40)	//ExtendedFlags[1] & 0x20
	{
		AddData(buffer, Data->StartValue, index);
	}
}

void AuxPercent::BuildSpecialPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	if (Flags[0] & 0x10)	//ExtendedFlags[0] & 0x80
	{
		AddData(buffer, Data->EndTime, index);
	}

	if (Flags[0] & 0x20)	//ExtendedFlags[1] & 0x10
	{
		AddData(buffer, Data->ChangePerTick, index);
	}

	if (Flags[0] & 0x40)	//ExtendedFlags[1] & 0x20
	{
		AddData(buffer, Data->StartValue, index);
	}
}

/******************************
*         GET METHODS         *
******************************/

_Percent * AuxPercent::GetData()		{return Data;}

u32 AuxPercent::GetEndTime()			{return Data->EndTime;}
float AuxPercent::GetChangePerTick()	{return Data->ChangePerTick;}
float AuxPercent::GetStartValue()		{return Data->StartValue;}

/******************************
*         SET METHODS         *
******************************/

void AuxPercent::SetData(_Percent *NewData)
{
	ReplaceData(Data->EndTime, NewData->EndTime, 0);
	ReplaceData(Data->ChangePerTick, NewData->ChangePerTick, 1);
	ReplaceData(Data->StartValue, NewData->StartValue, 2);
}

void AuxPercent::SetEndTime(u32 NewEndTime)
{
	ReplaceData(Data->EndTime, NewEndTime, 0);
}

void AuxPercent::SetChangePerTick(float NewChangePerTick)
{
	ReplaceData(Data->ChangePerTick, NewChangePerTick, 1);
}

void AuxPercent::SetStartValue(float NewStartValue)
{
	ReplaceData(Data->StartValue, NewStartValue, 2);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxPercent::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));
}