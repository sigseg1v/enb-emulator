// PlayerClass.cpp
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

#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"
#include "Opcodes.h"
#include "PacketMethods.h"
#include "StaticData.h"
#include <math.h>

#define BOOST_XP 1

int LevelXP[] = { 10000,12500,15000,17500,20000,22500,27500,32500,37500,42500,47500,52500,57500,62500,
				  67500,72500,77500,82500,87500,92500,97500,102500,102500,102500,102500,102500,102500,
				  102500,102500,102500,102500,102500,112500,122500,132500,142500,152500,162500,182500,
				  202500,222500,242500,262500,282500,302500,322500,342500,362500,382500,402500,
				  // Added XP for levels over 50
				  402500,402500,402500,402500,402500,402500,402500,402500,402500,402500,402500,402500,
				  402500,402500,402500,402500,402500,402500,402500,402500,402500,402500,402500,402500,
				  402500,402500,402500,402500,402500,402500,402500,402500,402500,402500,402500,402500,
				  402500,402500,402500,402500,402500,402500,402500,402500,402500,402500,402500,402500,
				  402500,402500,402500,402500,402500,402500,402500,402500,402500,402500,402500,402500
				};

int MOBXP[] =
{
	50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 
	1000,
	1200, 1400, 1600, 1800, 2000, 3000, 4000, 5000, 6000, 7000
} ;


// All experience in the game is now centrally run through this formula to calculate 
// how much experience you get based on difficulty_level vs player_level.
// The formula used here is the one developed by Westwood Games, as mentioned in their
// beta design docs.
long Player::CalculateXP(experience_type xp_type, short base, short difficulty_rating, short player_level, float spread_down, float spread_up)
{
	if (player_level < 0) // Normally, the player's level is used, however this can be 'hard set'
	{
		switch (xp_type)
		{
		case XP_COMBAT:
			player_level = (short)PlayerIndex()->RPGInfo.GetCombatLevel();
			break;
		case XP_EXPLORE:
			player_level = (short)PlayerIndex()->RPGInfo.GetExploreLevel();
			break;
		case XP_TRADE:
			player_level = (short)PlayerIndex()->RPGInfo.GetTradeLevel();
			break;
		default:
			LogMessage("Bad XP type for player %s\n",Name());
			return 0;
			break;
		}
	}

	float spread = 25.0f; // baseline spread for non-combat
	
    if (player_level < difficulty_rating)
	{
       spread = spread_down;
	}
    else if (player_level >= difficulty_rating)
	{
       spread = spread_up;
	}

	float step = base / spread;
	float xp_earned = (float)((difficulty_rating - player_level) * step) + base;

	if (xp_earned < 0.0f) xp_earned = 0.0f;

	// slightly higher then westwood's cap but works well with
	// group formula and its a bit easier to level this way.
    if (xp_earned > base * 5.0f) xp_earned = base * 5.0f;

    return (long) xp_earned; // large stacks can give > 32768xp
}

// OK a lied in the function headers above: not all XP currently runs
// through that formula - this is one such case. Eventually when sector
// challenge ratings are put into the game, Nav point XP will use the
// CalculateXP() formula. For now, however, do a simple hack.
void Player::AwardNavExploreXP(Object *obj)
{
    char message[128];
    sprintf_s(message, sizeof(message), "Discovered %s", obj->Name());
    SendClientSound("Location_Discovered", 0, 0);

	long xpAward = 400;
	long sector_id = 0;
	SectorData *sector_data = (0);

	if (!this) return;

	sector_id = PlayerIndex()->GetSectorNum();
	if (sector_id)
	{
		sector_data = g_ServerMgr->m_SectorContent.GetSectorData(sector_id);
	}

	// better formula needs fixes to calculateXP		
	if (sector_data && sector_data->challenge_rating)
	{
		xpAward = CalculateXP(XP_EXPLORE, 400, (short)ceil(sector_data->challenge_rating*5.5f), (short)ExploreLevel(), 16, 16);
	}
	if (xpAward < 250)  xpAward = 250;
	if (xpAward > 1250) xpAward = 1250;

    // This is just a hack for now
	if (obj->ObjectType() == OT_STARGATE || obj->Signature() < 1500.0f)
	{
		xpAward *= 2;
	}
    else if (obj->Signature() < 5000.0f) 
	{
		xpAward = (long)((float)xpAward * 1.5f);
	}

	SaveExploreNav(obj->GetDatabaseUID());
	g_ServerMgr->m_PlayerMgr.GroupExploreXP(this, message, xpAward);
}

void Player::AddMOBDestroyExperience(short mob_level, char *mob_name)
{
	char msg_buffer[128];
	float spread = floor(5.5f + (CombatLevel() / 10.0f));

    //if player is grouped, this will not display the correct XP, but it's only for debug.
	short xp_earned = CalculateMOBXP(mob_level);
	
	sprintf_s(msg_buffer, 128, "Defeated %s:", mob_name);

	LogMessage("Mob XP: %d MobLevel: %d (%s)\n", xp_earned, mob_level, Name());
    g_ServerMgr->m_PlayerMgr.GroupCombatXP(this, msg_buffer, mob_level);
}

void Player::AddMiningExploreExperience(short XP_earned, short stack, char *raw_name)
{						  
    char msg_buffer[128];	
    
    sprintf_s(msg_buffer, 128, "Prospected %s:", raw_name);
    
	g_ServerMgr->m_PlayerMgr.GroupExploreXP(this, msg_buffer, XP_earned);
    
    sprintf_s(msg_buffer, 128, "Prospected (%d) %s", stack, raw_name);
    SendMessageString(msg_buffer, 0);	
}

short Player::CalcMiningXP(short stack, short resource_techLevel)
{
	short oreXP = (resource_techLevel * 10);
	short xp = (short)CalculateXP(XP_EXPLORE, oreXP, (short)ceil(resource_techLevel*5.5f), (short)ExploreLevel());
	return xp * stack;
}

short Player::CalcAnalyzingXP(short item_techLevel)
{
	short analyzeXP = (item_techLevel * 25);
	short xp = (short)CalculateXP(XP_EXPLORE, analyzeXP, (short)ceil(item_techLevel*5.5f), (short)ExploreLevel());
	return  xp;
}

short Player::CalcRefineXP(short item_techLevel)
{
	short refinedXP = (item_techLevel * 30);
	short xp = (short)CalculateXP(XP_TRADE, refinedXP, (short)ceil(item_techLevel*5.5f), (short)TradeLevel());
	return  xp;
}

// Return the amount of tradeXP a player should get when trading a stack
// of items to a vendor or another player (adjusted for level cap).
long Player::CalcItemStackTradeXP(_Item *stack, u32 amountTraded)
{
	// No units in the stack are tradable, return
	if (stack->TradeStack < 1)
		return 0;

	ItemBase * itemBase = g_ItemBaseMgr->GetItem(stack->ItemTemplateID);

	// No itembase? Log the bug
	if (!itemBase)
	{
		LogMessage("ERROR: (%s) has unknown itembase for CalcItemStackTradeXP()\n", Name());
		return 0;
	}
	
	long XPvalue = 0;
	short XPLevelCap = 0;
	short XPPerUnit = 0;

	// Sanity check, we should never have more TradeStack then we have items
	// how do we get "TradeStack(5) > StackCount(5)" messages exactly?
	if (stack->TradeStack > stack->StackCount)
	{
		LogMessage("ERROR: %s TradeStack(%d) > StackCount(%d) for (%s)\n", 
			itemBase->Name(), stack->TradeStack, stack->StackCount, Name());
		stack->TradeStack = stack->StackCount;
	}
	
	// Trade Items
	if (itemBase->Category() == IB_CATEGORY_TRADE_GOOD)
	{
		short baseXP = Negotiate((short)ceil((stack->Price - stack->AveCost) * stack->Structure), false, true);
		short itemLevel = (short)ceil(itemBase->TechLevel()*5.5f);
		XPvalue = CalculateXP(XP_TRADE, baseXP, itemLevel, (short)TradeLevel());

		if (XPvalue > Negotiate(500,false,true)  || stack->ItemTemplateID == 5781) //Yum o rum
			XPvalue = Negotiate(500,false,true);

		//this is yielding way too low trade XP for trade runs. Carting a hold full of nanos from Fenris
		//to Somerled nets you around 800 credits profit. You should get more than 500 XP per item.
		//for high profit trade items you should *at least* get 1:1 XP per credit profit.
		if (baseXP > 500 && XPvalue < baseXP)
		{
			//for high profit items let's award a little more trade XP
			//round baseXP up to nearest 100 plus a little bit
			XPvalue = (baseXP/100 + 2)*100;
		}

		return XPvalue;
	} 
	// Ore
	else if (itemBase->Category() == IB_CATEGORY_RAW_RESOURCE)
	{
		short oreXP = (itemBase->TechLevel() * 5);
		XPvalue = CalculateXP(XP_TRADE, oreXP, (short)ceil(itemBase->TechLevel()*5.5f), (short)TradeLevel());
	}	// Large stacks
	else if (itemBase->SubCategory() == IB_SUBCATEGORY_AMMO ||
		itemBase->Category() == IB_CATEGORY_REFINED_RESOURCE)
	{
		short oreXP = (itemBase->TechLevel() * 10);
		XPvalue = CalculateXP(XP_TRADE, oreXP, (short)ceil(itemBase->TechLevel()*5.5f), (short)TradeLevel());
	}
	// Stackable 'other' item like components (drops)
	else if (itemBase->MaxStack() > 1) 
	{
		XPPerUnit = 200;
		XPvalue = CalculateXP(XP_TRADE, XPPerUnit, (short)ceil(itemBase->TechLevel()*5.5f), (short)TradeLevel());
	}
	// Other (non-stacking) Items (drops)
	else 
	{
		XPPerUnit = 500;
		XPvalue = CalculateXP(XP_TRADE, XPPerUnit, (short)ceil(itemBase->TechLevel()*5.5f), (short)TradeLevel());
	}

	if (!amountTraded || amountTraded > stack->TradeStack)
	{
		amountTraded = stack->TradeStack;
	}

	stack->TradeStack = stack->TradeStack - amountTraded;

	return XPvalue * amountTraded;
}

void Player::AwardTradeXP(char *message, long xp_gain, long group_bonus, bool skip_debt)
{
    float xp_bar = AwardXP(XP_TRADE, message, xp_gain, group_bonus, skip_debt);
    PlayerIndex()->RPGInfo.SetTradeXP(xp_bar);
    SendAuxPlayer();
}

void Player::AwardCombatXP(char *message, long xp_gain, long group_bonus, bool skip_debt)
{
    float xp_bar = AwardXP(XP_COMBAT, message, xp_gain, group_bonus, skip_debt);
    PlayerIndex()->RPGInfo.SetCombatXP(xp_bar);
    SendAuxPlayer();
}

void Player::AwardExploreXP(char *message, long xp_gain, long group_bonus, bool skip_debt)
{
    float xp_bar = AwardXP(XP_EXPLORE, message, xp_gain, group_bonus, skip_debt);
    PlayerIndex()->RPGInfo.SetExploreXP(xp_bar);
    SendAuxPlayer();
}

float Player::AwardXP(experience_type xp_type, char *prefix, long xp_gain, long group_bonus, bool skip_debt)
{
    u32 level;
    u32 skill_points_earned = 0;
    long xp;
    char xp_string[8],suffix[48]="";
    char msg_buffer[160];

	// boosts now AFTER caps
// #ifdef BETA_TESTING
//    xp_gain *= 4;
//	group_bonus *= 4;
// #endif
// #ifdef BOOST_XP
//	if (TotalLevel() < 75)
//	{
//		xp_gain *= 2;
//		group_bonus *= 2;
//	}
//	else if (TotalLevel() < 100)
//	{
//		xp_gain = (long)((float)xp_gain*1.5f);
//		group_bonus = (long)((float)group_bonus*1.5f);
//	}
// #endif

	#ifdef BETA_TESTING
    xp_gain *= 2;
	group_bonus *= 2;
#endif
#ifdef BOOST_XP
	if (TotalLevel() < 75)
	{
		xp_gain *= 2;
		group_bonus *= 2;
	}
	else if (TotalLevel() < 100)
	{
		xp_gain = (long)((float)xp_gain*1.5f);
		group_bonus = (long)((float)group_bonus*1.5f);
	}
#endif

	// pay any debt (but skip if the xp was redirected because debt was already removed)
	u32 debt = PlayerIndex()->GetXPDebt();
	if (!skip_debt && debt)
	{
		if ((u32)xp_gain/2 > debt)
		{
			xp_gain -= debt;
			debt = 0;
		}
		else
		{
			xp_gain /= 2;
			debt -= xp_gain;
		}
        sprintf_s(suffix, sizeof(suffix), " (%d debt paid, %d remaining)", xp_gain, debt);
		PlayerIndex()->SetXPDebt(debt);
		SaveXPDebt();
	}
    
    switch (xp_type)
    {
    case XP_COMBAT:
		// Sanity check. If XP is negative for any reason reset it to 0
		if (PlayerIndex()->RPGInfo.GetCombatXP() < 0.0f)
			PlayerIndex()->RPGInfo.SetCombatXP(0.0f);

        level = PlayerIndex()->RPGInfo.GetCombatLevel();
        xp = (u32)(PlayerIndex()->RPGInfo.GetCombatXP() * LevelXP[level]) + xp_gain;
        sprintf_s(xp_string, sizeof(xp_string), "Combat");
        break;

    case XP_EXPLORE:
		// Sanity check. If XP is negative for any reason reset it to 0
		if (PlayerIndex()->RPGInfo.GetExploreXP() < 0.0f)
			PlayerIndex()->RPGInfo.SetExploreXP(0.0f);

        level = PlayerIndex()->RPGInfo.GetExploreLevel();
        xp = (u32)(PlayerIndex()->RPGInfo.GetExploreXP() * LevelXP[level]) + xp_gain;
        sprintf_s(xp_string, sizeof(xp_string), "Explore");
        break;

    case XP_TRADE:
		// Sanity check. If XP is negative for any reason reset it to 0
		if (PlayerIndex()->RPGInfo.GetTradeXP() < 0.0f)
			PlayerIndex()->RPGInfo.SetTradeXP(0.0f);

        level = PlayerIndex()->RPGInfo.GetTradeLevel();
        xp = (u32)(PlayerIndex()->RPGInfo.GetTradeXP() * LevelXP[level]) + xp_gain;
        sprintf_s(xp_string, sizeof(xp_string), "Trade");
        break;

    default:
        LogMessage("Bad XP type for player %s\n",Name());
        return 0;
        break;
    }

	// format the message depending on the group type
	if (group_bonus < 0) // solo
	{
		sprintf_s(msg_buffer, sizeof(msg_buffer), "%s %d %s experience earned%s", prefix, xp_gain, xp_string, suffix);
	}
	else if (group_bonus == 0) // out of range of group
	{
		sprintf_s(msg_buffer, sizeof(msg_buffer), "%s %d %s experience earned (out of range for bonus)%s", prefix, xp_gain, xp_string, suffix);
	}
	else // grouped
	{
		sprintf_s(msg_buffer, sizeof(msg_buffer), "%s %d %s experience earned (%d + %d group bonus)%s", prefix, xp_gain, xp_string, xp_gain - group_bonus, group_bonus, suffix);
	}
	SendPriorityMessageString(msg_buffer,"MessageLine",4000,4);

    if (level > 50)
    {
        level = 50;
    }
   
    while (xp >= LevelXP[level]) 
    {			
        xp -= LevelXP[level];
        if (level < 19) 
        {
            skill_points_earned++;
            level++;
        } 
        else if (level < 39) 
        {
            skill_points_earned += 2;
            level++;
        } 
        else if (level < 50) 
        {
            skill_points_earned += 3; 
            level++;
        } 
        else
        {
            skill_points_earned++;
        }
    }

	//split XP among other bars
	if(level == 50 && 
		!(PlayerIndex()->RPGInfo.GetExploreLevel() == 50 && 
		PlayerIndex()->RPGInfo.GetTradeLevel() == 50 &&
		PlayerIndex()->RPGInfo.GetCombatLevel() == 50))
	{
#ifndef BOOST_XP
		// 20% of xp is lost when redirecting to other bars
		xp = xp * 8 / 10;
#endif
		switch (xp_type)
		{
		case XP_COMBAT:
			if(PlayerIndex()->RPGInfo.GetExploreLevel() == 50)
				AwardTradeXP("combat xp diverted to trade", xp, -1, true);
			else if(PlayerIndex()->RPGInfo.GetTradeLevel() == 50)
				AwardExploreXP("combat xp diverted to explore", xp, -1, true);
			else
			{
				AwardExploreXP("50% of combat xp diverted to explore", xp/2, -1, true);
				AwardTradeXP("50% of combat xp diverted to trade", xp/2, -1, true);
			}
			break;

		case XP_EXPLORE:
			if(PlayerIndex()->RPGInfo.GetCombatLevel() == 50)
				AwardTradeXP("explore xp diverted to trade", xp, -1, true);
			else if(PlayerIndex()->RPGInfo.GetTradeLevel() == 50)
				AwardCombatXP("explore xp diverted to combat", xp, -1, true);
			else
			{
				AwardCombatXP("50% of explore xp diverted to combat", xp/2, -1, true);
				AwardTradeXP("50% of explore xp diverted to trade", xp/2, -1, true);
			}
			break;

		case XP_TRADE:
			if(PlayerIndex()->RPGInfo.GetCombatLevel() == 50)
				AwardExploreXP("trade xp diverted to explore", xp, -1, true);
			else if(PlayerIndex()->RPGInfo.GetExploreLevel() == 50)
				AwardCombatXP("trade xp diverted to combat", xp, -1, true);
			else
			{
				AwardCombatXP("50% of trade xp diverted to combat", xp/2, -1, true);
				AwardExploreXP("50% of trade xp diverted to explore", xp/2, -1, true);
			}
			break;

		default:
			LogMessage("Bad XP type for player %s\n",Name());
			return 0;
		}

		//100% of remaining XP was diverted to other XP bar(s)
		xp = 0;
	}
    
    float xp_bar = (float)(xp)/(float)(LevelXP[level]);
       
    if (skill_points_earned > 0)
    {
        SendClientSound("Player_Levels",0,1);
        
        switch (xp_type)
        {
        case XP_COMBAT:
            PlayerIndex()->RPGInfo.SetCombatLevel(level);
            ShipIndex()->SetCombatLevel(level);
            break;

        case XP_EXPLORE:
            PlayerIndex()->RPGInfo.SetExploreLevel(level);
            break;
        
        case XP_TRADE:
            PlayerIndex()->RPGInfo.SetTradeLevel(level);
            break;
        }

        PlayerIndex()->RPGInfo.SetSkillPoints(PlayerIndex()->RPGInfo.GetSkillPoints() + skill_points_earned);
        PlayerIndex()->RPGInfo.SetTotalSkillPoints(PlayerIndex()->RPGInfo.GetTotalSkillPoints() + skill_points_earned);

		SaveSkillPoints();
        
        SendPushMessage("LEVEL UP!","QuickLine",0,3);
        sprintf_s(msg_buffer, sizeof(msg_buffer), "%s level is now %d!", xp_string, level);
        SendPushMessage(msg_buffer, "MessageLine", 3000, 3);
       
        // Update the level display when targeting
        LevelUpForSkills();
        UpdateSkills();       
        SendAuxShip();
		SaveAdvanceLevel(xp_type, level);
    }

	SaveXPBarLevel(xp_type, xp_bar);

    return xp_bar;
}

void Player::ResourceEmptyXP(Object *obj)
{
	float XP_for_clear = 50.0f;//(float)obj->Level() * 25.0f; //Should this be fixed for all roids or vary for level? For now, let's award clearing level 7 and above with extra XP
	if (obj->Level() > 6) XP_for_clear = (float)obj->Level() * 25.0f;
	char *buffer = (char*)m_ScratchBuffer;
	snprintf(buffer, 1024, "Emptied %s:", obj->Name());

	AwardExploreXP(buffer, (long)XP_for_clear);

	//now check for field clearance XP
	obj->AddToClearList(this);
}

short Player::CalculateMOBXP(short mob_level)
{
	short index = mob_level - PlayerIndex()->RPGInfo.GetCombatLevel() + 10;

	if (index < 0) 
	{
		return 0;
	}
	else if (index > 20)
	{
		return 8000;
	}
	else
	{
		return (short)MOBXP[index];
	}
}