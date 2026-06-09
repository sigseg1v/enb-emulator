// bot.go -- the Discord bot for the status-notifier sidecar (Phase AM-8 / AM-9).
//
// The bot is the sidecar's single Discord touchpoint: main.go's relay posts each
// event line THROUGH this bot's session into STATUS_CHANNEL_ID, and the bot also
// answers two slash commands -- `/status` (read-only snapshot) and `/notify`
// (admin-only per-kind relay toggles).
//
// The bot connects to the Discord gateway (an OUTBOUND websocket -- no inbound
// port is exposed) and registers the slash commands. On a `/status` invocation it
// runs READ-ONLY SQL against the databases the rest of the stack already owns
// and replies with an embed:
//
//   - players online (with C/E/T levels, class, and the sector they occupy),
//     from net7_user (accounts + avatar_info + avatar_data + avatar_level_info);
//   - authoritative player count, sectors STARTED in memory, and server uptime,
//     from the server_status heartbeat row the game server UPSERTs (net7_user);
//   - location names resolved from the net7 content DB (sectors for players in
//     space, starbases for docked players).
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

// classCodes maps ClassIndex (race*3 + prof) to the two-letter class code.
// Mirrors the server (UDP_Global.cpp kClassAbbrev) and the CLI (CharacterClass.cs
// ClassTable): the profession slot is an archetype but the class identity is
// race-specific. The full names (for reference) are, in order:
//
//	TE Enforcer, TT Trader, TS Scout, JD Defender, JS Seeker, JE Explorer,
//	PW Warrior, PP Privateer, PS Sentinel. There is no "TW"/"JW".
var classCodes = [9]string{
	"TE", "TT", "TS",
	"JD", "JS", "JE",
	"PW", "PP", "PS",
}

func classCode(race, prof int) string {
	idx := race*3 + prof
	if idx < 0 || idx >= len(classCodes) {
		return "??"
	}
	return classCodes[idx]
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

// startBot opens the Discord gateway, registers the slash commands, and returns
// the live session (nil on failure -- a bot failure must never take down the
// relay). The session stays usable for REST sends (botDeliverer) until ctx is
// cancelled, at which point a watcher goroutine closes the gateway. userDSN is the
// net7_user DSN; the net7 DSN is derived from it for sector-name lookups.
func startBot(ctx context.Context, userDSN, token, guildID string) *discordgo.Session {
	dg, err := discordgo.New("Bot " + token)
	if err != nil {
		logf("bot: discordgo.New failed: %v -- bot disabled", err)
		return nil
	}
	// We only need to receive interactions; no privileged message-content intent.
	dg.Identify.Intents = discordgo.IntentsGuilds

	dg.AddHandler(func(s *discordgo.Session, ic *discordgo.InteractionCreate) {
		if ic.Type != discordgo.InteractionApplicationCommand {
			return
		}
		switch ic.ApplicationCommandData().Name {
		case "status":
			handleStatusCommand(ctx, s, ic, userDSN)
		case "notify":
			handleNotifyCommand(ctx, s, ic, userDSN)
		}
	})

	if err := dg.Open(); err != nil {
		logf("bot: gateway open failed: %v -- bot disabled", err)
		return nil
	}
	logf("bot: connected to Discord gateway as %s", dg.State.User.String())

	registerCommands(dg, guildID)

	// Close the gateway when the process context ends. Until then the returned
	// session is used by botDeliverer for REST message posts.
	go func() {
		<-ctx.Done()
		logf("bot: shutting down")
		dg.Close()
	}()

	return dg
}

// registerCommands (re)registers /status and the admin-gated /notify. A failure
// to register one is logged but non-fatal -- a previously-registered copy may
// still serve.
func registerCommands(dg *discordgo.Session, guildID string) {
	// /notify is admin-only: Discord hides it from, and rejects it for, members
	// without Manage Server. This is the primary gate (handleNotifyCommand
	// re-checks as defense in depth).
	manageGuild := int64(discordgo.PermissionManageServer)
	cmds := []*discordgo.ApplicationCommand{
		{
			Name:        "status",
			Description: "Show live server status: players online, sectors, uptime.",
		},
		{
			Name:                     "notify",
			Description:              "Admin: toggle which status notifications the relay posts.",
			DefaultMemberPermissions: &manageGuild,
			Options: []*discordgo.ApplicationCommandOption{
				{
					Type:        discordgo.ApplicationCommandOptionSubCommand,
					Name:        "list",
					Description: "Show which notification kinds are on or off.",
				},
				{
					Type:        discordgo.ApplicationCommandOptionSubCommand,
					Name:        "set",
					Description: "Turn a notification kind on or off.",
					Options: []*discordgo.ApplicationCommandOption{
						{
							Type:        discordgo.ApplicationCommandOptionString,
							Name:        "kind",
							Description: "Which notification kind.",
							Required:    true,
							Choices:     kindChoices(),
						},
						{
							Type:        discordgo.ApplicationCommandOptionString,
							Name:        "state",
							Description: "Turn it on or off.",
							Required:    true,
							Choices: []*discordgo.ApplicationCommandOptionChoice{
								{Name: "on", Value: "on"},
								{Name: "off", Value: "off"},
							},
						},
					},
				},
			},
		},
	}
	for _, cmd := range cmds {
		if _, err := dg.ApplicationCommandCreate(dg.State.User.ID, guildID, cmd); err != nil {
			logf("bot: registering /%s failed: %v", cmd.Name, err)
		}
	}
	if guildID != "" {
		logf("bot: commands registered to guild %s", guildID)
	} else {
		logf("bot: commands registered globally (first propagation can take ~1h)")
	}
}

// kindChoices renders the kind allowlist as slash-command choices so the picker
// only ever offers a known kind.
func kindChoices() []*discordgo.ApplicationCommandOptionChoice {
	choices := make([]*discordgo.ApplicationCommandOptionChoice, 0, len(notificationKinds))
	for _, k := range notificationKinds {
		choices = append(choices, &discordgo.ApplicationCommandOptionChoice{Name: k, Value: k})
	}
	return choices
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
	locationNames := readLocationNames(ctx, userDSN) // best-effort; empty map on failure

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
			b.WriteString(renderPlayerLine(p, locationNames))
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
// (no entered avatar) shows without a location/levels. locationNames maps the
// avatar_info.sector value (a sector id in space, or a starbase interior id when
// docked) to a human name; an unknown id falls back to the bare number.
func renderPlayerLine(p onlinePlayer, locationNames map[int64]string) string {
	if p.firstName == "" {
		return fmt.Sprintf("• `%s` -- _in character select_", p.username)
	}
	clsCode := classCode(p.race, p.prof)
	location := "?"
	if p.sector > 0 {
		if name, ok := locationNames[p.sector]; ok && name != "" {
			location = name
		} else {
			location = fmt.Sprintf("%d", p.sector)
		}
	}
	// Displayed character level is the highest of the three skill levels (EnB
	// convention); the per-skill breakdown follows in parentheses.
	level := p.combat
	if p.explore > level {
		level = p.explore
	}
	if p.trade > level {
		level = p.trade
	}
	return fmt.Sprintf("• **%s** -- %s, lvl %d (C%d/E%d/T%d) -- %s",
		p.firstName, clsCode, level, p.combat, p.explore, p.trade, location)
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
		        COALESCE(i.combat, 0)::int,
		        COALESCE(i.explore, 0)::int,
		        COALESCE(i.trade, 0)::int
		   FROM accounts a
		   LEFT JOIN avatar_info i
		          ON i.account_id = a.id AND i.last_login > i.last_logout
		   LEFT JOIN avatar_data d        ON d.avatar_id = i.avatar_id
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

// readLocationNames maps the value stored in avatar_info.sector to a human name,
// pulled from the net7 CONTENT database (a different DB than net7_user, so a
// separate connection -- a single connection cannot cross-DB join in Postgres).
//
// That value is NOT always a sector id: when a player is docked the server stores
// the starbase's interior id (starbases.starbase_sector_id, a distinct high-numbered
// id space) instead of the parent sector_id. So we load BOTH tables into one map --
// sectors.sector_id -> name for players in space, and starbases.starbase_sector_id
// -> name for docked players. Sectors are loaded first; a starbase never overwrites
// a real sector entry on the off chance the id spaces ever collide.
//
// Best-effort: on any failure we return whatever we have and the caller falls back
// to the bare id.
func readLocationNames(ctx context.Context, userDSN string) map[int64]string {
	names := map[int64]string{}
	contentDSN := swapDBName(userDSN, "net7")
	conn, err := pgx.Connect(ctx, contentDSN)
	if err != nil {
		logf("bot: location-name lookup connect failed: %v (showing ids only)", err)
		return names
	}
	defer conn.Close(ctx)

	load := func(query, label string) {
		rows, err := conn.Query(ctx, query)
		if err != nil {
			logf("bot: %s query failed: %v (showing ids only)", label, err)
			return
		}
		defer rows.Close()
		for rows.Next() {
			var id int64
			var name string
			if err := rows.Scan(&id, &name); err != nil {
				continue
			}
			if _, taken := names[id]; !taken && name != "" {
				names[id] = name
			}
		}
	}

	load("SELECT sector_id, name FROM sectors", "sector-name")
	load("SELECT starbase_sector_id, name FROM starbases WHERE starbase_sector_id IS NOT NULL", "starbase-name")
	return names
}

// handleNotifyCommand answers /notify list and /notify set. Discord already gates
// this on Manage Server (DefaultMemberPermissions); we re-check here so a stale or
// mis-scoped registration can never let a non-admin flip a switch. Replies are
// ephemeral so the toggles do not spam the channel. All writes bind parameters and
// validate the kind against the allowlist.
func handleNotifyCommand(ctx context.Context, s *discordgo.Session, ic *discordgo.InteractionCreate, userDSN string) {
	if !callerIsAdmin(ic) {
		respondEphemeral(s, ic, "You need the Manage Server permission to use /notify.")
		return
	}
	data := ic.ApplicationCommandData()
	if len(data.Options) == 0 {
		respondEphemeral(s, ic, "Usage: /notify list  |  /notify set <kind> <on|off>")
		return
	}
	sub := data.Options[0]

	conn, err := pgx.Connect(ctx, userDSN)
	if err != nil {
		logf("notify: connect failed: %v", err)
		respondEphemeral(s, ic, "Could not reach the settings database.")
		return
	}
	defer conn.Close(ctx)

	switch sub.Name {
	case "list":
		enabled := readEnabledKinds(ctx, conn)
		var b strings.Builder
		b.WriteString("**Status relay -- per-kind state**\n")
		for _, k := range notificationKinds {
			state := "🔴 off"
			if enabled[k] {
				state = "🟢 on"
			}
			fmt.Fprintf(&b, "• `%s` -- %s\n", k, state)
		}
		respondEphemeral(s, ic, b.String())

	case "set":
		kind := optString(sub.Options, "kind")
		state := optString(sub.Options, "state")
		if !isKnownKind(kind) {
			respondEphemeral(s, ic, fmt.Sprintf("Unknown notification kind %q.", kind))
			return
		}
		if state != "on" && state != "off" {
			respondEphemeral(s, ic, "State must be `on` or `off`.")
			return
		}
		updatedBy := callerID(ic)
		if err := setKindEnabled(ctx, conn, kind, state == "on", updatedBy); err != nil {
			logf("notify: set %s=%s failed: %v", kind, state, err)
			respondEphemeral(s, ic, "Failed to update the setting.")
			return
		}
		logf("notify: %s set %s -> %s", updatedBy, kind, state)
		respondEphemeral(s, ic, fmt.Sprintf("`%s` notifications are now **%s**.", kind, state))

	default:
		respondEphemeral(s, ic, "Unknown subcommand.")
	}
}

// callerIsAdmin returns true only if the invoking guild member has Manage Server.
// ic.Member.Permissions is Discord's computed permission set for the caller in the
// invoking channel, so this needs no extra lookup. A DM invocation (no Member) is
// rejected -- /notify is a guild operation.
func callerIsAdmin(ic *discordgo.InteractionCreate) bool {
	if ic.Member == nil {
		return false
	}
	return ic.Member.Permissions&discordgo.PermissionManageServer != 0
}

// callerID returns the invoking user's Discord id for the audit trail.
func callerID(ic *discordgo.InteractionCreate) string {
	if ic.Member != nil && ic.Member.User != nil {
		return ic.Member.User.ID
	}
	if ic.User != nil {
		return ic.User.ID
	}
	return "unknown"
}

// optString pulls a named string option from an interaction option list.
func optString(opts []*discordgo.ApplicationCommandInteractionDataOption, name string) string {
	for _, o := range opts {
		if o.Name == name {
			return o.StringValue()
		}
	}
	return ""
}

// respondEphemeral sends a one-shot ephemeral reply (visible only to the caller).
func respondEphemeral(s *discordgo.Session, ic *discordgo.InteractionCreate, msg string) {
	if err := s.InteractionRespond(ic.Interaction, &discordgo.InteractionResponse{
		Type: discordgo.InteractionResponseChannelMessageWithSource,
		Data: &discordgo.InteractionResponseData{
			Content: msg,
			Flags:   discordgo.MessageFlagsEphemeral,
		},
	}); err != nil {
		logf("notify: respond failed: %v", err)
	}
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
