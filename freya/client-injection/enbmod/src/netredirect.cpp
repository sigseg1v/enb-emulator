// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya
//
// netredirect.cpp -- rewrite the client's fixed-loopback game dials to a
// per-instance loopback IP, so several local clients each reach their OWN proxy.
//
// Why a connect() hook and not config: the client honours -SERVER_ADDR only for
// the global/auth plane (TCP 3805); the master (3801) and sector (3500) planes
// always dial 127.0.0.1 no matter what network.ini [MasterServer] or the redirect
// advertises. The only mechanism-independent fix is to intercept the dial itself.
// We already inject this DLL, so a ws2_32 connect() detour is the lightest tool:
// no network namespaces, no root, no server or proxy change.

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

// Network-order replacement loopback address (e.g. 127.0.0.2). 0 = inactive.
unsigned long g_target = 0;

// The fixed ports client.exe dials on loopback; net_port is network byte order.
bool is_game_port(uint16_t net_port) {
    uint16_t p = ntohs(net_port);
    return p == 3500 || p == 3801 || p == 3805 || p == 3807;
}

int WSAAPI hk_connect(SOCKET s, const struct sockaddr* name, int namelen) {
    if (name && namelen >= (int)sizeof(sockaddr_in) && name->sa_family == AF_INET) {
        const sockaddr_in* in = reinterpret_cast<const sockaddr_in*>(name);
        // Only a 127.0.0.1 dial on one of the four fixed game ports is rewritten.
        // The shared LocalAuthRelay (127.0.0.1:4180) and everything else are left
        // untouched, and a dial already aimed at .N (the global plane via
        // -SERVER_ADDR) does not match 127.0.0.1 so it passes straight through.
        if (in->sin_addr.s_addr == htonl(INADDR_LOOPBACK) && is_game_port(in->sin_port)) {
            sockaddr_in copy = *in;
            copy.sin_addr.s_addr = g_target;
            return real_connect(s, reinterpret_cast<const sockaddr*>(&copy), namelen);
        }
    }
    return real_connect(s, name, namelen);
}

} // namespace

void init() {
    char buf[64];
    DWORD n = GetEnvironmentVariableA("FREYA_GAME_HOST", buf, sizeof buf);
    if (n == 0 || n >= sizeof buf)
        n = GetEnvironmentVariableA("ENB_GAME_HOST", buf, sizeof buf);
    if (n == 0 || n >= sizeof buf)
        return; // not requested -- ordinary launch, no redirect

    unsigned long target = inet_addr(buf);
    if (target == INADDR_NONE || target == htonl(INADDR_LOOPBACK))
        return; // unset / malformed / plain 127.0.0.1 -- instance 1 keeps loopback
    g_target = target;

    // Make sure ws2_32 is resident before the master connect (which happens long
    // after DLL attach, but a LoadLibrary here is cheap and removes the ordering
    // assumption). MinHook is already initialised by hooks::mh_init().
    HMODULE ws = LoadLibraryA("ws2_32.dll");
    if (!ws) {
        logf("netredirect: ws2_32.dll not present -- redirect disabled");
        return;
    }
    void* fn = reinterpret_cast<void*>(GetProcAddress(ws, "connect"));
    if (!fn) {
        logf("netredirect: connect() not found -- redirect disabled");
        return;
    }
    if (MH_CreateHook(fn, reinterpret_cast<void*>(&hk_connect),
                      reinterpret_cast<void**>(&real_connect)) != MH_OK ||
        MH_EnableHook(fn) != MH_OK) {
        logf("netredirect: failed to hook connect() -- redirect disabled");
        return;
    }
    logf("netredirect: loopback game ports (3500/3801/3805/3807) -> %s", buf);
}

} // namespace netredirect
} // namespace enb
