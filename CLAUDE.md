# CLAUDE.md — instructions for Claude (and other agents)

## What this repo is

This is the consolidated preservation project for the **Earth & Beyond MMO emulator**. It merges three upstreams into one cleanly-structured codebase:

1. The Net-7 / tada-o server fork (C++ server, ~162K LOC)
2. The kyp snapshot (older server + the C# editor suite + packet captures + docs)
3. The `enb-linux-installer` script (GPLv3 bash script that installs the client on Linux via WINE)

The long-term goal is to make the server run cleanly on Linux, use Postgres, have tests, build without warnings, and ship as containers.

## Plans workflow (READ THIS FIRST, EVERY INVOCATION)

The source of truth for "what's done / what's next" across invocations is the `plans/` directory:

- `plans/00-master.md` — status table for all phases. **Read this on every startup.**
- `plans/01-phase-a-merge.md` ... `plans/09-phase-i-dev-env.md` — per-phase checklists.
- `plans/99-decisions-log.md` — append-only log of meaningful decisions.

### Rules

1. **On startup**: read `plans/00-master.md`. Identify the in-progress phase. Read that phase's file.
2. **Do not stop at phase boundaries.** Push through Phase A → B → C → ... continuously. The only legitimate stops are: context budget genuinely exhausted (do a final plan update first, then stop cleanly), an external dependency that is unrecoverably blocked, or all phases done.
3. **Never ask "should I continue?"** — continue.
4. **Update plans continuously**. As items finish, flip `[ ]` → `[~]` → `[x]` (or `[!]` blocked, with reason in Notes). Add commit SHAs / file paths to the Notes. Append newly-discovered subtasks. Update the master status table.
5. **Commit as you go.** Don't accumulate giant uncommitted diffs. Use clear commit messages tied to plan items.
6. **If plans and reality diverge**, update plans first, then continue.

## Repo map

```
.
├── CLAUDE.md          (you are here)
├── README.md          project overview, quickstart, license summary
├── plans/             multi-phase plan files — source of truth for progress
├── docs/              comprehensive documentation: architecture, protocol, modules, schema, abilities, tools, build
├── LICENSES/          license texts and the license map
├── common/            shared protocol/wire-format headers (Phase R)
│   └── include/net7/  Opcodes.h, PacketStructures.h, Ports.h, Packing.h, Mutex.h, WestwoodRC4.h, WestwoodRSA.h
│                      — included into server/, proxy/, login-server/ via PRIVATE include dir
│                      — single source of truth for anything that crosses a process boundary
├── server/            C++ server (from tada-o)
│   ├── src/           all .cpp / .h
│   ├── compat/        Win32 → POSIX shims (new code)
│   ├── third_party/   vendored libs (boost subset, cryptopp, etc.)
│   ├── CMakeLists.txt modern CMake
│   └── Makefile.legacy tada-o's original Makefile, kept for reference
├── login-server/      Net7Mysql + Net7SSL — auth/login flow
├── proxy/             FreyaProxy
├── launcher/          MVASlaunch
├── client/
│   ├── linux-installer/   GPLv3 WINE installer (verbatim from upstream)
│   ├── detours/           Microsoft Detours (client API hooking)
│   └── mods/              client-side mods
├── tools/             C# editor suite (sector / mob / mission / etc.) — .NET 10 + WinForms
├── db/
│   ├── mysql/         original MySQL dumps
│   └── postgres/      converted Postgres schema (new)
├── freya/             NEW self-contained code (MIT, see LICENSES/Freya) -- no Net-7 compile dependency
│   ├── website/           project website + auction house (placeholder)
│   ├── status-notifier/   Go sidecar: external-status events -> Discord bot + /status, /notify
│   ├── cli-client/        C# headless CLI client (CliClient.Core / .App) -- Phase S
│   └── tests/
│       ├── server/        gtest harness + smoke tests for the C++ server
│       │                  -- build with `cmake -S freya/tests/server -B build/tests`
│       └── integration/CliClient.IntegrationTests/  Phase T xUnit suite that drives CliClient.Core
│                          against the live docker-compose stack -- see docs/16-integration-tests.md
├── vendor/            third-party binaries without source (with THIRD_PARTY_BINARIES.md notes)
├── archive/           historical material — old snapshots, packet captures, original docs
├── justfile           build / lint / test / dev / package targets
├── docker-compose.yml dev environment (postgres + server + login)
└── .github/workflows/ CI
```

## License rules (CRITICAL)

- **Project default**: CC BY-NC-SA 3.0 (see `LICENSES/enb-emulator`). NonCommercial-only.
- **Precedence**: per-file header > per-folder LICENSE > project default.
- **Never strip or modify a license header**. Every Net-7 `.cpp`/`.h` carries a CC BY-NC-SA 3.0 header. Preserve it when moving, renaming, or refactoring files.
- **Never strip a per-folder LICENSE file** (e.g. `client/linux-installer/LICENSE` is GPLv3 and stays as-is).
- **Don't relicense Net-7 code**. Only Net-7 Entertainment can do that.
- **Don't add code that requires commercial use**. The NC clause forbids it.

### Project location & the `freya/` MIT rule (where new code goes)

There are exactly two buckets. Decide which one a change is BEFORE you write it:

- **Modification to an existing file, or to the existing server binary == Net-7
  CC BY-NC-SA 3.0, and it stays where it already lives.** Editing an inherited
  Net-7 / tada-o file in place does NOT make it Freya/MIT, no matter how much
  you rewrite inside it. It keeps its CC BY-NC-SA 3.0 header and its current
  directory (`server/`, `login-server/`, `proxy/`, `launcher/`, `tools/`,
  `client/`, ...). You may NOT move such a file under `freya/` to relicense it
  -- that would relicense Net-7 code, which is forbidden.
- **New code that produces a NEW binary/artifact and does NOT depend on any
  Net-7 source to compile == Freya, MIT, and lives under `freya/`.** This is
  the self-contained new work: the website + auction house (`freya/website`),
  the Go status-notifier sidecar (`freya/status-notifier`), the C# CLI client
  (`freya/cli-client`), and the test suites we wrote (`freya/tests`). Give it
  the Freya name (see the naming bullet below) and the MIT header / `LICENSES/Freya`.

The two are not in tension with the precedence rules above: `freya/` is MIT;
everything else follows per-file header > per-folder LICENSE > project default
(CC BY-NC-SA 3.0). If a Freya-named reference is mixed *inside* an inherited
Net-7 file, that file is still CC BY-NC-SA 3.0 -- only the self-contained code
under `freya/` is MIT. **Before adding project code, decide the bucket and put
it in the right place first** (move existing tooling under `freya/` only if it
is genuinely self-contained new code with no Net-7 compile dependency).

Note what is deliberately NOT under `freya/`: `login-server/` (Net7Mysql /
Net7SSL) and `proxy/` are inherited Net-7 code (the proxy compiles against
`common/include/net7/`), so they stay put under CC BY-NC-SA 3.0 even though we
have heavily edited them. New code that links Net-7 headers is a *modification*,
not new independent work.

## Coding rules

- **Naming new code "Freya", not "Net7"**: the inherited upstream code carries the `Net7` brand (Net-7 / tada-o) -- leave those existing names alone (renaming live symbols/files churns the merge and breaks cross-refs). But anything **new** -- a new system, subsystem, tool, DLL, rewrite, or replacement for an old Net7 component -- gets the **Freya** name (`FreyaProxy`, `FreyaPosFeed.dll`, `FreyaInject.exe`, `tools/LaunchFreya`, ...). Rule of thumb: if you're writing it fresh or rewriting an old piece, it's Freya; if you're editing inherited code in place, keep its Net7 name.
  - **Inside `freya/*`, ALL identifiers/macros/symbols we author use `Freya`/`FREYA`, never `Net7`/`NET7`.** Code under `freya/` is new, self-contained, MIT work -- it is not Net7 code, so it must not carry the Net7 brand. A symbol/macro/struct/function we invented gets `Freya`/`FREYA` (e.g. `FreyaClientPosFeed_Start`, `FREYA_CLIENT_POS_PORT`, `FreyaClientPosDatagram`) -- including symbols that a CC-BY-NC-SA file outside `freya/` (the proxy, etc.) consumes by including a `freya/` header: rename our invented symbol everywhere it is used, the consumer's reference included. The ONLY `Net7`/`NET7` tokens allowed to remain inside `freya/*` are references to genuinely inherited, pre-existing things we did NOT author (`Net-7` the project, `Net7Proxy` the real proxy binary, `NET7MP` the multiplayer mode). Test before renaming: did we invent this name (check `git log -S`)? If yes -> rename it Freya. If it was already there in upstream -> leave it.
- **C++**: target Linux first, Windows second. New code must compile on g++ 13+ with `-Wall -Wextra`. Don't reintroduce Win32 APIs in new code; use shims in `server/compat/` or POSIX directly.
- **"Runs on Linux" scope** — this means the *server* runs **natively** on Linux (no WINE). The Win32 cleanup applies to **server-native code only**: `server/src/`, `login-server/Net7Mysql/`, `login-server/Net7SSL/`, `proxy/`. It does **NOT** apply to:
  - **`client/**`** — the EnB client is a Win32 binary that runs under WINE (or on Windows). It's allowed and expected to use Win32 APIs. `client/mods/`, the linux-installer's WINE prefix, and the in-client injection unit `freya/client-injection/` (FreyaInject.exe + FreyaPosFeed.dll) — all stay Win32. Document this in any client-touching plan.
  - **`server/third_party/**` and vendored deps (boost, cryptopp, zlib, lua, MySQL Connector/C)** — we *consume* these libraries; we don't rewrite them. boost::interprocess on Linux uses real POSIX primitives through the same boost API; cryptopp / openssl / etc. likewise. Anything that looks like a Win32 symbol *inside* `third_party/` or a vendored header is upstream's concern, not ours.
- **SQL**: target Postgres syntax in new code. Existing MySQL-flavoured SQL is being migrated. Don't add new MySQL-isms.
- **NEVER build a DB query by string-concatenating values. Ever.** A *value* (anything a user typed, anything read from another row, any id/name/number) must reach the database as a bound **parameter** (`@p` via Npgsql, or a Dapper anonymous-object parameter -- `conn.Query<T>("... WHERE id = @id", new { id })`), never spliced into the SQL text. This is non-negotiable for the C# tools (`tools/`), the server, and anything else that talks to Postgres.
  - The SQL **text** is a plain literal string. Do NOT use a keyword-builder DSL (`DB.SELECT + DB.FROM + DB.WHERE + ...`); write the literal query. The only things that vary at runtime are the bound parameters.
  - No manual escaping of values into SQL (`"'" + v.Replace("'", "''") + "'"` is banned). If you find yourself escaping a quote to put a value in a query, you are doing it wrong -- bind a parameter.
  - Prefer **Dapper** for new/refactored read queries in the C# tools (it parameterizes cleanly and maps rows to records). Plain parameterized `NpgsqlCommand` is also fine. A DataSet/`NpgsqlDataAdapter` is acceptable *only* with bound parameters.
  - `DataTable.Select("...")` is an in-memory ADO.NET row filter, not a DB query and has no parameters -- it is NOT an injection vector, but it is still fragile string-building: filter rows with LINQ instead of a concatenated filter expression.
  - **Exception:** generated `.sql` *script artifacts* (e.g. the editor change-tracking changesets, seed/migration generators) are flat scripts with no runtime parameter channel, so they legitimately emit literal SQL text; keep their quoting robust. The hand-maintained seed-account shell scripts are likewise fine as-is.
- **C#**: tools target `net10.0-windows` with `<UseWindowsForms>true</UseWindowsForms>`. SDK-style csproj only.
- **No binaries in git** by default. Exception: third-party tools/libs we don't have source for go in `vendor/` (or alongside their project) with a `THIRD_PARTY_BINARIES.md` listing what they are, where they came from, and why we can't rebuild from source. The `.gitignore` uses `!` re-includes for these paths.
- **No secrets, credentials, or per-developer config** (`*.user`, `.suo`, `.env`).
- **No server IP addresses in committed files.** Never write a real server's IP address into any committed file -- source, comments, docs, plans, commit messages, test fixtures, capture provenance, justfile/README defaults. This covers the live Net-7 reference server, any retail/historical EnB server IPs, DB-dump source/target hosts, and any other real host. Refer to such a server by role instead ("the live reference server", "the retail sector server", "the capture source host"). Private/loopback addresses used purely as local-dev defaults (`127.0.0.1`, RFC1918) are fine, but prefer a hostname or placeholder where one reads as well. When a packet decode legitimately produces a dotted-quad as *output* (e.g. inet_ntoa of an s_addr the test pins), keep the bytes but do not also restate the real IP in surrounding prose.
- **No filesystem paths outside the repo root in committed files.** Paths under the repo root are fine (`/data/dev/enb-emulator/...` or, better, repo-relative `tools/...`). Never commit an absolute path to anything *outside* the repo root (another checkout, an external dataset, a home dir, a scratch dir). Describe such a location by role ("the external reconstruct dataset") and make build tools take it via an arg/env var with a neutral default -- do not hardcode the absolute external path into committed source, generated file headers, docs, or plans.
- **Preserve license headers** (see above).

## Server modification rules (CRITICAL -- read before touching the server for ANY reason)

The server is an OLD, questionable-quality implementation inherited from upstream, and the long-term plan is to rewrite it. So unlike the earlier "the server is frozen" stance, "the server stays as-is" is NOT the automatic answer any more: the server CAN be changed to make it CORRECT. But "correct" has a high, evidence-based bar, and a tooling consumer's convenience never clears it. The goal is unchanged -- faithfully reproduce how the **real Earth & Beyond server** talked to the **real Win32 client** -- we are now just allowed to *fix the server toward* that target, not only freeze it.

Two things stay non-negotiable:

1. **NEVER weaken the server's security posture for a tooling consumer's convenience.** This includes the CLI client (Phase S), the integration test suite (Phase T), the editor suite, packet-capture replay tooling, fuzzers, debug harnesses -- *anything*. Do not disable authentication checks, widen rate limits, accept malformed packets the real server would reject, bypass session-state guards, turn on debug-only opcodes, expand visibility/scope filters, log secrets, or return more data than the real server would, *because a tool finds it easier*. A tool wanting something just to make the tool's life easier is the tool being wrong, not the server.

2. **A server change must be a CORRECTNESS change, proven against a primary source.** A change that makes us diverge from the real server's observable behaviour destroys preservation value. A change is allowed only when a primary source shows the server is currently WRONG (or incomplete) measured against the real server-and-client pair.

**When you MAY change the server.** You may change `server/src/`, `login-server/Net7Mysql/`, `login-server/Net7SSL/` (and `proxy/`) when ALL THREE of these hold:

**A. Primary-source proof** that the change is what correctness requires. Acceptable sources, in roughly decreasing order of weight:
- A **packet capture** of the live retail server (the RARs in `archive/kyp-snapshot/capturedPackets/` are the canonical set), or a local cleartext proxy<->server capture, showing the real behaviour.
- **Behavioural analysis of the retail client or server binary** showing the code path that produced the behaviour.
- **First-hand documentation** from a Net-7 developer or Westwood / EA engineer (RTFs in `archive/kyp-snapshot/Documents/`, the Net-7 server architecture doc, GMCommands.txt, etc.).
- A **reproducible trace** from a Win32 client against our server alongside a captured trace of the same operation against the retail server, with byte-level agreement.

**B. Full CLI parsing + tests of the packet land FIRST.** Before the server-side wire change, the opcode/packet must be fully parsed in the C# CLI client (a `CliClient.Core` record) and pinned by tests, so we provably understand the format byte-for-byte BEFORE changing how the server emits or accepts it. Understanding-before-change is mandatory: a server wire change without the accompanying CLI parse + test is incomplete and gets rejected. (The "three places in sync" rule -- server + proxy + CLI -- already binds; this makes the CLI half a *precondition*, not a follow-up.)

**C. A real-client (client.exe) verification step is tracked.** The CLI proves the *format*; only the real Win32 client proves the *game still works*. Every server/proxy wire change the CLI alone cannot fully validate gets an entry appended to `plans/29-client-verification.md` -- the running checklist of things for the project owner to confirm against the real client. This does NOT block progress: keep implementing, the owner verifies asynchronously whenever convenient. But the change is not DONE until its checklist entry is confirmed, and you must never silently assume a change works with the real client because the CLI passed.

What is still **NOT** a justification on its own (none is a correctness proof):
- "The CLI client needs it" / "the integration test would be easier" / "it's faster this way" / "it only matters in test mode."
- "We can hide it behind a `#ifdef DEV` flag." *No*. Dev flags rot, get turned on in prod, and silently widen the attack surface.
- "The kyp/tada-o source already had it like this." The upstream forks added their own divergence -- verify against capture / client behaviour before treating upstream as authority.

**Process**: any server/proxy wire-behaviour change MUST carry, in its commit message, (1) the primary-source citation (capture filename + frame number, the analysed function, or the document section), (2) a pointer to the CLI parse + test that landed first, and (3) the `plans/29-client-verification.md` entry id for the real-client check. Reviewers reject the change otherwise.

Changes that purely *tighten* the server toward fidelity (rejecting an input the real server rejected but we currently accept) remain always-welcome and need only the primary-source citation.

## Log warnings and errors are not noise (CRITICAL)

If the server, proxy, login-server, or any docker container logs an
`Error`, `WARNING`, `FATAL`, `failed`, exception trace, SQL error,
"Unable to ...", or similar, you do NOT get to wave it off as boot
noise, stale state, or "pre-existing". Treat every such line as a real
defect until you have read the code that produced it, identified the
root cause, and confirmed in writing that:

1. The failure has no functional consequence (e.g. the affected code
   path is genuinely unreachable in the running configuration), OR
2. The failure is logged but the operation transparently recovers via
   a documented retry / fallback that you have actually verified.

Otherwise: fix it.

The Phase-N Postgres case-folding incident is the canonical example.
The boot log printed 3104 `Error executing` lines that "had always been
there"; treating that as noise meant items loaded with empty effect
payloads for years, which was a load-bearing contributor to the
loading-screen hang investigation taking far longer than it should
have. Errors in the log are debt with compound interest.

This applies equally to:

- `docker compose logs <svc>` output (server, proxy, login, postgres)
- `wine` / client traces (some lines really are mouse-handler noise --
  see the Wine-debug-noise memory -- but the burden is on you to
  identify them by code path, not by hand-wave)
- Test runner output, build warnings, lint findings, CI step exit
  codes that didn't go to zero
- Anything that says "Error" or "warning" anywhere

If you log something and it doesn't matter, demote it to debug or
delete the log line. A log line that says "Error" but doesn't mean
"Error" is a worse bug than a silent failure.

## The proxy is NOT a dumb relay (READ before using ANY capture or dump)

FreyaProxy sits between the EnB client and the server, and it is an active
protocol participant -- not a passthrough. Treating a server-side packet
capture as if it were the byte stream the client receives (or vice-versa)
is wrong and will waste your time. Concretely, on the server<->client path
the proxy:

- **Strips and re-frames.** Server->client UDP arrives as a 12-byte outer
  header followed by packed `[length:u16][opcode:u16][payload]` sub-packets.
  The proxy peels the outer header, walks the sub-packets, and re-emits each
  one to the client over its own TCP framing. The client never sees the UDP
  outer header or the server's sub-packet packing.
- **Consumes opcodes the client never sees.** A whole band of control /
  proxy-management opcodes (galaxy-map cache, prospect/tractor/loot,
  static/resource object create, login-stage confirm, the 0x2025..0x202e
  gate-cache band, MVAS terminate, ...) are handled *inside* the proxy and
  never forwarded. The client's game receiver only ever sees opcodes
  `0x01..0xFE`; anything else is consumed, dropped, or treated as a bad
  opcode and recovered from. A capture full of 0x20xx opcodes does not mean
  the client ever parsed them.
- **Drops malformed / undersized frames** (e.g. an aux-data frame whose
  framed length is < 8 bytes) so the client never has to.
- **Rewrites payloads in flight** for a few opcodes (gate-cache timestamp
  injection, etc.) -- the bytes on the server side are not the bytes the
  client decodes.
- **Generates packets the server never sent** -- resend requests, login
  handoff, 0x3004/0x3008 visibility kicks, MVAS position feed -- and
  reassembles split packets.
- **Encrypts.** Client<->proxy TCP is Westwood RSA + dual RC4 streams;
  proxy<->server UDP is cleartext. A cleartext server-side capture has no
  direct relationship to the on-wire client bytes.

**Consequence for packet work (captures, parsing, reconstruction, dumps,
fixtures):** a Net7 server-side packet capture can NOT be applied directly
to `client.exe`, and a client-side capture can NOT be replayed straight at
the server, without first accounting for what the proxy does to that
opcode. Before you pin bytes, build a fixture, or "replay a capture,"
identify which side of the proxy the capture was taken on and what the
proxy does to that opcode in that direction. The proxy's per-opcode
behaviour is the single source of truth here: see
`proxy/UDPProxyToClient_linux.cpp` (server->client demux, `HandleCustomOpcode`
+ `SendClientPacketSequence`) and `proxy/ClientToServer_linux_stubs.cpp`
(client->server dispatch, `ProcessSectorServerOpcode`). The committed
reference captures are under `archive/kyp-snapshot/capturedPackets/`;
`proxy/local-debug/` is a gitignored, local-only scratch dir for your own
working captures -- when present, those are taken on the cleartext
proxy<->server UDP leg, NOT the encrypted client<->proxy leg. Read any
capture knowing which leg it came from.

There is exactly ONE proxy implementation. `proxy/` builds as a single
cross-platform source tree: Linux-native (server-side, no WINE) for docker,
and Win32 PE via the MinGW toolchain (`proxy/cmake/mingw-w64-x86_64.toolchain.cmake`)
for the client side under WINE / native Windows -- which is how real players
run it. Do not add a second, platform-forked copy of any proxy translation
unit; fix the one source. (The old `NET7_LEGACY_WIN32`-walled twin files were
deleted -- they were dead code that only invited drift.)

## Wire format & byte order (READ before touching ANY packet emitter)

The EnB protocol predates portable serialization. Almost every packet
that crosses a process boundary is a **packed C struct memcpy'd
verbatim onto the socket** (`SendResponse((unsigned char*) &s,
sizeof(s))` / `*((int*) p) = value`). The retail server, the retail
client, and the proxy are all x86 little-endian, and the wire format
*inherits* that. For nearly every numeric field, the on-wire bytes are
the **little-endian** representation of the host-order integer.

This bites in two reliable ways. Both have shipped real crashes:

### Trap 1: `ntohl` / `htonl` on host-order data right before send

`htonl` and `ntohl` are byte-swap macros on x86. If a field's source
value is already in host order (a sector id, an avatar id, a slot
number, a game id), then `redirect.field = ntohl(host_value)` puts the
*byte-swapped* int into the struct. memcpy to wire yields BE-encoded
bytes. The Win32 client reads LE, gets nonsense, looks it up in a
pool, gets NULL, and crashes on the next vtable dispatch.

This is exactly the `proxy/ClientToMasterServer.cpp` ServerRedirect
crash. The fix: just assign the value (`redirect.sector_id = sector_id`).

The ONLY legitimate use of `htonl`/`ntohl` on a send path is when the
source value is genuinely in **network byte order** (`inet_addr`'s
return value, `s_addr`, a field that was already byte-swapped on the
receive side and stored that way). The IP-address slot in
ServerRedirect is the canonical example -- `m_IpAddress` came from
`inet_addr` and the `ntohl(m_ServerMgr.m_IpAddress)` IS correct
there. Every other field in that struct is host-order and stays
host-order on the wire.

### Trap 2: stale docker images mask the source fix

`docker compose up -d` alone reuses the existing image if the source
has changed but the image hasn't been rebuilt. If a launcher only did
`up -d`, fixing a bug in `proxy/`, `server/src/`, or `login-server/`
and then re-running it would silently hand the old binary to the
client and reproduce the original crash verbatim -- it would look like
the fix did nothing.

`play-local` and `play-cli` therefore **build-if-stale by default**:
each runs `docker compose build` first (the layer cache makes an
unchanged service a near-instant no-op -- no recompile, no restart),
then `up -d`, which recreates ONLY the containers whose image ID
actually changed. So if nothing changed, nothing rebuilds and nothing
bounces; if you changed C++, the new binary is built and only that one
container restarts. This is what kills the stale-image trap at the
source. (`play-cli` builds only its own CLI unit -- it brings the
SHARED server/login/proxy up with `--no-recreate`, so launching a CLI
never rebuilds or bounces the shared stack and never disturbs another
player's session.)

Escape hatch: **`ENB_NOREBUILD=1`** forces a pure attach -- skip the
build entirely and start only missing containers (`--no-recreate`
leaves every running container, and its in-flight player/session
state, exactly as-is). Use it when you KNOW the running binaries are
current and must not bounce.

To rebuild explicitly without launching a client:
- Changed `server/`, `proxy/`, or `login-server/`? Run **`just rebuild`**
  (builds those three, recreating only the containers whose image
  actually changed; postgres + pgdata untouched). Scope it with an
  arg: `just rebuild proxy`.
- Changed CliClient code or the unit proxy? Run **`just rebuild-cli <UNIT>`**.

Both launchers print each image's build time and flag any image that
MAY be out of date (source on disk newer than the built image -- you
will mainly see this under `ENB_NOREBUILD=1`, or for the shared server
under `play-cli`). Do NOT remove the default `docker compose build`
step from `play-local` / `play-cli` -- it is the stale-image guard;
gate extra disruption behind `ENB_NOREBUILD` instead.

### Process when adding or changing a packet emitter

1. **Find a real capture** of the same packet in
   `archive/kyp-snapshot/capturedPackets/` (extract the relevant RAR
   to `/tmp/` and `grep` for the opcode). If none exists, find one
   for a structurally similar packet.
2. **Build the struct, send it, capture the bytes, and diff against
   the retail capture.** Byte-for-byte agreement is the bar.
3. If the bytes differ on a field, the field's value-vs-byte-order
   convention is wrong -- audit `htonl`/`ntohl` use on that field
   first, then field type/size.
4. Add a fixture under
   `freya/tests/integration/CliClient.IntegrationTests/Fixtures/Captures/`
   and a `CaptureReplayTests.cs` `[Fact]` that pins the bytes, so a
   future regression breaks the build.
5. Cite the capture file + frame number in the commit message
   (CLAUDE.md "Server integrity rules" requires this for ANY change
   to server/proxy/login-server wire behaviour anyway).

## Opcode / packet-structure knowledge lives in THREE places -- keep them in sync (CRITICAL)

The same wire packet is touched by three independent codebases, and a
change to how one opcode is built, parsed, framed, or fabricated almost
always has to be mirrored in the other two or the stack silently diverges:

1. **The server** (`server/src/`, `login-server/`) -- the authoritative
   emitter and parser. It defines what the bytes mean.
2. **The proxy** (`proxy/`) -- re-frames, consumes, drops, rewrites, and
   *fabricates* packets between server and client (see "The proxy is NOT a
   dumb relay"). For control opcodes the server emits in compact form (the
   `0x20xx` fabrication band), the proxy is what expands them into the
   client-facing game packets -- so the proxy holds packet-structure
   knowledge the server side never serializes directly.
3. **The C# CLI** (`tools/`/`CliClient.Core` + the Phase T integration
   suite) -- parses and asserts the same packets to drive and verify the
   server. It is the byte-pin: it is where a fabricated/served packet's
   exact bytes get locked against regression.

**Rule:** when you add or change opcode handling or a packet structure in
ANY one of these, audit the other two in the SAME change and update
whichever also encode that opcode/structure. A packet emitter changed in
the server but not pinned in the CLI, or fabricated in the proxy but not
parseable by the CLI, is an incomplete change. Note the cross-references
in the commit message.

**The server caveat still binds.** "Keep them in sync" is NOT licence to
edit the server casually. Every server-side change is still governed by
the "Server integrity rules" above: never weaken the security posture for
a tooling consumer, never make the server accept inputs the real server
rejected, and cite a primary source for any wire-behaviour change. If the
CLI or proxy needs something, the default is that the *tool* adapts to the
server, not the reverse. Sync the server only when the server is genuinely
the thing that is wrong (measured against capture / decomp / first-hand
docs), and cite it.

## When you implement a new server-side opcode handler

The integration suite tracks opcodes the Net-7 server does NOT implement in `freya/tests/integration/CliClient.IntegrationTests/Coverage/KnownUnimplementedOpcodes.cs`. Each entry has a matching `[Fact(Skip = ...)]` stub in `UnimplementedOpcodeStubTests.cs` whose body throws `NotImplementedException` on the first line.

**When you wire an opcode server-side that was on the unimplemented list, you MUST**:

1. Delete the `[Fact(Skip = ...)]` line on the matching `Opcode_NNNN_*` stub. The throw will now fire, forcing you to replace the body with a real round-trip test rather than silently green-passing.
2. Replace the stub body with the real test (mirror the shape of existing `Opcodes/*Tests.cs` files in the suite -- send the opcode, drain frames, byte-pin the reply).
3. Remove the corresponding entry from `KnownUnimplementedOpcodes.Opcodes`.
4. Add a `new TestedOpcode(...)` entry in `TestedOpcodes.Opcodes` (sorted by opcode value) and bump `TestedOpcodes.MinTestedCount` by one.
5. The cross-check `UnimplementedOpcodeStubTests.EveryEntry_HasMatchingSkippedStub` and `CoverageRatchetTests.Ratchet_CountEqualsFloor` will both verify the migration was done cleanly -- they're paired by design so you can't half-do it.

The same migration path applies if the upstream protocol catalog adds an opcode that was missed: add a `KnownUnimplementedOpcode` entry first (with a paired Skip stub), then upgrade it to a `TestedOpcode` when the server-side handler lands.

## Where to put new things

| You're adding... | Put it in... |
|---|---|
| A new server ability | `server/src/Abilities/` |
| A new C++ subsystem | `server/src/<subsystem>/` |
| A wire-format struct, opcode, or port macro | `common/include/net7/` (NOT a per-process header) |
| A POSIX shim for a Win32 API | `server/compat/` |
| A new C# editor or tool | `tools/<kebab-name>/` |
| A new documentation page | `docs/<NN-topic>.md` (numbered) |
| A new server-side gtest | `freya/tests/server/<area>/` |
| A new CLI/integration xUnit test | `freya/tests/integration/CliClient.IntegrationTests/<area>/` |
| A new third-party C++ dep | `server/third_party/<name>/` |
| A precompiled binary we can't rebuild | `vendor/<name>/` with `THIRD_PARTY_BINARIES.md` |
| A new plan/sub-plan | `plans/<NN-phase>.md`, update `plans/00-master.md` |

## Build & dev

```
just dev       # docker compose up (postgres + server + login)
just build     # cmake build server, dotnet build tools
just test      # ctest + dotnet test
just package   # build OCI image
```

See `docs/08-build.md` and `docs/09-running-locally.md` for the details.

## Pointers

- Architecture: `docs/02-architecture.md`
- Network protocol: `docs/03-network-protocol.md`
- DB schema: `docs/06-database-schema.md`
- Modernization roadmap: `docs/10-modernization-roadmap.md`
- CLI client (Phase S): `docs/15-cli-client.md`
- Integration tests (Phase T): `docs/16-integration-tests.md`
- Open work: `plans/00-master.md`
