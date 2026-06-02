// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x008B ATTACKER_UPDATES. Server tells the client a mob has started/stopped
/// attacking it. 9 bytes, per PlayerConnection.cpp:3604-3620:
///   int32  Update  @0   (attacker state / count)
///   uint8  Fixed   @4   (always 0x01 in the capture)
///   int32  MobId   @5   (game id of the attacker)
/// </summary>
public sealed class AttackerUpdatesRecord : PacketRecord
{
    public AttackerUpdatesRecord(ReadOnlySpan<byte> payload) : base(0x008B, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 9) { Flag(sb, $"ATTACKER_UPDATES truncated -- {Payload.Length} bytes, expected 9"); return; }
        FDec(sb, 0, "Update", ReadI32LE(Payload, 0));
        byte fixedByte = Payload[4];
        FHex(sb, 4, "Fixed", fixedByte);
        if (fixedByte != 0x01) Flag(sb, $"ATTACKER_UPDATES fixed byte expected 0x01, got 0x{fixedByte:X2}");
        FHex(sb, 5, "MobId", ReadI32LE(Payload, 5));
    }
}
