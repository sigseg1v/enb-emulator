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
#include "AuxSubCategory.h"

void AuxSubCategory::BuildPacket(unsigned char *buffer, long &index, bool TwoBitFlags)
{
	if (TwoBitFlags)
	{
		AddFlags(ExtendedFlags, sizeof(ExtendedFlags), buffer, index);
		memcpy(Flags, ExtendedFlags, sizeof(Flags));
	}
	else
	{
		AddFlags(Flags, sizeof(Flags), buffer, index);
	}

	if (Flags[0] & 0x10)	//ExtendedFlags[0] & 0x80
	{
		AddString(buffer, Data->Name, index);
	}

	if (Flags[0] & 0x20)	//ExtendedFlags[1] & 0x01
	{
		AddData(buffer, Data->SubCategoryID, index);
	}

	if (Flags[0] & 0x40)	//ExtendedFlags[1] & 0x02
	{
		AddData(buffer, char(Data->IsVisible), index);
	}

	memset(Flags,0,sizeof(Flags));
}

/******************************
*         GET METHODS         *
******************************/

_SubCategory * AuxSubCategory::GetData()		{return Data;}

char * AuxSubCategory::GetName()                {return Data->Name;}
u32 AuxSubCategory::GetSubCategoryID()          {return Data->SubCategoryID;}
bool AuxSubCategory::GetIsVisible()             {return Data->IsVisible;}

/******************************
*         SET METHODS         *
******************************/

void AuxSubCategory::SetData(_SubCategory * NewData)
{
	ReplaceString(Data->Name, NewData->Name, 0, 32);
	ReplaceData(Data->SubCategoryID, NewData->SubCategoryID, 1);
	ReplaceData(Data->IsVisible, NewData->IsVisible, 2);

	CheckData();
}

void AuxSubCategory::SetName(char * NewName)
{
	ReplaceString(Data->Name, NewName, 0, 32);
	CheckData();
}

void AuxSubCategory::SetSubCategoryID(u32 NewSubCategoryID)
{
	ReplaceData(Data->SubCategoryID, NewSubCategoryID, 1);
	CheckData();
}

void AuxSubCategory::SetIsVisible(bool NewIsVisible)
{
	ReplaceData(Data->IsVisible, NewIsVisible, 2);
	CheckData();
}

/******************************
*     PARENT FLAG METHODS     *
******************************/

void AuxSubCategory::CheckData()
{
	SetParentExtendedFlag(HasData());
}

int AuxSubCategory::HasData()
{
	return (ExtendedFlags[0] & 0x70);
}

/******************************
*       UTILITY METHODS       *
******************************/

void AuxSubCategory::ClearFlags()
{
	memset(Flags,0,sizeof(Flags));
}