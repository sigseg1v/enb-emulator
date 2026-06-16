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

### AT-2 — Apply formatters via CLI (one commit per language) [not started]
- [ ] C#: `dotnet format tools/FreyaTools.slnx` (whitespace + style +
      analyzers) then the cli-client solution. Build + smoke + unit tests green.
- [ ] C++: `clang-format -i` over in-scope dirs (proxy, common, launcher,
      client, freya C++, tools C++ EXCL vendored). Server gtest + proxy build
      green; mojibake check clean.
- [ ] TS/Vue/CSS: `npm run format` + `eslint --fix` in `freya/online/web`.
      vitest green.
- [ ] Go: `gofmt -w` + `goimports` over all modules; `go vet ./...`,
      `go test ./...` green per module.

### AT-3 — CI enforcement (format:check gates) [not started]
- [ ] Add verify steps to `.github/workflows/build.yml`:
      `dotnet format --verify-no-changes`, `clang-format --dry-run -Werror`,
      `prettier --check` + `eslint`, `gofmt -l` (fail if non-empty) + `go vet`.
- [ ] A `just fmt` + `just fmt-check` recipe pair mirroring CI locally.

### AT-4 — Compiler / analyzer warning cleanup (no behaviour change) [not started]
- [ ] C# build warnings across in-scope projects -> 0 (or justified suppress).
- [ ] C++ `-Wall -Wextra` warnings in proxy/common/launcher/freya C++ -> 0
      where fixable without behaviour change (server/src excluded).
- [ ] Go `go vet` clean.

### AT-5 — God-class audit + DI modularization (MANAGED CODE ONLY) [not started]
- [ ] Enumerate every C# class > 1200 lines (in-scope trees).
- [ ] For each: split into cohesive collaborators wired via constructor DI;
      preserve behaviour; add/keep tests. (C++ god-classes explicitly deferred.)

### AT-6 — Final audit [not started]
- [ ] Maintainability / security / correctness review of all AT changes.
- [ ] Full test suite green (`just test`); game still functions (CV entry if any
      wire-adjacent code moved -- formatting alone is not a wire change).

## Notes / log
- (AT-1) starting 2026-06-16.
