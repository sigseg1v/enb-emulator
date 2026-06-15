// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// STARBASE_ROOM_CHANGE -- shared 0x009F (client->server) / 0x00A0
/// (server->client). Wire (struct StarbaseRoomChange, 12 bytes, LITTLE-ENDIAN):
/// int32 AvatarID; int32 NewRoom; int32 OldRoom. The 0x9F variant is the client
/// telling the server it walked from OldRoom to NewRoom inside a starbase; the
/// server rebroadcasts that to everyone else in the room as a byte-identical
/// 0xA0 reply (built from the same struct -- all three 0xA0 emitters in
/// PlayerClass.cpp do SendOpcode(0xA0, &SRoomUpdate, sizeof(SRoomUpdate))).
/// Every field is little-endian: Player::HandleStarbaseRoomChange reads
/// change->NewRoom / change->OldRoom with no ntohl, and the 0xA0 emitter
/// direct-assigns AvatarID/NewRoom/OldRoom with no htonl. On 0x9F AvatarID is
/// the sender's own GameID (the server ignores it and substitutes GameID());
/// on 0xA0 it is the moving player's GameID the receiving client must relocate.
/// OldRoom == -1 with NewRoom == 0 is the "just entered the station" case; a
/// NewRoom == -1 (seen on 0xA0) is a player leaving the room. The struct field
/// order is AvatarID, NewRoom, OldRoom -- NewRoom precedes OldRoom on the wire.
/// </summary>
public sealed class StarbaseRoomChangeRecord : PacketRecord
{
    public StarbaseRoomChangeRecord(ReadOnlySpan<byte> payload, ushort opcode) : base(opcode, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 12) { Flag(sb, $"STARBASE_ROOM_CHANGE truncated -- {Payload.Length} bytes, expected 12"); return; }

        bool s2c = Opcode == 0x00A0;
        FHex(sb, 0, "AvatarID", ReadI32LE(Payload, 0), s2c ? "(LE; moving player GameID)" : "(LE; player GameID)");
        int newRoom = ReadI32LE(Payload, 4);
        FDec(sb, 4, "NewRoom",  newRoom, newRoom == -1 ? "(LE; -1 = left the room)" : "(LE; destination room)");
        int oldRoom = ReadI32LE(Payload, 8);
        FDec(sb, 8, "OldRoom",  oldRoom, oldRoom == -1 ? "(LE; -1 = just entered station)" : "(LE; previous room)");
    }
}
