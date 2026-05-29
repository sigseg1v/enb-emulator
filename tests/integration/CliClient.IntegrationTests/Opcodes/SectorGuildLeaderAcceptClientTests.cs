// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 38 survival probe: client sends 0x00C5 GUILD_LEADER_ACCEPT_CLIENT
/// with a decline (accept=0) payload referencing a non-existent guild
/// name; assert the server survives by round-tripping REQUEST_TIME after.
///
/// <para>
/// Wire shape (server/src/PlayerGuild.cpp:737 local struct):
/// <code>
///   struct GuildLeaderAcceptClientPacket {
///       long  gameid;          // 4 bytes (Win32 LP32)
///       short length1;         // 2 bytes
///       char  guildname[length1]; // no NUL on wire
///       char  accept;          // 1 byte
///   };
/// </code>
/// Total wire = 4 + 2 + length + 1 = 7 + length bytes.
/// </para>
///
/// <para>
/// Server handler walk-through (server/src/PlayerGuild.cpp:737).
/// </para>
/// <list type="number">
///   <item>Read raw gameid via <c>*(long *)data</c> -- ignored thereafter.</item>
///   <item>Read length via <c>*(short *)&amp;data[4]</c> -- native u16, LE
///     on x86_64.</item>
///   <item><c>strncpy(guildname, &amp;data[6], 64)</c> into a 64-byte stack
///     buffer. We pass length=4 with name="TEST" followed by accept=0;
///     the accept byte (NUL) terminates the strncpy at offset 4 so no
///     OOB read past our 11-byte payload.</item>
///   <item><c>guildname[length] = 0</c> writes NUL at index 4 in the
///     stack buffer (defensive re-termination).</item>
///   <item>Read accept = data[6+length] = data[10] = 0.</item>
///   <item>accept=0 enters the else-branch: <c>Guild *g =
///     g_PlayerMgr->GuildFromName("TEST", true)</c>
///     (server/src/GuildManager.cpp:10221). On a fresh test server
///     there is no pending guild named "TEST" so g=NULL. The <c>if (g)</c>
///     guard skips both SendMessageToFounders and RemoveGuildFromList.
///     Handler returns; no SendOpcode emitted -- survival via
///     REQUEST_TIME -&gt; CLIENT_SET_TIME echo is the test signal.</item>
/// </list>
///
/// <para>
/// Why accept=0 and not accept=1. The accept=1 branch calls
/// <c>g_PlayerMgr->CheckGuildCreationAccepted(guildname, Name())</c>
/// which iterates the per-name founder list. The dangerous path is
/// the accept=1 arm of the founder-tracking subsystem; we don't
/// exercise it because it's covered by the dedicated guild-creation
/// flow tests (Wave-pending). The decline path is the safest legal
/// shape for a survival probe.
/// </para>
///
/// <para>
/// Why the payload is 11 bytes and not larger. The strncpy reads up
/// to 64 bytes from data+6, stopping at the first NUL. By making
/// accept=0 (also a NUL byte at data[10]), the strncpy terminates
/// exactly at the buffer end -- no OOB read past our 11-byte
/// payload. A larger payload of zeros would have the same effect
/// but the 11-byte minimum is the most-faithful wire shape (matches
/// the on-wire layout the retail Win32 client emits for "decline
/// guild leader invitation").
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at PlayerConnection.cpp:604.</b>
///     The 0x00C5 case sits between 0x00C0 CONFIRMED_ACTION_RESPONSE
///     (line 599) and 0x00C9 GUILD_RECRUIT_ACCEPT_CLIENT (line 608).
///     A swap with 0x00C9 mis-routes to HandleRecruitAcceptClient
///     which deterministically NULL-derefs <c>m_Recruiter</c> on a
///     fresh starbase character (m_Recruiter is uninitialised in the
///     Player class -- see SectorGuildRecruitAcceptClientTests.cs);
///     survival probe catches the SEGV.
///   </item>
///   <item>
///     <b>PlayerGuild.cpp:746 <c>long gameid</c> width regression.</b>
///     The handler does <c>*(long *)data</c> on Linux LP64 = 8-byte
///     read. The buffer is 11 bytes so the read is in-bounds, but if
///     the buffer ever shrinks (e.g. a length-cap regression on the
///     packet receiver) this becomes an OOB read.
///   </item>
///   <item>
///     <b>strncpy OOB regression at PlayerGuild.cpp:749.</b> If the
///     bounded copy is widened past 64 or the buffer cap removed,
///     the read past data[6+length] walks unrelated memory. Our
///     accept=0 NUL coincidence guards that today; a regression that
///     drops the NUL coincidence (e.g. inserts a non-zero accept) AND
///     the size cap would walk OOB.
///   </item>
///   <item>
///     <b>GuildFromName NULL-handling regression at
///     GuildManager.cpp:10221.</b> If a refactor drops the
///     <c>return NULL</c> for the empty-list case, the handler's
///     <c>if (g)</c> guard would not skip and would walk garbage
///     state into SendMessageToFounders. Survival catches the crash.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     PlayerConnection.cpp:127.</b> Corrupts the 0x2016 inner-tuple
///     parser; REQUEST_TIME echo never observed.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode dropping 0x00C5.</b>
///     0x00C5 is not explicitly listed in
///     proxy/ClientToServer_linux_stubs.cpp ProcessSectorServerOpcode
///     switch; falls through to bottom default-forward arm. Regression
///     dropping that arm silents both 0x00C5 dispatch AND the
///     REQUEST_TIME echo.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The 11-byte
/// GuildLeaderAcceptClientPacket is the canonical retail Win32 client
/// wire shape emitted when the player clicks "Decline" on a guild
/// formation invitation. Sending it referencing a non-existent guild
/// name is legal client behaviour (the popup can race against the
/// guild's removal by another player). The server's handler tolerates
/// the missing-guild case via the <c>if (g)</c> guard at
/// PlayerGuild.cpp:760; we exercise that path. No server change, no
/// widened input acceptance, no fabricated reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; GUILD_LEADER_ACCEPT_CLIENT +
/// REQUEST_TIME round-trip sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorGuildLeaderAcceptClientTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorGuildLeaderAcceptClientTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task GuildLeaderAcceptClient_DeclineNonExistentGuild_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Guildor" -- contains 'u', 'i', 'o' for the vowel-check.
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Guildor", shipName: "GuildShip", cts.Token);

        try
        {
            // GuildLeaderAcceptClient canonical 11-byte decline shape:
            //   [0..4)  long  gameid  = 0
            //   [4..6)  short length  = 4 (LE)
            //   [6..10) char  name[4] = "TEST"
            //   [10]    char  accept  = 0 (decline)
            byte[] payload = new byte[11];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(4, 2), 4);
            payload[6] = (byte)'T';
            payload[7] = (byte)'E';
            payload[8] = (byte)'S';
            payload[9] = (byte)'T';
            payload[10] = 0; // accept = 0 (decline)

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.GuildLeaderAcceptClient.Value, payload),
                cts.Token);

            // Survival probe: send REQUEST_TIME, assert CLIENT_SET_TIME
            // echoes our sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));
            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

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
                $"drained {maxFrames} frames after sending 0x00C5 GUILD_LEADER_ACCEPT_CLIENT (decline) " +
                $"+ 0x0044 REQUEST_TIME without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely HandleGuildLeaderAcceptClient SEGV'd inside strncpy on an OOB regression, " +
                $"GuildFromName returned a wild pointer the if-guard didn't catch, " +
                $"the dispatcher case at PlayerConnection.cpp:604 got mis-routed to HandleRecruitAcceptClient " +
                $"(which deterministically NULL-derefs m_Recruiter on a fresh char), " +
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
