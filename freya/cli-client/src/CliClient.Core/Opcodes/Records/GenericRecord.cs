// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// Fallback record for opcodes without a structured decoder yet. Emits
/// any embedded ASCII strings as fields and falls through to the base
/// class's hex+ASCII gutter dump.
/// </summary>
public sealed class GenericRecord : PacketRecord
{
    public GenericRecord(ushort opcode, ReadOnlySpan<byte> payload)
        : base(opcode, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        var strings = ExtractAsciiStrings(Payload, minLen: 4);
        if (strings.Count == 0)
        {
            Field(sb, "ascii-scan", "(none, minLen=4)");
            return;
        }
        foreach (var (off, s) in strings)
            Field(sb, $"@0x{off:X3}", Quote(s));
    }
}
