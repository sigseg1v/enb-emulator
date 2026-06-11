// SPDX-License-Identifier: MIT
package main

import (
	"bufio"
	"io"
	"net"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

// fakeNet7go stands in for the net7go upstream: it accepts one connection, reads
// the request headers, then writes a hand-framed response verbatim and closes
// (exactly net7go's single-shot hijack+write behaviour). It returns its addr.
func fakeNet7go(t *testing.T, rawResponse string) string {
	t.Helper()
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("listen: %v", err)
	}
	t.Cleanup(func() { ln.Close() })
	go func() {
		for {
			c, err := ln.Accept()
			if err != nil {
				return
			}
			go func(c net.Conn) {
				defer c.Close()
				// Drain request headers up to the blank line so we don't reset
				// the peer before reading; we don't otherwise inspect them.
				br := bufio.NewReader(c)
				for {
					line, err := br.ReadString('\n')
					if err != nil || line == "\r\n" || line == "\n" {
						break
					}
				}
				_, _ = c.Write([]byte(rawResponse))
			}(c)
		}
	}()
	return ln.Addr().String()
}

// The relay must hand the client net7go's response bytes verbatim -- no
// net/http re-framing, no injected Date/Content-Length, exact header order.
func TestRelayIsByteExact(t *testing.T) {
	// A response shaped like net7go's httpResult -- note no Date header and the
	// AuthServer/2.5 server token, which net/http would never emit on its own.
	want := "HTTP/1.1 200 OK\r\n" +
		"Content-Type: text/plain\r\n" +
		"Server: AuthServer/2.5\r\n" +
		"Content-Length: 18\r\n" +
		"\r\n" +
		"Valid=TRUE\r\nx=1\r\n"

	upstream := fakeNet7go(t, want)
	proxy := &legacyProxy{upstream: upstream}

	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if proxy.tryLegacy(w, r) {
			return
		}
		http.NotFound(w, r)
	}))
	t.Cleanup(srv.Close)

	// Raw TCP request so we observe the exact bytes the client would see (an
	// http.Client would parse + normalise them and hide a re-framing bug).
	host := strings.TrimPrefix(srv.URL, "http://")
	conn, err := net.DialTimeout("tcp", host, 5*time.Second)
	if err != nil {
		t.Fatalf("dial test server: %v", err)
	}
	defer conn.Close()
	_, _ = conn.Write([]byte("GET /AuthLogin?username=a&password=b HTTP/1.1\r\nHost: x\r\n\r\n"))
	_ = conn.SetReadDeadline(time.Now().Add(5 * time.Second))

	got, err := io.ReadAll(conn)
	if err != nil {
		t.Fatalf("read response: %v", err)
	}
	if string(got) != want {
		t.Fatalf("relay not byte-exact:\n got %q\nwant %q", got, want)
	}
}

// A non-legacy URI must not be relayed (it falls through to the SPA/API).
func TestRelaySkipsNonLegacy(t *testing.T) {
	proxy := &legacyProxy{upstream: "127.0.0.1:1"} // would fail if dialed
	rec := httptest.NewRecorder()
	req := httptest.NewRequest(http.MethodGet, "/api/status", nil)
	if proxy.tryLegacy(rec, req) {
		t.Fatal("/api/status should not be treated as a legacy URI")
	}
}

// An empty upstream disables the relay entirely.
func TestRelayDisabledWhenNoUpstream(t *testing.T) {
	proxy := &legacyProxy{upstream: ""}
	rec := httptest.NewRecorder()
	req := httptest.NewRequest(http.MethodGet, "/AuthLogin?username=a", nil)
	if proxy.tryLegacy(rec, req) {
		t.Fatal("relay must be disabled when FREYA_LOGIN_UPSTREAM is empty")
	}
}
