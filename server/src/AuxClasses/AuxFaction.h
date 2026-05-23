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
#ifndef _AUXFACTION_H_INCLUDED_
#define _AUXFACTION_H_INCLUDED_

#include "AuxBase.h"
	
struct _Faction
{
	char	Name[64];
	float	Reaction;
	u32		Order;
} ATTRIB_PACKED;

class AuxFaction : public AuxBase
{
public:
    AuxFaction()
	{
	}

    ~AuxFaction()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _Faction * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 3, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0x86);
		ExtendedFlags[1] = char(0x03);

		*Data->Name = 0; 
		Data->Reaction = 0;
		Data->Order = 0;
	}

    void ClearFlags();

	void BuildPacket(unsigned char *, long &);
	void BuildExtendedPacket(unsigned char *, long &);

	_Faction * GetData();

	char * GetName();
	float GetReaction();
	u32 GetOrder();

	void SetData(_Faction *);

	void SetName(char *);
	void SetReaction(float);
	void SetOrder(u32);

protected:
    void CheckData();

private:
	int HasData();

	_Faction * Data;

	unsigned char Flags[1];
	unsigned char ExtendedFlags[2];
};

#endif
