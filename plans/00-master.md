# Master plan — Earth & Beyond emulator consolidation & modernization

This is the source of truth for what's done and what's next across invocations. Every Claude invocation in this repo MUST read this file first, then the plan file for the in-progress phase.

## Status table

| # | Phase | File | Status | Started | Last updated |
|---|---|---|---|---|---|
| A | Repo merge + scaffolding | [01-phase-a-merge.md](01-phase-a-merge.md) | complete | 2026-05-22 | 2026-05-22 |
| B | Best-effort Linux server build | [02-phase-b-linux-server.md](02-phase-b-linux-server.md) | complete | 2026-05-22 | 2026-05-22 |
| C | Postgres migration | [03-phase-c-postgres.md](03-phase-c-postgres.md) | complete (scaffold; mysqlplus.cpp rewrite is continuation) | 2026-05-22 | 2026-05-22 |
| D | C# tools → .NET 10 | [04-phase-d-csharp-tools.md](04-phase-d-csharp-tools.md) | complete (16 csproj build clean; itemeditor/enbpatcher deferred — need new csproj) | 2026-05-22 | 2026-05-22 |
| E | OpenSSL 1.0 → 3.x | [05-phase-e-openssl.md](05-phase-e-openssl.md) | complete (server clean under API_COMPAT 3.0; proxy/login-server prepped for later) | 2026-05-22 | 2026-05-22 |
| F | Warning cleanup | [06-phase-f-warnings.md](06-phase-f-warnings.md) | complete (baseline + 2 categories fixed; long-tail deferred per WARNINGS_BASELINE.md) | 2026-05-22 | 2026-05-22 |
| G | Tests | [07-phase-g-tests.md](07-phase-g-tests.md) | complete (3 binaries, CI ctest job, fixed Phase D sln→slnx CI regression; subsystem tests need source isolation first) | 2026-05-22 | 2026-05-22 |
| H | Deepen docs | [08-phase-h-docs.md](08-phase-h-docs.md) | complete (4 new docs + §8 flow walkthroughs + capture analysis in docs/03 §8 with 120k-packet histogram) | 2026-05-22 | 2026-05-23 |
| I | Dev env polish | [09-phase-i-dev-env.md](09-phase-i-dev-env.md) | complete (justfile/Dockerfiles/CI matrix/release.yml/pre-commit; wine-tools profile deferred) | 2026-05-22 | 2026-05-22 |
| J | Runnable end-to-end | [10-phase-j-runnable.md](10-phase-j-runnable.md) | complete (server UDP binds; Net7Proxy bind + RSA+RC4 handshake + framed opcode dispatch on Linux; Net7SSL login binds, terminates TLSv1.3, parses /AuthLogin against MySQL, issues tickets; AF_UNIX SOCK_DGRAM IPC bus replaces Win32 mailslots; live integration tests 5/5; in-game opcode handlers + UDP proxy plane + ticket handoff carry to Phase K) | 2026-05-23 | 2026-05-23 |
| K | In-game opcode handlers + UDP proxy plane | [11-phase-k-ingame.md](11-phase-k-ingame.md) | in progress (TCP 3500 published, UDP plane min-viable subset, per-connection threading in net7ssl, opcode round-trip test wired + promoted to assert response opcodes against captured Win32 bytes; first 4 client-side opcodes ported on Linux: MasterJoin/0x0035 → ServerRedirect, VersionRequest/0x0000 → VersionResponse, sector LOGIN/0x0002 state-change, MasterServer's only opcode complete; PacketStructures.h migrated for all 3 wire structs actively consumed on Linux (ServerRedirect/VersionRequest/MasterJoin → int32_t) — MasterJoin parse now correct (was reading zeros); /who.cgi re-scoped to `[!]` since Win32 reference is also vapor (WhoHtml never defined); 8/8 integration tests green; remaining: UDP-plane-dependent global+sector handlers (blocked behind server-side PlayerManager + 3809 listener — multi-day) and ticket re-scoped as `[!]`) | 2026-05-23 | 2026-05-23 |
| L | C# tools → Avalonia (Linux-native UI) | [12-phase-l-avalonia.md](12-phase-l-avalonia.md) | in progress (Tier 0 + Tier 1 + Tier 2 complete: w3d-parser + ExeUpdater retargeted net10.0; toolspatcher-avalonia + enbpatcher-avalonia ported; commontools-avalonia shared library ported (4 windows + DB-layer UI extraction via DBErrorReporter sink) with passing Avalonia.Headless smoke test → 5/14 tools have Linux-native paths AND the shared library bottleneck for dataimport/faction-editor/mob-editor/talktreeeditor/missioneditor is cleared) | 2026-05-23 | 2026-05-23 |
| M | Eliminate Win32 from server-native code | [13-phase-m-win32-elimination.md](13-phase-m-win32-elimination.md) | in progress — behavioral half done (all 3 compat/ dirs deleted; mailslot→AF_UNIX rewrite for server+login MailslotManager; CreateMutex→flock via net7ipc::SingleInstance; _beginthreadex→pthread_create for every Linux-active call site; OpenSSL opensslconf.h Win32-gating patch removes the last <windows.h> drag; CMakeLists references stripped; CreateEvent/WaitForSingleObject audit: zero live hits — comment-only; vocabulary sweep landed: Sleep→usleep, GetTickCount→Net7TickMs via new common/include/net7/Ticks.h, WSAGetLastError→errno across ~20 .cpp files (commits dc5cfe4 + 2e9937b + 623300c). docker build context fix: per-target Dockerfiles now COPY common/ from repo-root context. 20/20 unit tests + 8/8 live integration green against rebuilt containers). **Remaining:** pure-typedef strip (SOCKET/DWORD/HANDLE/TRUE/FALSE — cosmetic, deferred until Phase K unwalls more TUs). | 2026-05-23 | 2026-05-23 |
| N | `mysqlplus.cpp` → libpqxx rewrite (Phase C continuation) | [14-phase-n-libpqxx-rewrite.md](14-phase-n-libpqxx-rewrite.md) | not started (~2-3 day rewrite of the 7-class wrapper that the entire DAO layer consumes; deferred at Phase C close as "too large for one invocation"; sequenced after Phase M so DB code starts on POSIX-clean ground) | — | 2026-05-23 |
| O | OpenSSL 3.x for proxy + login-server (Phase E continuation) | [15-phase-o-openssl3-rest.md](15-phase-o-openssl3-rest.md) | not started (Phase E migrated server only; proxy + login-server still pinned to OPENSSL_API_COMPAT=0x10100000L because login-server uses SSLv2_client_method which OpenSSL 1.1 removed; this finishes the migration) | — | 2026-05-23 |
| P | Stub-debt audit (kyp-era dead-on-Linux classification) | [16-phase-p-stub-debt-audit.md](16-phase-p-stub-debt-audit.md) | complete — Phase Q deleted rows 1, 3, 4, 6 (kyp-era TCP cluster). Row 5 (`PlayerClass.h` silent stubs: `ChangeProspectSkill`/`AddScanSkill`/`ChangeTractorBeamSpeed`/`SetTCPTerminate`) instrumented to `LogMessage` on entry. Row 8 (`LinuxAuth.cpp` /who.cgi) comment rewritten to make deliberate-no-op intent explicit. Second-pass sweep over 407 marker hits found no further silent-stub hazards. CI drift-tracking script deferred (low ROI now that the dangerous category is empty). | 2026-05-23 | 2026-05-23 |
| Q | Delete dead kyp-era TCP cluster from server/src | [17-phase-q-kyp-cluster-deletion.md](17-phase-q-kyp-cluster-deletion.md) | complete (15 files removed: Connection/ConnectionManager/TcpListener/SSL_Listener/SSL_Connection/ClientTo{Master,Global,Sector}Server/EffectManager/JobManager_DEP_; ServerManager.h shed m_ConnectionMgr field + GetSSLConnection decl + Phase-P loud-abort stubs; PlayerConnection.cpp kept — misleadingly named, it's the live UDP send layer for Player::Send*) | 2026-05-23 | 2026-05-23 |
| R | Extract `common/` for shared protocol headers | [18-phase-r-common-headers.md](18-phase-r-common-headers.md) | complete (Wave 1 + Wave 2: 7 headers consolidated into common/include/net7/ — Opcodes.h, Mutex.h, WestwoodRC4.h, WestwoodRSA.h, PacketStructures.h, Packing.h, Ports.h. All 3 binaries link [163/16/8]; 22/22 tests green incl. 8/8 live. Caught 2 real wire-format drifts: MasterJoin long->int32_t backport to server+login; SECTOR_SERVER_PORT 3500-vs-3501 semantic split into PROXY_LOCAL_TCP_PORT/SECTOR_SERVER_PORT. CLAUDE.md + docs/02-architecture.md §10 updated. Wave 3 (Globals/CircularBuffer/cmdcodes/Net7Types) deferred to backlog — not wire-format.) | 2026-05-23 | 2026-05-23 |

Decisions log: [99-decisions-log.md](99-decisions-log.md).

Status markers:
- `not started` — no work yet
- `in progress` — at least one item ticked off; not all items done
- `complete` — every item in the phase plan is `[x]` or `[!]` with justification
- `blocked` — waiting on external dep / user input

## Iteration rules

1. On startup read this file; pick up at the in-progress phase.
2. **Do not stop at phase boundaries.** Finish current → roll directly into next.
3. As items finish, flip `[ ]` → `[~]` → `[x]` (or `[!]` blocked) in the per-phase file.
4. Update this status table whenever a phase opens or closes.
5. Append meaningful decisions to `99-decisions-log.md`.
6. Legitimate stops: context exhausted, unrecoverable external block, all phases done. Otherwise keep going.
7. If reality and plans diverge, update plans first, then continue.

## Bootstrap log (one-time setup work done before Phase A)

- [x] `plans/` directory and all 11 plan files written
- [x] `CLAUDE.md` created with plans-workflow rules
- [x] Repo skeleton directories created with `.gitkeep`
- [x] `LICENSES/` populated (CC BY-NC-SA 3.0 project default; Net7, Tada-O, enb-linux-installer; README)
- [x] Initial `.gitignore`
- [x] First git commit
