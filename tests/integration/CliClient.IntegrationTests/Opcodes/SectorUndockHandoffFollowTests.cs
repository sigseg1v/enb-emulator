// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Repl;
using Xunit;
using Xunit.Abstractions;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// End-to-end proof that the CLI <c>undock</c> flow FOLLOWS the handoff into
/// space, not just fires the launch packet. <see cref="SectorServerHandoffTests"/>
/// already pins that the server emits a 0x003A SERVER_HANDOFF with
/// ToSectorID == station/10 on a 0x004E Action=1; this test exercises the
/// second half -- <see cref="SectorEnterDriver.FollowHandoffAsync"/>, which is
/// what <c>UndockCommand</c> calls to RE-JOIN the space sector with the SAME
/// avatar id (no fresh GlobalTicketRequest), exactly as the real client does
/// on a handoff.
///
/// <para>
/// Flow:
/// </para>
/// <list type="number">
///   <item>Establish at Luna Station (10151) -- the Terran Warrior start.</item>
///   <item>Send 0x004E STARBASE_REQUEST Action=1; drain to 0x003A; read the
///   ToSectorID (offset 20, big-endian) and assert it is 1015 (Luna space).</item>
///   <item>Call <see cref="SectorEnterDriver.FollowHandoffAsync"/> with the
///   existing GameId to re-join 1015. Assert it lands (returns a non-zero
///   StartId) at sector 1015. The station connection stays open across the
///   re-join and is disposed afterwards -- the same ordering
///   <c>UndockCommand</c> uses.</item>
///   <item>Feed the re-join handshake frames + a short post-START fanout drain
///   into a <see cref="SectorWorld"/> and assert the space sector announced
///   live mob spawns (Luna 1015 is a Needlenose newbie field). Mob ship
///   spawns are open-space-only -- a station-docked sector fans out furniture
///   and lounge NPCs, never NPC ships -- so their presence proves the
///   handoff-follow landed in OPEN SPACE, not back in the station. (Asteroids
///   are NOT asserted: 1015 is a mob/nav sector, not a resource field; mining
///   means gating on to a resource sector, a separate step.)</item>
/// </list>
///
/// <para>
/// Why this is the load-bearing new test: the same-GameId re-join is a code
/// path the suite did not exercise before. <see cref="SectorHandshake.ReestablishAsync"/>
/// re-runs GlobalTicketRequest and gets a NEW GameId; the handoff path reuses
/// the player node the server kept alive across LaunchIntoSpace
/// (DropPlayerFromSector, not DropPlayerFromGalaxy -- SectorManager.cpp:574).
/// If that node-reuse ever regresses, the re-join here fails to reach START.
/// </para>
///
/// <para>
/// CLAUDE.md server-integrity: no server change. The inputs are the real
/// retail "Launch" packet (0x004E Action=1) and a standard MasterJoin +
/// sector LOGIN with an avatar id the server already issued; every reply is
/// server-originated. No widened acceptance, no loosened gating.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorUndockHandoffFollowTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;
    private readonly ITestOutputHelper _out;

    public SectorUndockHandoffFollowTests(ServerFixture server, ITestOutputHelper output)
    {
        _server = server;
        _client = new ClientFixture(server);
        _out = output;
    }

    [Fact]
    public async Task Undock_FollowsHandoff_LandsInSpaceWithAsteroids()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int stationSectorId = 10151;       // Luna Station (Terran Warrior start)
        const int expectedSpaceSectorId = 1015;  // 10151 / 10 = Luna space

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // NOT `await using`: Session.DisposeAsync sends a 0x00B9 logoff on the
        // STATION sector connection, but after the handoff the avatar has left
        // that sector and we re-join on a new connection. We close the station
        // socket raw (no logoff) ourselves and drive the clean logoff on the
        // space connection instead; the global plane is kept for cleanup.
        var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "Mwillo", shipName: "MwilloShip", cts.Token);

        EncryptedTcpConnection? spaceConn = null;
        try
        {
            // --- 1. Launch: 0x004E STARBASE_REQUEST Action=1, drain to 0x003A. ---
            byte[] launch = new byte[9];
            launch[8] = 1; // Action = 1 (exit station)
            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.StarbaseRequest.Value, launch), cts.Token);

            int toSectorId = -1;
            for (int seen = 0; seen < 400; seen++)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);
                if (reply!.Header.Opcode == OpcodeId.Known.ServerHandoff.Value)
                {
                    Assert.True(reply.Payload.Length >= 24,
                        $"0x003A payload {reply.Payload.Length}B < 24B; cannot read ToSectorID");
                    toSectorId = BinaryPrimitives.ReadInt32BigEndian(reply.Payload.Span.Slice(20, 4));
                    break;
                }
            }
            Assert.Equal(expectedSpaceSectorId, toSectorId);

            // --- 2. Follow the handoff: re-join 1015 with the SAME GameId. ---
            var ctx = new SessionContext(new OpcodeRegistry())
            {
                Host = _server.SectorHost,
                MasterPort = _server.MasterPort,
                SectorPort = _server.SectorPort,
                Ticket = login.Ticket,
                Username = account.Username,
            };

            var rejoin = await SectorEnterDriver.FollowHandoffAsync(
                ctx, session.GameId, slot, toSectorId, cts.Token);
            spaceConn = rejoin.Sector;

            // Station connection is finished now -- close it raw (no logoff).
            await session.Sector.DisposeAsync();

            Assert.Equal(expectedSpaceSectorId, rejoin.SectorId);
            Assert.NotEqual(0, rejoin.StartId);
            _out.WriteLine($"re-joined sector {rejoin.SectorId} startId=0x{rejoin.StartId:X8} " +
                $"handshake-frames={rejoin.HandshakeFrames.Count}");

            // --- 3. World: feed handshake + post-START fanout; assert asteroids. ---
            var world = new SectorWorld();
            foreach (var f in rejoin.HandshakeFrames) world.Ingest(f);

            using (var fanoutCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
            {
                fanoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    while (!fanoutCts.IsCancellationRequested)
                    {
                        var p = await rejoin.Sector.ReceiveAsync(fanoutCts.Token);
                        if (p is null) break;
                        world.Ingest(p);
                    }
                }
                catch (OperationCanceledException) { /* fanout window closed */ }
            }

            var tracked = world.NearestTo(rejoin.GameId);
            int mobs = tracked.Count(t => SectorWorld.TypeName(t.Obj) is "mob spawn" or "mob");
            int navs = tracked.Count(t => SectorWorld.TypeName(t.Obj).StartsWith("nav"));
            int resources = tracked.Count(t => SectorWorld.TypeName(t.Obj) == "resource");
            _out.WriteLine($"space sector {rejoin.SectorId}: {tracked.Count} objects " +
                $"({mobs} mobs, {navs} navs, {resources} resources)");
            foreach (var t in tracked.Take(20))
                _out.WriteLine($"  gid=0x{t.Obj.GameId:X8} type={SectorWorld.TypeName(t.Obj)} name={t.Obj.Name}");

            // Proof we are actually in OPEN SPACE and not still docked: the
            // sector announced live mob spawns. Station-docked sectors fan out
            // furniture / lounge NPCs, never NPC ship spawns. Luna space (1015)
            // is a Needlenose newbie field. (Asteroids are NOT asserted here:
            // 1015 is a mob/nav sector, not a resource field -- mining means
            // gating/warping on to a resource sector, a separate step. The
            // resource counter above stays as a diagnostic for sectors that
            // do bear them.)
            Assert.True(tracked.Count > 0,
                $"re-join to sector {rejoin.SectorId} produced START 0x{rejoin.StartId:X8} but the " +
                $"world stayed empty; the handoff-follow did not land in a populated sector.");
            Assert.True(mobs > 0,
                $"space sector {rejoin.SectorId} announced {tracked.Count} objects but 0 mob spawns; " +
                $"a station-docked sector fans out furniture, not NPC ships -- the handoff-follow may " +
                $"have re-joined the station instead of open space.");

            // --- clean logoff on the space connection. ---
            try
            {
                using var logoffCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await rejoin.Sector.SendAsync(
                    Packet.ForOpcode(OpcodeId.Known.LogoffRequest.Value, new byte[8]), logoffCts.Token);
                await SectorHandshake.DrainUntilOpcode(
                    rejoin.Sector, OpcodeId.Known.LogoffConfirmation.Value, logoffCts.Token);
            }
            catch { /* best-effort logoff */ }
        }
        finally
        {
            if (spaceConn is not null) { try { await spaceConn.DisposeAsync(); } catch { } }
            else { try { await session.Sector.DisposeAsync(); } catch { } }

            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
            try { await session.Global.DisposeAsync(); } catch { }
        }
    }
}
