// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya
//
// ClientPositionShared.h
// ----------------------
// IPC contract between TWO of our own processes for the MVAS position feed
// (PB-2): the in-client position hook and the proxy. This header is wholly our
// own work (no Net-7 content -- just a port, a magic, and a 40-byte struct), so
// it ships MIT alongside the rest of the injection unit. The proxy (CC BY-NC-SA)
// includes it from here; MIT permits that.
//
//   * PRODUCER -- the in-client position hook (freya/client-injection). It runs inside
//     client.exe, reads the rendered ship position/orientation from the game
//     engine, and sends it to the proxy as a single fixed-size UDP datagram on
//     the loopback intake port below, ~10x/sec.
//   * CONSUMER -- the proxy's MVAS position feed (proxy/UDPClient_linux.cpp,
//     UDPClient::ReadClientShipPosition). It binds the intake port, drains the
//     latest datagram, and streams it to the server as opcode 0x1004 on
//     MVAS_LOGIN_PORT over its already-authenticated DTLS channel.
//
// Why a loopback DATAGRAM and not shared memory: the proxy and the client do
// NOT always share an OS object namespace. In `just play-local` the proxy is a
// Linux binary in docker while the client runs under WINE -- a Win32 named
// shared-memory mapping created by the client is invisible to that Linux
// process. A loopback UDP datagram crosses that boundary (it is exactly how the
// client's own TCP game link reaches the docker proxy), so ONE transport works
// for all three run modes: play-local (WINE client -> docker proxy), play-online
// / NET7MP (WINE client -> WINE proxy), and native Win32. The wire result on
// MVAS_LOGIN_PORT is identical to the real Net7Proxy's -- proven by the live
// capture (proxy/local-debug Combat capture: 0x1004 x196 to udp/3806 during
// thruster movement).
//
// This header carries NO client memory layout -- only the proxy<->hook exchange
// format. The producer keeps engine offsets to itself (ClientEngineOffsets.h).
#ifndef _FREYA_CLIENT_POSITION_SHARED_H_
#define _FREYA_CLIENT_POSITION_SHARED_H_

#include <cstdint>

// Loopback intake port the proxy binds and the hook sends to. UDP. The proxy
// binds 127.0.0.1 on the Win32/WINE build (co-located proxy) and INADDR_ANY on
// the Linux-native docker build, where docker publishes it back to host
// loopback only (127.0.0.1:FREYA_CLIENT_POS_PORT) -- never network-reachable.
#define FREYA_CLIENT_POS_PORT 3807

// Stamped in every datagram; the consumer drops any datagram whose magic does
// not match, so unrelated loopback traffic can never feed garbage positions.
#define FREYA_CLIENT_POS_MAGIC 0x4E37504Fu  // 'N7PO'

// One position sample, sent verbatim as the UDP payload. All fields are 4-byte
// aligned and the total is a multiple of 4, so the struct is naturally packed
// and identical on every x86/x86-64 little-endian target (client and proxy are
// both LE) -- no #pragma pack needed. Datagrams are atomic at the socket layer,
// so there is no torn-read concern and no seqlock: the consumer simply keeps the
// most recent valid datagram it has drained.
struct FreyaClientPosDatagram
{
    uint32_t magic;        // == FREYA_CLIENT_POS_MAGIC
    uint32_t seq;          // producer-monotonic; consumer keeps the latest seq
    float    position[3];  // engine ship position    x, y, z
    float    heading[3];   // engine ship orientation x, y, z
    uint32_t sector_id;    // current sector id (0 = unknown / not in space)
    uint32_t valid;        // 1 = live, in-space sample (else the producer is
                           //     loading/docked/at char-select; consumer skips)
};

#ifdef __cplusplus
static_assert(sizeof(FreyaClientPosDatagram) == 40,
              "FreyaClientPosDatagram must stay a fixed 40-byte wire struct");
#endif

#endif // _FREYA_CLIENT_POSITION_SHARED_H_
