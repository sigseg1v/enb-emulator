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
// Encapsulates mutual exclusion methods for both Windows and Linux platforms.
//
// DESIGN NOTES:
// Windows allows nested calls to the mutex lock method, but Linux does not.
// This class has been modified to add a "Lock Count" to pthreads_mutex in order
// to support nested calls.

// In practice a mutex is a semaphore with count 1



//#include "Net7.h"
#include <stdio.h>
#include <net7/Mutex.h>

Mutex::Mutex()
{
#ifdef WIN32
    InitializeCriticalSection(&m_Mutex);
#else
    pthread_mutex_init(&m_Mutex, NULL);
    m_LockCount = 0;
#endif
}

Mutex::~Mutex()
{
#ifdef WIN32
    DeleteCriticalSection(&m_Mutex);
#else
    pthread_mutex_destroy(&m_Mutex);
#endif
}

void Mutex::Lock()
{
#ifdef WIN32

    bool locked = false;
	if (m_Mutex.LockCount > 0 && m_Mutex.OwningThread > 0)
    {
        fprintf(stderr,"Mutex appears locked %d.", m_Mutex.LockCount);
        locked = true;
    }
    EnterCriticalSection(&m_Mutex);

    if (locked)
    {
        fprintf(stderr,"Mutex unlocked, and re-blocked [%d].\n", m_Mutex.LockCount);
    }
#else

    pthread_t thread_id = pthread_self();
    int ret = pthread_mutex_trylock(&m_Mutex);
    if (ret == 0)
    {
        // Lock was successful
        m_ThreadID = thread_id;
        m_LockCount++;
    }
    else if (m_ThreadID == thread_id)
    {
        // Lock failed, we already own the lock
        m_LockCount++;
    }
    else
    {
        // Another thread owns the lock.
        // We have no choice but to wait until they release it.
        pthread_mutex_lock(&m_Mutex);
    }

#endif
}

void Mutex::Unlock()
{
#ifdef WIN32
    LeaveCriticalSection(&m_Mutex);

#else
    if (m_LockCount > 0)
    {
        m_LockCount--;
        if (m_LockCount == 0)
        {
            // When the lock count reaches zero, release the resource
            pthread_mutex_unlock(&m_Mutex);
        }
    }
    // else mutex is not locked, ignore the call
#endif
}

