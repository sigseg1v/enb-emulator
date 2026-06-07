// tests/protocol/header_layout_test.cpp
//
// Phase G scaffold. This is the first protocol test — it pins down the
// wire layout of EnbTcpHeader / EnbUdpHeader from server/src/PacketStructures.h
// so a future refactor that reorders or repacks them fails fast.
//
// Why not include PacketStructures.h directly? It transitively pulls in
// most of Net7.h, which depends on Win32 typedefs and ServerManager forward
// decls. The headers don't isolate cleanly yet. Phase G continuation: split
// the wire structs into their own header so tests can include them without
// dragging the whole runtime.

#include <gtest/gtest.h>
#include <cstdint>
#include <cstring>

namespace {

// Mirror of server/src/PacketStructures.h::EnbTcpHeader. Keep in lock-step.
struct EnbTcpHeader {
    int16_t size;
    int16_t opcode;
} __attribute__((packed));

struct EnbUdpHeader {
    int16_t  size;
    int16_t  opcode;
    int32_t  player_id;
    int32_t  packet_sequence;
};

// Mirror of common/include/net7/PacketStructures.h::EnbUdpAuthWrapper. Phase AH
// (AH-8): the per-packet C->S auth token prepended INSIDE the DTLS channel to
// every gameplay datagram. Keep in lock-step with the C header AND with the C#
// model (tools/cli-client .../Net/AuthWrappedPacket.cs) -- the three places
// that touch this format must agree byte-for-byte.
constexpr uint8_t kAuthWrapperVersion = 0x01;   // NET7_UDP_AUTH_WRAPPER_VERSION
constexpr int     kAuthTokenLen       = 16;     // NET7_UDP_AUTH_TOKEN_LEN
struct EnbUdpAuthWrapper {
    uint8_t version;
    uint8_t token[kAuthTokenLen];
} __attribute__((packed));

}  // namespace

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
    // Must match NET7_UDP_AUTH_WRAPPER_VERSION and the C# WrapperVersion pin.
    EXPECT_EQ(kAuthWrapperVersion, 0x01);
    EXPECT_EQ(kAuthTokenLen, 16);
}
