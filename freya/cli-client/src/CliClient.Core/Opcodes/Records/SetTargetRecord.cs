// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0019 SET_TARGET. Wire (struct SetTarget, 8 bytes, LE, no byte-swap):
///   int32 GameID; int32 TargetID (0xFFFFFFFF = no target).
/// </summary>
public sealed class SetTargetRecord : PacketRecord
{
    public SetTargetRecord(ReadOnlySpan<byte> payload) : base(0x0019, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"SET_TARGET truncated -- {Payload.Length} bytes, expected 8"); return; }
        FHex(sb, 0, "GameID",   ReadI32LE(Payload, 0));
        int target = ReadI32LE(Payload, 4);
        FHex(sb, 4, "TargetID", target, target == -1 ? "(no target)" : null);
    }
}
