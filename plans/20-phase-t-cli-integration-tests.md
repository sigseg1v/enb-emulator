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

- [x] Item 6 — Capture replay tests
      Status: done (first pass — codec-fidelity tests against extracted
        retail-server bytes; live server-reply replay deferred to a
        follow-up tracked in Notes)
      Touches:
        tests/integration/CliClient.IntegrationTests/Fixtures/Captures/{README.md, masterjoin_packet220.hex, serverredirect_packet222.hex} (new),
        tests/integration/CliClient.IntegrationTests/Verification/{HexFixture.cs, CaptureReplayTests.cs} (new)
      Notes:
        ▸ Source data: archive/kyp-snapshot/capturedPackets/capture_1.rar
           is a 54MB textual hex-dump of a real 2006-era EnB session
           against the live retail server (159.153.232.146). We extract
           individual opcode payloads as hex-with-comments fixtures
           rather than committing the RAR — fixture files stay KB-scale,
           CI doesn't need unrar, and reviewers can eyeball the bytes
           against common/include/net7/PacketStructures.h in PR diffs.
           Per the server-integrity rules, each fixture cites its
           primary source (capture file + frame number) in a comment
           header — that's the chain-of-custody proof required for the
           bytes to count as preservation reference material.
        ▸ HexFixture.cs is the loader: Load(relative) reads from
           AppContext.BaseDirectory/Fixtures/Captures/ (csproj
           <None Include="Fixtures/**/*"> ships the files into bin/);
           Parse(text) strips '#'-to-EOL comments and whitespace,
           accumulates hex digits, throws FormatException on odd nibble
           count or any non-hex non-whitespace non-comment character so
           silently-wrong fixtures fail loudly instead of silently
           decoding to garbage bytes.
        ▸ CaptureReplayTests.cs has 3 [Fact]s, no docker dependency
           (they operate on cached bytes — they live in the integration
           project because the fixtures are integration-suite artifacts,
           not because they need the stack):
           MasterJoin_RealCaptureBytes_RoundTripIdentity — loads the
           64-byte frame-220 payload, asserts payload length matches
           MasterJoinCodec.WireSize, decodes via the typed codec,
           asserts every transcribed field (Unknown1=2, Unknown3=
           0x40E60235 session token, AvatarIdMsb=0x3E221201, ToSector-
           Id=0x0000B05F=45151, Unknown10=0x7FFFFFFF=INT32_MAX, ticket
           length=20), then RE-ENCODES and asserts the bytes equal
           the original byte-for-byte. This round-trip-identity check
           is the strongest fidelity guarantee we have for the codec:
           if it ever fails, our wire format has silently drifted from
           the real retail format and any "fix" must come with primary-
           source proof per the server-integrity rules.
           ServerRedirect_RealCaptureBytes_DecodesAllFields — loads the
           10-byte frame-222 payload, decodes, asserts SectorId=
           0x5FB00000 (BE, see divergence note below), IP=46.232.153.159,
           port=3500.
           HexFixture_RejectsMalformedInput — sanity-checks the loader
           rejects bad characters and odd nibble counts but accepts
           comments + whitespace.
        ▸ **Preservation finding** (sector_id byte-order divergence):
           the captured ServerRedirect's sector_id bytes are
           `5F B0 00 00`. Read big-endian (what our codec does, matching
           our proxy's ntohl-then-dump path) this is 0x5FB00000 — a
           gibberish-large 1.6-billion sector ID. Read little-endian it
           is 0xB05F=45151, which exactly matches the ToSectorID the
           client just sent in MasterJoin. The plausible interpretation
           is that the retail server flipped byte order between the
           ToSectorID it received (BE) and the sector_id it echoed back
           (LE). Our proxy doesn't replicate this asymmetry. Per the
           server-integrity rules in CLAUDE.md, this is a **finding to
           investigate, NOT a license to "fix" the codec** — a single
           capture isn't sufficient primary-source proof to declare
           which byte order is canonical, and we need either (a) a
           second capture confirming the same behaviour or (b) a
           decompilation of the retail ServerRedirect emission path
           before changing anything. The fixture comment, codec doc
           comment, and test comment all flag this; the test asserts
           what our codec produces today (BE) so a regression in the
           codec breaks the build either way.
        ▸ **Live server-reply replay deferred**: the original Item 6
           description called for replaying the client side against our
           live server and comparing the server's reply to the captured
           server reply byte-for-byte. That comparison is currently
           unimplementable because (a) our proxy's ServerRedirect
           response in the test environment is the hardcoded UDP-
           timeout fallback (PROXY_LOCAL_TCP_PORT=3500), not a real
           response sourced from a backend MVAS, so a byte-for-byte
           comparison to retail would be testing the fallback, not the
           real codepath; and (b) for the MasterJoin specifically, the
           captured request's avatar_id_lsb identifies a player that
           does not exist in our seed DB, so the server would either
           reject or fall through to defaults — neither of which
           matches the retail capture. The honest first-pass deliver-
           able is the codec-fidelity tests above (which are the
           preservation-grade comparison we can actually defend right
           now); the live-replay comparison comes back into scope when
           Phase K wires the real UDP plane and the harness can seed a
           player matching the captured avatar_id. Item 6 stays [x]
           because what is shipped is what is grounded; the follow-up
           is tracked here rather than blocking the phase.
        ▸ Test counts: integration suite 16/16 green (3 smoke + 3 TLS +
           3 RSA + 3 VersionRequest + 1 MasterJoin + 1 ConnectAndLogin
           happy + 1 ConnectAndLogin wrong-pw + 3 CaptureReplay incl.
           HexFixture self-test). The 3 capture-replay tests run in
           ~10ms total with no docker dependency, so they ratchet codec
           fidelity even when the stack is down.

- [x] Item 7 — Robustness tests (HealthGuard, malformed replies, rate-limit respect)
      Status: done
      Touches: tests/integration/CliClient.IntegrationTests/Robustness/{ScriptedServer.cs, DisconnectMidHandshakeTests.cs, MalformedReplyTests.cs, RateLimitRespectTests.cs}
      Notes:
        ▸ ScriptedServer is the bad-server harness — a single-shot
           TcpListener on 127.0.0.1:0 that accepts one connection and
           runs a caller-supplied `Func<NetworkStream, CT, Task>` on
           the stream, then disposes. Required by Phase T's "never
           break the real server to test client robustness" guard-rail:
           a test that needed the real proxy to send garbage would
           force us to violate server-integrity rule #1. ScriptedServer
           is the standard escape hatch. Also ships
           HandshakeAsServerAsync (drives the server side of the
           RSA+RC4 handshake: ships a 74-byte dummy pubkey, reads the
           68-byte client key packet, RSA-decrypts the 64-byte block,
           extracts the 8-byte session key, returns paired RC4
           contexts) and EncryptFrame (builds + encrypts an opcode
           frame using a supplied outbound RC4 context).
        ▸ DisconnectMidHandshakeTests (4 tests): server closes before
           sending pubkey → ConnectAsync throws EndOfStreamException
           (no exception swallowing); server sends 10 of 74 pubkey
           bytes then closes → same outcome, message includes
           "closed"; full handshake then server closes → ReceiveAsync
           returns null cleanly (clean EOF), workflow notifies
           HealthGuard via OnDisconnect, guard trips with reason
           "disconnected", Token cancels; no-retry contract verified
           by counting server-side accepts (must be exactly 1 for one
           ConnectAsync call — proves no internal reconnect loop).
        ▸ MalformedReplyTests (3 tests): hand-built encrypted frame
           with header.Size=2 (below the 4-byte header) → Receive-
           Async throws InvalidDataException with "desynced RC4" in
           the message (the canonical signal that something went
           wrong, not silent corruption); unexpected opcode arriving
           while a BeginExpectResponse(opcode=0x0036, timeout=300ms)
           is in flight → expectation times out, HealthGuard trips
           with "response timeout" + the workflow label, the workflow
           never retries the request; counter-test ships a perfectly
           valid 64-byte payload with opcode 0x1234 (unregistered)
           and asserts ReceiveAsync returns the full payload —
           catches a regression where rejection becomes "any unknown
           opcode" instead of "malformed frame".
        ▸ RateLimitRespectTests (2 tests, the integration slice on
           top of CliClient.UnitTests/HealthGuardTests which already
           covers the pure logic): ServerFloodsClient runs a script
           that ships frames as fast as the socket allows for ~250ms;
           the client loop drains via real ReceiveAsync, feeds the
           guard (budget=50/s), trips inbound rate threshold,
           cancellation token fires, loop exits cleanly. Asserts
           seen >= budget so we know the trip happened at the
           configured threshold not earlier. ClientSelfFlood is the
           symmetric scenario — a buggy workflow firing too many
           SendAsync calls; the guard catches outbound flood and
           trips before the hardCap=4×budget escape. Sanity-bounded
           against "tripped too early" too.
        ▸ Build clean; 9/9 Robustness tests pass in 533ms (no docker
           dependency for any of them — ScriptedServer binds 127.0.0.1
           on an OS-assigned port, doesn't collide with the real
           proxy on 3500). Full integration suite now 27/27 green
           (was 16/16 pre-Item-7) in ~7s total wall-clock.

- [x] Item 8 — CI integration
      Status: done
      Touches: .github/workflows/build.yml (new `cli-integration-test` job), justfile (new `cli-integration` + `cli-integration-fast` recipes)
      Notes:
        ▸ New CI job `cli-integration-test` on ubuntu-24.04, 20-min
           timeout, NO `needs:` chain — runs in parallel with cmake-
           build/ctest/integration-test so a Phase T regression
           surfaces at the same wall-clock time as a server-side one.
           Steps: checkout → install .NET 10 SDK → generate self-
           signed TLS cert (same openssl invocation as `just gen-
           certs`) → `docker compose up -d --wait mysql login proxy
           server` → `dotnet build` the integration project → run
           with CLI_INTEGRATION_SKIP_COMPOSE=1 so the test fixture
           doesn't try to own the lifecycle of the compose we
           already brought up (keeps logs collectable through the
           final teardown step) → TRX upload as artifact → on
           failure dump all four service logs → unconditional
           `docker compose down -v` at the end.
        ▸ `just cli-integration` is the laptop equivalent: probes
           the live ports (4443/3801/3805/3500) and either reuses an
           existing `just run-stack-bg` stack (sets SKIP_COMPOSE=1)
           or hands the docker lifecycle to ServerFixture. `just
           cli-integration-fast` filters to the no-docker subset
           (Robustness/Verification/Smoke) for inner-loop work —
           15/15 pass in 538ms with no containers running. Verified
           both recipes locally; full suite passes 27/27 against a
           live stack.
        ▸ Workflow YAML validated via `python3 -c "import yaml;
           yaml.safe_load(...)"`. TRX results land under
           `test-results/` and get uploaded via actions/upload-
           artifact@v4 — failed runs surface in the PR's "Test
           summary" tab with per-test links.
        ▸ Conscious choice NOT to gate the cmake-build/ctest jobs
           on `cli-integration-test`: the .NET integration job has
           a much faster failure surface (no C++ compile needed,
           no toolchain matrix). Letting it fail-fast independently
           of the C++ pipeline gives quicker signal on server-
           protocol regressions during a PR's lifecycle.

- [x] Item 9 — Documentation: docs/16-integration-tests.md (renumbered — 13 was taken by 13-gameplay-loop.md)
      Status: done
      Touches: docs/16-integration-tests.md (new), docs/README.md (index row), CLAUDE.md (repo-map sub-line + Pointers section)
      Notes:
        ▸ Plan-file path was `docs/13-integration-tests.md` but slot
           13 is already `13-gameplay-loop.md`. Bumped to slot 16
           (next free after 15-cli-client.md) — same convention
           Phase S used.
        ▸ Doc covers: hard rules (verbatim from plan), folder
           layout, per-category coverage table (Smoke /
           Handshake / Opcodes / Workflows / Verification /
           Robustness — which need docker, which don't), fixture
           accounts mechanics (TestAccounts.Pool, seed.sql,
           idempotent seeding), ScriptedServer architecture +
           when it must NOT be used, capture-replay format with a
           5-step "how to add a fixture" walkthrough (and the
           "do NOT 'fix' a divergence without primary-source proof"
           rule restated), how to add a new opcode test (drain on
           opcode-not-ordering, own EncryptedTcpConnection per
           test, register codec in ClientFixture.cs), how to
           debug locally + in CI (TRX upload + compose-logs
           dumps), justfile recipe table (`cli-integration` vs
           `cli-integration-fast`), and an explicit "what this
           suite is NOT" section (not a fuzzer, not a load test,
           not the only test harness).
        ▸ CLAUDE.md repo-map row updated with a sub-line under
           tests/ pointing at the new doc; Pointers section gets
           two new rows (CLI client + Integration tests) since
           Phase S/T are now the most-asked-about additions.
        ▸ docs/README.md index gets the row 16 entry.

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
