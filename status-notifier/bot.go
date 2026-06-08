// bot.go -- optional read-only Discord bot for the status-notifier sidecar
// (Phase AM-8).
//
// Separate from the webhook relay (main.go): the relay PUSHES rendered event
// lines outbound; this bot answers a PULL `/status` slash command. Both are
// optional and independent -- set STATUS_WEBHOOK_URL for push, DISCORD_BOT_TOKEN
// for the bot, either/both/neither.
//
// The bot connects to the Discord gateway (an OUTBOUND websocket -- no inbound
// port is exposed) and registers one slash command, `/status`. On invocation it
// runs READ-ONLY SQL against the databases the rest of the stack already owns
// and replies with an embed:
//
//   - players online (with C/E/T levels, class, and the sector they occupy),
//     from net7_user (accounts + avatar_info + avatar_data + avatar_level_info);
//   - authoritative player count, sectors STARTED in memory, and server uptime,
//     from the server_status heartbeat row the game server UPSERTs (net7_user);
//   - sector names resolved from the net7 content DB.
//
// It NEVER mutates game state and has no path into the game process: everything
// it reports is a plain SELECT. That keeps CLAUDE.md's "no inbound control path
// into the security-sensitive server" rule intact -- the bot is just another DB
// reader, like the editor tools.
//
// Config (env; the token is a SECRET, never committed):
//
//	DISCORD_BOT_TOKEN   the bot token (required to run the bot; empty => no bot).
//	DISCORD_GUILD_ID    optional -- register /status to this guild for instant
//	                    availability. Empty registers it globally (can take up to
//	                    ~1h to propagate the first time).
package main

import (
	"context"
	"fmt"
	"strings"
	"time"

	"github.com/bwmarrin/discordgo"
	"github.com/jackc/pgx/v5"
)

// classNames maps ClassIndex (race*3 + prof) to the real EnB class name + the
// two-letter class code. Mirrors the server (UDP_Global.cpp kClassAbbrev) and the
// CLI (CharacterClass.cs ClassTable): the profession slot is an archetype but the
// class NAME is race-specific -- Terran warrior = Enforcer (TE), Jenquai warrior =
// Defender (JD), etc. There is no "TW"/"JW".
var classNames = [9]struct{ Name, Code string }{
	{"Enforcer", "TE"}, {"Trader", "TT"}, {"Scout", "TS"},
	{"Defender", "JD"}, {"Seeker", "JS"}, {"Explorer", "JE"},
	{"Warrior", "PW"}, {"Privateer", "PP"}, {"Sentinel", "PS"},
}

func className(race, prof int) (string, string) {
	idx := race*3 + prof
	if idx < 0 || idx >= len(classNames) {
		return fmt.Sprintf("Class(%d,%d)", race, prof), "??"
	}
	return classNames[idx].Name, classNames[idx].Code
}

// onlinePlayer is one row of the /status player table.
type onlinePlayer struct {
	username  string
	firstName string
	sector    int64
	race      int
	prof      int
	combat    int
	explore   int
	trade     int
}

// serverStatus is the heartbeat row the game server keeps fresh.
type serverStatus struct {
	bootTime      time.Time
	playersOnline int
	sectorsOnline int
	updatedAt     time.Time
	uptimeSecs    int64
	staleSecs     int64
}

// startBot runs the Discord bot until ctx is cancelled. It is its own goroutine
// in main(); a failure here must not take down the webhook relay, so it logs and
// returns rather than crashing the process. userDSN is the net7_user DSN; the
// net7 DSN is derived from it for sector-name lookups.
func startBot(ctx context.Context, userDSN, token, guildID string) {
	dg, err := discordgo.New("Bot " + token)
	if err != nil {
		logf("bot: discordgo.New failed: %v -- bot disabled", err)
		return
	}
	// We only need to receive interactions; no privileged message-content intent.
	dg.Identify.Intents = discordgo.IntentsGuilds

	dg.AddHandler(func(s *discordgo.Session, ic *discordgo.InteractionCreate) {
		if ic.Type != discordgo.InteractionApplicationCommand {
			return
		}
		if ic.ApplicationCommandData().Name != "status" {
			return
		}
		handleStatusCommand(ctx, s, ic, userDSN)
	})

	if err := dg.Open(); err != nil {
		logf("bot: gateway open failed: %v -- bot disabled", err)
		return
	}
	defer dg.Close()
	logf("bot: connected to Discord gateway as %s", dg.State.User.String())

	cmd := &discordgo.ApplicationCommand{
		Name:        "status",
		Description: "Show live server status: players online, sectors, uptime.",
	}
	if _, err := dg.ApplicationCommandCreate(dg.State.User.ID, guildID, cmd); err != nil {
		logf("bot: registering /status failed: %v", err)
		// Keep running -- a previously-registered command may still work.
	} else if guildID != "" {
		logf("bot: /status registered to guild %s", guildID)
	} else {
		logf("bot: /status registered globally (first propagation can take ~1h)")
	}

	<-ctx.Done()
	logf("bot: shutting down")
}

// handleStatusCommand answers one /status invocation. It defers the reply first
// (Discord requires an ack within 3s) then edits in the assembled embed once the
// queries finish.
func handleStatusCommand(ctx context.Context, s *discordgo.Session, ic *discordgo.InteractionCreate, userDSN string) {
	if err := s.InteractionRespond(ic.Interaction, &discordgo.InteractionResponse{
		Type: discordgo.InteractionResponseDeferredChannelMessageWithSource,
	}); err != nil {
		logf("bot: defer ack failed: %v", err)
		return
	}

	embed, err := buildStatusEmbed(ctx, userDSN)
	if err != nil {
		logf("bot: /status query failed: %v", err)
		msg := "Could not read server status (database unreachable)."
		_, _ = s.InteractionResponseEdit(ic.Interaction, &discordgo.WebhookEdit{Content: &msg})
		return
	}
	if _, err := s.InteractionResponseEdit(ic.Interaction, &discordgo.WebhookEdit{
		Embeds: &[]*discordgo.MessageEmbed{embed},
	}); err != nil {
		logf("bot: /status reply edit failed: %v", err)
	}
}

// buildStatusEmbed runs the read-only queries and renders the status embed.
func buildStatusEmbed(ctx context.Context, userDSN string) (*discordgo.MessageEmbed, error) {
	uconn, err := pgx.Connect(ctx, userDSN)
	if err != nil {
		return nil, fmt.Errorf("connect net7_user: %w", err)
	}
	defer uconn.Close(ctx)

	status, haveStatus := readServerStatus(ctx, uconn)
	players, err := readOnlinePlayers(ctx, uconn)
	if err != nil {
		return nil, err
	}
	sectorNames := readSectorNames(ctx, userDSN) // best-effort; empty map on failure

	embed := &discordgo.MessageEmbed{
		Title: "Earth & Beyond -- Server Status",
		Color: 0x39a0ed,
	}

	// Header: up/down + uptime + counts. The heartbeat is considered live only if
	// it was written recently; a stale row means the server is down or wedged.
	const staleThreshold = 120 // seconds; heartbeat cadence is ~30s
	if haveStatus && status.staleSecs <= staleThreshold {
		embed.Description = fmt.Sprintf(
			"🟢 **Online** -- uptime %s\nPlayers: **%d**  |  Sectors loaded: **%d**",
			humanDuration(status.uptimeSecs), status.playersOnline, status.sectorsOnline)
	} else if haveStatus {
		embed.Description = fmt.Sprintf(
			"🔴 **Offline / not responding** -- last heartbeat %s ago.",
			humanDuration(status.staleSecs))
	} else {
		embed.Description = "🔴 **Offline** -- no heartbeat recorded yet."
	}

	// Player table. The DB-derived list is the source of truth for WHO is online
	// (and survives a heartbeat gap); the heartbeat count is the authoritative
	// in-memory number shown above.
	if len(players) == 0 {
		embed.Fields = append(embed.Fields, &discordgo.MessageEmbedField{
			Name:  "Players",
			Value: "_nobody online_",
		})
	} else {
		var b strings.Builder
		for _, p := range players {
			b.WriteString(renderPlayerLine(p, sectorNames))
			b.WriteByte('\n')
		}
		// Embed field values cap at 1024 chars; truncate gracefully.
		val := b.String()
		if len(val) > 1024 {
			val = val[:1000] + "\n… (truncated)"
		}
		embed.Fields = append(embed.Fields, &discordgo.MessageEmbedField{
			Name:  fmt.Sprintf("Players (%d)", len(players)),
			Value: val,
		})
	}

	return embed, nil
}

// renderPlayerLine formats one player row. A player sitting in character-select
// (no entered avatar) shows without a sector/levels.
func renderPlayerLine(p onlinePlayer, sectorNames map[int64]string) string {
	if p.firstName == "" {
		return fmt.Sprintf("• `%s` -- _in character select_", p.username)
	}
	clsName, clsCode := className(p.race, p.prof)
	sector := "?"
	if p.sector > 0 {
		if name, ok := sectorNames[p.sector]; ok && name != "" {
			sector = fmt.Sprintf("%s (%d)", name, p.sector)
		} else {
			sector = fmt.Sprintf("%d", p.sector)
		}
	}
	return fmt.Sprintf("• **%s** -- %s %s, lvl %d/%d/%d (C/E/T) -- %s",
		p.firstName, clsName, clsCode, p.combat, p.explore, p.trade, sector)
}

// readServerStatus reads the singleton heartbeat row. The second return is false
// if there is no row yet (server never heartbeated).
func readServerStatus(ctx context.Context, conn *pgx.Conn) (serverStatus, bool) {
	var st serverStatus
	err := conn.QueryRow(ctx,
		`SELECT boot_time, players_online, sectors_online, updated_at,
		        EXTRACT(EPOCH FROM (now() - boot_time))::bigint,
		        EXTRACT(EPOCH FROM (now() - updated_at))::bigint
		   FROM server_status WHERE id = 1`).
		Scan(&st.bootTime, &st.playersOnline, &st.sectorsOnline, &st.updatedAt,
			&st.uptimeSecs, &st.staleSecs)
	if err != nil {
		return serverStatus{}, false
	}
	return st, true
}

// readOnlinePlayers returns the accounts whose session is open (last_login >
// last_logout), LEFT JOINed to their entered avatar so a character-select
// session still appears (with an empty firstName).
func readOnlinePlayers(ctx context.Context, conn *pgx.Conn) ([]onlinePlayer, error) {
	rows, err := conn.Query(ctx,
		`SELECT a.username,
		        COALESCE(d.first_name, ''),
		        COALESCE(i.sector, 0),
		        COALESCE(d.race, 0),
		        COALESCE(d.prof, 0),
		        COALESCE(floor(l.combat_bar_level)::int, 0),
		        COALESCE(floor(l.explore_bar_level)::int, 0),
		        COALESCE(floor(l.trade_bar_level)::int, 0)
		   FROM accounts a
		   LEFT JOIN avatar_info i
		          ON i.account_id = a.id AND i.last_login > i.last_logout
		   LEFT JOIN avatar_data d        ON d.avatar_id = i.avatar_id
		   LEFT JOIN avatar_level_info l  ON l.avatar_id = i.avatar_id
		  WHERE a.last_login > a.last_logout
		  ORDER BY a.username`)
	if err != nil {
		return nil, fmt.Errorf("query online players: %w", err)
	}
	defer rows.Close()

	var out []onlinePlayer
	for rows.Next() {
		var p onlinePlayer
		if err := rows.Scan(&p.username, &p.firstName, &p.sector,
			&p.race, &p.prof, &p.combat, &p.explore, &p.trade); err != nil {
			return nil, fmt.Errorf("scan online player: %w", err)
		}
		out = append(out, p)
	}
	return out, rows.Err()
}

// readSectorNames pulls sector_id -> name from the net7 CONTENT database (a
// different DB than net7_user, so a separate connection -- a single connection
// cannot cross-DB join in Postgres). Best-effort: on any failure we return an
// empty map and the caller falls back to bare sector ids.
func readSectorNames(ctx context.Context, userDSN string) map[int64]string {
	names := map[int64]string{}
	contentDSN := swapDBName(userDSN, "net7")
	conn, err := pgx.Connect(ctx, contentDSN)
	if err != nil {
		logf("bot: sector-name lookup connect failed: %v (showing ids only)", err)
		return names
	}
	defer conn.Close(ctx)

	rows, err := conn.Query(ctx, "SELECT sector_id, name FROM sectors")
	if err != nil {
		logf("bot: sector-name query failed: %v (showing ids only)", err)
		return names
	}
	defer rows.Close()
	for rows.Next() {
		var id int64
		var name string
		if err := rows.Scan(&id, &name); err != nil {
			continue
		}
		names[id] = name
	}
	return names
}

// humanDuration renders a second count as a compact "Nd Nh Nm" string.
func humanDuration(secs int64) string {
	if secs < 0 {
		secs = 0
	}
	d := secs / 86400
	h := (secs % 86400) / 3600
	m := (secs % 3600) / 60
	parts := []string{}
	if d > 0 {
		parts = append(parts, fmt.Sprintf("%dd", d))
	}
	if h > 0 {
		parts = append(parts, fmt.Sprintf("%dh", h))
	}
	if m > 0 || len(parts) == 0 {
		parts = append(parts, fmt.Sprintf("%dm", m))
	}
	return strings.Join(parts, " ")
}
