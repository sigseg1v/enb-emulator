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

#include "PlayerClass.h"
#include "PlayerManager.h"
#include "Opcodes.h"
#include "PacketMethods.h"
#include "Guilds.h"

void Player::SetupGuildInfo(int id, int rank, bool autoadd)
{
	m_GuildID = id;
	Guild *g = g_PlayerMgr->GuildFromId(id);
	if (g)
	{
		float same[3]  = {0.0, 1.0, 0.0};
		float other[3] = {0.0, 0.0, 1.0};
		ShipIndex()->SetGuildName(g->Name);
		ShipIndex()->SetGuildRank(rank+1);	// this rank is 1 based
		ShipIndex()->SetGuildRankName(g_PlayerMgr->GetRankName(g, rank));
		ShipIndex()->SetSameGuildTagColor(same);
		ShipIndex()->SetOtherGuildTagColor(other);
		if (autoadd)
		{
			g_PlayerMgr->AddMemberToGuild(id, Name(), CharacterID(), rank);
			SaveGuildId(rank);
		}
		m_ChannelSubscription[g_PlayerMgr->GetChannelFromName("Guild")] = true;
	}
}

void Player::SendGuildMOTD()
{
	Guild *g = g_PlayerMgr->GuildFromId(m_GuildID);
	if (g)
	{
		SendGuildMessage(GUILD_MSG_MOTD, g->MOTD);
	}
}

void Player::HandleGuildCommand(char *param)
{
	int n = param ? strlen(param) : 0;

	if (n == 0)
	{
		SendGuildMessage(GUILD_MSG_AVAILABLE_COMMANDS);
	}
	else if (n >= 6 && strncmp(param,"create",6) == 0)
	{
		HandleCreateGuild(param+6);
	}
	else if (m_GuildID == -1)
	{
		SendGuildMessage(GUILD_MSG_NOT_IN_A_GUILD);
	}
	else if (strncmp(param,"list all",8) == 0)
	{
		HandleListAllGuildMembers();
		HandleCurrentPermissions();
	}
	else if (n >= 7 && strncmp(param,"message",7) == 0)
	{
		HandleGuildMOTD(param+7);
	}
	else if (n >= 7 && strncmp(param,"promote",7) == 0)
	{
		HandlePromoteMember(param+7, false);
	}
	else if (n >= 6 && strncmp(param,"demote",6) == 0)
	{
		HandleDemoteMember(param+6, false);
	}
	else if (n >= 6 && strncmp(param,"remove",6) == 0)
	{
		HandleRemoveMember(param+6, false);
	}
	else if (strncmp(param,"leave",5) == 0)
	{
		HandleLeaveGuild(false);
	}
	else if (strncmp(param,"disband",7) == 0)
	{
		HandleDisbandGuild(false);
	}
	else if (n >= 7 && strncmp(param,"recruit",7) == 0)
	{
		HandleRecruitMember(param+7);
	}
	else if (n >= 11 && strncmp(param,"publicstats",11) == 0)
	{
		HandlePublicStats(param+11);
	}
	else if (n >= 5 && strncmp(param,"stats",5) == 0)
	{
		HandleShowGuildStats(param+5);
	}
	else if (n >= 3 && strncmp(param,"tag",3) == 0)
	{
		HandleTagMember(param+3);
	}
	else if (n >= 13 && strncmp(param,"contributions",13) == 0)
	{
		HandleMemberContributions(param+13);
	}
	else
	{
		SendGuildMessage(GUILD_MSG_INVALID_COMMAND);
		LogMessage("Unknown /gc command '%s'\n", param);
	}
}

void Player::HandleCreateGuild(char *name)
{
	if (*name != ' ' || *(++name) == 0)
	{
		SendGuildMessage(GUILD_MSG_INVALID_GUILD, "", name);
	}
	else if (m_GuildID != -1)
	{
		SendGuildMessage(GUILD_MSG_INVALID_COMMAND);
	}
	else if (AdminLevel() < GM && (TotalLevel() < 15 || PlayerIndex()->GetCredits() < 10000 || GroupID() == -1 || 
		!PlayerIndex()->GroupInfo.GetIsGroupLeader() || g_PlayerMgr->GetMemberCount(GroupID()) < 6))
	{
		SendVaMessage("You must have 10000 credits, be at least overall level 15 and leader of a full group to create a guild!");
	}
	else if (g_PlayerMgr->GuildFromName(name, true))
	{
		SendGuildMessage(GUILD_MSG_GUILD_ALREADY_CREATING);
	}
	else if (g_PlayerMgr->GuildFromName(name, false))
	{
		SendGuildMessage(GUILD_MSG_GUILD_NAME_IN_USE);
	}
	else
	{
		Player *founders[6]={this,NULL,NULL,NULL,NULL,NULL};
		int okcount = 0;
		// get the group
		Group *g = g_PlayerMgr->GetGroupFromID(GroupID());
		if (g)
		{
			for(int x=0;x<6;x++)
			{
				long PlayerID = g->Member[x].GameID;
				if (PlayerID > 0)
				{
					Player *p = g_PlayerMgr->GetPlayer(PlayerID);
					// check if player is valid to be entered into list
					if (p)
					{
						if (p->GuildID() != -1)
						{
							SendGuildMessage(GUILD_MSG_ALL_GROUP_NOT_IN_A_GUILD);
							break;
						}
						else if (!IsInSameSector(p))
						{
							SendGuildMessage(GUILD_MSG_ABORT_PLAYER_LEFT_SECTOR, p->Name());
							break;
						}
						else if (g_PlayerMgr->CheckIfFounderOfPendingGuild(Name()))
						{
							SendGuildMessage(GUILD_MSG_ALREADY_A_FOUNDER, p->Name());
							break;
						}
						else
						{
							founders[x] = p;
							okcount++;
						}
					}
				}
			}
		}
		// check if group ok
		if (AdminLevel() >= GM || okcount == 6)
		{
			for(int x=0;x<6;x++)
			{
				if (founders[x])
				{
					founders[x]->SendGuildSimpleSectorClient(GUILD_CREATE_CONFIRM, name);
				}
			}
			// create a pending guild structure
			g_PlayerMgr->CreateGuild(founders, name);
		}
	}
}

void Player::HandleListAllGuildMembers()
{
	Guild *g = g_PlayerMgr->GuildFromId(m_GuildID);
	if (g)
	{
		bool alloced_mem = false;
		char *ptr;
		char *members;
		unsigned char *resp;

		//only resort to heap if we have to
		if ((4 + g->Count*(16+32+1) + 2) > 4096)
		{
			members = new char[g->Count*(16+32+1)];
			resp = new unsigned char[4 + g->Count*(16+32+1) + 2];
			alloced_mem = true;
		}
		else
		{
			members = (char*) m_ScratchBuffer;
			resp = m_ScratchBuffer + 4096;
		}

		int index = 0;
		AddData(resp, 3L, index);
		ptr = members;
		*ptr = 0;
		for (int i=0;i < g->Count;i++)
		{
			sprintf(ptr, i ? "\n%s %s" : "%s %s", g->Members[i].Name, g_PlayerMgr->GetRankName(g, g->Members[i].Rank));
			ptr += strlen(ptr);
		}
		AddDataLS(resp, members, index);
		AddDataLS(resp, "", index);

		SendOpcode(ENB_OPCODE_00D0_GUILD_MESSAGE_SECTOR, resp, index);

		if (alloced_mem)
		{
			delete [] resp;
			delete [] members;
		}
	}
}

void Player::HandleCurrentPermissions()
{
	Guild *g = g_PlayerMgr->GuildFromId(m_GuildID);
	if (g)
	{
		GuildMember *member = g_PlayerMgr->GetGuildMember(g, Name());
		if (member)
		{
			GuildRank *r = g_PlayerMgr->GetRank(g, member->Rank);
			if (r) // should never be 0
			{
				SendGuildPlayerPermissions(r->PermissionFlags, r->MaxPromote, r->MaxRemove, r->MinDemote);
			}
		}
	}
}

void Player::HandleGuildMOTD(char *motd)
{
	if (*motd != ' ' || *(++motd) == 0)
	{
		SendGuildMessage(GUILD_MSG_INTERNAL_ERROR);
	}
	else
	{
		Guild *g = g_PlayerMgr->GuildFromId(m_GuildID);
		if (g)
		{
			if (g_PlayerMgr->CheckPermission(g, Name(), PERMISSION_MESSAGE))
			{
				strncpy(g->MOTD, motd, sizeof(g->MOTD));
				SendGuildMessage(GUILD_MSG_CHANGED_GUILD_MESSAGE, "", g->Name);
				g_PlayerMgr->SendMessageToGuildMembers(g->id, this, GUILD_MSG_MOTD, motd);
				g_PlayerMgr->SaveGuildInfo(g);
			}
			else
			{
				SendGuildMessage(GUILD_MSG_NOT_HIGH_ENOUGH_TO_MESSAGE);
			}
		}
	}
}

void Player::HandlePromoteMember(char *name, bool confirmed)
{
	if (confirmed)
	{
		Guild *g = g_PlayerMgr->GuildFromId(m_GuildID);
		if (g)
		{
			GuildMember *member = g_PlayerMgr->GetGuildMember(g, name);
			if (member)
			{
				GuildRank *memberrank = g_PlayerMgr->GetRank(g, member->Rank+1);
				if (memberrank)
				{
					Player *promoted = g_PlayerMgr->GetPlayerFromCharacterID(member->avatar_id);
					if (promoted)
					{
						promoted->SendGuildMessage(GUILD_MSG_NEW_RANK, memberrank->Name);
						promoted->SetupGuildInfo(g->id, member->Rank);
					}
					SendGuildMessage(GUILD_MSG_YOU_HAVE_PROMOTED, name, memberrank->Name);
					member->Rank++;
					g_PlayerMgr->SaveGuildMember(m_GuildID, member);
					HandleListAllGuildMembers();
				}
			}
		}
	}
	else
	{
		if (*name != ' ' || *(++name) == 0)
		{
			SendGuildMessage(GUILD_MSG_USAGE_PROMOTE);
		}
		else
		{
			Guild *g = g_PlayerMgr->GuildFromId(m_GuildID);
			if (g)
			{
				GuildMember *me = g_PlayerMgr->GetGuildMember(g, Name());
				GuildMember *member = g_PlayerMgr->GetGuildMember(g, name);
				if (me && member)
				{
					GuildRank *myrank = g_PlayerMgr->GetRank(g, me->Rank);
					if (me == member)
					{
						SendGuildMessage(GUILD_MSG_CANT_PROMOTE_SELF);
					}
					else if (member->Rank >= myrank->MaxPromote-1 || !(myrank->PermissionFlags & PERMISSION_PROMOTE))
					{
						SendGuildMessage(GUILD_MSG_NOT_HIGH_ENOUGH_TO_PROMOTE, name);
					}
					else
					{
						SendGuildSimpleSectorClient(GUILD_PROMOTE_CONFIRM, name);
					}
				}
				else
				{
					SendGuildMessage(GUILD_MSG_PLAYER_NOT_FOUND, name);
				}
			}
		}
	}
}

void Player::HandleDemoteMember(char *name, bool confirmed)
{
	if (confirmed)
	{
		Guild *g = g_PlayerMgr->GuildFromId(m_GuildID);
		if (g)
		{
			GuildMember *member = g_PlayerMgr->GetGuildMember(g, name);
			if (member)
			{
				GuildRank *memberrank = g_PlayerMgr->GetRank(g, member->Rank-1);
				if (memberrank)
				{
					Player *demoted = g_PlayerMgr->GetPlayerFromCharacterID(member->avatar_id);
					if (demoted)
					{
						demoted->SendGuildMessage(GUILD_MSG_NEW_RANK, memberrank->Name);
						demoted->SetupGuildInfo(g->id, member->Rank);
					}
					SendGuildMessage(GUILD_MSG_YOU_HAVE_DEMOTED, name, memberrank->Name);
					member->Rank--;
					g_PlayerMgr->SaveGuildMember(m_GuildID, member);
					HandleListAllGuildMembers();
				}
			}
		}
	}
	else
	{
		if (*name != ' ' || *(++name) == 0)
		{
			SendGuildMessage(GUILD_MSG_USAGE_DEMOTE);
		}
		else
		{
			Guild *g = g_PlayerMgr->GuildFromId(m_GuildID);
			if (g)
			{
				GuildMember *me = g_PlayerMgr->GetGuildMember(g, Name());
				GuildMember *member = g_PlayerMgr->GetGuildMember(g, name);
				if (me && member)
				{
					GuildRank *myrank = g_PlayerMgr->GetRank(g, me->Rank);
					if (me == member)
					{
						SendGuildMessage(GUILD_MSG_CANT_DEMOTE_SELF);
					}
					else if (member->Rank <= myrank->MinDemote-1 || !(myrank->PermissionFlags & PERMISSION_DEMOTE))
					{
						SendGuildMessage(GUILD_MSG_NOT_HIGH_ENOUGH_TO_DEMOTE, name);
					}
					else
					{
						SendGuildSimpleSectorClient(GUILD_DEMOTE_CONFIRM, name);
					}
				}
				else
				{
					SendGuildMessage(GUILD_MSG_PLAYER_NOT_FOUND, name);
				}
			}
		}
	}
}

void Player::HandleRemoveMember(char *name, bool confirmed)
{
	if (confirmed)
	{
		Guild *g = g_PlayerMgr->GuildFromId(m_GuildID);
		if (g)
		{
			GuildMember *member = g_PlayerMgr->GetGuildMember(g, name);
			if (member)
			{
				Player *removed = g_PlayerMgr->GetPlayerFromCharacterID(member->avatar_id);
				if (removed)
				{
					removed->SendGuildMessage(GUILD_MSG_YOU_HAVE_BEEN_REMOVED, Name()); // ids etc reset in here
				}
				SendGuildMessage(GUILD_MSG_YOU_HAVE_REMOVED, name);
				g_PlayerMgr->RemoveMemberFromGuild(m_GuildID, name);
				HandleListAllGuildMembers();
			}
		}
	}
	else
	{
		if (*name != ' ' || *(++name) == 0)
		{
			SendGuildMessage(GUILD_MSG_USAGE_REMOVE);
		}
		else
		{
			Guild *g = g_PlayerMgr->GuildFromId(m_GuildID);
			if (g)
			{
				GuildMember *me = g_PlayerMgr->GetGuildMember(g, Name());
				GuildMember *member = g_PlayerMgr->GetGuildMember(g, name);
				if (me && member)
				{
					GuildRank *myrank = g_PlayerMgr->GetRank(g, me->Rank);
					if (member->Rank >= myrank->MaxPromote-1 || !(myrank->PermissionFlags & PERMISSION_REMOVE))
					{
						SendGuildMessage(GUILD_MSG_NOT_HIGH_ENOUGH_TO_REMOVE, name);
					}
					else
					{
						SendGuildSimpleSectorClient(GUILD_REMOVE_CONFIRM, name);
					}
				}
				else
				{
					SendGuildMessage(GUILD_MSG_PLAYER_NOT_FOUND, name);
				}
			}
		}
	}
}

void Player::HandleLeaveGuild(bool confirmed)
{
	if (confirmed)
	{
		Guild *g = g_PlayerMgr->GuildFromId(m_GuildID);
		if (g)
		{
			SendGuildMessage(GUILD_MSG_YOU_HAVE_LEFT_GUILD, "", g->Name); // ids etc reset in here
			g_PlayerMgr->RemoveMemberFromGuild(g->id, Name());
		}
	}
	else
	{
		SendGuildSimpleSectorClient(GUILD_LEAVE_CONFIRM);
	}
}

void Player::HandleDisbandGuild(bool confirmed)
{
	if (confirmed)
	{
		Guild *g = g_PlayerMgr->GuildFromId(m_GuildID);
		if (g)
		{
			if (g_PlayerMgr->CheckPermission(g, Name(), PERMISSION_DISBAND))
			{
				SendGuildMessage(GUILD_MSG_YOU_HAVE_DISBANDED, "" , g->Name); // ids etc reset in here
				g_PlayerMgr->RemoveGuildFromList(g->id, this, true);
			}
			else
			{
				SendGuildSimpleSectorClient(GUILD_MSG_NOT_HIGH_ENOUGH_TO_DISBAND);
			}
		}
	}
	else
	{
		SendGuildSimpleSectorClient(GUILD_DISBAND_CONFIRM);
	}
}

void Player::HandleRecruitMember(char *name)
{
	Guild *g = g_PlayerMgr->GuildFromId(m_GuildID);
	if (g)
	{
		if (*name != ' ' || *(++name) == 0)
		{
			SendGuildMessage(GUILD_MSG_USAGE_RECRUIT);
		}
		else
		{
			if (g_PlayerMgr->CheckPermission(g, Name(), PERMISSION_RECRUIT))
			{
				Player *recruit = g_PlayerMgr->GetPlayer(name);
				if (recruit)
				{
					if (recruit->GuildID() != -1)
					{
						SendGuildMessage(GUILD_MSG_ALREADY_IN_A_GUILD);
					}
					else
					{
						recruit->SendGuildRecruitConfirmSector(this, g->Name);
					}
				}
				else
				{
					SendGuildMessage(GUILD_MSG_PLAYER_NOT_FOUND, name);
				}
			}
			else
			{
				SendGuildMessage(GUILD_MSG_NOT_HIGH_ENOUGH_TO_RECRUIT);
			}
		}
	}
}

void Player::HandleRecruitMember2(Player *recruit, char accept)
{
	Guild *g = g_PlayerMgr->GuildFromId(m_GuildID);
	if (g)
	{
		if (accept)
		{
			g_PlayerMgr->SendMessageToGuildMembers(g->id, this, GUILD_MSG_HAS_BEEN_RECRUITED, recruit->Name(), g->Name);
			g_PlayerMgr->AddMemberToGuild(g->id, recruit->Name(), recruit->CharacterID());
			recruit->SendGuildMessage(GUILD_MSG_YOU_HAVE_JOINED_GUILD, "", g->Name);
			recruit->SetupGuildInfo(g->id, 0);
			recruit->SendAuxShip();
			recruit->SaveGuildId(0);
			HandleListAllGuildMembers();
		}
		else
		{
			SendGuildMessage(GUILD_MSG_DECLINED_INVITATION, recruit->Name(), g->Name);
		}
	}
}

void Player::HandlePublicStats(char *data)
{
	Guild *g = g_PlayerMgr->GuildFromId(m_GuildID);
	if (g)
	{
		bool on = !strcmp(data," on");
		if (g_PlayerMgr->CheckPermission(g, Name(), on ? PERMISSION_PUBLIC_STATUS : PERMISSION_PRIVATE_STATUS))
		{
			g->PublicStats = on;
			SendGuildMessage(g->PublicStats ? GUILD_MSG_STATS_CAN_BE_SEEN : GUILD_MSG_STATS_CANT_BE_SEEN, "", g->Name);
			g_PlayerMgr->SaveGuildInfo(g);
		}
		else
		{
			SendGuildMessage(GUILD_MSG_NOT_HIGH_ENOUGH_TO_PUBLIC);
		}
	}
}

void Player::HandleShowGuildStats(char *data)
{
	SendGuildMessage(GUILD_MSG_NOT_IMPLEMENTED);
}

void Player::HandleTagMember(char *data)
{
	SendGuildMessage(GUILD_MSG_NOT_IMPLEMENTED);
}

void Player::HandleMemberContributions(char *data)
{
	SendGuildMessage(GUILD_MSG_NOT_IMPLEMENTED);
}

////////// GM commands

void Player::HandleGuildGMCommand(char *param)
{
	int n = param ? strlen(param) : 0;

	if (n == 0)
	{
		SendGuildMessage(GUILD_MSG_AVAILABLE_COMMANDS);
	}
	else if (n >= 7 && strncmp(param,"disband",7) == 0)
	{
		HandleGMDisbandGuild(param+7, false);
	}
	else
	{
		SendGuildMessage(GUILD_MSG_INVALID_COMMAND);
		LogMessage("Unknown /gmgc command '%s'\n", param);
	}
}

void Player::HandleGMDisbandGuild(char *name, bool confirmed)
{
	if (confirmed)
	{
		Guild *g = g_PlayerMgr->GuildFromName(name);
		if (g)
		{
			SendGuildMessage(GUILD_GMMSG_GUILD_DISBANDED, "" , name);
			g_PlayerMgr->RemoveGuildFromList(g->id, this, true);
		}
	}
	else
	{
		if (*name != ' ' || *(++name) == 0)
		{
			SendGuildMessage(GUILD_GMMSG_USAGE_DISBAND);
		}
		else
		{
			if (*name == '\"')
			{
				name[strlen(name)-1] = 0;
				name++;
			}
			if (!g_PlayerMgr->GuildFromName(name))
			{
				SendGuildMessage(GUILD_MSG_INVALID_GUILD, "", name);
			}
			else
			{
				SendGuildSimpleSectorClient(GUILD_GM_DISBAND_CONFIRM, name);
			}
		}
	}
}

////////// opcode handling

void Player::HandleGuildRankNamesRequestClient(unsigned char *data)
{
	struct GuildRankNamesRequestPacket
	{
		long gameid;
		short unknown;
	} *request;
	request = (struct GuildRankNamesRequestPacket *)data;

	Guild *g = g_PlayerMgr->GuildFromId(m_GuildID);
	if (g)
	{
		unsigned char resp[10*68+4];
		int index = 0;

		AddData(resp, htonl(10), index); // guess
		for (long i=0;i < 10;i++)
		{
			char *rank = g_PlayerMgr->GetRankName(g, i);
			AddDataLS(resp, rank, index);
			AddData(resp, htonl(i+1), index); // guess
		}
		SendOpcode(ENB_OPCODE_00D3_GUILD_RANK_NAMES_SECTOR, (unsigned char *) &resp, index);
	}
}

void Player::HandleGuildSimpleClientSector(unsigned char *data)
{
	struct GuildSimpleClientSectorPacket
	{
		long type;
		long gameid;
		short length;
		char optionalparam[16];
	} *request;
	request = (struct GuildSimpleClientSectorPacket *)data;

	switch (request->type)
	{
	case GUILD_PROMOTE_CONFIRM+1:
		HandlePromoteMember(request->optionalparam, true);
		break;
	case GUILD_DEMOTE_CONFIRM+1:
		HandleDemoteMember(request->optionalparam, true);
		break;
	case GUILD_REMOVE_CONFIRM+1:
		HandleRemoveMember(request->optionalparam, true);
		break;
	case GUILD_LEAVE_CONFIRM+1:
		HandleLeaveGuild(true);
		break;
	case GUILD_DISBAND_CONFIRM+1:
		HandleDisbandGuild(true);
		break;
	case GUILD_GM_DISBAND_CONFIRM+1:
		HandleGMDisbandGuild(request->optionalparam, true);
		break;
	default:
		LogMessage("Unknown guild confirmation type %d\n", request->type);
	}
}

void Player::HandleGuildLeaderAcceptClient(unsigned char *data)
{
	struct GuildLeaderAcceptClientPacket
	{
		long gameid;
		short length1;
		char guildname[1]; // no null
		char accept;
	};
	long gameid = *(long *)data;
	short length = *(short *)&data[4];
	char guildname[64];
	strncpy(guildname, (char *)&data[6], 64);
	guildname[length] = 0;
	unsigned char accept = data[6+length];

	if (accept)
	{
		g_PlayerMgr->CheckGuildCreationAccepted(guildname, Name());
	}
	else
	{
		Guild *g = g_PlayerMgr->GuildFromName(guildname, true);
		if (g)
		{
			g_PlayerMgr->SendMessageToFounders(g, GUILD_MSG_GUILD_FORMATION_ABORTED_BY, Name(), guildname);
			g_PlayerMgr->RemoveGuildFromList(g->id);
		}
	}
}

void Player::HandleRecruitAcceptClient(unsigned char *data)
{
	struct RecruitAcceptClientPacket
	{
		long gameid;
		char accept;
	} *request;
	request = (RecruitAcceptClientPacket *)data;

	m_Recruiter->HandleRecruitMember2(this, request->accept);
}

void Player::SendGuildPlayerPermissions(long p1, long p2, long p3, long p4)
{
	unsigned char resp[32];
	int index = 0;

	AddData(resp, htonl(p1), index);
	AddData(resp, htonl(p2), index);
	AddData(resp, htonl(p3), index);
	AddData(resp, htonl(p4), index);

	SendOpcode(ENB_OPCODE_00D2_GUILD_PLAYER_PERMISSIONS, (unsigned char *) &resp, index);
}

void Player::SendGuildMessage(long Type, char *OtherName, char *GuildName)
{
	unsigned char *resp = m_ScratchBuffer;  //use scratchbuffer - for some reason the ref of resp was used below which was causing incorrect results.
	int index = 0;

	AddData(resp, Type, index);
	AddDataLS(resp, OtherName, index);
	AddDataLS(resp, GuildName, index);

	SendOpcode(ENB_OPCODE_00D0_GUILD_MESSAGE_SECTOR, resp, index);

	// extra processing for certain messages (via playermanager)
	if (Type == GUILD_MSG_YOU_HAVE_LEFT_GUILD || Type == GUILD_MSG_YOU_HAVE_BEEN_REMOVED || 
		Type == GUILD_MSG_HAS_BEEN_DISBANDED || Type == GUILD_MSG_YOU_HAVE_DISBANDED)
	{
		m_GuildID = -1;
		SaveGuildId(0);
		ShipIndex()->SetGuildName("");
		ShipIndex()->SetGuildRank(0);	// this rank is 1 based
		ShipIndex()->SetGuildRankName("");
		SendAuxShip();
	}
}

void Player::SendGuildSimpleSectorClient(long Type, char *OptionalParam)
{
	unsigned char resp[96];
	int index = 0;

	AddData(resp, Type, index);
	AddDataLS(resp, OptionalParam, index);

	SendOpcode(ENB_OPCODE_00CC_GUILD_SIMPLE_SECTOR_CLIENT, (unsigned char *) &resp, index);
}

void Player::SendGuildRecruitConfirmSector(Player *recruiter, char *guild)
{
	unsigned char resp[96];
	int index = 0;

	AddDataLS(resp, recruiter->Name(), index);
	AddDataLS(resp, guild, index);

	SendOpcode(ENB_OPCODE_00C8_GUILD_RECRUIT_CONFIRM_SECTOR, (unsigned char *) &resp, index);
	// the accept packet goes to the player class of the acceptee, which isnt much use to then add the person to the guild!
	m_Recruiter = recruiter;
}