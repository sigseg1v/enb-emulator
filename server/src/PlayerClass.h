// PlayerClass.h
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

#ifndef _PLAYER_CLASS_H_INCLUDED_
#define _PLAYER_CLASS_H_INCLUDED_

#define MAX_NUMBER_OF_SPACE_SECTORS 100
#define MAX_NUMBER_OF_GALAXY_OBJECTS 65536 //this refers to the database UID of the object, which can't change. We could use a std::map here but this method is quicker and guaranteed static
#define MAX_MISSIONS 12
#define ATTRIBUTE_ENABLE 0x00
#define ATTRIBUTE_DIS_INGROUP 0x01 //disabled - 'already in group'
#define ATTRIBUTE_DIS_TOOFAR 0x02 //disabled - 'Too Far'

#define UDP_BUFFER_SEND_SIZE 16384

#define CIRCULAR_PACKET_ARRAY_SIZE 16
#define PERIODIC_CACHE_SEND_SIZE 512 //max UDP size without fragmentation for older ISP's
#define PERIODIC_CACHE_SEND_SIZE_OPT 1400 //newer max UDP size for modern ISP's
#define MAXIMUM_PACKET_CACHE 10240
#define TALKTREE_BUFFER_SIZE 1024

#define	MAX_WEAPON_FIRE_PER_TICK 10   // maximum displayed weapon fire per tick

#define MAX_NAVS_DECOS	768			  //this refers to how many navs and decos we can track the ranges from for the arrival stuff - any decos above that number won't get tracked.
#define TRIGGER_RANGE_1	30000
#define TRIGGER_RANGE_2	10000
#define TRIGGER_RANGE_3	2500

#define MAX_MISSION_ID 1000 //1000 missions for now
#define NUMBER_OF_CHANNELS	32
#define INVALID_CHANNEL		(NUMBER_OF_CHANNELS-1)
#define MAX_FRIEND_LIST		100
#define MAX_IGNORE_LIST		100

#define RESEND_ELEMENTS		20

#define PLAYER_NODE_AVAILABLE -1

#define LAST_LOGIN_STAGE 20

//#define TEST_CREATE 1

#include "Net7.h"
#include "AuxClasses/AuxPlayerIndex.h"
#include "AuxClasses/AuxShipIndex.h"
#include "AuxClasses/AuxManufacturingIndex.h"
#include "CMobClass.h"
#include "CMobEquippable.h"
#include "MissionManager.h"
#include "ItemList.h"
#include "MessageQueue.h"
#include "CMobBuffs.h"
#include "Abilities/AbilityBase.h"

class Player;
class Stats;
struct StaticObjectPacket;
struct JobNode;

typedef std::vector<JobNode*> vecJobList;
typedef std::vector<Player*> PlayerList;

#define GRAIL_AFFINITY (1<<0) //TODO: integrate this into equipable, we need a custom item handling system

enum experience_type { XP_COMBAT, XP_EXPLORE, XP_TRADE };

typedef struct _st_resend
{
	u8	*data;
	u16 length;
	u32 packet_num;
	u32 message;
} st_resend;

typedef enum
{
	//Basic ship values
	ID_SCAN_RANGE,
	ID_IMPULSE,
	ID_SIGNATURE,
	ID_TURN_RATE,
	ID_MASS,

	//weapon damage
	ID_BEAM_DAMAGE,
	ID_PROJ_DAMAGE,
	ID_MISSILE_DAMAGE,

	//Weapon damage modifiers
	ID_BEAM_MULTIPLIER,
	ID_PROJ_MULTIPLIER,
	ID_MISSILE_MULTIPLIER,
	
	//Warp things
	ID_WARP_ENERGY,
	ID_WARP_RECOVERY,

	//weapon accuracy
	ID_BEAM_ACCURACY,
	ID_PROJ_ACCURACY,
	ID_MISSILE_ACCURACY,

	//deflects
	ID_IMPACT_DEFLECT,
	ID_EXPLOSIVE_DEFLECT,
	ID_PLASMA_DEFLECT,
	ID_ENERGY_DEFLECT,
	ID_EMP_DEFLECT,
	ID_CHEMICAL_DEFLECT,
	ID_PSIONIC_DEFLECT,

	//Abilities/Skills
	ID_REACTOR_RECHARGE_RATE,
	ID_SHIELD_RECHARGE_RATE,
	ID_CRITICAL_TARGETING,
	ID_DAMAGE_CONTROL,
	ID_WARP_SPEED,
	ID_NEGOTIATE,
	ID_COMBAT_TRANCE,
	ID_SCAN,
	ID_NAVIGATE_IMPULSE,
	ID_WARP_RECOVERY_NAV,
	ID_PROSPECT,

	//Tractor Beam
	ID_TRACTORBEAM_RANGE,
	ID_TRACTORBEAM_SPEED,

	// add more here

	MAX_BASE_STAT_IDS // MUST BE LAST ITEM!
} BASE_STAT_IDS;

typedef enum
{
	ITEMLIST_CARGO = 1, 
	ITEMLIST_VAULT, 
	ITEMLIST_NPC
} item_send;

struct VerbSlot  //this structure details a verb storage slot
{
    short       verb_id;
	bool	    active;
    float       activate_range;
} ATTRIB_PACKED;

struct MissionDataNode
{
	//int ID;
	int	Level;
	int Sponsor;
	ItemBase *Item;
	Object *Obj;
	Object *Mob;
} ;

class Player : public CMob
{
    friend class Equipable;
	friend class Stats;

public:
    Player(long object_id);
    Player();
    virtual ~Player();

    void        SetGroupID(long group_id)       { m_GroupID = group_id;}
    long        GroupID()                       { return (m_GroupID); }
	bool		GetInGroupFormation();
	Player		*GetGroupLeader();
	void		SetAcceptedGroupInvite(bool ac) { m_AcceptedGroupInvite = ac; }
	bool		AcceptedGroupInvite()			{ return (m_AcceptedGroupInvite); }

    long        CharacterID()                   { return (m_CharacterID); }
    void        SetCharacterID(long char_id);
    long        CharacterSlot()                 { return (m_CharacterSlot); }
    void        SetCharacterSlot(long slot)     { m_CharacterSlot = slot; }
    AuxPlayerIndex  *PlayerIndex()              { return (&m_PlayerIndex); }
    AuxShipIndex    *ShipIndex()                { return (&m_ShipIndex); }
    AuxManufacturingIndex *ManuIndex()          { return (&m_ManuIndex); }
    long        ManuID()                        { return (GameID() | MANU_TAG); }
    Group      *GetGroup()                      { return (m_Group); }
    void        SetGroup(Group *group)          { m_Group = group; }
	int			GuildID()						{ return m_GuildID; }
	void		SetName(char *name);

    //Helper methods for data
    long        CombatLevel()                   { return (PlayerIndex()->RPGInfo.GetCombatLevel()); }
    long        ExploreLevel()                  { return (PlayerIndex()->RPGInfo.GetExploreLevel()); }
    long        TradeLevel()                    { return (PlayerIndex()->RPGInfo.GetTradeLevel()); }
    long        TotalLevel()                    { return (CombatLevel() + ExploreLevel() + TradeLevel()); }
    long        Race()                          { return (m_Database.ship_data.race); }
    long        Profession()                    { return (m_Database.ship_data.profession); }
    long        ClassIndex()                    { return (Race() * 3 + Profession()); }
    long        AdminLevel()                    { return (htonl(m_Database.info.admin_level)); }
	long		GetSector()						{ return PlayerIndex()->GetSectorNum(); }
	void		SetAdminLevel(long level)		{ m_Database.info.admin_level = htonl(level); }

    long        FromSector()                    { return m_FromSectorID; }
	long		StargateDestination()			{ return m_StargateDestination; }
	void		SetStargateDestination(long s)	{ m_StargateDestination = s; }


    float        TractorRange();
    long        ProspectRange();

    void        SetDebugPlayer()                { m_DebugPlayer = true; }
    bool        DebugPlayer()                   { return (m_DebugPlayer); }

    void        ResetPlayer();
    void        SectorReset();
    void        Remove();
    void        FirstLogin();
    void        SendMOTD();
    void        SetAccountUsername(char *name);
    char       *AccountUsername()               { return (m_AccountUsername); }

	void		SetGameIndex(u32 game_index)	{ m_GameIndex = game_index; }
	u32			GetGameIndex()					{ return m_GameIndex; }

    void        SetDatabase(CharacterDatabase &database);
	bool		IsSubscribed(long channel_id)	{ if (channel_id < NUMBER_OF_CHANNELS) return m_ChannelSubscription[channel_id]; else return false; }

    void        MarkForRemoval();
    bool        ToBeRemoved()                   { return (m_Remove); }
	void		SetLoginStage(long stage)		{ m_LoginStage = (u8)stage; }
	long		GetLoginStage()					{ return (long)m_LoginStage; }
	void		HandleLoginStage2();
	void		HandleLoginStage3();
	void		IssueTCPBuffer(long max_cycles = 5);
	void		CompleteLogin();
	bool		WaitForLoginAck(long stage);
	void		SetLoginAck(long value)			{ m_LoginAckReceived = value; m_LoginAckCounter = 0; }
	void		SendLoginStageConfirm(long stage);
	void		HandleLoginAckReturn(unsigned char *data);

    long       *ObjectRangeList()               { return (m_ObjRangeList); }
    long       *ResourceSendList()              { return (m_ResourceSendList); }
    long       *ExposedNavList();   
	long       *ExploredNavList()				{ return (m_NavsExplored); }
	long	   *EffectNavList()					{ return (m_NavEffects); }
	void		ResetRangeLists();
	void		ExposeDecosOn(Object *obj);
	void		ExposeDecosOff(Object *obj);
	u32		   *GetSectorPlayerList();

	void		SetDistressBeacon(bool Status);
	void		TowToBase();

	void		AddRacialBuff();
	void		AddGroupRacialBuff(short count, short class_index);
	void		GetRacialBuff(Buff& RacialBuff, short count, short class_index);

    CharacterDatabase *Database()               { return (&m_Database); }

	void	    SendVaMessageS(const char *msg);
	void	    SendVaMessage(char *msg, ...);
    void        SendVaMessageC(char colour, char *msg, ...);
	void		SendMessageStringToGroup(char *msg, char color=4, bool log=true);
    void        SendMessageString(char *msg, char color=5, bool log=true);

    void        SetProspecting(bool p)          { m_Prospecting = p; };
    bool        Prospecting()                   { return (m_Prospecting); };

    bool        HijackObject(Object *obj);
	char	  * GetHijackeeName();

    void        SetMyDebugPlayer(Player *p)     { m_MyDebugPlayer = p;}
    void        DebugPlayerDock(bool flag);

    //Connection Methods
    void        SetUDPConnection(UDP_Connection *conn)      { m_UDPConnection = conn; }
    long        PlayerIPAddr()                  { return (m_Player_IPAddr); }
    void        SetHandoffReceived(bool value)  { m_UDPHandoffReceived = value; }
    void        WaitForHandoffReceived();
    void        LoginFailed();
    void        PacketCache();
    void        SendPacketCache();
    void        CacheOpcode(short opcode, unsigned char *data, u16 length);
	void		CommitCacheToQueue();
	void		PulsePlayerInput();
	void		FinishLogin(bool udp);
	bool		CheckQueueOverloading();

    //starbase methods
    void        SendStarbaseAvatarList();
    void        HandleStarbaseAvatarChange(long sector_id, StarbaseAvatarChange *change);
    void        HandleStarbaseRoomChange(long sector_id, StarbaseRoomChange *change, int aflag=1);    
    void        BroadcastPosition();
    void        SendAvatarListToPlayer();
    void        SendStarbaseAvatarChange(Player *p);

    void        SetActionFlag(long flag)        { m_ActionFlag = flag; }
    long        ActionFlag()                    { return (m_ActionFlag); }
    bool        HavePosition()                  { return (m_HavePosition); }
    void        SetHavePosition()               { m_HavePosition = true; }

    void        LogDockCoords();                 

    char       *GetRank();

    //ship send methods
	void        SendShipData(Player *player_to_send_to, bool is_group_member = false);
	void		SendClickPacketTo(Player *player_to_send_to);
    void        SendSubparts(Player *player_to_send_to);
    void        SendShipColorization(Player * player_to_send_to, int count);
    void        SendPlayerInfo();
    void        SendShipInfo(long, long);
    short       GetHullNum();

    void        SendLoginShipData();
    void        SendLoginSubparts();
    void        SendLoginShipColorization(int count);

    //aux methods
	void	    SendAuxShip(Player * other = 0);
private:
	void	   _SendAuxShip(Player * other = 0);
public:

	void	    SendAuxShipExtended();

	void	    SendAuxPlayer();
	void	    SendAuxPlayerExtended();

	void	    SendAuxManu(bool TwoBitFlags = false);

    void        SetActive(bool flag);

    //position updates
    void        SendLocationAndSpeed(bool include_player, bool zeroupdate = false);
    void        SetPlayerUpdate(short n)        { m_SetUpdate = n; }
    short       PlayerUpdateSet()               { return (m_SetUpdate); }
	void		SendStartMovementRefresh();
	void		SendSlowingDown();
	void		SendStartMoving();

    //MVAS methods:
    u16			UpdatePositionFromMVAS(float *position, float *orientation, bool orientation_sent);
    bool        ReceivedMVAS()                  { return (m_ReceivedMVAS); }
    void        SetMVASIndex(long MVAS_index)   { m_MVAS_index = MVAS_index;}
    long        MVASIndex()                     { return (m_MVAS_index); }
    bool        UsingMVAS()                     { return (m_MVAS_index == -1) ? false : true; }
    u16			Frequency()                     { return (m_MVAS_frequency); }
    void        SetFrequency(u16 freq)			{ m_MVAS_frequency = freq; }
    unsigned char *GetUDPBuffer()               { return (m_UDPSendBuffer); }
    
    //Movement methods
    void        SetVelocity(float velocity);
    float       Velocity()                      { return (m_Velocity); }
    void        Move(int type);
    void        MoveToward(Object *obj, float speed);
	void		SetNoPlayerUpdate()				{ m_NoPlayerUpdate = true; };
    void        CalcNewPosition(unsigned long current_tick, bool turn = false);
    void        SetInSpace(bool in_space)       { m_InSpace = in_space; }
    bool        InSpace()                       { return (m_InSpace); }
    void        SectorLogin();
    void        CheckNavs();
    bool        Following()                     { return (m_FollowObject); }
    void        StoreDockingCoords(float *position, float *heading);
    bool        RestoreDockingCoords();
    void        SetOrient(float o)              { m_Orient = o; }
	float		CalculateSpeed();
	float		CalculateTurnRate();
	void		AdjustAndSetSpeeds(bool sendaux, bool immobilise);	// adjust speed for current modifiers and pass to aux
	void		SetDoubleSpeed(bool on);
	void		SetHalfSpeed(bool on);
	void		ResetMaxSpeed(bool increase);

    void        SetNearestNav(Object *obj)      { m_NearestNav = obj; }
    Object     *GetNearestNav()                 { return (m_NearestNav); }

    //warp methods
    void        SetupWarpNavs(short navs, long *target_id);
    void        PrepareForWarp();
    void        StartWarp();
    void        TerminateWarp(bool player_forced = false);
	void		TerminateWarpGroup(bool player_forced = false);
    void        UpdateWarpNavigation();
    void        FinalWarpReset();
    bool        WarpDrive()                     { return (m_WarpDrive); }
    void        SetWarp()                       { m_WarpDrive = true; }
    void        CheckWarpNavigation();
	bool		IsWarpCharging()				{ return (m_WarpCharing); }

    //rangelist handling
    void        UpdatePlayerVisibilityList();
    bool        PlayerInRangeList(Player *p_check);
    void        AddPlayerToRangeList(Player *p, bool is_group_member = false);
    void        RemovePlayerFromRangeList(Player *p);
    void        RemoveFromAllSectorRangeLists(); //this removes the presence of the player from all the lists in the current sector
	void		BlankRangeList(bool group = false); //this removes the player from visibility

    //FX sending
    void        SendTractorComponentRL(Object *obj, float decay, float tractor_speed, long article_id, long effect_id, long timestamp);
    void        SendResourceNameRL(long article_UID, char *raw_name);
    void        SendResourceRemoveRL(long article_UID);
    void        SendContrailsRL(bool contrails);
    void        SendEffectRL(long target_id, char *message, long effect_type, long effectUID, long timestamp, short duration = 0);
    void        SendToRangeList(short opcode, unsigned char *data, size_t length, bool weapon_fire = false);
    void        SendToGroup(short opcode, unsigned char *data, size_t length);
    void        SendToSector(short opcode, unsigned char *data, size_t length);
    void        SendObjectCreateRL(long article_UID, float scale, short basset, int type);
    void        SendRelationshipRL(long article_UID, long relationship, long is_attacking);
    void        SendObjectLinkedEffectRL(short bitmask, long UID, long effectID, short effectDID, long effect_time);
	void		SendEffect(ObjectEffect *object_effect);

    //XP
	long        CalculateXP(experience_type xp_type, short base, short difficulty_level, short player_level = -1, float spread_down = 25, float spread_up = 25);
    float       AwardXP(experience_type xp_type, char *prefix, long xp_gain, long group_bonus = -1, bool skip_debt = false);
    void        AwardCombatXP(char *message, long xp, long group_bonus = -1, bool skip_debt = false);
    void        AwardExploreXP(char *message, long xp, long group_bonus = -1, bool skip_debt = false);
    void        AwardTradeXP(char *message, long xp, long group_bonus = -1, bool skip_debt = false);
    void        AwardNavExploreXP(Object *obj);
    void        AddMOBDestroyExperience(short mob_level, char *mob_name);
	short       CalculateMOBXP(short mob_level);
	short		CalcMiningXP(short stack, short resource_techLevel);
	short		CalcAnalyzingXP(short item_techLevel);
	short		CalcRefineXP(short item_techLevel);
	long		CalcItemStackTradeXP(_Item* stack, u32 tradedItems = 0);

	void		AwardFaction(long faction_id, long faction_change, bool play_sound = false);

	void		RadiationDmg(bool enb);

    //inventory methods
    void        SendItemBase(long);
	bool		CanReceiveTradeItems(Player *trader);
	int			TradeSpaceUsed();
	int			CargoFreeSpace();
	long        CargoItemCount(long ItemID);
	long		VaultItemCount(long ItemID);
	long		EquipItemCount(long ItemID);
	long		TradeItemCount(long ItemID);
	bool		CanHaveAnotherOf(long ItemID);
	bool		IsPartialStackOf(long ItemID, float Quality);
    bool        CanCargoAddItem(_Item *);
    bool        CanCargoAddItem(long ItemID, u32 Stack = 1, float Quality = 1.0f);
    u32         CargoAddItemCount(long ItemID, float Quality = 1.0f);
    int         CargoAddItem(long ItemID, u32 Stack, u32 TradeStack = 0);
    int         CargoAddItem(_Item *);
    void        CargoRemoveItem(long ItemID, u32 Stack);
	static void	CheckStack(u32 MoveNum, _Item * From, _Item * To);
	void		AwardCreditsToGroup(u64 credits);
	void		AwardCredits(u64 credits, long XP_earned = 0, int messagetype = 0);
	long		FindFreeVaultSpace(long item_id, u32 stack);
	int         DamageTradeCargo(float damage);
	void		DamageEquipment(float hull_dmg,float hull_remaining, Object *mob);
	void		RestoreEquipmentStructure();
	void		RestoreEquipmentStructure(int index,float structure);
	bool		SendItemList();
	void		SetItemList(item_send send_type)		{ m_ItemSendType = send_type; m_ItemSendIndex = 0; }

	void		CompleteInstalls();

public:
    void        SetPrices();
    void        ClearPrices();
	bool		CheckForInstalls();
	void		FinishAllInstalls();

	// kyp-era stubs referenced from Connection.cpp / Equipable.cpp. tada-o
	// rewired or dropped these but kyp call sites still exist. Phase M
	// audit: silent no-ops were masking real gameplay bugs (e.g. Sculptor
	// devices not actually granting Prospect skill). Log loudly on hit so
	// any production occurrence shows up in the server log; promote to
	// real implementations in the equipable-modifiers rework.
	void		SetTCPTerminate()				{ LogMessage("STUB: Player::SetTCPTerminate() called — kyp-era; no behavior wired"); }
	void		ChangeProspectSkill(float v)	{ LogMessage("STUB: Player::ChangeProspectSkill(%.3f) called — kyp-era; no behavior wired", v); }
	void		AddScanSkill(float v)			{ LogMessage("STUB: Player::AddScanSkill(%.3f) called — kyp-era; no behavior wired", v); }
	void		ChangeTractorBeamSpeed(float v)	{ LogMessage("STUB: Player::ChangeTractorBeamSpeed(%.3f) called — kyp-era; no behavior wired", v); }
    //save/load status
	void		LoadGMItems();
	void		SaveWarnLvl(long WarnChar, long warn_inc, char *WMsg);
    void        SavePosition();
    bool        LoadPosition();
    bool        ReadSavedData();
    void        ReInitializeSavedData();
	void		ReloadSavedData();
	void		ReadEquipment();
    void        SaveData(bool reset_data = false);
    void        SaveDatabase();
	void		SavePetition(unsigned char *data, short bytes);
	void		UpdateDatabase();
    bool        WriteCompletedMissions(FILE *f);
    bool        ReadCompletedMissions(FILE *f);
	void		SaveNewSkillLevel(long skill_id, long skill_level);
	void		SaveNewRecipe(long item_id);
	void 		SaveManufactureAttempt(long item_id, float quality);
	void		SaveInventoryChange(long slot);
	void		SaveVaultChange(long slot);
	void		SaveTradeChange(long slot);
	void		SaveXPBarLevel(long xp_type, float xp_bar);
	void		SaveCreditLevel();
	void		SaveEquipmentChange(long slot, _Item *item);
	void		SaveAmmoChange(long slot, _Item *ammo);
	void		SaveAdvanceMission(long slot);
	void		SaveMissionFlags(long slot);
	void		SaveAdvanceLevel(long xp_type, long level);
	void		SaveHullLevelChange(float new_hull_level);
	void		SaveHullUpgrade();
	void		SaveFactionChange(long faction_id, float new_value, long faction_order = -1);
	void		CreatePositionSave();
	void		SaveRemoveMission(long mission_id);
	void		ResetAllMissions();
	void		CompleteMission(long mission_id, long completion_flags);
	void		WipeCharacter();
	void		WipeFactions();
	void		DeleteAllAvatarRecords();
	void		SaveDiscoverNav(long object_uid);
	void		SaveExploreNav(long object_uid);
	void		LoadExploredNavs(sql_query_c *query);
	void		SetupFactions(sql_query_c *query, bool force);
	void		SaveSkillPoints();
	void		LoadMissionStatus(sql_query_c *query);
	void		LoadMissionCompletions(sql_query_c *query);
	void		SaveAmmoLevels();
	void		SaveRegisteredStarbase();
	void		SaveEnergyLevels();
	void		SetHullUpgrade();
	void		SaveFriendsList(char *name, bool add);
	void		SaveIgnoreList(char *name, bool add);
	void		SaveAudioWarnLvl();
	void 		SaveXPDebt();
	void		SaveCargoAndVault();
	void 		SaveGuildId(short rank);

	// Save database info
	void		SaveLogout();
	void		SaveLogin();

    //skills
    void        HandleSkillAction(unsigned char *data);
	void		SkillUpdateStats(int SkillID);
    void        UpdateSkills();
    void        UpgradeSkill(int);
    void        LevelUpForSkills();
	void		SkillsList();
    void        ResetWeaponMounts();
	void		ResetDeviceMounts();
	long		ConvertAbilityToBaseSkill(long &level, long ability);
	
	int			RespecSkill(int SkillID);  //Modifies the skill data of one skill and updates player's skill point total.
	int			RespecSkills(bool callForward);  //reclaims skill points from skills.  If callForward, does not modify engine shield or reactor. Does a batch respec.
	int			RespecOneSkill(int SkillID); //atomically respecs one skill
	int			CountSpentPoints();
	
	// Abilities
	void		HandleSkillAbility(unsigned char *data);
	int			GetCounterMeasuresLevel()					{ return m_CounterMeasuresLevel; };
	void		SetCounterMeasuresLevel(int NewCounterMeasures)		{ m_CounterMeasuresLevel = NewCounterMeasures; };
	
	void		ResetCombatTrance();
	void        RefreshCombatTrance();
	
    //sector login camera
    void        SetLoginCamera(long index, long obj_id)     { m_CameraSignal = index; m_CameraID = obj_id; }
    void        SendLoginCamera();

    long	    Hijackee()                                  { return (m_Hijackee); }
    void        SetHijackee(long obj_id)                    { m_Hijackee = obj_id; }

    //Combat & Damage
	float		DamageObject(long game_id, long damage_type, float damage, long inflicted);
    void        AttackMusicUpdate(bool update, long mob_id);
    void        FireAllWeapons();
    void        RemoveHull(float hull_dmg, CMob *mob);
    void        ImmobilisePlayer();
    void        RemobilisePlayer();
	void		SelfDestruct();
	void		SetSelfDestructed(bool update);
	bool		GetSelfDestructed()							{ return m_SelfDestructed; };
    bool        FireEnergyCannon(ItemInstance *item);
    short       AttacksThisTick()                           { return m_AttacksThisTick; }
    void        ResetAttacksThisTick()                      { m_AttacksThisTick = 0; }
    void        IncAttacksThisTick()                        { m_AttacksThisTick++; };
	short		CalcMissChanceVersus(int subcat, short mob_level);
    float       CalcDamage(int weapon_damage, int subcat, bool *critical, int mob_level);
	bool		HasCombatImmunity()							{ return m_CombatImmunity; };
	bool		HasScanInvisibility()						{ return m_ScanInvisible; };
	void		RepairShip();
	bool		GetIsIncapacitated()						{ return ShipIndex()->GetIsIncapacitated(); }
	void		CalculateXPDebt();
	void 		ForgiveXPDebt(time_t logout);
	time_t 		ConvertStringToTimeT(char *datetime);
	u32			CalcLastLoginDiff(char *datetime);
	void 		JumpStart(float hull_repair, float level);
	void		SwitchOffAutofire();

    // energy and shield
    float       BaseEnergyRecharge();
    float       BaseShieldRecharge();
	class AuxPercent *ShieldAux()							{ return &ShipIndex()->Shield; }
	float		GetMaxShield()								{ return ShipIndex()->GetMaxShield(); }
    float		GetHullPoints()								{ return ShipIndex()->GetHullPoints(); }
	void		SetHullPoints(float points)					{ ShipIndex()->SetHullPoints(points); }
    float		GetMaxHullPoints()							{ return ShipIndex()->GetMaxHullPoints(); }
    float	    GetEnergy();
	float	    GetEnergyValue();
	float		GetMaxEnergy()								{ return ShipIndex()->GetMaxEnergy(); }
	float	    DrainReactor(unsigned long, float);	
    void        RemoveEnergy(float energy, long recharge_time = 0);
    void        EnergyUpdate(unsigned long, float, float);
	void	    RechargeReactor(float relief = -1.0f);
	void        RecalculateEnergyRegen(float MaxEnergy=0.0f, float RechargeEnergy=0.0f, bool pullData=true);

	bool		IsShieldStalled() {return (m_ShieldRecharge > GetNet7TickCount());}
    //manufacturing
    void        HandleManufactureTerminal(unsigned char *data);
    void        HandleManufactureCategorySelection(unsigned char *data);
	bool		AnalyseDismantleSetItem(_Item *Source);
    void        HandleManufactureSetItem(unsigned char *data);
    void        HandleRefineSetItem(unsigned char *data);
    void        HandleManufactureAction(unsigned char *data);
    void        HandleManufactureLevelFilter(unsigned char *data);
    void        ManufactureTimedReturn(long);
	bool		AllowManufacture(ItemBase * iBase);
	int			GetManufactureSkill(ItemBase * iBase, float *buff_bonus);
	int			CountKnownRecipesOfSameType();
	float		CalculateBuiltItemQuality(float *min_quality,float *max_quality);
	float		CalculateBuiltItemQuality(int skill,int tech_level,int difficulty,int trade_level,int recipes,bool show_calcs,float *min_quality,float *max_quality,float buff_bonus);
	float		CalculateAverageComponentQuality();
	long		GetComponentTradeStackXP();
	void		TestBuildQualities();

    //missions
    long        AssignMission(long mission_id); //the mission id here is the 'Mission ID =' from the XML/DB.
    bool        CheckMissions(long object_id, long item_id, long npc_id, completion_node_type completion_type);
    bool        CheckForNewMissions(long object_id, long param_1, long npc_id);
    void        SetGrailAffinity(bool flag)             { flag ? m_MissionFlags |= GRAIL_AFFINITY : m_MissionFlags &= (0xFFFFFFFF - GRAIL_AFFINITY); }
    bool        GrailAffinity()                         { return (m_MissionFlags & GRAIL_AFFINITY); }
    bool        CheckMissionValidity(long target);
    void        MissionObjectVerb(Object *obj, long stage_type);
    void        ShipUpgrade(long upgrade);
    void        MissionDismiss(long mission_id, bool forfeit_pressed);
	void		CheckMissionMOBKill(long MOB_id);
	void		CheckMissionSkillUse(long base_skill, long level);
	void		CheckMissionArrivedAt();
	void		CheckMissionArrivedAt(Object *obj);
	void		CheckMissionRangeTrigger(Object *obj, long range);
	void		MissionVerbDisplay(Object *obj);
	bool		CheckCurrentMissionStage(long type, long data, long count, bool check = false);
	bool		MissionStageNeeded(long completionIndex, long mission_data);
	int			ProcessConversation(int i, long param_1, completion_node_type completion_type, AuxMission * am);
	void		AcceptJob(JobNode *jn);

	void		CheckArrival();
	int			GetVenderBuyPrice(int ItemID);

    //verbs
    void        BlankVerbs();
    void        UpdateVerbs(bool force_update=false);
    bool        AddVerb(short verb_id, float verb_activate_range);
    void        OnTargeted(Player *player);
	void		WormHole(int sector_id);		// Wormhole to a sector

    //prospecting & looting
    void        LootItem(long slot, bool mined);
	bool		Looting() {return m_Looting;};
	void		SetLooting(bool answer) {m_Looting = answer;};
    void        MineResource(long slot);
    void        AbortProspecting(bool recharge_reactor, bool abort_both_beams);
    void        PullOreFromResource(Object *obj, long stack_val, long slot, long effect_UID, long incomplete);
    void        TakeOreOnboard(Object *obj, long article_UID, bool mined);
    long        GetCargoSlotFromItemID(int StartLocation, int ItemID);
    void        RemoveProspectNodes();

    //timing
    void        CheckEventTimes(unsigned long current_tick);
    void        PopulateTimedCall(TimeNode *this_node, broadcast_function func, long time_offset, Object *obj, long i1 = 0, long i2 = 0, long i3 = 0, long i4 = 0, char *ch = 0, float a = 0.0f);

    void        ChangeMountBoneName(long weapon_id, char *mount_name);

    //connection/message handling
    void        AddMessage(short opcode, short length, unsigned char *data);
    void        HandleClientOpcode(short opcode, short bytes, unsigned char *data);
    void        SetPlayerPortIP(short port, long ip_addr);

	bool		HandlePacketOptRequest(char *param);

	//faction interaction
	float		GetFactionStanding(Object *obj);

	//ship recustomization

	bool		RecustomizeShip(char *cmd);
private:
	// NPC Talk Trees
	bool		CheckTalkTree(int Selection);
	bool		NPCTradeItems();
	long		GenerateTalkTree(TalkTree *tree, int tree_node_id);
	u16			GetNodeFlags(TalkTree *tree, int tree_node_id);
	long		ParseTalkTokens(char *write_buffer, char *input_buffer);
	void		SendPIPAvatar(long game_id, long avatar_id, bool actual_use, bool avatar_id_is_npc = false);
	void		DoVrixEncoding(char *message);

    //MVAS methods
    void        UpdateOrientation();
    void        UpdateLastPosition(unsigned long current_tick);

    //Movement methods
    bool        CheckUpdateConditions(unsigned long current_tick);
    void        CalcNewHeading(float tdiff, bool turn);
    float       CalcVelocity(float tdiff);
    void        SendToVisibilityList(bool include_player);
    void        UpdatePositionInfo();
    float       FollowTarget();
    void        StopFollowing();
	void		CheckArrivalTriggers();
    
    //warp methods
    void        BlankWarpNavs();
    void        InitiateWarpBroadcast();
    void        StartWarpBroadcast();
    void        SendEndWarpBroadcast();
    void        FreeWarpDrain();

    //skills
    long        SkillLevelRequirement(long);
    void        AddWeapon(long weapon_id);
    void        NeatenUpWeaponMounts();
	char *      GetBoneName(long weapon_id);
	int			Negotiate(int price, bool isDiscount, bool atVendor);  //negotiate prices



    //manufacturing
	void		BuildManufactureList();
    void        BuildRefineList();
    void        CheckItemRequirements();
    void        SetRefineIterations();
    void        ResetManuItems();
    bool        HasComponents(long = 1);
    void        RemoveComponentsFromInventory(long = 1);
	float		GetAnalyzeChance();
	float		GetDismantleChance();

    //missions
    void        AdvanceMission(long mission_slot, long stage); //internal mission is the player mission ref, 0 to 19.
    bool        NPCTalkTree(long mission_slot, long response, completion_node_type completion_type = NULL_NODE);
    void        ProposeMissionTree(long mission_id, long response);
	void		ProposePushMissionTree(long mission_id, long response);
    void        RemoveMission(long mission_slot);
    void        EndStageReward(long mission_id, long stage, long slot = -1);
	bool		EndStageItemCheck(MissionTree *mTree, long stage);
    bool        CheckEndStageConditions(AuxMission *m);
	bool		CheckStageCompletionNodes(long mission_id, long stage, long npc_id, Object *obj = (0), long param_data = 0, completion_node_type completion_type = NULL_NODE);
    long        GetSlotForMission(long mission_id, bool job = false);
	bool		CheckMissionCompleted(long mission_id);
	bool		CheckMissionStarted(long mission_id);
	int         UpdateMissionData(int offset, int length, long &missionData);
	bool		CheckSpaceNPC(long response);
	void		AddStageExpoDebrief(CompletionNode *cNode, long param);
	void		AddDebrief(char *msg, ...);
	void		DisplayMissionRequirements(MissionTree *mTree);
	void		CheckMissionStageSounds(long mission_id, long stage);
	bool		InsertJobIntoQueue(long mission_slot, JobNode *jn);
	void		RemoveJobFromQueue(long mission_slot);
	MissionDataNode *GetMissionData(long mission_slot);
	long		GetJobData(long mission_slot);
	float		GetJobRewardMultiplier(long mission_slot);
	void		AwardJobFaction(long mission_slot);
	void		DoJobEnvironmentals(JobNode *jn, long mission_slot);

    //verbs
    void        CloseInterfaceIfRequired(short verb_id);

	//private range list
	void		RemovePlayerFromView(bool keep_group_view = false);

    //prospecting & looting
	void		GetOreRequirements(ItemBase *item, long stack, float *energy_per_ore, float *time_per_ore);
    float       GetEnergyPerOre(ItemBase *item);
    bool        TractorInUse();
    u32         TractorCompletion();
    void        UseTractorBeam(Object *obj, short stack_val, ItemBase * contents, long effect_UID, bool mined);
    short       CalcMiningXP(int stack, short level);
    void        AddMiningExploreExperience(short XP_earned, short stack, char *raw_name);
    bool        CheckMiningConditions(long slot, float reactor_energy);
    bool        CheckLootConditions(long slot, bool mined);
    bool        CheckInventoryForRoom(long ItemID, int Stack);
	void		ResourceEmptyXP(Object *obj);

    //connection methods for incomming opcodes (from client)
    void        HandleLogin(unsigned char *data);                          // opcode 0x02
    void        HandleStartAck(unsigned char *data);                       // opcode 0x06
	void	    HandleTurn(unsigned char *data);           				// opcode 0x12
	void    	HandleTilt(unsigned char *data);	            			// opcode 0x13
    void        HandleMove(unsigned char *data);                           // opcode 0x14
    void        HandleRequestTarget(unsigned char *data);                  // opcode 0x17
	void	    HandleRequestTargetsTarget(unsigned char *data);			// opcode 0x18
    void        HandleDebug(unsigned char *data);                          // opcode 0x1A
	void	    HandleInventoryMove(unsigned char *data);					// opcode 0x27 
	void	    HandleInventorySort(unsigned char *data);					// opcode 0x28
	void	    HandleItemState(unsigned char *data);                      // opcode 0x29
    void        HandleAction(unsigned char *data);							// opcode 0x2C
    void        HandleAction2(unsigned char *data);							// opcode 0x2D
    void        HandleOption(unsigned char *data);                         // opcode 0x2E
    void        HandleClientChat(unsigned char *data);                     // opcode 0x33
	void		HandleSlashCommands(char *Msg);
    void        HandleMasterJoin(unsigned char *data);                     // opcode 0x35
    void        HandleRequestTime(unsigned char *data);                    // opcode 0x44
    void        HandleStarbaseRequest(unsigned char *data);                // opcode 0x4E
	void		HandleRecustomizeShipDone(unsigned char *data);
	void		HandleRecustomizeAvatarDone(unsigned char *data);
	void	    HandleSkillStringRequest(unsigned char *data);				// opcode 0x51
    void        HandleSelectTalkTree(unsigned char *data);                 // opcode 0x55
	void	    HandleEquipUse(unsigned char *data);						// opcode 0x5D
    void        HandleVerbRequest(unsigned char *data);                    // opcode 0x5A
	void        HandleChatStream(unsigned char *data);						// opcode 0x5E
    void        HandleGlobalConnect(unsigned char *data);                  // opcode 0x6D
    void        HandleGlobalTicketRequest(unsigned char *data);            // opcode 0x6E
    void        HandleDeleteCharacter(unsigned char *data);                // opcode 0x71
    void        HandleCreateCharacter(unsigned char *data);                // opcode 0x72
	void	    HandleMissionForfeit(unsigned char *data);					// opcode 0x86
	void	    HandleMissionDismissal(unsigned char *data);				// opcode 0x87
    void        HandlePetitionStuck(unsigned char *data, short bytes);      // opcode 0x88
    void        HandleIncapacitanceRequest(unsigned char *data);           // opcode 0x8D
    void        HandleGalaxyMapRequest();                                  // opcode 0x98
	void	    HandleWarp(unsigned char *data);							// opcode 0x9B
    void        HandleStarbaseAvatarChange(unsigned char *data);           // opcode 0x9D
    void        HandleStarbaseRoomChange(unsigned char *data);             // opcode 0x9F
    void        HandleTriggerEmote(unsigned char *data);                   // opcode 0xA1
    void        HandleClientChatRequest(unsigned char *data);              // opcode 0xA3
    void        HandleLogoffRequest(unsigned char *data);                  // opcode 0xB9
	void	    HandleCTARequest(unsigned char *data);						// opcode 0xBC
    void	    HandleActionResponse(unsigned char *data);
    void        Contrails(long player_id, bool contrails);
	void		HandleFireMOBWeapon();

    void        SendLogoffConfirmation();               // opcode 0xba
    void        SendTalkTreeAction(long action);        // opcode 0x56
	void		SendClientChatList(long listtype, char **names, char **sector, long number1, long number2, char *channel="");
	void		GetPostFix(char *FName, int length);

    void        ActivateProspectBeam(long player_id, long target_id, char *message, short effect_type, long effectUID, long timestamp, short effect_time);

    bool        HandleMobCreateRequest(char *param);
    bool	    HandleObjCreateRequest(char *param);
    bool	    HandleFetchRequest();
    bool	    HandleFaceRequest(long Target);
	bool		HandleFaceMeRequest(long Target);
	//bool		HandleMenaceTest(char *param);
	//bool		HandleFaceAwayFromMeRequest(long Target);
	bool		HandlePanRequest(char *param, int axis); 
	bool		HandleRotateRequest(char *param, int axis);
	bool		HandleLevelOutRequest(long Target);
	bool		HandleCommitRequest(long Target);
	bool		HandleChangeFieldRequest(char *param, int option);
	bool	    HandleGotoRequest();
    bool        HandleObjectHijack();
    bool	    HandleKick(char *param);
	bool		HandleInvis(char *param);
    bool	    HandleMoveRequest(char *param);
    bool        HandleOrientationRequest(char *param);
    bool	    HandleEulerOrientationRequest(char *orientation);	// used by '/euler' command
    bool	    HandleRenderStateRequest();
    void        HandleReleaseHijack();
	bool	    HandleRenderStateInitRequest(char *render_state);
	bool	    HandleRenderStateActivateRequest(char *param);
	bool	    HandleRenderStateActivateNextRequest(char * param);
	bool	    HandleRenderStateDeactivate();
	void	    HandleSendVerbRequest(char *param);
    bool	    HandleRangeRequest();
    bool        HandleScaleRequest(char *param);
	bool		HandleSpinRequest(char *param);
	bool		HandleNavChangeRequest(char *param, int option);
	bool		HandleTiltRequest(char *param);
	bool		HandleBassetRequest(char *param);
    bool	    HandleWormholeRequest(char *sector);	// used by '/wormhole' command
	bool		HandleAggroSetting(char *param);
	bool		HandleBaseItemListCreate();
	bool		FindSectorFromName(char *param);
	bool		HandleRestartSectorComms(char *param);
	bool		HandleObjectDestruction();

	void		HandleSetTurrets();
	void		HandleSetRespawns();

	void		DisplayClassFactionStanding();
	bool		DisplayPlayerFactionStanding(char *username);
	bool		EditFactionStanding(char *param);
	bool		EditPlayerFactionStanding(char *param);

	void		SetInvisible(bool invis)										{ m_TagInvisible = invis; }
	void		SetWormholed(bool wormholed)									{ m_Wormholed = wormholed; }
	bool		GetWormholed()													{ return m_Wormholed; }

    void	    ProcessConfirmedActionOffer();

    long        BuildCachePacket();
    long        BuildCachePacket(long index);
    short       ReadBuffer(u8 *buff, long &read_ptr_index);
    void        ReSendOpcodes(unsigned char *data);                         // opcode 0x2017
	void		ReSendLoginOpcode(long packet_num);							// opcode 0x2020

	void		AddFriend(char *name);
	void		RemoveFriend(char *name);
	void		ListFriends();
	void		AddIgnore(char *name);
	void		RemoveIgnore(char *name);
	void		ListIgnores();
	bool		DevGMChannel(char *channel);

	//helper hacks for devs
	void		SetOverrideFaction(bool f_override)								{ m_Faction_Override = f_override; }

	// guild
	void		HandleGuildCommand(char *param);
	void 		HandleCreateGuild(char *name);
	void 		HandleListAllGuildMembers();
	void 		HandleCurrentPermissions();
	void 		HandleGuildMOTD(char *motd);
	void 		HandlePromoteMember(char *name, bool confirmed);
	void 		HandleDemoteMember(char *name, bool confirmed);
	void 		HandleRemoveMember(char *name, bool confirmed);
	void 		HandleLeaveGuild(bool confirmed);
	void 		HandleDisbandGuild(bool confirmed);
	void 		HandleRecruitMember(char *data);
	void 		HandleRecruitMember2(Player *recruit, char accept);
	void 		HandlePublicStats(char *data);
	void 		HandleShowGuildStats(char *data);
	void 		HandleTagMember(char *data);
	void 		HandleMemberContributions(char *data);
	void 		HandleGuildRankNamesRequestClient(unsigned char *data);
	void 		HandleGuildSimpleClientSector(unsigned char *data);
	void		HandleGuildLeaderAcceptClient(unsigned char *data);
	void 		HandleRecruitAcceptClient(unsigned char *data);
	void		HandleGuildGMCommand(char *param);
	void 		HandleGMDisbandGuild(char *name, bool confirmed);
public:
	void 		SetupGuildInfo(int id, int rank, bool autoadd=false);
	void		SendGuildMOTD();
	void		SendGuildPlayerPermissions(long p1=0xFFFFFFFFL, long p2=0xFFFFFFFFL, long p3=0xFFFFFFFFL, long p4=0xFFFFFFFFL);
	void 		SendGuildMessage(long Type, char *OtherName="", char *GuildName="");
	void 		SendGuildSimpleSectorClient(long Type, char *OptionalParam="");
	void 		SendGuildRecruitConfirmSector(Player *recruiter, char *guild);
	bool		IsMyFriend(char *name);
	bool		IsIgnored(char *name);

	void		SendConfirmation(char * msg, int PlayerID, int Ability, int Confirmation = 2);
    void        SendResourceName(long resourceID, char *resource_name);
    void        SendHuskContent(Object *husk);
    void        SendHuskName(Object *husk);
    void        SendMobName(Object *mob);
    void        SendSimpleAuxName(Object *obj);
    void        SendAuxNameSignature(Object *obj);
    void        SendAuxNameResource(Object *obj);
	bool		GetInvisible()													{ return m_TagInvisible; }
	bool		GetVisiblityStatus()											{ return m_StatusToFriendsOnly; }
	bool		IsGating()														{ return m_Gating; }

    void        UnSetTarget(long GameID);
    void        SendOpcode(short opcode, unsigned char *data = (0), long length = 0, bool issue = false);
    void        SendStarbaseSet(char action, char exit_mode);
    void        SendClientSetTime(long TimeSent);
    void	    SendConfirmedActionOffer();
    void        SendObjectToObjectLinkedEffect(Object *target, Object *source, short effect1, short effect2, float speedup = 1.0f);
    void        SendClientSound(char *sound_name, long channel = 0, char queue = 0, long warninglevel = -1);
    void        PointEffect(float *position, short effect_id, float scale = 1.0f);
    void        SendObjectEffect(ObjectEffect *object_effect);
    void        SendClientType(long client_type);       // opcode 0x3c
    void        SendStart(long start_id);               // opcode 0x05
    void        SendSetBBox(float xmin, float ymin, float xmax, float ymax);
    void        SendSetZBand(float min, float max);     // opcode 0x2a
    void        SendNavigation(int game_id, float signature, char visited, int nav_type, char is_huge);
    void        SendCreate(int game_id, float scale, short asset, int type, float h=0.0, float s=0.0, float v=0.0);
    void        SendDecal(int game_id, int decal_id, int decal_count);
    void        SendNameDecal(Player *send_to);
    void        SendConstantPositionalUpdate(long game_id, float x, float y, float z, float *orientation=NULL);
    void        SendFormationPositionalUpdate(long leader_id, long target_id, float x, float y, float z);
    void        SendSimplePositionalUpdate(long object_id, PositionInformation * position_info);
    void        SendPlanetPositionalUpdate(long object_id, PositionInformation * position_info);
    void        SendComponentPositionalUpdate(long object_id, PositionInformation * position_info, long timestamp=0);
    void        SendAdvancedPositionalUpdate(long object_id, PositionInformation * position_info);
    void        SendObjectToObjectEffect(ObjectToObjectEffect *obj_effect);
    void        SendActivateRenderState(long game_id, unsigned long render_state_id);
    void        SendInitRenderState(long game_id, unsigned long render_state_id);
    void        SendActivateNextRenderState(long game_id, unsigned long render_state_id);
    void        SendDeactivateRenderState(long game_id);
    void        OpenInterface(long UIChange, long UIType);
    void        SendSetTarget(int game_id, int target_id);
    void        SendPushMessage(char *msg1, char *type, long time, long priority);
    void        SetResourceDrainLevel(Object *obj, long slot);
	void		SetHuskDrainLevel(Object *obj, long slot);
    void        RemoveObject(long object_id);
    void        SendProspectAUX(long value, int type); //HAX! Remove when decyphered
    void        CreateTractorComponent(float *position, float decay, float tractor_speed, long player_id, long article_id, long effect_id, long timestamp);
    bool        CheckResourceLock(long object_id);
    long        CurrentResourceTarget();
    void        CloseInterfaceIfTargetted(long target_id);
    void        CloseInterfaceIfOpen();
    void        SendAttackerUpdates(long mob_id, long update);
    void        SendChangeBasset(ChangeBaseAsset *NewAsset);
    void        CheckObjectRanges();
	void		SendRemainingStaticObjs();
    void        SendRemoveEffect(int target_id);
    void        TradeAction(long GameID, int Action);
    void        SendResourceContentsAUX(Object *obj);
	void		ClearTradeWindowForBoth(Player *targetp);
    void        CancelTrade();
    void        SendResourceLevel(long target_id);
    void        SendCameraControl(long Message, long GameID);
    void        SendClientDamage(long target_id, long source_id, float damage, float modifier, long type, long inflicted = 0);
    void        Dialog(char *Stringd, int Type);
    void        ForceLogout();
    void        ChangeSectorID(long SectorID);
    void        OpenStargate(long object_id);
    void        CloseStargate(long object_id);
    bool        SendLoungeNPC(long StationID);
    void        SetManufactureID(long mfg_id);          // opcode 0x7f
    void        SendServerHandoff(long from_sector_id, long to_sector_id, char *from_sector, char *from_system, char *to_sector, char *to_system);
    void        SendNotifyEmote(long game_id, long emote);
    void        SendClientChatEvent(long Type, Player *Source, char *Channel="", char *Message="" , char *OtherPlayer="", char *NonPlayerSrc="");
	void		SendClientChatError(long reason, long type, char *player, char *channel="", char *other="");
    void        SendRelationship(long ObjectID, long Reaction, bool IsAttacking);
    void        SendGalaxyMap(char *system, char *sector, char *station);
    void        SetStartingPosition();
    void        SendPriorityMessageString(char *msg1, char *msg2, long time, long priority);
    void        SendWarpIndex(int index);
    void        SendCreateAttachment(int parent, int child, int slot);
	void		SendObjectFull(unsigned char *msg, int index, object_type ot = OT_NAV);
	void		SendFindMember(struct FindMember *players);

	void		HandleLocalGate(long destination, long source);
	void		ArriveAtLocalGate(long destination);

    bool        MatchOptWithParam (char *option, char *arg, char *&param, bool &msg_sent, bool allowNoParams = false);

    long        TryLoungeFile(long sector_id);
    void        SendDataFileToClient(char *filename, long avatar_id=0);
    void        SendDataFileToClientTCP(char *filename, long avatar_id=0);

	void		SetConfirmation(int x)						{ m_Confirmation = x; }
	bool		ConfirmationBusy()							{ return (m_Confirmation > 0); }
    void        WaitForAuxResponse();
    void        SetLoungeReady()                            { m_AuxWaiting = false; }
    void        SetNavCommence()                            { m_NavCommence = true; }
    bool        WaitForNavCommence();
	long		GetSectorNextObjID();
	SectorManager *GetSectorManager();
	ObjectManager *GetObjectManager();

	bool		GetStartStatus()							{ return m_SentStart; }
	bool		GetOverrideFaction()						{ return m_Faction_Override; }
	bool		GetProspectWindowOpen()						{ return m_ProspectWindow; }

	void		SetEnvironmentalEffect(Object *obj);
	void		UnSetEnvironmentalEffect(Object *obj);
	void		SetGWell(long ed);
	void		SetRadiation(long ed);
	long		GetGWell()									{ return m_GWell; }
	long		GetRadiation()								{ return m_Radiation; }
	void		SetIsCloaked(bool on)						{ ShipIndex()->SetIsCloaked(on); };
	bool		GetIsCloaked()								{ return ShipIndex()->GetIsCloaked(); };

private:	
	TalkTree        * m_CurrentTalkTree;		// Holds parsed data for talk tree
	StationTemplate * m_CurrentStation;			// Holds the Current Station data
	NPCTemplate		* m_CurrentNPC;				// Holds the current NPC Data

	int m_BaseID[MAX_BASE_STAT_IDS];			// Base Stat ID's

	typedef map<int, int> mapManu;
	typedef map<int, bool> mapCreate;			// temp for debugging 'Unknown' objects

	mapManu			m_ManuRecipes;

	// Conformation dialog
	int m_Confirmation;					// 0 = Not Busy 1 = Group 2 = Confromation
	int m_Confirmation_PlayerID;
	int m_Confirmation_Ability;

	Player *m_Recruiter;
	int m_GuildID;
    long m_GroupID;
    long m_CharacterID;    //this is the number used for account management (avatar_id)
    long m_CharacterSlot;  //this is the character's slot number in a user's account
	char m_NameBuffer[20]; //use this to stop stress on the string manager

    Group           * m_Group;
	bool              m_AcceptedGroupInvite;

	AuxPlayerIndex	  m_PlayerIndex;
	AuxShipIndex	  m_ShipIndex;
    AuxManufacturingIndex m_ManuIndex;
    CharacterDatabase m_Database;

    // shield and reactor
	unsigned long     m_LastReactorChange;

    //manufacturing
    unsigned int      m_NumFormulas;
    bool              m_Manufacturing;
	bool			  m_ItemsWaiting;
	float			  m_base_quality;
	int				  m_tradeStackXP;
	bool			  m_SentStart;
    ItemBase        * m_CurManuItem;

    //Misc
    bool              m_Remove; //setting this will remove the player at the next safe opportunity.
    bool              m_DebugPlayer;
    bool              m_FirstLogin;
	bool			  m_SpeedDoubled; // planet
	bool			  m_SpeedHalved;  // eg. cloak	
    char            * m_AccountUsername;
    short             m_SetUpdate;
    bool              m_InSpace;
    bool              m_Prospecting;
	bool			  m_Looting;
	bool			  m_TagInvisible;
    Player          * m_MyDebugPlayer;
    ItemList          m_ItemList;
    Object          * m_NearestNav;
    VerbSlot          m_Verbs[4];
    long              m_LoadFlags;
    short             m_AttacksThisTick;
    bool              m_LogDockCoords;
	bool			  m_SentDockPos;	//set this so we only send one position update while a player is docking for neatness
	bool			  m_SendDockPos;
	bool			  m_Faction_Override;
    MasterJoin        m_MasterJoin;
	long			  m_RegisteredSectorID;
	long			  m_WeaponsPerTick; //this keeps track of how many weapons FX have been sent per tick (100ms)
	u32				  m_BroadcastPositionTick; //used to split up the starbase broadcasts
	u16				  m_OpcodeResends;
	u8				  m_LoginStage;
	int				  m_CounterMeasuresLevel;
	u32				  m_GameIndex;
	u32				  m_JoinTime;

    float             m_DockCoords[3];
    float             m_DockHeading[3];
	u8				  m_PDAFactionID[32]; //keep track of PDA order vs faction ID
	bool			  m_SelfDestructed;

	// chat
	bool			  m_ChannelSubscription[NUMBER_OF_CHANNELS];
	char			  m_FriendNames[MAX_FRIEND_LIST][20];
	char			  m_IgnoreNames[MAX_IGNORE_LIST][20];
	short			  m_NumFriends;
	short			  m_NumIgnore;
	bool			  m_StatusToFriendsOnly;
	bool			  m_TellsFromFriendsOnly;
	
    //range lists and object ranging indicies
    long              m_ObjRangeList[MAX_OBJS_PER_SECTOR/32]; //array of objects which are currently marked as in range
    long              m_ResourceSendList[MAX_OBJS_PER_SECTOR/32];
	long			  m_NavEffects[MAX_NAVS_DECOS/32];

	long			  m_NavsExposed[MAX_NUMBER_OF_GALAXY_OBJECTS/32];
	long			  m_NavsExplored[MAX_NUMBER_OF_GALAXY_OBJECTS/32];
    bool              m_FoundAllSectorNavs[MAX_NUMBER_OF_SPACE_SECTORS];

	long			  m_NavRange_1[MAX_NAVS_DECOS/32];
	long			  m_NavRange_2[MAX_NAVS_DECOS/32];
	long			  m_NavRange_3[MAX_NAVS_DECOS/32];
	bool			  m_Arrival_Flag;

	//ship slots
	u8				  m_WeaponSlots;
	u8				  m_DeviceSlots;
	short			  m_ReplacementShipAsset;
	float			  m_ReplacementShipScale;

	// Enviremental Effects
	long			  m_GWell;
	long			  m_Radiation;
	long			  m_EnvStats[2];
	long			  m_EnvEffects[1];

    //movement on stations
    bool              m_HavePosition;
    long              m_Room;
    long	          m_Oldroom;
    long              m_ActionFlag;
    float             m_Orient;

    //movement in space
    bool              m_FollowObject;
	bool			  m_NoPlayerUpdate;

    //MVAS members
    bool              m_ReceivedMVAS;
    float             m_LastPos[3];
    unsigned long     m_LastRead;
    long              m_MVAS_index; //if -1 then use Server forced positioning, rather than client based positioning
    u16				  m_MVAS_frequency;
    float             m_LastVelocity;
    unsigned char     m_UDPSendBuffer[UDP_BUFFER_SEND_SIZE];

    //warp data
  	long			  m_WarpNavs[20];
	long			  m_WarpNavIndex;
	long			  m_WarpEffect;
	bool			  m_WarpCharing;
    bool              m_WarpDrive;
    float             m_DistToNav;
    short             m_OverrunCount;
    short             m_WarpNavCount;
    unsigned long     m_WarpTime;
    unsigned long     m_WarpBroadcastTime;
	float			  m_WarpDrain;

    //energy
    unsigned long     m_EnergyRecharge;

    //start sector methods
    long              m_CameraSignal;  //if this is anything other than -1, we send a camera signal at sector login
    long              m_CameraID;

    long              m_FromSectorID;     // how did we get here?

    //Connection members
    UDP_Connection  * m_UDPConnection;
    MessageQueue    * m_RecvQueue;
    long              m_PacketSequenceNum;
    long              m_Player_IPAddr;
    short             m_Player_Port;
    bool              m_UDPHandoffReceived;
	long			  m_LoginAckReceived;
	long			  m_LoginAckCounter;
	u32				  m_ResendTimer;

    unsigned char     m_ScratchBuffer[MAXIMUM_PACKET_CACHE];   //used for general scratch data, packet forming and sending data to player via UDP
	unsigned char	  m_OpcodeFormingBuffer[8192];
	unsigned char	  m_PacketSplitBuffer[MAXIMUM_PACKET_CACHE];
	long			  m_PacketSplitRemaining;
	unsigned short    m_PeriodicCacheSize;	// This can be varied for performance. Players with decent ISP's can have a larger send packet size to boost performance
	MessageQueue	* m_RSendQueue;
	MessageQueue	* m_UDPQueue; //used for forming the UDP packet sends
	item_send		  m_ItemSendType;
	unsigned long	  m_ItemSendIndex;

	st_resend		  m_ResendQueue[RESEND_ELEMENTS];
	long			  m_ResendIndex;

    //combat
    bool              m_AttackMusic;
    short             m_AttackerCount;
	bool			  m_IncapAvatarSent;
	bool			  m_CombatImmunity;
	bool			  m_ScanInvisible;

    //prospecting & loot
    bool		      m_TractorBeam;
    bool		      m_ProspectBeam;
	TimeNode          m_ProspectBeamNode;
	TimeNode          m_ProspectTractorNode;
    float             m_ProspectDrain;
	ItemBase	    * m_FloatingOre_contents;
	short             m_FloatingOre_stack;
	float			  m_TractorItemLocation[3];
    //hijack
    long			  m_Hijackee;

    //missions
	char			  m_TalkTreeBuffer[TALKTREE_BUFFER_SIZE];
    unsigned long     m_MissionFlags;
    bool              m_MissionAcceptance;
	bool			  m_DebugMissions;
	bool			  m_MissionStageDebrief; // this is used to allow the NPC to inform the player about why the stage won't progress
	bool			  m_MissionDebriefed;	 // ensure we don't enter an endless debrief cycle.
	int				  m_MoreDestination; // the number of the talk node to go to when more is pressed
	long			  m_PushMissionID;
	long			  m_PushMissionUID;
	long			  m_CompletedMissions[MAX_MISSION_ID/32]; //removed some more STL
	MissionDataNode	  m_MissionNodes[MAX_MISSIONS];

	// trading
    long			  m_TradeID;			// ID of person you are trading with
    bool              m_TradeConfirm;
    bool		      m_TradeWindow;

	// more misc
    bool              m_ActionResponseReceived;
    bool		      m_ProspectWindow;
    Object          * m_CurrentDecoObj; //this is used by content devs creating decos
    long              m_StarbaseTargetID;
    bool			  m_BeaconRequest;
    bool              m_AuxWaiting;
    bool              m_NavCommence;
    bool              m_LoginFailed;
	bool			  m_ExposeDecos;
	bool			  m_UpdateSent;
	bool			  m_Wormholed;
    long              m_StargateDestination;
	bool			  m_Gating;
	bool			  m_GravityWell;
	char			  m_SoundWarningSetting;
	u32				  m_IncapXPDebt; // amount of last death
	time_t			  m_LogoutTime;
	u32				  m_LastSort; // for now, limit players to 1 sort per 10 seconds to save pressure on the save system.

#ifdef TEST_CREATE //this can used to debug 'unknown' mobs and navs, which happens when we send a 'create' packet for an ID that's already been sent
	typedef std::map<long, bool> check_list;
	check_list		  m_CheckList;
#endif
	
public:
    //TODO: these need to be encapsulated & made private
    Equipable         m_Equip[20];
};

#define ABILITY_JUMPSTART	65

#endif // _PLAYER_CLASS_H_INCLUDED_
