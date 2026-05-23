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
#ifndef _AUXEFFECT_H_INCLUDED_
#define _AUXEFFECT_H_INCLUDED_

#include "AuxBase.h"
	
struct _Effect
{
	float	Range;
	u32		Usage;
	u32		Targets;
	u32		Validity;
} ATTRIB_PACKED;

class AuxEffect : public AuxBase
{
public:
    AuxEffect()
	{
	}

    ~AuxEffect()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _Effect * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 4, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0x06);
		ExtendedFlags[1] = char(0x0F);

		Data->Range = 0; 
		Data->Usage = 0;
		Data->Targets = 0;
		Data->Validity = 0;
	}

    _Effect GetClearStruct();
    void Clear();
    void ClearFlags();

	void BuildPacket(unsigned char *, long &);
	void BuildExtendedPacket(unsigned char *, long &);

	_Effect * GetData();

	float GetRange();
	u32 GetUsage();
	u32 GetTargets();
	u32 GetValidity();

	void SetData(_Effect *);

	void SetRange(float);
	void SetUsage(u32);
	void SetTargets(u32);
	void SetValidity(u32);

protected:
    void CheckData();

private:
	int HasData();

	_Effect * Data;

	unsigned char Flags[1];
	unsigned char ExtendedFlags[2];
};

#endif
