# Phase I — Dev env polish

Goal: developer ergonomics. justfile polish, docker-compose hot-reload, packaging, CI matrix expansion.

## Items

- [ ] `justfile` polished: `dev` brings up compose with logs; `down` tears down; `psql` opens a shell to the DB; `logs server` tails; `shell server` exec's into the running container.
- [ ] `docker-compose.yml` — mount `server/src` and `server/build` for hot rebuild; add `pgadmin` optional profile; add `--profile tools` for the C# editors (running under wine in a container if practical).
- [ ] `server/Dockerfile` multi-stage build (build → runtime); package final image as `ghcr.io/.../enb-server:latest`.
- [ ] `login-server/Dockerfile` same pattern.
- [ ] `.github/workflows/build.yml` matrix: ubuntu-24.04 + ubuntu-22.04; build server, run schema apply, run tests.
- [ ] `.github/workflows/release.yml` — on tag, build + push images to GHCR.
- [ ] `pre-commit` config (optional): clang-format, dotnet format, shellcheck.

## Verification

- `just dev` brings up postgres + server (server may fail to start; that's a Phase B issue, document it).
- `just package` produces a server image.
- CI workflow file is valid (`yamllint`).
- Mark all phases complete in master plan. Tell the user.
