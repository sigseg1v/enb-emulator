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
#ifndef _AUXINVENTORY20_H_INCLUDED_
#define _AUXINVENTORY20_H_INCLUDED_

#include "AuxItem.h"
	
struct _Inventory20
{
	_Item Item[20];
} ATTRIB_PACKED;

class AuxInventory20 : public AuxBase
{
public:
    AuxInventory20()
	{
	}

    ~AuxInventory20()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _Inventory20 * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 20, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0xF6);
		ExtendedFlags[1] = char(0xFF);
		ExtendedFlags[2] = char(0xFF);
		ExtendedFlags[3] = char(0xFF);
		ExtendedFlags[4] = char(0xFF);
		ExtendedFlags[5] = char(0x0F);

		for (int i=0;i<20;i++)
		{
			Item[i].Init(i, this, &Data->Item[i]);
		}
	}

    void Clear();
    void ClearFlags();

    void BuildPacket(unsigned char *, long &);
    void BuildExtendedPacket(unsigned char *, long &);

	_Inventory20 * GetData();

	void SetData(_Inventory20 *);

private:
    _Inventory20 * Data;

	unsigned char Flags[3];
	unsigned char ExtendedFlags[6];

public:
	class AuxItem Item[20];
};

#endif
