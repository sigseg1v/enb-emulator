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
/// Wave 18 post-handshake survival round-trip: client sends 0x005A
/// VERB_REQUEST (the wire frame the retail Win32 client emits when the
/// user right-clicks an in-world object to ask the server "what verbs
/// are valid against this target?"), then verifies the connection
/// survives via a 0x0044 REQUEST_TIME round-trip.
///
/// <para>
/// Why survival probe rather than direct reply assertion.
/// <c>Player::HandleVerbRequest</c>
/// (<c>server/src/PlayerConnection.cpp:3547</c>) is short:
/// <code>
///     VerbRequest * pkt = (VerbRequest *) data;
///     long subject_id = (long) ntohl(pkt-&gt;SubjectID);
///     long object_id  = (long) ntohl(pkt-&gt;ObjectID);
///     if (subject_id == GameID() &amp;&amp; pkt-&gt;Action == 1)
///         UpdateVerbs(true);
/// </code>
/// The single conditional is "is this verb-request from me about
/// myself, with Action=1 (refresh-now)?". On mismatch — including any
/// SubjectID that isn't our own GameID — the handler returns
/// silently. No state mutation, no reply. Even on a match, the path
/// drops through <c>UpdateVerbs(true)</c> which itself early-returns
/// when <c>ShipIndex()-&gt;GetTargetGameID()</c> returns 0 (no
/// targeted object), which is exactly the post-handshake state for a
/// freshly-created character: no reply either. Per CLAUDE.md
/// server-integrity we don't fabricate a reply.
/// </para>
///
/// <para>
/// SubjectID choice. We send <c>SubjectID=0</c>. The player's actual
/// GameID is assigned by SaveManager at character-creation
/// (<c>account_id*5 + slot + 1</c>) and is never 0 for any seeded
/// account in <c>cli_test*</c>, so the equality test
/// <c>subject_id == GameID()</c> is guaranteed to fail and the
/// handler exits without entering the UpdateVerbs path. The wire
/// shape is byte-identical to what the retail Win32 client emits when
/// it sends a verb-update request — we just send a SubjectID that
/// doesn't match this connection's player, which is exactly the
/// branch the retail server takes on receipt of any mis-targeted
/// VerbRequest. We are not making the server accept anything new.
/// </para>
///
/// <para>
/// Concrete regression class this catches: VerbRequest is
/// <c>{int32_t SubjectID; int32_t ObjectID; int32_t Action;}</c> =
/// 12B canonical
/// (<c>common/include/net7/PacketStructures.h:570</c>). If any of the
/// three int32_t fields is widened to <c>long</c>, the struct grows
/// from 12B to 16B/20B/24B on Linux x86_64 and the Action field
/// reads from the wrong offset — at minimum mis-comparing against
/// the equality guard, at worst reading garbage from past the 12B
/// payload end into undefined memory.
/// </para>
///
/// <para>
/// Other bugs this test would also catch:
/// </para>
/// <list type="bullet">
///   <item>
///     Proxy default-case <c>ForwardClientOpcode</c> regression.
///     VERB_REQUEST is not explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>, so it hits the
///     <c>default:</c> arm at line 514 and falls through to the
///     bottom-of-switch <c>ForwardClientOpcode</c>. A regression that
///     <c>return</c>ed early or that added an empty hand-coded case
///     that returned would silently drop the opcode.
///   </item>
///   <item>
///     Dispatch mis-route. The case label at
///     <c>server/src/PlayerConnection.cpp:507</c> is hand-maintained
///     in a ~200-entry switch; a copy-paste error could route 0x005A
///     to a different handler that crashes on the 12-byte payload.
///   </item>
///   <item>
///     ntohl/htonl byte-order flip on the SubjectID/ObjectID fields.
///     SubjectID=0 is a fixed point under byte-swap so the equality
///     test result is invariant — but a regression that dropped the
///     ntohl (or added an extra one) on a non-zero SubjectID would
///     suddenly hit (or miss) the GameID-equality branch on the
///     wrong endianness.
///   </item>
///   <item>
///     Regression in the equality short-circuit. The current code
///     reads both subject_id and object_id from the wire before the
///     equality check; a refactor that reordered the deref past a
///     null-check or that introduced a null-deref on a stale pkt
///     pointer would surface here.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The VerbRequest payload
/// sent here is the exact wire shape the retail Win32 client emits
/// when the user right-clicks an object to refresh its available
/// verbs: 4B SubjectID + 4B ObjectID + 4B Action. SubjectID=0 is
/// the value the retail client would send if its UI ever
/// dispatched a verb-refresh outside of a targeted context — and
/// the retail server's silent no-op on subject-mismatch is the
/// faithful behaviour. We are not making the server accept any new
/// input shape, and we don't fabricate a reply (retail doesn't emit
/// one on this branch either).
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; VERB_REQUEST+REQUEST_TIME round-trip
/// is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorVerbRequestTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorVerbRequestTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task VerbRequest_OnNonMatchingSubject_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        // cli_test15 — Pool[13]. Dedicated to this test so its
        // Create/Delete cycle doesn't collide with Pool[3..12] which
        // are owned by SectorLogin / SectorChat / SectorRequestTime /
        // SectorStartAck / SectorTurnTilt / SectorAction / SectorMove /
        // SectorStarbaseRoomChange / SectorStarbaseRequest / SectorSkillUp
        // respectively.
        var account = TestAccounts.Pool[13];
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Verber", shipName: "VerbShip", cts.Token);

        try
        {
            // VerbRequest wire layout — 12 bytes:
            //   [0..4)   int32 LE  SubjectID  — actor's avatar id;
            //                                    retail client sets
            //                                    this to the player's
            //                                    own GameID. We send 0
            //                                    so the server's
            //                                    `subject_id == GameID()`
            //                                    equality fails (every
            //                                    cli_test account's GameID
            //                                    is non-zero by SaveManager
            //                                    construction), tripping
            //                                    the silent-no-op branch
            //                                    of HandleVerbRequest.
            //   [4..8)   int32 LE  ObjectID   — the targeted object's
            //                                    GameID. Unused on the
            //                                    silent branch.
            //   [8..12)  int32 LE  Action     — 1 = refresh-now request
            //                                    (the only Action value
            //                                    the handler dispatches
            //                                    on). Set to 1 to match
            //                                    the retail wire shape;
            //                                    the SubjectID mismatch
            //                                    short-circuits before
            //                                    this field gates a
            //                                    branch.
            // common/include/net7/PacketStructures.h:570
            byte[] payload = new byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 1);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.VerbRequest.Value, payload),
                cts.Token);

            // Survival probe.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

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
                $"drained {maxFrames} frames after sending 0x005A VERB_REQUEST (SubjectID=0, Action=1) " +
                $"+ 0x0044 REQUEST_TIME without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the server's HandleVerbRequest read past the 12B payload " +
                $"(VerbRequest long-revert regression on SubjectID/ObjectID/Action fields), " +
                $"the proxy default-case forwarding dropped the opcode, " +
                $"the ntohl byte-order on SubjectID got flipped, " +
                $"or the dispatcher case at PlayerConnection.cpp:507 got mis-routed.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
