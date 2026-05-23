# Phase B — best-effort Linux server build

Goal: get the C++ server building on Linux as far as practical in one invocation. The codebase is ~162K LOC, Windows-targeted, 2010-vintage. We will not finish. Commit partial work and a remaining-errors doc.

## Approach

1. Inventory Windows-only API usage with `grep`. Build the shim layer in `server/compat/` first so the rest of the codebase has a working `#include` story.
2. Add `cmake --build` to the CI workflow with `continue-on-error: true` so progress is visible across commits.
3. Iterate: pick the most common compile error class, fix or shim it, rebuild, repeat. Stop at diminishing returns (when each fix unblocks <5 files).
4. Commit after every coherent fix wave. Update plan with the running error count.

## Items

### B1 — Inventory Windows-isms

- [ ] Run `grep -rno -E "_beginthreadex|WaitForSingleObject|CreateMailslot|CreateMutex|CreateEvent|LPTSTR|WIN32|HWND|HANDLE|Sleep\(|_snprintf|stricmp|InterlockedIncrement|Sleep\b" server/src` and bucket counts into `server/compat/WIN32_INVENTORY.md`.
      Touches: server/compat/WIN32_INVENTORY.md
      Notes:
- [ ] Identify mailslot IPC sites (`CreateMailslot`, `CreateFile.*mailslot`) — these need a real replacement strategy (Unix domain sockets / message queues), not just a typedef shim.
      Touches: server/compat/WIN32_INVENTORY.md
      Notes:

### B2 — Compat shims

- [ ] Extend `server/compat/win32_shim.h` with the obvious typedef-only shims: `LPTSTR`, `DWORD`, `HANDLE`, `BOOL`, `TRUE/FALSE` macros, `Sleep(ms)` → `usleep(ms*1000)`, `_snprintf` → `snprintf`, `stricmp` → `strcasecmp`.
      Touches: server/compat/win32_shim.h
      Notes:
- [ ] Add `server/compat/threading_shim.h` — `_beginthreadex`, `WaitForSingleObject`, `CreateMutex`, `CreateEvent` thin wrappers over pthreads / std::thread.
      Touches: server/compat/threading_shim.h
      Notes:
- [ ] Add `server/compat/mailslot_shim.{h,cpp}` — stub initially (returns sentinel) so files including `MailslotManager.h` compile. Real implementation deferred.
      Touches: server/compat/mailslot_shim.h, .cpp
      Notes:

### B3 — Build iteration

- [x] `cmake -S server -B build/server -G Ninja` succeeds (configure step).
      Touches: server/CMakeLists.txt
      Notes: Done in earlier waves; runs cleanly inside the build docker image.
- [x] `cmake --build build/server` produces first error log. Capture in `server/BUILD_ERRORS.md` with grouping by error class.
      Touches: server/BUILD_ERRORS.md
      Notes: First wave logged ~600+ errors; categorized and worked down across waves 1-12.
- [x] Iterate fix waves. Commit after each wave with running count: "Phase B: build errors X → Y".
      Touches: server/src/, server/compat/
      Notes: Trajectory: 600+ → 466 → 159 → 213 → 15 → 0 compile errors. As of wave 12 (commit 9090889) ALL compile errors resolved; build proceeds to link step. Link errors are now mysqlclient symbols, addressed by linking libmysqlclient via wave 13.

### B4 — Stop conditions

- [x] Stop iterating when: (a) server links, (b) error count plateaus across 3 consecutive waves, or (c) context budget low.
      Notes: **Met condition (a): wave 15 produces a 14 MB `net7` ELF that prints its usage text. Commit aa5fd2c.**
- [x] Final `BUILD_ERRORS.md` update enumerating remaining error classes and a hand-off list for Phase B continuation in a future invocation.
      Touches: server/BUILD_ERRORS.md
      Notes: Written; lists 7 categories of remaining hand-off work (mailslot IPC, CreateProcess, kyp-era stubs, mysqlclient, OpenSSL 3.x compat, etc.).

## Verification (Phase B done for now when)

- [x] `cmake -S server -B build/server` configures cleanly. (Done inside docker build.)
- [x] `cmake --build build/server` runs to completion. (Wave 15: `[178/178] Linking CXX executable net7`.)
- [x] `server/BUILD_ERRORS.md` committed. (`WIN32_INVENTORY.md` skipped — inventory ended up inline in the Net7.h shim block rather than a separate file.)
- [x] Master plan updated. (Phase B → complete in 00-master.md.)
- [x] Proceed to Phase C without stopping.

## Outcome

Phase B reached its "best-effort" target ahead of expectations: the server not only compiles but also links and starts. The binary prints its usage banner when invoked without args. Runtime correctness for any code path exercising the stubbed kyp-era functions (mailslot, CreateProcess, Player::SetTCPTerminate/AddScanSkill/etc.) will silently no-op — see `server/BUILD_ERRORS.md` for the hand-off list. Phase E will address the OpenSSL 1.0 compat shim; Phase C replaces the mysqlclient link with libpqxx.
