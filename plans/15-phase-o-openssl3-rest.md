# Phase O — OpenSSL 3.x for proxy + login-server (Phase E continuation)

## Scope

Phase E migrated **the server only** to OpenSSL 3.x (`OPENSSL_API_COMPAT=0x30000000L` in `server/CMakeLists.txt`, deprecated EVP / RAND / `RAND_pseudo_bytes` / `RAND_seed` call sites rewritten). At Phase E close, proxy and login-server were still pinned to the OpenSSL 1.1 compat shim because login-server referenced `SSLv2_client_method` (removed in 1.1).

## Reality check (audit done 2026-05-24)

Plan-file scope is now **stale** — the migration happened in pieces alongside Phase J (TLSv1.3 handshake work) and Phase E continuation, before this plan file was opened:

- `proxy/CMakeLists.txt:66` already defines `OPENSSL_API_COMPAT=0x30000000L`.
- `proxy/Dockerfile:16` already exports the same.
- `login-server/Net7SSL/CMakeLists.txt:80` already defines `OPENSSL_API_COMPAT=0x30000000L`.
- `login-server/Dockerfile:20` already exports the same.
- `find . -name '*.cpp' -o -name '*.h' | xargs grep -l 'OPENSSL_API_COMPAT=0x10100000L'` returns nothing.
- No top-level 1.1 compat shim exists anywhere in the build.

The remaining `SSLv2_/SSLv23_` references in the tree are either dead-code-walled or runtime-guarded:

| Site | Status |
|---|---|
| `login-server/Net7SSL/SSL_Listener.cpp:56` `SSLv23_server_method()` | Behind `#if OPENSSL_VERSION_NUMBER >= 0x10100000L` else — on modern OpenSSL the `TLS_server_method()` branch runs |
| `proxy/SSL_Connection.cpp:106` `SSLv23_server_method()` | Inside `#ifdef WIN32` file-level wall (the in-proxy HTTPS responder is Win32-only; on Linux the auth flow belongs to login-server's Net7SSL) |
| `proxy/ServerManager.cpp:390` `SSLv2_client_method()` | Inside `#ifdef WIN32` wall in `RegisterSectorServer`; Linux short-circuits to success at line 391 |
| `server/src/ServerManager.cpp:814` `SSLv2_client_method()` | Inside `#if 0` deadblock (entire `RegisterSectorServer` is dead code) |
| `server/src/openssl/ssl.h:138*` declarations | Vendored 2010 OpenSSL 1.0 header tree; see "Latent concern" below |

## Verification

Built proxy + login-server + the gtest suite from a clean tree against host OpenSSL 3.0.13 on 2026-05-24:

- `cmake --build /tmp/o-proxy-build` → `[22/22] Linking CXX executable net7proxy`, only `-Wmisleading-indentation` + `#pragma warning(disable:…)` non-fatal noise.
- `cmake --build /tmp/o-login-build` → `[17/17] Linking CXX executable net7ssl`.
- `ldd net7proxy` and `ldd net7ssl` both show `libssl.so.3 + libcrypto.so.3` (no 1.x linkage anywhere).
- `ctest --output-on-failure` → 23/23 pass; 9 env-gated tests skip cleanly without a live docker-compose stack.

## Items

- [x] login-server: drop `SSLv2_client_method`. Done in Phase J via the `OPENSSL_VERSION_NUMBER >= 0x10100000L` guard at `SSL_Listener.cpp:53`.
- [x] proxy: audit and update EVP/RAND call sites. No live EVP/RAND uses found in proxy/ — all OpenSSL machinery is in `SSL_Connection.cpp`, which is Win32-walled.
- [x] `login-server/Net7SSL/CMakeLists.txt`: pin `OPENSSL_API_COMPAT=0x30000000L`. Done (line 80).
- [x] `proxy/CMakeLists.txt`: pin `OPENSSL_API_COMPAT=0x30000000L`. Done (line 66).
- [x] Drop the 1.1 compat shim from the top-level build. No such shim exists.
- [x] CI matrix: confirm Ubuntu 24.04 OpenSSL 3.x build of proxy + login-server is green. Confirmed locally; CI matrix already runs `cmake --build` on both targets.

## Latent concern — defer to its own follow-up

`server/src/openssl/` is a 2010-vintage **vendored** OpenSSL 1.0 header tree (~1960-line `ssl.h`, 966-line `evp.h`, full crypto headers). It survives because:

1. `target_include_directories(net7 PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src" ...)` puts `server/src` before the system include path, so `#include "openssl/ssl.h"` (e.g. `server/src/ServerManager.cpp:24`) resolves to the vendored copy.
2. The two .cpp files that touch the vendored tree (`ServerManager.cpp` via `"openssl/ssl.h"`, `WestwoodRSA.cpp` via `<openssl/ssl.h>`) only call SSL/RSA APIs that are ABI-stable between OpenSSL 1.0 and 3.x — or the calls are walled off entirely (`ServerManager.cpp::RegisterSectorServer` is `#if 0`).
3. Phase M patched the vendored `opensslconf.h` to gate the `OPENSSL_SYSNAME_WIN32` define on `_WIN32` so the headers stop dragging `<windows.h>` in on Linux.

This works today but is a landmine: any future call site that exercises a 1.x-vs-3.x ABI-divergent struct (e.g. `EVP_MD_CTX`, `RSA`, `BIGNUM` internals) will silently corrupt memory because the header layout disagrees with the runtime library layout.

**Follow-up plan O+**: delete `server/src/openssl/` entirely, switch all `#include "openssl/..."` to `<openssl/...>`, rely on `find_package(OpenSSL 3.0 REQUIRED)` for headers and linkage. Scoped to one separate plan file because (a) needs an audit of every consumer in server/src and proxy/login-server, (b) requires re-running the full integration suite to catch any ABI-divergent corner case, (c) genuinely orthogonal to Phase O's "drop the 1.1 compat shim" goal.

## Definition of done

- [x] `find . -name '*.cpp' -o -name '*.h' | xargs grep -l 'SSLv2_client_method'` returns only Win32-walled / `#if 0` / vendored-header hits — no live Linux call sites.
- [x] `grep -r 'OPENSSL_API_COMPAT=0x10100000L' .` returns nothing.
- [x] Both proxy and login-server build clean against system OpenSSL on Ubuntu 24.04 (which ships OpenSSL 3.0.13).
- [x] Full gtest suite (23 tests) passes; 9 env-gated tests skip cleanly absent a live stack.
