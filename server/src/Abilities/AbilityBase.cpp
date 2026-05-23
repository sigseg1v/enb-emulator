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

#include "AbilityBase.h"
#include "ObjectManager.h"
#include "PlayerClass.h"

AbilityBase::AbilityBase(CMob *me, char *stat_skill)
{
	Init(me);
	m_SkillLevel=0;					
	m_SkillRank=0;					
	m_AbilityID=0;					
	m_SkillID=0;						
	m_IAmAPlayer=false;
	m_MobPtr=NULL;
	m_MyIndex=0;							
	m_TargetID=0;						
	m_EffectID=0;						
	m_DamageTaken=0;					
	m_NextUse=0;				
	m_InUse=false;				
	m_SkillActivationID=0;
	m_StatSkill=stat_skill;
}
	
void AbilityBase::Init(CMob *me)
{
	m_IAmAPlayer = me->ObjectType() == OT_PLAYER;
	if (m_IAmAPlayer)
		m_MyIndex = dynamic_cast<Player *>(me)->GetGameIndex();
	else
		m_MobPtr = me;
}

CMob *AbilityBase::GetPointerToCommon()
{
	if (m_IAmAPlayer)
		return g_PlayerMgr->GetPlayerFromIndex(m_MyIndex);
	else
		return m_MobPtr;
}

Player *AbilityBase::GetPointerToPlayer()
{
	if (m_IAmAPlayer)
		return g_PlayerMgr->GetPlayerFromIndex(m_MyIndex);
	else
		return NULL;
}

MOB *AbilityBase::GetPointerToMOB()
{
	return dynamic_cast<MOB *>(m_MobPtr);
}

CMob *AbilityBase::GetPointerToTarget(CMob *c)
{
	if (c)
	{
		ObjectManager *om = c->GetObjectManager();
		if (om && m_TargetID > 0)
		{
			Object *Target = om->GetObjectFromID(m_TargetID);
			return dynamic_cast<CMob *>(Target);
		}
	}
	return NULL;
}

Player *AbilityBase::GetTargetAsPlayer()
{
	return dynamic_cast<Player *>(m_Target);
}

MOB *AbilityBase::GetTargetAsMOB()
{
	return dynamic_cast<MOB *>(m_Target);
}

void AbilityBase::ChangeTarget(long newID)
{
	m_TargetID = newID;
	m_Target = GetPointerToTarget(GetPointerToCommon());
}

void AbilityBase::SetTimer(long Duration)
{
	if (Duration)
	{
		CMob *c = GetPointerToCommon();
		if (c)
		{
			SectorManager *sm = c->GetSectorManager();
			if (sm)
			{
				m_SkillActivationID = m_EffectID;
				sm->AddTimedCall(c, B_ABILITY_REMOVE, Duration, c, m_AbilityID, m_SkillActivationID, 0);
			}
		}
	}
}

void AbilityBase::SetEffectTimer(int EffectID, long Duration)
{
	CMob *c = GetPointerToCommon();
	if (c)
	{
		SectorManager *sm = c->GetSectorManager();
		if (sm) sm->AddTimedCall(c, B_REMOVE_EFFECT, Duration, NULL, EffectID);
	}
}

void AbilityBase::SetObjectEffectTimer(int EffectID, long Duration)
{
	CMob *c = GetPointerToCommon();
	if (c)
	{
		SectorManager *sm = c->GetSectorManager();
		if (sm) sm->AddTimedCall(c, B_REMOVE_OBJECT_EFFECT, Duration, NULL, EffectID);
	}
}

void AbilityBase::SendError(char * EMsg)
{
	Player *p = GetPointerToPlayer();
	if (p)
	{
		p->SendPriorityMessageString(EMsg,"MessageLine",1000,4); // no error messages for mobs
	}
}

float AbilityBase::GetInterruptThreshHold()
{
	CMob *c = GetPointerToCommon();
	if (c)
	{
		return pow((float)c->TotalLevel(), 4) + 20 + m_SkillLevel*100;
	}
	return 0.0f;
}

int AbilityBase::GetSkillLevel(Player *p, CMob *c)
{
	// move to CMobClass?
	int unbuffed = p ? p->PlayerIndex()->RPGInfo.Skills.Skill[m_SkillID].GetLevel() : c->TotalLevel()/7+1;
	m_SkillLevel = (float)unbuffed;
	if (m_StatSkill)
	{
		if (p)
			m_SkillLevel += p->m_Stats.GetStat(m_StatSkill);
		else if (c)
			m_SkillLevel += c->m_Stats.GetStat(m_StatSkill);
	}
	if (m_SkillLevel > MAX_SKILL_LEVEL)
		m_SkillLevel = MAX_SKILL_LEVEL;
	return unbuffed;
}

bool AbilityBase::CanUse(long TargetID, long AbilityID, long SkillID)
{
	Player *p = GetPointerToPlayer();
	CMob *c = GetPointerToCommon();
	if (!c) return false;
	// Get the skill level
	m_SkillID = SkillID;
	GetSkillLevel(p,c);
	m_SkillRank = DetermineSkillRank(AbilityID);
	m_AbilityID = AbilityID;
	m_TargetID = TargetID;
	m_Target = GetPointerToTarget(c);

	if (m_IAmAPlayer)
	{
		// Too low level?
		if (m_SkillRank > m_SkillLevel)
		{
			SendError("You do not have enough skill points in this skill!");
			return false;
		}

		// Skill does not exist.
		if (m_SkillRank == -1)
		{
			SendError("This ability does not exist?");
			return false;
		}
	}

	// skill ready for use again
	unsigned long now = GetNet7TickCount();
	if (!m_InUse && m_NextUse > now)
	{
		char buffer[100];
		sprintf_s(buffer,sizeof(buffer),"Skill can be used again in %d seconds",(m_NextUse - now)/1000); 
		SendError(buffer);
		return false;
	}

	return true;
}

bool AbilityBase::CanUseEx(bool check_prospect, bool check_warp, bool check_looting, bool check_incap)
{
	Player *p = GetPointerToPlayer();
	if (p)
	{
		// Make sure we are not dead
		if (check_incap && p->GetIsIncapacitated())
		{
			SendError("Cannot use this ability while dead!");
			return false;
		}

		// Check if we are looting
		if (check_looting && p->Looting())
		{
			SendError("Cannot use while looting!");
			return false;
		}

		// If we are warping we cannot use the skill
		if (check_warp && p->WarpDrive())
		{
			SendError("Cannot use while in warp!");
			return false;
		}

		// If we are prospecting we cannot use this skill
		if (check_prospect && p->Prospecting())
		{
			SendError("Cannot use while prospecting!");
			return false;
		}
	}

	return true;
}

bool AbilityBase::CanUseWithCurrentTarget(bool default_to_self)
{
	CMob *c = GetPointerToCommon();

	if (m_Target)
	{
		// check if can be used on others
		if (SelfOnly() && m_Target != c)
		{
			if (default_to_self)
				m_Target = c;
			else
				SendError("You may not use this skill on other players.");
			return default_to_self;
		}

		// Check if target is enemy
		if (IsUsedOnEnemies() && !c->IsEnemyOf(m_Target))
		{
			SendError("Target must be an enemy!");
			return false; // dont default
		}

		// Check if target is friend
		if (IsUsedOnFriends() && !c->IsFriendOf(m_Target))
		{
			if (default_to_self)
				m_Target = c;
			else
				SendError("Target must be a friendly!");
			return default_to_self;
		}

		// check if target is alive
		if (!IsUsedOnTheDead() && m_Target->GetIsIncapacitated())
		{
			if (default_to_self)
				m_Target = c;
			else
				SendError("Cannot use this skill on a dead player!");
			return default_to_self;
		}

		// Check if target is grouped (TODO: Mob version of this)
		if (IsGroupSkill() && c != m_Target && !g_PlayerMgr->CheckGrouped(dynamic_cast<Player *>(c),GetTargetAsPlayer()))
		{
			if (default_to_self)
				m_Target = c;
			else
				SendError("This skill only works on members of your group!");
			return default_to_self;
		}

		// See if we are in range
		if (m_Target->RangeFrom(c) > CalculateRange(m_SkillLevel, m_SkillRank))
		{
			SendError("Out of range!");
			return false; // dont default
		}
	}
	else
	{
		if (RequiresTarget()) // check for no target when needed
		{
			SendError("No target chosen!");
			return false; // dont default
		}
		// use self if allowed
		if (default_to_self)
			m_Target = c;
	}

	return true;
}

bool AbilityBase::Use(long TargetID)
{
	CMob *p = GetPointerToCommon();
	//allow the ability to be toggled off.
	if (m_InUse && !IsToggleSkill())
	{
		InterruptSkillOnAction(OTHER); 
		return true;
	}

	m_FirstUse = true; // set first use toggle

	// hmmm, check for change of target somehow
	if (!p || TargetID != m_TargetID)
	{
		p->SetCurrentSkill();
		return true;
	}

	long ChargeTime = (long)(CalculateChargeUpTime(m_SkillLevel, m_SkillRank)*1000.0f);

	// remove energy only when switched on (toggle skills)
	if (!m_InUse)
	{
		float EnergyCost = CalculateEnergy(m_SkillLevel, m_SkillRank);
		// enough energy?
		if (p->GetEnergyValue() < EnergyCost)
		{
			SendError("Not enough energy!");
			p->SetCurrentSkill();
			return true;
		}
		// Remove the energy
		p->RemoveEnergy(EnergyCost, 0);//ChargeTime);

		//grab a number for the effectID & timestamp
		m_EffectID = GetNet7TickCount();
	}

	m_NextUse = m_EffectID + (unsigned long)(CalculateCoolDownTime(m_SkillLevel, m_SkillRank)*1000.0f);
	m_SkillActivationID = 0;
	m_DamageTaken = 0;
	
	// do the skill specific work now
	if (!UseSkill(ChargeTime))
	{
		p->SetCurrentSkill();
		return true;
	}

	// dont mark toggle skills as in use
	if (!IsToggleSkill())
	{
		// Mark the skill as in progress
		m_InUse = true;
	}

	// delay call Update()
	if (m_InUse)
		SetTimer(ChargeTime);

	// no timer to wait for so finish the skill off
	if (!m_SkillActivationID)
	{
		m_InUse = false;
		p->SetCurrentSkill();
	}

	return true;
}

bool AbilityBase::Update(long activation_ID)
{
	if(activation_ID != m_SkillActivationID)
	{
		return false;
	}
	
	CMob *c = GetPointerToCommon();
	if(!IsToggleSkill() && !m_InUse)
	{
		if (c)
			c->SetCurrentSkill();
		return false;
	}

	return c != NULL && (SelfOnly() || !RequiresTarget() || m_Target != NULL);
}

bool AbilityBase::InterruptSkillOnDamage(float Damage)
{
	CMob *p = GetPointerToCommon();
	ObjectEffect InterruptEffect;

	// somehow p was null for an interrupted player
	if(!p || Damage <= 0.0f)
	{
		return false;
	}
	
	if(!m_InUse) //cannot interrupt non-active skill.
	{
		p->SetCurrentSkill();
		return false;
	}

	m_DamageTaken += Damage;
	if(m_DamageTaken > GetInterruptThreshHold())
	{
		//remove current effect
		p->RemoveEffectRL(m_EffectID);

		//get new effectID
		m_EffectID = GetNet7TickCount();

		InterruptEffect.Bitmask = 3;
		InterruptEffect.GameID = p->GameID();
		InterruptEffect.EffectDescID = 735;
		InterruptEffect.EffectID = m_EffectID;
		InterruptEffect.TimeStamp = m_EffectID;

		p->SendObjectEffectRL(&InterruptEffect);

		p->RemoveEffectRL(m_EffectID);
		p->SetCurrentSkill();
		m_InUse = false;
		m_DamageTaken = 0;
		return true;
	}

	return false;
}

bool AbilityBase::InterruptSkillOnAction(int Type)
{
	CMob *p = GetPointerToCommon();
	if(!m_InUse)
	{
		p->SetCurrentSkill();
		return false;
	}

	if(Interrupts(Type)) // test in helper function
	{
		if (p) p->RemoveEffectRL(m_EffectID);

		//mark skill as not in use
		m_InUse = false;
		if (p) p->SetCurrentSkill();
		m_NextUse = 0;

		return true;
	}

	return false;
}

short AbilityBase::GetEnemyGroup()
{
	int x,count;
	float Range = CalculateAOE(m_SkillLevel, m_SkillRank);

	for(count=x=0;x<6;x++)
	{
		m_AOEEnemyList[x] = NULL;
	}
	if (m_IAmAPlayer)
	{
		// target is mob
		MOB *m = GetTargetAsMOB();
		if (m)
		{
			count = m->GetGroupPointers(m_AOEEnemyList, Range);
		}
	}
	else
	{
		Player *c = GetTargetAsPlayer();
		if (c)
		{
			// get the group
			Group *g = g_PlayerMgr->GetGroupFromID(c->GroupID());
			if (g)
			{
				for(x=0;x<6;x++)
				{
					long PlayerID = g->Member[x].GameID;
					if (PlayerID > 0)
					{
						Player *p = g_PlayerMgr->GetPlayer(PlayerID);
						// check if player is valid to be entered into list
						if (p && c->IsInSameSector(p) && !p->GetIsIncapacitated() && c->RangeFrom(p) < Range)
						{
							m_AOEEnemyList[x] = p;
							count++;
						}
					}
				}
			}
		}
	}
	return count;
}

short AbilityBase::GetFriendlyGroup()
{
	int x,count;
	bool in_formation,iamformedup;
	float Range = CalculateAOE(m_SkillLevel, m_SkillRank);

	CMob *c = GetPointerToCommon();
	for(count=x=0;x<6;x++)
	{
		m_AOEFriendList[x] = NULL;
	}
	if (m_IAmAPlayer)
	{
		// add self if not grouped
		m_AOEFriendList[count++] = c;
		// get the group
		Group *g = g_PlayerMgr->GetGroupFromID(c->GroupID());
		if (g)
		{
			iamformedup = false;
			for(x=0;x<6;x++)
				if (g->Member[x].GameID == c->GameID())
					iamformedup = g->Member[x].Position != -1;
			for(x=0;x<6;x++)
			{
				long PlayerID = g->Member[x].GameID;
				if (PlayerID > 0)
				{
					Player *p = g_PlayerMgr->GetPlayer(PlayerID);
					in_formation = iamformedup && g->Member[x].Position != -1;
					// check if player is valid to be entered into list (fix range problems with formation warp)
					if (p && p != c && c->IsInSameSector(p) && !p->GetIsIncapacitated() && (c->RangeFrom(p) < Range || in_formation))
					{
						m_AOEFriendList[x] = p;
						count++;
					}
				}
			}
		}
	}
	else
	{
		// skill user is mob
		MOB *m = GetPointerToMOB();
		if (m)
		{
			count = m->GetGroupPointers(m_AOEFriendList, Range);
		}
	}
	return count;
}

short AbilityBase::UseOnAllEnemiesInRange(bool of_target, proxparam p1, proxparam p2, proxparam p3)
{
	CMob *c = of_target ? m_Target : GetPointerToCommon();
	float Range = CalculateAOE(m_SkillLevel , m_SkillRank);
	short count = 0;

	if (m_IAmAPlayer)
	{
		if (c->GetObjectManager())
		{
			// Get Sectors moblist
			ObjectList *mob_list = c->GetObjectManager()->GetMOBList();
			if (mob_list)
			{
				// Iterate through current sector moblist
				for (ObjectList::iterator it = mob_list->begin(); it < mob_list->end(); ++it) 
				{
					Object *mob = (*it);
					// If mob is in range and active
					if (mob && mob->Active() && mob->ObjectType() == OT_MOB && mob->RangeFrom(c) < Range)
					{
						ProximityAOE(dynamic_cast<CMob *>(mob), ++count, p1, p2, p3);
					}
				}
			}
		}
	}
	else
	{
		if (c->GetSectorManager())
		{
			u32 *player_list = c->GetSectorManager()->GetSectorPlayerList();
			if (player_list)
			{
				Player *p = NULL;
				while (g_PlayerMgr->GetNextPlayerOnList(p, player_list))
				{
					if (p && p->Active() && p->RangeFrom(c) < Range)
					{
						ProximityAOE(p, ++count, p1, p2, p3);
					}
				}
			}
		}
	}

	return count;
}

short AbilityBase::UseOnAllFriendsInRange(bool of_target, proxparam p1, proxparam p2, proxparam p3)
{
	CMob *c = of_target ? m_Target : GetPointerToCommon();
	float Range = CalculateAOE(m_SkillLevel , m_SkillRank);
	short count = 0;

	if (m_IAmAPlayer)
	{
		if (c->GetSectorManager())
		{
			u32 *player_list = c->GetSectorManager()->GetSectorPlayerList();
			if (player_list)
			{
				Player *p = NULL;
				while (g_PlayerMgr->GetNextPlayerOnList(p, player_list))
				{
					if (p && p->Active() && p->RangeFrom(c) < Range)
					{
						ProximityAOE(p, ++count, p1, p2, p3);
					}
				}
			}
		}
	}
	else
	{
		if (c->GetObjectManager())
		{
			// Get Sectors moblist
			ObjectList *mob_list = c->GetObjectManager()->GetMOBList();
			if (mob_list)
			{
				// Iterate through current sector moblist
				for (ObjectList::iterator it = mob_list->begin(); it < mob_list->end(); ++it) 
				{
					Object *mob = (*it);
					// If mob is in range and active
					if (mob && mob->Active() && mob->ObjectType() == OT_MOB && mob->RangeFrom(c) < Range)
					{
						ProximityAOE(dynamic_cast<CMob *>(mob), ++count, p1, p2, p3);
					}
				}
			}
		}
	}

	return count;
}

// call this to start a constant drain on the reactor
void AbilityBase::DrainReactor(float drain)
{
	CMob *p = GetPointerToCommon();
	float energy = p->GetEnergyValue();
	long time_to_drain = (long)((energy/drain)*1000.0f);

	m_ReactorDrain = p->DrainReactor(time_to_drain, energy);
}

// called when skill finishes to recharge the reactor
void AbilityBase::RechargeReactor()
{
	CMob *p = GetPointerToCommon();
	p->RechargeReactor(m_ReactorDrain);
}

// used for sustained skills, this will efficiently call back the 'Update' function
long AbilityBase::UpdateDelay(u32 end_time, u32 time_delay)
{
	CMob *p = GetPointerToCommon();
	float energy = p->GetEnergy(); //drain uses the ratio not the absolute value
	u32 current_tick = GetNet7TickCount();
	
	if (end_time > 0 && end_time <= (current_tick + time_delay)) 
	{
		time_delay = end_time - current_tick;
	}
	else if (m_ReactorDrain != 0.0f && energy < m_ReactorDrain*(float)(time_delay/1000))
	{
		time_delay = (long)( (energy/m_ReactorDrain)*1000.0f );
	}

	SetTimer(time_delay);

	return time_delay;
}