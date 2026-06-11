// SPDX-License-Identifier: MIT
package main

import (
	"encoding/base64"
	"strings"
	"testing"
	"time"

	"golang.org/x/crypto/argon2"
)

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

// hashPassword must emit a PHC in exactly the libsodium INTERACTIVE shape
// (m=65536,t=2,p=1, argon2id v=19) that verifyPassword -- and the C++ game
// login's crypto_pwhash_str_verify -- accept, with a fresh random salt each
// call. A regression here silently locks players out of the game after a
// website password reset, so the format and round-trip are both pinned.
func TestHashPassword(t *testing.T) {
	const pw = "a-valid-password-123"
	phc, err := hashPassword(pw)
	if err != nil {
		t.Fatalf("hashPassword: %v", err)
	}
	if !strings.HasPrefix(phc, "$argon2id$v=19$m=65536,t=2,p=1$") {
		t.Fatalf("PHC has wrong params/prefix: %q", phc)
	}
	if ok, err := verifyPassword(phc, pw); err != nil || !ok {
		t.Fatalf("hashPassword output failed verify: ok=%v err=%v", ok, err)
	}
	if ok, _ := verifyPassword(phc, "different"); ok {
		t.Fatal("wrong password verified against hashPassword output")
	}
	// Fresh random salt -> two hashes of the same password must differ.
	phc2, err := hashPassword(pw)
	if err != nil {
		t.Fatalf("hashPassword(2): %v", err)
	}
	if phc == phc2 {
		t.Fatal("two hashes of the same password are identical -- salt is not random")
	}
}

func TestRarityForLevelQuality(t *testing.T) {
	f := func(v float64) *float64 { return &v }
	cases := []struct {
		level int
		q     *float64
		want  string
	}{
		// quality is the game-native fraction (1.0 == 100%).
		{9, f(1.90), "epic"},
		{9, f(1.60), "rare"},
		{9, f(1.40), "uncommon"},
		{9, f(1.20), "common"},
		{2, f(2.00), "uncommon"}, // level cap 1-3
		{5, f(2.00), "rare"},     // level cap 4-6
		{9, nil, "common"},       // no quality
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
