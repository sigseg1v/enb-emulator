//==============================================================================
// ClientEngineOffsets.h
//
// PB-2 MVAS position-feed engine read for the client build in use. Carries the
// build-specific addresses the feed patches and reads through.
//
// HOW IT WORKS. The engine's live ship transform lives behind a DYNAMIC heap
// pointer; there is no static address that always points at it. So, like the
// historical feed, we patch client code sites once so the running client stashes
// the live transform pointer (and the player GameID) into a fixed .data scratch
// slot, then we read position/orientation through that slot every frame. Running
// IN-PROCESS, the "write to client memory" is a direct VirtualProtect + memcpy
// and the "read" is a direct dereference.
//
// !! DIAGNOSING A REGRESSION (chat/UI broke after enabling the feed) !!
// These patches overwrite LIVE client code at fixed addresses derived from one
// specific build. Only the JMP site is fingerprint-checked; the others are
// written blind. If your client.exe differs from that build at any patched site,
// we corrupt a code path -> broken chat/UI is the classic symptom.
//
// To isolate it, flip the switches below and rebuild (`just build-posfeed-dll`):
//   1. FREYA_FEED_MASTER_ENABLE 0  -> DLL still injects but patches NOTHING and
//      the read is inert. If chat WORKS here, the breakage is the patches (go to
//      step 2). If chat is STILL broken, the patches are innocent -- the cause is
//      the injection/feed thread itself, not this header.
//   2. Master back to 1, then turn the per-site switches off one at a time (start
//      with GAMEID -- it replaces a whole function and is the prime suspect) until
//      chat returns. The site you turned off to fix it is wrong for your build.
// Run with `WINEDEBUG=+debugstr,+loaddll just play-local` to see the [PosFeed]
// diagnostic lines (fingerprint match, which sites patched, first GameID/sample).
//
// ClientPositionFeed.cpp includes this directly. windows.h and <cstring> are
// already included by that TU before this point.
//==============================================================================
#ifndef _FREYA_CLIENT_ENGINE_OFFSETS_H_
#define _FREYA_CLIENT_ENGINE_OFFSETS_H_

// ---- bisection switches -----------------------------------------------------
#define FREYA_FEED_MASTER_ENABLE 1   // 0 = inject DLL but patch nothing / read inert
#define FREYA_FEED_PATCH_GPS_ON   1  // capture the transform ptr (own-page trampoline + JMP hijack)
#define FREYA_FEED_SEND_ON 1         // 1 = full pipeline: read transform + send to proxy (-> 0x1004 -> MVAS).
                                    //     0 = capture only, never send (bisection mode).
#define FREYA_FEED_GPS_PASSTHROUGH 0 // 1 = trampoline does ONLY the 2 displaced instrs + jmp back (NO
                                    //     capture). Semantically identical to the unpatched client, so
                                    //     it bisects "is the JMP/trampoline MECHANISM the freeze" (still
                                    //     freezes -> mechanism) vs "is the CAPTURE logic the freeze"
                                    //     (works -> capture). Feed stays inert either way (slot never set).
#define FREYA_FEED_PATCH_GAMEID_ON 0 // site 5: capture GameID (needed for the send gate)
#define FREYA_FEED_PATCH_REDIRECT_ON 0 // site 4: reset ptr on sector change (multi-sector)
#define FREYA_FEED_PATCH_LAG_ON   0  // site 3: cosmetic movement smoothing (optional)
// -----------------------------------------------------------------------------

namespace {

inline void FreyaDbg(const char *s) { OutputDebugStringA(s); }

// Scratch slot the patched client code writes into. [0] = live transform
// pointer, [1] = player GameID. The original recipe used a fixed client .data
// address (0x00b6e5a8) it believed was unused padding -- but that is BUILD-
// SPECIFIC and in this build holds live data (a __FILE__ string), so writing
// there corrupts the client. Running in-process we allocate our OWN slot and
// patch its absolute address into the injected code; the client's memory is
// never used as scratch. Set at patch time.
unsigned long *g_freya_slot = 0;

// The patch sites (no-ASLR fixed base 0x00400000, so these are absolute).
const unsigned long FREYA_PATCH_JMP      = 0x0098f0e5; // object-transform loop; hijacked to our trampoline
const unsigned long FREYA_PATCH_LAG      = 0x007379e2; // perceived send/recv lag -> 0 (smooth)
const unsigned long FREYA_PATCH_REDIRECT = 0x00767009; // reset captured ptr on sector change
const unsigned long FREYA_PATCH_GAMEID   = 0x00733620; // stash GameID into g_freya_slot[1]

// Build fingerprint: the original 7 bytes at FREYA_PATCH_JMP for this build (the
// instructions the JMP hijack relocates). If these do not match, this is not the
// build these addresses were derived from -- refuse to patch.
const unsigned char FREYA_JMP_FINGERPRINT[7] = { 0x8b, 0x48, 0x14, 0x8d, 0x44, 0x19, 0x48 };

// In-process code write: drop page protection, copy, restore, flush I-cache.
inline bool FreyaWriteCode(unsigned long addr, const void *src, size_t n)
{
    DWORD old = 0;
    void *p = (void *) addr;
    if (!VirtualProtect(p, n, PAGE_EXECUTE_READWRITE, &old)) return false;
    memcpy(p, src, n);
    DWORD tmp = 0;
    VirtualProtect(p, n, old, &tmp);
    FlushInstructionCache(GetCurrentProcess(), p, n);
    return true;
}

// Apply the enabled patches once. Returns false (and patches nothing) on a build
// fingerprint mismatch so a wrong client stays inert.
inline bool FreyaPatchClientOnce()
{
#if !FREYA_FEED_MASTER_ENABLE
    FreyaDbg("[PosFeed] master disabled -- no patches applied, read inert\n");
    return false;
#else
    if (memcmp((const void *) FREYA_PATCH_JMP, FREYA_JMP_FINGERPRINT,
               sizeof FREYA_JMP_FINGERPRINT) != 0)
    {
        FreyaDbg("[PosFeed] FINGERPRINT MISMATCH at JMP site -- wrong build, staying inert\n");
        return false; // not this build -> never patch
    }
    FreyaDbg("[PosFeed] fingerprint OK; applying patches\n");

    // Allocate OUR OWN scratch slot (never the client's .data). The injected
    // code writes the captured pointer/GameID here; the read side reads here.
    g_freya_slot = (unsigned long *) VirtualAlloc(
        NULL, 16, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!g_freya_slot) { FreyaDbg("[PosFeed] slot VirtualAlloc FAILED -- staying inert\n"); return false; }
    g_freya_slot[0] = g_freya_slot[1] = g_freya_slot[2] = g_freya_slot[3] = 0;

#if FREYA_FEED_PATCH_GPS_ON
    // Sites 1+2: hijack the object-transform loop at FREYA_PATCH_JMP into a
    // trampoline that captures the player hull's transform pointer, re-runs the
    // two displaced instructions, then returns.
    //
    // The ORIGINAL recipe stashed this trampoline in a "code cave" at a fixed
    // client address (0x00888278) -- but that address is BUILD-SPECIFIC. In this
    // build the cave is only 24 bytes; a live function sits right after it, so the
    // 72-byte recipe buffer clobbered live code and broke the whole client. Running
    // IN-PROCESS we have no need for the client's cave: VirtualAlloc our own
    // executable page and jump to that. No build-specific cave, nothing clobbered.
    {
        const unsigned long RET_ADDR = FREYA_PATCH_JMP + 7; // displaced 7 bytes -> resume here
        unsigned char *tramp = (unsigned char *) VirtualAlloc(
            NULL, 64, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
        if (!tramp) { FreyaDbg("[PosFeed] VirtualAlloc FAILED -- staying inert\n"); return false; }

#if FREYA_FEED_GPS_PASSTHROUGH
        // PASSTHROUGH bisection: trampoline = the two displaced instructions + jmp
        // back, NOTHING else. Behaviourally identical to the unpatched client. If
        // the game STILL freezes with this, the JMP/trampoline mechanism itself is
        // the cause (not our capture logic); if it runs clean, the capture block is.
        unsigned char t[] = {
            0x8b, 0x48, 0x14,           // 0: MOV ECX,[EAX+14]          (relocated original)
            0x8d, 0x44, 0x19, 0x48,     // 3: LEA EAX,[ECX+EBX+48]      (relocated original)
            0xe9, 0,0,0,0               // 7: JMP RET_ADDR              (rel32 @8)
        };
        *((unsigned long *) &t[8]) = RET_ADDR - ((unsigned long) tramp + 12); // jmp-back rel32
        memcpy(tramp, t, sizeof t);
        FlushInstructionCache(GetCurrentProcess(), tramp, sizeof t);
        FreyaDbg("[PosFeed]  +trampoline (own page, PASSTHROUGH -- no capture)\n");
#else
        // NO one-shot latch: write the current player-hull transform ptr EVERY
        // frame the loop visits it, exactly like the real recipe. The earlier
        // "capture once, then TEST ECX,ECX / JNZ skip" latched the FIRST hull
        // pointer seen after login and never refreshed it -- so on any sector
        // change (gating, and notably entering a PLANET sub-sector) the client
        // rebuilds the ship object at a NEW heap address while our slot kept the
        // old, now-freed pointer. The feed then read garbage/zero, went silent,
        // and the server froze the avatar at its last position (could not dock at
        // a planet base because "you are not near it"). The decompiled Net7Proxy
        // re-captures whenever the client's transform ptr changes (FUN_0040ebc0:
        // `if (ptr+0x48 != cached) cached = ptr+0x48;`), i.e. it tracks the live
        // object every poll; mirror that by always overwriting the slot. The
        // player-hull signature gate ('S' at [EAX], 0x48 at [EAX+2]) still scopes
        // the write to the controllable hull and is unchanged.
        unsigned char t[] = {
            0x80, 0x38, 0x53,           // 0:  CMP BYTE[EAX],'S'        (player hull sig)
            0x75, 0x0f,                 // 3:  JNZ +0x0f -> off 20 (skip capture)
            0x80, 0x78, 0x02, 0x48,     // 5:  CMP BYTE[EAX+2],0x48
            0x75, 0x09,                 // 9:  JNZ +0x09 -> off 20
            0x8b, 0x48, 0x14,           // 11: MOV ECX,[EAX+14]         (live transform ptr)
            0x89, 0x0d, 0,0,0,0,        // 14: MOV [scratch],ECX        (scratch @16; ALWAYS write)
            0x8b, 0x48, 0x14,           // 20: MOV ECX,[EAX+14]         (relocated original)
            0x8d, 0x44, 0x19, 0x48,     // 23: LEA EAX,[ECX+EBX+48]     (relocated original)
            0xe9, 0,0,0,0               // 27: JMP RET_ADDR             (rel32 @28)
        };
        *((unsigned long *) &t[16]) = (unsigned long) g_freya_slot;
        *((unsigned long *) &t[28]) = RET_ADDR - ((unsigned long) tramp + 32); // jmp-back rel32
        memcpy(tramp, t, sizeof t);
        FlushInstructionCache(GetCurrentProcess(), tramp, sizeof t);
        FreyaDbg("[PosFeed]  +trampoline (own page)\n");
#endif

        // JMP hijack: overwrite the 7 displaced bytes with JMP rel32 -> tramp + 2 NOP.
        unsigned char b2[7] = { 0xe9, 0,0,0,0, 0x90, 0x90 };
        *((unsigned long *) &b2[1]) = (unsigned long) tramp - (FREYA_PATCH_JMP + 5);
        FreyaWriteCode(FREYA_PATCH_JMP, b2, sizeof b2);
        FreyaDbg("[PosFeed]  +JMP hijack\n");
    }
#endif

#if FREYA_FEED_PATCH_LAG_ON
    // Site 3 (optional smoothness): perceived send/recv time difference -> 0.
    {
        unsigned char b3[] = { 0x8b, 0xd5 };
        FreyaWriteCode(FREYA_PATCH_LAG, b3, sizeof b3);
        FreyaDbg("[PosFeed]  +LAG smoothing\n");
    }
#endif

#if FREYA_FEED_PATCH_REDIRECT_ON
    // Site 4: reset the captured pointer to 0 on each sector change so the next
    // sector recaptures a fresh pointer. Writes 0 to g_freya_slot[0] (patched @5).
    // NOTE: FREYA_PATCH_REDIRECT is itself a build-specific address that has NOT
    // been re-verified against this build -- enable with care.
    {
        unsigned char b4[] = {
            0x55,                               // PUSH EBP
            0x33, 0xed,                         // XOR EBP,EBP
            0x89, 0x2d, 0,0,0,0,                // MOV [g_freya_slot+0],EBP   (addr @5)
            0x5d,                               // POP EBP
            0xc2, 0x04, 0x00,                   // RETN 4
            0x90, 0x90, 0x90, 0x90
        };
        *((unsigned long *) &b4[5]) = (unsigned long) g_freya_slot;
        FreyaWriteCode(FREYA_PATCH_REDIRECT, b4, sizeof b4);
        FreyaDbg("[PosFeed]  +REDIRECT reset\n");
    }
#endif

#if FREYA_FEED_PATCH_GAMEID_ON
    // Site 5: stash the player GameID into g_freya_slot[1] (addr @6).
    // NOTE: FREYA_PATCH_GAMEID is itself a build-specific address that has NOT
    // been re-verified against this build -- enable with care.
    {
        unsigned char b5[] = {
            0x56,                               // PUSH ESI
            0x8b, 0x77, 0x0c,                   // MOV ESI,[EDI+0C]         (GameID)
            0x89, 0x35, 0,0,0,0,                // MOV [g_freya_slot+4],ESI  (addr @6)
            0x5e,                               // POP ESI
            0xc2, 0x04, 0x00                    // RETN 4
        };
        *((unsigned long *) &b5[6]) = (unsigned long) g_freya_slot + 4;
        FreyaWriteCode(FREYA_PATCH_GAMEID, b5, sizeof b5);
        FreyaDbg("[PosFeed]  +GAMEID capture\n");
    }
#endif

    return true;
#endif // FREYA_FEED_MASTER_ENABLE
}

} // namespace

// The OWNER SEAM ClientPositionFeed.cpp calls. Patches the client on first use,
// then reads the live ship transform through the scratch slot every frame.
//
// Transform layout: a row-major 3x4 affine matrix at (capturedPtr + 0x48), three
// 16-byte rows of [3 orientation floats][1 translation float]. World position is
// the translation column (matrix bytes 12 / 28 / 44); heading is the first
// orientation column (bytes 0 / 16 / 32).
static bool FreyaReadEngineShipState_Local(float pos[3], float heading[3],
                                          unsigned int *sector)
{
    static int  s_patchState = 0;             // 0=untried, 1=patched, -1=inert
    if (s_patchState == 0)
        s_patchState = FreyaPatchClientOnce() ? 1 : -1;
    if (s_patchState != 1 || !g_freya_slot)
        return false;                         // wrong build / disabled -> inert

    // Read [livePtr, gameID] from our own scratch slot the trampoline populates.
    const unsigned long livePtr = g_freya_slot[0];
    if (livePtr == 0) return false;                       // not captured yet (loading/docked)

    static bool s_loggedCapture = false;
    if (!s_loggedCapture)
    {
        s_loggedCapture = true;
        char m[96];
        wsprintfA(m, "[PosFeed] slot captured transform ptr=0x%lx\n", livePtr);
        FreyaDbg(m);
    }
#if FREYA_FEED_PATCH_GAMEID_ON
    // GameID sanity gate -- only meaningful when site 5 is populating the slot.
    // With GAMEID off, the player-hull signature check in the trampoline is what
    // identifies the ship, so the feed runs on the pointer + (0,0,0) checks alone.
    const unsigned long gameID = g_freya_slot[1];
    if (gameID < 0x40000000UL || gameID > 0x40f00000UL)
        return false;
#endif

    const void *xform = (const void *) (livePtr + 0x48);
    if (IsBadReadPtr(xform, 48)) return false;            // stale during sector change

    unsigned char buf[48];
    memcpy(buf, xform, sizeof buf);

    const float px = *((const float *) &buf[12]);
    const float py = *((const float *) &buf[28]);
    const float pz = *((const float *) &buf[44]);
    if (px == 0.0f && py == 0.0f && pz == 0.0f) return false; // pre-first-frame zero

    // Diagnostic: log the parsed coords once so we can tell a real in-space
    // position (thousands..millions of game units) from garbage (NaN / absurd /
    // tiny). wsprintfA has no %f, so log int-truncated plus raw IEEE hex.
    static int s_logCoords = 0;
    if ((s_logCoords++ % 10) == 0)   // ~once/sec at 100ms poll
    {
        char m[160];
        wsprintfA(m, "[PosFeed] coords px=%ld py=%ld pz=%ld  raw=%08lx %08lx %08lx\n",
                  (long) px, (long) py, (long) pz,
                  *((const unsigned long *) &buf[12]),
                  *((const unsigned long *) &buf[28]),
                  *((const unsigned long *) &buf[44]));
        FreyaDbg(m);
    }

#if !FREYA_FEED_SEND_ON
    return false;  // BISECTION: coords parsed + logged above, but do NOT send.
#endif

    pos[0] = px; pos[1] = py; pos[2] = pz;
    heading[0] = *((const float *) &buf[0]);
    heading[1] = *((const float *) &buf[16]);
    heading[2] = *((const float *) &buf[32]);

    // The proxy attributes the position to its own session and ignores this
    // field; the historical feed never read a sector id. Leave it 0.
    *sector = 0;

    static bool s_loggedFirst = false;
    if (!s_loggedFirst) { s_loggedFirst = true; FreyaDbg("[PosFeed] first live sample sent\n"); }
    return true;
}

#endif // _FREYA_CLIENT_ENGINE_OFFSETS_H_
