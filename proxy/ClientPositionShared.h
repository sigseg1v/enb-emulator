// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator
//
// ClientPositionShared.h
// ----------------------
// Shared-memory IPC contract between TWO of our own processes on the Win32
// (WINE) client host:
//
//   * PRODUCER -- the in-client position hook (client/detours). It runs inside
//     client.exe, reads the rendered ship position/orientation from the game
//     engine, and publishes it here every frame.
//   * CONSUMER -- the proxy's MVAS position feed (proxy/UDPClient_linux.cpp,
//     UDPClient::ReadClientShipPosition). It reads the latest published value
//     and streams it to the server as opcode 0x1004 on MVAS_LOGIN_PORT.
//
// Why this exists: the real Net7Proxy sourced ship position by scraping the
// client process's memory directly. We instead read it IN-PROCESS (the client
// hook owns the value) and hand it across a named shared-memory mapping, which
// avoids cross-process memory scraping at a hardcoded, build-specific virtual
// address. The wire result on MVAS_LOGIN_PORT is identical either way -- proven
// by the live Net7Proxy capture (proxy/local-debug Combat capture: 0x1004 x196
// to udp/3806 during thruster movement).
//
// This header carries NO client memory layout -- only the proxy<->hook
// exchange format. The producer keeps engine offsets to itself.
//
// IMPORTANT: this feed is Win32/WINE-only. The Linux-native (docker) proxy has
// no client process behind it, so the consumer compiles to a no-op there and
// never opens the mapping.
#ifndef _NET7_CLIENT_POSITION_SHARED_H_
#define _NET7_CLIENT_POSITION_SHARED_H_

#include <cstdint>

// Named mapping. The producer creates it (CreateFileMapping); the consumer
// opens it read-only (OpenFileMapping). Session-local Win32 namespace -- the
// proxy and the client run in the same WINE session / Windows logon.
#define NET7_CLIENT_POS_SHM_NAME "Net7ClientShipPosition"

// Bumped if the struct layout below ever changes; consumer ignores a mapping
// whose magic does not match so a stale producer can never feed garbage.
#define NET7_CLIENT_POS_SHM_MAGIC 0x4E37504Fu  // 'N7PO'

// Published ship state. Read with the seqlock protocol below so the consumer
// never observes a half-written sample (the producer writes ~every frame).
struct Net7ClientShipPosition
{
    uint32_t magic;        // == NET7_CLIENT_POS_SHM_MAGIC once initialised
    volatile uint32_t seq; // seqlock: odd while a write is in progress
    float    position[3];  // engine ship position  x, y, z
    float    heading[3];   // engine ship orientation x, y, z
    uint32_t sector_id;    // current sector id (0 = unknown / not in space)
    uint32_t valid;        // 1 once the producer has a live, in-space sample
};

// Seqlock reader: returns true and fills *out with a tear-free snapshot, or
// false if a consistent read could not be taken within a few tries (producer
// mid-write storm -- the caller just skips this tick). Header-only so both the
// proxy and the client hook share one implementation.
static inline bool Net7ClientPos_Read(const Net7ClientShipPosition *shm,
                                      Net7ClientShipPosition *out)
{
    if (!shm || shm->magic != NET7_CLIENT_POS_SHM_MAGIC) return false;
    for (int tries = 0; tries < 4; ++tries)
    {
        uint32_t s1 = shm->seq;
        if (s1 & 1u) continue;            // write in progress
        *out = *shm;
        uint32_t s2 = shm->seq;
        if (s1 == s2) return out->valid != 0;
    }
    return false;
}

#endif // _NET7_CLIENT_POSITION_SHARED_H_
