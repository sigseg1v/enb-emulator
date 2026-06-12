// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x2025. Retail-only sector opcode (not present in our Opcodes.h). Observed
/// 4-byte payload carrying a game id. Sent S2C during the sector login
/// sequence. We decode the single int32 and flag it as retail-only so the
/// coverage report shows it has no server-side emitter yet.
/// </summary>
public sealed class Opcode2025Record : PacketRecord
{
    public Opcode2025Record(ReadOnlySpan<byte> payload) : base(0x2025, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"opcode 0x2025 truncated -- {Payload.Length} bytes, expected 4"); return; }
        FHex(sb, 0, "GameID", ReadI32LE(Payload, 0));
    }
}
