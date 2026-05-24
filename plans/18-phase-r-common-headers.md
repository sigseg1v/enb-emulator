# Phase R — Extract `common/` for shared protocol headers

**Status**: in progress
**Opened**: 2026-05-23 (Phase Q follow-on)
**Why**: tada-o's split-process design is architecturally sound, but it has one real un-mitigated cost — wire-format headers are triplicated across `proxy/`, `server/src/`, and `login-server/Net7SSL/`, and they have already drifted. If `proxy/Opcodes.h` and `server/src/Opcodes.h` define different values for the same opcode mnemonic, the proxy will route packets to the wrong handlers.

## Confirmed drift (smoking gun)

```
$ diff <(grep -h "^#define ENB_OPCODE\|_PORT\b" proxy/Opcodes.h proxy/Net7.h | sort -u) \
       <(grep -h "^#define ENB_OPCODE\|_PORT\b" server/src/Opcodes.h server/src/Net7.h | sort -u)
```

Returns a large diff. Need to enumerate which definitions actually conflict (vs. which only exist in one tree) and triage.

## Scope

Files to consolidate (currently triplicated or duplicated):

| File | proxy/ | server/src/ | login-server/Net7SSL/ |
|---|---|---|---|
| Net7.h (port + tunable defs) | yes | yes | yes (renamed Net7SSL.h) |
| Opcodes.h | yes | yes | — |
| PacketStructures.h | yes | yes | yes |

Likely additional candidates surfaced during work: `Globals.h`, `Mutex.h`, `CircularBuffer.h`, `cmdcodes.h`, `Net7Types.h`.

## Approach

1. **Inventory**: list every header that exists in ≥2 of {proxy, server/src, login-server/Net7SSL}.
2. **Diff each pair**: classify as (a) identical, (b) drifted in a way that's safe to unify (one is stale), (c) drifted because the trees genuinely need different definitions.
3. **Pick winner per file**: for class (b), choose the authoritative version (usually the most recent / most-actively-edited). For class (c), keep the per-process header but namespace its conflict-prone bits (or simply document the divergence).
4. **Move winners into `common/include/net7/`**: a new top-level subtree that all three CMakeLists.txt files add as an `INTERFACE` library / include directory.
5. **Drop the duplicates** from `proxy/`, `server/src/`, `login-server/Net7SSL/`. Replace `#include "Net7.h"` with `#include <net7/Net7.h>`.
6. **Build all three**: ensure `cmake --build build` in `/server/`, `/proxy/`, `/login-server/` all stay green.
7. **Integration tests**: re-run the live integration suite (5/5 currently in Phase J) to confirm no wire-format regression.

## Checklist

### Wave 1 — utility + opcode headers (done 2026-05-23, commit 779a277)

- [x] Inventory of duplicated headers
      Status: done
      Notes: confirmed Mutex.h, Opcodes.h, WestwoodRC4.h, WestwoodRSA.h, PacketStructures.h, Net7.h(/Net7SSL.h) duplicated; lots of others (Globals.h, CircularBuffer.h, cmdcodes.h, Net7Types.h) also overlap but are deferred to a later wave.
- [x] Per-file diff classification for Wave 1 set
      Status: done
      Notes: Opcodes.h — server is strict superset of proxy (adds INVENTORY_SORT 0x0028, FIND_MEMBER 0x0053, CHAT_LIST/ERROR 0x00A4/A6, guild opcodes 0xC5-D5, WAIT_AUX 0x3000, AUX_RESPONSE 0x3001; one rename: ACCOUNTVALIDATE→ACCOUNTVALID at same value 0x2001). Server wins. Mutex/WestwoodRC4/WestwoodRSA: server versions carry the CC BY-NC-SA 3.0 license header that the other two trees stripped at some point — server wins on both correctness and license-header preservation grounds.
- [x] Create `common/include/net7/` directory
      Status: done
      Notes: bare include dir (no INTERFACE library needed yet — directly added to each target's PRIVATE include dirs in their respective CMakeLists.txt).
- [x] Move Wave 1 winners into `common/`, update includes (~43 source files)
      Status: done
      Notes: bulk sed of `#include "<Header>.h"` → `#include <net7/<Header>.h>` across proxy/, server/src/, login-server/Net7SSL/. Two relative-path includes needed manual fix (server/src/mysql/mysqlplus.h, server/src/AuxClasses/AuxBase.h had `#include "../Mutex.h"`).
- [x] Drop Wave 1 per-process duplicates
      Status: done
      Notes: `git rm` on the 7 dupes (proxy/{Opcodes,Mutex,WestwoodRC4,WestwoodRSA}.h + login-server/Net7SSL/{Mutex,WestwoodRC4,WestwoodRSA}.h; server/src copies became `common/` via rename).
- [x] All three subprojects build clean on Linux (Wave 1)
      Status: done
      Notes: net7 [169/169], net7proxy [23/23], net7ssl [18/18] all linked successfully after Wave 1.

### Wave 2 — PacketStructures + port macros (done 2026-05-23, commit b6c43ac)

- [x] PacketStructures.h per-struct merge
      Status: done
      Notes: server's copy (94 structs, full license header) chosen as merge base. Audit confirmed proxy's 10 missing structs are all server-only consumers on Linux (proxy lives consumers: only VersionRequest/MasterJoin/ServerRedirect, the handshake structs). Phase K int32_t fixes backported into the unified file. login's copy was 93/94 structs (missing InvSort only); easy fit. Extracted ATTRIB_PACKED + u8/u16/.../BSTR into `common/include/net7/Packing.h` so the unified header doesn't need to pull per-process Net7.h.
- [x] Extract port macros from Net7.h into common/include/net7/Ports.h
      Status: done
      Notes: smoking gun caught — proxy/Net7.h had `SECTOR_SERVER_PORT 3500`, server+login had `SECTOR_SERVER_PORT 3501`. Comment in original Net7SSL.h explained the split: proxy binds 3500 locally and forwards to 3501+ sector servers. The same macro NAME meant two different things in two trees. Resolved by splitting into PROXY_LOCAL_TCP_PORT (3500) and SECTOR_SERVER_PORT (3501); proxy's 9 call sites rewritten to PROXY_LOCAL_TCP_PORT (it was semantically the proxy-local port, not the canonical sector port). All 3 Net7.h headers now `#include <net7/Ports.h>` instead of duplicating macros.
- [x] Integration tests pass after Wave 2 (8/8 from running docker proxy + 14/14 unit, total 22/22 green)
      Status: done
      Notes: test client + running proxy interop'd cleanly (wire format intact across the Wave 2 client-side rebuild). Local docker proxy container reuse meant we couldn't validate the post-Wave-2 *proxy binary*, but the WireFormat.* tests check struct sizes/offsets at compile time via static_assert, and those passed.
- [ ] Update CLAUDE.md + docs/02-architecture.md to point at `common/`
      Status: not started

### Wave 3 — backlog (deferred — Phase R can close after Wave 2 wire-format wins, these are cleanups)

- [ ] Consolidate the remaining duplicated headers: Globals.h, CircularBuffer.h, cmdcodes.h, Net7Types.h
      Status: backlog
      Notes: lower priority — these are NOT wire-format headers. Audit drift, classify, merge if low-effort.

## Non-goals

- Not collapsing the three processes back into one. The split is the right architecture (see plans/17 §"Architectural rationale").
- Not refactoring the protocol itself. This is mechanical de-duplication only.
- Not touching `client/`, `tools/`, or `server/third_party/` — out of scope per established Phase M boundary.

## Exit criteria

- `find . -name 'Opcodes.h' -not -path './common/*'` returns zero (or only documented per-process variants with a `// LOCAL: ...` justification).
- Same for `Net7.h`, `PacketStructures.h`.
- All three subprojects link.
- Integration tests still green.
- `docs/02-architecture.md` has a §"Wire-format header layout" section documenting `common/`.
