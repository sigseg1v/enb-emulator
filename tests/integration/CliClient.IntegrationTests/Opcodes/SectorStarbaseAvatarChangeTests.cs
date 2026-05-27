// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 36 post-handshake survival round-trip: client sends 0x009D
/// STARBASE_AVATAR_CHANGE for an unknown AvatarID, then verifies the
/// connection survives via 0x0044 REQUEST_TIME.
///
/// <para>
/// Wire layout (mirror of <c>common/include/net7/PacketStructures.h:787-794</c>).
/// </para>
/// <code>
///   [0..4)   int32 AvatarID
///   [4..8)   int32 RoomType
///   [8..12)  float Orient
///   [12..24) float Position[3]
///   [24..28) int32 ActionFlag
/// </code>
/// <para>
/// 28 bytes total; <c>ATTRIB_PACKED</c> so no implicit padding.
/// </para>
///
/// <para>
/// Server handler. After the dispatcher case at
/// <c>server/src/PlayerConnection.cpp:575</c> calls
/// <c>HandleStarbaseAvatarChange(data)</c>, the handler
/// (<c>server/src/PlayerClass.cpp:587-629</c>) casts
/// <c>data</c> to a <c>StarbaseAvatarChange*</c>, calls
/// <c>g_PlayerMgr-&gt;GetPlayer(change-&gt;AvatarID)</c>, and
/// <b>early-returns</b> when the lookup yields null. By sending an
/// AvatarID that no seeded player owns (<c>0x12345678</c> — well above
/// the <c>account_id*5+slot+1</c> range any of our test accounts can
/// hash into) we deterministically hit that early-return path: zero
/// mutex contention, zero shared-state mutation, zero observer fan-out.
/// </para>
///
/// <para>
/// Why this wave target. The remaining client→server dispatch arms in
/// <c>PlayerConnection.cpp</c>'s switch-block (lines 423-618) split
/// into two groups: handlers with verifiably safe fresh-character
/// paths (early-return / out-of-range guard / no-op), and handlers
/// with known-crashing or risky paths (per the Wave 29 / 30
/// triage). <c>HandleStarbaseAvatarChange</c> sits in the first group:
/// the only deref before the null-check is <c>change-&gt;AvatarID</c>
/// at offset 0, which is fully covered by our 28-byte canonical
/// payload. Compare with the Wave 29 doc-comment's explicit triage of
/// 0x008D INCAPACITANCE_REQUEST (mutates shared <c>NPCs-&gt;Avatar</c>
/// state and SEGVs on first call), 0x009B WARP (iterates formation
/// state on a fresh char with no group), 0x005D EQUIP_USE (derefs an
/// empty <c>m_Equip</c> array). 0x009D is the cleanest unused arm:
/// one null-check gate, one early-return.
/// </para>
///
/// <para>
/// No post-emit side effects. The if-branch beyond the null-check
/// runs only when GetPlayer returns non-null; for the unknown-AvatarID
/// case the handler returns before touching the mutex, before
/// position/orientation/action-flag mutation, and before the
/// <c>SendStarbaseAvatarList</c> / <c>BroadcastPosition</c>
/// fan-out at lines 615-627. Same favourable post-emit shape as Wave
/// 27 (INVENTORY_MOVE default arm) and Wave 29 (PETITION_STUCK).
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>StarbaseAvatarChange struct long-revert in
///     PacketStructures.h:787-794.</b> Currently 5 fields × 4B = 20B
///     for the fixed portion plus the 12B Position[3] = 28B canonical.
///     Widening any of the three int32 fields to <c>long</c> on Linux
///     x86_64 (sizeof(long)==8) grows the struct — the handler's
///     <c>change-&gt;AvatarID</c> read still hits offset 0, but
///     downstream observers parsing the broadcast frame would see
///     misaligned fields. The unknown-AvatarID early-return path
///     doesn't itself exercise downstream parsing, so this catches
///     only the AvatarID offset-0 invariant (which is the load-bearing
///     part for the null-check).
///   </item>
///   <item>
///     <b>Dispatcher mis-route at server/src/PlayerConnection.cpp:575.</b>
///     The 0x009D case label sits between 0x009B WARP (known-risky
///     formation walk) and 0x009F STARBASE_ROOM_CHANGE. A copy-paste
///     swap with the WARP arm would route our 28-byte payload through
///     <c>HandleWarp</c>, which on a fresh char attempts to fetch
///     formation members via <c>g_PlayerMgr-&gt;GetMemberID</c> and
///     run <c>SetupWarpNavs</c> — uncertain but very likely to crash
///     or hang on our unseeded character state. Test surfaces this
///     as a REQUEST_TIME timeout.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode regression.</b>
///     0x009D is NOT explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp:387-518</c>'s
///     <c>ProcessSectorServerOpcode</c> switch, so it falls through
///     to the bottom-of-switch ForwardClientOpcode at line 524. A
///     regression dropping the default-case forward would mean the
///     server never sees the avatar-change frame and the handler
///     never runs (so the test still passes via REQUEST_TIME, which
///     IS forwarded explicitly via the proxy's REQUEST_TIME arm).
///     The diagnostic loss is silent for this opcode — but the same
///     class is caught by the petition-stuck / mission-forfeit tests
///     so a default-arm regression would surface there first.
///   </item>
///   <item>
///     <b>HandleStarbaseAvatarChange null-check removal.</b> The
///     <c>if (p == (0)) { return; }</c> guard at PlayerClass.cpp:596
///     is what makes this opcode safe on a fresh char. A refactor
///     that dropped the early-return would skip into the mutex-lock /
///     <c>p-&gt;m_PlayerIndex.SetSectorNum</c> / <c>p-&gt;SetPosition</c>
///     / <c>p-&gt;m_Orient = ...</c> chain on a NULL <c>p</c> —
///     SEGV. Test surfaces this as a REQUEST_TIME timeout.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at PlayerConnection.cpp:127.</b>
///     The Phase K sizeof(int32_t) opcode-header fix keeps the
///     per-client UDP queue header at the canonical 4-byte width;
///     a revert would corrupt the 0x2016 inner-tuple parser and
///     break the REQUEST_TIME reply path. Same load-bearing
///     SendOpcode invariant as Waves 8/24/26/27/29/30.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x009D STARBASE_AVATAR_CHANGE
/// is what the retail Win32 client emits when the user clicks an
/// avatar position-update terminal in a starbase room. The 28-byte
/// canonical wire shape is byte-identical to retail. The
/// unknown-AvatarID early-return is what the retail server does too
/// (the lookup fails for any AvatarID the server doesn't have in its
/// active-player map). Zero permissiveness added; not loosening any
/// security posture; not fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; STARBASE_AVATAR_CHANGE + REQUEST_TIME
/// round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorStarbaseAvatarChangeTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorStarbaseAvatarChangeTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task StarbaseAvatarChange_OnUnknownAvatarId_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        // cli_test33 — Pool[31]. Dedicated to this wave so its
        // Create/Delete cycle doesn't collide with Pool slots owned
        // by earlier waves. seed.sql carries the matching 9_000_033
        // row.
        var account = TestAccounts.Pool[31];
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Avchnge", shipName: "AvchngeShip", cts.Token);

        try
        {
            // 0x009D STARBASE_AVATAR_CHANGE — 28B canonical payload.
            //   [0..4)   int32 AvatarID    = 0x12345678 (unknown — null-check trips)
            //   [4..8)   int32 RoomType    = 0
            //   [8..12)  float Orient      = 0.0f
            //   [12..24) float Position[3] = 0.0f x 3
            //   [24..28) int32 ActionFlag  = 0
            byte[] payload = new byte[28];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0x12345678);
            // Remaining 24 bytes already zero from new byte[28].

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.StarbaseAvatarChange.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the
            // avatar-change handler? Send REQUEST_TIME and assert
            // CLIENT_SET_TIME echoes our sentinel tick.
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
                $"drained {maxFrames} frames after sending 0x009D STARBASE_AVATAR_CHANGE + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the server's HandleStarbaseAvatarChange null-check at PlayerClass.cpp:596 was removed " +
                $"(would SEGV on p->m_PlayerIndex.SetSectorNum), " +
                $"the dispatcher case at PlayerConnection.cpp:575 got mis-routed to HandleWarp at line 571 " +
                $"(formation-walk on a fresh char with no group), " +
                $"the StarbaseAvatarChange struct in PacketStructures.h:787-794 was widened to long " +
                $"(AvatarID offset shifted past the 28B payload), " +
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
