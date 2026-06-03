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
      DELETED 2026-06-03 (commit dd529875). Wave 2 swept every server-native sprintf-SQL call site -- SaveManager's included -- onto the parameterised path, so the shim had zero callers in active code; its comment still claimed SaveManager used it, which was false. Removed per the no-dead-code rule along with the `EscapeHostileLiteral` test that was its only remaining exerciser. (The file is now `server/src/db/sqlplus.h` after task #60's mysql->db rename.)
- [x] Wrapper round-trip test (`tests/db/mysqlplus_wrapper_test.cpp`).
      Notes: 3 env-gated cases: SELECT 1, multi-row VALUES table, hostile-literal escape round-trip. Self-skips when `NET7_TEST_DB_DSN` unset; verified locally (3/3 SKIPPED).
- [x] Live integration suite (the 8 tests behind `tests/it/`) still green against rebuilt container.

### Wave 2 — parameterised API + DAO migration (DONE 2026-05-24)

Audit ratchet drove the server-native sprintf-SQL surface from **148 → 0** for both single-line and multi-line patterns. The audit script itself was upgraded mid-wave to catch the multi-line idiom that was escaping the line-anchored check (commit 75a4ba4); a synthetic injection test confirms it now reports cross-line sites and exits non-zero. From here, new injection sites cannot land without tripping the gate.

- [x] **Add parameterised execute API to the wrapper.** (commit pending — Wave-2 first landing 2026-05-24)
      Notes: `sql_query_c` gains `AddParam(int/long/uint/ulong/double/const char*)`, `AddParamNull()`, `ClearParams()`, and `execute_params(const char *sql)`. Placeholders are `?`; translated to Postgres `$N` internally (with string-literal awareness). Internal `sql_param_bag` keeps `pqxx::params` out of the public header. Param state cleared on success or failure.
- [x] **Audit script landed.** `tools/sql_injection_audit.sh` (commit 7c54053) walks tracked sources in `server/`, `login-server/`, `proxy/`, flags lines that combine `SELECT|INSERT|UPDATE|DELETE|REPLACE|CALL` with `sprintf|snprintf|strcat|strncat|stpcpy`. Tracks `#if WIN32` nesting to exempt walled blocks; excludes vendored/archived trees; LC_ALL=C to handle Latin-1 © glyphs.
      Notes: current baseline is **148 lines across 11 files**, NOT the 36 the original Phase N audit claimed. The Phase N audit table at the top of this file is stale — AccountManager.cpp's 34 sites are *not* walled (they use `sprintf_s` which Net7.h shims via vsnprintf on Linux), so they're live on Linux today. Real surface includes server/src/AccountManager.cpp (20), PlayerSaves.cpp (~24), PlayerConnection.cpp (1), GuildManager.cpp (2), ItemBaseSQL.cpp (4), plus the rest of *SQL.cpp. login-server/Net7Mysql/Tab2.cpp adds 3.
- [x] **LinuxAuth.cpp now uses prepared statements directly.** (commit 7c54053) `ValidateAccountLinux` rewritten on libmysqlclient `mysql_stmt_*` API with `?` placeholders for both the SELECT and the accLogin CALL. `SafeUsername`/`SafePassword` deleted — they were SQL-shape constraints masquerading as input policy and were rejecting valid passwords (`'`/`"`/`\`/`%`).
- [x] **Wrapper round-trip tests for parameterised path.** (commit pending — same as API landing) `tests/db/mysqlplus_wrapper_test.cpp` gets `ExecuteParamsHostileLiteral` (binds `'; DROP TABLE accounts; --` as a parameter and asserts it round-trips as data, not SQL) and `ExecuteParamsMixedTypesAndNull` (int/unsigned long/double/NULL plus `?`→`$N` placeholder rewrite). Both pass live against postgres:16.
- [x] **Per-DAO call-site sweep — server/src/* (148 → 0).**
      Notes: landed across commits 5f4c3d5 (AccountManager.cpp 18 → 0), fbd9068 (PlayerSaves.cpp 40 sites), c3ea4aa (SqlQueryP1 helper + 14 sites), 8ffe3d1 (SaveManager.cpp 54 sites), 3b3fa88 (cross-line sweep across the rest of *SQL.cpp + PlayerConnection.cpp + PlayerMisc.cpp + FactionDataSQL.cpp), c71769a (SectorContentSQL.cpp four multi-line sprintf JOINs), c8c09fe (final: StationLoader.cpp 5 sites, ServerManager.cpp CALL, SkillsDatabaseSQL.cpp, residuals). Pattern in each site: `sprintf(query, "... '%s' ...", value); q.execute(query);` → `q.AddParam(value); q.execute_params("... ? ...");`. PlayerConnection.cpp HandleCommitRequest collapsed 8 column-update calls into a `{sql, double}` table + loop. `update_done` deferred-trigger flags removed because the SQL now runs inline at each switch case.
- [x] **login-server/Net7Mysql/Tab2.cpp (3 sites)** — walled in `#ifdef WIN32` (commit c8c09fe). Tab2.cpp is a Windows MFC `CDialog` admin tab; Net7Mysql has no CMakeLists.txt and the kyp-era mysqlplus bundled inside it has no AddParam API. Wrapping it under WIN32 follows the audit script's documented escape for "truly dead-on-Linux code awaiting a Phase J rewrite" — the actual fix is an Avalonia rewrite of the admin UI in Phase L.
- [x] **Multi-line audit detection** (commit 75a4ba4). The original audit was line-anchored and missed `sprintf_s(buf, sizeof(buf),\n    "SELECT ...", x);` even though it's the same shape and risk. The script now opens a `CROSS_LINE_WINDOW=8` lookahead when it sees an unsafe-build line with unbalanced parens and a trailing comma, and reports the original line when a subsequent line contains a SQL keyword *inside a string literal* (the literal requirement avoids the obvious false-positive of bare identifiers like `XML_TAG_ID_FOO_UPDATE` in switch cases). Verified with a synthetic injection: detection works and exit code goes to 1.
- [x] Extend `postgres_smoke_test` to a real DAO round-trip + hostile-input case (currently only the wrapper test exercises this; the DAO test still uses raw libpq).
      Closed 2026-06-03 as already-satisfied, not by extending `postgres_smoke_test`. The DAO API *is* the sqlplus wrapper (`sql_connection_c`/`sql_query_c`), and `sqlplus_wrapper_test` already exercises it end-to-end: `ConnectsAndSelectsOne`, `ParameterisedRoundTrip`, `ExecuteParamsMixedTypesAndNull`, and `ExecuteParamsHostileLiteral` (binds `'; DROP TABLE accounts; --` as a *parameter* and asserts it round-trips as literal data, not SQL). That is the real DAO round-trip + hostile-input coverage this item wanted, and through the parameterised path the server actually uses. The remaining `postgres_smoke_test` (raw libpq) stays a pure connectivity check (`SELECT 1`) on purpose -- pulling the wrapper into it would only duplicate `sqlplus_wrapper_test`. Note: the former `EscapeHostileLiteral` case (which exercised the now-deleted `mysql_escape_string` escape shim) was removed in the same 2026-06-03 cleanup -- the parameterised `ExecuteParamsHostileLiteral` supersedes it and tests the correct mechanism.
- [x] Wire `tools/sql_injection_audit.sh` to CI as a non-zero-exit gate (DONE 2026-06-02). Added the `sql-injection-audit` job to `.github/workflows/build.yml` (modeled on `check-no-mojibake`: checkout + run script). The script exits 1 on any flagged site, 0 at the current baseline; verified locally at exit 0.

### Pre-existing bug to fix opportunistically (DONE 2026-06-02)

- [x] `sql_var_c::operator unsigned long()` was `(unsigned long)atoi(value)` -- atoi parses as int32, so any value >= 0x80000000 sign-extended to a huge unsigned long. Fixed to `strtoul(value, nullptr, 10)`. Also fixed `operator long()` (`(long)atoi` -> `strtol`) which truncated to 32 bits before widening to 64-bit `long`. `operator unsigned int()` was already on strtoul. Regression coverage: `ExecuteParamsMixedTypesAndNull` now binds+reads `0xFFFFFFFE` (was 0x12345678, which dodged the bug) and the comment reflects the fix.

### Wave 3 -- dialect cleanup (DONE 2026-06-02)

- [x] Per-DAO query rewrites where the SQL itself is MySQL-specific (`LIMIT offset, count`, `INTERVAL` arithmetic, etc.).
      Notes: **syntax sweep came back EMPTY** (as predicted). Swept all
      server-native SQL strings for `LIMIT n,m` / `IFNULL` / MySQL `IF()`
      ternary / `REPLACE INTO` / `ON DUPLICATE KEY` / `LAST_INSERT_ID` /
      `AUTO_INCREMENT` / MySQL date funcs / `RAND` / `GROUP_CONCAT` /
      `STRAIGHT_JOIN` / `CAST AS UNSIGNED` / `CONVERT` / `INSERT IGNORE` /
      `GROUP BY` non-aggregate / `SET @var` / multi-statement / DDL-in-code.
      Zero real hits -- only vendored MySQL error-code headers, C++
      `unsigned int` decls, and commented log lines. DAOs are static
      `SELECT * FROM table` loaders that run straight through libpqxx.
- [x] **Collation divergence (the one real Wave-3 finding).** The
      MySQL->Postgres conversion silently dropped `latin1_swedish_ci`
      (case-insensitive) -> `C.UTF-8` (case-sensitive). `convert.sh` now
      appends a citext fidelity block to the generated `net7_user`
      `seed.sql`: `accounts.username` / `avatar_data.first_name` /
      `forbidden_names.nickname` become `citext` (+ length CHECKs), so
      login lookup, character-name uniqueness, and the forbidden-name
      filter/PK match case-insensitively as on the real server.
      Transparent to the DAOs (no query change). Verified before/after at
      the SQL layer (forbidden_names PK now rejects a recapitalised dup).
      Primary source: the dump's `latin1_swedish_ci` collation. Full
      writeup in plans/99-decisions-log.md (2026-06-02 addendum 2).

### Wave 4 — runtime-path Postgres correctness regressions (2026-06-02, commit e1976506)

Found via the CLAUDE.md boot-log audit, NOT by a test: a live boot
logged ~8700 SQL errors per sector-load pass in two distinct bugs. Both
are the kind of MySQL->Postgres divergence the unit/integration suites
miss because they never drive these specific runtime queries against a
live Postgres. Full diagnosis in plans/99-decisions-log.md (2026-06-02
entry). These were NOT injection sites (Wave 2 already parameterised
both files in c71769a/c8c09fe) -- they are correctness bugs Wave 2's
parameterisation didn't touch.

- [x] **`StationLoader.cpp::AddNPCs` -- case-fold on `npc_Id`.** Bare
      `starbase_npcs.npc_Id` in the join ON-clauses folded to `npc_id`
      on Postgres -> `column ... does not exist` -> the join failed and
      ALL starbase NPCs silently failed to load (1776 pg / 222
      server-side errors/boot). Fix: backtick-quote
      `` `starbase_npcs`.`npc_Id` ``. **Same case-fold class as the
      Phase-N canonical incident** (`"npc_Id"`/`"mission_XML"`/
      `"EffectID"`), now confirmed to recur in a runtime-path JOIN.
- [x] **`SectorContentSQL.cpp::ParseSectorContent` -- cross-database
      read.** The per-field respawn read hit
      `server_local_field_respawn_times` on the `net7` content
      connection, but that table lives in `net7_user` (Postgres DBs are
      isolated; MySQL schemas were not) -> `relation ... does not exist`
      ~6904x/pass; respawn timers never restored. Fix: open a
      `net7_user` handle and route the read through it.
- [x] **`mysqlplus.cpp::sql_result_c::table()` -- compound `table.field`
      row lookup broken (commit 8f36e7b1).** Found on the clean-boot
      re-verification: ~6005 `Field \`item_manufacturer_base.name\` does
      not exist in this table ''` per item-load pass (5012 manufacturer
      + 650+292 ammo_type). The Wave-1 rewrite stubbed `table()` to ""
      because libpqxx exposes a column's table as an OID not a name, but
      `sql_row_c::operator[](char*)` builds its compound match key from
      `table()+field()` -- so every `["item_manufacturer_base.name"]` /
      `["item_ammo_type.name"]` / `["item_ammo_type.sub_category"]` in
      ItemBaseSQL.cpp fell through and returned empty. These are
      `SELECT *` joins where BOTH tables carry a `name` column, so a
      bare-name lookup would silently return the ITEM name as the
      manufacturer -- every item loaded with an empty manufacturer and
      every ammo item with AmmoTypeNum=0. **Same empty-payload class as
      the case-fold incident.** Fix: resolve column table OID -> relname
      via pg_class at execute() time, cached per-connection, returned
      from `table()`. NB this was NOT a NET7 vs Postgres dialect issue --
      it was a wrapper-completeness gap; the MySQL `mysql_fetch_field`
      gave table names for free.
- [x] **Regression guard (the gap that let all three ship).** The
      integration suite asserted opcode round-trips but never that the
      boot log is clean. Added `Smoke/BootLogHealthTests.cs` (Phase T)
      with two `[Fact]`s on a `ServerFixture`-owned clean boot:
      - `Postgres_HasNoSchemaDoesNotExistErrors` -- fails if the postgres
        log has an `ERROR:` line matching `(relation|column) ... does
        not exist` (catches the case-fold + cross-DB classes: Bug A/B).
      - `Server_HasNoSqlReadFailures` -- fails if the server log has
        `does not exist in this table` (the compound-lookup class: Bug C)
        or `Error reading with MySQL` (any DAO SELECT failure).

      Both have a verified true-zero baseline on a volume-wiped boot.
      Supporting changes:
      - `ServerFixture.CaptureServiceLogsAsync(service)` -- reads a
        service's full `docker compose logs`; works in both owns-compose
        and `CLI_INTEGRATION_SKIP_COMPOSE=1` modes.
      - The postgres assertion is scoped to lines carrying
        `app=net7-server`: `mysqlplus.cpp::grabdb` now tags every server
        libpqxx connection `application_name=net7-server`, and
        docker-compose puts `%a` in postgres `log_line_prefix`. This
        stops ad-hoc `psql`/pgadmin on a shared dev stack from tripping
        the guard -- only the server's own SQL counts. Verified
        end-to-end: a `net7-server`-tagged failing query logs
        `app=net7-server`; a differently-tagged one does not match.
      Committed alongside this plan update. (The Wave-2 item "extend
      postgres_smoke_test to a real DAO round-trip" stays open -- this is
      a log-health guard, complementary to a positive round-trip check.)

## Decisions deferred

- libpqxx version pin (recommend tracking whatever ubuntu-24.04 ships; cross-distro pin if needed).
- Connection pooling: libpqxx itself doesn't pool; the existing code already uses one connection per worker. Leave the policy alone for the rewrite; revisit later if needed.
