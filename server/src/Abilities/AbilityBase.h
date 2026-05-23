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
#ifndef _ABILITY_BASE_H_INCLUDED_
#define _ABILITY_BASE_H_INCLUDED_

/*
* A note on the difference between "SkillLevel" and "SkillRank".
* -SkillLevel refers to the actual level of the skill that the player has. That is, how
*   many golden buttons appear on his or her character screen for a given skill.
* -SkillRank refers to the actual rank of the skill being used. For example, the Skill earned
*   by spending a single point in the skill is the 1st Rank of a skill, the skill obtained by
*   putting 3 golden buttons into a skill is the 3rd rank and so on.
* -SkillRank will tell you if they are using the AoE version of a skill or not. SkillLevel will
*   tell you how much skill to use in your calculations for a given SkillRank of a skill.
*/

// now changed to use a common base between player and mobs so that mobs can use skills on players

#include "Net7.h"
#include "Mutex.h"
#include "PlayerSkills.h"
#include "Stats.h"

class Object;
class CMob;
class MOB;
class Player;

union proxparam
{
	long lng;
	float flt;
	void *struc;
	proxparam()			{ lng = 0; }
	proxparam(long l)	{ lng = l; }
	proxparam(float f)	{ flt = f; }
	proxparam(void *s)	{ struc = s; }
};

typedef enum
{
	WARPING,
	SHOOTING,
	LOOTING,		// cloak
	ITEM_USE,		// unused?
	INCAPACITATE,
	OTHER
};

//this is the skill cap, after all buffs/bonuses have been applied.
#define MAX_SKILL_LEVEL 10

class AbilityBase
{
protected:
	AbilityBase(CMob *me, char *stat_skill=NULL);
public:
	virtual ~AbilityBase() {};

protected:
	// Virtual methods
	virtual float CalculateEnergy ( float SkillLevel, long SkillRank ) { return 0.0f; };		// required energy
	virtual float CalculateChargeUpTime ( float SkillLevel, long SkillRank ) { return 0.0f; };	// time between click and activation
	virtual float CalculateCoolDownTime ( float SkillLevel, long SkillRank ) { return 0.0f; };	// time between 2 uses of the skill
	virtual float CalculateRange ( float SkillLevel, long SkillRank ) { return 0.0f; };			// maximum range of target
	virtual float CalculateAOE ( float SkillLevel, long SkillRank ) { return CalculateRange(SkillLevel, SkillRank); }; // max area of effect range
	virtual long  DetermineSkillRank (int SkillID) {return 0; };								// calculate m_SkillRank

public:
	void Init(CMob *me);	// setup the owner

	virtual bool CanUse(long TargetID, long AbilityID, long SkillID);			// Used to see if ability can be used on target
	virtual	bool Use(long TargetID);											// When a player tries to use an ability
	virtual bool Update(long activation_ID);									// Used to delay the when the skills fires
	virtual void Confirmation(bool Confirm, long AbilityID, long GameID) {};	// Used when a confirmation is needed
	virtual void Execute() {};													// Used when confirmation is accepted
																
	// (some abilitys can not be activated together)
	//Write in a way to search though all ability IDs associated with this skill
	// and also change the search in PlayerAbilities.cpp
	
	virtual bool SkillInterruptable(bool* OnMotion, bool* OnDamage, bool* OnAction) { return false; };	//Can this skill be interrupted by anything?
	virtual bool InterruptSkillOnMotion(float speed)  { return false; };	//Returns if this skill was interrupted based on current motion.
	virtual bool InterruptSkillOnDamage(float damage);					 	//Returns if this skill was interrupted based on damage taken
	virtual bool InterruptSkillOnAction(int type);							//Returns if this skill was interrupted based on action taken or not.

protected:
	virtual bool IsUsedOnEnemies()  { return true; };				// for target checking
	virtual bool IsUsedOnFriends()  { return !IsUsedOnEnemies(); };	// for target checking
	virtual bool RequiresTarget()	{ return IsUsedOnEnemies(); };	// for target checking
	virtual bool SelfOnly()			{ return false; };				// for target checking
	virtual bool IsGroupSkill()		{ return false; };				// for target checking
	virtual bool IsUsedOnTheDead()	{ return false; };				// for target checking
	virtual bool IsToggleSkill()	{ return false; };				// change how m_Use is treated
	virtual bool Interrupts(int Type) { return Type == OTHER; };	// check for InterruptSkillOnAction

	virtual bool UseSkill(long ChargeTime) { return false; };		// called by Use to do the skill specific work
	CMob	*GetPointerToCommon();				// CMob *me
	Player	*GetPointerToPlayer();				// Player *me
	MOB		*GetPointerToMOB();					// MOB *me
	CMob	*GetPointerToTarget(CMob *c);		// convert target id into CMob*
	Player	*GetTargetAsPlayer();				// convert target to Player*
	MOB		*GetTargetAsMOB();					// convert target to MOB*
	void	ChangeTarget(long newID);			// target a new enemy
	float	GetInterruptThreshHold();			// amount of damage needed to interrupt this skill	
	void	SetTimer(long Duration);			// Setup call back
	void	SetEffectTimer(int EffectID, long Duration);	// callback to remove gfx
	void	SetObjectEffectTimer(int EffectID, long Duration);	// callback to remove gfx
	void	SendError(char * EMsg);				// Send out error messages
	int		GetSkillLevel(Player *p, CMob *c);	// retrieve from player/mob and add m_Stats boost if available
	bool	CanUseEx(bool check_prospect=true, bool check_warp=true, bool check_looting=false, 
				bool check_incap=true);			// extra common state checking
	bool	CanUseWithCurrentTarget(bool default_to_self=false); // friend or foe checking
	short	GetEnemyGroup();					// fill m_AOEEnemyList
	short	GetFriendlyGroup();					// fill m_AOEFriendList
	short	UseOnAllEnemiesInRange(bool of_target=false, proxparam p1=proxparam(), 
				proxparam p2=proxparam(), proxparam p3=proxparam()); // PBAOE enemy iteration
	short	UseOnAllFriendsInRange(bool of_target=false, proxparam p1=proxparam(),
				proxparam p2=proxparam(), proxparam p3=proxparam()); // PBAOE friend iteration
	virtual void ProximityAOE(CMob *target, short seq,
				proxparam p1, proxparam p2, proxparam p3) {};		 // called by above functions for each enemy/friend

	void	DrainReactor(float drain);
	void	RechargeReactor();
	long	UpdateDelay(u32 end_time = 0, u32 time_delay = 20000);

private:
	// targetting info
	bool	m_IAmAPlayer;					// skill user is a player
	CMob	*m_MobPtr;						// pointer to mob using skills
	long	m_MyIndex;						// ID of skill user
	long	m_TargetID;						// ID of skill victim (player or mob)

	// used for constant reactor drains
	float	m_ReactorDrain;

protected:
	CMob	*m_Target;						// pointer to target
	CMob	*m_AOEEnemyList[6];				// array of enemy group ptrs
	CMob	*m_AOEFriendList[6];			// array of friendly group member ptrs

	// skill info
	float	m_SkillLevel;					// Used to hold the current skill level
	long	m_SkillRank;					// Hold the rank of the skill being used
	long	m_AbilityID;					// #define from PlayerSkills.h
	long	m_SkillID;						// not currently used for anything
	char    *m_StatSkill;					// m_Stats string to boost this skill level

	// other
	long	m_EffectID;						// for the visual effect
	float	m_DamageTaken;					// total for interrupts based on damage

	// Uses
	unsigned long	m_NextUse;				// remember time for cooldowns
	bool			m_InUse;				// skill is being used now
	long			m_SkillActivationID;	// match timer to caller
	bool			m_FirstUse;				// some actions need to be performed on the first use only

	Mutex	m_Mutex;
};

#endif