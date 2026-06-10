// SPDX-License-Identifier: MIT
// Freya Online -- static SPA serving with client-side-routing fallback.
//
// The built Vite bundle (web/dist) is served from FREYA_WEB_ROOT. Hashed asset
// requests resolve to real files; any other path (the SPA's client routes)
// falls back to index.html so a deep link / refresh still loads the app. There
// is no SSR -- index.html is served verbatim and React hydrates on the client.

package main

import (
	"net/http"
	"os"
	"path/filepath"
	"strings"
)

type spaHandler struct {
	root  string
	index string
}

func newSPAHandler(root string) *spaHandler {
	return &spaHandler{root: root, index: filepath.Join(root, "index.html")}
}

func (h *spaHandler) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	if h.root == "" {
		http.NotFound(w, r)
		return
	}
	// Clean the path and reject traversal out of root.
	clean := filepath.Clean("/" + strings.TrimPrefix(r.URL.Path, "/"))
	target := filepath.Join(h.root, clean)
	if !strings.HasPrefix(target, filepath.Clean(h.root)+string(os.PathSeparator)) && target != filepath.Clean(h.root) {
		http.NotFound(w, r)
		return
	}

	if info, err := os.Stat(target); err == nil && !info.IsDir() {
		http.ServeFile(w, r, target)
		return
	}
	// SPA fallback -- serve index.html for client-side routes.
	http.ServeFile(w, r, h.index)
}
