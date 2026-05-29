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
/// Wave 46 post-handshake survival round-trip: client sends 0x007E
/// MANUFACTURE_ACTION with Action=0 (ACTION_LEAVE_TERMINAL) — but the
/// fresh-char player's manufacture-mode is MODE_NONE (=0, the
/// ctor-init default), and the outer switch in
/// <c>HandleManufactureAction</c> has no <c>case MODE_NONE</c> arm —
/// so any Action value falls into the outer-switch default at line
/// 682 which is a pure LogMessage. ZERO state mutation. ZERO reply.
/// Connection survival verified via 0x0044 REQUEST_TIME.
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
/// 8-byte packed payload — same struct as Wave 44 (0x0079) and Wave
/// 45 (0x007A). The Action field is network-order.
/// </para>
///
/// <para>
/// Server handler walk-through.
/// </para>
/// <list type="number">
///   <item>Dispatcher case at
///     <c>server/src/PlayerConnection.cpp:535-537</c> calls
///     <c>HandleManufactureAction(data)</c>.</item>
///   <item><c>HandleManufactureAction</c> at
///     <c>server/src/PlayerManufacturing.cpp:499-686</c>: casts data →
///     <c>ManufactureData *</c>, <c>ntohl</c>-decodes
///     <c>Packet-&gt;Data</c> into <c>long Action</c>, LogDebugs,
///     defines a local <c>int NextFormula = 0</c> and grabs
///     <c>SectorManager *sm = GetSectorManager()</c> (unused on the
///     path we take), then switches on
///     <c>ManuIndex()-&gt;GetMode()</c> at line 508.</item>
///   <item>On a fresh-character session,
///     <c>AuxManufacturingIndex::Data.Mode</c> is ctor-initialised to
///     0 at <c>AuxManufacturingIndex.cpp:394</c>. The
///     <c>Manufacture_Mode</c> enum at
///     <c>server/src/PlayerManufacturing.h:29-37</c> declares
///     <c>MODE_NONE</c> as the first value (= 0),
///     <c>MODE_MANUFACTURE = 1</c>, <c>MODE_ANALIZE = 2</c>,
///     <c>MODE_DISMANTLE = 3</c>, <c>MODE_REFINE = 4</c>,
///     <c>MODE_REFINE_STACK = 5</c>. The mode is only flipped to a
///     non-zero value when the player opens a manufacture terminal
///     in a starbase (via the unrelated <c>TerminalReset</c> at
///     <c>AuxManufacturingIndex.cpp:210-241</c>), which we don't do
///     in this test. So <c>GetMode()</c> returns
///     <c>MODE_NONE = 0</c>.</item>
///   <item>The outer switch covers MODE_MANUFACTURE / MODE_DISMANTLE
///     / MODE_ANALIZE / MODE_REFINE — but NOT MODE_NONE. So for
///     mode=0 we fall into the outer-switch default arm at line
///     682-684 which is purely
///     <c>LogMessage("ManufactureAction - Unknown Action: %d\n", ManuIndex()-&gt;GetMode())</c>
///     — log only, no SendOpcode, no state mutation, no SaveDatabase,
///     no credit deduction, no SetCredits, no sm-&gt;AddTimedCall
///     scheduling.</item>
/// </list>
/// <para>
/// Strictly safer than Wave 44 (which had a SendAuxManu emit) and
/// Wave 45 (which also had a SendAuxManu emit). Wave 46 is the
/// cleanest possible direct-stimulus probe — handler dispatches,
/// decodes 4 bytes, logs, and returns. The only externally
/// observable change is a server log line.
/// </para>
///
/// <para>
/// Why this wave target. Continuation of Wave 44+45's per-arm
/// re-evaluation of the bulk-UNSAFE manufacture-action family. Wave
/// 44 found 0x0079 Terminal=0 safe; Wave 45 found 0x007A Category=0
/// safe; Wave 46 finds 0x007E safe on fresh char because the outer
/// switch on the manufacture-mode has no MODE_NONE arm and falls
/// into a pure-log default. Generalisable seam-discovery pattern:
/// "handler switches on a per-session state field whose ctor-init
/// value lacks a switch arm" → default-arm fall-through is usually
/// a benign LogMessage / no-op.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at PlayerConnection.cpp:535-537.</b>
///     The 0x007E case sits between 0x007C REFINERY_ITEM_ID (line
///     531, routes to HandleRefineSetItem) and 0x0080
///     MANUFACTURE_TECH_LEVEL_FILTER (line 539, routes to
///     HandleManufactureLevelFilter — Wave 43). A swap with
///     HandleRefineSetItem reads a wider struct and would over-read
///     our 8B payload into stack garbage. A swap with
///     HandleManufactureLevelFilter reads the same 9B layout — for
///     our 8B payload the trailing Enable byte would be read past
///     the buffer end (UB).
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
///     <b><c>ntohl</c> revert at PlayerManufacturing.cpp:502.</b>
///     A regression dropping the ntohl on <c>Packet-&gt;Data</c> feeds
///     a byte-swapped Action into the inner switches — but since the
///     outer switch on GetMode() = MODE_NONE = 0 falls through to
///     default before any inner-switch evaluation, the Action value
///     doesn't matter for survival. Documented for completeness.
///   </item>
///   <item>
///     <b><c>long</c>-widening of Action at
///     PlayerManufacturing.cpp:502.</b> Same generic Linux x86_64
///     long-width footgun documented in Wave 44/45. The Action value
///     is unused on the MODE_NONE default path.
///   </item>
///   <item>
///     <b>MODE_NONE enum-value drift at
///     PlayerManufacturing.h:31.</b> MODE_NONE is currently the first
///     enum value (= 0) and the ctor-init at
///     AuxManufacturingIndex.cpp:394 sets Data.Mode = 0. A refactor
///     that inserts a new enum value before MODE_NONE would change
///     the ctor-init's effective mode and might match a real
///     mode-arm — Survival probe might still pass (any non-MODE_REFINE
///     arm with Action=0 takes the ACTION_LEAVE_TERMINAL branch which
///     for MODE_MANUFACTURE / MODE_DISMANTLE / MODE_ANALIZE / MODE_REFINE
///     is bounded behavior; for MODE_REFINE specifically
///     ACTION_LEAVE_TERMINAL is a plain break per line 627-628) but
///     a byte-comparison wave would catch the divergence.
///   </item>
///   <item>
///     <b>MODE_NONE arm insertion regression at
///     PlayerManufacturing.cpp:508-685.</b> A refactor that adds a
///     <c>case MODE_NONE</c> arm with state-mutating body would
///     change the no-op semantics; survival probe wouldn't catch
///     the semantic shift but a byte-comparison or state-inspect wave
///     would.
///   </item>
///   <item>
///     <b>Outer-switch default crash regression at
///     PlayerManufacturing.cpp:683.</b> The default arm is currently
///     a single LogMessage call. A refactor that calls a method on a
///     potentially-null pointer (e.g. SaveDatabase via a stale
///     m_CurManuItem) would crash on fresh char. Survival catches.
///   </item>
///   <item>
///     <b>GetSectorManager null deref regression at
///     PlayerManufacturing.cpp:506.</b> The handler grabs
///     <c>SectorManager *sm = GetSectorManager()</c> before the
///     outer switch. On fresh char immediately after SectorLogin,
///     GetSectorManager() should return a valid pointer (the sector
///     thread is the one dispatching this opcode); a regression
///     that returns nullptr here would crash later in inner-switch
///     paths but we don't reach those, so this is documented for
///     completeness.
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
///     0x0FFF. A guard tightening from <c>opcode &lt; 0x0FFF</c>
///     to <c>opcode &lt; 0x0030</c> would drop 0x0034 silently and
///     the survival probe would time out.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode regression for
///     0x007E.</b> 0x007E is NOT explicitly cased in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>'s
///     ProcessSectorServerOpcode switch so it falls through to the
///     bottom-of-switch default arm; a regression dropping that
///     default would mean the server never sees the 0x007E frame.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x007E with Action=0 on a
/// player who has not opened a manufacture terminal is a legal but
/// degenerate client message — the retail Win32 client might emit
/// this on a UI race where the player closes a terminal while a
/// queued action is pending. The retail server's mode-vs-action
/// outer-switch fall-through to a LogMessage default is exactly the
/// correct preservation behaviour: the server doesn't mutate any
/// session state when the player isn't in a manufacture mode. Zero
/// permissiveness added; not loosening any security posture; not
/// fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; 0x007E + REQUEST_TIME round-trip is
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorManufactureActionTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorManufactureActionTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ManufactureAction_ModeNoneOuterSwitchDefault_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Ulani" starts with lowercase 'u' for the
        // AccountManager.cpp:1147 vowel-check footgun (case-sensitive
        // a/e/i/o/u/y BEFORE toupper at line 1153).
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Ulani", shipName: "UlaniShip", cts.Token);

        try
        {
            // 0x007E MANUFACTURE_ACTION (HandleManufactureAction) —
            // 8B packed payload:
            //   int32_t GameID   (handler ignores; identity from connection)
            //   int32_t Data     (network byte order — Action=0
            //                     (ACTION_LEAVE_TERMINAL) is irrelevant
            //                     because the outer switch on
            //                     ManuIndex()->GetMode() = MODE_NONE = 0
            //                     has no case-MODE_NONE arm so we fall
            //                     into the default LogMessage; no
            //                     state mutation, no reply)
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);  // GameID
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), 0);     // Data=0 (network order, irrelevant on default path)

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.ManufactureAction.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the
            // MANUFACTURE_ACTION handler? Send REQUEST_TIME and
            // assert CLIENT_SET_TIME echoes our sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Tolerate interleaved
            // in-sector frames (positional updates from observers).
            // NOTE: unlike Wave 44/45 there's no 0x001B AUX_DATA reply
            // expected — the MODE_NONE default arm doesn't call
            // SendAuxManu — so the only frames between our send and
            // the CLIENT_SET_TIME echo should be positional updates
            // from any in-sector observers.
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
                $"drained {maxFrames} frames after sending 0x007E MANUFACTURE_ACTION + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the dispatcher arm at PlayerConnection.cpp:535 got mis-routed " +
                $"(swap with HandleRefineSetItem over-reads our 8B payload), " +
                $"ATTRIB_PACKED on the ManufactureData struct at PacketStructures.h:1062 was dropped, " +
                $"a case-MODE_NONE arm was added at PlayerManufacturing.cpp:508 with state-mutating body, " +
                $"the outer-switch default at PlayerManufacturing.cpp:683 was changed to call a crashing method, " +
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
