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
/// Wave 20 direct-reply round-trip: the natural follow-on to Wave 19.
/// Client sends 0x0018 REQUEST_TARGETS_TARGET (the wire frame the
/// retail Win32 client emits to ask the server "what is the player
/// currently targeted by player X targeting?"), then asserts on the
/// server's mandatory 0x0019 SET_TARGET reply from the
/// not-a-connected-player else branch.
///
/// <para>
/// Why this opcode follows REQUEST_TARGET. Wave 19's SET_TARGET reply
/// carried TargetID=-1, which is the sentinel meaning "no rank/threat
/// info yet, client should re-request via 0x0018 REQUEST_TARGETS_TARGET".
/// This wave exercises that follow-up call — the second half of the
/// target-info handshake the retail client uses every time it makes a
/// new object the active target.
/// </para>
///
/// <para>
/// Wire layout. <c>RequestTarget</c>
/// (<c>common/include/net7/PacketStructures.h:528</c>): same 8B struct
/// as Wave 19's opcode — <c>{int32_t GameID; int32_t TargetID;}</c>.
/// The retail Win32 client reuses the struct because both opcodes ask
/// "who/what is being targeted" — the difference is whose target.
/// 0x0017 asks "make this object my target"; 0x0018 asks "give me
/// targeting info on the player whose GameID I'm passing as TargetID".
/// </para>
///
/// <para>
/// Server handler. <c>Player::HandleRequestTargetsTarget</c>
/// (<c>server/src/PlayerConnection.cpp:3494</c>):
/// <code>
///     RequestTarget * request = (RequestTarget *) data;
///     Player *p = g_ServerMgr-&gt;m_PlayerMgr.GetPlayer(request-&gt;TargetID);
///     if (p) {
///         *((int *) &amp;data[4]) = p-&gt;ShipIndex()-&gt;GetTargetGameID();
///         HandleRequestTarget(data);
///     } else {
///         SendSetTarget(0, -1);
///         ShipIndex()-&gt;SetTargetGameID(-1);
///         SendAuxShip();
///     }
/// </code>
/// <c>PlayerManager::GetPlayer</c>
/// (<c>server/src/PlayerManager.cpp:209</c>) is a hash-map lookup over
/// <c>m_PlayerLookup</c> keyed on CharacterID; returns null for any
/// GameID that doesn't correspond to a connected player. With a
/// single-player integration test, any TargetID other than our own
/// avatar's GameID trips the else branch. We send TargetID=0x12345678
/// (305419896 = a fixed nonzero sentinel that is comfortably outside
/// the SaveManager-assigned CharacterID space of
/// <c>account_id*5 + slot + 1</c> for any seeded account).
/// </para>
///
/// <para>
/// Else-branch reply path. <c>SendSetTarget(0, -1)</c> emits an 8-byte
/// 0x0019 SET_TARGET payload with GameID=0 (literal) and TargetID=-1
/// (literal). Then <c>SetTargetGameID(-1)</c> mutates ship state.
/// Then <c>SendAuxShip()</c>
/// (<c>server/src/PlayerClass.cpp:1145</c>) — only emits a 0x001B
/// AUX_DATA frame if <c>m_ShipIndex.HasDiff()</c>; we drain any aux
/// frames as no-ops and continue looking for SET_TARGET.
/// </para>
///
/// <para>
/// Why this is a *direct-reply* test and not a survival probe — and
/// why it's stronger than Wave 19. <b>The reply's GameID slot
/// differentiates HandleRequestTargetsTarget's else branch from
/// HandleRequestTarget</b>:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>HandleRequestTargetsTarget else branch</b> (the correct path
///     for 0x0018): <c>SendSetTarget(0, -1)</c> — GameID is the
///     **literal 0**, regardless of what TargetID we sent.
///   </item>
///   <item>
///     <b>HandleRequestTarget</b> (what a 0x0018→0x0017 dispatcher
///     mis-route would invoke): <c>SendSetTarget(request-&gt;TargetID, -1)</c>
///     — GameID would mirror our sent TargetID = 0x12345678.
///   </item>
/// </list>
/// <para>
/// So asserting <c>replyGameID == 0</c> on a nonzero-TargetID send is
/// a positive-correlation signal that the dispatcher routed to the
/// correct handler AND that the handler took the else branch. Wave 19
/// could not catch this misroute because both handlers happened to
/// produce identical replies for TargetID=0.
/// </para>
///
/// <para>
/// Concrete regression classes this catches (on top of Wave 19's
/// catches):
/// </para>
/// <list type="bullet">
///   <item>
///     <b>0x0018→0x0017 dispatcher mis-route.</b> The case label at
///     <c>server/src/PlayerConnection.cpp:447</c> is one entry above
///     0x0017 in the hand-maintained ~200-entry switch; a copy-paste
///     error swapping the two cases is plausible. Our nonzero TargetID
///     send + replyGameID==0 assert fails immediately if the dispatch
///     went to HandleRequestTarget instead — its reply would have
///     GameID=0x12345678.
///   </item>
///   <item>
///     <b>HandleRequestTargetsTarget else-branch logic regression.</b>
///     If anyone "fixes" the literal <c>SendSetTarget(0, -1)</c> to
///     <c>SendSetTarget(request-&gt;TargetID, -1)</c> (a plausible
///     "consistency with HandleRequestTarget" mistake), our assertion
///     fails. Per CLAUDE.md server-integrity rules the literal-0 in
///     the else branch is the retail behaviour and shouldn't be
///     "fixed".
///   </item>
///   <item>
///     <b>GetPlayer hash-map regression.</b> If
///     <c>PlayerManager::GetPlayer(int)</c> stops returning null for
///     unknown IDs (e.g. starts returning a dangling pointer or a
///     wrong player), the test fails because the if-branch fires
///     and forwards to HandleRequestTarget, producing a different
///     reply (or crashing).
///   </item>
///   <item>
///     <b>RequestTarget long-revert (shared with Wave 19).</b> Struct
///     widening to 12B+ on Linux would shift TargetID's read past the
///     end of the 8B wire payload, returning garbage. If garbage
///     happens to match a connected player's GameID, we'd hit the
///     if-branch instead of the else-branch. Realistically with a
///     single-player test this is unlikely to cause a stable failure
///     but the length assertion (8B reply payload) still catches the
///     parallel SetTarget widening.
///   </item>
///   <item>
///     <b>SetTarget reply long-revert (shared with Wave 19).</b>
///     Payload length assert at exactly 8B catches widening to 12B/16B.
///   </item>
///   <item>
///     <b>Proxy default-case <c>ForwardClientOpcode</c> regression.</b>
///     REQUEST_TARGETS_TARGET is not explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>, so it hits the
///     <c>default:</c> arm at line 514 and falls through to the
///     bottom-of-switch <c>ForwardClientOpcode</c>. A regression that
///     dropped this opcode would surface as a timeout waiting for
///     SET_TARGET.
///   </item>
///   <item>
///     <b>ShipIndex/SetTargetGameID crash hazard.</b> The else branch
///     calls <c>ShipIndex()-&gt;SetTargetGameID(-1)</c> followed by
///     <c>SendAuxShip()</c>. A regression in either that crashed the
///     sector thread would prevent the SET_TARGET reply from being
///     drained (the cache flush happens after the handler returns).
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The REQUEST_TARGETS_TARGET
/// payload sent here is exactly the wire shape the retail Win32 client
/// emits as the follow-up to clicking a player to make them the active
/// target — the client sends 0x0017 first (to set its own active
/// target to that player), then 0x0018 (to ask "what is that player
/// targeting"). TargetID=0x12345678 is just a "no such player"
/// sentinel; the retail server's else branch fires identically for
/// any unknown TargetID — we are not making the server accept any
/// new input shape, and we don't fabricate a reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; REQUEST_TARGETS_TARGET → SET_TARGET
/// round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorRequestTargetsTargetTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorRequestTargetsTargetTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task RequestTargetsTarget_OnUnknownPlayer_ReceivesSetTargetWithLiteralZeroGameId()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Inquirer", shipName: "InquireShip", cts.Token);

        try
        {
            // RequestTarget wire layout — 8 bytes (shared with 0x0017):
            //   [0..4)   int32 LE  GameID    — actor's own avatar id;
            //                                   retail client sets this
            //                                   to the player's own
            //                                   GameID. We send 0 — the
            //                                   field is not consumed
            //                                   by HandleRequestTargetsTarget.
            //   [4..8)   int32 LE  TargetID  — the GameID of the player
            //                                   whose target we're
            //                                   inquiring about. We send
            //                                   0x12345678 (305419896) —
            //                                   a fixed nonzero sentinel
            //                                   that doesn't correspond
            //                                   to any connected player.
            //                                   GetPlayer(0x12345678)
            //                                   returns null → handler
            //                                   takes the else branch →
            //                                   SendSetTarget(0, -1).
            //
            // Why 0x12345678 specifically — it's safely outside any
            // possible CharacterID assigned by SaveManager
            // (account_id*5 + slot + 1) for any seeded account
            // (id <= 9_000_017 → max CharacterID 45_000_086). It's also
            // a recognizable byte pattern in a hex dump if the test
            // ever fails noisily.
            //
            // common/include/net7/PacketStructures.h:528
            const int unknownPlayerTargetId = 0x12345678;

            byte[] payload = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), unknownPlayerTargetId);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTargetsTarget.Value, payload),
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

                // Critical assertion — differentiates HandleRequestTargetsTarget
                // (else branch, SendSetTarget(0, -1) → GameID=0) from
                // HandleRequestTarget (SendSetTarget(request->TargetID, -1)
                // → GameID=0x12345678 in this scenario). A 0x0018→0x0017
                // dispatcher mis-route would fail HERE.
                Assert.Equal(0, replyGameID);

                // TargetID=-1 sentinel — same as Wave 19's reply; means
                // "no rank/threat info available". Confirms the
                // SendSetTarget hardcoded -1 is intact.
                Assert.Equal(-1, replyTargetID);

                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0018 REQUEST_TARGETS_TARGET (GameID=0, TargetID=0x12345678) " +
                $"without seeing 0x0019 SET_TARGET. " +
                $"Likely the server's HandleRequestTargetsTarget crashed on the 8B payload " +
                $"(RequestTarget long-revert regression), " +
                $"PlayerManager::GetPlayer returned non-null for the unknown TargetID " +
                $"(would route to the if-branch HandleRequestTarget instead — would still emit SET_TARGET, but with a different GameID), " +
                $"the proxy default-case forwarding dropped the opcode, " +
                $"the dispatcher case at PlayerConnection.cpp:447 got mis-routed elsewhere, " +
                $"or ShipIndex/SendAuxShip in the else branch crashed.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
