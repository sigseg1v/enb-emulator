// ItemList.cpp
//
//
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

#include "Net7.h"
#include "ItemList.h"

ItemList::ItemList()
{
    m_ItemList = new long[INITIAL_ARRAY_SIZE];
    if (m_ArraySize)
    {
        m_ArraySize = INITIAL_ARRAY_SIZE;
    }
    else
    {
        m_ArraySize = 0;
    }
    m_NumItems = 0;
}

ItemList::~ItemList()
{
    if (m_ItemList)
    {
        delete [] m_ItemList;
    }
}

bool ItemList::IsItemOnList(long item)
{
    bool found = false;

    for (long i=0; i<m_NumItems; i++)
    {
        if (m_ItemList[i] == item)
        {
            found = true;
            break;
        }
    }

    return found;
}

void ItemList::AddItemToList(long item)
{
    if (m_ItemList)
    {
        // Is the array full?
        if (m_NumItems == m_ArraySize)
        {
            LogMessage("ItemList - Array of size [%d] full. Adding [%d] slots!\n",m_ArraySize,ARRAY_SIZE_INCREMENT);

            // The array is full, we need to increase the size of the list
            m_ArraySize += ARRAY_SIZE_INCREMENT;
            long * array = new long[m_ArraySize];
            memcpy(array, m_ItemList, sizeof(long) * m_NumItems);
            delete [] m_ItemList;
            m_ItemList = array;
        }

        if (m_NumItems < m_ArraySize)
        {
            // We have room in the array for another item
            m_ItemList[m_NumItems++] = item;
        }
        else
        {
            // Fatal error -- num items is greater than the array size.
        }
    }
}

long ItemList::GetNumItems()
{
    return m_NumItems;
}

long ItemList::GetItem(long index)
{
    long item = -1;
    if (m_ItemList && (index >= 0) && (index < m_NumItems))
    {
        item = m_ItemList[index];
    }
    return (item);
}

void ItemList::ClearList()
{
    for (long i=0; i<m_NumItems; i++)
    {
        m_ItemList[i] = 0;
    }

    m_NumItems = 0;
}

