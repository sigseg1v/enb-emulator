// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya
//
// netredirect.h -- per-instance port-block remap for local multibox.

#pragma once

namespace enb {
namespace netredirect {

// Install the loopback connect() port remap used for local multibox.
//
// The EnB client dials FIXED ports baked into client.exe -- sector (TCP 3500),
// master (TCP 3801), global/auth (TCP 3805), posfeed (UDP 3807) -- and for the
// master and sector planes it always targets 127.0.0.1 (only the global/auth
// plane honours the -SERVER_ADDR argument; the master/sector host is effectively
// hardcoded to loopback regardless of network.ini or -SERVER_ADDR). On a single
// host that means every concurrent client would share one 127.0.0.1:3801, so a
// second client cannot get its own proxy.
//
// init() reads FREYA_GAME_PORT_BASE (or ENB_GAME_PORT_BASE) -- the base of a
// contiguous 4-port block -- and, when it names a non-default base, hooks ws2_32
// connect() so a dial to 127.0.0.1 on a fixed TCP game port is remapped to the
// per-instance block on the SAME loopback IP:
//   3500 -> base+0, 3801 -> base+1, 3805 -> base+2.
// The proxy listens on that mapping (native: FREYA_PROXY_PORT_BASE; docker:
// published ports). The posfeed plane (UDP 3807) is not a connect() and is
// handled by ClientPositionFeed via FREYA_POS_FEED_PORT (= base+3). No-op when
// the env is unset or names the stock base 3500 (instance 1 keeps default ports).
void init();

} // namespace netredirect
} // namespace enb
