// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x00A2 NOTIFY_EMOTE. Wire (struct NotifyEmote, PacketStructures.h:595):
///   int32 GameID; int32 Emote. = 8 bytes, LE.
/// Emitter PlayerConnection.cpp:10398 (SendToRangeList).
/// </summary>
public sealed class NotifyEmoteRecord : PacketRecord
{
    public NotifyEmoteRecord(ReadOnlySpan<byte> payload) : base(0x00A2, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"NOTIFY_EMOTE truncated -- {Payload.Length} bytes, expected 8"); return; }
        FHex(sb, 0, "GameID", ReadI32LE(Payload, 0));
        FDec(sb, 4, "Emote",  ReadI32LE(Payload, 4));
    }
}
