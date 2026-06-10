// SPDX-License-Identifier: MIT
// Freya Online -- password verification.
//
// The C++ Net7SSL stored an Argon2id PHC string in accounts.password_phc and
// verified it with libsodium's crypto_pwhash_str_verify(). libsodium emits the
// standard PHC encoding:
//
//   $argon2id$v=19$m=<KiB>,t=<iters>,p=<lanes>$<salt-b64>$<hash-b64>
//
// (base64 is raw std, no padding). We parse those parameters and recompute the
// hash with the reference Argon2id (golang.org/x/crypto/argon2), then compare in
// constant time -- no cgo, no libsodium dependency, byte-identical result.

package main

import (
	"crypto/rand"
	"crypto/subtle"
	"encoding/base64"
	"errors"
	"fmt"
	"strings"

	"golang.org/x/crypto/argon2"
)

var errBadPHC = errors.New("malformed argon2id PHC string")

// libsodium INTERACTIVE limits -- the same parameters PyNaCl's
// nacl.pwhash.argon2id.str() (used by `just seed-account`) emits and that the
// C++ game login (login-server/Net7SSL crypto_pwhash_str_verify) accepts. A
// password the website writes MUST land in this exact encoding or the player
// can no longer log into the game with it, so these are pinned, not tunable.
const (
	phcMemKiB = 65536 // m= : 64 MiB
	phcIters  = 2     // t=
	phcLanes  = 1     // p=
	phcSalt   = 16    // salt bytes
	phcKeyLen = 32    // hash bytes
)

// hashPassword produces a libsodium-compatible Argon2id PHC string for password
// with a fresh random 16-byte salt. The encoding is byte-for-byte what
// verifyPassword (and the C++ login's crypto_pwhash_str_verify) parse:
//
//	$argon2id$v=19$m=65536,t=2,p=1$<salt-b64>$<hash-b64>   (raw std base64, no pad)
func hashPassword(password string) (string, error) {
	salt := make([]byte, phcSalt)
	if _, err := rand.Read(salt); err != nil {
		return "", fmt.Errorf("argon2id salt: %w", err)
	}
	hash := argon2.IDKey([]byte(password), salt, phcIters, phcMemKiB, phcLanes, phcKeyLen)
	return fmt.Sprintf("$argon2id$v=19$m=%d,t=%d,p=%d$%s$%s",
		phcMemKiB, phcIters, phcLanes,
		base64.RawStdEncoding.EncodeToString(salt),
		base64.RawStdEncoding.EncodeToString(hash)), nil
}

// verifyPassword reports whether password matches the stored Argon2id PHC.
// It is constant-time with respect to the comparison; a malformed/empty PHC
// returns (false, err) without leaking which account exists -- callers treat
// any non-true result identically (no user enumeration).
func verifyPassword(phc, password string) (bool, error) {
	parts := strings.Split(phc, "$")
	// ["", "argon2id", "v=19", "m=..,t=..,p=..", "<salt>", "<hash>"]
	if len(parts) != 6 || parts[0] != "" || parts[1] != "argon2id" {
		return false, errBadPHC
	}
	if parts[2] != "v=19" {
		return false, fmt.Errorf("%w: unsupported version %q", errBadPHC, parts[2])
	}

	var mem uint32
	var iters uint32
	var lanes uint8
	if _, err := fmt.Sscanf(parts[3], "m=%d,t=%d,p=%d", &mem, &iters, &lanes); err != nil {
		return false, fmt.Errorf("%w: params %q", errBadPHC, parts[3])
	}

	salt, err := base64.RawStdEncoding.DecodeString(parts[4])
	if err != nil {
		return false, fmt.Errorf("%w: salt", errBadPHC)
	}
	want, err := base64.RawStdEncoding.DecodeString(parts[5])
	if err != nil {
		return false, fmt.Errorf("%w: hash", errBadPHC)
	}

	got := argon2.IDKey([]byte(password), salt, iters, mem, lanes, uint32(len(want)))
	return subtle.ConstantTimeCompare(got, want) == 1, nil
}
