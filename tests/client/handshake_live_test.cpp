// tests/client/handshake_live_test.cpp
//
// Live + loopback integration tests for the handshake driver.
//
// Loopback tests run unconditionally: we spin up a thread that plays the
// server side of the handshake against the client driver on a localhost
// socket pair. This proves the wire format is internally consistent.
//
// Live test runs only when NET7_TEST_PROXY_HOST is set in the env. It
// connects to a real Net7Proxy instance and walks the handshake.

#include <gtest/gtest.h>

#include <arpa/inet.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <unistd.h>

#include <atomic>
#include <chrono>
#include <cstdlib>
#include <cstring>
#include <string>
#include <thread>

#include "handshake_driver.h"
#include "tcp_client.h"
#include "westwood/westwood_rsa.h"

namespace {

uint16_t PickEphemeralPort() {
    // bind(0) then read back the chosen port; close, return.
    int s = ::socket(AF_INET, SOCK_STREAM, 0);
    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    addr.sin_port = 0;
    bind(s, reinterpret_cast<sockaddr*>(&addr), sizeof(addr));
    socklen_t len = sizeof(addr);
    getsockname(s, reinterpret_cast<sockaddr*>(&addr), &len);
    uint16_t port = ntohs(addr.sin_port);
    close(s);
    return port;
}

// Plays the server side of the 4-step handshake on `listen_port`. Stores
// the RC4 key it decoded from SYN2 so the test can compare. Returns true
// on success.
bool ServerHandshakeOnce(uint16_t listen_port,
                         std::array<unsigned char, 8>* decoded_key,
                         uint16_t advertised_cord_port,
                         uint16_t expected_session_id) {
    int srv = ::socket(AF_INET, SOCK_STREAM, 0);
    if (srv < 0) return false;
    int one = 1;
    ::setsockopt(srv, SOL_SOCKET, SO_REUSEADDR, &one, sizeof(one));

    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    addr.sin_port = htons(listen_port);
    if (::bind(srv, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) < 0) {
        ::close(srv);
        return false;
    }
    if (::listen(srv, 1) < 0) {
        ::close(srv);
        return false;
    }

    int conn = ::accept(srv, nullptr, nullptr);
    ::close(srv);
    if (conn < 0) return false;

    westwood::Rsa rsa;
    unsigned char modulus[westwood::kRsaBlockSize];
    rsa.GetModulusBytes(modulus);
    unsigned char exponent_byte = rsa.GetPublicExponentByte();

    auto recv_all = [&](unsigned char* buf, int n) {
        int got = 0;
        while (got < n) {
            int r = ::recv(conn, buf + got, n - got, 0);
            if (r <= 0) return false;
            got += r;
        }
        return true;
    };
    auto send_all = [&](const unsigned char* buf, int n) {
        return ::send(conn, buf, n, MSG_NOSIGNAL) == n;
    };

    // SYN1
    unsigned char syn1[4];
    if (!recv_all(syn1, 4) || syn1[0] != 0x00) {
        ::close(conn);
        return false;
    }
    uint16_t sid = static_cast<uint16_t>((syn1[2] << 8) | syn1[3]);
    if (sid != expected_session_id) {
        ::close(conn);
        return false;
    }

    // ACK1
    unsigned char ack1[86]{};
    ack1[0] = 0x01;
    ack1[1] = 0x14;
    ack1[2] = syn1[2];
    ack1[3] = syn1[3];
    ack1[4] = 0xAA; ack1[5] = 0xBB; ack1[6] = 0xCC; ack1[7] = 0xDD;
    ack1[8] = 0x11; ack1[9] = 0x22; ack1[10] = 0x33; ack1[11] = 0x44;
    ack1[12] = 0; ack1[13] = 0; ack1[14] = 0; ack1[15] = 0x41;
    ack1[16] = 0;
    std::memcpy(ack1 + 17, modulus, westwood::kRsaBlockSize);
    ack1[81] = 0; ack1[82] = 0; ack1[83] = 0; ack1[84] = 0x01;
    ack1[85] = exponent_byte;
    if (!send_all(ack1, sizeof(ack1))) {
        ::close(conn);
        return false;
    }

    // SYN2
    unsigned char syn2[84];
    if (!recv_all(syn2, sizeof(syn2)) || syn2[0] != 0x02) {
        ::close(conn);
        return false;
    }
    unsigned char ciphertext[westwood::kRsaBlockSize];
    std::memcpy(ciphertext, syn2 + 20, westwood::kRsaBlockSize);
    unsigned char plaintext[westwood::kRsaBlockSize] = {};
    rsa.DecryptBlock(ciphertext, plaintext);
    // Server reads the reversed key from plaintext[63..56].
    for (int i = 0; i < 8; ++i) {
        (*decoded_key)[i] = plaintext[63 - i];
    }

    // ACK2
    unsigned char ack2[12]{};
    ack2[0] = 0x03;
    ack2[1] = 0x14;
    ack2[2] = syn1[2];
    ack2[3] = syn1[3];
    ack2[4] = 0x18; ack2[5] = 0x99;
    ack2[6] = static_cast<unsigned char>((advertised_cord_port >> 8) & 0xff);
    ack2[7] = static_cast<unsigned char>(advertised_cord_port & 0xff);
    ack2[8] = 0; ack2[9] = 0; ack2[10] = 0x40; ack2[11] = 0;
    if (!send_all(ack2, sizeof(ack2))) {
        ::close(conn);
        return false;
    }
    ::close(conn);
    return true;
}

}  // namespace

// ---------------------------------------------------------------------------
// Loopback: drive both sides of the handshake; assert the key the
// "server" thread decoded matches the one the client picked, and that
// the CORD port the driver returned matches what we advertised.
// ---------------------------------------------------------------------------

TEST(HandshakeDriver, LoopbackEndToEnd) {
    const uint16_t port = PickEphemeralPort();
    const uint16_t session_id = 0x2546;
    const uint16_t cord_port = 0x0D3B;  // 3387, from capture_1
    std::array<unsigned char, 8> server_decoded_key{};
    std::atomic<bool> server_ok{false};

    std::thread server([&]() {
        server_ok = ServerHandshakeOnce(port, &server_decoded_key, cord_port,
                                        session_id);
    });

    // Give the server time to bind/listen.
    std::this_thread::sleep_for(std::chrono::milliseconds(50));

    enbtest::TcpClient client;
    ASSERT_TRUE(client.Connect("127.0.0.1", port, 2000))
        << client.last_error();

    westwood::Rsa rsa;
    enbtest::HandshakeResult result;
    std::string err;
    ASSERT_TRUE(enbtest::RunClientHandshake(client, rsa, session_id,
                                            /*rng_seed=*/0xDEADBEEFCAFEBABEull,
                                            result, &err))
        << err;

    server.join();
    EXPECT_TRUE(server_ok.load());
    EXPECT_EQ(result.session_id, session_id);
    EXPECT_EQ(result.cord_port, cord_port);
    EXPECT_EQ(std::memcmp(result.rc4_key.data(), server_decoded_key.data(), 8), 0)
        << "client RC4 key did not match the one the server decoded";
}

// ---------------------------------------------------------------------------
// Live: only runs when NET7_TEST_PROXY_HOST is set. Default port 3801.
// Used in CI once the Net7Proxy port lands and the server is reachable.
// ---------------------------------------------------------------------------

TEST(HandshakeDriver, LiveServerHandshake) {
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
    ASSERT_TRUE(enbtest::RunClientHandshake(client, rsa, /*session_id=*/0x1234,
                                            /*rng_seed=*/0xA5A5A5A5A5A5A5A5ull,
                                            result, &err))
        << err;

    EXPECT_NE(result.cord_port, 0)
        << "server returned CORD port 0; expected a real sector port";
}
