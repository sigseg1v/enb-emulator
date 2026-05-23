// MissionDatabaseSQL.h
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

#ifndef _MISSIONDATABASE_SQL_H_INCLUDED_
#define _MISSIONDATABASE_SQL_H_INCLUDED_

#include <map>
#include <vector>
#include "TalkTreeParser.h"

// forward references
class sql_connection_c;
struct MissionTree;

typedef std::map<unsigned long, MissionTree*> _MissionList;
typedef std::map<unsigned long, bool> _StartNPCs;

class MissionHandler
{
// Constructor/Destructor
public:
    MissionHandler();
	virtual ~MissionHandler();

// Public Methods
public:
    bool        LoadMissionContent();
    MissionTree *   GetMissionTree(long mission_id);
    void        ReloadMissionData(long mission_id);
	long		GetMissionCount();
	char		*	GetMissionName(long mission_id);
	long		GetNextMissionID(long mission_id);
	char		*	GetStageDescription(long mission_id, long stage);
	long		GetHighestID()				{ return m_highest_id; }
	bool		GetMissionStartNPC(long NPC_id);
	void		SetMissionStartNPC(long NPC_id);

	_MissionList*	GetMissionList()		{ return &m_Missions; }

// Private Methods
private:

// Private Member Attributes
private:
    _MissionList	m_Missions;
    long			m_highest_id;
	TalkTreeParser	m_TalkTreeParser;
	_StartNPCs		m_StarterNPCs;

};

#endif // _MISSIONDATABASE_SQL_H_INCLUDED_

