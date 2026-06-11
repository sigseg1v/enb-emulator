// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Copyright (c) 2010 Net-7 Entertainment, Ltd.
//
// net7go -- standalone Go reimplementation of Net7SSL (see config.go header).
//
// main.go -- entry point. net7go is the standalone game-auth server: it serves
// ONLY the legacy Net7SSL endpoints (legacy.go), byte-for-byte, over plain HTTP
// behind a TLS terminator (the Go Freya Online server, which raw-relays the
// legacy URIs here and copies our response verbatim back to the client). It
// also runs the AF_UNIX server-liveness keepalive (keepalive.go) and the
// launcher self-update manifest cache (manifest.go).
//
// This binary links NO Freya/MIT code -- it is a self-contained CC BY-NC-SA 3.0
// replacement for login-server/Net7SSL and is independently distributable.
package main

import (
	"context"
	"errors"
	"log"
	"net/http"
	"os"
	"os/signal"
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
		log.Fatalf("net7go: open store: %v", err)
	}
	defer store.Close()

	// Phase AN: populate the launcher self-update hash cache once at startup.
	// Failure is non-fatal -- /updateCheck then reports the server DOWN (503)
	// so the launcher fails closed; the login/auth path is unaffected.
	manifest := newPatcherManifest(cfg)
	if !manifest.Load() {
		log.Printf("net7go: patcher manifest not loaded; /updateCheck will report DOWN")
	}

	legacy := &legacyServer{store: store, cfg: cfg, manifest: manifest}

	// net7go serves ONLY the legacy endpoints. Anything else gets the verbatim
	// 404 (the same the C++ login returned for unknown paths) -- the Freya
	// Online relay only forwards legacy URIs here, so this is defence in depth.
	root := http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if legacy.tryLegacy(w, r) {
			return
		}
		legacy.writeRaw(w, legacyNotFound)
	})

	if cfg.KeepaliveEnabled {
		startKeepalive(ctx, cfg)
	} else {
		log.Printf("net7go: keepalive disabled (NET7_IPC_KEEPALIVE=0)")
	}

	srv := &http.Server{
		Addr:              cfg.Addr,
		Handler:           root,
		ReadHeaderTimeout: 10 * time.Second,
	}
	go func() {
		log.Printf("net7go: HTTP listening on %s (legacy game-auth, TLS terminated upstream)", cfg.Addr)
		if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			log.Fatalf("net7go: HTTP server: %v", err)
		}
	}()

	// Graceful shutdown.
	sig := make(chan os.Signal, 1)
	signal.Notify(sig, syscall.SIGINT, syscall.SIGTERM)
	<-sig
	log.Printf("net7go: shutting down")
	cancel()

	shutCtx, shutCancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer shutCancel()
	_ = srv.Shutdown(shutCtx)
}
