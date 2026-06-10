// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// 0x00C9 GUILD_RECRUIT_ACCEPT_CLIENT -- HANDLED-but-CRASHES skip stub.
/// Server <c>Player::HandleRecruitAcceptClient</c>
/// (<c>server/src/PlayerGuild.cpp:768</c>) deterministically dereferences
/// an uninitialised <c>m_Recruiter</c> on a fresh starbase character.
/// The opcode is HANDLED but NOT HITTABLE per the Phase K coverage
/// taxonomy.
///
/// <para>
/// Empirical confirmation. The handler:
/// </para>
/// <code>
///   void Player::HandleRecruitAcceptClient(unsigned char *data)
///   {
///       struct RecruitAcceptClientPacket {
///           long gameid;
///           char accept;
///       } *request;
///       request = (RecruitAcceptClientPacket *)data;
///
///       m_Recruiter-&gt;HandleRecruitMember2(this, request-&gt;accept);
///   }
/// </code>
///
/// <para>
/// The <c>m_Recruiter</c> field is declared at
/// <c>server/src/PlayerClass.h:1091</c> as a raw
/// <c>Player *m_Recruiter;</c> with NO constructor initialiser. Verified
/// by exhaustive grep across server/src/*.cpp -- the only assignment
/// is at <c>PlayerGuild.cpp:560</c> inside <c>HandleRecruitMember</c>
/// when a guild leader invokes "/grecruit name" against this character
/// (sets <c>recruit-&gt;m_Recruiter = this</c>). For a character that
/// has never been the target of a /grecruit invocation -- which is
/// every fresh-test character -- <c>m_Recruiter</c> holds whatever
/// bytes the Player ctor heap region happened to contain at allocation.
/// </para>
///
/// <para>
/// The deterministic-crash path:
/// </para>
/// <list type="number">
///   <item>Client emits 0x00C9 with a 5-byte payload {long gameid;
///     char accept}.</item>
///   <item>Dispatcher at <c>PlayerConnection.cpp:608</c> routes to
///     <c>HandleRecruitAcceptClient(data)</c>.</item>
///   <item>Cast <c>request = (RecruitAcceptClientPacket *)data</c>
///     succeeds (in-bounds for a 5B buffer).</item>
///   <item><c>m_Recruiter-&gt;HandleRecruitMember2(...)</c> dereferences
///     the wild pointer. On Linux x86_64 with ASLR, the wild pointer
///     almost always lands outside the process's mapped address space,
///     triggering SIGSEGV. The worker thread dies; the per-Player UDP
///     queue is never flushed; any pending REQUEST_TIME echo is lost.</item>
/// </list>
///
/// <para>
/// Same blast-radius shape as 0x008D INCAPACITANCE_REQUEST: docker-
/// compose's auto-restart will fire after the SIGSEGV but the test
/// would time out at 90s waiting for CLIENT_SET_TIME. Per CLAUDE.md
/// server-integrity, we don't patch the server to satisfy a tooling
/// consumer; the test adapts by Skip'ing.
/// </para>
///
/// <para>
/// What to do when fixing this. (a) Add <c>m_Recruiter(nullptr)</c> to
/// the Player constructor's initialiser list at PlayerClass.cpp; (b)
/// add an <c>if (m_Recruiter)</c> guard at PlayerGuild.cpp:777 before
/// the dereference; (c) confirm the retail server's handling of the
/// race-case where a recruit clicks accept after the recruiter has
/// disconnected (the m_Recruiter pointer would be stale, not just
/// null -- a separate use-after-free class). Once fixed: remove the
/// [Skip] attribute, replace the throw with a real round-trip
/// assertion (likely a survival probe since m_Recruiter=null hits
/// the no-op early-return), add a <see cref="TestedOpcode"/> entry
/// to <see cref="Coverage.TestedOpcodes.Opcodes"/>, and bump
/// <see cref="Coverage.TestedOpcodes.MinTestedCount"/>.
/// </para>
///
/// <para>
/// Why not in <see cref="Coverage.KnownUnimplementedOpcodes.Opcodes"/>.
/// That list is strictly for opcodes with NO handler at all -- 0x00C9
/// HAS a handler, just one that crashes on a precondition the test
/// can't legally set up (would require a second account, recruiting
/// the first into a guild, then the first accepting -- doable but
/// expensive infrastructure for a single opcode). This is the same
/// HANDLED-but-UNSAFE bucket as 0x008D INCAPACITANCE_REQUEST.
/// </para>
///
/// <para>
/// Cross-references. The Wave 38 0x00C5 GUILD_LEADER_ACCEPT_CLIENT
/// TestedOpcode entry mentions this opcode as the "swap-target" risk
/// for the dispatcher mis-route catch -- HandleRecruitAcceptClient is
/// what 0x00C5 would crash-route into if line 605 and line 609 in
/// PlayerConnection.cpp were swapped.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorGuildRecruitAcceptClientTests
{
    private const string SkipReason =
        "0x00C9 GUILD_RECRUIT_ACCEPT_CLIENT deterministically dereferences an uninitialised " +
        "m_Recruiter pointer on a fresh starbase character (Player::HandleRecruitAcceptClient at " +
        "server/src/PlayerGuild.cpp:777). The m_Recruiter field at PlayerClass.h:1091 has NO ctor " +
        "initialiser -- verified by exhaustive grep, the only assignment is in HandleRecruitMember " +
        "when a guild leader invokes /grecruit against this character. A fresh test character has " +
        "never been recruited so m_Recruiter is wild-pointer memory; the deref SEGVs the worker " +
        "and the per-Player UDP queue dies before any reply flushes. Skip until the underlying " +
        "handler bug is fixed (likely: add m_Recruiter(nullptr) to Player ctor + if(m_Recruiter) " +
        "guard at PlayerGuild.cpp:777). When fixed: drop this Skip, replace the throw with a real " +
        "round-trip assertion (m_Recruiter=null hits the no-op early-return so a survival probe " +
        "is the natural shape), add a TestedOpcode entry, bump MinTestedCount.";

    [Fact(Skip = SkipReason)]
    public void Opcode_00C9_GuildRecruitAcceptClient_CrashesServerOnFirstCallFromFreshChar()
    {
        throw new System.NotImplementedException(
            "0x00C9 GUILD_RECRUIT_ACCEPT_CLIENT: server handler exists at PlayerGuild.cpp:768 " +
            "but deterministically NULL-derefs m_Recruiter on a fresh starbase character -- see " +
            "class XML docs for the wild-pointer chain and fix path. Do NOT remove [Skip] without " +
            "first patching the handler.");
    }
}
