// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x006A CLIENT_SOUND. Server cues a sound file on the client. Variable, per
/// PlayerConnection.cpp:945-969:
///   int32  Length     @0    (strlen(SoundName) + 1, i.e. includes the null)
///   char   SoundName  @4    (Length-1 chars, then a null byte)
///   int32  Channel    @..
///   uint8  Queue      @..
/// </summary>
public sealed class ClientSoundRecord : PacketRecord
{
    public ClientSoundRecord(ReadOnlySpan<byte> payload) : base(0x006A, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"CLIENT_SOUND truncated -- {Payload.Length} bytes"); return; }
        int len = ReadI32LE(Payload, 0);
        FDec(sb, 0, "Length", len);

        if (len < 1 || 4 + len > Payload.Length) { Flag(sb, $"CLIENT_SOUND Length {len} overruns payload ({Payload.Length} bytes)"); return; }
        int nameLen = len - 1; // last byte is the null
        string name = Encoding.Latin1.GetString(Payload, 4, nameLen);
        FStr(sb, 4, nameLen, "SoundName", name, required: false);
        Mark(4 + nameLen, 1); // null terminator

        int off = 4 + len;
        if (off + 5 > Payload.Length) { Flag(sb, $"CLIENT_SOUND missing trailing Channel/Queue -- {Payload.Length - off} bytes left"); return; }
        FDec(sb, off,     "Channel", ReadI32LE(Payload, off));
        FDec(sb, off + 4, "Queue",   Payload[off + 4]);
    }
}
