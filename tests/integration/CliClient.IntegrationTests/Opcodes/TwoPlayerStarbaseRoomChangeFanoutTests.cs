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
/// Two-player peer-fan-out pin for 0x00A0 STARBASE_ROOM_CHANGE: when
/// Player A sends 0x009F STARBASE_ROOM_CHANGE the server fans out
/// 0x00A0 STARBASE_ROOM_CHANGE to every OTHER player in the same
/// sector (the self-skip is enforced at
/// <c>server/src/PlayerClass.cpp:660</c> -- <c>p-&gt;GameID() !=
/// GameID()</c>). The existing single-player
/// <see cref="SectorStarbaseRoomChangeTests"/> can only verify that
/// the server doesn't crash on receipt; it can't witness the fan-out
/// because there's no second observer. This test establishes two
/// sessions in the same starbase sector and pins the byte-exact
/// 0x00A0 frame Player B receives after Player A sends 0x009F.
/// </summary>
/// <remarks>
/// <para>
/// Wire shape pinned (<c>common/include/net7/PacketStructures.h:805</c>):
/// </para>
/// <list type="bullet">
///   <item><c>[0..4)</c>  AvatarID -- the mover's GameID (Player A).</item>
///   <item><c>[4..8)</c>  NewRoom  -- 1 (taken from the client send).</item>
///   <item><c>[8..12)</c> OldRoom  -- whatever <c>m_Room</c> was on
///         Player A's Player object BEFORE the room-change handler
///         ran. The handler captures it via <c>m_Oldroom = m_Room</c>
///         under <c>m_Mutex</c> (PlayerClass.cpp:644). For a
///         freshly-spawned starbase character that value is the
///         default-initialised room id; we don't pin it to a specific
///         value because the spawn-time room initialisation is
///         starbase-data-driven and may legitimately differ across
///         StaticData revisions. We DO pin that the field is some
///         well-defined int32_t and that bytes 0..8 are exact.</item>
/// </list>
///
/// <para>
/// Regression class this catches that the single-player test
/// cannot:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Self-skip inversion.</b> If anyone flips
///     PlayerClass.cpp:660 to <c>p-&gt;GameID() == GameID()</c> the
///     mover would receive their OWN room-change frame and other
///     players would receive nothing. The single-player test still
///     passes because it never asserts on any 0x00A0 frame; this
///     test would fail two ways: (1) Player B sees no 0x00A0,
///     (2) Player A sees an unexpected 0x00A0 echo.
///   </item>
///   <item>
///     <b>Sector-list iteration corruption.</b>
///     <c>g_PlayerMgr-&gt;GetNextPlayerOnList</c> walking off the end
///     or skipping live players. The single-player test sees no
///     fan-out (correct for one player); a regression that emptied
///     the iteration would look identical. With two players the
///     iteration MUST visit the second player and we'd notice if
///     it didn't.
///   </item>
///   <item>
///     <b>0x00A0 wire-frame regression.</b> The fanout packet is
///     built from a server-side <c>StarbaseRoomChange</c> struct
///     (PlayerClass.cpp:649-652) that's filled by-name: AvatarID =
///     GameID(), OldRoom = m_Oldroom, NewRoom = m_Room. Wire field
///     ORDER is AvatarID, NewRoom, OldRoom (the struct's declared
///     order at PacketStructures.h:805-810). If someone reorders the
///     struct or the by-name assignment without updating both, the
///     pin diverges immediately.
///   </item>
///   <item>
///     <b>Phase R <c>sizeof(long)</c> regression.</b> If someone
///     reverts StarbaseRoomChange's fields from <c>int32_t</c> to
///     <c>long</c>, the server sends 24 bytes on Linux while Player
///     B's client expects 12. The header-length field (or the
///     post-payload framing) breaks and Player B either gets a
///     header mismatch or no frame at all.
///   </item>
/// </list>
///
/// <para>
/// Two-player infrastructure cost. We provision two TestAccounts,
/// each with a unique first_name (the server enforces global
/// avatar_data.first_name uniqueness via AccountManager.cpp:316;
/// both names contain a vowel per the G_ERROR_ONE_VOWEL=4 rule),
/// and call <see cref="SectorHandshake.EstablishAsync"/> twice
/// against the same sector. Each session owns its own TCP
/// connections; xUnit's serial collection
/// (<see cref="ServerCollection"/>) prevents other tests from
/// stepping on either player mid-test.
/// </para>
///
/// <para>
/// <b>BLOCKED by proxy single-tenancy.</b> The Net7Proxy that
/// terminates our TCP on PROXY_LOCAL_TCP_PORT (3500) was lifted
/// straight from the Win32 client launcher, where one proxy
/// process serves one client. The proxy state -- including
/// <c>g_ServerMgr-&gt;m_UDPClient</c>, <c>m_MasterConnection</c>,
/// and the LOGIN_STAGE auto-ACK path in
/// <c>UDPClient::HandleStageConfirm</c>
/// (proxy/UDPProxyToClient_linux.cpp:600-628) -- is global state,
/// set most-recently-wins by every new MasterJoin
/// (proxy/ClientToMasterServer.cpp:104:
/// <c>g_ServerMgr-&gt;m_MasterConnection = this;</c>). When Player
/// B's MasterJoin lands, Player A's auto-ACK path is clobbered;
/// the server keeps retrying its 0x2020 LOGIN_STAGE_S_C for A
/// (server log: "Re-send Ack request 9 for Cypria") until A
/// times out at stage 9 and is removed from the galaxy. Even if
/// we serialise the handshakes, the second handshake overwrites
/// the first's UDP routing. This was verified empirically
/// (2026-05-29) -- see attempt-log: Cypria login stage 9 timeout
/// followed by Eboria login stage 12 timeout in the same run.
/// Unskipping requires the proxy to demultiplex by session
/// (one UDPClient instance per accepted TCP connection, not the
/// single boot-time <c>UDPClient udp_to_global</c> at
/// proxy/Net7.cpp:264) -- a substantial refactor of
/// ServerManager / UDPClient / Connection that would replace
/// every <c>g_ServerMgr-&gt;m_UDPClient</c> singleton access with
/// a per-session lookup. Out of scope for this opcode-coverage
/// pass; tracked separately. CLAUDE.md server-integrity rule:
/// the fix lives in the PROXY, not the server -- the server is
/// already multi-tenant (its sector lists are per-player); the
/// proxy is the bottleneck.
/// </para>
///
/// <para>
/// Budget: 200s. Each handshake takes ~2s, and we run two; the
/// fan-out itself is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class TwoPlayerStarbaseRoomChangeFanoutTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public TwoPlayerStarbaseRoomChangeFanoutTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    private const string ProxySingleTenancySkip =
        "BLOCKED by Net7Proxy single-tenancy: the proxy global state " +
        "(g_ServerMgr->m_UDPClient, m_MasterConnection, LOGIN_STAGE auto-ACK " +
        "path at proxy/UDPProxyToClient_linux.cpp:600-628) is set " +
        "most-recently-wins by every MasterJoin (proxy/ClientToMasterServer.cpp:104). " +
        "Player B's handshake clobbers Player A's UDP routing; A times out at " +
        "login stage 9. Verified 2026-05-29. Unskip requires per-session " +
        "UDPClient demultiplexing in the proxy -- a substantial refactor " +
        "of ServerManager / UDPClient / Connection. The test code shape " +
        "is correct and will work as-is once the proxy multiplexes.";

    [Fact(Skip = ProxySingleTenancySkip)]
    public async Task PlayerA_SendsRoomChange_PlayerB_ReceivesFanoutWithMoverGameId()
    {
        var accountA = TestAccounts.New(_server);
        var accountB = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Luna Station: Terran Warrior start, a starbase.
        const int newRoom = 1;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(200));

        var loginA = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(accountA.Username, accountA.Password), cts.Token);
        Assert.True(loginA.Valid, $"loginA: {loginA.RawBody.TrimEnd()}");

        var loginB = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(accountB.Username, accountB.Password), cts.Token);
        Assert.True(loginB.Valid, $"loginB: {loginB.RawBody.TrimEnd()}");

        await using var sessionA = await SectorHandshake.EstablishAsync(
            _server, loginA.Ticket!, accountA.Username, slot, sectorId,
            firstName: "Cypria", shipName: "CypriaShip", cts.Token);

        await using var sessionB = await SectorHandshake.EstablishAsync(
            _server, loginB.Ticket!, accountB.Username, slot, sectorId,
            firstName: "Eboria", shipName: "EboriaShip", cts.Token);

        try
        {
            Assert.NotEqual(sessionA.GameId, sessionB.GameId);

            // Inbound 0x009F payload (StarbaseRoomChange, 12B, 3x int32_t LE).
            // common/include/net7/PacketStructures.h:805
            byte[] payload = new byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), sessionA.GameId);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), newRoom);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 0);

            var roomChangePacket = Packet.ForOpcode(
                OpcodeId.Known.StarbaseRoomChange.Value, payload);

            // Login-stage 10 race: EstablishAsync returns at 0x0005 START
            // (login stage 7, PlayerManager.cpp:540-604 state machine).
            // Player A's HandleStarbaseRoomChange (PlayerClass.cpp:631-672)
            // walks GetSectorPlayerList -- which is all-zeros for Player B
            // until stage 10's HandleSectorLogin3 -> AddPlayerToSectorList
            // (SectorManager.cpp:307-322) fires. During the race window the
            // self-skip-and-fanout loop iterates an empty bitmap and no
            // 0x00A0 reaches Player B. Retry-send is idempotent: each
            // 0x009F updates A's m_Room/m_Oldroom under m_Mutex
            // (PlayerClass.cpp:643-647) and triggers ANOTHER fan-out
            // attempt; once stage 10 lands on BOTH players the next send
            // hits the populated bitmap and Player B sees the frame. We
            // do NOT modify the server to expose a "fully logged in"
            // signal (CLAUDE.md server-integrity rule). Same pattern as
            // SectorAvatarEmoteTests.
            TimeSpan attemptTimeout = TimeSpan.FromSeconds(2);
            const int maxAttempts = 60;
            int attempt;
            bool seenFanout = false;

            for (attempt = 0; attempt < maxAttempts && !seenFanout; attempt++)
            {
                await sessionA.Sector.SendAsync(roomChangePacket, cts.Token);

                using var attemptCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                attemptCts.CancelAfter(attemptTimeout);

                try
                {
                    while (true)
                    {
                        var reply = await sessionB.Sector.ReceiveAsync(attemptCts.Token);
                        Assert.NotNull(reply);

                        if (reply!.Header.Opcode != OpcodeId.Known.StarbaseRoomChangeServerToClient.Value)
                            continue;

                        var span = reply.Payload.Span;
                        Assert.Equal(12, span.Length);

                        int wireAvatarId = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);
                        int wireNewRoom  = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4));
                        // OldRoom (bytes [8..12)) intentionally not pinned -
                        // reflects Player A's spawn-time m_Room which is
                        // StationData-driven.

                        Assert.Equal(sessionA.GameId, wireAvatarId);
                        Assert.Equal(newRoom, wireNewRoom);
                        seenFanout = true;
                        break;
                    }
                }
                catch (OperationCanceledException) when (!cts.IsCancellationRequested)
                {
                    // Attempt timed out -- either both players aren't on
                    // the sector list yet (stage 10 race) or this fan-out
                    // raced ahead of the next pulse and got dropped. Retry.
                }
            }

            if (!seenFanout)
            {
                throw new Xunit.Sdk.XunitException(
                    $"sent 0x009F STARBASE_ROOM_CHANGE {attempt} times " +
                    $"(2s attempt window) without Player B seeing the 0x00A0 fan-out. " +
                    $"PlayerA.GameID=0x{sessionA.GameId:X8}, PlayerB.GameID=0x{sessionB.GameId:X8}. " +
                    $"Likely the sector-list iteration in HandleStarbaseRoomChange " +
                    $"(PlayerClass.cpp:631-672) skipped Player B, the self-skip guard " +
                    $"inverted, the proxy dropped the inbound 0x009F, or the login-stage " +
                    $"state machine never reached stage 10 (AddPlayerToSectorList) for one " +
                    $"or both players within the outer budget.");
            }

            // Self-skip check: Player A must NOT receive the 0x00A0 fanout
            // from their OWN room change. We can't drain Player A forever,
            // so use a 0x0044 REQUEST_TIME sentinel: the server's
            // HandleRequestTime echoes 0x0034 CLIENT_SET_TIME unconditionally
            // (no sector-list dependency), so seeing 0x0034 proves the pipe
            // is alive past the fan-out window. If 0x00A0 appears on A's
            // pipe before 0x0034 the self-skip guard has inverted.
            int sentinelTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));
            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, sentinelTick);
            await sessionA.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            int aFramesSeen = 0;
            const int maxAFrames = 400;
            bool sawSelfEcho00A0 = false;
            while (aFramesSeen++ < maxAFrames)
            {
                var reply = await sessionA.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode == OpcodeId.Known.StarbaseRoomChangeServerToClient.Value)
                {
                    sawSelfEcho00A0 = true;
                    continue;
                }

                if (reply.Header.Opcode != OpcodeId.Known.ClientSetTime.Value)
                    continue;

                int echoedTick = BinaryPrimitives.ReadInt32LittleEndian(reply.Payload.Span[..4]);
                Assert.Equal(sentinelTick, echoedTick);
                Assert.False(sawSelfEcho00A0,
                    "Player A received 0x00A0 STARBASE_ROOM_CHANGE for their own room change -- " +
                    "the self-skip guard at PlayerClass.cpp:660 has inverted.");
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxAFrames} frames on Player A's pipe without seeing the 0x0034 " +
                $"CLIENT_SET_TIME sentinel; cannot verify the self-skip guard.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(sessionA.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
            try { await SectorHandshake.DeleteCreatedCharacterAsync(sessionB.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
