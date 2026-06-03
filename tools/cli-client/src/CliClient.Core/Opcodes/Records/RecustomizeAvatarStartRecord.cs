// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0083 RECUSTOMIZE_AVATAR_START. Wire (struct RecustomizeAvatarStart,
/// PacketStructures.h:1097):
///   int32 costs[14]; int32 PlayerID. = 60 bytes.
/// Emitter: PlayerConnection.cpp:10033. costs[] are host-order LE; PlayerID is
/// emitted BE (`ras.playerid = htonl(pkt-&gt;PlayerID)`), matching the
/// RELATIONSHIP ObjectID convention.
/// </summary>
public sealed class RecustomizeAvatarStartRecord : PacketRecord
{
    public RecustomizeAvatarStartRecord(ReadOnlySpan<byte> payload) : base(0x0083, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 60) { Flag(sb, $"RECUSTOMIZE_AVATAR_START truncated -- {Payload.Length} bytes, expected 60"); return; }
        for (int i = 0; i < 14; i++)
            FDec(sb, i * 4, $"Cost[{i}]", ReadI32LE(Payload, i * 4));
        FHex(sb, 56, "PlayerID", ReadI32BE(Payload, 56), "(BE -- ntohl at emit)");
    }
}
