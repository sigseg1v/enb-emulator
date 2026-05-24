// ObjectClass.h
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

#ifndef _OBJECT_CLASS_H_INCLUDED_
#define _OBJECT_CLASS_H_INCLUDED_

#include "ObjectEffects.h"
#include "Mutex.h"
#include "SectorData.h"
#include "SectorManager.h"
#include <vector>

#define MAX_ITEMS_PER_RESOURCE 8

class ItemBase;
class Mutex;
class Player;

struct RangeElement
{
    Player     *player;
	bool        has_clicked;
} ATTRIB_PACKED;

struct ContentSlot  //this structure details a slot in the pickup menu for roids and hulks
{
    ItemBase *  item; 	
	u16			stack;
} ATTRIB_PACKED;

typedef std::vector<long> MOBIDList;

typedef enum 
{
	CREATE_PLAYER = 0x00,
	CREATE_SHIP = 0x01,
	CREATE_PLANET = 0x03,
	CREATE_COMPONENT = 0x04,
	CREATE_LIGHTSOURCE = 0x08,
	CREATE_STARGATE = 0x0B,
	CREATE_STARBASE = 0x0C,
	CREATE_HULK = 0x19,
	CREATE_DECO1 = 0x22,
	CREATE_DECO2 = 0x25,
	CREATE_DECO3 = 0x29,
	CREATE_HARVESTABLE = 0x26,
	CREATE_MANU_LAB = 0x27
} CreateType;

typedef std::vector<RangeElement> RangeListVec;

class Object
{
public:
    Object(long object_id);
    virtual ~Object();
    Object& Object::operator=(const Object& obj);

    //position & movement
	void			SetSecurityLvl(int Level)		    { m_MinSecLevel = Level; };
	int				GetSecurityLvl()					{ return m_MinSecLevel; };
    void            SetPosition(float x, float y, float z);
    void            SetOrientation(float o1, float o2, float o3, float o4);
    void            SetPosition(float *pos);
    void            SetOrientation(float *ori);
    void            SetEulerOrientation(float roll, float pitch, float yaw);
    void            SetVelocityVector(float *velocity);
	void			SetSector(long sector)						{ m_SectorID = sector; }
	virtual long	GetSector()									{ return m_SectorID; }
	bool			IsInSameSector(Object *o)					{ return GetSector() == o->GetSector(); }
    void            CopyPostionInfo(PositionInformation *pos);
    void            RandomiseOrientation();
    float         * Position()                                  { return (m_Position_info.Position); };
    float         * MutexedPos();
    void            BlankPositionInfo();
    void            CalcOrientation(float ZHeading, float YHeading);
    void            FaceObject(Object *obj);
	void			FacePosition(float *position);
	void			FaceAwayFromObject(Object *obj);
	bool			IsFacingObject(Object *obj);
    virtual void    MovePosition(float x, float y, float z, bool _override = true);
    float           GetAngleTo(float *pos);
    float         * Heading()                                   { return (m_Position_info.Velocity); };
    void            LevelOrientation();
	void			LevelOut();
	void			Rotate(float x, float y, float z);
	void			SetSpin(float rate);
	float			Spin()										{ return (m_Position_info.RotateRate); };
	void			SetTilt(float tilt);
	float			Tilt()										{ return (m_Position_info.TiltAngle); };

    void            SetPositionType(PositionalUpdateType type)  { m_Position_info.type = type; };
    bool            IsInRange(float *position, float scan_range, bool is_active);
    float			RangeFrom(float *position, bool abs = false);
	float			RangeFrom(Object *obj);

    //Generic object information
    virtual void    SetName(char *name);
    virtual void    SetHSV(float h1, float h2, float h3);
    virtual void    SetBasset(short basset);
    virtual void    SetLevel(short level)                       { m_Level = level; };
    void            SetObjectType(object_type type)             { m_Type = type; };
    void            SetCreateType(char type)                    { m_CreateInfo.Type = type; };
    void            SetScale(float scale)                       { m_CreateInfo.Scale = (scale == 0.0f) ? 1.0f : scale;};
    virtual void    SetSignature(float sig)                     { m_Signature = sig; };
    void            SetRadarRange(float range);
	virtual bool	HasValidContent()							{ return false; };
	void			ResetStaticPacket()							{ m_StaticPacketFormed = false; }
	virtual void	SetLocalStargate()							{ };
	virtual bool	IsLocalStargate()							{ return false; };

    void            SetHostileTo(object_type hostility_type)    { m_Hostile = hostility_type; };
    void            SetHostilityTarget(long GameID)             { m_HostilityTargetID = GameID; };
    void            SetScanRange(float scan_range)              { m_ScanRange = scan_range; };
    void            SetNavInfo()                                { m_NavInfo = true; };
	virtual float	GetFactionStanding(Object *obj);
	s16				GetFactionID()								{ return m_FactionID; };
	void			SetFactionID(long faction_id)				{ m_FactionID = (s16)faction_id; };			
	void			SetClassSpecific()							{ m_ClassSpecific = true; };
	bool			GetClassSpecific()							{ return m_ClassSpecific; };

	virtual float	GetShieldLevel()							{ return 0.0f; };

	// DamageMOB: overridden by MOB. Equipable.cpp calls this on a generic
	// Object pointer. No-op default so non-MOB Objects ignore damage.
	virtual void	DamageMOB(long /*attacker_id*/, long /*damage_type*/,
								float /*damage*/, long /*flags*/ = 0) { }

    virtual void    SetDestination(long destination_sector)     { };
    virtual void    SetAppearsInRadar()                         { };
    virtual void    SetHuge()                                   { };
    virtual void    SetNavType(long nav_type)                   { };
    virtual long    NavType()                                   { return (0); };
    virtual char    IsHuge()                                    { return (0); };
    virtual float   FieldRadius()                               { return (0.0f); };
    virtual void    SetFieldRadius(float radius)                { };
    virtual void    SetLevelFromHSV(float)                      { };
    virtual void    SetFieldCount(u16 count)					{ };
    virtual short   FieldCount()                                { return (0); };
	virtual Object *GetLastFieldAsteroid()						{ return (0); };
    virtual void    SetTypeAndName(short type)                  { };
    virtual void    SetContainerField(Object *obj)              { };
    virtual long    Destination()                               { return (0); };
    virtual float   ResourceRemains()                           { return (0.0f); };
    virtual void    SetFieldType(u16 type)						{ };
    virtual void    PopulateField(bool live = false, bool repop = false)		{ };
	virtual void	SetRespawnTimer(int respwan_time)			{ };
    long            BroadcastID()                               { return (m_BroadcastID); };
    void            SetBroadcastID(long id)                     { m_BroadcastID = id; };
	virtual void	OutOfRangeTrigger(Player *p, float range)   { };
	virtual void	InRangeTrigger(Player *p,    float range)	{ };
	virtual	void	SetDestroyTimer(long time_delay, long respawn_delay = -1)	{ };
	virtual void	DestroyHusk()								{ };
	virtual void	RemoveDestroyTimer()						{ };
	bool			GetUsedInMission()							{ return m_UsedInMission; }
	void			SetUsedInMission()							{ m_UsedInMission = true; }
	virtual void	SetLootTimer(long time_delay)				{ };
	virtual u32		GetLootTime()								{ return 0; };
	virtual void	DisplayLoot(Player *p)						{ };
	virtual void	AddToClearList(Player *player)				{ };


    virtual void    SetSpawnRadius(float radius)                { };
    virtual void    SetBehaviour(long type)                     { };
    virtual void    SetGroupAttack(long value)                  { };
    virtual void    SetSpawnCount(short count)                  { };
    virtual short   SpawnCount()                                { return 0; };
    virtual void    AddMOBID(long MOBID)                        { };
    virtual void    AddResource(long resource)                  { };
    virtual void    SetLevelMinMax(long level1, long level2)    { };
    virtual void    PopulateSpawn(bool live = true)             { };
	virtual void	AddItemID(long item_id, float frequency)	{ };
	virtual void	BlankItemIDs()								{ };
	virtual void	ResetObjectContents()						{ };
	virtual void	SetRespawnTime(unsigned long respawn_time)	{ };
	virtual void	CheckFieldRespawn(u32 tick)					{ };


    virtual long    GetBroadcastID()                            { return (0); };
    void			SendSoundBroadcast(Player *p);
	void			RemoveSoundBroadcast(Player *p);
    virtual Object *ContainerField()                            { return (0); }; //pointer to a 'Field' class which this object belongs to
    virtual void    BlipGate(long player_id)                    { };

    float           HSV0()                                      { return (m_CreateInfo.HSV[0]); };
    float           HSV1()                                      { return (m_CreateInfo.HSV[1]); };
    float           HSV2()                                      { return (m_CreateInfo.HSV[2]); };
    short           BaseAsset()                                 { return (m_CreateInfo.BaseAsset); };
    char            CreateType()                                { return (m_CreateInfo.Type); };
    float           Scale()                                     { return (m_CreateInfo.Scale); };
    float           Signature()                                 { return (m_Signature); };
    float           RadarRange()                                { return (m_RadarRange); };
    float           PosX()                                      { return (m_Position_info.Position[0]); };
    float           PosY()                                      { return (m_Position_info.Position[1]); };
    float           PosZ()                                      { return (m_Position_info.Position[2]); };
    char          * Name()                                      { return (m_Name); };
	long			NameLen()									{ return (m_NameLen); };
    float         * Orientation()                               { return (&m_Position_info.Orientation[0]); };
    short           Level()                                     { return (m_Level); };
    virtual char    IsNav();
    virtual bool    AppearsInRadar()                            { return (false); };
    float           Radius()                                    { return (m_ObjectRadius); };
	void			SetObjectRadius(float r)					{ m_ObjectRadius = r; }

    bool            HasNavInfo()                                { return (m_NavInfo); };


    object_type     HostileTo()                                 { return (m_Hostile); };
    long            HostilityTarget()                           { return (m_HostilityTargetID); };

    PositionInformation * PosInfo()                             { return (&m_Position_info); };


    //object indexing methods
    void            SetIndex(long *index_array);
    void            UnSetIndex(long *index_array);
    bool            GetIndex(long *index_array);
    virtual void    SetEIndex(long *index_array)                { };
    virtual bool    GetEIndex(long *index_array)                { return (true); };
	virtual void	UnSetEIndex(long *index_array)				{ };
    void            SetObjectIndex(long object_index)           { m_ObjectIndex = object_index;  };
    long            ObjectIndex()                               { return (m_ObjectIndex); };
    virtual long    ExploreIndex()                              { return (0); };
    virtual void    SetExploreIndex(long index)                 { };
    void            SetDatabaseUID(long index)                  { m_DatabaseUID = index; };
    long            GetDatabaseUID()                            { return (m_DatabaseUID); };
	bool			GetBitEntry(long *bit_array, long index);
	void			SetBitEntry(long *bit_array, long index);
	void			UnsetBitEntry(long *bit_array, long index);

    virtual void    SetRespawnTick(unsigned long respawn)                { };
    virtual unsigned long    RespawnTick()                               { return 0; };

    //Manipulating loot items
    virtual void    AddItem(ItemBase *item, long stack)			{ };
    virtual float   RemoveItem(long slot_id, long stack)		{ return (1.0f); };
    virtual void    ResetResource()								{ };
	virtual void	FullResetResource()							{ };
    virtual ContentSlot *GetContents(long index)				{ return (0); };
    virtual ItemBase    *GetItem(long index)					{ return (0); };
    virtual short        GetStack(long index)					{ return 0; };
	virtual long		 GetCreditLoot()						{ return 0; };
	virtual void		 SetCreditLoot(long creds)				{ };

    virtual void    Remove();


    //game ID methods
    object_type     ObjectType()                                { return (m_Type); };
    void            SetGameID(long game_id)                     { m_CreateInfo.GameID = game_id; };
    long            GameID()                                    { return (m_CreateInfo.GameID); };
    Object        * GetObjectFromID(long GameID);
	Object		  * GetNextObject()								{ return m_Next; }
	void			SetNextObject(Object *obj)					{ m_Next = obj; }

    PositionalUpdateType PosType()                              { return (m_Position_info.type); }; //return the position type needed
    void            SetPosType(PositionalUpdateType type)       { m_Position_info.type = type;   };

    virtual void    SetActive(bool active)                      { m_Active = active; };
    bool            Active()                                    { return m_Active;   };

    void            SetRequiresReset(bool reset)                { m_Requires_reset = reset; };
    bool            RequiresReset()                             { return (m_Requires_reset);};

    void            SetRenderState(short state)                 { m_RenderState = state; };
    short           RenderState()                               { return (m_RenderState); };

    bool            IsTemp()                                    { return (m_TempObject); };
    void            SetTemp()                                   { m_TempObject = true; };

    virtual void    SetLastAccessTime(unsigned long time)       { m_LastAccessTime = time; };
    unsigned long   LastAccessTime()                            { return (m_LastAccessTime); };
    void            SetRemove()                                 { m_ToBeRemoved = true; };
    bool            IsToBeRemoved()                             { return (m_ToBeRemoved); };
    virtual void    SendObjectReset()                           { };
    virtual void    SendObjectDrain(long slot)					{ };
    Player         *CheckResourceLock();

    virtual SectorManager  *GetSectorManager();
	virtual ObjectManager  *GetObjectManager();
	u32 *			GetSectorList();

    void            SetPlayerLootLock(long game_id)             { m_TargetOnCreate = game_id; };
    long            GetPlayerLootLock()                         { return (m_TargetOnCreate); };

    Object         *NearestNav();

//Movement methods
    void            Turn(float intensity);
    void            Tilt(float intensity);
    virtual void    CalcNewPosition(unsigned long current_tick, bool turn = false);
    void            SetMovementID(long mov);
    void            IncrementMovementID(long add)               { m_MovementID += add; };
    long            MovementID()                                { return (m_MovementID); };
	virtual void	SendLocationAndSpeed(bool include_player, bool zeroupdate = false);
    virtual void	SendLocationAndSpeed(Player *player);
	virtual void	AdjustAndSetSpeeds(bool sendaux, bool immobilise) {};
    virtual bool    ObjectIsMoving();
    virtual void    SendToVisibilityList(bool include_player);
    void            SetVelocity(float velocity);
    float           Velocity()                                  { return (m_Velocity); };
	void            SetGravityField(float modifier, float steering, long expire);
	void            CheckAndRemoveGravity();
    virtual void    CalcNewHeading(float tdiff);
    void            SetHeading();
    void            SetLastUpdateNow()                          { m_LastUpdate = GetNet7TickCount(); };
    void            CalcOrientation(float *pos1, float *pos2, bool set_heading=true); //set orientation from two coordinates
	void			SetAcceleration(float acceleration)			{ m_Position_info.Acceleration = acceleration; };


//Hijack Player Info accessors
    void            SetPlayerHijack(Player *p)                  { m_PlayerHijack = p; };
    Player         *GetPlayerHijack()                           { return (m_PlayerHijack); };

//Visibility list handling
    virtual void    UpdateObjectVisibilityList()                { LogMessage("UpdateObjectVisibilityList Unimplemented.\n"); };
    virtual bool    ObjectInRangeList(Object *obj);
    virtual void    RemoveObjectFromRangeList(Object *obj)      { LogMessage("RemoveObjectFromRangeList Unimplemented.\n"); };
    virtual void    AddObjectToRangeList(Object *obj)           { LogMessage("AddObjectToRangeList Unimplemented.\n"); };
    virtual bool    IsClickedBy(Player *p)                      { return false; };
    virtual void    RemovePlayerFromRangeLists(Player *p)       { };



//MOB methods
    virtual float   DamageObject(long id, long dt, float damage, long inflicted)  { return -1.0f; };
    virtual void    DestroyMOB(long game_id)                    { };
    virtual void    SendExplosion()                             { };
    virtual void    SendShieldUpdate(Player *p)                 { };
    virtual void    SendHullUpdate(Player *p)                   { };
    void            SetUpdateRate(long rate)                    { m_UpdateRate = rate; };
    long            UpdateRate()                                { return m_UpdateRate; };
    virtual void    SetDefaultStats(float turn, short behaviour, float velocity, long update)   { };
    virtual void    LostTarget(Player *p, bool incap = false)   { };
    virtual float   GetHullLevel()                              { return 0.0f; };
    virtual float   GetStartHullLevel()                         { return 0.0f; };
	virtual float	SpawnRadius()								{ return 0.0f; };
	virtual void	AddGroupHate(long GameID, long Hate)		{ };


//Vector math
    void            Quat4fNormalize(float *vector1);
    void            Quat4fMul1(float vector1[], float vector2[], float *vector3);
    void            Quat4fMul(float vector1[], float vector2[], float *vector3);
    void            Quat4fMulInv(float vector1[], float vector2[], float *vector3);
	void			TransformCoords(float *pos1, float *pos2, float ori[]);

//packet methods
    virtual void	SendObject(Player *player);
    void            SendCreateInfo(Player *player);
    virtual void    SendObjectEffects(Player *player)   		{ };
    virtual void    SendRelationship(Player *player);
    virtual void    SendPosition(Player *player)        		{ };
    virtual void    SendAuxDataPacket(Player *player)   		{ };
    virtual void    SendNavigation(Player *player)              { };
    virtual void    OnCreate(Player *player)                    { }; //called once when the object is created for this player
    virtual void    OnTargeted(Player *player)                  { }; //called every time player clicks on this object
	virtual void    OnUnTargeted(Player *player)				{ }; //called every time player clicks a different target

// effects
	virtual bool	GetStartStatus()							{ return true; } // non-players are always "logged in"
    void			RemoveEffectRL(long effect_UID);
    void			SendObjectEffectRL(ObjectEffect *object_effect);
    void			SendObjectToObjectEffectRL(ObjectToObjectEffect *obj_effect, bool weapon_fire = false);
	virtual void	SendToRangeList(short opcode, unsigned char *data, size_t length, bool = false);
	void			SendObjectEffect(long effect_id, long duration, float scale = 0.0f);

	void			DestroyStaticObject();

protected:
    void            ExtrapolatePosition(float av_speed, float tdiff);
    bool            CheckBoundaries();
    virtual void    SetClickedBy(Player *p)                     { LogMessage("Clicked by unimplemented for %s.\n", Name()); };

protected:
    Create      m_CreateInfo;
    char      * m_Name;
	long		m_NameLen;
    short       m_Level;
    object_type m_Type;

	int			m_MinSecLevel;

    object_type m_Hostile;           //this tells what this object can be hostile toward - we can have things hostile to MOBS, Players etc
    long        m_HostilityTargetID; //this tells what this gameID this object is hostile toward at the moment. zero means non-hostile
                                     //can a MOB be hostile to multiple targets? Does this need to be an array?

	float       m_Signature;
    float       m_RadarRange;
    float       m_ScanRange;

    PositionInformation m_Position_info;
    u32				m_LastUpdate;          // last update tick
    float			m_Velocity;            // current velocity
    u32				m_MovementID;
    float			m_YInput;            //current tilt input from player
    float			m_ZInput;            //current turn input from player
    bool			m_Accelerating;      //is the ship accelerating or decellerating?
    char			m_Thrust;            //1, 0 or -1
    bool			m_ReceivedMovement;
    long			m_UpdateRate;

	SectorManager  *m_SectorManager;
	ObjectManager  *m_ObjectManager;

	// Gravity link modifiers
	float			m_GravityField;		   // Current gravity field affecting object movement
	float           m_GravityHandle;	   // How much should steering be affected
	unsigned long	m_GravityTimeExpire;   // When should Gravity field expire
	float			m_GravityVelocity;     // Saved velocity
	float			m_GravityAcceleration; // Saved Acceleration

    u32			m_RangeList[MAX_ONLINE_PLAYERS/32 + 1]; // list of Players who can see this object
    long        m_ObjectIndex;       //this indexes into an array of object pointers which the object manager maintains.
                                     //this array is used to directly index a GameID into an Object.
                                     //to eliminate any kind of looping, searching for objects.
                                     //This index is also essential for determining if object is in range or not.

    long        m_DatabaseUID;       //this value refers to the 'sector_id' field of this object in the database.

    bool        m_Active;
    bool        m_Requires_reset;    //This signals that this is a temp object that requires a reset
    bool        m_NavInfo;
    bool        m_TempObject;
    bool        m_ToBeRemoved;
	bool		m_UsedInMission;	 //This flag determines if this object is used in a mission or not. This is just to save processing time
	bool		m_ClassSpecific;
	bool		m_StaticPacketFormed; // This flag specifies if the object static packet is correct
    short       m_RenderState;
	short		m_FactionID;
    u32			m_LastAccessTime;    //This will also be handy for getting an idea of if objects are being used
    long        m_TargetOnCreate;    //This allows us to automatically target a player onto this object when it's created

    float       m_ObjectRadius;

    long        m_BroadcastID;

	long		m_SectorID;

    Player      *m_PlayerHijack;   //This is used for when a player takes possession of an object

	Object	   *m_Next;				//This is used for whipping through spawn entries
    Mutex       m_Mutex;
public:
	Effects		m_Effects;			// Moved here so all objects can have visual effects
};

#define VERBID_SCAN 0x01
#define VERBID_LAND 0x02
#define VERBID_LOOT 0x03
#define VERBID_GROUP 0x04
#define VERBID_MESSAGE 0x05
#define VERBID_TRADE 0x06
#define VERBID_TRACTOR 0x07
#define VERBID_DOCK 0x08
#define VERBID_PROSPECT 0x09
#define VERBID_GATE 0x0a
#define VERBID_REGISTER 0x0b
#define VERBID_JUMPSTART 0x0c
#define VERBID_FOLLOW 0x0d

#define HAS_VISITED 0x80
#define IS_HUGE		0x40
#define IS_NAV		0x20
#define HAS_NAV		0x10

struct StaticObjectPacket
{
	long game_id;
	float scale;
	short base_asset;
	float hsv0;
	float hsv1;
	float hsv2;
	u8 createtype;
	u8 reaction;
	u8 position_type;
    float   position[3];
    float   orientation[4];
	char	name[128];
	float	signature;
	u8		sig_flags;
};

struct MovingObjectPacket
{
	long game_id;
	float scale;
	short base_asset;
	float hsv1;
	float hsv2;
	float hsv3;
	u8 createtype;
	u8 reaction;
	u8 is_attacking;
};

#endif // _OBJECT_CLASS_H_INCLUDED_