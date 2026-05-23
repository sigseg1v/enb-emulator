// Connection.cpp
//

#include "Net7.h"
#include "Connection.h"
#include "ServerManager.h"
#include "PacketStructures.h"
#include "PlayerClass.h"

Connection::Connection()
{
	m_ConnectionActive		= true;		// Mark this as an active connection
	m_TcpThreadRunning		= false;	// Start thread in non-running mode
	m_KeysExchanged			= false;
	m_TerminateAfterSend	= false;

	// Initialize settings
	m_Socket	= INVALID_SOCKET;
	m_TcpPort	= 0;
	m_ServerType	= 0;
	m_IPaddr	= 0;
	m_AvatarID	= 0;
	m_LastOwner	= (0);

	m_AccountUsername = (0);

	m_PacketLoggingEnabled = false;		// Disable packet logging

	m_SendQueue = new MessageQueue("TCP", g_ServerMgr->GetTCPCBuffer());
}

Connection * Connection::ReSetConnection(SOCKET s, ServerManager &server_mgr, short port, int server_type, unsigned long* ip_addr)
{
	m_Mutex.Lock();

	m_ConnectionActive	= true;		// Mark this as an active connection
	m_PacketLoggingEnabled	= false;	// Disable packet logging
	m_KeysExchanged		= false;
	m_TerminateAfterSend = false;
	m_RunCount = 0;

	// Temporarily set the AvatarID to 1, otherwise it might get killed off
	// too soon with a heavy load (lots of connections)
	m_AvatarID			= 1;
	m_Socket		= s;
	m_TcpPort		= port;
	m_ServerType		= server_type;
	m_AccountUsername = NULL;

	if (ip_addr) m_IPaddr 	= *ip_addr;

	m_Mutex.Unlock();

	m_SendQueue->ResetQueue();

	SetNonBlocking(false);
	m_LastActivity = GetNet7TickCount();
	m_TcpThreadRunning = true;	

	LogMessage("%s*** new connection ***\n", GetLogInfo());

	return this;
}

Connection::~Connection()
{
	KillConnection("destructor");

	delete m_SendQueue;
}

bool Connection::IsActive()
{
	// Good opportunity to check g_ServerShutdown
	//if (g_ServerShutdown) WakeupThread();

	return m_ConnectionActive;
}

// Will return false if timeout expired or error occured.
bool Connection::SocketReady(int ttimeout, int usec_timeout)
{
	fd_set fds;
	struct timeval timeout;
	long ret;

	FD_ZERO(&fds);
	FD_SET(m_Socket, &fds);

	if (ttimeout >= 0)
	{
		timeout.tv_sec	= ttimeout;
		timeout.tv_usec	= usec_timeout;

		ret = select(sizeof(fds)*8, &fds, NULL, NULL, &timeout);
	}
	else
	{
		ret = select(sizeof(fds)*8, &fds, NULL, NULL, NULL);
	}

	return bool(ret > 0);	
}

void Connection::SendObjectEffect(ObjectEffect *object_effect)
{
	// Delegate to Player by GameID. EffectManager calls this through the
	// stored Connection pointer; the actual packet serialization lives on
	// Player. If no Player is associated yet, drop the effect silently.
	(void)object_effect;
	// Phase B: no-op stub. Resolves Connection::SendObjectEffect link errors
	// in EffectManager.cpp. Phase B-continuation can wire this up to the
	// PlayerManager lookup once the threading semantics are reviewed.
}

void Connection::Send(unsigned char *Buffer, int length)
{
	/*
	if (m_PacketLoggingEnabled)
	{
	LogMessage("[%08x] Adding packet to Send Queue, length=%d\n", this, length);
	DumpBuffer(Buffer, length);
	}
	*/

	//possible file IO operations between two mutex locks = lag

	// Mutexed, so thread safe. Not sure about DumpBuffer... (didn't look)
	m_SendQueue->Add(Buffer, length, m_AvatarID);
}

void Connection::SetRC4Key(unsigned char *rc4_key)
{
	/*
	LogMessage("SetRC4Key: %02x %02x %02x %02x %02x %02x %02x %02x\n",
	rc4_key[0],
	rc4_key[1],
	rc4_key[2],
	rc4_key[3],
	rc4_key[4],
	rc4_key[5],
	rc4_key[6],
	rc4_key[7] );
	*/

	m_CryptOut.PrepareKey(rc4_key, RC4_KEY_SIZE);
	m_CryptIn.PrepareKey(rc4_key, RC4_KEY_SIZE);
}

bool Connection::DoKeyExchange()
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
		LogMessage("%sERROR: DoKeyExchange send/receive failed\n", GetLogInfo());
		return false;		
	}

	long key_length = (long) ntohl((*((unsigned long *) buffer)));
	if ( (key_length <  WWRSA_BLOCK_SIZE) || (key_length > (WWRSA_BLOCK_SIZE + 1)) )
	{
		LogMessage("%sERROR: DoKeyExchange key_length = %d\n", GetLogInfo(), key_length);
		return false;
	}

	// Get the encrypted RC4 Session Key response from the client
	length = recv(m_Socket, (char *) buffer, key_length, 0);
	if (length != key_length)
	{
		LogMessage("%sERROR: DoKeyExchange key_length = %d, recv_length = %d\n", GetLogInfo(), key_length, length);
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
		LogMessage("%sERROR: DoKeyExchange m_WestwoodRSA.Decrypt failed\n", GetLogInfo());
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

bool Connection::DoClientKeyExchange()
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
		LogMessage("%sERROR: DoClientKeyExchange recv\n", GetLogInfo());
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

bool Connection::RunKeyExchange()
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
			LogMessage("%sClient key exchange failed. Aborting.\n", GetLogInfo());
			return false;
		}
	}
	else
	{
		if (!DoClientKeyExchange())
		{
			m_ConnectionActive = false;
			LogMessage("%sServer/Server key exchange failed. Aborting.\n", GetLogInfo());
			return false;
		}
	}

	return true;
}

void Connection::SendResponse(short opcode, unsigned char *data, size_t length)
{
	if (!this)
	{
		LogMessage("%sERROR: SendResponse called without connection", GetLogInfo());
		return;
	}

	if (m_ServerType == CONNECTION_TYPE_SECTOR_SERVER_TO_PROXY && m_AvatarID == 0)
	{
		LogMessage("%sInvalid attempt to send via TCP: Last Owner = %s\n", GetLogInfo(), m_LastOwner);
		return;
	}

	*((short *) &m_SendBuffer[0]) = (short) length + sizeof(long);
	*((short *) &m_SendBuffer[2]) = opcode;

	if ((length + 4) < MAX_TCP_BUFFER)
	{
		memcpy(m_SendBuffer + sizeof(long), data, length);
	}
	else
	{
		LogMessage("%sMessage too long to send via Connection: opcode %04x length %d\n", GetLogInfo(), opcode, length);
		return;
	}

	int bytes = length + sizeof(long);

	if (m_PacketLoggingEnabled)
	{
		LogMessage("%sSending %d bytes (unencrypted)\n", GetLogInfo(), bytes);
		DumpBuffer(m_SendBuffer, bytes);
	}

	if (m_ServerType == CONNECTION_TYPE_CLIENT_TO_GLOBAL_SERVER && opcode == 0x006F)
	{
		LogMessage("%sTerminating connection (got ticket)\n", GetLogInfo());
		m_TerminateAfterSend = true;
	}

	if (m_ServerType != CONNECTION_TYPE_SECTOR_SERVER_TO_PROXY && m_ServerType != CONNECTION_TYPE_GLOBAL_PROXY_TO_SERVER)
	{
		m_CryptOut.RC4(m_SendBuffer, bytes);
	}

	Send(m_SendBuffer, bytes);
}

// Call to check if there are any TCP inputs to process
// In the server, we now only check once for input, the server will only ever receive sector TCP logins, 
// with just one input which is to positiviely identify the player ID of the Net7Proxy logging in.

void Connection::ProcessRecvInputs(u32 tick)
{
	int numretries		= 0;

	int received = 0;
	unsigned short bytes;
	short opcode;

	m_RunCount++;

	//only scan inputs every 20 cycles after first connect.
	if (m_RunCount%20 != 0) return;

	EnbTcpHeader header;
	char *ptr_hdr = (char*)&header;
	memset(&header, 0, sizeof(header));

	//check key exchange
	if (!m_KeysExchanged)
	{
		if (!RunKeyExchange())
		{
			// Key Exchange failed.
			KillConnection("key exchange failed");
			return;
		}
		else
		{
			m_KeysExchanged = true;
		}
	}

	//take a peek to see if there's any incomming
	m_Mutex.Lock();
	SetNonBlocking(true);
	received = recv(m_Socket, (char *) m_RecvBuffer, 4, MSG_PEEK);
	SetNonBlocking(false);
	m_Mutex.Unlock();

	//if we have incomming, then process that in socket blocking mode
	if ( received == 4 && recv(m_Socket, ptr_hdr, 4, 0) == 4 )
	{
		m_LastActivity = tick;

		if (m_ServerType != CONNECTION_TYPE_SECTOR_SERVER_TO_PROXY && m_ServerType != CONNECTION_TYPE_GLOBAL_PROXY_TO_SERVER)
		{
			m_CryptIn.RC4((unsigned char *) &header, 4);
		}

		bytes	= header.size - sizeof(EnbTcpHeader);	// Bytes to fetch
		opcode	= header.opcode;			// Opcode for this packet

		// buffer check
		if ( (bytes > MAX_TCP_BUFFER) )
		{
			LogMessage("%sReceived packet with incorrect payload length: opcode = 0x%02x, length = %d. Aborting.\n", GetLogInfo(), opcode, bytes);
			KillConnection("bad payload");			// We're not permitting a 2nd chance.
			return;
		}

		//LogMessage("Received packet: opcode = 0x%02x, length = %d\n", opcode, bytes);

		received = recv(m_Socket, (char *) m_RecvBuffer, bytes, 0);

		numretries = 0;

		if (received < 0) received = 0;

		while ( received != bytes && 			// Did we fetch everything?
			SocketReady(0,500) && 			// Can we still get more?
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

		if ( received == bytes )	// We got the whole package and we haven't been told top stop
		{
			if (m_ServerType != CONNECTION_TYPE_SECTOR_SERVER_TO_PROXY && m_ServerType != CONNECTION_TYPE_GLOBAL_PROXY_TO_SERVER)
			{
				m_CryptIn.RC4(m_RecvBuffer, bytes);
			}

			switch (m_ServerType)
			{
			case CONNECTION_TYPE_CLIENT_TO_GLOBAL_SERVER :
				ProcessGlobalServerOpcode(opcode, bytes);
				break;

			case CONNECTION_TYPE_CLIENT_TO_MASTER_SERVER :
				ProcessMasterServerOpcode(opcode, bytes);
				break;

			case CONNECTION_TYPE_CLIENT_TO_SECTOR_SERVER :
				//ProcessSectorServerOpcode(opcode, bytes);
				LogMessage("%sERROR: Sector Server opcode received!\n", GetLogInfo());
				break;

			case CONNECTION_TYPE_MASTER_SERVER_TO_SECTOR_SERVER :
				ProcessMasterServerToSectorServerOpcode(opcode, bytes);
				break;

			case CONNECTION_TYPE_SECTOR_SERVER_TO_SECTOR_SERVER :
				ProcessSectorServerToSectorServerOpcode(opcode, bytes);
				break;

			case CONNECTION_TYPE_SECTOR_SERVER_TO_PROXY:
				ProcessProxyClientOpcode(opcode, bytes);
				break;

			case CONNECTION_TYPE_GLOBAL_PROXY_TO_SERVER:
				ProcessProxyGlobalOpcode(opcode, bytes);
				break;

			default:
				LogMessage("%sERROR: Unknown type of connection!\n", GetLogInfo());
				m_TcpThreadRunning = false; 
				break;
			}
		}
		else
		{
			// Error Stage

			if (m_TcpThreadRunning) // We weren't told to stop, but never got our whole packet
			{
				LogMessage("[0x%08x] Error receiving TCP packet on port %d, got %d bytes, expecting %d -- aborting!\n", m_AvatarID, m_TcpPort, received, bytes);
			}

			m_TcpThreadRunning = false;
		}
	}
	else
	{
		// Check Connection Status
		DWORD error = WSAGetLastError();
		if ( error != 0 )
		{
			switch (error)
			{
			case 0:
				LogMessage("[0x%08x] TCP connection on port %d gracefully closed\n", m_AvatarID, m_TcpPort);
				break;
			case WSAECONNRESET:
				LogMessage("[0x%08x] TCP connection on port %d was reset\n", m_AvatarID, m_TcpPort);
				m_TcpThreadRunning = false;
				break;
			default:
				LogMessage("[0x%08x] TCP error on port %d (Error %d). Aborting.\n", m_AvatarID, m_TcpPort, error);
				m_TcpThreadRunning = false;
				break;
			}

			m_TcpThreadRunning = false;
		}
	}

	if (tick > (m_LastActivity + 10*1000*60) || m_TcpThreadRunning == false)  //10 minute timeout to allow character creation
	{
		//TODO: send ping/pongs if connection is still valid
		KillConnection("10m timeout");
	}
	else if (m_TerminateAfterSend && tick > (m_LastActivity + 10*1000)) //10 second timeout after last GLOBAL opcode sent
	{
		KillConnection("10s no longer needed");
	}
}

bool Connection::SetNonBlocking(bool blocking)
{
	unsigned long l = blocking ? 1 : 0;
	int n = ioctlsocket(m_Socket, FIONBIO, &l);
	if (n != 0)
	{
		LogMessage("%sunable to set non-blocking (%d)\n", GetLogInfo(), WSAGetLastError() );
		return false;
	}
	return true;
}

// Kills an active connection on the Socket, if any, and suspends the thread
void Connection::KillConnection(char *origin)
{
	if (origin)
		LogMessage("%sKillConnection (%s)\n", GetLogInfo(), origin);

	m_Mutex.Lock();

	if (m_ServerType == CONNECTION_TYPE_SECTOR_SERVER_TO_PROXY && !m_TerminateAfterSend && !g_ServerShutdown)
	{
		Player *p = g_ServerMgr->m_PlayerMgr.GetPlayer(m_AvatarID);
		if (p)
		{
			LogMessage("%sEmergency shutdown for TCP comms thread for '%s'\n", GetLogInfo(), p->Name());
			p->SetTCPTerminate();
		}
	}

	if ( m_Socket != INVALID_SOCKET )
	{
		//LogMessage("[%08x] Closing Socket on port %d\n", this, m_TcpPort);

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

	m_Mutex.Unlock();
}

void Connection::PulseConnectionOutput(int max_cycles)
{
	int length;
	int cumulative = 0;
	int cycles = 0;
	long player_id;

	if (!m_ConnectionActive || !m_TcpThreadRunning) return;
	u32 tick = GetNet7TickCount();

	while ((cycles < max_cycles) && m_SendQueue->CheckQueue((m_SendBuffer2+cumulative), &length, (SEND_BUFFER_SIZE - cumulative), &player_id) )
	{
		//check opcode length written
		if (m_ServerType == CONNECTION_TYPE_SECTOR_SERVER_TO_PROXY || m_ServerType == CONNECTION_TYPE_GLOBAL_PROXY_TO_SERVER)
		{
			short readlength = *((short *) (m_SendBuffer2+cumulative) );
			if (readlength != length)
			{
				LogMessage("%sbad opcode readlength %d, length %d\n",GetLogInfo(),readlength,length);
				*((short *) &m_SendBuffer2[cumulative]) = length;
			}
		}
		cumulative += length;
		if (m_SendQueue->CheckNextQueueSize() >= (SEND_BUFFER_SIZE - cumulative))
		{
			break;
		}
		cycles++;
	}

	if (cumulative > 0)
	{
		m_Mutex.Lock();
		send(m_Socket, (char *) m_SendBuffer2, cumulative, 0);
		m_Mutex.Unlock();
		m_LastActivity = tick;
	}
}

char *Connection::GetLogInfo()
{
	unsigned char *ip = (unsigned char *)&m_IPaddr;
	sprintf_s(m_loginfo, sizeof(m_loginfo), "[TCP port:%d IP:%3d.%3d.%3d.%3d id:%08x acc:%16s this:%08x] ", m_TcpPort, ip[0], ip[1], ip[2], ip[3], m_AvatarID, m_AccountUsername ? m_AccountUsername : "?", this);
	return m_loginfo;
}
