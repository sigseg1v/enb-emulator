// MessageQueue.h
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

#ifndef _MESSAGE_QUEUE_H_INCLUDED_
#define _MESSAGE_QUEUE_H_INCLUDED_

#include <net7/Mutex.h>

struct MessageEntry
{
    unsigned char *message;
    int length;
	//int sequence_num;
	long player_id;
};

#define QUEUE_INDEX_SIZE 500

//================================================================================
// MessageQueue
//================================================================================

class CircularBuffer
{
public:
    CircularBuffer(long size, long message_slots, bool checkslots=true);
    virtual ~CircularBuffer();
    
    unsigned char * Write(unsigned char *buff, long l, short opcode = 0);
    bool Read(unsigned char *dest, unsigned char *read_ptr, long l);
    bool DeCommit(unsigned char *dest, long l);
    long ToBufferEnd();
    unsigned char * CurrentWrite()       { return m_WritePtr; }
	void SetFillCount(long count)		 { m_FillCount = count; }
    unsigned long Cumulative_Written(bool clear = false);
	void RemoveAllPlayerEntries(long player_id);

	bool RetreiveMessage( unsigned char *pMessage, int length, unsigned char * pBuffer );

	MessageEntry   *GetNextEntry();
    
private:
    unsigned char *	m_Buffer;

    long			m_Size;
    unsigned char *	m_WritePtr;
    
    long			m_FillCount;
    unsigned long	m_Cumulative_Count;

    MessageEntry   *m_EntryBuffer;
    unsigned int    m_EntryIndex;

	Mutex			m_Mutex;

	uint32_t m_MessageSlots;
	uint32_t m_EntriesInQueue;
	u32	  m_WarningTimer;

	bool			m_CheckSlots;
};

class MessageQueue
{
public:
	MessageQueue(char *name, CircularBuffer *circ_buff = (0), long queue_slots = 0, bool check = false);	//queue_slots governs how many slots from the circ_buff this queue can use at once
	virtual ~MessageQueue();

// Public methods
public:
	void Add( char *buffer );
	u8*	 Add( unsigned char *buffer, int length, long player_id, short opcode = 0 );
	void AddHead( char *buffer );
	void AddHead( unsigned char *buffer, int length );
	bool CheckQueue( char *pMessage, long size );
	bool CheckQueue( unsigned char *pMessage, int * length, long size, long *player_id );
	long CheckNextQueueSize();

	void RemoveAllPlayerEntries(long player_id);
	void ResetQueue();

	uint32_t Count() { return m_EntriesInQueue; };

	void RetreiveMessage( unsigned char *pMessage, int length, unsigned char * pBuffer );

// Private member attributes
private:
    Mutex  m_GroupMutex;
    Mutex  m_Mutex;
    CircularBuffer *m_QueueBuffer;

	MessageEntry   **m_Queue;

	uint32_t m_QueueIndexSize;
	uint32_t m_ReadIndex;
	uint32_t m_WriteIndex;

	uint32_t m_TotalAdded;
	uint32_t m_TotalAddedToHead;
	uint32_t m_TotalAddedToTail;
	uint32_t m_TotalRemoved;
	uint32_t m_EntriesInQueue;

	char *m_Name;
	bool  m_CheckQueue;
	u32	  m_WarningTimer;
};

#endif // _MESSAGE_QUEUE_H_INCLUDED_

