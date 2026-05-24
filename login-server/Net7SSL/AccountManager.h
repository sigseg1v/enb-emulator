// AccountManager.h

#ifndef _ACCOUNT_MANAGER_SSL_H_INCLUDED_
#define _ACCOUNT_MANAGER_SSL_H_INCLUDED_

#include "Net7SSL.h"
#include <net7/Mutex.h>
#include <net7/PacketStructures.h>

#define MAX_ACCOUNTS	1024
#define TICKET_EXPIRE_TIME  300000  //5 minutes (in milliseconds)

#define G_ERROR_BANNED_ACCOUNT		0
#define G_ERROR_NICKNAME_USED		1
#define	G_ERROR_INVALID_CHARS		2
#define	G_ERROR_TOO_SHORT			3
#define	G_ERROR_ONE_VOWEL			4
#define	G_ERROR_REPEATING_CHAR		5
#define	G_ERROR_RESTRICTED_LIST		6
#define	G_ERROR_TICKET_INVALID		7
#define	G_ERROR_AUTH_SERVER_DOWN	8
#define G_ERROR_INACTIVE_ACCOUNT	9
#define	G_ERROR_RESTRICTED_SHIP		10
#define	G_ERROR_NET7_INTERNAL		11
#define G_ERROR_STRESS_TEST_CLOSED	12
#define G_ERROR_ACCOUNT_IN_USE		13
#define G_ERROR_SERVER_SHUTDOWN		14

//Returns the avatar id from a given account and a slot (0-4)
#define AVATAR_ID(account_id, slot) (account_id * 5 + slot + 1)

//Returns the account id from a given avatar id
#define ACCOUNT_ID(avatar_id) (avatar_id - 1) / 5

class AccountManager
{
public:
	AccountManager();
	~AccountManager();

public:
	char  * IssueTicket(char *username, char *password);
    void	GetUsernameFromTicket(char *ticket, char *name, long length);

    bool	GetEmailAddress(char *username, char *buffer, int buflen);
	long	GetAccountID(char *username);
	long	GetAvatarID(char *username, int slot);

	bool	SetAccountStatus(char *username, long status);
    long	GetAccountStatus(char *username);
	bool	ChangePassword(char *username, char *password);
	
	bool	AddUser(char *username, char *password, char *access);

    long    CreateCharacter(GlobalCreateCharacter * create);
    void    DeleteCharacter(long avatar_id);

    bool    SaveDatabase(CharacterDatabase * database, long avatar_id);
    bool    ReadDatabase(CharacterDatabase * database, long avatar_id);

    void    BuildAvatarList(GlobalAvatarList * list, long account_id);
	long	GetPlayerSector(long avatar_id);

private:
    struct AccountTicket
    {
        AccountTicket * next;
        char username[64];
        char ticket[64];
        unsigned long expire_time;
    } ATTRIB_PACKED;

	void	LoadAccounts();
    void    SetupTickets();

    char  * BuildTicket(char *username);
	long    ValidateAccount(char *username, char *password);

    bool    IsUsernameUnique(char *name);
	bool	IsForbidden(char *name);

	bool	UpdateTicket(int Index, char * Ticket);
	void	UpdateLoginTime(long account_id);

private:
    AccountTicket * m_Tickets;
    Mutex m_Mutex;
};

#endif // _ACCOUNT_MANAGER_SSL_H_INCLUDED_
