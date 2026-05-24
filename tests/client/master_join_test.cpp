// tests/client/master_join_test.cpp
//
// Phase K: end-to-end opcode-dispatch validation against the live proxy.
// Verifies that after RSA+RC4 handshake on TCP 3801, sending a MasterJoin
// (opcode 0x0035) frame produces a ServerRedirect (opcode 0x0036) response.
//
// This exercises the entire post-handshake pipeline that landed in commit
// 5bd0afd: framed read (EnbTcpHeader → RC4-decrypt → payload read), opcode
// dispatch via m_ServerType, Linux HandleMasterJoin stub, SendResponse
// (build frame + RC4-encrypt + send).
//
// Env-gated: skips unless NET7_TEST_PROXY_HOST is set (default port 3801).

#include <gtest/gtest.h>

#include <arpa/inet.h>

#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "handshake_driver.h"
#include "tcp_client.h"
#include "westwood/westwood_rc4.h"
#include "westwood/westwood_rsa.h"

namespace {

// Mirrors proxy/PacketStructures.h EnbTcpHeader (4 bytes on the wire).
constexpr int kWireHeaderSize = 4;

// MasterJoin struct layout from proxy/PacketStructures.h: 11 longs + ticket[20]
// = 64 bytes packed. We zero-fill and stamp avatar_id_lsb at offset 16 and
// ToSectorID at offset 20 — same fields the Linux HandleMasterJoin reads.
constexpr int kMasterJoinPayloadSize = 64;

// Opcode constants from proxy/Opcodes.h
constexpr uint16_t kOpcodeMasterJoin     = 0x0035;
constexpr uint16_t kOpcodeServerRedirect = 0x0036;

}  // namespace

TEST(MasterJoin, LiveMasterJoinReturnsServerRedirect) {
    const char* host = std::getenv("NET7_TEST_PROXY_HOST");
    if (!host) {
        GTEST_SKIP() << "NET7_TEST_PROXY_HOST not set; skipping live test";
    }
    const char* port_env = std::getenv("NET7_TEST_PROXY_PORT");
    uint16_t port = port_env ? static_cast<uint16_t>(std::atoi(port_env)) : 3801;

    enbtest::TcpClient client;
    ASSERT_TRUE(client.Connect(host, port, 5000)) << client.last_error();

    westwood::Rsa rsa;
    enbtest::HandshakeResult result;
    std::string err;
    ASSERT_TRUE(enbtest::RunNet7Handshake(client, rsa,
                                          /*rng_seed=*/0xC0DEFEEDDEADBEEFull,
                                          result, &err))
        << err;

    // Bring up RC4 streams keyed identically to the proxy's m_CryptIn /
    // m_CryptOut. The session key bytes are what the proxy decoded from the
    // SYN2-equivalent block we sent during the handshake.
    westwood::Rc4 tx;
    westwood::Rc4 rx;
    tx.PrepareKey(result.rc4_key.data(), 8);
    rx.PrepareKey(result.rc4_key.data(), 8);

    // Build a MasterJoin frame: header + 64-byte payload, all encrypted.
    std::vector<unsigned char> frame(kWireHeaderSize + kMasterJoinPayloadSize, 0);

    // Size field. proxy/Connection.cpp Linux SendResponse uses
    // `length + sizeof(long)` (8 on x86_64), Win32 uses 4. We're talking to
    // a Linux proxy, but the dispatch path reads `header.size - sizeof(EnbTcpHeader)`
    // (=4 on both platforms), so the size field for client->server here is
    // payload + sizeof(EnbTcpHeader) — the receive side strips exactly 4.
    uint16_t size_field = static_cast<uint16_t>(kMasterJoinPayloadSize + kWireHeaderSize);
    std::memcpy(&frame[0], &size_field, 2);
    std::memcpy(&frame[2], &kOpcodeMasterJoin, 2);
    // Stamp avatar_id_lsb (offset 16 in MasterJoin, after 4 unknown longs) +
    // ToSectorID (offset 20). Network-byte-order, since the handler runs ntohl.
    uint32_t avatar_id_lsb = htonl(1);
    uint32_t to_sector_id  = htonl(1);   // sol sector — value doesn't matter for the redirect
    std::memcpy(&frame[kWireHeaderSize + 16], &avatar_id_lsb, 4);
    std::memcpy(&frame[kWireHeaderSize + 20], &to_sector_id, 4);

    tx.Crypt(frame.data(), static_cast<int>(frame.size()));
    ASSERT_EQ(client.Send(frame.data(), static_cast<int>(frame.size())),
              static_cast<int>(frame.size()));

    // Read response header (4 bytes), RC4-decrypt, check opcode.
    unsigned char rsp_header[kWireHeaderSize] = {};
    ASSERT_TRUE(client.RecvExact(rsp_header, kWireHeaderSize, 5000))
        << "no response header received: " << client.last_error();
    rx.Crypt(rsp_header, kWireHeaderSize);

    uint16_t rsp_size, rsp_opcode;
    std::memcpy(&rsp_size, &rsp_header[0], 2);
    std::memcpy(&rsp_opcode, &rsp_header[2], 2);

    EXPECT_EQ(rsp_opcode, kOpcodeServerRedirect)
        << "expected ServerRedirect (0x0036), got 0x" << std::hex << rsp_opcode
        << " size=" << std::dec << rsp_size;

    // Drain the payload so the connection close is clean and subsequent
    // tests don't see stale bytes on a reconnect. Size field includes the
    // 4-byte EnbTcpHeader; payload length = rsp_size - 4.
    int payload_len = static_cast<int>(rsp_size) - kWireHeaderSize;
    if (payload_len > 0 && payload_len < 1024) {
        std::vector<unsigned char> rsp_payload(payload_len, 0);
        ASSERT_TRUE(client.RecvExact(rsp_payload.data(), payload_len, 5000))
            << "short payload read: " << client.last_error();
        rx.Crypt(rsp_payload.data(), payload_len);
        // ServerRedirect layout still depends on platform `long` width
        // (PacketStructures.h struct ServerRedirect uses long for
        // sector_id and ip_address — the long → int32_t migration is a
        // separate Phase K item):
        //   Win32 (long=4):    sector_id(4) + ip_address(4) + port(2) = 10 byte payload, port at offset 8
        //   Linux x86_64 (8):  sector_id(8) + ip_address(8) + port(2) = 18 byte payload, port at offset 16
        // Probe both — either is a pass for this test.
        bool found = false;
        for (int probe : {8, 16}) {
            if (probe + 2 > payload_len) continue;
            uint16_t maybe_port;
            std::memcpy(&maybe_port, &rsp_payload[probe], 2);
            if (maybe_port == 3500) {
                found = true;
                break;
            }
        }
        EXPECT_TRUE(found)
            << "ServerRedirect did not contain port=3500 at expected offset; "
               "payload_len=" << payload_len;
    }
}
