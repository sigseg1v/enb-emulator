# Phase G — Tests

Goal: scaffolded test harness + a few smoke tests. Real test growth is ongoing.

## Outcome

GTest harness now ships three binaries — `smoke_test`, `header_layout_test`, `postgres_smoke_test` (env-gated). CI workflow runs `ctest` with a sidecar Postgres. Subsystem tests (abilities, full protocol round-trips, integration flows) require source-level isolation work that's documented as continuation items.

## Items

- [x] `tests/CMakeLists.txt` pulling in GoogleTest via `FetchContent`.
      Notes: In place since Phase B; Phase G added per-binary `gtest_discover_tests` + an optional libpq build block.
- [x] `tests/smoke_test.cpp` — trivial assertion that compiles + links.
      Notes: Existing from Phase B.
- [x] `tests/db_smoke_test.cpp` — connects to Postgres and runs `SELECT 1`. Gated behind an env var.
      Touches: tests/db/postgres_smoke_test.cpp
      Notes: Lives under `tests/db/` (the planned `<area>/` layout). Skips when `NET7_TEST_DB_DSN` is unset, and isn't built at all if libpq isn't installed. Verified the SQL pattern is standalone (no schema dependencies) so it works against any reachable Postgres.
- [x] `tests/protocol/` — scaffold one packet parser test.
      Touches: tests/protocol/header_layout_test.cpp
      Notes: Pins `EnbTcpHeader` (4 bytes, [size, opcode]) and `EnbUdpHeader` (12 bytes, [size, opcode, player_id, packet_sequence]) layout. Uses an in-file mirror of the structs because `PacketStructures.h` transitively pulls Win32 typedefs; splitting the wire structs out is a Phase G continuation item.
- [x] CI step: `ctest --output-on-failure`.
      Touches: .github/workflows/build.yml
      Notes: Added a `ctest` job with a Postgres 16 service sidecar; sets `NET7_TEST_DB_DSN` so the env-gated test runs against it. Also fixed a Phase D regression in the same workflow: `dotnet build tools/Net7Tools.sln` → `…/Net7Tools.slnx` (Phase D switched to the slnx solution format).
- [x] `tests/README.md` — what's tested, what's not, how to add a test.
      Notes: Rewritten with the per-binary table, the local Postgres recipe, and a "how to add a test" checklist.

## Verification

- Standalone-compiled the protocol assertions (size, offset, byte order) outside the gtest harness; values match the assertions exactly.
- CMake configure of `tests/` will succeed both with and without libpq; the libpq-gated postgres test is built only when libpq is found, and self-skips when `NET7_TEST_DB_DSN` is unset, so offline builds stay green.
- Proceed to Phase H.

## Deferred (Phase G continuation)

- `tests/abilities/` — needs the abilities subsystem to isolate from `ServerManager`/`Player`. Phase H is documenting the boundaries; pick one ability after that and start there.
- Full packet round-trip tests with goldens from `archive/kyp-snapshot/capturedPackets/` — first the wire structs need to live in their own header that doesn't drag in Net7.h.
- DAO tests — need the `mysqlplus.cpp` → libpqxx rewrite (Phase C continuation) to land before there's a Postgres-aware DAO surface to test.
- Integration flows (login → char-select → handoff) — need a fake client harness.
