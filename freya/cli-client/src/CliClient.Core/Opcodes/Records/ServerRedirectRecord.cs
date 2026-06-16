// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

using System.Net;

/// <summary>
/// 0x0036 SERVER_REDIRECT. Wire: int32 SectorID; int32 IP (network-order); uint16 Port. = 10 bytes.
/// IP is in network byte order so BinaryPrimitives.WriteInt32BigEndian recovers the address bytes.
/// </summary>
public sealed class ServerRedirectRecord : PacketRecord
{
    public ServerRedirectRecord(ReadOnlySpan<byte> payload) : base(0x0036, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 10) { Flag(sb, $"SERVER_REDIRECT truncated -- {Payload.Length} bytes, expected 10"); return; }
        int sectorId = ReadI32LE(Payload, 0);
        int ipValue = ReadI32LE(Payload, 4);
        ushort port = ReadU16LE(Payload, 8);
        Span<byte> ipBytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ipBytes, ipValue);
        var ip = new IPAddress(ipBytes.ToArray());
        FHex(sb, 0, "SectorID", sectorId);
        FlagSuspicious(sb, "SectorID", sectorId);
        F(sb, 4, 4, "IP", ip.ToString());
        FDec(sb, 8, "Port", (short)port);
        if (port == 0) Flag(sb, "Port == 0 (likely uninitialised)");
    }
}
