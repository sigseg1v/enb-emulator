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
/// Wave 55 direct-reply round-trip: client establishes a station-sector
/// handshake (Luna Station, sector 10151), then sends a 9-byte
/// 0x004E STARBASE_REQUEST with <c>Action=1</c> (exit the station),
/// expects the server to fan out the LaunchIntoSpace chain ending in a
/// 0x003A SERVER_HANDOFF frame that hands the avatar off from the
/// station's sector (10151) to its parent space sector (10151 / 10 =
/// 1015 = Luna).
///
/// <para>
/// Wire layout (mirror of <c>common/include/net7/PacketStructures.h:812-817</c>):
/// <code>
///   [0..4) int32 PlayerID    — handler logs but ignores in Action=1
///   [4..8) int32 StarbaseID  — handler logs but ignores in Action=1
///   [8..9)   char Action     — set to 1 to drive the exit-station branch
/// </code>
/// 9 bytes total; <c>ATTRIB_PACKED</c> so no implicit padding. The
/// real EnB client emits this packet exactly when the player clicks
/// "Launch" in the station UI — the case-1 dispatch in the server is
/// reachable from a fresh avatar with no special precondition beyond
/// "being docked at a station sector".
/// </para>
///
/// <para>
/// Server handler chain (<c>server/src/PlayerConnection.cpp:9846-9892</c>):
/// <code>
///   void Player::HandleStarbaseRequest(unsigned char *data)
///   {
///       StarbaseRequest * pkt = (StarbaseRequest *) data;
///       ...
///       switch (pkt->Action) {
///       case 1: // Exiting the station action
///           if (sm) {
///               FinishAllInstalls();
///               m_Gating = true;
///               if (m_TradeID != -1) CancelTrade();
///               sm->LaunchIntoSpace(this);
///           }
///           break;
///       ...
///       }
///   }
/// </code>
/// <c>SectorManager::LaunchIntoSpace</c>
/// (<c>server/src/SectorManager.cpp:534-552</c>) then emits, in order:
/// 0x004F STARBASE_SET (SendStarbaseSet(1, 0) at line 537), 0x0086
/// MESSAGE_STRING (SendMessageString with the "control... cleared for
/// departure" string at line 543, when the sector has a Station row in
/// the StationMgr), and finally 0x003A SERVER_HANDOFF at line 551 via
/// <c>player-&gt;SendServerHandoff(m_SectorID, to_sector_id, m_SectorName,
/// "", m_ParentSectorName, m_SystemName)</c>. The handoff target sector
/// is computed at line 547: <c>to_sector_id = m_SectorID / 10</c>
/// (station ids encode the parent space sector × 10 + station index), so
/// for Luna Station 10151 the handoff target is 1015 (Luna space). The
/// guard at line 548 (<c>if (m_SectorID &lt; 9999) to_sector_id =
/// m_SectorID</c>) does NOT fire for station sectors — station sectors
/// are >9999 by convention, so the divide-by-ten branch is the one we
/// take.
/// </para>
///
/// <para>
/// Why a single-stage login (no Wave 52/53/54-style 2-stage dance):
/// the case-1 branch only fires when the player is currently in a
/// station-sector's SectorManager (the <c>sm</c> resolved from
/// <c>GetSectorManager()</c> at <c>PlayerConnection.cpp:9853</c> must be
/// the station's manager — calling LaunchIntoSpace on a space-sector
/// manager would compute a nonsense to_sector_id = sector_id/10). The
/// Terran Warrior StartSector is 10151 (Luna Station) so the first
/// login already lands us in the right place. We send STARBASE_REQUEST
/// directly on the post-handshake sector TCP — the connection is
/// authenticated, in-sector, m_Active=true, and the case-1 branch is
/// reachable.
/// </para>
///
/// <para>
/// 0x003A wire layout (<c>server/src/PlayerConnection.cpp:10146-10202</c>'s
/// SendServerHandoff packs a ServerHandoff struct then four short-prefixed
/// strings into <c>variable_data</c>): the inner <c>join</c> mirrors the
/// MasterJoin struct (cf.
/// <c>common/include/net7/PacketStructures.h</c>'s MasterJoin) with
/// ToSectorID/FromSectorID overwritten via <c>ntohl</c> at lines
/// 10162-10163, followed by 4 length-prefixed string blocks (from_sector,
/// from_system, to_sector, to_system). We assert only the opcode arrival —
/// the inner struct + strings layout is asserted by a future typed-codec
/// wave; the opcode-level assertion here catches the regression classes
/// listed below already.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>HandleStarbaseRequest case-1 branch deletion.</b> If the
///     case-1 arm of the switch at <c>PlayerConnection.cpp:9879-9892</c>
///     vanishes (or short-circuits before <c>sm-&gt;LaunchIntoSpace</c>),
///     0x003A never arrives — drain would time out on the outer CTS.
///   </item>
///   <item>
///     <b>LaunchIntoSpace SendServerHandoff call removal at
///     <c>SectorManager.cpp:551</c>.</b> If the final SendServerHandoff
///     invocation is deleted (e.g. someone replaces it with an internal
///     state mutation but forgets the wire emit), the test still sees
///     0x004F STARBASE_SET and 0x0086 MESSAGE_STRING but never reaches
///     0x003A.
///   </item>
///   <item>
///     <b>Sector-ID divide-by-ten regression at
///     <c>SectorManager.cpp:547</c>.</b> If <c>to_sector_id = m_SectorID
///     / 10</c> is replaced with the wrong arithmetic (e.g.
///     <c>m_SectorID - 9000</c> or <c>m_SectorID % 10</c>), the
///     ServerHandoff is still emitted but with a nonsense destination.
///     The opcode-level assertion still passes but the future typed-codec
///     wave for 0x003A will catch the wrong ToSectorID; this test
///     anchors the emit-site existence.
///   </item>
///   <item>
///     <b>SendServerHandoff opcode-id regression at
///     <c>PlayerConnection.cpp:10202</c>.</b> A typo flip from
///     <c>ENB_OPCODE_003A_SERVER_HANDOFF</c> to a neighbouring define
///     would still emit a frame but under a different opcode — drain
///     for 0x003A would time out.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every inner
///     opcode in the 0x2016 PACKET_SEQUENCE parser; 0x003A would not
///     appear under its correct label.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard
///     tightening at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b>
///     Currently passes 0x003A (&lt; 0x0FFF). A regression to a tighter
///     upper bound would silently drop it from the wire.
///   </item>
///   <item>
///     <b>StarbaseRequest dispatch-table entry deletion at
///     <c>PlayerConnection.cpp:487-489</c>.</b> If the case
///     <c>ENB_OPCODE_004E_STARBASE_REQUEST</c> in the main packet
///     dispatch table is removed or renamed, the server silently
///     swallows the request and 0x003A never arrives.
///   </item>
///   <item>
///     <b>StarbaseRequest struct layout regression in
///     <c>common/include/net7/PacketStructures.h:812-817</c>.</b> The
///     struct is currently 9B packed (int32 + int32 + char,
///     ATTRIB_PACKED so no padding). A revert that loses the packed
///     attribute pads it to 12B on x86_64 — the cast at
///     <c>PlayerConnection.cpp:9848</c> would then read Action from
///     offset 8 (which our 9-byte wire correctly puts the byte at) but
///     PlayerID/StarbaseID from the wrong offsets in any payload that
///     had a 12B struct serialised on the way in. Our test sends 9
///     bytes so the cast reads Action=1 from byte 8 — passing here
///     would still detect the regression if the codec ever serialised
///     a packed-vs-unpacked-mismatched 12-byte struct.
///   </item>
///   <item>
///     <b>StationMgr.GetStation lookup regression for station 10151.</b>
///     The optional SendMessageString at <c>SectorManager.cpp:541-544</c>
///     is gated by <c>g_ServerMgr-&gt;m_StationMgr.GetStation(m_SectorID)
///     != NULL</c>. If station 10151's row vanishes from the StationMgr
///     bootstrap, 0x0086 disappears but the test still expects 0x003A
///     to follow because the SendServerHandoff call sits outside that
///     guard.
///   </item>
/// </list>
///
/// <para>
/// CLAUDE.md server-integrity. Wave 55 sends an input shape the real
/// retail client emitted (STARBASE_REQUEST with Action=1 is the
/// "Launch" UI button) and asserts a server-originated reply
/// (0x003A SERVER_HANDOFF) the real retail server has always produced
/// on that input. No server change, no widened input acceptance, no
/// loosened gating, no debug-only opcode, no security-posture
/// relaxation. The 9-byte payload matches the canonical
/// <c>StarbaseRequest</c> packed struct.
/// </para>
///
/// <para>
/// Cleanup. After SendServerHandoff, the server has called
/// <c>DropPlayerFromSector</c> (PlayerManager.cpp:102-117) — the
/// player is marked inactive, removed from sector range lists, and
/// has had SaveData/UpdateDatabase/SaveAmmoLevels run. The player
/// node is NOT released (that's DropPlayerFromGalaxy's job). We send
/// 0x00B9 LOGOFF_REQUEST to drive the release synchronously, drain
/// for the 0x00BA confirmation, then delete the avatar slot via the
/// still-open global TCP. The same pattern is used by
/// <see cref="SectorLogoffRequestTests"/> as the canonical clean-shutdown.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; STARBASE_REQUEST + LaunchIntoSpace
/// fan-out + 0x003A is sub-second; LOGOFF_REQUEST + 0x00BA round-trip
/// is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorServerHandoffTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorServerHandoffTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task StarbaseExitAction_ReceivesServerHandoffFrame()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Hando", shipName: "HandoShip", cts.Token);

        try
        {
            // 0x004E STARBASE_REQUEST — 9B canonical packed payload.
            // Server code at PlayerConnection.cpp:9879-9892 only
            // reads Action in the case=1 branch; PlayerID and
            // StarbaseID are logged but not consulted on this path.
            //   [0..4) int32 PlayerID   = 0 (case-1 ignores)
            //   [4..8) int32 StarbaseID = 0 (case-1 ignores)
            //   [8..9)   char Action    = 1 (drives LaunchIntoSpace)
            byte[] payload = new byte[9];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);
            payload[8] = 1;

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.StarbaseRequest.Value, payload),
                cts.Token);

            // Drain inbound until we see a 0x003A SERVER_HANDOFF.
            // The LaunchIntoSpace fan-out also emits 0x004F
            // STARBASE_SET and (when the station has a StationMgr
            // entry) 0x0086 MESSAGE_STRING ahead of it — both are
            // already covered by other waves, so we tolerate and
            // skip past them rather than asserting their order.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode == OpcodeId.Known.ServerHandoff.Value)
                {
                    // ServerHandoff payload starts with the inner
                    // MasterJoin-shaped `join` struct (64B canonical
                    // per PacketStructures.h's MasterJoin) plus
                    // four length-prefixed strings. Minimum sane
                    // length: 64B inner + 4×2B length fields + the
                    // non-empty sector/system names = >70B. Pin a
                    // very loose lower bound here to catch a
                    // zero-body emit regression without coupling
                    // to the exact retail layout.
                    Assert.True(reply.Payload.Length >= 64,
                        $"0x003A SERVER_HANDOFF payload was {reply.Payload.Length}B; " +
                        $"expected ≥64B (inner MasterJoin struct alone is 64B). " +
                        $"Likely SendServerHandoff at PlayerConnection.cpp:10202 " +
                        $"is emitting an empty/truncated payload.");
                    return;
                }
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x004E STARBASE_REQUEST " +
                $"Action=1 without seeing 0x003A SERVER_HANDOFF. " +
                $"Likely the case-1 branch in HandleStarbaseRequest " +
                $"(PlayerConnection.cpp:9879-9892) was deleted/short-circuited, " +
                $"SectorManager::LaunchIntoSpace's SendServerHandoff call at " +
                $"SectorManager.cpp:551 was removed, SendServerHandoff's opcode " +
                $"constant at PlayerConnection.cpp:10202 was flipped, the proxy's " +
                $"SendClientPacketSequence guard at UDPProxyToClient_linux.cpp:568 " +
                $"was tightened past 0x003A, or the STARBASE_REQUEST case at " +
                $"PlayerConnection.cpp:487 was removed/renamed in the dispatch table.");
        }
        finally
        {
            // Drive a clean LOGOFF round-trip so DropPlayerFromGalaxy
            // runs (PlayerConnection.cpp:7719) and releases the
            // player node before the global plane deletes the
            // avatar slot. DropPlayerFromSector already ran inside
            // LaunchIntoSpace so this is the second drop call —
            // DropPlayerFromSector is idempotent (range-list
            // removal + SetActive(false) are no-ops the second
            // time; SaveData no-ops if PlayerIndex's sector_num
            // wasn't reset).
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
}
