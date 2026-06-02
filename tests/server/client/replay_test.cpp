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
// IMPORTANT -- what this test does and does NOT prove:
//
// This harness completes the Net7 RSA/RC4 handshake against the live proxy
// and then REPLAYS RAW CAPTURED BYTES. The post-handshake capture's only
// content packet is a Master_Join (0x35) carrying the *retail* avatar_id
// from the original session (0xF7642540). That avatar is not logged into
// our server, so:
//
//   * No live MVAS will ever send the 0x2009 confirm for it. The proxy's
//     SendMasterLogin handoff therefore cannot succeed from a byte-replay.
//   * The proxy's only possible reply is its cold-start fallback
//     ServerRedirect, emitted only after kHandoffAttempts (30) x ~5s ~= 150s
//     (ClientToMasterServer::HandleMasterJoin). That retry window is a
//     legitimate production feature -- it covers the ~84s server cold start
//     while every sector UDP port binds -- and must NOT be shortened to suit
//     a test.
//
// Consequence: the captured 0x36 ServerRedirect cannot arrive inside any
// sane test timeout, in EITHER the proxy-only or the full-stack job. And
// even if we waited 150s, the reply would be the *fallback* redirect, not a
// real MVAS dispatch -- so an opcode round-trip would be a false-green, not
// proof that Linux opcode dispatch works.
//
// So this test honestly verifies only what a raw byte-replay CAN verify
// against a live proxy: the handshake completes and the encrypted
// post-handshake send path is accepted. The session-backed redirect
// round-trip is verified instead by the Phase-T xUnit suite
// (tests/integration/CliClient.IntegrationTests), which logs a REAL avatar
// in and drives a real sector handshake. require_responses=false records the
// unreproducible captured response in stats.responses_missing rather than
// failing on it.
TEST(Replay, LivePostHandshakeReplay) {
    const char* host = std::getenv("NET7_TEST_PROXY_HOST");
    if (!host) {
        GTEST_SKIP() << "NET7_TEST_PROXY_HOST not set; skipping live test";
    }
    const char* port_env = std::getenv("NET7_TEST_PROXY_PORT");
    uint16_t port = port_env ? static_cast<uint16_t>(std::atoi(port_env)) : 3801;

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
    // The captured ServerRedirect cannot be reproduced from a stale-avatar
    // byte-replay (see the comment block above): a missing S2C packet is
    // recorded, not fatal. Keep the timeout short so a hung proxy still
    // fails fast on the send path rather than blocking CI.
    opts.require_responses = false;
    // If a reply ever does arrive, check its opcode -- but the replay is not
    // expected to elicit one (stale avatar, no confirm), so this only guards.
    opts.verify_response_opcode = true;
    opts.response_timeout_ms = 2000;

    auto stats = enbtest::RunReplay(client, master, opts, &hs.tx_cipher,
                                    &hs.rx_cipher);

    // What we can deterministically prove: the handshake completed (asserted
    // above) and the encrypted post-handshake send path was accepted.
    EXPECT_GT(stats.packets_sent, 0)
        << "no packets sent -- handshake or send path broken";
    EXPECT_EQ(stats.last_error, "")
        << "replay aborted on the send path: " << stats.last_error;

    // The redirect is expected to be absent (see comment block): a
    // stale-avatar replay gets no MVAS confirm, and the ~150s fallback
    // cannot land inside the timeout. Record it for visibility.
    if (stats.responses_missing > 0) {
        GTEST_LOG_(INFO)
            << stats.responses_missing << " captured response(s) not reproduced "
            << "from byte-replay (expected: stale-avatar Master_Join cannot "
            << "elicit a live ServerRedirect). The session-backed redirect "
            << "round-trip is covered by the Phase-T xUnit suite.";
    }
    // If a response DID arrive, its opcode must match -- but the replay is
    // not expected to elicit one, so this guards rather than asserts presence.
    EXPECT_EQ(stats.opcode_mismatches, 0)
        << stats.opcode_mismatches << " response(s) had unexpected opcode";
}
