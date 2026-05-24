# Earth & Beyond emulator preservation project

> A consolidated, modernised home for the Earth & Beyond MMO server emulator. The goal is to keep the game playable on contemporary hardware; Linux server, Linux or Windows client; and bring the codebase forward enough that contributors can actually work on it again.

## What this is

Westwood's *Earth & Beyond* (2002) was shut down by EA in 2004. A community team at Net-7 Entertainment reverse-engineered the server protocol and built an open emulator in C++. That code split into multiple forks and drifted; the C# content editors lived in one repo, the server fork with the latest gameplay code lived in another, and a Linux client installer lived in a third.

This project is **one repo** that consolidates:

| Upstream | Lives in | What it brought |
|---|---|---|
| **tada-o fork** of Net-7 server (svn r2974, 2010-03-15) | `server/`, `login-server/`, `proxy/`, `launcher/`, `client/detours/`, `client/mods/`, `db/mysql/` | Newer/more complete C++ server (~162K LOC), the MySQL schema + seed data, ~20 ability implementations that other forks only had stubs for |
| **kyp snapshot** (older Net-7 snapshot, 2014 GitHub dump) | `tools/`, `archive/kyp-snapshot/` | Full C# editor suite (Sector, Mob, Mission, Faction, Item, Effect, TalkTree editors plus Station Tools, EnBPatcher, LaunchNet7, W3D Parser, etc.), the original Net-7 architecture documentation, packet captures, the historical Linux-port attempt |
| **enb-linux-installer** | `client/linux-installer/` | A GPLv3 bash script that automates installing and configuring the Windows client under WINE on Linux distros |

These projects it's based on are super old code but the Net-7 current codebase is private and otherwise inaccessible for extending, so this is the best I can do for now. If more modern code for that was released I'd be happy to build on it.

## Project status

Tracked in `plans/00-master.md`. The summary:

| Phase | What | Status |
|---|---|---|
| A | Repo merge + scaffolding + docs | in progress |
| B | Best-effort Linux server build (CMake, Win32→POSIX shims) | not started |
| C | Postgres migration (schema + start of C++ call-site migration) | not started |
| D | C# tools → .NET 10 (WinForms on net10.0-windows) | not started |
| E | OpenSSL 1.0 → 3.x | not started |
| F | Warning cleanup | not started |
| G | Tests | not started |
| H | Deepen docs (protocol RE, runtime walkthroughs) | not started |
| I | Dev env polish (justfile, docker-compose hot-reload, CI matrix, OCI images) | not started |

## Quickstart

### Linux client (works today)

```
client/linux-installer/install-enb-linux.sh
```

Installs WINE + the Earth & Beyond client + the Net-7 launcher. See `client/linux-installer/README.md` for prerequisites and supported distros.

### Server (Phase B in progress)

```
just dev          # docker-compose up postgres + server + login
just build        # cmake build server, dotnet build tools
just test         # ctest + dotnet test
just package      # build OCI image of the server
```

Today `just dev` brings up Postgres and *attempts* to build/run the server. Build is not yet clean on Linux - that's Phase B. See `server/BUILD_ERRORS.md` (after Phase B has been worked on) for the running error list.

### C# content tools

```
dotnet build tools/Net7Tools.sln
```

Build is cross-platform after Phase D. Runtime is Windows-only (WinForms). On Linux, run them under Wine + `dotnet` (Wine + `wine dotnet ...`) or in a Windows VM.

## Repo layout

See `CLAUDE.md` for the full directory map and rules. Short version:

- `server/`, `login-server/`, `proxy/`, `launcher/` - C++ server-side
- `client/` - Linux installer + client mods + Detours
- `tools/` - C# content editors
- `db/` - MySQL dumps (original) + Postgres schema (converted)
- `docs/` - architecture, protocol, modules, schema, abilities, tools, build, running, roadmap
- `plans/` - multi-phase plan files (source of truth for what's done/next)
- `archive/` - historical material from upstream repos that didn't make it into the active tree
- `LICENSES/` - license texts and the directory-by-directory license map

## License

**Non-commercial only.**

The project default license is **Creative Commons Attribution-NonCommercial-ShareAlike 3.0 United States** because the bulk of the inherited code (the Net-7 server) is under that license and we can't relicense it.

- `LICENSES/enb-emulator` - project default (CC BY-NC-SA 3.0)
- `LICENSES/Net7` - original Net-7 license header + deed URL
- `LICENSES/Tada-O` - note that tada-o adds no separate license; modifications inherit CC BY-NC-SA 3.0 under ShareAlike
- `LICENSES/enb-linux-installer` - GPLv3 verbatim (governs only `client/linux-installer/`)

Precedence: per-file header > per-folder `LICENSE` > project default. See `LICENSES/README.md` for the full directory-by-directory map.

## Credits

- **Net-7 Entertainment** (2005–2009) - the original team that built the emulator from reverse-engineered protocol work. None of this exists without them.
- **The tada-o contributors** - the post-Net-7 fork that landed the abilities, guild, and combat work consolidated here.
- **kyp / therealkyp** - the 2014 GitHub snapshot that preserved the C# editor suite, packet captures, and architecture docs.
- **Nimsy** - the original WINE-on-Linux guide whose steps became the basis of the installer.
- **ciphersimian** - author of the `enb-linux-installer` script.
- Westwood Studios - *Earth & Beyond* (2002, o7).

## Contributing

Read `CLAUDE.md` first. It explains the plans workflow, repo layout, coding rules, and license precedence. Then look at `plans/00-master.md` to see what's in progress.
