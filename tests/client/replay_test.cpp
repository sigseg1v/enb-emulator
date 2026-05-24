// tests/client/replay_test.cpp
//
// Offline tests for the replay engine plumbing + an env-gated live
// replay against a real server (skips when NET7_TEST_PROXY_HOST unset).

#include <gtest/gtest.h>

#include <cstdlib>
#include <filesystem>

#include "capture_parser.h"
#include "handshake_driver.h"
#include "replay.h"
#include "tcp_client.h"
#include "westwood/westwood_rsa.h"

namespace {

std::filesystem::path CaptureFile(const char* name) {
    return std::filesystem::path(NET7_TEST_CAPTURES_DIR) / name;
}

}  // namespace

TEST(Replay, FilterByPortIsolatesSectorTraffic) {
    auto all = enbtest::ParseCaptureFile(
        CaptureFile("capture_1_handshake.txt").string());
    // capture_1_handshake.txt is sliced to include the handshake (port 3801)
    // and the first few post-handshake sector packets (port 3387).
    auto sector = enbtest::FilterByPort(all, 3387);
    auto master = enbtest::FilterByPort(all, 3801);

    EXPECT_FALSE(sector.empty());
    EXPECT_FALSE(master.empty());
    for (const auto& p : sector) EXPECT_EQ(p.peer_port, 3387);
    for (const auto& p : master) EXPECT_EQ(p.peer_port, 3801);
}

TEST(Replay, PostHandshakeCaptureContainsMasterJoinOpcode) {
    auto packets = enbtest::ParseCaptureFile(
        CaptureFile("capture_1_post_handshake.txt").string());
    bool saw_master_join = false;
    for (const auto& p : packets) {
        if (p.direction != enbtest::Direction::kClientToServer) continue;
        if (p.bytes.size() < 4) continue;
        // EnB TCP header: little-endian length, little-endian opcode.
        if (p.bytes[2] == 0x35 && p.bytes[3] == 0x00) {
            saw_master_join = true;
            break;
        }
    }
    EXPECT_TRUE(saw_master_join)
        << "post-handshake capture should contain Master_Join (opcode 0x35)";
}

// Live: handshake then replay a few captured packets. Env-gated.
//
// IMPORTANT — what this test does and does NOT prove:
//
// The proxy has a deliberate Phase J option-b fallback in
// ClientToMasterServer::HandleMasterJoin(): when SendMasterLogin to a real
// MVAS times out (no MVAS reachable on UDP/3808), the proxy synthesizes
// its OWN ServerRedirect locally and returns opcode 0x0036 — which is
// exactly the opcode the capture expects as the response to MasterJoin.
// That means a response-opcode round-trip test passes mechanically
// whether or not the UDP plane is working.
//
// To avoid false-green CI, the strict opcode-round-trip assertion is
// gated on NET7_TEST_REAL_MVAS=1, which only the cli-integration-test
// job (full docker-compose stack: mysql + login + proxy + server) sets.
// In the proxy-only integration-test job, the body still exercises the
// full handshake + send path (catches regressions there), but does not
// claim to verify Linux opcode dispatch since it cannot.
TEST(Replay, LivePostHandshakeReplay) {
    const char* host = std::getenv("NET7_TEST_PROXY_HOST");
    if (!host) {
        GTEST_SKIP() << "NET7_TEST_PROXY_HOST not set; skipping live test";
    }
    const char* port_env = std::getenv("NET7_TEST_PROXY_PORT");
    uint16_t port = port_env ? static_cast<uint16_t>(std::atoi(port_env)) : 3801;

    const char* real_mvas_env = std::getenv("NET7_TEST_REAL_MVAS");
    const bool real_mvas = real_mvas_env && std::string(real_mvas_env) == "1";

    enbtest::TcpClient client;
    ASSERT_TRUE(client.Connect(host, port, 5000)) << client.last_error();

    westwood::Rsa rsa;
    enbtest::HandshakeResult hs;
    std::string err;
    ASSERT_TRUE(enbtest::RunNet7Handshake(client, rsa,
                                          /*rng_seed=*/0xA5A5A5A5A5A5A5A5ull,
                                          hs, &err))
        << err;

    auto packets = enbtest::ParseCaptureFile(
        CaptureFile("capture_1_post_handshake.txt").string());
    auto master = enbtest::FilterByPort(packets, 3387);

    enbtest::ReplayOptions opts;
    opts.apply_rc4 = true;
    // Only check the response opcode when a real MVAS is behind the
    // proxy — otherwise the proxy-local ServerRedirect fallback would
    // fake a pass. See the comment block above this function.
    opts.verify_response_opcode = real_mvas;
    opts.response_timeout_ms = 5000;

    auto stats = enbtest::RunReplay(client, master, opts, &hs.tx_cipher,
                                    &hs.rx_cipher);
    EXPECT_GT(stats.packets_sent, 0);
    EXPECT_GT(stats.packets_received, 0)
        << "no server responses received — handshake or proxy reply path broken";
    EXPECT_EQ(stats.last_error, "")
        << "replay aborted: " << stats.last_error;
    if (real_mvas) {
        EXPECT_EQ(stats.opcode_mismatches, 0)
            << stats.opcode_mismatches << " response(s) had unexpected opcode";
    } else {
        GTEST_LOG_(WARNING)
            << "NET7_TEST_REAL_MVAS!=1: response-opcode round-trip NOT verified. "
            << "Proxy may have served a fallback ServerRedirect instead of a "
            << "real MVAS response. Set NET7_TEST_REAL_MVAS=1 against the full "
            << "docker-compose stack to actually validate opcode dispatch.";
    }
}
