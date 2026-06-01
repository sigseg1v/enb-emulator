// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x001B AUX_DATA. Variable-layout opaque carrier used for many sub-types.
///
/// Common Aux outer structure (AuxPlayerIndex / AuxShipIndex /
/// AuxManufacturingIndex and most other Aux classes):
///   u32   GameID       (offset 0, LE)
///   u16   BodyLen      (offset 4, LE) = payload_size - 6; 0 when the Aux
///                      class doesn't fill it in post-build (AuxPlayerIndex)
///   u8    Version      (offset 6) = 1
///   u8[]  Flags        (offset 7+, N bytes of change-bitmask)
///   ...   conditional data, flag-driven
///
/// Strings inside the conditional data use AuxBase::AddString encoding:
///   u16   len   (LE)
///   char[] str  (len bytes, NO NUL)
/// This is distinct from AddDataLS (same) and AddDataLSN (NUL-included).
///
/// The "simple name" sub-types from PlayerConnection.cpp (SendResourceName,
/// SendSimpleAuxName, SendAuxNameResource, etc.) are NOT the Aux-class
/// outer structure -- they start at offset 0 with GameID (4B), then have
/// their own inline length/type header at offset 4-7.
///
/// Source: AuxManufacturingIndex.cpp, AuxShipIndex.cpp, AuxPlayerIndex.cpp,
///         PlayerConnection.cpp SendResourceName/SendAuxNameResource.
/// </summary>
public sealed class AuxDataRecord : PacketRecord
{
    public AuxDataRecord(ReadOnlySpan<byte> payload) : base(0x001B, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4)
        {
            Flag(sb, $"AUX_DATA truncated -- {Payload.Length} bytes, expected >= 4");
            return;
        }
        int gameId = ReadI32LE(Payload, 0);
        FieldHex(sb, "GameID", gameId);

        if (Payload.Length < 7)
            return;

        ushort bodyLen = ReadU16LE(Payload, 4);
        byte version   = Payload[6];

        // Version == 1 and BodyLen == payload-6 (or 0 for types that skip
        // the post-build fill) identifies the common Aux outer structure.
        bool isAuxOuter = version == 1
            && (bodyLen == 0 || bodyLen == (ushort)(Payload.Length - 6));

        if (isAuxOuter)
        {
            FieldDec(sb, "BodyLen", bodyLen,
                bodyLen == 0 ? "(not filled -- AuxPlayerIndex pattern)" : null);
            FieldDec(sb, "Version", version);

            // Flags start at offset 7; scan the rest for AddString-encoded strings.
            var strings = ExtractAddStrings(Payload.AsSpan(7), minLen: 2);
            foreach (var (offset, value) in strings)
                Field(sb, $"  str@{offset + 7:X4}", Quote(value));
            return;
        }

        // Non-Aux-outer sub-types: check for known inline layouts from
        // PlayerConnection.cpp before falling back to AddString scan.

        if (Payload.Length >= 10)
        {
            ushort inlineLen  = ReadU16LE(Payload, 4);
            ushort inlineType = ReadU16LE(Payload, 6);

            // 0x1201: SendResourceName / SendSimpleAuxName
            if (inlineType == 0x1201)
            {
                short strLen = ReadI16LE(Payload, 8);
                FieldDec(sb, "InlineLen",  inlineLen);
                Field(sb,    "SubType",    "0x1201  (resource name)");
                if (strLen > 0 && Payload.Length >= 10 + strLen)
                {
                    string name = ReadNulString(Payload.AsSpan(10, strLen));
                    FieldString(sb, "Name", name, requiredNonEmpty: true);
                    return;
                }
            }

            // 0x0116 / 0x1603 / 0x03E0: husk/mob name variants
            if (inlineType == 0x0116 || inlineType == 0x1603 || inlineType == 0x03E0)
            {
                Field(sb, "SubType", $"0x{inlineType:X4}  (husk/mob name)");
                var strings = ExtractAddStrings(Payload.AsSpan(8), minLen: 2);
                foreach (var (off, value) in strings)
                    Field(sb, $"  str@{off + 8:X4}", Quote(value));
                return;
            }
        }

        // Generic fallback: use AddString scan across the whole payload
        // (starts past the 4-byte GameID so we skip non-string header bytes).
        {
            var strings = ExtractAddStrings(Payload.AsSpan(4), minLen: 2);
            foreach (var (off, value) in strings)
                Field(sb, $"  str@{off + 4:X4}", Quote(value));
        }
    }

    // Scan for AuxBase::AddString-encoded strings: u16(len) + char[len].
    // Only emits strings where all chars are printable ASCII and len >= minLen.
    // Advances by 1 on mismatch so it can re-sync after binary runs.
    private static List<(int offset, string value)> ExtractAddStrings(
        ReadOnlySpan<byte> span, int minLen)
    {
        var result = new List<(int, string)>();
        int i = 0;
        while (i + 2 <= span.Length)
        {
            int len = span[i] | (span[i + 1] << 8);
            if (len >= minLen && len <= 256 && i + 2 + len <= span.Length)
            {
                bool ok = true;
                for (int k = 0; k < len; k++)
                {
                    byte b = span[i + 2 + k];
                    if (b < 0x20 || b >= 0x7F) { ok = false; break; }
                }
                if (ok)
                {
                    result.Add((i, System.Text.Encoding.ASCII.GetString(span.Slice(i + 2, len))));
                    i += 2 + len;
                    continue;
                }
            }
            i++;
        }
        return result;
    }
}
