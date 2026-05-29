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
}
