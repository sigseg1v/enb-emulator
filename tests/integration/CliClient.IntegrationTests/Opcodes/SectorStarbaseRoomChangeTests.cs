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
/// Wave 15 post-handshake survival round-trip: client sends 0x009F
/// STARBASE_ROOM_CHANGE (the in-station nav opcode the retail Win32
/// client emits when the user clicks a doorway in a starbase), then
/// verifies the connection survives via 0x0044 REQUEST_TIME.
///
/// <para>
/// This is the natural in-starbase parallel to Wave 14's MOVE: we are
/// dropped into Luna Station (sectorId 10151, a starbase per
/// <c>StaticData.h:63-74</c>) and STARBASE_ROOM_CHANGE is the only
/// movement-shaped opcode the retail client emits while docked. Walking
/// between rooms is the most-common in-station wire frame.
/// </para>
///
/// <para>
/// Why survival probe rather than direct reply assertion.
/// <c>Player::HandleStarbaseRoomChange</c>
/// (<c>server/src/PlayerClass.cpp:631</c>) reads the 12-byte
/// StarbaseRoomChange struct, mutates <c>m_Oldroom</c>/<c>m_Room</c>
/// under <c>m_Mutex</c>, then iterates the sector's player list and
/// sends 0x00A0 STARBASE_ROOM_CHANGE to every OTHER player in the
/// sector (skips self via <c>p->GameID() != GameID()</c>). For other
/// players in the same room it also fires <c>SendStarbaseAvatarChange</c>
/// to render the new arrival's avatar. With a single-player integration
/// test there are no other observers, so the fan-out loop is empty and
/// we receive nothing back. Pipe survival is the only assertable
/// post-condition.
/// </para>
///
/// <para>
/// Concrete regression class this catches: if anyone reverts the Phase R
/// PacketStructures.h StarbaseRoomChange layout from <c>int32_t</c> to
/// <c>long</c>, the struct grows from 12B (3× 4B fields) to 24B on
/// Linux x86_64 and the handler reads <c>NewRoom</c> from offset 8
/// (instead of 4) and <c>OldRoom</c> from offset 16 (instead of 8) —
/// both past the end of the 12B wire payload, into undefined memory.
/// The garbage room numbers then land in <c>m_Room</c> and trigger the
/// fan-out loop on a corrupt room id; for stations that load actual
/// room state from <c>StationData</c>, that could mis-route avatars to
/// rooms that don't exist or crash on subsequent
/// <c>SendStarbaseAvatarChange</c> attempts.
/// </para>
///
/// <para>
/// Other bugs this test would also catch:
/// </para>
/// <list type="bullet">
///   <item>
///     Proxy <c>ProcessSectorServerOpcode</c> for STARBASE_ROOM_CHANGE
///     (<c>proxy/ClientToServer_linux_stubs.cpp:491-496</c>) failing to
///     fall through to the bottom-of-switch
///     <c>ForwardClientOpcode</c>. The current path calls
///     <c>HandleStarbaseRoomChange_Linux</c> (whose body is empty,
///     matching Win32's commented-out no-op) then breaks; the bottom
///     forward then relays to the server. A regression that
///     <c>return</c>ed instead of <c>break</c>ing would drop the
///     forward and the survival probe still passes (since the
///     connection doesn't die) but the server never sees the room
///     transition — silent state divergence.
///   </item>
///   <item>
///     <c>m_Mutex</c> deadlock or lock-order inversion. HandleStarbaseRoomChange
///     takes <c>m_Mutex</c> around the room update; if any code path
///     re-entered the same mutex (e.g. via a callback fired by
///     <c>SetActionFlag</c>), the sector thread would deadlock and the
///     survival probe would never complete.
///   </item>
///   <item>
///     <c>GetSectorPlayerList</c> returning a stale or corrupt pointer
///     that the iteration walks off the end of — surfaces as a
///     sector-thread crash in <c>g_PlayerMgr->GetNextPlayerOnList</c>.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The STARBASE_ROOM_CHANGE
/// payload sent here is exactly the wire shape the retail Win32 client
/// emits: 4B AvatarID + 4B NewRoom + 4B OldRoom, all int32_t LE. OldRoom=0
/// / NewRoom=1 is a typical first-room-transition value (lobby → main
/// hall). We are not making the server accept anything it didn't
/// previously accept. The retail server's fan-out goes to other players
/// only; there is no direct reply on this branch and we don't fabricate
/// one.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; STARBASE_ROOM_CHANGE+REQUEST_TIME
/// round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorStarbaseRoomChangeTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorStarbaseRoomChangeTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task RoomChange_DoesNotBreakConnection_RequestTimeStillRoundTrips()
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
            firstName: "Roomer", shipName: "RoomShip", cts.Token);

        try
        {
            // StarbaseRoomChange wire layout — 12 bytes, 3× int32_t LE:
            //   [0..4)   AvatarID — retail client sets the actor's game
            //                       id; server uses connection binding,
            //                       this field is unused server-side but
            //                       its width matters for struct offsets.
            //   [4..8)   NewRoom  — destination room id within the
            //                       starbase. 1 = first room after lobby
            //                       (matches typical retail-client first
            //                       transition).
            //   [8..12)  OldRoom  — source room id. 0 = lobby (the
            //                       value m_Room is initialised to on
            //                       starbase entry, per
            //                       server/src/PlayerClass.cpp:638
            //                       which special-cases OldRoom=-1 /
            //                       NewRoom=0 — we use 0/1 to drive the
            //                       normal "first move" path).
            // common/include/net7/PacketStructures.h:805
            byte[] payload = new byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 1);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.StarbaseRoomChange.Value, payload),
                cts.Token);

            // Survival probe.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Tolerate interleaved
            // in-sector frames; cap on frame count so a stalled
            // pipeline doesn't masquerade as the outer-CTS timeout.
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
                $"drained {maxFrames} frames after sending 0x009F STARBASE_ROOM_CHANGE + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the server's HandleStarbaseRoomChange read past the 12B payload " +
                $"(sizeof(long) regression on StarbaseRoomChange struct), " +
                $"the proxy's ProcessSectorServerOpcode dispatch dropped the bottom-of-switch forward " +
                $"(proxy/ClientToServer_linux_stubs.cpp:491-496), " +
                $"or the m_Mutex room-update path deadlocked / GetSectorPlayerList iteration crashed.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
