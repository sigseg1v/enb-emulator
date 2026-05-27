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
/// Wave 40 post-handshake survival round-trip: client sends 0x00CD
/// GUILD_SIMPLE_CLIENT_SECTOR with a <c>type=0</c> payload that
/// falls into the handler's default LogMessage arm, then verifies
/// the connection survives via 0x0044 REQUEST_TIME.
///
/// <para>
/// Wire layout (from the local struct at
/// <c>server/src/PlayerGuild.cpp:703-709</c>):
/// </para>
/// <code>
///   struct GuildSimpleClientSectorPacket
///   {
///       long  type;            // sizeof(long) bug — wire is int32
///       long  gameid;          // sizeof(long) bug — wire is int32
///       short length;
///       char  optionalparam[16];
///   };
/// </code>
/// <para>
/// The two <c>long</c> fields are the Phase K Wave 11 bug class on
/// Linux x86_64 (sizeof(long)==8 over-reads the wire-effective 4B
/// int32 fields). But for an all-zero payload the over-read pulls
/// 8 bytes of zeros which still resolves to <c>type=0</c>. The
/// wire-effective size is 26B (4 + 4 + 2 + 16); we send 26B of
/// zeros which leaves room for the handler's full struct cast
/// regardless of how the read widths resolve.
/// </para>
///
/// <para>
/// Server handler walk-through.
/// </para>
/// <list type="number">
///   <item>Dispatcher case at <c>server/src/PlayerConnection.cpp:613</c>
///     calls <c>HandleGuildSimpleClientSector(data)</c>.</item>
///   <item>Handler casts <c>data</c> to
///     <c>GuildSimpleClientSectorPacket *</c>.</item>
///   <item><c>switch (request->type)</c> — for all-zero payload
///     <c>type=0</c>.</item>
///   <item>The case labels are <c>GUILD_PROMOTE_CONFIRM+1=2</c>,
///     <c>GUILD_DEMOTE_CONFIRM+1=6</c>,
///     <c>GUILD_REMOVE_CONFIRM+1=8</c>,
///     <c>GUILD_LEAVE_CONFIRM+1=10</c>,
///     <c>GUILD_DISBAND_CONFIRM+1=12</c>,
///     <c>GUILD_GM_DISBAND_CONFIRM+1=14</c> (per
///     <c>server/src/Guilds.h:140-146</c>). <c>type=0</c> doesn't
///     match any.</item>
///   <item>Falls to default arm:
///     <c>LogMessage("Unknown guild confirmation type %d\n",
///     request->type)</c> — safe no-op. Function returns.</item>
/// </list>
/// <para>
/// Zero state mutation. Zero SendOpcode. Zero observer fan-out.
/// The <c>HandlePromoteMember</c>, <c>HandleDemoteMember</c>,
/// <c>HandleRemoveMember</c>, <c>HandleLeaveGuild</c>,
/// <c>HandleDisbandGuild</c>, <c>HandleGMDisbandGuild</c> mutators
/// all sit BEHIND the matched-case arms — none run for type=0.
/// Same favourable post-emit shape as Wave 27 (INVENTORY_MOVE
/// default arm — also a switch-fall-through to no-op), Wave 39
/// (GUILD_RANK_NAMES_REQUEST_CLIENT — also a no-payload-field-
/// effect short-circuit).
/// </para>
///
/// <para>
/// Why this wave target. Per Wave 39's revised triage of the four
/// guild handlers (after the disambiguation that all four ARE
/// defined in PlayerGuild.cpp, contrary to the wrong claim in
/// 99-decisions-log.md line 4449-4453):
/// </para>
/// <list type="bullet">
///   <item>0x00C5 GUILD_LEADER_ACCEPT_CLIENT — UNSAFE (sizeof(long)
///     over-read + unbounded strncpy + OOB read at
///     PlayerGuild.cpp:746-751).</item>
///   <item>0x00C9 GUILD_RECRUIT_ACCEPT_CLIENT — UNSAFE
///     (<c>m_Recruiter->HandleRecruitMember2</c> at
///     PlayerGuild.cpp:777 with null <c>m_Recruiter</c> on fresh
///     char → SEGV).</item>
///   <item>0x00CD GUILD_SIMPLE_CLIENT_SECTOR — SAFE (this wave;
///     default LogMessage arm on type=0).</item>
///   <item>0x00D4 GUILD_RANK_NAMES_REQUEST_CLIENT — SAFE (Wave 39;
///     <c>if (g)</c> short-circuit on null guild).</item>
/// </list>
/// <para>
/// 0x00CD is the second-cleanest safe arm in the guild family —
/// it does read <c>request->type</c> (so unlike Wave 39 the
/// handler IS exercising a payload field) but the switch
/// fall-through to a logger-only default arm gives the same
/// favourable zero-mutation shape.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at server/src/PlayerConnection.cpp:613.</b>
///     The 0x00CD case label sits between 0x00C9
///     GUILD_RECRUIT_ACCEPT_CLIENT (line 608) and 0x00D4
///     GUILD_RANK_NAMES_REQUEST_CLIENT (line 616). A swap with
///     HandleRecruitAcceptClient would deref a null
///     <c>m_Recruiter</c> on a fresh char → SEGV. A swap with
///     HandleGuildRankNamesRequestClient would walk that handler's
///     <c>if (g)</c> short-circuit on m_GuildID=0 and survive but
///     via the wrong code arm.
///   </item>
///   <item>
///     <b>Switch case-label drift in Guilds.h:140-146.</b> If any
///     of the GUILD_*_CONFIRM constants changes to 0 or wraps to
///     0 via signed-int issues, the type=0 payload would hit a
///     real mutator. <c>HandleLeaveGuild(true)</c> does
///     <c>m_GuildID = 0</c> + LogChange — destructive even for a
///     fresh char (would set already-0 to 0; visible side effect
///     is the log entry).
///   </item>
///   <item>
///     <b><c>request->type</c> read-width regression.</b> If the
///     sizeof(long) bug ever gets MIS-fixed (e.g. cast to
///     <c>int16_t</c> instead of <c>int32_t</c>) the switch
///     selector would read different bytes than expected. Current
///     behaviour is robust to either width-fix for all-zero
///     payload (all-zero reads to 0 at any width).
///   </item>
///   <item>
///     <b>Default arm <c>LogMessage</c> removal.</b> Would do
///     nothing on type=0 (test still passes) but a future tighter
///     assertion (log-message-emitted check) would catch it.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at PlayerConnection.cpp:127.</b>
///     Phase K sizeof(int32_t) opcode-header fix keeps the
///     per-client UDP queue header at 4B; a revert corrupts the
///     0x2016 inner-tuple parser → REQUEST_TIME path silent.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode regression.</b>
///     0x00CD is NOT explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>'s
///     <c>ProcessSectorServerOpcode</c> switch, so it falls
///     through to the bottom-of-switch ForwardClientOpcode
///     default arm.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard
///     tightening at UDPProxyToClient_linux.cpp:568.</b>
///     Currently passes 0x0034 CLIENT_SET_TIME because 0x0034
///     &lt; 0x0FFF.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x00CD
/// GUILD_SIMPLE_CLIENT_SECTOR is what the retail Win32 client
/// emits when the user clicks a confirm/cancel button in the
/// guild-management UI (promote/demote/remove/leave/disband/
/// GM-disband). Sending GUILD_SIMPLE_CLIENT_SECTOR with
/// <c>type=0</c> (i.e. invalid/zero confirmation type) is a legal
/// but server-side no-op — the retail server's default arm in
/// the switch silently logs the unknown type and returns. Zero
/// permissiveness added; not loosening any security posture; not
/// fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; GUILD_SIMPLE_CLIENT_SECTOR +
/// REQUEST_TIME round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorGuildSimpleClientSectorTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorGuildSimpleClientSectorTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task GuildSimpleClientSector_OnZeroTypePayload_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        // cli_test37 — Pool[35]. Dedicated to this wave so its
        // Create/Delete cycle doesn't collide with Pool slots owned
        // by earlier waves. seed.sql carries the matching 9_000_037
        // row.
        var account = TestAccounts.Pool[35];
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Gusimple" — lowercase 'u', 'i', 'e' for the
        // AccountManager.cpp:1147 vowel-check footgun.
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Gusimple", shipName: "GusimpleShip", cts.Token);

        try
        {
            // 0x00CD GUILD_SIMPLE_CLIENT_SECTOR — 26B payload
            // (wire-effective: 4B int32 type + 4B int32 gameid + 2B
            // short length + 16B optionalparam). The local struct
            // at PlayerGuild.cpp:703-709 declares type/gameid as
            // `long` (sizeof(long) bug class) but for all-zero
            // payload the 8B over-read still resolves type=0.
            byte[] payload = new byte[26];

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.GuildSimpleClientSector.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the
            // guild-simple-client-sector handler? Send REQUEST_TIME
            // and assert CLIENT_SET_TIME echoes our sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Tolerate
            // interleaved in-sector frames.
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
                $"drained {maxFrames} frames after sending 0x00CD GUILD_SIMPLE_CLIENT_SECTOR + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the dispatcher arm at PlayerConnection.cpp:613 got mis-routed " +
                $"(swap with HandleRecruitAcceptClient → SEGV on null m_Recruiter), " +
                $"a GUILD_*_CONFIRM constant in Guilds.h wrapped to 0 (would route type=0 into a real mutator), " +
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
