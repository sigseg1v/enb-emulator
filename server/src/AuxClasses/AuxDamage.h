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
#ifndef _AUXDAMAGE_H_INCLUDED_
#define _AUXDAMAGE_H_INCLUDED_

#include "AuxBase.h"
	
struct _Damage
{
	u32	NoClueWhatSoEver;
} ATTRIB_PACKED;

class AuxDamage : public AuxBase
{
public:
    AuxDamage()
	{
	}

    ~AuxDamage()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _Damage * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 5, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0x06);
		ExtendedFlags[1] = char(0x3E);

		Data->NoClueWhatSoEver = 0;
	}

    void Clear();
    void ClearFlags();

	void BuildPacket(unsigned char *, long &);
	void BuildExtendedPacket(unsigned char *, long &);
	void BuildSpecialPacket(unsigned char *, long &);

	_Damage * GetData();

	u32 GetNoClueWhatSoEver();

	void SetData(_Damage *);

	void SetNoClueWhatSoEver(u32);


private:
	_Damage * Data;

	unsigned char Flags[2];
	unsigned char ExtendedFlags[2];
};

#endif
