// UDPClient.h
// Defines the UDPClient class which connects to the UDP Connection server in the Net7 server

#ifndef _UDP_CLIENT_H_INCLUDED_
#define _UDP_CLIENT_H_INCLUDED_

#define	MAX_UDPC_BUFFER					65536 //35840		//16384
#define MAX_QUEUE_BUFFER                8192 //4096

#pragma warning(disable:4786)

#include "PacketStructures.h"
#include <vector>
#include <map>

class UDPClient
{
public:
    UDPClient(short port, short connection_type, long ip_addr);
	virtual ~UDPClient();

    void    SetBroadcast(SOCKET socket);
    void    RecvThread();
    bool    VerifyConnection();

    void    SendLogin();
    void    SendAccount(char *username, char *password, char *info);
    void    SendResponse(long player_id, short port, short opcode, unsigned char *data, size_t length);
	void	SendAvatarLoggingIn(long avatar_id, long slot, char *account_name, long player_addr);
	

private:
    bool    OpenMultiPort(short port, long ip_addr);
    void    FixedClientComm();
	void    CreateFrom(long ip_addr, short port);
	bool    OpenFixedPort(short port, long ip_addr);
    //keepalive
    void    SendClientAlive();
    void    SendCommsAlive();
    int     UDP_RecvFromServer(char *buffer, int size);
    void    SendResponse(short port, short opcode, unsigned char *data, size_t length);
    void    UDP_Send(short port, const char *buffer, int bufferLen);
	void	WaitForResponse();
    void    WaitForResponse(short port, short opcode, unsigned char *data, size_t length);



private:
    long m_Port;
    long m_IPAddr;
	short m_ConnectionType;
    bool m_recv_thread_running;
	bool m_global_account_rcv;
	bool m_TransactionComplete;
    SOCKET m_Listen_Socket;
    SOCKADDR_IN m_SockAddr;
    unsigned char m_RecvBuffer[MAX_UDPC_BUFFER];
    unsigned char m_SendBuffer[MAX_UDPC_BUFFER];
    unsigned char m_QueueBuffer[MAX_QUEUE_BUFFER];


};


#endif