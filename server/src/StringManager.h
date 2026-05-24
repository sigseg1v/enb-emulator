// StringManager.h
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

#ifndef _STRING_MANAGER_H_INCLUDED_
#define _STRING_MANAGER_H_INCLUDED_

#include "Net7.h"
#include <net7/Mutex.h>

#define STRING_TABLE_SIZE 320000
#define STRING_MEMORY_SIZE 20971520 //20MB: 1024 * 1024 * 20

#define STRING_EMPTY 0
#define STRING_BUSY 1

class StringManager
{
public:
    StringManager();
    ~StringManager();

public:
    char * GetStr(char *);
    char * GetStr(const char *);
    char * NullStr();
    void PrintOut();
    void Statistics(char * = 0, int BufferSize = 0);

private:
    struct StringHashElement
    {
        char * Str;
        unsigned long Busy;
    } ATTRIB_PACKED;

    struct StringHashTable
    {
        unsigned long Size;
        StringHashElement * Table;
    } ATTRIB_PACKED;;

    unsigned long HashStr(char *);
    unsigned long HashCollision(unsigned long, char *);
    char * AddStr(unsigned long, char *);

private:
    StringHashTable m_HashTable;
    char * m_Memory;
    char * m_CurLoc;
    long m_CurrentSize;
    Mutex m_Mutex;
};

#endif