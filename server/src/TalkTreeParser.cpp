// TalkTreeParser.cpp
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

#include "Net7.h"

#ifdef USE_MYSQL_STATIONS

#include "TalkTreeParser.h"
#include "StringManager.h"
#include "xmlParser/xmlParser.h"
#include "PlayerClass.h"
#include "ObjectManager.h"
#include "ServerManager.h"
#include "MissionDatabaseSQL.h"

TalkTree * TalkTreeParser::GetTalkTree()
{
	return m_TalkTrees;
}

bool TalkTreeParser::ParseTalkTree(TalkTree *tree, char *data)
{
    XMLNode TalkTreeBase;

    TalkTreeBase = XMLNode::parseString(data);

	InnerTalkTreeParser(tree, &TalkTreeBase);

    return true;
}

void TalkTreeParser::InnerTalkTreeParser(TalkTree *tree, XMLNode *TalkTreeBase)
{
	TalkNode * Current_Node = NULL;
	TalkBranch * Current_Branch = NULL;
	int NumNodes;
	int NumBranches;

    NumNodes = TalkTreeBase->nChildNode("Tree");

	XMLNode TNode;
	XMLNode TBranch;

	tree->NumNodes = NumNodes;
	for (mapNodes::iterator itrCList = tree->Nodes.begin(); itrCList != tree->Nodes.end(); ++itrCList)
	{
		if (itrCList->second)
		{
			for (BranchList::iterator itrBList = itrCList->second->Branches.begin(); itrBList < itrCList->second->Branches.end(); ++itrBList)
				delete *itrBList;
			itrCList->second->Branches.clear();
			delete itrCList->second;
		}
	}
	tree->Nodes.clear();

	// Loop though all nodes
	for(int i=0;i<NumNodes;i++)
	{
		Current_Node = new TalkNode;

		if (Current_Node)
		{
			memset (Current_Node, 0, sizeof(TalkNode));
			TNode = TalkTreeBase->getChildNode("Tree",i);

			// Current Node Data
			Current_Node->Flags = (talk_type)intVal(TNode.getChildNode("Trade").getText());

			if (Current_Node->Flags == 0) 
			{
				Current_Node->Flags = (talk_type)intVal(TNode.getChildNode("Flags").getText());
			}

			if (Current_Node->Flags > 0) 
			{
				Current_Node->Destination = intVal(TNode.getChildNode("Flags").getAttribute("Data"));
			}

			char *sound = (char*)TNode.getAttribute("Sound");
			if (sound)
			{
				Current_Node->Sound_Data = g_StringMgr->GetStr(sound);
			}
	
			Current_Node->NodeNumber = intVal(TNode.getAttribute("Node"));
			Current_Node->Text = g_StringMgr->GetStr(TNode.getChildNode("Text").getText()); //no need to reallocate

			// Save our Current Node
			tree->Nodes[Current_Node->NodeNumber] = Current_Node;

			NumBranches = TNode.nChildNode("Branch");

			Current_Node->NumBranches = NumBranches;

			// Loop though all nodes
			for(int j=0;j<NumBranches;j++)
			{
				Current_Branch = new TalkBranch;
				//memset(Current_Branch,0,sizeof(TalkBranches));

				if (Current_Branch)
				{
					Current_Node->Branches.push_back(Current_Branch);

					TBranch = TNode.getChildNode("Branch",j);
					Current_Branch->BranchDestination = intVal(TBranch.getAttribute("Node"));
					Current_Branch->Text = g_StringMgr->GetStr(TNode.getChildNode("Branch", j).getText());
				}
			}
		}
	}

}

bool TalkTreeParser::ParseMissions(MissionTree *tree, char *data)
{
	TalkNode * Current_Node = (0);
	TalkBranch * Current_Branch = (0);
	_MissionNode *mNode = (0);
	Object *obj;

    XMLNode TalkTreeBase;
	XMLNode m_TalkNode;
	XMLNode TalkBranch;
	XMLNode MissionBranch;
	XMLNode TempBranch;

	int NumRestrictionNodes;
	int NumCompletionNodes;
	int NumRewardNodes;

	MissionBranch = XMLNode::parseString(data);

	for (mapMissionNodes::iterator itrNList = tree->Nodes.begin(); itrNList != tree->Nodes.end(); ++itrNList)
	{
		if (itrNList->second)
		{
			for (CompletionList::iterator itrCList = itrNList->second->completion_list.begin(); itrCList < itrNList->second->completion_list.end(); ++itrCList)
				delete *itrCList;
			itrNList->second->completion_list.clear();
			for (RewardList::iterator itrCList = itrNList->second->rewards.begin(); itrCList < itrNList->second->rewards.end(); ++itrCList)
				delete *itrCList;
			itrNList->second->rewards.clear();
			delete itrNList->second;
		}
	}
	tree->Nodes.clear();
	tree->NumNodes = 0;
	tree->Job_Category = 0;

	TempBranch = MissionBranch.getChildNode("Mission");

	if (intVal(MissionBranch.getAttribute("forfeitable")) > 0)
	{
		tree->forfeitable = true;
	}
	else
	{
		tree->forfeitable = false;
	}
	
	tree->Job_Category = intVal(MissionBranch.getAttribute("jobcategory"));

	if (tree->MissionID == 500)
	{
		Sleep(1);
	}

	if (tree->Job_Category > 0)
	{
		g_ServerMgr->AddJob(tree->Job_Category);
	}

	//find name and summary of mission
	
	char *summary = (char*)MissionBranch.getChildNode("Summary").getText();
	
	LogMessage("Mission %d: %s %s\n", tree->MissionID, tree->name, summary);

	tree->summary = g_StringMgr->GetStr(summary);

	//process mission conditions (if any)
	NumRestrictionNodes = MissionBranch.nChildNode("Condition");

	for (RestrictionList::iterator itrNList = tree->restriction_list.begin(); itrNList < tree->restriction_list.end(); ++itrNList)
		delete *itrNList;
	tree->restriction_list.clear();

	for (int x=0;x<NumRestrictionNodes;x++)
	{
		RestrictionNode *rNode = new RestrictionNode;
		tree->restriction_list.push_back(rNode);
		TalkBranch = MissionBranch.getChildNode("Condition",x);
		rNode->type = (restrict_type)intVal(TalkBranch.getAttribute("ID"));
		rNode->flags = intVal(TalkBranch.getAttribute("Flags"));
		rNode->data = intVal(TalkBranch.getText());
	}

	//now process stages

	tree->NumNodes = MissionBranch.nChildNode("Stage");

	for (int x=0;x<tree->NumNodes;x++)
	{
		_MissionNode *this_node;

		if (tree->Nodes[x] == 0)
		{
			this_node = new _MissionNode;
			tree->Nodes[x] = this_node;
		}
		else
		{
			this_node = tree->Nodes[x];
		}

		//now read talktree (if any)
		TalkBranch = MissionBranch.getChildNode("Stage",x);

		// Get description			
		char *description = (char*)TalkBranch.getChildNode("Description").getText();
		this_node->description = g_StringMgr->GetStr(description);

		InnerTalkTreeParser(&this_node->talk_tree, &TalkBranch);

		NumCompletionNodes = TalkBranch.nChildNode("Completion");
		int mutual = 0;

		for (int y=0;y<NumCompletionNodes;y++)
		{
			CompletionNode *cNode = new CompletionNode;
			memset(cNode, 0, sizeof(cNode));
			this_node->completion_list.push_back(cNode);
			TempBranch = TalkBranch.getChildNode("Completion",y);

			cNode->type = (completion_node_type)intVal(TempBranch.getAttribute("ID"));
			cNode->data = intVal(TempBranch.getText());
			cNode->count = intVal(TempBranch.getAttribute("Count"));
			if (cNode->count == 0) cNode->count = intVal(TempBranch.getAttribute("Data"));
			if (cNode->count == 0) cNode->count = 1;
			char *char_data = (char*)TempBranch.getAttribute("Text");
			if (char_data)
			{
				cNode->char_data = g_StringMgr->GetStr(char_data);
			}

			switch (cNode->type)
			{
			case ARRIVE_AT:
			case NAV_MESSAGE:
			case PROXIMITY_TO_SPACE_NPC:
			case TALK_SPACE_NPC:
			case USE_SKILL_ON_OBJECT:
			case NEAREST_NAV:
			case SCAN_OBJECT:
				obj = g_SectorObjects[cNode->data];
				if (obj) obj->SetUsedInMission();
				break;

			case TALK_NPC:
				{
					if (x == 0) //if this is a stage 0 TALK_NPC this is a mission starter
					{
						g_ServerMgr->m_Missions.SetMissionStartNPC(cNode->data);
					}

					NPCTemplate *mission_npc = g_ServerMgr->m_StationMgr.GetNPC(cNode->data);
					if (!mission_npc)
					{
						LogMessage("[%d] ERROR IN MISSION: NPC [%d] doesn't exist.\n", tree->MissionID, cNode->data);
					}

					//TALK_NPC must be before any of the other completion nodes, so a node is rejected before
					//we can send dialogue to the player about why the stage isn't progressing
					if (y != 0)
					{
						LogMessage("ReOrdered TALK_NPC to first completion node.\n");
						//swap this node with the zero node
						CompletionNode *tempnode = this_node->completion_list[0];
						this_node->completion_list[0] = cNode;
						this_node->completion_list[y] = tempnode;
					}
				}
				break;

			default:
				break;
			}
			// mutual exclusion check
			switch (cNode->type)
			{
				case ARRIVE_AT:
				case FIGHT_MOB:
				case OBTAIN_ITEMS:
				case TALK_NPC:
				case USE_SKILL_ON_MOB_TYPE:
				case USE_SKILL_ON_OBJECT:
				case TALK_SPACE_NPC:       
				case PROXIMITY_TO_SPACE_NPC: 
				case NAV_MESSAGE:
					mutual++;
					break;
			}
		}
		// mutual exclusion result
		if (mutual > 1)
		{
			LogMessage("[%d] ERROR: Mutually exclusive types in stage %d\n",tree->MissionID,x);
		}

		NumRewardNodes = TalkBranch.nChildNode("Reward");

		for (int z=0;z<NumRewardNodes;z++)
		{
			RewardNode *rNode = new RewardNode;
			memset(rNode, 0, sizeof(rNode));
			this_node->rewards.push_back(rNode);
			TempBranch = TalkBranch.getChildNode("Reward",z);

			rNode->type = (reward_type)intVal(TempBranch.getAttribute("ID"));
			if (rNode->type == PLAY_SOUND_REWARD)
			{
				char *char_data = (char*)TempBranch.getText();
				if (char_data)
				{
					rNode->char_data = g_StringMgr->GetStr(char_data);
				}
			}
			else
			{
				rNode->data = intVal(TempBranch.getText());
			}
			rNode->flags = intVal(TempBranch.getAttribute("Flags"));
		}
	}

	return true;
}

#endif