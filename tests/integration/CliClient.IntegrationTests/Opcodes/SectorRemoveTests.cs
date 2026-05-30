// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;
using System.Buffers.Binary;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 41 server-emit pinning: client sends 0x0082 RECUSTOMIZE_SHIP_DONE
/// (canonical 210-byte all-zero payload, same stimulus as Wave 36's
/// SectorRecustomizeShipDoneTests survival probe), then drains for the
/// 0x0007 REMOVE the server unconditionally emits at
/// <c>server/src/PlayerConnection.cpp:10091</c> as part of the recustomize
/// fan-out (the old ship gets removed from the sector visibility before
/// the new ship_data fans out via SendShipData / SendAuxShipExtended /
/// SendAuxPlayer).
///
/// <para>
/// Server emit walk (server/src/PlayerConnection.cpp:10049 onwards,
/// HandleRecustomizeShipDone):
/// </para>
/// <list type="number">
///   <item>Recompute cost, SetCredits, save credit level.</item>
///   <item>Overwrite <c>m_Database.ship_data = packet-&gt;ship</c>.</item>
///   <item>SaveDatabase, NeatenUpWeaponMounts.</item>
///   <item><c>RemoveObject(GameID())</c> -- emits 0x0007 REMOVE with a
///     4-byte int32 payload = the caller's own GameID. See
///     <c>Player::RemoveObject</c> (PlayerConnection.cpp:2335-2345)
///     which calls <c>SendOpcode(ENB_OPCODE_0007_REMOVE,
///     &amp;object_id, sizeof(object_id))</c>. The Wave 11 / Wave 12
///     int32_t-pinning comment at PlayerConnection.cpp:2337-2339 calls
///     out the load-bearing 4-byte wire shape: storing object_id as
///     <c>long</c> would emit 8 bytes on LP64 Linux.</item>
///   <item>SendShipData(this), SendAuxShipExtended, SendAuxPlayer
///     (the visual fan-out for the freshly recustomised ship).</item>
/// </list>
///
/// <para>
/// Why this is a tractable single-player path to 0x0007. The only
/// alternative paths to RemoveObject from single-player game state are:
/// (a) trade arms in HandleAction (multi-player), (b) prospect
/// completion (requires HUSK target), (c) MOB destruction (multi-player
/// or hostile-mob spawn). RECUSTOMIZE_SHIP_DONE is the only path the
/// retail Win32 client triggers solo and that emits 0x0007 to OURSELVES
/// (the GameID() argument is the calling player).
/// </para>
///
/// <para>
/// What this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>RemoveObject int32_t -&gt; long width regression at
///     PlayerConnection.cpp:2335.</b> The Wave-11/12 int32_t pinning is
///     load-bearing; revert to <c>long</c> would grow the wire payload
///     from 4 to 8 bytes on Linux LP64. The 4-byte length assertion
///     pins the canonical retail Win32 LP32 wire shape.
///   </item>
///   <item>
///     <b>RemoveObject removal from the HandleRecustomizeShipDone
///     fan-out at PlayerConnection.cpp:10091.</b> A refactor that drops
///     the RemoveObject call (e.g. "we just overwrite ship_data, no
///     need to re-spawn") would leak the old ship visual on retail
///     clients -- the test catches the missing 0x0007 emit before any
///     client-visible regression.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at PlayerConnection.cpp:127.</b>
///     Corrupts the 0x2016 PACKET_SEQUENCE inner-tuple parser; 0x0007
///     would be mis-labelled in the inbound stream -- drain times out.
///   </item>
///   <item>
///     <b>HandleRecustomizeShipDone reordering regression.</b> The
///     current order is RemoveObject -&gt; SendShipData; reordering to
///     SendShipData -&gt; RemoveObject would briefly show the new ship
///     and then remove it. Test catches the missing 0x0007 if the
///     RemoveObject call was dropped, and would still pass (with
///     ordering changed) if reordered -- a stricter ordering assertion
///     is left to a future ordering-pinning wave.
///   </item>
///   <item>
///     <b>UnSetTarget side-effect regression at
///     PlayerConnection.cpp:2340.</b> RemoveObject calls UnSetTarget
///     before SendOpcode; a refactor that moved UnSetTarget below the
///     SendOpcode would change observable client behaviour (target
///     stays selected on a removed object). Not directly observable
///     here but the wire-shape pin guards the SendOpcode call itself.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard
///     tightening at proxy/UDPProxyToClient_linux.cpp:568.</b>
///     Currently passes 0x0007 because &lt; 0x0FFF; a tighter bound
///     would silently drop.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity (CLAUDE.md). The 0x0082 RECUSTOMIZE_SHIP_DONE
/// stimulus is the same canonical 210-byte wire shape Wave 36
/// documents (matches PacketStructures.h:1101 ATTRIB_PACKED). The
/// 0x0007 REMOVE emit with a 4-byte int32 GameID body is the verbatim
/// retail server behaviour for "remove this game object from your
/// sector view" -- Player::RemoveObject is the only function that
/// emits 0x0007 and its source comments explicitly call out the
/// 4-byte wire shape as load-bearing. No input permissiveness added,
/// no fabricated reply, no widened server state.
/// </para>
///
/// <para>
/// Destructive-nature note (inherited from Wave 36). This test
/// deliberately corrupts the character's ship_data row in the DB. The
/// character is deleted at end-of-test, so the corruption is contained.
/// Sibling tests each get their own TestAccounts.New scope; orphaned
/// rows are only visible via direct DB inspection.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; RECUSTOMIZE_SHIP_DONE + 0x0007 drain
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorRemoveTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorRemoveTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task Remove_EmittedAfterRecustomizeShipDone_HasExactly4BytePayload()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Remover" -- contains 'e', 'o' for the vowel-check.
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Remover", shipName: "RemoverShip", cts.Token);

        try
        {
            // RecustomizeShipDone canonical 210-byte all-zero shape:
            //   [0..194)  ShipData (5 int32 + 26 name + 12 color + 8*17 ColorInfo)
            //   [194..198) int32 playerid
            //   [198]      bool unknown
            //   [199..210) char _unknown[11]
            byte[] payload = new byte[210];

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RecustomizeShipDone.Value, payload),
                cts.Token);

            // Drain looking for 0x0007 REMOVE. The handler unconditionally
            // calls RemoveObject(GameID()) at PlayerConnection.cpp:10091
            // which emits 0x0007 with a 4-byte int32 payload. The
            // post-stimulus fan-out also includes SendShipData,
            // SendAuxShipExtended, and SendAuxPlayer -- those are already
            // covered by other waves and this loop tolerates interleaving.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.Remove.Value)
                    continue;

                // 0x0007 wire layout:
                //   [0..4)  int32 LE object_id  (the removed object's GameID)
                // The Wave-11/12 int32_t pinning at
                // PlayerConnection.cpp:2335-2345 makes this exactly 4 bytes.
                var span = reply.Payload.Span;
                Assert.Equal(4, span.Length);

                // Sanity probe: the 4-byte payload decodes to a non-zero
                // int32 (we know our own GameID is positive). This is a
                // weak check -- the exact GameID is sector-assigned at
                // handshake and not directly accessible from the session
                // wrapper -- but a payload of all-zero would indicate a
                // serious regression (RemoveObject called with GameID()==0
                // which never happens for an active Player).
                int removedId = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);
                Assert.NotEqual(0, removedId);

                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0082 RECUSTOMIZE_SHIP_DONE " +
                $"without seeing 0x0007 REMOVE. " +
                $"Likely RemoveObject was dropped from HandleRecustomizeShipDone's fan-out at " +
                $"PlayerConnection.cpp:10091, RemoveObject's SendOpcode call at " +
                $"PlayerConnection.cpp:2341 was removed or rewired, the SendOpcode header-width " +
                $"fix at PlayerConnection.cpp:127 was reverted (mislabeling 0x0007 in the inbound " +
                $"stream), or the proxy SendClientPacketSequence inner-opcode guard tightened " +
                $"to drop 0x0007.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
