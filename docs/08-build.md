# 08 - Build

This document covers building the four components of the project:

1. The C++ server / proxy / login-server (Linux primary, Windows secondary).
2. The C# tool suite (`tools/FreyaTools.slnx`) -- Avalonia ports run native
   on Linux. The original WinForms projects have been removed.
3. The Linux client installer (`client/linux-installer/install-enb-linux.sh`).
4. The dev environment (`just dev` / `docker compose`).

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

- System OpenSSL 3.x. The server, proxy, and login-server build against
  the OpenSSL 3.x headers and libraries shipped by the host. (The proxy's
  Win32 / WINE build statically links a MinGW-built OpenSSL from
  `proxy/third_party/openssl-mingw64/` instead.)
- libpqxx 7.x is the C++ Postgres client. `server/src/db/sqlplus.cpp` and
  the login-server's `LinuxAuth.cpp` both speak Postgres through libpqxx;
  the server links `PkgConfig::LIBPQXX` and `PostgreSQL::PostgreSQL`. No
  server-native code links libmysqlclient.

There are no Win32 compat shim trees. The minimum typedef set the legacy
code still names (`SOCKET`, `INVALID_SOCKET`, `SOCKET_ERROR` and the
`closesocket` / `Net7TickMs` macros) is provided inline in each target's
umbrella header (`server/src/Net7.h`, `proxy/Net7.h`,
`login-server/Net7SSL/Net7SSL.h`). IPC uses AF_UNIX SOCK_DGRAM via
`net7ipc::PosixIpc` (see `common/include/net7/PosixIpc.h`); the
single-instance lock uses `flock` on a pidfile via `net7ipc::SingleInstance`
(`common/include/net7/SingleInstance.h`). Shared wire-format headers
(opcodes, packet structures, port numbers, RSA/RC4) live under
`common/include/net7/` and are included as a PRIVATE include dir by all
three targets.

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
- Many source files in the original tree `#include <windows.h>`; the live
  Linux build drops those includes and uses system OpenSSL 3.x. The
  legacy Makefile predates both changes.
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

There are also standalone VC6 `.dsp` projects: `tools/chunktypes/`,
`tools/udpdump/`, `tools/unmix/`, `tools/xml-exporter/`. These are not
part of `Net7.sln`. See `07-tools-toolchain.md` for their status.

## C# tools

Every C# project is SDK-style and targets `net10.0` (Avalonia ports) or
`net10.0-windows` (console/library tools). Build everything:

```sh
dotnet build tools/FreyaTools.slnx
```

Run the central Avalonia launcher (recommended entry point):

```sh
just launch                   # tools/toolslauncher-avalonia
```

Or jump directly to an editor -- every Avalonia port has a `just launch-*`
recipe (`just launch-sector-editor`, `just launch-mob-editor`,
`just launch-mission-editor`, etc.). `just --list` prints them all.

Per-tool status table is in `tools/README.md`; historical Phase D build
status is in `tools/BUILD_STATUS.md`.

### .NET 10 SDK requirement

You need the .NET 10 SDK on whatever box runs `dotnet build`. For the
Avalonia ports this is also the only runtime requirement on Linux.

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

The Avalonia editors (`tools/<name>-avalonia/`) run on Linux, macOS, and
Windows with only the .NET 10 runtime installed. Every user-facing editor,
including the Item Editor (`tools/item-editor-avalonia/`), is an Avalonia
build. The original WinForms projects have been removed.

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

The dev environment interface:

```sh
just init                # bring up Postgres 16 + apply the schema (schema-init)
just dev                 # = just run-stack-bg: server + proxy + login in the background
just build               # cmake build the server
just build-tools         # dotnet build tools/FreyaTools.slnx
just test                # ctest + dotnet test
just launch              # central Avalonia tool launcher (recommended)
just launch-mob-editor   # per-tool recipes -- see `just --list`
just package             # build OCI images of the server + login
just down                # tear down the compose stack
just logs server         # tail a service's logs (server | proxy | login | postgres)
just shell server        # exec into the running server container
```

`docker-compose.yml` brings up:

- `postgres`: Postgres 16, the runtime database. The `schema-init`
  one-shot applies `db/postgres/schema.sql` (and the seed scripts) to the
  `net7` (content) and `net7_user` (accounts) databases. Host port 5434
  maps to container 5432.
- `server`: the C++ sector/world server, built from `server/Dockerfile`.
- `proxy`: FreyaProxy, built from `proxy/Dockerfile`.
- `login`: the login server, built from `login-server/Dockerfile`. Reads
  the `net7_user` database on Postgres for auth.
- `mysql` (profile `mysql-legacy`, opt-in): MySQL 8.0 loading the
  historical `db/mysql/` dumps. Kept for reference only; not the runtime
  DB.

All three C++ services build clean and the stack passes the CLI-driven
integration test suite. See `09-running-locally.md` for the walkthrough.

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
    libsodium-dev \
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
| `libssl-dev` | OpenSSL 3.x headers. The server-native code is clean against the OpenSSL 3.x API. |
| `libcrypto++-dev` | Crypto++ headers. Used by the original RSA/RC4 client crypto. |
| `libtinyxml-dev` | TinyXML, used by content loaders. |
| `liblua5.4-dev` | Lua 5.4 runtime + headers. The server embeds Lua for scripting. |
| `libpqxx-dev` | C++ Postgres client. The server and login-server speak Postgres through it. |
| `libsodium-dev` | libsodium, used for Argon2id password hashing. |
| `docker.io`, `docker-compose-v2` | Dev environment runs in compose. |
| `just` | Task runner; `justfile` at repo root. |
| `dotnet-sdk-10.0` | C# tools require .NET 10. |

If `libpqxx-dev` is too old on your distro (Ubuntu 22.04 ships 6.4), grab
a newer build from the Postgres APT repository
(`apt.postgresql.org/pub/repos/apt`).

### Fedora / RHEL

Equivalent packages: `gcc-c++`, `cmake`, `ninja-build`, `openssl-devel`,
`cryptopp-devel`, `tinyxml-devel`, `lua-devel`, `libpqxx-devel`,
`libsodium-devel`, `docker`, `docker-compose`, `just`. The .NET SDK is `dotnet-sdk-10.0`
from the Microsoft RPM repo or the dnf module.

### Arch

`base-devel`, `cmake`, `ninja`, `openssl`, `crypto++`, `tinyxml`, `lua`,
`libpqxx`, `libsodium`, `docker`, `docker-compose`, `just`, `dotnet-sdk` (AUR).

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
| Linux installer | Yes (bash) | Yes | n/a | n/a (it is *for* Linux) |
| Game client | n/a (Windows binary) | Yes via WINE | n/a | Yes (native) |
| Legacy C++ tools | No (Win32-only as written) | No | Yes (VS 2022, may need older compatibility) | Yes |

## Troubleshooting

**`cmake` configure fails on `find_package(OpenSSL)`** -- you need
`libssl-dev` (Debian/Ubuntu) or `openssl-devel` (Fedora).

**`dotnet build` reports `net2.0` or `net4.x` not found** -- you are
building a stale tree. Every project that exists today is SDK-style and
targets `net10.0` / `net10.0-windows`; if you hit this, pull the latest
source.

**`just: command not found`** -- install `just`
(`apt install just` on 24.04+, otherwise the upstream install
instructions at `https://github.com/casey/just`).

**`docker compose` vs `docker-compose`** -- compose v2 ships as a
`docker compose` subcommand; v1 was a separate `docker-compose` binary.
The justfile assumes v2.
