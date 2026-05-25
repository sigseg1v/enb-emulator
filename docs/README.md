# Documentation index

This directory holds the consolidated documentation for the Earth & Beyond
emulator preservation project. New readers should start with `01-overview.md`,
then move to `02-architecture.md`.

The numbered files are stable; the `reference/` subdirectory holds verbatim
copies of historical material from the upstream repos.

## Files

| File | Purpose |
|---|---|
| `01-overview.md` | What this project is, upstream sources, status, license, how to get started. |
| `02-architecture.md` | Server architecture: login flow, sector servers, managers. Derived from the Net-7 architecture RTF plus a code read of `Net7.cpp` and `ServerManager` (the kyp-era `ConnectionManager` cluster was deleted in Phase Q — the proxy and login-server still own the equivalent TCP plumbing). |
| `03-network-protocol.md` | Ports, the client to login to sector handoff, packet framing notes. |
| `04-server-modules.md` | One section per top-level manager class, with `file:line` references into `server/src/`. |
| `05-abilities.md` | Full ability list, marking which were added by the tada-o fork. |
| `06-database-schema.md` | Per-table summary of all 71 tables in `db/mysql/net7.sql`, grouped thematically, with cross-references to the C# editor that owns each. |
| `07-tools-toolchain.md` | One paragraph per C# editor / utility under `tools/`, plus the content pipeline and the Phase L Avalonia status. |
| `08-build.md` | How to build the server (Linux + Windows), the C# tool suite, and the Linux client installer. |
| `09-running-locally.md` | How to bring up the dev stack with `docker compose`, apply the schema, create a test account, and point a client at it. |
| `10-modernization-roadmap.md` | Retrospective on Phases A through T (from `plans/*.md`), what remains, and an explicit list of things deliberately skipped. |
| `11-gm-commands.md` | GM and admin slash commands, reformatted from `reference/gm-commands-original.txt`. |
| `12-content-pipeline.md` | How content flows from the C# editors through the DB into the running server, with per-domain loaders. |
| `13-gameplay-loop.md` | Server-side walkthrough of the major gameplay systems (combat, sector travel, missions, trading, guilds, chat). |
| `14-extending.md` | How to add a new ability, mob type, or sector. |
| `15-cli-client.md` | Headless C# CLI client (`tools/cli-client/`): subcommands, log formats, how to add a new opcode, and the hard rules that keep it honest. |
| `16-integration-tests.md` | Phase T xUnit suite that drives `CliClient.Core` against the live docker-compose stack: layout, fixture accounts, how to add an opcode test, how to add a capture-replay fixture, how to debug a failure locally and in CI. |

## Reference material

`reference/` contains verbatim historical files that the numbered docs are
derived from. Treat them as primary sources; the numbered docs are the
synthesis.

| File | Origin |
|---|---|
| `reference/net7-architecture-original.rtf` | Original Net-7 server architecture document. |
| `reference/faq-original.txt` | Jadefalcon's 2006 server FAQ. |
| `reference/gm-commands-original.txt` | Original GM command list (unformatted). |
| `reference/original-readme.txt` | Original Net-7 README. |

## Conventions

- All numbered docs are CommonMark, ASCII only, no emojis.
- File paths in prose are absolute from the repo root (e.g. `server/src/Net7.cpp`).
- "TODO" markers indicate content that depends on the remaining Phase K
  work (in-game UDP opcode plane) and will be filled in as it completes.
- "Unknown" means exactly that. We do not guess.

## See also

- `../README.md` -- project overview and quickstart for users.
- `../CLAUDE.md` -- repo map, license rules, coding rules.
- `../plans/00-master.md` -- live status of the modernization phases.
