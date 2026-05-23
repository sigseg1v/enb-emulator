// MessageQueue.cpp
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

//////////////////////////////////////////////////////////////////////
// Include Files
//////////////////////////////////////////////////////////////////////

#include "Net7.h"
#include "MessageQueue.h"

////////////////////////////////////////////////////////////////////////////////
// Class : MessageQueue
////////////////////////////////////////////////////////////////////////////////

MessageQueue::MessageQueue(char *name, CircularBuffer *circ_buff, long queue_slots, bool check)
{
	m_TotalAdded = 0;
	m_TotalAddedToHead = 0;
	m_TotalAddedToTail = 0;
	m_TotalRemoved = 0;
	m_EntriesInQueue = 0;
	m_ReadIndex = 0;
	m_WriteIndex = 0;
	m_Name = name;
	m_WarningTimer = 0;
	m_CheckQueue = check;

	if (circ_buff)
	{
		m_QueueBuffer = circ_buff;
	}
	else
	{
		LogMessage("WARNING !!!! This should not be used now\n");
		m_QueueBuffer = new CircularBuffer(16384, 128);
	}

	if (queue_slots > 0)
	{
		m_QueueIndexSize = queue_slots;
	}
	else
	{
		m_QueueIndexSize = 200;//QUEUE_INDEX_SIZE;
	}

	m_Queue = new MessageEntry*[m_QueueIndexSize];
	memset(m_Queue, 0, sizeof(MessageEntry*)*m_QueueIndexSize);
}

MessageQueue::~MessageQueue()
{
	delete [] m_Queue;

	//char *message;
    //delete m_QueueBuffer;

	/*while (CheckQueue( message ))
	{
		delete [] message;
	}*/
}

void MessageQueue::ResetQueue()
{
	m_TotalAdded = 0;
	m_TotalAddedToTail = 0;
	m_EntriesInQueue = 0;
	m_TotalAddedToHead = 0;
	m_TotalAddedToTail = 0;
	m_TotalRemoved = 0;
	m_ReadIndex = 0;
	m_WriteIndex = 0;
	m_WarningTimer = 0;
	memset(m_Queue, 0, sizeof(m_Queue));
}

void MessageQueue::Add( char *buffer )
{
    Add((unsigned char *) buffer, strlen(buffer) + 1, 0);
}

u8* MessageQueue::Add( unsigned char *buffer, int length, long player_id, short opcode)
{
	u8 *packet = (0);

	try 
	{
		MessageEntry *new_entry = m_QueueBuffer->GetNextEntry();
		new_entry->message = m_QueueBuffer->Write(buffer, length, opcode);

		new_entry->length = length;
		new_entry->player_id = player_id;
		packet = new_entry->message;

		if (m_CheckQueue && m_Queue[m_WriteIndex] != 0)
		{
			u32 tick = GetNet7TickCount();
			if (tick > (m_WarningTimer + 5000))
			{
				LogMessage(">> Slots in Queue for %s need to be extended. Present size = %d\n", m_Name, m_QueueIndexSize);
				m_WarningTimer = tick;
			}
		}

		// Critical Section
		m_Mutex.Lock();

		//Write to write index
		m_Queue[m_WriteIndex] = new_entry;

		m_WriteIndex++;

		if (m_WriteIndex >= m_QueueIndexSize)
		{
			m_WriteIndex = 0;
		}

		m_TotalAdded++;
		m_TotalAddedToTail++;
		m_EntriesInQueue++;
	}
	catch (...)
	{
		LogMessage("Failure in MessageQueue::Add()\n");
	}
	m_Mutex.Unlock();

	return packet;
}

void MessageQueue::AddHead( char *buffer )
{
	MessageEntry *new_entry = m_QueueBuffer->GetNextEntry();
	new_entry->message = m_QueueBuffer->Write((unsigned char*)buffer, strlen(buffer) + 1);
    new_entry->length = strlen(buffer) + 1;

    // Critical Section
    m_Mutex.Lock();

	//Write to write index
	m_Queue[m_WriteIndex] = new_entry;

	m_WriteIndex++;

	if (m_WriteIndex >= m_QueueIndexSize)
	{
		m_WriteIndex = 0;
	}

	m_TotalAdded++;
	m_TotalAddedToHead++;
	m_EntriesInQueue++;

    m_Mutex.Unlock();
}

void MessageQueue::AddHead( unsigned char *buffer, int length )
{
	MessageEntry *new_entry = m_QueueBuffer->GetNextEntry();
	new_entry->message = m_QueueBuffer->Write(buffer, length);
    new_entry->length = length;

    // Critical Section
    m_Mutex.Lock();

	//Write to write index
	m_Queue[m_WriteIndex] = new_entry;

	m_WriteIndex++;

	if (m_WriteIndex >= m_QueueIndexSize)
	{
		m_WriteIndex = 0;
	}

	m_TotalAdded++;
	m_TotalAddedToHead++;
	m_EntriesInQueue++;

    m_Mutex.Unlock();
}

// returns true if message was removed from the Queue
bool MessageQueue::CheckQueue( char *pMessage, long buffer_size )
{
    int length;
	long player_id;
    return CheckQueue( (unsigned char *) pMessage, &length, buffer_size, &player_id );
}

long MessageQueue::CheckNextQueueSize()
{
	long size = 0;
	MessageEntry *this_entry;
	m_Mutex.Lock();

	if (m_ReadIndex != m_WriteIndex)
	{
		this_entry = m_Queue[m_ReadIndex];
		if (this_entry)
		{
			size = this_entry->length;
		}
	}

	m_Mutex.Unlock();

	return size;
}

// returns true if message was removed from the Queue
bool MessageQueue::CheckQueue( unsigned char *pMessage, int *length, long buffer_size, long *player_id)
{
	bool success = false;
    MessageEntry *this_entry;
	*length = 0;
	*player_id = 0;

	m_Mutex.Lock();

	if (m_ReadIndex != m_WriteIndex)
	{
		//there's something to read here
		this_entry = m_Queue[m_ReadIndex];
		if (this_entry)
		{
			*length = this_entry->length;
			if (this_entry->length > buffer_size)
			{
				LogMessage("Error, buffer size for Queue not large enough: is %d, message size is %d\n", buffer_size, this_entry->length);
			}
			else
			{
				if (m_QueueBuffer->Read(pMessage, this_entry->message, this_entry->length))
				{
					success = true;
					*player_id = this_entry->player_id;
				}
				this_entry->length = 0;
				this_entry->message = 0;
			}
			m_Queue[m_ReadIndex] = 0;
			m_ReadIndex++;
        }
		
		if (m_ReadIndex >= m_QueueIndexSize)
		{
			m_ReadIndex = 0;
		}
        m_TotalRemoved++;
		m_EntriesInQueue--;
	}

	m_Mutex.Unlock();

	return (success);
}

void MessageQueue::RetreiveMessage( unsigned char *pMessage, int length, unsigned char * pBuffer )
{
	m_QueueBuffer->Read(pMessage, pBuffer, length);
}

void MessageQueue::RemoveAllPlayerEntries(long player_id)
{
	m_QueueBuffer->RemoveAllPlayerEntries(player_id);
}


/*********************************************************
 *
 * Circular Buffer Methods
 *
 *********************************************************/



CircularBuffer::CircularBuffer(long size, long message_slots, bool check_slots)
{
    m_Buffer = new unsigned char[size];

	m_MessageSlots = message_slots;

	m_EntryBuffer = new MessageEntry[m_MessageSlots];

	g_cumulative_mem += size + sizeof(MessageEntry)*m_MessageSlots;

	m_WarningTimer = 0;

	m_CheckSlots = check_slots;

    if (m_Buffer && m_EntryBuffer)
    {
        m_Size      = size;
        m_WritePtr  = m_Buffer;
        m_FillCount = 0;
		m_EntriesInQueue = 0;
		m_EntryIndex = 0;
		memset (m_EntryBuffer, 0, sizeof(MessageEntry)*m_MessageSlots);
	}
	else
	{
        LogMessage("Unable to allocate %d bytes for Circular Buffer\n",size);
		m_Size = 0;
		m_WritePtr  = 0;
    }
}

CircularBuffer::~CircularBuffer()
{
    delete[] m_Buffer;
	delete[] m_EntryBuffer;
    m_Size = 0;
}

MessageEntry *CircularBuffer::GetNextEntry()
{
    MessageEntry *this_entry;

	//we need to mutex this now as we're using a global buffer for received player opcodes
	//- these can come from the TCP receive threads
	m_Mutex.Lock();
    this_entry = &m_EntryBuffer[m_EntryIndex];

    ++m_EntryIndex;
	++m_EntriesInQueue;

	if (m_EntriesInQueue > m_MessageSlots)
	{
		u32 tick = GetNet7TickCount();
		if (m_CheckSlots && (tick > (m_WarningTimer + 5000)))
		{
			LogMessage(">> Slots for circular buffer need to be bumped. Current setting: %d [%d]\n", m_MessageSlots, m_EntriesInQueue);
			m_WarningTimer = tick;
		}
		if (m_EntriesInQueue > 0xFFFF0000)
		{
			m_EntriesInQueue = 0;
		}
	}
	
	//By the circular nature of the way messages are checked
	//we should never smack into an unread message of another player
    if (m_EntryIndex >= m_MessageSlots)
    {
        m_EntryIndex = 0;
    }
	m_Mutex.Unlock();

    return this_entry;
}

unsigned char * CircularBuffer::Write(unsigned char *buff, long size, short opcode)
{
    //can we grab space from the buffer?
	/*if (size > (m_Size - m_FillCount))
	{
		LogMessage("WARNING:: We may be smacking the back of the queue buffer.\n");
		m_FillCount = 0;
    }*/

	m_Mutex.Lock();
    m_Cumulative_Count += size;
    m_FillCount += size;

#ifdef _DEBUG_CHECKS
	if (m_WritePtr > (m_Buffer + m_Size))
	{
		LogMessage("Error with write ptr, greater than buffer.\n");
		m_WritePtr = m_Buffer;
	}
#endif

	unsigned char *start_write = m_WritePtr;

	if ((m_WritePtr + size) > (m_Buffer + m_Size)) 
	{
		long write1 = m_Size - (m_WritePtr - m_Buffer);
		memcpy(m_WritePtr, buff, write1);
		memcpy(m_Buffer, buff+write1, size - write1);
		m_WritePtr = m_Buffer + (size - write1);
	}
	else
	{
		memcpy(m_WritePtr, buff, size);
		m_WritePtr += size;
	}
	m_Mutex.Unlock();

	//now check the buffer guard bands

    return start_write;
}

bool CircularBuffer::Read(unsigned char *buff, unsigned char *read_ptr, long size)
{
	//check for errors
	if (read_ptr > (m_Buffer + m_Size))
	{
		return false;
	}
    //see if buffer goes off the end
    if ((read_ptr + size) > (m_Buffer + m_Size))
    {
        long read1 = m_Size - (read_ptr - m_Buffer);
		memcpy(buff, read_ptr, read1);
		memcpy(buff + read1, m_Buffer, size - read1);
    }
    else
    {
        memcpy(buff, read_ptr, size);
    }

	m_EntriesInQueue--;

    return true;
}

void CircularBuffer::RemoveAllPlayerEntries(long player_id)
{
	/*for (u32 i = 0; i < m_MessageSlots; i++)
	{
		//are there any messages from this player still on the queue?
		if (m_EntryBuffer[i].player_id == player_id)
		{
			//Decommit the entry
			DeCommit(m_EntryBuffer[i].message, m_EntryBuffer[i].length);
		}
	}*/
}

bool CircularBuffer::DeCommit(unsigned char *dest, long size)
{
	/*m_Mutex.Lock();
    m_FillCount -= size;
	if (m_FillCount < 0) m_FillCount = 0;
	m_EntriesInQueue--;
	if (m_EntriesInQueue < 0) m_EntriesInQueue = 0;
	m_Mutex.Unlock();*/
    return true;
}

unsigned long CircularBuffer::Cumulative_Written(bool clear)
{
    unsigned long count = m_Cumulative_Count;
    if (clear) m_Cumulative_Count = 0;
    return count;
}

long CircularBuffer::ToBufferEnd()
{
	long to_end = m_Size - ((m_WritePtr - m_Buffer) + 1);

#ifdef _DEBUG_CHECKS
	if (to_end < 0)
	{
		LogMessage("Negative buffer spill value.\n");
		to_end = 0;
	}

#else
	if (to_end < 0) to_end = 0;
#endif

	return to_end;
}