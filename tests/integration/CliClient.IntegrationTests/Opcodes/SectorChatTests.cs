// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// First post-START sector opcode round-trip: client sends 0x0033
/// CLIENT_CHAT with <see cref="ChatChannel.Group"/> while ungrouped,
/// expects the server's "Error: You are not in a group!" 0x001D
/// MESSAGE_STRING reply.
///
/// <para>
/// Exercises the full server→client server-side opcode pipeline that
/// every in-sector reply rides:
/// </para>
/// <list type="bullet">
///   <item><c>Player::HandleClientChat</c>
///         (server/src/PlayerConnection.cpp:4544) — the chat type
///         dispatch landing in the <c>type == 1</c> / no-group branch
///         which calls <c>SendVaMessage</c>.</item>
///   <item><c>Player::SendVaMessage</c> → <c>SendMessageString</c>
///         (PlayerConnection.cpp:10918) — builds the
///         [u16 length][u8 color][string\0] payload and calls
///         <c>SendOpcode(0x001D)</c>.</item>
///   <item><c>Player::SendOpcode</c> (PlayerConnection.cpp:127) — the
///         Phase K wire-size fix lives here (header is sizeof(int32_t)
///         = 4, not sizeof(long) which is 8 on Linux x86_64). The
///         payload is queued onto <c>m_UDPQueue</c>; SendPacketCache
///         later ships it inside a 0x2016 PACKET_SEQUENCE over UDP to
///         the proxy's global plane.</item>
///   <item><c>UDPClient::SendClientPacketSequence</c>
///         (proxy/UDPProxyToClient_linux.cpp:531) — walks the inner
///         [u16 size][u16 opcode] tuples, hits the
///         <c>0x0000 &lt; op &lt; 0x0FFF</c> band for 0x001D, and
///         <c>SendResponse</c>s it over the sector TCP connection.</item>
/// </list>
///
/// <para>
/// Why the group-with-no-group branch: it's the simplest server-state-
/// independent path that produces a deterministic, single-frame reply
/// keyed by the chat message. The slash-command branch
/// (<c>HandleSlashCommands</c>) is conditional on GM permissions and
/// returns variable output; sector-wide broadcast requires another
/// player to receive it; the target / local / guild branches need
/// extra setup. "You are not in a group" only depends on
/// <c>GroupID() == -1</c>, which is the default.
/// </para>
///
/// <para>
/// Budget: 90s. The full handshake takes ~2s; the chat round-trip
/// itself is a few hundred ms (server tick + UDP queue flush). The
/// budget is generous to leave room for the sector-login state machine
/// retry path in case a stage ack drops.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorChatTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorChatTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task GroupChat_WhenUngrouped_ReceivesNotInGroupErrorString()
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
            firstName: "Chatter", shipName: "ChatShip", cts.Token);

        try
        {
            // Build a Type=Group chat with non-slash content. The
            // server's HandleClientChat hits the `chat->Type == 1`
            // branch, checks GroupID() (== -1 for a freshly-logged-in
            // player), and calls SendVaMessage with the literal
            // "Error: You are not in a group!" string.
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "hello group");

            await session.Sector.SendAsync(
                Packet.ForOpcode(
                    OpcodeId.Known.ClientChat.Value,
                    codec.EncodeOutbound(chat)),
                cts.Token);

            // Drain inbound until we see 0x001D MESSAGE_STRING. The
            // server may interleave other post-login fan-out (state
            // updates, NPC chatter, etc.); a frame cap keeps a stalled
            // pipeline from masquerading as the outer-CTS timeout.
            int framesSeen = 0;
            const int maxFrames = 200;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.MessageString.Value)
                    continue;

                // 0x001D wire layout
                //   [0..2)  short  length  = strlen(msg) + 1   (includes NUL)
                //   [2]     byte   color   (default 5 for SendVaMessage path)
                //   [3..N)  char[] msg + NUL terminator
                // Mirror of Player::SendMessageString
                // (server/src/PlayerConnection.cpp:10918).
                var span = reply.Payload.Span;
                Assert.True(span.Length >= 4,
                    $"MESSAGE_STRING payload too short: {span.Length}B");

                short msgLen = BinaryPrimitives.ReadInt16LittleEndian(span[..2]);
                Assert.True(msgLen >= 1,
                    $"MESSAGE_STRING length field={msgLen}, expected >= 1 (NUL).");

                // Strip the trailing NUL when reading the body — be
                // defensive in case the server short-frames us.
                int bodyBytes = Math.Min(msgLen - 1, span.Length - 3);
                Assert.True(bodyBytes > 0,
                    $"MESSAGE_STRING body bytes={bodyBytes}, expected > 0.");

                string text = Encoding.ASCII.GetString(span.Slice(3, bodyBytes));

                // The exact server-side literal is "Error: You are not
                // in a group!". Pin on the distinctive substring rather
                // than the whole string so a colour-byte or punctuation
                // tweak doesn't sink the test.
                Assert.Contains("not in a group", text);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT " +
                $"without seeing 0x001D MESSAGE_STRING. " +
                $"Likely the server's chat dispatch didn't reach SendVaMessage, " +
                $"or the proxy's SendClientPacketSequence dropped the inner 0x001D " +
                $"out of the 0x2016 envelope.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// The verbatim ASCII body the retail-faithful HandleClientChat
    /// no-group branch passes to <c>SendVaMessage</c> at
    /// <c>server/src/PlayerConnection.cpp:4624</c>. 30 bytes of payload
    /// content; <c>SendMessageString</c> appends a NUL terminator and
    /// emits <c>length = 31</c>.
    /// </summary>
    private const string NotInGroupLiteral =
        "Error: You are not in a group!";

    /// <summary>
    /// Wave 110 frame-shape hardening (+0 ratchet, 0x001D): pins the
    /// byte-exact 34-byte wire-shape of the single 0x001D MESSAGE_STRING
    /// the server emits in reply to a 0x0033 CLIENT_CHAT with
    /// <see cref="ChatChannel.Group"/> while ungrouped. Wave 8's existing
    /// test
    /// (<see cref="GroupChat_WhenUngrouped_ReceivesNotInGroupErrorString"/>)
    /// asserts only that the response body <em>contains</em> the
    /// distinctive substring "not in a group"; the bounds checks
    /// (<c>Assert.True(span.Length &gt;= 4)</c>,
    /// <c>Assert.True(msgLen &gt;= 1)</c>) are deliberately loose so a
    /// punctuation tweak doesn't sink it. Wave 110 layers byte-exact
    /// pinning on top, locking the full 34-byte response shape in place.
    ///
    /// <para>
    /// Backstory. 0x001D MESSAGE_STRING is server-emitted by
    /// <c>Player::SendMessageString</c> at
    /// <c>server/src/PlayerConnection.cpp:10987-10997</c>:
    /// <code>
    ///     short length = strlen(msg) + 1;          // includes NUL
    ///     *((short *) &amp;buffer[0]) = length;       // wire offset 0..2 (LE)
    ///     buffer[2]                  = color;       // wire offset 2  (u8)
    ///     strcpy_s(&amp;buffer[3], ..., msg);          // wire offset 3..(3+length)
    ///     SendOpcode(ENB_OPCODE_001D_MESSAGE_STRING, buffer, length + 3);
    /// </code>
    /// <c>SendVaMessage</c> (PlayerClass.cpp:3415-3425) calls
    /// <c>SendMessageString(pch)</c> with the default <c>color=5</c>
    /// (PlayerClass.h:277). For the verbatim 30-byte literal
    /// "Error: You are not in a group!" at
    /// <c>PlayerConnection.cpp:4624</c>:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>length field</b> = strlen(30) + 1 = <c>31</c></item>
    ///   <item><b>color byte</b> = <c>5</c> (default)</item>
    ///   <item><b>msg + NUL</b> = 30 + 1 = <c>31 bytes</c></item>
    ///   <item><b>total payload</b> = <c>length + 3 = 34 bytes</c></item>
    /// </list>
    ///
    /// <para>
    /// Why a separate test method. Mirrors the Wave 92 (RELATIONSHIP
    /// count) / Wave 101 (CLIENT_AVATAR+SHIP paired) / Wave 108
    /// (ITEM_STATE UNRECOGNISED) / Wave 109 (INVENTORY_MOVE UNRECOGNISED)
    /// splits: Wave 8's looser substring assertion stays intact (narrow-
    /// scope failure surface — a wire-shape drift that still produces
    /// the literal substring would not surface as a Wave 8 failure),
    /// Wave 110 adds the byte-exact pin as its own discrete test artifact
    /// for the regression-class catalogue. Mirrors the Wave 104 narrative's
    /// "upgrade existing survival-probe / direct-reply assertions to
    /// byte-exact" pivot — Wave 110 is the third application of that pivot
    /// to the SendVaMessage → SendMessageString fan-out path (after Waves
    /// 108 and 109).
    /// </para>
    ///
    /// <para>
    /// Regression classes Wave 110 catches beyond what Wave 8 catches.
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b><c>SendMessageString</c> length-field width regression at
    ///     <c>PlayerConnection.cpp:10992</c>.</b> The cast
    ///     <c>*((short *) &amp;buffer[0]) = length</c> writes a 2-byte
    ///     length prefix. A regression to <c>int32_t</c> (the canonical
    ///     length type used elsewhere in the protocol) would shift the
    ///     color byte from offset 2 to offset 4 and grow the total
    ///     payload from 34 to 36 bytes. Wave 8's
    ///     <c>Assert.True(span.Length &gt;= 4)</c> would still pass at
    ///     36B; <c>Assert.Equal(34, span.Length)</c> catches.
    ///   </item>
    ///   <item>
    ///     <b><c>SendMessageString</c> color-default regression at
    ///     <c>PlayerClass.h:277</c>.</b> The signature is
    ///     <c>SendMessageString(char *msg, char color=5, bool log=true)</c>.
    ///     A regression to a different default (or a refactor where
    ///     SendVaMessage passes an explicit non-5 color) would change
    ///     wire byte 2 without changing the substring. Wave 8's text
    ///     assertion is structurally blind; Wave 110 pins
    ///     <c>span[2] == 5</c>.
    ///   </item>
    ///   <item>
    ///     <b>Length-field LE byte-order regression.</b> The cast
    ///     <c>*((short *) &amp;buffer[0])</c> writes a host-order short.
    ///     The retail Win32 client (x86 LE) reads it as LE. A
    ///     hypothetical big-endian server build would swap the byte
    ///     pair and break decoding; Wave 8's substring assertion would
    ///     still pass (the body bytes are after the length prefix and
    ///     unaffected). Wave 110's
    ///     <c>BinaryPrimitives.ReadInt16LittleEndian == 31</c> catches.
    ///   </item>
    ///   <item>
    ///     <b><c>SendOpcode</c> trailing-bytes regression at
    ///     <c>PlayerConnection.cpp:10996</c>.</b> The third argument
    ///     <c>length + 3</c> is what bounds the emit to 34 bytes; a
    ///     regression to <c>sizeof(buffer)</c> (512) would emit 478
    ///     trailing zero bytes after the legitimate payload. Wave 8's
    ///     substring assertion would still pass; Wave 110's
    ///     <c>Assert.Equal(34, span.Length)</c> catches.
    ///   </item>
    ///   <item>
    ///     <b>Verbatim-literal drift at
    ///     <c>PlayerConnection.cpp:4624</c>.</b> A refactor that
    ///     replaces "<c>not in</c>" with "<c>without</c>" (a "polish-
    ///     the-error" edit) or drops the trailing "!" would silently
    ///     change the wire bytes the retail Win32 client's decoder was
    ///     compiled to accept. Wave 8's <c>Contains("not in a group")</c>
    ///     would still pass on most such edits; Wave 110's full-literal
    ///     <c>Assert.Equal</c> on the body bytes catches.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Per CLAUDE.md server-integrity. 0x001D MESSAGE_STRING is
    /// server-originated. Wave 110 adds no client stimulus beyond the
    /// same Type=Group / non-slash CLIENT_CHAT Wave 8 already sends, and
    /// no server change — pure passive-observation tightening of a
    /// retail-faithful wire shape. The 34-byte response is exactly what
    /// the retail Win32 client's MESSAGE_STRING decoder was compiled to
    /// receive. No widened input acceptance, no loosened gating, no
    /// fabricated replies — server-integrity POSITIVE.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s. Handshake ~2s; CHAT+REPLY round-trip is sub-second.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GroupChat_WhenUngrouped_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (31) = 34 bytes.
        const int ExpectedReplyPayloadLength = 34;
        // strlen(literal) + 1 NUL = 31.
        const short ExpectedReplyLengthField = 31;
        // SendVaMessage → SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 30.
        const int ExpectedLiteralByteCount = 30;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Chat110er", shipName: "Chat110Ship", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "hello group");

            await session.Sector.SendAsync(
                Packet.ForOpcode(
                    OpcodeId.Known.ClientChat.Value,
                    codec.EncodeOutbound(chat)),
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
                if (span.Length < 4) continue;

                short msgLen = BinaryPrimitives.ReadInt16LittleEndian(span[..2]);
                if (msgLen < 1) continue;

                int bodyBytes = Math.Min(msgLen - 1, span.Length - 3);
                if (bodyBytes <= 0) continue;

                string text = Encoding.ASCII.GetString(span.Slice(3, bodyBytes));

                // Filter on the distinctive substring so other
                // MESSAGE_STRING traffic (motd, NPC chatter) doesn't
                // race ahead of the not-in-group reply. Once we have
                // the right reply we pin its full wire shape.
                if (!text.Contains("not in a group", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(NotInGroupLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT (Type=Group) " +
                $"without seeing 0x001D MESSAGE_STRING containing \"not in a group\". " +
                $"Same drain-loop budget as Wave 8's sibling test; the failure modes are " +
                $"identical.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// The verbatim ASCII body the retail-faithful HandleSlashCommands
    /// <c>/authlevel</c> arm passes to <c>SendVaMessage</c> at
    /// <c>server/src/PlayerConnection.cpp:5457</c>. The format string is
    /// <c>"Authentication Level - Num: %d"</c> and the substituted value
    /// is <c>AdminLevel()</c>, which for the seed-sql test accounts is
    /// <c>ADMIN = 100</c> (<c>server/src/Net7.h:366</c>) — admin_level is
    /// seeded from the account row's status field
    /// (<c>login-server/Net7SSL/AccountManager.cpp:950</c>), and every
    /// non-status0 entry in <c>Fixtures/seed.sql</c> is status=100.
    /// </summary>
    private const string AuthLevelLiteral =
        "Authentication Level - Num: 100";

    /// <summary>
    /// Wave 123 multi-arm dispatcher sibling-arm pinning (+0 ratchet,
    /// 0x0033 / 0x001D): pins the byte-exact 35-byte wire-shape of the
    /// 0x001D MESSAGE_STRING the server emits in reply to a 0x0033
    /// CLIENT_CHAT whose body starts with '/' — exercising the
    /// slash-command short-circuit arm of
    /// <c>Player::HandleClientChat</c> rather than the
    /// <c>chat->Type == 1</c> no-group arm Wave 8 / Wave 110 pin.
    ///
    /// <para>
    /// Why this arm specifically. <c>Player::HandleClientChat</c> at
    /// <c>server/src/PlayerConnection.cpp:4594</c> dispatches on the
    /// <em>first byte</em> of <c>chat-&gt;String</c> ahead of any Type
    /// check:
    /// <code>
    ///     if (this &amp;&amp; chat-&gt;String[0] == '/')
    ///         HandleSlashCommands(chat-&gt;String);
    ///     else if (this &amp;&amp; chat-&gt;Type == 1) { ... no-group reply ... }
    ///     else if (this &amp;&amp; chat-&gt;Type == 2) { ... GuildChat ... }
    ///     ...
    /// </code>
    /// Wave 8's <see cref="GroupChat_WhenUngrouped_ReceivesNotInGroupErrorString"/>
    /// and Wave 110's <see cref="GroupChat_WhenUngrouped_PinsExactReplyWireShape"/>
    /// both reach the no-group arm by sending non-slash content. Wave 123
    /// pins the slash-prefix arm so a refactor that swallows '/' before
    /// the Type-fanout (e.g. trimming whitespace and stripping '/' as
    /// part of generic chat sanitisation) would surface as a Wave 123
    /// failure even when Wave 110 still passes — the no-group arm would
    /// keep emitting its byte-exact 34-byte reply for non-slash content,
    /// but the slash arm would silently fall through to one of the
    /// Type-keyed arms instead of routing into <c>HandleSlashCommands</c>.
    /// </para>
    ///
    /// <para>
    /// Why <c>/authlevel</c> as the slash-command payload. The
    /// user-tier slash-command block at
    /// <c>PlayerConnection.cpp:5434</c> (the second of the two tiers —
    /// the first is the GM-only <c>//</c>-prefix block at 4716) holds
    /// the <c>/authlevel</c> arm at lines 5455-5460:
    /// <code>
    ///     if (strcmp(pch, "authlevel") == 0)
    ///     {
    ///         SendVaMessage("Authentication Level - Num: %d", AdminLevel());
    ///         msg_sent = true;
    ///         success = true;
    ///     }
    /// </code>
    /// This arm is uniquely test-friendly: (a) zero-arg, so the codec
    /// doesn't have to synthesise a parameter list; (b) deterministic
    /// — output depends only on <c>AdminLevel()</c>, which is sourced
    /// from the seed.sql status field at character-create time
    /// (<c>login-server/Net7SSL/AccountManager.cpp:950</c>); (c) emits
    /// a single SendVaMessage with no side-effects (no group chat
    /// broadcast, no character-state mutation, no manager-class call);
    /// (d) does not depend on AdminLevel gating — the strcmp arm runs
    /// for any user. The neighbouring case-'a' arms (anon, altweapon,
    /// altname, addbaseore) all either gate on AdminLevel &gt;= DEV or
    /// use MatchOptWithParam which rejects bare "authlevel" via the
    /// strncmp-then-first-char-after-prefix-character-class probe at
    /// <c>PlayerConnection.cpp:4530-4540</c> — so /authlevel emits
    /// exactly one MESSAGE_STRING and no other arms of the switch
    /// produce output.
    /// </para>
    ///
    /// <para>
    /// Why the slash short-circuit fires regardless of chat-&gt;Type.
    /// The first branch of HandleClientChat's if-else chain is the
    /// String[0]=='/' check (PlayerConnection.cpp:4614), which is
    /// type-agnostic. We send Type=Group not because Group has any
    /// special meaning here, but because (a) the codec needs a Type
    /// byte, (b) Group is what Wave 8 / Wave 110 already use so the
    /// stimulus diff is exactly the message body, isolating the
    /// regression-class surface to the slash-vs-non-slash dispatch
    /// fork.
    /// </para>
    ///
    /// <para>
    /// Reply wire shape derivation. <c>SendVaMessage</c> at
    /// <c>server/src/PlayerClass.cpp:3415-3425</c> formats via
    /// <c>vsprintf_s</c> and forwards to <c>SendMessageString(pch)</c>
    /// with the default <c>color = 5</c>
    /// (<c>server/src/PlayerClass.h:277</c>). <c>SendMessageString</c>
    /// emits <c>[u16 LE length][u8 color][ASCII msg][NUL]</c> with
    /// <c>length = strlen(msg) + 1</c>. For the 31-byte body
    /// "Authentication Level - Num: 100":
    /// </para>
    /// <list type="bullet">
    ///   <item><b>length field</b> = strlen(31) + 1 = <c>32</c></item>
    ///   <item><b>color byte</b> = <c>5</c></item>
    ///   <item><b>body + NUL</b> = 31 + 1 = <c>32 bytes</c></item>
    ///   <item><b>total payload</b> = <c>length + 3 = 35 bytes</c></item>
    /// </list>
    ///
    /// <para>
    /// Regression classes Wave 123 catches that no prior wave catches.
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Slash-prefix dispatch fork at
    ///     <c>PlayerConnection.cpp:4614</c>.</b> A refactor that moves
    ///     the slash check inside one of the Type-keyed arms (e.g.
    ///     "only honour /-commands on Type=Target") would silently
    ///     drop /authlevel from a Type=Group sender — Wave 110's
    ///     no-group reply would <em>start</em> firing in its place
    ///     (different literal, different byte count). Wave 123 catches.
    ///   </item>
    ///   <item>
    ///     <b><c>HandleSlashCommands</c> user-block guard at
    ///     <c>PlayerConnection.cpp:5434</c>.</b> The guard is
    ///     <c>(Msg[0] == '/') &amp;&amp; (Msg[1] != 0) &amp;&amp; (!msg_sent || !success)</c>.
    ///     A regression that flips the last clause to <c>(msg_sent &amp;&amp; success)</c>
    ///     (an "only fall through after a GM-block emit" misreading)
    ///     would cause the user-block to skip /authlevel from the
    ///     first attempt — Wave 123 fails immediately because no
    ///     MESSAGE_STRING with the expected literal arrives.
    ///   </item>
    ///   <item>
    ///     <b>Case-'a' switch arm at
    ///     <c>PlayerConnection.cpp:5444-5460</c>.</b> A regression that
    ///     reshuffles the case labels (e.g. dropping case 'a' fall-
    ///     through after fall-through-to-default in a switch rewrite)
    ///     would cause /authlevel to land in default. The default arm
    ///     ends the routing without emitting "Authentication Level"
    ///     — Wave 123 catches by literal mismatch.
    ///   </item>
    ///   <item>
    ///     <b>strcmp at <c>PlayerConnection.cpp:5455</c>.</b> A
    ///     regression to <c>strncmp(pch, "authlevel", N)</c> with N
    ///     too small (e.g. N=4 matching only "auth") would shotgun
    ///     /authlevel to match other "auth*" commands, including
    ///     hypothetical future ones. Wave 123 doesn't catch a
    ///     shortened-prefix match on the existing arm, but it does
    ///     catch the inverse: a regression to a tighter
    ///     <c>strcmp(pch, "AuthLevel")</c> (case sensitivity flip)
    ///     drops the match and Wave 123 fails on literal absence.
    ///   </item>
    ///   <item>
    ///     <b><c>%d</c> format-specifier width regression in
    ///     <c>SendVaMessage</c>.</b> AdminLevel returns int; a
    ///     regression to <c>%ld</c> on a platform where long is 8B
    ///     while a 4B int sits on the va_list would print garbage.
    ///     Wave 123's verbatim "100" assertion catches.
    ///   </item>
    ///   <item>
    ///     <b><c>AdminLevel()</c> seeding regression at
    ///     <c>login-server/Net7SSL/AccountManager.cpp:950</c>.</b> The
    ///     character-create path sets <c>admin_level =
    ///     ntohl(GetAccountStatus(username))</c>. A regression that
    ///     drops the seed (e.g. defaults admin_level to 0) would
    ///     print "Authentication Level - Num: 0" — Wave 123 catches
    ///     by literal byte count mismatch (29 vs 31) and the verbatim
    ///     literal check.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Per CLAUDE.md server-integrity. 0x001D MESSAGE_STRING is
    /// server-originated; Wave 123's stimulus is a single
    /// /authlevel slash-command, which is retail-faithful — the
    /// /authlevel handler has been part of the Net-7 server source
    /// at this location since the kyp snapshot. No widened input
    /// acceptance, no loosened gating, no fabricated replies; the
    /// test relies on the same seed.sql status=100 fixture every other
    /// admin-flavour test does. Server-integrity POSITIVE.
    /// </para>
    ///
    /// <para>
    /// Wave classification: NINTH multi-arm dispatcher sibling-arm-
    /// pinning wave; SEVENTH SendMessageString-flavour byte-exact
    /// wave (after Waves 108 ItemState, 109 InventoryMove, 110
    /// GroupChat-no-group, 121 RemoveIgnore, 113 AddFriendSelf — all
    /// in the SendMessageString family); FIRST byte-exact pin on
    /// <c>Player::HandleClientChat</c>'s slash-command short-circuit
    /// arm.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s. Handshake ~2s; CHAT+REPLY round-trip is sub-second.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashAuthlevel_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (32) = 35 bytes.
        const int ExpectedReplyPayloadLength = 35;
        // strlen(literal) + 1 NUL = 32.
        const short ExpectedReplyLengthField = 32;
        // SendVaMessage → SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 31.
        const int ExpectedLiteralByteCount = 31;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Authleveler", shipName: "AuthLvlShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/authlevel");

            await session.Sector.SendAsync(
                Packet.ForOpcode(
                    OpcodeId.Known.ClientChat.Value,
                    codec.EncodeOutbound(chat)),
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
                if (span.Length < 4) continue;

                short msgLen = BinaryPrimitives.ReadInt16LittleEndian(span[..2]);
                if (msgLen < 1) continue;

                int bodyBytes = Math.Min(msgLen - 1, span.Length - 3);
                if (bodyBytes <= 0) continue;

                string text = Encoding.ASCII.GetString(span.Slice(3, bodyBytes));

                // Filter on the distinctive substring so other
                // MESSAGE_STRING traffic (motd, NPC chatter, post-
                // handshake server fan-out) doesn't race ahead of the
                // /authlevel reply. Once we have the right reply we
                // pin its full wire shape.
                if (!text.Contains("Authentication Level", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(AuthLevelLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/authlevel\" without seeing 0x001D MESSAGE_STRING containing " +
                $"\"Authentication Level\". Likely the slash-prefix short-circuit at " +
                $"server/src/PlayerConnection.cpp:4614 was bypassed, or the user-block " +
                $"guard at line 5434 rejected the dispatch, or AdminLevel seeding from " +
                $"the account status field at AccountManager.cpp:950 regressed.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
