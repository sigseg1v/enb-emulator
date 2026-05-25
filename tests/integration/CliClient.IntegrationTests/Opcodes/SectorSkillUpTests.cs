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
public sealed class SectorSkillUpTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorSkillUpTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task SkillUp_OnUntrainedSkill_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        // cli_test14 — Pool[12]. Dedicated to this test so its
        // Create/Delete cycle doesn't collide with Pool[3..11] which
        // are owned by SectorLogin / SectorChat / SectorRequestTime /
        // SectorStartAck / SectorTurnTilt / SectorAction / SectorMove /
        // SectorStarbaseRoomChange / SectorStarbaseRequest respectively.
        var account = TestAccounts.Pool[12];
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Skiller", shipName: "SkillShip", cts.Token);

        try
        {
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
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
