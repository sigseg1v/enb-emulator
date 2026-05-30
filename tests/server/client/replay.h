// tests/client/replay.h
//
// Drives a sequence of captured packets against a live server. The replay
// engine:
//   1. Filters a parsed capture down to packets matching a given peer:port
//      (typically a sector port observed in capture_1).
//   2. For each Client->Server packet, applies the RC4 transmit cipher (if
//      `apply_rc4` is true) and writes it to the socket.
//   3. For each Server->Client packet, reads the expected number of bytes,
//      decrypts with the RC4 receive cipher, and (if a matcher is supplied)
//      checks the opcode/length header for equality.
//
// The replay does NOT byte-compare full server responses — server state
// (timestamps, session ids, redirects) will differ between captures and
// replays. The opcode header check is the meaningful regression signal.

#pragma once

#include <cstdint>
#include <functional>
#include <string>
#include <vector>

#include "capture_parser.h"
#include "tcp_client.h"
#include "westwood/westwood_rc4.h"

namespace enbtest {

struct ReplayOptions {
    bool apply_rc4 = false;            // most post-handshake traffic is RC4-encrypted
    bool verify_response_opcode = true; // check first 4 bytes of response header
    int response_timeout_ms = 2000;
};

struct ReplayStats {
    int packets_sent = 0;
    int packets_received = 0;
    int opcode_mismatches = 0;
    std::string last_error;
};

// Replays `packets` (already filtered to one direction-pair) against
// `client`. Optional `tx`/`rx` provide the RC4 ciphers; pass nullptr to
// skip encryption (e.g. for the plaintext SYN/ACK exchange).
//
// `on_response` (optional) is called for every server->client packet with
// the decoded plaintext, so the caller can perform domain-specific
// assertions.
ReplayStats RunReplay(TcpClient& client, const std::vector<Packet>& packets,
                      const ReplayOptions& opts, westwood::Rc4* tx, westwood::Rc4* rx,
                      const std::function<void(const Packet&,
                                               const std::vector<unsigned char>&)>&
                          on_response = {});

// Convenience: filter packets to those matching `peer_port`.
std::vector<Packet> FilterByPort(const std::vector<Packet>& packets,
                                 uint16_t peer_port);

}  // namespace enbtest
