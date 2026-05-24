# Phase Q — Delete dead kyp-era TCP cluster from server/src

**Status**: complete (2026-05-23)
**Supersedes**: Phase P (which made the same stubs loud-abort as a stopgap)

## Motivation

The user pushed back on the Phase P pattern of `#ifdef WIN32` walling / loud `abort()` stubs:

> why is there no connection on linux? do we need it? cant we just implement what we need in the proper order instead of commenting everything out

They were right. tada-o's split-process design moves TCP termination into `proxy/` (binds `GLOBAL_SERVER_PORT` 3805) and SSL handling into `login-server/Net7SSL/`. `server/src/` runs purely as a UDP-fed sector/master logic core. `ServerManager.cpp:142-153` shows every `new TcpListener` / `new SSL_Listener` / `new UDPListener` call commented out. The kyp-era TCP classes were leftover scaffolding that compiled but were never instantiated on Linux at runtime. The right answer is delete them, not wall them.

## Architectural rationale (the user's question: is the split worth it?)

**Benefits that justify the split:**
- Crash isolation between proxy and sector-server
- Independent restart / rolling upgrade
- Per-process OpenSSL ABI (lets proxy stay on 1.1 while server moves to 3.x — Phase E/O)
- Internet attack surface confined to proxy
- N-sector fan-out from one proxy

**Risks (manageable):**
- UDP desync between proxy↔sector — mitigated by `m_ReSendBuffer` + `m_UDPSendBuffer` at the application protocol level. Whether the resend logic is correct is a Phase G concern, not an architecture concern.
- Critical opcode drops — same mitigation.
- Latency — sub-ms on loopback, negligible.

**Real un-mitigated cost:** header duplication. `proxy/Net7.h:139` and `login-server/Net7SSL/Net7SSL.h:174` both define `GLOBAL_SERVER_PORT 3805`. PacketStructures.h, Opcodes.h, Net7.h are triplicated. Verified: opcodes have already drifted between `proxy/Opcodes.h` and `server/src/Opcodes.h`. → Opens Phase R.

## What was deleted

15 files, all part of the kyp-era flat single-process TCP design, never instantiated on Linux:

- `Connection.cpp` / `Connection.h`
- `ConnectionManager.cpp` / `ConnectionManager.h`
- `TcpListener.cpp` / `TcpListener.h`
- `SSL_Listener.cpp` / `SSL_Listener.h`
- `SSL_Connection.cpp` / `SSL_Connection.h`
- `ClientToGlobalServer.cpp`
- `ClientToMasterServer.cpp`
- `ClientToSectorServer.cpp`
- `EffectManager.cpp` / `EffectManager.h` — verified zero live callers (`grep "EffectManager"` hits were unrelated `Player::SendObjectEffect` and `Object::SendObjectEffects` methods that share name fragments)
- `JobManager_DEP_.h` — orphaned; nothing included it

## What was edited

- `server/src/ServerManager.h`:
  - Removed `#include "ConnectionManager.h"`
  - Removed `class SSL_Listener;` forward decl
  - Removed `SSL_Connection *GetSSLConnection();` declaration
  - Removed the `ConnectionManager m_ConnectionMgr;` value-typed field
  - Removed the Phase-P `GetConnection()` / `GetTCPCBuffer()` loud-abort stubs entirely; replaced with a comment pointing future readers at `proxy/ServerManager.cpp` as the real TCP entry point
- `server/src/ServerManager.cpp`:
  - Removed `#include "SSL_Connection.h"`
  - Removed the dead `#if 0 ... #endif` block around `GetSSLConnection()`

## What was kept (despite misleading naming)

- `PlayerConnection.cpp` — 11225 lines. **Live UDP send layer for Player methods**: `Player::SendOpcode`, `Player::SendObjectEffect`, `Player::SendAdvancedPositionalUpdate`, `Player::SendConfirmation`, etc. Called from `Abilities/`, `MOBClass`, `GroupManager`, `GuildManager`, `HuskClass`, `FieldClass`. Initial deletion attempt produced ~50 undefined-reference linker errors; restored.
- `UDPConnection.cpp` / `UDPConnection.h` — the actual UDP transport used by `m_UDPConnection`.

## Verification

- `cmake --build build` → `[2/2] Linking CXX executable net7` (green)
- `git ls-files server/src/ | wc -l` decreased by 16 (15 deleted, JobManager_DEP_.h orphan)
- ServerManager.h is ~47 lines shorter

## Lessons / follow-ups

1. **Don't grep for filenames to decide if a translation unit is dead.** Grep for the *symbols defined inside it*. PlayerConnection.cpp had no callers by name but its `Player::Send*` methods had dozens.
2. **Phase R candidate identified**: extract `common/` subtree for the triplicated protocol headers. Opcodes have already drifted between proxy and server — this is a wire-format-corruption time bomb if left alone.
3. The Phase-P approach (walling + loud abort()) is the wrong default. When a code path is dead, delete it; when it's broken-but-needed, fix it properly; only wall/abort if there's a real ownership transfer happening in a single commit window and the alternative is link failure.
