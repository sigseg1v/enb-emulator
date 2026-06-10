// tests/client/westwood/westwood_rc4.cpp

#include "westwood_rc4.h"

#include <cstring>

namespace westwood {

Rc4::Rc4() : x_(0), y_(0) {
    std::memset(state_, 0, sizeof(state_));
}

void Rc4::PrepareKey(const unsigned char* key, int key_len) {
    for (int i = 0; i < 256; ++i) {
        state_[i] = static_cast<unsigned char>(i);
    }
    x_ = 0;
    y_ = 0;
    unsigned char index1 = 0;
    unsigned char index2 = 0;
    for (int i = 0; i < 256; ++i) {
        index2 = static_cast<unsigned char>((key[index1] + state_[i] + index2) % 256);
        SwapByte(&state_[i], &state_[index2]);
        index1 = static_cast<unsigned char>((index1 + 1) % key_len);
    }
}

void Rc4::Crypt(unsigned char* buffer, long buffer_len) {
    unsigned char x = x_;
    unsigned char y = y_;
    for (long i = 0; i < buffer_len; ++i) {
        x = static_cast<unsigned char>((x + 1) % 256);
        y = static_cast<unsigned char>((state_[x] + y) % 256);
        SwapByte(&state_[x], &state_[y]);
        unsigned char xor_index = static_cast<unsigned char>((state_[x] + state_[y]) % 256);
        buffer[i] ^= state_[xor_index];
    }
    x_ = x;
    y_ = y;
}

void Rc4::SwapByte(unsigned char* a, unsigned char* b) {
    unsigned char tmp = *a;
    *a = *b;
    *b = tmp;
}

}  // namespace westwood
