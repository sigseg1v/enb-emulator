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
#ifndef _AUXPERCENT_H_INCLUDED_
#define _AUXPERCENT_H_INCLUDED_

#include "AuxBase.h"
	
struct _Percent
{
	u32 EndTime;
	float ChangePerTick;
	float StartValue;
} ATTRIB_PACKED;

class AuxPercent : public AuxBase
{
public:
    AuxPercent()
	{
	}

    ~AuxPercent()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _Percent * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 3, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0x86);
		ExtendedFlags[1] = char(0x03);

		Data->EndTime = 0;
		Data->ChangePerTick = 0;
		Data->StartValue = 0;
	}

    void ClearFlags();

	void BuildPacket(unsigned char *, long &);
	void BuildExtendedPacket(unsigned char *, long &);
	void BuildSpecialPacket(unsigned char *, long &);

	_Percent * GetData();

	u32 GetEndTime();
	float GetChangePerTick();
	float GetStartValue();

	void SetData(_Percent *);

	void SetEndTime(u32);
	void SetChangePerTick(float);
	void SetStartValue(float);


private:
	_Percent * Data;

	unsigned char Flags[1];
	unsigned char ExtendedFlags[2];
};

#endif
