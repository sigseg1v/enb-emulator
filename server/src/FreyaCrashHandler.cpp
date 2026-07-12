// FreyaCrashHandler.cpp
/* Net-7 Entertainment: Net-7 Earth and Beyond emulator project
**
** This code/content is licensed under the Creative Commons license, it is interactive content. You can view the terms of our:
** Creative Commons Attribution-Noncommercial-Share Alike 3.0 United States License
** http://creativecommons.org/licenses/by-nc-sa/3.0/us/
**
** Net-7 Emulator Project, an Earth & Beyond emulator by Net7 Entertainment is licensed under a Creative Commons Attribution-Noncommercial-Share Alike 3.0 United States License
**
** Based on a work at http://www.earthandbeyond.com
**
** Permissions beyond the scope of this license may be available at http://www.dreamersofdawn.org/docs/More_Information.htm
**
** The license can be modified at our discretion within the bounds of Creative Commons at any time.
**
** Copyright of our assets/code/software began in 2005-2009 ©, Net-7 Entertainment.
**
*/

// Fatal-signal backtrace handler.
//
// The server had NO handler for SIGSEGV/SIGABRT/SIGBUS/SIGFPE/SIGILL: a crash
// died with only the kernel's bare `net7[pid]: segfault at ADDR ip IP` line,
// which carries no stack and no game context. That made intermittent
// production crashes (an aggressive-mob aggro path faulting in a sector, a
// heap-corruption general-protection-fault in libc) effectively
// undiagnosable without a live repro -- see plans/41 PB-76 (Fenris).
//
// This installs a handler that, on a fatal signal, writes the signal, the
// fault address, and a symbolized backtrace to stderr (fd 2). The server runs
// in a container, so stderr is captured by `docker logs` and survives the
// process death -- the next crash leaves a stack behind. The handler then
// restores the default disposition and re-raises, so the kernel still logs
// its line and (where enabled) a core is still produced.
//
// Only async-signal-safe primitives are used on the crash path: backtrace()
// and backtrace_symbols_fd() (the fd variant does not call malloc, unlike
// backtrace_symbols()), plus raw write(2). No printf, no malloc.
//
// Requires -rdynamic on the link line for backtrace_symbols_fd to resolve
// our own function names (the dynamic symbol table); without it the frames
// still print as `net7(+0xOFFSET)`, which is addr2line-able against the
// unstripped build. -g gives file:line on top of that.

#ifndef WIN32

#include <execinfo.h>
#include <signal.h>
#include <unistd.h>
#include <string.h>
#include <stdint.h>

namespace {

// Async-signal-safe raw write of a NUL-terminated string.
void safe_write(const char* s)
{
    if (!s) return;
    ssize_t n = (ssize_t)strlen(s);
    while (n > 0) {
        ssize_t w = write(STDERR_FILENO, s, (size_t)n);
        if (w <= 0) break;
        s += w;
        n -= w;
    }
}

// Async-signal-safe write of an unsigned value as 0x-prefixed hex.
void safe_write_hex(uintptr_t v)
{
    char buf[2 + 2 * sizeof(uintptr_t) + 1];
    char* p = buf;
    *p++ = '0';
    *p++ = 'x';
    bool started = false;
    for (int shift = (int)(sizeof(uintptr_t) * 8) - 4; shift >= 0; shift -= 4) {
        unsigned nyb = (unsigned)((v >> shift) & 0xF);
        if (nyb || started || shift == 0) {
            *p++ = (char)(nyb < 10 ? '0' + nyb : 'a' + (nyb - 10));
            started = true;
        }
    }
    *p = '\0';
    safe_write(buf);
}

const char* signal_name(int signo)
{
    switch (signo) {
        case SIGSEGV: return "SIGSEGV";
        case SIGABRT: return "SIGABRT";
        case SIGBUS:  return "SIGBUS";
        case SIGFPE:  return "SIGFPE";
        case SIGILL:  return "SIGILL";
        default:      return "signal";
    }
}

void crash_handler(int signo, siginfo_t* info, void* /*ucontext*/)
{
    safe_write("\n==== Net7 FATAL ");
    safe_write(signal_name(signo));
    if (info) {
        safe_write(" fault_addr=");
        safe_write_hex((uintptr_t)info->si_addr);
    }
    safe_write(" ====\nbacktrace (symbolize with: addr2line -f -e net7 <offset>):\n");

    void* frames[64];
    int n = backtrace(frames, 64);
    // fd variant is async-signal-safe (no malloc).
    backtrace_symbols_fd(frames, n, STDERR_FILENO);
    safe_write("==== end backtrace ====\n");

    // Restore the default disposition and re-raise so the kernel logs its
    // segfault line and a core is produced where enabled. SA_RESETHAND has
    // already reset us to SIG_DFL for this signal; raise() re-delivers it.
    raise(signo);
}

} // namespace

// Called once from main() after the shutdown-signal handlers are installed.
void FreyaInstallCrashHandler()
{
    // Run the handler on a dedicated stack so a stack-overflow SIGSEGV (guard
    // page hit -> no room on the thread stack) can still execute it. 64 KiB is
    // comfortably above SIGSTKSZ; a literal is used because on glibc >= 2.34
    // SIGSTKSZ is a runtime sysconf() value, not a compile-time constant.
    static const size_t kAltStackSize = 64 * 1024;
    static char alt_stack[kAltStackSize];
    stack_t ss{};
    ss.ss_sp = alt_stack;
    ss.ss_size = sizeof(alt_stack);
    ss.ss_flags = 0;
    sigaltstack(&ss, nullptr);

    struct sigaction sa{};
    sa.sa_sigaction = crash_handler;
    sigemptyset(&sa.sa_mask);
    // SA_SIGINFO: hand us siginfo (fault address).
    // SA_ONSTACK: use the alternate stack installed above.
    // SA_RESETHAND: after we run, disposition returns to default so the
    //   re-raise inside the handler actually terminates (no recursion).
    // SA_NODEFER: don't block the signal while handling -- lets a fault
    //   inside the handler itself terminate rather than deadlock.
    sa.sa_flags = SA_SIGINFO | SA_ONSTACK | SA_RESETHAND | SA_NODEFER;

    sigaction(SIGSEGV, &sa, nullptr);
    sigaction(SIGABRT, &sa, nullptr);
    sigaction(SIGBUS,  &sa, nullptr);
    sigaction(SIGFPE,  &sa, nullptr);
    sigaction(SIGILL,  &sa, nullptr);
}

#endif // !WIN32
