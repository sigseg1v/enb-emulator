//==============================================================================
// ClientPositionFeed.cpp
//
// In-client producer for the MVAS position feed (PB-2). See ClientPositionFeed.h
// for the design. This file owns the shared-memory publishing and the polling
// thread; the actual engine read is isolated in ReadEngineShipState() so that
// no build-specific client internals live in version control.
//==============================================================================
#include <windows.h>
#include <process.h>
#include <cstring>

// The proxy<->hook shared-memory contract. Kept in the proxy tree because the
// proxy owns the consumer; included here by relative path so both ends compile
// against ONE definition. (Build systems add proxy/ to the include path; the
// relative include keeps this self-evident.)
#include "../../proxy/ClientPositionShared.h"

//------------------------------------------------------------------------------
// ReadEngineShipState -- OWNER SEAM.
//
// Fill this in for the client build in use: read the ship's current world
// position (x,y,z) and orientation (x,y,z) from the running engine and write
// them into the out params, returning true once a live in-space sample exists.
// Return false whenever there is no valid position yet (loading, docked, char
// select), so the publisher skips that frame.
//
// It is left returning false on purpose: until it is filled the feed publishes
// nothing, the proxy reads nothing, and server behaviour is unchanged. Keep any
// build-specific addresses/offsets out of version control -- supply them here in
// the local build only. Confirm against the real client before relying on it
// (plans/29 CV-MVAS-POS).
//------------------------------------------------------------------------------
static bool ReadEngineShipState(float pos[3], float heading[3], unsigned int *sector)
{
    (void) pos;
    (void) heading;
    (void) sector;
    return false; // OWNER SEAM: not wired -> feed inert (see header)
}

//------------------------------------------------------------------------------
// Publishing internals.
//------------------------------------------------------------------------------
static HANDLE                  g_MapHandle  = NULL;
static Net7ClientShipPosition *g_Shm        = NULL;
static volatile bool           g_Run        = false;
static HANDLE                  g_Thread     = NULL;

// Publish one sample under the seqlock: bump seq odd, write, bump seq even. The
// proxy-side reader (Net7ClientPos_Read) retries while seq is odd, so it never
// observes a half-written sample.
static void Publish(const float pos[3], const float heading[3], unsigned int sector)
{
    if (!g_Shm) return;
    g_Shm->seq++;                       // -> odd: write in progress
    _ReadWriteBarrier();
    memcpy((void *) g_Shm->position, pos, sizeof(float) * 3);
    memcpy((void *) g_Shm->heading, heading, sizeof(float) * 3);
    g_Shm->sector_id = sector;
    g_Shm->valid     = 1;
    _ReadWriteBarrier();
    g_Shm->seq++;                       // -> even: sample complete
}

static unsigned __stdcall FeedThread(void *)
{
    // Poll a touch faster than the proxy (which samples at ~200ms) so a fresh
    // value is always waiting. Change-gating is the proxy's job -- we just keep
    // the published sample current.
    const DWORD POLL_MS = 100;
    while (g_Run)
    {
        float pos[3] = {0, 0, 0};
        float heading[3] = {0, 0, 0};
        unsigned int sector = 0;
        if (ReadEngineShipState(pos, heading, &sector))
            Publish(pos, heading, sector);
        Sleep(POLL_MS);
    }
    return 0;
}

void Net7ClientPosFeed_Start()
{
    if (g_Run) return;

    g_MapHandle = CreateFileMappingA(
        INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE, 0,
        (DWORD) sizeof(Net7ClientShipPosition), NET7_CLIENT_POS_SHM_NAME);
    if (!g_MapHandle) return;

    g_Shm = (Net7ClientShipPosition *) MapViewOfFile(
        g_MapHandle, FILE_MAP_WRITE, 0, 0, sizeof(Net7ClientShipPosition));
    if (!g_Shm)
    {
        CloseHandle(g_MapHandle);
        g_MapHandle = NULL;
        return;
    }

    memset(g_Shm, 0, sizeof(*g_Shm));
    g_Shm->magic = NET7_CLIENT_POS_SHM_MAGIC;

    g_Run = true;
    g_Thread = (HANDLE) _beginthreadex(NULL, 0, FeedThread, NULL, 0, NULL);
    if (!g_Thread) g_Run = false;       // mapping stays; reader just sees !valid
}

void Net7ClientPosFeed_Stop()
{
    g_Run = false;
    if (g_Thread)
    {
        WaitForSingleObject(g_Thread, 1000);
        CloseHandle(g_Thread);
        g_Thread = NULL;
    }
    if (g_Shm)      { UnmapViewOfFile(g_Shm); g_Shm = NULL; }
    if (g_MapHandle){ CloseHandle(g_MapHandle); g_MapHandle = NULL; }
}
