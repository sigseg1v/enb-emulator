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
#ifndef _AUXSUBCATEGORY_H_INCLUDED_
#define _AUXSUBCATEGORY_H_INCLUDED_

#include "AuxBase.h"
	
struct _SubCategory
{
	char Name[32];
	u32 SubCategoryID;
	bool IsVisible;
} ATTRIB_PACKED;

class AuxSubCategory : public AuxBase
{
public:
    AuxSubCategory()
	{
	}

    ~AuxSubCategory()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _SubCategory * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 3, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0x86);
		ExtendedFlags[1] = char(0x03);

		*Data->Name = 0; 
		Data->SubCategoryID = 0;
		Data->IsVisible = 0;
	}

    void ClearFlags();

    void BuildPacket(unsigned char *, long &, bool);

	_SubCategory * GetData();

	char * GetName();
	u32 GetSubCategoryID();
	bool GetIsVisible();

	void SetData(_SubCategory *);

	void SetName(char *);
	void SetSubCategoryID(u32);
	void SetIsVisible(bool);

protected:
    void CheckData();

private:
	int HasData();

	_SubCategory * Data;

	unsigned char Flags[1];
	unsigned char ExtendedFlags[2];
};

#endif
