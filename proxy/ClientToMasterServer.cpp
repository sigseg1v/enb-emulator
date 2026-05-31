// ClientToMasterServer.cpp
//
// Phase K: opcode dispatch for Master Server (TCP 3801). Compiles on
// both Win32 (original behaviour: full SendMasterLogin → UDP MVAS round-
// trip → SendServerRedirect) and Linux (minimal port: SendMasterLogin
// hits the server's UDP_MASTER_SERVER_PORT, waits up to ~5s for the
// 0x2009 confirm, then sends ServerRedirect).
//
// Phase J carry-over: HandleMasterJoin used to be a Linux stub that
// short-circuited the MVAS round-trip and sent a hardcoded
// ServerRedirect pointing at the proxy's own PROXY_LOCAL_TCP_PORT. As of
// Phase K the Linux UDP plane is partially ported (UDPClient_linux.cpp)
// so we now call SendMasterLogin like the Win32 path; if it times out
// we fall back to the hardcoded redirect to preserve the "client moves
// on" property.

/*************************************
 *   /////////////////////////////   *
 *   //  MASTER SERVER OPCODES  //   *
 *   /////////////////////////////   *
 *************************************/

#include "Net7.h"
#include "Connection.h"
#include <net7/Opcodes.h>
#include "ServerManager.h"
#include <net7/PacketStructures.h>
#include "UDPClient.h"

void Connection::ProcessMasterServerOpcode(short opcode, short bytes)
{
	switch (opcode)
	{
	case ENB_OPCODE_0035_MASTER_JOIN :
		HandleMasterJoin();
		break;

	default :
		LogMessage("ProcessMasterServerOpcode -- UNRECOGNIZED OPCODE 0x%04x (%d bytes)\n",
			(unsigned short) opcode, (int) bytes);
		break;
	}
}

#ifdef NET7_LEGACY_WIN32
void Connection::HandleMasterJoin()
{
	// The player's client is displaying the inter-sector wait screen
	MasterJoin * join = (MasterJoin *) m_RecvBuffer;
	long sector_id = ntohl(join->ToSectorID);

	m_AvatarID = ntohl(join->avatar_id_lsb);
	//LogMessage("<client> Received MasterJoin packet, ToSectorID = %d [%d] [%d]\n", sector_id, m_AvatarID, ntohl(join->avatar_id_msb));

	g_LoggedIn = true;

    long sector_ipaddr;
    short sector_port;

    g_ServerMgr->m_MasterConnection = this;

    sector_port = g_ServerMgr->m_UDPConnection->SendMasterLogin(m_AvatarID, sector_id, &sector_ipaddr);

	if (sector_port == -1)
	{
		::MessageBox(NULL, "Server Failed to respond to sector login", "Net7Proxy", MB_ICONERROR);
		LogMessage(">> CRITICAL ERROR: Server unable to process sector login\n");
		ShutdownClient();
	}
	else
	{
		LogMessage("<server> Master Login received - UDP sector port: %d\n", sector_port);
		g_ServerMgr->m_UDPConnection->SetClientPort(sector_port);
		g_ServerMgr->m_UDPConnection->SetClientIP(sector_ipaddr);
		g_ServerMgr->m_UDPConnection->SetSectorID(sector_id);
		g_ServerMgr->m_UDPClient->SetSectorID(sector_id);

		// Redirect the client to the appropriate sector server
		SendServerRedirect(sector_id);

		//start login thread here
		g_ServerMgr->m_UDPClient->StartLoginTimer();
	}
}
#else
// Linux: drive the same SendMasterLogin round-trip as the Win32 path
// (proxy -> server:3808 UDP, opcode 0x2008, wait for 0x2009 confirm).
//
// If the server doesn't confirm on the first SendMasterLogin attempt we
// retry the handoff up to kHandoffAttempts times before giving up.
// SendMasterLogin itself does ~5s of internal retransmits via
// WaitForResponse (5 sends x 1s window); the outer retry covers the
// case where the server-side master-listener recv thread has not yet
// started (the master UDP listener is deferred until every sector's
// UDP port has been bound -- see ServerManager::Run, which can take
// 60-90s during a cold start with ~300 sectors at ~100ms each). With
// kHandoffAttempts=30 and a 5s inner window the proxy waits ~150s
// before giving up, which comfortably covers the observed ~84s cold
// start. SendMasterLogin resets m_global_account_rcv = false at the
// top of every call so outer retries do not race on stale state.
//
// The fallback path (sending a proxy-local ServerRedirect without
// SetClientPort/SetClientIP/SetSectorID being populated) leaves the
// proxy's UDP back-channel unbound and manifests as a permanent
// loading-screen hang client-side. We still send the redirect on
// exhaustion so the client's TCP state machine advances out of
// MasterJoin, but log loudly so the dead end is obvious.
void Connection::HandleMasterJoin()
{
	MasterJoin * join = (MasterJoin *) m_RecvBuffer;
	long sector_id = ntohl(join->ToSectorID);
	m_AvatarID = ntohl(join->avatar_id_lsb);

	LogMessage("<client> MasterJoin avatar_id=%ld ToSectorID=%ld FromSectorID=%ld\n",
		(long) m_AvatarID, (long) sector_id, (long) ntohl(join->FromSectorID));

	g_LoggedIn = true;

	g_ServerMgr->m_MasterConnection = this;

	long  sector_ipaddr = 0;
	short sector_port   = -1;

	if (!g_ServerMgr->m_UDPConnection)
	{
		LogMessage("<proxy> HandleMasterJoin: m_UDPConnection null; cannot SendMasterLogin\n");
		SendServerRedirect(sector_id);
		return;
	}

	const int kHandoffAttempts = 30;
	for (int attempt = 0; attempt < kHandoffAttempts; attempt++)
	{
		sector_port = g_ServerMgr->m_UDPConnection->SendMasterLogin(
			m_AvatarID, sector_id, &sector_ipaddr);
		if (sector_port != -1)
		{
			break;
		}
		LogMessage("<proxy> SendMasterLogin attempt %d/%d timed out; retrying\n",
			attempt + 1, kHandoffAttempts);
	}

	if (sector_port == -1)
	{
		LogMessage(">> CRITICAL: SendMasterLogin failed after %d attempts; "
			"client will hang at loading screen. Check that the server's "
			"master UDP listener has started (look for 'Registering "
			"sector server' in server logs).\n", kHandoffAttempts);
		SendServerRedirect(sector_id);
		return;
	}

	LogMessage("<server> Master Login received - UDP sector port: %d\n", sector_port);
	g_ServerMgr->m_UDPConnection->SetClientPort(sector_port);
	g_ServerMgr->m_UDPConnection->SetClientIP(sector_ipaddr);
	g_ServerMgr->m_UDPConnection->SetSectorID(sector_id);
	if (g_ServerMgr->m_UDPClient)
	{
		g_ServerMgr->m_UDPClient->SetSectorID(sector_id);
	}

	// Redirect the client to the appropriate sector server. The
	// ServerRedirect payload still points at m_IpAddress / our
	// PROXY_LOCAL_TCP_PORT -- once ProcessSectorServerOpcode is ported the
	// client's next-stage TCP traffic resumes here.
	SendServerRedirect(sector_id);
}
#endif

void Connection::SendServerRedirect(long sector_id)
{
	// Redirect the client to the correct Sector Server!!!
	ServerRedirect redirect;

    memset(&redirect, 0, sizeof(redirect));
	// sector_id: pass through in host byte order. The Win32 client reads
	// this field as a LE int on x86 (matches the retail server's wire
	// format -- see archive/kyp-snapshot/capturedPackets/capture_1.rar
	// frames 222 / 656 / 1062 and capture_2.rar frame 222, all of which
	// show sector_id as LE-on-wire e.g. 0x69 0x29 0x00 0x00 for Aragoth
	// 10601). A previous ntohl() here byte-swapped to BE on the wire,
	// which the client decoded as a garbage sector index, looked up NULL
	// in its sector pool, then crashed on the next vtable dispatch
	// during sector handoff. ip_address keeps its ntohl(): m_IpAddress
	// is held in network byte order (sockaddr_in.s_addr convention from
	// inet_addr), and the same captures confirm the IP field is also a
	// LE-on-wire int whose value, fed to inet_ntoa via s_addr, prints
	// the right address only when we byte-swap once here.
	redirect.sector_id = sector_id;
	redirect.ip_address = ntohl(m_ServerMgr.m_IpAddress);
    redirect.port = PROXY_LOCAL_TCP_PORT;

    //LogMessage("<proxy> Master Server sending ServerRedirect packet, SectorID = %d\n", sector_id);
	SendResponse(ENB_OPCODE_0036_SERVER_REDIRECT, (unsigned char *) &redirect, sizeof(redirect));
}
