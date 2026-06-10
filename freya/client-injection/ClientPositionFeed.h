// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya
//==============================================================================
// ClientPositionFeed.h
//
// In-client (Win32/WINE) producer half of the MVAS position feed (PB-2).
//
// Runs inside client.exe. Reads the rendered ship position/orientation from the
// game engine and sends it to the proxy as a loopback UDP datagram
// (ClientPositionShared.h -> UDPClient::ReadClientShipPosition), which
// streams it to the server as opcode 0x1004. This replaces the real Net7Proxy's
// cross-process memory scrape with an in-process read handed over loopback --
// same wire result, no scraping at a hardcoded external address, and one
// transport that reaches the proxy in all three run modes (including play-local,
// where the proxy is a Linux docker process no Win32 mapping could reach).
//
// The build-specific engine read lives in ClientEngineOffsets.h, behind
// ReadEngineShipState() (see ClientPositionFeed.cpp). Confirm it against the
// real client when changing it (plans/29 CV-MVAS-POS).
//==============================================================================
#ifndef _FREYA_CLIENT_POSITION_FEED_H_
#define _FREYA_CLIENT_POSITION_FEED_H_

// Start the publisher thread (idempotent). Opens the loopback UDP socket and
// spins a thread that polls ReadEngineShipState() and sends every sample. Safe
// to call from the injected DLL's attach path; returns immediately.
void FreyaClientPosFeed_Start();

// Stop the publisher thread and close the socket. Call from the DLL detach.
void FreyaClientPosFeed_Stop();

#endif // _FREYA_CLIENT_POSITION_FEED_H_
