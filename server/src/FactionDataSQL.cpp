// FactionDataSQL.cpp
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

#include "Net7.h"

#ifdef USE_MYSQL_SECTOR

#include "FactionDataSQL.h"
#include "mysql/mysqlplus.h"
#include "StringManager.h"


Factions::Factions()
{
    m_factions.clear();
}

Factions::~Factions()
{
	for (FactionList::iterator itrAList = m_factions.begin(); itrAList != m_factions.end(); ++itrAList) 
		delete itrAList->second;
    m_factions.clear();
}

bool Factions::LoadFactions()
{
    long current_faction_id;
    FactionData *current_faction;
	char QueryString[1024];
	long faction_order = 0;

	m_faction_count = 0;

	if(!g_MySQL_User || !g_MySQL_Pass) 
    {
		LogMessage("You need to set a mysql user/pass in the net7.cfg\n");
		return false;
	}

	sql_connection_c connection( "net7", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
	sql_query_c FactionTable( &connection );
    sql_result_c result;
	sql_result_c *faction_result = &result;
	sql_result_c matrix;

    strcpy_s(QueryString, sizeof(QueryString), "SELECT * FROM `factions`");
	QueryString[sizeof(QueryString)-1] = '\0';

    if ( !FactionTable.execute( QueryString ) )
    {
        LogMessage( "MySQL Login error/Database error: (User: %s Pass: %s)\n", g_MySQL_User, g_MySQL_Pass );
        return false;
    }
    
    FactionTable.store(faction_result);
    
    if (!faction_result->n_rows() || !faction_result->n_fields()) 
    {
        LogMessage("Error loading rows/fields\n");
        return false;
    }
    
    LogMessage("Loading Factions from SQL (%d)\n", (int)faction_result->n_rows());
    
	sql_row_c FactionSQLData;
	for(int x=0;x<faction_result->n_rows();x++)
	{
		faction_result->fetch_row(&FactionSQLData);
        current_faction_id = (long)FactionSQLData["faction_id"];
		current_faction = m_factions[current_faction_id];
		if (!current_faction)
		{
			current_faction = new FactionData;
			m_factions[current_faction_id] = current_faction;
		}

		m_faction_count++;

		current_faction->m_name = g_StringMgr->GetStr((char*)FactionSQLData["name"]);
		current_faction->m_description = 0; //Not sure we need to load this. If we do: g_StringMgr->GetStr((char*)FactionSQLData["description"]);
		current_faction->m_player_PDA = (int)FactionSQLData["player_PDA"] == 1 ? true : false;
		if (current_faction->m_player_PDA)
		{
			current_faction->m_PDA_text = g_StringMgr->GetStr((char*)FactionSQLData["PDA_text"]);
			current_faction->m_faction_order = faction_order;
			faction_order++;
		}
		else
		{
			current_faction->m_PDA_text = 0;
			current_faction->m_faction_order = -1;
		}

		current_faction->m_Faction_gain_sfx = g_StringMgr->GetStr((char*)FactionSQLData["faction_gain_sound"]);
		
		//OK now load in the faction relationships for this faction
		sql_query_c Faction_matrix( &connection );
        sql_result_c *matrix_result = &matrix;

		Faction_matrix.AddParam((long)current_faction_id);
		if ( !Faction_matrix.execute_params(
				"SELECT * FROM `faction_matrix` WHERE faction_id = ?" ) )
		{
			printf( "MySQL Error (Faction Matrix)\n" );
			return 0;
		}

		Faction_matrix.store(matrix_result);		

		sql_row_c MatrixSQLData;
		for(int y=0;y<matrix_result->n_rows();y++)
		{
			matrix_result->fetch_row(&MatrixSQLData);	// Read in row
			float faction_value = (float)MatrixSQLData["base_value"];
			float reward_faction = (float)MatrixSQLData["reward_faction"];
			long faction_entry_id = (int)MatrixSQLData["faction_entry_id"];

			current_faction->m_reaction[faction_entry_id] = reward_faction;
			current_faction->m_value[faction_entry_id] = faction_value;
		}

		//set default standing with your own faction to 7500.0
		//current_faction->m_value[current_faction_id] = 7500.0f;
	}

    return true;
}

float Factions::GetFactionStanding(long faction_id, long faction_ref)
{
	if (m_factions[faction_id])
	{
		return m_factions[faction_id]->m_value[faction_ref];
	}
	else
	{
		return 0.0f;
	}
}

char* Factions::GetFactionName(long faction_id)
{
	if (m_factions[faction_id])
	{
		return m_factions[faction_id]->m_name;
	}
	else
	{
		return 0;
	}
}

char* Factions::GetFactionPDAName(long faction_id)
{
	if (m_factions[faction_id])
	{
		return m_factions[faction_id]->m_PDA_text;
	}
	else
	{
		return 0;
	}
}

bool Factions::GetPDA(long faction_id)
{
	if (m_factions[faction_id])
	{
		return m_factions[faction_id]->m_player_PDA;
	}
	else
	{
		return false;
	}
}

long Factions::GetFactionOrderFromID(long faction_id)
{
	if (m_factions[faction_id])
	{
		return m_factions[faction_id]->m_faction_order;
	}
	else
	{
		return -1;
	}
}

FactionData * Factions::GetFactionData(long faction_id)
{
    return (m_factions[faction_id]);
}

long Factions::GetFactionCount()
{
    return (m_faction_count);
}

#endif