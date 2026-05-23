// ServerManager.cpp

#include "Connection_B.h"
#include "Net7SSL.h"
#include "ServerManager.h"
#include "TcpListener_B.h"
#include "MemoryHandler.h"

// Constructor
ServerManager::ServerManager(unsigned long ip_addr)
{
	g_ServerMgr = this;
	m_IpAddressInternal = ip_addr;
	m_TCPSendBuffer = new CircularBuffer(0x80000, 0x1000);
	m_Connections = new MemorySlot<Connection_B>(75); //allow for 75 players to have avatar selection/editing connections simultaneously
}

// Destructor
ServerManager::~ServerManager()
{
	delete m_Connections;
	delete m_TCPSendBuffer;
}

void ServerManager::RunMasterServer()
{
	// Instantiate the TCP Listener object for the Global Server

	m_global_server_listener = new TcpListener(m_IpAddressInternal, GLOBAL_SERVER_PORT, *this, CONNECTION_TYPE_CLIENT_TO_GLOBAL_SERVER);
	m_local_cert_listener = new TcpListener(m_IpAddressInternal, SSL_LOCALCERT_LOGIN_PORT, *this, CONNECTION_TYPE_GLOBAL_PROXY_TO_SERVER);
}

Connection_B* ServerManager::GetConnection()
{
	Connection_B *c = m_Connections->GetInactiveNode();
	if (c == 0)
	{
		LogMessage(">>>>> CRITICAL ERROR! Out Of Connection Nodes!! This should only happen if we exceed the 100 player mark.\n");
	}
	else
	{
		c->SetGameID(-1);
	}
	return c;
}

void ServerManager::SetUDPConnections(UDPClient *connection, UDPClient *send)
{
	m_UDPMVAS		= connection;
    m_UDPGlobal     = send;
}