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

- [ ] `cmake -S server -B build/server -G Ninja` succeeds (configure step).
      Touches: server/CMakeLists.txt
      Notes:
- [ ] `cmake --build build/server` produces first error log. Capture in `server/BUILD_ERRORS.md` with grouping by error class.
      Touches: server/BUILD_ERRORS.md
      Notes:
- [ ] Iterate fix waves. Commit after each wave with running count: "Phase B: build errors X → Y".
      Touches: server/src/, server/compat/
      Notes:

### B4 — Stop conditions

- [ ] Stop iterating when: (a) server links, (b) error count plateaus across 3 consecutive waves, or (c) context budget low.
      Notes:
- [ ] Final `BUILD_ERRORS.md` update enumerating remaining error classes and a hand-off list for Phase B continuation in a future invocation.
      Touches: server/BUILD_ERRORS.md
      Notes:

## Verification (Phase B done for now when)

- `cmake -S server -B build/server` configures cleanly.
- `cmake --build build/server 2>&1 | tee server/BUILD_ERRORS.md` runs to completion (errors are fine; crashes/configure failures aren't).
- `server/compat/WIN32_INVENTORY.md` and `server/BUILD_ERRORS.md` are committed.
- Master plan updated.
- Proceed to Phase C without stopping.
