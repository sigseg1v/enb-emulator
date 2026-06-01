// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x00A5 CLIENT_CHAT_EVENT. Wire (Player::SendClientChatEvent):
///   int32 Type (CHEV_*); int32 unknown (=0);
///   AddDataLS LastName; AddDataLS LastName (duplicated -- Rank slot is
///   commented out server-side); AddDataLS OtherPlayer; AddDataLS Channel;
///   AddDataLS Message; int16 0 (empty 6th-string length); int32 0 (trailing).
/// AddDataLS = int16 length (LE) + chars, no NUL.
/// CHEV: 1 = LOGGED_IN, 0x1A = ALL_STATUS.
/// </summary>
public sealed class ClientChatEventRecord : PacketRecord
{
    private static readonly string[] StrLabels = { "LastName", "LastName2", "OtherPlayer", "Channel", "Message" };

    public ClientChatEventRecord(ReadOnlySpan<byte> payload) : base(0x00A5, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"CLIENT_CHAT_EVENT truncated -- {Payload.Length} bytes"); return; }
        int type = ReadI32LE(Payload, 0);
        string tn = type switch { 1 => " (LOGGED_IN)", 0x1A => " (ALL_STATUS)", _ => "" };
        FHex(sb, 0, "Type", type, tn.Length > 0 ? tn.Trim() : null);
        FHex(sb, 4, "Unknown", ReadI32LE(Payload, 4));
        int off = 8;
        foreach (var label in StrLabels)
        {
            if (off + 2 > Payload.Length) return;
            ushort len = ReadU16LE(Payload, off);
            if (off + 2 + len > Payload.Length) { Flag(sb, $"{label} length {len} overruns"); return; }
            string s = Encoding.ASCII.GetString(Payload, off + 2, len);
            F(sb, off, 2 + len, label, Quote(s));
            off += 2 + len;
        }
        if (off + 2 <= Payload.Length) { FDec(sb, off, "Unknown6Len", ReadI16LE(Payload, off)); off += 2; }
        if (off + 4 <= Payload.Length) { FHex(sb, off, "Trailing", ReadI32LE(Payload, off)); }
    }
}
