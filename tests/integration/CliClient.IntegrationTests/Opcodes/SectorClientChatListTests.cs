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
/// Wave 63 direct-reply +1 ratchet: client sends an 18-byte 0x00A3
/// CLIENT_CHAT_REQUEST on the sector connection with type=24
/// (CCR_LIST_FRIENDS), expects the server's <c>Player::ListFriends</c> reply
/// at <c>server/src/PlayerMisc.cpp:1442</c> which calls
/// <c>SendClientChatList(CHAT_LIST_FRIENDS, name, sector, 0, 0)</c> on a
/// fresh character (<c>m_NumFriends==0</c>). The emit lives in
/// <c>Player::SendClientChatList</c> at
/// <c>server/src/PlayerConnection.cpp:4645-4665</c> and lands a single
/// 0x00A4 CLIENT_CHAT_LIST frame with a deterministic 14-byte all-zero
/// payload.
///
/// <para>
/// Stimulus wire layout (mirror of <c>common/include/net7/PacketStructures.h:669-681</c>):
/// <code>
///   [0..4)   int32 PlayerID         = 0  (handler ignores)
///   [4..8)   int32 type             = 24 (CCR_LIST_FRIENDS)
///   [8..10)  short string_length1   = 0
///   [10..12) short string_length2   = 0
///   [12..14) short string_length3   = 0
///   [14..18) int32 data_size        = 0
/// </code>
/// 18 bytes total, <c>ATTRIB_PACKED</c>.
/// </para>
///
/// <para>
/// Server dispatch. <c>server/src/PlayerConnection.cpp:587</c> routes 0x00A3 to
/// <c>HandleClientChatRequest</c> at line 1648. The variable-length string
/// walker at lines 1659-1692 fast-paths through (all three lengths are 0).
/// The switch at line 1695 hits <c>case CCR_LIST_FRIENDS</c> at line 1720:
/// <code>
///   case CCR_LIST_FRIENDS:
///       ListFriends();
///       return;
/// </code>
/// Note the <c>return</c> (not <c>break</c>) — the post-switch if-chain at
/// line 1731 is skipped entirely, so the only emit is the 0x00A4 frame from
/// <c>ListFriends → SendClientChatList</c>.
/// </para>
///
/// <para>
/// ListFriends body (<c>PlayerMisc.cpp:1419-1443</c>):
/// <code>
///   char *name[MAX_FRIEND_LIST];
///   char *sector[MAX_FRIEND_LIST];
///   short count = 0;
///   for (int i = 0; i &lt; m_NumFriends; i++)   // fresh char: m_NumFriends==0
///   {
///       /* skipped */
///   }
///   SendClientChatList(CHAT_LIST_FRIENDS, name, sector, count, count);
/// </code>
/// On a fresh character, <c>m_NumFriends == 0</c> (initialised in
/// <c>Player::FinishInit</c>), the loop is skipped, and <c>count</c> stays
/// at 0. The <c>name[]</c> and <c>sector[]</c> stack arrays are NEVER read
/// inside <c>SendClientChatList</c> because the two iteration counts
/// passed are 0, so the uninitialised stack memory is harmless here — a
/// fresh-character invariant the wave pins.
/// </para>
///
/// <para>
/// SendClientChatList emit (<c>PlayerConnection.cpp:4645-4665</c>):
/// <code>
///   AddData(packet, listtype, index);          // 4B int32 LE: 0 (CHAT_LIST_FRIENDS)
///   AddDataLS(packet, channel, index);         // short(0) — channel="" default
///   AddData(packet, number1, index);           // 4B int32 LE: 0
///   for(int x=0;x&lt;number1;x++) { ... }       // skipped
///   AddData(packet, number2, index);           // 4B int32 LE: 0
///   for(int x=0;x&lt;number2;x++) { ... }       // skipped
///   SendOpcode(ENB_OPCODE_00A4_CLIENT_CHAT_LIST, packet, index);
/// </code>
/// Final wire payload: 4 + 2 + 4 + 4 = <b>14 bytes, all zero</b>. The
/// <c>channel</c> parameter defaults to <c>""</c> (declaration default in
/// <c>PlayerClass.h:850</c>) — <c>strlen("") == 0</c>, so AddDataLS writes
/// only the 2-byte length prefix (zero) and zero string bytes.
/// </para>
///
/// <para>
/// Why this wave target. 0x00A4 CLIENT_CHAT_LIST is a previously-uncovered
/// server-emit opcode, the CCR_LIST_FRIENDS branch is deterministic (no
/// dependency on friend list state — a fresh character's
/// <c>m_NumFriends==0</c> is the cleanest path), and the emit is a
/// 14-byte all-zero payload making byte-for-byte assertion trivial. The
/// switch case uses <c>return;</c> not <c>break;</c> so there's no
/// follow-up emit to drown the 0x00A4 frame in noise. Complements Wave 32
/// (CCR_FRIEND_STATUS_ONLY=28 → 0x00A5) which hits an adjacent switch
/// case via the same dispatcher.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>0x00A4 emit removal at <c>PlayerConnection.cpp:4662</c>.</b>
///     Drain times out for 0x00A4 — no other code path emits this opcode
///     for a fresh character with no friend list.
///   </item>
///   <item>
///     <b>Wire-shape regression in <c>SendClientChatList</c>.</b> Any
///     change to the AddData/AddDataLS sequence — adding fields, reordering,
///     widening listtype to 8B (the AddData&lt;long&gt; specialisation
///     class), changing the channel-string default — surfaces via the
///     14B length assertion and the all-zero content assertion.
///   </item>
///   <item>
///     <b><c>AddData&lt;long&gt;</c> specialisation revert at
///     <c>server/src/PacketMethods.h:37-42</c>.</b> If the specialisation
///     is removed, the generic template would emit 8 bytes for the
///     listtype/number1/number2 long fields on Linux x86_64. Total wire
///     payload would balloon to 4+4+2+8+8 = 26 bytes (instead of 14) —
///     length assertion catches.
///   </item>
///   <item>
///     <b><c>ListFriends</c> body regression at <c>PlayerMisc.cpp:1442</c>.</b>
///     A regression that called <c>SendClientChatList</c> with non-zero
///     iteration counts (or with a non-empty channel string) would inflate
///     the payload beyond 14 bytes — length assertion catches.
///   </item>
///   <item>
///     <b>Switch-case selector regression at <c>PlayerConnection.cpp:1720</c>.</b>
///     The literal <c>CCR_LIST_FRIENDS</c> must match
///     <c>PacketStructures.h:659</c> (= 24). A drift in either constant
///     leaves no case matching — default falls through, no reply.
///   </item>
///   <item>
///     <b><c>return;</c> → <c>break;</c> regression at
///     <c>PlayerConnection.cpp:1722</c>.</b> If <c>return</c> is flipped to
///     <c>break</c>, the post-switch if-chain at line 1731 would run; for
///     type=24 none of those branches match (CCE_SPEAK_LOCALLY=1,
///     CCE_SPEAK_ON=0, etc.), so the visible effect is nil — but the
///     subtle invariant is that <c>ListFriends</c> must be the ONLY emit
///     site reachable from CCR_LIST_FRIENDS. Future regression-tightening
///     could assert no extra frames after 0x00A4.
///   </item>
///   <item>
///     <b><c>m_NumFriends</c> initialisation regression in
///     <c>Player::FinishInit</c>.</b> If a fresh character's friend list
///     came up with stale entries (e.g. from uninitialised heap memory),
///     <c>ListFriends</c> would iterate them and emit a non-empty payload.
///     The all-zero content assertion catches.
///   </item>
///   <item>
///     <b>Variable-length string walker regression at
///     <c>PlayerConnection.cpp:1659-1692</c>.</b> Same as Wave 32 — a
///     short→int32 revert on string_length fields desyncs the walker.
///   </item>
///   <item>
///     <b>ClientChatRequest layout regression in
///     <c>PacketStructures.h:669-681</c>.</b> A long→int32 revert on
///     PlayerID or type shifts type to offset 8 — switch reads type==0
///     (CCE_SPEAK_ON) → default break → falls into if-chain →
///     CCE_SPEAK_ON branch reads string3 which is NULL → no emit, drain
///     times out.
///   </item>
///   <item>
///     <b>Dispatcher mis-route at <c>PlayerConnection.cpp:587</c>.</b>
///     The case label sits between 0x00A1 TRIGGER_EMOTE and 0x00B9
///     LOGOFF_REQUEST. A copy-paste swap with HandleTriggerEmote routes
///     our 18B payload to <c>SendNotifyEmote</c> which fans out 0x00A2
///     NOTIFY_EMOTE to range-list peers and never replies to sender —
///     test times out. A swap with HandleLogoffRequest invokes
///     <c>SendLogoffConfirmation+DropPlayerFromGalaxy</c> — sector
///     connection tears down before 0x00A4 ever emits.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode dropping 0x00A3.</b>
///     0x00A3 is NOT explicitly cased in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c> — falls through to
///     bottom-of-switch forward. A regression that re-introduced opcode
///     whitelisting would stop the server from ever receiving 0x00A3 —
///     test times out.
///   </item>
///   <item>
///     <b>Proxy <c>SendClientPacketSequence</c> guard at
///     <c>UDPProxyToClient_linux.cpp:568</c>.</b> Currently passes 0x00A4
///     (&lt; 0x0FFF). A regression tightening the upper bound would
///     silently drop the reply.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x00A3 with type=CCR_LIST_FRIENDS
/// is the exact opcode the retail Win32 client emits when the user opens
/// the Friends tab in the social UI — the retail packet carries three empty
/// strings (no Friend name, no Channel, no Message) and the type=24
/// selector. The 0x00A4 CLIENT_CHAT_LIST reply with CHAT_LIST_FRIENDS=0
/// is the verbatim retail-server confirmation. We are not making the server
/// accept any new input shape, not loosening any security posture, not
/// fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~22s; CLIENT_CHAT_REQUEST + 0x00A4 round-trip
/// sub-second; LOGOFF sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorClientChatListTests
{
    private const int ExpectedClientChatListPayloadSize = 14;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorClientChatListTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ClientChatRequest_ListFriendsEmptyFriendList_ReceivesClientChatList()
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
            firstName: "Friender", shipName: "FrienderShip", cts.Token);

        try
        {
            // 0x00A3 CLIENT_CHAT_REQUEST — 18B canonical layout.
            //   [0..4)   int32 PlayerID       = 0
            //   [4..8)   int32 type           = 24 (CCR_LIST_FRIENDS)
            //   [8..10)  short string_length1 = 0
            //   [10..12) short string_length2 = 0
            //   [12..14) short string_length3 = 0
            //   [14..18) int32 data_size      = 0
            byte[] payload = new byte[18];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 24);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(8, 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(10, 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(12, 2), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(14, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.ClientChatRequest.Value, payload),
                cts.Token);

            // Drain inbound until we see 0x00A4 CLIENT_CHAT_LIST. The
            // ListFriends/SendClientChatList chain emits exactly one
            // 0x00A4 frame per CCR_LIST_FRIENDS request — no interleaving
            // emits are expected for type=24 (the post-switch if-chain
            // is skipped due to the `return` at PlayerConnection.cpp:1722).
            int framesSeen = 0;
            const int maxFrames = 400;
            var observed = new List<string>();
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);
                observed.Add($"0x{reply!.Header.Opcode:X4}/{reply.Payload.Length}");

                if (reply.Header.Opcode != OpcodeId.Known.ClientChatList.Value)
                    continue;

                // Wire layout (14 bytes, all zero):
                //   bytes [0..4]   int32 LE ListType = 0 (CHAT_LIST_FRIENDS)
                //   bytes [4..6]   int16 LE channel_len = 0 (empty channel)
                //   bytes [6..10]  int32 LE number1 = 0 (no name entries)
                //   bytes [10..14] int32 LE number2 = 0 (no sector entries)
                Assert.Equal(ExpectedClientChatListPayloadSize, reply.Payload.Length);
                var span = reply.Payload.Span;

                Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0, 4)));
                Assert.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(span.Slice(4, 2)));
                Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(span.Slice(6, 4)));
                Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(span.Slice(10, 4)));
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x00A3 " +
                $"CLIENT_CHAT_REQUEST (type=CCR_LIST_FRIENDS=24) without " +
                $"seeing 0x00A4 CLIENT_CHAT_LIST. Observed [{observed.Count}]: " +
                $"{string.Join(" | ", observed)}");
        }
        finally
        {
            try
            {
                using var logoffCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                byte[] logoffPayload = new byte[8];
                await session.Sector.SendAsync(
                    Packet.ForOpcode(OpcodeId.Known.LogoffRequest.Value, logoffPayload),
                    logoffCts.Token);
                await SectorHandshake.DrainUntilOpcode(
                    session.Sector, OpcodeId.Known.LogoffConfirmation.Value, logoffCts.Token);
            }
            catch { /* best-effort logoff */ }

            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
