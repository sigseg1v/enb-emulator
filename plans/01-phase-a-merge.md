# Phase A — repo merge + Phase B scaffolding

Goal: consolidate the three upstream repos into a clean layout, write docs, write build/dev scaffolding (justfile, docker-compose, CMake skeleton, Postgres schema conversion).

## Items

### A1 — Source merge

- [ ] Copy `tada-o/Source Code/Net7/*.{cpp,h}` and subdirs → `server/src/`. Strip `.svn/`. Preserve license headers verbatim.
      Touches: server/src/
      Notes:
- [ ] Copy `tada-o/Source Code/Net7/Makefile` → `server/Makefile.legacy`.
      Touches: server/Makefile.legacy
      Notes:
- [ ] Copy `tada-o/Source Code/libs/*` → `server/third_party/`. Strip `.svn/`.
      Touches: server/third_party/
      Notes:
- [ ] Copy `tada-o/Source Code/Net7Mysql/` → `login-server/Net7Mysql/`. Verify what it actually is.
      Touches: login-server/
      Notes:
- [ ] Copy `tada-o/Source Code/Net7SSL/` → `login-server/Net7SSL/`.
      Touches: login-server/Net7SSL/
      Notes:
- [ ] Copy `tada-o/Source Code/Net7Proxy/` → `proxy/`.
      Touches: proxy/
      Notes:
- [ ] Copy `tada-o/Source Code/MVASlaunch/` → `launcher/`.
      Touches: launcher/
      Notes:
- [ ] Copy `tada-o/Source Code/ClientDetours/` → `client/detours/`.
      Touches: client/detours/
      Notes:
- [ ] Copy `tada-o/Source Code/Client Mods/` → `client/mods/`.
      Touches: client/mods/
      Notes:
- [ ] Copy `tada-o/net7.sql` + `net7_user.sql` → `db/mysql/`.
      Touches: db/mysql/
      Notes:
- [ ] Copy `tada-o/account for game.txt` → `archive/tada-o-account-note.txt`.
      Touches: archive/
      Notes:
- [ ] Copy `kyp/trunk/Net7Tools/*` → `tools/` (kebab-case rename per tool).
      Touches: tools/
      Notes:
- [ ] Copy `kyp/trunk/Documents/` → `archive/kyp-snapshot/Documents/`.
      Touches: archive/kyp-snapshot/Documents/
      Notes:
- [ ] Copy `kyp/trunk/capturedPackets/` → `archive/kyp-snapshot/capturedPackets/`.
      Touches: archive/kyp-snapshot/capturedPackets/
      Notes:
- [ ] Copy `kyp/trunk/html/` → `archive/kyp-snapshot/html/`.
      Touches: archive/kyp-snapshot/html/
      Notes:
- [ ] Copy `kyp/branches/linux-port/` → `archive/kyp-snapshot/linux-port-legacy/`.
      Touches: archive/kyp-snapshot/linux-port-legacy/
      Notes:
- [ ] Copy `kyp/branches/Net7_TD/` → `archive/kyp-snapshot/Net7_TD/`.
      Touches: archive/kyp-snapshot/Net7_TD/
      Notes:
- [ ] Copy `kyp/trunk/_workspace/` → `archive/kyp-snapshot/_workspace/`.
      Touches: archive/kyp-snapshot/_workspace/
      Notes:
- [ ] Copy `enb-linux-installer/*` (script, README, LICENSE, .github/) → `client/linux-installer/`. PRESERVE GPLv3 LICENSE verbatim.
      Touches: client/linux-installer/
      Notes:
- [ ] Verify no `.svn/` directories anywhere in the merged tree.
      Touches: (audit)
      Notes:
- [ ] Verify Net-7 CC BY-NC-SA 3.0 header still present in every Net-7 source file (count "Net-7 Entertainment" matches).
      Touches: (audit)
      Notes:

### A2 — Binary audit + .gitignore re-includes

- [ ] Enumerate all binary-ish files in merged tree. Classify: build-output (drop), vendor-without-source (keep + note), resource (keep).
      Touches: (audit)
      Notes:
- [ ] Move kept third-party binaries (DLLs, libs we don't have source for) under `vendor/` or alongside their project, with a `THIRD_PARTY_BINARIES.md` listing what+where-from+why.
      Touches: vendor/, server/third_party/, etc.
      Notes:
- [ ] Add `!` re-include rules to `.gitignore` for every kept-binary path so they're not silently ignored.
      Touches: .gitignore
      Notes:

### A3 — Docs

- [ ] `docs/README.md` — index
- [ ] `docs/01-overview.md`
- [ ] `docs/02-architecture.md` — from Net-7 architecture RTF + Net7.cpp/ServerManager/ConnectionManager code reading
- [ ] `docs/03-network-protocol.md` — ports, client→login→sector handoff
- [ ] `docs/04-server-modules.md` — one section per top-level manager class, with file:line refs
- [ ] `docs/05-abilities.md` — full ability list, mark which are tada-o-new
- [ ] `docs/06-database-schema.md` — per-table summary of all 71 tables
- [ ] `docs/07-tools-toolchain.md` — one paragraph per C# editor
- [ ] `docs/08-build.md`
- [ ] `docs/09-running-locally.md`
- [ ] `docs/10-modernization-roadmap.md` — Phase B/C/D/E/F/G/H/I summary with effort estimates
- [ ] `docs/11-gm-commands.md` — reformatted from GMCommands.txt
- [ ] `docs/reference/` — preserved original architecture RTF + FAQ
- [ ] Top-level `README.md`

### A4 — Phase B scaffolding

- [ ] `justfile` with build/lint/test/dev/package/clean targets
- [ ] `docker-compose.yml` with postgres + server + login services
- [ ] `server/CMakeLists.txt` — modern CMake, Linux-first
- [ ] `server/compat/win32_shim.h` — typedef + macro stubs for the common Windows-isms
- [ ] `server/Dockerfile`, `login-server/Dockerfile`
- [ ] `db/postgres/convert.sh` + `db/postgres/schema.sql` — schema conversion script + first-pass output
- [ ] `db/postgres/README.md` — what was converted, what residual fixes are needed
- [ ] `.github/workflows/build.yml` — CI matrix
- [ ] `tests/` scaffold — gtest, one smoke test, README explaining gaps
- [ ] `tools/README.md` — .NET 10 + WinForms + runtime-Windows-only note

## Verification (Phase A done when)

- `ls /data/dev/enb-emulator/` matches the target layout in the master plan
- `git ls-files | grep '\.svn'` returns nothing
- `git ls-files | grep -E '\.(exe|dll|lib|obj|pdb)$'` returns only vendored paths with `THIRD_PARTY_BINARIES.md` siblings
- `head -25 server/src/Net7.cpp | grep -c "Net-7 Entertainment"` returns ≥ 1
- `client/linux-installer/LICENSE` is bit-identical to the upstream GPLv3 file
- `docs/` contains all 11 numbered files plus references, each non-empty
- `just --list` runs without error
- `docker compose config` parses without error
- `psql -f db/postgres/schema.sql` against an empty Postgres 16 succeeds OR fails only on a documented short list

When all of the above are ticked, mark this phase complete in `plans/00-master.md` and proceed to Phase B without stopping.
