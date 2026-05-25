# 08 - Build

This document covers building the four components of the project:

1. The C++ server / proxy / login-server (Linux primary, Windows secondary).
2. The C# tool suite (`tools/Net7Tools.slnx`) — Avalonia ports run native
   on Linux; legacy WinForms ports build cross-platform but only run on
   Windows / WINE.
3. The Linux client installer (`client/linux-installer/install-enb-linux.sh`).
4. The dev environment (`just dev` / `docker compose`).

If something here is wrong, plans diverged from reality. Check
`plans/00-master.md` for the current phase and update plans before code.

## Server -- Linux (current build path)

CMake + Ninja:

```sh
cmake -S server -B build/server -G Ninja
cmake --build build/server

# Same recipe for the proxy and login-server:
cmake -S proxy -B build/proxy -G Ninja && cmake --build build/proxy
cmake -S login-server -B build/login-server -G Ninja && cmake --build build/login-server
```

All three targets build clean against:

- System OpenSSL 3.x (Phase E for the server; Phase O for proxy + login).
  The vendored 2010 OpenSSL 1.0 header tree at `server/src/openssl/` was
  deleted in Phase O+.
- libpqxx 7.x (Phase N rewrote `mysqlplus.cpp` against libpqxx; a few
  DAOs still use libmysqlclient pending Phase N Wave 3).
- libmysqlclient (transitional — see above).

Phase M (see `plans/13-phase-m-win32-elimination.md`) deleted the
`server/compat/`, `proxy/compat/`, and `login-server/Net7SSL/compat/`
shim directories. The minimum typedef set the legacy code still names
(`SOCKET`, `INVALID_SOCKET`, `SOCKET_ERROR` and the `closesocket` /
`Net7TickMs` macros) is provided inline in each target's umbrella header
(`server/src/Net7.h`, `proxy/Net7.h`, `login-server/Net7SSL/Net7SSL.h`).
The mailslot IPC was replaced with AF_UNIX SOCK_DGRAM via
`net7ipc::PosixIpc` (see `common/include/net7/PosixIpc.h`); the
single-instance lock uses `flock` on a pidfile via `net7ipc::SingleInstance`
(`common/include/net7/SingleInstance.h`). Shared wire-format headers
(opcodes, packet structures, port numbers, RSA/RC4) live under
`common/include/net7/` and are included as a PRIVATE include dir by all
three targets — see Phase R notes in the master plan.

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
- Many source files still `#include <windows.h>` — the live Linux build
  drops the includes outright in Phase M and uses system OpenSSL 3.x
  (Phase E/O). The legacy Makefile predates both.
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

Phase D upgraded every C# project that had an upstream `.csproj` to
SDK-style `net10.0-windows`; Phase L then added Avalonia 11 ports for
every user-facing editor, targeting `net10.0` (no `-windows`) so they
run natively on Linux. Build everything:

```sh
dotnet build tools/Net7Tools.slnx
```

Run the central Avalonia launcher (recommended entry point):

```sh
just launch                   # tools/toolslauncher-avalonia
```

Or jump directly to an editor — every Avalonia port has a `just launch-*`
recipe (`just launch-sector-editor`, `just launch-mob-editor`,
`just launch-mission-editor`, etc.). `just --list` prints them all.

Per-tool Avalonia status table is in `tools/README.md`; build-diff status
for the legacy WinForms projects is in `tools/BUILD_STATUS.md`.

### .NET 10 SDK requirement

You need the .NET 10 SDK on whatever box runs `dotnet build`. For the
Avalonia ports this is also the only runtime requirement on Linux. The
legacy WinForms tools (`tools/<name>/` without `-avalonia`) build with
the same SDK but only **run** on Windows / WINE.

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

The Avalonia ports (`tools/<name>-avalonia/`) run on Linux, macOS, and
Windows with only the .NET 10 runtime installed. This is the recommended
path — every user-facing editor except `itemeditor` has an Avalonia
build.

The legacy WinForms ports (`tools/<name>/`) are **Windows-only**:
WinForms has not been ported to Linux/macOS. To run them on Linux,
install the Windows .NET 10 Desktop Runtime inside a WINE prefix and
launch under `wine`. A Windows VM is the unglamorous alternative.

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

Phase I (closed) and the per-phase polish since have stabilised the dev
environment. The interface:

```sh
just init                # bring up mysql:8.0 on :3307 and load both dumps
just dev                 # = just run-stack-bg — server + proxy + login in the background
just build               # cmake build server + proxy + login, dotnet build tools
just test                # ctest + dotnet test
just launch              # central Avalonia tool launcher (recommended)
just launch-mob-editor   # per-tool recipes — see `just --list`
just package             # build OCI image of the server
just down                # tear down the compose stack
just logs server | logs proxy | logs login | logs db
just shell server        # exec into the running server container
```

`docker-compose.yml` brings up:

- `mysql`: MySQL 8.0 on host port 3307, auto-loads `db/mysql/net7.sql`
  and `db/mysql/net7_user.sql`.
- `server`: the C++ sector/world server, built from `server/Dockerfile`.
- `proxy`: Net7Proxy, built from `proxy/Dockerfile`.
- `login`: the login server, built from `login-server/Dockerfile`.
- `postgres` (profile-gated): Postgres 16 with `db/postgres/schema.sql`
  pre-applied — staged for the eventual cutover, not the runtime DB
  today.

All four C++ services build clean and the stack passes the CLI-driven
integration test suite (33/33). See `09-running-locally.md` for the
walkthrough.

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
| C++ server / proxy / login | Yes (CMake + Ninja, OpenSSL 3, libpqxx) | Yes (passes integration tests) | Yes (VS 2022) | Yes |
| C# tools (Avalonia ports) | .NET 10 SDK | Yes (native, no WINE) | .NET 10 SDK | Yes |
| C# tools (legacy WinForms) | .NET 10 SDK | No (WinForms / WINE only) | .NET 10 SDK or VS 2022 | Yes |
| Linux installer | Yes (bash) | Yes | n/a | n/a (it is *for* Linux) |
| Game client | n/a (Windows binary) | Yes via WINE | n/a | Yes (native) |
| Legacy C++ tools | No (Win32-only as written) | No | Yes (VS 2022, may need older compatibility) | Yes |

## Troubleshooting

**`cmake` configure fails on `find_package(OpenSSL)`** -- you need
`libssl-dev` (Debian/Ubuntu) or `openssl-devel` (Fedora).

**Link errors against `mysql_*` symbols** -- the server still links
against `libmysqlclient` for the DAOs that Phase N Wave 3 has not yet
moved to libpqxx. If you see these errors, install `libmysqlclient-dev`
(Debian/Ubuntu) — they are expected, not a regression.

**`dotnet build` reports `net2.0` or `net4.x` not found** -- you are
trying to build pre-Phase-D source. Every project that exists today
has been upgraded to SDK-style; if you hit this, your tree is stale,
not the project file.

**`just: command not found`** -- install `just`
(`apt install just` on 24.04+, otherwise the upstream install
instructions at `https://github.com/casey/just`).

**`docker compose` vs `docker-compose`** -- compose v2 ships as a
`docker compose` subcommand; v1 was a separate `docker-compose` binary.
The justfile assumes v2.
