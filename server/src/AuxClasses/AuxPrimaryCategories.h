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
#ifndef _AUXPRIMARYCATEGORIES_H_INCLUDED_
#define _AUXPRIMARYCATEGORIES_H_INCLUDED_

#include "AuxPrimaryCategory.h"
	
struct _PrimaryCategories
{
	_PrimaryCategory PrimaryCategory[2];
} ATTRIB_PACKED;

class AuxPrimaryCategories : public AuxBase
{
public:
    AuxPrimaryCategories()
	{
	}

    ~AuxPrimaryCategories()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _PrimaryCategories * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 2, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0xC6);

		PrimaryCategory[0].Init(0, this, &Data->PrimaryCategory[0]);
		PrimaryCategory[1].Init(1, this, &Data->PrimaryCategory[1]);
	}

    void ClearFlags();

	void BuildPacket(unsigned char *, long &, bool);

	_PrimaryCategories * GetData();

	void SetData(_PrimaryCategories *);

protected:
    void CheckData();

private:
    int HasData();

    _PrimaryCategories * Data;

	unsigned char Flags[1];
	unsigned char ExtendedFlags[1];

public:
	class AuxPrimaryCategory PrimaryCategory[2];
};

#endif
