// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x002A SET_ZBAND. Wire (struct SetZBand, PacketStructures.h:423):
///   float Min; float Max. = 8 bytes, LE. Emitter PlayerConnection.cpp:1110.
/// </summary>
public sealed class SetZBandRecord : PacketRecord
{
    public SetZBandRecord(ReadOnlySpan<byte> payload) : base(0x002A, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"SET_ZBAND truncated -- {Payload.Length} bytes, expected 8"); return; }
        FFloat(sb, 0, "Min", ReadF32LE(Payload, 0));
        FFloat(sb, 4, "Max", ReadF32LE(Payload, 4));
    }
}
