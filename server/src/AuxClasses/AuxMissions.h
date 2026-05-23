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
#ifndef _AUXMISSIONS_H_INCLUDED_
#define _AUXMISSIONS_H_INCLUDED_

#include "AuxMission.h"
	
struct _Missions
{
	_Mission Mission[12];
} ATTRIB_PACKED;

class AuxMissions : public AuxBase
{
public:
    AuxMissions()
	{
	}

    ~AuxMissions()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _Missions * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 12, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0xF6);
		ExtendedFlags[1] = char(0x3F);
		ExtendedFlags[2] = char(0xFF);
		ExtendedFlags[3] = char(0x0F);

		for (int i=0;i<12;i++)
		{
			Mission[i].Init(i, this, &Data->Mission[i]);
			Mission[i].SetID(i);
		}
	}

    void ClearFlags();

	void BuildPacket(unsigned char *, long &);
	void BuildExtendedPacket(unsigned char *, long &);

	_Missions * GetData();

	void SetData(_Missions *);

private:
    _Missions * Data;

	unsigned char Flags[2];
	unsigned char ExtendedFlags[4];

public:
	class AuxMission Mission[12];
};

#endif
