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
#ifndef NET7_MAILSLOT_MANAGER_H_
#define NET7_MAILSLOT_MANAGER_H_

#include "Net7.h"
#include <stdio.h>

// Server-native build is Linux-only — the AF_UNIX SOCK_DGRAM IPC bus
// replaced the Win32 mailslot transport in Phase J / M. These globals
// are the per-app socket paths under /run/net7-ipc/.
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

#endif // NET7_MAILSLOT_MANAGER_H_
