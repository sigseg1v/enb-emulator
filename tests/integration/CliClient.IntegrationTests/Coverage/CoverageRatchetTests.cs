// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Coverage;

/// <summary>
/// Phase T Item 10 — the opcode-coverage ratchet. A meta-test that
/// counts the number of opcodes from <c>common/include/net7/Opcodes.h</c>
/// that have at least one round-trip test, and enforces "never goes
/// down". Build breaks if the ratchet drops.
/// </summary>
/// <remarks>
/// <para>
/// The ratchet is the line-count of <see cref="TestedOpcodes.Opcodes"/>,
/// which must equal <see cref="TestedOpcodes.MinTestedCount"/>. The
/// equality (not just <c>&gt;=</c>) is deliberate: every entry-add
/// MUST be paired with a constant-bump, and every entry-delete
/// MUST be paired with a constant-decrement and a commit message
/// explaining what coverage went away. Drift gets caught at PR time.
/// </para>
/// <para>
/// Phase T starts at single-digit coverage (4 of 207 opcodes) — the
/// ratchet ramps with Phase K as more opcodes get wired server-side
/// and get real round-trip tests in this suite.
/// </para>
/// </remarks>
public sealed class CoverageRatchetTests
{
    [Fact]
    public void Ratchet_CountEqualsFloor()
    {
        Assert.Equal(TestedOpcodes.MinTestedCount, TestedOpcodes.Opcodes.Count);
    }

    [Fact]
    public void EveryEntry_ResolvesToARealOpcode()
    {
        foreach (var t in TestedOpcodes.Opcodes)
        {
            Assert.True(
                OpcodeNames.All.TryGetValue(t.Value, out var name),
                $"opcode 0x{t.Value:X4} ({t.SymbolicName}) is not in OpcodeNames.All — typo? renamed upstream?");
            Assert.Equal(name, t.SymbolicName);
        }
    }

    [Fact]
    public void NoDuplicateOpcodes()
    {
        var seen = new HashSet<ushort>();
        foreach (var t in TestedOpcodes.Opcodes)
        {
            Assert.True(seen.Add(t.Value),
                $"opcode 0x{t.Value:X4} ({t.SymbolicName}) appears more than once");
        }
    }

    [Fact]
    public void EntriesAreSortedByOpcodeValue()
    {
        // Stable diffs — every PR that adds coverage shows as a clean
        // single-line insertion at the correct sorted position.
        for (int i = 1; i < TestedOpcodes.Opcodes.Count; i++)
        {
            Assert.True(
                TestedOpcodes.Opcodes[i - 1].Value < TestedOpcodes.Opcodes[i].Value,
                $"TestedOpcodes.Opcodes is not sorted: 0x{TestedOpcodes.Opcodes[i - 1].Value:X4} >= 0x{TestedOpcodes.Opcodes[i].Value:X4}");
        }
    }

    [Fact]
    public void EveryEntry_HasANonEmptyTestCitation()
    {
        // The citation is what makes the entry honest — a reader can
        // open the cited test file and verify the opcode actually has
        // round-trip coverage there.
        foreach (var t in TestedOpcodes.Opcodes)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.TestCitation),
                $"opcode 0x{t.Value:X4} ({t.SymbolicName}) has an empty test citation");
            Assert.Contains(".cs", t.TestCitation);
        }
    }

    [Fact]
    public void CoverageStat_IsHumanReadable()
    {
        // Not an assertion — emits the current coverage ratio so CI
        // logs and local runs can grep for it.
        double pct = 100.0 * TestedOpcodes.Opcodes.Count / OpcodeNames.Count;
        Console.WriteLine(
            $"[coverage] {TestedOpcodes.Opcodes.Count}/{OpcodeNames.Count} opcodes have round-trip integration coverage ({pct:F1}%)");
        Assert.True(TestedOpcodes.Opcodes.Count <= OpcodeNames.Count,
            "tested-opcode count exceeds the OpcodeNames.All universe — impossible by construction");
    }
}
