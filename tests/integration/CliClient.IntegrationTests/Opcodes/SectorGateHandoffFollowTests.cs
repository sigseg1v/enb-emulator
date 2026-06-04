// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Repl;
using N7.CliClient.Repl.Commands;
using Xunit;
using Xunit.Abstractions;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// End-to-end proof of the CLI <c>gate</c> flow: a full mob-sector -> resource-
/// sector jump driven entirely by the retail gate sequence. This is the live
/// half of the gate command (commit fbe16a5e); <see cref="GateCommandTests"/>
/// only byte-pins the ActionPacket layout offline.
///
/// <para>
/// Flow:
/// </para>
/// <list type="number">
///   <item>Establish at Luna Station (10151), undock (0x004E Action=1) and
///   FOLLOW the handoff into Luna space (1015) -- the same path
///   <see cref="SectorUndockHandoffFollowTests"/> proves.</item>
///   <item>In 1015, find the stargate named "Sector Gate to Earth" in the
///   fanned-out world (DB sector_objects: obj 533, type 11, gate_to 1060).</item>
///   <item>Send 0x002C ACTION Action=18 (Target = that gate's game id) then,
///   after a short pause, Action=19 -- the retail gate sequence. The server
///   (PlayerConnection.cpp:3923 case 18 -> SectorManager::Gate stores
///   StargateDestination; :3965 case 19 -> SectorServerHandoff) emits a 0x003A
///   SERVER_HANDOFF. Assert ToSectorID == 1060 (Earth).</item>
///   <item>FollowHandoffAsync into 1060 with the SAME avatar id; assert it
///   lands (non-zero StartId) and that the destination fans out RESOURCE nodes
///   (Earth space carries 20 type-38 asteroids). Resource presence is the proof
///   the gate jump reached a mineable sector -- the gate-half of the live mining
///   round-trip.</item>
/// </list>
///
/// <para>
/// CLAUDE.md server-integrity: no server change. Every input is a real retail
/// packet (0x004E Action=1, 0x002C Action=18/19) and a standard MasterJoin +
/// sector LOGIN reusing an avatar id the server already issued. The gate has no
/// server-side range check (case 18/19 never test position), so this faithfully
/// reproduces the client's select-then-confirm sequence; no gating is loosened.
/// Route confirmed against the live DB: sector_objects(sector_id=1015) gate 533
/// "Sector Gate to Earth" gate_to=1060; sector_objects(sector_id=1060,type=38)
/// = 20 resource nodes.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorGateHandoffFollowTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;
    private readonly ITestOutputHelper _out;

    public SectorGateHandoffFollowTests(ServerFixture server, ITestOutputHelper output)
    {
        _server = server;
        _client = new ClientFixture(server);
        _out = output;
    }

    [Fact]
    public async Task Gate_FromMobSector_LandsInResourceSector()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int stationSectorId = 10151;   // Luna Station (Terran Warrior start)
        const int lunaSpaceId     = 1015;    // 10151 / 10
        const int earthSpaceId    = 1060;    // "Sector Gate to Earth" destination

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "Galeo", shipName: "GaleoShip", cts.Token);

        var ctx = new SessionContext(new OpcodeRegistry())
        {
            Host = _server.SectorHost,
            MasterPort = _server.MasterPort,
            SectorPort = _server.SectorPort,
            Ticket = login.Ticket,
            Username = account.Username,
        };

        EncryptedTcpConnection? lunaConn = null;
        EncryptedTcpConnection? earthConn = null;
        try
        {
            // --- 1. Undock: 0x004E Action=1 -> handoff -> re-join Luna space. ---
            byte[] launch = new byte[9];
            launch[8] = 1;
            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.StarbaseRequest.Value, launch), cts.Token);

            int toLuna = await DrainToHandoff(session.Sector, cts.Token);
            Assert.Equal(lunaSpaceId, toLuna);

            var luna = await SectorEnterDriver.FollowHandoffAsync(ctx, session.GameId, slot, toLuna, cts.Token);
            lunaConn = luna.Sector;
            await session.Sector.DisposeAsync();
            Assert.Equal(lunaSpaceId, luna.SectorId);
            Assert.NotEqual(0, luna.StartId);

            // --- 2. Build the Luna world; find the Earth gate. ---
            var lunaWorld = new SectorWorld();
            foreach (var f in luna.HandshakeFrames) lunaWorld.Ingest(f);
            await DrainFanout(luna.Sector, lunaWorld, TimeSpan.FromSeconds(6), cts.Token);

            var stargates = lunaWorld.NearestTo(luna.GameId)
                .Select(t => t.Obj)
                .Where(o => SectorWorld.TypeName(o) == "stargate")
                .ToList();
            _out.WriteLine($"luna {luna.SectorId}: {stargates.Count} stargate(s)");
            foreach (var g in stargates)
                _out.WriteLine($"  gate gid=0x{g.GameId:X8} name=\"{g.Name}\"");

            var earthGate = stargates.FirstOrDefault(
                o => o.Name?.Contains("Earth", StringComparison.OrdinalIgnoreCase) == true);
            Assert.True(earthGate is not null,
                $"sector {luna.SectorId} fanned out {stargates.Count} stargates but none named *Earth*; " +
                $"the gate-jump target could not be resolved.");

            // --- 3. Gate sequence: 0x002C Action=18 (select) then 19 (confirm). ---
            await luna.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.Action.Value,
                    GateCommand.BuildActionFrame(luna.GameId, 18, earthGate!.GameId, 0)), cts.Token);

            // The server stores StargateDestination synchronously on Action=18
            // (SectorManager::Gate), so Action=19 may follow immediately. A short
            // pause lets the gate-open path settle without the retail 5.8s wait.
            await Task.Delay(TimeSpan.FromMilliseconds(750), cts.Token);

            await luna.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.Action.Value,
                    GateCommand.BuildActionFrame(luna.GameId, 19, earthGate.GameId, 0)), cts.Token);

            int toEarth = await DrainToHandoff(luna.Sector, cts.Token);
            Assert.Equal(earthSpaceId, toEarth);

            // --- 4. Follow the gate handoff into Earth space; assert resources. ---
            var earth = await SectorEnterDriver.FollowHandoffAsync(ctx, luna.GameId, slot, toEarth, cts.Token);
            earthConn = earth.Sector;
            await luna.Sector.DisposeAsync();
            lunaConn = null;
            Assert.Equal(earthSpaceId, earth.SectorId);
            Assert.NotEqual(0, earth.StartId);

            var earthWorld = new SectorWorld();
            foreach (var f in earth.HandshakeFrames) earthWorld.Ingest(f);
            await DrainFanout(earth.Sector, earthWorld, TimeSpan.FromSeconds(6), cts.Token);

            var earthObjs = earthWorld.NearestTo(earth.GameId);
            int resources = earthObjs.Count(t => SectorWorld.TypeName(t.Obj) == "resource");
            int mobs = earthObjs.Count(t => SectorWorld.TypeName(t.Obj) is "mob spawn" or "mob");
            _out.WriteLine($"earth {earth.SectorId}: {earthObjs.Count} objects ({resources} resources, {mobs} mobs)");

            Assert.True(resources > 0,
                $"gated into sector {earth.SectorId} (START 0x{earth.StartId:X8}) but saw 0 resource nodes; " +
                $"Earth space carries 20 type-38 asteroids -- the gate jump did not reach the mineable sector.");

            // --- clean logoff on the Earth connection. ---
            try
            {
                using var logoffCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await earth.Sector.SendAsync(
                    Packet.ForOpcode(OpcodeId.Known.LogoffRequest.Value, new byte[8]), logoffCts.Token);
                await SectorHandshake.DrainUntilOpcode(
                    earth.Sector, OpcodeId.Known.LogoffConfirmation.Value, logoffCts.Token);
            }
            catch { /* best-effort logoff */ }
        }
        finally
        {
            if (earthConn is not null) { try { await earthConn.DisposeAsync(); } catch { } }
            if (lunaConn is not null) { try { await lunaConn.DisposeAsync(); } catch { } }

            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
            try { await session.Global.DisposeAsync(); } catch { }
        }
    }

    /// <summary>Drain the sector connection until a 0x003A SERVER_HANDOFF and
    /// return its ToSectorID (offset 20, big-endian).</summary>
    private static async Task<int> DrainToHandoff(EncryptedTcpConnection conn, CancellationToken ct)
    {
        for (int seen = 0; seen < 600; seen++)
        {
            var reply = await conn.ReceiveAsync(ct);
            Assert.NotNull(reply);
            if (reply!.Header.Opcode == OpcodeId.Known.ServerHandoff.Value)
            {
                Assert.True(reply.Payload.Length >= 24,
                    $"0x003A payload {reply.Payload.Length}B < 24B; cannot read ToSectorID");
                return BinaryPrimitives.ReadInt32BigEndian(reply.Payload.Span.Slice(20, 4));
            }
        }
        throw new Xunit.Sdk.XunitException("no 0x003A SERVER_HANDOFF within 600 frames");
    }

    /// <summary>Drain a fixed post-START window into the world model.</summary>
    private static async Task DrainFanout(EncryptedTcpConnection conn, SectorWorld world, TimeSpan window, CancellationToken ct)
    {
        using var fanoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        fanoutCts.CancelAfter(window);
        try
        {
            while (!fanoutCts.IsCancellationRequested)
            {
                var p = await conn.ReceiveAsync(fanoutCts.Token);
                if (p is null) break;
                world.Ingest(p);
            }
        }
        catch (OperationCanceledException) { /* window closed */ }
    }
}
