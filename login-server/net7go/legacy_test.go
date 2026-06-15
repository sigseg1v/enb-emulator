// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Copyright (c) 2010 Net-7 Entertainment, Ltd.
// Modified by: Max Verigin, 2026 -- ported to go
//
// net7go -- standalone Go reimplementation of Net7SSL (see config.go header).
package main

import (
	"encoding/base64"
	"regexp"
	"strings"
	"testing"

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
