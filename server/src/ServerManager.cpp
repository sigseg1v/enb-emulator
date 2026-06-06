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

// Phase AI Stage 2: idle sector teardown tuning. Defaults below; both are
// overridable at runtime via the NET7_SECTOR_IDLE_POLL_MS /
// NET7_SECTOR_IDLE_TEARDOWN_MS env vars (read in the ctor). Scan at most once
// per poll interval, and park a space sector once it has been continuously
// empty at least the teardown interval.
#define SECTOR_IDLE_POLL_MS_DEFAULT		300000  // 5 min between idle scans
#define SECTOR_IDLE_TEARDOWN_MS_DEFAULT	300000  // 5 min empty before teardown

// Read a positive-millisecond tuning value from an env var, falling back to a
// default when unset/blank/unparseable. Zero or negative is rejected (would
// busy-poll / tear down instantly), so it also falls back.
static u32 ReadIdleMsEnv(const char *name, u32 fallback)
{
	const char *v = getenv(name);
	if (!v || !*v)
		return fallback;
	long parsed = atol(v);
	if (parsed <= 0)
	{
		LogMessage("%s=\"%s\" is not a positive integer; using default %u\n",
			name, v, (unsigned)fallback);
		return fallback;
	}
	return (u32)parsed;
}

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
	m_SQLLogFileTimer = 0;
    m_ChatFile = (0);
	m_SQLLogFile = (0);
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
	m_LastTeardownPoll = 0;
	m_SectorIdlePollMs     = ReadIdleMsEnv("NET7_SECTOR_IDLE_POLL_MS",     SECTOR_IDLE_POLL_MS_DEFAULT);
	m_SectorIdleTeardownMs = ReadIdleMsEnv("NET7_SECTOR_IDLE_TEARDOWN_MS", SECTOR_IDLE_TEARDOWN_MS_DEFAULT);
	LogMessage("Phase AI Stage 2 idle teardown: poll=%u ms, teardown=%u ms\n",
		(unsigned)m_SectorIdlePollMs, (unsigned)m_SectorIdleTeardownMs);

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
    // The master listener OBJECT is constructed here; its RECEIVER THREAD is
    // started separately (StartReceiver(), below). Under Phase AI on-demand
    // start there is no sector-assignment wait to defer past: ProcessHandoff
    // calls EnsureSectorStarted, which binds the target sector's deterministic
    // UDP port BEFORE the MASTER_HANDOFF_CONFIRM (0x2009) reports it -- so the
    // confirm always carries a real, listening port and the proxy never has to
    // fall back to a proxy-local ServerRedirect.
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

	sql_connection_c connection( "net7_user", g_DB_Host, g_DB_User, g_DB_Pass);
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
	// Phase AI: at boot, load per-sector metadata + the navigation skeleton
	// (gates/planets/stations/navs/gwell/radiation) for ALL sectors, but DEFER the
	// heavy MOBs + asteroid fields. The skeleton must stay resident galaxy-wide
	// because mission generation and gate-sealing read remote sectors' gates and
	// planets cross-sector (see SectorLoadMode). The deferred MOBs/fields load on
	// demand per sector on the first handoff (ServerManager::EnsureSectorStarted).
	m_SectorContent.LoadSectorMetadata();

	// Load Stations from database
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

		// Phase AI -- on-demand sector start. We no longer pre-assign and
		// pre-bind all ~130 sectors at boot. Each sector is content-loaded,
		// UDP-bound, and thread-started lazily on the first handoff to it
		// (UDP_Master::ProcessHandoff -> ServerManager::EnsureSectorStarted),
		// which binds the target's deterministic port BEFORE LookupSectorServer
		// reads it -- so the 0x2009 MASTER_HANDOFF_CONFIRM always carries a real,
		// listening port. That makes the old "wait until every sector is bound
		// before starting the master receiver" ordering unnecessary: the
		// per-handoff bind (serialized in EnsureSectorStarted) now provides it.
		//
		// We still flip the assignments-complete flag here: it means "the server
		// is up and ready to route handoffs on demand" and is what lets MVAS
		// logins through -- UDP_MVAS.cpp returns the pre-start 0x100B response
		// while IsSectorAssignmentsComplete() is still false.
		m_SectorAssignmentsComplete = true;

		// Safe to begin dispatching master-plane MASTER_HANDOFF (0x2008): the
		// handler starts the target sector on demand and only then reports its
		// port.
		master_udp_listener.StartReceiver();

		// Phase S (2026-06-01): start the MVAS (move-assist) receiver. The
		// MVASauth connection (MVAS_LOGIN_PORT/3806, CONNECTION_TYPE_MVAS_TO_PROXY,
		// constructed in Net7.cpp and handed to us via SetUDPConnection) binds
		// its socket in the constructor but -- unlike the global, master, and
		// sector listeners above -- its receiver thread was never started by
		// the deferred-start refactor. The result was that EVERY inbound MVAS
		// datagram (0x1004 position, 0x1000 register, comms-alive) was silently
		// dropped: no player's position ever updated from the move-assist feed
		// (Player::UpdatePositionFromMVAS), so proximity nav exposure driven by
		// movement (Player::CheckNavs) could never fire. HandleMVASPosReturn
		// (server/src/UDP_MVAS.cpp) and the proxy's emit to MVAS_LOGIN_PORT
		// (proxy/UDPProxyMVAS.cpp:161) both show 3806 is the intended receive
		// path; this restores it. In-game/position traffic only matters once
		// players are in-sector, so it starts here alongside the master plane.
		if (m_UDPConnection) m_UDPConnection->StartReceiver();

		LogMessage("Registering sector server: port=%d, max_sectors=%d\n", m_Port, m_MaxSectors);
		//RegisterSectorServer(m_Port, m_MaxSectors);
		//RegisterSectorServer(GLOBAL_SERVER_PORT, m_MaxSectors);

		m_SectorServerMgr.SectorLockdown();

		MainLoop();

		// Every slot [0, m_MaxSectors) was allocated at boot (see ctor loop) and
		// lives at a fixed index regardless of online/parked state. m_SectorCount
		// is now an active-online tally (Phase AI: cold-start bumps it, idle
		// teardown decrements it), so it is NOT the right bound here -- iterate the
		// allocation range and NULL-guard.
		for (i = 0; i < m_MaxSectors; i++)
		{
			if (m_SectorMgrList[i])
			{
				delete m_SectorMgrList[i];
				m_SectorMgrList[i] = NULL;
			}
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

	// Phase AI Stage 2: periodically park sectors that have sat empty.
	if (start_tick > (m_LastTeardownPoll + m_SectorIdlePollMs))
	{
		m_LastTeardownPoll = start_tick;
		IdleSectorPoll();
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

    if (m_SQLLogFileTimer)
    {
		// if the chat file has been idle for 2 seconds, close it
        m_Mutex.Lock();
        m_SQLLogFileTimer--;
        if (m_SQLLogFileTimer == 0 && m_SQLLogFile != NULL)
        {
            fclose(m_SQLLogFile);  // close the chat file
			m_SQLLogFile = NULL;   // forget the file handle
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
			m_SectorContent.LoadSectorContent(sector_id, SECTOR_LOAD_ALL);
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

SectorManager *ServerManager::EnsureSectorStarted(long sector_id)
{
	// Fast path: already started. m_SectorMap[sector_id] is set inside
	// SectorManager::SetupSectorServer, and EnsureSectorStarted is now the ONLY
	// path that starts a sector, so a non-null manager here means fully online
	// (objects loaded, port bound, thread running). No lock needed to read it --
	// a started sector never reverts on this path (teardown, Phase AI Stage 2,
	// takes the same mutex).
	SectorManager *mgr = GetSectorManager(sector_id);
	if (mgr && mgr->IsSectorOnline())
	{
		mgr->StampActivity(GetNet7TickCount());
		return mgr;
	}

	// Slow path: bring it online under the global start lock. The lock spans the
	// whole start because the pieces touch GLOBAL shared state, not just this
	// sector: LoadSectorContent populates the process-wide g_SectorObjects map,
	// and SetupSectorServer claims a manager from the shared unassigned pool
	// (GetSectorManager(-1)) and bumps m_SectorCount. Per-sector locking would
	// not make those safe, so we serialize cold starts globally.
	//
	// NOTE: this is a SYNCHRONOUS blocking load on the caller's thread. For a
	// gate/dock the caller is the SOURCE sector's event thread, so that sector
	// hitches for the duration. Live measurement (2026-06-06) puts the whole
	// cold-start (this load + SetupSectorServer + port bind) at 27ms for a
	// station sector up to ~236ms for the busiest space sector; the worst-
	// populated sector in the content DB extrapolates to ~300ms. It also fires
	// at most once per sector per process uptime -- the lock-free fast path
	// above serves every later entry, and a parked-restart is cheaper still.
	// That is well under the threshold where an async refactor would pay for
	// its complexity, so keep it sync for now.
	std::lock_guard<std::mutex> guard(m_SectorStartMutex);

	// Re-check under the lock (another thread may have started it while we waited).
	mgr = GetSectorManager(sector_id);
	if (mgr && mgr->IsSectorOnline())
		return mgr;

	// Parked-restart path: the manager + its galaxy-resident skeleton are still
	// mapped (Phase AI Stage 2 teardown PARKS rather than unmaps, to avoid racing
	// the lock-free fast path above). Reload only the deferred objects the purge
	// freed, rebind the deterministic port, and restart the event thread. We must
	// NOT re-run SetupSectorServer here -- the manager is already claimed and wired.
	if (mgr)
	{
		LogMessage("EnsureSectorStarted: restarting parked sector_id=%d\n", sector_id);
		m_SectorContent.LoadSectorContent(sector_id);
		mgr->BeginSectorThread();
		mgr->StampActivity(GetNet7TickCount());
		m_SectorCount++;
		return mgr;
	}

	LogMessage("EnsureSectorStarted: cold-starting sector_id=%d\n", sector_id);

	// 1) Load THIS sector's DEFERRED objects (MOBs + asteroid fields/resources)
	//    from the DB into its obj_manager + g_SectorObjects. The boot pass already
	//    loaded the nav skeleton (gates/planets/stations/navs) for every sector, so
	//    this appends only the heavy populating objects (mode defaults to
	//    SECTOR_LOAD_DEFERRED_ONLY -- it must NOT wipe the resident skeleton).
	m_SectorContent.LoadSectorContent(sector_id);

	// 2) Claim a free manager and wire it to the sector (registers m_SectorMap,
	//    points m_ObjectMgr at the now-populated obj_manager, InitialiseResourceContent,
	//    InitialiseSector, ++m_SectorCount).
	if (!SetupSectorServer(sector_id))
	{
		LogMessage("EnsureSectorStarted: FAILED to set up sector_id=%d\n", sector_id);
		return NULL;
	}

	// 3) Bind the deterministic UDP port and start the event thread.
	mgr = GetSectorManager(sector_id);
	if (mgr)
		mgr->BeginSectorThread();

	return mgr;
}

// Phase AI Stage 2: scan all online space sectors. An occupied sector gets its
// activity tick refreshed; a sector empty past m_SectorIdleTeardownMs is parked.
void ServerManager::IdleSectorPoll()
{
	u32 now = GetNet7TickCount();
	for (int i = 0; i < m_MaxSectors; i++)
	{
		SectorManager *mgr = m_SectorMgrList[i];
		if (!mgr || !mgr->IsSectorOnline())
			continue;
		long sid = mgr->GetSectorID();
		if (sid < 0 || sid >= 9999)   // space sectors only; stations share a parent
			continue;
		if (mgr->GetOccupancy() > 0)
		{
			mgr->StampActivity(now);
			continue;
		}
		if (now > (mgr->GetLastActivityTick() + m_SectorIdleTeardownMs))
			TeardownSector(sid);
	}
}

// Phase AI Stage 2: PARK one empty space sector. Stops its event thread, drops
// its UDP listener, and frees its deferred objects, while leaving the manager
// MAPPED and its galaxy-resident skeleton intact. Mutating m_SectorMap would
// race the lock-free EnsureSectorStarted fast path, so we never unmap -- a
// later entry restarts the parked manager in place (same start mutex).
void ServerManager::TeardownSector(long sector_id)
{
	std::lock_guard<std::mutex> guard(m_SectorStartMutex);
	SectorManager *space = GetSectorManager(sector_id);
	if (!space || !space->IsSectorOnline())
		return;
	if (sector_id < 0 || sector_id >= 9999)
		return;

	// CRITICAL: the obj_manager is SHARED by this space sector and every station
	// instance built on it (station id/10 == space id; see
	// SectorManager::SetupSectorServer). A docked player counts toward the STATION
	// manager's occupancy, NOT the space manager's, so "space is empty" does NOT
	// mean the shared manager is idle. Purging the shared manager while a station
	// recv/event thread is live is a use-after-free (GetObjectFromID is lockless).
	// So treat the shared obj_manager as the teardown UNIT: collect the whole
	// family, bail if ANY member still has players, then quiesce EVERY member
	// before purging the shared pool exactly once.
	ObjectManager *shared = space->GetObjectManager();
	SectorManager *family[MAX_SECTORS];
	int family_count = 0;
	for (int i = 0; i < m_MaxSectors; i++)
	{
		SectorManager *m = m_SectorMgrList[i];
		if (!m || !m->IsSectorOnline())
			continue;
		if (m->GetObjectManager() != shared)
			continue;
		if (m->GetOccupancy() > 0)
		{
			// Family still occupied (e.g. a docked player at a station). Refresh the
			// space sector's activity stamp so the idle poll re-arms cleanly.
			space->StampActivity(GetNet7TickCount());
			return;
		}
		family[family_count++] = m;
	}

	// Probe BEFORE parking anything: if the shared manager's skeleton is not a
	// contiguous prefix (e.g. a DEV /deco appended an OT_DECO after the deferred
	// load), PurgeDeferredObjects would abort and free nothing. Parking the family
	// first and then aborting the purge would leave it parked-but-resident, and the
	// restart path would double-load the still-resident deferred objects. So if we
	// can't purge cleanly, leave the whole family fully online and re-arm the timer.
	if (!shared->CanPurgeDeferredObjects())
	{
		LogMessage("TeardownSector: sector_id=%d not tail-purgeable (non-contiguous skeleton); leaving online\n",
			(int)sector_id);
		space->StampActivity(GetNet7TickCount());
		return;
	}

	LogMessage("TeardownSector: parking idle sector_id=%d (%d shared managers in family)\n",
		(int)sector_id, family_count);

	// Phase 1: take every family member offline and FULLY quiesce its threads
	// (event thread joined, recv thread joined via StopReceiver, event slots
	// nulled) BEFORE touching the shared manager. After this loop no event or
	// recv thread of any family member is in flight, so the purge cannot race a
	// lockless reader.
	for (int i = 0; i < family_count; i++)
	{
		family[i]->SetSectorOnline(false);   // close the lock-free fast path first
		family[i]->StopSectorThread();
		family[i]->ClearAllEventSlots();
		family[i]->DropListener();
		m_SectorCount--;
	}

	// Phase 2: now that the whole family is quiesced, free the shared manager's
	// deferred objects exactly once. We probed CanPurgeDeferredObjects() above
	// before parking, so this must succeed; a false here means a runtime append
	// raced the probe during quiesce -- the family is now parked-but-resident and
	// a restart will double-load. Flag it as the defect it is.
	if (!shared->PurgeDeferredObjects())
	{
		LogMessage("TeardownSector: WARNING sector_id=%d purge aborted AFTER parking (append raced probe); deferred objects resident, restart will double-load\n",
			(int)sector_id);
	}
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
void LogSQLMsg(char *format, ...)
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

	// Add _SQL to the log file name
	sprintf_s(LogFile, sizeof(LogFile), "%s_SQL", g_LogFilename);
	
	strftime(timestr, 18, "%d/%m/%y %H:%M:%S",timeinfo);

    if (g_ServerMgr)
    {
        g_ServerMgr->ResetSQLLogFileTimer(); //m_ChatFileTimer = 40;
		g_ServerMgr->m_SQLLogFile = OpenLogFile(g_ServerMgr->m_SQLLogFile, LogFile);
		if (g_ServerMgr->m_SQLLogFile)
		{
			fprintf(g_ServerMgr->m_SQLLogFile, "%s %s", timestr , buffer);
			fflush(g_ServerMgr->m_SQLLogFile);
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

void ServerManager::ResetSQLLogFileTimer()
{
    m_Mutex.Lock();
    g_ServerMgr->m_SQLLogFileTimer = 40;
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