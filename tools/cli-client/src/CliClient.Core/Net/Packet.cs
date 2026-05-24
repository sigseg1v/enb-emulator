// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

namespace N7.CliClient.Net;

/// <summary>
/// One complete EnB TCP frame: a <see cref="PacketHeader"/> plus its
/// payload. The frame is the unit of dispatch — every send/receive in
/// the codec is a single Packet, and every opcode handler operates on
/// the payload bytes.
/// </summary>
/// <param name="Header">4-byte size+opcode header.</param>
/// <param name="Payload">The opcode-specific bytes that follow the
/// header on the wire. May be empty.</param>
public sealed record Packet(PacketHeader Header, ReadOnlyMemory<byte> Payload)
{
    /// <summary>Build a packet from an opcode + payload (size is derived).</summary>
    public static Packet ForOpcode(ushort opcode, ReadOnlyMemory<byte> payload)
    {
        ushort total = checked((ushort)(PacketHeader.WireSize + payload.Length));
        return new Packet(new PacketHeader(total, opcode), payload);
    }

    /// <summary>
    /// Serialise the full frame (header + payload) into a fresh buffer.
    /// Used by the codec on the way out, just before RC4 encryption.
    /// </summary>
    public byte[] ToWireBytes()
    {
        byte[] wire = new byte[Header.Size];
        Header.Write(wire);
        Payload.Span.CopyTo(wire.AsSpan(PacketHeader.WireSize));
        return wire;
    }
}
