// ClientToServer_linux_stubs.cpp
//
// Client->server opcode dispatchers for the Global and Sector planes.
// This is the single implementation -- the proxy is one source tree built
// for both Linux-native and Win32-PE, with no separate per-platform copy.
// Handlers that don't require per-sector Player state are fully
// implemented here; the rest consume-and-log the frame so it is handled
// correctly and operators can see what real clients send.
//
// The global-plane account / avatar-list / ticket / create / delete chain
// is wired end to end through proxy::m_UDPGlobalClient
// (proxy/UDPClient_linux.cpp, peer = UDP_GLOBAL_SERVER_PORT 3810) into
// server::UDP_Connection::HandleGlobalOpcode (server/src/UDP_Global.cpp).
//
// Handlers:
//   ProcessGlobalServerOpcode:
//     0x0000 VersionRequest  -> 0x0001 VersionResponse
//     0x0035 MasterJoin      -> silent drop
//     0x006D GLOBAL_CONNECT  -> SendTicket(ticket) -> 0x0070 AvatarList
//     0x006E GLOBAL_TICKET_REQUEST -> SendAvatarLogin(slot) ->
//                                     0x006F GlobalTicket
//     0x0071 GLOBAL_DELETE_CHARACTER -> DeleteCharacter(slot) -> 0x0070
//     0x0072 GLOBAL_CREATE_CHARACTER -> CreateCharacter(struct) -> 0x0070
//   ProcessSectorServerOpcode:
//     0x0002 LOGIN           -> activate proxy<->server connection state, fwd
//     0x0006 START_ACK       -> fwd + 0x3004/0x3008 visibility kick to server
//     0x0012 TURN            -> 250ms throttle WHILE MOVING, else fwd
//     0x0013 TILT            -> 250ms throttle WHILE MOVING, else fwd
//     0x0014 MOVE            -> forward verbatim
//     0x002C ACTION          -> forward verbatim
//     0x009B WARP            -> forward verbatim
//     0x009F STARBASE_ROOM_CHANGE -> forward verbatim
//     0x00B9 LOGOFF_REQUEST  -> set g_LoggedIn=true, forward
//     (default)              -> forward verbatim
//
// Also defines Connection::GlobalError, Connection::SendGlobalTicket,
// Connection::ProcessGlobalTicket, Connection::SendAvatarList.

#include "Net7.h"
#include "Connection.h"
#include <net7/Opcodes.h>
#include <net7/PacketStructures.h>
#include "PacketMethods.h"
#include "ServerManager.h"
#include "UDPClient.h"

#include <string.h>
// arpa/inet.h is redundant -- Net7.h provides ntohl on both platforms.

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

// Phase AH: answer the CLI's 0x3009 status request with the proxy's own view
// of the proxy<->server link. This is read-only telemetry consumed entirely by
// the proxy -- nothing is forwarded to the server, and the reply touches no
// game state. The proxy is the only component that negotiates the proxy<->server
// DTLS, so it is the ground-truth source for whether that leg is encrypted.
void HandleCliStatusRequest_Linux(Connection *conn, ServerManager &mgr)
{
    ProxyStatusReply reply;
    memset(&reply, 0, sizeof(reply));

    // Policy: DTLS is enforced unless the operator opted out into plaintext.
    reply.dtls_required = g_DtlsPlaintext ? 0 : 1;

    // Live (handshake-complete) associations across both server-facing legs:
    // the master/sector send socket and the global control-plane socket.
    size_t established = 0;
    if (mgr.m_UDPConnection)   established += mgr.m_UDPConnection->EstablishedDtls();
    if (mgr.m_UDPGlobalClient) established += mgr.m_UDPGlobalClient->EstablishedDtls();
    reply.dtls_live_assocs = established > 255 ? 255 : (uint8_t) established;

    // The proxy has an active server-side UDP link once the master socket exists.
    reply.connected = mgr.m_UDPConnection ? 1 : 0;

    conn->SendResponse(ENB_OPCODE_300A_CLI_STATUS_REPLY,
                       (unsigned char *) &reply, sizeof(reply));
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
        // It's harmless -- silently drop, matching Win32.
        break;

    case ENB_OPCODE_006D_GLOBAL_CONNECT:
        HandleGlobalConnect();
        break;

    case ENB_OPCODE_006E_GLOBAL_TICKET_REQUEST:
        HandleGlobalTicketRequest();
        break;

    case ENB_OPCODE_0071_GLOBAL_DELETE_CHARACTER:
        HandleDeleteCharacter();
        break;

    case ENB_OPCODE_0072_GLOBAL_CREATE_CHARACTER:
        HandleCreateCharacter();
        break;

    case ENB_OPCODE_3009_CLI_STATUS_REQUEST:
        // CLI-only introspection; consumed here, never forwarded to the server.
        HandleCliStatusRequest_Linux(this, m_ServerMgr);
        break;

    default:
        LogMessage("Linux stub: ProcessGlobalServerOpcode 0x%04x (%d bytes) -- not yet implemented\n",
                   (unsigned short) opcode, (int) bytes);
        break;
    }
}

// ===========================================================================
// Global plane handlers -- Linux ports of ClientToGlobalServer.cpp:124-249
// ===========================================================================
//
// All four route through g_ServerMgr->m_UDPGlobalClient, the dedicated
// UDPClient connect()'d to UDP_GLOBAL_SERVER_PORT (3810) -- see
// proxy/Net7.cpp main() and proxy/UDPClient_linux.cpp.

// Phase AH (AH-8): decode `len` hex chars into `out` (len/2 bytes). Returns
// false unless the input is exactly the expected hex length with no stray
// characters. The proxy does not link libsodium, so this is a small local
// decoder for the 32-hex login-ticket suffix.
static bool ProxyHexToBin(const char *hex, unsigned char *out, size_t out_len)
{
    if (!hex) return false;
    for (size_t i = 0; i < out_len; ++i)
    {
        int hi = -1, lo = -1;
        char ch = hex[i * 2], cl = hex[i * 2 + 1];
        if (ch >= '0' && ch <= '9') hi = ch - '0';
        else if (ch >= 'a' && ch <= 'f') hi = ch - 'a' + 10;
        else if (ch >= 'A' && ch <= 'F') hi = ch - 'A' + 10;
        if (cl >= '0' && cl <= '9') lo = cl - '0';
        else if (cl >= 'a' && cl <= 'f') lo = cl - 'a' + 10;
        else if (cl >= 'A' && cl <= 'F') lo = cl - 'A' + 10;
        if (hi < 0 || lo < 0) return false;
        out[i] = (unsigned char)((hi << 4) | lo);
    }
    // require the suffix to end right after the hex (NUL terminator)
    return hex[out_len * 2] == '\0';
}

void Connection::HandleGlobalConnect()
{
    // Frame payload layout (matches Win32 ClientToGlobalServer.cpp:124):
    //   [u32 ticket_len][char ticket[ticket_len]]
    // Tickets are issued by Net7SSL's VerifyAccountInfo path and look
    // like "USERNAME-EXTRABYTES"; the dash splits user from auth tail.
    char *ticket = (char *) &m_RecvBuffer[4];

    // Phase AH (AH-8): learn the per-packet auth token from the ticket suffix
    // ("USERNAME-<32 hex>") BEFORE SendTicket goes on the wire, so even the
    // ticket datagram carries the wrapper. The strrchr/decode here does not
    // mutate `ticket` (the later strrchr truncation does); the suffix is the
    // 16-byte binary form of the CSPRNG login-ticket token. The server only
    // enforces it on gameplay datagrams, so a malformed suffix here is non-fatal.
    if (g_ServerMgr)
    {
        // The token is the trailing 32 hex chars; split on the LAST '-' so a
        // username containing a dash still yields the correct suffix. (The
        // issuer formats "USERNAME-<32hex>" and the token is pure hex, so it
        // never contains a dash itself.) ProxyHexToBin requires exactly 32 hex
        // followed by NUL, so a malformed suffix simply leaves the token unset.
        const char *dash = strrchr(ticket, '-');
        unsigned char tok[16];
        if (dash && ProxyHexToBin(dash + 1, tok, sizeof(tok)))
            g_ServerMgr->SetAuthToken(tok);
    }

    UDPClient *gc = g_ServerMgr ? g_ServerMgr->m_UDPGlobalClient : nullptr;
    if (!gc) {
        LogMessage("HandleGlobalConnect: no global UDP client wired -- dropping\n");
        return;
    }

    char *avatar_list = gc->SendTicket(ticket);
    if (!avatar_list) {
        // Server rejected or timed out. ProcessGlobalError will have
        // already routed a 0x0075 error back if the server replied
        // 0x200E; otherwise the client just sees no response.
        return;
    }

    // Set the proxy-local account name to the FULL username: everything before
    // the LAST '-' (the trailing hex token). A first-dash strtok truncated
    // dashed usernames, and this name is forwarded to the server verbatim in
    // create/delete-character requests (UDPClient::AddDataLS(m_AccountName)),
    // so the dashed name must survive intact. Truncating ticket in place is
    // safe -- it lives in m_RecvBuffer and we don't touch the payload again,
    // and SendTicket already consumed the full ticket above.
    char *last_dash = strrchr(ticket, '-');
    if (last_dash) *last_dash = '\0';
    m_AccountUsername = ticket;
    gc->SetAccountName(m_AccountUsername);

    memcpy(&m_Player_Avatar_List, avatar_list, sizeof(GlobalAvatarList));
    SendResponse(ENB_OPCODE_0070_GLOBAL_AVATAR_LIST,
                 (unsigned char *) avatar_list, sizeof(GlobalAvatarList));
}

void Connection::HandleDeleteCharacter()
{
    long character_slot = (long) ntohl(*(uint32_t *) m_RecvBuffer);

    UDPClient *gc = g_ServerMgr ? g_ServerMgr->m_UDPGlobalClient : nullptr;
    if (!gc) {
        LogMessage("HandleDeleteCharacter: no global UDP client -- dropping\n");
        return;
    }

    char *avatar_list = gc->DeleteCharacter(character_slot);
    if (avatar_list) {
        memcpy(&m_Player_Avatar_List, avatar_list, sizeof(GlobalAvatarList));
        SendResponse(ENB_OPCODE_0070_GLOBAL_AVATAR_LIST,
                     (unsigned char *) avatar_list, sizeof(GlobalAvatarList));
    }
}

void Connection::HandleCreateCharacter()
{
    GlobalCreateCharacter *create = (GlobalCreateCharacter *) m_RecvBuffer;

    UDPClient *gc = g_ServerMgr ? g_ServerMgr->m_UDPGlobalClient : nullptr;
    if (!gc) {
        LogMessage("HandleCreateCharacter: no global UDP client -- dropping\n");
        return;
    }

    char *avatar_list = gc->CreateCharacter(create);
    if (avatar_list) {
        memcpy(&m_Player_Avatar_List, avatar_list, sizeof(GlobalAvatarList));
        SendResponse(ENB_OPCODE_0070_GLOBAL_AVATAR_LIST,
                     (unsigned char *) avatar_list, sizeof(GlobalAvatarList));
    }
}

void Connection::HandleGlobalTicketRequest()
{
    long char_slot = (long) ntohl(*(uint32_t *) m_RecvBuffer);

    UDPClient *gc = g_ServerMgr ? g_ServerMgr->m_UDPGlobalClient : nullptr;
    if (!gc) {
        LogMessage("HandleGlobalTicketRequest: no global UDP client -- dropping\n");
        return;
    }

    long avatar_id = gc->SendAvatarLogin(char_slot);
    if (avatar_id == -1) {
        LogMessage("GlobalTicketRequest(): error obtaining slot -- galaxy-full reply\n");
        SendGlobalTicket(0x40000000, 0, 1002, false);   // 1002 = galaxy full
        return;
    }

    LogMessage("GlobalTicketRequest: user='%s' slot=%ld avatar_id=%ld\n",
               m_AccountUsername ? m_AccountUsername : "(unknown)",
               (long) char_slot, (long) avatar_id);

    ProcessGlobalTicket(char_slot);
}

// ===========================================================================
// Connection global-plane helpers (Win32 lives in ClientToGlobalServer.cpp)
// ===========================================================================

namespace {

// Table indices line up with G_ERROR_* in login-server/Net7SSL/AccountManager.h.
// Keep all 15 entries in lock-step with that header -- when codes get added on
// the server/login side, append here in the same order or GlobalError() will
// silently drop the new code (it bounds-checks against g_GlobalErrorMsgCount).
static const char *g_GlobalErrorMsg[] = {
    "Error: You have been temporarily banned.",                                                                                                          //  0  G_ERROR_BANNED_ACCOUNT
    "Sorry, that name has already been taken. Please try again.",                                                                                        //  1  G_ERROR_NICKNAME_USED
    "Sorry, your name can only contain the letters a-z. No spaces or other special characters are allowed.  Please try again.",                          //  2  G_ERROR_INVALID_CHARS
    "Sorry, this name is too short. It must contain at least 3 characters. Please try again.",                                                           //  3  G_ERROR_TOO_SHORT
    "Sorry, this name needs enough vowels (a,e,i,o,u & y) to be pronouncable. Please try again.",                                                        //  4  G_ERROR_ONE_VOWEL
    "Sorry, there are too many repeating characters in this name. Please try again.",                                                                    //  5  G_ERROR_REPEATING_CHAR
    "Sorry, this name contains a reserved or illegal word. Please try again.",                                                                           //  6  G_ERROR_RESTRICTED_LIST
    "Error: Ticket Validation Failed.",                                                                                                                  //  7  G_ERROR_TICKET_INVALID
    "Error: Authentication Server (AUTHD) is unavailable.  Try again in a few minutes.",                                                                 //  8  G_ERROR_AUTH_SERVER_DOWN
    "Error: You have not compleated registration.",                                                                                                      //  9  G_ERROR_INACTIVE_ACCOUNT
    "Sorry, that ship name is not allowed. Please try again",                                                                                            // 10  G_ERROR_RESTRICTED_SHIP
    "Sorry, Net7 experienced an internal error. Please submit a bug report",                                                                             // 11  G_ERROR_NET7_INTERNAL
    "Sorry, the server is not currently accepting new logins.  Please try again later.",                                                                 // 12  G_ERROR_STRESS_TEST_CLOSED
    "Error: That account is already logged in.",                                                                                                         // 13  G_ERROR_ACCOUNT_IN_USE
    "Sorry, the server is shutting down.  Please try again later.",                                                                                      // 14  G_ERROR_SERVER_SHUTDOWN
};
static const int g_GlobalErrorMsgCount =
    (int)(sizeof(g_GlobalErrorMsg) / sizeof(g_GlobalErrorMsg[0]));

} // namespace

void Connection::GlobalError(int Error)
{
    if (Error < 0 || Error >= g_GlobalErrorMsgCount) {
        LogMessage("GlobalError: unknown error code %d -- dropping\n", Error);
        return;
    }

    const char *msg = g_GlobalErrorMsg[Error];
    size_t msg_len = strlen(msg);

    // Wire layout (matches Win32 ClientToGlobalServer.cpp:36-49):
    //   [u32 length][u32 be(Error+7)][char msg[length]]
    char buffer[1024];
    char *p = buffer;
    *((int *) p) = (int) msg_len; p += 4;
    *((int *) p) = (int) ntohl((uint32_t)(Error + 7)); p += 4;
    memcpy(p, msg, msg_len);
    p += msg_len;

    SendResponse(ENB_OPCODE_0075_GLOBAL_ERROR,
                 (unsigned char *) buffer, (size_t)(p - buffer));
}

void Connection::SendGlobalTicket(long avatar_id, long sector_id, long level, bool issue)
{
    GlobalTicket ticket;
    memset(&ticket, 0, sizeof(ticket));
    unsigned char *ptr_ticket = (unsigned char *) &ticket;
    int index = 0;

    // Layout reproduced from Win32 ClientToGlobalServer.cpp:207-238.
    // The first BE32 slot is overloaded: success carries 0; failure
    // carries the level (re-purposed by the client as an error code --
    // 1000 / 1002).
    if (issue)
        AddDataFlip4(ptr_ticket, 0, index);
    else
        AddDataFlip4(ptr_ticket, level, index);

    index = 20;
    AddDataFlip4(ptr_ticket, avatar_id, index);
    AddDataFlip4(ptr_ticket, sector_id, index);
    index = 32;
    AddData(ptr_ticket, level, index);
    index = 48;
    AddDataS(ptr_ticket, (char *) "MY_Avatar_Ticket", index);

    SendResponse(ENB_OPCODE_006F_GLOBAL_TICKET,
                 (unsigned char *) &ticket, sizeof(ticket));
}

void Connection::ProcessGlobalTicket(long char_slot)
{
    // m_Player_Avatar_List is in network byte order (server fills it
    // via BuildAvatarList → BE on the wire). Pull out the two fields
    // ProcessGlobalTicket needs.
    m_SectorID = (long) ntohl((uint32_t) m_Player_Avatar_List.avatar[char_slot].info.sector_id);
    long admin_level = (long) ntohl((uint32_t) m_Player_Avatar_List.avatar[char_slot].info.admin_level);

    UDPClient *gc = g_ServerMgr ? g_ServerMgr->m_UDPGlobalClient : nullptr;
    long player_id = gc ? gc->PlayerID() : 0;

    LogMessage("ProcessGlobalTicket: GameID=0x%08lx sector=%ld admin=%ld\n",
               (long) player_id, (long) m_SectorID, (long) admin_level);

    SendGlobalTicket(player_id, m_SectorID, admin_level, true);
}

void Connection::SendAvatarList(long /*account_id*/)
{
    // The Win32 body of this is fully commented out -- the avatar list
    // is sent inline from HandleGlobalConnect / HandleCreateCharacter
    // / HandleDeleteCharacter using the buffer returned by SendTicket
    // / CreateCharacter / DeleteCharacter. Preserved here as a no-op
    // so the symbol resolves on Linux.
}

// ===========================================================================
// ProcessSectorServerOpcode -- client->server sector-plane dispatch
// ===========================================================================
//
// UDPClient::ForwardClientOpcode relays a client TCP frame onto the sector
// server's UDP port. The bottom-of-switch ForwardClientOpcode call is the
// default action: every opcode that doesn't `return` early gets pushed to
// the server (including the LOGIN/MOVE/LOGOFF cases -- the proxy may set some
// local state for them but the server is the authority that acts on them).
// The opcode set and per-opcode actions match a known-good proxy<->client
// stream; see the per-case notes.
//
// Per-opcode helpers:
//   - ProcessAction:       the six Action sub-cases (Dock/Land/Gate/etc.) drive
//                          gate-cache + launcher-handoff state in the reference;
//                          neither subsystem is wired here, so this is a no-op
//                          and the 0x2C frame forwards unmodified.
//   - HandleStarbaseRoomChange: log-only in the reference (a single
//                          NewRoom==-1 check); net no-op, 0x9F forwards.
//   - HandleWarp:          the reference pre-sends a positional update
//                          (SendPositionIfChanged) before the WARP forwards.
//                          That position feed reads the client's shared-memory
//                          avatar position and only functions when the proxy
//                          runs alongside the Win32 client; on the server-side
//                          build there is no client shared memory, so the
//                          pre-send is skipped and WARP forwards as-is. The
//                          server tolerates the absence exactly as it would a
//                          dropped UDP position packet. Carry-over for the full
//                          position-feed port (Phase AA Tier-2).

namespace {

void ProcessAction_Linux(Connection * /*conn*/, ActionPacket * /*action*/)
{
    // Win32 (ClientToSectorServer.cpp:706-740) is a switch on action->Action
    // with all six cases (7/8/18/19/28/29) having commented-out
    // //LogMessage bodies. Mirror as a no-op.
}

void HandleStarbaseRoomChange_Linux(Connection * /*conn*/,
                                    StarbaseRoomChange * /*change*/)
{
    // Win32 (ClientToSectorServer.cpp:742-750) has only a
    // `if (change->NewRoom == -1) { //LogMessage("Leaving starbase?\n"); }`
    // with the log line commented out -- net no-op. Mirror.
}

void HandleWarp_Linux(Connection * /*conn*/)
{
    // Win32 (ClientToSectorServer.cpp:752-756) calls
    //     g_ServerMgr->m_UDPClient->SendPositionIfChanged();
    // to flush a positional update before warp so the client doesn't
    // rubber-band. SendPositionIfChanged lives in UDPProxyMVAS.cpp which
    // is WIN32-walled -- porting it is part of the larger UDPProxyMVAS
    // port. Skipping the pre-send means the server may briefly see the
    // client at its pre-warp position; that's a UX nit, not a correctness
    // issue (the WARP opcode itself still forwards below).
}

} // namespace

void Connection::ProcessSectorServerOpcode(short opcode, short bytes)
{
    unsigned long tick = GetNet7TickCount();

    switch ((unsigned short) opcode) {
    case ENB_OPCODE_0002_LOGIN:
        // Matches Win32 ClientToSectorServer.cpp:22-31. Activates the proxy↔
        // server connection state so subsequent UDP traffic from the game server
        // gets relayed back to this client. time_debug=50 from Win32 is skipped:
        // it's only consumed by UDPProxyToClient.cpp, which is itself WIN32-walled.
        g_LoggedIn = true;
        g_ServerMgr->m_SectorConnection = this;
        LogMessage("<client> SectorServer LOGIN -- connection active\n");
        g_ServerMgr->m_UDPConnection->SetConnectionActive(true);
        g_ServerMgr->m_UDPClient->SetConnectionActive(true);
        g_ServerMgr->m_UDPConnection->SetLoginComplete(false);
        // m_UDPClient was the odd plane out: its m_LoginComplete was set true
        // at START (0x0005) but never reset here at the next sector LOGIN, so
        // the timer pump's m_LoginComplete gate would stay open through the
        // re-join handshake. Reset it in lockstep with m_UDPConnection and
        // m_UDPGlobalClient so every plane's pump is dormant during login.
        g_ServerMgr->m_UDPClient->SetLoginComplete(false);
        // Phase K: the server's MVASauth (3806) sends in-game UDP (0x2016
        // PACKET_SEQUENCE wrapping 0x2020 LOGIN_STAGE_S_C, position fan-out,
        // etc.) to the proxy's *global plane* source port (m_Player_Port is
        // captured from the AVATARLOGIN's source in server/src/UDP_Global.cpp).
        // The global-plane UDPClient's SendPacketSequence early-returns on
        // !m_ConnectionActive, so without this we drop every in-game packet
        // and the server's login-stage retry loop spins forever.
        //
        // NAT traversal (real internet, not docker-bridge): the global socket's
        // only prior outbound was the AVATARLOGIN to server:3810, so client-side
        // stateful NAT (home router + docker MASQUERADE) holds a mapping keyed on
        // remote port 3810. The server's in-game UDP arrives from a DIFFERENT
        // remote port -- MVASauth (server:3806, server/src/UDP_Master.cpp:73) --
        // so a restricted-cone/symmetric NAT drops it before our recv() and the
        // server times out at login stage 3. Punch a mapping for server:3806 by
        // sending a 0x3005 PLAYER_COMMS_ALIVE FROM the global socket TO 3806, in
        // lockstep right here: the player is already registered server-side
        // (SetupPlayer runs in UDP_Global.cpp GlobalTicketRequest at avatar
        // login, strictly before this sector LOGIN), so GetPlayer() resolves and
        // the server will NOT answer with 0x100A MVAS_TERMINATE. Then keep the
        // mapping warm on the global socket's own keepalive thread.
        // Primary source for the 0x3005-to-3806 packet: proxy/local-debug/
        // net7-live-2026-06-02-login-undock-clicknavs-warp-logout.pcap.
        if (g_ServerMgr->m_UDPGlobalClient) {
            g_ServerMgr->m_UDPGlobalClient->SetConnectionActive(true);
            g_ServerMgr->m_UDPGlobalClient->SetLoginComplete(false);
            g_ServerMgr->m_UDPGlobalClient->SetPlayerID(
                g_ServerMgr->m_UDPClient->PlayerID());
            g_ServerMgr->m_UDPGlobalClient->SendServerKeepalive();
            g_ServerMgr->m_UDPGlobalClient->StartMVASKeepalive();
        }
        m_SectorTCPRequest = false;
        break;

    case ENB_OPCODE_0006_START_ACK: {
        // Win32 ClientToSectorServer.cpp:33-53. Forward START_ACK to the
        // server, then send 0x3008 STARBASE_LOGIN_COMPLETE (if SectorID
        // > 9999 = starbase) or 0x3004 PLAYER_SHIP_SENT (in-space) so
        // the server knows the client finished its load. Both branches
        // mark the connection LoginComplete; KillTCPConnection() in the
        // ship branch closes the temporary auth socket (no-op on Linux --
        // KillTCPConnection lives in UDPProxyToClient.cpp which is
        // WIN32-walled; the equivalent close happens naturally as the
        // server tears the connection state on its side).
        if (!g_ServerMgr || !g_ServerMgr->m_UDPConnection ||
            !g_ServerMgr->m_UDPClient) break;

        g_ServerMgr->m_UDPConnection->ForwardClientOpcode(
            opcode, bytes, (char *) m_RecvBuffer);

        long player_id = g_ServerMgr->m_UDPClient->PlayerID();
        if (g_ServerMgr->m_UDPClient->GetSectorID() > 9999) {
            g_ServerMgr->m_UDPConnection->ForwardClientOpcode(
                ENB_OPCODE_3008_STARBASE_LOGIN_COMPLETE,
                sizeof(player_id), (char *) &player_id);
        } else {
            g_ServerMgr->m_UDPConnection->ForwardClientOpcode(
                ENB_OPCODE_3004_PLAYER_SHIP_SENT,
                sizeof(player_id), (char *) &player_id);
            // Win32 also KillTCPConnection() here -- see note above.
        }
        g_ServerMgr->m_UDPClient->SetLoginComplete(true);
        g_ServerMgr->m_UDPConnection->SetLoginComplete(true);
        if (g_ServerMgr->m_UDPGlobalClient)
            g_ServerMgr->m_UDPGlobalClient->SetLoginComplete(true);

        LogMessage("<client> START_ACK -> server (player_id=%ld start_id=%ld)\n",
                   (long) player_id, (long) *((int32_t *) &m_RecvBuffer[0]));
        m_SectorTCPRequest = false;
        return; // do NOT fall through to bottom forward (we already forwarded)
    }

    case ENB_OPCODE_0012_TURN: {
        // Throttle TURN to at most 1 per 250ms -- but ONLY while the avatar
        // is moving. The TURN payload carries the ship's current speed as a
        // float at offset 4 (the byte after GameID); it is exactly 0.0 when
        // the ship is stationary. A moving avatar's position is already
        // streamed continuously, so its turn spam is safely rate-limited; a
        // stationary avatar's turns must ALL reach the server or the ship
        // visibly stutters when the player mouse-looks in place. The gate is
        // therefore "drop iff within the 250ms window AND speed != 0", which
        // matches a known-good proxy<->client stream. (We previously threw
        // away stationary turns by throttling unconditionally.)
        //
        // The speed float lives at offset 4..7, so the read is only valid when
        // the frame actually carries it (GameID + speed = 8 bytes). A real
        // client TURN always does; an undersized/malformed frame does not, and
        // reading past its end would test a stale prior-packet byte. Default
        // speed to 0 in that case so the gate degrades to "never throttle,
        // always forward" -- the server, not the proxy, decides on a short
        // frame, and we never silently drop one on a stale-buffer misread.
        float speed = (bytes >= 8) ? *((float *) &m_RecvBuffer[4]) : 0.0f;
        if (tick <= (m_Turn_Sent + 250) && speed != 0.0f) {
            return; // throttled while moving
        }
        g_ServerMgr->m_UDPConnection->ForwardClientOpcode(
            opcode, bytes, (char *) m_RecvBuffer);
        m_Turn_Sent = tick;
        return; // do NOT fall through
    }

    case ENB_OPCODE_0013_TILT: {
        // Same moving-only 250ms throttle as TURN, with its own timestamp and
        // the same undersized-frame guard (read speed only when present).
        float speed = (bytes >= 8) ? *((float *) &m_RecvBuffer[4]) : 0.0f;
        if (tick <= (m_Tilt_Sent + 250) && speed != 0.0f) {
            return; // throttled while moving
        }
        g_ServerMgr->m_UDPConnection->ForwardClientOpcode(
            opcode, bytes, (char *) m_RecvBuffer);
        m_Tilt_Sent = tick;
        return; // do NOT fall through
    }

    case ENB_OPCODE_002C_ACTION:
        // Win32 ClientToSectorServer.cpp:88-92. Forward explicitly, then
        // call ProcessAction (all sub-cases are //commented-out logs).
        g_ServerMgr->m_UDPConnection->ForwardClientOpcode(
            opcode, bytes, (char *) m_RecvBuffer);
        ProcessAction_Linux(this, (ActionPacket *) m_RecvBuffer);
        return; // do NOT fall through (we already forwarded explicitly)

    case ENB_OPCODE_009B_WARP:
        // Win32 ClientToSectorServer.cpp:98-101. HandleWarp pre-sends a
        // positional update (skipped on Linux -- see helper note), then
        // falls to the bottom forward.
        HandleWarp_Linux(this);
        m_SectorTCPRequest = false;
        break; // fall through to bottom forward

    case ENB_OPCODE_0014_MOVE:
        // Win32 body is `break;` (line 55-56) -- falls to bottom forward.
        break;

    case ENB_OPCODE_009F_STARBASE_ROOM_CHANGE:
        // Win32 calls HandleStarbaseRoomChange (ClientToSectorServer.cpp:94-96),
        // whose body is entirely commented out -- net no-op. Falls through.
        HandleStarbaseRoomChange_Linux(this,
            (StarbaseRoomChange *) m_RecvBuffer);
        break;

    case ENB_OPCODE_00B9_LOGOFF_REQUEST:
        // LOGOFF sets the connection-active sentinel g_LoggedIn (the name is
        // historical -- it is polled by the master-server teardown path, not
        // a logged-in/out flag) and then forwards the opcode so the server
        // tears down its side of the connection. Matches a known-good
        // proxy<->client stream.
        g_LoggedIn = true;
        LogMessage("<client> SectorServer LOGOFF_REQUEST\n");
        break;

    // NOTE: 0x3A SERVER_HANDOFF has NO client->server case here on purpose.
    // In a known-good proxy<->client stream 0x3A is a SERVER->client opcode
    // (the proxy consumes the handoff side-effects in
    // UDPClient::IncommingOpcodePreProcessing on the S->C path, then forwards
    // it to the client). A client-originated 0x3A is not a real flow; were
    // one to arrive it simply falls to the default forward below, exactly as
    // the reference C->S sector dispatch does.

    default:
        LogVMessage("Linux: ProcessSectorServerOpcode 0x%04x (%d bytes) -- forwarding to server\n",
                    (unsigned short) opcode, (int) bytes);
        break;
    }

    // Bottom-of-switch forward -- Win32 line 108. Every opcode that
    // doesn't `return` early above ends up here. The server is the
    // authority on whether the opcode is meaningful in this connection
    // state; the proxy's job is just to relay.
    if (g_ServerMgr && g_ServerMgr->m_UDPConnection) {
        g_ServerMgr->m_UDPConnection->ForwardClientOpcode(
            opcode, bytes, (char *) m_RecvBuffer);
    }
}
