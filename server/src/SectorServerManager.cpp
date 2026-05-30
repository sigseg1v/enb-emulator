// SectorServerManager.cpp
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
** Copyright of our assets/code/software began in 2005-2009 �, Net-7 Entertainment.
**
*/

#include "Net7.h"
#include "SectorServerManager.h"
#include "UDPConnection.h"
#include "ServerManager.h"

SectorServerManager::SectorServerManager()
{
	// Sector servers
	m_NumSectorServers = 0;

	// Sectors
	m_NumSectors = 0;
	m_NumUnassignedSectors = 0;
	memset(m_SectorAssigned, 0, sizeof(m_SectorAssigned));
    memset(m_SectorName, 0, sizeof(m_SectorName));
    memset(m_SystemName, 0, sizeof(m_SystemName));
    memset(m_ParentSectorName, 0, sizeof(m_ParentSectorName));

	m_ServerMgr = NULL;
	m_ServerList = NULL;

	//LoadSectorServers();
}

SectorServerManager::~SectorServerManager()
{
	// Kill all TCP/IP connections and destroy the objects
	SectorServer * p = m_ServerList;
	SectorServer * next = NULL;
	while (p)
	{
		next = p->next;
		if (p->username)
		{
			delete [] p->username; // new'd so needs delete
		}
        for (short i=0; i < p->max_sectors; i++)
        {
            if (p->udp_connection[i])
            {
        		delete p->udp_connection[i];
            }
        }
		delete p;
		p = next;
	}

	for (long i=0; i < m_NumSectors; i++)
	{
		if (m_SectorName[i])
		{
			delete [] m_SectorName[i];
		}
		if (m_SystemName[i])
		{
			delete [] m_SystemName[i];
		}
		if (m_ParentSectorName[i])
		{
			delete [] m_ParentSectorName[i];
		}
	}
}

bool SectorServerManager::LookupSectorServer(ServerRedirect & redirect)
{
	bool success = false;
	long sector_id = (long)ntohl(redirect.sector_id);

	SectorManager *sector_manager = g_ServerMgr->GetSectorManager(sector_id);

	if (sector_manager)
	{
		short port = sector_manager->GetPort();
		// Defense in depth: SectorManager::m_Port is initialised to -1 and
		// only set to a real value once StartListener() has bound the UDP
		// socket. Returning success=true with port=-1 here used to feed a
		// sentinel into MASTER_HANDOFF_CONFIRM (0x2009) and hang the client
		// at the loading screen. The primary fix is to defer the master
		// listener's recv thread until sectors are bound (see
		// ServerManager::Run), but if any future code path manages to look
		// up an unbound sector we want a clean failure here instead.
		if (port <= 0)
		{
			LogMessage("!!! LookupSectorServer sector_id=%d: manager exists but port=%d (not yet bound); refusing handoff.\n",
				sector_id, (int) port);
			return false;
		}
		redirect.ip_address = sector_manager->GetIPAddr();
		redirect.port = port;
		success = true;
	}
	else
	{
		LogMessage("!!! Unable to find sector to which we've been handed!! [%d]\n", sector_id);
	}

	return (success);
}

void SectorServerManager::AddSector(long sector_id, char *sector_name, char *system_name, char *parent_sector_name)
{
	m_SectorID[m_NumSectors] = sector_id;

    m_SectorName[m_NumSectors] = new char[strlen(sector_name) + 1];
	strcpy_s(m_SectorName[m_NumSectors], strlen(sector_name) + 1, sector_name);
	m_SectorName[m_NumSectors][strlen(sector_name)] = '\0';

	m_SystemName[m_NumSectors] = new char[strlen(system_name) + 1];
	strcpy_s(m_SystemName[m_NumSectors], strlen(system_name) + 1, system_name);
	m_SystemName[m_NumSectors][strlen(system_name)] = '\0';

    if (parent_sector_name)
    {
	    m_ParentSectorName[m_NumSectors] = new char[strlen(parent_sector_name) + 1];
	    strcpy_s(m_ParentSectorName[m_NumSectors], strlen(parent_sector_name) + 1, parent_sector_name);
		m_ParentSectorName[m_NumSectors][strlen(parent_sector_name)] = '\0';
    }
    else
    {
        m_ParentSectorName[m_NumSectors] = NULL;
    }

    m_SectorAssigned[m_NumSectors] = false;
	m_NumSectors++;
	m_NumUnassignedSectors++;
}

void SectorServerManager::SetServerManager(ServerManager * server_mgr)
{
	m_ServerMgr = server_mgr;
}

bool SectorServerManager::RegisterSectorServer(unsigned long ip_address, short port_number, short max_sectors, char *username)
{
	bool success = false;
	unsigned char * ip = (unsigned char *) &ip_address;
	LogMessage("RegisterSectorServer at IP address %d.%d.%d.%d, port %d\n",
		ip[0], ip[1], ip[2], ip[3], port_number);

	// To register a sector server, the IP address must be in the
	// list of authorized servers.  This list may be updated by
	// the user via a Web Browser using the same IP address as the
	// server.

	// Don't permit any changes once the server is actually running
	if (m_ServerLockdown) return true;

	// Scan through the linked list
	SectorServer * server = m_ServerList;
	while (server)
	{
		if ((server->ip_address == ip_address) &&
			/*(server->port == port_number) &&*/
			(strcmp(server->username, username) == 0))
		{
            if (server->max_sectors != max_sectors)
            {
                server->max_sectors = max_sectors;
            }
			//LogMessage("Sector Server authenticated\n");
			//success = ConnectBackToSectorServer(server);
            success = true;
			break;
		}
		server = server->next;
	}

	return success;
}

bool SectorServerManager::CheckConnections()
{
	bool assignments_complete = true;
    // Called by the Main thread in the Main Loop

	// Assign a sector to an available server if we have any unassigned sectors
	// Scan through the linked list
	// Do we have any available servers?
	if (m_NumUnassignedSectors > 0)
	{
		for (long i=0; i < m_NumSectors; i++)
		{
			if (!m_SectorAssigned[i])
			{
                if  (m_SectorID[i] >= 973)
                {
				    if (AssignSectorToAvailableServer(m_SectorID[i], m_SectorName[i]))
				    {
						//LogMessage("Assigned ID: %d out of %d\n", i, m_NumUnassignedSectors);
					    m_SectorAssigned[i] = true;
    					m_NumUnassignedSectors--;
                        usleep(100 * 1000); // wait 100 ms between assignments 
						assignments_complete = false;
                        break;
                    } 
                    else 
                    {
						//LogMessage("Cant assign Sector: %d to server\n", m_SectorID[i]);
					}
                }
			}
		}
	}

	return assignments_complete;
}

bool SectorServerManager::AssignSectorToAvailableServer(long sector_id, char *sector_name)
{
	//LogMessage("Looking for an available server for sector %d (%s)\n", sector_id, sector_name);
	// Loop through the list of servers to find one that is available
	for (long i=0; i < m_NumSectors; i++)
	{
		if (!m_SectorAssigned[i] && m_SectorID[i] >= 970)
		{
			g_ServerMgr->m_UDPMasterConnection->ValidateSectorServer(sector_id);
			return true;
		}
	}

	return false;
}

//Unused code
bool SectorServerManager::LoadSectorServers()
{
	bool success = false;
	// Record format example:
	//
	// 192.168.1.101,3500,18,VectoR,VectoR.360,Vector.360@gmail.com,VectoR's multi-player sector server
	// 
    // Fields:
    //	- IP Address
    //	- First Port
    //  - Max Number of Sectors
    //	- Username
    //  - Toon name to be listed in the credits
    //	- Email Address,
    //	- Server Description

	// Read the list of usernames and passwords from accounts.txt
	SectorServer * last = NULL;
	char buffer[256];
	char filename[MAX_PATH];
	char *next_token = NULL;
	sprintf_s(filename, sizeof(filename), "%ssector_servers.txt", SERVER_DATABASE_PATH);
	FILE *f;
	fopen_s(&f, filename, "r");
	if (f)
	{
		while (!feof(f))
		{
			if (fgets(buffer, sizeof(buffer), f))
			{
                // strip off any trailing cr/lf
				strtok_s(buffer, "\r\n", &next_token);
				// ignore blank lines and records starting with a semicolon
                char c = buffer[0];
				if ((c != ';') && (c != 0))
				{
                    strcat_s(buffer, sizeof(buffer), ",,,,");
					char *ip_address = strtok_s(buffer, ",", &next_token);
					char *port = strtok_s(NULL, ",", &next_token);
					char *max_sectors = strtok_s(NULL, ",", &next_token);
					char *username = strtok_s(NULL, ",", &next_token);
					char *toon = strtok_s(NULL, ",", &next_token);
					char *email = strtok_s(NULL, ",", &next_token);
					char *description = strtok_s(NULL, ",", &next_token);
					if (ip_address && port && max_sectors && username)
					{
						// Create a new entry to add to the linked list
						SectorServer * server = new SectorServer;
                        memset(server, 0, sizeof(SectorServer));
						server->ip_address = inet_addr(ip_address);
						server->port = atoi(port) + 1;
                        server->max_sectors = atoi(max_sectors);
						server->username = new char[strlen(username) + 1];
						strcpy_s(server->username, strlen(username) + 1, username);
						server->username[strlen(username)] = '\0';
						server->countdown_to_ready = 0;
						server->next = NULL;

						// Add this server the end of the linked list
						if (last)
						{
							last->next = server;
						}
						else
						{
							m_ServerList = server;
						}
						last = server;
						m_NumSectorServers++;
					}
				}
			}
		}
	}

	return success;
}

/*bool SectorServerManager::SendPacket(long sector_id, short opcode, unsigned char *data, size_t length)
{
	SectorServer * server = m_ServerList;
	while (server)
	{
        for (short i=0; i < server->max_sectors; i++)
        {
		    // Is this the correct server?
		    if ((server->sector_assigned[i] == sector_id) &&
			    (server->udp_connection[i]))
		    {
                LogMessage("SendPacket -- bad!\n");
			    //server->udp_connection[i]->SendResponse(opcode, data, length);
			    return true;
		    }
        }
		server = server->next;
	}

	return false;
}*/

