// UDPClient_linux.cpp
//
// Phase K Linux port of the UDPClient subset required to drive
// HandleMasterJoin -> SendMasterLogin -> UDP_MASTER (server:3808) ->
// MasterLoginConfirm -> SendServerRedirect.
//
// SCOPE
// -----
// The Win32 UDPClient is a ~1900 LOC class spanning 4 source files. The
// vast majority is client-launcher machinery (read ENB.exe memory for
// position updates, drive MVAS keep-alives, packet-sequence replay,
// data-file streaming, prospecting / tractor / loot opcodes, etc.) that
// is not relevant to the server-side proxy and remains WIN32-walled at
// file level in:
//   - UDPClient.cpp         (RecvThread + full ctor / OpenFixedPort)
//   - UDPProxyMVAS.cpp      (position-update thread, client-process IO)
//   - UDPProxyToClient.cpp  (server-to-client forwarding)
//   - UDPProxyToGlobal.cpp  (full SendMasterLogin / Avatar* / Ticket)
//
// This file implements only the methods on the MasterJoin -> ServerRedirect
// path:
//   - ctor (FixedPort path)               -- opens local UDP socket,
//                                            resolves NET7_GAME_SERVER_HOST
//                                            (default "server") :3808,
//                                            starts RecvThread
//   - OpenFixedPort                       -- POSIX socket + bind + connect
//   - CreateFrom                          -- shared with header inline
//   - SendMasterLogin                     -- builds opcode 0x2008 and waits
//                                            for opcode 0x2009 confirm
//   - MasterLoginConfirm                  -- sets m_global_account_rcv
//   - WaitForResponse(port,op,data,len)   -- bounded retry loop
//   - SendResponse(port,...)              -- prepend EnbUdpHeader + sendto
//   - UDP_Send                            -- sendto on m_Listen_Socket
//   - UDP_RecvFromServer                  -- recv on m_Listen_Socket
//   - RecvThread                          -- minimal dispatch: only
//                                            ENB_OPCODE_2009 wakes WaitFor
//   - FixedClientComm                     -- opcode 0x200F
//   - several no-op stubs the link path may hit (BlankTCPConnection)
//
// Everything else (avatar list, ticket exchange, position updates,
// keep-alives, packet-sequence resend, custom opcodes, etc.) is NOT
// defined here. If a future Linux code path references one of those
// methods directly, the linker will tell us — and that's the right
// signal to port the next slice.
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
// (CC BY-NC-SA 3.0 — LICENSES/enb-emulator).

#ifndef WIN32

#include "Net7.h"
#include "UDPClient.h"
#include <net7/Opcodes.h>
#include "PacketStructures.h"
#include "PacketMethods.h"
#include "ServerManager.h"

#include <sys/types.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <arpa/inet.h>
#include <netdb.h>
#include <unistd.h>
#include <pthread.h>
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
// connect() to (ip_addr, port). The connect() lets us use send()/recv()
// without specifying the peer every time (matches the Win32 path).
// ---------------------------------------------------------------------------
bool UDPClient::OpenFixedPort(short port, long ip_addr)
{
    (void) port;  // The 'port' arg in the Win32 path is actually the
                  // PEER port, not the bind port. The ctor passes
                  // MVAS_LOGIN_PORT and then later code calls
                  // SendResponse(UDP_MASTER_SERVER_PORT, ...) which
                  // overrides via UDP_Send/sendto on a per-call basis.

    m_Listen_Socket = ::socket(AF_INET, SOCK_DGRAM, 0);
    if (m_Listen_Socket < 0)
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
        m_Listen_Socket = -1;
        return false;
    }

    // Resolve the game server's IP. Falls back to the ip_addr ctor arg
    // if NET7_GAME_SERVER_HOST is unset and the default 'server' name
    // doesn't resolve (e.g. running outside docker).
    long server_ip = ResolveGameServerIP(ip_addr);

    // Per-call sendto uses CreateFrom + UDP_Send; we keep m_SockAddr
    // pointing at UDP_MASTER_SERVER_PORT as the default peer.
    CreateFrom(server_ip, UDP_MASTER_SERVER_PORT);

    if (::connect(m_Listen_Socket, (struct sockaddr *) &m_SockAddr,
                  sizeof(m_SockAddr)) < 0)
    {
        // connect() on a SOCK_DGRAM is just default-peer setup; if it
        // fails we can still sendto() explicitly. Log and continue.
        LogMessage("UDPClient: connect() warning: %s (continuing)\n",
                   strerror(errno));
    }

    m_IPAddr = server_ip;

    // Report the bound source port for debugging.
    struct sockaddr_in actual;
    socklen_t actual_len = sizeof(actual);
    if (::getsockname(m_Listen_Socket, (struct sockaddr *) &actual,
                      &actual_len) == 0)
    {
        unsigned char *b = (unsigned char *) &server_ip;
        LogMessage("UDPClient: bound UDP %u (src) -> %u.%u.%u.%u:%d (game server)\n",
                   ntohs(actual.sin_port), b[0], b[1], b[2], b[3],
                   UDP_MASTER_SERVER_PORT);
    }

    return true;
}

// ---------------------------------------------------------------------------
// Constructor — FixedPort path only on Linux.
// ---------------------------------------------------------------------------
UDPClient::UDPClient(short port, short connection_type, long ip_addr)
{
    m_IPAddr               = ip_addr;
    m_Port                 = port;
    m_ConnectionType       = connection_type;
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
    m_Listen_Socket        = -1;
    m_recv_thread_running  = false;

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
    if (m_Listen_Socket >= 0)
    {
        ::close(m_Listen_Socket);
        m_Listen_Socket = -1;
    }
}

// ---------------------------------------------------------------------------
// Minimal RecvThread — only the opcodes Phase K's MasterJoin path needs.
// Other opcodes are logged and dropped (no client-side launcher behaviour
// on Linux).
// ---------------------------------------------------------------------------
void UDPClient::RecvThread()
{
    m_recv_thread_running = true;

    while (!g_ServerShutdown && m_recv_thread_running)
    {
        int received = UDP_RecvFromServer((char *) m_RecvBuffer,
                                          MAX_UDPC_BUFFER);
        if (received <= 0)
        {
            // recv() returned -1 (with errno set by SIGINT / EINTR /
            // close()) or 0 (shouldn't happen on UDP). Bail if the
            // socket has been closed under us.
            if (m_Listen_Socket < 0) break;
            continue;
        }

        if ((size_t) received < sizeof(EnbUdpHeader)) continue;

        EnbUdpHeader *header = (EnbUdpHeader *) m_RecvBuffer;
        unsigned short bytes = header->size - sizeof(EnbUdpHeader);
        short          opcode = header->opcode;
        char          *msg   = (char *) (m_RecvBuffer + sizeof(EnbUdpHeader));

        if (received != (int)(bytes + sizeof(EnbUdpHeader)))
        {
            LogMessage("UDPClient(Linux): malformed packet opcode=0x%04x size=%d received=%d\n",
                       opcode, bytes, received);
            continue;
        }

        switch (opcode)
        {
        case ENB_OPCODE_2009_MASTER_HANDOFF_CONFIRM:
            MasterLoginConfirm(msg, header);
            break;

        default:
            LogVMessage("UDPClient(Linux): RX opcode 0x%04x %u bytes (not handled on Linux)\n",
                        (unsigned short) opcode, bytes);
            break;
        }
    }

    if (m_Listen_Socket >= 0)
    {
        ::close(m_Listen_Socket);
        m_Listen_Socket = -1;
    }
}

int UDPClient::UDP_RecvFromServer(char *buffer, int size)
{
    int rtn = ::recv(m_Listen_Socket, buffer, size, 0);
    if (rtn < 0)
    {
        // 200ms backoff matches the Win32 path's Sleep(200) on -1.
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

void UDPClient::UDP_Send(short port, const char *buffer, int bufferLen)
{
    if (m_Listen_Socket < 0) return;

    ssize_t sent;
    if (port == 0)
    {
        // The Win32 path uses send() on a connected SOCK_DGRAM, which
        // routes to the default peer (we connect()'d to
        // UDP_MASTER_SERVER_PORT in OpenFixedPort). Match that here:
        // port=0 means "send to the default peer".
        sent = ::send(m_Listen_Socket, buffer, bufferLen, 0);
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
        *sector_ipaddr = *((long *) m_ticket);
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

#endif  // !WIN32
