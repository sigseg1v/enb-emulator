// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya
//==============================================================================
// ClientPositionFeed.cpp
//
// In-client producer for the MVAS position feed (PB-2). See ClientPositionFeed.h
// for the design. This file owns the loopback-datagram send and the polling
// thread; the actual engine read is isolated in ReadEngineShipState(), backed by
// the build-specific addresses in ClientEngineOffsets.h.
//
// Transport: a fixed 40-byte UDP datagram to FREYA_CLIENT_POS_PORT on loopback,
// ~10x/sec. The proxy binds that port and streams the latest sample to the
// server as 0x1004. A loopback datagram is the one transport that works in all
// three run modes -- crucially play-local, where the proxy is a Linux docker
// process the client cannot share a Win32 named mapping with.
//==============================================================================
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <process.h>
#include <cstring>

#pragma comment(lib, "ws2_32.lib")

// The proxy<->hook IPC contract. It lives here with the producer (this is the
// self-contained injection unit); the proxy includes the same header from this
// directory so both ends compile against ONE definition.
#include "ClientPositionShared.h"

//------------------------------------------------------------------------------
// ReadEngineShipState.
//
// Reads the ship's current world position (x,y,z) and orientation (x,y,z) from
// the running engine, returning true once a live in-space sample exists and
// false whenever there is no valid position yet (loading, docked, char select),
// so the publisher skips that frame. The build-specific engine read lives in
// ClientEngineOffsets.h.
//------------------------------------------------------------------------------
#include "ClientEngineOffsets.h"

static bool ReadEngineShipState(float pos[3], float heading[3], unsigned int* sector) {
    return FreyaReadEngineShipState_Local(pos, heading, sector);
}

//------------------------------------------------------------------------------
// Sending internals.
//------------------------------------------------------------------------------
static volatile bool g_Run = false;
static HANDLE g_Thread = NULL;
static SOCKET g_Sock = INVALID_SOCKET;
static sockaddr_in g_ProxyAddr;
static unsigned int g_Seq = 0;

// Send one sample as a single loopback datagram. UDP is connectionless and
// lossy-tolerant -- exactly right for a latest-wins position feed: a dropped
// frame just means the proxy keeps the previous one for another ~100ms.
static void SendSample(const float pos[3], const float heading[3], unsigned int sector) {
    if (g_Sock == INVALID_SOCKET)
        return;

    FreyaClientPosDatagram dg;
    dg.magic = FREYA_CLIENT_POS_MAGIC;
    dg.seq = ++g_Seq;
    memcpy(dg.position, pos, sizeof(float) * 3);
    memcpy(dg.heading, heading, sizeof(float) * 3);
    dg.sector_id = sector;
    dg.valid = 1;

    sendto(g_Sock, (const char*)&dg, (int)sizeof(dg), 0, (const sockaddr*)&g_ProxyAddr,
           (int)sizeof(g_ProxyAddr));
}

static unsigned __stdcall FeedThread(void*) {
    // Poll a touch faster than the proxy (which drains at ~200ms) so a fresh
    // value is always waiting. Change-gating is the proxy's job -- we just keep
    // the latest sample flowing.
    const DWORD POLL_MS = 100;
    while (g_Run) {
        float pos[3] = {0, 0, 0};
        float heading[3] = {0, 0, 0};
        unsigned int sector = 0;
        if (ReadEngineShipState(pos, heading, &sector))
            SendSample(pos, heading, sector);
        Sleep(POLL_MS);
    }
    return 0;
}

void FreyaClientPosFeed_Start() {
    if (g_Run)
        return;

    // WSAStartup is refcounted; the client already initialised Winsock, but a
    // DLL must not assume that, and a paired Start/Stop keeps the count balanced.
    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0)
        return;

    g_Sock = ::socket(AF_INET, SOCK_DGRAM, 0);
    if (g_Sock == INVALID_SOCKET) {
        WSACleanup();
        return;
    }

    memset(&g_ProxyAddr, 0, sizeof(g_ProxyAddr));
    g_ProxyAddr.sin_family = AF_INET;
    g_ProxyAddr.sin_port = htons(FREYA_CLIENT_POS_PORT);
    g_ProxyAddr.sin_addr.s_addr = htonl(INADDR_LOOPBACK); // 127.0.0.1

    g_Run = true;
    g_Thread = (HANDLE)_beginthreadex(NULL, 0, FeedThread, NULL, 0, NULL);
    if (!g_Thread) {
        g_Run = false;
        closesocket(g_Sock);
        g_Sock = INVALID_SOCKET;
        WSACleanup();
    }
}

void FreyaClientPosFeed_Stop() {
    g_Run = false;
    if (g_Thread) {
        WaitForSingleObject(g_Thread, 1000);
        CloseHandle(g_Thread);
        g_Thread = NULL;
    }
    if (g_Sock != INVALID_SOCKET) {
        closesocket(g_Sock);
        g_Sock = INVALID_SOCKET;
        WSACleanup();
    }
}
