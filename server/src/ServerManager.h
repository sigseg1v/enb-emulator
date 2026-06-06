// ServerManager.h
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

#ifndef _SERVER_MANAGER_H_INCLUDED_
#define _SERVER_MANAGER_H_INCLUDED_

#include <mutex>

#include "AccountManager.h"
#include "SectorServerManager.h"
#include "SectorManager.h"
#include "ItemBaseManager.h"
#include "SkillParser.h"
#include "StationLoader.h"
#include "MemoryHandler.h"
#include "MissionManager.h"
#include "StringManager.h"
#include "MOBDatabase.h"
#include "AssetDatabase.h"
#include "SectorContentParser.h"
#include "CBAssetParser.h"
#include "MissionDatabaseSQL.h"
#include "SkillsDatabase.h"
#include "BuffDatabaseSQL.h"
#include "FactionDataSQL.h"

class Player;
class SaveManager;
class CircularBuffer;
class SectorManager;
class ObjectManager;

typedef map<int, SectorManager *> mapSectors;
typedef map<int, ObjectManager *> mapObjectMan;

class ServerManager
{
public:
    ServerManager(bool is_sector_server, unsigned long ip_address, short port, short max_sectors, bool standalone, unsigned long internal_ip_address = 0);
    virtual ~ServerManager();

public:
	void	RunServer();
	bool	SetupSectorServer(long sector_id);
	// Phase AI: bring a sector fully online (load its objects, claim a manager,
	// bind its deterministic UDP port, start its event thread) if it is not
	// already running, and return its manager. Idempotent and thread-safe -- a
	// lock-free O(1) GetSectorManager() fast path, then m_SectorStartMutex guards
	// the actual start (the manager-pool claim + g_SectorObjects population are
	// global shared state, so the start critical section is globally serialized;
	// cold starts are rare, ~tens of ms each). Called on the handoff path.
	SectorManager *EnsureSectorStarted(long sector_id);
	bool	IsSectorServerReady(short port);
	short	SetSectorServerReady(long sector, bool ready);
    void    StoreCharacterData(CharacterDatabase &character_database);
    bool    RequestCharacterDatabase(long avatar_id, CharacterDatabase &character_database);
	void	SetUDPConnection(UDP_Connection* connection);
    void    SetPlayerMgrGlobalMemoryHandler();

	bool	IsSectorAssignmentsComplete()				{ return m_SectorAssignmentsComplete; }

	void	AddSector(long sector_id, char *sector_name, char *system_name, char *parent_sector_name);
	void	SetSectorMap(long sector_id, SectorManager * sectormanager);
	void	SetObjectManMap(long sector_id, ObjectManager * objectmanager);

	// Immutable base port for sector listeners (SECTOR_SERVER_PORT). A sector's
	// deterministic port is GetSectorBasePort() + its slot index -- see
	// SectorManager::BeginSectorThread. (Replaces the old mutating GetSectorPort()
	// sequential allocator, which only produced correct ports when sectors were
	// bound in slot order at boot -- incompatible with on-demand start.)
	short	GetSectorBasePort()							{ return m_Port; }

    void	ReloadAllObjects(); // this is quite slow
	void	ReloadSectorObjects(long sector_id);
	SectorManager *GetSectorManager(short port);
    SectorManager *GetSectorManager(long sector_id);   
	ObjectManager *GetObjectManager(long sector_id);

	char *	GetSectorName(long sector_id);
	char *	GetSystemName(long sector_id);
	SectorManager ** GetSectorManagerList();
	long	GetSectorCount();

	void	ResetSQLLogFileTimer();
    void    ResetChatFileTimer();
    void    ResetLogFileTimer();

	long	GetNextEffectID();

	void	ProcessCommands();

	bool	HalloweenActive()							{ return m_Halloween; }
	void	SetHalloween(bool setting)					{ m_Halloween = setting; }

	void	AddJob(long category)							{ m_JobCatCount[category]++; }
	u32		JobCount(long category)							{ return m_JobCatCount[category]; }

    MOBContent		* MOBList()			{ return &m_MOBList; }
	AssetContent	* AssetList()		{ return &m_AssetList; }
	CBAssetParser	* BAssetRadii()		{ return &m_CBassetList; }

	CircularBuffer	* GetReSendCBuffer()	{ return m_ReSendBuffer; }
	CircularBuffer	* GetUDPCBuffer()	{ return m_UDPSendBuffer; }
	CircularBuffer  * GetMessageBuffer(){ return m_MessageBuffer; }
	// GetTCPCBuffer / GetConnection / m_ConnectionMgr / SSL_Listener / TcpListener
	// were part of kyp's flat single-process TCP design. tada-o moved that role
	// to proxy/ (which binds GLOBAL_SERVER_PORT) and login-server/Net7SSL/, so
	// server/src/ runs purely as a UDP-fed sector/master logic core on Linux.
	// Phase Q (2026-05-23) deleted the cluster outright; if you need the
	// server to terminate TCP again, build it on top of proxy/ServerManager.cpp
	// rather than reviving these.

private:
    void    ServerCheck();
	// Phase AI Stage 2: park sectors that have sat empty past the idle threshold
	// (stop thread, drop listener, free deferred objects). IdleSectorPoll is the
	// throttled scan from ServerCheck; TeardownSector parks one sector.
	void	IdleSectorPoll();
	void	TeardownSector(long sector_id);
	void	MainLoop();
	void	RunMasterServer();
	void	RunSectorServer();
    bool    RegisterSectorServer(short first_port, short max_sectors);

	void	HandleCommandCode(short command_code, int bytes, unsigned char *data);


public:
	// Applies to all servers
	bool				m_IsMasterServer;
    bool                m_IsStandaloneServer;

	// Applies only to Master Server
	AccountManager    * m_AccountMgr;
	SectorServerManager	m_SectorServerMgr;

	// Applies only to Sector Server
	SectorManager	  * m_SectorMgrList[MAX_SECTORS];
	StationLoader		m_StationMgr;
	BuffContent			m_BuffData;			// Holds data on buff effects
    PlayerManager       m_PlayerMgr;
	ItemBaseManager		m_ItemBaseMgr;		// ItemBase
	MissionHandler		m_Missions;			// Contains all the missions
	SkillData		  * m_SkillList;
    MOBContent          m_MOBList;
	Factions			m_FactionData;
	SkillsContent		m_SkillsList;
	AssetContent		m_AssetList;
	CBAssetParser		m_CBassetList;
	SectorContentParser m_SectorContent;
	UDP_Connection	  * m_UDPConnection;	// for MVAS
    UDP_Connection    * m_UDPMasterConnection;
    UDP_Connection    * m_UDPGlobalConnection;  // Phase K: proxy<->server global control plane
    GMemoryHandler    * m_GlobMemMgr;
    StringManager     * m_StringMgr;
	SaveManager		  * m_SaveMgr;
	JobManager		  * m_JobMgr;

	short				m_Port;
    short               m_MaxSectors;
	short				m_SectorCount;
	bool				m_SectorUpdateSelect;
	unsigned long		m_IpAddress;
	unsigned long		m_IpAddressInternal;
	long				m_SectorID;
    int                 m_LogFileTimer;
    int                 m_ChatFileTimer;
	int					m_SQLLogFileTimer;
    FILE              * m_LogFile;
    FILE              * m_ChatFile;
	FILE			  * m_SQLLogFile;
	bool				m_AllowCreate;
	bool				m_DumpXML;

private:
	mapSectors			m_SectorMap;
	mapObjectMan		m_ObjectManMap;
    Mutex               m_Mutex;
	std::mutex			m_SectorStartMutex;   // Phase AI: serializes on-demand sector starts (EnsureSectorStarted)
	bool				m_SectorAssignmentsComplete;
	long				m_SectorEffectID;

	CircularBuffer	  * m_UDPSendBuffer;
	CircularBuffer    * m_ReSendBuffer;
	CircularBuffer	  * m_MessageBuffer;

	bool				m_Halloween;
	u32					m_LastPlayerCount;
	u32					m_LastTeardownPoll; // Phase AI Stage 2: last idle-sector scan tick
	u32					m_SectorIdlePollMs;     // NET7_SECTOR_IDLE_POLL_MS (default 300000)
	u32					m_SectorIdleTeardownMs; // NET7_SECTOR_IDLE_TEARDOWN_MS (default 300000)
	u32					m_JobCatCount[4];
};

#endif // _SERVER_MANAGER_H_INCLUDED_
