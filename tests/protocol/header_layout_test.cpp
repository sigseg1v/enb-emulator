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
