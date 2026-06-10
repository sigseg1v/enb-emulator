// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x001E GROUP. Wire (PlayerConnection.cpp:3700 SendConfirmation /
/// GroupManager.cpp:436 SendGroupInvite):
///   int16 Len (LE, = strlen(msg)+1); uint8 Flag (= 0x01); uint8 Message[Len]
///   (NUL-terminated; the NUL is included in Len). Total = 3 + Len.
/// </summary>
public sealed class GroupRecord : PacketRecord
{
    public GroupRecord(ReadOnlySpan<byte> payload) : base(0x001E, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 3) { Flag(sb, $"GROUP truncated -- {Payload.Length} bytes, expected >= 3"); return; }
        int len = ReadU16LE(Payload, 0);
        FDec(sb, 0, "Len", (short)len, "(includes trailing NUL)");
        FDec(sb, 2, "Flag", Payload[2]);
        if (3 + len > Payload.Length)
        {
            Flag(sb, $"GROUP: Message needs {len} bytes at offset 3, only {Payload.Length - 3} remain");
            return;
        }
        // Len counts the trailing NUL; display the text without it but mark all Len bytes.
        int textLen = len > 0 ? len - 1 : 0;
        string msg = Encoding.Latin1.GetString(Payload, 3, textLen);
        FStr(sb, 3, len, "Message", msg);
    }
}
