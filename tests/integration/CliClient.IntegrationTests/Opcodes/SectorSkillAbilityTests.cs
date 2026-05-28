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
/// Wave 26 direct-reply round-trip: client sends a 12-byte 0x0058
/// SKILL_ABILITY with an <c>AbilityIndex</c> outside the populated
/// <c>m_AbilityList[MAX_ABILITY_IDS]</c> range, expects the server's
/// "not yet working" priority-message back as a 0x0020 PRIORITY_MESSAGE.
///
/// <para>
/// Wire layout (mirror of <c>common/include/net7/PacketStructures.h:994</c>):
/// <code>
///   [0..4)  int32  GameID
///   [4..8)  int32  Action
///   [8..12) int32  AbilityIndex
/// </code>
/// 12 bytes total; struct is <c>ATTRIB_PACKED</c> so there's no
/// implicit padding. Handler reads <c>Action-&gt;AbilityIndex</c>
/// without ntohl — host byte order (LE on x86_64).
/// </para>
///
/// <para>
/// Server handler. <c>Player::HandleSkillAbility</c>
/// (<c>server/src/PlayerAbilitys.cpp:23</c>) reinterprets the payload
/// as a <c>SkillUse *</c>, calls <c>ConvertAbilityToBaseSkill</c>
/// (safe: <c>SkillsDatabaseSQL.cpp:107</c> std::map insert of nullptr
/// for unknown key is a no-op), then bounds-checks:
/// <code>
///     if (Action-&gt;AbilityIndex &lt; MAX_ABILITY_IDS &amp;&amp;
///         Action-&gt;AbilityIndex &gt;= 0 &amp;&amp;
///         m_AbilityList[Action-&gt;AbilityIndex])
///     {
///         // mutation branch: SetCurrentSkill + Use → ability-specific reply
///     }
///     else
///     {
///         SendPriorityMessageString(
///             "Error: This ability is not yet working. Try later!",
///             "MessageLine", 1000, 4);
///     }
/// </code>
/// <c>MAX_ABILITY_IDS</c>=138 (<c>server/src/PlayerSkills.h:275</c>),
/// so any index &gt;= 138 fails the upper bound and the else-branch
/// unconditionally emits 0x0020 PRIORITY_MESSAGE. Sending
/// AbilityIndex=200 picks an index well clear of any valid ability
/// slot — even on a character where every ability slot is populated,
/// 200 would still trip the bound.
/// </para>
///
/// <para>
/// Why <c>AbilityIndex=200</c>. The else-branch is reached for either
/// out-of-range index OR for in-range index with a null
/// <c>m_AbilityList[index]</c> slot. The latter is character-state-
/// dependent (a freshly-created Terran Warrior populates only a
/// subset of slots) — using an out-of-range index is the deterministic
/// path that works for any character. 200 is also large enough to
/// detect a struct widening regression: if SkillUse grew to 24B on
/// Linux x86_64 (e.g. a long-revert of any field) the AbilityIndex
/// read would land in receive-buffer slack and likely produce a
/// smaller value that might accidentally hit a populated slot and
/// dereference into <c>AbilityBase::Use</c> — a clear failure mode.
/// </para>
///
/// <para>
/// Reply wire layout. <c>Player::SendPriorityMessageString</c>
/// (<c>server/src/PlayerConnection.cpp:10986</c>) builds:
/// <code>
///   AddDataSN(msg1)          // string + '\0'
///   AddDataSN(msg2)          // string + '\0'
///   AddData&lt;long&gt;(time)      // 4B LE (Wave 12 template specialisation)
///   AddData&lt;long&gt;(priority)  // 4B LE
///   SendOpcode(0x0020, ...)
/// </code>
/// SendOpcode is per-client (queues into the originator's UDP queue
/// directly) — NOT SendToSector — so there's no login-stage race like
/// Wave 25's STARBASE_ROOM_CHANGE fan-out. Sector bitmap state is
/// irrelevant for this path.
/// </para>
///
/// <para>
/// Direct-reply assertion vs. survival probe. The else-branch is
/// unconditional and emits a single 0x0020 frame with a deterministic
/// substring — same direct-reply shape as Wave 24
/// (<see cref="SectorItemStateTests"/>) and stronger than a survival
/// probe.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>SkillUse struct long-revert in PacketStructures.h:994.</b>
///     Currently 3× int32_t = 12B canonical. If anyone widens any
///     field to <c>long</c> the struct grows on Linux x86_64
///     (sizeof(long)==8) and AbilityIndex reads from offset 16 past
///     the 12B wire payload end into receive-buffer slack. The garbage
///     value would likely fall under 138 and hit a populated slot,
///     dereferencing <c>AbilityBase::Use</c> on a real ability with a
///     wire-supplied junk TargetID — likely SEGV.
///   </item>
///   <item>
///     <b>Dispatcher mis-route at server/src/PlayerConnection.cpp:503.</b>
///     Case label sits between 0x0057 SKILL_UP and 0x005A VERB_REQUEST
///     in the hand-maintained ~200-entry switch. A copy-paste swap
///     with HandleSkillAction (Wave 17's 10B SkillAction struct) would
///     mis-interpret the 12B SkillUse — reading SkillID from the
///     wrong offset and either crashing on a >= 64 SkillID (the Wave
///     17 trap) or silently succeeding without emitting the expected
///     PRIORITY_MESSAGE.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode regression.</b>
///     0x0058 is NOT explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>, so it hits the
///     <c>default:</c> arm and falls through to the bottom-of-switch
///     forward. A regression dropping this opcode would surface as a
///     timeout waiting for the PRIORITY_MESSAGE reply.
///   </item>
///   <item>
///     <b><c>AddData&lt;long&gt;</c> Wave 12 template specialisation
///     regression.</b> The reply payload's time / priority fields ride
///     <c>PacketMethods.h:37-49</c>'s int32_t cast. A revert to a
///     plain 8-byte long emit would shift the parse: the SN strings
///     are emitted first so their lengths are unaffected, but a
///     consumer that parses time/priority would see misaligned values.
///     We only assert on the leading msg1 substring so this regression
///     wouldn't directly trip THIS test — but the buffer overruns
///     <c>buffer[512]</c> only at much larger sizes, so the reply is
///     still emitted and the substring still pins.
///   </item>
///   <item>
///     <b>The <c>__try/__except</c> → <c>try/catch</c> Linux shim
///     regression.</b> <c>server/src/Net7.h:307-308</c> maps Win32
///     SEH macros to C++ exceptions on Linux. The else-branch sits
///     OUTSIDE the __try block so an accidental brace mis-nesting that
///     pulled it inside would still emit on success — but a SEH→try
///     shim regression (e.g. swallowing all exceptions silently
///     including bad casts) is partly probed by this path.
///   </item>
///   <item>
///     <b>MAX_ABILITY_IDS bounds check inversion.</b> A regression
///     that flipped <c>&lt; MAX_ABILITY_IDS</c> to <c>&gt; 0</c> or
///     dropped the bound entirely would let AbilityIndex=200 enter the
///     mutation branch and SEGV on m_AbilityList[200] (the array is
///     sized exactly to MAX_ABILITY_IDS=138 at
///     <c>server/src/CMobClass.h:169</c>).
///   </item>
///   <item>
///     <b>Server→client 0x0020 fan-out path regression.</b> The reply
///     rides SendOpcode → m_UDPQueue → SendPacketCache → 0x2016
///     PACKET_SEQUENCE wrapper → proxy SendClientPacketSequence → TCP.
///     Distinct from MESSAGE_STRING because the opcode differs in the
///     wrapper's first two bytes — catches a regression where the
///     proxy's UDP→TCP forward whitelists only specific server-emit
///     opcodes.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x0058 SKILL_ABILITY is
/// what the retail Win32 client emits when the user activates a skill
/// ability from the ability toolbar. The retail server's
/// HandleSkillAbility uses the exact same bounds check + else-branch
/// emit; the "Error: This ability is not yet working. Try later!"
/// string is the verbatim retail-server error message for an ability
/// index outside the populated AbilityList — preservation-grade
/// fidelity, not a fabrication. We are not making the server accept
/// any new input shape, not loosening any security posture, not
/// fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; SKILL_ABILITY+REPLY round-trip is
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorSkillAbilityTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorSkillAbilityTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task SkillAbility_OnUnknownAbilityIndex_ReceivesPriorityMessageNotYetWorking()
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
            firstName: "Skillet", shipName: "SkillShip", cts.Token);

        try
        {
            // 0x0058 SKILL_ABILITY — 12B canonical payload with
            // AbilityIndex=200 (>= MAX_ABILITY_IDS=138 → else-branch).
            //   [0..4)   int32 GameID       = 0
            //   [4..8)   int32 Action       = 0
            //   [8..12)  int32 AbilityIndex = 200
            byte[] payload = new byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 200);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.SkillAbility.Value, payload),
                cts.Token);

            // Drain inbound until we see a 0x0020 PRIORITY_MESSAGE
            // whose first AddDataSN string contains "not yet working".
            // Post-handshake the server may interleave other in-sector
            // fan-out (NPC chatter, state updates, etc.); a frame cap
            // keeps a stalled pipeline from masquerading as the outer-
            // CTS timeout.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.PriorityMessage.Value)
                    continue;

                // 0x0020 wire layout (mirror of Player::SendPriorityMessageString
                // at server/src/PlayerConnection.cpp:10986):
                //   AddDataSN(msg1)          // string + '\0'
                //   AddDataSN(msg2)          // string + '\0'
                //   AddData<long>(time)      // 4B LE
                //   AddData<long>(priority)  // 4B LE
                var span = reply.Payload.Span;
                Assert.True(span.Length >= 10,
                    $"PRIORITY_MESSAGE payload too short: {span.Length}B " +
                    $"(needs at least 2× '\\0' + 8B time/priority).");

                // Find the first NUL terminator — that bounds msg1.
                int nulIdx = span.IndexOf((byte)0);
                if (nulIdx < 0) continue;

                string msg1 = Encoding.ASCII.GetString(span[..nulIdx]);

                // Filter — other PRIORITY_MESSAGE frames may arrive
                // first. Keep draining until we see the one keyed by
                // our SKILL_ABILITY.
                if (!msg1.Contains("not yet working", StringComparison.Ordinal))
                    continue;

                // Pin on the distinctive substring rather than the
                // whole string so punctuation tweaks don't sink the
                // test. Full literal at PlayerAbilitys.cpp:72:
                // "Error: This ability is not yet working. Try later!"
                Assert.Contains("not yet working", msg1);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0058 SKILL_ABILITY (AbilityIndex=200) " +
                $"without seeing 0x0020 PRIORITY_MESSAGE containing \"not yet working\". " +
                $"Likely the server's HandleSkillAbility else-branch SendPriorityMessageString path broke, " +
                $"the proxy default-case forwarding dropped the opcode, " +
                $"the dispatcher case at PlayerConnection.cpp:503 got mis-routed, " +
                $"or SkillUse struct was widened past 12B and AbilityIndex landed in receive-buffer slack.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 112 sibling byte-exact pin on the same 0x0058 SKILL_ABILITY
    /// → 0x0020 PRIORITY_MESSAGE direct-reply path probed by
    /// <see cref="SkillAbility_OnUnknownAbilityIndex_ReceivesPriorityMessageNotYetWorking"/>,
    /// but asserting the FULL reply payload byte-for-byte rather than
    /// just the substring "not yet working".
    ///
    /// <para>
    /// FIRST byte-exact wave on the <c>SendPriorityMessageString</c>
    /// framing path. The 0x0020 PRIORITY_MESSAGE wire shape differs
    /// from the SendMessageString-based 0x001D MESSAGE_STRING that
    /// Waves 108–111 pin: SendPriorityMessageString builds two
    /// AddDataSN-emitted null-terminated strings followed by two
    /// AddData&lt;long&gt; 4-byte LE ints (PacketMethods.h:37-42
    /// template specialisation forces 4-byte emission on Linux x86_64
    /// where sizeof(long)==8). No length prefix, no colour byte —
    /// completely different framing pattern, complementary regression
    /// coverage to the SendMessageString path.
    /// </para>
    ///
    /// <para>
    /// Reply layout (mirror of <c>Player::SendPriorityMessageString</c>
    /// at <c>server/src/PlayerConnection.cpp:10999</c>):
    /// <code>
    ///   [0..50)   "Error: This ability is not yet working. Try later!"
    ///   [50]      0x00 (msg1 NUL terminator)
    ///   [51..62)  "MessageLine"
    ///   [62]      0x00 (msg2 NUL terminator)
    ///   [63..67)  int32 time     = 1000 (0xE8 0x03 0x00 0x00 LE)
    ///   [67..71)  int32 priority = 4    (0x04 0x00 0x00 0x00 LE)
    /// </code>
    /// 71 bytes total. msg1=50 bytes, msg2=11 bytes.
    /// </para>
    ///
    /// <para>
    /// Concrete regressions THIS sibling catches that the substring
    /// probe above does NOT:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Frame-tail size drift.</b> Any change that widens
    ///     time/priority back to plain <c>long</c> (8B on Linux) would
    ///     push the payload to 79 bytes and the strict 71-byte length
    ///     assertion below trips immediately. The substring probe
    ///     above ignores everything past msg1's NUL and would silently
    ///     accept the regression.
    ///   </item>
    ///   <item>
    ///     <b>msg1 punctuation / typo drift.</b> A copy-edit shaving
    ///     the trailing "!" or swapping "Try later" → "try later" in
    ///     PlayerAbilitys.cpp:73 leaves the substring "not yet working"
    ///     intact but the byte-exact pin trips.
    ///   </item>
    ///   <item>
    ///     <b>msg2 label drift.</b> The sentinel "MessageLine" string
    ///     is unrelated to the substring probe but pins the exact bytes
    ///     here — a refactor to "Message Line" or "MessageLine\0" empty-
    ///     extend would trip immediately.
    ///   </item>
    ///   <item>
    ///     <b>time/priority value swap.</b> A regression that swapped
    ///     SendPriorityMessageString's (time, priority) argument order
    ///     in the PlayerAbilitys.cpp:73 callsite would emit
    ///     priority=1000, time=4 — both fields still present, same
    ///     overall payload size, msg1 still "not yet working" → the
    ///     substring probe above passes, this sibling trips.
    ///   </item>
    ///   <item>
    ///     <b>AddData&lt;long&gt; 4-vs-8-byte revert.</b> Wave 12's
    ///     template specialisation at <c>PacketMethods.h:37-42</c> is
    ///     what forces 4-byte int32_t emission on Linux x86_64. A
    ///     revert to plain <c>long</c> changes the payload tail size
    ///     immediately — this sibling's 71-byte length assertion
    ///     catches it.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity note. No new server behaviour, no loosening
    /// of input acceptance. We pin the byte-exact wire shape the
    /// retail server has always emitted on this code path.
    /// </para>
    ///
    /// <para>
    /// Budget: 90s; same handshake / round-trip cost as the sibling
    /// substring probe.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SkillAbility_OnUnknownAbilityIndex_PinsExactReplyWireShape()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        const string ExpectedMsg1 = "Error: This ability is not yet working. Try later!";
        const string ExpectedMsg2 = "MessageLine";
        const int ExpectedTime = 1000;
        const int ExpectedPriority = 4;
        const int ExpectedMsg1ByteCount = 50;
        const int ExpectedMsg2ByteCount = 11;
        // msg1(50) + NUL + msg2(11) + NUL + time(4) + priority(4) = 71
        const int ExpectedReplyPayloadLength = 71;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Skilex12", shipName: "SkillShip12", cts.Token);

        try
        {
            byte[] payload = new byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 200);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.SkillAbility.Value, payload),
                cts.Token);

            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.PriorityMessage.Value)
                    continue;

                var span = reply.Payload.Span;
                if (span.Length < ExpectedMsg1ByteCount + 1)
                    continue;

                int firstNul = span.IndexOf((byte)0);
                if (firstNul < 0) continue;

                string msg1Preview = Encoding.ASCII.GetString(span[..firstNul]);
                if (!msg1Preview.Contains("not yet working", StringComparison.Ordinal))
                    continue;

                Assert.Equal(ExpectedReplyPayloadLength, span.Length);

                Assert.Equal(ExpectedMsg1ByteCount, firstNul);
                Assert.Equal(ExpectedMsg1, Encoding.ASCII.GetString(span[..ExpectedMsg1ByteCount]));
                Assert.Equal((byte)0, span[ExpectedMsg1ByteCount]);

                int msg2Start = ExpectedMsg1ByteCount + 1;
                int msg2End = msg2Start + ExpectedMsg2ByteCount;
                Assert.Equal(
                    ExpectedMsg2,
                    Encoding.ASCII.GetString(span.Slice(msg2Start, ExpectedMsg2ByteCount)));
                Assert.Equal((byte)0, span[msg2End]);

                int timeStart = msg2End + 1;
                int priorityStart = timeStart + 4;
                Assert.Equal(
                    ExpectedTime,
                    BinaryPrimitives.ReadInt32LittleEndian(span.Slice(timeStart, 4)));
                Assert.Equal(
                    ExpectedPriority,
                    BinaryPrimitives.ReadInt32LittleEndian(span.Slice(priorityStart, 4)));
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0058 SKILL_ABILITY (AbilityIndex=200) " +
                $"without seeing 0x0020 PRIORITY_MESSAGE containing \"not yet working\" for byte-exact pin.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
