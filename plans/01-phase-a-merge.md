# Phase A — repo merge + Phase B scaffolding

Goal: consolidate the three upstream repos into a clean layout, write docs, write build/dev scaffolding (justfile, docker-compose, CMake skeleton, Postgres schema conversion).

## Items

### A1 — Source merge

- [x] Copy `tada-o/Source Code/Net7/*.{cpp,h}` and subdirs → `server/src/`. Strip `.svn/`. Preserve license headers verbatim.
      Notes: 300 files contain "Net-7 Entertainment" header (verified). Files are ISO-8859 with CRLF; need `--binary-files=text` for grep.
- [x] Copy `tada-o/Source Code/Net7/Makefile` → `server/Makefile.legacy`.
- [x] Copy `tada-o/Source Code/libs/*` → `server/third_party/`. Strip `.svn/`.
- [x] Copy `tada-o/Source Code/Net7Mysql/` → `login-server/Net7Mysql/`.
- [x] Copy `tada-o/Source Code/Net7SSL/` → `login-server/Net7SSL/`.
- [x] Copy `tada-o/Source Code/Net7Proxy/` → `proxy/`.
- [x] Copy `tada-o/Source Code/MVASlaunch/` → `launcher/`.
- [x] Copy `tada-o/Source Code/ClientDetours/` → `client/detours/`.
- [x] Copy `tada-o/Source Code/Client Mods/` → `client/mods/`.
- [x] Copy `tada-o/net7.sql` + `net7_user.sql` → `db/mysql/`.
- [x] Copy `tada-o/account for game.txt` → `archive/tada-o-account-note.txt`.
- [x] Copy `kyp/trunk/Net7Tools/*` → `tools/` (kebab-case rename per tool).
- [x] Copy `kyp/trunk/Documents/` → `archive/kyp-snapshot/Documents/`.
- [x] Copy `kyp/trunk/capturedPackets/` → `archive/kyp-snapshot/capturedPackets/`.
- [x] Copy `kyp/trunk/html/` → `archive/kyp-snapshot/html/`.
- [x] Copy `kyp/branches/linux-port/` → `archive/kyp-snapshot/linux-port-legacy/`.
- [x] Copy `kyp/branches/Net7_TD/` → `archive/kyp-snapshot/Net7_TD/`.
- [x] Copy `kyp/trunk/_workspace/` → `archive/kyp-snapshot/_workspace/`.
- [x] Copy `enb-linux-installer/*` (script, README, LICENSE, .github/) → `client/linux-installer/`. PRESERVE GPLv3 LICENSE verbatim. (Verified diff == 0.)
- [x] Verify no `.svn/` directories anywhere in the merged tree. (Found 166, removed all.)
- [x] Verify Net-7 CC BY-NC-SA 3.0 header still present in every Net-7 source file. (300 hits across server/src.)

### A2 — Binary audit + .gitignore re-includes

- [x] Enumerate all binary-ish files in merged tree. Classify: build-output (drop), vendor-without-source (keep + note), resource (keep).
      Notes: dropped Debug/, Release/, obj/, .ncb, .suo, .user, .pdb, .obj, .sbr, .pfx (code-signing keys), .ipdb, .iobj, .ipch, .tlog, .aps, .opensdf, .sdf, .VC.db.
- [x] Move kept third-party binaries (DLLs, libs we don't have source for) under `vendor/` or alongside their project, with a `THIRD_PARTY_BINARIES.md` listing.
      Notes: kept in-place (server/third_party/, server/src/LUA/, client/mods/release/, tools/<each>/Libs/). Wrote 3 THIRD_PARTY_BINARIES.md files (server/third_party/, client/mods/, tools/).
- [x] Add `!` re-include rules to `.gitignore` for every kept-binary path so they're not silently ignored.
      Notes: only `login-server/Net7Mysql/res/Thumbs.db` remains ignored (OS junk, correct).

### A3 — Docs

- [x] `docs/README.md` — index
      Notes: Doc index with one-line per-file descriptions, conventions, reference-material table; points readers at 01-overview then 02-architecture.
- [x] `docs/01-overview.md`
      Notes: Preservation framing, three upstreams with credit, current Phase A/B-I status, NC license summary, get-started paths for client/server/tools.
- [x] `docs/02-architecture.md` — from Net-7 architecture RTF + Net7.cpp/ServerManager/ConnectionManager code reading
      Notes: 811 lines. Process topology (Master+Global+Auth or Sector), startup from Net7.cpp main():91, server roles, MailslotManager IPC, UDP_SSLcomms, main loop (50ms tick / ~10Hz movement), database layer, in-memory managers, sector mgmt. Three mermaid diagrams (topology, startup sequence, login flow). Heavy file:line refs.
- [x] `docs/03-network-protocol.md` — ports, client→login→sector handoff
      Notes: 617 lines. UDP transport, packet framing (EnbTcpHeader 4B / EnbUdpHeader 12B), Westwood RSA-512 + RC4 + OpenSSL, port table (443, 3500-3501+, 3801, 3805, 3806, 3807, 3808, 3809), login flow + sector handoff (three mermaid diagrams), full opcode tables for 0x00xx/0x10xx/0x20xx/0x30xx/0x40xx/0x50xx + server-to-server 0x78xx-0x79xx, captured-packets reference, "Unknown from code reading" section called out.
- [x] `docs/04-server-modules.md` — one section per top-level manager class, with file:line refs
      Notes: 1086 lines. Seven groupings: ServerManager/ConnectionManager/SectorServerManager/MailManager/SaveManager/StringManager+GMemoryHandler; SectorManager/ObjectManager/EffectManager; PlayerManager + Player + CMob/MOB/MOBSpawn + CMobBuffs + Equipable; AccountManager; Groups + GuildManager; ItemBaseManager + 5 other content loaders; UDP_Connection + Connection + SSL_Listener/SSL_Connection. Constructor/method/field refs throughout.
- [x] `docs/05-abilities.md` — full ability list, mark which are tada-o-new
      Notes: 310 lines. Skill-vs-ability distinction (58 skills, 138 ability IDs, MAX_ABILITY_IDS=138), AbilityBase lifecycle (Use→Update→Execute, Confirmation), m_AbilityList[138] O(1) dispatch, SetupAbilities() mapping from CMobClass.cpp:885-1091, dispatch flowchart, tada-o-new annotation (22 abilities flagged), full table of 28 classes with ability IDs handled + summaries + tada-o-new flag, "Gaps and known issues" (AHacking's wrong skill ID, COMPULSORY_CONTEMPLATION not implemented, ARally rank-6 gap, AAfterburn memory-leak comment), add-an-ability cookbook.
- [x] `docs/06-database-schema.md` — per-table summary of all 71 tables
      Notes: All 71 net7.sql tables grouped thematically (world/assets/mobs/avatars/items/effects/skills/factions/starbases/missions/audit); per-table PK/cols/key cols/FKs/owning editor; full net7_user.sql 42-table list at end; editor-to-table cross-reference; Postgres conversion gotchas (preserved sic spellings, binary(1), zerofill, identifier folding).
- [x] `docs/07-tools-toolchain.md` — one paragraph per C# editor
      Notes: All 21 tools covered (17 in Net7Tools.sln + 4 legacy VC6 .dsp + 1 standalone .sln); per-tool purpose, type, entry point, dependencies; content-pipeline section (editor → DB → server); Phase D upgrade matrix; runtime requirements (WinForms means Windows-only at runtime).
- [x] `docs/08-build.md`
      Notes: Linux CMake (Phase B in progress), Windows VS server build, legacy Makefile reference, C# tools (.NET 10 SDK), Linux client installer, dev env via just+docker, Debian/Fedora/Arch/Windows dependency lists, troubleshooting.
- [x] `docs/09-running-locally.md`
      Notes: docker-compose stack walkthrough (postgres+login+server), schema apply via psql, test account creation paths, client connection (Linux+Windows), expected-by-phase table; ports flagged as TODO pending Phase B output.
- [x] `docs/10-modernization-roadmap.md` — Phase B/C/D/E/F/G/H/I summary with effort estimates
      Notes: Per-phase goal/approach/effort/risks/deliverables for B-I derived from plans/02..09; explicit "deliberately skipped" section (no Avalonia, no full RE, no engine rewrite, no DRM-free distribution, no commercial use, no asio migration, no Crypto++ replacement, etc.); total effort estimate (4-6 months FTE / 12-18 months weekend hacking).
- [x] `docs/11-gm-commands.md` — reformatted from GMCommands.txt
      Notes: Reformatted faithfully from reference/gm-commands-original.txt; split into account admin (`//`) and in-game admin (`/`) sections; per-command table with Description and Example; noted that command list is partial and access-level numerics are unverified.
- [x] `docs/reference/` — preserved original architecture RTF + FAQ
      Notes: faq-original.txt, gm-commands-original.txt, net7-architecture-original.rtf, original-readme.txt all in place under docs/reference/.
- [x] Top-level `README.md` (done during bootstrap)

### A4 — Phase B scaffolding

- [x] `justfile` with build/lint/test/dev/package/clean targets
      Notes: /data/dev/enb-emulator/justfile. `just --list` succeeds (16 recipes).
- [x] `docker-compose.yml` with postgres + server + login services
      Notes: /data/dev/enb-emulator/docker-compose.yml. Includes schema-init job, optional `pgadmin` (profile `dev-tools`). `docker compose config` parses.
- [x] `server/CMakeLists.txt` — modern CMake, Linux-first
      Notes: /data/dev/enb-emulator/server/CMakeLists.txt. GLOB_RECURSE with MSVC-output exclusions; finds 181 .cpp under src/ post-exclude. Configure surfaces the expected missing-Lua-dev error cleanly until liblua5.4-dev is installed.
- [x] `server/compat/win32_shim.h` — typedef + macro stubs for the common Windows-isms
      Notes: /data/dev/enb-emulator/server/compat/win32_shim.h plus threading_shim.{h,cpp}, mailslot_shim.{h,cpp}, README.md. Threading + mailslot are STUBS — flagged in README status table.
- [x] `server/Dockerfile`, `login-server/Dockerfile`
      Notes: /data/dev/enb-emulator/server/Dockerfile (real multi-stage build); /data/dev/enb-emulator/login-server/Dockerfile (scaffolding — conditional on a future login-server/CMakeLists.txt).
- [x] `db/postgres/convert.sh` + `db/postgres/schema.sql` — schema conversion script + first-pass output
      Notes: /data/dev/enb-emulator/db/postgres/convert.sh (executable). Generates schema.sql (55433 lines, 71 CREATE TABLE) and seed.sql (2931 lines, 42 CREATE TABLE) from db/mysql/. Residual issues documented in README.md.
- [x] `db/postgres/README.md` — what was converted, what residual fixes are needed
      Notes: /data/dev/enb-emulator/db/postgres/README.md. Covers the `text`, escape-string, NUL-byte, hex-literal, tinyint(1), index-extraction, FK, reserved-word, and ON UPDATE issues for Phase C.
- [x] `.github/workflows/build.yml` — CI matrix
      Notes: /data/dev/enb-emulator/.github/workflows/build.yml. 6 jobs: lint-plans, cmake-configure, cmake-build (continue-on-error, uploads BUILD_ERRORS.md), db-schema (hard requirement), dotnet-build (continue-on-error pre-Phase-D), installer-shellcheck.
- [x] `tests/` scaffold — gtest, one smoke test, README explaining gaps
      Notes: /data/dev/enb-emulator/tests/CMakeLists.txt (FetchContent GoogleTest v1.15.2), /data/dev/enb-emulator/tests/smoke_test.cpp, /data/dev/enb-emulator/tests/README.md.
- [x] `tools/README.md` — .NET 10 + WinForms + runtime-Windows-only note
      Notes: /data/dev/enb-emulator/tools/README.md. Lists all 21 tools, documents build vs. runtime split, pre-Phase-D caveat.

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
