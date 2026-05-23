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
#include "AuxQuadrantDamage.h"

void AuxQuadrantDamage::BuildPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	if (Flags[0] & 0x10)	//ExtendedFlags[1] & 0x01
	{
		AddData(buffer, Data->Slot1, index);
	}

	if (Flags[0] & 0x20)	//ExtendedFlags[1] & 0x02
	{
		AddData(buffer, Data->Slot2, index);
	}

	if (Flags[0] & 0x40)	//ExtendedFlags[1] & 0x04
	{
		AddData(buffer, Data->Slot3, index);
	}

	if (Flags[0] & 0x80)	//ExtendedFlags[1] & 0x08
	{
		AddData(buffer, Data->Slot4, index);
	}

	memset(Flags,0,sizeof(Flags));
}

void AuxQuadrantDamage::BuildExtendedPacket(unsigned char *buffer, long &index)
{
	AddFlags(ExtendedFlags, sizeof(ExtendedFlags), buffer, index);

	if (ExtendedFlags[0] & 0x10)	//ExtendedFlags[1] & 0x01
	{
		AddData(buffer, Data->Slot1, index);
	}

	if (ExtendedFlags[0] & 0x20)	//ExtendedFlags[1] & 0x02
	{
		AddData(buffer, Data->Slot2, index);
	}

	if (ExtendedFlags[0] & 0x40)	//ExtendedFlags[1] & 0x04
	{
		AddData(buffer, Data->Slot3, index);
	}

	if (ExtendedFlags[0] & 0x80)	//ExtendedFlags[1] & 0x08
	{
		AddData(buffer, Data->Slot4, index);
	}
}

void AuxQuadrantDamage::BuildSpecialPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	if (Flags[0] & 0x10)	//ExtendedFlags[1] & 0x01
	{
		AddData(buffer, Data->Slot1, index);
	}

	if (Flags[0] & 0x20)	//ExtendedFlags[1] & 0x02
	{
		AddData(buffer, Data->Slot2, index);
	}

	if (Flags[0] & 0x40)	//ExtendedFlags[1] & 0x04
	{
		AddData(buffer, Data->Slot3, index);
	}

	if (Flags[0] & 0x80)	//ExtendedFlags[1] & 0x08
	{
		AddData(buffer, Data->Slot4, index);
	}
}

/******************************
*         GET METHODS         *
******************************/

_QuadrantDamage * AuxQuadrantDamage::GetData()			{return Data;}

float AuxQuadrantDamage::GetSlot1()						{return Data->Slot1;}
float AuxQuadrantDamage::GetSlot2()						{return Data->Slot2;}
float AuxQuadrantDamage::GetSlot3()						{return Data->Slot3;}
float AuxQuadrantDamage::GetSlot4()						{return Data->Slot4;}

/******************************
*         SET METHODS         *
******************************/

void AuxQuadrantDamage::SetData(_QuadrantDamage *NewData)
{
	ReplaceData(&Data->Slot1, NewData->Slot1, 0);
	ReplaceData(&Data->Slot2, NewData->Slot2, 1);
	ReplaceData(&Data->Slot3, NewData->Slot3, 2);
	ReplaceData(&Data->Slot4, NewData->Slot4, 3);
}

void AuxQuadrantDamage::SetSlot1(float NewSlot1)
{
	ReplaceData(&Data->Slot1, NewSlot1, 0);
}

void AuxQuadrantDamage::SetSlot2(float NewSlot2)
{
	ReplaceData(&Data->Slot2, NewSlot2, 1);
}

void AuxQuadrantDamage::SetSlot3(float NewSlot3)
{
	ReplaceData(&Data->Slot3, NewSlot3, 2);
}

void AuxQuadrantDamage::SetSlot4(float NewSlot4)
{
	ReplaceData(&Data->Slot4, NewSlot4, 3);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxQuadrantDamage::Clear()
{
    _QuadrantDamage ClearData;
    
	ClearData.Slot1 = 0;
	ClearData.Slot2 = 0;
	ClearData.Slot3 = 0;
	ClearData.Slot4 = 0;

    SetData(&ClearData);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxQuadrantDamage::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));
}