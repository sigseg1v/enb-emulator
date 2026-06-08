//==============================================================================
// ClientPositionFeed.h
//
// In-client (Win32/WINE) producer half of the MVAS position feed (PB-2).
//
// Runs inside client.exe. Reads the rendered ship position/orientation from the
// game engine and publishes it into the named shared memory the proxy consumes
// (proxy/ClientPositionShared.h -> UDPClient::ReadClientShipPosition), which
// streams it to the server as opcode 0x1004. This replaces the real Net7Proxy's
// cross-process memory scrape with an in-process read handed across shared
// memory -- same wire result, no scraping at a hardcoded external address.
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

// Start the publisher thread (idempotent). Creates the shared mapping and spins
// a thread that polls ReadEngineShipState() and publishes every change. Safe to
// call from the injected DLL's attach path; returns immediately.
void Net7ClientPosFeed_Start();

// Stop the publisher thread and release the mapping. Call from the DLL detach.
void Net7ClientPosFeed_Stop();

#endif // _NET7_CLIENT_POSITION_FEED_H_
