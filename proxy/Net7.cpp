// Net7.cpp
#ifdef WIN32
#define _WIN32_WINNT _WIN32_WINNT_WINXP
#include <process.h>
#endif
#include "Net7.h"
#include "ServerManager.h"
// UDPConnection.h is the server-side header (server/src/); proxy/ has
// only UDPClient.h. The client-launcher-era code referenced UDPConnection
// directly but the symbol is unused in the proxy translation unit.
#ifdef WIN32
#include "UDPConnection.h"
#endif
#include "UDPClient.h"

#ifdef WIN32
#pragma comment(lib, "wsock32.lib")
#pragma comment(lib, "ssleay32.lib")
#pragma comment(lib, "libeay32.lib")
#endif

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

#ifdef WIN32
bool StartENBClient()
{
	STARTUPINFO si = {NULL};
	PROCESS_INFORMATION pi = {NULL};
	int success;
	char cur_dir[MAX_PATH+2];
    char start_dir[MAX_PATH+2];
	_splitpath(g_exe, cur_dir, &cur_dir[2], NULL, NULL);
	cur_dir[1] = ':';

	if (!engine_open_process("Earth & Beyond"))
	{
        GetCurrentDirectory(MAX_PATH+2, start_dir);

		SetCurrentDirectory(cur_dir);

		success = CreateProcess(g_exe, g_cmd, NULL, NULL, TRUE,
			CREATE_DEFAULT_ERROR_MODE, NULL, NULL, &si, &pi);

		LogMessage("Starting E&B...\n");
        SetCurrentDirectory(start_dir);
	}

	if (GetProcessHandle())
	{
		LogMessage("Launch E&B successful\n");
	}
	else
	{
		fprintf(stderr,"\nAppears to be an initialisation problem starting E&B\nTerminate E&B and restart Launcher\n");
		return (false);
	}

	return (true);
}
#else
// Linux: Net7Proxy here is a SERVER-SIDE TCP listener. The original
// Net7Proxy was a client launcher: it spawned ENB.exe, attached Detours,
// and patched the client in memory. None of that applies server-side.
// Stub it as a no-op success so any legacy call site that still calls
// StartENBClient() doesn't error out.
bool StartENBClient()
{
    return true;
}
#endif

#ifdef WIN32
int main(int argc, char* argv[])
{
    long port = PROXY_LOCAL_TCP_PORT;
    char *domain = "";
	char cmd_buffer[MAX_PATH];
	g_cmd = &cmd_buffer[0];
	char exe_buffer[MAX_PATH];
	g_exe = &exe_buffer[0];
	char exeL_buffer[MAX_PATH];
	char *exeL = &exeL_buffer[0];
	bool UseDetours = false;
    bool local = false;

	g_StartTick = GetTickCount();
    g_internal_addr = default_addr;

    printf("Net7Proxy version %s\n", VERSION);

    GetCurrentDirectory(sizeof(exeL_buffer),exeL_buffer);
  	strcat(exeL, "\\Detours.exe");
	strcpy(g_exe, "C:\\Program Files\\EA GAMES\\Earth & Beyond\\release\\client.exe");

	srand((unsigned)GetTickCount());

	_ASSERTE( _CrtCheckMemory( ) );

    // No arguments indicate a standalone server via localhost
    for (int i = 1; i < argc; i++)
    {
	    if ((strncmp(argv[i], "/ADDRESS:", 9) == 0))
	    {
            g_server_addr = argv[i] + 9;
            if (strcmp(g_server_addr, "127.0.0.1") == 0)
            {
                g_server_addr = 0;
            }
        }
		else if (strncmp(argv[i], "/LC", 3) == 0)
		{
			g_LocalCert = true;
		}
		else if (strncmp(argv[i], "/L", 2) == 0)
		{
			UseDetours = true;
		}
		else if (strncmp(argv[i], "/CLIENT:", 8) == 0)
		{
			g_exe = argv[i] + 8;
		}
		else if (strncmp(argv[i], "/OPCODES", 8) == 0)
		{
			g_OpcodeDebugging = true;
		}
		else if (strncmp(argv[i], "/POPT", 5) == 0)
		{
			g_Packet_Opt_requested = true;
		}
		else if (strncmp(argv[i], "/SSL:", 5) == 0)
		{
			long ssl_p = atoi(argv[i] + 5);
			if (ssl_p > 0)
			{
				ssl_port = (unsigned short)ssl_p;
			}
		}
		else if (strncmp(argv[i], "/DEBUGL", 5) == 0)
		{
			g_Debug_Launch = true;
		}
        else
        {
            printf("Unrecognized switch: '%s'\n", argv[i]);
            Usage();
			if (g_Debug_Launch)
			{
				getchar();
			}
            return(1);
        }
    }

	if (g_Debug_Launch)
	{
		for (int i = 1; i < argc; i++)
		{
			printf("Params: %s\n", argv[i]);
		}
	}

    if (g_server_addr == 0)
    {
        g_server_addr = default_addr;
        local = true;
    }

	if (UseDetours == true)
	{
		sprintf(g_cmd, "Detours.exe /ADDR:%s /CLIENT:\"%s\"",g_server_addr, g_exe);

		g_exe = exeL;
	}
	else
	{
		if (g_LocalCert)
		{
			sprintf(g_cmd, "client.exe -SERVER_ADDR %s -PROTOCOL TCP", default_addr);
		}
		else
		{
			sprintf(g_cmd, "client.exe -SERVER_ADDR %s -PROTOCOL TCP", g_server_addr);
		}
	}

    sprintf(g_DomainName, "local.net-7.org");

    // Winsock startup
    WSADATA	wsaData = {NULL};
	WSAStartup(MAKEWORD(2, 0), &wsaData);

	struct hostent * host = gethostbyname(g_DomainName);
	if (!host)
	{
        int err = WSAGetLastError();
        printf("Unable to resolve IP address for %s (error=%d)\n", g_DomainName, err);
		if (g_Debug_Launch)
		{
			getchar();
		}
        return(1);
    }
    unsigned char *ip = (unsigned char *) host->h_addr;

	unsigned long ip_address_internal = inet_addr(g_internal_addr);
	unsigned long net7_server_ip_address = inet_addr(g_server_addr);

    if (local)
    {
        char strLocal[MAX_PATH] = { 0 };
        if (SOCKET_ERROR != gethostname(strLocal, MAX_PATH))
        {
            struct hostent* hp;
            hp = gethostbyname(strLocal);
            if (hp != NULL)	
            {
                strcpy(strLocal, hp->h_name);
                net7_server_ip_address = *((ULONG *) hp->h_addr_list[0]);
            }
        }
    }

	_ASSERTE( _CrtCheckMemory( ) );

    //first establish contact with server

    //open UDP receive connection
    UDPClient UDP_connection(MVAS_LOGIN_PORT, CLIENT_TYPE_FIXED_PORT, net7_server_ip_address);

    LogMessage("Attempting to connect to IP address: %s\n", g_server_addr);

    if (UDP_connection.VerifyConnection())
    {
        if (!StartENBClient())
        {
			::MessageBox(NULL, "Unable to start E&B client", "Net7Proxy", MB_ICONERROR);
			if (g_Debug_Launch)
			{
				getchar();
			}
            return (1);
        }

        UDPClient UDP_sendport(GLOBAL_SERVER_PORT, CLIENT_TYPE_MULTI_PORT, net7_server_ip_address);
        //now start the TCP link to E&B
        ServerManager server_mgr(false, ip_address_internal, (short) port, (short) 1, true, ip_address_internal);

        //server_mgr.SetPlayerMgrGlobalMemoryHandler();  
        server_mgr.SetUDPConnections(&UDP_connection, &UDP_sendport);

        //now patch E&B
        PatchClient();

		if (g_Packet_Opt_requested)
		{
			LogMessage("UDP packet optimisation selected (reduces server load).\n");
		}

		_ASSERTE( _CrtCheckMemory( ) );
    
        server_mgr.RunServer();
    }
	else if (!g_ServerShutdown)
	{
		::MessageBox(NULL, "Server Failed to respond to Login attempt", "Net7Proxy", MB_ICONERROR);
	}

    // Winsock cleanup
    WSACleanup();

	if (g_Debug_Launch)
	{
		getchar();
	}
    return 0;
}
#else
// Linux: SERVER-SIDE main. Net7Proxy here is the TCP entry point for the
// Westwood RSA+RC4 handshake on port 3801 (MASTER_SERVER_PORT). The
// original client-launcher main() (CreateProcess, Detours, MessageBox)
// is irrelevant server-side and lives in #ifdef WIN32 above.
//
// What this main does:
//   - parse minimal command-line (currently none required)
//   - set up g_DomainName (default: local.net-7.org)
//   - construct a ServerManager and call RunMasterServer()
// What it intentionally does NOT do (vs. Win32 main):
//   - launch the ENB.exe client (irrelevant server-side)
//   - patch the client in memory (Detours)
//   - open a UDP receive connection to verify a server is reachable;
//     server-side Net7Proxy IS the server.
int main(int argc, char* argv[])
{
    g_StartTick = GetTickCount();
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
    UDPClient udp_to_server(MVAS_LOGIN_PORT,
                            CLIENT_TYPE_FIXED_PORT,
                            ip_address_internal);

    // The proxy uses ONE UDPClient instance for both roles on Linux:
    // both m_UDPConnection (send-to-server) and m_UDPClient (recv-from-
    // server) point at it. The Win32 path had two distinct objects
    // (FIXED + MULTI port); on Linux we only port FIXED, and that one
    // object handles both the send and recv halves.
    server_mgr.SetUDPConnections(&udp_to_server, &udp_to_server);

    server_mgr.RunServer();

    return 0;
}
#endif

#ifdef WIN32
static volatile HANDLE 	g_ProcessHandle 	= (HANDLE) INVALID_HANDLE_VALUE;
static volatile bool	g_EngineInUse		= FALSE;
#endif
long g_AddrStore = 0x00b6e5a8; //this virtual offset places us within the known .data area offset.

//=========================

#ifdef WIN32
bool engine_close_process()
{
	if (g_EngineInUse) 
	{
		// are we in use?
		if (CloseHandle(g_ProcessHandle)) 
		{
			// yup, so close the process handle
			g_EngineInUse = FALSE;
			return TRUE;
		}
	}

	return FALSE;
}

//=========================

bool engine_open_process(char * processwindowtitle) 
{

	HWND 	TargetWindowHandle	= (HWND) -1;
	DWORD	Process_Id;
	LPDWORD PID;
	HANDLE 	WindowProcessId		= (HANDLE) INVALID_HANDLE_VALUE;

	if (g_EngineInUse) 
	{
		// we are already in use...
		return FALSE;
	}

	TargetWindowHandle = FindWindow(NULL, processwindowtitle); 	// see if it exists

	if (TargetWindowHandle) 
	{
		// got the window handle...
		Process_Id = GetWindowThreadProcessId(TargetWindowHandle, (LPDWORD)&PID); //get a PROCESS number

		if (Process_Id) 
		{
			// we have a valid process id, now to open it...
			g_ProcessHandle = OpenProcess(PROCESS_ALL_ACCESS, FALSE, (DWORD)PID);

			if (g_ProcessHandle) 
			{
				// process succesfully opened
				g_EngineInUse = TRUE;
				return TRUE;
			}
			else
			{
				 long error_code = GetLastError();
				 LogMessage("Error code: %d [%x]\n", error_code, error_code);
			}


		}
	}

	return FALSE;
}

//=========================

bool engine_check_process(char * processwindowtitle)
{
	HWND 	TargetWindowHandle	= (HWND) -1;
	DWORD	Process_Id;
	LPDWORD PID;
	HANDLE 	WindowProcessId		= (HANDLE) INVALID_HANDLE_VALUE;

	TargetWindowHandle = FindWindow(NULL, processwindowtitle); 	// see if it exists

	if (TargetWindowHandle) 
	{
		// got the window handle...
		Process_Id = GetWindowThreadProcessId(TargetWindowHandle, (LPDWORD)&PID); //get a PROCESS number

		if (Process_Id) 
		{
			return TRUE;
		}
	}
	return FALSE;
}

bool engine_write_process(LPVOID lpBaseAddress, LPVOID lpBuffer, DWORD nSize) 
{
	bool SuccessCode = FALSE;

	if (g_EngineInUse) 
	{
		// are we in use
		if (g_ProcessHandle) 
		{
			// do we have a process handle

			DWORD BytesWritten = 0;
			bool  ProcessSuspended = FALSE;

			if (SuspendThread(g_ProcessHandle) != (DWORD) -1) 
			{
				// suspend the thread - its safer
				ProcessSuspended = TRUE;
			}

			if (WriteProcessMemory(g_ProcessHandle, lpBaseAddress, lpBuffer, nSize, &BytesWritten) && BytesWritten == nSize) 
			{
				// write was successful
				// flush the instruction cache (for safety)
				FlushInstructionCache(g_ProcessHandle, lpBaseAddress, nSize);
				SuccessCode = TRUE;
			}
			
			// resume the process if we suspended it
			if (ProcessSuspended) 
			{
				ResumeThread(g_ProcessHandle);
			}

		}
	}
	else
	{
		LogMessage("unable to write to process\n");
	}

	return SuccessCode;
}

//=========================

bool engine_read_process(LPVOID lpBaseAddress, LPVOID lpBuffer, DWORD nSize) 
{
	bool SuccessCode = FALSE;

	if (g_EngineInUse) 
    {
		if (g_ProcessHandle) 
        {
			DWORD BytesRead = 0;
			bool  ProcessSuspended = FALSE;
			DWORD BytesWritten;

			if (SuspendThread(g_ProcessHandle) != (DWORD) -1) 
            {
				ProcessSuspended = TRUE;
			}

			if (ReadProcessMemory(g_ProcessHandle, lpBaseAddress, lpBuffer, nSize, &BytesWritten) && BytesRead == nSize) 
            {
				SuccessCode = TRUE;
			}

			if (ProcessSuspended) 
            {
				ResumeThread(g_ProcessHandle);
			}

		}
	}
	else
	{
		LogMessage("unable to read process\n");
	}

	return SuccessCode;
}

//=========================

bool ShutdownClient() 
{
	if (g_EngineInUse) 
    {
		if (TerminateProcess(g_ProcessHandle, (UINT) 0x0D1ED1E)) 
        {
			CloseHandle(g_ProcessHandle);
			g_EngineInUse = FALSE;
			return TRUE;
		}
	}
	
	return FALSE;
}
	
//=========================

void PatchClient()
{
	DWORD zero = 0;
	DWORD addr_off;

	unsigned char inject_buffer1[] =
	{
		0x8B, 0x0D, 0xa8, 0xe5, 0xb6, 0x00, //this assembly code checks to see if there's already an address for
		0x85, 0xC9, //6                     //the player coords, if not then it checks to make sure this is a valid
		0x75, 0x2E,                         //player hull type, if so then it assumes that it will be the player's
		0x80, 0x38, 0x53,                   //hull (since player's hull is always the first location sent).
		0x75, 0x29, //13                    //Then it stores the write location of the player hull coords in a
		0x80, 0x78, 0x02, 0x48,             //fixed position (which is g_AddrStore).
		0x75, 0x23, //19
		0x8B, 0x48, 0x14,
		0xBB, 0xa8, 0xe5, 0xb6, 0x00, //this is the addr to write ptr to (25)
		0x89, 0x0B,
		0x33, 0xDB,
		0xEB, 0x15,
		0x01,  
		0x90,
		0x68, 0x28, 0x3C, 0xB7, 0x00,
		0x50,
		0x51,
		0xE8, 0x6A, 0xE2, 0x19, 0x00,
		0x83, 0xC4, 0x10,
		0xC2, 0x08, 0x00,
		0x90,
		0x8B, 0x48, 0x14,
		0x8D, 0x44, 0x19, 0x48,
		0xE9, 0x30, 0x6E, 0x10, 0x00,	
		0x00, 0x00, 0x00, 0x00
	};
	//0x00b6e5a8
	*((long*)&inject_buffer1[25]) = g_AddrStore;
	*((long*)&inject_buffer1[2]) = g_AddrStore;

	unsigned char inject_buffer2[] =
	{
		0xE9, 0x8E, 0x91, 0xEF, 0xFF,  //jump to 888278
		0x90,
		0x90
	};

	unsigned char inject_buffer3[] =
	{
		0x8B, 0xD5						//set client's perceived time difference between sendtime and receive to 0
	};									//this makes movement appear very smooth

	unsigned char inject_buffer4[] =
	{
		0x55,
		0x33, 0xED,
		0x89, 0x2D, 0xA8, 0xE5, 0xB6, 0x00, //this resets the client coords position each time we change sectors
		0x5D,
		0xC2, 0x04, 0x00,
		0x90,
		0x90,
		0x90,
		0x90
	};

	*((long*)&inject_buffer4[5]) = g_AddrStore;

    unsigned char inject_buffer5[] =
    {
        0x56,                                   //PUSH ESI
        0x8B, 0x77, 0x0C,                       //MOV ESI,DWORD PTR DS:[EDI+C]   //copy gameID to ESI 
        0x89, 0x35, 0xAC, 0xE5, 0xB6, 0x00,     //MOV DWORD PTR DS:[B6E5AC],ESI  //store gameID into 0x00b6e5ac (gameID flag)
        0x5E,                                   //POP ESI
        0xC2, 0x04, 0x00                        //RETN 4
    };

	addr_off = g_AddrStore; //this addr is a fix temp store 
	engine_write_process((void*)addr_off, (void*)&zero, 16); //make room for addr for position, orientation client gameID and also internal orientation update flag
	addr_off = 0x00888278; //this is the addr of the padding around the GPS request code
	engine_write_process((void*)addr_off, (void*)inject_buffer1, sizeof(inject_buffer1));
	addr_off = 0x0098F0E5; //this is the jump hijack for the object coords transform loop
	engine_write_process((void*)addr_off, (void*)inject_buffer2, sizeof(inject_buffer2));
	addr_off = 0x007379E2; //this is the addr of the code to calculate the lag for movement
	engine_write_process((void*)addr_off, (void*)inject_buffer3, sizeof(inject_buffer3));
	addr_off = 0x00767009; //this is the addr of the code at the end of the redirect packet code
	engine_write_process((void*)addr_off, (void*)inject_buffer4, sizeof(inject_buffer4));
    addr_off = 0x00733620; //store client GameID to read buffer
    engine_write_process((void*)addr_off, (void*)inject_buffer5, sizeof(inject_buffer5));
}
#endif // WIN32 — end engine_* / PatchClient block

unsigned long GetNet7TickCount()
{
    return ((GetTickCount() - g_StartTick) & 0x7FFFFFFF);
}

#ifdef WIN32
bool GetProcessHandle()
{
	long count = 0;

	while (count < 50)
	{
		if (engine_open_process("Earth & Beyond"))
		{
			break;
		}
		count++;
		Sleep(1000);
	}

	if (g_ProcessHandle == INVALID_HANDLE_VALUE)
	{
		LogMessage("Failed to open E&B\n");
		return false;
	}
	else
	{
		return true;
	}
}
#else
// Linux: client-process functions are no-ops server-side.
bool GetProcessHandle() { return true; }
bool engine_open_process(char * /*processwindowtitle*/) { return false; }
bool engine_read_process(LPVOID, LPVOID, DWORD) { return false; }
void PatchClient() { /* no client to patch server-side */ }
bool ClientStillRunning() { return true; }
bool ShutdownClient() { return true; }
#endif

void WaitForEngineReady()
{
#ifdef WIN32
	long counter = 0;
	while (!g_EngineInUse && counter < 300 && !g_ServerShutdown)
	{
		Sleep(250);
		counter++;
	}
#endif
}

void WaitForLogin()
{
	long counter = 0;
	while (!g_LoggedIn && counter < 300 && !g_ServerShutdown)
	{
		Sleep(250);
		counter++;
	}
}

#ifdef WIN32
bool ClientStillRunning()
{
	return (engine_check_process("Earth & Beyond"));
}
#endif
