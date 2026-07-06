/* Net-7 Entertainment: Net-7 Earth and Beyond emulator project
**
** This code/content is licensed under the Creative Commons license, it is interactive content. You can view the terms of our:
** Creative Commons Attribution-Noncommercial-Share Alike 3.0 United States License
** http://creativecommons.org/licenses/by-nc-sa/3.0/us/
**
** Net-7 Emulator Project, an Earth & Beyond emulator by Net7 Entertainment is licensed under a Creative Commons Attribution-Noncommercial-Share Alike 3.0 United States License
**
** Based on a work at http://www.earthandbeyond.com
**
** Permissions beyond the scope of this license may be available at http://www.dreamersofdawn.org/docs/More_Information.htm
**
** The license can be modified at our discretion within the bounds of Creative Commons at any time.
**
** Copyright of our assets/code/software began in 2005-2009 ©, Net-7 Entertainment.
**
*/


#include "Net7.h"

#include "ServerManager.h"
#include "UDPConnection.h"
#include <net7/PacketStructures.h>
#include "PlayerClass.h"
#include <net7/Opcodes.h>
#include <net7/DtlsTransport.h>
#include "AuthWrapper.h"   // Phase AH: shared CheckAuthWrapper decision (also unit-tested)

// Entry point handed to pthread_create.
void * LaunchUDPRecvThread(void *arg)
{
    ((UDP_Connection *) arg)->RunRecvThread();
    return NULL;
}

//make a send and receive socket, and start a send and receive thread
UDP_Connection::UDP_Connection(unsigned short port, ServerManager *server_mgr, int server_type)
	: m_ServerType(server_type)
{
	//m_Send_Socket = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
	m_Socket = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
	m_Port = port;
	m_ServerMgr = server_mgr;
	m_Sector_Operational = true; //false
	m_SectorID = 0;
	m_LoginAttempts = 0;
	m_Socket_Reset_Required = false;
	m_SSLIPAddr = 0;
	m_SSLPort = 0;
	m_Dtls = nullptr;   // attached in StartReceiver(), after the DTLS policy is set

	if(m_Socket == INVALID_SOCKET)
	{
		LogMessage("Invalid Socket %d for UDP connection.\n", errno);
		return;
	}

	UDP_SetBroadcast(m_Socket);

	if (!UDP_BindPort(m_Port, m_Socket))
	{
		m_Socket = INVALID_SOCKET;
		return;
	}

	m_ThreadRunning = false;

	// NOTE: the receiver thread is intentionally NOT spawned here. Callers
	// must invoke StartReceiver() once they are ready to dispatch packets.
	// See class doc-comment in UDPConnection.h and the deferred-start
	// sequence in ServerManager::Run.
}

void UDP_Connection::StartReceiver()
{
	if (m_Socket == INVALID_SOCKET)
	{
		LogMessage("UDP_Connection::StartReceiver: socket invalid, not starting recv thread.\n");
		return;
	}

	// Phase AH: attach the per-listener DTLS transport here (not in the ctor) so
	// it is created AFTER ServerManager::InitDtlsServerPolicy has run, regardless
	// of when this connection object was constructed (the MVAS listener is built
	// before RunServer). Returns nullptr when plaintext is explicitly opted out,
	// in which case the recv/send paths run cleartext exactly as before.
	if (m_ServerMgr && !m_Dtls)
		m_Dtls = m_ServerMgr->MakeServerDtlsTransport();

	pthread_create(&m_Thread, NULL, &LaunchUDPRecvThread, (void *) this);
}

// Phase AI Stage 2: surrender ownership of the DTLS transport so a park (which
// deletes this UDP_Connection) does NOT tear down the per-peer SSL associations.
// The caller (SectorManager::DropListener) stashes the returned transport and
// re-installs it via AdoptDtls on the rebound listener. Safe only when no recv/
// send thread is in flight (StopReceiver has joined the recv thread and the
// sector event thread is already stopped by TeardownSector before DropListener).
net7::DtlsTransport* UDP_Connection::DetachDtls()
{
	net7::DtlsTransport *d = m_Dtls;
	m_Dtls = nullptr;
	return d;
}

// Install a transport detached from the pre-park listener. Must be called BEFORE
// StartReceiver(), whose `if (!m_Dtls) m_Dtls = MakeServerDtlsTransport()` then
// sees a non-null m_Dtls and reuses the adopted associations instead of minting
// a fresh (and, to the proxy, unknown) transport.
void UDP_Connection::AdoptDtls(net7::DtlsTransport *dtls)
{
	m_Dtls = dtls;
}

UDP_Connection::~UDP_Connection()
{
//	LogMessage("UDP connection terminated.\n");

#ifdef WIN32
	shutdown(m_Socket, SD_BOTH);
#else
	shutdown(m_Socket, SHUT_RDWR);
#endif
	closesocket(m_Socket); // unblock the recv
	m_Socket = INVALID_SOCKET;
	int timeout = 0; // port 3702 is not closing properly!
	while (m_ThreadRunning && timeout < 100)
	{
		usleep(1 * 1000);
		timeout++;
	}

	delete m_Dtls;   // Phase AH: tears down all per-peer SSL associations
	m_Dtls = nullptr;
}

unsigned long GetLocalAddr()
{
	struct hostent* hp;
	char localname[MAX_PATH];
	unsigned long addr = 0;


#ifdef WIN32
	if (SOCKET_ERROR != gethostname(localname, MAX_PATH))
#else
	if (0 != gethostname(localname, MAX_PATH))
#endif
	{
		hp = gethostbyname(localname);
		if (hp != NULL)	
		{
			strcpy_s(localname, sizeof(localname), hp->h_name);
			// Phase K Wave 12: h_addr_list[0] is 4B IPv4 — `unsigned long*` reads
			// 8B on Linux which corrupts addr.
			addr = *((uint32_t *) hp->h_addr_list[0]);
		}
	}

	return addr;
}

bool UDP_Connection::UDP_BindPort(short port, SOCKET socket)  
{
	sockaddr_in localAddr;
	unsigned long addr;
    
	memset(&localAddr, 0, sizeof(localAddr));
	localAddr.sin_family = AF_INET;

	addr = GetLocalAddr();

	localAddr.sin_addr.s_addr = addr; //INADDR_ANY;
	localAddr.sin_port = htons(port);

	
	if (bind(socket, (sockaddr *) &localAddr, sizeof(sockaddr_in)) < 0) 
	{
		LogMessage("Listener unable to bind to UDP on %s:%d\n", inet_ntoa(localAddr.sin_addr), port);
		return false;
	}

	//LogMessage("Listening for incoming UDP connections on %s:%d\n", inet_ntoa(localAddr.sin_addr), port);

	return true;
}

unsigned long UDP_Connection::checksum(char *buffer, int size) 
{ 
	unsigned long cksum=0; 

	while(size > 1) { cksum+=*buffer++; size -= 2; }
	if(size) cksum += *(unsigned char*)buffer;
 
	cksum = (cksum >> 16) + (cksum & 0xffff); 
	cksum += (cksum >>16); 
	return (~cksum); 
}

void UDP_Connection::UDP_SetBroadcast(SOCKET socket) 
{
	int broadcastPermission = 1;
	if (setsockopt(socket, SOL_SOCKET, SO_BROADCAST, (char *)&broadcastPermission, sizeof(broadcastPermission)) < 0)
	{
		LogMessage("Failed to set UDP Broadcast Option\n");
	}
}

// Phase AH (AH-9): rate-limited log for dropped C->S datagrams that failed the
// per-packet auth wrapper check. A flood of these means a malformed/forged
// client or a proxy<->server version skew; we log at most one summary line per
// 5s so an attacker cannot spam the log. The token itself is NEVER logged.
static void LogAuthWrapperDrop(const char *reason, long source_addr)
{
	static time_t       s_last       = 0;
	static unsigned long s_suppressed = 0;
	time_t now = time(NULL);
	s_suppressed++;
	if (now - s_last >= 5)
	{
		unsigned char *ip = (unsigned char *)&source_addr;
		LogMessage("DTLS auth wrapper: dropped %lu C->S datagram(s) [%s] "
			"(last from %u.%u.%u.%u)\n",
			s_suppressed, reason, ip[0], ip[1], ip[2], ip[3]);
		s_last = now;
		s_suppressed = 0;
	}
}

void UDP_Connection::RunRecvThread()
{
	long source_addr;
	unsigned short source_port;
	int received;

	if (m_Port <= 0)
		LogMessage("UDP_Connection: RunRecvThread - Port not set\n");

	if (m_Socket == INVALID_SOCKET)
		LogMessage("UDP_Connection: RunRecvThread - Invalid Socket\n");

	if (m_ThreadRunning)
		LogMessage("UDP_Connection: RunRecvThread - Thread already running\n");

	m_ThreadRunning = true;

	//LogMessage("Receive thread running.\n");

	while (!g_ServerShutdown && m_ThreadRunning)
	{
		received = UDP_RecvS((char*)m_RecvBuffer, MAX_BUFFER, source_addr, source_port);

		if (received != -1)
		{
			if (m_Dtls)
			{
				// Phase AH: the datagram is a DTLS record (handshake or encrypted
				// app data). Feed it to the per-peer association; emit any
				// handshake bytes it produced, then dispatch each decrypted app
				// datagram exactly as the cleartext path would.
				uint64_t key = net7::DtlsPeerKey((uint32_t)source_addr, source_port);
				net7::DtlsStep step = m_Dtls->Feed(key, m_RecvBuffer, received);
				if (!step.to_send.empty())
					RawSendTo((const char*)step.to_send.data(), (int)step.to_send.size(),
						source_addr, source_port);
				if (step.fatal)
					m_Dtls->ForgetPeer(key);
				for (auto &rec : step.app_data)
				{
					// Phase AH (AH-8/AH-9): every DTLS app datagram on the C->S
					// leg is wrapped with a 17-byte per-packet auth wrapper. The
					// accept/drop decision lives in net7::CheckAuthWrapper
					// (server/src/AuthWrapper.h) so the runtime and the unit test
					// (tests/server/protocol/auth_wrapper_test.cpp) share one
					// implementation. On Accept it hands back the inner
					// [EnbUdpHeader][payload], byte-for-byte what the handlers
					// (and the plaintext CLI path) already expect. The gameplay
					// token check (player_id != 0) is keyed on the GameID's bound
					// token; pre-auth datagrams (player_id == 0) skip it -- they
					// precede ticket-learning and are gated by ticket validation.
					const unsigned char *inner = nullptr;
					int inner_len = 0;
					net7::AuthWrapperVerdict verdict = net7::CheckAuthWrapper(
						rec.data(), rec.size(), MAX_BUFFER,
						[](int32_t player_id, unsigned char out[net7::kAuthTokenLen]) -> bool {
							Player *p = g_ServerMgr->m_PlayerMgr.GetPlayer(player_id);
							if (!p || !p->AuthTokenSet())
								return false;
							memcpy(out, p->AuthToken(), net7::kAuthTokenLen);
							return true;
						},
						&inner, &inner_len);

					if (verdict != net7::AuthWrapperVerdict::Accept)
					{
						// Rate-limited; never logs the token (see AuthWrapperReason).
						LogAuthWrapperDrop(net7::AuthWrapperReason(verdict), source_addr);
						continue;
					}

					memcpy(m_RecvBuffer, inner, inner_len);
					DispatchDatagram(m_RecvBuffer, inner_len, source_addr, source_port);
				}
			}
			else
			{
				DispatchDatagram(m_RecvBuffer, received, source_addr, source_port);
			}
		}

		if (m_Socket_Reset_Required)
		{
			m_Socket_Reset_Required = false;
			LogMessage("WARNING: Socket loss, Attempting to reset socket\n");
			Reset_Socket();
		}
	}

	LogMessage("Stopping UDP Listener on port %d\n", m_Port);
	if (m_Socket != INVALID_SOCKET)
	{
		closesocket(m_Socket);
		m_Socket = INVALID_SOCKET;
	}

	m_ThreadRunning = false;
}

// Dispatch one cleartext datagram (plaintext mode, or a DTLS-decrypted app
// record) through the per-server-type opcode handlers. This is the body that
// used to live inline in RunRecvThread; extracted so DTLS can call it once per
// decrypted record.
void UDP_Connection::DispatchDatagram(unsigned char *buf, int received, long source_addr, unsigned short source_port)
{
	EnbUdpHeader *header = (EnbUdpHeader*)buf;

	unsigned short bytes = header->size - sizeof(EnbUdpHeader);
	char *msg = (char*)(buf + sizeof(EnbUdpHeader));
	buf[header->size] = 0;

	// Make sure CRC & Bytes match
	if (received != (int)(bytes + sizeof(EnbUdpHeader)))
		return;

	switch (m_ServerType)
	{
	case CONNECTION_TYPE_MVAS_TO_PROXY:
		HandleMVASOpcode(msg, header, source_addr, source_port);
		break;

	case CONNECTION_TYPE_GLOBAL_SERVER_TO_PROXY:
		HandleGlobalOpcode(msg, header, source_addr, source_port);
		break;

	case CONNECTION_TYPE_SECTOR_SERVER_TO_PROXY:
		HandleClientOpcode(msg, header, source_addr, source_port);
		break;

	case CONNECTION_TYPE_MASTER_SERVER_TO_PROXY:
		HandleMasterOpcode(msg, header, source_addr, source_port);
		break;

	default:
		LogMessage("Unknown reception opcode, port %d\n", m_Port);
		break;
	}
}

// Raw sendto that bypasses DTLS -- used only to put DTLS handshake bytes the
// transport produced (ServerHello/Finished/etc.) back on the wire. App traffic
// goes through UDP_Send, which encrypts when DTLS is active.
void UDP_Connection::RawSendTo(const char *buffer, int bufferLen, long IPaddr, short port)
{
	sockaddr_in lSockAddr;
	memset(&lSockAddr, 0, sizeof(lSockAddr));
	lSockAddr.sin_family = AF_INET;
	lSockAddr.sin_addr.s_addr = IPaddr;
	lSockAddr.sin_port = htons(port);

	if (sendto(m_Socket, buffer, bufferLen, 0, (sockaddr *) &lSockAddr, sizeof(lSockAddr)) != bufferLen)
	{
		LogMessage("DTLS raw send failed.\n");
	}
}

void UDP_Connection::Reset_Socket()
{
	closesocket(m_Socket);

	m_Socket = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);

	if(m_Socket == INVALID_SOCKET)
	{
		LogMessage("Socket Reset: Invalid Socket %d for UDP connection.\n", errno);
		return;
	}

	UDP_SetBroadcast(m_Socket);

	if (!UDP_BindPort(m_Port, m_Socket))
	{
		m_Socket = INVALID_SOCKET;
		return;
	}
}

void UDP_Connection::UDP_Send(const char *buffer, int bufferLen, long IPaddr, short port) 
{
	sockaddr_in lSockAddr;
	unsigned char *ip;

	memset(&lSockAddr, 0, sizeof(lSockAddr));
	lSockAddr.sin_family = AF_INET;
	lSockAddr.sin_addr.s_addr = IPaddr;
	lSockAddr.sin_port = htons(port);

	ip = (unsigned char *) &IPaddr;

	LogDebug("Sending signal to %d (%d.%d.%d.%d)\n",IPaddr, ip[0], ip[1], ip[2], ip[3]);

	if (m_Dtls)
	{
		// Phase AH: encrypt under the per-peer DTLS association. If the proxy has
		// not completed a handshake for this peer yet, SendApp has no association
		// and the datagram is dropped -- we never emit gameplay in cleartext when
		// DTLS is active. (The server never initiates a handshake; the proxy
		// always connects first, so an established peer is the normal case.)
		uint64_t key = net7::DtlsPeerKey((uint32_t)IPaddr, (uint16_t)port);
		net7::DtlsStep step = m_Dtls->SendApp(key, (const uint8_t*)buffer, bufferLen);
		if (step.fatal)
		{
			m_Dtls->ForgetPeer(key);
			LogDebug("DTLS send: no association for peer; dropped %d bytes\n", bufferLen);
			return;
		}
		if (!step.to_send.empty())
			RawSendTo((const char*)step.to_send.data(), (int)step.to_send.size(), IPaddr, port);
		return;
	}

	// Write out the whole buffer as a single message.
	if (sendto(m_Socket, buffer, bufferLen, 0, (sockaddr *) &lSockAddr, sizeof(lSockAddr)) != bufferLen)
	{
		LogMessage("Send failed.\n");
	}
}

void UDP_Connection::SetServerManager(ServerManager * server_mgr)
{
	m_ServerMgr = server_mgr;
}

int UDP_Connection::UDP_RecvS(char *buffer, int size, long &source_addr, unsigned short &source_port)  
{
	sockaddr_in clntAddr;



#ifdef WIN32
	int addrLen = sizeof(clntAddr);
#else
	socklen_t addrLen = sizeof(clntAddr);
#endif
	int rtn;

#ifdef WIN32
	if ((rtn = recvfrom(m_Socket, buffer, size, 0, (sockaddr *) &clntAddr, (int *) &addrLen)) < 0) 
#else
	if ((rtn = recvfrom(m_Socket, buffer, size, 0, (sockaddr *) &clntAddr, &addrLen)) < 0) 
#endif
	{
		if (!g_ServerShutdown)
		{
			//usleep(200 * 1000);
			uint32_t dwError = errno;
#ifdef WIN32
			if (dwError == WSAENOTSOCK)
#else
			if (dwError == ENOTSOCK)
#endif
			{
				//return to thread and perform a socket reset, see if this helps
				m_Socket_Reset_Required = true;
			}
			LogMessage("UDP Receive failed.\n");
		}
	}
	else
	{
		//LogMessage("Got Something!\n");
	}
	
	source_addr = clntAddr.sin_addr.s_addr;
	source_port = ntohs(clntAddr.sin_port);
	
	return rtn;
}

void UDP_Connection::SendOpcode(short opcode, unsigned char *data, size_t length, long player_ip, short port)
{
	unsigned char buffer[2060];

	EnbUdpHeader * header = (EnbUdpHeader *) &buffer[0];
	header->size = (short) length + sizeof(EnbUdpHeader);
	header->opcode = opcode;
	header->player_id = 0;
	header->packet_sequence = 0;

	if ((length + sizeof(EnbUdpHeader)) > 2060)
	{
		LogMessage("UDP send exceeds stack allocation! DEBUG ME: size = %d\n", (length + sizeof(EnbUdpHeader)));
	}
	else
	{
		if (length)
		{
			memcpy(buffer + sizeof(EnbUdpHeader), data, length);
		}
	}

	int bytes = length + sizeof(EnbUdpHeader);

	UDP_Send((const char *) buffer, bytes, player_ip, port);
}

void UDP_Connection::SendOpcode(short opcode, Player *p, unsigned char *data, size_t length, long player_ip, short port, long sequence_num)
{
	unsigned char *buffer = p->GetUDPBuffer();

	EnbUdpHeader * header = (EnbUdpHeader *) &buffer[0];
	header->size = (short) length + sizeof(EnbUdpHeader);
	header->opcode = opcode;
	header->player_id = p->GameID();
	header->packet_sequence = sequence_num;

	if ((length + sizeof(EnbUdpHeader)) > UDP_BUFFER_SEND_SIZE)
	{
		LogMessage("UDP send exceeds player buffer allocation! DEBUG ME: size = %d", (length + sizeof(EnbUdpHeader)));
	}
	else
	{
		if (length)
		{
			memcpy(buffer + sizeof(EnbUdpHeader), data, length);
		}
	}

	int bytes = length + sizeof(EnbUdpHeader);

    //LogMessage("Opcode: %04x, Length: %x\n", opcode, length);
    //DumpBuffer(buffer, bytes);

	UDP_Send((const char *) buffer, bytes, player_ip, port);
}

void UDP_Connection::Shutdown()
{
	closesocket(m_Socket);
	m_Socket = INVALID_SOCKET;
}

void UDP_Connection::StopReceiver()
{
	// The recv loop runs while (!g_ServerShutdown && m_ThreadRunning). At runtime
	// g_ServerShutdown is false, so clearing m_ThreadRunning is what asks it to
	// exit; closing the socket unblocks an in-flight recvfrom. Then a REAL join
	// (vs ~UDP_Connection's 100ms spin-and-give-up) guarantees the recv thread is
	// gone -- no handler can touch sector objects after we return. Guard on
	// m_ThreadRunning: if the thread was never started (m_Thread invalid) we must
	// not join it.
	if (!m_ThreadRunning)
		return;
	m_ThreadRunning = false;
	if (m_Socket != INVALID_SOCKET)
	{
#ifdef WIN32
		shutdown(m_Socket, SD_BOTH);
#else
		shutdown(m_Socket, SHUT_RDWR);
#endif
		closesocket(m_Socket);
		m_Socket = INVALID_SOCKET;
	}
	pthread_join(m_Thread, NULL);
}
