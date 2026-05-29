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
/// Wave 49 post-handshake survival round-trip: client sends 0x007C
/// REFINERY_ITEM_ID with Item=0 — the requested item template
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
/// 8-byte packed payload — same struct as Wave 44/45/46/48. Item=0
/// (the Data field, network byte order) is the invalid-item
/// sentinel.
/// </para>
///
/// <para>
/// Server handler walk-through. Near-identical to the
/// HandleManufactureSetItem handler covered by Wave 48 (0x007B at
/// PlayerManufacturing.cpp:333-440) — the differences are flagged
/// below.
/// </para>
/// <list type="number">
///   <item>Dispatcher case at
///     <c>server/src/PlayerConnection.cpp:531-533</c> calls
///     <c>HandleRefineSetItem(data)</c>.</item>
///   <item><c>HandleRefineSetItem</c> at
///     <c>server/src/PlayerManufacturing.cpp:442-497</c>: casts data →
///     <c>ManufactureData *</c>, decodes
///     <c>long Item = Packet-&gt;Data</c> (note: NO ntohl on the
///     first read — distinct from 0x007B which does
///     <c>long Item = ntohl(Packet-&gt;Data)</c> unconditionally
///     at line 336; here the ntohl only fires inside the
///     <c>if (Item &gt; 0xFFFF)</c> re-swap guard. Item=0 is
///     endian-invariant either way so the test passes; a future
///     non-zero-Item refine wave would expose the per-handler
///     endian-handling divergence).</item>
///   <item><c>if (m_Manufacturing) return;</c> at line 453 —
///     <c>m_Manufacturing</c> is ctor-initialised to <c>false</c> at
///     <c>server/src/PlayerClass.cpp:214</c> so this guard does not
///     fire on fresh char.</item>
///   <item><c>SendItemBase(0)</c> at line 458 — delegates to
///     <c>ItemBaseManager::SendItem</c>; <c>GetItem(0)</c> returns
///     <c>nullptr</c> (template id 0 is reserved-empty) and the
///     <c>if (m_Item &amp;&amp; m_Item-&gt;BuildPacket())</c>
///     null-guard short-circuits, SendItemBase emits ZERO
///     frames.</item>
///   <item><c>m_CurManuItem = g_ItemBaseMgr-&gt;GetItem(0) = nullptr</c>
///     at line 460.</item>
///   <item><c>if (!m_CurManuItem) { SendVaMessage("Invalid Item!"); return; }</c>
///     at lines 462-466 — the invalid-item early-bail fires.
///     <c>SendVaMessage</c> emits ONE 0x001D MESSAGE_STRING frame
///     with the literal "Invalid Item!" payload, then the handler
///     returns.</item>
///   <item>The 30+ lines of state-mutating code at
///     <c>PlayerManufacturing.cpp:468-496</c> (ResetManuItems,
///     ManuIndex Target/Components SetItemTemplateID loop,
///     SetValidity, SetAdditionalIterations, SetBaseCost, Negotiate,
///     CheckItemRequirements, SendAuxManu emit) all sit BEHIND the
///     invalid-item early-bail and NEVER run on this code arm.
///     Importantly: HandleRefineSetItem has NO AllowManufacture
///     gate (which 0x007B has at line 360) — for any non-null
///     m_CurManuItem the refine path would run unconditionally.</item>
/// </list>
///
/// <para>
/// Why this wave target. Companion to Wave 48 (0x007B
/// HandleManufactureSetItem with Item=0) — completes the
/// manufacture-action family coverage. Same seam-discovery pattern
/// variant: <b>"null-item-from-GetItem early-bail"</b> —
/// generalisable to any handler that resolves an
/// item-ID-from-payload via <c>g_ItemBaseMgr-&gt;GetItem</c> and has
/// an <c>if (!m_CurX)</c> guard.
/// </para>
///
/// <para>
/// Concrete regression classes this catches (13 classes documented
/// in the TestedOpcodes.cs entry — abbreviated here):
/// </para>
/// <list type="bullet">
///   <item>Dispatcher mis-route at PlayerConnection.cpp:531-533 —
///     case sits between 0x007B HandleManufactureSetItem (line 527)
///     and 0x007E HandleManufactureAction (line 535); swaps with
///     neighbours all walk near-identical Item=0 / Action=0
///     fresh-char-safe paths that survive but byte-comparison wave
///     would catch the divergence.</item>
///   <item>PacketStructures.h ManufactureData ATTRIB_PACKED
///     regression at PacketStructures.h:1062-1066.</item>
///   <item>Bit-revert at PlayerManufacturing.cpp:446 — the
///     handler-specific endian quirk (no leading ntohl, only the
///     re-swap guard) is preserved verbatim.</item>
///   <item>m_Manufacturing init regression at PlayerClass.cpp:214 —
///     if ctor flips to true, the early-return at line 453 fires;
///     survival via REQUEST_TIME still passes.</item>
///   <item>GetItem(0) returns-non-null regression at
///     ItemBaseManager.cpp — if a future db migration assigns a real
///     item to template ID 0, the handler proceeds past line 462
///     into the UNCONDITIONAL refine path (no AllowManufacture gate
///     — the refine handler is strictly less guarded than 0x007B);
///     ResetManuItems/SetItemTemplateID loop runs against the real
///     item, SetBaseCost reads from m_CurManuItem, Negotiate reads
///     RefineSkill, CheckItemRequirements iterates components; for a
///     fresh char the path almost certainly emits a SendAuxManu and
///     returns without crashing — byte-comparison wave catches.</item>
///   <item>SendItemBase silent-fail regression at
///     ItemBaseManager.cpp — if the null-guard is converted to
///     unconditional SendOpcode, a SendOpcode on a null ItemBase
///     would SEGV; survival catches.</item>
///   <item>SendVaMessage payload-format regression at
///     PlayerClass.cpp — the "Invalid Item!" literal is
///     preserved-as-string.</item>
///   <item>SendOpcode header-width revert at
///     PlayerConnection.cpp:127.</item>
///   <item>Proxy SendClientPacketSequence inner-opcode guard
///     tightening at UDPProxyToClient_linux.cpp:568.</item>
///   <item>Proxy default-case ForwardClientOpcode regression for
///     0x007C — not explicitly cased in proxy stubs.</item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x007C with Item=0 models
/// a UI race where the player selects a refine recipe that's just
/// been delisted server-side — the retail Win32 client emits the
/// requested template ID and the retail server's
/// <c>if (!m_CurManuItem)</c> early-bail returns "Invalid Item!" to
/// the client without mutating any session state. Zero
/// permissiveness added; not loosening any security posture; not
/// fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; 0x007C + REQUEST_TIME round-trip is
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorRefinerySetItemTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorRefinerySetItemTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task RefinerySetItem_InvalidItemZero_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Orin" starts with lowercase 'o' for the
        // AccountManager.cpp:1147 vowel-check footgun (case-sensitive
        // a/e/i/o/u/y BEFORE toupper at line 1153).
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Orin", shipName: "OrinShip", cts.Token);

        try
        {
            // 0x007C REFINERY_ITEM_ID (HandleRefineSetItem) —
            // 8B packed payload:
            //   int32_t GameID   (handler ignores; identity from connection)
            //   int32_t Data     (network byte order — Item=0
            //                     (sentinel invalid item) triggers the
            //                     if (!m_CurManuItem) early-bail at
            //                     PlayerManufacturing.cpp:462-466;
            //                     SendVaMessage emits one 0x001D
            //                     MESSAGE_STRING "Invalid Item!" then
            //                     return)
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);  // GameID
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), 0);     // Data=0 (network order)

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RefinerySetItem.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the
            // REFINERY_ITEM_ID handler? Send REQUEST_TIME and
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
                $"drained {maxFrames} frames after sending 0x007C REFINERY_ITEM_ID + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the dispatcher arm at PlayerConnection.cpp:531 got mis-routed, " +
                $"GetItem(0) now returns a non-null pointer (db migration added item id 0) and the " +
                $"unconditional refine path crashed (no AllowManufacture gate to bounce off), " +
                $"SendVaMessage format-spec regression at PlayerClass.cpp crashed on the literal, " +
                $"the proxy's bottom-of-switch ForwardClientOpcode default at proxy/ClientToServer_linux_stubs.cpp dropped 0x007C, " +
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
