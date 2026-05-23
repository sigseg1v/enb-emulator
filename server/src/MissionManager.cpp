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
#include "XmlTags.h"
#include "MissionManager.h"
#include "ItemList.h"
#include "Opcodes.h"
#include "SectorData.h"
#include "ObjectManager.h"
#include "SectorContentParser.h"
#include "ServerManager.h"

JobManager::JobManager()
{
	memset(m_JobCount, 0, sizeof(m_JobCount));
}

void JobManager::InitialiseJobs()
{

}

long JobManager::GetJobDescription(u8 *ptr, JobNode *jn)
{
	//first get the mission data
	char *mission_description = 0;
	long index = 0;
	MissionTree *mission = g_ServerMgr->m_Missions.GetMissionTree(jn->MissionID);

	if (!mission && mission->Nodes.size() < 2) return 0;

	mission_description = mission->Nodes[1]->description;

	index = ParseJobInfo(ptr, mission_description, jn);

	return index;
}

MOBData * JobManager::GetMOB(long level)
{
	//run through the DB to find a matching MOB
	long mob_level = level/3 + 1;
	MOBData *mob_data = g_ServerMgr->MOBList()->GetMOBforLevel(mob_level);

	return mob_data;
}

long JobManager::ParseJobInfo(u8 *write_buffer, char *mission_description, JobNode *jn)
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

	char *ptr = (char*)write_buffer;
	char *token_ptr;

	long length = 0;
	//scan text for flags
	token_ptr = strchr(mission_description, '@');

	if (token_ptr)
	{
		char *tree_ptr = mission_description;
		while (*tree_ptr != 0)
		{
			if (*tree_ptr == '@')
			{
				token_ptr = tree_ptr+1;
				switch (token_ptr[0])
				{
				case 'a': //a[n] @profession
					/*if (strncmp(token_ptr, "aprofession", 11) == 0)
					{
						strcpy(ptr, aprofession[Profession()]);
						ptr += strlen(aprofession[Profession()]);
						tree_ptr += 11;
					}*/
					break;
				case 'c': //class (== profession)
					/*if (token_ptr[1] == 'l' && token_ptr[2] == 'a' && token_ptr[3] == 's' &&
						token_ptr[4] == 's')
					{
						strcpy(ptr, profession[Profession()]);
						ptr += strlen(profession[Profession()]);
						tree_ptr += 10;
					}*/
					break;
				case 'i':
					if (token_ptr[1] == 't' && token_ptr[2] == 'e' && token_ptr[3] == 'm') //item
					{
						char *item_name = (0);
						ItemBase *itembase = (0);
						if (jn->Item) itembase = g_ItemBaseMgr->GetItem(jn->Item->ItemTemplateID());
						if (itembase) item_name = itembase->Name();
						if (item_name)
						{
							strcpy(ptr, item_name);
							ptr += strlen(item_name);
						}
						else
						{
							strcpy(ptr, "<Mission Error>");
							ptr += strlen("<Mission Error>");
						}
						tree_ptr += 4;
					}
					break;
				case 'm':
					if (token_ptr[1] == 'o' && token_ptr[2] == 'b')  //mob
					{
						if (jn->Mob)
						{
							strcpy(ptr, jn->Mob->m_Name);
							ptr += strlen(jn->Mob->m_Name);
						}
						else
						{
							strcpy(ptr, "Generic Object");
							ptr += strlen("Generic Object");
						}
						tree_ptr += 3;
					}
					break;				
				case 'n': //name
					/*if (token_ptr[1] == 'a' && token_ptr[2] == 'm' && token_ptr[3] == 'e')
					{
						strcpy(ptr, Name());
						ptr += strlen(Name());
						tree_ptr += 4;
					}*/
					break;
				case 'o':
					if (token_ptr[1] == 'b' && token_ptr[2] == 'j' && token_ptr[3] == 'e' &&
						token_ptr[4] == 'c' && token_ptr[5] == 't')  //object
					{
						if (jn->Obj)
						{
							strcpy(ptr, jn->Obj->Name());
							ptr += strlen(jn->Obj->Name());
						}
						else
						{
							strcpy(ptr, "Generic Object");
							ptr += strlen("Generic Object");
						}
						tree_ptr += 6;
					}
					break;
				case 'p':
					/*if (strncmp(token_ptr, "profession", 10) == 0)
					{
						strcpy(ptr, profession[Profession()]);
						ptr += strlen(profession[Profession()]);
						tree_ptr += 10;
					}*/
					break;
				case 'r': //race
					/*if (token_ptr[1] == 'a' && token_ptr[2] == 'c' && token_ptr[3] == 'e')
					{
						strcpy(ptr, race[Race()]);
						ptr += strlen(race[Race()]);
						tree_ptr += 4;
					}*/
					break;
				case 's':
					if (token_ptr[1] == 'e' && token_ptr[2] == 'c' && token_ptr[3] == 't' &&
						token_ptr[4] == 'o' && token_ptr[5] == 'r')  //sector
					{
						char *sector_name = (0);
						if (jn->Obj) sector_name = g_ServerMgr->GetSectorName(jn->Obj->GetSector());
						if (sector_name)
						{
							strcpy(ptr, sector_name);
							ptr += strlen(sector_name);
						}
						else
						{
							strcpy(ptr, "<Mission Error>");
							ptr += strlen("<Mission Error>");
						}
						tree_ptr += 6;
					}
					break;

				default:
					*ptr++ = *tree_ptr;
					break;
				}
			}
			else
			{
				*ptr++ = *tree_ptr;
			}
			tree_ptr++;
		}
	}
	else
	{
		strcpy(ptr, mission_description);
		ptr += strlen(mission_description);
	}

	memset(ptr, 0, 100);

	length = (long) ((u8*)ptr - write_buffer);

	return length;
}

char * JobManager::GetJobTitle(long mission_id)
{
	char *mission_description = 0;
	MissionTree *mission = g_ServerMgr->m_Missions.GetMissionTree(mission_id);

	if (mission)
	{
		mission_description = mission->name;
	}

	return mission_description;
}

MissionTree * JobManager::SelectJob(long selection, long category)
{
	_MissionList *m_list = g_ServerMgr->m_Missions.GetMissionList();
	MissionTree *mTree;
	long mission_sz = g_ServerMgr->m_Missions.GetHighestID();
	long mission_id = 0;
	long index = 0;

	for (mission_id = 0; mission_id <= mission_sz; ++mission_id) 
	{
		mTree = (*m_list)[mission_id];
		if (!mTree) continue;

		if (mTree->Job_Category == category)
		{
			if (selection == index)
			{
				return mTree;
			}
			index++;
		}
	}

	return (0);
}

Object * JobManager::GetJobObject(long level, long sector_id)
{
	return(FindPlanet(level, sector_id));
}

Object *JobManager::FindPlanet(long level, long sector_id)
{
	long hops = 0;
	if (level >= 50) hops++;
	if (level >= 75) hops++;
	if (level >= 100) hops+=2;
	if (level >= 125) hops+=2;
	if (level >= 150) hops+=2;
	if (sector_id > 9999)
	{
		sector_id = sector_id / 10;
	}
	ObjectManager *om = g_ServerMgr->GetObjectManager(sector_id);
	ObjectManager *old_om = om;
	Object *stargate = 0;
	Object *planet = 0;
	long dest = 0;

	if (!om) return 0;

	while (hops > 0 && om)
	{
		//first find a random gate
		stargate = om->FindGate();

		if (stargate)
		{
			//hop through the gate
			dest = stargate->Destination();
			if (dest)
			{
				om = g_ServerMgr->GetObjectManager(dest);
			}
			if (!om)
			{
				om = old_om;
			}
			old_om = om;
		}
		else 
		{
			om = old_om;
		}
		hops--;
		if (hops == 0)
		{
			if (om->FindPlanet() == 0)
			{
				om = g_ServerMgr->GetObjectManager(sector_id);
				hops = 1;
			}
		}
	}

	if (om)
	{
		planet = om->FindPlanet();
		if (planet) planet->SetUsedInMission();
	}

	return planet;
}

#if 0
JobManager::JobManager(void) {}
JobManager::~JobManager(void) {}

bool JobManager::Initialize(void)
{
	jobId = 0; //Initialize the jobId value

	if ( //Load up the various tables needed
		m_JobParser.LoadRoutes() &&
		m_JobParser.LoadRewards() &&
		m_JobParser.LoadDetails() &&
		m_JobParser.LoadSponsors()
		)
		return true;
	else
		return false;
}

// This needs to be re-done to use the normal connection buffer system.
// We don't want an independant comms system.

// Every time a new player uses the terminal, build a new
// job list around their level to add to the global job list
void JobManager::GetJobList(Connection *c, PacketBuffer *buffer)
{
	// for now, fake the player to have 5 levels in each category
	int PlayerLevel[3];
//	PlayerLevel[COMBAT] = c->m_Player->PlayerIndex()->RPGInfo.GetCombatLevel();
//	PlayerLevel[TRADE] = c->m_Player->PlayerIndex()->RPGInfo.GetTradeLevel();
//	PlayerLevel[EXPLORE] = c->m_Player->PlayerIndex()->RPGInfo.GetExploreLevel();

	int JobsInCategory[3];
	// for now, fake 5 jobs in each category
	JobsInCategory[COMBAT] = PLAYER_JOBS_PER_CATEGORY - 0;
	JobsInCategory[TRADE] = PLAYER_JOBS_PER_CATEGORY - 0;
	JobsInCategory[EXPLORE] = PLAYER_JOBS_PER_CATEGORY - 0;

	int jobTotal = JobsInCategory[0]+JobsInCategory[1]+JobsInCategory[2];

	// Lets build a job queue!
	buffer->addLong(jobTotal);

	for (int cat = 0; cat < 3; cat++) // Loop through all 3 categories
	{
		// For each available player job slot, generate a blank job structure
		for (int i = 0; i < JobsInCategory[cat]; i++)
		{
			// Create a new job
			Job	job;

			// Randomly generate a job level based on the player's category level
			job.JobLevel = GetRandomJobLevel(PlayerLevel[cat]);

			// Now we pick a sponsor.
			JobParser::JobSponsor *sponsor = GetRandomSponsor();
			job.Sponsor = sponsor->SponsorName;

			// Get the rewards for the missions (item, cash, faction);
			job.CashReward = job.JobLevel * CASH_MULTIPLIER;
			job.ExperienceReward = job.JobLevel * XP_MULTIPLIER;
			job.FactionReward = 0; // For now, no faction reward

			// Build the rewards string that gets displayed
			JobParser::JobReward *reward = GetRandomReward(PlayerLevel[cat]);
			job.Reward = reward->ItemName + " / " + to_string(job.CashReward) + " CR";
			job.RewardItemId = reward->ItemID;

			// Now the fun part. Get a job!
			JobParser::JobDetail *details = GetRandomJobDetails(cat);
			job.Type = details->Type;
			job.Description = details->Description;
			job.Category = details->Category;

			// Generate a "unique" id for this job
			if (jobId == LONG_MAX) jobId = 0;
			jobId = jobId++;

			job.JobDescriptionId = jobId;

			m_Jobs.push_back(job);

			// add this job to the packet
			buffer->addLong(job.JobDescriptionId);
			buffer->addLong(job.Category);
			buffer->addLong(0); //unknown bits
			buffer->addLong(job.JobLevel);
			buffer->addString(job.Type);
			buffer->addString(job.Sponsor);
			buffer->addString(job.Reward);
		}
	}
}

// This function should be more complexed to make sure a range of missions around
// the player's level are created, but for now we'll do something simple.
long JobManager::GetRandomJobLevel(long playerLevel)
{
	int level = playerLevel + rand()%5 - 2;
	if (level < 1) level = -level + 1; //flip to a positive if we're dealing with n00bs
	return level;
}

JobParser::JobReward *JobManager::GetRandomReward(long playerLevel)
{
	// First determine the level of the item using fancy magic numbers
	// (sorry too lazy to const these just now)
	double table; // itemTable is use for reward[itemTable]
	double levelCalc = (double)playerLevel/(5.6 + (double)(rand()%20)/10.0);
	double faction = modf(levelCalc, &table);
	int itemTable = (int) table;
	if (itemTable >8) itemTable = 8; //As an added check, make sure level is !> 9

	//Then we look at the level reward table and get a random item from it.
	int tableSize = m_JobParser.m_Rewards[itemTable].size();
	int randItem = rand()%tableSize;

	// Return our player's uber loot!
	return &m_JobParser.m_Rewards[itemTable][randItem];
}

// All sorts of fun stuff we can do with this method and
// the sponsors system, but that's for later
JobParser::JobSponsor *JobManager::GetRandomSponsor()
{
	int tableSize = m_JobParser.m_Sponsors.size();
	int randSponsor = rand()%tableSize;
	return &m_JobParser.m_Sponsors[randSponsor];
}

// Grab a random job from the job list based on category
JobParser::JobDetail *JobManager::GetRandomJobDetails(int category)
{
	int tableSize = m_JobParser.m_Details[category].size();
	int randJob = rand()%tableSize;
	return &m_JobParser.m_Details[category][randJob];
}

void JobManager::GetJobDescription(long JobID, PacketBuffer *buffer, TokenParser *tokenParser)
{
	TokenParser::keywords keyWords;
	TokenParser::splitString splitStr;
	string parsedString;

	vector<Job>::iterator i;
	for(i = m_Jobs.begin(); i != m_Jobs.end(); i++)
	{
		if (i->JobDescriptionId == JobID)
		{
			buffer->addLong(i->JobDescriptionId);
			buffer->addByte(0x01);
			buffer->addString(i->Type);

			tokenParser->SplitString(i->Description, splitStr, keyWords);

			//Change requested by Zackmann
			parsedString = ParseTokens(&*i, splitStr, keyWords);

			buffer->addString(parsedString);
		}
	}
}

long JobManager::GetItem(long JobID)
{
	vector<Job>::iterator i;
	for(i = m_Jobs.begin(); i != m_Jobs.end(); i++)
	{
		if (i->JobDescriptionId == JobID)
		{
			return i->RewardItemId;
		}
	}
	return 0;
}

// If statements are just for basic testing, this search needs tobe replace by lookup table
string JobManager::ParseTokens(Job *job, TokenParser::splitString &splitStr, TokenParser::keywords &keyWords)
{
	TokenParser::keywords::iterator i;

	// For speed I've tried to make each keyword start with a unique character.
	for(i = keyWords.begin(); i != keyWords.end(); i++)
	{
		if (i->key.substr(1,1) == "D") // Device
		{
			if (i->key == "{Device}") // Random device item
			{}
			else if (i->key == "{DeviceMob}") // a mob that drops tech device items.
			{}
		}
		else if (i->key.substr(1,1) == "F") // From
		{
			if (i->key == "{FromSector}") // The sector the current job station is in.
			{}
			else if (i->key == "{FromStation}") // The station where the job terminal is in
			{}
			else if (i->key == "{FromNavPoint}") // A NavPoint in the same sector as the job terminals
			{}
			else if (i->key == "{FromStationPerson}") // A person in the "FromStation"
			{}
			else if (i->key == "{FromSectorPerson}") // A person in the "FromSector"
			{}
		}
		else if (i->key.substr(1,2) == "T") //To
		{
			if (i->key == "{ToSector}") // The to sector
			{}
			else if (i->key == "{ToStation}") // A station in the to sectors
			{}
			else if (i->key == "{ToNavPoint}") // A navpoint in the tos sector
			{}
		}
		else if (i->key.substr(1,1) == "S") //Some & Spawn
		{
			if (i->key == "{SpawnItem}")
			{}
		}
	}
	return "";
}
#endif

#if 0
Mission::Mission()
{
    m_MissionNodes.clear();
    m_MissionName = 0;
    m_MissionSummary = 0;
    m_MissionNodeCount = 0;
    m_LevelRequirement = 0;
}

Mission::~Mission()
{
	delete [] m_MissionName;
	delete [] m_MissionSummary;

	for (MissionNodeList::iterator itrNodeList = m_MissionNodes.begin(); itrNodeList < m_MissionNodes.end(); ++itrNodeList)
		if (*itrNodeList)
		{
			for (TalkTreeList::iterator itrTTList = (*itrNodeList)->talk_tree_list->begin(); itrTTList < (*itrNodeList)->talk_tree_list->end(); ++itrTTList) 
				delete [] (*itrTTList)->talk_text;
			delete (*itrNodeList)->object_list;
			delete *itrNodeList;
		}
}

void Mission::SetMissionName(char *name)
{
    if (name)
    {
        int length = strlen(name);
        m_MissionName = new char [length + 1];

        strcpy_s(m_MissionName, length + 1, name);
		m_MissionName[length] = '\0';
    }
}

void Mission::SetMissionSummary(char *summary)
{
    if (summary)
    {
        int length = strlen(summary);
        m_MissionSummary = new char [length + 1];

        strcpy_s(m_MissionSummary, length + 1, summary);
		m_MissionSummary[length] = '\0';
    }
}

void Mission::AddNewNode(MissionNode *node)
{
    if (node)
    {
        m_MissionNodes.push_back(node);
        m_MissionNodeCount++;
    }
}

char * Mission::GetNodeDescription(u32 node)
{
    if (node < m_MissionNodes.size())
    {
        return (m_MissionNodes[node]->description);
    }
    else
    {
        return (0);
    }
}

node_type Mission::GetNodeType(u32 node)
{
    if (node < m_MissionNodes.size())
    {
        return (m_MissionNodes[node]->type);
    }
    else
    {
        return NULL_NODE;
    }
}

char * Mission::GetNPCName(u32 node)
{
    if (node < m_MissionNodes.size())
    {
        return (m_MissionNodes[node]->npc_name);
    }
    else
    {
        return (0);
    }
}

long Mission::GetNodeSectorID(u32 node)
{
    if (node < m_MissionNodes.size())
    {
        return (m_MissionNodes[node]->sector_id);
    }
    else
    {
        return (0);
    }
}

char * Mission::GetNodeLocation(u32 node)
{
    if (node < m_MissionNodes.size())
    {
        return (m_MissionNodes[node]->location);
    }
    else
    {
        return (0);
    }
}

Object * Mission::GetONodeLocation(u32 node)
{
    if (node < m_MissionNodes.size())
    {
        return (m_MissionNodes[node]->obj_location);
    }
    else
    {
        return (0);
    }
}

long Mission::GetObjectIndex(u32 node, Object *obj)
{
    long index = 0;
    ObjectList::iterator itrOList;

    if (node < m_MissionNodes.size())
    {
        for (itrOList = m_MissionNodes[node]->object_list->begin(); itrOList < m_MissionNodes[node]->object_list->end(); ++itrOList) 
        {
            if ((*itrOList) == obj)
            {
                return index;
            } 
            index++;
        }
    }
        
    return (-1);
}

long Mission::GetObjectNameIndex(u32 node, char *name, long complete)
{
    long index = 0;
    ObjNameList::iterator itrOList;

    if (node < m_MissionNodes.size())
    {
        for (itrOList = m_MissionNodes[node]->object_name_list->begin(); itrOList < m_MissionNodes[node]->object_name_list->end(); ++itrOList) 
        {
            if ((*itrOList) == name && !(complete & (1 << index)))
            {
                return index;
            } 
            index++;
        }
    }
        
    return (-1);
}

char * Mission::GetNodeObject(u32 node)
{
    if (node < m_MissionNodes.size())
    {
        return (m_MissionNodes[node]->object_name);
    }
    else
    {
        return (0);
    }
}

long Mission::GetNodeItem(u32 node)
{
    if (node < m_MissionNodes.size())
    {
        return (m_MissionNodes[node]->item_id);
    }
    else
    {
        return (0);
    }
}

short Mission::GetNodeItemQuantity(u32 node)
{
    if (node < m_MissionNodes.size())
    {
        return (m_MissionNodes[node]->item_quantity);
    }
    else
    {
        return (0);
    }
}

char * Mission::GetNodeTalkTree(u32 node, u32 tree_id, short *length, short *next_stage)
{
    if (node < m_MissionNodes.size() && 
        m_MissionNodes[node]->talk_tree_list && 
        tree_id < m_MissionNodes[node]->talk_tree_list->size() &&
        tree_id != 0)
    {
        TalkTreeList talk_list = *m_MissionNodes[node]->talk_tree_list;
        TalkTreeNode *tree_node = talk_list[tree_id];
        //if the text hasn't been built yet, build it now.
        BuildTalkTreeNode(tree_node);

        *length = tree_node->talk_text_len;
        *next_stage = tree_node->tree_dest;
        return (tree_node->talk_text);
    }
    else
    {
        return (0);
    }
}

void Mission::BuildTalkTreeNode(TalkTreeNode *node)
{
    BranchTextList::iterator itrBranchList;
    BranchDesc *desc;
    u16 length = node->talk_text_len;

    if (node->built_text)
    {
        return;
    }

    //here we construct the string to drive the E&B client's conversation tree system
    //it's only done the first time a particular tree is requested

    //find total length
    if (node->branch_desc != (0))
    {
        short branch_count = 0;
        long string_index;
        for (itrBranchList = node->branch_desc->begin(); itrBranchList < node->branch_desc->end(); ++itrBranchList) 
        {
            u16 len;
            desc = (*itrBranchList);
            len = strlen(desc->branch_text);
            len += 5;
            branch_count++;
            length += len;
        }

        //now we have the new string length.
        char *pString = new char [length];
        memset(pString, 0, length);
        memcpy(pString, node->talk_text, node->talk_text_len);
        pString[node->talk_text_len-1] = (char)branch_count; //set branch count
        string_index = node->talk_text_len;

        //now add in each branch
        for (itrBranchList = node->branch_desc->begin(); itrBranchList < node->branch_desc->end(); ++itrBranchList) 
        {
            long len;
            desc = (*itrBranchList);
            len = strlen(desc->branch_text);
            pString[string_index] = (char)desc->branch_dest; //set branch destination
            string_index += 4;
            strcpy_s( (pString + string_index), length - string_index, desc->branch_text);
			(pString + string_index)[length-1] = '\0';
            string_index = string_index + len + 1;
        }

        delete [] node->talk_text;
        node->talk_text = pString;
    }

    node->built_text = true;
    node->talk_text_len = length;
}

short Mission::GetNPCID(u32 node)
{
    if (node < m_MissionNodes.size())
    {
        return (m_MissionNodes[node]->npc_id);
    }
    else
    {
        return (0);
    }
}

bool Mission::GetNodeReward(u32 node)
{
    if (node < m_MissionNodes.size() &&
        m_MissionNodes[node]->reward != 0)
    {
        return (true);
    }
    else
    {
        return (false);
    }
}

void Mission::GetNodeXPReward(u32 node, short *combat, short *explore, short *trade)
{
    if (node < m_MissionNodes.size() &&
        m_MissionNodes[node]->reward != 0)
    {
        *combat = m_MissionNodes[node]->reward->CombatReward;
        *explore = m_MissionNodes[node]->reward->ExploreReward;
        *trade = m_MissionNodes[node]->reward->TradeReward;
    }
    return;
}

long Mission::GetNodeCreditReward(u32 node)
{
    if (node < m_MissionNodes.size() &&
        m_MissionNodes[node]->reward != 0)
    {
        return m_MissionNodes[node]->reward->CreditReward;
    }
    else
    {
        return 0;
    }
}

void Mission::GetNodeFactionReward(u32 node, short *faction, short *faction_reward)
{
    if (node < m_MissionNodes.size() &&
        m_MissionNodes[node]->reward != 0)
    {
        *faction = m_MissionNodes[node]->reward->FactionType;
        *faction_reward = m_MissionNodes[node]->reward->FactionReward;
    }
    return;
}

long Mission::GetNodeItemReward(u32 node)
{
    if (node < m_MissionNodes.size() &&
        m_MissionNodes[node]->reward != 0)
    {
        return m_MissionNodes[node]->reward->ItemRewardID;
    }
    else
    {
        return 0;
    }
}

long Mission::GetNodeHullUpgradeReward(u32 node)
{
    if (node < m_MissionNodes.size() &&
        m_MissionNodes[node]->reward != 0)
    {
        return m_MissionNodes[node]->reward->HullUpgrade;
    }
    else
    {
        return 0;
    }
}

char * Mission::GetNodeRewardDescription(u32 node)
{
    if (node < m_MissionNodes.size() &&
        m_MissionNodes[node]->reward != 0)
    {
        return m_MissionNodes[node]->reward->description;
    }
    else
    {
        return 0;
    }
}

long Mission::GetNodeObjectMask(u32 node)
{
    if (node < m_MissionNodes.size())
    {
        long mask = (1 << m_MissionNodes[node]->object_name_list->size()) - 1;
        return mask;
    }
    else
    {
        return 0;
    }
}

bool Mission::InitialiseNodes()
{
    ObjNameList::iterator itrOList;
    MissionNodeList::iterator itrNodeList;
    MissionNode *node;
    Object *obj;

    //spin through each mission element
    for (itrNodeList = m_MissionNodes.begin(); itrNodeList < m_MissionNodes.end(); ++itrNodeList) 
    {
        node = (*itrNodeList);
        
        if (node)
        {
			node->object_list = NULL;

            //match location to object.
            ObjectManager *node_manager = GetObjectManager(node->sector_id);
            
            //match object text to objects.
            if (node_manager)
            {                
                if (node->location)
                {
                    obj = node_manager->GetObjectFromName(node->location);
                    if (obj)
                    {
                        node->obj_location = obj;
                    }
                    else
                    {
                        //report error in mission
                        LogMessage("No Object in sector %d found to match '%s'\n", node->sector_id, node->location);
                    }
                }
                
                if (node->object_name_list && node->type != FIGHT_MOB)
                {
                    node->object_list = new ObjectList;
                    for (itrOList = node->object_name_list->begin(); itrOList < node->object_name_list->end(); ++itrOList) 
                    {
                        obj = node_manager->GetObjectFromName(*itrOList);
                        
                        if (obj)
                        {
                            node->object_list->push_back(obj);
                        }
                        else
                        {
                            //report error in mission
                            LogMessage("No Object in sector %d found to match '%s'\n", node->sector_id, (*itrOList));
                        }                        
                    }
                }
            }
        }
    }

    return true;
}

ObjectManager *Mission::GetObjectManager(long sector_id)
{
    //SectorData * p = data;
    ObjectManager *manager = (0);
	SectorData *p = g_ServerMgr->m_SectorContent.GetSectorData(sector_id);
	if (p)
	{
		manager = p->obj_manager;
	}

    return manager;
}

MissionManager::MissionManager(void)
{
	m_missionList = NULL;
}

MissionManager::~MissionManager(void)
{
}

bool MissionManager::Initialize(void)
{
	if (m_missionList != NULL)
	{
		m_MissionParser.refreshMissionList();
	}
	else
	{
		if (!m_MissionParser.LoadMissions())
		{
			return false;
		}
	}
	m_missionList = m_MissionParser.GetMissionList();
	return true;
}

MissionParser::MissionDetails *MissionManager::getMission(int id)
{
	MissionParser::MissionList *p = m_missionList;
	while (p != NULL)
	{
		if (p->mission.auxMissionDetails.ID == id)
		{
			return &p->mission;
		}
		p = p->next;
	}
	return NULL;
}

bool MissionManager::givePlayerMission(Connection *c, int missionID)
{
	int slot;

	MissionParser::MissionDetails *missionTemplate = NULL;

	for (slot=0; slot<12; slot++)
	{
		if (c->m_AuxPlayer.m_Missions.Missions[slot].ID == 0)
		{
			break;
		}
	}
	if (slot == 12)
	{
		LogMessage("No more mission slots available");
		return false;
	}
	
	missionTemplate = getMission(missionID);
	if (missionTemplate != NULL)
	{
		printf("Creating mission id %d in mission slot %d\n", missionID, slot);

		//memcpy(&c->m_AuxPlayer.m_Missions.Missions[slot], &missionTemplate->auxMissionDetails, sizeof(Mission));

		// TODO: When timing is in place, this will need to be updated so the current time / expiration time are set
		c->m_AuxPlayer.m_Missions.Flags[(slot + 4) / 8] |= 1<<((slot + 4) % 8);
		c->m_AuxPlayer.SetMission(&missionTemplate->auxMissionDetails, slot);
		c->m_SectorMgr->AddTimedCall(c->m_Player, B_MISSION_CHECKLOCATION, 5000, NULL, c->m_AuxShip.m_GameID);
		return true;
	}
	LogMessage("AddMission: Could not find mission ID %d\n", missionID);
	return false;
}
bool MissionManager::clearMission(Connection *c, int missionID)
{
	bool foundMission = false;
	int index, i;
	for (i=0; i < 12 && c->m_AuxPlayer.m_Missions.Missions[i].ID > 0; i++)
	{
		if (c->m_AuxPlayer.m_Missions.Missions[i].ID == missionID &&
				(c->m_AuxPlayer.m_Missions.Missions[i].IsCompleted ||
				 c->m_AuxPlayer.m_Missions.Missions[i].IsFailed ||
				 c->m_AuxPlayer.m_Missions.Missions[i].IsExpired) &&
				 !foundMission)
		{
			foundMission = true;
			index = i;

			printf("Clearing mission id %d in mission i %d\n", missionID, i);
		}
		else if (foundMission)
		{
			// TODO : Send this in one packet, instead of multiple packets.
			// Also figure out how to prevent the client from announcing
			// new missions for each moved mission
			c->m_AuxPlayer.m_Missions.Missions[i].HasGivenNewMissionMessage = true;
			c->m_AuxPlayer.SetMission(&c->m_AuxPlayer.m_Missions.Missions[i], i-1);
		}
	}
	if (foundMission)
	{
		c->m_AuxPlayer.m_Missions.Missions[i-1].Flags[0] = (char)0x22;
		c->m_AuxPlayer.m_Missions.Missions[i-1].Flags[1] = (char)0x00;	// Send failed
		c->m_AuxPlayer.m_Missions.Missions[i-1].Flags[2] = (char)0x00;
		c->m_AuxPlayer.m_Missions.Missions[i-1].Name[0] = '\0';			// 0-length name = clear
		c->m_AuxPlayer.SetMission(&c->m_AuxPlayer.m_Missions.Missions[i-1], i-1);

		char mask = ~(1<<((i + 3) % 8));
		c->m_AuxPlayer.m_Missions.Flags[(i + 3) / 8] &= mask;
		c->m_AuxPlayer.m_Missions.Missions[i-1].ID = 0;
	}
	return foundMission;
}
bool MissionManager::forfeitMission(Connection *c, int missionID)
{
	char savedFlags[3];
	MissionParser::MissionDetails *details = getMission(missionID);

	if (details == NULL)
	{
		return false;
	}

	for (int i=0; i < 12; i++)
	{
		if (c->m_AuxPlayer.m_Missions.Missions[i].ID == missionID)
		{
			if (!details->auxMissionDetails.IsForfeitable)
			{
				return false;
			}

			c->m_AuxPlayer.m_Missions.Missions[i].IsFailed = true;
			
			memcpy(savedFlags, c->m_AuxPlayer.m_Missions.Missions[i].Flags, sizeof(savedFlags));
			c->m_AuxPlayer.m_Missions.Missions[i].Flags[0] = (char)0x02;
			c->m_AuxPlayer.m_Missions.Missions[i].Flags[1] = (char)0x80;	// Send failed
			c->m_AuxPlayer.m_Missions.Missions[i].Flags[2] = (char)0x00;
			c->m_AuxPlayer.SetMission(&c->m_AuxPlayer.m_Missions.Missions[i], i);
			memcpy(c->m_AuxPlayer.m_Missions.Missions[i].Flags, savedFlags, sizeof(c->m_AuxPlayer.m_Missions.Missions[i].Flags));
			printf("Forfeiting mission id %d in mission slot %d\n", missionID, i);
			
			c->m_AuxPlayer.AddCredits(details->ForfeitConsequences.CreditReward);
			c->AddCombatXP("Mission:", details->ForfeitConsequences.CombatReward);
			c->AddExploreXP("Mission:", details->ForfeitConsequences.ExploreReward);
			c->AddTradeXP("Mission:", details->ForfeitConsequences.TradeReward);


			return true;
		}
	}
	return false;
}

bool MissionManager::EvaluateConditions(Connection *c, MissionParser::MissionTriggerList *trigger)
{
	int overallLevel = c->m_AuxPlayer.m_RPGInfo.ExploreLevel + c->m_AuxPlayer.m_RPGInfo.CombatLevel + c->m_AuxPlayer.m_RPGInfo.TradeLevel;
	if ((trigger->conditions.conditionType & TRIGGER_CONDITION_LEVEL) != 0 )
	{
		// Make sure their level is in range for this trigger
		if (c->m_AuxPlayer.m_RPGInfo.ExploreLevel < trigger->conditions.level.minExploreLevel ||
			c->m_AuxPlayer.m_RPGInfo.CombatLevel < trigger->conditions.level.minCombatLevel ||
			c->m_AuxPlayer.m_RPGInfo.TradeLevel < trigger->conditions.level.minTradeLevel ||
			overallLevel < trigger->conditions.level.minOverallLevel ||
			c->m_AuxPlayer.m_RPGInfo.ExploreLevel > trigger->conditions.level.maxExploreLevel ||
			c->m_AuxPlayer.m_RPGInfo.CombatLevel > trigger->conditions.level.maxCombatLevel ||
			c->m_AuxPlayer.m_RPGInfo.TradeLevel > trigger->conditions.level.maxTradeLevel ||
			overallLevel > trigger->conditions.level.maxOverallLevel
			)
		{
			return false;
		}
	}
	return true;
}

bool MissionManager::EvaluateActions(Connection *c, MissionParser::MissionTriggerList *trigger, int missionNumber, Mission &playerMission, MissionParser::MissionDetails *details)
{
	char savedFlags[3];
	char savedFlags2[3];

	// This method is ugly and needs to be split up.  I'll do that later.  -Unimatrix

	doMissionRewards(c, trigger);

	if ((trigger->actions.actionType & TRIGGERACTION_TALKTREE) != 0)
	{
		c->m_IsInMissionConversation = true;
        c->SendResponse(ENB_OPCODE_0054_TALK_TREE, (unsigned char *) trigger->actions.talkTree.Message, trigger->actions.talkTree.MessageLength-1);
	}
	if ((trigger->actions.actionType & TRIGGERACTION_NEXTSTAGE) != 0)
	{
		if (playerMission.Stage == playerMission.StageCount)
		{
			playerMission.IsCompleted = true;

			memcpy(savedFlags, playerMission.Flags, sizeof(savedFlags));
			playerMission.Flags[0] = (char)0x02;
			playerMission.Flags[1] = (char)0x40;	// Send completed
			playerMission.Flags[2] = (char)0x00;
			c->m_AuxPlayer.SetMission(&playerMission, missionNumber);
			memcpy(playerMission.Flags, savedFlags, sizeof(playerMission.Flags));
		}
		else
		{
			playerMission.Stage++;

			memcpy(savedFlags, playerMission.Flags, sizeof(savedFlags));
			playerMission.Flags[0] = (char)0x02;
			playerMission.Flags[1] = (char)0x00;	// Send completed
			playerMission.Flags[2] = (char)0x18;

			int flagIndex = (playerMission.Stage + 3) / 8;
			int shift = (playerMission.Stage + 3) % 8;

			memcpy(savedFlags2, playerMission.StageFlags, sizeof(savedFlags2));
			playerMission.StageFlags[0] = 0x02;
			playerMission.StageFlags[flagIndex] |= (1<<shift);

			c->m_AuxPlayer.SetMission(&playerMission, missionNumber);
			memcpy(playerMission.StageFlags, savedFlags2, sizeof(playerMission.StageFlags));
			memcpy(playerMission.Flags, savedFlags, sizeof(playerMission.Flags));
			// Keep the stage for when they logout / login
			playerMission.StageFlags[flagIndex] |= (1<<shift);
		}
    }
	if ((trigger->actions.actionType & TRIGGERACTION_FAIL) != 0)
	{
		playerMission.IsFailed = true;
			
		memcpy(savedFlags, playerMission.Flags, sizeof(savedFlags));
		playerMission.Flags[0] = (char)0x02;
		playerMission.Flags[1] = (char)0x80;	// Send failed
		playerMission.Flags[2] = (char)0x00;
		c->m_AuxPlayer.SetMission(&playerMission, missionNumber);
		memcpy(playerMission.Flags, savedFlags, sizeof(playerMission.Flags));
    }
	if ((trigger->actions.actionType & TRIGGERACTION_EXPIRE) != 0)
	{
		playerMission.IsExpired = true;
			
		memcpy(savedFlags, playerMission.Flags, sizeof(savedFlags));
		playerMission.Flags[0] = (char)0x02;
		playerMission.Flags[1] = (char)0x00;	// Send expired
		playerMission.Flags[2] = (char)0x01;
		c->m_AuxPlayer.SetMission(&playerMission, missionNumber);
		memcpy(playerMission.Flags, savedFlags, sizeof(playerMission.Flags));
    }
	return true;
}

bool MissionManager::interceptTalkTree(Connection *c)
{
	int x;
	MissionParser::MissionDetails *details;

	for (x=0; x<12; x++)
	{
		if (c->m_AuxPlayer.m_Missions.Missions[x].ID > 0 &&
			!c->m_AuxPlayer.m_Missions.Missions[x].IsCompleted &&
			!c->m_AuxPlayer.m_Missions.Missions[x].IsFailed &&
			!c->m_AuxPlayer.m_Missions.Missions[x].IsExpired)
		{
			details = getMission(c->m_AuxPlayer.m_Missions.Missions[x].ID);
			if (details == NULL)
			{
				LogMessage("InterceptTalkTree: Could not find MissionID %d\n", c->m_AuxPlayer.m_Missions.Missions[x].ID);
				return false;
			}
			
			MissionParser::MissionTriggerList *trigger = details->Stages[c->m_AuxPlayer.m_Missions.Missions[x].Stage-1].triggers;
			while (trigger != NULL)
			{
				if ((trigger->conditions.conditionType & TRIGGER_CONDITION_TALKTREE) != 0 &&
					(trigger->actions.actionType & TRIGGERACTION_TALKTREE) != 0 &&
					trigger->conditions.talkTree.SelectionID == -1 &&
					c->m_SectorID == trigger->conditions.talkTree.SectorID &&
					c->m_StarbaseTargetID == trigger->conditions.talkTree.StarbaseID)
				{
					if (EvaluateConditions(c, trigger))
					{
						EvaluateActions(c, trigger, x, c->m_AuxPlayer.m_Missions.Missions[x], details);
						return true;
					}
				}
				trigger = trigger->next;
			}
		}
	}
	return false;
}

bool MissionManager::interceptTalkTreeAction(Connection *c, int Selection)
{
	int x;
	MissionParser::MissionDetails *details;

	if (c->m_IsInMissionConversation &&
		(Selection == 0 || Selection == 230))
	{
		c->m_IsInMissionConversation = false;
		c->SendTalkTreeAction(-32);
		return true;
	}
	else if (!c->m_IsInMissionConversation)
	{
		return false;
	}

	for (x=0; x<12; x++)
	{
		// Check for active mission slot
		if (c->m_AuxPlayer.m_Missions.Missions[x].ID > 0 &&
			!c->m_AuxPlayer.m_Missions.Missions[x].IsCompleted &&
			!c->m_AuxPlayer.m_Missions.Missions[x].IsFailed &&
			!c->m_AuxPlayer.m_Missions.Missions[x].IsExpired )
		{
			details = getMission(c->m_AuxPlayer.m_Missions.Missions[x].ID);
			if (details == NULL)
			{
				LogMessage("InterceptTalkTree: Could not find MissionID %d\n", c->m_AuxPlayer.m_Missions.Missions[x].ID);
				return false;
			}
			MissionParser::MissionTriggerList *trigger = details->Stages[c->m_AuxPlayer.m_Missions.Missions[x].Stage-1].triggers;
			while (trigger != NULL)
			{
				if ((trigger->conditions.conditionType & TRIGGER_CONDITION_TALKTREE) != 0 &&
					(trigger->actions.actionType & TRIGGERACTION_TALKTREE) != 0 &&
					c->m_SectorID == trigger->conditions.talkTree.SectorID)
				{
					if (EvaluateConditions(c, trigger))
					{
						if (trigger->conditions.talkTree.SelectionID != -1 &&
							trigger->conditions.talkTree.SelectionID == Selection)
						{
							EvaluateActions(c, trigger, x, c->m_AuxPlayer.m_Missions.Missions[x], details);
							return true;
						}
					}
				}
				trigger = trigger->next;
			}
		}
	}
	return false;
}

bool MissionManager::checkPlayerLocation(Connection *c, float position[3])
{
	bool playerHasLocationCondition = false;
	for (int x=0; x<12 && c->m_AuxPlayer.m_Missions.Missions[x].ID > 0; x++)
	{
		if (!c->m_AuxPlayer.m_Missions.Missions[x].IsCompleted &&
			!c->m_AuxPlayer.m_Missions.Missions[x].IsFailed &&
			!c->m_AuxPlayer.m_Missions.Missions[x].IsExpired &&
			!c->m_IsInMissionConversation)
		{
			MissionParser::MissionDetails *details = getMission(c->m_AuxPlayer.m_Missions.Missions[x].ID);
			if (details != NULL)
			{
				MissionParser::MissionTriggerList *trigger = details->Stages[c->m_AuxPlayer.m_Missions.Missions[x].Stage-1].triggers;
				while (trigger != NULL)
				{
					if ((trigger->conditions.conditionType & TRIGGER_CONDITION_LOCATION) != 0 &&
						trigger->conditions.location.SectorID == c->m_SectorID)
					{
						playerHasLocationCondition = true;

						// x^2 + y^2 + z^2 = r^2
						float range = trigger->conditions.location.range * trigger->conditions.location.range;
						float pos = (trigger->conditions.location.x - position[0]) * (trigger->conditions.location.x - position[0]) +
										(trigger->conditions.location.y - position[1]) * (trigger->conditions.location.y - position[1]) +
										(trigger->conditions.location.z - position[2]) * (trigger->conditions.location.z - position[2]);
						if (pos < range &&	EvaluateConditions(c, trigger))
						{
							EvaluateActions(c, trigger, x, c->m_AuxPlayer.m_Missions.Missions[x], details);
						}
					}
					trigger = trigger->next;
				}
			}
		}
	}
	return playerHasLocationCondition;
}

bool MissionManager::doMissionRewards(Connection *c, MissionParser::MissionTriggerList *trigger)
{
	if ( trigger->Rewards.CreditReward != 0 )
	{
		c->m_AuxPlayer.AddCredits(trigger->Rewards.CreditReward);
	}

	if ( trigger->Rewards.CombatReward != 0 )
	{
		c->AddCombatXP("Mission:", trigger->Rewards.CombatReward);
	}

	if ( trigger->Rewards.ExploreReward != 0 )
	{
		c->AddExploreXP("Mission:", trigger->Rewards.ExploreReward);
	}

	if ( trigger->Rewards.TradeReward != 0 )
	{
		c->AddTradeXP("Mission:", trigger->Rewards.TradeReward);
	}
	// TODO: Faction, items
	return true;
}
#endif