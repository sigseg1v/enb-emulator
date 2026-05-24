// Memory.cpp
//
// Handles allocation of Time Nodes.
//
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
#include "MemoryHandler.h"
#include "SectorData.h"
#include <vector>
#include "ObjectManager.h"
#include "PlayerClass.h"
#include "ServerManager.h"

// Start player cleanout thread
#ifdef WIN32
void __cdecl RunCleanoutThreadAPI(void *arg)
{
    GMemoryHandler::MParam * param = (GMemoryHandler::MParam *) arg;
    GMemoryHandler * mgr = param->ClassAdd;
    delete param;
    mgr->CheckPlayerNodeThread();
    _endthread();
}
#else // Linux
void * RunCleanoutThreadAPI(void *arg)
{
    GMemoryHandler::MParam * param = (GMemoryHandler::MParam *) arg;
    GMemoryHandler * mgr = param->ClassAdd;
    delete param;
    mgr->CheckPlayerNodeThread();
    return NULL;
}
#endif

TimeNode * MemoryHandler::GetBroadcastNodeSlot()
{
	TimeNode *NextNode;// = m_BroadcastQueueBuf;
	static u32 last_tick = 0;
	long i = m_CircularIndex;
    long spincount = 0;
	//just use the next available broadcast slot,
	//it should be ok to use by now, but test to see if it fixes the lockup

	//long term slots will need to be allocated in a different buffer
	/*while (m_BroadcastQueueNodes[i]->player_id != 0)
	{
		i++;
		if (i==m_NodeSize)
		{
			i = 0;
			break;
		}
	}*/

	NextNode = m_BroadcastQueueNodes[i];

	//LogMessage("->Grabbed slot %d\n", i);

	m_CircularIndex = i + 1;

	if (m_CircularIndex == m_NodeSize)
	{
		m_CircularIndex = 0;
		float time_diff = (float)(GetNet7TickCount() - last_tick)/1000.0f;
		LogMessage("--->>>> Reset the timeslot system, time since last reset = %.2f\n", time_diff);
	}

	return NextNode;
}

MemoryHandler::MemoryHandler(long nodes)
{
	TimeNode * BroadcastQueueBuf = new TimeNode[nodes];
	memset(BroadcastQueueBuf, 0, sizeof(TimeNode)*nodes);
	g_cumulative_mem += sizeof(TimeNode)*nodes;

	m_NodeSize = nodes;

	if (!BroadcastQueueBuf)
	{
		LogMessage("FATAL ERROR: Unable to initialise memory.\n");
		exit(1);
	}

	int i;
    TimeNode *Node;
	for (i = 0; i < m_NodeSize; i++)
	{
        Node = &BroadcastQueueBuf[i];
		m_BroadcastQueueNodes.push_back(Node); //assign each node to a vector slot
	}

	m_CircularIndex = 0;
}

MemoryHandler::~MemoryHandler()
{
	//unhook any active calls still on the buffer
	long i;
	for (i = 0; i < m_NodeSize; i++)
	{
		if (m_BroadcastQueueNodes[i]->player_id != 0)
		{
			m_BroadcastQueueNodes[i]->player_id = NODE_NO_LONGER_NEEDED;
		}
	}

	usleep(100 * 1000);

	TimeNode * BroadcastBuff = m_BroadcastQueueNodes[0];

	delete[] BroadcastBuff;

	i = BROADCAST_SLOTS;

	while (i > m_NodeSize)
	{
		BroadcastBuff = m_BroadcastQueueNodes[i];
		delete[] BroadcastBuff;
		i += NODE_EXTEND_SIZE;
	}

	m_BroadcastQueueNodes.clear();
}

// Global memory handler

GMemoryHandler::GMemoryHandler(long player_nodes)
{
    long node_index;

    m_PlayerQueueBuf = new Player[player_nodes];
    //NB - no memset for derived class - you'll obliterate the vptr.

	g_cumulative_mem += sizeof(Player)*player_nodes;

	m_PlayerBufferSize = player_nodes;
    m_PlayerCount = 0;
    m_PlayerCircularIndex = 0;

	if (!m_PlayerQueueBuf)
	{
		LogMessage("FATAL ERROR: Unable to initialise memory for player handling.\n");
	}
    else
    {
        for (node_index = 0; node_index < m_PlayerBufferSize; node_index++) //set all players to 'AVAILABLE'
        {
            m_PlayerQueueBuf[node_index].SetGameID(PLAYER_NODE_AVAILABLE);
            m_PlayerQueueBuf[node_index].SetCharacterID(-1);
            m_PlayerQueueBuf[node_index].SetAccountUsername(0);
			m_PlayerQueueBuf[node_index].SetLastAccessTime(0);
        }      
    }

	//Now Allocate Timenodes
	m_Timeslots = new MemoryHandler(player_nodes * 450);
}

GMemoryHandler::~GMemoryHandler()
{
	delete[] m_PlayerQueueBuf;
	delete m_Timeslots;
}


long GMemoryHandler::GetPlayerCount()
{
    return (m_PlayerCount);
}

TimeNode * GMemoryHandler::GetBroadcastNodeSlot()
{
	return m_Timeslots->GetBroadcastNodeSlot();
}

//this is no longer needed, done in PlayerManager::RunMovementThread
void GMemoryHandler::CheckPlayerNodeThread()
{
	Player *node = (0);
	long node_index;

	unsigned long current_time = GetNet7TickCount();

	m_PlayerMutex.Lock();

	for (node_index = 0; node_index < m_PlayerBufferSize; node_index++)
	{
		node = &m_PlayerQueueBuf[node_index];
		//See if the player has had any involvement with anything for the past half hour 
		//    (if the connection is invalid and we haven't handled them for 
		//     more than 2 mins then it is a dead connection - Net7Proxy will issue a keepalive every 30 secs)
		if (node->GameID() != PLAYER_NODE_AVAILABLE && node->LastAccessTime() != 0 && (node->LastAccessTime() + 60000*2) < current_time )
		{
			//release the player node.
			/*LogMessage("*** Cleanup thread is removing dead player: %s [0x%08x]\n*** last received time: %d, time now: %d diff: %d\n", node->Name(), node->GameID(),
			node->LastAccessTime(), current_time, current_time - node->LastAccessTime());*/
			g_ServerMgr->m_PlayerMgr.DropPlayerFromGalaxy(node);
			ReleasePlayerNode(node);              
		}
	}

	m_PlayerMutex.Unlock();
}


// for New 'Player' class

Player * GMemoryHandler::GetPlayerNode(char *account_name)
{
    Player *node = (0);
    Player *NodeAllocated = (0);
    long node_index; 

    if (m_PlayerCount >= MAX_ONLINE_PLAYERS)
    {
        return (0);
    }

    m_PlayerMutex.Lock();

    node_index = m_PlayerCircularIndex;
    
    //Get first available PlayerNode after the current circular index
	while (m_PlayerQueueBuf[node_index].GameID() != PLAYER_NODE_AVAILABLE)
	{
		node_index++;
		if (node_index==m_PlayerBufferSize)
		{
			node_index = 0;
		}
	}
    
    NodeAllocated = &m_PlayerQueueBuf[node_index];
    
    m_PlayerCircularIndex = node_index + 1;

	if (m_PlayerCircularIndex == m_PlayerBufferSize)
	{
		m_PlayerCircularIndex = 0;
	}
    
    NodeAllocated->SetGameID(node_index | PLAYER_TAG);
    NodeAllocated->SetGroupID(-1);
    NodeAllocated->SetMVASIndex(-1);
    NodeAllocated->SetLastAccessTime(GetNet7TickCount());

	NodeAllocated->SetGameIndex(node_index);
    
    m_PlayerCount++;

    m_PlayerMutex.Unlock();

	return NodeAllocated;
}

void GMemoryHandler::ReleasePlayerNode(Player *player)
{
    m_PlayerMutex.Lock();
    if (player->GameID() != PLAYER_NODE_AVAILABLE && m_PlayerCount > 0)
    {
		//LogMessage("Player %s [%08x] released\n", player->Name(), player->GameID());
        player->SetGameID(PLAYER_NODE_AVAILABLE);
        m_PlayerCount--;
    }

    player->SetLastAccessTime(0);
    player->SetAccountUsername(0);
    player->SetCharacterID(-1);
    m_PlayerMutex.Unlock();
}

//NB This is only used internally for range checking, do not use this for anything else!
Player * GMemoryHandler::_GetPlayerNumber(long node_index)
{
	if (node_index >= 0 && node_index < MAX_ONLINE_PLAYERS)
	{
		return &m_PlayerQueueBuf[node_index];
	}
	else
	{
		return 0;
	}
}

Player * GMemoryHandler::GetPlayerA(long avatar_id, bool sector_login)
{
    Player *player = 0;

    long avatar_index = avatar_id & 0x00FFFFFF;

	if (!IS_PLAYER(avatar_id)) return (0);

    if (avatar_index > m_PlayerBufferSize) //see if this is in valid player range
    {
        //LogMessage(">> Player Not valid. id: %x index = %x\n", avatar_id, avatar_index);
        return (0);
    }

    m_PlayerMutex.Lock();
    player = &m_PlayerQueueBuf[avatar_index];
    m_PlayerMutex.Unlock();

    if (player->GameID() == PLAYER_NODE_AVAILABLE) //make sure there's something there
    {
        //LogMessage("GameID for avatarid %x = %x\n",avatar_id,player->GameID());
        if (sector_login)
        {
            LogMessage("Override invalid player ID for %s\n", player->Name());
            player->SetGameID(avatar_id);
            return player;
        }
        return (0);
    }

    //player->SetLastAccessTime(GetNet7TickCount());

    return player;
}