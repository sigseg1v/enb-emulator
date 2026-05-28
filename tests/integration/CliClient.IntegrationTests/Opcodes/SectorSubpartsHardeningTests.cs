// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 90 hardening test (+0 ratchet, 0x00B4): pins the byte-exact
/// 54-byte payload shape of every 0x00B4 SUBPARTS frame the server
/// emits during a Terran-Warrior station-sector login handshake stream.
///
/// <para>
/// Backstory. 0x00B4 is server-emitted by
/// <c>Player::SendSubparts</c> at
/// <c>server/src/PlayerClass.cpp:988-1043</c>. The handler builds a
/// per-ship subpart manifest into a 128-byte stack buffer using the
/// <c>AddData&lt;T&gt;</c> / <c>AddDataS</c> helpers from
/// <c>server/src/PacketMethods.h</c>, then ships exactly <c>index</c>
/// bytes via <c>SendOpcode(ENB_OPCODE_00B4_SUBPARTS, subparts, index)</c>.
/// Unlike the fixed-shape opcodes hardened by Waves 67/71/76-89, the
/// SUBPARTS payload is composed dynamically from <c>m_Database.ship_info</c>
/// (profession, wing, engine) and branches on <c>ship_data.race</c>. For
/// the Terran-Warrior race/profession pair that every test account in
/// <see cref="TestAccounts"/> spawns as (see
/// <c>SectorHandshake.CreateCharacterOnSlotAsync</c> at
/// <c>tests/integration/CliClient.IntegrationTests/Opcodes/SectorHandshake.cs:258-260</c>
/// where <c>race: 0 (RACE_TERRAN)</c> and <c>profession: 0 (Warrior)</c>
/// are hardcoded), the handler walks the <b>default</b> race-switch arm
/// (PlayerClass.cpp:1016-1038), <b>skips</b> the Trader/Terran/upgrade≥5
/// branch (PlayerClass.cpp:1026-1037), and produces a deterministic
/// payload of exactly <b>54 bytes</b>:
/// <code>
///   AddData(subparts, ntohl(GameID()), index);                  // 0..4
///   AddData(subparts, ntohl(4), index);                         // 4..8
///   AddDataS(subparts, "~01", index);                           // 8..11
///   index++;                                                    // 11..12
///   AddData(subparts, ntohl(profession), index);                // 12..16
///   // switch case default (race=0=RACE_TERRAN):
///   AddDataS(subparts, "~02", index);                           // 16..19
///   index++;                                                    // 19..20
///   AddData(subparts, ntohl(wing), index);                      // 20..24
///   AddDataS(subparts, "~02/~03_01", index);                    // 24..34
///   index++;                                                    // 34..35
///   AddData(subparts, ntohl(engine), index);                    // 35..39
///   AddDataS(subparts, "~02/~03_02", index);                    // 39..49
///   index++;                                                    // 49..50
///   AddData(subparts, ntohl(engine), index);                    // 50..54
///   // not Trader/Terran/upgrade≥5 → skip the +20B branch
/// </code>
/// </para>
///
/// <para>
/// Primary source citation (CLAUDE.md server-integrity rule). The
/// retail wire shape for SUBPARTS is determined by the
/// <c>AddData&lt;long&gt;</c> 4-byte-LE specialization at
/// <c>server/src/PacketMethods.h:37-49</c> (Phase K Wave 12 comment
/// inline): every <c>AddData(subparts, ntohl(int_val), index)</c> call
/// emits exactly 4 wire bytes regardless of the host's
/// <c>sizeof(long)</c>. Combined with the Phase K Wave 12 inline-write
/// at <c>server/src/PlayerClass.cpp:1030</c>
/// (<c>*((int32_t*) &amp;subparts[4]) = ntohl(6)</c>), this is the
/// project's own primary-source attestation that wire offsets 0/4/12/20/35/50
/// are 4-byte slots and the "~01"/"~02"/"~02/~03_01"/"~02/~03_02"
/// string literals are emitted verbatim (no length prefix, no null
/// terminator — <c>AddDataS</c> is a bare <c>memcpy(strlen)</c>). The
/// retail Win32 client's SUBPARTS parser is built against the same wire
/// layout the AddData* helpers serialise to; reverting any field-width
/// specialization would misalign the parser by exactly the slot the
/// revert widened. The deterministic 54-byte length encodes all three
/// invariants (Wave 12 long→int32_t specialization, ATTRIB_PACKED-free
/// hand-rolled buffer layout, race-switch default arm selection) in a
/// single byte-exact assertion.
/// </para>
///
/// <para>
/// Why a hardening test, not a +1 ratchet wave. 0x00B4 is already
/// counted by Wave 35
/// (<see cref="SectorHandshakeFanoutTests.HandshakeEmitsFullSendLoginShipDataFanout"/>) —
/// the passive-observation <c>Assert.Contains</c> that opcode 0x00B4
/// appears in the captured handshake stream. Wave 35's assertion is
/// opcode-presence only; it would still pass if a single
/// <c>AddData&lt;long&gt;</c> specialization were reverted (the
/// individual emit would silently widen from 4B to 8B on Linux,
/// shifting every downstream wire slot — but the opcode would still
/// fire). Wave 90 adds the byte-exact 54-byte payload-length assertion
/// the presence-only check cannot make, locking the per-field wire
/// widths and the race-switch dispatch in place. +0 ratchet because
/// 0x00B4 is already counted; depth coverage of a regression class
/// Wave 35 was structurally blind to.
/// </para>
///
/// <para>
/// Pattern lineage. SEVENTEENTH hardening-pattern wave (Waves 67/71/76/77/
/// 78/79/80/81/82/83/84/85/86/87/88/89 → 90). FIRST framing-audit
/// hardening — pinning the byte length of a hand-rolled variable-shape
/// emit rather than a fixed-shape <c>sizeof(struct)</c> emit, which
/// closes the long-pending 0x00B4 framing-audit carryover task. Wave 90
/// stays on the station-sector arm (Luna Station, sector 10151) — same
/// 1-stage path Wave 79 uses for the player-self
/// <c>Player::SendRelationship</c> hardening.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b><c>AddData&lt;long&gt;</c> specialization revert at
///     <c>server/src/PacketMethods.h:37-42</c>.</b> Phase K Wave 12
///     forces 4-byte LE emission for long values; deleting the
///     specialization makes the generic template
///     (PacketMethods.h:23-28) fire instead and the host's
///     <c>sizeof(long)</c> determines the slot width (8B on Linux). Each
///     of the four <c>AddData(subparts, ntohl(...), index)</c> calls in
///     the default arm at PlayerClass.cpp:996/1000/1019/1022/1025 would
///     write 8 wire bytes instead of 4, growing the payload from 54B to
///     86B. The byte-exact 54B assertion fires.
///   </item>
///   <item>
///     <b><c>AddData&lt;unsigned long&gt;</c> specialization revert at
///     <c>server/src/PacketMethods.h:44-49</c>.</b> Same regression
///     class as <c>long</c> — the <c>ntohl(...)</c> return type is
///     <c>uint32_t</c> on Linux but a long literal in some refactors,
///     and a missing unsigned-long specialization would re-introduce
///     the LP64 inflation. Caught by the same 54B byte-exact check.
///   </item>
///   <item>
///     <b>Race-switch dispatch revert at
///     <c>server/src/PlayerClass.cpp:1002-1014</c>.</b> If the
///     <c>RACE_JENQUAI</c> case label were widened to include Terran
///     (e.g. <c>case RACE_TERRAN</c> falling through), the Jenquai arm
///     would fire instead. Jenquai arm emits: 3 + 1 + 4 + 11 + 1 + 4 +
///     3 + 1 + 4 = 32 bytes after the common 16B prefix = 48B total.
///     Different byte count → byte-exact 54B check fires.
///   </item>
///   <item>
///     <b>Trader/Terran/upgrade≥5 fallthrough at
///     <c>server/src/PlayerClass.cpp:1026-1037</c>.</b> If the
///     <c>Profession() == PROFESSION_TRADER &amp;&amp; Race() ==
///     RACE_TERRAN &amp;&amp; hull_upgrade ≥ 5</c> guard were inverted
///     (e.g. flipped to <c>!=</c> or dropped the upgrade check), the
///     inner branch would fire for our Warrior account and add an extra
///     ~20B of "~02/~03_03"/"~02/~03_04" + 2× engine emits, plus the
///     inline-write at offset 4. Payload grows past 54B. Caught.
///   </item>
///   <item>
///     <b>Phase K Wave 12 inline-write reintroduction at
///     <c>server/src/PlayerClass.cpp:1030</c>.</b> The
///     <c>*((int32_t*) &amp;subparts[4]) = ntohl(6)</c> is conditional
///     on the Trader/Terran/upgrade≥5 branch and currently a no-op for
///     our Warrior account. A regression that unconditionally fires the
///     overwrite would still produce 54B but would corrupt slot[4..8]
///     from the AddData-emitted <c>ntohl(4)</c> sentinel to
///     <c>ntohl(6)</c>. The byte-exact length check alone doesn't catch
///     this content regression, but the
///     <c>Assert.NotEmpty</c>-then-<c>Assert.All</c> pattern is
///     compatible with a future Wave that adds slot-content assertions
///     on top of length.
///   </item>
///   <item>
///     <b><c>AddDataS</c> length-revert at
///     <c>server/src/PacketMethods.h:52-56</c>.</b> The helper is a
///     bare <c>memcpy</c> + <c>index += strlen</c>. If a refactor
///     accidentally swapped in a null-terminated or length-prefixed
///     variant, the four string emits ("~01" / "~02" / "~02/~03_01" /
///     "~02/~03_02") would each grow by 1B (null terminator) or 2B
///     (length prefix), shifting the payload from 54B to 58-62B.
///     Caught.
///   </item>
///   <item>
///     <b><c>SendOpcode</c> header-width revert at
///     <c>server/src/PlayerConnection.cpp:127</c>.</b> Would corrupt
///     every inner opcode in the 0x2016 PACKET_SEQUENCE parser; 0x00B4
///     wouldn't appear under its correct label at all (so the
///     <c>Assert.NotEmpty</c> filter catches it before the length check
///     fires).
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x00B4 (0x00B4 &lt; 0x0FFF). A regression to a tighter
///     upper bound would silently drop 0x00B4 from the wire — the
///     captured-frame filter returns empty and the
///     <c>Assert.NotEmpty</c> check fires.
///   </item>
///   <item>
///     <b><c>SendLoginShipData</c> dispatch chain regression at
///     <c>server/src/PlayerClass.cpp:871</c>.</b> The
///     <c>SendSubparts(this)</c> call is unconditional; a regression
///     gating it (e.g. a missing-data short-circuit) would silently
///     drop 0x00B4 from the handshake stream and <c>Assert.NotEmpty</c>
///     catches.
///   </item>
///   <item>
///     <b>DoSectorLoginUntilStartAsync drain-loop payload-length
///     capture regression
///     (<c>tests/integration/CliClient.IntegrationTests/Opcodes/SectorHandshake.cs:127-138</c>).</b>
///     The Wave 68 harness addition that populates
///     <see cref="SectorHandshake.Session.HandshakeFrames"/> with
///     payload-length info on the <see cref="SectorHandshake.EstablishAsync"/>
///     path. If a future refactor drops the length field or
///     under-counts payload bytes, this test observes wrong (or zero)
///     lengths for every captured 0x00B4 frame.
///   </item>
/// </list>
///
/// <para>
/// Budget: 60s. Single-stage station handshake ~2s; assertions run
/// synchronously against already-captured state. No additional client
/// stimulus. SUBPARTS is server-originated. Wave 90 adds no client
/// stimulus and makes no server change — pure passive-observation
/// tightening of the captured handshake stream.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorSubpartsHardeningTests
{
    /// <summary>
    /// 4 (GameID) + 4 (literal 4) + 3 ("~01") + 1 (raw byte) + 4 (profession)
    /// + 3 ("~02") + 1 (raw byte) + 4 (wing) + 10 ("~02/~03_01") + 1 (raw byte)
    /// + 4 (engine) + 10 ("~02/~03_02") + 1 (raw byte) + 4 (engine) = 54.
    /// Default race-switch arm at <c>server/src/PlayerClass.cpp:1016-1038</c>
    /// for race=0 (RACE_TERRAN); the Trader/Terran/upgrade≥5 branch at
    /// <c>server/src/PlayerClass.cpp:1026-1037</c> does NOT fire for our
    /// Warrior account (profession=0).
    /// </summary>
    private const int ExpectedSubpartsPayloadLength = 54;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorSubpartsHardeningTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task Subparts_EmittedDuringStationSectorHandshake_HasExactly54BytePayloadForTerranWarrior()
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
            firstName: "Sub90Pin", shipName: "Sub90PinShip", cts.Token);

        var subpartsFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.Subparts.Value)
            .ToList();

        Assert.NotEmpty(subpartsFrames);
        Assert.All(subpartsFrames, f =>
            Assert.Equal(ExpectedSubpartsPayloadLength, f.PayloadLength));
    }
}
