// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0021 QueueMessageLine / 0x0022 PushMessageLine. Shared wire layout
/// (variable length, LE, no byte-swap):
///   char Message[];   // raw chars, NUL-terminated (AddDataS + explicit NUL)
///   char Type[];      // raw chars, NUL-terminated -- the chat channel
///                     // ("MessageLine", "QuickLine", ...)
///   int32 Time;       // display duration in ms
///   int32 Priority;
/// Neither string carries a length prefix -- AddDataS memcpys strlen() bytes and
/// the terminator comes from a separate AddData(char(0)), so the decoder walks to
/// each NUL. The two int32 tail fields are written raw host-order via
/// AddData(long) (no ntohl), hence LE.
///
/// 0x22 is fully grounded in Player::SendPushMessage(msg, type, time, priority)
/// (PlayerConnection.cpp), which emits ENB_OPCODE_0022_PUSH_MESSAGE -- the
/// "LEVEL UP!"/"QuickLine"/0/3 retail frame matches SendPushMessage("LEVEL UP!",
/// "QuickLine", 0, 3) byte-for-byte. 0x21 QueueMessageLine is the retail sibling
/// our server never emits; it has the identical wire shape, so the same field
/// model is applied (Time corroborated by the capture's 3000 == the 0x22
/// MessageLine duration). Pinned to capture_3.rar Packet 1676-area #1 (0x22) and
/// the group-bonus 0x21 frames, all Server->Client.
/// </summary>
public sealed class PushMessageRecord : PacketRecord
{
    public PushMessageRecord(ReadOnlySpan<byte> payload, ushort opcode) : base(opcode, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        int o = 0;
        if (!TryReadCString(o, out string message, out int afterMsg))
        { Flag(sb, "Message string is unterminated"); return; }
        FStr(sb, o, afterMsg - o, "Message", message, required: true);
        o = afterMsg;

        if (!TryReadCString(o, out string type, out int afterType))
        { Flag(sb, "Type (channel) string is unterminated"); return; }
        FStr(sb, o, afterType - o, "Type", type, required: true);
        o = afterType;

        if (Payload.Length < o + 8)
        { Flag(sb, $"tail truncated -- need 8 bytes at offset {o}, have {Payload.Length - o}"); return; }
        FDec(sb, o, "Time", ReadI32LE(Payload, o), "(display ms)");
        FDec(sb, o + 4, "Priority", ReadI32LE(Payload, o + 4));
    }

    /// <summary>Read a NUL-terminated ASCII string at <paramref name="off"/>;
    /// <paramref name="nextOffset"/> lands just past the NUL. False if no NUL.</summary>
    private bool TryReadCString(int off, out string value, out int nextOffset)
    {
        value = "";
        nextOffset = off;
        ReadOnlySpan<byte> p = Payload;
        if (off >= p.Length) return false;
        int nul = -1;
        for (int i = off; i < p.Length; i++) { if (p[i] == 0) { nul = i; break; } }
        if (nul < 0) return false;
        value = Encoding.ASCII.GetString(p.Slice(off, nul - off));
        nextOffset = nul + 1;
        return true;
    }
}
