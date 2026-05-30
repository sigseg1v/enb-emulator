# tests/server/

GoogleTest harness for the Net-7 C++ server. Sits alongside `tests/integration/`
(the C# xUnit suite that drives `CliClient.Core` against the live docker-compose
stack -- see `docs/16-integration-tests.md`). Server unit/smoke tests live here;
end-to-end opcode round-trips live in the integration suite.

## Status

Scaffolding plus first real tests.

| Binary | What it checks |
|---|---|
| `smoke_test` | 1+1==2. Proves GTest/CTest plumbing works. |
| `header_layout_test` | Wire-layout invariants for `EnbTcpHeader`/`EnbUdpHeader` (size, field offsets, little-endian byte order). Catches accidental reorders/repacks. |
| `postgres_smoke_test` | `SELECT 1` against a live Postgres. Skipped unless `NET7_TEST_DB_DSN` is set. Not built if libpq is missing. |
| `handshake_test` | Offline tests for the Westwood RSA+RC4 client: capture parser, RSA modulus matches captured ACK1, RSA round-trip, RC4 self-inverse, RC4 matches RFC 6229 vector, SYN2 decrypt recovers the annotated session key (reversed layout per `proxy/Connection.cpp:230-268`). Requires OpenSSL. |
| `handshake_live_test` | Two cases: (1) loopback — spawns a thread playing the server side on an ephemeral port and verifies the client-chosen RC4 key matches what the server decoded; (2) live — runs the 4-step handshake against `$NET7_TEST_PROXY_HOST:$NET7_TEST_PROXY_PORT` (default 3801) and asserts ACK2 returns a non-zero CORD port. Live case is skipped unless `NET7_TEST_PROXY_HOST` is set. |
| `replay_test` | Two offline cases (port-filter and post-handshake opcode pinning) plus a live env-gated replay that walks the captured post-handshake packets through the same RC4 stream the handshake produced. Live case skipped unless `NET7_TEST_PROXY_HOST` is set. |

## Test client architecture (`tests/server/client/`)

The handshake / replay binaries above are built from a small standalone C++ client that knows nothing about `Net7.h`. Three layers:

1. **Crypto + parsing** — `westwood/westwood_rsa.{h,cpp}`, `westwood/westwood_rc4.{h,cpp}`, `capture_parser.{h,cpp}`. Reimplements the 512-bit `e=35` Westwood RSA (using OpenSSL 3 BIGNUM) and the swap-byte RC4 with the same semantics as `server/src/WestwoodRSA.cpp` / `WestwoodRC4.cpp`. The capture parser reads the canonical `capturedPackets/*.txt` format and caps each packet's byte stream at the declared length so annotation hex never leaks into the wire bytes.
2. **Transport + handshake driver** — `tcp_client.{h,cpp}`, `handshake_driver.{h,cpp}`. POSIX blocking TCP with `SO_RCVTIMEO` and a `select`-gated non-blocking connect. `RunClientHandshake()` does the full 4-step SYN1 / ACK1 / SYN2 / ACK2 dance, including the **reversed key layout** (the RC4 session key is written to plaintext[63..56], not plaintext[0..7] — see `proxy/Connection.cpp:230-268 DoClientKeyExchange` for the server-side reversal).
3. **Replay** — `replay.{h,cpp}`. Walks a filtered packet list, encrypts client→server packets with the established RC4 stream, reads the expected number of bytes for server→client packets, and (optionally) checks the response opcode against the recorded value. Full byte equality is not asserted because server state diverges run-to-run.

Live tests are env-gated. To exercise them against a running stack:

```sh
just run-stack-bg
NET7_TEST_PROXY_HOST=127.0.0.1 NET7_TEST_PROXY_PORT=3801 \
    ctest --test-dir build/tests --output-on-failure -R 'HandshakeDriver|Replay'
```

Or use `just integration-test`, which brings up the stack, waits for `tcp/3801`, builds, runs the gated tests, then tears down.

Honest status note: as of Phase J, the live handshake test will fail against the proxy container because the Linux `Connection` body is still a stub that accepts and closes. The offline tests, the loopback handshake test, and the replay-opcode-pinning test all pass in CI. The live tests are wired and will pass automatically once the proxy's RC4 session is implemented on Linux.

## Build & run

```sh
cmake -S tests/server -B build/tests -G Ninja
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

- `tests/server/abilities/`  -- per-ability unit tests against the abilities
  engine, isolated from the network and DB layers. **Blocker:** the
  abilities engine doesn't isolate cleanly from `ServerManager` /
  `Player`. Phase H is documenting the boundaries; once that's done,
  picking one ability to isolate is the first cut.
- `tests/server/protocol/`   -- golden-file round-trip tests for the packet
  encoders/decoders (uses the captures in
  `archive/kyp-snapshot/capturedPackets/`). Today only the wire-layout
  pins live here.
- `tests/server/db/`         -- schema + DAO tests against a per-test database
  (the Postgres smoke is the first member; real DAO tests need the
  `mysqlplus.cpp` -> libpqxx rewrite to finish).
- `tests/integration/` is the C# xUnit suite (already live, separate scope)
  that drives `CliClient.Core` against the docker-compose stack -- see
  `docs/16-integration-tests.md`.

## How to add a test

1. Put the `.cpp` under the right subdirectory:
   `tests/server/<area>/<thing>_test.cpp`.
2. Add an `add_executable` + `gtest_discover_tests` entry in
   `tests/server/CMakeLists.txt`.
3. Link against `GTest::gtest_main`.
4. If the test needs an external service (DB, network), gate it on an
   env var and `GTEST_SKIP()` when absent so offline builds stay green.
5. Add a row to the table above.

## Why GoogleTest

Path of least resistance for C++17 on Linux + Windows, and the Net-7
server has no existing test framework to defer to. Pulled in via
`FetchContent` so contributors don't need to install gtest separately.
