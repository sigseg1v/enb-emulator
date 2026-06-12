// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// CLIENT_CHAT_LIST (0x00A4, server-&gt;client) -- a chat-related name list: the
/// friends list, the ignore list, or a chat-channel membership/active/current
/// list. Variable length, ALL LITTLE-ENDIAN. Wire (Player::SendClientChatList,
/// PlayerConnection.cpp:4645):
/// <code>
///   int32  ListType                     (AddData&lt;long&gt; -- raw 4-byte LE)
///   { int16 len; char[len] }  Channel   (AddDataLS -- LE len prefix, no NUL; empty unless MEMBERS_CHANNEL)
///   int32  NameCount  (number1)         (AddData&lt;long&gt;)
///   NameCount   x { int16 len; char[len] }   names   (AddDataLS each)
///   int32  SectorCount (number2)        (AddData&lt;long&gt;)
///   SectorCount x { int16 len; char[len] }   sectors (AddDataLS each)
/// </code>
/// Byte order is proven by the emitter helpers (server/src/PacketMethods.h):
/// AddData&lt;long&gt; stores a raw int32 with no byte flip (host-order LE on x86),
/// and AddDataLS writes the length as short(strlen) with AddData (LE) followed by
/// the raw string bytes with NO null terminator. ListType is one of CHAT_LIST_*
/// (PacketStructures.h:683): 0 FRIENDS, 1 IGNORES, 2 MEMBERS_CHANNEL,
/// 3 ACTIVE_CHANNELS, 4 CURRENT_CHANNELS. For a FRIENDS list (Player::ListFriends,
/// PlayerMisc.cpp:1442) the two lists are paired: names[i] is a friend, sectors[i]
/// is that friend's current sector name or "offline". All six captured frames are
/// FRIENDS lists with an empty channel; each ends with the last sector string
/// carrying a trailing '*' inside its own length prefix (a retail data artifact),
/// which is rendered verbatim as part of the string value rather than stripped.
/// Pinned to capture_3.rar.
/// </summary>
public sealed class ClientChatListRecord : PacketRecord
{
    // Defensive sanity cap on list counts so a corrupt length can't make the
    // decoder loop on garbage. MAX_FRIEND_LIST / MAX_IGNORE_LIST are far below
    // this; the cap only ever trips on a malformed frame.
    private const int MaxListEntries = 4096;

    public ClientChatListRecord(ReadOnlySpan<byte> payload) : base(0x00A4, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        // ListType(4) + channel-len(2): the smallest readable prefix.
        if (Payload.Length < 6)
        {
            Flag(sb, $"CLIENT_CHAT_LIST truncated -- {Payload.Length} bytes, need >= 6");
            return;
        }

        int off = 0;
        int listType = ReadI32LE(Payload, off);
        FDec(sb, off, "ListType", listType, $"(LE; {ListTypeName(listType)})");
        off += 4;

        // Channel: length-prefixed string, populated only for MEMBERS_CHANNEL.
        if (!TryString(sb, ref off, "Channel")) return;

        if (off + 4 > Payload.Length) { Flag(sb, "truncated before NameCount"); return; }
        int nameCount = ReadI32LE(Payload, off);
        FDec(sb, off, "NameCount", nameCount, "(LE; number1)");
        off += 4;
        if (nameCount < 0 || nameCount > MaxListEntries)
        {
            Flag(sb, $"NameCount {nameCount} out of plausible range [0,{MaxListEntries}]");
            return;
        }
        for (int i = 0; i < nameCount; i++)
            if (!TryString(sb, ref off, $"Name[{i}]")) return;

        if (off + 4 > Payload.Length) { Flag(sb, "truncated before SectorCount"); return; }
        int sectorCount = ReadI32LE(Payload, off);
        FDec(sb, off, "SectorCount", sectorCount, "(LE; number2)");
        off += 4;
        if (sectorCount < 0 || sectorCount > MaxListEntries)
        {
            Flag(sb, $"SectorCount {sectorCount} out of plausible range [0,{MaxListEntries}]");
            return;
        }
        for (int i = 0; i < sectorCount; i++)
            if (!TryString(sb, ref off, $"Sector[{i}]")) return;

        if (off < Payload.Length)
            Flag(sb, $"CLIENT_CHAT_LIST has {Payload.Length - off} trailing bytes");
    }

    /// <summary>
    /// Decodes one AddDataLS field at <paramref name="off"/> (int16 LE length
    /// prefix, then that many raw bytes, no NUL), emits one annotated line
    /// covering both the prefix and the string, and advances <paramref name="off"/>.
    /// Returns false (after flagging) if the field would run past the buffer.
    /// </summary>
    private bool TryString(StringBuilder sb, ref int off, string name)
    {
        if (off + 2 > Payload.Length)
        {
            Flag(sb, $"{name}: truncated -- no length prefix at [{off:X4}]");
            return false;
        }
        int len = ReadU16LE(Payload, off);
        if (off + 2 + len > Payload.Length)
        {
            Flag(sb, $"{name}: length {len} at [{off:X4}] runs past end of buffer");
            return false;
        }
        string value = Encoding.ASCII.GetString(Payload, off + 2, len);
        F(sb, off, 2 + len, name, Quote(value), $"(len {len})");
        off += 2 + len;
        return true;
    }

    private static string ListTypeName(int t) => t switch
    {
        0 => "CHAT_LIST_FRIENDS",
        1 => "CHAT_LIST_IGNORES",
        2 => "CHAT_LIST_MEMBERS_CHANNEL",
        3 => "CHAT_LIST_ACTIVE_CHANNELS",
        4 => "CHAT_LIST_CURRENT_CHANNELS",
        _ => "unknown list type",
    };
}
