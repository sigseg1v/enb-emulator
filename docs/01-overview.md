# 01 - Overview

## What this project is

This repository is a **preservation project** for the *Earth & Beyond* MMO
server emulator. The original game shipped in 2002 from Westwood Studios and
was shut down by EA in 2004. The Net-7 Entertainment team reverse-engineered
the client/server protocol and built an open-source C++ server emulator
between roughly 2005 and 2009; volunteers have kept variations of that server
running off and on ever since.

The code drifted into multiple forks. The C# content editors lived in one
repo, the most-up-to-date server fork lived in another, and a Linux install
script for the original Windows client lived in a third. None of them built
out of the box on a modern (2026) Linux system without significant work.

This project does three things:

1. Consolidates the three upstream sources into one cleanly-laid-out repo.
2. Preserves the original license headers and the upstream history.
3. Sets up a multi-phase plan (`plans/`) for moving the code forward
   enough that someone can actually build, run, and contribute to it again.

Nothing here is novel game content. The goal is **conservation**: keep the
code buildable, keep the protocol decoded, keep the install path working.

## The three upstreams

| Upstream | Snapshot date | Lives in | What it brought |
|---|---|---|---|
| **tada-o** fork of Net-7 server (SVN r2974) | 2010-03-15 | `server/`, `login-server/`, `proxy/`, `launcher/`, `client/detours/`, `client/mods/`, `db/mysql/` | The most complete C++ server fork we have: ~162K LOC, the MySQL schema and seed data, ~20 ability implementations that earlier forks only stubbed. |
| **kyp** snapshot (older Net-7) | 2014 GitHub dump | `tools/`, `archive/kyp-snapshot/` | Full C# editor suite (Sector, Mob, Mission, Faction, Item, Effect, TalkTree, Station Tools, LaunchNet7, EnBPatcher, W3D Parser, others), packet captures, original architecture documentation, the historical Linux-port branch. |
| **enb-linux-installer** | 2023-era, GPLv3 | `client/linux-installer/` | A bash script that automates installing and configuring the Windows client under WINE on Linux. Verbatim from upstream. |

Per-file and per-folder license headers are preserved exactly. See
`LICENSES/README.md` for the directory-by-directory license map. The two
load-bearing facts are:

- The Net-7 server code is **CC BY-NC-SA 3.0** (NonCommercial-ShareAlike).
- The Linux installer script is **GPLv3**, scoped strictly to
  `client/linux-installer/`.

## Current status

The work is broken into phases tracked in `plans/`. Live status is in
`plans/00-master.md`. As of the most recent doc generation:

| Phase | What it does | Status |
|---|---|---|
| A | Repo merge, docs, scaffolding | in progress |
| B | Best-effort Linux server build via CMake + Win32-to-POSIX shims | not started |
| C | MySQL to Postgres migration (schema + start of C++ call sites) | not started |
| D | C# tools to .NET 10 (`net10.0-windows` + WinForms) | not started |
| E | OpenSSL 1.0 to 3.x migration | not started |
| F | Compiler warning cleanup (`-Wall -Wextra` baseline + top-N fixes) | not started |
| G | Test scaffolding (GoogleTest + a few smoke tests) | not started |
| H | Deepen docs (protocol RE, sequence diagrams, ability internals) | not started |
| I | Dev env polish (justfile, docker-compose, OCI images, CI matrix) | not started |

Phase A is what produced this document, plus the rest of `docs/` and the
top-level `README.md`. Phases B-I are the modernization work.

The server **does not yet build on Linux**. Expect Phase B to land it in a
"configure succeeds, build emits an honest error log we can iterate on" state.
Until then, treat the server as "Windows + Visual Studio only" for
build purposes.

## License summary

The whole repository is **non-commercial only**.

- Project default: `LICENSES/enb-emulator` (CC BY-NC-SA 3.0 United States).
- Per-file Net-7 headers take precedence over the project default; do not
  modify them when refactoring.
- `client/linux-installer/` is GPLv3, governed by its own `LICENSE` file.
- Specifics in `LICENSES/README.md`.

Practical consequences:

- You may use, modify, and redistribute this for non-commercial purposes.
- You may not run a paid server, sell mods, charge for access, or otherwise
  commercialise this or any derivative.
- Derivatives must be released under CC BY-NC-SA 3.0 (or a later compatible
  CC license).
- License headers are load-bearing. Do not strip them when moving files.

There is no path to relicense the Net-7 code. Only Net-7 Entertainment can do
that.

## How to get started

The right starting point depends on what you want to do.

### "I just want to play on Linux"

The Linux client installer works today. It installs WINE plus the Earth &
Beyond client plus the Net-7 launcher.

```
client/linux-installer/install-enb-linux.sh
```

Prerequisites and tested distributions are documented in
`client/linux-installer/README.md`. The script is from upstream
`enb-linux-installer` and is GPLv3. It is verbatim; we do not modify it.

The client itself is the original Windows binary running under WINE. The
script connects you to the public Net-7 server, not to anything you run
locally.

### "I want to run my own server"

Not yet. The server does not currently build on Linux. After Phase B is far
enough along you will be able to:

1. Bring up the dev stack: `just dev` (docker-compose up postgres + server +
   login). See `08-build.md` and `09-running-locally.md`.
2. Build manually: `cmake -S server -B build/server -G Ninja && cmake --build
   build/server`. Expect failures until Phase B completes.

If you want to build on Windows today, open `server/src/Net7.sln` in Visual
Studio. The legacy `server/Makefile.legacy` (vintage 2010, g++ 4.x style)
exists for reference but is not the supported path.

### "I want to edit game content (sectors, mobs, missions, items)"

The C# editors live in `tools/`. They are WinForms applications. After Phase
D upgrades them to .NET 10:

```
dotnet build tools/Net7Tools.sln
```

The build step is cross-platform. The **runtime is still Windows-only**:
WinForms does not run on Linux or macOS. To use the editors on Linux you
need either Wine + a Windows .NET runtime, or a Windows VM.

Per-tool documentation is in `07-tools-toolchain.md`.

### "I want to understand the code"

Read in this order:

1. `01-overview.md` (you are here)
2. `02-architecture.md` -- server architecture, login flow, sector handoff.
3. `04-server-modules.md` -- per-manager-class summary with file:line refs.
4. `06-database-schema.md` -- table-by-table summary of the 71-table MySQL
   schema.
5. `05-abilities.md` -- ability implementations, with notes on which are new
   in the tada-o fork.

For the network protocol, `03-network-protocol.md` is the starting point;
Phase H will deepen it after a packet capture analysis pass.

### "I want to contribute"

Read `CLAUDE.md` first. It documents the plans workflow, the repo layout,
the coding rules (Linux first, no new Win32 in new code, Postgres syntax in
new SQL, license-header preservation), and the where-do-I-put-X table.

Then look at `plans/00-master.md` to see what is in progress and pick
something off the current phase's checklist.

## What this project is not

- Not a fan fiction site. We do not host new game content.
- Not a public server operator. We package the software; running a server is
  your problem.
- Not a relicense project. CC BY-NC-SA 3.0 is the legal floor.
- Not a complete protocol reverse-engineering effort. Existing RE notes
  (captured packets, architecture document) are preserved and supplemented
  during Phase H, but a clean-room reimplementation is out of scope.

## Credits

- **Net-7 Entertainment** (2005-2009): the team that built the original
  emulator from reverse-engineered protocol work. None of this exists without
  them.
- **The tada-o contributors**: post-Net-7 fork that landed the abilities,
  guild, and combat work that ended up here.
- **kyp / therealkyp**: 2014 GitHub snapshot that preserved the C# editor
  suite, packet captures, and architecture docs.
- **Nimsy**: original WINE-on-Linux guide whose steps became the basis of the
  installer.
- **ciphersimian**: author of the `enb-linux-installer` script.
- **Westwood Studios** for *Earth & Beyond* (2002).

## See also

- `../README.md` -- top-level project overview and quickstart.
- `../CLAUDE.md` -- repo map, license rules, coding rules.
- `../plans/00-master.md` -- modernization phase status.
- `reference/net7-architecture-original.rtf` -- primary source for the
  architecture doc.
- `reference/faq-original.txt` -- 2006 player-facing FAQ.
