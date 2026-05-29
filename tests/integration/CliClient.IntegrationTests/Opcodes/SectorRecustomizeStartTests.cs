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
/// Wave 56 direct-reply round-trip (+2 ratchet): client establishes a
/// station-sector handshake at Luna Station (sector 10151), then drives
/// the two trivial "open the recustomize terminal" actions of
/// 0x004E STARBASE_REQUEST and asserts each emits the corresponding
/// "start" frame back:
/// <list type="bullet">
///   <item>
///     <c>Action=10</c> → server emits 0x0083 RECUSTOMIZE_AVATAR_START
///     carrying the 14-cost table + playerid.
///   </item>
///   <item>
///     <c>Action=11</c> → server emits 0x0081 RECUSTOMIZE_SHIP_START
///     carrying the player's current ShipData snapshot, the 12-cost
///     table, playerid, and 4 unknown int32s.
///   </item>
/// </list>
///
/// <para>
/// Why both in one test: both actions share the same input opcode
/// (0x004E STARBASE_REQUEST), the same 9B packed-struct payload shape,
/// and the same handler entry point (<c>Player::HandleStarbaseRequest</c>
/// at <c>server/src/PlayerConnection.cpp:9846</c>). The case-10 and
/// case-11 arms are adjacent (<c>PlayerConnection.cpp:10020-10039</c>),
/// build their reply struct inline, call <c>SendOpcode</c>, and break —
/// no shared state, no cross-action side effects beyond the
/// <c>m_TradeWindow = false</c> reset at <c>PlayerConnection.cpp:9875</c>
/// (which a fresh-login avatar already has at false). One handshake
/// amortises the 25s live-stack cost across two coverage rows.
/// </para>
///
/// <para>
/// Wire layout of the inbound 0x004E (mirror of
/// <c>common/include/net7/PacketStructures.h:812-817</c>):
/// <code>
///   [0..4) int32 PlayerID    — case 10 ignores; case 11 ignores
///   [4..8) int32 StarbaseID  — case 10 ignores; case 11 ignores
///   [8..9)   char Action     — 10 or 11
/// </code>
/// 9 bytes total, <c>ATTRIB_PACKED</c>. Identical to the Wave 55
/// payload shape (only Action byte differs).
/// </para>
///
/// <para>
/// Server emit sites (<c>server/src/PlayerConnection.cpp:10020-10039</c>):
/// <code>
///   case 10: // Customize avatar
///       {
///           struct RecustomizeAvatarStart ras;
///           for (int i=0;i &lt; 14;i++)
///               ras.costs[i] = g_CustomiseAvatarCosts[i];
///           ras.playerid = htonl(pkt-&gt;PlayerID);
///           SendOpcode(ENB_OPCODE_0083_RECUSTOMIZE_AVATAR_START,
///                      (unsigned char *)&amp;ras, sizeof(ras));
///       }
///       break;
///   case 11: // Customize starship
///       {
///           struct RecustomizeShipStart rss;
///           rss.ship = m_Database.ship_data;
///           for (int i=0;i &lt; 12;i++)
///               rss.costs[i] = g_CustomiseShipCosts[i];
///           rss.playerid = htonl(pkt-&gt;PlayerID);
///           rss.unknown[0] = rss.unknown[1] = rss.unknown[2] = rss.unknown[3] = 0;
///           SendOpcode(ENB_OPCODE_0081_RECUSTOMIZE_SHIP_START,
///                      (unsigned char *)&amp;rss, sizeof(rss));
///       }
///       break;
/// </code>
/// Both arms are unconditional — no precondition checks beyond
/// "<c>HandleStarbaseRequest</c> was reached", which any post-handshake
/// in-station avatar satisfies.
/// </para>
///
/// <para>
/// Expected reply sizes (all <c>ATTRIB_PACKED</c>):
/// <list type="bullet">
///   <item>
///     <c>RecustomizeAvatarStart</c> = 14×int32 costs + int32 playerid
///     = <b>60 bytes</b>
///     (<c>common/include/net7/PacketStructures.h:1087-1091</c>).
///   </item>
///   <item>
///     <c>RecustomizeShipStart</c> = 194B ShipData + 12×int32 costs +
///     int32 playerid + 4×int32 unknown = <b>262 bytes</b>
///     (<c>common/include/net7/PacketStructures.h:1093-1099</c>;
///     ShipData width at <c>PacketStructures.h:196-220</c>).
///   </item>
/// </list>
/// Both are byte-exact (no length-prefixed strings), so assert
/// <c>Payload.Length == expected</c> — tighter than Wave 55's
/// <c>&gt;= 64</c> floor.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>HandleStarbaseRequest case-10/case-11 branch deletion at
///     <c>PlayerConnection.cpp:10020-10039</c>.</b> If either arm
///     vanishes (or short-circuits before <c>SendOpcode</c>), the
///     corresponding reply never arrives — drain times out.
///   </item>
///   <item>
///     <b>SendOpcode opcode-id flip at the case-10 or case-11 emit
///     site.</b> A typo from <c>0x0083</c> to a neighbouring define
///     (e.g. <c>0x0084</c> RECUSTOMIZE_AVATAR_DONE which is a
///     client-&gt;server opcode the server should not emit) still emits
///     a frame but under the wrong opcode — drain times out.
///   </item>
///   <item>
///     <b>RecustomizeAvatarStart / RecustomizeShipStart struct-width
///     regression in <c>PacketStructures.h:1087-1099</c>.</b> If
///     <c>ATTRIB_PACKED</c> is lost, the structs grow with implicit
///     compiler padding (e.g. on x86_64 a 4-byte member after a 3-byte
///     gap inserts the gap), changing <c>sizeof(ras)</c> and
///     <c>sizeof(rss)</c> — the byte-exact length assertion catches
///     this even though the opcode arrival itself would still pass.
///     The whole project depends on <c>ATTRIB_PACKED</c> matching the
///     Win32 wire layout the retail client expects.
///   </item>
///   <item>
///     <b>ShipData struct-width regression in
///     <c>PacketStructures.h:196-220</c>.</b> ShipData was migrated
///     long-&gt;int32_t in Phase K (5 ints + 26 + 12 + 8×17 = 194B
///     Win32-packed). A revert to <c>long</c> on Linux makes it 214B,
///     pushing <c>sizeof(RecustomizeShipStart)</c> from 262 to 282 —
///     the byte-exact length assertion catches it.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every inner
///     opcode in the 0x2016 PACKET_SEQUENCE parser; neither 0x0081 nor
///     0x0083 would appear under their correct labels.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes both (0x0081 and 0x0083 are both &lt; 0x0FFF). A
///     regression to a tighter upper bound would silently drop them.
///   </item>
///   <item>
///     <b>STARBASE_REQUEST dispatch-table entry deletion at
///     <c>PlayerConnection.cpp:487-489</c>.</b> If the case
///     <c>ENB_OPCODE_004E_STARBASE_REQUEST</c> in the main dispatch
///     table is removed or renamed, the server silently swallows
///     both requests.
///   </item>
///   <item>
///     <b>m_Database.ship_data initialisation regression.</b> The
///     case-11 arm at <c>PlayerConnection.cpp:10032</c> does
///     <c>rss.ship = m_Database.ship_data</c> — if the ship_data
///     blob is left zeroed by a regression in
///     <c>PlayerSaves::ReInitializeSavedData</c>, the 0x0081 reply
///     still arrives at the correct length (262B), so the inner
///     content is not asserted here; a future typed-codec wave for
///     0x0081 would catch the content regression.
///   </item>
///   <item>
///     <b>g_CustomiseShipCosts / g_CustomiseAvatarCosts global table
///     regression.</b> If the cost tables are NULL-pointered or the
///     enum sizes drift past the hard-coded 14/12 loop bounds, the
///     loops at <c>PlayerConnection.cpp:10023</c> and
///     <c>PlayerConnection.cpp:10033</c> would read OOB. The length
///     assertion alone doesn't catch this; relying on the same-shape
///     emit not crashing the server thread.
///   </item>
/// </list>
///
/// <para>
/// CLAUDE.md server-integrity. Wave 56 sends two input shapes the
/// real retail client emitted (STARBASE_REQUEST with Action=10 is the
/// "Customize Avatar" terminal-click and Action=11 is the "Customize
/// Ship" terminal-click) and asserts two server-originated replies
/// (0x0083 RECUSTOMIZE_AVATAR_START, 0x0081 RECUSTOMIZE_SHIP_START)
/// the real retail server has always produced on those inputs. No
/// server change, no widened input acceptance, no loosened gating,
/// no debug-only opcode, no security-posture relaxation. The 9B
/// payload shape matches the canonical <c>StarbaseRequest</c> packed
/// struct exactly.
/// </para>
///
/// <para>
/// Cleanup. Both case-10 and case-11 arms are pure emits — no
/// player-state mutation, no <c>DropPlayerFrom*</c>, no <c>m_Gating
/// = true</c> like case-1 does. The player remains an active,
/// in-station avatar after both replies arrive. Cleanup is the
/// standard 0x00B9 LOGOFF_REQUEST → 0x00BA LOGOFF_CONFIRMATION
/// round-trip + GlobalDeleteCharacter on the global TCP, identical
/// to Wave 55.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~22s on a cold stack; each
/// STARBASE_REQUEST + reply round-trip is sub-second; LOGOFF
/// round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorRecustomizeStartTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    private const int ExpectedAvatarStartSize = 60;   // 14*int32 + int32
    private const int ExpectedShipStartSize   = 262;  // 194 ShipData + 12*int32 + int32 + 4*int32

    public SectorRecustomizeStartTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task StarbaseRecustomizeActions_ReceivesShipAndAvatarStartFrames()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Recust", shipName: "RecustShip", cts.Token);

        try
        {
            // --- Action=10 → 0x0083 RECUSTOMIZE_AVATAR_START ---
            await SendStarbaseRequestAsync(session, action: 10, cts.Token);
            var avatarStartReply = await DrainUntilOpcodeAsync(
                session, OpcodeId.Known.RecustomizeAvatarStart.Value,
                stimulusName: "STARBASE_REQUEST Action=10",
                expectedReplyOpcode: 0x0083, cts.Token);
            Assert.Equal(ExpectedAvatarStartSize, avatarStartReply.Payload.Length);

            // --- Action=11 → 0x0081 RECUSTOMIZE_SHIP_START ---
            await SendStarbaseRequestAsync(session, action: 11, cts.Token);
            var shipStartReply = await DrainUntilOpcodeAsync(
                session, OpcodeId.Known.RecustomizeShipStart.Value,
                stimulusName: "STARBASE_REQUEST Action=11",
                expectedReplyOpcode: 0x0081, cts.Token);
            Assert.Equal(ExpectedShipStartSize, shipStartReply.Payload.Length);
        }
        finally
        {
            try
            {
                using var logoffCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                byte[] logoffPayload = new byte[8];
                await session.Sector.SendAsync(
                    Packet.ForOpcode(OpcodeId.Known.LogoffRequest.Value, logoffPayload),
                    logoffCts.Token);
                await SectorHandshake.DrainUntilOpcode(
                    session.Sector, OpcodeId.Known.LogoffConfirmation.Value, logoffCts.Token);
            }
            catch { /* best-effort logoff */ }

            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    private static async Task SendStarbaseRequestAsync(
        SectorHandshake.Session session, byte action, CancellationToken ct)
    {
        // Canonical 9B packed StarbaseRequest payload. PlayerID and
        // StarbaseID are not consulted on the case=10/11 arms.
        byte[] payload = new byte[9];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);
        payload[8] = action;

        await session.Sector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.StarbaseRequest.Value, payload),
            ct);
    }

    private static async Task<Packet> DrainUntilOpcodeAsync(
        SectorHandshake.Session session, ushort expectedOpcode,
        string stimulusName, ushort expectedReplyOpcode, CancellationToken ct)
    {
        const int maxFrames = 400;
        int seen = 0;
        while (seen++ < maxFrames)
        {
            var reply = await session.Sector.ReceiveAsync(ct);
            Assert.NotNull(reply);
            if (reply!.Header.Opcode == expectedOpcode)
                return reply;
        }

        throw new Xunit.Sdk.XunitException(
            $"drained {maxFrames} frames after sending 0x004E {stimulusName} " +
            $"without seeing 0x{expectedReplyOpcode:X4} reply. " +
            $"Likely the case-{(expectedReplyOpcode == 0x0083 ? "10" : "11")} branch in " +
            $"HandleStarbaseRequest (PlayerConnection.cpp:10020-10039) was deleted/short-circuited, " +
            $"the SendOpcode opcode-id at that emit site was flipped, the proxy's " +
            $"SendClientPacketSequence guard at UDPProxyToClient_linux.cpp:568 was tightened past " +
            $"0x{expectedReplyOpcode:X4}, or the STARBASE_REQUEST case at PlayerConnection.cpp:487 " +
            $"was removed/renamed in the main dispatch table.");
    }
}
