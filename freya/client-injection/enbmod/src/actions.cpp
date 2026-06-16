#include "actions.h"
#include "log.h"
#include <windows.h>

namespace enb {
namespace actions {

// ---- find the game's top-level window ---------------------------------------
struct FindCtx {
    DWORD pid;
    HWND hwnd;
};
static BOOL CALLBACK enum_cb(HWND h, LPARAM lp) {
    FindCtx* c = (FindCtx*)lp;
    DWORD pid = 0;
    GetWindowThreadProcessId(h, &pid);
    if (pid == c->pid && IsWindowVisible(h) && GetWindow(h, GW_OWNER) == nullptr) {
        c->hwnd = h;
        return FALSE; // stop at first visible owned-by-none top-level
    }
    return TRUE;
}
void* game_hwnd() {
    FindCtx c{GetCurrentProcessId(), nullptr};
    EnumWindows(enum_cb, (LPARAM)&c);
    return c.hwnd;
}

static LPARAM key_lparam(int vk, bool down) {
    UINT scan = MapVirtualKeyA(vk, MAPVK_VK_TO_VSC);
    LPARAM lp = (1) | (scan << 16);
    if (!down)
        lp |= (1u << 30) | (1u << 31); // previous-state + transition (key-up)
    return lp;
}

bool post_key(int vk, bool down) {
    HWND h = (HWND)game_hwnd();
    if (!h) {
        logf("actions: no game window");
        return false;
    }
    return PostMessageA(h, down ? WM_KEYDOWN : WM_KEYUP, (WPARAM)vk, key_lparam(vk, down)) != 0;
}
bool tap_key(int vk) {
    bool a = post_key(vk, true);
    bool b = post_key(vk, false);
    return a && b;
}

bool post_char(unsigned cp) {
    HWND h = (HWND)game_hwnd();
    if (!h)
        return false;
    return PostMessageW(h, WM_CHAR, (WPARAM)cp, 1) != 0;
}

// ---- generic __thiscall caller ----------------------------------------------
// One asm block: save ESP (in a callee-saved reg GCC will preserve), push args right-to-left,
// set ECX = this, call, restore ESP (fixes any cleanup-convention mismatch), capture EAX.
uint32_t call_thiscall(uintptr_t fn, uintptr_t thisptr, const uint32_t* args, int argc) {
    if (argc < 0)
        argc = 0;
    if (argc > 8)
        argc = 8;
    uint32_t ret = 0;
    // Load ALL inputs into registers first (while esp is unchanged, so the esp-relative memory
    // operands resolve correctly), THEN push args. edx=fn, ecx=this, esi=args, edi=argc, ebx=saved esp.
    __asm__ __volatile__(
        "movl %[fn],    %%edx\n\t"
        "movl %[thisp], %%ecx\n\t"
        "movl %[args],  %%esi\n\t"
        "movl %[argc],  %%edi\n\t"
        "movl %%esp,    %%ebx\n\t" // save esp (restored after, fixes any cleanup mismatch)
        "testl %%edi, %%edi\n\t"
        "jz 2f\n\t"
        "leal -4(%%esi,%%edi,4), %%esi\n\t" // esi -> &args[argc-1]
        "1:\n\t"
        "pushl (%%esi)\n\t"
        "subl $4, %%esi\n\t"
        "decl %%edi\n\t"
        "jnz 1b\n\t"
        "2:\n\t"
        "call *%%edx\n\t" // this already in ecx
        "movl %%ebx, %%esp\n\t"
        "movl %%eax, %[ret]\n\t"
        : [ret] "=m"(ret)
        : [fn] "m"(fn), [thisp] "m"(thisptr), [args] "m"(args), [argc] "m"(argc)
        : "eax", "ecx", "edx", "esi", "edi", "ebx", "cc", "memory");
    return ret;
}

uint32_t call_cdecl(uintptr_t fn, const uint32_t* args, int argc) {
    return call_thiscall(fn, 0, args, argc); // ecx ignored by a cdecl/stdcall callee
}

} // namespace actions
} // namespace enb
