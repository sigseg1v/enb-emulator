// Net7.cpp
//
// Phase M: server-native-Linux only. The 2010-era Win32 client-launcher
// code paths (CreateProcess + Detours + WriteProcessMemory patching of
// the client) were deleted — Net7Proxy is the server-side TCP entry
// point for the Westwood RSA+RC4 handshake (MASTER_SERVER_PORT, 3801)
// and has no business spawning a client. Function declarations remain
// in proxy/Net7.h so legacy WIN32-walled translation units
// (UDPProxyMVAS.cpp, UDPProxyToClient.cpp, UDPProxyToGlobal.cpp) still
// link; the bodies are no-op stubs on Linux.

#include "Net7.h"
#include "ServerManager.h"
#include "UDPClient.h"

char g_LogFilename[MAX_PATH];
char g_InternalIP[MAX_PATH];
char g_DomainName[MAX_PATH];
char *g_server_addr = (0);
char *default_addr = "127.0.0.1";
char *g_internal_addr = (0);

bool g_Debug = false;
bool g_ServerShutdown = false; // Terminated the global Server

char *g_exe;
char *g_cmd;

ServerManager * g_ServerMgr = 0;
GMemoryHandler * g_GlobMemMgr = 0;
AccountManager * g_AccountMgr = 0;

bool g_OpcodeDebugging = false;

bool g_LoggedIn = false;
unsigned long g_StartTick;
bool g_LocalCert = false;
unsigned short ssl_port = SSL_PORT;
bool g_Packet_Opt_requested = false;
bool g_Debug_Launch = false;

void Usage()
{
	printf("Net7Proxy Usage:\n\n");
	printf("Starts E&B client to interface with server:\n");
	printf("   Net7Proxy /ADDRESS:(ip address)\n");
}

// Server-side: the Win32 StartENBClient() spawned ENB.exe + attached
// Detours. None of that applies on Linux. Stubbed as a no-op success.
bool StartENBClient()
{
    return true;
}

// SERVER-SIDE main. Net7Proxy here is the TCP entry point for the
// Westwood RSA+RC4 handshake on port 3801 (MASTER_SERVER_PORT).
//
// What this main does:
//   - parse minimal command-line (currently none required)
//   - set up g_DomainName (default: local.net-7.org)
//   - construct a ServerManager and call RunMasterServer()
// What it intentionally does NOT do (vs. the deleted Win32 main):
//   - launch the ENB.exe client (irrelevant server-side)
//   - patch the client in memory (Detours)
//   - open a UDP receive connection to verify a server is reachable;
//     server-side Net7Proxy IS the server.
int main(int argc, char* argv[])
{
    g_StartTick = Net7TickMs();
    g_internal_addr = default_addr;
    g_server_addr  = default_addr;

    // Make stdout line-buffered so `docker logs -f` shows messages as
    // they happen (default for a non-tty is fully-buffered).
    setvbuf(stdout, NULL, _IOLBF, 0);

    printf("Net7Proxy (server-side, Linux) version %s\n", VERSION);
    fflush(stdout);

    for (int i = 1; i < argc; i++)
    {
        if (strncmp(argv[i], "/ADDRESS:", 9) == 0)
        {
            g_internal_addr = argv[i] + 9;
        }
        else if (strncmp(argv[i], "/LC", 3) == 0)
        {
            g_LocalCert = true;
        }
        else if (strncmp(argv[i], "/OPCODES", 8) == 0)
        {
            g_OpcodeDebugging = true;
        }
        else if (strncmp(argv[i], "/SSL:", 5) == 0)
        {
            long ssl_p = atoi(argv[i] + 5);
            if (ssl_p > 0)
            {
                ssl_port = (unsigned short)ssl_p;
            }
        }
        else
        {
            printf("Unrecognized switch: '%s'\n", argv[i]);
            Usage();
            return 1;
        }
    }

    // g_DomainName defaults to local.net-7.org; docker-compose extra_hosts
    // remaps it to the container loopback so gethostbyname() succeeds.
    snprintf(g_DomainName, MAX_PATH, "local.net-7.org");

    // Bind on 0.0.0.0 by default so the container is reachable from the
    // host. The TcpListener actually uses INADDR_ANY (TcpListener.cpp:80)
    // regardless of m_IpAddress.
    unsigned long ip_address_internal = inet_addr(g_internal_addr);

    LogMessage("Net7Proxy: binding TCP %d (MASTER_SERVER_PORT) on %s\n",
               MASTER_SERVER_PORT, g_internal_addr);
    LogMessage("Net7Proxy: binding TCP %d (GLOBAL_SERVER_PORT) on %s\n",
               GLOBAL_SERVER_PORT, g_internal_addr);

    // The "is_master_server", "max_sectors", "standalone" constructor args
    // are ServerManager's; for the proxy-as-listener role we want the
    // master-server path (RunMasterServer creates the TCP listeners).
    ServerManager server_mgr(true /*master*/,
                             ip_address_internal,
                             (short) PROXY_LOCAL_TCP_PORT,
                             (short) 1,
                             true /*standalone*/,
                             ip_address_internal);

    // Phase K: stand up a UDPClient pointing at the game server's
    // UDP_MASTER_SERVER_PORT (3808). HandleMasterJoin will use this to
    // send the MasterHandoff opcode and wait for the sector-port reply.
    // The ip_addr arg is a FALLBACK only — UDPClient::OpenFixedPort
    // first tries getenv("NET7_GAME_SERVER_HOST") (default "server",
    // which docker resolves to the game server container).
    //
    // The ctor's first arg is the PEER port (Linux interprets it as
    // such — see UDPClient_linux.cpp::OpenFixedPort). Win32 historically
    // passed MVAS_LOGIN_PORT here; that was a launcher-side artefact and
    // is wrong server-side.
    UDPClient udp_to_master(UDP_MASTER_SERVER_PORT,
                            CLIENT_TYPE_FIXED_PORT,
                            ip_address_internal);

    server_mgr.SetUDPConnections(&udp_to_master, &udp_to_master);

    // Phase K (2026-05-24): second UDPClient for the proxy<->server
    // "global" control plane (peer = UDP_GLOBAL_SERVER_PORT 3810).
    // Was TCP via SSL_LOCALCERT_LOGIN_PORT in the kyp-era Win32 build;
    // Phase Q deleted the server-side TCP cluster so the wire is now
    // UDP — symmetric with the master plane.
    //
    // Two UDPClients (not one) because Linux's connected SOCK_DGRAM
    // filters incoming datagrams by (peer_addr, peer_port); a single
    // socket connect()'d to 3808 would never see replies from 3810
    // and vice versa.
    //
    // Phase K (2026-05-25): construct the global plane UNCONNECTED. The
    // global plane source port is what the server records as
    // player->m_Player_Port during HandleGlobalTicketRequest (server/src/
    // UDP_Global.cpp:227 sets it from the AVATARLOGIN packet's source).
    // Once login completes, all server->proxy in-game UDP comes from
    // MVASauth (server:3806) because player->m_UDPConnection is always
    // MVASauth (server/src/UDP_Master.cpp:73). That packet is destined for
    // (proxy_ip, proxy_global_src) — i.e. THIS socket — but from a
    // different peer port than the global plane's 3810. A connect() here
    // would have the kernel drop those packets. Unconnected mode keeps
    // the same default-peer for outgoing sendto() while accepting recv()
    // from any peer port (with a source-IP whitelist check in
    // UDP_RecvFromServer).
    UDPClient udp_to_global(UDP_GLOBAL_SERVER_PORT,
                            CLIENT_TYPE_FIXED_PORT,
                            ip_address_internal,
                            true /*unconnected*/);

    server_mgr.SetGlobalUDPClient(&udp_to_global);

    server_mgr.RunServer();

    return 0;
}

long g_AddrStore = 0x00b6e5a8; //this virtual offset places us within the known .data area offset.

unsigned long GetNet7TickCount()
{
    return ((Net7TickMs() - g_StartTick) & 0x7FFFFFFF);
}

// Client-process functions are no-ops server-side. They remain because
// WIN32-walled translation units (UDPProxyMVAS.cpp, UDPProxyToClient.cpp,
// UDPProxyToGlobal.cpp) name them in their declarations / dispatch.
bool GetProcessHandle() { return true; }
bool engine_open_process(char * /*processwindowtitle*/) { return false; }
bool engine_read_process(void*, void*, uint32_t) { return false; }
void PatchClient() { /* no client to patch server-side */ }
bool ClientStillRunning() { return true; }
bool ShutdownClient() { return true; }

void WaitForEngineReady()
{
    // Server-side: there is no client engine to wait for.
}

void WaitForLogin()
{
	long counter = 0;
	while (!g_LoggedIn && counter < 300 && !g_ServerShutdown)
	{
		usleep(250 * 1000);
		counter++;
	}
}
