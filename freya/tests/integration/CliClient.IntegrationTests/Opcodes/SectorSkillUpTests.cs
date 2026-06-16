// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 17 post-handshake survival round-trip: client sends 0x0057
/// SKILL_UP (the wire frame the retail Win32 client emits when the
/// user clicks "+" on a skill in the skill tree to spend a skill
/// point), then verifies the connection survives via a 0x0044
/// REQUEST_TIME round-trip.
///
/// <para>
/// Why survival probe rather than direct reply assertion.
/// <c>Player::HandleSkillAction</c> (<c>server/src/PlayerSkills.cpp:97</c>)
/// has three early-return guards before any state mutation:
/// (1) <c>SkillLevel == SkillMaxLevel</c> (already maxed);
/// (2) insufficient skill points;
/// (3) <c>SkillLevelRequirement &gt; 0</c> (prereq skill not high enough).
/// For a SkillID that is valid in the <c>AuxSkill</c> wrapper array but
/// has no class entry for the player's profession (so MaxSkillLevel
/// stays at the <c>AuxSkill::Init</c> default of 0), the first guard
/// trips because <c>0 == 0</c> and the handler returns silently — no
/// DB write, no AuxPlayer/AuxShip refresh, no reply. Pipe survival is
/// the only assertable post-condition. Per CLAUDE.md server-integrity
/// we don't fabricate a reply.
/// </para>
///
/// <para>
/// SkillID choice — and the trap we are avoiding. The dispatcher
/// indexes <c>m_PlayerIndex.RPGInfo.Skills.Skill[Action-&gt;SkillID]</c>.
/// <c>RPGInfo.Skills</c> is <c>class AuxSkills</c>
/// (<c>server/src/AuxClasses/AuxRPGInfo.h:134</c>), whose
/// <c>Skill</c> member is <c>AuxSkill Skill[64]</c>
/// (<c>server/src/AuxClasses/AuxSkills.h:86</c>) — 64 entries, not
/// 170. The raw <c>_Skills::Skill[170]</c> data array exists separately
/// but the handler reads through the wrapper. Any SkillID &gt;= 64
/// dereferences past the array end into Player-object memory, reads a
/// garbage <c>Data</c> pointer, and crashes the sector thread on
/// <c>GetAvailability()</c>. (Earlier drafts of this test sent
/// SkillID=169 reasoning from <c>_Skills::Skill[170]</c>; the server
/// faulted and the docker compose health-restarted it — see
/// plans/99-decisions-log.md 2026-05-25.)
/// </para>
///
/// <para>
/// We pick SkillID=29 SKILL_JENQUAI_CULTURE. Per the seeded <c>skills</c>
/// table, <c>warrior_max_level = -1</c> for that row, so the
/// per-profession loop in <c>PlayerSaves::LoadPlayer</c>
/// (<c>server/src/PlayerSaves.cpp:609-647</c>) skips the entry —
/// <c>Skills[29].ClassType[0].MaxLevel</c> is not &gt; 0 — and
/// <c>RPGInfo.Skills.Skill[29]</c> stays at <c>AuxSkill::Init</c>
/// defaults: Level=0, MaxSkillLevel=0. First early-return fires.
/// avatar_skill_levels has no row for a freshly-created character
/// either, so the post-class skill-row loop doesn't overwrite it.
/// </para>
///
/// <para>
/// Concrete regression class this catches: SkillAction is
/// <c>{int32_t GameID; int SkillPoints; short SkillID;}</c> = 10B
/// canonical (<c>common/include/net7/PacketStructures.h:987</c>). The
/// <c>int</c> in the middle is 4B on both Win32 and Linux x86_64, so
/// the struct width is identical on both. But if anyone reverts the
/// Phase R sweep on this struct and changes <c>int</c> or <c>int32_t</c>
/// to <c>long</c>, the struct grows from 10B to 14B on Linux x86_64
/// and SkillID would read from byte 12 (instead of 8), past the end
/// of the 10B wire payload, into undefined memory. A garbage SkillID
/// would index a random AuxSkill slot — at minimum corrupting state
/// on the wrong skill, at worst (SkillID &gt;= 64) crashing the
/// sector thread on the GetAvailability dereference past
/// AuxSkills::Skill[64].
/// </para>
///
/// <para>
/// Other bugs this test would also catch:
/// </para>
/// <list type="bullet">
///   <item>
///     Proxy default-case <c>ForwardClientOpcode</c> regression.
///     SKILL_UP is not explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>, so it hits the
///     <c>default:</c> arm at line 514 and falls through to the
///     bottom-of-switch <c>ForwardClientOpcode</c>. A regression that
///     <c>return</c>ed early or that added an empty hand-coded case
///     that returned would silently drop the opcode.
///   </item>
///   <item>
///     <c>m_Mutex</c> deadlock in HandleSkillAction's mutation path.
///     The early-return path we exercise doesn't take the mutex, but
///     a regression that moved the lock above the early-return checks
///     would interact with concurrent Aux refresh callers in a way
///     the original code carefully avoided.
///   </item>
///   <item>
///     Dispatch mis-route. The case label at
///     <c>server/src/PlayerConnection.cpp:499</c> is hand-maintained
///     in a ~200-entry switch; a copy-paste error could route 0x0057
///     to a different handler that crashes on the 10-byte payload.
///   </item>
///   <item>
///     Regression in <c>AuxSkills::Init</c>'s loop bound (currently
///     <c>i &lt; 64</c>). If a future change reduces it (say to 32),
///     a SkillID in [32..63] would suddenly dereference an
///     uninitialised <c>AuxSkill</c> with a null <c>Data</c> pointer
///     and the GetAvailability call would crash. The Jenquai-Culture
///     pick (29) sits inside the safe sub-range of the current bound,
///     but the same regression class on a different uninit skill
///     would still surface here as connection death.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The SKILL_UP payload sent
/// here is exactly the wire shape the retail Win32 client emits when
/// the user clicks "+" on a skill: 4B GameID + 4B SkillPoints + 2B
/// SkillID. SkillID=29 is a valid index into the AuxSkills wrapper
/// array and a valid row in the skills table. The "already maxed"
/// early-return is the retail server's normal no-op behaviour when a
/// player tries to level up a skill not in their class tree (Max=0,
/// Level=0, so the equality trips) — we are not making the server
/// accept anything it didn't previously accept, and we don't
/// fabricate a reply (retail doesn't emit one on this branch either).
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; SKILL_UP+REQUEST_TIME round-trip is
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorSkillUpTests : SectorIntegrationTest
{
    public SectorSkillUpTests(ServerFixture server) : base(server) { }

    [RetryFact]
    public async Task SkillUp_OnUntrainedSkill_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        var session = Track(await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Skiller", shipName: "SkillShip", cts.Token));

        // SkillAction wire layout — 10 bytes:
        //   [0..4)   int32 LE  GameID       — retail client sets the
        //                                      actor's avatar id;
        //                                      server resolves via
        //                                      connection binding.
        //   [4..8)   int32 LE  SkillPoints  — current skill-point
        //                                      pool from client UI;
        //                                      server re-reads the
        //                                      authoritative value
        //                                      from RPGInfo so this
        //                                      field is effectively
        //                                      a hint. 0 here.
        //   [8..10)  int16 LE  SkillID      — 29 = SKILL_JENQUAI_CULTURE.
        //                                      warrior_max_level = -1 in
        //                                      the skills table so for
        //                                      a fresh Terran Warrior
        //                                      Skill[29] stays at the
        //                                      AuxSkill::Init default
        //                                      (Level=0, MaxSkillLevel=0)
        //                                      and trips the
        //                                      "already maxed" early
        //                                      return in
        //                                      server/src/PlayerSkills.cpp:106.
        // common/include/net7/PacketStructures.h:987
        byte[] payload = new byte[10];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(8, 2), 29);

        await session.Sector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.SkillUp.Value, payload),
            cts.Token);

        // Survival probe.
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
            $"drained {maxFrames} frames after sending 0x0057 SKILL_UP (SkillID=29, untrained) " +
            $"+ 0x0044 REQUEST_TIME without seeing 0x0034 CLIENT_SET_TIME. " +
            $"Likely the server's HandleSkillAction read past the 10B payload " +
            $"(SkillAction long-revert regression on SkillPoints field), " +
            $"the proxy default-case forwarding dropped the opcode, " +
            $"the AuxSkills::Skill[] Init bound shrank below 30, " +
            $"or the dispatcher case at PlayerConnection.cpp:499 got mis-routed.");
    }

    [RetryFact]
    public async Task SkillUp_CodecBuiltPayload_RoundTripsThroughServer()
    {
        // Same survival-probe contract, but the 0x0057 payload is built by the
        // production SkillUpCodec (the byte-pin lives in the unit suite's
        // SkillUpCodecTests against the live SkillTrainingHostileDevice2 dg #18
        // capture). This proves the codec-produced wire shape is one the server
        // accepts and dispatches -- the CLI-parse-first half of the parity
        // requirement, exercised end-to-end through the proxy.
        //
        // NOTE the wire-shape correction: the canonical struct is 10B packed
        // (SkillID as short), but the retail client serializes SkillID as a
        // 4-byte int, so the real frame is 12B (`37 00 00 00` in the capture).
        // The codec emits the faithful 12B shape; the server's `short SkillID`
        // read of the low 2 bytes still yields the right value on LE.
        //
        // SkillID 55 is the capture's skill and < 64, so it is in-bounds for the
        // AuxSkills::Skill[64] wrapper (no OOB read). Whether the fresh char has
        // the points to train or early-returns on a guard, the connection lives.
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        var session = Track(await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Skilla", shipName: "SkillaShip", cts.Token));

        byte[] payload = new SkillUpCodec().EncodeOutbound(
            new SkillUpMessage(session.GameId, skillId: 55));
        Assert.Equal(SkillUpCodec.Size, payload.Length);

        await session.Sector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.SkillUp.Value, payload), cts.Token);

        int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));
        byte[] reqTimePayload = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);
        await session.Sector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload), cts.Token);

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
            Assert.Equal(clientTick, BinaryPrimitives.ReadInt32LittleEndian(span[..4]));
            return;
        }

        throw new Xunit.Sdk.XunitException(
            $"drained {maxFrames} frames after a codec-built 0x0057 SKILL_UP + 0x0044 " +
            $"REQUEST_TIME without seeing 0x0034 CLIENT_SET_TIME -- the SkillUpCodec " +
            $"wire shape was not accepted/dispatched by the server.");
    }

    /// <summary>
    /// AJ-2 negative test: a 0x0057 SKILL_UP whose SkillID is OUT OF BOUNDS
    /// (>= 64) must be DROPPED by the server with no crash and no state
    /// change -- the connection survives and a follow-up 0x0044 REQUEST_TIME
    /// still round-trips.
    ///
    /// <para>
    /// <c>Player::HandleSkillAction</c> (<c>server/src/PlayerSkills.cpp:97</c>)
    /// indexes <c>RPGInfo.Skills.Skill[Action-&gt;SkillID]</c> where
    /// <c>Skill</c> is the wrapper <c>AuxSkill Skill[64]</c>
    /// (<c>server/src/AuxClasses/AuxSkills.h:86</c>; only 0..63 are
    /// <c>Init</c>'d). Before AJ-2 there was NO bounds check: a wire SkillID
    /// of e.g. 20000 dereferenced ~20000 entries past the array end into
    /// Player-object memory, read a garbage <c>Data</c> pointer, and faulted
    /// the sector thread on <c>GetAvailability()</c> -- an OOB read followed,
    /// on the train path, by an OOB <c>SetLevel</c> WRITE (memory corruption,
    /// potential RCE / cross-object overwrite). The docker compose health
    /// check would then restart the crashed server. (See
    /// plans/99-decisions-log.md 2026-05-25 for the SkillID=169 crash that
    /// first surfaced this array.)
    /// </para>
    ///
    /// <para>
    /// AJ-2 fix: reject <c>SkillID &lt; 0 || SkillID &gt;= 64</c> with a
    /// LogDebug + early return at the top of <c>HandleSkillAction</c>
    /// (PlayerSkills.cpp:108). Pure tightening -- the retail Win32 client
    /// only ever sends a valid 0..63 skill id, so an OOB index is malformed
    /// input the real server never had to serve. This test sends SkillID=20000
    /// (>= 64, still a positive 16-bit value so the server's <c>short</c> read
    /// yields 20000) and proves the drop: the sector thread lives and the
    /// REQUEST_TIME probe still answers. If the bounds check were removed the
    /// sector thread would fault on the OOB dereference and this probe would
    /// hang until the test's CTS fired.
    /// </para>
    ///
    /// <para>
    /// Server-integrity POSITIVE: rejects an input the real client never
    /// produces; no widened acceptance, no fabricated reply (the retail
    /// server emits nothing on a dropped skill action either). Budget: 90s.
    /// </para>
    /// </summary>
    [RetryFact]
    public async Task SkillUp_OutOfBoundsSkillId_IsDropped_ConnectionSurvives()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        var session = Track(await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Skillz", shipName: "SkillzShip", cts.Token));

        // SkillAction 10B wire layout; SkillID = 20000 (>= 64 -> OOB index
        // into AuxSkill Skill[64]). Positive 16-bit so the server's `short`
        // read is exactly 20000 and trips the AJ-2 `SkillID >= 64` reject.
        byte[] payload = new byte[10];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), session.GameId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(8, 2), 20000);

        await session.Sector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.SkillUp.Value, payload), cts.Token);

        // Survival probe: if the server faulted on the OOB index the sector
        // thread is dead and this never answers (CTS fires the test).
        int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));
        byte[] reqTimePayload = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);
        await session.Sector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload), cts.Token);

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
            Assert.Equal(clientTick, BinaryPrimitives.ReadInt32LittleEndian(span[..4]));
            return;
        }

        throw new Xunit.Sdk.XunitException(
            "drained 400 frames after a 0x0057 SKILL_UP with an OUT-OF-BOUNDS " +
            "SkillID (20000) + 0x0044 REQUEST_TIME without seeing 0x0034 " +
            "CLIENT_SET_TIME. The AJ-2 bounds check at " +
            "server/src/PlayerSkills.cpp HandleSkillAction has likely been " +
            "removed -- the server faulted on the OOB AuxSkill index. Restore " +
            "the `SkillID < 0 || SkillID >= 64` reject before reverting this test.");
    }
}
