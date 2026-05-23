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
#ifndef _AUXINVENTORY6_H_INCLUDED_
#define _AUXINVENTORY6_H_INCLUDED_

#include "AuxItem.h"
	
struct _Inventory6
{
	_Item Item[6];
} ATTRIB_PACKED;

class AuxInventory6 : public AuxBase
{
public:
    AuxInventory6()
	{
	}

    ~AuxInventory6()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _Inventory6 * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 6, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0xF6);
		ExtendedFlags[1] = char(0xFF);

		for (int i=0;i<6;i++)
		{
			Item[i].Init(i, this, &Data->Item[i]);
		}
	}

    void Clear();
    void Empty();
    void ClearFlags();

	void BuildPacket(unsigned char *, long &);
	void BuildExtendedPacket(unsigned char *, long &);

	_Inventory6 * GetData();

	void SetData(_Inventory6 *);

private:
    _Inventory6 * Data;

	unsigned char Flags[2];
	unsigned char ExtendedFlags[2];

public:
	class AuxItem Item[6];
};

#endif
