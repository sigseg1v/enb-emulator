// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x009D STARBASE_AVATAR_CHANGE (client->server). Wire (struct
/// StarbaseAvatarChange, 28 bytes, LE, no byte-swap):
///   int32 AvatarID; int32 RoomType; float Orient; float Position[3]; int32 ActionFlag.
/// The walking-avatar position feed inside a starbase. The server reads every field
/// raw host-order in Player::HandleStarbaseAvatarChange (change->AvatarID,
/// change->Position, change->Orient, change->ActionFlag -- no ntohl), so the wire
/// is LE, and branches on ActionFlag == 0x41 ("send avatar to everyone").
/// Source: struct StarbaseAvatarChange (PacketStructures.h),
/// Player::HandleStarbaseAvatarChange (PlayerClass.cpp). Pinned to capture_3.rar
/// Packet 553 (Client->Server) -- ActionFlag 0x41, the entering-room broadcast.
/// </summary>
public sealed class StarbaseAvatarChangeRecord : PacketRecord
{
    public StarbaseAvatarChangeRecord(ReadOnlySpan<byte> payload) : base(0x009D, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 28) { Flag(sb, $"STARBASE_AVATAR_CHANGE truncated -- {Payload.Length} bytes, expected 28"); return; }
        FHex(sb, 0, "AvatarID", ReadI32LE(Payload, 0));
        FDec(sb, 4, "RoomType", ReadI32LE(Payload, 4));
        FFloat(sb, 8, "Orient", ReadF32LE(Payload, 8));
        float px = ReadF32LE(Payload, 12), py = ReadF32LE(Payload, 16), pz = ReadF32LE(Payload, 20);
        FBytes(sb, 12, 12, "Position", $"({px:0.0##}, {py:0.0##}, {pz:0.0##})");
        int flag = ReadI32LE(Payload, 24);
        FHex(sb, 24, "ActionFlag", flag, StarbaseActionFlag.Note(flag));
    }
}

/// <summary>
/// ActionFlag enum shared by 0x009D / 0x009E. Values from the branch table in
/// Player::HandleStarbaseAvatarChange (PlayerClass.cpp).
/// </summary>
internal static class StarbaseActionFlag
{
    public static string? Note(int flag) => flag switch
    {
        0x41 => "(broadcast -- send avatar to everyone)",
        0x11 => "(recustomise terminal enter)",
        0x01 => "(recustomise cancel)",
        _    => null,
    };
}
