// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0041 FORMATION_POSITIONAL_UPDATE. Wire (struct FormationPositionalUpdate,
/// PacketStructures.h:521 -- fields emit in DECLARATION order, the stale
/// `this[16]`/`this[12]` comments notwithstanding):
///   int32 TargetID; int32 LeaderID; float Position[3]. = 20 bytes.
/// Emitter: Player::SendFormationPositionalUpdate (PlayerConnection.cpp:1222);
/// all fields host-order LE.
/// </summary>
public sealed class FormationPositionalUpdateRecord : PacketRecord
{
    public FormationPositionalUpdateRecord(ReadOnlySpan<byte> payload) : base(0x0041, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 20) { Flag(sb, $"FORMATION_POSITIONAL_UPDATE truncated -- {Payload.Length} bytes, expected 20"); return; }
        int   targetId = ReadI32LE(Payload, 0);
        int   leaderId = ReadI32LE(Payload, 4);
        float px = ReadF32LE(Payload, 8), py = ReadF32LE(Payload, 12), pz = ReadF32LE(Payload, 16);
        FHex(sb,   0, "TargetID", targetId);
        FHex(sb,   4, "LeaderID", leaderId);
        FBytes(sb, 8, 12, "Position", $"({px:0.0##}, {py:0.0##}, {pz:0.0##})");
    }
}
