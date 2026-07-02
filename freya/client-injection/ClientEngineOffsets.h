//==============================================================================
// ClientEngineOffsets.h
//
// PB-2 / BA-2c MVAS position-feed engine read.
//
// The feed needs the LOCAL ship's world position + orientation each frame. The
// live ship transform lives behind a DYNAMIC heap pointer with no static address,
// and this client build has no dedicated local-ship global to read it from. The
// only correct way to resolve "our ship" is the GameID-keyed lookup enbmod
// already does (it captures the targeting/HUD controller and reads the ship it
// resolves under a VEH fault guard).
//
// So the source of truth for the sample is enbmod.dll, NOT this DLL. enbmod
// publishes the local ship's {position, orientation} every tick and exports
// FreyaEnbmodShipState(); we bind that export here and hand the sample to the
// feed thread. This DLL patches NO client code -- the previous approach hijacked
// the engine's per-ship render loop and captured ANY player hull matching a
// shared signature, so with a grouped teammate rendered in range it periodically
// shipped the OTHER ship's transform as ours and the remote ship "teleported"
// between two positions. Sourcing from enbmod's unambiguous per-GameID
// resolution removes both the code-patching risk and that crosstalk. See
// plans/57 BA-2c and plans/29 CV-MVAS-POS.
//
// DEPENDENCY: the feed now REQUIRES enbmod.dll to be injected alongside this DLL.
// The launcher enforces that (ConfigureInjection injects enbmod whenever the
// position feed is enabled). If enbmod is absent, FreyaEnbmodShipState resolves
// to null and the read stays inert (the feed sends nothing) -- never a crash.
//
// ClientPositionFeed.cpp includes this directly. windows.h and <cstring> are
// already included by that TU before this point.
//==============================================================================
#ifndef _FREYA_CLIENT_ENGINE_OFFSETS_H_
#define _FREYA_CLIENT_ENGINE_OFFSETS_H_

namespace {

inline void FreyaDbg(const char* s) {
    OutputDebugStringA(s);
}

// Signature of enbmod's export. Fills pos[3]/heading[3]/*sector from the latest
// valid local-ship sample; returns 1 on a live sample, 0 when there is none yet
// (loading / docked / char-select / enbmod not ready).
typedef int(__cdecl* FreyaEnbmodShipState_t)(float pos[3], float heading[3], unsigned int* sector);

// Resolve enbmod's export, caching the function pointer. enbmod and this DLL are
// injected together but load order is not guaranteed, so re-resolve on each miss
// until enbmod is present.
inline FreyaEnbmodShipState_t FreyaResolveEnbmodExport() {
    static FreyaEnbmodShipState_t s_fn = nullptr;
    if (s_fn)
        return s_fn;
    HMODULE h = GetModuleHandleA("enbmod.dll");
    if (!h)
        return nullptr; // enbmod not loaded (yet, or not injected this launch)
    s_fn = (FreyaEnbmodShipState_t)(void*)GetProcAddress(h, "FreyaEnbmodShipState");
    if (s_fn)
        FreyaDbg("[PosFeed] bound enbmod FreyaEnbmodShipState export\n");
    return s_fn;
}

} // namespace

// The OWNER SEAM ClientPositionFeed.cpp calls. Returns the local ship's world
// position + orientation as published by enbmod, or false when no live sample is
// available yet. No client code is patched and no client memory is read here --
// enbmod owns all game-memory access, under its own fault guard.
static bool FreyaReadEngineShipState_Local(float pos[3], float heading[3], unsigned int* sector) {
    FreyaEnbmodShipState_t fn = FreyaResolveEnbmodExport();
    if (!fn)
        return false; // enbmod not ready -> inert (feed sends nothing)

    unsigned int sec = 0;
    if (!fn(pos, heading, &sec))
        return false; // no live in-space sample
    *sector = sec;

    static bool s_loggedFirst = false;
    if (!s_loggedFirst) {
        s_loggedFirst = true;
        FreyaDbg("[PosFeed] first live sample from enbmod\n");
    }
    return true;
}

#endif // _FREYA_CLIENT_ENGINE_OFFSETS_H_
