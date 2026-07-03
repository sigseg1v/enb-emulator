// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.IntegrationTests.Net;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using N7.CliClient.Repl;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// End-to-end live PROSPECT/MINE round-trip: a prospecting-capable Explorer
/// undocks into a resource sector, flies to an asteroid field, locks a roid,
/// and triggers the mine action -- and we pin the server's authoritative
/// 0x2012 START_PROSPECT emission that results.
///
/// <para>
/// The trigger chain (all server-verified, no server change):
/// <list type="number">
///   <item>0x0017 REQUEST_TARGET locks the roid server-side
///         (<c>ShipIndex()-&gt;SetTargetGameID</c>).</item>
///   <item>0x0027 INVENTORY_MOVE with <c>FromInv=18</c> ("from mining window",
///         <c>PlayerConnection.cpp:3210 case 18</c>) -- the server reads the
///         target lock; an <c>OT_RESOURCE</c> target routes to
///         <c>MineResource(FromSlot)</c>.</item>
///   <item><c>Player::MineResource</c> (PlayerSkills.cpp) checks mining
///         conditions then emits 0x2012 START_PROSPECT to the range list
///         (self included) -- 20 bytes: PlayerGID, AsteroidGID, EffectUID,
///         ProspectTick, DrainMs.</item>
/// </list>
/// </para>
///
/// <para>
/// Transport: the harness flies via the proxy's MVAS position feed -- it sends
/// the 40-byte <c>FreyaClientPosDatagram</c> to the proxy's loopback intake port
/// 3807 (<see cref="ProxyPositionFeed"/>), EXACTLY as the in-client position hook
/// (FreyaPosFeed.dll) does, and the proxy re-emits 0x1004 MVAS_SEND_POSITION to
/// the server from its OWN socket. This keeps the avatar's registered IP == the
/// proxy across the move, so the proxy-relayed mine trigger
/// (REQUEST_TARGET + INVENTORY_MOVE over the sector TCP leg) is NOT dropped by
/// the server's anti-spoof guard (UDP_Client.cpp,
/// <c>PlayerIPAddr() == source_addr</c>). A DIRECT MVAS feed would instead
/// overwrite <c>m_Player_IPAddr</c> to the docker-gateway IP
/// (<c>HandleMVASPosReturn</c> -&gt; <c>SetPlayerPortIP</c>, unconditional) and
/// the subsequent proxy-relayed trigger would fail the guard. Because everything
/// stays on the proxy leg, the roid creates arrive as 0x0004 CREATE (proxy-
/// expanded from the server's 0x2019, create-type 38 == "resource") and the
/// prospect beam as the proxy-fabricated 0x000B OBJECT_TO_OBJECT_EFFECT -- the
/// exact bytes the Win32 client decodes. <see cref="SectorWorld"/> tracks both.
/// </para>
///
/// <para>
/// CLAUDE.md server-integrity: NO server change. The server already handles
/// REQUEST_TARGET + INVENTORY_MOVE(FromInv=18) + MineResource; this test only
/// drives the existing retail trigger and byte-pins the existing emission.
/// Prospect capability is granted the same way the retail server grants it --
/// an avatar_skill_levels row (SKILL_PROSPECT=41) on a prof=2 Explorer, loaded
/// at login (PlayerSaves.cpp). prof MUST be 2 or the server clamps the skill
/// to 0 at login.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorMiningTests : SectorIntegrationTest
{
    private const int ProgenExplorerStation = 10301; // Arx Prima (Mars Beta)
    private const int ProgenExplorerSpace = 1030;    // 10301 / 10
    private const int ProgenRace = 2;
    private const int ExplorerProf = 2;
    private const int SkillProspect = 41;       // SKILL_PROSPECT

    // "Asteroid Field 6" centre in sector 1030 (db net7.sector_objects, type=38,
    // sector_object_id=6651). The field spawns OT_RESOURCE roids within its
    // radius around this point.
    private static readonly (float X, float Y, float Z) FieldCentre = (-8919f, -9247f, 0f);

    // ProspectRange() = 750 + skill_level*250 -> 2500 at level 7 (PlayerClass.cpp).
    // Fly to within a margin under that before triggering.
    private const float ProspectRange = 2500f;
    private const float ApproachTarget = 1800f;

    private readonly ITestOutputHelper _out;

    public SectorMiningTests(ServerFixture server, ITestOutputHelper output)
        : base(server)
    {
        _out = output;
    }

    [RetryFact]
    public async Task Mine_Roid_ServerEmits_StartProspect_0x2012()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(220));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");

        // --- Stage 1: create the Progen Explorer (prof=2) and log it in once
        //     at its home station, then log off cleanly. The avatar persists. ---
        var stage1 = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, ProgenExplorerStation,
            firstName: "Korvae", shipName: "KorvaeRok", cts.Token,
            race: ProgenRace, profession: ExplorerProf);
        await stage1.DisposeAsync(); // 0x00B9 logoff -> DropPlayerFromGalaxy

        // --- Seed SKILL_PROSPECT so the second login loads it. ---
        await GrantProspectAsync(account.Username, slot, level: 7, explore: 150, cts.Token);

        // --- Stage 2: re-login (reads the skill via ReloadSavedData). ---
        var session = Track(await SectorHandshake.ReestablishAsync(
            _server, login.Ticket!, slot, ProgenExplorerStation, cts.Token));

        ProxyPositionFeed? feed = null;
        EncryptedTcpConnection? spaceConn = null;
        CancellationTokenSource? drainCts = null;
        Task? drain = null;
        var tcpFrames = new List<Packet>();
        try
        {
            // --- Undock + follow the handoff into the resource sector. ---
            byte[] launch = new byte[9];
            launch[8] = 1; // Action = 1 (exit station)
            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.StarbaseRequest.Value, launch), cts.Token);

            int toSectorId = -1;
            int fromSectorId = -1;
            for (int seen = 0; seen < 400; seen++)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                if (reply!.Header.Opcode == OpcodeId.Known.ServerHandoff.Value)
                {
                    toSectorId = BinaryPrimitives.ReadInt32BigEndian(reply.Payload.Span.Slice(20, 4));
                    fromSectorId = BinaryPrimitives.ReadInt32BigEndian(reply.Payload.Span.Slice(24, 4));
                    break;
                }
            }
            Assert.Equal(ProgenExplorerSpace, toSectorId);

            var ctx = new SessionContext(new OpcodeRegistry())
            {
                Host = _server.SectorHost,
                MasterPort = _server.MasterPort,
                SectorPort = _server.SectorPort,
                Ticket = login.Ticket,
                Username = account.Username,
            };
            var rejoin = await SectorEnterDriver.FollowHandoffAsync(
                ctx, session.GameId, slot, toSectorId, fromSectorId, cts.Token); // does START_ACK -> InSpace
            spaceConn = rejoin.Sector;
            await session.Sector.DisposeAsync();
            Assert.Equal(ProgenExplorerSpace, rejoin.SectorId);

            var world = new SectorWorld();
            foreach (var f in rejoin.HandshakeFrames) world.Ingest(f);

            // --- Background drain of the proxy TCP (sector) leg for the whole
            //     in-space phase. Everything we need fans out HERE: roids as
            //     0x0004 CREATE (proxy-expanded from the server's 0x2019,
            //     create-type 38 == "resource"), self-position via 0x0008/0x003E,
            //     and the prospect beam as the proxy-fabricated 0x000B. ---
            drainCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            drain = Task.Run(async () =>
            {
                try
                {
                    while (!drainCts.IsCancellationRequested)
                    {
                        var p = await spaceConn.ReceiveAsync(drainCts.Token);
                        if (p is null) break;
                        lock (tcpFrames) tcpFrames.Add(p);
                        world.Ingest(p);
                    }
                }
                catch (OperationCanceledException) { }
            });

            // Let the initial fanout + self-position settle.
            await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);

            var self = world.SelfSnapshot(rejoin.GameId).Pos;
            Assert.True(self is not null,
                "no own position after re-join; cannot drive the proxy position feed");
            var start = self!.Value;
            _out.WriteLine($"self ({start.X:0}, {start.Y:0}, {start.Z:0}); " +
                $"field ({FieldCentre.X:0}, {FieldCentre.Y:0}, {FieldCentre.Z:0})");

            // --- Proxy position feed: send FreyaClientPosDatagram to 127.0.0.1:3807,
            //     exactly as FreyaPosFeed.dll does; the proxy re-emits 0x1004 to the
            //     server from its OWN socket so the avatar's registered IP stays ==
            //     the proxy and the proxy-relayed mine trigger is not IP-dropped. ---
            feed = new ProxyPositionFeed(_server.SectorHost);

            // --- Fly toward the field; retarget onto the nearest roid as roids
            //     fan out (0x0004 type-38 on the proxy leg). Stop within range.
            //     The proxy only re-emits 0x1004 on a CHANGED position, so we vary
            //     the fed coordinate each tick. ---
            (float X, float Y, float Z) cur = start;
            SectorWorld.Tracked? roid = null;
            float roidDist = float.MaxValue;
            for (int i = 0; i < 160 && !cts.IsCancellationRequested; i++)
            {
                (roid, roidDist) = NearestResource(world, rejoin.GameId, cur);
                if (roid is not null && roidDist <= ApproachTarget) break;

                (float X, float Y, float Z) dst = roid is not null
                    ? (world.PositionOf(roid.GameId) ?? FieldCentre)
                    : FieldCentre;

                float dx = dst.X - cur.X, dy = dst.Y - cur.Y, dz = dst.Z - cur.Z;
                float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                (float X, float Y, float Z) head = dist < 1f ? (1f, 0f, 0f) : (dx / dist, dy / dist, dz / dist);
                if (dist >= 1f)
                {
                    float adv = MathF.Min(1500f, dist);
                    cur = (cur.X + head.X * adv, cur.Y + head.Y * adv, cur.Z + head.Z * adv);
                }
                feed.Send(cur, head, ProgenExplorerSpace);
                await Task.Delay(100, cts.Token);
            }

            (roid, roidDist) = NearestResource(world, rejoin.GameId, cur);
            Assert.True(roid is not null,
                "no OT_RESOURCE roid ever appeared in range -- the field did not spawn/send " +
                "roids on the proxy leg, the position feed never moved us into the field, or " +
                "0x0004 type-38 ingestion failed.");
            Assert.True(roidDist <= ProspectRange,
                $"nearest roid 0x{roid!.GameId:X8} '{roid.Name}' is {roidDist:0} units away, " +
                $"beyond ProspectRange {ProspectRange:0} -- cannot mine.");

            // Stop feeding position and let the server settle the ship as stationary
            // (the position feed is change-gated, so simply not sending more freezes
            // it; the change-gate also means no velocity was ever applied).
            await Task.Delay(TimeSpan.FromMilliseconds(1700), cts.Token);

            // Candidate roids in range, nearest first. A single roid can have an
            // empty/depleted ore stack (a prior run mined it out within this
            // server's uptime) and CheckMiningConditions then silently refuses
            // via a 0x0022 PUSH_MESSAGE (no server log). Try each in turn so the
            // test asserts the MECHANISM, not the luck of the nearest roid.
            var candidates = RoidsInRange(world, rejoin.GameId, cur, ProspectRange);
            Assert.NotEmpty(candidates);
            _out.WriteLine($"{candidates.Count} candidate roid(s) in range; nearest " +
                $"0x{candidates[0].Roid.GameId:X8} at {candidates[0].Dist:0} units");

            Packet? tcpBeam = null;
            SectorWorld.Tracked minedRoid = candidates[0].Roid;
            foreach (var (cand, candDist) in candidates)
            {
                minedRoid = cand;
                int baseline;
                lock (tcpFrames) baseline = tcpFrames.Count;
                _out.WriteLine($"trigger mine on roid 0x{cand.GameId:X8} '{cand.Name}' at {candDist:0} units");

                // --- 1. REQUEST_TARGET the roid (server-side target lock). ---
                byte[] target = new RequestTargetCodec().EncodeOutbound(
                    new RequestTargetMessage(rejoin.GameId, cand.GameId));
                await spaceConn.SendAsync(
                    Packet.ForOpcode(OpcodeId.Known.RequestTarget.Value, target), cts.Token);
                await Task.Delay(250, cts.Token);

                // --- 2. INVENTORY_MOVE FromInv=18 ("from mining window"), FromSlot=0
                //        (first ore stack) -> MineResource.
                //
                // These are the EXACT bytes the in-client mine/loot take builds
                // (enbmod l_loot_take: b[6]={gid, from_inv, slot, ToInv=1, ToSlot=-1,
                //  Num=1}) -- ToInv=1 (CargoInv), ToSlot=-1 (auto-select a free slot).
                // This doubles as the AJ-3 regression guard (plans/37, CV-BA-LOOT):
                // the AJ-3 destination-bounds check in HandleInventoryMove rejects a
                // move when dst_slots>0 && ToSlot<0, and InventorySlotCount(ToInv=1)=40.
                // Without the FromInv 6/18 `dest_not_indexed` exemption the take is
                // silently dropped BEFORE MineResource -- no 0x2012/0x000B beam -- and
                // this test fails. So it must send the client's real ToInv=1/ToSlot=-1,
                // NOT a synthetic ToInv=0 (which yields dst_slots=0 and never trips the
                // guard, passing on both the buggy and fixed server). ---
                byte[] mine = new InventoryMoveCodec().EncodeOutbound(
                    new InventoryMoveMessage(rejoin.GameId, FromInv: 18, FromSlot: 0,
                        ToInv: 1, ToSlot: -1, Num: 1));
                await spaceConn.SendAsync(
                    Packet.ForOpcode(OpcodeId.Known.InventoryMove.Value, mine), cts.Token);

                // --- Wait for the prospect beam. The server emits compact 0x2012
                //     to the range list (self included); on the proxy leg the
                //     player's own proxy EXPANDS 0x2012 -> 0x000B
                //     (UDPClient::StartProspecting), so the beam arrives here as
                //     0x000B. Watch the shared frame log (fed by the background
                //     drain) for a 0x000B that landed AFTER this trigger. ---
                using (var wait = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
                {
                    wait.CancelAfter(TimeSpan.FromSeconds(6));
                    while (!wait.IsCancellationRequested)
                    {
                        lock (tcpFrames)
                            tcpBeam = tcpFrames.Skip(baseline)
                                .FirstOrDefault(p => p.Header.Opcode == 0x000B);
                        if (tcpBeam is not null) break;
                        try { await Task.Delay(100, wait.Token); } catch { }
                    }
                }

                ushort[] postOps;
                lock (tcpFrames)
                    postOps = tcpFrames.Skip(baseline).Select(p => p.Header.Opcode)
                        .Distinct().OrderBy(x => x).ToArray();
                _out.WriteLine($"  post-mine opcodes: [{string.Join(",", postOps.Select(x => $"0x{x:X4}"))}]");

                // Surface any 0x0022 PUSH_MESSAGE -- CheckMiningConditions rejections
                // are delivered as a client-side message string (no server log), so
                // this is the only window into WHY a mine was refused.
                List<Packet> pushMsgs;
                lock (tcpFrames)
                    pushMsgs = tcpFrames.Skip(baseline).Where(p => p.Header.Opcode == 0x0022).ToList();
                foreach (var pm in pushMsgs)
                {
                    var txt = new string(System.Text.Encoding.Latin1.GetString(pm.Payload.ToArray())
                        .Where(c => c >= ' ' && c < 127).ToArray());
                    _out.WriteLine($"  PUSH_MESSAGE(0x0022) {pm.Payload.Length}B: \"{txt}\"");
                }

                if (tcpBeam is not null) break; // mined OK -> pin it
            }

            Assert.True(tcpBeam is not null,
                "no candidate roid in range yielded a proxy-fabricated 0x000B prospect beam -- " +
                "every CheckMiningConditions attempt was rejected (skill/energy/range/inventory/" +
                "moving) or no target was an OT_RESOURCE.");

            var roidMined = minedRoid; // the roid that actually mined, for the byte-pins below
            _out.WriteLine($"mined roid 0x{roidMined.GameId:X8} '{roidMined.Name}'");

            // --- Byte-pin the proxy-fabricated 0x000B OBJECT_TO_OBJECT_EFFECT.
            //     The sector server never sends the client-facing beam; it
            //     sends compact 0x2012 to the range list and each member's
            //     proxy expands it (UDPClient::StartProspecting,
            //     proxy/UDPProxyToClient_linux.cpp). Layout mirrors the
            //     authoritative range-list emitter
            //     Object::SendObjectToObjectEffectRL with Bitmask 0x0007
            //     (EffectID|TimeStamp|Duration), EffectDescID 0x00BF:
            //       u16 Bitmask=0x0007 @0
            //       i32 GameID  (prospector)@2
            //       i32 TargetID(roid)     @6
            //       u16 EffectDescID=0x00BF@10
            //       u8  Message NUL        @12
            //       i32 EffectID           @13
            //       u32 TimeStamp          @17
            //       i16 Duration           @21  -> 23 bytes total. ---
            var beam = tcpBeam!.Payload.Span;
            Assert.Equal(23, beam.Length);
            Assert.Equal(0x0007, BinaryPrimitives.ReadUInt16LittleEndian(beam.Slice(0, 2)));
            int beamSrc = BinaryPrimitives.ReadInt32LittleEndian(beam.Slice(2, 4));
            int beamTgt = BinaryPrimitives.ReadInt32LittleEndian(beam.Slice(6, 4));
            Assert.Equal(rejoin.GameId, beamSrc);   // beam emanates from the mining ship
            Assert.Equal(roidMined.GameId, beamTgt); // onto the locked roid
            Assert.Equal(0x00BF, BinaryPrimitives.ReadUInt16LittleEndian(beam.Slice(10, 2)));
            Assert.Equal(0, beam[12]);               // empty Message
            short beamDur = BinaryPrimitives.ReadInt16LittleEndian(beam.Slice(21, 2));
            Assert.True(beamDur > 0 && beamDur <= 32000,
                $"beam Duration {beamDur} must be a positive ms value capped at 32000 " +
                "(client reads it signed; the server emitter caps at 32000 so a long " +
                "mine still renders -- ObjectClass.cpp:884).");
            _out.WriteLine($"proxy-fabricated 0x000B beam ({beam.Length}B): " +
                $"src=0x{beamSrc:X8} tgt=0x{beamTgt:X8} " +
                $"fxDesc=0x{BinaryPrimitives.ReadUInt16LittleEndian(beam.Slice(10, 2)):X4} " +
                $"effectUID=0x{BinaryPrimitives.ReadInt32LittleEndian(beam.Slice(13, 4)):X8} " +
                $"dur={beamDur}ms");

            // The CLI's own decoder must consume the fabricated bytes to the
            // last byte (no over/under-read) -- proves CLI <-> proxy parity.
            var decoded = new N7.CliClient.Opcodes.Records.ObjectToObjectEffectRecord(
                tcpBeam.Payload.ToArray()).DumpToString();
            Assert.DoesNotContain("truncated", decoded);
            Assert.DoesNotContain("runs past payload", decoded);
        }
        finally
        {
            if (drainCts is not null) { try { drainCts.Cancel(); } catch { } }
            if (drain is not null) { try { await drain; } catch { } }
            feed?.Dispose();
            if (drainCts is not null) drainCts.Dispose();
            if (spaceConn is not null) { try { await spaceConn.DisposeAsync(); } catch { } }
        }
    }

    /// <summary>Nearest tracked OT_RESOURCE (TypeName "resource") to a point,
    /// with its distance; (null, MaxValue) if none known with a position.</summary>
    private static (SectorWorld.Tracked? Roid, float Dist) NearestResource(
        SectorWorld world, int selfGameId, (float X, float Y, float Z) from)
    {
        SectorWorld.Tracked? best = null;
        float bestD = float.MaxValue;
        foreach (var (o, _) in world.NearestTo(selfGameId))
        {
            if (SectorWorld.TypeName(o) != "resource") continue;
            if (world.PositionOf(o.GameId) is not { } p) continue;
            float d = MathF.Sqrt(
                (p.X - from.X) * (p.X - from.X) +
                (p.Y - from.Y) * (p.Y - from.Y) +
                (p.Z - from.Z) * (p.Z - from.Z));
            if (d < bestD) { bestD = d; best = o; }
        }
        return (best, bestD);
    }

    /// <summary>All tracked OT_RESOURCE roids with a known position within
    /// <paramref name="range"/> of <paramref name="from"/>, nearest first.</summary>
    private static List<(SectorWorld.Tracked Roid, float Dist)> RoidsInRange(
        SectorWorld world, int selfGameId, (float X, float Y, float Z) from, float range)
    {
        var list = new List<(SectorWorld.Tracked, float)>();
        foreach (var (o, _) in world.NearestTo(selfGameId))
        {
            if (SectorWorld.TypeName(o) != "resource") continue;
            if (world.PositionOf(o.GameId) is not { } p) continue;
            float d = MathF.Sqrt(
                (p.X - from.X) * (p.X - from.X) +
                (p.Y - from.Y) * (p.Y - from.Y) +
                (p.Z - from.Z) * (p.Z - from.Z));
            if (d <= range) list.Add((o, d));
        }
        list.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return list;
    }

    /// <summary>
    /// Grant SKILL_PROSPECT (41) at <paramref name="level"/> to the avatar on
    /// <paramref name="username"/>/<paramref name="slot"/> in net7_user, the
    /// same row the retail server loads at login (PlayerSaves.cpp). Mirrors the
    /// justfile <c>grant-prospect</c> recipe.
    /// </summary>
    private async Task GrantProspectAsync(
        string username, int slot, int level, int explore, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_server.PostgresConnectionString);
        await conn.OpenAsync(ct);

        int avatarId;
        await using (var find = new NpgsqlCommand(
            @"SELECT i.avatar_id FROM avatar_info i
              JOIN accounts a ON a.id = i.account_id
              WHERE a.username = @u AND i.slot = @s", conn))
        {
            find.Parameters.AddWithValue("u", username);
            find.Parameters.AddWithValue("s", slot);
            var got = await find.ExecuteScalarAsync(ct);
            Assert.True(got is not null,
                $"no avatar for account '{username}' slot {slot} -- stage-1 create failed");
            avatarId = (int)(long)got!;
        }

        await using (var ins = new NpgsqlCommand(
            @"INSERT INTO avatar_skill_levels (avatar_id, skill_id, skill_level)
              VALUES (@a, @sk, @lvl)
              ON CONFLICT (avatar_id, skill_id) DO UPDATE SET skill_level = EXCLUDED.skill_level;
              UPDATE avatar_info SET explore = @exp WHERE avatar_id = @a;", conn))
        {
            ins.Parameters.AddWithValue("a", avatarId);
            ins.Parameters.AddWithValue("sk", SkillProspect);
            ins.Parameters.AddWithValue("lvl", level);
            ins.Parameters.AddWithValue("exp", explore);
            await ins.ExecuteNonQueryAsync(ct);
        }
        _out.WriteLine($"granted Prospect({SkillProspect})={level} + explore={explore} to avatar {avatarId}");
    }
}
