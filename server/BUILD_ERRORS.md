# Phase B build error log

Running record of the build state. The Phase B goal is "best-effort Linux build"; this file tracks what got us to the current state and what remains.

## State as of wave 14

- Image: `enb-server-build:phase-b` built from `server/Dockerfile`
- Toolchain: g++ on Ubuntu 24.04, CMake 3.x, Ninja, OpenSSL 3.x (with `OPENSSL_API_COMPAT=0x10100000L`)
- All **compile** errors resolved. Compile-error trajectory across waves: ~600+ → 466 → 159 → 213 → 15 → 0.
- Link stage: in flux. Wave 13 surfaced mysqlclient, BN_init, kyp-era duplicate-cpp, and missing inter-server dispatcher symbols. Wave 14 addresses all four.

## Categories of fixes applied

### 1. Win32 → POSIX shims (server/src/Net7.h Linux block, server/compat/)

Typedefs and stubs for: `HANDLE`, `BOOL`, `DWORD`, `LPTSTR`, `TCHAR`, `TEXT/_T`, `__int64/__int32/__int16`, `WAIT_*` constants, `INFINITE`, `ERROR_*` constants, `GetLastError`, `CloseHandle`, `Sleep`, `_strnicmp` → `strncasecmp`, `sscanf_s` → `sscanf`, `sprintf_s`/`strcpy_s`/`strncpy_s`/`strcat_s`/`gmtime_s`/`fopen_s`/`memcpy_s`, `STARTUPINFO`, `PROCESS_INFORMATION`, `SYSTEMTIME`, `LPSECURITY_ATTRIBUTES`, `_beginthreadex`, `TerminateProcess`, `WriteFile`, `CreateMutex`, `CreateProcess`, `CreateMailslot`, `GetMailslotInfo`, `MAILSLOT_*`, `CreateFile`, `ReadFile`, `ResumeThread`, `OVERLAPPED`, `LPOVERLAPPED`, `LPDWORD`, `LPARAM/WPARAM`, `__try/__except/EXCEPTION_EXECUTE_HANDLER`, `SD_BOTH`, `ioctlsocket`, `MAXINT`, `_mkdir`, `lstrlen`, `_access`, `CreateEvent`/`SetEvent`/`ResetEvent`, `GetSystemTime`.

All wrapped in `#ifndef WIN32` so the existing Windows build is undisturbed.

### 2. POSIX socket struct compat

`in.S_un.S_addr` (WinSock) → `in.s_addr` (POSIX) in PlayerManager.cpp.

### 3. MSVC-isms in source

- `unsigned long(x)` constructor-style cast → `(unsigned long)(x)` (perl rewrite, 7 sites)
- `getcwd(...) < 0` → `== NULL`
- `STARTUPINFO si = {NULL};` → `STARTUPINFO si = {0};`
- 9-element initializer for 8-field struct → trimmed
- `BIGNUM M; BN_init(&M);` (OpenSSL 1.0 style) → `BIGNUM *M = BN_new(); … BN_free(M);` (OpenSSL 1.1+)

### 4. Missing kyp-era declarations / definitions

Code paths still reference symbols the tada-o refactor didn't fully wire up. Added no-op stubs so the binary links; runtime behavior in these paths will be wrong (silently no-ops):

- `Connection::SendObjectEffect` — header decl + empty body
- `Object::DamageMOB` — virtual no-op default
- `Player::SetTCPTerminate`, `ChangeProspectSkill`, `AddScanSkill`, `ChangeTractorBeamSpeed` — inline empty bodies
- `ServerManager::GetTCPCBuffer`, `GetConnection` — inline stubs (return `m_ReSendBuffer` and `nullptr`)
- `Connection::ProcessMasterServerToSectorServerOpcode`, `ProcessSectorServerToSectorServerOpcode` — empty bodies (the actual .cpp files are entirely `#if 0`'d out)

### 5. Header / file plumbing

- `MissionParser.h`: added `typedef std::vector<Mission*> MissionList;` (the upstream definitions were inside `/* … */` or `#if 0` blocks)
- `ServerManager.h`: added `ConnectionManager m_ConnectionMgr;` field
- `Equipable.h`: added `m_TimeNode` value-typed member, `RemoveTimeNode()` no-arg decl, `CheckForItem` decl
- `HulkClass.h`: corrected include path (`AuxClasses/AuxHulkIndex.h`) + class name (`AuxHulkIndex`)
- `Equipable.cpp:FinishInstall`: signature aligned with header's defaulted `int Slot = -1`
- `PlayerSkills.cpp:CheckMiningConditions`: hoisted `itembase` decl ahead of `goto bailout` to fix jump-crosses-initialization
- `CMobBuffs.cpp` / `PlayerMissions.cpp`: copied packed-struct fields into locals before passing to `pair<>` constructors (gcc's `-Waddress-of-packed-member` is fatal as-error)

### 6. Build system

- `CMakeLists.txt`:
  - GLOB exclusions for `*_DEP_.cpp` (deprecated copies that reference pre-rename headers)
  - GLOB exclusion for `CMobEquippable.cpp` (kyp-era duplicate of Equipable.cpp; both .cpp's define same Equipable methods, multiple-definition link error)
  - Added `find_library(mysqlclient)` + link
- `Dockerfile`: added `libmysqlclient-dev` (build), `libmysqlclient21` (runtime)
- `server/compat/windows.h`: created empty shim because vendored `server/src/openssl/rand.h` does `#include <windows.h>`

## Remaining known issues (handoff list)

Phase B's "best effort" stops here. Items that need follow-up work:

1. **Mailslot IPC** — `CreateMailslot`, `GetMailslotInfo` are stubbed to return `INVALID_HANDLE_VALUE` / 0. Inter-process master ↔ sector communication will not actually work. Phase B continuation needs to replace this with Unix domain sockets or POSIX message queues. Estimate: days, see `docs/10-modernization-roadmap.md`.
2. **CreateProcess / process spawning** — `LaunchNet7SSL` and similar paths are stubbed to no-op. The SSL/login process subprocess never starts. Replace with `fork`+`execvp` or use `posix_spawn`.
3. **Master/Sector dispatch** — the `Process(Master|Sector)ServerToSectorServerOpcode` cases are stubs; the actual handlers are wholesale `#if 0`'d out in upstream. tada-o's standalone-server mode may not exercise these, but multi-server deployment will need them revived.
4. **kyp-era Player methods** — `SetTCPTerminate`, `ChangeProspectSkill`, `AddScanSkill`, `ChangeTractorBeamSpeed` are no-ops. The skill/tractor-beam interactions need to be re-pointed at tada-o's replacement code paths.
5. **mysqlclient** — still linked. Phase C replaces this with libpqxx; the entire `server/src/mysql/mysqlplus.cpp` shim layer goes away.
6. **OpenSSL 3.x** — only `BN_init` was directly broken; the rest works under `OPENSSL_API_COMPAT=0x10100000L`. Phase E removes the compat shim and migrates EVP/SSL_CTX usage to native 3.x APIs.
7. **WestwoodRSA leak on early-return path** — added `BN_free` for both happy and error paths in `Decrypt`, but the pre-OpenSSL-3.x stack-allocated semantics were "free on scope exit"; verify no caller paths leak after the heap-allocation refactor.
