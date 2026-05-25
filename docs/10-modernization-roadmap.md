# 10 - Modernization roadmap

A retrospective on phases A through T plus an honest accounting of what
still needs doing. Authoritative source for current status is
`plans/00-master.md`; if this document and the plans disagree, the plans
win.

This is **not a marketing pitch**. The codebase was a 2010 Windows-targeted
~162K-LOC C++ project with a 2008-vintage C# editor suite and a 2024 GPLv3
bash installer bolted on. The "modernization" was bringing it to the point
where a contributor with current tools can build, test, and extend it on
Linux. Most of that work landed across phases A-T; the parts that remain
are listed at the bottom.

## What's done

| Phase | Scope | Status |
|---|---|---|
| A | Repo merge, docs, scaffolding | complete |
| B | Best-effort Linux server build via CMake | complete |
| C | MySQL → Postgres scaffolding (schema conversion) | complete (schema scaffold; DAO migration is Phase N) |
| D | C# tools to .NET 10 SDK-style csproj | complete |
| E | OpenSSL 1.0 → 3.x for the server target | complete |
| F | Compiler warning cleanup | complete (baseline + 2 categories) |
| G | Test scaffolding (GoogleTest + smoke tests) | complete |
| H | Deepen docs (protocol RE, sequence diagrams, ability internals) | complete |
| I | Dev env polish (justfile, docker-compose, OCI images, CI matrix) | complete |
| J | End-to-end runnable: server + proxy + login on Linux, integration tests | complete |
| L | C# tools to Avalonia (Linux-native UI; no WINE) | complete |
| M | Eliminate Win32 from server-native code (mailslot → AF_UNIX, etc.) | complete |
| N | `mysqlplus.cpp` → libpqxx rewrite (Phase C continuation) | complete |
| O | OpenSSL 3.x for proxy + login-server | complete |
| P | Stub-debt audit | complete |
| Q | Delete dead kyp-era TCP cluster | complete |
| R | Extract `common/include/net7/` for shared protocol headers | complete |
| S | Headless CLI client (C# / .NET 10) | complete (14/17 items; 3 blocked on Phase K) |
| T | CLI-driven integration test suite (xUnit) | complete (9/10 items; enumerate blocked on Phase K) |

Concrete outcomes:

- Server + proxy + login-server build clean on Linux via CMake + Ninja
  against system OpenSSL 3.x and libpqxx 7.x; the gtest suite and the
  CLI-driven integration tests pass (33/33).
- The Win32-specific shim trees (`server/compat/`, `proxy/compat/`,
  `login-server/Net7SSL/compat/`) are deleted. Mailslot IPC is replaced
  with AF_UNIX SOCK_DGRAM (`net7ipc::PosixIpc`); single-instance lock is
  `flock` on a pidfile (`net7ipc::SingleInstance`); threading is plain
  pthreads.
- Cross-process headers (opcodes, packet structures, port numbers,
  RSA/RC4) live in `common/include/net7/` and are shared by all three
  C++ targets.
- 13 Avalonia editor ports replace the 2008-era WinForms suite for
  Linux runtime; `tools/itemeditor/` is the only un-ported editor (it
  never had a `.csproj` in the upstream snapshot).
- The kyp-era TCP cluster (Connection, ConnectionManager, TcpListener,
  SSL_Listener, SSL_Connection, ClientTo{Master,Global,Sector}Server,
  EffectManager, JobManager_DEP_ — 15 files) is gone from `server/src/`.
  The proxy and login-server still own the equivalent code where it's
  load-bearing.

For per-phase deliverables and decision history, see
`plans/00-master.md`, the per-phase `plans/NN-phase-*.md` files, and
the append-only `plans/99-decisions-log.md`.

## What's left

### Phase K — in-game opcode handlers + UDP plane

The remaining open phase. Plan: `plans/11-phase-k-opcodes.md`. The
master-server / global-server / sector-server handlers all exist in
`login-server/Net7SSL/` and `proxy/`; the inside-the-sector UDP opcode
dispatch (combat, ability execution, MOB AI, world updates) is the
next pass. Three Phase S and one Phase T item are blocked on this work
finishing.

### Phase N Wave 3 — DAO sweep tail

Phase N replaced `mysqlplus.cpp` with libpqxx and migrated most DAOs;
a handful still use the libmysqlclient path. Wave 3 closes the tail.
Tracked in `plans/14-phase-n-libpqxx.md`.

### Optional — C++23 bump

Task #60 in the TaskList. The server's CMake currently sets a
conservative C++ standard; bumping to C++23 would unlock std::expected
and friends but requires a manual approval pass to confirm no
regressions on the supported compilers. Not autonomous.

### `tools/itemeditor/` Avalonia port

No upstream `.csproj` to start from. Doable as a from-scratch Avalonia
build against the same `commontools-avalonia` shared library the other
editors use — but it is the only remaining WinForms-only editor. Not
prioritised; tracked in `plans/12-phase-l-avalonia.md`.

## What we deliberately skipped

Things we are **not** doing. Each has a reason; in some cases the reason
is "out of scope" rather than "bad idea".

- **No full clean-room protocol reverse engineering.** The existing
  Net-7 RE work (architecture document, packet captures, UdpDump, the
  in-tree protocol code) is preserved and documented. Going beyond that
  requires sustained packet-capture work that is bigger than the rest
  of this roadmap combined. Phases H and K deepen what we have; they
  do not start over.

- **No new gameplay content.** Adding new sectors / missions / mobs is
  something the editors enable; the preservation project itself is
  content-neutral. Custom content lives in forks.

- **No engine rewrite.** The C++ codebase is what it is. Rewriting in
  Rust or modern C++23 would be more fun than maintaining what exists,
  but the value of *this* project is "the old thing still works", not
  "a new thing exists".

- **No DRM-free client distribution.** The Earth & Beyond client is the
  original Westwood / EA binary. We document how to install it; we do
  not redistribute it. (`enb-linux-installer` downloads it from public
  mirrors at runtime.)

- **No commercial use.** Forced by the CC BY-NC-SA 3.0 license, and
  reinforced by policy. We will not consider PRs that move toward "paid
  server" / "premium tier" / "marketplace" features. See
  `LICENSES/README.md`.

- **No mobile, no console, no VR.** These would all be major ports
  built on top of the working server; not the preservation project's
  job.

- **No "modernise to async/await/coroutines".** Tempting in a few
  hot-paths but invasive. The threading model works. Leave it.

- **No replacing Crypto++ with OpenSSL** (or vice versa). Phases E and O
  touched OpenSSL only. Crypto++ is used for the client-protocol RSA/RC4
  in `tools/udpdump/` and is a stable, narrow surface; leaving it alone
  is the right call.

- **No `boost::asio` migration.** The original code rolls its own
  sockets via POSIX + pthreads. Moving to `asio` is appealing for code
  quality but is a large enough refactor to qualify as an engine
  rewrite. Out of scope.

- **No automatic content migration tool from existing live shards.** If
  someone is running a Net-7 server today, they have data we do not.
  Migrating their data into this repo's schema is a one-off operation;
  we will not build a generic tool for it.

- **No relicense.** The Net-7 server is CC BY-NC-SA 3.0; only Net-7
  Entertainment can relicense it. The project floor is non-commercial.
