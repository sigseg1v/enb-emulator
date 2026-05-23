// tests/client/westwood/westwood_rsa.h
//
// Westwood RSA wrapper, used during the 4-step handshake on port 3801.
// Self-contained (no Net7.h dependency) so the test client can be built
// independently of the server tree. The RSA key constants are the same
// 512-bit Westwood key Net-7 ships in server/src/WestwoodRSA.h.

#pragma once

#include <openssl/bn.h>

namespace westwood {

inline constexpr int kRsaBlockSize = 64;  // bytes

class Rsa {
public:
    Rsa();
    ~Rsa();

    Rsa(const Rsa&) = delete;
    Rsa& operator=(const Rsa&) = delete;

    // Returns the 64-byte big-endian modulus N.
    void GetModulusBytes(unsigned char out[kRsaBlockSize]) const;

    // Returns the public exponent (1 byte, value 0x23 / 35).
    unsigned char GetPublicExponentByte() const;

    // Encrypt `length` bytes of `in`. `length` must be <= kRsaBlockSize.
    // Output is exactly kRsaBlockSize bytes.
    bool EncryptBlock(const unsigned char* in, unsigned int length,
                      unsigned char out[kRsaBlockSize]) const;

    // Decrypt one kRsaBlockSize block. Output is up to kRsaBlockSize bytes,
    // right-aligned (BN_bn2bin strips leading zeros, then we shift right).
    bool DecryptBlock(const unsigned char in[kRsaBlockSize],
                      unsigned char out[kRsaBlockSize]) const;

private:
    BIGNUM* p_;
    BIGNUM* q_;
    BIGNUM* N_;
    BIGNUM* d_;
    BIGNUM* e_;
    BN_CTX* ctx_;
};

}  // namespace westwood
