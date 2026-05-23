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
#include "AuxMounts.h"

void AuxMounts::BuildPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	for (int i=0;i<20;i++)
	{
		if (CheckFlagBit(i))
		{
			AddData(buffer, Data->Mount[i], index);
			AddData(buffer, s32(-1), index);
			AddData(buffer, s32(-1), index);
		}
	}

	memset(Flags,0,sizeof(Flags));
}

void AuxMounts::BuildExtendedPacket(unsigned char *buffer, long &index)
{
	AddFlags(ExtendedFlags, sizeof(ExtendedFlags), buffer, index);

	for (int i=0;i<20;i++)
	{
		if (CheckExtendedFlagBit1(i))
		{
			AddData(buffer, Data->Mount[i], index);
			AddData(buffer, s32(-1), index);
			AddData(buffer, s32(-1), index);
		}
	}
}

/******************************
*         GET METHODS         *
******************************/

_Mounts * AuxMounts::GetData()					{return Data;}

u32 AuxMounts::GetMount(unsigned int Index)		{return Data->Mount[Index];}

/******************************
*         SET METHODS         *
******************************/

void AuxMounts::SetData(_Mounts *NewData)
{
	for (int i=0;i<20;i++)
	{
		ReplaceData(&Data->Mount[i], NewData->Mount[i], i);
	}
}

void AuxMounts::SetMount(unsigned int Index, u32 NewMount)
{
	ReplaceData(&Data->Mount[Index], NewMount, Index);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxMounts::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));
}