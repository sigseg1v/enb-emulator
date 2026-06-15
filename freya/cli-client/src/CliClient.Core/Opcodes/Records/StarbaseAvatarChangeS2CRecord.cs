// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x009E STARBASE_AVATAR_CHANGE (server->client). Wire (struct
/// StarbaseAvatarChange_S2C, 28 bytes, LE, no byte-swap):
///   int32 AvatarID; float Orient; float Position[3]; int32 ActionFlag; int32 Room.
/// NOTE the field order differs from the 0x009D client->server form: there is no
/// RoomType slot here, Orient sits immediately after AvatarID, and the Room id is
/// appended last. The server fills the struct by direct assignment and memcpy's it
/// to the wire (Player::SendStarbaseAvatarChange: change.AvatarID = p->GameID();
/// change.Orient = p->m_Orient; change.Position = ...; change.Room = p->m_Room),
/// so every field is host-order LE. This is the single most common undecoded
/// server->client opcode in the capture corpus.
/// </summary>
public sealed class StarbaseAvatarChangeS2CRecord : PacketRecord
{
    public StarbaseAvatarChangeS2CRecord(ReadOnlySpan<byte> payload) : base(0x009E, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 28) { Flag(sb, $"STARBASE_AVATAR_CHANGE(S2C) truncated -- {Payload.Length} bytes, expected 28"); return; }
        FHex(sb, 0, "AvatarID", ReadI32LE(Payload, 0));
        FFloat(sb, 4, "Orient", ReadF32LE(Payload, 4));
        float px = ReadF32LE(Payload, 8), py = ReadF32LE(Payload, 12), pz = ReadF32LE(Payload, 16);
        FBytes(sb, 8, 12, "Position", $"({px:0.0##}, {py:0.0##}, {pz:0.0##})");
        int flag = ReadI32LE(Payload, 20);
        FHex(sb, 20, "ActionFlag", flag, StarbaseActionFlag.Note(flag));
        FDec(sb, 24, "Room", ReadI32LE(Payload, 24));
    }
}
