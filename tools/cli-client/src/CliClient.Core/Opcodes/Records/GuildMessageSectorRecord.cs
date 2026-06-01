// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x00D0 GUILD_MESSAGE_SECTOR. Wire (variable):
///   int32 Type;
///   AddDataLS OtherName: uint16 len + len bytes UTF-8 (no NUL);
///   AddDataLS GuildName: uint16 len + len bytes UTF-8 (no NUL).
///
/// Type values (from Guilds.h):
///   1=Success1, 2=Success2, 3=ListMembers, 5=LowerPane, 6=MOTD,
///   7=InternalError.
/// </summary>
public sealed class GuildMessageSectorRecord : PacketRecord
{
    private static readonly Dictionary<int, string> TypeNames = new()
    {
        { 1, "Success1"      },
        { 2, "Success2"      },
        { 3, "ListMembers"   },
        { 5, "LowerPane"     },
        { 6, "MOTD"          },
        { 7, "InternalError" },
    };

    public GuildMessageSectorRecord(ReadOnlySpan<byte> payload) : base(0x00D0, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"GUILD_MESSAGE_SECTOR truncated -- {Payload.Length} bytes, expected >= 4"); return; }

        int type = ReadI32LE(Payload, 0);
        string typeName = TypeNames.TryGetValue(type, out var n) ? n : "Unknown";
        F(sb, 0, 4, "Type", $"{type}  ({typeName})");

        int off = 4;
        if (!TryReadAddDataLS(sb, ref off, "OtherName")) return;
        if (!TryReadAddDataLS(sb, ref off, "GuildName"))  return;
    }

    private bool TryReadAddDataLS(StringBuilder sb, ref int off, string name)
    {
        if (off + 2 > Payload.Length)
        {
            Flag(sb, $"{name}: truncated -- offset {off}, only {Payload.Length - off} bytes remain (need 2 for length)");
            return false;
        }
        ushort len = ReadU16LE(Payload, off);
        Mark(off, 2);
        off += 2;
        if (off + len > Payload.Length)
        {
            Flag(sb, $"{name}: truncated -- need {len} bytes of string data at offset {off}, only {Payload.Length - off} remain");
            return false;
        }
        string value = Encoding.UTF8.GetString(Payload, off, len);
        FStr(sb, off, len, name, value);
        off += len;
        return true;
    }
}
