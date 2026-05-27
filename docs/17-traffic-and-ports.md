# Traffic and ports

This page covers every network leg the stack uses end-to-end: which component talks to which, on what port, over TCP or UDP, with what crypto, and where the boundary lives.

If you only remember one thing: **the client never talks directly to a public server.** It only talks to the local proxy on `localhost`. The local proxy is what decides which (if any) upstream the traffic gets forwarded to. That choice is configurable at proxy runtime — no recompile needed.

## Components

| Component        | Where it runs                                       | Binary / project                                  | Role                                                                                       |
| ---------------- | --------------------------------------------------- | ------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| `client.exe`     | Windows native or Linux via WINE                    | `client/mods/release/`                            | The game client. Speaks Westwood RC4 framing to whatever it's pointed at.                  |
| `authlogin.dll`  | Loaded in-process by `client.exe`                   | `client/mods/release/`                            | The client's auth plugin. Performs the HTTPS `/AuthLogin` call to get a session ticket.    |
| `LaunchNet7.exe` | Same host as the client (Win/WINE)                  | `tools/launchnet7/LaunchNet7/`                    | Patches client config / `authlogin.dll` offsets, runs `mkcert`, spawns the proxy + client. |
| `Net7Proxy.exe`  | Same host as the client (Win/WINE) OR Linux native  | `proxy/`                                          | TLS / Westwood-crypto terminator. Bridges the client's loopback traffic to the upstream.    |
| `Net7SSL` (login) | Linux container (`docker-compose` service `login`) | `login-server/Net7SSL/`                           | Terminates TLS for `/AuthLogin`, validates credentials, issues 20-byte session tickets.    |
| `Net7` (server)  | Linux container (`docker-compose` service `server`) | `server/`                                         | The game world. Sector / global / master control planes, UDP fan-out, world simulation.    |
| Postgres         | Linux container                                     | `db/postgres/`                                    | Persistence for accounts, characters, world state.                                         |

## Topology

```
                            (loopback only)
                              ┌──────────────┐
+----------+   Westwood RC4   │              │   TLS 1.3      ┌─────────────────┐
|client.exe| ───── TCP ──────►│ Net7Proxy.exe├──── TCP ──────►│  Net7SSL (auth) │
| (Win/    | ◄──── UDP ──────►│              │  port 443      └─────────────────┘
|  WINE)   |                  │              │
+----------+                  │              │   UDP control  ┌─────────────────┐
                              │              │◄───────────────┤ Net7 (game)     │
                              │              │   ports 3808/  │  ports 3501+    │
                              │              │   3810         └─────────────────┘
                              └──────┬───────┘
                                     │
                              (optional, runtime-
                               configurable)
                                     │
                                     ▼
                        ┌──────────────────────────┐
                        │ NET7_UPSTREAM_HOST       │
                        │ (your public deployment, │
                        │  whatever it is)         │
                        └──────────────────────────┘
```

The bracketed "loopback only" box is the whole story for the client's network surface. Everything south of the proxy can move between hosts, containers, or stay on the same machine — the client doesn't know and doesn't care.

## Ports

Authoritative list — every port the stack binds. Macros are in [`common/include/net7/Ports.h`](../common/include/net7/Ports.h).

| Macro                          | Port  | Proto    | Bound by                | Talks to                          | Role                                                                                                                            |
| ------------------------------ | ----- | -------- | ----------------------- | --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| `SSL_PORT`                     | 443   | TCP/TLS  | `login-server/Net7SSL/` | `Net7Proxy.exe` (and `client.exe` via the proxy) | Terminates the auth TLS pipe. Parses `/AuthLogin`, validates against the user DB, returns a 20-byte session ticket.            |
| `PROXY_LOCAL_TCP_PORT`         | 3500  | TCP      | `proxy/`                | `client.exe`                      | The proxy's own loopback terminator. Where the client's Westwood RC4-framed traffic lands.                                      |
| `SECTOR_SERVER_PORT`           | 3501+ | TCP      | `server/` (per sector)  | `proxy/`                          | Sector-server TCP control plane. Each sector adds an offset to the base port.                                                   |
| `MASTER_SERVER_PORT`           | 3801  | TCP      | `proxy/` and `server/`  | each other                        | Per-galaxy master handler. Multiplexes client connections onto the right sector.                                                |
| `GLOBAL_SERVER_PORT`           | 3805  | TCP      | `proxy/`                | `server/`                         | Global control plane (cross-galaxy). The proxy's listener that the master service registers with.                               |
| `MVAS_LOGIN_PORT`              | 3806  | TCP      | n/a (client-side hookup, port number only) | n/a                | The launcher's MVAS hookup; here for the port number only, no server binds it.                                                  |
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

| Concern                  | Where to set                        | How it propagates                                                                                                                              |
| ------------------------ | ----------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| Local hostname           | `Launcher.LocalHostname` (C#) + `NET7_DOMAIN` env var (proxy) | Defaults to `localhost`. Cert CN matches. Client config files (`Network.ini`, `Auth.ini`, `rg_regdata.ini`) are rewritten on every launch to this value. |
| Upstream host (where the proxy forwards) | `NET7_UPSTREAM_HOST` env var, or `/UPSTREAM:<host>` on the proxy CLI | The launcher passes the user-selected entry from `LaunchNet7.cfg` through to the proxy as `NET7_UPSTREAM_HOST` when spawning. The proxy logs the configured upstream at startup. Empty = no upstream configured (outbound flows that require one log and skip). |
| Auth port                | `LaunchSetting.AuthenticationPort` (C#) + patched into `authlogin.dll` | The launcher patches the port and HTTPS flag into the DLL at byte offsets `0x8328` (HTTPS bool), `0x82AD` (port u16 LE), `0x8292` (timeout u16 LE). Patching is gated on `authlogin.dll` reporting `FileVersion == 3.3.0.6` — mismatched builds are refused. |
| TLS cert path            | `proxy/SSL_Connection.cpp` builds `%s.cer` / `%s.pem` from `g_DomainName` | So the cert filename tracks the local hostname automatically; changing `NET7_DOMAIN=foo` looks for `foo.cer`/`foo.pem`. |

## Client patching and the backup rule

Anywhere the launcher writes to a client file (`Network.ini`, `Auth.ini`, `rg_regdata.ini`, `authlogin.dll`):

- On the **first** patch ever, a sibling backup is created (`<name>.orig`).
- The backup is **never** modified or overwritten after that.
- If you want to re-patch from a clean baseline, copy the `.orig` back over the working file, then run the launcher again.

This matters because the launcher mutates the live client files in-place. Without the one-shot backup, repeated launches with different settings would silently drift the on-disk state away from any known-good baseline.
