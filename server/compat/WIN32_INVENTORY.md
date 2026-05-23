# Win32 API inventory (server/src/)

Inventory of Windows-only API usage in the Net-7 server source tree, generated for Phase B
(best-effort Linux port). Counts exclude vendored third-party headers (Lua libs, mysql
headers, openssl headers) and the legacy `.vcproj` file.

## Threading APIs (`_beginthreadex`, `CreateThread`)

8 source files spawn threads via Win32:

- `server/src/TcpListener.cpp`
- `server/src/SaveManager.cpp`
- `server/src/SectorManager.cpp`
- `server/src/SSL_Listener.cpp`
- `server/src/UDPConnection.cpp`
- `server/src/ConnectionManager.cpp`
- `server/src/PlayerManager.cpp`
- `server/src/SSL_Connection.cpp`

**Shim strategy:** `server/compat/threading_shim.h` provides `n7_thread_create()`,
`n7_thread_join()` typedefs over `std::thread` / pthreads.

## Synchronization primitives (`CreateMutex`, `CreateSemaphore`, `InitializeCriticalSection`, `WaitForSingleObject`)

3 first-party source files:

- `server/src/Net7.cpp` — main thread sync
- `server/src/MailslotManager.cpp` — sync around the mailslot reader thread
- `server/src/Mutex.cpp` — thin wrapper class around `CRITICAL_SECTION` (refactor target)

**Shim strategy:** `Mutex.cpp/h` is rewritten to wrap `std::mutex`. `WaitForSingleObject`
on mutex/event handles maps to `std::condition_variable::wait_for()`.

## Mailslot IPC (`CreateMailslot`)

4 first-party source files use Win32 mailslots for inter-process communication between
the master, global, sector, and login server processes:

- `server/src/MailslotManager.cpp` — mailslot wrapper class
- `server/src/ConnectionManager.cpp` — sends/receives via mailslot
- `server/src/ServerManager.cpp` — server-side mailslot endpoint
- `server/src/Net7.cpp` — top-level mailslot setup

**Shim strategy:** `server/compat/mailslot_shim.{h,cpp}` replaces mailslots with Unix
domain sockets (`AF_UNIX`, `SOCK_DGRAM`) under `/var/run/net7/` or `$XDG_RUNTIME_DIR/net7/`.
Same datagram semantics, message-bounded reads. This is the single largest behavioral
shim — needs careful testing.

## Winsock (`WSAStartup`, `closesocket`, `WSAGetLastError`, `ioctlsocket`)

7 first-party source files include `winsock2.h`:

- `server/src/Net7.h` (transitively pulls it in everywhere)
- `server/src/Net7.cpp`
- `server/src/TcpListener.cpp`
- `server/src/ServerManager.cpp`
- `server/src/SSL_Listener.cpp`
- `server/src/Connection.cpp`
- `server/src/SSL_Connection.cpp`
- `server/src/UDPConnection.cpp`

**Shim strategy:** `win32_shim.h` provides:
- `closesocket(s)` → `close(s)` macro
- `WSAGetLastError()` → `errno` macro
- `ioctlsocket()` → `ioctl()` with `FIONBIO`
- `WSAStartup()/WSACleanup()` → no-op macros
- `SOCKET` → `int`
- `INVALID_SOCKET` → `(-1)`
- `SOCKET_ERROR` → `(-1)`

## Sleep(ms)

20 source files call `Sleep(ms)`. Trivially replaced with `std::this_thread::sleep_for()`
or a `Sleep(ms)` macro in `win32_shim.h`:

```cpp
#define Sleep(ms) usleep((useconds_t)(ms) * 1000)
```

## `windows.h` / `process.h` direct includes

Only 2 first-party headers:

- `server/src/Mutex.h` — `windows.h` for `CRITICAL_SECTION`
- `server/src/Net7.h` — `windows.h` + `process.h` for handles and `_beginthreadex`

Both can be guarded with `#ifdef _WIN32` and the shim included on Linux.

## TCHAR / LPTSTR / Windows string macros

8 source files use Windows string types. Mostly trivially mappable: `TCHAR` → `char`,
`LPTSTR` → `char*`, `_T("x")` → `"x"`, `_tcsXxx` → `strXxx`. Covered by `win32_shim.h`.

## HANDLE typedefs

11 source files use bare `HANDLE`. Handled per-site:
- Thread `HANDLE` → `pthread_t`
- Mutex/event `HANDLE` → `std::mutex*` / `std::condition_variable*` in a wrapper
- Mailslot `HANDLE` → `int` (socket fd)
- File `HANDLE` → `int` (file fd) — not encountered in this tree

## Out of scope (not first-party)

These pulls aren't part of the server proper and don't need shimming:
- `server/src/mysql/*` — vendored mysql connector C headers
- `server/src/openssl/*` — vendored OpenSSL 1.0-era headers (Phase E)
- `server/src/LUA/**` — Lua 5.x distribution

## Estimated effort

| Item | Status | Estimate |
|---|---|---|
| `win32_shim.h` (Winsock, Sleep, TCHAR) | scaffolded | 1h to refine + verify |
| `threading_shim.h` (std::thread wrapper) | scaffolded | 2-3h to rewire 8 callers |
| `mailslot_shim.{h,cpp}` (AF_UNIX SOCK_DGRAM) | scaffolded | 1 day to implement + smoke-test |
| `Mutex.cpp` rewrite (std::mutex) | not started | 1h |
| Per-file compile error iteration | not started | days (this is the bulk of Phase B) |

The shim layer is the easy part. Iterating compile errors across 200 files is where
the real Phase B time goes.
