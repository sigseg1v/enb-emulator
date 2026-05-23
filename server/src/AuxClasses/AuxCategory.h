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
#ifndef _AUXCATEGORY_H_INCLUDED_
#define _AUXCATEGORY_H_INCLUDED_

#include "AuxSubCategories.h"
	
struct _Category
{
	char Name[32];
	_SubCategories SubCategories;
	u32 CategoryID;
} ATTRIB_PACKED;

class AuxCategory : public AuxBase
{
public:
    AuxCategory()
	{
	}

    ~AuxCategory()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _Category * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 3, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0x86);
		ExtendedFlags[1] = char(0x03);

        *Data->Name = 0;
        SubCategories.Init(1, this, &Data->SubCategories);
        Data->CategoryID = 0;
	}

    void ClearFlags();

	void BuildPacket(unsigned char *, long &, bool);

	_Category * GetData();

	char * GetName();
	u32 GetCategoryID();

	void SetData(_Category *);

	void SetName(char *);
	void SetCategoryID(u32);

protected:
    void CheckData();

private:
	int HasData();

    _Category * Data;

	unsigned char Flags[1];
	unsigned char ExtendedFlags[2];

public:
	class AuxSubCategories SubCategories;
};

#endif
