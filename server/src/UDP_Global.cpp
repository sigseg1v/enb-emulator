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
#include <net7/Opcodes.h>
#include <net7/PacketStructures.h>
#include "PacketMethods.h"
#include "PlayerClass.h"
#include "MemoryHandler.h"
#include "SaveManager.h"   // Phase AM: EmitExternalStatusEvent
#include <net7/DtlsTransport.h>   // net7::DtlsPeerKey -- per-session (addr,port) key

#include <map>
#include <mutex>
#include <time.h>

// ---------------------------------------------------------------------------
// Multibox fix: per-session login-token stash.
//
// login_ticket is ONE ROW PER USERNAME (UPSERT ON CONFLICT username). A second
// concurrent login of the SAME account overwrites that row, so binding the
// player's DTLS auth token by re-querying login_ticket at character-select
// (GetLoginTokenBinary(username)) can bind a DIFFERENT session's token. The
// mis-bound session's gameplay datagrams then fail the per-packet token check
// at the recv edge, its 0x2008 master handoff is dropped, and it hangs at the
// loading screen -- exactly the two-different-characters-same-account case the
// server otherwise explicitly supports (see the multibox NOTE in
// ProcessTicketInfo below, and Player::SetCharacterID -> CheckForDuplicatePlayers
// which only blocks the SAME character twice).
//
// The authoritative per-session token is the ticket SUFFIX the session itself
// presented at ProcessTicketInfo (the same 32-hex token its proxy learned from
// the ticket and wraps every datagram with). Both the ticket-info and the later
// avatar-login for one proxy session arrive on the same global UDP socket, i.e.
// the same (source_addr, source_port). Stash the validated suffix keyed by that
// session key at ticket time, then bind THAT token at character-select instead
// of the clobber-prone DB re-query.
//
// This does not weaken the DTLS posture: the suffix was already validated
// against login_ticket in ProcessTicketInfo (ValidateTicketSuffix) before it is
// stashed, and the per-packet token check at the recv edge is unchanged. It is
// strictly MORE correct than the DB re-query, which could bind a token the
// session never presented.
namespace {

struct SessionToken {
    unsigned char token[16];
    long          expires_ms;   // prune entries older than this
};

std::mutex                       g_SessionTokenMtx;
std::map<uint64_t, SessionToken> g_SessionTokens;

// Login tickets expire ~5 min after issue; bound the stash to that window so a
// session that presents a ticket but never selects a character cannot leak an
// entry indefinitely.
const long kSessionTokenTtlMs = 5 * 60 * 1000L;

void StashSessionToken(long source_addr, short source_port, const unsigned char token[16])
{
    uint64_t key = net7::DtlsPeerKey((uint32_t)source_addr, (uint16_t)source_port);
    long now_ms = (long)time(NULL) * 1000L;

    std::lock_guard<std::mutex> lk(g_SessionTokenMtx);
    // Opportunistically prune expired entries so the map stays bounded to the
    // set of sessions within the ticket-expiry window.
    for (auto it = g_SessionTokens.begin(); it != g_SessionTokens.end(); )
        it = (it->second.expires_ms < now_ms) ? g_SessionTokens.erase(it) : std::next(it);

    SessionToken &e = g_SessionTokens[key];
    memcpy(e.token, token, 16);
    e.expires_ms = now_ms + kSessionTokenTtlMs;
}

// Consume-on-bind: a session selects one character per ticket presentation, and
// every avatar-login is preceded by a fresh ProcessTicketInfo, so erasing on
// bind both frees the entry and prevents a stale token from surviving.
bool TakeSessionToken(long source_addr, short source_port, unsigned char out[16])
{
    uint64_t key = net7::DtlsPeerKey((uint32_t)source_addr, (uint16_t)source_port);
    long now_ms = (long)time(NULL) * 1000L;

    std::lock_guard<std::mutex> lk(g_SessionTokenMtx);
    auto it = g_SessionTokens.find(key);
    if (it == g_SessionTokens.end()) return false;
    bool fresh = (it->second.expires_ms >= now_ms);
    if (fresh) memcpy(out, it->second.token, 16);
    g_SessionTokens.erase(it);
    return fresh;
}

} // namespace

#define G_ERROR_BANNED_ACCOUNT		0
#define G_ERROR_NICKNAME_USED		1
#define	G_ERROR_INVALID_CHARS		2
#define	G_ERROR_TOO_SHORT			3
#define	G_ERROR_ONE_VOWEL			4
#define	G_ERROR_REPEATING_CHAR		5
#define	G_ERROR_RESTRICTED_LIST		6
#define	G_ERROR_TICKET_INVALID		7
#define	G_ERROR_AUTH_SERVER_DOWN	8
#define G_ERROR_INACTIVE_ACCOUNT	9
#define	G_ERROR_RESTRICTED_SHIP		10
#define	G_ERROR_NET7_INTERNAL		11
#define G_ERROR_STRESS_TEST_CLOSED	12

void UDP_Connection::HandleGlobalOpcode(char *msg, EnbUdpHeader *hdr, const long source_addr, const short source_port)
{
    switch (hdr->opcode)
    {
    case ENB_OPCODE_2000_ACCOUNTDATA:
        LogMessage("UDP AccountData received\n");
        VerifyAccountInfo(msg, hdr, source_addr, source_port);
        break;

    case ENB_OPCODE_2002_TICKET:
        LogMessage("Received ticket!\n");
        ProcessTicketInfo(msg, hdr, source_addr, source_port);
        break;

    case ENB_OPCODE_2004_AVATARLOGIN:
        LogMessage("Received AvatarLogin confirm (start player)\n");
        HandleGlobalTicketRequest(msg, hdr, source_addr, source_port);
        break;

    case ENB_OPCODE_200B_CREATE_AVATAR:
        LogMessage("Received Create Avatar packet\n");
        HandleAvatarCreateRequest(msg, hdr, source_addr, source_port);
        break;

    case ENB_OPCODE_200D_DELETE_AVATAR:
        LogMessage("Received Delete Avatar packet\n");
        HandleAvatarDeleteRequest(msg, hdr, source_addr, source_port);
        break;
 
    default:					
        LogMessage("bad G-UDP opcode, id 0x%04X\n",hdr->opcode);
        break;
    }   
}

bool UDP_Connection::VerifyAccountInfo(char *msg, EnbUdpHeader *hdr, const long source_addr, const short source_port)
{
    char username[64];
    char password[64];
    int index = 0;
    char info[128] = "Valid=False\r\n";
    long length = 13;

    ExtractDataLS((unsigned char*)msg, username, index);
    ExtractDataLS((unsigned char*)msg, password, index);

    LogMessage("Account info received: %s %s\n", username, password);

	if (username && password)
	{
        char * ticket = 0;

        unsigned char *ip = (unsigned char *)&source_addr;
	    LogMessage("User '%s' logging in from IP: %u.%u.%u.%u\n", username, ip[0], ip[1], ip[2], ip[3]);

        // TODO: This is a good place to check for "banned" IP addresses.
        // TODO: we need to create a list of accounts which are logged in successfully, so Net7Proxy create/delete can't be abused

        if (ticket = g_AccountMgr->IssueTicket(username, password))
        {
		    length = sprintf_s(info, sizeof(info), "Valid=TRUE\r\nTicket=%s\r\n", ticket);
            LogMessage("Valid ticket %s\n",ticket);
        }
	}

    //now transmit a 2001 validate account message
    SendOpcode(ENB_OPCODE_2001_ACCOUNTVALID, (unsigned char *) &info, length, source_addr, source_port);

    return true;
}

bool UDP_Connection::ProcessTicketInfo(char *msg, EnbUdpHeader *hdr, const long source_addr, const short source_port)
{
    //ok, we have a new ticket, lets validate the ticket and send the avatarlist
    char *ticket = (char *) &msg[0];

    // Split "USERNAME-<32hex>" on the LAST '-'. The token suffix is pure hex and
    // never contains a dash, but a username legitimately can, so a first-dash
    // split (the old strtok_s) misparsed dashed usernames -- account_name was
    // truncated and the account lookup / ValidateTicketSuffix failed, locking
    // those accounts out. Mirror the proxy's strrchr-based parse in
    // ClientToServer_linux_stubs.cpp::HandleGlobalConnect so both sides agree.
    char *suffix = strrchr(ticket, '-');
    char *account_name = (*ticket) ? ticket : NULL;
    if (suffix)
    {
        *suffix = '\0';     // terminate the username portion
        suffix++;           // suffix now points at the hex token
    }

    fprintf(stderr,"We got accountname back from UDP: %s\n", account_name);

    if (!account_name)
    {
		LogDebug("HandleGlobalConnect() - Invalid Username\n");
		SendGlobalError(G_ERROR_NET7_INTERNAL, source_addr, source_port);
        return false;
    }

    // AH-9: validate the ticket SUFFIX (the part after the first '-'), not
    // just the username. The Win32 server received issued tickets over an
    // SSL handoff (RegisterSectorServer) and matched them here; the Linux
    // port dropped that handoff, so until now ProcessTicketInfo trusted any
    // "username-anything" presented to the cleartext global UDP port. The
    // issuer (login-server BuildTicketLocked, or this server's BuildTicket)
    // now persists the suffix in net7_user.login_ticket; reject if the
    // presented suffix has no matching, unexpired row. Do NOT log the token.
    if (!g_AccountMgr->ValidateTicketSuffix(account_name, suffix ? suffix : ""))
    {
        unsigned char *ip = (unsigned char *)&source_addr;
        LogMessage("ProcessTicketInfo: rejected invalid/expired ticket for "
                   "'%s' from %u.%u.%u.%u\n",
                   account_name, ip[0], ip[1], ip[2], ip[3]);
        SendGlobalError(G_ERROR_TICKET_INVALID, source_addr, source_port);
        return false;
    }

    // Multibox fix: remember THIS session's validated token, keyed by its global
    // UDP (addr,port), so character-select can bind the token the session itself
    // presented rather than re-querying the username-keyed login_ticket row (which
    // a second same-account login clobbers). See the stash block at the top of
    // this file. The suffix is 32 hex chars -> 16 binary bytes.
    {
        unsigned char sess_tok[16];
        if (suffix && g_AccountMgr->TokenHexToBinary(suffix, sess_tok))
            StashSessionToken(source_addr, source_port, sess_tok);
    }

    long account_id = g_AccountMgr->GetAccountID(account_name);
	long account_status = g_AccountMgr->GetAccountStatus(account_name);

	// NOTE: no account-level "already logged in" gate here. Multiboxing is allowed
	// -- two DIFFERENT characters on the same account may be online at once. Only a
	// duplicate of the SAME character is prevented, and that is enforced later at
	// character-select time (Player::SetCharacterID -> CheckForDuplicatePlayers,
	// keyed on CharacterID), where the chosen avatar is finally known. The account
	// is not known to be "in use" by a character until then.

	//block people entering while stress server down
	if (account_status == 0)
	{
		SendGlobalError(G_ERROR_STRESS_TEST_CLOSED, source_addr, source_port);
        return false;
	}

    if (account_id == -1)
    {
		LogDebug("HandleGlobalConnect() - Invalid AccountID\n");
		SendGlobalError(G_ERROR_NET7_INTERNAL, source_addr, source_port);
        return false;
    }

	if (account_status == -2)		// Banned
	{
		LogDebug("HandleGlobalConnect() - Banned Account\n");
		SendGlobalError(G_ERROR_BANNED_ACCOUNT, source_addr, source_port);
		return false;
	}

	if (account_status == -1)		// Complete Registration
	{
		LogDebug("HandleGlobalConnect() - Inactive Account\n");
		SendGlobalError(G_ERROR_INACTIVE_ACCOUNT, source_addr, source_port);
		return false;
	}

    // Record the account login time so status tooling (and the boot-time stale-
    // session sweep) can tell this account is online: an account is online iff
    // accounts.last_login > accounts.last_logout. The legacy username+password
    // path sets this via IssueTicket->UpdateLoginTime, but the online ticket
    // path validates a pre-issued login_ticket and never called it, so online
    // logins never marked the account online (status always reported 0). This
    // fires at character-select, which is exactly when the account is "online"
    // per the status predicate (it counts char-select sessions, no avatar yet).
    g_AccountMgr->UpdateLoginTime(account_id);

    SendAvatarList(account_id, source_addr, source_port);

    return true;
}

void UDP_Connection::SendGlobalError(int error, const long source_addr, const short source_port)
{
    SendOpcode(ENB_OPCODE_200E_GLOBAL_ERROR, (unsigned char *) &error, sizeof(int), source_addr, source_port);
}

//This sends the client the avatar list after: Loggin in, deleting character, creating character
void UDP_Connection::SendAvatarList(long account_id, const long source_addr, const short source_port)
{
    GlobalAvatarList avatar_list;
    g_AccountMgr->BuildAvatarList(&avatar_list, account_id);
    SendOpcode(ENB_OPCODE_2003_AVATARLIST, (unsigned char *) &avatar_list, sizeof(GlobalAvatarList), source_addr, source_port);
    fprintf(stderr,"Sent avatarlist (%d)\n", sizeof(GlobalAvatarList));
}

void UDP_Connection::HandleGlobalTicketRequest(char *msg, EnbUdpHeader *hdr, const long source_addr, const short source_port)
{
    // The player selected a character.
    // Phase K: wire field is 4 bytes (proxy's UDPClient_linux.cpp::SendAvatarLogin
    // writes via AddData / ntohl which produces an int32_t). Reading as Linux
    // `long` (8 bytes) used to pull the trailing length-prefix of the username
    // into the high half of char_slot, so every Linux GlobalTicketRequest
    // resolved to a wrong slot and GetAvatarID returned -1.
    // Phase K (slot 1+ fix): proxy applies ntohl(char_slot) before AddData,
    // i.e. the wire field is big-endian. Reading as raw int32_t LE silently
    // worked for slot 0 (0x00 swaps to 0x00) but produced 0x01000000 for
    // slot 1, failing GetAvatarID's [0,4] range check and surfacing as
    // "galaxy full" (error 1002) on the client. ntohl here to match the
    // proxy's BE wire encoding.
    long char_slot = (long) ntohl((uint32_t) *(int32_t *) msg);
    char username[128];
    int index = 4;
    ExtractDataLS((unsigned char*)msg, username, index);
    long avatar_id = g_AccountMgr->GetAvatarID(username, char_slot);

    if (avatar_id == -1)
    {
        LogMessage("GlobalTicketRequest() Error obtaining global character slot id\n");
        return;
    }

    LogMessage("Received GlobalTicketRequest packet -- User: `%s`, Slot: %d\n", username, char_slot);

    //Now finally we can allocate a player slot, since the player is logging into the game
    Player *player = g_ServerMgr->m_GlobMemMgr->GetPlayerNode(username);

    if (player)
    {
        LogMessage("Allocated %s a playerID of 0x%08x [%d]\n", username, player->GameID(), player->CharacterID());

        player->SetCharacterID(avatar_id);
        player->SetCharacterSlot(char_slot);
		g_PlayerMgr->SetupPlayer(player, source_addr);
        player->SetAccountUsername(username);
		player->SetGameID(player->CharacterID() | PLAYER_TAG);

		player->SetPlayerPortIP(source_port, source_addr);

        // Phase AH (AH-9): bind the account's per-packet auth token (the
        // 16-byte binary login-ticket suffix) to this player. Over DTLS, every
        // gameplay C->S datagram (player_id != 0) must carry a matching token
        // or it is dropped at the recv edge (UDP_Connection::RunRecvThread).
        // Functionally a no-op when DTLS is opted out -- the plaintext path
        // never checks the token. Fail-closed: if the token cannot be bound,
        // DTLS gameplay for this player is dropped rather than trusted.
        // Multibox fix: bind the token THIS session presented at ProcessTicketInfo
        // (stashed by its global (addr,port)), NOT a re-query of the username-keyed
        // login_ticket row -- that row is clobbered by a second concurrent
        // same-account login, which would bind the wrong session's token and drop
        // this session's gameplay/handoff at the DTLS recv edge. Fall back to the
        // DB query only if the stash missed (e.g. a legacy/plaintext path that did
        // not route through ProcessTicketInfo).
        unsigned char auth_tok[16];
        if (TakeSessionToken(source_addr, source_port, auth_tok))
            player->SetAuthToken(auth_tok);
        else if (g_AccountMgr->GetLoginTokenBinary(username, auth_tok))
            player->SetAuthToken(auth_tok);
        else
            LogMessage("HandleGlobalTicketRequest: no login token to bind for '%s' "
                       "(DTLS gameplay will be rejected)\n", username);

        //Read the character database
        g_AccountMgr->ReadDatabase(player->Database(), avatar_id);
        player->SetName(player->Database()->avatar.avatar_first_name);

        //Now send the avatar_id and relay the new player's GameID to confirm login.
        // Phase K: wire payload is 4 bytes (proxy reads via int32_t cast in
        // UDPClient_linux.cpp::SendAvatarLogin). Sending sizeof(long)=8 on
        // Linux x86_64 trailed 4 bytes of garbage into the UDP frame.
        int32_t avatar_id_wire = (int32_t) avatar_id;
        SendOpcode(ENB_OPCODE_2005_AVATARLOGIN_CONFIRM, player, (unsigned char *) &avatar_id_wire, sizeof(avatar_id_wire), source_addr, source_port);

        // Phase AM: announce the login as an external-status event. This is the
        // once-per-login moment (the global ticket / character-select stage);
        // the per-sector FinishLogin path fires on every gate jump, so it would
        // spam. GetPlayerCount() already counts this node, so the online tally
        // reflects this login. No-op unless NET7_EXTERNAL_STATUS_ENABLED.
        //
        // AM-10: read the levels from the database struct just populated by
        // ReadDatabase() above, NOT from player->CombatLevel()/Trade/Explore.
        // Those getters read PlayerIndex()->RPGInfo, which is not loaded until
        // sector login (PlayerSaves.cpp LoadSavedData), so at this stage they
        // all return 0 -- which is why the bot printed "C0/T0/E0". The DB fields
        // are stored big-endian (AccountManager::ReadDatabase ntohl's on the way
        // in from the row), so ntohl back to host order here.
        {
            // Indexed by ClassIndex() == Race*3 + Profession (Net7.h).
            static const char *kClassAbbrev[9] =
                { "TE", "TT", "TS", "JD", "JS", "JE", "PW", "PP", "PS" };
            long ci = player->ClassIndex();
            const char *cls = (ci >= 0 && ci < 9) ? kClassAbbrev[ci] : "??";
            long combat  = (long)(int32_t) ntohl(player->Database()->info.combat_level);
            long trade   = (long)(int32_t) ntohl(player->Database()->info.trade_level);
            long explore = (long)(int32_t) ntohl(player->Database()->info.explore_level);
            char line[256];
            snprintf(line, sizeof(line),
                "Player %s (level: C%ld/T%ld/E%ld, class: %s) logged in. (%ld online)",
                player->Name(), combat, trade, explore, cls,
                g_ServerMgr->m_GlobMemMgr->GetPlayerCount());
            EmitExternalStatusEvent(EXT_STATUS_LOGIN, line);
        }
    }
    else
    {
		LogMessage("** Critical Error: Can't get AvatarID [%d]! **\n", avatar_id);
        SendGlobalError(G_ERROR_NET7_INTERNAL, source_addr, source_port);
        //SendGlobalTicket(player->GameID(), 0, 1002, false); //galaxy full
    }
}

void UDP_Connection::HandleAvatarCreateRequest(char *msg, EnbUdpHeader *hdr, const long source_addr, const short source_port)
{
    GlobalCreateCharacter *create = (GlobalCreateCharacter *) msg;

    char *username = create->account_username;//m_AccountUsername = g_StringMgr->GetStr(create->account_username);
    long ret_val = g_AccountMgr->CreateCharacter(create);

    if (ret_val != -1)
    {
        SendGlobalError(ret_val, source_addr, source_port);
        return;
    }

    LogDebug("New character `%s` created\n", create->avatar.avatar_first_name);

    SendAvatarList(g_AccountMgr->GetAccountID(username), source_addr, source_port);
}

void UDP_Connection::RegisterIP(long addr)
{
	IP_vec::iterator itrIP;
	
	for (itrIP = m_RegisteredAddrs.begin(); itrIP < m_RegisteredAddrs.end(); itrIP++)
	{
		if (*itrIP == addr)
		{
			return;
		}
	}

	m_RegisteredAddrs.push_back(addr);
}

bool UDP_Connection::IsRegisteredIP(long addr)
{
	IP_vec::iterator itrIP;
	
	for (itrIP = m_RegisteredAddrs.begin(); itrIP < m_RegisteredAddrs.end(); itrIP++)
	{
		if (*itrIP == addr)
		{
			return true;
		}
	}

	return false;
}

void UDP_Connection::HandleAvatarDeleteRequest(char *msg, EnbUdpHeader *hdr, const long source_addr, const short source_port)
{
    long character_slot;
    char username[64];
    int index = 0;

    // Same proxy-side encoding convention as HandleGlobalTicketRequest:
    // proxy's DeleteCharacter (UDPClient_linux.cpp / UDPProxyToGlobal.cpp)
    // writes `AddData(data, ntohl((uint32_t) character_slot), index)`, so
    // the on-wire slot is big-endian. ExtractLong reads host-LE, which
    // worked accidentally for slot 0 only. Apply ntohl on read so any
    // slot resolves correctly.
    character_slot = (long) ntohl((uint32_t) ExtractLong((unsigned char*)msg, index));
    ExtractDataLS((unsigned char*)msg, username, index);

	LogMessage("Delete params: Account %s, slot: %d\n", username, character_slot);
    
	long avatar_id = g_AccountMgr->GetAvatarID(username, character_slot);

	LogMessage("Delete character %d\n", avatar_id);

    g_AccountMgr->DeleteCharacter(avatar_id);

    SendAvatarList(g_AccountMgr->GetAccountID(username), source_addr, source_port);
}
