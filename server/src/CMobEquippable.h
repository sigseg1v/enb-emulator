#ifndef _EQUIPABLE_H_INCLUDED_
#define _EQUIPABLE_H_INCLUDED_

#include "ItemBase.h"
#include "AuxClasses/AuxEquipItem.h"
#include "TimeNode.h"
#include "Mutex.h"

#define MAX_EQUIP_STATS 60

typedef enum
{
    EQUIP_SHIELD,
    EQUIP_REACTOR,
    EQUIP_ENGINE,
    EQUIP_WEAPON,
    EQUIP_DEVICE
} EquipType;

class Object;
class Player;

class Equipable
{
public:
    Equipable();
    ~Equipable();

    void Init(Player *, int);

    bool CanEquip(_Item *);
	bool InvalidType(long slot);
    _Item Equip(_Item *, bool delay = false);
    _Item EquipAmmo(_Item *);
    bool CorrectAmmo(_Item *);

    void FinishInstall(Player *p = 0, int Slot = -1);
    void Hack(unsigned long);

    void PullAuxData();

    void ManualActivate();
    void CheckAutoActivate();

	void ShootAmmo(int Target, unsigned int quantity);		// Shoot ammo
	void UpdateRange();
	void UpdateRange(float beamRangeBMult, float beamRangeDMult,
					float beamRangeBValue, float beamRangeDValue,
					float projRangeBMult, float projRangeDMult,
					float projRangeBValue, float projRangeDValue,
					float missRangeBMult, float missRangeDMult,
					float missRangeBValue, float missRangeDValue);

    void CancelAutofire();

    void CoolDown();

	void Install(unsigned long); //moved to public so we can call installs at end of login

    ItemBase * GetItemBase();
    ItemInstance * GetItemInstance();

	_Item * GetItem();

	float GetQuality();

	bool ItemReady();
	bool ItemInstalled();
	void Lock()
	{
		//printf("Equippable::Lock() locking mutex\n");
		m_Mutex.Lock();
	}
	void Unlock()
	{
		//printf("Equippable::Unlock() unlocking mutex\n");
		m_Mutex.Unlock();
	}

private:
    void AddEffects();
    void RemoveEffects();

    void SetItemInstance();
    void SetAmmoInstance();

    void SetStats(bool Remove = false);
	void EquipEffects(int RemoveStat);

    void AddItemStateFlag(unsigned long);
    void RemoveItemStateFlag(unsigned long);

    void RemoveTimeNode(TimeNode *node);

    bool Activate();
	bool Reload(unsigned int quantity);
    bool UseWeapon(Object * Target);
    bool UseDevice(Object * Target);
	//Device,Shield,Reactor, Engine
	float DamageMult(float Damage);

    bool CheckRange(Object * Target);
    bool CheckOrientation(Object * Target);

	void EquipDevice(bool equip);
    
//Private non-locking functions

	void _Init(Player *, int);
	bool _CanEquip(_Item *);
    bool _CorrectAmmo(_Item *);
	_Item _Equip(_Item *, bool delay);
    _Item _EquipAmmo(_Item *);
	void _UpdateRange(float beamRangeBMult, float beamRangeDMult,
						float beamRangeBValue, float beamRangeDValue,
						float projRangeBMult, float projRangeDMult,
						float projRangeBValue, float projRangeDValue,
						float missRangeBMult, float missRangeDMult,
						float missRangeBValue, float missRangeDValue);
	void _FinishInstall(Player *p = 0, int Slot = -1);
	void _ManualActivate();
    void _CoolDown();
	void _ShootAmmo(int Target, unsigned int quantity);		// Shoot ammo

private:
    EquipType m_Type;

    long	  m_PlayerID;

    ItemBase * m_ItemBase;
	ItemBase * m_AmmoBase;
    
    ItemInstance m_ItemInstance;
    AmmoInstance m_AmmoInstance;
	EffectInstance m_EffectInstance;

    TimeNode *m_EquipTimeNode;
	TimeNode *m_CoolDownNode;

    AuxEquipItem * m_AuxEquipItem;
    AuxItem * m_AuxAmmoItem;

	int m_StatIDs[MAX_EQUIP_STATS];		// Save Stat ID's to remove them
	int m_EEffectID;
	int m_MaxID;

    unsigned long m_ReadyTime;
    bool m_UsesAmmo;
    int m_Slot;
	float m_Range;
	int m_Target;
	bool m_first_equip;
	bool m_autoactivate;
	Mutex m_Mutex;
};

#endif