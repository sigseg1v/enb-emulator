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
#ifndef _AUXMISSIONSTAGE_H_INCLUDED_
#define _AUXMISSIONSTAGE_H_INCLUDED_

#include "AuxBase.h"
	
struct _MissionStage
{
	char	Text[2048];
	bool	IsTimed;
} ATTRIB_PACKED;

class AuxMissionStage : public AuxBase
{
public:
    AuxMissionStage()
	{
	}

    ~AuxMissionStage()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _MissionStage * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 2, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0xC6);

	    *Data->Text = 0; 
		Data->IsTimed = 0;
	}

    _MissionStage GetClearStruct();
    void Clear();
    void ClearFlags();
	void SetAllFlags();

	void BuildPacket(unsigned char *, long &);
	void BuildExtendedPacket(unsigned char *, long &);

	_MissionStage * GetData();

	char * GetText();
	bool GetIsTimed();

	void SetData(_MissionStage *);

	void SetText(char *);
	void SetIsTimed(bool);

protected:
    void CheckData();

private:
	int HasData();

	_MissionStage * Data;

	unsigned char Flags[1];
	unsigned char ExtendedFlags[1];
};

#endif
