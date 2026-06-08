package main

import (
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

// jsonString must produce a valid JSON string literal that round-trips, including
// the characters a player-controlled name or broadcast could contain.
func TestJSONString(t *testing.T) {
	cases := []string{
		`Player Bob logged in.`,
		`Server broadcast: [Bob] he said "hi" and \ left`,
		"line1\nline2\ttabbed\r",
		"control\x01\x1fchars",
		`unicode: café ` + "café 日本語",
	}
	for _, in := range cases {
		lit := jsonString(in)
		var out string
		if err := json.Unmarshal([]byte(lit), &out); err != nil {
			t.Fatalf("jsonString(%q) = %q is not valid JSON: %v", in, lit, err)
		}
		if out != in {
			t.Fatalf("round-trip mismatch: in=%q out=%q", in, out)
		}
	}
}

// deliver returns (true, 0) on a 2xx and POSTs a {"content": ...} body.
func TestDeliverSuccess(t *testing.T) {
	var got string
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Header.Get("Content-Type") != "application/json" {
			t.Errorf("missing JSON content-type")
		}
		b, _ := io.ReadAll(r.Body)
		var payload struct {
			Content string `json:"content"`
		}
		if err := json.Unmarshal(b, &payload); err != nil {
			t.Errorf("body not JSON: %v (%s)", err, b)
		}
		got = payload.Content
		w.WriteHeader(http.StatusNoContent)
	}))
	defer srv.Close()

	ok, retry := deliver(srv.URL, `Player Zoe logged in. (3 online)`)
	if !ok || retry != 0 {
		t.Fatalf("deliver = (%v,%v), want (true,0)", ok, retry)
	}
	if got != `Player Zoe logged in. (3 online)` {
		t.Fatalf("server saw content %q", got)
	}
}

// A 429 returns (false, Retry-After) so the caller backs off; the row stays unsent.
func TestDeliverRateLimited(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Retry-After", "2")
		w.WriteHeader(http.StatusTooManyRequests)
	}))
	defer srv.Close()

	ok, retry := deliver(srv.URL, "x")
	if ok {
		t.Fatalf("deliver should report failure on 429")
	}
	if retry != 2*time.Second {
		t.Fatalf("retry = %v, want 2s", retry)
	}
}

// Any other non-2xx returns (false, 0): retry later, no forced backoff.
func TestDeliverServerError(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusInternalServerError)
	}))
	defer srv.Close()

	ok, retry := deliver(srv.URL, "x")
	if ok || retry != 0 {
		t.Fatalf("deliver = (%v,%v), want (false,0)", ok, retry)
	}
}

// A runaway broadcast is truncated to the webhook char cap rather than rejected.
func TestDeliverTruncates(t *testing.T) {
	var gotLen int
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		b, _ := io.ReadAll(r.Body)
		var payload struct {
			Content string `json:"content"`
		}
		json.Unmarshal(b, &payload)
		gotLen = len(payload.Content)
		w.WriteHeader(http.StatusOK)
	}))
	defer srv.Close()

	if _, _ = deliver(srv.URL, strings.Repeat("a", maxContentLen+500)); gotLen != maxContentLen {
		t.Fatalf("content len = %d, want %d", gotLen, maxContentLen)
	}
}

// buildDSN assembles a libpq URL from the DB_* env and escapes the password.
func TestBuildDSNFromParts(t *testing.T) {
	t.Setenv("DATABASE_URL", "")
	t.Setenv("DB_HOST", "postgres:5432")
	t.Setenv("DB_USER", "net7")
	t.Setenv("DB_PASS", "p@ss/word")
	t.Setenv("DB_NAME", "net7_user")
	dsn := buildDSN()
	want := "postgres://net7:p%40ss%2Fword@postgres:5432/net7_user?sslmode=disable"
	if dsn != want {
		t.Fatalf("buildDSN = %q, want %q", dsn, want)
	}
}

// DATABASE_URL overrides the DB_* pieces entirely.
func TestBuildDSNOverride(t *testing.T) {
	t.Setenv("DATABASE_URL", "postgres://u:p@h:1/db")
	if got := buildDSN(); got != "postgres://u:p@h:1/db" {
		t.Fatalf("buildDSN override = %q", got)
	}
}
