//==============================================================================
// PosFeedDllMain.cpp
//
// DllMain for the standalone MVAS position-feed DLL (PB-2). This is the ENTIRE
// DLL besides ClientPositionFeed.cpp -- it has NO dependency on Microsoft
// Detours, installs NO API hooks, and contains NO hardcoded client addresses.
// It exists only to start the in-process position publisher
// (Net7ClientPosFeed_Start) when the DLL is loaded into client.exe, and stop it
// on unload.
//
// Why this and not ClientDetours.dll: the legacy ClientDetours.dll links MSVC's
// prebuilt detours.lib (which mingw cannot link) and installs winsock/file hooks
// at hardcoded client.exe offsets (0x00A27B12, ...) specific to ONE client build
// -- loading it into a different build would corrupt it. The position feed needs
// none of that. So the feed ships as this minimal, build-agnostic DLL instead.
//
// Injection: under WINE the launcher registers this DLL in AppInit_DLLs for the
// dedicated prefix (see tools/LaunchFreya). AppInit loads a DLL into EVERY
// process in the prefix that links user32 -- not just client.exe -- so DllMain
// guards on the host module name and only starts the feed inside client.exe.
// Inside any other process (e.g. a WINE-side FreyaProxy) it loads and does
// nothing.
//==============================================================================
#include <windows.h>
#include <cstring>
#include "ClientPositionFeed.h"

// True only when the current process image is client.exe. AppInit_DLLs is
// prefix-global, so without this the feed thread would also spin up inside any
// other user32 process in the prefix.
static bool HostIsClientExe()
{
    char path[MAX_PATH];
    DWORD n = GetModuleFileNameA(NULL, path, (DWORD) sizeof(path));
    if (n == 0 || n >= sizeof(path)) return false;

    // Compare the trailing filename, case-insensitively (WINE paths are
    // case-preserving but the client may be invoked as Client.exe / CLIENT.EXE).
    const char *base = path;
    for (const char *p = path; *p; ++p)
        if (*p == '\\' || *p == '/') base = p + 1;

    return _stricmp(base, "client.exe") == 0;
}

BOOL APIENTRY DllMain(HANDLE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    (void) lpReserved;
    switch (ul_reason_for_call)
    {
        case DLL_PROCESS_ATTACH:
            // We never need thread-attach/detach notifications; suppressing them
            // avoids per-thread DllMain overhead in the client's many threads.
            DisableThreadLibraryCalls((HMODULE) hModule);
            if (HostIsClientExe())
            {
                // Loader-lock-safe confirmation that injection actually landed in
                // client.exe -- visible in the WINE console / a debugger even
                // before the engine read is filled (when the feed sends nothing).
                OutputDebugStringA("[Net7PosFeed] attached to client.exe; starting MVAS position feed\n");
                Net7ClientPosFeed_Start();   // inert until the owner seam is filled
            }
            break;

        case DLL_PROCESS_DETACH:
            // Safe to call unconditionally: Stop() is a no-op if Start() never ran.
            Net7ClientPosFeed_Stop();
            break;
    }
    return TRUE;
}
