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
/// Wave 28 direct-reply round-trip: client sends a 5-byte 0x0055
/// SELECT_TALK_TREE with Selection=0 against a fresh starbase
/// character (no NPC dialogue open), expects the server's
/// "close-dialog" reply back as a 0x0056 TALK_TREE_ACTION with
/// 4-byte int32 payload = -32 (0xFFFFFFE0 host-LE).
///
/// <para>
/// Wire layout (mirror of <c>common/include/net7/PacketStructures.h:608</c>):
/// <code>
///   [0..4) int32 PlayerID
///   [4]    char  Selection
/// </code>
/// 5 bytes total; <c>ATTRIB_PACKED</c>.
/// </para>
///
/// <para>
/// Byte order. <c>Player::HandleSelectTalkTree</c>
/// (<c>server/src/PlayerConnection.cpp:10431</c>) does NOT ntohl-swap
/// either field — the dispatcher reads PlayerID/Selection in host
/// byte order. Both fields are zero so byte-order doesn't materially
/// affect this payload, but the test uses host-LE to match the
/// handler's expectation faithfully (a future Wave 28+ that picks a
/// non-zero PlayerID for cross-handler dispatcher-misroute coverage
/// would matter; this one keeps it boring).
/// </para>
///
/// <para>
/// Server handler walk-through (fresh starbase character — m_CurrentNPC
/// is NULL because we never opened an NPC dialogue; m_TradeWindow,
/// m_BeaconRequest, m_ActionResponseReceived, m_MoreDestination all
/// false/zero by default-construction):
/// </para>
/// <list type="number">
///   <item>
///     Line 10440: <c>if (packet-&gt;Selection == 0 &amp;&amp; m_MoreDestination)</c>
///     — false (m_MoreDestination=0), skipped.
///   </item>
///   <item>
///     Line 10446: <c>if (m_ActionResponseReceived == true ...)</c> —
///     false, skipped.
///   </item>
///   <item>
///     Line 10456: <c>if (m_TradeWindow == true)</c> — false, skipped.
///   </item>
///   <item>
///     Line 10468: <c>if (m_BeaconRequest)</c> — false, skipped.
///   </item>
///   <item>
///     Line 10498: <c>if (packet-&gt;Selection == 255)</c> — false
///     (Selection=0), skipped.
///   </item>
///   <item>
///     Line 10526: <c>else if (packet-&gt;Selection != 230 &amp;&amp;
///     packet-&gt;Selection != 0)</c> — false (Selection==0), skipped.
///   </item>
///   <item>
///     Line 10542: <c>if (CheckTalkTree(packet-&gt;Selection))</c> —
///     <c>CheckTalkTree</c> at line 10579 short-circuits at
///     <c>if (!m_CurrentNPC) return false;</c> on line 10582. Returns
///     false, the <c>if</c> body does not run.
///   </item>
///   <item>
///     Line 10546: <c>if (packet-&gt;Selection == 0 &amp;&amp;
///     m_CurrentNPC != NULL)</c> — false (m_CurrentNPC null), trade
///     branch skipped.
///   </item>
///   <item>
///     Line 10560-10565: <c>else { SendTalkTreeAction(-32);
///     m_MissionDebriefed = false; }</c> — <b>this is the assertable
///     emission</b>. The handler ALWAYS reaches one of these two
///     branches; with m_CurrentNPC null we deterministically land in
///     the else.
///   </item>
///   <item>
///     Line 10567: <c>if (packet-&gt;Selection == 1 ||
///     packet-&gt;Selection == 230)</c> — false (Selection=0), the
///     SendAuxPlayer/SendAuxShip fan-out is skipped.
///   </item>
/// </list>
///
/// <para>
/// Reply wire layout. <c>Player::SendTalkTreeAction</c>
/// (<c>server/src/PlayerConnection.cpp:7762</c>) writes
/// <c>int32_t action_wire = (int32_t) action</c> then emits
/// <c>SendOpcode(ENB_OPCODE_0056_TALK_TREE_ACTION, &amp;action_wire,
/// sizeof(action_wire))</c> — 4-byte payload, no htonl. Host-LE on
/// x86_64 means -32 = 0xFFFFFFE0 lands on the wire as bytes
/// <c>[E0 FF FF FF]</c>. The Phase K Wave 11 sizeof(long) fix at
/// PlayerConnection.cpp:7767 is what pins the width to 4 bytes
/// instead of 8 — a regression to that would balloon the payload to
/// 8B and our length assertion catches it immediately.
/// </para>
///
/// <para>
/// Per-client SendOpcode path. <c>SendOpcode</c> at PlayerConnection
/// .cpp:127 routes through the per-client <c>m_UDPQueue</c> →
/// <c>SendPacketCache</c> → 0x2016 PACKET_SEQUENCE → proxy
/// <c>SendClientPacketSequence</c> → TCP fan-out. No SendToSector
/// fan-out, so no login-stage race like Wave 25 had — the per-client
/// UDP queue is set up at session creation, not at sector-list
/// insertion. Simple drain-loop pattern suffices, no retry loop
/// needed. Same favourable structural property as Waves 24, 26, 27.
/// </para>
///
/// <para>
/// Why Selection=0 with no NPC. The "close any open dialogue" reply
/// is the most-frequent legitimate use of 0x0055 from the real Win32
/// client: it is exactly what the client sends when the user clicks
/// the "X" on a TalkTree dialog window. With no NPC actually open on
/// our side, the server's deterministic response is "send the close
/// signal anyway" — preservation-faithful, no permissiveness added
/// (the real server does the same thing). Per CLAUDE.md
/// server-integrity: this is the documented retail behaviour for
/// SELECT_TALK_TREE with Selection=0 and no current NPC state.
/// </para>
///
/// <para>
/// Direct-reply assertion vs. survival probe. Three positive-
/// correlation signals: (a) the inbound opcode is exactly 0x0056
/// TALK_TREE_ACTION (not just "any opcode arrived"); (b) the payload
/// length is exactly 4 bytes (catches Wave 11's sizeof(long) fix
/// regression that would balloon to 8); (c) the int32 value equals
/// -32 (the literal "close dialog" sentinel — catches a regression
/// that changes the sentinel value or sends a different action
/// code).
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>SelectTalkTree struct long-revert in PacketStructures.h:608.</b>
///     Currently <c>int32_t PlayerID + unsigned char Selection</c> =
///     5B canonical. Widening PlayerID to <c>long</c> on Linux
///     x86_64 would grow the struct to 9B — Selection would read
///     from offset 8 instead of offset 4. Our PlayerID bytes are all
///     zero so offset 8 also reads 0 (no change in test behaviour)
///     BUT a non-zero-PlayerID test (any future wave) would silently
///     corrupt. This wave can't catch the regression alone, but the
///     Wave 12 long-sweep + grep guards do.
///   </item>
///   <item>
///     <b>Dispatcher mis-route at server/src/PlayerConnection.cpp:495.</b>
///     Case label sits between 0x004E STARBASE_REQUEST (Wave 16) and
///     0x0057 SKILL_UP (Wave 17) in the hand-maintained ~200-entry
///     switch. A copy-paste swap with HandleSkillAction would
///     mis-interpret 5B SelectTalkTree as 10B SkillAction — reading
///     SkillID from past the end of our wire payload (into receive-
///     buffer slack). Likely SEGVs on the SkillID&gt;=64 trap that
///     Wave 17 documented (AuxSkills wrapper-array overflow into
///     Player memory).
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode regression.</b>
///     0x0055 is NOT explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>, so it hits the
///     <c>default:</c> arm and falls through to the bottom-of-switch
///     ForwardClientOpcode. A regression dropping this opcode would
///     surface as a timeout waiting for the TALK_TREE_ACTION reply.
///   </item>
///   <item>
///     <b>HandleSelectTalkTree else-branch removal.</b> A refactor
///     that deletes the else-branch at line 10560-10565 (or replaces
///     SendTalkTreeAction(-32) with a different action code) would
///     break this test. The -32 literal is "close display" per the
///     comment at line 10533; preserving it is preservation
///     fidelity.
///   </item>
///   <item>
///     <b>CheckTalkTree m_CurrentNPC null-guard removal.</b> The
///     guard at line 10582 (<c>if (!m_CurrentNPC) return false;</c>)
///     is what keeps a fresh character from null-deref'ing through
///     to the GenerateTalkTree call at line 10592. Removing it would
///     crash the server on this exact wire payload (segfault on
///     <c>m_CurrentNPC-&gt;NPCInteraction.talk_tree</c>).
///   </item>
///   <item>
///     <b>SendTalkTreeAction sizeof(long) Wave 11 regression.</b>
///     <c>int32_t action_wire = (int32_t) action</c> at line 7767 is
///     the Phase K fix that pins the wire field to 4 bytes; reverting
///     to <c>SendOpcode(..., &amp;action, sizeof(action))</c> on
///     Linux x86_64 would emit 8 bytes with the top 4 being garbage
///     (because <c>long</c> is 8B). Our 4-byte payload-length
///     assertion catches this immediately.
///   </item>
///   <item>
///     <b>Server→client 0x0056 forwarding regression in proxy.</b>
///     <c>SendClientPacketSequence</c>
///     (<c>proxy/UDPProxyToClient_linux.cpp</c>) walks inner
///     <c>[size,opcode,data]</c> tuples and dispatches one TCP frame
///     per opcode. A regression that whitelists only specific server-
///     emit opcodes (or mis-classes 0x0056 as a launcher opcode)
///     would drop the reply — surfaces as a timeout.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x0055 SELECT_TALK_TREE
/// with Selection=0 is what the retail Win32 client sends when the
/// user closes a dialogue window. The retail server's
/// HandleSelectTalkTree has the same fall-through else-branch
/// emitting SendTalkTreeAction(-32) for the "no current NPC, close
/// dialog" case. Zero permissiveness added; no behaviour relaxed;
/// the test asserts the documented retail close-signal response.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; SELECT_TALK_TREE + reply round-trip
/// is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorSelectTalkTreeTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorSelectTalkTreeTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task SelectTalkTree_NoCurrentNpc_ReceivesTalkTreeActionCloseSentinel()
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
            firstName: "Talker", shipName: "TalkerShip", cts.Token);

        try
        {
            // 0x0055 SELECT_TALK_TREE — 5B canonical payload, host-LE
            // (handler does NOT ntohl-decode):
            //   [0..4) int32 PlayerID  = 0
            //   [4]    char  Selection = 0
            // Selection=0 + no open NPC dialogue (m_CurrentNPC null on
            // fresh character) → handler walks every guard, lands in
            // the bottom else-branch at PlayerConnection.cpp:10560 →
            // single SendTalkTreeAction(-32) emission.
            byte[] payload = new byte[5];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            payload[4] = 0;

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.SelectTalkTree.Value, payload),
                cts.Token);

            // Drain inbound until we see a 0x0056 TALK_TREE_ACTION
            // with the expected 4-byte payload = -32. Post-handshake
            // the server may interleave other in-sector fan-out (NPC
            // chatter, state updates, login-stage probes etc.); a frame
            // cap keeps a stalled pipeline from masquerading as the
            // outer-CTS timeout.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.TalkTreeAction.Value)
                    continue;

                // 0x0056 wire layout (mirror of Player::SendTalkTreeAction
                // at server/src/PlayerConnection.cpp:7762):
                //   [0..4) int32 action  (host-LE)
                var span = reply.Payload.Span;

                // Payload-length assertion catches Wave 11 sizeof(long)
                // regression that would balloon this to 8 bytes.
                Assert.Equal(4, span.Length);

                int action = BinaryPrimitives.ReadInt32LittleEndian(span);

                // -32 is the literal "close display" sentinel — see the
                // comment at PlayerConnection.cpp:10533 ("close display").
                // Pinning the exact value catches a regression that
                // swapped the sentinel (e.g. 0, -1, 230) which would
                // silently break client-side dialog dismissal.
                Assert.Equal(-32, action);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0055 SELECT_TALK_TREE (Selection=0) " +
                $"without seeing 0x0056 TALK_TREE_ACTION with payload=-32. " +
                $"Likely the server's HandleSelectTalkTree else-branch broke (line 10560-10565), " +
                $"CheckTalkTree's m_CurrentNPC null-guard was removed, " +
                $"the proxy default-case forwarding dropped 0x0055, " +
                $"the dispatcher case at PlayerConnection.cpp:495 got mis-routed, " +
                $"or SendTalkTreeAction's int32_t width-fix at line 7767 was reverted.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
