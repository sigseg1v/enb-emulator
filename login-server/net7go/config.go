// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Copyright (c) 2010 Net-7 Entertainment, Ltd.
//
// net7go -- a standalone Go reimplementation of the Net7SSL game-auth server
// (login-server/Net7SSL). It is a direct functional port of that C++ code and
// therefore inherits the upstream Net-7 CC BY-NC-SA 3.0 license (share-alike):
// it is NOT MIT and is NOT part of the freya/* MIT tree. See LICENSES/ in the
// repo root and login-server/net7go/README.md.
//
// config.go -- runtime configuration. The DB env contract (DB_HOST/DB_USER/
// DB_PASS/DB_NAME) and the patcher/keepalive knobs mirror the C++ Net7SSL it
// replaces, so net7go drops into the same docker-compose wiring. Unlike the
// C++ login it does NOT terminate TLS: it listens plain HTTP behind the Freya
// Online TLS terminator, which raw-relays the legacy URIs to it.
package main

import (
	"os"
	"strings"
)

type Config struct {
	// Plain-HTTP listener. TLS is terminated upstream (the Go Freya Online
	// server) and the decrypted legacy request is relayed here; net7go writes
	// the byte-exact response that the relay copies verbatim back to the client.
	Addr string // NET7GO_ADDR, default ":8085"

	// Domain name echoed into the certificate.html body (HandleCertificate).
	Domain string // DOMAIN, default "localhost"

	// Postgres (net7_user holds accounts + login_ticket). One pool -- net7go
	// only touches the user DB, never the content catalogue.
	DBHost string // DB_HOST, default "postgres:5432"
	DBUser string // DB_USER, default "net7"
	DBPass string // DB_PASS, default "net7"
	DBName string // DB_NAME, default "net7_user"

	// AF_UNIX server-liveness keepalive (mirrors Net7SSL MailslotManager). ON by
	// default -- net7go is the sole owner of the recv socket post-cutover (the
	// C++ login it replaces is gone). Set NET7_IPC_KEEPALIVE=0 to disable.
	KeepaliveEnabled bool   // NET7_IPC_KEEPALIVE != "0"
	IPCSendSock      string // NET7_IPC_SEND_SOCK, default /run/net7-ipc/net7.sock (peer = server)
	IPCRecvSock      string // NET7_IPC_RECV_SOCK, default /run/net7-ipc/net7SSL.sock (ours)

	// Patcher self-update (/updateCheck). Empty manifest URL -> 503, matching
	// the C++ behaviour when the manifest cache is cold (fail-closed).
	PatcherManifestURL string // NET7_PATCHER_MANIFEST_URL
	PatcherDLBase      string // NET7_PATCHER_DL_BASE
}

func env(k, def string) string {
	if v, ok := os.LookupEnv(k); ok && v != "" {
		return v
	}
	return def
}

func loadConfig() Config {
	return Config{
		Addr:               env("NET7GO_ADDR", ":8085"),
		Domain:             env("DOMAIN", "localhost"),
		DBHost:             env("DB_HOST", "postgres:5432"),
		DBUser:             env("DB_USER", "net7"),
		DBPass:             env("DB_PASS", "net7"),
		DBName:             env("DB_NAME", "net7_user"),
		KeepaliveEnabled:   env("NET7_IPC_KEEPALIVE", "1") != "0",
		IPCSendSock:        env("NET7_IPC_SEND_SOCK", "/run/net7-ipc/net7.sock"),
		IPCRecvSock:        env("NET7_IPC_RECV_SOCK", "/run/net7-ipc/net7SSL.sock"),
		PatcherManifestURL: env("NET7_PATCHER_MANIFEST_URL", ""),
		PatcherDLBase:      strings.TrimRight(env("NET7_PATCHER_DL_BASE", ""), "/"),
	}
}

// dsn builds a libpq connection string for the user database.
func (c Config) dsn() string {
	host, port := c.DBHost, "5432"
	if i := strings.LastIndex(c.DBHost, ":"); i >= 0 {
		host = c.DBHost[:i]
		if p := c.DBHost[i+1:]; p != "" {
			port = p
		}
	}
	return "host=" + host + " port=" + port +
		" user=" + c.DBUser + " password=" + c.DBPass + " dbname=" + c.DBName
}
