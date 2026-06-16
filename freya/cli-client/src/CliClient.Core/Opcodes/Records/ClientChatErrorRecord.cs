// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x00A6 CLIENT_CHAT_ERROR. Wire (PlayerConnection.cpp:4674 SendClientChatError):
///   int32 Reason (LE); int32 Type (LE);
///   AddDataLS Player; AddDataLS Channel; AddDataLS Other
///   (each = uint16 len LE + len raw bytes, no NUL).
/// </summary>
public sealed class ClientChatErrorRecord : PacketRecord
{
    public ClientChatErrorRecord(ReadOnlySpan<byte> payload) : base(0x00A6, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"CLIENT_CHAT_ERROR truncated -- {Payload.Length} bytes, expected >= 8"); return; }
        FDec(sb, 0, "Reason", ReadI32LE(Payload, 0));
        FDec(sb, 4, "Type", ReadI32LE(Payload, 4));
        int off = 8;
        if (!TryReadAddDataLS(sb, ref off, "Player")) return;
        if (!TryReadAddDataLS(sb, ref off, "Channel")) return;
        if (!TryReadAddDataLS(sb, ref off, "Other")) return;
    }
}
