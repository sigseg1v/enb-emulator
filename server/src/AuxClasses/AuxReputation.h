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
#ifndef _AUXREPUTATION_H_INCLUDED_
#define _AUXREPUTATION_H_INCLUDED_

#include "AuxFactions.h"
	
struct _Reputation
{
	_Factions Factions;
	char Affiliation[64];
} ATTRIB_PACKED;

class AuxReputation : public AuxBase
{
public:
    AuxReputation()
	{
	}

    ~AuxReputation()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _Reputation * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 2, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0xD6);

		Factions.Init(0, this, &Data->Factions);
		*Data->Affiliation = 0;
	}

    void ClearFlags();

	void BuildPacket(unsigned char *, long &);
	void BuildExtendedPacket(unsigned char *, long &);

	_Reputation * GetData();

	char * GetAffiliation();

	void SetData(_Reputation *);

	void SetAffilitation(char *);

private:
	_Reputation * Data;

	unsigned char Flags[1];
	unsigned char ExtendedFlags[1];

public:
	class AuxFactions Factions;
};

#endif
