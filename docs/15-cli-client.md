# CLI client (`tools/cli-client/`)

Headless C# .NET 10 client that speaks the Earth & Beyond wire protocol
end to end (TLS auth login + RSA/RC4 handshake + opcode-level chat).
Built for automation: capture replay, scripted login-and-do-X smoke
tests, and the Phase T integration suite. Not a game client — there is
no rendering, no UDP world state, no graphics. Plain stdout, plain
exit codes, plain test fixtures.

The full step-by-step plan is in `plans/19-phase-s-cli-client.md`;
this doc is the user-facing reference.

## Hard rules (verbatim from the plan)

The CLI client is a *consumer* of the server. The server is a faithful
reimplementation of the retail Earth & Beyond protocol. Two rules
flow from that and they are non-negotiable:

1. **Never modify the server to make the CLI client easier.** If the
   CLI needs something the server doesn't expose, the CLI is wrong,
   not the server. The escape hatch (and it's the only one) is a
   primary-source citation in the commit message — a packet capture,
   a decompilation, or first-hand documentation that proves the
   behaviour matches retail. See the "Server integrity rules" block
   in the top-level `CLAUDE.md` for the full text.
2. **Respect server limits.** No retry storms, no parallel auth
   floods, no probing for hidden opcodes the server would never
   accept from a real client. The HealthGuard kill-switch
   (`src/CliClient.Core/Net/HealthGuard.cs`) exists to enforce this
   — abort and surface the reason, do not silently back off and
   keep going.
3. The CLI MAY request broader data than a real Win32 client would
   (e.g. enumerate-all-X) when the server is willing to serve it.
   This is the one place the CLI is allowed to deviate from "do
   exactly what the real client does".
4. **The real client wins on protocol disputes.** If a capture
   shows the real client doing X and our codec does Y, the codec
   is wrong. Fix the codec, not the server.

## What it does today

| Subcommand | Purpose |
|---|---|
| `--help` / `-h` | Print usage and exit (rc 0) |
| `--version` / `-v` | Print version + phase marker and exit (rc 0) |
| `--smoke` | Print "ok: <name> <version>" and exit (rc 0) — CI smoke target |
| `repl` | Interactive REPL (`help`, `quit`). Workflow commands land here as Items 9–13 finish |
| `connect-and-login` | TLS login → global TCP connect + RSA/RC4 handshake → idle N seconds → clean disconnect |
| `send-chat` | Log in, connect to a global server, send one `ClientChat` (0x0033), exit |

Exit codes are stable: `0` success, `1` workflow-level failure
(login rejected, server aborted, kill-switch tripped), `2` bad
command-line usage.

## How to use it

```sh
# 1. Smoke
dotnet run --project tools/cli-client/src/CliClient.App -- --smoke

# 2. Connect-and-login against a local docker-compose stack
dotnet run --project tools/cli-client/src/CliClient.App -- connect-and-login \
    --user alice --pass alicepw \
    --login-host 127.0.0.1 --login-port 443 \
    --global-host 127.0.0.1 --global-port 3500 \
    --idle 5

# 3. Send a single chat (server only routes it if --game-id matches the
#    avatar currently attached to the session; the avatar handoff is
#    Phase K — see plans/19-phase-s-cli-client.md Items 10-12 for the
#    blocker description).
dotnet run --project tools/cli-client/src/CliClient.App -- send-chat \
    --user alice --pass alicepw \
    --game-id 12345 \
    --channel broadcast \
    --message "hello world"
```

By default the login client accepts an untrusted login-server TLS
certificate — the docker-compose stack ships a self-signed one.
Pass `--strict-tls` to enforce certificate validation against the
system trust store.

## Log formats

The Core library writes two kinds of structured logs and one console
sink. All three are optional; pass instances to the workflow constructors.

### `ConsoleSink` (stderr, human-readable)

```
[12:34:56.123] → 0x0033 ClientChat (12 bytes)  ee ccaa 0004 …
[12:34:56.131] ← 0x0036 ServerRedirect (10 bytes)  19290000 2ce8 999f af0d
[12:34:56.140] info  chat: gameId=12345 channel=Broadcast text=hello world
```

Arrows are `→` for outbound, `←` for inbound. Opcode names come
from `OpcodeNameLookup`, which is exhaustive over `Opcodes.h`
(207 entries) — so even an opcode the CLI has no typed codec for
still logs with its upstream symbolic name, e.g.
`0x00CE GUILD_REQUEST_CHANGE`.

### `PacketLog` (NDJSON, machine-readable)

One JSON object per packet, newline-delimited. Default rotation
size is 8 MB; rolled files keep the timestamp suffix.

```json
{"ts":"2026-05-24T12:34:56.123Z","dir":"out","op":51,"name":"ClientChat","bytes":12,"decoded":{"GameId":12345,"Type":"Broadcast","Message":"hello world"}}
{"ts":"2026-05-24T12:34:56.131Z","dir":"in","op":54,"name":"ServerRedirect","bytes":10,"decoded":{"SectorId":422576128,"ServerEndPoint":"44.232.153.159:3503"}}
```

The `decoded` field is the typed codec's output when one is
registered; `RawPayload` (hex) when only a `NamedOpaqueCodec` is
in use; absent when the payload is empty. Phase T's integration
tests grep against these files.

### `ChatLog` (NDJSON, chat-only)

A subset of `PacketLog` filtered to chat-bearing opcodes
(`0x0033 CLIENT_CHAT`, `0x005E AVATAR_EMOTE`, `0x005F AVATAR_EMOTE_RESPONSE`).
Easier to tail when you only care about player chatter during a
load test.

### Where files land

By default all three sinks write under `./logs/` relative to the
working directory:

```
logs/
├── packets-2026-05-24T12-34-56.ndjson
├── chat-2026-05-24T12-34-56.ndjson
└── (rolled files: …-001.ndjson, …-002.ndjson, …)
```

Override the directory by constructing `PacketLog` / `ChatLog`
with a different `baseDir`. The `connect-and-login` and `send-chat`
subcommands today only wire the `ConsoleSink` — passing the file
sinks is a Phase T concern (the integration tests need them).

## How to add a new opcode

Two paths depending on how deeply you need to understand the payload.

### Decode + log only (one minute)

Already done for every opcode in `Opcodes.h` by
`OpcodeRegistry.RegisterAllNamedOpaque()` — incoming traffic for
opcode 0x00CE shows up in the log as
`NamedOpaquePayload(0x00CE, "GUILD_REQUEST_CHANGE", <raw bytes>)`.
No code to write. The CLI doesn't *do* anything with the payload,
it just records it.

### Typed codec (an hour or so per opcode)

Add a real `IOpcodeCodec` so the payload becomes a strongly-typed
record:

1. Find the C struct in `common/include/net7/PacketStructures.h`.
   Note the field types, endianness conventions
   (`int32` BE for most global/master opcodes, LE for in-sector
   opcodes — check the server code that emits or consumes it).
   Watch for `long` vs `int32_t`: `long` is 4 bytes on the Win32
   client and 8 on Linux x86_64. The real wire is 4. Phase K's
   `struct MasterJoin` comment block has the canonical example.
2. Add a `Foo.cs` under
   `tools/cli-client/src/CliClient.Core/Opcodes/Inbound/`
   (server → client) or `Opcodes/Outbound/` (client → server).
   Mirror the shape of `ClientChatCodec` or `ServerRedirectCodec`.
3. Add the opcode constant to `OpcodeId.Known` if it doesn't
   already exist — that gives it the PascalCase name in logs.
4. Register the codec in `Program.cs` next to the existing
   `registry.Register(new ServerRedirectCodec())` line, and in
   the relevant workflow if one consumes the typed result.
5. Write unit tests in `tools/cli-client/tests/CliClient.UnitTests/Opcodes/`.
   At minimum: opcode-value check, layout check against the C
   struct (each field offset asserted), round-trip
   (decode → encode → byte-equal), and validation guards
   (reject malformed inputs the server's handler would also
   reject).
6. If you have a retail capture frame, add it to
   `tools/cli-client/tests/CliClient.UnitTests/Captures/fixtures/`
   and exercise it via `RetailCaptureTests`. The fixture format
   is documented at the top of `capture3-frames.txt`.

## Verifying against retail captures

The repository ships RAR-archived captures from a live retail
session in `archive/kyp-snapshot/capturedPackets/`. The CLI's
test suite extracts frames from these by hand into committed
fixture files (see `RetailCaptureTests` for the pattern).
**Do not decrypt or replay live captures at runtime in the CLI
itself** — keep that to the test suite, where the inputs are
reviewed.

## Limitations

- No avatar selection. The `send-chat` subcommand requires
  `--game-id` to match an avatar already attached to the session.
  Phase K's `0x006D GlobalConnect` → `0x006F GlobalTicket`
  → `0x0070 GlobalAvatarList` → `MasterJoin` handoff needs to be
  live server-side before the CLI can pick an avatar end-to-end.
  Tracked as Items 10–12 in `plans/19-phase-s-cli-client.md`.
- No UDP plane. Position updates, combat events, and anything else
  on the in-sector UDP socket are out of scope for Phase S; the
  CLI is TCP-only.
- No GUI. Won't ever be one — that's what the real client is for.

## See also

- `plans/19-phase-s-cli-client.md` — phase plan with per-item status.
- `plans/20-phase-t-cli-integration-tests.md` — the xUnit harness
  that consumes CliClient.Core for end-to-end tests.
- `CLAUDE.md` "Server integrity rules" — the non-negotiable block
  on weakening the server for tool convenience.
- `docs/03-network-protocol.md` — opcode-level overview of the wire.
- `common/include/net7/Opcodes.h` — source of truth for opcode IDs.
- `common/include/net7/PacketStructures.h` — source of truth for
  payload layouts.
