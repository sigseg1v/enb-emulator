// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0064 CLIENT_DAMAGE. Server reports a damage event to the client. 24 bytes,
/// per PlayerConnection.cpp:4563-4576:
///   float  Damage     @0
///   float  Modifier   @4
///   int32  Type       @8
///   int32  Inflicted  @12
///   int32  SourceId   @16
///   int32  TargetId   @20
/// </summary>
public sealed class ClientDamageRecord : PacketRecord
{
    public ClientDamageRecord(ReadOnlySpan<byte> payload) : base(0x0064, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 24) { Flag(sb, $"CLIENT_DAMAGE truncated -- {Payload.Length} bytes, expected 24"); return; }
        FFloat(sb, 0, "Damage",    ReadF32LE(Payload, 0));
        FFloat(sb, 4, "Modifier",  ReadF32LE(Payload, 4));
        FDec(sb,   8, "Type",      ReadI32LE(Payload, 8));
        FDec(sb,  12, "Inflicted", ReadI32LE(Payload, 12));
        FHex(sb,  16, "SourceId",  ReadI32LE(Payload, 16));
        FHex(sb,  20, "TargetId",  ReadI32LE(Payload, 20));
    }
}
