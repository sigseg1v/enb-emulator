// common/include/net7/Ports.h
/* Net-7 Entertainment: Net-7 Earth and Beyond emulator project
**
** This code/content is licensed under the Creative Commons license, it is interactive content. You can view the terms of our:
** Creative Commons Attribution-Noncommercial-Share Alike 3.0 United States License
** http://creativecommons.org/licenses/by-nc-sa/3.0/us/
**
** Copyright of our assets/code/software began in 2005-2009 ©, Net-7 Entertainment.
**
** Phase R Wave 2 (2026-05-23): extracted from per-process Net7.h /
** Net7SSL.h so the wire-load-bearing port assignments live in exactly
** one place, shared across proxy/, server/, and login-server/.
**
** Drift caught during extraction: SECTOR_SERVER_PORT was 3500 in
** proxy/Net7.h vs 3501 in server/Net7.h + login-server/Net7SSL/Net7SSL.h.
** The split was intentional — proxy binds 3500 as its own local TCP port,
** sector servers bind 3501+ — but the same macro name meant two different
** things in two trees. Split into PROXY_LOCAL_TCP_PORT (3500) and
** SECTOR_SERVER_PORT (3501) here; proxy call sites were rewritten.
*/

#ifndef _NET7_PORTS_H_INCLUDED_
#define _NET7_PORTS_H_INCLUDED_

// Authentication. Net7SSL terminates TLSv1.3, parses /AuthLogin against
// the user DB, and issues a 20-byte ticket the client carries through the
// global → master → sector handoff chain.
#define SSL_PORT                    443     // handles authentication (0x01BB)

// TCP control plane.
#define GLOBAL_SERVER_PORT          3805    // proxy listener; multiplexes galaxies
#define MASTER_SERVER_PORT          3801    // per-galaxy master handler
#define SECTOR_SERVER_PORT          3501    // base port for sector servers; each sector adds an offset

// Proxy's own local TCP terminator. Distinct from SECTOR_SERVER_PORT
// because the proxy itself binds 3500 and forwards to 3501+ sector
// servers behind it. Comment in the original Net7SSL.h header:
//   "we start from 3501 now because 3500 is used as the local TCP port in Net7Proxy"
#define PROXY_LOCAL_TCP_PORT        3500

// MVAS launcher hookup (client-side process; here for the port number only).
#define MVAS_LOGIN_PORT             3806

// Out-of-band logins/UDP control.
#define SSL_LOCALCERT_LOGIN_PORT    3807
#define UDP_MASTER_SERVER_PORT      3808
#define PROXY_SERVER_PORT           3809

// Connection-type tags (constants, not ports — kept here because they're
// referenced in the same blocks the port macros are):
#define CLIENT_TYPE_FIXED_PORT      1
#define CLIENT_TYPE_MULTI_PORT      2

#endif // _NET7_PORTS_H_INCLUDED_
