// SSL_Listener.h

#ifndef _SSL_LISTENER_H_INCLUDED_
#define _SSL_LISTENER_H_INCLUDED_

#include <net7/Mutex.h>
#include <openssl/ssl.h>

#ifndef WIN32
#include <atomic>
#endif

class ServerManager;
class SSL_Connection;
class ConnectionManager;

class SSL_Listener
{
public:
	SSL_Listener(unsigned long ip_address, unsigned short port);
	virtual ~SSL_Listener();

public:
	void		RunThread();
	void		Shutdown();

	bool		ListenerThreadRunning()	{ return m_SslListenerThreadRunning; }

#ifndef WIN32
	// Phase K: per-connection worker entry point. Runs SSL_accept +
	// one read/write/close round trip on a detached std::thread, so a
	// slow handshake doesn't block other accepts.
	void		HandleAcceptedConnection(SSL_CTX *ctx, SOCKET sock, unsigned long client_ip);
#endif

private:
	bool		SocketReady(int ttimeout);
	Mutex		m_Mutex;
	WORD		m_TcpPort;
	unsigned long	m_IpAddress;
	SOCKET		m_ListenerSocket;
	bool		m_SslListenerThreadRunning;

	SSL_CTX 	*m_ssl_context;

#ifndef WIN32
	pthread_t m_Thread;
	std::atomic<int> m_ActiveWorkers{0};
#endif
};

SSL_Connection *GetSSLConnection();

extern ConnectionManager *g_ConnectionMgr;

#endif // _SSL_LISTENER_H_INCLUDED_

