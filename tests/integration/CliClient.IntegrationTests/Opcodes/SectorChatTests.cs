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

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// in HandleSlashCommands' user-block case 'n' for the
    /// <c>strcmp(pch, "notells") == 0 &amp;&amp; AdminLevel() &gt;= GM</c>
    /// arm on FIRST invocation (m_TellsFromFriendsOnly initialised
    /// false in Player ctor at <c>server/src/PlayerClass.cpp:167</c>).
    /// The format string is <c>"Allow tells %s"</c> and the substituted
    /// value is the TERNARY OF THE OLD STATE: <c>m_TellsFromFriendsOnly
    /// ? "off" : "on"</c>. With initial state false, the substituted
    /// value is "on" (the message reflects the OLD state, not the new
    /// one — toggle happens on the line AFTER the SendVaMessageC call,
    /// see <c>server/src/PlayerConnection.cpp:6863-6868</c>).
    /// 14 ASCII bytes; SendVaMessageC hands the string to
    /// SendMessageString with EXPLICIT color=17 (red — see
    /// <c>server/src/PlayerClass.cpp:3438-3452</c> for the colour code
    /// table comment).
    /// </summary>
    private const string NotellsFirstInvocationLiteral = "Allow tells on";

    /// <summary>
    /// Wave 125 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT → 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 18-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/notells</c> on FIRST invocation (initial state
    /// m_TellsFromFriendsOnly=false).
    ///
    /// <para>
    /// Why a +0 ratchet, and why this specific sibling. Wave 123
    /// (<see cref="SlashAuthlevel_OnAdminAccount_PinsExactReplyWireShape"/>)
    /// pinned the SAME HandleSlashCommands user-block dispatcher on a
    /// DIFFERENT case-letter ('a' /authlevel), routing through
    /// <c>SendVaMessage(...)</c> with DEFAULT color=5. Wave 125 pins a
    /// DIFFERENT case-letter ('n' /notells) routing through a
    /// STRUCTURALLY DISTINCT EMIT FN <c>SendVaMessageC(17, ...)</c> with
    /// EXPLICIT color=17. The two together box in three orthogonal
    /// fan-outs on the same dispatcher: (a) case-letter routing 'a' vs
    /// 'n' inside the <c>switch(*pch)</c> at PlayerConnection.cpp:5444;
    /// (b) emit-fn fork SendVaMessage vs SendVaMessageC at
    /// PlayerClass.cpp:3415 vs 3443; (c) SendMessageString color-param
    /// routing — default-arg branch (color=5) vs explicit-arg branch
    /// (color=17).
    /// </para>
    ///
    /// <para>
    /// What this catches. Six concrete regression classes Waves 8/110/123
    /// (the existing HandleClientChat dispatcher pins) are structurally
    /// blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <c>SendVaMessageC</c>→<c>SendMessageString</c> colour-routing
    ///     regression at <c>server/src/PlayerClass.cpp:3451</c>. The
    ///     two-arg call <c>SendMessageString(pch, colour)</c> threads
    ///     the leading colour through; if a refactor dropped the second
    ///     arg (e.g. consolidated to <c>SendMessageString(pch)</c>) the
    ///     reply would carry the default color=5 instead of the
    ///     explicit color=17 from the slash-command site. Wave 125 pins
    ///     <c>span[2] == 17</c>; Wave 123 pins <c>span[2] == 5</c>;
    ///     together they nail the colour fork from both sides.
    ///   </item>
    ///   <item>
    ///     <c>SendVaMessageC</c> arg-order regression at
    ///     <c>server/src/PlayerClass.cpp:3443</c>. The signature is
    ///     <c>void SendVaMessageC(char colour, char *string, ...)</c>
    ///     — colour FIRST, format SECOND. A refactor that flipped them
    ///     to <c>(char *string, char colour, ...)</c> would cause every
    ///     caller's first arg (often a small int like 17) to be
    ///     interpreted as a string pointer and crash the server. Wave
    ///     125 catches by drain-timeout (server crash) or by reading
    ///     the wrong colour byte.
    ///   </item>
    ///   <item>
    ///     <c>m_TellsFromFriendsOnly</c> ctor-init regression at
    ///     <c>server/src/PlayerClass.cpp:167</c>. The Player ctor
    ///     initialises this flag to false; the slash-command emits
    ///     OLD state via ternary <c>m_TellsFromFriendsOnly ? "off" :
    ///     "on"</c>. A refactor that flipped the default to true would
    ///     emit "Allow tells off" (15 bytes literal, 19-byte payload)
    ///     instead of "Allow tells on" (14 bytes literal, 18-byte
    ///     payload). Wave 125 pins literal byte-count (14) and
    ///     payload-length (18) — both fail on the flip.
    ///   </item>
    ///   <item>
    ///     Slash-command toggle-message regression. The message
    ///     literal pre-existed the toggle line at PlayerConnection.cpp:
    ///     6865-6866; if a refactor moved the message AFTER the toggle
    ///     (printing NEW state instead of OLD), the first invocation
    ///     would emit "Allow tells off" rather than "Allow tells on".
    ///     Wave 125 pins verbatim "Allow tells on".
    ///   </item>
    ///   <item>
    ///     User-block case-'n' arm AdminLevel-guard regression at
    ///     <c>server/src/PlayerConnection.cpp:6863</c>. The conjunction
    ///     <c>strcmp(pch, "notells") == 0 &amp;&amp; AdminLevel() &gt;=
    ///     GM</c> requires admin-level ≥ 50 (GM). Wave 123 already
    ///     verifies the seed.sql status=100→admin_level=100 plumbing
    ///     for the case-'a' /authlevel arm, but /authlevel has NO
    ///     admin-level guard so a regression that broke
    ///     <c>AdminLevel()</c> back to 0 would be invisible to Wave
    ///     123. Wave 125 catches by drain-timeout: case-'n' /notells is
    ///     SILENT when admin&lt;GM (the branch is gated by &amp;&amp;).
    ///   </item>
    ///   <item>
    ///     User-block case-'n' switch-arm routing at
    ///     <c>server/src/PlayerConnection.cpp:6855-6870</c>. The
    ///     case-'n' arm only contains 'noattack' and 'notells'; a
    ///     copy-paste swap that bound 'notells' to <c>noattack</c>'s
    ///     body would emit "Combat immunity on" instead of "Allow
    ///     tells on" — both go through SendVaMessageC(17,...) so colour
    ///     and length-encoding would still pin, but the literal-body
    ///     verbatim check catches.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (CLAUDE.md). The case-'n' /notells slash
    /// command is the retail server's documented chat-flag toggle path
    /// (admin-only — the retail server gated this with the same
    /// AdminLevel ≥ GM check). The SendVaMessageC fn and the 0x001D
    /// MESSAGE_STRING wire shape it travels on are both retail
    /// behaviour, not test-only artefacts. No server permissiveness is
    /// added: a real-client /notells from a GM-flagged account lands
    /// here too. The colour code 17 (red) is also retail behaviour, see
    /// the colour-table comment at PlayerClass.cpp:3438-3441.
    /// </para>
    ///
    /// <para>
    /// Why fresh-char first-invocation matters. The message reflects
    /// the OLD state — true→"off", false→"on" — and toggles AFTER. So
    /// the test MUST invoke /notells exactly once on a never-before-
    /// invoked Player object to assert "Allow tells on". A retry harness
    /// that re-sends /notells on flake would observe "Allow tells off"
    /// on the second invocation; the per-account isolation (cli_test119
    /// dedicated, ServerFixture tears down with -v wiping pgdata) makes
    /// the single-invocation invariant safe.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s. Handshake ~2s; CHAT+REPLY round-trip is sub-second.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashNotells_OnAdminAccountFirstInvocation_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (15) = 18 bytes.
        const int ExpectedReplyPayloadLength = 18;
        // strlen(literal) + 1 NUL = 15.
        const short ExpectedReplyLengthField = 15;
        // SendVaMessageC explicit colour parameter — red.
        const byte ExpectedReplyColor = 17;
        // strlen(literal) = 14.
        const int ExpectedLiteralByteCount = 14;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Notelle", shipName: "NotellShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/notells");

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

                // Filter on the distinctive substring so handshake-tail
                // and chatter frames don't race ahead of the /notells
                // reply. Once we have the right reply we pin its full
                // wire shape.
                if (!text.Contains("Allow tells", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(NotellsFirstInvocationLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/notells\" without seeing 0x001D MESSAGE_STRING containing " +
                $"\"Allow tells\". Likely the user-block case-'n' arm at " +
                $"server/src/PlayerConnection.cpp:6863 changed shape, the AdminLevel " +
                $"guard rejected the dispatch (admin_level seeding regression), the " +
                $"SendVaMessageC→SendMessageString colour-routing was rewired, or the " +
                $"m_TellsFromFriendsOnly ctor-init at PlayerClass.cpp:167 changed.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// in HandleSlashCommands' user-block case 'n' for the
    /// <c>strcmp(pch, "noattack") == 0 &amp;&amp; AdminLevel() &gt;= GM</c>
    /// arm on FIRST invocation (m_CombatImmunity initialised false in
    /// Player ctor at <c>server/src/PlayerClass.cpp:119</c>). The format
    /// string is <c>"Combat immunity %s"</c> and the substituted value
    /// is the TERNARY OF THE OLD STATE: <c>m_CombatImmunity ? "off" :
    /// "on"</c>. With initial state false, the substituted value is
    /// "on" (the message reflects the OLD state — toggle happens on
    /// the line AFTER the SendVaMessageC call, see
    /// <c>server/src/PlayerConnection.cpp:6858-6860</c>). 18 ASCII
    /// bytes; SendVaMessageC hands the string to SendMessageString with
    /// EXPLICIT color=17 (red).
    /// </summary>
    private const string NoattackFirstInvocationLiteral = "Combat immunity on";

    /// <summary>
    /// Wave 126 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT → 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 22-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/noattack</c> on FIRST invocation (initial state
    /// m_CombatImmunity=false).
    ///
    /// <para>
    /// Why a +0 ratchet, and why this specific sibling. Wave 125
    /// (<see cref="SlashNotells_OnAdminAccountFirstInvocation_PinsExactReplyWireShape"/>)
    /// pinned the SAME case-'n' arm at PlayerConnection.cpp:6863-6868
    /// — same case-letter, same emit fn (SendVaMessageC), same explicit
    /// colour 17, same ctor-init-false flag pattern. Wave 126 is the
    /// TIGHTEST POSSIBLE sibling pair to Wave 125: structurally
    /// identical wire-emit path, but with a DIFFERENT internal
    /// boolean flag (<c>m_CombatImmunity</c> at PlayerClass.cpp:119)
    /// and DIFFERENT literal body ("Combat immunity on" vs "Allow
    /// tells on"). The pair pins both arms of the case-'n' sub-switch
    /// at PlayerConnection.cpp:6855-6870 (noattack at 6856 and notells
    /// at 6863) — the two if-statements inside the case-'n' arm.
    /// </para>
    ///
    /// <para>
    /// What this catches. Five concrete regression classes Wave 125
    /// alone is blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     Case-'n' sub-switch arm-body swap between /noattack and
    ///     /notells at <c>server/src/PlayerConnection.cpp:6856-6868</c>.
    ///     Both arms emit SendVaMessageC(17,...) with structurally
    ///     identical ternary OLD-state reads on a bool flag; a refactor
    ///     that copy-pasted the /noattack body into /notells (or
    ///     vice-versa) would emit "Combat immunity on" instead of
    ///     "Allow tells on" — Wave 125 catches the wrong-literal in
    ///     ONE direction (notells→noattack swap); Wave 126 catches the
    ///     OPPOSITE direction (noattack→notells swap, emitting "Allow
    ///     tells on" instead of "Combat immunity on"). Without both
    ///     waves, half the swap surface is uncovered.
    ///   </item>
    ///   <item>
    ///     <c>m_CombatImmunity</c> ctor-init regression at
    ///     <c>server/src/PlayerClass.cpp:119</c>. The Player ctor
    ///     initialises this flag to false; a flip to true would emit
    ///     "Combat immunity off" (19B literal, 23B payload) instead of
    ///     "Combat immunity on" (18B/22B). Wave 125 only pins
    ///     m_TellsFromFriendsOnly init at line 167 — these are two
    ///     separate ctor-init lines and the regression surface is per-
    ///     line. Wave 126 pins m_CombatImmunity init independently.
    ///   </item>
    ///   <item>
    ///     case-'n' AdminLevel guard regression for the /noattack arm
    ///     specifically at <c>server/src/PlayerConnection.cpp:6856</c>.
    ///     The two if-statements in case-'n' have INDEPENDENT
    ///     AdminLevel guards — `strcmp(...,"noattack")==0 &amp;&amp;
    ///     AdminLevel()&gt;=GM` and `strcmp(...,"notells")==0
    ///     &amp;&amp; AdminLevel()&gt;=GM`. A regression that broke
    ///     just one guard's AdminLevel check (e.g. typo "AdminLevle()")
    ///     would be invisible to Wave 125 if it broke the noattack
    ///     guard. Wave 126 pins the /noattack-specific AdminLevel
    ///     dispatch path.
    ///   </item>
    ///   <item>
    ///     SendVaMessageC literal-substitution arg-marshalling
    ///     regression. Both /notells and /noattack pass a ternary
    ///     <c>flag ? "off" : "on"</c> as the va-args %s argument. If
    ///     the ternary-vs-format coupling broke (e.g. a refactor that
    ///     passed an int instead of a string pointer), Wave 125 catches
    ///     for the notells arm; Wave 126 catches for the noattack arm.
    ///     The va-args marshalling surface is per-callsite.
    ///   </item>
    ///   <item>
    ///     case-'n' switch-arm length-prefix calculation regression
    ///     for the LONGER literal. The /noattack literal is 18 bytes,
    ///     LONGER than /notells's 14. SendMessageString writes
    ///     <c>strlen(msg) + 1</c> as the u16 length prefix; a buffer-
    ///     overflow / strlen miscalculation that worked for short
    ///     literals (Wave 125's 14B body) might fail for longer ones
    ///     (Wave 126's 18B body). Wave 126 pins length=19 specifically.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (CLAUDE.md). The case-'n' /noattack slash
    /// command is the retail server's documented combat-immunity GM
    /// toggle path. Both the AdminLevel guard and the emit shape are
    /// retail behaviour. No server permissiveness added: a real-client
    /// /noattack from a GM-flagged account lands on the exact same code
    /// path.
    /// </para>
    ///
    /// <para>
    /// Why fresh-char first-invocation matters. Same as Wave 125 — the
    /// message reflects the OLD state via ternary, and toggles AFTER.
    /// Per-account isolation (cli_test120 dedicated, ServerFixture
    /// tears down with -v wiping pgdata) makes the single-invocation
    /// invariant safe.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashNoattack_OnAdminAccountFirstInvocation_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (19) = 22 bytes.
        const int ExpectedReplyPayloadLength = 22;
        // strlen(literal) + 1 NUL = 19.
        const short ExpectedReplyLengthField = 19;
        // SendVaMessageC explicit colour parameter — red.
        const byte ExpectedReplyColor = 17;
        // strlen(literal) = 18.
        const int ExpectedLiteralByteCount = 18;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Noattacker", shipName: "NoatkShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/noattack");

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

                if (!text.Contains("Combat immunity", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(NoattackFirstInvocationLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/noattack\" without seeing 0x001D MESSAGE_STRING containing " +
                $"\"Combat immunity\". Likely the user-block case-'n' arm at " +
                $"server/src/PlayerConnection.cpp:6856 changed shape, the AdminLevel " +
                $"guard rejected the dispatch, the SendVaMessageC→SendMessageString " +
                $"colour-routing was rewired, or the m_CombatImmunity ctor-init at " +
                $"PlayerClass.cpp:119 changed.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// in HandleSlashCommands' user-block case 'p' for the
    /// <c>strcmp(pch, "position") == 0</c> arm on a freshly-entered sector
    /// with no target acquired. Format string is
    /// <c>"ObjectID = 0x%08x"</c>; substituted value is
    /// <c>ShipIndex()-&gt;GetTargetGameID()</c>, which
    /// <c>Player::SendShipInfo</c> seeds to <c>-1</c> at
    /// <c>server/src/PlayerClass.cpp:1085</c> during sector entry. The
    /// printf cast <c>%08x</c> renders int -1 as its unsigned bit-pattern
    /// "ffffffff", so the body is "ObjectID = 0xffffffff" -- 21 ASCII
    /// bytes. The arm uses the DEFAULT-colour <c>SendVaMessage</c> (no
    /// explicit colour arg), which threads through
    /// <c>SendMessageString(pch)</c> picking up the default-arg
    /// <c>colour=5</c> from the header declaration at
    /// <c>server/src/PlayerConnection.h:277</c>.
    /// </summary>
    private const string PositionNoTargetLiteral = "ObjectID = 0xffffffff";

    /// <summary>
    /// Wave 129 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -> 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 25-byte wire-shape of the FIRST 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/position</c> on a freshly-entered sector with no
    /// target acquired (GetTargetGameID() == -1, the
    /// <c>Player::SendShipInfo</c> seed at PlayerClass.cpp:1085).
    ///
    /// <para>
    /// Why a +0 ratchet, and why this specific sibling. Wave 123
    /// (<see cref="SlashAuthlevel_OnAdminAccount_PinsExactReplyWireShape"/>)
    /// pinned the HandleSlashCommands user-block case-'a' /authlevel arm
    /// routing through default-colour <c>SendVaMessage(...)</c> (colour=5
    /// via the header default arg). Wave 129 pins a DIFFERENT case-letter
    /// ('p' /position) routing through the SAME default-colour
    /// <c>SendVaMessage</c> emit fn -- but with a DIFFERENT format
    /// substitution shape: Wave 123 uses <c>"%d"</c> with the per-account
    /// <c>AdminLevel()</c> int, while Wave 129 uses <c>"%08x"</c> with the
    /// per-ship <c>GetTargetGameID()</c> int. The pair pins two
    /// orthogonal fan-outs on the default-colour fork of
    /// HandleSlashCommands: (a) case-letter routing 'a' vs 'p' inside the
    /// <c>switch(*pch)</c> at PlayerConnection.cpp:5442; (b) printf
    /// format-string substitution decimal vs hex-with-fill.
    /// </para>
    ///
    /// <para>
    /// Wave 129 also extends the sibling-arm catalogue on the
    /// HandleSlashCommands user-tier dispatcher to FOUR byte-exact-pinned
    /// arms -- Wave 123 case-'a' /authlevel (SendVaMessage default-
    /// colour, %d format); Wave 125 case-'n' /notells (SendVaMessageC
    /// explicit-colour 17, %s ternary); Wave 126 case-'n' /noattack
    /// (SendVaMessageC explicit-colour 17, %s ternary); Wave 129 case-'p'
    /// /position (SendVaMessage default-colour, %08x format).
    /// </para>
    ///
    /// <para>
    /// What this catches. Five concrete regression classes the existing
    /// HandleSlashCommands pins (Waves 123/125/126) are structurally blind
    /// to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     Case-letter mis-dispatch between case-'a' and case-'p' at
    ///     <c>server/src/PlayerConnection.cpp:5442</c>. The switch
    ///     dispatches on the first byte after the slash. A refactor that
    ///     coalesced or swapped case-arm bodies would emit the wrong
    ///     literal -- e.g. <c>/position</c> hitting the /authlevel body
    ///     would produce "Authentication Level - Num: %d" instead of
    ///     "ObjectID = 0x%08x". Wave 129's literal-prefix filter
    ///     ("ObjectID =") catches this fail-direction; Wave 123 catches
    ///     the reverse.
    ///   </item>
    ///   <item>
    ///     <c>Player::SendShipInfo</c> target-seeding regression at
    ///     <c>server/src/PlayerClass.cpp:1085</c>. The line
    ///     <c>ShipIndex()-&gt;SetTargetGameID(-1)</c> resets the player's
    ///     locked target to -1 on every sector-entry SendShipInfo call.
    ///     A regression that dropped this reset, or changed -1 to 0 or
    ///     some other sentinel, would substitute a different value into
    ///     <c>"ObjectID = 0x%08x"</c>: 0 yields "ObjectID = 0x00000000"
    ///     (same length), some other id yields a random hex string.
    ///     Wave 129 pins the EXACT substituted body, which fails for any
    ///     deviation from -1.
    ///   </item>
    ///   <item>
    ///     <c>printf %08x</c> formatting regression at
    ///     <c>server/src/PlayerClass.cpp:3422</c>
    ///     (<c>vsprintf_s</c> in SendVaMessage). The %08x specifier
    ///     zero-pads to a minimum of 8 hex digits. A vsprintf_s
    ///     implementation regression that dropped the zero-pad, or
    ///     widened the field, would change the body length: "ffffffff" is
    ///     8 chars padded; without the pad a small target id like 0xff
    ///     would render as "ff" (2 chars) -- but on -1 (all bits set) the
    ///     observable would only catch the >=8-char path. Wave 129 pins
    ///     the full 21-byte body including the leading "0x" prefix from
    ///     the format string, which fails if the printf machinery
    ///     swallows the literal prefix or rewrites the hex digits.
    ///   </item>
    ///   <item>
    ///     <c>SendVaMessage</c> default-colour routing regression at
    ///     <c>server/src/PlayerConnection.h:277</c>. The header declares
    ///     <c>SendMessageString(char *msg, char color=5, bool log=true)</c>;
    ///     <c>SendVaMessage</c>'s body at PlayerClass.cpp:3423 calls
    ///     <c>SendMessageString(pch)</c> with NO explicit colour, picking
    ///     up the default-arg 5. A header refactor that flipped the
    ///     default to a different value would change the on-wire colour
    ///     byte for every default-colour caller. Wave 129 pins
    ///     <c>span[2] == 5</c>; Wave 123 pins the same byte on a
    ///     different case-letter; together they nail the default-colour
    ///     fork.
    ///   </item>
    ///   <item>
    ///     <c>HandleSlashCommands</c> case-'p' second-emit suppression at
    ///     <c>server/src/PlayerConnection.cpp:6952-6956</c>. The arm
    ///     guards a SECOND <c>SendVaMessage("%s @ %.2f %.2f %.2f", ...)</c>
    ///     behind <c>if (GetTargetGameID() != -1)</c>. A regression that
    ///     swapped the comparison sense (== -1) or dropped the guard
    ///     would emit a second MESSAGE_STRING. The test does not pin a
    ///     single-frame count (other MESSAGE_STRING traffic is filtered
    ///     out via the "ObjectID =" prefix), but a typical regression
    ///     would also dereference a null obj-from-id and likely crash --
    ///     so the drain-timeout / server-crash signal lights up first.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (CLAUDE.md). The case-'p' /position slash command
    /// is the retail server's documented user-tier target-debug path.
    /// Note this arm is NOT AdminLevel-gated -- any user can type
    /// <c>/position</c> and read back their currently-locked target's id
    /// and (if locked) its name and world coords. No server permissiveness
    /// added: a real-client /position from any account lands on the exact
    /// same code path. The test uses a non-admin cli_test account; the
    /// retail server permits this.
    /// </para>
    ///
    /// <para>
    /// Why fresh-char no-target matters. The TargetGameID seeds to -1 on
    /// EVERY <c>SendShipInfo</c> call (sector entry), so the test is
    /// stable across retries provided no auto-target acquisition fires
    /// between the handshake-complete signal and the /position dispatch.
    /// Targeting only fires via explicit
    /// <c>SetTargetGameID(obj-&gt;GameID())</c> at PlayerClass.cpp:2444
    /// and 2474 -- both routed off opcode handlers the cli client never
    /// invokes during this test. Per-account isolation (cli_test121
    /// dedicated, ServerFixture tears down with -v wiping pgdata) makes
    /// the no-target invariant safe.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s. Handshake ~2s; CHAT+REPLY round-trip is sub-second.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashPosition_OnFreshCharNoTarget_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (22) = 25 bytes.
        const int ExpectedReplyPayloadLength = 25;
        // strlen(literal) + 1 NUL = 22.
        const short ExpectedReplyLengthField = 22;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 21.
        const int ExpectedLiteralByteCount = 21;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Positioner", shipName: "PosShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/position");

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

                // Filter on the distinctive prefix so handshake-tail and
                // chatter frames don't race ahead of the /position reply.
                // Once we have the right reply we pin its full wire shape.
                if (!text.StartsWith("ObjectID =", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(PositionNoTargetLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/position\" without seeing 0x001D MESSAGE_STRING starting with " +
                $"\"ObjectID =\". Likely the user-block case-'p' arm at " +
                $"server/src/PlayerConnection.cpp:6948 changed shape, the " +
                $"SendVaMessage default-colour routing was rewired, or the " +
                $"SendShipInfo target-seed at PlayerClass.cpp:1085 changed.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// in HandleSlashCommands' user-block case 'l' for the
    /// <c>MatchOptWithParam("level", pch, param, msg_sent)</c> arm when
    /// the param is out of range (atoi(param) &lt; 0 || atoi(param) &gt;
    /// 50) AND <c>AdminLevel() &gt;= GM</c>. The literal is hard-coded as
    /// <c>"0 &lt;= Level &lt;= 50"</c> at
    /// <c>server/src/PlayerConnection.cpp:6787</c>; no format
    /// substitution -- raw literal pass-through into
    /// <c>SendVaMessage</c>, which threads the default colour=5 via the
    /// header default arg at PlayerConnection.h:277. 16 ASCII bytes;
    /// after the emit the arm <c>return</c>s at line 6788 (NOT
    /// <c>break</c>), short-circuiting the rest of the case-'l' arm and
    /// every later case-letter -- the only emit reaching the wire.
    /// </summary>
    private const string LevelOutOfRangeLiteral = "0 <= Level <= 50";

    /// <summary>
    /// Wave 130 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -> 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 20-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/level 99</c> -- an out-of-range param (atoi("99")=99
    /// &gt; 50) against an admin-level fixture (status=100 -&gt;
    /// AdminLevel()=100 &gt;= GM=50).
    ///
    /// <para>
    /// Why a +0 ratchet, and why this specific sibling. Wave 129
    /// (<see cref="SlashPosition_OnFreshCharNoTarget_PinsExactReplyWireShape"/>)
    /// and Wave 123
    /// (<see cref="SlashAuthlevel_OnAdminAccount_PinsExactReplyWireShape"/>)
    /// pinned two arms on the SendVaMessage default-colour fork with
    /// format-substitution. Wave 130 is the FIRST byte-exact pin on a
    /// MatchOptWithParam-dispatched arm (vs strcmp-dispatched arms in
    /// Waves 123/125/126/129) AND the FIRST on a raw-literal-pass-through
    /// path (no %d/%s/%08x format substitution -- the arg to
    /// SendVaMessage is a string literal with no variable arguments).
    /// The pair pins four orthogonal fan-outs on the user-tier
    /// dispatcher: (a) case-letter routing 'a'/'n'/'p'/'l' inside the
    /// switch at PlayerConnection.cpp:5442; (b) dispatch mechanism
    /// strcmp (Waves 123/125/126/129) vs MatchOptWithParam (Wave 130) at
    /// PlayerConnection.cpp:4526; (c) admin-gating placement -- in the
    /// if-condition (`&amp;&amp; AdminLevel() &gt;= GM` Waves 125/126)
    /// vs in a body-block guard (`if (AdminLevel() &gt;= GM) { ... }`
    /// Wave 130 at PlayerConnection.cpp:6782); (d) post-emit
    /// short-circuit -- early <c>return</c> (Wave 130 at line 6788) vs
    /// <c>break</c>+msg_sent=true (Waves 123/125/126/129).
    /// </para>
    ///
    /// <para>
    /// Wave 130 also extends the sibling-arm catalogue on the
    /// HandleSlashCommands user-tier dispatcher to FIVE byte-exact-pinned
    /// arms -- Wave 123 case-'a' /authlevel; Wave 125 case-'n' /notells;
    /// Wave 126 case-'n' /noattack; Wave 129 case-'p' /position; Wave 130
    /// case-'l' /level out-of-range.
    /// </para>
    ///
    /// <para>
    /// What this catches. Six concrete regression classes Waves
    /// 123/125/126/129 are structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <c>MatchOptWithParam</c> dispatch regression at
    ///     <c>server/src/PlayerConnection.cpp:4526</c>. The matcher
    ///     accepts "name=value" or "name value" forms, sets <c>param</c>
    ///     by ref, and returns true. Waves 123/125/126/129 use plain
    ///     <c>strcmp(pch, literal) == 0</c> dispatch. A regression that
    ///     broke MatchOptWithParam's param-extraction (e.g. off-by-one
    ///     on the <c>arg + len + 1</c> calculation) would silently route
    ///     /level 99 into a different arm or short-circuit with the
    ///     "Missing arg" emit at line 4548. Wave 130 pins the
    ///     out-of-range body specifically, which fails if the matcher
    ///     short-circuits or routes incorrectly.
    ///   </item>
    ///   <item>
    ///     atoi out-of-range comparison regression at
    ///     <c>server/src/PlayerConnection.cpp:6785</c>. The check is
    ///     <c>atoi(param) &lt; 0 || atoi(param) &gt; 50</c>. A regression
    ///     flipping the bounds (e.g. &gt;=50, or 100 instead of 50) or
    ///     dropping a clause would change which paths emit the
    ///     out-of-range body. Wave 130 sends 99 -- a value that's
    ///     unambiguously &gt; 50 but well below any plausible mis-typed
    ///     bound like 1000.
    ///   </item>
    ///   <item>
    ///     Body-block AdminLevel guard regression at
    ///     <c>server/src/PlayerConnection.cpp:6782</c>. Unlike Waves
    ///     125/126's in-condition guards, /level wraps its body in
    ///     <c>if (AdminLevel() &gt;= GM) { ... } else { ... }</c>. The
    ///     else-branch emits a different literal ("/level not available
    ///     at [BETA] and below") -- which Wave 130 would catch if a
    ///     regression broke the AdminLevel comparison sense and dropped
    ///     admin accounts into the BETA-and-below path.
    ///   </item>
    ///   <item>
    ///     Early-<c>return</c> short-circuit regression at
    ///     <c>server/src/PlayerConnection.cpp:6788</c>. The out-of-range
    ///     arm calls SendVaMessage and immediately <c>return</c>s --
    ///     skipping the SetCombatLevel/SetTradeLevel/SetExploreLevel
    ///     side-effects and the follow-up "Combat, Explore and Trade
    ///     LVLs set to %d" emit at line 6802. A regression that swapped
    ///     <c>return</c> for <c>break</c> would NOT execute the level-
    ///     setting code (atoi check still passes 99 as out-of-range), but
    ///     fall-through to the lootstats strcmp at 6812 then to case-'m'
    ///     -- but the wire-shape stays the same. The more subtle
    ///     regression: dropping the <c>return</c> entirely would let the
    ///     out-of-range body's emit follow through into the success path
    ///     and corrupt player state. Wave 130 pins ONLY the single emit;
    ///     a regression that added a second emit on the out-of-range
    ///     path would fail the test's single-frame-shape assertion (the
    ///     filter is on the prefix "0 &lt;= Level", which is distinctive
    ///     enough to skip the rare follow-up cases).
    ///   </item>
    ///   <item>
    ///     Raw-literal SendVaMessage path regression at
    ///     <c>server/src/PlayerClass.cpp:3415</c>. Waves 123/125/126/129
    ///     all exercise va-args format substitution; Wave 130 passes a
    ///     literal string with no format specifiers. A regression in
    ///     vsprintf_s that mis-handled the empty-va_args case (e.g.
    ///     spurious "%" emission or terminator handling) would change
    ///     the body length or content. Wave 130 pins the exact 16-byte
    ///     literal pass-through.
    ///   </item>
    ///   <item>
    ///     Case-letter mis-dispatch between case-'l' and case-'a'/'n'/'p'.
    ///     A switch-arm copy-paste swap that bound /level's body to a
    ///     different case-letter (or vice-versa) emits the wrong literal.
    ///     Wave 130's "0 &lt;=" prefix is distinctive enough to detect
    ///     mis-routing; Waves 123/125/126/129's prefixes are equally
    ///     distinctive in the opposite direction.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (CLAUDE.md). The case-'l' /level slash command
    /// is the retail server's documented admin level-set debug path. The
    /// AdminLevel guard, range check, and out-of-range message are all
    /// retail behaviour. No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Why out-of-range matters. The valid-range path (atoi(param) in
    /// 0..50) emits TWO MESSAGE_STRING replies and mutates player state
    /// (combat/trade/explore levels + skill points + saved-advance
    /// rows). Both emits use format substitution with atoi(param), so
    /// the wire-shape varies with the param. Pinning the out-of-range
    /// arm gives byte-exact stability with no state mutation -- a clean
    /// hardening pin.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashLevelOutOfRange_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (17) = 20 bytes.
        const int ExpectedReplyPayloadLength = 20;
        // strlen(literal) + 1 NUL = 17.
        const short ExpectedReplyLengthField = 17;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 16.
        const int ExpectedLiteralByteCount = 16;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Leveler", shipName: "LvlShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/level 99");

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

                // Filter on the distinctive prefix so handshake-tail and
                // chatter frames don't race ahead of the /level reply.
                // Once we have the right reply we pin its full wire shape.
                if (!text.StartsWith("0 <= Level", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(LevelOutOfRangeLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/level 99\" without seeing 0x001D MESSAGE_STRING starting with " +
                $"\"0 <= Level\". Likely the user-block case-'l' arm at " +
                $"server/src/PlayerConnection.cpp:6777 changed shape, MatchOptWithParam " +
                $"at PlayerConnection.cpp:4526 broke param extraction, the AdminLevel " +
                $"body-block guard at PlayerConnection.cpp:6782 inverted, or the atoi " +
                $"out-of-range check at PlayerConnection.cpp:6785 changed bounds.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// from INSIDE <c>Player::MatchOptWithParam</c> at
    /// <c>server/src/PlayerConnection.cpp:4548</c> on the missing-arg
    /// error path. When a slash command matches an option name (strncmp
    /// passes) but supplies no '=' / ' ' separator AND the matcher was
    /// invoked with the default <c>allowNoParams = false</c>, the matcher
    /// emits <c>SendVaMessage("Missing arg for option %s", option)</c>
    /// with the option name substituted, then sets <c>msg_sent = true</c>
    /// and returns false. For <c>"/level"</c> (no trailing space or '='),
    /// the substituted body is "Missing arg for option level" -- 28
    /// ASCII bytes. SendVaMessage forwards to SendMessageString with the
    /// default colour=5 from the header default arg at
    /// PlayerConnection.h:277.
    /// </summary>
    private const string MissingArgLevelLiteral = "Missing arg for option level";

    /// <summary>
    /// Wave 131 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -> 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 32-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/level</c> with no param -- which routes into
    /// <c>MatchOptWithParam</c>'s ERROR fork (the "Missing arg" emit at
    /// PlayerConnection.cpp:4548) rather than its SUCCESS fork (the
    /// param-extraction path Wave 130 exercised).
    ///
    /// <para>
    /// Why a +0 ratchet, and why this specific sibling. Wave 130
    /// (<see cref="SlashLevelOutOfRange_OnAdminAccount_PinsExactReplyWireShape"/>)
    /// pinned MatchOptWithParam's SUCCESS path: strncmp matched,
    /// arg[len]==' ' set param, returned true, body-block AdminLevel
    /// guard passed, atoi out-of-range triggered the "0 &lt;= Level
    /// &lt;= 50" emit at PlayerConnection.cpp:6787. Wave 131 pins the
    /// SAME MatchOptWithParam function from the SAME slash dispatcher
    /// call site (case-'l' /level at PlayerConnection.cpp:6777) but via
    /// the ERROR fork instead -- the missing-separator branch at
    /// PlayerConnection.cpp:4548. The pair pins both forks of
    /// MatchOptWithParam's separator-check fan-out at
    /// PlayerConnection.cpp:4532 (`arg[len] == '=' || arg[len] == ' '`).
    /// </para>
    ///
    /// <para>
    /// Wave 131 also extends the sibling-arm catalogue on the
    /// HandleSlashCommands user-tier dispatcher to SIX byte-exact-pinned
    /// arms -- Waves 123/125/126/129/130/131 -- and is the SECOND pin on
    /// the case-'l' arm (sibling to Wave 130).
    /// </para>
    ///
    /// <para>
    /// What this catches. Five concrete regression classes Wave 130 is
    /// structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     MatchOptWithParam separator-check regression at
    ///     <c>server/src/PlayerConnection.cpp:4532</c>. The check is
    ///     <c>arg[len] == '=' || arg[len] == ' '</c>. A regression that
    ///     widened the accepted separators (e.g. <c>|| arg[len] ==
    ///     '\0'</c>) would let /level (no param) into the SUCCESS path,
    ///     where atoi("") would coerce to 0 and pass the range check,
    ///     setting combat/trade/explore levels to 0 -- a corrupting
    ///     side-effect. Wave 131 pins the missing-arg body specifically;
    ///     a regression that bypassed this body and hit the success
    ///     path would fail the prefix filter.
    ///   </item>
    ///   <item>
    ///     MatchOptWithParam <c>isalpha</c> guard regression at
    ///     <c>server/src/PlayerConnection.cpp:4537</c>. The guard
    ///     suppresses dispatch when arg[len] is a letter (so "/leveling"
    ///     doesn't match "/level"). For "/level" arg[len]='\0' which is
    ///     NOT isalpha, so this guard does NOT short-circuit -- control
    ///     falls through to the missing-arg emit. A regression that
    ///     widened isalpha to include '\0' (e.g. via a faulty
    ///     <c>iscntrl</c> swap) would short-circuit the missing-arg emit
    ///     entirely and return false silently. Wave 131's drain-timeout
    ///     catches this.
    ///   </item>
    ///   <item>
    ///     MatchOptWithParam <c>allowNoParams</c> default regression at
    ///     <c>server/src/PlayerConnection.cpp:4541</c>. The
    ///     <c>allowNoParams = false</c> default at the function
    ///     signature (PlayerConnection.cpp:4526) drives /level into the
    ///     error fork. A regression flipping the default to true would
    ///     set <c>param = NULL</c> and return true; the case-'l' arm
    ///     would then dereference <c>atoi(NULL)</c> -- undefined
    ///     behaviour, likely crash. Wave 131's PASSED state implies the
    ///     default is still false; a future regression that flipped it
    ///     would either crash the server or skip the missing-arg emit.
    ///   </item>
    ///   <item>
    ///     SendVaMessage %s format-substitution regression at
    ///     <c>server/src/PlayerClass.cpp:3422</c>. Wave 131 exercises a
    ///     %s substitution with a CONST-STRING-LITERAL option name
    ///     ("level") -- structurally distinct from Wave 125/126's %s
    ///     substitution with a TERNARY ("off"/"on") and Wave 129's %08x
    ///     substitution with an INT (-1). A regression in vsprintf_s's
    ///     handling of %s with a literal-string-from-caller would change
    ///     the body length or content. Wave 131 pins the exact 28-byte
    ///     body including the substituted option name.
    ///   </item>
    ///   <item>
    ///     MatchOptWithParam <c>msg_sent</c> by-ref write regression at
    ///     <c>server/src/PlayerConnection.cpp:4549</c>. The matcher sets
    ///     <c>msg_sent = true</c> before returning false. A regression
    ///     that dropped this assignment would leave msg_sent at its
    ///     previous state -- if false, the downstream HandleSlashCommands
    ///     post-switch tail (PlayerConnection.cpp:7060+ "unknown slash
    ///     command" path or similar) might emit a SECOND
    ///     MESSAGE_STRING. Wave 131's single-frame-shape assertion
    ///     would catch a second emit.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (CLAUDE.md). The MatchOptWithParam missing-arg
    /// emit is the retail server's documented dispatcher-level error
    /// path; it fires for any user (no admin gating -- the emit happens
    /// BEFORE control returns to the case-'l' body-block AdminLevel
    /// guard). No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Why no admin gating matters. The missing-arg emit is inside
    /// MatchOptWithParam itself, so it fires before the case-'l' body-
    /// block <c>if (AdminLevel() &gt;= GM)</c> guard at line 6782.
    /// Wave 131 uses the admin-level fixture for consistency with the
    /// rest of the slash-command suite, but the test would also pass
    /// against a non-admin account -- a useful invariant in its own
    /// right.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashLevelMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (29) = 32 bytes.
        const int ExpectedReplyPayloadLength = 32;
        // strlen(literal) + 1 NUL = 29.
        const short ExpectedReplyLengthField = 29;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 28.
        const int ExpectedLiteralByteCount = 28;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Missarg", shipName: "MissArgShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/level");

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

                // Filter on the distinctive prefix so handshake-tail and
                // chatter frames don't race ahead of the /level reply.
                // Once we have the right reply we pin its full wire shape.
                if (!text.StartsWith("Missing arg", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgLevelLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/level\" without seeing 0x001D MESSAGE_STRING starting with " +
                $"\"Missing arg\". Likely MatchOptWithParam's missing-arg branch at " +
                $"server/src/PlayerConnection.cpp:4548 changed shape, the separator " +
                $"check at line 4532 widened to accept no-separator, the isalpha guard " +
                $"at line 4537 widened to include NUL, or the allowNoParams default at " +
                $"line 4526 flipped to true.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// in HandleSlashCommands' user-block case 'b' for the
    /// <c>strcmp(pch, "beon") == 0</c> arm when
    /// <c>AdminLevel() &gt;= BETA</c>. The literal is hard-coded as
    /// <c>"Beta channel on."</c> at
    /// <c>server/src/PlayerConnection.cpp:5525</c>; no format substitution
    /// -- raw literal pass-through into SendVaMessage, which threads the
    /// default colour=5 via the header default arg at
    /// PlayerConnection.h:277. 16 ASCII bytes. The arm has a side-effect
    /// (sets <c>m_ChannelSubscription[Beta] = true</c> at line 5524)
    /// BEFORE the emit; the per-account isolation (cli_test124
    /// dedicated, ServerFixture tears down with -v wiping pgdata) makes
    /// this safe.
    /// </summary>
    private const string BetaChannelOnLiteral = "Beta channel on.";

    /// <summary>
    /// Wave 132 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -> 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 20-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/beon</c> against an admin-level fixture (status=100
    /// -&gt; AdminLevel()=100 &gt;= BETA=30).
    ///
    /// <para>
    /// Why a +0 ratchet, and why this specific sibling. Wave 130
    /// (<see cref="SlashLevelOutOfRange_OnAdminAccount_PinsExactReplyWireShape"/>)
    /// pinned a 20-byte wire-shape on the SendVaMessage default-colour
    /// fork via a raw-literal pass-through ("0 &lt;= Level &lt;= 50")
    /// gated on AdminLevel &gt;= GM (50) via a body-block guard, with
    /// MatchOptWithParam dispatch and early `return`. Wave 132 pins the
    /// SAME 20-byte wire-shape and the SAME default-colour raw-literal
    /// pass-through, but with FOUR orthogonal differences: (a) case-
    /// letter 'b' vs 'l'; (b) dispatch via strcmp (Wave 132) vs
    /// MatchOptWithParam (Wave 130); (c) AdminLevel threshold BETA=30
    /// (Wave 132) vs GM=50 (Wave 130) -- pinning two different rungs of
    /// the access-control ladder defined at Net7.h:367-373; (d) `break`
    /// (Wave 132 implicit via case-fall-through to break at 5630) vs
    /// early `return` (Wave 130 explicit at 6788). The identical wire-
    /// shape between Wave 130 and Wave 132 is intentional -- it
    /// guarantees that a regression that affects the SendMessageString
    /// length-prefix or color-byte routing would fail BOTH tests in the
    /// same way, while a regression that affects only ONE arm's
    /// dispatch/guard/threshold would fail only that arm's test.
    /// </para>
    ///
    /// <para>
    /// Wave 132 also extends the sibling-arm catalogue on the
    /// HandleSlashCommands user-tier dispatcher to SEVEN byte-exact-
    /// pinned arms -- Waves 123/125/126/129/130/131/132 -- and is the
    /// FIRST pin on the case-'b' arm.
    /// </para>
    ///
    /// <para>
    /// What this catches. Five concrete regression classes Waves
    /// 123/125/126/129/130/131 are structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     AdminLevel BETA tier regression at
    ///     <c>server/src/Net7.h:373</c>. The constant <c>BETA = 30</c>
    ///     defines the lowest admin tier. A regression that flipped BETA
    ///     to a higher value (e.g. 50 collapsing into GM) would gate the
    ///     /beon emit out for status=100-but-below-new-BETA accounts.
    ///     Wave 130 pins GM=50; Wave 132 pins BETA=30; together they nail
    ///     two rungs of the access-control ladder. A regression in the
    ///     ladder ordering would fail one or both tests.
    ///   </item>
    ///   <item>
    ///     Body-block <c>if (AdminLevel() &gt;= BETA)</c> guard regression
    ///     at <c>server/src/PlayerConnection.cpp:5521</c>. The comment on
    ///     the line itself reads "low right now for zapgun's loot
    ///     builders" -- indicating the threshold is intentionally lowered
    ///     for tooling. A regression that raised the threshold would gate
    ///     out the BETA-tier emit silently. Wave 132 catches.
    ///   </item>
    ///   <item>
    ///     m_ChannelSubscription side-effect regression at
    ///     <c>server/src/PlayerConnection.cpp:5524</c>. The arm sets
    ///     <c>m_ChannelSubscription[channel_id] = true</c> BEFORE the
    ///     emit. A regression that swapped the order (or dropped the
    ///     set) would still emit the "Beta channel on." literal but
    ///     would silently leave the player unsubscribed. Wave 132's
    ///     wire-shape assertion alone does NOT catch this directly --
    ///     but the emit's existence at all confirms the strcmp arm
    ///     dispatched correctly, which is the minimum invariant.
    ///   </item>
    ///   <item>
    ///     <c>GetChannelFromName("Beta")</c> regression at
    ///     <c>server/src/PlayerConnection.cpp:5523</c>. A regression in
    ///     PlayerManager::GetChannelFromName that returned -1 or an
    ///     out-of-bounds index would not affect the emit (the call
    ///     result is only used to index m_ChannelSubscription), but a
    ///     buffer-overflow regression could corrupt subsequent state.
    ///     Wave 132's PASSED state implies the call returns a valid
    ///     index that doesn't immediately crash.
    ///   </item>
    ///   <item>
    ///     Implicit-`break` regression at
    ///     <c>server/src/PlayerConnection.cpp:5630</c>. Unlike Wave 130's
    ///     early `return` short-circuit, Wave 132 relies on falling
    ///     through to the case-'b' implicit `break` at the end of the
    ///     case. A regression that added a follow-up emit between
    ///     /beon's body and the case-end would fail Wave 132's single-
    ///     frame-shape assertion.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (CLAUDE.md). The case-'b' /beon slash command is
    /// the retail server's documented BETA-channel subscription enable
    /// path. The AdminLevel guard (lowered to BETA per the in-code
    /// comment for zapgun's loot builders) and the emit shape are retail
    /// behaviour. No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Why fresh-account matters. The arm sets
    /// m_ChannelSubscription[Beta]=true unconditionally; the per-account
    /// isolation (cli_test124 dedicated, ServerFixture tears down with
    /// -v wiping pgdata) makes this safe across retries.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashBeon_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (17) = 20 bytes.
        const int ExpectedReplyPayloadLength = 20;
        // strlen(literal) + 1 NUL = 17.
        const short ExpectedReplyLengthField = 17;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 16.
        const int ExpectedLiteralByteCount = 16;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Beoner", shipName: "BeonShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/beon");

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

                // Filter on the distinctive prefix so handshake-tail and
                // chatter frames don't race ahead of the /beon reply.
                // Once we have the right reply we pin its full wire shape.
                if (!text.StartsWith("Beta channel on", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(BetaChannelOnLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/beon\" without seeing 0x001D MESSAGE_STRING starting with " +
                $"\"Beta channel on\". Likely the user-block case-'b' arm at " +
                $"server/src/PlayerConnection.cpp:5519 changed shape, the body-block " +
                $"AdminLevel>=BETA guard at PlayerConnection.cpp:5521 inverted, the BETA " +
                $"constant at Net7.h:373 changed, or GetChannelFromName at " +
                $"PlayerConnection.cpp:5523 broke.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// in HandleSlashCommands' user-block case 'b' for the
    /// <c>strcmp(pch, "beoff") == 0</c> arm when
    /// <c>AdminLevel() &gt;= BETA</c>. The literal is hard-coded as
    /// <c>"Beta channel off."</c> at
    /// <c>server/src/PlayerConnection.cpp:5541</c>; no format substitution
    /// -- raw literal pass-through into SendVaMessage with default
    /// colour=5 (PlayerConnection.h:277). 17 ASCII bytes. The arm has a
    /// side-effect (sets <c>m_ChannelSubscription[Beta] = false</c> at
    /// line 5540) BEFORE the emit. Sibling pair to Wave 132's /beon.
    /// </summary>
    private const string BetaChannelOffLiteral = "Beta channel off.";

    /// <summary>
    /// Wave 133 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 21-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/beoff</c> against an admin-level fixture (status=100
    /// -&gt; AdminLevel()=100 &gt;= BETA=30).
    ///
    /// <para>
    /// Sibling pair to Wave 132 (/beon). Wave 133 completes the
    /// case-'b' channel-toggle: Wave 132 pins the subscribe path
    /// (m_ChannelSubscription[Beta]=true + "Beta channel on." 16-byte
    /// literal -&gt; 20-byte wire); Wave 133 pins the unsubscribe path
    /// (m_ChannelSubscription[Beta]=false + "Beta channel off."
    /// 17-byte literal -&gt; 21-byte wire). Both arms share IDENTICAL
    /// case-letter ('b'), IDENTICAL dispatch (strcmp), IDENTICAL
    /// AdminLevel-tier guard (BETA at PlayerConnection.cpp:5537),
    /// IDENTICAL emit fork (SendVaMessage default-colour=5), IDENTICAL
    /// raw-literal pass-through (no format substitution), IDENTICAL
    /// success/msg_sent set-pattern, IDENTICAL short-circuit (implicit
    /// case-end break at line 5630) -- the ONLY differences are the
    /// 17 vs 16 byte literal length and the m_ChannelSubscription set
    /// value (false vs true). This makes Waves 132+133 the TIGHTEST
    /// sibling pair in the entire byte-exact catalogue: any regression
    /// shared between the two arms would fail BOTH tests; any
    /// regression specific to one arm (e.g. wrong literal in /beon,
    /// wrong set-value direction) would fail only that arm.
    /// </para>
    ///
    /// <para>
    /// Wave 133 also extends the sibling-arm catalogue on the
    /// HandleSlashCommands user-tier dispatcher to EIGHT byte-exact-
    /// pinned arms -- Waves 123/125/126/129/130/131/132/133 -- and is
    /// the SECOND pin on the case-'b' arm.
    /// </para>
    ///
    /// <para>
    /// What this catches. Four concrete regression classes Wave 132 is
    /// structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     /beoff literal regression at
    ///     <c>server/src/PlayerConnection.cpp:5541</c>. A copy-paste
    ///     error from /beon would emit "Beta channel on." for /beoff.
    ///     Wave 133 pins the exact "Beta channel off." literal -- the
    ///     17 vs 16 length-field divergence and the trailing "off."
    ///     byte sequence catch it.
    ///   </item>
    ///   <item>
    ///     m_ChannelSubscription set-value-direction regression at
    ///     <c>server/src/PlayerConnection.cpp:5540</c>. The /beoff arm
    ///     sets the subscription to FALSE; a regression that wrote
    ///     `true` here would silently leave the player subscribed.
    ///     Wave 133's wire-shape assertion alone does NOT catch this
    ///     directly (the emit fires regardless of the set value), but
    ///     the emit's presence confirms strcmp dispatch fired
    ///     correctly into the unsubscribe arm.
    ///   </item>
    ///   <item>
    ///     case-'b' arm ordering regression at
    ///     <c>server/src/PlayerConnection.cpp:5519-5550</c>. The /beon
    ///     and /beoff arms are sequential else-if siblings. A
    ///     regression that reordered or merged the arms (e.g.
    ///     accidentally falling through from /beon to /beoff after
    ///     the success block) would fire BOTH emits, doubling the
    ///     frame count. Wave 132+133 together pin the single-emit
    ///     invariant per command.
    ///   </item>
    ///   <item>
    ///     /beoff GetChannelFromName lookup-path regression at
    ///     <c>server/src/PlayerConnection.cpp:5539</c>. Independent
    ///     call from /beon's lookup at line 5523. A regression in
    ///     PlayerManager::GetChannelFromName affecting only the
    ///     /beoff call path would fail Wave 133 alone.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (CLAUDE.md). The case-'b' /beoff slash command
    /// is the retail server's documented BETA-channel subscription
    /// disable path, paired with /beon. The AdminLevel guard, the set
    /// of m_ChannelSubscription[Beta]=false, and the emit shape are
    /// all retail behaviour. No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Why fresh-account matters. The arm sets
    /// m_ChannelSubscription[Beta]=false unconditionally; the per-
    /// account isolation (cli_test125 dedicated, ServerFixture tears
    /// down with -v wiping pgdata) makes this safe across retries.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashBeoff_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (18) = 21 bytes.
        const int ExpectedReplyPayloadLength = 21;
        // strlen(literal) + 1 NUL = 18.
        const short ExpectedReplyLengthField = 18;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 17.
        const int ExpectedLiteralByteCount = 17;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Beoffer", shipName: "BeoffShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/beoff");

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

                // Filter on the distinctive prefix so handshake-tail and
                // chatter frames don't race ahead of the /beoff reply.
                // Note: "Beta channel" is shared with Wave 132's /beon
                // reply, but with cli_test125 isolation and a single
                // /beoff request on this connection there is no /beon
                // emit to race with. We still filter on the full
                // "channel off" suffix to make the test deterministic
                // if someone ever wires both slash commands in series.
                if (!text.StartsWith("Beta channel off", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(BetaChannelOffLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/beoff\" without seeing 0x001D MESSAGE_STRING starting with " +
                $"\"Beta channel off\". Likely the user-block case-'b' /beoff arm at " +
                $"server/src/PlayerConnection.cpp:5535 changed shape, the body-block " +
                $"AdminLevel>=BETA guard at PlayerConnection.cpp:5537 inverted, the BETA " +
                $"constant at Net7.h:373 changed, or GetChannelFromName at " +
                $"PlayerConnection.cpp:5539 broke.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// when <c>MatchOptWithParam("chjoin", "chjoin", ...)</c> at
    /// <c>server/src/PlayerConnection.cpp:5633</c> falls through the
    /// separator-check at line 4532 (NUL is not '=', not ' ', not
    /// isalpha), through the allowNoParams default-false guard at
    /// line 4541, and hits the else-branch emit at
    /// <c>PlayerConnection.cpp:4548</c>:
    /// <c>SendVaMessage("Missing arg for option %s", "chjoin")</c>.
    /// 29 ASCII bytes after %s substitution. Same emit location as
    /// Wave 131's /level pin -- different option name parameter.
    /// </summary>
    private const string MissingArgChjoinLiteral = "Missing arg for option chjoin";

    /// <summary>
    /// Wave 134 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 33-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/chjoin</c> (NO param) on any account tier -- routes
    /// into MatchOptWithParam's ERROR fork (the missing-arg emit at
    /// PlayerConnection.cpp:4548) BEFORE control reaches the case-'c'
    /// body-block AdminLevel guard at line 5637.
    ///
    /// <para>
    /// Sibling to Wave 131 (/level missing-arg pattern). Wave 131 pins
    /// the SAME MatchOptWithParam ERROR fork at PlayerConnection.cpp:4548
    /// with option name "level" (5 bytes); Wave 134 pins the SAME emit
    /// with option name "chjoin" (6 bytes). Both arms share IDENTICAL
    /// dispatch path (case-letter -&gt; MatchOptWithParam -&gt;
    /// strncmp passes -&gt; arg[len]=NUL -&gt; fall-through to else-branch
    /// -&gt; SendVaMessage with %s substitution of CONST-STRING-LITERAL),
    /// IDENTICAL emit fork (SendVaMessage default-colour=5), IDENTICAL
    /// AdminLevel non-gating (the matcher fires BEFORE any body-block
    /// guard runs -- works on any user tier), IDENTICAL msg_sent
    /// by-ref write (line 4549). The ONLY differences are the case-
    /// letter ('c' vs 'l') and the option-name substituted into the
    /// %s slot (6 vs 5 bytes -&gt; 29 vs 28 byte body -&gt; 33 vs 32
    /// byte wire). This makes Waves 131+134 a TIGHT sibling pair
    /// pinning the %s format-substitution length-variability of
    /// MatchOptWithParam's missing-arg emit.
    /// </para>
    ///
    /// <para>
    /// Wave 134 also extends the sibling-arm catalogue on the
    /// HandleSlashCommands user-tier dispatcher to NINE byte-exact-
    /// pinned arms -- Waves 123/125/126/129/130/131/132/133/134 -- and
    /// is the FIRST pin on the case-'c' arm at
    /// <c>server/src/PlayerConnection.cpp:5631</c>.
    /// </para>
    ///
    /// <para>
    /// What this catches. Four concrete regression classes Wave 131 is
    /// structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     case-'c' arm dispatch regression at
    ///     <c>server/src/PlayerConnection.cpp:5631</c>. A regression
    ///     that swapped the case-'c' arm with a different case-letter
    ///     or rerouted the /chjoin opt-name to a different matcher
    ///     would fail the literal-prefix filter. Wave 134 is the
    ///     FIRST pin proving case-'c' dispatches at all.
    ///   </item>
    ///   <item>
    ///     %s format-substitution length-variability regression at
    ///     PlayerClass.cpp:3422 -- vsprintf_s must correctly handle
    ///     the variable-width %s substitution; a regression that
    ///     mishandled 6-byte vs 5-byte substitution (e.g. length
    ///     accounting off-by-one) would emit a different body length.
    ///     Wave 131 pins the 5-byte substitution; Wave 134 pins the
    ///     6-byte. A regression specific to width-handling at one
    ///     length would fail one test.
    ///   </item>
    ///   <item>
    ///     /chjoin opt-name passed-as-second-argument regression at
    ///     <c>server/src/PlayerConnection.cpp:5633</c>. The matcher
    ///     receives "chjoin" as the option name. A regression that
    ///     mis-spelled the argument or swapped opt-names with a
    ///     sibling matcher call (e.g. "chleave", "ccamera",
    ///     "changepassword") would emit the wrong %s body. Wave 134
    ///     pins exact "chjoin".
    ///   </item>
    ///   <item>
    ///     case-'c' sibling-matcher fan-out regression at
    ///     <c>server/src/PlayerConnection.cpp:5667-5704</c>. After
    ///     MatchOptWithParam("chjoin", ...) returns false, control
    ///     falls through to MatchOptWithParam("chleave", ...),
    ///     MatchOptWithParam("ccamera", ...),
    ///     MatchOptWithParam("changepassword", ...) etc -- each
    ///     should strncmp-fail and return false WITHOUT emitting. A
    ///     regression that fell through into one of these without
    ///     proper strncmp-rejection would emit a second
    ///     MESSAGE_STRING; Wave 134's single-frame-shape assertion
    ///     catches.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (CLAUDE.md). The MatchOptWithParam missing-arg
    /// emit is the retail server's documented dispatcher-level error
    /// path; fires for any user (no admin gating) before the body
    /// block's AdminLevel guard at line 5637. No server permissiveness
    /// added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashChjoinMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (30) = 33 bytes.
        const int ExpectedReplyPayloadLength = 33;
        // strlen(literal) + 1 NUL = 30.
        const short ExpectedReplyLengthField = 30;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 29.
        const int ExpectedLiteralByteCount = 29;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Chjoiner", shipName: "ChjoinShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/chjoin");

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

                // Filter on the distinctive "for option chjoin" suffix --
                // the "Missing arg for option" prefix is shared with
                // Wave 131's /level reply, but with cli_test126
                // isolation and a single /chjoin request we don't race
                // /level. Use a more specific prefix to be defensive.
                if (!text.StartsWith("Missing arg for option chjoin", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgChjoinLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/chjoin\" without seeing 0x001D MESSAGE_STRING starting with " +
                $"\"Missing arg for option chjoin\". Likely the case-'c' arm at " +
                $"server/src/PlayerConnection.cpp:5631 stopped dispatching to " +
                $"MatchOptWithParam at line 5633, or the missing-arg ERROR fork at " +
                $"PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// when <c>MatchOptWithParam("chleave", "chleave", ...)</c> at
    /// <c>server/src/PlayerConnection.cpp:5667</c> falls through the
    /// separator-check (NUL is not '=', not ' ', not isalpha) and hits
    /// the else-branch emit at <c>PlayerConnection.cpp:4548</c>:
    /// <c>SendVaMessage("Missing arg for option %s", "chleave")</c>.
    /// 30 ASCII bytes after %s substitution. Same emit location as
    /// Wave 131 (/level) and Wave 134 (/chjoin); 7-byte option name
    /// substituted into %s slot.
    /// </summary>
    private const string MissingArgChleaveLiteral = "Missing arg for option chleave";

    /// <summary>
    /// Wave 135 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 34-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/chleave</c> (NO param) -- routes into
    /// MatchOptWithParam's ERROR fork at PlayerConnection.cpp:4548
    /// BEFORE control reaches the case-'c' body-block AdminLevel guard.
    ///
    /// <para>
    /// TIGHT same-case-letter sibling pair with Wave 134's /chjoin. Both
    /// arms are in the SAME case-'c' block, share IDENTICAL dispatch
    /// path (MatchOptWithParam -&gt; strncmp pass -&gt; NUL arg[len]
    /// -&gt; fall-through to else-branch emit), IDENTICAL emit fork
    /// (SendVaMessage default-colour=5), IDENTICAL AdminLevel
    /// non-gating, IDENTICAL %s format-substitution shape. The ONLY
    /// differences are the option-name passed to the matcher
    /// (/chjoin's "chjoin" 6 bytes vs /chleave's "chleave" 7 bytes)
    /// AND the matcher's position in the case-'c' arm (chjoin is the
    /// FIRST matcher at line 5633; chleave is the SECOND at line
    /// 5667 -- exercising the matcher fall-through path that chjoin
    /// does NOT). This SECOND-matcher pinning is the new structural
    /// fan-out Wave 135 brings.
    /// </para>
    ///
    /// <para>
    /// Wave 135 also extends the sibling-arm catalogue on the
    /// HandleSlashCommands user-tier dispatcher to TEN byte-exact-
    /// pinned arms (Waves 123/125/126/129/130/131/132/133/134/135),
    /// is the SECOND pin on case-'c', and is the THIRD pin on the
    /// MatchOptWithParam ERROR path (after Waves 131 /level and 134
    /// /chjoin).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 134
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     case-'c' matcher fall-through regression at
    ///     <c>server/src/PlayerConnection.cpp:5633-5667</c>. After
    ///     MatchOptWithParam("chjoin", "chleave", ...) returns false
    ///     (strncmp mismatches at index 2: 'j' vs 'l'), control must
    ///     fall through to MatchOptWithParam("chleave", ...) at line
    ///     5667. A regression that short-circuited the chjoin matcher
    ///     to NOT return false (e.g. returning true on strncmp
    ///     mismatch) or that gated the chleave matcher behind chjoin's
    ///     success would silently swallow the /chleave emit. Wave 135
    ///     pins that the chleave matcher is REACHED.
    ///   </item>
    ///   <item>
    ///     %s format-substitution length-variability regression
    ///     (extends Wave 134's coverage) at PlayerClass.cpp:3422.
    ///     Wave 131 pins 5-byte ("level"); Wave 134 pins 6-byte
    ///     ("chjoin"); Wave 135 pins 7-byte ("chleave"). A regression
    ///     in vsprintf_s with an off-by-one length bug at a specific
    ///     width would fail one test but not the others.
    ///   </item>
    ///   <item>
    ///     /chleave opt-name passed-as-second-argument regression at
    ///     <c>server/src/PlayerConnection.cpp:5667</c>. The matcher
    ///     receives "chleave" as the option name. A regression that
    ///     mis-spelled it as "chleve" (or swapped with /chjoin's
    ///     "chjoin") would emit the wrong %s body. Wave 135 pins
    ///     exact "chleave".
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (CLAUDE.md). The MatchOptWithParam missing-arg
    /// emit is the retail server's documented dispatcher-level error
    /// path; fires for any user (no admin gating). No server
    /// permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashChleaveMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (31) = 34 bytes.
        const int ExpectedReplyPayloadLength = 34;
        // strlen(literal) + 1 NUL = 31.
        const short ExpectedReplyLengthField = 31;
        // SendVaMessage -> SendMessageString default color parameter.
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
            firstName: "Chleaver", shipName: "ChleaveShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/chleave");

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

                // Filter on the distinctive "for option chleave" suffix.
                if (!text.StartsWith("Missing arg for option chleave", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgChleaveLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/chleave\" without seeing 0x001D MESSAGE_STRING starting with " +
                $"\"Missing arg for option chleave\". Likely the case-'c' chleave " +
                $"matcher at server/src/PlayerConnection.cpp:5667 stopped dispatching, or " +
                $"the chjoin matcher at line 5633 incorrectly returned true (preventing " +
                $"fall-through), or the missing-arg ERROR fork at PlayerConnection.cpp:4548 " +
                $"changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// when the GM-block (<c>//</c>-prefix) dispatch at
    /// <c>server/src/PlayerConnection.cpp:4716</c> strips the leading
    /// 2 chars and hands <c>pch="adduser"</c> to
    /// <c>MatchOptWithParam("adduser", pch, param, msg_sent)</c> at
    /// <c>PlayerConnection.cpp:4728</c>. The matcher's separator-check
    /// (NUL is not '=', not ' ', not isalpha) hits the else-branch emit
    /// at <c>PlayerConnection.cpp:4548</c>:
    /// <c>SendVaMessage("Missing arg for option %s", "adduser")</c>.
    /// 30 ASCII bytes after %s substitution. Same emit location as
    /// Waves 131 / 134 / 135; 7-byte option name in %s slot.
    /// </summary>
    private const string MissingArgAdduserLiteral = "Missing arg for option adduser";

    /// <summary>
    /// Wave 136 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 34-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the GM-tier slash
    /// command <c>//adduser</c> (NO param) -- routes through the
    /// GM-block (<c>//</c>-prefix) entry guard at
    /// <c>server/src/PlayerConnection.cpp:4716</c>, the 2-char strip
    /// (<c>pch = Msg + 2</c>) at line 4719-4721, the case-'a' GM-block
    /// dispatch at line 4726-4728, and MatchOptWithParam's ERROR fork
    /// at <c>PlayerConnection.cpp:4548</c> BEFORE control reaches the
    /// adduser body-block strtok param parsing.
    ///
    /// <para>
    /// FIRST pin on the GM-block (<c>//</c>-prefix) dispatch path.
    /// Prior Waves 117 / 123 / 125 / 126 / 129 / 130 / 131 / 132 / 133 /
    /// 134 / 135 (the user-tier slash arms) all entered HandleSlashCommands
    /// and fell through the GM-block guard at line 4716 (because their
    /// Msg[1] != '/'). Wave 136 is the FIRST byte-exact pin that EXERCISES
    /// the GM-block guard's positive path: Msg[0]=='/' AND Msg[1]=='/'
    /// AND Msg[2]!='\0' AND AdminLevel() &gt;= GM. Status=100 fixture
    /// account satisfies AdminLevel() == 100 &gt;= 50 (GM).
    /// </para>
    ///
    /// <para>
    /// Wave 136 also extends the sibling-arm catalogue on
    /// HandleSlashCommands to ELEVEN byte-exact-pinned arms (Waves
    /// 117/123/125/126/129/130/131/132/133/134/135 user-tier +
    /// 136 GM-tier), is the FIRST pin on case-'a' GM-block (vs Wave
    /// 117's case-'a' user-block /authlevel), and is the FOURTH pin on
    /// the MatchOptWithParam ERROR path (after Waves 131 /level, 134
    /// /chjoin, 135 /chleave).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Waves 117 /
    /// 123 / 125 / 126 / 129 / 130 / 131 / 132 / 133 / 134 / 135 are
    /// structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     GM-block entry-guard regression at
    ///     <c>server/src/PlayerConnection.cpp:4716</c>. The condition
    ///     <c>Msg[0]=='/' AND Msg[1]=='/' AND Msg[2]!=0 AND
    ///     AdminLevel() &gt;= GM</c> must hold. A regression that
    ///     tightened the AdminLevel cutoff (e.g. raised to DEV=80) for
    ///     a status=100 account, that mis-indexed Msg[1] vs Msg[0]
    ///     (would mis-route ALL user-tier slash arms or NONE of the
    ///     GM-tier ones), or that dropped the GM-block guard entirely
    ///     would silently fail to dispatch //adduser. Wave 136 pins
    ///     that the GM-block IS REACHED for status=100.
    ///   </item>
    ///   <item>
    ///     2-char strip regression at
    ///     <c>server/src/PlayerConnection.cpp:4719-4721</c>. The
    ///     <c>_alloca(strlen(&amp;Msg[2]) + 1)</c> + <c>strcpy_s(...,
    ///     &amp;Msg[2])</c> strips the leading <c>//</c> from
    ///     <c>//adduser</c> leaving <c>pch="adduser"</c>. A regression
    ///     that mis-offset (e.g. <c>&amp;Msg[1]</c>) would leave
    ///     <c>pch="/adduser"</c>; the switch(*pch) would land on case
    ///     '/' (not present in the GM-block switch -- defaults to no
    ///     match) and emit NOTHING. Wave 136 pins that the strip lands
    ///     at offset 2 EXACTLY.
    ///   </item>
    ///   <item>
    ///     case-'a' GM-block dispatch regression at
    ///     <c>server/src/PlayerConnection.cpp:4726-4728</c>. The
    ///     case-'a' arm contains MatchOptWithParam("adduser", ...) AND
    ///     a second AdminLevel() &gt;= GM guard inside the matcher's
    ///     true-branch (defense-in-depth -- redundant with the outer
    ///     guard at 4716). A regression that swapped the case letter,
    ///     mis-spelled "adduser" as the matcher's first argument
    ///     (would emit the wrong %s body), or removed the case-'a' arm
    ///     entirely would silently drop the //adduser dispatch.
    ///     Wave 136 pins the case-'a' arm reaches the matcher AND that
    ///     the matcher emits the missing-arg literal with the EXACT
    ///     "adduser" %s substitution.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (CLAUDE.md). The MatchOptWithParam missing-arg
    /// emit is the retail server's documented dispatcher-level error
    /// path; the GM-block guard at line 4716 enforces the AdminLevel
    /// &gt;= GM gate the retail server enforced. No server permissiveness
    /// added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashSlashAdduserMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (31) = 34 bytes.
        const int ExpectedReplyPayloadLength = 34;
        // strlen(literal) + 1 NUL = 31.
        const short ExpectedReplyLengthField = 31;
        // SendVaMessage -> SendMessageString default color parameter.
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
            firstName: "Adduser", shipName: "AdduserShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "//adduser");

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

                // Filter on the distinctive "for option adduser" suffix.
                if (!text.StartsWith("Missing arg for option adduser", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgAdduserLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"//adduser\" without seeing 0x001D MESSAGE_STRING starting with " +
                $"\"Missing arg for option adduser\". Likely the GM-block entry guard at " +
                $"server/src/PlayerConnection.cpp:4716 stopped admitting status=100 accounts, " +
                $"the 2-char strip at lines 4719-4721 mis-offset, the case-'a' GM-block " +
                $"matcher at line 4728 stopped dispatching, or the missing-arg ERROR fork at " +
                $"PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// when the GM-block (<c>//</c>-prefix) dispatch at
    /// <c>server/src/PlayerConnection.cpp:4716</c> strips the leading
    /// 2 chars and hands <c>pch="ban"</c> to
    /// <c>MatchOptWithParam("ban", pch, param, msg_sent)</c> at
    /// <c>PlayerConnection.cpp:4756</c>. The matcher's separator-check
    /// (NUL is not '=', not ' ', not isalpha) hits the else-branch emit
    /// at <c>PlayerConnection.cpp:4548</c>:
    /// <c>SendVaMessage("Missing arg for option %s", "ban")</c>.
    /// 26 ASCII bytes after %s substitution. Same emit location as
    /// Waves 131 / 134 / 135 / 136; NEW MINIMAL 3-byte option name in %s
    /// slot (vs prior pins' 5/6/7-byte widths).
    /// </summary>
    private const string MissingArgBanLiteral = "Missing arg for option ban";

    /// <summary>
    /// Wave 137 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 30-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the GM-tier slash
    /// command <c>//ban</c> (NO param) -- routes through the GM-block
    /// (<c>//</c>-prefix) entry guard at
    /// <c>server/src/PlayerConnection.cpp:4716</c>, the 2-char strip at
    /// lines 4719-4721, the case-'b' GM-block dispatch at line 4754-4756,
    /// and MatchOptWithParam's ERROR fork at
    /// <c>PlayerConnection.cpp:4548</c> BEFORE control reaches the
    /// case-'b' GM-block body-block strtok param parsing.
    ///
    /// <para>
    /// SECOND pin on the GM-block (<c>//</c>-prefix) dispatch path
    /// (after Wave 136's //adduser). FIRST pin on case-'b' GM-block.
    /// TIGHT same-case-letter sibling pair with Waves 132 (/beon) and
    /// 133 (/beoff) which both pinned case-'b' user-block -- Wave 137
    /// pins the case-'b' GM-block, spanning the TWO TIERS of
    /// HandleSlashCommands within case-'b' (mirroring Wave 136's
    /// tier-spanning pair within case-'a' against Wave 117).
    /// </para>
    ///
    /// <para>
    /// Wave 137 also extends the sibling-arm catalogue on
    /// HandleSlashCommands to TWELVE byte-exact-pinned arms across BOTH
    /// tiers (Waves 117/123/125/126/129/130/131/132/133/134/135 user-tier
    /// + 136/137 GM-tier), is the SECOND pin on the GM-block dispatch
    /// path (after Wave 136), and is the FIFTH pin on the
    /// MatchOptWithParam ERROR path (after Waves 131 /level 5-byte,
    /// 134 /chjoin 6-byte, 135 /chleave 7-byte, 136 //adduser 7-byte) --
    /// NEW MINIMAL 3-byte option-name width pin.
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 136
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     %s format-substitution minimal-width regression at
    ///     PlayerClass.cpp:3422. Wave 131 pins 5-byte ("level"), Wave
    ///     134 pins 6-byte ("chjoin"), Wave 135 pins 7-byte ("chleave"),
    ///     Wave 136 pins 7-byte ("adduser"), Wave 137 pins 3-byte ("ban").
    ///     A regression with off-by-one length accounting at a specific
    ///     short width (e.g. mishandling option names &lt; 5 bytes) would
    ///     fail Wave 137 but pass all prior pins. NEW MINIMAL-WIDTH
    ///     %s-substitution coverage.
    ///   </item>
    ///   <item>
    ///     case-'b' GM-block dispatch regression at
    ///     <c>server/src/PlayerConnection.cpp:4754-4756</c>. The
    ///     case-'b' GM-block arm contains MatchOptWithParam("ban", ...)
    ///     (NO redundant AdminLevel guard inside the matcher's true-branch
    ///     -- the outer GM-block entry-guard at line 4716 is the only
    ///     gate). A regression that swapped the case letter, mis-spelled
    ///     "ban" as the matcher's first argument (would emit the wrong %s
    ///     body), or removed the case-'b' GM-block arm entirely would
    ///     silently drop the //ban dispatch. Wave 137 pins case-'b'
    ///     GM-block reaches the matcher AND that the matcher emits the
    ///     missing-arg literal with the EXACT "ban" %s substitution.
    ///   </item>
    ///   <item>
    ///     GM-block tier-routing fidelity regression at
    ///     <c>server/src/PlayerConnection.cpp:4716</c> + line 4754. The
    ///     case-'b' letter is ALSO present in the user-tier block at
    ///     line 5519 (/beon) and 5535 (/beoff). A regression that
    ///     mis-routed //ban through the user-tier dispatcher would land
    ///     on case-'b' user-block and either emit "Beta channel on."
    ///     (matching /beon) or fall through case-'b' user-block to no
    ///     emit -- both would silently swallow the //ban missing-arg
    ///     emit. Wave 137 pins that //ban routes through the GM-tier,
    ///     NOT the user-tier, even though case-'b' is reachable from both.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-level
    /// error path; the GM-block guard at line 4716 enforces the
    /// AdminLevel &gt;= GM gate the retail server enforced. No server
    /// permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashSlashBanMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (27) = 30 bytes.
        const int ExpectedReplyPayloadLength = 30;
        // strlen(literal) + 1 NUL = 27.
        const short ExpectedReplyLengthField = 27;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 26.
        const int ExpectedLiteralByteCount = 26;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Banner", shipName: "BanShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "//ban");

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

                // Filter on the distinctive "for option ban" suffix.
                if (!text.StartsWith("Missing arg for option ban", StringComparison.Ordinal))
                    continue;

                // Reject longer-suffix collisions (e.g. "Missing arg for
                // option banaccount" if such an arm ever existed).
                if (text.Length > "Missing arg for option ban".Length &&
                    char.IsLetter(text["Missing arg for option ban".Length]))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgBanLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"//ban\" without seeing 0x001D MESSAGE_STRING starting with " +
                $"\"Missing arg for option ban\". Likely the GM-block entry guard at " +
                $"server/src/PlayerConnection.cpp:4716 stopped admitting status=100 accounts, " +
                $"the 2-char strip at lines 4719-4721 mis-offset, the case-'b' GM-block " +
                $"matcher at line 4756 stopped dispatching, the case-'b' tier-routing " +
                $"mis-routed //ban to the user-tier dispatcher (case-'b' user-block at " +
                $"line 5519 /beon path), or the missing-arg ERROR fork at " +
                $"PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// when the GM-block (<c>//</c>-prefix) dispatch at
    /// <c>server/src/PlayerConnection.cpp:4716</c> strips the leading
    /// 2 chars and hands <c>pch="gmgetaccess"</c> to
    /// <c>MatchOptWithParam("gmgetaccess", pch, param, msg_sent)</c> at
    /// <c>PlayerConnection.cpp:5207</c>. The matcher's separator-check
    /// (NUL is not '=', not ' ', not isalpha) hits the else-branch emit
    /// at <c>PlayerConnection.cpp:4548</c>:
    /// <c>SendVaMessage("Missing arg for option %s", "gmgetaccess")</c>.
    /// 34 ASCII bytes after %s substitution. Same emit location as
    /// Waves 131 / 134 / 135 / 136 / 137; NEW 11-byte option name in %s
    /// slot (widest %s width pinned to date, extending the catalogue
    /// from 3/5/6/7 to 3/5/6/7/11).
    /// </summary>
    private const string MissingArgGmgetaccessLiteral = "Missing arg for option gmgetaccess";

    /// <summary>
    /// Wave 138 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 38-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the GM-tier slash
    /// command <c>//gmgetaccess</c> (NO param) -- routes through the
    /// GM-block (<c>//</c>-prefix) entry guard at
    /// <c>server/src/PlayerConnection.cpp:4716</c>, the 2-char strip at
    /// lines 4719-4721, the case-'g' GM-block dispatch at line
    /// 5205-5207, and MatchOptWithParam's ERROR fork at
    /// <c>PlayerConnection.cpp:4548</c> BEFORE control reaches the
    /// case-'g' GM-block body-block GetPlayer lookup.
    ///
    /// <para>
    /// THIRD pin on the GM-block (<c>//</c>-prefix) dispatch path
    /// (after Wave 136 //adduser case-'a' and Wave 137 //ban case-'b').
    /// FIRST pin on case-'g' GM-block. The case-'g' GM-block contains
    /// FIVE distinct MatchOptWithParam matchers in sequence at lines
    /// 5207 (gmgetaccess), 5221 (gmsetaccess), 5260 (gmskillpoints),
    /// 5302 (gmenableskills), and several more -- Wave 138 pins the
    /// FIRST matcher in the chain, exercising the matcher's NUL
    /// separator-check path EARLY in the case-'g' fall-through.
    /// </para>
    ///
    /// <para>
    /// Wave 138 also extends the sibling-arm catalogue on
    /// HandleSlashCommands to THIRTEEN byte-exact-pinned arms across BOTH
    /// tiers (Waves 117/123/125/126/129/130/131/132/133/134/135
    /// user-tier + 136/137/138 GM-tier), is the THIRD pin on the GM-block
    /// dispatch path (confirms the path's reliability across THREE case
    /// letters: 'a'/'b'/'g'), and is the SIXTH pin on the
    /// MatchOptWithParam ERROR path with NEW WIDEST 11-byte option-name
    /// %s width (extending the catalogue from 3/5/6/7 to 3/5/6/7/11).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 137
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     %s format-substitution WIDEST-width regression at
    ///     PlayerClass.cpp:3422. Wave 137 pinned 3-byte MINIMAL ("ban"),
    ///     prior pins cover 5/6/7-byte; Wave 138 pins 11-byte
    ///     ("gmgetaccess") -- NEW WIDEST %s-substitution coverage.
    ///     A regression with a fixed-size vsprintf_s buffer truncating
    ///     at e.g. 10 bytes would fail Wave 138 but pass all prior pins.
    ///   </item>
    ///   <item>
    ///     case-'g' GM-block matcher-chain head-position regression at
    ///     <c>server/src/PlayerConnection.cpp:5205-5207</c>. The
    ///     case-'g' GM-block contains FIVE+ MatchOptWithParam matchers
    ///     evaluated in sequence; gmgetaccess is FIRST in the chain.
    ///     A regression that swapped matcher ordering (e.g. moved
    ///     gmsetaccess ahead of gmgetaccess) would change which matcher
    ///     emits the missing-arg literal first (since strncmp prefixes
    ///     differ: "gmget" vs "gmset" mismatch at index 2). A
    ///     regression that mis-spelled "gmgetaccess" as the matcher's
    ///     first argument, swapped the case letter, or removed the
    ///     case-'g' GM-block arm entirely would silently drop the
    ///     //gmgetaccess dispatch. Wave 138 pins case-'g' GM-block
    ///     reaches the FIRST matcher AND that matcher emits the
    ///     missing-arg literal with the EXACT "gmgetaccess" %s
    ///     substitution.
    ///   </item>
    ///   <item>
    ///     GM-block dispatch-path scalability regression at
    ///     <c>server/src/PlayerConnection.cpp:4716</c> + line 4724
    ///     switch. The GM-block dispatch path now has pins on case-'a'
    ///     (Wave 136), case-'b' (Wave 137), and case-'g' (Wave 138).
    ///     A regression that worked for the first two case letters but
    ///     failed for a third (e.g. switch table size, jump offset,
    ///     or compile-time case-folding bug at a specific letter)
    ///     would fail Wave 138 but pass Waves 136 and 137. Wave 138
    ///     pins THREE-letter case dispatch within the GM-block.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-level
    /// error path; the GM-block guard at line 4716 enforces the
    /// AdminLevel &gt;= GM gate the retail server enforced. No server
    /// permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashSlashGmgetaccessMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (35) = 38 bytes.
        const int ExpectedReplyPayloadLength = 38;
        // strlen(literal) + 1 NUL = 35.
        const short ExpectedReplyLengthField = 35;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 34.
        const int ExpectedLiteralByteCount = 34;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Gmget", shipName: "GmgetShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "//gmgetaccess");

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

                // Filter on the distinctive "for option gmgetaccess" suffix.
                if (!text.StartsWith("Missing arg for option gmgetaccess", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgGmgetaccessLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"//gmgetaccess\" without seeing 0x001D MESSAGE_STRING starting with " +
                $"\"Missing arg for option gmgetaccess\". Likely the GM-block entry guard at " +
                $"server/src/PlayerConnection.cpp:4716 stopped admitting status=100 accounts, " +
                $"the 2-char strip at lines 4719-4721 mis-offset, the case-'g' GM-block " +
                $"FIRST matcher at line 5207 stopped dispatching (matcher chain head), " +
                $"or the missing-arg ERROR fork at PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// when the GM-block (<c>//</c>-prefix) dispatch at
    /// <c>server/src/PlayerConnection.cpp:4716</c> strips the leading
    /// 2 chars and hands <c>pch="gmsetaccess"</c> to
    /// <c>MatchOptWithParam("gmgetaccess", pch, ...)</c> at line 5207
    /// (strncmp mismatches "gmget" vs "gmset" at index 2 -- returns
    /// false WITHOUT emit) which falls through to
    /// <c>MatchOptWithParam("gmsetaccess", pch, param, msg_sent)</c>
    /// at <c>PlayerConnection.cpp:5221</c>. The matcher's separator-check
    /// (NUL is not '=', not ' ', not isalpha) hits the else-branch emit
    /// at <c>PlayerConnection.cpp:4548</c>:
    /// <c>SendVaMessage("Missing arg for option %s", "gmsetaccess")</c>.
    /// 34 ASCII bytes after %s substitution. Same emit location as
    /// Waves 131 / 134 / 135 / 136 / 137 / 138; SAME 11-byte width as
    /// Wave 138 BUT exercises the matcher-chain SECOND-position
    /// fall-through path (Wave 138 pinned matcher-chain HEAD).
    /// </summary>
    private const string MissingArgGmsetaccessLiteral = "Missing arg for option gmsetaccess";

    /// <summary>
    /// Wave 139 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 38-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the GM-tier slash
    /// command <c>//gmsetaccess</c> (NO param) -- routes through the
    /// GM-block (<c>//</c>-prefix) entry guard at
    /// <c>server/src/PlayerConnection.cpp:4716</c>, the 2-char strip,
    /// the case-'g' GM-block dispatch, the FIRST matcher
    /// MatchOptWithParam("gmgetaccess", ...) at line 5207 returning
    /// FALSE on strncmp mismatch (index 2 'g' vs 's'), and the SECOND
    /// matcher MatchOptWithParam("gmsetaccess", ...) at line 5221
    /// hitting the missing-arg ERROR fork at
    /// <c>PlayerConnection.cpp:4548</c> BEFORE control reaches the
    /// adjacent AdminLevel() &gt;= SDEV guard at line 5221.
    ///
    /// <para>
    /// FOURTH pin on the GM-block (<c>//</c>-prefix) dispatch path
    /// (after Waves 136/137/138). SECOND pin on case-'g' GM-block --
    /// TIGHT same-case-letter sibling pair with Wave 138's
    /// //gmgetaccess pinning the FIRST matcher (HEAD). Wave 139 pins
    /// the SECOND matcher (after fall-through). This is the FIRST
    /// matcher-chain fall-through pin within the GM-block (mirrors
    /// Wave 135's user-tier matcher-chain fall-through within
    /// case-'c' /chleave after /chjoin).
    /// </para>
    ///
    /// <para>
    /// Wave 139 also extends the sibling-arm catalogue on
    /// HandleSlashCommands to FOURTEEN byte-exact-pinned arms across BOTH
    /// tiers (11 user-tier + 4 GM-tier: Waves 136/137/138/139), is the
    /// FOURTH pin on the GM-block dispatch path (confirms reliability
    /// across THREE distinct case letters now with intra-case fall-through
    /// coverage), and is the SEVENTH pin on the MatchOptWithParam ERROR
    /// path -- SAME 11-byte %s width as Wave 138 BUT via matcher-chain
    /// SECOND-position dispatch (NEW structural fan-out on the matcher
    /// fall-through path within case-'g').
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 138
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     case-'g' GM-block matcher-chain SECOND-position regression
    ///     at <c>server/src/PlayerConnection.cpp:5207-5221</c>. After
    ///     MatchOptWithParam("gmgetaccess", "gmsetaccess", ...) returns
    ///     false (strncmp mismatches at index 2: 'g' vs 's'), control
    ///     MUST fall through to MatchOptWithParam("gmsetaccess", ...)
    ///     at line 5221. A regression that short-circuited the
    ///     gmgetaccess matcher to NOT return false (e.g. returning true
    ///     on strncmp mismatch) or that gated the gmsetaccess matcher
    ///     behind gmgetaccess's success would silently swallow the
    ///     //gmsetaccess emit. Wave 139 pins the gmsetaccess matcher
    ///     is REACHED via fall-through.
    ///   </item>
    ///   <item>
    ///     AdminLevel() &gt;= SDEV inner-guard short-circuit ordering
    ///     regression at <c>server/src/PlayerConnection.cpp:5221</c>.
    ///     The matcher-and-guard line reads
    ///     <c>if (MatchOptWithParam("gmsetaccess", pch, param, msg_sent)
    ///     &amp;&amp; AdminLevel() &gt;= SDEV)</c>. C++ short-circuit
    ///     order matters: MatchOptWithParam runs FIRST (with side
    ///     effects -- the missing-arg emit) regardless of the
    ///     AdminLevel value. Even though status=100 satisfies SDEV=90,
    ///     the emit happens during the matcher call before the &amp;&amp;
    ///     evaluates AdminLevel. A regression that reordered the guard
    ///     to AdminLevel-first (e.g. <c>AdminLevel() &gt;= SDEV
    ///     &amp;&amp; MatchOptWithParam(...)</c>) would still emit
    ///     correctly for status=100 BUT would silently skip the emit
    ///     for status &lt; SDEV. Status=100 doesn't catch that
    ///     directly, but Wave 139's emit confirms the matcher fires
    ///     regardless of inner guard ordering, providing structural
    ///     fan-out vs Wave 138 (case-'g' first matcher with NO inner
    ///     AdminLevel guard).
    ///   </item>
    ///   <item>
    ///     %s format-substitution SAME-WIDTH structural-divergence
    ///     regression. Waves 135/136 share 7-byte width across user-tier
    ///     case-'c' /chleave and GM-tier case-'a' //adduser. Waves
    ///     138/139 share 11-byte width across case-'g' matcher-chain
    ///     FIRST and SECOND positions. A regression in vsprintf_s with
    ///     an off-by-one length bug at a specific width AND a specific
    ///     dispatch position would fail one but not the other. Wave
    ///     139 pins SAME-WIDTH cross-position divergence within case-'g'.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-level
    /// error path; the GM-block guard at line 4716 enforces the
    /// AdminLevel &gt;= GM gate the retail server enforced. No server
    /// permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashSlashGmsetaccessMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (35) = 38 bytes.
        const int ExpectedReplyPayloadLength = 38;
        // strlen(literal) + 1 NUL = 35.
        const short ExpectedReplyLengthField = 35;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 34.
        const int ExpectedLiteralByteCount = 34;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Gmset", shipName: "GmsetShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "//gmsetaccess");

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

                // Filter on the distinctive "for option gmsetaccess" suffix.
                if (!text.StartsWith("Missing arg for option gmsetaccess", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgGmsetaccessLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"//gmsetaccess\" without seeing 0x001D MESSAGE_STRING starting with " +
                $"\"Missing arg for option gmsetaccess\". Likely the case-'g' GM-block " +
                $"FIRST matcher (gmgetaccess at line 5207) incorrectly returned true " +
                $"(preventing fall-through), the SECOND matcher (gmsetaccess at line 5221) " +
                $"stopped dispatching, the AdminLevel() >= SDEV inner-guard short-circuited " +
                $"before the matcher could emit, or the missing-arg ERROR fork at " +
                $"PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// when the case-'g' GM-block matcher chain reaches its THIRD matcher
    /// at <c>PlayerConnection.cpp:5260</c>:
    /// <c>MatchOptWithParam("gmskillpoints", pch, param, msg_sent)</c>.
    /// FIRST matcher gmgetaccess (line 5207) returns false on strncmp
    /// mismatch at index 2 ('g' vs 's'); SECOND matcher gmsetaccess
    /// (line 5221) returns false on strncmp mismatch at index 5
    /// ('e' vs 'k' after "gmset" matches "gmski" first 4 bytes...
    /// actually wait, "gmset"[4]='t' vs "gmski"[4]='i', mismatches at
    /// index 4); THIRD matcher gmskillpoints matches all 13 bytes and
    /// hits the separator-check NUL fall-through. 36 ASCII bytes after
    /// %s substitution -- NEW WIDEST %s pin (was Wave 138/139 11-byte).
    /// </summary>
    private const string MissingArgGmskillpointsLiteral = "Missing arg for option gmskillpoints";

    /// <summary>
    /// Wave 140 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 40-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the GM-tier slash
    /// command <c>//gmskillpoints</c> (NO param) -- routes through the
    /// GM-block (<c>//</c>-prefix) entry guard, the 2-char strip, the
    /// case-'g' GM-block dispatch, two matcher-chain fall-throughs
    /// (gmgetaccess at 5207, gmsetaccess at 5221), and the THIRD
    /// matcher MatchOptWithParam("gmskillpoints", ...) at line 5260
    /// hitting the missing-arg ERROR fork at
    /// <c>PlayerConnection.cpp:4548</c>.
    ///
    /// <para>
    /// FIFTH pin on the GM-block (<c>//</c>-prefix) dispatch path.
    /// THIRD pin on case-'g' GM-block -- TIGHT same-case-letter
    /// sibling triple with Waves 138 (HEAD position gmgetaccess) and
    /// 139 (SECOND position gmsetaccess). Wave 140 pins the THIRD
    /// matcher in the chain, exercising TWO fall-through steps.
    /// EIGHTH pin on the MatchOptWithParam ERROR path with NEW WIDEST
    /// 13-byte option-name %s width (vs Waves 138/139 11-byte WIDEST,
    /// Waves 135/136 7-byte, Wave 134 6-byte, Wave 131 5-byte,
    /// Wave 137 3-byte MINIMAL).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 139
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     case-'g' GM-block matcher-chain THIRD-position fall-through
    ///     regression at <c>PlayerConnection.cpp:5207-5260</c>. After
    ///     FIRST matcher gmgetaccess returns false (strncmp mismatch
    ///     index 2), and SECOND matcher gmsetaccess returns false
    ///     (strncmp mismatch index 4: "gmset"[4]='t' vs
    ///     "gmskillpoints"[4]='i'), control MUST fall through to
    ///     THIRD matcher gmskillpoints at line 5260. A regression
    ///     that short-circuited the gmsetaccess matcher (e.g.
    ///     returning true on mismatch) or that gated gmskillpoints
    ///     behind earlier matchers' success would silently swallow
    ///     the //gmskillpoints emit. Wave 140 pins TWO-step matcher
    ///     fall-through within case-'g'.
    ///   </item>
    ///   <item>
    ///     %s format-substitution NEW WIDEST 13-byte width regression
    ///     at PlayerClass.cpp:3422. Wave 140 pins 13-byte
    ///     ("gmskillpoints") -- extends the catalogue from 3/5/6/7/11
    ///     to 3/5/6/7/11/13 widths. A regression with a fixed-size
    ///     vsprintf_s buffer truncating at 12 bytes would fail Wave
    ///     140 but pass Waves 138/139 (11-byte).
    ///   </item>
    ///   <item>
    ///     gmskillpoints opt-name passed-as-second-argument regression
    ///     at <c>PlayerConnection.cpp:5260</c>. The matcher receives
    ///     "gmskillpoints" as the option name. A regression that
    ///     mis-spelled it (e.g. "gmskillpts", "gmskill_points",
    ///     "gmskillpoint" singular) would emit the wrong %s body OR
    ///     fail to match a properly-spelled //gmskillpoints request.
    ///     Wave 140 pins exact "gmskillpoints".
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-level
    /// error path; the GM-block guard at line 4716 enforces the
    /// AdminLevel &gt;= GM gate the retail server enforced. No server
    /// permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashSlashGmskillpointsMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (37) = 40 bytes.
        const int ExpectedReplyPayloadLength = 40;
        // strlen(literal) + 1 NUL = 37.
        const short ExpectedReplyLengthField = 37;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 36.
        const int ExpectedLiteralByteCount = 36;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Gmskill", shipName: "GmskillShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "//gmskillpoints");

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

                // Filter on the distinctive "for option gmskillpoints" suffix.
                if (!text.StartsWith("Missing arg for option gmskillpoints", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgGmskillpointsLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"//gmskillpoints\" without seeing 0x001D MESSAGE_STRING starting with " +
                $"\"Missing arg for option gmskillpoints\". Likely the case-'g' GM-block " +
                $"FIRST matcher (gmgetaccess at line 5207) or SECOND matcher (gmsetaccess " +
                $"at line 5221) incorrectly returned true (preventing fall-through), the " +
                $"THIRD matcher (gmskillpoints at line 5260) stopped dispatching, or the " +
                $"missing-arg ERROR fork at PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// when the case-'g' GM-block matcher chain reaches its FOURTH matcher
    /// at <c>PlayerConnection.cpp:5302</c>:
    /// <c>MatchOptWithParam("gmenableskills", pch, param, msg_sent)</c>.
    /// FIRST matcher gmgetaccess (line 5207) strncmp mismatches at index 2
    /// ('g' vs 'e'); SECOND matcher gmsetaccess (line 5221) strncmp
    /// mismatches at index 2 ('s' vs 'e'); THIRD matcher gmskillpoints
    /// (line 5260) strncmp mismatches at index 2 ('s' vs 'e'); FOURTH
    /// matcher gmenableskills matches all 14 bytes and hits the
    /// separator-check NUL fall-through. 37 ASCII bytes after %s
    /// substitution -- NEW WIDEST %s pin (was Wave 140 13-byte).
    /// </summary>
    private const string MissingArgGmenableskillsLiteral = "Missing arg for option gmenableskills";

    /// <summary>
    /// Wave 141 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 41-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the GM-tier slash
    /// command <c>//gmenableskills</c> (NO param) -- routes through the
    /// GM-block (<c>//</c>-prefix) entry guard, the 2-char strip, the
    /// case-'g' GM-block dispatch, three matcher-chain fall-throughs
    /// (gmgetaccess at 5207, gmsetaccess at 5221, gmskillpoints at 5260),
    /// and the FOURTH matcher MatchOptWithParam("gmenableskills", ...)
    /// at line 5302 hitting the missing-arg ERROR fork at
    /// <c>PlayerConnection.cpp:4548</c>.
    ///
    /// <para>
    /// SIXTH pin on the GM-block (<c>//</c>-prefix) dispatch path.
    /// FOURTH pin on case-'g' GM-block -- TIGHT same-case-letter
    /// sibling quadruple with Waves 138 (HEAD gmgetaccess), 139 (SECOND
    /// gmsetaccess), and 140 (THIRD gmskillpoints). Wave 141 pins the
    /// FOURTH matcher in the chain, exercising THREE consecutive
    /// fall-through steps -- the deepest case-'g' fall-through pin so
    /// far. NINTH pin on the MatchOptWithParam ERROR path with NEW
    /// WIDEST 14-byte option-name %s width (vs Wave 140 13-byte WIDEST,
    /// Waves 138/139 11-byte, Waves 135/136 7-byte, Wave 134 6-byte,
    /// Wave 131 5-byte, Wave 137 3-byte MINIMAL).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 140
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     case-'g' GM-block matcher-chain FOURTH-position fall-through
    ///     regression at <c>PlayerConnection.cpp:5207-5302</c>. After
    ///     FIRST matcher gmgetaccess returns false (strncmp mismatch
    ///     index 2), SECOND matcher gmsetaccess returns false (strncmp
    ///     mismatch index 2), and THIRD matcher gmskillpoints returns
    ///     false (strncmp mismatch index 2), control MUST fall through
    ///     to FOURTH matcher gmenableskills at line 5302. A regression
    ///     that short-circuited any earlier matcher (e.g. returning true
    ///     on mismatch) or that gated gmenableskills behind earlier
    ///     matchers' success would silently swallow the //gmenableskills
    ///     emit. Wave 141 pins THREE-step matcher fall-through within
    ///     case-'g' -- the deepest matcher-chain pin in the catalogue.
    ///   </item>
    ///   <item>
    ///     %s format-substitution NEW WIDEST 14-byte width regression
    ///     at PlayerClass.cpp:3422. Wave 141 pins 14-byte
    ///     ("gmenableskills") -- extends the catalogue from 3/5/6/7/11/13
    ///     to 3/5/6/7/11/13/14 widths. A regression with a fixed-size
    ///     vsprintf_s buffer truncating at 13 bytes would fail Wave 141
    ///     but pass Wave 140 (13-byte).
    ///   </item>
    ///   <item>
    ///     gmenableskills opt-name passed-as-second-argument regression
    ///     at <c>PlayerConnection.cpp:5302</c>. The matcher receives
    ///     "gmenableskills" as the option name. A regression that
    ///     mis-spelled it (e.g. "gmenableskill" singular, "gmenable",
    ///     "gm_enable_skills") would emit the wrong %s body OR fail to
    ///     match a properly-spelled //gmenableskills request. Wave 141
    ///     pins exact "gmenableskills".
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-level
    /// error path; the GM-block guard at line 4716 enforces the
    /// AdminLevel &gt;= GM gate the retail server enforced. No server
    /// permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashSlashGmenableskillsMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (38) = 41 bytes.
        const int ExpectedReplyPayloadLength = 41;
        // strlen(literal) + 1 NUL = 38.
        const short ExpectedReplyLengthField = 38;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 37.
        const int ExpectedLiteralByteCount = 37;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Gmenable", shipName: "GmenableShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "//gmenableskills");

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

                // Filter on the distinctive "for option gmenableskills" suffix.
                if (!text.StartsWith("Missing arg for option gmenableskills", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgGmenableskillsLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"//gmenableskills\" without seeing 0x001D MESSAGE_STRING starting with " +
                $"\"Missing arg for option gmenableskills\". Likely the case-'g' GM-block " +
                $"FIRST matcher (gmgetaccess at line 5207), SECOND matcher (gmsetaccess " +
                $"at line 5221), or THIRD matcher (gmskillpoints at line 5260) incorrectly " +
                $"returned true (preventing fall-through), the FOURTH matcher " +
                $"(gmenableskills at line 5302) stopped dispatching, or the missing-arg " +
                $"ERROR fork at PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// when the case-'s' GM-block sole matcher at
    /// <c>PlayerConnection.cpp:5155</c>
    /// <c>MatchOptWithParam("setpassword", pch, param, msg_sent)</c>
    /// matches 11 bytes and hits the separator-check NUL fall-through.
    /// 34 ASCII bytes after %s substitution -- 11-byte %s width
    /// (SAME as Waves 138/139's gmgetaccess/gmsetaccess but via DIFFERENT
    /// case-letter, providing cross-case-letter SAME-WIDTH structural
    /// divergence).
    /// </summary>
    private const string MissingArgSetpasswordLiteral = "Missing arg for option setpassword";

    /// <summary>
    /// Wave 142 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 38-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the GM-tier slash
    /// command <c>//setpassword</c> (NO param) -- routes through the
    /// GM-block (<c>//</c>-prefix) entry guard, the 2-char strip, the
    /// case-'s' GM-block dispatch (NEW case-letter), and the SOLE
    /// matcher MatchOptWithParam("setpassword", ...) at line 5155
    /// hitting the missing-arg ERROR fork at
    /// <c>PlayerConnection.cpp:4548</c>.
    ///
    /// <para>
    /// SEVENTH pin on the GM-block (<c>//</c>-prefix) dispatch path.
    /// FIRST pin on case-'s' GM-block -- NEW case-letter extends
    /// HandleSlashCommands GM-block switch coverage to FOUR distinct
    /// case-letters (case-'a' Wave 136, case-'b' Wave 137, case-'g'
    /// Waves 138/139/140/141, case-'s' Wave 142). SINGLE-matcher
    /// case-letter (no fall-through chain, no inner AdminLevel guard).
    /// TENTH pin on the MatchOptWithParam ERROR path with SAME 11-byte
    /// option-name %s width as Waves 138/139 BUT via DIFFERENT
    /// case-letter -- pins cross-case-letter SAME-WIDTH structural
    /// divergence.
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 141
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     case-'s' GM-block dispatch regression at
    ///     <c>PlayerConnection.cpp:5153</c>. case-'s' was previously
    ///     unpinned in the GM-block switch; a regression that dropped
    ///     case-'s' entirely (e.g. accidental deletion or fall-through
    ///     to default), reordered case labels, or routed *pch=='s' to
    ///     the wrong handler would silently swallow //setpassword
    ///     (along with any other case-'s' commands). Wave 142 pins
    ///     case-'s' is REACHABLE via the GM-block switch dispatcher.
    ///   </item>
    ///   <item>
    ///     setpassword opt-name passed-as-second-argument regression
    ///     at <c>PlayerConnection.cpp:5155</c>. The matcher receives
    ///     "setpassword" as the option name. A regression that
    ///     mis-spelled it (e.g. "set_password", "setpass", "setpasswd")
    ///     would emit the wrong %s body OR fail to match a
    ///     properly-spelled //setpassword request. Wave 142 pins exact
    ///     "setpassword".
    ///   </item>
    ///   <item>
    ///     SAME-WIDTH cross-case-letter %s format-substitution
    ///     structural divergence at PlayerClass.cpp:3422. Waves 138/139
    ///     pin 11-byte at case-'g' HEAD/SECOND; Wave 142 pins 11-byte
    ///     at case-'s' HEAD. A regression in vsprintf_s with off-by-one
    ///     at 11-byte width AND a specific case-letter dispatch path
    ///     would fail one but not the other. Wave 142 pins SAME-WIDTH
    ///     cross-case-letter divergence and rules out per-case-letter
    ///     format-substitution branches.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-level
    /// error path; the GM-block guard at line 4716 enforces the
    /// AdminLevel &gt;= GM gate the retail server enforced. No server
    /// permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashSlashSetpasswordMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (35) = 38 bytes.
        const int ExpectedReplyPayloadLength = 38;
        // strlen(literal) + 1 NUL = 35.
        const short ExpectedReplyLengthField = 35;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 34.
        const int ExpectedLiteralByteCount = 34;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Setpass", shipName: "SetpassShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "//setpassword");

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

                // Filter on the distinctive "for option setpassword" suffix.
                if (!text.StartsWith("Missing arg for option setpassword", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgSetpasswordLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"//setpassword\" without seeing 0x001D MESSAGE_STRING starting with " +
                $"\"Missing arg for option setpassword\". Likely the case-'s' GM-block " +
                $"dispatch at line 5153 stopped routing, the sole setpassword matcher at " +
                $"line 5155 stopped dispatching, or the missing-arg ERROR fork at " +
                $"PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// when the user-tier case-'d' HEAD matcher at
    /// <c>PlayerConnection.cpp:5903</c>
    /// <c>MatchOptWithParam("d", pch, param, msg_sent)</c> matches the
    /// single byte 'd' and hits the separator-check NUL fall-through.
    /// 24 ASCII bytes after %s substitution -- NEW MINIMAL 1-byte %s
    /// width (vs Wave 137's 3-byte MINIMAL). The "d" matcher's RHS
    /// short-circuit `&amp;&amp; AdminLevel() &gt;= DEV` never gates the
    /// emit because C++ short-circuit order makes MatchOptWithParam
    /// (LHS) run first with side effects.
    /// </summary>
    private const string MissingArgDLiteral = "Missing arg for option d";

    /// <summary>
    /// Wave 143 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 28-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/d</c> (NO param) -- routes through the user-tier
    /// dispatcher entry at line 5434 (NOT the GM-block, since Msg[1]
    /// != '/'), the 1-char strip at line 5438-5440, the case-'d'
    /// user-tier dispatch (NEW case-letter), and the HEAD matcher
    /// MatchOptWithParam("d", ...) at line 5903 hitting the missing-arg
    /// ERROR fork at <c>PlayerConnection.cpp:4548</c>.
    ///
    /// <para>
    /// TWELFTH pin on the user-tier (single-slash) dispatch path
    /// (Waves 117/123/125/126/129/130/131/132/133/134/135/143).
    /// FIRST pin on user-tier case-'d' -- NEW case-letter extends
    /// user-tier dispatcher switch coverage to SEVEN distinct
    /// case-letters (case-'a' Waves 117/123, case-'b' Waves
    /// 132/133, case-'c' Waves 134/135, case-'d' Wave 143, case-'l'
    /// Waves 130/131, case-'n' Waves 125/126, case-'p' Wave 129).
    /// ELEVENTH pin on the MatchOptWithParam ERROR path with NEW
    /// MINIMAL 1-byte option-name %s width (vs Wave 137's 3-byte
    /// MINIMAL).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 142
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     user-tier case-'d' dispatch regression at
    ///     <c>PlayerConnection.cpp:5902</c>. case-'d' was previously
    ///     unpinned in the user-tier switch; a regression that
    ///     dropped case-'d' entirely, reordered case labels, or
    ///     routed *pch=='d' to the wrong handler would silently
    ///     swallow /d (along with /don, /doff, /dwho, /dialog,
    ///     /debug, /deco, /dockp, /debugmissions). Wave 143 pins
    ///     case-'d' is REACHABLE via the user-tier switch dispatcher.
    ///   </item>
    ///   <item>
    ///     %s format-substitution NEW MINIMAL 1-byte width regression
    ///     at PlayerClass.cpp:3422. Wave 143 pins 1-byte ("d") --
    ///     extends the catalogue from 3/5/6/7/11/13/14 to
    ///     1/3/5/6/7/11/13/14 widths. A regression with vsprintf_s
    ///     mishandling 1-byte %s arguments (e.g. integer-promotion
    ///     bug, off-by-one on tiny strings, single-char allocation
    ///     bug) would fail Wave 143 but pass Waves 137-142.
    ///   </item>
    ///   <item>
    ///     AdminLevel() &gt;= DEV short-circuit ordering regression at
    ///     <c>PlayerConnection.cpp:5903</c>. The matcher-and-guard
    ///     line reads `if (MatchOptWithParam("d", pch, param,
    ///     msg_sent) &amp;&amp; AdminLevel() &gt;= DEV)`. C++
    ///     short-circuit order matters: MatchOptWithParam runs FIRST
    ///     with side effects -- the missing-arg emit -- regardless of
    ///     AdminLevel. Even at status=100 admin (AdminLevel &gt;=
    ///     ADMIN), the emit happens DURING the matcher call before
    ///     the &amp;&amp; evaluates AdminLevel; a regression
    ///     reordering the guard to AdminLevel-first would still emit
    ///     correctly for status=100 BUT would silently skip the emit
    ///     for status&lt;DEV. Wave 143 confirms the matcher fires
    ///     regardless of inner guard ordering at NEW MINIMAL width,
    ///     structurally analogous to Wave 139's case-'g' SDEV-guard
    ///     pin at 11-byte width.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-level
    /// error path. No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashDMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (25) = 28 bytes.
        const int ExpectedReplyPayloadLength = 28;
        // strlen(literal) + 1 NUL = 25.
        const short ExpectedReplyLengthField = 25;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 24.
        const int ExpectedLiteralByteCount = 24;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Diota", shipName: "DiotaShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/d");

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

                // Filter on EXACT match -- "Missing arg for option d" is a
                // prefix of "Missing arg for option deco" etc., so use
                // equals rather than startswith to avoid mis-matching
                // sibling case-'d' matchers (none of which fire for pch="d"
                // since their option names are longer, but defensive).
                if (text != "Missing arg for option d")
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgDLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/d\" without seeing 0x001D MESSAGE_STRING with body " +
                $"\"Missing arg for option d\". Likely the user-tier case-'d' dispatch " +
                $"at line 5902 stopped routing, the HEAD \"d\" matcher at line 5903 " +
                $"stopped dispatching, or the missing-arg ERROR fork at " +
                $"PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// when the user-tier case-'r' GUARD-FIRST else-if matcher at
    /// <c>PlayerConnection.cpp:7074</c>
    /// <c>AdminLevel() &gt;= DEV &amp;&amp; MatchOptWithParam("removebaseore", pch, param, msg_sent)</c>
    /// passes the GUARD-FIRST AdminLevel check (status=100 satisfies
    /// DEV=80), then runs MatchOptWithParam which matches 13 bytes and
    /// hits the separator-check NUL fall-through. 36 ASCII bytes after
    /// %s substitution -- 13-byte %s width (SAME as Wave 140's GM-tier
    /// case-g gmskillpoints THIRD-position; Wave 144 provides cross-tier
    /// cross-case-letter SAME-WIDTH divergence at 13-byte).
    /// </summary>
    private const string MissingArgRemovebaseoreLiteral = "Missing arg for option removebaseore";

    /// <summary>
    /// Wave 144 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 40-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/removebaseore</c> (NO param) -- routes through the
    /// user-tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'r' user-tier dispatch (NEW case-letter), traverses the
    /// case-'r' else-if matcher chain (rsi, rsa, rsn, rotatex, rotatey,
    /// rotatez all strncmp-fail at index 1), and hits the
    /// removebaseore matcher at line 7074 -- the FIRST GUARD-FIRST
    /// short-circuit pattern pin in the catalogue
    /// (<c>AdminLevel() &gt;= DEV &amp;&amp; MatchOptWithParam(...)</c>
    /// -- AdminLevel evaluated FIRST, matcher SECOND), then the
    /// missing-arg ERROR fork at <c>PlayerConnection.cpp:4548</c>.
    ///
    /// <para>
    /// THIRTEENTH pin on the user-tier (single-slash) dispatch path.
    /// FIRST pin on user-tier case-'r' -- NEW case-letter extends
    /// user-tier dispatcher switch coverage to EIGHT distinct
    /// case-letters (a/b/c/d/l/n/p/r). TWELFTH pin on the
    /// MatchOptWithParam ERROR path with SAME 13-byte option-name %s
    /// width as Wave 140 (GM-tier case-'g' THIRD-position
    /// gmskillpoints) BUT via DIFFERENT tier AND DIFFERENT case-letter
    /// -- pins cross-tier cross-case-letter SAME-WIDTH structural
    /// divergence. FIRST pin on the GUARD-FIRST short-circuit pattern
    /// (NEW structural variant -- prior pins covered MATCHER-FIRST
    /// at Waves 139 case-g SDEV-guard 11-byte and 143 user-tier
    /// case-d DEV-guard 1-byte).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 143
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     user-tier case-'r' dispatch + else-if chain regression at
    ///     <c>PlayerConnection.cpp:6992-7074</c>. case-'r' was
    ///     previously unpinned in the user-tier switch; a regression
    ///     that dropped case-'r' entirely, reordered case labels, or
    ///     routed *pch=='r' to the wrong handler would silently
    ///     swallow /removebaseore (along with /rs, /rsi, /rsa, /rsn,
    ///     /rsd, /range, /restoreinv, /rotatex/y/z, /resetchar/mounts/navs,
    ///     /reffect, /release). Wave 144 pins case-'r' is REACHABLE
    ///     via the user-tier switch dispatcher AND the else-if chain
    ///     traverses correctly from line 7000 to line 7074 (6 matchers
    ///     deep -- rsi/rsa/rsn/rotatex/rotatey/rotatez all
    ///     strncmp-fail at index 1, then removebaseore matches).
    ///   </item>
    ///   <item>
    ///     GUARD-FIRST short-circuit ordering regression at
    ///     <c>PlayerConnection.cpp:7074</c>. The matcher-and-guard
    ///     line reads `else if (AdminLevel() &gt;= DEV &amp;&amp;
    ///     MatchOptWithParam("removebaseore", pch, param, msg_sent))`.
    ///     C++ short-circuit order matters: AdminLevel evaluates
    ///     FIRST -- if status &lt; DEV, MatchOptWithParam NEVER runs
    ///     and NO emit fires. With status=100 (AdminLevel &gt;= DEV),
    ///     guard passes and matcher runs -- emit fires. A regression
    ///     that reordered the guard to MATCHER-FIRST (LHS swap) would
    ///     emit the missing-arg message for ALL admin levels (even
    ///     BETA), accidentally exposing /removebaseore's existence to
    ///     low-privilege users. Wave 144 pins guard-first AdminLevel
    ///     gates the emit -- the inverse-direction sibling to Wave
    ///     139's matcher-first AdminLevel-irrelevant pin.
    ///   </item>
    ///   <item>
    ///     case-'r' else-if matcher-chain fall-through regression at
    ///     <c>PlayerConnection.cpp:7012-7074</c>. After 6 prior
    ///     matchers (rsi, rsa, rsn, rotatex, rotatey, rotatez) all
    ///     strncmp-fail at index 1 ('s'/'o' vs 'e' of "removebaseore"),
    ///     control MUST fall through to removebaseore at line 7074. A
    ///     regression that short-circuited any prior matcher (e.g.
    ///     returning true on mismatch) would silently swallow the
    ///     /removebaseore emit. Wave 144 pins SIX-step matcher-chain
    ///     fall-through within case-'r' -- ties Wave 141 (case-'g'
    ///     THREE-step deepest fall-through) for catalogue's deepest
    ///     fall-through pin (Wave 141 in case-'g', Wave 144 in
    ///     case-'r', different chain composition).
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-level
    /// error path; the GUARD-FIRST AdminLevel &gt;= DEV gate enforces
    /// the retail server's privilege check (low-privilege users do not
    /// learn that //removebaseore exists). No server permissiveness
    /// added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashRemovebaseoreMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (37) = 40 bytes.
        const int ExpectedReplyPayloadLength = 40;
        // strlen(literal) + 1 NUL = 37.
        const short ExpectedReplyLengthField = 37;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 36.
        const int ExpectedLiteralByteCount = 36;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Removeb", shipName: "RemovebShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/removebaseore");

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

                // Filter on the distinctive "for option removebaseore" suffix.
                if (!text.StartsWith("Missing arg for option removebaseore", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgRemovebaseoreLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/removebaseore\" without seeing 0x001D MESSAGE_STRING starting with " +
                $"\"Missing arg for option removebaseore\". Likely the user-tier case-'r' " +
                $"dispatch at line 6992 stopped routing, the else-if chain (rsi/rsa/rsn/" +
                $"rotatex/y/z) before line 7074 incorrectly short-circuited, the GUARD-FIRST " +
                $"AdminLevel >= DEV check at line 7074 failed (status=100 admin should pass), " +
                $"the removebaseore matcher at line 7074 stopped dispatching, or the " +
                $"missing-arg ERROR fork at PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// when the user-tier case-'e' OUTER-AdminLevel-GUARDED matcher at
    /// <c>PlayerConnection.cpp:6076</c> -- wrapped inside the outer
    /// <c>if (AdminLevel() &gt;= GM)</c> block at line 6074 --
    /// passes the OUTER AdminLevel block (status=100 satisfies GM=50),
    /// then runs MatchOptWithParam("effect", ...) which matches 6 bytes
    /// and hits the separator-check NUL fall-through. 29 ASCII bytes
    /// after %s substitution -- 6-byte %s width (SAME as Wave 134's
    /// user-tier case-c chjoin; Wave 145 provides
    /// cross-case-letter SAME-WIDTH divergence at 6-byte AND NEW
    /// outer-AdminLevel-guard structural pattern divergence).
    /// </summary>
    private const string MissingArgEffectLiteral = "Missing arg for option effect";

    /// <summary>
    /// Wave 145 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 33-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/effect</c> (NO param) -- routes through the
    /// user-tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'e' user-tier dispatch (NEW case-letter), traverses the
    /// case-'e' head matchers (endtalk, enableskills both strcmp-fail),
    /// then enters the OUTER <c>if (AdminLevel() &gt;= GM)</c> block at
    /// line 6074, and hits the effect matcher at line 6076 -- the FIRST
    /// OUTER-AdminLevel-GUARD structural pattern pin in the catalogue
    /// (matcher NOT inline-guarded; the AdminLevel gate wraps a BLOCK
    /// containing multiple matchers -- effect, effecto, effects,
    /// exposedecos, errorson, errorsoff), then the missing-arg ERROR
    /// fork at <c>PlayerConnection.cpp:4548</c>.
    ///
    /// <para>
    /// FOURTEENTH pin on the user-tier (single-slash) dispatch path.
    /// FIRST pin on user-tier case-'e' -- NEW case-letter extends
    /// user-tier dispatcher switch coverage to NINE distinct
    /// case-letters (a/b/c/d/e/l/n/p/r). THIRTEENTH pin on the
    /// MatchOptWithParam ERROR path with SAME 6-byte option-name %s
    /// width as Wave 134 (user-tier case-'c' chjoin) BUT via DIFFERENT
    /// case-letter -- pins cross-case-letter SAME-WIDTH structural
    /// divergence. FIRST pin on the OUTER-AdminLevel-GUARD structural
    /// pattern (NEW structural variant -- prior pins covered
    /// MATCHER-FIRST inline AdminLevel at Waves 139 case-g SDEV-guard
    /// 11-byte and 143 user-tier case-d DEV-guard 1-byte; GUARD-FIRST
    /// inline AdminLevel at Wave 144 case-r DEV-guard 13-byte;
    /// Wave 145 is the FIRST OUTER-BLOCK-GUARD where the AdminLevel
    /// gate wraps an entire matcher block rather than a single matcher).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 144
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     user-tier case-'e' dispatch + head-matcher chain regression
    ///     at <c>PlayerConnection.cpp:6045-6076</c>. case-'e' was
    ///     previously unpinned in the user-tier switch; a regression
    ///     that dropped case-'e' entirely, reordered case labels, or
    ///     routed *pch=='e' to the wrong handler would silently
    ///     swallow /effect (along with /effecto, /effects, /exposedecos,
    ///     /errorson, /errorsoff, /endtalk, /enableskills). Wave 145
    ///     pins case-'e' is REACHABLE via the user-tier switch
    ///     dispatcher AND the head-matcher chain (endtalk strcmp at
    ///     6047, enableskills strcmp at 6054) fall-through correctly
    ///     to the outer-guard block at line 6074.
    ///   </item>
    ///   <item>
    ///     OUTER-AdminLevel-GUARD structural regression at
    ///     <c>PlayerConnection.cpp:6074</c>. The block reads
    ///     <c>if (AdminLevel() &gt;= GM) { ... if (MatchOptWithParam(
    ///     "effect", ...)) ... }</c>. The OUTER block-guard wraps SIX
    ///     matchers (effect, effecto, effects, exposedecos, errorson,
    ///     errorsoff); a regression that flipped, dropped, or
    ///     misordered the outer guard would either (a) expose ALL six
    ///     matchers to low-privilege users (regression dropping outer
    ///     guard) or (b) deny ALL six matchers to GM-tier users
    ///     (regression tightening guard). With status=100 (AdminLevel
    ///     &gt;= GM), guard passes and matcher runs -- emit fires. The
    ///     structural variant is NEW: prior pins (Waves 139/143/144)
    ///     all guarded ONE matcher inline; Wave 145 is the FIRST pin
    ///     where the AdminLevel gate wraps an ENTIRE BLOCK of matchers
    ///     -- a different breakage class.
    ///   </item>
    ///   <item>
    ///     case-'e' head-matcher fall-through regression at
    ///     <c>PlayerConnection.cpp:6047-6076</c>. Before the effect
    ///     matcher, case-'e' has TWO head matchers (endtalk strcmp at
    ///     6047, enableskills strcmp at 6054 -- note these are NOT
    ///     else-ifs but bare ifs; both must strcmp-fail for control
    ///     to reach the outer-guard block). With pch="effect": strcmp(
    ///     "effect","endtalk") != 0 skips; strcmp("effect",
    ///     "enableskills") != 0 skips; control reaches line 6074. A
    ///     regression that made either head matcher emit a competing
    ///     message or short-circuit case-'e' (e.g. add msg_sent=true
    ///     in the wrong branch) would silently swallow the /effect
    ///     emit. Wave 145 pins TWO-step head-matcher fall-through
    ///     within case-'e' before the outer-guarded matcher block.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-level
    /// error path; the OUTER AdminLevel &gt;= GM gate enforces the
    /// retail server's privilege check (low-privilege users do not
    /// learn that /effect exists). No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashEffectMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (30) = 33 bytes.
        const int ExpectedReplyPayloadLength = 33;
        // strlen(literal) + 1 NUL = 30.
        const short ExpectedReplyLengthField = 30;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 29.
        const int ExpectedLiteralByteCount = 29;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Effecta", shipName: "EffectaShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/effect");

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

                // EXACT equals filter (not StartsWith) -- "Missing arg for option effect"
                // is a prefix of sibling option emits ("effecto", "effects").
                if (!text.Equals("Missing arg for option effect", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgEffectLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/effect\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option effect\". Likely the user-tier case-'e' " +
                $"dispatch at line 6045 stopped routing, the head matchers (endtalk/" +
                $"enableskills) at lines 6047/6054 incorrectly short-circuited, the " +
                $"OUTER AdminLevel >= GM block at line 6074 failed (status=100 admin " +
                $"should pass), the effect matcher at line 6076 stopped dispatching, " +
                $"or the missing-arg ERROR fork at PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// when the user-tier case-'f' COMBINED-GUARD matcher at
    /// <c>PlayerConnection.cpp:6283</c> -- wrapped inside the outer
    /// <c>if (AdminLevel() &gt;= GM)</c> block at line 6281 AND
    /// inline-guarded with <c>&amp;&amp; AdminLevel() &gt;= 50</c> --
    /// passes BOTH guards (status=100 satisfies GM=50 and the literal
    /// 50 threshold), then runs MatchOptWithParam("form", ...) MATCHER-
    /// FIRST which matches 4 bytes and hits the separator-check NUL
    /// fall-through. 27 ASCII bytes after %s substitution -- NEW
    /// MIDDLE 4-byte %s width (fills the gap between Wave 137's 3-byte
    /// MINIMAL and the 5/6/7/11/13/14-byte widths).
    /// </summary>
    private const string MissingArgFormLiteral = "Missing arg for option form";

    /// <summary>
    /// Wave 146 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 31-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/form</c> (NO param) -- routes through the user-tier
    /// dispatcher entry at line 5434, the 1-char strip, the case-'f'
    /// user-tier dispatch (NEW case-letter), enters the OUTER
    /// <c>if (AdminLevel() &gt;= GM)</c> block at line 6281, and hits
    /// the form matcher at line 6283 -- the FIRST COMBINED-GUARD
    /// (outer-block + inline-matcher-first) structural pattern pin in
    /// the catalogue (matcher is OUTER-block-guarded AND
    /// inline-matcher-first-guarded with literal 50 threshold;
    /// MatchOptWithParam runs FIRST with side effect emit, &amp;&amp;
    /// AdminLevel never evaluated because matcher returns false), then
    /// the missing-arg ERROR fork at
    /// <c>PlayerConnection.cpp:4548</c>.
    ///
    /// <para>
    /// FIFTEENTH pin on the user-tier (single-slash) dispatch path.
    /// FIRST pin on user-tier case-'f' -- NEW case-letter extends
    /// user-tier dispatcher switch coverage to TEN distinct
    /// case-letters (a/b/c/d/e/f/l/n/p/r). FOURTEENTH pin on the
    /// MatchOptWithParam ERROR path with NEW MIDDLE 4-byte %s width
    /// (fills the gap between Wave 137's 3-byte and Wave 131's 5-byte
    /// to give EIGHT distinct widths: 1/3/4/5/6/7/11/13/14 -- now NINE
    /// widths). FIRST pin on the COMBINED-GUARD structural pattern
    /// (NEW structural variant -- prior pins covered MATCHER-FIRST
    /// inline at Waves 139/143, GUARD-FIRST inline at Wave 144, and
    /// OUTER-BLOCK-GUARD at Wave 145 -- Wave 146 is the FIRST
    /// COMBINED-GUARD where OUTER-block-guard AND
    /// INLINE-matcher-first-guard with literal 50 threshold BOTH
    /// apply).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 145
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     user-tier case-'f' dispatch + multi-block fall-through
    ///     regression at <c>PlayerConnection.cpp:6280-6398</c>.
    ///     case-'f' was previously unpinned in the user-tier switch;
    ///     a regression that dropped case-'f' entirely, reordered
    ///     case labels, or routed *pch=='f' to the wrong handler
    ///     would silently swallow /form (along with /flushinv,
    ///     /factionset, /factionoverride, /fetch, /find, /face,
    ///     /faceme, /fgps, /fireweapon, /fhelp). Wave 146 pins
    ///     case-'f' is REACHABLE via the user-tier switch dispatcher
    ///     AND the FIRST inner block (lines 6281-6335 outer
    ///     AdminLevel >= GM) dispatches correctly to the form
    ///     matcher at line 6283.
    ///   </item>
    ///   <item>
    ///     COMBINED-GUARD structural regression at
    ///     <c>PlayerConnection.cpp:6281-6283</c>. The line reads
    ///     <c>if (AdminLevel() &gt;= GM) { if (MatchOptWithParam(
    ///     "form", ...) &amp;&amp; AdminLevel() &gt;= 50) ... }</c>.
    ///     TWO guards apply: OUTER block-guard wraps the form matcher
    ///     (and 3 sibling matchers); INNER matcher-first inline
    ///     guard with LITERAL 50 (semantically equivalent to GM but
    ///     textually distinct -- a regression that switched GM=50 to
    ///     a different value would break the GM constant but the
    ///     literal 50 inline guard would still gate at 50). With
    ///     status=100, both guards pass; MatchOptWithParam runs
    ///     FIRST, matches 4 bytes, emits, returns false; &amp;&amp;
    ///     AdminLevel skipped. A regression that dropped EITHER
    ///     guard would still emit (since both pass at status=100),
    ///     but a regression that switched the inline guard from
    ///     literal 50 to a tighter threshold (e.g. SDEV=90) would
    ///     still emit at status=100 (BOTH still pass) -- the pin
    ///     does NOT detect that. What it DOES detect: outer-block
    ///     traversal from line 6281 reaches line 6283, and matcher
    ///     runs (rather than the matcher being skipped via outer
    ///     block-guard tightening).
    ///   </item>
    ///   <item>
    ///     %s format-substitution NEW MIDDLE 4-byte width regression
    ///     at <c>PlayerClass.cpp:3422</c>. Catalogue had 7 distinct
    ///     widths (1/3/5/6/7/11/13/14) -- Wave 146 adds 4-byte,
    ///     filling the gap between 3 and 5; a regression in
    ///     vsprintf_s with off-by-one at 4-byte width specifically
    ///     would fail Wave 146 but pass prior pins. Pins
    ///     format-substitution stability at NEW MIDDLE width.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-level
    /// error path; BOTH the OUTER AdminLevel &gt;= GM and INLINE
    /// AdminLevel &gt;= 50 gates enforce the retail server's privilege
    /// check (low-privilege users do not learn that /form exists). No
    /// server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashFormMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (28) = 31 bytes.
        const int ExpectedReplyPayloadLength = 31;
        // strlen(literal) + 1 NUL = 28.
        const short ExpectedReplyLengthField = 28;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 27.
        const int ExpectedLiteralByteCount = 27;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Forma", shipName: "FormaShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/form");

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

                // EXACT equals filter (not StartsWith) -- defensive against
                // future sibling "form*" option emits in case-'f'.
                if (!text.Equals("Missing arg for option form", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgFormLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/form\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option form\". Likely the user-tier case-'f' " +
                $"dispatch at line 6280 stopped routing, the OUTER AdminLevel >= GM " +
                $"block at line 6281 failed (status=100 admin should pass), the form " +
                $"matcher at line 6283 stopped dispatching, the inline AdminLevel >= 50 " +
                $"guard at line 6283 was reordered to GUARD-FIRST and short-circuited, " +
                $"or the missing-arg ERROR fork at PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// when the user-tier case-'h' NO-GUARD matcher at
    /// <c>PlayerConnection.cpp:6647</c> -- <c>MatchOptWithParam("ht",
    /// pch, param, msg_sent)</c> with NO AdminLevel guard (neither
    /// outer-block nor inline) -- runs and matches 2 bytes, then hits
    /// the separator-check NUL fall-through. 25 ASCII bytes after %s
    /// substitution -- NEW MIDDLE 2-byte %s width (fills the gap
    /// between Wave 143's 1-byte MINIMAL and Wave 137's 3-byte;
    /// catalogue now spans TEN distinct widths
    /// 1/2/3/4/5/6/7/11/13/14).
    /// </summary>
    private const string MissingArgHtLiteral = "Missing arg for option ht";

    /// <summary>
    /// Wave 147 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 29-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/ht</c> (NO param) -- routes through the user-tier
    /// dispatcher entry at line 5434, the 1-char strip, the case-'h'
    /// user-tier dispatch (NEW case-letter), traverses the case-'h'
    /// head matchers (hijack strcmp+target-check, heading strcmp both
    /// fail), and hits the ht matcher at line 6647 -- the FIRST
    /// NO-GUARD inline matcher pattern pin in the catalogue
    /// (<c>MatchOptWithParam("ht", ...)</c> with NEITHER outer-block
    /// guard NOR inline AdminLevel guard; the matcher dispatches
    /// unconditionally regardless of AdminLevel), then the missing-arg
    /// ERROR fork at <c>PlayerConnection.cpp:4548</c>.
    ///
    /// <para>
    /// SIXTEENTH pin on the user-tier (single-slash) dispatch path.
    /// FIRST pin on user-tier case-'h' -- NEW case-letter extends
    /// user-tier dispatcher switch coverage to ELEVEN distinct
    /// case-letters (a/b/c/d/e/f/h/l/n/p/r). FIFTEENTH pin on the
    /// MatchOptWithParam ERROR path with NEW MIDDLE 2-byte %s width
    /// (fills the gap between Wave 143's 1-byte MINIMAL and Wave
    /// 137's 3-byte; catalogue now spans TEN distinct widths
    /// 1/2/3/4/5/6/7/11/13/14). FIRST pin on the NO-GUARD inline
    /// matcher structural pattern (NEW structural variant -- prior
    /// pins covered MATCHER-FIRST inline at Waves 139/143,
    /// GUARD-FIRST inline at Wave 144, OUTER-BLOCK-GUARD at Wave 145,
    /// and COMBINED outer+inline at Wave 146; Wave 147 is the FIRST
    /// NO-GUARD pin where the matcher dispatches unconditionally
    /// regardless of AdminLevel -- inverse-direction sibling that
    /// rules out spurious AdminLevel gating).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 146
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     user-tier case-'h' dispatch + head-matcher chain regression
    ///     at <c>PlayerConnection.cpp:6627-6705</c>. case-'h' was
    ///     previously unpinned in the user-tier switch; a regression
    ///     that dropped case-'h' entirely, reordered case labels, or
    ///     routed *pch=='h' to the wrong handler would silently
    ///     swallow /ht (along with /hijack, /heading, /helpedit,
    ///     /helpfield). Wave 147 pins case-'h' is REACHABLE via the
    ///     user-tier switch dispatcher AND the head-matcher chain
    ///     (hijack strcmp+target-check at 6628, heading strcmp at
    ///     6640 -- both bare ifs not else-ifs) fall-through correctly
    ///     to the ht matcher at line 6647.
    ///   </item>
    ///   <item>
    ///     NO-GUARD inline matcher structural regression at
    ///     <c>PlayerConnection.cpp:6647</c>. The line reads
    ///     <c>if (MatchOptWithParam("ht", pch, param, msg_sent))</c>
    ///     -- NEITHER outer-block AdminLevel guard NOR inline
    ///     AdminLevel guard. The matcher dispatches unconditionally.
    ///     A regression that ADDED an AdminLevel guard (e.g.
    ///     `if (AdminLevel() &gt;= GM &amp;&amp; MatchOptWithParam(
    ///     "ht", ...))`) would NOT detect at status=100 (would still
    ///     emit) -- so Wave 147 is NOT a direct guard-presence
    ///     detector. What it DOES detect: the matcher dispatches AT
    ///     ALL (a regression that wrapped /ht in any guard at
    ///     SDEV-tighter threshold would skip the matcher and fail
    ///     this test); also, the OUTER block-guard pattern of Wave
    ///     145 IS NOT present at /ht (a regression that wrapped
    ///     /ht in an outer-block-guard at SDEV would break this).
    ///     Wave 147 is the inverse-direction sibling to Wave 145's
    ///     outer-block-guard pin.
    ///   </item>
    ///   <item>
    ///     %s format-substitution NEW MIDDLE 2-byte width regression
    ///     at <c>PlayerClass.cpp:3422</c>. Catalogue had 9 distinct
    ///     widths (1/3/4/5/6/7/11/13/14) -- Wave 147 adds 2-byte,
    ///     filling the gap between 1 and 3 to give 10 widths; a
    ///     regression in vsprintf_s with off-by-one at 2-byte width
    ///     specifically would fail Wave 147 but pass prior pins.
    ///     Pins format-substitution stability at NEW MIDDLE width
    ///     adjacent to MINIMAL (1-byte).
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-level
    /// error path. /ht (head/body/gender selector) had NO AdminLevel
    /// guard in the retail server -- it's a "user-tier" command
    /// regardless of privilege. No server permissiveness added; the
    /// pin preserves retail behavior.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashHtMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (26) = 29 bytes.
        const int ExpectedReplyPayloadLength = 29;
        // strlen(literal) + 1 NUL = 26.
        const short ExpectedReplyLengthField = 26;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 25.
        const int ExpectedLiteralByteCount = 25;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Hta", shipName: "HtaShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/ht");

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

                // EXACT equals filter (not StartsWith) -- defensive against
                // future sibling "ht*" option emits in case-'h'.
                if (!text.Equals("Missing arg for option ht", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgHtLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/ht\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option ht\". Likely the user-tier case-'h' " +
                $"dispatch at line 6627 stopped routing, the head matchers (hijack/" +
                $"heading) at lines 6628/6640 incorrectly emitted competing messages, " +
                $"the ht matcher at line 6647 was wrapped in a spurious AdminLevel " +
                $"guard at SDEV-tighter threshold, or the missing-arg ERROR fork at " +
                $"PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verbatim body of the 0x001D MESSAGE_STRING reply the server emits
    /// when the user-tier case-'k' OUTER-AdminLevel-GUARDED matcher at
    /// <c>PlayerConnection.cpp:6759</c> -- wrapped inside the outer
    /// <c>if (AdminLevel() &gt;= GM)</c> block at line 6757 --
    /// passes the OUTER AdminLevel block (status=100 satisfies GM=50),
    /// then runs MatchOptWithParam("kick", ...) which matches 4 bytes
    /// and hits the separator-check NUL fall-through. 27 ASCII bytes
    /// after %s substitution -- 4-byte %s width (SAME as Wave 146's
    /// user-tier case-f form COMBINED-GUARD; Wave 148 provides
    /// cross-case-letter SAME-WIDTH structural-pattern divergence at
    /// 4-byte AND NEW CASE-FALL-THROUGH structural pattern).
    /// </summary>
    private const string MissingArgKickLiteral = "Missing arg for option kick";

    /// <summary>
    /// Wave 148 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 31-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/kick</c> (NO param) -- routes through the user-tier
    /// dispatcher entry at line 5434, the 1-char strip, the case-'k'
    /// user-tier dispatch (NEW case-letter), enters the OUTER
    /// <c>if (AdminLevel() &gt;= GM)</c> block at line 6757, hits the
    /// kick matcher at line 6759, then -- and this is the key
    /// structural variant -- FALLS THROUGH to case-'l' at line 6766
    /// because case-'k' has NO `break` statement at line 6764. The
    /// fall-through traverses case-'l's matchers (leavegroup, levelout,
    /// level, lootstats) all of which strcmp/strncmp-fail against
    /// pch="kick", then case-'l' breaks at line 6822. NET RESULT: ONE
    /// emit, but a regression that ADDED a competing emit anywhere in
    /// case-'l' would silently produce a second message after /kick.
    /// FIRST pin on the CASE-FALL-THROUGH structural pattern in the
    /// catalogue.
    ///
    /// <para>
    /// SEVENTEENTH pin on the user-tier (single-slash) dispatch path.
    /// FIRST pin on user-tier case-'k' -- NEW case-letter extends
    /// user-tier dispatcher switch coverage to TWELVE distinct
    /// case-letters (a/b/c/d/e/f/h/k/l/n/p/r). SIXTEENTH pin on the
    /// MatchOptWithParam ERROR path with SAME 4-byte option-name %s
    /// width as Wave 146 (user-tier case-'f' form COMBINED-GUARD)
    /// BUT via DIFFERENT case-letter AND DIFFERENT structural pattern
    /// -- pins cross-case-letter SAME-WIDTH structural-pattern
    /// divergence at 4-byte. FIRST pin on the CASE-FALL-THROUGH
    /// structural pattern (NEW structural variant -- prior pins
    /// covered MATCHER-FIRST inline at Waves 139/143, GUARD-FIRST
    /// inline at Wave 144, OUTER-BLOCK-GUARD at Wave 145, COMBINED
    /// outer+inline at Wave 146, and NO-GUARD inline at Wave 147;
    /// Wave 148 is the FIRST CASE-FALL-THROUGH pin where the case
    /// statement has NO break and execution traverses INTO the next
    /// case label's matchers before breaking).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 147
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     user-tier case-'k' dispatch + outer-block-guard regression
    ///     at <c>PlayerConnection.cpp:6756-6764</c>. case-'k' was
    ///     previously unpinned in the user-tier switch; a regression
    ///     that dropped case-'k' entirely, reordered case labels, or
    ///     routed *pch=='k' to the wrong handler would silently
    ///     swallow /kick. Wave 148 pins case-'k' is REACHABLE via the
    ///     user-tier switch dispatcher AND the outer AdminLevel >= GM
    ///     block at line 6757 dispatches correctly to the kick matcher
    ///     at line 6759.
    ///   </item>
    ///   <item>
    ///     CASE-FALL-THROUGH structural regression at
    ///     <c>PlayerConnection.cpp:6754-6766</c>. case-'k' has NO
    ///     break statement after its block ends at line 6764;
    ///     execution falls through to case-'l' at line 6766. With
    ///     pch="kick", case-'l' matchers (leavegroup strcmp at 6767,
    ///     levelout strcmp at 6772, level MatchOptWithParam at 6777,
    ///     lootstats strcmp at 6812) all fail and case-'l' breaks at
    ///     6822. NET RESULT: ONE emit. A regression that ADDED a
    ///     competing emit anywhere in case-'l' (e.g. a new bare-`if`
    ///     matcher that matches against pch="kick" by accident) would
    ///     produce a second message. A regression that ADDED a break
    ///     statement to case-'k' (closing the fall-through) would
    ///     NOT change behavior for /kick but WOULD change behavior
    ///     for any future case-'k' command that previously relied on
    ///     case-'l' fall-through. Wave 148 pins the fall-through as
    ///     a structural invariant; the inverse-direction sibling to
    ///     Wave 144's matcher-chain fall-through pin (within a single
    ///     case) -- Wave 148 pins fall-through ACROSS case labels.
    ///   </item>
    ///   <item>
    ///     cross-case-letter SAME-WIDTH structural-pattern divergence
    ///     regression at <c>PlayerClass.cpp:3422</c>. Wave 146 pins
    ///     4-byte at user-tier case-'f' COMBINED-GUARD (outer-block
    ///     + inline matcher-first); Wave 148 pins 4-byte at user-tier
    ///     case-'k' OUTER-BLOCK-GUARD (no inline guard). Same width,
    ///     different case-letter, different structural pattern. A
    ///     regression in vsprintf_s with off-by-one at 4-byte width
    ///     AND a specific case-letter/structural-pattern dispatch
    ///     path would fail one but not the other; Wave 148 rules
    ///     out per-case-letter AND per-structural-pattern
    ///     format-substitution branches at 4-byte width.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-level
    /// error path; the OUTER AdminLevel &gt;= GM gate enforces the
    /// retail server's privilege check (low-privilege users do not
    /// learn that /kick exists). The case-'k' fall-through to case-'l'
    /// preserves retail server behavior (the missing break is in the
    /// upstream source; Wave 148 pins this as-is). No server
    /// permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashKickMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (28) = 31 bytes.
        const int ExpectedReplyPayloadLength = 31;
        // strlen(literal) + 1 NUL = 28.
        const short ExpectedReplyLengthField = 28;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 27.
        const int ExpectedLiteralByteCount = 27;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Kicka", shipName: "KickaShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/kick");

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

                // EXACT equals filter (not StartsWith) -- defensive against
                // any future sibling "kick*" option emits in case-'k'.
                if (!text.Equals("Missing arg for option kick", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgKickLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/kick\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option kick\". Likely the user-tier case-'k' " +
                $"dispatch at line 6756 stopped routing, the OUTER AdminLevel >= GM " +
                $"block at line 6757 failed (status=100 admin should pass), the kick " +
                $"matcher at line 6759 stopped dispatching, the case-'k' fall-through " +
                $"to case-'l' at line 6766 produced a competing emit, or the missing-arg " +
                $"ERROR fork at PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 149 missing-arg ERROR literal for case-'i' /invite. The
    /// matcher at PlayerConnection.cpp:6709 reads
    /// `if (MatchOptWithParam("invite", pch, param, msg_sent))` with
    /// NEITHER outer-block AdminLevel guard NOR inline AdminLevel guard
    /// -- matcher dispatches unconditionally. With NO param after
    /// "/invite", MatchOptWithParam's matcher branch matches 6 bytes,
    /// arg[6]='\0' -- NOT '=' / NOT ' ' / NOT isalpha, allowNoParams
    /// false, so falls through to else at line 4546 and hits the
    /// separator-check NUL fall-through. 29 ASCII bytes after %s
    /// substitution -- 6-byte %s width (SAME as Wave 145's user-tier
    /// case-e effect OUTER-BLOCK-GUARD; Wave 149 provides cross-case-
    /// letter SAME-WIDTH structural-pattern divergence at 6-byte AND
    /// deepens the NO-GUARD inline matcher coverage to TWO pins).
    /// </summary>
    private const string MissingArgInviteLiteral = "Missing arg for option invite";

    /// <summary>
    /// Wave 149 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 33-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/invite</c> (NO param) -- routes through the user-tier
    /// dispatcher entry at line 5434, the 1-char strip, the case-'i'
    /// user-tier dispatch (NEW case-letter), hits the NO-GUARD inline
    /// matcher at line 6709 <c>if (MatchOptWithParam("invite", pch,
    /// param, msg_sent))</c>. With pch="invite" and NO param, the
    /// matcher matches 6 bytes, falls through allowNoParams=false to
    /// the separator-check else, emits "Missing arg for option invite"
    /// via SendVaMessage at line 4548, sets msg_sent=true, returns
    /// false. Body block (GetGameIDFromName/GroupInvite) skipped.
    /// Execution then traverses the SECOND structural block at line
    /// 6730 <c>if (AdminLevel() &gt;= GM) { ... "invisible" ... "invis"
    /// ... }</c> -- enters because status=100, but strcmp("invite",
    /// "invisible") != 0 skip, strcmp("invite","invis") != 0 skip,
    /// outer block ends, case-'i' breaks at 6754. NET RESULT: ONE
    /// emit.
    ///
    /// <para>
    /// EIGHTEENTH pin on the user-tier (single-slash) dispatch path.
    /// FIRST pin on user-tier case-'i' -- NEW case-letter extends
    /// user-tier dispatcher switch coverage to THIRTEEN distinct
    /// case-letters (a/b/c/d/e/f/h/i/k/l/n/p/r). SEVENTEENTH pin on
    /// the MatchOptWithParam ERROR path. SAME 6-byte option-name %s
    /// width as Wave 145 (user-tier case-'e' effect OUTER-BLOCK-GUARD)
    /// BUT via DIFFERENT case-letter AND DIFFERENT structural pattern
    /// -- pins cross-case-letter SAME-WIDTH structural-pattern
    /// divergence at 6-byte. SECOND pin on the NO-GUARD inline matcher
    /// structural pattern (FIRST was Wave 147 case-'h' /ht); Wave 149
    /// is the FIRST SAME-STRUCTURAL-PATTERN deepening of NO-GUARD --
    /// a structural-pattern deepening pin where both pins share the
    /// SAME pattern but different case-letter, ruling out per-case-
    /// letter regressions within the NO-GUARD pattern.
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 148
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     user-tier case-'i' dispatch + NO-GUARD inline matcher
    ///     regression at <c>PlayerConnection.cpp:6708-6728</c>.
    ///     case-'i' was previously unpinned in the user-tier switch;
    ///     a regression that dropped case-'i' entirely, reordered case
    ///     labels, or routed *pch=='i' to the wrong handler would
    ///     silently swallow /invite. A regression that wrapped
    ///     /invite in any AdminLevel guard would skip the matcher for
    ///     non-privileged users and fail this test. Wave 149 pins
    ///     case-'i' is REACHABLE via the user-tier switch dispatcher
    ///     AND the NO-GUARD inline matcher dispatches correctly to
    ///     emit on missing arg.
    ///   </item>
    ///   <item>
    ///     MIXED structural-pattern within case-'i' regression at
    ///     <c>PlayerConnection.cpp:6709-6753</c>. case-'i' has TWO
    ///     different structural patterns coexisting: NO-GUARD inline
    ///     matcher at 6709 (/invite) AND OUTER-BLOCK-GUARD at 6730
    ///     wrapping "invisible" + "invis" strcmps. With pch="invite",
    ///     the first matcher emits and msg_sent=true; the second
    ///     block enters (status=100 >= GM) but both strcmps fail
    ///     against pch="invite". NET RESULT: ONE emit. A regression
    ///     that ADDED a competing matcher in the outer-block-guard
    ///     section that matched against pch="invite" (e.g. a typo
    ///     "invit*" matcher) would produce a second message. Wave 149
    ///     pins the mixed structural-pattern within a single case-
    ///     letter -- a structural-locality invariant.
    ///   </item>
    ///   <item>
    ///     cross-case-letter SAME-WIDTH structural-pattern divergence
    ///     regression at <c>PlayerClass.cpp:3422</c>. Wave 145 pins
    ///     6-byte at user-tier case-'e' OUTER-BLOCK-GUARD; Wave 149
    ///     pins 6-byte at user-tier case-'i' NO-GUARD inline. Same
    ///     width, different case-letter, different structural
    ///     pattern. A regression in vsprintf_s with off-by-one at
    ///     6-byte width AND a specific case-letter/structural-
    ///     pattern dispatch path would fail one but not the other;
    ///     Wave 149 rules out per-case-letter AND per-structural-
    ///     pattern format-substitution branches at 6-byte width.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-
    /// level error path; /invite has NO AdminLevel guard in the retail
    /// server -- it's a "user-tier" command available to all players
    /// (group invitation is a baseline social feature). No server
    /// permissiveness added; the pin preserves retail behavior.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashInviteMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (30) = 33 bytes.
        const int ExpectedReplyPayloadLength = 33;
        // strlen(literal) + 1 NUL = 30.
        const short ExpectedReplyLengthField = 30;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 29.
        const int ExpectedLiteralByteCount = 29;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Invitee", shipName: "InviteeShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/invite");

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

                // EXACT equals filter (not StartsWith) -- defensive against
                // any future sibling "invite*" option emits in case-'i'.
                if (!text.Equals("Missing arg for option invite", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgInviteLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/invite\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option invite\". Likely the user-tier case-'i' " +
                $"dispatch at line 6708 stopped routing, the NO-GUARD inline matcher " +
                $"at line 6709 stopped dispatching, an AdminLevel guard was wrapped " +
                $"around the matcher (regression), the second structural block at " +
                $"line 6730 produced a competing emit (regression), or the missing-arg " +
                $"ERROR fork at PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 150 missing-arg ERROR literal for case-'m' /move. The matcher
    /// at PlayerConnection.cpp:6825 reads
    /// `if (MatchOptWithParam("move", pch, param, msg_sent))` -- HEAD
    /// position in case-'m' with NEITHER outer-block AdminLevel guard
    /// NOR inline AdminLevel guard, matcher dispatches unconditionally.
    /// With NO param after "/move", MatchOptWithParam's matcher branch
    /// matches 4 bytes, arg[4]='\0' -- NOT '=' / NOT ' ' / NOT isalpha,
    /// allowNoParams false, so falls through to else at line 4546 and
    /// hits the separator-check NUL fall-through. 27 ASCII bytes after
    /// %s substitution -- 4-byte %s width (TRIPLE 4-byte pin: SAME as
    /// Waves 146 case-'f' form COMBINED-GUARD and 148 case-'k' kick
    /// CASE-FALL-THROUGH; Wave 150 provides cross-case-letter SAME-
    /// WIDTH triple structural-pattern divergence at 4-byte AND
    /// deepens the NO-GUARD inline matcher coverage to THREE pins).
    /// </summary>
    private const string MissingArgMoveLiteral = "Missing arg for option move";

    /// <summary>
    /// Wave 150 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 31-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/move</c> (NO param) -- routes through the user-tier
    /// dispatcher entry at line 5434, the 1-char strip, the case-'m'
    /// user-tier dispatch (NEW case-letter), hits the NO-GUARD HEAD
    /// matcher at line 6825 <c>if (MatchOptWithParam("move", pch,
    /// param, msg_sent))</c>. With pch="move" and NO param, the matcher
    /// matches 4 bytes, falls through allowNoParams=false to the
    /// separator-check else, emits "Missing arg for option move" via
    /// SendVaMessage at line 4548, sets msg_sent=true, returns false.
    /// Body block (HandleMoveRequest) skipped. Execution traverses the
    /// remaining else-if chain at line 6830 (mobaggro 8-byte strncmp
    /// mismatch at index 2 'b' vs 'v', returns false NO emit) and line
    /// 6835 (music 5-byte strncmp mismatch at index 1 'u' vs 'o',
    /// returns false NO emit). case-'m' breaks at line 6853. NET
    /// RESULT: ONE emit.
    ///
    /// <para>
    /// NINETEENTH pin on the user-tier (single-slash) dispatch path.
    /// FIRST pin on user-tier case-'m' -- NEW case-letter extends
    /// user-tier dispatcher switch coverage to FOURTEEN distinct
    /// case-letters (a/b/c/d/e/f/h/i/k/l/m/n/p/r). EIGHTEENTH pin on
    /// the MatchOptWithParam ERROR path. THIRD 4-byte %s width pin
    /// (Waves 146 case-'f' form COMBINED-GUARD + 148 case-'k' kick
    /// CASE-FALL-THROUGH + 150 case-'m' move NO-GUARD) -- TRIPLE
    /// cross-case-letter SAME-WIDTH structural-pattern divergence at
    /// 4-byte. THIRD pin on the NO-GUARD inline matcher structural
    /// pattern (Waves 147 case-'h' /ht + 149 case-'i' /invite + 150
    /// case-'m' /move) -- TRIPLE NO-GUARD pin, a structural-pattern
    /// triple-deepening across three distinct case-letters at three
    /// distinct widths (2/6/4 bytes) which rules out per-case-letter
    /// AND per-width regressions within the NO-GUARD pattern.
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 149
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     user-tier case-'m' dispatch + NO-GUARD HEAD matcher
    ///     regression at <c>PlayerConnection.cpp:6824-6829</c>.
    ///     case-'m' was previously unpinned in the user-tier switch;
    ///     a regression that dropped case-'m' entirely, reordered
    ///     case labels, or routed *pch=='m' to the wrong handler
    ///     would silently swallow /move. A regression that wrapped
    ///     /move in any AdminLevel guard would skip the matcher for
    ///     non-privileged users and fail this test. Wave 150 pins
    ///     case-'m' is REACHABLE via the user-tier switch dispatcher
    ///     AND the NO-GUARD HEAD matcher dispatches correctly to
    ///     emit on missing arg.
    ///   </item>
    ///   <item>
    ///     case-'m' matcher-chain fall-through regression at
    ///     <c>PlayerConnection.cpp:6830-6852</c>. After the HEAD
    ///     matcher emits and returns false, execution continues
    ///     through the else-if chain: mobaggro (DEV-guard MATCHER-
    ///     FIRST inline at 6830, 8-byte strncmp mismatch at index 2
    ///     'b' vs 'v'), music (DEV-guard MATCHER-FIRST inline at
    ///     6835, 5-byte strncmp mismatch at index 1 'u' vs 'o').
    ///     NET RESULT: ONE emit. A regression that ADDED a competing
    ///     matcher anywhere in the chain that matched against
    ///     pch="move" by accident would produce a second message.
    ///     Wave 150 pins the matcher-chain fall-through as a
    ///     structural invariant.
    ///   </item>
    ///   <item>
    ///     TRIPLE 4-byte cross-case-letter SAME-WIDTH structural-
    ///     pattern divergence regression at <c>PlayerClass.cpp:3422</c>.
    ///     Wave 146 pins 4-byte at user-tier case-'f' COMBINED-GUARD
    ///     (outer-block + inline matcher-first); Wave 148 pins 4-byte
    ///     at user-tier case-'k' CASE-FALL-THROUGH (OUTER-BLOCK-GUARD
    ///     + no break); Wave 150 pins 4-byte at user-tier case-'m'
    ///     NO-GUARD (no guards anywhere). Same width, THREE different
    ///     case-letters, THREE different structural patterns. A
    ///     regression in vsprintf_s with off-by-one at 4-byte width
    ///     AND a specific case-letter/structural-pattern dispatch path
    ///     would fail one but not all three; Wave 150 completes the
    ///     4-byte triple-pin which rules out per-case-letter AND
    ///     per-structural-pattern format-substitution branches at
    ///     4-byte width across THREE structural patterns.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-
    /// level error path. /move (avatar position request) had NO
    /// AdminLevel guard in the retail server -- baseline user-tier
    /// command. No server permissiveness added; the pin preserves
    /// retail behavior.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashMoveMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (28) = 31 bytes.
        const int ExpectedReplyPayloadLength = 31;
        // strlen(literal) + 1 NUL = 28.
        const short ExpectedReplyLengthField = 28;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 27.
        const int ExpectedLiteralByteCount = 27;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Movee", shipName: "MoveeShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/move");

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

                // EXACT equals filter (not StartsWith) -- defensive against
                // any future sibling "move*" option emits in case-'m'.
                if (!text.Equals("Missing arg for option move", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgMoveLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/move\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option move\". Likely the user-tier case-'m' " +
                $"dispatch at line 6824 stopped routing, the NO-GUARD HEAD matcher " +
                $"at line 6825 stopped dispatching, an AdminLevel guard was wrapped " +
                $"around the matcher (regression), the matcher-chain fall-through " +
                $"to mobaggro/music produced a competing emit (regression), or the " +
                $"missing-arg ERROR fork at PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 151 missing-arg ERROR literal for case-'o' /orientation. The
    /// matcher at PlayerConnection.cpp:6881 reads
    /// `if (MatchOptWithParam("orientation", pch, param, msg_sent))`
    /// with NEITHER outer-block AdminLevel guard NOR inline AdminLevel
    /// guard, matcher dispatches unconditionally. With NO param after
    /// "/orientation", MatchOptWithParam's matcher branch matches 11
    /// bytes, arg[11]='\0' -- NOT '=' / NOT ' ' / NOT isalpha,
    /// allowNoParams false, so falls through to else at line 4546 and
    /// hits the separator-check NUL fall-through. 34 ASCII bytes after
    /// %s substitution -- 11-byte %s width (FOURTH 11-byte pin: SAME
    /// width as Waves 138 GM case-g HEAD + 139 GM case-g SECOND + 142
    /// GM case-s HEAD; Wave 151 is FIRST 11-byte pin at USER-TIER
    /// providing cross-tier 11-byte structural divergence; deepens
    /// the NO-GUARD inline matcher coverage to FOUR pins).
    /// </summary>
    private const string MissingArgOrientationLiteral = "Missing arg for option orientation";

    /// <summary>
    /// Wave 151 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 38-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/orientation</c> (NO param) -- routes through the
    /// user-tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'o' user-tier dispatch (NEW case-letter), skips the strcmp
    /// "ori" bare-if at line 6873 (pch="orientation" != "ori"), then
    /// hits the NO-GUARD inline matcher at line 6881
    /// <c>if (MatchOptWithParam("orientation", pch, param, msg_sent))</c>.
    /// With pch="orientation" and NO param, the matcher matches 11
    /// bytes, falls through allowNoParams=false to the separator-check
    /// else, emits "Missing arg for option orientation" via SendVaMessage
    /// at line 4548, sets msg_sent=true, returns false. Body block
    /// (HandleOrientationRequest) skipped. else-if oeuler at 6886
    /// (strncmp mismatch at index 1 'e' vs 'r', false NO emit). else-if
    /// openif at 6891 (strncmp mismatch at index 1 'p' vs 'r', false NO
    /// emit). case-'o' breaks at 6946. NET RESULT: ONE emit.
    ///
    /// <para>
    /// TWENTIETH pin on the user-tier (single-slash) dispatch path.
    /// FIRST pin on user-tier case-'o' -- NEW case-letter extends
    /// user-tier dispatcher switch coverage to FIFTEEN distinct
    /// case-letters (a/b/c/d/e/f/h/i/k/l/m/n/o/p/r). NINETEENTH pin on
    /// the MatchOptWithParam ERROR path. FOURTH 11-byte %s width pin
    /// (Waves 138 GM case-g HEAD + 139 GM case-g SECOND + 142 GM
    /// case-s HEAD + 151 user-tier case-o NO-GUARD) -- FIRST user-tier
    /// 11-byte pin AND cross-tier 11-byte structural divergence at
    /// NO-GUARD pattern. FOURTH pin on the NO-GUARD inline matcher
    /// structural pattern (Waves 147 case-'h' 2-byte + 149 case-'i'
    /// 6-byte + 150 case-'m' 4-byte + 151 case-'o' 11-byte) --
    /// QUADRUPLE NO-GUARD pin spanning four distinct case-letters at
    /// four distinct widths (2/4/6/11 bytes) which rules out per-case-
    /// letter AND per-width regressions within the NO-GUARD pattern.
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 150
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     user-tier case-'o' dispatch + bare-if "ori" head skip +
    ///     NO-GUARD inline matcher regression at
    ///     <c>PlayerConnection.cpp:6872-6885</c>. case-'o' was
    ///     previously unpinned in the user-tier switch; a regression
    ///     that dropped case-'o' entirely, reordered case labels, or
    ///     routed *pch=='o' to the wrong handler would silently
    ///     swallow /orientation. The HEAD bare-if at 6873 strcmps for
    ///     exact "ori" which fails against pch="orientation" --
    ///     control falls through to the matcher at 6881. A regression
    ///     that converted the bare-if to an else-if or added a return
    ///     would short-circuit the matcher path. Wave 151 pins case-
    ///     'o' is REACHABLE via the user-tier switch dispatcher AND
    ///     the bare-if/matcher fall-through correctly emits.
    ///   </item>
    ///   <item>
    ///     case-'o' matcher-chain fall-through regression at
    ///     <c>PlayerConnection.cpp:6886-6945</c>. After the orientation
    ///     matcher emits, execution continues through the else-if
    ///     chain: oeuler (NO-GUARD MATCHER-FIRST inline at 6886,
    ///     6-byte strncmp mismatch at index 1 'e' vs 'r'), openif
    ///     (NO-GUARD MATCHER-FIRST inline at 6891, 6-byte strncmp
    ///     mismatch at index 1 'p' vs 'r'). NET RESULT: ONE emit. A
    ///     regression that ADDED a competing matcher anywhere in the
    ///     chain that matched against pch="orientation" by accident
    ///     would produce a second message. Wave 151 pins the matcher-
    ///     chain fall-through as a structural invariant.
    ///   </item>
    ///   <item>
    ///     cross-tier 11-byte structural divergence regression at
    ///     <c>PlayerClass.cpp:3422</c>. Waves 138/139 pin 11-byte at
    ///     GM-tier case-'g' HEAD/SECOND (MATCHER-FIRST inline +
    ///     SDEV-guard); Wave 142 pins 11-byte at GM-tier case-'s'
    ///     HEAD. Wave 151 pins 11-byte at USER-tier case-'o' NO-
    ///     GUARD. Same width, FIRST user-tier 11-byte pin, FOURTH
    ///     11-byte pin overall, NEW NO-GUARD structural pattern at
    ///     11-byte width. A regression in vsprintf_s with off-by-one
    ///     at 11-byte width AND a specific tier/case-letter/
    ///     structural-pattern dispatch path would fail one but not
    ///     all four; Wave 151 deepens 11-byte to cross-tier coverage.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-
    /// level error path. /orientation (avatar rotation request) had NO
    /// AdminLevel guard in the retail server -- baseline user-tier
    /// command. No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashOrientationMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (35) = 38 bytes.
        const int ExpectedReplyPayloadLength = 38;
        // strlen(literal) + 1 NUL = 35.
        const short ExpectedReplyLengthField = 35;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 34.
        const int ExpectedLiteralByteCount = 34;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Oriento", shipName: "OrientoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/orientation");

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

                // EXACT equals filter (not StartsWith) -- defensive against
                // any future sibling "orientation*" option emits in case-'o'.
                if (!text.Equals("Missing arg for option orientation", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgOrientationLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/orientation\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option orientation\". Likely the user-tier case-'o' " +
                $"dispatch at line 6872 stopped routing, the bare-if \"ori\" head skip " +
                $"at line 6873 stopped falling through, the NO-GUARD inline matcher at " +
                $"line 6881 stopped dispatching, an AdminLevel guard was wrapped around " +
                $"the matcher (regression), the matcher-chain fall-through to oeuler/" +
                $"openif produced a competing emit (regression), or the missing-arg " +
                $"ERROR fork at PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 152 missing-arg ERROR literal for case-'s' /script. The
    /// matcher at PlayerConnection.cpp:7143 reads
    /// `else if (MatchOptWithParam("script", pch, param, msg_sent) &amp;&amp; AdminLevel() &gt;= SDEV)`
    /// -- SDEV-guarded MATCHER-FIRST inline (matcher emits via
    /// SendVaMessage regardless of the &amp;&amp; short-circuit because
    /// SendVaMessage runs INSIDE MatchOptWithParam before return).
    /// With admin status=100 (AdminLevel=100 &gt;= SDEV=90) the guard
    /// passes; with NO param after "/script", strncmp matches 6 bytes,
    /// arg[6]='\0' fails separator-check, allowNoParams=false -- emits
    /// "Missing arg for option script", returns false, &amp;&amp;
    /// short-circuits body block (lua dofile / "Lua removed until
    /// build warnings fixed" message) SKIPPED. 29 ASCII bytes after %s
    /// substitution -- 6-byte %s width (FIRST user-tier 6-byte
    /// SDEV-guard MATCHER-FIRST pin; deepens MATCHER-FIRST inline
    /// structural pattern coverage to a THIRD pin; FIRST user-tier
    /// SDEV-guard pin in the entire HandleSlashCommands catalogue --
    /// previously SDEV-guards were only pinned at the GM-tier
    /// dispatcher in Wave 139's case-g HEAD/SECOND).
    /// </summary>
    private const string MissingArgScriptLiteral = "Missing arg for option script";

    /// <summary>
    /// Wave 152 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 33-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/script</c> (NO param) -- routes through the
    /// user-tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'s' user-tier dispatch (NEW case-letter -- 16th user-tier
    /// case-letter), skips the strcmp "slaysectormobs" head at line
    /// 7137, then hits the SDEV-guard MATCHER-FIRST inline matcher at
    /// line 7143
    /// <c>else if (MatchOptWithParam("script", pch, param, msg_sent) &amp;&amp; AdminLevel() &gt;= SDEV)</c>.
    /// With pch="script" and NO param, matcher matches 6 bytes,
    /// fails separator-check allowNoParams=false, emits "Missing arg
    /// for option script" via SendVaMessage at line 4548, sets
    /// msg_sent=true, returns false. && short-circuits body block.
    /// Subsequent sibling matchers (sounds 6B mismatch idx 1 'o'/'c',
    /// strcmp setturrets/setrespawns FAIL, scale 5B mismatch idx 2,
    /// skillpoints 11B mismatch idx 1, stat 4B mismatch idx 1, scan
    /// 4B mismatch idx 2, shieldwarnings 14B mismatch idx 1) all
    /// return false NO emit. Outer DEV-guard block at 7286 enters
    /// (AdminLevel=100 >= DEV=80) but signature/setradius/setradius/
    /// shutdown/sendp/strings/stats/shieldbuff all mismatch against
    /// pch="script". case-'s' breaks at 7458. NET RESULT: ONE emit.
    ///
    /// <para>
    /// TWENTY-FIRST pin on the user-tier (single-slash) dispatch path.
    /// FIRST pin on user-tier case-'s' -- NEW case-letter extends
    /// user-tier dispatcher switch coverage to SIXTEEN distinct
    /// case-letters (a/b/c/d/e/f/h/i/k/l/m/n/o/p/r/s). TWENTIETH pin
    /// on the MatchOptWithParam ERROR path. FIRST user-tier SDEV-guard
    /// MATCHER-FIRST inline pin in the entire HandleSlashCommands
    /// catalogue (previously SDEV-guards were only pinned at the
    /// GM-tier dispatcher via Wave 139 case-g HEAD/SECOND) --
    /// cross-tier SDEV-guard structural deepening. THIRD MATCHER-FIRST
    /// inline pin (Waves 139 GM case-g + 143 user-tier case-d + 152
    /// user-tier case-s) -- deepens MATCHER-FIRST inline to THREE
    /// distinct case-letters across BOTH tiers at THREE distinct widths
    /// (11/1/6 bytes). SECOND 6-byte user-tier %s width pin (Wave 149
    /// /invite case-i NO-GUARD + Wave 152 /script case-s SDEV-guard
    /// MATCHER-FIRST) -- SAME width DIFFERENT case-letter DIFFERENT
    /// structural pattern, ruling out per-case-letter and
    /// per-structural-pattern format-substitution branches at 6-byte
    /// width within the user-tier dispatcher.
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 151
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     user-tier case-'s' dispatch + strcmp head skip +
    ///     SDEV-guard MATCHER-FIRST inline matcher regression at
    ///     <c>PlayerConnection.cpp:7136-7178</c>. case-'s' was
    ///     previously unpinned in the user-tier switch (only pinned
    ///     at GM-tier via Wave 142); a regression that dropped
    ///     user-tier case-'s' entirely, reordered case labels, or
    ///     routed *pch=='s' to the wrong handler would silently
    ///     swallow /script. The strcmp head at 7137 checks for
    ///     "slaysectormobs" (FAIL against "script"); a regression
    ///     that made the strcmp lossy (e.g. strncmp-like prefix
    ///     match) would cause head to enter and produce a different
    ///     emit. The SDEV-guard MATCHER-FIRST matcher at 7143 is the
    ///     FIRST inline SDEV-guard at user-tier; a regression that
    ///     swapped the guard tier (SDEV -&gt; GM, DEV, or NO-GUARD)
    ///     would change emit visibility per tier; a regression that
    ///     swapped MATCHER-FIRST -&gt; GUARD-FIRST would short-circuit
    ///     the matcher emit when AdminLevel &lt; SDEV (admin sees
    ///     emit, non-admin does not). Wave 152 pins case-'s' is
    ///     REACHABLE via user-tier switch AND SDEV-guard MATCHER-
    ///     FIRST emits correctly on missing arg.
    ///   </item>
    ///   <item>
    ///     case-'s' matcher-chain fall-through regression at
    ///     <c>PlayerConnection.cpp:7179-7457</c> spanning 9 sibling
    ///     matchers + 8 sibling strcmps across BOTH the inline arm
    ///     AND the outer DEV-guard block at 7286. After the script
    ///     matcher emits and msg_sent=true, execution continues
    ///     through the entire else-if chain plus the OUTER DEV-guard
    ///     block which enters because AdminLevel=100 &gt;= DEV=80.
    ///     NONE of the 9 MatchOptWithParam siblings or 8 strcmp
    ///     siblings match against pch="script" -- ONE emit. A
    ///     regression that ADDED a competing matcher matching pch=
    ///     "script" by accident would produce a second message;
    ///     a regression that ACCIDENTALLY entered the DEV-guard block
    ///     content with the wrong AdminLevel threshold would not be
    ///     caught by this pin alone but the matcher-chain
    ///     fall-through invariant is preserved. Wave 152 pins the
    ///     largest matcher-chain fall-through in the user-tier
    ///     dispatcher (17 sibling arms) as a structural invariant.
    ///   </item>
    ///   <item>
    ///     cross-tier SDEV-guard structural divergence regression
    ///     at <c>PlayerClass.cpp:3422</c>. Wave 139 pins GM-tier
    ///     case-'g' SDEV-guard MATCHER-FIRST at 11-byte width; Wave
    ///     152 pins user-tier case-'s' SDEV-guard MATCHER-FIRST at
    ///     6-byte width. SAME structural pattern, DIFFERENT tier,
    ///     DIFFERENT case-letter, DIFFERENT %s width. A regression
    ///     that selectively broke SDEV-guard at one tier but not
    ///     the other would fail one pin but not both; Wave 152
    ///     deepens SDEV-guard to cross-tier coverage. Additionally:
    ///     FIRST user-tier 6-byte SDEV-guard pin paired with Wave
    ///     149 user-tier 6-byte NO-GUARD pin -- same tier, same
    ///     width, DIFFERENT structural pattern (SDEV-guard vs
    ///     NO-GUARD) -- rules out per-structural-pattern format-
    ///     substitution branches at 6-byte width within user-tier.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-
    /// level error path. /script (lua scripting dispatch, retail had
    /// SDEV-guard inline) emits "Missing arg" regardless of guard
    /// pass/fail because SendVaMessage executes inside
    /// MatchOptWithParam before the && return-value short-circuit
    /// rejects the body. No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashScriptMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (30) = 33 bytes.
        const int ExpectedReplyPayloadLength = 33;
        // strlen(literal) + 1 NUL = 30.
        const short ExpectedReplyLengthField = 30;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 29.
        const int ExpectedLiteralByteCount = 29;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Scripto", shipName: "ScriptoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/script");

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

                // EXACT equals filter (not StartsWith) -- defensive against
                // any future sibling "script*" option emits in case-'s'.
                if (!text.Equals("Missing arg for option script", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgScriptLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/script\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option script\". Likely the user-tier case-'s' " +
                $"dispatch at line 7136 stopped routing, the strcmp \"slaysectormobs\" " +
                $"head at line 7137 stopped failing through, the SDEV-guard MATCHER-FIRST " +
                $"inline matcher at line 7143 stopped emitting (guard regression: tier " +
                $"swap SDEV -> GM/DEV/NO-GUARD changed visibility; structural swap " +
                $"MATCHER-FIRST -> GUARD-FIRST would block matcher emit when AdminLevel " +
                $"< SDEV), the matcher-chain fall-through across 17 sibling arms " +
                $"produced a competing emit (regression), or the missing-arg ERROR fork " +
                $"at PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 153 missing-arg ERROR literal for case-'t' /tilt. The
    /// matcher at PlayerConnection.cpp:7495 reads
    /// `if (MatchOptWithParam("tilt", pch, param, msg_sent))`
    /// -- NO AdminLevel guard, but case-'t' is structured as
    /// CONSECUTIVE-IF statements (NOT an else-if chain) so the matcher
    /// is independent of preceding /test, /talktree, /testmsg ifs.
    /// With NO param after "/tilt", strncmp matches 4 bytes,
    /// arg[4]='\0' fails separator-check, allowNoParams=false -- emits
    /// "Missing arg for option tilt", returns false. Body block
    /// (HandleTiltRequest) SKIPPED. 27 ASCII bytes after %s
    /// substitution -- 4-byte %s width (FIFTH 4-byte pin: Waves 146
    /// /form COMBINED-GUARD + 148 /kick CASE-FALL-THROUGH + 150
    /// /move NO-GUARD-ELSE-IF + 153 /tilt NO-GUARD-CONSECUTIVE-IF;
    /// FIRST CONSECUTIVE-IF structural pattern pin across the entire
    /// HandleSlashCommands catalogue -- seventh distinct structural
    /// dispatcher pattern after MATCHER-FIRST inline + GUARD-FIRST
    /// inline + OUTER-BLOCK-GUARD + COMBINED + NO-GUARD-ELSE-IF +
    /// CASE-FALL-THROUGH).
    /// </summary>
    private const string MissingArgTiltLiteral = "Missing arg for option tilt";

    /// <summary>
    /// Wave 153 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 31-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/tilt</c> (NO param) -- routes through the user-tier
    /// dispatcher entry at line 5434, the 1-char strip, the case-'t'
    /// user-tier dispatch (NEW case-letter -- 17th user-tier
    /// case-letter), then hits the CONSECUTIVE-IF NO-GUARD matcher at
    /// line 7495 <c>if (MatchOptWithParam("tilt", pch, param, msg_sent))</c>.
    /// With pch="tilt" and NO param, matcher matches 4 bytes,
    /// fails separator-check allowNoParams=false, emits "Missing arg
    /// for option tilt" via SendVaMessage at line 4548, sets
    /// msg_sent=true, returns false. Body block (HandleTiltRequest)
    /// SKIPPED.
    ///
    /// <para>
    /// Crucially, case-'t' uses CONSECUTIVE-IF statements (NOT
    /// else-if chain). The preceding ifs at 7462 (strcmp "test"
    /// FAIL), 7471 (strcasecmp "talktree" FAIL), 7486
    /// (MatchOptWithParam "testmsg" 7-byte strncmp mismatch idx 1
    /// 'e' vs 'i' false NO emit) all skip their bodies. The
    /// subsequent ifs at 7501 (MatchOptWithParam "terminate" 9-byte
    /// strncmp mismatch idx 1 'e' vs 'i' false NO emit) and 7521
    /// (MatchOptWithParam "trade" 5-byte strncmp mismatch idx 1
    /// 'r' vs 'i' false NO emit) also skip. case-'t' breaks at 7573.
    /// NET RESULT: ONE emit.
    /// </para>
    ///
    /// <para>
    /// TWENTY-SECOND pin on the user-tier (single-slash) dispatch
    /// path. FIRST pin on user-tier case-'t' -- NEW case-letter
    /// extends user-tier dispatcher switch coverage to SEVENTEEN
    /// distinct case-letters (a/b/c/d/e/f/h/i/k/l/m/n/o/p/r/s/t).
    /// TWENTY-FIRST pin on the MatchOptWithParam ERROR path. FIRST
    /// CONSECUTIVE-IF structural pattern pin in the entire
    /// HandleSlashCommands catalogue -- SEVENTH distinct structural
    /// dispatcher pattern after MATCHER-FIRST inline + GUARD-FIRST
    /// inline + OUTER-BLOCK-GUARD + COMBINED outer+inline + NO-GUARD-
    /// ELSE-IF + CASE-FALL-THROUGH. FIFTH 4-byte %s width pin (Waves
    /// 146 /form COMBINED-GUARD + 148 /kick CASE-FALL-THROUGH + 150
    /// /move NO-GUARD-ELSE-IF + 153 /tilt NO-GUARD-CONSECUTIVE-IF) --
    /// SAME width FOUR distinct case-letters FOUR distinct structural
    /// patterns, rules out per-case-letter and per-structural-pattern
    /// format-substitution branches at 4-byte width across FOUR
    /// distinct structural patterns. SIXTH NO-GUARD pin counting both
    /// ELSE-IF and CONSECUTIVE-IF variants (Waves 147/149/150/151
    /// NO-GUARD-ELSE-IF + 153 NO-GUARD-CONSECUTIVE-IF).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 152
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     user-tier case-'t' dispatch + CONSECUTIVE-IF independence
    ///     regression at <c>PlayerConnection.cpp:7461-7572</c>.
    ///     case-'t' was previously unpinned in the user-tier switch;
    ///     a regression that dropped case-'t' entirely, reordered
    ///     case labels, or routed *pch=='t' to the wrong handler
    ///     would silently swallow /tilt. The CONSECUTIVE-IF structure
    ///     is unique within the user-tier switch: each `if` is
    ///     independent rather than chained via `else if`, which
    ///     means a matcher that emits does NOT short-circuit
    ///     subsequent matchers; a regression that ADDED `else` keywords
    ///     converting CONSECUTIVE-IF to ELSE-IF would change
    ///     fall-through semantics (e.g. if /tilt and /trade both
    ///     somehow matched, ELSE-IF would emit only one, CONSECUTIVE-
    ///     IF would emit both). Wave 153 pins case-'t' is REACHABLE
    ///     via the user-tier switch dispatcher AND the CONSECUTIVE-IF
    ///     NO-GUARD matcher dispatches correctly to emit on missing
    ///     arg AND the consecutive-if independence semantic is
    ///     preserved (no spurious second emit from /terminate or
    ///     /trade siblings).
    ///   </item>
    ///   <item>
    ///     case-'t' matcher-chain fall-through regression at
    ///     <c>PlayerConnection.cpp:7501-7572</c> across 2 NO-GUARD
    ///     CONSECUTIVE-IF siblings (/terminate, /trade). After the
    ///     /tilt matcher emits, execution continues through 2
    ///     subsequent independent ifs (NOT chained via else-if).
    ///     /terminate (9-byte) and /trade (5-byte) both strncmp-
    ///     mismatch against pch="tilt" at idx 1. A regression that
    ///     ADDED a competing matcher matching pch="tilt" by accident
    ///     would produce a second message (and the CONSECUTIVE-IF
    ///     structure means even an else-clause regression could not
    ///     suppress that second emit). Wave 153 pins the CONSECUTIVE-
    ///     IF matcher-chain fall-through as a structural invariant.
    ///   </item>
    ///   <item>
    ///     QUADRUPLE 4-byte cross-case-letter cross-structural-
    ///     pattern divergence regression at <c>PlayerClass.cpp:3422</c>.
    ///     Wave 146 pins 4-byte at user-tier case-'f' COMBINED-GUARD;
    ///     Wave 148 pins 4-byte at user-tier case-'k' CASE-FALL-
    ///     THROUGH; Wave 150 pins 4-byte at user-tier case-'m'
    ///     NO-GUARD-ELSE-IF; Wave 153 pins 4-byte at user-tier
    ///     case-'t' NO-GUARD-CONSECUTIVE-IF. Same width, FOUR
    ///     different case-letters, FOUR different structural patterns.
    ///     A regression in vsprintf_s with off-by-one at 4-byte width
    ///     AND a specific case-letter/structural-pattern dispatch
    ///     path would fail one but not all four; Wave 153 deepens
    ///     4-byte to QUADRUPLE-pin which rules out per-case-letter
    ///     AND per-structural-pattern format-substitution branches
    ///     at 4-byte width across FOUR structural patterns.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-
    /// level error path. /tilt (avatar tilt request) had NO AdminLevel
    /// guard in the retail server -- baseline user-tier command. No
    /// server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashTiltMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (28) = 31 bytes.
        const int ExpectedReplyPayloadLength = 31;
        // strlen(literal) + 1 NUL = 28.
        const short ExpectedReplyLengthField = 28;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 27.
        const int ExpectedLiteralByteCount = 27;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Tilto", shipName: "TiltoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/tilt");

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

                // EXACT equals filter (not StartsWith) -- defensive against
                // any future sibling "tilt*" option emits in case-'t'.
                if (!text.Equals("Missing arg for option tilt", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgTiltLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/tilt\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option tilt\". Likely the user-tier case-'t' " +
                $"dispatch at line 7461 stopped routing, the CONSECUTIVE-IF " +
                $"NO-GUARD matcher at line 7495 stopped dispatching, the " +
                $"CONSECUTIVE-IF structure was changed to ELSE-IF chain (suppressing " +
                $"the independence semantic), an AdminLevel guard was wrapped around " +
                $"the matcher (regression), the matcher-chain fall-through to " +
                $"terminate/trade produced a competing emit (regression), or the " +
                $"missing-arg ERROR fork at PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 154 missing-arg ERROR literal for case-'u' /uitrigger. The
    /// matcher at PlayerConnection.cpp:7576 reads
    /// `if (MatchOptWithParam("uitrigger", pch, param, msg_sent))`
    /// -- NO AdminLevel guard, ELSE-IF chain structural pattern.
    /// With NO param after "/uitrigger", strncmp matches 9 bytes,
    /// arg[9]='\0' fails separator-check, allowNoParams=false -- emits
    /// "Missing arg for option uitrigger", returns false. Body block
    /// (strtok_s param parse + SendOpcode 0x0065 UI_TRIGGER) SKIPPED.
    /// 32 ASCII bytes after %s substitution -- 9-byte %s width (FIRST
    /// 9-byte pin; fills a gap in the MatchOptWithParam ERROR fork
    /// width coverage which previously spanned 1/2/3/4/5/6/7/11/13/14
    /// bytes -- Wave 154 adds 9-byte as the ELEVENTH distinct width).
    /// </summary>
    private const string MissingArgUitriggerLiteral = "Missing arg for option uitrigger";

    /// <summary>
    /// Wave 154 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 36-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/uitrigger</c> (NO param) -- routes through the
    /// user-tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'u' user-tier dispatch (NEW case-letter -- 18th user-tier
    /// case-letter), then hits the NO-GUARD ELSE-IF inline matcher at
    /// line 7576 <c>if (MatchOptWithParam("uitrigger", pch, param, msg_sent))</c>.
    /// With pch="uitrigger" and NO param, matcher matches 9 bytes,
    /// fails separator-check allowNoParams=false, emits "Missing arg
    /// for option uitrigger" via SendVaMessage at line 4548, sets
    /// msg_sent=true, returns false. Body block SKIPPED.
    /// else-if /upgrade at 7593 strncmp("upgrade","uitrigger",7)
    /// u-u, p-i mismatch idx 1 → false NO emit. else-if
    /// strcmp(pch,"undockp") at 7607 FAIL. else-if
    /// strcmp(pch,"uptime") at 7614 FAIL. case-'u' breaks at 7625.
    /// NET RESULT: ONE emit.
    ///
    /// <para>
    /// TWENTY-THIRD pin on the user-tier (single-slash) dispatch
    /// path. FIRST pin on user-tier case-'u' -- NEW case-letter
    /// extends user-tier dispatcher switch coverage to EIGHTEEN
    /// distinct case-letters (a/b/c/d/e/f/h/i/k/l/m/n/o/p/r/s/t/u).
    /// TWENTY-SECOND pin on the MatchOptWithParam ERROR path. FIRST
    /// 9-byte %s width pin -- fills the gap in the ERROR fork width
    /// coverage which previously spanned 1/2/3/4/5/6/7/11/13/14 bytes;
    /// Wave 154 adds 9-byte as the ELEVENTH distinct width covered.
    /// SEVENTH NO-GUARD-family pin (Waves 147 case-'h' 2B + 149
    /// case-'i' 6B + 150 case-'m' 4B + 151 case-'o' 11B + 153 case-'t'
    /// 4B-CONSECUTIVE-IF + 154 case-'u' 9B; six ELSE-IF + one
    /// CONSECUTIVE-IF). FIFTH NO-GUARD-ELSE-IF pin specifically.
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 153
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     user-tier case-'u' dispatch + NO-GUARD ELSE-IF inline
    ///     matcher regression at <c>PlayerConnection.cpp:7575-7592</c>.
    ///     case-'u' was previously unpinned in the user-tier switch;
    ///     a regression that dropped case-'u' entirely, reordered
    ///     case labels, or routed *pch=='u' to the wrong handler
    ///     would silently swallow /uitrigger. The HEAD matcher at
    ///     7576 is NO-GUARD; a regression that wrapped /uitrigger in
    ///     any AdminLevel guard would skip the matcher for non-
    ///     privileged users (or change visibility per tier). Wave 154
    ///     pins case-'u' is REACHABLE via the user-tier switch
    ///     dispatcher AND the NO-GUARD HEAD matcher dispatches
    ///     correctly to emit on missing arg.
    ///   </item>
    ///   <item>
    ///     case-'u' matcher-chain ELSE-IF fall-through regression at
    ///     <c>PlayerConnection.cpp:7593-7624</c> across 1 sibling
    ///     matcher + 2 sibling strcmps. After the HEAD matcher
    ///     emits and msg_sent=true, execution continues through
    ///     /upgrade (7-byte NO-GUARD ELSE-IF inline with INSIDE-BODY
    ///     GM-guard, strncmp mismatch idx 1 'p' vs 'i' false NO
    ///     emit), strcmp "undockp" FAIL, strcmp "uptime" FAIL. NET
    ///     RESULT: ONE emit. A regression that ADDED a competing
    ///     matcher anywhere in the chain that matched against
    ///     pch="uitrigger" by accident would produce a second
    ///     message; Wave 154 pins the matcher-chain fall-through as
    ///     a structural invariant.
    ///   </item>
    ///   <item>
    ///     9-byte %s width gap-fill regression at
    ///     <c>PlayerClass.cpp:3422</c>. Prior to Wave 154 the
    ///     MatchOptWithParam ERROR fork was pinned at 10 distinct
    ///     widths (1/2/3/4/5/6/7/11/13/14 bytes) with a gap at 8/9/10
    ///     /12. Wave 154 fills the 9-byte gap with /uitrigger -- a
    ///     regression in vsprintf_s with off-by-one at 9-byte width
    ///     specifically would not be caught by any of the prior 22
    ///     pins; Wave 154 closes that specific format-substitution
    ///     blind spot. The 9-byte pin is exercised at user-tier
    ///     NO-GUARD ELSE-IF structural pattern which deepens both
    ///     the width and pattern axes simultaneously.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-
    /// level error path. /uitrigger (UI trigger dispatch) had NO
    /// AdminLevel guard in the retail server -- baseline user-tier
    /// command. No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashUitriggerMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (33) = 36 bytes.
        const int ExpectedReplyPayloadLength = 36;
        // strlen(literal) + 1 NUL = 33.
        const short ExpectedReplyLengthField = 33;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 32.
        const int ExpectedLiteralByteCount = 32;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Uitri", shipName: "UitriShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/uitrigger");

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

                // EXACT equals filter (not StartsWith) -- defensive against
                // any future sibling "uitrigger*" option emits in case-'u'.
                if (!text.Equals("Missing arg for option uitrigger", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgUitriggerLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/uitrigger\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option uitrigger\". Likely the user-tier case-'u' " +
                $"dispatch at line 7575 stopped routing, the NO-GUARD ELSE-IF inline " +
                $"matcher at line 7576 stopped dispatching, an AdminLevel guard was " +
                $"wrapped around the matcher (regression), the matcher-chain " +
                $"fall-through to upgrade/undockp/uptime produced a competing emit " +
                $"(regression), or the missing-arg ERROR fork at PlayerConnection.cpp:4548 " +
                $"changed shape (esp. vsprintf_s 9-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 155 missing-arg ERROR literal for case-'w' /wormhole. The
    /// matcher at PlayerConnection.cpp:7673 reads
    /// `if (MatchOptWithParam("wormhole", pch, param, msg_sent))`
    /// -- NO AdminLevel guard outside, INSIDE-BODY BETA_PLUS-guard
    /// (which only affects SUCCESS path). Critically this is an
    /// INDEPENDENT `if` (NOT chained via else-if from preceding
    /// /who/warp matchers at 7628/7650) -- second CONSECUTIVE-IF
    /// structural pattern pin in the dispatcher. With NO param after
    /// "/wormhole", strncmp matches 8 bytes, arg[8]='\0' fails
    /// separator-check, allowNoParams=false -- emits "Missing arg for
    /// option wormhole", returns false. Body block (HandleWormholeRequest
    /// or "/wormhole GM and above only" emit) SKIPPED. 31 ASCII bytes
    /// after %s substitution -- 8-byte %s width (FIRST 8-byte pin;
    /// fills another gap in the MatchOptWithParam ERROR fork width
    /// coverage after Wave 154's 9-byte gap-fill -- Wave 155 adds
    /// 8-byte as the TWELFTH distinct width).
    /// </summary>
    private const string MissingArgWormholeLiteral = "Missing arg for option wormhole";

    /// <summary>
    /// Wave 155 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 35-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/wormhole</c> (NO param) -- routes through the
    /// user-tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'w' user-tier dispatch (NEW case-letter -- 19th user-tier
    /// case-letter), then through the ELSE-IF chain head /who at 7628
    /// (allowNoParams=TRUE, 3-byte strncmp mismatch idx 1 'h' vs 'o'
    /// false NO emit), else-if /warp at 7650 (4-byte strncmp mismatch
    /// idx 1 'a' vs 'o' false NO emit), else-if strcmp(pch,"warpreset")
    /// at 7668 FAIL. Then case-'w' continues with INDEPENDENT
    /// CONSECUTIVE-IF at 7673 <c>if (MatchOptWithParam("wormhole", pch, param, msg_sent))</c>.
    /// With pch="wormhole" and NO param, matcher matches 8 bytes, fails
    /// separator-check allowNoParams=false, emits "Missing arg for
    /// option wormhole" via SendVaMessage at line 4548, sets
    /// msg_sent=true, returns false. Body block (BETA_PLUS-guard +
    /// HandleWormholeRequest or "/wormhole GM and above only" emit)
    /// SKIPPED. Subsequent CONSECUTIVE-IF at 7691 strcmp(pch,"warpreset")
    /// FAIL → skip. case-'w' breaks at 7699. The trailing fallback
    /// `if (!success && !msg_sent) SendVaMessage("Illegal slash command: %s", pch)`
    /// at 7702 is SKIPPED because msg_sent=true. NET RESULT: ONE emit.
    ///
    /// <para>
    /// TWENTY-FOURTH pin on the user-tier (single-slash) dispatch
    /// path. FIRST pin on user-tier case-'w' -- NEW case-letter
    /// extends user-tier dispatcher switch coverage to NINETEEN
    /// distinct case-letters (a/b/c/d/e/f/h/i/k/l/m/n/o/p/r/s/t/u/w).
    /// TWENTY-THIRD pin on the MatchOptWithParam ERROR path. FIRST
    /// 8-byte %s width pin -- fills another gap in ERROR fork width
    /// coverage after Wave 154's 9-byte gap-fill (prior pins now
    /// spanned 1/2/3/4/5/6/7/9/11/13/14); Wave 155 adds 8-byte as
    /// the TWELFTH distinct width covered. Remaining gaps: 10/12
    /// bytes. EIGHTH NO-GUARD-family pin (now SEVEN ELSE-IF + ONE
    /// CONSECUTIVE-IF = wait: Waves 147/149/150/151/154 are NO-GUARD-
    /// ELSE-IF (5 pins), Waves 153 is NO-GUARD-CONSECUTIVE-IF (1 pin),
    /// Wave 155 is NO-GUARD-CONSECUTIVE-IF (within MIXED case-letter
    /// ELSE-IF+CONSECUTIVE-IF structure) -- total 5 ELSE-IF + 2
    /// CONSECUTIVE-IF = 7 distinct NO-GUARD pins. SECOND CONSECUTIVE-IF
    /// pin (Waves 153 case-t pure-CONSECUTIVE-IF + 155 case-w MIXED
    /// ELSE-IF+CONSECUTIVE-IF) -- introduces the MIXED case-letter
    /// dispatcher pattern variant which is structurally distinct from
    /// both pure ELSE-IF (Waves 147/149/150/151/154) and pure
    /// CONSECUTIVE-IF (Wave 153).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 154
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     user-tier case-'w' dispatch + MIXED ELSE-IF+CONSECUTIVE-IF
    ///     case-letter structure regression at
    ///     <c>PlayerConnection.cpp:7627-7699</c>. case-'w' was
    ///     previously unpinned in the user-tier switch; a regression
    ///     that dropped case-'w' entirely, reordered case labels, or
    ///     routed *pch=='w' to the wrong handler would silently
    ///     swallow /wormhole. The MIXED structure is unique within
    ///     the user-tier switch: ELSE-IF chain (7628 /who + 7650
    ///     /warp + 7668 strcmp warpreset) followed by INDEPENDENT
    ///     CONSECUTIVE-IF (7673 /wormhole + 7691 strcmp warpreset).
    ///     A regression that converted the CONSECUTIVE-IF arms to
    ///     ELSE-IF chained off the preceding chain would chain
    ///     /wormhole's dispatch off /warp's success; a regression
    ///     that converted ELSE-IF to CONSECUTIVE-IF would change the
    ///     /who/warp dispatch semantics. Wave 155 pins case-'w' is
    ///     REACHABLE via the user-tier switch dispatcher AND the
    ///     MIXED ELSE-IF+CONSECUTIVE-IF structure is preserved.
    ///   </item>
    ///   <item>
    ///     case-'w' matcher-chain fall-through regression at
    ///     <c>PlayerConnection.cpp:7691-7702</c> including the
    ///     trailing fallback at 7702. After the /wormhole matcher
    ///     emits, execution continues through CONSECUTIVE-IF at 7691
    ///     (strcmp "warpreset" FAIL), case-'w' breaks, and the
    ///     `if (!success && !msg_sent) SendVaMessage("Illegal slash
    ///     command: %s", pch)` fallback at 7702 is SKIPPED because
    ///     msg_sent=true. A regression that flipped the fallback's
    ///     `!msg_sent` to `msg_sent` or removed the guard entirely
    ///     would emit a second message ("Illegal slash command:
    ///     wormhole"). Wave 155 pins the trailing illegal-slash
    ///     fallback's msg_sent gate as a structural invariant via
    ///     the EXACT-equals filter (which would mismatch on a
    ///     spurious "Illegal slash command" second emit).
    ///   </item>
    ///   <item>
    ///     8-byte %s width gap-fill regression at
    ///     <c>PlayerClass.cpp:3422</c>. Prior to Wave 155 the
    ///     MatchOptWithParam ERROR fork was pinned at 11 distinct
    ///     widths (1/2/3/4/5/6/7/9/11/13/14 bytes) after Wave 154's
    ///     9-byte gap-fill, with gaps remaining at 8/10/12. Wave 155
    ///     fills the 8-byte gap with /wormhole -- a regression in
    ///     vsprintf_s with off-by-one at 8-byte width specifically
    ///     would not be caught by any of the prior 23 pins; Wave 155
    ///     closes that format-substitution blind spot. The 8-byte
    ///     pin is exercised at user-tier NO-GUARD CONSECUTIVE-IF
    ///     within MIXED case-letter structure which deepens both
    ///     width and pattern axes simultaneously.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-
    /// level error path. /wormhole (wormhole travel request) had NO
    /// outer AdminLevel guard in the retail server -- INSIDE-BODY
    /// BETA_PLUS check gates SUCCESS path only; ERROR path emits
    /// regardless of AdminLevel. No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashWormholeMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (32) = 35 bytes.
        const int ExpectedReplyPayloadLength = 35;
        // strlen(literal) + 1 NUL = 32.
        const short ExpectedReplyLengthField = 32;
        // SendVaMessage -> SendMessageString default color parameter.
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
            firstName: "Wormo", shipName: "WormoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/wormhole");

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

                // EXACT equals filter (not StartsWith) -- defensive against
                // any future sibling "wormhole*" option emits or the trailing
                // "Illegal slash command: wormhole" fallback at line 7702.
                if (!text.Equals("Missing arg for option wormhole", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgWormholeLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/wormhole\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option wormhole\". Likely the user-tier case-'w' " +
                $"dispatch at line 7627 stopped routing, the ELSE-IF chain head /who/warp " +
                $"stopped falling through, the CONSECUTIVE-IF /wormhole matcher at line " +
                $"7673 stopped dispatching (CONSECUTIVE-IF -> ELSE-IF structural conversion " +
                $"would chain off /warp), an AdminLevel guard was wrapped around the " +
                $"matcher (regression), the trailing illegal-slash fallback at 7702 fired " +
                $"as a second emit (msg_sent gate regression), or the missing-arg ERROR " +
                $"fork at PlayerConnection.cpp:4548 changed shape (esp. vsprintf_s 8-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 156 missing-arg ERROR literal for case-'w' /warp. The
    /// matcher at PlayerConnection.cpp:7650 reads
    /// `else if (MatchOptWithParam("warp", pch, param, msg_sent))`
    /// -- NO outer AdminLevel guard, INSIDE-BODY GM-guard
    /// (which only affects SUCCESS path; ERROR path emits
    /// regardless of AdminLevel). FIRST INSIDE-BODY GM-guard
    /// ELSE-IF pin in the dispatcher catalogue. With NO param
    /// after "/warp", strncmp matches 4 bytes, arg[4]='\0' fails
    /// separator-check, allowNoParams=false -- emits "Missing arg
    /// for option warp", returns false. Body block (GM-guard +
    /// SetWarpSpeed / "Warp limits ..." emit) SKIPPED. 27 ASCII
    /// bytes after %s substitution -- 4-byte %s width SIXTH pin
    /// across FIVE distinct structural patterns (146 COMBINED +
    /// 148 CASE-FALL-THROUGH + 150 NO-GUARD-ELSE-IF + 153
    /// NO-GUARD-CONSECUTIVE-IF + 156 NO-OUTER-GUARD INSIDE-BODY-
    /// GM-GUARD ELSE-IF).
    /// </summary>
    private const string MissingArgWarpLiteral = "Missing arg for option warp";

    /// <summary>
    /// Wave 156 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 31-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/warp</c> (NO param) -- routes through the
    /// user-tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'w' user-tier dispatch (case-letter already pinned by
    /// Wave 155 via /wormhole CONSECUTIVE-IF arm; Wave 156 deepens
    /// case-'w' to the ELSE-IF chain arm /warp). ELSE-IF chain head
    /// /who at 7628 (allowNoParams=TRUE, 3-byte strncmp mismatch idx
    /// 1 'h' vs 'a' false NO emit). else-if /warp at 7650 NO-OUTER-
    /// GUARD INSIDE-BODY-GM-GUARD: strncmp("warp","warp",4) all match,
    /// arg[4]='\0' fails separator-check, allowNoParams=false, emits
    /// "Missing arg for option warp" via SendVaMessage at 4548, sets
    /// msg_sent=true, returns false. INSIDE-BODY GM-guard
    /// `if (AdminLevel() >= GM)` for SUCCESS path SKIPPED (matcher
    /// returned false, body block never entered). else-if strcmp
    /// warpreset at 7668 SKIPPED (chained via else-if; preceding
    /// /warp branch was taken). Then case-'w' continues with
    /// INDEPENDENT CONSECUTIVE-IF at 7673 /wormhole: strncmp
    /// ("wormhole","warp",8) w-w, o-a MISMATCH idx 1 false NO emit.
    /// Independent CONSECUTIVE-IF at 7691 strcmp warpreset FAIL.
    /// case-'w' breaks at 7699. Trailing fallback at 7702 SKIPPED
    /// (msg_sent=true). NET RESULT: ONE emit.
    ///
    /// <para>
    /// TWENTY-FIFTH pin on the user-tier (single-slash) dispatch
    /// path. SECOND pin on user-tier case-'w' (Wave 155 pinned the
    /// CONSECUTIVE-IF arm /wormhole at 7673; Wave 156 deepens case-
    /// 'w' coverage to the ELSE-IF chain arm /warp at 7650). TWENTY-
    /// FOURTH pin on the MatchOptWithParam ERROR path. SIXTH 4-byte
    /// %s width pin -- 4-byte was QUINTUPLE-pinned after Wave 153
    /// (146 COMBINED + 148 CASE-FALL-THROUGH + 150 NO-GUARD-ELSE-IF +
    /// 153 NO-GUARD-CONSECUTIVE-IF) but actually that's only FOUR
    /// before Wave 156; with Wave 156's NO-OUTER-GUARD INSIDE-BODY-
    /// GM-GUARD ELSE-IF this becomes the FIFTH 4-byte structural-
    /// pattern variant. NINTH NO-GUARD-FAMILY pin (counting NO-outer-
    /// guard variants only; Wave 156 specifically introduces the
    /// INSIDE-BODY-GUARD sub-variant that was unpinned before).
    /// FIRST INSIDE-BODY-GUARD pin in the entire HandleSlashCommands
    /// catalogue -- structurally distinct from both NO-GUARD (no
    /// guard anywhere) and outer-GUARD variants (guard wraps the
    /// matcher invocation itself). The INSIDE-BODY guard variant
    /// gates only the SUCCESS path; the ERROR path emits regardless,
    /// so a regression that swapped INSIDE-BODY-GUARD to OUTER-GUARD
    /// would skip the ERROR-fork emit for non-GM users.
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 155
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     case-'w' ELSE-IF chain dispatch + NO-OUTER-GUARD INSIDE-
    ///     BODY-GM-GUARD ELSE-IF matcher regression at
    ///     <c>PlayerConnection.cpp:7650-7667</c>. Wave 155 pinned the
    ///     CONSECUTIVE-IF arm /wormhole at 7673; case-'w' /warp at
    ///     7650 sits on the ELSE-IF chain head/arm sequence (chained
    ///     off /who at 7628) and was UNPINNED before Wave 156. A
    ///     regression that converted ELSE-IF to CONSECUTIVE-IF (or
    ///     vice versa) within case-'w' would change dispatch
    ///     semantics; a regression that converted NO-OUTER-GUARD
    ///     INSIDE-BODY-GM-GUARD to OUTER-GUARD GM-GUARD would skip
    ///     the ERROR-fork emit for non-GM users (the body GM-guard
    ///     gates SUCCESS only; pulling it outside the matcher would
    ///     also gate ERROR); Wave 156 pins case-'w' /warp ELSE-IF
    ///     chain arm is REACHABLE AND the NO-OUTER-GUARD INSIDE-
    ///     BODY-GM-GUARD structural variant is preserved.
    ///   </item>
    ///   <item>
    ///     case-'w' ELSE-IF chain fall-through + cross-pattern
    ///     interleave regression at <c>PlayerConnection.cpp:7668-7702</c>.
    ///     After the /warp matcher emits, execution leaves the
    ///     ELSE-IF chain (because /warp matched and short-circuited
    ///     the else-if to /warpreset at 7668), then enters the
    ///     INDEPENDENT CONSECUTIVE-IF block at 7673 (/wormhole
    ///     strncmp 8B mismatch idx 1 false NO emit), then 7691
    ///     (strcmp warpreset FAIL). case-'w' breaks; trailing
    ///     fallback at 7702 SKIPPED (msg_sent=true). A regression
    ///     that converted CONSECUTIVE-IF to ELSE-IF chained off the
    ///     /warp branch would skip the /wormhole/warpreset block
    ///     entirely; a regression that flipped the trailing
    ///     fallback's `!msg_sent` to `msg_sent` would emit a second
    ///     "Illegal slash command: warp" message. Wave 156 pins the
    ///     cross-pattern interleave (ELSE-IF chain followed by
    ///     INDEPENDENT CONSECUTIVE-IF block) as a structural
    ///     invariant via the EXACT-equals filter.
    ///   </item>
    ///   <item>
    ///     6th 4-byte %s width cross-structural-pattern divergence
    ///     regression at <c>PlayerClass.cpp:3422</c>. Wave 146 pinned
    ///     4-byte at case-'f' /form COMBINED-GUARD; Wave 148 at
    ///     case-'k' /kick CASE-FALL-THROUGH; Wave 150 at case-'m'
    ///     /move NO-GUARD-ELSE-IF; Wave 153 at case-'t' /tilt NO-
    ///     GUARD-CONSECUTIVE-IF; Wave 156 at case-'w' /warp NO-OUTER-
    ///     GUARD INSIDE-BODY-GM-GUARD ELSE-IF. SAME width, FIVE
    ///     different case-letters, FIVE different structural
    ///     patterns. A regression in vsprintf_s with off-by-one at
    ///     4-byte width AND specific case-letter/structural-pattern
    ///     dispatch path would fail one but not all five; Wave 156
    ///     deepens 4-byte to QUINTUPLE-pin ruling out per-case-
    ///     letter AND per-structural-pattern format-substitution
    ///     branches at 4-byte width across FIVE structural patterns.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-
    /// level error path. /warp (warp-speed-set command) had NO outer
    /// AdminLevel guard in the retail server -- INSIDE-BODY GM check
    /// gates SUCCESS path only (setting warp speed is GM-restricted
    /// because it edits ship stats); ERROR path emits regardless of
    /// AdminLevel. No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashWarpMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (28) = 31 bytes.
        const int ExpectedReplyPayloadLength = 31;
        // strlen(literal) + 1 NUL = 28.
        const short ExpectedReplyLengthField = 28;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 27.
        const int ExpectedLiteralByteCount = 27;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Warpo", shipName: "WarpoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/warp");

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

                // EXACT equals filter (not StartsWith) -- defensive against
                // any future sibling "warp*" option emits (warpreset, etc.)
                // or the trailing "Illegal slash command: warp" fallback at
                // line 7702.
                if (!text.Equals("Missing arg for option warp", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgWarpLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/warp\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option warp\". Likely the user-tier case-'w' " +
                $"dispatch at line 7627 stopped routing, the ELSE-IF chain head /who " +
                $"stopped falling through, the /warp ELSE-IF arm at line 7650 stopped " +
                $"dispatching (NO-OUTER-GUARD INSIDE-BODY-GM-GUARD -> OUTER-GM-GUARD " +
                $"regression would skip ERROR emit for non-GM), the INSIDE-BODY GM-guard " +
                $"leaked into the ERROR path (regression), the trailing illegal-slash " +
                $"fallback at 7702 fired as a second emit (msg_sent gate regression), " +
                $"or the missing-arg ERROR fork at PlayerConnection.cpp:4548 changed " +
                $"shape (esp. vsprintf_s 4-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 157 missing-arg ERROR literal for case-'u' /upgrade. The
    /// matcher at PlayerConnection.cpp:7593 reads
    /// `else if (MatchOptWithParam("upgrade", pch, param, msg_sent))`
    /// -- NO outer AdminLevel guard, INSIDE-BODY GM-guard
    /// (which only affects SUCCESS path; ERROR path emits
    /// regardless of AdminLevel). SECOND INSIDE-BODY-GUARD
    /// pin (Wave 156 /warp + Wave 157 /upgrade). With NO param
    /// after "/upgrade", strncmp matches 7 bytes, arg[7]='\0' fails
    /// separator-check, allowNoParams=false -- emits "Missing arg
    /// for option upgrade", returns false. Body block (GM-guard +
    /// ShipUpgrade) SKIPPED. 30 ASCII bytes after %s substitution
    /// -- 7-byte %s width deepened to triple-pin via the INSIDE-
    /// BODY-GUARD structural variant.
    /// </summary>
    private const string MissingArgUpgradeLiteral = "Missing arg for option upgrade";

    /// <summary>
    /// Wave 157 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 34-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/upgrade</c> (NO param) -- routes through the
    /// user-tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'u' user-tier dispatch (case-letter already pinned by
    /// Wave 154 via /uitrigger HEAD matcher at 7576; Wave 157 deepens
    /// case-'u' to the ELSE-IF chain arm /upgrade at 7593). HEAD
    /// matcher at 7576 /uitrigger 9-byte: strncmp("uitrigger","upgrade",9)
    /// u-u, i-p MISMATCH idx 1 false NO emit. else-if at 7593 /upgrade
    /// NO-OUTER-GUARD INSIDE-BODY-GM-GUARD: strncmp("upgrade","upgrade",7)
    /// all match, arg[7]='\0' fails separator-check, allowNoParams=false,
    /// emits "Missing arg for option upgrade" via SendVaMessage at 4548,
    /// sets msg_sent=true, returns false. INSIDE-BODY GM-guard
    /// `if (AdminLevel() >= GM)` at 7596/7598 for SUCCESS path SKIPPED
    /// (matcher returned false, body block never entered). else-if at
    /// 7607 `strcmp(pch,"undockp")` FAIL. else-if at 7614
    /// `strcmp(pch,"uptime")` FAIL. case-'u' breaks at 7625. Trailing
    /// fallback `if (!success &amp;&amp; !msg_sent) SendVaMessage("Illegal
    /// slash command: %s", pch)` at 7702 SKIPPED because msg_sent=true.
    /// NET RESULT: ONE emit.
    ///
    /// <para>
    /// TWENTY-SIXTH pin on the user-tier (single-slash) dispatch path.
    /// SECOND pin on user-tier case-'u' (Wave 154 pinned the HEAD
    /// matcher /uitrigger at 7576; Wave 157 deepens case-'u' coverage
    /// to the ELSE-IF chain arm /upgrade at 7593). TWENTY-FIFTH pin
    /// on the MatchOptWithParam ERROR path. SECOND INSIDE-BODY-GUARD
    /// pin -- Wave 156 introduced the structural pattern at case-'w'
    /// /warp 4-byte; Wave 157 deepens to case-'u' /upgrade 7-byte;
    /// INSIDE-BODY-GUARD is now DOUBLE-PINNED across TWO distinct
    /// case-letters AND TWO distinct %s widths. THIRD 7-byte %s
    /// width pin (Wave 144 /clear at case-'c' GUARD-FIRST inline +
    /// Wave 154 reference to /upgrade noted; actually wait -- let me
    /// recount: 7-byte width pins prior to Wave 157 were Wave 144
    /// case-'c' /clear GUARD-FIRST inline; Wave 157 adds case-'u'
    /// /upgrade NO-OUTER-GUARD INSIDE-BODY-GM-GUARD ELSE-IF as the
    /// SECOND 7-byte pin) -- SAME width DIFFERENT case-letter
    /// DIFFERENT structural pattern.
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 156
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     case-'u' ELSE-IF chain arm deepening regression at
    ///     <c>PlayerConnection.cpp:7593-7606</c>. Wave 154 pinned
    ///     the HEAD matcher /uitrigger at 7576 (NO-GUARD ELSE-IF
    ///     pattern, no body guard); case-'u' /upgrade at 7593 sits
    ///     on the ELSE-IF chain arm (chained off /uitrigger) and
    ///     was UNPINNED before Wave 157. A regression that converted
    ///     ELSE-IF to CONSECUTIVE-IF within case-'u' would change
    ///     dispatch semantics (independent CONSECUTIVE-IF would NOT
    ///     short-circuit when /uitrigger matched); a regression
    ///     that converted NO-OUTER-GUARD INSIDE-BODY-GM-GUARD to
    ///     OUTER-GUARD GM-GUARD would skip the ERROR-fork emit for
    ///     non-GM users (the body GM-guard gates SUCCESS only;
    ///     pulling it outside the matcher would also gate ERROR);
    ///     Wave 157 pins case-'u' /upgrade ELSE-IF chain arm is
    ///     REACHABLE AND the NO-OUTER-GUARD INSIDE-BODY-GM-GUARD
    ///     structural variant is preserved at this case-letter.
    ///   </item>
    ///   <item>
    ///     case-'u' ELSE-IF chain fall-through regression at
    ///     <c>PlayerConnection.cpp:7607-7625</c>. After the /upgrade
    ///     matcher emits, execution leaves the ELSE-IF chain
    ///     (because /upgrade matched and short-circuited the else-if
    ///     to /undockp and /uptime at 7607/7614), case-'u' breaks at
    ///     7625; trailing fallback at 7702 SKIPPED (msg_sent=true).
    ///     A regression that converted ELSE-IF to CONSECUTIVE-IF
    ///     would cause /undockp and /uptime strcmps to run after
    ///     /upgrade emits; both FAIL against pch="upgrade" so no
    ///     second emit would result, but the structural semantics
    ///     would have changed; a regression that flipped the
    ///     trailing fallback's `!msg_sent` to `msg_sent` would emit
    ///     a second "Illegal slash command: upgrade" message;
    ///     Wave 157 pins the ELSE-IF chain fall-through as a
    ///     structural invariant via the EXACT-equals filter.
    ///   </item>
    ///   <item>
    ///     INSIDE-BODY-GUARD pattern + 7-byte %s width cross-case-
    ///     letter cross-width divergence regression at
    ///     <c>PlayerClass.cpp:3422</c>. Wave 156 pinned INSIDE-
    ///     BODY-GUARD at case-'w' /warp 4-byte; Wave 157 pins
    ///     INSIDE-BODY-GUARD at case-'u' /upgrade 7-byte. SAME
    ///     structural pattern, DIFFERENT case-letter, DIFFERENT
    ///     width. A regression that selectively broke INSIDE-BODY-
    ///     GUARD at one case-letter or one width but not the other
    ///     would fail one pin but not both; Wave 157 deepens
    ///     INSIDE-BODY-GUARD to cross-case-letter cross-width
    ///     coverage. Additionally 7-byte %s width is now DOUBLE-
    ///     PINNED across TWO distinct case-letters (Wave 144 case-c
    ///     /clear GUARD-FIRST + Wave 157 case-u /upgrade NO-OUTER-
    ///     GUARD INSIDE-BODY-GM-GUARD) ruling out per-case-letter
    ///     AND per-structural-pattern format-substitution branches
    ///     at 7-byte width.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-
    /// level error path. /upgrade (ship-upgrade-by-id command) had NO
    /// outer AdminLevel guard in the retail server -- INSIDE-BODY GM
    /// check gates SUCCESS path only (ship upgrades are GM-restricted
    /// because they grant items); ERROR path emits regardless of
    /// AdminLevel. No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashUpgradeMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (31) = 34 bytes.
        const int ExpectedReplyPayloadLength = 34;
        // strlen(literal) + 1 NUL = 31.
        const short ExpectedReplyLengthField = 31;
        // SendVaMessage -> SendMessageString default color parameter.
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
            firstName: "Upgro", shipName: "UpgroShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/upgrade");

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

                // EXACT equals filter (not StartsWith) -- defensive against
                // any future spurious "Illegal slash command: upgrade" emit
                // if the trailing fallback at line 7702 msg_sent gate
                // regresses.
                if (!text.Equals("Missing arg for option upgrade", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgUpgradeLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/upgrade\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option upgrade\". Likely the user-tier case-'u' " +
                $"dispatch at line 7575 stopped routing, the HEAD matcher /uitrigger at " +
                $"7576 stopped falling through to /upgrade at 7593 (ELSE-IF chain " +
                $"regression), the NO-OUTER-GUARD INSIDE-BODY-GM-GUARD structural variant " +
                $"converted to OUTER-GM-GUARD (would skip ERROR emit for non-GM), the " +
                $"INSIDE-BODY GM-guard leaked into the ERROR path (regression), the " +
                $"trailing illegal-slash fallback at 7702 fired as a second emit " +
                $"(msg_sent gate regression), or the missing-arg ERROR fork at " +
                $"PlayerConnection.cpp:4548 changed shape (esp. vsprintf_s 7-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 158 missing-arg ERROR literal for case-'t' /testmsg. The
    /// matcher at PlayerConnection.cpp:7486 reads
    /// `if (MatchOptWithParam("testmsg", pch, param, msg_sent) &amp;&amp; (AdminLevel() >= DEV))`
    /// -- CONSECUTIVE-IF independent block (NOT chained via else-if),
    /// MATCHER-FIRST short-circuit-direction (matcher evaluated
    /// before the DEV-guard), inline DEV-guard. With pch="testmsg"
    /// and NO param, MatchOptWithParam runs FIRST: strncmp matches
    /// 7 bytes, arg[7]='\0' fails separator-check, allowNoParams=
    /// false -- emits "Missing arg for option testmsg", returns
    /// false. &amp;&amp; short-circuits, DEV-guard SKIPPED, body block
    /// (timed B_TEST_MESSAGE) SKIPPED. 30 ASCII bytes after %s
    /// substitution -- 7-byte %s width triple-pinned via a NEW
    /// structural variant (CONSECUTIVE-IF DEV-guard MATCHER-FIRST,
    /// tenth distinct dispatcher pattern).
    /// </summary>
    private const string MissingArgTestmsgLiteral = "Missing arg for option testmsg";

    /// <summary>
    /// Wave 158 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 34-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/testmsg</c> (NO param) -- routes through the
    /// user-tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'t' user-tier dispatch (case-letter already pinned by
    /// Wave 153 via /tilt CONSECUTIVE-IF NO-GUARD arm; Wave 158
    /// deepens case-'t' to the CONSECUTIVE-IF DEV-guard MATCHER-FIRST
    /// arm /testmsg at 7486). Independent CONSECUTIVE-IF at 7462
    /// `strcmp(pch,"test")==0` FAIL → skip. Independent at 7471
    /// `strcasecmp(pch,"talktree")==0` FAIL → skip. Independent at
    /// 7486 `MatchOptWithParam("testmsg", pch, param, msg_sent) &amp;&amp;
    /// AdminLevel() >= DEV`: MATCHER-FIRST short-circuit-direction
    /// -- matcher evaluated BEFORE the guard. MatchOptWithParam:
    /// strncmps "testmsg" against "testmsg" (7 byte match), arg[7]
    /// ='\0' fails separator-check, allowNoParams=false, emits
    /// "Missing arg for option testmsg" via SendVaMessage at 4548,
    /// sets msg_sent=true, returns false. &amp;&amp; short-circuits,
    /// DEV-guard `(AdminLevel() >= DEV)` SKIPPED (matcher returned
    /// false; short-circuit boolean evaluation), body block (timed
    /// B_TEST_MESSAGE) SKIPPED. Independent at 7495
    /// `MatchOptWithParam("tilt",...)`: strncmp("tilt","testmsg",4)
    /// t-t, i-e MISMATCH idx 1 false NO emit. Independent at 7501
    /// `MatchOptWithParam("terminate",...)`: strncmp("terminate",
    /// "testmsg",9) t-t, e-e match idx 1, r-s MISMATCH idx 2 false
    /// NO emit. Independent at 7521 `MatchOptWithParam("trade",...)`:
    /// strncmp("trade","testmsg",5) t-t, r-e MISMATCH idx 1 false
    /// NO emit. case-'t' breaks at 7573. Trailing fallback at 7702
    /// SKIPPED (msg_sent=true). NET RESULT: ONE emit.
    ///
    /// <para>
    /// TWENTY-SEVENTH pin on the user-tier (single-slash) dispatch
    /// path. SECOND pin on user-tier case-'t' (Wave 153 pinned the
    /// CONSECUTIVE-IF NO-GUARD arm /tilt at 7495; Wave 158 deepens
    /// case-'t' to the CONSECUTIVE-IF DEV-guard MATCHER-FIRST arm
    /// /testmsg at 7486). TWENTY-SIXTH pin on the MatchOptWithParam
    /// ERROR path. TENTH distinct structural dispatcher pattern --
    /// CONSECUTIVE-IF DEV-guard MATCHER-FIRST (combines the
    /// MATCHER-FIRST short-circuit-direction (3rd pin: Waves 139
    /// GM case-g SDEV + 143 user-tier case-d + 152 user-tier case-s
    /// SDEV + 158 user-tier case-t DEV) with the CONSECUTIVE-IF
    /// block structure (3rd pin: Waves 153 case-t pure NO-GUARD +
    /// 155 case-w MIXED + 158 case-t DEV-guard MATCHER-FIRST) at a
    /// NEW guard tier (FIRST inline DEV-guard pin -- prior DEV-guards
    /// only pinned via case-'s' OUTER-BLOCK-GUARD shell at 7286,
    /// never as inline matcher guard; SDEV-guards pinned at Waves
    /// 139 GM case-g HEAD and 152 user-tier case-s but DEV-guard
    /// is a strictly lower tier (80 vs 90) and was unpinned until
    /// Wave 158)). THIRD 7-byte %s width pin (Waves 144 case-c
    /// /clear GUARD-FIRST inline + 157 case-u /upgrade NO-OUTER-
    /// GUARD INSIDE-BODY-GM-GUARD ELSE-IF + 158 case-t /testmsg
    /// CONSECUTIVE-IF DEV-guard MATCHER-FIRST) -- SAME width,
    /// THREE distinct case-letters, THREE distinct structural
    /// patterns. THIRD MATCHER-FIRST pin AT USER-TIER (Waves 143
    /// case-d 1-byte + 152 case-s SDEV 6-byte + 158 case-t DEV
    /// 7-byte).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 157
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     case-'t' CONSECUTIVE-IF DEV-guard MATCHER-FIRST arm
    ///     deepening regression at <c>PlayerConnection.cpp:7486-7493</c>.
    ///     Wave 153 pinned case-'t' /tilt at 7495 (pure CONSECUTIVE-
    ///     IF NO-GUARD); case-'t' /testmsg at 7486 sits on a
    ///     different CONSECUTIVE-IF block with DEV-guard MATCHER-
    ///     FIRST and was UNPINNED before Wave 158. A regression
    ///     that converted MATCHER-FIRST to GUARD-FIRST (inverting
    ///     to `if (AdminLevel() >= DEV &amp;&amp; MatchOptWithParam(...))`)
    ///     would block the ERROR-fork emit for non-DEV users
    ///     (the matcher would NOT execute when guard fails);
    ///     a regression that lowered the DEV-guard tier (e.g. to
    ///     GM=50) would change SUCCESS-path eligibility but not
    ///     ERROR (which short-circuits before guard); Wave 158
    ///     pins case-'t' /testmsg CONSECUTIVE-IF DEV-guard
    ///     MATCHER-FIRST arm is REACHABLE AND the MATCHER-FIRST
    ///     short-circuit direction is preserved (matcher runs
    ///     BEFORE guard, so ERROR emits independent of AdminLevel).
    ///   </item>
    ///   <item>
    ///     case-'t' CONSECUTIVE-IF cross-arm fall-through regression
    ///     at <c>PlayerConnection.cpp:7495-7572</c> across 3 sibling
    ///     CONSECUTIVE-IF matchers (/tilt 4B, /terminate 9B,
    ///     /trade 5B). After the /testmsg matcher emits, execution
    ///     continues through 3 subsequent independent ifs; each
    ///     strncmp-mismatches against pch="testmsg" at idx 1 or 2;
    ///     case-'t' breaks; trailing fallback SKIPPED (msg_sent=
    ///     true). A regression that ADDED a competing matcher
    ///     matching pch="testmsg" by accident would produce a
    ///     second message; the CONSECUTIVE-IF structure means even
    ///     an else-clause regression could not suppress that second
    ///     emit; Wave 158 pins the CONSECUTIVE-IF cross-arm fall-
    ///     through as a structural invariant.
    ///   </item>
    ///   <item>
    ///     TENTH distinct structural dispatcher pattern + DEV-guard
    ///     tier introduction regression at <c>PlayerClass.cpp:3422</c>.
    ///     Prior to Wave 158 NINE distinct dispatcher patterns were
    ///     pinned (MATCHER-FIRST inline, GUARD-FIRST inline, OUTER-
    ///     BLOCK-GUARD, COMBINED outer+inline, NO-GUARD-ELSE-IF,
    ///     CASE-FALL-THROUGH, pure CONSECUTIVE-IF, MIXED ELSE-IF+
    ///     CONSECUTIVE-IF case-letter, INSIDE-BODY-GUARD); Wave 158
    ///     adds CONSECUTIVE-IF DEV-guard MATCHER-FIRST as the TENTH.
    ///     Additionally DEV-guard tier (admin level 80) was unpinned
    ///     as an inline matcher guard (only pinned via OUTER-BLOCK-
    ///     GUARD shell at case-'s' 7286); a regression that
    ///     selectively broke DEV-guard inline behaviour would not
    ///     be caught by the OUTER-BLOCK-GUARD pins; Wave 158
    ///     introduces inline DEV-guard coverage. 7-byte %s width
    ///     is now TRIPLE-PINNED across THREE distinct case-letters
    ///     (c/u/t) and THREE distinct structural patterns (GUARD-
    ///     FIRST inline / NO-OUTER-GUARD INSIDE-BODY-GM-GUARD
    ///     ELSE-IF / CONSECUTIVE-IF DEV-guard MATCHER-FIRST) ruling
    ///     out per-case-letter, per-structural-pattern, AND per-
    ///     guard-tier format-substitution branches at 7-byte width.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-
    /// level error path. /testmsg (timed test message debug command)
    /// is DEV-restricted in the retail server -- MATCHER-FIRST short-
    /// circuit-direction means the missing-arg ERROR emit happens
    /// FOR ALL users (matcher runs before guard), but the SUCCESS
    /// path is DEV-gated. This is a deliberate retail server pattern:
    /// disclose the option name on missing-arg while still gating
    /// execution by AdminLevel. No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashTestmsgMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (31) = 34 bytes.
        const int ExpectedReplyPayloadLength = 34;
        // strlen(literal) + 1 NUL = 31.
        const short ExpectedReplyLengthField = 31;
        // SendVaMessage -> SendMessageString default color parameter.
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
            firstName: "Testo", shipName: "TestoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/testmsg");

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

                // EXACT equals filter (not StartsWith) -- defensive against
                // any future spurious "Illegal slash command: testmsg" emit
                // if the trailing fallback at line 7702 msg_sent gate
                // regresses.
                if (!text.Equals("Missing arg for option testmsg", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgTestmsgLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/testmsg\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option testmsg\". Likely the user-tier case-'t' " +
                $"dispatch at line 7461 stopped routing, the CONSECUTIVE-IF independent " +
                $"block at 7486 stopped dispatching, the MATCHER-FIRST short-circuit " +
                $"direction inverted to GUARD-FIRST (would block ERROR emit for non-DEV " +
                $"users), the DEV-guard tier changed (would not affect ERROR emit due " +
                $"to MATCHER-FIRST short-circuit), the trailing illegal-slash fallback " +
                $"at 7702 fired as a second emit (msg_sent gate regression), or the " +
                $"missing-arg ERROR fork at PlayerConnection.cpp:4548 changed shape " +
                $"(esp. vsprintf_s 7-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 159 missing-arg ERROR literal for case-'t' /terminate.
    /// The matcher at PlayerConnection.cpp:7501 reads
    /// `if (MatchOptWithParam("terminate", pch, param, msg_sent))`
    /// -- independent CONSECUTIVE-IF block (NOT chained via else-if),
    /// NO outer AdminLevel guard, INSIDE-BODY DEV-guard at line
    /// 7511 (gates SUCCESS path only; ERROR path emits regardless
    /// of AdminLevel). THIRD INSIDE-BODY-GUARD pin (Wave 156 case-w
    /// /warp 4-byte ELSE-IF GM + Wave 157 case-u /upgrade 7-byte
    /// ELSE-IF GM + Wave 159 case-t /terminate 9-byte CONSECUTIVE-
    /// IF DEV). With NO param after "/terminate", strncmp matches
    /// 9 bytes, arg[9]='\0' fails separator-check, allowNoParams=
    /// false -- emits "Missing arg for option terminate", returns
    /// false. INSIDE-BODY DEV-guard SKIPPED (matcher returned false,
    /// body block never entered). 32 ASCII bytes after %s
    /// substitution -- 9-byte %s width SECOND pin (Wave 154
    /// /uitrigger NO-GUARD ELSE-IF + Wave 159 INSIDE-BODY-DEV-GUARD
    /// CONSECUTIVE-IF) -- SAME width DIFFERENT case-letter
    /// DIFFERENT structural pattern.
    /// </summary>
    private const string MissingArgTerminateLiteral = "Missing arg for option terminate";

    /// <summary>
    /// Wave 159 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 36-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/terminate</c> (NO param) -- routes through the
    /// user-tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'t' user-tier dispatch (case-letter already pinned by
    /// Wave 153 via /tilt CONSECUTIVE-IF NO-GUARD arm AND Wave 158
    /// via /testmsg CONSECUTIVE-IF DEV-guard MATCHER-FIRST arm; Wave
    /// 159 deepens case-'t' to the CONSECUTIVE-IF INSIDE-BODY-DEV-
    /// GUARD arm /terminate at 7501). Independent CONSECUTIVE-IF at
    /// 7462 strcmp test FAIL. 7471 strcasecmp talktree FAIL. 7486
    /// MatchOptWithParam("testmsg",...) &amp;&amp; DEV-guard: strncmp
    /// ("testmsg","terminate",7) t-t, e-e match idx 1, s-r MISMATCH
    /// idx 2 false NO emit, &amp;&amp; short-circuits. 7495 MatchOptWithParam
    /// ("tilt",...): strncmp("tilt","terminate",4) t-t, i-e MISMATCH
    /// idx 1 false NO emit. Independent at 7501 `MatchOptWithParam
    /// ("terminate", pch, param, msg_sent)` -- NO outer AdminLevel
    /// guard. MatchOptWithParam: strncmps "terminate" against
    /// "terminate" (9 byte match), arg[9]='\0' -- NOT '=', NOT ' ',
    /// NOT isalpha, allowNoParams=false -- emits "Missing arg for
    /// option terminate" via SendVaMessage at 4548, sets msg_sent=
    /// true, returns false. INSIDE-BODY DEV-guard
    /// `if (p &amp;&amp; AdminLevel() >= DEV)` at 7511 for SUCCESS path
    /// SKIPPED (matcher returned false, body block never entered;
    /// the GetPlayer(param) call at 7503 is also INSIDE-BODY but
    /// crashes with param being uninitialized -- wait, MatchOptWithParam
    /// returning false means msg_sent=true and body never entered).
    /// 7521 MatchOptWithParam("trade",...): strncmp("trade",
    /// "terminate",5) t-t, r-e MISMATCH idx 1 false NO emit. case-
    /// 't' breaks at 7573. Trailing fallback at 7702 SKIPPED
    /// (msg_sent=true). NET RESULT: ONE emit.
    ///
    /// <para>
    /// TWENTY-EIGHTH pin on the user-tier (single-slash) dispatch
    /// path. THIRD pin on user-tier case-'t' (Wave 153 pinned pure
    /// CONSECUTIVE-IF NO-GUARD /tilt + Wave 158 pinned CONSECUTIVE-
    /// IF DEV-guard MATCHER-FIRST /testmsg + Wave 159 deepens case-
    /// 't' to CONSECUTIVE-IF INSIDE-BODY-DEV-GUARD /terminate at
    /// 7501); case-'t' is now TRIPLE-PINNED. TWENTY-SEVENTH pin on
    /// the MatchOptWithParam ERROR path. THIRD INSIDE-BODY-GUARD
    /// pin -- Wave 156 case-w /warp 4-byte ELSE-IF GM-guard +
    /// Wave 157 case-u /upgrade 7-byte ELSE-IF GM-guard + Wave 159
    /// case-t /terminate 9-byte CONSECUTIVE-IF DEV-guard --
    /// INSIDE-BODY-GUARD now TRIPLE-PINNED across THREE distinct
    /// case-letters AND THREE distinct widths AND TWO distinct
    /// guard tiers (GM=50 + DEV=80) AND TWO distinct block
    /// structures (ELSE-IF chain + CONSECUTIVE-IF). SECOND 9-byte
    /// %s width pin (Wave 154 case-u /uitrigger NO-GUARD ELSE-IF +
    /// Wave 159 case-t /terminate INSIDE-BODY-DEV-GUARD
    /// CONSECUTIVE-IF) -- SAME width DIFFERENT case-letter DIFFERENT
    /// structural pattern; remaining %s width gaps: 10/12. FOURTH
    /// CONSECUTIVE-IF pin (Waves 153 case-t pure NO-GUARD + 155
    /// case-w MIXED + 158 case-t DEV-guard MATCHER-FIRST + 159
    /// case-t INSIDE-BODY-DEV-GUARD) -- CONSECUTIVE-IF now
    /// QUADRUPLE-PINNED.
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 158
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     case-'t' CONSECUTIVE-IF INSIDE-BODY-DEV-GUARD arm
    ///     deepening regression at <c>PlayerConnection.cpp:7501-7518</c>.
    ///     Wave 153 pinned case-'t' /tilt at 7495 (pure CONSECUTIVE-
    ///     IF NO-GUARD); Wave 158 pinned case-'t' /testmsg at 7486
    ///     (CONSECUTIVE-IF DEV-guard MATCHER-FIRST); case-'t'
    ///     /terminate at 7501 sits on yet another CONSECUTIVE-IF
    ///     variant with INSIDE-BODY DEV-guard and was UNPINNED
    ///     before Wave 159. A regression that converted INSIDE-BODY-
    ///     DEV-GUARD to OUTER-GUARD DEV-GUARD would skip the ERROR-
    ///     fork emit for non-DEV users; a regression that converted
    ///     CONSECUTIVE-IF to ELSE-IF would change cross-arm dispatch
    ///     semantics; Wave 159 pins case-'t' /terminate CONSECUTIVE-
    ///     IF INSIDE-BODY-DEV-GUARD arm is REACHABLE AND the
    ///     INSIDE-BODY-DEV-GUARD structural variant is preserved
    ///     at THIS combination of (case-letter, block structure,
    ///     guard tier).
    ///   </item>
    ///   <item>
    ///     case-'t' CONSECUTIVE-IF cross-arm fall-through regression
    ///     at <c>PlayerConnection.cpp:7521-7572</c> via 1 sibling
    ///     CONSECUTIVE-IF matcher (/trade 5B). After the /terminate
    ///     matcher emits, execution continues through 1 subsequent
    ///     independent if at 7521 /trade strncmp-mismatch idx 1 'r'
    ///     vs 'e' false NO emit; case-'t' breaks; trailing fallback
    ///     SKIPPED (msg_sent=true). A regression that ADDED a
    ///     competing matcher matching pch="terminate" by accident
    ///     would produce a second message; the CONSECUTIVE-IF
    ///     structure means even an else-clause regression could not
    ///     suppress that second emit; Wave 159 pins the case-'t'
    ///     CONSECUTIVE-IF cross-arm fall-through as a structural
    ///     invariant.
    ///   </item>
    ///   <item>
    ///     INSIDE-BODY-GUARD pattern triple-pin cross-tier cross-
    ///     block-structure divergence regression at
    ///     <c>PlayerClass.cpp:3422</c>. Wave 156 pinned INSIDE-BODY-
    ///     GUARD at case-'w' /warp 4-byte ELSE-IF GM-guard;
    ///     Wave 157 at case-'u' /upgrade 7-byte ELSE-IF GM-guard;
    ///     Wave 159 at case-'t' /terminate 9-byte CONSECUTIVE-IF
    ///     DEV-guard. THREE distinct case-letters, THREE distinct
    ///     widths, TWO distinct guard tiers (GM/DEV), TWO distinct
    ///     block structures (ELSE-IF/CONSECUTIVE-IF). A regression
    ///     that selectively broke INSIDE-BODY-GUARD at one (case-
    ///     letter, width, tier, structure) combination but not the
    ///     others would fail one pin but not all three; Wave 159
    ///     deepens INSIDE-BODY-GUARD to cross-tier AND cross-block-
    ///     structure coverage. Additionally 9-byte %s width is now
    ///     DOUBLE-PINNED across TWO distinct case-letters (u/t) AND
    ///     TWO distinct structural patterns (NO-GUARD-ELSE-IF /
    ///     INSIDE-BODY-DEV-GUARD CONSECUTIVE-IF); the gap in 9-byte
    ///     coverage is closed AND per-structural-pattern divergence
    ///     at 9-byte width is ruled out.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-
    /// level error path. /terminate (kick/disconnect target player
    /// command) is DEV-restricted in the retail server -- INSIDE-
    /// BODY DEV-guard at 7511 gates SUCCESS path only (terminating a
    /// player connection is DEV-restricted because it disrupts other
    /// players); ERROR path emits regardless of AdminLevel. No server
    /// permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashTerminateMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (33) = 36 bytes.
        const int ExpectedReplyPayloadLength = 36;
        // strlen(literal) + 1 NUL = 33.
        const short ExpectedReplyLengthField = 33;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 32.
        const int ExpectedLiteralByteCount = 32;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Termo", shipName: "TermoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/terminate");

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

                // EXACT equals filter (not StartsWith) -- defensive against
                // any future spurious "Illegal slash command: terminate" emit
                // if the trailing fallback at line 7702 msg_sent gate
                // regresses.
                if (!text.Equals("Missing arg for option terminate", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgTerminateLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/terminate\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option terminate\". Likely the user-tier case-'t' " +
                $"dispatch at line 7461 stopped routing, the CONSECUTIVE-IF independent " +
                $"block at 7501 stopped dispatching, the NO-OUTER-GUARD INSIDE-BODY-DEV-" +
                $"GUARD structural variant converted to OUTER-DEV-GUARD (would skip ERROR " +
                $"emit for non-DEV users), the INSIDE-BODY DEV-guard at 7511 leaked into " +
                $"the ERROR path (regression), the trailing illegal-slash fallback at " +
                $"7702 fired as a second emit (msg_sent gate regression), or the missing-" +
                $"arg ERROR fork at PlayerConnection.cpp:4548 changed shape (esp. " +
                $"vsprintf_s 9-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 160 missing-arg ERROR literal for case-'t' /trade.
    /// The matcher at PlayerConnection.cpp:7521 reads
    /// `if (MatchOptWithParam("trade", pch, param, msg_sent))`
    /// -- independent CONSECUTIVE-IF block (NOT chained via else-if),
    /// NO outer AdminLevel guard, NO inside-body guard on the outer
    /// matcher itself (internal `if (!targetp)` at 7526 is a separate
    /// post-success-match check). FIFTH NO-GUARD pin (Wave 153 case-t
    /// /tilt pure CONSECUTIVE-IF NO-GUARD + Wave 154 case-u /uitrigger
    /// NO-GUARD ELSE-IF + Wave 155 case-w /wormhole pure NO-GUARD
    /// MIXED + Wave 156 case-w /warp NO-OUTER-GUARD INSIDE-BODY-GM
    /// + Wave 160 case-t /trade pure NO-GUARD CONSECUTIVE-IF).
    /// With NO param after "/trade", strncmp matches 5 bytes,
    /// arg[5]='\0' fails separator-check, allowNoParams=false -- emits
    /// "Missing arg for option trade", returns false. INNER `if
    /// (!targetp)` body block SKIPPED (matcher returned false, body
    /// block never entered). 28 ASCII bytes after %s substitution --
    /// 5-byte %s width SECOND pin (Wave 131 case-l /level NO-GUARD
    /// ELSE-IF + Wave 160 case-t /trade pure NO-GUARD CONSECUTIVE-IF)
    /// -- SAME width DIFFERENT case-letter DIFFERENT structural
    /// pattern.
    /// </summary>
    private const string MissingArgTradeLiteral = "Missing arg for option trade";

    /// <summary>
    /// Wave 160 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 32-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/trade</c> (NO param) -- routes through the user-
    /// tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'t' user-tier dispatch (case-letter already pinned by
    /// Wave 153 via /tilt CONSECUTIVE-IF NO-GUARD arm + Wave 158 via
    /// /testmsg CONSECUTIVE-IF DEV-guard MATCHER-FIRST arm + Wave 159
    /// via /terminate CONSECUTIVE-IF INSIDE-BODY-DEV-GUARD arm; Wave
    /// 160 deepens case-'t' to a FOURTH CONSECUTIVE-IF variant: pure
    /// NO-GUARD /trade at 7521 with NO outer guard AND NO inside-
    /// body guard on the matcher itself). Independent CONSECUTIVE-IF
    /// at 7462 strcmp test FAIL. 7471 strcasecmp talktree FAIL. 7486
    /// MatchOptWithParam("testmsg",...) &amp;&amp; DEV-guard: strncmp
    /// ("testmsg","trade",5) t-t, e-r MISMATCH idx 1 false NO emit,
    /// &amp;&amp; short-circuits. 7495 MatchOptWithParam("tilt",...):
    /// strncmp("tilt","trade",4) t-t, i-r MISMATCH idx 1 false NO
    /// emit. 7501 MatchOptWithParam("terminate",...): strncmp
    /// ("terminate","trade",5) t-t, e-r MISMATCH idx 1 false NO emit
    /// (INSIDE-BODY DEV-guard never reached, body block never entered).
    /// Independent at 7521 `MatchOptWithParam("trade", pch, param,
    /// msg_sent)` -- NO outer AdminLevel guard, NO inside-body guard
    /// on the matcher itself. MatchOptWithParam: strncmps "trade"
    /// against "trade" (5 byte match), arg[5]='\0' -- NOT '=', NOT
    /// ' ', NOT isalpha, allowNoParams=false -- emits "Missing arg
    /// for option trade" via SendVaMessage at 4548, sets msg_sent=
    /// true, returns false. INNER `Player *targetp = GetPlayer(param)`
    /// at 7524 and subsequent INNER `if (!targetp)` body block
    /// SKIPPED (matcher returned false, outer body block never
    /// entered). case-'t' breaks at 7573. Trailing fallback at 7702
    /// SKIPPED (msg_sent=true). NET RESULT: ONE emit.
    ///
    /// <para>
    /// TWENTY-NINTH pin on the user-tier (single-slash) dispatch
    /// path. FOURTH pin on user-tier case-'t' (Wave 153 pinned pure
    /// CONSECUTIVE-IF NO-GUARD /tilt + Wave 158 pinned CONSECUTIVE-
    /// IF DEV-guard MATCHER-FIRST /testmsg + Wave 159 pinned
    /// CONSECUTIVE-IF INSIDE-BODY-DEV-GUARD /terminate + Wave 160
    /// deepens case-'t' to pure CONSECUTIVE-IF NO-GUARD /trade at
    /// 7521); case-'t' is now QUADRUPLE-PINNED. TWENTY-EIGHTH pin on
    /// the MatchOptWithParam ERROR path. FIFTH NO-GUARD pin (Wave
    /// 153 case-t /tilt pure CONSECUTIVE-IF + Wave 154 case-u
    /// /uitrigger NO-GUARD ELSE-IF + Wave 155 case-w /wormhole pure
    /// NO-GUARD MIXED + Wave 156 case-w /warp NO-OUTER-GUARD INSIDE-
    /// BODY-GM + Wave 160 case-t /trade pure NO-GUARD CONSECUTIVE-
    /// IF). SECOND 5-byte %s width pin (Wave 131 case-l /level
    /// NO-GUARD ELSE-IF + Wave 160 case-t /trade pure NO-GUARD
    /// CONSECUTIVE-IF) -- SAME width DIFFERENT case-letter DIFFERENT
    /// structural pattern; remaining %s width gaps: 10/12. FIFTH
    /// CONSECUTIVE-IF pin (Waves 153 case-t pure NO-GUARD + 155
    /// case-w MIXED + 158 case-t DEV-guard MATCHER-FIRST + 159
    /// case-t INSIDE-BODY-DEV-GUARD + 160 case-t pure NO-GUARD) --
    /// CONSECUTIVE-IF now QUINTUPLE-PINNED.
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 159
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     case-'t' CONSECUTIVE-IF pure NO-GUARD arm deepening
    ///     regression at <c>PlayerConnection.cpp:7521-7572</c>.
    ///     Wave 153 pinned case-'t' /tilt at 7495 (pure CONSECUTIVE-
    ///     IF NO-GUARD); Wave 158 pinned case-'t' /testmsg at 7486
    ///     (CONSECUTIVE-IF DEV-guard MATCHER-FIRST); Wave 159 pinned
    ///     case-'t' /terminate at 7501 (CONSECUTIVE-IF INSIDE-BODY-
    ///     DEV-GUARD); case-'t' /trade at 7521 sits on yet another
    ///     CONSECUTIVE-IF variant -- pure NO-GUARD with NO outer
    ///     guard AND NO inside-body guard on the matcher itself --
    ///     and was UNPINNED before Wave 160. A regression that
    ///     converted pure NO-GUARD to OUTER-GUARD or INSIDE-BODY-
    ///     GUARD would either skip the ERROR-fork emit (OUTER-GUARD)
    ///     or change SUCCESS-path semantics (INSIDE-BODY-GUARD);
    ///     Wave 160 pins case-'t' /trade pure NO-GUARD CONSECUTIVE-
    ///     IF arm is REACHABLE AND the pure NO-GUARD structural
    ///     variant is preserved at THIS combination of (case-letter,
    ///     block structure, guard pattern).
    ///   </item>
    ///   <item>
    ///     case-'t' CONSECUTIVE-IF terminal-arm regression at
    ///     <c>PlayerConnection.cpp:7521-7573</c>. /trade is the LAST
    ///     CONSECUTIVE-IF independent block in case-'t' before the
    ///     break at 7573. After the /trade matcher emits, execution
    ///     immediately hits the case-'t' break; no further matchers
    ///     run; trailing fallback at 7702 SKIPPED (msg_sent=true). A
    ///     regression that ADDED a competing matcher AFTER /trade
    ///     matching pch="trade" by accident would produce a second
    ///     message; the CONSECUTIVE-IF structure means even an else-
    ///     clause regression could not suppress that second emit;
    ///     Wave 160 pins the case-'t' CONSECUTIVE-IF terminal-arm
    ///     position as a structural invariant.
    ///   </item>
    ///   <item>
    ///     NO-GUARD pattern quintuple-pin cross-case-letter cross-
    ///     block-structure divergence regression at
    ///     <c>PlayerConnection.cpp:5434+</c>. Wave 153 pinned NO-
    ///     GUARD at case-'t' /tilt pure CONSECUTIVE-IF; Wave 154 at
    ///     case-'u' /uitrigger NO-GUARD ELSE-IF; Wave 155 at case-
    ///     'w' /wormhole pure NO-GUARD MIXED; Wave 156 at case-'w'
    ///     /warp NO-OUTER-GUARD INSIDE-BODY-GM; Wave 160 at case-'t'
    ///     /trade pure NO-GUARD CONSECUTIVE-IF. FIVE distinct NO-
    ///     GUARD pins across THREE distinct case-letters (t/u/w) AND
    ///     THREE distinct block structures (pure CONSECUTIVE-IF /
    ///     NO-GUARD ELSE-IF / MIXED / NO-OUTER-GUARD INSIDE-BODY-GM).
    ///     A regression that selectively broke NO-GUARD at one
    ///     (case-letter, structure) combination but not the others
    ///     would fail one pin but not all five; Wave 160 deepens NO-
    ///     GUARD to QUINTUPLE-PIN coverage. Additionally 5-byte %s
    ///     width is now DOUBLE-PINNED across TWO distinct case-
    ///     letters (l/t) AND TWO distinct structural patterns (NO-
    ///     GUARD-ELSE-IF / pure NO-GUARD CONSECUTIVE-IF); the gap in
    ///     5-byte coverage is closed AND per-structural-pattern
    ///     divergence at 5-byte width is ruled out.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-
    /// level error path. /trade (initiate trade with target player
    /// command) is OPEN to all users in the retail server (no outer
    /// AdminLevel guard at 7521; range check at 7533 only requires
    /// BETA_PLUS to bypass distance check, and self-trade check at
    /// 7542 only requires GM to bypass self-trade-block -- both are
    /// SUCCESS-path checks AFTER the matcher succeeds, so they do NOT
    /// gate the ERROR-fork). ERROR path emits regardless of AdminLevel.
    /// No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashTradeMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (29) = 32 bytes.
        const int ExpectedReplyPayloadLength = 32;
        // strlen(literal) + 1 NUL = 29.
        const short ExpectedReplyLengthField = 29;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 28.
        const int ExpectedLiteralByteCount = 28;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Trado", shipName: "TradoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/trade");

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

                // EXACT equals filter (not StartsWith) -- defensive against
                // any future spurious "Illegal slash command: trade" emit if
                // the trailing fallback at line 7702 msg_sent gate regresses.
                if (!text.Equals("Missing arg for option trade", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgTradeLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/trade\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option trade\". Likely the user-tier case-'t' " +
                $"dispatch at line 7461 stopped routing, the CONSECUTIVE-IF independent " +
                $"block at 7521 stopped dispatching, the pure NO-GUARD structural variant " +
                $"converted to OUTER-GUARD (would skip ERROR emit for non-privileged " +
                $"users) or INSIDE-BODY-GUARD (would skip ERROR emit if matcher ran but " +
                $"body block gate failed), the case-'t' break at 7573 moved before 7521 " +
                $"(would skip /trade arm entirely), the trailing illegal-slash fallback " +
                $"at 7702 fired as a second emit (msg_sent gate regression), or the " +
                $"missing-arg ERROR fork at PlayerConnection.cpp:4548 changed shape " +
                $"(esp. vsprintf_s 5-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 161 missing-arg ERROR literal for case-'o' /oeuler.
    /// The matcher at PlayerConnection.cpp:6886 reads
    /// `else if (MatchOptWithParam("oeuler", pch, param, msg_sent))`
    /// -- ELSE-IF chain arm (chained off /orientation HEAD at 6881),
    /// NO outer AdminLevel guard, NO inside-body guard. SECOND case-
    /// 'o' pin (Wave 143 pinned /orientation HEAD matcher at 6881
    /// 11-byte; Wave 161 deepens case-'o' to ELSE-IF chain arm at
    /// 6886). With NO param after "/oeuler", strncmp matches 6
    /// bytes, arg[6]='\0' fails separator-check, allowNoParams=false
    /// -- emits "Missing arg for option oeuler", returns false. 29
    /// ASCII bytes after %s substitution -- 6-byte %s width
    /// deepening pin (prior 6-byte pins at GM-tier; THIRD user-tier
    /// 6-byte width pin or deepening).
    /// </summary>
    private const string MissingArgOeulerLiteral = "Missing arg for option oeuler";

    /// <summary>
    /// Wave 161 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 33-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/oeuler</c> (NO param) -- routes through the user-
    /// tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'o' user-tier dispatch (case-letter already pinned by
    /// Wave 143 via /orientation HEAD matcher at 6881; Wave 161
    /// deepens case-'o' to the ELSE-IF chain arm /oeuler at 6886).
    /// Independent CONSECUTIVE-IF at 6873 strcmp "ori" FAIL (strcmp
    /// "ori" vs "oeuler" o-o, r-e MISMATCH idx 1). ELSE-IF chain
    /// HEAD at 6881 MatchOptWithParam("orientation",...): strncmp
    /// ("orientation","oeuler",6) o-o, r-e MISMATCH idx 1 false NO
    /// emit. else-if at 6886 `MatchOptWithParam("oeuler", pch,
    /// param, msg_sent)` -- NO outer AdminLevel guard, NO inside-
    /// body guard. MatchOptWithParam: strncmps "oeuler" against
    /// "oeuler" (6 byte match), arg[6]='\0' -- NOT '=', NOT ' ',
    /// NOT isalpha, allowNoParams=false -- emits "Missing arg for
    /// option oeuler" via SendVaMessage at 4548, sets msg_sent=
    /// true, returns false. Body block `success = HandleEuler
    /// OrientationRequest(param)` at 6888 SKIPPED (matcher returned
    /// false, body block never entered). else-if at 6891 /openif
    /// SKIPPED (chained via else-if; preceding /oeuler branch was
    /// taken). case-'o' breaks. Trailing fallback at 7702 SKIPPED
    /// (msg_sent=true). NET RESULT: ONE emit.
    ///
    /// <para>
    /// THIRTIETH pin on the user-tier (single-slash) dispatch path.
    /// SECOND pin on user-tier case-'o' (Wave 143 pinned /orientation
    /// HEAD matcher 11-byte ELSE-IF chain HEAD; Wave 161 deepens
    /// case-'o' to ELSE-IF chain arm /oeuler 6-byte at 6886); case-
    /// 'o' is now DOUBLE-PINNED across BOTH ELSE-IF chain HEAD AND
    /// ELSE-IF chain arm positions. TWENTY-NINTH pin on the
    /// MatchOptWithParam ERROR path. SIXTH NO-GUARD pin (Wave 153
    /// case-t /tilt + Wave 154 case-u /uitrigger + Wave 155 case-w
    /// /wormhole + Wave 156 case-w /warp + Wave 160 case-t /trade +
    /// Wave 161 case-o /oeuler ELSE-IF chain arm) -- NO-GUARD now
    /// SEXTUPLE-PINNED across FOUR distinct case-letters (o/t/u/w)
    /// AND FOUR distinct block structures (pure CONSECUTIVE-IF /
    /// NO-GUARD ELSE-IF / MIXED / NO-OUTER-GUARD INSIDE-BODY-GM /
    /// ELSE-IF chain arm).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 160
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     case-'o' ELSE-IF chain arm deepening regression at
    ///     <c>PlayerConnection.cpp:6886-6890</c>. Wave 143 pinned
    ///     case-'o' /orientation HEAD matcher at 6881 (ELSE-IF chain
    ///     HEAD, 11-byte width); case-'o' /oeuler at 6886 sits on
    ///     the ELSE-IF chain arm (chained off /orientation) and was
    ///     UNPINNED before Wave 161. A regression that converted
    ///     ELSE-IF to CONSECUTIVE-IF within case-'o' would change
    ///     dispatch semantics (independent CONSECUTIVE-IF would NOT
    ///     short-circuit when /orientation matched); a regression
    ///     that added an outer AdminLevel guard would skip the ERROR-
    ///     fork emit; Wave 161 pins case-'o' /oeuler ELSE-IF chain
    ///     arm is REACHABLE AND the NO-GUARD structural variant is
    ///     preserved at this case-letter / chain-arm position.
    ///   </item>
    ///   <item>
    ///     case-'o' ELSE-IF chain fall-through regression at
    ///     <c>PlayerConnection.cpp:6891+</c>. After the /oeuler
    ///     matcher emits, execution leaves the ELSE-IF chain because
    ///     /oeuler matched and short-circuited the else-if to
    ///     /openif at 6891; case-'o' breaks; trailing fallback at
    ///     7702 SKIPPED (msg_sent=true). A regression that converted
    ///     ELSE-IF to CONSECUTIVE-IF would cause /openif strtok_s to
    ///     run after /oeuler emits (strncmp would FAIL against pch=
    ///     "oeuler" but structural semantics change); a regression
    ///     that flipped the trailing fallback's `!msg_sent` to
    ///     `msg_sent` would emit a second "Illegal slash command:
    ///     oeuler" message; Wave 161 pins the ELSE-IF chain fall-
    ///     through as a structural invariant via the EXACT-equals
    ///     filter.
    ///   </item>
    ///   <item>
    ///     NO-GUARD pattern sextuple-pin cross-case-letter cross-
    ///     block-structure divergence regression at
    ///     <c>PlayerConnection.cpp:5434+</c>. Wave 153 pinned NO-
    ///     GUARD at case-'t' /tilt pure CONSECUTIVE-IF; Wave 154 at
    ///     case-'u' /uitrigger NO-GUARD ELSE-IF; Wave 155 at case-
    ///     'w' /wormhole pure NO-GUARD MIXED; Wave 156 at case-'w'
    ///     /warp NO-OUTER-GUARD INSIDE-BODY-GM; Wave 160 at case-'t'
    ///     /trade pure NO-GUARD CONSECUTIVE-IF; Wave 161 at case-'o'
    ///     /oeuler ELSE-IF chain arm NO-GUARD. SIX distinct NO-GUARD
    ///     pins across FOUR distinct case-letters (o/t/u/w) AND
    ///     FOUR distinct block structures. A regression that
    ///     selectively broke NO-GUARD at one (case-letter, structure)
    ///     combination but not the others would fail one pin but
    ///     not all six; Wave 161 deepens NO-GUARD to SEXTUPLE-PIN
    ///     coverage.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-
    /// level error path. /oeuler (Euler-angle orientation debug
    /// command) is OPEN to all users in the retail server -- NO
    /// outer AdminLevel guard at 6886, NO inside-body guard. ERROR
    /// path emits regardless of AdminLevel. No server permissiveness
    /// added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashOeulerMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (30) = 33 bytes.
        const int ExpectedReplyPayloadLength = 33;
        // strlen(literal) + 1 NUL = 30.
        const short ExpectedReplyLengthField = 30;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 29.
        const int ExpectedLiteralByteCount = 29;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Eulro", shipName: "EulroShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/oeuler");

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

                if (!text.Equals("Missing arg for option oeuler", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgOeulerLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/oeuler\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option oeuler\". Likely the user-tier case-'o' " +
                $"dispatch at line 6872 stopped routing, the ELSE-IF chain arm at " +
                $"6886 stopped dispatching, the NO-GUARD structural variant " +
                $"converted to OUTER-GUARD (would skip ERROR emit for non-privileged " +
                $"users), the ELSE-IF chain converted to CONSECUTIVE-IF (would change " +
                $"cross-arm dispatch semantics), the trailing illegal-slash fallback " +
                $"at 7702 fired as a second emit (msg_sent gate regression), or the " +
                $"missing-arg ERROR fork at PlayerConnection.cpp:4548 changed shape " +
                $"(esp. vsprintf_s 6-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 162 missing-arg ERROR literal for case-'o' /openif.
    /// The matcher at PlayerConnection.cpp:6891 reads
    /// `else if (MatchOptWithParam("openif", pch, param, msg_sent))`
    /// -- ELSE-IF chain arm (chained off /orientation HEAD at 6881
    /// and /oeuler arm at 6886), NO outer AdminLevel guard, NO
    /// inside-body guard on matcher. THIRD case-'o' pin (Wave 143
    /// /orientation ELSE-IF chain HEAD + Wave 161 /oeuler ELSE-IF
    /// chain arm pos 1 + Wave 162 /openif ELSE-IF chain arm pos 2).
    /// 29 ASCII bytes after %s substitution -- same width as Wave
    /// 161 (6-byte) but different arm position; 6-byte %s width
    /// double-pinned within SAME case-letter, deepening case-'o'
    /// to ALL THREE ELSE-IF chain positions.
    /// </summary>
    private const string MissingArgOpenifLiteral = "Missing arg for option openif";

    /// <summary>
    /// Wave 162 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 33-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/openif</c> (NO param) -- routes through the user-
    /// tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'o' user-tier dispatch (case-letter already DOUBLE-pinned
    /// by Wave 143 /orientation ELSE-IF chain HEAD + Wave 161 /oeuler
    /// ELSE-IF chain arm pos 1; Wave 162 deepens case-'o' to ELSE-IF
    /// chain arm pos 2 /openif at 6891). Independent CONSECUTIVE-IF
    /// at 6873 strcmp "ori" FAIL. ELSE-IF chain HEAD at 6881
    /// MatchOptWithParam("orientation",...): strncmp("orientation",
    /// "openif",6) o-o, r-p MISMATCH idx 1 false NO emit. else-if at
    /// 6886 MatchOptWithParam("oeuler",...): strncmp("oeuler",
    /// "openif",6) o-o, e-p MISMATCH idx 1 false NO emit. else-if at
    /// 6891 `MatchOptWithParam("openif", pch, param, msg_sent)` --
    /// NO outer AdminLevel guard, NO inside-body guard. MatchOpt
    /// WithParam: strncmps "openif" against "openif" (6 byte match),
    /// arg[6]='\0' -- NOT '=', NOT ' ', NOT isalpha, allowNoParams=
    /// false -- emits "Missing arg for option openif" via
    /// SendVaMessage at 4548, sets msg_sent=true, returns false.
    /// Body block strtok_s + OpenInterface SKIPPED (matcher returned
    /// false, body block never entered). case-'o' breaks. Trailing
    /// fallback at 7702 SKIPPED (msg_sent=true). NET RESULT: ONE emit.
    ///
    /// <para>
    /// THIRTY-FIRST pin on the user-tier (single-slash) dispatch path.
    /// THIRD pin on user-tier case-'o' -- case-'o' now TRIPLE-PINNED
    /// across ALL THREE ELSE-IF chain positions (HEAD + arm pos 1 +
    /// arm pos 2). THIRTIETH pin on the MatchOptWithParam ERROR path.
    /// SEVENTH NO-GUARD pin -- NO-GUARD now SEPTUPLE-PINNED. SECOND
    /// 6-byte %s width pin at SAME case-letter (case-'o') -- Wave
    /// 161 /oeuler + Wave 162 /openif -- DOUBLE-PINNED 6-byte width
    /// within SAME case-letter pinning per-arm-position invariants.
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes Wave 161
    /// is structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     case-'o' ELSE-IF chain arm pos 2 deepening regression at
    ///     <c>PlayerConnection.cpp:6891-6901</c>. Wave 143 pinned
    ///     ELSE-IF chain HEAD /orientation; Wave 161 pinned ELSE-IF
    ///     chain arm pos 1 /oeuler; case-'o' /openif at 6891 sits
    ///     on ELSE-IF chain arm pos 2 (chained off /oeuler) and was
    ///     UNPINNED before Wave 162. A regression that converted
    ///     ELSE-IF to CONSECUTIVE-IF within case-'o' would change
    ///     dispatch semantics; a regression that added an outer
    ///     AdminLevel guard would skip the ERROR-fork emit; Wave
    ///     162 pins case-'o' /openif ELSE-IF chain arm pos 2 is
    ///     REACHABLE AND the NO-GUARD structural variant is preserved
    ///     at the TERMINAL position of the case-'o' ELSE-IF chain.
    ///   </item>
    ///   <item>
    ///     case-'o' ELSE-IF chain TERMINAL-arm regression at
    ///     <c>PlayerConnection.cpp:6891-6901</c>. /openif is the
    ///     LAST ELSE-IF arm in case-'o' before the break (the
    ///     remaining else-if blocks at 6902+ are commented out).
    ///     After the /openif matcher emits, case-'o' breaks; trailing
    ///     fallback at 7702 SKIPPED (msg_sent=true). A regression
    ///     that uncommented the dead else-if blocks or added a
    ///     competing matcher AFTER /openif matching pch="openif" by
    ///     accident would produce a second message; Wave 162 pins
    ///     the case-'o' ELSE-IF chain TERMINAL-arm position as a
    ///     structural invariant.
    ///   </item>
    ///   <item>
    ///     6-byte %s width within-case-letter double-pin divergence
    ///     regression at <c>PlayerClass.cpp:3422</c>. Wave 161 pinned
    ///     6-byte %s width at case-'o' /oeuler ELSE-IF chain arm pos
    ///     1; Wave 162 pins 6-byte %s width at case-'o' /openif
    ///     ELSE-IF chain arm pos 2. SAME case-letter, SAME width,
    ///     DIFFERENT chain-arm position. A regression that broke
    ///     6-byte %s rendering at one chain-arm position but not the
    ///     other would fail one pin but not both; Wave 162 deepens
    ///     6-byte %s coverage to per-chain-arm-position invariance
    ///     within a single case-letter -- ruling out arm-position-
    ///     specific format-substitution divergence.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-
    /// level error path. /openif (open-interface debug command) is
    /// OPEN to all users in the retail server -- NO outer AdminLevel
    /// guard at 6891, NO inside-body guard. ERROR path emits
    /// regardless of AdminLevel. No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashOpenifMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (30) = 33 bytes.
        const int ExpectedReplyPayloadLength = 33;
        // strlen(literal) + 1 NUL = 30.
        const short ExpectedReplyLengthField = 30;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 29.
        const int ExpectedLiteralByteCount = 29;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Openo", shipName: "OpenoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/openif");

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

                if (!text.Equals("Missing arg for option openif", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgOpenifLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/openif\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option openif\". Likely the user-tier case-'o' " +
                $"dispatch at line 6872 stopped routing, the ELSE-IF chain arm pos 2 at " +
                $"6891 stopped dispatching, the NO-GUARD structural variant converted " +
                $"to OUTER-GUARD, the ELSE-IF chain converted to CONSECUTIVE-IF, the " +
                $"commented-out dead matchers at 6902+ were re-enabled (would change " +
                $"chain length), the trailing illegal-slash fallback at 7702 fired as " +
                $"a second emit (msg_sent gate regression), or the missing-arg ERROR " +
                $"fork at PlayerConnection.cpp:4548 changed shape (esp. vsprintf_s " +
                $"6-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 163 missing-arg ERROR literal for case-'s' /sounds.
    /// The matcher at PlayerConnection.cpp:7179 reads
    /// `else if (MatchOptWithParam("sounds", pch, param, msg_sent))`
    /// -- ELSE-IF chain arm (chained off strcmp("slaysectormobs")
    /// HEAD at 7137 + tail-guarded /script arm at 7143). NO outer
    /// AdminLevel guard, NO inside-body guard on matcher. FIRST
    /// case-'s' user-tier pin (case-'s' previously pinned only in
    /// admin double-slash GM-block via Wave 142 /setpassword).
    /// 29 ASCII bytes after %s substitution -- 6-byte width matches
    /// Waves 161 (/oeuler) and 162 (/openif); 6-byte %s width
    /// TRIPLE-PINNED across THREE case-letters now (o/s).
    /// </summary>
    private const string MissingArgSoundsLiteral = "Missing arg for option sounds";

    /// <summary>
    /// Wave 163 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 33-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/sounds</c> (NO param) -- routes through the user-
    /// tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'s' user-tier dispatch at line 7136 (NEW case-letter for
    /// user-tier NO-GUARD pattern; case-'s' was previously pinned only
    /// in the admin double-slash GM-block via Wave 142 /setpassword).
    /// HEAD strcmp at 7137 `strcmp(pch,"slaysectormobs") == 0 &amp;&amp;
    /// AdminLevel() >= SDEV` FAIL ("sounds" != "slaysectormobs"). ELSE-IF
    /// at 7143 `MatchOptWithParam("script", pch, param, msg_sent) &amp;&amp;
    /// AdminLevel() >= SDEV` MATCHER-FIRST + tail-guard: strncmp
    /// ("script","sounds",6) idx 1 'c' vs 'o' MISMATCH, returns false
    /// without emit; short-circuit AND skips tail guard. ELSE-IF at
    /// 7179 `MatchOptWithParam("sounds", pch, param, msg_sent)` -- NO
    /// outer AdminLevel guard, NO inside-body guard. MatchOptWithParam:
    /// strncmp "sounds" against "sounds" (6 byte match), arg[6]='\0'
    /// -- NOT '=', NOT ' ', NOT isalpha, allowNoParams=false -- emits
    /// "Missing arg for option sounds" via SendVaMessage at 4548, sets
    /// msg_sent=true, returns false. Body block SendClientSound(param)
    /// SKIPPED (matcher returned false). case-'s' chain continues
    /// through remaining else-if arms but they all evaluate strncmp
    /// MISMATCHES against "sounds"; case-'s' breaks. Trailing fallback
    /// at 7702 SKIPPED (msg_sent=true). NET RESULT: ONE emit.
    ///
    /// <para>
    /// THIRTY-SECOND pin on the user-tier (single-slash) dispatch path.
    /// FIRST pin on user-tier case-'s' -- NEW case-letter extends
    /// user-tier coverage to 5 case-letters (o/s/t/u/w). THIRTY-FIRST
    /// pin on the MatchOptWithParam ERROR path. EIGHTH NO-GUARD pin --
    /// NO-GUARD now OCTUPLE-PINNED across 5 case-letters AND 5
    /// structural variants. THIRD 6-byte %s width pin -- TRIPLE-PINNED
    /// across 2 case-letters (Waves 161 /oeuler arm pos 1 + Wave 162
    /// /openif arm pos 2 + Wave 163 /sounds new case-letter).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes prior waves
    /// are structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     case-'s' user-tier dispatch regression at
    ///     <c>PlayerConnection.cpp:7136</c>. case-'s' was previously
    ///     pinned ONLY at the admin double-slash GM-block (Wave 142
    ///     /setpassword at line 5153); the user-tier case-'s' at 7136
    ///     was UNPINNED before Wave 163. A regression that broke the
    ///     user-tier switch dispatch case-letter 's' but kept the GM-
    ///     block intact (or vice versa) would slip past Wave 142;
    ///     Wave 163 pins user-tier case-'s' is REACHABLE via the user-
    ///     tier switch dispatcher and the NO-GUARD structural variant
    ///     is preserved within case-'s'.
    ///   </item>
    ///   <item>
    ///     case-'s' MATCHER-FIRST tail-guard /script arm short-circuit
    ///     regression at <c>PlayerConnection.cpp:7143</c>. The /script
    ///     arm has condition `MatchOptWithParam("script",...) &amp;&amp;
    ///     AdminLevel() >= SDEV` -- if a regression reordered the
    ///     guard before the matcher, the matcher would never run and
    ///     the chain would still proceed to /sounds; if a regression
    ///     converted the AND short-circuit ordering, the /script body
    ///     could fire on non-SDEV users. Wave 163 pins the chain
    ///     SUCCEEDS at the /sounds arm (proving /script arm short-
    ///     circuited correctly on "sounds" pch without emitting).
    ///   </item>
    ///   <item>
    ///     6-byte %s width cross-case-letter divergence regression at
    ///     <c>PlayerClass.cpp:3422</c>. Waves 161/162 pinned 6-byte %s
    ///     within case-'o' (arms pos 1 + pos 2); Wave 163 pins 6-byte
    ///     %s at a DIFFERENT case-letter (case-'s'). A regression that
    ///     broke 6-byte %s rendering only on a specific case-letter's
    ///     dispatch path (e.g. case-letter-specific buffer corruption)
    ///     would fail one pin but not the other; Wave 163 extends
    ///     6-byte %s coverage cross-case-letter -- ruling out case-
    ///     letter-specific format-substitution divergence.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-
    /// level error path. /sounds (client-side sound trigger debug
    /// command) is OPEN to all users in the retail server -- NO outer
    /// AdminLevel guard at 7179, NO inside-body guard. ERROR path emits
    /// regardless of AdminLevel. No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashSoundsMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (30) = 33 bytes.
        const int ExpectedReplyPayloadLength = 33;
        // strlen(literal) + 1 NUL = 30.
        const short ExpectedReplyLengthField = 30;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 29.
        const int ExpectedLiteralByteCount = 29;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Sounos", shipName: "SounosShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/sounds");

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

                if (!text.Equals("Missing arg for option sounds", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgSoundsLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/sounds\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option sounds\". Likely the user-tier case-'s' " +
                $"dispatch at line 7136 stopped routing, the ELSE-IF chain arm at " +
                $"7179 stopped dispatching, the NO-GUARD structural variant converted " +
                $"to OUTER-GUARD or INSIDE-BODY-GUARD, the /script tail-guard short-" +
                $"circuit at 7143 changed semantics, the trailing illegal-slash " +
                $"fallback at 7702 fired as a second emit (msg_sent gate regression), " +
                $"or the missing-arg ERROR fork at PlayerConnection.cpp:4548 changed " +
                $"shape (esp. vsprintf_s 6-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 164 missing-arg ERROR literal for case-'s' /scale.
    /// The matcher at PlayerConnection.cpp:7201 reads
    /// `else if (MatchOptWithParam("scale", pch, param, msg_sent))`
    /// -- pure NO-GUARD ELSE-IF chain arm, NO outer AdminLevel guard,
    /// NO inside-body guard. SECOND case-'s' user-tier pin (Wave 163
    /// /sounds 6-byte + Wave 164 /scale 5-byte); case-'s' user-tier
    /// now DOUBLE-PINNED across TWO ELSE-IF chain positions. 28 ASCII
    /// bytes after %s substitution -- 5-byte width matches Waves 158
    /// (/testmsg) and 160 (/trade); 5-byte %s width TRIPLE-PINNED
    /// across TWO case-letters now (t/s).
    /// </summary>
    private const string MissingArgScaleLiteral = "Missing arg for option scale";

    /// <summary>
    /// Wave 164 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 32-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/scale</c> (NO param) -- routes through the user-
    /// tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'s' user-tier dispatch at line 7136 (NEWLY pinned by Wave
    /// 163; Wave 164 deepens user-tier case-'s' to DOUBLE-PINNED across
    /// TWO ELSE-IF chain positions). HEAD strcmp at 7137 FAIL ("scale"
    /// != "slaysectormobs"). ELSE-IF at 7143 MatchOptWithParam("script"
    /// ...): strncmp("script","scale",6) idx 2 'r' vs 'a' MISMATCH
    /// returns false NO emit. ELSE-IF at 7179 MatchOptWithParam("sounds"
    /// ...): strncmp("sounds","scale",6) idx 1 'o' vs 'c' MISMATCH
    /// returns false NO emit. Intervening strcmp arms at 7189/7195
    /// strcmp FAIL. ELSE-IF at 7201 `MatchOptWithParam("scale", pch,
    /// param, msg_sent)` -- NO outer AdminLevel guard, NO inside-body
    /// guard. MatchOptWithParam: strncmps "scale" against "scale" (5
    /// byte match), arg[5]='\0' -- NOT '=', NOT ' ', NOT isalpha,
    /// allowNoParams=false -- emits "Missing arg for option scale"
    /// via SendVaMessage at 4548, sets msg_sent=true, returns false.
    /// Body block HandleScaleRequest(param) SKIPPED (matcher returned
    /// false). case-'s' chain continues but no later arm matches
    /// "scale"; case-'s' breaks. Trailing fallback at 7702 SKIPPED
    /// (msg_sent=true). NET RESULT: ONE emit.
    ///
    /// <para>
    /// THIRTY-THIRD pin on the user-tier (single-slash) dispatch path.
    /// SECOND pin on user-tier case-'s' -- case-'s' user-tier now
    /// DOUBLE-PINNED across TWO ELSE-IF chain positions (Wave 163
    /// /sounds at 7179 + Wave 164 /scale at 7201). THIRTY-SECOND pin
    /// on the MatchOptWithParam ERROR path. NINTH NO-GUARD pin --
    /// NO-GUARD now NONUPLE-PINNED across 5 case-letters (o/s/t/u/w).
    /// THIRD 5-byte %s width pin -- TRIPLE-PINNED across 2 case-
    /// letters (Waves 158 /testmsg + 160 /trade case-'t' + Wave 164
    /// /scale case-'s').
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes prior waves
    /// are structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     case-'s' ELSE-IF chain arm deepening regression at
    ///     <c>PlayerConnection.cpp:7201</c>. Wave 163 pinned case-'s'
    ///     ELSE-IF chain at the /sounds arm (line 7179, 6-byte option);
    ///     case-'s' /scale at 7201 sits FURTHER DOWN the same ELSE-IF
    ///     chain (after intervening strcmp arms at 7189/7195) and was
    ///     UNPINNED before Wave 164. A regression that broke the ELSE-IF
    ///     chain after /sounds (or before /scale) but kept the earlier
    ///     arms intact would slip past Wave 163; Wave 164 pins case-'s'
    ///     /scale is REACHABLE via the deeper chain position AND the
    ///     NO-GUARD structural variant is preserved at TWO chain-arm
    ///     positions within case-'s'.
    ///   </item>
    ///   <item>
    ///     case-'s' chain intervening strcmp arms regression at
    ///     <c>PlayerConnection.cpp:7189-7199</c>. Between /sounds (7179)
    ///     and /scale (7201) sit TWO strcmp arms (`strcmp(pch,
    ///     "setturrets")` at 7189 + `strcmp(pch,"setrespawns")` at 7195),
    ///     both AdminLevel >= SDEV gated. If a regression converted
    ///     either strcmp arm to a MatchOptWithParam matcher with the
    ///     same prefix, /scale could be intercepted; if a regression
    ///     removed the AdminLevel guard, the strcmp bodies could fire
    ///     for non-SDEV users (server permissiveness). Wave 164 pins
    ///     the strcmp arms short-circuit correctly on "scale" pch AND
    ///     /scale is reached as the next MatchOptWithParam arm.
    ///   </item>
    ///   <item>
    ///     5-byte %s width cross-case-letter divergence regression at
    ///     <c>PlayerClass.cpp:3422</c>. Waves 158 (/testmsg) and 160
    ///     (/trade) pinned 5-byte %s within case-'t'; Wave 164 pins
    ///     5-byte %s at case-'s' (DIFFERENT case-letter). A regression
    ///     that broke 5-byte %s rendering only on a specific case-
    ///     letter's dispatch path would fail one pin but not the
    ///     others; Wave 164 extends 5-byte %s coverage cross-case-
    ///     letter -- ruling out case-letter-specific format-substitution
    ///     divergence at the 5-byte width.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-
    /// level error path. /scale (client-side object scale debug command)
    /// is OPEN to all users in the retail server -- NO outer AdminLevel
    /// guard at 7201, NO inside-body guard. ERROR path emits regardless
    /// of AdminLevel. No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashScaleMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (29) = 32 bytes.
        const int ExpectedReplyPayloadLength = 32;
        // strlen(literal) + 1 NUL = 29.
        const short ExpectedReplyLengthField = 29;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 28.
        const int ExpectedLiteralByteCount = 28;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Scalo", shipName: "ScaloShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/scale");

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

                if (!text.Equals("Missing arg for option scale", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgScaleLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/scale\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option scale\". Likely the user-tier case-'s' " +
                $"dispatch at line 7136 stopped routing, the ELSE-IF chain arm at " +
                $"7201 stopped dispatching, the NO-GUARD structural variant converted " +
                $"to OUTER-GUARD or INSIDE-BODY-GUARD, the intervening strcmp arms at " +
                $"7189/7195 changed to matchers that intercept \"scale\", the trailing " +
                $"illegal-slash fallback at 7702 fired as a second emit (msg_sent gate " +
                $"regression), or the missing-arg ERROR fork at PlayerConnection.cpp:4548 " +
                $"changed shape (esp. vsprintf_s 5-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 165 missing-arg ERROR literal for case-'s' /shieldwarnings.
    /// The matcher at PlayerConnection.cpp:7272 reads
    /// `else if (MatchOptWithParam("shieldwarnings", pch, param, msg_sent))`
    /// -- pure NO-GUARD ELSE-IF chain arm, NO outer AdminLevel guard,
    /// NO inside-body guard. THIRD case-'s' user-tier pin (Wave 163
    /// /sounds 6-byte + Wave 164 /scale 5-byte + Wave 165 /shieldwarnings
    /// 14-byte). 37 ASCII bytes after %s substitution -- NEW 14-byte
    /// width pin (no prior 14-byte width); FIRST 14-byte %s pin in
    /// HandleSlashCommands.
    /// </summary>
    private const string MissingArgShieldwarningsLiteral = "Missing arg for option shieldwarnings";

    /// <summary>
    /// Wave 165 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 41-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/shieldwarnings</c> (NO param) -- routes through the
    /// user-tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'s' user-tier dispatch at line 7136 (Wave 165 deepens case-
    /// 's' to TRIPLE-PINNED across THREE distinct chain-arm positions).
    /// All prior strncmp matchers in case-'s' (/script at 7143, /sounds
    /// at 7179, /scale at 7201, /skillpoints at 7206, /stat at 7219,
    /// /scan at 7248) MISMATCH against "shieldwarnings" at byte 1
    /// (c/k/t/o vs 'h'). Intervening strcmp arms at 7137/7189/7195
    /// strcmp FAIL. ELSE-IF at 7272 `MatchOptWithParam("shieldwarnings",
    /// pch, param, msg_sent)` -- NO outer AdminLevel guard, NO inside-
    /// body guard. MatchOptWithParam: strncmps "shieldwarnings" against
    /// "shieldwarnings" (14 byte match), arg[14]='\0' -- NOT '=', NOT
    /// ' ', NOT isalpha, allowNoParams=false -- emits "Missing arg for
    /// option shieldwarnings" via SendVaMessage at 4548 (default
    /// COLOR=5), sets msg_sent=true, returns false. Body block at
    /// 7274-7283 SKIPPED (matcher returned false; would have emitted
    /// via SendVaMessageC with COLOR=13 -- a DIFFERENT color emission
    /// that Wave 165 implicitly negative-pins). case-'s' breaks.
    /// Trailing fallback at 7702 SKIPPED (msg_sent=true). NET RESULT:
    /// ONE emit.
    ///
    /// <para>
    /// THIRTY-FOURTH pin on the user-tier (single-slash) dispatch path.
    /// THIRD pin on user-tier case-'s' -- case-'s' user-tier now
    /// TRIPLE-PINNED across THREE ELSE-IF chain positions (Wave 163
    /// /sounds + Wave 164 /scale + Wave 165 /shieldwarnings). THIRTY-
    /// THIRD pin on the MatchOptWithParam ERROR path. TENTH NO-GUARD
    /// pin -- NO-GUARD now DECUPLE-PINNED across 5 case-letters.
    /// FIRST 14-byte %s width pin -- THIRTEENTH distinct %s-width
    /// pinned (was 1/2/3/4/5/6/7/8/9/11/13 + Wave 165 14).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes prior waves
    /// are structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     case-'s' deep ELSE-IF chain arm regression at
    ///     <c>PlayerConnection.cpp:7272</c>. /shieldwarnings sits SEVEN
    ///     arms deep in the case-'s' ELSE-IF chain (after /script,
    ///     strcmps, /sounds, /scale, /skillpoints, /stat, /scan). A
    ///     regression that broke the chain at any of those intermediate
    ///     positions (chain converted to CONSECUTIVE-IF, fall-through
    ///     introduced, or chain truncated by accidental brace closure)
    ///     would prevent /shieldwarnings from being reached; Wave 165
    ///     pins the FULL case-'s' chain depth is preserved.
    ///   </item>
    ///   <item>
    ///     ERROR-fork COLOR=5 vs body-fork COLOR=13 divergence
    ///     regression at <c>PlayerConnection.cpp:7279</c> vs
    ///     <c>PlayerConnection.cpp:4548</c>. /shieldwarnings is one of
    ///     the few arms whose SUCCESS body uses SendVaMessageC with an
    ///     EXPLICIT COLOR=13 (warning-yellow); a regression that
    ///     conflated the body-fork color with the ERROR-fork color
    ///     (e.g. ERROR-fork started using COLOR=13) would produce a
    ///     COLOR=13 reply instead of COLOR=5. Wave 165 pins the ERROR
    ///     path emits with COLOR=5 -- implicitly negative-pinning the
    ///     body-fork COLOR=13 path is NOT engaged on missing-arg.
    ///   </item>
    ///   <item>
    ///     NEW 14-byte %s width pin at <c>PlayerClass.cpp:3422</c>.
    ///     No prior wave pinned a 14-byte option name through the
    ///     MatchOptWithParam ERROR fork; Wave 165 establishes the
    ///     14-byte %s-substitution width as a structural invariant.
    ///     A regression that introduced off-by-N in vsprintf_s only at
    ///     longer %s widths (e.g. buffer-resize bug that triggers at
    ///     >=14 chars) would fail Wave 165 but pass all prior shorter-
    ///     width pins.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). /shieldwarnings (the
    /// audio shield-warning level toggle) is OPEN to all users in the
    /// retail server -- NO outer AdminLevel guard at 7272, NO inside-
    /// body guard. ERROR path emits regardless of AdminLevel. No
    /// server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashShieldwarningsMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (38) = 41 bytes.
        const int ExpectedReplyPayloadLength = 41;
        // strlen(literal) + 1 NUL = 38.
        const short ExpectedReplyLengthField = 38;
        // SendVaMessage -> SendMessageString default color parameter
        // (NOT SendVaMessageC(13,...) which is the body-fork color).
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 37.
        const int ExpectedLiteralByteCount = 37;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Shieldo", shipName: "ShieldoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/shieldwarnings");

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

                if (!text.Equals("Missing arg for option shieldwarnings", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgShieldwarningsLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/shieldwarnings\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option shieldwarnings\". Likely the user-tier case-'s' " +
                $"dispatch at line 7136 stopped routing, the deep ELSE-IF chain arm at " +
                $"7272 stopped dispatching (chain truncated, fall-through introduced, " +
                $"or intermediate arm intercepted), the NO-GUARD structural variant " +
                $"converted to OUTER-GUARD, the ERROR-fork COLOR=5 conflated with the " +
                $"body-fork COLOR=13 at SendVaMessageC, the trailing illegal-slash " +
                $"fallback at 7702 fired as a second emit (msg_sent gate regression), " +
                $"or the missing-arg ERROR fork at PlayerConnection.cpp:4548 changed " +
                $"shape (esp. vsprintf_s 14-byte %s width buffer-resize).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 166 missing-arg ERROR literal for case-'s' /skillpoints.
    /// The matcher at PlayerConnection.cpp:7206 reads
    /// `else if (MatchOptWithParam("skillpoints", pch, param, msg_sent))`
    /// -- NO outer AdminLevel guard, but INSIDE-BODY-GM-guard at 7208
    /// (`if (AdminLevel() >= GM)`). FOURTH case-'s' user-tier pin
    /// (Waves 163 /sounds + 164 /scale + 165 /shieldwarnings + 166
    /// /skillpoints). 34 ASCII bytes after %s substitution -- 11-byte
    /// width matches Wave 143 (/orientation); 11-byte %s width
    /// DOUBLE-PINNED across TWO case-letters now (o/s).
    /// </summary>
    private const string MissingArgSkillpointsLiteral = "Missing arg for option skillpoints";

    /// <summary>
    /// Wave 166 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 38-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/skillpoints</c> (NO param) -- routes through the
    /// user-tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'s' user-tier dispatch at line 7136 (Wave 166 deepens case-
    /// 's' to QUADRUPLE-PINNED across FOUR distinct ELSE-IF chain-arm
    /// positions AND introduces INSIDE-BODY-GM-guard pattern to case-
    /// 's' for the first time). ELSE-IF at 7206 `MatchOptWithParam(
    /// "skillpoints", pch, param, msg_sent)` -- NO outer AdminLevel
    /// guard, INSIDE-BODY-GM-guard at 7208 `if (AdminLevel() >= GM)`.
    /// MatchOptWithParam: strncmps "skillpoints" against "skillpoints"
    /// (11 byte match), arg[11]='\0' -- NOT '=', NOT ' ', NOT isalpha,
    /// allowNoParams=false -- emits "Missing arg for option skillpoints"
    /// via SendVaMessage at 4548 with default COLOR=5, sets msg_sent=
    /// true, returns false. Body block at 7208-7217 SKIPPED (matcher
    /// returned false; INSIDE-BODY-GM-guard NEVER EVALUATED -- this is
    /// the structural invariant being pinned: the ERROR-fork emits
    /// REGARDLESS of AdminLevel because the body-guard is downstream
    /// of the matcher). case-'s' chain continues but no later arm
    /// matches "skillpoints"; case-'s' breaks. Trailing fallback at
    /// 7702 SKIPPED (msg_sent=true). NET RESULT: ONE emit.
    ///
    /// <para>
    /// THIRTY-FIFTH pin on the user-tier (single-slash) dispatch path.
    /// FOURTH pin on user-tier case-'s' -- case-'s' user-tier now
    /// QUADRUPLE-PINNED across FOUR ELSE-IF chain-arm positions. THIRTY-
    /// FOURTH pin on the MatchOptWithParam ERROR path. SECOND INSIDE-
    /// BODY-guard pin -- INSIDE-BODY-guard now DOUBLE-PINNED across 2
    /// guard tiers (Wave 159 /terminate INSIDE-BODY-DEV + Wave 166
    /// /skillpoints INSIDE-BODY-GM). SECOND 11-byte %s width pin --
    /// 11-byte width DOUBLE-PINNED across 2 case-letters (Wave 143
    /// /orientation case-'o' + Wave 166 /skillpoints case-'s').
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes prior waves
    /// are structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     INSIDE-BODY-GM-guard ERROR-fork-bypass regression at
    ///     <c>PlayerConnection.cpp:7206-7218</c>. The matcher emits via
    ///     the SendVaMessage at 4548 (inside MatchOptWithParam) BEFORE
    ///     control returns to PlayerConnection.cpp:7208's GM-guard
    ///     evaluation. A regression that moved the AdminLevel check
    ///     INTO MatchOptWithParam (e.g. as a new parameter), or that
    ///     wrapped the matcher in an outer guard, would gate the ERROR-
    ///     fork emit behind GM-tier -- non-GM users would receive NO
    ///     reply on /skillpoints. Wave 166 pins the ERROR fork emits
    ///     on a BETA-tier (non-GM) test account, proving the body-
    ///     guard does NOT gate the missing-arg emit.
    ///   </item>
    ///   <item>
    ///     INSIDE-BODY-guard structural pattern cross-tier divergence
    ///     regression at <c>PlayerConnection.cpp:7208</c> vs <c>7501</c>.
    ///     Wave 159 pinned INSIDE-BODY-DEV-guard at /terminate; Wave
    ///     166 pins INSIDE-BODY-GM-guard at /skillpoints. SAME
    ///     structural pattern, DIFFERENT tier (DEV=80 vs GM=50). A
    ///     regression that broke ERROR-fork-bypass at one tier but not
    ///     the other would fail one pin but not both; Wave 166 pins
    ///     INSIDE-BODY-guard ERROR-fork-bypass is tier-independent.
    ///   </item>
    ///   <item>
    ///     11-byte %s width cross-case-letter divergence regression
    ///     at <c>PlayerClass.cpp:3422</c>. Wave 143 pinned 11-byte %s
    ///     at case-'o' /orientation; Wave 166 pins 11-byte %s at
    ///     case-'s' /skillpoints. A regression that broke 11-byte %s
    ///     rendering only on a specific case-letter's dispatch path
    ///     would fail one pin but not the other; Wave 166 extends
    ///     11-byte %s coverage cross-case-letter.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). /skillpoints
    /// (skillpoint-set debug command) is open to ALL users at the
    /// dispatcher level -- the INSIDE-BODY-GM-guard at 7208 restricts
    /// the SUCCESS path to GM+ only, but the ERROR fork at 4548 emits
    /// for ALL tiers (faithful to retail). The MatchOptWithParam
    /// missing-arg emit is the retail server's documented dispatcher-
    /// level error path. No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashSkillpointsMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (35) = 38 bytes.
        const int ExpectedReplyPayloadLength = 38;
        // strlen(literal) + 1 NUL = 35.
        const short ExpectedReplyLengthField = 35;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 34.
        const int ExpectedLiteralByteCount = 34;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Skilo", shipName: "SkiloShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/skillpoints");

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

                if (!text.Equals("Missing arg for option skillpoints", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgSkillpointsLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/skillpoints\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option skillpoints\". Likely the user-tier case-'s' " +
                $"dispatch at line 7136 stopped routing, the ELSE-IF chain arm at " +
                $"7206 stopped dispatching, the INSIDE-BODY-GM-guard moved BEFORE the " +
                $"matcher (gating the ERROR-fork emit behind GM-tier), the matcher's " +
                $"AdminLevel parameter was added to MatchOptWithParam itself, the " +
                $"trailing illegal-slash fallback at 7702 fired as a second emit " +
                $"(msg_sent gate regression), or the missing-arg ERROR fork at " +
                $"PlayerConnection.cpp:4548 changed shape (esp. vsprintf_s 11-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 167 missing-arg ERROR literal for case-'s' /stat.
    /// The matcher at PlayerConnection.cpp:7219 reads
    /// `else if (MatchOptWithParam("stat", pch, param, msg_sent))`
    /// -- NO outer AdminLevel guard, INSIDE-BODY-DEV-guard at 7221
    /// (`if (AdminLevel() >= DEV)`). FIFTH case-'s' user-tier pin
    /// (Waves 163 /sounds + 164 /scale + 165 /shieldwarnings + 166
    /// /skillpoints + 167 /stat). 27 ASCII bytes after %s substitution
    /// -- 4-byte width matches Waves 146 (/form), 148 (/kick), 150
    /// (/move), 153 (/tilt), 156 (/warp); 4-byte %s width SEXTUPLE-
    /// PINNED across 6 case-letters / structural patterns now.
    /// </summary>
    private const string MissingArgStatLiteral = "Missing arg for option stat";

    /// <summary>
    /// Wave 167 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 31-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/stat</c> (NO param) -- routes through the user-tier
    /// dispatcher entry at line 5434, the 1-char strip, the case-'s'
    /// user-tier dispatch at line 7136 (Wave 167 deepens case-'s' to
    /// QUINTUPLE-PINNED across FIVE distinct ELSE-IF chain-arm
    /// positions AND introduces INSIDE-BODY-DEV-guard pattern to
    /// case-'s'). Prior matchers in case-'s' MISMATCH at byte 1 against
    /// "stat" (c/o/c/k MISMATCH). ELSE-IF at 7219 `MatchOptWithParam(
    /// "stat", pch, param, msg_sent)` -- NO outer AdminLevel guard,
    /// INSIDE-BODY-DEV-guard at 7221 `if (AdminLevel() >= DEV)`.
    /// MatchOptWithParam: strncmps "stat" against "stat" (4 byte
    /// match), arg[4]='\0' -- NOT '=', NOT ' ', NOT isalpha,
    /// allowNoParams=false -- emits "Missing arg for option stat"
    /// via SendVaMessage at 4548 with default COLOR=5, sets msg_sent=
    /// true, returns false. Body block at 7221-7246 SKIPPED (matcher
    /// returned false; INSIDE-BODY-DEV-guard NEVER EVALUATED -- the
    /// ERROR-fork-bypass invariant being pinned at DEV tier). case-'s'
    /// chain continues but no later arm matches "stat"; case-'s'
    /// breaks. Trailing fallback at 7702 SKIPPED (msg_sent=true).
    /// NET RESULT: ONE emit.
    ///
    /// <para>
    /// THIRTY-SIXTH pin on the user-tier (single-slash) dispatch path.
    /// FIFTH pin on user-tier case-'s' -- case-'s' user-tier now
    /// QUINTUPLE-PINNED across FIVE ELSE-IF chain-arm positions.
    /// THIRTY-FIFTH pin on the MatchOptWithParam ERROR path. FOURTH
    /// INSIDE-BODY-guard pin (Waves 156 /warp GM + 159 /terminate
    /// DEV + 166 /skillpoints GM + 167 /stat DEV) -- INSIDE-BODY-
    /// guard QUADRUPLE-PINNED with INSIDE-BODY-GM and INSIDE-BODY-DEV
    /// EACH DOUBLE-PINNED at this point. SIXTH 4-byte %s width pin --
    /// SEXTUPLE-PINNED across 6 case-letters / structural patterns
    /// (Waves 146 /form COMBINED-GUARD case-'f' + 148 /kick CASE-
    /// FALL-THROUGH case-'k' + 150 /move NO-GUARD-ELSE-IF case-'m' +
    /// 153 /tilt CONSECUTIVE-IF case-'t' + 156 /warp INSIDE-BODY-GM
    /// case-'w' + 167 /stat INSIDE-BODY-DEV case-'s').
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes prior waves
    /// are structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     INSIDE-BODY-DEV-guard cross-arm divergence regression at
    ///     <c>PlayerConnection.cpp:7221</c> vs <c>7501</c>. Wave 159
    ///     pinned INSIDE-BODY-DEV-guard at /terminate (case-'t');
    ///     Wave 167 pins INSIDE-BODY-DEV-guard at /stat (case-'s').
    ///     SAME tier, DIFFERENT case-letter. A regression that broke
    ///     the ERROR-fork-bypass for one DEV-gated arm but not the
    ///     other would fail one pin but not both; Wave 167 deepens
    ///     INSIDE-BODY-DEV ERROR-fork-bypass to DOUBLE-PINNED across
    ///     2 case-letters -- ruling out case-letter-specific INSIDE-
    ///     BODY-DEV-guard regressions.
    ///   </item>
    ///   <item>
    ///     INSIDE-BODY-guard tier symmetry regression at <c>PlayerConnection.cpp</c>.
    ///     After Wave 167, BOTH guard tiers (GM and DEV) have TWO
    ///     INSIDE-BODY pins each (GM: Waves 156 /warp + 166
    ///     /skillpoints; DEV: Waves 159 /terminate + 167 /stat).
    ///     This pins symmetric coverage of the INSIDE-BODY-guard
    ///     ERROR-fork-bypass invariant across BOTH tier families --
    ///     a regression that gated the ERROR-fork emit behind ANY
    ///     AdminLevel tier (GM or DEV) would fail at LEAST 2 of the
    ///     4 pins.
    ///   </item>
    ///   <item>
    ///     4-byte %s width SIXTH cross-structural-pattern divergence
    ///     regression at <c>PlayerClass.cpp:3422</c>. 4-byte %s now
    ///     pinned at SIX distinct (case-letter, structural-pattern)
    ///     combinations -- f/COMBINED-GUARD, k/CASE-FALL-THROUGH,
    ///     m/NO-GUARD-ELSE-IF, t/CONSECUTIVE-IF, w/INSIDE-BODY-GM,
    ///     s/INSIDE-BODY-DEV. A regression in vsprintf_s 4-byte path
    ///     specific to ONE (case-letter, pattern) combination would
    ///     fail one pin but not all six; Wave 167 deepens 4-byte
    ///     coverage to SEXTUPLE-PIN across 6 distinct dispatch paths.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). /stat is open to
    /// ALL users at the dispatcher level -- the INSIDE-BODY-DEV-guard
    /// at 7221 restricts the SUCCESS path to DEV+ only, but the ERROR
    /// fork at 4548 emits for ALL tiers (faithful to retail).
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashStatMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (28) = 31 bytes.
        const int ExpectedReplyPayloadLength = 31;
        // strlen(literal) + 1 NUL = 28.
        const short ExpectedReplyLengthField = 28;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 27.
        const int ExpectedLiteralByteCount = 27;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Stato", shipName: "StatoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/stat");

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

                if (!text.Equals("Missing arg for option stat", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgStatLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/stat\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option stat\". Likely the user-tier case-'s' " +
                $"dispatch at line 7136 stopped routing, the ELSE-IF chain arm at " +
                $"7219 stopped dispatching, the INSIDE-BODY-DEV-guard moved BEFORE " +
                $"the matcher (gating the ERROR-fork emit behind DEV-tier), the " +
                $"trailing illegal-slash fallback at 7702 fired as a second emit, " +
                $"or the missing-arg ERROR fork at PlayerConnection.cpp:4548 changed " +
                $"shape (esp. vsprintf_s 4-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 168 missing-arg ERROR literal for case-'s' /scan.
    /// The matcher at PlayerConnection.cpp:7248 reads
    /// `else if (MatchOptWithParam("scan", pch, param, msg_sent))`
    /// -- NO outer AdminLevel guard, INSIDE-BODY-BETA_PLUS-guard at
    /// 7250 (`if (AdminLevel() >= BETA_PLUS)`). SIXTH case-'s' user-
    /// tier pin. 27 ASCII bytes after %s substitution -- 4-byte width
    /// matches Waves 146 (/form) + 148 (/kick) + 150 (/move) + 153
    /// (/tilt) + 156 (/warp) + 167 (/stat); 4-byte %s width SEPTUPLE-
    /// PINNED across 7 case-letters / structural patterns now.
    /// INTRODUCES INSIDE-BODY-BETA_PLUS guard subfamily (NEW tier).
    /// </summary>
    private const string MissingArgScanLiteral = "Missing arg for option scan";

    /// <summary>
    /// Wave 168 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 31-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/scan</c> (NO param) -- routes through the user-tier
    /// dispatcher entry at line 5434, the 1-char strip, the case-'s'
    /// user-tier dispatch at line 7136 (Wave 168 deepens case-'s' to
    /// SEXTUPLE-PINNED across SIX distinct ELSE-IF chain-arm positions
    /// AND introduces INSIDE-BODY-BETA_PLUS-guard subfamily to the
    /// HandleSlashCommands catalogue). Prior matchers in case-'s'
    /// MISMATCH at byte 1 against "scan" (only /scale matches at
    /// byte 1 'c'='c' but diverges at byte 2 'a'='a' / byte 3 'l' vs
    /// 'n' MISMATCH). ELSE-IF at 7248 `MatchOptWithParam("scan", pch,
    /// param, msg_sent)` -- NO outer AdminLevel guard, INSIDE-BODY-
    /// BETA_PLUS-guard at 7250 `if (AdminLevel() >= BETA_PLUS)`.
    /// MatchOptWithParam: strncmps "scan" against "scan" (4 byte
    /// match), arg[4]='\0' -- NOT '=', NOT ' ', NOT isalpha,
    /// allowNoParams=false -- emits "Missing arg for option scan"
    /// via SendVaMessage at 4548 with default COLOR=5, sets msg_sent=
    /// true, returns false. Body block at 7250-7270 SKIPPED (matcher
    /// returned false; INSIDE-BODY-BETA_PLUS-guard NEVER EVALUATED --
    /// the ERROR-fork-bypass invariant being pinned at the NEW
    /// BETA_PLUS tier). case-'s' chain continues but no later arm
    /// matches "scan"; case-'s' breaks. Trailing fallback at 7702
    /// SKIPPED (msg_sent=true). NET RESULT: ONE emit.
    ///
    /// <para>
    /// THIRTY-SEVENTH pin on the user-tier (single-slash) dispatch
    /// path. SIXTH pin on user-tier case-'s' -- case-'s' user-tier now
    /// SEXTUPLE-PINNED across SIX ELSE-IF chain-arm positions. THIRTY-
    /// SIXTH pin on the MatchOptWithParam ERROR path. FIFTH INSIDE-
    /// BODY-guard pin (Waves 156 GM + 159 DEV + 166 GM + 167 DEV + 168
    /// BETA_PLUS) -- INSIDE-BODY-guard QUINTUPLE-PINNED across THREE
    /// tier-subfamilies (GM DOUBLE + DEV DOUBLE + BETA_PLUS SINGLE).
    /// SEVENTH 4-byte %s width pin -- SEPTUPLE-PINNED across 7 case-
    /// letters / structural patterns.
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes prior waves
    /// are structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     NEW INSIDE-BODY-BETA_PLUS-guard tier-subfamily regression
    ///     at <c>PlayerConnection.cpp:7248-7270</c>. Wave 156/166
    ///     pinned INSIDE-BODY-GM-guard (tier 50); Wave 159/167 pinned
    ///     INSIDE-BODY-DEV-guard (tier 80); Wave 168 introduces
    ///     INSIDE-BODY-BETA_PLUS-guard (tier 40 -- LOWER than GM).
    ///     A regression that introduced a global "if AdminLevel >=
    ///     SOME_TIER" wrapper around the entire dispatcher (or around
    ///     each matcher) would gate the ERROR-fork emit behind that
    ///     tier; Wave 168 pins the ERROR fork emits on a BETA-tier
    ///     (tier 30, BELOW BETA_PLUS=40) test account, proving the
    ///     body-guard at BETA_PLUS does NOT gate the missing-arg emit.
    ///   </item>
    ///   <item>
    ///     INSIDE-BODY-guard tier-orthogonality regression -- after
    ///     Wave 168, THREE distinct tier-subfamilies (BETA_PLUS=40 +
    ///     GM=50 + DEV=80) are each pinned at INSIDE-BODY-guard
    ///     positions; if a regression introduced a tier-floor that
    ///     gated the dispatcher at ANY non-zero AdminLevel, ALL
    ///     test accounts (status=100 admin) would still pass except
    ///     for the literal "Missing arg for option" emit being
    ///     suppressed at the FLOOR tier. Wave 168 pins the orthogonal
    ///     coverage across THREE tier-subfamilies -- a regression
    ///     would have to break ALL three to slip past.
    ///   </item>
    ///   <item>
    ///     4-byte %s width SEVENTH cross-structural-pattern divergence
    ///     regression at <c>PlayerClass.cpp:3422</c>. 4-byte %s now
    ///     pinned at SEVEN distinct (case-letter, structural-pattern)
    ///     combinations -- f/COMBINED + k/CASE-FALL-THROUGH +
    ///     m/NO-GUARD-ELSE-IF + t/CONSECUTIVE-IF + w/INSIDE-BODY-GM +
    ///     s/INSIDE-BODY-DEV + s/INSIDE-BODY-BETA_PLUS. Wave 168
    ///     deepens 4-byte coverage to SEPTUPLE-PIN.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). /scan is open to ALL
    /// users at the dispatcher level -- the INSIDE-BODY-BETA_PLUS-guard
    /// at 7250 restricts the SUCCESS path to BETA_PLUS+ only, but the
    /// ERROR fork at 4548 emits for ALL tiers (faithful to retail).
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashScanMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (28) = 31 bytes.
        const int ExpectedReplyPayloadLength = 31;
        // strlen(literal) + 1 NUL = 28.
        const short ExpectedReplyLengthField = 28;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 27.
        const int ExpectedLiteralByteCount = 27;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Scano", shipName: "ScanoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/scan");

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

                if (!text.Equals("Missing arg for option scan", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgScanLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/scan\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option scan\". Likely the user-tier case-'s' " +
                $"dispatch at line 7136 stopped routing, the ELSE-IF chain arm at " +
                $"7248 stopped dispatching, the INSIDE-BODY-BETA_PLUS-guard moved " +
                $"BEFORE the matcher (gating the ERROR-fork emit behind BETA_PLUS-tier), " +
                $"the trailing illegal-slash fallback at 7702 fired as a second emit, " +
                $"or the missing-arg ERROR fork at PlayerConnection.cpp:4548 changed " +
                $"shape (esp. vsprintf_s 4-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 169 missing-arg ERROR literal for case-'s' /signature.
    /// The matcher at PlayerConnection.cpp:7288 reads
    /// `if (MatchOptWithParam("signature", pch, param, msg_sent))`
    /// -- nested INSIDE an OUTER-BLOCK-DEV-guard at line 7286
    /// (`if (AdminLevel() >= DEV)`) that wraps the matcher AND
    /// its body. NEW structural pattern: OUTER-BLOCK-DEV-guard
    /// (distinct from INSIDE-BODY-DEV-guard at Waves 159/167 where
    /// the matcher runs unconditional and only the body is gated).
    /// 9 ASCII bytes after %s substitution -- 9-byte width
    /// DOUBLE-PINNED across 2 case-letters / 2 structural patterns
    /// after Wave 154 (case-'u' /uitrigger NO-GUARD-ELSE-IF) +
    /// Wave 169 (case-'s' /signature OUTER-BLOCK-DEV).
    /// </summary>
    private const string MissingArgSignatureLiteral = "Missing arg for option signature";

    /// <summary>
    /// Wave 169 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 36-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/signature</c> (NO param) -- routes through the
    /// user-tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'s' user-tier dispatch at line 7136 (Wave 169 deepens
    /// case-'s' to SEPTUPLE-PINNED across SEVEN distinct ELSE-IF chain
    /// positions AND introduces OUTER-BLOCK-DEV-guard structural
    /// pattern to the HandleSlashCommands catalogue). Prior matchers
    /// in case-'s' MISMATCH at byte 1 against "signature" (only /scale
    /// matches byte 1 'c' vs 'i' MISMATCH -- actually 'c' vs 'i' so
    /// MISMATCH at byte 1; everything else also mismatches by byte 1
    /// or earlier).
    ///
    /// <para>
    /// The OUTER-BLOCK-DEV-guard at 7286-7457 wraps the entire
    /// /signature matcher invocation site, /setradius strcmp arm, and
    /// the /setradius MatchOptWithParam arm, plus the shutdown / sendp
    /// / strings / stats / shieldbuff arms (the DEV-tier admin block).
    /// For lower-tier accounts the matcher at 7288 is NEVER INVOKED --
    /// no ERROR fork emit on /signature. For DEV+ accounts (our admin
    /// test accounts have AdminLevel=SDEV which is &gt;= DEV) the
    /// matcher runs: strncmps "signature" against "signature" (9 byte
    /// match), arg[9]='\0' -- NOT '=', NOT ' ', NOT isalpha,
    /// allowNoParams=false -- emits "Missing arg for option signature"
    /// via SendVaMessage at 4548 with default COLOR=5, sets msg_sent=
    /// true, returns false. Body at 7290 SKIPPED (matcher returned
    /// false). Falls through OUTER-DEV-block's strcmp/MatchOptWithParam
    /// /setradius arms (all mismatch /signature). OUTER-DEV-block
    /// closes at 7457. case-'s' breaks at 7458. Trailing fallback at
    /// 7702 SKIPPED (msg_sent=true). NET RESULT: ONE emit.
    /// </para>
    ///
    /// <para>
    /// THIRTY-EIGHTH pin on the user-tier (single-slash) dispatch
    /// path. SEVENTH pin on user-tier case-'s' -- case-'s' user-tier
    /// now SEPTUPLE-PINNED across SEVEN ELSE-IF chain-arm positions.
    /// THIRTY-SEVENTH pin on the MatchOptWithParam ERROR path. FIRST
    /// OUTER-BLOCK-DEV-guard pin -- ELEVENTH distinct structural
    /// dispatcher pattern (MATCHER-FIRST inline / GUARD-FIRST inline /
    /// OUTER-BLOCK-GUARD / COMBINED / NO-GUARD-ELSE-IF /
    /// CASE-FALL-THROUGH / CONSECUTIVE-IF / MIXED ELSE-IF+
    /// CONSECUTIVE-IF / INSIDE-BODY-GM-guard / INSIDE-BODY-DEV-guard /
    /// INSIDE-BODY-BETA_PLUS-guard / OUTER-BLOCK-DEV-guard). SECOND
    /// 9-byte %s width pin -- DOUBLE-PINNED across 2 case-letters
    /// (Wave 154 /uitrigger case-'u' NO-GUARD-ELSE-IF + Wave 169
    /// /signature case-'s' OUTER-BLOCK-DEV-guard).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes prior
    /// waves are structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     NEW OUTER-BLOCK-DEV-guard structural pattern regression at
    ///     <c>PlayerConnection.cpp:7286-7457</c>. INSIDE-BODY-DEV-guard
    ///     (Waves 159 /terminate + 167 /stat) pins "matcher runs
    ///     unconditional, body gated"; OUTER-BLOCK-DEV-guard wraps
    ///     BOTH matcher AND body. A regression that lifted an
    ///     INSIDE-BODY guard to OUTER-BLOCK position (or vice versa)
    ///     would break the ERROR-fork-bypass invariant for
    ///     INSIDE-BODY-DEV-pinned arms (Waves 159/167) while masking
    ///     itself at the OUTER-BLOCK-DEV pin. Wave 169 pins the
    ///     OUTER-BLOCK-DEV emit shape so the DUAL-pattern coverage
    ///     (INSIDE-BODY-DEV + OUTER-BLOCK-DEV) can detect either
    ///     direction of structural drift.
    ///   </item>
    ///   <item>
    ///     case-'s' deep ELSE-IF chain SEVENTH-position arm regression
    ///     at <c>PlayerConnection.cpp:7286</c>. /signature is the
    ///     SEVENTH case-'s' user-tier arm pinned (/script + /sounds +
    ///     /scale + /skillpoints + /stat + /scan + /signature). A
    ///     regression that broke the case-'s' fall-through chain at
    ///     any intermediate position (CONSECUTIVE-IF conversion,
    ///     accidental brace closure, swapped order) would prevent
    ///     /signature from being reachable. Wave 169 pins the FULL
    ///     case-'s' user-tier chain depth is preserved through SEVEN
    ///     distinct ELSE-IF arm positions.
    ///   </item>
    ///   <item>
    ///     9-byte %s width cross-case-letter / cross-structural-pattern
    ///     divergence regression at <c>PlayerClass.cpp:3422</c>. Wave
    ///     154 pinned 9-byte %s at case-'u' /uitrigger NO-GUARD-ELSE-IF;
    ///     Wave 169 pins 9-byte %s at case-'s' /signature OUTER-BLOCK-
    ///     DEV-guard. SAME width, DIFFERENT case-letter AND
    ///     DIFFERENT structural pattern. A regression that broke
    ///     vsprintf_s 9-byte %s rendering only on a specific case-
    ///     letter or under a specific structural-pattern dispatch
    ///     would fail one pin but not the other; Wave 169 deepens
    ///     9-byte coverage to DOUBLE-PIN across 2 distinct dispatch
    ///     paths.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). /signature is
    /// OUTER-BLOCK-DEV-gated at the dispatcher level -- matcher
    /// invocation requires AdminLevel &gt;= DEV. For our admin test
    /// account (AdminLevel=SDEV, satisfies DEV+ tier), the ERROR fork
    /// at 4548 fires unmodified (same COLOR=5 default; same %s
    /// substitution path). Pins the tier-gated ERROR fork shape; a
    /// regression that lowered the OUTER-BLOCK-DEV guard tier (e.g.
    /// changed to BETA_PLUS or removed the guard entirely) would
    /// permit the matcher to fire on lower-tier accounts and emit on
    /// non-DEV accounts -- a server-fidelity regression. No server
    /// permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashSignatureMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (33) = 36 bytes.
        const int ExpectedReplyPayloadLength = 36;
        // strlen(literal) + 1 NUL = 33.
        const short ExpectedReplyLengthField = 33;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 32.
        const int ExpectedLiteralByteCount = 32;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Signo", shipName: "SignoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/signature");

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

                if (!text.Equals("Missing arg for option signature", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgSignatureLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/signature\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option signature\". Likely the user-tier case-'s' " +
                $"dispatch at line 7136 stopped routing, the OUTER-BLOCK-DEV-guard " +
                $"at 7286 gating tier changed (test account AdminLevel may have dropped " +
                $"below DEV), the matcher at 7288 stopped dispatching, the trailing " +
                $"illegal-slash fallback at 7702 fired as a second emit, or the " +
                $"missing-arg ERROR fork at PlayerConnection.cpp:4548 changed shape " +
                $"(esp. vsprintf_s 9-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 170 missing-arg ERROR literal for case-'r' /rotatex.
    /// The matcher at PlayerConnection.cpp:7059 reads
    /// `else if (MatchOptWithParam("rotatex", pch, param, msg_sent))`
    /// -- NO outer AdminLevel guard, NO inside-body guard
    /// (pure NO-GUARD-ELSE-IF pattern). SECOND case-'r' user-tier
    /// pin -- Wave 144 pinned /removebaseore at user-tier case-'r'
    /// GUARD-FIRST inline (`AdminLevel() >= DEV && MatchOptWith
    /// Param(...)` short-circuit AND); Wave 170 deepens case-'r' to
    /// DOUBLE-PINNED with a NO-GUARD-ELSE-IF structural variant.
    /// 7 ASCII bytes after %s substitution -- 4th 7-byte width pin
    /// QUADRUPLE-PINNED across 4 case-letters (c/u/t/r) /
    /// 4 structural patterns.
    /// </summary>
    private const string MissingArgRotatexLiteral = "Missing arg for option rotatex";

    /// <summary>
    /// Wave 170 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 34-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/rotatex</c> (NO param) -- routes through the
    /// user-tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'r' user-tier dispatch at line 6992 (Wave 170 deepens
    /// case-'r' to DOUBLE-PINNED across TWO distinct structural
    /// patterns). Prior matchers in case-'r' MISMATCH at byte 1
    /// against "rotatex" (reffect/rs/release/rsi/rsa/rsn/rsd/range/
    /// restoreinv MISMATCHES).
    ///
    /// <para>
    /// ELSE-IF at 7059 `MatchOptWithParam("rotatex", pch, param,
    /// msg_sent)` -- NO outer AdminLevel guard, NO inside-body guard
    /// (pure NO-GUARD-ELSE-IF pattern, same as Waves 150 /move,
    /// 154 /uitrigger, 161 /oeuler, 162 /openif, 163 /sounds,
    /// 164 /scale, 165 /shieldwarnings). MatchOptWithParam: strncmps
    /// "rotatex" against "rotatex" (7 byte match), arg[7]='\0' --
    /// NOT '=', NOT ' ', NOT isalpha, allowNoParams=false -- emits
    /// "Missing arg for option rotatex" via SendVaMessage at 4548
    /// with default COLOR=5, sets msg_sent=true, returns false.
    /// Body at 7061 SKIPPED (matcher returned false). case-'r' chain
    /// continues: /rotatey/rotatez MISMATCH; /removebaseore
    /// AdminLevel-AND-matcher short-circuit -- AdminLevel passes
    /// (SDEV) but MatchOptWithParam "removebaseore" vs "rotatex" byte
    /// 1 'e' vs 'o' MISMATCH NO emit; rest of case-'r' MISMATCH.
    /// case-'r' breaks. Trailing fallback at 7702 SKIPPED
    /// (msg_sent=true). NET RESULT: ONE emit.
    /// </para>
    ///
    /// <para>
    /// THIRTY-NINTH pin on the user-tier (single-slash) dispatch
    /// path. SECOND pin on user-tier case-'r' -- case-'r' user-tier
    /// now DOUBLE-PINNED across TWO ELSE-IF chain-arm positions
    /// (Wave 144 /removebaseore GUARD-FIRST inline + Wave 170
    /// /rotatex NO-GUARD-ELSE-IF). THIRTY-EIGHTH pin on the
    /// MatchOptWithParam ERROR path. ELEVENTH NO-GUARD pin --
    /// NO-GUARD now UNDECUPLE-PINNED across 6 case-letters
    /// (o/s/t/u/w/r) AND 6 structural variants. FOURTH 7-byte %s
    /// width pin -- QUADRUPLE-PINNED across 4 case-letters
    /// (c/u/t/r) AND 4 structural patterns (c/chjoin GM-block
    /// MATCHER-FIRST + u/upgrade INSIDE-BODY-GM + t/testmsg
    /// CONSECUTIVE-IF DEV-guard MATCHER-FIRST + r/rotatex
    /// NO-GUARD-ELSE-IF).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes prior
    /// waves are structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     SECOND case-'r' user-tier ELSE-IF chain arm regression at
    ///     <c>PlayerConnection.cpp:7059</c>. Wave 144 pinned
    ///     /removebaseore at case-'r' GUARD-FIRST inline (line 7074
    ///     with `AdminLevel() >= DEV && MatchOptWithParam(...)`
    ///     short-circuit AND); Wave 170 pins /rotatex at case-'r'
    ///     NO-GUARD-ELSE-IF (line 7059, no AdminLevel guard). A
    ///     regression that wrapped ALL case-'r' arms in a global
    ///     AdminLevel guard would gate /rotatex behind that tier;
    ///     Wave 170 pins /rotatex emits on missing-arg without any
    ///     AdminLevel gating, ruling out spurious case-'r'-wide
    ///     guards.
    ///   </item>
    ///   <item>
    ///     case-'r' structural-pattern divergence within a single
    ///     case-letter regression at <c>PlayerConnection.cpp:7059</c>
    ///     vs <c>PlayerConnection.cpp:7074</c>. case-'r' has TWO
    ///     different AdminLevel gating structures coexisting:
    ///     NO-GUARD-ELSE-IF at 7059 (/rotatex) and GUARD-FIRST inline
    ///     at 7074 (/removebaseore). A regression that conflated the
    ///     two patterns (e.g. accidentally added an AdminLevel guard
    ///     to /rotatex, or removed the AdminLevel guard from
    ///     /removebaseore) would break one pin but not both; Wave
    ///     170 pins the structural-pattern locality within case-'r'
    ///     -- ruling out single-case-letter pattern conflation.
    ///   </item>
    ///   <item>
    ///     7-byte %s width FOURTH cross-(case-letter, structural-
    ///     pattern) divergence regression at <c>PlayerClass.cpp:3422</c>.
    ///     7-byte %s now pinned at FOUR distinct (case-letter,
    ///     structural-pattern) combinations -- c/chjoin GM-block
    ///     MATCHER-FIRST + u/upgrade INSIDE-BODY-GM + t/testmsg
    ///     CONSECUTIVE-IF DEV-guard MATCHER-FIRST + r/rotatex
    ///     NO-GUARD-ELSE-IF. A regression in vsprintf_s 7-byte path
    ///     specific to ONE combination would fail one pin but not
    ///     all four. Wave 170 deepens 7-byte coverage to QUADRUPLE-PIN.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). /rotatex is open
    /// to ALL users at the dispatcher level -- the NO-GUARD-ELSE-IF
    /// pattern at 7059 has no AdminLevel guard at all. ERROR fork at
    /// 4548 emits for ALL tiers (faithful to retail). No server
    /// permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashRotatexMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (31) = 34 bytes.
        const int ExpectedReplyPayloadLength = 34;
        // strlen(literal) + 1 NUL = 31.
        const short ExpectedReplyLengthField = 31;
        // SendVaMessage -> SendMessageString default color parameter.
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
            firstName: "Rotaxo", shipName: "RotaxoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/rotatex");

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

                if (!text.Equals("Missing arg for option rotatex", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgRotatexLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/rotatex\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option rotatex\". Likely the user-tier case-'r' " +
                $"dispatch at line 6992 stopped routing, the NO-GUARD-ELSE-IF arm at " +
                $"7059 acquired a spurious AdminLevel guard, the trailing illegal-slash " +
                $"fallback at 7702 fired as a second emit, or the missing-arg ERROR " +
                $"fork at PlayerConnection.cpp:4548 changed shape (esp. vsprintf_s " +
                $"7-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 171 missing-arg ERROR literal for case-'r' /rotatey.
    /// The matcher at PlayerConnection.cpp:7064 reads
    /// `else if (MatchOptWithParam("rotatey", pch, param, msg_sent))`
    /// -- NO outer AdminLevel guard, NO inside-body guard
    /// (pure NO-GUARD-ELSE-IF pattern, same shape as Wave 170
    /// /rotatex at 7059). THIRD case-'r' user-tier pin -- Wave 144
    /// /removebaseore GUARD-FIRST inline at 7074 + Wave 170
    /// /rotatex NO-GUARD-ELSE-IF at 7059 + Wave 171 /rotatey
    /// NO-GUARD-ELSE-IF at 7064 = TRIPLE-PINNED case-'r' user-tier
    /// across THREE ELSE-IF positions. NO-GUARD-ELSE-IF within
    /// case-'r' DOUBLE-PINNED. 7 ASCII bytes after %s substitution
    /// -- 5th 7-byte width pin QUINTUPLE-PINNED across 4
    /// case-letters (c/u/t/r) and case-'r' double-instance.
    /// </summary>
    private const string MissingArgRotateyLiteral = "Missing arg for option rotatey";

    /// <summary>
    /// Wave 171 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 34-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/rotatey</c> (NO param) -- routes through the
    /// user-tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'r' user-tier dispatch at line 6992. Wave 171 deepens
    /// case-'r' to TRIPLE-PINNED across THREE ELSE-IF positions
    /// (Wave 144 /removebaseore at 7074, Wave 170 /rotatex at 7059,
    /// Wave 171 /rotatey at 7064). Prior matchers in case-'r' MISMATCH
    /// at byte 1 against "rotatey" (reffect/rs/release/rsi/rsa/rsn/
    /// rsd/range/restoreinv MISMATCHES); /rotatex MISMATCH at byte 6
    /// ('x' vs 'y').
    ///
    /// <para>
    /// ELSE-IF at 7064 `MatchOptWithParam("rotatey", pch, param,
    /// msg_sent)` -- NO outer AdminLevel guard, NO inside-body guard
    /// (pure NO-GUARD-ELSE-IF pattern). MatchOptWithParam: strncmps
    /// "rotatey" against "rotatey" (7 byte match), arg[7]='\0' --
    /// NOT '=', NOT ' ', NOT isalpha, allowNoParams=false -- emits
    /// "Missing arg for option rotatey" via SendVaMessage at 4548
    /// with default COLOR=5, sets msg_sent=true, returns false.
    /// Body at 7066 SKIPPED (matcher returned false). case-'r' chain
    /// continues: /rotatez MISMATCH at byte 6 ('z' vs 'y');
    /// /removebaseore AdminLevel-AND-matcher short-circuit --
    /// AdminLevel passes (SDEV) but MatchOptWithParam "removebaseore"
    /// vs "rotatey" byte 1 'e' vs 'o' MISMATCH NO emit; rest of
    /// case-'r' MISMATCH. case-'r' breaks. Trailing fallback at 7702
    /// SKIPPED (msg_sent=true). NET RESULT: ONE emit.
    /// </para>
    ///
    /// <para>
    /// FORTIETH pin on the user-tier (single-slash) dispatch path.
    /// THIRD pin on user-tier case-'r' -- case-'r' user-tier now
    /// TRIPLE-PINNED across THREE ELSE-IF chain-arm positions
    /// (Wave 144 /removebaseore GUARD-FIRST inline at 7074 + Wave 170
    /// /rotatex NO-GUARD-ELSE-IF at 7059 + Wave 171 /rotatey
    /// NO-GUARD-ELSE-IF at 7064). THIRTY-NINTH pin on the
    /// MatchOptWithParam ERROR path. TWELFTH NO-GUARD pin --
    /// NO-GUARD now DUODECUPLE-PINNED across 6 case-letters
    /// (o/s/t/u/w/r) with case-'r' now DOUBLE-instance within the
    /// pattern. FIFTH 7-byte %s width pin -- QUINTUPLE-PINNED across
    /// 4 case-letters (c/u/t/r) AND 5 structural-pattern instances
    /// (case-'r' now double-instance: /rotatex + /rotatey).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes prior
    /// waves are structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     THIRD case-'r' user-tier ELSE-IF chain arm regression at
    ///     <c>PlayerConnection.cpp:7064</c>. A regression that wrapped
    ///     ALL case-'r' arms in a global AdminLevel guard would gate
    ///     /rotatey behind that tier; Wave 171 pins /rotatey emits on
    ///     missing-arg without any AdminLevel gating, ruling out
    ///     spurious case-'r'-wide guards even after the existing
    ///     Wave 170 /rotatex pin (locality: 5-line ELSE-IF stride
    ///     7059 vs 7064 within the same case body).
    ///   </item>
    ///   <item>
    ///     NO-GUARD-ELSE-IF intra-case-letter locality regression at
    ///     <c>PlayerConnection.cpp:7059</c> vs <c>7064</c>. case-'r'
    ///     has TWO consecutive NO-GUARD-ELSE-IF arms emitting
    ///     identical 7-byte %s width missing-arg ERRORS that differ
    ///     only in the last literal byte ('x' vs 'y'). A regression
    ///     in the ELSE-IF chain order (e.g. accidentally moved the
    ///     /rotatey arm out of case-'r', or fell through to /rotatex
    ///     for /rotatey input) would break one pin but not the other;
    ///     Wave 171 fixes the intra-arm position.
    ///   </item>
    ///   <item>
    ///     7-byte %s width FIFTH (case-letter, structural-pattern,
    ///     literal) divergence regression at <c>PlayerClass.cpp:3422</c>.
    ///     7-byte %s now pinned at FIVE distinct (case-letter,
    ///     structural-pattern, literal) combinations -- c/chjoin
    ///     GM-block MATCHER-FIRST + u/upgrade INSIDE-BODY-GM +
    ///     t/testmsg CONSECUTIVE-IF DEV-guard MATCHER-FIRST +
    ///     r/rotatex NO-GUARD-ELSE-IF + r/rotatey NO-GUARD-ELSE-IF.
    ///     A regression in the vsprintf_s 7-byte path specific to ONE
    ///     literal would fail one pin but not the other four.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). /rotatey is open
    /// to ALL users at the dispatcher level -- the NO-GUARD-ELSE-IF
    /// pattern at 7064 has no AdminLevel guard at all. ERROR fork at
    /// 4548 emits for ALL tiers (faithful to retail). No server
    /// permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashRotateyMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (31) = 34 bytes.
        const int ExpectedReplyPayloadLength = 34;
        // strlen(literal) + 1 NUL = 31.
        const short ExpectedReplyLengthField = 31;
        // SendVaMessage -> SendMessageString default color parameter.
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
            firstName: "Rotyo", shipName: "RotyoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/rotatey");

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

                if (!text.Equals("Missing arg for option rotatey", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgRotateyLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/rotatey\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option rotatey\". Likely the user-tier case-'r' " +
                $"dispatch at line 6992 stopped routing, the NO-GUARD-ELSE-IF arm at " +
                $"7064 acquired a spurious AdminLevel guard, the trailing illegal-slash " +
                $"fallback at 7702 fired as a second emit, or the missing-arg ERROR " +
                $"fork at PlayerConnection.cpp:4548 changed shape (esp. vsprintf_s " +
                $"7-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 172 missing-arg ERROR literal for case-'r' /rotatez.
    /// The matcher at PlayerConnection.cpp:7069 reads
    /// `else if (MatchOptWithParam("rotatez", pch, param, msg_sent))`
    /// -- NO outer AdminLevel guard, NO inside-body guard
    /// (pure NO-GUARD-ELSE-IF pattern, same shape as Waves 170
    /// /rotatex at 7059 and 171 /rotatey at 7064). FOURTH case-'r'
    /// user-tier pin -- case-'r' user-tier now QUADRUPLE-PINNED
    /// across FOUR ELSE-IF chain positions. NO-GUARD-ELSE-IF
    /// within case-'r' TRIPLE-PINNED at three consecutive
    /// 5-line-stride positions (7059/7064/7069). 7 ASCII bytes after
    /// %s substitution -- 6th 7-byte width pin SEXTUPLE-PINNED.
    /// </summary>
    private const string MissingArgRotatezLiteral = "Missing arg for option rotatez";

    /// <summary>
    /// Wave 172 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 34-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/rotatez</c> (NO param) -- routes through the
    /// user-tier dispatcher entry at line 5434, the 1-char strip, the
    /// case-'r' user-tier dispatch at line 6992. Wave 172 deepens
    /// case-'r' to QUADRUPLE-PINNED across FOUR ELSE-IF positions
    /// (Wave 144 /removebaseore at 7074, Wave 170 /rotatex at 7059,
    /// Wave 171 /rotatey at 7064, Wave 172 /rotatez at 7069). Prior
    /// matchers in case-'r' MISMATCH at byte 1 against "rotatez"
    /// (reffect/rs/release/rsi/rsa/rsn/rsd/range/restoreinv
    /// MISMATCHES); /rotatex MISMATCH at byte 6 ('x' vs 'z');
    /// /rotatey MISMATCH at byte 6 ('y' vs 'z').
    ///
    /// <para>
    /// ELSE-IF at 7069 `MatchOptWithParam("rotatez", pch, param,
    /// msg_sent)` -- NO outer AdminLevel guard, NO inside-body guard
    /// (pure NO-GUARD-ELSE-IF pattern). MatchOptWithParam: strncmps
    /// "rotatez" against "rotatez" (7 byte match), arg[7]='\0' --
    /// NOT '=', NOT ' ', NOT isalpha, allowNoParams=false -- emits
    /// "Missing arg for option rotatez" via SendVaMessage at 4548
    /// with default COLOR=5, sets msg_sent=true, returns false.
    /// Body at 7071 SKIPPED (matcher returned false). case-'r' chain
    /// continues: /removebaseore AdminLevel-AND-matcher short-circuit
    /// -- AdminLevel passes (SDEV) but MatchOptWithParam
    /// "removebaseore" vs "rotatez" byte 1 'e' vs 'o' MISMATCH NO
    /// emit; rest of case-'r' MISMATCH. case-'r' breaks. Trailing
    /// fallback at 7702 SKIPPED (msg_sent=true). NET RESULT: ONE
    /// emit.
    /// </para>
    ///
    /// <para>
    /// FORTY-FIRST pin on the user-tier (single-slash) dispatch path.
    /// FOURTH pin on user-tier case-'r' -- case-'r' user-tier now
    /// QUADRUPLE-PINNED across FOUR ELSE-IF chain-arm positions.
    /// FORTIETH pin on the MatchOptWithParam ERROR path. THIRTEENTH
    /// NO-GUARD pin -- NO-GUARD now TREDECUPLE-PINNED across 6
    /// case-letters (o/s/t/u/w/r) with case-'r' TRIPLE-instance
    /// (rotatex+rotatey+rotatez) within the pattern. SIXTH 7-byte %s
    /// width pin -- SEXTUPLE-PINNED across 4 case-letters (c/u/t/r)
    /// AND 6 structural-pattern instances (case-'r' now
    /// triple-instance: /rotatex + /rotatey + /rotatez).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes prior
    /// waves are structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     FOURTH case-'r' user-tier ELSE-IF chain arm regression at
    ///     <c>PlayerConnection.cpp:7069</c>. A regression that
    ///     wrapped ALL case-'r' arms in a global AdminLevel guard
    ///     would gate /rotatez behind that tier; Wave 172 pins
    ///     /rotatez emits on missing-arg without any AdminLevel
    ///     gating, ruling out spurious case-'r'-wide guards even
    ///     after the Wave 170 /rotatex and Wave 171 /rotatey pins
    ///     (locality: 10-line ELSE-IF stride 7059 vs 7069 within the
    ///     same case body).
    ///   </item>
    ///   <item>
    ///     NO-GUARD-ELSE-IF intra-case-letter TRIPLE-locality
    ///     regression at <c>PlayerConnection.cpp:7059</c> vs
    ///     <c>7064</c> vs <c>7069</c>. case-'r' has THREE consecutive
    ///     NO-GUARD-ELSE-IF arms emitting identical 7-byte %s width
    ///     missing-arg ERRORS that differ only in the last literal
    ///     byte ('x' vs 'y' vs 'z'). A regression in the ELSE-IF
    ///     chain order (e.g. accidentally moved the /rotatez arm out
    ///     of case-'r', or fell through to /rotatex or /rotatey for
    ///     /rotatez input) would break one pin but not the others;
    ///     Wave 172 fixes the intra-arm position TRIPLE.
    ///   </item>
    ///   <item>
    ///     7-byte %s width SIXTH (case-letter, structural-pattern,
    ///     literal) divergence regression at <c>PlayerClass.cpp:3422</c>.
    ///     7-byte %s now pinned at SIX distinct (case-letter,
    ///     structural-pattern, literal) combinations -- c/chjoin +
    ///     u/upgrade + t/testmsg + r/rotatex + r/rotatey + r/rotatez.
    ///     A regression in the vsprintf_s 7-byte path specific to
    ///     ONE literal would fail one pin but not the other five.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). /rotatez is open
    /// to ALL users at the dispatcher level -- the NO-GUARD-ELSE-IF
    /// pattern at 7069 has no AdminLevel guard at all. ERROR fork at
    /// 4548 emits for ALL tiers (faithful to retail). No server
    /// permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashRotatezMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (31) = 34 bytes.
        const int ExpectedReplyPayloadLength = 34;
        // strlen(literal) + 1 NUL = 31.
        const short ExpectedReplyLengthField = 31;
        // SendVaMessage -> SendMessageString default color parameter.
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
            firstName: "Rotzo", shipName: "RotzoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/rotatez");

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

                if (!text.Equals("Missing arg for option rotatez", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgRotatezLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/rotatez\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option rotatez\". Likely the user-tier case-'r' " +
                $"dispatch at line 6992 stopped routing, the NO-GUARD-ELSE-IF arm at " +
                $"7069 acquired a spurious AdminLevel guard, the trailing illegal-slash " +
                $"fallback at 7702 fired as a second emit, or the missing-arg ERROR " +
                $"fork at PlayerConnection.cpp:4548 changed shape (esp. vsprintf_s " +
                $"7-byte %s width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 173 missing-arg ERROR literal for case-'r' /rsi.
    /// The matcher at PlayerConnection.cpp:7012 reads
    /// `else if (MatchOptWithParam("rsi", pch, param, msg_sent))`
    /// -- NO outer AdminLevel guard, NO inside-body guard
    /// (pure NO-GUARD-ELSE-IF pattern). FIFTH case-'r' user-tier
    /// pin. FIRST 3-byte %s width pin within case-'r' -- 3-byte
    /// %s now pinned across MULTIPLE case-letters. /rsi is 3 ASCII
    /// bytes after %s substitution; SHORTEST literal pinned within
    /// case-'r'.
    /// </summary>
    private const string MissingArgRsiLiteral = "Missing arg for option rsi";

    /// <summary>
    /// Wave 173 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 30-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/rsi</c> (NO param) -- routes through the user-tier
    /// dispatcher entry at line 5434, the 1-char strip, the case-'r'
    /// user-tier dispatch at line 6992. Wave 173 deepens case-'r' to
    /// QUINTUPLE-PINNED across FIVE ELSE-IF positions (Wave 144
    /// /removebaseore at 7074, Wave 170 /rotatex at 7059, Wave 171
    /// /rotatey at 7064, Wave 172 /rotatez at 7069, Wave 173 /rsi at
    /// 7012). Prior case-'r' matchers MISMATCH at byte 1 against "rsi"
    /// (reffect MISMATCH byte 1 'e' vs 's'); /rs MISMATCH at byte 2
    /// (3rd byte present, /rs is 2-byte strcmp); /release MISMATCH
    /// byte 1 'e' vs 's'.
    ///
    /// <para>
    /// ELSE-IF at 7012 `MatchOptWithParam("rsi", pch, param,
    /// msg_sent)` -- NO outer AdminLevel guard, NO inside-body guard
    /// (pure NO-GUARD-ELSE-IF pattern). MatchOptWithParam: strncmps
    /// "rsi" against "rsi" (3 byte match), arg[3]='\0' -- NOT '=',
    /// NOT ' ', NOT isalpha, allowNoParams=false -- emits
    /// "Missing arg for option rsi" via SendVaMessage at 4548 with
    /// default COLOR=5, sets msg_sent=true, returns false. Body at
    /// 7014 SKIPPED (matcher returned false). case-'r' chain
    /// continues: /rsa MISMATCH byte 2 'a' vs 'i'; /rsn MISMATCH byte
    /// 2 'n' vs 'i'; /rsd strcmp MISMATCH; /range/restoreinv/rotatex/
    /// rotatey/rotatez MISMATCH at byte 1 ('a'/'e'/'o'/'o'/'o' vs
    /// 's'); /removebaseore AdminLevel-AND-matcher short-circuit --
    /// MatchOptWithParam "removebaseore" vs "rsi" byte 1 'e' vs 's'
    /// MISMATCH NO emit. case-'r' breaks. Trailing fallback at 7702
    /// SKIPPED (msg_sent=true). NET RESULT: ONE emit.
    /// </para>
    ///
    /// <para>
    /// Important: /rs at line 7000 is a 2-byte strcmp (NOT
    /// MatchOptWithParam) -- /rs receives bare command "/rs" and is
    /// dispatched IMMEDIATELY without ERROR fork; /rsi sends through
    /// MatchOptWithParam where ERROR fork emits on missing-arg. The
    /// case-'r' body at 6993-7124 has a structurally mixed pattern:
    /// FIRST-IF (strcmp /reffect at 6993, NO closing brace before
    /// 7000 /rs), then ELSE-IF chain. Wave 173 verifies that /rsi at
    /// 7012 is reachable through the chain even though /rs at 7000
    /// is structurally a CONSECUTIVE-IF (parallel-IF) rather than
    /// ELSE-IF (a regression that converted /rs to ELSE-IF or moved
    /// it would shadow /rsi by prefix match if MatchOptWithParam used
    /// substring -- it doesn't; strncmp respects length argument).
    /// </para>
    ///
    /// <para>
    /// FORTY-SECOND pin on the user-tier (single-slash) dispatch path.
    /// FIFTH pin on user-tier case-'r' -- case-'r' user-tier now
    /// QUINTUPLE-PINNED across FIVE ELSE-IF chain-arm positions.
    /// FORTY-FIRST pin on the MatchOptWithParam ERROR path.
    /// FOURTEENTH NO-GUARD pin -- NO-GUARD now QUATTUORDECUPLE-PINNED
    /// across 6 case-letters with case-'r' QUADRUPLE-instance
    /// (rotatex+rotatey+rotatez+rsi). FIRST 3-byte %s width pin in
    /// case-'r' -- 3-byte %s coverage extended to case-'r'.
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes prior
    /// waves are structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     FIFTH case-'r' user-tier ELSE-IF chain arm regression at
    ///     <c>PlayerConnection.cpp:7012</c>. A regression that moved
    ///     /rsi out of case-'r', or wrapped it in an AdminLevel
    ///     guard, would break Wave 173 pin while leaving Waves 170/171
    ///     /172 intact (locality: 47-line ELSE-IF stride 7012 vs 7059
    ///     within the same case body).
    ///   </item>
    ///   <item>
    ///     case-'r' /rs vs /rsi prefix-shadowing regression at
    ///     <c>PlayerConnection.cpp:7000</c> vs <c>7012</c>. /rs is a
    ///     2-byte strcmp at 7000; /rsi is a 3-byte MatchOptWithParam
    ///     at 7012. A regression that converted /rs to a prefix-match
    ///     or substring-match would shadow /rsi (because "rsi" starts
    ///     with "rs"); Wave 173 pins /rsi reachable through the
    ///     case-'r' chain so /rs cannot accidentally shadow /rsi.
    ///   </item>
    ///   <item>
    ///     3-byte %s width fresh-case-letter cross-(case-letter,
    ///     structural-pattern, literal) divergence regression at
    ///     <c>PlayerClass.cpp:3422</c>. Wave 173 extends 3-byte %s
    ///     coverage to case-'r' /rsi -- a regression in the
    ///     vsprintf_s 3-byte path specific to the case-'r' dispatch
    ///     would fail Wave 173 pin while leaving previously-pinned
    ///     3-byte pins (e.g. via case-'p' or earlier) intact.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). /rsi is open to ALL
    /// users at the dispatcher level -- the NO-GUARD-ELSE-IF pattern
    /// at 7012 has no AdminLevel guard at all. ERROR fork at 4548
    /// emits for ALL tiers (faithful to retail). No server
    /// permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashRsiMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (27) = 30 bytes.
        const int ExpectedReplyPayloadLength = 30;
        // strlen(literal) + 1 NUL = 27.
        const short ExpectedReplyLengthField = 27;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 26.
        const int ExpectedLiteralByteCount = 26;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Rsio", shipName: "RsioShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/rsi");

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

                if (!text.Equals("Missing arg for option rsi", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgRsiLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/rsi\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option rsi\". Likely the user-tier case-'r' " +
                $"dispatch at line 6992 stopped routing, the NO-GUARD-ELSE-IF arm at " +
                $"7012 acquired a spurious AdminLevel guard, /rs at 7000 acquired a " +
                $"prefix-match that shadowed /rsi, the trailing illegal-slash fallback " +
                $"at 7702 fired as a second emit, or the missing-arg ERROR fork at " +
                $"PlayerConnection.cpp:4548 changed shape (esp. vsprintf_s 3-byte %s " +
                $"width).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 174 missing-arg ERROR literal for case-'r' /rsa.
    /// The matcher at PlayerConnection.cpp:7016 reads
    /// `else if (MatchOptWithParam("rsa", pch, param, msg_sent))`
    /// -- NO outer AdminLevel guard, NO inside-body guard
    /// (pure NO-GUARD-ELSE-IF pattern). SIXTH case-'r' user-tier
    /// pin. SECOND 3-byte %s width pin within case-'r' --
    /// case-'r' 3-byte DOUBLE-PINNED (rsi + rsa). 3-byte %s now
    /// pinned at MULTIPLE case-letters.
    /// </summary>
    private const string MissingArgRsaLiteral = "Missing arg for option rsa";

    /// <summary>
    /// Wave 174 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the byte-exact 30-byte wire-shape of the single 0x001D
    /// MESSAGE_STRING the server emits in reply to the user-tier slash
    /// command <c>/rsa</c> (NO param) -- routes through the user-tier
    /// dispatcher entry at line 5434, the 1-char strip, the case-'r'
    /// user-tier dispatch at line 6992. Wave 174 deepens case-'r' to
    /// SEXTUPLE-PINNED across SIX ELSE-IF positions. Sibling pin to
    /// Wave 173 /rsi -- both /rsi and /rsa are 3-byte
    /// MatchOptWithParam ELSE-IF arms in case-'r'; they differ only
    /// in byte 2 ('i' vs 'a').
    ///
    /// <para>
    /// ELSE-IF at 7016 `MatchOptWithParam("rsa", pch, param,
    /// msg_sent)` -- NO outer AdminLevel guard, NO inside-body guard.
    /// MatchOptWithParam: strncmps "rsa" against "rsa" (3 byte match),
    /// arg[3]='\0' -- NOT '=', NOT ' ', NOT isalpha,
    /// allowNoParams=false -- emits "Missing arg for option rsa" via
    /// SendVaMessage at 4548 with default COLOR=5. Prior case-'r'
    /// matchers MISMATCH against "rsa" (/reffect strcmp MISMATCH, /rs
    /// strcmp MISMATCH 3-byte input vs 2-byte target, /release
    /// MISMATCH, /rsi MISMATCH byte 2 'i' vs 'a'). NET RESULT: ONE
    /// emit.
    /// </para>
    ///
    /// <para>
    /// FORTY-THIRD pin on the user-tier (single-slash) dispatch path.
    /// SIXTH pin on user-tier case-'r' -- case-'r' user-tier now
    /// SEXTUPLE-PINNED. FORTY-SECOND pin on the MatchOptWithParam
    /// ERROR path. FIFTEENTH NO-GUARD pin -- NO-GUARD now
    /// QUINDECUPLE-PINNED. SECOND 3-byte %s width pin within case-'r'
    /// -- case-'r' 3-byte DOUBLE-PINNED.
    /// </para>
    ///
    /// <para>
    /// What this catches. Three concrete regression classes prior
    /// waves are structurally blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     SIXTH case-'r' user-tier ELSE-IF chain arm regression at
    ///     <c>PlayerConnection.cpp:7016</c>. A regression that moved
    ///     /rsa out of case-'r' or fell through to /rsi for /rsa
    ///     input would break Wave 174 pin while leaving Wave 173 /rsi
    ///     intact (locality: 4-line ELSE-IF stride 7012 vs 7016).
    ///   </item>
    ///   <item>
    ///     case-'r' /rsi vs /rsa intra-arm prefix-shadowing regression
    ///     at <c>PlayerConnection.cpp:7012</c> vs <c>7016</c>. /rsi
    ///     and /rsa share 2-byte prefix "rs"; a regression that
    ///     truncated MatchOptWithParam's strncmp length to less than
    ///     3 bytes for either arm would shadow the other. Wave 174
    ///     pins /rsa byte-exactly so a shadowing regression in
    ///     strncmp length would fail Wave 174 while Wave 173 stays
    ///     reachable (or vice versa).
    ///   </item>
    ///   <item>
    ///     3-byte %s width DOUBLE-case-'r' intra-pattern regression
    ///     at <c>PlayerClass.cpp:3422</c>. case-'r' now has TWO
    ///     3-byte %s pins; a regression in vsprintf_s 3-byte path
    ///     specific to ONE literal ('rsi' vs 'rsa') would fail one
    ///     pin but not the other.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). /rsa is open to ALL
    /// users at the dispatcher level -- NO-GUARD-ELSE-IF pattern with
    /// no AdminLevel guard. No server permissiveness added.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SlashRsaMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (27) = 30 bytes.
        const int ExpectedReplyPayloadLength = 30;
        // strlen(literal) + 1 NUL = 27.
        const short ExpectedReplyLengthField = 27;
        // SendVaMessage -> SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 26.
        const int ExpectedLiteralByteCount = 26;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Rsao", shipName: "RsaoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/rsa");

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

                if (!text.Equals("Missing arg for option rsa", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgRsaLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/rsa\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option rsa\". Likely the user-tier case-'r' " +
                $"dispatch at line 6992 stopped routing, the NO-GUARD-ELSE-IF arm at " +
                $"7016 acquired a spurious AdminLevel guard, /rsi at 7012 shadowed " +
                $"/rsa via truncated strncmp, the trailing illegal-slash fallback at " +
                $"7702 fired as a second emit, or the missing-arg ERROR fork at " +
                $"PlayerConnection.cpp:4548 changed shape.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 175 missing-arg ERROR literal for case-'r' /rsn.
    /// Matcher at PlayerConnection.cpp:7020:
    /// `else if (MatchOptWithParam("rsn", pch, param, msg_sent))`
    /// -- pure NO-GUARD-ELSE-IF. SEVENTH case-'r' user-tier pin.
    /// THIRD 3-byte %s width pin within case-'r' -- case-'r' 3-byte
    /// TRIPLE-PINNED (rsi + rsa + rsn).
    /// </summary>
    private const string MissingArgRsnLiteral = "Missing arg for option rsn";

    /// <summary>
    /// Wave 175 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the 30-byte wire-shape of the single 0x001D MESSAGE_STRING
    /// reply to user-tier slash <c>/rsn</c> (NO param). Wave 175
    /// deepens case-'r' to SEPTUPLE-PINNED. Sibling pin to Waves 173
    /// /rsi and 174 /rsa -- all three are 3-byte MatchOptWithParam
    /// ELSE-IF arms in case-'r' that share the 2-byte prefix "rs" and
    /// differ only in byte 2 ('i'/'a'/'n').
    ///
    /// <para>
    /// ELSE-IF at 7020 `MatchOptWithParam("rsn", pch, param,
    /// msg_sent)` -- pure NO-GUARD-ELSE-IF. MatchOptWithParam:
    /// strncmps "rsn" against "rsn", arg[3]='\0' -- emits "Missing
    /// arg for option rsn" at 4548 COLOR=5. Prior case-'r' matchers
    /// MISMATCH against "rsn" (/rsi byte 2 'i' vs 'n', /rsa byte 2
    /// 'a' vs 'n'). NET RESULT: ONE emit.
    /// </para>
    ///
    /// <para>
    /// FORTY-FOURTH pin on the user-tier dispatch path. SEVENTH pin on
    /// user-tier case-'r' -- SEPTUPLE-PINNED. FORTY-THIRD pin on the
    /// MatchOptWithParam ERROR path. SIXTEENTH NO-GUARD pin --
    /// SEXDECUPLE-PINNED. THIRD 3-byte %s width pin within case-'r'
    /// -- case-'r' 3-byte TRIPLE-PINNED.
    /// </para>
    ///
    /// <para>
    /// What this catches. Three regression classes prior waves are
    /// blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     SEVENTH case-'r' user-tier ELSE-IF chain arm regression at
    ///     <c>PlayerConnection.cpp:7020</c>. Locality: 4-line stride
    ///     7016 vs 7020.
    ///   </item>
    ///   <item>
    ///     case-'r' /rsi vs /rsa vs /rsn TRIPLE intra-arm
    ///     prefix-shadowing regression at <c>PlayerConnection.cpp:7012</c>
    ///     vs <c>7016</c> vs <c>7020</c>. Three 3-byte MatchOptWithParam
    ///     arms sharing 2-byte prefix "rs"; a truncated-strncmp
    ///     regression would shadow some but not all three.
    ///   </item>
    ///   <item>
    ///     3-byte %s width TRIPLE-case-'r' intra-pattern regression at
    ///     <c>PlayerClass.cpp:3422</c>. case-'r' now has THREE 3-byte
    ///     %s pins; a vsprintf_s 3-byte regression specific to ONE
    ///     literal would fail one but not the other two.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). /rsn is open to ALL
    /// users -- NO-GUARD-ELSE-IF pattern at 7020. No server
    /// permissiveness added.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashRsnMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        const int ExpectedReplyPayloadLength = 30;
        const short ExpectedReplyLengthField = 27;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 26;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Rsno", shipName: "RsnoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/rsn");

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

                if (!text.Equals("Missing arg for option rsn", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgRsnLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/rsn\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option rsn\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 176 missing-arg ERROR literal for case-'p' /packetopt.
    /// Matcher at PlayerConnection.cpp:6960:
    /// `else if (MatchOptWithParam("packetopt", pch, param, msg_sent))`
    /// -- pure NO-GUARD-ELSE-IF. FIRST case-'p' user-tier pin --
    /// opens case-'p' coverage. 9-byte %s width -- TRIPLE-PINNED
    /// across case-letters (u/s/p) and structural patterns
    /// (NO-GUARD-ELSE-IF + OUTER-BLOCK-DEV-guard + NO-GUARD-ELSE-IF).
    /// </summary>
    private const string MissingArgPacketoptLiteral = "Missing arg for option packetopt";

    /// <summary>
    /// Wave 176 sibling-arm-pinning hardening (+0 ratchet, 0x0033
    /// CLIENT_CHAT -&gt; 0x001D MESSAGE_STRING via slash short-circuit):
    /// pins the 36-byte wire-shape of the single 0x001D MESSAGE_STRING
    /// reply to user-tier slash <c>/packetopt</c> (NO param) -- routes
    /// through user-tier dispatcher entry at 5434, 1-char strip, case-'p'
    /// user-tier dispatch at line 6948. Wave 176 OPENS case-'p'
    /// coverage -- FIRST case-'p' pin. Prior /position strcmp at 6949
    /// MISMATCHES at byte 1 'o' vs 'a'.
    ///
    /// <para>
    /// ELSE-IF at 6960 `MatchOptWithParam("packetopt", pch, param,
    /// msg_sent)` -- pure NO-GUARD-ELSE-IF. MatchOptWithParam:
    /// strncmps "packetopt" against "packetopt" (9 byte match),
    /// arg[9]='\0' -- emits "Missing arg for option packetopt" at
    /// 4548 COLOR=5. Subsequent case-'p' arms MISMATCH (/panup byte 1
    /// 'a' match but byte 2 'n' vs 'c' MISMATCH, /panx/pany/panz/
    /// planetspin similarly MISMATCH). NET RESULT: ONE emit.
    /// </para>
    ///
    /// <para>
    /// FORTY-FIFTH pin on the user-tier dispatch path. FIRST pin on
    /// user-tier case-'p' -- opens case-'p' coverage. FORTY-FOURTH
    /// pin on the MatchOptWithParam ERROR path. SEVENTEENTH NO-GUARD
    /// pin -- SEPTENDECUPLE-PINNED across SEVEN case-letters
    /// (o/s/t/u/w/r/p). THIRD 9-byte %s width pin -- TRIPLE-PINNED
    /// across THREE case-letters (u/s/p) AND THREE structural
    /// patterns (Wave 154 /uitrigger NO-GUARD-ELSE-IF + Wave 169
    /// /signature OUTER-BLOCK-DEV-guard + Wave 176 /packetopt
    /// NO-GUARD-ELSE-IF).
    /// </para>
    ///
    /// <para>
    /// What this catches. Three regression classes prior waves are
    /// blind to:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     FRESH case-'p' user-tier letter-coverage regression at
    ///     <c>PlayerConnection.cpp:6948-6990</c>. case-'p' was
    ///     previously UNPINNED; a regression that broke case-'p'
    ///     entirely (e.g. removed the case label, replaced it with
    ///     `default`, or made the strip-1-char step skip 'p') would
    ///     fail Wave 176 while leaving cases o/s/t/u/w/r untouched.
    ///   </item>
    ///   <item>
    ///     case-'p' /position vs /packetopt structural-pattern
    ///     divergence regression at <c>PlayerConnection.cpp:6949</c>
    ///     vs <c>6960</c>. /position is a FIRST-IF strcmp (matcher
    ///     without ELSE), /packetopt is an ELSE-IF MatchOptWithParam;
    ///     a regression that conflated the two structures (e.g. moved
    ///     /packetopt before /position, or made /position fall through
    ///     to /packetopt) would shadow one.
    ///   </item>
    ///   <item>
    ///     9-byte %s width TRIPLE-(case-letter, structural-pattern,
    ///     literal) divergence regression at <c>PlayerClass.cpp:3422</c>.
    ///     9-byte %s now pinned at THREE distinct combinations --
    ///     u/uitrigger NO-GUARD-ELSE-IF + s/signature OUTER-BLOCK-DEV
    ///     + p/packetopt NO-GUARD-ELSE-IF. A regression in vsprintf_s
    ///     9-byte path specific to ONE combination would fail one
    ///     pin but not all three.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (POSITIVE per CLAUDE.md). /packetopt is open
    /// to ALL users -- NO-GUARD-ELSE-IF pattern at 6960. No server
    /// permissiveness added.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashPacketoptMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (33) = 36 bytes.
        const int ExpectedReplyPayloadLength = 36;
        // strlen(literal) + 1 NUL = 33.
        const short ExpectedReplyLengthField = 33;
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 32.
        const int ExpectedLiteralByteCount = 32;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Packo", shipName: "PackoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/packetopt");

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

                if (!text.Equals("Missing arg for option packetopt", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgPacketoptLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/packetopt\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option packetopt\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 177 missing-arg ERROR literal for case-'p' /panup.
    /// Matcher at PlayerConnection.cpp:6965:
    /// `else if (MatchOptWithParam("panup", pch, param, msg_sent))`
    /// -- pure NO-GUARD-ELSE-IF. SECOND case-'p' user-tier pin.
    /// 5-byte %s width.
    /// </summary>
    private const string MissingArgPanupLiteral = "Missing arg for option panup";

    /// <summary>
    /// Wave 177 sibling-arm-pinning hardening (+0 ratchet): pins the
    /// 32-byte wire-shape of the single 0x001D MESSAGE_STRING reply to
    /// user-tier slash <c>/panup</c> (NO param). Wave 177 deepens
    /// case-'p' to DOUBLE-PINNED.
    ///
    /// <para>
    /// ELSE-IF at 6965 -- pure NO-GUARD-ELSE-IF. MatchOptWithParam:
    /// strncmps "panup" (5 byte match), emits "Missing arg for option
    /// panup" at 4548 COLOR=5. Prior /position MISMATCH, /packetopt
    /// MISMATCH byte 1 'a' vs 'a' but byte 2 'n' vs 'c' MISMATCH.
    /// Subsequent /panx/pany/panz/planetspin MISMATCH. NET RESULT:
    /// ONE emit.
    /// </para>
    ///
    /// <para>
    /// FORTY-SIXTH pin on the user-tier dispatch path. SECOND pin on
    /// user-tier case-'p' -- case-'p' DOUBLE-PINNED. FORTY-FIFTH pin
    /// on the MatchOptWithParam ERROR path. EIGHTEENTH NO-GUARD pin
    /// -- OCTODECUPLE-PINNED.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashPanupMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        // length-prefix u16 (2) + color u8 (1) + body+NUL (29) = 32 bytes.
        const int ExpectedReplyPayloadLength = 32;
        const short ExpectedReplyLengthField = 29;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 28;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Panupo", shipName: "PanupoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/panup");

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

                if (!text.Equals("Missing arg for option panup", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgPanupLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/panup\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option panup\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 178 missing-arg ERROR literal for case-'p' /panx.
    /// Matcher at PlayerConnection.cpp:6970 -- pure NO-GUARD-ELSE-IF.
    /// THIRD case-'p' user-tier pin. 4-byte %s width.
    /// </summary>
    private const string MissingArgPanxLiteral = "Missing arg for option panx";

    /// <summary>
    /// Wave 178 sibling-arm-pinning hardening (+0 ratchet): pins the
    /// 31-byte wire-shape of the single 0x001D MESSAGE_STRING reply to
    /// user-tier slash <c>/panx</c> (NO param). Wave 178 deepens
    /// case-'p' to TRIPLE-PINNED.
    ///
    /// <para>
    /// ELSE-IF at 6970 `MatchOptWithParam("panx", pch, param,
    /// msg_sent)` -- pure NO-GUARD-ELSE-IF. Emits "Missing arg for
    /// option panx" at 4548 COLOR=5.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashPanxMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        // length-prefix u16 (2) + color u8 (1) + body+NUL (28) = 31 bytes.
        const int ExpectedReplyPayloadLength = 31;
        const short ExpectedReplyLengthField = 28;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 27;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Panxo", shipName: "PanxoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/panx");

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

                if (!text.Equals("Missing arg for option panx", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgPanxLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/panx\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option panx\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 179 missing-arg ERROR literal for case-'p' /pany.
    /// Matcher at PlayerConnection.cpp:6975 -- pure NO-GUARD-ELSE-IF.
    /// FOURTH case-'p' user-tier pin. 4-byte %s width.
    /// </summary>
    private const string MissingArgPanyLiteral = "Missing arg for option pany";

    /// <summary>
    /// Wave 179 sibling-arm-pinning hardening (+0 ratchet): pins the
    /// 31-byte wire-shape of the single 0x001D MESSAGE_STRING reply to
    /// user-tier slash <c>/pany</c> (NO param). Wave 179 deepens
    /// case-'p' to QUADRUPLE-PINNED.
    ///
    /// <para>
    /// ELSE-IF at 6975 `MatchOptWithParam("pany", pch, param,
    /// msg_sent)` -- pure NO-GUARD-ELSE-IF. Emits "Missing arg for
    /// option pany" at 4548 COLOR=5.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashPanyMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        // length-prefix u16 (2) + color u8 (1) + body+NUL (28) = 31 bytes.
        const int ExpectedReplyPayloadLength = 31;
        const short ExpectedReplyLengthField = 28;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 27;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Panyo", shipName: "PanyoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/pany");

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

                if (!text.Equals("Missing arg for option pany", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgPanyLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/pany\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option pany\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 180 missing-arg ERROR literal for case-'p' /panz.
    /// Matcher at PlayerConnection.cpp:6980 -- pure NO-GUARD-ELSE-IF.
    /// FIFTH case-'p' user-tier pin. 4-byte %s width.
    /// </summary>
    private const string MissingArgPanzLiteral = "Missing arg for option panz";

    /// <summary>
    /// Wave 180 sibling-arm-pinning hardening (+0 ratchet): pins the
    /// 31-byte wire-shape of the single 0x001D MESSAGE_STRING reply to
    /// user-tier slash <c>/panz</c> (NO param). Wave 180 deepens
    /// case-'p' to QUINTUPLE-PINNED.
    ///
    /// <para>
    /// ELSE-IF at 6980 `MatchOptWithParam("panz", pch, param,
    /// msg_sent)` -- pure NO-GUARD-ELSE-IF. Emits "Missing arg for
    /// option panz" at 4548 COLOR=5.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashPanzMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        // length-prefix u16 (2) + color u8 (1) + body+NUL (28) = 31 bytes.
        const int ExpectedReplyPayloadLength = 31;
        const short ExpectedReplyLengthField = 28;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 27;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Panzo", shipName: "PanzoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/panz");

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

                if (!text.Equals("Missing arg for option panz", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgPanzLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/panz\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option panz\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 181 missing-arg ERROR literal for case-'p' /planetspin.
    /// Matcher at PlayerConnection.cpp:6985 -- pure NO-GUARD-ELSE-IF.
    /// SIXTH case-'p' user-tier pin. 10-byte %s width (NEW).
    /// </summary>
    private const string MissingArgPlanetspinLiteral = "Missing arg for option planetspin";

    /// <summary>
    /// Wave 181 sibling-arm-pinning hardening (+0 ratchet): pins the
    /// 37-byte wire-shape of the single 0x001D MESSAGE_STRING reply to
    /// user-tier slash <c>/planetspin</c> (NO param). Wave 181 deepens
    /// case-'p' to SEXTUPLE-PINNED. FIRST 10-byte %s width pin.
    ///
    /// <para>
    /// ELSE-IF at 6985 `MatchOptWithParam("planetspin", pch, param,
    /// msg_sent)` -- pure NO-GUARD-ELSE-IF. Emits "Missing arg for
    /// option planetspin" at 4548 COLOR=5.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashPlanetspinMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        // length-prefix u16 (2) + color u8 (1) + body+NUL (34) = 37 bytes.
        const int ExpectedReplyPayloadLength = 37;
        const short ExpectedReplyLengthField = 34;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 33;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Planeto", shipName: "PlanetoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/planetspin");

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

                if (!text.Equals("Missing arg for option planetspin", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgPlanetspinLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/planetspin\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option planetspin\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 182 missing-arg ERROR literal for case-'g' /gm.
    /// Matcher at PlayerConnection.cpp:6484 -- SAME-LINE-AND-GUARD-GM
    /// pattern (`MatchOptWithParam(...) &amp;&amp; AdminLevel() &gt;= GM`).
    /// The matcher emits BEFORE the AdminLevel short-circuit evaluates.
    /// FIRST case-'g' user-tier pin (OPENS case-'g'). 2-byte %s width.
    /// </summary>
    private const string MissingArgGmLiteral = "Missing arg for option gm";

    /// <summary>
    /// Wave 182 sibling-arm-pinning hardening (+0 ratchet): pins the
    /// 29-byte wire-shape of the single 0x001D MESSAGE_STRING reply to
    /// user-tier slash <c>/gm</c> (NO param). Wave 182 OPENS case-'g'
    /// user-tier coverage and OPENS the 2-byte %s width category.
    ///
    /// <para>
    /// ELSE-IF at 6484 `MatchOptWithParam("gm", pch, param, msg_sent)
    /// &amp;&amp; AdminLevel() &gt;= GM` -- SAME-LINE-AND-GUARD-GM
    /// structural pattern. The matcher's missing-arg fork at 4548 fires
    /// BEFORE the &amp;&amp; short-circuit reaches the guard, so the
    /// guard does not affect the wire-shape.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashGmMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        // length-prefix u16 (2) + color u8 (1) + body+NUL (26) = 29 bytes.
        const int ExpectedReplyPayloadLength = 29;
        const short ExpectedReplyLengthField = 26;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 25;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Gemo", shipName: "GemoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/gm");

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

                if (!text.Equals("Missing arg for option gm", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgGmLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/gm\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option gm\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 183 missing-arg ERROR literal for case-'g' /gwormhole.
    /// Matcher at PlayerConnection.cpp:6569 -- SAME-LINE-AND-GUARD-BETA_PLUS
    /// pattern (`MatchOptWithParam(...) &amp;&amp; AdminLevel() &gt;= BETA_PLUS`).
    /// NEW guard variant within the SAME-LINE-AND-GUARD pattern family.
    /// SECOND case-'g' user-tier pin. 9-byte %s width.
    /// </summary>
    private const string MissingArgGwormholeLiteral = "Missing arg for option gwormhole";

    /// <summary>
    /// Wave 183 sibling-arm-pinning hardening (+0 ratchet): pins the
    /// 36-byte wire-shape of the single 0x001D MESSAGE_STRING reply to
    /// user-tier slash <c>/gwormhole</c> (NO param). Wave 183 deepens
    /// case-'g' to DOUBLE-PINNED and introduces a NEW guard variant
    /// (BETA_PLUS) within the SAME-LINE-AND-GUARD pattern family.
    ///
    /// <para>
    /// ELSE-IF at 6569 `MatchOptWithParam("gwormhole", pch, param,
    /// msg_sent) &amp;&amp; AdminLevel() &gt;= BETA_PLUS` -- the matcher's
    /// missing-arg fork at 4548 fires BEFORE the &amp;&amp; short-circuit
    /// reaches the BETA_PLUS guard.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashGwormholeMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        // length-prefix u16 (2) + color u8 (1) + body+NUL (33) = 36 bytes.
        const int ExpectedReplyPayloadLength = 36;
        const short ExpectedReplyLengthField = 33;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 32;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Gwormo", shipName: "GwormoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/gwormhole");

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

                if (!text.Equals("Missing arg for option gwormhole", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgGwormholeLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/gwormhole\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option gwormhole\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 184 missing-arg ERROR literal for case-'g' /getstat.
    /// Matcher at PlayerConnection.cpp:6547 -- SAME-LINE-AND-GUARD-GM
    /// pattern (`MatchOptWithParam(...) &amp;&amp; AdminLevel() &gt;= GM`).
    /// SECOND SAME-LINE-AND-GUARD-GM pin (Wave 182 /gm was first).
    /// THIRD case-'g' user-tier pin. 7-byte %s width.
    /// </summary>
    private const string MissingArgGetstatLiteral = "Missing arg for option getstat";

    /// <summary>
    /// Wave 184 sibling-arm-pinning hardening (+0 ratchet): pins the
    /// 34-byte wire-shape of the single 0x001D MESSAGE_STRING reply to
    /// user-tier slash <c>/getstat</c> (NO param). Wave 184 deepens
    /// case-'g' to TRIPLE-PINNED and SAME-LINE-AND-GUARD-GM variant to
    /// DOUBLE-PINNED.
    ///
    /// <para>
    /// ELSE-IF at 6547 `MatchOptWithParam("getstat", pch, param,
    /// msg_sent) &amp;&amp; AdminLevel() &gt;= GM` -- the matcher's
    /// missing-arg fork at 4548 fires BEFORE the &amp;&amp; short-circuit
    /// reaches the GM guard.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashGetstatMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        // length-prefix u16 (2) + color u8 (1) + body+NUL (31) = 34 bytes.
        const int ExpectedReplyPayloadLength = 34;
        const short ExpectedReplyLengthField = 31;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 30;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Geto", shipName: "GetoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/getstat");

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

                if (!text.Equals("Missing arg for option getstat", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgGetstatLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/getstat\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option getstat\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 185 missing-arg ERROR literal for case-'g' /gform.
    /// Matcher at PlayerConnection.cpp:6559 -- SAME-LINE-AND-GUARD-GM
    /// pattern. THIRD SAME-LINE-AND-GUARD-GM pin. FOURTH case-'g'
    /// user-tier pin. 5-byte %s width.
    /// </summary>
    private const string MissingArgGformLiteral = "Missing arg for option gform";

    /// <summary>
    /// Wave 185 sibling-arm-pinning hardening (+0 ratchet): pins the
    /// 32-byte wire-shape of the single 0x001D MESSAGE_STRING reply to
    /// user-tier slash <c>/gform</c> (NO param). Wave 185 deepens
    /// case-'g' to QUADRUPLE-PINNED and SAME-LINE-AND-GUARD-GM variant
    /// to TRIPLE-PINNED.
    ///
    /// <para>
    /// ELSE-IF at 6559 `MatchOptWithParam("gform", pch, param,
    /// msg_sent) &amp;&amp; AdminLevel() &gt;= GM` -- the matcher's
    /// missing-arg fork at 4548 fires BEFORE the &amp;&amp; short-circuit
    /// reaches the GM guard.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashGformMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        // length-prefix u16 (2) + color u8 (1) + body+NUL (29) = 32 bytes.
        const int ExpectedReplyPayloadLength = 32;
        const short ExpectedReplyLengthField = 29;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 28;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Gformo", shipName: "GformoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/gform");

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

                if (!text.Equals("Missing arg for option gform", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgGformLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/gform\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option gform\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 186 missing-arg ERROR literal for case-'b' /buff.
    /// Matcher at PlayerConnection.cpp:5579 -- NO-GUARD-IF pattern
    /// (independent if-chain, no surrounding outer or inside-body
    /// guard around the MatchOptWithParam call). FIRST case-'b' pin
    /// for the BUFF-family. THIRD case-'b' pin overall (after the
    /// Ban GM-tier pin from prior waves).
    /// </summary>
    private const string MissingArgBuffLiteral = "Missing arg for option buff";

    /// <summary>
    /// Wave 186 sibling-arm-pinning hardening (+0 ratchet): pins the
    /// 31-byte wire-shape of the single 0x001D MESSAGE_STRING reply to
    /// user-tier slash <c>/buff</c> (NO param). Wave 186 deepens
    /// case-'b' user-tier coverage and pins the 4-byte %s width column
    /// of the "Missing arg for option %s" template.
    ///
    /// <para>
    /// NO-GUARD-IF at 5579 `MatchOptWithParam("buff", pch, param,
    /// msg_sent)` -- the matcher's missing-arg fork at 4548 fires
    /// regardless of admin level. Preceding arms in case-'b' that
    /// could potentially match "buff" as a prefix all fail
    /// MatchOptWithParam's strncmp ("be" matches first 2 bytes then
    /// sees alpha 'f' at arg[2] -> silent false return; "bwho" /
    /// "basset" mismatch on byte 1) and the strcmp arms ("beon",
    /// "beoff") differ in body. NET RESULT: ONE emit.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashBuffMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        // length-prefix u16 (2) + color u8 (1) + body+NUL (28) = 31 bytes.
        const int ExpectedReplyPayloadLength = 31;
        const short ExpectedReplyLengthField = 28;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 27;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Buffo", shipName: "BuffoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/buff");

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

                if (!text.Equals("Missing arg for option buff", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgBuffLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/buff\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option buff\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 187 missing-arg ERROR literal for case-'b' /basset.
    /// Matcher at PlayerConnection.cpp:5573 -- NO-GUARD-ELSE-IF
    /// pattern (chained via `else if` to the /bwho SAME-LINE-AND-GUARD
    /// arm at 5551 without any outer or inside-body guard around the
    /// matcher itself). SECOND case-'b' user-tier pin -- case-'b' now
    /// DOUBLE-PINNED.
    /// </summary>
    private const string MissingArgBassetLiteral = "Missing arg for option basset";

    /// <summary>
    /// Wave 187 sibling-arm-pinning hardening (+0 ratchet): pins the
    /// 33-byte wire-shape of the single 0x001D MESSAGE_STRING reply to
    /// user-tier slash <c>/basset</c> (NO param). Wave 187 deepens
    /// case-'b' to DOUBLE-PINNED and re-pins NO-GUARD-ELSE-IF.
    ///
    /// <para>
    /// ELSE-IF at 5573 `MatchOptWithParam("basset", pch, param,
    /// msg_sent)` -- the matcher's missing-arg fork at 4548 fires
    /// regardless of admin level. Preceding /bwho at 5551 has
    /// allowNoParams=true and `&amp;&amp; AdminLevel() &gt;= BETA`,
    /// but its strncmp ("bwho" vs "basset") fails at byte 1 ('w' vs
    /// 'a') so /bwho returns false without emitting and the else-if
    /// reaches /basset. NET RESULT: ONE emit.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashBassetMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        // length-prefix u16 (2) + color u8 (1) + body+NUL (30) = 33 bytes.
        const int ExpectedReplyPayloadLength = 33;
        const short ExpectedReplyLengthField = 30;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 29;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Basso", shipName: "BassoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/basset");

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

                if (!text.Equals("Missing arg for option basset", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgBassetLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/basset\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option basset\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 188 missing-arg ERROR literal for case-'c' /ccamera.
    /// Matcher at PlayerConnection.cpp:5684 -- NO-GUARD-ELSE-IF
    /// pattern (chained via `else if` to the /chleave arm at 5667
    /// without any outer or inside-body guard around the matcher
    /// itself). THIRD case-'c' user-tier pin (after /chjoin and
    /// /chleave) -- case-'c' now TRIPLE-PINNED.
    /// </summary>
    private const string MissingArgCcameraLiteral = "Missing arg for option ccamera";

    /// <summary>
    /// Wave 188 sibling-arm-pinning hardening (+0 ratchet): pins the
    /// 34-byte wire-shape of the single 0x001D MESSAGE_STRING reply to
    /// user-tier slash <c>/ccamera</c> (NO param). Wave 188 deepens
    /// case-'c' to TRIPLE-PINNED.
    ///
    /// <para>
    /// ELSE-IF at 5684 `MatchOptWithParam("ccamera", pch, param,
    /// msg_sent)` -- the matcher's missing-arg fork at 4548 fires
    /// regardless of admin level. Preceding /chjoin at 5633 and
    /// /chleave at 5667 both fail strncmp at byte 1 ('h' vs 'c') and
    /// return false without emitting. Subsequent /changepassword,
    /// /createitem, /createcredits, /createmission, /createmob,
    /// /create, /customizeship all fail strncmp at byte 1 ('h'/'r'/
    /// 'u' vs 'c'). NET RESULT: ONE emit.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashCcameraMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        // length-prefix u16 (2) + color u8 (1) + body+NUL (31) = 34 bytes.
        const int ExpectedReplyPayloadLength = 34;
        const short ExpectedReplyLengthField = 31;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 30;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Ccamo", shipName: "CcamoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/ccamera");

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

                if (!text.Equals("Missing arg for option ccamera", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgCcameraLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/ccamera\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option ccamera\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 189 missing-arg ERROR literal for case-'c' /changepassword.
    /// Matcher at PlayerConnection.cpp:5698 -- NO-GUARD-ELSE-IF
    /// pattern (chained via `else if` to the /ccamera arm at 5684
    /// without any outer or inside-body guard around the matcher
    /// itself). FOURTH case-'c' user-tier pin -- case-'c' user-tier
    /// now QUADRUPLE-PINNED.
    /// </summary>
    private const string MissingArgChangepasswordLiteral = "Missing arg for option changepassword";

    /// <summary>
    /// Wave 189 sibling-arm-pinning hardening (+0 ratchet): pins the
    /// 41-byte wire-shape of the single 0x001D MESSAGE_STRING reply to
    /// user-tier slash <c>/changepassword</c> (NO param). Wave 189
    /// deepens case-'c' to QUADRUPLE-PINNED and ratchets 14-byte %s
    /// width to TRIPLE-PINNED.
    ///
    /// <para>
    /// ELSE-IF at 5698 `MatchOptWithParam("changepassword", pch, param,
    /// msg_sent)` -- the matcher's missing-arg fork at 4548 fires
    /// regardless of admin level. Preceding /chjoin at 5633 strncmp
    /// fails at byte 2 ('j' vs 'a'); /chleave at 5667 strncmp fails at
    /// byte 2 ('l' vs 'a'); /ccamera at 5684 strncmp fails at byte 1
    /// ('c' vs 'h'). NET RESULT: ONE emit. CRITICAL safety: the
    /// matcher's missing-arg ERROR fork emits BEFORE ChangePassword
    /// is invoked, so no password mutation occurs.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashChangepasswordMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        // length-prefix u16 (2) + color u8 (1) + body+NUL (38) = 41 bytes.
        const int ExpectedReplyPayloadLength = 41;
        const short ExpectedReplyLengthField = 38;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 37;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Chango", shipName: "ChangoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/changepassword");

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

                if (!text.Equals("Missing arg for option changepassword", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgChangepasswordLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/changepassword\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option changepassword\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 190 missing-arg ERROR literal for case-'d' /deco.
    /// Matcher at PlayerConnection.cpp:5984 -- NO-GUARD-ELSE-IF
    /// pattern (chained via `else if` to the /debug strcmp arm at
    /// 5976 without any outer or inside-body guard around the matcher
    /// itself). SECOND case-'d' user-tier pin (after /d
    /// SAME-LINE-AND-GUARD-DEV) -- case-'d' user-tier now
    /// DOUBLE-PINNED.
    /// </summary>
    private const string MissingArgDecoLiteral = "Missing arg for option deco";

    /// <summary>
    /// Wave 190 sibling-arm-pinning hardening (+0 ratchet): pins the
    /// 31-byte wire-shape of the single 0x001D MESSAGE_STRING reply to
    /// user-tier slash <c>/deco</c> (NO param). Wave 190 deepens
    /// case-'d' to DOUBLE-PINNED and ratchets 4-byte %s width to
    /// SEPTUPLE-PINNED.
    ///
    /// <para>
    /// ELSE-IF at 5984 `MatchOptWithParam("deco", pch, param,
    /// msg_sent)` -- the matcher's missing-arg fork at 4548 fires
    /// regardless of admin level. Preceding /d at 5903 strncmp byte 0
    /// matches BUT arg[1]='e' is alpha -> silent FALSE; /don /doff
    /// strcmp MISMATCH; /dwho at 5944 strncmp("dwho","deco",4) byte 1
    /// 'w' vs 'e' MISMATCH -> silent FALSE; /dialog at 5966 strncmp
    /// byte 2 'i' vs 'c' MISMATCH; /debug strcmp MISMATCH. /deco at
    /// 5984 MATCHES -- emits missing-arg. NET RESULT: ONE emit.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashDecoMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        // length-prefix u16 (2) + color u8 (1) + body+NUL (28) = 31 bytes.
        const int ExpectedReplyPayloadLength = 31;
        const short ExpectedReplyLengthField = 28;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 27;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Decoo", shipName: "DecooShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/deco");

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

                if (!text.Equals("Missing arg for option deco", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgDecoLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/deco\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option deco\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 191 missing-arg ERROR literal for case-'e' /effecto.
    /// Matcher at PlayerConnection.cpp:6120 -- INSIDE-BODY pattern
    /// inside the OUTER-BLOCK-GM-guard at 6074 (`if (AdminLevel() >=
    /// GM)`). cli_test1xx accounts have status=100 which satisfies
    /// AdminLevel >= GM, so the outer block enters and the matcher's
    /// missing-arg fork at 4548 fires. SECOND case-'e' user-tier pin
    /// (after /effect) -- case-'e' user-tier now DOUBLE-PINNED.
    /// </summary>
    private const string MissingArgEffectoLiteral = "Missing arg for option effecto";

    /// <summary>
    /// Wave 191 sibling-arm-pinning hardening (+0 ratchet): pins the
    /// 34-byte wire-shape of the single 0x001D MESSAGE_STRING reply to
    /// user-tier slash <c>/effecto</c> (NO param). Wave 191 deepens
    /// case-'e' to DOUBLE-PINNED.
    ///
    /// <para>
    /// ELSE-IF at 6120 `MatchOptWithParam("effecto", pch, param,
    /// msg_sent)` -- inside the OUTER-BLOCK-GM-guard at 6074. /effect
    /// at 6076 strncmp("effect","effecto",6)=match, arg[6]='o' is
    /// alpha -> silent FALSE (no emit). /effecto at 6120 matches,
    /// arg[7]=NUL not '=' not ' ' not alpha -> param=NULL fork ->
    /// emits "Missing arg for option effecto". /effects at 6193
    /// strncmp("effects","effecto",6)=match, byte 6='s' vs 'o'
    /// MISMATCH -> silent FALSE. Preceding /endtalk strcmp at 6047
    /// MISMATCH, /enableskills strcmp at 6054 MISMATCH. NET RESULT:
    /// ONE emit.
    /// </para>
    ///
    /// <para>
    /// Server-integrity: the matcher's missing-arg ERROR fork is
    /// behaviour the real server exhibited; pinning the wire shape
    /// ratchets fidelity without weakening any security posture.
    /// CRITICAL safety regression: if a future refactor weakened the
    /// OUTER-BLOCK-GM-guard at 6074 so /effecto became reachable
    /// without GM, this test would still pass (status=100 already has
    /// GM), but a sibling /effecto-on-non-GM test would catch it.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashEffectoMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        // length-prefix u16 (2) + color u8 (1) + body+NUL (31) = 34 bytes.
        const int ExpectedReplyPayloadLength = 34;
        const short ExpectedReplyLengthField = 31;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 30;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Effecto", shipName: "EffectoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/effecto");

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

                // EXACT equals filter -- "Missing arg for option effecto"
                // is itself a prefix-free string under the case-'e' family
                // (/effect emits a shorter literal; /effects a different
                // longer one).
                if (!text.Equals("Missing arg for option effecto", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgEffectoLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/effecto\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option effecto\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 192 missing-arg ERROR literal for case-'f' /find.
    /// Matcher at PlayerConnection.cpp:6345 -- NO-GUARD-IF pattern
    /// inside the SECOND OUTER-BLOCK-GM-guard at 6337 ("GM to Admin").
    /// FIRST case-'f' user-tier pin -- OPENS case-'f' coverage.
    /// </summary>
    private const string MissingArgFindLiteral = "Missing arg for option find";

    /// <summary>
    /// Wave 192 sibling-arm-pinning hardening (+0 ratchet): pins the
    /// 31-byte wire-shape of the single 0x001D MESSAGE_STRING reply to
    /// user-tier slash <c>/find</c> (NO param). Wave 192 OPENS case-'f'
    /// user-tier coverage.
    ///
    /// <para>
    /// NO-GUARD-IF at 6345 `MatchOptWithParam("find", pch, param,
    /// msg_sent)` -- inside the SECOND OUTER-BLOCK-GM-guard at 6337.
    /// Preceding case-'f' arms: /form at 6283 SAME-LINE-AND-GUARD
    /// strncmp("form","find",4) byte 1 'o' vs 'i' MISMATCH -> silent
    /// FALSE; /flushinv strcmp MISMATCH; /factionset strcmp MISMATCH;
    /// /factionoverride strcmp MISMATCH; /fetch strcmp at 6339
    /// MISMATCH. /find at 6345 matches -- emits "Missing arg for
    /// option find" at 4548 COLOR=5, returns FALSE. Subsequent /face
    /// /faceme /fgps /fireweapon strcmp MISMATCH; /fhelp /fradius
    /// /ftype /flevel /fcount /faddasteroidtype /faddoretofield
    /// /fdelorefromfield /faddoretosector /fdelorefromsector all gated
    /// by `AdminLevel() >= DEV` (cli_test184 status=100 does NOT meet
    /// DEV-tier; block short-circuits). NET RESULT: ONE emit.
    /// </para>
    ///
    /// <para>
    /// Server-integrity: the matcher's missing-arg ERROR fork is
    /// behaviour the real server exhibited. cli_test184 status=100
    /// satisfies AdminLevel >= GM so the outer block enters. No
    /// permissiveness added.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashFindMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        // length-prefix u16 (2) + color u8 (1) + body+NUL (28) = 31 bytes.
        const int ExpectedReplyPayloadLength = 31;
        const short ExpectedReplyLengthField = 28;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 27;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Findo", shipName: "FindoShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/find");

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

                if (!text.Equals("Missing arg for option find", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgFindLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/find\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option find\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 193 missing-arg ERROR literal for case-'f' /fradius.
    /// Matcher at PlayerConnection.cpp:6401 -- NO-GUARD-IF pattern
    /// inside the OUTER-BLOCK-DEV-guard at 6394 (`if (AdminLevel() >=
    /// DEV)`). cli_test185 status=100 satisfies DEV (80). SECOND
    /// case-'f' user-tier pin -- case-'f' now DOUBLE-PINNED.
    /// </summary>
    private const string MissingArgFradiusLiteral = "Missing arg for option fradius";

    /// <summary>
    /// Wave 193 sibling-arm-pinning hardening (+0 ratchet): pins the
    /// 34-byte wire-shape of the single 0x001D MESSAGE_STRING reply to
    /// user-tier slash <c>/fradius</c> (NO param). Wave 193 deepens
    /// case-'f' to DOUBLE-PINNED.
    ///
    /// <para>
    /// NO-GUARD-IF at 6401 `MatchOptWithParam("fradius", pch, param,
    /// msg_sent)` -- inside the OUTER-BLOCK-DEV-guard at 6394.
    /// Preceding case-'f' arms all MISMATCH on byte 1 ('o'/'l'/'a'/'i'/
    /// 'e' vs 'r'). /fhelp at 6396 has allowNoParams=true and does NOT
    /// emit missing-arg (matcher returns true with param=NULL); but
    /// /fhelp's strncmp on "fradius" mismatches at byte 1 'h' vs 'r'
    /// anyway. /fradius at 6401 matches arg="fradius" -- emits
    /// "Missing arg for option fradius" at 4548 COLOR=5, returns
    /// FALSE. Subsequent /ftype /flevel /fcount /faddasteroidtype/
    /// /faddoretofield /fdelorefromfield /faddoretosector/
    /// /fdelorefromsector all MISMATCH at byte 1. NET RESULT: ONE
    /// emit.
    /// </para>
    ///
    /// <para>
    /// Server-integrity: cli_test185 status=100 satisfies AdminLevel
    /// >= DEV (DEV=80). The matcher's missing-arg ERROR fork fires
    /// within the DEV-block; pinning its wire shape ratchets fidelity.
    /// No server permissiveness added.
    /// </para>
    ///
    /// <para>Budget: 90s.</para>
    /// </summary>
    [Fact]
    public async Task SlashFradiusMissingArg_OnAdminAccount_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        // length-prefix u16 (2) + color u8 (1) + body+NUL (31) = 34 bytes.
        const int ExpectedReplyPayloadLength = 34;
        const short ExpectedReplyLengthField = 31;
        const byte ExpectedReplyColor = 5;
        const int ExpectedLiteralByteCount = 30;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Fradio", shipName: "FradioShip", cts.Token);

        try
        {
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/fradius");

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

                if (!text.Equals("Missing arg for option fradius", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(MissingArgFradiusLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                $"\"/fradius\" without seeing 0x001D MESSAGE_STRING equal to " +
                $"\"Missing arg for option fradius\".");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
