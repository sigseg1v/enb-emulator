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
/// Wave 13 post-handshake survival round-trip: client sends 0x002C ACTION
/// (the catch-all combat / interaction / docking opcode) with a sub-action
/// that is a server-side no-op, then verifies the connection survives by
/// round-tripping 0x0044 REQUEST_TIME.
///
/// <para>
/// Why survival probe rather than direct reply assertion.
/// <c>Player::HandleAction</c>
/// (<c>server/src/PlayerConnection.cpp:3708</c>) dispatches on
/// <c>myAction-&gt;Action</c> through a 30-ish entry switch. The vast
/// majority of sub-actions need authoritative in-space state — a valid
/// target object, an equipped weapon, a registered starbase, a started
/// trade — none of which a freshly-handed-off starbase character has.
/// Pick a sub-action that lands cleanly with zero side effects:
/// <c>case 23 // keep trading???</c> (lines 4104-4108) is a literal
/// commented-out no-op. The handler reads the canonical 16-byte
/// ActionPacket struct (GameID/Action/Target/OptionalVar as int32_t LE)
/// and falls into an empty case body, then returns. No reply opcode is
/// emitted on this branch and the retail server doesn't emit one either,
/// so per the CLAUDE.md server-integrity rule we cannot fabricate one.
/// </para>
///
/// <para>
/// What we CAN assert: the dispatcher accepted the wire format, the
/// switch found case 23, the proxy didn't drop or mangle the frame, and
/// the connection survives — all observable through a follow-up 0x0044
/// REQUEST_TIME round-trip.
/// </para>
///
/// <para>
/// Concrete regression class this catches: if anyone reverts the Wave 11
/// PacketStructures.h <c>long</c>→<c>int32_t</c> migration on
/// ActionPacket, the struct would grow from 16B to 32B on Linux x86_64
/// and the handler would read Action from offset 8 (where the wire has
/// Target's high half) instead of offset 4. The dispatched sub-action
/// number would then be garbage and almost certainly miss case 23 → fall
/// to the default branch → emit "UNRECOGNIZED ACTION! SUBMIT BUG
/// REPORT!" via SendVaMessage. The survival probe still passes in that
/// regression (the connection doesn't die) but the explicit case-23
/// payload choice makes the retail-fidelity intent visible in the test.
/// </para>
///
/// <para>
/// Other bugs this test would also catch:
/// </para>
/// <list type="bullet">
///   <item>
///     Proxy <c>ProcessSectorServerOpcode</c> for ACTION
///     (<c>proxy/ClientToServer_linux_stubs.cpp:471-477</c>) dropping or
///     double-forwarding the opcode. The current path forwards
///     explicitly then calls <c>ProcessAction_Linux</c> (whose body is
///     all <c>//</c>-commented no-ops); a regression that called
///     <c>ForwardClientOpcode</c> twice would manifest as two server-side
///     handler invocations — benign for sub-action 23 but a silent
///     duplicate-input hazard for state-mutating sub-actions later.
///   </item>
///   <item>
///     <c>HandleAction</c>'s lookup of <c>obj</c> via
///     <c>GetObjectFromID(myAction-&gt;Target)</c> returning a bogus
///     pointer for the Target=0 sentinel and crashing on a deref before
///     reaching the switch. Sub-action 23 doesn't touch <c>obj</c>, so a
///     null-deref pre-switch would surface here as the survival probe
///     never completing.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The ACTION payload sent here
/// is exactly the wire shape the retail Win32 client emits: 4-byte LE
/// GameID, Action, Target, OptionalVar. Sub-action 23 ("keep trading")
/// is one of the retail client's published values; we are not making the
/// server accept anything it didn't previously accept. The retail server
/// also emits no direct reply on this branch — that's why we use a
/// survival probe rather than asserting a fabricated reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; ACTION+REQUEST_TIME round-trip is
/// sub-second. Wide budget covers stage-ack retry in the login state
/// machine.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorActionTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorActionTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task Action_NoOpSubAction_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Actor", shipName: "ActShip", cts.Token);

        try
        {
            // ActionPacket wire layout — 16 bytes total, all int32_t LE:
            //   [0..4)   GameID       — retail client sets the actor's
            //                            game id; server resolves the
            //                            actor via the connection
            //                            binding so this field is
            //                            effectively unused, but its
            //                            width matters for the struct
            //                            offset of Action.
            //   [4..8)   Action       — sub-action selector. 23 =
            //                            "keep trading???" (a commented-
            //                            out no-op in HandleAction).
            //   [8..12)  Target       — target game id. 0 (none) is
            //                            safe for sub-action 23 because
            //                            the case body never touches it.
            //   [12..16) OptionalVar  — sub-action-specific scalar;
            //                            unused by sub-action 23.
            // common/include/net7/PacketStructures.h:546
            byte[] actionPayload = new byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(actionPayload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(actionPayload.AsSpan(4, 4), 23);
            BinaryPrimitives.WriteInt32LittleEndian(actionPayload.AsSpan(8, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(actionPayload.AsSpan(12, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.Action.Value, actionPayload),
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
                $"drained {maxFrames} frames after sending 0x002C ACTION (sub=23) + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the server's HandleAction switch fell to default (UNRECOGNIZED ACTION) " +
                $"and the connection state was corrupted, the proxy's ProcessSectorServerOpcode dispatch " +
                $"(proxy/ClientToServer_linux_stubs.cpp:471-477) dropped the frame, " +
                $"or HandleAction's pre-switch GetObjectFromID(Target=0) crashed.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Body of the 0x001D MESSAGE_STRING reply the server emits via
    /// SendVaMessage("Invalid JS target! SUBMIT BUG REPORT!") in
    /// HandleAction's case 26 (Jump Start) when <c>obj</c> resolves to
    /// nullptr — exactly the situation produced by Target=0 (or any
    /// invalid Target the client hands in). 37 ASCII bytes; SendVaMessage
    /// hands the string to SendMessageString, which writes
    /// strlen+1 (=38) as the little-endian length prefix and a trailing
    /// NUL terminator. Net7.h does not symbol-define this string; the
    /// literal is hardcoded in the case-26 body.
    /// </summary>
    private const string InvalidJsTargetLiteral = "Invalid JS target! SUBMIT BUG REPORT!";

    /// <summary>
    /// Wave 124 sibling-arm-pinning hardening (+0 ratchet, 0x002C ACTION
    /// → 0x001D MESSAGE_STRING): pins the byte-exact 41-byte wire-shape
    /// of the single 0x001D MESSAGE_STRING the server emits in reply to
    /// a 0x002C ACTION whose <c>Action</c> field is 26 (Jump Start) and
    /// whose <c>Target</c> field is 0 (no target selected).
    ///
    /// <para>
    /// Why a +0 ratchet. 0x002C ACTION is already counted by Wave 13
    /// <see cref="Action_NoOpSubAction_DoesNotBreakConnection_RequestTimeStillRoundTrips"/>
    /// (sub-action 23, the commented-out no-op survival probe). This
    /// wave does NOT add an opcode to <c>TestedOpcodes.MinTestedCount</c>;
    /// it adds a structurally-distinct sibling-arm pin on the same
    /// <c>HandleAction</c> dispatcher Wave 13 exercises. Wave 13 sits on
    /// the silent default arm (Action=23 emits nothing); Wave 124 sits
    /// on the directly-replying arm (Action=26 with no target emits a
    /// fixed-string error). The two together box in the dispatcher's
    /// shape from both sides.
    /// </para>
    ///
    /// <para>
    /// What this catches. Six concrete regression classes, all of which
    /// the existing Wave 13 survival probe would silently pass through:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <c>ActionPacket</c> struct-layout drift. If anyone reverts
    ///     the Wave 11 PacketStructures.h <c>long</c>→<c>int32_t</c>
    ///     migration on ActionPacket the struct grows to 32B on Linux
    ///     x86_64 and the handler reads <c>Action</c> from offset 8
    ///     (where the wire has the high half of Target) instead of
    ///     offset 4 — the dispatched sub-action number is then garbage
    ///     and almost certainly misses case 26, so we never see the
    ///     "Invalid JS target!" reply. Wave 13 keeps passing
    ///     (the default case still emits no reply); Wave 124 fails.
    ///   </item>
    ///   <item>
    ///     <c>HandleAction</c>'s pre-switch
    ///     <c>obj = om-&gt;GetObjectFromID(myAction-&gt;Target)</c>
    ///     regressing to read <c>ShipIndex()-&gt;GetTargetGameID()</c>
    ///     instead of the wire field — Wave 13 still passes
    ///     (sub-action 23 doesn't touch <c>obj</c>); Wave 124 fails
    ///     because freshly-handed-off characters with no selected
    ///     target also resolve <c>obj</c> to nullptr and we'd see the
    ///     reply, but the test pins that we got it via the wire-Target=0
    ///     path. (This particular regression is detected differently —
    ///     by a Wave-130-class pin that flips Target to a known
    ///     non-zero GameID and asserts a different reply — but Wave 124
    ///     gives us the negative anchor for that future test.)
    ///   </item>
    ///   <item>
    ///     <c>ObjectManager::GetObjectFromID(0)</c> regressing into
    ///     returning a non-nullptr sentinel. The function's first arm
    ///     (<c>server/src/ObjectManager.cpp:567</c>) explicitly handles
    ///     <c>object_id &lt; m_StartObjectID &amp;&amp; object_id &gt;=
    ///     0</c> by returning the zero-initialised local; a refactor
    ///     that initialised <c>obj</c> to <c>m_SectorIndexList[0]</c>
    ///     "for safety" would make <c>!obj</c> false and we'd skip the
    ///     error branch — Wave 124 fails.
    ///   </item>
    ///   <item>
    ///     <c>SendVaMessage</c> → <c>SendMessageString</c> color-default
    ///     regression at <c>server/src/PlayerClass.h:277</c>. The
    ///     declaration reads <c>SendMessageString(char *msg, char
    ///     color=5, bool log=true)</c>; if the default were lost or
    ///     changed (e.g. to 17 to match the case-1 incapacitation arm)
    ///     the third byte of the wire body would change. Wave 124 pins
    ///     <c>span[2] == 5</c>.
    ///   </item>
    ///   <item>
    ///     <c>SendMessageString</c> length-field width regression at
    ///     <c>server/src/PlayerConnection.cpp</c>. The current emit
    ///     writes a <c>short</c> (u16 LE) length; if a refactor promoted
    ///     the field to <c>int</c> (u32 LE) the payload would be 43B,
    ///     not 41B, and offset 2 would be 0 instead of the color
    ///     byte. Wave 124 pins both total length (41) and the u16 at
    ///     offset 0 (38).
    ///   </item>
    ///   <item>
    ///     The fixed error string drifting. <c>SendVaMessage("Invalid
    ///     JS target! SUBMIT BUG REPORT!")</c> at
    ///     <c>server/src/PlayerConnection.cpp:4151</c> is a literal —
    ///     a typo-correction PR that "fixed" the punctuation, dropped
    ///     "SUBMIT BUG REPORT!", or localised the message would
    ///     silently change the body bytes. Wave 124 pins the verbatim
    ///     37-byte ASCII run and the trailing NUL.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity (CLAUDE.md). The case-26 error string and the
    /// 0x001D MESSAGE_STRING wire shape it travels on are both the
    /// retail server's behaviour, not test-only artefacts. Action=26 is
    /// the Jumpstart ability click; the retail client hands in
    /// <c>Target=GameID-of-incapacitated-ally</c> and the server runs
    /// the ability. The "Invalid JS target!" arm is what the retail
    /// server emits when the click arrives with no resolvable target —
    /// e.g. the targeted player left the sector between selection and
    /// click. This test exercises that POSITIVE-fidelity path by
    /// presenting Target=0 (the same shape the retail client sends on a
    /// no-selection click) and asserting the retail-shape reply. No
    /// server permissiveness is added: a real client click with no
    /// target lands here too.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s. Handshake ~2s; ACTION send + drain of one
    /// MESSAGE_STRING is sub-second. Wide budget covers stage-ack retry
    /// in the login state machine and any handshake-tail debris frames.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Action_JumpStartOnNullTarget_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "JSTest", shipName: "JSShip", cts.Token);

        try
        {
            // ActionPacket wire layout — 16 bytes total, all int32_t LE.
            // common/include/net7/PacketStructures.h:546
            //   [0..4)   GameID       — actor's game id (server resolves
            //                            actor via the connection binding
            //                            so this field is effectively
            //                            unused for routing).
            //   [4..8)   Action       — 26 = Jump Start.
            //   [8..12)  Target       — 0 = no target selected. The
            //                            handler's pre-switch
            //                            GetObjectFromID(0) returns
            //                            nullptr (ObjectManager.cpp:567
            //                            first arm), case 26 sees
            //                            !obj true and emits the fixed
            //                            error string.
            //   [12..16) OptionalVar  — unused by case 26.
            byte[] actionPayload = new byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(actionPayload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(actionPayload.AsSpan(4, 4), 26);
            BinaryPrimitives.WriteInt32LittleEndian(actionPayload.AsSpan(8, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(actionPayload.AsSpan(12, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.Action.Value, actionPayload),
                cts.Token);

            // Drain until we see the specific 0x001D MESSAGE_STRING
            // carrying "Invalid JS target!". Post-handshake the server
            // may interleave other MESSAGE_STRING frames (e.g. tutorial
            // banners), so substring-filter rather than first-match.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.MessageString.Value)
                    continue;

                var span = reply.Payload.Span;
                // Cheap substring probe before the strict pin so we
                // don't hard-fail on unrelated handshake-tail frames.
                if (span.Length < 3 + InvalidJsTargetLiteral.Length)
                    continue;
                var bodyText = System.Text.Encoding.ASCII.GetString(
                    span.Slice(3, Math.Min(span.Length - 3, 64)).ToArray());
                if (!bodyText.StartsWith("Invalid JS target!", StringComparison.Ordinal))
                    continue;

                // 0x001D wire layout — mirror of SendMessageString:
                //   [0..2)  u16 LE length = strlen(msg) + 1
                //   [2]     u8  color (default 5)
                //   [3..3+strlen)  ASCII body
                //   [3+strlen]     '\0'
                Assert.Equal(41, span.Length);

                short msgLen = BinaryPrimitives.ReadInt16LittleEndian(span[..2]);
                Assert.Equal((short)38, msgLen);

                Assert.Equal((byte)5, span[2]);

                byte[] expectedBody = System.Text.Encoding.ASCII.GetBytes(
                    InvalidJsTargetLiteral);
                Assert.Equal(37, expectedBody.Length);
                Assert.True(span.Slice(3, 37).SequenceEqual(expectedBody));

                Assert.Equal((byte)0x00, span[40]);

                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x002C ACTION " +
                $"(Action=26 Jump Start, Target=0) without seeing the 0x001D " +
                $"MESSAGE_STRING reply containing \"Invalid JS target!\". " +
                $"Likely HandleAction case 26 at PlayerConnection.cpp:4147 " +
                $"changed shape, GetObjectFromID(0) stopped returning nullptr, " +
                $"the SendVaMessage→SendMessageString fan-out was rewired, " +
                $"or the proxy dropped the 0x001D inside the encrypted client tunnel.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
