// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x001B AUX_DATA. Variable-layout opaque carrier. The emitter uses this
/// opcode for several distinct payloads (player info, resource names, husk
/// content, etc.), all sharing a 4-byte int32 player/entity ID at offset 0.
/// ResourceName shape (most common in the handshake stream):
///   int32  entity_id   (offset 0)
///   int16  data_len    (offset 4, = strlen(name) + 4)
///   int16  data_type   (offset 6, = 0x1201 for resource name)
///   int16  str_len     (offset 8)
///   char[] name        (offset 10, NUL-terminated)
/// Unrecognised sub-types fall back to ASCII string extraction.
/// Source: PlayerConnection.cpp SendResourceName(), SendAuxData().
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
        int entityId = ReadI32LE(Payload, 0);
        FieldHex(sb, "EntityID", entityId);

        if (Payload.Length >= 10)
        {
            short dataLen  = ReadI16LE(Payload, 4);
            short dataType = ReadI16LE(Payload, 6);
            short strLen   = ReadI16LE(Payload, 8);

            FieldDec(sb, "DataLen",  dataLen);
            FieldHex(sb, "DataType", (ushort)dataType,
                dataType == 0x1201 ? "(resource name)" : null);

            if (dataType == 0x1201 && Payload.Length >= 10 + strLen)
            {
                string name = ReadNulString(Payload.AsSpan(10, Math.Min(strLen, Payload.Length - 10)));
                FieldString(sb, "Name", name, requiredNonEmpty: true);
                return;
            }
        }

        // Fallback: surface any embedded ASCII strings
        var strings = ExtractAsciiStrings(Payload.AsSpan(), 3);
        foreach (var (offset, value) in strings)
            Field(sb, $"  str@{offset:X4}", Quote(value));
    }
}
