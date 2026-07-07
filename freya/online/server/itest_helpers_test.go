// SPDX-License-Identifier: MIT
// Freya Online -- integration-test harness.
//
// These tests exercise the real store/API SQL against a live Postgres with the
// actual two-DB schema (net7_user save-state + net7 content). They are the only
// place the cross-DB resolve, the FOR-UPDATE offline-guard, the deposit/bid/
// buyout credit math, and the mail loot path are tested against real SQL --
// the pure-function unit tests in legacy_test.go cannot reach them.
//
// They are GATED on FREYA_TEST_DB so `go test ./...` without a database still
// runs the unit tests and skips these cleanly. To run them:
//
//	FREYA_TEST_DB=1 go test ./...                      # postgres on localhost:5434
//	FREYA_TEST_DB=1 FREYA_TEST_DB_HOST=host:port go test ./...
//
// (just test-online-it brings the docker postgres up and runs them.)
//
// Every test seeds its own account(s)/avatar(s)/items in a reserved id band
// (testIDBase) that satisfies the GameID ceiling CHECK constraints, and wipes
// that band before and after so reruns are deterministic and nothing else in
// the DB is touched.

package main

import (
	"context"
	"fmt"
	"os"
	"testing"

	"github.com/jackc/pgx/v5/pgxpool"
)

// testIDBase is the start of the reserved account-id band these tests own.
// account_id <= 214748363 and avatar_id = account_id*5+slot+1 < 1073741823 must
// both hold (the GameID ceiling, db/postgres/account_id_guard.sql); 200000000*5+1
// = 1000000001 < 1073741823, so this band is in range with headroom.
const testIDBase int64 = 200000000

// avatarFor is the formula the login-server AVATAR_ID macro uses: the wire
// GameID round-trips only when avatar_id = account_id*5 + slot + 1.
func avatarFor(accountID int64, slot int) int64 { return accountID*5 + int64(slot) + 1 }

func testStore(t *testing.T) (*Store, context.Context) {
	t.Helper()
	if os.Getenv("FREYA_TEST_DB") == "" {
		t.Skip("FREYA_TEST_DB unset; needs Postgres (default localhost:5434). Run via `just test-online-it`.")
	}
	cfg := Config{
		DBHost:          env("FREYA_TEST_DB_HOST", "localhost:5434"),
		DBUser:          env("DB_USER", "net7"),
		DBPass:          env("DB_PASS", "net7"),
		DBUserName:      env("DB_NAME", "net7_user"),
		DBContent:       env("FREYA_DB_CONTENT", "net7"),
		StatusStaleSecs: 90,
	}
	ctx := context.Background()
	st, err := openStore(ctx, cfg)
	if err != nil {
		t.Fatalf("openStore: %v", err)
	}
	// Fail fast with a clear message if the DB is unreachable rather than
	// surfacing a confusing error deep in the first query.
	if err := st.user.Ping(ctx); err != nil {
		st.Close()
		t.Fatalf("ping net7_user (%s): %v", cfg.DBHost, err)
	}
	t.Cleanup(st.Close)
	return st, ctx
}

// testAccount is a seeded account with one or more live avatars.
type testAccount struct {
	id       int64
	username string
	password string
	avatars  []testAvatar
}

type testAvatar struct {
	id   int64
	name string
}

// seedAccount creates account testIDBase+suffix with `chars` live, offline
// avatars (slot 0..n-1), each holding `credits`. The password is a freshly
// generated Argon2id PHC verifiable by verifyPassword. It wipes the account's
// id band first and registers a t.Cleanup to wipe it again.
func seedAccount(t *testing.T, st *Store, ctx context.Context, suffix int, chars int, credits int64) testAccount {
	t.Helper()
	acctID := testIDBase + int64(suffix)
	username := fmt.Sprintf("itest_%d", suffix)
	password := fmt.Sprintf("pw-%d-secret", suffix)

	wipeAccount(t, st, ctx, acctID, username)
	t.Cleanup(func() { wipeAccount(t, st, context.Background(), acctID, username) })

	phc, err := hashPasswordForTest(password)
	if err != nil {
		t.Fatalf("hash password: %v", err)
	}
	mustExec(t, st.user, ctx,
		`INSERT INTO accounts (id, username, password_phc) VALUES ($1,$2,$3)`,
		acctID, username, phc)

	acct := testAccount{id: acctID, username: username, password: password}
	for slot := 0; slot < chars; slot++ {
		avID := avatarFor(acctID, slot)
		name := fmt.Sprintf("Itester%dx%d", suffix, slot) // contains a vowel; AH name = first_last
		first := name
		last := fmt.Sprintf("Slot%d", slot)
		seedAvatarOffline(t, st, ctx, acctID, avID, slot, first, last, credits)
		acct.avatars = append(acct.avatars, testAvatar{id: avID, name: first + "_" + last})
	}
	return acct
}

// seedAvatarOffline inserts the avatar_info/avatar_data/avatar_level_info rows
// for one live, OFFLINE avatar (last_login <= last_logout). avatar_data has ~59
// NOT NULL columns, so it is filled dynamically from information_schema (every
// column zeroed except the names) -- robust to the legacy schema's column churn
// and correctly double-quoting its mixed-case identifiers (hair_H, tattoo_X, ...)
// which would otherwise fold to lowercase.
func seedAvatarOffline(t *testing.T, st *Store, ctx context.Context, acctID, avID int64, slot int, first, last string, credits int64) {
	t.Helper()
	// avatar_info: offline => last_login <= last_logout (the server's own
	// definition, assertAvatarOffline). The columns are timestamps, so "logged
	// out one hour after logging in two hours ago" reads as offline.
	mustExec(t, st.user, ctx, `
		INSERT INTO avatar_info
		  (avatar_id, account_id, slot, sector, galaxy, count, admin,
		   combat, explore, trade, last_login, last_logout, deleted_at)
		VALUES ($1,$2,$3,0,0,0,0, 1,1,1,
		        now() - interval '2 hours', now() - interval '1 hour', NULL)`,
		avID, acctID, slot)

	seedAvatarData(t, st.user, ctx, avID, first, last)

	mustExec(t, st.user, ctx, `
		INSERT INTO avatar_level_info
		  (avatar_id, player_rank_name, hull_upgrade_level, hull_points,
		   max_hull_points, credits, cargo_space, combat_bar_level,
		   explore_bar_level, trade_bar_level, engine_thrust_type, warp_power_level)
		VALUES ($1,0,0,100,100,$2,100,0,0,0,0,0)`,
		avID, credits)
}

// setAvatarOnline flips an avatar to the server's "online" state
// (last_login > last_logout) so the offline-guard rejects web mutations.
func setAvatarOnline(t *testing.T, st *Store, ctx context.Context, avID int64) {
	t.Helper()
	// online => last_login > last_logout (logged in more recently than out).
	mustExec(t, st.user, ctx,
		`UPDATE avatar_info SET last_login = now(), last_logout = now() - interval '1 hour' WHERE avatar_id = $1`, avID)
}

// setAccountOnline flips an account to "online" (last_login > last_logout). The
// galaxy occupancy read (like the /status bot) requires BOTH the account and the
// avatar to read online before the avatar is counted.
func setAccountOnline(t *testing.T, st *Store, ctx context.Context, acctID int64) {
	t.Helper()
	mustExec(t, st.user, ctx,
		`UPDATE accounts SET last_login = now(), last_logout = now() - interval '1 hour' WHERE id = $1`, acctID)
}

// seedVaultItem puts one item instance into an avatar's vault slot.
func seedVaultItem(t *testing.T, st *Store, ctx context.Context, avID int64, slot, itemID, stack int, quality *float64) {
	t.Helper()
	mustExec(t, st.user, ctx, `
		INSERT INTO avatar_vault_items
		  (avatar_id, inventory_slot, item_id, trade_stack, quality)
		VALUES ($1,$2,$3,$4,$5)`,
		avID, slot, itemID, stack, quality)
}

// vaultCount returns how many vault rows an avatar has for an item.
func vaultCount(t *testing.T, st *Store, ctx context.Context, avID int64, itemID int) int {
	t.Helper()
	var n int
	if err := st.user.QueryRow(ctx,
		`SELECT count(*) FROM avatar_vault_items WHERE avatar_id=$1 AND item_id=$2`,
		avID, itemID).Scan(&n); err != nil {
		t.Fatalf("vaultCount: %v", err)
	}
	return n
}

// creditsOf reads an avatar's current credit balance.
func creditsOf(t *testing.T, st *Store, ctx context.Context, avID int64) int64 {
	t.Helper()
	var c int64
	if err := st.user.QueryRow(ctx,
		`SELECT credits::bigint FROM avatar_level_info WHERE avatar_id=$1`, avID).Scan(&c); err != nil {
		t.Fatalf("creditsOf: %v", err)
	}
	return c
}

// pickTradableItem finds a real tradable item in the content DB. wantStack=true
// prefers an ore (stackable, no variable quality); wantStack=false prefers a
// quality-bearing equipment item. Skips the test if the catalogue is empty.
func pickTradableItem(t *testing.T, st *Store, ctx context.Context, wantOre bool) (id, buyingPrice int) {
	t.Helper()
	q := `SELECT id, buying_price FROM item_base
	       WHERE no_trade = 0 AND buying_price > 0 AND category = ANY($1)
	       ORDER BY id LIMIT 1`
	cats := []int{10, 14, 15, 2, 7} // weapons/shields/reactors (equipment)
	if wantOre {
		cats = []int{80, 81} // refined / raw resource
	}
	if err := st.content.QueryRow(ctx, q, cats).Scan(&id, &buyingPrice); err != nil {
		t.Skipf("no tradable item in content DB for cats %v: %v", cats, err)
	}
	return id, buyingPrice
}

// wipeAccount removes every row this harness could have created for an account,
// child tables first (bids before listings, attachments cascade with messages).
func wipeAccount(t *testing.T, st *Store, ctx context.Context, acctID int64, username string) {
	t.Helper()
	stmts := []struct {
		sql  string
		args []any
	}{
		{`DELETE FROM auction_bids WHERE bidder_account_id=$1
		   OR listing_id IN (SELECT id FROM auction_listings
		                      WHERE seller_account_id=$1 OR high_bidder_account_id=$1)`, []any{acctID}},
		{`DELETE FROM auction_listings WHERE seller_account_id=$1 OR high_bidder_account_id=$1`, []any{acctID}},
		{`DELETE FROM mailbox_messages WHERE account_id=$1`, []any{acctID}}, // attachments ON DELETE CASCADE
		{`DELETE FROM avatar_vault_items WHERE avatar_id IN (SELECT avatar_id FROM avatar_info WHERE account_id=$1)`, []any{acctID}},
		{`DELETE FROM avatar_inventory_items WHERE avatar_id IN (SELECT avatar_id FROM avatar_info WHERE account_id=$1)`, []any{acctID}},
		{`DELETE FROM avatar_equipment WHERE avatar_id IN (SELECT avatar_id FROM avatar_info WHERE account_id=$1)`, []any{acctID}},
		{`DELETE FROM avatar_skill_levels WHERE avatar_id IN (SELECT avatar_id FROM avatar_info WHERE account_id=$1)`, []any{acctID}},
		{`DELETE FROM ship_data WHERE avatar_id IN (SELECT avatar_id FROM avatar_info WHERE account_id=$1)`, []any{acctID}},
		{`DELETE FROM avatar_level_info WHERE avatar_id IN (SELECT avatar_id FROM avatar_info WHERE account_id=$1)`, []any{acctID}},
		{`DELETE FROM avatar_data WHERE avatar_id IN (SELECT avatar_id FROM avatar_info WHERE account_id=$1)`, []any{acctID}},
		{`DELETE FROM avatar_info WHERE account_id=$1`, []any{acctID}},
		{`DELETE FROM accounts WHERE id=$1`, []any{acctID}},
		{`DELETE FROM login_ticket WHERE username=$1`, []any{username}},
	}
	for _, s := range stmts {
		if _, err := st.user.Exec(ctx, s.sql, s.args...); err != nil {
			t.Fatalf("wipeAccount %q: %v", s.sql, err)
		}
	}
}

// deliverCreditsForTest posts a credits attachment to an account's mailbox via
// the same deliverCredits helper the AH settlement uses, in its own committed
// transaction. recipientAvatar addresses it so loot has a default target.
func deliverCreditsForTest(t *testing.T, st *Store, ctx context.Context, accountID, recipientAvatar int64, amount int64) {
	t.Helper()
	tx, err := st.user.Begin(ctx)
	if err != nil {
		t.Fatalf("begin: %v", err)
	}
	defer tx.Rollback(ctx)
	// recipientAvatar param isn't taken by deliverCredits (credits are
	// account-level), so the loot target resolves to the account's primary
	// character -- which is recipientAvatar here (the only/slot-0 avatar).
	_ = recipientAvatar
	if err := deliverCredits(ctx, tx, accountID, "Auction House", "Test refund", "test", amount); err != nil {
		t.Fatalf("deliverCredits: %v", err)
	}
	if err := tx.Commit(ctx); err != nil {
		t.Fatalf("commit: %v", err)
	}
}

// deliverItemForTest posts a single-item attachment to an account's mailbox via
// the same deliverItem helper the AH settlement uses. recipientAvatar 0 =
// account-level (as a settled auction win is delivered, store_ah_write.go), in
// its own committed transaction.
func deliverItemForTest(t *testing.T, st *Store, ctx context.Context, accountID, recipientAvatar int64, itemID int) {
	t.Helper()
	tx, err := st.user.Begin(ctx)
	if err != nil {
		t.Fatalf("begin: %v", err)
	}
	defer tx.Rollback(ctx)
	if err := deliverItem(ctx, tx, accountID, recipientAvatar, "Auction House", "Test win", "test",
		itemID, 1, nil, nil, nil, nil); err != nil {
		t.Fatalf("deliverItem: %v", err)
	}
	if err := tx.Commit(ctx); err != nil {
		t.Fatalf("commit: %v", err)
	}
}

func mustExec(t *testing.T, pool *pgxpool.Pool, ctx context.Context, sql string, args ...any) {
	t.Helper()
	if _, err := pool.Exec(ctx, sql, args...); err != nil {
		t.Fatalf("exec %q: %v", sql, err)
	}
}
