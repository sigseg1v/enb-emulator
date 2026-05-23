#ifndef _MAILSLOTMANAGER_SSL_H_INCLUDED_
#define _MAILSLOTMANAGER_SSL_H_INCLUDED_

//#include "Net7SSL.h"
//#include <stdio.h>

extern LPTSTR g_OutputSlot;
extern LPTSTR g_InputSlot;
extern LPTSTR g_EventName;

class MailManager
{
public:
	MailManager(); //set up receive slot
	~MailManager();

	bool WriteMessage(char *message);
	void CheckMessages();
	void HandleMessage(short opcode, short slot, short bytes);
	void HandleMessage();
	void ResetMailSystem();

private:
	void SetUpSendSlot();

	HANDLE m_hSlot;
	HANDLE m_hFile; 
	HANDLE m_hEvent;
	bool   m_SendSlotInit;
	unsigned char m_Buffer[1024];
};

#define LOCAL_PING_SSL_SERVER			0x04 //keepalive ping from ssl to server
#define LOCAL_PING_SERVER_SSL			0x05 //keepalive ping from server to ssl

#endif //_MAILSLOTMANAGER_SSL_H_INCLUDED_