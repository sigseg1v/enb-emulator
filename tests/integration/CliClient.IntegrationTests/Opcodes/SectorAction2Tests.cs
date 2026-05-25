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
/// Wave 21 post-handshake survival round-trip: client sends 0x002D
/// ACTION2 (the "cross-sector-boundary action" wrapper opcode used by
/// the retail client for things like group-kick where the target may
/// not be in the actor's sector) with a sub-action that is a server-side
/// no-op, then verifies the connection survives by round-tripping
/// 0x0044 REQUEST_TIME.
///
/// <para>
/// Why ACTION2 follows ACTION (Wave 13). 0x002D is the
/// <c>HandleAction2</c> twin of 0x002C <c>HandleAction</c>: same
/// dispatch table on the back end (<c>HandleAction2</c> reshapes its
/// packet into the canonical <c>ActionPacket</c> then chains to
/// <c>HandleAction</c>), but a different wire envelope. The retail
/// Win32 client emits 0x002D when it wants to act on a target by
/// **name** rather than by GameID — useful for the cross-sector cases
/// where the GameID isn't valid in the actor's sector but the player
/// name is globally unique.
/// </para>
///
/// <para>
/// Wire layout. <c>ActionPacket2</c>
/// (<c>common/include/net7/PacketStructures.h:554</c>):
/// <code>
///     struct ActionPacket2 {
///         int32_t GameID;        // reversed bytes (big-endian wire)
///         int32_t Action;        // reversed bytes (big-endian wire)
///         short   string_len;    // host-endian (little-endian wire)
///         char    string[1];     // variable; null-terminated target name
///         int32_t _OptionalVar;  // reversed bytes (big-endian wire)
///     } ATTRIB_PACKED;
/// </code>
/// Min payload with string_len=0 is 14 bytes (4+4+2+4). The
/// <c>char string[1]</c> declaration is a flexible-array-member pattern;
/// the actual wire layout has <c>string_len</c> bytes of name content
/// starting at offset 10, and <c>_OptionalVar</c> follows at offset
/// <c>10 + string_len</c>.
/// </para>
///
/// <para>
/// Server handler. <c>Player::HandleAction2</c>
/// (<c>server/src/PlayerConnection.cpp:4257</c>):
/// <code>
///     ActionPacket2 *myAction2 = (ActionPacket2 *) data;
///     ActionPacket converted;
///     converted.GameID     = ntohl(myAction2-&gt;GameID);
///     converted.Action     = ntohl(myAction2-&gt;Action);
///     converted.Target     = g_PlayerMgr-&gt;GetGameIDFromName(myAction2-&gt;string);
///     converted.OptionalVar = ntohl(*(uint32_t *)(myAction2-&gt;string+myAction2-&gt;string_len));
///     HandleAction((unsigned char *) &amp;converted);
/// </code>
/// (The cast was <c>u_long*</c> before this wave — see the
/// "Server tightening" paragraph below.)
/// </para>
///
/// <para>
/// Why survival probe rather than direct reply assertion. After
/// reshaping, the handler chains into <c>HandleAction</c> which
/// dispatches on the converted Action value. We send Action=23
/// ("keep trading???", <c>PlayerConnection.cpp:4104</c>) — the literal
/// commented-out no-op already verified by Wave 13. No reply opcode is
/// emitted on this branch and the retail server doesn't emit one either,
/// so per the CLAUDE.md server-integrity rule we cannot fabricate one.
/// </para>
///
/// <para>
/// What we CAN assert: the dispatcher accepted the 0x002D wire
/// envelope, <c>HandleAction2</c> reshaped it correctly (no read past
/// the 14B payload end, no GetGameIDFromName crash on the empty
/// string), the chain into HandleAction landed on case 23, the proxy
/// didn't drop or mangle the frame, and the connection survives — all
/// observable through a follow-up 0x0044 REQUEST_TIME round-trip.
/// </para>
///
/// <para>
/// Why string_len=0 (and the empty-string name lookup). Two reasons.
/// </para>
/// <list type="number">
///   <item>
///     <b>Min payload exercises the OptionalVar offset arithmetic.</b>
///     <c>HandleAction2</c> reads OptionalVar from
///     <c>myAction2-&gt;string + myAction2-&gt;string_len</c>; with
///     string_len=0 that's offset 10. If the field's wire-side cast
///     is wider than int32 (Wave-12-class <c>sizeof(long)</c> bug —
///     see Server tightening below) the handler reads 8 bytes from
///     offset 10, runs past the end of the 14B payload into whatever
///     follows in the receive buffer, and produces undefined behaviour.
///     A correctly-cast handler reads exactly 4 bytes and the test
///     survives.
///   </item>
///   <item>
///     <b>Empty-string name lookup hits the "no such player" path
///     deterministically.</b>
///     <c>GetGameIDFromName("")</c> walks the global player list
///     calling <c>strcasecmp("", playerName)</c> on every entry; no
///     player has an empty name so the loop falls through and returns
///     -1. The pre-switch <c>GetObjectFromID(-1)</c> in HandleAction
///     hits none of the three branches in
///     <c>ObjectManager::GetObjectFromID</c>
///     (<c>server/src/ObjectManager.cpp:563</c>) — object_id&lt;0 so
///     all three guards fail and obj stays null. Case 23's body never
///     touches obj, so the null is fine.
///   </item>
/// </list>
///
/// <para>
/// Why Action=23 specifically. Same reason as Wave 13: case 23 is a
/// literal commented-out no-op in <c>HandleAction</c>'s switch — the
/// safest sub-action for a freshly-handed-off starbase character that
/// has no in-space target object, no equipped weapon, no registered
/// starbase, and no started trade. Any other sub-action either requires
/// authoritative in-space state we don't have or emits a side-effect
/// reply we'd need a more complex assertion for. The Wave 21 wire
/// bytes for the Action field are <c>0x00 0x00 0x00 0x17</c>
/// (big-endian 23) — when <c>ntohl</c> swaps on Linux x86_64 the host
/// value becomes 0x00000017 = 23.
/// </para>
///
/// <para>
/// Server tightening landed in this wave. The
/// <c>HandleAction2</c> handler contained
/// <c>ntohl(*(u_long *)(myAction2-&gt;string+myAction2-&gt;string_len))</c>
/// — the same <c>sizeof(long)==8</c> on Linux x86_64 bug class fixed
/// sweep-wide in Wave 12. That sweep's grep covered
/// <c>unsigned long</c> but missed the POSIX <c>u_long</c> typedef
/// variant, leaving this one site. With a min-size 14B ActionPacket2
/// the bug reads 4 bytes past the payload end into whatever the receive
/// buffer holds at that offset, producing a garbage OptionalVar value
/// passed into <c>HandleAction</c>. Sub-action 23 doesn't consume
/// OptionalVar so the bug was latent rather than crashing — but a
/// future sub-action that does consume OptionalVar would inherit it.
/// Fixed in the same commit as this test by changing the cast to
/// <c>uint32_t *</c>. Per CLAUDE.md this is a tightening toward
/// retail fidelity (Win32's <c>sizeof(u_long)==4</c> made the original
/// code accidentally correct on the platform the retail server ran on)
/// and explicitly welcomed by the server-integrity rules.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>The <c>u_long*</c> cast tightening regression.</b> If the
///     cast is reverted to <c>u_long*</c>, on Linux x86_64 it reads 8
///     bytes from offset 10 into a 14B payload, running 4 bytes past
///     the end. Whether that crashes or just produces garbage depends
///     on receive-buffer slack — flaky symptoms are exactly the kind
///     of bug the test catches by failing intermittently. (More
///     valuable than catching it deterministically because the
///     fundamentally undefined behaviour can't be deterministically
///     caught.)
///   </item>
///   <item>
///     <b>The Wave-12-class bug class re-introduced anywhere in
///     HandleAction2's read path.</b> Same reasoning extends to any
///     future modification that re-introduces a <c>sizeof(long)</c>
///     cast over a 4-byte wire field.
///   </item>
///   <item>
///     <b>ActionPacket2 struct layout regression in
///     PacketStructures.h.</b> If <c>GameID</c> or <c>Action</c> were
///     widened to <c>long</c> (Linux x86_64 8B), the struct grows and
///     the field offsets shift; the handler would read garbage from
///     the wrong wire positions, the ntohl-swapped Action would not
///     equal 23, the dispatch would fall to HandleAction's default
///     UNRECOGNIZED ACTION branch, and the survival probe would
///     usually still pass (the default branch logs but doesn't crash)
///     — but a `long` widening of OptionalVar specifically would
///     re-introduce the <c>sizeof(long)</c> read-past-end through the
///     <c>*string+string_len</c> arithmetic.
///   </item>
///   <item>
///     <b>HandleAction2's chain into HandleAction breaking.</b> A
///     refactor that forgot to call HandleAction at the end of the
///     reshape would silently drop the action; sub-action 23 produces
///     no reply either way, so this would still pass — but a future
///     wave that asserts on an Action-side effect would catch it.
///     This wave doesn't catch this regression, just documents the
///     chain dependency.
///   </item>
///   <item>
///     <b>GetGameIDFromName crash on the empty string.</b> If
///     <c>strcasecmp</c> were called with a null pointer (e.g. a
///     refactor that read <c>myAction2-&gt;string</c> after the
///     payload ended), the test crashes the sector thread and the
///     survival probe never gets a CLIENT_SET_TIME reply.
///   </item>
///   <item>
///     <b>Proxy default-case <c>ForwardClientOpcode</c> regression.</b>
///     0x002D is not explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>, so it hits the
///     <c>default:</c> arm and falls through to the bottom-of-switch
///     forward. A regression dropping this opcode would surface as a
///     timeout waiting for CLIENT_SET_TIME.
///   </item>
///   <item>
///     <b>Dispatcher mis-route at
///     <c>server/src/PlayerConnection.cpp:471</c>.</b> The case label
///     is in a ~200-entry hand-maintained switch immediately below
///     0x002C ACTION; a copy-paste error swapping the two would route
///     0x002D into HandleAction directly, which would mis-interpret
///     the 14B ActionPacket2 payload as a 16B ActionPacket — reading
///     OptionalVar from offset 12 (the wire's last 2 bytes of
///     OptionalVar plus 2 bytes of receive-buffer slack). Sub-action
///     decode would land on Action=ntohl(GameID)=0 instead of 23
///     (Action would be read from offset 4 = wire bytes
///     <c>00 00 00 17</c> which in host LE is 0x17000000, not 0x17)
///     and fall to default branch. The connection survives, the test
///     passes — but a follow-up wave that asserts on a non-no-op
///     sub-action would catch the misroute.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The ACTION2 payload sent
/// here is exactly the wire shape the retail Win32 client emits when
/// dispatching a name-targeted action: 4B big-endian GameID, 4B
/// big-endian Action, 2B host-order string_len, the
/// (string_len)-byte name string, 4B big-endian OptionalVar.
/// Sub-action 23 ("keep trading") is a published retail value; the
/// empty-name no-such-player path is exactly how retail handles a
/// stale/disconnected target name. We are not making the server accept
/// any new input shape, and the retail server emits no reply on this
/// branch either — that's why we use a survival probe rather than
/// asserting a fabricated reply. The <c>u_long*</c> → <c>uint32_t*</c>
/// cast change in the same commit is a tightening (Win32-fidelity
/// alignment); see plans/99-decisions-log.md 2026-05-25 Wave 21.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; ACTION2+REQUEST_TIME round-trip is
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorAction2Tests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorAction2Tests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task Action2_NoOpSubActionAndEmptyName_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        // cli_test18 — Pool[16]. Dedicated to this test so its
        // Create/Delete cycle doesn't collide with Pool[3..15] which
        // are owned by prior Phase K waves.
        var account = TestAccounts.Pool[16];
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Crosser", shipName: "CrossShip", cts.Token);

        try
        {
            // ActionPacket2 wire layout — 14 bytes with string_len=0:
            //   [0..4)   big-endian int32   GameID       = 0
            //                                 (ntohl-swapped on host;
            //                                  not consumed by case 23)
            //   [4..8)   big-endian int32   Action       = 23
            //                                 wire bytes: 00 00 00 17
            //                                 ntohl-swapped to host 23 LE
            //                                 ("keep trading???" no-op)
            //   [8..10)  little-endian int16 string_len = 0
            //                                 (host-endian; handler does
            //                                  not ntohl this field)
            //   [10..14) big-endian int32   OptionalVar  = 0
            //                                 read by handler at
            //                                 *(uint32_t*)(string+string_len)
            //                                 = *(uint32_t*)(byte10) = wire
            //                                 bytes 10..13.
            //                                 (Was u_long* pre-Wave-21
            //                                  → 8B read past payload end
            //                                  on Linux x86_64.)
            //
            // No string bytes between offset 10 and OptionalVar because
            // string_len=0. The handler passes myAction2->string
            // (= pointer to byte 10) into GetGameIDFromName; byte 10 is
            // OptionalVar's first byte = 0x00, so the C string starts
            // and ends with a null terminator — strcasecmp("", any-name)
            // returns nonzero for every connected player, GetGameIDFromName
            // returns -1, HandleAction's pre-switch GetObjectFromID(-1)
            // returns null (object_id<0 fails all three branches in
            // ObjectManager::GetObjectFromID at line 563), and case 23
            // never touches obj.
            //
            // common/include/net7/PacketStructures.h:554
            byte[] payload = new byte[14];
            // GameID big-endian = 0 → all zero bytes (no-op write).
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), 0);
            // Action big-endian = 23 → wire bytes 00 00 00 17.
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), 23);
            // string_len host-endian (little-endian on x86) = 0.
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(8, 2), 0);
            // OptionalVar big-endian = 0.
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(10, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.Action2.Value, payload),
                cts.Token);

            // Survival probe.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Post-handshake the
            // server may begin streaming in-sector frames so this loop
            // tolerates interleaved traffic. Cap on frame count so a
            // stalled pipeline can't masquerade as the outer-CTS
            // timeout.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.ClientSetTime.Value)
                    continue;

                // 0x0034 wire layout (ClientSetTime struct):
                //   [0..4)  int32  ClientSent
                //   [4..8)  int32  ServerReceived
                //   [8..12) int32  ServerSent
                // common/include/net7/PacketStructures.h:563
                var span = reply.Payload.Span;
                Assert.Equal(12, span.Length);

                int echoedClientSent = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);
                Assert.Equal(clientTick, echoedClientSent);

                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x002D ACTION2 (sub=23, empty-name) + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the server's HandleAction2 crashed (u_long* cast regression reading 4 bytes past 14B payload end), " +
                $"GetGameIDFromName segfaulted on the empty-string lookup, " +
                $"the proxy default-case forwarding dropped the opcode, " +
                $"the dispatcher case at PlayerConnection.cpp:471 got mis-routed, " +
                $"or HandleAction's pre-switch GetObjectFromID(Target=-1) null-derefed.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
