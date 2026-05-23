// ClientToMasterServer.cpp
//
// Phase J: opcode dispatch for Master Server (TCP 3801). Compiles on
// both Win32 (original behaviour: full SendMasterLogin → UDP MVAS round-
// trip → SendServerRedirect) and Linux (option-b stub: log + hardcoded
// ServerRedirect using the proxy's own IP). The Win32 path still
// requires g_ServerMgr->m_UDPConnection / m_UDPClient — neither is
// constructed on the Linux proxy yet (UDP proxy plane is still WIN32-
// walled at file level — see proxy/UDPProxyMVAS.cpp, UDPClient.cpp, etc.)
// so the Linux branch bypasses them entirely.

/*************************************
 *   /////////////////////////////   *
 *   //  MASTER SERVER OPCODES  //   *
 *   /////////////////////////////   *
 *************************************/

#include "Net7.h"
#include "Connection.h"
#include "Opcodes.h"
#include "ServerManager.h"
#include "PacketStructures.h"
#ifdef WIN32
#include "UDPClient.h"
#endif

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
// Linux option-b stub: log the join + send a hardcoded ServerRedirect
// back. No MVAS round-trip (the UDP proxy plane is still WIN32-walled),
// so we point the client at SECTOR_SERVER_PORT on the proxy itself —
// once we port ClientToSectorServer.cpp, the client's next TCP to 3500
// lands here too.
void Connection::HandleMasterJoin()
{
	MasterJoin * join = (MasterJoin *) m_RecvBuffer;
	long sector_id = ntohl(join->ToSectorID);
	m_AvatarID = ntohl(join->avatar_id_lsb);

	LogMessage("<client> MasterJoin avatar_id=%ld ToSectorID=%ld FromSectorID=%ld (Linux stub)\n",
		(long) m_AvatarID, (long) sector_id, (long) ntohl(join->FromSectorID));

	g_LoggedIn = true;

	// Linux stub: skip MVAS SendMasterLogin (no UDP plane), send a
	// hardcoded ServerRedirect that points the client at the proxy's
	// own SECTOR_SERVER_PORT. This lets us see what the client sends
	// next without requiring the full UDP login round-trip.
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
