# Phase V — Client isolation, launcher hardening, neutral hostnames

Goal: every client-side network call goes through the local proxy on `localhost`. Public-server hostnames disappear from the client. The proxy chooses upstream targets at runtime. Launcher hardens the cert flow (no root-store writes) and the in-place client patches it does.

## Why

- Public deployments will use hostnames we control — never the legacy public host. Hardcoded references to a specific public host in the client / launcher / proxy are wrong by default.
- Cert trust shouldn't require writing to `LocalMachine\Root`. Modern dev workflow uses mkcert; we should adopt it.
- The launcher's in-place patches of client files need a one-shot backup so we can recover the original bytes.

## Scope (single wave)

- [x] **Launcher: version check in `PatchAuthLoginFile`.** `AuthLoginPatcher.VerifyVersion()` reads `FileVersionInfo.FileVersion` and refuses to patch unless it matches the expected build (3.3.0.6). Both Read and Write go through the check.
- [x] **Launcher: `PatchRegistry64`.** New method writes `Software\Wow6432Node\Westwood Studios\Earth and Beyond\Registration\Registered = 1` for 64-bit hosts running the 32-bit client (WoW64 or 64-bit WINE prefix). Logs and continues on failure (32-bit-only path is already covered).
- [x] **Launcher: quote `/CLIENT:<path>`** in `LaunchClient()` and `LaunchNet7Proxy()` so paths with spaces (most WINE prefixes; `Program Files`) survive argv parsing.
- [x] **Launcher: mkcert flow replaces root-store install.** New `Patching/CertificationUtility.cs` wraps `mkcert.exe`. Called from `Launch()` only when `UseLocalCert` is on. Idempotent: skips if `bin/local.crt` + `bin/local.key` already exist and are non-empty. Hard-errors with install instructions if mkcert isn't present.
- [x] **Launcher: single local-hostname constant.** New `Launcher.LocalHostname = "localhost"`. The three previous string literals in `PatchNetworkIniFile` / `PatchAuthIniFile` / `PatchRegDataFile` all use the constant. Patch is unconditional — `UseLocalCert` no longer gates the hostname choice, because the client now always points at the local proxy.
- [x] **Launcher: pass upstream to proxy via env var.** When `Setting.Hostname` is non-empty and not the local hostname, `LaunchNet7Proxy()` sets `info.EnvironmentVariables["NET7_UPSTREAM_HOST"]` on the proxy spawn. Operators can also set it directly in their environment.
- [x] **Launcher: one-shot backup helper.** New `EnsureBackup(src, backup)` static. First time it sees `src` without a sibling `backup`, copies once. Never re-copies after. Called from all four patch methods (Network.ini, Auth.ini, rg_regdata.ini, authlogin.dll).
- [x] **Launcher: `LaunchNet7.cfg`.** Removed all `*.net-7.org` entries. Single-player entries default to `localhost`. Multi-player entries default to empty string (= "operator sets `NET7_UPSTREAM_HOST` in env / picks via Custom flow"). Auto-update section dropped (dormant; operator updates binaries out of band).
- [x] **Client config: strip `www.net-7.org`.** Backed up `auth.ini`, `rg_regdata.ini` as `.orig`. Replaced `AAIUrl=www.net-7.org` with `AAIUrl=localhost`, `LKeyUrl=https://www.net-7.org/...` with `https://localhost/...`, `regserverurl=https://www.net-7.org/subsxml` with `https://localhost/subsxml`.
- [x] **Server-side defaults: `localhost`.** `proxy/Net7.cpp` and `login-server/Net7SSL/Net7SSL.cpp` now default `g_DomainName` to `"localhost"`. The proxy also reads `NET7_DOMAIN` env var if set.
- [x] **Server-side: separate `g_UpstreamHost` from `g_DomainName`.** New extern in `proxy/Net7.h`, populated from `/UPSTREAM:<host>` CLI or `NET7_UPSTREAM_HOST` env. Default empty — outbound flows must guard against empty and skip rather than dialling a hardcoded host. (The Linux build's `RegisterSectorServer` already short-circuits to success, so wiring the rest is follow-up work.)
- [x] **`deploy/Net7Config.cfg`.** `domain=local.net-7.org` → `domain=localhost`.
- [x] **Strip `local.net-7.org` from `proxy/`, `login-server/Net7SSL/` source comments and `Host:` header literals** so leaked example strings don't carry the old name.
- [x] **docs/17-traffic-and-ports.md.** New page covering components, topology diagram, port table (all 10 from `Ports.h`), Westwood RC4 vs TLS 1.3 boundaries, OpenSSL 3 verification (`ldd` output), mkcert flow, runtime config, one-shot backup rule.

## Out of scope (deliberately deferred)

- **Auto-update.** Launcher's update machinery stays dormant; operator updates binaries out of band. No replacement for `patch.net-7.org` URLs in this phase.
- **Packet-opt and reorder.** `/POPT`, `/EXREORDER` and friends from the historical proxy CLI surface are not reintroduced. Skip per request.
- **Outbound upstream wiring for `RegisterSectorServer`.** Plumbing for `g_UpstreamHost` exists; the actual outbound TLS dial against it is follow-up because the Linux build currently short-circuits the whole path.
- **Tools and other entry points that still reference `net-7.org`** (`tools/sector-editor`, `tools/mob-editor`, `tools/enb-ini-parser`, `tools/toolslauncher`, `tools/enbpatcher-avalonia`, `tools/toolspatcher-avalonia`). These are dev tools that hit a DB host or patch URL; they're not on the client traffic path. Cleanup is a separate task.

## Verification

- `dotnet build tools/launchnet7/LaunchNet7/LaunchNet7.csproj`: 0 errors, 240 pre-existing CA1416 warnings only.
- `cmake --build proxy/build --target net7proxy`: clean.
- `cmake --build login-server/Net7SSL/build`: clean.
- `ldd` on both binaries confirms `libssl.so.3` / `libcrypto.so.3` (OpenSSL 3.0.13).
- `grep -rn "local\.net-7\.org" proxy/ login-server/ tools/launchnet7/ client/mods/Data/ deploy/` returns no matches.

## Status

Complete (single wave landed 2026-05-27).
