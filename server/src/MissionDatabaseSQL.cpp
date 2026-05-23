// MissionDatabaseSQL.cpp
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

#ifdef USE_MYSQL_SECTOR

#include "MissionDatabaseSQL.h"
#include "mysql/mysqlplus.h"
#include "StringManager.h"
#include "TalkTree.h"


MissionHandler::MissionHandler()
{
    m_highest_id = 0;
    m_Missions.clear();
}

MissionHandler::~MissionHandler()
{
	for (_MissionList::iterator itrAList = m_Missions.begin(); itrAList != m_Missions.end(); ++itrAList) 
		if (itrAList->second)
		{
			for (mapMissionNodes::iterator itrNList = itrAList->second->Nodes.begin(); itrNList != itrAList->second->Nodes.end(); ++itrNList)
			{
				if (itrNList->second)
				{
					for (CompletionList::iterator itrCList = itrNList->second->completion_list.begin(); itrCList < itrNList->second->completion_list.end(); ++itrCList)
						delete *itrCList;
					itrNList->second->completion_list.clear();
					for (RewardList::iterator itrCList = itrNList->second->rewards.begin(); itrCList < itrNList->second->rewards.end(); ++itrCList)
						delete *itrCList;
					itrNList->second->rewards.clear();
					for (mapNodes::iterator itrCList = itrNList->second->talk_tree.Nodes.begin(); itrCList != itrNList->second->talk_tree.Nodes.end(); ++itrCList)
					{
						if (itrCList->second)
						{
							for (BranchList::iterator itrBList = itrCList->second->Branches.begin(); itrBList < itrCList->second->Branches.end(); ++itrBList)
								delete *itrBList;
							itrCList->second->Branches.clear();
							delete itrCList->second;
						}
					}
					itrNList->second->talk_tree.Nodes.clear();
					delete itrNList->second;
				}
			}
			itrAList->second->Nodes.clear();
			for (RestrictionList::iterator itrNList = itrAList->second->restriction_list.begin(); itrNList < itrAList->second->restriction_list.end(); ++itrNList)
				delete *itrNList;
			delete [] itrAList->second->name;
			delete itrAList->second;
		}
    m_Missions.clear();
}

bool MissionHandler::LoadMissionContent()
{
    long current_mission_id;
    MissionTree *current_mission;
	char *current_mission_name;
	char QueryString[1024];
	long MinSecurityLevel;

	if(!g_MySQL_User || !g_MySQL_Pass) 
    {
		LogMessage("You need to set a mysql user/pass in the net7.cfg\n");
		return false;
	}

	sql_connection_c connection( "net7", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
	sql_query_c MissionTable( &connection );
    sql_result_c result;
	sql_result_c *mission_result = &result;

    strcpy_s(QueryString, sizeof(QueryString), "SELECT * FROM `missions`");
	QueryString[sizeof(QueryString)-1] = '\0';

    if ( !MissionTable.execute( QueryString ) )
    {
        LogMessage( "MySQL Login error/Database error: (User: %s Pass: %s)\n", g_MySQL_User, g_MySQL_Pass );
        return false;
    }
    
    MissionTable.store(mission_result);
    
    if (!mission_result->n_rows() || !mission_result->n_fields()) 
    {
        LogMessage("Error loading rows/fields\n");
        return false;
    }
    
    LogMessage("Loading Missions from SQL (%d)\n", (int)mission_result->n_rows());
	m_Missions.clear();
	m_StarterNPCs.clear();
    
	sql_row_c MissionSQLData;
	char *mission_xml;
	for(int x=0;x<mission_result->n_rows();x++)
	{
		mission_result->fetch_row(&MissionSQLData);
        current_mission_id = (long)MissionSQLData["mission_id"];
		mission_xml = (char*)MissionSQLData["mission_XML"];
		current_mission_name = (char*)MissionSQLData["mission_name"];
		MinSecurityLevel = (long)MissionSQLData["mission_minSecurityLevel"];


		current_mission = m_Missions[current_mission_id];

		if (!current_mission)
		{
			current_mission = new MissionTree;
		}
		else
		{
			delete[] (current_mission->name);
		}

		current_mission->MissionID = current_mission_id;

		current_mission->MinSecurityLevel = MinSecurityLevel;

		current_mission->name = new char[strlen(current_mission_name) + 1];

		strcpy_s(current_mission->name, strlen(current_mission_name) + 1, current_mission_name);
		current_mission->name[strlen(current_mission_name)] = '\0';

		m_TalkTreeParser.ParseMissions(current_mission, mission_xml);

        m_Missions[current_mission_id] = current_mission; //add to Missions map

        if (current_mission_id > m_highest_id)
        {
            m_highest_id = current_mission_id;
        }
    }
    return true;
}

MissionTree * MissionHandler::GetMissionTree(long mission_id)
{
    return (m_Missions[mission_id]);
}

long MissionHandler::GetMissionCount()
{
    return (m_highest_id);
}

char * MissionHandler::GetMissionName(long mission_id)
{
	MissionTree *mission = m_Missions[mission_id];
	char *name = (0);

	if (mission)
	{
		name = mission->name;
	}

	return name;
}

char * MissionHandler::GetStageDescription(long mission_id, long stage)
{
	MissionTree *mission = m_Missions[mission_id];
	char *description = (0);

	if (mission && mission->Nodes[stage])
	{
		description = mission->Nodes[stage]->description;
	}

	return description;
}

void MissionHandler::SetMissionStartNPC(long NPC_id)
{
	m_StarterNPCs[NPC_id] = true;
}

bool MissionHandler::GetMissionStartNPC(long NPC_id)
{
	return m_StarterNPCs[NPC_id];
}

#endif