// status-notifier -- out-of-process relay for the game server's external-status
// events (Phase AM).
//
// The C++ game server never talks to Discord (or any external service) directly:
// pulling an outbound HTTP client into the security-sensitive game process would
// widen its attack surface for a convenience feature, which CLAUDE.md forbids.
// Instead the server INSERTs a fully-rendered message line into the
// external_status_events table (in net7_user) and fires `NOTIFY
// external_status_event`. This sidecar LISTENs for that, drains the unsent rows,
// and delivers each one to a Discord channel through the bot (the table and the
// rendered content stay consumer-agnostic; only the transport is Discord-specific).
//
// Durability: a row stays with sent_at = NULL until this process confirms a 2xx,
// so events survive a sidecar restart and nothing is silently dropped. On startup
// the sidecar drains anything that piled up while it was down BEFORE settling into
// LISTEN.
//
// Config (all env; secret never committed):
//
//	DISCORD_BOT_TOKEN    the bot token (SECRET, required). Empty => the sidecar
//	                     idles (it does not crash-loop), so a default-off compose
//	                     stack is harmless.
//	STATUS_CHANNEL_ID    the Discord channel the relay posts each event line into
//	                     (POST /channels/{id}/messages). Empty => the bot still
//	                     serves /status + /notify but the relay is idle.
//	DISCORD_GUILD_ID     optional -- register the slash commands to one guild for
//	                     instant availability; empty registers globally.
//	DB_HOST              postgres host:port (default "postgres:5432"); same env
//	DB_USER              the other services already use. DB_NAME defaults to
//	DB_PASS              net7_user (where the outbox lives).
//	DB_NAME
//	DATABASE_URL         optional -- a full libpq DSN/URL that overrides the DB_*
//	                     pieces if set.
//	STATUS_RETENTION_DAYS  delete delivered rows older than this many days
//	                       (default 7; 0 disables the sweep).
//
// Delivery: a row stays with sent_at = NULL until the bot confirms the post, then
// is marked sent. An admin can disable individual kinds at runtime via the bot's
// /notify command; the drain loop reads status_notification_settings and drops a
// disabled kind (marking it sent = suppressed) WITHOUT the game server ever seeing
// that table.
package main

import (
	"context"
	"fmt"
	"net/url"
	"os"
	"os/signal"
	"strconv"
	"strings"
	"syscall"
	"time"

	"github.com/bwmarrin/discordgo"
	"github.com/jackc/pgx/v5"
)

const (
	notifyChannel = "external_status_event"
	// Discord caps a channel message at 2000 chars; truncate a runaway broadcast
	// rather than have the post rejected.
	maxContentLen = 2000
)

// logf writes a single line to stdout. We deliberately never log the bot token or
// any secret -- only event ids, kinds, and outcomes.
func logf(format string, a ...any) {
	fmt.Printf("status-notifier: "+format+"\n", a...)
}

func getenv(key, def string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return def
}

// buildDSN prefers an explicit DATABASE_URL, otherwise assembles one from the
// DB_* env the rest of the stack uses. Values are URL-escaped so a password with
// reserved characters does not corrupt the DSN.
func buildDSN() string {
	if dsn := os.Getenv("DATABASE_URL"); dsn != "" {
		return dsn
	}
	hostport := getenv("DB_HOST", "postgres:5432")
	host, port := hostport, "5432"
	if i := strings.LastIndex(hostport, ":"); i >= 0 {
		host, port = hostport[:i], hostport[i+1:]
	}
	user := getenv("DB_USER", "net7")
	pass := getenv("DB_PASS", "net7")
	name := getenv("DB_NAME", "net7_user")
	return fmt.Sprintf("postgres://%s:%s@%s:%s/%s?sslmode=disable",
		url.QueryEscape(user), url.QueryEscape(pass), host, port, name)
}

type event struct {
	id      int64
	kind    string
	content string
}

// deliverer abstracts the Discord transport so drain() stays decoupled from
// discordgo (and unit-testable without a live gateway).
//
//	send  posts a new message and returns its id (needed so a wreck line can be
//	      edited later by a jumpstart). ok=false leaves the row unsent for retry.
//	edit  replaces an existing message's content (used to strike through a wreck
//	      line when the player is jumpstarted). ok=false leaves the row unsent.
type deliverer interface {
	send(content string) (messageID string, ok bool)
	edit(messageID, content string) (ok bool)
}

func main() {
	botToken := os.Getenv("DISCORD_BOT_TOKEN")
	channelID := os.Getenv("STATUS_CHANNEL_ID")
	dsn := buildDSN()
	ctx := context.Background()

	// The bot drives everything: the /status + /notify slash commands AND the relay
	// (it posts each event line into STATUS_CHANNEL_ID). With no token there is
	// nothing to do, so idle rather than crash-loop -- a default-off compose stack
	// stays clean under `docker compose ps`.
	if botToken == "" {
		logf("DISCORD_BOT_TOKEN is empty -- nothing to do; idling. Set it (a secret) to enable the bot.")
		idle()
		return
	}

	guildID := os.Getenv("DISCORD_GUILD_ID")
	botSession := startBot(ctx, dsn, botToken, guildID)
	if botSession == nil {
		logf("bot failed to start -- idling.")
		idle()
		return
	}

	if channelID == "" {
		logf("STATUS_CHANNEL_ID is empty -- bot serves /status + /notify but the relay is idle (no target channel).")
		idle()
		return
	}
	deliver := botDeliverer{s: botSession, channelID: channelID}
	logf("relay: delivering via bot channel %s", channelID)

	// Wreck->jumpstart correlation lives only in this process's memory (see
	// deaths.go). Create it once so it survives DB reconnects; only a full restart
	// forgets in-flight wrecks.
	tracker := newDeathTracker()

	retentionDays := 7
	if v := os.Getenv("STATUS_RETENTION_DAYS"); v != "" {
		if n, err := strconv.Atoi(v); err == nil && n >= 0 {
			retentionDays = n
		}
	}

	// Reconnect loop: a dropped DB connection (postgres restart, network blip)
	// must not kill the sidecar -- back off and re-establish.
	for {
		if err := run(ctx, dsn, deliver, tracker, retentionDays); err != nil {
			logf("connection error: %v -- reconnecting in 5s", err)
			time.Sleep(5 * time.Second)
			continue
		}
		return
	}
}

// idle blocks until SIGINT/SIGTERM. A bare `select {}` would panic with a
// deadlock when no other goroutine can wake us; waiting on a signal gives the
// runtime a real wakeup source AND lets the container stop cleanly.
func idle() {
	sig := make(chan os.Signal, 1)
	signal.Notify(sig, syscall.SIGINT, syscall.SIGTERM)
	<-sig
}

// botDeliverer posts and edits messages in a channel through the bot session
// (REST: /channels/{id}/messages, Authorization: Bot <token>). discordgo's
// internal rate-limiter sleeps on a 429 before returning, so a transient 429 just
// surfaces as a slower-but-successful call; a hard error leaves the row unsent.
type botDeliverer struct {
	s         *discordgo.Session
	channelID string
}

func (b botDeliverer) send(content string) (string, bool) {
	if len(content) > maxContentLen {
		content = content[:maxContentLen]
	}
	m, err := b.s.ChannelMessageSend(b.channelID, content)
	if err != nil {
		logf("bot: ChannelMessageSend failed: %v -- will retry", err)
		return "", false
	}
	return m.ID, true
}

func (b botDeliverer) edit(messageID, content string) bool {
	if len(content) > maxContentLen {
		content = content[:maxContentLen]
	}
	if _, err := b.s.ChannelMessageEdit(b.channelID, messageID, content); err != nil {
		logf("bot: ChannelMessageEdit failed: %v -- will retry", err)
		return false
	}
	return true
}

// swapDBName returns dsn with its database-name path segment replaced by name,
// preserving host/credentials/query. Used to reach the net7 CONTENT DB from the
// net7_user DSN for read-only sector-name lookups. Falls back to returning dsn
// unchanged if it cannot be parsed (the caller then just fails its lookup and
// shows bare ids).
func swapDBName(dsn, name string) string {
	u, err := url.Parse(dsn)
	if err != nil {
		return dsn
	}
	u.Path = "/" + name
	return u.String()
}

// run holds one DB connection: LISTEN, an initial catch-up drain, then a loop of
// (wait for NOTIFY | periodic retention/heartbeat) -> drain. Returns on any DB
// error so main() can reconnect.
func run(ctx context.Context, dsn string, deliver deliverer, tracker *deathTracker, retentionDays int) error {
	conn, err := pgx.Connect(ctx, dsn)
	if err != nil {
		return err
	}
	defer conn.Close(ctx)

	if _, err := conn.Exec(ctx, "LISTEN "+notifyChannel); err != nil {
		return fmt.Errorf("LISTEN: %w", err)
	}
	logf("connected; LISTEN %s; relay delivery enabled", notifyChannel)

	// Catch up on anything inserted while we were down, before waiting for the
	// next NOTIFY.
	if err := drain(ctx, conn, deliver, tracker); err != nil {
		return err
	}
	lastSweep := time.Time{}

	for {
		// Wake at least every 60s even with no NOTIFY: lets us run the retention
		// sweep and re-drain anything a prior failed delivery left unsent.
		waitCtx, cancel := context.WithTimeout(ctx, 60*time.Second)
		_, err := conn.WaitForNotification(waitCtx)
		cancel()
		if err != nil && !isTimeout(err) {
			return fmt.Errorf("WaitForNotification: %w", err)
		}

		if err := drain(ctx, conn, deliver, tracker); err != nil {
			return err
		}

		if retentionDays > 0 && time.Since(lastSweep) >= time.Hour {
			sweep(ctx, conn, retentionDays)
			lastSweep = nowFrom(ctx, conn)
		}
	}
}

func isTimeout(err error) bool {
	return strings.Contains(err.Error(), "context deadline exceeded")
}

// nowFrom returns the server clock; we avoid the Go process clock for the sweep
// cadence only to keep timestamps coherent with the DB. A failure just returns
// zero (forces a sweep next loop), which is harmless.
func nowFrom(ctx context.Context, conn *pgx.Conn) time.Time {
	var t time.Time
	if err := conn.QueryRow(ctx, "SELECT now()").Scan(&t); err != nil {
		return time.Time{}
	}
	return t
}

// drain delivers every undelivered row, oldest first, marking each sent only
// after a 2xx. It stops (without error) at the first row it could not deliver so
// ordering is preserved and the next wake retries from there. A DB error is
// returned so the caller reconnects.
func drain(ctx context.Context, conn *pgx.Conn, deliver deliverer, tracker *deathTracker) error {
	rows, err := conn.Query(ctx,
		"SELECT id, kind, content FROM external_status_events WHERE sent_at IS NULL ORDER BY id")
	if err != nil {
		return fmt.Errorf("select unsent: %w", err)
	}
	var batch []event
	for rows.Next() {
		var e event
		if err := rows.Scan(&e.id, &e.kind, &e.content); err != nil {
			rows.Close()
			return fmt.Errorf("scan: %w", err)
		}
		batch = append(batch, e)
	}
	rows.Close()
	if err := rows.Err(); err != nil {
		return fmt.Errorf("rows: %w", err)
	}

	// Admin toggles: read the per-kind switches once per drain. A kind an admin
	// disabled is consumed-and-dropped (marked sent = suppressed) rather than left
	// queued, so it never piles up and re-enabling does not flush a stale backlog.
	// An unknown kind (absent from the settings map) is delivered -- fail-open, a
	// missing switch must not silently swallow a new event type.
	enabled := readEnabledKinds(ctx, conn)

	for _, e := range batch {
		if on, present := enabled[e.kind]; present && !on {
			if _, err := conn.Exec(ctx,
				"UPDATE external_status_events SET sent_at = now() WHERE id = $1", e.id); err != nil {
				return fmt.Errorf("mark suppressed %d: %w", e.id, err)
			}
			logf("suppressed event %d (%s): kind disabled by admin", e.id, e.kind)
			continue
		}
		if !deliverEvent(deliver, tracker, e, time.Now()) {
			// Leave sent_at NULL; stop here to preserve order, retry next wake.
			// (discordgo already slept through any 429 internally before failing.)
			return nil
		}
		if _, err := conn.Exec(ctx,
			"UPDATE external_status_events SET sent_at = now() WHERE id = $1", e.id); err != nil {
			return fmt.Errorf("mark sent %d: %w", e.id, err)
		}
		logf("delivered event %d (%s)", e.id, e.kind)
	}
	return nil
}

// deliverEvent routes one event to Discord and returns true when the row is
// resolved (mark it sent) or false when delivery failed (leave it for retry).
//
//   - player_destroyed: post the wreck line, remember its message id keyed by the
//     player so a jumpstart can edit it.
//   - jumpstarted: if the player has a recent wreck on file, EDIT that message
//     (strike the wreck line, append the jumpstart line) instead of posting a new
//     one; the edit failing leaves the row for retry (the wreck record is kept).
//     With no recent wreck (towed to station, or window elapsed, or sidecar
//     restarted) the event is consumed without posting -- the annotation only
//     makes sense attached to a wreck line.
//   - everything else: a plain post.
func deliverEvent(deliver deliverer, tracker *deathTracker, e event, now time.Time) bool {
	switch e.kind {
	case "player_destroyed":
		msgID, ok := deliver.send(e.content)
		if !ok {
			return false
		}
		if name := playerNameFromContent(e.content); name != "" && msgID != "" {
			tracker.recordDeath(name, msgID, e.content, now)
		}
		return true

	case "jumpstarted":
		name := playerNameFromContent(e.content)
		rec, ok := tracker.recentDeath(name, now)
		if !ok {
			logf("jumpstart for %q has no recent wreck to annotate -- consumed without posting", name)
			return true
		}
		if !deliver.edit(rec.messageID, strikethrough(rec.content)+" "+e.content) {
			return false // keep the record; retry the edit next wake
		}
		tracker.clear(name)
		return true

	default:
		_, ok := deliver.send(e.content)
		return ok
	}
}

// sweep deletes delivered rows older than the retention window so the table does
// not grow without bound. A failure is logged and ignored -- retention is
// best-effort and never blocks delivery.
func sweep(ctx context.Context, conn *pgx.Conn, days int) {
	tag, err := conn.Exec(ctx,
		fmt.Sprintf("DELETE FROM external_status_events WHERE sent_at IS NOT NULL AND sent_at < now() - interval '%d days'", days))
	if err != nil {
		logf("retention sweep failed: %v", err)
		return
	}
	if n := tag.RowsAffected(); n > 0 {
		logf("retention: deleted %d delivered rows older than %d days", n, days)
	}
}
