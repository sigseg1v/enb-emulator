// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 30 direct-reply round-trip: client sends an 8-byte 0x0086
/// MISSION_FORFEIT for an empty mission slot, expects the server's
/// "This mission is non forfeitable." reply back as a 0x001D
/// MESSAGE_STRING.
///
/// <para>
/// Wire layout (mirror of <c>common/include/net7/PacketStructures.h:1001</c>):
/// <code>
///   [0..4) int32 PlayerID
///   [4..8) int32 MissionID
/// </code>
/// 8 bytes total; <c>ATTRIB_PACKED</c> so no implicit padding.
/// </para>
///
/// <para>
/// Byte order. <c>Player::HandleMissionForfeit</c>
/// (<c>server/src/PlayerConnection.cpp:11009</c>) ntohl-swaps both
/// fields on entry — wire format is NETWORK byte order
/// (big-endian), matching Wave 27 INVENTORY_MOVE and unlike Wave 28
/// SELECT_TALK_TREE which is host-LE. The per-handler byte-order
/// methodology from Wave 28's decisions entry is now reflexive: this
/// test follows the handler's own ntohl calls, not a sibling opcode.
/// </para>
///
/// <para>
/// Server handler. After the dispatcher case at
/// <c>PlayerConnection.cpp:551</c> calls HandleMissionForfeit, the
/// handler does:
/// <code>
///     if (MissionID >= 0 &amp;&amp; MissionID &lt; 12)
///         MissionDismiss(MissionID, true);
/// </code>
/// MissionDismiss (<c>PlayerMissions.cpp:1616</c>) walks
/// <c>m_PlayerIndex.Missions.Mission[mission_slot]</c> and checks
/// <c>m-&gt;GetIsForfeitable()</c>. For a freshly-created character
/// who has never accepted a mission, the AuxMission slot was
/// zero-initialised by <c>AuxMission::Init</c>
/// (<c>AuxClasses/AuxMission.h:85</c>: <c>Data-&gt;IsForfeitable = 0</c>).
/// With <c>forfeit_pressed=true</c> the boolean reduces to
/// <c>(!true || false) = false</c> and the handler falls into the
/// else branch at <c>PlayerMissions.cpp:1630</c>:
/// <code>
///     SendVaMessageC(17, "This mission is non forfeitable.");
/// </code>
/// SendVaMessageC (<c>PlayerClass.cpp:3443</c>) vsprintf_s's the
/// format string then calls SendMessageString
/// (<c>PlayerConnection.cpp:10974</c>) which builds a
/// <c>[short length][char color=17][string\0]</c> buffer and emits
/// via <c>SendOpcode(0x001D, ..., length + 3)</c> — per-client UDP
/// queue, no SendToSector login-stage race (same favourable
/// structural property Wave 24 / Wave 26 / Wave 27 exploited).
/// </para>
///
/// <para>
/// Why MissionID=0. The handler's <c>[0..12)</c> filter is the only
/// gate; any value in range routes to MissionDismiss. Slot 0 is the
/// canonical "first mission slot" — empty on a fresh starbase
/// character with no mission acceptance history. The <c>AuxMission</c>
/// at that slot was zeroed by Init when the AuxMissions container
/// was constructed (<c>AuxClasses/AuxMissions.h:74</c> declares
/// <c>class AuxMission Mission[12];</c>), so <c>IsForfeitable</c> is
/// guaranteed false regardless of any state the test character
/// accumulates between handshake and forfeit send. Deterministic
/// without seeding mission data.
/// </para>
///
/// <para>
/// Why <c>forfeit_pressed=true</c> and not the sibling 0x0087
/// MISSION_DISMISSAL. HandleMissionDismissal calls
/// <c>MissionDismiss(MissionID, false)</c>. With forfeit_pressed=false
/// the boolean reduces to <c>(!false || ...) = true</c> and the if
/// branch fires <c>RemoveMission(mission_slot)</c> instead of the
/// SendVaMessageC reply. RemoveMission on an empty slot has uncertain
/// side-effect surface — may or may not emit, may touch
/// <c>m_AssignedMissions</c> or fire mission-update auxes downstream.
/// MISSION_FORFEIT's else-branch is the strictly cleaner direct-reply
/// candidate: no array dereference past the slot index itself, no
/// state mutation, single deterministic frame out.
/// </para>
///
/// <para>
/// No post-emit side effects (per the Wave 29 methodology
/// refinement). The else branch is the LAST statement in
/// MissionDismiss's nested if/else; both branches return from
/// MissionDismiss, MissionDismiss returns from HandleMissionForfeit,
/// HandleMissionForfeit returns from the dispatcher. No conditional
/// first-time blocks, no shared-state mutation past the emit, no
/// fan-out to nearby observers. This is the same safe shape as Wave
/// 27 (INVENTORY_MOVE default arm) — handler runs to completion in
/// a bounded number of statements with no recursion into other
/// subsystems.
/// </para>
///
/// <para>
/// Direct-reply assertion vs. survival probe. The else branch
/// unconditionally emits a single 0x001D MESSAGE_STRING frame with a
/// deterministic substring — same direct-reply shape as Wave 24
/// (<see cref="SectorItemStateTests"/>), Wave 26
/// (<see cref="SectorSkillAbilityTests"/>), Wave 27
/// (<see cref="SectorInventoryMoveTests"/>). Strictly stronger than
/// Wave 29's PETITION_STUCK survival probe.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>MissionDismissal struct long-revert in
///     PacketStructures.h:1001.</b> Currently 2× int32_t = 8B
///     canonical. Widening either field to <c>long</c> on Linux
///     x86_64 (sizeof(long)==8) grows the struct — MissionID would
///     read from offset 8 (past end of 8-byte payload) into
///     uninitialised stack memory and ntohl that garbage. The
///     result might still happen to be in <c>[0..12)</c> and pass,
///     but more likely lands outside the range and the if-guard
///     filters it out → no reply → test times out → regression
///     caught.
///   </item>
///   <item>
///     <b>ntohl/htonl byte-order flip on MissionID.</b> The handler
///     ntohl-decodes both fields. Sending MissionID=0 BE is
///     <c>00 00 00 00</c> which is identical under any byte-order
///     interpretation — by itself this payload doesn't catch a
///     ntohl drop. A future variant test sending MissionID=1 BE
///     (host-LE would read 0x01000000 = 16_777_216, outside
///     <c>[0..12)</c>, filter-rejected) would catch it; for Wave 30
///     the deterministic-empty-slot constraint takes priority over
///     byte-order probing.
///   </item>
///   <item>
///     <b>Dispatcher mis-route at server/src/PlayerConnection.cpp:551.</b>
///     Case label sits between 0x0084 RECUSTOMIZE_AVATAR_DONE and
///     0x0087 MISSION_DISMISSAL. A copy-paste swap with
///     HandleMissionDismissal would route our payload through
///     <c>forfeit_pressed=false</c> instead of true and produce a
///     RemoveMission call on slot 0 of an empty mission table —
///     either silent no-op (test times out) or a state-emitting
///     side effect that doesn't match our substring filter (also
///     test times out).
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode regression.</b>
///     0x0086 is NOT explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp:387-518</c>, so it
///     hits the <c>default:</c> arm and falls through to the
///     bottom-of-switch ForwardClientOpcode at line 524. A
///     regression dropping this opcode would surface as a timeout
///     waiting for the MESSAGE_STRING reply.
///   </item>
///   <item>
///     <b>HandleMissionForfeit guard removal.</b> The
///     <c>MissionID &gt;= 0 &amp;&amp; MissionID &lt; 12</c> filter
///     at PlayerConnection.cpp:11015 is what gates the call to
///     MissionDismiss. Removing it would allow out-of-range
///     MissionIDs to reach MissionDismiss, whose own guard then
///     no-ops them — by itself no regression, but the surrounding
///     change might also disrupt the else-branch reply path. Test
///     pins the reply substring, so any wording change in the
///     SendVaMessageC literal also surfaces here.
///   </item>
///   <item>
///     <b>AuxMission::Init IsForfeitable default flip.</b> The
///     <c>Data-&gt;IsForfeitable = 0;</c> at AuxMission.h:85 is what
///     makes a fresh slot return false from GetIsForfeitable. If a
///     refactor flips the default to true, the if branch fires
///     RemoveMission instead and the test times out waiting for
///     the substring. Catches that initial-state regression.
///   </item>
///   <item>
///     <b>0x001D MESSAGE_STRING SendOpcode width regression.</b>
///     Same SendOpcode→m_UDPQueue→SendPacketCache→0x2016
///     PACKET_SEQUENCE→proxy SendClientPacketSequence→TCP fan-out
///     path as Waves 8/24/27. The Phase K sizeof(int32_t)
///     opcode-header fix at PlayerConnection.cpp:127 keeps the
///     per-client UDP queue header at the canonical 4-byte width —
///     a revert would shift the reply opcode bytes and break TCP
///     framing.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x0086 MISSION_FORFEIT is
/// what the retail Win32 client emits when the user clicks the
/// Forfeit button in the mission-tracker UI. The retail server's
/// HandleMissionForfeit has the same else branch with the same
/// verbatim error string — preservation-grade fidelity, not a
/// fabrication. The else branch is strictly defensive: it tells
/// the user "you can't forfeit this mission" when the slot's
/// IsForfeitable flag is false. We are not making the server
/// accept any new input shape, not loosening any security posture,
/// not fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; MISSION_FORFEIT + reply round-trip
/// is sub-second (single client→server frame in, single
/// server→client frame out).
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorMissionForfeitTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorMissionForfeitTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task MissionForfeit_EmptySlotZero_ReceivesNonForfeitableErrorString()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Forfeitor", shipName: "ForfeitorShip", cts.Token);

        try
        {
            // 0x0086 MISSION_FORFEIT — 8B canonical payload. Wire is
            // BIG-ENDIAN (HandleMissionForfeit at PlayerConnection
            // .cpp:11012-11013 ntohl-decodes both fields).
            //   [0..4) int32 PlayerID  = 0 (handler ignores; only
            //                            MissionID is used for the
            //                            range check + slot index)
            //   [4..8) int32 MissionID = 0 (empty slot 0)
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.MissionForfeit.Value, payload),
                cts.Token);

            // Drain inbound until we see a 0x001D MESSAGE_STRING whose
            // body contains "non forfeitable". Post-handshake the server
            // may interleave other in-sector fan-out (NPC chatter, state
            // updates, etc.); a frame cap keeps a stalled pipeline from
            // masquerading as the outer-CTS timeout.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.MessageString.Value)
                    continue;

                // 0x001D wire layout (mirror of Player::SendMessageString
                // at server/src/PlayerConnection.cpp:10974):
                //   [0..2) short length    (LE; strlen(msg)+1)
                //   [2]    char  color     (17 for this reply)
                //   [3..]  msg + '\0'
                var span = reply.Payload.Span;
                if (span.Length < 4) continue;

                int nulIdx = span[3..].IndexOf((byte)0);
                if (nulIdx < 0) continue;

                string body = Encoding.ASCII.GetString(span.Slice(3, nulIdx));

                // Filter — other MESSAGE_STRING frames (e.g. login
                // chatter) may arrive first. Keep draining until we see
                // the one keyed by our MISSION_FORFEIT.
                if (!body.Contains("non forfeitable", StringComparison.Ordinal))
                    continue;

                // Pin on the distinctive substring rather than the
                // whole string so punctuation/casing tweaks don't sink
                // the test. Full literal at PlayerMissions.cpp:1630:
                // "This mission is non forfeitable."
                Assert.Contains("non forfeitable", body);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0086 MISSION_FORFEIT (MissionID=0) " +
                $"without seeing 0x001D MESSAGE_STRING containing \"non forfeitable\". " +
                $"Likely the server's HandleMissionForfeit / MissionDismiss else-branch " +
                $"SendVaMessageC path broke, the proxy default-case forwarding dropped the opcode, " +
                $"the dispatcher case at PlayerConnection.cpp:551 got mis-routed, " +
                $"or AuxMission's default IsForfeitable flipped to true.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// The verbatim ASCII body the retail-faithful MissionDismiss
    /// non-forfeitable branch passes to <c>SendVaMessageC</c> at
    /// <c>server/src/PlayerMissions.cpp:1630</c>. 32 bytes of payload
    /// content; <c>SendMessageString</c> appends a NUL terminator and
    /// emits <c>length = 33</c>.
    /// </summary>
    private const string NonForfeitableLiteral =
        "This mission is non forfeitable.";

    /// <summary>
    /// Wave 111 frame-shape hardening (+0 ratchet, 0x001D): pins the
    /// byte-exact 36-byte wire-shape of the single 0x001D MESSAGE_STRING
    /// the server emits in reply to a 0x0086 MISSION_FORFEIT whose
    /// MissionID points at an empty slot. Wave 27's existing test
    /// (<see cref="MissionForfeit_EmptySlotZero_ReceivesNonForfeitableErrorString"/>)
    /// asserts only that the response body <em>contains</em> the
    /// distinctive substring "non forfeitable"; Wave 111 layers byte-exact
    /// pinning on top, locking the full 36-byte response shape in place.
    ///
    /// <para>
    /// Backstory. 0x001D MESSAGE_STRING is server-emitted by
    /// <c>Player::SendMessageString</c> at
    /// <c>server/src/PlayerConnection.cpp:10987-10997</c>:
    /// <code>
    ///     short length = strlen(msg) + 1;          // includes NUL
    ///     *((short *) &amp;buffer[0]) = length;       // wire offset 0..2 (LE)
    ///     buffer[2]                  = color;       // wire offset 2  (u8)
    ///     strcpy_s(&amp;buffer[3], ..., msg);          // wire offset 3..(3+length)
    ///     SendOpcode(ENB_OPCODE_001D_MESSAGE_STRING, buffer, length + 3);
    /// </code>
    /// Unlike Waves 108/109/110, MissionDismiss uses
    /// <c>SendVaMessageC(17, ...)</c> (PlayerClass.cpp:3443-3453), which
    /// passes color=17 EXPLICITLY to <c>SendMessageString(pch, colour)</c>
    /// rather than letting the default fire. For the verbatim 32-byte
    /// literal "This mission is non forfeitable." at PlayerMissions.cpp:1630:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>length field</b> = strlen(32) + 1 = <c>33</c></item>
    ///   <item><b>color byte</b> = <c>17</c> (SendVaMessageC explicit override)</item>
    ///   <item><b>msg + NUL</b> = 32 + 1 = <c>33 bytes</c></item>
    ///   <item><b>total payload</b> = <c>length + 3 = 36 bytes</c></item>
    /// </list>
    ///
    /// <para>
    /// Why a separate test method. Mirrors the Wave 108/109/110 split:
    /// Wave 27's looser substring assertion stays intact (narrow-scope
    /// failure surface — a wire-shape drift that still produces the
    /// literal substring would not surface as a Wave 27 failure),
    /// Wave 111 adds the byte-exact pin as its own discrete test artifact
    /// for the regression-class catalogue. Wave 111 is the FIRST byte-exact
    /// hardening wave on the <c>SendVaMessageC</c> (explicit-color) fan-out
    /// — Waves 108/109/110 all pinned the <c>SendVaMessage</c> /
    /// default-color=5 path. That makes Wave 111 a complementary catching
    /// surface: a regression to <c>SendVaMessageC</c>'s color-pass-through
    /// (e.g. discarding the colour arg on its way to SendMessageString)
    /// would surface here but NOT on Waves 108/109/110 (and vice versa).
    /// </para>
    ///
    /// <para>
    /// Regression classes Wave 111 catches beyond what Wave 27 catches.
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b><c>SendMessageString</c> length-field width regression at
    ///     <c>PlayerConnection.cpp:10992</c>.</b> The cast
    ///     <c>*((short *) &amp;buffer[0]) = length</c> writes a 2-byte
    ///     length prefix. A regression to <c>int32_t</c> would shift the
    ///     color byte from offset 2 to offset 4 and grow the total
    ///     payload from 36 to 38 bytes. Wave 27's loose-shape parse
    ///     still passes; <c>Assert.Equal(36, span.Length)</c> catches.
    ///   </item>
    ///   <item>
    ///     <b><c>SendVaMessageC</c> color-pass-through regression at
    ///     <c>PlayerClass.cpp:3443-3453</c>.</b> The function signature
    ///     is <c>SendVaMessageC(char colour, char *string, ...)</c> and
    ///     it MUST forward <c>colour</c> to
    ///     <c>SendMessageString(pch, colour)</c>. A refactor that drops
    ///     the second argument (defaulting back to 5) or hardcodes a
    ///     different value would change wire byte 2 from 17 without
    ///     affecting the substring. Wave 27's text-only assert is
    ///     structurally blind; Wave 111 pins <c>span[2] == 17</c>.
    ///   </item>
    ///   <item>
    ///     <b>Length-field LE byte-order regression.</b> Same
    ///     <c>*((short *)&amp;buffer[0])</c> host-LE write as Waves 108/110.
    ///     Wave 111's
    ///     <c>BinaryPrimitives.ReadInt16LittleEndian == 33</c> catches.
    ///   </item>
    ///   <item>
    ///     <b><c>SendOpcode</c> trailing-bytes regression at
    ///     <c>PlayerConnection.cpp:10996</c>.</b> The third arg
    ///     <c>length + 3</c> bounds the emit to 36 bytes; a regression
    ///     to <c>sizeof(buffer)</c> (512) would leak 476 trailing zero
    ///     bytes. Wave 27's substring assertion still passes; Wave 111's
    ///     <c>Assert.Equal(36, span.Length)</c> catches.
    ///   </item>
    ///   <item>
    ///     <b>Verbatim-literal drift at
    ///     <c>PlayerMissions.cpp:1630</c>.</b> A refactor that
    ///     changes the casing of "non" / "forfeitable", drops the
    ///     trailing period, or swaps to "cannot be forfeited" would
    ///     silently shift wire bytes the retail Win32 client's decoder
    ///     was compiled to accept. Wave 27's <c>Contains</c> would still
    ///     pass on minor casing tweaks (it's case-sensitive but the
    ///     check is on the lowercase form already); Wave 111's full-
    ///     literal <c>Assert.Equal</c> on the body bytes catches.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Per CLAUDE.md server-integrity. 0x001D MESSAGE_STRING is
    /// server-originated. Wave 111 adds no client stimulus beyond the
    /// same 8-byte MISSION_FORFEIT (PlayerID=0, MissionID=0) Wave 27
    /// already sends, and no server change — pure passive-observation
    /// tightening of a retail-faithful wire shape. The 36-byte response
    /// is exactly what the retail Win32 client's MESSAGE_STRING decoder
    /// was compiled to receive. No widened input acceptance, no loosened
    /// gating, no fabricated replies — server-integrity POSITIVE.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s. Handshake ~2s; FORFEIT+REPLY round-trip is
    /// sub-second.
    /// </para>
    /// </summary>
    [Fact]
    public async Task MissionForfeit_EmptySlotZero_PinsExactReplyWireShape()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        // length-prefix u16 (2) + color u8 (1) + body+NUL (33) = 36 bytes.
        const int ExpectedReplyPayloadLength = 36;
        // strlen(literal) + 1 NUL = 33.
        const short ExpectedReplyLengthField = 33;
        // SendVaMessageC(17, ...) → SendMessageString(pch, 17) — explicit
        // override of the default color=5; first wave to pin this path.
        const byte ExpectedReplyColor = 17;
        // strlen(literal) = 32.
        const int ExpectedLiteralByteCount = 32;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Frftor11", shipName: "Frftor11Ship", cts.Token);

        try
        {
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.MissionForfeit.Value, payload),
                cts.Token);

            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.MessageString.Value)
                    continue;

                var span = reply.Payload.Span;
                if (span.Length < 4) continue;

                short msgLen = BinaryPrimitives.ReadInt16LittleEndian(span[..2]);
                if (msgLen < 1) continue;

                int bodyBytes = Math.Min(msgLen - 1, span.Length - 3);
                if (bodyBytes <= 0) continue;

                string text = Encoding.ASCII.GetString(span.Slice(3, bodyBytes));

                // Filter on the distinctive substring so other
                // MESSAGE_STRING traffic (motd, NPC chatter) doesn't
                // race ahead of the non-forfeitable reply.
                if (!text.Contains("non forfeitable", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);
                Assert.Equal(ExpectedReplyLengthField, msgLen);
                Assert.Equal(ExpectedReplyColor, span[2]);

                int literalEnd = 3 + ExpectedLiteralByteCount;
                string fullBody = Encoding.ASCII.GetString(
                    span.Slice(3, ExpectedLiteralByteCount));
                Assert.Equal(NonForfeitableLiteral, fullBody);
                Assert.Equal((byte)0x00, span[literalEnd]);  // NUL terminator
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0086 MISSION_FORFEIT (MissionID=0) " +
                $"without seeing 0x001D MESSAGE_STRING containing \"non forfeitable\". " +
                $"Same drain-loop budget as Wave 27's sibling test; the failure modes are " +
                $"identical.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
