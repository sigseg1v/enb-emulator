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

#include "AuxMissionStages.h"

void AuxMissionStages::BuildPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	for (int i=0;i<20;i++)
	{
		if (CheckFlagBit(i))
		{
			Stage[i].BuildPacket(buffer, index);
		}
	}

	memset(Flags,0,sizeof(Flags));
}

void AuxMissionStages::BuildExtendedPacket(unsigned char *buffer, long &index)
{
	AddFlags(ExtendedFlags, sizeof(ExtendedFlags), buffer, index);

	for (int i=0;i<20;i++)
	{
		if (CheckExtendedFlagBit1(i))
		{
			Stage[i].BuildExtendedPacket(buffer, index);
		}
		else if (CheckExtendedFlagBit2(i))
		{
			AddData(buffer, char(0x05), index);
		}
	}
}

/******************************
*         GET METHODS         *
******************************/

_MissionStages * AuxMissionStages::GetData()    {return Data;}

/******************************
*         SET METHODS         *
******************************/

void AuxMissionStages::SetData(_MissionStages *NewData)
{
	for (int i=0;i<20;i++)
	{
		Stage[i].SetData(&NewData->Stage[i]);
	}
}

/******************************
*     PARENT FLAG METHODS     *
******************************/

void AuxMissionStages::CheckData()
{
	SetParentExtendedFlag(HasData());
}

int AuxMissionStages::HasData()
{
	return (ExtendedFlags[0] & 0xF0 || ExtendedFlags[1] & 0xFF || ExtendedFlags[2] & 0xFF);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxMissionStages::SetAllFlags(int stages)
{
	if ((Flags[0] & 0x02) == 0)
	{
		Flags[0] |= 0x02;
		SetParentFlag();
		SetParentExtendedFlag(1);
	}
	for(int x=0;x<stages;x++)
	{
		// If flag should be set, then set it
		if (ExtendedFlags[(x + 4) / 8] & (1 << ((x + 4) % 8)))
		{
			Flags[(x + 4) / 8] |= (1 << ((x + 4) % 8));
		}
	}
}

_MissionStages AuxMissionStages::GetClearStruct()
{
    _MissionStages ClearData;
    
	for (int i=0;i<20;i++)
	{
		ClearData.Stage[i] = Stage[i].GetClearStruct();
	}

    return ClearData;
}

void AuxMissionStages::Clear()
{
    SetData(&GetClearStruct());
}

void AuxMissionStages::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));

	for (int i=0;i<20;i++)
	{
        Stage[i].ClearFlags();
	}
}