#ifndef _GUILDS_H_INCLUDED_
#define _GUILDS_H_INCLUDED_

// ENB_OPCODE_00D0_GUILD_MESSAGE_SECTOR types
#define GUILD_MSG_INVALID						0
#define GUILD_MSG_SUCCESS1						1
#define GUILD_MSG_SUCCESS2						2
#define GUILD_MSG_LIST_MEMBERS					3 // name rank \n ...
#define GUILD_MSG_LIST_UNKNOWN					4 // no visible effect
#define GUILD_MSG_LIST_LOWER_PANE				5 // echo to lower chat pane
#define GUILD_MSG_MOTD							6 // 'Guild MOTD : $OtherName$'
#define GUILD_MSG_INTERNAL_ERROR				7 // 'Guild : Internal error while processing that command.'
#define GUILD_MSG_INVALID_PLAYER				8 // 'Guild : $OtherName$ is not a valid player name.'
#define GUILD_MSG_INVALID_GUILD					9 // 'Guild : $GuildName$ is not a valid guild name.'
#define GUILD_MSG_NOT_IN_A_GUILD				10 // 'Guild : You must be in a guild to issue this command.'
#define GUILD_MSG_NOT_IMPLEMENTED				11 // 'Guild : Not implemented yet.'
#define GUILD_MSG_INVALID_COMMAND				12 // 'Guild : Invalid command.'
#define GUILD_MSG_AVAILABLE_COMMANDS			13 // ...
#define GUILD_MSG_USAGE_PROMOTE					14 // 'Guild : Usage /gc promote [player name] - If you do not specify a player name, you must have the player targetted.'
#define GUILD_MSG_USAGE_DEMOTE					15 // 'Guild : Usage /gc demote [player name] - If you do not specify a player name, you must have the player targetted.'
#define GUILD_MSG_USAGE_RECRUIT					16 // 'Guild : Usage /gc recruit <player name> - You must specify the player name.'
#define GUILD_MSG_USAGE_REMOVE					17 // 'Guild : Usage /gc remove <player name> - You must specify the player name.'
#define GUILD_MSG_USAGE_STATS					18 // 'Guild : Usage /gc stats [guild name] - Request statistics for the named guild, or for your own guild if no name is specified.'
#define GUILD_MSG_USAGE_PUBLICSTATS				19 // 'Guild : Usage /gc publicstats <on | off> - You must specify ',27h,'on',27h,' or ',27h,'off',27h,'.'
#define GUILD_MSG_USAGE_CONTRIBUTIONS			20 // 'Guild : Usage /gc contributions <player name> - You must specify the name of a player in your guild.'
#define GUILD_MSG_USAGE_TAG						21 // 'Guild : Usage /gc tag <player name> "tag string" - You must specify both the player name and the tag string. The tag string must be enclosed in double-quotes if it contains more than one word.'
#define GUILD_MSG_USAGE_RANKNAME				22 // 'Guild : Usage /gc rankname <current rank name / number 0-9> [new rank name] - Rank names must be wrapped in double-quotes if they are more than one word. If no new rank name is specified, the current rank name will be displayed. NOTE - $OtherName$ credits will be deducted if you rename a rank.'
#define GUILD_MSG_USAGE_MAXPROMOTE				23 // 'Guild : Usage /gc set maxpromote <rank name / number 0-9> [new max promote rank name / number 0-9] - You must specify the rank to modify. If you do not specify a new max promote rank, that ability will be revoked. Rank names must be wrapped in double-quotes if they are more than one word.'
#define GUILD_MSG_USAGE_MAXREMOVE				24 // 'Guild : Usage /gc set maxremove <rank name / number 0-9> [new max remove rank name / number 0-9] - You must specify the rank to modify. If you do not specify a new max remove rank, that ability will be revoked. Rank names must be wrapped in double-quotes if they are more than one word.'
#define GUILD_MSG_USAGE_MINDEMOTE				25 // 'Guild : Usage /gc set mindemote <rank name / number 0-9> [new min demote rank name / number 0-9] - You must specify the rank to modify. If you do not specify a new min demote rank, that ability will be revoked. Rank names must be wrapped in double-quotes if they are more than one word.'
#define GUILD_MSG_USAGE_VIEW					26 // 'Guild : Usage /gc view <rank name / number 0-9> - You must specify a rank name (wrapped in double-quotes if more than one word) or a rank number 0-9.'
#define GUILD_MSG_USAGE_PERMISSIONS_ADD			27 // 'Guild : Usage /gc permission[s] add <rank name / number 0-9> <permission name / number> - You must specify valid rank, and a permission to grant. Rank and permission names must be wrapped in double-quotes if they are more than one word. Permission numbers can be seen using /gc permission list.'
#define GUILD_MSG_USAGE_PERMISSIONS_REMOVE		28 // 'Guild : Usage /gc permission[s] remove <rank name / number 0-9> <permission name / number> - You must specify valid rank, and a permission to remove. Rank and permission names must be wrapped in double-quotes if they are more than one word. Permission numbers can be seen using /gc permission list.'
#define GUILD_MSG_USAGE_SET_ACTIVE				29 // 'Guild : Usage /gc set active <player name> - You must specify the name of a player in your guild.'
#define GUILD_MSG_USAGE_SET_INACTIVE			30 // 'Guild : Usage /gc set inactive <player name> - You must specify the name of a player in your guild.'
#define GUILD_GMMSG_USAGE_DISBAND				31 // 'GM Command : Usage /gmgc disband "guild name" - You must specify the name of the guild to disband. Enclose in double-quotes if it is more than one word.'
#define GUILD_GMMSG_USAGE_RENAME				32 // 'GM Command : Usage /gmgc rename "guild name" "new guild name" - You must specify the old and new guild names, and the names must be wrapped in double-quotes.'
#define GUILD_GMMSG_USAGE_REMOVE				33 // 'GM Command : Usage /gmgc remove <player name> - You must specify the player name.'
#define GUILD_GMMSG_USAGE_PROMOTE				34 // 'GM Command : Usage /gmgc promote <player name> - You must specify the player name.'
#define GUILD_GMMSG_USAGE_DEMOTE				35 // 'GM Command : Usage /gmgc demote <player name> - You must specify the player name.'
#define GUILD_GMMSG_USAGE_LIST_ONLINE			36 // 'GM Command : Usage /gmgc list [online] "guild name" - You must specify the name of the guild to examine. Enclose in double-quotes if it is more than one word.'
#define GUILD_GMMSG_USAGE_LIST_OFFLINE			37 // 'GM Command : Usage /gmgc list offline "guild name" - You must specify the name of the guild to examine. Enclose in double-quotes if it is more than one word.'
#define GUILD_GMMSG_USAGE_LIST_ALL				38 // 'GM Command : Usage /gmgc list all "guild name" - You must specify the name of the guild to examine. Enclose in double-quotes if it is more than one word.'
#define GUILD_GMMSG_USAGE_LIST_ACTIVE			39 // 'GM Command : Usage /gmgc list active "guild name" - You must specify the name of the guild to examine. Enclose in double-quotes if it is more than one word.'
#define GUILD_GMMSG_USAGE_LIST_INACTIVE			40 // 'GM Command : Usage /gmgc list inactive "guild name" - You must specify the name of the guild to examine. Enclose in double-quotes if it is more than one word.'
#define GUILD_GMMSG_USAGE_RANKNAME				41 // 'GM Command : Usage /gmgc rankname "guild name" <rank name / number 0-9> [new rank name] - Guild and rank names must be enclosed in double-quotes if they are more than one word. If no new rank name is specified, the current rank name will be displayed.'
#define GUILD_GMMSG_USAGE_VIEW					42 // 'GM Command : Usage /gmgc view "guild name" <rank name / number 0-9> - Guild and rank names must be enclosed in double-quotes if they are more than one word.'
#define GUILD_GMMSG_USAGE_PERMS_ADD				43 // 'GM Command : Usage /gmgc permission[s] add "guild name" <rank name / number 0-9> <permission name / number> - You must specify a valid guild name, rank, and a permission to grant. Guild, rank and permission names must be enclosed in double-quotes if they are more than one word. Permission numbers can be seen using /gmgc permission[s] list.'
#define GUILD_GMMSG_USAGE_PERMS_REMOVE			44 // 'GM Command : Usage /gmgc permission[s] remove "guild name" <rank name / number 0-9> <permission name / number> - You must specify a valid guild name, rank, and a permission to remove. Guild, rank and permission names must be enclosed in double-quotes if they are more than one word. Permission numbers can be seen using /gmgc permission[s] list.'
#define GUILD_GMMSG_USAGE_MODIFY_GUILD_POINTS	45 // 'GM Command : Usage /gmgc modifyguildpoints "guild name" <num guild points> - You must enclose the guild name in double-quotes if it is more than one word. You can specify a positive or negative value for the number of guild points (just not zero).'
#define GUILD_GMMSG_USAGE_MAXPROMOTE			46 // 'GM Command : Usage /gmgc set maxpromote "guild name" <rank name / number 0-9> [new max promote rank name / number 0-9] - You must specify the rank to modify. If you do not specify a new max promote rank, that ability will be revoked. Guild and rank names must be wrapped in double-quotes if they are more than one word.'
#define GUILD_GMMSG_USAGE_MAXREMOVE				47 // 'GM Command : Usage /gmgc set maxremove "guild name" <rank name / number 0-9> [new max remove rank name / number 0-9] - You must specify the rank to modify. If you do not specify a new max remove rank, that ability will be revoked. Guild and rank names must be wrapped in double-quotes if they are more than one word.'
#define GUILD_GMMSG_USAGE_MINDEMOTE				48 // 'GM Command : Usage /gmgc set mindemote "guild name" <rank name / number 0-9> [new min demote rank name / number 0-9] - You must specify the rank to modify. If you do not specify a new min demote rank, that ability will be revoked. Guild and rank names must be wrapped in double-quotes if they are more than one word.'
#define GUILD_GMMSG_USAGE_SET_ACTIVE			49 // 'GM Command : Usage /gmgc set active <player name> - You must specify the name of a player in your guild.'
#define GUILD_GMMSG_USAGE_SET_INACTIVE			50 // 'GM Command : Usage /gmgc set inactive <player name> - You must specify the name of a player in your guild.'
#define GUILD_GMMSG_USAGE_MESSAGE				51 // 'GM Command : Usage /gmgc message "guild name" "new message of the day" - You must enclose the guild name and the new message of the day in double-quotes.'
#define GUILD_MSG_ALL_GROUP_NOT_IN_A_GUILD		52 // 'Guild : All group members must not already be part of other guilds.'
#define GUILD_MSG_ALREADY_A_FOUNDER				53 // 'Guild : $OtherName$ is already a founder of another pending guild, unable to continue guild formation.'
#define GUILD_MSG_GUILD_NAME_VIOLATION			54 // 'Guild : The guild name ',27h,'$GuildName$',27h,' violates the Terms of Service agreement and cannot be used. Please choose another name for your guild.'
#define GUILD_MSG_PLAYER_NOT_FOUND				55 // 'Guild : No player named ',27h,'$OtherName$',27h,' was found.'
#define GUILD_MSG_GUILD_NAME_IN_USE				56 // 'Guild : That guild name is already in use. Please choose another.'
#define GUILD_MSG_GUILD_ALREADY_CREATING		57 // 'Guild : A guild of that name is already in the process of being created. Try again later.'
#define GUILD_MSG_REGISTRATION_FEE_DEDUCTED		58 // 'Guild : The guild registration fee of $OtherName$ credits has been deducted from your account.'
#define GUILD_MSG_NOT_ENOUGH_CREDITS_ABORTED	59 // 'Guild : You no longer have enough credits to cover the guild registration fee! Guild creation aborted.'
#define GUILD_MSG_REGISTRATION_FEE_REFUNDED		60 // 'Guild : Your guild registration fee has been refunded.'
#define GUILD_MSG_MUST_BE_GUILD_TO_RECRUIT		61 // 'Guild : You must be in a guild in order to recruit a member!'
#define GUILD_MSG_ALREADY_IN_A_GUILD			62 // 'Guild : You cannot recruit someone who is already in a guild.'
#define GUILD_MSG_NOT_HIGH_ENOUGH_TO_RECRUIT	63 // 'Guild : Your guild rank is not high enough to recruit members!'
#define GUILD_MSG_GUILD_FORMATION_ABORTED_BY	64 // 'Guild : The formation of $GuildName$ was aborted by $OtherName$!'
#define GUILD_MSG_ABORT_PLAYER_LEFT_SECTOR		65 // 'Guild : Guild creation aborted because $OtherName$ left the sector!'
#define GUILD_MSG_DECLINED_INVITATION			66 // 'Guild : $OtherName$ has declined your invitation to join $GuildName$!'
#define GUILD_MSG_HAS_BEEN_RECRUITED			67 // 'Guild : $OtherName$ has been recruited into $GuildName$!'
#define GUILD_MSG_MUST_BE_SAME_GUILD			68 // 'Guild : You must be in the same guild as $OtherName$ to use this command.'
#define GUILD_MSG_NEW_RANK						69 // 'Guild : Your guild rank has been set to ',27h,'$OtherName$!',27h
#define GUILD_MSG_CANT_PROMOTE_SELF				70 // 'Guild : You cannot promote yourself!'
#define GUILD_MSG_NOT_HIGH_ENOUGH_TO_PROMOTE	71 // 'Guild : Your guild rank is not high enough to promote $OtherName$!'
#define GUILD_MSG_YOU_HAVE_PROMOTED				72 // 'Guild : You have promoted $OtherName$ to ',27h,'$GuildName$',27h,' status!'
#define GUILD_MSG_CANT_DEMOTE_SELF				73 // 'Guild : You cannot demote yourself!'
#define GUILD_MSG_CANT_DEMOTE_FURTHER			74 // 'Guild : $OtherName$ cannot be demoted any further!'
#define GUILD_MSG_NOT_HIGH_ENOUGH_TO_DEMOTE		75 // 'Guild : Your guild rank is not high enough to demote $OtherName$!'
#define GUILD_MSG_YOU_HAVE_DEMOTED				76 // 'Guild : You have demoted $OtherName$ to ',27h,'$GuildName$',27h,' status!'
#define GUILD_MSG_NOT_HIGH_ENOUGH_TO_REMOVE		77 // 'Guild : Your guild rank is not high enough to remove $OtherName$ from the guild!'
#define GUILD_MSG_YOU_HAVE_REMOVED				78 // 'Guild : You have removed $OtherName$ from the guild!'
#define GUILD_MSG_YOU_HAVE_BEEN_REMOVED			79 // 'Guild : You have been removed from the guild by $OtherName$!'
#define GUILD_MSG_CANT_LEAVE_GUILD				80 // 'Guild : You may not leave the guild, you must promote another officer to leader first!'
#define GUILD_MSG_ABOUT_TO_REMOVE_LEADER		81 // 'Guild : You are about to remove the leader of a Guild.  Make sure you have promoted some other guild member to leader before proceding!'
#define GUILD_MSG_YOU_HAVE_LEFT_GUILD			82 // 'Guild : You have left $GuildName$!'
#define GUILD_MSG_NOT_HIGH_ENOUGH_TO_DISBAND	83 // 'Guild : Your guild rank is not high enough to disband the guild!'
#define GUILD_MSG_YOU_HAVE_DISBANDED			84 // 'Guild : You have disbanded $GuildName$!'
#define GUILD_MSG_HAS_BEEN_DISBANDED			85 // 'Guild : Your guild has been disbanded!'
#define GUILD_MSG_NOT_HIGH_ENOUGH_TO_MESSAGE	86 // 'Guild : Your guild rank is not high enough to set the guild',27h,'s message of the day!'
#define GUILD_MSG_CHANGED_GUILD_MESSAGE			87 // 'Guild : You have changed $GuildName$',27h,'s message of the day.'
#define GUILD_MSG_STATS_NOT_AVAILABLE			88 // 'Guild : The guild statistics of $GuildName$ are not available to non-members.'
#define GUILD_MSG_NOT_HIGH_ENOUGH_TO_PUBLIC		89 // 'Guild : Your guild rank is not high enough to make the guild',27h,'s statistics publicly visible!'
#define GUILD_MSG_STATS_CAN_BE_SEEN				90 // 'Guild : $GuildName$',27h,'s statistics may now be seen by non-members.'
#define GUILD_MSG_STATS_CANT_BE_SEEN			91 // 'Guild : $GuildName$',27h,'s statistics may no longer be seen by non-members.'
#define GUILD_MSG_NOT_HIGH_ENOUGH_TO_CONTRIB	92 // 'Guild : Your guild rank is not high enough to see $OtherName$',27h,'s guild contributions!'
#define GUILD_MSG_NOT_HIGH_ENOUGH_TO_TAG		93 // 'Guild : Your guild rank is not high enough to set $OtherName$',27h,'s guild tag!'
#define GUILD_MSG_HAVE_SET_TAG					94 // 'Guild : You have set $OtherName$',27h,'s guild tag.'
#define GUILD_MSG_YOU_HAVE_JOINED_GUILD			95 // 'Guild : You have joined $GuildName$!'
#define GUILD_MSG_NOT_HIGH_ENOUGH_TO_RANKNAME	96 // 'Guild : You do not have permission to rename guild ranks!'
#define GUILD_MSG_RANK_RENAMED					97 // 'Guild : Rank renamed to $OtherName$.'
#define GUILD_MSG_RANK_NAME_VIOLATION			98 // 'Guild : That rank name violates the Terms of Service agreement. Please choose a different name.'
#define GUILD_MSG_VIEW_RANK_PERMISSIONS			99 // 'Guild : Members of the "$OtherName$" rank have the following permissions:\nGuild : $GuildName$'
#define GUILD_MSG_NOT_HIGH_ENOUGH_TO_PERMISSION	100 // 'Guild : You do not have permission to modify $OtherName$ rank abilites!'
#define GUILD_MSG_MAX_PROMOTE_HIGHER			101 // 'Guild : You cannot specify a Max Promote rank that is higher than the rank you are modifying.'
#define GUILD_MSG_MAX_REMOVE_HIGHER				102 // 'Guild : You cannot specify a Max Remove rank that is higher than the rank you are modifying.'
#define GUILD_MSG_MIN_DEMOTE_LOWER				103 // 'Guild : You can only specify a Min Demote rank that is at least two ranks below than the rank you are modifying.'
#define GUILD_MSG_RANK_CAN_PROMOTE_TO			104 // 'Guild : Members of $OtherName$ rank can now promote guild members up to the rank of $GuildName$'
#define GUILD_MSG_RANK_CAN_REMOVE_TO 			105 // 'Guild : Members of $OtherName$ rank can now remove guild members up to the rank of $GuildName$'
#define GUILD_MSG_RANK_CAN_DEMOTE_TO			106 // 'Guild : Members of $OtherName$ rank can now demote guild members down to the rank of $GuildName$'
#define GUILD_MSG_RANK_NO_LONGER_PROMOTE 		107	// 'Guild : Members of $OtherName$ rank can no longer promote anyone.'
#define GUILD_MSG_RANK_NO_LONGER_REMOVE			108 // 'Guild : Members of $OtherName$ rank can no longer remove anyone.'
#define GUILD_MSG_RANK_NO_LONGER_DEMOTE			109 // 'Guild : Members of $OtherName$ rank can no longer demote anyone.'
#define GUILD_MSG_AVAILABLE_PERMISSIONS			110 // 'Guild : Permissions that can be assigned to guild ranks:\n'$OtherName$'
#define GUILD_MSG_RANK_ALREADY_HAS_PERMISSION	111 // 'Guild : The ',27h,'$OtherName$',27h,' rank already has the ',27h,'$GuildName$',27h,' permission.'
#define GUILD_MSG_RANK_DOESNT_HAVE_PERMISSION	112 // 'Guild : The ',27h,'$OtherName$',27h,' rank does not have the ',27h,'$GuildName$',27h,' permission.'
#define GUILD_MSG_PERMISSION_ADDED				113 // 'Guild : Successfully added the ',27h,'$GuildName$',27h,' permission to ',27h,'$OtherName$',27h,'.'
#define GUILD_MSG_PERMISSION_REMOVED			114 // 'Guild : Successfully removed the ',27h,'$GuildName$',27h,' permission from ',27h,'$OtherName$',27h,'.
#define GUILD_MSG_NOT_HIGH_ENOUGH_TO_ACTIVE		115 // 'Guild : You do not have permission to make somebody active.'
#define GUILD_MSG_NOT_HIGH_ENOUGH_TO_INACTIVE 	116 // 'Guild : You do not have permission to make somebody inactive.'
#define GUILD_MSG_ALREADY_ACTIVE 				117 // 'Guild : $OtherName$ is already active.'
#define GUILD_MSG_ALREADY_INACTIVE 				118 // 'Guild : $OtherName$ is already inactive.'
#define GUILD_MSG_MARKED_ACTIVE					119 // 'Guild : $OtherName$ successfully marked as active.'
#define GUILD_MSG_MARKED_INACTIVE	 			120 // 'Guild : $OtherName$ successfully marked as inactive.'
#define GUILD_MSG_CHARGED_A_FEE					121 // 'Guild : You have been charged a fee of $OtherName$ credits.'
#define GUILD_MSG_INACTIVE_LEADER_X_PROMOTED	122 // 'GUILD ANNOUNCEMENT : Your Guild Leader ',27h,'$OtherName$',27h,' has not logged in for the past 30 days. ',27h,'$GuildName$',27h,' has been promoted to Guild Leader in his/her place!'
#define GUILD_MSG_DELETED_LEADER_X_PROMOTED 	123 // 'GUILD ANNOUNCEMENT : Your Guild Leader ',27h,'$OtherName$',27h,' has deleted his/her character. ',27h,'$GuildName$',27h,' has been promoted to Guild Leader in his/her place!'
#define GUILD_MSG_NO_LEADER_AUTO_PROMOTION		124 // 'GUILD ANNOUNCEMENT : ',27h,'$GuildName$',27h,' has been automatically promoted to Guild Leader because your guild was leaderless!'
#define GUILD_GMMSG_NOT_HIGH_ENOUGH_TO_DISBAND	125 // 'GM Command : Your admin level is not high enough to disband a guild!!'
#define GUILD_GMMSG_GUILD_DISBANDED				126 // 'GM Command : Success! ',27h,'$GuildName$',27h,' is no more!'
#define GUILD_GMMSG_NOT_HIGH_ENOUGH_TO_RENAME	127 // 'GM Command : Your admin level is not high enough to rename a guild!!'
#define GUILD_GMMSG_GUILD_RENAMED				128 // 'GM Command : You have renamed ',27h,'$OtherName$',27h,' to ',27h,'$GuildName$'
#define GUILD_GMMSG_NOT_VALID_GUILD_NAME		129 // 'GM Command : ',27h,'$GuildName$',27h,' is not a valid guild name.'
#define GUILD_GMMSG_MODIFIED_GUILD_POINTS		130 // 'GM Command : Guild points have been modified by $OtherName$. New guild point total is $GuildName$.'
#define GUILD_GMMSG_GUILD_MESSAGE_CHANGED		131 // 'GM Command : Successfully changed $GuildName$',27h,'s MOTD to ',27h,'$OtherName$'

//ENB_OPCODE_00CC_GUILD_SIMPLE_SECTOR_CLIENT types (most take the parameter)
#define GUILD_CREATE_CONFIRM		0
#define GUILD_PROMOTE_CONFIRM		1
#define GUILD_NEWLEADER_CONFIRM		3
#define GUILD_DEMOTE_CONFIRM		5
#define GUILD_REMOVE_CONFIRM		7
#define GUILD_LEAVE_CONFIRM			9	// no param
#define GUILD_DISBAND_CONFIRM		11	// no param
#define GUILD_GM_DISBAND_CONFIRM	13
#define GUILD_GM_REMOVE_CONFIRM		16
#define GUILD_GM_PROMOTE_CONFIRM	18
#define GUILD_GM_DEMOTE_CONFIRM		20

// permission bits
#define PERMISSION_SHOW_STATS		0x0001
#define PERMISSION_CONTRIBUTIONS	0x0002
#define PERMISSION_LEAVE			0x0004
#define PERMISSION_RECRUIT			0x0008
#define PERMISSION_REMOVE			0x0010
#define PERMISSION_MESSAGE			0x0020
#define PERMISSION_PROMOTE			0x0040
#define PERMISSION_DEMOTE			0x0080
#define PERMISSION_DISBAND			0x0100
#define PERMISSION_TAG				0x0200
#define PERMISSION_PUBLIC_STATUS	0x0400
#define PERMISSION_PRIVATE_STATUS	0x0800
//
#define PERM_LEADER					0x0FFF
#define PERM_OFFICER1				0x00FF
#define PERM_OFFICER2				0x00CF
#define PERM_NEWBIE					0x0005

#define MAX_GUILD_RANKS		10
#define MAX_GUILD_MEMBERS	150

// note: higher number is higher rank
struct GuildRank
{
	char Name[64];
	long PermissionFlags;
	long MaxPromote;	// 1 based
	long MaxRemove;		// 1 based
	long MinDemote;		// 1 based
} ATTRIB_PACKED;

struct GuildMember
{
	char Name[16];
	int Rank;
	int Contribution;
	bool Active; // and creation confirmation accept
	char Tag[32];
	long avatar_id;
} ATTRIB_PACKED;

struct Guild
{
// guild info
	int id;
	char Name[64];
	char MOTD[512];
	long Points;
	short Level;
	bool PublicStats;
// ranks
	struct GuildRank Ranks[MAX_GUILD_RANKS];
// members
	int Count;					    				// number of members
	struct GuildMember Members[MAX_GUILD_MEMBERS];	// max guild size = Dunbars number
// not saved
	bool pending;
} ATTRIB_PACKED;

#include <vector>
typedef std::vector<Guild*> GuildList;

#endif