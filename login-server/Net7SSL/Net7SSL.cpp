// Net7.cpp
//
// Phase J (Linux port): this TU contains the original Win32 process entry
// point, the (Win32-only) Net-7 instance mutex, the MySQL-driven sector
// table loader, and the legacy "register-with-auth-server" helper. The
// dependency cone (MailslotManager, MemoryHandler, ConnectionManager,
// AccountManager, mysqlplus, UDPClient, ServerManager) is heavyweight
// Windows code. For Phase J we wall the entire original file behind
// `#ifdef WIN32` and provide a minimal Linux `main()` plus the few
// globals the rest of the Linux build actually links against.
//
// This mirrors proxy/Net7.cpp.
#ifdef WIN32
#define _WIN32_WINNT _WIN32_WINNT_WINXP
#include <process.h>
#include "Net7SSL.h"
#include "SSL_Listener.h"
#include "SSL_Connection.h"
#include "MailslotManager.h"
#include "MemoryHandler.h"
#include "ConnectionManager.h"
#include "AccountManager.h"
#include "mysql/mysqlplus.h"
#include "UDPClient.h"
#include "ServerManager.h"
#include <map>

LPTSTR g_OutputSlot = TEXT("\\\\.\\mailslot\\net7");
LPTSTR g_InputSlot = TEXT("\\\\.\\mailslot\\net7SSL");
LPTSTR g_EventName = TEXT("Net7SSLSlot");

char g_Galaxy_Name[MAX_PATH];

#define VERSION "1.0"
#define VERSION_N 100

#pragma comment(lib, "wsock32.lib")
#pragma comment(lib, "ssleay32.lib")
#pragma comment(lib, "libeay32.lib")
#pragma comment(lib, "libmySQL.lib")

char g_MySQL_User[MAX_PATH];
char g_MySQL_Pass[MAX_PATH];
char g_MySQL_Host[MAX_PATH];
char g_LogFilename[MAX_PATH];
char g_InternalIP[MAX_PATH];
char g_DomainName[MAX_PATH];
char *g_server_addr = (0);
char *default_addr = "127.0.0.1";
char *g_internal_addr = (0);

AccountManager * g_AccountMgr = 0;
unsigned long g_cumulative_mem = 0;

bool g_Debug = false;
bool g_ServerShutdown = false; // Terminated the global Server
bool g_ShuttingDown = false;

char *g_exe;
char *g_cmd;

bool g_LoggedIn = false;
unsigned long g_StartTick;
bool g_LocalCert = false;
unsigned short ssl_port = SSL_PORT;
bool Net7StillRunning();
unsigned long g_receive_time = 0;
char mutex_name[80];
SSL_DenyList * g_SSL_Deny_List;
long g_PlayerCount;
long g_MaxPlayerCount = 0;

//TODO: add these to server manager
MemorySlot<SSL_Connection> *g_SSL_Connections;
SslConnectionEntry *g_SslConnectionList;
ConnectionManager *g_ConnectionMgr;
ServerManager *g_ServerMgr;
extern sql_connection_c m_SQL_Conn;

typedef std::map<long, char*> SectorNameList;

SectorNameList SectorNames; 

void Usage()
{
	printf("Net7SSL Usage:\n\n");
	printf("Starts SSL listener for Net7:\n");
	printf("Using a separate process we can auto-recover from SSL failures\n");
}

int main_prog(int argc, char* argv[]);
bool RegisterSectorServer(short first_port, short max_sectors);

int main(int argc, char** argv)
{
	int ret;
	try	
	{
		if(ret = main_prog(argc,argv))
		{
			return ret;
		}
	}
	catch(...)
	{
		fprintf(stderr, "Net7SSL terminating.\n"); //Net7SSL will now shut down if any fault occurs
		return 1;
	}
}

int main_prog(int argc, char* argv[])
{
    char *domain = "";
    bool local = false;
	bool use_config_internal = false;

	g_StartTick = GetTickCount();
    g_internal_addr = default_addr;

	//check another instance of Net7SSL isn't already running
	strcpy(mutex_name, SSL_INSTANCE_MUTEX_NAME);

#ifdef WIN32
    // First, make sure we only have one instance of Net7SSL running
    HANDLE instance_mutex = ::CreateMutex(NULL, TRUE, mutex_name);
    if (instance_mutex == INVALID_HANDLE_VALUE)
	{
		return(1);
	}

    // if we did not create this mutex then .. another instance
    // is already running
    if (::GetLastError() == ERROR_ALREADY_EXISTS)
    {
        // close the mutex
		LogMessage("Net7SSL instance terminating, another instance already running.\n");
        ::CloseHandle(instance_mutex);
		return(1);
    }
#endif

	FILE *f = fopen(CONFIG_FILE, "r");
    if (f)
    {
        fseek(f, 0, SEEK_END);
        long file_size = ftell(f);
        fseek(f, 0, SEEK_SET);
        char *data = new char[file_size + 1];
        if (data)
        {
			char *Info;
			char *VarName;
            long size = fread(data, 1, file_size, f);
            data[size] = 0;
			VarName = strtok(data, "=");
			Info = strtok(NULL, "\n");
			do
            {
				if (!strcasecmp(VarName, "domain")) 
                {
					strcpy(g_DomainName, Info);
				}
				if (!strcasecmp(VarName, "mysql_user")) 
                {
					strcpy(g_MySQL_User, Info);
                }
				if (!strcasecmp(VarName, "mysql_pass")) 
                {
					strcpy(g_MySQL_Pass, Info);
				}
				if (!strcasecmp(VarName, "mysql_host")) 
                {
					strcpy(g_MySQL_Host, Info);
				}
				if (!strcasecmp(VarName, "internal_ip")) 
                {
					strcpy(g_InternalIP, Info);
					use_config_internal = true;
				}
				if (!strcasecmp(VarName, "galaxy_name")) 
                {
					strcpy_s(g_Galaxy_Name, sizeof(g_Galaxy_Name), Info);
					g_Galaxy_Name[sizeof(g_Galaxy_Name)-1] = '\0';
				}
				VarName = strtok(NULL, "=");
				Info = strtok(NULL, "\n");
			} 
            while(Info != NULL);

            delete [] data;
        }
        fclose(f);
    }
	else
	{
		sprintf(g_DomainName, "local.net-7.org");
	}

    g_AccountMgr = new AccountManager();

	g_SSL_Connections = new MemorySlot<SSL_Connection>(75);

	g_SSL_Deny_List = new SSL_DenyList();

	LoadSectorData();

    LogMessage("Net7SSL version %s\n", VERSION);
	LogMessage("Using IP: %s\n", g_InternalIP);

    if (!use_config_internal)
    {
        g_server_addr = default_addr;
        local = true;
    }

    // Winsock startup
    WSADATA	wsaData = {NULL};
	WSAStartup(MAKEWORD(2, 0), &wsaData);

	unsigned long ip_address_internal = inet_addr(g_InternalIP);
	unsigned long net7_server_ip_address = inet_addr(g_InternalIP);

    if (1)
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

	g_ServerMgr = new ServerManager(ip_address_internal);
	g_ConnectionMgr = new ConnectionManager();
	g_ServerMgr->RunMasterServer();

	//OK start the SSL listener
#ifndef SSL_SYSTEM_OFF
	SSL_Listener *SSL_listener = new SSL_Listener(ip_address_internal, SSL_PORT);
	RegisterSectorServer(SECTOR_SERVER_PORT, 1);
#endif
	MailManager *MailMgr = new MailManager();

    //open UDP receive connection
    UDPClient UDP_connection(MVAS_LOGIN_PORT, CLIENT_TYPE_FIXED_PORT, net7_server_ip_address);

    if (UDP_connection.VerifyConnection())
    {
        //UDPClient UDP_sendport(GLOBAL_SERVER_PORT, CLIENT_TYPE_MULTI_PORT, net7_server_ip_address);
	}
	else
	{
		return -1;
	}

	g_ServerMgr->SetUDPConnections(&UDP_connection, 0);

    //now simply go into a loop until Net7 stops running, or we receive an error for shutdown
	
	unsigned long send_check = g_receive_time = GetNet7TickCount() + 60*1000;
	unsigned long current_tick;
	while (g_ServerShutdown == false)
	{
		Sleep(500); //check connections every half second
		current_tick = GetNet7TickCount();
		g_ConnectionMgr->CheckSslConnections();
		g_ConnectionMgr->CheckConnections();
		MailMgr->CheckMessages();
		
		if (current_tick > (send_check + 10000))
		{
			MailMgr->WriteMessage("Ping");
			send_check = current_tick;
		}
#if 1
		if (current_tick > (g_receive_time + 60000))
#else
		if (current_tick > (g_receive_time + 30*60*1000)) //30 minute timeout for debugging
#endif
		{
			LogMessage("Net7 Server seems to have stopped\n");
			LogMessage("Net7SSL Terminating\n");
			break;
		}
	};

    return 0;
}

static volatile HANDLE 	g_ProcessHandle 	= (HANDLE) INVALID_HANDLE_VALUE;
static volatile bool	g_EngineInUse		= FALSE;

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

bool Net7StillRunning()
{
	return (engine_check_process("Net7.exe"));
}

SSL_Connection* GetSSLConnection()
{
	SSL_Connection *c;
	c = g_SSL_Connections->GetNode();

	//is this node active? If so, kill it.
	if (c->IsActive())
	{
		c->KillConnection();
	}

	c->SetGameID(1);

	return c;
}

// This function formats a message and adds it to the message queue
void LogMessage(const char *format, ...)
{
    char buffer[8192];
    char timestr[20];
	time_t rawtime;
	struct tm * timeinfo;

    va_list args;
    va_start(args, format);
    _vsnprintf(buffer, 8192, format, args);
    va_end(args);

	time ( &rawtime );
	timeinfo = localtime ( &rawtime );
	strftime(timestr, 18, "%d/%m/%y %H:%M:%S",timeinfo);

	fprintf(stdout, "%s SSL:%s", timestr, buffer); 
}

void LogMySQLMsg(char *format, ...)
{
    char buffer[8192];
    char timestr[20];
	char LogFile[MAX_PATH];
	time_t rawtime;
	struct tm * timeinfo;

    va_list args;
    va_start(args, format);
    vsprintf(buffer, format, args);
    va_end(args);

	time ( &rawtime );
	timeinfo = localtime ( &rawtime );

	// Add _MySQL to the log file name
	sprintf(LogFile, "%s_MySQL", g_LogFilename);
	
	strftime(timestr, 18, "%d/%m/%y %H:%M:%S",timeinfo);

	fprintf(stdout, "%s SSL:SQL:%s", timestr , buffer);
}

unsigned long GetNet7TickCount()
{
    return (GetTickCount() - g_StartTick);
}

// This is called only for ONE instance of the sector manager
bool RegisterSectorServer(short first_port, short max_sectors)
{
	char buffer[4096];
	SSL_METHOD * ssl_client_method;
	SSL_CTX * ssl_context;
	SSL * ssl;

    SSLeay_add_ssl_algorithms();
	ssl_client_method = SSLv2_client_method();
	SSL_load_error_strings();
	ssl_context = SSL_CTX_new(ssl_client_method);
	if (!ssl_context)
	{
        LogMessage("SSL_CTX_new failed\n");
        return false;
	}

	// Establish a SSL connection to the Authentication Server
	// Create a socket
	SOCKET ssl_socket = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
	if (ssl_socket == INVALID_SOCKET)
    {
        LogMessage("Unable to create outgoing SSL socket\n");
        return false;
    }

    unsigned long ip_address = 0x0100007f;

	struct hostent * host;

	if (strlen(g_InternalIP)==0)
	    host = gethostbyname(g_DomainName);
	else
	    host = gethostbyname(g_InternalIP);

	    if (!host)
	    {
           LogMessage("Unable to resolve IP address for %s\n", g_DomainName);
            return false;
	    }
        ip_address = *((unsigned long *) host->h_addr_list[0]);
    //}

	struct sockaddr_in address;
    memset(&address, 0, sizeof(address));
	address.sin_family = AF_INET;
	address.sin_addr.s_addr = ip_address;
	address.sin_port = htons(ssl_port);

    unsigned char * ip = (unsigned char *) &ip_address;
    LogMessage("Connecting to Authentication Server on %d.%d.%d.%d:%d\n",
        ip[0], ip[1], ip[2], ip[3], ssl_port);
	if (connect(ssl_socket, (struct sockaddr*) &address, sizeof(address)))
	{
        LogMessage("Unable to connect to Authentication Server on port %d\n", ssl_port);
        return false;
	}

    //LogMessage("SSL Connected!\n");

	ssl = SSL_new(ssl_context);
	if (!ssl)
	{
        LogMessage("SSL_new failed\n");
        return false;
	}

	SSL_set_fd(ssl, ssl_socket);

	if (!SSL_connect(ssl))
	{
        LogMessage("SSL_connect failed\n");
        return false;
	}

    // TODO: change this from a hard-coded username something that is set on the command line
    // or a data file.
	snprintf(buffer, 128,
		"GET /sectorserver.cgi?username=VectoR&port=%d&max_sectors=%d&version=%d.%d HTTP/1.1\r\n"
		"User-Agent: AuthLogin\r\n"
        "Host: %s\r\n"
		"Connection: Keep-Alive\r\n"
		"Cache-Control: no-cache\r\n"
		"\r\n",
		first_port,
        max_sectors,
		SECTOR_SERVER_MAJOR_VERSION,
        SECTOR_SERVER_MINOR_VERSION,
        g_DomainName);

    //printf("------\n", buffer);
    //printf("%s", buffer);
    //printf("------\n", buffer);

    //LogMessage("SectorManager calling SSL_write (%d bytes)\n", strlen(buffer) + 1);
	if (SSL_write(ssl, buffer, strlen(buffer) + 1) == -1)
	{
        LogMessage("SSL_write failed\n");
        return false;
	}

    //LogMessage("SectorManager calling SSL_read\n");
	int bytes = SSL_read(ssl, buffer, sizeof(buffer) - 1);
	if (bytes == -1)
	{
        LogMessage("SSL_read failed\n");
		return false;
	}
    buffer[bytes] = 0;

	/* Clean up. */
    //LogMessage("SectorManager calling closesocket\n");
	closesocket(ssl_socket);
    //LogMessage("SectorManager calling SSL_free\n");
	SSL_free(ssl);
    //LogMessage("SectorManager calling SSL_CTX_free\n");
	SSL_CTX_free(ssl_context);

	if (strstr(buffer, "Success=TRUE") == 0)
	{
        LogMessage("Attempt to register the Sector Server failed\n");
        LogMessage("SSL Response:%s\n", buffer);
		return false;
	}
    //else
    //{
    //  LogMessage("Successfully registered the Sector Server!\n");
	//}

	return true;
}

//load in sector names and numbers
void LoadSectorData()
{
    long sector_count = 0;
	char QueryString[256];

	if(!g_MySQL_User || !g_MySQL_Pass) 
	{
		printf("You need to set a mysql user/pass in the net7.cfg\n");
		return;
	}

	sql_connection_c SQL_Conn;

	SQL_Conn.connect( "net7", g_MySQL_Host, g_MySQL_User, g_MySQL_Pass);
	sql_query_c SectorTb( &SQL_Conn );
    sql_result_c result;
    sql_result_c object_result;
	sql_result_c *Sector_result = &result;

	strcpy_s(QueryString, sizeof(QueryString), "SELECT * FROM `sectors`");

    if ( !SectorTb.execute( QueryString ) )
    {
        printf( "MySQL Login error/Database error: (User: %s Pass: %s)\n", g_MySQL_User, g_MySQL_Pass );
        return;
    }
    
    SectorTb.store(Sector_result);
    
    if (!Sector_result->n_rows() || !Sector_result->n_fields()) 
	{
        printf("Error loading rows/fields\n");
        return;
    }
    
    printf("Loading Sectors from SQL (%d)\n", (int)Sector_result->n_rows());

	SectorNames.clear();
    
	sql_row_c SectorSQLData;
	for(int x=0;x<Sector_result->n_rows();x++)
	{
		Sector_result->fetch_row(&SectorSQLData);	// Read in first row
		int sector_id = (int) SectorSQLData["sector_id"];
		char *name = strdup((char*) SectorSQLData["name"]);

		SectorNames[sector_id] = name;
	}

	//now load in the station names
	strcpy_s(QueryString, sizeof(QueryString), "SELECT * FROM `starbases`");

    if ( !SectorTb.execute( QueryString ) )
    {
        printf( "MySQL Login error/Database error: (User: %s Pass: %s)\n", g_MySQL_User, g_MySQL_Pass );
        return;
    }
    
    SectorTb.store(Sector_result);
    
    if (!Sector_result->n_rows() || !Sector_result->n_fields()) 
	{
        printf("Error loading rows/fields\n");
        return;
    }
    
    printf("Loading Starbases from SQL (%d)\n", (int)Sector_result->n_rows());
    
	for(int x=0;x<Sector_result->n_rows();x++)
	{
		Sector_result->fetch_row(&SectorSQLData);	// Read in first row
		int sector_id = (int) SectorSQLData["starbase_sector_id"];
		char *name = strdup((char*) SectorSQLData["name"]);

		SectorNames[sector_id] = name;
	}

	m_SQL_Conn.disconnect();
}

char *GetSectorName(long sector_id)
{
	return SectorNames[sector_id];
}

#else // !WIN32 — Phase J Linux entry point + minimal globals

#include "Net7SSL.h"
#include "SSL_Listener.h"
#include "MailslotManager.h"
#include <net7/SingleInstance.h>

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cstdarg>
#include <ctime>
#include <unistd.h>
#include <signal.h>
#include <arpa/inet.h>

// Phase J IPC: AF_UNIX SOCK_DGRAM replaces the Win32 mailslot pair. The
// server side (server/src/Net7.cpp) binds the inverse mapping. Both sides
// need a writable directory at /run/net7-ipc/ — docker-compose mounts a
// shared named volume there.
const char *g_OutputSlot = "/run/net7-ipc/net7.sock";
const char *g_InputSlot  = "/run/net7-ipc/net7SSL.sock";
const char *g_EventName  = "Net7SSLSlot";

// Globals declared extern in Net7SSL.h. We define just enough here to make
// the Linux build link. WIN32-walled translation units in this directory
// would otherwise own these symbols.
char g_LogFilename[MAX_PATH]   = {0};
char g_InternalIP[MAX_PATH]    = {0};
char g_DomainName[MAX_PATH]    = "local.net-7.org";
char g_MySQL_User[MAX_PATH]    = {0};
char g_MySQL_Pass[MAX_PATH]    = {0};
char g_MySQL_Host[MAX_PATH]    = {0};
char g_Galaxy_Name[MAX_PATH]   = {0};
int  g_DASE                    = 0;
unsigned long g_receive_time   = 0;
long g_PlayerCount             = 0;
unsigned long g_cumulative_mem = 0;
long g_MaxPlayerCount          = 0;

bool g_Debug                   = false;
bool g_ServerShutdown          = false;
bool g_LoggedIn                = false;
bool g_ShuttingDown            = false;

// Manager singletons — Linux build does not instantiate the heavy ones.
class GMemoryHandler;
class ServerManager;
class PlayerManager;
class StringManager;
class ItemBaseManager;
class AccountManager;
class SaveManager;
class SSL_DenyList;
class ConnectionManager;

GMemoryHandler   * g_GlobMemMgr   = nullptr;
ServerManager    * g_ServerMgr    = nullptr;
PlayerManager    * g_PlayerMgr    = nullptr;
StringManager    * g_StringMgr    = nullptr;
ItemBaseManager  * g_ItemBaseMgr  = nullptr;
AccountManager   * g_AccountMgr   = nullptr;
SaveManager      * g_SaveMgr      = nullptr;
SSL_DenyList     * g_SSL_Deny_List = nullptr;
ConnectionManager* g_ConnectionMgr = nullptr;

static unsigned long s_StartTick = 0;

// LogMessage is referenced from WestwoodRSA.cpp and other minimally-ported
// TUs. Match the variadic signature declared in Net7SSL.h.
void LogMessage(const char *format, ...)
{
    char buffer[8192];
    char timestr[20];
    time_t rawtime;
    struct tm * timeinfo;

    va_list args;
    va_start(args, format);
    vsnprintf(buffer, sizeof(buffer), format, args);
    va_end(args);

    time(&rawtime);
    timeinfo = localtime(&rawtime);
    strftime(timestr, sizeof(timestr), "%d/%m/%y %H:%M:%S", timeinfo);

    fprintf(stdout, "%s SSL:%s", timestr, buffer);
    fflush(stdout);
}

// Linux stubs for the Win32 directory helpers prototyped in Net7SSL.h.
unsigned long GetCurrentDirectory(unsigned long size, char *path)
{
    if (!path || size == 0) return 0;
    if (getcwd(path, size) == nullptr) return 0;
    return strlen(path);
}

int SetCurrentDirectory(const char *path)
{
    if (!path) return 0;
    return chdir(path) == 0 ? 1 : 0;
}

bool DeleteFile(const char *filename)
{
    if (!filename) return false;
    return unlink(filename) == 0;
}

unsigned long GetNet7TickCount()
{
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    unsigned long now = (unsigned long)(ts.tv_sec * 1000UL + ts.tv_nsec / 1000000UL);
    return now - s_StartTick;
}

void LockMessageQueue()   {}
void UnlockMessageQueue() {}
void LogDebug(char * /*format*/, ...)    {}
void LogChatMsg(char * /*format*/, ...)  {}
void LogMySQLMsg(char * /*format*/, ...) {}
void DumpBuffer(unsigned char * /*b*/, int /*l*/) {}
void DumpBufferToFile(unsigned char * /*b*/, int /*l*/, char * /*f*/, bool /*r*/) {}
char* GetSectorName(long /*sector_id*/) { return nullptr; }
void LoadSectorData() {}

static volatile sig_atomic_t s_shutdown_requested = 0;
static void handle_signal(int /*sig*/) { s_shutdown_requested = 1; g_ServerShutdown = true; }

int main(int argc, char **argv)
{
    // Line-buffer stdout so docker logs show output promptly.
    setvbuf(stdout, nullptr, _IOLBF, 0);
    setvbuf(stderr, nullptr, _IOLBF, 0);

    struct timespec ts0;
    clock_gettime(CLOCK_MONOTONIC, &ts0);
    s_StartTick = (unsigned long)(ts0.tv_sec * 1000UL + ts0.tv_nsec / 1000000UL);

    signal(SIGINT,  handle_signal);
    signal(SIGTERM, handle_signal);
    signal(SIGPIPE, SIG_IGN);

    // Default bind address: 0.0.0.0 (any). Allow override via env / argv[1].
    const char *bind_addr = "0.0.0.0";
    if (argc > 1) bind_addr = argv[1];
    if (const char *env = getenv("NET7SSL_BIND_ADDR")) bind_addr = env;

    unsigned long ip_address_internal = inet_addr(bind_addr);
    if (ip_address_internal == INADDR_NONE) {
        ip_address_internal = htonl(INADDR_ANY);
    }

    LogMessage("Net7SSL (Linux Phase J) starting — bind %s:%d\n", bind_addr, SSL_PORT);

    // Single-instance guard (flock on /run/enb-emulator/Net7SSL_Instance.pid).
    // Replaces the Win32 CreateMutex(NULL, TRUE, "Net7SSL Instance") guard.
    net7::SingleInstance instance_guard;
    if (!instance_guard.Acquire(SSL_INSTANCE_MUTEX_NAME))
    {
        fprintf(stderr, "Net7SSL: another instance is already running.\n");
        return 1;
    }

    // Stand up the SSL listener. Mirrors the SSL_Listener constructor used
    // by Win32 main_prog() above. The listener spawns its own accept loop
    // thread on Linux (see SSL_Listener.cpp).
    SSL_Listener *listener = new SSL_Listener(ip_address_internal, SSL_PORT);
    (void)listener;

    // Mailslot IPC peer (server <-> login keepalive). Same wiring as Win32
    // main_prog(): every ~10s send "Ping" to the server's recv socket; if
    // we don't hear from the peer for ~60s, declare it dead and exit.
    MailManager *MailMgr = new MailManager();

    LogMessage("Net7SSL listener up; entering main loop\n");

    unsigned long send_check  = GetNet7TickCount() + 60 * 1000;
    g_receive_time            = GetNet7TickCount() + 60 * 1000;

    while (!g_ServerShutdown) {
        usleep(500 * 1000); // 500ms — matches Win32 Sleep(500)
        unsigned long current_tick = GetNet7TickCount();

        MailMgr->CheckMessages();

        if (current_tick > (send_check + 10000)) {
            MailMgr->WriteMessage(const_cast<char *>("Ping"));
            send_check = current_tick;
        }

        if (current_tick > (g_receive_time + 60000)) {
            LogMessage("Net7 Server seems to have stopped\n");
            LogMessage("Net7SSL Terminating\n");
            break;
        }
    }

    delete MailMgr;

    LogMessage("Net7SSL shutting down\n");
    return 0;
}

#endif // WIN32 / Linux Phase J split
