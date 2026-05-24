// ClientToServer_linux_stubs.cpp
//
// Phase J Linux: stub dispatchers for the Global and Sector server
// opcode tables. The real Win32 implementations live in
// ClientToGlobalServer.cpp (286 LOC, ~15 handlers) and
// ClientToSectorServer.cpp (757 LOC, ~50+ handlers); both files are
// WIN32-walled today because the full ports depend on the UDP plane
// (SendTicket/SendAvatarLogin/etc.), MySQL access from the proxy
// process, and Player lifecycle — all multi-day items.
//
// Phase K progressive port: handlers that don't require the UDP plane
// or Player state land here as real implementations. Everything else
// stays a logging stub so the frame is consumed correctly and operators
// can see what real clients send.
//
// Current Linux handlers:
//   ProcessGlobalServerOpcode:
//     0x0000 VersionRequest  -> 0x0001 VersionResponse (status = 0 if
//                               major=42 minor=0, 1 if outdated, 2 if newer)
//   ProcessSectorServerOpcode:
//     0x0002 LOGIN           -> activate the proxy↔server connection state
//                               (matches Win32 ClientToSectorServer.cpp:22-31)
//
// This file is Linux-only (the WIN32 build picks up the real dispatch
// from ClientToGlobalServer.cpp / ClientToSectorServer.cpp).

#ifndef WIN32

#include "Net7.h"
#include "Connection.h"
#include <net7/Opcodes.h>
#include <net7/PacketStructures.h>
#include "ServerManager.h"
#include "UDPClient.h"

#include <arpa/inet.h>

namespace {

// Local mirror of the Win32 HandleVersionRequest body. Reads two big-endian
// longs from m_RecvBuffer (Major, Minor) and returns a single int32_t status
// in the VersionResponse. Status: 0 = version OK, 1 = client too old,
// 2 = client newer than this server (per Win32 ClientToGlobalServer.cpp:94-115).
void HandleVersionRequest_Linux(Connection *conn, unsigned char *recv_buf)
{
    VersionRequest *request = (VersionRequest *) recv_buf;
    long major = (long) ntohl((uint32_t) request->Major);
    long minor = (long) ntohl((uint32_t) request->Minor);

    int32_t status;
    if (major == 42 && minor == 0)        status = 0;
    else if (major < 42)                  status = 1;
    else                                  status = 2;

    LogMessage("<client> VersionRequest major=%ld minor=%ld -> status=%d\n",
               major, minor, (int) status);

    conn->SendResponse(ENB_OPCODE_0001_VERSION_RESPONSE,
                       (unsigned char *) &status, sizeof(status));
}

} // namespace

void Connection::ProcessGlobalServerOpcode(short opcode, short bytes)
{
    switch ((unsigned short) opcode) {
    case ENB_OPCODE_0000_VERSION_REQUEST:
        HandleVersionRequest_Linux(this, m_RecvBuffer);
        break;

    case ENB_OPCODE_0035_MASTER_JOIN:
        // The client occasionally sends MasterJoin on the global socket
        // (per the Win32 dispatch's TODO comment in ClientToGlobalServer.cpp:61).
        // It's harmless — silently drop, matching Win32.
        break;

    default:
        LogMessage("Linux stub: ProcessGlobalServerOpcode 0x%04x (%d bytes) — not yet implemented\n",
                   (unsigned short) opcode, (int) bytes);
        break;
    }
}

void Connection::ProcessSectorServerOpcode(short opcode, short bytes)
{
    switch ((unsigned short) opcode) {
    case ENB_OPCODE_0002_LOGIN:
        // Matches Win32 ClientToSectorServer.cpp:22-31. Activates the proxy↔
        // server connection state so subsequent UDP traffic from the game server
        // gets relayed back to this client. time_debug=50 from Win32 is skipped:
        // it's only consumed by UDPProxyToClient.cpp, which is itself WIN32-walled.
        g_LoggedIn = true;
        g_ServerMgr->m_SectorConnection = this;
        LogMessage("<client> SectorServer LOGIN — connection active\n");
        g_ServerMgr->m_UDPConnection->SetConnectionActive(true);
        g_ServerMgr->m_UDPClient->SetConnectionActive(true);
        g_ServerMgr->m_UDPConnection->SetLoginComplete(false);
        m_SectorTCPRequest = false;
        break;

    default:
        LogMessage("Linux stub: ProcessSectorServerOpcode 0x%04x (%d bytes) — not yet implemented\n",
                   (unsigned short) opcode, (int) bytes);
        break;
    }
}

#endif // !WIN32
