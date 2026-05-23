// ConnectionManager.cpp


#include "Net7.h"
#include "ConnectionManager.h"
#include "SSL_Connection.h"
#include "Connection.h"
#include "ServerManager.h"
#include "PlayerClass.h"
#include "Opcodes.h"
#include "MailslotManager.h"


ConnectionManager::ConnectionManager()
{
	m_ConnectionList = NULL;
	m_ConnectionCount = 0;
	m_comms_thread_running = false;
	UINT uiThreadId = 0;

	//m_ResendBuffer = new CircularBuffer(0x1000, 2048);
	//m_ResendQueue = new MessageQueue(m_ResendBuffer);

	m_CommsThread = (HANDLE)_beginthreadex(NULL, 0, OpcodeCommsThread, this, CREATE_SUSPENDED, &uiThreadId);
}

ConnectionManager::~ConnectionManager()
{
	// Kill all TCP/IP connections and destroy the objects
	ConnectionEntry * p = m_ConnectionList;
	ConnectionEntry * next = NULL;
	while (p)
	{
		next = p->next;
		//delete p->connection;
		delete p;
		p = next;
	}
}

void ConnectionManager::AddConnection(Connection *tcp_connection)
{
	ConnectionEntry *entry = new ConnectionEntry;
	entry->connection = tcp_connection;
	entry->next = NULL;
	if (m_ConnectionList)
	{
		ConnectionEntry * p = m_ConnectionList;
		while (p->next)
		{
			p = p->next;
		}
		p->next = entry;
	}
	else
	{
		m_ConnectionList = entry;
	}

	m_ConnectionCount++;
}

void ConnectionManager::CheckConnections()
{
	// Drop the dead TCP/IP connections and destroy the objects
	ConnectionEntry * last = NULL;
	ConnectionEntry * p = m_ConnectionList;
	ConnectionEntry * kill = NULL;
	u32 current_tick = GetNet7TickCount();

	while (p)
	{
		if (!p->connection->IsActive())
		{
			kill = p;
			// This connection is no longer active
			// Remove this entry from the linked list
			if (last)
			{
				last->next = p->next;
			}
			else
			{
				m_ConnectionList = p->next;
			}
		}
		else
		{
			p->connection->ProcessRecvInputs(current_tick);
			p->connection->PulseConnectionOutput();
			last = p;
		}
		p = p->next;
		if (kill)
		{
			//LogMessage("ConnectionManager closed TCP connection for [%08x]\n", kill->connection->GameID());
			//kill the node, but don't kill the connection as the connection is static
			delete kill; 
			kill = NULL;
			m_ConnectionCount--;
		}
	}
}

bool ConnectionManager::CheckAccountInUse(char *accountname, Connection *c)
{
	// Drop the dead TCP/IP connections and destroy the objects
	ConnectionEntry * p = m_ConnectionList;

	while (p)
	{
		if (p->connection)
		{
			if (p->connection->GetAccountName())
			{
				if (_stricmp(p->connection->GetAccountName(), accountname) == 0 && p->connection != c)
				{
					//g_PlayerMgr->ErrorBroadcast("Account user %s trying to log in twice!\n", accountname);
					LogMessage("Account user %s trying to log in twice!\n", accountname);

					if (p->connection->GetServerType() == CONNECTION_TYPE_GLOBAL_PROXY_TO_SERVER)
					{
						p->connection->KillConnection("2x login GP2S");
						return true;
					}
					else
					{
						//free up this floating connection
						p->connection->KillConnection("2x login floating");
					}
				}
			}
		}
		p = p->next;
	}

	return false;
}

#if 0
void ConnectionManager::CheckSslConnections()
{
	// Drop the dead SSL connections and destroy the objects
	// Perform a check of each active SSL connection and destroy if they are dead
	SslConnectionEntry * last = NULL;
	SslConnectionEntry * p = m_SslConnectionList;
	SslConnectionEntry * kill = NULL;

	u32 tick = GetNet7TickCount();

	while (p)
	{
		if (!p->connection->IsActive())
		{
			// Kill this connection
			kill = p;
			// This SSL connection is no longer active
			// Remove this entry from the linked list
			if (last)
			{
				last->next = p->next;
			}
			else
			{
				m_SslConnectionList = p->next;
			}
		}
		else
		{				
			last = p;
		}
		p = p->next;
		if (kill)
		{
			//LogMessage("ConnectionManager deleted SSL connection\n");
			//delete kill->connection;
			delete kill;
			kill = NULL;
		}
	}

}
#endif

//This thread will check queues and send any required opcodes to players in turn.
//We will send off a maximum of MAX_OPCODES_PER_PLAYER_PER_CYCLE each cycle

//this may need to be tweaked to get the optimum throughput
#define MAX_OPCODES_PER_PLAYER_PER_CYCLE = 5

//This should probably be taken out of a thread and put somewhere else.
//Now it just sends off any TCP opcodes when they're ready.
void ConnectionManager::RunOpcodeSendThread()
{    
    m_comms_thread_running = true;
    
	// this sends TCP opcodes
    while (!g_ServerShutdown)
    {  
		CheckConnections();
		Sleep(50);
	};

	//ensure queues are empty
	int i = 5;
	while (i)
	{
		CheckConnections();
		g_PlayerMgr->SendUDPOpcodes();
		i--;
	}

	m_comms_thread_running = false;
}

void ConnectionManager::BeginOpcodeSendThread()
{
	ResumeThread(m_CommsThread);
}

UINT WINAPI ConnectionManager::OpcodeCommsThread(LPVOID Param)
{
	ConnectionManager* p_this = reinterpret_cast<ConnectionManager*>( Param );

	p_this->RunOpcodeSendThread();

	return 1;
}