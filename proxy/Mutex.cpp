// Mutex.cpp
//
// pthread-based recursive mutex shim. The header (common/include/net7/
// Mutex.h) declares pthread fields unconditionally; on the Win32 PE
// build of net7proxy this lands on winpthreads (MinGW-posix variant),
// which provides the same POSIX threading API the Linux build uses.
//
// DESIGN NOTES:
// pthread_mutex_t isn't recursive by default. This class adds a thread-
// owner field and a lock counter on top so the same thread can re-acquire
// the mutex without deadlocking — matching how the Win32 CRITICAL_SECTION
// (which the historical Win32 launcher used) behaved.

#include "Net7.h"
#include <net7/Mutex.h>

Mutex::Mutex()
{
    pthread_mutex_init(&m_Mutex, NULL);
    m_LockCount = 0;
}

Mutex::~Mutex()
{
    pthread_mutex_destroy(&m_Mutex);
}

void Mutex::Lock()
{
    pthread_t thread_id = pthread_self();
    int ret = pthread_mutex_trylock(&m_Mutex);
    if (ret == 0)
    {
        m_ThreadID = thread_id;
        m_LockCount++;
    }
    else if (pthread_equal(m_ThreadID, thread_id))
    {
        m_LockCount++;
    }
    else
    {
        pthread_mutex_lock(&m_Mutex);
        m_ThreadID = thread_id;
        m_LockCount = 1;
    }
}

void Mutex::Unlock()
{
    if (m_LockCount > 0)
    {
        m_LockCount--;
        if (m_LockCount == 0)
        {
            pthread_mutex_unlock(&m_Mutex);
        }
    }
}
