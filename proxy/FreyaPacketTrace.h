// FreyaPacketTrace.h
//
// FreyaPacketTrace: a small global ring buffer of the most recent packets
// crossing the proxy<->client boundary, in BOTH directions. On a client
// connection drop the proxy dumps it to the log, so an otherwise blind
// "client randomly closed" / "client froze then dropped" report arrives with
// the exact packet lead-up to the disconnect. The proxy is single-client, so
// one global ring is correct. The record side is lock-guarded -- the TCP recv
// thread and the UDP recv thread both feed it (see Connection::SendResponse).
#pragma once

#include <cstddef>

// Record one packet. dir: 'C' = FROM the client (client->server), 'S' = TO the
// client (server->client). data/length is the opcode PAYLOAD (the opcode is
// passed separately); only the first few bytes are retained.
void FreyaPacketTraceRecord(char dir, unsigned short opcode,
                            const unsigned char* data, std::size_t length);

// Dump the retained packets (oldest first) to the proxy log.
void FreyaPacketTraceDump(const char* reason);
