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
}
