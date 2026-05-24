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
// Phase M: dropped the Win32 mailslot wall entirely. The server is Linux-
// native and the IPC bus is AF_UNIX SOCK_DGRAM through net7ipc::PosixIpc.
#include "MailslotManager.h"
#include "Net7.h"
#include "../compat/posix_ipc.h"

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
        // Mailslot wire format (Win32): opcode byte at [0], slot at [2],
        // length at [4], payload at [6]. The only message actually sent
        // by the existing codebase is the literal "Ping" keepalive
        // (login -> server) and "Pong" / silence in return. Both sides'
        // HandleMessage() reduces to "update g_receive_time"; we honour
        // that by delegating to the same hook.
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
