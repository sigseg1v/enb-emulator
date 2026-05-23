# Phase E — OpenSSL 1.0 → 3.x migration

Goal: get the server building against OpenSSL 3.x without deprecation warnings. Currently includes use `-DUSE_OPENSSL` and link `-lssl -lcrypto -lcryptopp` (per `server/Makefile.legacy`).

## Items

- [ ] Inventory OpenSSL API usage: `grep -rn 'EVP_\|RSA_\|BIO_\|SSL_\|ERR_\|HMAC_\|SHA1_\|MD5_\|DES_\|CIPHER_\|EC_\|DH_\|PEM_\|X509_' server/src login-server proxy` → bucket counts in `server/crypto/OPENSSL_INVENTORY.md`.
      Touches: server/crypto/OPENSSL_INVENTORY.md
      Notes:
- [ ] Identify the deprecated-in-3.x calls (low-level RSA/DH/EC, HMAC_*, MD5_*, SHA1_*, DES_*) and produce a migration table: old call → 3.x replacement (typically `EVP_*` higher-level API).
      Touches: server/crypto/MIGRATION_TABLE.md
      Notes:
- [ ] For files using cryptopp: leave alone (cryptopp is independent of OpenSSL version).
- [ ] Compile-time test: build server with `-DOPENSSL_API_COMPAT=0x30000000L` (no deprecated). Capture diff in errors.
      Touches: server/CMakeLists.txt
      Notes:
- [ ] Migrate the lowest-hanging fruit (`MD5_*` → `EVP_md5()` via `EVP_Digest`; `HMAC_*` → `EVP_MAC_*`) as a worked example.
      Touches: at most 1-2 files
      Notes:

## Verification

- `OPENSSL_INVENTORY.md` and `MIGRATION_TABLE.md` committed.
- Server still compiles (no regression in error count) after the example migration.
- Proceed to Phase F.
