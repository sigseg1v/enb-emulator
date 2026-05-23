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
#ifndef _AUXSUBCATEGORIES_H_INCLUDED_
#define _AUXSUBCATEGORIES_H_INCLUDED_

#include "AuxSubCategory.h"
	
struct _SubCategories
{
	_SubCategory SubCategory[5];
} ATTRIB_PACKED;

class AuxSubCategories : public AuxBase
{
public:
    AuxSubCategories()
	{
	}

    ~AuxSubCategories()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _SubCategories * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 5, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0x06);
		ExtendedFlags[1] = char(0x3E);

		for (int i=0;i<5;i++)
		{
			SubCategory[i].Init(i, this, &Data->SubCategory[i]);
		}
	}

    void ClearFlags();

	void BuildPacket(unsigned char *, long &, bool);

	_SubCategories * GetData();

	void SetData(_SubCategories *);

protected:
    void CheckData();

private:
    int HasData();

    _SubCategories * Data;

	unsigned char Flags[2];
	unsigned char ExtendedFlags[2];

public:
	class AuxSubCategory SubCategory[5];
};

#endif
