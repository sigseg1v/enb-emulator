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

#include "Net7.h"
#include "PlayerManager.h"
#include "PlayerClass.h"
#include "SaveManager.h"
#include "PacketMethods.h"
#include "mysql/mysqlplus.h"

void PlayerManager::LoadGuildsFromSQL()
{
	char QueryString[128];
	int id,rank;

	sql_connection_c connection("net7_user", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
	sql_query_c GuildQuery(&connection);
    sql_result_c result1,result2,result3;
	sql_row_c GuildData,RankData,MemberData;

    strncpy(QueryString, "SELECT * FROM guilds", sizeof(QueryString));
	GuildQuery.run_query(QueryString);
	GuildQuery.store(&result1);
	for (int i=0;i < result1.n_rows();i++)
	{
		result1.fetch_row(&GuildData);
		id = GuildData["guild_id"];
		AddGuildToList(id, GuildData["name"], GuildData["motd"], GuildData["points"], GuildData["level"], (char)GuildData["public"] != 0);
		// ranks
		GuildQuery.AddParam((long)(id*10));
		GuildQuery.AddParam((long)(id*10+9));
		GuildQuery.run_query_params("SELECT * FROM guild_ranks WHERE id>=? AND id<=?");
		GuildQuery.store(&result2);
		for (int j=0;j < result2.n_rows();j++)
		{
			result2.fetch_row(&RankData);
			rank = (long)RankData["id"] - (long)id*10;
			AddRankToGuild(id, rank, RankData["name"], RankData["permissions"], RankData["maxpromote"], RankData["maxremove"], RankData["mindemote"]);
		}
		// members
		GuildQuery.AddParam((long)id);
		GuildQuery.run_query_params("SELECT * FROM guild_members JOIN avatar_data ON guild_members.avatar_id=avatar_data.avatar_id WHERE guild_id=?");
		GuildQuery.store(&result3);
		for (int k=0;k < result3.n_rows();k++)
		{
			result3.fetch_row(&MemberData);
			AddMemberToGuild(id, MemberData["first_name"], MemberData["avatar_id"], MemberData["rank"], (char)MemberData["active"]!=0, MemberData["contribution"], MemberData["tag"]);
		}
	}
	// add in dev guild
	if (m_NextGuild == 0)
	{
		AddGuildToList(0, "Earth And Beyond Staff", "Welcome to the Staff Room");
		AddRanksToGuild(0, "Admin,Server Dev,Content Dev,Head GM,Deputy GM,GM");
		SaveNewGuild(GuildFromId(0));
	}
}

void PlayerManager::FreeGuilds()
{
	for (unsigned int i=0;i < m_GuildList.size();i++)
	{
		delete m_GuildList[i];
	}
	m_GuildList.clear();
}

// id -1 for a new guild
int PlayerManager::AddGuildToList(int id, char *name, char *motd, long points, short level, bool publicstats)
{
	Guild *g = new Guild;
	g->pending = id == -1;
	if (id == -1)
	{
		id = m_NextPendingGuild++;
	}
	else if (id >= m_NextGuild)
	{
		m_NextGuild = id+1;
		m_NextPendingGuild = m_NextGuild;
	}
	g->id = id;
	g->PublicStats = true;
	g->Count = 0;
	g->Level = 0;
	g->Points = 0;
	strncpy(g->Name, name, sizeof(g->Name));
	strncpy(g->MOTD, motd, sizeof(g->MOTD));
	for (int i=0;i < MAX_GUILD_RANKS;i++)
	{
		char *Defaults[MAX_GUILD_RANKS] = { "Recruit", "Apprentice", "Petty Officer", "Master Chief", "Ensign", "Lieutenant", "Commander", "Captain", "Commodore", "Admiral" };
		long  DefPerms[MAX_GUILD_RANKS] = { PERM_NEWBIE, PERM_NEWBIE, PERM_NEWBIE, PERM_NEWBIE, PERM_OFFICER2, PERM_OFFICER2, PERM_OFFICER2, PERM_OFFICER1, PERM_OFFICER1, PERM_LEADER };
		strncpy(g->Ranks[i].Name, Defaults[i], sizeof(g->Ranks[i].Name));
		g->Ranks[i].PermissionFlags = DefPerms[i];
		g->Ranks[i].MaxPromote = i<4 ? 0 : i+1;
		g->Ranks[i].MaxRemove  = i<4 ? 0 : i;
		g->Ranks[i].MinDemote  = 1;
	}
	m_GuildList.push_back(g);
	return id;
}

void PlayerManager::RemoveGuildFromList(int id, Player *origin, bool disbanded)
{
	for (unsigned int i=0;i < m_GuildList.size();i++)
	{
		if (m_GuildList[i] && m_GuildList[i]->id == id)
		{
			delete m_GuildList[i];
			m_GuildList[i] = NULL;
			if (disbanded)
			{
				SendMessageToGuildMembers(id, origin, GUILD_MSG_HAS_BEEN_DISBANDED, "", "", false);
				g_SaveMgr->AddSaveMessage(SAVE_CODE_DELETE_GUILD, id, 0, 0);
			}
		}
	}
}

void PlayerManager::AddRankToGuild(int id, int rank, char *name, long flags, long perm2, long perm3, long perm4)
{
	Guild *g = GuildFromId(id);
	if (g)
	{
		strncpy(g->Ranks[rank].Name, name, sizeof(g->Ranks[rank].Name));
		g->Ranks[rank].PermissionFlags = flags;
		g->Ranks[rank].MaxPromote = perm2;
		g->Ranks[rank].MaxRemove = perm3;
		g->Ranks[rank].MinDemote = perm4;
	}
}

void PlayerManager::AddRanksToGuild(int id, char *ranks)
{
	Guild *g = GuildFromId(id);
	char *to;
	int index;

	if (g && *ranks)
	{
		index = MAX_GUILD_RANKS-1;
		to = g->Ranks[index].Name;
		while (*ranks && index >= 0)
		{
			if (*ranks == ',')
			{
				*to = 0;
				to = g->Ranks[--index].Name;
				ranks++;
			}
			else
			{
				*to++ = *ranks++;
			}
		}
		*to = 0;
	}
}

int PlayerManager::AddMemberToGuild(int id, char *player_name, long avatar_id, int rank, bool active, int contribution, char *tag)
{
	Guild *g = GuildFromId(id);

	if (g && g->Count < MAX_GUILD_MEMBERS && GetMemberIndex(g, player_name) == -1)
	{
		strncpy(g->Members[g->Count].Name, player_name, sizeof(g->Members[g->Count].Name));
		strncpy(g->Members[g->Count].Tag, tag, sizeof(g->Members[g->Count].Tag));
		g->Members[g->Count].Rank = rank;
		g->Members[g->Count].Contribution = contribution;
		g->Members[g->Count].Active = active;
		g->Members[g->Count].avatar_id = avatar_id;
		return g->Count++;
	}
	return -1;
}

bool PlayerManager::RemoveMemberFromGuild(int id, char *player_name)
{
	Guild *g = GuildFromId(id);

	if (g)
	{
		int index = GetMemberIndex(g, player_name);
		if (index != -1)
		{
			for (int i=index;i < g->Count;i++)
			{
				g->Members[i] = g->Members[i+1];
			}
			// check for empty guild
			if (--g->Count == 0)
			{
				RemoveGuildFromList(g->id, NULL, true);
			}
			return true;
		}
	}
	return false;
}

Guild *PlayerManager::GuildFromId(int id)
{
	for (unsigned int i=0;i < m_GuildList.size();i++)
	{
		if (m_GuildList[i] && m_GuildList[i]->id == id)
		{
			return m_GuildList[i];
		}
	}
	return NULL;
}

Guild *PlayerManager::GuildFromName(char *name)
{
	for (unsigned int i=0;i < m_GuildList.size();i++)
	{
		if (m_GuildList[i] && strcmp(m_GuildList[i]->Name,name) == 0)
		{
			return m_GuildList[i];
		}
	}
	return NULL;
}

Guild *PlayerManager::GuildFromName(char *name, bool pending)
{
	for (unsigned int i=0;i < m_GuildList.size();i++)
	{
		if (m_GuildList[i] && m_GuildList[i]->pending == pending && strcmp(m_GuildList[i]->Name,name) == 0)
		{
			return m_GuildList[i];
		}
	}
	return NULL;
}

int PlayerManager::GetMemberIndex(Guild *g, char *name)
{
	for (int i=0;i < g->Count;i++)
	{
		if (strcmp(g->Members[i].Name, name) == 0)
		{
			return i;
		}
	}
	return -1;
}

GuildMember *PlayerManager::GetGuildMember(Guild *g, char *name)
{
	for (int i=0;i < g->Count;i++)
	{
		if (strcmp(g->Members[i].Name, name) == 0)
		{
			return &g->Members[i];
		}
	}
	return NULL;
}

char *PlayerManager::GetRankName(Guild *g, int rank)
{
	if (rank >= 0 && rank < MAX_GUILD_RANKS)
	{
		return g->Ranks[rank].Name;
	}
	return "error";
}

GuildRank *PlayerManager::GetRank(Guild *g, int rank)
{
	if (rank >= 0 && rank < MAX_GUILD_RANKS)
	{
		return &g->Ranks[rank];
	}
	return NULL;
}

bool PlayerManager::CheckPermission(Guild *g, char *name, long permission_bit)
{
	int index = GetMemberIndex(g, name);
	if (index != -1)
	{
		int rank = g->Members[index].Rank;
		return (g->Ranks[rank].PermissionFlags & permission_bit) != 0;
	}
	return false;
}

void PlayerManager::CreateGuild(Player *founders[], char *name)
{
	char newmotd[80];
	int id;

	sprintf_s(newmotd, sizeof(newmotd), "Welcome to %s", name);
	id = AddGuildToList(-1, name, newmotd);
	for (int i=0;i < 6;i++)
	{
		if (founders[i])
		{
			AddMemberToGuild(id, founders[i]->Name(), founders[i]->CharacterID(), i==0 ? 9 : 5, false);
		}
	}
}

bool PlayerManager::CheckGuildCreationAccepted(char *guild_name, char *player_name)
{
	Guild *g = GuildFromName(guild_name, true);
	if (!g) return false;

	GuildMember *member = GetGuildMember(g, player_name);
	if (!member) return false;
	member->Active = true;

	for (int i=0;i < g->Count;i++)
	{
		if (!g->Members[i].Active)
			return false;
	}
	// everyone accepted
	Player *founders[6];
	bool abort = false;
	for (int i=0;i < g->Count;i++)
	{
		founders[i] = GetPlayerFromCharacterID(g->Members[i].avatar_id);
		if (!founders[i])
		{
			SendMessageToFounders(g, GUILD_MSG_INTERNAL_ERROR);
			abort = true;
			break;
		}
		if (i && !founders[i]->IsInSameSector(founders[0]))
		{
			SendMessageToFounders(g, GUILD_MSG_ABORT_PLAYER_LEFT_SECTOR, founders[i]->Name());
			abort = true;
			break;
		}
	}
	if (!abort && founders[0]->PlayerIndex()->GetCredits() < 10000)
	{
		SendMessageToFounders(g, GUILD_MSG_NOT_ENOUGH_CREDITS_ABORTED);
		abort = true;
	}
	// check final conditions
	if (abort)
	{
		RemoveGuildFromList(g->id);
	}
	else
	{
		// hooray!, give it a proper id
		g->id = m_NextGuild++;
		g->pending = false;
		SaveNewGuild(g);
		SendMessageToFounders(g, GUILD_MSG_YOU_HAVE_JOINED_GUILD, "", g->Name);
		// setup players guild info
		for (int i=0;i < g->Count;i++)
		{
			founders[i]->SetupGuildInfo(g->id, g->Members[i].Rank);
			founders[i]->SendAuxShip();
		}
		// pay fee
		founders[0]->PlayerIndex()->SetCredits(founders[0]->PlayerIndex()->GetCredits()-10000);
		founders[0]->SaveCreditLevel();
		founders[0]->SendGuildMessage(GUILD_MSG_REGISTRATION_FEE_DEDUCTED, "10000");
	}
	return true;
}

bool PlayerManager::CheckIfFounderOfPendingGuild(char *name)
{
	for (unsigned int i=0;i < m_GuildList.size();i++)
	{
		if (m_GuildList[i] && m_GuildList[i]->pending && GetGuildMember(m_GuildList[i], name))
		{
			return true;
		}
	}
	return false;
}

void PlayerManager::GuildChat(long GameID, char *message, bool copy_to_originator)
{
    if (message)
    {
        // Find sender
        Player *s = GetPlayer(GameID);
		Player *p = (0);

        if (s)
        {
			char Channel[] = "Guild";
            
			while (GetNextPlayerOnList(p, m_GlobalPlayerList))   
			{
				//only send to originating player if directed to.
				if (((p->GameID() != GameID) || copy_to_originator)
					&& p->GuildID() == s->GuildID()
					&& p->IsSubscribed(GetChannelFromName(Channel)))		
				{
					p->SendClientChatEvent(CHEV_CHANNEL_MESSAGE, s, Channel, message);
				}
			}
        }
    }
}

void PlayerManager::SendMessageToFounders(Guild *g, int Type, char *OtherName, char *GuildName)
{
	for (int i=0;i < g->Count;i++)
	{
		Player *p = GetPlayerFromCharacterID(g->Members[i].avatar_id);
		if (p)
		{
			p->SendGuildMessage(Type, OtherName, GuildName);
		}
	}
}

void PlayerManager::SendMessageToGuildMembers(int id, Player *origin, int Type, char *OtherName, char *GuildName, bool copy_to_originator)
{
	Player *p = (0);

	while (GetNextPlayerOnList(p, m_GlobalPlayerList))   
	{
		//only send to originating player if directed to.
		if (((p != origin) || copy_to_originator) && p->GuildID() == id)
		{
			p->SendGuildMessage(Type, OtherName, GuildName);
		}
	}
}

void PlayerManager::SaveNewGuild(Guild *guild)
{
	SaveGuildInfo(guild);
	for (int i=0;i < MAX_GUILD_RANKS;i++)
	{
		SaveGuildRank(guild->id, i, &guild->Ranks[i]);
	}
	for (int i=0;i < guild->Count;i++)
	{
		SaveGuildMember(guild->id, &guild->Members[i]);
	}
}

void PlayerManager::SaveGuildMember(int guild_id, GuildMember *member, bool remove)
{
	unsigned char data[sizeof(GuildMember)];
	int index = 0;

	if (remove)
	{
		AddData(data, (short)0, index);
		AddData(data, (int)-1, index);

		g_SaveMgr->AddSaveMessage(SAVE_CODE_GUILD_ID, member->avatar_id, index, data);
	}
	else
	{
		AddBuffer(data, (unsigned char *)member, sizeof(GuildMember), index);

		g_SaveMgr->AddSaveMessage(SAVE_CODE_GUILD_MEMBER, guild_id, index, data);
	}
}

void PlayerManager::SaveGuildRank(int guild_id, short rank_num, GuildRank *rank)
{
	unsigned char data[sizeof(GuildRank)+2];
	int index = 0;

	AddData(data, rank_num, index);
	AddBuffer(data, (unsigned char *)rank, sizeof(GuildRank), index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_GUILD_RANK, guild_id, index, data);
}

void PlayerManager::SaveGuildInfo(Guild *guild)
{
	unsigned char data[offsetof(Guild,Ranks)];
	int index = 0;

	AddBuffer(data, (unsigned char *)guild, offsetof(Guild,Ranks), index);

	g_SaveMgr->AddSaveMessage(SAVE_CODE_GUILD_INFO, guild->id, index, data);
}
