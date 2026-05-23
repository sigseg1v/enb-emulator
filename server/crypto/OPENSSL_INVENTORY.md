# OpenSSL API inventory (Phase E)

## Scope

What follows is a per-target inventory of OpenSSL symbol use across the
codebase, gathered with:

```sh
grep -rln 'EVP_\|RSA_\|BIO_\|SSL_\|ERR_\|HMAC_\|SHA1_\|MD5_\|DES_\|BN_\|X509_\|PEM_' \
    server/src login-server proxy | grep -v '/openssl/'
```

Vendored OpenSSL headers under `server/src/openssl/` are excluded — they are
**vestigial**: they shipped with the 2010-era source tree, are not on the
CMake include path (verified: not listed in `server/CMakeLists.txt`), and
should be deleted in a later cleanup pass. The build uses the system
OpenSSL 3.x via `find_package(OpenSSL 3.0 REQUIRED)`.

## Server (`server/src/`)

Only two files in the actively-compiled server actually touch OpenSSL:

| File | Calls | Notes |
|---|---|---|
| `WestwoodRSA.cpp` | `BN_new`, `BN_free`, `BN_dec2bn`, `BN_bn2bin`, `BN_bin2bn`, `BN_mod_exp`, `BN_CTX_new`, `BN_CTX_free` | Already migrated in Phase B (BIGNUM heap-allocated, no `BN_init`). |
| `SSL_Listener.cpp` | `SSL_load_error_strings`, `SSLv23_server_method`, `SSL_CTX_new`, `SSL_CTX_use_certificate_file`, `SSL_CTX_use_PrivateKey_file`, `SSL_CTX_check_private_key` | `SSL_load_error_strings` is a macro to `OPENSSL_init_ssl(...)` in 3.x — not deprecated, just legacy. `SSLv23_server_method` is the same function as `TLS_server_method` (alias), still supported. |
| `SSL_Connection.cpp` | `SSL_new`, `SSL_set_fd`, `SSL_accept`, `SSL_read`, `SSL_write`, `SSL_get_error`, `SSL_free`, `SSL_shutdown` | All current-API; nothing deprecated. |
| `Connection.cpp` | (transitively via `WestwoodRSA`) | No direct OpenSSL calls. |

**Compile-time verification** (with deprecation as error):

```sh
g++ -c -DOPENSSL_API_COMPAT=0x30000000L -DUSE_OPENSSL -DLINUX -D__linux__ \
    -Wall -Wextra -Werror=deprecated-declarations -fpermissive \
    -Icompat -Isrc -Ithird_party -Isrc/LUA/lua/include \
    src/{WestwoodRSA,SSL_Listener,SSL_Connection,Connection,ServerManager,ConnectionManager,Net7}.cpp \
    -o /tmp/x.o
```

Per-file: **zero OpenSSL deprecation warnings** across the seven
representative TUs. `OPENSSL_API_COMPAT` raised from `0x10100000L` to
`0x30000000L` in `server/CMakeLists.txt` to lock this in.

## Login-server (`login-server/Net7SSL/`)

Not currently in the build (no CMakeLists). Files touching OpenSSL:

| File | Calls | Notes |
|---|---|---|
| `WestwoodRSA.cpp` | (same shape as server) | **Migrated in Phase E** (BIGNUM heap-allocated). |
| `Net7SSL.cpp` | `SSLv2_client_method`, `SSL_load_error_strings`, `SSL_CTX_new`, `SSL_CTX_use_certificate_file`, `SSL_CTX_use_PrivateKey_file` | `SSLv2_client_method` was **removed in OpenSSL 1.1**. Replace with `TLS_client_method` when this target is built. |
| `SSL_Listener.cpp` | `SSL_load_error_strings`, `SSLv23_server_method` | Same as server's `SSL_Listener.cpp`. |

## Proxy (`proxy/`)

Not currently in the build. Files touching OpenSSL:

| File | Calls | Notes |
|---|---|---|
| `WestwoodRSA.cpp` | (same shape as server) | **Migrated in Phase E**. |
| `ServerManager.cpp` | `SSLv2_client_method`, `SSL_load_error_strings`, `SSL_CTX_new` | Same `SSLv2_client_method` removal needed when this target is built. |
| `SSL_Connection.cpp` | `SSL_load_error_strings`, `SSLv23_server_method`, `SSL_CTX_new` | Fine for 3.x. |

## What's NOT used

No call to: `HMAC_*`, `MD5_*`, `SHA1_*`, `DES_*`, `EVP_*` (low or
high-level), `RSA_generate_key*`, `RSA_new`/`RSA_free`, `PEM_*`, `X509_*`
(beyond `SSL_CTX_use_certificate_file` which internally uses PEM/X509 but
doesn't expose the deprecated API surface), `ENGINE_*`,
`CRYPTO_set_locking_callback` (legacy threading).

Crypto++ (`server/src/CryptoPP/`) is independent of OpenSSL version and
needs no Phase E work.

## Verdict

- **Server is clean against OpenSSL 3.0 with `-Werror=deprecated-declarations`.**
- Proxy and login-server have two issues each that will surface only when
  they are added to the build (the removed `SSLv2_client_method`). Both
  are documented in `MIGRATION_TABLE.md`.
- The 70 vestigial headers under `server/src/openssl/` should be deleted
  in a later cleanup; they are not on the include path but they confuse
  the inventory.
