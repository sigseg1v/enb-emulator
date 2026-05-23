// SectorManager.h
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

#ifndef _SECTOR_MANAGER_H_INCLUDED_
#define _SECTOR_MANAGER_H_INCLUDED_

#include "Mutex.h"
#include "PlayerManager.h"
//#include "MemoryHandler.h"
#include "TimeNode.h"
#include "StationLoader.h"
#include <vector>

struct CharacterDatabase;
class ServerManager;
struct SectorData;
class MemoryHandler;
class SectorMemoryManager;
class Object;
class CMob;
struct MOBData;

typedef std::vector<TimeNode*> TimeNodeVec;
typedef std::vector<Object*> ObjectList;

enum object_type
{
	OT_NONE, OT_STATION, OT_STARGATE, OT_OBJECT, OT_RESOURCE, OT_PLAYER, OT_HULK, 
    OT_ABS, OT_FLOATING_ORE, OT_MOB, OT_NAV, OT_PLANET, OT_FIELD, OT_CAPSHIP, OT_DECO,
    OT_HUSK, OT_MOBSPAWN, OT_GROUP, OT_GWELL, OT_RADIATION
};

enum sector_type
{
    ST_SPACE = 0, ST_PLANET, ST_GAS_GIANT
};

struct StationBroadcastNode
{
    Object *station;
};

struct JobNode
{
	int ID;
	int MissionID;
	int Category;
	int	Level;
	int Sponsor;
	ItemBase *Item;		// may not be needed - could be done in 
	Object *Obj;		// navs
	MOBData *Mob;		// MOB spawned for this job
	bool available;
} ATTRIB_PACKED;

typedef vector<JobNode*> vecJobList;

// There is one instance of this class for each sector


//This defines how long in seconds we want timeslots to be used
//before we switch back to the older style of timenodes.
#define TIMESLOT_DURATION 60

//TODO: change this to make a single global event slot timer
#define LONG_TERM_NODES 500

class SectorManager
{
public:
	SectorManager(ServerManager *server_mgr);
	virtual ~SectorManager();

public:
	//int		m_EffectID;				// ID used for each effect

    void    SetSectorData(SectorData * sector_list);
    bool    StartListener(short port);
	bool	RegisterSectorServer(short first_port, short max_sectors);
	bool	SetupSectorServer(long sector_id);
    long    GetSectorID();
    short   GetTcpPort();
    bool    IsSectorServerReady();
    void    SetSectorServerReady(bool ready);
    bool    HandleSectorLogin(Player *player);
	void	HandleSectorLogin2(Player *player); //split the actual login over 2 cycles. This helps to conserve buffer space
	void	HandleSectorLogin3(Player *player);
    char *  GetSystemName(long sector_id);
    char *  GetSectorName(long sector_id);
    long    GetSectorIDFromName(char *sector_name);
	void	GateJump(Player *player);
	void	DestroyMOB(Object *mob, long player_id);
	void	RemoveMOB(Object *mob);
	void	SetBoundaries(int sector);
	long	GetNextEffectID();
	void	CheckMissionPlayerLocation(Player *player, long GameID);
    ServerParameters *GetSectorParams();
	void	SlaySectorMobs(Player *dev);
    void    ProcessMOBs(u32 current_tick, long check_tick, bool handle_attacks);

	// client calls
	void	SendServerParameters(Player *player);
    void    LaunchIntoSpace(Player *player);
    void    Dock(Player *player, long target);
    bool    Gate(Player *player, long target);
	bool	GateActivate(Player *player, long target);
	void	SectorServerHandoff(Player *player, int sector_id);
	Object *GetObject(long object_id);
    Object *GetObject(char *object_name);
    float	PlayerSeparation(long object_id, Player *player, object_type = OT_OBJECT);

	TimeNode * AddTimedCall(CMob *player, broadcast_function func, long time_offset, Object *obj, long i1 = 0, long i2 = 0, long i3 = 0, long i4 = 0, char *ch = 0, float a = 0.0f);
    void    AddTimedCallPNode(TimeNode *node, unsigned long time_offset);
    void    AddTimedCallPNode(TimeNode *this_node, broadcast_function func, long time_offset, Object *obj, long i1 = 0, long i2 = 0, long i3 = 0, long i4 = 0, char *ch = 0, float a = 0.0f);
	void	MakeTimedCall(TimeNode *node);
	void	RemoveTimedCall(TimeNode *node, bool force = false);
	void	RunSectorEventThread();
	void	InitialiseSector();
	void	SlotTimedCall(TimeNode *node, unsigned long time_offset);
	void	RemovePlayerInstallEvents(Player *player);
	void	RemovePlayerEvents(Player *player);
    long    GetSectorNextObjID();
	
	//all client->sector broadcast calls which can be placed on the timer queue
	void	RechargeReactor(Player *player);
	void	RechargeShield(Player *player);
	void	ForceLogout(Player *player);
	void	BuffTimeout(Player *player);

    u32	  * GetSectorPlayerList();
    void    AddPlayerToSectorList(Player *player);
    void    RemovePlayerFromSectorList(Player *player);

    ObjectManager * GetObjectManager()      { return (m_ObjectMgr); };

    void    SetSectorNumber(short number)   { m_SectorNumber = number; };
    short   GetSectorNumber()               { return (m_SectorNumber); };
	long	GetSectorType()					{ return (m_SectorData->sector_type); };

	long	GetIPAddr()						{ return (m_IPAddr); }
	void	SetIPAddr(long addr)			{ m_IPAddr = addr; }
	short	GetPort()						{ return m_Port; }

	char *	GetSectorName()					{ return m_SectorName; }

	long	GetOccupancy();

	void	ShutdownListener();
	void	ReStartListener();

	void	BeginSectorThread();

	void	MOBBlastDamage(Player *p, float *position, float damage);

	int		GetJobList(u8* buffer);
	int		GetJobDescription(u8 *m_ScratchBuffer, long job_id);
	bool	AwardJob(Player *p, long job_id);

private:
    void    StationLogin(Player *player);
    void    SectorLogin(Player *player);
	void	StationLogin2(Player *player);
	void	SectorLogin2(Player *player);
	void	SectorLogin3(Player *player);
    bool    CallSlotEvents(long index);
	bool	CallLongTermEvents(long index);
    long    GetSlotIndex(long time_offset);

	void	SetIndex(u32 index);
	void	UnSetIndex(u32 index);
	bool	GetIndex(u32 index);

	void	RefreshJobs();
	long	GetJobCount();
	JobNode	*GetJobNode(int ID);

	static UINT WINAPI SectorManager::RunEventThreadAPI(void *Param);

private:
    Mutex           m_Mutex;
	int				m_ShutDownMark;			// Makes the shutdown Message
	int		        m_SectorID;             // Sector ID for THIS sector
    int             m_SystemID;             // System ID for THIS sector
    SectorData    * m_SectorData;           // Pointer to Sector Data for THIS sector
    ServerManager * m_ServerMgr;
    UDP_Connection* m_SectorConnection;     // UDP connection for sector
    short           m_Port;
	long			m_IPAddr;
	bool	        m_IsSectorServerReady;
    short           m_SectorNumber;         // primarily used to index into the player explored nav lists

	long			m_SectorEffectID;

    char          * m_SectorName;			// or station name, eg. Earth Station
    char          * m_SystemName;			// eg. Sol
    char          * m_ParentSectorName;		// Sector name for Stations, i.e. Earth
    char          * m_Greetings;			// Greetings upon entering station or sector

	TimeNode	  * m_CoarseEventSlots[LONG_TERM_NODES];// 200 additional slots for coarse memory usage

	TimeNode	  * m_EventSlots[TIMESLOT_DURATION*10][100]; //100 slots per 100ms timeslot
	u8				m_EventSlotsIndex[TIMESLOT_DURATION*10]; //index into timeslot
    long            m_EventSlotIndex;
	u32				m_StartSlotTick;

	u32				m_check_tick;

	SectorMemoryManager * m_ObjectMemMgr;

    u32				m_PlayerList[MAX_ONLINE_PLAYERS/32 + 1];           // this is a list of players in the sector
	u32				m_PlayerCount;

    ObjectManager * m_ObjectMgr;

	bool			m_SectorThreadRunning;

	long			m_JobTerminalLevel;
	vecJobList		m_JobList;
	long			m_JobListCount;
	long			m_JobListID;
	

	HANDLE			m_SectorThread;

	float			m_xmin;
	float			m_xmax;
	float			m_ymin;
	float			m_ymax;
	float			m_xctr;
	float			m_yctr;
};


#endif // _SECTOR_MANAGER_H_INCLUDED_

