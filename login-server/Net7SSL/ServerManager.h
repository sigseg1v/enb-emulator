// ServerManager.h

#ifndef _SERVER_MANAGER_H_INCLUDED_
#define _SERVER_MANAGER_H_INCLUDED_

#include "MemoryHandler.h"
//#include "AccountManager.h"
//#include "SectorServerManager.h"
//#include "ConnectionManager.h"
//#include "MessageQueue.h"

class TcpListener;
class Connection_B;
class UDPClient;
class CircularBuffer;

class ServerManager
{
public:
    ServerManager(unsigned long ip_addr);
    virtual ~ServerManager();
	void RunMasterServer();
	void SetUDPConnections(UDPClient *connection, UDPClient *send);

public:

	Connection_B	  *GetConnection();
	CircularBuffer	* GetTCPCBuffer()	{ return m_TCPSendBuffer; }
	UDPClient		* MVASConnection()	{ return m_UDPMVAS; }


private:
	unsigned long		m_IpAddressInternal;
	CircularBuffer    * m_TCPSendBuffer;
	MemorySlot<Connection_B> *m_Connections;
	TcpListener		  * m_global_server_listener;
	TcpListener		  * m_local_cert_listener;
	UDPClient	      * m_UDPGlobal;  // used for sending to different ports	
    UDPClient         * m_UDPMVAS;    // used for receiving from the server


};

#endif // _SERVER_MANAGER_H_INCLUDED_
