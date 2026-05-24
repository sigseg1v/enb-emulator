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
#include <float.h>
#include "PlayerClass.h"
#include "ServerManager.h"
#include "ObjectManager.h"
#include "MissionManager.h"
#include "Opcodes.h"
#include "TalkTreeParser.h"
#include "SectorManager.h"


//'One method for all mission increments' is clearly not a good approach after all.
//I'll revert this back to a hybrid of this and the original system, where there was one overall
//method 'still checkstagecompletionnodes' to siphon off each type of completion,
//and then call a finisher method to check if a stage has been completed.
bool Player::CheckStageCompletionNodes(long mission, long stage, long npc_id, Object *obj, long param_data, completion_node_type completion_type)
{
#define MISSION_BIT_ALREADY_DONE(count) ((completionCount = UpdateMissionData(completionIndex, count, mission_data)) == 0) //NB this changes the mission data
#define OPTIONALLY_UPDATE_MISSION_BIT (completionCount = UpdateMissionData(completionIndex, cNode->count, mission_data)); 
	bool conditions_met = true;
	bool mission_stage_data_usage = false;
	bool valid_mission_update = false;
	bool talk_npc = false;
	long mission_data = -1; // Special flag to signify Stage 0
	long mission_id;
	long mission_slot;
	int completionIndex = 0;
	int completionCount;
	long completion_mask = 0;
	AuxMission	*am = 0;
	long object_uid = 0;
	long node_data;
	if (obj)
	{
		object_uid = obj->GetDatabaseUID();
	}

	CompletionList::iterator itrCList;

	if (m_MissionDebriefed) //we aren't checking missions anymore, we were debriefed already about why we couldn't accept a mission
	{
		return false;
	}

	if (stage == 0) 
	{
		mission_id = mission; //no slot yet, 'mission' is the mission database ID
		mission_slot = -1;
	}
	else
	{
		mission_slot = mission; //mission is 'slot'
		am = &m_PlayerIndex.Missions.Mission[mission_slot];
		mission_id = am->GetDatabaseID();
		mission_data = am->GetMissionData();
	}

	//get node list
	MissionTree *mTree = g_ServerMgr->m_Missions.GetMissionTree(mission_id);

	if (m_DebugMissions && stage>0) 
	{
		SendVaMessageC(12,"Checking mission validity for %s stage [%d]", mTree->name, stage);
	}

	if (!mTree) return false;

	//now get completion list for the stage
	CompletionList *cList = &mTree->Nodes[stage]->completion_list;

	if (cList)
	{
		for (itrCList = cList->begin(); itrCList < cList->end(); ++itrCList)
		{
			CompletionNode *cNode = (*itrCList);

			// Check the "status" cases
			switch (cNode->type)
			{
			case GIVE_CREDITS_NPC:
				if (PlayerIndex()->GetCredits() < cNode->data)
				{
					if (talk_npc) AddStageExpoDebrief(cNode, param_data);
					//if (m_DebugMissions && stage>0) SendVaMessageC(11,"Insufficient credits: Required %d", cNode->data);
					return false;
				}
				OPTIONALLY_UPDATE_MISSION_BIT;
				break;
			case OBTAIN_BLUEPRINT:
				// See if we have this blueprint
				if (m_ManuRecipes.find(cNode->data) != m_ManuRecipes.end())
				{
					OPTIONALLY_UPDATE_MISSION_BIT
				}
				else
				{
					if (talk_npc) AddStageExpoDebrief(cNode, param_data);
					return false;
				}
				break;
			case GIVE_ITEM_NPC:
				if (CargoItemCount(cNode->data) < cNode->count)
				{
					if (talk_npc) AddStageExpoDebrief(cNode, param_data);
					return false;
				}
				OPTIONALLY_UPDATE_MISSION_BIT;
				break;
			case CARGO_AVAL_SLOTS:
				if (CargoFreeSpace() < cNode->data)
				{
					if (talk_npc) AddStageExpoDebrief(cNode, param_data);
					return false;
				}
				OPTIONALLY_UPDATE_MISSION_BIT;
				break;
				//case TAKE_ITEM_TO_LOCATION:
			case POSSESS_ITEM:
				if (CargoItemCount(cNode->data) < cNode->count)
				{
					if (talk_npc) AddStageExpoDebrief(cNode, param_data);
					return false;
				}
				OPTIONALLY_UPDATE_MISSION_BIT;
				break;
			case NEAREST_NAV:
				{
					Object *nearest_nav = GetNearestNav();
					if (nearest_nav && nearest_nav->GetDatabaseUID() != cNode->data)
					{
						if (m_DebugMissions && stage>0) 
						{
							Object *NNobj = g_SectorObjects[cNode->data];
							if (obj)
							{
								SendVaMessageC(11, "%s not nearest nav", NNobj->Name() );
							}
						}

						return false;
					}
					if (cList->size() == 1) valid_mission_update = true;
				}
				OPTIONALLY_UPDATE_MISSION_BIT;
				break;
			case RECEIVE_ITEM_NPC:
				if (GetCargoSlotFromItemID(0, -1) == -1)
				{
					if (talk_npc) AddStageExpoDebrief(cNode, param_data);
					return false;
				}
				OPTIONALLY_UPDATE_MISSION_BIT;
				break;
			case SECTOR:
				if (PlayerIndex()->GetSectorNum() != cNode->data)
				{
					/*if (m_DebugMissions && stage>0) 
					{
						SendVaMessageC(11, "not in sector %d. Currently in %d", cNode->data, PlayerIndex()->GetSectorNum() );
					}*/
					return false;
				}
				OPTIONALLY_UPDATE_MISSION_BIT;
				break;
			case PLAY_SOUND: //this doesn't affect mission acceptance
				break;
			}

			if(completion_type == cNode->type)
			{
				//CheckNodeStatus(cNode, am, param_data, completionIndex, 
				// Check the "trigger" cases
				// These should be mutually exclusive within the Mission Editor
				// and appear at the end of the list; if they are valid then we
				// can send a notification to the player.  They can be used with
				// some of the "status" cases.
				switch (cNode->type)
				{
				case ARRIVE_AT:
					// Did we arrive at the correct nav?
					if (object_uid != cNode->data
						|| MISSION_BIT_ALREADY_DONE(cNode->count))
						return false;
					break;
				case FIGHT_MOB:
					// Did we kill the correct mob and did we need to?
					if (mTree->Job_Category != 0)
					{
						node_data = GetJobData(mission_slot);
					}
					else
					{
						node_data = cNode->data;
					}
					if(param_data != node_data || MISSION_BIT_ALREADY_DONE(cNode->count))
						return false;
					if(completionCount != -1)
					{
						SendVaMessage("Killed %d of %d for %s.", completionCount, (int) cNode->count, am->GetName());
						LogMessage("Required MOB killed %d of %d by %s for mission (%d) %s\n",
							completionCount,
							(int) cNode->count,
							Name(),
							am->GetDatabaseID(),
							am->GetName());
					}
					break;					
				case OBTAIN_ITEMS:
					//case OBTAIN_ITEMS_AT_LOCATION:
					// Did we loot the correct item, and do we have the required amount now?
					if (cNode->data == param_data)
					{
						long count = CargoItemCount(cNode->data);
						if (count >= cNode->count)
						{
							OPTIONALLY_UPDATE_MISSION_BIT;
						}
						else
						{
							SendVaMessage("Obtained %d of %d for %s.", count, (int) cNode->count, am->GetName());
							return false;
						}
					}
					else
						return false;
					/*if (cNode->data != param_data || MISSION_BIT_ALREADY_DONE(cNode->count))
					return false;*/
					break;
				case TALK_NPC:
					// Are we talking to the right NPC?
					if (npc_id != cNode->data)
					{
						if (m_DebugMissions && stage>0) 
						{
							NPCTemplate *mission_npc = g_ServerMgr->m_StationMgr.GetNPC(cNode->data);
							if (mission_npc)
							{
								SendVaMessageC(11, "not talking to correct NPC: need to talk to %s %s", mission_npc->Avatar.avatar_first_name, mission_npc->Avatar.avatar_last_name );
							}
							else
							{
								SendVaMessageC(17, "MISSION ERROR: NPC %d doesn't exist in the table. Report this mission ID [%s] [%d] to devs", cNode->data, mTree->name, mTree->MissionID);
							}
						}
						return false;
					}
					talk_npc = true;
					OPTIONALLY_UPDATE_MISSION_BIT;
					break;
				case USE_SKILL_ON_MOB_TYPE:
					// Did we use the correct skill on the correct mob?
					if(npc_id != cNode->data // Correct mob targetted?
						|| param_data != cNode->count // Correct skill?
						|| MISSION_BIT_ALREADY_DONE(1))
						return false;
					completionIndex -= cNode->count - 1; // This ensures that this case only increments the completion index by one
					break;
				case USE_SKILL_ON_OBJECT:
					// Did we use the correct skill on the correct object?
					if(obj && cNode->data != object_uid // Correct object targetted?
						|| param_data != cNode->count // Correct skill?
						|| MISSION_BIT_ALREADY_DONE(1))
						return false;
					completionIndex -= cNode->count - 1; // This ensures that this case only increments the completion index by one
					break;
				case TALK_SPACE_NPC:         //this only triggers when you click on a Space NPC and are in range (5000 distance)
					if (param_data > 5000)
						return false;		 
					//drop through to PROXIMITY_TO_SPACE_NPC if range ok ...
				case PROXIMITY_TO_SPACE_NPC: //this only triggers when you stop within 2500 of the object
					if (npc_id != cNode->data)
						return false;
					m_PushMissionUID = npc_id;
					OPTIONALLY_UPDATE_MISSION_BIT;
					break;
				case NAV_MESSAGE:
					completionIndex = 0;
					if (npc_id != cNode->data) //is correct space object
						return false;
					if (param_data > cNode->count)//range is good
						return false;
					OPTIONALLY_UPDATE_MISSION_BIT;
					break;
				}
				valid_mission_update = true;
			}

			if (cNode->type != NAV_MESSAGE && cNode->type != OBTAIN_ITEMS && cNode->type != POSSESS_ITEM && cNode->type != GIVE_ITEM_NPC) //NAV_MESSAGE uses 'cNode->count' as range
			{
				completionIndex += cNode->count;
			}
		} // for each CompletionNode

		// Check whether every completion has been performed i.e. is the stage completed?
		if(stage != 0)
		{
			if (completionIndex > 31)
			{
				// TODO: Should be validated when loading the mission
				LogMessage("Mission stage overflow in mission %s ID #%d, stage %d\n",
					mTree->name, mTree->MissionID, stage);
				g_PlayerMgr->ErrorBroadcast("Mission stage overflow in mission %s ID #%d, stage %d\n",
					mTree->name, mTree->MissionID, stage);
				return false;
			}

			// Update the mission data that may have been modified above
			am->SetMissionData(mission_data);

			//form a test mask
			long test_mask = 0;
			for (int i=0; i < completionIndex; i++)
			{
				test_mask |= 1 << i;
			}

			//now check if all the indicies in this range are set
			if ( (test_mask & mission_data) != test_mask )
			{
				conditions_met = false;
			}
			else if (!EndStageItemCheck(mTree, stage)) //check to see if player has enough space if there's an item reward
			{
				conditions_met = false;
			}
		}
	}

	return conditions_met && valid_mission_update;
}

bool Player::CheckMissionStarted(long mission_id)
{
	AuxMission * am;
	for (int i = 0; i < MAX_MISSIONS; i++)
	{
		am = &m_PlayerIndex.Missions.Mission[i];
		if (   am->GetDatabaseID() == mission_id
			&& !am->GetIsCompleted()
			&& am->GetStageNum() > 0)
		{
			return true;
		}
	}
	return false;
}

bool Player::CheckMissionCompleted(long mission_id)
{
	bool ret_val = false;
	if (mission_id >= 0 && mission_id < MAX_MISSION_ID)
	{
		ret_val = GetBitEntry(m_CompletedMissions, mission_id);
	}
	else
	{
		LogMessage("Need to increase 'MAX_MISSION_ID' to %d, unless this is a bug\n", mission_id+1);
	}
	return ret_val;
}

bool Player::MissionStageNeeded(long completionIndex, long mission_data)
{
	//form required mask
	long needed_mask = 1<<completionIndex;

	if ( (mission_data & needed_mask) == 0 )
	{
		return true;
	}
	else
	{
		return false;
	}
}


long Player::GetSlotForMission(long mission_id, bool job)
{
	long mission_slot = -1;
	int i;
	for (i = 0; i < MAX_MISSIONS; i++)
	{
		if (m_PlayerIndex.Missions.Mission[i].GetStageNum() == 0)
		{
			mission_slot = i;
			break;
		}
	}

	if (!job)
	{
		if (mission_slot != -1 && CheckMissionCompleted(mission_id))
		{        
			mission_slot = -1;
		}

		if (mission_slot != -1)
		{            
			//do we already have this mission?
			for (i = 0; i < MAX_MISSIONS; i++)
			{
				AuxMission * m = &m_PlayerIndex.Missions.Mission[i];
				if (m->GetDatabaseID() == mission_id)
				{
					mission_slot = -1; //player already has this mission active
					//LogMessage("Cannot start mission %d, already active.\n", mission_id);
					SendTalkTreeAction(-32); //close display
					break;
				}
			}
		}
	}

	return mission_slot;
}


long Player::AssignMission(long mission_id)
{
	//mission valid?
	if (!g_ServerMgr->m_Missions.GetMissionCount())
	{
		LogMessage("Mission out of range: %d\n",mission_id);
		return (-1);
	}

	//check mission is valid
	MissionTree *mission = g_ServerMgr->m_Missions.GetMissionTree(mission_id);
	if (!mission)
	{
		return (-1);
	}

	if (mission->NumNodes < 2)
	{
		LogMessage("Invalid mission ID %d\n", mission_id);
		SendVaMessageC(13,"Please report mission '%s' ID %d as nvalid - too few stages.", mission->name, mission->MissionID);
		return (-1);
	}

	long mission_slot = GetSlotForMission(mission_id);

	if (mission_slot != -1)
	{        
		AuxMission * m = &m_PlayerIndex.Missions.Mission[mission_slot];
		m->Clear();
		m->SetDatabaseID(mission_id);
		m->SetName(mission->name);
		m->SetStageNum(1);
		m->SetStageCount(mission->NumNodes);
		m->SetSummary(mission->summary);
		m->SetIsForfeitable(mission->forfeitable);

		m->Stages.Stage[0].SetText(mission->Nodes[1]->description); //load the first stage description

		SendAuxPlayer();

		SaveAdvanceMission(mission_slot);
	}

	return mission_slot;
}

bool Player::CheckForNewMissions(long obj_id, long param_1, long npc_id)
{
	long mission_slot;
	_MissionList *m_list = g_ServerMgr->m_Missions.GetMissionList();
	MissionTree *mTree;
	bool criteria_met;
	RestrictionList::iterator itrRList;
	CompletionList::iterator itrCList;
	bool race_pass;
	bool profession_pass;
	bool race_restriction;
	bool profession_restriction;
	long objgame_id = 0;
	long mission_sz = g_ServerMgr->m_Missions.GetHighestID();
	long mission_id = 0;
	bool talk_npc_start = false;
	bool low_level = false;
	bool low_level_set_in_different_mission = false;

	Object *obj = g_SectorObjects[obj_id];

	if (obj)
	{
		objgame_id = obj->GameID();
	}

	if (g_ServerMgr->m_Missions.GetMissionStartNPC(npc_id) && AdminLevel() >= 80 && param_1 == 1 )
	{
		SendVaMessageC(12, "NPC %d is a start mission NPC. Mission ID's this NPC starts are:", npc_id);
	}

	//first check if we meet any mission criteria
	for (mission_id = 0; mission_id <= mission_sz; ++mission_id) 
	{
		mTree = (*m_list)[mission_id];
		if (!mTree) continue;

		talk_npc_start = false;
		if (g_ServerMgr->m_Missions.GetMissionStartNPC(npc_id) && param_1 == 1)
		{
			CompletionList *cList = &mTree->Nodes[0]->completion_list;
			if (cList)
			{
				for (itrCList = cList->begin(); itrCList < cList->end(); ++itrCList)
				{
					CompletionNode *cNode = (*itrCList);
					// Check the "status" cases
					if (cNode->type == TALK_NPC && cNode->data == npc_id)
					{
						if (AdminLevel() >= 80)
						{
							DisplayMissionRequirements(mTree);
						}
						talk_npc_start = true;
						break;
					}
				}
			} //if (cList)
		}

		//first see if we're already doing this mission
		if (CheckMissionStarted(mTree->MissionID))
			continue;

		//secondly if we have already done this mission
		if (CheckMissionCompleted(mTree->MissionID))
			continue;

		// See if we can take this mission
		if (mTree->MinSecurityLevel > AdminLevel())
			continue;

		criteria_met = true;
		race_pass = false;
		profession_pass = false;
		race_restriction = false;
		profession_restriction = false;

		//third check each restriction
		for (itrRList = mTree->restriction_list.begin(); itrRList < mTree->restriction_list.end(); ++itrRList)
		{
			RestrictionNode *rNode = (*itrRList);

			switch (rNode->type)
			{
			case OVERALL_LEVEL:
				if (TotalLevel() < (long)rNode->data) 
				{
					criteria_met = false;
					if (talk_npc_start) low_level = true;			
				}
				break;
			case COMBAT_LEVEL:
				if (CombatLevel() < (long)rNode->data) 
				{
					criteria_met = false;
					if (talk_npc_start) low_level = true;			
				}
				break;
			case EXPLORE_LEVEL:
				if (ExploreLevel() < (long)rNode->data) 
				{
					criteria_met = false;
					if (talk_npc_start) low_level = true;			
				}
				break;
			case TRADE_LEVEL:
				if (TradeLevel() < (long)rNode->data) 
				{
					criteria_met = false;
					if (talk_npc_start) low_level = true;			
				}
				break;
			case RACE:
				if (Race() == rNode->data) race_pass = true;
				race_restriction = true;
				break;
			case PROFESSION:
				if (Profession() == rNode->data) profession_pass = true;
				profession_restriction = true;
				break;
			case HULL_LEVEL:
				if (PlayerIndex()->RPGInfo.GetHullUpgradeLevel() != rNode->data) criteria_met = false;
				break;
			case FACTION_REQUIRED:
				if (PlayerIndex()->Reputation.Factions.Faction[rNode->flags].GetReaction() < (float)rNode->data) criteria_met = false;
				break;
			case ITEM_REQUIRED:
				if (CargoItemCount(rNode->flags) < (long)rNode->data) criteria_met = false;
				break;
			case MISSION_REQUIRED:
				if (!CheckMissionCompleted(rNode->data)) criteria_met = false;
				break;

			default:
				LogMessage("Error, unsupported type in mission '%s'\n", mTree->name);
				SendVaMessage("Error in mission requirements for mission [%d] '%s': please report to devs", mTree->MissionID, mTree->name);
				break;

			}

			if (criteria_met == false) break;
		}

		if (!m_Faction_Override)
		{
			if (race_restriction && !race_pass) criteria_met = false;
			if (profession_restriction && !profession_pass) criteria_met = false;

			if (((race_restriction && !race_pass) || (profession_restriction && !profession_pass)) 
				&& talk_npc_start == true && low_level_set_in_different_mission == false)
			{
				low_level = false;
			}
		}

		if (low_level)
		{
			low_level_set_in_different_mission = true;
		}

		if (criteria_met)
		{
			//check stage 0 requirements
			mission_slot = GetSlotForMission(mTree->MissionID);
			if (mission_slot != -1)
			{ 
				if (!InSpace() && CheckStageCompletionNodes(mTree->MissionID, 0, npc_id
					, 0, 0, TALK_NPC))
				{
					//we can now launch the starter talk tree
					m_MissionAcceptance = true;
					ProposeMissionTree(mTree->MissionID, param_1);				
					return true;
				}
				else if (InSpace())
				{
					if (CheckStageCompletionNodes(mTree->MissionID, 0, obj_id  // have we arrived at a mission giving object/NPC?
						, 0, 0, PROXIMITY_TO_SPACE_NPC) )
					{
						m_MissionAcceptance = true;
						m_PushMissionUID = obj_id;
						ProposePushMissionTree(mTree->MissionID, param_1);				
						return true;
					}
					else if (CheckStageCompletionNodes(mTree->MissionID, 0, obj_id  // have we arrived at a mission giving object/NPC?
						, 0, 0, TALK_SPACE_NPC) && ShipIndex()->GetTargetGameID() == objgame_id)
					{
						m_MissionAcceptance = true;
						m_PushMissionUID = obj_id;
						ProposeMissionTree(mTree->MissionID, param_1);				
						return true;
					}
				}
			}
		}
	}

	if (low_level == true && !InSpace()) //the NPC we're talking to had a mission, but we were too low level
	{
		AddDebrief("I have an important task I need you to do @name, but you need to be more experienced before I can trust you with the mission.");
	}

	if (m_MissionStageDebrief == true)
	{
		m_MissionStageDebrief = false;
		return true;
	}
	else
	{
		return false;
	}
}

bool Player::CheckMissions(long object_uid, long param_1, long npc_id, completion_node_type completion_type)
{
	bool mission_action = false;
	bool valid_mission = false;
	int i;
	int timeout;
	ObjectManager *om = GetObjectManager();
	Object *obj = g_SectorObjects[object_uid];
	AuxMission * am;

	//are we currently in a mission acceptance tree?
	if (m_MissionAcceptance)
	{
		if (InSpace())
		{
			return CheckSpaceNPC(param_1);
		}
		else
		{
			return CheckForNewMissions(object_uid, param_1, npc_id);
		}
	}
	else if (InSpace() && completion_type == TALK_NPC)
	{
		completion_type = TALK_SPACE_NPC;

		// Check to see if we have an ObjectManager first before trying to get TargetGameID.
		if (om) obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());
		if (obj)
		{
			npc_id = obj->GetDatabaseUID();
		}
	}
	else if (!InSpace() && m_PushMissionID > 0 && m_PushMissionID < 12 && param_1 > 1 && completion_type == TALK_NPC)
	{
		//we're in a convo tree for mission 'm_PushMissionID'
		am = &m_PlayerIndex.Missions.Mission[m_PushMissionID];
		ProcessConversation(m_PushMissionID, param_1, completion_type, am);
		return true;
	}

	//OK we have valid mission/s
	//check to see if we meet the criteria of the current stage
	for (i = 0; i < MAX_MISSIONS; ++i) 
	{
		timeout = 0;
		am = &m_PlayerIndex.Missions.Mission[i];
		if (!am->GetIsCompleted() && am->GetStageNum() > 0 && am->GetDatabaseID() > -1)
		{
			//check stage completion
			if (CheckStageCompletionNodes(i, am->GetStageNum(), npc_id, obj, param_1, completion_type))
			{
				//is there a talk tree to kick off?
				int result = ProcessConversation(i, param_1, completion_type, am);
				switch (result)
				{
				case -1:
					mission_action = false;
					break;
				case 1: //normal stage advance, check once more (time out after 6) to see if we can finish another stage
					i--;
					timeout++;
					mission_action = true;
					if (timeout > 5) break;
					continue;
					break;
				case 2:
				case 0:
					mission_action = true;
					break;
				}
			}
		}

		if (mission_action == true)
		{
			break;
		}
	}

	if (m_MissionStageDebrief == true)
	{
		m_MissionStageDebrief = false;
		mission_action = true;
	}

	return mission_action;
}

int Player::ProcessConversation(int i, long param_1, completion_node_type completion_type, AuxMission * am)
{
	int mission_action = 0;
	if (!NPCTalkTree(i, param_1, completion_type))
	{
		//ok this is a (at the moment) linear advancement mission
		//TODO: make either-or objectives, and mission stage gotos
		if(am->GetStageNum() + 1 == am->GetStageCount())
		{
			m_PushMissionID = 0;
			//assume this is an end of mission
			AdvanceMission(i, -3);
			mission_action = 2;
		}
		else if(am->GetStageNum() + 1 > am->GetStageCount())
		{
			m_PushMissionID = 0;
			LogMessage("Error, mission [%u] '%s' attempted to go to non-existing stage (%u)\n", am->GetDatabaseID(), am->GetName(), am->GetStageNum() + 1);
			SendVaMessage("Error in mission [%u] '%s': please report to devs, mission size: %d, trying to go to: %d. Stage %d should be a mission over.", am->GetDatabaseID(), am->GetName(), am->GetStageCount(), am->GetStageNum(), am->GetStageNum());
			mission_action = -1;
			if (completion_type == TALK_NPC)
			{
				SendTalkTreeAction(-32);
			}
		}
		else
		{
			m_PushMissionID = 0;
			AdvanceMission(i, am->GetStageNum()+1);
			mission_action = 1;
		}
	}

	return mission_action;
}

/* Each completion is processed serially, with the first completion starting at offset
0 and using "length" bits.  If any bits within that range are at 0 (i.e. not yet
done) then that index is returned.
Return values:
-1. This is stage 0 so it's ok to continue but do not update mission data nor notify the player
0. There are no bits "not yet done" so this action wasn't needed
?. Any other positive value means that there were bits "not yet done" and the actual value contains
the 1-base bit index that will be updated i.e. 1 for first, 2 for second, etc. */
int Player::UpdateMissionData(int offset, int length, long &missionData)
{
	if(missionData == -1)
	{
		// This happens during stage 0
		return -1;
	}
	for(int mission_flag = offset; mission_flag < offset + length; mission_flag++)
	{
		if ( ((1 << mission_flag) & missionData) == 0 )
		{
			// Found a bit we needed to do
			missionData = (1 << mission_flag) | missionData;
			//am->SetMissionData(missionData);
			return mission_flag - offset + 1;
		}
	}
	return 0;
}

void Player::CheckMissionMOBKill(long MOB_id)
{
	CheckMissions(0, MOB_id, 0, FIGHT_MOB);
}

void Player::CheckMissionSkillUse(long base_skill, long level)
{
	ObjectManager *om = GetObjectManager();
	Object *obj = (0);
	if (om) obj = om->GetObjectFromID(ShipIndex()->GetTargetGameID());

	if (obj)
	{
		switch(obj->ObjectType())
		{
		case OT_MOB:
			// SetUsedInMission has not been called for mobs with this type of completion!
			CheckMissions(0, base_skill, ((MOB*)obj)->GetMOBType(), USE_SKILL_ON_MOB_TYPE);
			break;
		case OT_STATION:
		case OT_STARGATE:
		case OT_RESOURCE:
		case OT_HULK:
		case OT_NAV:
		case OT_PLANET:
		case OT_HUSK:
			if (obj->GetUsedInMission())
				CheckMissions(obj->GetDatabaseUID(), base_skill, 0, USE_SKILL_ON_OBJECT);
			break;
		}
	}
}

void Player::CheckMissionRangeTrigger(Object *obj, long range)
{
	//this checks for mission updates for proximity to objects only

	//this can only be a 'NAV_MESSAGE'
	if (obj->GetUsedInMission())
	{
		CheckMissions(obj->GetDatabaseUID(), (long)obj->RangeFrom(Position()), obj->GetDatabaseUID(), NAV_MESSAGE);
	}
}

void Player::CheckMissionArrivedAt(Object *obj)
{
	//this can be an arrive_at, a last stop for a NAV_MESSAGE, or a push mission
	if (obj->GetUsedInMission())
	{
		CheckMissions(obj->GetDatabaseUID(), 0, obj->GetDatabaseUID(), ARRIVE_AT);
		CheckMissions(obj->GetDatabaseUID(), 500, obj->GetDatabaseUID(), NAV_MESSAGE); //last ditch to catch a nav message
		CheckForNewMissions(obj->GetDatabaseUID(), 1, 0); //might be a push mission, or a TALK_SPACE_NPC mission (coming out of warp).
	}
}

//no longer used
void Player::CheckMissionArrivedAt()
{

}

bool Player::CheckCurrentMissionStage(long type, long data, long count, bool check)
{
	int i;
	int completionIndex = 0;
	long stage_complete_mask = 0;
	CompletionList::iterator itrCList;
	long node_data;

	for (i = 0; i < MAX_MISSIONS; ++i) 
	{
		AuxMission * am = &m_PlayerIndex.Missions.Mission[i];
		if (am->GetStageNum() > 0 && am->GetDatabaseID() > -1)
		{
			MissionTree *mTree = g_ServerMgr->m_Missions.GetMissionTree(am->GetDatabaseID());
			if (mTree)
			{
				//now check current stage completion nodes to work out which one needs scanning
				CompletionList *cList = &mTree->Nodes[am->GetStageNum()]->completion_list;
				long mission_data = am->GetMissionData();
				if (cList)
				{
					completionIndex = 0;
					stage_complete_mask = (1<<(cList->size())) - 1; // gives all bits
					for (itrCList = cList->begin(); itrCList < cList->end(); ++itrCList)
					{
						CompletionNode *cNode = (*itrCList);

						if (mTree->Job_Category != 0)
						{
							node_data = GetJobData(i);
						}
						else
						{
							node_data = cNode->data;
						}

						//check this node hasn't been done
						if (cNode->type == type && data == node_data && MissionStageNeeded(completionIndex, mission_data))
						{
							if (check)
							{
								//this was just a check to see if we need to display a verb
								return true;
							}
							else
							{
								//mark as completed
								mission_data |= (1<<completionIndex);
								am->SetMissionData(mission_data);
								if (mission_data == stage_complete_mask)
								{
									//stage complete
									AdvanceMission(i, am->GetStageNum()+1);
								}
								else
								{
									SendVaMessageC(12, "Completed %d for %s.", completionIndex, am->GetName());
								}
							}
							return true;
						}
						completionIndex++;
					}
				}
			}
		}
	}
	return false;
}

void Player::MissionObjectVerb(Object *obj, long stage_type)
{
	bool success = CheckCurrentMissionStage(stage_type, obj->GetDatabaseUID(), 1);
	short verb_id = 0;

	switch (stage_type)
	{
	case SCAN_OBJECT:
		verb_id = VERBID_SCAN;
		break;
	case DEPLOY_ITEM:
		verb_id = VERBID_SCAN;
		//we need to remove the item that was deployed

		break;
	default:
		verb_id = 0;
		break;
	}

	if (success)
	{
		//mark target verb as inactive because if 'success' is true, it means we just completed this part of the mission.
		AddVerb(verb_id, -1.0f);
		MissionVerbDisplay(obj);
		UpdateVerbs();
	}
}

void Player::MissionVerbDisplay(Object *obj)
{
	//first see if this object is involved with missions
	if (obj->GetUsedInMission())
	{
		//find out if this is a valid scan target, if it is then display scan verb
		if (CheckCurrentMissionStage(SCAN_OBJECT, obj->GetDatabaseUID(), 1, true))
		{
			AddVerb(VERBID_SCAN, 1000.0f);
		}

		if (CheckCurrentMissionStage(DEPLOY_ITEM, obj->GetDatabaseUID(), 1, true))
		{
			AddVerb(VERBID_SCAN, 1000.0f);
		}
	}
}

bool Player::CheckMissionValidity(long target_param)
{
	Object *nearest_nav = NearestNav();
	ObjectManager *om = GetObjectManager();
	Object * target = (0);
	if (om) target = om->GetObjectFromID(ShipIndex()->GetTargetGameID());	// Get Target

	//quick check to see if device use is valid etc (will also be used for scans etc).
	bool success = false;
	for (int i = 0; i < MAX_MISSIONS; ++i) 
	{
		AuxMission * am = &m_PlayerIndex.Missions.Mission[i];
		if (am->GetStageNum() > 0 && am->GetDatabaseID() > -1)
		{
			if (CheckStageCompletionNodes(i, am->GetStageNum(), 0, target, target_param))
			{
				success = true;
				break;
			}
		}
	}

	return success;
}

bool Player::NPCTalkTree(long mission_slot, long response, completion_node_type completion_type)
{
	bool talk_tree = false;
	AuxMission * m = &PlayerIndex()->Missions.Mission[mission_slot];
	MissionTree *mTree = g_ServerMgr->m_Missions.GetMissionTree(m->GetDatabaseID());
	if (!mTree)
	{
		return false;
	}

	if (mTree->Nodes[m->GetStageNum()] == (0)) 
	{
		SendVaMessage("Bug in mission %s, stage %d, PLEASE REPORT TO DEVS", mTree->name, m->GetStageNum());
		return false;
	}

	switch (completion_type)
	{
	case NAV_MESSAGE:
		return false;
	case TALK_SPACE_NPC:
		if (response == 1) //send the avatar for the opening space talk tree.
		{
			SendPIPAvatar(-2, m_PushMissionUID, true);
		}
		break;
	default:
		break;
	}

	TalkTree *tree = &mTree->Nodes[m->GetStageNum()]->talk_tree;

	long length = GenerateTalkTree(tree, response);

	if (length)
	{
		SendOpcode(ENB_OPCODE_0054_TALK_TREE, (unsigned char *) m_TalkTreeBuffer, length);
		//read the talk tree node flags here
		//get the node flags and destination
		talk_type flags = tree->Nodes[response]->Flags;
		long destination = tree->Nodes[response]->Destination;

		if (response == 1) m_PushMissionID = mission_slot;

		switch (flags)
		{
		case NODE_MISSION_GOTO:
			if (destination == 0)
			{
				SendVaMessage("Error in mission '%s'. Stage destination for stage %d is invalid", mTree->name, m->GetStageNum());
				LogMessage("Error in mission '%s'. Stage destination for stage %d is invalid\n", mTree->name, m->GetStageNum());
				SendTalkTreeAction(-32);
			}
			else
			{
				SendTalkTreeAction(6);
				AdvanceMission(mission_slot, destination);
			}
			break;

		case NODE_MISSION_COMPLETE:
			SendTalkTreeAction(6);
			AdvanceMission(mission_slot, -3);
			break;

		case NODE_POSTPONE_MISSION:
			SendTalkTreeAction(6);
			RemoveMission(mission_slot);
			break;

		case NODE_DROP_MISSION:
			SendTalkTreeAction(6);
			CompleteMission(m->GetDatabaseID(), 1);
			m->SetIsCompleted(true);
			SendAuxPlayer();
			break;

		default:
			break;
		}

		talk_tree = true;
		/*if (next_stage != 0)
		{
		SendTalkTreeAction(6);
		AdvanceMission(mission_slot, next_stage);
		}*/
	}

	return talk_tree;
}

void Player::ProposePushMissionTree(long mission_id, long response)
{
	if (response == 1 && m_PushMissionID == 0)
	{
		SendConfirmedActionOffer(); //this produces the 'MISSION' tab on the client
		m_PushMissionID = mission_id;
	}
	else
	{
		LogMessage("What's happening here Response = %d??\n", response);
	}
}

bool Player::CheckSpaceNPC(long response)
{
	bool ret_val = false;
	int i;
	if (m_PushMissionID != 0)
	{
		if (m_MissionAcceptance)
		{
			//see if the current step 0 has a talk tree
			MissionTree *mTree = g_ServerMgr->m_Missions.GetMissionTree(m_PushMissionID);

			if (mTree)
			{
				ProposeMissionTree(m_PushMissionID, response);
				ret_val = true;
			}
		}
		else
		{
			//find the mission slot
			for (i = 0; i < MAX_MISSIONS; ++i) 
			{
				AuxMission * am = &m_PlayerIndex.Missions.Mission[i];
				if (am->GetDatabaseID() == m_PushMissionID)
				{
					//OK we're running a convo tree from this mission
					NPCTalkTree(i, response);
					ret_val = true;
					break;
				}
			}
		}
	}
	return ret_val;
}

void Player::ProposeMissionTree(long mission_id, long response)
{
	MissionTree *mTree = g_ServerMgr->m_Missions.GetMissionTree(mission_id);
	if (mTree)
	{
		TalkTree *tree = &mTree->Nodes[0]->talk_tree;
		long length = GenerateTalkTree(tree, response);

		if (length)
		{
			SendOpcode(ENB_OPCODE_0054_TALK_TREE, (unsigned char *) m_TalkTreeBuffer, length);

			talk_type flags = tree->Nodes[response]->Flags;
			long destination = tree->Nodes[response]->Destination;

			if (destination != 0 && flags == NODE_MISSION_GOTO)
			{
				SendTalkTreeAction(6);
				EndStageReward(mission_id, 0);
				AssignMission(mission_id);
				m_MissionAcceptance = false;
			}
		}
	}
	//check for any sounds that need to be issued
	if (response == 1)
	{
		CheckMissionStageSounds(mission_id, 0);
	}
}

void Player::CheckMissionStageSounds(long mission_id, long stage)
{
	CompletionList::iterator itrCList;
	MissionTree *mTree = g_ServerMgr->m_Missions.GetMissionTree(mission_id);

	if (!mTree) return;

	//check nodes for a sound
	//now get completion list for the stage
	CompletionList *cList = &mTree->Nodes[stage]->completion_list;

	if (cList)
	{
		for (itrCList = cList->begin(); itrCList < cList->end(); ++itrCList)
		{
			CompletionNode *cNode = (*itrCList);

			// Check the "status" cases
			if (cNode->type == PLAY_SOUND)
			{
				//queue up this sound
				SendClientSound(cNode->char_data, 0, 1); //queue up this sound
			}
		}
	}
}

void Player::AdvanceMission(long mission_slot, long stage)
{
	if(mission_slot >= 12) return;
	AuxMission * m = &(m_PlayerIndex.Missions.Mission[mission_slot]);

	MissionTree *mission = g_ServerMgr->m_Missions.GetMissionTree(m->GetDatabaseID());

	if (stage > 1)
	{
		CheckMissionStageSounds(m->GetDatabaseID(), stage-1);
	}

	if(stage >= (long)m->GetStageCount()) //first check this is a valid advancement
	{
		stage = -2; //actually - we might want a mission to end in a non-NPC type setting
		/*if (mission->Job_Category > 0)
		{
			//this is a job - it's ok to end the mission here
			
		}
		else
		{
			LogMessage("Error, mission [%u] '%s' attempted to go to non-existing stage (%u)\n", m->GetDatabaseID(), m->GetName(), stage);
			SendVaMessage("Error in mission [%u] '%s': please report to devs, mission size: %d, trying to go to: %d.", m->GetDatabaseID(), m->GetName(), m->GetStageCount(), stage);
			return;
		}*/
	}

	//see if there's any reward for completing the current stage
	if (CheckEndStageConditions(m))
	{
		EndStageReward(m->GetDatabaseID(), m->GetStageNum(), mission_slot);
		m->SetMissionData(0);

		if (stage > 0 && stage <= 20)
		{
			char *mission_error = "error! report to devs";
			char *description = g_ServerMgr->m_Missions.GetStageDescription(m->GetDatabaseID(), stage);
			if (description == 0)
			{
				description = mission_error;
			}
			m->SetStageNum(stage);
			m->Stages.Stage[stage-1].SetText(description);
			SendAuxPlayer();
			SaveAdvanceMission(mission_slot);
		}
		else if (stage == -2) //repeatable mission
		{
			m->SetIsCompleted(true);
			SendAuxPlayer();
			if (mission->Job_Category == 0)
			{
				CompleteMission(m->GetDatabaseID(), 0);
			}
			else
			{
				SendClientSound("Mission_Accomplished", 1, 1);
				//award faction
				AwardJobFaction(mission_slot);
			}
			RemoveMission(mission_slot);
		}
		else if (stage == -3) //non-repeatable mission
		{
			m->SetIsCompleted(true);
			SendAuxPlayer();
			CompleteMission(m->GetDatabaseID(), 1);
			RemoveMission(mission_slot);
		}
	}
}

bool Player::CheckEndStageConditions(AuxMission *m)
{
	bool success = true;
	CompletionList::iterator itrCList;
	//get node list
	MissionTree *mTree = g_ServerMgr->m_Missions.GetMissionTree(m->GetDatabaseID());
	if (mTree)
	{
		//now get completion list for the stage
		CompletionList *cList = &mTree->Nodes[m->GetStageNum()]->completion_list;

		Object *nearest_nav = GetNearestNav();

		if (cList)
		{
			for (itrCList = cList->begin(); itrCList < cList->end(); ++itrCList)
			{
				CompletionNode *cNode = (*itrCList);

				switch (cNode->type)
				{
				case GIVE_ITEM_NPC:
				case DEPLOY_ITEM:
					if (CargoItemCount(cNode->data) >= cNode->count)
					{
						CargoRemoveItem(cNode->data, cNode->count);
						SendAuxShip();
						success = true;
					}
					else
					{
						success = false;
					}
					break;

				case CARGO_AVAL_SLOTS:
					if (CargoFreeSpace() < cNode->data)
					{
						success = false;
					}
					break;

				case GIVE_CREDITS_NPC:
					if (PlayerIndex()->GetCredits() >= cNode->data)
					{
						PlayerIndex()->SetCredits(PlayerIndex()->GetCredits() - cNode->data);
						SaveCreditLevel();
					}
					else
					{
						success = false;
					}
					break;

				case NAV_MESSAGE: //display nav message
					{
						TalkTree *tree = &mTree->Nodes[m->GetStageNum()]->talk_tree;
						//strncpy(m_TalkTreeBuffer, tree->Nodes[1]->Text, TALKTREE_BUFFER_SIZE);
						if (tree->Nodes[1] && tree->Nodes[1]->Text)
						{
							memset(m_TalkTreeBuffer, 0, sizeof(m_TalkTreeBuffer));
							ParseTalkTokens(m_TalkTreeBuffer, tree->Nodes[1]->Text);
							SendPushMessage(m_TalkTreeBuffer, "MessageLine", 5000, 3);
						}
						else
						{
							SendVaMessageC(17,"INVALID push message for mission %s, stage %d - please report to content devs", mTree->name, m->GetStageNum());
						}
					}
					break;

				default:
					break;
				}

				if (!success) break;
			}
		}
		return success;
	}
	return false;
}

bool Player::EndStageItemCheck(MissionTree *mTree, long stage)
{
	RewardList::iterator itrRList;
	long slots_needed = 0;
	bool enough_space_available = true;

	if (mTree && mTree->Nodes[stage])
	{
		//now get completion list for the stage
		RewardList *rList = &mTree->Nodes[stage]->rewards;

		if (rList)
		{
			for (itrRList = rList->begin(); itrRList < rList->end(); ++itrRList)
			{
				RewardNode *rNode = (*itrRList);

				switch (rNode->type)
				{
				case ITEM_ID:
					{
						ItemBase *item = g_ItemBaseMgr->GetItem(rNode->data);
						if (item->MaxStack() > rNode->flags || rNode->flags == 1)
						{
							slots_needed++;
							if (rNode->flags > 1)
							{
								slots_needed += (rNode->flags - 1);
							}
						}
						else
						{
							slots_needed = ( rNode->flags / item->MaxStack() ) + 1;
						}
					}
					break;

				default:
					break;
				}
			}
		}
	}

	//ok we are to be awarded an item
	if (slots_needed && (CargoFreeSpace() < slots_needed))
	{
		enough_space_available = false;
		if (slots_needed > 1)
		{
			AddDebrief("/point Looks like you haven't got enough room in your cargo bay @name. Clear out some junk - you'll need at least %d empty slots.", slots_needed);
		}
		else
		{
			AddDebrief("/point Hello again @name, you will need to empty out some junk - come back when you've got some space in your cargo.");
		}
	}

	return enough_space_available;
}

void Player::EndStageReward(long mission_id, long stage, long slot)
{
	char msg_buffer[64];
	RewardList::iterator itrRList;
	int creditReward = 0;
	float job_multiplier = 1.0f;

	if (slot >= 0)
	{
		job_multiplier = GetJobRewardMultiplier(slot);
	}

	//get node list
	MissionTree *mTree = g_ServerMgr->m_Missions.GetMissionTree(mission_id);
	if (mTree && mTree->Nodes[stage])
	{
		//now get completion list for the stage
		RewardList *rList = &mTree->Nodes[stage]->rewards;

		if (rList)
		{
			for (itrRList = rList->begin(); itrRList < rList->end(); ++itrRList)
			{
				RewardNode *rNode = (*itrRList);

				long reward_val = (long)((float)rNode->data * job_multiplier);

				switch (rNode->type)
				{
				case CREDITS:
					creditReward = (long)((float)Negotiate(rNode->data,false,false) * job_multiplier);
					PlayerIndex()->SetCredits(PlayerIndex()->GetCredits() + creditReward);
					SaveCreditLevel();
					SendAuxPlayer();
					sprintf_s(msg_buffer, sizeof(msg_buffer), "You have gained %ld credits!", creditReward);
					SendMessageString(msg_buffer, 3);
					SendClientSound("coin.wav");
					break;

				case EXPLORE_XP:
					AwardExploreXP("Mission:", reward_val);
					break;

				case COMBAT_XP:
					AwardCombatXP("Mission:", reward_val);
					break;

				case TRADE_XP:
					AwardTradeXP("Mission:", reward_val);
					break;

				case FACTION:
					AwardFaction(rNode->flags, reward_val, true);
					break;

				case ITEM_ID:
					{
						_Item myItem = g_ItemBaseMgr->EmptyItem;

						if (rNode->flags == 0) rNode->flags = 1;

						myItem.ItemTemplateID = rNode->data;
						myItem.StackCount = rNode->flags;
						myItem.Price = 0;
						myItem.Quality = 1;
						myItem.Structure = 1;  

						LogMessage("Quest reward: %d for %s\n", myItem.ItemTemplateID, Name());

						CargoAddItem(&myItem);
						SendAuxShip();
					}
					break;

				case HULL_UPGRADE:
					ShipUpgrade(rNode->data);
					break;

				case RUN_SCRIPT:  //TODO
					break;

				case ITEM_BLUEPRINT:
				{
					int recipe = rNode->data; // copy out of packed struct
					m_ManuRecipes.insert(pair<int,int>(recipe,1));	// Unlock a scmatic
					SaveNewRecipe(recipe);				            // Save the scamatic
					break;
				}

				case AWARD_SKILL:
					if (PlayerIndex()->RPGInfo.Skills.Skill[rNode->data].GetAvailability()[0] == 3)
					{
						u32 Availability[4] = {4,0,0,1};
						PlayerIndex()->RPGInfo.Skills.Skill[rNode->data].SetAvailability(Availability);
						//set skill as level 1
						PlayerIndex()->RPGInfo.Skills.Skill[rNode->data].SetLevel(1);
						SkillUpdateStats(rNode->data);
						SaveNewSkillLevel(rNode->data, 1);
					}
					break;

				case ADVANCE_MISSION:
					{
						//first find if the player has this mission.
						for (int i = 0; i < MAX_MISSIONS; ++i) 
						{
							AuxMission * am = &m_PlayerIndex.Missions.Mission[i];
							if (am->GetDatabaseID() == rNode->data)
							{
								//OK found the relevant mission, now advance it
								AdvanceMission(i, am->GetStageNum() + 1);
								break;
							}
						}
					}
					break;

				case PLAY_SOUND_REWARD:
					if (rNode->char_data)
					{
						SendClientSound(rNode->char_data, 0, 1); //queue up this sound
					}
					break;

				default:
					break;
				}
			}
		}
	}
}

void Player::RemoveMission(long mission_slot)
{
	if (mission_slot >= 0 && mission_slot < 12)
	{
		//do we have this mission?
		AuxMission * m = &m_PlayerIndex.Missions.Mission[mission_slot];

		if (m && m->GetStageNum() > 0)
		{
			SaveRemoveMission(m->GetDatabaseID());
			m->Clear();
			m->SetStageNum(0);
			SendAuxPlayer();
		}
	}
}

void Player::MissionDismiss(long mission_slot, bool forfeit_pressed)
{
	if (mission_slot >= 0 && mission_slot < 12)
	{
		//see if this mission is forfeitable
		AuxMission * m = &m_PlayerIndex.Missions.Mission[mission_slot];
		if (m && (!forfeit_pressed || m->GetIsForfeitable()))
		{
			//mission cannot be repeated once forfeit
			//CompleteMission(m->GetDatabaseID(), 2);  // a '2' indicates mission was forfeited, so GM's can respond correctly
			RemoveMission(mission_slot);
		}
		else
		{
			SendVaMessageC(17,"This mission is non forfeitable."); // should not get here, button should not be there
		}
	}
}

void Player::AddStageExpoDebrief(CompletionNode *cNode, long param)
{
	ItemBase *item;

	if (param != 1 || InSpace()) return; //we only give the debrief message for first chat stage (subsequent convos will be stage 2+)

	switch (cNode->type)
	{
	case GIVE_CREDITS_NPC:
		AddDebrief("@name, I can't help you further until you have enough credits.");
		break;
	case OBTAIN_BLUEPRINT:
		item = g_ItemBaseMgr->GetItem(cNode->data);
		AddDebrief("You haven't learned the blueprint for %s yet, come back when you have.", item->Name());
		break;
	case GIVE_ITEM_NPC:
		item = g_ItemBaseMgr->GetItem(cNode->data);
		if (cNode->count > 1)
		{
			AddDebrief("Hey @name, where are those %ss we talked about? I need %d of them.", item->Name(), cNode->count); //Pluralisation gag - adding 's' is going to fail in some cases (what the heck... bring me those 'octopuss')
		}
		else
		{
			AddDebrief("Hey @name, when are you going to give me that %s?", item->Name());
		}
		break;
	case CARGO_AVAL_SLOTS:
	case RECEIVE_ITEM_NPC:
		AddDebrief("Looks like you haven't got enough room in your cargo bay @name. Get rid of some junk and I'll tell you more.");
		break;
	case POSSESS_ITEM:
		item = g_ItemBaseMgr->GetItem(cNode->data);
		if (cNode->count > 1)
		{
			AddDebrief("Listen @name, I need to see %d %s's in your cargo bay before we can go any further, knucklehead.", cNode->count, item->Name());
		}
		else
		{
			AddDebrief("Hey @name, I need to see a %s in your cargo bay before we can go any further, scuttlebug-for-brains @race.", item->Name());
		}
		break;
	}
}

#define MAX_DEBRIEF 400
void Player::AddDebrief(char *msg, ...)
{
	unsigned int len = MAX_DEBRIEF;
	char pch[MAX_DEBRIEF];
	char *ptr = m_TalkTreeBuffer;

	m_MissionStageDebrief = true;
	m_MissionDebriefed = true;

	memset(ptr, 0, TALKTREE_BUFFER_SIZE);

	va_list args;
	va_start(args, msg);
	vsprintf_s(pch, len, msg, args);
	long length = ParseTalkTokens(ptr, pch);
	va_end(args);

	ptr = ptr + 2 + length;
	*ptr = 1;
	ptr++;
	*ptr = -1;

	ptr += 4;

	length = ParseTalkTokens(ptr, "Continue...");

	ptr += length + 1;

	length = (long)(ptr - m_TalkTreeBuffer);

	SendOpcode(ENB_OPCODE_0054_TALK_TREE, (unsigned char *) m_TalkTreeBuffer, length);
}

void Player::DisplayMissionRequirements(MissionTree *mTree)
{
	_MissionList *m_list = g_ServerMgr->m_Missions.GetMissionList();
	MissionTree *mTree2;
	RestrictionList::iterator itrRList;
	SendVaMessageC(10, "[%d] : %s", mTree->MissionID, mTree->name);
	char *race[] =
	{
		"Terran",
		"Jenquai",
		"Progen"
	};
	char *profession[] =
	{
		"Warrior",
		"Trader",
		"Explorer"
	};

	ItemBase *item;

	for (itrRList = mTree->restriction_list.begin(); itrRList < mTree->restriction_list.end(); ++itrRList)
	{
		RestrictionNode *rNode = (*itrRList);
		switch (rNode->type)
		{
		case OVERALL_LEVEL:
			SendVaMessageC(12," Overall level %d", rNode->data);
			break;
		case COMBAT_LEVEL:
			SendVaMessageC(12," Combat level %d", rNode->data);
			break;
		case EXPLORE_LEVEL:
			SendVaMessageC(12," Explore level %d", rNode->data);
			break;
		case TRADE_LEVEL:
			SendVaMessageC(12," Trade level %d", rNode->data);
			break;
		case RACE:
			SendVaMessageC(12," Race: %s", race[rNode->data]);
			break;
		case PROFESSION:
			SendVaMessageC(12," Profession: %s", profession[rNode->data]);
			break;
		case HULL_LEVEL:
			SendVaMessageC(12," Hull level %d", rNode->data);
			break;
		case FACTION_REQUIRED:
			SendVaMessageC(12," Faction Required: %s [%d]", PlayerIndex()->Reputation.Factions.Faction[rNode->flags].GetName(), rNode->data);
			break;
		case ITEM_REQUIRED:
			item = g_ItemBaseMgr->GetItem(rNode->flags);
			if (item)
			{
				SendVaMessageC(12," Item Required: %s [%d] Count %d", item->Name(), rNode->flags, rNode->data);
			}
			else
			{
				SendVaMessageC(17," Item [%d] for this mission doesn't exist.", rNode->flags);
			}
			break;
		case MISSION_REQUIRED:
			mTree2 = (*m_list)[rNode->data];
			if (mTree2)
			{
				SendVaMessageC(12," Mission required: %s %d", mTree2->name, rNode->data);
			}
			else
			{
				SendVaMessageC(17," Mission required, but mission doesn't seem to exist [%d]", rNode->data);
			}
			break;
		}
	}
}

void Player::AcceptJob(JobNode *jn)
{
	//first check we have a free mission slot
	long mission_slot = GetSlotForMission(jn->MissionID, true);
	char *ptr = (char*)m_ScratchBuffer;

	MissionTree *mission = g_ServerMgr->m_Missions.GetMissionTree(jn->MissionID);

	if (mission_slot != -1 && mission)
	{
		//now push this job onto the player's job stack
		InsertJobIntoQueue(mission_slot, jn);

		DoJobEnvironmentals(jn, mission_slot);
		
		//assign mission to player
		AuxMission * m = &m_PlayerIndex.Missions.Mission[mission_slot];

		memset(m_ScratchBuffer, 0, 1024);

		g_ServerMgr->m_JobMgr->GetJobDescription((u8*)ptr, jn);
		m->Clear();
		m->SetDatabaseID(jn->MissionID);
		m->SetName(g_ServerMgr->m_JobMgr->GetJobTitle(jn->MissionID));
		m->SetStageNum(1);
		m->SetStageCount(mission->NumNodes);
		m->SetSummary(mission->summary);
		m->SetIsForfeitable(true);

		m->Stages.Stage[0].SetText(ptr); //load the first stage description

		//process rewards from stage 0 if any
		EndStageReward(jn->MissionID, 0);

		SendAuxPlayer();
	}
	else
	{
		SendVaMessageC(17," No free mission slot for this mission.");
	}
}

bool Player::InsertJobIntoQueue(long mission_slot, JobNode *jn)
{
	if (mission_slot >= MAX_MISSIONS || mission_slot < 0)
	{
		return false;
	}

	m_MissionNodes[mission_slot].Item = jn->Item;
	m_MissionNodes[mission_slot].Level = jn->Level;
	m_MissionNodes[mission_slot].Obj = jn->Obj;
	m_MissionNodes[mission_slot].Sponsor = jn->Sponsor;

	return true;
}

void Player::DoJobEnvironmentals(JobNode *jn, long mission_slot)
{
	Object *obj = jn->Obj;
	switch (jn->Category)
	{
	case JCAT_COMBAT:
		//initialise MOB if required
		if (jn->Mob && obj)
		{
			//spawn mob close to this nav
			ObjectManager *om = g_ServerMgr->GetObjectManager(jn->Obj->GetSector());
			MOB *mob = (MOB*)om->AddNewObject(OT_MOB); //MOB creation
			if (mob)
			{
				float pos[2];
				long name_len = strlen(jn->Mob->m_Name) + strlen(" for ") + strlen(this->Name());
				char *name = new char[name_len + 1];
				memset(name, 0, name_len+1);

				snprintf(name, name_len, "%s for %s", jn->Mob->m_Name, this->Name());

				SendVaMessage("Creating %s at level %d", name, jn->Mob->m_Level);
				float radius = obj->Radius() * (((float)(rand()%20)/20.0f) + 1.1f); // place mob out between 1 and 2 radii of the target object, so that it's never inside the nav
				//work out a position vector & angle
				float angle = (float)(rand()%360)/(360.0f / (2*PI));
				pos[0] = (radius) * cosf(angle);
				pos[1] = (radius) * sinf(angle);

				mob->SetPosition(obj->Position());
				mob->MovePosition(pos[0], pos[1], ((float)((rand()%1600) - 800) * 1.0f));

				mob->SetName(name);
				mob->SetActive(true);
				mob->SetRespawnTick(0);
				mob->SetOrientation(0.0f, 0.0f, 0.0f, 1.0f);
				mob->SetHostileTo(OT_PLAYER);
				float velocity = (float)(rand()%100 + 130);
				float turn = (float)((rand()%11) - 5) * 0.01f;
				if (turn == 0.0f) turn = 0.02f;
				mob->SetVelocity(velocity);
				mob->Turn(turn);
				mob->SetDefaultStats(turn, DRIFT, velocity, 50);
				mob->SetMOBType((short)jn->Mob->m_MOB_ID);
				mob->SetUpdateRate(50);
				mob->SetBehaviour(DRIFT);
				mob->AddBehaviourPosition(obj->Position());
				mob->SetRespawnTime(-1);
				mob->SetName(name);

				m_MissionNodes[mission_slot].Mob = mob;
			}
		}
		break;
	case JCAT_EXPLORE:
		break;
	case JCAT_TRADE:
		break;
	default:
		break;
	}
}

void Player::RemoveJobFromQueue(long mission_slot)
{
	if (mission_slot >= MAX_MISSIONS || mission_slot < 0)
	{
		return;
	}

	memset(&m_MissionNodes[mission_slot], 0, sizeof(MissionDataNode));
}

MissionDataNode *Player::GetMissionData(long mission_slot)
{
	if (mission_slot >= MAX_MISSIONS || mission_slot < 0)
	{
		return 0;
	}
	else
	{
		return (&m_MissionNodes[mission_slot]);
	}
}

long Player::GetJobData(long mission_slot)
{
	Object *obj = m_MissionNodes[mission_slot].Obj;
	MOB *mob = (MOB*)m_MissionNodes[mission_slot].Mob;
	long id = 0;

	if (mob)
	{
		id = mob->GetMOBType();
	}
	else if (obj)
	{
		id = obj->GetDatabaseUID();
	}

	return id;
}

//this method determines how the rewards for a mission are scaled up
float Player::GetJobRewardMultiplier(long mission_slot)
{
	float multiplier = 1.0f;
	MissionDataNode *node = GetMissionData(mission_slot);

	if (node)
	{
		if (node->Level >= 75) multiplier+=0.5f;
		if (node->Level >= 100) multiplier+=0.5f;
		if (node->Level >= 125) multiplier+=1.0f;
		if (node->Level >= 150) multiplier+=2.0f;
	}

	return multiplier;
}

void Player::AwardJobFaction(long mission_slot)
{
	MissionDataNode *node = GetMissionData(mission_slot);

	float faction_pts = 5.0f;
	faction_pts *= GetJobRewardMultiplier(mission_slot);

	AwardFaction(node->Sponsor, (long)faction_pts, true);
}