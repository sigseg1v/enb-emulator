// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Copyright (c) 2010 Net-7 Entertainment, Ltd.
// Modified by: Max Verigin, 2026 -- ported to go
//
// net7go -- standalone Go reimplementation of Net7SSL (see config.go header).
//
// auth.go -- password verification. The C++ Net7SSL stored an Argon2id PHC
// string in accounts.password_phc and verified it with libsodium's
// crypto_pwhash_str_verify(). libsodium emits the standard PHC encoding:
//
//	$argon2id$v=19$m=<KiB>,t=<iters>,p=<lanes>$<salt-b64>$<hash-b64>
//
// (base64 is raw std, no padding). We parse those parameters and recompute the
// hash with the reference Argon2id (golang.org/x/crypto/argon2), then compare in
// constant time -- no cgo, no libsodium dependency, byte-identical result.
//
// net7go only ever VERIFIES (on /AuthLogin); it never writes a password, so it
// carries no hashPassword -- the website (Freya Online) owns password changes.
package main

import (
	"crypto/subtle"
	"encoding/base64"
	"errors"
	"fmt"
	"strings"

	"golang.org/x/crypto/argon2"
)

var errBadPHC = errors.New("malformed argon2id PHC string")

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
