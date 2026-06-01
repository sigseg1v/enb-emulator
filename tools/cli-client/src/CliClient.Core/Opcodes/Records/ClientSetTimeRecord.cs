// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0034 CLIENT_SET_TIME. Wire (struct ClientSetTime, 12 bytes LE):
///   int32 ClientSent; int32 ServerReceived; int32 ServerSent.
/// </summary>
public sealed class ClientSetTimeRecord : PacketRecord
{
    public ClientSetTimeRecord(ReadOnlySpan<byte> payload) : base(0x0034, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 12) { Flag(sb, $"CLIENT_SET_TIME truncated -- {Payload.Length} bytes, expected 12"); return; }
        int clientSent     = ReadI32LE(Payload, 0);
        int serverReceived = ReadI32LE(Payload, 4);
        int serverSent     = ReadI32LE(Payload, 8);
        FHex(sb, 0, "ClientSent",     clientSent);
        FHex(sb, 4, "ServerReceived", serverReceived);
        FHex(sb, 8, "ServerSent",     serverSent);
        if (serverReceived != serverSent)
            Flag(sb, $"ServerReceived != ServerSent ({serverReceived} vs {serverSent})");
    }
}
