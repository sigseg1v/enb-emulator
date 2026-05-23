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
#include "MailslotManager.h"
#include "Net7.h"

#ifdef WIN32
// ---------------------------------------------------------------------------
// Original Win32 mailslot implementation. Linux build uses the POSIX path
// below (see #else // !WIN32).
// ---------------------------------------------------------------------------

bool MailManager::WriteMessage(char *message)
{
	BOOL fResult; 
	DWORD cbWritten; 

	if (m_SendSlotInit == false) // set up the send slot if not already done so
	{
		SetUpSendSlot();
	}

	fResult = WriteFile(m_hFile, 
		message, 
		(DWORD) (lstrlen(message)+1)*sizeof(TCHAR),  
		&cbWritten, 
		(LPOVERLAPPED) NULL); 

	if (!fResult) 
	{ 
		LogMessage("WriteFile failed with %d.\n", GetLastError()); 
		return FALSE; 
	} 

	return TRUE;
}

void MailManager::CheckMessages()
{
    DWORD cbMessage, cMessage, cbRead; 
    BOOL fResult; 
	DWORD cAllMessages; 
	OVERLAPPED ov;
	short opcode;
	short slot;
	short bytes;

	cbMessage = cMessage = cbRead = 0; 

	ov.Offset = 0;
	ov.OffsetHigh = 0;
	ov.hEvent = m_hEvent;

	fResult = GetMailslotInfo( m_hSlot, // mailslot handle 
		(LPDWORD) NULL,               // no maximum message size 
		&cbMessage,                   // size of next message 
		&cMessage,                    // number of messages 
		(LPDWORD) NULL);              // no read time-out 

	if (!fResult) 
	{ 
		LogMessage("GetMailslotInfo failed with %d.\n", GetLastError()); 
		return; 
	} 

	if (cbMessage == MAILSLOT_NO_MESSAGE) 
	{ 
		return; 
	} 

	cAllMessages = cMessage;

	while (cMessage != 0)  // retrieve all messages
	{ 

		memset (m_Buffer, 0, 1024);

		if (cbMessage > 1024)
		{
			LogMessage("Error! Buffer overflow in CheckMessages()\n");
		}

		fResult = ReadFile(m_hSlot, 
			m_Buffer, 
			cbMessage, 
			&cbRead, 
			&ov); 

		if (!fResult) 
		{ 
			LogMessage("ReadFile failed with %d.\n", GetLastError()); 
			return; 
		} 

		// call message handler
		// get opcode 
		opcode = (short)m_Buffer[0];

		// get slot this message refers to (if any)
		slot = (short)m_Buffer[2];

		// get message length
		bytes = (short)m_Buffer[4];

		// message starts at m_Buffer[6]
		
		HandleMessage();

		fResult = GetMailslotInfo(m_hSlot,  // mailslot handle 
			(LPDWORD) NULL,               // no maximum message size 
			&cbMessage,                   // size of next message 
			&cMessage,                    // number of messages 
			(LPDWORD) NULL);              // no read time-out 

		if (!fResult) 
		{ 
			LogMessage("GetMailslotInfo failed (%d)\n", GetLastError());
			return; 
		} 
	}
}

MailManager::MailManager()
{
	m_hSlot = CreateMailslot(g_InputSlot, 
		0,                             // no maximum message size 
		MAILSLOT_WAIT_FOREVER,         // no time-out for operations 
		(LPSECURITY_ATTRIBUTES) NULL); // default security

	if (m_hSlot == INVALID_HANDLE_VALUE) 
	{ 
		LogMessage("CreateMailslot failed with %d\n", GetLastError()); 
	} 
	else 
	{
		//LogMessage("Mailslot created successfully.\n"); 
	}

	m_hEvent = CreateEvent(NULL, FALSE, FALSE, g_EventName);

    if( m_hEvent == NULL )
	{
		LogMessage("CreateEvent failed with %d.\n", GetLastError()); 
	}

	m_SendSlotInit = false;
	m_hFile = 0;
}

void MailManager::ResetMailSystem()
{
	if (m_hFile)  CloseHandle(m_hFile);
	if (m_hSlot)  CloseHandle(m_hSlot);
	//if (m_hEvent) CloseHandle(m_hEvent);

	m_hSlot = CreateMailslot(g_InputSlot, 
		0,                             // no maximum message size 
		MAILSLOT_WAIT_FOREVER,         // no time-out for operations 
		(LPSECURITY_ATTRIBUTES) NULL); // default security

	if (m_hSlot == INVALID_HANDLE_VALUE) 
	{ 
		LogMessage("CreateMailslot failed with %d\n", GetLastError()); 
	} 
	else 
	{
		LogMessage("Mailslot created successfully.\n"); 
	}

	/*m_hEvent = CreateEvent(NULL, FALSE, FALSE, g_EventName);

    if( m_hEvent == NULL )
	{
		LogMessage("CreateEvent failed with %d.\n", GetLastError()); 
	}*/

	m_SendSlotInit = false;
}

MailManager::~MailManager()
{
	if (m_hFile)  CloseHandle(m_hFile);
	if (m_hSlot)  CloseHandle(m_hSlot);
	if (m_hEvent) CloseHandle(m_hEvent);
}

void MailManager::SetUpSendSlot()
{
	m_hFile = CreateFile(g_OutputSlot,
		GENERIC_WRITE,
		FILE_SHARE_READ,
		(LPSECURITY_ATTRIBUTES) NULL,
		OPEN_EXISTING,
		FILE_ATTRIBUTE_NORMAL,
		(HANDLE) NULL);

	if (m_hFile == INVALID_HANDLE_VALUE)
	{
		LogMessage("CreateFile failed with %d.\n", GetLastError());
	}

	m_SendSlotInit = true;
}

#else // !WIN32 — Phase J POSIX (AF_UNIX SOCK_DGRAM) implementation
// ---------------------------------------------------------------------------
// On Linux the mailslot transport is replaced with AF_UNIX SOCK_DGRAM
// (see server/compat/posix_ipc.h). m_hSlot / m_hFile / m_hEvent member
// HANDLEs are unused; we keep a heap-allocated PosixIpc behind m_hSlot
// (cast through void*) so the class shape stays binary-compatible with
// the Win32 build's header.
// ---------------------------------------------------------------------------
#include "../compat/posix_ipc.h"

#include <cstring>
#include <new>

namespace {
inline net7ipc::PosixIpc * AsIpc(HANDLE h) {
    return reinterpret_cast<net7ipc::PosixIpc *>(h);
}
}

bool MailManager::WriteMessage(char *message)
{
    auto *ipc = AsIpc(m_hSlot);
    if (!ipc) return false;
    return ipc->WriteMessage(message);
}

void MailManager::CheckMessages()
{
    auto *ipc = AsIpc(m_hSlot);
    if (!ipc) return;

    int got;
    while ((got = ipc->ReadMessage(reinterpret_cast<char *>(m_Buffer),
                                   sizeof(m_Buffer))) > 0)
    {
        // Mailslot wire format (Win32): opcode byte at [0], slot at [2],
        // length at [4], payload at [6]. The only message actually sent
        // by the existing codebase is the literal "Ping" keepalive
        // (login -> server) and "Pong" / silence in return. Both sides'
        // HandleMessage() reduces to "update g_receive_time"; we honour
        // that by delegating to the same hook.
        HandleMessage();
    }
}

MailManager::MailManager()
    : m_hSlot(0), m_hFile(0), m_hEvent(0), m_SendSlotInit(false)
{
    std::memset(m_Buffer, 0, sizeof(m_Buffer));

    auto *ipc = new (std::nothrow)
        net7ipc::PosixIpc(g_InputSlot, g_OutputSlot);
    if (!ipc || !ipc->valid())
    {
        LogMessage("MailManager: PosixIpc bind('%s') failed; IPC disabled\n",
                   g_InputSlot ? g_InputSlot : "<null>");
        delete ipc;
        return;
    }

    LogMessage("MailManager: AF_UNIX recv=%s send=%s\n",
               ipc->recv_path().c_str(), ipc->send_path().c_str());
    m_hSlot = reinterpret_cast<HANDLE>(ipc);
    m_SendSlotInit = true; // PosixIpc opens the send fd lazily
}

void MailManager::ResetMailSystem()
{
    auto *ipc = AsIpc(m_hSlot);
    if (ipc) ipc->Reset();
}

MailManager::~MailManager()
{
    delete AsIpc(m_hSlot);
    m_hSlot = 0;
}

void MailManager::SetUpSendSlot()
{
    // No-op on Linux: PosixIpc::WriteMessage opens the send socket on
    // demand and reuses it. Kept for header symmetry with Win32.
}

#endif // WIN32
