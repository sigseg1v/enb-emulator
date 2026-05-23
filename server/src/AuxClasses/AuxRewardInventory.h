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
#ifndef _AUXREWARDINV_H_INCLUDED_
#define _AUXREWARDINV_H_INCLUDED_

#include "AuxItem.h"
	
struct _RewardInv
{
	_Item Item[2];
} ATTRIB_PACKED;

class AuxRewardInv : public AuxBase
{
public:
    AuxRewardInv()
	{
	}

    ~AuxRewardInv()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _RewardInv * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 2, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0xF6);

		Item[0].Init(0, this, &Data->Item[0]);
		Item[1].Init(1, this, &Data->Item[1]);
	}

    void ClearFlags();

	void BuildPacket(unsigned char *, long &);
	void BuildExtendedPacket(unsigned char *, long &);

	_RewardInv * GetData();

	void SetData(_RewardInv *);

private:
    _RewardInv * Data;

	unsigned char Flags[1];
	unsigned char ExtendedFlags[1];

public:
	class AuxItem Item[2];
};

#endif
