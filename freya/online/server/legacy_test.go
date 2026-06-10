// SPDX-License-Identifier: MIT
package main

import (
	"encoding/base64"
	"regexp"
	"strings"
	"testing"
	"time"

	"golang.org/x/crypto/argon2"
)

// The legacy AuthLogin success/failure bodies are wire-visible to the real
// client; pin their exact shape.
func TestHttpResultBytes(t *testing.T) {
	got := string(httpResult("Valid=False\r\n", "text/plain"))
	want := "HTTP/1.1 200 OK\r\n" +
		"Content-Type: text/plain\r\n" +
		"Server: AuthServer/2.5\r\n" +
		"Content-Length: 13\r\n" +
		"\r\n" +
		"Valid=False\r\n"
	if got != want {
		t.Fatalf("httpResult mismatch:\n got %q\nwant %q", got, want)
	}
}

func TestNotFoundBytes(t *testing.T) {
	// Content-Length: 2 with a "\r\n" body, verbatim from MakeNotFound.
	if !strings.HasSuffix(string(legacyNotFound), "\r\n\r\n\r\n") {
		t.Fatalf("404 body tail wrong: %q", legacyNotFound)
	}
	if !strings.Contains(string(legacyNotFound), "Content-Length: 2\r\n") {
		t.Fatalf("404 missing Content-Length: 2")
	}
}

func TestExtractField(t *testing.T) {
	uri := "/AuthLogin?username=alice&password=hunter2&version=1.0"
	if v, ok := extractField(uri, tagUsername); !ok || v != "alice" {
		t.Fatalf("username: got %q ok=%v", v, ok)
	}
	if v, ok := extractField(uri, tagPassword); !ok || v != "hunter2" {
		t.Fatalf("password: got %q ok=%v", v, ok)
	}
	if _, ok := extractField(uri, tagLkey); ok {
		t.Fatalf("lkey should be absent")
	}
}

func TestNewTicketFormat(t *testing.T) {
	tk, err := newTicket("alice")
	if err != nil {
		t.Fatal(err)
	}
	if !regexp.MustCompile(`^alice-[0-9a-f]{32}$`).MatchString(tk) {
		t.Fatalf("ticket format wrong: %q", tk)
	}
	// Usernames may contain '-': the game server splits on the FIRST '-'.
	tk2, _ := newTicket("a-b")
	dash := strings.IndexByte(tk2, '-')
	if tk2[:dash] != "a" {
		t.Fatalf("first-dash split wrong: %q", tk2)
	}
}

func TestComputeBand(t *testing.T) {
	cases := []struct {
		d    time.Duration
		want string
	}{
		{20 * time.Hour, "high"},
		{6 * time.Hour, "med"},
		{30 * time.Minute, "low"},
		{0, "low"},
	}
	for _, c := range cases {
		if got := computeBand(c.d); got != c.want {
			t.Errorf("computeBand(%v) = %q, want %q", c.d, got, c.want)
		}
	}
}

// verifyPassword must accept a correct libsodium-style Argon2id PHC and reject a
// wrong password. We build the PHC the same way libsodium emits it.
func TestVerifyPassword(t *testing.T) {
	salt := []byte("0123456789abcdef")
	const mem, iters, lanes = 65536, 2, 1
	hash := argon2.IDKey([]byte("correct horse"), salt, iters, mem, lanes, 32)
	phc := "$argon2id$v=19$m=65536,t=2,p=1$" +
		base64.RawStdEncoding.EncodeToString(salt) + "$" +
		base64.RawStdEncoding.EncodeToString(hash)

	if ok, err := verifyPassword(phc, "correct horse"); err != nil || !ok {
		t.Fatalf("correct password rejected: ok=%v err=%v", ok, err)
	}
	if ok, _ := verifyPassword(phc, "wrong"); ok {
		t.Fatal("wrong password accepted")
	}
	if _, err := verifyPassword("not-a-phc", "x"); err == nil {
		t.Fatal("malformed PHC should error")
	}
}

func TestRarityForLevelQuality(t *testing.T) {
	f := func(v float64) *float64 { return &v }
	cases := []struct {
		level int
		q     *float64
		want  string
	}{
		{9, f(190), "epic"},
		{9, f(160), "rare"},
		{9, f(140), "uncommon"},
		{9, f(120), "common"},
		{2, f(200), "uncommon"}, // level cap 1-3
		{5, f(200), "rare"},     // level cap 4-6
		{9, nil, "common"},      // no quality
	}
	for _, c := range cases {
		if got := rarityForLevelQuality(c.level, c.q); got != c.want {
			t.Errorf("rarityForLevelQuality(%d,%v) = %q, want %q", c.level, c.q, got, c.want)
		}
	}
}

func TestMinIncrement(t *testing.T) {
	if got := minIncrement(10000); got != 100 {
		t.Errorf("minIncrement(10000) = %d, want 100", got)
	}
	if got := minIncrement(0); got != 1 {
		t.Errorf("minIncrement(0) = %d, want 1 (floor)", got)
	}
	if got := minIncrement(50); got != 1 {
		t.Errorf("minIncrement(50) = %d, want 1 (ceil of 0.5 -> 1)", got)
	}
}
