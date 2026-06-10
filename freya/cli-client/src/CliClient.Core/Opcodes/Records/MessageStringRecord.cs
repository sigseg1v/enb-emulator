// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x001D MESSAGE_STRING. Wire (variable):
///   int16 length (strlen+1); uint8 color; char[length] message (NUL-terminated).
/// Total sent = length + 3.
/// </summary>
public sealed class MessageStringRecord : PacketRecord
{
    public MessageStringRecord(ReadOnlySpan<byte> payload) : base(0x001D, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 3) { Flag(sb, $"MESSAGE_STRING truncated -- {Payload.Length} bytes, expected >= 3"); return; }
        short length = ReadI16LE(Payload, 0);
        byte  color  = Payload[2];
        int   msgLen = Math.Max(0, Math.Min(length, Payload.Length - 3));
        string msg   = length > 0 ? ReadNulString(Payload.AsSpan(3, msgLen)) : "";
        FDec(sb, 0, "Length", length, "(strlen+1)");
        FDec(sb, 2, "Color",  color,
            color == 5  ? "(top panel green)"      :
            color == 4  ? "(group purple)"          :
            color == 7  ? "(system)"                :
            color == 17 ? "(yellow warning)"        :
            color == 11 ? "(orange warning)"        : null);
        FStr(sb, 3, msgLen, "Message", msg);
    }
}
