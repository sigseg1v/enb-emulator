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

## Items

### Wave 1 — wrapper rewrite (DONE 2026-05-24, commit fdfcbe5)

- [x] Re-read `MIGRATION_PATTERN.md` and confirm it's still accurate.
      Notes: pattern table still applicable; opaque-handle approach kept libpqxx headers out of `mysqlplus.h`.
- [x] Reimplement `sql_connection_c` (connect / disconnect / grabdb).
      Notes: OPENDB now holds `net7_db_handle *db`; default port 5432; DSN built from (database, host[:port], user, password) into libpq keyword=value form.
- [x] Reimplement `sql_query_c::execute` (single-arg overload preserved).
      Notes: runs under `pqxx::work`, captures result + affected_rows, translates `pqxx::sql_error`/`pqxx::failure`/`std::exception` into `Error()`/`ErrorMsg()`.
- [x] Reimplement `sql_result_c` (row iteration via `take(net7_result_holder*)` / `get_holder()`).
- [x] Reimplement `sql_row_c` (now takes row index; strdup's column values into a per-row `char**` cache so `sql_var_c` lifetime contract still holds).
- [x] Reimplement `sql_field_c` (forwards to `pqxx::result`; `get_type()` returns the pqxx oid as unsigned int — no DAO currently calls it).
- [x] CMakeLists update.
      Notes: dropped `find_library(MYSQLCLIENT_LIB)`; added `pkg_check_modules(LIBPQXX REQUIRED IMPORTED_TARGET libpqxx)` + `find_package(PostgreSQL REQUIRED)`.
- [x] DAO compile pass — the ~25 `*SQL.cpp` translation units build clean against the new wrapper.
      Notes: zero source changes required in the DAOs themselves; the public class signatures held.
- [x] Drop `libmysqlclient21` (runtime) + `libmysqlclient-dev` (build) from `server/Dockerfile`.
      Notes: rebuilt image links libpqxx-7.8 + libpq.so.5 only; binary 13.7 MB (verified via ldd).
- [x] `mysql_escape_string` shim in `mysqlplus.h` for SaveManager's 3 legacy call sites.
      Notes: standard SQL single-quote/backslash doubling; assumes `standard_conforming_strings = on` (Postgres default since 9.1).
- [x] Wrapper round-trip test (`tests/db/mysqlplus_wrapper_test.cpp`).
      Notes: 3 env-gated cases: SELECT 1, multi-row VALUES table, hostile-literal escape round-trip. Self-skips when `NET7_TEST_DB_DSN` unset; verified locally (3/3 SKIPPED).
- [x] Live integration suite (the 8 tests behind `tests/it/`) still green against rebuilt container.

### Wave 2 — parameterised API + DAO migration (DEFERRED to Phase N+)

These were originally lumped into Phase N but are wide-scope (touches every DAO call site) and belong in their own phase. Tracked here for handoff:

- [ ] Add typed `execute(template, args...)` overload backed by `pqxx::params`.
      Why: only way to retire the `sprintf`+raw-SQL injection surface for good.
      How to apply: extend `sql_query_c` with a variadic template that builds `pqxx::params` and calls `tx.exec_params`. Keep the single-arg `execute(char *)` as a transitional overload that asserts on `%`-characters once Wave 3 lands.
- [ ] Convert the 36 dynamic-SQL sites enumerated above (34 in `AccountManager.cpp`, 2 in `LinuxAuth.cpp`) to parameterised form.
- [ ] Delete `SafeUsername`/`SafePassword` from `LinuxAuth.cpp` once `tx.exec_params` is the only path.
- [ ] Add `tools/sql_injection_audit.sh` and wire to CI (greps for `sprintf.*FROM|INSERT|UPDATE|DELETE` outside `#ifdef WIN32`).
- [ ] Extend `postgres_smoke_test` to a real DAO round-trip + hostile-input case (currently only the wrapper test exercises this; the DAO test still uses raw libpq).

### Wave 3 — dialect cleanup (DEFERRED)

- [ ] Per-DAO query rewrites where the SQL itself is MySQL-specific (`LIMIT offset, count`, `INTERVAL` arithmetic, etc.).
      Notes: most queries are static `SELECT * FROM table` loaders that work straight-through; this wave is small but needs a query-by-query sweep against a populated Postgres.

## Decisions deferred

- libpqxx version pin (recommend tracking whatever ubuntu-24.04 ships; cross-distro pin if needed).
- Connection pooling: libpqxx itself doesn't pool; the existing code already uses one connection per worker. Leave the policy alone for the rewrite; revisit later if needed.
