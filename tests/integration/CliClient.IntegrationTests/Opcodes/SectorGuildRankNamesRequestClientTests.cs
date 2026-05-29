// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 39 post-handshake survival round-trip: client sends 0x00D4
/// GUILD_RANK_NAMES_REQUEST_CLIENT on a fresh-starbase character
/// (whose <c>m_GuildID</c> is 0 — guild not joined), then verifies
/// the connection survives via 0x0044 REQUEST_TIME.
///
/// <para>
/// Wire layout (from the local struct at
/// <c>server/src/PlayerGuild.cpp:677-681</c>):
/// </para>
/// <code>
///   struct GuildRankNamesRequestPacket
///   {
///       long  gameid;   // sizeof(long) bug — should be int32_t
///       short unknown;
///   };
/// </code>
/// <para>
/// The wire-side field widths are 4B + 2B = 6B; the local struct's
/// <c>long gameid</c> is the same Phase K Wave 11 bug class
/// (Linux x86_64 sizeof(long)==8 would over-read 2B into adjacent
/// memory). But the handler at PlayerGuild.cpp:675-699 NEVER
/// field-accesses the cast result — it only does
/// <c>request = (struct GuildRankNamesRequestPacket *)data;</c>
/// without ever reading <c>request->gameid</c> or
/// <c>request->unknown</c>. So the bug is dormant for this payload,
/// and a 6-byte all-zero buffer is the minimal-and-safe canonical
/// wire shape.
/// </para>
///
/// <para>
/// Server handler walk-through.
/// </para>
/// <list type="number">
///   <item>Dispatcher case at <c>server/src/PlayerConnection.cpp:617</c>
///     calls <c>HandleGuildRankNamesRequestClient(data)</c>.</item>
///   <item>Handler casts <c>data</c> to
///     <c>GuildRankNamesRequestPacket *</c>. Cast result is never
///     field-accessed (no <c>request->gameid</c> or
///     <c>request->unknown</c> read anywhere).</item>
///   <item><c>g = g_PlayerMgr->GuildFromId(m_GuildID)</c>. For a
///     fresh-starbase character <c>m_GuildID</c> is 0 (per
///     <c>Player::SetupGuildInfo</c> at PlayerGuild.cpp:25-45
///     never being called on the fresh-char path — the only
///     setter; alternatively initialised to 0 via
///     <c>Player::FinishInit</c>).</item>
///   <item><c>GuildFromId(0)</c> returns null — no guild has ID 0
///     in any real guild registry (the dump's
///     <c>guilds.AUTO_INCREMENT</c> starts at a non-zero seed,
///     and the fresh-char test pool never seeds any guild
///     rows).</item>
///   <item>Test <c>if (g)</c> short-circuits, function returns.
///     The unconditional 10-iteration <c>AddDataLS</c>/<c>AddData</c>
///     loop at PlayerGuild.cpp:687-696 and the
///     <c>SendOpcode(ENB_OPCODE_00D3_GUILD_RANK_NAMES_SECTOR, ...)</c>
///     emit at line 697 all sit BEHIND the <c>if (g)</c> guard —
///     none of them run on this test's payload.</item>
/// </list>
/// <para>
/// Zero state mutation. Zero SendOpcode. Zero observer fan-out.
/// Same favourable post-emit shape as Wave 27 (INVENTORY_MOVE
/// default arm), Wave 29 (PETITION_STUCK), Wave 30 (RELATIONSHIP),
/// Wave 36 (STARBASE_AVATAR_CHANGE early-return), Wave 37
/// (SKILL_STRING_RQ OT_HUSK short-circuit), Wave 38
/// (CONFIRMED_ACTION_RESPONSE non-matching player_id).
/// </para>
///
/// <para>
/// Why this wave target. Wave 38 was the third draw from the
/// safe-candidate list Wave 36 triaged. Wave 39 was originally
/// queued as 0x00C5 GUILD_LEADER_ACCEPT_CLIENT, but inspection of
/// HandleGuildLeaderAcceptClient at PlayerGuild.cpp:737-766
/// surfaced an unfixed bug class — sizeof(long) over-read at line
/// 746 (`*(long *)data`) + unbounded <c>strncpy</c> at line 749
/// reading 64 bytes from <c>data[6..69]</c> regardless of the
/// short length field at offset 4 + OOB read at line 751
/// (<c>data[6+length]</c> with caller-controlled length). The
/// retail server presumably either has bounds-checked layouts
/// the upstream forks dropped, or the retail client only ever
/// emits sane payloads — either way, Wave 39 cannot survival-probe
/// 0x00C5 without either triggering UB or fixing the bug first
/// (the latter being a legitimate CLAUDE.md fidelity-tightening
/// fix but out of scope for this wave). Pivoted to 0x00D4 which
/// has a clean <c>if (g)</c> short-circuit gate and zero payload-
/// field reads, making it the safest possible survival probe.
/// </para>
///
/// <para>
/// Disambiguation note. The plans/99-decisions-log.md entry at
/// line 4449-4453 (Wave 32 era) previously claimed that all four
/// guild handlers
/// (<c>HandleGuildLeaderAcceptClient</c>,
/// <c>HandleGuildSimpleClientSector</c>,
/// <c>HandleGuildRankNamesRequestClient</c>,
/// <c>HandleRecruitAcceptClient</c>) were
/// "DECLARED in PlayerClass.h but NOT DEFINED in any .cpp file".
/// That claim was wrong: all four are defined in
/// <c>server/src/PlayerGuild.cpp</c> (lines 675/701/737/768
/// respectively). The build links them cleanly and
/// <c>nm -C server/build/net7</c> resolves all four symbols. The
/// misclassification kept these opcodes off the safe-candidate
/// list for an unnecessary period; Wave 39 restores 0x00D4.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at server/src/PlayerConnection.cpp:617.</b>
///     The 0x00D4 case label sits between 0x00CD
///     GUILD_SIMPLE_CLIENT_SECTOR (line 612) and 0x2017
///     RESEND_PACKET_SEQUENCE (line 621). A swap with
///     HandleGuildSimpleClientSector would walk a different
///     default-LogMessage path on our 6B payload (still survives
///     via REQUEST_TIME but the wrong code arm). A swap with
///     HandleGuildLeaderAcceptClient at line 604 would invoke
///     the bug-laden 0x00C5 handler on our 6B buffer — UB but
///     unlikely to SEGV with all-zero data.
///   </item>
///   <item>
///     <b><c>m_GuildID</c> initialisation regression.</b> If
///     Player constructor or FinishInit starts setting
///     <c>m_GuildID</c> to a non-zero placeholder,
///     <c>GuildFromId</c> might return non-null and
///     <c>SendOpcode(ENB_OPCODE_00D3_GUILD_RANK_NAMES_SECTOR, ...)</c>
///     would fire, emitting an unexpected 0x00D3 frame. The drain
///     loop would skip past it and the test would still pass via
///     CLIENT_SET_TIME — but a future tighter no-preceding-frame
///     assertion would catch the regression.
///   </item>
///   <item>
///     <b><c>if (g)</c> guard removal at PlayerGuild.cpp:685.</b>
///     Without the guard the handler attempts
///     <c>g_PlayerMgr->GetRankName(g, i)</c> at line 693 with a
///     null Guild* → SEGV inside GetRankName. The REQUEST_TIME
///     reply would not arrive → test times out.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at PlayerConnection.cpp:127.</b>
///     Phase K sizeof(int32_t) opcode-header fix keeps the
///     per-client UDP queue header at 4B; a revert corrupts the
///     0x2016 inner-tuple parser → REQUEST_TIME path silent.
///     Same load-bearing SendOpcode invariant as Waves
///     8/24/26/27/29/30/36/37/38.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode regression.</b>
///     0x00D4 is NOT explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>'s
///     <c>ProcessSectorServerOpcode</c> switch, so it falls
///     through to the bottom-of-switch ForwardClientOpcode
///     default arm. A regression dropping that default would
///     mean the server never sees the rank-names-request frame.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard
///     tightening at UDPProxyToClient_linux.cpp:568.</b>
///     Currently passes 0x0034 CLIENT_SET_TIME because 0x0034
///     &lt; 0x0FFF; a regression to e.g. <c>opcode &lt; 0x0040</c>
///     would silently drop the REQUEST_TIME reply.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x00D4
/// GUILD_RANK_NAMES_REQUEST_CLIENT is what the retail Win32 client
/// emits when the user opens the guild rank-names UI. Sending
/// GUILD_RANK_NAMES_REQUEST_CLIENT with the player not in a guild
/// (this test's case: fresh-starbase character with
/// <c>m_GuildID=0</c>) is a legal but server-side no-op — the
/// retail server's <c>if (g)</c> guard at PlayerGuild.cpp:685
/// silently ignores it (the user wouldn't even see the UI unless
/// they were in a guild, but the wire frame is reachable via
/// scripted or modded clients). Zero permissiveness added; not
/// loosening any security posture; not fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; GUILD_RANK_NAMES_REQUEST_CLIENT +
/// REQUEST_TIME round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorGuildRankNamesRequestClientTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorGuildRankNamesRequestClientTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task GuildRankNamesRequestClient_OnFreshCharNoGuild_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Guildrn" — lowercase 'u' and 'i' satisfy the
        // AccountManager.cpp:1147 vowel check (case-sensitive
        // a/e/i/o/u/y scan before toupper at line 1153). Wave 39+
        // infra-hygiene item: factor a name-validation helper into
        // SectorHandshake.cs.
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Guildrn", shipName: "GuildrnShip", cts.Token);

        try
        {
            // 0x00D4 GUILD_RANK_NAMES_REQUEST_CLIENT — 6B payload.
            //   [0..4)   int32 gameid    (handler never reads)
            //   [4..6)   short unknown   (handler never reads)
            //
            // The handler casts to the local struct but reads only
            // m_GuildID — the cast fields are not field-accessed.
            // 6B all-zero is the minimum safe wire shape.
            byte[] payload = new byte[6];

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.GuildRankNamesRequestClient.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the
            // guild-rank-names-request handler? Send REQUEST_TIME
            // and assert CLIENT_SET_TIME echoes our sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Tolerate
            // interleaved in-sector frames; cap on frame count so a
            // stalled pipeline doesn't masquerade as the outer-CTS
            // timeout.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.ClientSetTime.Value)
                    continue;

                var span = reply.Payload.Span;
                Assert.Equal(12, span.Length);

                int echoedClientSent = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);
                Assert.Equal(clientTick, echoedClientSent);

                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x00D4 GUILD_RANK_NAMES_REQUEST_CLIENT + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the dispatcher arm at PlayerConnection.cpp:617 got mis-routed, " +
                $"the `if (g)` guard at PlayerGuild.cpp:685 was removed " +
                $"(would SEGV inside GetRankName on null Guild*), " +
                $"the m_GuildID initialisation regressed to a non-zero placeholder, " +
                $"or the SendOpcode header-width fix at PlayerConnection.cpp:127 was reverted.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
