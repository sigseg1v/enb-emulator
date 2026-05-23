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
#ifndef _AUXMANUFACTURINGINDEX_H_INCLUDED_
#define _AUXMANUFACTURINGINDEX_H_INCLUDED_

#include "AuxInventory6.h"
#include "AuxInventory1.h"
#include "AuxPrimaryCategories.h"
#include "AuxKnownFormulas.h"
#include "AuxPreviousAttempts.h"

struct _ManufacturingIndex
{
	char Name[20];
	u32 Mode;
	u32 Validity;
	char FailureMessage[40];
	u32 Difficulty;
    _Inventory1 Target;
    _Inventory6 Components;
    _Inventory1 Override;
    u64 NegotiatedCost;
    u64 BaseCost;
    _PrimaryCategories PrimaryCategories;
    _KnownFormulas KnownFormulas;
    _PreviousAttempts PreviousAttempts;
	u32 CurrentItemCat;
	float SuccessProbability;
	float CriticalSuccessProbability;
	float ExpectedQuality;
	float MinimumQuality;
	float MaximumQuality;
	u32 AdditionalIterations;   // No idea what this is for
	u32 TechFilterBitField;
} ATTRIB_PACKED;

class AuxManufacturingIndex : public AuxBase
{
public:
    AuxManufacturingIndex()
	{
		Construct(Flags, ExtendedFlags, 21, 0, 0);
		memset(PacketBuffer, 0, sizeof(PacketBuffer));
		PacketSize = 0;
        m_Mutex = new Mutex;

        Reset();
	}

    ~AuxManufacturingIndex()
	{
        if (m_Mutex)
            delete m_Mutex;
	}

    void Reset();
    void InitializeCategories();
    void ClearFlags();

    bool BuildPacket(bool = false);
	bool BuildPacket(unsigned char *, long &, bool = false);

    void TerminalReset(unsigned int);
	void ResetManuItems();

	_ManufacturingIndex * GetData();

	u32 GetGameID();
	char * GetName();
	u32 GetMode();
	u32 GetValidity();
	char * GetFailureMessage();
	u32 GetDifficulty();
    u64 GetNegotiatedCost();
    u64 GetBaseCost();
	u32 GetCurrentItemCat();
	float GetSuccessProbability();
	float GetCriticalSuccessProbability();
	float GetExpectedQuality();
	float GetMinimumQuality();
	float GetMaximumQuality();
	u32 GetAdditionalIterations();
	u32 GetTechFilterBitField();

	void SetData(_ManufacturingIndex *);

	void SetGameID(u32);
	void SetName(char *);
	void SetMode(u32);
	void SetValidity(u32);
	void SetFailureMessage(char *);
	void SetDifficulty(u32);
    void SetNegotiatedCost(u64);
    void SetBaseCost(u64);
	void SetCurrentItemCat(u32);
	void SetSuccessProbability(float);
	void SetCriticalSuccessProbability(float);
	void SetExpectedQuality(float);
	void SetMinimumQuality(float);
	void SetMaximumQuality(float);
	void SetAdditionalIterations(u32);
	void SetTechFilterBitField(u32);


private:
	u32 GameID;

	_ManufacturingIndex Data;

	unsigned char Flags[4];
	unsigned char ExtendedFlags[6];

public:
	unsigned char PacketBuffer[20000];
	long PacketSize;

	class AuxInventory1 Target;
	class AuxInventory6 Components;
	class AuxInventory1 Override;
	class AuxPrimaryCategories PrimaryCategories;
	class AuxKnownFormulas KnownFormulas;
	class AuxPreviousAttempts PreviousAttempts;
};

#endif
