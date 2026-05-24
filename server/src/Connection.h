// Connection.h

#ifndef _TCP_CONNECTION_H_INCLUDED_
#define _TCP_CONNECTION_H_INCLUDED_

#include "Mutex.h"
#include "WestwoodRSA.h"
#include "WestwoodRC4.h"
#include "PacketStructures.h"
#include "PlayerManager.h"
#include "MessageQueue.h"
#include "ItemBase.h"
#include "SectorManager.h"

#define RC4_KEY_SIZE		8
#define RC4_UDP_KEY_SIZE	16
#define TCP_BUFFER_SIZE		(128 * 1024)  // not used anymore for anything. Galaxy map is stored locally and we update it via the patcher
#define SEND_BUFFER_SIZE	10240
// Default timeout (3 seconds)
#define CONNECTION_TIMEOUT		3
#define MAX_RETRIES		5

#define MAX_TCP_BUFFER	    8192

class ServerManager;
class SectorManager;
class PlayerManager;
class Groups;
struct PositionInformation;
struct TimeNode;
class Object;

class Connection
{
//////////////////////////////
//  Constructor/Destructor  //
//////////////////////////////
public:
//    Connection(SOCKET s, ServerManager &server_mgr, short port, int server_type, unsigned long* ip_addr = 0); -- OBSOLETE

	Connection();
	virtual ~Connection();

//////////////////////
//  Public Methods  //
//////////////////////
public:
	Connection * ReSetConnection(SOCKET s, ServerManager &server_mgr, short port, int server_type, unsigned long* ip_addr = 0);
	void KillConnection(char *origin);

	void SetConnectionTerminate()	{ m_TerminateAfterSend = true; }

//	void    RunRecvThread(); -- Privatized
//	void    RunSendThread(); -- OBSOLETE
//	bool	RunKeyExchange(); -- Privatized

	bool	IsActive();

	bool	SetNonBlocking(bool blocking);

	void    SetRC4Key(unsigned char *rc4_key);

	void    SendSectorAssignment(long sector_id);
	void    SendResponse(short opcode, unsigned char *data=NULL, size_t length=0);
	// SendObjectEffect: EffectManager calls this on the connection.
	// Implementation delegates to the Player by GameID lookup.
	void    SendObjectEffect(class ObjectEffect *object_effect);
//	void	SendResponseTestFile(short opcode, char *filename=NULL); -- OBSOLETE
	void    Send(unsigned char *Buffer, int length);

	long	GameID()			{ return m_AvatarID; }
	void	SetGameID(long id)		{ m_Mutex.Lock(); m_AvatarID = id; m_Mutex.Unlock(); }

	void	PulseConnectionOutput(int max_cycles = 5);

	void	ProcessRecvInputs(u32 tick);

	char *	GetAccountName()	{ return m_AccountUsername; }

//	bool	CheckStatus(long size); -- OBSOLETE

	
///////////////////////
//  Private Methods  //
///////////////////////
private:
	void    RunRecvThread();	// Privatized
	bool	RunKeyExchange();	// Privatized

	bool    DoKeyExchange();
	bool    DoClientKeyExchange();

	bool	SocketReady(int ttimeout = WAIT_TIMEOUT, int ustimeout = 0);
	void	WakeupThread();

	void    ProcessGlobalServerOpcode(short opcode, short bytes);
	void    ProcessMasterServerOpcode(short opcode, short bytes);
	void    ProcessSectorServerOpcode(short opcode, short bytes);
	void    ProcessMasterServerToSectorServerOpcode(short opcode, short bytes);
	void    ProcessSectorServerToSectorServerOpcode(short opcode, short bytes);
	void    ProcessProxyClientOpcode(short opcode, short bytes);
	void	ProcessProxyGlobalOpcode(short opcode, short bytes);
	void    ProxyClientOpcode(short opcode, short bytes);

	void    HandleClientOpcode(short opcode, short bytes);
	void	HandleAccountValid(short bytes);
	void	ProcessTicketInfo(short bytes);
	void	ResetConnection();
	char   *GetLogInfo();

	static void* SocketRecvThread(void *param);

    ////////////////////////////////
    //  Server to Server Opcodes  //
    ////////////////////////////////

	void    HandleSectorServerAssignment();         // opcode 0x8701
	void    HandleRequestCharacterData();           // opcode 0x8702
	void    SendCharacterData();                    // opcode 0x8802
	void    HandleCharacterData(short length);      // opcode 0x8802

    ////////////////////////////////
    //  Client to Server Opcodes  //
    ////////////////////////////////

	void	GlobalError(int Error);			// Send Error
	void    HandleVersionRequest();                 // opcode 0x00
	void    HandleLogin();                          // opcode 0x02

	void    HandleMasterJoin();                     // opcode 0x35
	void    HandleGlobalConnect();                  // opcode 0x6D
	void    HandleGlobalTicketRequest();            // opcode 0x6E
	void    HandleDeleteCharacter();                // opcode 0x71
	void    HandleCreateCharacter();                // opcode 0x72


	void    ValidateLoginLink();                    // opcode 0x3002 (Net7Proxy)
	void    ShutdownLoginLink();                    // opcode 0x3003
	void    CommenceNavSend();                      // opcode 0x3004 (Net7Proxy)
	void    HandleLoginFailed();                    // opcode 0x3006
	void	HandleStarbaseLoginComplete();			// opcode 0x3008 (Net7Proxy)
	void	SetConnectionToLoginLink();
	void	SetConnectionToProxyLink();
	void	HandleMasterHandoff();					// opcode 

    ////////////////////////////////
    //  Server to Client Opcodes  //
    ////////////////////////////////
public:
	void    SendVersionResponse(long status);	// opcode 0x01
							// opcode 0x04
	void    SendServerRedirect(long sector_id);	// opcode 0x36
							// opcode 0x3a
	void    SendServerHandoff(long from_sector_id, long to_sector_id,
			char *from_sector, char *from_system, char *to_sector, char *to_system);	
	void    SendClientType(long client_type);	// opcode 0x3c
							// opcode 0x3e

	void    SendGlobalTicket(long avatar_id, long sector_id, long level, bool issue);
	void    ProcessGlobalTicket(Player *player);
	void    SendAvatarList(long account_id);	// opcode 0x70
	void	ShutdownTCPLink(long game_id);
	int	GetServerType()				{ return m_ServerType; }


/////////////////////////////////
//  Public Member Attributes  //
/////////////////////////////////
public:
	WestwoodRC4	m_CryptIn;	// RC4 decryption for inbound data
	WestwoodRC4	m_CryptOut;	// RC4 encryption for outbound data

	bool		m_PacketLoggingEnabled;

/////////////////////////////////
//  Private Member Attributes  //
/////////////////////////////////
private:
	// Attributes required for all servers
	WestwoodRSA	m_WestwoodRSA;			// RSA-155 encryption
	SOCKET		m_Socket;				// Our TCP/IP socket
	bool		m_ConnectionActive;		// true if the TCP/IP connection is active
	bool		m_TcpThreadRunning;		// true if TCP Thread is running

	bool		m_KeysExchanged;

	bool		m_TerminateAfterSend;	// use this for the global TCP->client connection after we send the ticket (finished with TCP then)

	char		m_RunCount;

	unsigned char	m_RecvBuffer[MAX_TCP_BUFFER];	// TCP/IP Receive buffer
	unsigned char	m_SendBuffer[MAX_TCP_BUFFER];   // TCP/IP Send buffer
	unsigned char	m_SendBuffer2[SEND_BUFFER_SIZE];
	char 			m_loginfo[256];
	MessageQueue	* m_SendQueue;

	int			m_ServerType;			// Server type (1=GS, 2=MS, 3=SS)

	u32			m_LastActivity;
	short		m_ReceivedSoFar;
	unsigned short m_DelayedOpcodeSize;
	short		m_DelayedOpcode;

	short		m_TcpPort;				// TCP/IP port number
	long		m_IPaddr;
	char *		m_AccountUsername;
	char *		m_LastOwner;
	long		m_AvatarID;   

	Mutex		m_Mutex;
};

#endif // _TCP_CONNECTION_H_INCLUDED_
