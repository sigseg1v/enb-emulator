# Traffic and ports

This page covers every network leg the stack uses end-to-end: which component talks to which, on what port, over TCP or UDP, with what crypto, and where the boundary lives.

If you only remember one thing: **the client only ever talks to loopback.** Every URL/host the client uses (`Network.ini`, `Auth.ini`, `rg_regdata.ini`, `authlogin.dll`'s patched server name) is hardcoded to `localhost`. Two in-launcher / in-host middlemen on loopback do all the off-host talking:

- **LocalAuthRelay** terminates the client's HTTPS-auth call on `127.0.0.1:4180` as plaintext HTTP, then re-wraps it as TLS to the upstream auth server. Hardcoded port (`LocalAuthRelay.ListenPort` is a `const`), hardcoded bind (`IPAddress.Loopback`), no setting / env var / registry key can move the client off it.
- **`net7proxy`** terminates the client's Westwood RC4 TCP on `localhost:3500` and bridges into the game server's UDP planes — also always loopback-from-the-client's-perspective.

For a remote deployment, neither the client nor authlogin.dll changes — only the *upstream* the relay and the proxy dial moves to a remote host, and that hop is TLS or Westwood RC4 respectively, never plaintext HTTP.

## Components

| Component        | Where it runs                                       | Binary / project                                  | Role                                                                                       |
| ---------------- | --------------------------------------------------- | ------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| `client.exe`     | Windows native or Linux via WINE                    | `client/mods/release/`                            | The game client. Speaks Westwood RC4 framing to whatever it's pointed at.                  |
| `authlogin.dll`  | Loaded in-process by `client.exe`                   | `client/mods/release/`                            | The client's auth plugin. Performs the HTTPS `/AuthLogin` call to get a session ticket.    |
| `LaunchNet7.exe` | Same host as the client (Win/WINE)                  | `tools/launchnet7/LaunchNet7/`                    | Patches client config / `authlogin.dll` offsets, runs `mkcert`, spawns the proxy + client. |
| `net7proxy`      | Linux container (`docker-compose` service `server` colocates it; production: same box as game server) | `proxy/`                                          | Server-side terminator for the client's Westwood RC4 TCP connection (`PROXY_LOCAL_TCP_PORT` 3500). Bridges into the colocated game server's UDP planes and the auth server's TLS endpoint. Build is Linux-only (`proxy/CMakeLists.txt`); there is no Win32 build target in this repo. |
| `Net7SSL` (login) | Linux container (`docker-compose` service `login`) | `login-server/Net7SSL/`                           | Terminates TLS for `/AuthLogin`, validates credentials, issues 20-byte session tickets.    |
| `Net7` (server)  | Linux container (`docker-compose` service `server`) | `server/`                                         | The game world. Sector / global / master control planes, UDP fan-out, world simulation.    |
| Postgres         | Linux container                                     | `db/postgres/`                                    | Persistence for accounts, characters, world state.                                         |

## Topology

```
                       ─────────── on the player's box (loopback only) ───────────  │  ────── upstream ──────
                                                                                    │
                                                                                    │
                       HTTP plaintext      ┌──────────────────────┐   TLS 1.2/1.3   │  ┌─────────────────────┐
                       to 127.0.0.1:4180   │  LocalAuthRelay      │   to :443       │  │  Net7SSL (auth)     │
+----------+ ───── 1 ────────────────────► │  (in-launcher)       │ ───── 2 ──────────►│  bound on :443      │
|client.exe|     (loopback)                │  TcpListener on      │   (loopback if  │  └─────────────────────┘
|+ authlogin                               │  IPAddress.Loopback  │    upstream =   │
|  dll     |                               │  ListenPort = 4180   │    localhost;   │
| (Win/    |                               │  HARDCODED const     │    real CA      │
|  WINE)   |                               └──────────────────────┘    otherwise)   │
|          |                                                                        │
|          | ───── 3 ────────────────────► ┌──────────────────────┐   UDP           │  ┌─────────────────────┐
+----------+   Westwood RC4 TCP            │  net7proxy           │ ───── 4 ──────────►│  Net7 (game server) │
              to localhost:3500            │  PROXY_LOCAL_TCP=3500│   :3806/3808    │  │  3806/3808/3810,    │
                                           │  (loopback bind)     │   /3810         │  │  3501+ per sector   │
                                           └──────────────────────┘                 │  └─────────────────────┘
                                                                                    │
```

**Legend.**
1. **Auth, plaintext, loopback only.** `authlogin.dll` is patched at every launch (`Launcher.PatchAuthLoginFile`) to dial `127.0.0.1:4180` with the HTTPS flag cleared. The port (`LocalAuthRelay.ListenPort = 4180`) is a `const`, not a setting. `HKLM\Software\EACom\AuthAuth\AuthLoginServer` is hardcoded to `localhost` on both WINE (`Launcher.PatchRegistry`) and Windows (`WindowsRegistryHelpers.EnsureRegistered`).
2. **Auth, TLS, always.** `LocalAuthRelay.HandleClient` wraps the upstream socket in `SslStream` with `Tls12 | Tls13` enabled, *unconditionally*. The validation callback returns `true` only if the upstream host is loopback by *syntax* (`localhost` literal or `IPAddress.IsLoopback(parsed)` — no DNS lookup). Anywhere else, validation falls back to the OS trust store with no overrides.
3. **Game data, Westwood RC4, loopback only.** Client TCP-connects to `localhost:3500` and does the RSA+RC4 handshake with `net7proxy`. `net7proxy`'s listener also binds loopback in the dev stack.
4. **Game data, UDP to upstream.** `net7proxy` dials `NET7_GAME_SERVER_HOST` (default `server` — docker compose DNS) for the UDP control planes. In dev, all three processes share the docker network; in a remote split, this hop crosses the public internet over UDP with the same Westwood RC4 framing.

**Where each box runs.** In this codebase the proxy is server-native Linux (see the Phase M comment at the top of `proxy/Net7.cpp`). The dev topology is one Linux host running `net7proxy`, `Net7SSL`, and `Net7`, with the client (Win/WINE) connecting in over RC4 on port 3500 and the launcher (also on the same host) running the auth relay. For a public deployment, only the *upstream targets* of the relay and proxy change — the client itself never learns about a non-loopback host.

There is no "client-side local proxy that forwards to a remote game server" flow in the current build. The launcher's `LaunchNet7Proxy()` step is vestigial in that sense: this repo's `proxy/CMakeLists.txt` is Linux-only, so the `bin/Net7Proxy.exe` it tries to spawn does not come from this source tree.

## Ports

Authoritative list — every port the stack binds. Macros are in [`common/include/net7/Ports.h`](../common/include/net7/Ports.h).

| Macro                          | Port  | Proto    | Bound by                | Talks to                          | Role                                                                                                                            |
| ------------------------------ | ----- | -------- | ----------------------- | --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| **`LocalAuthRelay.ListenPort`** | **4180** | **TCP/HTTP (loopback)** | **`tools/launchnet7-avalonia/Network/LocalAuthRelay.cs`** | **`authlogin.dll`** | **Plaintext HTTP terminator on `127.0.0.1`. Hardcoded `const` — no setting moves it. Wraps upstream-bound traffic as TLS; see §Topology hops 1+2.** |
| `SSL_PORT`                     | 443   | TCP/TLS  | `login-server/Net7SSL/` (primary), `proxy/` (also has an `SSL_Listener` on `ssl_port`, default 443) | `LocalAuthRelay` (was: `authlogin.dll` directly) | Terminates the auth TLS pipe. Parses `/AuthLogin`, validates against the user DB, returns a 20-byte session ticket. In docker dev, Net7SSL binds 443 inside the login container and the host sees it as 4443; `LocalAuthRelay` is what dials it. |
| `PROXY_LOCAL_TCP_PORT`         | 3500  | TCP      | `proxy/`                | `client.exe`                      | The proxy's own loopback terminator. Where the client's Westwood RC4-framed traffic lands.                                      |
| `SECTOR_SERVER_PORT`           | 3501+ | TCP      | `server/` (per sector)  | `proxy/`                          | Sector-server TCP control plane. Each sector adds an offset to the base port.                                                   |
| `MASTER_SERVER_PORT`           | 3801  | TCP      | `proxy/` and `server/`  | each other                        | Per-galaxy master handler. Multiplexes client connections onto the right sector.                                                |
| `GLOBAL_SERVER_PORT`           | 3805  | TCP      | `proxy/`                | `server/`                         | Global control plane (cross-galaxy). The proxy's listener that the master service registers with.                               |
| `MVAS_LOGIN_PORT`              | 3806  | UDP      | `server/` (`MVASauth` in `server/src/Net7.cpp:383`) | `proxy/`, `login-server/Net7SSL/` | Per-player "MVAS" channel. Carries avatar-login / position-update / logoff opcodes (`0x1000`-family, `0x3005_PLAYER_COMMS_ALIVE`, `0x4000_REGISTER_SSL`, `0x4003_AVATARLOGIN`). Bound by the game server; written to by the proxy (`proxy/UDPProxyMVAS.cpp`, `proxy/UDPClient.cpp`, `proxy/UDPProxyToGlobal.cpp`) and by Net7SSL during the auth handoff (`login-server/Net7SSL/UDPClient.cpp`). |
| `SSL_LOCALCERT_LOGIN_PORT`     | 3807  | TCP      | `login-server/Net7SSL/` (legacy)  | `proxy/`                | Out-of-band local-cert login. The original TCP global-control plane, since superseded by `UDP_GLOBAL_SERVER_PORT` (3810).        |
| `UDP_MASTER_SERVER_PORT`       | 3808  | UDP      | `server/`               | `proxy/` (via `UDPClient`)        | Master handoff for the proxy → server direction (ticket validation, sector port assignment).                                    |
| `PROXY_SERVER_PORT`            | 3809  | UDP      | `proxy/`                | `server/`                         | Proxy-to-proxy UDP plane.                                                                                                       |
| `UDP_GLOBAL_SERVER_PORT`       | 3810  | UDP      | `server/`               | `proxy/`                          | Global control plane: ticket validation, avatar list, char create/delete. Dispatched via `UDP_Connection::HandleGlobalOpcode`. |

In dev (`docker-compose.yml`) the login container's port 443 is published to the host as `4443`; the rest stay container-internal. See [`docs/09-running-locally.md`](09-running-locally.md).

## Crypto

Two distinct boundaries — not one.

### Client ↔ proxy: Westwood RSA-1024 + Westwood RC4

The boundary that lives on `localhost`. Implemented in:
- `common/include/net7/WestwoodRSA.h`, `WestwoodRC4.h` — single source of truth for both algorithms.
- `proxy/WestwoodRSA.cpp`, `WestwoodRC4.cpp` — proxy-side terminator.
- `server/src/WestwoodRSA.cpp`, `WestwoodRC4.cpp` and `login-server/Net7SSL/WestwoodRSA.cpp`, `WestwoodRC4.cpp` — same algorithm where the server-side processes need it directly.
- `tools/cli-client/tests/CliClient.UnitTests/Net/WestwoodRSATests.cs` and `WestwoodRC4Tests.cs` — round-trip tests for a C# port.

Handshake:

1. The client TCP-connects to the proxy on `PROXY_LOCAL_TCP_PORT` (3500) on `localhost`.
2. The proxy sends its 1024-bit Westwood-format RSA public key.
3. The client picks a session key (RC4 key material), RSA-encrypts it under the proxy's public key, sends the ciphertext back.
4. Both sides instantiate Westwood RC4 with the agreed session key.
5. Every subsequent frame: 4-byte clear length prefix, then RC4-encrypted opcode payload, in both directions.

This is a 2000s-era stream cipher: it is not modern crypto. It survives because:
- The leg never leaves the local host. There is no public-internet attack surface on the RC4 leg by design.
- The client demands it. The only way to remove it would be to ship a different client.

### Proxy ↔ upstream / Net7SSL: TLS 1.3 over OpenSSL 3

The boundary that crosses (or could cross) the public internet. Implemented in:
- `proxy/SSL_Connection.cpp` and `proxy/SSL_Listener.cpp` — TLS endpoints in the proxy.
- `login-server/Net7SSL/SSL_Connection.cpp` and `SSL_Listener.cpp` — TLS endpoints in the auth server.

Library: **OpenSSL 3.0.x** on Linux (verified: `libssl.so.3`, `libcrypto.so.3`). The Win32 build of the proxy still ships with `libssl-1_1.dll` / `libcrypto-1_1.dll` historically; that needs the same OpenSSL 3.x bump. There is no OpenSSL 1.x in the Linux build.

Protocol: `TLS_server_method()` / `TLS_client_method()` — i.e. let OpenSSL negotiate the highest mutually-supported version. In practice that's **TLS 1.3** end-to-end (verified via `openssl s_client -connect 127.0.0.1:4443` against the login container).

Ciphers: whatever OpenSSL 3's TLS 1.3 default suite list provides — `TLS_AES_256_GCM_SHA384`, `TLS_CHACHA20_POLY1305_SHA256`, `TLS_AES_128_GCM_SHA256`. The legacy `SSLv23_server_method()` call sites in `proxy/SSL_Connection.cpp` still compile against OpenSSL 3 (it's an alias for the version-flexible method); replacing them with `TLS_server_method()` is a cosmetic cleanup, not a behavioural change.

### Cert trust

The local TLS leg uses a self-issued cert. Trust is handled via **mkcert** (https://github.com/FiloSottile/mkcert):

1. The launcher invokes `mkcert -install` (idempotent — mkcert no-ops if its CA is already trusted).
2. The launcher then invokes `mkcert -cert-file ... -key-file ... localhost` to produce a cert for the local hostname.
3. The proxy's embedded HTTPS listener uses that cert.

No `LocalMachine\Root` write happens from this codebase. mkcert manages its own root CA in `~/.local/share/mkcert/` (or the WINE prefix equivalent) and adds it to the platform trust store the way the platform expects. Removing trust is `mkcert -uninstall`.

If `mkcert.exe` isn't on `PATH` or in the launcher's `bin/`, the launcher fails fast with a clear error message rather than silently falling back to an insecure path.

## Configuration

The proxy has three independent hostname/port knobs. They do different things and the difference matters — confusing them silently breaks remote deployments.

| Concern                  | Knob                                                              | Default                  | What it actually does                                                                                                                                                                                                                                                                              | Status      |
| ------------------------ | ----------------------------------------------------------------- | ------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------- |
| Local TLS listener name  | `NET7_DOMAIN` env var (proxy reads it in `proxy/Net7.cpp:125`)    | `localhost`              | Sets `g_DomainName`, which `proxy/SSL_Connection.cpp` then uses to build the cert path (`<g_DomainName>.cer` / `.pem`). It is **not** a hostname the proxy dials anywhere — it's the name the embedded TLS listener uses for its own cert lookup. The cert's CN must match.                       | Active.     |
| Game server target host  | `NET7_GAME_SERVER_HOST` env var (proxy reads it in `proxy/UDPClient_linux.cpp:129`) | `server` (docker compose DNS name) | Hostname the proxy's `UDPClient::OpenFixedPort` resolves and dials for the game server's UDP planes (`UDP_MASTER_SERVER_PORT` 3808, `UDP_GLOBAL_SERVER_PORT` 3810, `MVAS_LOGIN_PORT` 3806). **If you split the proxy and game server onto different boxes, this is the env var you set.** | Active.     |
| Auth port (upstream)     | `LaunchSetting.AuthenticationPort` (C#) — passed to `LocalAuthRelay.Start` as the *upstream* port | 4443 (`play-local`) | What the relay dials on the upstream side (hop 2). TLS, always. Does **not** affect what `authlogin.dll` sees on hop 1. | Active. |
| `authlogin.dll` target   | **HARDCODED** in `Launcher.PatchAuthLoginFile` to `Port=LocalAuthRelay.ListenPort, UseHttps=false` | 4180 + HTTP | The launcher byte-patches `authlogin.dll` at offsets `0x8328` (HTTPS bool → `0x40` / HTTP), `0x82AD` (port u16 LE → 4180), `0x8292` (timeout u16 LE). Values come from `const`s + literals, not settings. Gated on `FileVersion == 3.3.0.6`. There is no setting / env var / config knob that can change these. | Active. |
| Public-facing server host | `NET7_UPSTREAM_HOST` env var, or `/UPSTREAM:<host>` proxy CLI flag (alias for `NET7_GAME_SERVER_HOST`) | empty                    | **This is the knob the launcher writes when the user picks a server.** At proxy startup, if set and `NET7_GAME_SERVER_HOST` is not already explicitly set, `g_UpstreamHost`'s value is propagated into `NET7_GAME_SERVER_HOST` via `setenv` (`proxy/Net7.cpp` post-CLI block) — `ResolveGameServerIP` then picks it up like any other game-server host. An explicit `NET7_GAME_SERVER_HOST` always wins (split-deployment override). | Active. |

## Plaintext-never-leaves-the-box guarantee

This section exists because the auth path has a plaintext HTTP hop, and somebody is eventually going to ask whether that hop can be tricked into leaving the local machine. The short answer is no. The long answer:

| Vector | Why it can't escape loopback |
|---|---|
| User edits `LaunchNet7.settings.json` and sets `"AuthenticationPort": 80` | Doesn't matter for hop 1. `Launcher.PatchAuthLoginFile` writes `Port=LocalAuthRelay.ListenPort, UseHttps=false` unconditionally; the only auth-port setting in the launcher is the *upstream* port the relay dials on hop 2. There is no UI control or setting that toggles HTTPS off on hop 1 — the relay is always plaintext-on-loopback by design. |
| User edits the WINE registry by hand, sets `HKLM\Software\EACom\AuthAuth\AuthLoginServer` to `evil.example.com` | Doesn't matter. `Launcher.PatchRegistry` rewrites it to `localhost` at every launch. Same on Windows via `WindowsRegistryHelpers.EnsureRegistered`. |
| User sets `NET7_UPSTREAM_HOST=remote.example.com` and starts the launcher | Affects only the *upstream* the relay (and proxy) dial. `authlogin.dll` still sees `127.0.0.1:4180` plaintext. The relay's outbound connection is TLS regardless. |
| User points the launcher at a remote server in the UI | Same as above. The remote hostname becomes the relay's upstream and the proxy's `NET7_GAME_SERVER_HOST`. authlogin.dll's target is still hardcoded loopback. |
| Attacker poisons DNS so `attacker.example.com → 127.0.0.1` | The relay's `IsLoopback` check is syntactic — it only matches the literal string `localhost` and `IPAddress.IsLoopback(parsed-IP)`. Hostnames are never resolved to make the verify-skip decision. So a poisoned DNS answer can't downgrade verify; the relay would do full OS-trust-store validation against `attacker.example.com` (which would fail unless the attacker has a real cert for that name). |
| Attacker MITMs the loopback hop | The relay binds `IPAddress.Loopback` specifically (not `IPAddress.Any`). Plaintext bytes only exist inside the kernel's loopback path. An attacker who can sniff loopback already has root on the box and the threat model has bigger problems. |
| Future contributor adds a config knob to move authlogin.dll off loopback | `LocalAuthRelay.ListenPort` is `const ushort`. The bind is the literal `IPAddress.Loopback`. The patcher writes `Port=LocalAuthRelay.ListenPort, UseHttps=false` directly — neither value comes from a settable field. Changing any of them is a source edit, code review, and a CC BY-NC-SA-licensed commit — not a runtime mistake. |

If you find a way to make the client send plaintext HTTP to a non-loopback host through the launcher's configuration surface (settings.json, env vars, registry edits, INI edits the launcher won't overwrite, CLI flags), that's a security bug — file it.

## Client patching and the backup rule

Anywhere the launcher writes to a client file (`Network.ini`, `Auth.ini`, `rg_regdata.ini`, `authlogin.dll`):

- On the **first** patch ever, a sibling backup is created (`<name>.orig`).
- The backup is **never** modified or overwritten after that.
- If you want to re-patch from a clean baseline, copy the `.orig` back over the working file, then run the launcher again.

This matters because the launcher mutates the live client files in-place. Without the one-shot backup, repeated launches with different settings would silently drift the on-disk state away from any known-good baseline.
