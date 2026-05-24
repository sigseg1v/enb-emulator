// tests/client/version_request_test.cpp
//
// Phase K: end-to-end VersionRequest dispatch on the global-server port (3805).
// VersionRequest (opcode 0x0000) is the first opcode the client exchanges after
// the RSA+RC4 handshake on the global TCP socket; the server replies with
// VersionResponse (opcode 0x0001) carrying a 4-byte status (0=OK, 1=outdated,
// 2=newer). This exercises the Linux ProcessGlobalServerOpcode dispatch added
// to proxy/ClientToServer_linux_stubs.cpp.
//
// Env-gated: skips unless NET7_TEST_PROXY_HOST is set. NET7_TEST_GLOBAL_PORT
// overrides the default 3805.

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

constexpr int kWireHeaderSize = 4;
constexpr uint16_t kOpcodeVersionRequest  = 0x0000;
constexpr uint16_t kOpcodeVersionResponse = 0x0001;

// VersionRequest is two big-endian longs (Major, Minor). The Linux handler
// reads them via ntohl, so we send 42/0 in network byte order to get status=0.
constexpr int kVersionPayloadSize = 8;

}  // namespace

TEST(VersionRequest, LiveVersionRequestReturnsStatusOK) {
    const char* host = std::getenv("NET7_TEST_PROXY_HOST");
    if (!host) {
        GTEST_SKIP() << "NET7_TEST_PROXY_HOST not set; skipping live test";
    }
    const char* port_env = std::getenv("NET7_TEST_GLOBAL_PORT");
    uint16_t port = port_env ? static_cast<uint16_t>(std::atoi(port_env)) : 3805;

    enbtest::TcpClient client;
    ASSERT_TRUE(client.Connect(host, port, 5000)) << client.last_error();

    westwood::Rsa rsa;
    enbtest::HandshakeResult result;
    std::string err;
    ASSERT_TRUE(enbtest::RunNet7Handshake(client, rsa,
                                          /*rng_seed=*/0xABCD1234CAFEBABEull,
                                          result, &err))
        << err;

    westwood::Rc4 tx;
    westwood::Rc4 rx;
    tx.PrepareKey(result.rc4_key.data(), 8);
    rx.PrepareKey(result.rc4_key.data(), 8);

    std::vector<unsigned char> frame(kWireHeaderSize + kVersionPayloadSize, 0);
    uint16_t size_field = static_cast<uint16_t>(kVersionPayloadSize + kWireHeaderSize);
    std::memcpy(&frame[0], &size_field, 2);
    std::memcpy(&frame[2], &kOpcodeVersionRequest, 2);
    uint32_t major_be = htonl(42);
    uint32_t minor_be = htonl(0);
    std::memcpy(&frame[kWireHeaderSize + 0], &major_be, 4);
    std::memcpy(&frame[kWireHeaderSize + 4], &minor_be, 4);

    tx.Crypt(frame.data(), static_cast<int>(frame.size()));
    ASSERT_EQ(client.Send(frame.data(), static_cast<int>(frame.size())),
              static_cast<int>(frame.size()));

    unsigned char rsp_header[kWireHeaderSize] = {};
    ASSERT_TRUE(client.RecvExact(rsp_header, kWireHeaderSize, 5000))
        << "no response header received: " << client.last_error();
    rx.Crypt(rsp_header, kWireHeaderSize);

    uint16_t rsp_size, rsp_opcode;
    std::memcpy(&rsp_size, &rsp_header[0], 2);
    std::memcpy(&rsp_opcode, &rsp_header[2], 2);

    EXPECT_EQ(rsp_opcode, kOpcodeVersionResponse)
        << "expected VersionResponse (0x0001), got 0x" << std::hex << rsp_opcode;

    int payload_len = static_cast<int>(rsp_size) - kWireHeaderSize;
    ASSERT_GE(payload_len, 4) << "response payload too small for status long";
    ASSERT_LE(payload_len, 64) << "response payload unreasonably large";

    std::vector<unsigned char> rsp_payload(payload_len, 0);
    ASSERT_TRUE(client.RecvExact(rsp_payload.data(), payload_len, 5000))
        << "short payload read: " << client.last_error();
    rx.Crypt(rsp_payload.data(), payload_len);

    // VersionResponse status is a single int32_t at offset 0, host byte order
    // (SendResponse on Linux memcpy's it raw). status=0 means the 42.0 version
    // we sent was accepted as current.
    int32_t status;
    std::memcpy(&status, &rsp_payload[0], 4);
    EXPECT_EQ(status, 0)
        << "VersionRequest 42.0 should have returned status=0 (OK); got " << status
        << ", payload_len=" << payload_len;
}
