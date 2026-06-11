// SPDX-License-Identifier: MIT
// Freya Online -- Postgres access.
//
// Two pools: `user` (net7_user: accounts, login_ticket, avatar_*, server_status,
// and the Freya AH/mailbox tables) and `content` (net7: item_base catalogue).
// They are separate databases -- a single connection cannot read across them.

package main

import (
	"context"
	"errors"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
)

type Store struct {
	user    *pgxpool.Pool
	content *pgxpool.Pool
}

func openStore(ctx context.Context, cfg Config) (*Store, error) {
	up, err := pgxpool.New(ctx, cfg.dsn(cfg.DBUserName))
	if err != nil {
		return nil, err
	}
	cp, err := pgxpool.New(ctx, cfg.dsn(cfg.DBContent))
	if err != nil {
		up.Close()
		return nil, err
	}
	return &Store{user: up, content: cp}, nil
}

func (s *Store) Close() {
	if s.user != nil {
		s.user.Close()
	}
	if s.content != nil {
		s.content.Close()
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

// accountID resolves the numeric account id for a username.
func (s *Store) accountID(ctx context.Context, username string) (int64, error) {
	var id int64
	err := s.user.QueryRow(ctx,
		`SELECT id FROM accounts WHERE username = $1`, username,
	).Scan(&id)
	if errors.Is(err, pgx.ErrNoRows) {
		return 0, errNoAccount
	}
	return id, err
}

// passwordPHCByID returns the stored Argon2id PHC for an account id. Used by
// the website password-change flow, which knows the account id from the session
// cookie (not a typed username), so it must not re-resolve via username.
func (s *Store) passwordPHCByID(ctx context.Context, accountID int64) (string, error) {
	var phc string
	err := s.user.QueryRow(ctx,
		`SELECT password_phc FROM accounts WHERE id = $1`, accountID,
	).Scan(&phc)
	if errors.Is(err, pgx.ErrNoRows) {
		return "", errNoAccount
	}
	return phc, err
}

// updatePassword writes a new Argon2id PHC for an account id. The caller has
// already verified the current password and validated the new one; this is the
// bare parameterized UPDATE. errNoAccount if the row vanished.
func (s *Store) updatePassword(ctx context.Context, accountID int64, phc string) error {
	tag, err := s.user.Exec(ctx,
		`UPDATE accounts SET password_phc = $1 WHERE id = $2`, phc, accountID)
	if err != nil {
		return err
	}
	if tag.RowsAffected() == 0 {
		return errNoAccount
	}
	return nil
}

// Character is one live (non-soft-deleted) avatar on an account.
type Character struct {
	AvatarID int64  `json:"-"`
	Name     string `json:"name"`
	Credits  int64  `json:"-"`
}

// liveCharacters lists an account's non-deleted avatars, newest slot first,
// with each avatar's credit balance. Soft-deleted avatars (deleted_at set) are
// excluded so they neither appear in character select nor count against slots.
func (s *Store) liveCharacters(ctx context.Context, accountID int64) ([]Character, error) {
	rows, err := s.user.Query(ctx, `
		SELECT ai.avatar_id,
		       trim(both '_' from coalesce(ad.first_name,'') || '_' || coalesce(ad.last_name,'')),
		       coalesce(ali.credits, 0)::bigint
		  FROM avatar_info ai
		  JOIN avatar_data ad             ON ad.avatar_id = ai.avatar_id
		  LEFT JOIN avatar_level_info ali ON ali.avatar_id = ai.avatar_id
		 WHERE ai.account_id = $1
		   AND ai.deleted_at IS NULL
		 ORDER BY ai.slot ASC`, accountID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var out []Character
	for rows.Next() {
		var c Character
		if err := rows.Scan(&c.AvatarID, &c.Name, &c.Credits); err != nil {
			return nil, err
		}
		out = append(out, c)
	}
	return out, rows.Err()
}

// ServerStatus is the always-visible status surfaced to the website.
type ServerStatus struct {
	Status  string `json:"status"` // "ONLINE" | "OFFLINE"
	Players int    `json:"players"`
}

// readServerStatus reads the singleton server_status row. The server is ONLINE
// only if the row's updated_at is within staleSecs (the server heartbeats it).
func (s *Store) readServerStatus(ctx context.Context, staleSecs int) ServerStatus {
	var players int
	var updated time.Time
	err := s.user.QueryRow(ctx,
		`SELECT players_online, updated_at FROM server_status WHERE id = 1`,
	).Scan(&players, &updated)
	if err != nil {
		return ServerStatus{Status: "OFFLINE", Players: 0}
	}
	if time.Since(updated) > time.Duration(staleSecs)*time.Second {
		return ServerStatus{Status: "OFFLINE", Players: 0}
	}
	return ServerStatus{Status: "ONLINE", Players: players}
}
