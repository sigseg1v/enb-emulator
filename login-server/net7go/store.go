// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Copyright (c) 2010 Net-7 Entertainment, Ltd.
// Modified by: Max Verigin, 2026 -- ported to go
//
// net7go -- standalone Go reimplementation of Net7SSL (see config.go header).
//
// store.go -- Postgres access. net7go only touches the net7_user database
// (accounts + login_ticket), so it holds a single pool. The queries mirror the
// C++ Net7SSL AccountManager: ValidateAccount reads accounts.password_phc;
// StoreLoginTicket UPSERTs login_ticket.
package main

import (
	"context"
	"errors"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
)

type Store struct {
	user *pgxpool.Pool
}

func openStore(ctx context.Context, cfg Config) (*Store, error) {
	up, err := pgxpool.New(ctx, cfg.dsn())
	if err != nil {
		return nil, err
	}
	return &Store{user: up}, nil
}

func (s *Store) Close() {
	if s.user != nil {
		s.user.Close()
	}
}

var errNoAccount = errors.New("account not found")

// passwordPHC returns the stored Argon2id PHC for username, or errNoAccount.
func (s *Store) passwordPHC(ctx context.Context, username string) (string, error) {
	var phc string
	err := s.user.QueryRow(ctx,
		`SELECT password_phc FROM accounts WHERE username = $1`, username,
	).Scan(&phc)
	if errors.Is(err, pgx.ErrNoRows) {
		return "", errNoAccount
	}
	return phc, err
}

// upsertTicket stores token for username with a ms-epoch expiry, matching the
// C++ login_ticket UPSERT exactly (BIGINT expires_at).
func (s *Store) upsertTicket(ctx context.Context, username, token string, expiresAtMs int64) error {
	_, err := s.user.Exec(ctx,
		`INSERT INTO login_ticket (username, token, expires_at)
		 VALUES ($1, $2, $3)
		 ON CONFLICT (username) DO UPDATE
		   SET token = EXCLUDED.token, expires_at = EXCLUDED.expires_at`,
		username, token, expiresAtMs)
	return err
}
