// credit_save_width_test.cpp
//
// Regression guard for the credits-corruption bug: Player::SaveCreditLevel
// serializes a u64 credits value into a SaveManager IPC buffer that
// SaveManager::HandleCreditChange reads back as a full 8-byte u64.
//
// The defect: on Linux `u64` == `unsigned long`, so a bare
// AddData(buf, credits, index) bound the AddData<unsigned long> template
// specialization in PacketMethods.h, which writes only the low 4 bytes and
// advances index by 4. The 8-byte reader then pulled the high dword from
// uninitialized buffer/stack -- credits inflated into the billions and
// changed on every save/login. The fix routes the field through the explicit
// 8-byte AddData64 emitter. These tests pin: (a) AddData64 writes 8 little-
// endian bytes and advances by 8, (b) the producer->consumer round-trip
// preserves a value whose high dword is non-zero, and (c) the old 4-byte
// path demonstrably loses the high dword (documents why AddData64 is needed).

#include <cstdint>
#include <cstring>
#include <arpa/inet.h>
#include <gtest/gtest.h>

#include "PacketMethods.h"

namespace {

// Mirror of the SaveManager::HandleCreditChange read.
uint64_t ReadCreditsLikeConsumer(const unsigned char* data) {
    return *((const uint64_t*)&data[0]);
}

TEST(CreditSaveWidth, AddData64WritesEightLittleEndianBytesAndAdvancesEight) {
    unsigned char data[32];
    std::memset(data, 0xAA, sizeof(data)); // poison: catch any unwritten byte
    int index = 0;

    const uint64_t credits = 0x1122334455667788ULL;
    AddData64(data, credits, index);

    EXPECT_EQ(index, 8);
    // little-endian byte layout (the wire/save convention is LE)
    EXPECT_EQ(data[0], 0x88);
    EXPECT_EQ(data[1], 0x77);
    EXPECT_EQ(data[2], 0x66);
    EXPECT_EQ(data[3], 0x55);
    EXPECT_EQ(data[4], 0x44);
    EXPECT_EQ(data[5], 0x33);
    EXPECT_EQ(data[6], 0x22);
    EXPECT_EQ(data[7], 0x11);
}

TEST(CreditSaveWidth, ProducerConsumerRoundTripsHighDword) {
    // ~270 billion: the exact symptom magnitude the player reported. The high
    // dword (0x3E) MUST survive, which only the 8-byte path guarantees.
    const uint64_t credits = 270000000000ULL; // 0x0000003EE0AA1700

    unsigned char data[32];
    std::memset(data, 0xAA, sizeof(data));
    int index = 0;

    AddData64(data, credits, index);                    // producer (SaveCreditLevel)
    const uint64_t got = ReadCreditsLikeConsumer(data); // consumer

    EXPECT_EQ(got, credits);
    EXPECT_EQ(index, 8);
}

TEST(CreditSaveWidth, OldFourByteUnsignedLongPathLosesHighDword) {
    // Documents the bug: the AddData<unsigned long> specialization writes only
    // 4 bytes, so an 8-byte read sees uninitialized poison in the high dword.
    unsigned char data[32];
    std::memset(data, 0xAA, sizeof(data));
    int index = 0;

    const unsigned long credits_low_only = 0xE0AA1700UL;
    AddData<unsigned long>(data, credits_low_only, index);

    EXPECT_EQ(index, 4); // only 4 bytes advanced -- the root cause
    const uint64_t got = ReadCreditsLikeConsumer(data);
    // high dword is the 0xAA poison, NOT zero: value is corrupted upward.
    EXPECT_NE(got, static_cast<uint64_t>(credits_low_only));
    EXPECT_EQ(got >> 32, 0xAAAAAAAAULL);
}

} // namespace
