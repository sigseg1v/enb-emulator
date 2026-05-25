# Phase M — eliminate Win32 from server-native code

User directive: "we want to get rid of the windows shit." Scope correction (also user): "boost we should be able to use in linux with the proper headers right? dont rewrite boost dear god no. Also its in detours, which is under the client folder. The client stuff runs in wine and shouldnt be part of the migration... when we say 'run on linux' we mean the server runs natively on linux and the client runs on win32 and linux via wine so client can still use windows stuff."

## Honest assessment of what Phase B left behind

Phase B closed when the server **compiled and linked** as a Linux ELF. It did NOT close because Win32 was eliminated. Phase B introduced the `compat/` shim layer specifically as a "make it compile" expedient. That layer is **still in active use** today:

- `server/compat/`: 10 files (mailslot_shim, posix_ipc, threading_shim, win32_shim.h, windows.h, README.md, WIN32_INVENTORY.md)
- `proxy/compat/`: 3 files (threading_shim.{cpp,h}, win32_shim.h)
- `login-server/Net7SSL/compat/`: 3 files (threading_shim.{cpp,h}, win32_shim.h)
- `server/src/Net7.h:138-300`: ~160 lines of `typedef`s and `static inline` stubs (`HANDLE`, `CreateMutex`, `CreateEvent`, `CreateMailslot`, `_beginthreadex`, etc.) that compile-but-do-nothing
- The 4 STUB shims listed in `server/compat/README.md` ("**NO**" runtime correctness): `CreateMutex`, `CreateEvent`, `CreateMailslot`, `GetMailslotInfo`

Concrete metric (`git grep` across server-native code, *excluding* vendored deps + the shim layer itself):

| Symbol | Hits in server-native call sites |
|---|---|
| HANDLE | 91 |
| DWORD | 98 |
| LPTSTR | 23 |
| _beginthreadex | 16 (8 call sites — declarations + call) |
| CreateMutex | 8 |
| CreateEvent | 12 |
| CreateMailslot | 10 (4 call sites — declarations + call) |
| GetMailslotInfo | 7 |
| WaitForSingleObject | 8 |
| _snprintf | 59 |
| _stricmp / stricmp | 77 |
| WSAStartup | 3 |
| SOCKET | 160 |
| **Total** | **~580** |

(Earlier estimates of "295" and "597" were both wrong — the first under-counted because it only looked at `server/src/`, the second over-counted because it included vendored MySQL Connector/C headers and lualib.h. With the correct user-defined scope — server-native only, vendored excluded — the real number is ~580 hits across ~25 files.)

This is the technical debt Phase B left. Phase M pays it down.

## Scope (per user)

**IN scope** (must run natively on Linux, must not need Win32 shims):
- `server/src/` (excluding vendored: `mysql/`, `LUA/`, `cryptopp/`, `zlib/`)
- `server/third_party/` — **NO**, this is consumed libraries, leave alone
- `login-server/Net7Mysql/` (excluding `mysql/` subdir)
- `login-server/Net7SSL/`
- `proxy/`
- `server/compat/`, `proxy/compat/`, `login-server/Net7SSL/compat/` — the *goal* is to delete these, so they're the target of the work but not the target of API replacement

**OUT of scope** (Win32 is allowed):
- `client/**` — runs under WINE or on Windows. Win32 is correct here.
  - `client/detours/` — Microsoft Detours by definition is Win32 hooking. Leave alone.
  - `client/mods/` — client-side patches. Leave alone.
  - `client/linux-installer/` — bash + WINE prefix setup. Already Linux; no C++.
- `server/third_party/` — boost, cryptopp, zlib, lua, MySQL Connector/C. We consume these. Boost on Linux compiles via boost's own POSIX backend; we just need to make sure we're including the right boost headers, not the bundled Win32 ones (if any).
- vendored headers inside server/src/ — `server/src/mysql/`, `server/src/LUA/`. These are upstream code; their Win32 ifdefs never compile on Linux anyway.

## Definition of done

`server/compat/` and the compat subdirs in `proxy/` and `login-server/Net7SSL/` are **deleted**. `server/src/Net7.h:168-300` is **removed** (the Win32 typedef block). CMakeLists no longer globs `compat/*.cpp` or adds `compat/` to the include path. The following grep returns **zero** (with the same exclusions as the audit above):

```
git grep -nE 'HANDLE\b|DWORD\b|LPTSTR|_beginthreadex|CreateMutex|CreateEvent|CreateMailslot|GetMailslotInfo|WaitForSingleObject|_snprintf\b|_stricmp|stricmp\b|WSAStartup|SOCKET\b' \
  -- server/src/ login-server/Net7Mysql/ login-server/Net7SSL/ proxy/ \
  ':!server/src/mysql/**' ':!server/src/LUA/**' ':!server/src/cryptopp/**' \
  ':!server/src/zlib/**' ':!server/third_party/**' \
  ':!server/compat/**' ':!proxy/compat/**' ':!login-server/Net7SSL/compat/**' \
  ':!login-server/Net7Mysql/mysql/**'
```

Server still builds. Existing Phase J integration tests (5/5 green at Phase J close) still pass. Existing Phase K opcode round-trip tests (8/8 green at Phase K's last checkpoint) still pass.

## Items

### Cheap trivial replacements (do first — high impact, low risk)

- [x] **`_snprintf` → `snprintf`** (52 hits across 8 files). Identical signature on Linux. Sed sweep then verified `git grep` returns zero call sites (Net7.h's `#define _snprintf snprintf` macro retired in the same pass). Server rebuilds clean.
      Status: complete
      Touches: server/src/{FieldClass,PlayerConnection,PlayerExperience,PlayerMissions,SectorManager}.cpp, proxy/ServerManager.cpp, login-server/Net7SSL/{Net7SSL.cpp,Net7SSL.h}

- [x] **`stricmp` / `_stricmp` / `_strcmpi` → `strcasecmp`** (47+30 hits + ~13 `_strcmpi`). Identical semantics on POSIX. Sed sweep across 11 files. Net7.h's `#define stricmp strcasecmp`, `#define _strcmpi strcasecmp`, `#define _stricmp strcasecmp` macros retired. Server rebuilds clean.
      Status: complete
      Touches: server/src/{AccountManager,AssetDatabaseSQL,ConnectionManager,Net7,PlayerConnection,PlayerManager,PlayerMisc,SectorContentSQL,xmlParser/xmlParser_}.cpp, login-server/Net7SSL/{ConnectionManager.cpp,Net7SSL.{cpp,h}}

- [x] **`_atoi64` → `atoll`** (3 call sites in server/src/ClientToSectorServer.cpp, server/src/PlayerConnection.cpp, login-server/Net7SSL/Net7SSL.h). MSVC name retired from Net7.h.
      Status: complete

- [x] **`_beginthreadex` → `pthread_create`** — Linux-active call sites already use `pthread_create` directly. The remaining `_beginthreadex` hits in `proxy/Connection.cpp:400`, `login-server/Net7SSL/{Connection,SSL_Connection}.cpp` all live inside `#ifdef WIN32` walls (Phase J file-level guards). Dead code on Linux; deferred to Phase K when those TUs are unwalled.
      Status: complete for Linux-active code; walled sites tracked under Phase K
      Touches: server/src/{ConnectionManager,SectorManager,SSL_Connection}.cpp (no _beginthreadex; thread spawn done with pthread_create), proxy/Connection.cpp (walled)

- [x] **Drop `CREATE_SUSPENDED` semantics** — walled-out only on Linux; deferred to Phase K with the surrounding TU port.
      Status: complete for Linux-active code; walled sites tracked under Phase K

### Mailslot → AF_UNIX SOCK_DGRAM (already partly done in Phase J)

- [x] **Rewrite `server/src/MailslotManager.cpp` directly against `posix_ipc.{h,cpp}`**. Done — `MailManager` now holds a `net7ipc::PosixIpc*` and forwards `WriteMessage`/`ReadMessage`/`Reset` straight through. The `HANDLE m_hSlot`/`m_hFile`/`m_hEvent` fields and the `#ifdef WIN32 ... #endif` wall in the .cpp are gone.
      Status: complete
      Touches: server/src/MailslotManager.{h,cpp}

- [x] **Rewrite `login-server/Net7SSL/MailslotManager.cpp`** the same way. Done — mirror of the server-side rewrite; added the missing `HandleMessage()` impl that the Win32 side had.
      Status: complete
      Touches: login-server/Net7SSL/MailslotManager.{h,cpp}

- [x] **Delete `server/compat/mailslot_shim.{cpp,h}`** — done. No live or walled callers of `CreateMailslot`/`GetMailslotInfo` remain after the mailslot rewrites.
      Status: complete

### Mutex (one-instance guard) → flock pid file

- [x] **Replace `CreateMutex(...)` instance-guard pattern**. Replaced with `net7ipc::SingleInstance` (RAII `flock()` on a pid file under `/run/enb-emulator/<app>.pid`). Server and Net7SSL both use it. The Win32 named-mutex semantics (cross-process named handle) is honored as a single flock per host.
      Status: complete
      Touches: common/include/net7/SingleInstance.h, server/src/Net7.cpp, login-server/Net7SSL/Net7SSL.cpp

### Event-loop signaling → eventfd or condvar

- [x] **Replace `CreateEvent` / `WaitForSingleObject(event, timeout)` patterns**. Audit (git grep -nE 'CreateEvent\b|WaitForSingleObject\b|SetEvent\b|ResetEvent\b' across server-native code, excluding vendored) returns **zero live call sites** — only one comment hit in server/src/Net7.h listing retired Win32 stubs. The 12+8 hit count from the prior estimate counted comment references and walled-out branches. All live event-loop signaling in the server is already either `pthread_cond_t` (e.g. SaveManager save-queue wake) or std::condition_variable. No work needed.
      Status: complete
      Touches: (audit only)

### SOCKET → int

- [ ] **`SOCKET` → `int`** (~160 hits, but mostly typedef aliases / function signatures, not real Win32 socket calls). Remove `typedef int SOCKET;` from Net7.h + compat/win32_shim.h, then let the compiler walk the call sites with us. Look for `INVALID_SOCKET` (replace with `-1`), `closesocket` (replace with `close`), `WSAStartup`/`WSACleanup` (delete; not needed on Linux), `SOCKET_ERROR` (replace with `-1`).
      Status: not started

### DWORD / LPTSTR / LPSTR / HANDLE → real types

- [ ] **`DWORD` → `uint32_t` or `unsigned int`**, audit per call site (some are MSDN-handle-style, some are just "unsigned 32-bit").
      Status: not started

- [ ] **`LPTSTR` / `LPSTR` → `char*`**. ANSI vs Unicode distinction is meaningless on Linux. ~29 hits.
      Status: not started

- [ ] **`HANDLE` field declarations** — remove once all the above is done. There should be no remaining real Win32 handles; the typedef in Net7.h becomes dead and can be deleted.
      Status: not started

### Cleanup

- [x] **Delete `server/compat/`** directory. Done.
      Status: complete

- [x] **Delete `proxy/compat/`** directory. Done.
      Status: complete

- [x] **Delete `login-server/Net7SSL/compat/`** directory. Done. The minimum needed Win32 typedef set (SOCKET, HANDLE, DWORD, etc.) and helpers (Sleep, GetTickCount) was inlined into the respective `Net7.h` / `Net7SSL.h` umbrella headers instead, so legacy call sites typecheck without a separate compat tree.
      Status: complete

- [x] **Strip the Linux-side Win32 typedef block from `server/src/Net7.h`, `proxy/Net7.h`, `login-server/Net7SSL/Net7SSL.h`** (the SOCKET/HANDLE/DWORD/LPTSTR/LPVOID typedefs + Sleep/GetTickCount helpers). Pre-req: rewrite every Linux-active call site of those symbols to POSIX equivalents.
      Status: complete — all three umbrella headers stripped (Waves 1-4). Wave 1 (dc5cfe4 + 2e9937b): behavioral helpers — `Sleep(DWORD)` and `GetTickCount()` retired across all three; `Net7TickMs()` lives in `common/include/net7/Ticks.h`; call sites use `usleep(ms * 1000)` directly; `WSAGetLastError()` → `errno` swept. Wave 3 (2538444): proxy/Net7.h — replaced 4 Linux-active sites (WORD→uint16_t in TcpListener.h/SSL_Listener.h, BOOL/TRUE→int/1 in TcpListener.cpp, SOCKADDR_IN→struct sockaddr_in in UDPClient.h, engine_read_process LPVOID/DWORD→void*/uint32_t), then deleted ~25 unused aliases. Wave 4 (this commit): same pattern on server/src/Net7.h (replaced 23 Linux-active uses across UDPConnection.cpp/MessageQueue.h/XmlParser.{cpp,h}: ULONG→unsigned long, UCHAR→unsigned char, UINT→unsigned int, DWORD→uint32_t, SD_BOTH→SHUT_RDWR; stripped BSTR/LPSTR/LPCSTR/LPTSTR/LPCTSTR/LPVOID/LPCVOID/TCHAR/TEXT/_T/_TEXT/WPARAM/LPARAM/WAIT_TIMEOUT/WORD/DWORD/BOOL/ULONG/UINT/UCHAR/TRUE/FALSE typedefs) AND login-server/Net7SSL/Net7SSL.h (replaced WORD×2/SOCKADDR_IN×1 in SSL_Listener.h/TcpListener_B.h/UDPClient.h; stripped ~22 unused typedefs, kept SOCKET/INVALID_SOCKET/SOCKET_ERROR/WAIT_TIMEOUT since connection_B.h:82 uses it as default arg). All three umbrella headers now expose only SOCKET/INVALID_SOCKET/SOCKET_ERROR + canonical socket macros + the behavioral helper inlines. Remaining typedef uses across server-native code are all inside `#ifdef WIN32` walls.
      Touches: common/include/net7/Ticks.h (new), server/src/Net7.{cpp,h}, server/src/UDPConnection.cpp, server/src/MessageQueue.h, server/src/XmlParser.{cpp,h}, proxy/Net7.{h,cpp}, proxy/TcpListener.{h,cpp}, proxy/SSL_Listener.h, proxy/UDPClient.h, login-server/Net7SSL/Net7SSL.h, login-server/Net7SSL/SSL_Listener.h, login-server/Net7SSL/TcpListener_B.h, login-server/Net7SSL/UDPClient.h (commits dc5cfe4 + 2e9937b + 2538444 + Wave 4)

- [x] **Strip CMakeLists.txt references**: compat globs and include paths removed from `server/CMakeLists.txt`, `proxy/CMakeLists.txt`, `login-server/Net7SSL/CMakeLists.txt`. The shared `common/PosixIpc.cpp` is now compiled directly into server and login-server (proxy doesn't need IPC).
      Status: complete

- [ ] **Re-run the audit grep** (see "Definition of done") and confirm zero hits.
      Status: in progress — currently 223 hits; ~140 are inside `#ifdef WIN32` walls (dead on Linux), ~25 are in vendored headers (xmlParser, openssl/bio.h), ~58 are in live code. Genuine cleanup pending the vocabulary sweep above.

- [x] **Re-run Phase J + Phase K integration tests**, confirm green. 20/20 ctest passing after every Phase M commit.
      Status: complete

### Documentation

- [x] **Update `server/compat/README.md`** before deletion. Done — the README went away with the directory.
      Status: complete

- [x] **Update `docs/02-architecture.md`** if it references the compat layer. Done — the headers-shared-across-trees section now says "Phase M dissolved the separate compat/ directories" and describes the inlined typedef/helper set per umbrella header.
      Status: complete
      Touches: docs/02-architecture.md

- [x] **Update `docs/08-build.md`** to drop any mention of the compat layer. Done — the Phase B compat-shim paragraph is replaced with a pointer to the dissolved-in-Phase-M state and the `opensslconf.h` Win32-gating patch.
      Status: complete
      Touches: docs/08-build.md

- [x] **Append a decisions-log entry** capturing the Phase B → Phase M transition. Done — see `99-decisions-log.md` entry "2026-05-23 — Phase M dissolves the compat/ tree; Phase B → M scope handoff documented".
      Status: complete
      Touches: plans/99-decisions-log.md

## Decisions

- **Boost stays**. Per user: "dont rewrite boost dear god no." Boost on Linux is a different backend behind the same `boost::interprocess::*` API; we use it as a library, not as code to rewrite.
- **client/detours stays Win32**. Microsoft Detours is by definition a Win32 API hooking library. The client runs in WINE; Win32 is correct there.
- **Per-instance guards become flock**, not pthread mutexes. CreateMutex's Win32 semantics are *cross-process*; a process-local pthread mutex would silently regress to "no guard". flock on a pid file is the conventional Linux equivalent.
- **Mailslots become AF_UNIX SOCK_DGRAM**, reusing the already-shipped `server/compat/posix_ipc` from Phase J. Don't reinvent.
- **Don't preserve the Win32 API surface**. Phase B added "stub functions that compile-but-do-nothing" to get the build green. Phase M removes them outright. Call sites must use the real POSIX primitive directly — no thin wrappers.
- **CREATE_SUSPENDED pattern is dropped**. Win32 lets you `_beginthreadex` a thread suspended and resume it later. pthreads has no equivalent and the use cases in this codebase are all "create + immediately resume." Replace with plain create.

## Anti-scope (do not do)

- **Don't touch `client/**`**. Win32 there is correct.
- **Don't touch `server/third_party/**`**. Consumed libraries, not ours.
- **Don't touch `server/src/mysql/**` or `server/src/LUA/**`**. Vendored upstream code.
- **Don't write a "POSIX-ish HANDLE" shim**. Phase M's whole point is removing the indirection.
- **Don't try to keep cross-platform compile parity** — this is a server-native-Linux project now. Win32 builds of the server are not maintained.
