// ConnectionManager.h

#ifndef _CONNECTION_MANAGER_H_INCLUDED_
#define _CONNECTION_MANAGER_H_INCLUDED_

#include <net7/Mutex.h>

class Connection;
class SSL_Connection;

class ConnectionManager
{
public:
	ConnectionManager();
	virtual ~ConnectionManager();

public:
	//void	AddSslConnection(SSL_Connection *ssl_connection);
	void	AddConnection(Connection *tcp_connection);
	void	CheckConnections();
	void	CheckSslConnections();

	// True while any active client-facing TCP link (the client's connection to
	// the master 3801 or sector 3500 listener) is still up. Used by the
	// post-logoff auto-shutdown to know when the client has fully disconnected.
	bool	HasActiveClientLink();

private:
	// linked list for SSL Connection
/*	struct SslConnectionEntry;
	struct SslConnectionEntry
	{
		SSL_Connection * connection;
		struct SslConnectionEntry * next;
	};*/
	// linked list for TCP Connection
	struct ConnectionEntry;
	struct ConnectionEntry
	{
		Connection * connection;
		struct ConnectionEntry * next;
	};

private:
    Mutex   m_Mutex;
	//SslConnectionEntry * m_SslConnectionList;
	ConnectionEntry * m_ConnectionList;
};

#endif // _CONNECTION_MANAGER_H_INCLUDED_
