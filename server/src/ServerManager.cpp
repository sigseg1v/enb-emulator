// ServerManager.cpp
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

#include "Net7.h"
#include "ServerManager.h"
#include "UDPConnection.h"
#include "MessageQueue.h"
#include <openssl/ssl.h>
#include "SectorContentParser.h"
#include "ItemBase.h"
#include "StationLoader.h"
#include "CBAssetParser.h"
#include "SaveManager.h"
#include "MailslotManager.h"
#include "ObjectManager.h"
#include <net7/Opcodes.h>

// Constructor
ServerManager::ServerManager(bool is_master_server, unsigned long ip_address, short port, short max_sectors, bool standalone, unsigned long internal_ip_address)
	:
	m_IsMasterServer(is_master_server),
	m_IpAddress(ip_address),
	m_IpAddressInternal(internal_ip_address),
	m_Port(port),
    m_MaxSectors(max_sectors),
    m_IsStandaloneServer(standalone)
{
    m_SectorServerMgr.SetServerManager(this);
    m_LogFileTimer = 0;
    m_LogFile = (0);
    m_ChatFileTimer = 0;
	m_MySQLFileTimer = 0;
    m_ChatFile = (0);
	m_MySQLFile = (0);
	m_AllowCreate = false;
	m_DumpXML = false;
    m_SkillList = (0);
	m_SectorUpdateSelect = false;
	m_SectorCount = 0;
	m_UDPConnection = NULL;
	m_UDPMasterConnection = NULL;
	m_UDPGlobalConnection = NULL;

	g_ServerMgr = this;

	//now allocate the global circular buffers, one for TCP player send and one for UDP player send
	long buffer_space = 0x80000 * (MAX_ONLINE_PLAYERS / 50); //reserve 1/2 meg per 50 players
	long buffer_slots = 20000 * (MAX_ONLINE_PLAYERS / 50); //reserve 20000 slots per 50 players
	m_UDPSendBuffer = new CircularBuffer(buffer_space, buffer_slots, true);		//used for storing individual opcode/data elements
	m_ReSendBuffer = new CircularBuffer(buffer_space, buffer_slots, false);		//used for storing fully formed packets in case anything needs to be re-sent
																				//   we don't want to check this for overloading since new packets will eventually
																				//   displace old ones, way after they become obsolete.
	m_MessageBuffer = new CircularBuffer(buffer_space, buffer_slots, true);		//used for storing player inputs until they're processed.

    m_StringMgr = new StringManager();
    g_StringMgr = m_StringMgr;

    m_GlobMemMgr = new GMemoryHandler(MAX_ONLINE_PLAYERS);
    g_GlobMemMgr = m_GlobMemMgr;

    m_AccountMgr = new AccountManager();
    g_AccountMgr = m_AccountMgr;

	m_SaveMgr = new SaveManager();
	g_SaveMgr = m_SaveMgr;

    g_PlayerMgr = &m_PlayerMgr;
    g_ItemBaseMgr = &m_ItemBaseMgr;

	m_JobMgr = new JobManager();

	m_SectorAssignmentsComplete = false;
	
	//m_Connections = new MemorySlot<Connection>(MAX_ONLINE_PLAYERS);

	//g_cumulative_mem += sizeof(Connection)*MAX_ONLINE_PLAYERS;

	m_SectorEffectID = 0;
	LogMessage("Players and thread queue allocation: %d Mb\n", g_cumulative_mem/(1024*1024));

	m_Halloween = false;

	m_LastPlayerCount = 0;

	memset(m_JobCatCount, 0, sizeof(m_JobCatCount));
}

// Destructor
ServerManager::~ServerManager()
{
    // TODO: The server manager must wait for all threads to die before destructing!!!
    // The PlayerManager takes a while to save all Player information to disk!!!

	delete m_SaveMgr; // get saves out of the way first
	for (int i = 0; i < m_MaxSectors; i++)
		delete m_SectorMgrList[i];
    delete m_AccountMgr;
	delete g_MailMgr;
	//delete m_Connections;
	delete m_UDPSendBuffer;
	delete m_ReSendBuffer;
	delete m_MessageBuffer;
    delete m_StringMgr;
	delete m_GlobMemMgr;
}

// This is the entry point for running the server
void ServerManager::RunServer()
{
	if (m_IsMasterServer || m_IsStandaloneServer)
	{
        // This is a Master Server or a Standalone Server
		RunMasterServer();
	}
	else
	{
        // This is a Sector Server
		RunSectorServer();
	}
}

void ServerManager::RunMasterServer()
{
	//UDP_Connection udp_global_server_listener(UDP_GLOBAL_SERVER_PORT, this, CONNECTION_TYPE_GLOBAL_SERVER_TO_PROXY);

	// Instantiate the SSL Listener object
	// NB: This is now handled in Net7SSL
	// m_SSL_listener = new SSL_Listener(m_IpAddressInternal, SSL_PORT, *this);
	// RegisterSectorServer(SECTOR_SERVER_PORT, m_MaxSectors);

	// Instantiate the TCP Listener object for the Global Server

	// TcpListener global_server_listener(m_IpAddressInternal, GLOBAL_SERVER_PORT, *this, CONNECTION_TYPE_CLIENT_TO_GLOBAL_SERVER);
	
	// This is all the old pre-net7proxy stuff
	//  Instantiate the TCP Listener object for the Global Server
	//  TcpListener global_server_listener(m_IpAddressInternal, GLOBAL_SERVER_PORT, *this, CONNECTION_TYPE_CLIENT_TO_GLOBAL_SERVER);
    //  UdpListener global_server_listener(m_IpAddressInternal, UDP_GLOBAL_SERVER_PORT, *this, CONNECTION_TYPE_CLIENT_TO_GLOBAL_SERVER);
    //  UDP_Connection global_server_listener(UDP_GLOBAL_SERVER_PORT, this, CONNECTION_TYPE_GLOBAL_SERVER_TO_PROXY);
    //  m_GlobalConnection = &global_server_listener;
	// end pre-net7proxy stuff

	// Instantiate the TCP Listener object for the Master (galaxy) Server
	//TcpListener proxy_tcp_listener(m_IpAddressInternal, PROXY_SERVER_PORT, *this, CONNECTION_TYPE_SECTOR_SERVER_TO_PROXY);
    //UdpListener master_tcp_listener(m_IpAddressInternal, MASTER_SERVER_PORT, *this, CONNECTION_TYPE_CLIENT_TO_MASTER_SERVER);
    // The master listener OBJECT is constructed early so other code paths
    // (notably SectorServerManager::AssignSectorToAvailableServer) can call
    // ValidateSectorServer() on m_UDPMasterConnection during the sector-
    // assignment loop below. The RECEIVER THREAD is deferred (see
    // StartReceiver() call after the assignment wait): if it starts now, the
    // proxy's MASTER_HANDOFF (0x2008) packets get serviced before any
    // sector's UDP port has been bound, so ProcessHandoff returns the
    // sentinel port=-1 in MASTER_HANDOFF_CONFIRM (0x2009), the proxy parses
    // that as "no sector", falls back to a proxy-local ServerRedirect, and
    // the client hangs at the loading screen.
    UDP_Connection master_udp_listener(UDP_MASTER_SERVER_PORT, this, CONNECTION_TYPE_MASTER_SERVER_TO_PROXY);
    m_UDPMasterConnection = &master_udp_listener;

    // Phase K (2026-05-24): proxy<->server "global" control plane. Was TCP in
    // the kyp-era Win32 build via SSL_LOCALCERT_LOGIN_PORT; Phase Q deleted
    // the server-side TCP cluster so we bind UDP_GLOBAL_SERVER_PORT here and
    // the proxy gets a second UDPClient pointed at it. Server dispatcher
    // routes CONNECTION_TYPE_GLOBAL_SERVER_TO_PROXY into HandleGlobalOpcode
    // (server/src/UDPConnection.cpp:203, handlers in UDP_Global.cpp). The
    // global plane carries pre-sector account/ticket traffic, so its
    // receiver thread starts immediately -- only the master plane needs the
    // deferred-start guard.
    UDP_Connection global_udp_listener(UDP_GLOBAL_SERVER_PORT, this, CONNECTION_TYPE_GLOBAL_SERVER_TO_PROXY);
    m_UDPGlobalConnection = &global_udp_listener;
    global_udp_listener.StartReceiver();

	g_MailMgr = new MailManager();

    //TcpListener sector_comms(m_IpAddressInternal, SECTOR_SERVER_PORT, *this, CONNECTION_TYPE_CLIENT_TO_SECTOR_SERVER);
    //RegisterSectorServer(SECTOR_SERVER_PORT, m_MaxSectors);

	SkillParser	SkillLoad;

	// -------------------  This logs everyone out incase of a crash -----------------

	sql_connection_c connection( "net7_user", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
	sql_query_c MissionTable( &connection );
    sql_result_c result;
	sql_result_c *mission_result = &result;

	char timestr[32];
	time_t rawtime;
	struct tm	* gmttime = NULL;
	time(&rawtime);
	gmttime = gmtime(&rawtime);
	strftime(timestr, sizeof(timestr), "%Y/%m/%d %H:%M:%S", gmttime);

    // Phase N: the original `CALL net7_user.logoutOnShutdown(theTime)` stored
    // procedure body is two UPDATEs against `accounts` and `avatar_info`
    // setting last_logout for rows where last_login > last_logout
    // (db/postgres/seed.sql:759). Postgres has no equivalent procedure (the
    // MySQL DELIMITER block doesn't load), so we inline both statements.
    MissionTable.AddParam(timestr);
    MissionTable.execute_params( "UPDATE accounts SET last_logout = ? WHERE last_login > last_logout" );
    MissionTable.AddParam(timestr);
    MissionTable.execute_params( "UPDATE avatar_info SET last_logout = ? WHERE last_login > last_logout" );

	// ------------------------------------

	m_SkillsList.LoadSkillsContent();
	m_BuffData.LoadBuffContent();
    m_MOBList.LoadMOBContent();
	m_AssetList.LoadAssetContent();
	m_FactionData.LoadFactions();

	if(!m_CBassetList.ParseRadii())
	{
		//Error - Couldn't parse cbasset.xml
		LogMessage("Error - Could not parse \'cbasset.xml\'.\n");
	}

	m_ItemBaseMgr.Initialize();
	m_SectorContent.LoadSectorContent();

	// Load Stations from MySQL
	m_StationMgr.LoadStations();
 	
	SkillLoad.LoadSkills();
	m_SkillList = SkillLoad.GetSkillList();
	//m_SkillAbilities = SkillLoad.GetAbilityList();

	m_Missions.LoadMissionContent();
	
	m_PlayerMgr.LoadGuildsFromSQL();
	m_JobMgr->InitialiseJobs();

	//TODO: for distributed server, set up ports & ip here
	if (m_IsStandaloneServer)
	{
		int i;
		for (i = 0; i < m_MaxSectors; i++)
		{
			//short port = m_Port + i;
			//LogMessage("Starting listener on port %d\n", port);
			m_SectorMgrList[i] = new SectorManager(this);
			//m_SectorMgrList[i]->SetSectorData(m_SectorContent.GetSec);
			//m_SectorMgrList[i]->StartListener(port);
			m_SectorMgrList[i]->SetBoundaries(i);
			m_SectorMgrList[i]->SetSectorNumber(i);
			m_SectorMgrList[i]->SetIPAddr(m_IpAddress);
		}

		//m_SectorCount = i;

		// Drive sector assignment to completion before returning to MainLoop.
		// CheckConnections assigns one sector per ServerCheck tick (with an
		// internal usleep(100ms)), and BeginSectorThread -- which binds each
		// sector's UDP port via StartListener -- only fires once, in the same
		// tick that m_SectorAssignmentsComplete first flips to true. Until
		// then, GetSectorManager(id) for an assigned-but-not-yet-bound sector
		// returns a manager whose m_Port is still the -1 initializer value,
		// and UDP_Master::ProcessHandoff would happily emit that -1 in the
		// 0x2009 MASTER_HANDOFF_CONFIRM reply. The master listener's recv
		// thread is therefore NOT started yet (see StartReceiver() call
		// below) -- the master_udp_listener constructor only bound the
		// socket so SectorServerManager::AssignSectorToAvailableServer can
		// still call ValidateSectorServer() through m_UDPMasterConnection
		// during this wait.
		while (!m_SectorAssignmentsComplete && !g_ServerShutdown)
		{
			usleep(50 * 1000);
			ServerCheck();
		}

		// Sectors are bound; safe to begin dispatching master-plane
		// MASTER_HANDOFF (0x2008) packets -- ProcessHandoff will now find
		// real sector ports instead of -1.
		master_udp_listener.StartReceiver();

		LogMessage("Registering sector server: port=%d, max_sectors=%d\n", m_Port, m_MaxSectors);
		//RegisterSectorServer(m_Port, m_MaxSectors);
		//RegisterSectorServer(GLOBAL_SERVER_PORT, m_MaxSectors);

		m_SectorServerMgr.SectorLockdown();

		MainLoop();

		for (i = 0; i < m_SectorCount; i++)
		{
			delete m_SectorMgrList[i];
			m_SectorMgrList[i] = NULL;
		}
    }
    else
    {
		m_SectorServerMgr.SectorLockdown();
    	MainLoop();
    }

	//m_SSL_listener->Shutdown();
	//global_server_listener.Shutdown();
	//udp_global_server_listener.Shutdown();
	master_udp_listener.Shutdown();
}


long ServerManager::GetNextEffectID()
{
    if (m_SectorEffectID == 0 || m_SectorEffectID > 0x0FFFFFFF)
    {
        m_SectorEffectID = m_SectorEffectID;
    }

    return m_SectorEffectID++;
}

// This runs a single sector server on a single port
void ServerManager::RunSectorServer()
{
    SectorContentParser parser;
    if (!parser.LoadSectorContent())
    {
        printf("Fatal error loading sector content from Database. Program aborted.\n");
    }
    else
    {
        // Start a sector manager for each sector
        int i;
		short port = m_Port;
        for (i = 0; i < m_MaxSectors; i++)
        {
            //LogMessage("Launching SectorManager on port %d\n", port);
            m_SectorMgrList[i] = new SectorManager(this);
            //m_SectorMgrList[i]->SetSectorData(parser.GetSectorData());
			// Find the next port that can be used
			while(m_SectorMgrList[i]->StartListener(port) == false)
			{
				port++;
			}
			m_SectorMgrList[i]->SetIPAddr(m_IpAddress); //use the server's IP addr for now
			port++;
        }

        // Wait 2 seconds for the listeners to start before registering
        for (i = 0; i < 40; i++)
        {
            // Loop 20x per second
            usleep(50 * 1000);
            ServerCheck();
        }

	    // Register this Sector Server with the Authentication Server
        LogMessage("Registering sector server with Authentication Server\n");
        //RegisterSectorServer(m_Port, m_MaxSectors);

	    MainLoop();
    }
}

FILE *OpenLogFile(FILE *logfile, char *name)
{
    // We have at least one message in the queue
    if (!logfile)
    {
        // If the log file is not open, then open it
        // Create log filename with the current date
        SYSTEMTIME systime;
        GetSystemTime(&systime);
        char filename[MAX_PATH];
        sprintf_s(filename, sizeof(filename), "%s_%04d_%02d_%02d.log", name, systime.wYear, systime.wMonth, systime.wDay);
        fopen_s(&logfile, filename, "a+");
    }
    return logfile;
}

void ServerManager::ServerCheck()
{
	u32 start_tick = GetNet7TickCount();

	g_MailMgr->CheckMessages();

	// run player group check updates
	if (m_SectorUpdateSelect)
	{
		m_PlayerMgr.RunMovementThread();
	}

	m_SectorUpdateSelect = !m_SectorUpdateSelect;

	if (!m_SectorAssignmentsComplete && (m_IsMasterServer || m_IsStandaloneServer))
	{
		m_SectorAssignmentsComplete = m_SectorServerMgr.CheckConnections();
		if (m_SectorAssignmentsComplete)
		{
			//start sector threads
			for (int i = 0; i < m_SectorCount; i++)
			{		
				m_SectorMgrList[i]->BeginSectorThread();
			}
		}
	}

	if (m_SectorUpdateSelect && (GetNet7TickCount() > (g_SSL_receive_time + 60000)))
	{
		LogMessage(" ---------------------------\n");
		LogMessage("Net7SSL seems to have stopped\n");
		LogMessage("Restart Net7SSL in progress.\n");
		LogMessage(" ---------------------------\n");
		g_MailMgr->ResetMailSystem();
		RelaunchNet7SSL();
	}

	if ((GetNet7TickCount() - start_tick) > 49)
	{
		//LogMessage("Strangely long server check - took %d ms\n", (GetNet7TickCount() - start_tick) );
	}

	if (start_tick > (m_LastPlayerCount + 5000))
	{
		m_LastPlayerCount = start_tick;
		m_UDPConnection->SendPlayerCount();
	}

    //===========================================
    // Check for messages in the Server Log queue
    //===========================================

    /*if (m_LogFileTimer)
    {
		// if the log file has been idle for 2 seconds, close it
        m_Mutex.Lock();
        m_LogFileTimer--;
        if (m_LogFileTimer == 0 && m_LogFile != NULL)
        {
            fclose(m_LogFile);  // close the log file
			m_LogFile = NULL;   // forget the file handle
        }
        m_Mutex.Unlock();
    }

    //===========================================
    // Check for messages in the Chat Msg queue
    //===========================================

    if (m_ChatFileTimer)
    {
		// if the chat file has been idle for 2 seconds, close it
        m_Mutex.Lock();
        m_ChatFileTimer--;
        if (m_ChatFileTimer == 0 && m_ChatFile != NULL)
        {
            fclose(m_ChatFile);  // close the chat file
			m_ChatFile = NULL;   // forget the file handle
        }
        m_Mutex.Unlock();
    }

    //===========================================
    // Check for messages in the Chat Msg queue
    //===========================================

    if (m_MySQLFileTimer)
    {
		// if the chat file has been idle for 2 seconds, close it
        m_Mutex.Lock();
        m_MySQLFileTimer--;
        if (m_MySQLFileTimer == 0 && m_MySQLFile != NULL)
        {
            fclose(m_MySQLFile);  // close the chat file
			m_MySQLFile = NULL;   // forget the file handle
        }
        m_Mutex.Unlock();
    }*/
}

void ServerManager::MainLoop()
{
    //LogMessage("Entering MainLoop\n");
    //m_Missions.Initialise();
	u32 check_tick;
	long sleep_time;
	while (!g_ServerShutdown)
	{
		check_tick = GetNet7TickCount();
		ServerCheck();
		sleep_time = (long)(50 - (GetNet7TickCount() - check_tick)); 
		if (sleep_time < 0) sleep_time = 0;
		usleep(sleep_time * 1000);
	}

	LogMessage("Server Shutting down ...\n");

	ServerCheck();
	ServerCheck(); //blip servercheck to clear any remaining messages to players

    if (m_LogFile)
    {
        fclose(m_LogFile);
		m_LogFile = NULL;
	}
	
	// TODO: Use event notification to make this safe
	// Wait for clean shutdown
	usleep(5000 * 1000);
}

void ServerManager::ReloadAllObjects()
{
	g_ResetContent = true;
	m_MOBList.LoadMOBContent();
	m_SectorContent.LoadSectorContent();
    for (int i = 0; i < m_MaxSectors; i++)
    {
        if (m_SectorMgrList[i])
		{
			ObjectManager * om = m_SectorMgrList[i]->GetObjectManager();
			if (om)
				om->InitialiseResourceContent();
		}
    }

	g_ResetContent = false;
}

void ServerManager::ReloadSectorObjects(long sector_id)
{
	g_ResetContent = true;
	SectorManager *sm = GetSectorManager(sector_id);
	if (sm)
	{
		ObjectManager * om = sm->GetObjectManager();
		if (om)
		{
			m_MOBList.LoadMOBContent(); 
			m_SectorContent.LoadSectorContent(sector_id);
			om->InitialiseResourceContent();
		}
	}
	g_ResetContent = false;
}

void ServerManager::SetSectorMap(long sector_id, SectorManager * sectormanager)
{
	m_SectorMap[sector_id] = sectormanager;
}

void ServerManager::SetObjectManMap(long sector_id, ObjectManager * objectmanager)
{
	m_ObjectManMap[sector_id] = objectmanager;
}

SectorManager *ServerManager::GetSectorManager(short port)
{
    for (int i = 0; i < m_MaxSectors; i++)
    {
        if (m_SectorMgrList[i] && (m_SectorMgrList[i]->GetTcpPort() == port))
        {
            return m_SectorMgrList[i];
        }
    }
    return NULL;
}

ObjectManager *ServerManager::GetObjectManager(long sector_id)
{
	return m_ObjectManMap[sector_id];
}

SectorManager *ServerManager::GetSectorManager(long sector_id)
{
	SectorManager *sector_man = m_SectorMap[sector_id];

	if (sector_id == -1)
	{
		for (int i = 0; i < m_MaxSectors; i++)
		{
			if (m_SectorMgrList[i] && (m_SectorMgrList[i]->GetSectorID() == sector_id))
			{
				return m_SectorMgrList[i];
			}
		}
	}
	return sector_man;
}

bool ServerManager::SetupSectorServer(long sector_id)
{
	bool success = false;

    // Get a sector manager that has not been assigned a sector yet
    SectorManager *mgr = GetSectorManager((long) -1);
    if (mgr)
    {
	    if (mgr->SetupSectorServer(sector_id))
	    {
		    success = true;
	    }
    }

	return (success);
}

bool ServerManager::IsSectorServerReady(short port)
{
    bool ready = false;
    SectorManager *mgr = GetSectorManager(port);
    if (mgr)
    {
        ready = mgr->IsSectorServerReady();
    }

    return (ready);
}

short ServerManager::SetSectorServerReady(long sector, bool ready)
{
	short port = 0;
    SectorManager *mgr = GetSectorManager(sector);
    if (mgr)
    {
        mgr->SetSectorServerReady(ready);
		port = m_Port + mgr->GetSectorNumber();
    }

	return port;
}

// This function formats a message and adds it to the message queue
void LogMySQLMsg(char *format, ...)
{
    char buffer[8192];
    char timestr[20];
	char LogFile[MAX_PATH];
	time_t rawtime;
	struct tm * timeinfo = NULL;

    va_list args;
    va_start(args, format);
    vsprintf_s(buffer, sizeof(buffer), format, args);
    va_end(args);

	time ( &rawtime );
	timeinfo = localtime ( &rawtime );

	// Add _MySQL to the log file name
	sprintf_s(LogFile, sizeof(LogFile), "%s_MySQL", g_LogFilename);
	
	strftime(timestr, 18, "%d/%m/%y %H:%M:%S",timeinfo);

    if (g_ServerMgr)
    {
        g_ServerMgr->ResetMySQLFileTimer(); //m_ChatFileTimer = 40;
		g_ServerMgr->m_MySQLFile = OpenLogFile(g_ServerMgr->m_MySQLFile, LogFile);
		if (g_ServerMgr->m_MySQLFile)
		{
			fprintf(g_ServerMgr->m_MySQLFile, "%s %s", timestr , buffer);
			fflush(g_ServerMgr->m_MySQLFile);
		}
    }

    fprintf(stdout, "%s %s", timestr, buffer); //TODO: put this on a 'verbose' switch
    // Without a TTY (e.g. running under `docker compose logs`) stdout is fully
    // block-buffered and LogMessage output is invisible until the buffer fills
    // or the process exits. Flush explicitly so log lines reach observers in
    // real time.
    fflush(stdout);
}

// This function formats a message and adds it to the message queue
void LogChatMsg(char *format, ...)
{
    char buffer[8192];
    char timestr[20];
	char LogFile[MAX_PATH];
	time_t rawtime;
	struct tm * timeinfo = NULL;

    va_list args;
    va_start(args, format);
    vsprintf_s(buffer, sizeof(buffer), format, args);
    va_end(args);

	time ( &rawtime );
	timeinfo = localtime ( &rawtime );
	
	// Add chatlog to the log file name
	sprintf_s(LogFile, sizeof(LogFile), "%s_chatlog", g_LogFilename);

	strftime(timestr, 18, "%d/%m/%y %H:%M:%S",timeinfo);

    if (g_ServerMgr)
    {
        g_ServerMgr->ResetChatFileTimer(); //m_ChatFileTimer = 40;
        g_ServerMgr->m_ChatFile = OpenLogFile(g_ServerMgr->m_ChatFile, LogFile);
		if (g_ServerMgr->m_ChatFile)
			fprintf(g_ServerMgr->m_ChatFile, "%s %s", timestr , buffer);
    }
}

void ServerManager::ResetMySQLFileTimer()
{
    m_Mutex.Lock();
    g_ServerMgr->m_MySQLFileTimer = 40;
    m_Mutex.Unlock();
}

void ServerManager::ResetChatFileTimer()
{
    m_Mutex.Lock();
    m_ChatFileTimer = 40;
    m_Mutex.Unlock();
}

void ServerManager::ResetLogFileTimer()
{
    m_Mutex.Lock();
    m_LogFileTimer = 40;
    m_Mutex.Unlock();
}

// This function formats a message and adds it to the message queue
void LogMessage(const char *format, ...)
{
    char buffer[8192];
    char timestr[20];
	time_t rawtime;
	tm * timeinfo = NULL;

    va_list args;
    va_start(args, format);
	try
	{
		vsprintf_s(buffer, sizeof(buffer), format, args);
	}
	catch (...)
	{
		va_end(args);
		fprintf(stderr,"Bad Log attempt\n");
		return;
	}
    va_end(args);

	time ( &rawtime );
	timeinfo = localtime ( &rawtime );
	strftime(timestr, 18, "%d/%m/%y %H:%M:%S",timeinfo);

    //print and store - why do we try to buffer this anyway?

    if (g_ServerMgr)
    {
        g_ServerMgr->ResetLogFileTimer();//m_LogFileTimer = 40;
        g_ServerMgr->m_LogFile = OpenLogFile(g_ServerMgr->m_LogFile, g_LogFilename);
		if (g_ServerMgr->m_LogFile)
			fprintf(g_ServerMgr->m_LogFile, "%s %s", timestr, buffer);
    }

    fprintf(stdout, "%s %s", timestr, buffer); //TODO: put this on a 'verbose' switch
    // Without a TTY (e.g. running under `docker compose logs`) stdout is fully
    // block-buffered and LogMessage output is invisible until the buffer fills
    // or the process exits. Flush explicitly so log lines reach observers in
    // real time.
    fflush(stdout);
}

void LogDebug(char *format, ...)
{  
    if (!g_Debug) return;

	return; //no logdebugs for now, crashes the server

    char buffer[8192];
    char timestr[20];
	time_t rawtime;
	struct tm * timeinfo;


    va_list args;
    va_start(args, format);
    vsprintf_s(buffer, sizeof(buffer), format, args);
    va_end(args);

	time ( &rawtime );
	timeinfo = localtime ( &rawtime );
	
	strftime(timestr, 18, "%d/%m/%y %H:%M:%S",timeinfo);

    if (g_ServerMgr)
    {
        g_ServerMgr->m_LogFile = OpenLogFile(g_ServerMgr->m_LogFile, g_LogFilename);
        g_ServerMgr->m_LogFileTimer = 40;
        fprintf(g_ServerMgr->m_LogFile, "%s %s", timestr , buffer);
    }
}

void DumpBuffer(unsigned char *buffer, int length)
{
	char line[128];
    line[0] = 0;
	for (int i = 0; i < length; i++)
	{
		sprintf_s(line + strlen(line), sizeof(line) - strlen(line), "%02X ", buffer[i]);
		if ((i % 16) == 15)
		{
			LogMessage("%s\n",line);
            line[0] = 0;
		}
	}
    if (line[0])
    {
        LogMessage("%s\n",line);
    }
}

void DumpBufferToFile(unsigned char *buffer, int length, char *filename, bool rawData)
{
	FILE *f;
	fopen_s(&f, filename, "wb");

	if (f)
	{
		if (rawData)
		{
			fwrite(buffer,1,length,f);
		}
		else
		{
			char line[128];
			line[0] = 0;
			for (int i = 0; i < length; i++)
			{
				sprintf_s(line + strlen(line), sizeof(line) - strlen(line), "%02X ", buffer[i]);
				if ((i % 16) == 15)
				{
					fprintf(f, "%s\n", line);
					line[0] = 0;
				}
			}
			if (line[0])
			{
				fprintf(f, "%s\n", line);
			}
		}
		LogMessage("Data written to %s\n",filename);
		fclose(f);
	}
	else
	{
		LogMessage("Could not open %s\n",filename);
	}
}

#if 0
// This is called only for ONE instance of the sector manager
bool ServerManager::RegisterSectorServer(short first_port, short max_sectors)
{
	char buffer[4096];
	SSL_METHOD * ssl_client_method;
	SSL_CTX * ssl_context;
	SSL * ssl;

    SSLeay_add_ssl_algorithms();
	ssl_client_method = SSLv2_client_method();
	SSL_load_error_strings();
	ssl_context = SSL_CTX_new(ssl_client_method);
	if (!ssl_context)
	{
        LogMessage("SSL_CTX_new failed\n");
        return false;
	}

	// Establish a SSL connection to the Authentication Server
	// Create a socket
	SOCKET ssl_socket = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
	if (ssl_socket == INVALID_SOCKET)
    {
        LogMessage("Unable to create outgoing SSL socket\n");
        return false;
    }

    unsigned long ip_address = 0x0100007f;
    //if (strstr(g_DomainName, "local") == 0)
    //{
	struct hostent * host;

	if (strlen(g_InternalIP)==0)
	    host = gethostbyname(g_DomainName);
	else
	    host = gethostbyname(g_InternalIP);

	    if (!host)
	    {
           LogMessage("Unable to resolve IP address for %s\n", g_DomainName);
            return false;
	    }
        // Phase K Wave 12: h_addr_list[0] is an in_addr (4B IPv4); reading via
        // `unsigned long*` on Linux pulls 8 bytes and corrupts ip_address.
        ip_address = *((uint32_t *) host->h_addr_list[0]);
    //}

	struct sockaddr_in address;
    memset(&address, 0, sizeof(address));
	address.sin_family = AF_INET;
	address.sin_addr.s_addr = ip_address;
	address.sin_port = htons(SSL_PORT);

    unsigned char * ip = (unsigned char *) &ip_address;
    LogMessage("Connecting to Authentication Server on %d.%d.%d.%d:%d\n",
        ip[0], ip[1], ip[2], ip[3], SSL_PORT);
	if (connect(ssl_socket, (struct sockaddr*) &address, sizeof(address)))
	{
        LogMessage("Unable to connect to Authentication Server on port %d\n", SSL_PORT);
        return false;
	}

    //LogMessage("SSL Connected!\n");

	ssl = SSL_new(ssl_context);
	if (!ssl)
	{
        LogMessage("SSL_new failed\n");
        return false;
	}

	SSL_set_fd(ssl, ssl_socket);

	if (!SSL_connect(ssl))
	{
        LogMessage("SSL_connect failed\n");
        return false;
	}

    // TODO: change this from a hard-coded username something that is set on the command line
    // or a data file.
	sprintf(buffer,
		"GET /sectorserver.cgi?username=VectoR&port=%d&max_sectors=%d&version=%d.%d HTTP/1.1\r\n"
		"User-Agent: AuthLogin\r\n"
        "Host: %s\r\n"
		"Connection: Keep-Alive\r\n"
		"Cache-Control: no-cache\r\n"
		"\r\n",
		first_port,
        max_sectors,
		SECTOR_SERVER_MAJOR_VERSION,
        SECTOR_SERVER_MINOR_VERSION,
        g_DomainName);

    //printf("------\n", buffer);
    //printf("%s", buffer);
    //printf("------\n", buffer);

    //LogMessage("SectorManager calling SSL_write (%d bytes)\n", strlen(buffer) + 1);
	if (SSL_write(ssl, buffer, strlen(buffer) + 1) == -1)
	{
        LogMessage("SSL_write failed\n");
        return false;
	}

    //LogMessage("SectorManager calling SSL_read\n");
	int bytes = SSL_read(ssl, buffer, sizeof(buffer) - 1);
	if (bytes == -1)
	{
        LogMessage("SSL_read failed\n");
		return false;
	}
    buffer[bytes] = 0;

	/* Clean up. */
    //LogMessage("SectorManager calling closesocket\n");
	closesocket(ssl_socket);
    //LogMessage("SectorManager calling SSL_free\n");
	SSL_free(ssl);
    //LogMessage("SectorManager calling SSL_CTX_free\n");
	SSL_CTX_free(ssl_context);

	if (strstr(buffer, "Success=TRUE") == 0)
	{
        LogMessage("Attempt to register the Sector Server failed\n");
        LogMessage("SSL Response:%s\n", buffer);
		return false;
	}
    //else
    //{
    //  LogMessage("Successfully registered the Sector Server!\n");
	//}

	return true;
}
#endif

void ServerManager::SetUDPConnection(UDP_Connection* connection)
{
	m_UDPConnection = connection;
	m_PlayerMgr.SetUDPConnection(connection);
}

void ServerManager::SetPlayerMgrGlobalMemoryHandler()
{
    m_PlayerMgr.SetGlobalMemoryHandler(m_GlobMemMgr);
}

void ServerManager::AddSector(long sector_id, char *sector_name, char *system_name, char *parent_sector_name)
{
	m_SectorServerMgr.AddSector(sector_id, sector_name, system_name, parent_sector_name);
}

char *ServerManager::GetSectorName(long sector_id)
{
	if (sector_id < 10000)
	{
		return (m_SectorContent._GetSectorName(sector_id));
	}
	else
	{
		return (m_StationMgr._GetSectorName(sector_id));
	}
}

char *ServerManager::GetSystemName(long sector_id)
{
	if (sector_id > 9999) sector_id = sector_id / 10;
	return (m_SectorContent._GetSystemName(sector_id));
}

SectorManager **ServerManager::GetSectorManagerList()
{
	return m_SectorMgrList;
}

long ServerManager::GetSectorCount()
{
	return m_SectorCount;
}

void MailManager::HandleMessage()
{
	unsigned long current_tick = GetNet7TickCount();
	//LogMessage("Received query ping from Net7SSL\n");
	g_SSL_receive_time = current_tick;

	//send ping back to SSL
	WriteMessage("pong");
}