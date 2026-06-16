# 10 - Project status and known limitations

A present-tense summary of what works today, what is still incomplete, and
an explicit list of things this project deliberately does not do. Internal
progress notes live under `plans/`.

This is **not a marketing pitch**. The codebase started as a 2010
Windows-targeted ~162K-LOC C++ project with a 2008-vintage C# editor suite
and a 2024 GPLv3 bash installer bolted on. The goal of the modernization
work was to bring it to the point where a contributor with current tools
can build, test, and extend it on Linux. Most of that is done; the parts
that remain are listed below.

## What works today

The server runs natively on Linux. Concretely:

- Server, proxy, and login-server build clean on Linux via CMake + Ninja
  against system OpenSSL 3.x and libpqxx 7.x. The gtest suite and the
  CLI-driven integration tests pass.
- The runtime database is Postgres. The schema is `db/postgres/schema.sql`
  (71 tables). Every DAO speaks Postgres through libpqxx; none of the C++
  targets link libmysqlclient. The original MySQL dumps under `db/mysql/`
  are kept only as historical source.
- The Win32-specific shim trees are not present. Mailslot IPC is replaced
  with AF_UNIX SOCK_DGRAM (`net7ipc::PosixIpc`); single-instance locking is
  `flock` on a pidfile (`net7ipc::SingleInstance`); threading is plain
  pthreads.
- Cross-process headers (opcodes, packet structures, port numbers,
  RSA/RC4, the Mutex wrapper) live in `common/include/net7/` and are
  shared by all three C++ targets, so there is one canonical copy of every
  struct that crosses a process boundary.
- The C# editor and tool suite is ported to Avalonia 11 / .NET 10 and runs
  natively on Linux without WINE. There are 13 Avalonia executables (the
  content editors -- sector, mob, mission, faction, item, effect, talktree,
  station tools -- plus the launcher, patchers, data import, and
  LaunchFreya) on top of the shared `commontools` library. The
  original WinForms projects have been removed; the Avalonia ports are the
  only versions.
- A headless C# CLI client (`freya/cli-client/`) and an xUnit integration
  suite drive the live server end to end and byte-pin its packets.
- The Linux client installer (`client/linux-installer/`, GPLv3, verbatim
  from upstream) installs the original Windows client under WINE.

The kyp-era TCP cluster (Connection, ConnectionManager, TcpListener,
SSL_Listener, SSL_Connection, ClientTo{Master,Global,Sector}Server,
EffectManager, JobManager_DEP_) is not present in `server/src/`. The
proxy and login-server own the equivalent TCP plumbing where it is
load-bearing.

## What is incomplete

### In-sector UDP opcode plane

The master-server, global-server, and sector-server handlers exist in
`login-server/Net7SSL/` and `proxy/`. The inside-the-sector UDP opcode
dispatch -- combat, ability execution, MOB AI, and world updates -- is
still being filled in. This is the main capability gap: a player can log
in, reach a sector, and chat, but the in-game simulation handlers are
incomplete. A few CLI and integration-test features that depend on those
handlers are parked until the corresponding server-side opcode lands.

### Optional C++23 bump

The server's CMake currently sets a conservative C++ standard. Bumping to
C++23 would unlock `std::expected` and friends but requires a manual pass
to confirm no regressions on the supported compilers, so it is not done
automatically.

## What we deliberately skipped

Things we are **not** doing. Each has a reason; in some cases the reason
is "out of scope" rather than "bad idea".

- **No full clean-room protocol reverse engineering.** The existing
  protocol knowledge -- preserved architecture documents, packet captures,
  and the in-tree protocol code -- is conserved and documented. Going
  beyond that requires sustained packet-capture work that is bigger than
  the rest of this roadmap combined. We deepen what we have; we do not
  start over.

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

- **No replacing Crypto++ with OpenSSL** (or vice versa). The OpenSSL
  work was scoped to TLS; Crypto++ is used for the client-protocol RSA/RC4
  in `tools/udpdump/` and is a stable, narrow surface, so leaving it alone
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
