// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0020 PRIORITY_MESSAGE. On-screen banner message (e.g. "Discovered Sector
/// Gate to Ishuan ... Explore experience earned"). Variable, per
/// PlayerConnection.cpp:11000-11010:
///   char   Message1[]  @0    (null-terminated)
///   char   Message2[]  @..   (null-terminated, usually "MessageLine")
///   int32  Time        @..   (display time, ms)
///   int32  Priority    @..
/// </summary>
public sealed class PriorityMessageRecord : PacketRecord
{
    public PriorityMessageRecord(ReadOnlySpan<byte> payload) : base(0x0020, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        int off = 0;

        int end1 = IndexOfNul(off);
        if (end1 < 0) { Flag(sb, "PRIORITY_MESSAGE Message1 has no null terminator"); return; }
        FStr(sb, off, end1 - off, "Message1", Encoding.Latin1.GetString(Payload, off, end1 - off), required: false);
        Mark(end1, 1); // null terminator
        off = end1 + 1;

        int end2 = IndexOfNul(off);
        if (end2 < 0) { Flag(sb, "PRIORITY_MESSAGE Message2 has no null terminator"); return; }
        FStr(sb, off, end2 - off, "Message2", Encoding.Latin1.GetString(Payload, off, end2 - off), required: false);
        Mark(end2, 1);
        off = end2 + 1;

        if (off + 8 > Payload.Length) { Flag(sb, $"PRIORITY_MESSAGE missing trailing Time/Priority -- {Payload.Length - off} bytes left"); return; }
        FDec(sb, off,     "Time",     ReadI32LE(Payload, off));
        FDec(sb, off + 4, "Priority", ReadI32LE(Payload, off + 4));
    }

    private int IndexOfNul(int from)
    {
        for (int i = from; i < Payload.Length; i++)
            if (Payload[i] == 0) return i;
        return -1;
    }
}
