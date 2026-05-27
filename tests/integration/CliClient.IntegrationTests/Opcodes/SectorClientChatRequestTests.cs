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
/// Wave 32 direct-reply round-trip: client sends an 18-byte 0x00A3
/// CLIENT_CHAT_REQUEST on the sector connection with type=28
/// (CCR_FRIEND_STATUS_ONLY), expects the server's unconditional
/// 0x00A5 CLIENT_CHAT_EVENT reply carrying CHEV_FRIEND_STATUS_ONLY
/// (=25) in the first int32 of the body.
///
/// <para>
/// Wire layout (mirror of
/// <c>common/include/net7/PacketStructures.h:669-681</c>):
/// <code>
///   [0..4)   int32 PlayerID         = 0  (handler ignores; switch on `type`)
///   [4..8)   int32 type             = 28 (CCR_FRIEND_STATUS_ONLY)
///   [8..10)  short string_length1   = 0  (no Friend name string)
///   [10..12) short string_length2   = 0  (no Channel string)
///   [12..14) short string_length3   = 0  (no Message string)
///   [14..18) int32 data_size        = 0  (no optional trailing data block)
/// </code>
/// 18 bytes total, <c>ATTRIB_PACKED</c>, no implicit padding. The struct
/// in the header is variable-length (the three `char stringN[1]` fields
/// are zero-or-more byte runs each prefixed by the preceding `short
/// _string_lengthN`), so the canonical "empty all three strings" shape
/// collapses to short(0) × 3 + int32(0).
/// </para>
///
/// <para>
/// Server handler. The dispatcher case at
/// <c>server/src/PlayerConnection.cpp:587</c> calls
/// <c>HandleClientChatRequest(data)</c>. The handler at line 1648 casts
/// the data pointer to <c>ClientChatRequest*</c> and walks the
/// variable-length string layout (lines 1659-1692): it reads
/// <c>string_length1</c>, advances by 2 bytes, optionally copies
/// length1 bytes into <c>m_ScratchBuffer</c> (skipped here — length1=0),
/// then reads <c>_string_length2</c>, then <c>_string_length3</c>,
/// then the trailing <c>_data_size</c> int32. With our all-zero
/// lengths, every <c>memcpy_s</c> is skipped and the string-skip walker
/// fast-paths through to the switch.
/// </para>
///
/// <para>
/// Branch selection. The switch on <c>request->type</c> at line 1695
/// hits <c>case CCR_FRIEND_STATUS_ONLY</c> at line 1709
/// (PacketStructures.h:663 — <c>#define CCR_FRIEND_STATUS_ONLY 28</c>),
/// runs the two-statement body:
/// <code>
///     m_StatusToFriendsOnly = true;
///     SendClientChatEvent(CHEV_FRIEND_STATUS_ONLY, this);
/// </code>
/// then <c>break</c>. The setting of <c>m_StatusToFriendsOnly</c> is a
/// per-player chat-receive policy bit (benign for a fresh-starbase
/// character with no friends). After the switch breaks, the long
/// if-else chain at lines 1731-1803 is skipped because type=28 doesn't
/// match any of CCE_SPEAK_LOCALLY / CCE_SPEAK_ON / CCE_ENTER_CHANNEL /
/// CCE_EXIT_CHANNEL / CCE_INSERT_CHANNEL / CCR_LIST_CHANNELS /
/// CCR_LIST_ALL_CHANNELS / CCR_SECTOR_LOGIN. The handler returns
/// cleanly without further side-effects.
/// </para>
///
/// <para>
/// SendClientChatEvent emit path
/// (<c>server/src/PlayerConnection.cpp:10318-10369</c>). For a fresh
/// char calling with <c>Source==this</c>:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>IsIgnored(self) guard at line 10328</b> — returns false
///     (<c>m_NumIgnore==0</c>, <c>PlayerMisc.cpp:1445-1455</c>
///     short-circuits on empty ignore list).
///   </item>
///   <item>
///     <b>m_TellsFromFriendsOnly+PRIVATE_MESSAGE guard at line 10336</b>
///     — doesn't apply: <c>Type==CHEV_FRIEND_STATUS_ONLY</c> is not
///     <c>CHEV_PRIVATE_MESSAGE</c>.
///   </item>
///   <item>
///     <b>GetPostFix at line 10341</b> — for fresh-char
///     <c>AdminLevel==USER==0</c>, falls into the
///     <c>AdminLevel &lt; HELPER</c> default branch at
///     <c>PlayerConnection.cpp:10294</c> which
///     <c>snprintf(FName, length, "%s", Name())</c> — no "[XYZ]"
///     postfix, just the avatar's first name.
///   </item>
///   <item>
///     <b>Body assembly at lines 10351-10360</b> — built via
///     AddData/AddDataLS (PacketMethods.h:24-42 and 66-75):
///     <code>
///       AddData(Packet, Type=25, Index);    // int32 LE, 4B
///       AddData(Packet, 0,       Index);    // int32 spacer, 4B
///       AddDataLS(Packet, LastName, Index); // short(len)+bytes
///       AddDataLS(Packet, LastName, Index); // duplicated — retail wire idiom
///       AddDataLS(Packet, NULL,  Index);    // OtherPlayer — short(0)
///       AddDataLS(Packet, NULL,  Index);    // Channel — short(0)
///       AddDataLS(Packet, NULL,  Index);    // Message — short(0)
///       AddData(Packet, (short)0, Index);   // unknown blank — short(0)
///       AddData(Packet, 0,       Index);    // unknown data-size — int32(0)
///     </code>
///   </item>
///   <item>
///     <b>SendOpcode(0x00A5, Packet, Index) at line 10368</b> — per-
///     client UDP queue add (the Phase K sizeof(int32_t) header fix at
///     PlayerConnection.cpp:127 keeps the per-client UDP queue header
///     at the canonical 4-byte width). The next SendPacketCache tick
///     flushes synchronously via m_UDPConnection-&gt;SendResponse.
///   </item>
/// </list>
///
/// <para>
/// Wire assertion. The first 4 bytes of the body carry the int32 LE
/// <c>Type</c> field (=25 = CHEV_FRIEND_STATUS_ONLY per
/// PacketStructures.h:756). Pinning <c>body[0..4) == {0x19, 0x00,
/// 0x00, 0x00}</c> is the most robust check: it doesn't depend on the
/// avatar name length (LastName encodes to short+bytes whose length
/// varies with firstName), it survives admin-level changes (no
/// post-fix path lights up for USER), and it differentiates this
/// branch from CHEV_PRIVATE_MESSAGE (=4) / CHEV_CHANNEL_MESSAGE (=3) /
/// any other type the server might mis-emit if the switch case were
/// scrambled.
/// </para>
///
/// <para>
/// Why this wave target. 0x00A3 CLIENT_CHAT_REQUEST is a +2 ratchet
/// (both 0x00A3 and 0x00A5 previously uncovered), the CCR_FRIEND_STATUS_ONLY
/// branch is deterministic (no spawned threads, no SendVaMessage→
/// MESSAGE_STRING dependency on a particular admin level, no
/// channel/friend-list state assumption), and the handler is a clean
/// switch-on-type so the wire payload directly selects the path.
/// CCR_FRIEND_STATUS_ONLY (type=28) was chosen over CCR_LIST_CHANNELS
/// (type=25) because that branch emits 0x001D MESSAGE_STRING which is
/// already covered (Waves 8/24/26/27/30 hit it via SendVaMessage); the
/// type=28 path emits the previously-uncovered 0x00A5
/// CLIENT_CHAT_EVENT giving +2 ratchet instead of +1.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at server/src/PlayerConnection.cpp:587.</b>
///     The case label sits in the ~200-entry dispatcher between
///     0x00A1 TRIGGER_EMOTE and 0x00B9 LOGOFF_REQUEST. A copy-paste
///     swap with HandleTriggerEmote would route our 18B payload into
///     that handler's SendNotifyEmote which fans out 0x00A2
///     NOTIFY_EMOTE to range-list peers and never replies to sender —
///     test times out. A swap with HandleLogoffRequest would invoke
///     SendLogoffConfirmation+DropPlayerFromGalaxy — test sees a
///     surprise 0x00BA and the sector connection tears down before
///     0x00A5 ever emits.
///   </item>
///   <item>
///     <b>ClientChatRequest layout regression in PacketStructures.h:669-681.</b>
///     A long→int32 revert on either PlayerID or type would shift type
///     to offset 8 (where our short(0) sits) — the switch would read
///     type==0 which hits no case label and the default branch returns
///     without emitting 0x00A5 → test times out. The current canonical
///     2× int32 + 3× short + int32 = 18B layout is locked by the
///     payload-position assertion.
///   </item>
///   <item>
///     <b>Variable-length string walker regression at lines 1659-1692.</b>
///     A regression that treated string_length1 as int32 instead of
///     short would consume 4 bytes (covering length1 + length2) and
///     mis-read length2 as the high half of length3, eventually
///     reading data_size from past the 18B payload into the next
///     packet's bytes — undefined behaviour, may or may not reach the
///     switch.
///   </item>
///   <item>
///     <b>Switch case-25 selector regression at line 1709.</b> The
///     literal 28 must match PacketStructures.h:663 (which itself
///     defines CCR_FRIEND_STATUS_ONLY=28). A drift in either constant
///     leaves no case matching — default falls through, no reply.
///   </item>
///   <item>
///     <b>SendClientChatEvent guards firing on CHEV_FRIEND_STATUS_ONLY.</b>
///     IsIgnored(self) returning true (m_IgnoreNames seeded with own
///     name) → early return at line 10334 with no reply.
///     m_TellsFromFriendsOnly+CHEV_PRIVATE_MESSAGE guard widening to
///     match CHEV_FRIEND_STATUS_ONLY → emits CHAT_ERROR (0x00A4) at
///     line 10338 instead of CLIENT_CHAT_EVENT.
///   </item>
///   <item>
///     <b>AddData&lt;long&gt; width revert at PacketMethods.h:38-42.</b>
///     If AddData(Type=25) wrote 8 bytes instead of 4 (the Phase K
///     Wave 11 fix class), body[0..3] would still read 0x19000000 but
///     body[4..7] would overlap into the (0) spacer field rather than
///     being the spacer itself — only the body[0..3] check passes,
///     but the body would be 4 bytes longer than retail. We don't
///     assert total body length (LastName-dependent) but the
///     body[0..3] anchor is preserved.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode dropping 0x00A3.</b>
///     0x00A3 is NOT explicitly cased in
///     proxy/ClientToServer_linux_stubs.cpp so it falls through to
///     the bottom-of-switch forward. A regression that re-introduced
///     opcode whitelisting or that broke the default-case forward
///     would stop the server from ever receiving 0x00A3 → no 0x00A5
///     reply → test times out.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence guard at
///     UDPProxyToClient_linux.cpp:568.</b> Currently gates inner
///     opcodes on <c>opcode &gt; 0x0000 &amp;&amp; opcode &lt; 0x0FFF</c>.
///     0x00A5 passes (0x00A5 &lt; 0x0FFF). A regression that tightened
///     the upper bound to e.g. &lt; 0x0080 would silently drop the
///     reply — test times out.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x00A3 CLIENT_CHAT_REQUEST
/// with type=CCR_FRIEND_STATUS_ONLY is exactly what the retail Win32
/// client emits when the user toggles the chat-options checkbox "Only
/// friends can see my status / Only friends can send me tells" — the
/// retail packet carries three empty strings (no Friend name, no
/// Channel, no Message) and the type=28 selector. The 0x00A5
/// CLIENT_CHAT_EVENT reply carrying CHEV_FRIEND_STATUS_ONLY=25 is the
/// verbatim retail-server confirmation echoed back to the originator
/// so the client UI can update the indicator. We are not making the
/// server accept any new input shape, not loosening any security
/// posture, not fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; CLIENT_CHAT_REQUEST + 0x00A5
/// round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorClientChatRequestTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorClientChatRequestTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ClientChatRequest_FriendStatusOnlyBranch_ReceivesClientChatEvent()
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
            firstName: "Chatter", shipName: "ChatterShip", cts.Token);

        try
        {
            // 0x00A3 CLIENT_CHAT_REQUEST — 18B canonical layout.
            //   [0..4)   int32 PlayerID       = 0  (handler ignores)
            //   [4..8)   int32 type           = 28 (CCR_FRIEND_STATUS_ONLY)
            //   [8..10)  short string_length1 = 0
            //   [10..12) short string_length2 = 0
            //   [12..14) short string_length3 = 0
            //   [14..18) int32 data_size      = 0
            byte[] payload = new byte[18];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 28);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(8, 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(10, 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(12, 2), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(14, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.ClientChatRequest.Value, payload),
                cts.Token);

            // Drain inbound until we see 0x00A5 CLIENT_CHAT_EVENT with
            // Type=CHEV_FRIEND_STATUS_ONLY (=25) in the first int32 LE
            // of the body. The handler emits exactly one 0x00A5 frame
            // per CCR_FRIEND_STATUS_ONLY request — no interleaving
            // emits are expected for the type=28 path (the SendAuxShip/
            // SendNotifyEmote/SendPositionalUpdate sites are all
            // unrelated to chat-policy toggles).
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.ClientChatEvent.Value)
                    continue;

                // 0x00A5 wire layout: first int32 LE is Type. Pin
                // CHEV_FRIEND_STATUS_ONLY=25 → 0x19,0x00,0x00,0x00.
                // Robust to LastName length (avatar firstName +
                // optional admin postfix), survives admin-level
                // changes (no postfix path lights up for USER), and
                // differentiates this branch from any other Type the
                // server could mis-emit if the switch were scrambled.
                Assert.True(reply.Payload.Length >= 4,
                    $"0x00A5 body too short: {reply.Payload.Length} bytes (expected >= 4 for Type int32)");
                Assert.Equal((byte)0x19, reply.Payload.Span[0]);
                Assert.Equal((byte)0x00, reply.Payload.Span[1]);
                Assert.Equal((byte)0x00, reply.Payload.Span[2]);
                Assert.Equal((byte)0x00, reply.Payload.Span[3]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x00A3 " +
                $"CLIENT_CHAT_REQUEST (type=CCR_FRIEND_STATUS_ONLY=28) " +
                $"without seeing 0x00A5 CLIENT_CHAT_EVENT. Likely causes: " +
                $"(a) dispatcher mis-route at PlayerConnection.cpp:587 " +
                $"(swap with HandleTriggerEmote sends 0x00A2 NOTIFY_EMOTE " +
                $"to range-list peers only, swap with HandleLogoffRequest " +
                $"tears down sector before 0x00A5 emits); (b) " +
                $"ClientChatRequest layout regression at " +
                $"PacketStructures.h:669-681 (long-revert shifts type to " +
                $"offset 8 where our short(0) sits — switch reads type==0 " +
                $"→ default branch, no reply); (c) variable-length string " +
                $"walker regression at PlayerConnection.cpp:1659-1692 " +
                $"(short→int32 revert on string_length fields desyncs the " +
                $"walker); (d) CCR_FRIEND_STATUS_ONLY case-label revert " +
                $"at PlayerConnection.cpp:1709 (drift of the constant in " +
                $"PacketStructures.h:663 from 28); (e) SendClientChatEvent " +
                $"early-return: IsIgnored(self) returning true, or the " +
                $"m_TellsFromFriendsOnly+PRIVATE_MESSAGE guard widening " +
                $"to match CHEV_FRIEND_STATUS_ONLY → emits 0x00A4 " +
                $"CLIENT_CHAT_ERROR instead; (f) proxy 0x00A3 forward " +
                $"regression in ClientToServer_linux_stubs.cpp (0x00A3 " +
                $"falls through to bottom-of-switch default-case forward); " +
                $"(g) proxy SendClientPacketSequence guard tightening at " +
                $"UDPProxyToClient_linux.cpp:568 (opcode < 0x0FFF " +
                $"currently passes 0x00A5).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
