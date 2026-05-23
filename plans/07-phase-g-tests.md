# Phase G — Tests

Goal: scaffolded test harness + a few smoke tests. Real test growth is ongoing.

## Items

- [ ] `tests/CMakeLists.txt` pulling in GoogleTest via `FetchContent`.
- [ ] `tests/smoke_test.cpp` — trivial assertion that compiles + links.
- [ ] `tests/db_smoke_test.cpp` — connects to a docker-compose Postgres test fixture and runs `SELECT 1`. Gated behind an env var so it doesn't break offline builds.
- [ ] `tests/protocol/` — scaffold one packet parser test once the parser is identified.
- [ ] CI step: `ctest --output-on-failure`.
- [ ] `tests/README.md` — what's tested, what's not, how to add a test.

## Verification

- `cd build/tests && ctest` runs at least one passing test.
- Proceed to Phase H.
