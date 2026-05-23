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
#ifndef _AUXHARVESTABLE_H_INCLUDED_
#define _AUXHARVESTABLE_H_INCLUDED_

#include "AuxInventory40.h"

struct _Harvestable
{
	char Name[64];
	_Inventory40 CargoInv;
	float PercentFull;
	u32 TechLevel;
} ATTRIB_PACKED;

class AuxHarvestable : public AuxBase
{
public:
    AuxHarvestable()
	{
		m_Max_Buffer = 2000;
		Construct(Flags, ExtendedFlags, 4, 0, 0);
		memset(PacketBuffer, 0, sizeof(PacketBuffer));
		PacketSize = 0;

        Reset();
	}

    ~AuxHarvestable()
	{
	}

    void Reset();
    void ClearFlags();

	bool BuildPacket(bool = false);
	bool BuildPacket(unsigned char *, long &, bool = false);

	bool BuildNamePacket();
	bool BuildNamePacket(unsigned char *, long &);

	_Harvestable * GetData();

	u32 GetGameID();
	char * GetName();
	float GetPercentFull();
	u32 GetTechLevel();

	void SetData(_Harvestable *);

	void SetGameID(u32);
	void SetName(char *);
	void SetPercentFull(float);
	void SetTechLevel(u32);


private:
	u32 GameID;

	_Harvestable Data;

	unsigned char Flags[1];
	unsigned char ExtendedFlags[2];

public:
	unsigned char PacketBuffer[2000];
	long PacketSize;

	class AuxInventory40 CargoInv;
};

#endif
