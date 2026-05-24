// PlayerManager.h
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

#ifndef _PLAYER_MANAGER_H_INCLUDED_
#define _PLAYER_MANAGER_H_INCLUDED_

#define MAX_ONLINE_PLAYERS 500

#include <vector>
#include <map>
#include <net7/Mutex.h>
#include <net7/PacketStructures.h>
#include "SectorData.h"
#include "VectorMath.h"
#include "AuxClasses/AuxGroupInfo.h"
#include "Guilds.h"

class UDP_Connection;
struct StarbaseAvatarChange;
struct StationBroadcastNode;
class GMemoryHandler;
class Object;
class Player;

typedef std::map<unsigned long, unsigned long> PlayerIDMap;

struct Group
{
	int GroupID;
	bool ForceAutoSplit;
	bool RestrictedLootingRights;
	bool AutoReleaseLootingRights;
	char FormationName[40];
	_GroupMember Member[6];
	bool AcceptedInvite[6];
	int NextLooter;
	struct Group * next;
} ATTRIB_PACKED;

// There is one instance of this class for all sectors in this process
class PlayerManager
{
public:
    PlayerManager();
    virtual ~PlayerManager();

//private:
public:
    typedef std::vector<Player*> PlayerList;

    struct MParam
    {
	    PlayerManager * ClassAdd;
    } ATTRIB_PACKED;


///////////////////////////////
/////  Movement In Space //////
///////////////////////////////

public:
	void	RunMovementThread(void);
	void	RunPlayerUpdate(Player *p);
	void	RunLoginThread(void);
	void	TerminateAllPlayers(void); //used for forced shutdown
	void	SendUDPOpcodes(void);
	void	SendUDPPlayerOpcodes(Player *p);

	// Chat Stuff
	void	ChatSendEveryone(long GameID, char * Message, bool copy_to_originator = true, bool display_postfix = true, long minAdminLevel=0);
	void	BroadcastChat(long GameID, char * Message, bool copy_to_originator = true, bool display_postfix = true, long minAdminLevel=0);
	void	LocalChat(long GameID, char * Message, bool copy_to_originator = true, bool display_postfix = true, long minAdminLevel=0);
	void	ChatSendPrivate(long GameID, char * Nick, char * Message);
	void	ChatSendChannel(long GameID, char * Channel, char * Message);
	void	SendGlobalVaMessage(long GameID, long adminLevel, bool copy_to_originator, char colour, char *msg, ...);
	void	SendGlobalChatEvent(int type, Player *source, char *channel="", char *message="");
	void	GMMessage(char * Message);
	void	ErrorBroadcast(char * Message, ...);
	void	GlobalMessage(char * Message);
    void    GlobalAdminMessage(char * Message);
	long	GetChannelFromName(char * channel_name);

	// Send new players the players in the sector
	void	RemovePlayer(Player *player);
	void	ListPlayersAndLocations(Player *send_to, int min_admin_level = 0, int max_admin_level = 100);
	void	ListPlayersWithSearch(Player *send_to, char * searchString, int min_admin_level = 0, int max_admin_level = 100);
	void	SendPlayerWithoutConnection(long player_id);
    void    SetSector(Player *player, long sector_id);
    bool    SetupPlayer(Player *player, long IPaddr);
    void    SetGlobalMemoryHandler(GMemoryHandler *MemMgr);
    void    CheckForDuplicatePlayers(Player *player);
	bool	CheckAccountInUse(char *username);
    u32	  * GetSectorList(Player *player);
	bool	GetNextPlayerOnList(Player *&p, u32 *player_list);
	bool	GetIndex(Player *p, u32 *player_list);
	void	SetIndex(Player *p, u32 *player_list);
	void	UnSetIndex(Player *p, u32 *player_list);

    //new login/logout methods
    void    DropPlayerFromSector(Player *p);
    void    DropPlayerFromGalaxy(Player *p);

	//MVAS methods
	void	SendMVASUpdate(long GameID);
	void	SetUDPConnection(UDP_Connection *connection);

    // Player utility Methods
	Player *GetPlayer(long GameID, bool sector_login = false);
	Player *GetPlayer(char * Name);
    long    GetGameIDFromName(char * Name);
	Player *GetPlayerFromCharacterID(long CharacterID);
	Player *GetPlayerFromIndex(long index, bool sector_login = false);

    char  * WhoHtml(size_t *response_length);

	//Grouping Methods
	long	GetMemberID(int GroupID, int MemberIndex);
	void	GroupInvite(int GroupID, long LeaderID, long PlayerID);
	void	AcceptGroupInvite(int GroupID, long PlayerID);
	void	RejectGroupInvite(int GroupID, long PlayerID);
	void	LeaveGroup(int GroupID, long PlayerID);
	void	KickFromGroup(int GroupID, long LeaderID, long PlayerID);
	void	DisbanGroup(int GroupID, long PlayerID);
	void	RemoveGroup(int GroupID);
	int		GetMemberCount(int GroupID);
	void	RequestGroupAux(int GroupID, int PlayerID);
	void	GroupChat(int GroupID, long GameID, char * Message);
	bool	GroupExploreXP(Player *owner, char *message, long XP_Gain);
    void    GroupCombatXP(Player *owner, char *, int mob_level);
    int     GetGroupFromPlayer(long PlayerID);
    bool    CheckGrouped(Player *p1, Player *p2);
	float	GetGroupWarpSpeed(Player *p);
	u32 	GetGroupWarpRecovery(Player *p);
	bool 	GroupAction(long SourceID, long TargetID, long Action);
	bool 	SetFormation(long leaderID, long formation, char* formation_name);
	bool	FormUp(long PID);
	bool	LeaveFormation(long PID);
	bool	CheckGroupFormation(Player *p);
	bool	SendFormation(Player *SendP, Player *TargetP);
	bool 	BreakFormation(long leaderID);
	bool 	RequestTargetMyTarget(long leaderID, long targetID);
	void	FormationEngineOperation(Player *p, bool engine);
	void	TransferGroupBuffs();
	Group * GetGroupFromID(int GroupID);

	void	RequestAllPlayersLFG(Player *player);

	// guild methods
	void 	LoadGuildsFromSQL();
	void    FreeGuilds();
	int  	AddGuildToList(int id, char *name, char *motd, long points=0, short level=0, bool publicstats=true);
	void 	RemoveGuildFromList(int id, Player *origin=NULL, bool disbanded=false);
	void 	AddRankToGuild(int id, int rank, char *name, long flags, long perm2, long perm3, long perm4);
	void 	AddRanksToGuild(int id, char *ranks);
	int 	AddMemberToGuild(int id, char *player_name, long avatar_id, int rank=0, bool active=true, int contribution=0, char *tag="");
	bool 	RemoveMemberFromGuild(int id, char *player_name);
	Guild  *GuildFromId(int id);
	Guild  *GuildFromName(char *name);
	Guild  *GuildFromName(char *name, bool pending);
	int 	GetMemberIndex(Guild *g, char *name);
	GuildMember *GetGuildMember(Guild *g, char *name);
	char   *GetRankName(Guild *g, int rank);
	GuildRank *GetRank(Guild *g, int rank);
	bool	CheckPermission(Guild *g, char *name, long permission_bit);
	void	CreateGuild(Player *founders[], char *name);
	bool 	CheckGuildCreationAccepted(char *guild_name, char *player_name);
	bool	CheckIfFounderOfPendingGuild(char *name);
	void	GuildChat(long GameID, char * Message, bool copy_to_originator=true);
	void 	SendMessageToFounders(Guild *g, int Type, char *OtherName="", char *GuildName="");
	void 	SendMessageToGuildMembers(int id, Player *origin, int Type, char *OtherName="", char *GuildName="", bool copy_to_originator=true);
	void 	SaveNewGuild(Guild *guild);
	void 	SaveGuildMember(int guild_id, GuildMember *member, bool remove=false);
	void 	SaveGuildRank(int guild_id, short rank_num, GuildRank *rank);
	void 	SaveGuildInfo(Guild *guild);

private:
	int		GetMemberCount(Group * myGroup);
	int		GetMembersInXPRange(Player * player, Group * myGroup);
	void	DisbanGroup(Group *myGroup);
	void	RemovePlayerFromGroup(Group * myGroup, int MemberIndex);
	void	SendEmptyGroupAux(long PlayerID);
public:
	void	SendAuxToGroup(Group * myGroup);
	void	SendClickToGroup(Group *myGroup, Player *from);
private:
	void	SetGroupInfoMembersFromGroup(_GroupInfo & myGroupInfo, Group * myGroup, int MemberIndex);
	void	SendGroupInvite(Group * myGroup, Player *c);
	
private:
	Group		* m_GroupList;
	GuildList     m_GuildList;
	long		  m_NextGroup;
	int			  m_NextGuild;
	int 		  m_NextPendingGuild;
    Mutex		  m_Mutex;
	bool		  m_Movement_thread_running;
    u32			  m_GlobalPlayerList[MAX_ONLINE_PLAYERS/32 + 1];
    UDP_Connection * m_UDPConnection;
    GMemoryHandler * m_GlobMemMgr;
	PlayerIDMap   m_PlayerLookup;
	u32			  m_last_group_tick;
};

#endif // _PLAYER_MANAGER_H_INCLUDED_
