// MOBSpawnClass.h
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

#ifndef _MOBSPAWN_CLASS_H_INCLUDED_
#define _MOBSPAWN_CLASS_H_INCLUDED_

#include "SectorData.h"
#include "ObjectClass.h"
#include <vector>

class MOBSpawn : public Object
{
public:
    MOBSpawn(long object_id);
    virtual ~MOBSpawn();
    float   SpawnRadius()                   { return (m_SpawnRadius); };
    void    SetSpawnRadius(float radius)    { m_SpawnRadius = radius; };
    void    SetSpawnCount(short count)      { m_SpawnCount = count; };
    short   SpawnCount()                    { return (m_SpawnCount); };
    void    SetBehaviour(short type)        { m_SpawnBehaviour = type; };
    void    SetGroupAttack(short attack)    { m_GroupAttack = (attack != 0) ? true : false; };
    void    PopulateSpawn(bool live = true);
    void    AddMOBID(long MOBID);
	void	SetRespawnTime(unsigned long respawn_time)	{ m_Respawn_time = respawn_time;};

	void	AddGroupHate(long GameID, long Hate);

    long    UpdateSpawn(u32 current_tick, bool handle_attacks);
	long	GetFirstMOBID()					{ return m_FirstMOBID; };

private:
	unsigned long	m_Respawn_tick;
	unsigned long	m_Respawn_time;
    float           m_SpawnRadius;
    short           m_SpawnCount;
    short           m_SpawnBehaviour;//      Remove once AI scripts are working
    bool            m_GroupAttack;   //      "           "
    long            m_FirstMOBID;
    MOBIDList       m_MOBIDs;
};

#endif // _MOBSPAWN_CLASS_H_INCLUDED_
