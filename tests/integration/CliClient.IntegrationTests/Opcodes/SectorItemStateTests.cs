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
        // cli_test21 — Pool[19]. Dedicated to this test so its
        // Create/Delete cycle doesn't collide with Pool[3..18] which
        // are owned by prior Phase K waves.
        var account = TestAccounts.Pool[19];
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
}
