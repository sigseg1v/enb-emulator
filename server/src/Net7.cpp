// Net7.cpp
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

#include "Net7.h"
#include "ServerManager.h"
#include "UDPConnection.h"
#include "MailslotManager.h"
#include <net7/SingleInstance.h>
//#include "vld.h" // visual leak detector

//DIMA: I don't think these are needed
#define MASTER_INSTANCE_MUTEX_NAME	"Net7 Master Server Instance Mutex"
#define SECTOR_INSTANCE_MUTEX_NAME	"Net7 Sector Server port %d Instance Mutex"

// IPC endpoint paths. AF_UNIX SOCK_DGRAM paths consumed by
// net7ipc::PosixIpc (see common/include/net7/PosixIpc.h). The mailslot
// Win32 transport is gone.
const char *g_InputSlot  = "/run/net7-ipc/net7.sock";
const char *g_OutputSlot = "/run/net7-ipc/net7SSL.sock";
const char *g_EventName  = "Net7SSLSlot";

#pragma comment(lib, "wsock32.lib")
#pragma comment(lib, "libmySQL.lib")
#pragma comment(lib, "libeay32.lib")

//remove Lua for now - giving a lot of build warnings.
#if 0
#pragma comment(lib, "lua.lib")
#pragma comment(lib, "luabind.lib")
#endif

char g_Ticket_User[MAX_PATH];
char g_Ticket_Pass[MAX_PATH];
char g_Ticket_Host[MAX_PATH];
char g_Ticket_DB[MAX_PATH];
char g_MySQL_User[MAX_PATH];
char g_MySQL_Pass[MAX_PATH];
char g_MySQL_Host[MAX_PATH];
char g_Galaxy_Name[MAX_PATH];
int g_DASE = 0;

char g_Beta_Mode[MAX_PATH];
char g_LogFilename[MAX_PATH];
char g_InternalIP[MAX_PATH];
char g_DomainName[MAX_PATH];
unsigned long g_StartTick;
unsigned long g_SSL_receive_time = 0;

long g_Sector_Start = 973;
long g_Max_Space_Sector = 9000; //2210;

bool m_ShuttingDown = false;
bool g_Debug = false;
bool g_ServerShutdown = false; // Terminated the global Server
bool g_ResetContent = false;

#ifdef WIN32
PROCESS_INFORMATION sslpi = {NULL};
#endif

unsigned long g_cumulative_mem = 0;

ServerManager * g_ServerMgr = 0;
GMemoryHandler * g_GlobMemMgr = 0;
PlayerManager * g_PlayerMgr = 0;
StringManager * g_StringMgr = 0;
ItemBaseManager * g_ItemBaseMgr = 0;
AccountManager * g_AccountMgr = 0;
SaveManager	  * g_SaveMgr = 0;
MailManager * g_MailMgr = 0;

void Usage()
{
	printf("Net7 Usage:\n\n");
	printf("to run the main server:\n");
	printf("   Net7 /MASTER /ADDRESS:(ip address)\n\n");
	printf("to run a sector server:\n");
	printf("   Net7 /PORT:3500 /ADDRESS:(ip address) /MAX_SECTORS:(num sectors) /ALTSECTORS\n\n");
}

int main(int argc, char* argv[])
{
    // Let the user know when this was compiled for reference purposes
    printf("Net7: Built on %s, at %s\n\n",__DATE__, __TIME__);
    g_StartTick = Net7TickMs();

    bool standalone = false;
    bool master_server = false;
    bool sector_server = false;

    long port = SECTOR_SERVER_PORT;
    char address[32];
    char *domain = "";
    char *max_sectors_str = new char[4];
	char *server_name;
	char mutex_name[80]="Net7 Standalone Server Instance Mutex";

	//sprintf(max_sectors_str, "10");
	//sprintf(max_sectors_str, "74");
	sprintf_s(max_sectors_str, 4, "300");

	g_Ticket_Host[0] = 0;
	g_Ticket_User[0] = 0;
	g_Ticket_Pass[0] = 0;
	g_Ticket_DB[0] = 0;
	g_MySQL_Host[0] = 0;
	g_MySQL_User[0] = 0;
	g_MySQL_Pass[0] = 0;
	g_Galaxy_Name[0] = 0;
	g_DASE = false;

	srand((unsigned)GetNet7TickCount());

	FILE *f;
	fopen_s(&f, CONFIG_FILE, "r");
    if (f)
    {
        fseek(f, 0, SEEK_END);
        long file_size = ftell(f);
        fseek(f, 0, SEEK_SET);
        char *data = new char[file_size + 1];
		char *next_token;
        if (data)
        {
			char *Info;
			char *VarName;
            long size = fread(data, 1, file_size, f);
            data[size] = 0;
			VarName = strtok_s(data, "=", &next_token);
			Info = strtok_s(NULL, "\n", &next_token);
			do
            {
				if (!strcasecmp(VarName, "domain")) 
                {
					strcpy_s(g_DomainName, sizeof(g_DomainName), Info);
					g_DomainName[sizeof(g_DomainName)-1] = '\0';
				}
				if (!strcasecmp(VarName, "internal_ip")) 
                {
					strcpy_s(g_InternalIP, sizeof(g_InternalIP), Info);
					g_InternalIP[sizeof(g_InternalIP)-1] = '\0';
				}
				if (!strcasecmp(VarName, "mysql_user")) 
                {
					strcpy_s(g_MySQL_User, sizeof(g_MySQL_User), Info);
					g_MySQL_User[sizeof(g_MySQL_User)-1] = '\0';
                }
				if (!strcasecmp(VarName, "mysql_pass")) 
                {
					strcpy_s(g_MySQL_Pass, sizeof(g_MySQL_Pass), Info);
					g_MySQL_Pass[sizeof(g_MySQL_Pass)-1] = '\0';
				}
				if (!strcasecmp(VarName, "mysql_host")) 
                {
					strcpy_s(g_MySQL_Host, sizeof(g_MySQL_Host), Info);
					g_MySQL_Host[sizeof(g_MySQL_Host)-1] = '\0';
				}
				if (!strcasecmp(VarName, "ticket_user")) 
                {
					strcpy_s(g_Ticket_User, sizeof(g_Ticket_User), Info);
					g_Ticket_User[sizeof(g_Ticket_User)-1] = '\0';
                }
				if (!strcasecmp(VarName, "ticket_pass")) 
                {
					strcpy_s(g_Ticket_Pass, sizeof(g_Ticket_Pass), Info);
					g_Ticket_Pass[sizeof(g_Ticket_Pass)-1] = '\0';
				}
				if (!strcasecmp(VarName, "ticket_host")) 
                {
					strcpy_s(g_Ticket_Host, sizeof(g_Ticket_Host), Info);
					g_Ticket_Host[sizeof(g_Ticket_Host)-1] = '\0';
				}
				if (!strcasecmp(VarName, "ticket_db")) 
                {
					strcpy_s(g_Ticket_DB, sizeof(g_Ticket_DB), Info);
					g_Ticket_DB[sizeof(g_Ticket_DB)-1] = '\0';
				}
				if (!strcasecmp(VarName, "galaxy_name")) 
                {
					strcpy_s(g_Galaxy_Name, sizeof(g_Galaxy_Name), Info);
					g_Galaxy_Name[sizeof(g_Galaxy_Name)-1] = '\0';
				}
				if (!strcasecmp(VarName, "use_dase"))
				{
					g_DASE = atoi(Info);
				}
				if (!strcasecmp(VarName, "beta_mode"))
				{
					strcpy_s(g_Beta_Mode, sizeof(g_Beta_Mode), Info);
				}
				VarName = strtok_s(NULL, "=", &next_token);
				Info = strtok_s(NULL, "\n", &next_token);
			} 
            while(Info != NULL);

            delete [] data;
        }
        fclose(f);
    }
    else
    {
		char filedata[128];
        printf("Error opening %s\n", CONFIG_FILE);
		strcpy_s(g_DomainName, sizeof(g_DomainName), "local.net-7.org");
		g_DomainName[sizeof(g_DomainName)-1] = '\0';
		strcpy_s(filedata, sizeof(filedata), "domain=local.net-7.org\nmysql_user=YOURUSERNAME\nmysql_pass=YOURPASS\nmysql_host="
			"localhost:3307\nmysql_db=net7\ngalaxy_name=Andromeda");
		fopen_s(&f, CONFIG_FILE, "w");
		fwrite(filedata,1,strlen(filedata),f);
		fclose(f);
    }

	// if no galaxy name set one!
	if (g_Galaxy_Name[0] == 0)
	{
		strcpy_s(g_Galaxy_Name, sizeof(g_Galaxy_Name), "Andromeda");
		g_Galaxy_Name[sizeof(g_Galaxy_Name)-1] = '\0';
	}

#ifdef SQL_ENABLE
	printf("MySQL: Host: %s, User: %s\n", g_MySQL_Host, g_MySQL_User);
	printf("Ticket: Host: %s, User: %s\n", g_Ticket_Host, g_Ticket_User);
#endif
	// make sure logs directory exists
	if (_access(SERVER_LOGS_PATH,0))
		_mkdir(SERVER_LOGS_PATH);

    // No arguments indicate a standalone server via localhost
    for (int i = 1; i < argc; i++)
    {
	    if ((strncmp(argv[i], "/DOMAIN:", 8) == 0))
	    {
            domain = argv[i] + 8;
        }
        else if ((strncmp(argv[i], "/MASTER", 7) == 0) && !master_server)
        {
            master_server = true;
    		server_name = "Master Server";
		    strcpy_s(mutex_name, sizeof(mutex_name), MASTER_INSTANCE_MUTEX_NAME);
		    sprintf_s(g_LogFilename, sizeof(g_LogFilename), "%sNet7_server", SERVER_LOGS_PATH);
		    LogMessage("Net7 Master Server (Auth:%d, Global:%d, Master:%d)\n",
                SSL_PORT, GLOBAL_SERVER_PORT, MASTER_SERVER_PORT);
        }
        else if ((strncmp(argv[i], "/PORT:", 6) == 0) && !sector_server)
        {
            sector_server = true;
		    port = atoi(argv[i] + 6);
		    sprintf_s(mutex_name, sizeof(mutex_name), SECTOR_INSTANCE_MUTEX_NAME, port);
		    server_name = "Sector Server";
		    sprintf_s(g_LogFilename, sizeof(g_LogFilename), "%ssector_server_port_%d", SERVER_LOGS_PATH, port);
		    LogMessage("Net7 Sector Server (Port %d)\n", port);
        }
        else if ((strncmp(argv[i], "/MAX_SECTORS:", 13) == 0) && sector_server)
        {
		    max_sectors_str = argv[i] + 13;
            g_Max_Space_Sector = 4595;
        }
        else if (strncmp(argv[i], "/ALTSECTORS",11) == 0)
        {
            g_Sector_Start = 1910;
            g_Max_Space_Sector = 4595;
        }
        else if (strncmp(argv[i], "/ALLSECTORS",11) == 0)
        {
            g_Sector_Start = 973;
            g_Max_Space_Sector = 4595;
			strcpy_s(max_sectors_str, 4, "300");
			max_sectors_str[3] = '\0';
            printf("ALL SECTORS flag\n");
        }
        else if (strncmp(argv[i], "/STARTSECTOR:",13) == 0)
        {
            g_Sector_Start = atoi(argv[i]+13);
            printf("Starting at Sector %d\n", g_Sector_Start);
        }
		else if (strncmp(argv[i], "/DEBUG", 6) == 0)
		{
			g_Debug = true;
            printf("DEBUG flag\n");
		}
        else
        {
            printf("Unrecognized switch: '%s'\n", argv[i]);
            Usage();
            return(1);
        }
    }

	printf("Domain set to: %s\n", g_DomainName);


#ifdef WIN32
    // Winsock startup
    WSADATA	wsaData = {NULL};
	WSAStartup(MAKEWORD(2, 2), &wsaData);
#endif

	if (strlen(domain)>0)
	{
		strcpy_s(g_DomainName, sizeof(g_DomainName), domain);
		g_DomainName[sizeof(g_DomainName)-1] = '\0';
	}

	struct hostent * host = gethostbyname(g_DomainName);
	if (!host)
	{
        int err = errno;
        printf("Unable to resolve IP address for %s (error=%d)\n", g_DomainName, err);
        return(1);
    }
    unsigned char *ip = (unsigned char *) host->h_addr;
    sprintf_s(address, sizeof(address), "%d.%d.%d.%d", ip[0], ip[1], ip[2], ip[3]);

    if (!master_server && !sector_server)
    {
		sprintf_s(g_LogFilename, sizeof(g_LogFilename), "%sNet7_server", SERVER_LOGS_PATH);
		LogMessage("Net7 Standalone Server (Auth:%d, Global:%d, Master:%d \n\tMaxSectors: %s Version: %d.%d-%s%s Build %d)\n",
            SSL_PORT, GLOBAL_SERVER_PORT, MASTER_SERVER_PORT, max_sectors_str, UPPER_VER, LOWER_VER, VER_TYPE, 
			SQL_ACCOUNT_STRING,  BUILD_VER);
        standalone = true;
        LogMessage("Net7 IP addr = %s\n", address);
    }

    if (master_server && sector_server)
    {
        printf("Can't combine /MASTER and /PORT switches\n");
		Usage();
		return(1);
	}

	unsigned long ip_address_internal = inet_addr(g_InternalIP);
	unsigned long ip_address = inet_addr(address);

    long max_sectors = atoi(max_sectors_str);

    if ((port < 3500) || (port > 32767))
    {
        printf("Invalid /PORT specified for Sector Server\n");
		return(1);
    }

    if ((max_sectors < 1) || (max_sectors > 300))
    {
        printf("Invalid /MAX_SECTORS specified for Sector Server\n");
		return(1);
    }

    // Single-instance guard. flock on /run/enb-emulator/<name>.pid prevents
    // a second copy of the global/sector server from coming up on the same
    // host. Lock is held for the lifetime of `instance_guard` (i.e. main()'s
    // scope).
    net7::SingleInstance instance_guard;
    if (!instance_guard.Acquire(mutex_name))
    {
        fprintf(stderr, "Another instance of the Net-7 Server is already running (%s)\n",
                mutex_name);
        return(1);
    }

    // Delete the previous log file and start a new one
	//DeleteFile(g_LogFilename);
	{
		ServerManager server_mgr(master_server, ip_address, (short) port, (short) max_sectors, standalone, ip_address_internal);
		server_mgr.SetPlayerMgrGlobalMemoryHandler();

		//MVAS Login UDP connection - needs to be done after global memory manager setup.
		UDP_Connection MVASauth(MVAS_LOGIN_PORT, &server_mgr, CONNECTION_TYPE_MVAS_TO_PROXY);
		server_mgr.SetUDPConnection(&MVASauth);
		MVASauth.SetServerManager(&server_mgr);

		server_mgr.RunServer();
		TerminateNet7SSL();
	} // destructs here

#ifdef WIN32
    // Winsock cleanup
    WSACleanup();
#endif

#ifdef WIN32
	::CloseHandle(instance_mutex);
#endif

    return 0;
}

unsigned long GetNet7TickCount()
{
    return (Net7TickMs() - g_StartTick);
}

#ifdef WIN32
// The Win32 build spawns Net7SSL.exe as a child process and uses a named
// mutex for single-instance enforcement. On Linux Net7SSL runs as a
// separate docker-compose service (or systemd unit) — there is no
// in-process "launch sibling .exe" step, so these become no-ops.
void RelaunchNet7SSL()
{
	TerminateNet7SSL();
	LaunchNet7SSL();
}

void LaunchNet7SSL()
{
	char cmd[MAX_PATH];
	char app_path[MAX_PATH];
	STARTUPINFO si = {0};
	GetCurrentDirectory(MAX_PATH, app_path);

	strcpy_s(cmd, sizeof(cmd), "Net7SSL.exe");
	cmd[sizeof(cmd)-1] = '\0';

	SetCurrentDirectory(app_path);
	if (CreateProcess("Net7SSL.exe", cmd, NULL, NULL, FALSE, CREATE_DEFAULT_ERROR_MODE, NULL, NULL, &si, &sslpi) == 0)
	{
		LogMessage("Net7SSL CreateProcess failed, error %d\n",GetLastError());
	}

	g_SSL_receive_time = 10*60*1000 + GetNet7TickCount(); //give ourselves 10 minutes until SSL is required to return first ping
}

#define SSL_INSTANCE_MUTEX_NAME "Net7SSL Instance"

void TerminateNet7SSL()
{
	//first check to see if Net7SSL is actually running
	char mutex_name[80];

	//check instance of Net7SSL isn't already running
	strcpy(mutex_name, SSL_INSTANCE_MUTEX_NAME);
	HANDLE instance_mutex = ::CreateMutex(NULL, TRUE, mutex_name);

	if (::GetLastError() == ERROR_ALREADY_EXISTS)
	{
		if (sslpi.hThread != INVALID_HANDLE_VALUE)
		{
			try
			{
				CloseHandle(sslpi.hThread);
			}
			catch(...)
			{
				LogMessage("CloseHandle throws exception!");
			}
		}
		if (sslpi.hProcess != INVALID_HANDLE_VALUE)
		{
			if (!TerminateProcess(sslpi.hProcess,1))
				LogMessage("could not terminate net7ssl, error code %d\n",GetLastError());
			try
			{
				CloseHandle(sslpi.hProcess);
			}
			catch(...)
			{
				LogMessage("CloseHandle throws exception!");
			}
		}
	}
	// close the mutex
	::CloseHandle(instance_mutex);
}
#else
void RelaunchNet7SSL() {}
void LaunchNet7SSL()   {}
void TerminateNet7SSL(){}
#endif

// Functions added for Linux port
#ifndef WIN32
unsigned long GetCurrentDirectory(unsigned long size, char *path)
{
    if (getcwd(path, size) == NULL)
    {
        return 0;
    }
    return (strlen(path));
}

int SetCurrentDirectory(const char *path)
{
    if (chdir(path) < 0)
    {
        return 0;
    }
    return 1;
}

bool DeleteFile(const char *file)
{
    return (!remove(file));
}

// Phase M: Sleep() and GetTickCount() were removed. Call sites use
// usleep() and Net7TickMs() (<net7/Ticks.h>) directly now.

#endif
