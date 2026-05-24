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
| 1 | `server/src/ServerManager.h:113-114` | "stub to NULL ... Real wiring is Phase B work" — `GetTCPCBuffer()` returns m_ReSendBuffer, `GetConnection()` returns 0 | Connection.cpp:29 (`GetTCPCBuffer`) and TcpListener.cpp:107 (`GetConnection`) both compile and link on Linux. TcpListener is **never instantiated** on Linux server (only in login-server/Net7SSL/SSL_ServerManager.cpp, which is fully WIN32-walled). Connection.cpp:29 path is Linux-walled too. | **DEAD-ON-LINUX** | Wall TcpListener.cpp at file level (#ifdef WIN32) and delete the stubs from ServerManager.h. Or: keep the field but rename the comment to flag the trap explicitly. |
| 2 | `server/src/Equipable.h:114` | "CheckForItem: kyp-era code uses this for item description matching" | Audit pending | TBD | Need to grep callers, see if reached on Linux. |
| 3 | `server/src/Connection.cpp:112` | "Phase B-continuation can wire this up to the [EffectManager]" | Audit pending | TBD | |
| 4 | `server/src/Connection.cpp:609` | "kyp-era inter-server opcodes — both MasterServerToSectorServer.cpp and ..." | Audit pending | TBD | |
| 5 | `server/src/PlayerClass.h:479` | "kyp-era stubs referenced from Connection.cpp / Equipable.cpp. tada-o ..." | Audit pending | TBD | |
| 6 | `server/src/ServerManager.h:134` | "ConnectionManager — kyp-era code (SSL_Listener, TcpListener, ClientToGlobalServer) still references this" | Audit pending | TBD | |
| 7 | `login-server/Net7SSL/Connection.cpp:6` | "Porting it to Linux is a Phase J continuation; for now it is" | This is the file-level WIN32 wall preamble. The entire TU is `#ifdef WIN32`. | **DEAD-ON-LINUX** | No action needed — comment is accurate. Cross off. |
| 8 | `login-server/Net7SSL/LinuxAuth.cpp:530` | "Win32 has a stub for this too; return 404 for now" | This is `WhoHtml` (`/who.cgi`) — already known: the Win32 reference returns nothing either ("WhoHtml never defined" per phase K notes). | **DELIBERATE-NOOP** | Rephrase comment: "Win32 implementation is also a no-op (function declared, never defined). Returning 404 matches existing behaviour." |
| 9 | `server/src/CommonPlayerAndMob.h:32` (`{ return NULL; }` virtual) | Default for `ShieldAux()` virtual | Base-class default, derived classes override. | **NOT-A-STUB** | False positive — exclude from audit. |

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

- [ ] Re-run the grep above and fully populate the table.
- [ ] Per row #1 (`ServerManager.h:113-114`): wall TcpListener.cpp in `#ifdef WIN32` and delete the stubs; verify build still green; run integration tests.
- [ ] Per row #2 (`Equipable.h:114`): audit callers, classify, act.
- [ ] Per row #3 (`Connection.cpp:112`): audit callers, classify, act.
- [ ] Per row #4 (`Connection.cpp:609`): audit, classify, act.
- [ ] Per row #5 (`PlayerClass.h:479`): audit, classify, act.
- [ ] Per row #6 (`ServerManager.h:134`): audit, classify, act.
- [ ] Per row #8 (`LinuxAuth.cpp:530`): rewrite comment.
- [ ] Add `tools/check_no_stub_drift.sh` and wire to CI as informational.
- [ ] Add `assert(false && "Linux stub reached")` wrappers for any NEEDS-IMPL items remaining.
- [ ] Update plans/00-master.md status row when complete.
