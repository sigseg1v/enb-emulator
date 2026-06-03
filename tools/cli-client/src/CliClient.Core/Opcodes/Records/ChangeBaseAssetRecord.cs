// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0026 CHANGE_BASE_ASSET. Wire (struct ChangeBaseAsset, PacketStructures.h:358):
///   int32 GameID; int32 BaseAsset; float Scale; float HSV[3]. = 24 bytes, all LE.
/// Emitter PlayerConnection.cpp:3623.
/// </summary>
public sealed class ChangeBaseAssetRecord : PacketRecord
{
    public ChangeBaseAssetRecord(ReadOnlySpan<byte> payload) : base(0x0026, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 24) { Flag(sb, $"CHANGE_BASE_ASSET truncated -- {Payload.Length} bytes, expected 24"); return; }
        FHex(sb,   0, "GameID",    ReadI32LE(Payload, 0));
        FDec(sb,   4, "BaseAsset", ReadI32LE(Payload, 4));
        FFloat(sb, 8, "Scale",     ReadF32LE(Payload, 8));
        float h0 = ReadF32LE(Payload, 12), h1 = ReadF32LE(Payload, 16), h2 = ReadF32LE(Payload, 20);
        FBytes(sb, 12, 12, "HSV", $"({h0:0.0##}, {h1:0.0##}, {h2:0.0##})");
    }
}
