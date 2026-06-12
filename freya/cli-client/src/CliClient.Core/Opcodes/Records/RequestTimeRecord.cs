// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0044 REQUEST_TIME (client->server). Wire (4 bytes): int32 ClientTick.
/// The client sends its own millisecond tick; the server echoes it back in a
/// 0x34 SET_CLIENT_TIME reply (the "ClientSent" field) so the client can
/// measure round-trip latency. Player::HandleRequestTime reads it as
/// *((int32_t*) data) with no ntohl, so it is little-endian, then calls
/// SendClientSetTime(tick). Across a session the value climbs monotonically
/// (it is the client's uptime tick, not a wall clock).
/// Source: Player::HandleRequestTime (PlayerConnection.cpp:1629). Pinned to
/// capture_3.rar (Client->Server). The 0x34 reply is decoded by
/// <see cref="ClientSetTimeRecord"/>.
/// </summary>
public sealed class RequestTimeRecord : PacketRecord
{
    public RequestTimeRecord(ReadOnlySpan<byte> payload) : base(0x0044, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"REQUEST_TIME truncated -- {Payload.Length} bytes, expected 4"); return; }
        FDec(sb, 0, "ClientTick", ReadI32LE(Payload, 0), "(LE; client ms tick, echoed in 0x34 reply)");
    }
}
