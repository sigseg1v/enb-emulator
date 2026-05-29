// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 35 survival probe: client sends 0x009B WARP with a canonical
/// 86-byte all-zero WarpPacket payload from a freshly-handshaken
/// starbase character; assert the server survives by round-tripping
/// REQUEST_TIME after.
///
/// <para>
/// Wire shape (common/include/net7/PacketStructures.h:236):
/// <code>
///   struct WarpPacket {
///       int32_t GameID;       // [0..4)
///       short   Navs;         // [4..6)
///       int32_t TargetID[20]; // [6..86)
///   } ATTRIB_PACKED;          // sizeof = 86
/// </code>
/// All-zero is the canonical "warp with no nav targets" wire shape --
/// SetupWarpNavs(0, ...) is a no-op, and PrepareForWarp's m_WarpNavCount
/// gate at PlayerClass.cpp:2299 skips the nav-distance check.
/// </para>
///
/// <para>
/// Server handler walk-through (server/src/PlayerConnection.cpp:1873).
/// </para>
/// <list type="number">
///   <item>CheckForInstalls() -- returns true on a fresh character
///     (no equipment installs in progress). Path continues.</item>
///   <item>WarpDrive() -- returns false on a freshly-logged-in
///     character (not currently warping). Path falls through to the
///     else branch.</item>
///   <item>CheckGroupFormation(this) -- returns false on a fresh
///     character (not in a group). Path falls through to the inner
///     else.</item>
///   <item>GroupID() == -1 -- skip LeaveFormation.</item>
///   <item>SendContrailsRL(false) -- emits 0x001B AUX_DATA with the
///     contrails-off blob via Contrails(). Already counted by Wave
///     35 AUX_DATA coverage.</item>
///   <item>SetupWarpNavs(0, [0]*20) -- with navs=0, the for-loop at
///     PlayerClass.cpp:2174 body iterates zero times; m_WarpNavCount
///     stays at its prior (0) value.</item>
///   <item>PrepareForWarp() -- the dangerous part. The function
///     zeroes velocity/rotation, checks the current-skill cloak
///     interrupt path (m_CurrentSkill is NULL on a fresh char so
///     the if-block is skipped), then gates on
///     !m_WarpDrive &amp;&amp; !ShipIndex()->GetIsIncapacitated() &amp;&amp;
///     m_GWell == -1 (all true on a fresh starbase character) and
///     enters the reactor/engine lookup path. m_Equip[1] (reactor)
///     and m_Equip[2] (engine) lookups use the ItemBase ?: 1
///     fallback so a NULL m_Equip slot still returns warp_rating=1.
///     The energy/damage paths then execute, but with m_WarpNavCount=0
///     the actual warp-launch arm (PlayerClass.cpp:2400+, target-
///     finding loop) is reached with an empty nav list; the loop
///     body iterates zero times and PrepareForWarp returns
///     normally.</item>
/// </list>
///
/// <para>
/// Expected survival outcome: no direct reply to 0x009B from the
/// retail server, but the server-side state mutations (velocity=0,
/// SendContrailsRL emit) are all observable as side effects via the
/// in-sector AUX_DATA stream. The dispatcher case at
/// PlayerConnection.cpp:571 does not gate on Active() so the handler
/// fires even on a fresh starbase login. The connection should
/// survive and REQUEST_TIME should still echo CLIENT_SET_TIME with
/// the sentinel tick.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at PlayerConnection.cpp:571.</b>
///     The 0x009B case sits between 0x0098 GALAXY_MAP_REQUEST
///     (line 567, harmless one-line SendOpcode emit) and 0x009D
///     STARBASE_AVATAR_CHANGE (line 575, heavy state mutation that
///     would mis-cast our 86B buffer as the wrong struct). Survival
///     catches either swap.
///   </item>
///   <item>
///     <b>WarpPacket struct layout drift at
///     PacketStructures.h:236.</b> int32_t GameID -> long widening
///     on LP64 Linux inflates sizeof(WarpPacket) from 86 to 90,
///     shifts TargetID[] by 4 bytes, and SetupWarpNavs reads garbage
///     as navs count. With navs=0 from the wire, even a 4-byte
///     shift would not crash but would emit Contrails for the wrong
///     player ID. Catches the crash case; content drift would
///     need byte-level assertion.
///   </item>
///   <item>
///     <b>PrepareForWarp NULL-deref regression.</b> ShipIndex(),
///     m_Equip[1]/[2], m_CurrentSkill all currently have safe
///     fallbacks; a refactor that drops one of them would crash on
///     a fresh starbase character. Survival probe catches all such
///     crashes.
///   </item>
///   <item>
///     <b>SetupWarpNavs OOB on TargetID[20] write.</b> If the for-
///     loop bound at PlayerClass.cpp:2174 changes from navs to a
///     hardcoded 20, our navs=0 payload would still trigger 20 OOB
///     writes past m_WarpNavs[]. Catches via SEGV.
///   </item>
///   <item>
///     <b>SendContrailsRL emit-side crash.</b> Contrails() at
///     PlayerConnection.cpp:1933 builds a 20B AUX_DATA blob and
///     calls SendOpcode -- a regression in the AUX_DATA packing
///     path would corrupt the per-Player UDP queue. Survival
///     catches the crash; content drift would slip past.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     PlayerConnection.cpp:127.</b> Corrupts the 0x2016 inner-
///     tuple parser; the subsequent REQUEST_TIME -> CLIENT_SET_TIME
///     echo would be mis-framed and never observed.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode dropping 0x009B.</b>
///     0x009B is not explicitly listed in
///     proxy/ClientToServer_linux_stubs.cpp's ProcessSectorServerOpcode
///     switch (verified by grep returning zero matches outside
///     openssl-vendored tables); falls through to the bottom
///     default-forward arm. A regression dropping that arm silents
///     both 0x009B dispatch AND the REQUEST_TIME echo (same arm),
///     surfacing as test timeout.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The 86-byte WarpPacket is
/// exactly what the retail Win32 client emits when the user clicks
/// the warp button on the navigation UI. An all-zero "no targets"
/// shape is unusual in retail (the UI requires at least one nav
/// before allowing warp) but the wire is well-formed per the
/// canonical struct layout; the server's handler tolerates the
/// empty nav list via the m_WarpNavCount==0 gate. No server change,
/// no widened input acceptance, no fabricated reply.
/// </para>
///
/// <para>
/// Why probe from starbase rather than space. The 0x0092 CAMERA_CONTROL
/// test does the 2-stage station-then-space handshake to get into
/// space; that's needed there because HandleStartAck only emits
/// CAMERA_CONTROL in the space-arm. WARP does not require space
/// context to dispatch -- the case at PlayerConnection.cpp:571 has
/// no Active() guard, no sector-num gate. Probing from starbase is
/// the simpler shape (no 2-stage handshake), exercises the same
/// handler code, and is consistent with PETITION_STUCK / MISSION_*
/// / GALAXY_MAP_REQUEST sibling survival probes that all probe from
/// starbase.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; WARP + REQUEST_TIME round-trip
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorWarpTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorWarpTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task Warp_OnFreshStarbaseSession_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Warpio" -- contains 'a', 'i', 'o' for the vowel-check
        // (per CLAUDE.md feedback_character-name-vowel memory).
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Warpio", shipName: "WarpShip", cts.Token);

        try
        {
            // WarpPacket canonical 86-byte all-zero shape:
            //   [0..4)   int32 GameID = 0
            //   [4..6)   short Navs   = 0
            //   [6..86)  int32 TargetID[20] = {0}
            byte[] payload = new byte[86];
            // all zero already from new byte[86]; explicit writes documented
            // for clarity but not strictly needed.
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(4, 2), 0);
            // payload[6..86] = 0 from new byte[].

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.Warp.Value, payload),
                cts.Token);

            // Survival probe: send REQUEST_TIME, assert CLIENT_SET_TIME
            // echoes our sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));
            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

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
                $"drained {maxFrames} frames after sending 0x009B WARP + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely HandleWarp SEGV'd inside PrepareForWarp (NULL m_Equip / ShipIndex deref), " +
                $"SetupWarpNavs OOB'd past m_WarpNavs[], " +
                $"the dispatcher case at PlayerConnection.cpp:571 got mis-routed, " +
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
