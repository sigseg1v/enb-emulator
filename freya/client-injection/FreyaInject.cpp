// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya
//==============================================================================
// FreyaInject.cpp
//
// Tiny 32-bit launch-time DLL injector for client.exe. Injects one OR MORE
// 32-bit DLLs into a freshly-spawned client before its main thread runs:
// the MVAS position feed (FreyaPosFeed.dll) and/or the Lua mod runtime
// (enbmod.dll).
//
// WHY THIS EXISTS: WINE (tested: wine-11.8) does NOT implement the Windows
// AppInit_DLLs loader hook, so the conventional "register the DLL in
// HKLM\...\Windows\AppInit_DLLs" injection silently does nothing under WINE --
// the DLL is never loaded into client.exe. The reliable, WINE-supported path is
// the classic CreateProcess(SUSPENDED) + remote-LoadLibrary technique, which
// this binary performs. The launcher (tools/LaunchFreya) spawns
//
//     wine FreyaInject.exe <client.exe> <dll1> [dll2 ...] -- [client args...]
//
// instead of spawning client.exe directly, when any in-client DLL is enabled.
//
// It is build-toolchain-agnostic: built with the same i686 MinGW used for the
// injected DLLs (see `just build-posfeed-dll`); no Microsoft Detours / MSVC lib
// dependency. client.exe is 32-bit, so this injector is 32-bit too.
//
// Usage:
//   FreyaInject.exe <target-exe> <dll1> [dll2 ...] -- [args to forward...]
//   FreyaInject.exe <target-exe> <dll> [args to forward...]   (legacy: 1 DLL,
//                                                               no '--' delimiter)
// The arguments BEFORE the optional `--` (after the target) are DLL paths to
// inject, in order; everything AFTER `--` is forwarded verbatim as the client's
// own argv. With no `--`, the old single-DLL form is honoured: argv[2] is the
// lone DLL and argv[3..] are the client args.
//
// Exit:   0 success; non-zero on the first failing step (codes below). On any
//         failure after the child is created, the child is terminated so a
//         half-injected client never reaches the user.
//==============================================================================
#include <windows.h>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

static void Quote(char* dst, size_t cap, const char* s) {
    // Wrap in quotes so a path containing spaces (the default EnB install path
    // is ".../EA GAMES/Earth & Beyond/release") survives CreateProcess parsing.
    snprintf(dst, cap, "\"%s\"", s);
}

// Remote-LoadLibrary one DLL into an already-created (suspended) child process.
// Returns 0 on success, or a non-zero step code on failure.
static int InjectOne(HANDLE hProcess, const char* dllPath) {
    SIZE_T n = strlen(dllPath) + 1;
    void* remote = VirtualAllocEx(hProcess, NULL, n, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remote) {
        fprintf(stderr, "FreyaInject: VirtualAllocEx failed (%lu)\n", GetLastError());
        return 4;
    }

    if (!WriteProcessMemory(hProcess, remote, dllPath, n, NULL)) {
        fprintf(stderr, "FreyaInject: WriteProcessMemory failed (%lu)\n", GetLastError());
        VirtualFreeEx(hProcess, remote, 0, MEM_RELEASE);
        return 5;
    }

    // LoadLibraryA's address is identical across processes of the same bitness
    // in WINE/Windows (kernel32 is mapped at a shared base), so the local
    // GetProcAddress value is valid as the remote thread entry.
    HMODULE k32 = GetModuleHandleA("kernel32.dll");
    // The void* hop is the accepted idiom for the FARPROC -> typed-fn cast and
    // keeps -Wcast-function-type quiet; LoadLibraryA's ABI matches the thread-proc
    // shape (one pointer arg, stdcall) so the call is valid.
    LPTHREAD_START_ROUTINE loadLib =
        (LPTHREAD_START_ROUTINE)(void*)GetProcAddress(k32, "LoadLibraryA");
    if (!loadLib) {
        fprintf(stderr, "FreyaInject: no LoadLibraryA\n");
        VirtualFreeEx(hProcess, remote, 0, MEM_RELEASE);
        return 6;
    }

    HANDLE th = CreateRemoteThread(hProcess, NULL, 0, loadLib, remote, 0, NULL);
    if (!th) {
        fprintf(stderr, "FreyaInject: CreateRemoteThread failed (%lu)\n", GetLastError());
        VirtualFreeEx(hProcess, remote, 0, MEM_RELEASE);
        return 6;
    }

    WaitForSingleObject(th, 10000);
    DWORD mod = 0;
    GetExitCodeThread(th, &mod); // low 32 bits of the loaded HMODULE; 0 == LoadLibrary failed
    CloseHandle(th);
    VirtualFreeEx(hProcess, remote, 0, MEM_RELEASE);
    if (mod == 0) {
        fprintf(stderr, "FreyaInject: remote LoadLibraryA returned NULL for %s\n", dllPath);
        return 7;
    }

    fprintf(stderr, "FreyaInject: injected %s (HMODULE=0x%lx)\n", dllPath, mod);
    return 0;
}

int main(int argc, char** argv) {
    if (argc < 3) {
        fprintf(
            stderr,
            "FreyaInject: usage: FreyaInject.exe <target-exe> <dll1> [dll2 ...] -- [args...]\n");
        return 2;
    }

    const char* targetExe = argv[1];

    // Split argv[2..] into DLL paths and forwarded client args at the first `--`.
    // No `--` => legacy single-DLL form: argv[2] is the DLL, argv[3..] are args.
    std::vector<const char*> dlls;
    std::vector<const char*> clientArgs;
    int delim = -1;
    for (int i = 2; i < argc; ++i)
        if (strcmp(argv[i], "--") == 0) {
            delim = i;
            break;
        }

    if (delim < 0) {
        dlls.push_back(argv[2]);
        for (int i = 3; i < argc; ++i)
            clientArgs.push_back(argv[i]);
    } else {
        for (int i = 2; i < delim; ++i)
            dlls.push_back(argv[i]);
        for (int i = delim + 1; i < argc; ++i)
            clientArgs.push_back(argv[i]);
    }

    if (dlls.empty()) {
        fprintf(stderr, "FreyaInject: no DLLs to inject\n");
        return 2;
    }

    // Reconstruct the child command line: "exe" arg arg ...
    char cmd[8192];
    Quote(cmd, sizeof(cmd), targetExe);
    for (const char* a : clientArgs) {
        strncat(cmd, " ", sizeof(cmd) - strlen(cmd) - 1);
        strncat(cmd, a, sizeof(cmd) - strlen(cmd) - 1);
    }

    STARTUPINFOA si;
    PROCESS_INFORMATION pi;
    memset(&si, 0, sizeof(si));
    memset(&pi, 0, sizeof(pi));
    si.cb = sizeof(si);

    // Suspended: the child is created but its main thread does not run until we
    // ResumeThread, giving us a window to inject before any client code (and a
    // DLL's host guard / startup hook) executes.
    if (!CreateProcessA(NULL, cmd, NULL, NULL, FALSE, CREATE_SUSPENDED, NULL, NULL, &si, &pi)) {
        fprintf(stderr, "FreyaInject: CreateProcess failed (%lu): %s\n", GetLastError(), cmd);
        return 3;
    }

    int rc = 0;
    for (const char* dll : dlls) {
        rc = InjectOne(pi.hProcess, dll);
        if (rc != 0)
            break;
    }

    if (rc != 0) {
        TerminateProcess(pi.hProcess, 1);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return rc;
    }

    fprintf(stderr, "FreyaInject: %zu DLL(s) injected; resuming %s\n", dlls.size(), targetExe);

    // Let the (now-instrumented) client run.
    ResumeThread(pi.hThread);
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return 0;
}
