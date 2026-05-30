// tests/client/handshake_driver.cpp

#include "handshake_driver.h"

#include <chrono>
#include <cstring>
#include <random>

namespace enbtest {

namespace {

constexpr int kAck1Size = 86;
constexpr int kSyn2Size = 84;
constexpr int kAck2Size = 12;

void WriteBE32(unsigned char* out, uint32_t v) {
    out[0] = static_cast<unsigned char>((v >> 24) & 0xff);
    out[1] = static_cast<unsigned char>((v >> 16) & 0xff);
    out[2] = static_cast<unsigned char>((v >> 8) & 0xff);
    out[3] = static_cast<unsigned char>(v & 0xff);
}

uint16_t ReadBE16(const unsigned char* in) {
    return static_cast<uint16_t>((in[0] << 8) | in[1]);
}

uint32_t ReadBE32(const unsigned char* in) {
    return (static_cast<uint32_t>(in[0]) << 24) |
           (static_cast<uint32_t>(in[1]) << 16) |
           (static_cast<uint32_t>(in[2]) << 8) | static_cast<uint32_t>(in[3]);
}

}  // namespace

bool RunClientHandshake(TcpClient& client, const westwood::Rsa& rsa,
                        uint16_t session_id, uint64_t rng_seed,
                        HandshakeResult& out, std::string* err) {
    auto set_err = [&](const std::string& msg) {
        if (err) *err = msg;
    };

    // --- 1. SYN1 -----------------------------------------------------------
    unsigned char syn1[4];
    syn1[0] = 0x00;
    syn1[1] = 0x14;
    syn1[2] = static_cast<unsigned char>((session_id >> 8) & 0xff);
    syn1[3] = static_cast<unsigned char>(session_id & 0xff);
    if (client.Send(syn1, sizeof(syn1)) != static_cast<int>(sizeof(syn1))) {
        set_err("SYN1 send failed: " + client.last_error());
        return false;
    }

    // --- 2. ACK1 -----------------------------------------------------------
    unsigned char ack1[kAck1Size];
    if (!client.RecvExact(ack1, sizeof(ack1))) {
        set_err("ACK1 recv failed: " + client.last_error());
        return false;
    }
    if (ack1[0] != 0x01) {
        set_err("ACK1: bad opcode");
        return false;
    }
    if (ReadBE16(ack1 + 2) != session_id) {
        set_err("ACK1: session id mismatch");
        return false;
    }
    if (ReadBE32(ack1 + 12) != 0x41) {
        set_err("ACK1: bad modulus length");
        return false;
    }
    // Verify the server's advertised modulus matches the one we hold
    // statically; mismatch means we're talking to the wrong server.
    unsigned char our_modulus[westwood::kRsaBlockSize];
    rsa.GetModulusBytes(our_modulus);
    if (std::memcmp(our_modulus, ack1 + 17, westwood::kRsaBlockSize) != 0) {
        set_err("ACK1: modulus mismatch (server is using a different RSA key)");
        return false;
    }
    if (ack1[85] != rsa.GetPublicExponentByte()) {
        set_err("ACK1: exponent mismatch");
        return false;
    }
    unsigned char cookie1[4];
    unsigned char cookie2[4];
    std::memcpy(cookie1, ack1 + 4, 4);
    std::memcpy(cookie2, ack1 + 8, 4);

    // --- 3. SYN2 -----------------------------------------------------------
    // Choose a deterministic 8-byte RC4 session key.
    std::mt19937_64 rng(rng_seed == 0
                            ? static_cast<uint64_t>(std::chrono::steady_clock::now()
                                                        .time_since_epoch()
                                                        .count())
                            : rng_seed);
    std::array<unsigned char, 8> rc4_key{};
    for (auto& b : rc4_key) {
        b = static_cast<unsigned char>(rng() & 0xff);
    }

    unsigned char syn2[kSyn2Size];
    std::memset(syn2, 0, sizeof(syn2));
    syn2[0] = 0x02;
    syn2[1] = 0x14;
    syn2[2] = static_cast<unsigned char>((session_id >> 8) & 0xff);
    syn2[3] = static_cast<unsigned char>(session_id & 0xff);
    std::memcpy(syn2 + 4, cookie1, 4);
    std::memcpy(syn2 + 8, cookie2, 4);
    syn2[12] = 0x00;
    syn2[13] = 0x00;
    syn2[14] = 0x40;
    syn2[15] = 0x00;
    WriteBE32(syn2 + 16, westwood::kRsaBlockSize);

    // Build the 64-byte plaintext block: arbitrary leading padding,
    // reversed key in the last 8 bytes (matches proxy/Connection.cpp:260).
    unsigned char plain[westwood::kRsaBlockSize];
    for (int i = 0; i < westwood::kRsaBlockSize - 8; ++i) {
        plain[i] = static_cast<unsigned char>(rng() & 0xff);
    }
    for (int i = 0; i < 8; ++i) {
        plain[westwood::kRsaBlockSize - 1 - i] = rc4_key[i];
    }

    unsigned char ciphertext[westwood::kRsaBlockSize];
    if (!rsa.EncryptBlock(plain, sizeof(plain), ciphertext)) {
        set_err("RSA encrypt failed");
        return false;
    }
    std::memcpy(syn2 + 20, ciphertext, westwood::kRsaBlockSize);

    if (client.Send(syn2, sizeof(syn2)) != static_cast<int>(sizeof(syn2))) {
        set_err("SYN2 send failed: " + client.last_error());
        return false;
    }

    // --- 4. ACK2 -----------------------------------------------------------
    unsigned char ack2[kAck2Size];
    if (!client.RecvExact(ack2, sizeof(ack2))) {
        set_err("ACK2 recv failed: " + client.last_error());
        return false;
    }
    if (ack2[0] != 0x03) {
        set_err("ACK2: bad opcode");
        return false;
    }
    if (ReadBE16(ack2 + 2) != session_id) {
        set_err("ACK2: session id mismatch");
        return false;
    }

    out.session_id = session_id;
    out.cord_port = ReadBE16(ack2 + 6);
    out.rc4_key = rc4_key;
    out.rx_cipher.PrepareKey(rc4_key.data(), static_cast<int>(rc4_key.size()));
    out.tx_cipher.PrepareKey(rc4_key.data(), static_cast<int>(rc4_key.size()));
    return true;
}

bool RunNet7Handshake(TcpClient& client, const westwood::Rsa& rsa,
                      uint64_t rng_seed, HandshakeResult& out,
                      std::string* err) {
    auto set_err = [&](const std::string& msg) {
        if (err) *err = msg;
    };

    // --- 1. Server sends 74-byte pubkey packet immediately on connect. -----
    constexpr int kPubkeyPacketSize = 74;
    unsigned char pubkey[kPubkeyPacketSize];
    if (!client.RecvExact(pubkey, sizeof(pubkey))) {
        set_err("pubkey recv failed: " + client.last_error());
        return false;
    }
    // [0..3]   modulus length (BE, should be 65)
    // [4]      modulus MSB (00)
    // [5..68]  modulus bytes
    // [69..72] exponent length (BE, should be 1)
    // [73]     exponent
    if (ReadBE32(pubkey) != 0x41) {
        set_err("net7 pubkey: bad modulus length");
        return false;
    }
    unsigned char our_modulus[westwood::kRsaBlockSize];
    rsa.GetModulusBytes(our_modulus);
    if (std::memcmp(our_modulus, pubkey + 5, westwood::kRsaBlockSize) != 0) {
        set_err("net7 pubkey: modulus mismatch");
        return false;
    }
    if (ReadBE32(pubkey + 69) != 0x01 ||
        pubkey[73] != rsa.GetPublicExponentByte()) {
        set_err("net7 pubkey: bad exponent block");
        return false;
    }

    // --- 2. Choose RC4 key, build reversed-key plaintext, encrypt, send. ---
    std::mt19937_64 rng(rng_seed == 0
                            ? static_cast<uint64_t>(std::chrono::steady_clock::now()
                                                        .time_since_epoch()
                                                        .count())
                            : rng_seed);
    std::array<unsigned char, 8> rc4_key{};
    for (auto& b : rc4_key) {
        b = static_cast<unsigned char>(rng() & 0xff);
    }

    unsigned char plain[westwood::kRsaBlockSize];
    for (int i = 0; i < westwood::kRsaBlockSize - 8; ++i) {
        plain[i] = static_cast<unsigned char>(rng() & 0xff);
    }
    for (int i = 0; i < 8; ++i) {
        plain[westwood::kRsaBlockSize - 1 - i] = rc4_key[i];
    }

    unsigned char ciphertext[westwood::kRsaBlockSize];
    if (!rsa.EncryptBlock(plain, sizeof(plain), ciphertext)) {
        set_err("net7: RSA encrypt failed");
        return false;
    }

    unsigned char wire[sizeof(uint32_t) + westwood::kRsaBlockSize];
    WriteBE32(wire, westwood::kRsaBlockSize);
    std::memcpy(wire + sizeof(uint32_t), ciphertext, westwood::kRsaBlockSize);
    if (client.Send(wire, sizeof(wire)) != static_cast<int>(sizeof(wire))) {
        set_err("net7 keyblock send failed: " + client.last_error());
        return false;
    }

    // --- 3. No ACK packet in the Net-7 protocol. The next bytes from the --
    //        server are RC4-encrypted opcode traffic.
    out.session_id = 0;
    out.cord_port = 0;
    out.rc4_key = rc4_key;
    out.rx_cipher.PrepareKey(rc4_key.data(), static_cast<int>(rc4_key.size()));
    out.tx_cipher.PrepareKey(rc4_key.data(), static_cast<int>(rc4_key.size()));
    return true;
}

}  // namespace enbtest
