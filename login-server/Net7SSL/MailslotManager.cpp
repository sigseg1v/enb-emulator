// login-server/Net7SSL/MailslotManager.cpp
//
// Phase M: server-native build is Linux-only. The Win32 mailslot
// transport was replaced in Phase J with AF_UNIX SOCK_DGRAM via
// compat/posix_ipc.{h,cpp}; the Win32 implementation that used to mirror
// this was dead code and is now deleted.
//
// On the login side the read/write paths are mirrored relative to the
// server: login reads from net7SSL.sock, writes to net7.sock.
//
// Wire format (legacy Win32): opcode byte at [0], slot at [2], length at
// [4], payload at [6]. The actual traffic in this codebase is the
// literal "Ping" keepalive (login -> server) and "pong" reply (server ->
// login); both sides' HandleMessage() reduces to "update g_receive_time".

#include "Net7SSL.h"
#include "MailslotManager.h"
#include <net7/PosixIpc.h>

#include <cstring>
#include <new>

bool MailManager::WriteMessage(char *message)
{
    if (!m_Ipc) return false;
    return m_Ipc->WriteMessage(message);
}

void MailManager::CheckMessages()
{
    if (!m_Ipc) return;

    int got;
    while ((got = m_Ipc->ReadMessage(reinterpret_cast<char *>(m_Buffer),
                                     sizeof(m_Buffer))) > 0)
    {
        HandleMessage();
    }
}

MailManager::MailManager()
    : m_Ipc(nullptr)
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
    m_Ipc = ipc;
}

void MailManager::ResetMailSystem()
{
    if (m_Ipc) m_Ipc->Reset();
}

MailManager::~MailManager()
{
    delete m_Ipc;
    m_Ipc = nullptr;
}

void MailManager::HandleMessage()
{
    g_receive_time = GetNet7TickCount();
}

void MailManager::HandleMessage(short /*opcode*/, short /*slot*/, short /*bytes*/)
{
}
