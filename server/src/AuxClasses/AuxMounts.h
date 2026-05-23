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
#ifndef _AUXMOUNTS_H_INCLUDED_
#define _AUXMOUNTS_H_INCLUDED_

#include "AuxBase.h"
	
#define EmptyMount   0x00000000
#define ShieldMount  0x00000004
#define EngineMount  0x00000040
#define ReactorMount 0x00000080
#define WeaponMount  0x0001C002
#define DeviceMount  0x00020809

struct _Mounts
{
	u32 Mount[20];
} ATTRIB_PACKED;

class AuxMounts : public AuxBase
{
public:
    AuxMounts()
	{
	}

    ~AuxMounts()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _Mounts * DataPointer)
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
			Data->Mount[i] = EmptyMount;
        }
    }

    void ClearFlags();

	void BuildPacket(unsigned char *, long &);
	void BuildExtendedPacket(unsigned char *, long &);

	_Mounts * GetData();

	u32 GetMount(unsigned int);

	void SetData(_Mounts *);

	void SetMount(unsigned int, u32);


private:
	_Mounts * Data;

	unsigned char Flags[3];
	unsigned char ExtendedFlags[6];
};

#endif
