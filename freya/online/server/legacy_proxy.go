// SPDX-License-Identifier: MIT
// Freya Online -- raw byte-exact relay to the standalone net7go login server.
//
// The legacy Net7SSL game-auth endpoints (/AuthLogin, /touchsession.jsp,
// /sectorserver.cgi, certificate.html, /who.cgi) are NOT implemented here --
// that logic is a derivative of Net7SSL and lives in the separate CC BY-NC-SA
// 3.0 binary at login-server/net7go. Freya Online owns the TLS :443 listener (it
// terminates TLS for both the website AND these legacy endpoints), so it must
// forward the decrypted legacy requests to net7go and stream net7go's response
// back to the client.
//
// NOTE: /updateCheck is deliberately NOT in this list. The FreyaLauncher
// self-update endpoint is original Freya work (retail E&B had no launcher
// self-update), so freya-online serves it directly (MIT) -- see updatecheck.go.
//
// The forwarding is a RAW byte relay, deliberately NOT httputil.ReverseProxy:
// net7go writes a hand-framed HTTP response (exact status line, exact header
// order, `Server: AuthServer/2.5`, no Date) that the real client.exe parses
// byte-for-byte. ReverseProxy would re-frame those headers and break the wire
// format. So we hijack the client connection and io.Copy net7go's bytes onto it
// verbatim -- whatever net7go emits is exactly what the client receives.
//
// Knowing WHICH URIs to forward is routing config (the client's endpoint
// paths), not Net7SSL's implementation, so this list lives in MIT code.

package main

import (
	"io"
	"log"
	"net"
	"net/http"
	"strings"
	"time"
)

// legacyProxy raw-relays the legacy game-auth endpoints to net7go.
type legacyProxy struct {
	upstream string // host:port of net7go (FREYA_LOGIN_UPSTREAM); "" disables
}

// legacyURIMarkers are matched (substring) in the same order net7go dispatches.
var legacyURIMarkers = []string{
	"/AuthLogin",
	"/touchsession.jsp",
	"/sectorserver.cgi",
	"certificate.html",
	"/who.cgi",
}

func isLegacyURI(uri string) bool {
	for _, m := range legacyURIMarkers {
		if strings.Contains(uri, m) {
			return true
		}
	}
	return false
}

// tryLegacy forwards the request to net7go if it is a legacy URI and an
// upstream is configured. Returns true if it handled (hijacked) the request.
func (p *legacyProxy) tryLegacy(w http.ResponseWriter, r *http.Request) bool {
	if p.upstream == "" || !isLegacyURI(r.URL.RequestURI()) {
		return false
	}
	p.relay(w, r)
	return true
}

// relay forwards r to net7go and copies net7go's raw response back to the
// client verbatim (byte-exact). Both legs get a deadline so a wedged net7go
// can't pin a client connection forever.
func (p *legacyProxy) relay(w http.ResponseWriter, r *http.Request) {
	up, err := net.DialTimeout("tcp", p.upstream, 5*time.Second)
	if err != nil {
		log.Printf("legacy-proxy: dial %s: %v", p.upstream, err)
		http.Error(w, "", http.StatusBadGateway)
		return
	}
	defer up.Close()
	_ = up.SetDeadline(time.Now().Add(15 * time.Second))

	// AR-1: hand net7go the real client IP for its per-IP /AuthLogin throttle.
	// net7go only ever sees this relay as its peer (RemoteAddr), so without this
	// it could not tell players apart. We are the TLS edge: r.RemoteAddr is the
	// genuine client IP (no reverse proxy fronts freya-online -- it binds :443
	// directly). OVERWRITE any client-supplied value with Set so a client cannot
	// spoof the header to dodge throttling. Must match net7go's clientIPHeader.
	if host, _, err := net.SplitHostPort(r.RemoteAddr); err == nil {
		r.Header.Set("X-Freya-Client-IP", host)
	} else {
		r.Header.Set("X-Freya-Client-IP", r.RemoteAddr)
	}

	// Forward the request. RequestURI is set on server-received requests but
	// Request.Write rejects it; clear it. Force Close so net7go's single-shot
	// hijacked response (it closes the conn after writing) terminates our read.
	r.RequestURI = ""
	r.Close = true
	if err := r.Write(up); err != nil {
		log.Printf("legacy-proxy: forward request: %v", err)
		return
	}

	hj, ok := w.(http.Hijacker)
	if !ok {
		log.Printf("legacy-proxy: ResponseWriter is not a Hijacker; cannot preserve byte-exact response")
		return
	}
	conn, _, err := hj.Hijack()
	if err != nil {
		log.Printf("legacy-proxy: hijack client conn: %v", err)
		return
	}
	defer conn.Close()
	_ = conn.SetDeadline(time.Now().Add(15 * time.Second))

	// Copy net7go's raw bytes straight to the client -- no parsing, no header
	// injection. This is what keeps the response byte-exact.
	if _, err := io.Copy(conn, up); err != nil {
		log.Printf("legacy-proxy: relay response: %v", err)
	}
}
