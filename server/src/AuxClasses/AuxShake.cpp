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
#include "AuxShake.h"

void AuxShake::BuildPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	if (Flags[0] & 0x10)	//ExtendedFlags[1] & 0x01
	{
		AddData(buffer, Data->ForceX, index);
	}

	if (Flags[0] & 0x20)	//ExtendedFlags[1] & 0x02
	{
		AddData(buffer, Data->ForceY, index);
	}

	if (Flags[0] & 0x40)	//ExtendedFlags[1] & 0x04
	{
		AddData(buffer, Data->ForceZ, index);
	}

	if (Flags[0] & 0x80)	//ExtendedFlags[1] & 0x08
	{
		AddData(buffer, Data->Damage, index);
	}

    Clear();

	memset(Flags,0,sizeof(Flags));
}

void AuxShake::BuildExtendedPacket(unsigned char *buffer, long &index)
{
	AddFlags(ExtendedFlags, sizeof(ExtendedFlags), buffer, index);

	if (ExtendedFlags[0] & 0x10)	//ExtendedFlags[1] & 0x01
	{
		AddData(buffer, Data->ForceX, index);
	}

	if (ExtendedFlags[0] & 0x20)	//ExtendedFlags[1] & 0x02
	{
		AddData(buffer, Data->ForceY, index);
	}

	if (ExtendedFlags[0] & 0x40)	//ExtendedFlags[1] & 0x04
	{
		AddData(buffer, Data->ForceZ, index);
	}

	if (ExtendedFlags[0] & 0x80)	//ExtendedFlags[1] & 0x08
	{
		AddData(buffer, Data->Damage, index);
	}
}

/******************************
*         GET METHODS         *
******************************/

_Shake * AuxShake::GetData()		{return Data;}

float AuxShake::GetForceX()			{return Data->ForceX;}
float AuxShake::GetForceY()			{return Data->ForceY;}
float AuxShake::GetForceZ()			{return Data->ForceZ;}
float AuxShake::GetDamage()			{return Data->Damage;}

/******************************
*         SET METHODS         *
******************************/

void AuxShake::SetData(_Shake *NewData)
{
	ReplaceData(&Data->ForceX, NewData->ForceX, 0);
	ReplaceData(&Data->ForceY, NewData->ForceY, 1);
	ReplaceData(&Data->ForceZ, NewData->ForceZ, 2);
	ReplaceData(&Data->Damage, NewData->Damage, 3);
}

void AuxShake::SetForceX(float NewForceX)
{
	ReplaceData(&Data->ForceX, NewForceX, 0);
}

void AuxShake::SetForceY(float NewForceY)
{
	ReplaceData(&Data->ForceY, NewForceY, 1);
}

void AuxShake::SetForceZ(float NewForceZ)
{
	ReplaceData(&Data->ForceZ, NewForceZ, 2);
}

void AuxShake::SetDamage(float NewDamage)
{
	ReplaceData(&Data->Damage, NewDamage, 3);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxShake::Clear()
{
	Data->ForceX = 0;
	Data->ForceY = 0;
	Data->ForceZ = 0;
	Data->Damage = 0;
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxShake::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));
}