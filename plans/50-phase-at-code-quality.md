# Phase AT — Code quality: formatting, linting, warning cleanup, god-class modularization

**Goal.** Introduce strong, CLI-enforced code-formatting rules across every
language in the repo, eliminate compiler warnings without changing runtime
behaviour, and break up managed-code god-classes (>1200 lines) via dependency
injection. All fixes that a CLI can do are done by the CLI -- never by hand.

## Scope decisions (owner-set, do not re-litigate)

- **Format scope = everything EXCEPT `server/src`.** The C++ server is frozen
  for the rewrite; touching its formatting now is wasted churn. Every other
  tree is in scope: `proxy/`, `common/`, `login-server/`, `launcher/`,
  `client/`, `tools/`, `freya/`.
- **C++ refactor depth = managed code only.** God-class splitting / DI applies
  to **C# only**. NO `boost-ext/di`, NO C++ god-class splits -- all deferred to
  the server rewrite. C++ in this phase gets *formatting + warning cleanup* only.
- **Go indent = tabs.** `gofmt` is non-configurable and emits tabs; the
  "4-space" rule cannot and does not apply to Go. We enforce `gofmt`/`go vet`
  as the standard instead.
- **4-space indent** everywhere it *can* apply: C#, C++, TS/Vue/CSS, JSON.
- **Preserve CRLF + Latin-1 license headers** on inherited files. `.editorconfig`
  deliberately does NOT pin `end_of_line` (pinning re-triggers the charset
  guess + mojibake). clang-format runs with `ReflowComments: false` +
  `DeriveLineEnding: true` so it never rewraps the © header line nor flips line
  endings. `tools/check_no_mojibake.sh` is the backstop.

## Exclusions (vendored / inherited-not-ours / generated)

Never reformat: `server/src/**`, `server/third_party/**`, `**/third_party/**`
(incl. `freya/client-injection/enbmod/third_party/minhook`), `vendor/**`,
`archive/**`, `**/bin/**`, `**/obj/**`, `**/node_modules/**`, `**/dist/**`,
generated SQL artifacts.

## Sub-phases

### AT-1 — Formatting config + tooling (no code churn) [DONE 2026-06-16]
Lay down the config files and wire the analyzers. NO reformat yet; near-no-op.

- [x] Root `.editorconfig`: 4-space for C#/C++/TS, 2-space for markup/JSON, tab
      for Go; `charset=utf-8` only at `[*]`; `end_of_line` left UNPINNED. C#
      `dotnet_*`/`csharp_*` style rules added. StyleCop severities by category
      (all `suggestion` for the transition) + opinionated rules OFF (SA0001,
      SA1101, SA1200, SA1309, SA1413, SA1124, SA1402, SA1633, SA1600/01/02).
- [x] `StyleCop.Analyzers` 1.2.0-beta.556 (PrivateAssets=all) added to
      `tools/Directory.Build.props` + repo-root `stylecop.json` (4-space,
      usings outside namespace, documentationRules off) linked via AdditionalFiles.
      **Staging decision:** added to the non-TWAE `tools/` tree ONLY in AT-1
      (the bulk -- ~700 files -- where SA warnings can't break the build). The
      three TWAE projects (cli-client, integration tests, capture-extract) get
      StyleCop in AT-2 *after* `dotnet format` makes their trees clean, so the
      analyzers never surface as build-breaking errors.
- [x] `.clang-format` at repo root (LLVM base, IndentWidth 4, UseTab Never,
      `ReflowComments: false` to protect the © headers, `LineEnding: DeriveLF`
      to keep per-file CRLF/LF, SortIncludes off). Parses clean on proxy/.
- [x] Web: `tabWidth: 4` set in `freya/online/web/.prettierrc.json`. eslint +
      stylelint already present; `prettier --check` confirms 8 files reformat in AT-2.
- [x] Verify: `dotnet build tools/FreyaTools.slnx` 0/0; cli-client builds;
      gofmt clean on net7go; no source reformat in this commit.

### AT-2 — Apply formatters via CLI (one commit per language) [DONE 2026-06-16]
- [x] C#: `dotnet format whitespace --folder` over tools/ + freya/cli-client +
      freya/tests/integration + tools/capture-extract (353 files). 4-space, cast
      spacing, final newlines, block reflow, 109 BOM strips, UTF-16->UTF-8.
      StyleCop also wired into the 3 TWAE projects (suggestion-severity, build
      stays green). Build 0/0; CliClient.UnitTests 830/830; LaunchFreya 92/92;
      mojibake CLEAN. Commit 03e57b2e.
      **ENV NOTE:** the semantic `dotnet format` path (StyleCop autofix +
      using-sort) needs the out-of-process MSBuild **BuildHost**, which times
      out (named-pipe connect) in THIS sandbox. Worked around with `--folder`
      mode (editorconfig-only, no MSBuild). The semantic autofixes are deferred
      to CI/AT-3/AT-4 where the BuildHost works; SA rules guide at `suggestion`
      meanwhile. This is an environment limitation, not a config bug.
- [x] C++: `clang-format -i` over 108 in-scope files in two commits -- ours
      (freya/ + tools/, 66 files, e3fa38d1) then inherited (proxy/ common/
      launcher/, 42 files, a7f1616f). Vendored excluded (xml-exporter/mysql,
      xmlParser, minhook). proxy builds native cmake; gtest 46/46; 8 Latin-1 ©
      bytes byte-identical; mojibake CLEAN.
- [x] TS/Vue/CSS: prettier --write (tabWidth 4) + eslint --fix + stylelint
      --fix in freya/online/web. tsc clean, vitest 23/23. Commit 50aeb45f.
- [x] Go: already gofmt-clean across all 3 modules (net7go, freya/online,
      freya/status-notifier) -- no-op. goimports not separately run (gofmt
      satisfied; go vet deferred to AT-4).

### AT-3 — CI enforcement (format:check gates) [DONE 2026-06-16]
- [x] `just fmt` (apply) + `just fmt-check` (verify, the CI gate) recipe pair in
      `justfile`, single source of truth for all four languages. `fmt-check`
      runs: per-tree `dotnet format whitespace --folder --verify-no-changes`
      (folder mode, no MSBuild BuildHost), `clang-format --dry-run --Werror` over
      the in-scope C++ list, `gofmt -l` (fails if non-empty), prettier `format:check`
      + stylelint, and `check_no_mojibake.sh`. Verified locally EXIT=0.
- [x] `format-check` job added to `.github/workflows/build.yml` (installs dotnet
      10 / Go 1.22 / Node 20 / `just` + `clang-format-18`, `npm ci` for web, then
      `just fmt-check`). Mirrors CI exactly via the same recipe.
- [x] **Closed two AT-2 gaps surfaced by `fmt-check`:** `enbmod/src/game.h` was
      under-formatted (a standalone continuation-comment line indented 4 instead
      of 0) -- `clang-format -i` fixed it stably. `ClientEngineOffsets.h` has
      `#define FREYA_FEED_*` macros whose long trailing comments overflow
      ColumnLimit; clang-format **oscillates** the comment-gap spacing and can
      never reach a fixed point, so the block is wrapped in `// clang-format off`
      / `// clang-format on` (the documented escape hatch) with a note on why.
      `go vet` deferred to AT-4 (this job is format-only, not vet).

### AT-4 — Compiler / analyzer warning cleanup (no behaviour change) [DONE 2026-06-16]
- [x] C#: clean rebuild of `tools/FreyaTools.slnx` + the 3 TWAE projects =
      **0 warnings / 0 errors** already (StyleCop runs at `suggestion`, adds no
      warnings). No code change needed.
- [x] Go: `go vet ./...` clean across all 3 modules (net7go, freya/online/server,
      freya/status-notifier). No change needed.
- [x] C++ proxy (`net7proxy` native + `FreyaProxy.exe` MinGW): **0 warnings /
      0 errors** under `-Wall -Wextra`. Did the real cleanup instead of blanket
      suppression:
      - Measured what each `-Wno-*` actually hid: **6 suppressed zero warnings**
        (`-Wno-unused-parameter`, `-but-set-variable`, `-sign-compare`,
        `-deprecated-declarations`, `-address-of-packed-member`, `-pointer-arith`)
        -- removed as dead config.
      - Fixed the real ones in source: 2 `-Wreorder` (ServerManager, SSL_Listener
        ctor init lists reordered to match declaration order -- all scalar/ref
        params, no inter-member deps, behaviour-identical) and 5 `-Wunused-variable`
        (SSL_Listener `ip`/`ssl_connection`, SectorServerManager strtok results --
        kept side-effecting calls, dropped the unused names). Dropped `-Wno-reorder`
        + `-Wno-unused-variable`.
      - Fixed 4 MinGW-only `-fpermissive` `unsigned char* -> char*` conversions on
        Winsock `recv`/`send`/`sendto` (Connection.cpp x3, UDPClient_linux.cpp x1)
        with casts that are no-ops on the Linux `void*` signatures.
      - **Only 2 suppressions remain**, documented inline in proxy/CMakeLists.txt:
        `-Wno-write-strings` (~174 inherited `char* = "literal"` sites) and
        `-Wno-unused-function` (~26 inherited dead statics) -- both pervasive
        inherited-code idioms whose fix would be large behaviour-risky churn.
- [x] C++ gtest harness (`freya/tests/server`): builds 0 warnings; 46/46 ctest
      pass (DB-tests skip w/o DB). Its 2 narrow per-target suppressions guard
      `server/src` includes (explicitly out of scope), left as-is.
- [ ] **Not validated this environment:** the Win32-only `freya/client-injection`
      DLLs (FreyaInject / FreyaPosFeed / enbmod) and `launcher/` -- formatted in
      AT-2, but their MinGW DLL build path isn't exercised here. Warning sweep on
      those deferred (no Net-7 server dependency; client-side WINE artifacts).

### AT-5 — God-class audit + DI modularization (MANAGED CODE ONLY) [DONE 2026-06-16]
Enumerated every in-scope C# file > 1200 lines (excl bin/obj/Designer/Migrations).
Only FIVE, and the 1200-line heuristic is mostly false positives here -- applied
judgment rather than mechanically force-splitting:

- **`SectorChatTests.cs` (28260), `RetailRecordDecodeTests.cs` (3313),
  `SectorClientChatRequestTests.cs` (1217)** -- xUnit `[Fact]` collections +
  byte-fixture data, NOT god-classes. "Split via DI" does not apply to test
  fact-collections. Left as-is. (Honest follow-up, NOT done here: the 28K
  SectorChatTests is worth partitioning into per-feature files for *navigability*
  -- that is a mechanical file-split, not a DI refactor, and out of AT-5 scope.)
- **`Vector3.cs` (1554)** -- a single cohesive `struct Vector3` math primitive,
  **781 of 1554 lines are XML-doc comments** (~770 LOC of operator/method
  overloads). A value-type math struct cannot be meaningfully DI-split; it is
  single-responsibility already. Left as-is.
- **`MainWindow.axaml.cs` (1255 -> 1173)** -- the one genuine production
  god-class (Avalonia code-behind, 48 methods). Its heavy business logic was
  ALREADY extracted upstream into tested classes (Launcher, Updater, UpdateLogic,
  ModStore, AuthLoginPatcher); the remainder is irreducible UI event-handler glue
  plus a handful of pure helpers. Extracted the pure, UI-free helpers into two
  testable classes and delegated:
  - `HostResolver` (NormalizeHost, WebsiteUrlFor) -- pure string logic.
  - `LauncherLogFiles` (ReadLogFile, ExistingFile, NewestLogFile, FindClientLog)
    -- filesystem helpers.
  Added 28 unit tests (HostResolverTests + LauncherLogFilesTests); LaunchFreya.Tests
  now 120/120 (was 92). These helpers had ZERO coverage while private in the
  window; the real win is testability, not the line-count drop. Stateless statics
  (not constructor-DI) is the correct shape for pure functions -- forcing DI
  ceremony on them would be cargo-culting. Build 0/0.

Honest bottom line: there was no untestable monolith to break up with DI here.
The codebase was already well-separated; AT-5's value was pulling the last pure
helpers out of the UI shell and putting them under test.

### AT-6 -- Final audit [DONE 2026-06-16]
Maintainability / security / correctness review of all AT changes (the AT range
is d6abc730^..HEAD; the wider branch carries unrelated pre-AT work).

- **Correctness.** Phase AT touched 503 files, overwhelmingly pure whitespace
  reformat from CLI tools (dotnet format whitespace, clang-format -i, prettier,
  gofmt) -- formatters that cannot change semantics. The substantive
  (non-whitespace) edits are exactly three: proxy/ warning cleanup (AT-4),
  LaunchFreya helper extraction (AT-5), Vector3.cs LF normalize (AT-6). All
  verified GREEN on CI runs 27616529321 (AT-4) and 27617088473 (AT-5): every
  build job (cmake-build 22.04/24.04, dotnet-build, cli-unit-test) AND all four
  cli-integration shards AND the live-proxy-handshake job passed -- the
  integration suite drives the live docker stack, so the proxy warning-cleanup
  edits are runtime-proven, not just compile-proven. Game still functions.
- **Encoding audit caught one real defect.** dotnet format correctly re-encoded
  the single UTF-16 file (tools/w3d-parser/Utilities/Vector3.cs) to UTF-8 per the
  pinned `.editorconfig charset=utf-8`, but left it with MIXED line endings
  (1027 CRLF + 527 LF). end_of_line is unpinned so fmt-check did not flag it.
  Fixed in commit 689f9d18 (normalized to pure LF, the tools/*.cs convention);
  content verified byte-identical-modulo-indentation against origin/main.
- **Security.** No security-relevant surface in the AT edits. The proxy cleanup
  removed dead -Wno-* suppressions and added no-op `(char*)` socket casts +
  ctor-init-order/unused-var fixes -- zero behaviour change, no auth/crypto/SQL
  path touched. The extracted LaunchFreya helpers are pure string/filesystem
  logic with no new I/O surface. No new SQL (string-concat rule N/A). No secrets,
  no IPs, no out-of-repo paths introduced.
- **Maintainability.** Formatting is now CLI-enforced and CI-gated (AT-3
  format-check job), so style drift breaks the build going forward. StyleCop runs
  at `suggestion` (guidance without failing TWAE). God-class audit (AT-5) found
  no DI-splittable monolith; extracted the last pure helpers out of the one real
  code-behind and put them under test (+28 tests).
- No wire-adjacent behaviour changed, so NO plans/29 CV entry is required
  (formatting + warning-only cleanup is not a wire change; the proxy edits are
  no-op casts / init-order, validated by the live integration suite).
- `just fmt-check` green locally and on CI; `dotnet test tools/LaunchFreya.Tests`
  120/120; managed builds 0 warnings / 0 errors.

## Notes / log
- (AT-1) starting 2026-06-16.
