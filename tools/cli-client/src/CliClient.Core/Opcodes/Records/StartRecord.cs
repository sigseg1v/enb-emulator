// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0005 START -- in-sector handshake terminator. Payload is a single
/// int32 start_id (matches the player's CharacterID / GameID).
/// Server emitter: Player::SendStart in server/src/PlayerConnection.cpp.
/// </summary>
public sealed class StartRecord : PacketRecord
{
    public StartRecord(ReadOnlySpan<byte> payload) : base(0x0005, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4)
        {
            Flag(sb, $"START truncated -- {Payload.Length} bytes, expected 4");
            return;
        }
        int startId = ReadI32LE(Payload, 0);
        FieldHex(sb, "StartID", startId, "(= player's GameID; client uses this as self-id)");
        FlagSuspicious(sb, "StartID", startId);
        if (Payload.Length > 4)
            Flag(sb, $"START has {Payload.Length - 4} trailing bytes");
    }
}
