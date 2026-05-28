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
/// Wave 24 direct-reply round-trip: client sends an 11-byte 0x0029
/// ITEM_STATE with the <c>Inventory</c> byte set to a value other than
/// the gate-value 2, expects the server's UNRECOGNISED-ITEM-STATE
/// error string back as a 0x001D MESSAGE_STRING.
///
/// <para>
/// Wire layout (mirror of <c>common/include/net7/PacketStructures.h:263</c>):
/// <code>
///   [0..4)  int32  GameID
///   [4..8)  int32  BitMask
///   [8]     char   Enable
///   [9]     char   Inventory
///   [10]    char   ItemNum
/// </code>
/// 11 bytes total; struct is <c>ATTRIB_PACKED</c> so there's no
/// implicit padding.
/// </para>
///
/// <para>
/// Server handler. <c>Player::HandleItemState</c>
/// (<c>server/src/PlayerConnection.cpp:3359</c>) reinterprets the
/// payload as an <c>ItemState *</c> and branches on
/// <c>Data-&gt;Inventory</c>:
/// <code>
///     if (Data-&gt;Inventory == 2)
///     {
///         // mutation branch: m_Mutex + EquipItem[ItemNum].SetItemState(...)
///         // + SendAuxShip() fan-out, no direct reply
///     }
///     else
///     {
///         LogMessage("UNRECOGNISED ITEM STATE:\n");
///         DumpBuffer(data, sizeof(ItemState));
///         SendVaMessage("UNRECOGNISED ITEM STATE!\nPlease submit a bug report\n");
///     }
/// </code>
/// The else-branch is the clean direct-reply path: a single 0x001D
/// MESSAGE_STRING with a literal, deterministic body.
/// </para>
///
/// <para>
/// Why <c>Inventory=0</c> rather than e.g. 1 or 3. 2 is the only
/// accepted value (it indexes <c>EquipInv.EquipItem[]</c>); any other
/// byte value drives the else-branch. 0 is the smallest deviation
/// from the gate-value and the most likely retail garbage / probe
/// pattern (a freshly-zeroed packet buffer with the opcode written
/// in). Pinning to 0 also gives the test a stable byte pattern so
/// the LogMessage + DumpBuffer side-effects on the server are
/// reproducible run-to-run.
/// </para>
///
/// <para>
/// Why <c>ItemNum=0</c> on the else-branch. ItemNum is only
/// dereferenced on the Inventory==2 mutation branch
/// (<c>EquipItem[Data-&gt;ItemNum]</c>); the else-branch ignores it.
/// 0 keeps the payload deterministic without risking the mutation
/// branch reading past the EquipInv array.
/// </para>
///
/// <para>
/// Direct-reply assertion vs. survival probe. Unlike Wave 23
/// (<see cref="SectorDebugTests"/>) where HandleDebug is a true
/// no-op and a survival probe is the only assertable post-condition,
/// HandleItemState's else-branch emits a mandatory 0x001D
/// MESSAGE_STRING with a literal body — so we can directly correlate
/// the reply to the request. This is the same pattern Wave 19/20
/// (<see cref="SectorRequestTargetTests"/> /
/// <see cref="SectorRequestTargetsTargetTests"/>) used for SetTarget
/// and Wave 8 (<see cref="SectorChatTests"/>) used for the
/// "not in a group" path.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>ItemState struct long-revert in PacketStructures.h:263.</b>
///     Currently 2× int32_t + 3× char = 11B canonical. If anyone
///     widens GameID or BitMask to <c>long</c> the struct grows on
///     Linux x86_64 (sizeof(long)==8) and the Inventory / ItemNum
///     bytes read from beyond the 11B wire payload. The most
///     interesting failure mode: the over-read could land Inventory
///     on a 0x02 byte in receive-buffer slack and accidentally enter
///     the mutation branch — then EquipItem[garbage_ItemNum] would
///     dereference well outside the EquipInv array.
///   </item>
///   <item>
///     <b>Dispatcher mis-route at server/src/PlayerConnection.cpp:463.</b>
///     Case label sits between 0x0027 INVENTORY_SORT and 0x002C
///     ACTION in the hand-maintained ~200-entry switch. A
///     copy-paste swap with HandleInventorySort would re-interpret
///     the 11B ItemState as a larger InventorySort struct (16B+) —
///     reading past the wire payload and producing garbage sort
///     parameters rather than the expected MESSAGE_STRING reply.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode regression.</b>
///     0x0029 is NOT explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>, so it hits the
///     <c>default:</c> arm and falls through to the bottom-of-switch
///     forward. A regression dropping this opcode would surface as a
///     timeout waiting for the MESSAGE_STRING reply.
///   </item>
///   <item>
///     <b>SendVaMessage / SendMessageString format-string regression.</b>
///     A refactor that escapes <c>\n</c> or strips the literal
///     "UNRECOGNISED ITEM STATE" would break the substring assert.
///     SendVaMessage routes through vsprintf_s then SendMessageString
///     (server/src/PlayerClass.cpp:3415) — the [u16 len][u8 colour][string\0]
///     framing is shared with Wave 8's chat error path.
///   </item>
///   <item>
///     <b>Server→client 0x001D fan-out path regression.</b> The
///     reply rides SendOpcode → m_UDPQueue → SendPacketCache → 0x2016
///     PACKET_SEQUENCE wrapper → proxy SendClientPacketSequence →
///     TCP. Every Phase K survival probe exercises the same path
///     indirectly; this test exercises it as the primary assertion.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x0029 ITEM_STATE is what
/// the retail Win32 client emits when the user toggles ship-equipment
/// state (e.g. enabling a buff item). The retail server's
/// HandleItemState behaves identically — Inventory==2 mutation, any
/// other value triggers the verbatim UNRECOGNISED-error reply. We
/// are not making the server accept any new input shape, not
/// fabricating any reply; we drive the existing else-branch with the
/// minimum non-2 Inventory byte value.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; ITEM_STATE+REPLY round-trip is
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorItemStateTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorItemStateTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ItemState_UnrecognisedInventoryByte_ReceivesUnrecognisedErrorString()
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
            firstName: "Itemy", shipName: "ItemShip", cts.Token);

        try
        {
            // 0x0029 ITEM_STATE — 11B canonical payload with
            // Inventory=0 (anything != 2 trips the else-branch).
            //   [0..4)  int32 GameID    = 0
            //   [4..8)  int32 BitMask   = 0
            //   [8]     byte  Enable    = 0
            //   [9]     byte  Inventory = 0   (NOT 2 → else-branch)
            //   [10]    byte  ItemNum   = 0
            byte[] payload = new byte[11];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);
            payload[8] = 0;
            payload[9] = 0;
            payload[10] = 0;

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.ItemState.Value, payload),
                cts.Token);

            // Drain inbound until we see a 0x001D MESSAGE_STRING whose
            // body contains the literal "UNRECOGNISED ITEM STATE".
            // Post-handshake the server may interleave other in-sector
            // fan-out (NPC chatter, state updates, etc.); a frame cap
            // keeps a stalled pipeline from masquerading as the outer-
            // CTS timeout.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.MessageString.Value)
                    continue;

                // 0x001D wire layout (mirror of Player::SendMessageString
                // at server/src/PlayerConnection.cpp:10918):
                //   [0..2)  short  length  = strlen(msg) + 1   (includes NUL)
                //   [2]     byte   color   (default 5 for SendVaMessage)
                //   [3..N)  char[] msg + NUL terminator
                var span = reply.Payload.Span;
                Assert.True(span.Length >= 4,
                    $"MESSAGE_STRING payload too short: {span.Length}B");

                short msgLen = BinaryPrimitives.ReadInt16LittleEndian(span[..2]);
                Assert.True(msgLen >= 1,
                    $"MESSAGE_STRING length field={msgLen}, expected >= 1 (NUL).");

                int bodyBytes = Math.Min(msgLen - 1, span.Length - 3);
                if (bodyBytes <= 0) continue;

                string text = Encoding.ASCII.GetString(span.Slice(3, bodyBytes));

                // Filter — other MESSAGE_STRING frames may arrive
                // first (NPC chatter, motd, etc.). Keep draining until
                // we see the one keyed by our ITEM_STATE.
                if (!text.Contains("UNRECOGNISED ITEM STATE", StringComparison.Ordinal))
                    continue;

                // Pin on the distinctive substring rather than the
                // whole string so punctuation / newline tweaks don't
                // sink the test. The full literal at PlayerConnection.cpp:3386
                // is "UNRECOGNISED ITEM STATE!\nPlease submit a bug report\n".
                Assert.Contains("UNRECOGNISED ITEM STATE", text);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0029 ITEM_STATE (Inventory=0) " +
                $"without seeing 0x001D MESSAGE_STRING containing \"UNRECOGNISED ITEM STATE\". " +
                $"Likely the server's HandleItemState else-branch SendVaMessage path broke, " +
                $"the proxy default-case forwarding dropped the opcode, " +
                $"the dispatcher case at PlayerConnection.cpp:463 got mis-routed, " +
                $"or ItemState struct was widened past 11B and Inventory==0 landed elsewhere.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// The verbatim ASCII body the retail-faithful HandleItemState
    /// else-branch passes to <c>SendVaMessage</c> at
    /// <c>server/src/PlayerConnection.cpp:3386</c>. 52 bytes of payload
    /// content; <c>SendMessageString</c> appends a NUL terminator and
    /// emits <c>length = 53</c>.
    /// </summary>
    private const string UnrecognisedItemStateLiteral =
        "UNRECOGNISED ITEM STATE!\nPlease submit a bug report\n";

    /// <summary>
    /// Wave 108 frame-shape hardening (+0 ratchet, 0x001D): pins the
    /// byte-exact 56-byte wire-shape of the single 0x001D MESSAGE_STRING
    /// the server emits in reply to a 0x0029 ITEM_STATE whose
    /// <c>Inventory</c> byte is anything other than the gate-value 2.
    /// Wave 24's existing test
    /// (<see cref="ItemState_UnrecognisedInventoryByte_ReceivesUnrecognisedErrorString"/>)
    /// asserts only that the response body <em>contains</em> the
    /// distinctive substring "UNRECOGNISED ITEM STATE"; the bounds
    /// checks (<c>Assert.True(span.Length &gt;= 4)</c>,
    /// <c>Assert.True(msgLen &gt;= 1)</c>) are deliberately loose so a
    /// punctuation tweak doesn't sink it. Wave 108 layers byte-exact
    /// pinning on top, locking the full 56-byte response shape in place.
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
    /// (PlayerClass.h:277). For the verbatim 52-byte literal
    /// "UNRECOGNISED ITEM STATE!\\nPlease submit a bug report\\n" at
    /// <c>PlayerConnection.cpp:3386</c>:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>length field</b> = strlen(52) + 1 = <c>53</c></item>
    ///   <item><b>color byte</b> = <c>5</c> (default)</item>
    ///   <item><b>msg + NUL</b> = 52 + 1 = <c>53 bytes</c></item>
    ///   <item><b>total payload</b> = <c>length + 3 = 56 bytes</c></item>
    /// </list>
    ///
    /// <para>
    /// Why a separate test method. Mirrors the Wave 92 (RELATIONSHIP
    /// count) / Wave 101 (CLIENT_AVATAR+SHIP paired) split: Wave 24's
    /// looser substring assertion stays intact (narrow-scope failure
    /// surface — a wire-shape drift that still produces the literal
    /// substring would not surface as a Wave 24 failure), Wave 108 adds
    /// the byte-exact pin as its own discrete test artifact for the
    /// regression-class catalogue. Mirrors the Wave 104 narrative's
    /// "upgrade existing survival-probe assertions to byte-exact"
    /// pivot.
    /// </para>
    ///
    /// <para>
    /// Regression classes Wave 108 catches beyond what Wave 24 catches.
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b><c>SendMessageString</c> length-field width regression at
    ///     <c>PlayerConnection.cpp:10992</c>.</b> The cast
    ///     <c>*((short *) &amp;buffer[0]) = length</c> writes a 2-byte
    ///     length prefix. A regression to <c>int32_t</c> (the canonical
    ///     length type used elsewhere in the protocol) would shift the
    ///     color byte from offset 2 to offset 4 and grow the total
    ///     payload from 56 to 58 bytes. Wave 24's
    ///     <c>Assert.True(span.Length &gt;= 4)</c> would still pass at
    ///     58B; <c>Assert.Equal(56, span.Length)</c> catches.
    ///   </item>
    ///   <item>
    ///     <b><c>SendMessageString</c> color-default regression at
    ///     <c>PlayerClass.h:277</c>.</b> The signature is
    ///     <c>SendMessageString(char *msg, char color=5, bool log=true)</c>.
    ///     A regression to a different default (or a refactor where
    ///     SendVaMessage passes an explicit non-5 color) would change
    ///     wire byte 2 without changing the substring. Wave 24's text
    ///     assertion is structurally blind; Wave 108 pins
    ///     <c>span[2] == 5</c>.
    ///   </item>
    ///   <item>
    ///     <b>Length-field LE byte-order regression.</b> The cast
    ///     <c>*((short *) &amp;buffer[0])</c> writes a host-order short.
    ///     The retail Win32 client (x86 LE) reads it as LE. A
    ///     hypothetical big-endian server build would swap the byte
    ///     pair and break decoding; Wave 24's substring assertion would
    ///     still pass (the body bytes are after the length prefix and
    ///     unaffected). Wave 108's
    ///     <c>BinaryPrimitives.ReadInt16LittleEndian == 53</c> catches.
    ///   </item>
    ///   <item>
    ///     <b><c>SendOpcode</c> trailing-bytes regression at
    ///     <c>PlayerConnection.cpp:10996</c>.</b> The third argument
    ///     <c>length + 3</c> is what bounds the emit to 56 bytes; a
    ///     regression to <c>sizeof(buffer)</c> (512) would emit 456
    ///     trailing zero bytes after the legitimate payload. Wave 24's
    ///     substring assertion would still pass; Wave 108's
    ///     <c>Assert.Equal(56, span.Length)</c> catches.
    ///   </item>
    ///   <item>
    ///     <b>Verbatim-literal drift at
    ///     <c>PlayerConnection.cpp:3386</c>.</b> A refactor that
    ///     replaces "<c>\\n</c>" with "<c>. </c>" (a "polish-the-error"
    ///     edit) or trims the trailing newline would silently change
    ///     the wire bytes the retail Win32 client's decoder was
    ///     compiled to accept (newlines are the line-break marker the
    ///     in-game chat-log overlay uses). Wave 24's <c>Contains</c>
    ///     would still pass; Wave 108's full-literal
    ///     <c>Assert.Equal</c> on the body bytes catches.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Per CLAUDE.md server-integrity. 0x001D MESSAGE_STRING is
    /// server-originated. Wave 108 adds no client stimulus beyond the
    /// same 11-byte ITEM_STATE Wave 24 already sends, and no server
    /// change — pure passive-observation tightening of a retail-faithful
    /// wire shape. The 56-byte response is exactly what the retail
    /// Win32 client's MESSAGE_STRING decoder was compiled to receive.
    /// No widened input acceptance, no loosened gating, no fabricated
    /// replies — server-integrity POSITIVE.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s. Handshake ~2s; ITEM_STATE+REPLY round-trip is
    /// sub-second.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ItemState_UnrecognisedInventoryByte_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (53) = 56 bytes.
        const int ExpectedReplyPayloadLength = 56;
        // strlen(literal) + 1 NUL = 53.
        const short ExpectedReplyLengthField = 53;
        // SendVaMessage → SendMessageString default color parameter.
        const byte ExpectedReplyColor = 5;
        // strlen(literal) = 52.
        const int ExpectedLiteralByteCount = 52;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Itm108e", shipName: "Itm108Ship", cts.Token);

        try
        {
            byte[] payload = new byte[11];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);
            payload[8] = 0;
            payload[9] = 0;
            payload[10] = 0;

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.ItemState.Value, payload),
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
                // race ahead of the UNRECOGNISED reply. Once we have
                // the right reply we pin its full wire shape.
                if (!text.Contains("UNRECOGNISED ITEM STATE", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(UnrecognisedItemStateLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0029 ITEM_STATE (Inventory=0) " +
                $"without seeing 0x001D MESSAGE_STRING containing \"UNRECOGNISED ITEM STATE\". " +
                $"Same drain-loop budget as Wave 24's sibling test; the failure modes are " +
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
