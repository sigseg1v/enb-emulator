// ConnectionManager.cpp
//
// Phase J (Linux port): connection lifecycle / cleanup loop for the
// blocking-TCP and SSL connection lists. Depends on Connection_B,
// SSL_Connection, MailslotManager — all WIN32-only for Phase J.
#ifdef WIN32


#include "Net7SSL.h"
#include "ConnectionManager.h"
#include "SSL_Connection.h"
//#include "Opcodes.h"
#include "MailslotManager.h"
#include "Connection_B.h"


ConnectionManager::ConnectionManager()
{
	m_SslConnectionList = NULL;
	m_ConnectionList = NULL;
	m_ConnectionCount = 0;
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

	SslConnectionEntry * p_ssl = m_SslConnectionList;
	SslConnectionEntry * next_ssl = NULL;
	while (p_ssl)
	{
		next_ssl = p_ssl->next;
		//delete p_ssl->connection;
		delete p_ssl;
		p_ssl = next_ssl;
	}
}

void ConnectionManager::AddConnection(Connection_B *tcp_connection)
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

bool ConnectionManager::CheckAccountInUse(char *accountname, Connection_B *c)
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

					if (p && p->connection->GetServerType() == CONNECTION_TYPE_GLOBAL_PROXY_TO_SERVER)
					{
						p->connection->KillConnection();
						return true;
					}
					else
					{
						//free up this floating connection
						p->connection->KillConnection();
					}
				}
			}
		}
		p = p->next;
	}

	return false;
}

void ConnectionManager::AddSslConnection(SSL_Connection *ssl_connection)
{
	SslConnectionEntry *entry = new SslConnectionEntry;
	entry->connection = ssl_connection;
	entry->next = NULL;
	if (m_SslConnectionList)
	{
		// Find the last entry in the linked list
		SslConnectionEntry * p = m_SslConnectionList;
		while (p->next)
		{
			p = p->next;
		}
		// Add the new entry to the end of the linked list
		p->next = entry;
	}
	else
	{
		m_SslConnectionList = entry;
	}
}

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

// MailManager::HandleMessage() lives in MailslotManager.cpp (portable on
// both Win32 and Linux). It used to be defined here on Win32-only because
// the tada-o snapshot's login-server tree was missing MailslotManager.cpp
// entirely; Phase J restored that .cpp and moved the body there.
#endif // WIN32 — Phase J file-level guard