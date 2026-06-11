// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Copyright (c) 2010 Net-7 Entertainment, Ltd.
// Modified by: Max Verigin, 2026 -- ported to go
//
// net7go -- standalone Go reimplementation of Net7SSL (see config.go header).
//
// ticket.go -- login ticket issuance (AccountManager parity).
package main

import (
	"crypto/rand"
	"encoding/hex"
)

// ticketExpireMs matches the C++ kTicketExpireMs / TICKET_EXPIRE_TIME constant
// (AccountManager.h). MUST stay in sync with the game server's expiry.
const ticketExpireMs int64 = 1800000 // 30 minutes

// newTicket forms "<username>-<32 lowercase hex>" from 16 CSPRNG bytes, exactly
// as the C++ login did (randombytes_buf + sodium_bin2hex). The game server
// splits on the FIRST '-' to recover the username, so usernames may contain '-'.
func newTicket(username string) (string, error) {
	var b [16]byte
	if _, err := rand.Read(b[:]); err != nil {
		return "", err
	}
	return username + "-" + hex.EncodeToString(b[:]), nil
}
