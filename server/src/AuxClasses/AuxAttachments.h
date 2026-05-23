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
#ifndef _AUXATTACHMENTS_H_INCLUDED_
#define _AUXATTACHMENTS_H_INCLUDED_

#include "AuxAttachment.h"
	
struct _Attachments
{
	_Attachment Attachment[18];
} ATTRIB_PACKED;

class AuxAttachments : public AuxBase
{
public:
    AuxAttachments()
	{
	}

    ~AuxAttachments()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _Attachments * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 18, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0x06);
		ExtendedFlags[1] = char(0x00);
		ExtendedFlags[2] = char(0xC0);
		ExtendedFlags[3] = char(0xFF);
		ExtendedFlags[4] = char(0xFF);

		for (int i=0;i<18;i++)
		{
			Attachment[i].Init(i, this, &Data->Attachment[i]);
		}
	}

    void ClearFlags();

	void BuildPacket(unsigned char *, long &);
	void BuildExtendedPacket(unsigned char *, long &);
	void BuildSpecialPacket(unsigned char *, long &);

	_Attachments * GetData();

	void SetData(_Attachments *);

private:
    _Attachments * Data;

	unsigned char Flags[3];
	unsigned char ExtendedFlags[5];

public:
	class AuxAttachment Attachment[18];
};

#endif
