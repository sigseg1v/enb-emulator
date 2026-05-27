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
/// Wave 44 post-handshake survival round-trip: client sends 0x0079
/// MANUFACTURE_ITEM_CATAGORY (the dispatch name; the handler is
/// <c>HandleManufactureTerminal</c>) with Terminal=0 — the terminal-
/// exit selector — then verifies the connection survives via 0x0044
/// REQUEST_TIME.
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
/// <c>ntohl</c>-decodes <c>Data</c> into <c>long Terminal</c>. We
/// send Data=0 (network byte order — same byte representation either
/// way for zero), which selects the terminal-exit arm of the outer
/// switch at <c>server/src/PlayerManufacturing.cpp:32-48</c>.
/// </para>
///
/// <para>
/// Server handler walk-through.
/// </para>
/// <list type="number">
///   <item>Dispatcher case at
///     <c>server/src/PlayerConnection.cpp:519-521</c> calls
///     <c>HandleManufactureTerminal(data)</c>. NOTE: the opcode is
///     literally named <c>MANUFACTURE_ITEM_CATAGORY</c> in
///     <c>common/include/net7/Opcodes.h</c> (a typo carried from
///     upstream — 0x007A is also named MANUFACTURE_ITEM_CATAGORY)
///     but its handler dispatches to the terminal-mode setter.
///     Don't be misled by the opcode name; the wire intent of 0x0079
///     is "terminal mode selector" not "item category."</item>
///   <item><c>HandleManufactureTerminal</c> at
///     <c>server/src/PlayerManufacturing.cpp:25-70</c>: casts data →
///     <c>ManufactureData *</c>, <c>ntohl</c>-decodes <c>Packet-&gt;Data</c>
///     into <c>long Terminal</c>, switches on Terminal. Terminal=0
///     enters the case-0 arm at line 32 which has an inner switch
///     on <c>ManuIndex()-&gt;GetMode()</c> with all 4 known cases
///     (MODE_MANUFACTURE / MODE_ANALIZE / MODE_DISMANTLE /
///     MODE_REFINE) being plain <c>break</c> — no mode-change, no
///     SendOpcode, just a fall-through to the post-switch tail —
///     plus a default arm that LogMessage's an unknown previous mode
///     but otherwise no state change. The post-switch tail at lines
///     67-69 unconditionally runs:
///     <c>SetDifficulty(DIFFICULTY_AUTOMATIC)</c>,
///     <c>ResetManuItems()</c>, <c>SendAuxManu()</c>.</item>
///   <item><c>SetDifficulty</c> at
///     <c>server/src/AuxClasses/AuxManufacturingIndex.cpp:468-471</c>:
///     ReplaceData write of a u32. Difficulty is ctor-initialised to
///     0 at AuxManufacturingIndex.cpp:537 — DIFFICULTY_AUTOMATIC is
///     also 0 (the default; redefinition would be flagged by the
///     no-warnings build), so the ReplaceData detects no change and
///     skips the dirty-bit flip. No-op on fresh char.</item>
///   <item><c>ResetManuItems</c> at
///     <c>server/src/AuxClasses/AuxManufacturingIndex.cpp:239-249</c>:
///     calls <c>.Empty()</c> on Override.Item[0], Target.Item[0], and
///     Components.Item[0..5]. All struct slots, no allocations. On a
///     fresh char those slots are already default-constructed empty,
///     so Empty() is a no-op-ish; the dirty-bit flip may or may not
///     fire but no side-effects escape the AuxManufacturingIndex.</item>
///   <item><c>SendAuxManu</c> at
///     <c>server/src/PlayerClass.cpp:1301-1308</c>: emits a single
///     0x001B AUX_DATA frame via SendOpcode(ENB_OPCODE_001B_AUX_DATA,
///     ManuIndex()-&gt;PacketBuffer, ManuIndex()-&gt;PacketSize) gated
///     on BuildPacket success.</item>
/// </list>
/// <para>
/// Same favourable post-emit shape as the prior survival-probe waves:
/// handler dispatches, decodes payload, mutates a thin slice of
/// session state (here: the difficulty u32 which is already 0, and
/// the inventory-item slots which are already empty), and emits one
/// bounded reply.
/// </para>
///
/// <para>
/// Why this wave target. Wave 43 outlook noted the easy-direct-
/// stimulus seam was running thin. The manufacture-action family
/// (0x0079/0x007A/0x007B/0x007C/0x007E) had been bulk-triaged as
/// "all UNSAFE — require terminal-state setup" in earlier wave
/// notes, but a closer file-read of 0x0079 HandleManufactureTerminal
/// shows its Terminal=0 arm is the cleanest no-op in the family:
/// the inner switch all-break the four known modes, the default
/// LogMessages an unknown mode but doesn't mutate, and the post-
/// switch tail of SetDifficulty(0) + ResetManuItems + SendAuxManu
/// is fully fresh-char-safe. The "UNSAFE" bulk-triage was overly
/// pessimistic — only the Terminal=1/2/4 arms (the mode-set arms)
/// actually mutate ManuIndex state.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at PlayerConnection.cpp:519-521.</b>
///     The 0x0079 case sits between 0x005E AVATAR_EMOTE (line 515)
///     and 0x007A MANUFACTURE_ITEM_CATAGORY (line 523, which routes
///     to <c>HandleManufactureCategorySelection</c>). A copy-paste
///     swap with HandleManufactureCategorySelection on our 8B
///     payload would route Terminal=0 into the Category switch at
///     PlayerManufacturing.cpp:72-80, which case-0 SetCurrentItemCat(0)
///     and calls BuildManufactureList — for a fresh char with empty
///     m_ManuRecipes this is still safe so survival passes; but a
///     swap with HandleAvatarEmote (which routes through HandleChatStream
///     and expects an 11B ChatStream) would over-read our 8B payload
///     into stack garbage. Survival catches the crash boundary.
///   </item>
///   <item>
///     <b>PacketStructures.h ManufactureData layout regression.</b>
///     Currently 8B packed: <c>int32_t GameID; int32_t Data;</c>
///     with ATTRIB_PACKED. Widening either field to <c>long</c> on
///     Linux x86_64 would push <c>Data</c> from offset 4 to offset
///     8, and the handler would read past our 8B buffer end (UB).
///     Survival probe catches the crash.
///   </item>
///   <item>
///     <b><c>ntohl</c> revert at PlayerManufacturing.cpp:28.</b>
///     A regression dropping the ntohl on Packet-&gt;Data feeds a
///     byte-swapped Terminal into the switch. For Terminal=0 this
///     is a no-op (0 is endian-invariant), so the test still passes
///     — but a future non-zero-Terminal wave would catch the byte-
///     order regression.
///   </item>
///   <item>
///     <b><c>long</c>-widening of Terminal at
///     PlayerManufacturing.cpp:28.</b> The handler stores the ntohl
///     result in <c>long Terminal</c>; on Linux x86_64 long is 8B
///     with upper 4B uninitialised. The switch compares the lower
///     32 bits to small integer cases (0/1/2/4) so the upper bits
///     don't affect matching for the Terminal=0 path. Documented
///     for completeness.
///   </item>
///   <item>
///     <b>Terminal=0 inner-switch all-break regression at
///     PlayerManufacturing.cpp:33-46.</b> Currently all four known
///     modes (MODE_MANUFACTURE / MODE_ANALIZE / MODE_DISMANTLE /
///     MODE_REFINE) are plain <c>break</c> with no state change. A
///     refactor that turns one of them into a TerminalReset or
///     SetMode call would change the no-op semantics; the survival
///     probe wouldn't catch the semantic shift but the byte-
///     comparison wave for the 0x001B reply would.
///   </item>
///   <item>
///     <b>SetDifficulty(DIFFICULTY_AUTOMATIC) drift regression at
///     PlayerManufacturing.cpp:67.</b> The post-switch tail
///     unconditionally SetDifficulty's to DIFFICULTY_AUTOMATIC. If
///     that #define changes value or the AuxManufacturingIndex
///     dirty-bit flip semantics regress, the AUX_DATA reply might
///     fail to emit. Survival probe still passes via REQUEST_TIME
///     so this slips silently — byte-comparison wave catches.
///   </item>
///   <item>
///     <b>ResetManuItems crash regression at
///     PlayerManufacturing.cpp:68 → AuxManufacturingIndex.cpp:239.</b>
///     A refactor that replaces the explicit Empty() calls with a
///     loop indexed past the array bounds would surface as a crash.
///     Survival probe catches.
///   </item>
///   <item>
///     <b>SendAuxManu BuildPacket failure regression at
///     PlayerClass.cpp:1301.</b> The SendAuxManu wrapper gates the
///     SendOpcode on BuildPacket success; a regression making
///     BuildPacket return false on the post-Terminal-0 state would
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
/// Server-integrity note (per CLAUDE.md). 0x0079 with Terminal=0 is
/// what retail Win32 client emits when the user closes a manufacture
/// terminal in a starbase. The all-break inner switch on the
/// previous mode (followed by SetDifficulty + ResetManuItems +
/// SendAuxManu) is exactly what the retail server does — the
/// client wants a fresh state-of-the-world on next terminal entry.
/// Zero permissiveness added; not loosening any security posture;
/// not fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; 0x0079 + REQUEST_TIME round-trip is
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorManufactureTerminalTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorManufactureTerminalTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ManufactureTerminal_TerminalZeroExit_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Erman" starts with lowercase 'e' for the
        // AccountManager.cpp:1147 vowel-check footgun (case-sensitive
        // a/e/i/o/u/y BEFORE toupper at line 1153).
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Erman", shipName: "ErmanShip", cts.Token);

        try
        {
            // 0x0079 MANUFACTURE_ITEM_CATAGORY (HandleManufactureTerminal) —
            // 8B packed payload:
            //   int32_t GameID   (handler ignores; identity from connection)
            //   int32_t Data     (network byte order — Terminal=0 selects
            //                     the terminal-exit arm: inner switch
            //                     all-breaks then post-switch
            //                     SetDifficulty(0) + ResetManuItems +
            //                     SendAuxManu — no state change visible
            //                     beyond a single 0x001B AUX_DATA reply)
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);  // GameID
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), 0);     // Data=0 (network order)

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.ManufactureTerminal.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the
            // MANUFACTURE_TERMINAL handler? Send REQUEST_TIME and
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
            // to our 0x0079).
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
                $"drained {maxFrames} frames after sending 0x0079 MANUFACTURE_TERMINAL + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the dispatcher arm at PlayerConnection.cpp:519 got mis-routed " +
                $"(swap with HandleAvatarEmote → ChatStream over-reads our 8B payload), " +
                $"ATTRIB_PACKED on the ManufactureData struct at PacketStructures.h:1062 was dropped " +
                $"(Data field reads past buffer end), " +
                $"the Terminal=0 inner-switch all-break arm at PlayerManufacturing.cpp:33-46 was changed to call SetMode/TerminalReset, " +
                $"the post-switch ResetManuItems at PlayerManufacturing.cpp:68 crashed via OOB on AuxManufacturingIndex.cpp:239, " +
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
