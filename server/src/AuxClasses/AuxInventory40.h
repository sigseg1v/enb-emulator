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
#ifndef _AUXINVENTORY40_H_INCLUDED_
#define _AUXINVENTORY40_H_INCLUDED_

#include "AuxItem.h"
	
struct _Inventory40
{
	_Item Item[40];
} ATTRIB_PACKED;

class AuxInventory40 : public AuxBase
{
public:
    AuxInventory40()
	{
	}

    ~AuxInventory40()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _Inventory40 * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 40, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0xF6);
		ExtendedFlags[1] = char(0xFF);
		ExtendedFlags[2] = char(0xFF);
		ExtendedFlags[3] = char(0xFF);
		ExtendedFlags[4] = char(0xFF);
		ExtendedFlags[5] = char(0xFF);
		ExtendedFlags[6] = char(0xFF);
		ExtendedFlags[7] = char(0xFF);
		ExtendedFlags[8] = char(0xFF);
		ExtendedFlags[9] = char(0xFF);
		ExtendedFlags[10] = char(0x0F);
		//ExtendedFlags[11] = char(0x0F);

		for (int i=0;i<40;i++)
		{
			Item[i].Init(i, this, &Data->Item[i]);
		}
	}

    void Clear();
    void ClearFlags();

    void BuildPacket(unsigned char *, long &);
    void BuildExtendedPacket(unsigned char *, long &);

	_Inventory40 * GetData();

	void SetData(_Inventory40 *);

private:
    _Inventory40 * Data;

	unsigned char Flags[6];
	unsigned char ExtendedFlags[11];

public:
	class AuxItem Item[40];
};

#endif
