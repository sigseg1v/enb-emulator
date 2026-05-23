# tests/

GoogleTest harness for the Net-7 server.

## Status

Scaffolding plus first real tests.

| Binary | What it checks |
|---|---|
| `smoke_test` | 1+1==2. Proves GTest/CTest plumbing works. |
| `header_layout_test` | Wire-layout invariants for `EnbTcpHeader`/`EnbUdpHeader` (size, field offsets, little-endian byte order). Catches accidental reorders/repacks. |
| `postgres_smoke_test` | `SELECT 1` against a live Postgres. Skipped unless `NET7_TEST_DB_DSN` is set. Not built if libpq is missing. |

## Build & run

```sh
cmake -S tests -B build/tests -G Ninja
cmake --build build/tests -j"$(nproc)"
ctest --test-dir build/tests --output-on-failure
```

To exercise the Postgres smoke locally:

```sh
docker compose up -d postgres   # uses docker-compose.yml
NET7_TEST_DB_DSN='host=127.0.0.1 user=net7 password=net7 dbname=net7' \
    ctest --test-dir build/tests --output-on-failure
```

CI runs the harness with a sidecar Postgres service (see `.github/workflows/build.yml`).

## What Phase G continuation will add

- `tests/abilities/`  — per-ability unit tests against the abilities
  engine, isolated from the network and DB layers. **Blocker:** the
  abilities engine doesn't isolate cleanly from `ServerManager` /
  `Player`. Phase H is documenting the boundaries; once that's done,
  picking one ability to isolate is the first cut.
- `tests/protocol/`   — golden-file round-trip tests for the packet
  encoders/decoders (uses the captures in
  `archive/kyp-snapshot/capturedPackets/`). Today only the wire-layout
  pins live here.
- `tests/db/`         — schema + DAO tests against a per-test database
  (the Postgres smoke is the first member; real DAO tests need the
  `mysqlplus.cpp` → libpqxx rewrite to finish).
- `tests/integration/` — small end-to-end flows (login →
  character-select → sector handoff). Needs a fake client harness;
  significant work.

## How to add a test

1. Put the `.cpp` under the right subdirectory:
   `tests/<area>/<thing>_test.cpp`.
2. Add an `add_executable` + `gtest_discover_tests` entry in
   `tests/CMakeLists.txt`.
3. Link against `GTest::gtest_main`.
4. If the test needs an external service (DB, network), gate it on an
   env var and `GTEST_SKIP()` when absent so offline builds stay green.
5. Add a row to the table above.

## Why GoogleTest

Path of least resistance for C++17 on Linux + Windows, and the Net-7
server has no existing test framework to defer to. Pulled in via
`FetchContent` so contributors don't need to install gtest separately.
