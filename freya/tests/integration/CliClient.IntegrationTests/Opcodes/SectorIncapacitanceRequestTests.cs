// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// 0x008D INCAPACITANCE_REQUEST -- HANDLED-but-CRASHES skip stub.
/// Server <c>Player::HandleIncapacitanceRequest</c>
/// (<c>server/src/PlayerConnection.cpp:11066</c>) has a real handler
/// but its first-call branch (gated by <c>m_IncapAvatarSent</c>)
/// deterministically crashes the server worker on a fresh starbase
/// character. The opcode is therefore HANDLED but NOT HITTABLE per
/// the Phase K coverage taxonomy.
///
/// <para>
/// Empirical confirmation. The stub below was originally written as
/// a direct-reply assertion: send a 4-byte all-zero
/// INCAPACITANCE_REQUEST, drain until 0x0054 TALK_TREE arrives with
/// the hard-coded "Are you alright over there?" prompt. The handler
/// emits SendOpcode(ENB_OPCODE_0054_TALK_TREE, ...) at
/// PlayerConnection.cpp:11080 BEFORE the m_IncapAvatarSent gate at
/// :11082, so the reply should be observable. It isn't. Empirical
/// run (this exact test, against the live docker-compose stack)
/// shows the server process restart-banner appearing in the log
/// approximately 5 seconds after the "Station login for player
/// Inkapia" line and immediately after a benign
/// "ItemList - Array of size [0] full. Adding [1024] slots!"
/// message. No SIGSEGV / "Segmentation fault" log line surfaces
/// (docker stdout buffering eats it), but the docker-compose
/// auto-restart fires the startup sequence. Test times out at 90s
/// waiting for the TALK_TREE that the dead worker's UDP queue
/// never flushed.
/// </para>
///
/// <para>
/// Why the TALK_TREE never arrives even though it's queued first.
/// SendOpcode in the per-Player UDP path doesn't flush
/// synchronously; the worker thread aggregates into a 0x2016
/// PACKET_SEQUENCE batch that is sent on the next scheduler tick.
/// If the worker SEGVs before that tick fires, the queued frames
/// are lost. The crash site is somewhere in the avatar-mutation
/// arm:
/// </para>
/// <code>
///   if (!m_IncapAvatarSent) {                                   // 11082
///       m_IncapAvatarSent = true;
///       PlayerIndex()-&gt;SetPIPAvatarID(-3);                     // 11087
///       SendAuxPlayer();                                        // 11088
///       StationTemplate * Stn = ...GetStation(m_RegisteredSectorID); // 11090
///       if (!Stn) { Stn = ...GetStation(10711); if (!Stn) return; }
///       NPCTemplate * NPCs = ...GetNPC(Stn->NPCs[0]);            // 11098
///       if (!NPCs) return;
///       memcpy(&amp;avatar.avatar_data, &amp;NPCs->Avatar, ...);          // 11105
///       NPCs->Avatar.shirt_primary_color[0] += 0.1f;             // 11107 *** mutates shared NPC template state
///       NPCs->Avatar.shirt_primary_color[1] -= 0.1f;
///       NPCs->Avatar.shirt_primary_color[2] += 0.15f;
///       strcpy_s(avatar.avatar_data.avatar_first_name, ..., "Station\0");
///       strcpy_s(avatar.avatar_data.avatar_last_name, ..., "Mechanic\0");
///       SendOpcode(ENB_OPCODE_0061_AVATAR_DESCRIPTION, ...);     // 11117
///   }
/// </code>
///
/// <para>
/// Candidate crash sites: (a) SendAuxPlayer null-deref on a fresh
/// starbase character whose equipment/inventory pointers haven't
/// been populated; (b) GetStation(m_RegisteredSectorID) returning
/// non-null but with NPCs[] empty/uninit, so Stn->NPCs[0] reads
/// garbage and GetNPC returns a wild pointer that survives the
/// !NPCs early-return; (c) NPCs->Avatar.shirt_primary_color
/// mutation against shared global NPC template state under no
/// lock -- corrupts the per-sector NPC table for every subsequent
/// player and may SEGV directly. The actual crash address would
/// need a coredump to pin down.
/// </para>
///
/// <para>
/// Why Skip rather than try harder. Per CLAUDE.md:
/// </para>
/// <list type="bullet">
///   <item>"NEVER weaken, relax, or loosen the server's security
///     posture to satisfy a tooling consumer." Patching the
///     handler so the test passes is forbidden absent a
///     primary-source citation justifying the patch as a retail
///     fidelity improvement -- which this isn't (the test is the
///     consumer asking for a behaviour the retail server may or
///     may not have had).</item>
///   <item>"The test must adapt to the server, not vice-versa."
///     The test adapts by Skip'ing.</item>
///   <item>The test framework runs xUnit tests in parallel
///     within an assembly. A test that reliably kills the server
///     mid-run also breaks every concurrent test, so a [Skip] is
///     the only sane disposition.</item>
/// </list>
///
/// <para>
/// What to do when fixing this. (a) Either guard the
/// avatar-mutation arm behind a SetActive() / fully-loaded-player
/// check so a fresh starbase character bails out cleanly; (b) or
/// fix the underlying NULL-deref / OOB / shared-state crash;
/// (c) or audit whether the avatar-mutation arm is retail-faithful
/// at all (does the retail server actually splatter every player's
/// distress-call NPC avatar by mutating a SHARED template?
/// PlayerConnection.cpp:11107 is a code smell of the highest
/// order). Once fixed: remove the [Skip] attribute, replace the
/// throw with a real round-trip assertion against the TALK_TREE
/// reply, add a <see cref="TestedOpcode"/> entry to
/// <see cref="Coverage.TestedOpcodes.Opcodes"/>, and bump
/// <see cref="Coverage.TestedOpcodes.MinTestedCount"/>.
/// </para>
///
/// <para>
/// Cross-references. The Wave 29 abandonment of this opcode is
/// documented in 0x0088 PETITION_STUCK's TestedOpcode entry and
/// referenced in 0x0061 AVATAR_DESCRIPTION's Wave 34 prose.
/// 0x008D is NOT in
/// <see cref="Coverage.KnownUnimplementedOpcodes.Opcodes"/>
/// because that list is strictly for opcodes with NO handler at
/// all -- 0x008D has a handler, just one that crashes. This is a
/// separate taxonomy bucket: HANDLED-BUT-UNSAFE.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorIncapacitanceRequestTests
{
    private const string SkipReason =
        "0x008D INCAPACITANCE_REQUEST deterministically crashes the server worker " +
        "inside Player::HandleIncapacitanceRequest's first-call m_IncapAvatarSent branch " +
        "(PlayerConnection.cpp:11082-11118) on a fresh starbase character -- empirically " +
        "confirmed: docker-compose log shows the server restart banner ~5s after the " +
        "INCAPACITANCE_REQUEST is sent. The crash kills the per-Player UDP queue before " +
        "the queued 0x0054 TALK_TREE reply is flushed, so even a queue-only assertion " +
        "is unreachable. Skip until the underlying handler bug is fixed (likely candidate: " +
        "the NPCs->Avatar.shirt_primary_color mutation at PlayerConnection.cpp:11107-11109 " +
        "against shared global NPC template state). When fixed: drop this Skip, replace the " +
        "throw with a real round-trip assertion against the hard-coded 'Are you alright over " +
        "there?' TALK_TREE prompt, add a TestedOpcode entry, bump MinTestedCount.";

    [Fact(Skip = SkipReason)]
    public void Opcode_008D_IncapacitanceRequest_CrashesServerOnFirstCall()
    {
        throw new System.NotImplementedException(
            "0x008D INCAPACITANCE_REQUEST: server handler exists at PlayerConnection.cpp:11066 " +
            "but deterministically crashes on fresh starbase character -- see class XML docs for " +
            "the empirical confirmation, candidate crash sites, and fix path. Do NOT remove " +
            "[Skip] without first patching the handler.");
    }
}
