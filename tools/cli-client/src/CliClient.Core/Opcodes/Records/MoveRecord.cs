// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0014 MOVE. Wire (struct MovePacket, 5 bytes, LE, no byte-swap):
///   int32 GameID; byte type.
/// Client->server throttle command. The server reads Movement->type raw in
/// Player::HandleMove and branches type == 4 (engine off / break formation) vs
/// else (engine on); Move(type) drives the actual speed change.
/// Source: struct MovePacket (PacketStructures.h), Player::HandleMove
/// (PlayerConnection.cpp). Pinned to capture_3.rar Packet 5557 (Client->Server).
/// </summary>
public sealed class MoveRecord : PacketRecord
{
    public MoveRecord(ReadOnlySpan<byte> payload) : base(0x0014, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 5) { Flag(sb, $"MOVE truncated -- {Payload.Length} bytes, expected 5"); return; }
        FHex(sb, 0, "GameID", ReadI32LE(Payload, 0));
        byte type = Payload[4];
        FDec(sb, 4, "Type", type, type == 4 ? "(engine off / break formation)" : "(engine on)");
    }
}
