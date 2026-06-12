// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0065 UI_TRIGGER. Wire (PlayerConnection.cpp:7580, /uitrigger GM command):
///   int32 ParamA (LE); int32 ParamB (LE). = 8 bytes.
/// </summary>
public sealed class UiTriggerRecord : PacketRecord
{
    public UiTriggerRecord(ReadOnlySpan<byte> payload) : base(0x0065, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"UI_TRIGGER truncated -- {Payload.Length} bytes, expected 8"); return; }
        FDec(sb, 0, "ParamA", ReadI32LE(Payload, 0));
        FDec(sb, 4, "ParamB", ReadI32LE(Payload, 4));
    }
}
