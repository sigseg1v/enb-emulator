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
/// Wave 43 post-handshake survival round-trip: client sends 0x0080
/// MANUFACTURE_TECH_LEVEL_FILTER with Enable=0 and an empty BitField
/// (no-op against a fresh character with zero known formulas), then
/// verifies the connection survives via 0x0044 REQUEST_TIME.
///
/// <para>
/// Wire layout (from the canonical packet struct at
/// <c>common/include/net7/PacketStructures.h:1068-1073</c>):
/// </para>
/// <code>
///   struct ManufactureTechLevelFilter
///   {
///       int32_t GameID;
///       char    Enable;
///       int32_t BitField;
///   } ATTRIB_PACKED;
/// </code>
/// <para>
/// 9-byte packed payload. The handler reads the struct via direct
/// cast then <c>ntohl</c>-decodes <c>BitField</c>; <c>Enable</c> is a
/// single byte (endianness-invariant); <c>GameID</c> is ignored
/// (identity comes from the connection). We send Enable=0,
/// BitField=0x00000000 — the dispatch path goes through the
/// <c>else</c> arm of the if/else at PlayerManufacturing.cpp:695-702
/// which AND-NOTs zero into the existing bitfield (a no-op). Then
/// <c>BuildManufactureList()</c> runs at line 704.
/// </para>
///
/// <para>
/// Server handler walk-through.
/// </para>
/// <list type="number">
///   <item>Dispatcher case at
///     <c>server/src/PlayerConnection.cpp:539-541</c> calls
///     <c>HandleManufactureLevelFilter(data)</c>.</item>
///   <item><c>HandleManufactureLevelFilter</c> at
///     <c>server/src/PlayerManufacturing.cpp:688-705</c>: casts to
///     <c>ManufactureTechLevelFilter*</c>, reads <c>Enable</c>
///     directly, <c>ntohl</c>'s <c>BitField</c>, then OR's or
///     AND-NOTs it into <c>ManuIndex()-&gt;TechFilterBitField</c>.
///     Enable=0 + BitField=0 → AND-NOT zero → no change. Then
///     calls <c>BuildManufactureList()</c>.</item>
///   <item><c>BuildManufactureList</c> at
///     <c>server/src/PlayerManufacturing.cpp:707-741</c>: iterates
///     <c>m_ManuRecipes</c> which is cleared in the Player ctor at
///     <c>server/src/PlayerClass.cpp:184</c> and stays empty for a
///     freshly-created character (no skill-up actions in this test
///     have populated it). The for-loop is a no-op. Then
///     <c>SendAuxManu()</c> fires at line 739 which emits a single
///     0x001B AUX_DATA frame carrying the (empty) ManuIndex state.
///     Zero crashes, zero malloc churn, no observer fan-out beyond
///     the single back-to-self frame.</item>
/// </list>
/// <para>
/// Same favourable post-emit shape as the prior 0x0044-survival-probe
/// waves: handler dispatches, decodes payload, mutates a single u32
/// of session state (zero-OR / zero-AND-NOT is a no-op), and emits
/// one bounded reply. <c>m_ManuRecipes.empty()</c> guarantees the
/// loop never indexes <c>g_ItemBaseMgr</c> with an unknown ID, so
/// the famous Phase K wave-11 null-deref class never fires here.
/// </para>
///
/// <para>
/// Why this wave target (post-pivot from 0x009D and the
/// manufacture-mutation family).
/// 0x009D STARBASE_AVATAR_CHANGE mutates position/orient/action-flag
/// state and fan-outs to observers — not a clean survival probe.
/// The manufacture-action family (0x0079 / 0x007A / 0x007B / 0x007C
/// / 0x007E) likewise mutates inventory and ManuIndex item slots in
/// ways that require terminal-state setup. 0x0080 was the first
/// manufacture-family handler whose mutation path is a guaranteed
/// no-op on a freshly-spawned character: empty m_ManuRecipes plus
/// AND-NOT-zero on the session bitfield equals "did absolutely
/// nothing visible to anyone except a single AUX_DATA echo."
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at PlayerConnection.cpp:539-541.</b>
///     The 0x0080 case sits between 0x007E MANUFACTURE_ACTION
///     (line 535) and 0x0082 RECUSTOMIZE_SHIP_DONE (line 543). A
///     copy-paste swap with HandleManufactureAction (which casts to
///     the much-larger <c>ManufactureAction</c> struct and walks
///     into the AnalyseDismantleSetItem path) on our 9B payload
///     would over-read or land in a destructive recustomize-ship
///     arm. A swap with HandleRecustomizeShipDone (which mutates
///     ship_data and SaveDatabase's) on our 9B payload would either
///     over-read OR scribble random bytes into HullPrimaryColor —
///     the SaveDatabase call at the end of that handler would then
///     persist garbage. The survival probe catches the survive-vs-
///     crash boundary even when the garbage isn't immediately
///     visible.
///   </item>
///   <item>
///     <b>PacketStructures.h ManufactureTechLevelFilter layout
///     regression.</b> Currently 9B packed:
///     <c>int32_t GameID; char Enable; int32_t BitField;</c> with
///     <c>ATTRIB_PACKED</c>. Without <c>ATTRIB_PACKED</c> on Linux
///     GCC the struct would gain 3B padding between
///     <c>Enable</c> and <c>BitField</c> (12B total) — the handler
///     would read <c>BitField</c> from offset 8 of our 9B buffer
///     which is past the end (UB). A regression dropping the
///     ATTRIB_PACKED on this single struct silently passes the
///     compile but corrupts the wire-protocol contract. The
///     survival probe catches the crash but not the silent-garbage
///     case — that's why the suite ALSO has the byte-comparison
///     coverage waves.
///   </item>
///   <item>
///     <b><c>ntohl</c> revert at PlayerManufacturing.cpp:692.</b> A
///     regression that drops the <c>ntohl</c> on
///     <c>Filter-&gt;BitField</c> on a little-endian Linux host
///     would feed a byte-swapped BitField into the AND-NOT — for
///     our payload of zero this is a no-op so the test still
///     passes. (For a non-zero BitField wave the byte order would
///     matter; this wave is the cheap survival probe, not the
///     semantic-correctness check.)
///   </item>
///   <item>
///     <b><c>long</c>-widening of <c>BitField</c> at
///     PlayerManufacturing.cpp:692.</b> The handler currently
///     stores the ntohl result in <c>long BitField</c>; on Linux
///     x86_64 <c>long</c> is 8 bytes. The upper 4 bytes are
///     uninitialised. The subsequent
///     <c>SetTechFilterBitField(... | BitField)</c> implicitly
///     narrows back to u32. A future "treat BitField as 64-bit"
///     refactor that propagates the long width into the setter
///     would corrupt the TechFilterBitField. The survival probe
///     catches the crash-vs-survive boundary; the silent-corruption
///     case requires the byte-comparison wave.
///   </item>
///   <item>
///     <b>BuildManufactureList loop-boundary regression at
///     PlayerManufacturing.cpp:716-728.</b> The
///     <c>m_ManuRecipes.begin() != m_ManuRecipes.end()</c> guard
///     correctly skips the loop on empty maps. A refactor that
///     replaces the iterator-pair walk with index-based access
///     (e.g. <c>for (size_t i = 0; i &lt; m_ManuRecipes.size();
///     i++)</c> backed by a <c>vector</c>-typed
///     <c>m_ManuRecipes</c> refactor) would still be empty-safe,
///     but a buggy switch to <c>i &lt;= size()</c> would
///     out-of-bounds dereference the iterator. Survival probe
///     would crash.
///   </item>
///   <item>
///     <b>AuxManufacturingIndex::TechFilterBitField init regression
///     at AuxManufacturingIndex.cpp:553.</b> Currently
///     <c>Data.TechFilterBitField = 0</c> in the ctor. A refactor
///     that leaves the field uninitialised would surface here:
///     the AND-NOT of zero against undefined memory still produces
///     undefined memory, which then flows into SetTechFilterBitField
///     and the subsequent ReplaceData write (sets a dirty bit). The
///     test still passes for the survival check (no crash), but
///     the AUX_DATA frame emitted contains the undefined u32 — the
///     byte-comparison wave catches that. Survival here catches
///     the crash-class only.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     PlayerConnection.cpp:127.</b> Phase K sizeof(int32_t)
///     opcode-header fix keeps the per-client UDP queue header at
///     4B; a revert corrupts the 0x2016 inner-tuple parser →
///     REQUEST_TIME survival probe silent.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard
///     tightening at <c>UDPProxyToClient_linux.cpp:568</c>.</b>
///     Currently passes 0x0034 CLIENT_SET_TIME because 0x0034 &lt;
///     0x0FFF, and passes 0x001B AUX_DATA (the SendAuxManu reply)
///     same reason. A guard tightening from <c>opcode &lt; 0x0FFF</c>
///     to <c>opcode &lt; 0x0030</c> would drop both 0x0034 and
///     0x001B silently and the survival probe would time out
///     waiting for CLIENT_SET_TIME.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x0080
/// MANUFACTURE_TECH_LEVEL_FILTER is the opcode the retail client
/// emits whenever the user toggles a tech-level filter checkbox in
/// the manufacture UI. Sending Enable=0 with BitField=0 (a no-op
/// against the existing filter state) is legal client behaviour —
/// the UI can re-emit current state on tab focus. The retail
/// server's "AND-NOT zero is a no-op" is exactly what we preserve.
/// Zero permissiveness added; not loosening any security posture;
/// not fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; 0x0080 + REQUEST_TIME round-trip is
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorManufactureTechLevelFilterTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorManufactureTechLevelFilterTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ManufactureTechLevelFilter_DisableZeroBitField_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        // cli_test40 — Pool[38]. Dedicated to this wave so its
        // Create/Delete cycle doesn't collide with Pool slots owned
        // by earlier waves. seed.sql carries the matching 9_000_040
        // row.
        var account = TestAccounts.Pool[38];
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Anufa" — starts with lowercase 'a' for the
        // AccountManager.cpp:1147 vowel-check footgun (case-sensitive
        // a/e/i/o/u/y BEFORE toupper at line 1153).
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Anufa", shipName: "AnufaShip", cts.Token);

        try
        {
            // 0x0080 MANUFACTURE_TECH_LEVEL_FILTER — 9B packed payload:
            //   int32_t GameID   (handler ignores; identity from connection)
            //   char    Enable   (0 → AND-NOT arm at line 701)
            //   int32_t BitField (0 → AND-NOT-zero is a no-op against
            //                     the existing TechFilterBitField)
            byte[] payload = new byte[9];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);  // GameID
            payload[4] = 0;                                                    // Enable
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(5, 4), 0);  // BitField

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.ManufactureTechLevelFilter.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the
            // MANUFACTURE_TECH_LEVEL_FILTER handler? Send REQUEST_TIME
            // and assert CLIENT_SET_TIME echoes our sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Tolerate interleaved
            // in-sector frames (positional updates from observers, plus
            // the 0x001B AUX_DATA frame SendAuxManu emits as a response
            // to our 0x0080).
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
                $"drained {maxFrames} frames after sending 0x0080 MANUFACTURE_TECH_LEVEL_FILTER + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the dispatcher arm at PlayerConnection.cpp:539 got mis-routed " +
                $"(swap with HandleManufactureAction → ManufactureAction over-reads our 9B payload), " +
                $"ATTRIB_PACKED on the ManufactureTechLevelFilter struct at PacketStructures.h:1068 was dropped " +
                $"(BitField reads past buffer end), " +
                $"the BuildManufactureList loop at PlayerManufacturing.cpp:716 had its iterator-end guard " +
                $"replaced with an off-by-one index, " +
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
