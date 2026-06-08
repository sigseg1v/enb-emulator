// status-notifier -- out-of-process relay for the game server's external-status
// events (Phase AM).
//
// The C++ game server never talks to Discord (or any external service) directly:
// pulling an outbound HTTP client into the security-sensitive game process would
// widen its attack surface for a convenience feature, which CLAUDE.md forbids.
// Instead the server INSERTs a fully-rendered message line into the
// external_status_events table (in net7_user) and fires `NOTIFY
// external_status_event`. This sidecar LISTENs for that, drains the unsent rows,
// and POSTs each one to a webhook (Discord today; the table and protocol are
// consumer-agnostic).
//
// Durability: a row stays with sent_at = NULL until this process confirms a 2xx,
// so events survive a sidecar restart and nothing is silently dropped. On startup
// the sidecar drains anything that piled up while it was down BEFORE settling into
// LISTEN.
//
// Config (all env; secret never committed):
//
//	STATUS_WEBHOOK_URL   the webhook to POST each event to (SECRET, required to
//	                     actually deliver; if empty the sidecar idles -- it does
//	                     not crash-loop, so a default-off compose stack is fine).
//	DB_HOST              postgres host:port (default "postgres:5432"); same env
//	DB_USER              the other services already use. DB_NAME defaults to
//	DB_PASS              net7_user (where the outbox lives).
//	DB_NAME
//	DATABASE_URL         optional -- a full libpq DSN/URL that overrides the DB_*
//	                     pieces if set.
//	STATUS_RETENTION_DAYS  delete delivered rows older than this many days
//	                       (default 7; 0 disables the sweep).
package main

import (
	"context"
	"fmt"
	"net/http"
	"net/url"
	"os"
	"strconv"
	"strings"
	"time"

	"github.com/jackc/pgx/v5"
)

const (
	notifyChannel = "external_status_event"
	// Discord caps a webhook message at 2000 chars; truncate a runaway broadcast
	// rather than have the POST rejected. Generic enough for other webhooks too.
	maxContentLen = 2000
)

// logf writes a single line to stdout. We deliberately never log the webhook URL
// or any secret -- only event ids, kinds, and HTTP status codes.
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

func main() {
	webhook := os.Getenv("STATUS_WEBHOOK_URL")
	botToken := os.Getenv("DISCORD_BOT_TOKEN")
	dsn := buildDSN()
	ctx := context.Background()

	// The sidecar carries two independent, both-optional features that share only
	// the DB connection details: the outbound webhook RELAY (push) and the inbound
	// `/status` BOT (pull). Each is enabled by its own secret env var; with neither
	// set the process idles rather than crash-looping, so a default-off compose
	// stack stays clean under `docker compose ps`.
	if botToken != "" {
		guildID := os.Getenv("DISCORD_GUILD_ID")
		go startBot(ctx, buildDSN(), botToken, guildID)
		logf("discord /status bot enabled")
	}

	if webhook == "" {
		if botToken == "" {
			logf("neither STATUS_WEBHOOK_URL nor DISCORD_BOT_TOKEN is set -- " +
				"nothing to do; idling. Set either (a secret) to enable a feature.")
		} else {
			logf("STATUS_WEBHOOK_URL is empty -- webhook relay disabled; bot only.")
		}
		select {} // block forever; the bot goroutine (if any) does the work.
	}

	retentionDays := 7
	if v := os.Getenv("STATUS_RETENTION_DAYS"); v != "" {
		if n, err := strconv.Atoi(v); err == nil && n >= 0 {
			retentionDays = n
		}
	}

	// Reconnect loop: a dropped DB connection (postgres restart, network blip)
	// must not kill the sidecar -- back off and re-establish.
	for {
		if err := run(ctx, dsn, webhook, retentionDays); err != nil {
			logf("connection error: %v -- reconnecting in 5s", err)
			time.Sleep(5 * time.Second)
			continue
		}
		return
	}
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
func run(ctx context.Context, dsn, webhook string, retentionDays int) error {
	conn, err := pgx.Connect(ctx, dsn)
	if err != nil {
		return err
	}
	defer conn.Close(ctx)

	if _, err := conn.Exec(ctx, "LISTEN "+notifyChannel); err != nil {
		return fmt.Errorf("LISTEN: %w", err)
	}
	logf("connected; LISTEN %s; webhook delivery enabled", notifyChannel)

	// Catch up on anything inserted while we were down, before waiting for the
	// next NOTIFY.
	if err := drain(ctx, conn, webhook); err != nil {
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

		if err := drain(ctx, conn, webhook); err != nil {
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
func drain(ctx context.Context, conn *pgx.Conn, webhook string) error {
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

	for _, e := range batch {
		delivered, retryAfter := deliver(webhook, e.content)
		if !delivered {
			if retryAfter > 0 {
				logf("rate-limited on event %d (%s); backing off %v", e.id, e.kind, retryAfter)
				time.Sleep(retryAfter)
			}
			// Leave sent_at NULL; stop here to preserve order, retry next wake.
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

var httpClient = &http.Client{Timeout: 15 * time.Second}

// deliver POSTs one rendered line to the webhook as a Discord-style
// {"content": "..."} body. Returns (true, 0) on a 2xx. On HTTP 429 it returns
// (false, retry-after) so the caller backs off; on any other failure it returns
// (false, 0) and the row stays unsent for the next attempt.
func deliver(webhook, content string) (bool, time.Duration) {
	if len(content) > maxContentLen {
		content = content[:maxContentLen]
	}
	// content is JSON-encoded via a minimal escaper rather than dragging in a
	// struct: it is a single string field.
	body := `{"content":` + jsonString(content) + `}`

	req, err := http.NewRequest(http.MethodPost, webhook, strings.NewReader(body))
	if err != nil {
		logf("build request failed: %v", err)
		return false, 0
	}
	req.Header.Set("Content-Type", "application/json")

	resp, err := httpClient.Do(req)
	if err != nil {
		logf("POST failed: %v", err)
		return false, 0
	}
	defer resp.Body.Close()

	if resp.StatusCode >= 200 && resp.StatusCode < 300 {
		return true, 0
	}
	if resp.StatusCode == http.StatusTooManyRequests {
		if ra := resp.Header.Get("Retry-After"); ra != "" {
			if secs, err := strconv.ParseFloat(ra, 64); err == nil {
				return false, time.Duration(secs*1000) * time.Millisecond
			}
		}
		return false, time.Second
	}
	logf("webhook returned HTTP %d -- will retry", resp.StatusCode)
	return false, 0
}

// jsonString returns a JSON-quoted string literal for s (including the quotes),
// escaping the characters JSON requires. Keeps the dependency surface minimal for
// a one-field body.
func jsonString(s string) string {
	var b strings.Builder
	b.WriteByte('"')
	for _, r := range s {
		switch r {
		case '"':
			b.WriteString(`\"`)
		case '\\':
			b.WriteString(`\\`)
		case '\n':
			b.WriteString(`\n`)
		case '\r':
			b.WriteString(`\r`)
		case '\t':
			b.WriteString(`\t`)
		default:
			if r < 0x20 {
				fmt.Fprintf(&b, `\u%04x`, r)
			} else {
				b.WriteRune(r)
			}
		}
	}
	b.WriteByte('"')
	return b.String()
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
