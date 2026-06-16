#include "mem.h"

namespace enb {
namespace mem {

thread_local void* g_guard_jmp[16];
thread_local volatile int g_guard_on = 0;

static LONG CALLBACK guard_veh(EXCEPTION_POINTERS* ep) {
    // Only intercept access violations, and only while one of OUR guarded reads is mid-flight on
    // this thread. Everything else (including the game's own faults) passes straight through.
    if (ep->ExceptionRecord->ExceptionCode == EXCEPTION_ACCESS_VIOLATION && g_guard_on) {
        g_guard_on = 0;
        __builtin_longjmp(g_guard_jmp, 1);
    }
    return EXCEPTION_CONTINUE_SEARCH;
}

void install_guard() {
    static bool done = false;
    if (done)
        return;
    done = true;
    AddVectoredExceptionHandler(1 /*first*/, guard_veh);
}

} // namespace mem
} // namespace enb
