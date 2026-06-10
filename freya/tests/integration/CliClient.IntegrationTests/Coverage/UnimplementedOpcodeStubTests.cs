// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Coverage;

/// <summary>
/// One <c>[Fact(Skip = ...)]</c> stub per entry in
/// <see cref="KnownUnimplementedOpcodes.Opcodes"/>. Each stub throws
/// on the first executed line, so the second somebody removes the
/// <c>Skip</c> attribute they get a hard failure that forces a real
/// test to be written. The cross-check fact at the bottom enforces
/// 1:1 parity between the list and the stubs.
/// </summary>
/// <remarks>
/// <para>
/// Why <c>[Fact(Skip = ...)]</c> and not just an entry in the catalogue?
/// Because Skip facts show up as yellow / skipped in <c>dotnet test</c>
/// output. That keeps the gap VISIBLE -- somebody glancing at the
/// test report sees "6 skipped" and can drill in. A pure data list
/// gets ignored.
/// </para>
/// <para>
/// Why throw on the first line of each stub? So that the second the
/// <c>Skip</c> attribute is dropped (because the contributor wired
/// the opcode server-side) the test fails immediately and they CAN
/// NOT forget to replace the body with a real round-trip assertion.
/// A pass-through "TODO" body would silently green.
/// </para>
/// </remarks>
public sealed class UnimplementedOpcodeStubTests
{
    private const string SkipReason =
        "Server does not implement this opcode -- no handler case, no SendOpcode emit. " +
        "When the opcode is implemented server-side, drop the [Skip], replace the throw with a " +
        "real round-trip test, move the entry from KnownUnimplementedOpcodes into TestedOpcodes, " +
        "and bump TestedOpcodes.MinTestedCount.";

    [Fact(Skip = SkipReason)]
    public void Opcode_001C_PlayerVarAuxData_HasNoServerImplementation()
    {
        throw new System.NotImplementedException(
            "0x001C PLAYER_VAR_AUX_DATA: server has no handler and no emit -- when wired up, write a real test.");
    }

    [Fact(Skip = SkipReason)]
    public void Opcode_0043_RequestTransformChange_HasNoServerImplementation()
    {
        throw new System.NotImplementedException(
            "0x0043 REQUEST_TRANSFORM_CHANGE: server has no handler and no emit -- when wired up, write a real test.");
    }

    [Fact(Skip = SkipReason)]
    public void Opcode_0085_RecustomizeAvatarUpdate_HasNoServerImplementation()
    {
        throw new System.NotImplementedException(
            "0x0085 RECUSTOMIZE_AVATAR_UPDATE: server has no handler and no emit -- when wired up, write a real test.");
    }

    [Fact(Skip = SkipReason)]
    public void Opcode_0095_JobDelete_HasNoServerImplementation()
    {
        throw new System.NotImplementedException(
            "0x0095 JOB_DELETE: server has no handler and no emit -- when wired up, write a real test.");
    }

    [Fact(Skip = SkipReason)]
    public void Opcode_00D5_GuildRankNamesGuild_HasNoServerImplementation()
    {
        throw new System.NotImplementedException(
            "0x00D5 GUILD_RANK_NAMES_GUILD: server has no handler and no emit -- when wired up, write a real test.");
    }

    [Fact(Skip = SkipReason)]
    public void Opcode_00DD_GpsRequest_HasNoServerImplementation()
    {
        throw new System.NotImplementedException(
            "0x00DD GPS_REQUEST: server has no handler and no emit -- when wired up, write a real test.");
    }

    [Fact]
    public void EveryEntry_ResolvesToARealOpcode()
    {
        foreach (var u in KnownUnimplementedOpcodes.Opcodes)
        {
            Assert.True(
                OpcodeNames.All.TryGetValue(u.Value, out var name),
                $"unimplemented opcode 0x{u.Value:X4} ({u.SymbolicName}) is not in OpcodeNames.All -- typo? renamed upstream?");
            Assert.Equal(name, u.SymbolicName);
        }
    }

    [Fact]
    public void EntriesAreSortedByOpcodeValue()
    {
        for (int i = 1; i < KnownUnimplementedOpcodes.Opcodes.Count; i++)
        {
            Assert.True(
                KnownUnimplementedOpcodes.Opcodes[i - 1].Value < KnownUnimplementedOpcodes.Opcodes[i].Value,
                $"KnownUnimplementedOpcodes.Opcodes is not sorted: 0x{KnownUnimplementedOpcodes.Opcodes[i - 1].Value:X4} >= 0x{KnownUnimplementedOpcodes.Opcodes[i].Value:X4}");
        }
    }

    [Fact]
    public void NoEntry_AppearsAlsoInTestedOpcodes()
    {
        var tested = TestedOpcodes.Opcodes.Select(t => t.Value).ToHashSet();
        foreach (var u in KnownUnimplementedOpcodes.Opcodes)
        {
            Assert.False(tested.Contains(u.Value),
                $"opcode 0x{u.Value:X4} ({u.SymbolicName}) is in BOTH KnownUnimplementedOpcodes AND TestedOpcodes -- pick one.");
        }
    }

    [Fact]
    public void EveryEntry_HasMatchingSkippedStub()
    {
        var stubs = typeof(UnimplementedOpcodeStubTests)
            .GetMethods()
            .Where(m =>
            {
                var fact = m.GetCustomAttributes(typeof(FactAttribute), false)
                    .OfType<FactAttribute>()
                    .FirstOrDefault();
                return fact is not null && !string.IsNullOrEmpty(fact.Skip);
            })
            .Select(m => m.Name)
            .ToList();

        Assert.Equal(KnownUnimplementedOpcodes.Opcodes.Count, stubs.Count);

        foreach (var u in KnownUnimplementedOpcodes.Opcodes)
        {
            var expectedSubstring = $"_{u.Value:X4}_";
            Assert.Contains(stubs, name => name.Contains(expectedSubstring, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
