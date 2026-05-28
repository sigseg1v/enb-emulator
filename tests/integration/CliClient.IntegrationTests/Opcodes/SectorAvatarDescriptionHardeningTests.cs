// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 78 hardening test (+0 ratchet, 0x0061): pins the byte-exact
/// 260-byte payload shape of every 0x0061 AVATAR_DESCRIPTION frame
/// the server emits during the station-sector login handshake stream.
///
/// <para>
/// Backstory. 0x0061 AVATAR_DESCRIPTION is server-emitted by
/// <c>Player::SendLoginShipData</c> at
/// <c>server/src/PlayerClass.cpp:889</c>:
/// <c>SendOpcode(ENB_OPCODE_0061_AVATAR_DESCRIPTION, &amp;avatar, sizeof(avatar))</c>
/// — a zero-initialised <c>AvatarDescription</c> struct populated with
/// <c>AvatarID = GameID()</c>, a memcpy of the persistent
/// <c>m_Database.avatar</c> blob, and <c>unknown3 = 1.0, unknown4 = 1.0</c>
/// (PlayerClass.cpp:882-887). The wire size is computed via
/// <c>sizeof(avatar)</c> against the <c>ATTRIB_PACKED</c> struct
/// (PacketStructures.h:490-498) which under packed layout evaluates to
/// <c>4 (uint32_t AvatarID) + 241 (AvatarData) + 4 (int32_t unknown1) +
/// 3 (u8 unknown2[3]) + 4 (float unknown3) + 4 (float unknown4) = 260</c>
/// bytes. The retail Win32 client was compiled to receive exactly this
/// 260-byte body.
/// </para>
///
/// <para>
/// Why a hardening test, not a +1 ratchet wave. 0x0061 is already
/// counted by Wave 34
/// (<see cref="SectorHandshakeFanoutTests.HandshakeEmitsClientShipAndAvatarDescription"/>) —
/// the passive-observation assertion that the opcode appears in the
/// handshake stream during a station-sector login. Wave 34's assertion
/// is opcode-presence only; it would still pass if the
/// <c>AvatarDescription</c> or embedded <c>AvatarData</c> struct
/// layouts drifted (e.g. an <c>AvatarID uint32 → uint64</c> widening
/// would add 4 bytes per frame, or any of the <c>AvatarData</c>
/// fields' Phase K int32-narrowing reverting to <c>long</c> would
/// inflate the embedded blob by 4 bytes per field on LP64 Linux).
/// Wave 78 adds the byte-exact 260-byte payload-length assertion the
/// presence-only check cannot make, locking the wire shape in place.
/// +0 ratchet because 0x0061 is already counted; depth coverage of a
/// regression class Wave 34 was structurally blind to. Mirrors the
/// Wave 67/71/76/77 pattern (byte-exact tightenings on already-counted
/// handshake emits).
/// </para>
///
/// <para>
/// Wire shape and dispatch path. <c>SectorManager::HandleSectorLogin</c>
/// at <c>server/src/SectorManager.cpp:324-336</c> branches on
/// <c>m_SectorID</c> — sector IDs &gt; 9999 are stations (route to
/// <c>StationLogin</c> → <c>StationLogin2</c>), which calls
/// <c>SendLoginShipData</c> (<c>PlayerClass.cpp:855</c>). That function
/// dispatches the per-ship fanout chain, and after the SendCreate /
/// SendSubparts / SendShipColorization / SendOpcode(0x0037) /
/// SendOpcode(0x0047) prologue, emits the 0x0061 AVATAR_DESCRIPTION
/// from a stack-local zero-initialised struct (PlayerClass.cpp:882-889).
/// The station handshake into Luna Station (10151) is the same 1-stage
/// path Wave 34 exercises; Wave 78 reuses it without modification —
/// same account pool, same firstName / shipName payload, same drain
/// loop — and just adds the byte-exact length assertion on the captured
/// 0x0061 frames.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>AvatarDescription struct layout regression in
///     <c>common/include/net7/PacketStructures.h:490-498</c>.</b>
///     <c>uint32_t AvatarID; AvatarData avatar_data; int32_t unknown1;
///     u8 unknown2[3]; float unknown3; float unknown4</c> with
///     <c>ATTRIB_PACKED</c>. A regression widening <c>AvatarID</c> to
///     <c>uint64_t</c> would add 4 bytes per frame (264 vs 260). A
///     regression on <c>unknown1</c> from <c>int32_t</c> back to
///     <c>long</c> would inflate the frame by 4 bytes on LP64 Linux
///     (264 bytes).
///   </item>
///   <item>
///     <b>Embedded AvatarData struct layout regression in
///     <c>common/include/net7/PacketStructures.h:95-152</c>.</b>
///     AvatarData is 241 bytes per the inline comment. A regression
///     on any of the 9 Phase-K-narrowed int32_t fields (avatar_type,
///     race, profession, gender, mood_type, shirt_primary_metal,
///     shirt_secondary_metal, pants_primary_metal, pants_secondary_metal)
///     back to <c>long</c> would inflate AvatarData by 4 bytes per
///     field on LP64 Linux (up to +36 bytes), changing the embedded
///     blob size and thus the parent AvatarDescription frame size.
///   </item>
///   <item>
///     <b>SendLoginShipData sizeof(avatar) replacement at
///     <c>PlayerClass.cpp:889</c>.</b> A regression to a literal
///     constant (260) that drifts from the actual struct size, or to
///     <c>sizeof(AvatarData)</c> (241 — drops 19 bytes of trailer),
///     would emit a wrongly-sized frame the client decodes onto a
///     misaligned buffer.
///   </item>
///   <item>
///     <b>SendLoginShipData fanout chain truncation at
///     <c>PlayerClass.cpp:889</c>.</b> A regression that removes the
///     <c>SendOpcode(ENB_OPCODE_0061_AVATAR_DESCRIPTION, ...)</c> call
///     from the SendLoginShipData chain (or moves it past the chain
///     terminator) would drop 0x0061 from the handshake stream
///     entirely.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every inner
///     opcode in the 0x2016 PACKET_SEQUENCE parser; 0x0061 wouldn't
///     appear under its correct label at all (so the
///     <c>Assert.NotEmpty</c> filter catches it before the length
///     check fires).
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x0061 (0x0061 &lt; 0x0FFF). A regression to a tighter
///     upper bound would silently drop 0x0061 from the wire — the
///     captured-frame filter returns empty and the
///     <c>Assert.NotEmpty</c> check fires.
///   </item>
///   <item>
///     <b>DoSectorLoginUntilStartAsync drain-loop payload-length
///     capture regression
///     (<c>tests/integration/CliClient.IntegrationTests/Opcodes/SectorHandshake.cs</c>).</b>
///     The HandshakeFrames capture path populated by the drain loop
///     records the payload-length of every inbound frame. If a future
///     refactor drops the length field or under-counts payload bytes,
///     this test observes wrong (or zero) lengths for every captured
///     0x0061 frame.
///   </item>
/// </list>
///
/// <para>
/// Primary source citation (CLAUDE.md server-integrity rule). 0x0061
/// AVATAR_DESCRIPTION is server-originated. Wave 78 adds no client
/// stimulus and no server change — pure passive-observation tightening
/// of a retail-faithful wire shape. The 260-byte body is exactly what
/// the retail Win32 client's AVATAR_DESCRIPTION decoder was compiled
/// to receive; any drift breaks the client. No widened input
/// acceptance, no loosened gating, no fabricated replies —
/// server-integrity POSITIVE.
/// </para>
///
/// <para>
/// Budget: 60s. Single-stage station handshake into Luna Station
/// (10151) ~2s; assertions run synchronously against already-captured
/// state. No additional client stimulus.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorAvatarDescriptionHardeningTests
{
    /// <summary>
    /// 4 (uint32 AvatarID) + 241 (AvatarData) + 4 (int32 unknown1) + 3
    /// (u8 unknown2[3]) + 4 (float unknown3) + 4 (float unknown4) = 260.
    /// Matches the wire size computed by <c>sizeof(AvatarDescription)</c>
    /// against the <c>ATTRIB_PACKED</c> struct at
    /// <c>common/include/net7/PacketStructures.h:490-498</c>.
    /// </summary>
    private const int ExpectedAvatarDescriptionPayloadLength = 260;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorAvatarDescriptionHardeningTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task AvatarDescription_EmittedDuringStationSectorHandshake_HasExactly260BytePayload()
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
            firstName: "AvDesc78", shipName: "AvDesc78Ship", cts.Token);

        var avatarDescriptionFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.AvatarDescription.Value)
            .ToList();

        Assert.NotEmpty(avatarDescriptionFrames);
        Assert.All(avatarDescriptionFrames, f =>
            Assert.Equal(ExpectedAvatarDescriptionPayloadLength, f.PayloadLength));
    }
}
