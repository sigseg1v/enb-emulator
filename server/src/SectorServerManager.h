// SectorServerManager.h
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

#ifndef _SECTOR_SERVER_MANAGER_H_INCLUDED_
#define _SECTOR_SERVER_MANAGER_H_INCLUDED_

#include "Mutex.h"

#define MAX_SECTOR_SERVERS			        300
#define MAX_SECTORS					        300
#define MAX_SECTOR_CONNECTIONS_PER_SERVER   300

class ServerManager;
struct ServerRedirect;
class UDP_Connection;

class SectorServerManager
{
public:
	SectorServerManager();
	virtual ~SectorServerManager();

public:
	bool	RegisterSectorServer(unsigned long ip_address, short port_number, short max_sectors, char *username);
	bool	CheckConnections();
	void	SetServerManager(ServerManager * server_mgr);
	char  * GetSystemName(long sector_id);
	char  * GetSectorName(long sector_id);
	bool	LookupSectorServer(ServerRedirect & redirect);
	bool	SendPacket(long sector_id, short opcode, unsigned char *data=NULL, size_t length=0);
	void	AddSector(long sector_id, char *sector_name, char *system_name, char *parent_sector_name);
	long	GetNextSectorID(long sector_id);
	void	SectorLockdown()	{ m_ServerLockdown = true; }

private:
	// Linked list of "registered" sector servers
	struct	SectorServer;
	struct	SectorServer
	{
		unsigned long	ip_address;
		short			port;
        short           max_sectors;
		char		  * username;
		//char		  * toon;
		//char		  * email;
		//char		  * server_name;
		long			countdown_to_ready;
		UDP_Connection* udp_connection[MAX_SECTOR_CONNECTIONS_PER_SERVER];
        long            sector_assigned[MAX_SECTOR_CONNECTIONS_PER_SERVER];
		struct SectorServer * next;
	};

private:
	void	LoadSectorList();
	bool	LoadSectorServers();
	bool	ConnectBackToSectorServer(SectorServer *server);
	bool	AssignSectorToAvailableServer(long sector_id, char * sector_name);

private:
    Mutex   m_Mutex;
	ServerManager * m_ServerMgr;
	bool	m_ServerLockdown;

	// sector servers
	long	m_NumSectorServers;
	SectorServer * m_ServerList;

	// sectors
	long	m_NumSectors;
	long	m_NumUnassignedSectors;
	long	m_SectorID[MAX_SECTORS];
	char  *	m_SectorName[MAX_SECTORS];
	char  *	m_SystemName[MAX_SECTORS];
	char  *	m_ParentSectorName[MAX_SECTORS];
	bool	m_SectorAssigned[MAX_SECTORS];
	//short	m_SectorPort[MAX_SECTORS];
	//long	m_SectorAddr[MAX_SECTORS];
};

#endif // _SECTOR_SERVER_MANAGER_H_INCLUDED_

