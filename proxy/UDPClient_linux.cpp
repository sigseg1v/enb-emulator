// UDPClient_linux.cpp
//
// Phase K Linux port of UDPClient. Two roles on Linux, one class:
//
//   * Master plane (peer = UDP_MASTER_SERVER_PORT 3808): drives
//     HandleMasterJoin -> SendMasterLogin -> wait for 0x2009 -> reply
//     ServerRedirect to the client.
//
//   * Global plane (peer = UDP_GLOBAL_SERVER_PORT 3810): drives
//     HandleGlobalConnect / HandleGlobalTicketRequest /
//     HandleCreateCharacter / HandleDeleteCharacter -> wait for
//     0x2003 / 0x2005 / 0x200C / 0x200E -> reply to the client.
//
// One UDPClient instance per plane, each connect()'d to its own peer.
// Linux's connected SOCK_DGRAM filters by (peer_addr, peer_port) on
// recv, which is exactly the property we need to keep the two RecvThread
// dispatchers from cross-talking.
//
// Win32 origin
// ------------
// The Win32 UDPClient is a ~1900 LOC class spanning 4 source files. The
// global plane there rides over TCP (Connection::SendResponse with
// m_ServerTCP obtained from EstablishTCPConnection(SSL_LOCALCERT_LOGIN_PORT)).
// Phase Q deleted the server-side TCP cluster that backed that endpoint,
// so on Linux the global plane is UDP — symmetric with the master plane.
//
// Phase K Wave 6 update: this file also covers the master-plane recv
// dispatch for in-game sector traffic. The server sends three classes
// of opcode at the master-plane UDPClient:
//
//   * 0x2010 DATA_FILE        -> SendClientDataFile        (UDPProxyToClient_linux.cpp)
//   * 0x2016 PACKET_SEQUENCE  -> SendPacketSequence(false) (UDPProxyToClient_linux.cpp)
//   * 0x201A PACKET_C_SEQUENCE-> SendPacketSequence(true)  (UDPProxyToClient_linux.cpp)
//   * default                  -> ProcessClientOpcode      (UDPProxyToClient_linux.cpp)
//
// The Win32 client-launcher code (read ENB.exe memory, MVAS keep-alives)
// remains WIN32-walled at file level in UDPProxyMVAS.cpp and is not
// ported — that's launcher-side concern, irrelevant to a server-side
// proxy.
//
// What this file defines
// ----------------------
//   - ctor (FixedPort path)               -- opens local UDP socket,
//                                            resolves NET7_GAME_SERVER_HOST
//                                            (default "server"),
//                                            connect()s to (server, port),
//                                            starts RecvThread
//   - OpenFixedPort(port, ip_addr)        -- POSIX socket + bind ephemeral
//                                            + connect to (ip_addr, port).
//                                            `port` is the PEER port; pass
//                                            UDP_MASTER_SERVER_PORT for the
//                                            master plane or
//                                            UDP_GLOBAL_SERVER_PORT for the
//                                            global plane.
//   - CreateFrom                          -- populate m_SockAddr
//   - RecvThread                          -- dispatches both master and
//                                            global plane opcodes (see
//                                            switch below)
//   - UDP_Send / UDP_RecvFromServer       -- send/recv on m_Listen_Socket
//   - SendResponse(port, op, ...)         -- prepend EnbUdpHeader + sendto;
//                                            port=0 sends to default peer
//   - WaitForResponse                     -- bounded retry loop
//   - Master plane: SendMasterLogin, MasterLoginConfirm, FixedClientComm
//   - Global plane: SendTicket, SendAvatarLogin, CreateCharacter,
//                   DeleteCharacter, SetAccountName, ValidateAccount,
//                   ReceiveAvatarList, AvatarLoginConfirm,
//                   CreateDeleteConfirm, ProcessGlobalError
//   - BlankTCPConnection                  -- no-op (Win32 launcher
//                                            machinery — kept for legacy
//                                            link symbols)
//
// Everything else (avatar list streaming to launcher, position updates,
// MVAS keep-alives, packet-sequence resend, custom opcodes, etc.) is
// launcher-side and not ported. If a future Linux code path references
// one of those methods directly, the linker will tell us — and that's
// the right signal to port the next slice.
//
// LICENSE
// -------
// New file authored for the consolidated preservation fork. No Net-7
// CC BY-NC-SA 3.0 header was carried over from the WIN32 sources
// because none of the code below is copied — it's a fresh POSIX
// re-implementation against the same UDPClient class declaration
// (UDPClient.h, which retains its original header).
//
// New code is contributed under the project default license
// (CC BY-NC-SA 3.0 -- LICENSES/enb-emulator).

#include "Net7.h"
#include "UDPClient.h"
#include "../freya/client-injection/ClientPositionShared.h"   // PB-2: in-client position-hook IPC contract (MIT, lives with the producer)
#include <net7/Opcodes.h>
#include <net7/PacketStructures.h>
#include <net7/DtlsTransport.h>
#include "PacketMethods.h"
#include "ServerManager.h"
#include "Connection.h"

// Sockets / POSIX / pthread headers are pulled in by Net7.h with platform
// guards (winsock2 on Win32, POSIX on Linux); only the platform-neutral
// libc headers need explicit re-includes here.
#include <errno.h>
#include <string.h>
#include <stdlib.h>

// ---------------------------------------------------------------------------
// Thread launcher
// ---------------------------------------------------------------------------
static void *LaunchUDPCRecvThreadLinux(void *arg)
{
    ((UDPClient *) arg)->RecvThread();
    return NULL;
}

// Defined further down (next to the MVAS keepalive impl); forward-declared
// here so FixedClientComm can spawn it.
static void *LaunchUDPCKeepaliveThreadLinux(void *arg);

// ---------------------------------------------------------------------------
// Address helpers
// ---------------------------------------------------------------------------
//
// Resolve the game-server host the proxy should hand MasterLogin to.
// In docker the service is reachable as "server"; for non-docker dev a
// developer can override with NET7_GAME_SERVER_HOST=... . We do NOT
// hardcode an IP.
static long ResolveGameServerIP(long fallback)
{
    const char *host = getenv("NET7_GAME_SERVER_HOST");
    if (!host || !*host)
    {
        host = "server";
    }

    struct addrinfo hints;
    memset(&hints, 0, sizeof(hints));
    hints.ai_family   = AF_INET;
    hints.ai_socktype = SOCK_DGRAM;

    struct addrinfo *res = NULL;
    int rc = getaddrinfo(host, NULL, &hints, &res);
    if (rc != 0 || !res)
    {
        LogMessage("UDPClient: getaddrinfo('%s') failed: %s (falling back to ip_addr arg)\n",
                   host, gai_strerror(rc));
        return fallback;
    }

    long ip = 0;
    for (struct addrinfo *p = res; p; p = p->ai_next)
    {
        struct sockaddr_in *sin = (struct sockaddr_in *) p->ai_addr;
        ip = (long) sin->sin_addr.s_addr;
        break;
    }
    freeaddrinfo(res);

    unsigned char *b = (unsigned char *) &ip;
    LogMessage("UDPClient: resolved game server '%s' -> %u.%u.%u.%u\n",
               host, b[0], b[1], b[2], b[3]);
    return ip;
}

// ---------------------------------------------------------------------------
// CreateFrom — populate m_SockAddr for sendto()
// ---------------------------------------------------------------------------
void UDPClient::CreateFrom(long ip_addr, short port)
{
    memset(&m_SockAddr, 0, sizeof(m_SockAddr));
    m_SockAddr.sin_family      = AF_INET;
    m_SockAddr.sin_addr.s_addr = (in_addr_t) ip_addr;
    m_SockAddr.sin_port        = htons(port);
}

// ---------------------------------------------------------------------------
// OpenFixedPort — POSIX UDP socket, bind to ephemeral on INADDR_ANY,
// connect() to (ip_addr, peer_port). The connect() lets us use send()/recv()
// without specifying the peer every time AND filters the recv stream to
// just this peer — which is the property that keeps the master-plane
// RecvThread and global-plane RecvThread from cross-talking.
//
// `port` is interpreted as the PEER UDP port (i.e. UDP_MASTER_SERVER_PORT
// or UDP_GLOBAL_SERVER_PORT). The historical Linux build hardcoded
// UDP_MASTER_SERVER_PORT here; we now honour the caller's choice so the
// global-plane UDPClient can be pointed at UDP_GLOBAL_SERVER_PORT (3810).
// A 0 falls back to UDP_MASTER_SERVER_PORT for back-compat.
// ---------------------------------------------------------------------------
bool UDPClient::OpenFixedPort(short port, long ip_addr)
{
    short peer_port = (port == 0) ? (short) UDP_MASTER_SERVER_PORT : port;

    m_Listen_Socket = ::socket(AF_INET, SOCK_DGRAM, 0);
    if (m_Listen_Socket == INVALID_SOCKET)
    {
        LogMessage("UDPClient: socket() failed: %s\n", strerror(errno));
        return false;
    }

    // Bind to an ephemeral port on INADDR_ANY so the kernel picks a free
    // port for us. This is the SOURCE port the server will reply to.
    struct sockaddr_in bind_addr;
    memset(&bind_addr, 0, sizeof(bind_addr));
    bind_addr.sin_family      = AF_INET;
    bind_addr.sin_addr.s_addr = htonl(INADDR_ANY);
    bind_addr.sin_port        = 0;

    if (::bind(m_Listen_Socket, (struct sockaddr *) &bind_addr,
               sizeof(bind_addr)) < 0)
    {
        LogMessage("UDPClient: bind() failed: %s\n", strerror(errno));
        ::close(m_Listen_Socket);
        m_Listen_Socket = INVALID_SOCKET;
        return false;
    }

    // Bound recv wait so RecvThread can run PumpPacketResend even when the
    // server has gone quiet. Without this the reliable-stream retry only
    // ever ran on ARRIVAL of further datagrams -- and after a sector
    // handoff the 0x003A burst is the final traffic, so a lost tail packet
    // stalled the client on the load screen forever.
#ifdef _WIN32
    DWORD recv_timeout = 250; // milliseconds
#else
    struct timeval recv_timeout;
    recv_timeout.tv_sec  = 0;
    recv_timeout.tv_usec = 250 * 1000;
#endif
    if (::setsockopt(m_Listen_Socket, SOL_SOCKET, SO_RCVTIMEO,
                     (const char *) &recv_timeout, sizeof(recv_timeout)) < 0)
    {
        // Non-fatal: recovery degrades to the arrival-driven path only.
        LogMessage("UDPClient: setsockopt(SO_RCVTIMEO) failed: %s\n",
                   strerror(errno));
    }

    // Resolve the game server's IP. Falls back to the ip_addr ctor arg
    // if NET7_GAME_SERVER_HOST is unset and the default 'server' name
    // doesn't resolve (e.g. running outside docker).
    long server_ip = ResolveGameServerIP(ip_addr);

    // Set the default peer; SendResponse(port=0, ...) routes here via
    // send() on the connected socket (or sendto() if unconnected).
    CreateFrom(server_ip, peer_port);

    if (!m_Unconnected)
    {
        if (::connect(m_Listen_Socket, (struct sockaddr *) &m_SockAddr,
                      sizeof(m_SockAddr)) < 0)
        {
            // connect() on a SOCK_DGRAM is just default-peer setup; if it
            // fails we can still sendto() explicitly. Log and continue.
            LogMessage("UDPClient: connect() warning: %s (continuing)\n",
                       strerror(errno));
        }
    }

    m_IPAddr = server_ip;

    // Report the bound source port for debugging.
    struct sockaddr_in actual;
    socklen_t actual_len = sizeof(actual);
    if (::getsockname(m_Listen_Socket, (struct sockaddr *) &actual,
                      &actual_len) == 0)
    {
        unsigned char *b = (unsigned char *) &server_ip;
        LogMessage("UDPClient: bound UDP %u (src) -> %u.%u.%u.%u:%d (%s peer)\n",
                   ntohs(actual.sin_port), b[0], b[1], b[2], b[3],
                   (int) peer_port,
                   m_Unconnected ? "unconnected default" : "connected");
    }

    // Phase AH: build this socket's client-role DTLS transport (or leave it
    // nullptr in opted-out plaintext mode) and proactively start the handshake
    // to the default peer (3808 master / 3810 global) so the association is
    // established before the first request rides over it.
    InitDtls();
    DtlsKickHandshake(0);

    return true;
}

// ---------------------------------------------------------------------------
// Constructor — FixedPort path only on Linux.
// ---------------------------------------------------------------------------
UDPClient::UDPClient(short port, short connection_type, long ip_addr,
                     bool unconnected)
{
    m_IPAddr               = ip_addr;
    m_Port                 = port;
    m_ConnectionType       = connection_type;
    m_Unconnected          = unconnected;
    m_logged_in            = false;
    m_global_account_rcv   = false;
    m_PlayerID             = 0;
    m_SectorID             = 0;
    memset(m_AccountName, 0, sizeof(m_AccountName));
    m_ClientPort           = 0;
    m_ClientIP             = 0;
    m_ConnectionActive     = false;
    m_AlternatePorts       = false;
    m_ServerTCP            = NULL;
    m_CurrentPacketNum     = 0;
    m_Resync               = true;
    m_PacketTimeout        = 0;
    m_PacketDropThisSession = 0;
    m_Packets.clear();
    m_SplitPacketLength    = 0;
    m_SlotIndex            = 0;
    m_SplitPacketStart     = 0;
    m_SplitPacketptr       = NULL;
    m_QueueBufferFill      = 0;
    m_GalaxyMapReceived    = false;
    m_StartReceived        = false;
    m_Start_ID             = 0;
    m_LoginComplete        = false;
    m_ticket               = NULL;
    m_ticket_length        = 0;
    m_PacketTimer          = 0;
    m_PacketResendTimer    = 0;
    m_KeepaliveStarted     = false;
    m_KeepaliveSeq         = 0;
    memset(m_LastFedPos, 0, sizeof(m_LastFedPos));
    memset(m_LastFedHeading, 0, sizeof(m_LastFedHeading));
    m_HaveFedOnce          = false;
    m_PosFeedSeq           = 0;
    m_PosIntakeSock        = INVALID_SOCKET;
    m_PosIntakeTried       = false;
    m_HavePosSample        = false;
    memset(m_PosSample, 0, sizeof(m_PosSample));
    memset(m_HeadingSample, 0, sizeof(m_HeadingSample));
    m_PosSampleTick        = 0;
    m_Listen_Socket        = INVALID_SOCKET;
    m_recv_thread_running  = false;
    m_Dtls                 = nullptr;

    memset(&m_Server_handoff, 0, sizeof(m_Server_handoff));
    memset(m_RecvBuffer, 0, sizeof(m_RecvBuffer));
    memset(m_SendBuffer, 0, sizeof(m_SendBuffer));
    memset(m_QueueBuffer, 0, sizeof(m_QueueBuffer));

    if (connection_type != CLIENT_TYPE_FIXED_PORT)
    {
        LogMessage("UDPClient(Linux): only CLIENT_TYPE_FIXED_PORT is supported (got %d)\n",
                   connection_type);
        return;
    }

    if (!OpenFixedPort(port, ip_addr))
    {
        LogMessage("UDPClient(Linux): OpenFixedPort failed; recv thread not started\n");
        return;
    }

    pthread_t tid;
    if (pthread_create(&tid, NULL, &LaunchUDPCRecvThreadLinux, this) == 0)
    {
        pthread_detach(tid);
    }
    else
    {
        LogMessage("UDPClient(Linux): pthread_create failed: %s\n", strerror(errno));
    }
}

UDPClient::~UDPClient()
{
    m_recv_thread_running = false;
    if (m_Listen_Socket != INVALID_SOCKET)
    {
        ::close(m_Listen_Socket);
        m_Listen_Socket = INVALID_SOCKET;
    }
    if (m_PosIntakeSock != INVALID_SOCKET)
    {
        closesocket(m_PosIntakeSock);
        m_PosIntakeSock = INVALID_SOCKET;
    }
    delete m_Dtls;
    m_Dtls = nullptr;
}

// ---------------------------------------------------------------------------
// Minimal RecvThread — only the opcodes Phase K's MasterJoin path needs.
// Other opcodes are logged and dropped (no client-side launcher behaviour
// on Linux).
//
// Phase AH: when DTLS is active every inbound datagram is a DTLS record. We
// recvfrom() the raw bytes, route them through the per-peer transport (which
// advances the handshake and/or decrypts), put any handshake bytes it produces
// back on the wire to that same peer, and dispatch each decrypted application
// record through DispatchServerDatagram. In opted-out plaintext mode m_Dtls is
// nullptr and the raw datagram is dispatched directly, exactly as before.
// ---------------------------------------------------------------------------
void UDPClient::RecvThread()
{
    m_recv_thread_running = true;

    while (!g_ServerShutdown && m_recv_thread_running)
    {
        long           src_addr = 0;
        unsigned short src_port = 0;
        int received = UDP_RecvFromServer((char *) m_RecvBuffer,
                                          MAX_UDPC_BUFFER, src_addr, src_port);
        if (received <= 0)
        {
            // recv() returned -1 (SO_RCVTIMEO expiry, SIGINT / EINTR, or
            // close()) or 0 (shouldn't happen on UDP). Bail if the socket
            // has been closed under us; otherwise give the reliable-stream
            // recovery a chance to retry an outstanding hole -- the pump is
            // self-gated (active connection, pending hole, >=300ms since
            // the last NACK), so calling it on every quiet pass is cheap.
            if (m_Listen_Socket == INVALID_SOCKET) break;
            PumpPacketResend();
            continue;
        }

        if (m_Dtls)
        {
            uint64_t peer = net7::DtlsPeerKey((uint32_t) src_addr, src_port);
            net7::DtlsStep step = m_Dtls->Feed(peer, m_RecvBuffer,
                                               (size_t) received);
            if (!step.to_send.empty())
            {
                RawSendToServer(step.to_send.data(),
                                (int) step.to_send.size(), src_addr, src_port);
            }
            if (step.fatal)
            {
                m_Dtls->ForgetPeer(peer);
                continue;
            }
            if (step.handshake_done)
            {
                // Client-role 4-flight completed (and the server cert verified
                // against the configured name) -- this association now carries
                // encrypted gameplay. One line per (server_ip, server_port).
                unsigned char *b = (unsigned char *) &src_addr;
                LogMessage("UDPClient(Linux): DTLS association ESTABLISHED to "
                           "%u.%u.%u.%u:%u\n", b[0], b[1], b[2], b[3], src_port);
            }
            for (const std::vector<uint8_t> &rec : step.app_data)
            {
                if (rec.size() < sizeof(EnbUdpHeader) ||
                    rec.size() > sizeof(m_RecvBuffer))
                {
                    continue;
                }
                memcpy(m_RecvBuffer, rec.data(), rec.size());
                DispatchServerDatagram((int) rec.size());
            }
            continue;
        }

        DispatchServerDatagram(received);
    }

    if (m_Listen_Socket != INVALID_SOCKET)
    {
        ::close(m_Listen_Socket);
        m_Listen_Socket = INVALID_SOCKET;
    }
}

// ---------------------------------------------------------------------------
// DispatchServerDatagram — run one cleartext server->proxy datagram (sitting in
// m_RecvBuffer, `received` bytes) through the opcode switch. This is the body
// formerly inlined in RecvThread; factored out so both the DTLS-decrypt path
// and the plaintext path feed it.
// ---------------------------------------------------------------------------
void UDPClient::DispatchServerDatagram(int received)
{
    if ((size_t) received < sizeof(EnbUdpHeader)) return;

    EnbUdpHeader *header = (EnbUdpHeader *) m_RecvBuffer;
    unsigned short bytes = header->size - sizeof(EnbUdpHeader);
    short          opcode = header->opcode;
    char          *msg   = (char *) (m_RecvBuffer + sizeof(EnbUdpHeader));

    if (received != (int)(bytes + sizeof(EnbUdpHeader)))
    {
        LogMessage("UDPClient(Linux): malformed packet opcode=0x%04x size=%d received=%d\n",
                   opcode, bytes, received);
        return;
    }

    switch (opcode)
    {
        // ----- Master plane -----
        case ENB_OPCODE_2009_MASTER_HANDOFF_CONFIRM:
            MasterLoginConfirm(msg, header);
            break;

        // ----- Global plane -----
        // 0x2003 AVATARLIST is the server's reply to a 0x2002 TICKET. The
        // payload is sizeof(GlobalAvatarList) of network-order avatar
        // records (see server/src/UDP_Global.cpp SendAvatarList ->
        // BuildAvatarList).
        case ENB_OPCODE_2003_AVATARLIST:
            ReceiveAvatarList(msg, header);
            break;

        // 0x2005 AVATARLOGIN_CONFIRM is the server's reply to a 0x2004
        // AVATARLOGIN. Payload: long avatar_id. The server also stamps
        // header->player_id with the allocated GameID.
        case ENB_OPCODE_2005_AVATARLOGIN_CONFIRM:
            AvatarLoginConfirm(msg, header);
            break;

        // 0x200C is the confirm for both create (0x200B) and delete
        // (0x200D); payload is a refreshed GlobalAvatarList.
        case ENB_OPCODE_200C_CREATE_DELETE_AVATAR_CONFIRM:
            CreateDeleteConfirm(msg, header);
            break;

        // 0x200E is GLOBAL_ERROR with a single int32 G_ERROR_* code.
        case ENB_OPCODE_200E_GLOBAL_ERROR:
            ProcessGlobalError(msg, header);
            break;

        // ----- In-game sector traffic (Phase K Wave 6) -----
        // 0x2010 DATA_FILE: server pushes a TCP-framed payload directly
        // at the client. See UDPProxyToClient_linux.cpp::SendClientDataFile.
        case ENB_OPCODE_2010_DATA_FILE:
            SendClientDataFile(msg, header);
            break;

        // 0x2016 PACKET_SEQUENCE: server's batched in-order TCP frames
        // wrapped in a single UDP packet (with reliable-delivery /
        // resend semantics). Reassembled by SendPacketSequence and
        // forwarded opcode-at-a-time via the proxy↔client TCP socket.
        //
        // SendPacketSequence stores `header->size` bytes from its first
        // arg, then later re-casts that buffer to EnbUdpHeader inside
        // SendClientPacketSequence. It therefore expects the WHOLE UDP
        // packet (header + payload), not the payload alone — matching
        // Win32 UDPClient.cpp:256/260. Passing payload-only `msg` here
        // would have SendClientPacketSequence interpret the first 12
        // payload bytes as the header and skip them, dropping the real
        // first inner [length][opcode] tuple and producing the
        // "bad opcode 0x0000 len 0x0" diagnostic.
        case ENB_OPCODE_2016_PACKET_SEQUENCE:
            SendPacketSequence((char *) m_RecvBuffer, header, false);
            break;

        // 0x201A PACKET_C_SEQUENCE: continuation chunk of an oversized
        // 0x2016 packet that exceeded the UDP MTU. Same reassembler with
        // continuation=true. Same whole-packet calling convention as 0x2016.
        case ENB_OPCODE_201A_PACKET_C_SEQUENCE:
            SendPacketSequence((char *) m_RecvBuffer, header, true);
            break;

        default:
            // Anything else from the server on the master plane is treated
            // as a direct opcode passthrough: strip the EnbUdpHeader and
            // ship the opcode out over the proxy↔client TCP socket.
            ProcessClientOpcode(msg, header);
            break;
    }
}

// Phase AH: src_addr/src_port report the datagram's source so the caller can
// key the DTLS association. For an unconnected socket recvfrom() fills them; for
// a connected socket the only possible peer is the connect()'d default, so we
// report m_SockAddr.
int UDPClient::UDP_RecvFromServer(char *buffer, int size,
                                  long &src_addr, unsigned short &src_port)
{
    int rtn;
    if (m_Unconnected)
    {
        // Unconnected SOCK_DGRAM (global plane, and the master plane when DTLS
        // is on). Use recvfrom so we can validate the source address — drop
        // anything not from the game server IP. The peer port is intentionally
        // unfiltered here: server->proxy in-game UDP arrives from MVASauth
        // (3806) while global-control replies arrive from UDP_GLOBAL_SERVER_PORT
        // (3810) and DTLS handshake replies from the per-sector port (3501+N);
        // the same dispatcher (and the same per-peer DTLS transport) handles
        // all of them.
        struct sockaddr_in src;
        socklen_t          src_len = sizeof(src);
        rtn = ::recvfrom(m_Listen_Socket, buffer, size, 0,
                         (struct sockaddr *) &src, &src_len);
        if (rtn > 0)
        {
            if ((long) src.sin_addr.s_addr != m_IPAddr)
            {
                unsigned char *b = (unsigned char *) &src.sin_addr.s_addr;
                LogMessage("UDPClient(Linux,unconnected): drop packet from %u.%u.%u.%u:%d (not game server)\n",
                           b[0], b[1], b[2], b[3], ntohs(src.sin_port));
                return 0;  // dispatcher treats <=0 as "no packet"
            }
            src_addr = (long) src.sin_addr.s_addr;
            src_port = ntohs(src.sin_port);
        }
    }
    else
    {
        rtn = ::recv(m_Listen_Socket, buffer, size, 0);
        if (rtn > 0)
        {
            src_addr = m_IPAddr;
            src_port = ntohs(m_SockAddr.sin_port);
        }
    }
    if (rtn < 0)
    {
        // 200ms backoff matches the Win32 path's usleep(200 * 1000) on -1.
        usleep(200 * 1000);
    }
    return rtn;
}

// ---------------------------------------------------------------------------
// SendResponse / UDP_Send — wrap a buffer in EnbUdpHeader and sendto().
// ---------------------------------------------------------------------------
void UDPClient::SendResponse(short port, short opcode, unsigned char *data,
                             size_t length)
{
    EnbUdpHeader *header = (EnbUdpHeader *) m_SendBuffer;
    header->size      = (short)(length + sizeof(EnbUdpHeader));
    header->opcode    = opcode;
    header->player_id = m_PlayerID;
    if (length)
    {
        memcpy(m_SendBuffer + sizeof(EnbUdpHeader), data, length);
    }
    int bytes = (int)(length + sizeof(EnbUdpHeader));
    UDP_Send(port, (char *) m_SendBuffer, bytes);
}

void UDPClient::SendResponse(long player_id, short port, short opcode,
                             unsigned char *data, size_t length)
{
    EnbUdpHeader *header = (EnbUdpHeader *) m_SendBuffer;
    header->size      = (short)(length + sizeof(EnbUdpHeader));
    header->opcode    = opcode;
    header->player_id = player_id;
    if (length)
    {
        memcpy(m_SendBuffer + sizeof(EnbUdpHeader), data, length);
    }
    int bytes = (int)(length + sizeof(EnbUdpHeader));
    UDP_Send(port, (char *) m_SendBuffer, bytes);
}

// ---------------------------------------------------------------------------
// ForwardClientOpcode — proxy's plain copy-and-relay of client TCP frames
// onto the sector server's UDP port (m_ClientPort, set by HandleMasterJoin
// via SendMasterLogin's reply). Mirrors Win32 UDPProxyToClient.cpp:19-26
// exactly; same m_PlayerID/m_ConnectionActive gating, same SendResponse call.
// Phase K (2026-05-24): factored out of the WIN32-only UDPProxyToClient.cpp
// so the Linux sector dispatch in ClientToServer_linux_stubs.cpp can route
// in-game opcodes (TURN/TILT/MOVE/ACTION/WARP/...) through to the server.
// ---------------------------------------------------------------------------
void UDPClient::ForwardClientOpcode(short opcode, short bytes, char *packet)
{
    if (m_PlayerID != 0 && m_ConnectionActive)
    {
        SendResponse(m_ClientPort, opcode, (unsigned char *) packet, bytes);
        LogVMessage("UDPClient(Linux): forward [0x%04x] %d bytes -> port %d\n",
                    (unsigned short) opcode, (int) bytes, (int) m_ClientPort);
    }
}

void UDPClient::UDP_Send(short port, const char *buffer, int bufferLen)
{
    if (m_Listen_Socket == INVALID_SOCKET) return;

    // Phase AH: when DTLS is active, every application datagram is encrypted
    // into the association for its destination server port and only the
    // resulting DTLS record(s) go on the wire. SendApp also bootstraps the
    // handshake on first contact (queuing this datagram until it completes); we
    // proactively kick the handshake earlier (OpenFixedPort / SetClientPort) so
    // in practice the association is already established here and this datagram
    // becomes its own record. Never fall through to a cleartext sendto while
    // DTLS is on -- that would leak gameplay in the clear.
    if (m_Dtls)
    {
        short dest_port = (port == 0)
                        ? (short) ntohs(m_SockAddr.sin_port) : port;
        uint64_t peer = net7::DtlsPeerKey((uint32_t) m_IPAddr,
                                          (uint16_t) dest_port);

        // Phase AH (AH-8): prepend the per-packet auth wrapper INSIDE the DTLS
        // channel. Always 17 bytes (version + 16-byte token); the token is zero
        // until learned at global connect, and the server only validates it on
        // gameplay datagrams (player_id != 0). The server strips this wrapper at
        // its recv edge, so the inner bytes are unchanged.
        unsigned char wrapped[MAX_UDPC_BUFFER + sizeof(EnbUdpAuthWrapper)];
        size_t wlen = sizeof(EnbUdpAuthWrapper) + (size_t) bufferLen;
        if (bufferLen < 0 || wlen > sizeof(wrapped))
        {
            LogMessage("UDPClient(Linux): datagram too large to wrap (%d bytes); "
                       "dropped\n", bufferLen);
            return;
        }
        EnbUdpAuthWrapper *w = (EnbUdpAuthWrapper *) wrapped;
        w->version = NET7_UDP_AUTH_WRAPPER_VERSION;
        if (g_ServerMgr && g_ServerMgr->m_AuthTokenSet)
            memcpy(w->token, g_ServerMgr->m_AuthToken, sizeof(w->token));
        else
            memset(w->token, 0, sizeof(w->token));
        memcpy(wrapped + sizeof(EnbUdpAuthWrapper), buffer, (size_t) bufferLen);

        net7::DtlsStep step = m_Dtls->SendApp(peer, wrapped, wlen);
        if (step.fatal)
        {
            m_Dtls->ForgetPeer(peer);
            LogMessage("UDPClient(Linux): DTLS send to port %d failed (%s); "
                       "datagram dropped\n", (int) dest_port,
                       m_Dtls->LastError().c_str());
            return;
        }
        if (!step.to_send.empty())
        {
            RawSendToServer(step.to_send.data(), (int) step.to_send.size(),
                            m_IPAddr, (unsigned short) dest_port);
        }
        return;
    }

    ssize_t sent;
    if (port == 0)
    {
        // port=0 means "send to the default peer". For connected sockets
        // use send() (routes via the peer we connect()'d to). For
        // unconnected sockets (global plane) sendto() the default peer
        // captured in m_SockAddr by CreateFrom().
        if (m_Unconnected)
        {
            sent = ::sendto(m_Listen_Socket, buffer, bufferLen, 0,
                            (struct sockaddr *) &m_SockAddr,
                            sizeof(m_SockAddr));
        }
        else
        {
            sent = ::send(m_Listen_Socket, buffer, bufferLen, 0);
        }
    }
    else
    {
        struct sockaddr_in dst;
        memset(&dst, 0, sizeof(dst));
        dst.sin_family      = AF_INET;
        dst.sin_addr.s_addr = (in_addr_t) m_IPAddr;
        dst.sin_port        = htons(port);

        sent = ::sendto(m_Listen_Socket, buffer, bufferLen, 0,
                        (struct sockaddr *) &dst, sizeof(dst));
    }

    if (sent != bufferLen)
    {
        LogMessage("UDPClient(Linux): %s port %d failed: %zd/%d (%s)\n",
                   port == 0 ? "send" : "sendto", port, sent, bufferLen,
                   strerror(errno));
    }
}

// ===========================================================================
// Phase AH: proxy<->server DTLS plumbing
// ===========================================================================

// RawSendToServer — put bytes on the wire to (ip, port) WITHOUT going through
// DTLS. Used only for DTLS handshake records the transport produced; gameplay
// always rides SendApp. Mirrors UDP_Send's explicit-port sendto branch.
void UDPClient::RawSendToServer(const unsigned char *buffer, int bufferLen,
                                long ip, unsigned short port)
{
    if (m_Listen_Socket == INVALID_SOCKET || bufferLen <= 0) return;

    struct sockaddr_in dst;
    memset(&dst, 0, sizeof(dst));
    dst.sin_family      = AF_INET;
    dst.sin_addr.s_addr = (in_addr_t) ip;
    dst.sin_port        = htons(port);

    ssize_t sent = ::sendto(m_Listen_Socket, buffer, bufferLen, 0,
                            (struct sockaddr *) &dst, sizeof(dst));
    if (sent != bufferLen)
    {
        LogMessage("UDPClient(Linux): DTLS raw sendto port %d failed: %zd/%d (%s)\n",
                   (int) port, sent, bufferLen, strerror(errno));
    }
}

// InitDtls — build this socket's client-role DTLS transport from the resolved
// proxy policy. nullptr in opted-out plaintext mode; fail-closed exit on a
// misconfiguration the startup probe in InitProxyDtlsPolicy did not already
// catch (e.g. allocation failure).
void UDPClient::InitDtls()
{
    if (g_DtlsPlaintext)
    {
        m_Dtls = nullptr;
        return;
    }

    m_Dtls = new net7::DtlsTransport(net7::DtlsRole::Client);
    m_Dtls->SetVerifyHostname(g_DtlsVerifyDomain);
    if (g_DtlsCaFile[0])
    {
        if (!m_Dtls->SetVerifyCaFile(g_DtlsCaFile))
        {
            LogMessage("FATAL: UDPClient DTLS: cannot load CA '%s': %s\n",
                       g_DtlsCaFile, m_Dtls->LastError().c_str());
            exit(EXIT_FAILURE);
        }
    }
    if (!m_Dtls->Ok())
    {
        LogMessage("FATAL: UDPClient DTLS: client transport not configured: %s\n",
                   m_Dtls->LastError().c_str());
        exit(EXIT_FAILURE);
    }
}

// Phase AH: established (handshake-complete) DTLS associations on this socket,
// for the CLI status reply. 0 in plaintext mode (m_Dtls null).
size_t UDPClient::EstablishedDtls() const
{
    return m_Dtls ? m_Dtls->EstablishedPeerCount() : 0;
}

// ServerPeerKey — DTLS association key for (m_IPAddr, dest_port_host). A
// dest_port_host of 0 resolves to this socket's default peer port (the port
// captured in m_SockAddr by CreateFrom). The IP is host-stored in network byte
// order (m_IPAddr is an s_addr) and the recv path keys from the same s_addr, so
// Feed and SendApp agree on the key for a given peer.
uint64_t UDPClient::ServerPeerKey(short dest_port_host) const
{
    short p = (dest_port_host == 0)
            ? (short) ntohs(m_SockAddr.sin_port) : dest_port_host;
    return net7::DtlsPeerKey((uint32_t) m_IPAddr, (uint16_t) p);
}

// DtlsKickHandshake — proactively start the DTLS handshake to a server port so
// the association is Established before the first application datagram, avoiding
// pre-handshake record concatenation (SendApp queues all pre-handshake bytes
// into one record). No-op in plaintext mode or once the association is up.
void UDPClient::DtlsKickHandshake(short dest_port_host)
{
    if (!m_Dtls) return;

    short dp = (dest_port_host == 0)
             ? (short) ntohs(m_SockAddr.sin_port) : dest_port_host;
    uint64_t peer = ServerPeerKey(dp);
    if (m_Dtls->Established(peer)) return;

    net7::DtlsStep step = m_Dtls->ClientHandshake(peer);
    if (step.fatal)
    {
        m_Dtls->ForgetPeer(peer);
        LogMessage("UDPClient(Linux): DTLS handshake kick to port %d failed: %s\n",
                   (int) dp, m_Dtls->LastError().c_str());
        return;
    }
    if (!step.to_send.empty())
    {
        RawSendToServer(step.to_send.data(), (int) step.to_send.size(),
                        m_IPAddr, (unsigned short) dp);
    }
}

// SetClientPort — record the per-sector UDP port (3501+N) the master-handoff
// reply named, and (Phase AH) proactively start the DTLS handshake to it so the
// in-game C->S forward path (ForwardClientOpcode) rides an established
// association from its first packet.
void UDPClient::SetClientPort(short port)
{
    m_ClientPort = port;
    DtlsKickHandshake(port);
}

// ---------------------------------------------------------------------------
// WaitForResponse — re-send the request up to ~5s waiting for the recv
// thread to flip m_global_account_rcv via MasterLoginConfirm. Matches
// the Win32 behaviour: 20 ticks * 4 * 250ms ~ 20s cap; we shorten to
// 4 ticks * 250ms = 1s per send, 5 sends total = 5s, so HandleMasterJoin
// returns within the client's wait window even if the server never
// answers.
// ---------------------------------------------------------------------------
void UDPClient::WaitForResponse(short port, short opcode, unsigned char *data,
                                size_t length)
{
    int count = 5;
    while (count > 0 && !m_global_account_rcv)
    {
        SendResponse(port, opcode, data, length);
        for (int i = 0; i < 4; i++)
        {
            if (m_global_account_rcv) break;
            usleep(250 * 1000);
        }
        count--;
    }
}

void UDPClient::WaitForResponse()
{
    int count = 20;
    while (count > 0 && !m_global_account_rcv)
    {
        for (int i = 0; i < 4; i++)
        {
            if (m_global_account_rcv) break;
            usleep(250 * 1000);
        }
        count--;
    }
}

// ---------------------------------------------------------------------------
// MasterLoginConfirm / SendMasterLogin — copied logically from the Win32
// path (UDPProxyToGlobal.cpp:187 / :193) but adapted for Linux. Both
// methods access only fields and methods that exist here.
// ---------------------------------------------------------------------------
void UDPClient::MasterLoginConfirm(char *msg, EnbUdpHeader * /*header*/)
{
    m_global_account_rcv = true;
    m_ticket             = msg;
}

short UDPClient::SendMasterLogin(long avatar_id, long sector_id,
                                 long *sector_ipaddr)
{
    m_PlayerID = avatar_id;
    if (g_ServerMgr->m_UDPClient)
    {
        g_ServerMgr->m_UDPClient->SetPlayerID(m_PlayerID);
        g_ServerMgr->m_UDPClient->FixedClientComm();
    }

    unsigned char data[8];
    int index = 0;
    memset(data, 0, sizeof(data));
    m_ticket_length      = 0;
    m_global_account_rcv = false;
    m_ticket             = NULL;

    AddData(data, sector_id, index);

    if (g_Packet_Opt_requested)
    {
        AddData(data, (u8) 1, index);
    }

    WaitForResponse(UDP_MASTER_SERVER_PORT, ENB_OPCODE_2008_MASTER_HANDOFF,
                    data, index);

    m_CurrentPacketNum = -1;

    if (m_global_account_rcv && m_ticket)
    {
        short udp_port = *((short *) &m_ticket[4]);
        LogMessage("UDPClient(Linux): MasterLoginConfirm avatar=0x%08lx udp_port=%d\n",
                   (long) m_PlayerID, (int) udp_port);
        // m_ticket layout: [u32 ipaddr][u16 udp_port]. Reading ipaddr as
        // Linux `long` (8 bytes) pulls udp_port + 2 bytes past into the
        // upper half. Cast through int32_t so we copy exactly 4 wire bytes.
        *sector_ipaddr = (long) *((int32_t *) m_ticket);
        return udp_port;
    }

    LogMessage("UDPClient(Linux): SendMasterLogin timed out (no 0x2009 confirm)\n");
    *sector_ipaddr = 0;
    return -1;
}

// ---------------------------------------------------------------------------
// FixedClientComm — fire-and-forget opcode 0x200F (COMM_PORT). The
// Win32 variant of this is invoked from the recv path's avatar-login
// confirm; on Linux we keep it as a no-op-friendly send so SendMasterLogin
// can call it without dereferencing m_UDPClient (which is `this`).
// ---------------------------------------------------------------------------
void UDPClient::FixedClientComm()
{
    SendResponse((short) 0, ENB_OPCODE_200F_COMM_PORT, NULL, 0);

    // Do NOT start the MVAS feed/keepalive here. This runs on the sector-plane
    // m_UDPClient, which is connect()'d to UDP_MASTER_SERVER_PORT (3808) -- NOT
    // the socket the server delivers in-game sector S->C on. The server sends
    // ALL sector S->C from its MVASauth socket (3806) to (m_Player_IPAddr,
    // m_Player_Port), and that port is captured from the avatar-login source on
    // the GLOBAL plane (UDP_Global.cpp) -- i.e. the proxy's m_UDPGlobalClient
    // (unconnected), which is also the socket whose RecvThread relays S->C to the
    // TCP client. Every 0x1004/0x3005/0x200F the server receives calls
    // SetPlayerPortIP(source) (UDP_MVAS.cpp:103,149), repointing that single
    // delivery address at the SOURCE socket. So if the feed/keepalive rode this
    // sector socket, SetPlayerPortIP would steal S->C delivery away from the
    // global socket to a socket that (a) is connect()'d to 3808 and kernel-drops
    // the server's 3806 stream, and (b) nothing relays to the client -- silently
    // black-holing chat/target/warp. The feed/keepalive is therefore started
    // ONLY on m_UDPGlobalClient (ClientToServer_linux_stubs.cpp, the sector LOGIN
    // handler), which owns the S->C delivery mapping. Proven by per-socket bind-
    // port diagnostics: feed-from 41155 (connected-3808) vs S->C-rx on 60786
    // (unconnected, src_port=3806). See plans/29 CV-MVAS-POS.
}

// ---------------------------------------------------------------------------
// StartMVASKeepalive — spin up the 0x3005 keepalive thread on THIS UDPClient.
// Guarded by m_KeepaliveStarted so a re-login does not stack threads (the
// surviving thread picks up the latest m_PlayerID on its next tick). Called on
// the sector plane (FixedClientComm) and the global plane (the 0x0002 LOGIN
// handler in ClientToServer_linux_stubs.cpp, where it keeps the NAT mapping to
// server:3806 alive so the server's in-game UDP keeps reaching this socket).
// ---------------------------------------------------------------------------
void UDPClient::StartMVASKeepalive()
{
    if (m_KeepaliveStarted) return;

    pthread_t tid;
    if (pthread_create(&tid, NULL,
                       &LaunchUDPCKeepaliveThreadLinux, this) == 0)
    {
        pthread_detach(tid);
        m_KeepaliveStarted = true;
    }
    else
    {
        LogMessage("UDPClient(Linux): keepalive pthread_create failed: %s\n",
                   strerror(errno));
    }
}

// ---------------------------------------------------------------------------
// MVAS idle keepalive (Linux server-side proxy)
//
// The real Net7Proxy streams a 0x3005 PLAYER_COMMS_ALIVE to the server's MVAS
// handler port (MVAS_LOGIN_PORT/3806) every ~30s for the logged-in avatar,
// independent of movement. The server's idle reaper
// (server/src/PlayerManager.cpp SendUDPOpcodes / SendUDPPlayerOpcodes:
// `LastAccessTime + 2*60000 < now` -> DropPlayerFromGalaxy ->
// DropPlayerFromSector, which removes the ship from every other client's
// view) fires on any in-game player that has gone 2 minutes without a refresh.
// server/src/UDP_MVAS.cpp HandleKeepCommsAlive resolves GetPlayer(player_id)
// and refreshes LastAccessTime; it sends 0x100A MVAS_TERMINATE back ONLY when
// the id does not resolve, so the keepalive must carry the live GameID
// (m_PlayerID), which it does.
//
// Primary source: cleartext proxy<->server capture
// proxy/local-debug/net7-live-2026-06-02-login-undock-clicknavs-warp-logout.pcap
// -- 4x P->S 0x3005 to udp/3806 spaced 30.0/30.1/30.1s, each a bare 12-byte
// EnbUdpHeader { size=12, opcode=0x3005, player_id=<GameID>, sequence } with
// no body (e.g. 0c 00 05 30 2a99 0340 01000000).
// ---------------------------------------------------------------------------
static void *LaunchUDPCKeepaliveThreadLinux(void *arg)
{
    ((UDPClient *) arg)->MVASKeepaliveThread();
    return NULL;
}

void UDPClient::SendServerKeepalive()
{
    if (!m_ConnectionActive || m_PlayerID == 0) return;

    unsigned char buf[sizeof(EnbUdpHeader)];
    EnbUdpHeader *h    = (EnbUdpHeader *) buf;
    h->size            = (short) sizeof(EnbUdpHeader);
    h->opcode          = ENB_OPCODE_3005_PLAYER_COMMS_ALIVE;
    h->player_id       = m_PlayerID;
    h->packet_sequence = ++m_KeepaliveSeq;

    // 0x3005 COMMS_ALIVE is an MVAS-plane packet -- it goes to the server's
    // MVAS handler (UDP_MVAS.cpp HandleKeepCommsAlive, port MVAS_LOGIN_PORT/3806,
    // which refreshes LastAccessTime so the 2-minute idle reaper never drops the
    // avatar). The server's HandleKeepCommsAlive calls SetPlayerPortIP(source) --
    // it repoints EVERY server->client sector datagram for this avatar at the
    // source socket of the keepalive. That source MUST therefore be the socket
    // whose RecvThread drains S->C and relays it to the TCP client, or
    // chat/target/warp replies are sent to a socket nothing reads and silently
    // vanish. The keepalive (and the position feed) is therefore started ONLY on
    // m_UDPGlobalClient -- the global/unconnected plane that owns S->C delivery
    // (m_Player_Port is captured from the avatar-login source there, UDP_Global.cpp)
    // -- never on the sector-plane m_UDPClient (connect()'d to 3808, which cannot
    // even receive the server's 3806 stream). See FixedClientComm + plans/29
    // CV-MVAS-POS. UDP_Send sends from this client's m_Listen_Socket.
    UDP_Send(MVAS_LOGIN_PORT, (char *) buf, sizeof(EnbUdpHeader));
}

void UDPClient::MVASKeepaliveThread()
{
    // Two cadences ride this one thread:
    //  * the 0x1004 position feed (PB-2) is polled fast and only emits on actual
    //    movement (change-gated), matching the live Net7Proxy which streamed
    //    position densely while thrusting and went quiet when stationary;
    //  * the movement-independent 0x3005 keepalive still fires every ~30s
    //    (capture cadence 30.0/30.1/30.1s) so an idle in-space avatar refreshes
    //    LastAccessTime and never trips the server's 2-minute reaper.
    const int POLL_MS             = 200;
    const int KEEPALIVE_PERIOD_MS = 30 * 1000;
    int       since_keepalive_ms  = 0;     // first keepalive at ~30s, as captured

    while (!g_ServerShutdown && m_recv_thread_running)
    {
        usleep(POLL_MS * 1000);
        since_keepalive_ms += POLL_MS;

        // No-op until the in-client position hook is publishing (Win32/WINE);
        // inert on the Linux-native docker proxy.
        SendPositionIfChanged();

        if (since_keepalive_ms >= KEEPALIVE_PERIOD_MS)
        {
            SendServerKeepalive();
            since_keepalive_ms = 0;
        }
    }
}

// ---------------------------------------------------------------------------
// ReadClientShipPosition (PB-2) -- pull the latest ship position/orientation the
// in-client hook sent us over the loopback intake socket (ClientPositionShared.h).
//
// One transport for all three run modes (play-local / play-online / native
// Win32): the hook sends fixed 40-byte datagrams to FREYA_CLIENT_POS_PORT on
// loopback, this drains them and keeps the most recent valid one. Returns false
// when no hook is feeding (no datagram bound/received) or the last sample has
// gone stale (hook stopped, client closed), so the server's dead-reckoning
// stands undisturbed exactly as before. Datagrams are atomic, so there is no
// torn-read concern -- we just keep the newest.
// ---------------------------------------------------------------------------
bool UDPClient::ReadClientShipPosition(float pos[3], float heading[3])
{
    // Lazily bind the intake socket on first poll. A failed bind (e.g. the port
    // is already taken by another proxy instance) is latched so we never hammer.
    if (m_PosIntakeSock == INVALID_SOCKET)
    {
        if (m_PosIntakeTried) return false;
        m_PosIntakeTried = true;

        SOCKET s = ::socket(AF_INET, SOCK_DGRAM, 0);
        if (s == INVALID_SOCKET) return false;

        struct sockaddr_in addr;
        memset(&addr, 0, sizeof(addr));
        addr.sin_family = AF_INET;
        addr.sin_port   = htons(FREYA_CLIENT_POS_PORT);
        // Win32/WINE proxy: co-located with the client, bind loopback so the
        // intake is never network-reachable. Linux docker proxy: bind ANY so the
        // published-port forward delivers; compose restricts the host publish to
        // 127.0.0.1, so it is loopback-only there too.
#ifdef _WIN32
        addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
#else
        addr.sin_addr.s_addr = htonl(INADDR_ANY);
#endif
        if (::bind(s, (struct sockaddr *) &addr, sizeof(addr)) != 0)
        {
            closesocket(s);
            return false;
        }

        // Non-blocking so the per-tick drain never stalls the feed thread.
#ifdef _WIN32
        u_long nb = 1;
        ioctlsocket(s, FIONBIO, &nb);
#else
        int fl = fcntl(s, F_GETFL, 0);
        fcntl(s, F_SETFL, fl | O_NONBLOCK);
#endif
        m_PosIntakeSock = s;
    }

    // Drain everything queued; keep the newest valid datagram (latest-wins).
    FreyaClientPosDatagram dg;
    bool got = false;
    for (;;)
    {
        ssize_t n = ::recvfrom(m_PosIntakeSock, (char *) &dg, sizeof(dg), 0,
                               NULL, NULL);
        if (n != (ssize_t) sizeof(dg))
            break;                                  // EWOULDBLOCK / short / done
        if (dg.magic != FREYA_CLIENT_POS_MAGIC || dg.valid == 0)
            continue;                               // garbage or not-in-space
        m_PosSample[0]     = dg.position[0];
        m_PosSample[1]     = dg.position[1];
        m_PosSample[2]     = dg.position[2];
        m_HeadingSample[0] = dg.heading[0];
        m_HeadingSample[1] = dg.heading[1];
        m_HeadingSample[2] = dg.heading[2];
        m_PosSampleTick    = GetNet7TickCount();
        m_HavePosSample    = true;
        got                = true;
    }

    if (!m_HavePosSample) return false;

    // Staleness: if the hook has gone quiet (client closed, undocked->loading,
    // crash) stop feeding so the server is not fed a frozen position. The hook
    // sends ~10x/sec; 1.5s with nothing means the feed is dead.
    if (!got && (GetNet7TickCount() - m_PosSampleTick) > 1500)
        return false;

    pos[0] = m_PosSample[0];
    pos[1] = m_PosSample[1];
    pos[2] = m_PosSample[2];
    heading[0] = m_HeadingSample[0];
    heading[1] = m_HeadingSample[1];
    heading[2] = m_HeadingSample[2];
    return true;
}

// ---------------------------------------------------------------------------
// SendPositionIfChanged (PB-2) -- the proxy half of the MVAS position feed.
//
// When the avatar is in space and has moved since the last sample, stream its
// position+orientation to the server as opcode 0x1004 on MVAS_LOGIN_PORT (3806).
// The server (UDP_MVAS.cpp HandleMVASPosReturn) keys the update on the header
// player_id and applies it via UpdatePositionFromMVAS -> SetPosition, snapping
// the server's authoritative position to what the client actually renders.
// Without it the server only sees thrust-impulse 0x0014 MOVE (no coordinates),
// dead-reckons a diverging path, and the client "moves" while the server
// disagrees -- PB-2.
//
// Primary source for the wire shape: live Net7Proxy capture
// proxy/local-debug Combat-*.pcapng -- 0x1004 to udp/3806, a 40-byte datagram =
// 12-byte EnbUdpHeader + 6 floats (position xyz, heading xyz) + a trailing
// int32. The server reads ONLY the 6 floats (it ignores everything past byte 24
// -- see HandleMVASPosReturn's `size > 28` heading gate), so we emit the
// 24-byte server-consumed payload -- byte-identical to the verified headless
// feed (tools/cli-client MvasClient) -- and do not fabricate the
// server-ignored tail.
// ---------------------------------------------------------------------------
void UDPClient::SendPositionIfChanged()
{
    if (!m_ConnectionActive || m_PlayerID == 0) return;
    if (m_SectorID >= 9999) return;        // not in a real sector

    float pos[3], heading[3];
    if (!ReadClientShipPosition(pos, heading)) return;   // no live feed

    // Engine reports (0,0,0) before the first valid frame -- never feed it.
    if (pos[0] == 0.0f && pos[1] == 0.0f && pos[2] == 0.0f) return;

    // Change-gate: only stream on real positional movement.
    if (m_HaveFedOnce && memcmp(pos, m_LastFedPos, sizeof(pos)) == 0)
        return;

    // POSITION-ONLY feed. We deliberately do NOT send the orientation block.
    // The server's HandleMVASPosReturn applies the orientation triple as a raw
    // VELOCITY (Player::UpdatePositionFromMVAS -> SetVelocityVector) whenever the
    // packet is > 28 bytes. Our engine read supplies the ship's orientation
    // X-axis basis there -- a unit vector that is non-zero even at a dead stop --
    // so feeding it pins a permanent phantom velocity on the server: the avatar
    // is never "stopped", which blocks warp engagement and destabilizes target
    // locks. Orientation/velocity already arrive via the client's normal 0x0014
    // MOVE path; the MVAS feed only needs to correct ABSOLUTE POSITION (the PB-2
    // divergence). So we emit the 12-byte position payload only (total 24 bytes,
    // <= 28), which keeps HandleMVASPosReturn on its position-only branch -- a
    // protocol-legal 0x1004 variant the server's `size > 28` gate exists to
    // support. (The one live capture we have -- live_mvas_position_1004.hex -- is
    // 40 bytes incl. heading; we have NO primary source that the retail "heading"
    // triple is the orientation basis our engine read yields rather than a true
    // velocity vector, so until that is captured we feed position only and let the
    // 0x0014 MOVE path carry motion. CV-MVAS-POS tracks the real-client check.)
    unsigned char buf[sizeof(EnbUdpHeader) + 12];
    EnbUdpHeader *h    = (EnbUdpHeader *) buf;
    h->size            = (short) sizeof(buf);
    h->opcode          = ENB_OPCODE_1004_MVAS_SEND_POSITION_C_S;
    h->player_id       = m_PlayerID;
    h->packet_sequence = ++m_PosFeedSeq;   // server-ignored; real proxy increments
    memcpy(buf + sizeof(EnbUdpHeader), pos, 12);

    // Emit on THIS UDPClient's m_Listen_Socket (UDP_Send). The feed/keepalive is
    // started ONLY on m_UDPGlobalClient (the global/unconnected plane) -- the
    // socket the server delivers in-game sector S->C to and whose RecvThread
    // relays it to the TCP client. The server's HandleMVASPosReturn calls
    // SetPlayerPortIP(source), repointing ALL server->client sector traffic for
    // this avatar at the 0x1004 source socket; that source MUST be the S->C relay
    // socket, or chat/target/warp replies go to an unread socket and vanish. It is
    // exactly why FixedClientComm (sector plane) must NOT start this -- see the
    // FixedClientComm comment and plans/29 CV-MVAS-POS. Position-only keeps the
    // server on its <=28-byte branch, so no phantom velocity is applied.
    UDP_Send(MVAS_LOGIN_PORT, (char *) buf, (int) sizeof(buf));

    memcpy(m_LastFedPos, pos, sizeof(pos));
    m_HaveFedOnce = true;
}

// ---------------------------------------------------------------------------
// Stubs for methods referenced from non-WIN32 paths inside Connection.cpp
// (today there is one: BlankTCPConnection, in the dead SECTOR_SERVER_TO_PROXY
// cleanup branch — never hit on Linux but the linker needs it).
// ---------------------------------------------------------------------------
void UDPClient::BlankTCPConnection()
{
    // No-op on Linux. The Win32 implementation tears down the TCP link
    // owned by the launcher; the Linux proxy has no per-UDPClient TCP
    // ownership.
}

// ===========================================================================
// Global plane (peer = UDP_GLOBAL_SERVER_PORT 3810)
// ===========================================================================
//
// Sequence diagrams (server-side handlers in server/src/UDP_Global.cpp):
//
//   HandleGlobalConnect
//     proxy --0x2002 TICKET (length=strlen(ticket))------> server
//     proxy <--0x2003 AVATARLIST  (or 0x200E error)------- server
//
//   HandleGlobalTicketRequest
//     proxy --0x2004 AVATARLOGIN (char_slot | account)---> server
//     proxy <--0x2005 AVATARLOGIN_CONFIRM (avatar_id)----- server
//                     (header.player_id = GameID)
//
//   HandleCreateCharacter
//     proxy --0x200B CREATE_AVATAR (GlobalCreateCharacter)-> server
//     proxy <--0x2003 AVATARLIST  (or 0x200E error)------- server
//
//   HandleDeleteCharacter
//     proxy --0x200D DELETE_AVATAR (slot | account)------> server
//     proxy <--0x2003 AVATARLIST  (or 0x200E error)------- server
//
// All four wait synchronously on m_global_account_rcv being flipped by
// the RecvThread for THIS UDPClient. Because HandleGlobal* are invoked
// from Connection's per-client worker thread (not the recv thread), the
// flip happens out-of-line and the wait returns. Each global-plane
// request must complete before the next one is issued on the same
// UDPClient (single-outstanding-request contract); that matches how the
// Win32 path used the connection too.

void UDPClient::SetAccountName(char *name)
{
    if (name)
    {
        // strlen below could overflow m_AccountName (128 bytes) for a
        // pathological ticket. Cap to sizeof-1 and NUL-terminate.
        size_t n = strnlen(name, sizeof(m_AccountName) - 1);
        memcpy(m_AccountName, name, n);
        m_AccountName[n] = '\0';
    }
}

void UDPClient::ValidateAccount(char *msg, EnbUdpHeader *header)
{
    m_global_account_rcv = true;
    m_ticket        = msg;
    m_ticket_length = header->size;
}

void UDPClient::ReceiveAvatarList(char *msg, EnbUdpHeader * /*header*/)
{
    // The server replies with a 0x2003 AVATARLIST whose payload is
    // sizeof(GlobalAvatarList) bytes. m_RecvBuffer is what `msg` points
    // into; the caller (HandleGlobalConnect / Create / Delete) will
    // memcpy it into the Connection's m_Player_Avatar_List before the
    // next UDP recv overwrites it.
    m_global_account_rcv = true;
    m_ticket             = msg;
}

void UDPClient::AvatarLoginConfirm(char *msg, EnbUdpHeader *header)
{
    m_global_account_rcv = true;
    m_ticket             = msg;
    m_PlayerID           = header->player_id;
    // Mirror the new GameID on the master-plane UDPClient (the one that
    // SendMasterLogin will use next, and the one whose FixedClientComm
    // announces the comm port to the server).
    if (g_ServerMgr->m_UDPClient)
    {
        g_ServerMgr->m_UDPClient->SetPlayerID(m_PlayerID);
        g_ServerMgr->m_UDPClient->FixedClientComm();
    }
    LogMessage("UDPClient(Linux,global): AvatarLoginConfirm GameID=0x%08lx\n",
               (long) m_PlayerID);
}

void UDPClient::CreateDeleteConfirm(char *msg, EnbUdpHeader * /*header*/)
{
    m_global_account_rcv = true;
    m_ticket             = msg;
}

void UDPClient::ProcessGlobalError(char *msg, EnbUdpHeader * /*header*/)
{
    // Forward to the proxy<->client connection so it can build a
    // 0x0075 GLOBAL_ERROR frame for the client (see
    // Connection::GlobalError in ClientToGlobalServer_linux_stubs.cpp).
    // m_global_account_rcv = true wakes any caller currently blocked in
    // WaitForResponse so it can fail cleanly instead of timing out.
    m_global_account_rcv = true;
    m_ticket             = NULL;  // sentinel: failure path

    if (g_ServerMgr && g_ServerMgr->m_GlobalConnection)
    {
        // Wire field is 4 bytes (server-side UDP_Global.cpp writes int32_t).
        // Reading as Linux `long` would pull 8 bytes and treat the next 4
        // bytes of the UDP payload as the high half of the error code,
        // routing valid codes through the "unknown error — dropping" branch.
        long err = (long) *((int32_t *) msg);
        g_ServerMgr->m_GlobalConnection->GlobalError(err);
    }
}

char *UDPClient::SendTicket(char *ticket)
{
    if (!ticket) return NULL;

    int length = (int) strlen(ticket);
    m_global_account_rcv = false;
    m_ticket             = NULL;
    m_ticket_length      = 0;

    WaitForResponse((short) 0, ENB_OPCODE_2002_TICKET,
                    (unsigned char *) ticket, length);

    if (m_global_account_rcv && m_ticket)
    {
        LogMessage("UDPClient(Linux,global): SendTicket -> AvatarList received\n");
        return m_ticket;
    }

    LogMessage("UDPClient(Linux,global): SendTicket timed out / errored\n");
    return NULL;
}

long UDPClient::SendAvatarLogin(long char_slot)
{
    LogMessage("UDPClient(Linux,global): SendAvatarLogin slot=%ld user=%s\n",
               (long) ntohl((uint32_t) char_slot), m_AccountName);

    unsigned char data[128];
    int index = 0;
    memset(data, 0, sizeof(data));
    m_ticket_length      = 0;
    m_global_account_rcv = false;
    m_ticket             = NULL;

    // Layout: [be32 char_slot][len-prefixed string account_name]
    // (matches the Win32 path in UDPProxyToGlobal.cpp:133-167)
    AddData(data, ntohl((uint32_t) char_slot), index);
    AddDataLS(data, m_AccountName, index);

    WaitForResponse((short) 0, ENB_OPCODE_2004_AVATARLOGIN, data, index);

    if (m_global_account_rcv && m_ticket && m_PlayerID != 0)
    {
        // Payload of 0x2005 AVATARLOGIN_CONFIRM is a single 4-byte avatar_id
        // (server writes a 32-bit value). Reading as `long` on Linux x86_64
        // pulls 8 bytes and sign-extends garbage from the next field.
        return (long) *((int32_t *) m_ticket);
    }
    return -1;
}

char *UDPClient::CreateCharacter(GlobalCreateCharacter *create)
{
    if (!create) return NULL;

    m_global_account_rcv = false;
    m_ticket             = NULL;

    WaitForResponse((short) 0, ENB_OPCODE_200B_CREATE_AVATAR,
                    (unsigned char *) create, sizeof(GlobalCreateCharacter));

    if (m_global_account_rcv && m_ticket)
    {
        LogMessage("UDPClient(Linux,global): CreateCharacter confirm received\n");
        return m_ticket;
    }
    return NULL;
}

char *UDPClient::DeleteCharacter(long character_slot)
{
    unsigned char data[128];
    int index = 0;
    memset(data, 0, sizeof(data));
    m_ticket_length      = 0;
    m_global_account_rcv = false;
    m_ticket             = NULL;

    AddData(data, ntohl((uint32_t) character_slot), index);
    AddDataLS(data, m_AccountName, index);

    WaitForResponse((short) 0, ENB_OPCODE_200D_DELETE_AVATAR, data, index);

    if (m_global_account_rcv && m_ticket)
    {
        LogMessage("UDPClient(Linux,global): DeleteCharacter confirm received\n");
        return m_ticket;
    }
    return NULL;
}
