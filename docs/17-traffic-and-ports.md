# Traffic and ports

This page covers every network leg the stack uses end-to-end: which component talks to which, on what port, over TCP or UDP, with what crypto, and where the boundary lives.

If you only remember one thing: **the client talks to wherever `Network.ini` / `Auth.ini` / `rg_regdata.ini` point it.** In this repo those files are patched to `localhost` by default, because the dev scenario is "everything on one Linux box, client under WINE on the same host." For a remote deployment the client's INIs would name the public hostname of whichever box runs `net7proxy`. The client speaks Westwood RC4 to that hostname on `PROXY_LOCAL_TCP_PORT` (3500); from there `net7proxy` is what bridges into the auth and game UDP planes.

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
                                                                          ┌─────────────────────┐
+----------+   Westwood RC4   ┌────────────────────┐    TLS 1.3 :443      │  Net7SSL (auth)     │
|client.exe| ────── TCP ─────►│   net7proxy        │─────────────────────►│  bound on :443      │
| (Win/    |                  │   listens :3500    │                      └─────────────────────┘
|  WINE)   |                  │   (PROXY_LOCAL_TCP)│
+----------+                  │                    │    UDP :3806/3808    ┌─────────────────────┐
                              │                    │◄────────────────────►│  Net7 (game server) │
                              │                    │       /3810          │  bound on 3806/3808 │
                              └──────────┬─────────┘                      │  /3810 + 3501+      │
                                         │                                └─────────────────────┘
                                         │                                          ▲
                                         │            host the proxy dials for      │
                                         │            UDP game traffic is           │
                                         │            NET7_GAME_SERVER_HOST ────────┘
                                         │            (default "server" — docker
                                         │             compose DNS name)
                                         │
                                         │
                                         └────► (no separate "upstream" leg today;
                                                 the proxy IS server-side in this
                                                 build and dials the colocated
                                                 auth/game services directly)
```

**Where each box runs.** In this codebase the proxy is server-native Linux (see the Phase M comment at the top of `proxy/Net7.cpp`). The dev topology is one Linux host running `net7proxy`, `Net7SSL`, and `Net7`, with the client (Win/WINE) connecting in over RC4 on port 3500. For a public deployment, the same three Linux processes move to whichever box you point the client at — the client only needs the proxy's reachable hostname in `Network.ini`/`Auth.ini`/`rg_regdata.ini`.

There is no "client-side local proxy that forwards to a remote game server" flow in the current build. The launcher's `LaunchNet7Proxy()` step is vestigial in that sense: this repo's `proxy/CMakeLists.txt` is Linux-only, so the `bin/Net7Proxy.exe` it tries to spawn does not come from this source tree.

## Ports

Authoritative list — every port the stack binds. Macros are in [`common/include/net7/Ports.h`](../common/include/net7/Ports.h).

| Macro                          | Port  | Proto    | Bound by                | Talks to                          | Role                                                                                                                            |
| ------------------------------ | ----- | -------- | ----------------------- | --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| `SSL_PORT`                     | 443   | TCP/TLS  | `login-server/Net7SSL/` (primary), `proxy/` (also has an `SSL_Listener` on `ssl_port`, default 443) | `authlogin.dll` (in-process in `client.exe`) | Terminates the auth TLS pipe. Parses `/AuthLogin`, validates against the user DB, returns a 20-byte session ticket. In docker dev, Net7SSL binds 443 inside the login container and the host sees it as 4443; the client's `authlogin.dll` is what dials it (via `authlogin.dll`'s patched port — see Configuration below). |
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
| Auth port                | `LaunchSetting.AuthenticationPort` (C#) + patched into `authlogin.dll` | 443                   | Launcher patches the port + HTTPS flag into `authlogin.dll` at byte offsets `0x8328` (HTTPS bool), `0x82AD` (port u16 LE), `0x8292` (timeout u16 LE). Patching is gated on `authlogin.dll` reporting `FileVersion == 3.3.0.6` — mismatched builds are refused.                                  | Active.     |
| Public-facing server host | `NET7_UPSTREAM_HOST` env var, or `/UPSTREAM:<host>` proxy CLI flag (alias for `NET7_GAME_SERVER_HOST`) | empty                    | **This is the knob the launcher writes when the user picks a server.** At proxy startup, if set and `NET7_GAME_SERVER_HOST` is not already explicitly set, `g_UpstreamHost`'s value is propagated into `NET7_GAME_SERVER_HOST` via `setenv` (`proxy/Net7.cpp` post-CLI block) — `ResolveGameServerIP` then picks it up like any other game-server host. An explicit `NET7_GAME_SERVER_HOST` always wins (split-deployment override). | Active. |

## Client patching and the backup rule

Anywhere the launcher writes to a client file (`Network.ini`, `Auth.ini`, `rg_regdata.ini`, `authlogin.dll`):

- On the **first** patch ever, a sibling backup is created (`<name>.orig`).
- The backup is **never** modified or overwritten after that.
- If you want to re-patch from a clean baseline, copy the `.orig` back over the working file, then run the launcher again.

This matters because the launcher mutates the live client files in-place. Without the one-shot backup, repeated launches with different settings would silently drift the on-disk state away from any known-good baseline.
