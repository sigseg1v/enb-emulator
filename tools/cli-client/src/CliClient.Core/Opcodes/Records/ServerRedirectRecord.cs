// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Net;
using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0036 SERVER_REDIRECT -- master tells the client which sector
/// server to connect to next. Wire shape mirrors
/// <c>N7.CliClient.Opcodes.Inbound.ServerRedirectCodec</c>.
/// 10 bytes: int32 sector_id, int32 ip (LE storage of inet_addr's
/// pre-swap value), short port (HOST order).
/// </summary>
public sealed class ServerRedirectRecord : PacketRecord
{
    public ServerRedirectRecord(ReadOnlySpan<byte> payload) : base(0x0036, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 10)
        {
            Flag(sb, $"SERVER_REDIRECT truncated -- {Payload.Length} bytes, expected 10");
            return;
        }

        int sectorId = ReadI32LE(Payload, 0);
        int ipValue  = ReadI32LE(Payload, 4);
        ushort port  = ReadU16LE(Payload, 8);

        Span<byte> ipBytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ipBytes, ipValue);
        var ip = new IPAddress(ipBytes.ToArray());

        FieldHex(sb, "SectorID", sectorId);
        FlagSuspicious(sb, "SectorID", sectorId);
        Field(sb, "IP", ip.ToString());
        FieldDec(sb, "Port", port);
        if (port == 0)
            Flag(sb, "Port == 0 (likely uninitialised)");
    }
}
