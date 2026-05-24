# Phase O — OpenSSL 3.x for proxy + login-server (Phase E continuation)

## Scope

Phase E migrated **the server only** to OpenSSL 3.x (set `OPENSSL_API_COMPAT=0x30000000L` for `server/CMakeLists.txt`, rewrote the deprecated EVP / RAND / `RAND_pseudo_bytes` / `RAND_seed` call sites). Phase E left **proxy** and **login-server** on the OpenSSL 1.1 API compat shim:

> "Kept `OPENSSL_API_COMPAT=0x10100000L` because login-server isn't in the CMake build yet and still depends on `SSLv2_client_method` (removed in 1.1). The compat shim is the right level until Phase E continuation rewires those call sites."  
> — `plans/09-phase-i-dev-env.md:22`

`SSLv2_client_method` was removed in OpenSSL 1.1 — the fact that login-server still references it means it can't build against modern OpenSSL at all. Phase O fixes that.

## Items

- [ ] **login-server: drop `SSLv2_client_method`** — replace with `TLS_client_method()` (negotiates the highest mutually-supported TLS, which is what the client/server already use after Phase J's TLSv1.3 work). SSL2 has been broken-by-design since the 90s and unsupported by every modern OpenSSL.
- [ ] **proxy: audit and update EVP/RAND call sites** — same migration the server got in Phase E. Likely a smaller diff (proxy does less crypto than the server).
- [ ] **Add `login-server/Net7SSL/CMakeLists.txt`** that pins `OPENSSL_API_COMPAT=0x30000000L` once the call sites are clean, and integrates with the top-level build.
- [ ] **Add `proxy/CMakeLists.txt`** pin the same way.
- [ ] **Drop the 1.1 compat shim** from the top-level build and document the removal.
- [ ] **CI matrix**: confirm Ubuntu 24.04 OpenSSL 3.x build of proxy + login-server is green.

## Dependencies

- Phase M (Win32 elimination) — login-server has the same Win32 noise the server has; cleaner to fix once.
- Phase J (Net7SSL TCP/TLS handshake on Linux) — already done; this is just the API-level OpenSSL update.

## Definition of done

- `find . -name '*.cpp' -o -name '*.h' | xargs grep -l 'SSLv2_client_method'` returns nothing.
- `find . -name '*.cpp' -o -name '*.h' | xargs grep -l 'OPENSSL_API_COMPAT=0x10100000L'` returns nothing.
- Both proxy and login-server build clean against system OpenSSL on Ubuntu 24.04 (which ships OpenSSL 3.x).
