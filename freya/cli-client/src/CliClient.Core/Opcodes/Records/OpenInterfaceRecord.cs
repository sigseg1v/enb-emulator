// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0066 OPEN_INTERFACE. Server commands the client to open/close a UI panel
/// (vault, manufacturing, analyze, vendor ...). 8 bytes, per
/// PacketStructures.h:534-538:
///   int32  UIChange  @0   (open vs close / which slot)
///   int32  UIType    @4   (which interface)
/// </summary>
public sealed class OpenInterfaceRecord : PacketRecord
{
    public OpenInterfaceRecord(ReadOnlySpan<byte> payload) : base(0x0066, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"OPEN_INTERFACE truncated -- {Payload.Length} bytes, expected 8"); return; }
        FDec(sb, 0, "UIChange", ReadI32LE(Payload, 0));
        FDec(sb, 4, "UIType",   ReadI32LE(Payload, 4));
    }
}
