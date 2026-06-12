// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0034 CLIENT_SET_TIME. Wire (struct ClientSetTime, 12 bytes LE):
///   int32 ClientSent; int32 ServerReceived; int32 ServerSent.
///
/// A round-trip time sync: ClientSent is the client's tick echoed back,
/// ServerReceived/ServerSent are the server's tick when it received the request
/// and when it replied. The only invariant is ServerSent &gt;= ServerReceived
/// (server replies no earlier than it received); the difference is the server's
/// processing latency for the packet. Retail sends ServerSent = ServerReceived
/// + 1 tick; our server (PlayerConnection.cpp SendClientSetTime) sets them equal
/// (zero measured latency). Both are well-formed -- only ServerSent &lt;
/// ServerReceived (the server clock running backwards) is an anomaly.
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
        int processTicks   = serverSent - serverReceived;
        FHex(sb, 0, "ClientSent",     clientSent);
        FHex(sb, 4, "ServerReceived", serverReceived);
        FHex(sb, 8, "ServerSent",     serverSent,
             processTicks >= 0 ? $"(+{processTicks} tick server latency)" : null);
        if (processTicks < 0)
            Flag(sb, $"ServerSent < ServerReceived ({serverSent} < {serverReceived}) -- server clock ran backwards");
    }
}
