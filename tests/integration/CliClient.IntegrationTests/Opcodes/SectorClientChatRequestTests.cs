// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
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

    /// <summary>
    /// Wave 113 sibling byte-exact pin on the same 0x00A3 CLIENT_CHAT_REQUEST
    /// (CCR_FRIEND_STATUS_ONLY) → 0x00A5 CLIENT_CHAT_EVENT direct-reply
    /// path probed by
    /// <see cref="ClientChatRequest_FriendStatusOnlyBranch_ReceivesClientChatEvent"/>,
    /// but pinning the COMPLETE 54-byte reply payload byte-for-byte
    /// instead of only the first 4 bytes of the Type field.
    ///
    /// <para>
    /// SIXTH byte-exact upgrade of a direct-reply assertion in Phase K
    /// (after Waves 108/109/110/111/112). Targets a NEW framing pattern
    /// not previously exercised byte-exact: AddDataLS — a length-prefixed
    /// (short LE) string with NO NUL terminator. Wave 32's existing
    /// assertion only pins <c>body[0..4) == {0x19,0x00,0x00,0x00}</c>
    /// (Type=25 little-endian), leaving 50 bytes of the body
    /// unverified — including the entire string-encoding wire shape, the
    /// three trailing empty AddDataLS short(0) length-prefixes, the
    /// blank short(0), and the trailing int32(0) block-length.
    /// </para>
    ///
    /// <para>
    /// Wave 113 firstName "Cher113" (7 ASCII bytes; vowel 'e' satisfies
    /// the AccountManager.cpp:1147 G_ERROR_ONE_VOWEL check; no triple
    /// repeats satisfies the AccountManager.cpp:1158-1166
    /// G_ERROR_REPEATING_CHAR check — "11" peaks count at 1, 2 repeats).
    /// </para>
    ///
    /// <para>
    /// PRESERVATION DISCOVERY (Wave 113). Test characters created by
    /// the integration harness are ADMIN admin-level (=100), not USER
    /// (=0) as the existing Wave 32 docstring assumed. The seeded test
    /// accounts at Fixtures/seed.sql carry <c>status=100</c> to satisfy
    /// the global UDP plane's ProcessTicketInfo gate (G_ERROR 12
    /// otherwise), and at character creation
    /// <c>AccountManager.cpp:1221</c> initialises the avatar's
    /// admin_level from <c>GetAccountStatus(account_username)</c>
    /// directly: <c>database.info.admin_level = ntohl(GetAccountStatus(...))</c>.
    /// The account status 100 matches the <c>ADMIN</c> constant at
    /// <c>server/src/Net7.h:366</c>, so every test character is ADMIN.
    /// This propagates into <c>Player::GetPostFix</c> at
    /// <c>PlayerConnection.cpp:10303</c>'s <c>case ADMIN</c> branch which
    /// sets <c>PostFix = "ADMIN"</c>, and since <c>PostFix[0]</c> is
    /// non-empty, line 10323 produces <c>FName = "Cher113 [ADMIN]"</c>
    /// (15 bytes) via <c>snprintf(FName, length, "%s [%s]", Name(),
    /// PostFix)</c>. This is the LastName emitted on the wire, NOT the
    /// raw firstName.
    /// </para>
    ///
    /// <para>
    /// SECONDARY DISCOVERY. The four trailing string parameters of
    /// <c>Player::SendClientChatEvent</c> default to <b>empty string
    /// literal</b> <c>""</c>, not NULL, per the declaration at
    /// <c>server/src/PlayerClass.h:1031</c>:
    /// <code>
    ///   void SendClientChatEvent(long Type, Player *Source,
    ///                            char *Channel="", char *Message="",
    ///                            char *OtherPlayer="", char *NonPlayerSrc="");
    /// </code>
    /// The call site <c>SendClientChatEvent(CHEV_FRIEND_STATUS_ONLY,
    /// this)</c> at PlayerConnection.cpp:1709-1710 picks up
    /// Channel=Message=OtherPlayer="". AddDataLS at
    /// <c>PacketMethods.h:66-74</c> guards on <c>if (mydata)</c> — a
    /// non-null pointer to <c>""</c> PASSES the guard and emits
    /// <c>short(strlen("")) == short(0)</c> followed by zero bytes of
    /// payload, i.e. 2 bytes per empty-string AddDataLS. So the three
    /// AddDataLS(OtherPlayer/Channel/Message) calls each emit 2 bytes,
    /// for 6 bytes total — NOT zero as a NULL-pointer reading of the
    /// signature would suggest.
    /// </para>
    ///
    /// <para>
    /// Reply wire layout (mirror of <c>Player::SendClientChatEvent</c>
    /// at <c>server/src/PlayerConnection.cpp:10331-10381</c> for
    /// Source==this, ADMIN admin level, no friends, all string args
    /// defaulting to empty):
    /// <code>
    ///   [0..4)    int32 LE   Type      = 25 (CHEV_FRIEND_STATUS_ONLY)
    ///   [4..8)    int32 LE   unknown   = 0  (spacer)
    ///   [8..10)   short LE   LastName.len = 15
    ///   [10..25)  ASCII      LastName  = "Cher113 [ADMIN]"
    ///   [25..27)  short LE   LastName.len = 15 (duplicated emit)
    ///   [27..42)  ASCII      LastName  = "Cher113 [ADMIN]"
    ///   [42..44)  short LE   OtherPlayer.len = 0
    ///   [44..46)  short LE   Channel.len     = 0
    ///   [46..48)  short LE   Message.len     = 0
    ///   [48..50)  short LE   blank           = 0
    ///   [50..54)  int32 LE   blockLen        = 0
    /// </code>
    /// 54 bytes total. The two duplicated AddDataLS(LastName) emits
    /// are the retail wire idiom — historically one would have been
    /// "Rank" but it was commented out at PlayerConnection.cpp:10366,
    /// leaving LastName emitted twice.
    /// </para>
    ///
    /// <para>
    /// Concrete regressions THIS sibling catches that Wave 32 does NOT:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>AdminLevel-derived PostFix path mis-selection.</b> A
    ///     regression in GetPostFix's <c>case ADMIN</c> branch at
    ///     PlayerConnection.cpp:10303-10305 (e.g. dropping the case,
    ///     renaming the postfix string from "ADMIN" to "ADM", or moving
    ///     the case below the default) would change LastName from
    ///     "Cher113 [ADMIN]" to "Cher113" or "Cher113 [ADM]" — both
    ///     length deltas from 15. Wave 32 never inspects LastName;
    ///     Wave 113's byte[8..10) short(15) length pin and verbatim
    ///     [10..25) "Cher113 [ADMIN]" string pin trip immediately.
    ///   </item>
    ///   <item>
    ///     <b>AccountManager AdminLevel-from-status initialisation.</b>
    ///     A regression at AccountManager.cpp:1221 that broke the
    ///     <c>admin_level = ntohl(GetAccountStatus(...))</c> seeding
    ///     (e.g. zeroed admin_level on every new character, or read from
    ///     the wrong column) would push our test character to USER,
    ///     changing LastName back to bare "Cher113" — Wave 113's pins
    ///     trip on the length mismatch.
    ///   </item>
    ///   <item>
    ///     <b>AddDataLS empty-string emit divergence.</b> A change that
    ///     short-circuited <c>if (mydata &amp;&amp; *mydata)</c> instead
    ///     of <c>if (mydata)</c> at PacketMethods.h:66-74 would skip the
    ///     short(0) emit for empty strings, shrinking the reply by 6
    ///     bytes (54→48). Wave 32's body[0..4) check is structurally
    ///     blind; Wave 113's <c>span.Length == 54</c> length assertion
    ///     trips, and the trailing short(0)/int32(0) offset pins
    ///     pinpoint which empty-string emit went missing.
    ///   </item>
    ///   <item>
    ///     <b>LastName duplicated emit.</b> The retail wire idiom is two
    ///     LastName emits (PlayerConnection.cpp:10367-10368). A refactor
    ///     that "cleaned up" the dup to a single emit would shrink the
    ///     body by 17 bytes (54→37). Wave 32 is blind; Wave 113 trips
    ///     both via length and via the byte-[27..42) firstName-with-
    ///     postfix verbatim pins.
    ///   </item>
    ///   <item>
    ///     <b>Spacer / trailing field drift.</b> The int32(0) spacer at
    ///     offset 4 and trailing short(0)×4 + int32(0) at offsets
    ///     42/44/46/48/50 are Wave 32 blind spots. A regression that
    ///     swapped any of these for a different sentinel (e.g. -1 for
    ///     unknown, or omitted the trailing blank-string short) trips
    ///     Wave 113's exact-zero pins.
    ///   </item>
    ///   <item>
    ///     <b>AddData&lt;long&gt; vs AddData&lt;short&gt; mix-up.</b>
    ///     The body interleaves int32 (4B) and short (2B) AddData calls;
    ///     a width regression on any AddData call would shift every
    ///     subsequent offset. Wave 113's byte-position-anchored pins
    ///     trip independently per field — pinpoints WHICH width
    ///     regression by which Assert fires first.
    ///   </item>
    ///   <item>
    ///     <b>Variable-length string walker request-side.</b> The
    ///     handler's variable-length walker at
    ///     PlayerConnection.cpp:1659-1692 has to parse our 18B request
    ///     with three short(0) length-prefixes; a regression that mis-
    ///     consumed the string-length fields would mis-route or
    ///     mis-extract <c>type</c> and the switch would land on a
    ///     different (or no) case. Wave 32 already catches that as a
    ///     "no 0x00A5 reply" timeout; Wave 113 additionally pins the
    ///     emit shape so any regression that DOES emit 0x00A5 (e.g. via
    ///     a different mis-routed case label) but with a different body
    ///     shape is caught.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity note. No new server behaviour, no loosening of
    /// input acceptance — the 18B request is exactly what the retail
    /// Win32 client emits when the user toggles "Only friends can see
    /// my status" and the 54B response is exactly what the retail
    /// server's SendClientChatEvent path produces for a 7-character
    /// avatar firstName at ADMIN admin level with no friends. The
    /// AdminLevel=ADMIN baseline is a property of the test fixture's
    /// seeded account status (100), not server divergence: a retail
    /// player with status=ADMIN would see exactly this wire shape.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s. Handshake ~2s; CLIENT_CHAT_REQUEST + 0x00A5
    /// round-trip is sub-second.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ClientChatRequest_FriendStatusOnlyBranch_PinsExactReplyWireShape()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        const string FirstName = "Cher113";
        const string ExpectedLastName = "Cher113 [ADMIN]";
        const int ExpectedLastNameByteCount = 15;
        const int ExpectedReplyPayloadLength = 54;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: FirstName, shipName: "Cher113Ship", cts.Token);

        try
        {
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

            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.ClientChatEvent.Value)
                    continue;

                var span = reply.Payload.Span;
                if (span.Length < 4) continue;
                int type = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);
                if (type != 25) continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(25, type);
                Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4)));

                // First LastName emit: short(15) + "Cher113 [ADMIN]"
                Assert.Equal((short)ExpectedLastNameByteCount,
                    BinaryPrimitives.ReadInt16LittleEndian(span.Slice(8, 2)));
                Assert.Equal(ExpectedLastName,
                    Encoding.ASCII.GetString(span.Slice(10, ExpectedLastNameByteCount)));

                // Duplicated LastName emit: short(15) + "Cher113 [ADMIN]"
                int second = 10 + ExpectedLastNameByteCount;
                Assert.Equal((short)ExpectedLastNameByteCount,
                    BinaryPrimitives.ReadInt16LittleEndian(span.Slice(second, 2)));
                Assert.Equal(ExpectedLastName,
                    Encoding.ASCII.GetString(span.Slice(second + 2, ExpectedLastNameByteCount)));

                // Three AddDataLS("") empty-string emits — short(0) each.
                int afterDupName = second + 2 + ExpectedLastNameByteCount;
                Assert.Equal((short)0,
                    BinaryPrimitives.ReadInt16LittleEndian(span.Slice(afterDupName, 2)));      // OtherPlayer.len
                Assert.Equal((short)0,
                    BinaryPrimitives.ReadInt16LittleEndian(span.Slice(afterDupName + 2, 2)));  // Channel.len
                Assert.Equal((short)0,
                    BinaryPrimitives.ReadInt16LittleEndian(span.Slice(afterDupName + 4, 2)));  // Message.len

                // Trailing AddData((short)0) blank + AddData(0L→int32) block-length.
                Assert.Equal((short)0,
                    BinaryPrimitives.ReadInt16LittleEndian(span.Slice(afterDupName + 6, 2)));
                Assert.Equal(0,
                    BinaryPrimitives.ReadInt32LittleEndian(span.Slice(afterDupName + 8, 4)));
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x00A3 CLIENT_CHAT_REQUEST " +
                $"(type=CCR_FRIEND_STATUS_ONLY=28) without seeing 0x00A5 CLIENT_CHAT_EVENT " +
                $"with Type=25 for byte-exact pin.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 116 sibling byte-exact pin on the ADJACENT switch arm to
    /// Wave 113 (<see cref="ClientChatRequest_FriendStatusOnlyBranch_PinsExactReplyWireShape"/>),
    /// distinguished from Wave 113 by exactly ONE BYTE on the wire — the
    /// 0x00A5 reply's Type field flips from 25 (CHEV_FRIEND_STATUS_ONLY)
    /// to 26 (CHEV_ALL_STATUS) — and ONE BIT of server state — the
    /// <c>m_StatusToFriendsOnly</c> flag flips from <c>true</c> to
    /// <c>false</c>.
    ///
    /// <para>
    /// 0x00A5 CLIENT_CHAT_EVENT is +0 (already counted by Wave 32 with
    /// the CHEV_FRIEND_STATUS_ONLY=25 Type variant; coverage stays at
    /// 105/207 = 50.7%). 0x00A3 CLIENT_CHAT_REQUEST is +0 (already
    /// counted by Wave 32 with the type=28 CCR_FRIEND_STATUS_ONLY arm).
    /// </para>
    ///
    /// <para>
    /// SECOND multi-arm dispatcher sibling-arm-pinning wave (after
    /// Wave 115 which pinned the CCR_LIST_IGNORES arm vs Wave 63's
    /// CCR_LIST_FRIENDS arm of the same SendClientChatList emit fn).
    /// </para>
    ///
    /// <para>
    /// Wave 113 covered <c>case CCR_FRIEND_STATUS_ONLY</c> at
    /// <c>server/src/PlayerConnection.cpp:1706-1709</c>:
    /// <code>
    ///     case CCR_FRIEND_STATUS_ONLY:
    ///         m_StatusToFriendsOnly = true;
    ///         SendClientChatEvent(CHEV_FRIEND_STATUS_ONLY, this);
    ///         break;
    /// </code>
    /// Wave 116 covers the <b>immediately-following</b>
    /// <c>case CCR_ANYONE_STATUS</c> at
    /// <c>server/src/PlayerConnection.cpp:1710-1713</c>:
    /// <code>
    ///     case CCR_ANYONE_STATUS:
    ///         m_StatusToFriendsOnly = false;
    ///         SendClientChatEvent(CHEV_ALL_STATUS, this);
    ///         break;
    /// </code>
    /// </para>
    ///
    /// <para>
    /// Wire deltas vs Wave 113:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Request payload byte 4</b> flips from 28 (0x1C) to 29
    ///     (0x1D) — the dispatcher's <c>request->type</c> field. Per
    ///     <c>common/include/net7/PacketStructures.h:663-664</c>:
    ///     <code>
    ///       #define CCR_FRIEND_STATUS_ONLY 28
    ///       #define CCR_ANYONE_STATUS      29
    ///     </code>
    ///   </item>
    ///   <item>
    ///     <b>Reply payload byte 0</b> flips from 25 (0x19) to 26
    ///     (0x1A) — the <c>SendClientChatEvent</c> body's <c>Type</c>
    ///     field. Per <c>common/include/net7/PacketStructures.h:756-757</c>:
    ///     <code>
    ///       #define CHEV_FRIEND_STATUS_ONLY 25
    ///       #define CHEV_ALL_STATUS         26
    ///     </code>
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// All other 53 bytes of the 54-byte reply are byte-identical to
    /// Wave 113 (same firstName-driven LastName "Cher116 [ADMIN]"
    /// duplicated emit, same three trailing empty-string AddDataLS
    /// short(0) emits, same short(0) blank, same int32(0) block-length).
    /// </para>
    ///
    /// <para>
    /// Concrete regressions THIS sibling catches that Wave 113 does NOT:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>case CCR_ANYONE_STATUS dispatch deletion or fall-through</b>
    ///     at <c>PlayerConnection.cpp:1710-1713</c>. Wave 113 only
    ///     exercises the CCR_FRIEND_STATUS_ONLY arm; if a refactor
    ///     accidentally deleted the CCR_ANYONE_STATUS case label or
    ///     merged it into the default branch, our 18B type=29 request
    ///     would silently no-op (no reply) — Wave 116 traps via
    ///     assertion-timeout. Wave 113 stays green.
    ///   </item>
    ///   <item>
    ///     <b>CHEV_ALL_STATUS constant drift</b> at
    ///     <c>PacketStructures.h:757</c>. If the constant changed from
    ///     26 to any other value, Wave 113's CHEV_FRIEND_STATUS_ONLY=25
    ///     assertion stays green (different constant) but Wave 116's
    ///     byte[0]==0x1A pin trips.
    ///   </item>
    ///   <item>
    ///     <b>CCR_ANYONE_STATUS → CHEV_ALL_STATUS routing scramble</b>.
    ///     If someone copy-pasted the CCR_FRIEND_STATUS_ONLY body into
    ///     the CCR_ANYONE_STATUS case (calling
    ///     <c>SendClientChatEvent(CHEV_FRIEND_STATUS_ONLY, this)</c>
    ///     from the type=29 arm), Wave 113 sees its own correct reply
    ///     and stays green; Wave 116 sees Type=25 instead of 26 and
    ///     trips on the byte[0]==0x1A pin. This is the cleanest
    ///     copy-paste-error catch in the dispatcher.
    ///   </item>
    ///   <item>
    ///     <b><c>m_StatusToFriendsOnly = false</c> assignment drift</b>.
    ///     The two arms differ in the per-player flag setting (true vs
    ///     false). Wave 116 doesn't directly inspect the flag, but if a
    ///     regression swapped the two assignments (CCR_ANYONE_STATUS
    ///     setting true), the test character's status would be reported
    ///     to friends only thereafter — caught indirectly via downstream
    ///     IsIgnored / m_TellsFromFriendsOnly tests, but Wave 116
    ///     surfaces the wrong reply Type as the proximate symptom.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Why a paired sibling test rather than parameterising Wave 113:
    /// the regression classes above are catchable ONLY by exercising
    /// both arms independently. A parameterised <c>[Theory]</c> would
    /// still satisfy the catch surface, but a discrete <c>[Fact]</c>
    /// per arm produces clearer failure attribution (one method name
    /// fails) and matches the established Phase K sibling-arm-pinning
    /// convention (Wave 63 + Wave 115 on the same SendClientChatList
    /// emit).
    /// </para>
    ///
    /// <para>
    /// Server-integrity note. No new server behaviour, no loosening of
    /// input acceptance — the 18B request is exactly what the retail
    /// Win32 client emits when the user toggles "Everyone can see my
    /// status" (the inverse of the Wave 113 toggle), and the 54B
    /// response is exactly what the retail server's
    /// SendClientChatEvent path produces for a 7-character avatar
    /// firstName at ADMIN admin level with no friends.
    /// </para>
    ///
    /// <para>
    /// Wave 116 firstName "Cher116" — 7 ASCII bytes; vowel 'e' satisfies
    /// the AccountManager.cpp:1147 G_ERROR_ONE_VOWEL check; "11" peaks
    /// at 2 repeats (well under triple) so passes
    /// AccountManager.cpp:1158-1166 G_ERROR_REPEATING_CHAR. Same
    /// LastName-byte-length of 15 ("Cher116 [ADMIN]") as Wave 113's
    /// "Cher113 [ADMIN]", so the 54-byte reply length and offsets are
    /// byte-identical to Wave 113 except for byte[0].
    /// </para>
    ///
    /// <para>
    /// Budget: 90s. Handshake ~2s; CLIENT_CHAT_REQUEST + 0x00A5
    /// round-trip is sub-second.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ClientChatRequest_AnyoneStatusBranch_PinsExactReplyWireShape()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        const string FirstName = "Cher116";
        const string ExpectedLastName = "Cher116 [ADMIN]";
        const int ExpectedLastNameByteCount = 15;
        const int ExpectedReplyPayloadLength = 54;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: FirstName, shipName: "Cher116Ship", cts.Token);

        try
        {
            // 0x00A3 CLIENT_CHAT_REQUEST — 18B canonical layout.
            //   [0..4)   int32 PlayerID       = 0  (handler ignores)
            //   [4..8)   int32 type           = 29 (CCR_ANYONE_STATUS)
            //   [8..10)  short string_length1 = 0
            //   [10..12) short string_length2 = 0
            //   [12..14) short string_length3 = 0
            //   [14..18) int32 data_size      = 0
            byte[] payload = new byte[18];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 29);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(8, 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(10, 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(12, 2), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(14, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.ClientChatRequest.Value, payload),
                cts.Token);

            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.ClientChatEvent.Value)
                    continue;

                var span = reply.Payload.Span;
                if (span.Length < 4) continue;
                int type = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);
                if (type != 26) continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(26, type);
                Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4)));

                // First LastName emit: short(15) + "Cher116 [ADMIN]"
                Assert.Equal((short)ExpectedLastNameByteCount,
                    BinaryPrimitives.ReadInt16LittleEndian(span.Slice(8, 2)));
                Assert.Equal(ExpectedLastName,
                    Encoding.ASCII.GetString(span.Slice(10, ExpectedLastNameByteCount)));

                // Duplicated LastName emit: short(15) + "Cher116 [ADMIN]"
                int second = 10 + ExpectedLastNameByteCount;
                Assert.Equal((short)ExpectedLastNameByteCount,
                    BinaryPrimitives.ReadInt16LittleEndian(span.Slice(second, 2)));
                Assert.Equal(ExpectedLastName,
                    Encoding.ASCII.GetString(span.Slice(second + 2, ExpectedLastNameByteCount)));

                // Three AddDataLS("") empty-string emits — short(0) each.
                int afterDupName = second + 2 + ExpectedLastNameByteCount;
                Assert.Equal((short)0,
                    BinaryPrimitives.ReadInt16LittleEndian(span.Slice(afterDupName, 2)));
                Assert.Equal((short)0,
                    BinaryPrimitives.ReadInt16LittleEndian(span.Slice(afterDupName + 2, 2)));
                Assert.Equal((short)0,
                    BinaryPrimitives.ReadInt16LittleEndian(span.Slice(afterDupName + 4, 2)));

                // Trailing AddData((short)0) blank + AddData(0L→int32) block-length.
                Assert.Equal((short)0,
                    BinaryPrimitives.ReadInt16LittleEndian(span.Slice(afterDupName + 6, 2)));
                Assert.Equal(0,
                    BinaryPrimitives.ReadInt32LittleEndian(span.Slice(afterDupName + 8, 4)));
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x00A3 CLIENT_CHAT_REQUEST " +
                $"(type=CCR_ANYONE_STATUS=29) without seeing 0x00A5 CLIENT_CHAT_EVENT " +
                $"with Type=26 (CHEV_ALL_STATUS) for byte-exact pin.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 117 sibling byte-exact pin on the POST-SWITCH if-else branch
    /// of <c>HandleClientChatRequest</c> at
    /// <c>server/src/PlayerConnection.cpp:1788-1791</c>:
    /// <code>
    ///     else if (request->type == CCR_LIST_CHANNELS)
    ///     {
    ///         SendVaMessage("Request channel list");
    ///     }
    /// </code>
    ///
    /// <para>
    /// Drives a 0x00A3 CLIENT_CHAT_REQUEST with <c>type=25</c>
    /// (CCR_LIST_CHANNELS per
    /// <c>common/include/net7/PacketStructures.h:660</c>), threads
    /// through the switch which has NO matching case (Wave 32/113/116
    /// covered case labels 28/29; Wave 115/63 covered 23/24; no case
    /// label = 25 exists), falls through the switch's default branch,
    /// then matches the post-switch <c>else if</c> chain at line 1788
    /// firing <c>SendVaMessage("Request channel list")</c>.
    /// <c>SendVaMessage</c> at <c>PlayerClass.cpp:3415-3425</c> walks
    /// va_args through <c>vsprintf_s</c> into a stack scratch buffer
    /// then calls <c>SendMessageString(pch)</c> with the default
    /// <c>colour=5</c> per <c>PlayerClass.h:277</c>. The literal
    /// <c>"Request channel list"</c> has NO printf format specifiers
    /// so vsprintf_s emits the literal verbatim — 20 ASCII bytes.
    /// </para>
    ///
    /// <para>
    /// Reply wire layout (mirror of <c>SendMessageString</c> at
    /// <c>server/src/PlayerConnection.cpp:10987-10997</c>):
    /// <code>
    ///   [0..2)   short LE   length-field = 21 (strlen + NUL)
    ///   [2]      byte       colour       = 5  (SendMessageString default)
    ///   [3..23)  ASCII      payload      = "Request channel list" (20 bytes)
    ///   [23]     byte       NUL          = 0x00
    /// </code>
    /// 24 bytes total (length + 3).
    /// </para>
    ///
    /// <para>
    /// NINTH byte-exact upgrade of a direct-reply assertion in Phase K
    /// (after Waves 108/109/110/111/112/113/114/115/116). FIFTH
    /// SendMessageString-flavour byte-exact wave (after Waves
    /// 108/109/110/111). FIRST byte-exact wave to exercise the
    /// <b>post-switch if-else fall-through branch</b> of a multi-arm
    /// dispatcher — Waves 113/116 hit cases INSIDE the switch; Wave 115
    /// pinned the CCR_LIST_IGNORES case inside the switch; Wave 117
    /// uniquely exercises the post-switch <c>else if (request->type ==
    /// CCR_LIST_CHANNELS)</c> path that fires AFTER the switch breaks.
    /// </para>
    ///
    /// <para>
    /// Coverage delta. 0x00A3 already counted by Wave 32; 0x001D already
    /// counted by Wave 24/27/etc; coverage stays at 105/207 = 50.7%.
    /// </para>
    ///
    /// <para>
    /// Concrete regressions THIS sibling catches that Waves 32/113/116
    /// do NOT:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Post-switch if-else fall-through deletion or reorder.</b>
    ///     If a refactor accidentally deleted the <c>else if
    ///     (request->type == CCR_LIST_CHANNELS)</c> branch, or moved it
    ///     above one of the <c>SPEAK_ON/SPEAK_LOCALLY/ENTER_CHANNEL</c>
    ///     branches, our type=25 request would silently no-op (no
    ///     reply). Wave 117 traps via timeout. Wave 32/113/116 keep
    ///     passing because they only exercise inside-switch cases.
    ///   </item>
    ///   <item>
    ///     <b>SendVaMessage va_args walker regression.</b> A regression
    ///     in <c>SendVaMessage</c>'s <c>vsprintf_s</c> or
    ///     <c>strlen+_alloca</c> path that mis-allocated the scratch
    ///     buffer (e.g. <c>len = strlen(string)</c> without the +256
    ///     headroom) could overflow on long literals; the 20-byte
    ///     literal here is short so the overflow wouldn't fire, but a
    ///     regression that read length from the WRONG buffer (e.g.
    ///     hard-coded zero) would emit an empty MessageString. Wave 117
    ///     traps with length-field-not-21 / verbatim-literal mismatch.
    ///   </item>
    ///   <item>
    ///     <b>Literal drift.</b> "Request channel list" is also a
    ///     candidate for a user-visible polish edit (e.g. "Request
    ///     Channel List" capitalisation, or "Channels list" rename).
    ///     Wave 117's verbatim Assert.Equal catches any drift.
    ///   </item>
    ///   <item>
    ///     <b>CCR_LIST_CHANNELS=25 constant drift</b> at
    ///     PacketStructures.h:660. If renumbered, no branch matches —
    ///     timeout.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Wave 117 firstName "Chnel117" — 8 ASCII bytes; vowel 'e'
    /// satisfies AccountManager.cpp:1147 G_ERROR_ONE_VOWEL; "11" peaks
    /// at 2 repeats (under triple) so passes
    /// AccountManager.cpp:1158-1166 G_ERROR_REPEATING_CHAR. firstName
    /// is irrelevant to the wire content (SendMessageString emits the
    /// literal, not Name()), so any printable-vowel-bearing name works.
    /// </para>
    ///
    /// <para>
    /// Server-integrity note. No new server behaviour, no loosening of
    /// input acceptance — the 18B request is exactly what the retail
    /// Win32 client emits when the user opens the "Channels" menu and
    /// the 24B response is exactly what the retail server's
    /// SendVaMessage→SendMessageString path produces for this literal.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s. Handshake ~2s; CLIENT_CHAT_REQUEST + 0x001D
    /// round-trip is sub-second.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ClientChatRequest_ListChannelsBranch_PinsExactReplyWireShape()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;

        const string FirstName = "Chnel117";
        const string ExpectedLiteral = "Request channel list";
        const int ExpectedLengthField = 21;   // strlen("Request channel list") + NUL
        const byte ExpectedColour = 5;         // SendMessageString default
        const int ExpectedReplyPayloadLength = 24; // length + 3

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: FirstName, shipName: "Chnel117Ship", cts.Token);

        try
        {
            byte[] payload = new byte[18];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 25);  // CCR_LIST_CHANNELS
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(8, 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(10, 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(12, 2), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(14, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.ClientChatRequest.Value, payload),
                cts.Token);

            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.MessageString.Value)
                    continue;

                var span = reply.Payload.Span;
                if (span.Length < 3) continue;
                short lengthField = BinaryPrimitives.ReadInt16LittleEndian(span[..2]);
                if (lengthField != ExpectedLengthField) continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal((short)ExpectedLengthField, lengthField);
                Assert.Equal(ExpectedColour, span[2]);
                Assert.Equal(ExpectedLiteral,
                    Encoding.ASCII.GetString(span.Slice(3, ExpectedLiteral.Length)));
                Assert.Equal((byte)0x00, span[3 + ExpectedLiteral.Length]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x00A3 CLIENT_CHAT_REQUEST " +
                $"(type=CCR_LIST_CHANNELS=25) without seeing 0x001D MESSAGE_STRING " +
                $"with length-field=21 for byte-exact pin.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 118 sibling byte-exact pin on the SECOND post-switch
    /// if-else branch — sibling to Wave 117's CCR_LIST_CHANNELS=25
    /// branch — at <c>server/src/PlayerConnection.cpp:1792-1795</c>:
    /// <code>
    ///     else if (request->type == CCR_LIST_ALL_CHANNELS)
    ///     {
    ///         SendVaMessage("Request all channels list");
    ///     }
    /// </code>
    ///
    /// <para>
    /// Drives a 0x00A3 CLIENT_CHAT_REQUEST with <c>type=26</c>
    /// (CCR_LIST_ALL_CHANNELS per
    /// <c>common/include/net7/PacketStructures.h:661</c>), the literal
    /// emitted is <c>"Request all channels list"</c> (25 ASCII bytes,
    /// distinct from Wave 117's <c>"Request channel list"</c> 20-byte
    /// literal).
    /// </para>
    ///
    /// <para>
    /// Reply wire layout (mirror of <c>SendMessageString</c> at
    /// <c>server/src/PlayerConnection.cpp:10987-10997</c>):
    /// <code>
    ///   [0..2)   short LE   length-field = 26 (strlen + NUL)
    ///   [2]      byte       colour       = 5  (SendMessageString default)
    ///   [3..28)  ASCII      payload      = "Request all channels list" (25 bytes)
    ///   [28]     byte       NUL          = 0x00
    /// </code>
    /// 29 bytes total (length + 3).
    /// </para>
    ///
    /// <para>
    /// TENTH byte-exact upgrade of a direct-reply assertion in Phase K
    /// (after Waves 108/109/110/111/112/113/114/115/116/117). SIXTH
    /// SendMessageString-flavour byte-exact wave. SECOND post-switch
    /// if-else fall-through byte-exact wave (sibling to Wave 117 on
    /// the same dispatcher's T2 chain). THIRD multi-arm dispatcher
    /// sibling-arm-pinning wave (after Wave 115's CCR_LIST_IGNORES vs
    /// CCR_LIST_FRIENDS and Wave 116's CCR_ANYONE_STATUS vs
    /// CCR_FRIEND_STATUS_ONLY) — but FIRST sibling pair on the T2
    /// post-switch chain rather than the T1 inside-switch case-label
    /// chain.
    /// </para>
    ///
    /// <para>
    /// Coverage delta. 0x00A3 already counted; 0x001D already counted;
    /// coverage stays at 105/207 = 50.7%.
    /// </para>
    ///
    /// <para>
    /// Concrete regressions THIS sibling catches that Wave 117 does NOT:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>case CCR_LIST_ALL_CHANNELS branch deletion or reorder.</b>
    ///     Wave 117 only exercises the CCR_LIST_CHANNELS arm; if a
    ///     refactor deleted the <c>else if (request->type ==
    ///     CCR_LIST_ALL_CHANNELS)</c> branch at line 1792, our type=26
    ///     request would silently no-op. Wave 117 keeps passing.
    ///   </item>
    ///   <item>
    ///     <b>Literal drift.</b> "Request all channels list" is a
    ///     user-visible polish-edit candidate distinct from Wave 117's
    ///     "Request channel list" — the words "all channels" are a
    ///     natural target for rename (e.g. "global channels", "world
    ///     channels"). Wave 117 is structurally blind; Wave 118's
    ///     verbatim Assert.Equal catches.
    ///   </item>
    ///   <item>
    ///     <b>CCR_LIST_ALL_CHANNELS=26 constant drift</b> at
    ///     PacketStructures.h:661. If renumbered, no branch matches —
    ///     timeout.
    ///   </item>
    ///   <item>
    ///     <b>CCR_LIST_CHANNELS=25 vs CCR_LIST_ALL_CHANNELS=26
    ///     copy-paste swap.</b> If someone copy-pasted the
    ///     CCR_LIST_CHANNELS body into the CCR_LIST_ALL_CHANNELS arm
    ///     (calling SendVaMessage("Request channel list") for type=26),
    ///     Wave 117 stays green (its arm still emits its literal); Wave
    ///     118 sees the WRONG literal and trips on the verbatim ASCII
    ///     pin — the cleanest sibling-arm copy-paste catch.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Wave 118 firstName "Chnel118" — 8 ASCII bytes; vowel 'e'
    /// satisfies AccountManager.cpp:1147; "11" peaks at 2 repeats.
    /// </para>
    ///
    /// <para>
    /// Server-integrity note. No new server behaviour, no loosening of
    /// input acceptance — the 18B request is exactly what the retail
    /// Win32 client emits when the user opens the "All Channels" menu
    /// and the 29B response is the verbatim retail-server reply.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ClientChatRequest_ListAllChannelsBranch_PinsExactReplyWireShape()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;

        const string FirstName = "Chnel118";
        const string ExpectedLiteral = "Request all channels list";
        const int ExpectedLengthField = 26;   // strlen + NUL
        const byte ExpectedColour = 5;
        const int ExpectedReplyPayloadLength = 29;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: FirstName, shipName: "Chnel118Ship", cts.Token);

        try
        {
            byte[] payload = new byte[18];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 26);  // CCR_LIST_ALL_CHANNELS
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(8, 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(10, 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(12, 2), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(14, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.ClientChatRequest.Value, payload),
                cts.Token);

            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.MessageString.Value)
                    continue;

                var span = reply.Payload.Span;
                if (span.Length < 3) continue;
                short lengthField = BinaryPrimitives.ReadInt16LittleEndian(span[..2]);
                if (lengthField != ExpectedLengthField) continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal((short)ExpectedLengthField, lengthField);
                Assert.Equal(ExpectedColour, span[2]);
                Assert.Equal(ExpectedLiteral,
                    Encoding.ASCII.GetString(span.Slice(3, ExpectedLiteral.Length)));
                Assert.Equal((byte)0x00, span[3 + ExpectedLiteral.Length]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x00A3 CLIENT_CHAT_REQUEST " +
                $"(type=CCR_LIST_ALL_CHANNELS=26) without seeing 0x001D MESSAGE_STRING " +
                $"with length-field=26 for byte-exact pin.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
