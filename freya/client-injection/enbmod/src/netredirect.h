// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya
//
// netredirect.h -- per-instance loopback redirect for local multibox.

#pragma once

namespace enb {
namespace netredirect {

// Install the loopback connect() redirect used by `just play-multibox-local`.
//
// The EnB client dials FIXED ports baked into client.exe -- sector (TCP 3500),
// master (TCP 3801), global/auth (TCP 3805), posfeed (UDP 3807) -- and for the
// master and sector planes it always targets 127.0.0.1 (only the global/auth
// plane honours the -SERVER_ADDR argument; the master/sector host is effectively
// hardcoded to loopback regardless of network.ini or -SERVER_ADDR). On a single
// host that means every concurrent client would share one 127.0.0.1:3801, so a
// second client cannot get its own proxy.
//
// init() reads FREYA_GAME_HOST (or ENB_GAME_HOST) -- an IPv4 dotted quad such as
// 127.0.0.2 -- and, when it names a non-default loopback address, hooks ws2_32
// connect() so any dial to 127.0.0.1 on one of the four fixed ports is rewritten
// to that per-instance loopback IP. The recipe's per-client socat facade binds
// 127.0.0.N on those ports and forwards to that client's own proxy. No-op when
// the env is unset or 127.0.0.1 (instance 1 keeps plain loopback).
void init();

} // namespace netredirect
} // namespace enb
