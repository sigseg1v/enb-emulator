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
#include "AuxDamage.h"

void AuxDamage::BuildPacket(unsigned char *buffer, long &index)
{
	/*
	AddFlags(Flags, sizeof(Flags), buffer, index);

	memset(Flags,0,sizeof(Flags));
	*/
}

void AuxDamage::BuildExtendedPacket(unsigned char *buffer, long &index)
{
    /*
    AddFlags(ExtendedFlags, sizeof(ExtendedFlags), buffer, index);
    */
}

void AuxDamage::BuildSpecialPacket(unsigned char *buffer, long &index)
{
	/*
	AddFlags(Flags, sizeof(Flags), buffer, index);
    */
}

/******************************
*         GET METHODS         *
******************************/

_Damage * AuxDamage::GetData()			{return Data;}

/******************************
*         SET METHODS         *
******************************/

void AuxDamage::SetData(_Damage * NewData)
{
	/*
	ReplaceData(&Data.Slot1, NewData->Slot1, 0);
	ReplaceData(&Data.Slot2, NewData->Slot2, 1);
	ReplaceData(&Data.Slot3, NewData->Slot3, 2);
	ReplaceData(&Data.Slot4, NewData->Slot4, 3);
	*/
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxDamage::Clear()
{
    _Damage ClearData;
    
    ClearData.NoClueWhatSoEver = 0;

    SetData(&ClearData);
}

void AuxDamage::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));
}