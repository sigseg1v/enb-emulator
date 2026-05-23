#ifndef _COMMON_PLAYER_AND_MOB_H_INCLUDED_
#define _COMMON_PLAYER_AND_MOB_H_INCLUDED_

#include "objectclass.h"
#include "Stats.h"
#include "AuxClasses\AuxPercent.h"

#define SHIELD_RECHARGE_RESUME_DELAY	10000

class Player;
// shield stuff for now, but will be useful for mobs using skills on players
class CommonPlayerAndMob : public Object
{
public:
	CommonPlayerAndMob(long object_id);
	CommonPlayerAndMob(void);
	virtual ~CommonPlayerAndMob(void);
	void ResetCommon();

// shield
	float GetShield();
	float GetShieldValue();
    void  RemoveShield(float ShieldRemoved);
    void  ShieldUpdate(unsigned long EndTime, float ChangePerTick, float StartValue);
    void  RechargeShield();
	void  RecalculateShieldRegen();
//items
	bool QualityCalculator(struct _Item * myItem);

// placeholders
	virtual float BaseShieldRecharge()				{ return 0.0f; }
	virtual class AuxPercent *ShieldAux()			{ return NULL; }
	virtual float GetMaxShield()					{ return 0.0f; }
	virtual bool  GetIsIncapacitated()				{ return false; }
	virtual void  SendAuxShip(Player * other = 0)	{ }

protected:
	unsigned long m_LastShieldChange;
    unsigned long m_ShieldRecharge;
public:
	Stats m_Stats;
	// buffs too maybe?
};

#endif