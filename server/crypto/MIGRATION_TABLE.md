# OpenSSL 1.x → 3.x migration table

For each call this project uses that changed in OpenSSL 3.x, the
replacement is documented below alongside its status in this tree.

## BIGNUM

| OpenSSL 1.x | OpenSSL 3.x | Status |
|---|---|---|
| `BIGNUM x; BN_init(&x);` | `BIGNUM *x = BN_new(); ... BN_free(x);` | **Done.** BIGNUM was made opaque in 1.1 and `BN_init` was removed entirely in 3.0. The three `WestwoodRSA.cpp` copies (server, proxy, login-server) all migrated to heap-allocated BIGNUMs with matching `BN_free` on every exit path (incl. error return). Verified by trial-compiling `server/src/WestwoodRSA.cpp` against `OPENSSL_API_COMPAT=0x30000000L` with `-Werror=deprecated-declarations`. |

The remaining BIGNUM surface (`BN_new`/`BN_free`/`BN_dec2bn`/`BN_bn2bin`/
`BN_bin2bn`/`BN_mod_exp`/`BN_CTX_new`/`BN_CTX_free`) is unchanged across
1.x → 3.x and needs no work.

## TLS method selection

| OpenSSL 1.x | OpenSSL 3.x | Status |
|---|---|---|
| `SSLv23_server_method()` | `TLS_server_method()` | **No code change required.** `SSLv23_server_method` is now an alias for `TLS_server_method` in 3.x and is not deprecated. Leave call sites as-is; the next refactor can rename them for clarity. |
| `SSLv23_client_method()` | `TLS_client_method()` | Same alias situation. |
| `SSLv2_client_method()` | `TLS_client_method()` | **Required for proxy and login-server (when added to build).** `SSLv2_method`/`SSLv2_client_method`/`SSLv2_server_method` were *removed* in OpenSSL 1.1.0 — they will fail to link in 3.x. Call sites: `login-server/Net7SSL/Net7SSL.cpp:387`, `proxy/ServerManager.cpp:363`. Not patched now because those targets are not in the CMake build; documented here so the next person wiring them up sees the fix. |

## Library init / error string loading

| OpenSSL 1.x | OpenSSL 3.x | Status |
|---|---|---|
| `SSL_load_error_strings();` | `OPENSSL_init_ssl(OPENSSL_INIT_LOAD_SSL_STRINGS \| OPENSSL_INIT_LOAD_CRYPTO_STRINGS, NULL);` | **No code change required.** In 1.1+, `SSL_load_error_strings()` is a macro to the new init API. Auto-init is also implicit on first use of any SSL/crypto routine — the explicit call is harmless but unnecessary. Removable in a future cleanup; not deprecation-tagged in 3.x. |
| `OpenSSL_add_all_algorithms();` | (no-op; auto-init) | Not used in this tree. |
| `ERR_load_BIO_strings();` / `ERR_free_strings();` / `EVP_cleanup();` | (auto-init / no-op) | Not used in this tree. |
| `CRYPTO_set_locking_callback(...)` | (gone; OpenSSL is internally thread-safe) | Not used in this tree. |

## Categories NOT used by this project

These are the typical Phase E migration targets we did **not** need to do
because nothing in the tree calls into them:

- **MD5**: no `MD5_Init` / `MD5_Update` / `MD5_Final` → would be `EVP_MD_CTX_new` + `EVP_DigestInit_ex` + `EVP_DigestUpdate` + `EVP_DigestFinal_ex` with `EVP_md5()`.
- **SHA1 / SHA-2**: no `SHA1_*` / `SHA256_*` calls.
- **HMAC**: no `HMAC_Init_ex` / `HMAC_Update` / `HMAC_Final` → would be `EVP_MAC_fetch("HMAC")` + `EVP_MAC_CTX_*`.
- **DES**: no `DES_set_key` / `DES_ecb_encrypt` calls.
- **RSA low-level**: no `RSA_new` / `RSA_free` / `RSA_generate_key_ex` / `RSA_public_encrypt` / `RSA_private_decrypt`. The Westwood RSA implementation is hand-rolled on top of BIGNUMs and does not touch the deprecated `RSA *` API.
- **ENGINE**: no `ENGINE_*` calls (deprecated and queued for removal; not in use).
- **PEM / X509 manual handling**: certificates are loaded via the high-level `SSL_CTX_use_certificate_file` / `SSL_CTX_use_PrivateKey_file`, which is not deprecated.

If any of these categories are added later (e.g. a real HMAC anywhere),
they should go straight to the `EVP_*` API — do not call the deprecated
low-level forms.

## How to verify

The project's `server/CMakeLists.txt` now defines
`OPENSSL_API_COMPAT=0x30000000L`. With that set, calling a deprecated
API produces a compile error (when `-Werror=deprecated-declarations` is
in effect) or warning otherwise. A green build under `cmake --build` is
sufficient evidence that no regression to deprecated APIs has slipped
in.

Per-file trial compile (no full configure needed; useful when iterating
on a single file):

```sh
cd server
g++ -c -DOPENSSL_API_COMPAT=0x30000000L -DUSE_OPENSSL -DLINUX -D__linux__ \
    -Wall -Wextra -Werror=deprecated-declarations -fpermissive \
    -Icompat -Isrc -Ithird_party -Isrc/LUA/lua/include \
    src/<file>.cpp -o /tmp/<file>.o
```
