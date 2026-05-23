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
| H | Deepen docs | [08-phase-h-docs.md](08-phase-h-docs.md) | in progress | 2026-05-22 | 2026-05-22 |
| I | Dev env polish | [09-phase-i-dev-env.md](09-phase-i-dev-env.md) | not started | — | — |

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
