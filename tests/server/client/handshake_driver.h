// tests/client/handshake_driver.h
//
// Drives the 4-step Westwood RSA+RC4 handshake from the client side over
// a TcpClient. Encodes the wire format documented in
// archive/kyp-snapshot/capturedPackets/capture_1.txt (packets 216-219)
// and reverse-engineered from proxy/Connection.cpp:130-278.
//
// Wire format (everything big-endian):
//   SYN1 (client -> server, 4 bytes):
//     [0]   = 0x00
//     [1]   = 0x14
//     [2-3] = session id (client-chosen)
//
//   ACK1 (server -> client, 86 bytes):
//     [0]      = 0x01
//     [1]      = 0x14
//     [2-3]    = echoed session id
//     [4-7]    = unknown (server cookie?)
//     [8-11]   = unknown
//     [12-15]  = modulus length = 0x00000041 (65)
//     [16]     = modulus MSB = 0x00
//     [17-80]  = 64-byte modulus N
//     [81-84]  = exponent length = 0x00000001
//     [85]     = exponent = 0x23
//
//   SYN2 (client -> server, 84 bytes):
//     [0]      = 0x02
//     [1]      = 0x14
//     [2-3]    = session id
//     [4-7]    = echoed server cookie #1
//     [8-11]   = echoed server cookie #2
//     [12-15]  = 0x00 0x00 0x40 0x00 (unknown / flags)
//     [16-19]  = block length = 0x00000040 (64)
//     [20-83]  = 64-byte RSA-encrypted block:
//                  plaintext[0..55]  = arbitrary padding
//                  plaintext[56..63] = REVERSED 8-byte RC4 session key
//
//   ACK2 (server -> client, 12 bytes):
//     [0]      = 0x03
//     [1]      = 0x14
//     [2-3]    = session id
//     [4-5]    = unknown
//     [6-7]    = CORD port (big-endian uint16)
//     [8-11]   = unknown

#pragma once

#include <array>
#include <cstdint>
#include <string>

#include "tcp_client.h"
#include "westwood/westwood_rc4.h"
#include "westwood/westwood_rsa.h"

namespace enbtest {

struct HandshakeResult {
    uint16_t session_id = 0;
    uint16_t cord_port = 0;
    std::array<unsigned char, 8> rc4_key{};
    westwood::Rc4 rx_cipher;  // decrypts subsequent server -> client traffic
    westwood::Rc4 tx_cipher;  // encrypts subsequent client -> server traffic
};

// Performs the full 4-step Westwood-envelope handshake (matches the
// historical Westwood Online wire protocol seen in
// capturedPackets/capture_1.txt). Used for capture-validation and for the
// loopback self-test where both client and server speak this protocol.
//
// Note: the Net-7 emulator does **not** speak this envelope on TCP 3801 —
// see RunNet7Handshake() below for the live-server variant.
bool RunClientHandshake(TcpClient& client, const westwood::Rsa& rsa,
                        uint16_t session_id, uint64_t rng_seed,
                        HandshakeResult& out, std::string* err = nullptr);

// Performs the Net-7 raw RSA exchange as implemented in
// server/src/Connection.cpp:150 (DoKeyExchange) and the corresponding
// proxy/Connection.cpp Linux port. Wire format:
//
//   server -> client (74 bytes, server initiates immediately on connect):
//     [0-3]    = 0x00 0x00 0x00 0x41    modulus length = 65
//     [4]      = 0x00                   modulus MSB
//     [5-68]   = 64-byte modulus N
//     [69-72]  = 0x00 0x00 0x00 0x01    exponent length = 1
//     [73]     = 0x23                   exponent (35)
//
//   client -> server (4 + 64 = 68 bytes):
//     [0-3]    = 0x00 0x00 0x00 0x40    block length = 64 (network byte order)
//     [4-67]   = 64-byte RSA-encrypted block:
//                  plaintext[0..55]  = arbitrary padding
//                  plaintext[56..63] = REVERSED 8-byte RC4 session key
//                  (server reads it back via rc4_key_buffer[0] = rc4key[0x3f])
//
// After this, both sides have an RC4 session keyed identically and are
// ready for the EnB TCP framed opcode stream.
bool RunNet7Handshake(TcpClient& client, const westwood::Rsa& rsa,
                      uint64_t rng_seed, HandshakeResult& out,
                      std::string* err = nullptr);

}  // namespace enbtest
