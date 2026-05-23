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
#ifndef _AUXPREVIOUSATTEMPTS_H_INCLUDED_
#define _AUXPREVIOUSATTEMPTS_H_INCLUDED_

#include "AuxKnownFormula.h"
	
struct _PreviousAttempts
{
	_KnownFormula Attempt[16];
} ATTRIB_PACKED;

class AuxPreviousAttempts : public AuxBase
{
public:
    AuxPreviousAttempts()
	{
	}

    ~AuxPreviousAttempts()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _PreviousAttempts * DataPointer)
	{
		Construct(Flags, 0, 16, Parent, MemberIndex);
        Data = DataPointer;

        // We have no extended flags for this class since its NEVER sent
        // in 2-bit mode. Therefore, the flags are redundant
		memset(Flags,0, sizeof(Flags));

		for (int i=0;i<16;i++)
		{
			Attempt[i].Init(i, this, &Data->Attempt[i]);
		}
	}

    void ClearFlags();

	void BuildPacket(unsigned char *, long &, bool);

	_PreviousAttempts * GetData();

	void SetData(_PreviousAttempts *);

    void ResetAttempts(unsigned int);
    void Clear();

private:
    _PreviousAttempts * Data;

	unsigned char Flags[3];

public:
	class AuxKnownFormula Attempt[16];
};

#endif
