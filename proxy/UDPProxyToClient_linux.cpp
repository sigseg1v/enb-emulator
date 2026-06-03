// UDPProxyToClient_linux.cpp
//
// Phase K Wave 6 -- Linux port of the server→client UDP fan-out that lives
// in UDPProxyToClient.cpp on Win32. Splits cleanly into three pieces:
//
//   1. Direct passthrough -- the server has historically sent raw TCP
//      opcodes wrapped in a UDP frame (EnbUdpHeader prepended). Linux
//      ProcessClientOpcode strips the UDP header and hands the opcode
//      straight to the proxy↔client TCP connection via
//      m_SectorConnection->SendResponse(). Mirrors Win32
//      UDPProxyToClient.cpp:31-58.
//
//   2. Packet sequence reassembly (0x2016 / 0x201A) -- the server batches
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
//      file scope). We use SendResponse per opcode instead -- slightly
//      more TCP frames on the wire, but functionally equivalent.
//
//   3. Data-file streaming (0x2010) -- the server can shove a payload
//      directly at the client, opcode embedded at offset 2. On Linux,
//      everything except 0x0097 GALAXY_MAP is forwarded raw via
//      SendResponse; 0x0097 is logged but not acted on because
//      SendDataFileToClient (the path that loads cached GalaxyMap.dat
//      on Win32) is WIN32-walled in ClientToSectorServer.cpp. The
//      Phase K CLI test client doesn't need the galaxy map; if/when a
//      real game client lands the cache path needs the Win32 file ported.
//
// Fabrication opcodes -- KNOWN PARITY GAP (Phase AA Wave 3)
// ---------------------------------------------------------
// A known-good reference proxy EXPANDS the compact server control opcodes
// 0x2012-0x2014 (prospect/tractor/loot) and 0x2018-0x2019 (static/resource
// object create) into full client-facing game packets (0x0b object-to-object
// effect, 0x04 create, 0x1b aux, 0x46 position, plus 0x0f/0x07 follow-up
// effect-removal on a timer) and sends them to the client's game receiver.
// The server emits ONLY the compact form and depends on this expansion.
// The whole client-facing builder family (Connection::QueueObjectLinkedEffect
// / QueueObjectCreate / QueueAuxPacket ...) was removed when the dead Win32
// twins were deleted, leaving only the declarations in Connection.h, so our
// proxy currently fabricates NOTHING -- it consume-and-drops these in
// HandleCustomOpcode. The functional consequence is that mining / tractor /
// loot produce no client-visible beam or object-spawn effect (the server
// still credits cargo). This is being implemented; see HandleCustomOpcode's
// per-opcode comment and plans/27 §3a for the reconstructed spec. NOTE: the
// raw 0x20xx opcode is never forwarded (it would fail the 1..0xFE gate and
// stall the sequence); the only correct fix is to fabricate.
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
// because none of the code below is a copy -- it's a fresh POSIX
// re-implementation against the same UDPClient class declaration in
// UDPClient.h (which retains its original header).
//
// New code is contributed under the project default license
// (CC BY-NC-SA 3.0 -- LICENSES/enb-emulator).

#include "Net7.h"
#include "UDPClient.h"
#include <net7/Opcodes.h>
#include <net7/PacketStructures.h>
#include "PacketMethods.h"
#include "Connection.h"
#include "ServerManager.h"

// unistd is provided via Net7.h on both platforms.
#include <string.h>
#include <stdint.h>
#include <errno.h>
#include <deque>
#include <vector>

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
// Bisection diagnostic: suppress server->client opcode forwarding to
// narrow down which payload triggers the Win32 client crash during
// sector zone-in. Reads two env vars lazily on first call:
//
//   PROXY_S2C_DROP_ALL=1     drop every opcode (still ACKs to server)
//   PROXY_S2C_DROP=0x0025,0x001b,...   drop the listed opcodes only
//
// HandleCustomOpcode runs BEFORE this gate, so 0x2020/0x2021 stage
// confirms still flow back to the server. That keeps the server's
// login state machine advancing even when the client gets nothing.
// ---------------------------------------------------------------------------
static bool s_drop_init = false;
static bool s_drop_all  = false;
static bool s_drop_mask[0x1000];

static void init_drop_filter_once()
{
    if (s_drop_init) return;
    s_drop_init = true;
    memset(s_drop_mask, 0, sizeof(s_drop_mask));

    const char *drop_all = getenv("PROXY_S2C_DROP_ALL");
    if (drop_all && drop_all[0] == '1') {
        s_drop_all = true;
        LogMessage("UDPClient(Linux): bisection: PROXY_S2C_DROP_ALL=1 "
                   "(suppressing every server->client opcode)\n");
        return;
    }

    const char *list = getenv("PROXY_S2C_DROP");
    if (!list || !list[0]) return;

    char buf[512];
    strncpy(buf, list, sizeof(buf) - 1);
    buf[sizeof(buf) - 1] = '\0';

    int dropped = 0;
    for (char *tok = strtok(buf, ","); tok; tok = strtok(NULL, ",")) {
        while (*tok == ' ') tok++;
        unsigned int op = (unsigned int) strtoul(tok, NULL, 0);
        if (op > 0 && op < 0x1000) {
            s_drop_mask[op] = true;
            dropped++;
        }
    }
    if (dropped) {
        LogMessage("UDPClient(Linux): bisection: PROXY_S2C_DROP set "
                   "(%d opcodes suppressed): %s\n", dropped, list);
    }
}

static bool should_drop_s2c(unsigned short opcode)
{
    init_drop_filter_once();
    if (s_drop_all) return true;
    if (opcode < 0x1000 && s_drop_mask[opcode]) return true;
    return false;
}

// ---------------------------------------------------------------------------
// Capture-replay (Phase K Wave 324). Reads a binary replay file produced by
// tools/capture-extract/ and, when the proxy is about to forward a
// server->client opcode, substitutes the payload with the next retail
// payload for that opcode from the capture. The point is to disentangle
// transport bugs from payload bugs in the dock-zone-in client crash hunt:
//   - if retail bytes through OUR transport STILL crash, the bug is
//     in our transport/RC4/framing, not the payload our server computed;
//   - if retail bytes DON'T crash but our payloads do, the diff between
//     the substituted bytes and ours is the bug.
//
// Format (see tools/capture-extract/Program.cs for the writer):
//   magic     "ENBREPLAY"   (9 bytes ASCII)
//   version   1             (u8)
//   meta_len  u32 LE        (ASCII "key=value\n" lines)
//   meta      meta_len bytes
//   nframes   u32 LE
//   frames    nframes x [opcode u16 LE, payload_len u32 LE, payload bytes]
//
// Per-opcode FIFO: opcode 0xZZZZ -> deque of retail payloads in capture
// order. Pop head on each emit. A miss (we emit an opcode the capture
// didn't have) is logged but does not block the send.
//
// Substitution happens AT the proxy's S->C chokepoint (both
// ProcessClientOpcode "direct" path and the inner-bundle path here in
// SendClientPacketSequence). Bytes here are pre-RC4-encryption -- the
// existing m_CryptOut.RC4 layer at Connection.cpp:629/668/684 wraps the
// substituted bytes with the active session key transparently.
//
// Gated on PROXY_S2C_REPLAY=<path>. Off by default; play-local does not
// set it. `just packet-replay <capture>` does.
// ---------------------------------------------------------------------------
static bool s_replay_init = false;
static bool s_replay_on   = false;
static std::deque<std::vector<unsigned char>> s_replay_q[0x1000];

// Identity rewrite (Phase K Wave 325). The retail payloads in the replay
// file reference the retail session's avatar_id (e.g. 0x06EE13DE for "Ace"
// in capture_1). The live session has a different avatar_id assigned by
// our server. Without rewriting, the client receives frames addressed to
// an avatar it doesn't know about and crashes in the avatar-pool lookup.
//
// In the EnB protocol the same 4-byte avatar_id (LE) appears as the first
// payload word of Client_Avatar (0x37), Client_Ship (0x47), AvatarDesc
// (0x61), NameDecal (0xB2), and is embedded into most movement/effect
// payloads. ship_id aliases avatar_id, so a single ID-pair covers both.
//
// Learn s_live_avatar_id either from PROXY_LIVE_AVATAR_ID env var (decimal
// or 0xhex), or lazily on the first s2c substitution where the retail
// payload starts with s_retail_avatar_id (in which case orig[0..3] is the
// live id). After learning, every substituted payload gets every 4-byte
// LE occurrence of s_retail_avatar_id rewritten to s_live_avatar_id.
static uint32_t s_retail_avatar_id = 0;   // parsed from replay metadata
static uint32_t s_live_avatar_id   = 0;   // from env or learned

static void init_replay_once()
{
    if (s_replay_init) return;
    s_replay_init = true;

    const char *path = getenv("PROXY_S2C_REPLAY");
    if (!path || !path[0]) return;

    FILE *f = fopen(path, "rb");
    if (!f) {
        LogMessage("UDPClient(Linux): replay: PROXY_S2C_REPLAY=%s open failed: %s\n",
                   path, strerror(errno));
        return;
    }

    char magic[9];
    unsigned char ver = 0;
    if (fread(magic, 1, 9, f) != 9 || memcmp(magic, "ENBREPLAY", 9) != 0
        || fread(&ver, 1, 1, f) != 1 || ver != 1) {
        LogMessage("UDPClient(Linux): replay: bad magic or version in %s\n", path);
        fclose(f);
        return;
    }

    uint32_t meta_len = 0;
    if (fread(&meta_len, 1, 4, f) != 4 || meta_len > (1u << 20)) {
        LogMessage("UDPClient(Linux): replay: bad meta_len in %s\n", path);
        fclose(f);
        return;
    }
    std::vector<char> meta(meta_len + 1, 0);
    if (meta_len && fread(meta.data(), 1, meta_len, f) != meta_len) {
        fclose(f);
        return;
    }
    meta[meta_len] = '\0';

    uint32_t nframes = 0;
    if (fread(&nframes, 1, 4, f) != 4) {
        fclose(f);
        return;
    }

    unsigned loaded = 0;
    unsigned skipped_oversized = 0;
    for (uint32_t i = 0; i < nframes; i++) {
        uint16_t opcode = 0;
        uint32_t plen = 0;
        if (fread(&opcode, 1, 2, f) != 2) break;
        if (fread(&plen, 1, 4, f) != 4) break;
        std::vector<unsigned char> payload(plen);
        if (plen && fread(payload.data(), 1, plen, f) != plen) break;
        if (opcode >= 0x1000) { skipped_oversized++; continue; }
        s_replay_q[opcode].push_back(std::move(payload));
        loaded++;
    }
    fclose(f);

    s_replay_on = true;
    // The metadata block lives in a single multi-line string; log it
    // verbatim so the operator can confirm which capture is mounted.
    LogMessage("UDPClient(Linux): replay: PROXY_S2C_REPLAY=%s -- loaded %u frames "
               "(%u oversized opcode skipped)\n",
               path, loaded, skipped_oversized);
    LogMessage("UDPClient(Linux): replay: metadata: %s\n", meta.data());

    // Parse "avatar_id=0xNNNNNNNN" out of the metadata block so the
    // rewrite pass knows what 4-byte LE pattern to scrub. The extractor
    // emits avatar_id in 0x-prefixed hex; tolerate decimal too.
    const char *meta_str = meta.data();
    const char *k = strstr(meta_str, "avatar_id=");
    if (k) {
        k += strlen("avatar_id=");
        unsigned long parsed = strtoul(k, nullptr, 0);
        if (parsed && parsed <= 0xFFFFFFFFul) {
            s_retail_avatar_id = (uint32_t) parsed;
            LogMessage("UDPClient(Linux): replay: retail_avatar_id=0x%08x\n",
                       s_retail_avatar_id);
        }
    }

    // Optional explicit override of the live avatar_id. Useful when the
    // operator already knows which avatar will be selected (e.g. seeded
    // via SQL) and wants to skip the lazy-learn step.
    const char *live_env = getenv("PROXY_LIVE_AVATAR_ID");
    if (live_env && live_env[0]) {
        unsigned long live = strtoul(live_env, nullptr, 0);
        if (live && live <= 0xFFFFFFFFul) {
            s_live_avatar_id = (uint32_t) live;
            LogMessage("UDPClient(Linux): replay: live_avatar_id=0x%08x "
                       "(from PROXY_LIVE_AVATAR_ID env)\n",
                       s_live_avatar_id);
        }
    }
}

// Read a u32 LE from buf at offset, bounds-safe.
static uint32_t read_u32le(const unsigned char *buf, size_t len, size_t off)
{
    if (off + 4 > len) return 0;
    return (uint32_t) buf[off]
         | ((uint32_t) buf[off + 1] << 8)
         | ((uint32_t) buf[off + 2] << 16)
         | ((uint32_t) buf[off + 3] << 24);
}

// Scan `payload` for every 4-byte LE occurrence of `from` and overwrite
// each with `to`. Returns the number of replacements. Naive O(n) scan;
// payloads are at most a few KB so allocation-free byte-walk is fine.
static unsigned rewrite_u32le(std::vector<unsigned char> &payload,
                              uint32_t from, uint32_t to)
{
    if (from == 0 || to == 0 || from == to || payload.size() < 4) return 0;
    unsigned char from_b[4] = {
        (unsigned char) (from & 0xFF),
        (unsigned char) ((from >> 8) & 0xFF),
        (unsigned char) ((from >> 16) & 0xFF),
        (unsigned char) ((from >> 24) & 0xFF),
    };
    unsigned char to_b[4] = {
        (unsigned char) (to & 0xFF),
        (unsigned char) ((to >> 8) & 0xFF),
        (unsigned char) ((to >> 16) & 0xFF),
        (unsigned char) ((to >> 24) & 0xFF),
    };
    unsigned hits = 0;
    for (size_t i = 0; i + 4 <= payload.size(); i++) {
        if (payload[i]   == from_b[0]
         && payload[i+1] == from_b[1]
         && payload[i+2] == from_b[2]
         && payload[i+3] == from_b[3]) {
            payload[i]   = to_b[0];
            payload[i+1] = to_b[1];
            payload[i+2] = to_b[2];
            payload[i+3] = to_b[3];
            hits++;
            i += 3; // skip past the rewrite (loop ++ adds the 4th)
        }
    }
    return hits;
}

// Pop the next retail payload for `opcode` if one is queued and replay is
// active. On hit, copies bytes into `out` and emits a one-line diff
// summary (our_len, retail_len, byte-prefix match) so the operator can
// see at-a-glance how close our generated payload was. On miss, logs a
// MISS line and returns false; caller sends original.
static bool replay_substitute_s2c(unsigned short opcode,
                                  const unsigned char *orig, size_t orig_len,
                                  std::vector<unsigned char> &out)
{
    init_replay_once();
    if (!s_replay_on || opcode >= 0x1000) return false;

    if (s_replay_q[opcode].empty()) {
        LogMessage("UDPClient(Linux): replay: MISS op=0x%04x our=%zu "
                   "(no retail payload queued)\n",
                   opcode, orig_len);
        return false;
    }
    out = std::move(s_replay_q[opcode].front());
    s_replay_q[opcode].pop_front();

    // Lazy learn of live avatar_id. Trigger condition: we know the
    // retail_avatar_id (from metadata), the substituted payload starts
    // with it, AND the live orig payload has a non-zero u32 at offset 0
    // (so it's plausibly the live avatar_id at the same slot). This
    // matches the shape of 0x37 Client_Avatar / 0x47 Client_Ship /
    // 0x61 AvatarDescription / 0xB2 NameDecal. We do NOT learn from
    // other opcodes -- if the first emit isn't one of those, learning
    // simply waits for the next compatible opcode; the rewrite is a
    // no-op until learned.
    if (s_live_avatar_id == 0 && s_retail_avatar_id != 0
        && out.size() >= 4 && orig_len >= 4) {
        uint32_t retail_head = read_u32le(out.data(), out.size(), 0);
        uint32_t live_head   = read_u32le(orig, orig_len, 0);
        if (retail_head == s_retail_avatar_id && live_head != 0
            && live_head != s_retail_avatar_id) {
            s_live_avatar_id = live_head;
            LogMessage("UDPClient(Linux): replay: LEARN live_avatar_id="
                       "0x%08x from op=0x%04x\n",
                       s_live_avatar_id, opcode);
        }
    }

    // ID rewrite: every 4-byte LE occurrence of the retail avatar_id in
    // the substituted payload becomes the live avatar_id. No-op until
    // both IDs are known.
    unsigned hits = rewrite_u32le(out, s_retail_avatar_id, s_live_avatar_id);

    size_t prefix = 0;
    size_t n = orig_len < out.size() ? orig_len : out.size();
    while (prefix < n && orig[prefix] == out[prefix]) prefix++;
    LogMessage("UDPClient(Linux): replay: SUB op=0x%04x our=%zu retail=%zu "
               "prefix_match=%zu rewrites=%u\n",
               opcode, orig_len, out.size(), prefix, hits);
    return true;
}

// ---------------------------------------------------------------------------
// Hex-dump helper for server->client opcode payloads. Emits the bytes that
// will hit the proxy->client TCP socket pre-RC4-encryption, in the same
// [length, opcode, payload]-rows format the retail packet captures under
// archive/kyp-snapshot/capturedPackets/ use. That makes byte-diffing our
// emit against a capture trivial (paste, sort, diff).
//
// Gated on its own env var (PROXY_S2C_HEXDUMP=1) -- NOT on the /OPCODES
// verbose flag, because the bisect-drop* recipes also enable /OPCODES and
// we do not want to bury those traces under thousands of hex rows. Only
// `just debug-local` (which sets PROXY_S2C_HEXDUMP=1 explicitly) emits
// the dump; `just play-local` stays quiet; `just bisect-drop*` runs
// continue to emit the bisection DROP/keep summary lines without the
// per-frame payload spam.
//
// `tag` is a short label: "direct" for the ProcessClientOpcode passthrough,
// "inner"  for opcodes walked out of a reassembled packet sequence,
// "datafile" for the 0x2010 DATA_FILE non-galaxy-map path.
// ---------------------------------------------------------------------------
static bool s_hexdump_init = false;
static bool s_hexdump_on   = false;

static bool hexdump_enabled()
{
    if (!s_hexdump_init) {
        s_hexdump_init = true;
        const char *v = getenv("PROXY_S2C_HEXDUMP");
        if (v && v[0] == '1') {
            s_hexdump_on = true;
            LogMessage("UDPClient(Linux): PROXY_S2C_HEXDUMP=1 -- dumping every "
                       "server->client opcode payload (capture-file format)\n");
        }
    }
    return s_hexdump_on;
}

static void dump_s2c_hex(const char *tag, unsigned short opcode,
                         const unsigned char *bytes, size_t len)
{
    if (!hexdump_enabled()) return;

    // Header row mirrors the capture-file [length, opcode] preamble. The
    // wire frame the client sees is sizeof(short)*2 (length+opcode) + len
    // bytes, so we print the same length the client will decode.
    unsigned int frame_len = (unsigned int) len + 4;
    LogMessage("HEX(%s) op=0x%04x len=0x%04x (%u bytes payload, %u on wire)\n",
               tag, opcode, frame_len, (unsigned) len, frame_len);

    char line[128];
    char ascii[17];
    for (size_t off = 0; off < len; off += 16) {
        size_t n = (len - off > 16) ? 16 : (len - off);
        int pos = 0;
        for (size_t i = 0; i < 16; i++) {
            if (i < n) {
                pos += snprintf(line + pos, sizeof(line) - pos,
                                "%02X ", bytes[off + i]);
                unsigned char c = bytes[off + i];
                ascii[i] = (c >= 0x20 && c < 0x7F) ? (char) c : '.';
            } else {
                pos += snprintf(line + pos, sizeof(line) - pos, "   ");
                ascii[i] = ' ';
            }
        }
        ascii[16] = '\0';
        LogMessage("HEX(%s) %04zx  %s %s\n", tag, off, line, ascii);
    }
}

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

    if (should_drop_s2c((unsigned short) opcode)) {
        LogVMessage("UDPClient(Linux): bisection: DROP direct 0x%04x [%d bytes]\n",
                    (unsigned short) opcode, (int) bytes);
    } else if (g_ServerMgr && g_ServerMgr->m_SectorConnection) {
        std::vector<unsigned char> sub;
        bool subbed = replay_substitute_s2c(
            (unsigned short) opcode,
            (const unsigned char *) msg, (size_t) bytes, sub);
        unsigned char *out_buf = subbed ? sub.data() : (unsigned char *) msg;
        size_t         out_len = subbed ? sub.size() : (size_t) bytes;
        dump_s2c_hex("direct", (unsigned short) opcode, out_buf, out_len);
        g_ServerMgr->m_SectorConnection->SendResponse(
            opcode, out_buf, (short) out_len, header->packet_sequence);
    }

    IncommingOpcodePreProcessing(opcode, msg, bytes);
}

// ---------------------------------------------------------------------------
// IncommingOpcodePreProcessing -- connection-state side-effects for the
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
// SendCachedGalaxyMap -- Win32 reads GalaxyMap.dat from disk via
// SendDataFileToClient. That helper lives in ClientToSectorServer.cpp
// (WIN32-walled at file scope) and depends on Connection::SendDataFile,
// which isn't on the Linux side yet. The Phase K CLI test client does
// not request the galaxy map, so this is a logging no-op until a real
// game client lands.
// ---------------------------------------------------------------------------
void UDPClient::SendCachedGalaxyMap()
{
    LogMessage("UDPClient(Linux): SendCachedGalaxyMap requested -- not implemented "
               "on Linux yet (SendDataFileToClient lives in WIN32-walled "
               "ClientToSectorServer.cpp). Resetting packet timer.\n");
    m_PacketDropThisSession = 0;
    m_PacketTimer = 100;
}

// ---------------------------------------------------------------------------
// SendClientDataFile -- opcode 0x2010 DATA_FILE. The payload is a TCP
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
        dump_s2c_hex("datafile", (unsigned short) inner_opcode,
                     (const unsigned char *) msg + 4,
                     (size_t)(inner_length - 4));
        g_ServerMgr->m_SectorConnection->SendResponse(
            inner_opcode, (unsigned char *) msg + 4, inner_length - 4,
            header->packet_sequence);
        break;
    }
}

// ---------------------------------------------------------------------------
// Packet-sequence reassembly internals -- same sentinels as Win32.
// ---------------------------------------------------------------------------
#define PACKET_BLANK         ((char *) 0)
#define PACKET_DONE          ((char *) -1)
#define PACKET_RE_REQUESTED  ((char *) -2)

// ---------------------------------------------------------------------------
// SendPacketSequence -- reliable-delivery reassembly for opcode 0x2016
// (PACKET_SEQUENCE) and 0x201A (PACKET_C_SEQUENCE -- continuation of a
// split packet). Mirrors Win32 UDPProxyToClient.cpp:138-351 line-for-line
// but with usleep instead of Sleep and check_memory() instead of
// _CrtCheckMemory. The Win32 path's _ASSERTE(_CrtCheckMemory()) is
// preserved structurally -- Net7.h defines check_memory() as a Linux
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
        // Packet arrived early or there is a hole -- try to fill the hole.
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
                // Patch the reassembled buffer's EnbUdpHeader.size to the
                // total bytes we've gathered. The first packet's header was
                // memcpy'd verbatim and only reflects the first chunk's
                // size (header_size + sizeof(EnbUdpHeader)). Without this
                // patch SendClientPacketSequence computes
                // bytes = header->size - sizeof(EnbUdpHeader) = ~512 and
                // the inner walk breaks immediately on the first inner
                // opcode whose declared length spans the full reassembled
                // payload (e.g. 0x001B AUX_DATA at 0x320/0x600/0x1073
                // bytes), forwarding garbage to the TCP client and
                // crashing the retail Win32 client mid-load. Win32's
                // UDPProxyToClient.cpp has the same length-vs-bytes guard
                // but no break, so it silently falls through and reads
                // past the claimed bound -- works because the reassembled
                // data IS in the buffer, just unaccounted for. Stamping
                // the correct size here is cleaner than removing the
                // bound check.
                EnbUdpHeader *split_hdr = (EnbUdpHeader *) m_SplitPacketBuffer;
                split_hdr->size = (short)(m_SplitPacketptr - m_SplitPacketBuffer);
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
// HandleCustomOpcode -- the proxy's server->client control dispatcher. It
// runs BEFORE the forward gate and decides, per opcode, whether the proxy
// consumes the frame (return true -> the client never sees it) or lets it
// fall through to be forwarded (return false). This mirrors a known-good
// reference proxy's per-opcode behaviour. What we consume:
//   * 0x1b AUX_DATA -- DROP only when the framed length is < 8 (undersized
//     /malformed); a well-formed aux frame falls through and is forwarded
//     verbatim (the proxy does NOT rewrite aux payloads).
//   * 0x2020 LOGIN_STAGE_S_C -- ACK back to the server with 0x2021.
//   * 0x100A MVAS_TERMINATE_S_C -- set g_ShuttingDown so the sequence
//     walker exits (no launcher-side engine teardown on our side).
//   * 0x2011/0x2012-0x2014/0x2018/0x2019 launcher-side opcodes -- consume
//     and drop (cache/prospect/loot/object-create helpers are not wired
//     here; the reference proxy never forwards them to the client either).
//   * 0x2025/0x202a/0x202b/0x202d/0x202e -- proxy-control / gate-cache band;
//     consume and drop (we do not implement the proxy-side gate cache).
// Everything else returns false and is forwarded subject to the 1..0xFE gate
// in SendClientPacketSequence.
// ---------------------------------------------------------------------------
bool UDPClient::HandleCustomOpcode(short opcode, char *ptr, u8 *tcp_packet,
                                   short &tcp_index, short length)
{
    (void) tcp_packet;
    (void) tcp_index;

    switch (opcode)
    {
    case ENB_OPCODE_001B_AUX_DATA:
        // Aux-data frames carry a variable-length payload describing other
        // ships / mobs / hulks in range. A frame whose framed length (the
        // [length][opcode] header field, which counts the 4-byte header)
        // is under 8 bytes cannot hold even one payload word -- it is a
        // truncated / malformed frame. Clean-room observation of a known-
        // good proxy<->client stream shows the upstream proxy DROPS such
        // undersized aux frames rather than relaying them; a Win32 client
        // that parses a 0-payload aux record walks off the end of its
        // record array and crashes. Consume-and-drop (return true) so the
        // sequence walker advances past the junk frame. A well-formed aux
        // frame (length >= 8) falls through to the default and is forwarded
        // verbatim -- the proxy does NOT rewrite aux payloads.
        if (length < 8) {
            LogMessage("UDPClient(Linux): dropping undersized AUX_DATA frame "
                       "(length 0x%x < 8)\n", (unsigned) length);
            return true;
        }
        return false;
    case ENB_OPCODE_2020_LOGIN_STAGE_S_C:
        HandleStageConfirm(ptr, tcp_packet, tcp_index);
        return true;

    case ENB_OPCODE_100A_MVAS_TERMINATE_S_C:
        LogMessage("UDPClient(Linux): MVAS_TERMINATE_S_C -- setting g_ShuttingDown\n");
        g_ShuttingDown = true;
        // Win32 also enqueues a LOGOFF on the TCP socket and spawns a
        // launcher shutdown thread. The proxy↔client TCP teardown
        // happens naturally as the client drops the connection in
        // response to the server's terminate.
        return true;

    case ENB_OPCODE_2011_GALAXY_MAP_CACHE:
        // Proxy-side galaxy-map cache request. The reference loads its
        // cached GalaxyMap.dat and marks the cache primed; we do not
        // implement the proxy-side cache (galaxy data goes live to the
        // server), so consuming here is the correct no-op for our model.
        // Must return TRUE: returning false would fall through to the
        // forward gate, fail the 1..0xFE range, log "bad opcode", and
        // STALL the whole packet sequence (caller marks PACKET_BLANK,
        // never advances m_CurrentPacketNum). That stall was the failure
        // mode behind the Wave 70 first attempt on 0x0098.
        LogVMessage("UDPClient(Linux): galaxy-map cache request 0x%04x -- consumed\n",
                    (unsigned short) opcode);
        return true;

    case ENB_OPCODE_2012_START_PROSPECT:
        // FABRICATE the prospect / mining beam. The server emits only the
        // compact 0x2012 (5 x int32: prospectorGID, asteroidGID, effectUID,
        // startTick, drainMs -- see server/src/PlayerSkills.cpp
        // Player::MineResource) to every range-list member and relies on each
        // member's proxy to expand it into the client-facing 0x0b
        // OBJECT_TO_OBJECT_EFFECT the game client renders. StartProspecting()
        // builds that 0x0b byte-for-byte the way the server's own
        // Player::SendObjectToObjectEffect / Player::ActivateProspectBeam
        // serialize a timed beam. See plans/27 §3a.
        StartProspecting(ptr, tcp_packet, tcp_index);
        return true;

    case ENB_OPCODE_2013_TRACTOR_ORE:
    case ENB_OPCODE_2014_LOOT_ITEM:
    case ENB_OPCODE_2018_STATIC_OBJECT_CREATE:
    case ENB_OPCODE_2019_RESOURCE_OBJECT_CREATE:
        // KNOWN PARITY GAP (Phase AA -- 0x2012 fabricated above; these remain).
        //
        // These are NOT "consume and silently drop" opcodes. A known-good
        // reference proxy FABRICATES full client-facing game packets from
        // each of these compact server control opcodes and sends them to
        // the client's game receiver, then (for tractor/loot) emits a
        // follow-up effect-removal packet once the pull completes:
        //   0x2013 TRACTOR_ORE / 0x2014 LOOT_ITEM -> 0x04 CREATE (the ore
        //        article object) + 0x0b tractor beam + 0x1b name AUX +
        //        0x46 positional interp + a later 0x07 REMOVE.
        //   0x2018/0x2019 OBJECT_CREATE -> 0x04 CREATE + orientation /
        //        nav / aux follow-ons.
        // The server emits ONLY the compact form and relies on the proxy to
        // expand it (see server PlayerSkills.cpp UseTractorBeam / the loot
        // path). The client-facing builder family was removed when the Win32
        // twins were deleted, leaving only declarations in Connection.h -- so
        // our proxy still produces NOTHING for these. Net effect today: a
        // player tractoring / looting gets the server-side cargo result but
        // NO tractor beam or ore-spawn effect on any client.
        //
        // 0x2013/0x2014 are an ACTIVE regression (the server emits them in
        // normal play). 0x2018/0x2019 are LATENT only (the server's sole
        // emitter, Player::SendObjectFull, is currently never called).
        //
        // Interim behaviour is consume-and-drop (return TRUE): it leaves the
        // tractor missing but keeps the sequence advancing. Forwarding the raw
        // 0x20xx opcode is NOT an option -- it fails the 1..0xFE gate and
        // stalls the sequence. The fix is to fabricate, not to forward.
        // See plans/27 §3a for the full reconstructed spec.
        LogVMessage("UDPClient(Linux): fabrication opcode 0x%04x NOT YET fabricated -- dropped (parity gap)\n",
                    (unsigned short) opcode);
        return true;

    // Gate-cache / proxy-control opcodes in the 0x2025..0x202e band. These
    // are not named in the opcode catalog because they never reach the
    // client's game receiver: clean-room observation of a known-good
    // proxy<->client stream shows the upstream proxy CONSUMES every one of
    // them locally and forwards NOTHING to the client. They drive a proxy-
    // side jump-gate destination cache (an optimisation that lets the proxy
    // answer some gate queries without a server round-trip). We do not
    // implement that cache, so gate queries always go live to the server --
    // the safe, lower-throughput default, and a valid runtime state of the
    // reference proxy (its cache simply stays disabled). The critical point
    // is that these MUST be consumed here: if they fell through to the
    // post-gate forward path they would fail the 1..0xFE opcode gate, be
    // logged as "bad opcode", and STALL the entire packet sequence (the
    // same failure mode the launcher-side group above documents). Consume
    // and drop:
    case 0x2025:   // proxy-control (reference: consume, no client output)
    case 0x202a:   // proxy-control (reference: consume, no client output)
    case 0x202b:   // proxy-control no-op (reference: consume, no client output)
    case 0x202d:   // force gate caching   (no-op while our cache is disabled)
    case 0x202e:   // enable gate caching  (no-op while our cache is disabled)
        LogVMessage("UDPClient(Linux): proxy-control opcode 0x%04x -- silent drop\n",
                    (unsigned short) opcode);
        return true;

    default:
        return false;
    }
}

// ---------------------------------------------------------------------------
// SendClientPacketSequence -- once a full packet (single or reassembled
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
            // Opcode length exceeds remaining packet bytes -- match Win32:
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

        if (!HandleCustomOpcode(opcode, ptr + 4, tcp_packet, tcp_index, length)) {
            LogVMessage("UDPClient(Linux): <SERVER->CLIENT UDP> ----> 0x%04x [0x%x]\n",
                        (unsigned short) opcode, (unsigned) length);
            // Game-opcode gate. The real protocol only ever puts opcodes in
            // 0x01..0xFE on this forward path; the highest game opcode in the
            // catalog is 0x00DD and nothing is defined in 0xFF..0xFFE. Clean-
            // room observation of a known-good proxy<->client stream shows the
            // upstream proxy ACCEPTS exactly 1..0xFE here and treats anything
            // else as a bad opcode (recover + abort the packet). We had been
            // forwarding the whole 1..0xFFE range, which is looser than the
            // protocol and than the reference -- a stray high opcode (e.g. a
            // mis-handled control opcode) would be blindly relayed to the
            // client and crash it. Tighten to 1..0xFE; control opcodes (>=
            // 0x1000) are already consumed by HandleCustomOpcode above and
            // never reach this gate.
            if (opcode > 0x0000 && (unsigned short) opcode <= 0x00FE) {
                IncommingOpcodePreProcessing(opcode, ptr + 4, length - 4);
                if (should_drop_s2c((unsigned short) opcode)) {
                    LogVMessage("UDPClient(Linux): bisection: DROP inner 0x%04x [0x%x]\n",
                                (unsigned short) opcode, (unsigned) length);
                } else {
                    std::vector<unsigned char> sub;
                    bool subbed = replay_substitute_s2c(
                        (unsigned short) opcode,
                        (const unsigned char *) ptr + 4, (size_t)(length - 4),
                        sub);
                    unsigned char *out_buf = subbed
                        ? sub.data() : (unsigned char *) ptr + 4;
                    size_t         out_len = subbed
                        ? sub.size() : (size_t)(length - 4);
                    dump_s2c_hex("inner", (unsigned short) opcode, out_buf, out_len);
                    g_ServerMgr->m_SectorConnection->SendResponse(
                        opcode, out_buf, (short) out_len, header->packet_sequence);
                }
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
// HandleStageConfirm -- server sent 0x2020 LOGIN_STAGE_S_C with a
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

    // Phase K: forward EXACTLY 4 bytes (int32_t stage). sizeof(stage_id)
    // = sizeof(long) is 8 on Linux x86_64, and the server's
    // HandleLoginAckReturn reads exactly that many bytes -- so passing 8
    // here would shift every subsequent opcode in the UDP packet by 4
    // bytes. Same long-vs-int32 wire-size class as the server's
    // SendLoginStageConfirm / HandleLoginAckReturn fixes.
    if (g_ServerMgr && g_ServerMgr->m_UDPConnection) {
        int32_t stage_wire = (int32_t) stage_id;
        g_ServerMgr->m_UDPConnection->ForwardClientOpcode(
            ENB_OPCODE_2021_LOGIN_STAGE_ACK_C_S, sizeof(stage_wire),
            (char *) &stage_wire);
    }
}

// ---------------------------------------------------------------------------
// StartProspecting -- fabricate the prospect / mining beam for opcode 0x2012.
//
// The sector server does NOT send the client-facing mining beam directly. It
// sends the compact 0x2012 START_PROSPECT control packet to every player on
// the prospector's range list (server/src/PlayerSkills.cpp
// Player::MineResource -> SendToRangeList) and relies on each member's proxy
// to expand it into the 0x0b OBJECT_TO_OBJECT_EFFECT the game client actually
// renders. The compact packet is five little-endian int32 fields (AddData<long>
// is pinned to 4 wire bytes on every platform -- server/src/PacketMethods.h):
//
//   [0]  prospectorGID  -- the mining ship's object GameID (beam source)
//   [4]  asteroidGID     -- the targeted resource object    (beam target)
//   [8]  effectUID       -- unique id for this beam instance
//   [12] startTick       -- server GetNet7TickCount() at beam start
//   [16] drainMs         -- how long the beam runs before the ore is tractored
//
// We build the 0x0b body byte-for-byte the way the server's own
// Player::SendObjectToObjectEffect (PlayerConnection.cpp) serializes a beam,
// matching exactly the field set Player::ActivateProspectBeam produces for a
// *timed* beam: effect_time != 0 -> Bitmask 0x07, EffectDescID 0x00BF, with
// EffectID, TimeStamp and a u16 Duration. The Duration field lets the client
// self-expire the effect after drainMs, so no separate REMOVE_EFFECT and no
// per-beam timer thread is needed (and no thread-lifetime hazard across a
// mid-mine disconnect). Connection::SendResponse frames [len][opcode][body]
// and RC4-encrypts exactly as the server's SendOpcode frames an inner packet,
// so the bytes the client decodes are identical to a server-emitted 0x0b.
//
// This expansion is identical for the prospector and for observers: every
// recipient renders a beam from prospectorGID to asteroidGID. The prospector-
// only "Prospect ability activated." text + skill AUX is NOT yet fabricated
// (needs reliable local-avatar GameID resolution -- plans/27 §3a open item).
// ---------------------------------------------------------------------------
void UDPClient::StartProspecting(char *ch_msg, u8 *tcp_packet, short &tcp_index)
{
    (void) tcp_packet;
    (void) tcp_index;

    if (!g_ServerMgr || !g_ServerMgr->m_SectorConnection) return;

    unsigned char *msg = (unsigned char *) ch_msg;
    int in = 0;
    long     prospector_gid = ExtractLong(msg, in);
    long     asteroid_gid   = ExtractLong(msg, in);
    long     effect_uid     = ExtractLong(msg, in);
    uint32_t start_tick     = (uint32_t) ExtractLong(msg, in);
    uint32_t drain_ms       = (uint32_t) ExtractLong(msg, in);

    // 0x0b OBJECT_TO_OBJECT_EFFECT body. Field order/layout mirrors
    // Player::SendObjectToObjectEffect: Bitmask(u16), GameID(int32),
    // TargetID(int32), EffectDescID(u16), Message(NULL -> single 0 byte),
    // then the bit-gated fields EffectID(0x01), TimeStamp(0x02), Duration(0x04).
    unsigned char body[64];
    int idx = 0;
    AddData(body, (short) 0x0007, idx);           // Bitmask: EffectID|TimeStamp|Duration
    AddData(body, (int32_t) prospector_gid, idx); // GameID  (beam source)
    AddData(body, (int32_t) asteroid_gid, idx);   // TargetID (beam target)
    AddData(body, (short) 0x00BF, idx);           // EffectDescID: prospect/mining beam
    AddData(body, (char) 0, idx);                 // Message NULL -> one 0 byte
    AddData(body, (int32_t) effect_uid, idx);     // 0x01 EffectID
    AddData(body, (uint32_t) start_tick, idx);    // 0x02 TimeStamp
    // 0x04 Duration is a u16 (ms). The server truncates identically
    // (ActivateProspectBeam: Duration = short(effect_time)); mirror it so the
    // bytes match. Mines longer than ~65s share the server's wrap quirk.
    AddData(body, (short) (drain_ms & 0xFFFF), idx);

    g_ServerMgr->m_SectorConnection->SendResponse(
        ENB_OPCODE_000B_OBJECT_TO_OBJECT_EFFECT, body, (short) idx);

    LogVMessage("UDPClient(Linux): fabricated prospect beam 0x0b "
                "src=%ld tgt=%ld fx=%ld dur=%ums\n",
                prospector_gid, asteroid_gid, effect_uid, (unsigned) drain_ms);
}

// ---------------------------------------------------------------------------
// SendCommsAlive -- proxy→client keepalive ping over UDP. Win32 invokes
// this from a periodic timer that we don't have on Linux yet; the method
// is provided so any future caller links cleanly.
// ---------------------------------------------------------------------------
void UDPClient::SendCommsAlive()
{
    SendResponse(m_ClientPort, ENB_OPCODE_3005_PLAYER_COMMS_ALIVE, NULL, 0);
}

// ---------------------------------------------------------------------------
// RecordLastHandoff -- used by IncommingOpcodePreProcessing on
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
// KillTCPConnection -- Win32 path tears down the launcher's auth-port
// TCP socket. On Linux there is no per-UDPClient TCP socket to close;
// the proxy↔client TCP socket is owned by Connection (m_SectorConnection)
// and lives until the client drops, the server forces a disconnect, or
// the recv thread fails. No-op.
// ---------------------------------------------------------------------------
void UDPClient::KillTCPConnection()
{
    // intentional no-op on Linux
}
