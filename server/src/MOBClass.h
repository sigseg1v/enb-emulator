// MOBClass.h
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

#ifndef _MOB_CLASS_H_INCLUDED_
#define _MOB_CLASS_H_INCLUDED_

#include "AuxClasses\AuxMobIndex.h"
#include "CMobClass.h"
#include "HuskClass.h"
#include "MOBDatabase.h"

class Player;
class Stats;

typedef std::vector<Object*> ObjectList;

#define HATE_SIZE		6	// at least a full group accurately
#define DAMAGE_SIZE		12

struct Hate_Info
{
	long GameID;
	long hate;
} ATTRIB_PACKED;

struct Damage_Info
{
	long GameID;
	long GroupID;
	long damage;
} ATTRIB_PACKED;

typedef enum
{
	INVALID_BEHAVIOUR,
    PATROL_FIELD,	// only for field guardians
    PATROL_NAV,
    PATROL_POSITION,
    NAV_ROUTE,		// MOB must be given a set of Navs to visit
    HUNT,			// given a point in space the MOB will hunt around this
    CLUSTER,		// will seek out MOBs of the same type
    CURIOUS,		// will check out random objects in space it can see
	DRIFT,
    PURSUE,			// when in pursue mode MOBs will chase their target
	TURRET,			// Is stationary and will not fire on Players, will attack ALL mobs
	TURRET_ATTACK,	// When a turret attacks
	MENACE			// Menace affecting target
} MOB_Behaviour;

// types "ROBOTIC" and "MANNED" are now used to determine trash loot types
// in addition to whatever they were doing before.
typedef enum
{
    ROBOTIC, // use to be 'CYBERNETIC' 
    MANNED, // use to be 'STRUCTURAL'
    ORGANIC_RED,
    ORGANIC_GREEN,
    CRYSTALLINE,
    ENERGY,
    ROCK_BASED
} MOB_TYPE;

class MOB : public CMob
{
	friend class Stats;

public:
    MOB (long object_id);
    virtual ~MOB ();

    unsigned long RespawnTick()                     { return m_Respawn_tick; };
    void        SetRespawnTick(unsigned long respawn)       { m_Respawn_tick = respawn; };
	void		SetRespawnTime(unsigned long respawn_time)	{ m_Respawn_time = respawn_time;};

    void        UpdateObject(u32 current_tick);
    void        SendMOBData(Player *p);

    void        SendToVisibilityList(bool force_update = false);
    void        SendPosition(Player *player);
    void        SendAuxDataPacket(Player *player);
    void        OnCreate(Player *player);
    void        OnTargeted(Player *player);
	void 		OnUnTargeted(Player *player);
	void		SendRelationship(Player *player);
	void		SendAggroRelationship(Player *p);
	void		CheckWarningShots(Player *p);
	void		CheckAggro(Player *p);

    float       DamageObject(long game_id, long damage_type, float damage, long inflicted);
	void		RemoveHull(float hull_dmg, CMob *enemy);
	long		GetNextGroupLooter(Husk *husk, int GroupID);
    void        SendMobDamage(float damage, float bonus_damage, long damage_type, long game_id, long inflicted);
    void        DestroyMOB(long killing_blow);
	void		SelfDestruct() { DestroyMOB(GameID()); }
    void        SendExplosion();
	void		DebuffDisplay(ObjectEffect *DebuffEffect);
	void		SendShieldLevel(Player *p);
    void        Remove();
    void        OnRespawn();

    void        SetMOBType(short type);
    void        SetLevel(short type);
	void		SetName(char *name);

	//shield
    float       BaseShieldRecharge()            { return pow(2.1f,m_Level/5) / GetMaxShield() / 1000.0f; }
	class AuxPercent *ShieldAux()				{ return &m_ShipIndex.Shield; }
	float		GetMaxShield()					{ return m_ShipIndex.GetMaxShield(); }
	short		GetMOBType()					{ return m_MOB_Type; }
	float       GetHullPoints()                 { return m_ShipIndex.GetHullPoints(); }
	void		SetHullPoints(float points)		{ m_ShipIndex.SetHullPoints(points); }
	float       GetMaxHullPoints()              { return m_ShipIndex.GetMaxHullPoints(); }
	void		SetMaxHullPoints(float points)	{ m_ShipIndex.SetMaxHullPoints(points); }
	float		GetShieldLevel()				{ return GetShieldValue(); }
	float		GetMOBDPS()						{ return m_WeaponDPS; }
	bool		GetIsIncapacitated()			{ return !m_Active; }
	bool		IsOrganic()						{ return (m_MOB_Data ? m_MOB_Data->m_Type : ROBOTIC) > MANNED; }
	void		SetRandomAttackRange()			{ m_AttackRange = m_WeaponFiringRange*((((float)(rand()%10)-5.0f)/20.0f) + 1.0f); } //use multiplier between 0.75f and 1.25f
	void		SetIsCloaked(bool on)			{ m_ShipIndex.SetIsCloaked(on); };
	bool		GetIsCloaked()					{ return m_ShipIndex.GetIsCloaked(); };

	// hate and damage lists
	void		AddHate(long GameID, long Hate);		// Add Hate to a player
	void		SubtractHate(long GameID, long Hate);
	void		RemoveHate(long GameID);
	long		GetMaxHateID();
	void		AddDamage(long GameID, long Damage);
	long		GetMaxDamageID();

	char *		GetSpawnName();

	void		DisplayLoot(Player *p);

    //Targetting and attacking
    void        ChooseTarget();
	void		LockTarget();
	void		LockMOBTarget(MOB *mob);
	void		LockTurretTarget();
    void        HandleAttack();
	void		FireWeapon(Object *obj);
	bool 		UseSkill();
    void        SendFXToVisibilityList(long target_id, long source_id, long time_delay, short effect_id);
    void        AddBehaviourObject(Object *obj);
    void        AddBehaviourObject(char *name);
    void        AddBehaviourPosition(float *pos);
    void        SetBehaviour(short new_behaviour);
    void        LostTarget(Player *p = (0), bool incap = false);
	void		LostTarget(MOB *b);
	float		GetStealthDetectionLevel();
	float		GetScanSkill()						{return m_ScanSkill;};
	void		SetScanSkill(float NewScanSkill)	{m_ScanSkill = NewScanSkill;};
	// Menace
	bool		Menace(Object* obj,long skillLevel, float seconds,long EffectID,bool deny_skill,bool deny_weapon);

    //movement
    long        TimeToDestination()                 { return (m_ArrivalTime - GetNet7TickCount()); };
    void        CalcNewHeading(float tdiff);
    void        SetDefaultStats(float turn, short behaviour, float velocity, long update);        //TODO: expand this to set up some initial states
	void		MovePosition(float x, float y, float z, bool _override = true);

    //rangelist handling
    void        UpdateObjectVisibilityList(u32 check_tick = 0);
    bool        ObjectOnRangeList(ObjectList *object_list, Object *obj);
    void        AddObjectToRangeList(ObjectList *object_list, Object *obj);
    void        RemoveObjectFromRangeList(ObjectList *object_list, Object *obj);
    void        RemovePlayerFromRangeLists(Player *p);
	short		GetGroupPointers(CMob **list,float within_range); // for skills

    //MOB specific
    void        UpdateMOB(u32 current_tick, bool handle_attacks, u32 check_tick);
	void		SendAuxShip(Player * other);
	void		SetIsTurret()						{ m_Behaviour = TURRET; }
	bool        IsTurret()							{ return m_DefaultBehaviour == TURRET; }

	void		SetSpawnGroup(Object *obj)			{ m_SpawnGroup = obj; };
	Object	  * SpawnGroup()						{ return (m_SpawnGroup); };
	MOB_Aggression GetAggression();
	void		Taunt(CMob *taunter);
	void		Hack(CMob *hacker, HACK_Type hack);
	void		Befriend(CMob *afriend, Befriend_Type type);
	long		GetMOBAggroLevel()					{ return (m_MOB_Data->m_Agressiveness); }

	//command queue
	void		_SendAuxDataPacket(Player *p);

	void		BlastDamage(Player *p, float *position, float damage);
	

private:
    bool        CheckNoAttackingMOBs(Player *p);
    bool        PlayerInRangeList(Player *p_check);
    void        AddPlayerToRangeList(Player *p);
    void        RemovePlayerFromRangeList(Player *p);
	void		SendAuxDiff(Player *p, long type);

    void        MOBBehaviour();
    void        TravelToObject();
    void        TravelToPosition(float *position);
    void        UpdatePositionInfo(PositionInformation *pos);
    void        PursueTarget();
	bool		IsDetectable(Player *p);
	void		HandleMovementChanges();
	Object	  * PickObject();
	void		ExamineTarget();
	void		SetupRandomMovement();
	void		MOBEffect(long player_id, long effect_id);
	void		ResetMOBSpawnPosition();
	bool		MOBWillAggro(Player *p);
	void		GoingHome();
	bool		NoFaction()						{ return m_FactionID == 0; }
	bool		CheckHateList(long GameID);

	//private for command queue
	void		SendAuxShieldUpdate(bool shields=true, bool hull=false);

private:		
	AuxMobIndex		m_ShipIndex;				// Use MOB aux rather than PlayerShip Aux, more efficient and more relevant.
	Hate_Info		m_HateList[HATE_SIZE];		// Hate List for mobs, only track top #HATE_SIZE haterz.
	Damage_Info		m_DamageList[DAMAGE_SIZE];	// Damage List for determining loot rights
    unsigned long   m_Respawn_tick;
	unsigned long	m_Respawn_time;
    short           m_MOB_Type;
    unsigned long   m_LastAttackTime;
	unsigned long   m_SkillTime;
	MOBData		  * m_MOB_Data;
	float			m_LastTickHullLevel;

	u32				m_PlayerVisibleList[MAX_ONLINE_PLAYERS/32 + 1]; // list of Players visible to this MOB.
    ObjectList      m_MOBVisibleList;       // list of MOBs visible to this MOB.

	Object		  * m_SpawnGroup;			// this points to either a field or MOB spawn, if this MOB is spawned
    ObjectList      m_BehaviourList;        // list of objects that the behviour processing will use.
    float           m_HomePosition[3];
    u32             m_ArrivalTime;
    short           m_Behaviour;
    short           m_DefaultBehaviour;
    Object        * m_Destination;
    float           m_DestinationPos[3];
    bool            m_DestinationFlag;

	bool			m_Attacking;

    float           m_DefaultTurn;
    float           m_DefaultVelocity;
	float			m_PursuitVelocity;
	float			m_ScanRange;
	float			m_AggroRange;
	float			m_ScanSkill;
    long            m_DefaultUpdateRate;
    short           m_ShieldFXSent;
	u32				m_DamageTime;
    float           m_WeaponDPS;
    short           m_DamageType;
	short			m_WeaponFX;
	short			m_WeaponDamageDelay;
	float			m_WeaponReloadTime;
	int				m_WeaponAmmoPerShot;
	float			m_WeaponFiringRange;
	float			m_AttackRange;
	bool			m_GoingHome;

    TimeNode        m_DamageNode;

	//menace members
	unsigned long m_MenaceExpire;
	long		  m_MenaceID;
	Object*       m_MenaceObject;
	short		  m_MencaceOldBehaviour;
	bool		  m_Menace;
	bool          m_MenaceDenyWeapon;
	bool          m_MenaceDenySkill;

	float		  m_ShieldModifier;
	float		  m_DamageModifier;
	float		  m_RangeModifier;
};

struct MOB_Info
{
    char *name;
    short basset;
    short type;
    short behaviour;
    u32   hull;
    u32   shield;
    u8    level;
    float DPS;
} ATTRIB_PACKED;

MOB_Info MobData[];

#endif // _MOB_CLASS_H_INCLUDED_