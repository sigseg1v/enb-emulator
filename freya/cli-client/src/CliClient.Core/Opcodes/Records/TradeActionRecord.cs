// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x001F TRADE (server->client). Wire (5 bytes): int32 GameID; u8 Action.
/// Player::TradeAction(GameID, Action) writes the partner's GameID as a raw
/// int32 (no htonl, so little-endian) and a single Action byte, then
/// SendOpcode(..., buffer, 5). GameID is the trade partner's in-sector object
/// id (0 when the action carries no partner, e.g. confirm/money). Action codes
/// observed at the emitter's call sites (PlayerConnection.cpp):
///   0 = open trade window      1 = close trade window
///   2 = trade complete (both confirmed -- close + reset)
///   3 = you confirmed          4 = trade money updated
///   5 = partner confirmed      6 = cancel confirmations
/// </summary>
public sealed class TradeActionRecord : PacketRecord
{
    public TradeActionRecord(ReadOnlySpan<byte> payload) : base(0x001F, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 5) { Flag(sb, $"TRADE truncated -- {Payload.Length} bytes, expected 5"); return; }

        FHex(sb, 0, "GameID", ReadI32LE(Payload, 0), "(LE; trade partner object id; 0 = none)");
        byte action = Payload[4];
        FDec(sb, 4, "Action", action, ActionNote(action));
    }

    private static string ActionNote(byte action) => action switch
    {
        0 => "(open trade window)",
        1 => "(close trade window)",
        2 => "(trade complete -- close + reset)",
        3 => "(you confirmed)",
        4 => "(trade money updated)",
        5 => "(partner confirmed)",
        6 => "(cancel confirmations)",
        _ => "(unknown action)",
    };
}
