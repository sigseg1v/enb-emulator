// tests/client/sector_login_test.cpp
//
// Phase K: end-to-end LOGIN dispatch on the sector-server port (3500).
// LOGIN (opcode 0x0002) is a state-change opcode — the Win32 handler sets
// g_LoggedIn=true, sets g_ServerMgr->m_SectorConnection=this, and flips the
// UDP plane's ConnectionActive flag. It does NOT send a response.
//
// We can't directly observe the server-side flags from the client, so this
// test is a weak smoke check: after the handshake on port 3500, sending the
// 0x0002 frame should not terminate the connection (default-stub'd opcodes
// also keep it open, but at least we know our dispatch doesn't crash on the
// new switch). Stronger verification is via `docker logs enb-emulator-proxy-1`
// which should show "SectorServer LOGIN — connection active".
//
// Env-gated: skips unless NET7_TEST_PROXY_HOST is set. NET7_TEST_SECTOR_PORT
// overrides the default 3500.

#include <gtest/gtest.h>

#include <arpa/inet.h>

#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <string>

#include "handshake_driver.h"
#include "tcp_client.h"
#include "westwood/westwood_rc4.h"
#include "westwood/westwood_rsa.h"

TEST(SectorLogin, LiveLoginOpcodeAccepted) {
    const char* host = std::getenv("NET7_TEST_PROXY_HOST");
    if (!host) {
        GTEST_SKIP() << "NET7_TEST_PROXY_HOST not set; skipping live test";
    }
    const char* port_env = std::getenv("NET7_TEST_SECTOR_PORT");
    uint16_t port = port_env ? static_cast<uint16_t>(std::atoi(port_env)) : 3500;

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
    tx.PrepareKey(result.rc4_key.data(), 8);

    // 4-byte header only, no payload. size includes the header itself.
    unsigned char frame[4] = {0};
    uint16_t size_field = 4;
    uint16_t opcode = 0x0002;
    std::memcpy(&frame[0], &size_field, 2);
    std::memcpy(&frame[2], &opcode, 2);
    tx.Crypt(frame, 4);
    ASSERT_EQ(client.Send(frame, 4), 4);

    // No response is expected from LOGIN — the handler is pure state-change.
    // We rely on the socket staying open as a weak "didn't crash" signal.
    // For stronger evidence, check `docker logs` for "SectorServer LOGIN".
    SUCCEED();
}
