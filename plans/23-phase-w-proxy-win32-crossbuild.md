# Phase W — Proxy Win32 cross-build (so the proxy can run under WINE next to the client)

Goal: produce a working Win32 PE build of `Net7Proxy` so the launcher can spawn it as a sibling process inside the same WINE prefix as the EnB client. Linux native build remains the server-side deployment target; the Win32 build is what end users actually run on their machines.

## Why

- The EnB client is a Win32 binary. End users run it under WINE on Linux, or natively on Windows. Either way, the client lives in a Windows-API world.
- The original kyp-era proxy was a client-side launcher (`StartENBClient` / Detours / `WriteProcessMemory`). Phase M deleted those parts and made the proxy a server-side Linux daemon. That's still the right shape for server operators — but end users can't ship a Linux ELF next to a Win32 client.
- Shipping the proxy as a Win32 PE that runs inside the same WINE prefix solves it: launcher spawns `Net7Proxy.exe`, both processes share the prefix's loopback, the client talks to `localhost:3801` exactly as it did historically.
- The proxy stays **the same binary** logically — same code, same protocol, same opcode handlers. Only the build target changes.

## Scope (single wave, 2026-05-27)

- [x] **MinGW-w64 toolchain + winpthreads.** Cross-compile with `x86_64-w64-mingw32-g++-posix` (gcc 13, MinGW-posix variant — provides the POSIX pthread API on Windows so the pthread-based `Mutex` / listener threads compile without a Win32 rewrite).
- [x] **Static OpenSSL 3 for MinGW.** `proxy/third_party/openssl-3.0.16` source tree, configured with `./Configure mingw64 --cross-compile-prefix=x86_64-w64-mingw32- no-shared no-asm no-tests`, installed to `proxy/third_party/openssl-mingw64/` (lib64/libssl.a + libcrypto.a + include/openssl/). Static so the resulting `.exe` doesn't drag MinGW SSL DLLs around at runtime.
- [x] **`proxy/cmake/mingw-w64-x86_64.toolchain.cmake`.** New toolchain file: `CMAKE_SYSTEM_NAME=Windows`, the MinGW-posix tool prefix, `CMAKE_FIND_ROOT_PATH` covering `/usr/x86_64-w64-mingw32` + the local OpenSSL prefix, `-static -static-libgcc -static-libstdc++` linker flags.
- [x] **`proxy/CMakeLists.txt`: Win32 branch.** Detects cross-build via `WIN32` (genuine CMake variable, not the source-code macro), points OpenSSL at the local static prefix, links `ws2_32` / `crypt32` / `iphlpapi` / `pthread`, defines `_WIN32_WINNT=0x0601` + `WIN32_LEAN_AND_MEAN`, sets `OUTPUT_NAME=Net7Proxy` (capital N — what the launcher spawns). Linux branch unchanged.
- [x] **`proxy/Net7.h` POSIX shim layer.** Top-of-file `#ifdef _WIN32` branch pulls in `winsock2.h` / `ws2tcpip.h` / `windows.h` / `io.h` / `process.h` / MinGW's `unistd.h` + `pthread.h` and defines the small set of shims the codebase needs (`strcasecmp` → `_stricmp`, `MSG_NOSIGNAL` → 0, `SHUT_RD/WR/RDWR` → `SD_RECEIVE/SEND/BOTH`, `in_addr_t` → `ULONG`, `setenv` → `_putenv_s`). The `#else` branch retains the existing POSIX-native includes.
- [x] **`WIN32` → `NET7_LEGACY_WIN32` rename across `proxy/*.cpp` + `proxy/*.h`.** Source-code discriminator for the dead 2010-era client-launcher branches was named `WIN32`, which collides with `minwindef.h`'s unconditional `#define WIN32` (windows.h's transitive include re-defines it after `-UWIN32`). Renamed to `NET7_LEGACY_WIN32`, which `windows.h` never touches. CMake's Win32 branch leaves `NET7_LEGACY_WIN32` undefined — the legacy code stays dead on both Linux and Win32.
- [x] **`common/include/net7/Mutex.h` → unconditional pthread.** Header used to `#ifdef WIN32` between `CRITICAL_SECTION` and `pthread_mutex_t`. On the MinGW build the header saw one state and the `.cpp` saw the other (header/body skew via the same `windows.h` re-define above). Made the header unconditionally pthread; winpthreads gives us the same API on Windows. `proxy/Mutex.cpp` rewritten on the unconditional path (uses `pthread_equal` instead of `==` because `pthread_t` is a struct on winpthreads).
- [x] **Per-`.cpp` POSIX-include hygiene.** Removed direct `<sys/socket.h>` / `<arpa/inet.h>` / `<unistd.h>` etc. from `Connection.cpp` / `ClientToServer_linux_stubs.cpp` / `UDPClient_linux.cpp` / `UDPProxyToClient_linux.cpp` — `Net7.h`'s platform-guarded branch already pulls them in on Linux, and the direct includes fail to resolve on Win32.
- [x] **`WSAStartup(MAKEWORD(2,2))` at the top of `Net7.cpp::main`.** Winsock requires it before any `socket()` / `bind()` — without it every call fails with `WSANOTINITIALISED`, which surfaces as `bind() "Invalid argument"`. Gated `#ifdef _WIN32`.
- [x] **Banner text.** Dropped "(server-side, Linux)" from the proxy banner — same binary now runs on both Linux and WINE/Win32.
- [x] **WINE smoke test.** `wine Net7Proxy.exe` prints banner, calls WSAStartup, binds TCP 3801 + 3805 (verified LISTEN via `ss -ltn`), opens both UDP planes (3808 + 3810 unconnected default peer). `NET7_UPSTREAM_HOST=enb.sigsegv.land wine Net7Proxy.exe` and `wine Net7Proxy.exe /UPSTREAM:test.example.com` both propagate the upstream-host string through to the resolver fallback path.
- [x] **Linux native build still clean.** `cmake -B build -S . && cmake --build build` produces a working `proxy/build/net7proxy` ELF — the shared `Mutex.h` change + Net7.h split was a strict no-op on Linux. Server (which also includes `common/include/net7/Mutex.h`) rebuilds cleanly.

## Non-goals (deliberately deferred)

- **Bundling MinGW OpenSSL into git.** The 9.6 MB static prefix is reproducible from `proxy/third_party/openssl-3.0.16` source + the documented `./Configure mingw64` command. Add a `tools/build-openssl-mingw.sh` helper if cold-clone friction becomes a problem; for now the build script is in this plan file.
- **WINE CI matrix.** Linux gets CI; WINE smoke is manual for now (the WINE prefix setup time + `dotnet test` aren't worth it for one binary).
- **Win32-only feature work.** The proxy behaves identically under WINE and Linux; no separate code paths beyond what's listed above. Anything Win32-specific (registry, COM, etc.) lives in the launcher, not the proxy.
- **Tools, server, login-server.** Out of scope. The rest of the C++ tree stays Linux-only; tools stay .NET-Linux-friendly. Only the proxy needs to ship next to a WINE client.

## Verification

```
# Linux native
$ cmake --build proxy/build
[23/23] Linking CXX executable net7proxy

# MinGW cross
$ cmake --build proxy/build-win64
[100%] Built target net7proxy   # OUTPUT_NAME = Net7Proxy.exe (PE32+ x86-64, statically linked)

$ file proxy/build-win64/Net7Proxy.exe
PE32+ executable (console) x86-64, for MS Windows, 19 sections

# WINE smoke
$ wine proxy/build-win64/Net7Proxy.exe
Net7Proxy version 1.74
Net7Proxy: binding TCP 3801 (MASTER_SERVER_PORT) on 127.0.0.1
Net7Proxy: binding TCP 3805 (GLOBAL_SERVER_PORT) on 127.0.0.1
UDPClient: bound UDP <ephemeral> -> 127.0.0.1:3808 (connected peer)
UDPClient: bound UDP <ephemeral> -> 127.0.0.1:3810 (unconnected default peer)

$ ss -ltn '( sport = :3801 or sport = :3805 )'   # while WINE proxy is up
LISTEN 0 4096 0.0.0.0:3805 0.0.0.0:*
LISTEN 0 4096 0.0.0.0:3801 0.0.0.0:*
```

## Status

Complete (single wave landed 2026-05-27).

## How to rebuild the OpenSSL prefix (operator-side)

```
# from inside proxy/third_party/openssl-3.0.16 (extract from upstream tarball)
./Configure mingw64 \
    --cross-compile-prefix=x86_64-w64-mingw32- \
    --prefix=$PWD/../openssl-mingw64 \
    no-shared no-asm no-tests
make -j$(nproc)
make install_dev
```

The result is `proxy/third_party/openssl-mingw64/{lib64/libssl.a,lib64/libcrypto.a,include/openssl/}`. The CMake Win32 branch points at this prefix directly; no extra config.

## How to build the proxy for WINE

```
cd proxy
cmake -B build-win64 -S . \
    -DCMAKE_TOOLCHAIN_FILE=cmake/mingw-w64-x86_64.toolchain.cmake \
    -DCMAKE_BUILD_TYPE=Release
cmake --build build-win64
# output: proxy/build-win64/Net7Proxy.exe
```
