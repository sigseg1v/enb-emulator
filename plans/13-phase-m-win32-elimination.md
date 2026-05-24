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

- [ ] **`_beginthreadex` → `pthread_create`** (8 call sites). Replace `m_*Thread = (HANDLE)_beginthreadex(...)` with `pthread_create(&m_*Thread, NULL, ...)`. Change the field type from `HANDLE` to `pthread_t`. Change the thread-fn signature from `unsigned __stdcall (*)(void*)` returning unsigned → `void* (*)(void*)` returning `void*`. Sites:
      - server/src/ConnectionManager.cpp:24
      - server/src/SectorManager.cpp:76
      - server/src/SSL_Connection.cpp:38,55
      - login-server/Net7SSL/Connection.cpp:40,51
      - login-server/Net7SSL/SSL_Connection.cpp:42,59
      - proxy/Connection.cpp:400 (SocketSendThread)
      Status: not started

- [ ] **Drop `CREATE_SUSPENDED` semantics** — three call sites pass `CREATE_SUSPENDED` then immediately `ResumeThread()`. pthread_create starts running immediately; just drop the suspend/resume pair. If a real race exists between thread-start and parent-state-setup, fix with a condvar instead of suspend.
      Status: not started

### Mailslot → AF_UNIX SOCK_DGRAM (already partly done in Phase J)

- [ ] **Rewrite `server/src/MailslotManager.cpp` directly against `posix_ipc.{h,cpp}`** (already a real impl from Phase J). Remove the `HANDLE m_hSlot` field, replace with `net7ipc::PosixIpc*` or just an `int fd`. Remove the `CreateMailslot(...)` → `AsIpc(HANDLE)` indirection. Net result: MailslotManager becomes a thin wrapper over posix_ipc and the Win32 mailslot shim path is unreachable.
      Status: not started

- [ ] **Rewrite `login-server/Net7SSL/MailslotManager.cpp`** the same way. Currently it still uses `CreateMailslot` directly (Phase J wired posix_ipc into the server only, not the SSL login server).
      Status: not started

- [ ] **Delete `server/compat/mailslot_shim.{cpp,h}`** once nothing references CreateMailslot/GetMailslotInfo.
      Status: not started

### Mutex (one-instance guard) → flock pid file

- [ ] **Replace `CreateMutex(...)` instance-guard pattern**. Three call sites (`server/src/Net7.cpp:371,456`, `login-server/Net7SSL/Net7SSL.cpp:125`). Replace with `flock()` on a per-app pid file under `/run/enb-emulator/<app>.pid` (fall back to `/tmp/` if `/run` not writable). Drop the named-mutex semantics entirely — single-instance enforcement on a host is what flock is for.
      Status: not started

### Event-loop signaling → eventfd or condvar

- [ ] **Replace `CreateEvent` / `WaitForSingleObject(event, timeout)` patterns**. 12 + 8 hits but only ~3 distinct usage patterns:
      1. "wake me when there's work" → use `pthread_cond_t` + mutex, or eventfd if cross-thread fd-poll is needed.
      2. "wait until thread is ready" → drop with the CREATE_SUSPENDED cleanup above.
      3. "timed wait then poll" → `pthread_cond_timedwait` or just sleep + check.
      Audit each site; pick the simplest primitive per pattern.
      Status: not started

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

- [ ] **Delete `server/compat/`** directory.
      Status: not started

- [ ] **Delete `proxy/compat/`** directory.
      Status: not started

- [ ] **Delete `login-server/Net7SSL/compat/`** directory.
      Status: not started

- [ ] **Strip `server/src/Net7.h:168-300`** (the entire Win32 typedef + stub block, plus the `#ifdef _WIN32 #include <windows.h>` block that no longer has anything to gate).
      Status: not started

- [ ] **Strip CMakeLists.txt references**: `server/CMakeLists.txt` lines that glob `compat/*.cpp` or add `compat/` to include path. Same for proxy + login-server cmakelists.
      Status: not started

- [ ] **Re-run the audit grep** (see "Definition of done") and confirm zero hits.
      Status: not started

- [ ] **Re-run Phase J + Phase K integration tests**, confirm 5/5 + 8/8 still green.
      Status: not started

### Documentation

- [ ] **Update `server/compat/README.md`** before deletion to read "Phase M eliminated this directory; see plans/13-phase-m-win32-elimination.md" if anything still links to it, then delete.
      Status: not started

- [ ] **Update `docs/02-architecture.md`** if it references the compat layer.
      Status: not started

- [ ] **Update `docs/08-build.md`** to drop any mention of the compat layer.
      Status: not started

- [ ] **Append a decisions-log entry** capturing the Phase B → Phase M transition (Phase B was scoped to "compiles + links," Phase M finishes the job).
      Status: not started

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
