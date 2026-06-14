// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.IntegrationTests.Net;
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
///   lands (non-zero StartId), then FLY (via the proxy's MVAS position feed,
///   <see cref="ProxyPositionFeed"/> -- the same transport
///   <see cref="SectorMiningTests"/> proves) from the arrival gate toward the
///   "Nav High Earth 1 Resource Field" centre and assert RESOURCE nodes fan
///   out. Roid CREATEs are range-gated against the avatar's live position --
///   nothing arrives at the gate-entry point -- so the fly-in is required, and
///   resource presence is the proof the gate jump reached a mineable sector:
///   the gate-half of the live mining round-trip.</item>
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
/// = 20 resource nodes, nearest field to the arrival gate "Nav High Earth 1
/// Resource Field" (sector_object_id 200137) at (28940, 1080, 1000).
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorGateHandoffFollowTests : SectorIntegrationTest
{
    private readonly ITestOutputHelper _out;

    public SectorGateHandoffFollowTests(ServerFixture server, ITestOutputHelper output)
        : base(server)
    {
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

        var session = Track(await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "Galeo", shipName: "GaleoShip", cts.Token));

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
        ProxyPositionFeed? feed = null;
        CancellationTokenSource? drainCts = null;
        Task? drain = null;
        try
        {
            // --- 1. Undock: 0x004E Action=1 -> handoff -> re-join Luna space. ---
            byte[] launch = new byte[9];
            launch[8] = 1;
            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.StarbaseRequest.Value, launch), cts.Token);

            var (toLuna, fromLuna) = await DrainToHandoff(session.Sector, cts.Token);
            Assert.Equal(lunaSpaceId, toLuna);

            var luna = await SectorEnterDriver.FollowHandoffAsync(ctx, session.GameId, slot, toLuna, fromLuna, cts.Token);
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

            var (toEarth, fromEarth) = await DrainToHandoff(luna.Sector, cts.Token);
            Assert.Equal(earthSpaceId, toEarth);

            // --- 4. Follow the gate handoff into Earth space; fly to the field. ---
            var earth = await SectorEnterDriver.FollowHandoffAsync(ctx, luna.GameId, slot, toEarth, fromEarth, cts.Token);
            earthConn = earth.Sector;
            await luna.Sector.DisposeAsync();
            lunaConn = null;
            Assert.Equal(earthSpaceId, earth.SectorId);
            Assert.NotEqual(0, earth.StartId);

            var earthWorld = new SectorWorld();
            foreach (var f in earth.HandshakeFrames) earthWorld.Ingest(f);

            // Background drain of the proxy TCP leg for the fly-in: roid CREATEs
            // arrive here as 0x0004 (proxy-expanded from the server's compact
            // 0x2019, create-type 38 == "resource") as the avatar comes in range,
            // and self-position via 0x0008/0x003E.
            drainCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            var earthSector = earth.Sector;
            drain = Task.Run(async () =>
            {
                try
                {
                    while (!drainCts.IsCancellationRequested)
                    {
                        var p = await earthSector.ReceiveAsync(drainCts.Token);
                        if (p is null) break;
                        earthWorld.Ingest(p);
                    }
                }
                catch (OperationCanceledException) { }
            });

            // Let the initial fanout + self-position settle.
            await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
            var self = earthWorld.SelfSnapshot(earth.GameId).Pos;
            Assert.True(self is not null,
                "no own position after the gate re-join; cannot drive the proxy position feed");

            // Fly from the arrival gate toward the nearest resource field.
            // Roid CREATEs are range-gated against the avatar's live position,
            // so nothing fans out at the gate-entry point -- we feed positions
            // through the proxy's MVAS intake (the same transport the in-client
            // position hook uses; see SectorMiningTests for why NOT a direct
            // MVAS datagram) until the field's roids appear. The proxy only
            // re-emits 0x1004 on a CHANGED position, so each tick advances.
            (float X, float Y, float Z) fieldCentre = (28940f, 1080f, 1000f); // "Nav High Earth 1 Resource Field"
            feed = new ProxyPositionFeed(_server.SectorHost);
            (float X, float Y, float Z) cur = self!.Value;
            _out.WriteLine($"self ({cur.X:0}, {cur.Y:0}, {cur.Z:0}); flying to field " +
                $"({fieldCentre.X:0}, {fieldCentre.Y:0}, {fieldCentre.Z:0})");

            int resources = 0;
            for (int i = 0; i < 160 && !cts.IsCancellationRequested; i++)
            {
                resources = earthWorld.NearestTo(earth.GameId)
                    .Count(t => SectorWorld.TypeName(t.Obj) == "resource");
                if (resources > 0) break;

                float dx = fieldCentre.X - cur.X, dy = fieldCentre.Y - cur.Y, dz = fieldCentre.Z - cur.Z;
                float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                (float X, float Y, float Z) head = dist < 1f ? (1f, 0f, 0f) : (dx / dist, dy / dist, dz / dist);
                if (dist >= 1f)
                {
                    float adv = MathF.Min(1500f, dist);
                    cur = (cur.X + head.X * adv, cur.Y + head.Y * adv, cur.Z + head.Z * adv);
                }
                feed.Send(cur, head, earthSpaceId);
                await Task.Delay(100, cts.Token);
            }

            var earthObjs = earthWorld.NearestTo(earth.GameId);
            resources = earthObjs.Count(t => SectorWorld.TypeName(t.Obj) == "resource");
            int mobs = earthObjs.Count(t => SectorWorld.TypeName(t.Obj) is "mob spawn" or "mob");
            _out.WriteLine($"earth {earth.SectorId}: {earthObjs.Count} objects ({resources} resources, {mobs} mobs)");

            Assert.True(resources > 0,
                $"gated into sector {earth.SectorId} (START 0x{earth.StartId:X8}) and flew to the " +
                $"resource field at ({fieldCentre.X:0}, {fieldCentre.Y:0}, {fieldCentre.Z:0}) but saw " +
                $"0 resource nodes -- the field did not spawn/send roids on the proxy leg, or the " +
                $"position feed never moved us into range.");

            // Stop the background drain BEFORE the logoff drain below -- two
            // concurrent ReceiveAsync calls on one connection would race.
            drainCts.Cancel();
            try { await drain; } catch { }
            drain = null;

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
            if (drainCts is not null) { try { drainCts.Cancel(); } catch { } }
            if (drain is not null) { try { await drain; } catch { } }
            feed?.Dispose();
            drainCts?.Dispose();
            if (earthConn is not null) { try { await earthConn.DisposeAsync(); } catch { } }
            if (lunaConn is not null) { try { await lunaConn.DisposeAsync(); } catch { } }
        }
    }

    /// <summary>Drain the sector connection until a 0x003A SERVER_HANDOFF and
    /// return its ToSectorID (offset 20, BE) and FromSectorID (offset 24, BE).
    /// FromSectorID is echoed back in the re-join MasterJoin so the destination
    /// sector spawns the avatar at the gate linking back to where we came from.</summary>
    private static async Task<(int To, int From)> DrainToHandoff(EncryptedTcpConnection conn, CancellationToken ct)
    {
        for (int seen = 0; seen < 600; seen++)
        {
            var reply = await conn.ReceiveAsync(ct);
            Assert.NotNull(reply);
            if (reply!.Header.Opcode == OpcodeId.Known.ServerHandoff.Value)
            {
                Assert.True(reply.Payload.Length >= 28,
                    $"0x003A payload {reply.Payload.Length}B < 28B; cannot read To/FromSectorID");
                return (BinaryPrimitives.ReadInt32BigEndian(reply.Payload.Span.Slice(20, 4)),
                        BinaryPrimitives.ReadInt32BigEndian(reply.Payload.Span.Slice(24, 4)));
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
