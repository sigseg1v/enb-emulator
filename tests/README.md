# tests/

GoogleTest harness for the Net-7 server.

## Status

Scaffolding only. Phase B writes the wiring; Phase G writes the actual
tests.

The only test today is `smoke_test.cpp`, which checks that 1+1==2 — its
purpose is to prove that the CMake / FetchContent / GoogleTest / CTest
plumbing works on a fresh machine, not to test anything about the
server.

## Build & run

```sh
cmake -S tests -B build/tests -G Ninja
cmake --build build/tests -j"$(nproc)"
ctest --test-dir build/tests --output-on-failure
```

## What Phase G will add

- `tests/abilities/`  — per-ability unit tests against the abilities
  engine, isolated from the network and DB layers.
- `tests/protocol/`   — golden-file round-trip tests for the packet
  encoders/decoders (uses the captures in `archive/kyp-snapshot/capturedPackets/`).
- `tests/db/`         — Postgres schema + DAO tests against a
  per-test-database (`pg_tmp`-style or testcontainers).
- `tests/integration/` — small end-to-end flow tests: client login ->
  character select -> sector handoff. Likely needs a fake client.

## Why GoogleTest

It's the path of least resistance for C++17 on Linux + Windows, and
the Net-7 server has no existing test framework to defer to.
