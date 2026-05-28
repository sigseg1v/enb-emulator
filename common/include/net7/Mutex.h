// Mutex.h
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
** Copyright of our assets/code/software began in 2005-2009 �, Net-7 Entertainment.
**
*/

#ifndef _MUTEX_H_INCLUDED_
#define _MUTEX_H_INCLUDED_

// pthread is used unconditionally — winpthreads (MinGW-posix variant) makes
// the same API available on the Win32 PE build of net7proxy. Picking one
// implementation here avoids the header/body skew that came up when
// minwindef.h (via windows.h) re-defines the bare `WIN32` macro inside
// a TU compiled with -UWIN32, leaving the header on the pthread branch
// while the .cpp tries to use CRITICAL_SECTION (or vice versa).
#include <pthread.h>

class Mutex
{
public:
    Mutex();
    virtual ~Mutex();

public:
    void Lock();
    void Unlock();

private:
    pthread_mutex_t m_Mutex;
    pthread_t m_ThreadID;
    int m_LockCount;
};

#endif // _MUTEX_H_INCLUDED_
