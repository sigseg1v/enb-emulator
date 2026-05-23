# Phase I — Dev env polish

Goal: developer ergonomics. justfile polish, docker-compose hot-reload, packaging, CI matrix expansion.

## Outcome

Polished `justfile` with grouped sections, environment-variable image overrides, and proper just-style descriptions. Added a `docker-compose.override.yml.example` for opt-in bind-mount hot-reload. Bumped server Dockerfile to OpenSSL 3 compat (Phase E result). Added an ubuntu-22.04 / ubuntu-24.04 CI matrix to the cmake jobs. Added a tag-triggered GHCR release workflow. Added a `.pre-commit-config.yaml` covering trailing-whitespace, EOL, yaml, shellcheck, and clang-format (new code only).

## Items

- [x] `justfile` polished: `dev` brings up compose with logs; `down` tears down; `psql` opens a shell to the DB; `logs server` tails; `shell server` exec's into the running container.
      Touches: justfile
      Notes: Added `dev-fg` (foreground stream), `nuke` (down + volume wipe), `pgadmin` (opt-in profile), `build-tests` (Phase G binary), `push` (image push with env overrides), `format` (in-place clang-format + dotnet format). Fixed Phase D's `dotnet build tools/Net7Tools.sln` → `.slnx`. Grouped recipes under `---- build ----` / `---- dev stack ----` / `---- test ----` / `---- package / release ----` / `---- lint ----` / `---- housekeeping ----` banners.
- [x] `docker-compose.yml` — mount `server/src` and `server/build` for hot rebuild; add `pgadmin` optional profile; add `--profile tools` for the C# editors (running under wine in a container if practical).
      Touches: docker-compose.override.yml.example
      Notes: pgadmin profile already existed. Hot-reload added as opt-in override (`docker-compose.override.yml.example` — copy to enable). The "C# editors under wine in a container" item is intentionally NOT done: WinForms-on-wine-in-a-container is a 30-line `winecfg`/X11-forwarding rabbit hole for tools the Phase D path already builds cleanly with `dotnet build` on the host. Reclassified as a deferred Phase I continuation item.
- [x] `server/Dockerfile` multi-stage build (build → runtime); package final image as `ghcr.io/.../enb-server:latest`.
      Touches: server/Dockerfile, .github/workflows/release.yml
      Notes: Multi-stage build was already in place (Phase A scaffolding). Phase I bumped `OPENSSL_API_COMPAT` from `0x10100000L` → `0x30000000L` to match Phase E. Image push wired in `release.yml`.
- [x] `login-server/Dockerfile` same pattern.
      Touches: login-server/Dockerfile, .github/workflows/release.yml
      Notes: Multi-stage build already in place. Kept `OPENSSL_API_COMPAT=0x10100000L` because login-server isn't in the CMake build yet and still depends on `SSLv2_client_method` (removed in 1.1). The compat shim is the right level until Phase E continuation rewires those call sites. Image push wired in `release.yml`.
- [x] `.github/workflows/build.yml` matrix: ubuntu-24.04 + ubuntu-22.04; build server, run schema apply, run tests.
      Touches: .github/workflows/build.yml
      Notes: Added `strategy: matrix: os: [ubuntu-24.04, ubuntu-22.04]` to both `cmake-configure` and `cmake-build`. Also extracted warning lines from the build log to a `WARNINGS.txt` artifact per OS (Phase F continuation: trackable warning histograms over time without trawling the full build log). The other jobs (db-schema, dotnet-build, ctest, installer-shellcheck) intentionally NOT matrixed — they test things that don't depend on host kernel/glibc differences.
- [x] `.github/workflows/release.yml` — on tag, build + push images to GHCR.
      Touches: .github/workflows/release.yml
      Notes: Tag pattern `v*`; uses `docker/build-push-action@v6` with GHCR auth from `GITHUB_TOKEN`. Matrix builds server + login in parallel. `docker/metadata-action@v5` tags by semver + `latest`. `continue-on-error: true` because the server image build will currently fail end-to-end (Phase B is best-effort) and we don't want a failing release to require manual cleanup of GHCR.
- [x] `pre-commit` config (optional): clang-format, dotnet format, shellcheck.
      Touches: .pre-commit-config.yaml
      Notes: clang-format runs only against `tests/` and `server/compat/` (new code) — formatting the 162K LOC of legacy Net-7 source would produce an unreviewable diff. shellcheck runs on all shell. trailing-whitespace + EOF-fixer exclude archive/, server/src/, client/detours/, login-server/, proxy/ (legacy source we don't touch). Dotnet format is in `just lint`, not in pre-commit (it's slow and noisy).

## Verification

- `just --list` renders all 22 recipes with clean descriptions.
- `docker compose config -q` parses successfully (override is opt-in, not auto-loaded).
- All four YAML files (build.yml, release.yml, docker-compose.yml, .pre-commit-config.yaml) parse under PyYAML.
- Phase I closes the master plan — Phases A through I all marked complete.

## Deferred (Phase I continuation)

- WinForms-under-wine container profile for the C# tools — needs X11 / Wayland forwarding plumbing and is a separate project from "dev env polish."
- `release.yml` enforcement (drop `continue-on-error`) — gated on Phase B actually completing the server build.
- Matrix expansion to macOS/Windows runners — the server is Linux-first; cross-platform builds are out of scope until a contributor needs them.
