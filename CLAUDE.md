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
├── proxy/             Net7Proxy
├── launcher/          MVASlaunch
├── client/
│   ├── linux-installer/   GPLv3 WINE installer (verbatim from upstream)
│   ├── detours/           Microsoft Detours (client API hooking)
│   └── mods/              client-side mods
├── tools/             C# editor suite (sector / mob / mission / etc.) — .NET 10 + WinForms
├── db/
│   ├── mysql/         original MySQL dumps
│   └── postgres/      converted Postgres schema (new)
├── tests/
│   ├── server/        gtest harness + smoke tests (new) for the C++ server
│   │                  -- build with `cmake -S tests/server -B build/tests`
│   └── integration/CliClient.IntegrationTests/  Phase T xUnit suite that drives CliClient.Core
│                      against the live docker-compose stack -- see docs/16-integration-tests.md
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

## Coding rules

- **C++**: target Linux first, Windows second. New code must compile on g++ 13+ with `-Wall -Wextra`. Don't reintroduce Win32 APIs in new code; use shims in `server/compat/` or POSIX directly.
- **"Runs on Linux" scope** — this means the *server* runs **natively** on Linux (no WINE). The Win32 cleanup applies to **server-native code only**: `server/src/`, `login-server/Net7Mysql/`, `login-server/Net7SSL/`, `proxy/`. It does **NOT** apply to:
  - **`client/**`** — the EnB client is a Win32 binary that runs under WINE (or on Windows). It's allowed and expected to use Win32 APIs. `client/detours/`, `client/mods/`, the linux-installer's WINE prefix — all stay Win32. Document this in any client-touching plan.
  - **`server/third_party/**` and vendored deps (boost, cryptopp, zlib, lua, MySQL Connector/C)** — we *consume* these libraries; we don't rewrite them. boost::interprocess on Linux uses real POSIX primitives through the same boost API; cryptopp / openssl / etc. likewise. Anything that looks like a Win32 symbol *inside* `third_party/` or a vendored header is upstream's concern, not ours.
- **SQL**: target Postgres syntax in new code. Existing MySQL-flavoured SQL is being migrated. Don't add new MySQL-isms.
- **C#**: tools target `net10.0-windows` with `<UseWindowsForms>true</UseWindowsForms>`. SDK-style csproj only.
- **No binaries in git** by default. Exception: third-party tools/libs we don't have source for go in `vendor/` (or alongside their project) with a `THIRD_PARTY_BINARIES.md` listing what they are, where they came from, and why we can't rebuild from source. The `.gitignore` uses `!` re-includes for these paths.
- **No secrets, credentials, or per-developer config** (`*.user`, `.suo`, `.env`).
- **Preserve license headers** (see above).

## Server integrity rules (CRITICAL — read before touching the server for ANY reason)

The server's job is to be a faithful re-implementation of the **real Earth & Beyond server** as it talked to the **real Earth & Beyond Win32 client**. Two things follow from that and they are non-negotiable:

1. **NEVER weaken, relax, or loosen the server's security posture to satisfy a tooling consumer.** This includes the CLI client (Phase S), the integration test suite (Phase T), the editor suite, packet-capture replay tooling, fuzzers, debug harnesses — *anything*. Concretely: do not disable authentication checks, do not widen rate limits, do not accept malformed packets the real server would reject, do not bypass session-state guards, do not turn on debug-only opcodes, do not expand visibility/scope filters, do not log secrets, do not return more data than the real server would have. If a tool needs something the server doesn't expose, the tool is wrong, not the server.

2. **NEVER make the server accept inputs or behaviour the real server did not.** This is the accuracy half of the same coin. The whole project is preservation; if our server starts diverging from the real server, the preservation value evaporates. New permissiveness is divergence.

**The only escape hatch** — and this is the *only* one — is a proof from a primary source that the real EnB server-and-client pair allowed the behaviour in question. Acceptable primary sources, in roughly decreasing order of weight:

- A **packet capture** of the live retail server (the RARs in `archive/kyp-snapshot/capturedPackets/` are the canonical reference set) showing the real server doing the thing.
- A **decompilation / disassembly** of the retail client or server binary showing the code path that produced the behaviour.
- **First-hand documentation** from a Net-7 developer or Westwood / EA engineer (RTFs in `archive/kyp-snapshot/Documents/`, Net-7 server architecture doc, GMCommands.txt, etc.).
- A **reproducible trace** from a Win32 client running against our server alongside a captured trace from the same operation against the retail server, with byte-level agreement.

What is **NOT** an acceptable justification:

- "The CLI client needs it."
- "The integration test would be easier."
- "It's faster to develop this way."
- "It only matters in test mode."
- "We can put it behind a `#ifdef DEV` flag." *No*. Dev flags rot, get accidentally turned on in prod, and silently widen the attack surface — and even when they don't, they corrupt the preservation goal.
- "The kyp/tada-o source already had it like this." Maybe; verify against capture/decomp before treating that as authority. The upstream forks added their own divergence.

**Process**: if a contributor (human or agent) believes a server change is justified by the escape hatch, the change MUST be accompanied by a commit-message citation of the primary source (capture filename + frame number, decompiled function name, document section). Reviewers reject the change otherwise. If no primary source exists, the answer is "the server stays as-is; the tool adapts."

This rule applies to `server/src/`, `login-server/Net7Mysql/`, `login-server/Net7SSL/`, and `proxy/`. It does NOT restrict changes that *tighten* the server toward greater fidelity (e.g. rejecting an input the real server rejected but we currently accept) -- those are always welcome.

## When you implement a new server-side opcode handler

The integration suite tracks opcodes the Net-7 server does NOT implement in `tests/integration/CliClient.IntegrationTests/Coverage/KnownUnimplementedOpcodes.cs`. Each entry has a matching `[Fact(Skip = ...)]` stub in `UnimplementedOpcodeStubTests.cs` whose body throws `NotImplementedException` on the first line.

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
| A new server-side gtest | `tests/server/<area>/` |
| A new CLI/integration xUnit test | `tests/integration/CliClient.IntegrationTests/<area>/` |
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
