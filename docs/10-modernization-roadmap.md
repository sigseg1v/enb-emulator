# 10 - Modernization roadmap

A summary of phases B through I from the per-phase plan files under
`plans/`. Authoritative source for current status is
`plans/00-master.md`; if this document and the plans disagree, the plans
win.

This is **not a marketing pitch**. The codebase is a 2010 Windows-targeted
~162K-LOC C++ codebase with a 2008-vintage C# editor suite and a 2024
GPLv3 bash installer bolted on. The "modernization" is bringing it to
the point where a contributor with current tools can build, test, and
extend it. Done responsibly, that work is months, not days.

## Phase B -- best-effort Linux server build

Plan: `plans/02-phase-b-linux-server.md`.

**Goal.** Get the C++ server compiling on Linux as far as practical in
one focused effort. Not "production-ready"; not "passes tests" --
compiles with the most common Windows-isms behind a thin shim layer.

**Approach.**

1. Inventory Windows-only API usage with `grep` against `server/src`
   (`_beginthreadex`, `WaitForSingleObject`, `CreateMutex`,
   `CreateEvent`, `CreateMailslot`, `WIN32`, `HWND`, `HANDLE`,
   `Sleep`, `_snprintf`, `stricmp`, `InterlockedIncrement`). Bucket
   counts into `server/compat/WIN32_INVENTORY.md`.
2. Write the cheap shims first (`server/compat/win32_shim.h`,
   `server/compat/threading_shim.h`) so the include graph compiles.
3. Stub the harder shims (mailslots) so the build doesn't stop at the
   include line; defer the real implementation.
4. CMake configure, then build, then iterate: pick the most common
   compile-error class, fix or shim, rebuild, repeat. Commit per fix
   wave with a running count.
5. Stop when (a) the server links, (b) error counts plateau over three
   consecutive waves, or (c) context budget runs out.

**Effort.** Weeks, not days. The 162K LOC of C++ has accreted Windows
assumptions over its full history. Even with disciplined shimming, a
single-developer build-clean push is multi-week. Per-invocation progress
should be coherent fix waves, not a "one-shot finish" attempt.

**Key risks.**

- Mailslot IPC is not a typedef shim -- it's an architecture decision.
  The original code uses Windows mailslots for inter-process server
  communication. POSIX equivalent is Unix domain sockets or POSIX
  message queues, and choosing wrong affects performance and
  resilience.
- Threading model: the codebase mixes `_beginthreadex` with manual
  CRT teardown. A naive `std::thread` shim will not preserve the
  exact lifetime guarantees on which Win32 code may depend.
- 32-bit assumptions: pointer arithmetic and integer truncation
  sprinkled through 2010 code. `-Wall -Wextra` will catch some;
  silent data loss is the dangerous case.
- OpenSSL 1.0 -> 3.x interactions land in Phase E but bleed back into
  Phase B compile errors.

**Deliverables.** `server/CMakeLists.txt`, `server/compat/win32_shim.h`,
`server/compat/threading_shim.h`, `server/compat/mailslot_shim.h`,
`server/compat/WIN32_INVENTORY.md`, `server/BUILD_ERRORS.md`, plus the
fix commits.

## Phase C -- Postgres migration

Plan: `plans/03-phase-c-postgres.md`.

**Goal.** `psql -f db/postgres/schema.sql` against an empty Postgres 16
creates all 71 tables cleanly. Begin the C++ call-site migration off
`libmysqlclient` and on to `libpqxx`; full migration is multi-week and
out of scope for any single invocation.

**Approach.**

1. Write `db/postgres/convert.sh` as a `sed`/`awk` pipeline against
   `db/mysql/net7.sql`. Rules in detail in the Phase C plan and in
   `06-database-schema.md` "Postgres migration notes".
2. Run conversion, output `db/postgres/schema.sql` and
   `db/postgres/seed.sql`.
3. Validate with `docker compose up postgres` + `psql -f schema.sql`.
4. Document residual manual fixes in `db/postgres/README.md`.
5. Survey C++ MySQL call sites: `grep -rn 'mysql_query\|MYSQL\*\|...'
   server/src` into `server/db/MYSQL_CALLSITES.md`.
6. Migrate one simple call site end-to-end as a pattern reference, in
   `server/db/MIGRATION_PATTERN.md`.

**Effort.** Schema conversion is days. Full call-site migration is
weeks (probably comparable to Phase B). One invocation lands the
schema plus the pattern; the rest is a checklist for future work.

**Key risks.**

- MySQL-specific datatypes (`tinyint(1)` semantics, unsigned int
  ranges) translate imperfectly. Where the original code relied on
  MySQL's silent truncation/zerofill, Postgres will reject the data.
- `binary(1)` and zerofill columns are MySQL-isms with no clean
  Postgres equivalent; require column-by-column choice.
- Identifier collisions on quoted-vs-unquoted column names if
  capitalisation matters (`StatName` vs `statname`). Postgres folds
  unquoted identifiers to lowercase; the schema is mixed-case.
- Net7Mysql in `login-server/` directly speaks MySQL via the C
  client lib; needs separate refactor (likely Phase C continuation,
  not first pass).

**Deliverables.** `db/postgres/convert.sh`, `db/postgres/schema.sql`,
`db/postgres/seed.sql`, `db/postgres/README.md`,
`server/db/MYSQL_CALLSITES.md`, `server/db/MIGRATION_PATTERN.md`.

## Phase D -- C# tools to .NET 10

Plan: `plans/04-phase-d-csharp-tools.md`.

**Goal.** Upgrade every C# project under `tools/` from
.NET Framework 2.0-4.0 (old-style csproj) to .NET 10 SDK-style targeting
`net10.0-windows` with `<UseWindowsForms>true</UseWindowsForms>`.
`dotnet build tools/Net7Tools.sln` succeeds end-to-end on any platform
with the .NET 10 SDK. Runtime stays Windows-only.

**Approach.**

Per project, the sequence is:

1. Convert `.csproj` to SDK-style.
2. Target `net10.0-windows`, enable WinForms.
3. Replace `MySql.Data` with `MySqlConnector` (active maintenance,
   MIT-licensed). Add `Npgsql` where Postgres awareness makes sense
   post-Phase-C.
4. Fix removed APIs: `BinaryFormatter`, `ConfigurationManager` (if
   used), legacy resource loading patterns.
5. Strip per-developer files (`*.suo`, `*.user`, `obj/`) -- already
   gitignored.
6. Regenerate `Net7Tools.sln`.
7. `dotnet build`; capture remaining failures in
   `tools/BUILD_STATUS.md`.

**Effort.** Days per editor for the mechanical conversion; weeks
total if API breakage is heavy. The `commontools` library is the
prerequisite and the biggest blocker -- everything else depends on it.

**Key risks.**

- WinForms designer compatibility: the `*.Designer.cs` files were
  generated by Visual Studio 2008; modern designers regenerate them
  slightly differently. Visual diffs are noisy.
- `app.config`-driven configuration: needs migration to
  `appsettings.json` plus `Microsoft.Extensions.Configuration`, or
  a `System.Configuration.ConfigurationManager` NuGet shim.
- W3D Parser: heavy P/Invoke risk per the Phase D plan; may stay on
  the legacy framework or get a separate native helper.
- Cross-platform build via Wine for editor runtime is a separate
  Phase I task -- not in scope here.

**Deliverables.** 17 SDK-style `.csproj` files, regenerated
`Net7Tools.sln`, `tools/BUILD_STATUS.md`, per-tool README updates,
`tools/README.md` with the runtime caveat.

## Phase E -- OpenSSL 1.0 -> 3.x

Plan: `plans/05-phase-e-openssl.md`.

**Goal.** The server builds against OpenSSL 3.x without deprecation
warnings on the migrated APIs.

**Approach.**

1. Inventory OpenSSL API usage into `server/crypto/OPENSSL_INVENTORY.md`
   (`EVP_*`, `RSA_*`, `BIO_*`, `SSL_*`, `ERR_*`, `HMAC_*`, `SHA1_*`,
   `MD5_*`, `DES_*`, `CIPHER_*`, `EC_*`, `DH_*`, `PEM_*`, `X509_*`).
2. Build the deprecation-call migration table in
   `server/crypto/MIGRATION_TABLE.md`: old -> 3.x EVP-flavoured
   replacement.
3. Leave Crypto++ alone (it's independent of OpenSSL versioning).
4. Compile with `-DOPENSSL_API_COMPAT=0x30000000L` to surface the
   deprecation set.
5. Migrate the lowest-hanging fruit (`MD5_*` -> `EVP_md5()`,
   `HMAC_*` -> `EVP_MAC_*`) as a worked example.

**Effort.** Days. Most of the work is mechanical: replace deprecated
low-level APIs with EVP equivalents. The hard part (algorithm choice,
constant-time guarantees) was already done by Net-7; we are not
re-deriving crypto.

**Key risks.**

- Westwood's original RSA implementation in `tools/udpdump/` uses
  Crypto++; should stay as-is. Don't conflate it with OpenSSL.
- The DES-based legacy login flow: if it relies on a deprecated cipher
  that's been removed from OpenSSL 3 default builds, may need
  `-DOPENSSL_NO_LEGACY=0` plus a runtime provider load. Document the
  decision in `99-decisions-log.md`.

**Deliverables.** `server/crypto/OPENSSL_INVENTORY.md`,
`server/crypto/MIGRATION_TABLE.md`, the example migration commits.

## Phase F -- warning cleanup

Plan: `plans/06-phase-f-warnings.md`.

**Goal.** Establish a `-Wall -Wextra -Wno-unused-parameter` baseline.
Capture the full warning log, group by category, fix the top three
categories with safe global transforms.

**Approach.**

1. Add the flags to `server/CMakeLists.txt`. Build.
2. Capture `server/WARNINGS_BASELINE.md` with histograms
   (`-Wsign-compare`, `-Wparentheses`, `-Wreorder`,
   `-Wuninitialized`, etc.).
3. Fix the top three categories. Document residual categories +
   counts in the same file.
4. Add a CI step (allowed to fail) that re-runs the build and
   stores the warning log as a CI artifact.

**Effort.** Days to weeks depending on baseline size. 2010-vintage
C++ tends to have thousands of warnings; fixing the top three
categories should noticeably move the needle without descending into
long-tail review.

**Key risks.**

- `-Wsign-compare` and `-Wnarrowing` interact with the same
  signed/unsigned hazards that Phase B hopefully surfaced. Some
  "fixes" change behaviour silently if the wrong cast is chosen.
- Warning-fix commits should be small and per-category for
  bisectability.

**Deliverables.** `server/WARNINGS_BASELINE.md`, CMake flag
additions, the fix commits, CI workflow step.

## Phase G -- tests

Plan: `plans/07-phase-g-tests.md`.

**Goal.** A scaffolded test harness plus a few smoke tests. Real test
growth is ongoing; this phase is the foundation.

**Approach.**

1. `tests/CMakeLists.txt` pulling GoogleTest via `FetchContent`.
2. `tests/smoke_test.cpp`: a trivial assertion that compiles and
   links against the project headers.
3. `tests/db_smoke_test.cpp`: connect to the compose Postgres test
   fixture, `SELECT 1`. Env-var-gated so offline builds don't break.
4. `tests/protocol/`: scaffold one packet parser test once the
   parser is identified (Phase H deepening).
5. `ctest --output-on-failure` in CI.
6. `tests/README.md` documenting what's tested, what's not, how to
   add a test.

**Effort.** Days for the scaffolding. Real test coverage is years of
ongoing work; this phase deliberately does not try to back-fill it.

**Key risks.**

- GoogleTest via FetchContent adds a build dep; alternative is
  system `libgtest-dev`. The plan picks FetchContent for
  reproducibility but it's a judgement call.
- A passing smoke test in CI is a credibility floor; passing tests
  that don't actually exercise anything are worse than no tests.
  Resist the temptation to inflate the count.

**Deliverables.** `tests/CMakeLists.txt`, two passing smoke tests,
CI integration, `tests/README.md`.

## Phase H -- deepen docs

Plan: `plans/08-phase-h-docs.md`.

**Goal.** Take the Phase A docs from "summary plus pointers" to "you can
actually trace a packet from client to server". Add protocol RE detail,
runtime sequence diagrams, and ability-system internals.

**Approach.**

1. Extract `archive/kyp-snapshot/capturedPackets/*.rar` with `unrar
   x`. Classify packet opcodes against the list embedded in
   `tools/udpdump/UdpDump.cpp`. Build a packet-type table in
   `docs/03-network-protocol.md`.
2. Add Mermaid sequence diagrams to `docs/04-server-modules.md` for
   login -> character select -> sector entry.
3. For each ability in `docs/05-abilities.md`, link to its `.cpp`,
   summarise effect, list cooldown/range/damage formula.
4. Write new docs:
   - `docs/12-content-pipeline.md`: editor -> DB -> server flow.
   - `docs/13-gameplay-loop.md`: combat, exploration, missions,
     trading, guilds at a high level.
   - `docs/14-extending.md`: how to add a new ability, mob type,
     sector.

**Effort.** Days to weeks. Open-ended; bounded by quality bar, not
items remaining.

**Key risks.**

- Packet RE is tedious and error-prone. Spot-check claims against
  captures before publishing them as facts.
- Ability formulas: easy to misread combat math from a code skim.
  Verify with a debugger or a focused test before documenting.

**Deliverables.** Updated `03-network-protocol.md`,
`04-server-modules.md`, `05-abilities.md`; new
`12-content-pipeline.md`, `13-gameplay-loop.md`, `14-extending.md`.

## Phase I -- dev env polish

Plan: `plans/09-phase-i-dev-env.md`.

**Goal.** Developer ergonomics. Polish `justfile`, hot-reload in
docker-compose, build a release pipeline, expand the CI matrix.

**Approach.**

1. Polish `justfile`: `dev`, `down`, `psql`, `logs server|login|db`,
   `shell server`.
2. `docker-compose.yml`: mount `server/src` and `server/build` for
   hot rebuild; add `pgadmin` profile; experiment with a
   `tools` profile running editors under Wine inside a container.
3. Multi-stage `server/Dockerfile` (build -> runtime), publish as
   `ghcr.io/.../enb-server:latest`. Same for `login-server`.
4. `.github/workflows/build.yml` matrix: ubuntu-24.04 +
   ubuntu-22.04; build server, apply schema, run tests.
5. `.github/workflows/release.yml`: on tag, build and push images.
6. Optional `pre-commit`: clang-format, dotnet format, shellcheck.

**Effort.** Days. Hot-reload inside a compose container is the one
section that can rabbit-hole if you take it too far (full
incremental-build daemon vs. just bind-mounting source).

**Key risks.**

- Running C# WinForms editors under Wine inside a container is
  cute but may not be worth the complexity vs. "just use a
  Windows VM". Time-box it.
- GHCR publishing requires a GitHub PAT or `GITHUB_TOKEN` with
  package-write scope; not all forks have it. Make the release
  workflow gracefully degrade.

**Deliverables.** Polished `justfile`, multi-stage Dockerfiles,
GHCR-pushing release workflow, CI build matrix.

## What we deliberately skipped

Things we are **not** doing. Each has a reason; in some cases the
reason is "out of scope" rather than "bad idea".

- **No Avalonia / Uno / web-UI port of the C# editors.** WinForms is
  janky in 2026, but it works on Windows and under Wine, and the
  editors are not the bottleneck for the project. Rewriting them in
  Avalonia would be 6-12 months of UI work that displaces actual
  gameplay/server work. If someone shows up to do it, we will not
  stop them; we will not initiate it.

- **No full clean-room protocol reverse engineering.** The existing
  Net-7 RE work (architecture document, packet captures, UdpDump,
  the in-tree protocol code) is preserved and documented. Going
  beyond that requires sustained packet-capture work that is bigger
  than the rest of this roadmap combined. Phase H deepens what we
  have; it does not start over.

- **No new gameplay content.** Adding new sectors / missions /
  mobs is something the editors enable; the preservation project
  itself is content-neutral. Custom content lives in forks.

- **No engine rewrite.** The C++ codebase is what it is. Rewriting
  in Rust or modern C++23 would be more fun than maintaining what
  exists, but the value of *this* project is "the old thing still
  works", not "a new thing exists".

- **No DRM-free client distribution.** The Earth & Beyond client is
  the original Westwood/EA binary. We document how to install it;
  we do not redistribute it. (`enb-linux-installer` downloads it
  from public mirrors at runtime.)

- **No commercial use.** Forced by the CC BY-NC-SA 3.0 license,
  and reinforced by policy. We will not consider PRs that move
  toward "paid server" / "premium tier" / "marketplace" features.
  See `LICENSES/README.md`.

- **No mobile, no console, no VR.** These would all be major ports
  built on top of the working server; not the preservation
  project's job.

- **No "modernise to async/await/coroutines".** Tempting in a few
  hot-paths but invasive. The threading model works. Leave it.

- **No replacing Crypto++ with OpenSSL.** Phase E touches OpenSSL,
  not Crypto++. Crypto++ is used for the client-protocol RSA/RC4
  in `tools/udpdump/` and is a stable, narrow surface; leaving it
  alone is the right call.

- **No `boost::asio` migration.** The original code rolls its own
  sockets via `winsock2` + `pthread`/Win32 threading. Moving to
  `asio` is appealing for code quality but is a large enough
  refactor to qualify as an engine rewrite. Out of scope.

- **No automatic content migration tool from existing live shards.**
  If someone is running a Net-7 server today, they have data we do
  not. Migrating their data into this repo's Postgres schema is a
  one-off operation; we will not build a generic tool for it.

## Total effort estimate

Loose, opinionated, expect 2x:

- Phase B: 2-4 weeks of focused work to "compiles and links".
  Probably 2-3 more months to "actually runs without crashing".
- Phase C: 1 week for schema + first call site. 2-3 months for full
  call-site migration.
- Phase D: 1-2 weeks for the C# upgrades.
- Phase E: 1 week for inventory + example migration. 2-4 more weeks
  for full migration.
- Phase F: 1-2 weeks for baseline + top categories. Long-tail is
  ongoing.
- Phase G: 1 week for scaffolding. Real test growth is years.
- Phase H: 2-4 weeks for the documented scope. Open-ended.
- Phase I: 1 week for polish.

Total to "buildable, runnable, testable Linux server with Postgres":
roughly 4-6 months of one full-time engineer, or 12-18 months of
weekend hacking. Higher if you discover that mailslot IPC needs a
full architectural rethink.
