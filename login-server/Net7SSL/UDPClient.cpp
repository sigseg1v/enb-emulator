// Phase J (Linux port): UDP message-bus client used to receive MVAS keep-
// alives and dispatch opcodes back into the connection-manager tree. Uses
// _beginthread + PacketMethods (WIN32-only headers). Wall the whole TU.
#ifdef WIN32
#include "Net7SSL.h"
#include "UDPClient.h"
#include "Net7SSL_opcodes.h"
#include "PacketMethods.h"

void __cdecl LaunchUDPCRecvThread(void *arg)
{
    ((UDPClient *) arg)->RecvThread();
    _endthread();
}

bool UDPClient::OpenFixedPort(short port, long ip_addr)
{
    bool success = true;
    ULONG uAddr = 0;
    char strLocal[MAX_PATH] = { 0 };

    m_Listen_Socket = socket(AF_INET, SOCK_DGRAM, 0);
    
    if(m_Listen_Socket == INVALID_SOCKET)
    {
        LogMessage("Invalid Socket %d. Program Aborted\n", GetLastError());
        success = false;
    }

	if (SOCKET_ERROR != gethostname(strLocal, MAX_PATH))
	{
        struct hostent* hp;
        hp = gethostbyname(strLocal);
        if (hp != NULL)	
        {
            strcpy(strLocal, hp->h_name);
            uAddr = *((ULONG *) hp->h_addr_list[0]);
        }
    }

    CreateFrom(uAddr,0);

    if ( SOCKET_ERROR == bind(m_Listen_Socket, (sockaddr *) &m_SockAddr, sizeof(m_SockAddr)))
    {
        LogMessage("Socket Bind error '%d'. Connection not established\n", GetLastError());
        closesocket( m_Listen_Socket );
        success = false;
    }

    CreateFrom(ip_addr, port);

	if (SOCKET_ERROR == connect( m_Listen_Socket, (sockaddr *) &m_SockAddr, sizeof(m_SockAddr)))
    {
        closesocket( m_Listen_Socket );
        success = false;
    }

    return success;
}

bool UDPClient::OpenMultiPort(short port, long ip_addr)
{
    bool success = true;

    m_Listen_Socket = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
	
    if(m_Listen_Socket == INVALID_SOCKET)
    {
        LogMessage("Invalid Socket %d. Program Aborted\n", GetLastError());
        success = false;
    }

    SetBroadcast(m_Listen_Socket);

    return success;
}

UDPClient::UDPClient(short port, short connection_type, long ip_addr)
{
    m_IPAddr = ip_addr;
    m_Port = port;
	m_ConnectionType = connection_type;
	bool success = false;
	m_global_account_rcv = false;

    //if we want a UDP port that can be used to send messages to different ports, it needs to be opened in a different way
    switch (connection_type)
    {
    case CLIENT_TYPE_FIXED_PORT:
        success = OpenFixedPort(port, ip_addr);
        break;

    case CLIENT_TYPE_MULTI_PORT:
        success = OpenMultiPort(port, ip_addr);
        break;

    default:
        LogMessage("Bad port type.\n");
        return;
        break;
    }

    if (!success)
    {
        LogMessage("Error while opening UDP port\n.");
        return;
    }
    
    _beginthread(&LaunchUDPCRecvThread, 0, this);
}

UDPClient::~UDPClient()
{

}

void UDPClient::CreateFrom(long ip_addr, short port)
{
	memset(&m_SockAddr, 0, sizeof(m_SockAddr));
	m_SockAddr.sin_addr.s_addr = ip_addr;
	m_SockAddr.sin_port = htons( port );
	m_SockAddr.sin_family = AF_INET;
}

void UDPClient::SetBroadcast(SOCKET socket) 
{
	// If this fails, we'll hear about it when we try to send.  
	int broadcastPermission = 1;
	setsockopt(socket, SOL_SOCKET, SO_BROADCAST, (char *) &broadcastPermission, sizeof(broadcastPermission));
} 

void UDPClient::RecvThread()
{
	int received;
	EnbUdpHeader *header;
    short s_null = 0;

	m_recv_thread_running = true;

	while (!g_ServerShutdown && m_recv_thread_running)
    {	
		received = UDP_RecvFromServer((char*)m_RecvBuffer, MAX_UDPC_BUFFER);

		if (received > 0)
		{
			header = (EnbUdpHeader*)m_RecvBuffer;
			
			unsigned short bytes = header->size - sizeof(EnbUdpHeader);
			short opcode = header->opcode;
            long player_id = header->player_id;
            char *msg = (char*)(m_RecvBuffer + sizeof(EnbUdpHeader));
			
			// Make sure we recived the correct # of byes 
			if (received == (int)(bytes + sizeof(EnbUdpHeader)))
			{
				switch (opcode)
				{
				case ENB_OPCODE_4001_SSL_REGISTER_S_SSL:
					g_MaxPlayerCount = *((long *) &msg[0]);
                    g_LoggedIn = true;
					break;

				case ENB_OPCODE_4002_SSL_PLAYERCOUNT:
					g_PlayerCount = *((long *) &msg[0]);
					break;

				case ENB_OPCODE_4004_SSL_AVATARCONFIRM_S_SSL:
					m_TransactionComplete = true;
					break;

                default:
					LogMessage("Unknown SSL interaction opcode: 0x%04x\n", opcode);
                    break;
				}
			}
            else 
            {
                LogMessage("Message read failure... opcode = 0x%04x length = 0x%x (rcvd = %x)\n",opcode, bytes, received);
                LogMessage("Opcode 0x%04x: Length: 0x%04x\n", opcode, bytes);
            }
		}
	}

	closesocket(m_Listen_Socket);

	_endthread();
}

int UDPClient::UDP_RecvFromServer(char *buffer, int size)
{
	int rtn;

	if ((rtn = recv(m_Listen_Socket, buffer, size, 0)) < 0) 
	{
		Sleep(200); //little pause in here if we get a -1
	}
	
	return rtn;
}

bool UDPClient::VerifyConnection()
{
	long count = 5;

	long version = 1;

    LogMessage("Sending login.\n");

	while (count > 0 && g_LoggedIn == false)
	{
		SendResponse(MVAS_LOGIN_PORT, ENB_OPCODE_4000_SSL_REGISTER_SSL_S, (unsigned char *) &version, sizeof(version));
		Sleep(1000);
		count--;
	}

	if (g_LoggedIn)
	{
		LogMessage("Net7SSL Comms OK\n");
    }
	else
	{
        LogMessage("Net7SSL Comms failed - server not found.\n");
		return false;
	}
	   
	return true;
}

void UDPClient::SendAvatarLoggingIn(long avatar_id, long slot, char *account_name, long player_addr)
{
	long count = 5;
	m_TransactionComplete = false;

	unsigned char udp_auth_packet[256];
	int index = 0;

	AddData(udp_auth_packet, avatar_id, index);
	AddData(udp_auth_packet, player_addr, index);
	AddData(udp_auth_packet, (short)slot, index);
	AddDataLS(udp_auth_packet, account_name, index);

	LogMessage("Sending Avatar %s:%d login.\n", account_name, avatar_id);

	SendResponse(MVAS_LOGIN_PORT, ENB_OPCODE_4003_SSL_AVATARLOGIN_SSL_S, udp_auth_packet, index);

	//TODO: require verification for this critical opcode, but put the loop in ClientToGlobalServer at the call, so it doesn't
	//		hold up anyone else's login. When we receive the verification you'll need a selector from the avatar_id into the
	//		connection list, so we know who's connection to push.
}

void UDPClient::SendResponse(short port, short opcode, unsigned char *data, size_t length)
{
	EnbUdpHeader * header = (EnbUdpHeader *) m_SendBuffer;
	header->size = (short) length + sizeof(EnbUdpHeader);
	header->opcode = opcode;
    header->player_id = -2;

	if (length)
	{
		memcpy(m_SendBuffer + sizeof(EnbUdpHeader), data, length);
	}

	int bytes = length + sizeof(EnbUdpHeader);

    UDP_Send(port, (char*)&m_SendBuffer[0], bytes);
}

void UDPClient::SendResponse(long player_id, short port, short opcode, unsigned char *data, size_t length)
{
	EnbUdpHeader * header = (EnbUdpHeader *) m_SendBuffer;
	header->size = (short) length + sizeof(EnbUdpHeader);
	header->opcode = opcode;
    header->player_id = player_id;

	if (length)
	{
		memcpy(m_SendBuffer + sizeof(EnbUdpHeader), data, length);
	}

	int bytes = length + sizeof(EnbUdpHeader);

    UDP_Send(port, (char*)&m_SendBuffer[0], bytes);
}

void UDPClient::UDP_Send(short port, const char *buffer, int bufferLen) 
{
    int buffsend;
    
    switch (m_ConnectionType)
    {
    case CLIENT_TYPE_MULTI_PORT:
        {
            SOCKADDR_IN lSockAddr;
            memset(&lSockAddr,0, sizeof(lSockAddr));
            lSockAddr.sin_family = AF_INET;
            lSockAddr.sin_port = htons(port);
            lSockAddr.sin_addr.s_addr = m_IPAddr; //inet_addr(m_IPAddr);
            
            // Write out the whole buffer as a single message.
            if (buffsend = sendto(m_Listen_Socket, buffer, bufferLen, 0,
                (sockaddr *) &lSockAddr, sizeof(lSockAddr)) != bufferLen) 
            {
                fprintf(stderr,"\nSend failed %d %d", bufferLen, buffsend);
            }
        }
        break;
        
    case CLIENT_TYPE_FIXED_PORT:
        {
            buffsend = send(m_Listen_Socket, buffer, bufferLen, 0);
        }
        break;
    }
}

void UDPClient::WaitForResponse()
{
    int count = 20;
	while (count > 0 && m_global_account_rcv == false)
	{
        for (int i = 0; i < 4; i++)
        {
            if (m_global_account_rcv) break;
            Sleep(250);
        }
		count--;
	}
}

void UDPClient::WaitForResponse(short port, short opcode, unsigned char *data, size_t length)
{
    int count = 20;
	while (count > 0 && m_global_account_rcv == false)
	{
		SendResponse(port, opcode, data, length);
        for (int i = 0; i < 4; i++)
        {
            if (m_global_account_rcv) break;
            Sleep(250);
        }
		count--;
	}
}
#endif // WIN32 — Phase J file-level guard