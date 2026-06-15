// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x1004 MVAS_SEND_POSITION_C_S. The MVAS (Move-Assist) position datagram on
/// the dedicated UDP channel (MVAS_LOGIN_PORT). The payload here is the body
/// AFTER the 12-byte EnbUdpHeader { u16 size; u16 opcode; u32 player_id; u32
/// sequence } -- the header is owned by the datagram framing, not this record.
///
/// Several body forms exist on the wire, distinguished by body length (the
/// server gates the heading read on the datagram size: it reads the heading
/// only when size &gt; 28, i.e. body &gt;= 24, and always ignores a trailing
/// u32):
///   * 28-byte full form (retail): float PosX,PosY,PosZ; float HeadX,HeadY,HeadZ;
///     u32 Trailing(=0). 40-byte datagram including the header.
///   * 24-byte form: pos[3] + heading[3], no trailing u32 (our own feed emits
///     this -- functionally server-equivalent).
///   * 16-byte form: pos[3] + u32.
///   * 12-byte position-only form: pos[3].
/// </summary>
public sealed class MvasSendPositionRecord : PacketRecord
{
    public MvasSendPositionRecord(ReadOnlySpan<byte> payload) : base(0x1004, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 12)
        {
            Flag(sb, $"MVAS_SEND_POSITION truncated -- {Payload.Length} bytes, expected at least 12 (pos[3])");
            return;
        }

        FFloat(sb, 0, "PosX", ReadF32LE(Payload, 0));
        FFloat(sb, 4, "PosY", ReadF32LE(Payload, 4));
        FFloat(sb, 8, "PosZ", ReadF32LE(Payload, 8));

        // The server reads the heading only when body >= 24 (datagram size > 28).
        bool hasHeading = Payload.Length >= 24;
        if (hasHeading)
        {
            FFloat(sb, 12, "HeadX", ReadF32LE(Payload, 12));
            FFloat(sb, 16, "HeadY", ReadF32LE(Payload, 16));
            FFloat(sb, 20, "HeadZ", ReadF32LE(Payload, 20));

            // The 28-byte full form carries a trailing u32 the server ignores.
            if (Payload.Length >= 28)
                FHex(sb, 24, "Trailing", ReadU32LE(Payload, 24),
                    "(server-ignored; 0 on the wire)");
        }
        else if (Payload.Length >= 16)
        {
            // Short pos+u32 form: no heading, one trailing u32.
            FHex(sb, 12, "Trailing", ReadU32LE(Payload, 12),
                "(server-ignored; 0 on the wire)");
        }
    }
}
