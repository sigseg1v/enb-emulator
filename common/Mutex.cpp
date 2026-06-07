// Mutex.cpp
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
//
// SINGLE SOURCE OF TRUTH for the Mutex class. This one translation unit is
// compiled into the server, the login-server (Net7SSL), and the proxy -- each
// CMakeLists.txt adds "../common/Mutex.cpp" (or "../../common/Mutex.cpp") to
// its shared-common source list, exactly like common/PosixIpc.cpp and
// common/DtlsTransport.cpp. There used to be three near-identical copies
// (server/src, login-server/Net7SSL, proxy) that drifted out of sync; do NOT
// reintroduce a per-process copy -- edit THIS file.
//
// Implemented as a native recursive pthread mutex. winpthreads provides the
// same API on the Win32 PE proxy build, so the header (common/include/net7/
// Mutex.h) declares a plain pthread_mutex_t unconditionally.
//
// DESIGN NOTES:
// The original Win32 server allowed the same thread to nest Lock() calls.
// PTHREAD_MUTEX_RECURSIVE provides exactly those semantics: a thread that
// already owns the mutex re-acquires it with a kernel-maintained recursion
// count, and each Unlock() decrements that count, releasing only when it
// reaches zero.
//
// This replaces an earlier hand-rolled trylock + m_ThreadID/m_LockCount
// emulation that ORPHANED the mutex under contention: the contended path
// took pthread_mutex_lock() but never recorded ownership, so the matching
// Unlock() saw m_LockCount == 0 and skipped pthread_mutex_unlock(), leaving
// the lock held forever (deterministic deadlock on first true contention --
// observed as threads wedged on GMemoryHandler::m_PlayerMutex). The native
// recursive mutex tracks ownership atomically in the kernel, so there is no
// such window and no separate counter to keep in sync.

#include <net7/Mutex.h>

Mutex::Mutex()
{
    pthread_mutexattr_t attr;
    pthread_mutexattr_init(&attr);
    pthread_mutexattr_settype(&attr, PTHREAD_MUTEX_RECURSIVE);
    pthread_mutex_init(&m_Mutex, &attr);
    pthread_mutexattr_destroy(&attr);
}

Mutex::~Mutex()
{
    pthread_mutex_destroy(&m_Mutex);
}

void Mutex::Lock()
{
    pthread_mutex_lock(&m_Mutex);
}

void Mutex::Unlock()
{
    pthread_mutex_unlock(&m_Mutex);
}
