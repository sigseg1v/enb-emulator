// SPDX-License-Identifier: MIT
// Freya Online -- runtime configuration.
//
// Env contract mirrors the C++ Net7SSL it replaces (DB_HOST/DB_USER/DB_PASS,
// DOMAIN) so it drops into the same docker-compose wiring, plus a few new knobs
// for the website half. See login-server/Net7SSL for the legacy source.
package main

import (
	"os"
	"strconv"
	"strings"
)

type Config struct {
	// TLS listener (legacy game auth + website share :443, as the C++ did).
	BindAddr string // NET7SSL_BIND_ADDR, default ":443"
	Domain   string // DOMAIN, default "localhost"; cert=<domain>.cer key=<domain>.pem
	CertDir  string // NET7SSL_CERT_DIR, default "/app/certs"

	// Optional plain-HTTP listener for the website/API behind a separate TLS
	// terminator (dev: vite proxies here). Empty disables it.
	HTTPAddr string // FREYA_HTTP_ADDR, default ":8080"

	// Postgres. The auth/ticket/account tables live in net7_user; item_base
	// (catalogue) lives in net7. Two pools -- cross-DB reads in one connection
	// are impossible (see the two-DB topology note).
	DBHost     string // DB_HOST, default "postgres:5432"
	DBUser     string // DB_USER, default "net7"
	DBPass     string // DB_PASS, default "net7"
	DBUserName string // DB_NAME, default "net7_user"
	DBContent  string // FREYA_DB_CONTENT, default "net7"

	// Static SPA assets (built dist/). Empty -> API only, no website.
	WebRoot string // FREYA_WEB_ROOT, default "/app/web"

	// AF_UNIX server-liveness keepalive (matches Net7SSL MailslotManager).
	// OFF by default: only one process may bind the recv socket, so during the
	// coexistence period the C++ login owns it. Turn on at cutover.
	KeepaliveEnabled bool   // NET7_IPC_KEEPALIVE=1
	IPCSendSock      string // /run/net7-ipc/net7.sock
	IPCRecvSock      string // /run/net7-ipc/net7SSL.sock

	// Patcher self-update (/updateCheck). Empty manifest URL -> 503, matching
	// the C++ behaviour when the manifest cache is cold.
	PatcherManifestURL string // NET7_PATCHER_MANIFEST_URL
	PatcherDLBase      string // NET7_PATCHER_DL_BASE

	// Treat the game server as ONLINE only if server_status.updated_at is within
	// this many seconds. The server heartbeats that row.
	StatusStaleSecs int // FREYA_STATUS_STALE_SECS, default 90
}

func env(k, def string) string {
	if v, ok := os.LookupEnv(k); ok && v != "" {
		return v
	}
	return def
}

func loadConfig() Config {
	stale, _ := strconv.Atoi(env("FREYA_STATUS_STALE_SECS", "90"))
	if stale <= 0 {
		stale = 90
	}
	return Config{
		BindAddr:           env("NET7SSL_BIND_ADDR", ":443"),
		Domain:             env("DOMAIN", "localhost"),
		CertDir:            env("NET7SSL_CERT_DIR", "/app/certs"),
		HTTPAddr:           env("FREYA_HTTP_ADDR", ":8080"),
		DBHost:             env("DB_HOST", "postgres:5432"),
		DBUser:             env("DB_USER", "net7"),
		DBPass:             env("DB_PASS", "net7"),
		DBUserName:         env("DB_NAME", "net7_user"),
		DBContent:          env("FREYA_DB_CONTENT", "net7"),
		WebRoot:            env("FREYA_WEB_ROOT", "/app/web"),
		KeepaliveEnabled:   env("NET7_IPC_KEEPALIVE", "") == "1",
		IPCSendSock:        env("NET7_IPC_SEND_SOCK", "/run/net7-ipc/net7.sock"),
		IPCRecvSock:        env("NET7_IPC_RECV_SOCK", "/run/net7-ipc/net7SSL.sock"),
		PatcherManifestURL: env("NET7_PATCHER_MANIFEST_URL", ""),
		PatcherDLBase:      strings.TrimRight(env("NET7_PATCHER_DL_BASE", ""), "/"),
		StatusStaleSecs:    stale,
	}
}

// dsn builds a libpq connection string for one of the two databases.
func (c Config) dsn(dbname string) string {
	host, port := c.DBHost, "5432"
	if i := strings.LastIndex(c.DBHost, ":"); i >= 0 {
		host = c.DBHost[:i]
		if p := c.DBHost[i+1:]; p != "" {
			port = p
		}
	}
	return "host=" + host + " port=" + port +
		" user=" + c.DBUser + " password=" + c.DBPass + " dbname=" + dbname
}
