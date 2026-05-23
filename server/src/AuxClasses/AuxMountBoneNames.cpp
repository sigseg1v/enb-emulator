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
#include "AuxMountBoneNames.h"

void AuxMountBoneNames::BuildPacket(unsigned char *buffer, long &index)
{
	AddFlags(Flags, sizeof(Flags), buffer, index);

	for (int i=0;i<20;i++)
	{
		if (CheckFlagBit(i))
		{
			AddString(buffer, Data->MountBoneName[i], index);
		}
	}

	memset(Flags,0,sizeof(Flags));
}

void AuxMountBoneNames::BuildExtendedPacket(unsigned char *buffer, long &index)
{
	AddFlags(ExtendedFlags, sizeof(ExtendedFlags), buffer, index);

	for (int i=0;i<20;i++)
	{
		if (CheckExtendedFlagBit1(i))
		{
			AddString(buffer, Data->MountBoneName[i], index);
		}
	}
}

/******************************
*         GET METHODS         *
******************************/

_MountBones * AuxMountBoneNames::GetData()					        {return Data;}

char * AuxMountBoneNames::GetMountBoneName(unsigned int Index)		{return Data->MountBoneName[Index];}

/******************************
*         SET METHODS         *
******************************/

void AuxMountBoneNames::SetData(_MountBones *NewData)
{
	for (int i=0;i<20;i++)
	{
		ReplaceString(Data->MountBoneName[i], NewData->MountBoneName[i], i, 64);
	}
}

void AuxMountBoneNames::SetMountBoneName(unsigned int Index, char *NewMount)
{
	ReplaceString(Data->MountBoneName[Index], NewMount, Index, 64);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxMountBoneNames::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));
}