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
/// Wave 36 survival probe: client sends 0x0082 RECUSTOMIZE_SHIP_DONE
/// with a canonical 210-byte all-zero RecustomizeShipDone payload
/// from a freshly-handshaken starbase character; assert the server
/// survives by round-tripping REQUEST_TIME after.
///
/// <para>
/// Wire shape (common/include/net7/PacketStructures.h:1101):
/// <code>
///   struct RecustomizeShipDone {
///       struct ShipData ship;   // 194 bytes (5 int32 race/profession/
///                               //   hull/wing/decal + 26 ship_name +
///                               //   12 ship_name_color + 8 ColorInfo)
///       int32_t playerid;       // 4 bytes
///       bool    unknown;        // 1 byte
///       char    _unknown[11];   // 11 bytes
///   } ATTRIB_PACKED;            // sizeof = 210
/// </code>
/// </para>
///
/// <para>
/// Server handler walk-through (server/src/PlayerConnection.cpp:10056).
/// </para>
/// <list type="number">
///   <item>Cast data to <c>RecustomizeShipDone *</c>. 210-byte buffer
///     fits the struct exactly; no OOB read.</item>
///   <item>Compute recustomisation cost via 12 memcmp /
///     int-comparison checks between the incoming packet->ship and
///     the current m_Database.ship_data. All-zero packet means
///     every field differs from the fresh character's default ship
///     data (Terran Warrior starter ship has non-zero hull/wing/
///     decal/colours), so the maximum cost path is exercised.</item>
///   <item>SetCredits(GetCredits() - cost). On a fresh character with
///     starter credits, cost may exceed credits and the result
///     underflows; PlayerIndex()->SetCredits accepts the signed
///     result silently. Not a survival risk.</item>
///   <item>m_Database.ship_data = packet->ship; -- OVERWRITES the
///     character's ship_data with zeros. Destructive but contained:
///     character is deleted at end-of-test via
///     SectorHandshake.DeleteCreatedCharacterAsync.</item>
///   <item>SaveDatabase, NeatenUpWeaponMounts, RemoveObject(GameID),
///     SendShipData(this), SendAuxShipExtended, SendAuxPlayer. All
///     these emit on the per-Player UDP queue but no specific reply
///     opcode is the canonical post-condition; survival via
///     REQUEST_TIME -> CLIENT_SET_TIME echo is the test signal.</item>
/// </list>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at PlayerConnection.cpp:543.</b>
///     The 0x0082 case sits between 0x0080
///     MANUFACTURE_TECH_LEVEL_FILTER (line 539) and 0x0084
///     RECUSTOMIZE_AVATAR_DONE (line 547). Swap with the latter
///     mis-casts our 210B buffer as RecustomizeAvatarDone (257B)
///     and reads 47 bytes of OOB; survival probe catches the
///     SEGV.
///   </item>
///   <item>
///     <b>ShipData struct layout drift at PacketStructures.h:196.</b>
///     int32_t race/profession/hull/wing/decal -> long widening on
///     LP64 Linux inflates sizeof(ShipData) from 194 to 214, and
///     sizeof(RecustomizeShipDone) from 210 to 230 -- a 20-byte
///     wire-format divergence the proxy would forward as-is and
///     the server would read 20 bytes past our 210B buffer end.
///     SEGV catches.
///   </item>
///   <item>
///     <b>ColorInfo embedded struct drift at PacketStructures.h:178.</b>
///     int32_t metal -> long widening adds +4 bytes per ColorInfo
///     × 8 instances = +32 bytes to ShipData. Same SEGV class as
///     above.
///   </item>
///   <item>
///     <b>SendShipData NULL-deref / crash on the synthetic
///     all-zero ship.</b> The all-zero ship has hull=0 wing=0
///     decal=0, which may not correspond to any valid item ID in
///     the asset table; SendShipData lookup paths must tolerate
///     this gracefully or crash. Survival catches the crash.
///   </item>
///   <item>
///     <b>NeatenUpWeaponMounts crash after ship_data wipe.</b>
///     The function recomputes weapon mount positions from the
///     ship hull; a regression that drops the !ship_hull guard
///     would NULL-deref on lookup. Survival catches.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     PlayerConnection.cpp:127.</b> Corrupts the 0x2016 inner-
///     tuple parser; REQUEST_TIME echo never observed.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode dropping 0x0082.</b>
///     0x0082 is not explicitly listed in
///     proxy/ClientToServer_linux_stubs.cpp ProcessSectorServerOpcode
///     switch (verified by grep); falls through to bottom default-
///     forward arm. Regression dropping that arm silents both
///     0x0082 dispatch AND the REQUEST_TIME echo.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The 210-byte
/// RecustomizeShipDone is the canonical retail Win32 client wire
/// shape emitted when the player confirms a ship recustomisation
/// at a recustomiser terminal. All-zero is structurally well-formed
/// (per the ATTRIB_PACKED struct) even though no real recustomiser
/// UI would emit zero hull/wing/decal -- the server's handler
/// trusts the wire shape and the assumption that the client has
/// pre-validated. We are not loosening any input check; this is
/// the canonical wire byte count.
/// </para>
///
/// <para>
/// Destructive nature note. This test deliberately corrupts the
/// character's ship_data row in the database. The character is
/// deleted at end-of-test, so the corruption is contained. If a
/// future regression breaks DeleteCreatedCharacterAsync cleanup,
/// successive test runs will accumulate corrupted-ship characters
/// in the DB. The fixture pattern (TestAccounts.New per test)
/// scopes each test's account-uniqueness so DB FK collisions are
/// impossible; the orphaned rows would only be visible if you
/// poke at the DB directly.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; RECUSTOMIZE_SHIP_DONE +
/// REQUEST_TIME round-trip sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorRecustomizeShipDoneTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorRecustomizeShipDoneTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task RecustomizeShipDone_OnFreshStarbaseSession_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Reshyp" -- contains 'e' for the vowel-check.
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Reshyp", shipName: "ReshypShip", cts.Token);

        try
        {
            // RecustomizeShipDone canonical 210-byte all-zero shape:
            //   [0..194)  ShipData (5 int32 + 26 name + 12 color + 8*17 ColorInfo)
            //   [194..198) int32 playerid
            //   [198]      bool unknown
            //   [199..210) char _unknown[11]
            byte[] payload = new byte[210];
            // all zero already; payload[0..210] = 0.

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RecustomizeShipDone.Value, payload),
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
                $"drained {maxFrames} frames after sending 0x0082 RECUSTOMIZE_SHIP_DONE + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely HandleRecustomizeShipDone SEGV'd inside SendShipData on the all-zero ship_data, " +
                $"NeatenUpWeaponMounts NULL-deref'd on hull=0, " +
                $"ShipData struct layout drifted (int32_t -> long widening shifts the OOB boundary), " +
                $"the dispatcher case at PlayerConnection.cpp:543 got mis-routed, " +
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
