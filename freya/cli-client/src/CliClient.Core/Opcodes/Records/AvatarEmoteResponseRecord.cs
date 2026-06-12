// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x005F AVATAR_EMOTE_RESPONSE. Wire (ad-hoc buffer, PlayerConnection.cpp:10243):
///   int16 ChatSize (LE); uint8 Type (= 0x01); int32 GameID (LE);
///   uint8 Message[ChatSize].
/// Total = 7 + ChatSize bytes. GameID is written as a plain int32 (LE -- the
/// Phase-K Wave 11 fix casts through int32_t to keep it 4 bytes).
/// </summary>
public sealed class AvatarEmoteResponseRecord : PacketRecord
{
    public AvatarEmoteResponseRecord(ReadOnlySpan<byte> payload) : base(0x005F, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 7) { Flag(sb, $"AVATAR_EMOTE_RESPONSE truncated -- {Payload.Length} bytes, expected >= 7"); return; }
        int chatSize = ReadU16LE(Payload, 0);
        FDec(sb, 0, "ChatSize", (short)chatSize);
        FDec(sb, 2, "Type", Payload[2], Payload[2] == 0x01 ? "(emote)" : null);
        FHex(sb, 3, "GameID", ReadI32LE(Payload, 3));
        if (7 + chatSize > Payload.Length)
        {
            Flag(sb, $"AVATAR_EMOTE_RESPONSE: Message needs {chatSize} bytes at offset 7, only {Payload.Length - 7} remain");
            return;
        }
        string msg = Encoding.Latin1.GetString(Payload, 7, chatSize);
        FStr(sb, 7, chatSize, "Message", msg);
    }
}
