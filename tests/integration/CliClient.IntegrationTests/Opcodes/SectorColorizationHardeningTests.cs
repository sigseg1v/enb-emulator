// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 76 hardening test (+0 ratchet, 0x0011): pins the byte-exact
/// 134-byte payload shape of every 0x0011 COLORIZATION frame the
/// server emits during the station-sector login handshake stream.
///
/// <para>
/// Backstory. 0x0011 is server-emitted by
/// <c>Player::SendShipColorization</c> at
/// <c>server/src/PlayerClass.cpp:1310-1365</c>. The call site at
/// <c>PlayerClass.cpp:872</c> (and the matching peer-broadcast
/// variant at <c>PlayerClass.cpp:921</c>) always passes
/// <c>count = 8</c> for the "send the ship color scheme" emit during
/// SendLoginShipData. The wire payload size is computed via
/// <c>size = ((char *) &amp;colorization.item[count]) - ((char *) &amp;colorization)</c>
/// at <c>PlayerClass.cpp:1361</c>, which under
/// <c>ATTRIB_PACKED</c> (zero structure padding) evaluates to
/// <c>4 (GameID) + 2 (ItemCount) + 8 × 16 (ColorizationItem) = 134</c>
/// bytes. The retail Win32 client was compiled to receive exactly this
/// 134-byte body — the ship's 8-slot color palette (hull primary +
/// secondary, profession primary + secondary, wing primary +
/// secondary, engine primary + secondary).
/// </para>
///
/// <para>
/// Why a hardening test, not a +1 ratchet wave. 0x0011 is already
/// counted by Wave 51
/// (<see cref="SectorHandshakeFanoutTests.HandshakeEmitsFullSendLoginShipDataFanout"/>) —
/// the passive-observation assertion that the opcode appears in the
/// handshake stream during a station-sector login. Wave 51's assertion
/// is opcode-presence only; it would still pass if the
/// <c>Colorization</c> struct layout drifted (e.g. a field-type
/// widening that inflated <c>ItemCount</c> from <c>short</c> to
/// <c>int32</c> would add 2 bytes per frame and silently diverge from
/// the retail wire shape the client decodes). Wave 76 adds the
/// byte-exact 134-byte payload-length assertion the presence-only
/// check cannot make, locking the wire shape in place. +0 ratchet
/// because 0x0011 is already counted; depth coverage of a regression
/// class Wave 51 was structurally blind to. Mirrors the Wave 67/71
/// pattern (byte-exact tightenings on already-counted handshake
/// emits).
/// </para>
///
/// <para>
/// Wire shape and dispatch path. <c>SectorManager::HandleSectorLogin</c>
/// at <c>server/src/SectorManager.cpp:324-336</c> branches on
/// <c>m_SectorID</c> — sector IDs &gt; 9999 are stations (route to
/// <c>StationLogin</c> → <c>StationLogin2</c>), which calls
/// <c>SendLoginShipData</c> (<c>PlayerClass.cpp:855</c>). That function
/// dispatches the per-ship fanout chain
/// (<c>SendCreate</c> → <c>SendSubparts</c> →
/// <c>SendShipColorization(this, 8)</c> → <c>SendOpcode(0x0037)</c> →
/// <c>SendOpcode(0x0047)</c>). The station handshake into Luna Station
/// (10151) is the same 1-stage path Wave 51 exercises; Wave 76 reuses
/// it without modification — same account pool, same firstName /
/// shipName payload, same drain loop — and just adds the byte-exact
/// length assertion on the captured 0x0011 frames.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Colorization struct layout regression in
///     <c>common/include/net7/PacketStructures.h:477-484</c>.</b>
///     <c>int32_t GameID; short ItemCount; ColorizationItem item[10]</c>
///     with <c>ATTRIB_PACKED</c>. A regression widening <c>ItemCount</c>
///     to <c>int32_t</c> would add 2 bytes per frame (136 vs 134). A
///     regression on <c>ColorizationItem.metal</c> (currently
///     <c>int32_t</c>) back to <c>long</c> would inflate each item by 4
///     bytes on LP64 Linux (8 × 4 = 32 extra bytes per frame, total
///     166).
///   </item>
///   <item>
///     <b>SendShipColorization size-calc regression at
///     <c>PlayerClass.cpp:1361</c>.</b> The pointer-arithmetic
///     <c>((char *) &amp;colorization.item[count]) - ((char *) &amp;colorization)</c>
///     is the load-bearing wire-size computation. A regression to
///     <c>sizeof(Colorization)</c> would emit the full 10-item buffer
///     (4 + 2 + 10 × 16 = 166 bytes), trailing 32 bytes of uninitialised
///     stack into the wire. A regression to <c>count * sizeof(item[0])</c>
///     would emit only the items (128 bytes), losing the GameID and
///     ItemCount header.
///   </item>
///   <item>
///     <b>SendShipColorization count-argument regression at
///     <c>PlayerClass.cpp:872</c> and <c>PlayerClass.cpp:921</c>.</b>
///     Both call sites pass <c>count = 8</c>. A regression to a
///     different count (e.g. matching the
///     <c>MAX_COLORIZATION_ITEMS = 10</c> ceiling) would change the
///     wire-payload length per frame.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every inner
///     opcode in the 0x2016 PACKET_SEQUENCE parser; 0x0011 wouldn't
///     appear under its correct label at all (so the
///     <c>Assert.NotEmpty</c> filter catches it before the length
///     check fires).
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x0011 (0x0011 &lt; 0x0FFF). A regression to a tighter
///     upper bound would silently drop 0x0011 from the wire — the
///     captured-frame filter returns empty and the
///     <c>Assert.NotEmpty</c> check fires.
///   </item>
///   <item>
///     <b>SendLoginShipData fanout chain truncation at
///     <c>PlayerClass.cpp:872</c>.</b> A regression that removes the
///     <c>SendShipColorization(this, 8)</c> call from the
///     SendLoginShipData chain (or moves it past the chain terminator)
///     would drop 0x0011 from the handshake stream entirely.
///   </item>
///   <item>
///     <b>DoSectorLoginUntilStartAsync drain-loop payload-length
///     capture regression
///     (<c>tests/integration/CliClient.IntegrationTests/Opcodes/SectorHandshake.cs</c>).</b>
///     The HandshakeFrames capture path populated by the drain loop
///     records the payload-length of every inbound frame. If a future
///     refactor drops the length field or under-counts payload bytes,
///     this test observes wrong (or zero) lengths for every captured
///     0x0011 frame.
///   </item>
/// </list>
///
/// <para>
/// Primary source citation (CLAUDE.md server-integrity rule). 0x0011
/// COLORIZATION is server-originated. Wave 76 adds no client stimulus
/// and no server change — pure passive-observation tightening of a
/// retail-faithful wire shape. The 134-byte body is exactly what the
/// retail Win32 client's COLORIZATION decoder was compiled to receive;
/// any drift breaks the client. No widened input acceptance, no
/// loosened gating, no fabricated replies — server-integrity POSITIVE.
/// </para>
///
/// <para>
/// Budget: 60s. Single-stage station handshake into Luna Station
/// (10151) ~2s; assertions run synchronously against already-captured
/// state. No additional client stimulus.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorColorizationHardeningTests
{
    /// <summary>
    /// 4 (int32 GameID) + 2 (short ItemCount) + 8 × 16 bytes (8 ×
    /// (int32 metal + 3 × float HSV)) = 134. Matches the wire size
    /// computed by <c>SendShipColorization</c> with count=8 under
    /// <c>ATTRIB_PACKED</c> (zero structure padding).
    /// </summary>
    private const int ExpectedColorizationPayloadLength = 134;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorColorizationHardeningTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task Colorization_EmittedDuringStationSectorHandshake_HasExactly134BytePayload()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int stationSectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "ClrPin76", shipName: "ClrPin76Ship", cts.Token);

        var colorizationFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.Colorization.Value)
            .ToList();

        Assert.NotEmpty(colorizationFrames);
        Assert.All(colorizationFrames, f =>
            Assert.Equal(ExpectedColorizationPayloadLength, f.PayloadLength));
    }
}
