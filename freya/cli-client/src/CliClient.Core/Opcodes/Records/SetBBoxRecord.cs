// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x002B SET_BBOX. Wire (struct SetBBox, PacketStructures.h:415):
///   float XMin; float YMin; float XMax; float YMax. = 16 bytes, LE.
/// Emitter PlayerConnection.cpp:1100.
/// </summary>
public sealed class SetBBoxRecord : PacketRecord
{
    public SetBBoxRecord(ReadOnlySpan<byte> payload) : base(0x002B, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 16) { Flag(sb, $"SET_BBOX truncated -- {Payload.Length} bytes, expected 16"); return; }
        FFloat(sb, 0, "XMin", ReadF32LE(Payload, 0));
        FFloat(sb, 4, "YMin", ReadF32LE(Payload, 4));
        FFloat(sb, 8, "XMax", ReadF32LE(Payload, 8));
        FFloat(sb, 12, "YMax", ReadF32LE(Payload, 12));
    }
}
