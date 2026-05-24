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

- [ ] Inventory of duplicated headers
      Status: not started
      Touches: proxy/, server/src/, login-server/Net7SSL/
- [ ] Per-file diff classification
      Status: not started
- [ ] Create `common/include/net7/` directory + CMake INTERFACE library
      Status: not started
- [ ] Move winners into `common/`, update includes
      Status: not started
- [ ] Drop per-process duplicates
      Status: not started
- [ ] Update CLAUDE.md + docs/02-architecture.md to point at `common/`
      Status: not started
- [ ] All three subprojects build clean on Linux
      Status: not started
- [ ] Integration tests pass (5/5)
      Status: not started

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
