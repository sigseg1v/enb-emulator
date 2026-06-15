# net7go -- standalone Go reimplementation of the Net7SSL game-auth server

`net7go` is a self-contained Go program that replaces the C++
`login-server/Net7SSL` login server. It serves the legacy Earth & Beyond
game-auth endpoints byte-for-byte:

| URI | Handler |
|---|---|
| `/AuthLogin` | verify account (Argon2id) + issue/persist a login ticket |
| `/touchsession.jsp` | session keepalive (`Success` chunked body) |
| `/sectorserver.cgi` | sector-server registration check |
| `certificate.html` | the "certificate installed" landing page |
| `/who.cgi` | Linux no-op (404), as in the C++ |

The launcher self-update gate (`/updateCheck`) is **not** here -- it was never in
the retail Net7SSL; it is original Freya work and lives in `freya/online`
(`freya/online/server/updatecheck.go`, MIT). freya-online serves it directly and
does not relay it to net7go.

It also runs the AF_UNIX `SOCK_DGRAM` server-liveness keepalive (a "Ping" every
~10s to the game server).

## License -- CC BY-NC-SA 3.0, NOT MIT

**Every file in this directory is licensed CC BY-NC-SA 3.0** (`Copyright (c)
2010 Net-7 Entertainment, Ltd.`), the same as `login-server/Net7SSL`. net7go is
a direct functional port of that C++ code, so it inherits the upstream Net-7
license under the share-alike clause. It is **not** Freya/MIT code and is
deliberately kept **out of the `freya/` tree** to avoid any license confusion.

It links **no** Freya/MIT code -- the module (`go.mod`) depends only on
third-party libraries (pgx, `golang.org/x/crypto`) and is independently
buildable and distributable on its own.

## How it fits the stack

net7go does **not** terminate TLS. It listens on plain HTTP (`NET7GO_ADDR`,
default `:8085`) behind the Go Freya Online server, which terminates TLS on
`:443` and **raw-relays** the legacy URIs above to net7go, copying net7go's
response bytes verbatim back to the client. Because the relay copies raw bytes
(it does not re-frame headers), the bytes the real `client.exe` / launcher see
are exactly what net7go writes -- the byte-exactness the protocol requires.

```
client.exe / launcher / sector server
        | TLS :443
        v
  Freya Online (MIT)  --- raw-relay legacy URIs --->  net7go (:8085, this dir)
        |  /api + SPA                                      |  net7_user DB
        v                                                  v  AF_UNIX keepalive -> game server
   website
```

## Configuration (environment)

| Var | Default | Meaning |
|---|---|---|
| `NET7GO_ADDR` | `:8085` | plain-HTTP listen address |
| `DOMAIN` | `localhost` | echoed into `certificate.html` |
| `DB_HOST` | `postgres:5432` | Postgres host:port (`net7_user` DB) |
| `DB_USER` / `DB_PASS` / `DB_NAME` | `net7` / `net7` / `net7_user` | DB creds |
| `NET7_IPC_KEEPALIVE` | `1` | set `0` to disable the AF_UNIX keepalive |
| `NET7_IPC_SEND_SOCK` | `/run/net7-ipc/net7.sock` | game server's recv socket |
| `NET7_IPC_RECV_SOCK` | `/run/net7-ipc/net7SSL.sock` | net7go's recv socket |

The patcher manifest env (`FREYA_PATCHER_MANIFEST_URL` / `FREYA_PATCHER_DL_BASE`)
now belongs to freya-online (it owns `/updateCheck`), not net7go.

## Build / test

```sh
go build ./...
go test ./...
docker build -t net7go .
```
