// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;

namespace N7.CliClient.Net;

/// <summary>
/// The 4-byte EnB TCP frame header. Mirrors <c>struct EnbTcpHeader</c> in
/// <c>common/include/net7/PacketStructures.h</c>:
/// <code>
/// struct EnbTcpHeader { short size; short opcode; } ATTRIB_PACKED;
/// </code>
/// <para>
/// <b>Size semantics</b>: the wire <c>size</c> field is the TOTAL frame
/// length, header included. So payload length = size - 4.
/// </para>
/// <para>
/// <b>Endianness</b>: the wire format is little-endian (Win32 / x86), and
/// the entire frame (header + payload) is encrypted as one RC4 stream
/// once the handshake completes.
/// </para>
/// </summary>
public readonly record struct PacketHeader(ushort Size, ushort Opcode)
{
    public const int WireSize = 4;

    public int PayloadLength => Size - WireSize;

    /// <summary>
    /// Decode a 4-byte little-endian header from <paramref name="source"/>.
    /// </summary>
    public static PacketHeader Read(ReadOnlySpan<byte> source)
    {
        if (source.Length < WireSize)
        {
            throw new ArgumentException(
                $"need {WireSize} bytes for header, got {source.Length}",
                nameof(source));
        }
        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(source);
        ushort opcode = BinaryPrimitives.ReadUInt16LittleEndian(source[2..]);
        return new PacketHeader(size, opcode);
    }

    /// <summary>
    /// Encode this header into the first 4 bytes of <paramref name="destination"/>.
    /// </summary>
    public void Write(Span<byte> destination)
    {
        if (destination.Length < WireSize)
        {
            throw new ArgumentException(
                $"need {WireSize} bytes for header, got {destination.Length}",
                nameof(destination));
        }
        BinaryPrimitives.WriteUInt16LittleEndian(destination, Size);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], Opcode);
    }
}
