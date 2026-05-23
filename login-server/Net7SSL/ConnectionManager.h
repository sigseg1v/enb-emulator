// ConnectionManager.h

#ifndef _CONNECTION_MANAGER_SSL_H_INCLUDED_
#define _CONNECTION_MANAGER_SSL_H_INCLUDED_

#include "MessageQueue.h"

class SSL_Connection;
class Connection_B;

class ConnectionManager
{
public:
	ConnectionManager();
	virtual ~ConnectionManager();

public:
	void	AddSslConnection(SSL_Connection *ssl_connection);
	void	AddConnection(Connection_B *tcp_connection);
	void	CheckConnections();
	void	CheckSslConnections();
	u32		GetConnectionCount()		{ return m_ConnectionCount; }
	bool	CheckAccountInUse(char *accountname, Connection_B *c = 0);
	void	BeginOpcodeSendThread();

private:
	// linked list for SSL Connection
	struct SslConnectionEntry;
	struct SslConnectionEntry
	{
		SSL_Connection * connection;
		struct SslConnectionEntry * next;
	};
	// linked list for TCP Connection
	struct ConnectionEntry;
	struct ConnectionEntry
	{
		Connection_B * connection;
		struct ConnectionEntry * next;
	};

	static UINT WINAPI ConnectionManager::OpcodeCommsThread(void *Param);
	void	RunOpcodeSendThread();
	void	HandleResend(long player_id, long packet_num);

private:
	SslConnectionEntry * m_SslConnectionList;
	ConnectionEntry * m_ConnectionList;
	u32		m_ConnectionCount;
	HANDLE				m_CommsThread;

};


#endif // _CONNECTION_MANAGER_SSL_H_INCLUDED_
