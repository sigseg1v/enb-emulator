// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Repl;
using Xunit;
using Xunit.Abstractions;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// End-to-end proof that, once the CLI has FOLLOWED the undock handoff into
/// open space (<see cref="SectorUndockHandoffFollowTests"/>), a direct MVAS
/// position feed (0x1004 MVAS_SEND_POSITION to the server's MVAS port 3806) is
/// ACCEPTED and the server re-routes the player's downstream sector stream back
/// to the feeding socket.
///
/// <para>
/// This is the empirical answer to the long-standing "MVAS move IP-mismatch"
/// question. The earlier note feared the server's anti-spoof guard
/// (UDP_Client.cpp:72, <c>player-&gt;PlayerIPAddr() == source_addr</c>) would
/// drop the CLI's direct datagrams, because the player was established THROUGH
/// the proxy (server registers the proxy's container IP as PlayerIPAddr) while
/// the CLI's MVAS goes DIRECT to 3806 (server sees the docker-gateway IP). But
/// that guard lives on the SECTOR connection
/// (<c>UDP_Connection::HandleClientOpcode</c>). The MVAS port runs a DIFFERENT
/// dispatcher (<c>HandleMVASOpcode</c>, UDPConnection.cpp:214) whose 0x1004
/// handler (<c>HandleMVASPosReturn</c>, UDP_MVAS.cpp:118) keys purely on
/// <c>hdr-&gt;player_id</c> and re-points the player endpoint
/// (<c>SetPlayerPortIP(port, addr)</c>, UDP_MVAS.cpp:149) UNCONDITIONALLY -- no
/// IP guard. So a direct 0x1004 IS accepted and takes over the downstream from
/// the proxy. This test pins that behaviour so a future server change that
/// (wrongly) puts the IP guard on the MVAS path breaks the build.
/// </para>
///
/// <para>
/// CLAUDE.md server-integrity: no server change. The server keys the MVAS
/// update on the player_id it already issued; the test only feeds the retail
/// 0x1004 datagram from a real UDP socket, exactly as the proxy does when it
/// scrapes the Win32 client's engine position. No guard is weakened, no input
/// the real server rejects is accepted.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorMvasMoveTests
{
    private const int MvasPort = 3806; // MVAS_LOGIN_PORT (common/include/net7/Ports.h)

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;
    private readonly ITestOutputHelper _out;

    public SectorMvasMoveTests(ServerFixture server, ITestOutputHelper output)
    {
        _server = server;
        _client = new ClientFixture(server);
        _out = output;
    }

    [Fact]
    public async Task Mvas_DirectPositionFeed_IsAccepted_AndStreamsSectorBack()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int stationSectorId = 10151;       // Luna Station (Terran Warrior start)
        const int expectedSpaceSectorId = 1015;  // 10151 / 10 = Luna space

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(150));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");

        var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "Mwillo", shipName: "MwilloShip", cts.Token);

        SectorUdpClient? udp = null;
        EncryptedTcpConnection? spaceConn = null;
        try
        {
            // --- 1. Undock + follow handoff into space (the proven path). ---
            byte[] launch = new byte[9];
            launch[8] = 1; // Action = 1 (exit station)
            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.StarbaseRequest.Value, launch), cts.Token);

            int toSectorId = -1;
            for (int seen = 0; seen < 400; seen++)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                if (reply!.Header.Opcode == OpcodeId.Known.ServerHandoff.Value)
                {
                    toSectorId = System.Buffers.Binary.BinaryPrimitives
                        .ReadInt32BigEndian(reply.Payload.Span.Slice(20, 4));
                    break;
                }
            }
            Assert.Equal(expectedSpaceSectorId, toSectorId);

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
            await session.Sector.DisposeAsync(); // station conn finished
            Assert.Equal(expectedSpaceSectorId, rejoin.SectorId);
            Assert.NotEqual(0, rejoin.StartId);

            // --- 2. Build the world from the re-join handshake to learn our
            //        own position and pick a nav to fly toward. ---
            var world = new SectorWorld();
            foreach (var f in rejoin.HandshakeFrames) world.Ingest(f);

            // Drain the TCP (proxy) sector stream briefly so self-position and
            // navs land before we take the downstream over via MVAS.
            using (var warm = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
            {
                warm.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    while (!warm.IsCancellationRequested)
                    {
                        var p = await rejoin.Sector.ReceiveAsync(warm.Token);
                        if (p is null) break;
                        world.Ingest(p);
                    }
                }
                catch (OperationCanceledException) { }
            }

            var self = world.SelfSnapshot(rejoin.GameId).Pos;
            Assert.True(self is not null,
                "no own position after the re-join fanout; cannot drive MVAS");
            var start = self!.Value;
            _out.WriteLine($"self position ({start.X:0.0}, {start.Y:0.0}, {start.Z:0.0})");

            // Pick a nav as the flight target (1015 is a nav/mob field).
            var navTarget = world.NearestTo(rejoin.GameId)
                .Select(t => t.Obj)
                .FirstOrDefault(o => SectorWorld.TypeName(o).StartsWith("nav")
                    && world.PositionOf(o.GameId) is not null);

            (float X, float Y, float Z) dst = navTarget is not null
                ? world.PositionOf(navTarget.GameId)!.Value
                : (start.X + 5000f, start.Y, start.Z);

            // --- 3. Stand up the DIRECT MVAS/sector UDP client and feed
            //        position toward the target. ---
            udp = new SectorUdpClient(
                _server.SectorHost, MvasPort,
                onInbound: world.Ingest,
                log: m => _out.WriteLine(m));
            udp.Start(cts.Token);
            _out.WriteLine($"MVAS udp up: local :{udp.LocalPort} -> {_server.SectorHost}:{MvasPort}; " +
                $"target nav={(navTarget is null ? "none" : $"0x{navTarget.GameId:X8}")} " +
                $"({dst.X:0.0}, {dst.Y:0.0}, {dst.Z:0.0})");

            // Fly ~40 steps toward the target, feeding position+heading each tick
            // (the server only sweeps in-range navs while the position MOVES).
            (float X, float Y, float Z) cur = start;
            for (int i = 0; i < 40 && !cts.IsCancellationRequested; i++)
            {
                float dx = dst.X - cur.X, dy = dst.Y - cur.Y, dz = dst.Z - cur.Z;
                float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                if (dist < 1f) break;
                float inv = 1f / dist;
                (float X, float Y, float Z) head = (dx * inv, dy * inv, dz * inv);
                float adv = MathF.Min(1500f, dist);
                cur = (cur.X + head.X * adv, cur.Y + head.Y * adv, cur.Z + head.Z * adv);
                udp.SendPosition(rejoin.GameId, cur.X, cur.Y, cur.Z, head);
                await Task.Delay(50, cts.Token);
            }

            // Give the server a moment to route the downstream back to us.
            using (var settle = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
            {
                settle.CancelAfter(TimeSpan.FromSeconds(3));
                try { await Task.Delay(Timeout.Infinite, settle.Token); }
                catch (OperationCanceledException) { }
            }

            _out.WriteLine($"MVAS rx datagrams: {udp.ReceivedDatagrams}");

            // The load-bearing assertion: the server accepted our DIRECT MVAS
            // feed (keyed on player_id, not source IP) and re-routed the
            // player's downstream sector stream to our socket. If the MVAS path
            // were (wrongly) IP-guarded against the proxy-registered address,
            // zero datagrams would come back here.
            Assert.True(udp.ReceivedDatagrams > 0,
                "no datagrams returned to the direct MVAS socket; the server did not accept the " +
                "0x1004 feed or did not re-route the downstream -- the MVAS path may have regressed " +
                "to IP-guarding against the proxy-registered address.");
        }
        finally
        {
            if (udp is not null) { try { await udp.StopAsync(); } catch { } udp.Dispose(); }
            if (spaceConn is not null) { try { await spaceConn.DisposeAsync(); } catch { } }
            else { try { await session.Sector.DisposeAsync(); } catch { } }

            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { }
            try { await session.Global.DisposeAsync(); } catch { }
        }
    }
}
