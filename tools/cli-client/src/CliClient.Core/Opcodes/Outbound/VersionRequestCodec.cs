// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Buffers.Binary;

namespace N7.CliClient.Opcodes.Outbound;

/// <summary>
/// 0x0000 VERSION_REQUEST — the first opcode a client sends on the
/// global server connection after the RSA+RC4 handshake. Two big-endian
/// int32 fields: protocol major + minor. The server compares against
/// its own (currently hardcoded major=42, minor=0) and replies with a
/// <see cref="Inbound.VersionResponse"/> carrying a 3-way status code.
/// </summary>
/// <remarks>
/// Mirrors <c>struct VersionRequest</c> in
/// <c>common/include/net7/PacketStructures.h:35-41</c>. The header
/// declares <c>int32_t</c> explicitly (not <c>long</c>) so the wire
/// size is 8 bytes on every platform. Big-endian: the server uses
/// <c>ntohl</c> to read them (see
/// <c>proxy/ClientToServer_linux_stubs.cpp:47-48</c>).
/// </remarks>
public sealed record VersionRequest(int Major, int Minor);

/// <summary>Codec for opcode 0x0000 VERSION_REQUEST (client → global).</summary>
public sealed class VersionRequestCodec : IOpcodeCodec
{
    public const int WireSize = 8;

    public OpcodeId Opcode => OpcodeId.Known.VersionRequest;

    public object DecodeInbound(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < WireSize)
            throw new InvalidDataException(
                $"VersionRequest payload is {payload.Length} bytes, expected {WireSize}");

        int major = BinaryPrimitives.ReadInt32BigEndian(payload[0..4]);
        int minor = BinaryPrimitives.ReadInt32BigEndian(payload[4..8]);
        return new VersionRequest(major, minor);
    }

    public byte[] EncodeOutbound(object message)
    {
        if (message is not VersionRequest req)
            throw new ArgumentException(
                $"expected VersionRequest, got {message?.GetType().Name ?? "null"}",
                nameof(message));

        byte[] buf = new byte[WireSize];
        BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(0, 4), req.Major);
        BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(4, 4), req.Minor);
        return buf;
    }
}
