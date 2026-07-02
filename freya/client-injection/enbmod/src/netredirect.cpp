// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya
//
// netredirect.cpp -- remap the client's fixed-loopback game dials to a
// per-instance PORT block on 127.0.0.1, so several local clients each reach
// their OWN proxy without giving each a different loopback IP.
//
// Why a connect() hook and not config: the client honours -SERVER_ADDR only for
// the global/auth plane (TCP 3805); the master (3801) and sector (3500) planes
// always dial 127.0.0.1 no matter what network.ini [MasterServer] or the redirect
// advertises. The only mechanism-independent fix is to intercept the dial itself.
// We already inject this DLL, so a ws2_32 connect() detour is the lightest tool:
// no network namespaces, no root, no distinct loopback aliases (Windows-portable).
//
// The four fixed game ports the client dials are non-contiguous (sector 3500,
// master 3801, global 3805, posfeed 3807). This hook COLLAPSES the three TCP
// dials into a contiguous 4-port block [base, base+1, base+2] on 127.0.0.1:
//   3500 (sector) -> base+0
//   3801 (master) -> base+1
//   3805 (global) -> base+2
// The proxy listens on exactly that mapping (FREYA_PROXY_PORT_BASE, or docker
// publishes the block). The posfeed plane (UDP 3807) is sendto-based, not a
// connect(), so it is NOT remapped here -- ClientPositionFeed handles it via
// FREYA_POS_FEED_PORT (= base+3).

#include "netredirect.h"
#include "log.h"

#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>

#include "MinHook.h"

#include <cstdint>

namespace enb {
namespace netredirect {
namespace {

// We resolve connect() from ws2_32 at runtime and own its typedef.
using connect_fn = int(WSAAPI*)(SOCKET, const struct sockaddr*, int);
connect_fn real_connect = nullptr;

// Per-instance contiguous port block base on 127.0.0.1. 0 = inactive.
uint16_t g_port_base = 0;

// Map one of the client's fixed TCP game ports to its slot in the per-instance
// block. Returns 0 for a port we do not remap (caller passes it through).
uint16_t remap_port(uint16_t host_port) {
    switch (host_port) {
    case 3500:
        return (uint16_t)(g_port_base + 0); // sector
    case 3801:
        return (uint16_t)(g_port_base + 1); // master
    case 3805:
        return (uint16_t)(g_port_base + 2); // global / auth
    default:
        return 0;
    }
}

int WSAAPI hk_connect(SOCKET s, const struct sockaddr* name, int namelen) {
    if (name && namelen >= (int)sizeof(sockaddr_in) && name->sa_family == AF_INET) {
        const sockaddr_in* in = reinterpret_cast<const sockaddr_in*>(name);
        // Only a 127.0.0.1 dial on one of the three fixed TCP game ports is
        // remapped. The shared LocalAuthRelay (127.0.0.1:4180) and everything
        // else are left untouched.
        if (in->sin_addr.s_addr == htonl(INADDR_LOOPBACK)) {
            uint16_t dst = remap_port(ntohs(in->sin_port));
            if (dst != 0) {
                sockaddr_in copy = *in;
                copy.sin_port = htons(dst);
                return real_connect(s, reinterpret_cast<const sockaddr*>(&copy), namelen);
            }
        }
    }
    return real_connect(s, name, namelen);
}

} // namespace

void init() {
    char buf[64];
    DWORD n = GetEnvironmentVariableA("FREYA_GAME_PORT_BASE", buf, sizeof buf);
    if (n == 0 || n >= sizeof buf)
        n = GetEnvironmentVariableA("ENB_GAME_PORT_BASE", buf, sizeof buf);
    if (n == 0 || n >= sizeof buf)
        return; // not requested -- ordinary launch, no remap

    long base = strtol(buf, nullptr, 10);
    if (base <= 0 || base > 65535 - 3)
        return; // unset / malformed / no room for a 4-port block
    if (base == 3500)
        return; // the stock block -- instance 1 keeps default ports, no remap
    g_port_base = (uint16_t)base;

    // Make sure ws2_32 is resident before the master connect (which happens long
    // after DLL attach, but a LoadLibrary here is cheap and removes the ordering
    // assumption). MinHook is already initialised by hooks::mh_init().
    HMODULE ws = LoadLibraryA("ws2_32.dll");
    if (!ws) {
        logf("netredirect: ws2_32.dll not present -- remap disabled");
        return;
    }
    void* fn = reinterpret_cast<void*>(GetProcAddress(ws, "connect"));
    if (!fn) {
        logf("netredirect: connect() not found -- remap disabled");
        return;
    }
    if (MH_CreateHook(fn, reinterpret_cast<void*>(&hk_connect),
                      reinterpret_cast<void**>(&real_connect)) != MH_OK ||
        MH_EnableHook(fn) != MH_OK) {
        logf("netredirect: failed to hook connect() -- remap disabled");
        return;
    }
    logf("netredirect: loopback game ports 3500/3801/3805 -> 127.0.0.1:%d/%d/%d", g_port_base + 0,
         g_port_base + 1, g_port_base + 2);
}

} // namespace netredirect
} // namespace enb
