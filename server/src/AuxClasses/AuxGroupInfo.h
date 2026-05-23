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
#ifndef _AUXGROUPINFO_H_INCLUDED_
#define _AUXGROUPINFO_H_INCLUDED_

#include "AuxGroupMembers.h"
	
struct _GroupInfo
{
	bool	IsGroupLeader;
	bool	LookingForGroup;
	bool	AllowGroupInvite;
	bool	ShowNonCombatantActivities;
	bool	ForceAutoSplit;
	bool	RestrictedLootingRights;
	bool	AutoReleaseLootingRights;
	char	FormationName[64];
	u32		Formation;
	u32		Position;
	_GroupMembers Members;
} ATTRIB_PACKED;

class AuxGroupInfo : public AuxBase
{
public:
    AuxGroupInfo()
	{
	}

    ~AuxGroupInfo()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _GroupInfo * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 11, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0xF6);
		ExtendedFlags[1] = char(0x07);
		ExtendedFlags[2] = char(0xC0);
		ExtendedFlags[3] = char(0x03);

		Data->IsGroupLeader = false;
		Data->LookingForGroup = false;
		Data->AllowGroupInvite = true;
		Data->ShowNonCombatantActivities = true;
		Data->ForceAutoSplit = true;
		Data->RestrictedLootingRights = false;
		Data->AutoReleaseLootingRights = false;
		*Data->FormationName = 0;
		Data->Formation = 0;
		Data->Position = 0;

		Members.Init(10, this, &Data->Members);
	}

    void ClearFlags();

	void BuildPacket(unsigned char *, long &);
	void BuildExtendedPacket(unsigned char *, long &);

	_GroupInfo * GetData();

	bool GetIsGroupLeader();
	bool GetLookingForGroup();
	bool GetAllowGroupInvite();
	bool GetShowNonCombatantActivities();
	bool GetForceAutoSplit();
	bool GetRestrictedLootingRights();
	bool GetAutoReleaseLootingRights();
	char * GetFormationName();
	u32 GetFormation();
	u32 GetPosition();

	void SetData(_GroupInfo *NewData, bool group_only);

	void SetIsGroupLeader(bool);
	void SetLookingForGroup(bool);
	void SetAllowGroupInvite(bool);
	void SetShowNonCombatantActivities(bool);
	void SetForceAutoSplit(bool);
	void SetRestrictedLootingRights(bool);
	void SetAutoReleaseLootingRights(bool);
	void SetFormationName(char *);
	void SetFormation(u32);
	void SetPosition(u32);

private:
	_GroupInfo * Data;

	unsigned char Flags[2];
	unsigned char ExtendedFlags[4];

public:
	class AuxGroupMembers Members;
};

#endif
