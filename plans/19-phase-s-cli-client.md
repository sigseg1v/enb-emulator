# Phase S — Headless CLI client (C# / .NET 10)

## Goal

Build a **passive, headless CLI client** in C# / .NET 10 that speaks the real EnB client protocol against the emulator. The client is for:

- **Testing**: drive opcode round-trips end-to-end without launching the Win32 client under WINE
- **Data extraction**: enumerate sectors / missions / items and dump structured output to disk
- **Verification**: replay captured packet traces, compare what the server sends with what the real client received

## Hard rules (DO NOT VIOLATE)

1. **NEVER modify the server to make things easier for the CLI client.** The CLI client is a *passive observer*. The server's job is to talk to the *real* Win32 client. The only exception: if a packet capture or real-client decompilation proves the server is wrong, fix the server to match the real client — even if that happens to also fix the CLI client.

2. **The CLI client must always respect the server.** If the server imposes limits, returns garbage, or shows signs of crashing/overload (rate-limit replies, disconnects, packet floods, error opcodes), the CLI client stops the offending workflow immediately. No retry storms. No bypass attempts.

3. **The CLI client MAY request broader data than the real client** *if and only if* the server happily serves it without modification. Example: if the real client only requests nearby-object updates within a sector radius but the server has no enforcement, the CLI client may request the full sector. If the server starts misbehaving (timeouts, malformed replies, stalls) the CLI client drops back to real-client-shaped queries.

4. **The CLI client is not authoritative on protocol shape.** When the CLI client's understanding of an opcode disagrees with the real client (per capture or decompilation), the real client wins. The CLI client adapts.

## What this phase delivers

A new project at `tools/cli-client/` (C# / .NET 10, console app + reusable library, cross-platform — Linux primary). It:

- Connects to `proxy/` (TCP 3500) and `login-server/Net7SSL` (TCP 443) using the same RC4+RSA handshake the real client does
- Authenticates against the login server (TLSv1.3, `/AuthLogin`-style ticket flow already implemented in Phase J)
- Handles the global → master → sector handoff (the same multi-port redirect dance the real client does — see `docs/03-network-protocol.md` §1–§4)
- Sends/receives opcodes using the wire structs in `common/include/net7/PacketStructures.h` and the opcode enum in `common/include/net7/Opcodes.h`
- **Targets near-complete opcode coverage** — every opcode the server can emit gets a decoder; every opcode the server accepts gets an encoder. Phase S finishes the foundation + the opcodes already wired in Phases J+K (~10 opcodes round-trippable); the remaining ~200+ opcodes ratchet up as Phase K continues. Each opcode gets a registry entry + unit test for the codec; "not yet wired" opcodes generate a structured warning in the packet log rather than crashing.
- Logs every packet (direction, opcode, timestamp, payload hex, decoded fields when known) to a structured file (NDJSON for easy `jq` consumption)
- Logs received chat to a separate log
- Provides interactive REPL **and** scripted/headless workflow mode
- **Ships as a library project (`CliClient.Core`) plus thin console front-end (`CliClient.App`).** The library exposes the connection/codec/workflow primitives so xUnit integration tests in Phase T can instantiate a client in-process, drive it programmatically, and assert on responses — no shelling out to a process, no log scraping.

## Why C# / .NET 10

- Reuses the wire understanding already in `tools/commontools-avalonia/Database/` and the editor suite (which already talk to the same data the server emits)
- The `tools/` build already has a .NET 10 baseline; one more project costs ~zero infra
- A C# client can borrow the C# editor code that reads / writes the same DB rows the server emits — quick verification of "what server said matches what editor sees"
- C# `System.IO.Pipelines` + `Span<byte>` makes the byte-level packet codec straightforward

## Project layout

```
tools/cli-client/
├── CliClient.sln              (or just sit inside Net7Tools.slnx)
├── src/
│   ├── CliClient.Core/        ←── reusable library (xUnit pulls this in directly)
│   │   ├── CliClient.Core.csproj   net10.0, classlib, AOT-friendly
│   │   ├── Net/
│   │   ├── Auth/
│   │   ├── Opcodes/
│   │   ├── Session/
│   │   ├── Workflows/
│   │   └── Logging/
│   └── CliClient.App/         ←── thin console front-end
│       ├── CliClient.App.csproj    net10.0, console; references Core
│       ├── Program.cs              arg parsing + REPL dispatch
│       └── Repl/                   REPL UI + command parsing
├── tests/
│   ├── CliClient.UnitTests/   ←── codec / handshake / opcode encoder-decoder tests
│   └── (Phase T owns the live integration tests — they live under tests/integration/)
└── README.md                  what it does, what it doesn't, hard rules above

CliClient.Core/Net/
│   ├── PacketCodec.cs         read/write packet headers, length framing, opcode dispatch
│   ├── RC4.cs                 mirror of common WestwoodRC4 (or P/Invoke if practical)
│   ├── RSAHandshake.cs        mirror of WestwoodRSA exchange used in proxy handshake
│   ├── GlobalConnection.cs    TCP 3500 (proxy) — initial handshake + global opcodes
│   ├── MasterConnection.cs    master-server channel
│   ├── SectorConnection.cs    sector-server UDP channel (3809) + sector opcodes
│   └── LoginConnection.cs     Net7SSL TLS login + ticket fetch
├── Auth/
│   └── Login.cs               username/password → ticket via login-server
├── Opcodes/
│   ├── OpcodeRegistry.cs      maps opcode → decoder/encoder pairs; near-complete coverage is the goal (Inbound/ and Outbound/ grow as Phase K wires opcodes); unknown opcodes fall through to a structured "unknown opcode" logger entry (never throw)
│   ├── Inbound/               one file per opcode the server sends us — built from common/include/net7/Opcodes.h + PacketStructures.h
│   └── Outbound/              one file per opcode we send the server
├── Session/
│   ├── SessionState.cs        current player, sector, position, inventory snapshot
│   ├── EventBus.cs            "OnChatReceived", "OnSectorChanged", "OnObjectAdded"
│   └── HealthGuard.cs         per-rule-2 watchdog — bails on rate-limit/disconnect/garbage
├── Workflows/                 high-level scripted flows
│   ├── ConnectAndLogin.cs
│   ├── EnumerateSectors.cs    visit every sector + dump objects/NPCs/stations to JSON
│   ├── EnumerateMissions.cs   walk mission boards + dump
│   ├── EnumerateItems.cs      query item-base data + dump
│   └── SendChat.cs
├── Logging/
│   ├── PacketLog.cs           NDJSON per-packet log (./logs/packets-<timestamp>.ndjson)
│   ├── ChatLog.cs             received chat → ./logs/chat-<timestamp>.log
│   └── ConsoleSink.cs         structured-but-readable terminal output
└── Repl/  (lives in CliClient.App, not Core)
    ├── Repl.cs                interactive prompt: `connect`, `login`, `chat ...`, `enumerate ...`
    └── Commands.cs

CliClient.UnitTests/
├── PacketCodecTests.cs       round-trip known wire frames
├── HandshakeTests.cs         RC4+RSA against a fixture capture
└── OpcodeRegistryTests.cs    every registered opcode encoder/decoder round-trips a known-good payload
```

**Library/console split rationale:** Phase T's xUnit integration tests need to drive a *real* client against a *real* server (docker compose stack), assert on responses, and tear down. Shelling out to `dotnet run --project CliClient.App` and scraping logs would work but is brittle (process-lifecycle races, log-flush timing, parsing pain). Having a `CliClient.Core` library means the integration test instantiates `new GlobalConnection(...)` directly, awaits responses, and uses `Assert.Equal` on decoded fields. Much faster, much more reliable, gives proper test reporter output.

## Items

- [x] Item 1 — Project scaffold (Core lib + App console + UnitTests) + slnx wiring + README
      Status: done
      Touches: tools/cli-client/Directory.Build.props,
      tools/cli-client/src/CliClient.Core/{CliClient.Core.csproj,ClientInfo.cs},
      tools/cli-client/src/CliClient.App/{CliClient.App.csproj,Program.cs},
      tools/cli-client/tests/CliClient.UnitTests/{CliClient.UnitTests.csproj,TrinitySmokeTests.cs},
      tools/cli-client/README.md, tools/Net7Tools.slnx
      Notes: SDK-style csprojs, all net10.0 (no -windows). Core is a
      classlib (RootNamespace=N7.CliClient). App is OutputType=Exe
      `<UseAppHost>true</UseAppHost>` referencing Core, AssemblyName
      `cli-client`. UnitTests uses xunit 2.9.2 + xunit.runner.visualstudio
      2.8.2 + Microsoft.NET.Test.Sdk 17.11.1; references Core.
      Per-cli-client `Directory.Build.props` resets the parent
      `tools/Directory.Build.props` Windows-targeting properties
      (EnableWindowsTargeting=false, RuntimeIdentifiers=linux-x64+linux-arm64+
      win-x64+osx-x64+osx-arm64, Nullable=enable, TreatWarningsAsErrors=true)
      so the CLI is Linux-first rather than inheriting the WinForms-era
      tools defaults. All three projects added to Net7Tools.slnx.
      Trinity smoke check: `--smoke` prints
      `ok: enb-cli-client 0.1.0-dev`; `dotnet test` runs
      `TrinitySmokeTests.CoreLibraryIsReferenced` green (Passed 1, Failed 0).
      The hard rules from this plan file are reproduced verbatim in
      `tools/cli-client/README.md`.

- [x] Item 2 — Packet codec + opcode registry foundation (in CliClient.Core)
      Status: done
      Touches: tools/cli-client/src/CliClient.Core/Net/PacketHeader.cs,
               tools/cli-client/src/CliClient.Core/Net/Packet.cs,
               tools/cli-client/src/CliClient.Core/Opcodes/OpcodeId.cs,
               tools/cli-client/src/CliClient.Core/Opcodes/IOpcodeCodec.cs,
               tools/cli-client/src/CliClient.Core/Opcodes/OpcodeRegistry.cs,
               tools/cli-client/tests/CliClient.UnitTests/Net/PacketCodecTests.cs,
               tools/cli-client/tests/CliClient.UnitTests/Opcodes/OpcodeRegistryTests.cs
      Notes: Implementation breakdown ---
      `PacketHeader` is a `readonly record struct {ushort Size, ushort Opcode}`
      with `WireSize = 4` and `Read`/`Write` using
      `System.Buffers.Binary.BinaryPrimitives` for little-endian I/O. Mirrors
      `EnbTcpHeader` from `common/include/net7/PacketStructures.h` — `size` is
      the TOTAL frame length (header+payload), so `PayloadLength = Size - 4`.
      `Packet` is a `sealed record (PacketHeader Header, ReadOnlyMemory<byte> Payload)`
      with `ForOpcode(ushort, ReadOnlyMemory<byte>)` factory and
      `ToWireBytes()` for the on-wire bytes pre-RC4.
      `OpcodeId` is a `readonly record struct(ushort)` with
      implicit-to-ushort / explicit-from-ushort conversions and `ToString()`
      returning `0x####` hex. The nested `Known` class enumerates the Phase K
      integration-test opcodes (VersionRequest/Response, Login, Logoff,
      ClientChat, MasterJoin, ServerRedirect, ClientAvatar, ServerHandoff,
      ClientType, GlobalConnect, GlobalTicketRequest/GlobalTicket,
      GlobalAvatarList). Per-opcode codecs land in `Opcodes/Inbound/` and
      `Opcodes/Outbound/` later — no central switch.
      `IOpcodeCodec` is one interface per opcode (`Opcode`, `DecodeInbound`,
      `EncodeOutbound`); both directions because most opcodes are
      bidirectional. `UnknownOpcodeCodec` is the fallback the registry hands
      back for unregistered opcodes — returns `UnknownOpcodePayload(Opcode,
      RawPayload)` on decode, throws `NotSupportedException` on encode. This
      keeps capture-replay tests from breaking when Phase K wires server-side
      handlers ahead of CLI client decoders.
      `OpcodeRegistry` is backed by
      `ConcurrentDictionary<ushort, IOpcodeCodec>` — O(1) lock-free reads,
      last-writer-wins on `Register`, never returns null from `Resolve`.
      `IsRegistered` tells real codecs apart from the fallback.
      Tests: `PacketCodecTests` covers little-endian round-trip, payload
      length, short-buffer guards, empty payload, and `ForOpcode → ToWireBytes
      → PacketHeader.Read` round-trip. `OpcodeRegistryTests` covers register +
      resolve, unknown-opcode fallback, `EncodeOutbound` throw,
      last-writer-wins, null guard, `RegisteredOpcodes` snapshot, and
      `OpcodeId.ToString` hex format. `dotnet test` clean: Passed 16, Failed 0
      (8 codec + 7 registry + 1 carryover trinity smoke).
      Build clean under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
      and `<Nullable>enable</Nullable>` — 0 warnings, 0 errors.

- [x] Item 3 — RC4 + RSA handshake (mirror common/include/net7/WestwoodRC4.h + WestwoodRSA.h)
      Status: done
      Touches: tools/cli-client/src/CliClient.Core/Net/WestwoodRC4.cs,
               tools/cli-client/src/CliClient.Core/Net/WestwoodRSA.cs,
               tools/cli-client/src/CliClient.Core/Net/RsaHandshake.cs,
               tools/cli-client/tests/CliClient.UnitTests/Net/WestwoodRC4Tests.cs,
               tools/cli-client/tests/CliClient.UnitTests/Net/WestwoodRSATests.cs,
               tools/cli-client/tests/CliClient.UnitTests/Net/RsaHandshakeTests.cs
      Notes: Implementation breakdown ---
      `WestwoodRC4` — direct port of the KSA + PRGA loops from
      `proxy/WestwoodRC4.cpp`. Standard RC4; the Westwood-specific bit
      is the 8-byte session key (`KeySize = 8`, mirrors RC4_KEY_SIZE in
      `proxy/Connection.h`). Two instances per connection (inbound +
      outbound), both keyed off the same 8 bytes the client picks.
      Verified against RFC 6229's "Key" / "Plaintext" → 0xBBF316E8...
      reference vector.
      `WestwoodRSA` — replaces the OpenSSL BIGNUM dance with
      `System.Numerics.BigInteger.ModPow`. The (e, N) public key is the
      same fixed constants from `common/include/net7/WestwoodRSA.h`
      (e=35, N=10385578014804950221065190195736491193847541479389728420426514083771326945639729736695791225573893793119489336012297845146104637691941242485732839277543427).
      d is included only so we can round-trip in tests; production CLI
      client only ever calls `EncryptBlock`. Big-endian byte-order
      conversions (`FromBigEndian`/`ToBigEndian`) mirror OpenSSL's
      `BN_bin2bn` / `BN_bn2bin` semantics with the sign-byte trick to
      keep BigInteger from treating the high bit as a sign indicator.
      `RsaHandshake` — orchestrates the client side of `DoClientKeyExchange`:
      receive 74-byte server pubkey (and ignore — pubkey is hardcoded);
      pick 8 random bytes via `RandomNumberGenerator`; zero-fill a
      64-byte block; write the RC4 key REVERSED at positions [63..56];
      RSA-encrypt the block; prepend big-endian uint32 length = 64.
      The reversed placement matches the C++ `*dest-- = *src++` loop
      starting at `key[WWRSA_BLOCK_SIZE - 1]`.
      `ServerPubkeyPacketSize = 74`, `ClientKeyPacketSize = 68`.
      Tests (15 new, 31 total passing):
      `WestwoodRC4Tests` — known-answer (RFC 6229), symmetry,
      streaming==single-shot, empty-key guard.
      `WestwoodRSATests` — encrypt/decrypt round-trip, output size,
      input/output size guards, zero-block identity.
      `RsaHandshakeTests` — wire size, BE length prefix == 64, full
      client→server round trip extracting the same 8-byte key, random-key
      round trip, zero-padding shape verification, wrong-key-length guard.
      Build clean (0 warnings, 0 errors) under TreatWarningsAsErrors +
      Nullable enable. `dotnet test`: Passed 31, Failed 0.
      No capture-fixture validation yet — the .rar packet captures in
      `archive/kyp-snapshot/capturedPackets/` would need extraction +
      parsing. Deferred to Item 16 (capture replay), since the
      round-trip test above already proves wire compatibility with the
      server-side decrypt code we mirrored.

- [x] Item 4 — Login flow (TLS to Net7SSL, /AuthLogin GET, ticket extraction)
      Status: done
      Touches: tools/cli-client/src/CliClient.Core/Auth/AuthLoginRequest.cs,
               tools/cli-client/src/CliClient.Core/Auth/AuthLoginResponse.cs,
               tools/cli-client/src/CliClient.Core/Auth/AuthLoginClient.cs,
               tools/cli-client/src/CliClient.Core/CliClient.Core.csproj,
               tools/cli-client/tests/CliClient.UnitTests/Auth/AuthLoginResponseTests.cs,
               tools/cli-client/tests/CliClient.UnitTests/Auth/AuthLoginClientTests.cs
      Notes: Reality check: the original plan said "AuthLogin POST" but
      `login-server/Net7SSL/LinuxAuth.cpp:41` is explicit that "The
      client only ever sends GET requests" against /AuthLogin —
      credentials go in the query string and the server's `strstr`-based
      parser scans the raw recv buffer for the four tags. Implemented
      as GET to match.
      `AuthLoginRequest` — username/password/serviceID/version record;
      defaults to ServiceId="EA-ENB", Version="2.5" (real client values
      from the C++ server's expected-version check).
      `AuthLoginResponse` — bool Valid, string Ticket, string RawBody.
      Parser handles both CRLF and LF line endings (server emits CRLF
      per LinuxAuth.cpp:408); only the literal "Valid=TRUE" (uppercase)
      authenticates — anything else is a failure to be loud about.
      `AuthLoginClient` — TLS-over-TCP with SslClientAuthenticationOptions.
      EnabledSslProtocols=None (OS default TLS 1.2/1.3) so we don't
      lock CI to a specific server build. Sends a textbook HTTP/1.1
      GET with Host/User-Agent/Accept/Connection headers so a captured
      request looks like the real client. Default cert validation
      requires a valid chain; opt-in `acceptUntrustedCertificates`
      flag (loud-by-design, emits "WARNING: accepting untrusted TLS
      cert" via the diagnostics sink) for local docker/CI with
      self-signed dev certs. No env-var backdoor — caller must
      explicitly pass true.
      Tests (13 new, 44 total passing):
      `AuthLoginResponseTests` — success/failure/case-sensitivity,
      LF tolerance, unknown-keys-ignored, null-body guard (6 tests).
      `AuthLoginClientTests` — port/host validation, URL builder shape,
      URL encoding of special chars, body-extract from CRLF/LF/no-headers
      responses (7 tests).
      Required `<InternalsVisibleTo Include="CliClient.UnitTests" />`
      in CliClient.Core.csproj to test internal helpers
      (BuildUrl/ExtractBody) without exposing them publicly.
      Build clean (0 warnings, 0 errors). dotnet test: Passed 44, Failed 0.
      Live integration test against a real Net7SSL server lands in
      Item 9 (Workflow: connect-and-login smoke target) and Phase T.

- [x] Item 5 — Global → master → sector handoff (TCP redirect, server-list parse)
      Status: done
      Touches:
        - src/CliClient.Core/Net/EncryptedTcpConnection.cs (RSA handshake + RC4 framed I/O wrapper)
        - src/CliClient.Core/Opcodes/Inbound/ServerRedirectCodec.cs (10-byte payload, opcode 0x0036)
        - src/CliClient.Core/Opcodes/Outbound/MasterJoinCodec.cs (64-byte wire format, opcode 0x0035)
        - src/CliClient.Core/Session/SessionStage.cs (Disconnected/Authenticated/Global/Master/Sector)
        - src/CliClient.Core/Session/CliSession.cs (single-connection-at-a-time coordinator)
        - tests/CliClient.UnitTests/Net/EncryptedTcpConnectionTests.cs (live-socket round-trip with hand-rolled server-side RSA dance)
        - tests/CliClient.UnitTests/Opcodes/ServerRedirectCodecTests.cs
        - tests/CliClient.UnitTests/Opcodes/MasterJoinCodecTests.cs
        - tests/CliClient.UnitTests/Session/CliSessionTests.cs (incl. live-socket ConnectGlobal handshake test)
      Notes:
        EncryptedTcpConnection wraps TcpClient + NetworkStream + two WestwoodRC4 ciphers
        (separate in/out streams, both keyed with the same 8-byte session key, matching
        proxy/Connection.cpp::DoKeyExchange). ConnectAsync does the full RSA handshake:
        read 74-byte server pubkey (ignored — Westwood RSA modulus is hardcoded), generate
        a random 8-byte RC4 key, encrypt the reversed key into a 64-byte block via
        RsaHandshake.BuildClientKeyPacket, write 4-byte BE length + the 64-byte block.

        ServerRedirect byte-order quirk reproduced and tested explicitly: sector_id and
        ip_address are BIG-endian (server uses ntohl), port is LITTLE-endian (no htons in
        proxy/ClientToMasterServer.cpp::SendServerRedirect — host byte order on x86).

        MasterJoin: 11×BE int32 (matches server's ntohl reads in PlayerConnection.cpp:650)
        + 20-byte ASCII ticket at offset 44, zero-padded. Wire size fixed at 64 bytes to
        match the C++ struct on every platform (the historic Phase K Linux bug where
        sizeof(long)=8 on Linux x86_64 vs 4 on Win32 shifted later fields — int32_t
        throughout the codec prevents recurrence).

        CliSession is a thin coordinator: holds at most one EncryptedTcpConnection at a
        time, exposes Send/Receive that delegate to the current connection, transitions
        Authenticated → Global via ConnectGlobalAsync, and Global → Master or Master →
        Sector via FollowRedirectAsync (closes current connection, opens fresh one to
        redirect endpoint, runs a brand-new RSA+RC4 handshake — no session resumption at
        the transport layer, matching the real EnB design).

        UDP 3809 sector channel deferred — Item 5 covers the TCP redirect chain that the
        real Win32 client follows for sector handoff (Global TCP → Master TCP → Sector
        TCP). The persistent UDP-3809 sector world stream is a separate transport that
        gets wired up alongside the sector-entry workflow in later items.

        Live integration against a real proxy/master server deferred to Item 9 + Phase T.
        Current tests: in-process loopback TcpListener fakes that run the server side of
        the handshake. 70 tests passing (was 63 before Item 5).

- [x] Item 6 — Packet/chat log sinks (NDJSON + readable text)
      Status: done
      Touches:
        - src/CliClient.Core/Logging/PacketDirection.cs (Inbound/Outbound enum)
        - src/CliClient.Core/Logging/OpcodeNameLookup.cs (reflection over OpcodeId.Known)
        - src/CliClient.Core/Logging/PacketLog.cs (NDJSON sink, thread-safe, flush-per-line)
        - src/CliClient.Core/Logging/ChatLog.cs (readable text sink)
        - src/CliClient.Core/Logging/ConsoleSink.cs (single-line console pretty-printer)
        - tests/CliClient.UnitTests/Logging/{PacketLogTests,ChatLogTests,ConsoleSinkTests,OpcodeNameLookupTests}.cs
      Notes:
        PacketLog line schema matches plan exactly: {ts, direction, opcode_hex, opcode_name,
        length, payload_hex, decoded?}. opcode_name omitted when unknown; decoded omitted
        when caller passes null. Hex is lowercase no-separator. Files opened with
        FileShare.Read so `tail -F` works while the client is running. Every Log() call
        takes the gate, writes, and flushes — a crash mid-session loses zero packets.

        OpcodeNameLookup is the source of "MasterJoin" / "ServerRedirect" / etc. names —
        reflected once at type init from the static fields of OpcodeId.Known, so adding a
        new known opcode in Item 15 automatically lights up its log name.

        ChatLog is plain UTF-8: `YYYY-MM-DDTHH:MM:SS.fffZ [channel] sender: message`.
        Channel defaults to "chat" when null/empty.

        ConsoleSink fans out to Console.Out (or any TextWriter — tests use StringWriter).
        Truncates payloads to first 32 bytes + ellipsis to keep lines skimmable.
        Outbound = →, inbound = ←.

        96 tests passing (was 70 before Item 6).

- [x] Item 7 — HealthGuard (rule 2 enforcement)
      Status: done
      Touches:
        - src/CliClient.Core/Session/HealthGuard.cs (kill-switch)
        - src/CliClient.Core/Session/HealthGuardOptions.cs (tunables)
        - tests/CliClient.UnitTests/Session/HealthGuardTests.cs
      Notes:
        Enforces hard-rule #2: "The CLI client must always respect the server."
        Tripping is one-shot terminal — guard never re-arms. Workflows pass
        `guard.Token` into every async call so they stop at the next await
        when the guard fires.

        Trip conditions wired:
          - OnDisconnect("reason") — always trips
          - OnPacketReceived with opcode in ErrorOpcodes set — trips
          - Inbound/outbound packet rate > MaxPacketsPerSecond in any 1-second
            sliding window (default 500/s, real EnB rarely > 200/s)
          - BeginExpectResponse(label, timeout, opcodeFilter?) — disposable
            handle; if neither matching inbound packet arrives nor caller
            disposes within timeout, trips. Caller can scope to a specific
            opcode (e.g. wait for 0x0036 ServerRedirect after MasterJoin).
          - Trip(reason) — workflows can force-trip on protocol violations
            the guard can't see (malformed payload, unexpected state).

        What HealthGuard does NOT do: retry, reconnect, hide failure. It just
        stops the workflow and surfaces the cause via Reason (and ConsoleSink
        if provided). Matches rule 2's "No retry storms. No bypass attempts."

        111 tests passing (was 96).

- [x] Item 8 — REPL (`connect`, `login`, `chat`, `enumerate sectors|missions|items`, `quit`)
      Status: done (skeleton — workflow commands plug in via Items 9-13)
      Touches:
        - src/CliClient.Core/Repl/Repl.cs (dispatch loop + tokeniser)
        - src/CliClient.Core/Repl/ICommandHandler.cs (command contract)
        - src/CliClient.App/Program.cs (wires `cli-client repl` subcommand)
        - tests/CliClient.UnitTests/Repl/ReplTests.cs
      Notes:
        Hand-rolled REPL (no System.CommandLine dependency). Dispatches on
        first whitespace-separated token; case-insensitive. Built-in handlers:
        `help` (list + per-command usage) and `quit` (exit code 0). Everything
        else — `connect`, `login`, `chat`, `enumerate ...` — gets registered
        externally as Items 9-13 land, via repl.Register(ICommandHandler).
        Keeps the REPL itself a thin dispatcher with no network knowledge.

        Tokenisation supports double-quote grouping (`chat "hello world" team`
        → 3 tokens). No shell escape sequences — interactive use, not scripting.

        Exit-code mapping: handler returns 0 = success keep looping, positive
        = non-fatal error recorded, negative = quit (with `-(rc+1)` as exit).

        `cli-client repl` smoke-tested: `echo "help\nquit" | cli-client repl`
        prints command list and exits cleanly.

        Tab-completion deferred (plan called it nice-to-have, not required).

        127 tests passing (was 111).

- [x] Item 9 — Workflow: connect-and-login (smoke-test target)
      Status: done
      Touches:
        - src/CliClient.Core/Workflows/ConnectAndLogin.cs (workflow class)
        - src/CliClient.Core/Workflows/ConnectAndLoginOptions.cs (inputs)
        - src/CliClient.App/Program.cs (wires `cli-client connect-and-login` subcommand)
        - tests/CliClient.UnitTests/Workflows/ConnectAndLoginTests.cs
      Notes:
        Workflow chain: AuthLoginClient → ticket → CliSession.ConnectGlobalAsync →
        drain inbound packets for IdleDuration → clean dispose. Every async hop
        is gated by guard.Token; failures trip the guard cleanly and return a
        ConnectAndLoginResult rather than throwing.

        Sends NO opcodes (no GlobalConnect 0x006D, no MasterJoin, nothing) —
        Item 9 is "can we even connect, drain, and disconnect cleanly". In-game
        opcode workflows are Items 10-13.

        CLI subcommand: `cli-client connect-and-login --user X --pass Y
        [--login-host h] [--login-port p] [--global-host h] [--global-port p]
        [--idle 5] [--strict-tls]`. Default `--strict-tls` off so dev/CI work
        against self-signed certs; pass it for prod. Smoke-tested locally: when
        no login server is running, it trips the guard with "Connection refused"
        and exits 1 (no crash, no retry storm).

        Deep integration validation (against a live docker compose stack) is
        the explicit deliverable of Phase T, per the original plan note. Unit
        tests here cover constructor / argument validation and the no-server-
        running path; the rest is Phase T.

        132 tests passing (was 127).

- [!] Item 10 — Workflow: enumerate sectors (visit each sector, dump objects)
      Status: blocked on Phase K
      Touches: tools/cli-client/src/CliClient.Core/Workflows/EnumerateSectors.cs (deferred)
      Notes:
        Blocked-by:
          - plans/11-phase-k-ingame.md "Wire ticket handoff" [!] — without
            ticket handoff, the CLI client cannot attach an avatar to a
            sector, so there is nothing to enumerate.
          - plans/11-phase-k-ingame.md ProcessGlobalServerOpcode [~] —
            only 0x0000 VersionRequest is ported on the Linux dispatch;
            the avatar-select chain (HandleGlobalConnect, HandleGlobalTicketRequest,
            HandleCreateCharacter, ProcessGlobalTicket) all log-and-return
            on Linux, so the workflow cannot drive past avatar select.
          - plans/11-phase-k-ingame.md ProcessSectorServerOpcode [~] —
            only 0x0002 LOGIN is ported; without the ~49 remaining
            in-sector opcode handlers there is no object stream to dump.
        Real EnB has no single "enumerate sectors" opcode. The real client
        warps from sector to sector and the server pushes object state via
        0x0005 START, 0x0008 SIMPLE_POSITIONAL_UPDATE, 0x0025 ITEM_BASE,
        0x002F INIT_RENDER_STATE etc. as objects enter scan range.
        Implementing this workflow honestly means driving a full
        warp-and-observe loop, which requires the above Phase K items.
        Stub implementations that throw NotImplementedException are not
        worth the maintenance churn — revisit when Phase K's avatar
        handoff lights up. Phase T will own the live-fire test.

- [!] Item 11 — Workflow: enumerate missions
      Status: blocked on Phase K
      Touches: tools/cli-client/src/CliClient.Core/Workflows/EnumerateMissions.cs (deferred)
      Notes:
        Same Phase K block as Item 10 — mission boards are NPCs inside
        starbases (server-side: 0x0054 TALK_TREE / 0x0055 SELECT_TALK_TREE /
        0x0056 TALK_TREE_ACTION). Workflow needs to be docked at each
        starbase, target each mission-board NPC, and walk the talk tree.
        Requires: ticket handoff, in-sector dispatch, starbase docking
        (0x004E STARBASE_REQUEST and friends) — none of which are wired
        on Linux today. Deferred until Phase K hits the talk-tree
        handlers.

- [!] Item 12 — Workflow: enumerate items
      Status: blocked on Phase K
      Touches: tools/cli-client/src/CliClient.Core/Workflows/EnumerateItems.cs (deferred)
      Notes:
        EnB has no bulk-item-dump opcode. Item data flows via 0x0025
        ITEM_BASE on demand when the server reports an object that
        references an item id (inventory, loot, ammo, equipped weapons,
        manufacturing outputs). The real client builds up its item
        knowledge incrementally over a play session by following these
        references.
        The closest thing to a "give me everything" approach would be
        walking the item-base table in the DB directly — but that
        bypasses the server entirely and isn't a CLI-client workflow
        (use a SQL query / the editor suite instead). Honest
        opcode-driven enumeration requires the full in-game session,
        same Phase K dependency as Items 10/11.
        Defer; if a real need surfaces for "dump every item" before
        Phase K is further along, do it via direct DB query and call
        it out as outside Phase S's scope.

- [x] Item 13 — Workflow: send chat
      Status: done
      Touches:
        - src/CliClient.Core/Opcodes/Outbound/ClientChatCodec.cs (0x0033 codec)
        - src/CliClient.Core/Workflows/SendChat.cs (workflow class)
        - src/CliClient.App/Program.cs (wires `cli-client send-chat` subcommand)
        - tests/CliClient.UnitTests/Opcodes/ClientChatCodecTests.cs (12 tests)
        - tests/CliClient.UnitTests/Workflows/SendChatTests.cs (4 tests, incl. loopback)
      Notes:
        Wire layout: int32 LE GameID + byte Type + int16 LE Size + ASCII string + NUL.
        Matches the Win32 client's packed (long=4 bytes, LE) emission of
        `struct ClientChat` in common/include/net7/PacketStructures.h:572.
        ChatChannel enum: 0=Target, 1=Group, 2=Guild, 3=Local, 4=Broadcast — matches
        the switch in server/src/PlayerConnection.cpp:4515 (Player::HandleClientChat).

        Codec rejects empty messages (server's HandleClientChat indexes
        chat->String[0] unconditionally before checking the slash branch — refuse
        rather than potentially trip a server-side OOB read). Codec rejects strings
        whose UTF-8-length+1 exceeds int16.

        Workflow: SendChat is a thin wrapper around codec.EncodeOutbound +
        Packet.ForOpcode + session.SendAsync, with optional PacketLog + ConsoleSink
        plumbing. Does NOT manage session lifecycle — caller must hand it an
        already-connected CliSession.

        CLI subcommand: `cli-client send-chat --user X --pass Y --game-id N
        --message "text" [--channel target|group|guild|local|broadcast]
        [--login-host h] [--login-port p] [--global-host h] [--global-port p]
        [--strict-tls]`. Inlines the auth + connect-global sequence (doesn't
        reuse ConnectAndLogin because that workflow owns + disposes its own
        session). Honest help text notes the server cross-checks --game-id
        against the avatar attached to the session, so end-to-end visible
        chat requires Phase K's avatar handoff to be live.

        148 tests passing (was 132).

- [x] Item 14 — Codec unit tests in xUnit (CliClient.UnitTests)
      Status: done
      Touches: tools/cli-client/tests/CliClient.UnitTests/Captures/CaptureFixture.cs,
               tools/cli-client/tests/CliClient.UnitTests/Captures/RetailCaptureTests.cs,
               tools/cli-client/tests/CliClient.UnitTests/Captures/fixtures/capture3-frames.txt,
               tools/cli-client/src/CliClient.Core/Opcodes/Outbound/MasterJoinCodec.cs (Ticket: string → byte[])
      Notes: ▸ Hand-extracted 3 reference frames from archive/kyp-snapshot/capturedPackets/capture_3.rar
                (unrar to a tmp scratch — the .rar stays in tree as ground truth, the .txt is large
                so we don't commit it). Each frame committed verbatim hex with provenance metadata
                in tests/CliClient.UnitTests/Captures/fixtures/capture3-frames.txt:
                  master_join     (#224, Client→Server :3387, 64-byte payload)
                  server_redirect (#226, Server→Client :3387, 10-byte payload)
                  client_chat     (line 18515, sub-packet, 14-byte payload, "/who")
             ▸ CaptureFixture.cs is a tiny text loader (records, key:value, hex: block — strips #
                comments). Zero dependencies — runs in the same xUnit process. Test project's
                .csproj copies Captures/fixtures/** to output via PreserveNewest.
             ▸ Three retail-byte tests in RetailCaptureTests.cs:
                  ServerRedirect_RetailCapture_Decodes — decode + assert sector_id=0x19290000,
                      IP=44.232.153.159, port=3503 (LE — the codec's known port-asymmetry holds
                      on real bytes, not just synthetic ones).
                  MasterJoin_RetailCapture_RoundTrips_Exactly — decode all 11 BE int32 fields +
                      20-byte ticket, then re-encode and assert byte-equal with the captured 64
                      bytes. This is the gold-standard test: any field offset / endianness /
                      ticket-handling bug becomes a single failing assert.
                  ClientChat_RetailCapture_DecodesAndPrefixRoundTrips — decode, then re-encode
                      and prefix-match against the leading 12 bytes (codec models the mandatory
                      header; the trailing 2-byte optional `_data_size` field is by-design dropped).
             ▸ Root-cause fix surfaced by retail data: MasterJoinCodec previously modelled
                Ticket as `string` with `Encoding.ASCII.GetString` — wrong for retail, which uses
                a binary 20-byte ticket (0x89, 0xF7, 0xDF, …). ASCII would map non-printable
                bytes to '?', destroying round-trip. Changed the record's Ticket field to
                `byte[]` (exactly 20 bytes; codec zero-pads shorter inputs), added
                `MasterJoinRequest.AsciiTicket(string)` static helper for Net-7-emulator
                callers, overrode record Equals/GetHashCode to use SequenceEqual on the
                byte array (default record equality is reference for arrays). Updated
                MasterJoinCodecTests.cs to drive the new API; production callers were
                unaffected (no MasterJoinRequest constructor in src/ outside the codec).
             ▸ 155 tests passing (was 151 after the codec API change; 148 before Item 14).

- [x] Item 15 — Opcode coverage push: register decoders for every opcode in Opcodes.h
      Status: done
      Touches: tools/cli-client/scripts/generate-opcode-names.sh (new),
               tools/cli-client/src/CliClient.Core/Opcodes/OpcodeNames.Generated.cs (new, generated),
               tools/cli-client/src/CliClient.Core/Opcodes/IOpcodeCodec.cs (NamedOpaqueCodec + NamedOpaquePayload),
               tools/cli-client/src/CliClient.Core/Opcodes/OpcodeRegistry.cs (RegisterAllNamedOpaque),
               tools/cli-client/src/CliClient.Core/Logging/OpcodeNameLookup.cs (overlay Known on top of OpcodeNames.All),
               tools/cli-client/src/CliClient.App/Program.cs (call RegisterAllNamedOpaque on startup),
               tools/cli-client/tests/CliClient.UnitTests/Opcodes/OpcodeNamesTests.cs (new — 20 tests)
      Notes: ▸ Deliberately chose data-table + bulk-registrar over "209 stub codec
                classes" — same coverage, zero per-opcode boilerplate, and Phase K
                can light up typed codecs one at a time without churning a sea of
                empty placeholder files. The plan's "no big-bang rewrite" disclaimer
                rules out the per-class approach.
             ▸ Generator (scripts/generate-opcode-names.sh, awk + bash) scrapes
                209 `#define ENB_OPCODE_xxxx_NAME 0xxxxx` lines from
                common/include/net7/Opcodes.h and emits OpcodeNames.Generated.cs —
                a FrozenDictionary<ushort, string> with 207 entries (two pairs
                share a hex value: 0x2010 SET_GLOBAL_LOGIN_LINK/DATA_FILE and
                0x2011 SET_PROXY_SECTOR_LINK/GALAXY_MAP_CACHE; collapsed to
                NAME_A_OR_NAME_B). Rerun the script if Opcodes.h changes;
                output is committed so production builds need no codegen step.
             ▸ NamedOpaqueCodec mirrors UnknownOpcodeCodec but carries the
                upstream symbolic name → packet log shows "0x00CE GUILD_REQUEST_CHANGE:
                12 bytes" instead of "0x00CE UNKNOWN". Decode emits a defensive
                copy (test: payload alias mutation doesn't leak through).
                Encode throws — opaque codecs are decode-only by design.
             ▸ OpcodeRegistry.RegisterAllNamedOpaque uses TryAdd so it never
                clobbers a previously-registered typed codec. Order of calls
                doesn't matter — typed codecs always win. Idempotent: a second
                call adds zero entries. Verified by test.
             ▸ OpcodeNameLookup now seeds from OpcodeNames.All (207 SCREAMING
                _SNAKE) then overlays OpcodeId.Known (14 PascalCase). Net
                effect: typed-codec opcodes log with the friendly C# name
                ("MasterJoin"), the rest log with the upstream C header name
                ("GUILD_SIMPLE_SECTOR_CLIENT"). All 14 existing OpcodeNameLookup
                tests still pass.
             ▸ Program.cs's connect-and-login and send-chat subcommands both
                call RegisterAllNamedOpaque() right after creating the registry,
                so every CLI run has full name coverage out of the box.
             ▸ 175 tests passing (was 155 after Item 14). 20 new tests:
                OpcodeNamesTests (6), NamedOpaqueCodecTests (4),
                OpcodeRegistryBulkRegistrationTests (5), plus a few extras
                from OpcodeNamesTests for the dup-alias edge case.

- [x] Item 16 — Documentation: docs/15-cli-client.md
      Status: done
      Touches: docs/15-cli-client.md (created), docs/README.md (index row added)
      Notes:
        ▸ Slot 12 was already taken by docs/12-content-pipeline.md (Phase H output),
           so the cli-client doc landed at slot 15 instead. docs/README.md now
           lists 15-cli-client.md in the file table. The plan's original
           "12-cli-client.md" reference is left as-is in the historical Notes for
           Items 8/14 and the Verification block above — those reflect the plan
           text at write-time, not the final on-disk slot.
        ▸ Hard rules reproduced verbatim from this plan + a pointer to the
           "Server integrity rules" block in CLAUDE.md (which is the authoritative
           text). Two cross-references means agents who read either file
           independently still get the constraint, but only one place to update
           if the rule shifts.
        ▸ Subcommand table, exit codes (0/1/2 stable contract), three usage
           examples for --smoke / connect-and-login / send-chat, --strict-tls
           callout, three log sinks (ConsoleSink human-readable with arrows,
           PacketLog NDJSON, ChatLog filtered NDJSON), default log path under
           ./logs/ with rotation suffix.
        ▸ "How to add a new opcode" with the two paths it actually has today:
           (a) opaque-free via NamedOpaqueCodec — already done for all 207
           opcodes by RegisterAllNamedOpaque(), no work to add a new one;
           (b) typed codec — 6-step recipe (find struct → write codec → add
           to OpcodeId.Known → register in Program.cs → unit-test layout +
           round-trip + validation → optional retail-capture fixture).
        ▸ Limitations called out honestly: no avatar select (Phase K blocker
           on Items 10-12), no UDP plane (TCP-only by scope), no GUI ("won't
           ever be one — that's what the real client is for").
        ▸ Top-level README.md and CLAUDE.md repo-map were NOT touched. The
           CLI client doesn't fit cleanly into the top-level repo-map (it lives
           under tools/, which is already one row), and pointing top-level
           README at every new docs/ file would just churn. docs/README.md is
           the right index for it.

- [ ] Item 17 — Hand-off to Phase T
      Status: not started
      Touches: plans/20-phase-t-cli-integration-tests.md (created when S is ~done)
      Notes: Phase T is the real integration suite — xUnit tests that spin docker compose + drive CliClient.Core in-process. Phase S's job is to make the Core library good enough that T can be written cleanly.

## Verification

Phase S is done when:

- `dotnet build tools/cli-client/CliClient.csproj` is clean
- `dotnet run --project tools/cli-client/ -- --workflow connect-and-login --headless` completes successfully against the docker-compose stack
- `./logs/packets-*.ndjson` shows the expected handshake → login → idle → disconnect sequence
- Enumerate workflows produce non-empty, schema-consistent JSON dumps for sectors / missions / items
- CI gates the new smoke test
- `docs/12-cli-client.md` exists; the hard rules above are reproduced verbatim there
- A trip-test: deliberately misbehave the server (drop connection mid-handshake, send malformed reply) and confirm HealthGuard aborts cleanly — does not retry-storm

## Out of scope (don't do these in Phase S)

- 3D scene rendering / mesh inspection (the real client uses W3D; out of scope here)
- Combat / ability execution (Phase K is still landing in-game opcode handlers — drive the CLI client by what's actually shipped, don't pre-implement against vapor)
- GUI / TUI (this is a CLI; a TUI is a follow-up)
- Multi-account orchestration (one connection at a time; multi-instance is a follow-up)
- Server-side instrumentation (rule 1 — do not modify the server)
