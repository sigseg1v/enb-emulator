// SSL_Connection.h
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

#ifndef _SSL_CONNECTION_H_INCLUDED_
#define _SSL_CONNECTION_H_INCLUDED_

#include "Mutex.h"
#include "openssl/ssl.h"

// WARNING: This limits the size of images on the secure web pages.
#define MAX_SERVER_FILE_SIZE    16384
#define USERNAME_TAG            "username="
#define PASSWORD_TAG            "password="
#define PORT_TAG                "port="
#define MAX_SECTORS_TAG         "max_sectors="
#define VERSION_TAG             "version="
#define FORUM_NAME_TAG          "forumname="
#define EMAIL_TAG               "email="
#define LKEY_TAG                "lkey="

#define SSL_RECV_BUFFER			2048
class ServerManager;

class SSL_Connection
{
public:
	SSL_Connection();
	virtual ~SSL_Connection();

public:
	SSL_Connection * ReSetSSL_Connection(SOCKET s, ServerManager *server_mgr, unsigned long *ip_addr, SSL_CTX *ssl_context);
	void KillConnection(bool AbortSSL = false);

	void    RunThread();

	void	SSL_ConnectionThread();
	void	SSL_ThreadLockCheck(u32 tick);

	bool	IsActive();

	long	InactivityTime()		{ return m_InactivityTimer;  }
	void	SetInactivityTime(long secs)	{ m_MaxInactivityTime = secs; }

	long	GameID()			{ return m_AvatarID; } //for <MemorySlot> template access
	void	SetGameID(long id)		{ m_Mutex.Lock(); m_AvatarID = id; m_Mutex.Unlock(); }
	u32	CheckLastThreadTime()		{ return m_LastThreadComms; }
//	void	TerminateSSLThread(); -- OBSOLETE

private:
	bool	SocketReady(int ttimeout);
	void	WakeupThread();
	
	char  * GetResponse(char *request, size_t *response_length);
	char  *	HttpResult(size_t *response_length, char *data, char *content_type="text/html", size_t length=0);
	char  *	AuthLogin(size_t *response_length, char *recv_buffer);
	char  *	TouchSession(size_t *response_length, char *recv_buffer);
	char  *	SectorServer(size_t *response_length, char *recv_buffer);

	static void	*SSLRecvThread(void *param);

	bool		OpenSSL_Link();

private:
	Mutex		m_Mutex;
	SOCKET		m_Socket;
	long		m_IpAddress;
	ServerManager	*m_ServerMgr;
	pthread_t	m_RecvThreadHandle;
	pthread_cond_t	m_RecvThreadCond;
	pthread_mutex_t	m_RecvThreadMtx;		// Note: this is NOT m_Mutex!
	bool		m_SslConnectionThreadRunning;
	bool		m_SslConnectionThreadTerminated;
	bool		m_ConnectionActive;
	bool		m_ServerShutdown;
	char		m_Buffer[MAX_SERVER_FILE_SIZE];
	char		m_Recv_Buffer[SSL_RECV_BUFFER];
	u32		m_LastThreadComms;
	long		m_AvatarID;
	SSL		*m_SSL;

	long		m_InactivityTimer;		// Inactivity on this connection, in seconds
	long		m_MaxInactivityTime;		// Maximum inactivity on this connection (0 is Infinite)

};

#endif // _SSL_CONNECTION_H_INCLUDED_
