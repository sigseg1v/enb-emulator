// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0092 CAMERA_CONTROL. Wire (struct CameraControl, 8 bytes, LE):
///   int32 Message; int32 GameID.  Message is emitted FIRST.
/// Source: struct CameraControl (PacketStructures.h), emitter PlayerConnection.cpp.
/// </summary>
public sealed class CameraControlRecord : PacketRecord
{
    public CameraControlRecord(ReadOnlySpan<byte> payload) : base(0x0092, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"CAMERA_CONTROL truncated -- {Payload.Length} bytes, expected 8"); return; }
        FHex(sb, 0, "Message", ReadI32LE(Payload, 0));
        FHex(sb, 4, "GameID",  ReadI32LE(Payload, 4));
    }
}
