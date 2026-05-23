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
** Copyright of our assets/code/software began in 2005-2009 �, Net-7 Entertainment.
**
*/

#include "PlayerClass.h"
#include "PlayerSkills.h"
#include "ServerManager.h"
#include "ObjectManager.h"
#include "PacketMethods.h"
#include "Opcodes.h"
#include "StaticData.h"

// -1's indicate that this leveling scheme does not contain this level
static int LevelList[5][9] =
{
    { 0, 0, 12,  18, 24, 30, 36,  42,  50  },	// SKILL_PRIMWEP
    { 0, 7, 14, 21, 28, 35, 42,  49,  -1  },	// SKILL_SECWEP
    { 0, 0, 20, 40, 60, 80, 100, 120, 140 },	// SKILL_PRIMTECH
    { 0, 0, 30, 50, 70, 90, 110, 130, -1  },	// SKILL_SECTECH
    { 0, 0, 5,  15, 25, 35, 45,  -1,  -1  }	    // SKILL_OTHER
};

static char* SkillNames[64] = {"SKILL_AFTERBURN",
"SKILL_BEAM_WEAPON",
"SKILL_BEFRIEND",
"SKILL_BIOREPRESSION",
"SKILL_BUILD_COMPONENTS",
"SKILL_BUILD_DEVICES",
"SKILL_BUILD_ENGINES",
"SKILL_BUILD_ITEMS",
"SKILL_BUILD_REACTORS",
"SKILL_BUILD_SHIELDS",
"SKILL_BUILD_WEAPONS",
"SKILL_CALL_FORWARD",
"SKILL_CLOAK",
"SKILL_COMBAT_TRANCE",
"SKILL_COMPULSORY_CONTEMPLATION",
"SKILL_CREATE_WORMHOLE",
"SKILL_CRITICAL_TARGETING",
"SKILL_DAMAGE_CONTROL",
"SKILL_DEVICE_TECH",
"SKILL_ENERGY_LEECH",
"SKILL_ENGINE_TECH",
"SKILL_ENGINEERING",
"SKILL_ENRAGE",
"SKILL_ENVIRONMENT_SHIELD",
"SKILL_FOLD_SPACE",
"SKILL_GRAVITY_LINK",
"SKILL_HACKING",
"SKILL_HULL_PATCH",
"SKILL_ITEM_TECH",
"SKILL_JENQUAI_CULTURE",
"SKILL_JENQUAI_LORE",
"SKILL_JUMPSTART",
"SKILL_MAELSTROM_RESONANCE",
"SKILL_MENACE",
"SKILL_MISSILE_WEAPON",
"SKILL_NAVIGATE",
"SKILL_NEGOTIATE",
"SKILL_POWER_DOWN",
"SKILL_PROGEN_CULTURE",
"SKILL_PROGEN_LORE",
"SKILL_PROJECTILE_WEAPON",
"SKILL_PROSPECT",
"SKILL_PSIONIC_SHIELD",
"SKILL_QUANTUM_FLUX",
"SKILL_RALLY",
"SKILL_REACTOR_TECH",
"SKILL_RECHARGE_SHIELDS",
"SKILL_REPAIR_EQUIPMENT",
"SKILL_REPULSOR_FIELD",
"SKILL_SCAN",
"SKILL_SELF_DESTRUCT",
"SKILL_SHIELD_CHARGING",
"SKILL_SHIELD_INVERSION",
"SKILL_SHIELD_LEECH",
"SKILL_SHIELD_SAP",
"SKILL_SHIELD_TECH",
"SKILL_SUMMON",
"SKILL_TERRAN_CULTURE",
"SKILL_REACTOR_OPTIMISATION"};

void Player::HandleSkillAction(unsigned char *data)
{
    SkillAction *Action = (SkillAction *) data;
    int SkillPoints = m_PlayerIndex.RPGInfo.GetSkillPoints();
    int SkillPointsNeeded = m_PlayerIndex.RPGInfo.Skills.Skill[Action->SkillID].GetAvailability()[3];
    int SkillLevel = m_PlayerIndex.RPGInfo.Skills.Skill[Action->SkillID].GetLevel();
    int SkillMaxLevel = m_PlayerIndex.RPGInfo.Skills.Skill[Action->SkillID].GetMaxSkillLevel();
    
    // If the user hits the button twice really fast, the following checks are needed
    if (SkillLevel == SkillMaxLevel)
    {
        LogDebug("%s trying to update skill %d with it being already maxed!\n",Name(),Action->SkillID);
        return;
    }
    
    if (SkillPoints < SkillPointsNeeded)
    {
        LogDebug("%s has insufficient skill points to update skill %d [%d/%d]!\n",Name(),Action->SkillID,SkillPoints,SkillPointsNeeded);
        return;
    }

	if (SkillLevelRequirement(Action->SkillID) > 0)
	{
		// Oops, We can't upgrade!
		return;
	}
    
    m_Mutex.Lock();
    //Assume that the skill was allowed to be updated at this point (the availabilities were set properly)
    m_PlayerIndex.RPGInfo.SetSkillPoints(SkillPoints - SkillPointsNeeded);
    m_PlayerIndex.RPGInfo.Skills.Skill[Action->SkillID].SetLevel(SkillLevel + 1);


    //Update availablities for this skill
    UpgradeSkill(Action->SkillID);
    
    //Update the rest of the skills
    UpdateSkills();
	m_Mutex.Unlock();

    SkillUpdateStats(Action->SkillID);		// This will update any stats that need to be updated
	SaveNewSkillLevel(Action->SkillID, SkillLevel + 1);
	SaveSkillPoints();

    SendAuxPlayer();
    SendAuxShip();
}

void Player::SkillUpdateStats(int SkillID)
{
	float stat_val;

    switch (SkillID)
    {
        //TODO: Update stats on Aux!
    case SKILL_BEAM_WEAPON:
        // Update stat
		stat_val = (float)PlayerIndex()->RPGInfo.Skills.Skill[SKILL_BEAM_WEAPON].GetLevel() + m_Stats.GetStat(STAT_SKILL_BEAM_WEAPON);
		if (m_BaseID[ID_BEAM_DAMAGE] == 0)
		{
			m_BaseID[ID_BEAM_DAMAGE] = m_Stats.SetStat( STAT_BUFF_MULT, STAT_BEAM_DAMAGE, stat_val > 0 ? stat_val * 0.25f - 0.25f : 0, "SKILL_BEAM_WEAPON");
		}
		else
		{
			m_Stats.ChangeStat( m_BaseID[ID_BEAM_DAMAGE], stat_val > 0 ? stat_val * 0.25f - 0.25f : 0);
		}
		if (m_BaseID[ID_BEAM_ACCURACY] == 0)
		{
			m_BaseID[ID_BEAM_ACCURACY] = m_Stats.SetStat( STAT_BUFF_VALUE, STAT_BEAM_ACCURACY, stat_val > 0 ? stat_val * 30.0f : 0, "SKILL_BEAM_WEAPON");
		}
		else
		{
			m_Stats.ChangeStat( m_BaseID[ID_BEAM_ACCURACY], stat_val > 0 ? stat_val * 30.0f : 0);
		}
        break;
    case SKILL_PROJECTILE_WEAPON:
        // Update stat
		stat_val = (float)PlayerIndex()->RPGInfo.Skills.Skill[SKILL_PROJECTILE_WEAPON].GetLevel() + m_Stats.GetStat(STAT_SKILL_PROJECTILE_WEAPON);
		if (m_BaseID[ID_PROJ_DAMAGE] == 0)
		{
			m_BaseID[ID_PROJ_DAMAGE] = m_Stats.SetStat( STAT_BUFF_MULT, STAT_PROJECTILES_DAMAGE, stat_val > 0 ? stat_val * 0.25f - 0.25f : 0, "SKILL_PROJECTILE_WEAPON");
		}
		else
		{
			m_Stats.ChangeStat( m_BaseID[ID_PROJ_DAMAGE], stat_val > 0 ? stat_val * 0.25f - 0.25f : 0);
		}
		if (m_BaseID[ID_PROJ_ACCURACY] == 0)
		{
			m_BaseID[ID_PROJ_ACCURACY] = m_Stats.SetStat( STAT_BUFF_VALUE, STAT_PROJECTILE_ACCURACY, stat_val > 0 ? stat_val * 30.0f : 0, "SKILL_PROJECTILE_WEAPON");
		}
		else
		{
			m_Stats.ChangeStat( m_BaseID[ID_PROJ_ACCURACY], stat_val > 0 ? stat_val * 30.0f : 0);
		}
        break;
    case SKILL_MISSILE_WEAPON:
        // Update stat
		stat_val = (float)PlayerIndex()->RPGInfo.Skills.Skill[SKILL_MISSILE_WEAPON].GetLevel() + m_Stats.GetStat(STAT_SKILL_MISSILE_WEAPON);
		if (m_BaseID[ID_MISSILE_DAMAGE] == 0)
		{
			m_BaseID[ID_MISSILE_DAMAGE] = m_Stats.SetStat( STAT_BUFF_MULT, STAT_MISSILE_DAMAGE, stat_val > 0 ? stat_val * 0.25f - 0.25f : 0, "SKILL_MISSILE_WEAPON");
		}
		else
		{
			m_Stats.ChangeStat( m_BaseID[ID_MISSILE_DAMAGE], stat_val > 0 ? stat_val * 0.25f - 0.25f : 0);
		}
		if (m_BaseID[ID_MISSILE_ACCURACY] == 0)
		{
			m_BaseID[ID_MISSILE_ACCURACY] = m_Stats.SetStat( STAT_BUFF_VALUE, STAT_MISSILE_ACCURACY, stat_val > 0 ? stat_val * 30.0f : 0, "SKILL_MISSILE_WEAPON");
		}
		else
		{
			m_Stats.ChangeStat( m_BaseID[ID_MISSILE_ACCURACY], stat_val > 0 ? stat_val * 30.0f : 0);
		}
        break;
    case SKILL_SCAN:
		stat_val = (float)PlayerIndex()->RPGInfo.Skills.Skill[SKILL_SCAN].GetLevel() + m_Stats.GetStat(STAT_SKILL_SCAN);
		if (m_BaseID[ID_SCAN] == 0)
		{
			m_BaseID[ID_SCAN] = m_Stats.SetStat( STAT_BUFF_VALUE, STAT_SCAN_RANGE, stat_val > 0 ? 1000.0f + stat_val * 250.0f : 0, "SKILL_SCAN");
		}
		else
		{
			m_Stats.ChangeStat(m_BaseID[ID_SCAN], stat_val > 0 ? 1000.0f + stat_val * 250.0f : 0);
		}
        // Update Scan range reading
        ShipIndex()->CurrentStats.SetScanRange((s32)m_Stats.GetStat(STAT_SCAN_RANGE));
        break;
    case SKILL_NAVIGATE: //TO-DO: Reduce warp startup/recovery time, increase warp/impulse speeds.
		stat_val = (float)PlayerIndex()->RPGInfo.Skills.Skill[SKILL_NAVIGATE].GetLevel() + m_Stats.GetStat(STAT_SKILL_NAVIGATE);
		//Warp Energy Reduction calculation
		if (m_BaseID[ID_WARP_ENERGY] == 0)
		{
			m_BaseID[ID_WARP_ENERGY] = m_Stats.SetStat( STAT_DEBUFF_MULT, STAT_WARP_ENERGY, stat_val > 0 ? 0.10f + stat_val * 0.1f : 0, "SKILL_NAVIGATE");
		}
		else
		{
			m_Stats.ChangeStat(m_BaseID[ID_WARP_ENERGY], stat_val > 0 ? 0.10f + stat_val * 0.1f : 0);
		}

		//Impulse speed increase
		if (m_BaseID[ID_NAVIGATE_IMPULSE] == 0)
		{
			m_BaseID[ID_NAVIGATE_IMPULSE] = m_Stats.SetStat(STAT_BUFF_VALUE, STAT_ENGINE_TOP_SPEED, (stat_val >= 5)? (-80 + stat_val * 30) : 0,	"SKILL_NAVIGATE");
		}
		else
		{
			m_Stats.ChangeStat(m_BaseID[ID_NAVIGATE_IMPULSE], (stat_val >= 5)? (-80 + stat_val * 30) : 0);
		}
		//SendVaMessage("Impulse speed buff now = %f.",m_Stats.GetStat(STAT_IMPULSE));
		ShipIndex()->CurrentStats.SetSpeed((s32)CalculateSpeed());
		
		//Warp speed bonus
		if (m_BaseID[ID_WARP_SPEED] == 0)
		{
			m_BaseID[ID_WARP_SPEED] = m_Stats.SetStat(STAT_BUFF_VALUE, STAT_WARP, (stat_val >= 6)?-1400 + stat_val * 300:0,	"SKILL_NAVIGATE");
		}
		else
		{
			m_Stats.ChangeStat(m_BaseID[ID_WARP_SPEED], (stat_val >= 6)?-1400 + stat_val * 300:0);
		}
		ShipIndex()->CurrentStats.SetWarpSpeed((s32)m_Stats.GetStat(STAT_WARP));

		//Warp charge/recovery time modification
		//TO-DO: Implement me
		//50% chance to warp while in gravity well
		if (m_BaseID[ID_WARP_RECOVERY_NAV] == 0)
		{
			m_BaseID[ID_WARP_RECOVERY_NAV] = m_Stats.SetStat( STAT_DEBUFF_MULT, STAT_WARP_RECOVERY, stat_val > 2 ? (stat_val-1) * 0.05f : 0, "SKILL_NAVIGATE");
		}
		else
		{
			m_Stats.ChangeStat(m_BaseID[ID_WARP_RECOVERY_NAV], stat_val > 0 ? 0.10f + stat_val * 0.05f  : 0);
		}

        break;
	case SKILL_CRITICAL_TARGETING:
		stat_val = (float)PlayerIndex()->RPGInfo.Skills.Skill[SKILL_CRITICAL_TARGETING].GetLevel() + m_Stats.GetStat(STAT_SKILL_CRITICAL_TARGETING);
		if(m_BaseID[ID_CRITICAL_TARGETING] == 0)
		{
			m_BaseID[ID_CRITICAL_TARGETING] = m_Stats.SetStat(STAT_BUFF_MULT, STAT_CRITICAL_RATE, stat_val > 0 ? stat_val * 0.1f : 0, "SKILL_CRITICAL_TARGETING");
		}
		else
		{
			m_Stats.ChangeStat(m_BaseID[ID_CRITICAL_TARGETING], stat_val > 0 ? stat_val * 0.1f : 0);
		}
		break;
	case SKILL_DAMAGE_CONTROL:
		stat_val = (float)PlayerIndex()->RPGInfo.Skills.Skill[SKILL_DAMAGE_CONTROL].GetLevel() + m_Stats.GetStat(STAT_SKILL_DAMAGE_CONTROL);
		if(m_BaseID[ID_DAMAGE_CONTROL] == 0)
		{
			m_BaseID[ID_DAMAGE_CONTROL] = m_Stats.SetStat( STAT_BUFF_VALUE, STAT_HULL_DAMAGE_CONTROL, stat_val > 0 ? 0.15f + stat_val * 0.05f : 0, "SKILL_DAMAGE_CONTROL");
		}
		else
		{
			m_Stats.ChangeStat(m_BaseID[ID_DAMAGE_CONTROL], stat_val > 0 ? 0.15f + stat_val * 0.05f : 0);
		}
		break;
	case SKILL_NEGOTIATE:

		//recalculate prices
		if(m_TradeWindow)
		{
			NPCTradeItems();
		}
		
		/*
		stat_val = (float)PlayerIndex()->RPGInfo.Skills.Skill[SKILL_NEGOTIATE].GetLevel() + m_Stats.GetStat(STAT_SKILL_NEGOTIATE);
		if(m_BaseID[ID_NEGOTIATE] == 0)
		{
			m_BaseID[ID_NEGOTIATE] = m_Stats.SetStat( STAT_BASE_VALUE, STAT_SKILL_NEGOTIATE, stat_val > 0 ? 0.04f + (stat_val-1) * 0.03f : 0);
		}
		else
		{
			m_Stats.ChangeStat(m_BaseID[ID_NEGOTIATE], stat_val > 0 ? 0.04f + (stat_val-1) * 0.03f : 0);
		}
		*/
		break;	
	case SKILL_COMBAT_TRANCE:
		//ResetCombatTrance(); // too early in the login process
		break;
    }
}

void Player::SkillsList()
{
	// This will run everytime we login to make sure skills are ready

	SkillData *Skills = g_ServerMgr->m_SkillList;

	long class_index = ClassIndex(), race = Race();
    
    u32 Availability[4] = {4, 0, 0, 0};
    for (int i=0;i<64;i++)
    {
		// Make sure we are alowed to have the skill and if so make sure that we obtained it if its quested
		if (Skills[i].ClassType[class_index].MaxLevel > 0 && 
			((Skills[i].ClassType[class_index].Quested == 1 && m_PlayerIndex.RPGInfo.Skills.Skill[i].GetLevel() > 0) ||
			Skills[i].ClassType[class_index].Quested == 0))
		{
			// Check if we now have enough skillpoints
			if (m_PlayerIndex.RPGInfo.Skills.Skill[i].GetAvailability()[3] <= 
				m_PlayerIndex.RPGInfo.GetSkillPoints())
			{
				Availability[1] = SKILL_ERROR_NONE;
				Availability[3] = m_PlayerIndex.RPGInfo.Skills.Skill[i].GetLevel();
				m_PlayerIndex.RPGInfo.Skills.Skill[i].SetAvailability(Availability);
			}
			else
			{
				Availability[1] = SKILL_ERROR_SKILLPOINTS;
				Availability[2] = 0;
				Availability[2] = m_PlayerIndex.RPGInfo.Skills.Skill[i].GetLevel();
				m_PlayerIndex.RPGInfo.Skills.Skill[i].SetAvailability(Availability);
			}

			// Check is we are now high enough lvl
			if (SkillLevelRequirement(i) == 0)
			{
				Availability[1] = SKILL_ERROR_NONE;
				Availability[3] = m_PlayerIndex.RPGInfo.Skills.Skill[i].GetLevel();
				m_PlayerIndex.RPGInfo.Skills.Skill[i].SetAvailability(Availability);
			}
			else
			{
				// Show an error with the level you need to obtain
				Availability[1] = g_ServerMgr->m_SkillList[i].RequirementScheme;
				Availability[2] = LevelList[Skills[i].ClassType[ClassIndex()].LevelScheme][m_PlayerIndex.RPGInfo.Skills.Skill[i].GetLevel()];
				Availability[3] = m_PlayerIndex.RPGInfo.Skills.Skill[i].GetLevel();
				m_PlayerIndex.RPGInfo.Skills.Skill[i].SetAvailability(Availability);
			}
		}
    }
}

void Player::LevelUpForSkills()
{
    // The player has leveled up, check all of the level requirements for skills that have
    // the SKILL_ERROR_xxxLVL error and the SKILL_ERROR_SKILLPOINTS error
    
    // This function should be run everytime that the player gains skillpoints aswell
    
    u32 Availability[4] = {4, 0, 0, 0};
    for (int i=0;i<64;i++)
    {
        // The error is one of the LVL errors
        if (m_PlayerIndex.RPGInfo.Skills.Skill[i].GetAvailability()[1] & 0x39)
        {
            // Check is we are now high enough lvl to upgrade the skill
            if (SkillLevelRequirement(i) == 0)
            {
                Availability[1] = SKILL_ERROR_NONE;
				Availability[3] = m_PlayerIndex.RPGInfo.Skills.Skill[i].GetLevel();
                m_PlayerIndex.RPGInfo.Skills.Skill[i].SetAvailability(Availability);
            }
        }
        // The error was insufficient skillpoints
        else if (m_PlayerIndex.RPGInfo.Skills.Skill[i].GetAvailability()[1] == SKILL_ERROR_SKILLPOINTS)
        {
            // Check if we now have enough skillpoints
            if (m_PlayerIndex.RPGInfo.Skills.Skill[i].GetAvailability()[3] <= 
                m_PlayerIndex.RPGInfo.GetSkillPoints())
            {
                Availability[1] = SKILL_ERROR_NONE;
                Availability[3] = m_PlayerIndex.RPGInfo.Skills.Skill[i].GetLevel();
                m_PlayerIndex.RPGInfo.Skills.Skill[i].SetAvailability(Availability);
            }
        }
    }
}

void Player::UpdateSkills()
{
    // We only need to check the skills that have no error to see if we have sufficient skillpoints
    // This is because everything else is handled when the skill is upgraded or the player levels
    u32 SkillPoints = m_PlayerIndex.RPGInfo.GetSkillPoints();
    u32 Availability[4] = {4, SKILL_ERROR_SKILLPOINTS, 0, 0};
    
    for (u32 i=0;i<64;i++)
    {
        // Avaiability[0] must be 4 for the skill's avaiabilities to change
        if (m_PlayerIndex.RPGInfo.Skills.Skill[i].GetAvailability()[0] == 4 &&
            m_PlayerIndex.RPGInfo.Skills.Skill[i].GetAvailability()[1] == SKILL_ERROR_NONE &&
            m_PlayerIndex.RPGInfo.Skills.Skill[i].GetAvailability()[3] > SkillPoints)
        {
            Availability[3] = m_PlayerIndex.RPGInfo.Skills.Skill[i].GetLevel();
            m_PlayerIndex.RPGInfo.Skills.Skill[i].SetAvailability(Availability);
        }
    }
}

void Player::UpgradeSkill(int SkillID)
{
    // We have just upgraded this skill, therefore the following can happen in this order:
    // 1. Maximum skill level reached
    // 2. Required level too low to upgrade further
    // 3. Insufficient Skillpoints
    // 4. No error, can continue upgrading
    
    u32 Availablity[4];
    Availablity[0] = 4;
    
    if (m_PlayerIndex.RPGInfo.Skills.Skill[SkillID].GetLevel() == 
        g_ServerMgr->m_SkillList[SkillID].ClassType[ClassIndex()].MaxLevel)
    {
        Availablity[1] = SKILL_ERROR_MAXLVL;
        Availablity[2] = 0;
        Availablity[3] = 0;
    }
    // This IS an assignment
    else if (Availablity[2] = SkillLevelRequirement(SkillID))
    {
        // The level scheme corresponds to the proper error aswell
        
        Availablity[1] = g_ServerMgr->m_SkillList[SkillID].RequirementScheme;
        Availablity[3] = m_PlayerIndex.RPGInfo.Skills.Skill[SkillID].GetLevel();
    }
    else if (m_PlayerIndex.RPGInfo.Skills.Skill[SkillID].GetLevel() > m_PlayerIndex.RPGInfo.GetSkillPoints())
    {
        Availablity[1] = SKILL_ERROR_SKILLPOINTS;
        Availablity[2] = 0;
        Availablity[3] = m_PlayerIndex.RPGInfo.Skills.Skill[SkillID].GetLevel();
    }
    else
    {
        Availablity[1] = SKILL_ERROR_NONE;
        Availablity[2] = 0;
        Availablity[3] = m_PlayerIndex.RPGInfo.Skills.Skill[SkillID].GetLevel();
    }
    
    m_PlayerIndex.RPGInfo.Skills.Skill[SkillID].SetAvailability(Availablity);
	SaveNewSkillLevel(SkillID, Availablity[3]);
}

long Player::SkillLevelRequirement(long SkillID)
{
    long Level = 0;
    long Requirement = LevelList[g_ServerMgr->m_SkillList[SkillID].ClassType[ClassIndex()].LevelScheme]
        [m_PlayerIndex.RPGInfo.Skills.Skill[SkillID].GetLevel()];
    
    // Get our level under the skill's requirement scheme
    switch (g_ServerMgr->m_SkillList[SkillID].RequirementScheme)
    {
    case SKILL_TOTAL:
        Level = TotalLevel();
        break;
    case SKILL_COMBAT:
        Level = m_PlayerIndex.RPGInfo.GetCombatLevel();
        break;
    case SKILL_TRADE:
        Level = m_PlayerIndex.RPGInfo.GetTradeLevel();
        break;
    case SKILL_EXPLORE:
        Level = m_PlayerIndex.RPGInfo.GetExploreLevel();
        break;
    }
    
    if (Requirement == -1)
    {
        /*LogMessage("%s's skill# %d not allowed to be level %d!\n",Name(),SkillID,
            m_PlayerIndex.RPGInfo.Skills.Skill[SkillID].GetLevel());*/
        return 200;
    }
    
    if (Level < Requirement)
    {
        return Requirement;
    }
    else
    {
        return 0;
    }
}

/*Respec section*/
/*returns number of skill points reclaimed*/
int Player::RespecSkill(int SkillID)
{
	const int ppLevel[10] = {0,0,1,3,6,10,15,21,28,36};
	int SkillPointsReclaimed = 0;
	int SkillPoints = m_PlayerIndex.RPGInfo.GetSkillPoints();
	int oldSkillLevel = m_PlayerIndex.RPGInfo.Skills.Skill[SkillID].GetLevel();
	
    if(oldSkillLevel > 1 && oldSkillLevel <= 9)
	{
		SkillPointsReclaimed = ppLevel[oldSkillLevel];  //compute points reclaimed
		m_PlayerIndex.RPGInfo.SetSkillPoints(SkillPoints + SkillPointsReclaimed); //increment player's avaliable points
		m_PlayerIndex.RPGInfo.Skills.Skill[SkillID].SetLevel(1); //set the skill to level 1
		SaveNewSkillLevel(SkillID, 1);
		SkillUpdateStats(SkillID);		// This will update any stats that need to be updated
		SendVaMessage("Skill: %s Lv%d -> Lv%d (+%d SP)",SkillNames[SkillID],oldSkillLevel,1,SkillPointsReclaimed);
	}

	return SkillPointsReclaimed;
}

//respecs and commits one skill only
int Player::RespecOneSkill(int SkillID)
{
	int refund = 0;
	//m_Mutex.Lock();	
	refund = RespecSkill(SkillID);
	SaveSkillPoints();
	SkillsList();
	//m_Mutex.Unlock();
	SendAuxPlayer();
    SendAuxShip();
	return refund;
}

int Player::RespecSkills(bool callForward)  //reclaims skill points from skills.  If callForward, does not modify engine shield or reactor.
{
	//refunds the player all skill points not put into shield, engine and reactor
	int totalRefund = 0;
	//m_Mutex.Lock();
	//m_Stats.ResetStats();
	for(int index=0; index < 64; index++)
	{
		if(!callForward || (index != SKILL_SHIELD_TECH && index != SKILL_ENGINE_TECH && index != SKILL_REACTOR_TECH))
		{
			totalRefund += RespecSkill(index);
			SendAuxPlayer();
			SendAuxShip();
		}
	}
	SaveSkillPoints();
	SkillsList();
    //m_Mutex.Unlock();
	SendAuxPlayer();
    SendAuxShip();
	return totalRefund;
}

int Player::CountSpentPoints()
{
	const int ppLevel[10] = {0,0,1,3,6,10,15,21,28,36};
	int points = 0;
	for(int index=0; index<64;index++)
	{
		points+= ppLevel[m_PlayerIndex.RPGInfo.Skills.Skill[index].GetLevel()];
	}
	return points;
}


/*End Respec Section*/

//PROSPECTING & LOOTING
void Player::GetOreRequirements(ItemBase *item, long stack, float *energy_per_ore, float *time_per_ore)
{
	// formula from MasterDataTables.xls
	//float recharge = m_Stats.GetStat(STAT_ENERGY_RECHARGE);
	float skill = (float)PlayerIndex()->RPGInfo.Skills.Skill[SKILL_PROSPECT].GetLevel() + m_Stats.GetStat(STAT_SKILL_PROSPECT);
	float ore = item ? (float)item->TechLevel() : 999.0f;
	if (skill > 10.0f)
		skill = 10.0f;
	if (skill > ore)
		*time_per_ore = 1.0f / (1.0f + skill - ore);
	else
		*time_per_ore = (ore - skill) * 2.0f + 1.0f;
	float energy_per_sec = (ore * 2.0f) * (ore * 2.0f);// - recharge; // design doc says *, spreadsheet says +
	*energy_per_ore = energy_per_sec * (*time_per_ore);
    
	// special mission override
    if (item->ItemTemplateID() == 3009 && GrailAffinity() && CheckMissionValidity(3009))
    {
		*energy_per_ore = 120.0f;
    }
}

float Player::GetEnergyPerOre(ItemBase *item)
{
	float energy_per_ore,time_per_ore;
	GetOreRequirements(item,1,&energy_per_ore,&time_per_ore);
	return energy_per_ore;
}

bool Player::TractorInUse()
{
    return m_TractorBeam;
}

u32 Player::TractorCompletion()
{
    return m_ProspectTractorNode.event_time;
}

//initial method called when mining commences - slot refers to the asteroid's resource menu popup
void Player::MineResource(long slot)
{
    Object *obj;
    long effect_UID;
    u32 prospect_tick = GetNet7TickCount();
    long stack_val;
    float energy_per_ore,time_per_ore;
    bool incomplete = false;
    float reactor_energy = GetEnergyValue();
    u32 time_to_drain;
	u32 drain_effect_time; //we use this so when you're speed mining (ie prospecting the next ore while the current is being tractored) 
						   //  the prospect effect stays until it's ready to be tractored

	if(ShipIndex()->GetIsCloaked() || ShipIndex()->GetIsCountermeasureActive())
	{
		AbortProspecting(true, false);
		return;
	}
    
    //Check we are OK for mining
    if (CheckMiningConditions(slot, reactor_energy))
    {
        SetPlayerUpdate(2); //force a client update since we're guaranteed that here the player is static //TODO: make this a special Net7Proxy code, so we can do a direct update
        effect_UID = GetSectorManager()->GetSectorNextObjID();
        obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());
        stack_val = obj->GetStack(slot);
        
        //how much energy do we have? Work out now how many ores can be pulled
        GetOreRequirements(obj->GetItem(slot),stack_val,&energy_per_ore,&time_per_ore);
        
        if (energy_per_ore * (float)stack_val > reactor_energy)
        {
            stack_val = (long)(reactor_energy/energy_per_ore);
            incomplete = true;
        }
        
        m_ProspectBeam = true;
        SetProspecting(true);
        
        ////////////////////////////////////////////////////////////////////
        //Phase 1 - start prospecting, switch on the prospect beam
        
        time_to_drain = u32(time_per_ore * (float)stack_val * 1000); // in ticks
		if (time_to_drain < 1000)
			time_to_drain = 1000;

		drain_effect_time = time_to_drain;
        
        //SendVaMessage("energy per ore: %d, total energy = %d range = %.2f prospect time = %d", energy_required_per_ore, energy_required_per_ore * stack_val, resource_range, time_to_drain);
		//now just delay the tractor until existing tractor is done
        if (m_ProspectTractorNode.player_id != 0) //are we currently sucking on an ore? If so time this prospect to finish just after it comes on board.
        {
            SendVaMessage("Tractor Beam in use");
            if ((prospect_tick + drain_effect_time) < TractorCompletion())
            {
                drain_effect_time = (TractorCompletion() - prospect_tick) + 100;
            }
        }

        //Just send one UDP packet to initiate prospecting.
        //We need a player ID, a target, a start time and an effect ID
        //Note for implementing cloaking - the 2012 packet may cancel the cloak in Net7Proxy (SendProspectAUX)

        unsigned char udp_prospect_packet[32];
        int index = 0;
        
        AddData(udp_prospect_packet, GameID(), index);
        AddData(udp_prospect_packet, obj->GameID(), index);
        AddData(udp_prospect_packet, effect_UID, index);
        AddData(udp_prospect_packet, prospect_tick, index);
        AddData(udp_prospect_packet, drain_effect_time, index);
        
        SendToRangeList(ENB_OPCODE_2012_START_PROSPECT, udp_prospect_packet, index);
        
        m_ProspectDrain = DrainReactor(time_to_drain, energy_per_ore * (float)stack_val);
      
        //Call PullOreFromResource when prospect beam has done its work after 'time_to_drain'
        PopulateTimedCall(&m_ProspectBeamNode, B_MINE_RESOURCE, time_to_drain, obj, stack_val, slot, effect_UID, incomplete ? 1 : 0);
    }
}

void Player::PullOreFromResource(Object *obj, long stack_val, long slot, long effect_UID, long incomplete)
{
    ItemBase * resource = obj->GetItem(slot);
    
    if (!resource)
    {
        LogMessage("*** mining an empty roid - how did this happen?\n");
        return;
    }
    
    //see if an ore is currently being pulled, if so, force the issue
    if (m_ProspectTractorNode.player_id != 0)
    {
        //force previous ore on board
        TakeOreOnboard(m_ProspectTractorNode.obj, m_ProspectTractorNode.i3, true);
        m_ProspectTractorNode.player_id = 0;
    }
    
    UseTractorBeam(obj, (short)stack_val, resource, effect_UID, true);
    
    //remove items and drain the resource of colour depending on how much resource has gone
    if (obj->RemoveItem(slot, stack_val) == 0.0f)
    {
        UnSetTarget(obj->GameID()); //unset target if it's empty
		//award object drain XP
		ResourceEmptyXP(obj);
    }
    
    if (incomplete == 1)
    {
        SendMessageString("Insufficient energy to continue prospecting.", 11);
    }
}

void Player::UseTractorBeam(Object *obj, short stack_val, ItemBase * contents, long effect_UID, bool mined)
{
    u32 prospect_tick = GetNet7TickCount();
    u32 tractor_time;
	SectorManager *sm = GetSectorManager();
	if (!sm) return;

    float resource_range;
    ItemBase * resource = contents;
    long article_effect_UID = sm->GetSectorNextObjID();
    long article_UID = sm->GetSectorNextObjID();
    short XP_earned;
    
    m_ProspectBeam = false;
    SetProspecting(false);

	RechargeReactor(m_ProspectDrain);
    //m_EnergyRecharge = GetNet7TickCount();

	float tractor_speed = m_Stats.GetStat(STAT_TRACTORBEAM_SPEED);
    
    m_TractorBeam = true;
    resource_range = obj->RangeFrom(Position(), true);
   	tractor_time = (long) ((resource_range/tractor_speed) * 1000.0f) * 2; //Cap the time.

    //This packet is going to need:
    //player_id, a rticle_UID, article_effect_UID
    //resource basset, prospect_tick, tractor_time, tractor_speed
    //resource_name

    unsigned char udp_tractor_packet[256];
    int index = 0;
        
    AddData(udp_tractor_packet, GameID(), index);
    AddData(udp_tractor_packet, article_UID, index);
    AddData(udp_tractor_packet, article_effect_UID, index);
    AddData(udp_tractor_packet, resource->GameBaseAsset(), index);
    AddData(udp_tractor_packet, prospect_tick, index);
    AddDataLS(udp_tractor_packet, resource->Name(), index);
    AddData(udp_tractor_packet, tractor_time, index);
    AddData(udp_tractor_packet, tractor_speed, index);
    AddData(udp_tractor_packet, obj->PosX(), index);
    AddData(udp_tractor_packet, obj->PosY(), index);
    AddData(udp_tractor_packet, obj->PosZ(), index);
        
    SendToRangeList(ENB_OPCODE_2013_TRACTOR_ORE, udp_tractor_packet, index);
    
    if (obj->ObjectType() == OT_RESOURCE || obj->ObjectType() == OT_HULK)
    {
        XP_earned = CalcMiningXP(stack_val, resource->TechLevel());
        AddMiningExploreExperience(XP_earned, stack_val, resource->Name());
    }
    
    m_FloatingOre_contents = resource;
    m_FloatingOre_stack = stack_val;
    m_TractorItemLocation[0] = obj->PosX();
	m_TractorItemLocation[1] = obj->PosY();
	m_TractorItemLocation[2] = obj->PosZ();

    //call 'TakeOreOnboard' when 'tractor_time' has elapsed.
    PopulateTimedCall(&m_ProspectTractorNode, B_COLLECT_RESOURCE, tractor_time, obj, GetNet7TickCount(), article_effect_UID, article_UID, (long)mined, 0, tractor_speed);    
}

void Player::TakeOreOnboard(Object *obj, long article_UID, bool mined)
{	
    //Finished prospecting, take the ore on board
    ItemBase * resource = m_FloatingOre_contents;
    short stack = m_FloatingOre_stack;
    
    if (stack < 1 || stack > 800)
    {
        return;
    }
    
    if (resource == 0 || obj == 0)
    {
        return; // something's not right.
    }
    
    if (obj->ResourceRemains() == 0.0f)
    {
        SendProspectAUX(GameID(), 3);
    }
        
    char msg_buffer[128];
	sprintf_s(msg_buffer, 128, "%s has %s %d %s", Name(), mined ? "prospected" : "picked up", stack, resource->Name());
    
    SendMessageStringToGroup(msg_buffer);
    
    //credit ore to inventory
    
    _Item myItem;
    
    memset(&myItem,0,sizeof(_Item));
    
    myItem.ItemTemplateID = resource->ItemTemplateID();
    myItem.Price = 0;
    myItem.Quality = 1;
    myItem.Structure = 1;
    myItem.StackCount = stack;
    myItem.TradeStack = stack;
    
    CargoAddItem(&myItem);
    SendAuxShip();

    //Check for mission stage
	//CheckMissions(obj->GetDatabaseUID(), resource->ItemTemplateID(), 0, OBTAIN_ITEMS);
    
    //--------------------------------------------
    
    //this is done in Net7Proxy, but we now want to override it now we have early ore intercept
    //remove the ore in space
    SendResourceRemoveRL(article_UID);
    
    m_TractorBeam = false;
	SetLooting(false);
    
    SendClientSound("cargo_on_board_1.wav");
}

void Player::PopulateTimedCall(TimeNode *this_node, broadcast_function func, long time_offset, Object *obj, long i1, long i2, long i3, long i4, char *ch, float a)
{
    //store params in node	
    memset ((void*)this_node, 0, sizeof(TimeNode));
    
    this_node->event_time = time_offset + GetNet7TickCount();
    this_node->func = func;
    this_node->obj = obj;
    this_node->i1 = i1;
    this_node->i2 = i2;
    this_node->i3 = i3;
    this_node->i4 = i4;
    if (ch != 0)
    {
        this_node->ch = g_StringMgr->GetStr(ch);
    }
    else
    {
        this_node->ch = 0;
    }
    this_node->a = a;
    this_node->player_id = GameID();
}

bool Player::CheckMiningConditions(long slot, float reactor_energy)
{
    Object *obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());
    bool fail = false;
    ItemBase * itembase = NULL; // declared before any goto bailout below

	if (!obj)
	{
	    LogMessage("Could not get the object from the object ID: %d\n", ShipIndex()->GetTargetGameID());
        fail = true;
        goto bailout;
    }

	itembase = obj->GetItem(slot);
    
    if (itembase == 0 || obj->GetStack(slot) == 0 || slot >= MAX_ITEMS_PER_RESOURCE || (obj->ObjectType() != OT_HULK && obj->ObjectType() != OT_RESOURCE))
    {
        LogMessage("Fault during prospecting. This is an internal Net7 error.\n");
		LogMessage("Object is %s [%d]\n", obj->Name(), obj->GameID());
		LogMessage("Slot # = %d. Object Type = %d [should be 4] Stack = %d\n", slot, obj->ObjectType(), obj->GetStack(slot));
		//best to destroy the object really if this occurs!
		SendPushMessage("A Bizarre event...", "MessageLine", 3000, 3);
		SendPushMessage("An ore you wanted is gone", "MessageLine", 3000, 3);
		SendPushMessage("Server is baffled ...", "MessageLine", 3000, 3);
        fail = true;
        goto bailout;
    }

	if (PlayerIndex()->RPGInfo.Skills.Skill[SKILL_PROSPECT].GetLevel() == 0)
	{
        SendMessageString("You do not have the prospecting skill", 11);
        fail = true;
        goto bailout;
	}
    
    if (this->ObjectIsMoving())
    {
        SendMessageString("Unable to prospect while ship moving.", 11);
        fail = true;
        goto bailout;
    }
    
    if (m_ProspectBeam == true)
    {
        SendMessageString("Prospect Beam already in use.", 11);
        fail = true;
        goto bailout;
    }
    
	if (!fail && itembase && !CheckInventoryForRoom(itembase->ItemTemplateID(), obj->GetStack(slot)))
    {
        SendMessageString("Inventory full.", 11);
        fail = true;
        goto bailout;
    }
    
    //lastly check energy:
    if (reactor_energy < GetEnergyPerOre(obj->GetItem(slot)))
    {
        SendMessageString("Insufficient energy to prospect.", 11);
        fail = true;
        goto bailout;
    }
    
    //Check we are still in range
    if (obj->RangeFrom(Position()) > ProspectRange())
    {
        OpenInterface(1,0);
        fail = true;
        goto bailout;
    }
    
    if (ShipIndex()->GetIsIncapacitated())
    {
        SendMessageString("Unable to Prospect while incapacitated.", 11);
        fail = true;
    }
    
bailout:
    if (fail)
    {
        SendClientSound("On_Fail");
    }
    
    return (!fail);
}

bool Player::CheckLootConditions(long slot, bool mined)
{
    Object *obj = GetObjectManager()->GetObjectFromID(ShipIndex()->GetTargetGameID());
    bool fail = false;
    
    if (obj == 0 || obj->GetItem(slot) == (NULL) || obj->GetStack(slot) == 0 || slot >= MAX_ITEMS_PER_RESOURCE || (obj->ObjectType() != OT_HUSK && obj->ObjectType() != OT_FLOATING_ORE))
    {
        if (obj) // something is giving slot 39! :O
        {
			LogMessage("INTERNAL ERR: Fault looting: %s [%d], Slot: %d, OType: %d, Stack: %d, mined %d\n", obj->Name(), obj->GameID(), slot, obj->ObjectType(), obj->GetStack(slot), (int)mined);
        }
        else
        {
            LogMessage("INTERNAL ERR: Fault looting unknown object from object ID: %d\n", ShipIndex()->GetTargetGameID());
        }
        fail = true;
        goto bailout;
    }
    
    if (m_TractorBeam == true)
    {
        //quickly check the node to see if it should have expired
        if (m_ProspectTractorNode.player_id != 0 &&
            m_ProspectTractorNode.event_time < GetNet7TickCount())
        {
            //force previous ore on board
            TakeOreOnboard(m_ProspectTractorNode.obj, m_ProspectTractorNode.i3, mined);
            m_TractorBeam = false;
            m_ProspectTractorNode.player_id = 0;
        }
        else
        {
            SendMessageString("Tractor Beam already in use.", 11);
            fail = true;
            goto bailout;
        }
    }
  
    if (!fail && !CheckInventoryForRoom(obj->GetItem(slot)->ItemTemplateID(), obj->GetStack(slot)))
    {
        SendMessageString("Inventory full.", 11);
        fail = true;
        goto bailout;
    }

	if (!CanHaveAnotherOf(obj->GetItem(slot)->ItemTemplateID()))
	{
        SendMessageString("Duplicate unique item.", 11);
        fail = true;
        goto bailout;
    }
    
    //Check we are still in range
    if (obj->RangeFrom(this) > TractorRange())	
    {
        OpenInterface(1,0);
        fail = true;
        goto bailout;
    }
    
    if (ShipIndex()->GetIsIncapacitated())
    {
        SendMessageString("Unable to loot while incapacitated.", 11);
        fail = true;
    }
    
bailout:
    if (fail)
    {
        SendClientSound("On_Fail");
    }
    
    return (!fail);
}

bool Player::CheckInventoryForRoom(long ItemID, int Stack)
{
    //TODO: get this from item data
    ItemBase *myItemBase = g_ItemBaseMgr->GetItem(ItemID);
    
    if (!myItemBase)
    {
        LogMessage("No ItemBase for Item with ID %d\b",ItemID);
        return false;
    }
    
    int MaxStack = myItemBase->MaxStack();
    int Slot = -1;
    
    if (MaxStack > 1)	//no point in checking stacks for unstackable items
    {
        Slot = GetCargoSlotFromItemID(0,ItemID);
        
        while (Slot != -1 && ShipIndex()->Inventory.CargoInv.Item[Slot].GetStackCount() == MaxStack)
        {
            Slot = GetCargoSlotFromItemID(Slot + 1, ItemID);
        }
    }
    
    if (Slot == -1)	//no partial stacks
    {
        return (GetCargoSlotFromItemID(0, -1) != -1);
    }
    
    //if the slot was not null at this point, then we have room
    return true;
}

long Player::GetCargoSlotFromItemID(int StartLocation, int ItemID)
{
    for (u32 i = StartLocation; i < ShipIndex()->Inventory.GetCargoSpace(); i++)
    {
		if(i >= 40)
		{
			break;
		}
        if (ShipIndex()->Inventory.CargoInv.Item[i].GetItemTemplateID() == ItemID)
        {
            return i;
        }
    }
    
    return -1;
}

void Player::LootItem(long slot, bool mined)
{
    u32 loot_tick = GetNet7TickCount();
    Object *obj = 0;
    u32 tractor_time;
    float resource_range;
	SectorManager *sm = GetSectorManager();
	if (!sm) return;
    long article_effect_UID = sm->GetSectorNextObjID();
    long article_UID = sm->GetSectorNextObjID();

	m_ProspectDrain = 0;
    
    if (CheckLootConditions(slot, mined))
    {
		bool Ignored,OnAction;
		//if cloaked remove cloak (for applicable ranks only)
		if(m_CurrentSkill && m_CurrentSkill->SkillInterruptable(&Ignored, &Ignored, &OnAction))
		{
			if(OnAction)
			{
				m_CurrentSkill->InterruptSkillOnAction(LOOTING);
			}
		}

		SetLooting(true);
		ObjectManager *om = GetObjectManager();
		if(om)
		{
			obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
		}
		else
		{
			//uh oh
			return;
		}
        ItemBase * loot = obj->GetItem(slot);
		if (!loot) return;
        m_TractorBeam = true;
        
        resource_range = obj->RangeFrom(Position(), true);

		float tractor_speed = m_Stats.GetStat(STAT_TRACTORBEAM_SPEED);
        tractor_time = (long) ((resource_range/tractor_speed) * 1000.0f) * 2; //cap the time
        
        unsigned char udp_tractor_packet[256];
        int index = 0;
        
        AddData(udp_tractor_packet, GameID(), index);
        AddData(udp_tractor_packet, article_UID, index);
        AddData(udp_tractor_packet, article_effect_UID, index);
        AddData(udp_tractor_packet, loot->GameBaseAsset(), index);
        AddData(udp_tractor_packet, loot_tick, index);
        AddDataLS(udp_tractor_packet, loot->Name(), index);
        AddData(udp_tractor_packet, tractor_time, index);
        AddData(udp_tractor_packet, tractor_speed, index);
        AddData(udp_tractor_packet, obj->PosX(), index);
        AddData(udp_tractor_packet, obj->PosY(), index);
        AddData(udp_tractor_packet, obj->PosZ(), index);
        
        SendToRangeList(ENB_OPCODE_2014_LOOT_ITEM, udp_tractor_packet, index);
                
        m_FloatingOre_contents = obj->GetItem(slot);
        m_FloatingOre_stack = obj->GetStack(slot);
		m_TractorItemLocation[0] = obj->PosX();
		m_TractorItemLocation[1] = obj->PosY();
		m_TractorItemLocation[2] = obj->PosZ();
        //call TakeOreOnboard 
        PopulateTimedCall(&m_ProspectTractorNode, B_COLLECT_RESOURCE, tractor_time, obj, GetNet7TickCount(), article_effect_UID, article_UID, (long)mined, 0, tractor_speed);
        
        obj->RemoveItem(slot, m_FloatingOre_stack);
    }
}

void Player::AbortProspecting(bool recharge_reactor, bool abort_both_beams)
{
    float event_completion;
    float ore_pos[3];
    Object * floating_ore;
    
    if (m_ProspectBeamNode.player_id != 0)
    {
        m_ProspectBeamNode.player_id = 0;
        //just deactivate the prospecting beam and abort
        RemoveEffectRL(m_ProspectBeamNode.i3);
        m_ProspectBeam = false;
        SetProspecting(false);
        
        if (recharge_reactor)
        {
            RechargeReactor(m_ProspectDrain);
        }
    }
    
    if (abort_both_beams && m_ProspectTractorNode.player_id != 0)
    {
        m_ProspectTractorNode.player_id = 0;
        //Leave the ore hanging in space
        
        RemoveEffectRL(m_ProspectTractorNode.i2);
        SendResourceRemoveRL(m_ProspectTractorNode.i3); //remove tractor ore
        
        //Now just create a Floating Ore type with the ore basset
        event_completion = (float)(m_ProspectTractorNode.event_time - GetNet7TickCount()) / (float)(m_ProspectTractorNode.event_time - m_ProspectTractorNode.i1); //calculate fraction of time remaining for completion
        ore_pos[0] = PosX() - (PosX() - m_ProspectTractorNode.obj->PosX())*event_completion;
        ore_pos[1] = PosY() - (PosY() - m_ProspectTractorNode.obj->PosY())*event_completion;
        ore_pos[2] = PosZ() - (PosZ() - m_ProspectTractorNode.obj->PosZ())*event_completion;
        
		if (GetObjectManager())
		{
			floating_ore = GetObjectManager()->AddNewObject(OT_FLOATING_ORE);

			if (floating_ore)
			{
				floating_ore->AddItem(m_FloatingOre_contents, m_FloatingOre_stack);
				floating_ore->SetPosition(ore_pos);
				floating_ore->SetLevel(m_FloatingOre_contents->TechLevel());
				floating_ore->SetHSV(0,0,0);
				floating_ore->SetBasset(m_FloatingOre_contents->GameBaseAsset());
				floating_ore->SetName(m_FloatingOre_contents->Name());
			}
		}
        
        m_TractorBeam = false;
    }
}

void Player::RemoveProspectNodes()
{
    if (m_ProspectBeamNode.player_id)
    {
        m_ProspectBeamNode.player_id = 0;
    }
    
    if (m_ProspectTractorNode.player_id)
    {
        m_ProspectTractorNode.player_id = 0;
    }
}