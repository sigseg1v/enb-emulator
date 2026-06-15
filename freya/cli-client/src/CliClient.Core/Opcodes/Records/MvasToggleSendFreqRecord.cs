// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x1007 MVAS_TOGGLE_SEND_FREQ_S_C. Server-to-client control datagram on the
/// MVAS (Move-Assist) UDP channel: the server tells the client how often to
/// emit MVAS_SEND_POSITION (0x1004). 16-byte datagram on the wire; the payload
/// here is the body AFTER the 12-byte EnbUdpHeader { u16 size; u16 opcode;
/// u32 player_id; u32 sequence }, i.e. a single u32:
///   u32 Frequency @0   (e.g. 02 00 00 00 == 2)
///
/// Source: retail proxy&lt;-&gt;server captures (planet land/fly/dock leg --
/// the server emits this in response to the client's position feed).
/// </summary>
public sealed class MvasToggleSendFreqRecord : PacketRecord
{
    public MvasToggleSendFreqRecord(ReadOnlySpan<byte> payload) : base(0x1007, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4)
        {
            Flag(sb, $"MVAS_TOGGLE_SEND_FREQ truncated -- {Payload.Length} bytes, expected 4 (u32 Frequency)");
            return;
        }
        FDec(sb, 0, "Frequency", ReadI32LE(Payload, 0));
    }
}
