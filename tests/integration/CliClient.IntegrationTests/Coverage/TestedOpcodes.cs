// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Opcodes;

namespace N7.CliClient.IntegrationTests.Coverage;

/// <summary>
/// Hand-maintained catalogue of every opcode that has at least one
/// round-trip test in this suite. Drives the Phase T coverage ratchet
/// (<see cref="CoverageRatchetTests"/>).
/// </summary>
/// <remarks>
/// <para>
/// "Round-trip test" = at least one test exercises the opcode through
/// a real (or capture-replay) wire path, not just a unit test of the
/// codec in isolation. Adding an entry here without a corresponding
/// integration test is a lie that ratchets a false floor; the lie
/// will be caught by code review, not by tooling — be honest.
/// </para>
/// <para>
/// The ratchet works in two directions:
/// </para>
/// <list type="bullet">
///   <item>
///     <see cref="CoverageRatchetTests"/> asserts the set's count
///     equals the floor constant <see cref="MinTestedCount"/>. To
///     <i>add</i> an opcode you bump the constant by one and add the
///     entry. To <i>remove</i> an opcode (e.g. you deleted its test)
///     you must drop both — and the count-equality check forces you
///     to think about whether the deletion was intentional.
///   </item>
///   <item>
///     Every entry must resolve to a real opcode in
///     <see cref="OpcodeNames.All"/>. Catches typos and opcodes that
///     were renamed/removed upstream.
///   </item>
/// </list>
/// <para>
/// What is NOT counted as round-trip coverage here:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Unit tests of the codec only</b> (in
///     <c>tools/cli-client/tests/CliClient.UnitTests/Opcodes/</c>) —
///     those verify byte layout but not "the wire round-trips this".
///   </item>
///   <item>
///     <b>Named-opaque registrations</b> (the 207-opcode
///     <c>RegisterAllNamedOpaque</c> seed in <c>OpcodeRegistry</c>) —
///     opaque means "we recognise the opcode but don't decode the
///     payload". That's not coverage.
///   </item>
///   <item>
///     <b>Opcodes the client sends but whose reply we never
///     assert</b> (e.g. sector LOGIN/0x0002, which the Linux stub
///     today acks at the connection-state level only — no TCP
///     response to assert against). Add them here only when a
///     reply path lights up.
///   </item>
/// </list>
/// </remarks>
public static class TestedOpcodes
{
    /// <summary>
    /// The coverage floor. Bump up by one when you add an entry to
    /// <see cref="Opcodes"/>. NEVER decrease without a commit message
    /// explaining what deleted coverage and why it was OK to delete.
    /// </summary>
    public const int MinTestedCount = 9;

    /// <summary>
    /// Every opcode with round-trip coverage in this suite, with an
    /// inline citation to the test that proves it. Sorted by opcode
    /// value for stable diffs.
    /// </summary>
    public static readonly IReadOnlyList<TestedOpcode> Opcodes = new[]
    {
        new TestedOpcode(0x0000, "VERSION_REQUEST",
            "Opcodes/VersionRequestTests.cs — client sends VersionRequest, asserts on the typed-decoded VersionResponse status."),
        new TestedOpcode(0x0001, "VERSION_RESPONSE",
            "Opcodes/VersionRequestTests.cs — server reply decoded via VersionResponseCodec, status field asserted for all three branches (current/old/new)."),
        new TestedOpcode(0x0035, "MASTER_JOIN",
            "Opcodes/MasterJoinTests.cs — live send into proxy on 3801, ServerRedirect reply asserted; AND Verification/CaptureReplayTests.cs — retail capture_1 frame 220 decoded + codec round-trip identity."),
        new TestedOpcode(0x0036, "SERVER_REDIRECT",
            "Opcodes/MasterJoinTests.cs — received as the reply to MASTER_JOIN; AND Verification/CaptureReplayTests.cs — retail capture_1 frame 222 decoded with field-by-field assertions."),
        new TestedOpcode(0x006D, "GLOBAL_CONNECT",
            "Opcodes/GlobalConnectTests.cs — client sends GlobalConnect with a real Net7SSL-issued ticket; round-trip drives the Phase K proxy↔server global UDP plane (UDP 3810)."),
        new TestedOpcode(0x006E, "GLOBAL_TICKET_REQUEST",
            "Opcodes/GlobalTicketRequestTests.cs — client sends GlobalTicketRequest with slot=0 against a seeded account that has no avatars; exercises HandleGlobalTicketRequest's wire-size fix (server/src/UDP_Global.cpp:200,237) — without those fixes the slot index decoded into the username's length prefix and the AVATARLOGIN_CONFIRM reply was 8B instead of 4B."),
        new TestedOpcode(0x006F, "GLOBAL_TICKET",
            "Opcodes/GlobalTicketRequestTests.cs — received as the proxy's failure-path reply (response_code=1002 galaxy full) after SendAvatarLogin's WaitForResponse times out; decoded via GlobalTicketCodec which verifies the Phase K 68B canonical Win32 size (was 72B on Linux pre-int32_t migration)."),
        new TestedOpcode(0x0070, "GLOBAL_AVATAR_LIST",
            "Opcodes/GlobalConnectTests.cs — received as the reply to GlobalConnect after the proxy's SendTicket UDP round-trip to the server's HandleGlobalOpcode dispatcher and back."),
        new TestedOpcode(0x0075, "GLOBAL_ERROR",
            "Opcodes/GlobalConnectTests.cs — StressTestClosedAccount_GlobalConnect_ReturnsGlobalErrorCode12 sends GlobalConnect for a status=0 (STRESS_TEST_CLOSED) seed account; the server emits 0x2004 GLOBAL_ERROR err=12 on UDP 3810, the proxy forwards it as 0x0075, and the test asserts both the error code (12) and that the message text from the proxy's g_GlobalErrorMsg[12] table comes through (validates the table wasn't truncated at 11 entries)."),
    };
}

/// <summary>One row in the <see cref="TestedOpcodes.Opcodes"/> table.</summary>
public sealed record TestedOpcode(ushort Value, string SymbolicName, string TestCitation)
{
    public OpcodeId AsOpcodeId() => new OpcodeId(Value);
}
