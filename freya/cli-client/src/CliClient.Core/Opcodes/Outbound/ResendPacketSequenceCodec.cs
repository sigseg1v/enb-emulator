// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;

namespace N7.CliClient.Opcodes.Outbound;

/// <summary>
/// 0x2017 RESEND_PACKET_SEQUENCE -- NACK from the client side of the reliable
/// sector stream, asking the server to resend <see cref="PacketCount"/>
/// reliable 0x2016 datagrams starting at sequence <see cref="PacketStart"/>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>struct ReSendRequest</c> in
/// <c>common/include/net7/PacketStructures.h</c>. Canonical wire layout is
/// what the Win32 proxy build has always emitted (two 4-byte
/// little-endian <c>long</c>s):
/// </para>
/// <list type="bullet">
///   <item>offset 0..3 : uint32 LE PacketStart (first missing sequence number)</item>
///   <item>offset 4..7 : uint32 LE PacketCount (how many consecutive packets)</item>
/// </list>
/// <para>
/// A legacy LP64 proxy build widened both fields to 8 bytes (16-byte
/// payload, values in the low dwords); <see cref="DecodeInbound"/> accepts
/// that form too, the same way <c>Player::ReSendOpcodes</c>
/// (server/src/PlayerConnection.cpp) does. The server answers each requested
/// sequence number with a 0x2016 datagram -- the cached packet if it is still
/// in the resend ring, or a header-only blank if it has been evicted, so the
/// requester can stop waiting and advance. The CLI's own reassembler
/// (<see cref="N7.CliClient.Net.SectorStreamReassembler"/>) is
/// sequence-agnostic and never needs to send this; the record exists to pin
/// the wire format.
/// </para>
/// </remarks>
public sealed record ResendPacketSequenceRequest(
    uint PacketStart,
    uint PacketCount);

/// <summary>
/// Codec for opcode 0x2017 RESEND_PACKET_SEQUENCE (client → server).
/// </summary>
public sealed class ResendPacketSequenceCodec : IOpcodeCodec
{
    /// <summary>Canonical payload size: two uint32 fields.</summary>
    public const int WireSize = 8;

    /// <summary>Legacy LP64-build payload size: two uint64 fields.</summary>
    public const int LegacyLp64WireSize = 16;

    public OpcodeId Opcode => OpcodeId.Known.ResendPacketSequence;

    public object DecodeInbound(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < WireSize)
        {
            throw new InvalidDataException(
                $"ResendPacketSequence payload is {payload.Length} bytes, expected at least {WireSize}");
        }

        uint start = BinaryPrimitives.ReadUInt32LittleEndian(payload[0..4]);

        // Legacy LP64 form: {u64 start, u64 count}. Detected the same way the
        // server detects it -- a 16-byte payload whose bytes 4..7 (the high
        // dword of a 64-bit start) are zero.
        uint count = payload.Length >= LegacyLp64WireSize
                     && BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]) == 0
            ? BinaryPrimitives.ReadUInt32LittleEndian(payload[8..12])
            : BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]);

        return new ResendPacketSequenceRequest(start, count);
    }

    public byte[] EncodeOutbound(object message)
    {
        if (message is not ResendPacketSequenceRequest req)
            throw new ArgumentException(
                $"expected ResendPacketSequenceRequest, got {message?.GetType().Name ?? "null"}",
                nameof(message));

        byte[] buf = new byte[WireSize];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), req.PacketStart);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), req.PacketCount);
        return buf;
    }
}
