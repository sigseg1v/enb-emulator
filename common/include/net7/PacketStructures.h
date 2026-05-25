// PacketStructures.h
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

#ifndef _PACKET_STRUCTURES_H_INCLUDED_
#define _PACKET_STRUCTURES_H_INCLUDED_

// Phase R Wave 2: this header used to live in three places (proxy/, server/src/,
// login-server/Net7SSL/) and #include "Net7.h" (or Net7SSL.h) for ATTRIB_PACKED
// + u8/u16/.../BSTR. Those bits are now in common/include/net7/Packing.h so this
// file stands alone and can be shared across all three subprojects.
#include <net7/Packing.h>

struct EnbTcpHeader
{
    short   size;
    short   opcode;
} ATTRIB_PACKED;

struct VersionRequest
{
    // Phase K: int32_t guarantees 4 bytes on every platform. `long` is 8 bytes
    // on Linux x86_64 vs 4 bytes on Win32, and the wire format is 4 bytes.
    int32_t Major;
    int32_t Minor;
} ATTRIB_PACKED;

//New header for use with UDP comms.
//If we have the player_id it makes things a lot simpler
struct EnbUdpHeader
{
    // Phase K: int32_t guarantees 4 bytes on every platform. `long` is 8
    // bytes on Linux x86_64 vs 4 bytes on Win32, which made sizeof(header)
    // 20 on Linux and 12 on Win32 — wire format is 12. Both server and
    // proxy recompile from this header, so the size flip is symmetric.
    short   size;
    short   opcode;
    int32_t player_id;
    int32_t packet_sequence;
} ATTRIB_PACKED;

/*

  Personality:
    0 = Calm Male Personality           MacGregor, Herra, Cassel, Silva, Loric, Grayfeather, P3889
    1 = Reserved Female Personality     Kathrada, Ariad, Vinda
    2 = Macho Male Personality          Kahn, ShouTzu, Var
    3 = Wild Male Personality           ?
    4 = Sexy Female Personality         deWinter
    5 = Tomboy Female Personality       Amah
    6 = Undefined / Placeholder         -
    7 = Megan Female Personality        Megan
    8 = Male Cockpit Personality        -
    9 = Female Cockpit Personality      -
    10 = Vrix personality               V'rix

  Head and Body:
    0  = Megan (F,7)
    1  = MacGregor (M,0)
    2  = Morgan Thorne (F,5?)
    3  = Lady deWinter (F,4)
    4  = Kahn (M,2)
    5  = Merjan Kathrada (F,1)
    6  = Nostradamus Smythe (M,?)
    7  = Shou Tzu (M,2)
    8  = Herra (M,0)
    9  = Var (M,2)
    10 = Ariad (F,1)
    11 = Cassel (M,0)
    12 = Vinda (F,1)
    13 = Kayen Silva (M,0)
    14 = Loric deGrey (M,0)
    15 = Amah (F,5)
    17 = Grayfeather (M,0)
    19 = P3889 (M,0)
    100 = V'rix (?,10)

*/

struct AvatarData
{
    // Phase K: int32_t (was `long`). On Win32 `long` is 4 bytes so the
    // original wire size is 241 bytes; on Linux x86_64 `long` is 8 bytes,
    // which bloats the struct to 277 bytes (+9 longs × 4 bytes). The Win32
    // client decodes by memcpy onto a 241-byte buffer, so the Linux build
    // must serialise 4-byte fields too. Mirrors the e74f07c migration for
    // ServerRedirect/VersionRequest/MasterJoin. ATTRIB_PACKED still guards
    // the no-padding invariant.
    char    avatar_first_name[20];      // 14   d4  20
    char    avatar_last_name[20];       // 28   e8  20
    int32_t avatar_type;                // 04   08  4
    char    filler1;                    //      0c  -
    char    avatar_version;             // 09   0d  1
                                        //      0e
                                        //      0f
    int32_t race;                       // 0c   10  4
    int32_t profession;                 // 10   14  4
    int32_t gender;                     // 14   18  4
    int32_t mood_type;                  // 18   1c  4

    char    personality;                // 1c   20  1
    char    nlp;                        // 1d   21  1
    char    body_type;                  // 1e   22  1 (shirt type?)
    char    pants_type;                 // 1f   23  1
    char    head_type;                  // 20   24  1
    char    hair_num;                   // 21   27  1
    char    ear_num;                    // 22   26  1
    char    goggle_num;                 // 23   27  1
    char    beard_num;                  // 24   28  1
    char    weapon_hip_num;             // 25   29  1
    char    weapon_unique_num;          // 26   2a  1
    char    weapon_back_num;            // 27   2b  1
    char    head_texture_num;           // 28   2c  1
    char    tattoo_texture_num;         // 29   2d  1
                                        //      2e  -
                                        //      2f  -

    float   tattoo_offset[3];           // 2c   30  12 (x,y,zoom)
    float   hair_color[3];              // 38   3c  12
    float   beard_color[3];             // 44   48  12
    float   eye_color[3];               // 50   54  12
    float   skin_color[3];              // 5c   60  12
    float   shirt_primary_color[3];     // 68   6c  12
    float   shirt_secondary_color[3];   // 74   78  12
    float   pants_primary_color[3];     // 80   84  12
    float   pants_secondary_color[3];   // 8c   90  12

    int32_t shirt_primary_metal;        // 98   9c  4
    int32_t shirt_secondary_metal;      // 9c   a0  4
    int32_t pants_primary_metal;        // a0   a4  4
    int32_t pants_secondary_metal;      // a4   a8  4

    char    filler2;                    //          1?

    float   height_weight_1[5];         //      ac  20
    float   height_weight_2[5];         //      c0  20
} ATTRIB_PACKED;  // 241 bytes

struct AvatarInfo
{
    // Phase K: int32_t (was `long`). Win32 packed = 13×4 + 81 = 133 bytes;
    // Linux x86_64 with 8-byte long = 13×8 + 81 = 185 bytes — wire-format
    // divergence. Migrated to int32_t for parity with the Win32 client and
    // ntohl-compatibility (ntohl returns uint32_t; into a `long` field this
    // was previously a silent widening conversion).
    // NOTE: All fields are in Big Endian format -- use ntohl to convert!
    int32_t avatar_slot;        // 0 to 4 = 0
    int32_t sector_id;          // 1071
    int32_t galaxy_id;          // 1
    int32_t count;              // 5
    int32_t avatar_id_msb;      // 0
    int32_t avatar_id_lsb;      // 1
    int32_t account_id_msb;     // 0
    int32_t account_id_lsb;     // 2
    int32_t admin_level;        // 0
    int32_t gm_flag;            // 1
    int32_t combat_level;       // 0
    int32_t explore_level;      // 0
    int32_t trade_level;        // 0
    char    location[81];       // "Saturn"
} ATTRIB_PACKED;  // 133 bytes

struct ColorInfo
{
    // Phase K: int32_t (was `long`). Win32 packed = 12 + 1 + 4 = 17 bytes;
    // Linux with 8-byte long = 21 bytes. Embedded 8× in ShipData which is
    // embedded in GlobalCreateCharacter — pre-migration GCC bloated
    // ShipData to 226 bytes and GlobalCreateCharacter to 571 bytes (vs
    // canonical Win32 539). Sent verbatim via sizeof(GlobalCreateCharacter)
    // in UDPClient_linux.cpp::CreateCharacter so any Linux client paying
    // attention to Win32 wire size would corrupt the server's parse.
    // Server-side `metal` consumers (server/src/PlayerMisc.cpp,
    // AccountManager.cpp) all assign from a local `int metal`, so
    // narrowing long→int32_t is value-preserving for every existing
    // call site.
    float   HSV[3];
    char    flat;
    int32_t metal;
} ATTRIB_PACKED;  // 17 bytes

struct ShipData
{
    // Phase K: int32_t (was `long`). Win32 packed = 5*4 + 26 + 12 + 8*17 = 194
    // bytes; Linux with 8-byte long = 5*8 + 26 + 12 + 136 = 214 bytes. Embedded
    // in GlobalCreateCharacter — without this migration the proxy stamped a
    // 20-byte-too-large payload into the UDP_GLOBAL_SERVER_PORT exchange, and
    // the server's wire offsets walked off into the ship_data/unknown region.
    int32_t race;                           // 00
    int32_t profession;                     // 04
    int32_t hull;                           // 0c
    int32_t wing;                           // 08
    int32_t decal;                          // 10

    char    ship_name[26];                  // 14
    float   ship_name_color[3];             // 2e

    ColorInfo   HullPrimaryColor;           // 3a, 46, 47
    ColorInfo   HullSecondaryColor;         // 4b, 57, 58
    ColorInfo   ProfessionPrimaryColor;     // 5c, 68, 69
    ColorInfo   ProfessionSecondaryColor;   // 6d, 79, 7a
    ColorInfo   WingPrimaryColor;           // 7e, 8a, 8b
    ColorInfo   WingSecondaryColor;         // 8f, 9b, 9c
    ColorInfo   EnginePrimaryColor;         // a0, ac, ad
    ColorInfo   EngineSecondaryColor;       // b1, bd, be
} ATTRIB_PACKED;  // 194 bytes

struct Galaxy
{
    // Phase K: int32_t (was `long`). Win32 packed = 64 + 4×4 + 2 + 2 = 84
    // bytes; Linux with 8-byte long = 64 + 4×8 + 2 + 2 = 100 bytes. The
    // wire format is 84 — fix the local representation to match.
    char    Name[64];
    int32_t GalaxyID;
    int32_t IP_Address;
    short   port;
    int32_t NumPlayers;
    int32_t MaxPlayers;
    short   unknown2;
} ATTRIB_PACKED;  // 84 bytes

struct WarpPacket
{
    long GameID;
    short Navs;
    long TargetID[20];
} ATTRIB_PACKED;

struct InvMove
{
    long GameID;
    long FromInv;
    long FromSlot;
    long ToInv;
    long ToSlot;
    long Num;
} ATTRIB_PACKED;

struct InvSort
{
	long ID;
	long TargetInv;
	long Sort1;
	long Sort2;
	long Sort3;
	char Reverse;
} ATTRIB_PACKED;

struct ItemState
{
    long GameID;
    long BitMask;
    char Enable;
    char Inventory;
    char ItemNum;
} ATTRIB_PACKED;

struct AvatarListItem
{
    AvatarInfo      info;               // 133 bytes (internal struct is 144 bytes)
    AvatarData      data;               // 241 bytes (internal struct is 268 bytes)
} ATTRIB_PACKED;  // 374 bytes

struct GlobalAvatarList
{
    // Phase K: total wire size with int32_t-migrated members and 2 galaxies =
    // 5×374 + 4 + 2×84 = 2042 bytes. With unmigrated 8-byte longs on Linux,
    // the struct ballooned to 5×(185+277) + 8 + 2×100 = 2518 bytes — the
    // bytes a Win32 client would memcpy into a 2042-byte buffer would have
    // come from random fields. Migration restores Win32 parity.
    AvatarListItem  avatar[5];      // 5 * 374 bytes
    int32_t         num_galaxies;   // 4 bytes -- 1 to 6 currently hard-coded to 1!
    Galaxy          galaxy[2];      // support only one galaxy!
    // Galaxy           galaxy[4];  // 4 * 84 bytes -- variable length array of galaxies
} ATTRIB_PACKED;

struct GlobalCreateCharacter
{
    // Phase K: int32_t (was `long`). Win32 packed = 3*4 + 65 + 241 + 194 + 27 =
    // 539 bytes; Linux with 8-byte long = 3*8 + 65 + 241 + 194 + 27 = 551
    // bytes (still divergent through the embedded ShipData, since both this
    // struct and ShipData carry `long` fields). Sent verbatim as the payload
    // of the proxy->server UDP exchange (UDPClient_linux.cpp:746) via
    // sizeof(GlobalCreateCharacter), so a size mismatch silently corrupts the
    // CREATE_AVATAR request and AccountManager::CreateCharacter reads garbage.
    int32_t galaxy_id;              // 4 bytes
    int32_t character_slot;         // 4 bytes
    int32_t tutorial_status;        // 4 bytes
    char    account_username[65];   // 65 bytes
    AvatarData avatar;              // 241 bytes
    ShipData ship_data;             // 194 bytes
    char    unknown[27];            // 27 bytes
} ATTRIB_PACKED;  // 539 bytes

struct MasterJoin
{
    // Phase K: 11 * int32_t + 20-byte ticket = 64 bytes on every platform.
    // Was `long`, which is 8 bytes on Linux x86_64 (struct = 108B) vs 4 bytes
    // on Win32 (struct = 64B). The wire format is 64 bytes; the Linux
    // mismatch caused HandleMasterJoin to read avatar_id_lsb / ToSectorID
    // from bytes 32 / 40 instead of 16 / 20, yielding zeros and triggering
    // the SendMasterLogin-timeout fallback path on every join.
    int32_t unknown1;
    int32_t unknown2;
    int32_t unknown3;
    int32_t avatar_id_msb;
    int32_t avatar_id_lsb;
    int32_t ToSectorID;
    int32_t FromSectorID;
    int32_t PlayerLevel;
    int32_t unknown8;
    int32_t unknown9;
    int32_t unknown10;
    char    ticket[20];
} ATTRIB_PACKED;

// This packet is sent by the Global Server to the Client
// This packet causes the galaxy loading screen to appear.
// The port number should be somewhere within this scructure.
// The "unknown" values appear to control the animation of the "from" and "to" sectors
// on the wait screen.
// The MasterJoin packet is the first packet sent from the
// client to the Global Server once.
//
struct GlobalTicket
{
    // Phase K: int32_t (was `long`). Win32 = 4 + 64 = 68 bytes; Linux with
    // 8-byte long was 8 + 64 = 72. Sent by Connection::SendGlobalTicket via
    // sizeof(GlobalTicket) so the 4-byte gap would have shifted the embedded
    // MasterJoin avatar_id / sector_id / ticket fields on the Win32 client's
    // side, producing a wrong-galaxy ServerRedirect or rejected ticket.
    int32_t     response_code;
    MasterJoin  join_data;
} ATTRIB_PACKED;

struct ServerRedirect
{
    // Phase K: int32_t — Win32 client expects 4-byte fields on the wire.
    int32_t sector_id;
    int32_t ip_address;
    short   port;
} ATTRIB_PACKED;

struct ChangeBaseAsset
{
	long	GameID;			// buff[12] 4 bytes
	long	BaseAsset;		// buff[16] 4 bytes
	float	Scale;			// buff[20] 4 bytes
	float	HSV[3];			// buff[24] 12 bytes
} ATTRIB_PACKED;

struct Create
{
    long    GameID;                 // this[12] 4 bytes
    float   Scale;                  // this[16] 4 bytes
    short   BaseAsset;              // this[20] 2 bytes
    char    Type;                   // this[22] 1 byte
    float   HSV[3];                 // this[24] 12 bytes
} ATTRIB_PACKED;

struct ServerParameters
{
    float   ZBandMin;               // this[12] 4 bytes
    float   ZBandMax;               // this[16] 4 bytes
    float   XMin;                   // this[20] 4 bytes
    float   YMin;                   // this[24] 4 bytes
    float   XMax;                   // this[28] 4 bytes
    float   YMax;                   // this[32] 4 bytes
    float   FogNear;                // this[36] 4 bytes
    float   FogFar;                 // this[40] 4 bytes
    long    DebrisMode;             // this[44] 4 bytes
    char    LightBackdrop;          // this[48] 1 byte (boolean 1=true 0=false)
    char    FogBackdrop;            // this[49] 1 byte (boolean 1=true 0=false)
    char    SwapBackdrop;           // this[50] 1 byte (boolean 1=true 0=false)
    float   BackdropFogNear;        // this[52] 4 bytes
    float   BackdropFogFar;         // this[56] 4 bytes
    float   MaxTilt;                // this[60] 4 bytes
    char    AutoLevel;              // this[64] 1 byte (boolean 1=true 0=false)
    float   ImpulseRate;            // this[68] 4 bytes
    float   DecayVelocity;          // this[72] 4 bytes
    float   DecaySpin;              // this[76] 4 bytes
    short   BackdropBaseAsset;      // this[80] 2 bytes
    unsigned long SectorNum;        // this[84] 4 bytes
} ATTRIB_PACKED;

struct LoginData
{
    char    unknown40[40];
    char    timestamp[18];      // mm/dd/yy hh:mm:ss, example "10/01/06 16:43:25"
    char    unknown7[7];
} ATTRIB_PACKED;  // 65 bytes

struct Login
{
    MasterJoin  join_data;      // this[16] 64 bytes
    long        TimeSent;       // this[88] 4 bytes
    LoginData   login_data;     // this[96] 65 bytes
    long        TimeReceived;   // this[164] 4 bytes
} ATTRIB_PACKED;

struct SetBBox
{
    float   XMin;               // this[12] 4 bytes
    float   YMin;               // this[16] 4 bytes
    float   XMax;               // this[20] 4 bytes
    float   YMax;               // this[24] 4 bytes
} ATTRIB_PACKED;

struct SetZBand
{
    float   Min;                // this[12] 4 bytes
    float   Max;                // this[16] 4 bytes
} ATTRIB_PACKED;

struct Navigation
{
    long    GameID;
    float   Signature;
    char    PlayerHasVisited;
    long    NavType;
    char    IsHuge;
} ATTRIB_PACKED;

struct CreateAttachment
{
    long    Parent_ID;
    long    Child_ID;
    long    Slot;
} ATTRIB_PACKED;

struct DecalItem
{
    long    Index;
    long    decal_id;
    float   HSV[3];
    float   opacity;
} ATTRIB_PACKED;

#define MAX_DECALS  6   // arbitrary limit

struct Decal
{
    long    GameID;
    short   DecalCount;
    DecalItem Item[MAX_DECALS];
} ATTRIB_PACKED;

struct NameDecal
{
    long    GameID;
    char    Name[32];
    float   RGB[3];
} ATTRIB_PACKED;

struct ColorizationItem
{
    long    metal;
    float   HSV[3];
} ATTRIB_PACKED;

#define MAX_COLORIZATION_ITEMS  10  // arbitrary number

struct Colorization
{
    long    GameID;
    short   ItemCount;
    ColorizationItem item[MAX_COLORIZATION_ITEMS];
} ATTRIB_PACKED;

struct CharacterCreatorAvatarDataFile
{
    AvatarData  avatar;         // 241 bytes
    ShipData   ship;           // 194 bytes
} ATTRIB_PACKED;  // 435 bytes, Avatar1.dat is 564 bytes

struct AvatarDescription // opcode 0x61
{
    unsigned long AvatarID;
    AvatarData  avatar_data;
	long		unknown1;
    u8          unknown2[3];
    float       unknown3;
    float       unknown4;
} ATTRIB_PACKED;

struct Subparts // opcode 0xb4
{
    long    GameID;
    long    NumSubParts;
    char    BoneProfession[4];
    long    BassetProfession;
    char    BoneEngine1[11];
    long    BassetEngine1;
    char    BoneEngine2[11];
    long    BassetEngine2;
    char    BoneWing[4];
    long    BassetWing;
} ATTRIB_PACKED;

struct ConstantPositionalUpdate
{
    long    GameID;             // this[12] 4 bytes
    float   Position[3];        // this[16] 12 bytes
    float   Orientation[4];     // this[28] 16 bytes
} ATTRIB_PACKED;

struct FormationPositionalUpdate
{
    long	TargetID;			// this[16] 4 bytes
    long    LeaderID;           // this[12] 4 bytes
    float   Position[3];        // this[20] 12 bytes
} ATTRIB_PACKED;

struct RequestTarget
{
    long    GameID;             // this[12] 4 bytes
    long    TargetID;           // this[16] 4 bytes
} ATTRIB_PACKED;

struct SetInterface
{
	long UIChange;
	long UIType;
} ATTRIB_PACKED;

struct SetTarget
{
    long    GameID;             // this[12] 4 bytes
    long    TargetID;           // this[16] 4 bytes
} ATTRIB_PACKED;

struct ActionPacket
{
    long    GameID;             // this[12] 4 bytes
    long    Action;             // this[16] 4 bytes
    long    Target;             // this[20] 4 bytes
    long    OptionalVar;        // this[24] 4 bytes
} ATTRIB_PACKED;

struct ActionPacket2
{
    long    GameID;             // reversed bytes
    long    Action;             // reversed bytes
    short   string_len;         // BSTR
    char    string[1];			// ...
	long	_OptionalVar;		// reversed bytes
} ATTRIB_PACKED;

struct ClientSetTime
{
    long    ClientSent;
    long    ServerReceived;
    long    ServerSent;
} ATTRIB_PACKED;

struct VerbRequest
{
    long    SubjectID;
    long    ObjectID;
    long    Action;
} ATTRIB_PACKED;

struct CameraControl
{
    long    Message;
    long    GameID;
} ATTRIB_PACKED;

struct LogoffRequest
{
    long    PlayerID;           // this[12] 4 bytes
	long	LogOutType;
} ATTRIB_PACKED;

struct TriggerEmote
{
    long    GameID;
    long    Emote;
} ATTRIB_PACKED;

struct NotifyEmote
{
    long    GameID;
    long    Emote;
} ATTRIB_PACKED;

struct OptionPacket
{
    long    GameID;             // this[12] 4 bytes
    long    OptionType;         // this[16] 4 bytes
    unsigned char OptionVar;    // this[20] 1 byte
} ATTRIB_PACKED;

struct SelectTalkTree
{
    long    PlayerID;
    unsigned char Selection;
} ATTRIB_PACKED;

struct ChatStream
{
	long	GameID;
	char	Unknown1;			// I can't tell what this does (It's always 0x01.  Maybe for byte-alignment?)
	short	ChatSize;			// The size of the rest of the packet + 2 additional bytes (Target as mentioned below?)
	char	message[1];			// Variable length string
	short	_data_size;			// do not access from here on, reference only
	char	_unknown_data[1];	// optional block of data of data_size length
} ATTRIB_PACKED;

struct ClientChat
{
    long    GameID;             // this[12] 4 bytes
    char    Type;               // this[22] 1 byte
    short   Size;               // this[20] 2 bytes = strlen(String) + 1
    char    String[1];          // variable length string
	short	_data_size;			// do not access from here on, reference only
	char	_unknown_data[1];	// optional block of data of data_size length
} ATTRIB_PACKED;

//ClientChatError.type and ClientChatRequest.type
#define CCE_SPEAK_ON			0
#define CCE_SPEAK_LOCALLY		1
#define CCE_BROADCAST_TO		2
#define CCE_BROADCAST_ALL		3
#define CCE_INSERT_CHANNEL		4
#define CCE_REMOVE_CHANNEL		5
#define CCE_ENTER_CHANNEL		6
#define CCE_EXIT_CHANNEL		7
#define CCE_ADD_FRIEND			8
#define CCE_REMOVE_FRIEND		9
#define CCE_IGNORE				10
#define CCE_UNIGNORE			11
#define CCE_INVITE1				12
#define CCE_INVITE2				13
#define CCE_BAN					14
#define CCE_UNBAN				15
#define CCE_GAG					16
#define CCE_UNGAG				17
#define CCE_ADD_OWNER			18
#define CCE_REMOVE_OWNER		19
#define CCE_KICK				20
#define CCE_SET_ACCESS			21
#define CCE_SET_PASSWORD		22
#define CCR_LIST_IGNORES		23 // Request only
#define CCR_LIST_FRIENDS		24 // Request only
#define CCR_LIST_CHANNELS		25 // Request only
#define CCR_LIST_ALL_CHANNELS	26 // Request only
#define CCR_UNKNOWN				27
#define CCR_FRIEND_STATUS_ONLY	28 // Request only
#define CCR_ANYONE_STATUS		29 // Request only
#define CCR_SECTOR_LOGIN		30 // Request only
#define CCE_GMGAG				31
#define CCE_GMUNGAG				32

struct ClientChatRequest
{
    long    PlayerID;
    long    type;				// type of request
    short   string_length1;
	char	string1[1];			// string of length1 bytes
	short	_string_length2;	// do not access from here on, reference only
	char	_string2[1];		// string of length2 bytes
	short	_string_length3;
	char	_string3[1];		// string of length3 bytes
	long	_data_size;			// size of following block
	char	_unknown_data[1];	// optional block of data of data_size length
} ATTRIB_PACKED;

#define CHAT_LIST_FRIENDS			0
#define CHAT_LIST_IGNORES			1
#define CHAT_LIST_MEMBERS_CHANNEL	2
#define CHAT_LIST_ACTIVE_CHANNELS	3
#define CHAT_LIST_CURRENT_CHANNELS	4
struct ClientChatList
{
	long ListType;				// list id
	char unknown_string[2];		// string, empty is 2 bytes (unicode?)
	long count1;				// size of following array
	BSTR _players[1];			// array of players
	long _count2;				// size of following array
	BSTR _list2[1];				// array of matching info
} ATTRIB_PACKED;

//ClientChatError.reason
#define CHAT_ERROR_OK					0	// "ok"
#define CHAT_ERROR_UNKNOWN				1	// "unknown_error"
#define CHAT_ERROR_INVALID_CHANNEL		2	// "invalid channel"
#define CHAT_ERROR_INVALID_PARAM		3	// "invalid parameter"
#define CHAT_ERROR_INVALID_PERSON		4	// "invalid person"
#define CHAT_ERROR_DUPLICATE_NAME		5	// "duplicate name"
#define CHAT_ERROR_NOT_A_MEMBER			6	// "not a member"
#define CHAT_ERROR_ALREADY				7	// "already"
#define CHAT_ERROR_IS_IGNORED			8	// "is ignored"
#define CHAT_ERROR_IS_GAGGED			9	// "is gagged"
#define CHAT_ERROR_IS_BANNED			10	// "is banned"
#define CHAT_ERROR_IS_SYSTEM_CHANNEL	11	// "is system channel"
#define CHAT_ERROR_NEED_PASSWORD		12	// "need password"
#define CHAT_ERROR_WRONG_PASSWORD		13	// "wrong password"
#define CHAT_ERROR_NO_PERMISSION		14	// "no permission"
#define CHAT_ERROR_BAD_NAME				15	// "bad name"
#define CHAT_ERROR_NO_MEMORY			16	// "no memory"
#define CHAT_ERROR_YOURSELF				17	// "yourself"
#define CHAT_ERROR_REACHED_LIMIT		18	// "reached max limit"

struct ClientChatError
{
	long reason;
	long type;
	short string_length1;
	char player[1];
	short _string_length2;
	char _channel[1];
	short _string_length3;
	char _other[1];
} ATTRIB_PACKED;

// ClientChatEvent.type
#define CHEV_LOGGED_IN				1
#define CHEV_LOGGED_OUT				2
#define CHEV_CHANNEL_MESSAGE		3
#define CHEV_PRIVATE_MESSAGE		4
#define CHEV_ORANGE_MESSAGE_7		5
#define CHEV_SYSTEM_MESSAGE			6
#define CHEV_UNKNOWN				7
#define CHEV_UNKNOWN_GUILD			8
#define CHEV_CHANNEL_CREATED		9
#define CHEV_MISSING_TYPE			10
#define CHEV_REMOVED_CHANNEL		11
#define CHEV_INVITED				12
#define CHEV_UNINVITED				13
#define CHEV_GAGGED					14
#define CHEV_UNGAGGED				15
#define CHEV_BANNED					16
#define CHEV_UNBANNED				17
#define CHEV_KICKED					18
#define CHEV_NOW_IGNORING			19	
#define CHEV_NO_LONGER_IGNORING		20
#define CHEV_NOW_FRIENDS			21
#define CHEV_NO_LONGER_FRIENDS		22
#define CHEV_ADDED_OWNER			23
#define CHEV_REMOVED_OWNER			24
#define CHEV_FRIEND_STATUS_ONLY		25
#define CHEV_ALL_STATUS				26
#define CHEV_GAGGED_BY_GM			27
#define CHEV_UNGAGGED_BY_GM			28

struct ClientChatEvent
{
	long type;				// type of event
	long unknown;			// dont know yet, only used for some types
	short string_length1;
	char firstname[1];		// rank?
	short string_length2;
	char lastname[1];		// player name
	short string_length3;
	char otherplayer[1];	// events affecting another player
	short string_length4;
	char channel[1];		// channel name
	short string_length5;
	char message[1];		// message
	short string_length6;
	char unknown_string[1];	// not referenced
	long customcount;		// used for private messages in some way
	char custombytes[1];
} ATTRIB_PACKED;

struct ClientSkillsRequest
{
	long PlayerID;
	long unknown1;
} ATTRIB_PACKED;

struct StarbaseAvatarChange
{
    long    AvatarID;
    long    RoomType;
    float   Orient;
    float   Position[3];
    long    ActionFlag;
} ATTRIB_PACKED;

struct StarbaseAvatarChange_S2C
{
    long    AvatarID;
    float   Orient;
    float   Position[3];
    long    ActionFlag;
    long    Room;
} ATTRIB_PACKED;

struct StarbaseRoomChange
{
    long    AvatarID;
    long    NewRoom;
    long    OldRoom;
} ATTRIB_PACKED;

struct StarbaseRequest
{
    long    PlayerID;
    long    StarbaseID;
    char    Action;
} ATTRIB_PACKED;

#define RELATIONSHIP_ATTACK     0
#define RELATIONSHIP_SHUN       1
#define RELATIONSHIP_FRIENDLY   2
#define RELATIONSHIP_ADORATION  3

struct Relationship
{
    long    ObjectID;
    long    Reaction;
    char    IsAttacking;
} ATTRIB_PACKED;

struct ObjectEffect             // opcode 0x09
{
    char    Bitmask;            // bitfield of flags
    long    GameID;
    short   EffectDescID;
    long    EffectID;           // bit 0
    unsigned long TimeStamp;    // bit 1
    short   Duration;           // bit 2
    float   Scale;              // bit 3
    float   HSVShift[3];        // bit 4,5,6
} ATTRIB_PACKED;

struct ObjectToObjectEffect             // opcode 0x0B
{
    u16		Bitmask;            // 4 flags for condional fields
    long    GameID;
	long	TargetID;
    u16		EffectDescID;
	char	*Message;
    // the following fields are not always present, inclusion depends on bitmask
    long    EffectID;           // bit 0 mask 0x0001
    unsigned long TimeStamp;    // bit 1 mask 0x0002
	u16		Duration;			// bitmask[2] 2 bytes (time is in milli seconds)
	float	TargetOffset[3];	// bitmask[3] 12 bytes
	u16		OutsideTargetRadius;// bitmask[4] 2 bytes
	u16		unused;				// bitmask[5] 2 bytes
	float	Scale;				// bitmask[6] 4 bytes
	float	HSVShift[3];		// bitmask[7] 12 bytes
	float	Speedup;			// bitmask[8] 4 bytes
} ATTRIB_PACKED;

struct InitRenderState			// opcode 0x2f
{
	long	GameID;
	unsigned long RenderStateID;
} ATTRIB_PACKED;

struct ActivateRenderState      // opcode 0x30
{
    long    GameID;                 // this[12] 4 bytes
    unsigned long RenderStateID;    // this[20] 4 bytes
} ATTRIB_PACKED;

struct SimplePositionalUpdate
{
    long    GameID;                 // this[12] 4 bytes
    unsigned long TimeStamp;        // this[16] 4 bytes
    float   Position[3];            // this[20] 12 bytes
    float   Orientation[4];         // this[32] 16 bytes
    float   Velocity[3];            // this[48] 12 bytes
} ATTRIB_PACKED;

struct PlanetPositionalUpdate
{
    long    GameID;                 // this[12] 4 bytes
    unsigned long TimeStamp;        // this[16] 4 bytes
    float   Position[3];            // this[20] 12 bytes
    long    OrbitID;                // this[32] 4 bytes
    float   OrbitDist;              // this[36] 4 bytes
    float   OrbitAngle;             // this[40] 4 bytes
    float   OrbitRate;              // this[44] 4 bytes
    float   RotateAngle;            // this[48] 4 bytes
    float   RotateRate;             // this[52] 4 bytes
    float   TiltAngle;              // this[56] 4 bytes
} ATTRIB_PACKED;

struct ComponentPositionalUpdate
{
    struct  SimplePositionalUpdate simple;  // this[12] 48 bytes
    float   ImpartedDecay;                  // this[68] 4 bytes
    float   TractorSpeed;                   // this[72] 4 bytes
    long    TractorID;                      // this[76] 4 bytes
    long    TractorEffectID;                // this[80] 4 bytes
} ATTRIB_PACKED;

struct AdvancedPositionalUpdate
{
    short   Bitmask;                // flags for condional fields
    long    GameID;                 // this[12] 4 bytes
    unsigned long TimeStamp;        // this[16] 4 bytes
    float   Position[3];            // this[20] 12 bytes
    float   Orientation[4];         // this[32] 16 bytes
    unsigned long MovementID;       // this[100] 4 bytes
    // the following fields are not always present, inclusion depends on bitmask
    float   CurrentSpeed;           // this[48] 4 bytes     bit 0  0x0001
    float   SetSpeed;               // this[52] 4 bytes     bit 1  0x0002
    float   Acceleration;           // this[56] 4 bytes     bit 2  0x0004
    float   RotY;                   // this[60] 4 bytes     bit 3  0x0008
    float   DesiredY;               // this[64] 4 bytes     bit 4  0x0010
    float   RotZ;                   // this[68] 4 bytes     bit 5  0x0020
    float   DesiredZ;               // this[72] 4 bytes     bit 6  0x0040
    float   ImpartedVelocity[3];    // this[76] 12 bytes    bit 7  0x0080
    float   ImpartedSpin;           // this[88] 4 bytes     bit 7  0x0080
    float   ImpartedRoll;           // this[92] 4 bytes     bit 7  0x0080
    float   ImpartedPitch;          // this[96] 4 bytes     bit 7  0x0080
    unsigned long UpdatePeriod;     // this[104] 4 bytes    bit 8  0x0100
} ATTRIB_PACKED;

struct EquipUse
{
    long    GameID;      // 4 bytes
    char	InvNum;      // 1 bytes
	char	InvSlot;     // 1 bytes
} ATTRIB_PACKED;

struct AbilityUse
{
    long    GameID;      // 4 bytes
    long	UnKnown;     // 4 bytes
	long	Ability;     // 4 bytes
} ATTRIB_PACKED;

struct StarbaseSet
{
    long    StarbaseID;
    char    Action;
    char    ExitMode;
} ATTRIB_PACKED;

struct ServerHandoff
{
    MasterJoin  join;
    char        variable_data[128];
} ATTRIB_PACKED;

struct ShipInfo
{
    long    hull;
    long    profession;
    long    engine;
    long    wing;
    float   Position[3];
    float   Orientation[4];
} ATTRIB_PACKED;

struct CharacterDatabase
{
    AvatarInfo      info;               // 133 bytes
    AvatarData      avatar;             // 241 bytes
    ShipData        ship_data;          // 194 bytes
    ShipInfo        ship_info;
} ATTRIB_PACKED;

struct CTARequest
{
	long SourceID;
	long TargetID;
	long Action;
} ATTRIB_PACKED;

struct MovePacket
{
    long GameID;
    char type;
} ATTRIB_PACKED;

struct SkillAction
{
	long	GameID;
	int		SkillPoints;
	short	SkillID;
} ATTRIB_PACKED;

struct SkillUse // Opcode 0x58
{
    long GameID;
    long Action;
    long AbilityIndex;
} ATTRIB_PACKED;

struct MissionDismissal
{
	long PlayerID;
	long MissionID;					// Could be 2 or 4 bytes, the 1st 2 bytes are always 0 from my observations
} ATTRIB_PACKED;

struct MVASHandoff
{
	long	player_id;
	long	port;
} ATTRIB_PACKED;


// LoungeNPC Structure

struct StationData {
	int	StationType;
	int	RoomNumber;
} ATTRIB_PACKED;

struct StationRooms {
	int RoomNumber;
	int	RoomStyle;
	float FogNear;
	float FogFar;

	float FogRed;
	float FogGreen;
	float FogBlue;
} ATTRIB_PACKED;

// Term Number

struct StationTerms {
	int	RoomNumber;
	int Location;
	int TermType;
	int Unknown;
} ATTRIB_PACKED;

// NPC Number

struct StationNPC {
	int RoomNumber;
	int	Location;
	int NPCID;
	int BoothType;
	int Unknown1;
	int Unknown2;
	struct AvatarData Avatar;
} ATTRIB_PACKED;

struct StationLounge {
	struct StationData	Station;
	struct StationRooms	Rooms[5];
	int					NumTerms;
	struct StationTerms	Terms[15];
	int					NumNPCs;
	StationNPC	NPC[30];
} ATTRIB_PACKED;

struct ManufactureData
{
	long GameID;
	long Data;
} ATTRIB_PACKED;

struct ManufactureTechLevelFilter
{
    long GameID;
    char Enable;
    long BitField;
} ATTRIB_PACKED;

struct FindMember
{
	long count;
	struct fm_item
	{
		long GameID;		// reverse bytes
		long Level;			// reverse bytes
		long Race;			// reverse bytes
		long Profession;	// reverse bytes
	} list[1];			// array of count * 16 byte structures
} ATTRIB_PACKED;

struct RecustomizeAvatarStart
{
	long costs[14];
	long playerid;
} ATTRIB_PACKED;

struct RecustomizeShipStart
{
	struct ShipData ship;
	long costs[12];
	long playerid;
	long unknown[4];
} ATTRIB_PACKED;

struct RecustomizeShipDone
{
	struct ShipData ship;
	long playerid;
	bool unknown;
	char _unknown[11];
} ATTRIB_PACKED;

struct RecustomizeAvatarDone
{
	struct AvatarData avatar;
	long playerid;
	bool unknown;
	char _unknown[11];
} ATTRIB_PACKED;

#endif // _PACKET_STRUCTURES_H_INCLUDED_
