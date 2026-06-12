// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0037 CLIENT_AVATAR. Wire: int32 GameID (4 bytes, LE).
/// Emitter uses int32_t temporary to avoid LP64 sizeof(long)=8. Sent before 0x0047 CLIENT_SHIP.
/// </summary>
public sealed class ClientAvatarRecord : PacketRecord
{
    public ClientAvatarRecord(ReadOnlySpan<byte> payload) : base(0x0037, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"CLIENT_AVATAR truncated -- {Payload.Length} bytes, expected 4"); return; }
        int gameId = ReadI32LE(Payload, 0);
        FHex(sb, 0, "GameID", gameId);
        FlagSuspicious(sb, "GameID", gameId);
    }
}
