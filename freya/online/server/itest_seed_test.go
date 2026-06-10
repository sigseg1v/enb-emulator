// SPDX-License-Identifier: MIT
// Freya Online -- integration-test seed helpers (avatar_data + password hash).

package main

import (
	"context"
	"encoding/base64"
	"fmt"
	"strings"
	"testing"

	"github.com/jackc/pgx/v5/pgxpool"
	"golang.org/x/crypto/argon2"
)

// hashPasswordForTest builds a libsodium-style Argon2id PHC that verifyPassword
// accepts, with the same m/t/p the production hashes use. A fixed salt is fine
// for a test fixture (no secrecy requirement here).
func hashPasswordForTest(pw string) (string, error) {
	salt := []byte("0123456789abcdef")
	const mem, iters, lanes = 65536, 2, 1
	hash := argon2.IDKey([]byte(pw), salt, iters, mem, lanes, 32)
	return "$argon2id$v=19$m=65536,t=2,p=1$" +
		base64.RawStdEncoding.EncodeToString(salt) + "$" +
		base64.RawStdEncoding.EncodeToString(hash), nil
}

// seedAvatarData inserts an avatar_data row. The table has ~59 NOT NULL columns
// of legacy cosmetic state; rather than hard-code them (and break when the
// schema churns), it reads the live column list from information_schema and
// inserts every insertable column zeroed, overriding only avatar_id/first_name/
// last_name. Identity and generated columns are excluded. Identifiers come from
// the catalog (not user input) and are double-quoted so mixed-case names like
// "hair_H" / "tattoo_X" are not folded to lowercase; all VALUES are bound.
func seedAvatarData(t *testing.T, pool *pgxpool.Pool, ctx context.Context, avID int64, first, last string) {
	t.Helper()
	rows, err := pool.Query(ctx, `
		SELECT column_name, data_type
		  FROM information_schema.columns
		 WHERE table_name = 'avatar_data'
		   AND is_identity = 'NO'
		   AND is_generated = 'NEVER'
		 ORDER BY ordinal_position`)
	if err != nil {
		t.Fatalf("read avatar_data columns: %v", err)
	}
	type col struct{ name, dtype string }
	var cols []col
	for rows.Next() {
		var c col
		if err := rows.Scan(&c.name, &c.dtype); err != nil {
			rows.Close()
			t.Fatalf("scan column: %v", err)
		}
		cols = append(cols, c)
	}
	rows.Close()
	if err := rows.Err(); err != nil {
		t.Fatalf("iterate columns: %v", err)
	}
	if len(cols) == 0 {
		t.Fatal("avatar_data has no columns -- schema not initialized?")
	}

	names := make([]string, 0, len(cols))
	phs := make([]string, 0, len(cols))
	vals := make([]any, 0, len(cols))
	for i, c := range cols {
		var v any
		switch c.name {
		case "avatar_id":
			v = avID
		case "first_name":
			v = first
		case "last_name":
			v = last
		default:
			v = zeroForType(c.dtype)
		}
		names = append(names, `"`+c.name+`"`)
		phs = append(phs, fmt.Sprintf("$%d", i+1))
		vals = append(vals, v)
	}
	sql := `INSERT INTO "avatar_data" (` + strings.Join(names, ", ") +
		`) VALUES (` + strings.Join(phs, ", ") + `)`
	if _, err := pool.Exec(ctx, sql, vals...); err != nil {
		t.Fatalf("insert avatar_data: %v", err)
	}
}

// zeroForType is the additive identity for a Postgres data_type, used to fill
// the cosmetic NOT NULL avatar_data columns we do not care about.
func zeroForType(dt string) any {
	switch dt {
	case "smallint", "integer", "bigint":
		return 0
	case "numeric", "double precision", "real":
		return 0.0
	case "boolean":
		return false
	case "character varying", "character", "text", "name", "char", `"char"`:
		return ""
	default:
		return nil // nullable timestamp/other -> NULL
	}
}
