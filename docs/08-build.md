# 08 - Build

This document covers building the four components of the project:

1. The C++ server (Linux primary, Windows secondary).
2. The C# tool suite (`tools/Net7Tools.sln`).
3. The Linux client installer (`client/linux-installer/install-enb-linux.sh`).
4. The dev environment (`just dev` / `docker compose`).

If something here is wrong, plans diverged from reality. Check
`plans/00-master.md` for the current phase and update plans before code.

## Server -- Linux (target build path)

The intended modern path is CMake + Ninja.

```sh
cmake -S server -B build/server -G Ninja
cmake --build build/server
```

**Current status:** Phase B is in progress. Pre-Phase-B, `cmake -S server -B
build/server` may not even configure cleanly -- `server/CMakeLists.txt` is
written during Phase B's A4 scaffolding and iterated through B1-B3. Treat
the above command as the desired entry point; expect failures until Phase B
has had several commits worth of iteration.

Phase B documents its progress in two files that will appear in `server/`
as the work proceeds:

- `server/compat/WIN32_INVENTORY.md` -- counts of Windows-only API usage per
  shim category (mutexes, events, mailslots, etc.).
- `server/BUILD_ERRORS.md` -- the running compile-error log, grouped by
  error class, with a fix-progress count ("Phase B: build errors X -> Y").

The compat shim layer (`server/compat/`) provides typedefs and thin
wrappers for the Windows APIs the 2010 codebase depends on:
`_beginthreadex`, `WaitForSingleObject`, `CreateMutex`, `CreateEvent`,
`Sleep`, `_snprintf`, `stricmp`, `InterlockedIncrement`, `HANDLE`,
`HWND`, etc. The mailslot IPC (`MailslotManager`) needs a real
replacement (Unix domain sockets or POSIX message queues), not just a
typedef -- this is called out separately in Phase B's plan file.

## Server -- Linux (legacy path, reference only)

The original 2010-era Makefile is preserved at `server/Makefile.legacy`:

```sh
cd server/src
cp ../Makefile.legacy Makefile
make
```

What it does:

```
TARGET   = Net7
CC       = g++ -g -I/usr/local/ssl/include -DUSE_OPENSSL -O2 -march=athlon64
LIBS     = -pthread -lssl -lcrypto -ldl -lcryptopp
LDFLAGS  = -L../libs
```

This will not work as-is on a 2026 system:

- `march=athlon64` predates virtually every supported gcc target list;
  drop or replace with `march=x86-64-v3`.
- `-I/usr/local/ssl/include` assumes a hand-built OpenSSL 1.0 in
  `/usr/local`; system OpenSSL 3 ships headers in `/usr/include/openssl/`.
- Many source files still `#include <windows.h>` -- the shim layer in
  `server/compat/` exists to short-circuit that, but the legacy Makefile
  predates it.
- `cryptopp` is now packaged as `libcrypto++` on Debian/Ubuntu (yes, with
  the plus signs).

Use the legacy Makefile only as a historical reference for what dependencies
the codebase originally had. The supported path is CMake.

## Server -- Windows

Open the original solution in Visual Studio:

```
server/src/Net7.sln
```

This is a Visual Studio 2008 / 2010-era solution. Modern Visual Studio
versions will offer to upgrade it on first open. Build configuration is
the upstream tada-o one; we have not modified it. Expect warnings and
deprecated-API noise. If the Linux build is your goal, do the Windows
build first as a sanity check that nothing in the merge broke -- the
upstream code did build there in 2010 and the merge preserved file
contents.

There is also `tools/launchnet7-old/LaunchNet7.dsp`, `tools/chunktypes/`,
`tools/udpdump/`, `tools/unmix/`, `tools/xml-exporter/` as standalone VC6
`.dsp` projects. These are not part of `Net7.sln`. See
`07-tools-toolchain.md` for their status.

## C# tools

Phase D upgrades every C# project to `net10.0-windows` SDK-style. After
Phase D completes:

```sh
dotnet build tools/Net7Tools.sln
```

Pre-Phase-D, the projects are 2008-vintage non-SDK `.csproj` files
targeting .NET Framework 2.0-4.0. `dotnet build` will fail with framework
mismatch errors. The fix is the Phase D upgrade, not adding old framework
shims.

### .NET 10 SDK requirement

The runtime is Windows-only, but the **build** is cross-platform. You need
the .NET 10 SDK on whatever box runs `dotnet build`.

```sh
dotnet --list-sdks    # must include 10.x
```

Install on Debian/Ubuntu:

```sh
# Add Microsoft package repository (one-time setup), then:
sudo apt install dotnet-sdk-10.0
```

Install on Windows: the Visual Studio installer ships the .NET SDK; or
download from `https://dotnet.microsoft.com/download/dotnet/10.0`.

### Runtime

The editors are WinForms. WinForms is **Windows-only** -- the .NET
Foundation has been clear it will not be ported to Linux/macOS. To run
the editors on Linux, the only path is:

- Install the Windows .NET 10 Desktop Runtime inside a Wine prefix.
- Launch the editor under `wine`.

This works for most of the editors but is awkward. A Windows VM is the
unglamorous alternative.

## Linux client (game client, not server)

Today's working path. The installer is the upstream GPLv3 bash script,
verbatim:

```sh
client/linux-installer/install-enb-linux.sh
```

What it does (full prerequisite list and supported distros in
`client/linux-installer/README.md`):

- Installs WINE plus its prerequisites (`wine-gecko`, `mesa-utils`,
  `winetricks`).
- Downloads and installs the Earth & Beyond client.
- Downloads and installs the Net-7 launcher.
- Configures the WINE prefix for the client.

It does not require this repo's server to be running -- it connects to the
public Net-7 server by default. The installer is GPLv3 and isolated to
`client/linux-installer/`; that license does not propagate.

Supported distros (per the upstream README): Ubuntu, Debian, Linux Mint,
Pop!_OS, Fedora, Arch (with tweaks). The script has been tested on Ubuntu
20.04 through 22.04 (per the upstream history). Distro support is
contingent on WINE availability and varies; see the upstream README for
the current matrix.

## Dev environment -- justfile + docker-compose

Phase I (`plans/09-phase-i-dev-env.md`) polishes the dev environment. The
intended interface:

```sh
just dev       # docker-compose up postgres + server + login
just build     # cmake build server, dotnet build tools
just test      # ctest + dotnet test
just package   # build OCI image of the server
just down      # tear down compose stack
just psql      # open psql shell to the dev DB
just logs server | logs login | logs db
just shell server  # exec into the running server container
```

`docker-compose.yml` brings up:

- `postgres`: a Postgres 16 instance with `db/postgres/schema.sql`
  auto-applied.
- `server`: the C++ sector/world server, built from `server/Dockerfile`.
- `login`: the login server, built from `login-server/Dockerfile`.

Pre-Phase-B, `server` will not build or start cleanly. The `dev`
target should still bring up Postgres so you can work on the schema.

## Dependencies

### Debian / Ubuntu (24.04 reference)

```sh
sudo apt install \
    build-essential \
    g++ \
    cmake \
    ninja-build \
    libssl-dev \
    libcrypto++-dev \
    libtinyxml-dev \
    liblua5.4-dev \
    libpqxx-dev \
    pkg-config \
    docker.io \
    docker-compose-v2 \
    just
# .NET 10 SDK: add Microsoft package repository, then:
sudo apt install dotnet-sdk-10.0
```

Per-package rationale:

| Package | Why |
|---|---|
| `g++`, `build-essential`, `cmake`, `ninja-build`, `pkg-config` | C++ toolchain. g++ 13+ required for `-Wall -Wextra` clean code per CLAUDE.md. |
| `libssl-dev` | OpenSSL 3.x headers. Phase E migrates code off OpenSSL 1.0 APIs. |
| `libcrypto++-dev` | Crypto++ headers. Used by the original RSA/RC4 client crypto. |
| `libtinyxml-dev` | TinyXML, used by content loaders. |
| `liblua5.4-dev` | Lua 5.4 runtime + headers. The server embeds Lua for scripting. |
| `libpqxx-dev` | C++ Postgres client. Pulled in by Phase C for the MySQL-to-Postgres migration. |
| `docker.io`, `docker-compose-v2` | Dev environment runs in compose. |
| `just` | Task runner; `justfile` at repo root. |
| `dotnet-sdk-10.0` | C# tools require .NET 10. |

If `libpqxx-dev` is too old on your distro (Ubuntu 22.04 ships 6.4), grab
a newer build from the Postgres APT repository
(`apt.postgresql.org/pub/repos/apt`).

### Fedora / RHEL

Equivalent packages: `gcc-c++`, `cmake`, `ninja-build`, `openssl-devel`,
`cryptopp-devel`, `tinyxml-devel`, `lua-devel`, `libpqxx-devel`,
`docker`, `docker-compose`, `just`. The .NET SDK is `dotnet-sdk-10.0`
from the Microsoft RPM repo or the dnf module.

### Arch

`base-devel`, `cmake`, `ninja`, `openssl`, `crypto++`, `tinyxml`, `lua`,
`libpqxx`, `docker`, `docker-compose`, `just`, `dotnet-sdk` (AUR).

### Windows

- Visual Studio 2022 or later with the C++ desktop workload (for the
  server). The included MSBuild is enough; no separate CMake install
  needed if you use the Visual Studio CMake integration.
- .NET 10 SDK (for the tools); ships with Visual Studio 2022 17.x or
  install standalone.
- For running the dev environment, Docker Desktop is required (the
  Linux containers run under WSL2).

## Build matrix summary

| Component | Linux build | Linux runtime | Windows build | Windows runtime |
|---|---|---|---|---|
| C++ server | Phase B (in progress) | Phase B + C + E (in progress) | Yes (VS 2022) | Yes |
| C# tools | Phase D + .NET 10 SDK | No (WinForms) | Phase D + .NET 10 SDK or VS 2022 | Yes |
| Linux installer | Yes (bash) | Yes | n/a | n/a (it is *for* Linux) |
| Game client | n/a (Windows binary) | Yes via WINE | n/a | Yes (native) |
| Legacy C++ tools | No (Win32-only as written) | No | Yes (VS 2022, may need older compatibility) | Yes |

## Troubleshooting

**`cmake` configure fails on `find_package(OpenSSL)`** -- you need
`libssl-dev` (Debian/Ubuntu) or `openssl-devel` (Fedora).

**Link errors against `mysql_*` symbols** -- the C++ server used to link
against `libmysqlclient`. Phase C swaps to `libpqxx`. If you see these
errors after Phase C, you have stale build artefacts; `rm -rf build/`
and reconfigure.

**`dotnet build` reports `net2.0` or `net4.x` not found** -- you are
trying to build pre-Phase-D source. Either run the Phase D upgrade
(write SDK-style csproj, set `<TargetFramework>`) or install
.NET Framework targeting packs (not recommended).

**`just: command not found`** -- install `just`
(`apt install just` on 24.04+, otherwise the upstream install
instructions at `https://github.com/casey/just`).

**`docker compose` vs `docker-compose`** -- compose v2 ships as a
`docker compose` subcommand; v1 was a separate `docker-compose` binary.
The justfile assumes v2.
