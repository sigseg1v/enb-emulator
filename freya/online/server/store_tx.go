// SPDX-License-Identifier: MIT
// Freya Online -- serializable transaction helper.
//
// Every money/item movement (vault transfer, AH sell/bid/buyout, mail loot)
// runs through serialTx so two concurrent web mutations can never dup an item
// or double-spend credits. SERIALIZABLE is the strongest isolation Postgres
// offers: the database aborts one of any pair of transactions whose effects
// could not have arisen from running them one-at-a-time. We keep the explicit
// SELECT ... FOR UPDATE row locks too -- they take the conflict early (so the
// loser blocks instead of doing work it will have to throw away) and cut the
// serialization-failure retry rate to near zero.
//
// On a serialization failure (SQLSTATE 40001) or deadlock (40P01) the whole
// transaction is retried from scratch: nothing committed, so re-running the
// closure is safe. Domain errors (insufficient credits, item gone, ...) are NOT
// retried -- they are returned to the caller verbatim.

package main

import (
	"context"
	"errors"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgconn"
)

// maxSerialAttempts bounds the retry loop so a pathological live-lock cannot
// spin forever. In practice a website-scale workload retries ~never.
const maxSerialAttempts = 8

// isSerializationFailure reports whether err is a transient Postgres conflict
// that re-running the transaction can resolve (serialization failure / deadlock).
func isSerializationFailure(err error) bool {
	var pgErr *pgconn.PgError
	if errors.As(err, &pgErr) {
		return pgErr.Code == "40001" || pgErr.Code == "40P01"
	}
	return false
}

// serialTx runs fn inside a SERIALIZABLE transaction, retrying on serialization
// failure / deadlock. fn must touch the database ONLY through the supplied tx
// (no external side effects) so a retry is safe. fn must not commit or roll back
// -- serialTx owns the transaction lifecycle. A nil error from fn commits; any
// error rolls back.
func (s *Store) serialTx(ctx context.Context, fn func(pgx.Tx) error) error {
	var lastErr error
	for attempt := 0; attempt < maxSerialAttempts; attempt++ {
		tx, err := s.user.BeginTx(ctx, pgx.TxOptions{IsoLevel: pgx.Serializable})
		if err != nil {
			return err
		}

		if err := fn(tx); err != nil {
			_ = tx.Rollback(ctx)
			if isSerializationFailure(err) {
				lastErr = err
				continue
			}
			return err
		}

		if err := tx.Commit(ctx); err != nil {
			_ = tx.Rollback(ctx)
			if isSerializationFailure(err) {
				lastErr = err
				continue
			}
			return err
		}
		return nil
	}
	return lastErr
}
