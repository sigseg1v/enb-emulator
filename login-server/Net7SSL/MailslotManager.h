#ifndef _MAILSLOTMANAGER_SSL_H_INCLUDED_
#define _MAILSLOTMANAGER_SSL_H_INCLUDED_

// Server-native build is Linux-only — the AF_UNIX SOCK_DGRAM IPC bus
// replaced the Win32 mailslot transport in Phase J / M.
extern const char *g_OutputSlot;
extern const char *g_InputSlot;
extern const char *g_EventName;

namespace net7ipc { class PosixIpc; }

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
	net7ipc::PosixIpc *m_Ipc;
	unsigned char      m_Buffer[1024];
};

#define LOCAL_PING_SSL_SERVER			0x04 //keepalive ping from ssl to server
#define LOCAL_PING_SERVER_SSL			0x05 //keepalive ping from server to ssl

#endif //_MAILSLOTMANAGER_SSL_H_INCLUDED_
