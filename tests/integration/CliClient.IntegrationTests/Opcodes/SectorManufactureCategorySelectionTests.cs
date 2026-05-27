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
/// Wave 45 post-handshake survival round-trip: client sends 0x007A
/// MANUFACTURE_ITEM_CATAGORY (the dispatch name; the handler is
/// <c>HandleManufactureCategorySelection</c>) with Category=0 — the
/// already-default value — then verifies the connection survives via
/// 0x0044 REQUEST_TIME.
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
/// 8-byte packed payload. The handler reads via direct cast, then
/// <c>ntohl</c>-decodes <c>Data</c> into <c>long Category</c>. We
/// send Data=0 — for a fresh char with <c>AuxManufacturingIndex.Data.CurrentItemCat</c>
/// ctor-initialised to 0 (see
/// <c>server/src/AuxClasses/AuxManufacturingIndex.cpp:406</c>), the
/// <c>SetCurrentItemCat(0)</c> call inside the handler hits the
/// <c>ReplaceData</c> equality short-circuit at
/// <c>server/src/AuxClasses/AuxBase.h:94</c> and is a no-op.
/// </para>
///
/// <para>
/// Server handler walk-through.
/// </para>
/// <list type="number">
///   <item>Dispatcher case at
///     <c>server/src/PlayerConnection.cpp:523-525</c> calls
///     <c>HandleManufactureCategorySelection(data)</c>. NOTE: the
///     opcode is literally named <c>MANUFACTURE_ITEM_CATAGORY</c> in
///     <c>common/include/net7/Opcodes.h:119</c> (a typo carried from
///     upstream — same name as 0x0079 but a different macro line).
///     The dispatch table is correct; only the macro names collide.</item>
///   <item><c>HandleManufactureCategorySelection</c> at
///     <c>server/src/PlayerManufacturing.cpp:72-80</c>: casts data →
///     <c>ManufactureData *</c>, <c>ntohl</c>-decodes
///     <c>Packet-&gt;Data</c> into <c>long Category</c>, LogDebugs,
///     calls <c>ManuIndex()-&gt;SetCurrentItemCat(Category)</c>, then
///     <c>BuildManufactureList()</c>.</item>
///   <item><c>SetCurrentItemCat</c> at
///     <c>server/src/AuxClasses/AuxManufacturingIndex.cpp:343-346</c>:
///     <c>ReplaceData(&amp;Data.CurrentItemCat, NewCurrentItemCat, 13)</c>.
///     Per <c>AuxBase.h:82-129</c> the <c>ReplaceData</c> template
///     short-circuits when <c>cur == src</c>; the ctor-init at
///     <c>AuxManufacturingIndex.cpp:406</c> sets the field to 0 and
///     we pass 0 — no dirty-bit flip, no parent-flag walk.</item>
///   <item><c>BuildManufactureList</c> at
///     <c>server/src/PlayerManufacturing.cpp:707-741</c>:
///     <c>KnownFormulas.ResetKnownFormulas(false)</c> iterates only
///     <c>m_NumFormulas</c> entries (per
///     <c>AuxKnownFormulas.cpp:62-70</c>) — fresh char has 0 so the
///     loop body never runs. <c>m_NumFormulas = 0</c>. The for-loop
///     over <c>m_ManuRecipes</c> is empty because the recipe map is
///     cleared in the <c>Player</c> ctor at
///     <c>PlayerClass.cpp:184</c>. <c>GetKnownFormulas()</c> returns
///     0, <c>m_NumFormulas</c> is 0, so 0 &gt; 0 is false and the
///     else-branch runs <c>SetKnownFormulas(0)</c> (another
///     <c>ReplaceData</c> no-op via the same equality short-circuit)
///     plus <c>SendAuxManu()</c> emit-once.</item>
///   <item><c>SendAuxManu</c> at
///     <c>server/src/PlayerClass.cpp:1301-1308</c>: emits a single
///     0x001B AUX_DATA frame via SendOpcode(ENB_OPCODE_001B_AUX_DATA,
///     ManuIndex()-&gt;PacketBuffer, ManuIndex()-&gt;PacketSize) gated
///     on BuildPacket success.</item>
/// </list>
/// <para>
/// Same favourable post-emit shape as the prior survival-probe waves:
/// handler dispatches, decodes payload, mutates nothing (every
/// <c>ReplaceData</c> equality short-circuits and the recipe loop is
/// empty), and emits one bounded reply.
/// </para>
///
/// <para>
/// Why this wave target. Wave 44 outlook flagged the manufacture-
/// action family (0x007A / 0x007B / 0x007C / 0x007E) for per-arm
/// re-evaluation because the Wave 43 bulk-UNSAFE triage was overly
/// pessimistic — confirmed by Wave 44 finding the 0x0079 Terminal=0
/// arm safe. Re-reading 0x007A here confirms the case=0 arm is also
/// safe via the <c>ReplaceData</c> equality short-circuit plus the
/// empty <c>m_ManuRecipes</c> recipe map on fresh char. Wave 45+ will
/// continue re-evaluating 0x007B / 0x007C / 0x007E per-arm; some may
/// have a payload value that triggers similar early-bail behaviour.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at PlayerConnection.cpp:523-525.</b>
///     The 0x007A case sits between 0x0079 (line 519, routes to
///     HandleManufactureTerminal) and 0x007B (line 527, routes to
///     HandleManufactureSetItem). A copy-paste swap with
///     HandleManufactureTerminal on our 8B Category=0 payload would
///     route into the Terminal switch — which Wave 44 already
///     exercises and shows is also safe for Terminal=0, so the swap
///     wouldn't surface here; but a swap with HandleManufactureSetItem
///     reads a wider <c>ManufactureSetItem</c> struct and would over-
///     read our 8B payload. Survival catches the crash boundary.
///   </item>
///   <item>
///     <b>PacketStructures.h ManufactureData layout regression.</b>
///     Currently 8B packed: <c>int32_t GameID; int32_t Data;</c> with
///     ATTRIB_PACKED. Widening either field to <c>long</c> on Linux
///     x86_64 would push Data from offset 4 to offset 8 and the
///     handler would read past our 8B buffer end (UB). Survival probe
///     catches the crash.
///   </item>
///   <item>
///     <b><c>ntohl</c> revert at PlayerManufacturing.cpp:75.</b>
///     A regression dropping the ntohl on <c>Packet-&gt;Data</c> feeds
///     a byte-swapped Category into <c>SetCurrentItemCat</c>. For
///     Category=0 this is a no-op (0 is endian-invariant), so the
///     test still passes — but a future non-zero-Category wave would
///     catch the byte-order regression.
///   </item>
///   <item>
///     <b><c>long</c>-widening of Category at
///     PlayerManufacturing.cpp:75.</b> The handler stores the ntohl
///     result in <c>long Category</c>; on Linux x86_64 long is 8B
///     with upper 4B uninitialised. <c>SetCurrentItemCat</c> takes a
///     <c>u32</c>, so the upper bits are truncated at the call —
///     documented for completeness.
///   </item>
///   <item>
///     <b><c>ReplaceData</c> equality short-circuit regression at
///     AuxBase.h:94.</b> Currently the template's <c>if (cur != src)</c>
///     guard skips the dirty-bit flip and the parent-flag walk when
///     the field is already at the requested value. A refactor that
///     removes this guard would force unnecessary AUX_DATA emits for
///     "no-change" sets; the survival probe wouldn't catch the
///     semantic shift but the byte-comparison wave for the 0x001B
///     reply would.
///   </item>
///   <item>
///     <b><c>BuildManufactureList</c> iteration crash regression at
///     PlayerManufacturing.cpp:716.</b> A refactor that dereferences
///     <c>KnownItem-&gt;first</c> through <c>g_ItemBaseMgr-&gt;GetItem</c>
///     without a null check, or that iterates past <c>m_ManuRecipes.end()</c>,
///     would crash. Survival probe catches the crash even though
///     <c>m_ManuRecipes</c> is empty on fresh char (the iterator
///     equality test still has to evaluate).
///   </item>
///   <item>
///     <b>Empty-map iteration vs sentinel-key bug regression at
///     PlayerClass.cpp:184.</b> The Player ctor clears
///     <c>m_ManuRecipes</c>. If a refactor inserts a sentinel "dummy"
///     entry instead of leaving it empty, BuildManufactureList would
///     try to look up that sentinel via <c>g_ItemBaseMgr-&gt;GetItem</c>
///     and might crash on a null ItemBase pointer. Survival catches.
///   </item>
///   <item>
///     <b><c>ResetKnownFormulas(false)</c> over-iteration regression
///     at AuxKnownFormulas.cpp:64.</b> Currently iterates
///     <c>m_NumFormulas</c> (the count, not the capacity), so on
///     fresh char the loop body never runs. A refactor that swaps
///     for <c>MAX_KNOWN_FORMULAS</c> would touch uninitialised
///     Formula slots — survival catches if the touch crashes.
///   </item>
///   <item>
///     <b>SendAuxManu BuildPacket failure regression at
///     PlayerClass.cpp:1301.</b> The SendAuxManu wrapper gates the
///     SendOpcode on BuildPacket success; a regression making
///     BuildPacket return false on the post-Category-set state would
///     silently skip the AUX_DATA emit. Survival via REQUEST_TIME
///     still passes; the 0x001B byte-comparison wave catches.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     PlayerConnection.cpp:127.</b> Phase K sizeof(int32_t) header
///     fix; revert corrupts the 0x2016 inner-tuple parser and
///     REQUEST_TIME path silents.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard
///     tightening at <c>UDPProxyToClient_linux.cpp:568</c>.</b>
///     Currently passes 0x0034 CLIENT_SET_TIME because 0x0034 &lt;
///     0x0FFF, and passes 0x001B AUX_DATA (the SendAuxManu reply)
///     same reason. A guard tightening from <c>opcode &lt; 0x0FFF</c>
///     to <c>opcode &lt; 0x0030</c> would drop both 0x0034 and
///     0x001B silently and the survival probe would time out.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x007A with Category=0 is
/// what retail Win32 client emits when the manufacture UI is opened
/// to its default (uncategorised) state. The
/// <c>SetCurrentItemCat(0)</c> + <c>BuildManufactureList</c> sequence
/// is exactly what the retail server runs — list of known formulas
/// gets rebuilt for the new category filter and the client receives a
/// single AUX_DATA snapshot. Zero permissiveness added; not loosening
/// any security posture; not fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; 0x007A + REQUEST_TIME round-trip is
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorManufactureCategorySelectionTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorManufactureCategorySelectionTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ManufactureCategorySelect_CategoryZeroDefault_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Ocata" starts with lowercase 'o' for the
        // AccountManager.cpp:1147 vowel-check footgun (case-sensitive
        // a/e/i/o/u/y BEFORE toupper at line 1153).
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Ocata", shipName: "OcataShip", cts.Token);

        try
        {
            // 0x007A MANUFACTURE_ITEM_CATAGORY (HandleManufactureCategorySelection) —
            // 8B packed payload:
            //   int32_t GameID   (handler ignores; identity from connection)
            //   int32_t Data     (network byte order — Category=0 hits the
            //                     ReplaceData equality short-circuit on
            //                     fresh char's ctor-init 0 CurrentItemCat,
            //                     then BuildManufactureList iterates empty
            //                     m_ManuRecipes and SendAuxManu emits one
            //                     0x001B AUX_DATA frame — no state change
            //                     visible beyond the bounded reply)
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);  // GameID
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), 0);     // Data=0 (network order)

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.ManufactureCategorySelect.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the
            // MANUFACTURE_CATEGORY handler? Send REQUEST_TIME and
            // assert CLIENT_SET_TIME echoes our sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Tolerate interleaved
            // in-sector frames (positional updates from observers, plus
            // the 0x001B AUX_DATA frame SendAuxManu emits as a response
            // to our 0x007A).
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
                $"drained {maxFrames} frames after sending 0x007A MANUFACTURE_CATEGORY + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the dispatcher arm at PlayerConnection.cpp:523 got mis-routed " +
                $"(swap with HandleManufactureSetItem over-reads our 8B payload into stack garbage), " +
                $"ATTRIB_PACKED on the ManufactureData struct at PacketStructures.h:1062 was dropped " +
                $"(Data field reads past buffer end), " +
                $"the ReplaceData equality short-circuit at AuxBase.h:94 was removed and the resulting " +
                $"dirty-bit-flip parent-walk crashed, " +
                $"BuildManufactureList at PlayerManufacturing.cpp:716 introduced an iteration UB, " +
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
