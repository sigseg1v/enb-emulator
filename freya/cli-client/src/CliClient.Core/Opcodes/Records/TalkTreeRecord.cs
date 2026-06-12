// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0054 TALK_TREE. NPC dialog tree. Variable, matching the server emitter
/// Player::GenerateTalkTree (server/src/PlayerMisc.cpp:32-109):
///   char   MainText[]      @0    (null-terminated dialog body)
///   uint8  Pad             @..   (one 0x00 byte: emitter does `ptr += 2 + len`
///                                 past the text, so a SECOND zero follows the
///                                 string's own null terminator)
///   uint8  NumBranches     @..
///   per branch:
///     uint8  Dest          (branch destination; emitter writes 1 byte then
///                           `ptr += 4`, so 3 zero pad bytes follow)
///     uint8  Pad[3]
///     char   BranchText[]  (null-terminated menu label)
/// Validated byte-exact against the live retail capture: all 9 dialog frames
/// consume to the last byte with this layout.
/// </summary>
public sealed class TalkTreeRecord : PacketRecord
{
    public TalkTreeRecord(ReadOnlySpan<byte> payload) : base(0x0054, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 1) { Flag(sb, "TALK_TREE empty"); return; }

        int mainEnd = IndexOfNul(0);
        if (mainEnd < 0) { Flag(sb, "TALK_TREE MainText has no null terminator"); return; }
        FStr(sb, 0, mainEnd, "MainText", Encoding.Latin1.GetString(Payload, 0, mainEnd), required: false);
        Mark(mainEnd, 1);   // string's own null terminator
        int off = mainEnd + 1;

        if (off >= Payload.Length) return; // no branch block
        Mark(off, 1);       // the extra zero from the emitter's `ptr += 2 + len`
        off++;

        if (off >= Payload.Length) return;
        byte numBranches = Payload[off];
        FDec(sb, off, "NumBranches", numBranches);
        off++;

        for (int i = 0; i < numBranches; i++)
        {
            if (off + 4 > Payload.Length) { Flag(sb, $"TALK_TREE branch {i} header runs past payload at {off:X4}"); return; }
            FDec(sb, off, $"Branch{i}.Dest", Payload[off]);
            Mark(off + 1, 3); // pad
            off += 4;

            int textEnd = IndexOfNul(off);
            if (textEnd < 0) { Flag(sb, $"TALK_TREE branch {i} text has no null terminator"); return; }
            FStr(sb, off, textEnd - off, $"Branch{i}.Text", Encoding.Latin1.GetString(Payload, off, textEnd - off), required: false);
            Mark(textEnd, 1);
            off = textEnd + 1;
        }
    }

    private int IndexOfNul(int from)
    {
        for (int i = from; i < Payload.Length; i++)
            if (Payload[i] == 0) return i;
        return -1;
    }
}
