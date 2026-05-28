// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 64 direct-reply +1 ratchet: client sends a 0x00A3
/// CLIENT_CHAT_REQUEST on the sector connection with
/// type=CCE_SPEAK_LOCALLY=1, string1=&quot;Bzqxqxz64&quot; (an impossible-to-create
/// character name — no vowels — therefore guaranteed not to exist in the
/// global player list), string2="" (empty channel), string3="hi" (non-slash
/// message body). Expects the server's
/// <c>Player::SendClientChatError</c> reply at
/// <c>server/src/PlayerConnection.cpp:4667-4680</c> emitting opcode 0x00A6
/// CLIENT_CHAT_ERROR with the recipient-not-found echo.
///
/// <para>
/// Stimulus wire layout (mirror of <c>common/include/net7/PacketStructures.h:669-681</c>):
/// <code>
///   [0..4)    int32 PlayerID         = 0   (handler ignores)
///   [4..8)    int32 type             = 1   (CCE_SPEAK_LOCALLY)
///   [8..10)   short string_length1   = 9   (length of "Bzqxqxz64")
///   [10..19)  9B ASCII               = "Bzqxqxz64"
///   [19..21)  short string_length2   = 0   (no channel)
///   [21..23)  short string_length3   = 2   (length of "hi")
///   [23..25)  2B ASCII               = "hi"
///   [25..29)  int32 data_size        = 0   (no optional trailing data block)
/// </code>
/// 29 bytes total, <c>ATTRIB_PACKED</c>.
/// </para>
///
/// <para>
/// Server dispatch. <c>PlayerConnection.cpp:587</c> routes 0x00A3 to
/// <c>HandleClientChatRequest</c> at line 1648. The variable-length string
/// walker at lines 1659-1692 reads string1=9B "Bzqxqxz64", string2 empty,
/// string3=2B "hi". The switch at line 1695 has no case for type=1 so
/// default falls through and execution reaches the post-switch if-chain at
/// line 1731: <c>if (request->type == CCE_SPEAK_LOCALLY)</c> is true,
/// <c>string3 != NULL</c> is true, <c>string3[0] != '/'</c> (it's 'h') so
/// the else-branch fires:
/// <code>
///   g_ServerMgr->m_PlayerMgr.ChatSendPrivate(GameID(), string1, string3);
/// </code>
/// <c>PlayerManager::ChatSendPrivate</c> at
/// <c>server/src/PlayerManager.cpp:971-1014</c> walks
/// <c>m_GlobalPlayerList</c> via <c>GetNextPlayerOnList</c> comparing each
/// player's <c>Name()</c> against <c>nickTrim</c> via <c>strcasecmp</c>;
/// since "Bzqxqxz64" has no vowel it cannot be a real character name
/// (character creation enforces <c>G_ERROR_ONE_VOWEL</c> at the global-
/// plane <c>CreateCharacter</c> handler), so <c>FoundPlayer</c> stays
/// false. The post-loop branch at line 1008-1011 fires:
/// <code>
///   p->SendClientChatError(CHAT_ERROR_INVALID_PERSON, CCE_SPEAK_LOCALLY, Nick);
/// </code>
/// </para>
///
/// <para>
/// SendClientChatError emit (<c>PlayerConnection.cpp:4667-4680</c>):
/// <code>
///   AddData(packet, reason=4, index);    // 4B int32 LE: CHAT_ERROR_INVALID_PERSON
///   AddData(packet, type=1,   index);    // 4B int32 LE: CCE_SPEAK_LOCALLY
///   AddDataLS(packet, player="Bzqxqxz64", index);  // short(9) + 9B ASCII
///   AddDataLS(packet, channel="",         index);  // short(0)
///   AddDataLS(packet, other="",           index);  // short(0)
///   SendOpcode(ENB_OPCODE_00A6_CLIENT_CHAT_ERROR, packet, index);
/// </code>
/// Channel and Other default to <c>""</c> (declaration default in
/// <c>PlayerClass.h:1032</c>). Wire payload total: 4 + 4 + 2 + 9 + 2 + 2 =
/// <b>23 bytes</b>.
/// </para>
///
/// <para>
/// Why this wave target. 0x00A6 CLIENT_CHAT_ERROR is a previously-uncovered
/// server-emit opcode. Closing it makes 0x00A3 CLIENT_CHAT_REQUEST a
/// **complete triple-fan-out test surface** — three previously-untested
/// switch/branch arms (CCR_LIST_FRIENDS=24 from Wave 63 → 0x00A4,
/// CCR_FRIEND_STATUS_ONLY=28 from Wave 32 → 0x00A5, CCE_SPEAK_LOCALLY=1 +
/// nonexistent recipient from Wave 64 → 0x00A6), each emitting a distinct
/// server-side opcode from the same single-frame request opcode. The
/// no-vowel recipient name choice ensures determinism — character creation
/// rejects names without vowels via <c>G_ERROR_ONE_VOWEL</c>, so the
/// recipient lookup cannot accidentally collide with a real character even
/// if other parallel tests are running concurrently.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>0x00A6 SendOpcode removal at <c>PlayerConnection.cpp:4677</c>.</b>
///     Drain times out for 0x00A6 — no other code path emits this opcode
///     for a SpeakLocally-to-nonexistent-recipient flow.
///   </item>
///   <item>
///     <b><c>SendClientChatError</c> wire-shape regression at
///     <c>PlayerConnection.cpp:4669-4677</c>.</b> Any change to the
///     AddData/AddDataLS sequence — adding fields, reordering, widening
///     reason/type to 8B (the <c>AddData&lt;long&gt;</c> specialisation
///     class), changing the default-argument for channel or other —
///     surfaces via the 23B length assert or the byte-exact content
///     assertions on reason/type/player.
///   </item>
///   <item>
///     <b><c>AddData&lt;long&gt;</c> specialisation revert at
///     <c>server/src/PacketMethods.h:37-42</c>.</b> The specialisation
///     forces a 4B int32_t write on Linux x86_64. Without it, the generic
///     template would emit 8B for both <c>reason</c> and <c>type</c>
///     (both <c>long</c> in the C++ signature). Total wire payload would
///     balloon to 8+8+2+9+2+2 = 31 bytes instead of 23 — length assertion
///     catches.
///   </item>
///   <item>
///     <b><c>ChatSendPrivate</c> <c>!FoundPlayer</c> branch removal at
///     <c>PlayerManager.cpp:1008-1011</c>.</b> If the post-loop emit is
///     deleted, the recipient-not-found case becomes a silent no-op and
///     the drain times out.
///   </item>
///   <item>
///     <b><c>ChatSendPrivate</c> <c>Nick &amp;&amp; Message</c> guard
///     regression at <c>PlayerManager.cpp:973</c>.</b> The function early-
///     returns if either pointer is null. Our 9B Nick and 2B Message
///     bypass both null checks and the empty-nick early-return at line
///     979 (<c>Nick[0] == 0</c>) so the lookup loop is reached. A
///     regression that widened the guard (e.g. requiring strlen &gt;= 3
///     for Nick) would silently no-op on our 9B Nick — length assertion
///     still catches via timeout.
///   </item>
///   <item>
///     <b>Post-switch if-chain regression at <c>PlayerConnection.cpp:1731</c>.</b>
///     If the <c>request->type == CCE_SPEAK_LOCALLY</c> branch is deleted
///     or guarded behind an AdminLevel check, ChatSendPrivate never runs
///     and 0x00A6 never emits.
///   </item>
///   <item>
///     <b>Slash-dispatch over-trigger regression at
///     <c>PlayerConnection.cpp:1735</c>.</b> The branch tests
///     <c>string3[0] == '/'</c>. If a regression broadened this to e.g.
///     <c>string3[0] == 'h'</c> our message "hi" would be routed through
///     <c>HandleSlashCommands</c> instead of ChatSendPrivate and no
///     0x00A6 would emit.
///   </item>
///   <item>
///     <b>Variable-length string walker regression at
///     <c>PlayerConnection.cpp:1659-1692</c>.</b> Same as Waves 32/63 —
///     a short→int32 revert on string_length fields desyncs the walker
///     and string1/string3 land at wrong offsets, breaking the echoed
///     player name in the reply.
///   </item>
///   <item>
///     <b><c>ClientChatRequest</c> layout regression in
///     <c>PacketStructures.h:669-681</c>.</b> A long→int32 revert on
///     PlayerID or type shifts the type field to offset 8 — switch reads
///     type==0 (CCE_SPEAK_ON), falls into the SPEAK_ON branch which
///     routes to <c>ChatSendChannel</c>, no 0x00A6 emits.
///   </item>
///   <item>
///     <b>Dispatcher mis-route at <c>PlayerConnection.cpp:587</c>.</b>
///     A copy-paste swap with HandleTriggerEmote (0x00A1) routes our
///     29B payload through <c>SendNotifyEmote</c> which fans out 0x00A2
///     to range-list peers and never emits 0x00A6 — drain times out.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode dropping 0x00A3.</b>
///     0x00A3 is NOT explicitly cased in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c> — falls through to
///     bottom-of-switch forward. A regression re-introducing opcode
///     whitelisting would stop the server from receiving 0x00A3.
///   </item>
///   <item>
///     <b>Proxy <c>SendClientPacketSequence</c> guard at
///     <c>UDPProxyToClient_linux.cpp:568</c>.</b> Currently passes 0x00A6
///     (&lt; 0x0FFF). A regression tightening the upper bound silently
///     drops the reply.
///   </item>
///   <item>
///     <b><c>Player::Name()</c> postfix regression at
///     <c>PlayerConnection.cpp:10260+</c> (GetPostFix path).</b> The
///     echoed Player field in 0x00A6 is the Nick we sent verbatim (not
///     <c>Name()</c>), so this regression doesn't break Wave 64's
///     assertions — documented to clarify the diagnostic scope.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x00A3 with
/// type=CCE_SPEAK_LOCALLY is what retail Win32 client emits when the user
/// types a tell ("/tell &lt;nick&gt; &lt;msg&gt;" or via the Friends-tab
/// quick-tell button). When the recipient nick doesn't match any logged-in
/// character the retail server's <c>ChatSendPrivate</c> emits 0x00A6
/// CLIENT_CHAT_ERROR with reason=CHAT_ERROR_INVALID_PERSON=4 and the
/// recipient-name echo — verbatim retail behaviour. We are not making the
/// server accept any new input shape, not loosening any security posture,
/// not fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~22s; CLIENT_CHAT_REQUEST + 0x00A6 round-trip
/// sub-second; LOGOFF sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorClientChatErrorTests
{
    private const string NonexistentNick = "Bzqxqxz64";
    private const string Message = "hi";
    private const int ExpectedPayloadSize = 4 + 4 + 2 + 9 + 2 + 2;  // 23

    // Wave 119 nick: 8B ASCII. Content is irrelevant for the RemoveFriend
    // empty-list path — PlayerMisc.cpp:1400-1411 walks m_FriendNames with
    // strcasecmp; for a fresh char m_NumFriends==0 so the loop body never
    // runs and the post-loop "i == m_NumFriends" branch fires unconditionally.
    private const string AbsentFriendNick = "Ghost119";
    private const int RemoveFriendExpectedPayloadSize = 4 + 4 + 2 + 8 + 2 + 2;  // 22

    // Wave 120 firstName + self-nick: 7B ASCII. Has lowercase vowels 'o'+'e'
    // (satisfies AccountManager.cpp:1147 G_ERROR_ONE_VOWEL) and no triple-
    // repeating character (satisfies AccountManager.cpp:1158-1166
    // G_ERROR_REPEATING_CHAR). The stimulus AddFriend nick must match the
    // server-side Name() via strcasecmp (PlayerMisc.cpp:1363) so we send
    // the exact firstName verbatim; the server echoes the stimulus nick
    // back unchanged in the 0x00A6 reply.
    private const string SelfNick = "Yorself";
    private const int AddFriendSelfExpectedPayloadSize = 4 + 4 + 2 + 7 + 2 + 2;  // 21

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorClientChatErrorTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ClientChatRequest_SpeakLocallyNonexistentRecipient_ReceivesClientChatError()
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
            firstName: "Erroria", shipName: "ErroriaShip", cts.Token);

        try
        {
            // 0x00A3 CLIENT_CHAT_REQUEST — variable-length 29B layout.
            byte[] nickBytes = Encoding.ASCII.GetBytes(NonexistentNick);
            byte[] msgBytes = Encoding.ASCII.GetBytes(Message);
            int payloadSize = 4 + 4 + 2 + nickBytes.Length + 2 + 2 + msgBytes.Length + 4;
            byte[] payload = new byte[payloadSize];
            int o = 0;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(o, 4), 0); o += 4;             // PlayerID
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(o, 4), 1); o += 4;             // type = CCE_SPEAK_LOCALLY
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(o, 2), (short)nickBytes.Length); o += 2;
            nickBytes.CopyTo(payload, o); o += nickBytes.Length;
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(o, 2), 0); o += 2;             // string_length2 = 0
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(o, 2), (short)msgBytes.Length); o += 2;
            msgBytes.CopyTo(payload, o); o += msgBytes.Length;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(o, 4), 0);                     // data_size

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.ClientChatRequest.Value, payload),
                cts.Token);

            // Drain inbound until we see 0x00A6 CLIENT_CHAT_ERROR.
            int framesSeen = 0;
            const int maxFrames = 400;
            var observed = new List<string>();
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);
                observed.Add($"0x{reply!.Header.Opcode:X4}/{reply.Payload.Length}");

                if (reply.Header.Opcode != OpcodeId.Known.ClientChatError.Value)
                    continue;

                // Wire layout (23 bytes):
                //   bytes [0..4]   int32 LE reason  = 4 (CHAT_ERROR_INVALID_PERSON)
                //   bytes [4..8]   int32 LE type    = 1 (CCE_SPEAK_LOCALLY)
                //   bytes [8..10]  int16 LE player_len = 9
                //   bytes [10..19] 9B ASCII player  = "Bzqxqxz64"
                //   bytes [19..21] int16 LE channel_len = 0
                //   bytes [21..23] int16 LE other_len   = 0
                Assert.Equal(ExpectedPayloadSize, reply.Payload.Length);
                var span = reply.Payload.Span;

                Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0, 4)));
                Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4)));
                Assert.Equal((short)9, BinaryPrimitives.ReadInt16LittleEndian(span.Slice(8, 2)));
                Assert.Equal(NonexistentNick, Encoding.ASCII.GetString(span.Slice(10, 9)));
                Assert.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(span.Slice(19, 2)));
                Assert.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(span.Slice(21, 2)));
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x00A3 CLIENT_CHAT_REQUEST " +
                $"(type=CCE_SPEAK_LOCALLY=1, nick=\"{NonexistentNick}\", msg=\"{Message}\") " +
                $"without seeing 0x00A6 CLIENT_CHAT_ERROR. Observed [{observed.Count}]: " +
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

    /// <summary>
    /// Wave 119 sibling-arm pin on the 0x00A6 CLIENT_CHAT_ERROR emit. Where
    /// Wave 64 covered the T2 post-switch arm
    /// <c>request->type == CCE_SPEAK_LOCALLY</c> reaching
    /// <c>ChatSendPrivate</c>'s recipient-not-found branch
    /// (<c>PlayerManager.cpp:1008-1011</c>), this wave covers the parallel
    /// T1 in-switch arm <c>case CCE_REMOVE_FRIEND</c> at
    /// <c>PlayerConnection.cpp:1697-1699</c> which routes straight to
    /// <c>Player::RemoveFriend</c>. For a fresh character with
    /// <c>m_NumFriends == 0</c>, the friend-list loop at
    /// <c>PlayerMisc.cpp:1400-1407</c> never executes; the post-loop
    /// <c>i == m_NumFriends</c> check at line 1408 fires unconditionally
    /// and dispatches to
    /// <c>SendClientChatError(CHAT_ERROR_NOT_A_MEMBER, CCE_REMOVE_FRIEND, name)</c>.
    ///
    /// <para>
    /// Stimulus (26B 0x00A3):
    /// <code>
    ///   [0..4)    int32 PlayerID         = 0
    ///   [4..8)    int32 type             = 9   (CCE_REMOVE_FRIEND)
    ///   [8..10)   short string_length1   = 8
    ///   [10..18)  8B ASCII               = "Ghost119"
    ///   [18..20)  short string_length2   = 0
    ///   [20..22)  short string_length3   = 0
    ///   [22..26)  int32 data_size        = 0
    /// </code>
    /// </para>
    ///
    /// <para>
    /// Reply assertion (22B 0x00A6) — mirror of <c>SendClientChatError</c>
    /// at <c>PlayerConnection.cpp:4667-4680</c>:
    /// <code>
    ///   [0..4)   int32 LE reason       = 6 (CHAT_ERROR_NOT_A_MEMBER)
    ///   [4..8)   int32 LE type         = 9 (CCE_REMOVE_FRIEND)
    ///   [8..10)  int16 LE player_len   = 8
    ///   [10..18) 8B ASCII player       = "Ghost119"
    ///   [18..20) int16 LE channel_len  = 0
    ///   [20..22) int16 LE other_len    = 0
    /// </code>
    /// Total 4+4+2+8+2+2 = 22 bytes.
    /// </para>
    ///
    /// <para>
    /// Regression classes this catches beyond Wave 64.
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>T1 switch dispatch regression at
    ///     <c>PlayerConnection.cpp:1697-1699</c>.</b> Wave 64 exercises the
    ///     T2 post-switch if-chain only. A regression that
    ///     deletes/renumbers/reorders the <c>case CCE_REMOVE_FRIEND</c>
    ///     case label (e.g. swapping with CCE_ADD_FRIEND=8 or
    ///     CCE_UNIGNORE=11) silently routes our type=9 stimulus to a
    ///     different friend-list mutator. Wave 64's CCE_SPEAK_LOCALLY=1
    ///     stimulus does not touch the switch at all (it has no case for
    ///     type=1) so Wave 64 cannot detect T1 dispatch regressions.
    ///   </item>
    ///   <item>
    ///     <b><c>RemoveFriend</c> empty-list fall-through at
    ///     <c>PlayerMisc.cpp:1408-1411</c>.</b> If the post-loop emit is
    ///     deleted or guarded behind a non-zero check, the
    ///     remove-not-in-list case becomes a silent no-op and 0x00A6 never
    ///     emits.
    ///   </item>
    ///   <item>
    ///     <b><c>RemoveFriend</c> name-arg threading at
    ///     <c>PlayerMisc.cpp:1410</c>.</b> The <c>name</c> parameter
    ///     passed to <c>SendClientChatError</c> is the same pointer the
    ///     loop walked. A regression that swapped <c>name</c> with an
    ///     empty-string sentinel or an unrelated buffer would echo back
    ///     the wrong nick and the byte-exact "Ghost119" assertion catches.
    ///   </item>
    ///   <item>
    ///     <b><c>CHAT_ERROR_NOT_A_MEMBER</c> value drift at
    ///     <c>PacketStructures.h:709</c> (=6).</b> A renumber would surface
    ///     as a byte-1 mismatch on the reason field.
    ///   </item>
    ///   <item>
    ///     <b><c>CCE_REMOVE_FRIEND</c> value drift at
    ///     <c>PacketStructures.h:644</c> (=9).</b> A renumber would
    ///     surface on the type field, and the stimulus would also no
    ///     longer route to the correct switch arm.
    ///   </item>
    ///   <item>
    ///     <b><c>SendClientChatError</c> default-arg-default regression at
    ///     <c>PlayerClass.h:1032</c>.</b> The declaration defaults
    ///     <c>channel</c> and <c>other</c> to non-NULL empty string. A
    ///     change to NULL would make <c>AddDataLS</c> emit 0 bytes each
    ///     instead of <c>short(0)</c>, dropping the payload to 18 bytes
    ///     and breaking the 22B length assertion.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity note (per CLAUDE.md). 0x00A3 with
    /// type=CCE_REMOVE_FRIEND is what the retail Win32 client emits when
    /// the user removes a friend via the Friends-tab UI or the
    /// <c>/remfriend &lt;name&gt;</c> slash command. When the named friend
    /// is not on the player's friend list the retail server emits 0x00A6
    /// CLIENT_CHAT_ERROR with reason=CHAT_ERROR_NOT_A_MEMBER and the
    /// queried name echoed — verbatim retail behaviour. We are not making
    /// the server accept any new input shape, not loosening any security
    /// posture, not fabricating any reply.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s. Handshake ~22s; CLIENT_CHAT_REQUEST + 0x00A6
    /// round-trip sub-second; LOGOFF sub-second.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ClientChatRequest_RemoveFriendNotInList_PinsExactReplyWireShape()
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
            // 0x00A3 CLIENT_CHAT_REQUEST — 26B variable-length layout,
            // type=CCE_REMOVE_FRIEND=9, string1="Ghost119" (the friend
            // name we're asking the server to remove from our empty list).
            byte[] nickBytes = Encoding.ASCII.GetBytes(AbsentFriendNick);
            int payloadSize = 4 + 4 + 2 + nickBytes.Length + 2 + 2 + 4;
            byte[] payload = new byte[payloadSize];
            int o = 0;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(o, 4), 0); o += 4;             // PlayerID
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(o, 4), 9); o += 4;             // type = CCE_REMOVE_FRIEND
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(o, 2), (short)nickBytes.Length); o += 2;
            nickBytes.CopyTo(payload, o); o += nickBytes.Length;
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(o, 2), 0); o += 2;             // string_length2 = 0
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(o, 2), 0); o += 2;             // string_length3 = 0
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(o, 4), 0);                     // data_size

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.ClientChatRequest.Value, payload),
                cts.Token);

            // Drain inbound until we see 0x00A6 CLIENT_CHAT_ERROR.
            int framesSeen = 0;
            const int maxFrames = 400;
            var observed = new List<string>();
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);
                observed.Add($"0x{reply!.Header.Opcode:X4}/{reply.Payload.Length}");

                if (reply.Header.Opcode != OpcodeId.Known.ClientChatError.Value)
                    continue;

                // Wire layout (22 bytes):
                //   bytes [0..4]   int32 LE reason  = 6 (CHAT_ERROR_NOT_A_MEMBER)
                //   bytes [4..8]   int32 LE type    = 9 (CCE_REMOVE_FRIEND)
                //   bytes [8..10]  int16 LE player_len = 8
                //   bytes [10..18] 8B ASCII player  = "Ghost119"
                //   bytes [18..20] int16 LE channel_len = 0
                //   bytes [20..22] int16 LE other_len   = 0
                Assert.Equal(RemoveFriendExpectedPayloadSize, reply.Payload.Length);
                var span = reply.Payload.Span;

                Assert.Equal(6, BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0, 4)));
                Assert.Equal(9, BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4)));
                Assert.Equal((short)8, BinaryPrimitives.ReadInt16LittleEndian(span.Slice(8, 2)));
                Assert.Equal(AbsentFriendNick, Encoding.ASCII.GetString(span.Slice(10, 8)));
                Assert.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(span.Slice(18, 2)));
                Assert.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(span.Slice(20, 2)));
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x00A3 CLIENT_CHAT_REQUEST " +
                $"(type=CCE_REMOVE_FRIEND=9, nick=\"{AbsentFriendNick}\") without seeing " +
                $"0x00A6 CLIENT_CHAT_ERROR. Observed [{observed.Count}]: " +
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

    /// <summary>
    /// Wave 120 sibling-arm pin on the 0x00A6 CLIENT_CHAT_ERROR emit. Where
    /// Wave 119 covered the T1 switch arm <c>case CCE_REMOVE_FRIEND=9</c>
    /// routing to <c>RemoveFriend</c>'s empty-list fall-through emitting
    /// <c>reason=CHAT_ERROR_NOT_A_MEMBER=6</c>, this wave covers the
    /// adjacent T1 switch arm <c>case CCE_ADD_FRIEND=8</c> at
    /// <c>PlayerConnection.cpp:1694-1696</c> routing to
    /// <c>Player::AddFriend</c> at <c>PlayerMisc.cpp:1351-1394</c> with a
    /// stimulus nick equal to the player's own <c>Name()</c>, hitting the
    /// self-reject branch at <c>PlayerMisc.cpp:1363-1366</c> which emits
    /// <c>SendClientChatError(CHAT_ERROR_YOURSELF=17, CCE_ADD_FRIEND=8, name)</c>.
    ///
    /// <para>
    /// Stimulus (25B 0x00A3):
    /// <code>
    ///   [0..4)    int32 PlayerID         = 0
    ///   [4..8)    int32 type             = 8   (CCE_ADD_FRIEND)
    ///   [8..10)   short string_length1   = 7
    ///   [10..17)  7B ASCII               = "Yorself"
    ///   [17..19)  short string_length2   = 0
    ///   [19..21)  short string_length3   = 0
    ///   [21..25)  int32 data_size        = 0
    /// </code>
    /// The 7B nick is the same string as the avatar's <c>firstName</c>
    /// (case-sensitive equality — server's <c>strcasecmp</c> at
    /// <c>PlayerMisc.cpp:1363</c> is case-insensitive but we use verbatim
    /// equality so the echo assertion has a known byte sequence).
    /// </para>
    ///
    /// <para>
    /// Reply assertion (21B 0x00A6) — mirror of <c>SendClientChatError</c>
    /// at <c>PlayerConnection.cpp:4667-4680</c>:
    /// <code>
    ///   [0..4)   int32 LE reason       = 17 (CHAT_ERROR_YOURSELF)
    ///   [4..8)   int32 LE type         = 8  (CCE_ADD_FRIEND)
    ///   [8..10)  int16 LE player_len   = 7
    ///   [10..17) 7B ASCII player       = "Yorself"
    ///   [17..19) int16 LE channel_len  = 0
    ///   [19..21) int16 LE other_len    = 0
    /// </code>
    /// Total 4+4+2+7+2+2 = 21 bytes.
    /// </para>
    ///
    /// <para>
    /// Why this wave target. Wave 119 pinned the T1 arm
    /// <c>case CCE_REMOVE_FRIEND</c> with reason=6. Wave 120 pins the
    /// IMMEDIATELY ADJACENT T1 arm <c>case CCE_ADD_FRIEND</c> with
    /// reason=17 — both arms call the same <c>SendClientChatError</c>
    /// emit fn through different intermediate handlers (RemoveFriend vs
    /// AddFriend), and Wave 120 exclusively exercises the
    /// <c>strcasecmp(name, Name())</c> self-name check at
    /// <c>PlayerMisc.cpp:1363</c> which has no Wave 119 counterpart
    /// (RemoveFriend has no self-name guard — it walks the friend list
    /// regardless of whether <c>name</c> matches the player's own name).
    /// </para>
    ///
    /// <para>
    /// Regression classes this catches beyond Wave 119.
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>T1 switch arm <c>case CCE_ADD_FRIEND=8</c> deletion or
    ///     reorder at <c>PlayerConnection.cpp:1694-1696</c>.</b> Wave 119
    ///     only exercises <c>case CCE_REMOVE_FRIEND=9</c>. A regression
    ///     swapping the two case bodies (a natural copy-paste error —
    ///     both arms call a single-arg friend-list mutator with the same
    ///     <c>string1</c> argument) would route our type=8 stimulus to
    ///     <c>RemoveFriend</c> which would emit reason=6 not reason=17 —
    ///     Wave 120's byte[0]==0x11 reason pin trips.
    ///   </item>
    ///   <item>
    ///     <b><c>AddFriend</c> self-name guard deletion at
    ///     <c>PlayerMisc.cpp:1363-1366</c>.</b> If the
    ///     <c>strcasecmp(name, Name()) == 0</c> guard is removed, the
    ///     self-add path falls into the <c>else if (i == m_NumFriends)</c>
    ///     branch at line 1367, the strcpy_s at line 1371 stores the
    ///     player's own name in <c>m_FriendNames[0]</c>, SaveFriendsList
    ///     runs, and SendClientChatEvent(CHEV_NOW_FRIENDS) emits 0x00A5
    ///     instead of 0x00A6 — Wave 120 traps via drain-timeout
    ///     XunitException after 400 frames without seeing 0x00A6.
    ///   </item>
    ///   <item>
    ///     <b><c>CHAT_ERROR_YOURSELF=17</c> constant drift at
    ///     <c>PacketStructures.h:712</c>.</b> Wave 119 pins reason=6
    ///     (CHAT_ERROR_NOT_A_MEMBER); a renumber on CHAT_ERROR_YOURSELF
    ///     is invisible to Wave 119 but Wave 120's byte[0]==0x11 catches.
    ///   </item>
    ///   <item>
    ///     <b><c>CCE_ADD_FRIEND=8</c> constant drift at
    ///     <c>PacketStructures.h:643</c>.</b> Wave 119 pins type=9; a
    ///     renumber on CCE_ADD_FRIEND would surface on the type field
    ///     AND would also misroute the T1 switch dispatch since the
    ///     <c>case CCE_ADD_FRIEND</c> label is now a different integer —
    ///     double-asymmetric catch.
    ///   </item>
    ///   <item>
    ///     <b><c>strcasecmp</c> → <c>strcmp</c> case-sensitivity
    ///     regression at <c>PlayerMisc.cpp:1363</c>.</b> The retail server
    ///     uses case-insensitive comparison so "yorself" or "YORSELF"
    ///     both match the player's "Yorself" Name(). The test sends the
    ///     case-exact verbatim nick so a regression to case-sensitive
    ///     comparison stays green on Wave 120 specifically — this is a
    ///     known coverage gap that would be closed by a follow-on wave
    ///     using mixed-case stimulus.
    ///   </item>
    ///   <item>
    ///     <b><c>AddFriend</c> argument-routing regression at
    ///     <c>PlayerMisc.cpp:1365</c>.</b> If a refactor passed
    ///     <c>Name()</c> instead of <c>name</c> to the
    ///     SendClientChatError third arg, the echoed player field in the
    ///     reply would still be "Yorself" (since stimulus nick equals
    ///     Name() in this test) — Wave 120 specifically does not catch
    ///     this swap, but it does pin the existing wire shape against
    ///     any change that returns a buffer of a different length.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity note (per CLAUDE.md). 0x00A3 with
    /// type=CCE_ADD_FRIEND is what the retail Win32 client emits when the
    /// user invokes the <c>/addfriend &lt;name&gt;</c> slash command or
    /// uses the Friends-tab UI add. When the queried name matches the
    /// player's own name the retail server emits 0x00A6 with
    /// reason=CHAT_ERROR_YOURSELF — verbatim retail behaviour. We are
    /// not making the server accept any new input shape, not loosening
    /// any security posture, not fabricating any reply.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s. Handshake ~22s; CLIENT_CHAT_REQUEST + 0x00A6
    /// round-trip sub-second; LOGOFF sub-second.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ClientChatRequest_AddFriendSelfName_PinsExactReplyWireShape()
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
            firstName: SelfNick, shipName: "YorselfShip", cts.Token);

        try
        {
            // 0x00A3 CLIENT_CHAT_REQUEST — 25B variable-length layout,
            // type=CCE_ADD_FRIEND=8, string1="Yorself" (the player's own
            // first name — triggers the self-reject branch at
            // PlayerMisc.cpp:1363-1366 via strcasecmp(name, Name()) == 0).
            byte[] nickBytes = Encoding.ASCII.GetBytes(SelfNick);
            int payloadSize = 4 + 4 + 2 + nickBytes.Length + 2 + 2 + 4;
            byte[] payload = new byte[payloadSize];
            int o = 0;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(o, 4), 0); o += 4;             // PlayerID
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(o, 4), 8); o += 4;             // type = CCE_ADD_FRIEND
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(o, 2), (short)nickBytes.Length); o += 2;
            nickBytes.CopyTo(payload, o); o += nickBytes.Length;
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(o, 2), 0); o += 2;             // string_length2 = 0
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(o, 2), 0); o += 2;             // string_length3 = 0
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(o, 4), 0);                     // data_size

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.ClientChatRequest.Value, payload),
                cts.Token);

            // Drain inbound until we see 0x00A6 CLIENT_CHAT_ERROR.
            int framesSeen = 0;
            const int maxFrames = 400;
            var observed = new List<string>();
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);
                observed.Add($"0x{reply!.Header.Opcode:X4}/{reply.Payload.Length}");

                if (reply.Header.Opcode != OpcodeId.Known.ClientChatError.Value)
                    continue;

                // Wire layout (21 bytes):
                //   bytes [0..4]   int32 LE reason  = 17 (CHAT_ERROR_YOURSELF)
                //   bytes [4..8]   int32 LE type    = 8 (CCE_ADD_FRIEND)
                //   bytes [8..10]  int16 LE player_len = 7
                //   bytes [10..17] 7B ASCII player  = "Yorself"
                //   bytes [17..19] int16 LE channel_len = 0
                //   bytes [19..21] int16 LE other_len   = 0
                Assert.Equal(AddFriendSelfExpectedPayloadSize, reply.Payload.Length);
                var span = reply.Payload.Span;

                Assert.Equal(17, BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0, 4)));
                Assert.Equal(8, BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4)));
                Assert.Equal((short)7, BinaryPrimitives.ReadInt16LittleEndian(span.Slice(8, 2)));
                Assert.Equal(SelfNick, Encoding.ASCII.GetString(span.Slice(10, 7)));
                Assert.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(span.Slice(17, 2)));
                Assert.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(span.Slice(19, 2)));
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x00A3 CLIENT_CHAT_REQUEST " +
                $"(type=CCE_ADD_FRIEND=8, nick=\"{SelfNick}\" matching self Name()) " +
                $"without seeing 0x00A6 CLIENT_CHAT_ERROR. Observed [{observed.Count}]: " +
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
