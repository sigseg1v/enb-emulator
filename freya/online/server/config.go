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

	// Standalone net7go login server (login-server/net7go) host:port. Freya
	// Online terminates TLS and raw-relays the legacy game-auth URIs to it (see
	// legacy_proxy.go). Empty disables the relay -- legacy endpoints then fall
	// through to the SPA, which is wrong for a live stack, so it is logged at
	// startup. The legacy auth/ticket/keepalive/manifest logic is NOT here; it
	// is the separate CC BY-NC-SA binary at login-server/net7go.
	LoginUpstream string // FREYA_LOGIN_UPSTREAM, default "" (e.g. "net7go:8085")

	// Treat the game server as ONLINE only if server_status.updated_at is within
	// this many seconds. The server heartbeats that row.
	StatusStaleSecs int // FREYA_STATUS_STALE_SECS, default 90

	// AH faucet bots. When on, the AhBot account keeps the Auction House stocked
	// with a rarity/quality/price-distributed pool of listings (see bots.go).
	AhBotsEnabled bool // FREYA_AH_BOTS=1
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
		BindAddr:        env("NET7SSL_BIND_ADDR", ":443"),
		Domain:          env("DOMAIN", "localhost"),
		CertDir:         env("NET7SSL_CERT_DIR", "/app/certs"),
		HTTPAddr:        env("FREYA_HTTP_ADDR", ":8080"),
		DBHost:          env("DB_HOST", "postgres:5432"),
		DBUser:          env("DB_USER", "net7"),
		DBPass:          env("DB_PASS", "net7"),
		DBUserName:      env("DB_NAME", "net7_user"),
		DBContent:       env("FREYA_DB_CONTENT", "net7"),
		WebRoot:         env("FREYA_WEB_ROOT", "/app/web"),
		LoginUpstream:   env("FREYA_LOGIN_UPSTREAM", ""),
		StatusStaleSecs: stale,
		AhBotsEnabled:   env("FREYA_AH_BOTS", "") == "1",
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
