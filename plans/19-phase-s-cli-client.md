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

- [x] Item 17 — Hand-off to Phase T
      Status: done
      Touches: plans/20-phase-t-cli-integration-tests.md (populated)
      Notes:
        ▸ plans/20-phase-t-cli-integration-tests.md was created earlier in
           the Phase S run and is now a concrete, actionable plan: 10 items,
           full project structure (ServerFixture + ClientFixture + per-area
           subdirs), CI integration block, hard-rules block (same as Phase S
           rules 1-4 applied to the test harness), verification block, and
           explicit out-of-scope items. No further hand-off doc work needed
           on the Phase S side.
        ▸ Public surface audit for in-process driveability: ConnectAndLogin,
           SendChat, CliSession, AuthLoginClient, OpcodeRegistry,
           HealthGuard, PacketLog, ChatLog, ConsoleSink, MasterJoinCodec,
           ServerRedirectCodec, ClientChatCodec, NamedOpaqueCodec, OpcodeId,
           OpcodeNames are all `public`. A Phase T test project that
           ProjectReferences tools/cli-client/src/CliClient.Core can
           construct any of these directly — no internal-friend hacks, no
           reflection. Confirmed by the unit test project already doing it.
        ▸ What Phase T blocks on (NOT a Phase S problem):
            – Items 10-12 of this plan (GlobalConnect / GlobalTicket /
               GlobalAvatarList / MasterJoin handoff) are [!]-blocked on
               Phase K's in-game opcode handlers. Without them, send-chat
               can't be driven end-to-end against a real server. Phase T's
               Workflows/ + Capture-replay tests need those opcodes wired
               server-side first.
            – Sector-server connect (TCP 3812) is also Phase K territory.
            – Anything UDP is out of scope for both S and T.
          Phase T can start with: TLS login round-trip, RSA/RC4 handshake,
          MasterJoin (0x0035) → ServerRedirect (0x0036), clean disconnect.
          That's enough to get the harness, ServerFixture, ClientFixture,
          golden-bytes assertion, and CI ratchet shipped. Opcode coverage
          ramps with Phase K.
        ▸ Phase S verification block at the bottom of this file references
           "docs/12-cli-client.md" — that should read "docs/15-cli-client.md"
           given the Item 16 slot conflict resolution. The verification
           block is the spec, the doc landed at 15; this Note is the
           reconciliation. Future readers: docs/15-cli-client.md is the
           file.

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

## Continuation (2026-06-01): interactive REPL UX + in-sector observability

Driven by live-play feedback. All client-side; NO server change.

- [x] Chat echo by default. Inbound 0x00A5 CLIENT_CHAT_EVENT and outbound
      0x0033 CLIENT_CHAT now print a one-line `<-- [channel] sender: msg` /
      `--> [channel] you: msg` at the prompt regardless of dump-on (previously
      only visible under dump-on). The sector drain that feeds the hooks is
      now always-on post-`enter` (was dump-on-only), started by `enter` AFTER
      its foreground drain window so it doesn't race the single-reader socket.
      `dump-off` no longer cancels the drain. Files: `Repl/SessionContext.cs`
      (EchoChat/InterpretOutbound/WriteChatLine; StartDumpDrain→StartSectorDrain;
      setter no longer auto-starts), `Logging/ConsoleSink.cs` (reused),
      `Opcodes/Records/ClientChatEventRecord.cs` (+TryExtract/ChatEvent),
      `Repl/Commands/Dump{On,Off}Command.cs`, `Repl/Commands/EnterCommand.cs`.
- [x] `chat [sector|gm|dev|beta|whisper] <message>` command (default sector).
      Faithful to `Player::HandleClientChat`: sector→Type 4, whisper→Type 0,
      gm/dev/beta→slash-command text (`/gm …`) which the server routes via
      `ChatSendChannel` and gates on admin level — the CLI does NOT bypass that
      gate. Files: `Repl/Commands/ChatCommand.cs`, registered in `Program.cs`.
- [x] `quit`/`exit` alias surfaced. `Commands` now DistinctBy(Name) so `help`
      lists `quit` once (was twice); summary reads "exit the REPL (alias: exit)".
      File: `Repl/Repl.cs`.
- [x] zsh-style interactive line editor (`Repl/LineEditor.cs`, `Repl/Completion.cs`,
      `ILineInput`). Context-aware grey command suggestions (only commands whose
      `ICommandHandler.Available` is true in the current state), Tab/Shift-Tab
      menu cycling, Enter to pick, grey argument placeholder
      (`ICommandHandler.Placeholder`) once a command is chosen. Falls back to
      plain ReadLine when stdin/stdout is not a TTY (tests + `just *-replay`
      keep working). Availability wired: connect/help/quit/dump-on always;
      dump-off when dumping; login after connect; list/create/enter after login;
      chat in-sector. `SessionContext.Connected` flag added.
- [x] In-sector world model (`Repl/SectorWorld.cs`) fed by the always-on drain:
      ingests 0x0004 create, 0x0007 remove, 0x0008/0x0040/0x003E positions,
      0x0061 avatar names, 0x2018 static/nav create (name+pos+signature), 0x0099
      navigation (navType+visited). `enter`'s arrival summary and a now
      context-aware `list` (in-sector → nearby; else → cached characters) print
      per-object kind (English), name, distance, and own position to 4 d.p.
      Level is reported as unknown (`-`): it is not carried in any object frame
      (arrives via RPGInfo aux) — not guessed.
      Tests: +18 unit tests (CompletionTests, SectorWorldTests, LineEditorTests,
      ChatCommandTests, ClientChatEventExtractTests, ReplTests dedup); suite
      216→234, all green.
- [x] Follow-up: mob/ship level + object names now decoded from 0x001B aux.
      `SectorWorld` ingests 0x003F PLANET_POSITIONAL_UPDATE (planets announce
      position there, not via 0x0040) and 0x001B AUX_DATA. The aux is decoded
      by reusing the catalog's schema walker via a new
      `AuxDataRecord.TryExtractSummary` (returns GameId + depth-0 Name +
      CombatLevel) -- no hand-rolled offsets. `Tracked.Level` added; render
      shows real level when announced (else `-`), plus a `you:` own-ship line
      (name + level + position). Files: `Opcodes/Records/AuxDataRecord.cs`,
      `Repl/SectorWorld.cs`. Tests: +4 (planet 0x003F distance, ShipIndex aux
      name+CombatLevel=42 decode, gid=0 PlayerIndex skip, own-ship render);
      suite 234->238.

## Live verification (2026-06-01)

- [x] Drove the CLI against the running docker stack (server/proxy/login/pg)
      via piped stdin (non-TTY fallback). Seeded a Postgres account
      (`clitest*`, argon2 PHC for "testpw") mirroring `TestAccounts.New`, then
      `connect -> login -> create JE <name> -> enter -> list -> chat`. Confirmed:
      auth+global handshake, character create, sector LOGIN (gameId allocated,
      30 handshake frames), outbound chat echo (`--> [sector] you: ...`) without
      `dump-on`, and the in-sector `list`/arrival render.
- [x] The live drive surfaced (and fixed) two real gaps in the world model: the
      home planet "Io" was rendering with no name and `d=?`. Root cause from the
      dump: its name arrives via a 0x001B Harvestable aux and its position via
      0x003F -- neither was ingested. After the fix the drive renders
      `planet  lvl -  Io  d=140538.8  visited`.
- [!] A docked fresh char sees only its home planet (1 object) -- navs/mobs are
      exploration-gated and proximity-exposed, so they don't appear until the
      avatar moves. The CLI cannot yet move (no MVAS UDP), so a live
      navs-populate test still needs CLI movement.
- [!] Caveats: piped stdin exercises the ReadLine fallback, NOT the interactive
      editor's key handling (Tab/ghost) -- that path needs separate coverage.
      `quit` drops the socket without a clean logout, so the server holds the
      avatar `ACCOUNT_IN_USE` (G_ERROR 13) for a while -- use a fresh account
      per drive.

## Continuation (2026-06-01 cont.): client-fidelity world model + CLI movement

- [~] "Act like a real client" -- guid-keyed live model of objects + own ship,
      updated from packets, listable, reset on sector transition. The model is
      guid-keyed and reset on `enter`; own-ship (name/level/pos) now rendered.
      Reset-on-station-transition is structurally ready (`World.Reset()` is the
      single chokepoint) but the REPL is still single-enter-per-session, so
      to/from-station resets land when multi-sector enter is added.
- [ ] Mob level via a dedicated AuxMobIndex schema: deferred. No mob 0x001B was
      observable in a docked home sector and there's no pinned mob-aux capture
      to validate against, so a hand-ported MobIndex schema would be unverified
      and could perturb the (well-pinned) dump candidate selection. Other
      players' ships already decode level via the existing ShipIndex schema.
### CLI movement: full UDP client (2026-06-01, continued)

The "blocker" below was wrong -- the user clarified the CLI owns the avatar
exclusively, so the server rerouting the sector stream to the CLI's socket is
exactly what a full client wants. Built it as a real UDP client and, along the
way, found and fixed a real server bug.

- [x] SERVER BUG FIX (server/src/ServerManager.cpp): the MVAS receiver thread
      was never started. MVASauth (MVAS_LOGIN_PORT/3806, constructed in
      Net7.cpp:442) binds its socket but -- unlike the global (line 186), master
      (289), and sector (SectorManager.cpp:172) listeners -- nothing called
      StartReceiver() on it. Every inbound MVAS datagram (0x1004 position, 0x1000
      register) was silently dropped, so NO player's position ever updated from
      the move-assist feed and proximity nav exposure could never fire -- for
      the proxy OR the CLI. Added `m_UDPConnection->StartReceiver()` after the
      master plane starts. Fidelity fix (HandleMVASPosReturn + the proxy's emit
      to MVAS_LOGIN_PORT prove 3806 is the intended receive path), not a
      weakening. Verified: after the fix the server logs "Received MVAS login"
      and "MVAS synched and locked in for <char>".
- [x] docker-compose.yml: publish 3806/udp so the host CLI can reach the MVAS
      port (was proxy<->server internal only).
- [x] Net/MvasClient.cs + Net/SectorUdpClient.cs: full-duplex MVAS/sector UDP
      client. MvasClient builds byte-exact 0x1004 datagrams (unit-tested).
      SectorUdpClient binds an UNCONNECTED socket (the server reply NATs back
      from a different source -- a connect() drops it, exactly the trap
      proxy/UDPClient.h:30 warns about), sends position, and parses the
      downstream: EnbUdpHeader-wrapped 0x2016/0x201A batches of inner
      EnbTcpHeader frames (plaintext -- the proxy adds RC4 only for client TCP),
      feeding each inner opcode to the same world-model/echo hooks.
- [x] Repl/Commands/MoveCommand.cs: `move <x> <y> <z> [send]`. Realistic flight
      -- orient toward the target, step at the ship's MaxSpeed (read from the
      0x001B ship aux), send position+heading at the server's rate, stop within
      an arrival delta. Engages forward thrust (0x0014 MOVE type 2 over the
      sector channel) first, because CheckNavs only sweeps while the avatar has
      non-zero speed (Player::CalcVelocity gates on the throttle, not the
      position feed); kills thrust (type 4) on arrival.
- [x] VERIFIED end-to-end transport: the server accepts our MVAS ("MVAS synched
      and locked in"), the return path works (we receive 0x1007 freq + 0x2016
      sector batches), and SectorUdpClient correctly decodes the inner frames
      (observed 0x00A5 ClientChatEvent + 0x001B AuxData parsed out of a live
      0x2016).
- [!] NOT yet visually populating navs, two remaining reasons, both inherent to
      the CLI's hybrid session (established via the proxy, MVAS via its own
      socket):
      1. IP split (server/src/UDP_Client.cpp:72,96): the sector handler only
         processes a client opcode when source_addr == player->PlayerIPAddr().
         Our MVAS sets PlayerIPAddr to the CLI's IP (docker gateway 172.19.0.1),
         but the session/MOVE comes via the proxy (172.19.0.6) -> the server's
         anti-spoof check logs "Player IP mismatch" and drops the cross-IP
         opcode. (The initial throttle still lands because it precedes the first
         MVAS; later sector opcodes do not.)
      2. docker's userland UDP proxy does not reliably relay server-INITIATED
         pushes to the NAT'd client (only ~2-3 datagrams arrive per flight),
         independent of our code.
- [ ] TRUE completion = the CLI establishes its ENTIRE session over UDP from its
      own socket/IP (global 3810 + master 3808 + sector handoff + the 0x2016
      reliability layer), replacing the proxy. Then one IP owns everything, the
      anti-spoof check passes, and (run in-network, or with reliable NAT) the
      sector stream flows. That is a sizeable subsystem (a C# port of the
      proxy's UDPClient login/handoff sequence) -- scoped as the next step.

#### superseded earlier note (kept for history)

- [~] CLI movement (0x1004 MVAS_SEND_POSITION over UDP): wire emitter built and
      verified, but live driving is blocked by an architectural constraint
      (documented below).
      - `Net/MvasClient.cs`: byte-exact EnbUdpHeader emitter (12B header
        `{short size; short opcode; int32 player_id; int32 seq}` + pos[3]
        (+heading[3])), plaintext, mirroring proxy/UDPClient_linux.cpp
        SendResponse + server/src/UDPConnection.cpp SendOpcode. Unit-tested
        byte-for-byte (suite 244->246); dry-run verified live against a real
        session (datagram for player 0x40000033 / pos 50000,-1000,0 is exact).
      - `Repl/Commands/MoveCommand.cs`: `move <x> <y> <z> [send]`. DRY-RUN by
        default (prints the datagram + the reason it isn't sent); transmits
        only on an explicit `send`.
      - BLOCKER (server-code evidence, not a guess): the server routes a
        player's downstream sector data -- including the nav-exposure frames
        movement is meant to trigger -- to a single `m_Player_IPAddr`/
        `m_Player_Port` (server/src/PlayerConnection.cpp:246), which
        `SetPlayerPortIP` (server/src/UDP_MVAS.cpp:149) (re)sets to the source
        of every inbound MVAS datagram. In the live stack that source is the
        proxy, so data returns through the proxy to our TCP channel. If the CLI
        sends MVAS from its own socket the server redirects this player's whole
        sector stream to us over UDP and the TCP feed (where the world model
        reads navs) goes dark. Also: MVAS port 3806 is server-bound but not
        host-published in docker-compose, and CheckNavs only runs while the
        avatar is actually moving (PlayerClass.cpp:1780-1790).
      - CONCLUSION: faithful CLI movement requires the CLI to become a full UDP
        client (own the UDP receive path + the 0x2016 PACKET_SEQUENCE
        reliability layer), effectively replacing the proxy. That is a separate
        subsystem, out of scope for the passive observer. The verified emitter
        is the wire primitive that work would build on.

## Movement root cause: InSpace() / undock (2026-06-02)

Drove the CLI as a full UDP client and chased "navs don't populate" all the way
down. The earlier IP-mismatch / slirp4netns theories were RULED OUT by running
the CLI inside the docker network; the true blocker is server-side dock state.

- [x] Ran the CLI INSIDE the docker network (self-contained publish in an
      ubuntu:24.04 container on `enb-emulator_default`, invariant globalization)
      to take rootless-docker slirp4netns out of the picture. Added host
      overrides so the CLI can address the split dev stack: `N7_AUTH_HOST`
      (login container :443), connect-host (proxy container), `N7_MVAS_HOST`
      (server container :3806). Files: `SessionContext` (EffectiveAuthHost /
      EffectiveMvasHost), `ConnectCommand`, `LoginCommand`, `Program.cs`.
- [!] In-network gave the SAME result as host (server accepts MVAS -- "MVAS
      synched and locked in" -- but only ~2 datagrams back, no nav frames). So
      the sparse reverse-push is NOT (for the CLI) the slirp4netns pitfall the
      launcher config documents -- that pitfall is real but it's about the
      PROXY's login-ack path, not our nav stream. The IP mismatch also isn't the
      nav blocker: the throttle (0x0014 MOVE) is sent before the first MVAS, so
      it matches the proxy session IP and lands; the only "Player IP mismatch"
      logged is the final type-4 stop (harmless).
- [x] TRUE ROOT CAUSE: the server only runs the movement/nav loop
      (`Player::CalcNewPosition` -> `Player::CheckNavs`) for players that are
      `InSpace()` -- `PlayerManager::RunPlayerUpdate` gates on it
      (server/src/PlayerManager.cpp:476). A freshly created character is DOCKED,
      so the loop never runs and no amount of MVAS/throttle exposes navs.
- [x] Undock is NOT a one-shot opcode. `0x004E STARBASE_REQUEST` Action=1 ->
      `SectorManager::LaunchIntoSpace` (server/src/PlayerConnection.cpp:9897),
      which calls `SendServerHandoff(...)` -- i.e. a sector RE-LOGIN. `InSpace`
      is set only in `Player::FinishLogin` (PlayerClass.cpp:3878) after that
      handshake completes. Sending the bare opcode just gates the avatar out and
      the server drops it ("Removed from sector"). Removed the harmful
      auto-undock from `move`; the helper would need to drive the full launch
      handoff.
- [ ] NEXT STEP for live nav exposure: a `launch`/undock flow in the CLI that
      sends 0x004E Action=1, consumes the resulting ServerHandoff, re-runs the
      sector login handshake (reuse `SectorEnterDriver`), and lands the avatar
      InSpace(). Then the existing `move` (throttle + MVAS) drives
      CheckNavs and navs/objects fan in. Everything up to that point is built
      and verified; this is the remaining piece.

## The "move proxy to WINE" question -- answered (2026-06-02)

- The Net7Proxy is documented to run on the CLIENT host in real deploys
  (docs/03-network-protocol.md:192). The docker-compose proxy container is a
  dev-only arrangement. So the proxy-vs-CLI IP split is a dev artifact, not a
  real-deploy problem (a real client+local-proxy is one IP for session+MVAS).
- Moving the proxy back to WINE/host is the WRONG move: the launcher config
  (tools/launchnet7-avalonia/LaunchNet7.cfg:42-49) documents that a host-side
  proxy reintroduces the rootless-docker slirp4netns UDP conntrack failure on
  the MVASauth:3806 reverse-push -> stage-3 login-ack timeout, which is exactly
  why the proxy was moved INTO docker. And it wouldn't fly a real client anyway:
  `engine_read_process` (the position scrape) is stubbed `return false`
  unconditionally (proxy/Net7.cpp:288) -- the scrape is gone from BOTH the Linux
  and Windows builds of this fork. So WINE buys nothing here and breaks login.
- Net: don't move the proxy. The CLI's path to flying is the launch/undock
  handshake above (server-side dock state), which is unrelated to where the
  proxy runs.

## REPL UX polish: Tab arg-fill + state-aware flow + full-line colour (2026-06-01)

Live-play feedback. All client-side; NO server change.

- [x] Tab fills argument values, not just command names. Past the command word,
      Tab completes the suggestion embedded in the placeholder:
      `<name:default>` -> `default` (e.g. connect's `<ip:127.0.0.1>` -> `127.0.0.1`)
      and `[a|b|c]` -> the first option matching what's typed. A partially-typed
      arg must prefix the suggestion, so Tab never clobbers a value in progress.
      Pure logic in `Completion.CompleteArgument`/`SuggestArg`; `LineEditor`'s
      `HandleTab` routes to it when `PastFirstToken`. Files: `Repl/Completion.cs`,
      `Repl/LineEditor.cs`.
- [x] Flow-ordered suggestions via `ICommandHandler.Priority` (default 0;
      `CommandSpec` 4th field). `AvailableNames` sorts `Priority` desc then name.
      The expected next step leads: connect at startup (Priority 100, retires
      once `Connected`), login after connect (100, while `Global is null`),
      create+enter after login (both 100 -> `create` leads by the alpha tie),
      move/chat in-sector. So the first grey ghost command is always the obvious
      next action. Files: `Repl/ICommandHandler.cs`, `Repl/Completion.cs`,
      `Repl/Commands/{Connect,Login,Create,Enter,Move}Command.cs`, `Program.cs`.
- [x] Colour across the whole REPL, not just the packet dump. Added semantic
      helpers to `AnsiPalette` (Head/Ok/Err/Warn/Info/Muted/Accent/Value), each a
      no-op when colour is off. Every command's status/error/summary output now
      uses them (connect/login/create/enter/list/move/chat/dump/dump-on/dump-off/
      replay), plus the REPL built-ins (prompt, help, unknown-command, errors) and
      the `SectorWorld` render. The annotated hex `dump` view keeps its own
      per-byte palette untouched (it was already good). Colour auto-disables when
      stdout is redirected or `NO_COLOR` is set, so piped/non-TTY output (tests,
      `just *-replay`) stays plain -- this is why none of the colour additions
      needed test changes. State-aware prompt: a `Func<string>` prompt factory
      shows `offline` -> `connected` -> `user` -> `user@sector`. Files:
      `Logging/AnsiPalette.cs`, `Repl/Repl.cs` (Func prompt), `Repl/SessionContext.cs`
      (`PromptLabel`), `Repl/SectorWorld.cs`, all `Repl/Commands/*`, `Program.cs`.
- [x] Tests: +15 (CompletionTests: CompleteArgument default/prefix/non-prefix/
      no-default/bracket-options/before-word/past-slot/unavailable + AvailableNames
      priority; LineEditorTests: interactive Tab fills arg default, completes typed
      prefix, inert with no default). Suite 246->261, all green. Colour confirmed
      transparent under `dotnet test` (redirected stdout -> `AnsiPalette.Enabled`
      false).

## Multi-client: containerise the CLI, one proxy per client (2026-06-01)

Problem reported in live play: the CLI client and `client.exe` can't run at
the same time -- "one disconnects the other / steals its ports" -- and there
was no way to spawn several CLI clients at once.

Root cause (NOT a bug to fix in the proxy -- it's the proxy's design): the
**Net7Proxy is a single-client bridge**. It holds one global `g_ServerMgr`
(one `ServerManager`) with one upstream UDP triple
(`m_UDPConnection`/`m_UDPClient`/`m_UDPGlobalClient`) and singular
`m_{Sector,Global,Master}Connection` pointers (`proxy/ServerManager.h:50-76`),
gated by one global `g_LoggedIn` (`proxy/Net7.cpp:45`). A second client
through the same proxy clobbers those pointers -- exactly the observed
symptom. And the server's control plane is **UDP-only** (server publishes only
`*/udp`), so a TCP-speaking client *cannot* skip the proxy. Conclusion: the
proxy is architecturally one-per-client; the fix is to give each client its
own proxy, not to make the proxy multi-client (that would be a server-adjacent
rewrite with no preservation value).

Secondary constraint, intentionally preserved: the server force-kicks a
duplicate login **per account** (`PlayerManager::CheckAccountInUse`,
login-server `ConnectionManager::CheckAccountInUse`). This is correct retail
behaviour and is NOT bypassed -- each concurrent client needs a distinct
account.

Solution (containerise the CLI; one proxy + one CLI per "unit"):

- [x] `tools/cli-client/Dockerfile` -- multi-stage SDK->runtime publish of the
      `enb-cli` app. Invariant globalisation; ENTRYPOINT `enb-cli`, CMD `repl`.
      Build context is the repo root (matches proxy/server). Image builds clean.
- [x] `docker-compose.cli.yml` -- a CLI+proxy unit. The proxy joins the shared
      stack network (`stack`, external, `${STACK_NETWORK:-enb-emulator_default}`)
      to resolve `server`, plus a private `unit` net where it answers to the
      alias `cliproxy`. The CLI joins both nets: `unit` to dial its own proxy by
      the `cliproxy` alias (no cross-unit DNS collision even with many units up),
      `stack` to reach `login:443` (auth TLS) and `server:3806` (MVAS UDP)
      directly. Nothing host-published, so every unit reuses the default proxy
      ports 3801/3805/3500 in its own namespace. `stdin_open`+`tty` so the line
      editor + colour turn on.
- [x] CLI env wiring: `Program.cs` reads `N7_AUTH_HOST`/`N7_MVAS_HOST` (already)
      and now `N7_AUTH_PORT` (new) -- so in-network the CLI reaches login on 443
      rather than the 4443 host remap, without spelling the port out on every
      `connect`. Compose sets `N7_AUTH_HOST=login`, `N7_AUTH_PORT=443`,
      `N7_MVAS_HOST=server`.
- [x] `just play-cli UNIT='cli1'` -- ensures the shared stack is up
      (`run-stack-bg`), passes `STACK_NETWORK=${COMPOSE_PROJECT_NAME}_default`
      (so it works on any worktree, not just `main`), then
      `docker compose -f docker-compose.cli.yml -p <UNIT> run --rm --build cli`
      interactively. Several at once: `just play-cli cli1`, `just play-cli cli2`,
      .... `just stop-cli UNIT='cli1'` tears a unit's dedicated proxy down.
      (Distinct from the pre-existing host-local `launch-cli`, which dials
      127.0.0.1 and so conflicts with `client.exe` -- that one is the single
      host-side client path; `play-cli` is the containerised multi-client path.)
- [x] Verified end-to-end wiring: a `clismoke` unit's in-container CLI resolved
      its dedicated `cliproxy` (private net), set master/global/sector targets to
      it, AND reached the shared `login:443` ("probe: login:443 accepting TCP"),
      reaching `connected >` and exiting clean. SectorEnterDriver dials
      `ctx.Host` (not the redirect-advertised IP), so the in-container sector
      reconnect has no 127.0.0.1 trap. No server/proxy/login change -- this is
      pure packaging + an env knob on the CLI tool.
- [x] Live-play follow-up (the `connect 127.0.0.1` trap). In the container the
      proxy is `cliproxy`, not loopback, but the connect default WAS `127.0.0.1`
      (placeholder + Tab autofill), so a user typing `connect 127.0.0.1` pointed
      global/master/sector at the CLI container's own loopback -> `global connect
      failed: Connection refused` (auth still worked, since it uses
      `EffectiveAuthHost=login`). Fix: `N7_PROXY_HOST` env seeds
      `SessionContext.Host` (compose sets it to `cliproxy`); `connect` now takes
      ZERO args (probes the current default host) and its placeholder is dynamic
      (`<host:{Host}>`) so Tab/right-arrow fill the host that actually works
      (cliproxy in-container, 127.0.0.1 on the host stack). Also fixed a cosmetic
      bug: `LoginCommand` logged the auth GET against `_ctx.Host` while really
      dialing `EffectiveAuthHost` -- now logs the host it actually hits. Verified:
      bare `connect` in a unit prints `global=cliproxy:3805 master=cliproxy:3801
      sector=cliproxy:3500`; all three proxy ports answer TCP via the `cliproxy`
      alias from inside the unit net. Files: `Program.cs`, `ConnectCommand.cs`,
      `LoginCommand.cs`, `docker-compose.cli.yml`, README.

## REPL UX: right-arrow accepts the suggestion (2026-06-01)

Live-play request: "right arrow key should pick the option we are tabbing to".

- [x] In the Tab menu, Right-arrow now picks the highlighted candidate (commits
      the command word + arg-space, leaves the menu), identical to Enter-in-menu.
      Extended fish-style: at end of line with no menu open, Right-arrow accepts
      the inline grey suggestion too -- the argument placeholder default, or the
      rest of a uniquely-prefixed command word -- whatever Tab would fill.
      Mid-line it stays plain cursor motion; on a blank line it is inert (the
      ghost there is the whole command list, not a single suggestion). Menu hint
      updated to `Tab/Shift-Tab cycle, ->/Enter pick`. Files: `Repl/LineEditor.cs`
      (`RightArrow` case + `TryAcceptInlineGhost`). Tests: +5 in `LineEditorTests`
      (menu pick fwd/after-cycle, arg-ghost accept, command-word complete,
      mid-line cursor-move regression). Suite 261->266, all green.

## REPL UX: legible create-character failures (2026-06-01)

Live-play report: `create TW c1` -> `create failed: server returned GlobalError
code=3; expected opcode 0x0070`. NOT a server bug -- the server faithfully
rejects the 2-char name "c1" with G_ERROR_TOO_SHORT=3
(`AccountManager::CreateCharacter`, `name_len < 3`). Per the integrity rules the
server stays as-is; the only thing wrong was that the CLI surfaced the raw enum
number instead of a reason.

- [x] `SectorEnterDriver.GlobalErrorMessage(int)` decodes the G_ERROR_* enum
      (server/src/UDP_Global.cpp, codes 0-12) to a readable reason; the
      GlobalError throw now reads `server rejected the request: <reason>
      (GlobalError code=N)`. So `create TW c1` now says `name too short (minimum
      3 characters)`. Made `internal static` and pinned by
      `GlobalErrorMessageTests` (13 known codes + unknown fallback) so it can't
      drift from the server enum.
- [x] `CreateCommand` mirrors the server's hard length bound (`firstName.Length
      < 3 || > 19`) so an obvious miss fails instantly client-side instead of
      after a global round-trip; vowel / repeating-char / forbidden-name /
      uniqueness rules stay server-authoritative (surfaced via the decoded text).
      Files: `Repl/SectorEnterDriver.cs`, `Repl/Commands/CreateCommand.cs`.
      Tests: +`GlobalErrorMessageTests`. Suite 266->281, all green.

## In-space login: CLI now sends 0x0006 START_ACK (cross-visibility fix, 2026-06-01)

Live-play report: two CLI clients (and the real client) in the same sector saw
each other's *chat* but `list` showed `0 avatars` and no mobs -- "can't see
other players". Built a scripted 2-client harness
(`tools/cli-client/test-two-client-chat.sh`: spawns cli1=c1/chara + cli2=c2/charb
in sector 1015, each connect/login/enter/chat/list) to repro deterministically.

Root cause (NOT a server bug -- server was faithful): the CLI's sector handshake
drained to 0x0005 START and returned **without replying 0x0006 START_ACK**. The
real client always acks START; the proxy turns that ack into 0x3004
PLAYER_SHIP_SENT (`proxy/ClientToServer_linux_stubs.cpp` ENB_OPCODE_0006), which
drives server `FinishLogin` -> `SetInSpace(true)`. With no START_ACK the avatar
stays `!InSpace`, so `RunPlayerUpdate` skips the in-space block and
`UpdatePlayerVisibilityList` early-returns -- the avatar is in nobody's range
list (no players, no mobs visible, and invisible to others). Sector chat still
worked because `BroadcastChat` keys off the sector player-list (set at
HandleSectorLogin3 "fully logged in"), not the range list. The proxy already
auto-acks the 0x2020/0x2021 login *stages*, so login reached "fully logged in"
without the CLI; only START_ACK was missing. See memory
`project_inspace-startack-visibility`.

- [x] `SectorEnterDriver.DoSectorLoginUntilStartAsync` now sends 0x0006
      START_ACK on seeing 0x0005 START, via new pure `BuildStartAckPacket(int)`
      / `SendStartAckAsync`. START_ACK echoes the start id as 4-byte LE (matches
      `PlayerConnection::SendStart` int32 wire shape).
- [x] Verified end-to-end on the live stack: both clients' `list` now report
      `(1 avatars, 2 navs)` showing the OTHER char by name at d=0.0, AND the mob
      spawns (Needlenose lvl 5) that were previously invisible -- the whole
      in-space awareness loop was gated on InSpace. Harness asserts chat both
      ways + `>=1 avatars` both ways; all 4 PASS.
- [x] Byte-pinned the frame in `StartAckPacketTests` (opcode 0x0006, 4-byte LE
      start id, full wire `08 00 06 00 <id LE>`, round-trip preservation).
      Files: `Repl/SectorEnterDriver.cs`, `test-two-client-chat.sh`,
      `tests/.../StartAckPacketTests.cs`. Suite 281->288, all green.

## Async chat no longer clobbers the prompt line (2026-06-01)

The chat echo fires from the background sector-drain thread
(`SessionContext.OnPacketReceived` -> `EchoChat` -> `WriteChatLine`). The
interactive `LineEditor` draws prompt+buffer+grey-ghost on one line and parks
the cursor there, then blocks polling for a key. A chat frame arriving in that
window was written straight to the console, so it fused onto / appeared to
prepend the line being typed ("the chat msg overwrites my prompt completion").

- [x] New `Repl/LivePrompt.cs`: a thread-safe coordinator shared by the editor
      and the session. The editor publishes (on every render) the exact escape
      sequence that redraws its current prompt line; `TryWriteLineAbove(text)`
      -- under the same lock the editor renders under -- erases the prompt line,
      prints the message on its own line, then replays the redraw so the prompt
      reappears untouched beneath. Returns false when no interactive prompt is
      active (piped output / between lines / mid-command) so the caller writes
      plainly. Non-TTY path is unchanged (never Activated).
- [x] `LineEditor` Activates/Deactivates the coordinator per line and folds the
      cursor-park into the single composed render string it emits through it.
      `SessionContext.WriteChatLine` routes through `LivePrompt.TryWriteLineAbove`
      first, plain `ChatOutput.WriteLine` fallback, all under `_chatGate`.
      `Program.cs` builds one `LivePrompt`, hands it to the editor and the
      session.
- [x] Tests `LivePromptTests` (7): inactive->false, active-but-unrendered->false,
      erase+print+redraw byte-pin, message-on-own-line (not fused), deactivate->
      false, reactivate-without-render->no stale replay, and an editor
      integration test that injects an async write in the real blocked-on-key
      window and asserts it lands above the rendered prompt. Suite 288->295.
- [x] Live 2-client harness re-run on the rebuilt CLI image: chat both ways +
      `>=1 avatars` both ways, all 4 PASS, exit 0 (non-TTY path unaffected).
      Files: `Repl/LivePrompt.cs`, `Repl/LineEditor.cs`, `Repl/SessionContext.cs`,
      `CliClient.App/Program.cs`, `tests/.../LivePromptTests.cs`.

### Downstream sector-stream reassembly extracted + byte-pinned (2026-06-01)

- [x] The 0x2016/0x201A continuation-stream reassembly was inline + private in
      `SectorUdpClient` (commit 57e0a617) with NO unit coverage -- only the
      outbound `MvasClient.BuildDatagram` was tested. Per the "strengthen shallow
      tests" standing rule, extracted the pure wire logic into
      `Net/SectorStreamReassembler.cs` (`Push(datagram) -> IReadOnlyList<Packet>`,
      `Frequency`, `Aligned`). `SectorUdpClient.ProcessDatagram` is now a thin
      socket-loop adapter that forwards each emitted frame to `_onInbound`
      (consumer try/catch kept; `ForOpcode` proven non-throwing under the
      `size<=65535` cap so behaviour is byte-identical).
- [x] `SectorStreamReassemblerTests` (20): single/multi/empty-body frame emit,
      split-frame across 0x2016->0x201A, continuation-before-alignment dropped,
      unknown UDP opcode mid-split doesn't corrupt, 0x1007 frequency set+clamp
      ([1,60] incl. 0->1 / 61->60 / -5->1), short-payload-leaves-default,
      sub-12-byte datagram ignored, desync on bogus inner `size` (clears +
      de-aligns + logs), desync still emits the good frame that preceded it,
      desync->continuation-dropped->fresh-0x2016-realigns, and a 65535-byte
      max-size frame that must NOT trip the desync guard. Suite 295->315.
- [x] `AuxDataRecord.TryExtractSummary` (the world-model name/level/MaxSpeed
      extractor, ZERO coverage despite the 57e0a617 MaxSpeed addition) byte-pinned
      in `Opcodes/AuxDataRecordSummaryTests.cs` (9): ShipIndex Name+CombatLevel+
      MaxSpeed extract, F32 round-trip through the "0.0##" formatter (incl. a
      byte-exact 1500.0f LE pin 00 80 BB 44), no-MaxSpeed-flag-leaves-null
      regression guard, name-only, truncated->null, wrong-version->null. Plus
      `SectorWorldTests` SelfSpeed coverage (2): a MaxSpeed aux surfaces through
      `SelfSpeed` (drives the flight step size) and an unknown/no-aux object
      returns null. Extended the existing `ShipAux` helper non-breakingly with an
      optional flag-13 MaxSpeed field. Suite 315->326.

### Retail-capture decode validation: records cross-checked vs capture_3 (2026-06-01)

- [x] Ran the `PacketRecord` decode path (the REPL `dump` view) over real frames
      pulled from `archive/kyp-snapshot/capturedPackets/capture_3.rar` -- the same
      "feed retail bytes in, see if they make sense" method that got the item
      codec right. Wrote a throwaway harness against `CliClient.Core` to extract +
      decode every frame of the high-value opcodes. Findings (all decode to
      sensible content): ItemBase 0x25 fully decodes real ore/device items
      ("Sand"->"Refines to: Silicon", "Chemical Resistance Item C" with the live
      ActEffect tooltip); Aux 0x1B ShipIndex yields real mob names+CombatLevel
      ("Starbase Guardian Turret" lvl 66, "Shinwa Patrol Cruiser" lvl 25); Aux
      Harvestable yields nav/resource names ("Sector Gate to Jupiter", "Halon");
      GalaxyMap 0x97 -> "Sol"/"Io"/"Nishino Research Facility"; MessageString 0x1D,
      Relationship 0x89, AvatarDescription 0x61 ("Ace" + full appearance) all clean.
- [x] Pinned 10 COMPLETE retail frames as fixtures (`Captures/fixtures/
      capture3-records.txt`) + content tests (`Captures/RetailRecordDecodeTests.cs`,
      11): each frame verified complete (dump "Length = N" == payload+4, and Aux
      BodyLen == payload-6) so a future failure is the decoder, never a truncated
      fixture. Asserts decoded VALUES (quoted strings, not the raw ASCII gutter):
      two ItemBase ores incl. Name/Description/MaxStack/Flags/TechLevel/Cost, two
      ShipIndex (summary + dump agree on Name+CombatLevel+HullPoints), two
      Harvestable, GalaxyMap system/sector/station, MessageString docking banner
      (color 5), Relationship ObjectID/Reaction/IsAttacking, AvatarDescription
      Name/Race/Profession/appearance. Suite 326->337.
- [!] **Aux 0x1B schema-catalog gap (real, not noise).** Of 5063 COMPLETE Aux
      frames in capture_3 (108 more are dump-truncated and excluded), only 2450
      (48.4%) decode via an EXACT schema-walk. Breakdown of the rest: 1325 match
      NO schematised candidate (fall to the AddString scanner -- likely husk/loot/
      effect Aux classes we have not ported from server/src/AuxClasses/*); ~1270
      START matching but diverge mid-walk (464 Harvestable, 451 ShipIndex-extended,
      355 ShipIndex). The names/levels we DO extract are correct; the gap is
      breadth of schema coverage, not correctness of the existing four schemas.
      FOLLOW-UP: port the missing AuxClasses BuildPacket layouts into `AuxSchemas`
      and audit the Harvestable/ShipIndex field lists against captures (each new
      schema validated by an exact-consumption count rising on the real corpus).
- [x] **Colorization 0x11 decoder fixed (real 64-byte decode gap).** The record
      read `ItemCount` flat 16-byte `{metal, HSV[3]}` blocks. On a real retail
      frame that silently dropped 64 bytes: every Colorization frame across all
      three captures (capture_1/2/3, 90/90) is identical -- GameID, ItemCount=4,
      a 134-byte payload == 8 colour blocks == 4 primary/secondary SLOTS. So the
      counted unit retail uses is the 32-byte slot, and 6 + 4*16 left half the
      body (the Wing+Engine pairs) undecoded. Decoder now derives the block count
      from `(payload-6)/16` and pairs blocks into slots, so it does NOT trust
      ItemCount's unit. Pinned the verbatim 134-byte frame as `colorization_default`
      + 2 facts (retail count=4 and a flipped count=8 both decode the same four
      slots). Suite 337->339. Commits 0e1f02a1, 48ac7146.
- [!] **Server Colorization ItemCount divergence (do NOT flip blindly).**
      `server/src/PlayerClass.cpp` SendShipColorization writes `ItemCount=8` (it
      counts flat blocks) and sizes the packet `&item[count]` == 6+count*16. For
      the standard 8-block body that yields ItemCount=8 where retail's own server
      wrote 4 (90/90 frames). On its face that's a fidelity gap, BUT it is a
      MATCHED-PAIR divergence: the live Net-7-modded client evidently reads count
      as flat blocks too (else ItemCount=8 would make a slot-reading client over-
      read 8*32=256 bytes past a 134-byte packet and crash, which it demonstrably
      does not). "Fixing" the server toward retail's count=4 would (a) require
      decoupling the count field from the block count -- the size calc currently
      ties them -- and (b) regress the Net-7 client we actually run under
      `just play-local` (a flat-block reader would then apply only Hull+Profession
      and drop Wing+Engine colours). Per CLAUDE.md server-integrity rules this
      change needs the CLIENT-side parse confirmed (decomp or a live A/B with a
      retail-faithful client), NOT just the server capture. Primary source for the
      retail behaviour: capture_1/2/3.rar, 90 Colorization frames, all ItemCount=4
      / 134 bytes. LEFT AS-IS; logged for a future client-parse-backed decision.
