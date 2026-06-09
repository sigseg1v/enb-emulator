//==============================================================================
// Net7Inject.cpp
//
// Tiny 32-bit launch-time DLL injector for the PB-2 MVAS position feed.
//
// WHY THIS EXISTS: WINE (tested: wine-11.8) does NOT implement the Windows
// AppInit_DLLs loader hook, so the conventional "register the DLL in
// HKLM\...\Windows\AppInit_DLLs" injection silently does nothing under WINE --
// the DLL is never loaded into client.exe. The reliable, WINE-supported path is
// the classic CreateProcess(SUSPENDED) + remote-LoadLibrary technique, which
// this binary performs. The launcher (tools/LaunchFreya) spawns
//
//     wine Net7Inject.exe <client.exe> <Net7PosFeed.dll> [client args...]
//
// instead of spawning client.exe directly, when the position feed is enabled.
//
// It is build-toolchain-agnostic: built with the same i686 MinGW used for
// Net7PosFeed.dll (see `just build-posfeed-dll`); no Microsoft Detours / MSVC
// lib dependency. client.exe is 32-bit, so this injector is 32-bit too.
//
// Usage:  Net7Inject.exe <target-exe> <dll-to-inject> [args to forward...]
// Exit:   0 success; non-zero on the first failing step (codes below). On any
//         failure after the child is created, the child is terminated so a
//         half-injected client never reaches the user.
//==============================================================================
#include <windows.h>
#include <cstdio>
#include <cstring>

static void Quote(char *dst, size_t cap, const char *s)
{
    // Wrap in quotes so a path containing spaces (the default EnB install path
    // is ".../EA GAMES/Earth & Beyond/release") survives CreateProcess parsing.
    snprintf(dst, cap, "\"%s\"", s);
}

int main(int argc, char **argv)
{
    if (argc < 3)
    {
        fprintf(stderr, "Net7Inject: usage: Net7Inject.exe <target-exe> <dll> [args...]\n");
        return 2;
    }

    const char *targetExe = argv[1];
    const char *dllPath    = argv[2];

    // Reconstruct the child command line: "exe" arg3 arg4 ...
    char cmd[8192];
    Quote(cmd, sizeof(cmd), targetExe);
    for (int i = 3; i < argc; ++i)
    {
        strncat(cmd, " ", sizeof(cmd) - strlen(cmd) - 1);
        strncat(cmd, argv[i], sizeof(cmd) - strlen(cmd) - 1);
    }

    STARTUPINFOA si;
    PROCESS_INFORMATION pi;
    memset(&si, 0, sizeof(si));
    memset(&pi, 0, sizeof(pi));
    si.cb = sizeof(si);

    // Suspended: the child is created but its main thread does not run until we
    // ResumeThread, giving us a window to inject before any client code (and the
    // feed's HostIsClientExe guard / Net7ClientPosFeed_Start) executes.
    if (!CreateProcessA(NULL, cmd, NULL, NULL, FALSE,
                        CREATE_SUSPENDED, NULL, NULL, &si, &pi))
    {
        fprintf(stderr, "Net7Inject: CreateProcess failed (%lu): %s\n",
                GetLastError(), cmd);
        return 3;
    }

    int rc = 0;
    do
    {
        // Write the DLL path into the child, then run LoadLibraryA there.
        // LoadLibraryA's address is identical across processes of the same
        // bitness in WINE/Windows (kernel32 is mapped at a shared base), so the
        // local GetProcAddress value is valid as the remote thread entry.
        SIZE_T n = strlen(dllPath) + 1;
        void *remote = VirtualAllocEx(pi.hProcess, NULL, n,
                                      MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (!remote) { fprintf(stderr, "Net7Inject: VirtualAllocEx failed (%lu)\n", GetLastError()); rc = 4; break; }

        if (!WriteProcessMemory(pi.hProcess, remote, dllPath, n, NULL))
        { fprintf(stderr, "Net7Inject: WriteProcessMemory failed (%lu)\n", GetLastError()); rc = 5; break; }

        HMODULE k32 = GetModuleHandleA("kernel32.dll");
        LPTHREAD_START_ROUTINE loadLib =
            (LPTHREAD_START_ROUTINE) GetProcAddress(k32, "LoadLibraryA");
        if (!loadLib) { fprintf(stderr, "Net7Inject: no LoadLibraryA\n"); rc = 6; break; }

        HANDLE th = CreateRemoteThread(pi.hProcess, NULL, 0, loadLib, remote, 0, NULL);
        if (!th) { fprintf(stderr, "Net7Inject: CreateRemoteThread failed (%lu)\n", GetLastError()); rc = 6; break; }

        WaitForSingleObject(th, 10000);
        DWORD mod = 0;
        GetExitCodeThread(th, &mod);   // low 32 bits of the loaded HMODULE; 0 == LoadLibrary failed
        CloseHandle(th);
        if (mod == 0)
        { fprintf(stderr, "Net7Inject: remote LoadLibraryA returned NULL for %s\n", dllPath); rc = 7; break; }

        fprintf(stderr, "Net7Inject: injected %s (HMODULE=0x%lx); resuming %s\n",
                dllPath, mod, targetExe);
    } while (0);

    if (rc != 0)
    {
        TerminateProcess(pi.hProcess, 1);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return rc;
    }

    // Let the (now-instrumented) client run.
    ResumeThread(pi.hThread);
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return 0;
}
