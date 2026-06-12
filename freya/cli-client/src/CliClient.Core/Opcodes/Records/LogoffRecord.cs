// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0003 LOGOFF. Wire: bare int32 GameID (LE). = 4 bytes.
/// Emitter PlayerConnection.cpp:7990 (Phase-K int32 cast keeps it 4 bytes).
/// </summary>
public sealed class LogoffRecord : PacketRecord
{
    public LogoffRecord(ReadOnlySpan<byte> payload) : base(0x0003, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"LOGOFF truncated -- {Payload.Length} bytes, expected 4"); return; }
        FHex(sb, 0, "GameID", ReadI32LE(Payload, 0));
    }
}
