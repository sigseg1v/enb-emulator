// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x001B AUX_DATA. Variable-layout opaque carrier. Common Aux outer structure
/// (AuxManufacturingIndex, AuxShipIndex, AuxPlayerIndex):
///   u32 GameID; u16 BodyLen (= payload-6, 0 for AuxPlayerIndex); u8 Version=1;
///   u8+ Flags; ... conditional data with AuxBase::AddString-encoded strings (u16 len + chars, no NUL).
/// Non-Aux sub-types from PlayerConnection.cpp (SendResourceName etc.) use their own inline layout.
/// Source: AuxManufacturingIndex.cpp, AuxShipIndex.cpp, AuxPlayerIndex.cpp, PlayerConnection.cpp.
/// </summary>
public sealed class AuxDataRecord : PacketRecord
{
    public AuxDataRecord(ReadOnlySpan<byte> payload) : base(0x001B, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"AUX_DATA truncated -- {Payload.Length} bytes, expected >= 4"); return; }
        int gameId = ReadI32LE(Payload, 0);
        FHex(sb, 0, "GameID", gameId);
        if (Payload.Length < 7) return;
        ushort bodyLen = ReadU16LE(Payload, 4);
        byte   version = Payload[6];
        bool isAuxOuter = version == 1 && (bodyLen == 0 || bodyLen == (ushort)(Payload.Length - 6));
        if (isAuxOuter)
        {
            FDec(sb, 4, "BodyLen", (short)bodyLen, bodyLen == 0 ? "(not filled -- AuxPlayerIndex pattern)" : null);
            FDec(sb, 6, "Version", version);
            var strings = ExtractAddStrings(Payload.AsSpan(7), minLen: 2);
            foreach (var (soff, value) in strings)
            {
                int absOff = soff + 7;
                FStr(sb, absOff, 2 + value.Length, $"str@{absOff:X4}", value);
            }
            return;
        }
        if (Payload.Length >= 10)
        {
            ushort inlineLen  = ReadU16LE(Payload, 4);
            ushort inlineType = ReadU16LE(Payload, 6);
            if (inlineType == 0x1201)
            {
                short strLen = ReadI16LE(Payload, 8);
                FDec(sb, 4, "InlineLen", (short)inlineLen);
                F(sb,   6, 2, "SubType",   "0x1201  (resource name)");
                if (strLen > 0 && Payload.Length >= 10 + strLen)
                {
                    string name = ReadNulString(Payload.AsSpan(10, strLen));
                    FStr(sb, 10, strLen, "Name", name, required: true);
                    return;
                }
            }
            if (inlineType == 0x0116 || inlineType == 0x1603 || inlineType == 0x03E0)
            {
                F(sb, 6, 2, "SubType", $"0x{inlineType:X4}  (husk/mob name)");
                var strings = ExtractAddStrings(Payload.AsSpan(8), minLen: 2);
                foreach (var (soff, value) in strings) { int absOff = soff + 8; FStr(sb, absOff, 2 + value.Length, $"str@{absOff:X4}", value); }
                return;
            }
        }
        {
            var strings = ExtractAddStrings(Payload.AsSpan(4), minLen: 2);
            foreach (var (soff, value) in strings) { int absOff = soff + 4; FStr(sb, absOff, 2 + value.Length, $"str@{absOff:X4}", value); }
        }
    }

    private static List<(int offset, string value)> ExtractAddStrings(ReadOnlySpan<byte> span, int minLen)
    {
        var result = new List<(int, string)>();
        int i = 0;
        while (i + 2 <= span.Length)
        {
            int len = span[i] | (span[i + 1] << 8);
            if (len >= minLen && len <= 256 && i + 2 + len <= span.Length)
            {
                bool ok = true;
                for (int k = 0; k < len; k++) { byte b = span[i + 2 + k]; if (b < 0x20 || b >= 0x7F) { ok = false; break; } }
                if (ok) { result.Add((i, System.Text.Encoding.ASCII.GetString(span.Slice(i + 2, len)))); i += 2 + len; continue; }
            }
            i++;
        }
        return result;
    }
}
