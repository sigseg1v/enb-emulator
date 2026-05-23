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
#ifndef _AUXITEM_H_INCLUDED_
#define _AUXITEM_H_INCLUDED_

#include "AuxBase.h"

struct _BaseItem
{
	s32		ItemTemplateID;
	u32		StackCount; //the number of items in the stack
	u64 	Price; 
	float	AveCost;
	float	Structure;
	float	Quality;
	char	InstanceInfo[64];
	char	ActivatedEffectInstanceInfo[64];  // should be plenty 2*3*8? max
	char	EquipEffectInstanceInfo[6*3*8+1]; // 6? effects with up to 3? floats for each, 8 characters each
	char	BuilderName[64];
} ATTRIB_PACKED;

struct _Item : public _BaseItem
{
	u32     TradeStack; //the number of items in the stack which can award trade xp (not in packet)
} ATTRIB_PACKED;

class AuxItem : public AuxBase
{
public:
    AuxItem()
	{
		m_check = 0;
	}

    ~AuxItem()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _Item * DataPointer)
	{
        Construct(Flags, ExtendedFlags, 10, Parent, MemberIndex);

        Data = DataPointer;

    	memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0x16);
		ExtendedFlags[1] = char(0x80);
		ExtendedFlags[2] = char(0xFF);

        Data->ItemTemplateID = -2;	/* Invisible Item */
		Data->StackCount = 0;
		Data->Price = 0; 
		Data->AveCost = 0;
		Data->Structure = 0;
		Data->Quality = 0;
		*Data->InstanceInfo = 0;
		*Data->ActivatedEffectInstanceInfo = 0;
		*Data->EquipEffectInstanceInfo = 0;
		*Data->BuilderName = 0;

        Data->TradeStack = 0;
	}

    void Clear();   /* Sets as invisible item slot */
    void Empty();   /* Sets as empty item slot */
    void ClearFlags();

    void BuildPacket(unsigned char *, long &);
    void BuildExtendedPacket(unsigned char *, long &);

	_Item * GetData();

	s32 GetItemTemplateID();
	u32 GetStackCount();
	u64 GetPrice();
	float GetAveCost();
	float GetStructure();
	float GetQuality();
	char * GetInstanceInfo();
	char * GetActivatedEffectInstanceInfo();
	char * GetEquipEffectInstanceInfo();
	char * GetBuilderName();
    u32 GetTradeStack();

	void SetData(_Item *);

	void SetItemTemplateID(s32);
	void SetStackCount(u32);
	void SetPrice(u64);
	void SetAveCost(float);
	void SetStructure(float);
	void SetQuality(float);
	void SetInstanceInfo(char *);
	void SetActivatedEffectInstanceInfo(char *);
	void SetEquipEffectInstanceInfo(char *);
	void SetBuilderName(char *);
    void SetTradeStack(u32);

    void AddTradeStack(u32);

	short m_check; //added as public so we can check this is a valid item without crashing

private:
	_Item * Data;

	unsigned char Flags[2];
	unsigned char ExtendedFlags[3];
};

#endif
