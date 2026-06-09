// settings.go -- per-kind relay on/off switches for the status-notifier.
//
// Phase AM. A Discord admin can toggle which notification kinds the sidecar
// relays, at runtime, via the bot's `/notify` slash command. The switches live
// in status_notification_settings (net7_user). Two consumers:
//
//   - the drain loop (main.go) reads the enabled set before delivering, and
//     drops a disabled kind instead of POSTing it;
//   - the /notify command handler (bot.go) writes a single kind's flag.
//
// CRITICAL: this is a SIDECAR-side delivery filter only. The game server never
// reads this table -- it keeps emitting every kind unconditionally. See the
// header of db/postgres/status_notification_settings.sql for why that boundary
// matters (no Discord-writable control path into the game server).
package main

import (
	"context"
	"fmt"

	"github.com/jackc/pgx/v5"
)

// notificationKinds is the closed allowlist of relay-able event kinds. It mirrors
// the kinds the server emits (HandleExternalStatusEvent) and the seed in
// status_notification_settings.sql. The /notify command validates against this
// set, so an unknown kind can never reach an UPDATE.
var notificationKinds = []string{"login", "logout", "levelup", "broadcast", "server_start"}

func isKnownKind(kind string) bool {
	for _, k := range notificationKinds {
		if k == kind {
			return true
		}
	}
	return false
}

// readEnabledKinds returns the per-kind enabled flags. A kind absent from the
// table is treated as enabled (fail-open: a missing row must not silently
// swallow events). On a query error every kind is reported enabled so a settings
// glitch never suppresses the feed.
func readEnabledKinds(ctx context.Context, conn *pgx.Conn) map[string]bool {
	enabled := map[string]bool{}
	for _, k := range notificationKinds {
		enabled[k] = true // default-on; overwritten by an explicit row below
	}
	rows, err := conn.Query(ctx, "SELECT kind, enabled FROM status_notification_settings")
	if err != nil {
		logf("settings: read failed: %v -- treating all kinds as enabled", err)
		return enabled
	}
	defer rows.Close()
	for rows.Next() {
		var k string
		var on bool
		if err := rows.Scan(&k, &on); err != nil {
			continue
		}
		enabled[k] = on
	}
	return enabled
}

// setKindEnabled flips one kind's switch. kind MUST already be validated by the
// caller (isKnownKind); the value is bound as a parameter regardless. updatedBy
// is the Discord user id, recorded for audit. An UPSERT so a kind missing from a
// pre-existing volume's seed is still settable.
func setKindEnabled(ctx context.Context, conn *pgx.Conn, kind string, enabled bool, updatedBy string) error {
	if !isKnownKind(kind) {
		return fmt.Errorf("unknown notification kind %q", kind)
	}
	_, err := conn.Exec(ctx,
		`INSERT INTO status_notification_settings (kind, enabled, updated_at, updated_by)
		      VALUES ($1, $2, now(), $3)
		 ON CONFLICT (kind)
		   DO UPDATE SET enabled = EXCLUDED.enabled,
		                 updated_at = now(),
		                 updated_by = EXCLUDED.updated_by`,
		kind, enabled, updatedBy)
	return err
}
