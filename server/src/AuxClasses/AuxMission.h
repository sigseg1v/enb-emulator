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

#ifndef _AUXMISSION_H_INCLUDED_
#define _AUXMISSION_H_INCLUDED_

#include "AuxMissionStages.h"
	
struct _Mission
{
	u32 ID;
	char Name[256];
	char Summary[384];
	char Reward[128];
	char FailureConsequence[128];
	char IssuingFaction[64];
	bool IsTimed;
	u32 ExpirationTime;
	u32 StartTime;
	bool IsForfeitable;
	bool IsCompleted;
	bool IsFailed;
	bool IsExpired;
	bool IsFullyVisible;
	u32 StageCount;
	u32 StageNum;
	_MissionStages Stages;
	u32 StageExpirationTime;
	bool HasGivenNewMissionMessage;

    //NOT part of Aux packets
    long DatabaseID;
    long MissionData;
} ATTRIB_PACKED;

class AuxMission : public AuxBase
{
public:
    AuxMission()
	{
	}

    ~AuxMission()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _Mission * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 19, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0x16);
		ExtendedFlags[1] = char(0x00);
		ExtendedFlags[2] = char(0x00);
		ExtendedFlags[3] = char(0xFF);
		ExtendedFlags[4] = char(0xFF);
		ExtendedFlags[5] = char(0x03);

		Data->ID = 0;		/* This is set in AuxMissions */
		*Data->Name = 0;
		*Data->Summary = 0; 
		*Data->Reward = 0;
		*Data->FailureConsequence = 0; 
		*Data->IssuingFaction = 0; 
		Data->IsTimed = 0;
		Data->ExpirationTime = 0;
		Data->StartTime = 0;
		Data->IsForfeitable = 0;
		Data->IsCompleted = 0;
		Data->IsFailed = 0;
		Data->IsExpired = 0;
		Data->IsFullyVisible = 0;
		Data->StageCount = 0;
		Data->StageNum = 0;
		Stages.Init(16, this, &Data->Stages);
		Data->StageExpirationTime = 0;
		Data->HasGivenNewMissionMessage = 0;

        Data->DatabaseID = -1;
        Data->MissionData = 0;
	}

    void Clear();
    void ClearFlags();

	void BuildPacket(unsigned char *, long &);
	void BuildExtendedPacket(unsigned char *, long &);

	_Mission * GetData();

	u32 GetID();
	char * GetName();
	char * GetSummary();
	char * GetReward();
	char * GetFailureConsequence();
	char * GetIssuingFaction();
	bool GetIsTimed();
	u32 GetExpirationTime();
	u32 GetStartTime();
	bool GetIsForfeitable();
	bool GetIsCompleted();
	bool GetIsFailed();
	bool GetIsExpired();
	bool GetIsFullyVisible();
	u32 GetStageCount();
	u32 GetStageNum();
	u32 GetStageExpirationTime();
	bool GetHasGivenNewMissionMessage();

    long GetDatabaseID();
    long GetMissionData();

	void SetData(_Mission *);
	void SetAllFlags();

	void SetID(u32);
	void SetName(char *);
	void SetSummary(char *);
	void SetReward(char *);
	void SetFailureConsequence(char *);
	void SetIssuingFaction(char *);
	void SetIsTimed(bool);
	void SetExpirationTime(u32);
	void SetStartTime(u32);
	void SetIsForfeitable(bool);
	void SetIsCompleted(bool);
	void SetIsFailed(bool);
	void SetIsExpired(bool);
	void SetIsFullyVisible(bool);
	void SetStageCount(u32);
	void SetStageNum(u32);
	void SetStageExpirationTime(u32);
	void SetHasGivenNewMissionMessage(bool);

    void SetDatabaseID(long);
    void SetMissionData(long);

private:
	_Mission * Data;

	unsigned char Flags[3];
	unsigned char ExtendedFlags[6];

public:
	class AuxMissionStages Stages;
};

#endif
