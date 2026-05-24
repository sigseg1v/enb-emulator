# Phase N — `mysqlplus.cpp` → libpqxx rewrite

## Scope

Phase C delivered the Postgres schema, the `MIGRATION_PATTERN.md` translation guide, and the docker-compose `postgres` service. It explicitly **deferred** the actual `mysqlplus.cpp` rewrite as "too large for one invocation (733 lines of API translation across 7 classes; ~2-3 days fluent libpqxx work)."

Phase N is that rewrite.

The wrapper at `server/src/mysqlplus/mysqlplus.{cpp,h}` exposes 7 classes (`sql_connection_c`, `sql_query_c`, `sql_result_c`, etc.) that the entire server uses for DB I/O. Every `AssetDatabaseSQL.cpp`, `BuffDatabaseSQL.cpp`, `FactionDataSQL.cpp`, `ItemBaseManager.cpp`, etc. consumes this wrapper. Reimplementing it over libpqxx switches every DAO over without touching the call sites.

## Why now (after Phase M)

Sequence rationale: Phase M removes `_snprintf`/`stricmp`/etc. across the codebase. `mysqlplus.cpp` is full of them. Doing M first means N starts on POSIX-clean ground. Also: ServerManager and ConnectionManager invoke DB ops on threads that are themselves being rewritten in M; better to have one pthread refactor than two.

## Definition of done

- `server/src/mysqlplus/mysqlplus.{cpp,h}` reimplemented over libpqxx; same 7 classes with the same method signatures so DAOs don't change.
- `CMakeLists.txt` swaps `find_package(MySQL)` → `find_package(libpqxx CONFIG REQUIRED)`.
- All ~25 `*SQL.cpp` DAO files compile against the new wrapper without source changes (or with the minimal changes that the MIGRATION_PATTERN doc anticipates).
- `tests/postgres_smoke_test` (env-gated) extends to exercise at least one full DAO round-trip end-to-end against the docker-compose Postgres.
- docker-compose `server` image links libpqxx, drops libmysqlclient.
- `db/mysql/` keeps the original dumps as archival reference; new code targets only `db/postgres/`.

## Anti-scope

- Don't rewrite the DAOs (`AssetDatabaseSQL.cpp` et al). Wrapper-only.
- Don't change the SQL dialect of the *queries* yet — that's Phase N+ if needed. Postgres handles most MySQL flavour with libpqxx straight-through (backticks already swapped in Phase C schema conversion).
- Don't touch login-server's `Net7Mysql/` yet (that's Phase R territory — same job for the auth server).

## Items (placeholder — flesh out when Phase N starts)

- [ ] Re-read `MIGRATION_PATTERN.md` and confirm it's still accurate.
- [ ] Reimplement `sql_connection_c` (connect / disconnect / grabdb).
- [ ] Reimplement `sql_query_c` (parameterized queries via libpqxx `params`).
- [ ] Reimplement `sql_result_c` (row iteration).
- [ ] Reimplement `sql_param_c` (parameter binding).
- [ ] Reimplement the remaining 3 classes (`sql_field_c`, `sql_row_c`, error wrappers).
- [ ] CMakeLists update.
- [ ] DAO compile pass — fix the minor friction the migration doc anticipated.
- [ ] Wire `postgres_smoke_test` to a real DAO call.
- [ ] Drop libmysqlclient from server's Dockerfile.

## Decisions deferred

- libpqxx version pin (recommend tracking whatever ubuntu-24.04 ships; cross-distro pin if needed).
- Connection pooling: libpqxx itself doesn't pool; the existing code already uses one connection per worker. Leave the policy alone for the rewrite; revisit later if needed.
