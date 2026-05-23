// SSL_Listener.h
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

#ifndef _SSL_LISTENER_H_INCLUDED_
#define _SSL_LISTENER_H_INCLUDED_

#include "Mutex.h"
#include "openssl/ssl.h"

class ServerManager;

class SSL_Listener
{
public:
	SSL_Listener(unsigned long ip_address, unsigned short port, ServerManager &server_mgr);
	virtual ~SSL_Listener();

public:
	void		RunThread();
	void		Shutdown();

	bool		ListenerThreadRunning()	{ return m_SslListenerThreadRunning; }

private:
	bool		SocketReady(int ttimeout);
	Mutex		m_Mutex;
	WORD		m_TcpPort;
	unsigned long	m_IpAddress;
	ServerManager	&m_ServerMgr;
	SOCKET		m_ListenerSocket;
	bool		m_SslListenerThreadRunning;

	SSL_CTX 	*m_ssl_context;

#ifndef WIN32
	pthread_t m_Thread;
#endif
};

#endif // _SSL_LISTENER_H_INCLUDED_

