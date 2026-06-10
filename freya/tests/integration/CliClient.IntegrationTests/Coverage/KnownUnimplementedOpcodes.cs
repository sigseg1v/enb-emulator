// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Opcodes;

namespace N7.CliClient.IntegrationTests.Coverage;

/// <summary>
/// Catalogue of opcodes that exist in the wire protocol
/// (<c>common/include/net7/Opcodes.h</c>) but for which the current
/// Net-7 server has ZERO implementation -- no handler case in any
/// dispatch switch, no SendOpcode emit site anywhere across
/// <c>server/src/</c>, <c>login-server/</c>, and <c>proxy/</c>.
/// </summary>
/// <remarks>
/// <para>
/// These are <b>known gaps</b> in the server, NOT test debt. The
/// upstream Net-7 server fork simply never wired these up; the
/// retail Earth and Beyond client may or may not have ever used
/// them. Each gap is paired with a <c>[Fact(Skip = ...)]</c> stub
/// in <c>UnimplementedOpcodeStubTests</c> that throws immediately
/// on the first line, so the moment somebody implements the opcode
/// server-side they're forced to:
/// </para>
/// <list type="number">
///   <item>Drop the <c>Skip</c> attribute on the stub.</item>
///   <item>Replace the <c>throw</c> with a real round-trip test.</item>
///   <item>Move the entry from this list into
///         <see cref="TestedOpcodes.Opcodes"/> (and bump
///         <see cref="TestedOpcodes.MinTestedCount"/>).</item>
///   <item>Remove the entry from <see cref="Opcodes"/> below.</item>
/// </list>
/// <para>
/// The ratchet test
/// <see cref="UnimplementedOpcodeStubTests.EveryEntry_HasMatchingSkippedStub"/>
/// enforces that every entry here corresponds to an actual
/// <c>[Fact(Skip = ...)]</c> in the stub class, so the list cannot
/// silently rot.
/// </para>
/// <para>
/// Inclusion criteria (strict):
/// </para>
/// <list type="bullet">
///   <item>The opcode constant exists in
///         <c>common/include/net7/Opcodes.h</c> and resolves via
///         <see cref="OpcodeNames.All"/>.</item>
///   <item><c>grep -r "ENB_OPCODE_NNNN_" server/src/ login-server/ proxy/</c>
///         returns zero matches. (A comment-only reference in a
///         <c>.h</c> file does NOT count as an implementation.)</item>
///   <item>The opcode is on the <b>client wire</b>, not strictly an
///         inter-process server-server frame (MVAS 0x1xxx, SSL
///         0x4xxx, count 0x5xxx, master<->sector 0x78xx/0x79xx).
///         Internal-only opcodes are excluded here -- they cannot
///         be tested from a CLI client and live or die with the
///         server-server protocol.</item>
/// </list>
/// </remarks>
public static class KnownUnimplementedOpcodes
{
    /// <summary>
    /// Every client-wire opcode declared in Opcodes.h that the
    /// current server does not handle and does not emit. Sorted by
    /// opcode value for stable diffs.
    /// </summary>
    public static readonly IReadOnlyList<UnimplementedOpcode> Opcodes = new[]
    {
        new UnimplementedOpcode(0x001C, "PLAYER_VAR_AUX_DATA",
            "No handler, no emit. Player-variable AUX data channel -- no Net-7 server code ever wrote one or read one. The retail client may have used these for client-side derived stats; the server simply doesn't participate."),

        new UnimplementedOpcode(0x0043, "REQUEST_TRANSFORM_CHANGE",
            "No handler, no emit. Retail client may have sent this when changing ship transform / hierarchy; the server never accepted it. Player::Dispatch has no case for 0x0043 -- a real client send would land in the unknown-opcode log."),

        new UnimplementedOpcode(0x0085, "RECUSTOMIZE_AVATAR_UPDATE",
            "No handler, no emit. Companion to 0x0084 RECUSTOMIZE_AVATAR_DONE which IS handled (PlayerConnection.cpp). The retail recustomization flow may have streamed incremental updates as the player adjusted sliders; the Net-7 server only accepts the final DONE."),

        new UnimplementedOpcode(0x0095, "JOB_DELETE",
            "No handler, no emit. Mission/job system slot for deleting a job entry. Net-7's mission code (Player::HandleMission, MissionManager.cpp) has dispatch for accept / complete / abandon but no slot for explicit DELETE."),

        new UnimplementedOpcode(0x00D5, "GUILD_RANK_NAMES_GUILD",
            "No handler, no emit. Compare with 0x00D3 GUILD_RANK_NAMES_SECTOR (server emits in PlayerGuild.cpp:697 on guild rank query). 0x00D5 was presumably the guild-wide broadcast variant; PlayerGuild never lights it up."),

        new UnimplementedOpcode(0x00DD, "GPS_REQUEST",
            "No handler, no emit. The GPS / minimap query channel is silent on the Net-7 server. Retail clients have a /gps slash command that may have triggered this; in the current Net-7 code base /gps is handled via SendVaMessage replies (PlayerConnection.cpp slash-dispatch), not via a 0x00DD round-trip."),
    };
}

/// <summary>
/// One entry in <see cref="KnownUnimplementedOpcodes.Opcodes"/>.
/// </summary>
/// <param name="Value">The 16-bit opcode value (e.g. 0x00DD).</param>
/// <param name="SymbolicName">The upstream name from
///   <c>common/include/net7/Opcodes.h</c>, minus the
///   <c>ENB_OPCODE_NNNN_</c> prefix.</param>
/// <param name="Reason">Short justification for why the opcode is
///   listed -- typically the result of the inclusion-criteria grep
///   plus a sentence on what the retail client probably expected.</param>
public sealed record UnimplementedOpcode(
    ushort Value,
    string SymbolicName,
    string Reason);
