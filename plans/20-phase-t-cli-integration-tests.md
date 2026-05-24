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

- [x] Item 1 — Project scaffold + csproj + slnx wiring + ServerFixture skeleton
      Status: done
      Touches: tests/integration/CliClient.IntegrationTests/{CliClient.IntegrationTests.csproj, Directory.Build.props, RepoRoot.cs, ServerFixture.cs, ServerCollection.cs, ClientFixture.cs, Smoke/HarnessSmokeTest.cs, README.md}
      Notes:
        ▸ Project lives at tests/integration/CliClient.IntegrationTests/.
           Linux-first Directory.Build.props mirrors the cli-client one
           (EnableWindowsTargeting=false, nullable, TreatWarningsAsErrors).
           xunit 2.9.2 + xunit.runner.visualstudio 2.8.2 + Microsoft.NET.Test.Sdk
           17.11.1 (same versions as CliClient.UnitTests for consistency).
           ProjectReference up three levels into tools/cli-client/src/CliClient.Core.
        ▸ No slnx — neither cli-client nor the test project uses one today; each
           csproj builds standalone via `dotnet build <csproj>`. Net7Tools.slnx
           stays the WinForms-era solution. Adding a Cli.slnx wrapper is
           optional follow-up work, not blocking.
        ▸ ServerFixture is the docker-compose lifecycle owner: implements
           IAsyncLifetime, runs `docker compose up -d --wait` from the repo
           root on InitializeAsync, TCP-probes 4443/3805/3801/3500 with a
           120s deadline + 1s interval, runs `docker compose down -v` on
           DisposeAsync. Exposes LoginHost/LoginPort/GlobalHost/GlobalPort/
           MasterHost/MasterPort/SectorHost/SectorPort/MysqlPort as the
           single source of truth for endpoints. CI escape hatch:
           CLI_INTEGRATION_SKIP_COMPOSE=1 skips up/down (TCP probe still
           runs) so a sibling job can stand the stack up out-of-band.
        ▸ ServerCollection ([CollectionDefinition("ServerCollection")])
           bound to ServerFixture. Tests that need the live stack declare
           [Collection(ServerCollection.Name)] + ctor param.
        ▸ ClientFixture is the per-test seam: constructs a fresh
           OpcodeRegistry (with full named-opaque coverage + the typed
           ServerRedirectCodec), an AuthLoginClient pointed at the
           ServerFixture's TLS endpoint with acceptUntrustedCertificates:
           true (docker-compose ships a self-signed dev cert), and a
           ConnectGlobalAsync(user, pass) convenience that does login →
           new CliSession → ConnectGlobalAsync and returns the connected
           session. Caller owns disposal.
        ▸ RepoRoot.Path walks up from AppContext.BaseDirectory looking for
           docker-compose.yml so tests don't hard-code the repo location.
           Throws cleanly if the marker is missing.
        ▸ HarnessSmokeTest has 2 [Fact]s that pass without docker:
           RepoRoot_Resolves_ToDirectoryContainingDockerCompose +
           CliClientCore_IsReferenced_OpcodeRegistryConstructs (asserts
           OpcodeNames.Count == 207). Build green, 2/2 passing locally.
        ▸ Caught one compile error first try: ClientFixture referenced a
           non-existent AuthLoginResponse.RejectionReason property; fixed
           to dump RawBody.TrimEnd() into the error message instead
           (which is also more diagnostic — you see exactly what the
           server returned).

- [x] Item 2 — Fixture player account seeding
      Status: done
      Touches: tests/integration/CliClient.IntegrationTests/{Fixtures/seed.sql (new), TestAccounts.cs (new), ServerFixture.cs (SeedFixtureAccountsAsync), CliClient.IntegrationTests.csproj (CopyToOutputDirectory for Fixtures/), Smoke/HarnessSmokeTest.cs (seed-shape smoke)}
      Notes:
        ▸ Seed lives at Fixtures/seed.sql, copied to bin/ via csproj
           <None Include="Fixtures/**/*" CopyToOutputDirectory>. 5
           deterministic accounts (cli_test01..cli_test05) at IDs
           9_000_001..9_000_005 — well clear of the dump's
           AUTO_INCREMENT=15965 so seed and real data never collide.
        ▸ Password hash is UPPER(MD5('testpw')) per
           login-server/Net7SSL/LinuxAuth.cpp:227 (Linux auth path).
           Same plaintext "testpw" for every account so tests don't
           track per-account secrets.
        ▸ Plan said "postgres DB after compose up" — actually the
           default compose stack is MySQL (postgres is behind the
           --profile postgres flag). Pivoted to MySQL accordingly:
           seed runs via `docker compose exec -T mysql mysql -unet7
           -pnet7 net7_user` with seed.sql piped over stdin. The
           --profile postgres path can pick this up later when N+
           moves the auth layer to libpqxx.
        ▸ ServerFixture.SeedFixtureAccountsAsync runs after the TCP
           probes succeed (i.e. after up --wait + ports are listening
           = mysql is genuinely up). 60s timeout on the exec itself.
        ▸ Seed is idempotent inside a compose lifetime (DELETE WHERE
           id BETWEEN ... + INSERT) so a re-run in the same
           containers reseeds cleanly. Cross-run isolation comes from
           `down -v` in DisposeAsync.
        ▸ Skipped: a lease/pool with claim/release semantics — xUnit
           collection-fixture tests serialise by default, so the 5
           accounts can be referenced directly by index from
           TestAccounts.Pool. Add a lease if a parallel scenario ever
           emerges.
        ▸ New smoke test SeedSql_IsCopiedToOutput_AndMentionsEvery-
           PooledAccount runs without docker — asserts the file
           shipped to bin/ and every TestAccounts.Pool entry +
           the UPPER(MD5(...)) hash form appear in it. Catches
           mismatches between TestAccounts.cs and seed.sql at unit-
           test time, not integration-test time. 3/3 smoke tests
           passing.

- [x] Item 3 — Handshake tests (RSA + TLS login)
      Status: done
      Touches: tests/integration/CliClient.IntegrationTests/Handshake/{TlsLoginTests.cs, RsaHandshakeTests.cs}
      Notes:
        ▸ TlsLoginTests has 3 [Fact]s under [Collection(ServerCollection.Name)]:
           ValidAccount_ReturnsValidTicket (Pool[0]/testpw → Valid=true,
           non-empty ticket, length >= 20 — LinuxAuth.cpp issues 40-char
           hex so we assert "at least 20 hex chars" not exactly 40 to
           avoid false positives if ticket size changes),
           WrongPassword_ReturnsInvalid (Pool[0] + bogus password →
           Valid=false, empty ticket — failure includes RawBody for
           diagnosis), NonexistentAccount_ReturnsInvalid (user that's
           not in seed → Valid=false).
        ▸ RsaHandshakeTests has 3 [Fact]s — one per encrypted endpoint:
           GlobalServer_AcceptsClientKeyExchange (3805),
           MasterServer_AcceptsClientKeyExchange (3801),
           SectorServer_AcceptsClientKeyExchange (3500). Each does
           EncryptedTcpConnection.ConnectAsync — the connect itself
           drives the full RSA pubkey exchange + RC4 client-key reply;
           a hang or throw means the proxy rejected our key, hung up
           early, or sent garbage. 15s timeout per test.
        ▸ 6/6 passing locally in 171ms (post-build, against the live
           stack with CLI_INTEGRATION_SKIP_COMPOSE=1 since the user's
           docker compose was already up). Smoke tests (3) still pass
           too. Total: 9/9 tests green.
        ▸ Tests deliberately don't assert on key material or ticket
           contents beyond shape — that's Phase S's job (UnitTests
           cover the RSA/RC4 codecs in isolation). Phase T's job is
           "the live wire round-trips" so we assert at the protocol
           boundary, not at the byte level.
        ▸ Verified seed.sql is reapplied cleanly via the docker compose
           exec path even after mysql has been up 21h (DELETE BETWEEN +
           INSERT, no UNIQUE collisions). seed.sql comment was
           previously misleading (claimed wrong hash digest); corrected
           to "MySQL evaluates UPPER(MD5(...)) server-side, don't
           trust this comment over the function call".

- [x] Item 4 — Opcode round-trip tests for everything Phase J/K has wired today
      Status: done (Sector LOGIN deferred — see notes)
      Touches:
        tools/cli-client/src/CliClient.Core/Opcodes/Outbound/VersionRequestCodec.cs (new),
        tools/cli-client/src/CliClient.Core/Opcodes/Inbound/VersionResponseCodec.cs (new),
        tools/cli-client/tests/CliClient.UnitTests/Opcodes/{VersionRequestCodecTests.cs, VersionResponseCodecTests.cs} (new),
        tests/integration/CliClient.IntegrationTests/ClientFixture.cs (register new codecs),
        tests/integration/CliClient.IntegrationTests/Opcodes/{VersionRequestTests.cs, MasterJoinTests.cs} (new)
      Notes:
        ▸ VersionRequestTests covers all three branches of the Linux
           proxy's status logic in ClientToServer_linux_stubs.cpp:50-53:
           CurrentVersion (42,0)→0, OlderClient (41,0)→1, NewerClient
           (43,0)→2. Each test connects fresh to global (3805), sends
           the typed VersionRequest, drains until VersionResponse
           arrives, asserts status. ~10ms per test against the live
           stack.
        ▸ Wire-format gotcha worth knowing about: VersionRequest is
           big-endian on the wire (server ntohl's the two int32s),
           VersionResponse is little-endian (Linux stub ships the
           int32 status as raw host bytes with no htonl). Codec
           handles the asymmetry; documented in the VersionResponse-
           Codec doc comment.
        ▸ MasterJoinTests covers the 0x0035→0x0036 round-trip end-
           to-end. Logs in as Pool[0] for a real ticket, connects to
           master (3801), sends MasterJoin with ToSectorID=1, drains
           for ServerRedirect. ~5s per run because in this test env
           the proxy's HandleMasterJoin falls through the UDP
           SendMasterLogin timeout path (no MVAS responder on UDP
           3808) and lands in the hardcoded ServerRedirect fallback
           at PROXY_LOCAL_TCP_PORT (3500). When Phase K completes
           the UDP plane + adds a test MVAS responder this drops to
           sub-second.
        ▸ Ticket-field width mismatch documented in MasterJoinTests:
           /AuthLogin returns a 40-char ASCII hex ticket but the
           MasterJoin packet's ticket field is 20 bytes. The Linux
           HandleMasterJoin doesn't validate the ticket bytes today
           (only avatar_id_lsb + ToSectorID matter), so the test
           passes a truncated ASCII slice as placeholder. Comment in
           the test flags that Phase K's UDP-plane completion will
           require shipping the hex *decoded* to 20 binary bytes,
           not truncated as ASCII. Caught the limitation on first
           run when AsciiTicket threw ASCIIEncoding.GetBytes
           overflow on the 40-char input.
        ▸ Sector LOGIN/0x0002 *deliberately deferred*: the Linux
           stub at ClientToServer_linux_stubs.cpp:87-99 activates
           the proxy↔server connection state and intentionally
           does NOT send any reply (matches Win32 behaviour at
           ClientToSectorServer.cpp:22-31 — LOGIN is fire-and-
           forget at the TCP level, the next visible packet is
           whatever the UDP plane pushes once the connection is
           live). A round-trip test for LOGIN-only would be
           tautological ("we sent, server didn't crash"). It
           comes back in scope when Phase K wires the
           post-LOGIN UDP push so we have something to receive.
        ▸ New typed codecs registered in ClientFixture: Version-
           RequestCodec, VersionResponseCodec, MasterJoinCodec (the
           latter was already-typed-in-Core but only ServerRedirect
           was registered in the fixture's per-test registry).
        ▸ 11 new unit tests (5 VersionRequest, 6 VersionResponse
           inc. one [Theory] with 3 cases). UnitTests now 186/186
           green (was 175 pre-Item-4). Integration suite now 13/13
           green (3 smoke + 3 TLS + 3 RSA + 3 VersionRequest + 1
           MasterJoin) in ~6s wall-clock against the live stack.
        ▸ Each typed codec mirrors the C++ wire struct field-for-
           field with a doc comment citing
           common/include/net7/PacketStructures.h line numbers and
           the proxy handler that produces/consumes it — preserva-
           tion-grade source pointers so any future divergence is
           traceable.

- [~] Item 5 — Workflow tests (connect-and-login done; enumerate-* deferred behind Phase K)
      Status: partially done — ConnectAndLoginTests landed; the
        enumerate-sectors / enumerate-missions / enumerate-items
        workflows blocked on Phase K wiring the listing opcodes
        on the server side.
      Touches: tests/integration/CliClient.IntegrationTests/Workflows/ConnectAndLoginTests.cs
      Notes:
        ▸ ConnectAndLoginTests has 2 [Fact]s:
           ValidAccount_LandsInGlobalStage_NoHealthTrip — runs the
           full ConnectAndLogin workflow against Pool[0]/testpw with
           a 2s IdleDuration, asserts LoginValid=true, Stage==Global,
           guard not tripped, Aborted==null. Drain count asserted
           >=0 not ==0 so Phase K's future "server says hello on
           connect" doesn't false-break it.
           WrongPassword_AbortsWithLoginRejection — runs the same
           workflow with a bogus password, asserts LoginValid=false,
           Stage==Disconnected, Aborted contains "Valid=False"
           (i.e. the workflow correctly surfaces login rejection
           without tripping the health guard or attempting the
           global connect).
        ▸ Both pass against the live stack: happy path ~2s (mostly
           the IdleDuration), wrong-password path ~140ms (login
           rejected fast). Drives ConnectAndLogin + HealthGuard
           together end-to-end, which is the most realistic
           Phase-S-as-a-library smoke test we can write today.
        ▸ EnumerateSectorsTests / EnumerateMissionsTests /
           EnumerateItemsTests *deferred*: the underlying workflows
           don't exist yet in CliClient.Core (only ConnectAndLogin
           and SendChat are shipped today). Building them requires
           (a) wiring typed codecs for the listing opcode trios
           server-side (Phase K work; currently Linux dispatch
           only has 0x0000 VersionRequest + 0x0002 Sector LOGIN),
           and (b) building a workflow that paginates through the
           listing and writes a golden dump. Both halves come back
           in scope once Phase K wires e.g. HandleGlobalConnect on
           Linux so we have the GlobalAvatarList round-trip
           (0x006D → 0x0070) to drive an enumerate-avatars
           workflow as the first real listing test.

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
