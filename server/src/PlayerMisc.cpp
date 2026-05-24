// PlayerMisc.cpp
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

// These are routines that don't really have a proper home at the moment, placed here to avoid cluttering up PlayerClass & PlayerConnection

#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectClass.h"
#include "ObjectManager.h"
#include "Opcodes.h"
#include "PacketMethods.h"
#include "StaticData.h"
#include "UDPConnection.h"
#include "MOBDatabase.h"

long Player::GenerateTalkTree(TalkTree *tree, int tree_node_id)
{
	char *ptr = m_TalkTreeBuffer;
	TalkNode *talk_node = tree->Nodes[tree_node_id];
	TalkBranch *branch;
	long length;

	if (talk_node == (0))
	{
		return 0;
	}

	//queue sound effect/voice here if there is one.
	if (talk_node->Sound_Data)
	{
		SendClientSound(talk_node->Sound_Data, 0, 1);
	}

	memset(ptr, 0, TALKTREE_BUFFER_SIZE);

	length = ParseTalkTokens(ptr, talk_node->Text);

	if( (ptr + 2 + length) - m_TalkTreeBuffer >= TALKTREE_BUFFER_SIZE )
	{
		LogMessage("Error in GenerateTalkTree: Buffer too small.");
		return ptr - m_TalkTreeBuffer;
	}

	ptr = ptr + 2 + length;
	*ptr = (char)talk_node->NumBranches; //set branch count

	if( (ptr + 1) - m_TalkTreeBuffer >= TALKTREE_BUFFER_SIZE )
	{
		LogMessage("Error in GenerateTalkTree: Buffer too small.");
		return ptr - m_TalkTreeBuffer;
	}
	ptr++;

	//now add in each branch (vc2010 REALLY doesnt like this particular iterator at runtime)
	//for (BranchList::iterator itrBranchList = talk_node->Branches.begin(); itrBranchList < talk_node->Branches.end(); ++itrBranchList)
	for (u16 b=0; b < talk_node->Branches.size(); b++)
	{
		//branch = (*itrBranchList);
		branch = talk_node->Branches[b];
		*ptr = (char)branch->BranchDestination; //set branch destination

		if( (ptr + 4) - m_TalkTreeBuffer >= TALKTREE_BUFFER_SIZE )
		{
			LogMessage("Error in GenerateTalkTree: Buffer too small.");
			return ptr - m_TalkTreeBuffer;
		}
		ptr += 4;
		length = ParseTalkTokens(ptr, branch->Text);

		if( (ptr + 1 + length) - m_TalkTreeBuffer >= TALKTREE_BUFFER_SIZE )
		{
			LogMessage("Error in GenerateTalkTree: Buffer too small.");
			return ptr - m_TalkTreeBuffer;
		}
		ptr = ptr + 1 + length;
	}

	// check if its a more node
	if (talk_node->Flags == NODE_MORE)
	{
		m_MoreDestination = talk_node->Destination;
	}
	else
	{
		m_MoreDestination = 0;
		if (talk_node->NumBranches == 0)
			SendTalkTreeAction(6); // set done button depending on how far through the tree we are
	}

	length = (long)(ptr - m_TalkTreeBuffer);

	return length;
}

long Player::ParseTalkTokens(char *write_buffer, char *input_buffer)
{
	char *profession[] =
	{
		"Warrior",
		"Trader",
		"Explorer"
	};
	char *race[] =
	{
		"Terran",
		"Jenquai",
		"Progen"
	};
	char *aprofession[] =
	{
		"a Warrior",
		"a Trader",
		"an Explorer"
	};

	char *ptr = write_buffer;
	char *token_ptr;

	long length = 0;
	//scan text for flags
	token_ptr = strchr(input_buffer, '@');

	if (token_ptr)
	{
		char *tree_ptr = input_buffer;
		while (*tree_ptr != 0)
		{
			if (*tree_ptr == '@')
			{
				token_ptr = tree_ptr+1;
				switch (token_ptr[0])
				{
				case 'a': //a[n] @profession
					if (strncmp(token_ptr, "aprofession", 11) == 0)
					{
						if( (ptr + strlen(aprofession[Profession()])) - m_TalkTreeBuffer >= TALKTREE_BUFFER_SIZE )
						{
							LogMessage("Error in ParseTalkTree: Buffer too small.");
							return ptr - m_TalkTreeBuffer;
						}

						strcpy(ptr, aprofession[Profession()]);
						ptr += strlen(aprofession[Profession()]);
						tree_ptr += 11;
					}
					break;
				case 'c': //class (== profession)
					if (token_ptr[1] == 'l' && token_ptr[2] == 'a' && token_ptr[3] == 's' &&
						token_ptr[4] == 's')
					{
						if( (ptr + strlen(profession[Profession()])) - m_TalkTreeBuffer >= TALKTREE_BUFFER_SIZE )
						{
							LogMessage("Error in ParseTalkTree: Buffer too small.");
							return ptr - m_TalkTreeBuffer;
						}

						strcpy(ptr, profession[Profession()]);
						ptr += strlen(profession[Profession()]);
						tree_ptr += 10;
					}
					break;
				case 'n': //name
					if (token_ptr[1] == 'a' && token_ptr[2] == 'm' && token_ptr[3] == 'e')
					{
						if( (ptr + strlen(Name())) - m_TalkTreeBuffer >= TALKTREE_BUFFER_SIZE )
						{
							LogMessage("Error in ParseTalkTree: Buffer too small.");
							return ptr - m_TalkTreeBuffer;
						}

						strcpy(ptr, Name());
						ptr += strlen(Name());
						tree_ptr += 4;
					}
					break;
				case 'r': //race
					if (token_ptr[1] == 'a' && token_ptr[2] == 'c' && token_ptr[3] == 'e')
					{
						if( (ptr + strlen(race[Race()])) - m_TalkTreeBuffer >= TALKTREE_BUFFER_SIZE )
						{
							LogMessage("Error in ParseTalkTree: Buffer too small.");
							return ptr - m_TalkTreeBuffer;
						}

						strcpy(ptr, race[Race()]);
						ptr += strlen(race[Race()]);
						tree_ptr += 4;
					}
					break;
				case 'p':
					if (strncmp(token_ptr, "profession", 10) == 0)
					{
						if( (ptr + strlen(profession[Profession()])) - m_TalkTreeBuffer >= TALKTREE_BUFFER_SIZE )
						{
							LogMessage("Error in ParseTalkTree: Buffer too small.");
							return ptr - m_TalkTreeBuffer;
						}

						strcpy(ptr, profession[Profession()]);
						ptr += strlen(profession[Profession()]);
						tree_ptr += 10;
					}
					break;
				default:
					if( (ptr + 1) - m_TalkTreeBuffer >= TALKTREE_BUFFER_SIZE )
					{
						LogMessage("Error in ParseTalkTree: Buffer too small.");
						return ptr - m_TalkTreeBuffer;
					}
					*ptr++ = *tree_ptr;
					break;
				}
			}
			else
			{
				if( (ptr + 1) - m_TalkTreeBuffer >= TALKTREE_BUFFER_SIZE )
				{
					LogMessage("Error in ParseTalkTree: Buffer too small.");
					return ptr - m_TalkTreeBuffer;
				}

				*ptr++ = *tree_ptr;
			}
			tree_ptr++;
		}
	}
	else
	{
		if( (ptr + strlen(input_buffer)) - m_TalkTreeBuffer >= TALKTREE_BUFFER_SIZE )
		{
			LogMessage("Error in ParseTalkTree: Buffer too small.");
			return ptr - m_TalkTreeBuffer;
		}

		strcpy(ptr, input_buffer);
		ptr += strlen(input_buffer);
	}

	length = (long) (ptr - write_buffer);

	return length;
}

u16 Player::GetNodeFlags(TalkTree *tree, int tree_node_id)
{
	TalkNode *talk_node = tree->Nodes[tree_node_id];
	u16 flags = 0;
	if (talk_node != (0))
	{
		flags = talk_node->Flags;
	}

	return flags;
}

void Player::ResetWeaponMounts()
{
	//now reset each mount
	u32 weapons = 0;
	m_WeaponSlots = 0;
	u32 i;

	for(i=0;i<(u32)WeaponTable[ClassIndex() * 7];i++) 
	{
		AddWeapon(i+1);
	}

	for (i = 1; i <= PlayerIndex()->RPGInfo.GetHullUpgradeLevel(); i++)
	{
		if (WeaponTable[ClassIndex() * 7 + i] != 0)
		{
			AddWeapon(m_WeaponSlots + 1);
		}
	}
}

void Player::ResetDeviceMounts()
{
	//now reset each mount
	u32 devices = 0;
	u32 i;
	m_DeviceSlots = 0;

	for (i = 0; i <= PlayerIndex()->RPGInfo.GetHullUpgradeLevel(); i++)
	{
		devices += DeviceTable[ClassIndex()*7 + i];
	}

	for(i=0;i<devices;i++)
	{
		ShipIndex()->Inventory.Mounts.SetMount(9+i, DeviceMount);
		ShipIndex()->Inventory.EquipInv.EquipItem[9+i].SetItemTemplateID(-1);
		m_DeviceSlots++;
	}
}

void Player::DebugPlayerDock(bool flag)
{
	if (m_MyDebugPlayer == (0)) return;

	if (flag)
	{
		//issue docking impulses
		m_MyDebugPlayer->m_Oldroom = -1;
		m_MyDebugPlayer->m_Room = 0;
		m_MyDebugPlayer->SetActionFlag(65);
		m_MyDebugPlayer->SendStarbaseAvatarList();
		m_MyDebugPlayer->m_Oldroom = 0;
		m_MyDebugPlayer->SetActionFlag(65);
		m_MyDebugPlayer->SendStarbaseAvatarList();
		Sleep(300);
		if (m_MyDebugPlayer)
		{
			m_MyDebugPlayer->BroadcastPosition();
		}
	}
	else
	{
		m_MyDebugPlayer->m_Oldroom = 0;
		m_MyDebugPlayer->m_Room = -1;
		m_MyDebugPlayer->SetActionFlag(65);
		m_MyDebugPlayer->SendStarbaseAvatarList();
		m_MyDebugPlayer->BroadcastPosition();
	}
}

ObjectManager* Player::GetObjectManager()
{
	SectorManager *sm = GetSectorManager();
	ObjectManager *om = (0);
	if (sm)
	{
		om = sm->GetObjectManager();
	}
	return om;
}

SectorManager* Player::GetSectorManager()
{
	return g_ServerMgr->GetSectorManager((long)PlayerIndex()->GetSectorNum());
}

long Player::GetSectorNextObjID()
{
	SectorManager *sect_manager = GetSectorManager();
	if (sect_manager)
	{
		return sect_manager->GetSectorNextObjID();
	}
	else
	{
		return 0;
	}
}

void Player::ExposeDecosOn(Object *obj)
{
	if (m_ExposeDecos)
	{
		obj->SetObjectType(OT_NAV);
		obj->SetNavType(2);
	}
}

void Player::ExposeDecosOff(Object *obj)
{
	if (m_ExposeDecos)
	{
		obj->SetObjectType(OT_DECO);
		obj->SetNavType(0);
	}
}

u32 * Player::GetSectorPlayerList()
{
	u32 * sector_list = 0;
	SectorManager *sect_manager = g_ServerMgr->GetSectorManager((long)PlayerIndex()->GetSectorNum());
	if (sect_manager)
	{
		sector_list = sect_manager->GetSectorPlayerList();
	}

	return sector_list;
}

void Player::CheckArrivalTriggers()
{
	//first check for objects within 40K - only need to check these every 2 seconds
	if (m_MovementID%20) // NB movement ID in this func will always be a multiple of 5
	{
		GetObjectManager()->SetObjectsAtRange(this, TRIGGER_RANGE_1, m_NavRange_1, 0);
		GetObjectManager()->SetObjectsAtRange(this, TRIGGER_RANGE_2, m_NavRange_2, m_NavRange_1);
		GetObjectManager()->SetObjectsAtRange(this, TRIGGER_RANGE_3, m_NavRange_3, m_NavRange_2);
	}
}

int Player::GetVenderBuyPrice(int ItemID)
{

	VendorItemList *ItemL = 0;
	ItemBase * Item = 0;

	Item = g_ItemBaseMgr->GetItem(ItemID);
	if(!Item)
	{
		return 0;
	}

	//If no NPC involved, return the price
	if (!m_CurrentNPC)
	{
		return Item->Cost();
	}

	//if this item has value *to this npc*, return that
	for(vecItemList::const_iterator v_item = m_CurrentNPC->Vendor.Items.begin(); v_item < m_CurrentNPC->Vendor.Items.end(); ++v_item)
	{
		ItemL = g_ServerMgr->m_StationMgr.GetVendorItem(*v_item);

		if (ItemL && ItemL->ItemID == ItemID)
		{
			if (ItemL->BuyPrice > 0)
			{
				return ItemL->BuyPrice;
			}
			else
			{
				return 0;
			}
		}
	}

	//if this item has value, return it
	if(Item->VendorBuyPrice() > 0)
	{
		return Item->VendorBuyPrice();
	}	

	return 0;
}

/*void Player::CheckArrival()
{
if (!m_Arrival_Flag && !ObjectIsMoving())
{
m_Arrival_Flag = true;
CheckMissionArrivedAt();
}
}*/

void Player::AwardCreditsToGroup(u64 credits)
{
	// check for group
	Group *g = g_ServerMgr->m_PlayerMgr.GetGroupFromID(GroupID());
	Player *player = 0;

	if (g && g->ForceAutoSplit)
	{	
		int i,count;
		Player *plist[6];

		for (count=i=0;i<6;i++)
		{
			plist[i] = NULL;
			if (g->Member[i].GameID != -1 && g->AcceptedInvite[i])
			{
				player = g_ServerMgr->m_PlayerMgr.GetPlayer(g->Member[i].GameID);	

				if (player && player->GroupID() == GroupID())
				{
					plist[i] = player;
					count++;
				}
			}
		}
		if (count)
		{
			credits /= count;
			for (i=0;i<6;i++)
			{
				if (plist[i])
				{
					plist[i]->AwardCredits(credits,0,4);
				}
			}
		}
	}
	else
	{
		// give to self
		AwardCredits(credits,0,3);
	}
}

void Player::AwardCredits(u64 credits, long XP_earned, int messagetype)
{
	char msg_buffer[128];

	if (credits == 0 && XP_earned == 0) return;

	int sp_len = sprintf_s(msg_buffer, 128, "You have gained %ld credits", (int)credits);

	if (XP_earned > 0)
	{
		sprintf_s(msg_buffer + sp_len, 128 - sp_len, " and %ld trade experience", XP_earned);
	}

	if (messagetype != 4)
		SendPriorityMessageString(msg_buffer,"MessageLine",5000,4);

	if (credits > 0)
	{
		SendClientSound("coin.wav");
		if (messagetype)
		{
			if (messagetype == 4)
				sprintf_s(msg_buffer, 128, "Your share of the credits is %ld.", (int)credits);
			else
				sprintf_s(msg_buffer, 128, "%ld credits looted.", (int)credits);
			SendMessageString(msg_buffer, messagetype);
		}
	}

	if (XP_earned > 0)
	{
		sprintf_s(msg_buffer, sizeof(msg_buffer), "Item(s) eligible for");
		AwardTradeXP(msg_buffer, XP_earned);
	}

	PlayerIndex()->SetCredits(PlayerIndex()->GetCredits() + credits);
	SaveCreditLevel();
	SendAuxPlayer();
}

void Player::AwardFaction(long faction_id, long faction_change, bool play_sound)
{
	if (faction_change == 0) return;

	float faction = PlayerIndex()->Reputation.Factions.Faction[m_PDAFactionID[faction_id]].GetReaction();
	float new_faction_value = faction + (float)faction_change;

	PlayerIndex()->Reputation.Factions.Faction[m_PDAFactionID[faction_id]].SetReaction(new_faction_value);
	SaveFactionChange(faction_id, new_faction_value);

	SendAuxPlayer();

	FactionData *fData = g_ServerMgr->m_FactionData.GetFactionData(faction_id);

	if (play_sound && fData && fData->m_Faction_gain_sfx)
	{
		SendClientSound(fData->m_Faction_gain_sfx, 0, 1);
	}
}

float Player::GetFactionStanding(Object *obj)
{
	float standing = 0.0f;
	if (!obj) return standing;

	long faction_id = obj->GetFactionID();
	long faction_count = g_ServerMgr->m_FactionData.GetFactionCount();
	long player_faction_id = faction_list[ClassIndex()];

	//see if this faction is one of the PDA factions
	if (g_ServerMgr->m_FactionData.GetPDA(faction_id))
	{
		long pda_order = m_PDAFactionID[faction_id];
		//this faction can be influenced by the player's actions, read value from player indicies
		standing = PlayerIndex()->Reputation.Factions.Faction[pda_order].GetReaction();
	}
	else
	{
		//read the faction standing from the database
		standing = g_ServerMgr->m_FactionData.GetFactionStanding(player_faction_id, faction_id);
	}

	return standing;

}

void Player::AddRacialBuff()
{
	Buff RacialBuff;
	GetRacialBuff(RacialBuff,1,(short)ClassIndex());
	char *buffName = RacialBuff.BuffType;
	if (m_Buffs.FindBuff(buffName))
		m_Buffs.RemoveBuff(buffName);
	m_Buffs.AddBuff(&RacialBuff);

}

void Player::AddGroupRacialBuff(short count, short class_index)
{
	Buff GroupBuff;
	GetRacialBuff(GroupBuff,count,class_index);
	GroupBuff.IsPermanent = false;
	GroupBuff.ExpireTime = GetNet7TickCount() + 45000; // 45 sec
	m_Buffs.RefreshOrAddBuff(&GroupBuff);
}

void Player::GetRacialBuff(Buff& RacialBuff, short count, short class_index)
{
	const float Amount[] = { 0.05f, 0.09f, 0.12f, 0.14f, 0.15f, 0.15f };

	if (count < 1 || count > 6)
		count = 1;

	memset(&RacialBuff, 0, sizeof(Buff));
	RacialBuff.IsPermanent = true;
	RacialBuff.ExpireTime = MAXINT;
	RacialBuff.Stats[0].Value = Amount[count-1];
	RacialBuff.Stats[0].StatType = STAT_BUFF_MULT;
	switch (class_index)
	{
	case TERRAN_ENFORCER:
		strcpy_s(RacialBuff.BuffType, sizeof(RacialBuff.BuffType), "Terran Warrior Bonus Icon");
		RacialBuff.BuffType[sizeof(RacialBuff.BuffType)-1] = '\0';
		strcpy_s(RacialBuff.Stats[0].StatName, sizeof(RacialBuff.Stats[0].StatName), STAT_EQUIPMENT_DAMAGE_CONTROL);
		RacialBuff.Stats[0].StatName[sizeof(RacialBuff.Stats[0].StatName)-1] = '\0';
		break;
	case TERRAN_TRADER:
		strcpy_s(RacialBuff.BuffType, sizeof(RacialBuff.BuffType), "Terran Tradesman Bonus Icon");
		RacialBuff.BuffType[sizeof(RacialBuff.BuffType)-1] = '\0';
		strcpy_s(RacialBuff.Stats[0].StatName, sizeof(RacialBuff.Stats[0].StatName), STAT_SALES_BONUS);
		RacialBuff.Stats[0].StatName[sizeof(RacialBuff.Stats[0].StatName)-1] = '\0';
		break;
	case TERRAN_SCOUT:
		strcpy_s(RacialBuff.BuffType, sizeof(RacialBuff.BuffType), "Terran Explorer Bonus Icon");
		RacialBuff.BuffType[sizeof(RacialBuff.BuffType)-1] = '\0';
		strcpy_s(RacialBuff.Stats[0].StatName, sizeof(RacialBuff.Stats[0].StatName), STAT_IMPULSE);
		RacialBuff.Stats[0].StatName[sizeof(RacialBuff.Stats[0].StatName)-1] = '\0';
		break;
	case JENQUAI_DEFENDER:
		strcpy_s(RacialBuff.BuffType, sizeof(RacialBuff.BuffType), "Jenquai Warrior Bonus Icon");
		RacialBuff.BuffType[sizeof(RacialBuff.BuffType)-1] = '\0';
		strcpy_s(RacialBuff.Stats[0].StatName, sizeof(RacialBuff.Stats[0].StatName), STAT_CRITICAL_RATE);
		RacialBuff.Stats[0].StatName[sizeof(RacialBuff.Stats[0].StatName)-1] = '\0';
		break;
	case JENQUAI_SEEKER:
		strcpy_s(RacialBuff.BuffType, sizeof(RacialBuff.BuffType), "Jenquai Tradesman Bonus Icon");
		RacialBuff.BuffType[sizeof(RacialBuff.BuffType)-1] = '\0';
		strcpy_s(RacialBuff.Stats[0].StatName, sizeof(RacialBuff.Stats[0].StatName), STAT_WARP_RECOVERY);
		RacialBuff.Stats[0].StatName[sizeof(RacialBuff.Stats[0].StatName)-1] = '\0';
		break;
	case JENQUAI_EXPLORER:
		strcpy_s(RacialBuff.BuffType, sizeof(RacialBuff.BuffType), "Jenquai Explorer Bonus Icon");
		RacialBuff.BuffType[sizeof(RacialBuff.BuffType)-1] = '\0';
		strcpy_s(RacialBuff.Stats[0].StatName, sizeof(RacialBuff.Stats[0].StatName), STAT_ENERGY_RECHARGE);
		RacialBuff.Stats[0].StatName[sizeof(RacialBuff.Stats[0].StatName)-1] = '\0';
		break;
	case PROGEN_WARRIOR:
		strcpy_s(RacialBuff.BuffType, sizeof(RacialBuff.BuffType), "Progen Warrior Bonus Icon");
		RacialBuff.BuffType[sizeof(RacialBuff.BuffType)-1] = '\0';
		strcpy_s(RacialBuff.Stats[0].StatName, sizeof(RacialBuff.Stats[0].StatName), STAT_HULL_DAMAGE_CONTROL);
		RacialBuff.Stats[0].StatName[sizeof(RacialBuff.Stats[0].StatName)-1] = '\0';
		break;
	case PROGEN_PRIVATEER:
		strcpy_s(RacialBuff.BuffType, sizeof(RacialBuff.BuffType), "Progen Tradesman Bonus Icon");
		RacialBuff.BuffType[sizeof(RacialBuff.BuffType)-1] = '\0';
		strcpy_s(RacialBuff.Stats[0].StatName, sizeof(RacialBuff.Stats[0].StatName), STAT_SHIELD_RECHARGE);
		RacialBuff.Stats[0].StatName[sizeof(RacialBuff.Stats[0].StatName)-1] = '\0';
		break;
	case PROGEN_SENTINEL:
		strcpy_s(RacialBuff.BuffType, sizeof(RacialBuff.BuffType), "Progen Explorer Bonus Icon");
		RacialBuff.BuffType[sizeof(RacialBuff.BuffType)-1] = '\0';
		strcpy_s(RacialBuff.Stats[0].StatName, sizeof(RacialBuff.Stats[0].StatName), STAT_WEAPON_ENERGY_CONSERVATION);
		RacialBuff.Stats[0].StatName[sizeof(RacialBuff.Stats[0].StatName)-1] = '\0';
		break;
	}
}

void Player::RecalculateEnergyRegen(float MaxEnergy, float RechargeEnergy, bool pullData)
{
	if(pullData)
	{
		MaxEnergy = m_Stats.GetStat(STAT_ENERGY);
		RechargeEnergy = m_Stats.GetStat(STAT_ENERGY_RECHARGE);
	}
	float StartValue = GetEnergy();
	float ChargeRate = 0.0f;
	if (MaxEnergy != 0.0f)
	{
		ChargeRate = (RechargeEnergy / MaxEnergy) / 1000.0f;
	}
	unsigned long EndTime = GetNet7TickCount();
	if(ChargeRate != 0.0f)
	{
		EndTime += (unsigned long)((1.0f - StartValue) / ChargeRate);
	}
	EnergyUpdate(EndTime, ChargeRate, StartValue);
}

/*
1: vendor markup
3: vendor discount
5: terminal discount
7: quest markup
*/

int Player::Negotiate(int price, bool isDiscount, bool atVendor)
{
	int skillLevel = 0;
	float negotiateBonus = 0.0f;
	int levelReq = 0;
	float finalPrice = 0.0f;
	float minimum = 0.0f;

	//short circut function for quite alot of items, should cut down on CPU use
	if(price <= 0)
		return price;

	skillLevel = PlayerIndex()->RPGInfo.Skills.Skill[SKILL_NEGOTIATE].GetLevel();
	levelReq = (isDiscount)? ((atVendor)? 3 : 5) : ((atVendor)? 1 : 7);

	if(skillLevel >= levelReq)  //adjust bonus for negotiators
	{
		negotiateBonus = m_Stats.ModifyValueWithStat(STAT_SKILL_NEGOTIATE,(float)skillLevel);
		negotiateBonus = 0.05f + (negotiateBonus * 0.02f);
	}

	if(atVendor && !isDiscount) //add sales bonus for everyone
	{
		negotiateBonus += m_Stats.GetStat(STAT_SALES_BONUS);
	}

	//cap multiplier at 20%
	negotiateBonus = min(0.2f,negotiateBonus);
	//sanity check to force multiplier between 0.0 and 0.2
	negotiateBonus = max(0.0f,negotiateBonus);

	//create multiplier. If this is a discount, subtract our bonus from 1, otherwise this is a markup, so add it to 1.
	negotiateBonus = (isDiscount)? 1.0f - negotiateBonus : 1.0f + negotiateBonus;

	//Calculate price
	finalPrice = ((float)price) * negotiateBonus;

	//if this is a discount, don't let price go below 1
	//if this is a markup, don't let price rise above 0 if it is zero or negative (though how could it be, but I like this sanity check)
	finalPrice = (isDiscount)? max(1.0f,finalPrice) : max(0.0f,finalPrice) ;

	return (int) finalPrice;
}

bool hex2RGB(char *str, float &r, float &g, float &b)
{
	int ri,gi,bi;
	if(str && strlen(str) == 7 && str[0]=='#')
	{
		for(int i = 1; i < 7; i++)
		{
			if(!isxdigit((int)str[i]))
			{
				return false;
			}
		}
		sscanf_s(str,"#%2x%2x%2x",&ri,&gi,&bi);
		r = ri/255.0f;
		g= gi/255.0f;
		b= bi/255.0f;
		return true;
	}
	return false;  
}


bool Player::RecustomizeShip(char *cmd)
{
	//color <hull|wing|prof|engine|all> <primary|secondary> <gold|silver|bronze|<flat|glossy> #hexcolor>
	//style <hull|wing> <1|2|3>
	//name [name] | [#hexcolor]

	char command[512];
	char* next_token;

	int argc=0;
	char *argv[5];
	char *temp;
	char switchchar;

	int metal;
	int flat;
	int part;
	float r,g,b;

	char name[26];
	int namestart = 0;
	int nameend = 0;
	int colorstart = 0;

	if(!cmd || strlen(cmd) > 511)
	{
		return false;
	}

	strncpy_s(command, sizeof(command), cmd,512);
	command[511]='\0';
	temp = strtok_s(command," ",&next_token);

	while(temp && argc < (sizeof(argv)/sizeof(char *)))
	{
		argv[argc++]=temp;
		temp = strtok_s(NULL," ",&next_token);
	}

	if (argc < 1)
		return false;

	switch((char)tolower((int)argv[0][0]))
	{
	case 'c':
		if(argc < 4)
		{
			return false;
		}

		switchchar = (char)tolower((int)argv[1][0]); 
		part = 0;
		switch(switchchar)
		{
		case 'a':
			part+=2;
		case 'e':
			part+=2;
		case 'w':
			part+=2;
		case 'p':
			part+=2;
		case 'h':
			break;
		default:
			return false;
		}

		switchchar = (char)tolower((int)argv[2][0]);
		if(switchchar == 's')
		{
			part++;
		}
		else if(switchchar !='p')
		{
			return false;
		}

		switchchar = (char)tolower((int)argv[3][1]);
		if(switchchar != 'l')
		{
			flat = 0;
			r = g = b = 0.0f;
			//is metal
			switch(switchchar)
			{
			case 'o': //gold
				metal = 1;
				break;
			case 'i': //silver
				metal = 0;
				break;
			case 'r': //bronze
				metal = 2;
				break;
			default:
				return false;
			}
		}
		else if(switchchar == 'l')
		{
			if(argc < 5)
			{
				return false;
			}
			metal = -1;
			switchchar = (char)tolower((int)argv[3][0]);
			switch(switchchar)
			{
			case 'g':
				flat = 0;
				break;
			case 'f':
				flat = 1;
				break;
			default:
				return false;
			}

			if(!hex2RGB(argv[4],r,g,b))
			{
				return false;
			}
		}
		else
		{
			return false;
		}

		switch(part)
		{
		case 0: //hull primary
			//printf("hull primary metal=%d flat=%d r=%f g=%f b=%f\n",metal,flat,r,g,b);
			m_Database.ship_data.HullPrimaryColor.metal = metal;
			m_Database.ship_data.HullPrimaryColor.flat = flat;
			m_Database.ship_data.HullPrimaryColor.HSV[0] = r;
			m_Database.ship_data.HullPrimaryColor.HSV[1] = g;
			m_Database.ship_data.HullPrimaryColor.HSV[2] = b;
			break;
		case 1: //hull secondary
			//printf("hull secondary metal=%d flat=%d r=%f g=%f b=%f\n",metal,flat,r,g,b);
			m_Database.ship_data.HullSecondaryColor.metal = metal;
			m_Database.ship_data.HullSecondaryColor.flat = flat;
			m_Database.ship_data.HullSecondaryColor.HSV[0] = r;
			m_Database.ship_data.HullSecondaryColor.HSV[1] = g;
			m_Database.ship_data.HullSecondaryColor.HSV[2] = b;
			break;
		case 2: //profession primary
			//printf("prof primary metal=%d flat=%d r=%f g=%f b=%f\n",metal,flat,r,g,b);
			m_Database.ship_data.ProfessionPrimaryColor.metal = metal;
			m_Database.ship_data.ProfessionPrimaryColor.flat = flat;
			m_Database.ship_data.ProfessionPrimaryColor.HSV[0] = r;
			m_Database.ship_data.ProfessionPrimaryColor.HSV[1] = g;
			m_Database.ship_data.ProfessionPrimaryColor.HSV[2] = b;
			break;
		case 3: //profession secondary
			//printf("prof secondary metal=%d flat=%d r=%f g=%f b=%f\n",metal,flat,r,g,b);
			m_Database.ship_data.ProfessionSecondaryColor.metal = metal;
			m_Database.ship_data.ProfessionSecondaryColor.flat = flat;
			m_Database.ship_data.ProfessionSecondaryColor.HSV[0] = r;
			m_Database.ship_data.ProfessionSecondaryColor.HSV[1] = g;
			m_Database.ship_data.ProfessionSecondaryColor.HSV[2] = b;
			break;
		case 4: //wing primary
			//printf("wing primary metal=%d flat=%d r=%f g=%f b=%f\n",metal,flat,r,g,b);
			m_Database.ship_data.WingPrimaryColor.metal = metal;
			m_Database.ship_data.WingPrimaryColor.flat = flat;
			m_Database.ship_data.WingPrimaryColor.HSV[0] = r;
			m_Database.ship_data.WingPrimaryColor.HSV[1] = g;
			m_Database.ship_data.WingPrimaryColor.HSV[2] = b;
			break;
		case 5: //wing secondary
			//printf("wing secondary metal=%d flat=%d r=%f g=%f b=%f\n",metal,flat,r,g,b);
			m_Database.ship_data.WingSecondaryColor.metal = metal;
			m_Database.ship_data.WingSecondaryColor.flat = flat;
			m_Database.ship_data.WingSecondaryColor.HSV[0] = r;
			m_Database.ship_data.WingSecondaryColor.HSV[1] = g;
			m_Database.ship_data.WingSecondaryColor.HSV[2] = b;
			break;
		case 6: //engine primary
			//printf("engine primary metal=%d flat=%d r=%f g=%f b=%f\n",metal,flat,r,g,b);
			m_Database.ship_data.EnginePrimaryColor.metal = metal;
			m_Database.ship_data.EnginePrimaryColor.flat = flat;
			m_Database.ship_data.EnginePrimaryColor.HSV[0] = r;
			m_Database.ship_data.EnginePrimaryColor.HSV[1] = g;
			m_Database.ship_data.EnginePrimaryColor.HSV[2] = b;
			break;
		case 7: //engine secondary
			//printf("engine secondary metal=%d flat=%d r=%f g=%f b=%f\n",metal,flat,r,g,b);
			m_Database.ship_data.EngineSecondaryColor.metal = metal;
			m_Database.ship_data.EngineSecondaryColor.flat = flat;
			m_Database.ship_data.EngineSecondaryColor.HSV[0] = r;
			m_Database.ship_data.EngineSecondaryColor.HSV[1] = g;
			m_Database.ship_data.EngineSecondaryColor.HSV[2] = b;
			break;
		case 8: //all primary
			//printf("all primary metal=%d flat=%d r=%f g=%f b=%f\n",metal,flat,r,g,b);
			m_Database.ship_data.HullPrimaryColor.metal = metal;
			m_Database.ship_data.HullPrimaryColor.flat = flat;
			m_Database.ship_data.HullPrimaryColor.HSV[0] = r;
			m_Database.ship_data.HullPrimaryColor.HSV[1] = g;
			m_Database.ship_data.HullPrimaryColor.HSV[2] = b;
			memcpy(&m_Database.ship_data.ProfessionPrimaryColor,&m_Database.ship_data.HullPrimaryColor,sizeof(ColorInfo));
			memcpy(&m_Database.ship_data.WingPrimaryColor,&m_Database.ship_data.HullPrimaryColor,sizeof(ColorInfo));
			memcpy(&m_Database.ship_data.EnginePrimaryColor,&m_Database.ship_data.HullPrimaryColor,sizeof(ColorInfo));
			break;
		case 9: //all secondary	
			//printf("all secondary metal=%d flat=%d r=%f g=%f b=%f\n",metal,flat,r,g,b);
			m_Database.ship_data.HullSecondaryColor.metal = metal;
			m_Database.ship_data.HullSecondaryColor.flat = flat;
			m_Database.ship_data.HullSecondaryColor.HSV[0] = r;
			m_Database.ship_data.HullSecondaryColor.HSV[1] = g;
			m_Database.ship_data.HullSecondaryColor.HSV[2] = b;
			memcpy(&m_Database.ship_data.ProfessionSecondaryColor,&m_Database.ship_data.HullSecondaryColor,sizeof(ColorInfo));
			memcpy(&m_Database.ship_data.WingSecondaryColor,&m_Database.ship_data.HullSecondaryColor,sizeof(ColorInfo));
			memcpy(&m_Database.ship_data.EngineSecondaryColor,&m_Database.ship_data.HullSecondaryColor,sizeof(ColorInfo));
			break;
		default:
			//error D:
			break;	  
		}
		break;
	case 's':
		if(argc < 3)
		{
			return false;
		}
		switchchar = tolower((int)argv[1][0]);
		part = atoi(argv[2]);
		if(part < 1 || part > 3)
		{
			return false;
		}
		if(switchchar == 'w')
		{
			//printf("Set wing style %d\n",part);
			m_Database.ship_data.wing=part-1;
			SendVaMessage("Station Engineer: You may have to reequip your weapons to get them to show properly.");
		}
		else if(switchchar == 'h')
		{
			//printf("Set hull style %d\n",part);
			m_Database.ship_data.hull=part-1;
			SendVaMessage("Station Engineer: You may have to reequip your weapons to get them to show properly.");
		}
		break;
	case 'n':

		name[0] = '\0';
		while(cmd[namestart]!='\"' && cmd[namestart] !='\0' &&cmd[namestart]!='#'){namestart++;} //find start of string
		if(cmd[namestart] == '\0'){;return false;} //oops, no string found, eject
		if(cmd[namestart] == '\"')
		{
			namestart++;
			nameend = namestart;
			while(cmd[nameend] !='\"' && cmd[nameend] != '\0'){nameend++;}
			if(cmd[nameend] == '\0'){return false;}  //oops, no end of string found

			if(nameend-namestart > 25){return false;} //new name too long
			//color?
			colorstart = nameend+1;
			while(cmd[colorstart] != '#' && cmd[colorstart] != '\0'){colorstart++;}
			//copy new name
			strncpy_s(&name[0], sizeof(name), &cmd[namestart],nameend-namestart);
			name[nameend-namestart]='\0';
			//printf("Set new ship name to \"%s\"",name);
			strncpy_s(m_Database.ship_data.ship_name, sizeof(m_Database.ship_data.ship_name), name,26);
			m_Database.ship_data.ship_name[25]='\0'; //force terminate string
		}
		else
		{
			colorstart = namestart;
		}
		if(hex2RGB(&cmd[colorstart],r,g,b))
		{
			//printf(". set the color to %f %f %f",r,g,b);
			m_Database.ship_data.ship_name_color[0]=r;
			m_Database.ship_data.ship_name_color[1]=g;
			m_Database.ship_data.ship_name_color[2]=b;
		}

		break;
	case 'd':
		SendVaMessage("Decal change not operational at this time.");
		break;
	default:
		return false;
	}

	SetHullUpgrade();

	//save changes
	SaveDatabase();
	NeatenUpWeaponMounts();
	RemoveObject(GameID());
	// send new asset to yourself and everyone around
	SendShipData(this);
	SendAuxShipExtended();
	return true;
}

bool Player::CheckForInstalls()
{
	for(int x = 0; x < 20; x++)
	{
		m_Equip[x].Lock();
		if((m_Equip[x].GetItemBase() != NULL && VALID_ITEM(m_Equip[x].GetItemBase())) && (m_Equip[x].GetItemBase()->ItemTemplateID() != -1) && (m_Equip[x].ItemInstalled() == false))
		{
			m_Equip[x].Unlock();
			return false;
		}
		m_Equip[x].Unlock();
	}
	return true;
}

void Player::FinishAllInstalls()
{
	for(int x = 0; x < 20; x++)
	{
		m_Equip[x].Lock();
		if((m_Equip[x].GetItemBase() != NULL && VALID_ITEM(m_Equip[x].GetItemBase())) && (m_Equip[x].GetItemBase()->ItemTemplateID() != -1) && (m_Equip[x].ItemInstalled() == false))
		{
			m_Equip[x].Unlock();
			m_Equip[x].FinishInstall(this,x);
		}
		else
		{
			m_Equip[x].Unlock();
		}
	}
}

void Player::DisplayClassFactionStanding()
{
	char *profession[] =
	{
		"Warrior",
		"Trader",
		"Explorer"
	};
	char *race[] =
	{
		"Terran",
		"Jenquai",
		"Progen"
	};
	//display the startup faction status for this class
	SendVaMessage("Startup faction standings for the %s %s", race[Race()], profession[Profession()]);

	long faction_count = g_ServerMgr->m_FactionData.GetFactionCount();
	if (faction_count > 32) faction_count = 32;
	long player_faction_id = faction_list[ClassIndex()];

	for (int i=1;i<=faction_count;i++)
	{
		if (g_ServerMgr->m_FactionData.GetPDA(i))
		{
			char *faction_name = g_ServerMgr->m_FactionData.GetFactionName(i);
			float faction_standing = g_ServerMgr->m_FactionData.GetFactionStanding(player_faction_id, i);
			if (faction_standing < -9000.0f) faction_standing = -9000.0f;
			if (faction_standing > 9999.0f) faction_standing = 9999.0f;
			SendVaMessage("%i) %s : %.1f", i, faction_name, faction_standing);
		}
	}
}

bool Player::DisplayPlayerFactionStanding(char *username)
{
	//char *username = strtok_s(param, " ", NULL);
	long faction_count = g_ServerMgr->m_FactionData.GetFactionCount();
	if (faction_count > 32) faction_count = 32;

	Player * TargetP;
	TargetP= g_ServerMgr->m_PlayerMgr.GetPlayer(username);
	if (!TargetP)
	{
			SendVaMessage("Player %s is not online.", username);
			return false;
	}
	for (int i=1;i <=faction_count;i++)
		{
			if (g_ServerMgr->m_FactionData.GetPDA(i))
			{
				char *faction_name = g_ServerMgr->m_FactionData.GetFactionName(i);
				float faction_standing = TargetP->PlayerIndex()->Reputation.Factions.Faction[m_PDAFactionID[i]].GetReaction();
				if (faction_standing < -9000.0f) faction_standing = -9000.0f;
				if (faction_standing > 9999.0f) faction_standing = 9999.0f;
				SendVaMessage("%i) %s : %.1f", i, faction_name, faction_standing);
			}
		}
	return true;
}
bool Player::EditFactionStanding(char *param)
{
	char queryString[256];
	char *next_token;

	char *p_faction_id = strtok_s(param, " ", &next_token);
	char *p_new_faction = strtok_s(NULL, " ", &next_token);

	if (!p_faction_id || !p_new_faction)
	{
		SendVaMessageC(17, "//editfaction <1 .. 32 (from //displayfactions)> <-9000 .. 9999>");
		return false;
	}

	long faction_id = atoi(p_faction_id);
	float new_faction_standing = (float)atof(p_new_faction);
	long player_faction_id = faction_list[ClassIndex()];

	if (faction_id < 1 || faction_id > 32)
	{
		SendVaMessageC(17, "Faction ID must be between 1 and 32");
		return false;
	}

	if (new_faction_standing < -9000.0f || new_faction_standing > 9999.0f)
	{
		SendVaMessageC(17, "New faction standing must be between -9000 and 9999");
		return false;
	}

	// OK change faction id
	FactionData *data = g_ServerMgr->m_FactionData.GetFactionData(player_faction_id);
	data->m_value[faction_id] = new_faction_standing;

	// Now change database (this is lazy - should do this via the save manager)
	SendVaMessageC(12, "Now commiting new faction standing for %s [%.1f] to database.", g_ServerMgr->m_FactionData.GetFactionName(faction_id), new_faction_standing);
	SendVaMessageC(13, "You will need a //killfactions and then to logoff to change character to see new factions in PDA.");
	sql_connection_c connection( "net7", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
	sql_query_c FactionUpdate(&connection);

	sprintf_s(queryString, sizeof(queryString),
		"UPDATE `faction_matrix` SET `base_value` = '%f' WHERE `faction_id` = '%d' AND `faction_entry_id` = '%d'", new_faction_standing, player_faction_id, faction_id );
	FactionUpdate.run_query(queryString);

	return true;
}

bool Player::EditPlayerFactionStanding(char *param)
{
		char *next_token;
		char *p_name= strtok_s(param, " ", &next_token);
		char *p_faction_id = strtok_s(NULL, " ", &next_token);
		char *p_new_faction = strtok_s(NULL, " ", &next_token);

		if (!p_name || !p_faction_id || !p_new_faction)
			{
				SendVaMessageC(17, "//editplayerfaction <playername> <1 .. 32 (from //displayfactions)> <-9000 .. 9999>");
				return true;
			}

		long faction_id = atoi(p_faction_id);
		float new_faction_standing = (float)atof(p_new_faction);
		Player * target = g_ServerMgr->m_PlayerMgr.GetPlayer(p_name);
		if (!target)
			{
				SendVaMessageC(17, "Player '%s' not found", p_name);
				return true;
			}	

		if (faction_id < 1 || faction_id > 32)
			{
				SendVaMessageC(17, "Faction ID must be between 1 and 32");
				return true;
			}

		if (new_faction_standing < -9000.0f || new_faction_standing > 9999.0f)
			{
				SendVaMessageC(17, "New faction standing must be between -9000 and 9999");
				return true;
			}

		// OK change faction value
		target->PlayerIndex()->Reputation.Factions.Faction[m_PDAFactionID[faction_id]].SetReaction(new_faction_standing);
		SaveFactionChange(m_PDAFactionID[faction_id],new_faction_standing);
		SendAuxPlayer();
		SendVaMessageC(16, "New Faction standing for '%s' Faction '%d' is now '%f'! ", p_name, faction_id, new_faction_standing);
		return true;	
}

void Player::SetEnvironmentalEffect(Object *obj)
{
	switch(obj->ObjectType())
	{					
	case OT_GWELL:						
		SetGWell(obj->ObjectIndex());
		break;

	case OT_RADIATION:
		SetRadiation(obj->ObjectIndex());
		break;

	default:
		break;
	}
}

void Player::UnSetEnvironmentalEffect(Object *obj)
{
	switch(obj->ObjectType())
	{					
	case OT_GWELL:						
		SetGWell(-1);
		break;

	case OT_RADIATION:
		SetRadiation(-1);
		break;

	default:
		break;
	}
}

void Player::SetGWell(long gwell_id)
{
	if (m_GWell == gwell_id)
		return;

    m_GWell = gwell_id;

    if (gwell_id != -1)
    {
        SendVaMessageC(17,"You are now in a Gravity Well.");
        SendClientSound("Gravity_Enter");
    }
    else
    {
        SendVaMessageC(17,"You are now leaving the Gravity Well.");
        SendClientSound("Gravity_Leave");
    }
}

void Player::SetRadiation(long rad_id)
{
	if (m_Radiation == rad_id)
		return;

    m_Radiation = rad_id;

    if (rad_id != -1 && !m_Buffs.FindBuff("Environment_Shield"))
    {
        SendVaMessageC(17,"You are now taking Radiation Damage.");
        SendClientSound("Radiation_Enter");
        RadiationDmg(true);
    }
    else
    {
        SendVaMessageC(17,"You are now leaving the Radiation area.");
        SendClientSound("Radiation_Exit");
        RadiationDmg(false);
    }
}

bool Player::IsMyFriend(char *name)
{
	for (int i=0;i < m_NumFriends;i++)
	{
		if (name && strcasecmp(m_FriendNames[i],name) == 0)
		{
			return true;
		}
	}
	return false;
}

void Player::AddFriend(char *name)
{
	int i;

	for (i=0;i < m_NumFriends;i++)
	{
		if (name && strcasecmp(m_FriendNames[i],name) == 0)
		{
			SendClientChatError(CHAT_ERROR_DUPLICATE_NAME,CCE_ADD_FRIEND,name);
			break;
		}
	}
	if (name && strcasecmp(name,Name()) == 0)
	{
		SendClientChatError(CHAT_ERROR_YOURSELF,CCE_ADD_FRIEND,name);
	}
	else if (i == m_NumFriends)
	{
		if (m_NumFriends < MAX_FRIEND_LIST)
		{
			strcpy_s(m_FriendNames[m_NumFriends++],sizeof(m_FriendNames[m_NumFriends]),name);
		}
		else
		{
			for (i=0;i < m_NumFriends;i++)
			{
				if (m_FriendNames[m_NumFriends][0] == 0)
				{
					strcpy_s(m_FriendNames[m_NumFriends++],sizeof(m_FriendNames[m_NumFriends]),name);
					break;
				}
			}
		}
		if (i == m_NumFriends)
		{
			SendClientChatError(CHAT_ERROR_REACHED_LIMIT,CCE_ADD_FRIEND,name);
		}
		else
		{
			SaveFriendsList(name,true);
			SendClientChatEvent(CHEV_NOW_FRIENDS,this,"",name);
		}
	}
}

void Player::RemoveFriend(char *name)
{
	int i;

	for (i=0;i < m_NumFriends;i++)
	{
		if (name && strcasecmp(m_FriendNames[i],name) == 0)
		{
			m_FriendNames[i][0] = 0;
			break;
		}
	}
	if (i == m_NumFriends)
	{
		SendClientChatError(CHAT_ERROR_NOT_A_MEMBER,CCE_REMOVE_FRIEND,name);
	}
	else
	{
		SaveFriendsList(name,false);
		SendClientChatEvent(CHEV_NO_LONGER_FRIENDS,this,"",name);
	}
}

void Player::ListFriends()
{
	char *name[MAX_FRIEND_LIST];
	char *sector[MAX_FRIEND_LIST];
	short count = 0;

	for (int i=0;i < m_NumFriends;i++)
	{
		if (m_FriendNames[i][0])
		{
			Player *pfriend = g_PlayerMgr->GetPlayer(m_FriendNames[i]);
			if (pfriend && (!pfriend->m_StatusToFriendsOnly || pfriend->IsMyFriend(Name())))
			{
				sector[count] = pfriend->PlayerIndex()->GetSectorName();
			}
			else
			{
				sector[count] = "offline";
			}
			name[count] = m_FriendNames[i];
			count++;
		}
	}
	SendClientChatList(CHAT_LIST_FRIENDS, name, sector, count, count);
}

bool Player::IsIgnored(char *name)
{
	for (int i=0;i < m_NumIgnore;i++)
	{
		if (name && strcasecmp(m_IgnoreNames[i],name) == 0)
		{
			return true;
		}
	}
	return false;
}

void Player::AddIgnore(char *name)
{
	int i;

	for (i=0;i < m_NumIgnore;i++)
	{
		if (name && strcasecmp(m_IgnoreNames[i],name) == 0)
		{
			SendClientChatError(CHAT_ERROR_DUPLICATE_NAME,CCE_IGNORE,name);
			break;
		}
	}
	if (name && strcasecmp(name,Name()) == 0)
	{
		SendClientChatError(CHAT_ERROR_YOURSELF,CCE_IGNORE,name);
	}
	else if (i == m_NumIgnore)
	{
		if (m_NumIgnore < MAX_FRIEND_LIST)
		{
			strcpy_s(m_IgnoreNames[m_NumIgnore++],sizeof(m_IgnoreNames[m_NumIgnore]),name);
		}
		else
		{
			for (i=0;i < m_NumIgnore;i++)
			{
				if (m_IgnoreNames[m_NumIgnore][0] == 0)
				{
					strcpy_s(m_IgnoreNames[m_NumIgnore++],sizeof(m_IgnoreNames[m_NumIgnore]),name);
					break;
				}
			}
		}
		if (i == m_NumIgnore)
		{
			SendClientChatError(CHAT_ERROR_REACHED_LIMIT,CCE_IGNORE,name);
		}
		else
		{
			SaveIgnoreList(name,true);
			SendClientChatEvent(CHEV_NOW_IGNORING,this,"",name);
		}
	}
}

void Player::RemoveIgnore(char *name)
{
	int i;

	for (i=0;i < m_NumIgnore;i++)
	{
		if (name && strcasecmp(m_IgnoreNames[i],name) == 0)
		{
			m_IgnoreNames[i][0] = 0;
			break;
		}
	}
	if (i == m_NumIgnore)
	{
		SendClientChatError(CHAT_ERROR_NOT_A_MEMBER,CCE_UNIGNORE,name);
	}
	else
	{
		SaveIgnoreList(name,false);
		SendClientChatEvent(CHEV_NO_LONGER_IGNORING,this,"",name);
	}
}

void Player::ListIgnores()
{
	char *name[MAX_FRIEND_LIST];
	short count = 0;

	for (int i=0;i < m_NumIgnore;i++)
	{
		if (m_IgnoreNames[i][0])
		{
			name[count] = m_IgnoreNames[i];
			count++;
		}
	}
	SendClientChatList(CHAT_LIST_IGNORES, name, NULL, count, 0);
}

void Player::DoVrixEncoding(char *message)
{
	if (!Hijackee()) return;

	Object *obj = (0);
	ObjectManager *om = GetObjectManager();

	if (om) obj = om->GetObjectFromID(Hijackee());

	if (obj && obj->GetFactionID() == __vrix_faction)
	{
		//convert the message in place
		long len = strlen(message);
		for (long i = 0; i < len; i++)
		{
			switch (message[i])
			{
			case 'a':
			case 'A':
				message[i] = '1';
				break;
			case 'e':
			case 'E':
				message[i] = '2';
				break;
			case 'i':
			case 'I':
				message[i] = '3';
				break;
			case 'o':
			case 'O':
				message[i] = '4';
				break;
			case 'u':
			case 'U':
				message[i] = '5';
				break;
			case 'y':
			case 'Y':
				message[i] = '6';
				break;
			default:
				break;
			}
		}
	}
}