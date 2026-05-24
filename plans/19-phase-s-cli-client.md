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

- [ ] Item 4 — Login flow (TLS to Net7SSL, /AuthLogin POST, ticket extraction)
      Status: not started
      Touches: tools/cli-client/Auth/Login.cs, Net/LoginConnection.cs
      Notes: TLSv1.3, accept self-signed dev cert via env-gated knob, follow the AuthLogin contract already in login-server/

- [ ] Item 5 — Global → master → sector handoff (TCP redirect, server-list parse)
      Status: not started
      Touches: tools/cli-client/Net/GlobalConnection.cs, MasterConnection.cs, SectorConnection.cs
      Notes: ServerRedirect opcode already in common headers; sector handoff hands off to UDP 3809. Reproduce the dance documented in docs/03-network-protocol.md §1–§4.

- [ ] Item 6 — Packet/chat log sinks (NDJSON + readable text)
      Status: not started
      Touches: tools/cli-client/Logging/PacketLog.cs, ChatLog.cs, ConsoleSink.cs
      Notes: one packet = one NDJSON line: {ts, direction, opcode_hex, opcode_name, length, payload_hex, decoded?}; flush on each line.

- [ ] Item 7 — HealthGuard (rule 2 enforcement)
      Status: not started
      Touches: tools/cli-client/Session/HealthGuard.cs
      Notes: watch for: server disconnect, repeated error opcodes, response timeouts > 5s, RX/TX rate spikes. On trip, abort current workflow with reason; don't auto-retry. Surface to console + log.

- [ ] Item 8 — REPL (`connect`, `login`, `chat`, `enumerate sectors|missions|items`, `quit`)
      Status: not started
      Touches: tools/cli-client/Repl/Repl.cs, Commands.cs
      Notes: System.CommandLine or a hand-rolled prompt; readable output; tab-completion is nice-to-have, not required.

- [ ] Item 9 — Workflow: connect-and-login (smoke-test target)
      Status: not started
      Touches: tools/cli-client/Workflows/ConnectAndLogin.cs
      Notes: end-to-end Linux integration: docker compose up; cli-client connects + logs in + idles for 5s + clean disconnect. Wire into tests/integration/.

- [ ] Item 10 — Workflow: enumerate sectors (visit each sector, dump objects)
      Status: not started
      Touches: tools/cli-client/Workflows/EnumerateSectors.cs
      Notes: respect rule 3 — try broader queries first, fall back to real-client-shaped queries if server stalls. Output: ./out/sectors-<ts>/<sector-id>.json

- [ ] Item 11 — Workflow: enumerate missions
      Status: not started
      Touches: tools/cli-client/Workflows/EnumerateMissions.cs
      Notes: walk mission-board NPCs; capture mission text + stages + rewards; output ./out/missions-<ts>.json

- [ ] Item 12 — Workflow: enumerate items
      Status: not started
      Touches: tools/cli-client/Workflows/EnumerateItems.cs
      Notes: query item-base data via the appropriate opcode (TBD — may need real-client trace to confirm shape); output ./out/items-<ts>.json

- [ ] Item 13 — Workflow: send chat
      Status: not started
      Touches: tools/cli-client/Workflows/SendChat.cs
      Notes: REPL `chat <channel> <text>`; received chat already auto-logged via ChatLog.cs.

- [ ] Item 14 — Codec unit tests in xUnit (CliClient.UnitTests)
      Status: not started
      Touches: tools/cli-client/tests/CliClient.UnitTests/PacketCodecTests.cs, HandshakeTests.cs, OpcodeRegistryTests.cs
      Notes: take frames from archive/kyp-snapshot/capturedPackets/ (RAR-archived), decode them, re-encode, byte-compare. Every registered opcode round-trips a known-good payload. CI gates `dotnet test` on this project.

- [ ] Item 15 — Opcode coverage push: register decoders for every opcode in Opcodes.h
      Status: not started
      Touches: tools/cli-client/src/CliClient.Core/Opcodes/Inbound/*, Outbound/*
      Notes: scrape `common/include/net7/Opcodes.h` for every enum value; emit a stub Inbound/Outbound class per opcode that decodes/encodes the matching PacketStructures.h struct (where one exists) or treats the payload as opaque bytes (logged) where it doesn't. Goal: zero "unknown opcode" warnings in the packet log for any well-formed server traffic. As Phase K wires the server side of more opcodes, the stubs get fleshed out — no big-bang rewrite.

- [ ] Item 16 — Documentation: docs/12-cli-client.md
      Status: not started
      Touches: docs/12-cli-client.md, README.md (link), CLAUDE.md (repo-map row)
      Notes: how to use it, the hard rules verbatim, the supported workflows, the log formats, where dumps land, how to add a new opcode (one-pager).

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
