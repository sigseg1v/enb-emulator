// FreyaPacketTrace.cpp -- see FreyaPacketTrace.h.
//
// Global, lock-guarded ring buffer of the last TRACE_CAP packets crossing the
// proxy<->client boundary. Read-only instrumentation: it copies a few header
// bytes per packet and never alters the bytes on the wire. Built into the one
// proxy source tree (Linux-native and Win32-PE), so it sticks to portable
// std::chrono + the net7 Mutex the rest of the proxy uses.
#include "FreyaPacketTrace.h"

#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstring>

#include "Net7.h"       // LogMessage
#include <net7/Mutex.h> // Mutex

namespace {

struct TraceEntry {
    std::uint64_t seq;
    long long time_ms;
    char dir;
    unsigned short opcode;
    unsigned int length;  // full payload length (may exceed retained bytes)
    unsigned char nbytes; // bytes retained below
    unsigned char bytes[24];
};

const int TRACE_CAP = 256;
TraceEntry g_ring[TRACE_CAP];
long long g_count = 0; // total ever recorded (modulo gives the write slot)
std::uint64_t g_seq = 0;
Mutex g_mtx;

long long now_ms() {
    using namespace std::chrono;
    return duration_cast<milliseconds>(steady_clock::now().time_since_epoch()).count();
}

} // namespace

void FreyaPacketTraceRecord(char dir, unsigned short opcode, const unsigned char* data,
                            std::size_t length) {
    g_mtx.Lock();
    TraceEntry& e = g_ring[g_count % TRACE_CAP];
    e.seq = ++g_seq;
    e.time_ms = now_ms();
    e.dir = dir;
    e.opcode = opcode;
    e.length = (unsigned int)length;
    unsigned char n = (unsigned char)(length < sizeof(e.bytes) ? length : sizeof(e.bytes));
    e.nbytes = n;
    if (data && n)
        memcpy(e.bytes, data, n);
    g_count++;
    g_mtx.Unlock();
}

void FreyaPacketTraceDump(const char* reason) {
    g_mtx.Lock();
    int avail = (g_count < TRACE_CAP) ? (int)g_count : TRACE_CAP;
    LogMessage("==== FreyaPacketTrace: last %d packets (%s) ====\n", avail, reason ? reason : "?");
    long long prev = 0;
    for (int i = 0; i < avail; i++) {
        int idx = (int)((g_count - avail + i) % TRACE_CAP);
        const TraceEntry& e = g_ring[idx];
        char hex[3 * 24 + 1];
        int p = 0;
        for (int b = 0; b < (int)e.nbytes; b++)
            p += snprintf(hex + p, sizeof(hex) - p, "%02X ", e.bytes[b]);
        if (e.nbytes == 0)
            hex[0] = '\0';
        long long dt = prev ? (e.time_ms - prev) : 0;
        prev = e.time_ms;
        LogMessage("[trace] #%llu +%lldms %s op=0x%04X len=%u  %s\n", (unsigned long long)e.seq, dt,
                   (e.dir == 'C') ? "C->S" : "S->C", (unsigned int)e.opcode, e.length, hex);
    }
    LogMessage("==== FreyaPacketTrace end ====\n");
    g_mtx.Unlock();
}
