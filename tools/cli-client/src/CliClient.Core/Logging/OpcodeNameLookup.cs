// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Reflection;
using N7.CliClient.Opcodes;

namespace N7.CliClient.Logging;

/// <summary>
/// Resolves <see cref="OpcodeId"/> → human-readable name by reflecting
/// over the static fields of <see cref="OpcodeId.Known"/> once at type
/// init. Used by <see cref="PacketLog"/> so log lines carry both the
/// numeric opcode (always present) and the symbolic name (when known).
/// </summary>
public static class OpcodeNameLookup
{
    private static readonly IReadOnlyDictionary<ushort, string> Names = BuildIndex();

    /// <summary>
    /// Get the symbolic name for an opcode, or null if it's not one of
    /// the <see cref="OpcodeId.Known"/> entries. Callers should fall
    /// back to the hex form in that case.
    /// </summary>
    public static string? TryGetName(OpcodeId opcode)
        => Names.TryGetValue(opcode.Value, out var name) ? name : null;

    /// <summary>Total number of known opcode names.</summary>
    public static int KnownCount => Names.Count;

    private static IReadOnlyDictionary<ushort, string> BuildIndex()
    {
        var dict = new Dictionary<ushort, string>();
        foreach (var field in typeof(OpcodeId.Known).GetFields(
            BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(OpcodeId)) continue;
            var value = field.GetValue(null);
            if (value is OpcodeId op) dict[op.Value] = field.Name;
        }
        return dict;
    }
}
