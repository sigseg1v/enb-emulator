// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 48 post-handshake survival round-trip: client sends 0x007B
/// MANUFACTURE_ITEM_ID with Item=0 — the requested item template
/// doesn't exist (id 0 is reserved-empty in the items DB), so the
/// handler's <c>if (!m_CurManuItem)</c> early-bail fires after the
/// SendItemBase(Item) call short-circuits silently. The handler
/// emits ONE 0x001D MESSAGE_STRING ("Invalid Item!") frame and
/// returns. ZERO state mutation past that point.
///
/// <para>
/// Wire layout (from <c>common/include/net7/PacketStructures.h:1062-1066</c>):
/// </para>
/// <code>
///   struct ManufactureData
///   {
///       int32_t GameID;
///       int32_t Data;
///   } ATTRIB_PACKED;
/// </code>
/// <para>
/// 8-byte packed payload — same struct as Wave 44/45/46. Item=0
/// (the Data field, network byte order) is the invalid-item
/// sentinel.
/// </para>
///
/// <para>
/// Server handler walk-through.
/// </para>
/// <list type="number">
///   <item>Dispatcher case at
///     <c>server/src/PlayerConnection.cpp:527-529</c> calls
///     <c>HandleManufactureSetItem(data)</c>.</item>
///   <item><c>HandleManufactureSetItem</c> at
///     <c>server/src/PlayerManufacturing.cpp:333-440</c>: casts data →
///     <c>ManufactureData *</c>, <c>ntohl</c>-decodes
///     <c>Packet-&gt;Data</c> into <c>long Item</c>, runs the
///     redundant <c>if (Item &gt; 0xFFFF) Item = ntohl(...)</c> guard
///     (no-op for Item=0).</item>
///   <item><c>if (m_Manufacturing) return;</c> at line 343 —
///     <c>m_Manufacturing</c> is ctor-initialised to <c>false</c> at
///     <c>server/src/PlayerClass.cpp:214</c> so this guard does not
///     fire on fresh char.</item>
///   <item><c>SendItemBase(0)</c> at line 348 — delegates to
///     <c>ItemBaseManager::SendItem</c> via
///     <c>server/src/PlayerInventory.cpp</c>. The
///     <c>ItemBaseManager::SendItem(Player *, long ItemID)</c>
///     overload in <c>server/src/ItemBaseManager.cpp</c> calls
///     <c>GetItem(0)</c>; <c>GetItem(long ItemID)</c> returns
///     <c>m_ItemDB[0]</c> which is <c>nullptr</c> (item template id 0
///     is reserved-empty in the items database). The
///     <c>if (m_Item &amp;&amp; m_Item-&gt;BuildPacket())</c> guard
///     short-circuits on the null pointer so SendItemBase emits ZERO
///     frames.</item>
///   <item><c>m_CurManuItem = g_ItemBaseMgr-&gt;GetItem(0) = nullptr</c>
///     at line 350.</item>
///   <item><c>if (!m_CurManuItem) { SendVaMessage("Invalid Item!"); return; }</c>
///     at lines 352-356 — the invalid-item early-bail fires.
///     <c>SendVaMessage</c> emits ONE 0x001D MESSAGE_STRING frame
///     with the literal "Invalid Item!" payload, then the handler
///     returns.</item>
///   <item>The 100+ lines of state-mutating code at
///     <c>PlayerManufacturing.cpp:358-439</c> (AllowManufacture gate,
///     ResetManuItems, ManuIndex Target/Components SetItemTemplateID,
///     Cost/ManufactureCost calculation, Negotiate,
///     CheckItemRequirements, quality calculations, SendAuxManu
///     emit) all sit BEHIND the invalid-item early-bail and NEVER
///     run on this code arm.</item>
/// </list>
///
/// <para>
/// Why this wave target. Continuation of Wave 44/45/46/47's per-arm
/// re-evaluation of the bulk-UNSAFE manufacture-action family.
/// 0x007B requires <c>m_CurManuItem</c> to be set on the
/// state-mutating path — but with Item=0 the GetItem lookup
/// returns nullptr and the handler's existing early-bail at line
/// 352 fires before any state-mutating code runs. New
/// seam-discovery pattern variant: <b>"handler emits a
/// SendItemBase + null-item-early-bail when the requested item
/// template doesn't exist"</b> — generalisable to any handler that
/// resolves an item-ID-from-payload via
/// <c>g_ItemBaseMgr-&gt;GetItem</c> and has an
/// <c>if (!m_CurX)</c> guard.
/// </para>
///
/// <para>
/// Concrete regression classes this catches (12 classes documented
/// in the TestedOpcodes.cs entry — abbreviated here):
/// </para>
/// <list type="bullet">
///   <item>Dispatcher mis-route at PlayerConnection.cpp:527-529 —
///     case sits between 0x007A HandleManufactureCategorySelection
///     (line 523) and 0x007C HandleRefineSetItem (line 531); swaps
///     with neighbours all walk near-identical Item=0 early-bail
///     paths that survive.</item>
///   <item>PacketStructures.h ManufactureData ATTRIB_PACKED
///     regression at PacketStructures.h:1062-1066.</item>
///   <item>ntohl revert at PlayerManufacturing.cpp:336 — Item=0
///     endian-invariant so test still passes; future non-zero-Item
///     wave catches.</item>
///   <item>m_Manufacturing init regression at PlayerClass.cpp:214 —
///     if ctor flips to true, the early-return at line 343 fires and
///     the test's MESSAGE_STRING reply never comes; survival via
///     REQUEST_TIME still passes since the handler does nothing in
///     that branch.</item>
///   <item>GetItem(0) returns-non-null regression at
///     ItemBaseManager.cpp — if a future db migration assigns a real
///     item to template ID 0, the handler proceeds past line 352
///     into the AllowManufacture gate; state-inspect wave would
///     surface the divergence.</item>
///   <item>SendItemBase silent-fail regression at
///     ItemBaseManager.cpp — if the `if (m_Item &amp;&amp;
///     BuildPacket())` guard is converted to unconditional
///     SendOpcode, a SendOpcode on a null ItemBase would SEGV at the
///     m_Item-&gt;Packet() dereference; survival catches.</item>
///   <item>SendVaMessage payload-format regression at
///     PlayerClass.cpp — the "Invalid Item!" literal is
///     preserved-as-string; a refactor that changes the format-spec
///     to %s with a null arg would crash on the va_arg pull;
///     survival catches.</item>
///   <item>SendOpcode header-width revert at
///     PlayerConnection.cpp:127.</item>
///   <item>Proxy SendClientPacketSequence inner-opcode guard
///     tightening at UDPProxyToClient_linux.cpp:568 — currently
///     passes 0x0034 CLIENT_SET_TIME and 0x001D MESSAGE_STRING
///     because both &lt; 0x0FFF.</item>
///   <item>Proxy default-case ForwardClientOpcode regression for
///     0x007B — not explicitly cased in proxy stubs; falls through
///     to bottom-of-switch default.</item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x007B with Item=0 models
/// a UI race where the player selects a manufacture recipe that's
/// just been delisted server-side — the retail Win32 client emits
/// the requested template ID and the retail server's
/// <c>if (!m_CurManuItem)</c> early-bail returns "Invalid Item!" to
/// the client without mutating any session state. Zero
/// permissiveness added; not loosening any security posture; not
/// fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; 0x007B + REQUEST_TIME round-trip is
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorManufactureSetItemTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorManufactureSetItemTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ManufactureSetItem_InvalidItemZero_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Uria" starts with lowercase 'u' for the
        // AccountManager.cpp:1147 vowel-check footgun (case-sensitive
        // a/e/i/o/u/y BEFORE toupper at line 1153).
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Uria", shipName: "UriaShip", cts.Token);

        try
        {
            // 0x007B MANUFACTURE_ITEM_ID (HandleManufactureSetItem) —
            // 8B packed payload:
            //   int32_t GameID   (handler ignores; identity from connection)
            //   int32_t Data     (network byte order — Item=0
            //                     (sentinel invalid item) triggers the
            //                     if (!m_CurManuItem) early-bail at
            //                     PlayerManufacturing.cpp:352-356;
            //                     SendVaMessage emits one 0x001D
            //                     MESSAGE_STRING "Invalid Item!" then
            //                     return)
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);  // GameID
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), 0);     // Data=0 (network order)

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.ManufactureSetItem.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the
            // MANUFACTURE_ITEM_ID handler? Send REQUEST_TIME and
            // assert CLIENT_SET_TIME echoes our sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Tolerate the
            // 0x001D MESSAGE_STRING "Invalid Item!" reply plus any
            // interleaved positional-update frames from in-sector
            // observers.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.ClientSetTime.Value)
                    continue;

                var span = reply.Payload.Span;
                Assert.Equal(12, span.Length);

                int echoedClientSent = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);
                Assert.Equal(clientTick, echoedClientSent);

                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x007B MANUFACTURE_ITEM_ID + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the dispatcher arm at PlayerConnection.cpp:527 got mis-routed, " +
                $"GetItem(0) now returns a non-null pointer (db migration added item id 0) and the " +
                $"AllowManufacture path crashed, " +
                $"SendVaMessage format-spec regression at PlayerClass.cpp crashed on the literal, " +
                $"the proxy's bottom-of-switch ForwardClientOpcode default at proxy/ClientToServer_linux_stubs.cpp dropped 0x007B, " +
                $"or the SendOpcode header-width fix at PlayerConnection.cpp:127 was reverted.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
