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
#ifndef _AUXSHIPINV_H_INCLUDED_
#define _AUXSHIPINV_H_INCLUDED_

#include "AuxMounts.h"
#include "AuxMountBoneNames.h"
#include "AuxInventory40.h"
#include "AuxEquipInventory.h"
#include "AuxInventory20.h"
#include "AuxInventory6.h"
	
struct _ShipInv
{
	u32 CargoSpace;
	char EquipMountModel[20];
	_Mounts Mounts;
	_MountBones MountBones;
	u32 FutureWeapons;
	u32 FutureDevices;
	_Inventory40 CargoInv;
	_EquipInv EquipInv;
	_Inventory20 AmmoInv;
	_Inventory20 HullInv;
	_Inventory6 TradeInv;
} ATTRIB_PACKED;

class AuxShipInv : public AuxBase
{
public:
    AuxShipInv()
	{
	}

    ~AuxShipInv()
	{
	}

	void Init(unsigned int MemberIndex, class AuxBase * Parent, _ShipInv * DataPointer)
	{
		Construct(Flags, ExtendedFlags, 11, Parent, MemberIndex);
        Data = DataPointer;

		memset(Flags,0, sizeof(Flags));

		ExtendedFlags[0] = char(0xE6);
		ExtendedFlags[1] = char(0xDC);
		ExtendedFlags[2] = char(0xFE);
		ExtendedFlags[3] = char(0x03);

		Data->CargoSpace = 0;
		*Data->EquipMountModel = 0;
		strcpy_s(Data->EquipMountModel, sizeof(Data->EquipMountModel), "tvf01_1");
		Data->EquipMountModel[sizeof(Data->EquipMountModel)-1] = '\0';
		Mounts.Init(2, this, &Data->Mounts);
		MountBones.Init(3, this, &Data->MountBones);
		Data->FutureWeapons = 0;
		Data->FutureDevices = 0;
		CargoInv.Init(6, this, &Data->CargoInv);
		EquipInv.Init(7, this, &Data->EquipInv);
		AmmoInv.Init(8, this, &Data->AmmoInv);
		HullInv.Init(9, this, &Data->HullInv);
		TradeInv.Init(10, this, &Data->TradeInv);
	}

    void ClearFlags();

	void BuildPacket(unsigned char *, long &);
	void BuildExtendedPacket(unsigned char *, long &);

	_ShipInv * GetData();

	u32 GetCargoSpace();
	char * GetEquipMountModel();
	u32 GetFutureWeapons();
	u32 GetFutureDevices();

	void SetData(_ShipInv *);

	void SetCargoSpace(u32);
	void SetEquipMountModel(char *);
	void SetFutureWeapons(u32);
	void SetFutureDevices(u32);

private:
	_ShipInv * Data;

	unsigned char Flags[2];
	unsigned char ExtendedFlags[4];

public:
	class AuxMounts Mounts;
	class AuxMountBoneNames MountBones;
	class AuxInventory40 CargoInv;
	class AuxEquipInv EquipInv;
	class AuxInventory20 AmmoInv;
	class AuxInventory20 HullInv;
	class AuxInventory6 TradeInv;
};

#endif
