// BuffDatabaseSQL.cpp
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

#include "BuffDatabaseSQL.h"
#include "mysql/mysqlplus.h"
#include "StringManager.h"


BuffContent::BuffContent()
{
	m_BuffData.clear();
	m_updating = false;
}

BuffContent::~BuffContent()
{
    m_BuffData.clear();
}

bool BuffContent::LoadBuffContent()
{
	char QueryString[1024];

	if (m_updating) return false;

	m_updating = true;

	if(!g_MySQL_User || !g_MySQL_Pass) 
    {
		LogMessage("You need to set a mysql user/pass in the net7.cfg\n");
		return false;
	}

	sql_connection_c connection( "net7", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
	sql_query_c BuffTable( &connection );
    sql_result_c result;
	sql_result_c *buff_result = &result;

    strcpy_s(QueryString, sizeof(QueryString), "SELECT * FROM `buffs`");
	QueryString[sizeof(QueryString)-1] = '\0';

    if ( !BuffTable.execute( QueryString ) )
    {
        LogMessage( "MySQL Login error/Database error: (User: %s Pass: %s)\n", g_MySQL_User, g_MySQL_Pass );
        return false;
    }
    
    BuffTable.store(buff_result);
    
    if (!buff_result->n_rows() || !buff_result->n_fields()) 
    {
        LogMessage("Error loading rows/fields\n");
        return false;
    }

	LogMessage("Loading Buff Data from SQL (%d)\n",buff_result->n_rows());
   
	sql_row_c BuffSQLData;
	for(int x=0;x<buff_result->n_rows();x++)
	{
		struct BuffData current_buff;
		memset(&current_buff, 0, sizeof(BuffData));

		buff_result->fetch_row(&BuffSQLData);
		char * name = g_StringMgr->GetStr((char *)BuffSQLData["buff_name"]);
		m_BuffData[name].EffectID = BuffSQLData["EffectID"];
		m_BuffData[name].EffectLength = BuffSQLData["EffectLength"];
    }

	m_updating = false;

    return true;
}

long BuffContent::GetBuffEffect(char *buff)
{
	if (m_BuffData.count(buff) > 0)
	{
		return m_BuffData[buff].EffectID;
	}

	return -1;
}

long BuffContent::GetBuffEffectTime(char *buff)
{
	if (m_BuffData.count(buff) > 0)
	{
		return m_BuffData[buff].EffectLength;
	}

	return -1;
}

#endif