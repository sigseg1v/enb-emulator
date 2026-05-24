# CliClient integration tests (`tests/integration/CliClient.IntegrationTests/`)

xUnit / .NET 10 test project that drives the Phase S CLI client
(`tools/cli-client/src/CliClient.Core`) against a real docker-compose
server stack (mysql + login + proxy + server). Replaces shell-based
smoke tests with TRX-output, debuggable, in-process assertions.

Phase plan: `plans/20-phase-t-cli-integration-tests.md`.

## Hard rules

Same as Phase S:

1. **Never modify the server to make a test pass.** If a test asserts X
   and the server does Y, the test is right only when the *real* EnB
   client+server did X. Otherwise the test is wrong.
2. **Always respect the server.** No retry storms, no parallel auth
   floods. A test that detects server distress fails loudly.
3. The CLI client (and therefore the test) may request broader data
   than a real Win32 client when the server willingly serves it.
4. Real client wins on protocol disputes. Fix the codec, not the server.

See `CLAUDE.md` "Server integrity rules" for the authoritative text.

## Project layout

```
tests/integration/CliClient.IntegrationTests/
├── CliClient.IntegrationTests.csproj   net10.0, xunit, ProjectReference to CliClient.Core
├── Directory.Build.props               Linux-first, warnings-as-errors
├── RepoRoot.cs                         walks up to find docker-compose.yml
├── ServerFixture.cs                    xUnit IAsyncLifetime — `docker compose up -d` + TCP-probe; `down -v` on dispose
├── ServerCollection.cs                 [CollectionDefinition] so the stack stands up once per run
├── ClientFixture.cs                    builds per-test OpcodeRegistry + AuthLoginClient + ConnectGlobalAsync helper
├── Smoke/
│   └── HarnessSmokeTest.cs             tests for the test infra itself (no docker required)
└── Handshake/, Opcodes/, Workflows/, Verification/, Robustness/ (added as items 3-7 land)
```

## Running

Local, with docker compose available:

```sh
dotnet test tests/integration/CliClient.IntegrationTests/
```

ServerFixture runs `docker compose up -d` from the repo root, waits up
to 120s for ports `4443` (login TLS), `3805` (global), `3801` (master),
`3500` (sector) to accept, then hands the fixture to the tests. On
dispose it runs `docker compose down -v` (the `-v` wipes the named
volumes, so each test-run starts from a clean DB).

Skip the up/down half (point tests at an externally-managed stack —
e.g. a CI job that already has it running):

```sh
CLI_INTEGRATION_SKIP_COMPOSE=1 dotnet test tests/integration/CliClient.IntegrationTests/
```

The TCP probe still runs in skip mode; only the start/stop is bypassed.

## Status

Item 1 (scaffold) shipped: csproj + ServerFixture + ServerCollection +
ClientFixture + RepoRoot + harness smoke tests. The project builds and
its no-docker smoke tests pass. Items 2-10 (fixture accounts, handshake
tests, opcode round-trips, workflow tests, capture replay, robustness,
CI, docs, coverage ratchet) ship as Phase T progresses.

See `plans/20-phase-t-cli-integration-tests.md` for the per-item
checklist.
