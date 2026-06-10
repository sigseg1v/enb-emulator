// tests/server/protocol/auth_wrapper_test.cpp
//
// Phase AH (AH-8/AH-9): proves the per-packet C->S auth wrapper rejects a
// forged token and that the rejection carries a loggable reason. This drives
// the SAME net7::CheckAuthWrapper the runtime calls
// (server/src/UDPConnection.cpp), so a pass here is a pass for the real recv
// edge -- not a copy of the logic.
//
// The key adversarial case the owner asked for: a wrapper that parses perfectly
// (correct 17-byte length, correct version byte, well-formed inner header) but
// carries the WRONG token for the player it claims. That must NOT be allowed to
// drive any action on the player, and the drop must be logged. The runtime logs
// AuthWrapperReason(verdict) (rate-limited, token never logged); we assert both
// the verdict and that exact reason string here.

#include <gtest/gtest.h>
#include <cstdint>
#include <cstring>
#include <vector>

#include "AuthWrapper.h"

namespace {

constexpr size_t kMaxBuffer = 4096;   // stands in for the server's MAX_BUFFER

// Build a wrapped record: [version][16 token][12-byte EnbUdpHeader][payload].
// Only player_id (offset 4 of the header) matters to the decision; the rest is
// arbitrary filler so the record is a realistic length.
std::vector<unsigned char> MakeRecord(uint8_t version,
                                      const unsigned char token[16],
                                      int32_t player_id,
                                      size_t payload_len = 8)
{
    std::vector<unsigned char> rec;
    rec.push_back(version);
    rec.insert(rec.end(), token, token + 16);
    // 12-byte inner header.
    unsigned char hdr[12] = {0};
    std::memcpy(hdr + net7::kUdpHeaderPlayerIdOff, &player_id, sizeof(player_id));
    rec.insert(rec.end(), hdr, hdr + 12);
    for (size_t i = 0; i < payload_len; i++)
        rec.push_back((unsigned char)(0x40 + i));
    return rec;
}

void FillToken(unsigned char out[16], unsigned char base)
{
    for (int i = 0; i < 16; i++) out[i] = (unsigned char)(base + i);
}

// A lookup that always returns the same "correct" bound token for any player.
struct FixedTokenLookup {
    unsigned char token[16];
    bool found = true;
    bool operator()(int32_t /*player_id*/, unsigned char out[16]) const {
        if (!found) return false;
        std::memcpy(out, token, 16);
        return true;
    }
};

}  // namespace

TEST(AuthWrapper, CorrectToken_GameplayDatagram_IsAccepted) {
    unsigned char tok[16]; FillToken(tok, 0xA0);
    auto rec = MakeRecord(net7::kAuthWrapperVersion, tok, /*player_id=*/0x1234);

    FixedTokenLookup lookup; FillToken(lookup.token, 0xA0);   // matches
    const unsigned char *inner = nullptr; int inner_len = 0;

    auto v = net7::CheckAuthWrapper(rec.data(), rec.size(), kMaxBuffer,
                                    lookup, &inner, &inner_len);

    EXPECT_EQ(v, net7::AuthWrapperVerdict::Accept);
    ASSERT_NE(inner, nullptr);
    // Inner is exactly the bytes after the 17-byte wrapper.
    EXPECT_EQ(inner, rec.data() + 17);
    EXPECT_EQ((size_t)inner_len, rec.size() - 17);
}

TEST(AuthWrapper, WrongToken_WellFormedOtherwise_IsDroppedAsTokenMismatch) {
    // The adversarial case: length, version, and inner header all parse fine;
    // only the token is wrong. Must be dropped, and the reason must be the one
    // the runtime logs.
    unsigned char forged[16]; FillToken(forged, 0xB0);
    auto rec = MakeRecord(net7::kAuthWrapperVersion, forged, /*player_id=*/0x1234);

    FixedTokenLookup lookup; FillToken(lookup.token, 0xA0);   // the REAL token
    const unsigned char *inner = (const unsigned char *)0xdead; int inner_len = -1;

    auto v = net7::CheckAuthWrapper(rec.data(), rec.size(), kMaxBuffer,
                                    lookup, &inner, &inner_len);

    EXPECT_EQ(v, net7::AuthWrapperVerdict::DropTokenMismatch);
    // No action is allowed through: out-params untouched (caller dispatches
    // nothing on a non-Accept verdict).
    EXPECT_EQ(inner, (const unsigned char *)0xdead);
    EXPECT_EQ(inner_len, -1);
    // This is the exact string LogAuthWrapperDrop records (rate-limited).
    EXPECT_STREQ(net7::AuthWrapperReason(v), "token mismatch");
}

TEST(AuthWrapper, WrongToken_DiffersOnlyInLastByte_StillDropped) {
    // Constant-time compare must reject even a near-miss token.
    unsigned char real[16]; FillToken(real, 0xA0);
    unsigned char almost[16]; FillToken(almost, 0xA0);
    almost[15] ^= 0x01;   // flip one bit of the last byte

    auto rec = MakeRecord(net7::kAuthWrapperVersion, almost, /*player_id=*/7);
    FixedTokenLookup lookup; std::memcpy(lookup.token, real, 16);
    const unsigned char *inner = nullptr; int inner_len = 0;

    auto v = net7::CheckAuthWrapper(rec.data(), rec.size(), kMaxBuffer,
                                    lookup, &inner, &inner_len);
    EXPECT_EQ(v, net7::AuthWrapperVerdict::DropTokenMismatch);
}

TEST(AuthWrapper, NoBoundToken_GameplayDatagram_IsDropped) {
    // Player exists on the wire but has no token bound (lookup returns false):
    // a gameplay datagram for an unauthenticated GameID must be dropped.
    unsigned char tok[16]; FillToken(tok, 0xC0);
    auto rec = MakeRecord(net7::kAuthWrapperVersion, tok, /*player_id=*/99);

    FixedTokenLookup lookup; lookup.found = false;
    const unsigned char *inner = nullptr; int inner_len = 0;

    auto v = net7::CheckAuthWrapper(rec.data(), rec.size(), kMaxBuffer,
                                    lookup, &inner, &inner_len);
    EXPECT_EQ(v, net7::AuthWrapperVerdict::DropTokenMismatch);
}

TEST(AuthWrapper, PreAuthDatagram_PlayerIdZero_SkipsTokenCheck) {
    // player_id == 0 (master/global handshake before the ticket is learned):
    // accepted without ever consulting the token lookup, even with a zero token.
    unsigned char zero[16] = {0};
    auto rec = MakeRecord(net7::kAuthWrapperVersion, zero, /*player_id=*/0);

    // Lookup that would FAIL the test if called -- proves it is never consulted.
    auto must_not_be_called = [](int32_t, unsigned char[16]) -> bool {
        ADD_FAILURE() << "token lookup consulted for a pre-auth (player_id==0) datagram";
        return false;
    };
    const unsigned char *inner = nullptr; int inner_len = 0;

    auto v = net7::CheckAuthWrapper(rec.data(), rec.size(), kMaxBuffer,
                                    must_not_be_called, &inner, &inner_len);
    EXPECT_EQ(v, net7::AuthWrapperVerdict::Accept);
}

TEST(AuthWrapper, BadVersionByte_IsDropped) {
    unsigned char tok[16]; FillToken(tok, 0xA0);
    auto rec = MakeRecord(/*version=*/0x02, tok, /*player_id=*/0x1234);

    FixedTokenLookup lookup; FillToken(lookup.token, 0xA0);
    const unsigned char *inner = nullptr; int inner_len = 0;

    auto v = net7::CheckAuthWrapper(rec.data(), rec.size(), kMaxBuffer,
                                    lookup, &inner, &inner_len);
    EXPECT_EQ(v, net7::AuthWrapperVerdict::DropBadVersion);
    EXPECT_STREQ(net7::AuthWrapperReason(v), "bad wrapper version");
}

TEST(AuthWrapper, TooShortForWrapperPlusHeader_IsDropped) {
    // 17 + 12 = 29 is the minimum; give it 28.
    std::vector<unsigned char> rec(28, 0);
    rec[0] = net7::kAuthWrapperVersion;
    FixedTokenLookup lookup; FillToken(lookup.token, 0xA0);
    const unsigned char *inner = nullptr; int inner_len = 0;

    auto v = net7::CheckAuthWrapper(rec.data(), rec.size(), kMaxBuffer,
                                    lookup, &inner, &inner_len);
    EXPECT_EQ(v, net7::AuthWrapperVerdict::DropShortOrOversize);
    EXPECT_STREQ(net7::AuthWrapperReason(v), "short/oversize frame");
}

TEST(AuthWrapper, OversizeRecord_IsDropped) {
    unsigned char tok[16]; FillToken(tok, 0xA0);
    // Build a record at/over kMaxBuffer.
    auto rec = MakeRecord(net7::kAuthWrapperVersion, tok, 0x1234,
                          /*payload_len=*/kMaxBuffer);
    FixedTokenLookup lookup; FillToken(lookup.token, 0xA0);
    const unsigned char *inner = nullptr; int inner_len = 0;

    auto v = net7::CheckAuthWrapper(rec.data(), rec.size(), kMaxBuffer,
                                    lookup, &inner, &inner_len);
    EXPECT_EQ(v, net7::AuthWrapperVerdict::DropShortOrOversize);
}

TEST(AuthWrapper, ConstantTimeTokenEqual_Basic) {
    unsigned char a[16]; FillToken(a, 0x11);
    unsigned char b[16]; FillToken(b, 0x11);
    EXPECT_TRUE(net7::ConstantTimeTokenEqual(a, b));
    b[0] ^= 0x80;
    EXPECT_FALSE(net7::ConstantTimeTokenEqual(a, b));
}
