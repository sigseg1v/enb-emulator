//==============================================================================
// ClientPositionFeed.h
//
// In-client (Win32/WINE) producer half of the MVAS position feed (PB-2).
//
// Runs inside client.exe. Reads the rendered ship position/orientation from the
// game engine and sends it to the proxy as a loopback UDP datagram
// (proxy/ClientPositionShared.h -> UDPClient::ReadClientShipPosition), which
// streams it to the server as opcode 0x1004. This replaces the real Net7Proxy's
// cross-process memory scrape with an in-process read handed over loopback --
// same wire result, no scraping at a hardcoded external address, and one
// transport that reaches the proxy in all three run modes (including play-local,
// where the proxy is a Linux docker process no Win32 mapping could reach).
//
// The engine read itself is intentionally NOT baked in here: it is build- and
// version-specific and lives behind ReadEngineShipState(), which the project
// owner fills for the client build in use (see ClientPositionFeed.cpp). With it
// unfilled the producer publishes nothing and the feed stays inert -- no
// behaviour change until the owner wires the read AND confirms it against the
// real client (plans/29 CV-MVAS-POS).
//==============================================================================
#ifndef _NET7_CLIENT_POSITION_FEED_H_
#define _NET7_CLIENT_POSITION_FEED_H_

// Start the publisher thread (idempotent). Opens the loopback UDP socket and
// spins a thread that polls ReadEngineShipState() and sends every sample. Safe
// to call from the injected DLL's attach path; returns immediately.
void Net7ClientPosFeed_Start();

// Stop the publisher thread and close the socket. Call from the DLL detach.
void Net7ClientPosFeed_Stop();

#endif // _NET7_CLIENT_POSITION_FEED_H_
