// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x009C WARP_INDEX. Wire: a single int32 LE warp index (-1 = none).
/// Source: emitter PlayerConnection.cpp (SendOpcode of &amp;index, sizeof(index)).
/// </summary>
public sealed class WarpIndexRecord : PacketRecord
{
    public WarpIndexRecord(ReadOnlySpan<byte> payload) : base(0x009C, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"WARP_INDEX truncated -- {Payload.Length} bytes, expected 4"); return; }
        int idx = ReadI32LE(Payload, 0);
        FDec(sb, 0, "Index", idx, idx == -1 ? "(none)" : null);
    }
}
