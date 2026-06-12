// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x003C CLIENT_TYPE. Wire (4 bytes):
///   int32 ClientType  (sector_type field from server).
/// </summary>
public sealed class ClientTypeRecord : PacketRecord
{
    public ClientTypeRecord(ReadOnlySpan<byte> payload) : base(0x003C, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"CLIENT_TYPE truncated -- {Payload.Length} bytes, expected 4"); return; }
        int clientType = ReadI32LE(Payload, 0);
        FHex(sb, 0, "ClientType", clientType);
    }
}
