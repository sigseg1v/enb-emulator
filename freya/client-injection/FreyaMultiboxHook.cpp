// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya
//==============================================================================
// FreyaMultiboxHook.cpp
//
// Lets more than one client.exe run at once (multi-box). The retail client
// refuses to start a second instance with a named-mutex guard, in effect:
//
//     CreateMutexA(NULL, TRUE, "enb_mutex_lock");
//     if (GetLastError() == ERROR_ALREADY_EXISTS) {
//         MessageBoxA(NULL, "Earth & Beyond is already running", "Error", 0);
//         // ... and exits
//     }
//
// The second client sees the first instance's still-live named mutex, the OS
// returns ERROR_ALREADY_EXISTS, and the guard bails. We intercept CreateMutexA
// and, for exactly that one mutex name, clear the ERROR_ALREADY_EXISTS the OS
// set -- the real mutex is still created/opened, we only hide the
// "already exists" signal the guard keys off. Every other CreateMutexA call in
// the process passes straight through, untouched.
//
// Mechanism: an import-address-table (IAT) patch on the main image's
// kernel32!CreateMutexA slot. It is build-agnostic -- it walks the PE import
// directory by name, with NO hardcoded client offsets -- and it is installed
// from the injected DLL's DllMain, which (because injection is a remote
// LoadLibrary on a CREATE_SUSPENDED process, before ResumeThread) runs before
// the client's startup code calls the guard. So the hook is always in place by
// the time the guard fires.
//
// Opt-in: the whole thing is gated on the FREYA_MULTIBOX environment variable.
// A normal single launch does not set it, so the stock single-instance guard is
// left fully intact; only a launcher-driven multi-box session turns it on.
//==============================================================================
#include <windows.h>
#include <cstring>
#include "FreyaMultiboxHook.h"

// The exact name the client's single-instance guard uses.
static const char* kFreyaSingleInstanceMutex = "enb_mutex_lock";

typedef HANDLE(WINAPI* FreyaCreateMutexAFn)(LPSECURITY_ATTRIBUTES, BOOL, LPCSTR);
static FreyaCreateMutexAFn g_realCreateMutexA = nullptr;

static HANDLE WINAPI FreyaCreateMutexAHook(LPSECURITY_ATTRIBUTES attrs, BOOL initialOwner,
                                           LPCSTR name) {
    HANDLE h = g_realCreateMutexA(attrs, initialOwner, name);
    if (name && _stricmp(name, kFreyaSingleInstanceMutex) == 0) {
        // Real mutex still made; only the "already running" signal is hidden so
        // the second instance proceeds past its guard.
        SetLastError(ERROR_SUCCESS);
    }
    return h;
}

// Overwrite the main image's IAT slot for kernel32!CreateMutexA with our hook.
// Returns true if the slot was found and patched.
static bool PatchIatCreateMutexA(HMODULE module) {
    BYTE* base = reinterpret_cast<BYTE*>(module);
    IMAGE_DOS_HEADER* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE)
        return false;
    IMAGE_NT_HEADERS* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE)
        return false;

    DWORD impRva = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress;
    if (!impRva)
        return false;
    IMAGE_IMPORT_DESCRIPTOR* imp = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + impRva);

    for (; imp->Name; ++imp) {
        // The name table (OriginalFirstThunk) carries the import names; the
        // parallel IAT (FirstThunk) carries the resolved function pointers.
        DWORD nameRefRva = imp->OriginalFirstThunk ? imp->OriginalFirstThunk : imp->FirstThunk;
        IMAGE_THUNK_DATA* nameThunk = reinterpret_cast<IMAGE_THUNK_DATA*>(base + nameRefRva);
        IMAGE_THUNK_DATA* iatThunk = reinterpret_cast<IMAGE_THUNK_DATA*>(base + imp->FirstThunk);
        for (; nameThunk->u1.AddressOfData; ++nameThunk, ++iatThunk) {
            if (nameThunk->u1.Ordinal & IMAGE_ORDINAL_FLAG)
                continue; // imported by ordinal -- no name to match
            IMAGE_IMPORT_BY_NAME* byName =
                reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + nameThunk->u1.AddressOfData);
            if (strcmp(reinterpret_cast<const char*>(byName->Name), "CreateMutexA") != 0)
                continue;

            void** slot = reinterpret_cast<void**>(&iatThunk->u1.Function);
            if (!g_realCreateMutexA)
                g_realCreateMutexA = reinterpret_cast<FreyaCreateMutexAFn>(*slot);
            DWORD oldProt = 0;
            if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &oldProt))
                return false;
            *slot = reinterpret_cast<void*>(&FreyaCreateMutexAHook);
            VirtualProtect(slot, sizeof(void*), oldProt, &oldProt);
            return true;
        }
    }
    return false;
}

void FreyaMultiboxHook_Install() {
    // Opt-in only: a normal single launch leaves the stock guard alone.
    char buf[8];
    DWORD n = GetEnvironmentVariableA("FREYA_MULTIBOX", buf, sizeof(buf));
    if (n == 0 || n >= sizeof(buf) || buf[0] == '0')
        return;

    // Resolve the real CreateMutexA up front so the hook always has a valid
    // target even if the IAT walk picks up a forwarder/thunk.
    HMODULE k32 = GetModuleHandleA("kernel32.dll");
    if (k32)
        g_realCreateMutexA = reinterpret_cast<FreyaCreateMutexAFn>(
            reinterpret_cast<void*>(GetProcAddress(k32, "CreateMutexA")));

    if (PatchIatCreateMutexA(GetModuleHandleA(NULL)))
        OutputDebugStringA(
            "[FreyaMultibox] CreateMutexA IAT hook installed (single-instance bypass)\n");
    else
        OutputDebugStringA("[FreyaMultibox] CreateMutexA IAT slot not found; bypass inactive\n");
}
