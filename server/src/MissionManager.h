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
#ifndef _MISSIONMANAGER_H
#define _MISSIONMANAGER_H

#include "TalkTree.h"
#include "staticdata.h"
#include <vector>
class Player;
class Mission;
class Object;
struct SectorData;
class ObjectManager;
class ItemBase;
struct MOBData;
struct JobNode;

//TODO: store in SQL DB
enum
{
	JCAT_COMBAT = 1,
	JCAT_TRADE,
	JCAT_EXPLORE,
} ;

struct job_data
{
	int category;
	int type;
	char *description;
	char *text;
} ;

typedef vector<job_data*> vecJobData;

class JobManager
{
public:
	JobManager();
	~JobManager();

public:
	void InitialiseJobs();

	void GetDescription(unsigned char *buffer1, unsigned char *buffer2, long level, long type, long sector_id);
	long GetJobData(long type, ItemBase *item, Object *obj, long level, long sector_id);
	char *GetJobTitle(long type);
	MissionTree * SelectJob(long selection, long category);
	Object * GetJobObject(long level, long sector_id);
	long GetJobDescription(u8 *ptr, JobNode *jn);
	MOBData * GetMOB(long level);

private:
	long ParseJobInfo(u8 *ptr, char *mission_description, JobNode *jn);
	
	Object *FindPlanet(long level, long sector_id);

private:

	vecJobData m_JobData;
	long m_JobCount[3];
} ;

#if 0
/*typedef enum
{
    NULL_NODE = 0,
    TALK_NPC = 1, 
    GIVE_ITEM_NPC = 2, 
    RECEIVE_ITEM_NPC = 3, 
    FIGHT_MOB = 4, 
    TAKE_ITEM_TO_LOCATION = 5, 
    OBTAIN_ITEMS = 6, 
    OBTAIN_ITEMS_AT_LOCATION = 7, 
    PERFORM_ACTION_AT_LOCATION = 8, 
    PERFORM_ACTION_ON_OBJECT = 9,
    SCAN_OBJECT = 10
} node_type;*/

typedef completion_node_type node_type;

struct MissionReward
{
    char *description;
    int CreditReward;
    int ExploreReward;
    int CombatReward;
    int TradeReward;
    int FactionType;
    int FactionReward;
    int ItemRewardID;
    int HullUpgrade;
} ATTRIB_PACKED;

struct BranchDesc
{
    char *branch_text;
    short branch_dest;
} ATTRIB_PACKED;

typedef std::vector<BranchDesc*> BranchTextList;

struct TalkTreeNode
{
    short           node_id;
    short           talk_text_len;
    char           *talk_text;		// this needs deleting
    short           tree_dest;
    BranchTextList *branch_desc;
    bool            built_text;
} ATTRIB_PACKED;

typedef std::vector<TalkTreeNode*> TalkTreeList;
typedef std::vector<Object*> ObjectList;
typedef std::vector<char*> ObjNameList;

struct MissionNode
{
    node_type       type;
    char           *description;
    TalkTreeList   *talk_tree_list;
    long            item_id;
    short           item_quantity;
    short           npc_id;
    char           *location;
    Object         *obj_location;
    char           *object_name;
    char           *npc_name;
    long            sector_id;
    ObjectList     *object_list;		// this needs deleting
    ObjNameList    *object_name_list;    
    long            object_count;
    MissionReward  *reward;
} ATTRIB_PACKED;

typedef std::vector<MissionNode*> MissionNodeList;

class Mission
{
public:
    Mission();
	~Mission();

    bool        Initialize();
    bool        InitialiseNodes();

    //mission handling
    char      * GetMissionName()                { return m_MissionName; }
    char      * GetMissionSummary()             { return m_MissionSummary; }
    long        GetMissionID()                  { return m_MissionID; }
    long        GetNodeCount()                  { return (m_MissionNodeCount - 1); }

    node_type   GetNodeType(u32 node);
    long        GetNodeItem(u32 node);
    short       GetNodeItemQuantity(u32 node);
    Object    * GetONodeLocation(u32 node);
    Object    * GetONodeObject(u32 node, long index);
    char      * GetNodeLocation(u32 node);
    char      * GetNodeObject(u32 node);
    long        GetObjectIndex(u32 node, Object *obj);
    long        GetObjectNameIndex(u32 node, char *name, long complete);
    long        GetNodeObjectMask(u32 node);

    char      * GetNodeDescription(u32 node);
    char      * GetNPCName(u32 node);
    short       GetNPCID(u32 node);
    long        GetNodeSectorID(u32 node);
    char      * GetNodeTalkTree(u32 node, u32 tree_id, short *length, short *next_stage);
    bool        GetNodeReward(u32 node);
    void        GetNodeXPReward(u32 node, short *combat, short *explore, short *trade);
    long        GetNodeCreditReward(u32 node);
    void        GetNodeFactionReward(u32 node, short *faction, short *faction_reward);
    long        GetNodeItemReward(u32 node);
    char      * GetNodeRewardDescription(u32 node);
    long        GetNodeHullUpgradeReward(u32 node);
    long        GetLevelRequirement()           { return m_LevelRequirement; };

    void        CheckMissionStage(Player *p, u32 node);
    void        CompleteMissionStage(Player *p, bool success, u32 node);
    void        ForfeitMission(Player *p);

    //building missions
    void        AddNewNode(MissionNode *node);
    void        SetMissionName(char *name);
    void        SetMissionSummary(char *summary);
    void        SetMissionID(long mission_id)   { m_MissionID = mission_id; }
    void        SetLevelRequirement(long level) { m_LevelRequirement = level; }

private:
    ObjectManager *GetObjectManager(long sector_id);

    void        BuildTalkTreeNode(TalkTreeNode *node);

private:
    MissionNodeList m_MissionNodes;
    char          * m_MissionName;
    char          * m_MissionSummary;
    long            m_MissionID;
    long            m_MissionNodeCount;
    long            m_LevelRequirement;
};

typedef std::vector<Mission*> MissionList;

struct PlayerMission
{
    long        mission_id;
    short       stage;
    short       player_mission_id;       
};

/*class MissionManager
{
public:
	MissionManager(void);
	~MissionManager(void);

	bool Initialize();

public:
	bool AssignMission(Player *p, int mission_id);
	bool ClearMission(Player *p, int mission_id);
	bool ForfeitMission(Player *p, int mission_id);

	bool MissionReward(Player *p, int mission_id);

	bool MissionTalkTree(Player *p);
    void CheckMissionCondition(Player *p);

private:
	MissionParser m_MissionParser;
	MissionParser::MissionList *m_missionList;
};*/
#endif

#endif