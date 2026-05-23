// Memory.h
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

#ifndef _MEMORY_H_INCLUDED_
#define _MEMORY_H_INCLUDED_

#define BROADCAST_SLOTS 1000 //per sector
#define NODE_EXTEND_SIZE 200

#include "Mutex.h"
#include "Timenode.h"
#include <vector>

class Player;

template <class Tnode> //universal memory node system
class MemorySlot
{
public:
    MemorySlot(int nodes);
    ~MemorySlot();

    Tnode * GetNode();
	Tnode * GetInactiveNode();
    void    ReleaseNode(Tnode *node);
	void	ReleaseDuplicateNodes(Tnode *node);
	bool    ContainsNode(Tnode *query);

private:
    long    m_Index;
    long    m_Nodes;
    Tnode * m_NodeSpace;
    bool    m_Resizable;

    Mutex * m_Mutex;
};

// Sector memory handler - there is one of these per sector

typedef std::vector<TimeNode*> TimeNodeList;

class MemoryHandler
{
//////////////////////////////
//  Constructor/Destructor  //
//////////////////////////////
public:
    MemoryHandler(long nodes);
    ~MemoryHandler();

//////////////////////
//  Public Methods  //
//////////////////////
public:
	TimeNode	* GetBroadcastNodeSlot();

private:
	void		  ExtendBroadcastSlots(); //this adds another 30 time slots to the sector's timeslot list

	long		  m_CircularIndex;
	long		  m_NodeSize;
	TimeNodeList  m_BroadcastQueueNodes;

	Mutex		  m_NodeMutex;
};

// Global Memory Handler - there is one of these per server

class GMemoryHandler
{
//////////////////////////////
//  Constructor/Destructor  //
//////////////////////////////
public:
    GMemoryHandler(long player_nodes);
    ~GMemoryHandler();

//////////////////////
//  Public Methods  //
//////////////////////
public:
    long          GetPlayerCount();
    void          CheckPlayerNodeThread();
	TimeNode	* GetBroadcastNodeSlot();

    Player      * GetPlayerNode(char *account_name = (0));
    void          ReleasePlayerNode(Player *player);
    Player      * GetPlayerA(long avatar_id, bool sector_login = false); //DO NOT USE THIS TO LOOK UP PLAYERS DIRECTLY - IT WILL FAIL. USE g_PlayerMgr->GetPlayer(game_id)
	Player		* _GetPlayerNumber(long node_index); //used only for range checking, do not use to look up players

    struct MParam
    {
	    GMemoryHandler * ClassAdd;
    } ATTRIB_PACKED;

private:
    long          m_PlayerBufferSize;
    long          m_PlayerCount;
    long          m_PlayerCircularIndex;
	MemoryHandler *m_Timeslots;

    Player      * m_PlayerQueueBuf;

    Mutex         m_PlayerMutex;
};

//Code for Reusable Memory slot template - needs to be here so auto-constructors in ObjectManager have visibility
template<class Tnode> 
MemorySlot<Tnode>::MemorySlot(int nodes)
{
	m_NodeSpace = new Tnode[nodes];
    if (m_NodeSpace)
    {
        m_Index = 0;
        m_Nodes = nodes;
    }
    else
    {
		LogMessage("FATAL ERROR: Unable to initialise memory.\n");
	}
}

template<class Tnode> 
Tnode * MemorySlot<Tnode>::GetNode()
{
	Tnode *slot;
    long start_slot = m_Index;

    if (!m_NodeSpace) //memory not valid
    {
        return (0);
    }
    
	slot = &m_NodeSpace[m_Index];

	m_Index++;

	if (m_Index == m_Nodes)
	{
        m_Index = 0;
	}

	return slot;
}

template<class Tnode> 
Tnode * MemorySlot<Tnode>::GetInactiveNode()
{
	Tnode *slot;
	long spincount = 0;

    if (!m_NodeSpace) //memory not valid
    {
        return (0);
    }

	while (m_NodeSpace[m_Index].GameID() != 0)
	{
		m_Index++;
		if (m_Index==m_Nodes)
		{
			m_Index = 0;
            spincount++;
            if (spincount > 2) return (0);
		}
	}
    
	slot = &m_NodeSpace[m_Index];

	m_Index++;

	if (m_Index == m_Nodes)
	{
        m_Index = 0;
	}

	return slot;
}

template<class Tnode>
void MemorySlot<Tnode>::ReleaseNode(Tnode *node)
{
    //finished with this tnode, mark as available
    node->SetActive(false);
}

template<class Tnode>
MemorySlot<Tnode>::~MemorySlot()
{
    delete [] m_NodeSpace;
}

template<class Tnode>
void MemorySlot<Tnode>::ReleaseDuplicateNodes(Tnode *node)
{
    //finished with this tnode, mark as available
	long i;
	for (i = 0; i < m_Nodes; i++)
	{
		if (&m_NodeSpace[i] != node && m_NodeSpace[i].GameID() == node->GameID())
		{
			m_NodeSpace[i].SetGameID(0);
		}
	}
}

template<class Tnode>
bool MemorySlot<Tnode>::ContainsNode(Tnode *query)
{
	return query >= m_NodeSpace && query < m_NodeSpace+m_Nodes;
}

#endif // _MEMORY_H_INCLUDED_
