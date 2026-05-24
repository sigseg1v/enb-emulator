# Phase N — `mysqlplus.cpp` → libpqxx rewrite

## Scope

Phase C delivered the Postgres schema, the `MIGRATION_PATTERN.md` translation guide, and the docker-compose `postgres` service. It explicitly **deferred** the actual `mysqlplus.cpp` rewrite as "too large for one invocation (733 lines of API translation across 7 classes; ~2-3 days fluent libpqxx work)."

Phase N is that rewrite.

The wrapper at `server/src/mysqlplus/mysqlplus.{cpp,h}` exposes 7 classes (`sql_connection_c`, `sql_query_c`, `sql_result_c`, etc.) that the entire server uses for DB I/O. Every `AssetDatabaseSQL.cpp`, `BuffDatabaseSQL.cpp`, `FactionDataSQL.cpp`, `ItemBaseManager.cpp`, etc. consumes this wrapper. Reimplementing it over libpqxx switches every DAO over without touching the call sites.

## Why now (after Phase M)

Sequence rationale: Phase M removes `_snprintf`/`stricmp`/etc. across the codebase. `mysqlplus.cpp` is full of them. Doing M first means N starts on POSIX-clean ground. Also: ServerManager and ConnectionManager invoke DB ops on threads that are themselves being rewritten in M; better to have one pthread refactor than two.

## Definition of done

- `server/src/mysqlplus/mysqlplus.{cpp,h}` reimplemented over libpqxx; same 7 classes with the same method signatures so DAOs don't change.
- **Parameterised statements are the only execution path.** No `sprintf`/`snprintf` + raw query string. The wrapper's `execute()` must take a query template and bound parameters; the legacy single-arg `execute(char *sql)` either becomes parameter-less (no `%s`/`%d` allowed) or is deleted outright.
- `CMakeLists.txt` swaps `find_package(MySQL)` → `find_package(libpqxx CONFIG REQUIRED)`.
- All ~25 `*SQL.cpp` DAO files compile against the new wrapper without source changes (or with the minimal changes that the MIGRATION_PATTERN doc anticipates).
- **SQL-injection audit gate**: a `tools/sql_injection_audit.sh` (grep-based) reports zero call sites in server-native code that build query strings with embedded user/dynamic data via `sprintf*`/`snprintf*`/`strcat`. Walled `#ifdef WIN32` blocks are exempt (they don't run on Linux); everything else must use the parameterised wrapper. CI runs this on every PR.
- `LinuxAuth.cpp` `SafeUsername`/`SafePassword` are **deleted** once the parameterised path is in. They exist purely because the current wrapper has no parameter binding — a defense-in-depth band-aid that becomes redundant the moment `tx.exec_params(...)` is the only way to talk to the DB.
- `tests/postgres_smoke_test` (env-gated) extends to exercise at least one full DAO round-trip end-to-end against the docker-compose Postgres, including at least one query with a hostile-input string (`'; DROP TABLE accounts; --`) that must round-trip as literal data, not execute.
- docker-compose `server` image links libpqxx, drops libmysqlclient.
- `db/mysql/` keeps the original dumps as archival reference; new code targets only `db/postgres/`.

## Current dynamic-SQL surface (audit done 2026-05-23)

Sites that currently build queries with `sprintf`/`snprintf` + embedded values, by file:

| File | Dynamic-SQL sites | Reached on Linux today? |
|---|---|---|
| `login-server/Net7SSL/AccountManager.cpp` | 34 | No (Phase-J-walled WIN32) — but its Linux replacement uses the same pattern |
| `login-server/Net7SSL/LinuxAuth.cpp` | 2 (`ValidateAccountLinux`, `accLogin`) | **Yes** — gated by `SafeUsername`/`SafePassword` band-aid |
| `server/src/ServerManager.cpp:185` | 1 (`logoutOnShutdown` with `strftime` timestamp) | Yes — not user-controlled |
| `server/src/*SQL.cpp` (Asset/Buff/Faction/Item/Mission/MOB/Sector/Skills) | 0 dynamic; all are static `SELECT * FROM table` loaders | Yes |
| `proxy/*` | 0 | Yes (proxy doesn't talk to DB) |

The walled `AccountManager.cpp` is dormant on Linux now, but the moment Phase J finishes and unwalls it, all 34 sites become live injection vectors. Phase N must land before that.

## Anti-scope

- Don't rewrite the DAOs (`AssetDatabaseSQL.cpp` et al). Wrapper-only.
- Don't change the SQL dialect of the *queries* yet — that's Phase N+ if needed. Postgres handles most MySQL flavour with libpqxx straight-through (backticks already swapped in Phase C schema conversion).
- Don't touch login-server's `Net7Mysql/` yet (that's Phase R territory — same job for the auth server).

## Items (placeholder — flesh out when Phase N starts)

- [ ] Re-read `MIGRATION_PATTERN.md` and confirm it's still accurate.
- [ ] Reimplement `sql_connection_c` (connect / disconnect / grabdb).
- [ ] Reimplement `sql_query_c` so its `execute()` signature **requires** a parameter pack (or `pqxx::params`). The legacy `execute(char *sql)` becomes a deprecated overload that asserts on `%`-characters in the string.
- [ ] Reimplement `sql_result_c` (row iteration).
- [ ] Reimplement `sql_param_c` (parameter binding) — back it with `pqxx::params::append()`.
- [ ] Reimplement the remaining 3 classes (`sql_field_c`, `sql_row_c`, error wrappers).
- [ ] CMakeLists update.
- [ ] DAO compile pass — fix the minor friction the migration doc anticipated.
- [ ] Convert the 36 dynamic-SQL sites listed above to parameterised form, one file at a time. After each file: re-run the audit grep, confirm count drops by N.
- [ ] Delete `SafeUsername`/`SafePassword` from `LinuxAuth.cpp`.
- [ ] Add `tools/sql_injection_audit.sh` and wire to CI.
- [ ] Wire `postgres_smoke_test` to a real DAO call + hostile-input round-trip.
- [ ] Drop libmysqlclient from server's Dockerfile.

## Decisions deferred

- libpqxx version pin (recommend tracking whatever ubuntu-24.04 ships; cross-distro pin if needed).
- Connection pooling: libpqxx itself doesn't pool; the existing code already uses one connection per worker. Leave the policy alone for the rewrite; revisit later if needed.
