// login-server/Net7SSL/MailslotManager.cpp
//
// MailManager implementation for the Net7SSL login server.
//
// The Win32 path mirrors server/src/MailslotManager.cpp's logic (the
// original tada-o snapshot was missing this .cpp on the login side —
// the only definition that survived was HandleMessage() in
// ConnectionManager.cpp). Restored here so a future Win32 build links.
//
// The Linux path uses AF_UNIX SOCK_DGRAM via compat/posix_ipc.{h,cpp}.
// On the login side the read/write paths are mirrored relative to the
// server: login reads from net7SSL.sock, writes to net7.sock.
//
// Wire format (Win32): opcode byte at [0], slot at [2], length at [4],
// payload at [6]. The actual traffic in this codebase is the literal
// "Ping" keepalive (login -> server) and "pong" reply (server -> login);
// both sides' HandleMessage() reduces to "update g_receive_time".

// Net7SSL.h must come first — it pulls in compat/win32_shim.h on Linux,
// which provides HANDLE/LPTSTR typedefs the MailManager class declaration
// depends on.
#include "Net7SSL.h"
#include "MailslotManager.h"

#include <cstring>

#ifdef WIN32
// ---------------------------------------------------------------------------
// Win32 mailslot implementation — restored for header symmetry. Same
// shape as server/src/MailslotManager.cpp.
// ---------------------------------------------------------------------------

bool MailManager::WriteMessage(char *message)
{
    BOOL  fResult;
    DWORD cbWritten;

    if (m_SendSlotInit == false)
    {
        SetUpSendSlot();
    }

    fResult = WriteFile(m_hFile,
        message,
        (DWORD)(lstrlen(message) + 1) * sizeof(TCHAR),
        &cbWritten,
        (LPOVERLAPPED) NULL);

    if (!fResult)
    {
        LogMessage("WriteFile failed with %d.\n", GetLastError());
        return FALSE;
    }
    return TRUE;
}

void MailManager::CheckMessages()
{
    DWORD cbMessage, cMessage, cbRead;
    BOOL fResult;
    OVERLAPPED ov;

    cbMessage = cMessage = cbRead = 0;
    ov.Offset = 0;
    ov.OffsetHigh = 0;
    ov.hEvent = m_hEvent;

    fResult = GetMailslotInfo(m_hSlot,
        (LPDWORD) NULL,
        &cbMessage,
        &cMessage,
        (LPDWORD) NULL);

    if (!fResult) return;
    if (cbMessage == MAILSLOT_NO_MESSAGE) return;

    while (cMessage != 0)
    {
        std::memset(m_Buffer, 0, sizeof(m_Buffer));
        if (cbMessage > sizeof(m_Buffer)) continue;

        fResult = ReadFile(m_hSlot, m_Buffer, cbMessage, &cbRead, &ov);
        if (!fResult) return;

        HandleMessage();

        fResult = GetMailslotInfo(m_hSlot,
            (LPDWORD) NULL,
            &cbMessage,
            &cMessage,
            (LPDWORD) NULL);
        if (!fResult) return;
    }
}

MailManager::MailManager()
{
    m_hSlot = CreateMailslot(g_InputSlot,
        0,
        MAILSLOT_WAIT_FOREVER,
        (LPSECURITY_ATTRIBUTES) NULL);

    if (m_hSlot == INVALID_HANDLE_VALUE)
    {
        LogMessage("CreateMailslot failed with %d\n", GetLastError());
    }

    m_hEvent = CreateEvent(NULL, FALSE, FALSE, g_EventName);
    if (m_hEvent == NULL)
    {
        LogMessage("CreateEvent failed with %d.\n", GetLastError());
    }

    m_SendSlotInit = false;
    m_hFile = 0;
}

void MailManager::ResetMailSystem()
{
    if (m_hFile) CloseHandle(m_hFile);
    if (m_hSlot) CloseHandle(m_hSlot);

    m_hSlot = CreateMailslot(g_InputSlot,
        0,
        MAILSLOT_WAIT_FOREVER,
        (LPSECURITY_ATTRIBUTES) NULL);

    m_SendSlotInit = false;
}

MailManager::~MailManager()
{
    if (m_hFile)  CloseHandle(m_hFile);
    if (m_hSlot)  CloseHandle(m_hSlot);
    if (m_hEvent) CloseHandle(m_hEvent);
}

void MailManager::SetUpSendSlot()
{
    m_hFile = CreateFile(g_OutputSlot,
        GENERIC_WRITE,
        FILE_SHARE_READ,
        (LPSECURITY_ATTRIBUTES) NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        (HANDLE) NULL);

    if (m_hFile == INVALID_HANDLE_VALUE)
    {
        LogMessage("CreateFile failed with %d.\n", GetLastError());
    }
    m_SendSlotInit = true;
}

#else // !WIN32 — Phase J POSIX (AF_UNIX SOCK_DGRAM) implementation
// ---------------------------------------------------------------------------
#include "compat/posix_ipc.h"

#include <new>

namespace {
inline net7ipc::PosixIpc * AsIpc(HANDLE h) {
    return reinterpret_cast<net7ipc::PosixIpc *>(h);
}
}

bool MailManager::WriteMessage(char *message)
{
    auto *ipc = AsIpc(m_hSlot);
    if (!ipc) return false;
    return ipc->WriteMessage(message);
}

void MailManager::CheckMessages()
{
    auto *ipc = AsIpc(m_hSlot);
    if (!ipc) return;

    int got;
    while ((got = ipc->ReadMessage(reinterpret_cast<char *>(m_Buffer),
                                   sizeof(m_Buffer))) > 0)
    {
        HandleMessage();
    }
}

MailManager::MailManager()
    : m_hSlot(0), m_hFile(0), m_hEvent(0), m_SendSlotInit(false)
{
    std::memset(m_Buffer, 0, sizeof(m_Buffer));

    auto *ipc = new (std::nothrow)
        net7ipc::PosixIpc(g_InputSlot, g_OutputSlot);
    if (!ipc || !ipc->valid())
    {
        LogMessage("MailManager: PosixIpc bind('%s') failed; IPC disabled\n",
                   g_InputSlot ? g_InputSlot : "<null>");
        delete ipc;
        return;
    }

    LogMessage("MailManager: AF_UNIX recv=%s send=%s\n",
               ipc->recv_path().c_str(), ipc->send_path().c_str());
    m_hSlot = reinterpret_cast<HANDLE>(ipc);
    m_SendSlotInit = true;
}

void MailManager::ResetMailSystem()
{
    auto *ipc = AsIpc(m_hSlot);
    if (ipc) ipc->Reset();
}

MailManager::~MailManager()
{
    delete AsIpc(m_hSlot);
    m_hSlot = 0;
}

void MailManager::SetUpSendSlot()
{
    // No-op on Linux; PosixIpc lazily opens the send socket.
}

#endif // WIN32

// Portable on both platforms: invoked by CheckMessages() when a datagram
// arrives. Just refreshes the receive-time watchdog so the main loop
// doesn't decide the peer has gone away.
void MailManager::HandleMessage()
{
    g_receive_time = GetNet7TickCount();
}

// The three-argument overload was declared by the header but never
// referenced. Define a no-op so the vtable resolves cleanly on both
// platforms; future opcode dispatch can fill this in.
void MailManager::HandleMessage(short /*opcode*/, short /*slot*/, short /*bytes*/)
{
}
