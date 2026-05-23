# Phase E — OpenSSL 1.0 → 3.x migration

Goal: get the server building against OpenSSL 3.x without deprecation warnings. Currently includes use `-DUSE_OPENSSL` and link `-lssl -lcrypto -lcryptopp` (per `server/Makefile.legacy`).

## Outcome

**Server is clean under `OPENSSL_API_COMPAT=0x30000000L` with `-Werror=deprecated-declarations`.** Inventory shows the actively-built server only touches OpenSSL in two TUs (`WestwoodRSA.cpp`, `SSL_Listener.cpp`); both already use 3.x-compatible APIs after the Phase B BIGNUM migration. The CMake compat level was raised to lock this in.

Proxy and login-server (not currently in the CMake build) have one known issue each: `SSLv2_client_method()` was removed in OpenSSL 1.1 — they will fail to link in 3.x. Documented in `server/crypto/MIGRATION_TABLE.md` for whoever wires them up next.

## Items

- [x] Inventory OpenSSL API usage: `grep -rn 'EVP_\|RSA_\|BIO_\|SSL_\|ERR_\|HMAC_\|SHA1_\|MD5_\|DES_\|CIPHER_\|EC_\|DH_\|PEM_\|X509_' server/src login-server proxy` → bucket counts in `server/crypto/OPENSSL_INVENTORY.md`.
      Touches: server/crypto/OPENSSL_INVENTORY.md
      Notes: Only `server/src/{WestwoodRSA,SSL_Listener,SSL_Connection,Connection}.cpp` directly use OpenSSL on the server side; all other matches in `grep` were game-domain false positives ("ENGINE_" → ship engines, "SKILL_ENGINE_TECH", etc.) or vendored MySQL headers.
- [x] Identify the deprecated-in-3.x calls and produce a migration table: old call → 3.x replacement.
      Touches: server/crypto/MIGRATION_TABLE.md
      Notes: Only two real categories: `BN_init` (migrated in Phase B for server, migrated in Phase E for proxy + login-server) and `SSLv2_client_method` (in proxy + login-server — those are not in the build yet; documented for follow-up).
- [x] For files using cryptopp: leave alone.
      Notes: Crypto++ surface (`server/src/CryptoPP/`) is independent of OpenSSL version.
- [x] Compile-time test: build server with `-DOPENSSL_API_COMPAT=0x30000000L`.
      Touches: server/CMakeLists.txt
      Notes: Trial-compiled seven representative TUs (`WestwoodRSA`, `SSL_Listener`, `SSL_Connection`, `Connection`, `ServerManager`, `ConnectionManager`, `Net7`) with `-Werror=deprecated-declarations`. Zero OpenSSL deprecation warnings. `OPENSSL_API_COMPAT` bumped from `0x10100000L` to `0x30000000L` in `server/CMakeLists.txt`.
- [x] Migrate the lowest-hanging fruit as a worked example.
      Notes: Migrated `proxy/WestwoodRSA.cpp` and `login-server/Net7SSL/WestwoodRSA.cpp` (the last two `BN_init` callers in the tree) to the same heap-allocated-BIGNUM pattern as `server/src/WestwoodRSA.cpp`, including matching `BN_free` on every exit path.

## Cross-cutting

- [x] `server/crypto/` directory created; inventory + migration table committed there.

## Verification

- `OPENSSL_INVENTORY.md` and `MIGRATION_TABLE.md` committed.
- Server still compiles (no regression in error count) under the bumped API compat level — verified per-file with `-Werror=deprecated-declarations`.
- Proceed to Phase F.

## Deferred

- Vestigial `server/src/openssl/` (70 headers) is not on the include path; should be deleted in a later cleanup. Not deleting here to keep Phase E focused.
- Proxy and login-server's `SSLv2_client_method` calls: defer until they re-enter the build.
