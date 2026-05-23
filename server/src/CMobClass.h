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
#ifndef _COMMON_PLAYER_AND_MOB_H_INCLUDED_
#define _COMMON_PLAYER_AND_MOB_H_INCLUDED_

#include "objectclass.h"
#include "Stats.h"
#include "CMobBuffs.h"
#include "AuxClasses\AuxPercent.h"
#include "PlayerSkills.h"

#define SHIELD_RECHARGE_RESUME_DELAY	10000

class Player;
class AbilityBase;

typedef enum
{
	HACK_ENGINE,
	HACK_REACTOR,
	HACK_SHIELD,
	HACK_DEVICE,    // hack and biorepression
	HACK_WEAPON,    // hack and biorepression
	HACK_COMMS,
} HACK_Type;

typedef enum
{
	BEFRIEND_RELATION,
	BEFRIEND_CALM,
} Befriend_Type;

typedef enum
{
	AGGRESSION_NONE,
	AGGRESSION_CAUTIOUS,
	AGGRESSION_ANTAGONISTIC,
	AGGRESSION_FANATIC
} MOB_Aggression;

struct DamageShield
{
	long shield_id; //unique id to make removing this damage shield easy 
		//reccomend setting this to the SkillID / Item ID or the Skill / item creating this shield. 
		//The add function will automatically disallow duplicates.)
	int remove_buff_id; // id of the buff associated with this shield
	short shield_level; // this is needed to facilitate refreshing/checking for overwrite/checking for better already
	float capacitance; //how much damage does this shield absorb? (set to -1 if none absorbed)
	bool has_capacitance; //so we know if we should remove this buff
	int reflect_type; //Type of damage this shield reflects.  (For future modification).
	float reflect_percentage; //% value (50% = .5) of damage sent back to attacker
	float reflect_max; //maximum damage that can be reflected (set to -1 if no limit / Not applicable)
	float to_reactor_percentage; //% value of damage converted to reactor power
	float to_reactor_max; //maximum amount of reactor gained on a single attack (set to -1 if no limit / not applicable)
	bool reactor_to_group; // reactor gain goes to group members
	DamageShield *next;
} ATTRIB_PACKED;

// shield stuff for now, but will be useful for mobs using skills on players
class CMob : public Object
{
public:
	CMob(long object_id);
	CMob(void);
	virtual ~CMob(void);
	void ResetCommon();

// shield/hull
	float GetShield();
	float GetShieldValue();
    void  RemoveShield(float ShieldRemoved);
    void  ShieldUpdate(unsigned long EndTime, float ChangePerTick, float StartValue);
    void  RechargeShield();
	void  RecalculateShieldRegen(float MaxShield=0.0f, float RechargeShield=0.0f, bool pullData=true);
	virtual void RemoveHull(float hull_dmg, CMob *enemy) { };
	float CommonDamageHandling(Object *source, long damage_type, float damage);
// items
	bool  QualityCalculatorEffects(struct _Item * myItem);
	bool  QualityCalculator(struct _Item * myItem);
// skills
	void  DeleteAbilities();
	void  SetupAbilities();
	void  ResetAbilities();
	void  AbilityRemove(int AbilityID, long activation_ID);
	void  SetCurrentSkill(AbilityBase* CurrentSkill = NULL) { m_CurrentSkill = CurrentSkill; };
	AbilityBase	*GetCurrentSkill() { return m_CurrentSkill; };
	bool IsEnemyOf(CMob *target);
	bool IsFriendOf(CMob *target);
	virtual bool Menace(Object* obj,long skillLevel, float seconds,long EffectID,bool deny_skill,bool deny_weapon) { return false; };
	virtual void Hack(CMob *hacker, HACK_Type hack) { };
	virtual void Befriend(CMob *afriend, Befriend_Type type) { };
	virtual void Taunt(CMob *taunter)				{ };
	virtual void SetHalfSpeed(bool on)				{ };
	virtual void SetIsCloaked(bool on)				{ };
	virtual void BlankRangeList(bool group = false)	{ };
	virtual void RemovePlayerFromView(bool group = false) { };
	virtual bool GetIsCloaked()						{ return false; };
	virtual void SelfDestruct()						{ };
	virtual void RechargeReactor(float removedrain = -1.0f)	{ };
	virtual float DrainReactor(unsigned long, float){ return 0.0f; };
	int	 GetStealthLevel()							{ return m_StealthLevel; };
	void SetStealthLevel(int NewStealth)			{ m_StealthLevel = NewStealth; };
// damageshields
	void RemoveDamageShield(DamageShield *damage_shield);
	void RemoveDamageShield(long shield_id, bool doUpdate=true);
	void AddDamageShield(long shield_id, short shield_level = 1, int remove_buff_id = -1, float capacitance = 0, float reflect_percentage = 0, 
						 float reflect_max = -1.0f, float to_reactor_percentage = 0, float to_reactor_max = -1.0f, bool reactor_to_group = false);
	DamageShield* FindDamageShield(long shield_id);
	void ClearDamageShields(bool delete_only=false);
// targeting
    bool IsClickedBy(Player *player);
    void SetClickedBy(Player *player);
	void UnSetClickedBy(Player *player);

// placeholders
	virtual float BaseShieldRecharge()				{ return 0.0f; }
	virtual class AuxPercent *ShieldAux()			{ return NULL; }
	virtual float GetMaxShield()					{ return 0.0f; }
    virtual float GetHullPoints()					{ return 0.0f; }
	virtual void  SetHullPoints(float points)		{ }
    virtual float GetMaxHullPoints()				{ return 0.0f; }
	virtual bool  GetIsIncapacitated()				{ return false; }
	virtual void  SendAuxShip(Player * other = 0)	{ }
    virtual long  TotalLevel()						{ return Level(); }
    virtual long  CombatLevel()						{ return Level(); }
    virtual bool  InSpace()							{ return true; }
    virtual bool  WarpDrive()						{ return false; }
	virtual void  TerminateWarp(bool player_forced = false) { }
    virtual bool  Prospecting()						{ return false; }
    virtual long  GroupID()							{ return -1; }
	virtual bool  IsTurret()						{ return false; }
	virtual float GetEnergyValue()					{ return 1e6; }
	virtual float GetEnergy()						{ return 1.0f; }
	virtual float GetMaxEnergy()					{ return 1e6; }
	virtual void  RemoveEnergy(float, long rch=1000){ }
	virtual void  RecalculateEnergyRegen(float MaxEnergy=0.0f, float RechargeEnergy=0.0f, bool pullData=true) { }
	virtual void  SendVaMessage(char *msg, ...)		{ }
	virtual void  SendVaMessageC(char colour, char *msg, ...) { }
	virtual void  SendPriorityMessageString(char *msg1, char *msg2, long time, long priority) { }
	virtual void  AddHate(long GameID, long Hate)   { }
	virtual void  RadiationDmg(bool enb)				{ }
	virtual MOB_Aggression GetAggression()			{ return AGGRESSION_NONE; }
	virtual bool  IsOrganic()						{ return false; }
	virtual void  SendClientSound(char *sound_name, long channel = 0, char queue = 0, long level = -1)			{ }

protected:
	float CaughtInEnergyBlast(float blast_range);
	
protected:
	u32			  m_ClickedList[MAX_ONLINE_PLAYERS/32 + 1]; // list of players who have clicked on this object
	unsigned long m_LastShieldChange;
    unsigned long m_ShieldRecharge;
	AbilityBase	 *m_CurrentSkill;
	AbilityBase	 *m_AbilityList[MAX_ABILITY_IDS];
	AbilityBase  *m_CombatTrance;		    		// Combat Trance Passive
	DamageShield *m_ShieldListHead;
	int			  m_StealthLevel;
public:
	Stats		m_Stats;
	CMobBuffs	m_Buffs;
};

#endif