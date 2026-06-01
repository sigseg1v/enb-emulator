// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0047 CLIENT_SHIP. Wire shape: int32 GameID (4 bytes).
/// Emitter marshals through an int32_t temporary to avoid LP64 sizeof(long)=8.
/// Sent immediately after 0x0037 CLIENT_AVATAR with the same GameID.
/// </summary>
public sealed class ClientShipRecord : PacketRecord
{
    public ClientShipRecord(ReadOnlySpan<byte> payload) : base(0x0047, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4)
        {
            Flag(sb, $"CLIENT_SHIP truncated -- {Payload.Length} bytes, expected 4");
            return;
        }
        int gameId = ReadI32LE(Payload, 0);
        FieldHex(sb, "GameID", gameId);
        FlagSuspicious(sb, "GameID", gameId);
    }
}
