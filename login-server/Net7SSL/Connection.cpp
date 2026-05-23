// Connection.cpp for BLOCKING TCP connection (hence Connection_B)
//
// Phase J (Linux port): this translation unit is the heavyweight blocking-
// TCP login/global-server session handler — RC4 recv loop, opcode dispatch,
// Westwood RSA exchange, MessageQueue/CircularBuffer back-pressure, the
// works. Porting it to Linux is a Phase J continuation; for now it is
// WIN32-only. The Linux net7ssl binary stands up the SSL listener only
// (port 443) and any inbound TCP-side connection from the Net-7 server
// will currently bounce until this TU is ported. Same pattern as
// proxy/Connection.cpp.
#ifdef WIN32

#include "Net7SSL.h"
#include "Connection_B.h"
#include "PacketStructures.h"
#include "ServerManager.h"

Connection_B::Connection_B()
{
	m_ConnectionActive		= true;		// Mark this as an active connection
	m_TcpThreadRunning		= false;	// Start thread in non-running mode
	m_TcpThreadTerminated	= false;
	m_KeysExchanged			= false;

	// Initialize settings
	m_Socket	= INVALID_SOCKET;
	m_TcpPort	= 0;
	m_ServerType	= 0;
	m_IPaddr	= 0;
	m_AvatarID	= 0;

	memset(m_AccountUsername, 0, 100);
		
	m_InactivityTimer	= 0;
	m_MaxInactivityTime	= 0; // Infinite

	m_SendQueue = new MessageQueue("TCP", g_ServerMgr->GetTCPCBuffer());

	UINT uiThreadId = 0;
	m_RecvThreadHandle = (HANDLE)_beginthreadex(NULL, 1024, SocketRecvThread, this, 0, &uiThreadId);

	if (!m_RecvThreadHandle) LogMessage("Unable to create another thread for TCP Connection\n");
}

Connection_B * Connection_B::ReSetConnection(SOCKET s, ServerManager &server_mgr, short port, int server_type, unsigned long* ip_addr)
{
	if (!m_RecvThreadHandle) // This should never be needed, but it's in here just in case.
	{
		LogMessage("!Restarting TCP Connection receive thread\n");
		UINT uiThreadId = 0;
		m_RecvThreadHandle = (HANDLE)_beginthreadex(NULL, 1024, SocketRecvThread, this, 0, &uiThreadId);
	}

	m_ConnectionActive	= true;		// Mark this as an active connection
	m_KeysExchanged		= false;
	m_LoginHandoff		= false;
	m_InactivityTimer	= 0;

	memset(m_AccountUsername, 0, 100);

	// Temporarily set the AvatarID to 1, otherwise it might get killed off
	// too soon with a heavy load (lots of connections)
	m_AvatarID			= 1;

	m_Socket		= s;
	m_TcpPort		= port;
	m_ServerType		= server_type;
	
	if (ip_addr) m_IPaddr 	= *ip_addr;

	m_SendQueue->ResetQueue();

	WakeupThread();		

	return this;
}

Connection_B::~Connection_B()
{
	fprintf(stderr, "[%d] Connection Destroyed\n", (int)m_RecvThreadHandle);
	
	KillConnection();
	
	// Let thread know it is terminated.
	if (!m_TcpThreadTerminated)
	{
		m_TcpThreadTerminated = true;
		WakeupThread();

		Sleep(1);
	}

	delete m_SendQueue;
}

bool Connection_B::IsActive()
{
	// Good opportunity to check g_ServerShutdown
	if (g_ServerShutdown) WakeupThread();

	return m_ConnectionActive;
}

// Will return false if timeout expired or error occured.
bool Connection_B::SocketReady(int ttimeout)
{
	fd_set fds;
	struct timeval timeout;
	long ret;

	FD_ZERO(&fds);
	FD_SET(m_Socket, &fds);

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

void Connection_B::WakeupThread()
{
	m_TcpThreadRunning = true;

	ResumeThread(m_RecvThreadHandle);
}

void Connection_B::Send(unsigned char *Buffer, int length)
{
	m_SendQueue->Add(Buffer, length, m_AvatarID);
}

void Connection_B::SetRC4Key(unsigned char *rc4_key)
{
	m_CryptOut.PrepareKey(rc4_key, RC4_KEY_SIZE);
	m_CryptIn.PrepareKey(rc4_key, RC4_KEY_SIZE);
}

bool Connection_B::DoKeyExchange()
{
	unsigned char buffer[128];
	unsigned char *p = buffer;
	int length;

	// Send the RSA Public Key to the client
	length = m_WestwoodRSA.GetModulus(&p);
	length += m_WestwoodRSA.GetPublicExponent(&p);

	if ( (send(m_Socket, (char *) buffer, length, 0) != length) ||		// Did send fail?
	     !SocketReady() || 							// Did the socket timeout?
	     (recv(m_Socket, (char *) buffer, 4, 0) != 4) )			// Never got a 4-byte header?
	{
		return false;		
	}

	long key_length = (long) ntohl((*((unsigned long *) buffer)));
	if ( (key_length <  WWRSA_BLOCK_SIZE) || (key_length > (WWRSA_BLOCK_SIZE + 1)) )
	{
		LogMessage("[%d] ERROR: DoKeyExchange key_length = %d\n", m_RecvThreadHandle, key_length);
		return false;
	}

	// Get the encrypted RC4 Session Key response from the client
	length = recv(m_Socket, (char *) buffer, key_length, 0);
	if (length != key_length)
	{
		LogMessage("[%d] ERROR: DoKeyExchange key_length = %d, recv_length = %d\n", m_RecvThreadHandle, key_length, length);
		return false;
	}

	// Ignore leading 0 if present
	p = buffer;
	if ( (key_length == WWRSA_BLOCK_SIZE + 1) && (*p == 0) )
	{
		key_length--;
		p++;
	}

	// Decrypt the RC4 Session Key
	unsigned char rc4key[WWRSA_BLOCK_SIZE];
	if (!m_WestwoodRSA.Decrypt(p, WWRSA_BLOCK_SIZE, rc4key))
	{
		LogMessage("[%d] ERROR: DoKeyExchange m_WestwoodRSA.Decrypt failed\n", m_RecvThreadHandle);
	        return false;
	}

	unsigned char rc4_key_buffer[RC4_KEY_SIZE];
	// Reverse the order of the decrypted RC4 Session Key
	rc4_key_buffer[0] = rc4key[0x3f];
	rc4_key_buffer[1] = rc4key[0x3e];
	rc4_key_buffer[2] = rc4key[0x3d];
	rc4_key_buffer[3] = rc4key[0x3c];
	rc4_key_buffer[4] = rc4key[0x3b];
	rc4_key_buffer[5] = rc4key[0x3a];
	rc4_key_buffer[6] = rc4key[0x39];
	rc4_key_buffer[7] = rc4key[0x38];

	SetRC4Key(rc4_key_buffer);

	return true;
}

bool Connection_B::DoClientKeyExchange()
{
	// Generate RC4 key
	unsigned int i = 0;
	unsigned char rc4key[RC4_KEY_SIZE];
	unsigned char buffer[128];

	memset(rc4key, 0, sizeof(rc4key));
	SetRC4Key(rc4key);

	// Receive the pubic key packet
	Sleep(20);
	memset(buffer, 0, sizeof(buffer));

	if ( !SocketReady() || (recv(m_Socket, (char *) buffer, 74, 0) != 74) )
	{
		LogMessage("[%d] ERROR: DoClientKeyExchange recv\n", m_RecvThreadHandle);
		return false;
	}

	// Clear the buffer
	memset(buffer, 0, WWRSA_BLOCK_SIZE - RC4_KEY_SIZE + sizeof(long));

	// Put the length in front of the buffer
	unsigned char *key = buffer + sizeof(long);
	*((unsigned long *) buffer) = ntohl(WWRSA_BLOCK_SIZE);

	// Copy the RC4 key to the bottom of the buffer
	unsigned char *dest = &key[WWRSA_BLOCK_SIZE - 1];
	unsigned char *src = rc4key;
	for (i = 0; i < RC4_KEY_SIZE; i++) *dest-- = *src++;

	// Encrypt the RC4 key
	m_WestwoodRSA.Encrypt(key, WWRSA_BLOCK_SIZE, key);

	// Send the encrypted RC4 key to the server
	int length = WWRSA_BLOCK_SIZE + sizeof(long);

	// Returns true if not socket error or timeout and buffer was completely sent
	return bool( SocketReady() && (send(m_Socket, (char *) buffer, length, 0) == length) );
}

bool Connection_B::RunKeyExchange()
{
	if (m_ServerType == CONNECTION_TYPE_SECTOR_SERVER_TO_PROXY || m_ServerType == CONNECTION_TYPE_GLOBAL_PROXY_TO_SERVER)
	{
		//LogMessage("[%08x] Unencrypted TCP link\n", this);
	}
	else if (m_ServerType < CONNECTION_TYPE_MASTER_SERVER_TO_SECTOR_SERVER)
	{
		if (!DoKeyExchange())
		{
			m_ConnectionActive = false;
			LogMessage("[%d] Client key exchange failed. Aborting.\n", m_RecvThreadHandle);
			return false;
		}
	}
	else
	{
		if (!DoClientKeyExchange())
		{
			m_ConnectionActive = false;
			LogMessage("[%d] Server/Server key exchange failed. Aborting.\n", m_RecvThreadHandle);
			return false;
		}
	}

	return true;
}

void Connection_B::SendResponse(short opcode, unsigned char *data, size_t length)
{
	if (!this)
	{
		LogMessage("ERROR: Connection::SendResponse called without connection");
		return;
	}

	*((short *) &m_SendBuffer[0]) = (short) length + sizeof(long);
	*((short *) &m_SendBuffer[2]) = opcode;

	if ((length + 4) < 4096)
	{
		memcpy(m_SendBuffer + sizeof(long), data, length);
	}
	else
	{
		LogMessage("[%d] Message too long to send via Connection: opcode %04x length %d\n", m_RecvThreadHandle, opcode, length);
		return;
	}

	int bytes = length + sizeof(long);

	if (m_ServerType != CONNECTION_TYPE_GLOBAL_PROXY_TO_SERVER)
	{
		m_CryptOut.RC4(m_SendBuffer, bytes);
	}

	Send(m_SendBuffer, bytes);
}

UINT WINAPI Connection_B::SocketRecvThread(LPVOID Param)
{
	Connection_B* p_this = reinterpret_cast<Connection_B*>( Param );

	p_this->RunRecvThread();

	return 1;
}

void Connection_B::RunRecvThread()
{
	bool isSocketReady	= false;		// Used for checking Socket state
	int numretries		= 0;

	int received;
	unsigned short bytes;
	short opcode;

	EnbTcpHeader header;
	char *ptr_hdr = (char*)&header;

	memset(&header, 0, sizeof(header));

	while (!g_ServerShutdown && !m_TcpThreadTerminated)
	{
		while (m_TcpThreadRunning)
		{
			// Do the key exchange if it hasn't been done yet
			if (!m_KeysExchanged)
			{
				if (!RunKeyExchange())
				{
					// Key Exchange failed.
					KillConnection();
					break;
				}
				else
				{
					m_KeysExchanged = true;
				}
			}

			// Main Loop

			isSocketReady = SocketReady(1);		// One Second Timeout

			if ( isSocketReady && (recv(m_Socket, ptr_hdr, 4, 0) == 4) )
			{
				m_InactivityTimer = 0;	
				
				if (m_ServerType != CONNECTION_TYPE_GLOBAL_PROXY_TO_SERVER)
				{
					m_CryptIn.RC4((unsigned char *) &header, 4);
				}

				bytes	= header.size - sizeof(EnbTcpHeader);	// Bytes to fetch
				opcode	= header.opcode;			// Opcode for this packet

				// This buffer check MUST be in place
				if ( (bytes > 4096) )
				{
					LogMessage("[%d] Received packet with incorrect payload length: opcode = 0x%02x, length = %d. Aborting.\n", m_RecvThreadHandle, opcode, bytes);
					
					KillConnection();			// We're not permitting a 2nd chance.
					break;
				}

				//LogMessage("Received packet: opcode = 0x%02x, length = %d\n", opcode, bytes);
				
				received = recv(m_Socket, (char *) m_RecvBuffer, bytes, 0);
				numretries = 0;

				while ( received != bytes && 			// Did we fetch everything?
					SocketReady() && 			// Can we still get more?
					numretries < MAX_RETRIES &&		// Can we still retry to get more?
					m_TcpThreadRunning )			// And we haven't been told to stop?
				{
					int rcv = recv(m_Socket, (char *) (m_RecvBuffer + received), bytes - received, 0);
					if (rcv > 0)
					{
						received += rcv;
					}
					else break;

					numretries++;				// Prevent an infinite loop
				}

				if ( received == bytes && m_TcpThreadRunning )	// We got the whole package and we haven't been told top stop
				{
					if (m_ServerType != CONNECTION_TYPE_GLOBAL_PROXY_TO_SERVER)
					{
						m_CryptIn.RC4(m_RecvBuffer, bytes);
					}

					switch (m_ServerType)
					{
						case CONNECTION_TYPE_CLIENT_TO_GLOBAL_SERVER :
							ProcessGlobalServerOpcode(opcode, bytes);
							break;
						
						case CONNECTION_TYPE_GLOBAL_PROXY_TO_SERVER:
							ProcessProxyGlobalOpcode(opcode, bytes);
							break;

						default:
							LogMessage("[%d] ERROR: Unknown type of connection.\n", m_RecvThreadHandle);
							m_TcpThreadRunning = false; // Shouldn't happen (but was able to with a buffer overflow).
							break;
					}
				}
				else
				{
					// Error Stage

					if (m_TcpThreadRunning) // We weren't told to stop, but never got our whole packet
					{
						LogMessage("[%d] Error receiving TCP packet on port %d, got %d bytes, expecting %d -- aborting!\n", m_RecvThreadHandle,m_TcpPort, received, bytes);
					}

					m_TcpThreadRunning = false;
				}
			}
			else
			{
				// Check Connection Status
				DWORD error = WSAGetLastError();
				if ( !isSocketReady && error == 0 )
				{
					m_InactivityTimer++;

					if ( (m_MaxInactivityTime > 0) && (m_InactivityTimer > m_MaxInactivityTime) )
					{
						LogMessage("[%d] TCP connection on port %d exceeded maximum inactivity. Aborting.\n", m_RecvThreadHandle, m_TcpPort);

						m_TcpThreadRunning = false;
					}
				}
				else
				{
					switch (error)
					{
						case 0:
							LogMessage("[%d] TCP connection on port %d gracefully closed\n", m_RecvThreadHandle, m_TcpPort);
							break;
						case WSAECONNRESET:
							LogMessage("[%d] TCP connection on port %d was reset\n", m_RecvThreadHandle, m_TcpPort);
							break;
						default:
							LogMessage("[%d] TCP error on port %d (Error %d). Aborting.\n", m_RecvThreadHandle, m_TcpPort, error);
							break;
					}
					
					m_TcpThreadRunning = false;
				}
			}
		}

		KillConnection();		

		// m_TcpThreadRunning was set to false. Go to sleep...		
		SuspendThread(m_RecvThreadHandle);
	}

	m_TcpThreadTerminated = true;
}

// Kills an active connection on the Socket, if any, and suspends the thread
void Connection_B::KillConnection()
{
	if ( m_Socket != INVALID_SOCKET )
	{
		LogMessage("[%d] Closing Socket on port %d\n", m_RecvThreadHandle, m_TcpPort);

		shutdown(m_Socket, 2);	// Important!
		closesocket(m_Socket);
		m_Socket = INVALID_SOCKET;
	}

	m_SendQueue->ResetQueue();

	// No longer active, not running, Keys invalidated
	m_TcpThreadRunning	= false;
	m_ConnectionActive	= false;
	m_KeysExchanged		= false;
	m_AvatarID			= 0;
}

void Connection_B::PulseConnectionOutput(int max_cycles)
{
	int length;
	int cumulative = 0;
	int cycles = 0;
	long player_id;

	if (!m_ConnectionActive || !m_TcpThreadRunning) return;

	while ((cycles < max_cycles) && m_SendQueue->CheckQueue((m_SendBuffer2+cumulative), &length, (SEND_BUFFER_SIZE - cumulative), &player_id) )
	{
		//check opcode length written
		cumulative += length;
		if (m_SendQueue->CheckNextQueueSize() >= (SEND_BUFFER_SIZE - cumulative))
		{
			break;
		}
		cycles++;
	}

	if (cumulative > 0)
	{
		send(m_Socket, (char *) m_SendBuffer2, cumulative, 0);
	}
}

long Connection_B::GameID()			
{ 
	return m_AvatarID; 
}
#else // !WIN32 — Phase J Linux stub (file-level WIN32 wall, see top of file)
// Intentionally empty on Linux. The Connection_B class is forward-declared
// in connection_B.h but its methods compile to nothing here. Any code path
// that needs Connection_B at runtime is WIN32-walled at its call sites.
#endif // WIN32 — Phase J file-level guard
