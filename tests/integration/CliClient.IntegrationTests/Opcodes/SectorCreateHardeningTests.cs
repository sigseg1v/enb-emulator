// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 84 hardening test (+0 ratchet, 0x0004): pins the byte-exact
/// 23-byte payload shape of every 0x0004 CREATE frame the server
/// emits during a station-arm sector-login handshake.
///
/// <para>
/// Backstory. 0x0004 CREATE is server-emitted by
/// <c>Player::SendCreate</c> at
/// <c>server/src/PlayerConnection.cpp:1505-1529</c>. The handler
/// builds a <c>Create</c> local from its six scalar arguments and
/// emits via
/// <c>SendOpcode(ENB_OPCODE_0004_CREATE, &amp;create, sizeof(create))</c>.
/// The <c>Create</c> struct is defined at
/// <c>common/include/net7/PacketStructures.h:366-373</c>:
/// </para>
/// <code>
/// struct Create
/// {
///     int32_t GameID;     // 4 bytes
///     float   Scale;      // 4 bytes
///     short   BaseAsset;  // 2 bytes
///     char    Type;       // 1 byte
///     float   HSV[3];     // 12 bytes
/// } ATTRIB_PACKED;
/// </code>
/// <para>
/// Total = 4 + 4 + 2 + 1 + 12 = 23 bytes. ATTRIB_PACKED collapses
/// the natural-alignment hole that would otherwise sit between
/// <c>Type</c> (offset 22) and <c>HSV[0]</c> (would otherwise be
/// offset 24 with 1 byte of padding) — that padding byte is exactly
/// what the retail Win32 client's CREATE decoder was NOT compiled
/// to skip, so the packed 23-byte body is the wire-faithful shape.
/// </para>
///
/// <para>
/// Emission sites in the station-arm handshake. The captured
/// handshake stream shows 0x0004 emitted at least twice during a
/// StationLogin2 fan-out:
/// </para>
/// <list type="number">
///   <item>
///     <b>Player's own create.</b> The <c>SendLoginShipData</c>
///     chain in <c>server/src/PlayerClass.cpp:857-901</c> fans the
///     newly-arrived player's ship out to every tracked observer —
///     each observer's <c>SendCreate(player_game_id, scale, asset,
///     type, h, s, v)</c> emits one 0x0004 frame to the new player.
///   </item>
///   <item>
///     <b>Manufacturing-lab pseudo-object.</b>
///     <c>SectorManager::StationLogin</c> at
///     <c>server/src/SectorManager.cpp:467-526</c> creates a
///     manufacturing-lab pseudo-object specifically for the
///     station-arm handshake via
///     <c>player-&gt;SendCreate(ManuID, ..., CREATE_MANU_LAB)</c> —
///     this is what gives the in-station UI its interactable manu
///     terminal target. Wave 35's TestedOpcodes commentary calls
///     out the two-emit invariant explicitly:
///     <c>"0x0004 emitted twice per handshake (once for the player's
///     own create, once for a fan-out create on another tracked
///     object)"</c>. Both ride the same
///     <c>Player::SendCreate</c> serialiser, so both emit the
///     identical 23-byte body.
///   </item>
/// </list>
///
/// <para>
/// Why a hardening test, not a +1 ratchet wave. 0x0004 is already
/// counted by Wave 35
/// (<see cref="SectorHandshakeFanoutTests.HandshakeEmitsFullSendLoginShipDataFanout"/>)
/// which asserts presence-only via
/// <c>Assert.Contains(OpcodeId.Known.Create.Value, session.HandshakeOpcodes)</c>.
/// Wave 84 tightens that to byte-exact: every captured 0x0004 frame
/// must carry exactly 23 bytes of payload. +0 ratchet because 0x0004
/// is already in TestedOpcodes; depth coverage of a regression class
/// the presence-only assertion is structurally blind to. Mirrors the
/// Wave 67/71/76/77/78/79/80/81/82/83 pattern (byte-exact tightenings
/// on already-counted handshake emits). Eleventh hardening-pattern
/// wave.
/// </para>
///
/// <para>
/// Multi-emit invariant. Unlike Wave 83's terminator-only 0x0005
/// START (asserted via <c>Assert.Single</c>), 0x0004 CREATE is
/// emitted multiple times per handshake. Wave 84 uses
/// <c>Assert.NotEmpty</c> (proves at least one 0x0004 frame was
/// captured — same regression net as Wave 35's
/// <c>Assert.Contains</c>) + <c>Assert.All</c> (every captured 0x0004
/// frame carries exactly 23 bytes — the byte-exact tightening). The
/// per-frame loop catches a Create struct layout drift even on the
/// fan-out emit path that the player's own emit wouldn't surface
/// (e.g. if the manu-lab pseudo-object somehow took a different
/// serialiser code path, <c>Assert.All</c> still asserts on it).
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Create struct layout drift in
///     <c>common/include/net7/PacketStructures.h:366-373</c>.</b>
///     A revert of <c>int32_t GameID</c> back to <c>long GameID</c>
///     adds 4 bytes on LP64 Linux (27-byte body); a revert of
///     <c>short BaseAsset</c> back to <c>int BaseAsset</c> adds 2
///     bytes (25-byte body); a revert of <c>char Type</c> back to
///     <c>int Type</c> adds 3 bytes (26-byte body); a revert of
///     <c>float HSV[3]</c> to <c>double HSV[3]</c> adds 12 bytes
///     (35-byte body). The byte-exact 23-byte assertion catches
///     every one.
///   </item>
///   <item>
///     <b>ATTRIB_PACKED removal at PacketStructures.h:373.</b>
///     Without the packing attribute, the natural-alignment hole
///     between <c>Type</c> (offset 22) and <c>HSV[0]</c> (aligned
///     to offset 24) inserts 1 padding byte, growing
///     <c>sizeof(Create)</c> to 24. The retail Win32 client's
///     CREATE decoder reads exactly 23 bytes; the extra padding
///     byte either truncates the next opcode in the
///     0x2016 PACKET_SEQUENCE parser or pushes garbage into the
///     subsequent emit's GameID field. 23-byte assertion catches
///     this immediately.
///   </item>
///   <item>
///     <b>SendCreate <c>sizeof(create)</c> literal-replacement.</b>
///     A regression at <c>PlayerConnection.cpp:1528</c> that hard-codes
///     a constant length (e.g. <c>SendOpcode(..., &amp;create, 22)</c>
///     to "match the retail length" without accounting for ATTRIB_PACKED)
///     would emit the wrong size and either truncate or under-fill.
///   </item>
///   <item>
///     <b>SendCreate removal from the SendLoginShipData chain.</b>
///     If <c>SendCreate</c> drops out of the per-observer fan-out
///     loop, the captured frame count drops below the Wave 35
///     invariant. <c>Assert.NotEmpty</c> fires (zero captured 0x0004
///     frames) — same regression net as Wave 35's
///     <c>Assert.Contains</c>, but Wave 84 keeps it co-located with
///     the byte-exact assertion so a future Wave 35 refactor doesn't
///     accidentally weaken the presence guarantee.
///   </item>
///   <item>
///     <b>StationLogin SendCreate(ManuID, CREATE_MANU_LAB) removal at
///     <c>SectorManager.cpp:478</c>.</b> The manu-lab pseudo-object
///     is the station-arm-specific second emit; removing it drops
///     the captured count from two to one but Wave 35's
///     <c>Assert.Contains</c> would still pass. Wave 84's
///     <c>Assert.All</c> wouldn't catch this directly (every
///     remaining frame is still 23 bytes), but the captured-frame
///     count remains observable in test output — a future +1 wave
///     could pin the count via <c>Assert.Equal(2, createFrames.Count)</c>.
///     Documented here as the natural follow-up.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every inner
///     opcode in the 0x2016 PACKET_SEQUENCE parser; 0x0004 wouldn't
///     appear under its correct label at all — <c>Assert.NotEmpty</c>
///     fires.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x0004 (0x0004 &lt; 0x0FFF). A regression to a tighter
///     upper bound that excluded 0x0004 would silently drop the
///     frame from the wire — <c>Assert.NotEmpty</c> fires.
///   </item>
///   <item>
///     <b>DoSectorLoginUntilStartAsync drain-loop payload-length
///     capture regression
///     (<c>Opcodes/SectorHandshake.cs:416</c>).</b> The capture path
///     records <c>(reply.Header.Opcode, reply.Payload.Length)</c> for
///     every inbound frame. A future refactor that drops the length
///     field or under-counts payload bytes specifically for 0x0004
///     would observe the wrong length on every captured frame.
///   </item>
/// </list>
///
/// <para>
/// Primary source citation (CLAUDE.md server-integrity rule). 0x0004
/// CREATE is server-originated. Wave 84 adds no client stimulus and
/// no server change — pure passive-observation tightening of a
/// retail-faithful wire shape. The 23-byte body is exactly what the
/// retail Win32 client's CREATE decoder was compiled to receive (the
/// Win32 ATTRIB_PACKED footprint with <c>sizeof(long) == 4</c>,
/// <c>sizeof(short) == 2</c>, <c>sizeof(char) == 1</c>,
/// <c>sizeof(float) == 4</c> — all of which match the LP64 Linux
/// sizes for these specific types). No widened input acceptance, no
/// loosened gating, no fabricated replies — server-integrity
/// POSITIVE.
/// </para>
///
/// <para>
/// Budget: 60s. Single-stage station handshake into Luna Station
/// (10151) ~2s; assertions run synchronously against already-captured
/// state. No additional client stimulus.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorCreateHardeningTests
{
    /// <summary>
    /// <c>sizeof(Create) = 4 + 4 + 2 + 1 + 12 = 23</c>. Matches the
    /// wire size computed by <c>sizeof(create)</c> at
    /// <c>server/src/PlayerConnection.cpp:1528</c> against the
    /// ATTRIB_PACKED struct definition at
    /// <c>common/include/net7/PacketStructures.h:366-373</c>. The
    /// retail Win32 client was compiled with <c>sizeof(long) == 4</c>
    /// AND <c>sizeof(short) == 2</c> AND <c>sizeof(char) == 1</c>
    /// AND <c>sizeof(float) == 4</c> — every field width matches
    /// LP64 Linux for these types, so the only LP64 divergence vector
    /// is a future <c>int32_t→long</c> revert on <c>GameID</c>. The
    /// ATTRIB_PACKED attribute is load-bearing: without it the
    /// natural-alignment hole between <c>char Type</c> (offset 22)
    /// and <c>float HSV[0]</c> (would align to offset 24) inserts a
    /// padding byte and grows <c>sizeof(Create)</c> to 24.
    /// </summary>
    private const int ExpectedCreatePayloadLength = 23;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorCreateHardeningTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task Create_EmittedDuringStationSectorHandshake_HasExactly23BytePayload()
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
            firstName: "Create84", shipName: "Create84Ship", cts.Token);

        var createFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.Create.Value)
            .ToList();

        Assert.NotEmpty(createFrames);
        Assert.All(createFrames, f =>
            Assert.Equal(ExpectedCreatePayloadLength, f.PayloadLength));
    }
}
