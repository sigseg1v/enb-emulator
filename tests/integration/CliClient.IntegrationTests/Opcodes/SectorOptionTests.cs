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
/// Wave 22 post-handshake survival round-trip: client sends 0x002E
/// OPTION with an unhandled OptionType (=2) that falls through every
/// branch of <c>Player::HandleOption</c>, then verifies the connection
/// survives by round-tripping 0x0044 REQUEST_TIME.
///
/// <para>
/// Wire layout. <c>OptionPacket</c>
/// (<c>common/include/net7/PacketStructures.h:601</c>):
/// <code>
///     struct OptionPacket {
///         int32_t       GameID;       // this[12]  4 bytes
///         int32_t       OptionType;   // this[16]  4 bytes
///         unsigned char OptionVar;    // this[20]  1 byte
///     } ATTRIB_PACKED;
/// </code>
/// Total 9 bytes. Unlike ACTION/ACTION2, the integer fields are
/// host-endian (the handler does not <c>ntohl</c>-swap them — see
/// <c>HandleOption</c> body which compares <c>myOption-&gt;OptionType
/// == 0</c> directly without any byte-order conversion).
/// </para>
///
/// <para>
/// Server handler. <c>Player::HandleOption</c>
/// (<c>server/src/PlayerConnection.cpp:10389</c>):
/// <code>
///     OptionPacket *myOption = (OptionPacket *) data;
///     Group *myGroup = g_ServerMgr-&gt;m_PlayerMgr.GetGroupFromID(GroupID());
///
///     if (myOption-&gt;OptionType == 0)        { /* LFG: SendAuxPlayer */ }
///     else if (myOption-&gt;OptionType == 1)   { /* AllowInvite: SendAuxPlayer */ }
///     else if (myGroup
///              &amp;&amp; myGroup-&gt;Member[0].GameID == GameID()
///              &amp;&amp; GroupID() != -1) {
///         /* group-leader options 3/4/5 */
///     }
///     LogMessage(...);
///     DumpBuffer(...);
/// </code>
/// With OptionType=2 the first two arms fail. With a fresh starbase
/// character GroupID()==-1, so <c>GetGroupFromID(-1)</c> returns null
/// immediately (early-return at <c>server/src/GroupManager.cpp:43-46</c>),
/// and the else-if guard <c>myGroup &amp;&amp; ...</c> short-circuits on
/// the null. Handler then logs and returns — no DB write, no AuxPlayer
/// fan-out, no reply.
/// </para>
///
/// <para>
/// Why survival probe rather than direct reply assertion. The
/// no-op fall-through emits no opcode reply. The retail server's
/// HandleOption is the same code path (Net-7 source matches retail
/// here); retail also emits no reply on OptionType=2. Per the CLAUDE.md
/// server-integrity rule we cannot fabricate one — survival probe is
/// the only assertable post-condition.
/// </para>
///
/// <para>
/// Why OptionType=2 specifically. Three reasons.
/// </para>
/// <list type="number">
///   <item>
///     <b>It's the smallest gap in the dispatch table.</b> Retail
///     option values are 0, 1, 3, 4, 5. Sending value 2 — a published
///     gap — exercises the fall-through path deterministically without
///     having to construct a side-effect-free wire shape that a real
///     option (e.g. 0/1 SendAuxPlayer) would emit.
///   </item>
///   <item>
///     <b>Avoids the SendAuxPlayer reply.</b> OptionType=0 and 1 both
///     call <c>SendAuxPlayer()</c> which fans out an AuxPlayer struct
///     to the client. The test could swallow that frame in the drain
///     loop, but it would couple the test to the (separate) AuxPlayer
///     wire format. OptionType=2 keeps the test purely about the
///     dispatch + survival path.
///   </item>
///   <item>
///     <b>Exercises the GetGroupFromID(-1) null-safety guard.</b>
///     OptionType=2 falls through to the else-if which dereferences
///     <c>myGroup</c>. With GroupID()==-1 the early-return guard in
///     <c>PlayerManager::GetGroupFromID</c> is what keeps the handler
///     safe — a regression that removed that guard would walk the
///     m_GroupList linked list (empty or not) and at least crash on
///     m_Mutex.Lock in a thread-unsafe state, or at worst dereference
///     a stale list head. The test exercises that code path on every
///     run.
///   </item>
/// </list>
///
/// <para>
/// Why GameID=0 in the payload. The retail Win32 client emits
/// OPTION with its own GameID in the GameID slot. The server ignores
/// the GameID field entirely in <c>HandleOption</c> — it derives the
/// player from the connection context (<c>GameID()</c> method on
/// Player), not from the wire byte. So GameID=0 is harmless. We do
/// NOT match retail's client behaviour here because the field is
/// unused by the server; spending wire bytes to read our connection's
/// GameID would couple this test to the GameID-allocation algorithm
/// (which is `account_id*5 + slot + 1` per SaveManager) for no
/// preservation benefit.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>OptionPacket struct layout regression in
///     PacketStructures.h:601.</b> If GameID or OptionType were
///     widened from <c>int32_t</c> to <c>long</c> on Linux x86_64, the
///     struct grows from 9B to 17B and OptionVar's offset shifts past
///     the wire-payload end. The handler would read OptionVar from
///     uninitialised receive-buffer slack — usually harmless for
///     OptionType=2 (we don't take any branch that reads OptionVar),
///     but the regression itself would be silently introduced and
///     bite future waves that DO read OptionVar.
///   </item>
///   <item>
///     <b>Proxy default-case <c>ForwardClientOpcode</c> regression.</b>
///     0x002E is NOT explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>, so it hits the
///     <c>default:</c> arm and falls through to the bottom-of-switch
///     forward. A regression dropping this opcode would surface as a
///     timeout waiting for CLIENT_SET_TIME.
///   </item>
///   <item>
///     <b>Dispatcher mis-route at
///     <c>server/src/PlayerConnection.cpp:475</c>.</b> The case label
///     sits between 0x002D ACTION2 and 0x0033 CLIENT_CHAT in the same
///     ~200-entry hand-maintained switch; a copy-paste error swapping
///     HandleOption for HandleAction2 would mis-interpret the 9B
///     OptionPacket as a 14B ActionPacket2 — reading 5 bytes past the
///     payload end, then chaining into HandleAction with garbage
///     fields. Whether that crashes depends on receive-buffer slack.
///   </item>
///   <item>
///     <b>GetGroupFromID(-1) null-safety regression.</b> A future
///     refactor that removes the early-return guard at
///     <c>GroupManager.cpp:43-46</c> would either crash on the
///     m_Mutex.Lock thread-state path or (with no groups present)
///     walk the empty linked list fine — but the regression would be
///     introduced silently for any future wave that sends OptionType=2
///     on a character with a populated m_GroupList.
///   </item>
///   <item>
///     <b>OptionType byte-order assumption regression.</b> If a future
///     refactor adds <c>ntohl(myOption-&gt;OptionType)</c> to the
///     handler thinking it's wire-byte-order like ACTION/ACTION2 does,
///     OptionType=2 host-LE becomes 0x02000000 in the network-byte-order
///     interpretation, which fails every branch and the handler still
///     no-ops — but the regression would silently break retail Win32
///     clients that send OptionType=0 (LFG): host-LE 0 ntohl-swapped
///     is still 0, so 0 would still hit the LFG branch. The regression
///     would only surface on OptionType=1 (ntohl(1)=0x01000000 fails
///     every branch). This test doesn't directly catch the regression,
///     but it documents the assumption.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The 9-byte OptionPacket
/// payload is exactly the wire shape the retail Win32 client emits.
/// OptionType=2 is an unhandled-but-tolerated input on retail — the
/// HandleOption switch falls through silently on any value outside
/// 0/1/3/4/5 — and the retail server emits no reply on this branch
/// either. We are not making the server accept any new input shape;
/// we are not fabricating any reply. The "ungrouped character"
/// precondition (GroupID()==-1) is the default state for any newly
/// handed-off character entering a starbase, which is exactly where
/// the retail client first becomes able to send OPTION frames.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; OPTION+REQUEST_TIME round-trip is
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorOptionTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorOptionTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task Option_UnhandledOptionType_DoesNotBreakConnection_RequestTimeStillRoundTrips()
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
            firstName: "Optic", shipName: "OptiShip", cts.Token);

        try
        {
            // OptionPacket wire layout — 9 bytes:
            //   [0..4)  host-endian (LE on x86) int32  GameID     = 0
            //                              (ignored by handler;
            //                               player derived from
            //                               connection context)
            //   [4..8)  host-endian (LE on x86) int32  OptionType = 2
            //                              (published gap between LFG/
            //                               AllowInvite and group-leader
            //                               options 3/4/5 — fall-through
            //                               no-op branch)
            //   [8]                             byte   OptionVar  = 0
            //                              (not consumed on the fall-
            //                               through branch)
            //
            // common/include/net7/PacketStructures.h:601
            byte[] payload = new byte[9];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 2);
            payload[8] = 0;

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.Option.Value, payload),
                cts.Token);

            // Survival probe.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Post-handshake the
            // server may begin streaming in-sector frames so this loop
            // tolerates interleaved traffic. Cap on frame count so a
            // stalled pipeline can't masquerade as the outer-CTS
            // timeout.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.ClientSetTime.Value)
                    continue;

                // 0x0034 wire layout (ClientSetTime struct):
                //   [0..4)  int32  ClientSent
                //   [4..8)  int32  ServerReceived
                //   [8..12) int32  ServerSent
                // common/include/net7/PacketStructures.h:563
                var span = reply.Payload.Span;
                Assert.Equal(12, span.Length);

                int echoedClientSent = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);
                Assert.Equal(clientTick, echoedClientSent);

                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x002E OPTION (OptionType=2 fall-through) + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the server's HandleOption crashed (struct layout regression past 9B payload end), " +
                $"GetGroupFromID(-1) null-safety guard was removed and the empty m_GroupList walk segfaulted, " +
                $"the proxy default-case forwarding dropped the opcode, " +
                $"or the dispatcher case at PlayerConnection.cpp:475 got mis-routed to HandleAction2 which reads 5 bytes past the OptionPacket end.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
