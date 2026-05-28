// TcpListener.h

#ifndef _TCP_LISTENER_H_INCLUDED_
#define _TCP_LISTENER_H_INCLUDED_

#include <cstdint>
#include <net7/Mutex.h>

class ServerManager;

class TcpListener
{
public:
    TcpListener(unsigned long ip_address, unsigned short port, ServerManager &server_mgr, int server_type);
    virtual ~TcpListener();

public:
    void RunThread();
	void Shutdown();

private:
    unsigned long m_IpAddress;
    uint16_t m_TcpPort;
	ServerManager &m_ServerMgr;
	int		m_ServerType;
    SOCKET  m_TcpListenerSocket;
	bool	m_TcpListenerThreadRunning;
    pthread_t m_Thread;
};

#endif // _TCP_LISTENER_H_INCLUDED_
