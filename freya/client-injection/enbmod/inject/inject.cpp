// inject.exe -- minimal CreateRemoteThread/LoadLibraryA injector for enbmod.dll.
//
//   inject.exe [--pid N | --proc client.exe] [path\to\enbmod.dll]
//
// Defaults: target process "client.exe", DLL = "enbmod.dll" next to this exe.
// 32-bit injecting into a 32-bit target (must match). Runs fine under Wine.

#include <windows.h>
#include <tlhelp32.h>
#include <psapi.h>
#include <cstdio>
#include <string>

static DWORD find_pid(const char* name) {
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE)
        return 0;
    PROCESSENTRY32 pe{sizeof pe};
    DWORD pid = 0;
    if (Process32First(snap, &pe)) {
        do {
            if (_stricmp(pe.szExeFile, name) == 0) {
                pid = pe.th32ProcessID;
                break;
            }
        } while (Process32Next(snap, &pe));
    }
    CloseHandle(snap);
    return pid;
}

static std::string exe_dir() {
    char p[MAX_PATH];
    GetModuleFileNameA(nullptr, p, MAX_PATH);
    std::string s(p);
    auto pos = s.find_last_of('\\');
    return pos == std::string::npos ? "." : s.substr(0, pos);
}

int main(int argc, char** argv) {
    const char* proc = "client.exe";
    DWORD pid = 0;
    std::string dll;

    for (int i = 1; i < argc; ++i) {
        std::string a = argv[i];
        if (a == "--pid" && i + 1 < argc)
            pid = (DWORD)strtoul(argv[++i], nullptr, 10);
        else if (a == "--proc" && i + 1 < argc)
            proc = argv[++i];
        else
            dll = a;
    }
    if (dll.empty())
        dll = exe_dir() + "\\enbmod.dll";

    // absolutize the DLL path (LoadLibrary in the target uses ITS cwd otherwise)
    char full[MAX_PATH];
    if (GetFullPathNameA(dll.c_str(), MAX_PATH, full, nullptr))
        dll = full;
    if (GetFileAttributesA(dll.c_str()) == INVALID_FILE_ATTRIBUTES) {
        fprintf(stderr, "[inject] dll not found: %s\n", dll.c_str());
        return 2;
    }

    if (!pid)
        pid = find_pid(proc);
    if (!pid) {
        fprintf(stderr, "[inject] process not found: %s\n", proc);
        return 3;
    }
    printf("[inject] target pid=%lu dll=%s\n", pid, dll.c_str());

    HANDLE h = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_VM_OPERATION | PROCESS_VM_WRITE |
                               PROCESS_VM_READ | PROCESS_QUERY_INFORMATION,
                           FALSE, pid);
    if (!h) {
        fprintf(stderr, "[inject] OpenProcess failed: %lu\n", GetLastError());
        return 4;
    }

    SIZE_T len = dll.size() + 1;
    void* remote = VirtualAllocEx(h, nullptr, len, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remote) {
        fprintf(stderr, "[inject] VirtualAllocEx failed\n");
        CloseHandle(h);
        return 5;
    }
    if (!WriteProcessMemory(h, remote, dll.c_str(), len, nullptr)) {
        fprintf(stderr, "[inject] WriteProcessMemory failed\n");
        CloseHandle(h);
        return 6;
    }

    // LoadLibraryA lives at the same address in the target (kernel32 is loaded at a shared base).
    HMODULE k32 = GetModuleHandleA("kernel32.dll");
    auto loadlib = (LPTHREAD_START_ROUTINE)GetProcAddress(k32, "LoadLibraryA");
    HANDLE th = CreateRemoteThread(h, nullptr, 0, loadlib, remote, 0, nullptr);
    if (!th) {
        fprintf(stderr, "[inject] CreateRemoteThread failed: %lu\n", GetLastError());
        CloseHandle(h);
        return 7;
    }

    WaitForSingleObject(th, 10000);
    DWORD ret = 0;
    GetExitCodeThread(th, &ret);
    printf("[inject] remote LoadLibraryA returned 0x%lx %s\n", ret,
           ret ? "(loaded)" : "(FAILED -- module handle 0)");

    VirtualFreeEx(h, remote, 0, MEM_RELEASE);
    CloseHandle(th);
    CloseHandle(h);
    return ret ? 0 : 8;
}
