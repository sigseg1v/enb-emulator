// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya
//
// updatecheck.go -- the FreyaLauncher self-update endpoint (/updateCheck) and its
// manifest hash cache.
//
// This is NOT legacy Net7SSL: retail E&B had no launcher self-update. The
// endpoint, the manifest schema, and the patch/mod delivery are all original
// Freya work (added in this project), so they live here in the MIT freya-online
// binary rather than in the CC BY-NC-SA net7go login server. freya-online owns
// the TLS :443 listener, so it handles /updateCheck directly -- the other legacy
// game-auth URIs (AuthLogin, ...) are still raw-relayed to net7go (legacy_proxy.go).
//
// The launcher POSTs its local artifact hashes; we compare them to the cached
// manifest and reply UP_TO_DATE or UPDATE_NEEDED with the files it must fetch.
// Both replies also carry the unconditional "mods" (enbmod Lua mods) and
// "patches" (operator game-data patches, e.g. enb-patch.exe) arrays, which the
// launcher reconciles itself.
//
// Fail-closed: until a successful Load(), the cache is empty and /updateCheck
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

// A published enbmod Lua mod, distributed as mods/<id>-<hash>.zip under the dl
// base. The launcher compares the hash to its local ./mods/<id>/modhash and only
// re-downloads on a mismatch, so a user's own mod (unknown id) is never touched.
type manifestModEntry struct {
	Id   string `json:"id"`
	Hash string `json:"hash"`
}

// An operator-supplied game-data patch (e.g. enb-patch.exe), distributed as
// patches/<name> under the dl base. The launcher applies it to the EnB install
// once, tracked by patchlevel.txt -- see Updater.ReconcilePatchesAsync.
type manifestPatchEntry struct {
	Name   string `json:"name"`
	Sha512 string `json:"sha512"`
}

type manifestDoc struct {
	Files   []manifestFileEntry  `json:"files"`
	Mods    []manifestModEntry   `json:"mods"`
	Patches []manifestPatchEntry `json:"patches"`
}

// patcherManifest is the in-memory hash cache. Reads on the request path are
// mutex-guarded; Load() overwrites state only on a fully successful fetch.
type patcherManifest struct {
	url   string
	dlURL string // the launcher synthesizes the per-file URLs from this base

	mu          sync.Mutex
	loaded      bool
	lastAttempt time.Time // last Load() attempt (success or fail); gates refresh

	launcherExe string
	launcherCfg string
	proxyExe    string
	posFeedDll  string // "" when the manifest omits it
	injectExe   string // "" when the manifest omits it
	enbmodDll   string // "" when the manifest omits it (Lua mod runtime)
	mods        []manifestModEntry   // optional (id, hash) Lua mods
	patches     []manifestPatchEntry // optional operator game-data patches
}

func newPatcherManifest(cfg Config) *patcherManifest {
	return &patcherManifest{url: cfg.PatcherManifestURL, dlURL: cfg.PatcherDLBase}
}

// Load fetches + parses the manifest. Returns true only when the required trio
// is present with non-empty hashes (the optional add-ons are parsed when
// present). A missing URL, a transport error, or a malformed manifest leaves
// the PRIOR cache intact (state overwritten only on full success) and returns
// false. Called once at startup and again by refreshIfStale() on the request
// path.
func (m *patcherManifest) Load() bool {
	// Record the attempt up front so refreshIfStale() backs off for a full TTL
	// whether this load succeeds OR fails -- a CloudFront outage must not turn
	// every /updateCheck into an outbound fetch.
	m.mu.Lock()
	m.lastAttempt = time.Now()
	url := m.url
	m.mu.Unlock()

	if url == "" {
		log.Printf("patcherManifest: FREYA_PATCHER_MANIFEST_URL not set; /updateCheck disabled (server reports DOWN to the launcher)")
		return false
	}

	body, err := fetchManifest(url)
	if err != nil {
		log.Printf("patcherManifest: fetch failed (%s): %v", url, err)
		return false
	}

	var doc manifestDoc
	if err := json.Unmarshal(body, &doc); err != nil {
		log.Printf("patcherManifest: manifest at %s is not valid JSON: %v", url, err)
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

	// Optional "mods" array of {id, hash}. An id becomes a URL segment and a
	// client directory name, so it must be a safe single path segment; anything
	// else is dropped rather than trusted.
	var mods []manifestModEntry
	for _, md := range doc.Mods {
		if md.Id == "" || md.Hash == "" || !isSafeSegment(md.Id) {
			continue
		}
		mods = append(mods, manifestModEntry{Id: md.Id, Hash: md.Hash})
	}

	// Optional "patches" array of {name, sha512}. A name becomes a URL segment
	// and a staged file name on the client, so the same safe-segment guard
	// applies.
	var patches []manifestPatchEntry
	for _, pt := range doc.Patches {
		if pt.Name == "" || pt.Sha512 == "" || !isSafeSegment(pt.Name) {
			continue
		}
		patches = append(patches, manifestPatchEntry{Name: pt.Name, Sha512: pt.Sha512})
	}

	if launcherExe == "" || launcherCfg == "" || proxyExe == "" {
		log.Printf("patcherManifest: manifest at %s is missing one of FreyaLauncher.exe / FreyaLauncher.cfg / bin/FreyaProxy.exe; refusing to serve a partial manifest", url)
		return false
	}

	m.mu.Lock()
	m.launcherExe = launcherExe
	m.launcherCfg = launcherCfg
	m.proxyExe = proxyExe
	m.posFeedDll = posFeedDll
	m.injectExe = injectExe
	m.enbmodDll = enbmodDll
	m.mods = mods
	m.patches = patches
	m.loaded = true
	m.mu.Unlock()

	total := 3
	for _, h := range []string{posFeedDll, injectExe, enbmodDll} {
		if h != "" {
			total++
		}
	}
	log.Printf("patcherManifest: loaded %d file hashes + %d mod(s) + %d patch(es) from %s (dl base '%s')", total, len(mods), len(patches), url, m.dlURL)
	return true
}

// refreshIfStale re-Loads the manifest if the cache is older than the TTL,
// rate-limited to one fetch attempt per TTL. Cheap no-op when fresh.
func (m *patcherManifest) refreshIfStale() {
	m.mu.Lock()
	fresh := time.Since(m.lastAttempt) < manifestRefreshTTL
	m.mu.Unlock()
	if fresh {
		return
	}
	m.Load()
}

func (m *patcherManifest) Empty() bool {
	m.mu.Lock()
	defer m.mu.Unlock()
	return !m.loaded
}

// snapshot returns a consistent copy of the cached hashes under one lock.
func (m *patcherManifest) snapshot() (launcherExe, launcherCfg, proxyExe, posFeedDll, injectExe, enbmodDll, dlBase string) {
	m.mu.Lock()
	defer m.mu.Unlock()
	return m.launcherExe, m.launcherCfg, m.proxyExe, m.posFeedDll, m.injectExe, m.enbmodDll, m.dlURL
}

func (m *patcherManifest) Mods() []manifestModEntry {
	m.mu.Lock()
	defer m.mu.Unlock()
	return append([]manifestModEntry(nil), m.mods...)
}

func (m *patcherManifest) Patches() []manifestPatchEntry {
	m.mu.Lock()
	defer m.mu.Unlock()
	return append([]manifestPatchEntry(nil), m.patches...)
}

// fetchManifest GETs the manifest body with sane guards: short timeout, follow
// redirects, cap the body size (a manifest is tiny; refuse a runaway).
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
	const maxBody = 1 << 20 // 1 MiB
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

// isSafeSegment reports whether s is a single safe path/URL segment: only
// [A-Za-z0-9._-], no separators or traversal. A mod id or patch name from the
// manifest is interpolated into a download URL and a client file/dir name, so it
// must be constrained before it is trusted. Mirrors the launcher's IsSafeModId /
// IsSafePatchName.
func isSafeSegment(s string) bool {
	if s == "" || s == "." || s == ".." {
		return false
	}
	for _, c := range s {
		switch {
		case c >= 'a' && c <= 'z', c >= 'A' && c <= 'Z', c >= '0' && c <= '9':
		case c == '.' || c == '_' || c == '-':
		default:
			return false
		}
	}
	return true
}

// patcherHashEq is case-insensitive hex equality for SHA-512 strings. Empty
// never matches (a missing local hash from the launcher must read as a mismatch).
func patcherHashEq(a, b string) bool {
	if a == "" || b == "" || len(a) != len(b) {
		return false
	}
	return strings.EqualFold(a, b)
}

// handleUpdateCheck serves /updateCheck. POST body:
//
//	{"launcherHash","proxyHash","posFeedHash","injectHash","enbmodHash"}
//
// Compares the launcher's local hashes to the manifest cache and replies
// UP_TO_DATE when all match, else UPDATE_NEEDED with a conditional file list.
// Both replies also carry the unconditional "mods" and "patches" arrays. Cache
// miss (manifest not loaded) -> 503 (fail-closed). The launcher parses this with
// a normal HTTP client, so a standard JSON response is fine (unlike the legacy
// game-auth endpoints, this is not consumed by the retail client.exe).
func (m *patcherManifest) handleUpdateCheck(w http.ResponseWriter, r *http.Request) {
	// Lazily re-fetch (TTL-bounded) so a client-only patch -- which never changes
	// the login image, so never triggers a restart on deploy -- is still picked
	// up here without a manual bounce.
	m.refreshIfStale()
	if m.Empty() {
		w.WriteHeader(http.StatusServiceUnavailable)
		return
	}

	body := readBodyCapped(r)
	clientLauncher := patcherJSONField(body, "launcherHash")
	clientProxy := patcherJSONField(body, "proxyHash")
	clientPosFeed := patcherJSONField(body, "posFeedHash")
	clientInject := patcherJSONField(body, "injectHash")
	clientEnbmod := patcherJSONField(body, "enbmodHash")

	launcherSrv, cfgSrv, proxySrv, posFeedSrv, injectSrv, enbmodSrv, base := m.snapshot()

	// The published Lua mods and operator patches ride in the response
	// UNCONDITIONALLY. The launcher reconciles each itself: a mod by comparing its
	// local ./mods/<id>/modhash, a patch by consulting the EnB install's
	// patchlevel.txt -- so we always tell it what exists and let it decide.
	var modsBuf []string
	for _, md := range m.Mods() {
		modsBuf = append(modsBuf, `{"id":"`+md.Id+`","hash":"`+md.Hash+
			`","url":"`+base+"/mods/"+md.Id+"-"+md.Hash+".zip"+`"}`)
	}
	modsField := `,"mods":[` + strings.Join(modsBuf, ",") + `]`

	var patchesBuf []string
	for _, pt := range m.Patches() {
		patchesBuf = append(patchesBuf, `{"name":"`+pt.Name+`","hash":"`+pt.Sha512+
			`","url":"`+base+"/patches/"+pt.Name+`"}`)
	}
	patchesField := `,"patches":[` + strings.Join(patchesBuf, ",") + `]`

	launcherOk := patcherHashEq(clientLauncher, launcherSrv)
	proxyOk := patcherHashEq(clientProxy, proxySrv)
	// The MVAS pair and enbmod.dll are checked INDEPENDENTLY, like the proxy. An
	// unpublished file (empty server hash) is treated as up-to-date: there is
	// nothing to ship and we never emit a file the manifest does not vouch for.
	posFeedOk := posFeedSrv == "" || patcherHashEq(clientPosFeed, posFeedSrv)
	injectOk := injectSrv == "" || patcherHashEq(clientInject, injectSrv)
	enbmodOk := enbmodSrv == "" || patcherHashEq(clientEnbmod, enbmodSrv)

	var out string
	if launcherOk && proxyOk && posFeedOk && injectOk && enbmodOk {
		out = `{"status":"UP_TO_DATE"` + modsField + patchesField + `}`
	} else {
		var files []string
		add := func(rel, url, hash string) {
			files = append(files, `{"relativePath":"`+rel+`","url":"`+url+`","hash":"`+hash+`"}`)
		}
		if !launcherOk {
			add(relLauncherExe, base+"/FreyaLauncher.exe", launcherSrv)
			// The cfg has no independent hash and rides with the launcher.
			add(relLauncherCfg, base+"/FreyaLauncher.cfg", cfgSrv)
		}
		if !proxyOk {
			add(relProxyExe, base+"/FreyaProxy.exe", proxySrv)
		}
		if !posFeedOk {
			add(relPosFeedDll, base+"/FreyaPosFeed.dll", posFeedSrv)
		}
		if !injectOk {
			add(relInjectExe, base+"/FreyaInject.exe", injectSrv)
		}
		if !enbmodOk {
			add(relEnbmodDll, base+"/enbmod.dll", enbmodSrv)
		}
		out = `{"status":"UPDATE_NEEDED","files":[` + strings.Join(files, ",") + `]` + modsField + patchesField + `}`
	}

	w.Header().Set("Content-Type", "application/json")
	_, _ = io.WriteString(w, out)
}

// readBodyCapped slurps the (capped) request body once. The launcher's
// /updateCheck POST is a tiny fixed JSON blob.
func readBodyCapped(r *http.Request) string {
	if r.Body == nil {
		return ""
	}
	body, err := io.ReadAll(io.LimitReader(r.Body, 1<<16))
	r.Body.Close()
	if err != nil {
		return ""
	}
	return string(body)
}

// patcherJSONField extracts a JSON string field from the POST body. The launcher
// sends a fixed, self-describing contract; the extracted value is ONLY ever
// compared, never reflected into the response.
func patcherJSONField(text, field string) string {
	needle := `"` + field + `"`
	i := strings.Index(text, needle)
	if i < 0 {
		return ""
	}
	j := strings.IndexByte(text[i+len(needle):], ':')
	if j < 0 {
		return ""
	}
	rest := text[i+len(needle)+j+1:]
	q1 := strings.IndexByte(rest, '"')
	if q1 < 0 {
		return ""
	}
	rest = rest[q1+1:]
	q2 := strings.IndexByte(rest, '"')
	if q2 < 0 {
		return ""
	}
	return rest[:q2]
}
