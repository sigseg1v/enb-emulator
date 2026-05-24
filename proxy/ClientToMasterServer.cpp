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
// ServerRedirect pointing at the proxy's own SECTOR_SERVER_PORT. As of
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
#include "PacketStructures.h"
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

#ifdef WIN32
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
// If the server doesn't respond within the WaitForResponse window we
// fall through to a hardcoded ServerRedirect at the proxy's
// SECTOR_SERVER_PORT so the client's state machine keeps moving — same
// behaviour the Phase J option-b stub had, just now driven by an
// actually-attempted UDP exchange.
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

	if (g_ServerMgr->m_UDPConnection)
	{
		sector_port = g_ServerMgr->m_UDPConnection->SendMasterLogin(
			m_AvatarID, sector_id, &sector_ipaddr);
	}
	else
	{
		LogMessage("<proxy> HandleMasterJoin: m_UDPConnection null; cannot SendMasterLogin\n");
	}

	if (sector_port == -1)
	{
		// Server didn't confirm; fall back to the Phase J option-b path:
		// hand the client a redirect back at the proxy's own
		// SECTOR_SERVER_PORT and let the next TCP frame surface here.
		LogMessage("<proxy> SendMasterLogin failed/timed-out; sending proxy-local ServerRedirect\n");
		SendServerRedirect(sector_id);
		return;
	}

	LogMessage("<server> Master Login received - UDP sector port: %d\n", sector_port);
	if (g_ServerMgr->m_UDPConnection)
	{
		g_ServerMgr->m_UDPConnection->SetClientPort(sector_port);
		g_ServerMgr->m_UDPConnection->SetClientIP(sector_ipaddr);
		g_ServerMgr->m_UDPConnection->SetSectorID(sector_id);
	}
	if (g_ServerMgr->m_UDPClient)
	{
		g_ServerMgr->m_UDPClient->SetSectorID(sector_id);
	}

	// Redirect the client to the appropriate sector server. The
	// ServerRedirect payload still points at m_IpAddress / our
	// SECTOR_SERVER_PORT — once ProcessSectorServerOpcode is ported the
	// client's next-stage TCP traffic resumes here.
	SendServerRedirect(sector_id);
}
#endif

void Connection::SendServerRedirect(long sector_id)
{
	// Redirect the client to the correct Sector Server!!!
	ServerRedirect redirect;

    memset(&redirect, 0, sizeof(redirect));
	redirect.sector_id = ntohl(sector_id);
	redirect.ip_address = ntohl(m_ServerMgr.m_IpAddress);
    redirect.port = SECTOR_SERVER_PORT;

    //LogMessage("<proxy> Master Server sending ServerRedirect packet, SectorID = %d\n", sector_id);
	SendResponse(ENB_OPCODE_0036_SERVER_REDIRECT, (unsigned char *) &redirect, sizeof(redirect));
}
