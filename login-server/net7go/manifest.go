// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Copyright (c) 2010 Net-7 Entertainment, Ltd.
// Modified by: Max Verigin, 2026 -- ported to go
//
// net7go -- standalone Go reimplementation of Net7SSL (see config.go header).
//
// manifest.go -- the launcher self-update hash cache. A direct port of the C++
// PatcherManifest (login-server/Net7SSL/PatcherManifest.{h,cpp}): it holds the
// authoritative SHA-512 hashes of the published Windows artifacts -- the
// required trio (FreyaLauncher.exe, FreyaLauncher.cfg, bin/FreyaProxy.exe) plus
// the optional MVAS injection pair (bin/FreyaPosFeed.dll, bin/FreyaInject.exe)
// and the optional Lua mod runtime (bin/enbmod.dll). The proxy, the MVAS pair,
// and enbmod.dll are each hashed and shipped INDEPENDENTLY; only the cfg rides
// with the launcher. The cache is loaded once at startup by an HTTP GET of a
// small manifest.json (NET7_PATCHER_MANIFEST_URL) and refreshed lazily on the
// /updateCheck path, TTL-bounded, so a client-only patch (which never changes
// the login image) is picked up without a restart.
//
// Fail-closed: until a successful Load(), Empty() is true and /updateCheck
// reports the server DOWN (HTTP 503). The endpoint never reads a client-chosen
// path and only ever returns these fixed, server-controlled entries.
package main

import (
	"encoding/json"
	"io"
	"log"
	"net/http"
	"strings"
	"sync"
	"time"
)

// Published relativePaths, keyed exactly as the manifest publishes them and the
// /updateCheck response echoes. The proxy / MVAS pair / enbmod live under bin/
// in the launcher's install layout but are flat in the bucket; the launcher
// maps the relativePath to its own tree on download.
const (
	relLauncherExe = "FreyaLauncher.exe"
	relLauncherCfg = "FreyaLauncher.cfg"
	relProxyExe    = "bin/FreyaProxy.exe"
	relPosFeedDll  = "bin/FreyaPosFeed.dll"
	relInjectExe   = "bin/FreyaInject.exe"
	relEnbmodDll   = "bin/enbmod.dll"
)

// Re-fetch the manifest at most this often on the request path.
const manifestRefreshTTL = 60 * time.Second

type manifestFileEntry struct {
	RelativePath string `json:"relativePath"`
	Sha512       string `json:"sha512"`
}

type manifestDoc struct {
	Files []manifestFileEntry `json:"files"`
}

// PatcherManifest is the in-memory hash cache. Reads on the request path are
// mutex-guarded; Load() overwrites state only on a fully successful fetch.
type PatcherManifest struct {
	url   string
	dlURL string // NET7_PATCHER_DL_BASE (login synthesizes the per-file URLs)

	mu          sync.Mutex
	loaded      bool
	lastAttempt time.Time // last Load() attempt (success or fail); gates refresh

	launcherExe string
	launcherCfg string
	proxyExe    string
	posFeedDll  string // "" when the manifest omits it
	injectExe   string // "" when the manifest omits it
	enbmodDll   string // "" when the manifest omits it (Lua mod runtime)
}

func newPatcherManifest(cfg Config) *PatcherManifest {
	return &PatcherManifest{url: cfg.PatcherManifestURL, dlURL: cfg.PatcherDLBase}
}

// Load fetches + parses the manifest. Returns true only when the required trio
// is present with non-empty hashes (the optional add-ons are parsed when
// present). A missing URL, a transport error, or a malformed manifest leaves
// the PRIOR cache intact (state overwritten only on full success) and returns
// false. Called once at startup and again by refreshIfStale() on the request
// path.
func (m *PatcherManifest) Load() bool {
	// Record the attempt up front so refreshIfStale() backs off for a full TTL
	// whether this load succeeds OR fails -- a CloudFront outage must not turn
	// every /updateCheck into an outbound fetch.
	m.mu.Lock()
	m.lastAttempt = time.Now()
	url := m.url
	m.mu.Unlock()

	if url == "" {
		log.Printf("PatcherManifest: NET7_PATCHER_MANIFEST_URL not set; /updateCheck disabled (server reports DOWN to the launcher)")
		return false
	}

	body, err := fetchManifest(url)
	if err != nil {
		log.Printf("PatcherManifest: fetch failed (%s): %v", url, err)
		return false
	}

	var doc manifestDoc
	if err := json.Unmarshal(body, &doc); err != nil {
		log.Printf("PatcherManifest: manifest at %s is not valid JSON: %v", url, err)
		return false
	}

	var launcherExe, launcherCfg, proxyExe, posFeedDll, injectExe, enbmodDll string
	for _, f := range doc.Files {
		if f.RelativePath == "" || f.Sha512 == "" {
			continue
		}
		switch f.RelativePath {
		case relLauncherExe:
			launcherExe = f.Sha512
		case relLauncherCfg:
			launcherCfg = f.Sha512
		case relProxyExe:
			proxyExe = f.Sha512
		case relPosFeedDll:
			posFeedDll = f.Sha512
		case relInjectExe:
			injectExe = f.Sha512
		case relEnbmodDll:
			enbmodDll = f.Sha512
		}
	}

	if launcherExe == "" || launcherCfg == "" || proxyExe == "" {
		log.Printf("PatcherManifest: manifest at %s is missing one of FreyaLauncher.exe / FreyaLauncher.cfg / bin/FreyaProxy.exe; refusing to serve a partial manifest", url)
		return false
	}

	m.mu.Lock()
	m.launcherExe = launcherExe
	m.launcherCfg = launcherCfg
	m.proxyExe = proxyExe
	m.posFeedDll = posFeedDll
	m.injectExe = injectExe
	m.enbmodDll = enbmodDll
	m.loaded = true
	m.mu.Unlock()

	total := 3
	for _, h := range []string{posFeedDll, injectExe, enbmodDll} {
		if h != "" {
			total++
		}
	}
	log.Printf("PatcherManifest: loaded %d file hashes from %s (dl base '%s')", total, url, m.dlURL)
	return true
}

// refreshIfStale re-Loads the manifest if the cache is older than the TTL,
// rate-limited to one fetch attempt per TTL. Cheap no-op when fresh.
func (m *PatcherManifest) refreshIfStale() {
	m.mu.Lock()
	fresh := time.Since(m.lastAttempt) < manifestRefreshTTL
	m.mu.Unlock()
	if fresh {
		return
	}
	m.Load()
}

func (m *PatcherManifest) Empty() bool {
	m.mu.Lock()
	defer m.mu.Unlock()
	return !m.loaded
}

// snapshot returns a consistent copy of the cached hashes under one lock.
func (m *PatcherManifest) snapshot() (launcherExe, launcherCfg, proxyExe, posFeedDll, injectExe, enbmodDll, dlBase string) {
	m.mu.Lock()
	defer m.mu.Unlock()
	return m.launcherExe, m.launcherCfg, m.proxyExe, m.posFeedDll, m.injectExe, m.enbmodDll, m.dlURL
}

// fetchManifest GETs the manifest body with the same guards as the C++ libcurl
// path: short timeout, follow redirects, cap the body size.
func fetchManifest(url string) ([]byte, error) {
	client := &http.Client{Timeout: 30 * time.Second}
	resp, err := client.Get(url)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	if resp.StatusCode >= 400 {
		return nil, &fetchError{status: resp.StatusCode}
	}
	const maxBody = 1 << 20 // 1 MiB; a manifest is tiny
	body, err := io.ReadAll(io.LimitReader(resp.Body, maxBody))
	if err != nil {
		return nil, err
	}
	if len(strings.TrimSpace(string(body))) == 0 {
		return nil, &fetchError{status: resp.StatusCode, empty: true}
	}
	return body, nil
}

type fetchError struct {
	status int
	empty  bool
}

func (e *fetchError) Error() string {
	if e.empty {
		return "empty manifest body"
	}
	return "HTTP status " + http.StatusText(e.status)
}
