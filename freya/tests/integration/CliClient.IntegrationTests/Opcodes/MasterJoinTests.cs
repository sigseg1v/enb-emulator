// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Net;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// 0x0035 MasterJoin -> 0x0036 ServerRedirect happy-path round-trip
/// against the live proxy on the master server port (3801). The proxy's
/// Linux HandleMasterJoin (proxy/ClientToMasterServer.cpp:93-146) hands
/// the join off to the UDP plane (SendMasterLogin -> UDP 3808 -> wait for
/// 0x2009 confirm); when the server has a live Player for the joining
/// game id, the confirm comes back promptly and the proxy emits a
/// ServerRedirect pointing the client at the sector server (3500).
///
/// <para>
/// To exercise that path the test allocates a REAL game id: it creates a
/// character and issues a 0x0070 GlobalTicketRequest (which registers the
/// in-memory Player keyed by the PLAYER_TAG'd game id), then master-joins
/// with that id -- exactly what <see cref="SectorHandshake.EstablishAsync"/>
/// does. A bogus avatar id (e.g. a bare account id) has no Player, so the
/// handoff can never confirm and the proxy spins through its full
/// cold-start retry budget (~30 attempts, commit ef106aec) before falling
/// back -- that slow degraded path is not what a normal client hits and is
/// not what this test asserts.
/// </para>
///
/// <para>
/// Asserts the ServerRedirect is well-formed: the sector id is echoed back
/// from the join request, the redirect port is the sector server port, and
/// the redirect IP is a real address (not 0.0.0.0). The IP itself is not
/// pinned -- the proxy uses its own m_IpAddress (the docker-bridge address,
/// 172.x.x.x), not 127.0.0.1.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class MasterJoinTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public MasterJoinTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ValidMasterJoin_ReceivesServerRedirect()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 1015;  // Terran Warrior home (Luna space), StartSector[0*3+0]

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new global::N7.CliClient.Auth.AuthLoginRequest(account.Username, account.Password),
            cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // Allocate a real in-memory Player so HandleMasterJoin hands off to
        // a live game id (GetPlayer succeeds) and the master plane confirms
        // promptly -- the happy path.
        await using var globalConn = await EncryptedTcpConnection.ConnectAsync(
            _server.GlobalHost, _server.GlobalPort, cts.Token);
        await SectorHandshake.SendGlobalConnectAsync(globalConn, login.Ticket!, cts.Token);
        await SectorHandshake.DrainUntilOpcode(
            globalConn, OpcodeId.Known.GlobalAvatarList.Value, cts.Token);
        await SectorHandshake.CreateCharacterOnSlotAsync(
            globalConn, account.Username, slot, firstName: "Joiner", shipName: "JoinerShip", cts.Token);
        int gameId = await SectorHandshake.RequestTicketAsync(globalConn, slot, cts.Token);

        const int PlayerTag = 1 << 30;
        Assert.True((gameId & PlayerTag) != 0,
            $"GameID 0x{gameId:X8} missing PLAYER_TAG -- GlobalTicketRequest hit the failure path.");

        try
        {
            var redirect = await SectorHandshake.DoMasterJoinAsync(
                _server, login.Ticket!, gameId, sectorId, cts.Token);

            // Sector ID echoed back from the join request.
            Assert.Equal(sectorId, redirect.SectorId);

            // Redirect points the client at the sector server (3500).
            Assert.Equal(_server.SectorPort, redirect.ServerEndPoint.Port);
            Assert.True(
                !redirect.ServerEndPoint.Address.Equals(IPAddress.Any),
                $"redirect IP must be a real address, got {redirect.ServerEndPoint.Address}");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(globalConn, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
