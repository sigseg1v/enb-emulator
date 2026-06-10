// tests/client/handshake_test.cpp
//
// Offline verification that the standalone Westwood RSA + RC4 + capture
// parser plumbing matches what the real client/server exchange on
// TCP 3801. These tests do not touch a live socket; they only validate
// that our reimplementation is wire-compatible with a recorded session.
//
// Live-server tests live in handshake_live_test.cpp (env-gated).

#include <gtest/gtest.h>

#include <cstdint>
#include <cstring>
#include <filesystem>
#include <vector>

#include "capture_parser.h"
#include "westwood/westwood_rc4.h"
#include "westwood/westwood_rsa.h"

namespace {

// Repo-relative path. Tests are normally invoked via ctest from the repo
// build dir, so we resolve relative to the source tree using the macro
// CMake injects below.
std::filesystem::path CaptureFile(const char* name) {
    return std::filesystem::path(NET7_TEST_CAPTURES_DIR) / name;
}

const enbtest::Packet& FindPacket(const std::vector<enbtest::Packet>& packets, int sequence) {
    for (const auto& p : packets) {
        if (p.sequence == sequence) return p;
    }
    ADD_FAILURE() << "packet #" << sequence << " not found";
    static enbtest::Packet empty;
    return empty;
}

}  // namespace

// ---------------------------------------------------------------------------
// Capture parser
// ---------------------------------------------------------------------------

TEST(CaptureParser, ExtractsHandshakeFromCanonicalCapture) {
    auto packets = enbtest::ParseCaptureFile(CaptureFile("capture_1_handshake.txt").string());

    // We sliced out packets 216-225 plus a few extras around the handoff.
    ASSERT_GE(packets.size(), 4u);

    const auto& syn1 = FindPacket(packets, 216);
    EXPECT_EQ(syn1.declared_length, 4);
    EXPECT_EQ(syn1.direction, enbtest::Direction::kClientToServer);
    EXPECT_EQ(syn1.peer_port, 3801);
    ASSERT_EQ(syn1.bytes.size(), 4u);
    EXPECT_EQ(syn1.bytes[0], 0x00);  // SYN1 opcode
    EXPECT_EQ(syn1.bytes[1], 0x14);
    EXPECT_EQ(syn1.bytes[2], 0x25);
    EXPECT_EQ(syn1.bytes[3], 0x46);

    const auto& ack1 = FindPacket(packets, 217);
    EXPECT_EQ(ack1.declared_length, 86);
    EXPECT_EQ(ack1.direction, enbtest::Direction::kServerToClient);
    ASSERT_EQ(ack1.bytes.size(), 86u);
    EXPECT_EQ(ack1.bytes[0], 0x01);  // ACK1 opcode

    const auto& syn2 = FindPacket(packets, 218);
    EXPECT_EQ(syn2.declared_length, 84);
    ASSERT_EQ(syn2.bytes.size(), 84u);
    EXPECT_EQ(syn2.bytes[0], 0x02);  // SYN2 opcode

    const auto& ack2 = FindPacket(packets, 219);
    EXPECT_EQ(ack2.declared_length, 12);
    ASSERT_EQ(ack2.bytes.size(), 12u);
    EXPECT_EQ(ack2.bytes[0], 0x03);  // ACK2 opcode
}

// ---------------------------------------------------------------------------
// Westwood RSA
// ---------------------------------------------------------------------------

TEST(WestwoodRsa, ModulusMatchesCapturedAck1) {
    // ACK1 layout per server/src/Net7Proxy WW handshake docs:
    //   [0]    = 0x01 (ACK1)
    //   [1]    = 0x14
    //   [2-3]  = session id
    //   [4-7]  = unknown
    //   [8-11] = unknown
    //   [12-15]= modulus length (big-endian uint32, = 0x41 = 65)
    //   [16]   = modulus MSB = 0x00
    //   [17-80]= 64-byte modulus N
    //   [81-84]= exponent length = 0x01
    //   [85]   = exponent = 0x23

    auto packets = enbtest::ParseCaptureFile(CaptureFile("capture_1_handshake.txt").string());
    const auto& ack1 = FindPacket(packets, 217);
    ASSERT_EQ(ack1.bytes.size(), 86u);

    EXPECT_EQ(ack1.bytes[12], 0x00);
    EXPECT_EQ(ack1.bytes[13], 0x00);
    EXPECT_EQ(ack1.bytes[14], 0x00);
    EXPECT_EQ(ack1.bytes[15], 0x41);
    EXPECT_EQ(ack1.bytes[16], 0x00);

    westwood::Rsa rsa;
    unsigned char modulus[westwood::kRsaBlockSize];
    rsa.GetModulusBytes(modulus);

    for (int i = 0; i < westwood::kRsaBlockSize; ++i) {
        EXPECT_EQ(modulus[i], ack1.bytes[17 + i])
            << "modulus byte " << i << " mismatch";
    }

    EXPECT_EQ(ack1.bytes[81], 0x00);
    EXPECT_EQ(ack1.bytes[82], 0x00);
    EXPECT_EQ(ack1.bytes[83], 0x00);
    EXPECT_EQ(ack1.bytes[84], 0x01);
    EXPECT_EQ(ack1.bytes[85], rsa.GetPublicExponentByte());
    EXPECT_EQ(ack1.bytes[85], 0x23);  // 35 decimal
}

TEST(WestwoodRsa, EncryptDecryptRoundTrip) {
    westwood::Rsa rsa;

    unsigned char plain[westwood::kRsaBlockSize] = {};
    // RC4 session key length = 8 bytes; the client pads the rest with zeros.
    const unsigned char session_key[8] = {
        0x26, 0x52, 0x9D, 0x2B, 0x54, 0x30, 0xB9, 0x00,
    };
    std::memcpy(plain, session_key, sizeof(session_key));

    unsigned char ciphertext[westwood::kRsaBlockSize];
    ASSERT_TRUE(rsa.EncryptBlock(plain, sizeof(session_key), ciphertext));

    unsigned char recovered[westwood::kRsaBlockSize] = {};
    ASSERT_TRUE(rsa.DecryptBlock(ciphertext, recovered));

    EXPECT_EQ(std::memcmp(recovered, plain, sizeof(plain)), 0)
        << "RSA round-trip failed: plaintext != decrypt(encrypt(plaintext))";
}

TEST(WestwoodRsa, DecryptCapturedSyn2YieldsSessionKey) {
    // SYN2 carries the client-chosen 64-byte RSA-encrypted block. Per
    // proxy/Connection.cpp:230-268 (DoClientKeyExchange), the client lays
    // the 8-byte RC4 key REVERSED into bytes [56..63] of the plaintext
    // block, leaving [0..55] zero, then RSA-encrypts. The server then
    // reverses the byte order back when reading rc4key[0x3f..0x38].
    auto packets = enbtest::ParseCaptureFile(CaptureFile("capture_1_handshake.txt").string());
    const auto& syn2 = FindPacket(packets, 218);
    ASSERT_EQ(syn2.bytes.size(), 84u);

    // SYN2 layout:
    //   [0]     = 0x02 (SYN2)
    //   [1]     = 0x14
    //   [2-3]   = session id
    //   [4-7]   = unknown
    //   [8-11]  = unknown
    //   [12-15] = unknown
    //   [16-19] = session-key block length (= 64, big-endian)
    //   [20-83] = 64-byte RSA-encrypted block
    EXPECT_EQ(syn2.bytes[16], 0x00);
    EXPECT_EQ(syn2.bytes[17], 0x00);
    EXPECT_EQ(syn2.bytes[18], 0x00);
    EXPECT_EQ(syn2.bytes[19], 0x40);

    unsigned char ciphertext[westwood::kRsaBlockSize];
    std::memcpy(ciphertext, syn2.bytes.data() + 20, westwood::kRsaBlockSize);

    westwood::Rsa rsa;
    unsigned char plaintext[westwood::kRsaBlockSize] = {};
    ASSERT_TRUE(rsa.DecryptBlock(ciphertext, plaintext));

    // Capture annotates the RC4 session key as 26 52 9D 2B 54 30 B9 00.
    // The real game client fills [0..55] with non-zero padding (the
    // server ignores it); only bytes [56..63] carry the reversed key.
    const unsigned char captured_key[8] = {
        0x26, 0x52, 0x9D, 0x2B, 0x54, 0x30, 0xB9, 0x00,
    };
    for (int i = 0; i < 8; ++i) {
        EXPECT_EQ(plaintext[63 - i], captured_key[i])
            << "reversed key byte mismatch at plaintext[" << (63 - i) << "]";
    }
}

// ---------------------------------------------------------------------------
// Westwood RC4
// ---------------------------------------------------------------------------

TEST(WestwoodRc4, IsItsOwnInverse) {
    // RC4 is a stream cipher: enc(enc(x)) = x with a fresh key schedule.
    const unsigned char key[8] = {
        0x26, 0x52, 0x9D, 0x2B, 0x54, 0x30, 0xB9, 0x00,
    };
    const unsigned char plaintext[] =
        "the quick brown fox jumps over the lazy dog";
    const long len = static_cast<long>(sizeof(plaintext) - 1);

    unsigned char buf[sizeof(plaintext) - 1];
    std::memcpy(buf, plaintext, len);

    {
        westwood::Rc4 enc;
        enc.PrepareKey(key, sizeof(key));
        enc.Crypt(buf, len);
    }
    EXPECT_NE(std::memcmp(buf, plaintext, len), 0)
        << "ciphertext should differ from plaintext";
    {
        westwood::Rc4 dec;
        dec.PrepareKey(key, sizeof(key));
        dec.Crypt(buf, len);
    }
    EXPECT_EQ(std::memcmp(buf, plaintext, len), 0)
        << "round-trip RC4 should restore plaintext";
}

TEST(WestwoodRc4, MatchesRfc6229TestVector) {
    // RFC 6229 test vector for key 0x0102030405:
    //   keystream[0..15] = b2 39 63 05 f0 3d c0 27  cc c3 52 4a 0a 11 18 a8
    const unsigned char key[5] = {0x01, 0x02, 0x03, 0x04, 0x05};
    unsigned char buf[16] = {};

    westwood::Rc4 rc4;
    rc4.PrepareKey(key, sizeof(key));
    rc4.Crypt(buf, sizeof(buf));

    const unsigned char expected[16] = {
        0xb2, 0x39, 0x63, 0x05, 0xf0, 0x3d, 0xc0, 0x27,
        0xcc, 0xc3, 0x52, 0x4a, 0x0a, 0x11, 0x18, 0xa8,
    };
    EXPECT_EQ(std::memcmp(buf, expected, sizeof(buf)), 0);
}
