# cli-client — Earth & Beyond emulator CLI client

Headless, passive observer client for the EnB emulator. Speaks the real EnB
client protocol (RC4 + RSA handshake, TLS login, global → master → sector
handoff) so we can:

- **Test**: drive opcode round-trips end-to-end without launching the Win32
  client under WINE.
- **Extract**: enumerate sectors / missions / items and dump structured
  output to disk.
- **Verify**: replay captured packet traces and compare what the server
  sends with what the real client received.

## Layout

```
tools/cli-client/
├── Directory.Build.props        Linux-first overrides (resets parent tools/ Windows targeting)
├── src/
│   ├── CliClient.Core/          reusable library — pulled in directly by Phase T xUnit tests
│   └── CliClient.App/           thin console front-end — references Core
└── tests/
    └── CliClient.UnitTests/     codec / handshake / opcode round-trip tests
```

The library/console split is what makes Phase T (xUnit integration tests)
clean: tests instantiate `new GlobalConnection(...)` directly and
`Assert.Equal` on decoded fields rather than shelling out to the binary
and scraping logs.

## Hard rules (DO NOT VIOLATE)

These are reproduced verbatim from `plans/19-phase-s-cli-client.md`. Any
contributor (human or agent) touching this directory must read them in full
before changing the server or the client.

1. **NEVER modify the server to make things easier for the CLI client.**
   The CLI client is a *passive observer*. The server's job is to talk to
   the *real* Win32 client. The only exception: if a packet capture or
   real-client decompilation proves the server is wrong, fix the server to
   match the real client — even if that happens to also fix the CLI
   client.
2. **The CLI client must always respect the server.** If the server
   imposes limits, returns garbage, or shows signs of crashing/overload
   (rate-limit replies, disconnects, packet floods, error opcodes), the
   CLI client stops the offending workflow immediately. No retry storms.
   No bypass attempts.
3. **The CLI client MAY request broader data than the real client** *if
   and only if* the server happily serves it without modification. If the
   server starts misbehaving the CLI client drops back to
   real-client-shaped queries.
4. **The CLI client is not authoritative on protocol shape.** When the
   CLI client's understanding of an opcode disagrees with the real client
   (per capture or decompilation), the real client wins. The CLI client
   adapts.

The same rules are enforced project-wide in `CLAUDE.md` under
"Server integrity rules".

## Build

```
dotnet build tools/cli-client/src/CliClient.App/CliClient.App.csproj -c Debug
```

## Smoke test

```
dotnet run --project tools/cli-client/src/CliClient.App -- --smoke
```

Expected: `ok: enb-cli-client 0.1.0-dev`

## Unit tests

```
dotnet test tools/cli-client/tests/CliClient.UnitTests/CliClient.UnitTests.csproj
```

## Status

Tracked in `plans/19-phase-s-cli-client.md`. Item 1 (scaffold) was the
first deliverable; subsequent items add the packet codec, opcode
registry, handshake, login flow, REPL, and workflows.
