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
#ifndef _AUXKNOWNFORMULAS_H_INCLUDED_
#define _AUXKNOWNFORMULAS_H_INCLUDED_

#include "AuxKnownFormula.h"

#define MAX_KNOWN_FORMULAS	500
	
struct _KnownFormulas
{
	_KnownFormula Formula[MAX_KNOWN_FORMULAS];
} ATTRIB_PACKED;

class AuxKnownFormulas : public AuxBase
{
public:
    AuxKnownFormulas()
	{
	}

    ~AuxKnownFormulas()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _KnownFormulas * DataPointer)
	{
		Construct(Flags, 0, MAX_KNOWN_FORMULAS, Parent, MemberIndex);
        Data = DataPointer;

        // We have no extended flags for this class since its NEVER sent
        // in 2-bit mode. Therefore, the flags are redundant
		memset(Flags,0, sizeof(Flags));

		for (int i=0;i<MAX_KNOWN_FORMULAS;i++)
		{
			Formula[i].Init(i, this, &Data->Formula[i]);
		}
		m_NumFormulas = 0;
	}

    void ClearFlags();

	void BuildPacket(unsigned char *, long &, bool);

	_KnownFormulas * GetData();

	void SetData(_KnownFormulas *);

	void SetKnownFormulas(unsigned int count) { m_NumFormulas = count; };
	unsigned int GetKnownFormulas()			  { return m_NumFormulas; };
    void ResetKnownFormulas(bool change=false);
    void Clear();

private:
    _KnownFormulas * Data;
	unsigned int  m_NumFormulas;
	unsigned char Flags[MAX_KNOWN_FORMULAS/8+1];

public:
	class AuxKnownFormula Formula[MAX_KNOWN_FORMULAS];
};

#endif
