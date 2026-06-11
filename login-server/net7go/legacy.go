// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Copyright (c) 2010 Net-7 Entertainment, Ltd.
//
// net7go -- standalone Go reimplementation of Net7SSL (see config.go header).
//
// legacy.go -- byte-exact reimplementation of the Net7SSL game-auth endpoints.
// These responses go on the wire to the real client.exe / launcher / sector
// server, so every byte MUST match the C++ Net7SSL (login-server/Net7SSL/
// LinuxAuth.cpp). We dispatch by substring on the raw request-URI in the same
// order the C++ did (HandleHttpsRequest), and write the response with the
// connection hijacked so net/http injects nothing of its own. The Freya Online
// TLS terminator copies these bytes verbatim back to the client.
//
// Primary source for the exact bytes: login-server/Net7SSL/LinuxAuth.cpp
// (HttpResult / MakeNotFound / MakeServiceUnavailable / HandleAuthLogin /
// HandleTouchSession / HandleSectorServer / HandleCertificate /
// HandleUpdateCheck) and the tag/version macros in SSL_Connection.h + Net7SSL.h.
package main

import (
	"context"
	"io"
	"log"
	"net/http"
	"strconv"
	"strings"
	"time"
)

// Field tags -- verbatim from SSL_Connection.h.
const (
	tagUsername   = "username="
	tagPassword   = "password="
	tagPort       = "port="
	tagMaxSectors = "max_sectors="
	tagVersion    = "version="
	tagLkey       = "lkey="
)

// Sector server version the auth endpoint expects -- Net7SSL.h
// SECTOR_SERVER_MAJOR_VERSION.SECTOR_SERVER_MINOR_VERSION.
const sectorServerVersion = "0.2"

// extractField mimics the C++ strstr(recv,TAG)+strtok(p,"& \r\n") parse: find
// the tag in the raw request URI, then take bytes up to the first delimiter.
// No URL-decoding -- the legacy client does not percent-encode these fields and
// the C++ fed the raw bytes straight to the account check, so we must too.
func extractField(raw, tag string) (string, bool) {
	i := strings.Index(raw, tag)
	if i < 0 {
		return "", false
	}
	s := raw[i+len(tag):]
	j := strings.IndexAny(s, "& \r\n")
	if j >= 0 {
		s = s[:j]
	}
	return s, true
}

// httpResult builds the exact HttpResult() byte sequence.
func httpResult(body, contentType string) []byte {
	return []byte("HTTP/1.1 200 OK\r\n" +
		"Content-Type: " + contentType + "\r\n" +
		"Server: AuthServer/2.5\r\n" +
		"Content-Length: " + strconv.Itoa(len(body)) + "\r\n" +
		"\r\n" +
		body)
}

// MakeNotFound -- verbatim 404 with the trailing "\r\n" body (Content-Length: 2).
var legacyNotFound = []byte(
	"HTTP/1.1 404 File Not Found\r\n" +
		"Server: AuthServer/2.5\r\n" +
		"Keep-Alive: timeout=15, max=100\r\n" +
		"Connection: Keep-Alive\r\n" +
		"Content-Length: 2\r\n" +
		"Content-type: text/plain\r\n" +
		"\r\n" +
		"\r\n")

// MakeServiceUnavailable -- verbatim 503.
var legacyServiceUnavailable = []byte(
	"HTTP/1.1 503 Service Unavailable\r\n" +
		"Server: AuthServer/2.5\r\n" +
		"Content-Length: 0\r\n" +
		"Connection: Keep-Alive\r\n" +
		"\r\n")

// touchSessionOK -- verbatim chunked "Success" body.
var touchSessionOK = []byte(
	"HTTP/1.1 200 \r\n" +
		"Server: AuthServer/2.5\r\n" +
		"Content-Type: text/plain; charset=ISO-8859-1\r\n" +
		"Transfer-Encoding: chunked\r\n" +
		"\r\n" +
		"7\r\n" +
		"Success\r\n" +
		"0\r\n" +
		"\r\n")

// legacyServer carries the deps the legacy handlers need.
type legacyServer struct {
	store    *Store
	cfg      Config
	manifest *PatcherManifest
}

// tryLegacy dispatches the legacy game-auth endpoints by substring on the raw
// request URI, matching HandleHttpsRequest's order. Returns true if it handled
// the request (and has already written/hijacked the response).
func (l *legacyServer) tryLegacy(w http.ResponseWriter, r *http.Request) bool {
	uri := r.URL.RequestURI()

	switch {
	case strings.Contains(uri, "/AuthLogin"):
		l.writeRaw(w, l.handleAuthLogin(r, uri))
		return true
	case strings.Contains(uri, "/touchsession.jsp"):
		if _, ok := extractField(uri, tagLkey); ok {
			l.writeRaw(w, touchSessionOK)
			return true
		}
		// No lkey -> C++ returned nullptr and fell through to 404.
		l.writeRaw(w, legacyNotFound)
		return true
	case strings.Contains(uri, "/sectorserver.cgi"):
		l.writeRaw(w, l.handleSectorServer(uri))
		return true
	case strings.Contains(uri, "certificate.html"):
		l.writeRaw(w, l.handleCertificate())
		return true
	case strings.Contains(uri, "/updateCheck"):
		l.writeRaw(w, l.handleUpdateCheck(r))
		return true
	case strings.Contains(uri, "/who.cgi"):
		// Linux no-op by design (see LinuxAuth.cpp) -> 404 fall-through.
		l.writeRaw(w, legacyNotFound)
		return true
	}
	return false
}

// writeRaw hijacks the connection and writes response verbatim, so no net/http
// headers are added. Falls back to a plain write if hijacking is unavailable.
func (l *legacyServer) writeRaw(w http.ResponseWriter, response []byte) {
	hj, ok := w.(http.Hijacker)
	if !ok {
		// Should not happen on net/http's default server; degrade safely.
		w.Header()["Content-Type"] = nil
		_, _ = w.Write(response)
		return
	}
	conn, buf, err := hj.Hijack()
	if err != nil {
		log.Printf("legacy: hijack failed: %v", err)
		return
	}
	defer conn.Close()
	if _, err := buf.Write(response); err != nil {
		log.Printf("legacy: write failed: %v", err)
		return
	}
	_ = buf.Flush()
}

// handleAuthLogin -- HandleAuthLogin. Verifies the account, issues + persists a
// ticket, returns "Valid=TRUE\r\nTicket=<user>-<hex>\r\n" or "Valid=False\r\n".
func (l *legacyServer) handleAuthLogin(r *http.Request, uri string) []byte {
	const fail = "Valid=False\r\n"

	user, uok := extractField(uri, tagUsername)
	pass, pok := extractField(uri, tagPassword)
	if !uok || !pok {
		return httpResult(fail, "text/plain")
	}

	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
	defer cancel()

	phc, err := l.store.passwordPHC(ctx, user)
	if err != nil {
		log.Printf("LinuxAuth: ValidateAccount failed for %q", user)
		return httpResult(fail, "text/plain")
	}
	ok, err := verifyPassword(phc, pass)
	if err != nil || !ok {
		log.Printf("LinuxAuth: ValidateAccount failed for %q", user)
		return httpResult(fail, "text/plain")
	}

	ticket, err := newTicket(user)
	if err != nil {
		log.Printf("LinuxAuth: ticket gen failed for %q: %v", user, err)
		return httpResult(fail, "text/plain")
	}
	// Split on the FIRST '-' (game-server strtok rule): key=before, token=after.
	if dash := strings.IndexByte(ticket, '-'); dash >= 0 {
		key := ticket[:dash]
		token := ticket[dash+1:]
		expiry := time.Now().UnixMilli() + ticketExpireMs
		if err := l.store.upsertTicket(ctx, key, token, expiry); err != nil {
			log.Printf("LinuxAuth: StoreLoginTicket failed for %q: %v", user, err)
			return httpResult(fail, "text/plain")
		}
	}

	log.Printf("LinuxAuth: ticket issued for %q", user)
	return httpResult("Valid=TRUE\r\nTicket="+ticket+"\r\n", "text/plain")
}

// handleSectorServer -- HandleSectorServer. Validates version/port/max_sectors
// and returns the exact Success/error body. (Registration is informational
// here; the Go login does not own the sector registry table.)
func (l *legacyServer) handleSectorServer(uri string) []byte {
	info := "Success=FALSE\r\n"

	username, uok := extractField(uri, tagUsername)
	port, pok := extractField(uri, tagPort)
	maxSectors, mok := extractField(uri, tagMaxSectors)
	version, vok := extractField(uri, tagVersion)

	if !(uok && pok && mok && vok) {
		return httpResult(info+"Invalid parameters\r\n", "text/plain")
	}

	if version != sectorServerVersion {
		return httpResult(info+"Expected Sector Server version is "+sectorServerVersion+"\r\n", "text/plain")
	}

	portNum, perr := strconv.Atoi(port)
	nSectors, serr := strconv.Atoi(maxSectors)
	if perr != nil || serr != nil || portNum < 3500 || nSectors <= 0 {
		return httpResult(info+"Port number must be 3500 or above\r\n", "text/plain")
	}
	// C++ re-checks that atoi round-trips (rejects "3500x"). strconv.Atoi
	// already rejects trailing junk, so portNum is exact here.
	log.Printf("LinuxAuth: RegisterSectorServer user=%s port=%d", username, portNum)
	return httpResult("Success=TRUE\r\n", "text/plain")
}

// handleCertificate -- HandleCertificate.
func (l *legacyServer) handleCertificate() []byte {
	body := "<html>\r\n" +
		"<head>\r\n" +
		"<META HTTP-EQUIV=\"Pragma\" CONTENT=\"no-cache\">\r\n" +
		"</head>\r\n" +
		"<body>\r\n" +
		"<h3><tt>" + l.cfg.Domain + " certificate successfully installed!</tt></h3>\r\n" +
		"<h2>Please close the browser to continue<h2>\r\n" +
		"</body>\r\n" +
		"</html>\r\n"
	return httpResult(body, "text/html")
}

// hashEq is case-insensitive hex equality for SHA-512 strings. Empty never
// matches (a missing local hash from the client must read as a mismatch).
func hashEq(a, b string) bool {
	if a == "" || b == "" || len(a) != len(b) {
		return false
	}
	return strings.EqualFold(a, b)
}

// handleUpdateCheck -- HandleUpdateCheck. POST body:
//
//	{"launcherHash","proxyHash","posFeedHash","injectHash","enbmodHash"}
//
// Compares the client's local hashes to the manifest cache and replies
// UP_TO_DATE when all match, else UPDATE_NEEDED with a conditional file list.
// Cache miss (manifest not loaded) -> 503 (fail-closed). Mirrors the C++
// LinuxAuth.cpp HandleUpdateCheck byte-for-byte in the JSON it emits.
func (l *legacyServer) handleUpdateCheck(r *http.Request) []byte {
	mf := l.manifest
	if mf == nil {
		return legacyServiceUnavailable
	}
	// Lazily re-fetch (TTL-bounded) so a client-only patch -- which never
	// changes the login image, so never triggers a restart on deploy -- is
	// still picked up here without a manual bounce.
	mf.refreshIfStale()
	if mf.Empty() {
		return legacyServiceUnavailable
	}

	body := readBody(r)
	clientLauncher := jsonField(body, "launcherHash")
	clientProxy := jsonField(body, "proxyHash")
	clientPosFeed := jsonField(body, "posFeedHash")
	clientInject := jsonField(body, "injectHash")
	clientEnbmod := jsonField(body, "enbmodHash")

	launcherSrv, cfgSrv, proxySrv, posFeedSrv, injectSrv, enbmodSrv, base := mf.snapshot()

	launcherOk := hashEq(clientLauncher, launcherSrv)
	proxyOk := hashEq(clientProxy, proxySrv)
	// The MVAS pair and enbmod.dll are checked INDEPENDENTLY, like the proxy. An
	// unpublished file (empty server hash) is treated as up-to-date: there is
	// nothing to ship and we never emit a file the manifest does not vouch for.
	posFeedOk := posFeedSrv == "" || hashEq(clientPosFeed, posFeedSrv)
	injectOk := injectSrv == "" || hashEq(clientInject, injectSrv)
	enbmodOk := enbmodSrv == "" || hashEq(clientEnbmod, enbmodSrv)

	var json string
	if launcherOk && proxyOk && posFeedOk && injectOk && enbmodOk {
		json = `{"status":"UP_TO_DATE"}`
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
		json = `{"status":"UPDATE_NEEDED","files":[` + strings.Join(files, ",") + `]}`
	}

	return httpResult(json, "application/json")
}

// readBody slurps the (capped) request body once. The launcher's /updateCheck
// POST is a tiny fixed JSON blob.
func readBody(r *http.Request) string {
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

// jsonField extracts a JSON string field from the POST body. The launcher sends
// a fixed, self-describing contract; the extracted value is ONLY ever compared,
// never reflected into the response. Mirrors LinuxAuth.cpp JsonField.
func jsonField(text, field string) string {
	needle := `"` + field + `"`
	i := strings.Index(text, needle)
	if i < 0 {
		return ""
	}
	j := strings.IndexByte(text[i+len(needle):], ':')
	if j < 0 {
		return ""
	}
	p := i + len(needle) + j + 1
	for p < len(text) && (text[p] == ' ' || text[p] == '\t' || text[p] == '\n' || text[p] == '\r') {
		p++
	}
	if p >= len(text) || text[p] != '"' {
		return ""
	}
	p++
	end := strings.IndexByte(text[p:], '"')
	if end < 0 {
		return ""
	}
	return text[p : p+end]
}
