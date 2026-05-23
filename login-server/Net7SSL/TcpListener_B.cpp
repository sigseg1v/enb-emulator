// TcpListener.cpp
//
// Instantiating this class will create a new thread
// to listen on the specified port.
//

#include "Net7SSL.h"
#include "Connection_B.h"
#include "TcpListener_B.h"
#include "ServerManager.h"
#include "ConnectionManager.h"

// This helper function is referenced by _beginthread to launch the TCP Listener thread.
#ifdef WIN32
void __cdecl RunTcpListenerThread(void *arg)
#else
void * RunTcpListenerThread(void *arg)
#endif
{
	((TcpListener *) arg)->RunThread();
#ifdef WIN32
	_endthread();
#else
	return NULL;
#endif
}

// Constructor
TcpListener::TcpListener(unsigned long ip_address, unsigned short port, ServerManager &server_mgr, int server_type)
	: m_IpAddress(ip_address), m_TcpPort(port), m_ServerMgr(server_mgr), m_ServerType(server_type)
{
	m_TcpListenerThreadRunning = false;
	m_TcpListenerSocket = INVALID_SOCKET;

#ifdef WIN32
	_beginthread(&RunTcpListenerThread, 0, this);
#else
	pthread_create(&m_Thread, NULL, &RunTcpListenerThread, (void *) this);
#endif
}

// Destructor
TcpListener::~TcpListener()
{
	Shutdown();
	Sleep(1);
}

// This is the entry point for the TCP listener thread
void TcpListener::RunThread()
{
	struct sockaddr_in name;
	struct sockaddr_in from;
#ifdef WIN32
	int from_length;
#else
	socklen_t from_length;
#endif
	SOCKET s;
	unsigned char *ip;

	// Create a socket
	m_TcpListenerSocket = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
	if (m_TcpListenerSocket == INVALID_SOCKET)
	{
		LogMessage("Unable to create TCP listener socket\n");
		return;
	}

	memset(&name, 0, sizeof(sockaddr_in));
	name.sin_family = AF_INET;
	//name.sin_addr.s_addr = m_IpAddress;
	name.sin_addr.s_addr = INADDR_ANY;
	name.sin_port = htons(m_TcpPort);

	if (bind(m_TcpListenerSocket, (struct sockaddr *) &name, sizeof(name)))
	{
		LogMessage("TCP Listener unable to bind to socket on port %d\n", m_TcpPort);
		closesocket(m_TcpListenerSocket);
		return;
	}

	if (listen(m_TcpListenerSocket, SOMAXCONN))
	{
		LogMessage("Listen failed on port %d\n", m_TcpPort);
		closesocket(m_TcpListenerSocket);
		return;
	}

	LogMessage("Listening for incoming TCP/IP connections on port %d\n", m_TcpPort);

	m_TcpListenerThreadRunning = true;
    
	memset(&from, 0, sizeof(from));

	while ((!g_ServerShutdown) && (m_TcpListenerThreadRunning) && (m_TcpListenerSocket != INVALID_SOCKET))
	{
		from_length = sizeof(from);
		s = accept(m_TcpListenerSocket,(sockaddr *) &from, &from_length);

		if (s != INVALID_SOCKET)
		{
			BOOL bOptVal = TRUE;
			setsockopt(s, SOL_SOCKET, SO_KEEPALIVE, (char*)&bOptVal, sizeof(BOOL));

	            	ip = (unsigned char *) &from.sin_addr;
			LogMessage("Accepted TCP connection from %d.%d.%d.%d on port %d\n", ip[0], ip[1], ip[2], ip[3], m_TcpPort);

			// Create a new Connection using the new socket
			Connection_B * node = m_ServerMgr.GetConnection();
			if (node)
			{
				Connection_B * client = node->ReSetConnection(s, m_ServerMgr, m_TcpPort, m_ServerType, (unsigned long*)&from.sin_addr);
			
				g_ConnectionMgr->AddConnection(client);
			}
			else
			{
				//abort net7ssl
				g_ServerShutdown = true;
				break;
			}
		}
		else
		{
			// add error handling
		}

		SocketReady(-1); // Wait infinite until something is happening on the socket (a Shutdown() would trigger this too...)
	}

	Shutdown();

//	LogMessage("TCP Listener Thread exiting\n");
}

bool TcpListener::SocketReady(int ttimeout)
{
	fd_set fds;
	struct timeval timeout;
	long ret;

	FD_ZERO(&fds);
	FD_SET(m_TcpListenerSocket, &fds);

	if (ttimeout >= 0)
	{
		timeout.tv_sec	= ttimeout;
		timeout.tv_usec	= 0;

		ret = select(sizeof(fds)*8, &fds, NULL, NULL, &timeout);
	}
	else
	{
		ret = select(sizeof(fds)*8, &fds, NULL, NULL, NULL);
	}

	return bool(ret > 0);	
}

void TcpListener::Shutdown()
{
	m_Mutex.Lock();

	if (m_TcpListenerSocket != INVALID_SOCKET)
	{
		shutdown(m_TcpListenerSocket, 2);
		closesocket(m_TcpListenerSocket);
		m_TcpListenerSocket = INVALID_SOCKET;
	}

	m_TcpListenerThreadRunning = false;

	m_Mutex.Unlock();
}

