// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x00A3 CLIENT_CHAT_REQUEST (client->server). Variable-length, LITTLE-ENDIAN.
/// Wire (struct ClientChatRequest): int32 PlayerID; int32 type; then three
/// u16-length-prefixed byte strings (String1/String2/String3, each a u16 length
/// followed by that many ASCII bytes, NO NUL on the wire); then int32 DataSize
/// and an optional DataSize-byte trailing block.
/// Every field is little-endian: Player::HandleClientChatRequest walks the
/// buffer with plain `*((short*)p)` / `*((int32_t*)p)` reads and no ntohl. The
/// three strings carry different things per request type (channel name, target
/// nick, message text); the handler picks which by `type`, so they are reported
/// positionally here. `type` is one of the CCE_*/CCR_* request codes.
/// </summary>
public sealed class ClientChatRequestRecord : PacketRecord
{
    public ClientChatRequestRecord(ReadOnlySpan<byte> payload) : base(0x00A3, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 10) { Flag(sb, $"CLIENT_CHAT_REQUEST truncated -- {Payload.Length} bytes, expected at least 10 (PlayerID + type + Len1)"); return; }

        FHex(sb, 0, "PlayerID", ReadI32LE(Payload, 0), "(LE)");
        int type = ReadI32LE(Payload, 4);
        FDec(sb, 4, "Type", type, TypeNote(type));

        int o = 8;
        for (int i = 1; i <= 3; i++)
        {
            if (Payload.Length < o + 2) { Flag(sb, $"truncated reading String{i} length at offset {o}"); return; }
            short len = ReadI16LE(Payload, o);
            FDec(sb, o, $"Len{i}", len);
            o += 2;
            if (len < 0) { Flag(sb, $"String{i} length {len} is negative"); return; }
            if (len > 0)
            {
                if (Payload.Length < o + len) { Flag(sb, $"truncated reading String{i} body -- need {len} bytes at offset {o}, have {Payload.Length - o}"); return; }
                FStr(sb, o, len, $"String{i}", Encoding.ASCII.GetString(Payload, o, len));
                o += len;
            }
        }

        if (Payload.Length < o + 4) { Flag(sb, $"truncated reading DataSize at offset {o}"); return; }
        int dataSize = ReadI32LE(Payload, o);
        FDec(sb, o, "DataSize", dataSize, "(trailing optional block length)");
        o += 4;

        if (dataSize > 0 && Payload.Length >= o + dataSize)
            FBytes(sb, o, dataSize, "Data", $"{dataSize} bytes");
    }

    private static string TypeNote(int type) => type switch
    {
        0  => "(CCE_SPEAK_ON -- channel message)",
        1  => "(CCE_SPEAK_LOCALLY)",
        2  => "(CCE_BROADCAST_TO)",
        3  => "(CCE_BROADCAST_ALL)",
        4  => "(CCE_INSERT_CHANNEL)",
        5  => "(CCE_REMOVE_CHANNEL)",
        6  => "(CCE_ENTER_CHANNEL)",
        7  => "(CCE_EXIT_CHANNEL)",
        8  => "(CCE_ADD_FRIEND)",
        9  => "(CCE_REMOVE_FRIEND)",
        10 => "(CCE_IGNORE)",
        11 => "(CCE_UNIGNORE)",
        12 => "(CCE_INVITE1)",
        13 => "(CCE_INVITE2)",
        14 => "(CCE_BAN)",
        15 => "(CCE_UNBAN)",
        16 => "(CCE_GAG)",
        17 => "(CCE_UNGAG)",
        18 => "(CCE_ADD_OWNER)",
        19 => "(CCE_REMOVE_OWNER)",
        20 => "(CCE_KICK)",
        21 => "(CCE_SET_ACCESS)",
        22 => "(CCE_SET_PASSWORD)",
        23 => "(CCR_LIST_IGNORES)",
        24 => "(CCR_LIST_FRIENDS)",
        25 => "(CCR_LIST_CHANNELS)",
        26 => "(CCR_LIST_ALL_CHANNELS)",
        28 => "(CCR_FRIEND_STATUS_ONLY)",
        29 => "(CCR_ANYONE_STATUS)",
        30 => "(CCR_SECTOR_LOGIN)",
        31 => "(CCE_GMGAG)",
        32 => "(CCE_GMUNGAG)",
        _  => "(unknown request type)",
    };
}
