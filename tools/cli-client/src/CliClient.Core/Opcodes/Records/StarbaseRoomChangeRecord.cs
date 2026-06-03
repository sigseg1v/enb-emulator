// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x009F STARBASE_ROOM_CHANGE (client->server). Wire (struct
/// StarbaseRoomChange, 12 bytes, LITTLE-ENDIAN): int32 AvatarID; int32 NewRoom;
/// int32 OldRoom. The client tells the server it walked from OldRoom to NewRoom
/// inside a starbase. Every field is little-endian: Player::HandleStarbaseRoomChange
/// casts the buffer to StarbaseRoomChange* and reads change->NewRoom /
/// change->OldRoom with no ntohl. AvatarID is the player's own GameID (the
/// server actually ignores it on this path and substitutes GameID(), then
/// rebroadcasts the move to the room as a 0x00A0 reply using the same struct).
/// OldRoom == -1 with NewRoom == 0 is the "just entered the station, no room
/// yet" case the handler special-cases (m_Room = -1). Note the struct field
/// order is AvatarID, NewRoom, OldRoom -- NewRoom precedes OldRoom on the wire.
/// Source: struct StarbaseRoomChange (PacketStructures.h:805),
/// Player::HandleStarbaseRoomChange (PlayerClass.cpp:631). Pinned to
/// capture_3.rar (Client->Server).
/// </summary>
public sealed class StarbaseRoomChangeRecord : PacketRecord
{
    public StarbaseRoomChangeRecord(ReadOnlySpan<byte> payload) : base(0x009F, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 12) { Flag(sb, $"STARBASE_ROOM_CHANGE truncated -- {Payload.Length} bytes, expected 12"); return; }

        FHex(sb, 0, "AvatarID", ReadI32LE(Payload, 0), "(LE; player GameID)");
        FDec(sb, 4, "NewRoom",  ReadI32LE(Payload, 4), "(LE; destination room)");
        int oldRoom = ReadI32LE(Payload, 8);
        FDec(sb, 8, "OldRoom",  oldRoom, oldRoom == -1 ? "(LE; -1 = just entered station)" : "(LE; previous room)");
    }
}
