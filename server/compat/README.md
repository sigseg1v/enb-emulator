# server/compat — Win32 → POSIX shims

## Purpose

The Net-7 server is 2010-vintage Windows C++ (~162K LOC). It uses
`DWORD`, `HANDLE`, `LPTSTR`, `Sleep()`, `_snprintf`, `stricmp`,
`CreateMutex`, `CreateEvent`, `_beginthreadex`, `CreateMailslot` and
friends across roughly every translation unit.

Rewriting each call site by hand is a Phase F-or-later concern. For
Phase B we want the legacy code to **compile** on Linux. That's what
this directory provides: a minimum-viable header / source layer that
swaps Win32 names for POSIX equivalents (or stubs).

This layer is a one-way ratchet. As the codebase modernizes, shim usage
should only ever shrink.

## Include ordering rule

These headers must come **before** any `#include <windows.h>` in any
file that includes both. The server tree includes `windows.h` extremely
widely; in Phase B we will be carefully threading these headers in via
either:

- A force-include in `CMakeLists.txt` (`-include compat/win32_shim.h`),
  applied selectively to translation units that need it; or
- Edits to the legacy headers themselves to add a guarded
  `#include "win32_shim.h"` at the top.

The exact mechanism will be decided in Phase B iteration; until then,
new code that explicitly opts in should `#include "win32_shim.h"` first.

## Use POSIX in new code

`server/compat/` is for legacy. NEW code must:

- Use `<cstdint>` / `<cstring>` / POSIX names directly.
- Use `std::thread` / `std::mutex` / `std::condition_variable` instead
  of `CreateThread` / `CreateMutex` / `CreateEvent`.
- Use Unix domain sockets or named pipes (mkfifo) instead of mailslots.
- Use `clock_gettime(CLOCK_MONOTONIC, ...)` directly instead of
  `GetTickCount`.

If you find yourself reaching for a shim in new code, that's a signal
the shim should be deleted, not extended.

## Shim status

| Shim | File | Status | Runtime correct? |
|---|---|---|---|
| Integer / pointer typedefs (DWORD, HANDLE, LPTSTR, …) | `win32_shim.h` | real | yes |
| `Sleep(ms)` -> `usleep(ms*1000)` | `win32_shim.h` | real | yes |
| `GetTickCount` -> `clock_gettime(CLOCK_MONOTONIC)` | `win32_shim.h` | real | yes |
| `_snprintf` / `stricmp` / `_strdup` / `_unlink` aliases | `win32_shim.h` | real | yes |
| `_beginthreadex` over pthread | `threading_shim.{h,cpp}` | real | mostly (signature simplified) |
| `WaitForSingleObject` for thread handle | `threading_shim.{h,cpp}` | real (polling-based timed wait) | mostly |
| `CloseHandle` for thread handle | `threading_shim.{h,cpp}` | real | yes |
| `CreateMutex` / `ReleaseMutex` | `threading_shim.{h,cpp}` | **STUB** (logs, returns sentinel) | **NO** |
| `CreateEvent` / `SetEvent` / `ResetEvent` | `threading_shim.{h,cpp}` | **STUB** | **NO** |
| `CreateMailslot` / `WriteFile` / `ReadFile` (mailslot) | `mailslot_shim.{h,cpp}` | **STUB** | **NO** |
| `GetMailslotInfo` | `mailslot_shim.{h,cpp}` | **STUB** | **NO** |

Anything marked **STUB** will compile and link, but will not exhibit
correct Win32-equivalent behaviour at runtime. The server will not be
fully functional until those rows turn into "real". That work is
tracked in `plans/02-phase-b-linux-server.md` under B2 / Phase B
continuation.

## When this directory can be deleted

When `git grep -nE "DWORD|HANDLE|LPTSTR|Sleep\(|_snprintf|stricmp|_beginthreadex|CreateMutex|CreateEvent|CreateMailslot" server/src/`
returns nothing meaningful and CI builds without `-fpermissive`. Track
the count over time as a modernization metric.
