// tests/client/westwood/westwood_rc4.h
//
// Standalone Westwood RC4 implementation for the test client.
// Behaviourally identical to server/src/WestwoodRC4.{h,cpp}, but with no
// Net7.h dependency so it can be built outside the server tree.

#pragma once

namespace westwood {

class Rc4 {
public:
    Rc4();
    ~Rc4() = default;

    Rc4(const Rc4&) = delete;
    Rc4& operator=(const Rc4&) = delete;

    void PrepareKey(const unsigned char* key, int key_len);

    // RC4 is its own inverse: same call encrypts and decrypts.
    void Crypt(unsigned char* buffer, long buffer_len);

private:
    static void SwapByte(unsigned char* a, unsigned char* b);

    unsigned char state_[256];
    unsigned char x_;
    unsigned char y_;
};

}  // namespace westwood
