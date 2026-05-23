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
#ifndef _AUXVENDORINV_H_INCLUDED_
#define _AUXVENDORINV_H_INCLUDED_

#include "AuxItem.h"
	
struct _VendorInv
{
	_Item Item[128];
} ATTRIB_PACKED;

class AuxVendorInv : public AuxBase
{
public:
    AuxVendorInv()
	{
	}

    ~AuxVendorInv()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _VendorInv * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 128, Parent, MemberIndex);
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
		ExtendedFlags[10] = char(0xFF);
		ExtendedFlags[11] = char(0xFF);
		ExtendedFlags[12] = char(0xFF);
		ExtendedFlags[13] = char(0xFF);
		ExtendedFlags[14] = char(0xFF);
		ExtendedFlags[15] = char(0xFF);
		ExtendedFlags[16] = char(0xFF);
		ExtendedFlags[17] = char(0xFF);
		ExtendedFlags[18] = char(0xFF);
		ExtendedFlags[19] = char(0xFF);
		ExtendedFlags[20] = char(0xFF);
		ExtendedFlags[21] = char(0xFF);
		ExtendedFlags[22] = char(0xFF);
		ExtendedFlags[23] = char(0xFF);
		ExtendedFlags[24] = char(0xFF);
		ExtendedFlags[25] = char(0xFF);
		ExtendedFlags[26] = char(0xFF);
		ExtendedFlags[27] = char(0xFF);
		ExtendedFlags[28] = char(0xFF);
		ExtendedFlags[29] = char(0xFF);
		ExtendedFlags[30] = char(0xFF);
		ExtendedFlags[31] = char(0xFF);
		ExtendedFlags[32] = char(0x0F);

		for (int i=0;i<128;i++)
		{
			Item[i].Init(i, this, &Data->Item[i]);
		}
	}

    void Clear();
    void ClearFlags();

	void BuildPacket(unsigned char *, long &);
	void BuildExtendedPacket(unsigned char *, long &);

	_VendorInv * GetData();

	void SetData(_VendorInv *);

private:
    _VendorInv * Data;

	unsigned char Flags[17];
	unsigned char ExtendedFlags[33];

public:
	class AuxItem Item[128];
};

#endif
