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
#ifndef _AUXMISSIONSTAGES_H_INCLUDED_
#define _AUXMISSIONSTAGES_H_INCLUDED_

#include "AuxMissionStage.h"
	
struct _MissionStages
{
	_MissionStage Stage[20];
} ATTRIB_PACKED;

class AuxMissionStages : public AuxBase
{
public:
    AuxMissionStages()
	{
	}

    ~AuxMissionStages()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _MissionStages * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 20, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0x06);
		ExtendedFlags[1] = char(0x00);
		ExtendedFlags[2] = char(0x00);
		ExtendedFlags[3] = char(0xFF);
		ExtendedFlags[4] = char(0xFF);
		ExtendedFlags[5] = char(0x0F);

		for (int i=0;i<20;i++)
		{
			Stage[i].Init(i, this, &Data->Stage[i]);
		}
	}

    _MissionStages GetClearStruct();
    void Clear();
    void ClearFlags();
	void SetAllFlags(int stages = 20);

	void BuildPacket(unsigned char *, long &);
	void BuildExtendedPacket(unsigned char *, long &);

	_MissionStages * GetData();

	void SetData(_MissionStages *);

protected:
    void CheckData();

private:
	int HasData();

    _MissionStages * Data;

	unsigned char Flags[3];
	unsigned char ExtendedFlags[6];

public:
	class AuxMissionStage Stage[20];
};

#endif
