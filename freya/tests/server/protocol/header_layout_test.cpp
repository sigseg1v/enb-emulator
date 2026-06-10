// tests/server/protocol/header_layout_test.cpp
//
// Phase G scaffold. The first protocol test -- it pins the wire layout of
// EnbTcpHeader / EnbUdpHeader / EnbUdpAuthWrapper so a future refactor that
// reorders or repacks them fails fast.
//
// These structs come straight from common/include/net7/PacketStructures.h --
// the single shared definition the server, proxy and login-server all compile
// from. The test includes that header DIRECTLY rather than mirroring the structs
// locally: a copy would pin the copy, not the real layout, so the real header
// could silently diverge while this test stayed green. (The original scaffold
// note about PacketStructures.h dragging in Net7.h/Win32 is stale -- Phase R
// Wave 2 split the wire structs into this stand-alone header, whose only
// dependency is net7/Packing.h for ATTRIB_PACKED + the fixed-width int types.)

#include <gtest/gtest.h>
#include <cstdint>
#include <cstring>

#include <net7/PacketStructures.h>

TEST(WireFormat, EnbTcpHeaderIs4Bytes) {
    // The wire-format expects exactly two big-endian shorts back-to-back.
    // No padding, no alignment slack.
    EXPECT_EQ(sizeof(EnbTcpHeader), 4u);
}

TEST(WireFormat, EnbTcpHeaderFieldOrder) {
    // Layout: bytes 0-1 = size, bytes 2-3 = opcode.
    EnbTcpHeader h{};
    h.size = 0x0102;
    h.opcode = 0x0304;

    const uint8_t *raw = reinterpret_cast<const uint8_t *>(&h);
    // On a little-endian host the bytes go [0x02, 0x01, 0x04, 0x03].
    EXPECT_EQ(raw[0], 0x02);
    EXPECT_EQ(raw[1], 0x01);
    EXPECT_EQ(raw[2], 0x04);
    EXPECT_EQ(raw[3], 0x03);
}

TEST(WireFormat, EnbUdpHeaderIs12Bytes) {
    // size(2) + opcode(2) + player_id(4) + packet_sequence(4) = 12, no padding.
    EXPECT_EQ(sizeof(EnbUdpHeader), 12u);
}

TEST(WireFormat, EnbUdpHeaderFieldOffsets) {
    EXPECT_EQ(offsetof(EnbUdpHeader, size), 0u);
    EXPECT_EQ(offsetof(EnbUdpHeader, opcode), 2u);
    EXPECT_EQ(offsetof(EnbUdpHeader, player_id), 4u);
    EXPECT_EQ(offsetof(EnbUdpHeader, packet_sequence), 8u);
}

TEST(WireFormat, EnbUdpAuthWrapperIs17Bytes) {
    // version(1) + token(16) = 17, no padding. The server strips exactly this
    // many bytes off the front of every DTLS C->S datagram before dispatch.
    EXPECT_EQ(sizeof(EnbUdpAuthWrapper), 17u);
}

TEST(WireFormat, EnbUdpAuthWrapperFieldOffsets) {
    EXPECT_EQ(offsetof(EnbUdpAuthWrapper, version), 0u);
    EXPECT_EQ(offsetof(EnbUdpAuthWrapper, token), 1u);
}

TEST(WireFormat, EnbUdpAuthWrapperVersionConstant) {
    // Pin the shared header's macros against the values the proxy producer and
    // the C# model (tools/cli-client .../Net/AuthWrappedPacket.cs WrapperVersion)
    // also hard-code -- the three places that touch this format must agree.
    EXPECT_EQ(NET7_UDP_AUTH_WRAPPER_VERSION, 0x01);
    EXPECT_EQ(NET7_UDP_AUTH_TOKEN_LEN, 16);
}
