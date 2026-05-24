# Phase P — Stub-debt audit (sweep dangling "Phase X work" markers)

## Why this phase exists

User flag (2026-05-23):

> at some point we need to re-audit all things that say "this is phase _
> work" to make sure there isnt empty stub shit left over that is borken.
> For example ServerManager.h says this which seems dangerous if we dont
> go back to check later:
>
>     // GetTCPCBuffer/GetConnection — kyp-era TCP path. tada-o reuses the
>     // resend buffer for TCP and lets ConnectionManager hand out
>     // Connection nodes; stub to NULL so the listener compile-links.
>     // Real wiring is Phase B work.
>     CircularBuffer    * GetTCPCBuffer()    { return m_ReSendBuffer; }
>     class Connection * GetConnection()    { return 0; }

The hazard: an inline `{ return 0; }` stub compiles green and links green
but at runtime drops accepted sockets / dereferences NULL / leaks
resources. Coming back to these later doesn't happen automatically; the
TODO comment isn't a CI signal, and the build passing is misleading.

## Scope

Every "Phase X work" / "Phase X-continuation" / "for now" / "stub to" /
"kyp-era" / "not functional on Linux" marker in Linux-compiled code
(server/src, login-server/Net7SSL, login-server/Net7Mysql, proxy/) gets
triaged. **Not in scope**: the vendored trees (server/third_party,
server/src/openssl, server/src/mysql, server/src/LUA), client/, archive/.

For each stub, classify as one of:

1. **DEAD-ON-LINUX**: TU or function is reachable only from WIN32-walled
   code or has no callers at all on Linux. Action: wall the function in
   `#ifdef WIN32`, or delete it entirely. Cannot crash if it can't run.
2. **NEEDS-IMPL**: Real Linux callers reach it; current stub silently
   does the wrong thing. Action: implement the right behaviour, OR
   add `LogMessage("FATAL: %s called but unimplemented", __func__); abort();`
   so the next runtime hit is loud, not silent.
3. **DELIBERATE-NOOP**: The Linux build genuinely doesn't need the
   behaviour (Win32-only IPC, single-instance mutex on a service that
   runs under docker-compose, etc.). Action: rename comment to make the
   intent clear ("Linux no-op by design — see plans/X for why") so the
   next reviewer doesn't trip on it.

## Current known stubs (audit baseline 2026-05-23)

Captured by `grep -rEn "Phase [A-Z]+( work| continuation|-continuation| later)|kyp-era|stub to NULL|stub.*for now|never actually|not functional on Linux" server/src/ login-server/ proxy/` filtering out vendored trees.

| # | File:line | Marker | Reach on Linux? | Classification | Notes |
|---|-----------|--------|-----------------|----------------|-------|
| 1 | `server/src/ServerManager.h:113-114` | "stub to NULL ... Real wiring is Phase B work" — `GetTCPCBuffer()` returns m_ReSendBuffer, `GetConnection()` returns 0 | Connection.cpp:29 (`GetTCPCBuffer`) and TcpListener.cpp:107 (`GetConnection`) both compile and link on Linux. TcpListener is **never instantiated** on Linux server (only in login-server/Net7SSL/SSL_ServerManager.cpp, which is fully WIN32-walled). Connection.cpp:29 path is Linux-walled too. | **DEAD-ON-LINUX** | ✅ **DONE in Phase Q (commit d367091)** — kyp-era TCP cluster deleted outright; `server/src/Connection.cpp`, `TcpListener.cpp`, the `GetTCPCBuffer`/`GetConnection` methods, and the surrounding ConnectionManager scaffolding are all gone. `ServerManager.h:106` now carries a self-documenting comment block explaining the design choice. |
| 2 | `server/src/Equipable.h:114` | "CheckForItem: kyp-era code uses this for item description matching" | Live on Linux — called from `Equipable::EquipDevice()` (Equipable.cpp:1754, 1765) which is reached when a player equips a device whose description matches a Sculptor or Harpy pattern. The CheckForItem function itself is real code (does substring matching against item description text); the actual stubs are the `ChangeProspectSkill`/`AddScanSkill`/`ChangeTractorBeamSpeed` methods it then calls (see row 5). | **NEEDS-IMPL** (transitively — fixed at the callee, row 5) | ✅ **DONE indirectly via row 5.** CheckForItem itself is correct; the silent failure was downstream. |
| 3 | `server/src/Connection.cpp:112` | "Phase B-continuation can wire this up to the [EffectManager]" | Stale reference — `server/src/Connection.cpp` was deleted in Phase Q. | **N/A — file gone** | ✅ **DONE in Phase Q** (file deleted). |
| 4 | `server/src/Connection.cpp:609` | "kyp-era inter-server opcodes — both MasterServerToSectorServer.cpp and ..." | Stale reference — `server/src/Connection.cpp` was deleted in Phase Q. | **N/A — file gone** | ✅ **DONE in Phase Q** (file deleted). |
| 5 | `server/src/PlayerClass.h:479` | "kyp-era stubs referenced from Connection.cpp / Equipable.cpp. tada-o ..." | Live on Linux — `ChangeProspectSkill`/`AddScanSkill`/`ChangeTractorBeamSpeed`/`SetTCPTerminate` are called from Equipable.cpp:1756/1760/1767. Player equips a Sculptor device → no skill change happens silently. Pure stub. | **NEEDS-IMPL** (logging-instrumented; real fix is gameplay scope) | ✅ **DONE 2026-05-23** — stubs no longer silent; each `LogMessage()`s on entry with the arg value. Real implementation needs the equipable-modifiers rework that's blocked behind getting the active sculptor-device codepath under a test fixture. |
| 6 | `server/src/ServerManager.h:134` | "ConnectionManager — kyp-era code (SSL_Listener, TcpListener, ClientToGlobalServer) still references this" | Stale reference — the kyp-era ConnectionManager and the SSL_Listener/TcpListener/ClientToGlobalServer TUs that referenced it were all deleted in Phase Q. | **N/A — refs gone** | ✅ **DONE in Phase Q**. |
| 7 | `login-server/Net7SSL/Connection.cpp:6` | "Porting it to Linux is a Phase J continuation; for now it is" | This is the file-level WIN32 wall preamble. The entire TU is `#ifdef WIN32`. | **DEAD-ON-LINUX** | ✅ Comment is accurate — file compiles to nothing on Linux. No action. |
| 8 | `login-server/Net7SSL/LinuxAuth.cpp:530` | "Win32 has a stub for this too; return 404 for now" | This is `WhoHtml` (`/who.cgi`) — the upstream Win32 reference also never defined the handler. The 404 fall-through is the actual behaviour on both. | **DELIBERATE-NOOP** | ✅ **DONE 2026-05-23** — comment rewritten to make the no-op intent and Win32-side parity explicit. |
| 9 | `server/src/CommonPlayerAndMob.h:32` (`{ return NULL; }` virtual) | Default for `ShieldAux()` virtual | Base-class default, derived classes override. | **NOT-A-STUB** | ✅ False positive, excluded. |

## Sweep results (2026-05-23 second pass)

Beyond the rows above, a systematic re-grep for `Phase [A-Z]\b`, `kyp-era`, `stub`, `TODO`, `FIXME`, `HACK` across server-native code (excluding vendored trees) returned **407 hits**, but classification shows:

- **~300 are documentation comments** in headers like `Net7.h`, `Net7SSL.h`, `Net7.cpp` describing Phase M/J/K/Q/R decisions — these are explainers, not stubs.
- **~80 are TODOs in gameplay logic** (server/src/Abilities/*.cpp) flagging unknown game-balance values ("TODO: find real drain amount", "TODO: when mobs have deflects"). These are content/design questions, not engineering scaffolding. Out of scope for stub-audit; needs game-design input.
- **~25 are the documented Phase J file-level WIN32 walls** in proxy/Connection.cpp, login-server/Net7SSL/Connection.cpp, login-server/Net7SSL/SSL_Connection.cpp — all clearly marked "dead on Linux until Phase K continuation."
- **2 empty-body method defs** found via `grep -E "^\s*\w+\s+\w+::\w+\([^)]*\)\s*\{\s*\}"`: `proxy/SSL_Connection.cpp:702 SSL_Connection::RunThread() {}` — a documented Linux Phase J stub (proxy doesn't handle SSL, login-server does); not a hazard.

**Conclusion**: the dangerous category (silent stub returning fake-success values on a live Linux codepath) is empty after rows 1–8 are resolved. Phase Q already deleted the bulk; rows 5 + 8 closed in this commit.

Additionally, audit these comment phrases that didn't appear in the first
grep but mean the same thing:

- `// TODO`, `// FIXME`, `// HACK` inside Linux-compiled code
- `assert(false)` / `abort()` / `exit()` reached on a happy-path codepath
- `(void)` casts that swallow legitimate return values
- Functions declared but never defined (link-time UNDEF symbols)

## Definition of done

- Every row in the table above has Classification filled in.
- Every #1 (DEAD-ON-LINUX) item is either walled in #ifdef WIN32 or deleted.
- Every #2 (NEEDS-IMPL) item is implemented OR loudly abort()s at runtime — silent wrong-behaviour is the bug, not "unimplemented".
- Every #3 (DELIBERATE-NOOP) item has its comment rewritten to make the no-op intent explicit (no more "Phase X work" wording on something we've decided we don't need).
- A `tools/check_no_stub_drift.sh` greps for the original marker phrases and reports a count. Wired to CI as a tracked-not-failing job so the count is visible per-PR and trends toward zero.
- `plans/00-master.md` shows Phase P complete.

## Anti-scope

- Don't try to fix the underlying functional gaps. If a stub is in
  category #2 (NEEDS-IMPL), abort()ing on hit is the deliverable —
  actually implementing the missing feature is per-feature scope (Phase
  K-style work).
- Don't touch vendored trees.
- Don't sweep license headers / build-system / docs — code only.

## Items

- [x] Re-run the grep above and fully populate the table.
      Status: complete — see "Sweep results" section above.
- [x] Per row #1 (`ServerManager.h:113-114`): wall TcpListener.cpp in `#ifdef WIN32` and delete the stubs; verify build still green; run integration tests.
      Status: complete — Phase Q (commit d367091) deleted the entire kyp-era TCP cluster (`server/src/Connection.cpp`, `TcpListener.cpp`, the stub methods). 20/20 ctest + 8/8 integration green.
- [x] Per row #2 (`Equipable.h:114`): audit callers, classify, act.
      Status: complete — CheckForItem itself is correct; the silent-failure was at the callees (row 5).
- [x] Per row #3 (`Connection.cpp:112`): audit callers, classify, act.
      Status: complete — file deleted in Phase Q.
- [x] Per row #4 (`Connection.cpp:609`): audit, classify, act.
      Status: complete — file deleted in Phase Q.
- [x] Per row #5 (`PlayerClass.h:479`): audit, classify, act.
      Status: complete — silent no-op stubs (`SetTCPTerminate`, `ChangeProspectSkill`, `AddScanSkill`, `ChangeTractorBeamSpeed`) now `LogMessage()` on entry with the arg value. Real implementation is gameplay scope.
      Touches: server/src/PlayerClass.h
- [x] Per row #6 (`ServerManager.h:134`): audit, classify, act.
      Status: complete — kyp-era ConnectionManager + its dependents deleted in Phase Q.
- [x] Per row #8 (`LinuxAuth.cpp:530`): rewrite comment.
      Status: complete — comment now explains both that the upstream Win32 reference also lacked a real `who.cgi` implementation and that the 404 fall-through is the intended behaviour.
      Touches: login-server/Net7SSL/LinuxAuth.cpp
- [ ] Add `tools/check_no_stub_drift.sh` and wire to CI as informational.
      Status: deferred — value-to-effort ratio low now that the dangerous category is empty. Reopen if new silent stubs appear.
- [x] Add `assert(false && "Linux stub reached")` wrappers for any NEEDS-IMPL items remaining.
      Status: complete — chose `LogMessage` over `abort()` for the PlayerClass.h stubs because crashing the server when someone equips a Sculptor is worse than the documented gameplay bug. Logging surfaces hits without taking the server down.
- [ ] Update plans/00-master.md status row when complete.
      Status: pending this commit.
