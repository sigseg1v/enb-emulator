// MOBDatabaseSQL.cpp
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

#include "SkillsDatabase.h"
#include "mysql/mysqlplus.h"
#include "StringManager.h"


SkillsContent::SkillsContent()
{
    m_highest_id = 0;
    m_SkillConvList.clear();
	m_updating = false;
}

SkillsContent::~SkillsContent()
{
	for (SkillConversionList::iterator itrAList = m_SkillConvList.begin(); itrAList != m_SkillConvList.end(); ++itrAList) 
		delete itrAList->second;
    m_SkillConvList.clear();
}

bool SkillsContent::LoadSkillsContent()
{
    long current_skill_id;
    SkillConversion *current_skill;

	if (m_updating) return false;

	m_updating = true;

	if(!g_MySQL_User || !g_MySQL_Pass) 
    {
		LogMessage("You need to set a mysql user/pass in the net7.cfg\n");
		return false;
	}

	sql_connection_c connection( "net7", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
	sql_query_c SkillsTable( &connection );
    sql_result_c result;
	sql_result_c *skill_result = &result;

    if ( !SkillsTable.execute_params( "SELECT * FROM `skill_levels`" ) )
    {
        LogMessage( "MySQL Login error/Database error: (User: %s Pass: %s)\n", g_MySQL_User, g_MySQL_Pass );
        return false;
    }
    
    SkillsTable.store(skill_result);
    
    if (!skill_result->n_rows() || !skill_result->n_fields()) 
    {
        LogMessage("Error loading rows/fields\n");
        return false;
    }
    
    LogMessage("Loading Skill Levels from SQL (%d)\n", (int)skill_result->n_rows());
    
	sql_row_c SkillSQLData;
	for(int x=0;x<skill_result->n_rows();x++)
	{
		skill_result->fetch_row(&SkillSQLData);
        current_skill_id = (long)SkillSQLData["skill_level_id"];
		current_skill = m_SkillConvList[current_skill_id];
		if (!current_skill)
		{
			current_skill = new SkillConversion;
		}

        current_skill->m_Description = g_StringMgr->GetStr((char*)SkillSQLData["description"]);
        current_skill->m_Level = (int)SkillSQLData["level"];
        current_skill->m_BaseSkillID = (int)SkillSQLData["skill_id"];

        m_SkillConvList[current_skill_id] = current_skill; //add to MOB map

        if (current_skill_id > m_highest_id)
        {
            m_highest_id = current_skill_id;
        }
    }

	m_updating = false;

    return true;
}

long SkillsContent::GetSkillLevel(long skill_id)
{
	if (m_SkillConvList[skill_id])
	{
		return m_SkillConvList[skill_id]->m_Level;
	}
	else
	{
		return 0;
	}
}

long SkillsContent::GetBaseSkillID(long skill_id)
{
	if (skill_id != -1 && m_SkillConvList[skill_id])
	{
		return m_SkillConvList[skill_id]->m_BaseSkillID;
	}
	else
	{
		return 0;
	}
}

char* SkillsContent::GetSkillDescription(long skill_id)
{
	if (m_SkillConvList[skill_id])
	{
		return m_SkillConvList[skill_id]->m_Description;
	}
	else
	{
		return 0;
	}
}

#endif