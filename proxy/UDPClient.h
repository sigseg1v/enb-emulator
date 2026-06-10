// UDPClient.h
// Defines the UDPClient class which connects to the UDP Connection server in the Net7 server

#ifndef _UDP_CLIENT_H_INCLUDED_
#define _UDP_CLIENT_H_INCLUDED_

#define	MAX_UDPC_BUFFER					65536 //35840		//16384
#define MAX_QUEUE_BUFFER                8192 //4096
#define SLOT_RANGE						32

#include <net7/PacketStructures.h>
#include <cstdint>
#include <vector>
#include <map>

struct EffectCancel
{
    long time_delay;
    long effect_id;
};

typedef std::map<unsigned long, char *> PacketList;

class Connection;
namespace net7 { class DtlsTransport; }

class UDPClient
{
public:
    // unconnected=true (Linux only): skip the connect() call on the
    // SOCK_DGRAM socket. Required for the global plane, which must accept
    // server->proxy replies on TWO source ports — UDP_GLOBAL_SERVER_PORT
    // (control opcodes: 0x2003/0x2005/0x200C/0x200E) AND MVAS_LOGIN_PORT
    // (in-game opcodes: 0x2010/0x2016/0x201A and default sector traffic
    // routed via server->player->m_UDPConnection==MVASauth). A connect()
    // would filter recv() by single peer port and silently drop the other.
    // See proxy/UDPClient_linux.cpp::OpenFixedPort / ::RecvThread.
    UDPClient(short port, short connection_type, long ip_addr, bool unconnected = false);
	virtual ~UDPClient();

    void    SetBroadcast(SOCKET socket);
    void    RecvThread();
    void    MVASThread();
    // Linux MVAS idle-keepalive loop (see impl comment in UDPClient_linux.cpp).
    // Public because a free-function pthread trampoline invokes it, mirroring
    // RecvThread/MVASThread.
    void    MVASKeepaliveThread();
    // Linux server-side proxy: the periodic 0x3005 PLAYER_COMMS_ALIVE the real
    // Net7Proxy streams to the server's MVAS port (every ~30s, movement-
    // independent) so a stationary in-space avatar refreshes LastAccessTime and
    // never trips the server's 2-minute idle reaper. Defined in
    // UDPClient_linux.cpp. Public because Connection::ProcessSectorServerOpcode
    // also calls these on the global-plane socket to punch + keep alive the NAT
    // mapping to server:3806 (see ClientToServer_linux_stubs.cpp 0x0002 LOGIN).
    void    SendServerKeepalive();
    // Start the 0x3005 keepalive thread on THIS UDPClient (guarded so a re-login
    // does not stack threads -- the surviving thread picks up the latest
    // m_PlayerID on its next tick). The same thread also drives the 0x1004
    // position feed (SendPositionIfChanged), polled at a fast cadence.
    void    StartMVASKeepalive();
    // MVAS position feed (PB-2). When the in-client position hook is publishing
    // (Win32/WINE only -- see ClientPositionShared.h), read the latest ship
    // position/orientation and, if it moved since the last send, stream it to
    // the server as opcode 0x1004 on MVAS_LOGIN_PORT. No-op when there is no
    // live feed (always the case for the Linux-native docker proxy).
    void    SendPositionIfChanged();
    bool    VerifyConnection();

	unsigned long checksum(char *buffer, int size);		// checksum

    void    SendLogin();
    void    SendAccount(char *username, char *password, char *info);
    void    SendResponse(long player_id, short port, short opcode, unsigned char *data, size_t length);
    char  * SendTicket(char *ticket);
    void    SetPlayerID(long player_id)             { m_PlayerID = player_id; }
    long    PlayerID()                              { return m_PlayerID; }
    void    SetLoginComplete(bool flag)             { m_LoginComplete = flag; m_PacketDropThisSession = 0; }
    bool    GetLoginComplete()                      { return m_LoginComplete; }
	void	SetReceivedGalaxyMap()					{ m_GalaxyMapReceived = true; }
	void	SetStartReceived()						{ m_StartReceived = true; }
	bool	GetStartFailed()						{ return (m_GalaxyMapReceived && !m_StartReceived); }
	bool	GetStartReceived()						{ return m_StartReceived; }
	void	SetStartID(long start_id)				{ m_Start_ID = start_id; }
	long	GetStartID()							{ return m_Start_ID; }
	void	ResetGalaxyStart()						{ m_GalaxyMapReceived = false; m_StartReceived = false; }
    long    SendAvatarLogin(long char_slot);
    char  * CreateCharacter(GlobalCreateCharacter *create);
    char  * DeleteCharacter(long character_slot);
	void	SetAccountName(char *name);

    short   SendMasterLogin(long avatar_id, long sector_id, long *sector_ipaddr);
    void    ForwardClientOpcode(short opcode, short bytes, char *packet);

	void    SetClientPort(short port);   // Phase AH: defined in UDPClient_linux.cpp (kicks DTLS handshake)
    void    SetClientIP(long addr)                  { m_ClientIP = addr; }
    void    SetSectorID(long sector_id)             { m_SectorID = sector_id; }
    void    SetConnectionActive(bool active)        { m_ConnectionActive = active; }
    bool    ConnectionActive()                      { return m_ConnectionActive; }
    long    GetClientIP()                           { return m_IPAddr; }
    long    GetSectorID()                           { return m_SectorID; }

    void    StartLoginTimer();
    void    KillTCPConnection();
    void    BlankTCPConnection();
    void    SendLoginFail();

    Connection *EstablishTCPConnection(long ip_addr, short port = 0);
	Connection *EstablishGlobalConnection(long ip_addr);
    void    IncommingOpcodePreProcessing(short opcode, char *msg, short bytes, bool tcp = false); //examine incomming opcodes and react accordingly
	bool	HandleCustomOpcode(short opcode, char *ptr, u8 *tcp_packet, short &tcp_index, short length);
	u8    * GetQueueBuffer()						{ return m_QueueBuffer; }
	void	ValidAccount(unsigned char *msg, short len);
	void	ProcessAvatarList(unsigned char *msg, short len);
    // Phase AH: this socket's DTLS state for the CLI status reply. DtlsEnabled()
    // is the enforced policy (false only in the plaintext opt-out); EstablishedDtls()
    // counts associations whose handshake has completed. Both are 0/false when
    // m_Dtls is null (plaintext mode).
    bool    DtlsEnabled() const { return m_Dtls != nullptr; }
    size_t  EstablishedDtls() const;

private:
    int     UDP_RecvFromServer(char *buffer, int size,
                               long &src_addr, unsigned short &src_port);
    // Phase AH: dispatch one cleartext server->proxy datagram (sitting in
    // m_RecvBuffer) through the opcode switch -- the post-DTLS-decrypt path and
    // the plaintext-mode path share it.
    void    DispatchServerDatagram(int received);
    // Phase AH: raw sendto() bypassing DTLS, used to put handshake records (and
    // nothing else) on the wire toward a specific server (ip, port).
    void    RawSendToServer(const unsigned char *buffer, int bufferLen,
                            long ip, unsigned short port);
    // Phase AH: build this socket's client-role DTLS transport from the
    // resolved proxy policy (g_DtlsPlaintext/g_DtlsVerifyDomain/g_DtlsCaFile).
    // nullptr in opted-out plaintext mode; fail-closed exit on misconfig.
    void    InitDtls();
    // Phase AH: peer key for the DTLS association to (m_IPAddr, dest_port_host).
    // dest_port_host==0 means the socket's default peer port.
    uint64_t ServerPeerKey(short dest_port_host) const;
    // Phase AH: proactively start the DTLS handshake to a server port so app
    // data only flows once the association is Established (avoids pre-handshake
    // record concatenation). No-op in plaintext mode or if already established.
    void    DtlsKickHandshake(short dest_port_host);
    void    SignalWrongVersion(char *msg);
    void    TerminateClient(char *msg);
	void	PreStartReceived(char *msg);
    void    TerminateClient();
    void    SendResponse(short port, short opcode, unsigned char *data, size_t length);
    void    UDP_Send(short port, const char *buffer, int bufferLen);
	void	WaitForResponse();
    void    WaitForResponse(short port, short opcode, unsigned char *data, size_t length);
    void    CreateFrom(long ip_addr, short port);

    bool    OpenFixedPort(short port, long ip_addr);
    bool    OpenMultiPort(short port, long ip_addr);
    void    FixedClientComm();

	void	RecordLastHandoff(char *msg, short bytes);
    void    ValidateAccount     (char *msg, EnbUdpHeader *header);
    void    ReceiveAvatarList   (char *msg, EnbUdpHeader *header);
    void    AvatarLoginConfirm  (char *msg, EnbUdpHeader *header);
    void    CreateDeleteConfirm (char *msg, EnbUdpHeader *header);
    void    ProcessGlobalError  (char *msg, EnbUdpHeader *header);
    void    LoginFailAck();

    void    MasterLoginConfirm  (char *msg, EnbUdpHeader *header);

    void    ProcessClientOpcode  (char *msg, EnbUdpHeader *header);
    void    SendClientDataFile  (char *msg, EnbUdpHeader *header);
    bool    SendClientPacketSequence(char *msg);
    void    SendPacketSequence  (char *msg, EnbUdpHeader *header, bool continuation = false);
	void	SendLoginPacketSequence (char *msg, EnbUdpHeader *header);
    void    SendCachedGalaxyMap ();
    void    StartProspecting    (char *msg, u8* packet, short &index);
    void    TractorOre          (char *msg, u8* packet, short &index);
    void    LootItem            (char *msg, u8* packet, short &index);
	void	CreateObject		(char *msg, u8* packet, short &index);
	void	CreateResource		(char *msg, u8* packet, short &index);
	void	HandleStageConfirm	(char *msg, u8* packet, short &index);
    
    void    IncommingOpcodePostProcessing(short opcode, char *msg, short bytes); 

    void    SendLoungeOpcodes(unsigned char *data, size_t length);

    bool    CheckOpcodeOrder(short opcode, char *msg, EnbUdpHeader *header, int received);

    //MVAS
    bool    CheckPosition();
    void    ToggleSendFrequency(char *msg);

    //keepalive
    void    SendClientAlive();
    void    SendCommsAlive();
    // The 0x3005 server keepalive (SendServerKeepalive / StartMVASKeepalive /
    // MVASKeepaliveThread) lives in the public section above -- it is called
    // from Connection on the global-plane socket as well as internally.

private:
    long m_Port;
    long m_IPAddr;
    long m_SectorID;
    long m_PlayerID;
	long m_Start_ID;
    bool m_recv_thread_running;
    SOCKET m_Listen_Socket;
    struct sockaddr_in m_SockAddr;
    short m_ConnectionType;
    unsigned char m_RecvBuffer[MAX_UDPC_BUFFER];
    unsigned char m_SendBuffer[MAX_UDPC_BUFFER];
    unsigned char m_QueueBuffer[MAX_QUEUE_BUFFER];
	unsigned char m_SplitPacketBuffer[MAX_QUEUE_BUFFER*2];
	unsigned char *m_PacketSlots[SLOT_RANGE];
	unsigned long m_SlotIndex;
	unsigned long m_SplitPacketStart;
	unsigned char *m_SplitPacketptr;
	long		  m_SplitPacketLength;

    bool m_logged_in;
    bool m_global_account_rcv;
    bool m_ConnectionActive;
    char *m_ticket;
    short m_ticket_length;
    char m_AccountName[128];
    short m_QueueBufferFill;

	bool m_GalaxyMapReceived;
	bool m_StartReceived;

    short m_ClientPort;
    long m_ClientIP;
    long m_CurrentPacketNum;
    bool m_AlternatePorts;
    bool m_LoginComplete;
    bool m_Resync;
    // Linux unconnected-socket flag — see ctor comment above. Win32 ignores.
    bool m_Unconnected;

    // Phase AH: per-socket client-role DTLS transport. nullptr in opted-out
    // plaintext mode -- then the recv/send paths behave exactly as before. One
    // socket == one transport, multiplexing a DTLS association per server
    // (ip,port) this socket talks to.
    net7::DtlsTransport *m_Dtls;
	unsigned long m_PacketResendTimer;

    // MVAS idle-keepalive (Linux): started once per session on the sector
    // plane in FixedClientComm(); m_KeepaliveSeq fills the EnbUdpHeader
    // sequence field (server ignores it -- HandleKeepCommsAlive reads only
    // player_id -- but the real proxy increments it, so we mirror that).
    bool    m_KeepaliveStarted;
    int32_t m_KeepaliveSeq;

    // MVAS position feed (PB-2). Last position/orientation actually streamed to
    // the server, so SendPositionIfChanged only sends on real movement (the
    // real Net7Proxy is change-gated too -- a stationary ship stops feeding).
    // m_PosFeedSeq fills the EnbUdpHeader sequence field.
    float   m_LastFedPos[3];
    float   m_LastFedHeading[3];
    bool    m_HaveFedOnce;
    int32_t m_PosFeedSeq;
    // Read the latest ship position/orientation the in-client hook sent us over
    // the loopback intake socket (freya/client-injection/ClientPositionShared.h); false when no
    // live feed (no hook running, or its last sample has gone stale). Drives
    // SendPositionIfChanged. Works on BOTH the Linux docker proxy and the
    // Win32/WINE proxy -- the transport is a loopback UDP datagram, not a
    // namespace-bound shared mapping. Lazily binds the intake port on first call.
    bool    ReadClientShipPosition(float pos[3], float heading[3]);
    SOCKET  m_PosIntakeSock;     // loopback UDP intake (INVALID_SOCKET until bound)
    bool    m_PosIntakeTried;    // bind attempted once (success or fail) -- no retry
    bool    m_HavePosSample;     // >=1 valid datagram received this session
    float   m_PosSample[3];      // most recent drained position
    float   m_HeadingSample[3];  // most recent drained orientation
    unsigned long m_PosSampleTick; // GetNet7TickCount() at last valid datagram

    // The 0x1004 position feed + 0x3005 keepalive MUST be started ONLY on the
    // global plane (m_UDPGlobalClient -- the UNCONNECTED socket whose RecvThread
    // relays sector S->C to the TCP client). There are TWO server-facing UDP
    // planes: m_UDPClient (sector/master, connect()'d to UDP_MASTER_SERVER_PORT)
    // and m_UDPGlobalClient (global, unconnected). The server runs a SINGLE S->C
    // delivery socket (server_mgr m_UDPConnection bound to MVAS_LOGIN_PORT 3806),
    // and every 0x1004/0x200F it receives calls SetPlayerPortIP(source) --
    // repointing ALL of this avatar's sector S->C at the source socket of that
    // datagram. The feed therefore has to emit from the SAME socket that drains
    // S->C, i.e. the global plane. Starting it on the sector plane as well (the
    // old FixedClientComm bug) makes the two planes fight over SetPlayerPortIP:
    // the fast feed from m_UDPClient (a socket connect()'d to 3808, so the kernel
    // drops the server's 3806 stream, AND not the relay socket) repeatedly steals
    // the mapping and black-holes chat/target/warp. Start it on the global plane
    // and nowhere else.

    PacketList m_Packets;
    short m_PacketTimeout;
	short m_PacketDropThisSession;
	u32   m_PacketTimer;

	ServerHandoff m_Server_handoff;
    Connection *m_ServerTCP;

    struct ReSend
    {
        long packet_start;    
        long packet_count;
    };
};


#endif