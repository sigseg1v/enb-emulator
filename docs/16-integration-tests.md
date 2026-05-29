# Integration tests (`tests/integration/CliClient.IntegrationTests/`)

The integration suite is the **opcode-correctness gate** for the
project. xUnit 2.9 / .NET 10, references the `CliClient.Core` library
from Phase S, drives the live docker-compose stack
(mysql + login-server + proxy + server), and asserts protocol fidelity
opcode-by-opcode.

If a server change silently breaks the wire format, this suite catches
it. If a `CliClient.Core` codec drifts, this suite catches it. If
either side starts retry-storming under distress, this suite catches
it.

Phase plan: `plans/20-phase-t-cli-integration-tests.md`. Phase S
(the CLI client itself) is the dependency: `docs/15-cli-client.md`.

## Hard rules (verbatim from the plan)

These mirror the Phase S rules and apply doubly — the integration
suite is the most aggressive consumer of the CLI client, and the
CLI client is the most aggressive consumer of the server:

1. **NEVER modify the server to make a test pass.** If a test fails
   because the server behaves unlike the real client, fix the test —
   or fix the server *to match the real client*, never to match the
   test. The escape hatch (the only one) is a primary-source
   citation in the commit message per the "Server integrity rules"
   block in `CLAUDE.md`.
2. **Always respect the server.** Tests that detect server distress
   (disconnects, error opcodes, timeouts) fail loudly. They do not
   retry, they do not silently re-arm, they do not back off and try
   again. The HealthGuard kill-switch (`HealthGuard.cs`) drives this.
3. The CLI MAY request broader data than a real Win32 client would
   (e.g. enumerate-all-X), but only when the server is willing to
   serve it unmodified.
4. **Real client wins on protocol-shape disagreements** — the same
   way `docs/15-cli-client.md` describes it.

## Layout

```
tests/integration/CliClient.IntegrationTests/
├── CliClient.IntegrationTests.csproj   net10.0, xunit, ref's CliClient.Core
├── Directory.Build.props               nullable + TreatWarningsAsErrors
├── ServerFixture.cs                    IAsyncLifetime — docker compose up + TCP probes
├── ServerCollection.cs                 [CollectionDefinition("ServerCollection")]
├── ClientFixture.cs                    per-test seam: builds OpcodeRegistry + AuthLoginClient
├── RepoRoot.cs                         walks up from bin/ to find docker-compose.yml
├── TestAccounts.cs                     on-demand Npgsql INSERT per test
├── Fixtures/
│   └── Captures/                       hex-with-comments extracts from retail captures
│       ├── README.md
│       ├── masterjoin_packet220.hex
│       └── serverredirect_packet222.hex
├── Smoke/                              no-docker harness self-tests
├── Handshake/                          TLS auth + RSA+RC4 round-trips
├── Opcodes/                            per-opcode round-trip (one file per opcode)
├── Workflows/                          end-to-end client behaviours
├── Verification/                       capture-replay against retail bytes
└── Robustness/                         bad-server scenarios (uses ScriptedServer)
```

The folder names match xUnit's discovery — `dotnet test --filter
"FullyQualifiedName~Robustness"` runs only the Robustness folder.

## What the categories assert

| Category | What it asserts | Needs docker? |
|---|---|---|
| Smoke | Project loads, RepoRoot resolves, OpcodeRegistry constructs | No |
| Handshake | `/AuthLogin` returns valid+invalid tickets correctly; RSA+RC4 handshake completes on global/master/sector | Yes |
| Opcodes | Single-opcode round-trip: send typed packet, drain for the typed reply, assert decoded fields | Yes |
| Workflows | Composite operations like ConnectAndLogin — HealthGuard not tripped, stage transitions correct, abort surfaces login rejection | Yes |
| Verification | Capture-replay: load real retail bytes, decode via our codecs, re-encode, assert bytes-equal-original (codec round-trip identity) | No |
| Robustness | Client behaviour under server distress: mid-handshake disconnect, malformed reply, rate-limit. Uses ScriptedServer (in-process bad-server) | No |

## Fixture accounts

Tests provision their own accounts on demand. `TestAccounts.New(_server)`
opens an Npgsql connection to net7_user (host port 5434) and does an
INSERT against the accounts table, returning a `TestAccount` record
with the freshly-allocated id, username, and password.

IDs start at 9_000_001 and increment per call (atomic counter), well
clear of the dump's `AUTO_INCREMENT=15965` so per-test accounts never
collide with real data. Usernames have the shape
`t_<8-hex-process-id>_<6-hex-counter>` (17 chars, comfortably under
the accounts.username varchar(40) limit). The 8-hex process-id prefix
is drawn once per test run, so two concurrent test runs against the
same database do not collide.

Password is the literal string "testpw" for every account; the value
stored in `accounts.password_phc` is a precomputed Argon2id PHC string
(libsodium's INTERACTIVE profile -- m=64MiB, t=2, p=1), held as a
constant in TestAccounts. The login server verifies via
`crypto_pwhash_str_verify` (LinuxAuth.cpp) which accepts any conforming
Argon2id PHC regardless of the implementation that produced it. If
`SharedPassword` changes, the constant PHC must be regenerated.

For STRESS_TEST_CLOSED accounts (rejected at the global UDP plane
with G_ERROR 12, per server/src/UDP_Global.cpp:ProcessTicketInfo),
pass `status: 0`. Default is status=100 (ACTIVE/admin) which matches
what real accounts use.

Cross-run isolation comes from `down -v` in DisposeAsync.

## ScriptedServer (Robustness)

`Robustness/ScriptedServer.cs` is a single-shot `TcpListener` on
`127.0.0.1` bound to an OS-assigned port. It runs a caller-supplied
`Func<NetworkStream, CancellationToken, Task>` on the accepted
connection, then closes. This is the standard escape hatch for
"the test needs the server to do something bad" — instead of breaking
the real proxy (forbidden — see Hard Rules #1), we ship a fake
responder we can program.

`ScriptedServer.HandshakeAsServerAsync(stream, ct)` drives the
server side of the RSA + RC4 handshake (ships a 74-byte dummy
pubkey, reads the 68-byte client key packet, RSA-decrypts the
64-byte block, extracts the 8-byte session key, returns paired
RC4 contexts). After it returns, the script can send well-formed
encrypted frames via `ScriptedServer.EncryptFrame(outboundRc4,
opcode, payload)` or deliberately corrupt ones.

ScriptedServer **must not** be used for "test the real server's
behaviour" — that's the live-stack categories' job. It is exclusively
for asserting the *client's* response to bad server behaviour.

## Capture-replay (Verification)

`Verification/CaptureReplayTests.cs` loads hex fixtures from
`Fixtures/Captures/`, decodes them via the typed codecs in
`CliClient.Core`, asserts every transcribed field, then re-encodes
and asserts the bytes equal the original. This **round-trip
identity** is the strongest fidelity check we have for any codec
that touches the wire — if it fails, the codec has silently drifted
from the real retail format and any "fix" must come with primary-
source proof (per `CLAUDE.md` server-integrity rules).

Fixtures are textual hex with `#` line comments, parsed by
`Verification/HexFixture.cs`. The format was chosen so reviewers
can eyeball bytes against `common/include/net7/PacketStructures.h`
in PR diffs. Source files (capture name + frame number) are cited
in the fixture comment header — that's the primary-source
chain-of-custody required for the bytes to count as preservation
reference material.

### How to add a capture-replay fixture

1. Find the bytes. The canonical reference set is the RARs under
   `archive/kyp-snapshot/capturedPackets/`. Each RAR is a 54MB
   textual hex-dump of a real 2006-era session against the live
   retail server.
2. Extract just the payload bytes for one opcode frame (NOT the
   4-byte EnB header — that's `DecodeInbound`'s job to consume; the
   fixture is just what the opcode codec sees). Note the frame
   number and any sibling-frame context that helps interpret the
   bytes.
3. Write `Fixtures/Captures/<opcode>_packetNNN.hex`:
   ```
   # Source: archive/kyp-snapshot/capturedPackets/capture_1.rar (frame 220).
   # <one paragraph explaining what this is — header bytes, what
   #  fields the payload decodes to, any divergence findings to
   #  flag for future investigation>
   00 00 00 02
   00 00 00 02
   ...
   ```
4. Add a `[Fact]` to `CaptureReplayTests.cs` asserting field-by-
   field decode + (where the codec is bidirectional) round-trip
   identity.
5. If you discover a divergence between the captured bytes and
   what our codec produces today, **DO NOT** "fix" the codec
   without primary-source corroboration. Flag it in the fixture
   comment, the codec doc comment, and the test comment, and
   assert the current codec behaviour so a regression in the
   codec breaks the build either way. See the sector_id BE-vs-LE
   discussion in `Fixtures/Captures/serverredirect_packet222.hex`
   for the canonical example.

## How to add a new opcode test

1. Make sure the typed codec exists in `CliClient.Core` (under
   `Opcodes/Inbound/` or `Opcodes/Outbound/`). If it doesn't,
   add it first, register it in `ClientFixture.cs`'s registry,
   and add unit tests for it under
   `tools/cli-client/tests/CliClient.UnitTests/Opcodes/`.
2. Add `Opcodes/<OpcodeName>Tests.cs` with `[Collection(ServerCollection.Name)]`
   on the class and a `ServerFixture`/`ClientFixture` ctor.
3. Each `[Fact]` should: connect fresh (via the fixture
   convenience), send the typed packet via the codec, drain
   inbound packets in a loop until the expected opcode arrives,
   decode via the typed codec, assert the fields.
4. Drain on *opcode*, not on ordering. Phase K may add an
   unsolicited hello-packet on connect; tests that assert "the
   first packet is X" will false-break. Loop until you see X.
5. Each test gets its own `EncryptedTcpConnection` so the per-
   test cancellation tokens stay independent. Don't share
   sessions across tests.

## How to debug a failing test

### Local

```
just cli-integration-fast     # no docker; runs Robustness + Verification + Smoke
just dev                      # bring up the live stack
just cli-integration          # full xUnit suite; reuses the live stack
```

When a test fails, the assertion message includes the decoded
fields and (for round-trip tests) the byte-level diff. For
mid-flight diagnostics, drop a `Console.WriteLine` — xUnit
captures stdout per-test and surfaces it in the failure report.

### CI

The `cli-integration-test` GitHub Actions job uploads two
artifacts on every run:

- `cli-integration-trx` — TRX file with per-test results,
  consumed by the GitHub Actions "Test summary" tab. Click a
  failed test for the assertion message + stdout capture.
- On failure, the job also dumps `docker compose logs` for
  mysql, login, proxy, and server into the job log.

If a flake repros locally, the most useful first step is
`docker compose logs server | tail -200` while the failing test
is running.

## `cli-integration` justfile recipes

| Recipe | What it runs | Wall-clock |
|---|---|---|
| `just cli-integration` | Full suite. Probes live ports — reuses an existing `just run-stack-bg` if found, else hands lifecycle to `ServerFixture`. | ~10s + compose-boot if cold |
| `just cli-integration-fast` | No-docker subset (Smoke + Verification + Robustness). | ~540ms |

The fast variant is the inner-loop default for changes that don't
touch the live-stack codepaths (codec edits, HealthGuard logic,
ScriptedServer scenarios).

## Why xUnit specifically

- Standard .NET test framework in 2026; no reason to reinvent.
- `IAsyncLifetime` and `ICollectionFixture<T>` model the docker-
  compose stack lifecycle exactly right.
- `Assert.Equal(expected, actual)` on `byte[]` gives readable
  failures with hex dumps.
- TRX integrates with GitHub Actions test summaries — failed
  opcodes get linked directly in the PR view.

## What this suite is NOT

- **Not a fuzzer.** The job is correctness against retail bytes,
  not robustness of the server under arbitrary input. A fuzzer
  could share the CliClient.Core surface but lives elsewhere.
- **Not a load test.** Single-client, sequential. Multi-client
  orchestration is a follow-up.
- **Not a UI test.** The CLI client doesn't render; there are no
  Avalonia tests in here.
- **Not the only test harness in the repo.** The C++ gtest
  binaries (`tests/`) cover the server-internal paths the CLI
  client never reaches. See `docs/08-build.md` § "Running tests".
