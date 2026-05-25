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
/// Wave 19 post-handshake direct-reply round-trip: client sends 0x0017
/// REQUEST_TARGET (the wire frame the retail Win32 client emits when the
/// user clicks an in-world object to make it the active target), then
/// asserts on the server's mandatory 0x0019 SET_TARGET reply.
///
/// <para>
/// Why a direct-reply assertion (stronger than Waves 16/17/18 survival
/// probes). <c>Player::HandleRequestTarget</c>
/// (<c>server/src/PlayerConnection.cpp:3390</c>) <i>always</i> calls
/// <c>SendSetTarget(request->TargetID, -1)</c> regardless of whether the
/// requested target exists or whether the request makes sense. From the
/// handler:
/// <code>
///     RequestTarget * request = (RequestTarget *) data;
///     newtarget = obj_manager-&gt;GetObjectFromID(request-&gt;TargetID);
///     oldtarget = obj_manager-&gt;GetObjectFromID(ShipIndex()-&gt;GetTargetGameID());
///     // ...
///     SendSetTarget(request-&gt;TargetID, -1);
///     ShipIndex()-&gt;SetTargetGameID(request-&gt;TargetID);
///     BlankVerbs();
///     // ... per-target threat-rank logic guarded on newtarget != null
/// </code>
/// For TargetID=0: <c>GetObjectFromID(0)</c> returns null for both
/// newtarget and oldtarget; <c>m_ProspectWindow</c> is false on a fresh
/// starbase character so the OpenInterface branch is skipped; the
/// SendSetTarget reply fires unconditionally; <c>BlankVerbs()</c> is a
/// safe <c>memset(m_Verbs, 0, ...)</c>; the threat-rank logic is gated
/// on <c>newtarget != null</c> so it's skipped. The mandatory reply
/// makes this a *positive-correlation* test instead of a *survival
/// probe* — we can directly assert the SET_TARGET TargetID field came
/// back as -1 (the literal value <c>SendSetTarget</c> hard-codes).
/// </para>
///
/// <para>
/// Reply wire shape. <c>SendSetTarget</c>
/// (<c>server/src/PlayerConnection.cpp:3663</c>) emits 8 bytes:
/// <c>{int32_t GameID; int32_t TargetID;}</c> per
/// <c>common/include/net7/PacketStructures.h:540</c>. With our request
/// having TargetID=0, the reply carries GameID=0 (=
/// <c>request-&gt;TargetID</c>) and TargetID=-1 (the literal). Both
/// fields are assertable.
/// </para>
///
/// <para>
/// Concrete regression class this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>RequestTarget long-revert.</b> Currently 2× int32_t = 8B
///     canonical. If GameID is widened to <c>long</c>, the struct grows
///     to 12B on Linux x86_64; TargetID reads from byte 8 (past the
///     end of the 8B wire payload) into undefined memory. A non-zero
///     garbage TargetID would walk the threat-rank path on a stale
///     newtarget pointer — could crash or echo garbage.
///   </item>
///   <item>
///     <b>SetTarget reply long-revert.</b> Currently 2× int32_t = 8B
///     canonical. If GameID or TargetID is widened to <c>long</c>, the
///     reply grows to 12B/16B. Test asserts payload length is exactly
///     8 — would catch this immediately.
///   </item>
///   <item>
///     <b>Reply field-order swap.</b> If the SetTarget struct fields
///     are reordered (GameID/TargetID swap), our assert that
///     <c>TargetID == -1</c> fails — the literal -1 would appear in
///     the GameID slot instead.
///   </item>
///   <item>
///     <b>Proxy default-case <c>ForwardClientOpcode</c> regression.</b>
///     REQUEST_TARGET is not explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>, so it hits the
///     <c>default:</c> arm at line 514 and falls through to the
///     bottom-of-switch <c>ForwardClientOpcode</c>. A regression that
///     <c>return</c>ed early or added an empty hand-coded case that
///     returned would silently drop the opcode — we'd time out waiting
///     for SET_TARGET instead of failing on a content assert.
///   </item>
///   <item>
///     <b>Server→client UDP fan-out regression.</b> The reply rides
///     <c>Player::SendOpcode</c> →
///     <c>SendPacketCache</c> →
///     <c>0x2016 PACKET_SEQUENCE</c> on UDP →
///     <c>UDPClient::SendClientPacketSequence</c>
///     (<c>proxy/UDPProxyToClient_linux.cpp:531</c>) →
///     <c>SendResponse</c> over TCP. Same path as
///     Wave 8's MESSAGE_STRING — a regression in
///     <c>SendClientPacketSequence</c> that affected only this opcode
///     (e.g. on a different sub-branch of the framing logic) would
///     surface as a timeout here even though Wave 8 still passed.
///   </item>
///   <item>
///     <b>Dispatch mis-route.</b> The case label at
///     <c>server/src/PlayerConnection.cpp:443</c> is hand-maintained
///     in a ~200-entry switch; a copy-paste error could route 0x0017
///     to a different handler that crashes on the 8-byte payload or
///     emits a different opcode in reply.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The RequestTarget payload
/// sent here is the exact wire shape the retail Win32 client emits
/// when the user clicks an in-world object to make it the active
/// target: 4B GameID + 4B TargetID. TargetID=0 is the value the
/// retail client sends to clear the active target (click on empty
/// space). The retail server's <i>unconditional</i> SetTarget reply
/// with TargetID=-1 is the faithful behaviour — the -1 sentinel means
/// "no rank/threat info yet, client should re-request via
/// <c>0x0018 REQUEST_TARGETS_TARGET</c>". We are not making the server
/// accept any new input shape, and we don't fabricate a reply — the
/// 0x0019 emit is what retail did.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; REQUEST_TARGET → SET_TARGET round-trip
/// is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorRequestTargetTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorRequestTargetTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task RequestTarget_OnNullTarget_ReceivesSetTargetWithSentinelTargetIdMinusOne()
    {
        // cli_test16 — Pool[14]. Dedicated to this test so its
        // Create/Delete cycle doesn't collide with Pool[3..13] which
        // are owned by SectorLogin / SectorChat / SectorRequestTime /
        // SectorStartAck / SectorTurnTilt / SectorAction / SectorMove /
        // SectorStarbaseRoomChange / SectorStarbaseRequest / SectorSkillUp /
        // SectorVerbRequest respectively.
        var account = TestAccounts.Pool[14];
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Targeter", shipName: "TargetShip", cts.Token);

        try
        {
            // RequestTarget wire layout — 8 bytes:
            //   [0..4)   int32 LE  GameID    — actor's own avatar id;
            //                                   retail client sets this
            //                                   to the player's own
            //                                   GameID. We send 0 — the
            //                                   field is read but not
            //                                   used on the SetTarget
            //                                   reply path (the reply's
            //                                   GameID is mirrored from
            //                                   request->TargetID, not
            //                                   request->GameID).
            //   [4..8)   int32 LE  TargetID  — the requested target
            //                                   object's GameID. We send
            //                                   0 — the retail client
            //                                   sends this when the user
            //                                   clicks on empty space to
            //                                   clear the active target.
            //                                   GetObjectFromID(0)
            //                                   returns null → newtarget
            //                                   stays null → threat-rank
            //                                   logic is skipped →
            //                                   SendSetTarget(0, -1) is
            //                                   the only emit.
            // common/include/net7/PacketStructures.h:528
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTarget.Value, payload),
                cts.Token);

            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.SetTarget.Value)
                    continue;

                var span = reply.Payload.Span;
                Assert.Equal(8, span.Length);

                int replyGameID = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);
                int replyTargetID = BinaryPrimitives.ReadInt32LittleEndian(span[4..8]);

                // GameID field of SetTarget is mirrored from
                // request->TargetID (server/src/PlayerConnection.cpp:3410
                // calls SendSetTarget(request->TargetID, -1)). We sent
                // TargetID=0 so we expect GameID=0 in the reply.
                Assert.Equal(0, replyGameID);

                // TargetID=-1 is the hard-coded sentinel from
                // SendSetTarget itself, not echoed from the wire — this
                // is the positive-correlation signal that the dispatch
                // reached the right handler and the right code path.
                Assert.Equal(-1, replyTargetID);

                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0017 REQUEST_TARGET (GameID=0, TargetID=0) " +
                $"without seeing 0x0019 SET_TARGET. " +
                $"Likely the server's HandleRequestTarget crashed on the 8B payload " +
                $"(RequestTarget long-revert regression), " +
                $"the proxy default-case forwarding dropped the opcode, " +
                $"the dispatcher case at PlayerConnection.cpp:443 got mis-routed, " +
                $"the server→client UDP fan-out (SendOpcode → 0x2016 PACKET_SEQUENCE " +
                $"→ proxy SendClientPacketSequence → TCP) regressed for this opcode, " +
                $"or BlankVerbs/SetTargetGameID introduced a crash hazard.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
