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
#ifndef _AUXPLAYERINDEX_H_INCLUDED_
#define _AUXPLAYERINDEX_H_INCLUDED_

#include "AuxSecureInventory.h"
#include "AuxVendorInventory.h"
#include "AuxRewardInventory.h"
#include "AuxOverflowInventory.h"
#include "AuxRPGInfo.h"
#include "AuxMissions.h"
#include "AuxReputation.h"
#include "AuxGroupInfo.h"

struct _PlayerIndex
{
	u64 Credits;
	u32 XPDebt;
	_SecureInv SecureInv;
	_VendorInv VendorInv;
	_RewardInv RewardInv;
	_OverflowInv OverflowInv;
	_RPGInfo RPGInfo;
	char CommunityEventFlags[64];
	u32 MusicID;
	_Missions Missions;
	_Reputation Reputation;
	u32 PIPAvatarID;
	char RegistrationStarbase[64];
	char RegistrationStarbaseSector[64];
	char SectorName[64];
	u32 SectorNum;
	u32 ClientSendUITriggers;
	_GroupInfo GroupInfo;
} ATTRIB_PACKED;

class AuxPlayerIndex : public AuxBase
{
public:
    AuxPlayerIndex()
	{
        Construct(Flags, ExtendedFlags, 18, 0, 0);
		memset(PacketBuffer, 0, sizeof(PacketBuffer));
		PacketSize = 0;
        m_Mutex = new Mutex();

        Reset();
	}

    ~AuxPlayerIndex()
	{
        if (m_Mutex)
            delete m_Mutex;
	}

    void Reset();
    void ClearFlags();

	bool BuildPacket();
	bool BuildPacket(unsigned char *, long &);

	bool BuildExtendedPacket();
	bool BuildExtendedPacket(unsigned char *, long &);

	_PlayerIndex * GetData();

	u64 GetCredits();
	u32 GetXPDebt();
	char * GetCommunityEventFlags();
	u32 GetMusicID();
	u32 GetPIPAvatarID();
	char * GetRegistrationStarbase();
	char * GetRegistrationStarbaseSector();
	char * GetSectorName();
	u32 GetSectorNum();
	u32 GetClientSendUITriggers();

	void SetData(_PlayerIndex *);

	void SetCredits(u64);
	void SetXPDebt(u32);
	void SetCommunityEventFlags(char *);
	void SetMusicID(u32);
	void SetPIPAvatarID(u32);
	void SetRegistrationStarbase(char *);
	void SetRegistrationStarbaseSector(char *);
	void SetSectorName(char *);
	void SetSectorNum(u32);
	void SetClientSendUITriggers(u32);


private:
	_PlayerIndex Data;

	unsigned char Flags[3];
	unsigned char ExtendedFlags[5];

public:
	unsigned char PacketBuffer[20000];
	long PacketSize;

	class AuxSecureInv SecureInv;
	class AuxVendorInv VendorInv;
	class AuxRewardInv RewardInv;
	class AuxOverflowInv OverflowInv;
	class AuxRPGInfo RPGInfo;
	class AuxMissions Missions;
	class AuxReputation Reputation;
	class AuxGroupInfo GroupInfo;
};

#endif
