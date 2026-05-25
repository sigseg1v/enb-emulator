// UDPProxyToClient_linux.cpp
//
// Phase K Wave 6 — Linux port of the server→client UDP fan-out that lives
// in UDPProxyToClient.cpp on Win32. Splits cleanly into three pieces:
//
//   1. Direct passthrough — the server has historically sent raw TCP
//      opcodes wrapped in a UDP frame (EnbUdpHeader prepended). Linux
//      ProcessClientOpcode strips the UDP header and hands the opcode
//      straight to the proxy↔client TCP connection via
//      m_SectorConnection->SendResponse(). Mirrors Win32
//      UDPProxyToClient.cpp:31-58.
//
//   2. Packet sequence reassembly (0x2016 / 0x201A) — the server batches
//      multiple TCP opcodes into a single UDP frame, with sequence numbers
//      and resend on packet drop. SendPacketSequence reassembles in
//      m_CurrentPacketNum order, tracks gaps, requests resends via
//      0x2017 RESEND_PACKET_SEQUENCE, and supports split packets larger
//      than the UDP MTU. Once a packet is in-order, SendClientPacketSequence
//      walks its payload (each inner opcode is [size, opcode, data]) and
//      either dispatches via HandleCustomOpcode or forwards via
//      m_SectorConnection->SendResponse() one opcode at a time.
//
//      Win32 uses Queue*/SendQueuedPacket to batch multiple TCP opcodes
//      into a single TCP frame. None of those helpers exist on Linux
//      (they live in ClientToSectorServer.cpp, which is WIN32-walled at
//      file scope). We use SendResponse per opcode instead — slightly
//      more TCP frames on the wire, but functionally equivalent.
//
//   3. Data-file streaming (0x2010) — the server can shove a payload
//      directly at the client, opcode embedded at offset 2. On Linux,
//      everything except 0x0097 GALAXY_MAP is forwarded raw via
//      SendResponse; 0x0097 is logged but not acted on because
//      SendDataFileToClient (the path that loads cached GalaxyMap.dat
//      on Win32) is WIN32-walled in ClientToSectorServer.cpp. The
//      Phase K CLI test client doesn't need the galaxy map; if/when a
//      real game client lands the cache path needs the Win32 file ported.
//
// Launcher-only opcodes left out
// -------------------------------
// Win32 HandleCustomOpcode also routes 0x2012-0x2014 (prospect/tractor/
// loot), 0x2018-0x2019 (static/resource object create), and 0x2011
// (galaxy map cache request) into Queue* batch builders that depend on
// the entire ClientToSectorServer.cpp Queue* family — all WIN32-walled.
// On Linux these opcodes are stubbed: HandleCustomOpcode returns false
// for them, so SendClientPacketSequence's bottom path forwards the raw
// outer opcode (0x2012/0x2013/etc.) via SendResponse. The Phase K CLI
// test client receives them as opaque payloads, which is the right
// behaviour for a server-side proxy in the absence of the launcher.
//
// 0x100A MVAS_TERMINATE_S_C is also stubbed: the Win32 path calls
// ShutdownClient() and _beginthread(ShutdownThread), both of which
// belong to the launcher (engine_* and ClientStillRunning stubs in
// Net7.h are no-ops on Linux). We set g_ShuttingDown=true so the
// packet-sequence walker can terminate cleanly and log; tearing down
// the actual game client is the launcher's job.
//
// Thread safety
// -------------
// On Linux this file's methods run on the UDPClient RecvThread (master
// plane), which races with Connection::RunRecvThread on the TCP socket.
// Connection::SendResponse (proxy/Connection.cpp:862) now takes
// m_Mutex around m_CryptOut / m_SendBuffer to serialise the two
// producers. See the comment in Connection.cpp.
//
// File-scope globals
// ------------------
// `g_ShuttingDown` and `time_debug` were defined inside WIN32-walled
// translation units (UDPProxyToClient.cpp:15 and
// ClientToSectorServer.cpp:13 respectively). Their references on
// Linux (Connection.cpp:19 extern for g_ShuttingDown) need definitions;
// we provide them here so the symbols resolve cleanly on the Linux
// link. They are unused by the WIN32 build (its own definitions
// continue to apply inside the walls).
//
// LICENSE
// -------
// New file authored for the consolidated preservation fork. No Net-7
// CC BY-NC-SA 3.0 header was carried over from the WIN32 source
// because none of the code below is a copy — it's a fresh POSIX
// re-implementation against the same UDPClient class declaration in
// UDPClient.h (which retains its original header).
//
// New code is contributed under the project default license
// (CC BY-NC-SA 3.0 — LICENSES/enb-emulator).

#ifndef WIN32

#include "Net7.h"
#include "UDPClient.h"
#include <net7/Opcodes.h>
#include <net7/PacketStructures.h>
#include "PacketMethods.h"
#include "Connection.h"
#include "ServerManager.h"

#include <unistd.h>
#include <string.h>
#include <stdint.h>

// Win32 sources gate the broader resend cadence on g_ShuttingDown. The
// definition there lives in UDPProxyToClient.cpp (WIN32-walled at file
// scope), so the Linux link needs its own. Connection.cpp:19 also takes
// an `extern bool g_ShuttingDown;` inside its WIN32 wall, so this
// definition is the only one in the Linux build.
bool g_ShuttingDown = false;

// `time_debug` is a debug throttle for opcode logging. Win32 lives in
// ClientToSectorServer.cpp:13 (WIN32-walled). On Linux we don't actually
// consume it (the Win32 if-block in ProcessClientOpcode just decrements
// it for log scaling), but it's defined here so any future Linux code
// path that wants to wire up the same scaling has a symbol to use.
uint32_t time_debug = 0;

// ---------------------------------------------------------------------------
// Direct server→client passthrough.
//
// The server sends opcodes that should appear on the client's TCP socket
// by wrapping them in EnbUdpHeader and shipping them over UDP. This
// method strips the UDP header and re-emits the opcode on the proxy↔
// client TCP connection. Mirrors Win32 UDPProxyToClient.cpp:31-58.
//
// Note: in the Win32 path the bottom of this method also calls
// IncommingOpcodePreProcessing for connection-state opcodes (LOGOFF,
// SERVER_HANDOFF, START). We preserve that here so the UDPClient state
// machine sees the same transitions.
// ---------------------------------------------------------------------------
void UDPClient::ProcessClientOpcode(char *msg, EnbUdpHeader *header)
{
    short opcode = header->opcode;
    short bytes  = (short)(header->size - sizeof(EnbUdpHeader));

    if (!ConnectionActive()) {
        LogVMessage("UDPClient(Linux): direct opcode 0x%04x while connection inactive\n",
                    (unsigned short) opcode);
    }

    if (g_ServerMgr && g_ServerMgr->m_SectorConnection) {
        g_ServerMgr->m_SectorConnection->SendResponse(
            opcode, (unsigned char *) msg, bytes, header->packet_sequence);
    }

    IncommingOpcodePreProcessing(opcode, msg, bytes);
}

// ---------------------------------------------------------------------------
// IncommingOpcodePreProcessing — connection-state side-effects for the
// three opcodes the proxy intercepts: LOGOFF_CONFIRMATION,
// SERVER_HANDOFF, START. Mirrors Win32 UDPProxyToClient.cpp:61-95.
// ---------------------------------------------------------------------------
void UDPClient::IncommingOpcodePreProcessing(short opcode, char *msg, short bytes,
                                             bool tcp)
{
    switch (opcode)
    {
    case ENB_OPCODE_00BA_LOGOFF_CONFIRMATION:
        LogMessage("UDPClient(Linux): ---> LogOff confirm\n");
        if (g_ServerMgr) {
            if (g_ServerMgr->m_UDPConnection) {
                g_ServerMgr->m_UDPConnection->SetConnectionActive(false);
                g_ServerMgr->m_UDPConnection->SetPlayerID(0);
            }
            if (g_ServerMgr->m_UDPClient) {
                g_ServerMgr->m_UDPClient->SetConnectionActive(false);
                g_ServerMgr->m_UDPClient->SetPlayerID(0);
            }
        }
        break;

    case ENB_OPCODE_003A_SERVER_HANDOFF:
        LogMessage("UDPClient(Linux): ---> Server handoff\n");
        if (g_ServerMgr) {
            if (g_ServerMgr->m_UDPConnection)
                g_ServerMgr->m_UDPConnection->SetConnectionActive(false);
            if (g_ServerMgr->m_UDPClient)
                g_ServerMgr->m_UDPClient->SetConnectionActive(false);
            if (g_ServerMgr->m_UDPConnection)
                g_ServerMgr->m_UDPConnection->RecordLastHandoff(msg, bytes);
        }
        m_Packets.clear();
        m_CurrentPacketNum = -1;
        break;

    case ENB_OPCODE_0005_START:
        if (!tcp) {
            if (g_ServerMgr && g_ServerMgr->m_UDPClient) {
                g_ServerMgr->m_UDPClient->SetLoginComplete(true);
            }
            if (g_ServerMgr && g_ServerMgr->m_UDPConnection) {
                g_ServerMgr->m_UDPConnection->KillTCPConnection();
            }
        }
        break;

    default:
        break;
    }
}

// ---------------------------------------------------------------------------
// SendCachedGalaxyMap — Win32 reads GalaxyMap.dat from disk via
// SendDataFileToClient. That helper lives in ClientToSectorServer.cpp
// (WIN32-walled at file scope) and depends on Connection::SendDataFile,
// which isn't on the Linux side yet. The Phase K CLI test client does
// not request the galaxy map, so this is a logging no-op until a real
// game client lands.
// ---------------------------------------------------------------------------
void UDPClient::SendCachedGalaxyMap()
{
    LogMessage("UDPClient(Linux): SendCachedGalaxyMap requested — not implemented "
               "on Linux yet (SendDataFileToClient lives in WIN32-walled "
               "ClientToSectorServer.cpp). Resetting packet timer.\n");
    m_PacketDropThisSession = 0;
    m_PacketTimer = 100;
}

// ---------------------------------------------------------------------------
// SendClientDataFile — opcode 0x2010 DATA_FILE. The payload is a TCP
// frame (size at offset 0, opcode at offset 2, then data). On Win32 the
// galaxy-map case calls into the on-disk cache; everything else is
// forwarded verbatim. Linux drops the galaxy-map cache call (see above)
// and forwards the rest. Mirrors Win32 UDPProxyToClient.cpp:107-131.
// ---------------------------------------------------------------------------
void UDPClient::SendClientDataFile(char *msg, EnbUdpHeader *header)
{
    m_Resync = true;

    short inner_length = *((short *) &msg[0]);
    short inner_opcode = *((short *) &msg[2]);

    if (!g_ServerMgr || !g_ServerMgr->m_SectorConnection) return;

    switch (inner_opcode)
    {
    case ENB_OPCODE_0097_GALAXY_MAP:
        SendCachedGalaxyMap();
        break;

    default:
        // payload starts at offset 4 (after inner [size, opcode] header)
        g_ServerMgr->m_SectorConnection->SendResponse(
            inner_opcode, (unsigned char *) msg + 4, inner_length - 4,
            header->packet_sequence);
        break;
    }
}

// ---------------------------------------------------------------------------
// Packet-sequence reassembly internals — same sentinels as Win32.
// ---------------------------------------------------------------------------
#define PACKET_BLANK         ((char *) 0)
#define PACKET_DONE          ((char *) -1)
#define PACKET_RE_REQUESTED  ((char *) -2)

// ---------------------------------------------------------------------------
// SendPacketSequence — reliable-delivery reassembly for opcode 0x2016
// (PACKET_SEQUENCE) and 0x201A (PACKET_C_SEQUENCE — continuation of a
// split packet). Mirrors Win32 UDPProxyToClient.cpp:138-351 line-for-line
// but with usleep instead of Sleep and check_memory() instead of
// _CrtCheckMemory. The Win32 path's _ASSERTE(_CrtCheckMemory()) is
// preserved structurally — Net7.h defines check_memory() as a Linux
// no-op so the sites still annotate intent.
// ---------------------------------------------------------------------------
void UDPClient::SendPacketSequence(char *msg, EnbUdpHeader *header, bool continuation)
{
    ReSend resend;
    long header_size = 512;

    if (g_Packet_Opt_requested) {
        header_size = 1400;
    }

    check_memory();
    if (header->packet_sequence == 0) {
        LogVMessage("UDPClient(Linux): packet header num reset\n");
        m_CurrentPacketNum     = 0;
        m_Packets.clear();
        m_PacketDropThisSession = 0;
        m_PacketTimer          = 100;
    }

    if (!m_ConnectionActive) {
        LogVMessage("UDPClient(Linux): drop UDP packet outside login connection\n");
        return;
    }

    LogVMessage("UDPClient(Linux): incoming seq %ld expecting %ld\n",
                (long) header->packet_sequence, (long) m_CurrentPacketNum);

    // Store this packet. Replace any earlier non-DONE / non-REREQ slot to
    // accept retransmits of the same sequence number.
    if (m_Packets[header->packet_sequence] == PACKET_BLANK) {
        char *packet = new char[header->size];
        memcpy(packet, msg, header->size);
        m_Packets[header->packet_sequence] = packet;
    } else {
        if (m_Packets[header->packet_sequence] == PACKET_DONE) {
            LogVMessage("UDPClient(Linux): seq %ld already processed [%ld]\n",
                        (long) header->packet_sequence, (long) m_CurrentPacketNum);
        } else {
            LogVMessage("UDPClient(Linux): seq %ld was re-requested [%ld]\n",
                        (long) header->packet_sequence, (long) m_CurrentPacketNum);
        }
        char *message = m_Packets[header->packet_sequence];
        if (message != PACKET_BLANK && message != PACKET_DONE &&
            message != PACKET_RE_REQUESTED) {
            delete[] message;
        }
        char *packet = new char[header->size];
        memcpy(packet, msg, header->size);
        m_Packets[header->packet_sequence] = packet;
    }

    if (header->packet_sequence > (m_CurrentPacketNum + 30)) {
        LogVMessage("UDPClient(Linux): drop seq %ld (out of range)\n",
                    (long) header->packet_sequence);
        return;
    }

    if (header->packet_sequence > m_CurrentPacketNum) {
        // Packet arrived early or there is a hole — try to fill the hole.
        m_PacketTimeout++;
        if (m_Packets[m_CurrentPacketNum] == PACKET_BLANK) {
            resend.packet_start = m_CurrentPacketNum;
            resend.packet_count = 1;

            if (m_PacketTimeout > 10) {
                m_CurrentPacketNum++;
                m_PacketTimeout = 0;
                LogVMessage("UDPClient(Linux): skipping pesky packet %ld\n",
                            (long) m_CurrentPacketNum - 1);
                return;
            }

            unsigned long tick = GetNet7TickCount();

            LogVMessage("UDPClient(Linux): >> request resend of packet %ld\n",
                        (long) m_CurrentPacketNum);
            m_Packets[m_CurrentPacketNum] = PACKET_RE_REQUESTED;
            if (g_ServerMgr && g_ServerMgr->m_UDPConnection) {
                g_ServerMgr->m_UDPConnection->ForwardClientOpcode(
                    ENB_OPCODE_2017_RESEND_PACKET_SEQUENCE,
                    sizeof(ReSend), (char *) &resend);
            }
            m_PacketResendTimer = tick;
            m_PacketDropThisSession++;
            if (m_PacketDropThisSession > 40)  m_PacketTimer = 500;
            if (m_PacketDropThisSession > 500) {
                LogMessage("UDPClient(Linux): excessive packet loss this session.\n");
                m_PacketTimer = 2000;
            }
            return;
        } else if (m_Packets[m_CurrentPacketNum] == PACKET_RE_REQUESTED) {
            if (m_PacketTimeout > 10) {
                m_CurrentPacketNum++;
                m_PacketTimeout = 0;
                LogVMessage("UDPClient(Linux): skipping pesky packet %ld\n",
                            (long) m_CurrentPacketNum - 1);
                return;
            }
        }
    }

    check_memory();

    // Drain any in-order packets that are now ready.
    while (m_Packets[m_CurrentPacketNum] != PACKET_BLANK &&
           m_Packets[m_CurrentPacketNum] != PACKET_DONE &&
           m_Packets[m_CurrentPacketNum] != PACKET_RE_REQUESTED)
    {
        char *message = m_Packets[m_CurrentPacketNum];
        LogVMessage("UDPClient(Linux): processing packet %ld\n",
                    (long) m_CurrentPacketNum);
        bool packet_pass = true;

        if (m_CurrentPacketNum == 16) {
            // Win32 has Sleep(1) here. Match with a 1ms sleep.
            usleep(1000);
        }

        if (m_SplitPacketLength == 0) {
            // Inspect the first inner opcode to detect a split packet.
            char *ptr = message + sizeof(EnbUdpHeader);
            unsigned short length = *((unsigned short *) &ptr[0]);
            if (length > header_size && length < 20000) {
                m_SplitPacketLength = length - header_size;
                memcpy(m_SplitPacketBuffer, message,
                       header_size + sizeof(EnbUdpHeader));
                m_SplitPacketptr   = m_SplitPacketBuffer +
                                     header_size + sizeof(EnbUdpHeader);
                LogVMessage("UDPClient(Linux): split packet start total=0x%x remaining=%ld\n",
                            (unsigned) length, (long) m_SplitPacketLength);
                m_SplitPacketStart = m_CurrentPacketNum;
            } else {
                if (continuation) {
                    // Continuation opcode mid-sequence: skip and recover.
                    LogMessage("UDPClient(Linux): continuation opcode out of band, skipping\n");
                    packet_pass = true;
                } else {
                    packet_pass = SendClientPacketSequence(message);
                }
            }
        } else {
            EnbUdpHeader *hdr = (EnbUdpHeader *) message;
            size_t chunk_bytes = hdr->size - sizeof(EnbUdpHeader);
            memcpy(m_SplitPacketptr, message + sizeof(EnbUdpHeader), chunk_bytes);
            m_SplitPacketptr   += chunk_bytes;
            m_SplitPacketLength -= (long) chunk_bytes;
            LogVMessage("UDPClient(Linux): split chunk %zu, remaining=%ld\n",
                        chunk_bytes, (long) m_SplitPacketLength);

            if (m_SplitPacketLength <= 0) {
                packet_pass = SendClientPacketSequence((char *) m_SplitPacketBuffer);
                if (m_SplitPacketLength < 0) {
                    LogVMessage("UDPClient(Linux): split packet underflow %ld\n",
                                (long) m_SplitPacketLength);
                }
                m_SplitPacketLength = 0;

                if (!packet_pass) {
                    for (unsigned long i = m_SplitPacketStart;
                         i <= (unsigned long) m_CurrentPacketNum; i++)
                    {
                        if (m_Packets[i] != PACKET_BLANK &&
                            m_Packets[i] != PACKET_DONE &&
                            m_Packets[i] != PACKET_RE_REQUESTED)
                        {
                            delete[] m_Packets[i];
                        }
                        m_Packets[i] = PACKET_RE_REQUESTED;
                    }
                    LogVMessage("UDPClient(Linux): >> request resend %lu..%ld\n",
                                m_SplitPacketStart, (long) m_CurrentPacketNum);
                    resend.packet_start = m_SplitPacketStart;
                    resend.packet_count = m_CurrentPacketNum - m_SplitPacketStart;
                    m_CurrentPacketNum  = m_SplitPacketStart;
                    if (g_ServerMgr && g_ServerMgr->m_UDPConnection) {
                        g_ServerMgr->m_UDPConnection->ForwardClientOpcode(
                            ENB_OPCODE_2017_RESEND_PACKET_SEQUENCE,
                            sizeof(ReSend), (char *) &resend);
                    }
                    return;
                }
            }
        }

        delete[] message;
        m_PacketTimeout = 0;

        if (packet_pass) {
            m_Packets[m_CurrentPacketNum] = PACKET_DONE;
            m_CurrentPacketNum++;
        } else {
            m_Packets[m_CurrentPacketNum] = PACKET_BLANK;
        }
    }

    check_memory();
}

void UDPClient::SendLoginPacketSequence(char *msg, EnbUdpHeader *header)
{
    LogVMessage("UDPClient(Linux): received login packet sequence %ld\n",
                (long) header->packet_sequence);
    SendPacketSequence(msg, header);
}

// ---------------------------------------------------------------------------
// HandleCustomOpcode — Win32 intercepts a small set of UDP-only opcodes
// here. The Linux subset:
//   * 0x2020 LOGIN_STAGE_S_C — ACK back to the server with 0x2021
//   * 0x100A MVAS_TERMINATE_S_C — set g_ShuttingDown so the sequence
//                                  walker exits; no engine teardown
//                                  (launcher-side)
// Everything else (0x2011 galaxy-map cache, 0x2012-0x2014 prospect /
// tractor / loot, 0x2018-0x2019 static / resource object create) returns
// false so the caller forwards the raw outer opcode via SendResponse.
// Those branches all depend on WIN32-walled Queue* helpers in
// ClientToSectorServer.cpp; porting them is a separate Wave.
// ---------------------------------------------------------------------------
bool UDPClient::HandleCustomOpcode(short opcode, char *ptr, u8 *tcp_packet,
                                   short &tcp_index)
{
    (void) tcp_packet;
    (void) tcp_index;

    switch (opcode)
    {
    case ENB_OPCODE_2020_LOGIN_STAGE_S_C:
        HandleStageConfirm(ptr, tcp_packet, tcp_index);
        return true;

    case ENB_OPCODE_100A_MVAS_TERMINATE_S_C:
        LogMessage("UDPClient(Linux): MVAS_TERMINATE_S_C — setting g_ShuttingDown\n");
        g_ShuttingDown = true;
        // Win32 also enqueues a LOGOFF on the TCP socket and spawns a
        // launcher shutdown thread. The proxy↔client TCP teardown
        // happens naturally as the client drops the connection in
        // response to the server's terminate.
        return true;

    case ENB_OPCODE_2011_GALAXY_MAP_CACHE:
    case ENB_OPCODE_2012_START_PROSPECT:
    case ENB_OPCODE_2013_TRACTOR_ORE:
    case ENB_OPCODE_2014_LOOT_ITEM:
    case ENB_OPCODE_2018_STATIC_OBJECT_CREATE:
    case ENB_OPCODE_2019_RESOURCE_OBJECT_CREATE:
        // Launcher-side responsibility; not implemented on the Linux
        // server-side proxy. Returning false lets SendClientPacketSequence
        // forward the raw opcode for clients that handle it themselves
        // (the Phase K CLI test client just records it as opaque bytes).
        LogVMessage("UDPClient(Linux): launcher-side opcode 0x%04x — forwarding raw\n",
                    (unsigned short) opcode);
        return false;

    default:
        return false;
    }
}

// ---------------------------------------------------------------------------
// SendClientPacketSequence — once a full packet (single or reassembled
// split) is in order, walk its inner [size, opcode, data] tuples and
// dispatch each one. Returns false if the packet looks malformed (the
// caller will request a resend of the whole sequence window).
//
// Unlike Win32, we do not batch into m_QueueBuffer + SendQueuedPacket
// (those helpers don't exist on Linux yet). Each opcode lands in its
// own TCP frame via Connection::SendResponse. That is slightly more
// expensive on the wire but functionally equivalent.
// ---------------------------------------------------------------------------
bool UDPClient::SendClientPacketSequence(char *msg)
{
    EnbUdpHeader *header = (EnbUdpHeader *) msg;
    short bytes = (short)(header->size - sizeof(EnbUdpHeader));
    long  index = 0;
    short tcp_index = 0;
    unsigned char *tcp_packet = m_QueueBuffer;
    char *ptr = msg + sizeof(EnbUdpHeader);
    bool terminate = false;

    if (!g_ServerMgr || !g_ServerMgr->m_SectorConnection) return false;

    while (index < bytes && !terminate && !g_ShuttingDown) {
        short length = *((short *) &ptr[0]);
        short opcode = *((short *) &ptr[2]);
        LogVMessage("UDPClient(Linux): --> inner opcode 0x%04x length 0x%x\n",
                    (unsigned short) opcode, (unsigned) length);

        if (length > (bytes - index)) {
            // Opcode length exceeds remaining packet bytes — match Win32:
            // log and break out. Caller treats !terminate + index < bytes
            // as a soft error; we still return true so the packet is marked
            // DONE and the sequence advances.
            LogMessage("UDPClient(Linux): opcode 0x%04x length 0x%x exceeds packet (rem 0x%x)\n",
                       (unsigned short) opcode, (unsigned) length,
                       (unsigned)(bytes - index));
            break;
        }
        if (length < 0) {
            LogMessage("UDPClient(Linux): malformed inner opcode in packet seq %ld\n",
                       (long) header->packet_sequence);
            break;
        }

        if (!HandleCustomOpcode(opcode, ptr + 4, tcp_packet, tcp_index)) {
            LogVMessage("UDPClient(Linux): <SERVER->CLIENT UDP> ----> 0x%04x [0x%x]\n",
                        (unsigned short) opcode, (unsigned) length);
            if (opcode > 0x0000 && opcode < 0x0FFF) {
                IncommingOpcodePreProcessing(opcode, ptr + 4, length - 4);
                g_ServerMgr->m_SectorConnection->SendResponse(
                    opcode, (unsigned char *) ptr + 4, length - 4,
                    header->packet_sequence);
            } else {
                LogMessage("UDPClient(Linux): bad opcode through to proxy: 0x%04x len 0x%x\n",
                           (unsigned short) opcode, (unsigned) length);
                terminate = true;
            }
        }

        ptr   += length;
        index += length;
    }

    return !terminate;
}

// ---------------------------------------------------------------------------
// HandleStageConfirm — server sent 0x2020 LOGIN_STAGE_S_C with a
// 4-byte stage ID; we ACK with 0x2021 LOGIN_STAGE_ACK_C_S so the server
// advances its login state machine. Mirrors Win32
// UDPProxyToClient.cpp:693-704.
// ---------------------------------------------------------------------------
void UDPClient::HandleStageConfirm(char *ch_msg, u8 *tcp_packet, short &tcp_index)
{
    (void) tcp_packet;
    (void) tcp_index;

    int            index = 0;
    unsigned char *msg   = (unsigned char *) ch_msg;
    long           stage_id = ExtractLong(msg, index);

    LogVMessage("UDPClient(Linux): confirm login stage %ld\n", stage_id);

    if (g_ServerMgr && g_ServerMgr->m_UDPConnection) {
        g_ServerMgr->m_UDPConnection->ForwardClientOpcode(
            ENB_OPCODE_2021_LOGIN_STAGE_ACK_C_S, sizeof(stage_id),
            (char *) &stage_id);
    }
}

// ---------------------------------------------------------------------------
// SendCommsAlive — proxy→client keepalive ping over UDP. Win32 invokes
// this from a periodic timer that we don't have on Linux yet; the method
// is provided so any future caller links cleanly.
// ---------------------------------------------------------------------------
void UDPClient::SendCommsAlive()
{
    SendResponse(m_ClientPort, ENB_OPCODE_3005_PLAYER_COMMS_ALIVE, NULL, 0);
}

// ---------------------------------------------------------------------------
// RecordLastHandoff — used by IncommingOpcodePreProcessing on
// SERVER_HANDOFF. Stash the handoff payload so a subsequent reconnect
// has the target sector info. Mirrors Win32 UDPClient.cpp:462-466.
// ---------------------------------------------------------------------------
void UDPClient::RecordLastHandoff(char *msg, short bytes)
{
    memset(&m_Server_handoff, 0, sizeof(m_Server_handoff));
    if (bytes > 0 && msg) {
        size_t n = (size_t) bytes;
        if (n > sizeof(m_Server_handoff)) n = sizeof(m_Server_handoff);
        memcpy(&m_Server_handoff, msg, n);
    }
}

// ---------------------------------------------------------------------------
// KillTCPConnection — Win32 path tears down the launcher's auth-port
// TCP socket. On Linux there is no per-UDPClient TCP socket to close;
// the proxy↔client TCP socket is owned by Connection (m_SectorConnection)
// and lives until the client drops, the server forces a disconnect, or
// the recv thread fails. No-op.
// ---------------------------------------------------------------------------
void UDPClient::KillTCPConnection()
{
    // intentional no-op on Linux
}

#endif  // !WIN32
