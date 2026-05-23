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
#ifndef _AUXATTACHMENT_H_INCLUDED_
#define _AUXATTACHMENT_H_INCLUDED_

#include "AuxBase.h"
	
struct _Attachment
{
	char	BoneName[64];
	u32		Type;
	u32		Asset;
	char	DataStr[64];
} ATTRIB_PACKED;

class AuxAttachment : public AuxBase
{
public:
    AuxAttachment()
	{
	}

    ~AuxAttachment()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _Attachment * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 4, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0x06);
		ExtendedFlags[1] = char(0x0F);

        *Data->BoneName = 0;
		Data->Type = 0;
		Data->Asset = 0;
		*Data->DataStr = 0; 
	}

    void Clear();
    void ClearFlags();

	void BuildPacket(unsigned char *, long &);
	void BuildExtendedPacket(unsigned char *, long &);
	void BuildSpecialPacket(unsigned char *, long &);

	_Attachment * GetData();

	char * GetBoneName();
	u32 GetType();
	u32 GetAsset();
	char * GetDataStr();

	void SetData(_Attachment *);

	void SetBoneName(char *);
	void SetType(u32);
	void SetAsset(u32);
	void SetDataStr(char *);

protected:
    void CheckData();

private:
	int HasData();

	_Attachment * Data;

	unsigned char Flags[1];
	unsigned char ExtendedFlags[2];
};

#endif
