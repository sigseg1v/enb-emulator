// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Regression pin for REMOTE FACING REPLICATION: when a player flies under the
/// MVAS position feed, every in-range observer must see that player's ship NOSE
/// turn to match its flight direction -- not merely translate with a frozen
/// orientation.
///
/// <para>
/// The bug (server broadcasts a stale orientation for a moving player):
/// </para>
/// <list type="bullet">
///   <item>
///     Under the MVAS feed the server refreshes <c>m_Position_info.Position</c>
///     every frame (<c>Player::UpdatePositionFromMVAS</c> -&gt; <c>SetPosition</c>)
///     but NEVER refreshes <c>m_Position_info.Orientation</c>:
///     <c>CalcNewHeading</c> is skipped while <c>IS_PLAYER(m_MVAS_index)</c>
///     (<c>PlayerClass.cpp CalcNewPosition</c>), and the feed stores the client
///     nose vector only in the decoupled <c>m_ClientHeading</c> (firing-arc use,
///     see CV-MVAS-ORIENT).
///   </item>
///   <item>
///     So the 0x003E ADVANCED_POSITIONAL_UPDATE the server fans out to observers
///     (<c>Player::SendToVisibilityList</c> -&gt;
///     <c>SendAdvancedPositionalUpdate</c>) carries a stale/spawn Orientation
///     quaternion. Observed live: another player's hull slides through the sector
///     without ever turning.
///   </item>
/// </list>
///
/// <para>
/// The fix (<c>server/src/PlayerClass.cpp SendToVisibilityList</c>): on the
/// OUTBOUND broadcast only, convert the already-fed nose vector
/// (<c>m_ClientHeading</c>) to the wire quaternion via the engine look-rotation
/// (<c>Object::CalcOrientation(target, self, set_heading=false)</c>, yaw+pitch,
/// roll leveled) and send that, then restore the stored orientation -- mirroring
/// the existing transient <c>RotZ</c>/<c>UpdatePeriod</c> mutate-restore in the
/// same function. Strictly broadcast-only: it never persists into
/// <c>Orientation()</c>, never touches velocity, and cannot reintroduce the
/// CV-MVAS-POS phantom-velocity warp regression.
/// </para>
///
/// <para>
/// ROLL IS NOT REPLICATED, BY DESIGN. The retail MVAS 0x1004 feed carries
/// position xyz + nose-heading xyz and nothing else (live Net7Proxy capture,
/// server reads only the 6 floats) -- there is no roll component on the retail
/// flying-client -&gt; server wire, so no observer ever saw another ship's true
/// roll. Nose direction with roll leveled is the most any observer could
/// faithfully have seen; fabricating a roll channel would DIVERGE from retail.
/// </para>
///
/// <para>
/// CLAUDE.md server-integrity. Correctness change on (1) internal inconsistency
/// (the server advances a player's broadcast position every frame while
/// broadcasting a frozen orientation for the SAME player), (2) the nose vector is
/// already present server-side (<c>m_ClientHeading</c>, pinned by the 0x1004
/// heading capture), routed onto the existing 0x003E Orientation slot, and (3)
/// owner first-hand retail testimony that other ships visibly turned. No new wire
/// field, no widened input acceptance, no loosened gate. The 0x003E byte layout is
/// already pinned by <see cref="SectorAdvancedPositionalUpdateHardeningTests"/>;
/// this changes only the VALUE of the Orientation quaternion for a moving player.
/// Real-client verification: <c>plans/29-client-verification.md</c>
/// (CV-MVAS-ORIENT-BCAST).
/// </para>
///
/// <para>
/// <b>BLOCKED by Net7Proxy single-tenancy</b> -- identical wall to
/// <see cref="TwoPlayerGroupNavExploreShareTests"/>: Player B's MasterJoin
/// clobbers Player A's UDP routing (<c>proxy/ClientToMasterServer.cpp:104</c>), so
/// A times out at login stage 9. Additionally requires an in-test driver to move
/// A under the MVAS feed so its <c>m_ClientHeading</c> is set and its broadcast
/// orientation diverges from spawn -- no such driver exists today. The test body
/// encodes the intended shape (observer B decodes a 0x003E for A whose Orientation
/// quaternion is NOT the stale spawn identity once A has turned).
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class TwoPlayerRemoteFacingReplicationTests : SectorIntegrationTest
{
    public TwoPlayerRemoteFacingReplicationTests(ServerFixture server) : base(server) { }

    private const string ProxySingleTenancySkip =
        "BLOCKED by Net7Proxy single-tenancy: Player B's MasterJoin clobbers " +
        "Player A's UDP routing (proxy/ClientToMasterServer.cpp:104), so A times " +
        "out at login stage 9 -- same wall as TwoPlayerGroupNavExploreShareTests. " +
        "Additionally requires an in-test driver to move A under the MVAS position " +
        "feed so m_ClientHeading is set and A's broadcast orientation diverges from " +
        "spawn. Server fix (PlayerClass.cpp SendToVisibilityList) verified by code " +
        "inspection + plans/29 CV-MVAS-ORIENT-BCAST. Unskip once the proxy " +
        "multiplexes per session AND an MVAS movement driver exists.";

    // 0x003E ADVANCED_POSITIONAL_UPDATE, Bitmask==0 layout (42-byte body, pinned
    // by SectorAdvancedPositionalUpdateHardeningTests): u16 Bitmask; then 4-byte
    // slots -- int32 GameID; float TimeStamp; float Position[3]; float
    // Orientation[4]; int32 MovementID. Orientation floats start at byte 22.
    private const int OrientationOffset = 2 + 4 + 4 + 12; // 22

    [Fact(Skip = ProxySingleTenancySkip)]
    public async Task PlayerFliesUnderMvasFeed_InRangeObserver_ReceivesTurnedOrientation()
    {
        var accountA = TestAccounts.New(_server);  // the mover
        var accountB = TestAccounts.New(_server);  // the observer
        const int slot = 0;
        const int sectorId = 1071;  // Jupiter (a space sector, not a starbase).
        const string moverName = "Avara";    // must contain a vowel (G_ERROR_ONE_VOWEL=4)
        const string observerName = "Bevora";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(200));

        var loginA = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(accountA.Username, accountA.Password), cts.Token);
        Assert.True(loginA.Valid, $"loginA: {loginA.RawBody.TrimEnd()}");

        var loginB = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(accountB.Username, accountB.Password), cts.Token);
        Assert.True(loginB.Valid, $"loginB: {loginB.RawBody.TrimEnd()}");

        var sessionA = Track(await SectorHandshake.EstablishAsync(
            _server, loginA.Ticket!, accountA.Username, slot, sectorId,
            firstName: moverName, shipName: "AvaraShip", cts.Token));

        var sessionB = Track(await SectorHandshake.EstablishAsync(
            _server, loginB.Ticket!, accountB.Username, slot, sectorId,
            firstName: observerName, shipName: "BevoraShip", cts.Token));

        Assert.NotEqual(sessionA.GameId, sessionB.GameId);

        // Mover A now flies under the MVAS position feed and turns, so the server
        // stores A's nose vector in m_ClientHeading and (with the fix) broadcasts
        // A's turned facing on the 0x003E Orientation slot. Driving A under the
        // MVAS feed needs an in-test movement driver that does not yet exist; see
        // the skip reason.

        // Discriminating assertion: observer B receives a 0x003E for A whose
        // Orientation quaternion reflects A's flight direction -- specifically it
        // is NOT the frozen spawn identity that the pre-fix broadcast emitted.
        using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        drainCts.CancelAfter(TimeSpan.FromSeconds(30));

        while (true)
        {
            var frame = await sessionB.Sector.ReceiveAsync(drainCts.Token);
            Assert.NotNull(frame);
            if (frame!.Header.Opcode != OpcodeId.Known.AdvancedPositionalUpdate.Value)
                continue;

            var span = frame.Payload.Span;
            Assert.True(span.Length >= 42, $"0x003E truncated: {span.Length}B");

            int gameId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(2, 4));
            if (gameId != (int)sessionA.GameId)
                continue;  // not the mover's update

            float ox = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(OrientationOffset, 4));
            float oy = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(OrientationOffset + 4, 4));
            float oz = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(OrientationOffset + 8, 4));
            float ow = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(OrientationOffset + 12, 4));

            // After A has turned, its broadcast orientation must not be the stale
            // spawn quaternion. The pre-fix broadcast never updated it, so the
            // vector part stayed ~0 (identity) for the whole flight.
            float vectorMag = MathF.Sqrt(ox * ox + oy * oy + oz * oz);
            Assert.True(vectorMag > 1e-3f,
                $"Orientation still identity/stale ({ox},{oy},{oz},{ow}) -- facing not replicated");
            return;
        }
    }
}
