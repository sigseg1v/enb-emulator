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
/// Wave 41 post-handshake survival round-trip: client sends 0x0087
/// MISSION_DISMISSAL with an out-of-range <c>MissionID</c> (999, well
/// beyond the per-player 12-slot mission array), then verifies the
/// connection survives via 0x0044 REQUEST_TIME.
///
/// <para>
/// Wire layout (from the canonical packet struct at
/// <c>common/include/net7/PacketStructures.h:1001-1005</c>):
/// </para>
/// <code>
///   struct MissionDismissal
///   {
///       int32_t PlayerID;
///       int32_t MissionID;
///   };
/// </code>
/// <para>
/// 8-byte payload — both fields are network byte order. Handler reads
/// both via <c>ntohl</c> and only consumes <c>MissionID</c> — the
/// <c>PlayerID</c> field is read into a local but never referenced (a
/// vestige from a presumed earlier multi-player dismissal flow; the
/// retail handler trusts the per-connection player identity, not the
/// payload). The CLAUDE.md server-integrity floor is preserved: the
/// real server's handler also ignores the payload <c>PlayerID</c>, so
/// not asserting on it is fidelity, not permissiveness.
/// </para>
///
/// <para>
/// Server handler walk-through.
/// </para>
/// <list type="number">
///   <item>Dispatcher case at
///     <c>server/src/PlayerConnection.cpp:555</c> calls
///     <c>HandleMissionDismissal(data)</c>.</item>
///   <item><c>HandleMissionDismissal</c> at
///     <c>server/src/PlayerConnection.cpp:11000-11007</c>:
///     casts <c>data</c> to <c>MissionDismissal *</c>, reads
///     <c>MissionID</c> and <c>PlayerID</c> via <c>ntohl</c>, then
///     calls <c>MissionDismiss(MissionID, false)</c>. The boolean
///     <c>false</c> distinguishes dismissal from forfeit; the
///     forfeit-only path checks <c>m-&gt;GetIsForfeitable()</c>
///     which is irrelevant here.</item>
///   <item><c>MissionDismiss</c> at
///     <c>server/src/PlayerMissions.cpp:1616-1633</c>:
///     <c>if (mission_slot &gt;= 0 &amp;&amp; mission_slot &lt; 12)</c>
///     short-circuits because <c>MissionID=999</c> fails the
///     <c>&lt; 12</c> bound. Function returns immediately.</item>
/// </list>
/// <para>
/// Zero state mutation. Zero <c>RemoveMission</c>. Zero
/// <c>SendVaMessageC</c>. Zero SendOpcode. Zero observer fan-out.
/// Same favourable post-emit shape as Wave 27 / 29 / 30 / 36 / 37 /
/// 38 / 39 / 40 — the handler dispatches, reads payload fields, then
/// the guard at the call site rejects the out-of-range input.
/// </para>
///
/// <para>
/// Why this wave target. Per Wave 36 triage: 0x0087 MISSION_DISMISSAL
/// shares the <c>MissionDismissal</c> struct with 0x0086
/// MISSION_FORFEIT but has a SHALLOWER call chain — the dismissal
/// path goes directly to <c>MissionDismiss</c> with
/// <c>forfeit_pressed=false</c>, which means the
/// <c>GetIsForfeitable()</c> branch (which could
/// <c>SendVaMessageC</c> a "non-forfeitable" error string for in-
/// range slots with non-forfeitable missions) is unreachable. The
/// out-of-range filter at <c>PlayerMissions.cpp:1618</c> is the same
/// guard both handlers share, but only 0x0087 has the no-side-
/// effect default arm shape.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at server/src/PlayerConnection.cpp:555.</b>
///     The 0x0087 case label sits between 0x0086 MISSION_FORFEIT
///     (line 551) and 0x0088 PETITION_STUCK (line 559). A copy-paste
///     swap with HandleMissionForfeit (line 552) would call
///     <c>MissionDismiss(999, true)</c> — same out-of-range guard
///     short-circuits so the test would still pass, but a future
///     in-range-MissionID wave would surface the swap. A swap with
///     HandlePetitionStuck (line 560) would call
///     <c>SavePetition(data, bytes)</c> on our 8B payload —
///     SavePetition writes to the persistence layer with the bytes
///     as variable-length null-terminated strings; reading past the
///     8B buffer is UB and the connection might survive or might
///     not. The survival probe catches the survival case.
///   </item>
///   <item>
///     <b>Removal of the <c>mission_slot &lt; 12</c> guard at
///     <c>PlayerMissions.cpp:1618</c>.</b> Would call
///     <c>&amp;m_PlayerIndex.Missions.Mission[999]</c> — an OOB
///     read into adjacent <c>Player</c> instance memory. The
///     <c>m</c> pointer would be non-null (just a pointer
///     arithmetic result) so the <c>if (m &amp;&amp; ...)</c>
///     branch would proceed to <c>RemoveMission(999)</c> — which
///     would index the same array OOB for the actual removal logic.
///     SEGV likely; if not, silent state corruption.
///   </item>
///   <item>
///     <b>Removal of the <c>mission_slot &gt;= 0</c> guard at
///     <c>PlayerMissions.cpp:1618</c>.</b> A negative MissionID
///     (e.g. 0xFFFFFFFF in payload → -1 after ntohl-into-long sign
///     extension on Linux x86_64) would index into memory BEFORE
///     the Missions array, also OOB. Not exercised by this wave
///     (we send +999, not a negative value) but the symmetric
///     guard is documented.
///   </item>
///   <item>
///     <b>HandleMissionDismissal silent regression to <c>true</c>
///     forfeit flag.</b> If the bool literal at
///     <c>PlayerConnection.cpp:11006</c> flips from <c>false</c> to
///     <c>true</c>, <c>MissionDismiss(MissionID, true)</c> would
///     bypass the no-side-effect dismissal path and exercise the
///     forfeitable-check arm — for out-of-range MissionID the guard
///     still filters; for an in-range slot with a non-forfeitable
///     mission a <c>SendVaMessageC</c> would emit a "non-
///     forfeitable" string visible on the wire as a 0x001D
///     MESSAGE_STRING frame. The survival probe drains past it but
///     a future tighter assertion would catch it.
///   </item>
///   <item>
///     <b>ntohl read-width regression at
///     <c>PlayerConnection.cpp:11003-11004</c>.</b> Both reads
///     currently use <c>ntohl(dismiss-&gt;MissionID)</c> and
///     <c>ntohl(dismiss-&gt;PlayerID)</c>, which expand the wire-
///     effective 4B big-endian fields to <c>uint32_t</c>, then
///     assign to <c>long</c>. On Linux x86_64 this is benign for
///     non-negative values. A regression replacing <c>ntohl</c>
///     with <c>ntohs</c> would read only 2B of each 4B field and
///     swap them; MissionID=999 in network byte order is
///     <c>00 00 03 E7</c>, so <c>ntohs</c> of the first 2B reads
///     0 → in-range — would call <c>MissionDismiss(0, false)</c>
///     and exercise the in-range arm. For a fresh-starbase
///     character with no missions, <c>m_PlayerIndex.Missions.
///     Mission[0]</c> is the default-constructed
///     <c>AuxMission</c> — <c>RemoveMission(0)</c> would still run
///     but on an empty slot. Subtle but a regression in width
///     could move us from a no-mutation path to a no-op-mutation
///     path that future assertions might pin against.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Phase K
///     <c>sizeof(int32_t)</c> opcode-header fix keeps the per-
///     client UDP queue header at 4B; a revert corrupts the 0x2016
///     inner-tuple parser → REQUEST_TIME path silent.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode regression.</b>
///     0x0087 is NOT explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>'s
///     <c>ProcessSectorServerOpcode</c> switch, so it falls
///     through to the bottom-of-switch ForwardClientOpcode default
///     arm; a regression dropping that default would mean the
///     server never sees the dismissal frame (test still passes
///     via REQUEST_TIME's explicit proxy arm but the diagnostic
///     loss is documented).
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard
///     tightening at <c>UDPProxyToClient_linux.cpp:568</c>.</b>
///     Currently passes 0x0034 CLIENT_SET_TIME because 0x0034
///     &lt; 0x0FFF.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x0087 MISSION_DISMISSAL
/// is what the retail Win32 client emits when the user clicks the
/// "dismiss" button on a completed/expired mission panel. Sending
/// it with an out-of-range MissionID (e.g. via a UI race where the
/// player dismisses a mission that the server has already removed
/// — the slot index would refer to a stale UI slot beyond the
/// 12-element bound) is a legal but server-side no-op — the retail
/// server's <c>mission_slot &lt; 12</c> guard silently filters it.
/// Zero permissiveness added; not loosening any security posture;
/// not fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; MISSION_DISMISSAL +
/// REQUEST_TIME round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorMissionDismissalTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorMissionDismissalTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task MissionDismissal_OutOfRangeMissionId_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Misdis" — lowercase 'i' for the
        // AccountManager.cpp:1147 vowel-check footgun (case-sensitive
        // a/e/i/o/u/y BEFORE toupper at line 1153).
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Misdis", shipName: "MisdisShip", cts.Token);

        try
        {
            // 0x0087 MISSION_DISMISSAL — 8B payload:
            //   int32_t PlayerID    (network byte order, ignored by handler)
            //   int32_t MissionID   (network byte order, sent as 999 to fail
            //                        the `< 12` guard in MissionDismiss)
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), 0);   // PlayerID
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), 999); // MissionID — OOR

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.MissionDismissal.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the
            // mission-dismissal handler? Send REQUEST_TIME and
            // assert CLIENT_SET_TIME echoes our sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Tolerate
            // interleaved in-sector frames (positional updates from
            // observers, etc.).
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
                $"drained {maxFrames} frames after sending 0x0087 MISSION_DISMISSAL + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the dispatcher arm at PlayerConnection.cpp:555 got mis-routed " +
                $"(swap with HandlePetitionStuck → SavePetition over-reads our 8B payload), " +
                $"the `< 12` guard at PlayerMissions.cpp:1618 was removed " +
                $"(OOB read into adjacent Player instance memory), " +
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
