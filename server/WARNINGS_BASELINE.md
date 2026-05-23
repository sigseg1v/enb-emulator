# Server build warning baseline (Phase F)

## Methodology

The full server-side warning load can't be exactly measured in this
environment (the build needs `liblua5.4-dev` / `libmysqlclient-dev` /
`libtinyxml-dev` system packages that aren't installed). Instead, the
inventory below was gathered by trial-compiling a representative
20-file sample of `server/src/*.cpp` directly with `g++ -c`, against
the *same* flags `server/CMakeLists.txt` uses **but with the
suppression flags removed**, so the raw warning categories surface.

Sample (20 of 91 TUs ≈ 22%):

```
AccountManager AssetDatabaseSQL CMobClass Connection ConnectionManager
EffectManager Equipable GroupManager GuildManager ItemBaseManager Net7
Object Player PlayerCombat SectorManager ServerManager WestwoodRSA
SSL_Connection SSL_Listener MobAI
```

Command:

```sh
cd server
g++ -c -DOPENSSL_API_COMPAT=0x30000000L -DUSE_OPENSSL -DLINUX -D__linux__ \
    -Wall -Wextra -fpermissive \
    -Icompat -Isrc -Ithird_party -Isrc/LUA/lua/include \
    src/<file>.cpp -o /tmp/<file>.o
```

## Initial baseline (20 TUs, 3280 warnings)

| Count | Category | Notes |
|---:|---|---|
| 1379 | `-Wunused-parameter` | Endemic. Existing virtuals/callbacks declare params they don't use. Globally suppressed in CMake. Fixing safely requires either `[[maybe_unused]]` annotations everywhere or unnamed parameters — both are pure-noise changes. |
| 1140 | `-Wwrite-strings` | Legacy `char *` params taking string literals. Fixing safely requires a const-correctness cascade through all the C-string APIs. Globally suppressed. |
| 296 | `-Wunused-variable` | Mostly diagnostic locals like SQL result counters. Globally suppressed. |
| 245 | (no flag) `ignoring packed attribute because of unpacked non-POD field` | Network/save structs use `__attribute__((packed))` but embed non-POD C++ containers. **Not a flag, not suppressible** — emitted as part of structural codegen. Real fix is to split the wire structs from the runtime structs, which is a separate refactor. |
| 53 | `-Wmisleading-indentation` | **Fixed** in Phase F (3 sites in `VectorMath.h` + 1 in `ServerManager.cpp`; the 53 count was due to header inclusion fan-out). |
| 35 | (no flag) `extra qualification` | `class Foo { void Foo::bar(); };` form. Tolerated by `-fpermissive`. |
| 31 | `-Wunused-function` | File-scope helpers that became dead during the kyp→tada-o merge. Worth a once-over. |
| 30 | `-Waddress-of-packed-member` | Globally suppressed (Phase B note in CMakeLists explains why). |
| 24 | `-Wsign-compare` | Globally suppressed. |
| 7 | `-Waddress` | "Address of X will never be NULL" — guards on array-typed members (`obj.array_field != NULL`). Five real sites. |
| 6 | `-Wreorder` | Globally suppressed. |
| 5 | `-Wparentheses` | **Fixed** in Phase F (5 sites; assignment-in-condition, added explicit parens). |
| 5 | `-Wchar-subscripts` | `char` used as array index — five sites in `PlayerCombat.cpp::GetBoneName`. Real bug-bait on signed-char platforms; worth fixing in a follow-up by casting to `unsigned char`. |
| 4 | `-Wextra` (uncategorised) | One-offs. |
| 3 | `-Wunknown-pragmas` | MSVC `#pragma warning(...)` lines. Cosmetic. |
| 3 | `-Wmissing-field-initializers` | Aggregate inits leaving trailing fields default. Cosmetic. |
| 2 | `-Wformat=` | `printf("%d", long_int)` → use `%ld`. Two sites, one in `Net7.cpp`. Worth fixing. |
| 1 | `-Wunused-but-set-variable` | `server_name` in `Net7.cpp:105` assigned then never read. |
| 1 | `-Wnonnull-compare` | `Connection.cpp:287` checks `this != NULL`. Undefined behaviour by the standard — worth fixing. |

## What Phase F did

- **`-Wmisleading-indentation` → 0** in the sample. Fixed 4 sites:
  - `server/src/VectorMath.h:409–414` (`Box::update` overloads): broke
    multiple-statements-per-line `if` chains onto separate lines.
  - `server/src/ServerManager.cpp:475`: added braces around a `for`
    body containing a single `if` that itself contained a brace block.
- **`-Wparentheses` → 0** in the sample. Fixed 5 sites in
  `Equipable.cpp` (×2) and `ItemBaseManager.cpp` (×3): wrapped
  assignment-in-condition with explicit parentheses. Behaviour
  unchanged; intent now explicit.

## Suppressions in `server/CMakeLists.txt`

Unchanged in Phase F (cleaning them out would re-flood the log;
each is suppressing a real cascade that needs its own pass):

```
-Wno-unused-parameter
-Wno-unused-variable
-Wno-sign-compare
-Wno-reorder
-Wno-deprecated-declarations    # see Phase E; can be lowered later
-Wno-address-of-packed-member
-Wno-write-strings
-fpermissive
```

`-Wno-deprecated-declarations` was kept for now even though Phase E
made the server clean against deprecation. Reason: cryptopp 8.x is
known to deprecate APIs older Net-7 code calls (e.g. `RandomPool`),
and Phase F didn't audit those.

## What's left (Phase F continuation)

Small categories, all in the 1–35 range, each tractable in <hour:

| Category | Count | Suggested fix |
|---|---:|---|
| `-Wchar-subscripts` | 5 | Cast subscripts to `unsigned char` in `PlayerCombat.cpp::GetBoneName`. |
| `-Waddress` | 7 | Drop the meaningless `!= NULL` checks on array-typed fields. |
| `-Wformat=` | 2 | Change `%d`/`%ld` mismatches. |
| `-Wnonnull-compare` | 1 | Remove the `this != NULL` check in `Connection.cpp:287` (UB by the spec). |
| `-Wunused-function` | 31 | Audit; either delete or `[[maybe_unused]]`. |
| `-Wunused-but-set-variable` | 1 | Trivial. |
| (no flag) `ignoring packed attribute` | 245 | Structural refactor: split wire structs (`#pragma pack`) from runtime structs (`std::vector`/`std::map` members). Multi-day. |

Top three suppressed categories (`-Wunused-parameter`, `-Wwrite-strings`,
`-Wunused-variable`) are pure-noise cascades — not worth bulk-fixing
ahead of subsystem-by-subsystem refactors that will rewrite the call
sites anyway.

## Verification

After the Phase F fixes, re-trial-compiling the same 20-file sample
produced **0 misleading-indentation, 0 parentheses** warnings (down
from 53 + 5 = 58). Total warning count dropped from 3280 → 3227 — the
gross number is dominated by the three globally-suppressed categories
that this phase intentionally did not chase.
