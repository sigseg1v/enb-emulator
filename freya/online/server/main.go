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
	"syscall"
	"time"
)

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
	api := newAPIServer(store, status, cfg)
	legacy := &legacyServer{store: store, cfg: cfg}
	spa := newSPAHandler(cfg.WebRoot)

	// Top-level handler: legacy first (it hijacks and writes raw bytes), then
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

	if cfg.KeepaliveEnabled {
		// The C++ login owns the AF_UNIX recv socket during coexistence; the Go
		// keepalive sender is not yet ported. Website status reads server_status
		// directly, so this only matters to the game server's view of login
		// liveness at cutover. Fail loud rather than silently pretend.
		log.Printf("freya-online: WARNING NET7_IPC_KEEPALIVE=1 but the keepalive sender is not yet ported; leave the C++ login running to own it until cutover")
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
