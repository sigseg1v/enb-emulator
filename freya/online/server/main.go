// SPDX-License-Identifier: MIT
// Freya Online -- login server + website entry point.
//
// One process serves three things, in this precedence on each request:
//  1. the legacy Net7SSL game-auth endpoints, byte-for-byte (legacy.go);
//  2. the website JSON API under /api/* (api.go), behind scs session middleware;
//  3. the React SPA static bundle with client-routing fallback (spa.go).
//
// It listens on TLS :443 (where the real client.exe / launcher reach the legacy
// endpoints AND where browsers reach the website) and, optionally, a plain-HTTP
// :8080 for local dev (vite proxies /api there) or behind a separate terminator.
//
// This is the Go replacement for login-server/Net7SSL. During coexistence it
// runs ALONGSIDE the C++ login; the AF_UNIX server-liveness keepalive stays OFF
// (only one process may own the recv socket) until the owner-confirmed cutover.

package main

import (
	"context"
	"crypto/tls"
	"errors"
	"log"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"strings"
	"syscall"
	"time"
)

// tlsErrorFilter drops the benign "http: TLS handshake error ... EOF" /
// "connection reset" / "i/o timeout" lines the stdlib TLS server emits for
// every bare-TCP probe -- docker healthchecks, the integration fixture's
// WaitForPortAsync, and `</dev/tcp/HOST/443>` readiness checks all open a
// socket and close it without completing a handshake. Those are not errors;
// logging them as "error" violates the "a log line that says Error must mean
// Error" rule. A genuine TLS misconfiguration ("remote error: tls: ...",
// "unsupported versions", "bad certificate") carries no EOF/reset/timeout
// suffix and still passes through to the real log.
type tlsErrorFilter struct{ out *log.Logger }

func (f tlsErrorFilter) Write(p []byte) (int, error) {
	line := string(p)
	if strings.Contains(line, "TLS handshake error") &&
		(strings.Contains(line, ": EOF") ||
			strings.Contains(line, "connection reset by peer") ||
			strings.Contains(line, "i/o timeout")) {
		return len(p), nil
	}
	f.out.Print(strings.TrimRight(line, "\n"))
	return len(p), nil
}

func main() {
	log.SetFlags(log.LstdFlags | log.LUTC)
	cfg := loadConfig()

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	store, err := openStore(ctx, cfg)
	if err != nil {
		log.Fatalf("freya-online: open store: %v", err)
	}
	defer store.Close()

	status := newStatusCache(store, cfg.StatusStaleSecs)
	galaxy := newGalaxyCache(store)
	api := newAPIServer(store, status, galaxy, cfg)
	legacy := &legacyProxy{upstream: cfg.LoginUpstream}
	if cfg.LoginUpstream == "" {
		log.Printf("freya-online: WARNING FREYA_LOGIN_UPSTREAM unset; legacy game-auth endpoints (AuthLogin, updateCheck, ...) are NOT relayed and will fall through to the SPA. Point it at the net7go service (e.g. net7go:8085).")
	}
	spa := newSPAHandler(cfg.WebRoot)

	// Top-level handler: legacy first (raw-relayed byte-exact to net7go), then
	// the API (session-wrapped), then the SPA.
	apiHandler := api.session.LoadAndSave(api.routes())
	root := http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if legacy.tryLegacy(w, r) {
			return
		}
		if len(r.URL.Path) >= 4 && r.URL.Path[:4] == "/api" {
			apiHandler.ServeHTTP(w, r)
			return
		}
		spa.ServeHTTP(w, r)
	})

	// Background sweepers: expire auctions and old mail.
	startSweepers(ctx, store)

	// AH faucet bots (FREYA_AH_BOTS=1): keep the Auction House stocked.
	if cfg.AhBotsEnabled {
		startBots(ctx, store)
	}

	servers := startServers(cfg, root)

	// Graceful shutdown.
	sig := make(chan os.Signal, 1)
	signal.Notify(sig, syscall.SIGINT, syscall.SIGTERM)
	<-sig
	log.Printf("freya-online: shutting down")
	cancel()

	shutCtx, shutCancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer shutCancel()
	for _, srv := range servers {
		_ = srv.Shutdown(shutCtx)
	}
}

// startServers brings up the TLS listener (always) and the plain-HTTP listener
// (when FREYA_HTTP_ADDR is set), returning them for graceful shutdown.
func startServers(cfg Config, handler http.Handler) []*http.Server {
	var servers []*http.Server

	certFile := filepath.Join(cfg.CertDir, cfg.Domain+".cer")
	keyFile := filepath.Join(cfg.CertDir, cfg.Domain+".pem")

	if _, err := os.Stat(certFile); err == nil {
		tlsSrv := &http.Server{
			Addr:              cfg.BindAddr,
			Handler:           handler,
			ReadHeaderTimeout: 10 * time.Second,
			TLSConfig:         &tls.Config{MinVersion: tls.VersionTLS12},
			ErrorLog:          log.New(tlsErrorFilter{out: log.Default()}, "", 0),
		}
		servers = append(servers, tlsSrv)
		go func() {
			log.Printf("freya-online: TLS listening on %s (cert %s)", cfg.BindAddr, certFile)
			if err := tlsSrv.ListenAndServeTLS(certFile, keyFile); err != nil && !errors.Is(err, http.ErrServerClosed) {
				log.Fatalf("freya-online: TLS server: %v", err)
			}
		}()
	} else {
		log.Printf("freya-online: no cert at %s; TLS listener disabled (set NET7SSL_CERT_DIR/DOMAIN)", certFile)
	}

	if cfg.HTTPAddr != "" {
		httpSrv := &http.Server{
			Addr:              cfg.HTTPAddr,
			Handler:           handler,
			ReadHeaderTimeout: 10 * time.Second,
		}
		servers = append(servers, httpSrv)
		go func() {
			log.Printf("freya-online: HTTP listening on %s", cfg.HTTPAddr)
			if err := httpSrv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
				log.Fatalf("freya-online: HTTP server: %v", err)
			}
		}()
	}

	return servers
}
