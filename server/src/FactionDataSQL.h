// FactionDataSQL.h
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

#ifndef _FACTIONDATABASE_SQL_H_INCLUDED_
#define _FACTIONDATABASE_SQL_H_INCLUDED_

#include <map>

// forward references
class sql_connection_c;
struct FactionData;

typedef std::map<unsigned long, FactionData*> FactionList;
typedef std::map<unsigned long, float> FactionValues;

class Factions
{
// Constructor/Destructor
public:
    Factions();
	virtual ~Factions();

// Public Methods
public:
    bool			LoadFactions();
    FactionData *   GetFactionData(long faction_id);
    void			ReloadFactionData();

	float			GetFactionStanding(long faction_id, long faction_ref);
	char *			GetFactionName(long faction_id);
	char *			GetFactionPDAName(long faction_id);
	long			GetFactionCount();
	bool			GetPDA(long fection_id);
	long			GetFactionOrderFromID(long faction_id);

// Private Methods
private:

// Private Member Attributes
private:
    FactionList		m_factions;
	long			m_faction_count;
};

struct FactionData
{
	char	*m_name;
	char	*m_description;
	char	*m_PDA_text;
	char	*m_Faction_gain_sfx;
	long	m_faction_order;
	bool	m_player_PDA;
	FactionValues m_value;
	FactionValues m_reaction;
};


#endif // _FACTIONDATABASE_SQL_H_INCLUDED_

