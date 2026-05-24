# Phase T — CLI-client-driven integration test suite (xUnit / .NET 10)

## Goal

Build a **real integration test suite** in C# / xUnit that uses `CliClient.Core` (Phase S) to drive a running server stack and assert opcode-by-opcode correctness. Replaces shell-based smoke tests + log scraping with proper test reporter output.

Every commit runs this suite; opcode regressions break the build.

## Depends on

- **Phase S complete** (or at least: `CliClient.Core` library is usable — `GlobalConnection`, `MasterConnection`, `SectorConnection`, `LoginConnection` instantiable + awaitable in-process; `OpcodeRegistry` covers the opcodes the test wants to assert on).
- Phase J/K opcodes wired on the server side (the test asserts what Phase K ships).

## Same hard rules as Phase S

The integration test suite is the most aggressive consumer of the CLI client, so the Phase S rules apply doubly:

1. **NEVER modify the server to make tests pass.** If a test fails because the server behaves unlike the real client, fix the test (or fix the server *to match the real client*, never to match the test).
2. **Always respect the server.** Tests that detect server distress (disconnects, error opcodes, timeouts) fail loudly — they don't retry.
3. May request broader-than-real-client data, but only when the server serves it unmodified.
4. Real client wins on protocol-shape disagreements.

## What this phase delivers

A new test project at `tests/integration/CliClient.IntegrationTests/`:

```
tests/integration/CliClient.IntegrationTests/
├── CliClient.IntegrationTests.csproj   net10.0, xunit, references tools/cli-client/src/CliClient.Core
├── ServerFixture.cs                    xUnit IAsyncLifetime — docker compose up (postgres + login + proxy + server); waits for healthy; tears down
├── ClientFixture.cs                    spins a CliClient.Core instance, logs in with a known fixture account, hands the client to tests
├── Handshake/
│   ├── RsaHandshakeTests.cs            asserts proxy hands back a sane RC4 key
│   └── TlsLoginTests.cs                asserts login-server issues a valid ticket
├── Opcodes/
│   ├── MasterJoinTests.cs              0x0035 → ServerRedirect: asserts server reply matches captured Win32-client byte trace
│   ├── VersionRequestTests.cs          0x0000 → VersionResponse
│   ├── SectorLoginTests.cs             sector LOGIN/0x0002 state machine
│   └── (one file per opcode as Phase K wires them — long-tail, but every wire goes here)
├── Workflows/
│   ├── ConnectAndLoginTests.cs         end-to-end: login → idle 5s → clean disconnect; asserts packet log contains the expected handshake sequence
│   ├── EnumerateSectorsTests.cs        runs the enumerate-sectors workflow against a fixture DB; asserts dump matches a golden JSON
│   ├── EnumerateMissionsTests.cs       same shape for missions
│   └── EnumerateItemsTests.cs          same shape for items
├── Verification/
│   ├── CaptureReplayTests.cs           replays archive/kyp-snapshot/capturedPackets/ frames, asserts server response matches the captured real-server response
│   └── PacketLogShapeTests.cs          asserts the NDJSON schema is stable (consumer compatibility)
├── Robustness/
│   ├── DisconnectMidHandshakeTests.cs  HealthGuard trips cleanly
│   ├── MalformedReplyTests.cs          server sends garbage → client aborts, doesn't retry-storm
│   └── RateLimitRespectTests.cs        client backs off when server rate-limits
└── README.md
```

## CI integration

```yaml
# .github/workflows/build.yml (addition)
- name: Integration tests
  run: |
    docker compose -f docker-compose.yml up -d --wait
    dotnet test tests/integration/CliClient.IntegrationTests/ --logger "trx;LogFileName=cli-integration.trx"
    docker compose -f docker-compose.yml down -v
  timeout-minutes: 15
```

xUnit `[Collection("server")]` ensures the docker stack stands up exactly once per CI run (not per test class), with `ServerFixture` as the collection's `ICollectionFixture<T>`. Per-test isolation is provided by per-test `CliClient.Core` instances (one TCP connection per test) — the server itself is shared but each test gets its own client + its own player account from a pool.

## Why xUnit specifically

- Already the .NET-standard test framework in 2026; no reason to reinvent.
- `IAsyncLifetime` and `ICollectionFixture<T>` model the docker-compose stack lifecycle exactly right.
- `Assert.Equal(expected, actual, byteComparer)` on `Span<byte>` payloads gives readable failures with hex dumps.
- TRX output integrates with GitHub Actions test summaries — failed opcodes get linked directly in the PR view.

## Items

- [ ] Item 1 — Project scaffold + csproj + slnx wiring + ServerFixture skeleton
      Status: not started
      Touches: tests/integration/CliClient.IntegrationTests/CliClient.IntegrationTests.csproj, ServerFixture.cs, ClientFixture.cs, tools/Net7Tools.slnx (or a new test slnx)
      Notes: xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, ProjectReference to CliClient.Core. ServerFixture does `docker compose up -d --wait`, polls health endpoints (or TCP-probes 3500/443/3809), exposes connection info, tears down with `docker compose down -v` on dispose.

- [ ] Item 2 — Fixture player account seeding
      Status: not started
      Touches: tests/integration/CliClient.IntegrationTests/Fixtures/seed.sql, ServerFixture.cs (seed step)
      Notes: a deterministic set of test accounts (e.g. `test01`/`test02` with known characters in known sectors) inserted into the postgres DB after `compose up`. Each test claims an account from the pool to avoid cross-test interference.

- [ ] Item 3 — Handshake tests (RSA + TLS login)
      Status: not started
      Touches: tests/integration/CliClient.IntegrationTests/Handshake/*.cs
      Notes: first real assertions — proxy hands back a key; login server hands back a ticket; basic sanity.

- [ ] Item 4 — Opcode round-trip tests for everything Phase J/K has wired today
      Status: not started
      Touches: tests/integration/CliClient.IntegrationTests/Opcodes/*.cs
      Notes: MasterJoin/0x0035, VersionRequest/0x0000, sector LOGIN/0x0002 to start. One test class per opcode; one [Fact] per scenario (happy path + edge cases). Captured Win32 bytes from archive/kyp-snapshot/capturedPackets/ become the golden references.

- [ ] Item 5 — Workflow tests (connect-and-login, enumerate sectors/missions/items)
      Status: not started
      Touches: tests/integration/CliClient.IntegrationTests/Workflows/*.cs
      Notes: each workflow runs end-to-end and asserts the dump file matches a golden JSON (with a controlled set of fields — timestamps, UUIDs are normalised before compare).

- [ ] Item 6 — Capture replay tests
      Status: not started
      Touches: tests/integration/CliClient.IntegrationTests/Verification/CaptureReplayTests.cs
      Notes: load a capture from archive/kyp-snapshot/capturedPackets/, replay the client side against our server, compare server's reply byte-for-byte (or field-for-field) to the captured server reply. This is the single most valuable test in the suite — it gates server correctness against a real-world ground truth.

- [ ] Item 7 — Robustness tests (HealthGuard, malformed replies, rate-limit respect)
      Status: not started
      Touches: tests/integration/CliClient.IntegrationTests/Robustness/*.cs
      Notes: verify the CLI client behaves under server distress per Phase S rule 2. Uses a mocked / scripted server-side responder for the bad-server scenarios so we don't have to break the real server to test the response.

- [ ] Item 8 — CI integration
      Status: not started
      Touches: .github/workflows/build.yml, justfile (add `just integration` target)
      Notes: integration suite runs after the unit-test job; failure breaks the build; TRX uploaded as artifact.

- [ ] Item 9 — Documentation: docs/13-integration-tests.md
      Status: not started
      Touches: docs/13-integration-tests.md, CLAUDE.md (repo-map row)
      Notes: how to add a new opcode test, how golden files are produced/refreshed, how to debug a failing test (capture logs in CI artifacts, replay locally via `just integration`).

- [ ] Item 10 — Opcode coverage push: ratchet a "tested opcodes" metric in CI
      Status: not started
      Touches: tests/integration/CliClient.IntegrationTests/CoverageRatchet.cs, .github/workflows/build.yml
      Notes: a meta-test that counts how many opcodes from `common/include/net7/Opcodes.h` have at least one round-trip test; a CI ratchet enforces "never goes down". Phase T starts at single-digit coverage and ramps with Phase K.

## Verification

Phase T is done when:

- `dotnet test tests/integration/CliClient.IntegrationTests/` passes locally against `docker compose up`
- CI runs the integration suite on every PR and gates merges on it
- At least the opcodes wired in Phase K have round-trip tests + at least one capture-replay test exists
- Robustness tests catch a deliberately broken server (kill `proxy` mid-test → client aborts cleanly, test fails with a recognisable error, not a hang)
- The coverage ratchet is wired and shows a non-trivial number
- `docs/13-integration-tests.md` is written

## Out of scope (don't do these in Phase T)

- Performance / load testing (separate phase if/when needed)
- Fuzzing the server (separate effort; the CLI client could feed a fuzzer but T's job is correctness, not robustness of the server)
- Visual regression / 3D-scene asserts (the CLI client doesn't render)
- Multi-client orchestration (one client per test for Phase T; multi-client is a follow-up)
