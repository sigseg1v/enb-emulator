// tests/client/westwood/westwood_rsa.cpp
//
// Standalone Westwood RSA implementation for the test client. The 512-bit
// modulus N, primes p/q, private exponent d, and public exponent e=35 are
// the values Net-7 ships in server/src/WestwoodRSA.h. Keeping them inline
// here keeps the test client self-contained.

#include "westwood_rsa.h"

#include <openssl/bn.h>

#include <cstring>

namespace westwood {

namespace {
constexpr const char* kP = "90336306034988608177990369937674942312598126945629080039358980696516831956279";
constexpr const char* kQ = "114965715011442463284112195027084055446504070890856867618584335022146211064213";
constexpr const char* kN = "10385578014804950221065190195736491193847541479389728420426514083771326945639729736695791225573893793119489336012297845146104637691941242485732839277543427";
constexpr const char* kD = "10088847214381951643320470475858305731166183151407164751271470824235003318621252307969752086088076499395823874814123350292603347408732347765156628342107995";
constexpr const char* kE = "35";
}  // namespace

Rsa::Rsa() {
    p_ = BN_new();
    q_ = BN_new();
    N_ = BN_new();
    d_ = BN_new();
    e_ = BN_new();
    ctx_ = BN_CTX_new();
    BN_dec2bn(&p_, kP);
    BN_dec2bn(&q_, kQ);
    BN_dec2bn(&N_, kN);
    BN_dec2bn(&d_, kD);
    BN_dec2bn(&e_, kE);
}

Rsa::~Rsa() {
    BN_free(p_);
    BN_free(q_);
    BN_free(N_);
    BN_free(d_);
    BN_free(e_);
    BN_CTX_free(ctx_);
}

void Rsa::GetModulusBytes(unsigned char out[kRsaBlockSize]) const {
    std::memset(out, 0, kRsaBlockSize);
    int n = BN_num_bytes(N_);
    if (n > kRsaBlockSize) n = kRsaBlockSize;
    BN_bn2bin(N_, out + (kRsaBlockSize - n));
}

unsigned char Rsa::GetPublicExponentByte() const {
    unsigned char tmp[8] = {0};
    int n = BN_num_bytes(e_);
    BN_bn2bin(e_, tmp);
    // exponent is 35 / 0x23, fits in 1 byte
    return (n > 0) ? tmp[0] : 0;
}

bool Rsa::EncryptBlock(const unsigned char* in, unsigned int length,
                       unsigned char out[kRsaBlockSize]) const {
    if (!in || length == 0 || length > kRsaBlockSize) return false;

    unsigned char buf[kRsaBlockSize];
    std::memset(buf, 0, kRsaBlockSize);
    std::memcpy(buf, in, length);

    BIGNUM* M = BN_new();
    BIGNUM* C = BN_new();
    BN_bin2bn(buf, kRsaBlockSize, M);
    BN_mod_exp(C, M, e_, N_, ctx_);

    std::memset(out, 0, kRsaBlockSize);
    int n = BN_num_bytes(C);
    if (n > kRsaBlockSize) n = kRsaBlockSize;
    BN_bn2bin(C, out + (kRsaBlockSize - n));

    BN_free(M);
    BN_free(C);
    return true;
}

bool Rsa::DecryptBlock(const unsigned char in[kRsaBlockSize],
                       unsigned char out[kRsaBlockSize]) const {
    BIGNUM* C = BN_new();
    BIGNUM* M = BN_new();
    BN_bin2bn(in, kRsaBlockSize, C);
    BN_mod_exp(M, C, d_, N_, ctx_);

    std::memset(out, 0, kRsaBlockSize);
    int n = BN_num_bytes(M);
    if (n > kRsaBlockSize) {
        BN_free(C);
        BN_free(M);
        return false;
    }
    BN_bn2bin(M, out + (kRsaBlockSize - n));

    BN_free(C);
    BN_free(M);
    return true;
}

}  // namespace westwood
