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
** Copyright of our assets/code/software began in 2005-2009 �, Net-7 Entertainment.
**
*/

#ifndef _SERVER_MANAGER_H_INCLUDED_
#define _SERVER_MANAGER_H_INCLUDED_

#include "AccountManager.h"
#include "SectorServerManager.h"
#include "ConnectionManager.h"
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
class SSL_Listener;
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

	short	GetSectorPort()								{ ++m_Port; return (m_Port - 1); }

    void	ReloadAllObjects(); // this is quite slow
	void	ReloadSectorObjects(long sector_id);
	SectorManager *GetSectorManager(short port);
    SectorManager *GetSectorManager(long sector_id);   
	ObjectManager *GetObjectManager(long sector_id);

	char *	GetSectorName(long sector_id);
	char *	GetSystemName(long sector_id);
	SectorManager ** GetSectorManagerList();
	long	GetSectorCount();

	SSL_Connection *GetSSLConnection();

	void	ResetMySQLFileTimer();
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
	// GetTCPCBuffer/GetConnection — kyp-era TCP path. tada-o reuses the resend
	// buffer for TCP and lets ConnectionManager hand out Connection nodes;
	// stub to NULL so the listener compile-links. Real wiring is Phase B work.
	CircularBuffer	* GetTCPCBuffer()	{ return m_ReSendBuffer; }
	class Connection * GetConnection()	{ return 0; }

private:
    void    ServerCheck();
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
	// ConnectionManager — kyp-era code (SSL_Listener, TcpListener,
	// ClientToGlobalServer) still references this. The tada-o refactor moved
	// some logic into PlayerManager but didn't drop the field, so we keep it
	// present so those call sites compile.
	ConnectionManager   m_ConnectionMgr;

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
	int					m_MySQLFileTimer;
    FILE              * m_LogFile;
    FILE              * m_ChatFile;
	FILE			  * m_MySQLFile;
	bool				m_AllowCreate;
	bool				m_DumpXML;

private:
	mapSectors			m_SectorMap;
	mapObjectMan		m_ObjectManMap;
    Mutex               m_Mutex;
	bool				m_SectorAssignmentsComplete;
	long				m_SectorEffectID;
	//SSL_Listener	  * m_SSL_listener;

	CircularBuffer	  * m_UDPSendBuffer;
	CircularBuffer    * m_ReSendBuffer;
	CircularBuffer	  * m_MessageBuffer;

	bool				m_Halloween;
	u32					m_LastPlayerCount;
	u32					m_JobCatCount[4];
};

#endif // _SERVER_MANAGER_H_INCLUDED_
